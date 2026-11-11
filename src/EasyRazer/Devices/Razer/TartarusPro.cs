using EasyRazer.Config;
using static EasyRazer.Devices.LayoutShape;

namespace EasyRazer.Devices.Razer;

/// <summary>
/// Razer Tartarus Pro keypad (USB 1532:0244).
/// Key slot numbers aren't published, so they're learned on your device with "Learn keys"
/// (Settings → Devices). It's an analog-switch keypad, so it uses the analog keyboard command form.
/// Factory actions follow the printed key layout; confirm against the capture of Synapse's "reset to default".
/// </summary>
public sealed class TartarusPro : RazerKeymapDevice
{
    public override string KindId => "tartarus-pro";
    public override string DisplayName => "Tartarus Pro";
    public override int ProductId => 0x0244;
    public override KeymapStyle Style => KeymapStyle.AnalogKeyboard;

    public override IReadOnlyList<ButtonDef> Buttons { get; } = new List<ButtonDef>
    {
        new("key01", "Key 01", null, K(Keys.D1)),
        new("key02", "Key 02", null, K(Keys.D2)),
        new("key03", "Key 03", null, K(Keys.D3)),
        new("key04", "Key 04", null, K(Keys.D4)),
        new("key05", "Key 05", null, K(Keys.D5)),
        new("key06", "Key 06", null, K(Keys.Tab)),
        new("key07", "Key 07", null, K(Keys.Q)),
        new("key08", "Key 08", null, K(Keys.W)),
        new("key09", "Key 09", null, K(Keys.E)),
        new("key10", "Key 10", null, K(Keys.R)),
        new("key11", "Key 11", null, K(Keys.CapsLock)),
        new("key12", "Key 12", null, K(Keys.A)),
        new("key13", "Key 13", null, K(Keys.S)),
        new("key14", "Key 14", null, K(Keys.D)),
        new("key15", "Key 15", null, K(Keys.F)),
        new("key16", "Key 16", null, K(Keys.LShiftKey)),
        new("key17", "Key 17", null, K(Keys.Z)),
        new("key18", "Key 18", null, K(Keys.X)),
        new("key19", "Key 19", null, K(Keys.C)),
        new("key20", "Key 20 (thumb)", null, K(Keys.Space)),
        new("thumb-up", "Thumbpad up", null, K(Keys.Up)),
        new("thumb-down", "Thumbpad down", null, K(Keys.Down)),
        new("thumb-left", "Thumbpad left", null, K(Keys.Left)),
        new("thumb-right", "Thumbpad right", null, K(Keys.Right)),
        new("wheel-up", "Scroll up", null, M(MouseAction.ScrollUp)),
        new("wheel-down", "Scroll down", null, M(MouseAction.ScrollDown)),
        new("wheel-click", "Scroll click", null, M(MouseAction.Middle)),
    };

    public override IReadOnlyList<LayoutShape> Layout { get; } = BuildLayout();

    private static List<LayoutShape> BuildLayout()
    {
        const float key = 10f, pitch = 11.5f;
        var shapes = new List<LayoutShape> { Deco("", -4, -5, 92, 72, ShapeKind.Outline) };
        var n = 1;
        for (var row = 0; row < 4; row++)
        for (var col = 0; col < (row == 3 ? 4 : 5); col++, n++)
            shapes.Add(Btn($"key{n:00}", n.ToString("00"), col * pitch, row * pitch, key, key));

        shapes.Add(Btn("wheel-up", "Wheel up", 61, 0, 12, 7));
        shapes.Add(Btn("wheel-click", "Wheel click", 61, 8, 12, 7));
        shapes.Add(Btn("wheel-down", "Wheel down", 61, 16, 12, 7));

        shapes.Add(Btn("thumb-up", "Pad up", 63, 32, 10, 8));
        shapes.Add(Btn("thumb-left", "Pad left", 52, 41, 10, 8));
        shapes.Add(Btn("thumb-right", "Pad right", 74, 41, 10, 8));
        shapes.Add(Btn("thumb-down", "Pad down", 63, 50, 10, 8));
        shapes.Add(Btn("key20", "20 (thumb)", 46, 54, 14, 10));
        return shapes;
    }
}
