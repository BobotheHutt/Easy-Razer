using System.Diagnostics;
using System.Reflection;
using EasyRazer.Config;
using EasyRazer.Core;
using EasyRazer.Devices;
using EasyRazer.Devices.Razer;
using EasyRazer.UI.Modern;

namespace EasyRazer.UI;

/// <summary>
/// The main window: sidebar navigation with Profiles, Lighting, Devices and Settings pages.
/// Edits a copy of the config; nothing changes on the devices until you press Save.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly string[] PresetColors =
    {
        "#44D62C", "#00E5FF", "#2979FF", "#7C4DFF", "#E040FB", "#FF4081",
        "#FF1744", "#FF6D00", "#FFC400", "#FFFFFF", "#76FF03", "#000000",
    };

    private readonly ProfileEngine _engine;
    private readonly Updater _updater;
    private AppConfig _cfg;
    private bool _dirty;

    // Shell
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Window, Padding = new Padding(28, 22, 28, 22) };
    private readonly List<(NavItem Nav, Control Page)> _pages = new();
    private readonly ModernButton _save = new() { Text = "Save changes", Icon = Ui.IconSave, Kind = ButtonKind.Primary, AutoSize = false, Height = 40, Dock = DockStyle.Bottom };
    private readonly Label _saveHint = new() { Dock = DockStyle.Bottom, Height = 28, ForeColor = Ui.TextFaint, Font = Ui.Font(9f), TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _activeProfile = new() { Dock = DockStyle.Top, Height = 22, ForeColor = Ui.TextFaint, Font = Ui.Font(9f), Padding = new Padding(22, 0, 0, 0), UseMnemonic = false };

    // Profiles page
    private readonly ModernList _profiles = new() { Dock = DockStyle.Fill };
    private readonly Label _profileName = new() { AutoSize = true, Font = Ui.Font(20f, semibold: true), ForeColor = Ui.Text, UseMnemonic = false };
    private readonly Label _profileHint = new() { AutoSize = true, Font = Ui.Font(10f), ForeColor = Ui.TextDim, UseMnemonic = false };
    private readonly ModernButton _rename = new() { Text = "Rename", Icon = Ui.IconRename, Kind = ButtonKind.Ghost };
    private readonly ModernButton _copy = new() { Text = "Duplicate", Icon = Ui.IconCopy, Kind = ButtonKind.Ghost };
    private readonly ModernButton _delete = new() { Text = "Delete", Icon = Ui.IconDelete, Kind = ButtonKind.Danger };
    private readonly FlowLayoutPanel _chips = new() { Dock = DockStyle.Fill, BackColor = Ui.Card, WrapContents = true, AutoScroll = true };
    private readonly SegmentedControl _activation = new();
    private readonly Panel _activationBox = new() { Dock = DockStyle.Right, Width = 330, BackColor = Ui.Card };
    private readonly SegmentedControl _deviceKind = new();
    private readonly DeviceLayoutView _layout = new() { Dock = DockStyle.Fill };
    private readonly BindingPicker _picker = new();

    // Lighting page
    private readonly ColorSwatch _bigSwatch = new() { Size = new Size(112, 112), Big = true };
    private readonly InputBox _hex = new() { Width = 130 };
    private readonly List<ColorSwatch> _presets = new();
    private readonly ToggleSwitch _lightingOn = new() { Text = "Set the color on my Razer devices" };
    private readonly Label _lightingStatus = new() { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Ui.TextDim, Font = Ui.Font(9.5f), UseMnemonic = false };
    private readonly ToggleSwitch _useOpenRgb = new() { Text = "Also use OpenRGB for other devices" };
    private readonly InputBox _openRgbPath = new() { Width = 460 };
    private readonly ModernButton _openRgbBrowse = new() { Text = "Browse…" };

    // Devices page
    private readonly FlowLayoutPanel _deviceCards = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Ui.Window };
    private readonly Dictionary<string, (StatusDot Dot, ToggleSwitch? Write)> _deviceRows = new();

    // Settings page
    private readonly ToggleSwitch _autostart = new() { Text = "Start Easy Razer when I sign in to Windows" };
    private readonly ToggleSwitch _autoUpdate = new() { Text = "Install updates automatically" };
    private readonly Label _updateStatus = new() { AutoSize = true, ForeColor = Ui.TextDim, Font = Ui.Font(9.5f), UseMnemonic = false, Margin = new Padding(52, 0, 0, 10) };

    private readonly KeyRecorder _recorder = new();

    public event Action? Saved;

    public bool HasUnsavedChanges => _dirty;

    public SettingsForm(ProfileEngine engine, Updater updater)
    {
        _engine = engine;
        _updater = updater;
        _cfg = ConfigStore.DeepCopy(engine.Config);

        Text = "Easy Razer";
        Size = new Size(1340, 900);
        MinimumSize = new Size(1180, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Window;
        ForeColor = Ui.Text;
        Font = Ui.Font(10f);
        Ui.DarkChrome(this, Ui.Sidebar);

        Controls.Add(_content);
        Controls.Add(BuildSidebar());

        AddPage(Ui.IconProfiles, "Profiles", BuildProfilesPage());
        AddPage(Ui.IconLighting, "Lighting", BuildLightingPage());
        AddPage(Ui.IconDevices, "Devices", BuildDevicesPage());
        AddPage(Ui.IconSettings, "Settings", BuildSettingsPage());
        // EASYRAZER_PAGE=n opens a given page (used for screenshots).
        ShowPage(int.TryParse(Environment.GetEnvironmentVariable("EASYRAZER_PAGE"), out var startPage) ? Math.Clamp(startPage, 0, 3) : 0);

        FormClosing += OnClosing;
        _engine.DevicesChanged += OnDevicesChanged;
        FormClosed += (_, _) =>
        {
            _engine.DevicesChanged -= OnDevicesChanged;
            _recorder.Dispose();
        };
        // Never keep listening to the keyboard once you leave this window.
        Deactivate += (_, _) => _recorder.Cancel();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; _ = SaveAsync(); }
        };
        LoadFromConfig();
    }

    // ================================================================== shell

    private FlowLayoutPanel _nav = null!;

    private Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = Ui.Sidebar, Padding = new Padding(14, 18, 14, 16) };

        var brand = new BrandHeader { Dock = DockStyle.Top, Height = 46 };
        _nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Sidebar, Padding = new Padding(0, 14, 0, 0) };

        _save.Click += async (_, _) => await SaveAsync();
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Ui.Sidebar };
        bottom.Controls.Add(_saveHint);
        bottom.Controls.Add(_save);

        sidebar.Controls.Add(_nav);
        sidebar.Controls.Add(_activeProfile);
        sidebar.Controls.Add(brand);
        sidebar.Controls.Add(bottom);
        return sidebar;
    }

    private void AddPage(string icon, string title, Control page)
    {
        var nav = new NavItem(icon, title) { Dock = DockStyle.None, Width = 204, Margin = new Padding(-10, 0, 0, 2) };
        var index = _pages.Count;
        nav.Click += (_, _) => ShowPage(index);
        _nav.Controls.Add(nav);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _content.Controls.Add(page);
        _pages.Add((nav, page));
    }

    private void ShowPage(int index)
    {
        _recorder.Cancel();
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Nav.Selected = i == index;
            _pages[i].Page.Visible = i == index;
        }
    }

    private static Control PageHeader(string title, string subtitle)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Ui.Window };
        p.Controls.Add(new TextLabel(subtitle, 10f, color: Ui.TextDim) { Location = new Point(0, 40) });
        p.Controls.Add(new TextLabel(title, 20f, semibold: true) { Location = new Point(-2, 0) });
        return p;
    }

    // ================================================================== Profiles page

    private Control BuildProfilesPage()
    {
        // Left: profile list.
        _profiles.Describe = o =>
        {
            var p = (Profile)o;
            var sub = p.IsDefault ? "Desktop & unlinked games"
                : p.Games.Count == 0 ? "No games linked yet"
                : p.Games.Count == 1 ? p.Games[0] : $"{p.Games.Count} games";
            return (p.Name, sub);
        };
        _profiles.SelectedIndexChanged += (_, _) => { _recorder.Cancel(); ShowProfile(); };
        var newProfile = new ModernButton { Text = "New profile", Icon = Ui.IconAdd, Kind = ButtonKind.Primary, AutoSize = false, Height = 38, Dock = DockStyle.Bottom };
        newProfile.Click += (_, _) => AddProfile(null);
        var listCard = new Card { Title = "Profiles", Dock = DockStyle.Fill, Padding = new Padding(10, 48, 10, 12), Margin = new Padding(0, 0, 16, 0) };
        listCard.Controls.Add(_profiles);
        listCard.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Ui.Card });
        listCard.Controls.Add(newProfile);

        // Right: header.
        _rename.Click += (_, _) => RenameProfile();
        _copy.Click += (_, _) => { if (SelectedProfile is { } p) AddProfile(p); };
        _delete.Click += (_, _) => DeleteProfile();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Ui.Window, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.AddRange(new Control[] { _rename, _copy, _delete });
        var titles = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Window };
        _profileName.Location = new Point(-2, 0);
        _profileHint.Location = new Point(0, 42);
        titles.Controls.Add(_profileName);
        titles.Controls.Add(_profileHint);
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Window };
        header.Controls.Add(titles);
        header.Controls.Add(actions);

        // Right: linked games + when active.
        _activation.SetItems("While the game is in front", "While it's running");
        _activation.SelectedIndexChanged += (_, _) =>
        {
            if (SelectedProfile is { IsDefault: false } p && p.Activation != (ActivationMode)_activation.SelectedIndex)
            {
                p.Activation = (ActivationMode)_activation.SelectedIndex;
                MarkDirty();
            }
        };
        _activationBox.Controls.Add(_activation);
        _activationBox.Controls.Add(new TextLabel("ACTIVE", 8f, semibold: true, color: Ui.TextFaint) { Location = new Point(2, 0) });
        _activation.Location = new Point(0, 22);
        _activationBox.Width = _activation.Width + 4;
        var gamesCard = new Card { Title = "Linked games", Dock = DockStyle.Fill, Padding = new Padding(20, 48, 20, 8), Margin = new Padding(0, 0, 0, 14) };
        gamesCard.Controls.Add(_chips);
        gamesCard.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 24, BackColor = Ui.Card });
        gamesCard.Controls.Add(_activationBox);

        // Right: keybinds.
        _deviceKind.SetItems(_engine.Devices.Select(d => d.DisplayName).ToArray());
        _deviceKind.SelectedIndexChanged += (_, _) => { _recorder.Cancel(); ShowDeviceLayout(); };
        _layout.Describe = DescribeButton;
        _layout.ButtonClicked += StartRecording;
        _picker.ActionChosen += SetSelectedButton;
        _picker.CancelClicked += () => _recorder.Cancel();
        _picker.EscapeCancelsChanged += on =>
        {
            // A preference: saved straight away, doesn't need the Save button.
            _cfg.EscapeCancels = on;
            _engine.Config.EscapeCancels = on;
            _recorder.EscapeCancels = on;
            try { ConfigStore.Save(_engine.Config); } catch (Exception ex) { Log.Error("Saving Escape setting failed", ex); }
        };
        _recorder.Changed += (soFar, held) =>
        {
            if (CurrentButtonLabel() is { } label) _picker.ShowRecording(label, soFar, held);
        };
        _recorder.Completed += action =>
        {
            _layout.RecordingButton = null;
            if (action.Key == null) ShowSelectedButton();
            else SetSelectedButton(action);
        };
        _recorder.Cancelled += () =>
        {
            _layout.RecordingButton = null;
            ShowSelectedButton();
        };

        var bindTop = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.Card };
        var legend = new Legend { Dock = DockStyle.Right, Width = 430 };
        bindTop.Controls.Add(_deviceKind);
        bindTop.Controls.Add(legend);
        _deviceKind.Location = new Point(0, 0);

        // Top: device picture | what the selected button does + choices. Bottom: full-width keyboard.
        var bindGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, BackColor = Ui.Card };
        bindGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        bindGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        bindGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        bindGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        bindGrid.Controls.Add(_layout, 0, 0);
        bindGrid.Controls.Add(_picker.ActionsPanel, 1, 0);
        bindGrid.Controls.Add(_picker.KeyboardPanel, 0, 1);
        bindGrid.SetColumnSpan(_picker.KeyboardPanel, 2);
        _picker.ActionsPanel.Dock = _picker.KeyboardPanel.Dock = DockStyle.Fill;
        _layout.Margin = new Padding(0, 4, 8, 8);
        _picker.ActionsPanel.Margin = new Padding(8, 4, 0, 8);
        _picker.KeyboardPanel.Margin = new Padding(0, 6, 0, 0);

        var bindCard = new Card { Title = "Keybinds", Dock = DockStyle.Fill, Margin = Padding.Empty };
        bindCard.Controls.Add(bindGrid);
        bindCard.Controls.Add(bindTop);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Ui.Window };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(header, 0, 0);
        right.Controls.Add(gamesCard, 0, 1);
        right.Controls.Add(bindCard, 0, 2);
        header.Margin = gamesCard.Margin = bindCard.Margin = Padding.Empty;
        gamesCard.Margin = new Padding(0, 0, 0, 14);

        var page = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Ui.Window };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(listCard, 0, 0);
        page.Controls.Add(right, 1, 0);
        right.Margin = Padding.Empty;
        return page;
    }

    private Profile? SelectedProfile => _profiles.SelectedItem as Profile;
    private IKeymapDevice? SelectedDevice =>
        _deviceKind.SelectedIndex >= 0 && _deviceKind.SelectedIndex < _engine.Devices.Count ? _engine.Devices[_deviceKind.SelectedIndex] : null;

    private void FillProfiles(Profile? select = null)
    {
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var p in _cfg.Profiles.OrderBy(p => p.IsDefault ? 0 : 1).ThenBy(p => p.Name)) _profiles.Items.Add(p);
        _profiles.EndUpdate();
        _profiles.SelectedItem = select ?? _cfg.DefaultProfile;
    }

    private void ShowProfile()
    {
        var p = SelectedProfile;
        if (p == null) return;
        _profileName.Text = p.Name;
        _profileHint.Text = p.IsDefault
            ? "Used on the desktop and in any game that doesn't have its own profile."
            : "Binds you don't set here come from the Default profile.";
        _rename.Visible = _delete.Visible = !p.IsDefault;

        _chips.SuspendLayout();
        _chips.Controls.Clear();
        if (p.IsDefault)
        {
            _chips.Controls.Add(new TextLabel("The Default profile isn't linked to games. Create a profile for each game you want different binds in.", 10f, color: Ui.TextDim)
            {
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 6, 0, 0),
            });
        }
        else
        {
            foreach (var g in p.Games)
            {
                var chip = new Chip(g);
                chip.RemoveClicked += (_, _) => RemoveGame(g);
                _chips.Controls.Add(chip);
            }
            var addRunning = new ModernButton { Text = "Add running game", Icon = Ui.IconAdd, Height = 30, Font = Ui.Font(9.5f), Margin = new Padding(0, 0, 8, 8) };
            addRunning.Click += (_, _) => AddRunningGame();
            var browse = new ModernButton { Text = "Browse…", Kind = ButtonKind.Ghost, Height = 30, Font = Ui.Font(9.5f), Margin = new Padding(0, 0, 8, 8) };
            browse.Click += (_, _) => BrowseGame();
            _chips.Controls.Add(addRunning);
            _chips.Controls.Add(browse);
        }
        _chips.ResumeLayout();

        _activationBox.Visible = !p.IsDefault;
        _activation.SelectedIndex = (int)p.Activation;
        _layout.Invalidate();
        ShowSelectedButton();
        _profiles.Invalidate();
    }

    private void ShowDeviceLayout()
    {
        _layout.Shapes = SelectedDevice?.Layout ?? Array.Empty<LayoutShape>();
        _layout.SelectedButton = null;
        _picker.ShowNothingSelected();
    }

    /// <summary>What a button does in the selected profile, and where that comes from.</summary>
    private (KeyAction Action, BindingSource Source, KeyAction? SetHere) Resolve(string buttonId)
    {
        var p = SelectedProfile;
        var d = SelectedDevice;
        var def = d?.Buttons.FirstOrDefault(b => b.Id == buttonId);
        if (p == null || d == null || def == null) return (new KeyAction(), BindingSource.Factory, null);

        if (p.BindingsFor(d.KindId).TryGetValue(buttonId, out var own) && own.Type != ActionType.Default)
            return (own, BindingSource.Custom, own);
        if (!p.IsDefault && _cfg.DefaultProfile.BindingsFor(d.KindId).TryGetValue(buttonId, out var inh) && inh.Type != ActionType.Default)
            return (inh, BindingSource.Inherited, null);
        return (def.Factory, BindingSource.Factory, null);
    }

    private (string, BindingSource) DescribeButton(string buttonId)
    {
        var (action, source, _) = Resolve(buttonId);
        return (KeyLabels.Action(action), source);
    }

    private void ShowSelectedButton()
    {
        var id = _layout.SelectedButton;
        var def = id == null ? null : SelectedDevice?.Buttons.FirstOrDefault(b => b.Id == id);
        if (def == null)
        {
            _picker.ShowNothingSelected();
            return;
        }
        var (action, source, setHere) = Resolve(def.Id);
        _picker.ShowButton(def.Label, setHere, action, source);
    }

    private string? CurrentButtonLabel()
    {
        var id = _layout.SelectedButton;
        return id == null ? null : SelectedDevice?.Buttons.FirstOrDefault(b => b.Id == id)?.Label;
    }

    /// <summary>Clicking a button on the device starts listening for the new key/combo.</summary>
    private void StartRecording(string buttonId)
    {
        _recorder.Cancel();
        _layout.SelectedButton = buttonId;
        _layout.RecordingButton = buttonId;
        ShowSelectedButton(); // fills in the picker for this button (mouse/disable buttons still usable)
        _recorder.EscapeCancels = _cfg.EscapeCancels;
        _recorder.Start();
        if (CurrentButtonLabel() is { } label) _picker.ShowRecording(label, new KeyAction(), Array.Empty<Keys>());
    }

    private void SetSelectedButton(KeyAction action)
    {
        // Picking something with the mouse while recording finishes the recording.
        if (_recorder.IsRecording)
        {
            _recorder.Stop();
            _layout.RecordingButton = null;
        }

        if (SelectedProfile is not { } p || SelectedDevice is not { } d || _layout.SelectedButton is not { } id) return;
        var map = p.BindingsFor(d.KindId);
        var changed = action.Type == ActionType.Default ? map.Remove(id) : true;
        if (action.Type != ActionType.Default) map[id] = action;
        if (changed) MarkDirty();
        _layout.Invalidate();
        ShowSelectedButton();
    }

    private void AddProfile(Profile? copyFrom)
    {
        var name = ModernDialog.Prompt(this, copyFrom == null ? "New profile" : "Duplicate profile",
            "Name it after the game, e.g. \"Elden Ring\".", copyFrom != null ? copyFrom.Name + " copy" : "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!NameIsFree(name)) return;

        Profile p;
        if (copyFrom != null)
        {
            var tmp = new AppConfig { Profiles = new() { copyFrom } };
            p = ConfigStore.DeepCopy(tmp).Profiles[0];
            p.Games.Clear();
        }
        else p = new Profile();
        p.Name = name.Trim();
        _cfg.Profiles.Add(p);
        MarkDirty();
        FillProfiles(p);
    }

    private void RenameProfile()
    {
        if (SelectedProfile is not { IsDefault: false } p) return;
        var name = ModernDialog.Prompt(this, "Rename profile", "New name:", p.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == p.Name || !NameIsFree(name)) return;
        p.Name = name.Trim();
        MarkDirty();
        FillProfiles(p);
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is not { IsDefault: false } p) return;
        if (!ModernDialog.Confirm(this, "Delete profile", $"Delete \"{p.Name}\"? Its keybinds and linked games will be removed.", "Delete", danger: true)) return;
        _cfg.Profiles.Remove(p);
        MarkDirty();
        FillProfiles();
    }

    private bool NameIsFree(string name)
    {
        if (_cfg.Profiles.Any(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ModernDialog.Info(this, "Name taken", "There's already a profile with that name.");
            return false;
        }
        return true;
    }

    private void AddRunningGame()
    {
        if (SelectedProfile is not { IsDefault: false } p) return;
        using var dlg = new RunningProgramsDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var exe in dlg.SelectedExes) LinkGame(p, exe);
    }

    private void BrowseGame()
    {
        if (SelectedProfile is not { IsDefault: false } p) return;
        using var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick the game's .exe" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LinkGame(p, Path.GetFileName(dlg.FileName));
    }

    private void LinkGame(Profile p, string exe)
    {
        exe = exe.Trim().ToLowerInvariant();
        var owner = _cfg.Profiles.FirstOrDefault(x => x != p && x.MatchesExe(exe));
        if (owner != null)
        {
            if (!ModernDialog.Confirm(this, "Already linked", $"{exe} is linked to \"{owner.Name}\". Move it to \"{p.Name}\"?", "Move it")) return;
            owner.Games.RemoveAll(g => string.Equals(g, exe, StringComparison.OrdinalIgnoreCase));
        }
        if (!p.MatchesExe(exe)) p.Games.Add(exe);
        MarkDirty();
        ShowProfile();
    }

    private void RemoveGame(string exe)
    {
        if (SelectedProfile is not { } p) return;
        p.Games.Remove(exe);
        MarkDirty();
        ShowProfile();
    }

    // ================================================================== Lighting page

    private Control BuildLightingPage()
    {
        // Color card
        _bigSwatch.Click += (_, _) => PickCustomColor();
        foreach (var hex in PresetColors)
        {
            var s = new ColorSwatch { Size = new Size(34, 34), Color = ColorTranslator.FromHtml(hex), Margin = new Padding(0, 0, 10, 10) };
            s.Click += (_, _) => SetColor(hex);
            _presets.Add(s);
        }
        _hex.Box.Leave += (_, _) => ApplyHexBox();
        _hex.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyHexBox(); } };
        var custom = new ModernButton { Text = "Custom color…" };
        custom.Click += (_, _) => PickCustomColor();

        var presetFlow = new FlowLayoutPanel { Width = 300, Height = 92, BackColor = Ui.Card, Margin = new Padding(0, 6, 0, 0) };
        presetFlow.Controls.AddRange(_presets.ToArray<Control>());
        var hexRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0, 4, 0, 0) };
        hexRow.Controls.Add(_hex);
        hexRow.Controls.Add(custom);
        var colorRight = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(28, 0, 0, 0) };
        colorRight.Controls.Add(new TextLabel("PRESETS", 8f, semibold: true, color: Ui.TextFaint));
        colorRight.Controls.Add(presetFlow);
        colorRight.Controls.Add(hexRow);
        var colorRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Ui.Card };
        colorRow.Controls.Add(_bigSwatch);
        colorRow.Controls.Add(colorRight);
        var colorCard = FixedCard("Color", 222);
        colorCard.Controls.Add(colorRow);

        // Devices card
        _lightingOn.CheckedChanged += (_, _) => { if (_cfg.Lighting.Enabled != _lightingOn.Checked) { _cfg.Lighting.Enabled = _lightingOn.Checked; MarkDirty(); } };
        var lightFlow = Stack(
            _lightingOn,
            new TextLabel("Naga Trinity, Tartarus Pro and Cynosa Chroma are set directly. Your mic, soundbar and control pod keep the color saved on them.", 9.5f, color: Ui.TextDim) { MaximumSize = new Size(680, 0), Margin = new Padding(52, 0, 0, 10) },
            _lightingStatus);
        _lightingStatus.Margin = new Padding(52, 0, 0, 0);
        var lightCard = FixedCard("Razer devices", 170);
        lightCard.Controls.Add(lightFlow);

        // OpenRGB card
        _useOpenRgb.CheckedChanged += (_, _) =>
        {
            _openRgbPath.Enabled = _openRgbBrowse.Enabled = _useOpenRgb.Checked;
            if (_cfg.Lighting.UseOpenRgb != _useOpenRgb.Checked) { _cfg.Lighting.UseOpenRgb = _useOpenRgb.Checked; MarkDirty(); }
        };
        _openRgbPath.Box.TextChanged += (_, _) => { if (_cfg.Lighting.OpenRgbPath != _openRgbPath.Text) { _cfg.Lighting.OpenRgbPath = _openRgbPath.Text; MarkDirty(); } };
        _openRgbBrowse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "OpenRGB|OpenRGB.exe|Programs (*.exe)|*.exe", Title = "Find OpenRGB.exe" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _openRgbPath.Text = dlg.FileName;
        };
        var pathRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(52, 4, 0, 0) };
        pathRow.Controls.Add(_openRgbPath);
        pathRow.Controls.Add(_openRgbBrowse);
        var orgbFlow = Stack(
            _useOpenRgb,
            new TextLabel("Optional. Only needed if you want Easy Razer to color devices it can't reach itself. OpenRGB is started hidden in the background.", 9.5f, color: Ui.TextDim) { MaximumSize = new Size(680, 0), Margin = new Padding(52, 0, 0, 4) },
            pathRow);
        var orgbCard = FixedCard("Other devices", 180);
        orgbCard.Controls.Add(orgbFlow);

        return Page("Lighting", "One color for everything. Applied when you save, at startup, after sleep and when a device is plugged in.",
            colorCard, lightCard, orgbCard);
    }

    private void ApplyHexBox()
    {
        var h = _hex.Text.Trim();
        if (!h.StartsWith('#')) h = "#" + h;
        if (Lighting.LightingService.TryParseColor(h, out _)) SetColor(h.ToUpperInvariant());
        else _hex.Text = _cfg.Color;
    }

    private void PickCustomColor()
    {
        using var dlg = new ColorDialog { FullOpen = true, Color = _bigSwatch.Color };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
    }

    private void SetColor(string hex)
    {
        var c = ColorTranslator.FromHtml(hex);
        _bigSwatch.Color = c;
        _hex.Text = hex;
        foreach (var s in _presets) s.Selected = s.Color.ToArgb() == c.ToArgb();
        if (_cfg.Color != hex) { _cfg.Color = hex; MarkDirty(); }
    }

    // ================================================================== Devices page

    private Control BuildDevicesPage()
    {
        FillDeviceCards();
        Stretch(_deviceCards);
        var page = new Panel { BackColor = Ui.Window };
        page.Controls.Add(_deviceCards);
        page.Controls.Add(PageHeader("Devices", "Close Synapse and Razer services first, or devices will show as not answering. Other Razer devices are found automatically."));
        return page;
    }

    private void FillDeviceCards()
    {
        _deviceCards.SuspendLayout();
        _deviceCards.Controls.Clear();
        _deviceRows.Clear();
        foreach (var dev in _engine.Devices)
        {
            var dot = new StatusDot { Margin = new Padding(0, 2, 0, 12) };
            var card = FixedCard(dev.DisplayName, 0);
            var body = Stack(dot);

            ToggleSwitch? write = null;
            if (dev is RazerKeymapDevice razer)
            {
                write = new ToggleSwitch { Text = "Write keybinds to this device" };
                var kind = dev.KindId;
                write.CheckedChanged += (_, _) =>
                {
                    var s = _cfg.DeviceSettingsFor(kind);
                    if (s.KeymapWritesEnabled == write.Checked) return;
                    s.KeymapWritesEnabled = write.Checked;
                    MarkDirty();
                };
                var learn = new ModernButton { Text = "Learn keys…", Margin = new Padding(0, 6, 0, 0) };
                learn.Click += (_, _) => LearnDevice(razer);
                var learnHint = new TextLabel(dev is TartarusPro
                        ? "Needed once before the Tartarus keybinds work: press each key as it lights up (about a minute)."
                        : dev is GenericRazerMouse
                            ? "Found automatically: right, middle, back and forward are ready to bind. Color is set if the mouse accepts it."
                            : "Optional. Use it if a button does something different from what you set.", 9.5f, color: Ui.TextDim)
                    { MaximumSize = new Size(680, 0), Margin = new Padding(0, 10, 0, 0) };
                body.Controls.Add(write);
                body.Controls.Add(learnHint);
                body.Controls.Add(learn);
            }
            card.Height = dev is RazerKeymapDevice ? 228 : 110;
            card.Controls.Add(body);
            _deviceRows[dev.DisplayName] = (dot, write);
            _deviceCards.Controls.Add(card);
        }

        // Lighting-only devices
        foreach (var light in _engine.LightingDevices.Where(l => l is not IKeymapDevice))
        {
            var dot = new StatusDot();
            var card = FixedCard(light.DisplayName, 120);
            var note = light is GenericRazerLight ? "Found automatically. Color only, if the device accepts Razer's color commands." : "Color only.";
            card.Controls.Add(Stack(dot, new TextLabel(note, 9.5f, color: Ui.TextDim) { Margin = new Padding(0, 6, 0, 0) }));
            _deviceRows[light.DisplayName] = (dot, null);
            _deviceCards.Controls.Add(card);
        }
        var w = Math.Max(560, _deviceCards.ClientSize.Width - 2);
        foreach (Control c in _deviceCards.Controls) c.Width = w;
        _deviceCards.ResumeLayout();
    }

    /// <summary>A device was found automatically: add it to the device picker and the Devices page.</summary>
    private void OnDevicesChanged()
    {
        if (IsDisposed) return;
        var selected = _deviceKind.SelectedIndex;
        _deviceKind.SetItems(_engine.Devices.Select(d => d.DisplayName).ToArray());
        _deviceKind.SelectedIndex = Math.Max(0, selected);
        FillDeviceCards();
        RefreshStatus();
    }

    private void LearnDevice(RazerKeymapDevice dev)
    {
        if (!dev.IsConnected)
        {
            ModernDialog.Info(this, "Not connected", $"{dev.DisplayName} isn't connected, or Synapse is still running.");
            return;
        }
        using var dlg = new LearnDialog(_engine, dev);
        dlg.ShowDialog(this);
        // Keep this window's copy of the settings in step with what was just saved.
        var mine = _cfg.DeviceSettingsFor(dev.KindId);
        var saved = _engine.Config.DeviceSettingsFor(dev.KindId);
        mine.LearnedSlots = new Dictionary<string, byte>(saved.LearnedSlots);
        mine.LearnedDefaults = new Dictionary<string, string>(saved.LearnedDefaults);
        RefreshStatus();
    }

    /// <summary>Called by the engine when device/lighting status changes.</summary>
    public void RefreshStatus()
    {
        if (IsDisposed) return;
        foreach (var dev in _engine.Devices)
        {
            if (!_deviceRows.TryGetValue(dev.DisplayName, out var row)) continue;
            row.Dot.Set(dev.IsConnected ? (dev.Status.Contains(" — ") ? DotState.Warning : DotState.Ok) : dev.Status.StartsWith("Plugged") ? DotState.Error : DotState.Off, dev.Status);
            if (row.Write != null) row.Write.Checked = _cfg.DeviceSettingsFor(dev.KindId).KeymapWritesEnabled;
        }
        foreach (var light in _engine.LightingDevices.Where(l => l is not IKeymapDevice))
            if (_deviceRows.TryGetValue(light.DisplayName, out var row))
                row.Dot.Set(light.IsConnected ? DotState.Ok : DotState.Off, light.IsConnected ? "Connected" : "Not detected");
        _lightingStatus.Text = _engine.Lighting.Status;
        _activeProfile.Text = _engine.ActiveProfile is { } a ? $"Active: {a.Name}" : "";
        _updateStatus.Text = $"Version {Updater.Current}  ·  {_updater.Status}";
    }

    // ================================================================== Settings page

    private Control BuildSettingsPage()
    {
        _autostart.Checked = Autostart.IsEnabled;
        _autostart.CheckedChanged += (_, _) =>
        {
            try { Autostart.Set(_autostart.Checked); }
            catch (Exception ex) { ModernDialog.Info(this, "Couldn't change that", ex.Message); }
        };
        var startup = FixedCard("Startup", 130);
        startup.Controls.Add(Stack(_autostart,
            new TextLabel("Closing this window keeps Easy Razer running in the tray. Use the tray icon's menu to exit.", 9.5f, color: Ui.TextDim) { Margin = new Padding(52, 0, 0, 0) }));

        _autoUpdate.CheckedChanged += (_, _) =>
        {
            // A preference: saved straight away.
            _cfg.AutoUpdate = _engine.Config.AutoUpdate = _autoUpdate.Checked;
            try { ConfigStore.Save(_engine.Config); } catch (Exception ex) { Log.Error("Saving update setting failed", ex); }
        };
        var checkNow = new ModernButton { Text = "Check for updates now", Margin = new Padding(52, 0, 0, 0), Enabled = Updater.IsInstalled };
        checkNow.Click += async (_, _) => await _updater.CheckAsync(manual: true);
        var updates = FixedCard("Updates", 176);
        updates.Controls.Add(Stack(_autoUpdate, _updateStatus, checkNow));
        _autoUpdate.Enabled = Updater.IsInstalled;

        var openLog = new ModernButton { Text = "Open log", Icon = Ui.IconSearch };
        var openFolder = new ModernButton { Text = "Open settings folder", Icon = Ui.IconFolder };
        openLog.Click += (_, _) => Shell(Log.FilePath);
        openFolder.Click += (_, _) => Shell(Paths.DataDir);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card };
        buttons.Controls.Add(openLog);
        buttons.Controls.Add(openFolder);
        var trouble = FixedCard("Troubleshooting", 150);
        trouble.Controls.Add(Stack(
            new TextLabel("If something doesn't work, the log says what the device answered.", 9.5f, color: Ui.TextDim) { Margin = new Padding(0, 0, 0, 12) },
            buttons));

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        var about = FixedCard("About", 110);
        about.Controls.Add(Stack(new TextLabel($"Easy Razer {version}", 10f, semibold: true),
            new TextLabel("Per-game keybinds and lighting for Razer devices, without Synapse.", 9.5f, color: Ui.TextDim) { Margin = new Padding(0, 4, 0, 0) }));

        return Page("Settings", "App preferences.", startup, updates, trouble, about);
    }

    private static void Shell(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) Directory.CreateDirectory(Paths.DataDir);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't open " + path, ex);
        }
    }

    // ================================================================== layout helpers

    private const int CardWidth = 780;

    private static Card FixedCard(string title, int height) => new()
    {
        Title = title,
        Width = CardWidth,
        Height = height,
    };

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Card };
        p.Controls.AddRange(controls);
        return p;
    }

    /// <summary>Cards in a vertical page stretch to the page width.</summary>
    private static void Stretch(FlowLayoutPanel flow)
    {
        flow.Resize += (_, _) =>
        {
            var w = Math.Max(560, flow.ClientSize.Width - 2);
            foreach (Control c in flow.Controls) c.Width = w;
        };
    }

    private static Control Page(string title, string subtitle, params Control[] cards)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Ui.Window };
        flow.Controls.AddRange(cards);
        Stretch(flow);
        var page = new Panel { BackColor = Ui.Window };
        page.Controls.Add(flow);
        page.Controls.Add(PageHeader(title, subtitle));
        return page;
    }

    // ================================================================== save / load

    private void LoadFromConfig()
    {
        _picker.EscapeCancels = _cfg.EscapeCancels;
        _recorder.EscapeCancels = _cfg.EscapeCancels;
        FillProfiles();
        _deviceKind.SelectedIndex = 0;
        ShowDeviceLayout();
        SetColor(_cfg.Color);
        _lightingOn.Checked = _cfg.Lighting.Enabled;
        _openRgbPath.Text = _cfg.Lighting.OpenRgbPath;
        _useOpenRgb.Checked = _cfg.Lighting.UseOpenRgb;
        _openRgbPath.Enabled = _openRgbBrowse.Enabled = _cfg.Lighting.UseOpenRgb;
        _autoUpdate.Checked = _cfg.AutoUpdate;
        RefreshStatus();
        _dirty = false;
        UpdateDirtyUi();
    }

    private async Task SaveAsync()
    {
        if (!_dirty) return;
        var colorChanged = _cfg.Color != _engine.Config.Color
                           || _cfg.Lighting.Enabled != _engine.Config.Lighting.Enabled
                           || _cfg.Lighting.UseOpenRgb != _engine.Config.Lighting.UseOpenRgb
                           || _cfg.Lighting.OpenRgbPath != _engine.Config.Lighting.OpenRgbPath;
        _save.Enabled = false;
        _saveHint.Text = "Saving…";
        try
        {
            await _engine.UpdateConfigAsync(ConfigStore.DeepCopy(_cfg), colorChanged);
        }
        catch (Exception ex)
        {
            ModernDialog.Info(this, "Saving failed", ex.Message);
            UpdateDirtyUi();
            return;
        }
        _dirty = false;
        UpdateDirtyUi();
        _saveHint.Text = "Saved";
        Saved?.Invoke();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty) return;
        var answer = ModernDialog.Ask(this, "Unsaved changes", "Save your changes before closing?", "Save", "Don't save");
        if (answer == DialogResult.Cancel) e.Cancel = true;
        else if (answer == DialogResult.Yes)
        {
            e.Cancel = true;
            _ = SaveAsync().ContinueWith(_ => BeginInvoke(Close), TaskScheduler.FromCurrentSynchronizationContext());
        }
        else _dirty = false;
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateDirtyUi();
        _profiles.Invalidate();
    }

    private void UpdateDirtyUi()
    {
        _save.Enabled = _dirty;
        _saveHint.Text = _dirty ? "Unsaved changes  ·  Ctrl+S" : "All changes saved";
        _saveHint.ForeColor = _dirty ? Ui.Warning : Ui.TextFaint;
        Text = _dirty ? "Easy Razer — unsaved changes" : "Easy Razer";
    }
}

