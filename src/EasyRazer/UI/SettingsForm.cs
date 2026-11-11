using System.Diagnostics;
using EasyRazer.Config;
using EasyRazer.Core;
using EasyRazer.Devices;
using EasyRazer.Devices.Razer;

namespace EasyRazer.UI;

/// <summary>
/// Settings window. Edits a copy of the config; nothing changes on the devices until you press Save.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly ProfileEngine _engine;
    private AppConfig _cfg;
    private bool _dirty;

    // Profiles tab
    private readonly ListBox _profiles = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _games = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _activation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ComboBox _deviceKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly DeviceLayoutView _layout = new() { Dock = DockStyle.Fill };
    private readonly BindingPicker _picker = new() { Dock = DockStyle.Fill };
    private readonly GroupBox _gamesBox = new() { Text = "Linked games", Dock = DockStyle.Fill };

    // Lighting tab
    private readonly Panel _swatch = new() { Width = 64, Height = 32, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _hex = new() { Width = 90 };
    private readonly CheckBox _lightingOn = new() { Text = "Control lighting (through OpenRGB)", AutoSize = true };
    private readonly TextBox _openRgbPath = new() { Width = 380 };
    private readonly Label _lightingStatus = new() { AutoSize = true, MaximumSize = new Size(520, 0) };

    // Devices tab
    private readonly ListView _devices = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        Dock = DockStyle.Top,
        Height = 110,
    };
    private readonly CheckBox _writeEnabled = new()
    {
        Text = "Write keybinds to this device",
        AutoSize = true,
        MaximumSize = new Size(560, 0),
    };

    // General tab
    private readonly CheckBox _autostart = new() { Text = "Start Easy Razer with Windows", AutoSize = true };

    private readonly KeyRecorder _recorder = new();

    public event Action? Saved;

    public SettingsForm(ProfileEngine engine)
    {
        _engine = engine;
        _cfg = ConfigStore.DeepCopy(engine.Config);

        Text = "Easy Razer";
        Size = new Size(1180, 880);
        MinimumSize = new Size(1000, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildProfilesTab());
        tabs.TabPages.Add(BuildLightingTab());
        tabs.TabPages.Add(BuildDevicesTab());
        tabs.TabPages.Add(BuildGeneralTab());

        var save = new Button { Text = "Save", Width = 90 };
        var close = new Button { Text = "Close", Width = 90 };
        save.Click += async (_, _) => await SaveAsync();
        close.Click += (_, _) => Close();
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(6),
        };
        bottom.Controls.Add(close);
        bottom.Controls.Add(save);

        Controls.Add(tabs);
        Controls.Add(bottom);

        FormClosing += OnClosing;
        FormClosed += (_, _) => _recorder.Dispose();
        // Never keep listening to the keyboard once you leave this window.
        Deactivate += (_, _) => _recorder.Cancel();
        LoadFromConfig();
    }

    // ------------------------------------------------------------------ Profiles

    private TabPage BuildProfilesTab()
    {
        var page = new TabPage("Profiles") { Padding = new Padding(8) };

        var add = new Button { Text = "Add", Width = 70 };
        var rename = new Button { Text = "Rename", Width = 70 };
        var copy = new Button { Text = "Copy", Width = 70 };
        var delete = new Button { Text = "Delete", Width = 70 };
        add.Click += (_, _) => AddProfile(null);
        copy.Click += (_, _) => { if (SelectedProfile is { } p) AddProfile(p); };
        rename.Click += (_, _) => RenameProfile();
        delete.Click += (_, _) => DeleteProfile();
        var profileButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64 };
        profileButtons.Controls.AddRange(new Control[] { add, rename, copy, delete });

        var left = new GroupBox { Text = "Profiles", Dock = DockStyle.Fill };
        left.Controls.Add(_profiles);
        left.Controls.Add(profileButtons);

        // Games
        var addRunning = new Button { Text = "Add running game…", AutoSize = true };
        var browse = new Button { Text = "Browse for .exe…", AutoSize = true };
        var removeGame = new Button { Text = "Remove", AutoSize = true };
        addRunning.Click += (_, _) => AddRunningGame();
        browse.Click += (_, _) => BrowseGame();
        removeGame.Click += (_, _) => RemoveGame();
        var gameButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 150, FlowDirection = FlowDirection.TopDown };
        gameButtons.Controls.AddRange(new Control[] { addRunning, browse, removeGame });
        _gamesBox.Controls.Add(_games);
        _gamesBox.Controls.Add(gameButtons);

        _activation.Items.Add("Only while the game is in front (alt-tab = desktop binds)");
        _activation.Items.Add("As long as the game is running (even when alt-tabbed)");
        _activation.SelectedIndexChanged += (_, _) =>
        {
            if (SelectedProfile is { } p && _activation.SelectedIndex >= 0)
            {
                var mode = (ActivationMode)_activation.SelectedIndex;
                if (p.Activation != mode) { p.Activation = mode; MarkDirty(); }
            }
        };

        foreach (var d in _engine.Devices) _deviceKind.Items.Add(d.DisplayName);
        _deviceKind.SelectedIndexChanged += (_, _) => { _recorder.Cancel(); ShowDeviceLayout(); };

        _layout.Describe = DescribeButton;
        _layout.ButtonClicked += StartRecording;
        _picker.ActionChosen += SetSelectedButton;
        _picker.CancelClicked += () => _recorder.Cancel();
        _picker.EscapeCancelsChanged += on =>
        {
            // A viewing preference: saved straight away, doesn't need the Save button.
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

        var legend = new Label
        {
            AutoSize = true,
            Padding = new Padding(16, 6, 0, 0),
            Text = "Green = set in this profile    Blue = from Default profile    Grey = factory",
            ForeColor = SystemColors.GrayText,
        };
        var bindTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        bindTop.Controls.AddRange(new Control[]
        {
            new Label { Text = "Device:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _deviceKind, legend,
        });

        var editor = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = Theme.Background,
        };
        editor.Panel1.Controls.Add(_layout);
        editor.Panel2.Controls.Add(_picker);
        editor.HandleCreated += (_, _) => editor.SplitterDistance = Math.Max(150, editor.Height * 45 / 100);

        var bindsBox = new GroupBox { Text = "Keybinds — click a button on the device, then click what it should do", Dock = DockStyle.Fill };
        bindsBox.Controls.Add(editor);
        bindsBox.Controls.Add(bindTop);

        var activationRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        activationRow.Controls.Add(new Label { Text = "Active:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        activationRow.Controls.Add(_activation);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(_gamesBox, 0, 0);
        right.Controls.Add(activationRow, 0, 1);
        right.Controls.Add(bindsBox, 0, 2);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Controls.Add(left, 0, 0);
        split.Controls.Add(right, 1, 0);

        _profiles.SelectedIndexChanged += (_, _) => { _recorder.Cancel(); ShowProfile(); };
        page.Controls.Add(split);
        return page;
    }

    private Profile? SelectedProfile => _profiles.SelectedItem as Profile;
    private IKeymapDevice? SelectedDevice =>
        _deviceKind.SelectedIndex >= 0 ? _engine.Devices[_deviceKind.SelectedIndex] : null;

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
        _gamesBox.Enabled = !p.IsDefault;
        _gamesBox.Text = p.IsDefault ? "Linked games (Default is used whenever no game profile matches)" : "Linked games";
        _games.Items.Clear();
        foreach (var g in p.Games) _games.Items.Add(g);
        _activation.Enabled = !p.IsDefault;
        _activation.SelectedIndex = (int)p.Activation;
        _layout.Invalidate();
        ShowSelectedButton();
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
        var name = Prompt.Ask(this, "New profile", "Profile name (e.g. the game's name):", copyFrom != null ? copyFrom.Name + " copy" : "");
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
        if (SelectedProfile is not { } p) return;
        if (p.IsDefault) { MessageBox.Show(this, "The Default profile can't be renamed.", Text); return; }
        var name = Prompt.Ask(this, "Rename profile", "New name:", p.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == p.Name || !NameIsFree(name)) return;
        p.Name = name.Trim();
        MarkDirty();
        FillProfiles(p);
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is not { } p) return;
        if (p.IsDefault) { MessageBox.Show(this, "The Default profile can't be deleted.", Text); return; }
        if (MessageBox.Show(this, $"Delete profile '{p.Name}'?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _cfg.Profiles.Remove(p);
        MarkDirty();
        FillProfiles();
    }

    private bool NameIsFree(string name)
    {
        if (_cfg.Profiles.Any(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "There's already a profile with that name.", Text);
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
            if (MessageBox.Show(this, $"{exe} is linked to '{owner.Name}'. Move it to '{p.Name}'?", Text,
                    MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            owner.Games.RemoveAll(g => string.Equals(g, exe, StringComparison.OrdinalIgnoreCase));
        }
        if (!p.MatchesExe(exe)) p.Games.Add(exe);
        MarkDirty();
        ShowProfile();
    }

    private void RemoveGame()
    {
        if (SelectedProfile is not { } p || _games.SelectedItem is not string exe) return;
        p.Games.Remove(exe);
        MarkDirty();
        ShowProfile();
    }

    // ------------------------------------------------------------------ Lighting

    private TabPage BuildLightingTab()
    {
        var page = new TabPage("Lighting") { Padding = new Padding(12) };
        var pick = new Button { Text = "Choose color…", AutoSize = true };
        pick.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = _swatch.BackColor };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SetColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
        };
        _hex.Leave += (_, _) =>
        {
            var h = _hex.Text.Trim();
            if (!h.StartsWith('#')) h = "#" + h;
            if (Lighting.LightingService.TryParseColor(h, out _)) SetColor(h.ToUpperInvariant());
            else _hex.Text = _cfg.Color;
        };
        _lightingOn.CheckedChanged += (_, _) => { if (_cfg.Lighting.Enabled != _lightingOn.Checked) { _cfg.Lighting.Enabled = _lightingOn.Checked; MarkDirty(); } };

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "OpenRGB|OpenRGB.exe|Programs (*.exe)|*.exe", Title = "Find OpenRGB.exe" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _openRgbPath.Text = dlg.FileName;
        };
        _openRgbPath.TextChanged += (_, _) => { if (_cfg.Lighting.OpenRgbPath != _openRgbPath.Text) { _cfg.Lighting.OpenRgbPath = _openRgbPath.Text; MarkDirty(); } };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(new Label
        {
            Text = "One static color for every device. Applied when you press Save, at startup, after sleep, and on reconnect.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 12),
        });
        flow.Controls.Add(Row(new Label { Text = "Color:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, _swatch, _hex, pick));
        flow.Controls.Add(_lightingOn);
        flow.Controls.Add(new Label { Text = "OpenRGB.exe (started hidden automatically if it isn't running):", AutoSize = true, Margin = new Padding(0, 16, 0, 2) });
        flow.Controls.Add(Row(_openRgbPath, browse));
        flow.Controls.Add(new Label { Text = "Status:", AutoSize = true, Margin = new Padding(0, 16, 0, 2) });
        flow.Controls.Add(_lightingStatus);
        page.Controls.Add(flow);
        return page;
    }

    private void SetColor(string hex)
    {
        _swatch.BackColor = ColorTranslator.FromHtml(hex);
        _hex.Text = hex;
        if (_cfg.Color != hex) { _cfg.Color = hex; MarkDirty(); }
    }

    // ------------------------------------------------------------------ Devices

    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("Devices") { Padding = new Padding(12) };
        _devices.Columns.Add("Device", 160);
        _devices.Columns.Add("Status", 520);
        _devices.SelectedIndexChanged += (_, _) => ShowDevice();
        _writeEnabled.CheckedChanged += (_, _) =>
        {
            if (_devices.SelectedIndices.Count == 0) return;
            var dev = _engine.Devices[_devices.SelectedIndices[0]];
            var s = _cfg.DeviceSettingsFor(dev.KindId);
            if (s.KeymapWritesEnabled == _writeEnabled.Checked) return;
            s.KeymapWritesEnabled = _writeEnabled.Checked;
            MarkDirty();
        };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(new Label
        {
            Text = "Devices listed here get per-game keybinds stored in their own memory. " +
                   "Every other device (keyboard, mic, soundbar…) just gets the lighting color through OpenRGB.\n\n" +
                   "\"Learn keys\" finds out how a device numbers its buttons: press each one when it lights up. " +
                   "The Tartarus Pro needs this once before its keybinds work. The Naga Trinity works without it.",
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Margin = new Padding(0, 12, 0, 8),
        });
        flow.Controls.Add(_writeEnabled);
        var learn = new Button { Text = "Learn keys…", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        learn.Click += (_, _) => LearnSelectedDevice();
        flow.Controls.Add(learn);

        page.Controls.Add(flow);
        page.Controls.Add(_devices);
        return page;
    }

    private void LearnSelectedDevice()
    {
        if (_devices.SelectedIndices.Count == 0) return;
        if (_engine.Devices[_devices.SelectedIndices[0]] is not RazerKeymapDevice dev) return;
        if (!dev.IsConnected)
        {
            MessageBox.Show(this, $"{dev.DisplayName} isn't connected (or Synapse is still running).", Text);
            return;
        }
        using var dlg = new LearnDialog(_engine, dev);
        dlg.ShowDialog(this);
        // Keep this window's copy of the settings in step with what was just saved.
        var mine = _cfg.DeviceSettingsFor(dev.KindId);
        foreach (var (id, slot) in dlg.LearnedSlots) mine.LearnedSlots[id] = slot;
        RefreshStatus();
    }

    private void ShowDevice()
    {
        _writeEnabled.Enabled = _devices.SelectedIndices.Count > 0;
        if (_devices.SelectedIndices.Count == 0) return;
        var dev = _engine.Devices[_devices.SelectedIndices[0]];
        _writeEnabled.Checked = _cfg.DeviceSettingsFor(dev.KindId).KeymapWritesEnabled;
    }

    /// <summary>Called by the engine when device/lighting status changes.</summary>
    public void RefreshStatus()
    {
        if (IsDisposed) return;
        var selected = _devices.SelectedIndices.Count > 0 ? _devices.SelectedIndices[0] : 0;
        _devices.BeginUpdate();
        _devices.Items.Clear();
        foreach (var d in _engine.Devices) _devices.Items.Add(new ListViewItem(new[] { d.DisplayName, d.Status }));
        _devices.EndUpdate();
        if (_devices.Items.Count > 0) _devices.Items[Math.Min(selected, _devices.Items.Count - 1)].Selected = true;
        _lightingStatus.Text = _engine.Lighting.Status;
    }

    // ------------------------------------------------------------------ General

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General") { Padding = new Padding(12) };
        _autostart.Checked = Autostart.IsEnabled;
        _autostart.CheckedChanged += (_, _) =>
        {
            try { Autostart.Set(_autostart.Checked); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't change startup setting: " + ex.Message, Text); }
        };

        var openLog = new Button { Text = "Open log", AutoSize = true };
        var openFolder = new Button { Text = "Open settings folder", AutoSize = true };
        openLog.Click += (_, _) => Shell(Log.FilePath);
        openFolder.Click += (_, _) => Shell(Paths.DataDir);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(_autostart);
        flow.Controls.Add(Row(openLog, openFolder));
        flow.Controls.Add(new Label
        {
            Text = "Closing this window keeps Easy Razer running in the tray. Use the tray icon's menu to exit.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 16, 0, 0),
        });
        page.Controls.Add(flow);
        return page;
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

    // ------------------------------------------------------------------ Save / load

    private void LoadFromConfig()
    {
        _picker.EscapeCancels = _cfg.EscapeCancels;
        _recorder.EscapeCancels = _cfg.EscapeCancels;
        FillProfiles();
        if (_deviceKind.Items.Count > 0) _deviceKind.SelectedIndex = 0;
        _swatch.BackColor = ColorTranslator.FromHtml(_cfg.Color);
        _hex.Text = _cfg.Color;
        _lightingOn.Checked = _cfg.Lighting.Enabled;
        _openRgbPath.Text = _cfg.Lighting.OpenRgbPath;
        RefreshStatus();
        _dirty = false;
        UpdateTitle();
    }

    private async Task SaveAsync()
    {
        var colorChanged = _cfg.Color != _engine.Config.Color
                           || _cfg.Lighting.Enabled != _engine.Config.Lighting.Enabled
                           || _cfg.Lighting.OpenRgbPath != _engine.Config.Lighting.OpenRgbPath;
        var selected = SelectedProfile?.Name;
        try
        {
            await _engine.UpdateConfigAsync(ConfigStore.DeepCopy(_cfg), colorChanged);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Saving failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _dirty = false;
        UpdateTitle();
        Saved?.Invoke();
        if (selected != null) _profiles.SelectedItem = _cfg.Profiles.FirstOrDefault(p => p.Name == selected);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty) return;
        var answer = MessageBox.Show(this, "Save your changes?", Text, MessageBoxButtons.YesNoCancel);
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
        UpdateTitle();
    }

    private void UpdateTitle() => Text = _dirty ? "Easy Razer — unsaved changes" : "Easy Razer";

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        p.Controls.AddRange(controls);
        return p;
    }
}

/// <summary>Minimal text prompt (WinForms has none built in).</summary>
internal static class Prompt
{
    public static string? Ask(IWin32Window owner, string title, string label, string initial)
    {
        using var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 110),
        };
        var lbl = new Label { Text = label, Left = 12, Top = 12, AutoSize = true };
        var box = new TextBox { Text = initial, Left = 12, Top = 36, Width = 336 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 192, Top = 72, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 273, Top = 72, Width = 75 };
        f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
