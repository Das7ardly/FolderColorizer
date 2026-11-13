namespace FolderColorizer;

// Runs inside the tray process: watches the colored folders and redraws a folder's icon
// shortly after files in it are added, removed or changed, so the previews stay current.
//
// Instead of one watcher per folder (thousands of handles once many folders are colored),
// it watches the few parent folders that contain them all, including subfolders, and picks
// out the events that belong to a colored folder.
sealed class FolderWatcher : IDisposable
{
    const int SettleMs = 1500;

    readonly Control ui;
    readonly List<FileSystemWatcher> roots = [];
    readonly Dictionary<string, System.Windows.Forms.Timer> pending = new(StringComparer.OrdinalIgnoreCase);
    readonly FileSystemWatcher listWatcher;
    // Replaced as a whole on every change, so watcher threads can read it without locking.
    volatile HashSet<string> colored = new(StringComparer.OrdinalIgnoreCase);
    // Redraws run one at a time on a single STA thread (thumbnails use COM): at sign-in every
    // colored folder is checked, and doing them all at once would spike disk and CPU. It is a
    // background thread so the tray can quit promptly (e.g. for an update).
    readonly System.Collections.Concurrent.BlockingCollection<Action> work = new();

    public FolderWatcher(Control ui)
    {
        this.ui = ui;
        var worker = new Thread(() =>
        {
            foreach (var job in work.GetConsumingEnumerable())
            {
                try { job(); }
                catch (Exception) { } // one odd folder must not stop the others
            }
        }) { IsBackground = true, Name = "Folder redraws" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Directory.CreateDirectory(IconMaker.DataDir);
        listWatcher = new FileSystemWatcher(IconMaker.DataDir, Path.GetFileName(FolderList.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        listWatcher.Changed += (_, _) => Post(Sync);
        listWatcher.Created += (_, _) => Post(Sync);
        listWatcher.EnableRaisingEvents = true;

        Sync();
        // Catch up on anything that changed while the tray wasn't running, and clear icons
        // that earlier versions or interrupted changes left behind. Queued directly (no
        // per-folder timers), since there can be thousands.
        var all = colored.ToArray();
        work.Add(() => FolderIcon.CleanUpIcons(all));
        foreach (var folder in all) Refresh(folder);
    }

    // Marshals to the tray's UI thread; quietly does nothing while the tray is shutting down.
    void Post(Action action)
    {
        try { if (ui.IsHandleCreated && !ui.IsDisposed) ui.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    void Sync()
    {
        string[] folders;
        try { folders = FolderList.All(); }
        catch (IOException) { return; }

        var set = new HashSet<string>(folders.Where(Directory.Exists), StringComparer.OrdinalIgnoreCase);
        colored = set;
        var wanted = TopLevel(set.Select(f => Path.GetDirectoryName(f) ?? f));
        if (wanted.SetEquals(roots.Select(r => r.Path))) return;

        foreach (var r in roots) r.Dispose();
        roots.Clear();
        foreach (var path in wanted)
        {
            try
            {
                var w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024, // busy trees (builds, downloads) overflow the 8 KB default
                };
                w.Created += OnChange;
                w.Deleted += OnChange;
                w.Changed += OnChange;
                w.Renamed += (s, e) => { OnChange(s, e); Matched(e.OldFullPath); };
                // Buffer overflow, network drop, folder deleted: start over and re-check everything.
                w.Error += (_, _) => Post(() =>
                {
                    foreach (var r in roots) r.Dispose();
                    roots.Clear();
                    Sync();
                    foreach (var f in colored) Refresh(f);
                });
                w.EnableRaisingEvents = true;
                roots.Add(w);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
    }

    // The fewest folders that contain all the given ones.
    static HashSet<string> TopLevel(IEnumerable<string> paths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths.Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length))
            if (!result.Any(r => p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || p.Equals(r, StringComparison.OrdinalIgnoreCase)))
                result.Add(p);
        return result;
    }

    void OnChange(object? sender, FileSystemEventArgs e)
    {
        // Our own desktop.ini writes would otherwise trigger an endless loop.
        if (!string.Equals(e.Name is { } n ? Path.GetFileName(n) : null, "desktop.ini", StringComparison.OrdinalIgnoreCase))
            Matched(e.FullPath);
    }

    // A file changed: if it sits directly in a colored folder, redraw that folder soon.
    void Matched(string path)
    {
        if (Path.GetDirectoryName(path) is { } dir && colored.Contains(dir))
            Post(() => Schedule(dir));
    }

    // Copying many files fires many events; wait until things settle, then redraw once.
    void Schedule(string folder)
    {
        if (!pending.TryGetValue(folder, out var timer))
        {
            timer = new System.Windows.Forms.Timer { Interval = SettleMs };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                pending.Remove(folder);
                timer.Dispose();
                Refresh(folder);
            };
            pending[folder] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    // Off the UI thread (thumbnails can be slow); any failure just leaves the old icon.
    void Refresh(string folder)
    {
        if (work.IsAddingCompleted) return;
        work.Add(() =>
        {
            if (!FolderIcon.Update(folder))
                FolderList.Remove(folder);
        });
    }

    public void Dispose()
    {
        work.CompleteAdding();
        listWatcher.Dispose();
        foreach (var r in roots) r.Dispose();
        foreach (var t in pending.Values) t.Dispose();
    }
}
