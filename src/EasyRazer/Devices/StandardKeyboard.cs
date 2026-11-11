using System.Drawing;

namespace EasyRazer.Devices;

/// <summary>One key of the standard full-size layout, in key units (1 = one normal key).</summary>
public sealed record KeyCap(Keys Key, RectangleF Bounds);

/// <summary>Full-size keyboard layout (plus F13–F24), shared by the on-screen keyboard and keyboard drawings.</summary>
public static class StandardKeyboard
{
    public static IReadOnlyList<KeyCap> Caps { get; } = Build();

    private static List<KeyCap> Build()
    {
        var caps = new List<KeyCap>();
        void Add(Keys k, float x, float y, float w = 1, float h = 1)
        {
            if (HidKeys.IsSupported(k)) caps.Add(new KeyCap(k, new RectangleF(x, y, w, h)));
        }
        void RowOf(float x, float y, params Keys[] keys)
        {
            foreach (var k in keys) { Add(k, x, y); x += 1; }
        }

        // F13–F24 (handy for binds that never clash with anything in a game)
        for (var i = 0; i < 12; i++) Add(Keys.F13 + i, 2 + i + (i / 4) * 0.5f, 0);

        const float f = 1.25f;
        Add(Keys.Escape, 0, f);
        for (var i = 0; i < 12; i++) Add(Keys.F1 + i, 2 + i + (i / 4) * 0.5f, f);
        RowOf(15.5f, f, Keys.PrintScreen, Keys.Scroll, Keys.Pause);

        const float r1 = 2.5f;
        RowOf(0, r1, Keys.Oemtilde, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
            Keys.D0, Keys.OemMinus, Keys.Oemplus);
        Add(Keys.Back, 13, r1, 2);
        RowOf(15.5f, r1, Keys.Insert, Keys.Home, Keys.PageUp);
        RowOf(19, r1, Keys.NumLock, Keys.Divide, Keys.Multiply, Keys.Subtract);

        const float r2 = 3.5f;
        Add(Keys.Tab, 0, r2, 1.5f);
        RowOf(1.5f, r2, Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I, Keys.O, Keys.P,
            Keys.OemOpenBrackets, Keys.OemCloseBrackets);
        Add(Keys.OemPipe, 13.5f, r2, 1.5f);
        RowOf(15.5f, r2, Keys.Delete, Keys.End, Keys.PageDown);
        RowOf(19, r2, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9);
        Add(Keys.Add, 22, r2, 1, 2);

        const float r3 = 4.5f;
        Add(Keys.CapsLock, 0, r3, 1.75f);
        RowOf(1.75f, r3, Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K, Keys.L,
            Keys.OemSemicolon, Keys.OemQuotes);
        Add(Keys.Enter, 12.75f, r3, 2.25f);
        RowOf(19, r3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6);

        const float r4 = 5.5f;
        Add(Keys.LShiftKey, 0, r4, 2.25f);
        RowOf(2.25f, r4, Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M, Keys.Oemcomma, Keys.OemPeriod,
            Keys.OemQuestion);
        Add(Keys.RShiftKey, 12.25f, r4, 2.75f);
        Add(Keys.Up, 16.5f, r4);
        RowOf(19, r4, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3);

        const float r5 = 6.5f;
        Add(Keys.LControlKey, 0, r5, 1.25f);
        Add(Keys.LWin, 1.25f, r5, 1.25f);
        Add(Keys.LMenu, 2.5f, r5, 1.25f);
        Add(Keys.Space, 3.75f, r5, 6.25f);
        Add(Keys.RMenu, 10, r5, 1.25f);
        Add(Keys.RWin, 11.25f, r5, 1.25f);
        Add(Keys.Apps, 12.5f, r5, 1.25f);
        Add(Keys.RControlKey, 13.75f, r5, 1.25f);
        RowOf(15.5f, r5, Keys.Left, Keys.Down, Keys.Right);
        Add(Keys.NumPad0, 19, r5, 2);
        Add(Keys.Decimal, 21, r5);

        return caps;
    }
}
