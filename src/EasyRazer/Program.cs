using EasyRazer.Config;
using EasyRazer.Core;
using EasyRazer.UI;

namespace EasyRazer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\EasyRazer.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Easy Razer is already running — look for its icon in the system tray.",
                "Easy Razer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        var config = ConfigStore.Load();
        var firstRun = !File.Exists(ConfigStore.FilePath);
        if (firstRun) ConfigStore.Save(config);

        var openSettings = firstRun || !args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayContext(config, openSettings));
    }
}
