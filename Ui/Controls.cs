using System.Drawing.Text;

namespace FolderColorizer.Ui;

// Base for the owner-drawn controls: double-buffered, hover tracking, DPI helper.
abstract class ThemedControl : Control
{
    protected bool Hovered, Pressed;

    protected ThemedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    protected float S => DeviceDpi / 96f;

    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovered = Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    // Keyboard focus ring, drawn only when the user navigates with the keyboard.
    protected void DrawFocus(Graphics g, RectangleF r, float radius)
    {
        if (Focused && ShowFocusCues)
            Theme.StrokeRound(g, Theme.Ring, RectangleF.Inflate(r, 2 * S, 2 * S), radius + 2 * S, 2 * S);
    }

    protected void ClickFromKeyboard(KeyEventArgs e, Action click)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { click(); e.Handled = true; }
    }
}

enum ButtonKind { Primary, Secondary, Ghost, Danger }

sealed class FlatButton : ThemedControl
{
    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;
    public IconKind Icon { get; set; }

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary, IconKind icon = IconKind.None)
    {
        Text = text;
        Kind = kind;
        Icon = icon;
        Cursor = Cursors.Hand;
        TabStop = true;
        AutoSize = true;
        Font = kind == ButtonKind.Primary ? Theme.BodyStrong : Theme.Body;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        int icon = Icon == IconKind.None ? 0 : (int)(24 * S);
        return new Size(t.Width + (int)(28 * S) + icon, (int)(36 * S));
    }

    protected override void OnKeyUp(KeyEventArgs e) { ClickFromKeyboard(e, () => OnClick(EventArgs.Empty)); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new RectangleF(2 * S, 2 * S, Width - 4 * S, Height - 4 * S);
        float radius = 8 * S;
        Color fill, text, border = Color.Empty;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = Hovered ? Theme.AccentHi : Theme.Accent; text = Theme.OnAccent; break;
            case ButtonKind.Danger:
                fill = Hovered ? Theme.Hover : Theme.Muted; text = Theme.Danger; break;
            case ButtonKind.Ghost:
                fill = Hovered ? Theme.Muted : Color.Transparent; text = Theme.Text; break;
            default:
                fill = Hovered ? Theme.Hover : Theme.Muted; text = Theme.Text; border = Theme.BorderSoft; break;
        }
        if (Pressed) fill = ControlPaint.Dark(fill, 0.05f);
        if (!Enabled) { text = Color.FromArgb(110, text); fill = Kind == ButtonKind.Primary ? Color.FromArgb(90, fill) : fill; }
        if (fill.A > 0) Theme.FillRound(g, fill, r, radius);
        if (!border.IsEmpty) Theme.StrokeRound(g, border, r, radius);

        var size = TextRenderer.MeasureText(Text, Font);
        float iconW = Icon == IconKind.None ? 0 : 18 * S, gap = Icon == IconKind.None ? 0 : 7 * S;
        float x = (Width - (iconW + gap + size.Width)) / 2;
        if (Icon != IconKind.None)
            Icons.Draw(g, Icon, new RectangleF(x, (Height - iconW) / 2, iconW, iconW), text, 1.9f);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(x + iconW + gap), 0, size.Width + 2, Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        DrawFocus(g, r, radius);
    }
}

