namespace FolderColorizer;

// Runs inside the tray process: watches every colored folder and redraws its icon
// shortly after files are added, removed or changed, so the previews stay current.
sealed class FolderWatcher : IDisposable
{
    const int SettleMs = 1500;

    readonly Control ui;
    readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, System.Windows.Forms.Timer> pending = new(StringComparer.OrdinalIgnoreCase);
    readonly FileSystemWatcher listWatcher;

    public FolderWatcher(Control ui)
    {
        this.ui = ui;
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
        // that earlier versions or interrupted changes left behind (off the UI thread).
        var folders = watchers.Keys.ToArray();
        StaWorker.Run(() =>
        {
            foreach (var folder in folders)
            {
                try { FolderIcon.CleanUpIcons(folder); }
                catch (Exception) { } // one odd folder must not stop the tray
            }
        });
        foreach (var folder in folders)
            Schedule(folder);
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

        foreach (var gone in watchers.Keys.Except(folders, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            watchers[gone].Dispose();
            watchers.Remove(gone);
        }
        foreach (var folder in folders)
        {
            if (watchers.ContainsKey(folder) || !Directory.Exists(folder)) continue;
            try
            {
                var w = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                FileSystemEventHandler onChange = (_, e) =>
                {
                    // Our own desktop.ini writes would otherwise trigger an endless loop.
                    if (!string.Equals(e.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                        Post(() => Schedule(folder));
                };
                w.Created += onChange;
                w.Deleted += onChange;
                w.Changed += onChange;
                w.Renamed += (s, e) => onChange(s!, e);
                // Buffer overflow, network drop, folder deleted: start over with a fresh watcher.
                w.Error += (_, _) => Post(() =>
                {
                    if (watchers.Remove(folder, out var broken)) broken.Dispose();
                    Sync();
                    Schedule(folder);
                });
                w.EnableRaisingEvents = true;
                watchers[folder] = w;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
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
    static void Refresh(string folder) => StaWorker.Run(() =>
    {
        try
        {
            if (!FolderIcon.Update(folder))
                FolderList.Remove(folder);
        }
        catch (Exception) { }
    });

    public void Dispose()
    {
        listWatcher.Dispose();
        foreach (var w in watchers.Values) w.Dispose();
        foreach (var t in pending.Values) t.Dispose();
    }
}
