using System.Diagnostics;
using System.Runtime.InteropServices;
using EasyRazer.Config;
using EasyRazer.Core;
using EasyRazer.Devices;

namespace EasyRazer.UI;

/// <summary>
/// Records a key or key combo for a bind: every key pressed is added, and the bind is finished
/// once all keys are released. Escape can cancel.
///
/// Uses a keyboard hook so keys like Alt, Tab, the Windows key and Print Screen can be recorded
/// without Windows acting on them. The hook only exists while you're recording in the settings
/// window and is removed the moment recording ends, so it's never active while you play.
/// </summary>
public sealed class KeyRecorder : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr extra;
    }

    private readonly HookProc _proc;
    private IntPtr _hook;
    private readonly HashSet<Keys> _held = new();
    private readonly List<Keys> _pressedInOrder = new();

    public bool IsRecording => _hook != IntPtr.Zero;

    /// <summary>When true, pressing Escape cancels instead of being recorded.</summary>
    public bool EscapeCancels { get; set; } = true;

    /// <summary>Keys pressed so far changed; argument is the combo so far.</summary>
    public event Action<KeyAction, IReadOnlyCollection<Keys>>? Changed;
    /// <summary>All keys were released; argument is the finished combo.</summary>
    public event Action<KeyAction>? Completed;
    public event Action? Cancelled;

    public KeyRecorder()
    {
        _proc = HookCallback; // field keeps the delegate alive while the hook exists
    }

    public void Start()
    {
        Stop();
        _held.Clear();
        _pressedInOrder.Clear();
        using var cur = Process.GetCurrentProcess();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(cur.MainModule?.ModuleName), 0);
        if (_hook == IntPtr.Zero) Log.Error($"Couldn't start key recording (error {Marshal.GetLastWin32Error()})");
    }

    public void Cancel()
    {
        if (!IsRecording) return;
        Stop();
        Cancelled?.Invoke();
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _hook == IntPtr.Zero) return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var key = Normalize((Keys)info.vkCode);
        var msg = (int)wParam;

        if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            if (key == Keys.Escape && EscapeCancels)
            {
                Cancel();
                return (IntPtr)1;
            }
            if (_held.Add(key) && HidKeys.IsSupported(key)) // Add() is false for auto-repeat
            {
                _pressedInOrder.Remove(key);
                _pressedInOrder.Add(key);
                Changed?.Invoke(Build(), _held.ToList());
            }
        }
        else if (msg is WM_KEYUP or WM_SYSKEYUP)
        {
            _held.Remove(key);
            if (_held.Count == 0 && _pressedInOrder.Count > 0)
            {
                var result = Build();
                Stop();
                Completed?.Invoke(result);
            }
            else
            {
                Changed?.Invoke(Build(), _held.ToList());
            }
        }

        return (IntPtr)1; // swallow the key so Windows / the settings window don't act on it
    }

    /// <summary>
    /// Turn the pressed keys into one bind: Ctrl/Shift/Alt become modifiers and the last other key
    /// pressed is the main key. If only modifiers were pressed, the last one becomes the key itself.
    /// </summary>
    private KeyAction Build()
    {
        var mods = _pressedInOrder.Where(IsModifier).ToList();
        var main = _pressedInOrder.LastOrDefault(k => !IsModifier(k));
        if (main == Keys.None && mods.Count > 0)
        {
            main = mods[^1];
            mods.RemoveAt(mods.Count - 1);
        }
        return new KeyAction
        {
            Type = ActionType.Key,
            Key = main == Keys.None ? null : main.ToString(),
            Ctrl = mods.Any(k => k is Keys.LControlKey or Keys.RControlKey),
            Shift = mods.Any(k => k is Keys.LShiftKey or Keys.RShiftKey),
            Alt = mods.Any(k => k is Keys.LMenu or Keys.RMenu),
        };
    }

    private static bool IsModifier(Keys k) =>
        k is Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu;

    private static Keys Normalize(Keys k) => k switch
    {
        Keys.ControlKey => Keys.LControlKey,
        Keys.ShiftKey => Keys.LShiftKey,
        Keys.Menu => Keys.LMenu,
        Keys.Return => Keys.Enter,
        _ => k,
    };

    public void Dispose() => Stop();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
