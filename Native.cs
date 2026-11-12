using System.Runtime.InteropServices;
using System.Text;

namespace FolderColorizer;

static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool WritePrivateProfileString(string section, string? key, string? value, string file);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFOLDERCUSTOMSETTINGS
    {
        public int dwSize;
        public uint dwMask;
        public IntPtr pvid;
        public string? pszWebViewTemplate;
        public int cchWebViewTemplate;
        public string? pszWebViewTemplateVersion;
        public string? pszInfoTip;
        public int cchInfoTip;
        public IntPtr pclsid;
        public uint dwFlags;
        public string? pszIconFile;
        public int cchIconFile;
        public int iIconIndex;
        public string? pszLogo;
        public int cchLogo;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS settings, string path, uint readWrite);

    const uint FCSM_ICONFILE = 0x10, FCS_FORCEWRITE = 0x2;

    // Sets (or with null, clears) a folder's icon through the call Explorer's own "Change Icon"
    // uses. Writing desktop.ini by hand works on disk, but Explorer, and the desktop especially,
    // keeps showing what it cached for the folder; this call makes every window drop it.
    public static void SetFolderIcon(string folder, string? icon, int index = 0)
    {
        var s = new SHFOLDERCUSTOMSETTINGS
        {
            dwSize = Marshal.SizeOf<SHFOLDERCUSTOMSETTINGS>(),
            dwMask = FCSM_ICONFILE,
            pszIconFile = icon,
            iIconIndex = index,
        };
        int hr = SHGetSetFolderCustomSettings(ref s, folder, FCS_FORCEWRITE);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // A background process may not normally take focus; borrowing the
    // foreground thread's input state lets the picker receive keystrokes.
    public static void ForceForeground(IntPtr hwnd)
    {
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint me = GetCurrentThreadId();
        if (fgThread != me) AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
        if (fgThread != me) AttachThreadInput(me, fgThread, false);
    }

    public static void RoundCorners(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }
}