// On/off switch with a short slide.
sealed class Toggle : ThemedControl
{
    bool on;
    float pos;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 15 };
    public event EventHandler? CheckedChanged;

    public Toggle()
    {
        Cursor = Cursors.Hand;
        TabStop = true;
        anim.Tick += (_, _) =>
        {
            float target = on ? 1 : 0;
            pos += Math.Sign(target - pos) * 0.15f;
            if (Math.Abs(target - pos) < 0.15f) { pos = target; anim.Stop(); }
            Invalidate();
        };
    }

    public bool Checked
    {
        get => on;
        set { if (on == value) return; on = value; anim.Start(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    // Sets the state without the animation or the event (initial load).
    public void SetQuietly(bool value) { on = value; pos = value ? 1 : 0; Invalidate(); }

    public override Size GetPreferredSize(Size proposed) => new((int)(48 * S), (int)(28 * S));
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyUp(KeyEventArgs e) { ClickFromKeyboard(e, () => Checked = !Checked); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var track = new RectangleF(3 * S, (Height - 22 * S) / 2, 42 * S, 22 * S);
        var off = Hovered ? Theme.Hover : Theme.Muted;
        var fill = Color.FromArgb(
            (int)(off.R + (Theme.Accent.R - off.R) * pos), (int)(off.G + (Theme.Accent.G - off.G) * pos), (int)(off.B + (Theme.Accent.B - off.B) * pos));
        Theme.FillRound(g, fill, track, track.Height / 2);
        if (pos < 0.5f) Theme.StrokeRound(g, Theme.Border, track, track.Height / 2);
        float d = 16 * S, x = track.X + 3 * S + (track.Width - d - 6 * S) * pos;
        using (var knob = new SolidBrush(pos > 0.5f ? Theme.OnAccent : Theme.Text))
            g.FillEllipse(knob, x, track.Y + 3 * S, d, d);
        DrawFocus(g, track, track.Height / 2);
    }
}

sealed class NavButton : ThemedControl
{
    public IconKind Icon { get; }
    bool selected;
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public NavButton(string text, IconKind icon)
    {
        Text = text;
        Icon = icon;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnKeyUp(KeyEventArgs e) { ClickFromKeyboard(e, () => OnClick(EventArgs.Empty)); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new RectangleF(0, 2 * S, Width, Height - 4 * S);
        if (selected) Theme.FillRound(g, Theme.Muted, r, 8 * S);
        else if (Hovered) Theme.FillRound(g, Color.FromArgb(120, Theme.Muted), r, 8 * S);
        if (selected) Theme.FillRound(g, Theme.Accent, new RectangleF(0, r.Y + 10 * S, 3 * S, r.Height - 20 * S), 1.5f * S);
        var color = selected ? Theme.Text : Theme.TextMuted;
        Icons.Draw(g, Icon, new RectangleF(14 * S, (Height - 20 * S) / 2, 20 * S, 20 * S), selected ? Theme.Accent : color);
        TextRenderer.DrawText(g, Text, selected ? Theme.BodyStrong : Theme.Body,
            new Rectangle((int)(44 * S), 0, Width - (int)(48 * S), Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        DrawFocus(g, r, 8 * S);
    }
}

// Rounded surface; children sit on it with transparent backgrounds.
class Card : Panel
{
    public bool Dashed { get; set; }
    public bool Highlight { get; set; }

    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Backdrop.Paint(this, g, blur: false); // what shows around the rounded corners
        Theme.Smooth(g);
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Backdrop.PaintGlass(this, g, r, 14 * s,
            Highlight ? Color.FromArgb(150, Theme.Muted) : Color.FromArgb(118, Theme.Card),
            Highlight ? Theme.Accent : Color.FromArgb(34, 255, 255, 255));
        if (Dashed)
            Theme.StrokeRound(g, Highlight ? Theme.Accent : Color.FromArgb(90, Theme.Border), RectangleF.Inflate(r, -6 * s, -6 * s), 10 * s,
                1.4f * s, System.Drawing.Drawing2D.DashStyle.Dash);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        Invalidate(); // the backdrop behind it moved
    }
}

// A row of mutually exclusive options (segmented control). Arrow keys move the choice.
sealed class Segmented : ThemedControl
{
    readonly string[] items;
    int selected, hover = -1;
    public event EventHandler? SelectedChanged;

    public Segmented(params string[] items)
    {
        this.items = items;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public int SelectedIndex
    {
        get => selected;
        set { value = Math.Clamp(value, 0, items.Length - 1); if (value == selected) return; selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SetQuietly(int index) { selected = Math.Clamp(index, 0, items.Length - 1); Invalidate(); }

    int SegmentWidth => items.Max(t => TextRenderer.MeasureText(t, Theme.Body).Width) + (int)(26 * S);
    public override Size GetPreferredSize(Size proposed) => new(SegmentWidth * items.Length + (int)(8 * S), (int)(36 * S));
    RectangleF Segment(int i) => new(4 * S + i * SegmentWidth, 4 * S, SegmentWidth, Height - 8 * S);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Enumerable.Range(0, items.Length).FirstOrDefault(i => Segment(i).Contains(e.Location), -1);
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (hover >= 0) { Focus(); SelectedIndex = hover; } base.OnMouseUp(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { SelectedIndex--; e.Handled = true; }
        if (e.KeyCode == Keys.Right) { SelectedIndex++; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Color.FromArgb(120, Theme.Bg), outer, 10 * S);
        Theme.StrokeRound(g, Color.FromArgb(30, 255, 255, 255), outer, 10 * S);
        for (int i = 0; i < items.Length; i++)
        {
            var r = Segment(i);
            if (i == selected)
            {
                Theme.FillRound(g, Color.FromArgb(220, Theme.Hover), r, 7 * S);
                Theme.StrokeRound(g, Color.FromArgb(60, 255, 255, 255), r, 7 * S);
            }
            else if (i == hover)
                Theme.FillRound(g, Color.FromArgb(90, Theme.Muted), r, 7 * S);
            TextRenderer.DrawText(g, items[i], i == selected ? Theme.BodyStrong : Theme.Body, Rectangle.Round(r),
                i == selected ? Theme.Text : Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        if (Focused && ShowFocusCues) Theme.StrokeRound(g, Theme.Ring, Segment(selected), 7 * S, 1.5f * S);
    }
}

// A panel that shows the window backdrop (content areas, scrolling lists).
class BackdropPanel : Panel
{
    public BackdropPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Backdrop.Paint(this, e.Graphics, blur: false);
}

sealed class BackdropFlow : FlowLayoutPanel
{
    public BackdropFlow()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.Bg;
    }
    protected override void OnPaintBackground(PaintEventArgs e) => Backdrop.Paint(this, e.Graphics, blur: false);
    // The backdrop stays put while the rows scroll over it.
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
}

// The palette: one circle per colour, plus a ring around the chosen one.
sealed class SwatchGrid : ThemedControl
{
    public const int Columns = 6;
    int hover = -1;
    public int Selected { get; private set; } = -1;
    public Color? CustomColor { get; private set; }
    public event EventHandler? SelectionChanged;

    public SwatchGrid()
    {
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    int Cell => (int)(52 * S);
    int Count => Palette.Colors.Length;
    public override Size GetPreferredSize(Size proposed) => new(Columns * Cell, (Count + Columns - 1) / Columns * Cell);

    public Color? SelectedColor => CustomColor ?? (Selected >= 0 ? Palette.Colors[Selected].Color : null);
    public string SelectedName => CustomColor != null ? $"Custom #{Palette.Hex(CustomColor.Value).ToUpperInvariant()}" : Selected >= 0 ? Palette.Colors[Selected].Name : "";

    public void Select(Color? color)
    {
        CustomColor = null;
        Selected = color == null ? -1 : Array.FindIndex(Palette.Colors, c => c.Color.ToArgb() == color.Value.ToArgb());
        if (color != null && Selected < 0) CustomColor = color;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    Rectangle CellRect(int i) => new(i % Columns * Cell, i / Columns * Cell, Cell, Cell);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Enumerable.Range(0, Count).FirstOrDefault(i => CellRect(i).Contains(e.Location), -1);
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (hover >= 0) { Focus(); Select(Palette.Colors[hover].Color); }
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int i = Math.Max(Selected, 0);
        i = e.KeyCode switch
        {
            Keys.Left => i - 1, Keys.Right => i + 1, Keys.Up => i - Columns, Keys.Down => i + Columns, _ => int.MinValue,
        };
        if (i != int.MinValue) { Select(Palette.Colors[Math.Clamp(i, 0, Count - 1)].Color); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        for (int i = 0; i < Count; i++)
        {
            var cell = CellRect(i);
            float d = 38 * S;
            var r = new RectangleF(cell.X + (cell.Width - d) / 2, cell.Y + (cell.Height - d) / 2, d, d);
            if (i == hover && i != Selected) r.Inflate(2 * S, 2 * S);
            using (var b = new SolidBrush(Palette.Colors[i].Color))
                g.FillEllipse(b, r);
            if (i == Selected)
            {
                using var ring = new Pen(Theme.Text, 2.5f * S);
                g.DrawEllipse(ring, RectangleF.Inflate(r, 4 * S, 4 * S));
                var tick = Theme.Contrast(Color.White, Palette.Colors[i].Color) >= 3 ? Color.White : Theme.Bg;
                Icons.Draw(g, IconKind.Check, RectangleF.Inflate(r, -9 * S, -9 * S), tick, 2.6f);
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Ring, 1.5f * S) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                    g.DrawRectangle(focus, cell.X + 1, cell.Y + 1, cell.Width - 3, cell.Height - 3);
                }
            }
        }
    }
}

// Shows a bitmap centred and scaled down to fit, on the card colour.
sealed class ImageBox : ThemedControl
{
    Bitmap? image;
    public Bitmap? Image { get => image; set { image?.Dispose(); image = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (image == null) return;
        Theme.Smooth(e.Graphics);
        float k = Math.Min(1f, Math.Min((float)Width / image.Width, (float)Height / image.Height));
        float w = image.Width * k, h = image.Height * k;
        e.Graphics.DrawImage(image, (Width - w) / 2, (Height - h) / 2, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) image?.Dispose();
        base.Dispose(disposing);
    }
}

// A selectable tile with a picture and a caption (preview style choice).
sealed class ChoiceTile : ThemedControl
{
    public Bitmap? Picture { get; set; }
    bool selected;
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public ChoiceTile(string text)
    {
        Text = text;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnKeyUp(KeyEventArgs e) { ClickFromKeyboard(e, () => OnClick(EventArgs.Empty)); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var r = new RectangleF(1.5f * S, 1.5f * S, Width - 3 * S, Height - 3 * S);
        Theme.FillRound(g, selected ? Theme.Muted : Hovered ? Color.FromArgb(150, Theme.Muted) : Theme.Card, r, 10 * S);
        Theme.StrokeRound(g, selected ? Theme.Accent : Theme.BorderSoft, r, 10 * S, selected ? 2 * S : 1);
        if (Picture != null)
        {
            float side = Math.Min(Width - 24 * S, Height - 46 * S);
            g.DrawImage(Picture, (Width - side) / 2, 10 * S, side, side);
        }
        TextRenderer.DrawText(g, Text, selected ? Theme.BodyStrong : Theme.Body,
            new Rectangle(0, Height - (int)(34 * S), Width, (int)(26 * S)), selected ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        DrawFocus(g, r, 10 * S);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Picture?.Dispose();
        base.Dispose(disposing);
    }
}

static class UiText
{
    public static Label Make(string text, Font font, Color color, bool wrap = false) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = !wrap,
        AutoEllipsis = !wrap,
        UseMnemonic = false,
    };
}
