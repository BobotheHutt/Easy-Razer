using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace EasyRazer.UI.Modern;

/// <summary>Colors, fonts and icon glyphs for the dark, modern look.</summary>
public static class Ui
{
    public static readonly Color Window = Color.FromArgb(17, 17, 20);
    public static readonly Color Sidebar = Color.FromArgb(24, 24, 28);
    public static readonly Color Card = Color.FromArgb(30, 30, 35);
    public static readonly Color CardHover = Color.FromArgb(38, 38, 44);
    public static readonly Color Control = Color.FromArgb(42, 42, 49);
    public static readonly Color ControlHover = Color.FromArgb(54, 54, 62);
    public static readonly Color Border = Color.FromArgb(52, 52, 60);
    public static readonly Color Text = Color.FromArgb(240, 240, 244);
    public static readonly Color TextDim = Color.FromArgb(150, 150, 162);
    public static readonly Color TextFaint = Color.FromArgb(105, 105, 118);
    public static readonly Color Accent = Color.FromArgb(68, 214, 44);
    public static readonly Color AccentDark = Color.FromArgb(46, 150, 30);
    public static readonly Color AccentText = Color.FromArgb(10, 22, 8);
    public static readonly Color Danger = Color.FromArgb(232, 80, 80);
    public static readonly Color Warning = Color.FromArgb(255, 200, 40);

    private static readonly Dictionary<(float, FontStyle, bool), Font> Fonts = new();

