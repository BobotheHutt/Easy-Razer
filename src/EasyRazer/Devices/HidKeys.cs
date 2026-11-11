namespace EasyRazer.Devices;

/// <summary>
/// Maps Windows Forms key names to USB HID keyboard usage IDs (HID Usage Tables, page 0x07).
/// Device firmware stores keys as HID usages, so this is what gets written to the device.
/// </summary>
public static class HidKeys
{
    private static readonly Dictionary<Keys, byte> Map = Build();

    public static IReadOnlyCollection<Keys> SupportedKeys => Map.Keys;

    public static bool TryGetUsage(string? keyName, out byte usage)
    {
        usage = 0;
        return keyName != null
               && Enum.TryParse<Keys>(keyName, ignoreCase: true, out var k)
               && Map.TryGetValue(k, out usage);
    }

    public static bool IsSupported(Keys k) => Map.ContainsKey(k);

    private static readonly Dictionary<byte, Keys> Reverse = Map
        .GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    /// <summary>The key a HID usage stands for, if it's one we know.</summary>
    public static bool TryGetKey(byte usage, out Keys key) => Reverse.TryGetValue(usage, out key);

    /// <summary>Modifier bitmask as used in HID boot reports: Ctrl=1, Shift=2, Alt=4.</summary>
    public static byte ModifierMask(bool ctrl, bool shift, bool alt) =>
        (byte)((ctrl ? 0x01 : 0) | (shift ? 0x02 : 0) | (alt ? 0x04 : 0));

    private static Dictionary<Keys, byte> Build()
    {
        var m = new Dictionary<Keys, byte>();
        for (var i = 0; i < 26; i++) m[Keys.A + i] = (byte)(0x04 + i);
        for (var i = 1; i <= 9; i++) m[Keys.D0 + i] = (byte)(0x1E + i - 1);
        m[Keys.D0] = 0x27;
        m[Keys.Enter] = 0x28;
        m[Keys.Escape] = 0x29;
        m[Keys.Back] = 0x2A;
        m[Keys.Tab] = 0x2B;
        m[Keys.Space] = 0x2C;
        m[Keys.OemMinus] = 0x2D;
        m[Keys.Oemplus] = 0x2E;
        m[Keys.OemOpenBrackets] = 0x2F;
        m[Keys.OemCloseBrackets] = 0x30;
        m[Keys.OemPipe] = 0x31;
        m[Keys.OemSemicolon] = 0x33;
        m[Keys.OemQuotes] = 0x34;
        m[Keys.Oemtilde] = 0x35;
        m[Keys.Oemcomma] = 0x36;
        m[Keys.OemPeriod] = 0x37;
        m[Keys.OemQuestion] = 0x38;
        m[Keys.CapsLock] = 0x39;
        for (var i = 0; i < 12; i++) m[Keys.F1 + i] = (byte)(0x3A + i);
        m[Keys.PrintScreen] = 0x46;
        m[Keys.Scroll] = 0x47;
        m[Keys.Pause] = 0x48;
        m[Keys.Insert] = 0x49;
        m[Keys.Home] = 0x4A;
        m[Keys.PageUp] = 0x4B;
        m[Keys.Delete] = 0x4C;
        m[Keys.End] = 0x4D;
        m[Keys.PageDown] = 0x4E;
        m[Keys.Right] = 0x4F;
        m[Keys.Left] = 0x50;
        m[Keys.Down] = 0x51;
        m[Keys.Up] = 0x52;
        m[Keys.NumLock] = 0x53;
        m[Keys.Divide] = 0x54;
        m[Keys.Multiply] = 0x55;
        m[Keys.Subtract] = 0x56;
        m[Keys.Add] = 0x57;
        for (var i = 1; i <= 9; i++) m[Keys.NumPad0 + i] = (byte)(0x59 + i - 1);
        m[Keys.NumPad0] = 0x62;
        m[Keys.Decimal] = 0x63;
        m[Keys.Apps] = 0x65;
        for (var i = 0; i < 12; i++) m[Keys.F13 + i] = (byte)(0x68 + i);
        m[Keys.LControlKey] = 0xE0;
        m[Keys.LShiftKey] = 0xE1;
        m[Keys.LMenu] = 0xE2;
        m[Keys.LWin] = 0xE3;
        m[Keys.RControlKey] = 0xE4;
        m[Keys.RShiftKey] = 0xE5;
        m[Keys.RMenu] = 0xE6;
        m[Keys.RWin] = 0xE7;
        return m;
    }
}
