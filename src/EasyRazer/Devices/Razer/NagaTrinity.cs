using EasyRazer.Config;
using static EasyRazer.Devices.LayoutShape;

namespace EasyRazer.Devices.Razer;

/// <summary>
/// Razer Naga Trinity mouse (USB 1532:0067).
/// Left click is deliberately not remappable so a bad profile can never leave you without a left click.
/// Side buttons follow the 12-button plate; with the 2- or 7-button plate, binds for missing buttons do nothing.
/// Protocol button ids are null until confirmed by a USB capture (docs/CAPTURE.md).
/// </summary>
public sealed class NagaTrinity : RazerKeymapDevice
{
    public override string KindId => "naga-trinity";
    public override string DisplayName => "Naga Trinity";
    public override int ProductId => 0x0067;

    public override IReadOnlyList<ButtonDef> Buttons { get; } = new List<ButtonDef>
    {
        new("right", "Right click", null, M(MouseAction.Right)),
        new("middle", "Wheel click", null, M(MouseAction.Middle)),
        new("side01", "Side 1", null, K(Keys.D1)),
        new("side02", "Side 2", null, K(Keys.D2)),
        new("side03", "Side 3", null, K(Keys.D3)),
        new("side04", "Side 4", null, K(Keys.D4)),
        new("side05", "Side 5", null, K(Keys.D5)),
        new("side06", "Side 6", null, K(Keys.D6)),
        new("side07", "Side 7", null, K(Keys.D7)),
        new("side08", "Side 8", null, K(Keys.D8)),
        new("side09", "Side 9", null, K(Keys.D9)),
        new("side10", "Side 10", null, K(Keys.D0)),
        new("side11", "Side 11", null, K(Keys.OemMinus)),
        new("side12", "Side 12", null, K(Keys.Oemplus)),
    };

    public override IReadOnlyList<LayoutShape> Layout { get; } = BuildLayout();

    private static List<LayoutShape> BuildLayout()
    {
        var shapes = new List<LayoutShape>
        {
            Deco("Top", 0, -9, 44, 6, ShapeKind.Label),
            Deco("", 0, 0, 44, 80, ShapeKind.Outline),
            Deco("Left click (fixed)", 2, 2, 16, 30, ShapeKind.Fixed),
            Btn("right", "Right click", 26, 2, 16, 30),
            Btn("middle", "Wheel click", 18.5f, 6, 7, 20),

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
