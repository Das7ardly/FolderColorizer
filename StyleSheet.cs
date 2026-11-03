using System.Drawing.Imaging;

namespace FolderColorizer;

// Renders every preview style for one folder side by side, to compare them.
static class StyleSheet
{
    public static void Write(string outPng, string folder, Color color)
    {
        var previews = FolderIcon.LoadPreviews(FolderIcon.PickPreviewFiles(folder));
        int[] sizes = [256, 96, 48];
        var styles = Enum.GetValues<PreviewStyle>();
        const int col = 300, labelH = 30;
        using var sheet = new Bitmap(col * styles.Length, labelH + 256 + 20 + 96 + 20, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(0x20, 0x20, 0x20));
        using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
        for (int i = 0; i < styles.Length; i++)
        {
            int x = i * col;
            g.DrawString($"{i + 1}. {styles[i]}", font, Brushes.White, x + 20, 4);
            using (var big = IconMaker.DrawFolder(sizes[0], color, previews, styles[i]))
                g.DrawImage(big, x + 22, labelH);
            using (var mid = IconMaker.DrawFolder(sizes[1], color, previews, styles[i]))
                g.DrawImage(mid, x + 40, labelH + 276);
            using (var small = IconMaker.DrawFolder(sizes[2], color, previews, styles[i]))
                g.DrawImage(small, x + 170, labelH + 300);
        }
        sheet.Save(outPng, ImageFormat.Png);
        previews.ForEach(p => p.Dispose());
    }
}
