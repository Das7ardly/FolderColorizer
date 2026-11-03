using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FolderColorizer.Ui;

// Every colour in the settings window comes from here, so one change restyles the app.
static class Theme
{
    public static readonly Color Bg = Hex("0F172A");
    public static readonly Color Sidebar = Hex("0B1222");
    public static readonly Color Card = Hex("1B2336");
    public static readonly Color Muted = Hex("272F42");
    public static readonly Color Hover = Hex("323C54");
    public static readonly Color BorderSoft = Hex("2C3649");
    public static readonly Color Border = Hex("475569");
    public static readonly Color Text = Hex("F8FAFC");
    public static readonly Color TextMuted = Hex("94A3B8");
    public static readonly Color Accent = Hex("22C55E");
    public static readonly Color AccentHi = Hex("4ADE80");
    public static readonly Color Danger = Hex("F87171");
    public static readonly Color Ring = Hex("E2E8F0");
    // Text on the accent: whichever of dark or white reads better (WCAG 4.5:1).
    public static readonly Color OnAccent = Contrast(Hex("0F172A"), Accent) >= Contrast(Color.White, Accent) ? Hex("0F172A") : Color.White;

    public static readonly Font Title = new("Segoe UI Semibold", 18f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Body = new("Segoe UI", 10f);
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 10f);
    public static readonly Font Small = new("Segoe UI", 9f);

    static Theme()
    {
        if (Contrast(OnAccent, Accent) < 4.5f || Contrast(Text, Card) < 4.5f || Contrast(TextMuted, Card) < 4.5f)
            throw new InvalidOperationException("Theme colours fail the 4.5:1 contrast check.");
    }

    public static Color Hex(string h) =>
        Color.FromArgb(Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..6], 16));

    public static float Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        double la = L(a), lb = L(b);
        return (float)((Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05));
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius);
        g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f, DashStyle dash = DashStyle.Solid)
    {
        using var pen = new Pen(c, width) { DashStyle = dash };
        using var p = Round(r, radius);
        g.DrawPath(pen, p);
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    // Dark scroll bars for scrollable panels (Windows 10 1809+).
    public static void DarkScrollBars(Control c) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
}
