using System.Runtime.InteropServices;
using System.Text;

namespace FolderColorizer;

// Sets or clears a folder's icon through its desktop.ini, the same way Explorer's
// own "Change Icon" does, so nothing else about the folder is touched.
static class FolderIcon
{
    const string Section = ".ShellClassInfo";
    // Our own section: the chosen color, and which files the icon currently shows.
    const string OwnSection = "FolderColorizer";
    const int MaxPreviews = 3;
    // Thumbnails are cropped to fill the card, so fetch them larger than the icon.
    const int ThumbSize = 512;
    static readonly string[] IconKeys = ["IconResource", "IconFile", "IconIndex"];
    static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".avif",
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v",
    };

    // The time of the click, taken before waiting for the lock: if two requests overlap
    // (two quick clicks, or a click while the tray is redrawing), the newer one wins.
    public static void Apply(string folder, Color color)
    {
        long requested = DateTime.UtcNow.Ticks;
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Folder not found: {folder}");
        using (Lock(folder))
            ApplyLocked(folder, color, requested);
    }

    static void ApplyLocked(string folder, Color color, long requested)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        if (long.TryParse(Read(ini, OwnSection, "Stamp"), out var newest) && newest > requested)
            return;

        var style = AppSettings.Load().PreviewStyle;
        var files = PreviewFilesFor(folder, style);
        var previews = LoadPreviews(files);
        string ico;
        try
        {
            ico = previews.Count == 0 ? IconMaker.EnsureIcon(color) : IconMaker.WriteFolderIcon(folder, color, previews, style);
        }
        finally
        {
            previews.ForEach(p => p.Dispose());
        }

        bool wasColored = Read(ini, OwnSection, "Color") != null;
        if (File.Exists(ini))
            File.SetAttributes(ini, FileAttributes.Normal);
        else
            // UTF-16 so folder and icon paths with Cyrillic or other non-Latin names survive;
            // the shell would create it in the ANSI code page. It holds the icon line from
            // the start, because Explorer may read the file the moment it appears.
            File.WriteAllText(ini, $"[{Section}]\r\nIconResource={ico},0\r\n", Encoding.Unicode);

        // Icon keys other tools may have left behind would compete with ours.
        foreach (var key in IconKeys.Skip(1))
            Native.WritePrivateProfileString(Section, key, null, ini);
        Native.WritePrivateProfileString(OwnSection, "Color", Palette.Hex(color), ini);
        Native.WritePrivateProfileString(OwnSection, "Files", Signature(files), ini);
        Native.WritePrivateProfileString(OwnSection, "Stamp", requested.ToString(), ini);

        // Through the shell, so every Explorer window (the desktop too) drops what it cached
        // for the folder. It also hides desktop.ini and marks the folder as customized.
        Native.SetFolderIcon(folder, ico);
        // When a folder that Windows was drawing as its own (with a content preview) gets its
        // first color, the desktop misses the change, shows a blank folder and remembers that
        // icon file as failed. Repeating it a moment later with a copy under a new name works.
        if (!wasColored)
        {
            Thread.Sleep(1500);
            var missed = ico;
            ico = IconMaker.CopyAsFolderIcon(folder, ico);
            File.SetAttributes(ini, FileAttributes.Normal);
            Native.WritePrivateProfileString(Section, "IconResource", ico + ",0", ini);
            Native.SetFolderIcon(folder, ico);
            // Never displayed, so nothing can still be reading it.
            IconMaker.DeleteFolderIcon(missed);
        }
        IconMaker.DeleteStaleFolderIcons(folder, ico);
        FolderList.Add(folder);
    }

    // Removes this folder's icons that are no longer in use (used by the tray at start-up to
    // clear what older versions or interrupted changes left behind).
    public static void CleanUpIcons(string folder)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        if (!Directory.Exists(folder) || Read(ini, OwnSection, "Color") == null)
            return;
        var current = Read(ini, Section, "IconResource");
        if (current == null)
            return;
        using (Lock(folder))
            IconMaker.DeleteStaleFolderIcons(folder, current[..current.LastIndexOf(',')]);
    }

    // Rebuilds the icon if the files it should show have changed. Returns false
    // when the folder is gone or no longer colored by this app.
    public static bool Update(string folder)
    {
        if (!Directory.Exists(folder))
            return false;
        // The color is read inside the lock, so a redraw can never bring back a color
        // that was replaced while it was waiting.
        using (Lock(folder))
        {
            var ini = Path.Combine(folder, "desktop.ini");
            var hex = Read(ini, OwnSection, "Color");
            if (hex == null)
                return false;
            if (Read(ini, OwnSection, "Files") != Signature(PreviewFilesFor(folder, AppSettings.Load().PreviewStyle)))
                ApplyLocked(folder, Palette.FromHex(hex), DateTime.UtcNow.Ticks);
            return true;
        }
    }

    // Redraws a colored folder with its current color (after the preview style changed).
    public static void Redraw(string folder)
    {
        var hex = ColorOf(folder);
        if (hex != null)
            Apply(folder, hex.Value);
    }

    // The color this app gave the folder, or null if it isn't colored by this app.
    public static Color? ColorOf(string folder)
    {
        var hex = Read(Path.Combine(folder, "desktop.ini"), OwnSection, "Color");
        return hex == null ? null : Palette.FromHex(hex);
    }

    // The icon file the folder currently uses, if any.
    public static string? IconOf(string folder)
    {
        var res = Read(Path.Combine(folder, "desktop.ini"), Section, "IconResource");
        return res == null ? null : res[..(res.LastIndexOf(',') is var i and > 0 ? i : res.Length)];
    }

    public static List<FileInfo> PreviewFilesFor(string folder, PreviewStyle style) =>
        style == PreviewStyle.Plain ? [] : PickPreviewFiles(folder);

    public static void Reset(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        using var _ = Lock(folder);
        var ini = Path.Combine(folder, "desktop.ini");
        if (File.Exists(ini))
        {
            File.SetAttributes(ini, FileAttributes.Normal);
            Native.WritePrivateProfileString(OwnSection, null, null, ini);
            foreach (var key in IconKeys.Skip(1))
                Native.WritePrivateProfileString(Section, key, null, ini);
            // desktop.ini stays (now without an icon) and the folder stays marked as customized:
            // Windows shows its normal folder for that, and a later color is then an ordinary
            // change. Deleting both makes the desktop miss the next color.
            Native.SetFolderIcon(folder, null);
        }
        // Windows now draws its own folder, so none of this folder's icons are needed.
        IconMaker.DeleteStaleFolderIcons(folder, null, TimeSpan.Zero);
        FolderList.Remove(folder);
    }

    public static List<Preview> LoadPreviews(List<FileInfo> files) =>
        files.Select(f => Thumbs.Get(f, ThumbSize)).OfType<Preview>().ToList();

    // Photos and videos first (they make the best previews), newest first.
    public static List<FileInfo> PickPreviewFiles(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .OrderByDescending(f => MediaExtensions.Contains(f.Extension))
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxPreviews)
            .ToList();

    static string Signature(List<FileInfo> files) =>
        files.Count == 0 ? "-" : string.Join("|", files.Select(f => $"{f.Name}:{f.LastWriteTimeUtc.Ticks:x}"));

    // One writer per folder across all processes (context menu, shortcut, tray watcher).
    static IDisposable Lock(string folder)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())));
        var mutex = new Mutex(false, $"FolderColorizer.Folder.{hash}");
        bool owned;
        try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { owned = true; }
        return new Releaser(mutex, owned);
    }

    sealed class Releaser(Mutex mutex, bool owned) : IDisposable
    {
        public void Dispose()
        {
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPrivateProfileString(string section, string key, string? fallback, StringBuilder value, int size, string file);

    static string? Read(string ini, string section, string key)
    {
        if (!File.Exists(ini)) return null;
        var sb = new StringBuilder(1024);
        GetPrivateProfileString(section, key, "\u0001", sb, sb.Capacity, ini);
        var value = sb.ToString();
        return value == "\u0001" ? null : value;
    }
}
