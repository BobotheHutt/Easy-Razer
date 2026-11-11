using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace EasyRazer.UI;

/// <summary>Colors and drawing helpers shared by the device picture and the on-screen keyboard.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 24, 27);
    public static readonly Color Outline = Color.FromArgb(70, 70, 78);
    public static readonly Color Key = Color.FromArgb(44, 44, 50);
    public static readonly Color KeyHover = Color.FromArgb(62, 62, 70);
    public static readonly Color KeyFixed = Color.FromArgb(34, 34, 38);
    public static readonly Color Text = Color.FromArgb(235, 235, 240);
    public static readonly Color TextDim = Color.FromArgb(150, 150, 160);
    public static readonly Color Selected = Color.FromArgb(255, 255, 255);

    /// <summary>Set in this profile.</summary>
    public static readonly Color Custom = Color.FromArgb(68, 214, 44);
    /// <summary>Inherited from the Default profile.</summary>
    public static readonly Color Inherited = Color.FromArgb(80, 160, 255);
    /// <summary>Key currently chosen on the on-screen keyboard.</summary>
    public static readonly Color Chosen = Color.FromArgb(68, 214, 44);
    /// <summary>Waiting for / showing keys being recorded.</summary>
    public static readonly Color Recording = Color.FromArgb(255, 200, 40);
}

public static class KeyCapRenderer
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    /// <summary>Draw text centered in a rectangle, shrinking the font until it fits.</summary>
    public static void FitText(Graphics g, string text, RectangleF r, Color color, float maxSize, FontStyle style = FontStyle.Regular)
    {
        if (string.IsNullOrEmpty(text) || r.Width < 4 || r.Height < 4) return;
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        var size = Math.Max(6f, maxSize);
        while (true)
        {
            using var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font, (int)Math.Max(1, r.Width));
            if ((measured.Width <= r.Width && measured.Height <= r.Height) || size <= 7f)
            {
                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, r, fmt);
                return;
            }
            size -= 1f;
        }
    }
}
