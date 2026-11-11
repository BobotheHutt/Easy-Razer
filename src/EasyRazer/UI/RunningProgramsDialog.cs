using System.Diagnostics;
using EasyRazer.Core;

namespace EasyRazer.UI;

/// <summary>Lists programs that currently have a window, so you can link a game without knowing its exe name.</summary>
public sealed class RunningProgramsDialog : Form
{
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        Dock = DockStyle.Fill,
    };

    public IReadOnlyList<string> SelectedExes =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();

    public RunningProgramsDialog()
    {
        Text = "Pick a running game";
        Size = new Size(560, 460);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        _list.Columns.Add("Program", 180);
        _list.Columns.Add("Window title", 340);
        _list.DoubleClick += (_, _) => { if (_list.SelectedItems.Count > 0) { DialogResult = DialogResult.OK; Close(); } };

        var hint = new Label
        {
            Text = "Start the game first, then pick it here. Pick the game itself, not its launcher.",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(4, 6, 4, 0),
        };
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 38 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(buttons);
        Load += (_, _) => Fill();
    }

    private void Fill()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ListViewItem>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    var exe = ForegroundWatcher.ExeForPid((uint)p.Id) ?? (p.ProcessName.ToLowerInvariant() + ".exe");
                    if (exe == "easyrazer.exe" || !seen.Add(exe)) continue;
                    rows.Add(new ListViewItem(new[] { exe, p.MainWindowTitle }));
                }
                catch
                {
                    // process exited or access denied; skip
                }
            }
        }
        _list.Items.AddRange(rows.OrderBy(r => r.Text).ToArray());
    }
}
