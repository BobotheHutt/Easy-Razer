using System.Drawing;

namespace EasyRazer.Devices;

public enum ShapeKind
{
    /// <summary>A clickable button (rounded rectangle).</summary>
    Key,
    /// <summary>A clickable round button.</summary>
    Round,
    /// <summary>Non-clickable outline, e.g. the mouse body.</summary>
    Outline,
    /// <summary>Non-clickable greyed-out button, e.g. left click which can't be remapped.</summary>
    Fixed,
    /// <summary>Just text.</summary>
    Label,
}

/// <summary>
/// One piece of a device drawing. Coordinates are in arbitrary units; the view scales the whole drawing to fit.
/// Hand-drawn schematic, not product art.
/// </summary>
/// <param name="ButtonId">The <see cref="ButtonDef.Id"/> this shape sets, or null for decoration.</param>
/// <param name="Caption">Small label on the shape (e.g. "05").</param>
public sealed record LayoutShape(string? ButtonId, string Caption, RectangleF Bounds, ShapeKind Kind = ShapeKind.Key)
{
    public static LayoutShape Btn(string id, string caption, float x, float y, float w, float h, ShapeKind kind = ShapeKind.Key) =>
        new(id, caption, new RectangleF(x, y, w, h), kind);

    public static LayoutShape Deco(string caption, float x, float y, float w, float h, ShapeKind kind) =>
        new(null, caption, new RectangleF(x, y, w, h), kind);
}
