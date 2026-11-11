using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using EasyRazer.Config;
using EasyRazer.Core;

namespace EasyRazer.UI;

/// <summary>The app's whole presence: a tray icon and a menu. The settings window opens on demand.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ProfileEngine _engine;
    private readonly ToolStripMenuItem _profileItem;
    private readonly ToolStripMenuItem _pauseItem;
    private SettingsForm? _settings;
    private Icon? _currentIcon;

    public TrayContext(AppConfig config, bool openSettings)
    {
        _engine = new ProfileEngine(config);

        _profileItem = new ToolStripMenuItem("Profile: starting…") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Pause automatic switching", null, (_, _) => TogglePause())
        {
            Checked = config.PauseSwitching,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_profileItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Re-apply color", null, async (_, _) => await _engine.ReapplyLightingAsync());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Text = "Easy Razer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
        UpdateIcon();

        _engine.ProfileApplied += p =>
        {
            _profileItem.Text = $"Profile: {p.Name}";
            _tray.Text = Truncate($"Easy Razer — {p.Name}", 63);
        };
        _engine.StatusChanged += () => _settings?.RefreshStatus();

        // Start after the message loop is running so the foreground hook and timers have a loop to use.
        var startTimer = new System.Windows.Forms.Timer { Interval = 50 };
        startTimer.Tick += async (_, _) =>
        {
            startTimer.Stop();
            startTimer.Dispose();
            await _engine.StartAsync();
            if (openSettings) ShowSettings();
        };
        startTimer.Start();
    }

    private void ShowSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(_engine);
        _settings.Saved += UpdateIcon;
        _settings.Show();
    }

    private void TogglePause()
    {
        _pauseItem.Checked = !_pauseItem.Checked;
        _engine.SetPaused(_pauseItem.Checked);
    }

    private static void OpenLog()
    {
        if (!File.Exists(Log.FilePath)) Log.Info("Log opened");
        Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
    }

    /// <summary>Tray icon is a dot in the current lighting color.</summary>
    private void UpdateIcon()
    {
        var color = ColorTranslator.FromHtml(_engine.Config.Color);
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var fill = new SolidBrush(color);
            using var ring = new Pen(System.Drawing.Color.FromArgb(40, 40, 40), 3);
            g.FillEllipse(fill, 3, 3, 26, 26);
            g.DrawEllipse(ring, 3, 3, 26, 26);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);

        _tray.Icon = icon;
        _currentIcon?.Dispose();
        _currentIcon = icon;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    protected override void ExitThreadCore()
    {
        Log.Info("Easy Razer exiting");
        _tray.Visible = false;
        _tray.Dispose();
        _engine.Dispose();
        _settings?.Close();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
