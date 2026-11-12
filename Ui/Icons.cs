using System.Drawing.Drawing2D;

namespace FolderColorizer.Ui;

enum IconKind { None, Palette, List, Gear, Info, Folder, Open, Reset, Check, Drop, Brush, Broom, Stack, Stop }

// Small line icons drawn in code, on a 24x24 grid scaled into the target rectangle.
static class Icons
{
    public static void Draw(Graphics g, IconKind kind, RectangleF r, Color color, float stroke = 1.8f)
    {
        if (kind == IconKind.None) return;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        float k = Math.Min(r.Width, r.Height) / 24f;
        g.ScaleTransform(k, k);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        switch (kind)
        {
            case IconKind.Palette:
                using (var p = new GraphicsPath())
                {
                    p.AddArc(3, 3, 18, 18, 60, 300);
                    p.AddBezier(18.5f, 18.6f, 16, 17, 13.5f, 18, 13.5f, 20);
                    g.DrawPath(pen, p);
                }
                foreach (var (x, y) in new[] { (8f, 9f), (12f, 6.5f), (16f, 9f), (7.5f, 13.5f) })
                    g.FillEllipse(fill, x - 1.3f, y - 1.3f, 2.6f, 2.6f);
                break;
            case IconKind.List:
                foreach (var y in new[] { 6f, 12f, 18f })
                {
                    g.FillEllipse(fill, 3.5f, y - 1.2f, 2.4f, 2.4f);
                    g.DrawLine(pen, 9, y, 20.5f, y);
                }
                break;
            case IconKind.Gear:
                g.DrawEllipse(pen, 8.5f, 8.5f, 7, 7);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, 12 + (float)Math.Cos(a) * 6.3f, 12 + (float)Math.Sin(a) * 6.3f,
                        12 + (float)Math.Cos(a) * 9f, 12 + (float)Math.Sin(a) * 9f);
                }
                g.DrawEllipse(pen, 5.7f, 5.7f, 12.6f, 12.6f);
                break;
            case IconKind.Info:
                g.DrawEllipse(pen, 3, 3, 18, 18);
                g.DrawLine(pen, 12, 11, 12, 16.5f);
                g.FillEllipse(fill, 10.8f, 6.6f, 2.4f, 2.4f);
                break;
            case IconKind.Folder:
                using (var p = new GraphicsPath())
                {
                    p.AddLines(new PointF[] { new(3, 18.5f), new(3, 6.5f), new(9.5f, 6.5f), new(11.5f, 8.8f), new(21, 8.8f), new(21, 18.5f) });
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                break;
            case IconKind.Open:
                g.DrawLines(pen, new PointF[] { new(10, 5), new(5, 5), new(5, 19), new(19, 19), new(19, 14) });
                g.DrawLine(pen, 12, 12, 20, 4);
                g.DrawLines(pen, new PointF[] { new(14, 4), new(20, 4), new(20, 10) });
                break;
            case IconKind.Reset:
                g.DrawArc(pen, 4.5f, 4.5f, 15, 15, 200, 280);
                g.DrawLines(pen, new PointF[] { new(4.2f, 5.5f), new(5.1f, 10.4f), new(9.8f, 9.2f) });
                break;
            case IconKind.Check:
                g.DrawLines(pen, new PointF[] { new(5, 12.5f), new(10, 17.5f), new(19.5f, 7) });
                break;
            case IconKind.Drop:
                g.DrawLine(pen, 12, 3.5f, 12, 14.5f);
                g.DrawLines(pen, new PointF[] { new(7.5f, 10), new(12, 14.5f), new(16.5f, 10) });
                g.DrawLines(pen, new PointF[] { new(4, 15), new(4, 20), new(20, 20), new(20, 15) });
                break;
            case IconKind.Brush:
                g.DrawEllipse(pen, 4, 4, 16, 16);
                g.FillEllipse(fill, 8, 8, 8, 8);
                break;
            case IconKind.Stack:
                // Two folders, one behind the other.
                g.DrawLines(pen, new PointF[] { new(7, 6), new(7, 4.5f), new(11, 4.5f), new(12.5f, 6), new(20.5f, 6), new(20.5f, 14) });
                using (var p = new GraphicsPath())
                {
                    p.AddLines(new PointF[] { new(3.5f, 19.5f), new(3.5f, 9), new(8.5f, 9), new(10.3f, 11), new(17, 11), new(17, 19.5f) });
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                break;
            case IconKind.Stop:
                using (var p = Theme.Round(new RectangleF(6, 6, 12, 12), 2.5f)) g.DrawPath(pen, p);
                break;
            case IconKind.Broom:
                g.DrawLine(pen, 17, 3.5f, 11.5f, 11);
                using (var p = new GraphicsPath())
                {
                    p.AddLines(new PointF[] { new(8, 10), new(14.5f, 14.5f), new(11, 20.5f), new(4, 16.5f) });
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                g.DrawLine(pen, 8, 18.5f, 10, 15.5f);
                break;
        }
        g.Restore(state);
    }
}
