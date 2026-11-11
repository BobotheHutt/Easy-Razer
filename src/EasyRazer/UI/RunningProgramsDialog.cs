using System.Diagnostics;
using EasyRazer.Core;
using EasyRazer.UI.Modern;

namespace EasyRazer.UI;

/// <summary>Lists programs that currently have a window, so you can link a game without knowing its exe name.</summary>
public sealed class RunningProgramsDialog : Form
{
    private readonly ModernList _list = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, BackColor = Ui.Card };

    public IReadOnlyList<string> SelectedExes =>
        _list.SelectedItems.Cast<Entry>().Select(e => e.Exe).ToList();

    private sealed record Entry(string Exe, string Title);

    public RunningProgramsDialog()
    {
        Text = "Add a running game";
        Size = new Size(560, 600);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Ui.Card;
        ForeColor = Ui.Text;
        Font = Ui.Font(10f);
        Ui.DarkChrome(this, Ui.Card);

        _list.Describe = o => { var e = (Entry)o; return (e.Title, e.Exe); };
        _list.DoubleClick += (_, _) => { if (_list.SelectedItems.Count > 0) { DialogResult = DialogResult.OK; Close(); } };

        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Ui.Card, Padding = new Padding(22, 16, 22, 0) };
        header.Controls.Add(new TextLabel("Start the game first, then pick it here (the game, not its launcher).", 9.5f, color: Ui.TextDim) { Location = new Point(22, 50) });
        header.Controls.Add(new TextLabel("Add a running game", 13f, semibold: true) { Location = new Point(20, 16) });

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(14, 0, 14, 8) };
        listHost.Controls.Add(_list);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 14, 16, 14), BackColor = Ui.Sidebar };
        var ok = new ModernButton { Text = "Add", Kind = ButtonKind.Primary, DialogResult = DialogResult.OK, Margin = new Padding(8, 0, 0, 0) };
        var cancel = new ModernButton { Text = "Cancel", Kind = ButtonKind.Ghost, DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        bar.Controls.Add(ok);
        bar.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(listHost);
        Controls.Add(header);
        Controls.Add(bar);
        Load += (_, _) => Fill();
    }

    private void Fill()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Entry>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    var exe = ForegroundWatcher.ExeForPid((uint)p.Id) ?? (p.ProcessName.ToLowerInvariant() + ".exe");
                    if (exe == "easyrazer.exe" || !seen.Add(exe)) continue;
                    rows.Add(new Entry(exe, p.MainWindowTitle));
                }
                catch
                {
                    // process exited or access denied; skip
                }
            }
        }
        foreach (var r in rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)) _list.Items.Add(r);
    }
}
