using System.Drawing.Drawing2D;

namespace FolderColorizer.Ui;

// The look pickers shared by the Color, Color many and Settings pages, and the sample
// pictures that stand in for "the files inside" when there is no real folder.
static class LookChoices
{
    // Segment order of the pickers below.
    public static readonly FolderMaterial[] Materials = [FolderMaterial.Classic, FolderMaterial.Glass, FolderMaterial.Liquid];
    public static readonly PreviewStyle[] Styles = [PreviewStyle.Window, PreviewStyle.Card, PreviewStyle.Fan, PreviewStyle.Plain];
    public static readonly string[] StyleNames = ["Window", "Card", "Fan", "Plain"];

    public static Segmented MaterialPicker() => new("Classic", "Frosted", "Liquid glass");
    public static Segmented StylePicker() => new(StyleNames);

    public static FolderMaterial MaterialAt(Segmented s) => Materials[s.SelectedIndex];
    public static PreviewStyle StyleAt(Segmented s) => Styles[s.SelectedIndex];
    public static void Show(Segmented materials, Segmented styles, Look look)
    {
        materials.SetQuietly(Array.IndexOf(Materials, look.Material));
        styles.SetQuietly(Array.IndexOf(Styles, look.Style));
    }

    // Small landscapes (sky, sun, hills) in different colours. The caller disposes them.
    public static List<Preview> SamplePictures(int count = 3)
    {
        var palettes = new[] { ("38BDF8", "16A34A"), ("F59E0B", "7C3AED"), ("F472B6", "0EA5E9") };
        var list = new List<Preview>();
        foreach (var (sky, hill) in palettes.Take(count))
        {
            var bmp = new Bitmap(400, 300);
            using var g = Graphics.FromImage(bmp);
            Theme.Smooth(g);
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, 400, 300), Theme.Hex(sky), Color.White, 90f)) g.FillRectangle(b, 0, 0, 400, 300);
            g.FillEllipse(Brushes.LemonChiffon, 270, 50, 70, 70);
            using (var b = new SolidBrush(Theme.Hex(hill))) g.FillEllipse(b, -80, 170, 380, 260);
            using (var b = new SolidBrush(ControlPaint.Dark(Theme.Hex(hill), 0.1f))) g.FillEllipse(b, 160, 190, 360, 240);
            list.Add(new Preview(bmp, true));
        }
        return list;
    }
}