// ====================================================================== small widgets used above

/// <summary>App name with a green dot, top of the sidebar.</summary>
internal sealed class BrandHeader : DrawnControl
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Sidebar);
        Ui.Smooth(g);
        using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, 10, Height / 2f - 7, 14, 14);
        Ui.DrawText(g, "Easy Razer", Ui.Font(14f, semibold: true), Ui.Text, new Rectangle(34, 0, Width - 34, Height));
    }
}

/// <summary>Color key for the keybind picture.</summary>
internal sealed class Legend : DrawnControl
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Card);
        Ui.Smooth(g);
        var items = new[] { ("Set in this profile", Theme.Custom), ("From Default", Theme.Inherited), ("Device default", Ui.TextFaint) };
        var x = Width;
        for (var i = items.Length - 1; i >= 0; i--)
        {
            var (text, color) = items[i];
            var w = TextRenderer.MeasureText(text, Ui.Font(9f)).Width;
            x -= w + 30;
            using var b = new SolidBrush(color);
            g.FillEllipse(b, x, Height / 2f - 5, 10, 10);
            Ui.DrawText(g, text, Ui.Font(9f), Ui.TextDim, new Rectangle(x + 14, 0, w + 8, Height));
        }
    }
}

internal enum DotState { Off, Ok, Warning, Error }

