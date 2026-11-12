namespace FolderColorizer;

// Finds the folders "Color many folders" would change, and the ones it leaves alone (with why).
sealed class BulkScan
{
    public const int Limit = 2000;

    public List<string> Targets { get; } = [];
    public int AlreadyColored { get; private set; }
    public int Customized { get; private set; }
    public bool HitLimit { get; private set; }

    // Folders a bulk change must never touch: Windows itself, installed programs, app data.
    public static string? Refusal(string root, bool deep)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (deep && Path.GetPathRoot(root) is { } drive && Path.TrimEndingDirectorySeparator(drive).Equals(root, StringComparison.OrdinalIgnoreCase))
            return "Choose a folder, not a whole drive, when folders inside subfolders are included.";
        var off = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
        }.Select(Environment.GetFolderPath).Where(p => p.Length > 0);
        foreach (var p in off)
            if (root.Equals(p, StringComparison.OrdinalIgnoreCase) || root.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "Windows, program and app-data folders are left alone.";
        return null;
    }

    public static BulkScan Run(string root, bool deep, bool recolor)
    {
        var scan = new BulkScan();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = deep,
            IgnoreInaccessible = true,
            // Hidden, system and linked folders (junctions, OneDrive placeholders' links) are skipped.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        foreach (var dir in Directory.EnumerateDirectories(root, "*", options))
        {
            if (FolderIcon.ColorOf(dir) != null)
            {
                if (!recolor) { scan.AlreadyColored++; continue; }
            }
            else if (FolderIcon.HasForeignIcon(dir))
            {
                scan.Customized++;
                continue;
            }
            if (scan.Targets.Count >= Limit) { scan.HitLimit = true; break; }
            scan.Targets.Add(dir);
        }
        return scan;
    }
}
