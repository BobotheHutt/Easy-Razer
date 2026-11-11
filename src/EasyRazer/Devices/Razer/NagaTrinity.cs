using EasyRazer.Config;
using static EasyRazer.Devices.LayoutShape;

namespace EasyRazer.Devices.Razer;

/// <summary>
/// Razer Naga Trinity mouse (USB 1532:0067).
/// Left click is deliberately not remappable so a bad profile can never leave you without a left click.
/// Side buttons follow the 12-button plate; with the 2- or 7-button plate, binds for missing buttons do nothing.
/// Slot numbers follow the Naga Pro table validated by OpenSnek (body 0x01..0x05, DPI buttons 0x0B/0x0C,
/// 12-button plate 0x40..0x4B). If the Trinity turns out to differ, "Learn keys" can override them.
/// </summary>
public sealed class NagaTrinity : RazerKeymapDevice
{
    public override string KindId => "naga-trinity";
    public override string DisplayName => "Naga Trinity";
    public override int ProductId => 0x0067;
    public override KeymapStyle Style => KeymapStyle.Mouse;

    public override IReadOnlyList<ButtonDef> Buttons { get; } = new List<ButtonDef>
    {
        new("right", "Right click", 0x02, M(MouseAction.Right)),
        new("middle", "Wheel click", 0x03, M(MouseAction.Middle)),
        new("under-wheel-1", "Under wheel (front)", 0x0B, M(MouseAction.DpiUp)),
        new("under-wheel-2", "Under wheel (back)", 0x0C, M(MouseAction.DpiDown)),
        new("side01", "Side 1", 0x40, K(Keys.D1)),
        new("side02", "Side 2", 0x41, K(Keys.D2)),
        new("side03", "Side 3", 0x42, K(Keys.D3)),
        new("side04", "Side 4", 0x43, K(Keys.D4)),
        new("side05", "Side 5", 0x44, K(Keys.D5)),
        new("side06", "Side 6", 0x45, K(Keys.D6)),
        new("side07", "Side 7", 0x46, K(Keys.D7)),
        new("side08", "Side 8", 0x47, K(Keys.D8)),
        new("side09", "Side 9", 0x48, K(Keys.D9)),
        new("side10", "Side 10", 0x49, K(Keys.D0)),
        new("side11", "Side 11", 0x4A, K(Keys.OemMinus)),
        new("side12", "Side 12", 0x4B, K(Keys.Oemplus)),
    };

    public override IReadOnlyList<LayoutShape> Layout { get; } = BuildLayout();

    private static List<LayoutShape> BuildLayout()
    {
        var shapes = new List<LayoutShape>
        {
            Deco("Top", 0, -9, 44, 6, ShapeKind.Label),
            Deco("", 0, 0, 44, 80, ShapeKind.Outline),
            Deco("Left click (fixed)", 2, 2, 15, 34, ShapeKind.Fixed),
            Btn("right", "Right click", 27, 2, 15, 34),
            Btn("middle", "Wheel click", 18, 3, 8, 15),
            Btn("under-wheel-1", "Front", 18, 20, 8, 7.5f),
            Btn("under-wheel-2", "Back", 18, 28.5f, 8, 7.5f),

            Deco("Side plate (12-button)", 56, -9, 46, 6, ShapeKind.Label),
            Deco("", 56, 6, 46, 60, ShapeKind.Outline),
        };
        var n = 1;
        for (var row = 0; row < 4; row++)
        for (var col = 0; col < 3; col++, n++)
            shapes.Add(Btn($"side{n:00}", n.ToString(), 58.5f + col * 14.5f, 9 + row * 14, 13, 12));
        return shapes;
    }
}
