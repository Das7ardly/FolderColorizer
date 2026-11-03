namespace FolderColorizer;

// The folders this app has colored, so the tray can keep their previews up to date.
// Several processes may touch the list at once (one per folder in a multi-select).
static class FolderList
{
    public static string FilePath => Path.Combine(IconMaker.DataDir, "folders.txt");

    public static string[] All() => Locked(Read);

    public static void Add(string folder) => Locked(() =>
    {
        var list = Read();
        if (!list.Contains(folder, StringComparer.OrdinalIgnoreCase))
            Write([.. list, folder]);
        return list;
    });

    public static void Remove(string folder) => Locked(() =>
    {
        var list = Read();
        Write(list.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)).ToArray());
        return list;
    });

    static string[] Read() =>
        File.Exists(FilePath) ? File.ReadAllLines(FilePath).Where(l => l.Length > 0).ToArray() : [];

    static void Write(string[] list)
    {
        Directory.CreateDirectory(IconMaker.DataDir);
        File.WriteAllLines(FilePath, list);
    }

    static string[] Locked(Func<string[]> body)
    {
        using var mutex = new Mutex(false, "FolderColorizer.List.v1");
        bool owned;
        try { owned = mutex.WaitOne(5000); }
        catch (AbandonedMutexException) { owned = true; }
        try { return body(); }
        finally { if (owned) mutex.ReleaseMutex(); }
    }
}
