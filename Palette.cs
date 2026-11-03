namespace FolderColorizer;

record NamedColor(string Name, Color Color);

static class Palette
{
    public static readonly NamedColor[] Colors =
    [
        new("Red", Color.FromArgb(0xE5, 0x48, 0x4D)),
        new("Orange", Color.FromArgb(0xF2, 0x8C, 0x28)),
        new("Yellow", Color.FromArgb(0xF2, 0xC0, 0x2B)),
        new("Green", Color.FromArgb(0x3F, 0xB9, 0x50)),
        new("Teal", Color.FromArgb(0x1F, 0xB5, 0xA8)),
        new("Blue", Color.FromArgb(0x2F, 0x7D, 0xF6)),
        new("Indigo", Color.FromArgb(0x5B, 0x5F, 0xE8)),
        new("Purple", Color.FromArgb(0x9B, 0x59, 0xD0)),
        new("Pink", Color.FromArgb(0xE8, 0x5C, 0xA8)),
        new("Brown", Color.FromArgb(0x9C, 0x6B, 0x4A)),
        new("Gray", Color.FromArgb(0x8A, 0x8F, 0x98)),
        new("Black", Color.FromArgb(0x3A, 0x3D, 0x42)),
    ];

    public static string Hex(Color c) => $"{c.R:x2}{c.G:x2}{c.B:x2}";

    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
    }
}
