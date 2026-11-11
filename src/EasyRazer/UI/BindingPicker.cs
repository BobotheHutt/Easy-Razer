using EasyRazer.Config;

namespace EasyRazer.UI;

/// <summary>
/// The bottom half of the keybind editor. While a button is being recorded it shows the keys
/// being pressed live. Otherwise you can also click a key on the on-screen keyboard, a mouse action,
/// Disable or Reset. Raises <see cref="ActionChosen"/> when something is clicked.
/// </summary>
public sealed class BindingPicker : UserControl
{
    private static readonly Color ChosenFill = Color.FromArgb(40, 90, 30);

    private readonly Label _header = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(4, 0, 0, 0),
    };
    private readonly Button _cancel = Flat("Cancel");
    private readonly CheckBox _escapeCancels = new()
    {
        Text = "Escape cancels (Escape can't be bound)",
        AutoSize = true,
        ForeColor = Theme.Text,
        Padding = new Padding(0, 6, 0, 0),
    };
    private readonly KeyboardView _keyboard = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _ctrl = Toggle("Ctrl");
    private readonly CheckBox _shift = Toggle("Shift");
    private readonly CheckBox _alt = Toggle("Alt");
    private readonly List<Button> _mouseButtons = new();
    private readonly Button _autoLeft = Flat("Auto left click");
    private readonly Button _autoRight = Flat("Auto right click");
    private readonly NumericUpDown _autoRate = new()
    {
        Minimum = 2,
        Maximum = 50,
        Value = 15,
        Width = 56,
        BackColor = Theme.Key,
        ForeColor = Theme.Text,
        Margin = new Padding(3, 6, 0, 0),
    };
    private readonly Button _disable = Flat("Disable");
    private readonly Button _reset = Flat("Reset to default");

    private KeyAction? _current; // what's set in this profile (null = not set)
    private bool _loading;
    private bool _hasButton;

    public event Action<KeyAction>? ActionChosen;
    public event Action? CancelClicked;
    public event Action<bool>? EscapeCancelsChanged;

    public BindingPicker()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        _keyboard.KeyClicked += k => Emit(new KeyAction
        {
            Type = ActionType.Key,
            Key = k.ToString(),
            Ctrl = _ctrl.Checked,
            Shift = _shift.Checked,
            Alt = _alt.Checked,
        });

        foreach (var t in new[] { _ctrl, _shift, _alt })
            t.CheckedChanged += (_, _) =>
            {
                if (_loading || _current is not { Type: ActionType.Key } cur) return;
                var updated = cur.Clone();
                updated.Ctrl = _ctrl.Checked;
                updated.Shift = _shift.Checked;
                updated.Alt = _alt.Checked;
                Emit(updated);
            };

        foreach (var m in Enum.GetValues<MouseAction>())
        {
            var b = Flat(KeyLabels.Mouse(m));
            b.Tag = m;
            b.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Mouse, Mouse = m });
            _mouseButtons.Add(b);
        }
        _autoLeft.Click += (_, _) => EmitAutoClick(MouseAction.Left);
        _autoRight.Click += (_, _) => EmitAutoClick(MouseAction.Right);
        _autoRate.ValueChanged += (_, _) =>
        {
            if (_loading || _current is not { Type: ActionType.AutoClickToggle } cur) return;
            EmitAutoClick(cur.Mouse);
        };
        _disable.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Disabled });
        _reset.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Default });
        _cancel.Click += (_, _) => CancelClicked?.Invoke();
        _cancel.Visible = false;

        _escapeCancels.CheckedChanged += (_, _) =>
        {
            _keyboard.DisabledKeys = _escapeCancels.Checked ? new[] { Keys.Escape } : Array.Empty<Keys>();
            if (!_loading) EscapeCancelsChanged?.Invoke(_escapeCancels.Checked);
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
        top.Controls.AddRange(new Control[] { _escapeCancels, _cancel });

        var hint = Dim("Or click instead:  hold with");
        var modRow = Row(hint, _ctrl, _shift, _alt, Dim("then click a key above"));
        var mouseRow = Row(Dim("Mouse:"));
        mouseRow.Controls.AddRange(_mouseButtons
            .Where(b => (MouseAction)b.Tag! is not (MouseAction.DpiUp or MouseAction.DpiDown)).ToArray<Control>());
        var autoRow = Row(Dim("Toggle (press once = start, again = stop):"), _autoLeft, _autoRight, _autoRate, Dim("clicks/sec"));
        var otherRow = Row(Dim("Other:"));
        otherRow.Controls.AddRange(_mouseButtons
            .Where(b => (MouseAction)b.Tag! is MouseAction.DpiUp or MouseAction.DpiDown).ToArray<Control>());
        otherRow.Controls.Add(_disable);
        otherRow.Controls.Add(_reset);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        bottom.Controls.AddRange(new Control[] { modRow, mouseRow, autoRow, otherRow });

        Controls.Add(_keyboard);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_header);
        ShowNothingSelected();
    }

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
        _header.Text = "Click a button on the device above, then press the key or combo you want.";
        _header.ForeColor = Theme.TextDim;
        _cancel.Visible = false;
        SetEnabled(false);
        _keyboard.ChosenKey = null;
        _keyboard.HeldKeys = Array.Empty<Keys>();
        HighlightMouse(null);
    }

    /// <summary>Recording: show the combo built so far and the keys currently held.</summary>
    public void ShowRecording(string label, KeyAction soFar, IReadOnlyCollection<Keys> held)
    {
        var combo = soFar.Key == null ? "" : KeyLabels.Action(soFar).Replace("+", " + ");
        var cancelHint = EscapeCancels ? "Esc to cancel" : "click Cancel to stop";
        _header.Text = combo.Length == 0
            ? $"{label}:  press a key or combo…   ({cancelHint})"
            : $"{label}:  {combo}   (let go to set it)";
        _header.ForeColor = Theme.Recording;
        _cancel.Visible = true;
        _keyboard.HeldKeys = held;
        _keyboard.ChosenKey = null;
        HighlightMouse(null);
    }

    /// <summary>Show the button being edited and what it currently does.</summary>
    /// <param name="setHere">The action set in this profile, or null if it inherits.</param>
    /// <param name="effective">What the button actually does right now (for display).</param>
    /// <param name="source">Where <paramref name="effective"/> comes from.</param>
    public void ShowButton(string label, KeyAction? setHere, KeyAction effective, BindingSource source)
    {
        _hasButton = true;
        _current = setHere;
        var from = source switch
        {
            BindingSource.Custom => "",
            BindingSource.Inherited => "  (from Default profile)",
            _ => "  (factory)",
        };
        _header.Text = $"{label}  →  {KeyLabels.Action(effective)}{from}      click the button again to re-record";
        _header.ForeColor = source == BindingSource.Custom ? Theme.Custom : source == BindingSource.Inherited ? Theme.Inherited : Theme.Text;
        _cancel.Visible = false;
        SetEnabled(true);

        _loading = true;
        _ctrl.Checked = effective.Type == ActionType.Key && effective.Ctrl;
        _shift.Checked = effective.Type == ActionType.Key && effective.Shift;
        _alt.Checked = effective.Type == ActionType.Key && effective.Alt;
        _loading = false;

        _keyboard.HeldKeys = Array.Empty<Keys>();
        _keyboard.ChosenKey = effective.Type == ActionType.Key && Enum.TryParse<Keys>(effective.Key, out var k) ? k : null;
        HighlightMouse(effective.Type == ActionType.Mouse ? effective.Mouse : null);
        _disable.BackColor = effective.Type == ActionType.Disabled ? ChosenFill : Theme.Key;
        var auto = effective.Type == ActionType.AutoClickToggle;
        _autoLeft.BackColor = auto && effective.Mouse == MouseAction.Left ? ChosenFill : Theme.Key;
        _autoRight.BackColor = auto && effective.Mouse == MouseAction.Right ? ChosenFill : Theme.Key;
        _loading = true;
        if (auto) _autoRate.Value = Math.Clamp(effective.ClicksPerSecond, (int)_autoRate.Minimum, (int)_autoRate.Maximum);
        _loading = false;
        _reset.Enabled = setHere != null;
    }

    private void EmitAutoClick(MouseAction button) => Emit(new KeyAction
    {
        Type = ActionType.AutoClickToggle,
        Mouse = button,
        ClicksPerSecond = (int)_autoRate.Value,
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
        foreach (Control c in new Control[] { _ctrl, _shift, _alt, _disable, _reset, _autoLeft, _autoRight, _autoRate }) c.Enabled = on;
        foreach (var b in _mouseButtons) b.Enabled = on;
    }

    private void HighlightMouse(MouseAction? m)
    {
        foreach (var b in _mouseButtons)
            b.BackColor = m.HasValue && (MouseAction)b.Tag! == m.Value ? ChosenFill : Theme.Key;
    }

    private static Label Dim(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(0, 8, 4, 0) };

    private static CheckBox Toggle(string text) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        AutoSize = false,
        Size = new Size(70, 30),
        TextAlign = ContentAlignment.MiddleCenter,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Key,
        ForeColor = Theme.Text,
        FlatAppearance = { CheckedBackColor = ChosenFill, BorderColor = Theme.Outline },
    };

    private static Button Flat(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(0, 30),
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Key,
        ForeColor = Theme.Text,
        FlatAppearance = { BorderColor = Theme.Outline },
        Margin = new Padding(3),
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(4, 2, 4, 2) };
        p.Controls.AddRange(controls);
        return p;
    }
}
