using System.ComponentModel;
using EasyRazer.Config;
using EasyRazer.UI.Modern;

namespace EasyRazer.UI;

/// <summary>
/// The editing half of the keybind card, split into two areas the window places itself:
/// <see cref="ActionsPanel"/> (what the selected button does + mouse/turbo/DPI/other choices) and
/// <see cref="KeyboardPanel"/> (Ctrl/Shift/Alt + the on-screen keyboard).
/// While a button is being recorded it shows the keys being pressed live.
/// </summary>
public sealed class BindingPicker
{
    private readonly Label _header = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Font = Ui.Font(13f, semibold: true),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        UseMnemonic = false,
        BackColor = Ui.Card,
    };
    private readonly Label _subheader = new()
    {
        Dock = DockStyle.Top,
        Height = 26,
        Font = Ui.Font(9.5f),
        ForeColor = Ui.TextDim,
        TextAlign = ContentAlignment.TopLeft,
        AutoEllipsis = true,
        UseMnemonic = false,
        BackColor = Ui.Card,
    };
    private readonly ModernButton _cancel = new() { Text = "Cancel", Kind = ButtonKind.Secondary, Visible = false, Height = 30, Font = Ui.Font(9.5f) };
    private readonly ToggleSwitch _escapeCancels = new() { Text = "Esc cancels", Font = Ui.Font(9.5f) };
    private readonly KeyboardView _keyboard = new() { Dock = DockStyle.Fill };
    private readonly ModernButton _ctrl = Pill("Ctrl");
    private readonly ModernButton _shift = Pill("Shift");
    private readonly ModernButton _alt = Pill("Alt");
    private readonly List<ModernButton> _mouseButtons = new();
    private readonly ModernButton _turboLeft = Pill("Left");
    private readonly ModernButton _turboRight = Pill("Right");
    private readonly ModernButton _rateDown = Pill("−");
    private readonly ModernButton _rateUp = Pill("+");
    private readonly Label _rate = new() { AutoSize = false, Width = 50, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Text, Font = Ui.Font(9.5f, semibold: true), BackColor = Ui.Card, Margin = new Padding(0, 0, 6, 0) };
    private readonly ModernButton _disable = Pill("Disable");
    private readonly ModernButton _reset = new() { Text = "Reset to default", Kind = ButtonKind.Ghost, Height = 30, Font = Ui.Font(9.5f), Padding = new Padding(10, 0, 10, 0) };

    private KeyAction? _current; // what's set in this profile (null = not set)
    private bool _loading;
    private bool _hasButton;
    private int _turboRate = 15;

    public event Action<KeyAction>? ActionChosen;
    public event Action? CancelClicked;
    public event Action<bool>? EscapeCancelsChanged;

    /// <summary>Selected button's state and the non-keyboard choices.</summary>
    public Control ActionsPanel { get; }

    /// <summary>Ctrl/Shift/Alt toggles and the on-screen keyboard.</summary>
    public Control KeyboardPanel { get; }

    public BindingPicker()
    {
        _keyboard.KeyClicked += k => Emit(new KeyAction
        {
            Type = ActionType.Key,
            Key = k.ToString(),
            Ctrl = _ctrl.Active,
            Shift = _shift.Active,
            Alt = _alt.Active,
        });

        foreach (var t in new[] { _ctrl, _shift, _alt })
            t.Click += (_, _) =>
            {
                t.Active = !t.Active;
                if (_loading || _current is not { Type: ActionType.Key } cur) return;
                var updated = cur.Clone();
                updated.Ctrl = _ctrl.Active;
                updated.Shift = _shift.Active;
                updated.Alt = _alt.Active;
                Emit(updated);
            };

        foreach (var m in Enum.GetValues<MouseAction>())
        {
            var b = Pill(ShortMouse(m));
            b.Tag = m;
            b.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Mouse, Mouse = m });
            _mouseButtons.Add(b);
        }
        _turboLeft.Click += (_, _) => EmitTurbo(MouseAction.Left);
        _turboRight.Click += (_, _) => EmitTurbo(MouseAction.Right);
        _rateDown.Click += (_, _) => ChangeRate(-1);
        _rateUp.Click += (_, _) => ChangeRate(+1);
        _disable.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Disabled });
        _reset.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Default });
        _cancel.Click += (_, _) => CancelClicked?.Invoke();

        _escapeCancels.CheckedChanged += (_, _) =>
        {
            _keyboard.DisabledKeys = _escapeCancels.Checked ? new[] { Keys.Escape } : Array.Empty<Keys>();
            if (!_loading) EscapeCancelsChanged?.Invoke(_escapeCancels.Checked);
        };
        UpdateRateLabel();

        // ---- actions panel
        MouseAction[] clicks = { MouseAction.Left, MouseAction.Right, MouseAction.Middle, MouseAction.Back, MouseAction.Forward };
        MouseAction[] scrollDpi = { MouseAction.ScrollUp, MouseAction.ScrollDown, MouseAction.DpiUp, MouseAction.DpiDown };
        var lines = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Ui.Card,
            Padding = new Padding(0, 8, 0, 0),
        };
        lines.Controls.Add(Line("Mouse", _mouseButtons.Where(b => clicks.Contains((MouseAction)b.Tag!)).ToArray<Control>()));
        lines.Controls.Add(Line("Scroll / DPI", _mouseButtons.Where(b => scrollDpi.Contains((MouseAction)b.Tag!)).ToArray<Control>()));
        lines.Controls.Add(Line("Turbo", _turboLeft, _turboRight, Spacer(10), _rateDown, _rate, _rateUp));
        lines.Controls.Add(Line("Other", _disable, _reset));

        var cancelRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 0, AutoSize = true, BackColor = Ui.Card, WrapContents = false };
        cancelRow.Controls.Add(_cancel);

        var actions = new Panel { BackColor = Ui.Card, Padding = new Padding(8, 0, 0, 0) };
        actions.Controls.Add(lines);
        actions.Controls.Add(cancelRow);
        actions.Controls.Add(_subheader);
        actions.Controls.Add(_header);
        ActionsPanel = actions;

        // ---- keyboard panel
        var modRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.Card };
        var mods = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Ui.Card };
        mods.Controls.Add(new TextLabel("HOLD WITH", 8f, semibold: true, color: Ui.TextFaint) { Margin = new Padding(0, 9, 10, 0) });
        mods.Controls.AddRange(new Control[] { _ctrl, _shift, _alt });
        mods.Controls.Add(new TextLabel("then click a key, or click a button above and just press it", 9f, color: Ui.TextFaint) { Margin = new Padding(8, 7, 0, 0) });
        var escBox = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Ui.Card };
        escBox.Controls.Add(_escapeCancels);
        modRow.Controls.Add(mods);
        modRow.Controls.Add(escBox);

        var kb = new Panel { BackColor = Ui.Card };
        kb.Controls.Add(_keyboard);
        kb.Controls.Add(modRow);
        KeyboardPanel = kb;

        ShowNothingSelected();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool EscapeCancels
    {
        get => _escapeCancels.Checked;
        set
        {
            _loading = true;
            _escapeCancels.Checked = value;
            _keyboard.DisabledKeys = value ? new[] { Keys.Escape } : Array.Empty<Keys>();
            _loading = false;
        }
    }

    public void ShowNothingSelected()
    {
        _hasButton = false;
        _current = null;
        _header.Text = "Pick a button";
        _header.ForeColor = Ui.Text;
        _subheader.Text = "Click any button on the device, then press the key or combo you want.";
        _cancel.Visible = false;
        SetEnabled(false);
        _keyboard.ChosenKey = null;
        _keyboard.HeldKeys = Array.Empty<Keys>();
        Highlight(null);
    }

    /// <summary>Recording: show the combo built so far and the keys currently held.</summary>
    public void ShowRecording(string label, KeyAction soFar, IReadOnlyCollection<Keys> held)
    {
        var combo = soFar.Key == null ? "" : KeyLabels.Action(soFar).Replace("+", " + ");
        _header.Text = combo.Length == 0 ? "Press a key or combo…" : combo;
        _header.ForeColor = Ui.Warning;
        _subheader.Text = combo.Length == 0
            ? $"{label}  ·  {(EscapeCancels ? "Esc to cancel" : "click Cancel to stop")}"
            : $"{label}  ·  let go of all keys to set it";
        _cancel.Visible = true;
        _keyboard.HeldKeys = held;
        _keyboard.ChosenKey = null;
        Highlight(null);
    }

    /// <summary>Show the button being edited and what it currently does.</summary>
    public void ShowButton(string label, KeyAction? setHere, KeyAction effective, BindingSource source)
    {
        _hasButton = true;
        _current = setHere;
        _header.Text = KeyLabels.Action(effective);
        _header.ForeColor = source == BindingSource.Custom ? Theme.Custom : source == BindingSource.Inherited ? Theme.Inherited : Ui.Text;
        _subheader.Text = label + source switch
        {
            BindingSource.Custom => "  ·  set in this profile",
            BindingSource.Inherited => "  ·  from the Default profile",
            _ => "  ·  device default",
        } + "  ·  click it again to re-record";
        _cancel.Visible = false;
        SetEnabled(true);

        _loading = true;
        var isKey = effective.Type == ActionType.Key;
        _ctrl.Active = isKey && effective.Ctrl;
        _shift.Active = isKey && effective.Shift;
        _alt.Active = isKey && effective.Alt;
        if (effective.TurboRate is { } rate) { _turboRate = rate; UpdateRateLabel(); }
        _loading = false;

        _keyboard.HeldKeys = Array.Empty<Keys>();
        _keyboard.ChosenKey = isKey && Enum.TryParse<Keys>(effective.Key, out var k) ? k : null;
        Highlight(effective);
        _reset.Enabled = setHere != null;
    }

    private void Highlight(KeyAction? a)
    {
        var turbo = a is { Type: ActionType.Mouse, TurboRate: not null };
        foreach (var b in _mouseButtons)
            b.Active = a is { Type: ActionType.Mouse } && !turbo && (MouseAction)b.Tag! == a.Mouse;
        _turboLeft.Active = turbo && a!.Mouse == MouseAction.Left;
        _turboRight.Active = turbo && a!.Mouse == MouseAction.Right;
        _disable.Active = a is { Type: ActionType.Disabled };
    }

    private void ChangeRate(int delta)
    {
        _turboRate = Math.Clamp(_turboRate + delta, 1, 20);
        UpdateRateLabel();
        if (_current is { Type: ActionType.Mouse, TurboRate: not null } cur) EmitTurbo(cur.Mouse);
    }

    private void UpdateRateLabel() => _rate.Text = $"{_turboRate}/s";

    private void EmitTurbo(MouseAction button) => Emit(new KeyAction
    {
        Type = ActionType.Mouse,
        Mouse = button,
        TurboRate = _turboRate,
    });

    private void Emit(KeyAction action)
    {
        if (!_hasButton) return;
        _current = action.Type == ActionType.Default ? null : action;
        ActionChosen?.Invoke(action);
    }

    private void SetEnabled(bool on)
    {
        _keyboard.Enabled = on;
        foreach (Control c in new Control[] { _ctrl, _shift, _alt, _disable, _reset, _turboLeft, _turboRight, _rateDown, _rateUp }) c.Enabled = on;
        foreach (var b in _mouseButtons) b.Enabled = on;
    }

    private static string ShortMouse(MouseAction m) => m switch
    {
        MouseAction.Left => "Left",
        MouseAction.Right => "Right",
        MouseAction.Middle => "Middle",
        MouseAction.Back => "Back",
        MouseAction.Forward => "Fwd",
        MouseAction.ScrollUp => "Scroll ↑",
        MouseAction.ScrollDown => "Scroll ↓",
        MouseAction.DpiUp => "DPI +",
        MouseAction.DpiDown => "DPI −",
        _ => m.ToString(),
    };

    private static ModernButton Pill(string text) => new()
    {
        Text = text,
        Kind = ButtonKind.Secondary,
        Height = 30,
        Padding = new Padding(11, 0, 11, 0),
        Margin = new Padding(0, 0, 6, 0),
        Font = Ui.Font(9.5f),
    };

    private static Control Spacer(int w) => new Panel { Width = w, Height = 1, BackColor = Ui.Card, Margin = Padding.Empty };

    /// <summary>One labelled row of choices: "MOUSE   [Left] [Right] …".</summary>
    private static Control Line(string title, params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0, 0, 0, 8) };
        row.Controls.Add(new Label
        {
            Text = title.ToUpperInvariant(),
            Font = Ui.Font(8f, semibold: true),
            ForeColor = Ui.TextFaint,
            AutoSize = false,
            Width = 88,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Ui.Card,
            Margin = Padding.Empty,
        });
        row.Controls.AddRange(controls);
        return row;
    }
}