    public static Font Font(float size, FontStyle style = FontStyle.Regular, bool semibold = false)
    {
        if (!Fonts.TryGetValue((size, style, semibold), out var f))
        {
            f = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, style, GraphicsUnit.Point);
            Fonts[(size, style, semibold)] = f;
        }
        return f;
    }

    // Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10) glyphs.
    public const string IconProfiles = "";
    public const string IconLighting = "";
    public const string IconDevices = "";
    public const string IconSettings = "";
    public const string IconAdd = "";
    public const string IconRename = "";
    public const string IconCopy = "";
    public const string IconDelete = "";
    public const string IconClose = "";
    public const string IconFolder = "";
    public const string IconSave = "";
    public const string IconSearch = "";

    private static Font? _iconFont;
    private static bool _iconChecked;

    /// <summary>Icon font, or null if neither icon font is installed (icons are then simply left out).</summary>
    public static Font? IconFont(float size)
    {
        if (!_iconChecked)
        {
            _iconChecked = true;
            using var fonts = new InstalledFontCollection();
            var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var family = names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                : names.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
            if (family != null) _iconFont = new Font(family, 12f, GraphicsUnit.Point);
        }
        return _iconFont == null ? null : new Font(_iconFont.FontFamily, size, GraphicsUnit.Point);
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d < 1) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle r,
        TextFormatFlags align = TextFormatFlags.VerticalCenter | TextFormatFlags.Left)
    {
        TextRenderer.DrawText(g, text, font, r, color,
            align | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    // ---- Window chrome: dark title bar, rounded corners, caption matching the sidebar (Windows 10 1809+/11)

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void DarkChrome(Form form, Color? caption = null)
    {
        form.HandleCreated += (_, _) =>
        {
            try
            {
                var on = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
                var round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(form.Handle, 33, ref round, 4);
                var c = caption ?? Window;
                var colorRef = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(form.Handle, 35, ref colorRef, 4); // caption color (Windows 11)
            }
            catch
            {
                // Older Windows: keeps the default title bar.
            }
        };
    }
}

/// <summary>Base for owner-drawn controls: double buffered, hover/press tracking.</summary>
public abstract class DrawnControl : Control
{
    protected bool Hover { get; private set; }
    protected bool Pressed { get; private set; }

    protected DrawnControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        ForeColor = Ui.Text;
        Font = Ui.Font(10f);
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected void PaintParentBackground(Graphics g)
    {
        var bg = BackColor == Color.Transparent ? Parent?.BackColor ?? Ui.Window : BackColor;
        g.Clear(bg);
    }
}

public enum ButtonKind { Primary, Secondary, Ghost, Danger }

/// <summary>Rounded flat button with optional icon. Sizes itself to its text unless AutoSize is off.</summary>
public class ModernButton : DrawnControl, IButtonControl
{
    private ButtonKind _kind = ButtonKind.Secondary;
    private string _icon = "";
    private bool _active;

    public ModernButton()
    {
        SetStyle(ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Height = 34;
        Padding = new Padding(14, 0, 14, 0);
        Margin = new Padding(0, 0, 8, 0);
        AutoSize = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Icon { get => _icon; set { _icon = value; Invalidate(); } }

    /// <summary>Shows the button as "on" (accent outline), for pick-one choices.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active { get => _active; set { _active = value; Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) { }
    public void PerformClick() => OnClick(EventArgs.Empty);

    public override Size GetPreferredSize(Size proposed)
    {
        var textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
        var iconW = Icon.Length > 0 && Ui.IconFont(10) != null ? 18 + (textW > 0 ? 6 : 0) : 0;
        return new Size(Padding.Horizontal + textW + iconW, Height);
    }

    protected override void OnTextChanged(EventArgs e) { if (AutoSize) Size = GetPreferredSize(Size.Empty); base.OnTextChanged(e); }
    protected override void OnCreateControl() { if (AutoSize) Size = GetPreferredSize(Size.Empty); base.OnCreateControl(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } f) f.DialogResult = DialogResult;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintParentBackground(g);
        Ui.Smooth(g);

        var (fill, text, border) = _kind switch
        {
            ButtonKind.Primary => (Pressed ? Ui.AccentDark : Hover ? Color.FromArgb(96, 230, 74) : Ui.Accent, Ui.AccentText, Color.Empty),
            ButtonKind.Danger => (Hover ? Color.FromArgb(70, 36, 38) : Color.FromArgb(52, 30, 32), Ui.Danger, Color.Empty),
            ButtonKind.Ghost => (Hover ? Ui.Control : Color.Empty, Hover ? Ui.Text : Ui.TextDim, Color.Empty),
            _ => (Pressed ? Ui.Control : Hover ? Ui.ControlHover : Ui.Control, Ui.Text, Color.Empty),
        };
        if (_active) { fill = Color.FromArgb(34, 70, 28); text = Ui.Accent; border = Ui.Accent; }
        if (!Enabled)
        {
            fill = _kind == ButtonKind.Primary ? Color.FromArgb(40, 60, 38) : fill == Color.Empty ? fill : Color.FromArgb(34, 34, 39);
            text = Ui.TextFaint;
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Ui.Round(r, 7))
        {
            if (fill != Color.Empty) { using var b = new SolidBrush(fill); g.FillPath(b, path); }
            if (border != Color.Empty) { using var p = new Pen(border, 1.5f); g.DrawPath(p, path); }
            if (Focused && ShowFocusCues) { using var p = new Pen(Color.FromArgb(120, Ui.Text), 1f) { DashStyle = DashStyle.Dot }; g.DrawPath(p, path); }
        }

        var x = Padding.Left;
        using var icon = Icon.Length > 0 ? Ui.IconFont(10.5f) : null;
        var hasText = !string.IsNullOrEmpty(Text);
        if (icon != null)
        {
            if (!hasText) x = (Width - 18) / 2;
            TextRenderer.DrawText(g, Icon, icon, new Rectangle(x, 0, 18, Height), text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += 24;
        }
        if (hasText)
        {
            var textRect = icon == null && !AutoSize ? new Rectangle(0, 0, Width, Height) : new Rectangle(x, 0, Width - x - 6, Height);
            var flags = TextFormatFlags.VerticalCenter | (icon == null && !AutoSize ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            Ui.DrawText(g, Text, Font, text, textRect, flags);
        }
    }
}

/// <summary>On/off switch with a label to its right.</summary>
public class ToggleSwitch : DrawnControl
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Height = 32;
        Margin = new Padding(0, 4, 0, 4);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(52 + TextRenderer.MeasureText(Text, Font).Width + 4, Height);

    protected override void OnTextChanged(EventArgs e) { Size = GetPreferredSize(Size.Empty); base.OnTextChanged(e); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintParentBackground(g);
        Ui.Smooth(g);
        var track = new RectangleF(1, (Height - 22) / 2f, 42, 22);
        using (var path = Ui.Round(track, 11))
        {
            var fill = !Enabled ? Ui.Control : _checked ? (Hover ? Color.FromArgb(96, 230, 74) : Ui.Accent) : (Hover ? Ui.ControlHover : Ui.Control);
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            if (!_checked) { using var p = new Pen(Ui.Border); g.DrawPath(p, path); }
        }
        var knobX = _checked ? track.Right - 19 : track.X + 3;
        using (var knob = new SolidBrush(_checked ? Ui.AccentText : Ui.TextDim))
            g.FillEllipse(knob, knobX, track.Y + 3, 16, 16);
        Ui.DrawText(g, Text, Font, Enabled ? Ui.Text : Ui.TextFaint, new Rectangle(52, 0, Width - 52, Height));
    }
}

/// <summary>Rounded panel with an optional title. Children should use <see cref="Ui.Card"/> as BackColor.</summary>
public class Card : Panel
{
    private string _title = "";

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Card;
        ForeColor = Ui.Text;
        Padding = new Padding(20, 16, 20, 16);
        Margin = new Padding(0, 0, 0, 14);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Padding = new Padding(Padding.Left, string.IsNullOrEmpty(value) ? 16 : 48, Padding.Right, Padding.Bottom);
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Window);
        Ui.Smooth(g);
        using var path = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), 10);
        using var b = new SolidBrush(BackColor);
        g.FillPath(b, path);
        using var p = new Pen(Color.FromArgb(40, 40, 47));
        g.DrawPath(p, path);
        if (!string.IsNullOrEmpty(_title))
            Ui.DrawText(g, _title, Ui.Font(11.5f, semibold: true), Ui.Text, new Rectangle(Padding.Left, 12, Width - Padding.Horizontal, 26));
    }
}

