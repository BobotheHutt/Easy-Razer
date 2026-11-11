using System.ComponentModel;
using EasyRazer.Devices;

namespace EasyRazer.UI;

/// <summary>Clickable on-screen keyboard (full size, plus F13–F24) used to pick what a button sends.</summary>
public sealed class KeyboardView : Control
{
    private sealed record Cap(Keys Key, float X, float Y, float W, float H);

    private static readonly List<Cap> Caps = Build();
    private static readonly RectangleF Extent = Caps.Aggregate(RectangleF.Empty, (acc, c) =>
        RectangleF.Union(acc, new RectangleF(c.X, c.Y, c.W, c.H)));

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

    private RectangleF Rect(Cap c)
    {
        var gap = _unit * 0.07f;
        return new RectangleF(_offset.X + c.X * _unit + gap, _offset.Y + c.Y * _unit + gap,
            c.W * _unit - 2 * gap, c.H * _unit - 2 * gap);
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

    private static List<Cap> Build()
    {
        var caps = new List<Cap>();
        void Add(Keys k, float x, float y, float w = 1, float h = 1)
        {
            if (HidKeys.IsSupported(k)) caps.Add(new Cap(k, x, y, w, h));
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
