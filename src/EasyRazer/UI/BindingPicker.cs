using EasyRazer.Config;

namespace EasyRazer.UI;

/// <summary>
/// Everything a button can be set to, all clickable: an on-screen keyboard, Ctrl/Shift/Alt toggles,
/// mouse actions, Disable and Reset. Raises <see cref="ActionChosen"/> on every click.
/// </summary>
public sealed class BindingPicker : UserControl
{
    private readonly Label _header = new()
    {
        Dock = DockStyle.Top,
        Height = 30,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(4, 0, 0, 0),
    };
    private readonly KeyboardView _keyboard = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _ctrl = Toggle("Ctrl");
    private readonly CheckBox _shift = Toggle("Shift");
    private readonly CheckBox _alt = Toggle("Alt");
    private readonly List<Button> _mouseButtons = new();
    private readonly Button _disable = Flat("Disable");
    private readonly Button _reset = Flat("Reset to default");

    private KeyAction? _current; // what's set in this profile (null = not set)
    private bool _loading;

    public event Action<KeyAction>? ActionChosen;

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
        _disable.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Disabled });
        _reset.Click += (_, _) => Emit(new KeyAction { Type = ActionType.Default });

        var modRow = Row(new Label { Text = "Hold with key:", AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(0, 8, 4, 0) },
            _ctrl, _shift, _alt,
            new Label { Text = "   (turn these on, then click a key)", AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(0, 8, 0, 0) });
        var mouseRow = Row(new Label { Text = "Mouse:", AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(0, 8, 4, 0) });
        mouseRow.Controls.AddRange(_mouseButtons.ToArray<Control>());
        var otherRow = Row(_disable, _reset);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        bottom.Controls.AddRange(new Control[] { modRow, mouseRow, otherRow });

        Controls.Add(_keyboard);
        Controls.Add(bottom);
        Controls.Add(_header);
        ShowNothingSelected();
    }

    public void ShowNothingSelected()
    {
        _current = null;
        _header.Text = "Click a button on the device above to change it.";
        _header.ForeColor = Theme.TextDim;
        SetEnabled(false);
        _keyboard.ChosenKey = null;
        HighlightMouse(null);
    }

    /// <summary>Show the button being edited and what it currently does.</summary>
    /// <param name="setHere">The action set in this profile, or null if it inherits.</param>
    /// <param name="effective">What the button actually does right now (for display).</param>
    /// <param name="source">Where <paramref name="effective"/> comes from.</param>
    public void ShowButton(string label, KeyAction? setHere, KeyAction effective, BindingSource source)
    {
        _current = setHere;
        var from = source switch
        {
            BindingSource.Custom => "",
            BindingSource.Inherited => "  (from Default profile)",
            _ => "  (factory)",
        };
        _header.Text = $"{label}  →  {KeyLabels.Action(effective)}{from}";
        _header.ForeColor = source == BindingSource.Custom ? Theme.Custom : source == BindingSource.Inherited ? Theme.Inherited : Theme.Text;
        SetEnabled(true);

        _loading = true;
        _ctrl.Checked = effective.Type == ActionType.Key && effective.Ctrl;
        _shift.Checked = effective.Type == ActionType.Key && effective.Shift;
        _alt.Checked = effective.Type == ActionType.Key && effective.Alt;
        _loading = false;

        _keyboard.ChosenKey = effective.Type == ActionType.Key && Enum.TryParse<Keys>(effective.Key, out var k) ? k : null;
        HighlightMouse(effective.Type == ActionType.Mouse ? effective.Mouse : null);
        _disable.BackColor = effective.Type == ActionType.Disabled ? Color.FromArgb(40, 90, 30) : Theme.Key;
        _reset.Enabled = setHere != null;
    }

    private void Emit(KeyAction action)
    {
        _current = action.Type == ActionType.Default ? null : action;
        ActionChosen?.Invoke(action);
    }

    private void SetEnabled(bool on)
    {
        _keyboard.Enabled = on;
        foreach (Control c in new Control[] { _ctrl, _shift, _alt, _disable, _reset }) c.Enabled = on;
        foreach (var b in _mouseButtons) b.Enabled = on;
    }

    private void HighlightMouse(MouseAction? m)
    {
        foreach (var b in _mouseButtons)
            b.BackColor = m.HasValue && (MouseAction)b.Tag! == m.Value ? Color.FromArgb(40, 90, 30) : Theme.Key;
    }

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
        FlatAppearance = { CheckedBackColor = Color.FromArgb(40, 90, 30), BorderColor = Theme.Outline },
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
