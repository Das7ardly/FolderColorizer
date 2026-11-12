using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FolderColorizer.Ui;

// The window's "wallpaper": a dark base with a few soft colour glows. Pages paint it as is;
// glass surfaces (cards, sidebar) paint a blurred copy of the part behind them, then a tint.
static class Backdrop
{
    // Drawn once at this size and stretched to the window: it is all soft gradients, so scaling
    // doesn't show, and resizing the window doesn't have to rebuild it.
    static readonly Size RenderSize = new(1600, 1000);
    static Bitmap? sharp, blurred;

    // Paints the part of the window backdrop that lies behind control c.
    public static void Paint(Control c, Graphics g, bool blur)
    {
        var form = c.FindForm();
        if (form == null || form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0)
        {
            g.Clear(Theme.Bg);
            return;
        }
        Ensure();
        var offset = form.PointToClient(c.PointToScreen(Point.Empty));
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(blur ? blurred! : sharp!, new Rectangle(-offset.X, -offset.Y, form.ClientSize.Width, form.ClientSize.Height));
        g.Restore(state);
    }

    static void Ensure()
    {
        if (sharp != null) return;
        var s = RenderSize;
        sharp = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(sharp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new Rectangle(Point.Empty, s), Theme.Hex("0E1526"), Theme.Hex("0A0F1C"), 60f))
                g.FillRectangle(b, 0, 0, s.Width, s.Height);
            // Few, large and quiet: they only need to show through the glass.
            Glow(g, s, 0.82f, 0.06f, 0.55f, Theme.Hex("1FB5A8"), 70);
            Glow(g, s, 0.30f, 0.95f, 0.60f, Theme.Hex("5B5FE8"), 60);
            Glow(g, s, 0.05f, 0.20f, 0.35f, Theme.Hex("2F7DF6"), 45);
            Glow(g, s, 0.62f, 0.62f, 0.30f, Theme.Hex("E85CA8"), 22);
        }
        blurred = IconMaker.Blur(sharp, 24);
    }

    static void Glow(Graphics g, Size s, float cx, float cy, float radius, Color c, int alpha)
    {
        float r = Math.Max(s.Width, s.Height) * radius;
        var rect = new RectangleF(s.Width * cx - r, s.Height * cy - r, r * 2, r * 2);
        using var path = new GraphicsPath();
        path.AddEllipse(rect);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, c),
            SurroundColors = [Color.FromArgb(0, c)],
        };
        g.FillEllipse(brush, rect);
    }

    // Liquid-glass surface: the softened backdrop seen through a light tint, a hairline border
    // and a specular rim that is brightest at the top-left and glints again at the bottom-right.
    public static void PaintGlass(Control c, Graphics g, RectangleF r, float radius, Color tint, Color border)
    {
        using var path = Theme.Round(r, radius);
        var state = g.Save();
        g.SetClip(path);
        Paint(c, g, blur: true);
        using (var b = new SolidBrush(tint)) g.FillPath(b, path);
        // Faint sheen across the upper part.
        var sheen = new RectangleF(r.X, r.Y, r.Width, Math.Min(r.Height * 0.45f, 120));
        using (var gloss = new LinearGradientBrush(RectangleF.Inflate(sheen, 0, 1), Color.FromArgb(16, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillRectangle(gloss, sheen);
        g.Restore(state);

        Theme.Smooth(g);
        using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
        using var rim = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), Color.White, Color.White, 35f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(95, 255, 255, 255), Color.FromArgb(18, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                          Color.FromArgb(14, 255, 255, 255), Color.FromArgb(48, 255, 255, 255)],
                Positions = [0f, 0.22f, 0.5f, 0.8f, 1f],
            },
        };
        using var rimPen = new Pen(rim, 1.2f);
        using var inner = Theme.Round(RectangleF.Inflate(r, -0.7f, -0.7f), Math.Max(1, radius - 0.7f));
        g.DrawPath(rimPen, inner);
    }
}
