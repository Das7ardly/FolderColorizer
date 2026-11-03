using System.Diagnostics;
using Microsoft.Win32;

namespace FolderColorizer;

// Everything is per-user (HKCU + %LOCALAPPDATA%), so no admin rights are needed.
static class Setup
{
    const string MenuKey = @"Software\Classes\Directory\shell\FolderColorizer";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "FolderColorizer";
    // Its presence makes Windows 11 show the full classic right-click menu.
    const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    public const string TrayMutexName = "FolderColorizer.Tray.v1";
    public const string QuitEventName = "FolderColorizer.Quit.v1";

    static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FolderColorizer");
    public static string InstalledExe => Path.Combine(InstallDir, "FolderColorizer.exe");

    public static bool IsMenuOn => Exists(MenuKey);
    public static bool IsShortcutOn => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunName) != null;
    public static bool IsClassicMenuOn => Exists(ClassicMenuKey);

    static bool Exists(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k != null;
    }

    // Copies this exe to a fixed per-user location so the registry entries keep
    // working even if the downloaded copy is moved or deleted.
    static string EnsureInstalled()
    {
        var me = Environment.ProcessPath!;
        if (string.Equals(Path.GetFullPath(me), InstalledExe, StringComparison.OrdinalIgnoreCase))
            return InstalledExe;
        if (File.Exists(InstalledExe) && File.ReadAllBytes(InstalledExe).AsSpan().SequenceEqual(File.ReadAllBytes(me)))
            return InstalledExe;

        Directory.CreateDirectory(InstallDir);
        bool trayWasRunning = StopTray();
        File.Copy(me, InstalledExe, overwrite: true);
        if (trayWasRunning)
            StartTray();
        return InstalledExe;
    }

    public static void SetMenu(bool on)
    {
        Registry.CurrentUser.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
        if (!on) return;

        var exe = EnsureInstalled();
        var appIcon = IconMaker.EnsureAppIcon();
        using var root = Registry.CurrentUser.CreateSubKey(MenuKey);
        root.SetValue("MUIVerb", "Folder color");
        root.SetValue("Icon", appIcon);
        root.SetValue("SubCommands", "");

        // Sub-entries are listed in key-name order, hence the numeric prefixes.
        int i = 0;
        foreach (var c in Palette.Colors)
            AddCommand(root, $"{i++:D2}", c.Name, IconMaker.EnsureIcon(c.Color), $"\"{exe}\" --set {Palette.Hex(c.Color)} \"%1\"", false);
        AddCommand(root, $"{i++:D2}", "Custom color…", appIcon, $"\"{exe}\" --custom \"%1\"", true);
        AddCommand(root, $"{i:D2}", "Restore default", @"%SystemRoot%\System32\imageres.dll,-3", $"\"{exe}\" --reset \"%1\"", false);
    }

    static void AddCommand(RegistryKey root, string id, string label, string icon, string command, bool separatorBefore)
    {
        using var k = root.CreateSubKey($@"shell\{id}");
        k.SetValue("MUIVerb", label);
        k.SetValue("Icon", icon, icon.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        if (separatorBefore)
            k.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord); // ECF_SEPARATORBEFORE
        using var cmd = k.CreateSubKey("command");
        cmd.SetValue("", command);
    }

    public static void SetShortcut(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            var exe = EnsureInstalled();
            run.SetValue(RunName, $"\"{exe}\" --tray");
            StartTray();
        }
        else
        {
            run.DeleteValue(RunName, throwOnMissingValue: false);
            StopTray();
        }
    }

    public static void SetClassicMenu(bool on)
    {
        if (on)
        {
            using var k = Registry.CurrentUser.CreateSubKey(ClassicMenuKey + @"\InprocServer32");
            k.SetValue("", "");
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClassicMenuKey, throwOnMissingSubKey: false);
        }
    }

    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        Thread.Sleep(500);
        // Windows usually restarts the shell by itself; only start it if it didn't.
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    public static void UninstallAll()
    {
        SetMenu(false);
        SetShortcut(false);
        SetClassicMenu(false);
    }

    static void StartTray()
    {
        if (!IsTrayRunning())
            Process.Start(InstalledExe, "--tray");
    }

    static bool IsTrayRunning()
    {
        if (!Mutex.TryOpenExisting(TrayMutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    // Returns whether a tray instance was running.
    static bool StopTray()
    {
        if (!EventWaitHandle.TryOpenExisting(QuitEventName, out var quit)) return false;
        using (quit) quit.Set();
        for (int i = 0; i < 50 && IsTrayRunning(); i++)
            Thread.Sleep(100);
        // The mutex can outlive the message loop by a moment; make sure the exe is unlocked.
        Thread.Sleep(300);
        return true;
    }
}
