namespace EasyRazer.UI.Modern;

/// <summary>Dark replacements for MessageBox and a text prompt.</summary>
public static class ModernDialog
{
    public static string? Prompt(IWin32Window owner, string title, string message, string initial)
    {
        using var f = Shell(title, message, 150);
        var input = new InputBox { Width = 400, Location = new Point(24, 92), Text = initial };
        f.Controls.Add(input);
        var ok = AddButtons(f, "OK", "Cancel", primaryResult: DialogResult.OK);
        f.Shown += (_, _) => { input.Box.Focus(); input.Box.SelectAll(); };
        f.AcceptButton = ok;
        return f.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }

    public static bool Confirm(IWin32Window owner, string title, string message, string yes, bool danger = false)
    {
        using var f = Shell(title, message, 110);
        var ok = AddButtons(f, yes, "Cancel", DialogResult.Yes, danger);
        f.AcceptButton = ok;
        return f.ShowDialog(owner) == DialogResult.Yes;
    }

    /// <summary>Yes / No / Cancel. Returns Yes, No or Cancel.</summary>
    public static DialogResult Ask(IWin32Window owner, string title, string message, string yes, string no)
    {
        using var f = Shell(title, message, 110);
        var ok = AddButtons(f, yes, "Cancel", DialogResult.Yes, extra: (no, DialogResult.No));
        f.AcceptButton = ok;
        var r = f.ShowDialog(owner);
        return r is DialogResult.Yes or DialogResult.No ? r : DialogResult.Cancel;
    }

    public static void Info(IWin32Window owner, string title, string message)
    {
        using var f = Shell(title, message, 110);
        var ok = AddButtons(f, "OK", null, DialogResult.OK);
        f.AcceptButton = ok;
        f.ShowDialog(owner);
    }

    private static Form Shell(string title, string message, int bodyHeight)
    {
        var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(448, bodyHeight + 64),
            BackColor = Ui.Card,
            ForeColor = Ui.Text,
            Font = Ui.Font(10f),
            KeyPreview = true,
        };
        Ui.DarkChrome(f, Ui.Card);
        f.Controls.Add(new TextLabel(title, 13f, semibold: true) { Location = new Point(22, 18) });
        f.Controls.Add(new TextLabel(message, 10f, color: Ui.TextDim) { Location = new Point(24, 52), MaximumSize = new Size(400, 0) });
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { f.DialogResult = DialogResult.Cancel; } };
        return f;
    }

    private static ModernButton AddButtons(Form f, string primary, string? cancel, DialogResult primaryResult,
        bool danger = false, (string Text, DialogResult Result)? extra = null)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 14, 16, 14),
            BackColor = Ui.Sidebar,
        };
        var ok = new ModernButton { Text = primary, Kind = danger ? ButtonKind.Danger : ButtonKind.Primary, DialogResult = primaryResult, Margin = new Padding(8, 0, 0, 0) };
        bar.Controls.Add(ok);
        if (extra is { } x) bar.Controls.Add(new ModernButton { Text = x.Text, DialogResult = x.Result, Margin = new Padding(8, 0, 0, 0) });
        if (cancel != null) bar.Controls.Add(new ModernButton { Text = cancel, Kind = ButtonKind.Ghost, DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) });
        f.Controls.Add(bar);
        return ok;
    }
}
