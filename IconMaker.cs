using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FolderColorizer;

// Draws Windows 11 style folder icons and writes them as multi-size .ico files.
// Plain: the color only, no file previews.
enum PreviewStyle { Card, Fan, Window, Plain }

static class IconMaker
{
    // Bump when the drawing changes, so Explorer's icon cache sees new file names.
    const string Version = "v2";
    static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    // Below this size the file previews are unreadable, so small icons stay plain like Explorer's.
    const int MinPreviewSize = 48;
    const string FolderIconPrefix = "f_";

    // FOLDERCOLORIZER_DATA moves the data folder; only used for testing.
    public static string DataDir => Environment.GetEnvironmentVariable("FOLDERCOLORIZER_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderColorizer");
    static string IconDir => Path.Combine(DataDir, "icons");

    public static string EnsureIcon(Color c) => Ensure($"{Version}_{Palette.Hex(c)}.ico", s => DrawFolder(s, c));

    // A one-off icon for a folder that shows its files. The name changes on every
    // write so Explorer's icon cache can't keep showing the old contents.
    public static string WriteFolderIcon(string folder, Color c, IReadOnlyList<Preview> previews, PreviewStyle style)
    {
        var path = NewFolderIconPath(folder);
        WriteIco(path, s => DrawFolder(s, c, s >= MinPreviewSize ? previews : null, style));
        return path;
    }

    public static string CopyAsFolderIcon(string folder, string icon)
    {
        var path = NewFolderIconPath(folder);
        File.Copy(icon, path);
        // A copy keeps the source's date; the clean-up below goes by age.
        File.SetLastWriteTime(path, DateTime.Now);
        return path;
    }

