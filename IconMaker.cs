using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FolderColorizer;

// Draws Windows 11 style folder icons and writes them as multi-size .ico files.
// Plain: the color only, no file previews.
enum PreviewStyle { Card, Fan, Window, Plain }

// Classic: flat Windows 11 look. Glass: frosted, translucent front panel.
// Liquid: clear glass like macOS 26, bending what's behind it at the edges, with bright rims.
enum FolderMaterial { Classic, Glass, Liquid }

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

    // Shared icon for a color without file previews (also used by the right-click menu).
    public static string EnsureIcon(Color c, FolderMaterial material = FolderMaterial.Classic) => material switch
    {
        FolderMaterial.Glass => Ensure($"glass1_{Palette.Hex(c)}.ico", s => DrawFolder(s, c, material: material)),
        FolderMaterial.Liquid => Ensure($"liquid1_{Palette.Hex(c)}.ico", s => DrawFolder(s, c, material: material)),
        _ => Ensure($"{Version}_{Palette.Hex(c)}.ico", s => DrawFolder(s, c)),
    };

    // A one-off icon for a folder that shows its files. The name changes on every
    // write so Explorer's icon cache can't keep showing the old contents.
    public static string WriteFolderIcon(string folder, Color c, IReadOnlyList<Preview> previews, PreviewStyle style, FolderMaterial material)
    {
        var path = NewFolderIconPath(folder);
        WriteIco(path, s => DrawFolder(s, c, s >= MinPreviewSize ? previews : null, style, material));
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

    // One pass over the icon folder for many folders at once: removes each listed folder's
    // icons except the one it uses now (and except icons less than a minute old). Icons of
    // folders that aren't listed are left alone. Returns the bytes freed.
    public static long CleanUpAll(IEnumerable<(string Folder, string? Current)> folders)
    {
        if (!Directory.Exists(IconDir)) return 0;
        var current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, icon) in folders) current[FolderHash(folder)] = icon;
        var cutoff = DateTime.Now.AddMinutes(-1);
        long freed = 0;
        foreach (var f in new DirectoryInfo(IconDir).EnumerateFiles($"{FolderIconPrefix}*.ico"))
        {
            var parts = f.Name.Split('_');
            if (parts.Length < 3 || !current.TryGetValue(parts[1], out var keep)) continue;
            if (string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase) || f.LastWriteTime > cutoff) continue;
            long size = f.Length;
            DeleteFolderIcon(f.FullName);
            if (!File.Exists(f.FullName)) freed += size;
        }
        return freed;
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

    public static Bitmap DrawFolder(int size, Color c, IReadOnlyList<Preview>? previews = null, PreviewStyle? style = null,
        FolderMaterial material = FolderMaterial.Classic) =>
        material == FolderMaterial.Glass ? DrawGlass(size, c, previews, style ?? Style)
        : material == FolderMaterial.Liquid ? DrawLiquid(size, c, previews, style ?? Style)
        : Draw(size, Shade(c, -0.2f), (r, alpha) => new LinearGradientBrush(r,
                Color.FromArgb(alpha, Shade(c, 0.16f)), Color.FromArgb(alpha, c), LinearGradientMode.Vertical), previews, style ?? Style);

    // Frosted glass: a deep, tinted back panel, and a translucent front panel that shows a
    // blurred copy of whatever is behind it (the back panel and the file previews), with a
    // soft sheen and a bright rim along the top edge.
    static Bitmap DrawGlass(int size, Color c, IReadOnlyList<Preview>? previews, PreviewStyle style)
    {
        float k = size / 256f;
        var (scene, top) = GlassBack(size, Color.FromArgb(185, Shade(c, 0.08f)), Color.FromArgb(218, Shade(c, -0.34f)),
            rimAlpha: 80, outline: false, previews, style, emptyTop: 84);
        using var _ = scene;

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var o = Canvas(bmp, 1);
        o.DrawImage(scene, 0, 0, size, size);
        o.ScaleTransform(k, k);

        var fr = new RectangleF(16, top, 224, 214 - top);
        using var front = RoundRect(fr, 14);
        var state = o.Save();
        o.SetClip(front);
        using (var blurred = Blur(scene, 14))
        {
            o.ResetTransform();
            o.DrawImage(blurred, 0, 0, size, size);
            o.ScaleTransform(k, k);
        }
        // Tint: lighter at the top, like light passing through coloured glass.
        using (var tint = new LinearGradientBrush(RectangleF.Inflate(fr, 0, 1),
                   Color.FromArgb(118, Shade(c, 0.4f)), Color.FromArgb(165, Shade(c, -0.02f)), 90f))
            o.FillPath(tint, front);
        // Sheen over the upper part of the panel.
        var sheen = new RectangleF(fr.X, fr.Y, fr.Width, fr.Height * 0.75f);
        using (var gloss = new LinearGradientBrush(RectangleF.Inflate(sheen, 0, 2), Color.White, Color.Transparent, 90f)
        {
            WrapMode = WrapMode.TileFlipXY,
            // Eases out instead of stopping at a hard edge.
            InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(110, 255, 255, 255), Color.FromArgb(45, 255, 255, 255), Color.FromArgb(12, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)],
                Positions = [0f, 0.3f, 0.65f, 1f],
            },
        })
            o.FillRectangle(gloss, sheen);
        o.Restore(state);

        // Bright rim on top fading out downwards, and a thin darker outline for definition.
        using (var rimBrush = new LinearGradientBrush(RectangleF.Inflate(fr, 0, 1),
                   Color.FromArgb(220, 255, 255, 255), Color.FromArgb(30, 255, 255, 255), 90f))
        using (var rim = new Pen(rimBrush, 2f))
        using (var inner = RoundRect(RectangleF.Inflate(fr, -1, -1), 13))
            o.DrawPath(rim, inner);
        using (var edge = new Pen(Color.FromArgb(90, Shade(c, -0.55f)), 1.2f))
            o.DrawPath(edge, front);
        return bmp;
    }

    // The back panel and the file previews of a glass folder, i.e. everything the front glass
    // panel shows through. Returns where the front panel starts.
    static (Bitmap scene, float top) GlassBack(int size, Color bodyTop, Color bodyBottom, int rimAlpha, bool outline,
        IReadOnlyList<Preview>? previews, PreviewStyle style, float emptyTop)
    {
        var scene = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Canvas(scene, size / 256f);
        using (var b = new LinearGradientBrush(new RectangleF(16, 29, 224, 186), bodyTop, bodyBottom, 90f))
        using (var tab = RoundRect(new RectangleF(16, 30, 100, 40), 12))
        using (var body = RoundRect(new RectangleF(16, 46, 224, 168), 14))
        {
            g.FillPath(b, tab);
            g.FillPath(b, body);
        }
        using (var rim = new Pen(Color.FromArgb(rimAlpha, 255, 255, 255), 2f))
            g.DrawLine(rim, 29, 31.5f, 103, 31.5f);
        if (outline)
        {
            using var bodyRim = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f);
            using var bodyEdge = RoundRect(new RectangleF(16.7f, 46.7f, 222.6f, 166.6f), 13.3f);
            g.DrawPath(bodyRim, bodyEdge);
        }
        return (scene, previews is { Count: > 0 } ? DrawContents(g, previews, style).top : emptyTop);
    }

    // Opacity of the liquid-glass body (top and bottom of its gradient): low enough that the
    // wallpaper shows through, high enough that the color still reads at 16 px.
    const int LiquidBodyTop = 140, LiquidBodyBottom = 192;

    // Liquid glass: almost clear, a little magnification where the glass curves at its edges,
    // a light tint that deepens towards the bottom, and specular rims (bright top-left,
    // softer bottom-right) that give the panel its thickness. The body itself is partly
    // see-through, so the wallpaper shows through like macOS glass.
    static Bitmap DrawLiquid(int size, Color c, IReadOnlyList<Preview>? previews, PreviewStyle style)
    {
        float k = size / 256f;
        var (scene, top) = GlassBack(size, Color.FromArgb(LiquidBodyTop, Shade(c, 0.18f)), Color.FromArgb(LiquidBodyBottom, Shade(c, -0.28f)),
            rimAlpha: 150, outline: true, previews, style, emptyTop: 92);
        using var _ = scene;

        var fr = new RectangleF(16, top, 224, 214 - top);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var o = Canvas(bmp, 1);
        o.DrawImage(scene, 0, 0, size, size);
        o.ScaleTransform(k, k);
        using var front = RoundRect(fr, 16);

        var state = o.Save();
        o.SetClip(front);
        using (var lensed = Refract(scene, new RectangleF(fr.X * k, fr.Y * k, fr.Width * k, fr.Height * k), 24 * k, 12 * k))
        {
            o.ResetTransform();
            o.DrawImage(lensed, 0, 0, size, size);
            o.ScaleTransform(k, k);
        }
        using (var tint = new LinearGradientBrush(RectangleF.Inflate(fr, 0, 1),
                   Color.FromArgb(28, Shade(c, 0.6f)), Color.FromArgb(70, Shade(c, 0.05f)), 90f))
            o.FillPath(tint, front);
        // A thin sheen along the top, and a little shade at the bottom for thickness.
        var sheen = new RectangleF(fr.X, fr.Y, fr.Width, Math.Min(26, fr.Height * 0.3f));
        using (var gloss = new LinearGradientBrush(RectangleF.Inflate(sheen, 0, 1),
                   Color.FromArgb(85, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            o.FillRectangle(gloss, sheen);
        var shade = new RectangleF(fr.X, fr.Bottom - Math.Min(22, fr.Height * 0.3f), fr.Width, Math.Min(22, fr.Height * 0.3f));
        using (var dark = new LinearGradientBrush(RectangleF.Inflate(shade, 0, 1),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(40, Shade(c, -0.6f)), 90f))
            o.FillRectangle(dark, shade);
        o.Restore(state);

        // Specular rim: bright at the top-left, fading, then a softer glint at the bottom-right.
        using (var rimBrush = new LinearGradientBrush(RectangleF.Inflate(fr, 2, 2), Color.White, Color.White, 35f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(240, 255, 255, 255), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(25, 255, 255, 255),
                          Color.FromArgb(60, 255, 255, 255), Color.FromArgb(170, 255, 255, 255)],
                Positions = [0f, 0.25f, 0.55f, 0.8f, 1f],
            },
        })
        using (var rim = new Pen(rimBrush, 2.2f))
        using (var inner = RoundRect(RectangleF.Inflate(fr, -1.1f, -1.1f), 15))
            o.DrawPath(rim, inner);
        using (var glow = new Pen(Color.FromArgb(45, 255, 255, 255), 1.2f))
        using (var deeper = RoundRect(RectangleF.Inflate(fr, -4, -4), 12))
            o.DrawPath(glow, deeper);
        using (var edge = new Pen(Color.FromArgb(80, Shade(c, -0.6f)), 1f))
            o.DrawPath(edge, front);
        return bmp;
    }

    // Lens effect for a rounded slab of glass: near its edges the content behind is sampled
    // from further inside, so it looks magnified and bent, like light through a curved rim.
    static Bitmap Refract(Bitmap src, RectangleF area, float bevel, float strength)
    {
        int w = src.Width, h = src.Height;
        var rect = new Rectangle(0, 0, w, h);
        var input = new int[w * h];
        var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, input, y * w, w);
        }
        finally { src.UnlockBits(data); }

        var output = (int[])input.Clone();
        int x0 = Math.Max(0, (int)area.Left), x1 = Math.Min(w, (int)Math.Ceiling(area.Right));
        int y0 = Math.Max(0, (int)area.Top), y1 = Math.Min(h, (int)Math.Ceiling(area.Bottom));
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                // How deep into each edge's curve this pixel is (1 at the edge, 0 past the bevel).
                float l = Curve((px - area.Left) / bevel), r = Curve((area.Right - px) / bevel);
                float t = Curve((py - area.Top) / bevel), b = Curve((area.Bottom - py) / bevel);
                float sx = px + (l - r) * strength, sy = py + (t - b) * strength;
                output[y * w + x] = Sample(input, w, h, sx - 0.5f, sy - 0.5f);
            }

        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var outData = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(output, y * w, outData.Scan0 + y * outData.Stride, w);
        }
        finally { dst.UnlockBits(outData); }
        return dst;

        static float Curve(float d) => d >= 1 ? 0 : (1 - Math.Max(d, 0)) * (1 - Math.Max(d, 0));
    }

    // Bilinear sample with edge clamping, per channel.
    static int Sample(int[] px, int w, int h, float x, float y)
    {
        x = Math.Clamp(x, 0, w - 1.001f);
        y = Math.Clamp(y, 0, h - 1.001f);
        int ix = (int)x, iy = (int)y;
        float fx = x - ix, fy = y - iy;
        int a = px[iy * w + ix], b = px[iy * w + ix + 1], c = px[(iy + 1) * w + ix], d = px[(iy + 1) * w + ix + 1];
        int result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float top = ((a >> shift) & 255) * (1 - fx) + ((b >> shift) & 255) * fx;
            float bottom = ((c >> shift) & 255) * (1 - fx) + ((d >> shift) & 255) * fx;
            result |= ((int)(top * (1 - fy) + bottom * fy + 0.5f) & 255) << shift;
        }
        return result;
    }

    static Graphics Canvas(Bitmap bmp, float scale)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        if (scale != 1) g.ScaleTransform(scale, scale);
        return g;
    }

    // Cheap, smooth blur: shrink the image by `factor`, then scale it back up with bicubic
    // filtering. Also used for the settings window's glass backdrop.
    public static Bitmap Blur(Bitmap src, int factor)
    {
        int w = Math.Max(6, src.Width / factor), h = Math.Max(6, src.Height / factor);
        using var tiny = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(tiny))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        var big = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(tiny, new Rectangle(0, 0, src.Width, src.Height), 0, 0, w, h, GraphicsUnit.Pixel);
        }
        return big;
    }

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

        float top = 78;
        int frontAlpha = 255;
        if (previews is { Count: > 0 })
            (top, frontAlpha) = DrawContents(g, previews, style);

        var fr = new RectangleF(16, top, 224, 214 - top);
        using (var fb = front(RectangleF.Inflate(fr, 1, 1), frontAlpha))
        using (var p = RoundRect(fr, 14))
            g.FillPath(fb, p);
        using (var hl = new Pen(Color.FromArgb(70, 255, 255, 255), 3f))
            g.DrawLine(hl, 30, top + 2.5f, 226, top + 2.5f);
        return bmp;
    }

    // Draws the file previews between the back and front panels. Returns where the front
    // panel starts (it drops lower so the files are what you see) and how opaque it is.
    static (float top, int frontAlpha) DrawContents(Graphics g, IReadOnlyList<Preview> previews, PreviewStyle style)
    {
        switch (style)
        {
            case PreviewStyle.Card:
                DrawCard(g, previews[0], new RectangleF(30, 50, 196, 132), 0);
                return (142, 240);
            case PreviewStyle.Fan:
                var slots = new[] { (x: 98f, y: 112f, a: -9f), (x: 158f, y: 112f, a: 9f), (x: 128f, y: 104f, a: 0f) };
                var shown = previews.Take(3).Reverse().ToList(); // newest drawn last, so on top
                for (int i = 0; i < shown.Count; i++)
                {
                    var (x, y, a) = shown.Count == 1 ? slots[2] : slots[3 - shown.Count + i];
                    DrawCard(g, shown[i], new RectangleF(x - 70, y - 50, 140, 100), a);
                }
                return (146, 240);
            case PreviewStyle.Window:
                using (var body = RoundRect(new RectangleF(22, 52, 212, 160), 10))
                {
                    var state = g.Save();
                    g.SetClip(body);
                    DrawCover(g, previews[0].Image, new RectangleF(22, 52, 212, 160));
                    g.Restore(state);
                }
                return (160, 225);
            default:
                return (78, 255);
        }
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
        // Classic 32-bit bitmaps up to 48 px (some older Windows code paths can't read PNG
        // frames at small sizes), compressed PNG from 64 px up: the large uncompressed frames
        // made each icon about 160 KB, which adds up when many folders are colored.
        var pngs = Sizes.Select(s =>
        {
            using var bmp = render(s);
            if (s <= 48) return ToDib(bmp);
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