/// <summary>Colored dot + status text.</summary>
internal sealed class StatusDot : DrawnControl
{
    private DotState _state;

    public StatusDot()
    {
        Height = 24;
        Width = 700;
        Font = Ui.Font(10f);
    }

    public void Set(DotState state, string text)
    {
        _state = state;
        Text = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Card);
        Ui.Smooth(g);
        var color = _state switch
        {
            DotState.Ok => Ui.Accent,
            DotState.Warning => Ui.Warning,
            DotState.Error => Ui.Danger,
            _ => Ui.TextFaint,
        };
        using (var b = new SolidBrush(color)) g.FillEllipse(b, 1, Height / 2f - 5, 10, 10);
        Ui.DrawText(g, Text, Font, _state == DotState.Off ? Ui.TextDim : Ui.Text, new Rectangle(20, 0, Width - 20, Height));
    }
}

/// <summary>Round color sample; click to choose it.</summary>
internal sealed class ColorSwatch : DrawnControl
{
    private Color _color = Color.Lime;
    private bool _selected;

    public ColorSwatch() => Cursor = Cursors.Hand;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Color { get => _color; set { _color = value; Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Big { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Card);
        Ui.Smooth(g);
        var inset = Big ? 6 : 4;
        var r = new RectangleF(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
        if (Big)
        {
            // soft glow in the color itself
            using var glow = new SolidBrush(Color.FromArgb(45, _color));
            g.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
        }
        using (var b = new SolidBrush(_color)) g.FillEllipse(b, r);
        using (var edge = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawEllipse(edge, r);
        if (_selected || (Hover && !Big))
        {
            using var ring = new Pen(_selected ? Ui.Text : Ui.TextDim, 2f);
            g.DrawEllipse(ring, 1, 1, Width - 3, Height - 3);
        }
    }
}
