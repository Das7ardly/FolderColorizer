using System.Runtime.InteropServices;

namespace FolderColorizer;

// Reads which folders are selected in a File Explorer window (the active tab on Windows 11).
static class ExplorerSelection
{
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    }

    // Only the IOleWindow part of IShellBrowser is needed.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
    }

    static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    // Selected folders, or the open folder itself when nothing is selected.
    public static string[] Get(IntPtr explorerHwnd)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type == null) return [];
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            IntPtr activeTab = Native.FindWindowEx(explorerHwnd, IntPtr.Zero, "ShellTabWindowClass", null);
            dynamic? match = null;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    object? item = windows.Item(i);
                    if (item == null) continue;
                    dynamic w = item;
                    if (Convert.ToInt64(w.HWND) != explorerHwnd.ToInt64()) continue;
                    match ??= w;
                    if (activeTab != IntPtr.Zero && TabOf(item) == activeTab)
                    {
                        match = w;
                        break;
                    }
                }
                catch (COMException) { }
            }
            return match == null ? [] : Read(match);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    static string[] Read(dynamic window)
    {
        dynamic doc = window.Document;
        dynamic items = doc.SelectedItems();
        int count = items.Count;
        var result = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string path = items.Item(i).Path;
            if (Directory.Exists(path)) result.Add(path);
        }
        if (count == 0)
        {
            string current = doc.Folder.Self.Path;
            if (Directory.Exists(current)) result.Add(current);
        }
        return result.ToArray();
    }

    static IntPtr TabOf(object window)
    {
        try
        {
            var sid = SID_STopLevelBrowser;
            var iid = typeof(IShellBrowser).GUID;
            ((IServiceProvider)window).QueryService(ref sid, ref iid, out var browser);
            ((IShellBrowser)browser).GetWindow(out var hwnd);
            return hwnd;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}
