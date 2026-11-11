using System.Runtime.InteropServices;
using System.Text;

namespace EasyRazer.Core;

/// <summary>
/// Gets told by Windows every time a different window comes to the front (SetWinEventHook).
/// No polling, no hooks into other processes, no window of our own required — this is what lets
/// profile switching work while the app sits in the tray.
/// Must be created on a thread with a message loop (the WinForms UI thread).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    private readonly WinEventDelegate _callback; // kept in a field so the GC can't collect it
    private readonly IntPtr _hookForeground;
    private readonly IntPtr _hookRestore;

    /// <summary>Raised with the lower-case exe file name of the new foreground window (e.g. "cs2.exe").</summary>
    public event Action<string>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;
        var flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
        _hookForeground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, flags);
        _hookRestore = SetWinEventHook(EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _callback, 0, 0, flags);
        if (_hookForeground == IntPtr.Zero)
            Log.Error("SetWinEventHook failed; automatic profile switching will not work");
    }

    /// <summary>Exe name of whatever is in front right now, or null.</summary>
    public static string? CurrentExe() => ExeForWindow(GetForegroundWindow());

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0) return; // only whole-window events
        var exe = ExeForWindow(hwnd);
        if (exe != null) ForegroundChanged?.Invoke(exe);
    }

    public static string? ExeForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : ExeForPid(pid);
    }

    public static string? ExeForPid(uint pid)
    {
        // "Limited information" access works even for games running as admin or protected by anti-cheat.
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size)
                ? Path.GetFileName(sb.ToString()).ToLowerInvariant()
                : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    public void Dispose()
    {
        if (_hookForeground != IntPtr.Zero) UnhookWinEvent(_hookForeground);
        if (_hookRestore != IntPtr.Zero) UnhookWinEvent(_hookRestore);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
