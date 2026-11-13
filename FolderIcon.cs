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
    // look: the preview style and material to use; null keeps the folder's own look
    // (or the default from Settings for a folder that has none yet).
    public static void Apply(string folder, Color color, Look? look = null)
    {
        long requested = DateTime.UtcNow.Ticks;
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Folder not found: {folder}");
        using (Lock(folder))
            ApplyLocked(folder, color, requested, look);
    }

    // The look a folder was given, falling back to the defaults from Settings.
    public static Look LookOf(string folder)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        var defaults = AppSettings.Load();
        return new Look(
            Enum.TryParse<PreviewStyle>(Read(ini, OwnSection, "Style"), out var s) ? s : defaults.PreviewStyle,
            Enum.TryParse<FolderMaterial>(Read(ini, OwnSection, "Material"), out var m) ? m : defaults.Material);
    }

    static void ApplyLocked(string folder, Color color, long requested, Look? look = null)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        if (long.TryParse(Read(ini, OwnSection, "Stamp"), out var newest) && newest > requested)
            return;

        var (style, material) = look ?? LookOf(folder);
        var files = PreviewFilesFor(folder, style);
        var previews = LoadPreviews(files);
        string ico;
        try
        {
            ico = previews.Count == 0
                ? IconMaker.EnsureIcon(color, material)
                : IconMaker.WriteFolderIcon(folder, color, previews, style, material);
        }
        finally
        {
            previews.ForEach(p => p.Dispose());
        }

        bool wasColored = Read(ini, OwnSection, "Color") != null;
        // An icon set by Windows (Documents, Downloads...) or by the user before: remembered
        // so Restore default can put it back.
        string? original = wasColored ? null : CurrentIconSpec(ini);

        try
        {
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
            if (original != null)
                Native.WritePrivateProfileString(OwnSection, "OriginalIcon", original, ini);
            Native.WritePrivateProfileString(OwnSection, "Color", Palette.Hex(color), ini);
            Native.WritePrivateProfileString(OwnSection, "Files", Signature(files), ini);
            Native.WritePrivateProfileString(OwnSection, "Stamp", requested.ToString(), ini);
            Native.WritePrivateProfileString(OwnSection, "Style", style.ToString(), ini);
            Native.WritePrivateProfileString(OwnSection, "Material", material.ToString(), ini);
        }
        finally
        {
            // Never leave desktop.ini visible in the folder, even if a write failed.
            Hide(ini);
        }

        // Through the shell, so every Explorer window (the desktop too) drops what it cached
        // for the folder. It also hides desktop.ini and marks the folder as customized.
        Native.SetFolderIcon(folder, ico);
        // When a desktop folder that Windows was drawing as its own gets its first color, the
        // desktop misses the change, shows a blank folder and remembers that icon file as
        // failed. Repeating it a moment later with a copy under a new name works. Folders
        // elsewhere don't need it, which keeps coloring many folders fast.
        if (!wasColored && IsOnDesktop(folder))
        {
            Thread.Sleep(1500);
            var missed = ico;
            ico = IconMaker.CopyAsFolderIcon(folder, ico);
            try
            {
                File.SetAttributes(ini, FileAttributes.Normal);
                Native.WritePrivateProfileString(Section, "IconResource", ico + ",0", ini);
            }
            finally { Hide(ini); }
            Native.SetFolderIcon(folder, ico);
            // Never displayed, so nothing can still be reading it.
            IconMaker.DeleteFolderIcon(missed);
        }
        IconMaker.DeleteStaleFolderIcons(folder, ico);
        // The list only drives preview updates and the Colored folders page; the color itself is done.
        try { FolderList.Add(folder); }
        catch (IOException) { }
    }

    static void Hide(string ini)
    {
        try
        {
            if (File.Exists(ini))
                File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static bool IsOnDesktop(string folder)
    {
        var parent = Path.GetDirectoryName(folder);
        return parent != null && new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }
            .Any(f => string.Equals(Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(f)), parent, StringComparison.OrdinalIgnoreCase));
    }

    // The folder's current icon as "path,index", from IconResource or the older IconFile/IconIndex.
    static string? CurrentIconSpec(string ini)
    {
        if (Read(ini, Section, "IconResource") is { Length: > 0 } res)
            return res.Contains(',') ? res : res + ",0";
        if (Read(ini, Section, "IconFile") is { Length: > 0 } file)
            return $"{file},{Read(ini, Section, "IconIndex") ?? "0"}";
        return null;
    }

    // True when Windows or another program gave the folder its own icon (e.g. Documents).
    public static bool HasForeignIcon(string folder)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        return File.Exists(ini) && ColorOf(folder) == null && CurrentIconSpec(ini) != null;
    }

    // Removes icons the colored folders no longer use (left by older versions or interrupted
    // changes), in one pass over the icon folder. Returns the bytes freed.
    public static long CleanUpIcons(IEnumerable<string> folders) =>
        IconMaker.CleanUpAll(folders
            .Where(f => Directory.Exists(f) && ColorOf(f) != null)
            .Select(f => (f, IconOf(f))));

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
            if (Read(ini, OwnSection, "Files") != Signature(PreviewFilesFor(folder, LookOf(folder).Style)))
                ApplyLocked(folder, Palette.FromHex(hex), DateTime.UtcNow.Ticks);
            return true;
        }
    }

    // Redraws a colored folder with its current color in the given look (Settings' "Update
    // colored folders" passes the new defaults).
    public static void Redraw(string folder, Look look)
    {
        var hex = ColorOf(folder);
        if (hex != null)
            Apply(folder, hex.Value, look);
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
        long requested = DateTime.UtcNow.Ticks;
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        using var _ = Lock(folder);
        var ini = Path.Combine(folder, "desktop.ini");
        if (File.Exists(ini))
        {
            var original = Read(ini, OwnSection, "OriginalIcon");
            try
            {
                File.SetAttributes(ini, FileAttributes.Normal);
                Native.WritePrivateProfileString(OwnSection, null, null, ini);
                // Keep a stamp, so a color request older than this restore can't win afterwards.
                Native.WritePrivateProfileString(OwnSection, "Stamp", requested.ToString(), ini);
                foreach (var key in IconKeys.Skip(1))
                    Native.WritePrivateProfileString(Section, key, null, ini);
            }
            finally { Hide(ini); }
            // desktop.ini stays and the folder stays marked as customized: Windows shows its own
            // icon for the folder (or the one it had before), and a later color is then an
            // ordinary change. Deleting both makes the desktop miss the next color.
            if (original is { } spec && spec.LastIndexOf(',') is var comma and > 0 && int.TryParse(spec[(comma + 1)..], out var index))
                Native.SetFolderIcon(folder, spec[..comma], index);
            else
                Native.SetFolderIcon(folder, null);
        }
        // Windows now draws its own folder, so none of this folder's icons are needed.
        IconMaker.DeleteStaleFolderIcons(folder, null, TimeSpan.Zero);
        try { FolderList.Remove(folder); }
        catch (IOException) { }
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
        if (!owned)
        {
            // Going ahead without the lock could interleave two writers; refuse instead.
            mutex.Dispose();
            throw new TimeoutException($"{Path.GetFileName(folder)} is busy with another change. Try again in a moment.");
        }
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
