using EasyRazer.Config;
using EasyRazer.Devices;

namespace EasyRazer.UI;

/// <summary>Pick what one button does: default, disabled, a key (with modifiers) or a mouse action.</summary>
public sealed class BindDialog : Form
{
    private readonly RadioButton _default = new() { Text = "Default", AutoSize = true };
    private readonly RadioButton _disabled = new() { Text = "Disabled (does nothing)", AutoSize = true };
    private readonly RadioButton _key = new() { Text = "Keyboard key", AutoSize = true };
    private readonly RadioButton _mouse = new() { Text = "Mouse action", AutoSize = true };

    private readonly TextBox _capture = new() { ReadOnly = true, Width = 160, Text = "click here, then press a key" };
    private readonly ComboBox _keyList = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _ctrl = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox _shift = new() { Text = "Shift", AutoSize = true };
    private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
    private readonly ComboBox _mouseList = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };

    public KeyAction Result { get; private set; }

    public BindDialog(string buttonLabel, KeyAction current, KeyAction factory)
    {
        Result = current.Clone();
        Text = $"Bind {buttonLabel}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _default.Text = $"Default ({factory})";

        foreach (var k in HidKeys.SupportedKeys.OrderBy(k => k.ToString())) _keyList.Items.Add(k.ToString());
        foreach (var m in Enum.GetValues<MouseAction>()) _mouseList.Items.Add(m);

        _capture.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            var key = e.KeyCode;
            if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu) return; // use the checkboxes for those
            if (!HidKeys.IsSupported(key))
            {
                _capture.Text = $"{key} isn't supported";
                return;
            }
            _keyList.SelectedItem = key.ToString();
            _key.Checked = true;
        };
        _capture.PreviewKeyDown += (_, e) => e.IsInputKey = true; // let Tab/arrows reach KeyDown
        _keyList.SelectedIndexChanged += (_, _) =>
        {
            _capture.Text = _keyList.SelectedItem?.ToString() ?? "";
            _key.Checked = true;
        };
        _mouseList.SelectedIndexChanged += (_, _) => _mouse.Checked = true;

        var keyRow = Row(_capture, new Label { Text = "or", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _keyList);
        var modRow = Row(_ctrl, _shift, _alt);
        modRow.Padding = new Padding(20, 0, 0, 0);
        keyRow.Padding = new Padding(20, 0, 0, 0);
        var mouseRow = Row(_mouseList);
        mouseRow.Padding = new Padding(20, 0, 0, 0);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Result = Build();
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        layout.Controls.AddRange(new Control[]
        {
            _default, _disabled, _key, keyRow, modRow, _mouse, mouseRow, Row(ok, cancel),
        });
        Controls.Add(layout);

        // Load current value.
        switch (current.Type)
        {
            case ActionType.Disabled: _disabled.Checked = true; break;
            case ActionType.Key:
                _keyList.SelectedItem = current.Key;
                _ctrl.Checked = current.Ctrl;
                _shift.Checked = current.Shift;
                _alt.Checked = current.Alt;
                _key.Checked = true;
                break;
            case ActionType.Mouse:
                _mouseList.SelectedItem = current.Mouse;
                _mouse.Checked = true;
                break;
            default: _default.Checked = true; break;
        }
    }

    private KeyAction Build()
    {
        if (_disabled.Checked) return new KeyAction { Type = ActionType.Disabled };
        if (_key.Checked && _keyList.SelectedItem is string key)
            return new KeyAction { Type = ActionType.Key, Key = key, Ctrl = _ctrl.Checked, Shift = _shift.Checked, Alt = _alt.Checked };
        if (_mouse.Checked && _mouseList.SelectedItem is MouseAction m)
            return new KeyAction { Type = ActionType.Mouse, Mouse = m };
        return new KeyAction { Type = ActionType.Default };
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        p.Controls.AddRange(controls);
        return p;
    }
}
