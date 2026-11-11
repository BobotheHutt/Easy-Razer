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
            // Already running: ask that copy to bring its window up instead.
            try { EventWaitHandle.OpenExisting(TrayContext.ShowWindowSignal).Set(); } catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        // Make sure everything created below (engine, tray) posts its updates to this UI thread.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        var config = ConfigStore.Load();
        var firstRun = !File.Exists(ConfigStore.FilePath);
        if (firstRun) ConfigStore.Save(config);

        var openSettings = firstRun || !args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        var justUpdated = args.Contains("--updated", StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayContext(config, openSettings, justUpdated));
    }
}
