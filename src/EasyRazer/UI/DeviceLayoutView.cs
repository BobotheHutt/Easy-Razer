using EasyRazer.Devices;

namespace EasyRazer.UI;

/// <summary>How a button should be shown: its current action text and where that action comes from.</summary>
public enum BindingSource { Factory, Inherited, Custom }

/// <summary>
/// Draws a device as a clickable picture. Click a button to select it; each button shows what it currently does.
/// </summary>
public sealed class DeviceLayoutView : Control
{
    private IReadOnlyList<LayoutShape> _shapes = Array.Empty<LayoutShape>();
    private string? _hover;
    private string? _selected;

    // Layout units -> pixels
    private float _scale = 1;
    private PointF _offset;

    /// <summary>Supplies text + source for a button id.</summary>
    public Func<string, (string Text, BindingSource Source)>? Describe { get; set; }

    public event Action<string>? ButtonClicked;

    public DeviceLayoutView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Background;
        Cursor = Cursors.Default;
    }

    public IReadOnlyList<LayoutShape> Shapes
    {
        get => _shapes;
        set { _shapes = value; _hover = null; Invalidate(); }
    }

    public string? SelectedButton
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        KeyCapRenderer.Prepare(g);
        g.Clear(BackColor);
        if (_shapes.Count == 0) return;

        ComputeTransform();

        // Outlines and labels first, buttons on top.
        foreach (var s in _shapes.Where(s => s.Kind is ShapeKind.Outline or ShapeKind.Label)) DrawShape(g, s);
        foreach (var s in _shapes.Where(s => s.Kind is not (ShapeKind.Outline or ShapeKind.Label))) DrawShape(g, s);
    }

    private void DrawShape(Graphics g, LayoutShape s)
    {
        var r = ToScreen(s.Bounds);
        switch (s.Kind)
        {
            case ShapeKind.Label:
                KeyCapRenderer.FitText(g, s.Caption, r, Theme.TextDim, 14, FontStyle.Bold);
                return;

            case ShapeKind.Outline:
            {
                using var path = KeyCapRenderer.Rounded(r, 6 * _scale);
                using var pen = new Pen(Theme.Outline, 2);
                g.DrawPath(pen, path);
                return;
            }

            case ShapeKind.Fixed:
            {
                using var path = KeyCapRenderer.Rounded(r, 1.5f * _scale);
                using var fill = new SolidBrush(Theme.KeyFixed);
                g.FillPath(fill, path);
                KeyCapRenderer.FitText(g, s.Caption, Inset(r, 4), Theme.TextDim, 12);
                return;
            }
        }

        var id = s.ButtonId!;
        var (text, source) = Describe?.Invoke(id) ?? ("", BindingSource.Factory);
        var isSelected = id == _selected;
        var isHover = id == _hover;

        using (var path = s.Kind == ShapeKind.Round ? Ellipse(r) : KeyCapRenderer.Rounded(r, 1.5f * _scale))
        {
            using var fill = new SolidBrush(isHover ? Theme.KeyHover : Theme.Key);
            g.FillPath(fill, path);

            var accent = source switch
            {
                BindingSource.Custom => Theme.Custom,
                BindingSource.Inherited => Theme.Inherited,
                _ => Theme.Outline,
            };
            using var border = new Pen(isSelected ? Theme.Selected : accent, isSelected ? 3 : source == BindingSource.Factory ? 1 : 2);
            g.DrawPath(border, path);
        }

        // Small caption at the top, current action big in the middle.
        var captionH = Math.Min(r.Height * 0.32f, 16);
        var captionRect = new RectangleF(r.X + 2, r.Y + 1, r.Width - 4, captionH);
        var bodyRect = new RectangleF(r.X + 3, r.Y + captionH, r.Width - 6, r.Height - captionH - 2);
        KeyCapRenderer.FitText(g, s.Caption, captionRect, Theme.TextDim, 11);
        var textColor = source == BindingSource.Factory ? Theme.Text : source == BindingSource.Custom ? Theme.Custom : Theme.Inherited;
        KeyCapRenderer.FitText(g, text, bodyRect, textColor, 15, FontStyle.Bold);
    }

    private static System.Drawing.Drawing2D.GraphicsPath Ellipse(RectangleF r)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddEllipse(r);
        return p;
    }

    private static RectangleF Inset(RectangleF r, float by) => new(r.X + by, r.Y + by, r.Width - 2 * by, r.Height - 2 * by);

    private void ComputeTransform()
    {
        var b = _shapes[0].Bounds;
        foreach (var s in _shapes) b = RectangleF.Union(b, s.Bounds);
        const float pad = 16;
        var sx = (Width - 2 * pad) / b.Width;
        var sy = (Height - 2 * pad) / b.Height;
        _scale = Math.Max(0.1f, Math.Min(sx, sy));
        _offset = new PointF(
            (Width - b.Width * _scale) / 2 - b.X * _scale,
            (Height - b.Height * _scale) / 2 - b.Y * _scale);
    }

    private RectangleF ToScreen(RectangleF r) =>
        new(_offset.X + r.X * _scale, _offset.Y + r.Y * _scale, r.Width * _scale, r.Height * _scale);

    private string? HitTest(Point p)
    {
        if (_shapes.Count == 0) return null;
        ComputeTransform();
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            var s = _shapes[i];
            if (s.ButtonId != null && ToScreen(s.Bounds).Contains(p)) return s.ButtonId;
        }
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
        if (_hover == null) return;
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Focus();
        var hit = HitTest(e.Location);
        if (hit == null) return;
        SelectedButton = hit;
        ButtonClicked?.Invoke(hit);
    }
}
