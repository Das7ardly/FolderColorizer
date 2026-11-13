namespace FolderColorizer;

// Finds the folders "Color many folders" would change, and the ones it leaves alone (with why).
sealed class BulkScan
{
    public const int Limit = 2000;
    // Rough size of one icon with file previews, for the space estimate.
    const long PreviewIconBytes = 95 * 1024;

    public List<string> Targets { get; } = [];
    public int AlreadyColored { get; private set; }
    public int Customized { get; private set; }
    public int InsideApps { get; private set; }
    public int WithFiles { get; private set; }
    public bool HitLimit { get; private set; }

    // Disk space the per-folder preview icons would take (folders without files share one icon).
    public long PreviewBytes => WithFiles * PreviewIconBytes;

    // Tool and program internals: never interesting to color, and often thousands of them.
    static readonly HashSet<string> InternalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", "__pycache__", ".venv", "venv", "site-packages",
        ".vs", ".vscode", ".idea", ".gradle", ".cache", ".npm", ".nuget", "bower_components",
    };

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
        // Hidden, system and linked folders (junctions, symlinks) are skipped and not entered.
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0 && !scan.HitLimit)
        {
            string parent = queue.Dequeue();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(parent, "*", options).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var dir in children)
            {
                if (InternalNames.Contains(Path.GetFileName(dir))) continue;
                scan.Consider(dir, recolor);
                // A program's own folders (a portable app, a game) hold thousands of internal
                // folders; color the program folder itself but don't go inside it.
                if (deep && !IsProgramFolder(dir, options)) queue.Enqueue(dir);
                else if (deep) scan.InsideApps += CountSubfolders(dir, options);
                if (scan.HitLimit) break;
            }
        }
        return scan;
    }

    void Consider(string dir, bool recolor)
    {
        if (FolderIcon.ColorOf(dir) != null)
        {
            if (!recolor) { AlreadyColored++; return; }
        }
        else if (FolderIcon.HasForeignIcon(dir))
        {
            Customized++;
            return;
        }
        if (Targets.Count >= Limit) { HitLimit = true; return; }
        Targets.Add(dir);
        try
        {
            if (Directory.EnumerateFiles(dir, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }).Any())
                WithFiles++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static bool IsProgramFolder(string dir, EnumerationOptions options)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", options).Any() || Directory.EnumerateFiles(dir, "*.dll", options).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // How many folders a program folder holds (for the summary); stops counting at the limit.
    static int CountSubfolders(string dir, EnumerationOptions options)
    {
        try
        {
            return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = options.AttributesToSkip,
            }).Take(Limit).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