/// <summary>Pick-one row of options drawn as a pill ("segmented control").</summary>
public class SegmentedControl : DrawnControl
{
    private readonly List<string> _items = new();
    private int _selected;
    private int _hoverIndex = -1;
    public event EventHandler? SelectedIndexChanged;

    public SegmentedControl()
    {
        Cursor = Cursors.Hand;
        Height = 34;
        Margin = new Padding(0, 0, 12, 0);
    }

    public void SetItems(params string[] items)
    {
        _items.Clear();
        _items.AddRange(items);
        Width = _items.Sum(i => TextRenderer.MeasureText(i, Font).Width + 32) + 8;
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IEnumerable<(int Index, RectangleF Rect)> Segments()
    {
        float x = 4;
        for (var i = 0; i < _items.Count; i++)
        {
            var w = TextRenderer.MeasureText(_items[i], Font).Width + 32;
            yield return (i, new RectangleF(x, 4, w, Height - 8));
            x += w;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = -1;
        foreach (var (i, r) in Segments())
            if (r.Contains(e.Location)) hit = i;
        if (hit != _hoverIndex) { _hoverIndex = hit; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (Enabled)
            foreach (var (i, r) in Segments())
                if (r.Contains(e.Location)) SelectedIndex = i;
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintParentBackground(g);
        Ui.Smooth(g);
        using (var track = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8))
        using (var b = new SolidBrush(Ui.Window))
            g.FillPath(b, track);

        foreach (var (i, r) in Segments())
        {
            var selected = i == _selected;
            if (selected || i == _hoverIndex)
            {
                using var path = Ui.Round(r, 6);
                using var b = new SolidBrush(selected ? Ui.Control : Color.FromArgb(28, 28, 33));
                g.FillPath(b, path);
            }
            var color = !Enabled ? Ui.TextFaint : selected ? Ui.Text : Ui.TextDim;
            Ui.DrawText(g, _items[i], selected ? Ui.Font(10f, semibold: true) : Font, color, Rectangle.Round(r),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected && Enabled)
            {
                using var accent = new SolidBrush(Ui.Accent);
                g.FillRectangle(accent, r.X + r.Width / 2 - 8, r.Bottom - 3, 16, 2);
            }
        }
    }
}

/// <summary>Sidebar navigation entry.</summary>
public class NavItem : DrawnControl
{
    private bool _selected;
    public string Icon { get; }

    public NavItem(string icon, string text)
    {
        Icon = icon;
        Text = text;
        Cursor = Cursors.Hand;
        Height = 42;
        Dock = DockStyle.Top;
        Font = Ui.Font(10.5f);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Sidebar);
        Ui.Smooth(g);
        var r = new RectangleF(10, 3, Width - 20, Height - 6);
        if (_selected || Hover)
        {
            using var path = Ui.Round(r, 7);
            using var b = new SolidBrush(_selected ? Ui.Control : Color.FromArgb(32, 32, 37));
            g.FillPath(b, path);
        }
        if (_selected)
        {
            using var accent = new SolidBrush(Ui.Accent);
            using var bar = Ui.Round(new RectangleF(10, Height / 2f - 9, 3, 18), 1.5f);
            g.FillPath(accent, bar);
        }
        var color = _selected ? Ui.Text : Ui.TextDim;
        var x = 26;
        using var icon = Ui.IconFont(12f);
        if (icon != null)
        {
            TextRenderer.DrawText(g, Icon, icon, new Rectangle(x, 0, 22, Height), _selected ? Ui.Accent : color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += 34;
        }
        Ui.DrawText(g, Text, _selected ? Ui.Font(10.5f, semibold: true) : Font, color, new Rectangle(x, 0, Width - x - 8, Height));
    }
}

/// <summary>Small rounded pill showing a value, with a remove "×".</summary>
public class Chip : DrawnControl
{
    public event EventHandler? RemoveClicked;
    private bool _overX;

    public Chip(string text)
    {
        Text = text;
        Height = 30;
        Font = Ui.Font(9.5f);
        Margin = new Padding(0, 0, 8, 8);
        Width = TextRenderer.MeasureText(text, Font).Width + 46;
    }

    private Rectangle XRect => new(Width - 28, 0, 24, Height);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var over = XRect.Contains(e.Location);
        if (over != _overX) { _overX = over; Cursor = over ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (XRect.Contains(e.Location)) RemoveClicked?.Invoke(this, EventArgs.Empty);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintParentBackground(g);
        Ui.Smooth(g);
        using (var path = Ui.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f))
        using (var b = new SolidBrush(Ui.Control))
            g.FillPath(b, path);
        Ui.DrawText(g, Text, Font, Ui.Text, new Rectangle(14, 0, Width - 42, Height));
        if (_overX)
        {
            using var b = new SolidBrush(Ui.ControlHover);
            g.FillEllipse(b, Width - 26, Height / 2f - 10, 20, 20);
        }
        Ui.DrawText(g, "×", Ui.Font(11f), _overX ? Ui.Text : Ui.TextDim, XRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Dark single-line text input with a rounded border.</summary>
public class InputBox : Panel
{
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Ui.Control,
        ForeColor = Ui.Text,
        Font = Ui.Font(10.5f),
    };

    public InputBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 36;
        Width = 240;
        Padding = new Padding(12, 9, 12, 6);
        Margin = new Padding(0, 0, 8, 0);
        Box.Dock = DockStyle.Fill;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

    protected override void OnEnabledChanged(EventArgs e) { Box.ForeColor = Enabled ? Ui.Text : Ui.TextFaint; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Card);
        Ui.Smooth(g);
        using var path = Ui.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7);
        using var b = new SolidBrush(Ui.Control);
        g.FillPath(b, path);
        using var p = new Pen(Box.Focused ? Ui.Accent : Ui.Border, Box.Focused ? 1.5f : 1f);
        g.DrawPath(p, path);
    }
}

/// <summary>Owner-drawn dark list (title + optional subtitle per row).</summary>
public class ModernList : ListBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<object, (string Title, string? Subtitle)>? Describe { get; set; }

