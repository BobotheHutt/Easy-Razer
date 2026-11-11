using System.ComponentModel;
using EasyRazer.Devices;

namespace EasyRazer.UI;

/// <summary>Clickable on-screen keyboard (full size, plus F13–F24) used to pick what a button sends.</summary>
public sealed class KeyboardView : Control
{

    private static readonly IReadOnlyList<KeyCap> Caps = StandardKeyboard.Caps;
    private static readonly RectangleF Extent = Caps.Aggregate(RectangleF.Empty, (acc, c) =>
        RectangleF.Union(acc, c.Bounds));

    private Keys? _hover;
    private Keys? _chosen;
    private IReadOnlyCollection<Keys> _held = Array.Empty<Keys>();
    private IReadOnlyCollection<Keys> _disabled = Array.Empty<Keys>();
    private float _unit = 30;
    private PointF _offset;

    public event Action<Keys>? KeyClicked;

    public KeyboardView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
    }

    /// <summary>The key to highlight as the current choice (null = none).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys? ChosenKey
    {
        get => _chosen;
        set { _chosen = value; Invalidate(); }
    }

    /// <summary>Keys being held down right now while recording (drawn highlighted).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyCollection<Keys> HeldKeys
    {
        get => _held;
        set { _held = value; Invalidate(); }
    }

    /// <summary>Keys that can't be picked (drawn greyed out), e.g. Escape.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyCollection<Keys> DisabledKeys
    {
        get => _disabled;
        set { _disabled = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        KeyCapRenderer.Prepare(g);
        g.Clear(BackColor);
        ComputeLayout();

        foreach (var c in Caps)
        {
            var r = Rect(c);
            var disabled = _disabled.Contains(c.Key);
            var held = _held.Contains(c.Key);
            var chosen = !held && c.Key == _chosen;
            var hover = !disabled && c.Key == _hover;
            using var path = KeyCapRenderer.Rounded(r, _unit * 0.12f);
            var fillColor = disabled ? Theme.KeyFixed
                : held ? Color.FromArgb(110, 90, 20)
                : chosen ? Color.FromArgb(40, 90, 30)
                : hover ? Theme.KeyHover : Theme.Key;
            using var fill = new SolidBrush(fillColor);
            g.FillPath(fill, path);
            var border = held ? Theme.Recording : chosen ? Theme.Chosen : Theme.Outline;
            using var pen = new Pen(border, held || chosen ? 2 : 1);
            g.DrawPath(pen, path);
            var textColor = disabled ? Theme.TextDim : held ? Theme.Recording : chosen ? Theme.Chosen : Theme.Text;
            KeyCapRenderer.FitText(g, KeyLabels.Key(c.Key), new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4),
                textColor, _unit * 0.36f);
        }
    }

    private void ComputeLayout()
    {
        const float pad = 8;
        _unit = Math.Max(8, Math.Min((Width - 2 * pad) / Extent.Width, (Height - 2 * pad) / Extent.Height));
        _offset = new PointF(
            (Width - Extent.Width * _unit) / 2 - Extent.X * _unit,
            (Height - Extent.Height * _unit) / 2 - Extent.Y * _unit);
    }

    private RectangleF Rect(KeyCap c)
    {
        var gap = _unit * 0.07f;
        var b = c.Bounds;
        return new RectangleF(_offset.X + b.X * _unit + gap, _offset.Y + b.Y * _unit + gap,
            b.Width * _unit - 2 * gap, b.Height * _unit - 2 * gap);
    }

    private Keys? HitTest(Point p)
    {
        ComputeLayout();
        foreach (var c in Caps)
            if (Rect(c).Contains(p)) return c.Key;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover) return;
        _hover = hit;
        Cursor = hit != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (HitTest(e.Location) is { } k && !_disabled.Contains(k)) KeyClicked?.Invoke(k);
    }

}