    // Deletes one of the per-folder icons; shared color icons are never touched.
    public static void DeleteFolderIcon(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(path), IconDir, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).StartsWith(FolderIconPrefix, StringComparison.Ordinal))
            return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static string NewFolderIconPath(string folder)
    {
        Directory.CreateDirectory(IconDir);
        return Path.Combine(IconDir, $"{FolderIconPrefix}{FolderHash(folder)}_{DateTime.UtcNow.Ticks:x}.ico");
    }

    // Explorer, and the desktop especially, keeps reading a folder's previous icon for a
    // moment after a change; deleting it right away makes it fall back to the default
    // folder icon. So old icons are only removed once they're a minute old (by default).
    public static void DeleteStaleFolderIcons(string folder, string? current, TimeSpan? minAge = null)
    {
        if (!Directory.Exists(IconDir)) return;
        var cutoff = DateTime.Now - (minAge ?? TimeSpan.FromMinutes(1));
        foreach (var f in new DirectoryInfo(IconDir).EnumerateFiles($"{FolderIconPrefix}{FolderHash(folder)}_*.ico"))
        {
            if (string.Equals(f.FullName, current, StringComparison.OrdinalIgnoreCase) || f.LastWriteTime > cutoff)
                continue;
            DeleteFolderIcon(f.FullName);
        }
    }

    static string FolderHash(string folder) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..10].ToLowerInvariant();
    public static string EnsureAppIcon() => Ensure($"{Version}_app.ico", DrawAppFolder);
    public static Bitmap DrawAppIcon(int size) => DrawAppFolder(size);
    public static void WriteAppIcon(string path) => WriteIco(path, DrawAppFolder);

    static string Ensure(string name, Func<int, Bitmap> render)
    {
        Directory.CreateDirectory(IconDir);
        var path = Path.Combine(IconDir, name);
        if (!File.Exists(path))
        {
            // Several processes can run at once (multi-select in Explorer), so write then move.
            var tmp = $"{path}.{Environment.ProcessId}.tmp";
            WriteIco(tmp, render);
            try { File.Move(tmp, path, overwrite: true); }
            catch (IOException) { File.Delete(tmp); }
        }
        return path;
    }

    public static PreviewStyle Style = PreviewStyle.Window;

    public static Bitmap DrawFolder(int size, Color c, IReadOnlyList<Preview>? previews = null, PreviewStyle? style = null) =>
        Draw(size, Shade(c, -0.2f), (r, alpha) => new LinearGradientBrush(r,
            Color.FromArgb(alpha, Shade(c, 0.16f)), Color.FromArgb(alpha, c), LinearGradientMode.Vertical), previews, style ?? Style);

    static Bitmap DrawAppFolder(int size) =>
        Draw(size, Color.FromArgb(0x55, 0x5A, 0x66), (r, _) =>
        {
            var cols = new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple" }
                .Select(n => Palette.Colors.First(p => p.Name == n).Color).ToArray();
            return new LinearGradientBrush(r, cols[0], cols[^1], LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = cols,
                    Positions = cols.Select((_, i) => i / (float)(cols.Length - 1)).ToArray(),
                },
            };
        });

    static Bitmap Draw(int size, Color back, Func<RectangleF, int, Brush> front, IReadOnlyList<Preview>? previews = null, PreviewStyle style = PreviewStyle.Card)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.ScaleTransform(size / 256f, size / 256f);

        using (var b = new SolidBrush(back))
        using (var tab = RoundRect(new RectangleF(16, 30, 100, 40), 12))
        using (var body = RoundRect(new RectangleF(16, 46, 224, 168), 14))
        {
            g.FillPath(b, tab);
            g.FillPath(b, body);
        }

        bool hasFiles = previews is { Count: > 0 };
        float top = 78;
        int frontAlpha = 255;
        if (hasFiles)
        {
            // The front flap drops lower (and lets a little through) so the files are what you see.
            switch (style)
            {
                case PreviewStyle.Card:
                    DrawCard(g, previews![0], new RectangleF(30, 50, 196, 132), 0);
                    top = 142; frontAlpha = 240;
                    break;
                case PreviewStyle.Fan:
                    var slots = new[] { (x: 98f, y: 112f, a: -9f), (x: 158f, y: 112f, a: 9f), (x: 128f, y: 104f, a: 0f) };
                    var shown = previews!.Take(3).Reverse().ToList(); // newest drawn last, so on top
                    for (int i = 0; i < shown.Count; i++)
                    {
                        var (x, y, a) = shown.Count == 1 ? slots[2] : slots[3 - shown.Count + i];
                        DrawCard(g, shown[i], new RectangleF(x - 70, y - 50, 140, 100), a);
                    }
                    top = 146; frontAlpha = 240;
                    break;
                case PreviewStyle.Window:
                    using (var body = RoundRect(new RectangleF(22, 52, 212, 160), 10))
                    {
                        var state = g.Save();
                        g.SetClip(body);
                        DrawCover(g, previews![0].Image, new RectangleF(22, 52, 212, 160));
                        g.Restore(state);
                    }
                    top = 160; frontAlpha = 225;
                    break;
            }
        }

        var fr = new RectangleF(16, top, 224, 214 - top);
        using (var fb = front(RectangleF.Inflate(fr, 1, 1), frontAlpha))
        using (var p = RoundRect(fr, 14))
            g.FillPath(fb, p);
        using (var hl = new Pen(Color.FromArgb(70, 255, 255, 255), 3f))
            g.DrawLine(hl, 30, top + 2.5f, 226, top + 2.5f);
        return bmp;
    }

    // One file as a white-framed photo card (or its type icon on paper), rotated around its centre.
    static void DrawCard(Graphics g, Preview p, RectangleF r, float angle)
    {
        var state = g.Save();
        g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
        g.RotateTransform(angle);
        var local = new RectangleF(-r.Width / 2, -r.Height / 2, r.Width, r.Height);

        using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
        using (var shadowPath = RoundRect(local with { X = local.X + 2, Y = local.Y + 4 }, 8))
            g.FillPath(shadow, shadowPath);
        using (var frame = RoundRect(local, 8))
            g.FillPath(Brushes.White, frame);

        var inner = RectangleF.Inflate(local, -6, -6);
        if (p.IsThumbnail)
        {
            using var clip = RoundRect(inner, 4);
            g.SetClip(clip, CombineMode.Intersect);
            DrawCover(g, p.Image, inner);
        }
        else
        {
            float s = Math.Min(inner.Width, inner.Height) * 0.8f;
            g.DrawImage(p.Image, Fit(p.Image.Size, s, s) with { Y = inner.Y + 4 });
        }
        g.Restore(state);
    }

    // Fills the rectangle completely, cropping the overflow. Tall images keep their upper
    // part, where faces and titles usually are; wide ones keep their middle.
    static void DrawCover(Graphics g, Image img, RectangleF dest)
    {
        float k = Math.Max(dest.Width / img.Width, dest.Height / img.Height);
        float sw = dest.Width / k, sh = dest.Height / k;
        var src = new RectangleF((img.Width - sw) / 2, (img.Height - sh) * 0.15f, sw, sh);
        g.DrawImage(img, dest, src, GraphicsUnit.Pixel);
    }

    // Largest rectangle of the image's aspect ratio inside w x h, centred on the origin.
    static RectangleF Fit(Size img, float w, float h)
    {
        float k = Math.Min(w / img.Width, h / img.Height);
        float iw = img.Width * k, ih = img.Height * k;
        return new RectangleF(-iw / 2, -ih / 2, iw, ih);
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // amount > 0 mixes toward white, < 0 toward black.
    static Color Shade(Color c, float amount)
    {
        var t = amount < 0 ? Color.Black : Color.White;
        float k = Math.Abs(amount);
        return Color.FromArgb(255, (int)(c.R + (t.R - c.R) * k), (int)(c.G + (t.G - c.G) * k), (int)(c.B + (t.B - c.B) * k));
    }

    static void WriteIco(string path, Func<int, Bitmap> render)
    {
        // PNG for the 256px frame, classic 32-bit bitmaps for the rest: some older
        // Windows code paths can't read PNG frames at small sizes.
        var pngs = Sizes.Select(s =>
        {
            using var bmp = render(s);
            if (s < 256) return ToDib(bmp);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        for (int i = 0; i < pngs.Count; i++)
        {
            byte dim = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
            w.Write(dim);
            w.Write(dim);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
            w.Write(png);
    }

    // BITMAPINFOHEADER + bottom-up BGRA pixels + an empty AND mask (alpha does the masking).
    static byte[] ToDib(Bitmap bmp)
    {
        int size = bmp.Width;
        int maskStride = (size + 31) / 32 * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((short)1);
        w.Write((short)32);
        w.Write(0);
        w.Write(size * size * 4 + maskStride * size);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);

        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[size * 4];
            for (int y = size - 1; y >= 0; y--)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                w.Write(row);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        w.Write(new byte[maskStride * size]);
        return ms.ToArray();
    }
}