    public ModernList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 50;
        BorderStyle = BorderStyle.None;
        BackColor = Ui.Card;
        ForeColor = Ui.Text;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        Ui.Smooth(g);
        var selected = (e.State & DrawItemState.Selected) != 0;
        var r = new RectangleF(e.Bounds.X + 4, e.Bounds.Y + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);
        if (selected)
        {
            using var path = Ui.Round(r, 7);
            using var b = new SolidBrush(Ui.Control);
            g.FillPath(b, path);
            using var accent = new SolidBrush(Ui.Accent);
            using var bar = Ui.Round(new RectangleF(r.X, r.Y + r.Height / 2 - 9, 3, 18), 1.5f);
            g.FillPath(accent, bar);
        }
        var item = Items[e.Index];
        var (title, sub) = Describe?.Invoke(item) ?? (item.ToString() ?? "", null);
        var x = e.Bounds.X + 18;
        if (sub == null)
        {
            Ui.DrawText(g, title, Ui.Font(10.5f, semibold: selected), selected ? Ui.Text : Ui.TextDim,
                new Rectangle(x, e.Bounds.Y, e.Bounds.Width - x - 8, e.Bounds.Height));
        }
        else
        {
            Ui.DrawText(g, title, Ui.Font(10.5f, semibold: true), selected ? Ui.Text : Color.FromArgb(215, 215, 222),
                new Rectangle(x, e.Bounds.Y + 6, e.Bounds.Width - x - 8, 22));
            Ui.DrawText(g, sub, Ui.Font(8.75f), Ui.TextFaint, new Rectangle(x, e.Bounds.Y + 26, e.Bounds.Width - x - 8, 18));
        }
    }
}

/// <summary>Plain label preset for the dark theme.</summary>
public class TextLabel : Label
{
    public TextLabel(string text, float size = 10f, bool semibold = false, Color? color = null)
    {
        Text = text;
        Font = Ui.Font(size, semibold: semibold);
        ForeColor = color ?? Ui.Text;
        AutoSize = true;
        BackColor = Color.Transparent;
        UseMnemonic = false;
    }
}
