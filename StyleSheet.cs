using System.Drawing.Imaging;

namespace FolderColorizer;

// Renders every preview style in both materials for one folder side by side, to compare them.
static class StyleSheet
{
    public static void Write(string outPng, string folder, Color color)
    {
        var previews = FolderIcon.LoadPreviews(FolderIcon.PickPreviewFiles(folder));
        var styles = Enum.GetValues<PreviewStyle>();
        var materials = Enum.GetValues<FolderMaterial>();
        const int col = 290, row = 330, labelH = 30;
        using var sheet = new Bitmap(col * styles.Length, row * materials.Length, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        // A colourful "wallpaper", so see-through materials show what's behind them.
        using (var wall = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, sheet.Width, sheet.Height),
                   Color.FromArgb(0x1E, 0x3A, 0x5F), Color.FromArgb(0x7A, 0x2E, 0x5C), 20f))
            g.FillRectangle(wall, 0, 0, sheet.Width, sheet.Height);
        using (var stripe = new SolidBrush(Color.FromArgb(90, 255, 200, 80)))
            for (int x = -sheet.Height; x < sheet.Width; x += 70)
                g.FillPolygon(stripe, new PointF[] { new(x, sheet.Height), new(x + 22, sheet.Height), new(x + 22 + sheet.Height, 0), new(x + sheet.Height, 0) });
        using var font = new Font("Segoe UI", 12f, FontStyle.Bold);
        for (int m = 0; m < materials.Length; m++)
            for (int i = 0; i < styles.Length; i++)
            {
                int x = i * col, y = m * row;
                g.DrawString($"{materials[m]} · {styles[i]}", font, Brushes.White, x + 14, y + 4);
                using (var big = IconMaker.DrawFolder(256, color, previews, styles[i], materials[m]))
                    g.DrawImage(big, x + 16, y + labelH);
                using (var small = IconMaker.DrawFolder(48, color, null, styles[i], materials[m]))
                    g.DrawImage(small, x + 220, y + labelH + 250);
            }
        sheet.Save(outPng, ImageFormat.Png);
        previews.ForEach(p => p.Dispose());
    }
}
