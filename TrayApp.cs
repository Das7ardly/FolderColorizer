using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FolderColorizer;

// Background mode: a tray icon plus a keyboard hook that opens the color picker
// when Ctrl+Shift+F is pressed while File Explorer is in front.
static class TrayApp
{
    public static void Run()
    {
        using var mutex = new Mutex(true, Setup.TrayMutexName, out bool first);
        if (!first) return;
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, Setup.QuitEventName);
        Application.Run(new TrayContext(quit));
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly Control ui = new();
    readonly NotifyIcon tray;
    readonly KeyHook hook;
    readonly FolderWatcher watcher;
    PickerForm? open;

    public TrayContext(EventWaitHandle quit)
    {
        _ = ui.Handle; // create the handle so BeginInvoke works

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => Process.Start(Environment.ProcessPath!));
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        tray = new NotifyIcon
        {
            Icon = new Icon(IconMaker.EnsureAppIcon(), SystemInformation.SmallIconSize),
            Text = "Folder Colorizer — Ctrl+Shift+F in File Explorer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => Process.Start(Environment.ProcessPath!);

        hook = new KeyHook(hwnd => ui.BeginInvoke(() => ShowPicker(hwnd)));
        watcher = new FolderWatcher(ui);
        ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => ui.BeginInvoke(ExitThread), null, Timeout.Infinite, true);
    }

    void ShowPicker(IntPtr explorer)
    {
        string[] folders;
        try { folders = ExplorerSelection.Get(explorer); }
        // Views like Home or Gallery aren't normal folder views and can fail in several ways.
        catch (Exception) { return; }
        if (folders.Length == 0) return;

        open?.Close();
        open = new PickerForm(folders);
        open.FormClosed += (s, _) => ((Form)s!).Dispose();
        open.ShowNear(Cursor.Position);
    }

    protected override void ExitThreadCore()
    {
        hook.Dispose();
        watcher.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}

sealed class KeyHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_F = 0x46;

    readonly Native.LowLevelKeyboardProc proc; // kept alive so the GC doesn't collect the callback
    readonly Action<IntPtr> onHotkey;
    readonly IntPtr handle;

    public KeyHook(Action<IntPtr> onHotkey)
    {
        this.onHotkey = onHotkey;
        proc = Callback;
        handle = Native.SetWindowsHookEx(WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == WM_KEYDOWN && Marshal.ReadInt32(lParam) == VK_F
            && Down(VK_CONTROL) && Down(VK_SHIFT) && !Down(VK_MENU) && !Down(VK_LWIN) && !Down(VK_RWIN))
        {
            var fg = Native.GetForegroundWindow();
            // Only inside File Explorer windows; everywhere else the keys pass through untouched.
            if (Native.ClassOf(fg) == "CabinetWClass")
            {
                onHotkey(fg);
                return 1;
            }
        }
        return Native.CallNextHookEx(handle, code, wParam, lParam);
    }

    static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose() => Native.UnhookWindowsHookEx(handle);
}
