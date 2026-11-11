using EasyRazer.Config;

namespace EasyRazer.UI;

/// <summary>Short, human-friendly names for keys and actions, for drawing on small key caps.</summary>
public static class KeyLabels
{
    private static readonly Dictionary<Keys, string> Names = new()
    {
        [Keys.Escape] = "Esc", [Keys.Back] = "Bksp", [Keys.Tab] = "Tab", [Keys.Enter] = "Enter",
        [Keys.CapsLock] = "Caps", [Keys.Space] = "Space", [Keys.Apps] = "Menu",
        [Keys.LShiftKey] = "LShift", [Keys.RShiftKey] = "RShift",
        [Keys.LControlKey] = "LCtrl", [Keys.RControlKey] = "RCtrl",
        [Keys.LMenu] = "LAlt", [Keys.RMenu] = "RAlt", [Keys.LWin] = "LWin", [Keys.RWin] = "RWin",
        [Keys.Oemtilde] = "`", [Keys.OemMinus] = "-", [Keys.Oemplus] = "=",
        [Keys.OemOpenBrackets] = "[", [Keys.OemCloseBrackets] = "]", [Keys.OemPipe] = "\\",
        [Keys.OemSemicolon] = ";", [Keys.OemQuotes] = "'", [Keys.Oemcomma] = ",",
        [Keys.OemPeriod] = ".", [Keys.OemQuestion] = "/",
        [Keys.PrintScreen] = "PrtSc", [Keys.Scroll] = "ScrLk", [Keys.Pause] = "Pause",
        [Keys.Insert] = "Ins", [Keys.Delete] = "Del", [Keys.Home] = "Home", [Keys.End] = "End",
        [Keys.PageUp] = "PgUp", [Keys.PageDown] = "PgDn",
        [Keys.Up] = "↑", [Keys.Down] = "↓", [Keys.Left] = "←", [Keys.Right] = "→",
        [Keys.NumLock] = "Num", [Keys.Divide] = "/", [Keys.Multiply] = "*",
        [Keys.Subtract] = "-", [Keys.Add] = "+", [Keys.Decimal] = ".",
    };

    public static string Key(Keys k)
    {
        if (Names.TryGetValue(k, out var n)) return n;
        if (k is >= Keys.D0 and <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
        if (k is >= Keys.NumPad0 and <= Keys.NumPad9) return ((int)(k - Keys.NumPad0)).ToString();
        return k.ToString();
    }

    /// <summary>Numpad keys get a prefix when shown outside the on-screen keyboard, so "Num 5" isn't confused with "5".</summary>
    public static string KeyLong(Keys k) =>
        k is >= Keys.NumPad0 and <= Keys.NumPad9 or Keys.Divide or Keys.Multiply or Keys.Subtract or Keys.Add or Keys.Decimal
            ? "Num " + Key(k)
            : Key(k);

    public static string Mouse(MouseAction m) => m switch
    {
        MouseAction.Left => "Left click",
        MouseAction.Right => "Right click",
        MouseAction.Middle => "Middle click",
        MouseAction.Back => "Mouse back",
        MouseAction.Forward => "Mouse fwd",
        MouseAction.ScrollUp => "Scroll up",
        MouseAction.ScrollDown => "Scroll down",
        MouseAction.DpiUp => "DPI up",
        MouseAction.DpiDown => "DPI down",
        _ => m.ToString(),
    };

    /// <summary>Short text for an action, as drawn on a device button.</summary>
    public static string Action(KeyAction a)
    {
        var text = ActionCore(a);
        return a.TurboRate is { } r && a.SupportsTurbo ? $"Turbo {text} {r}/s" : text;
    }

    private static string ActionCore(KeyAction a)
    {
        switch (a.Type)
        {
            case ActionType.Disabled: return "Off";
            case ActionType.Mouse: return Mouse(a.Mouse);
            case ActionType.Key:
                var main = Enum.TryParse<Keys>(a.Key, out var k) ? KeyLong(k) : a.Key ?? "?";
                var mods = (a.Ctrl ? "Ctrl+" : "") + (a.Shift ? "Shift+" : "") + (a.Alt ? "Alt+" : "");
                return mods + main;
            default: return "Default";
        }
    }
}
