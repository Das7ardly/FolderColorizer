namespace FolderColorizer;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Length == 0)
            {
                Application.Run(new MainForm());
                return 0;
            }
            switch (args[0].ToLowerInvariant())
            {
                case "--set":
                    FolderIcon.Apply(args[2], Palette.FromHex(args[1]));
                    break;
                case "--reset":
                    FolderIcon.Reset(args[1]);
                    break;
                case "--custom":
                    using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true })
                        if (dlg.ShowDialog() == DialogResult.OK)
                            FolderIcon.Apply(args[1], dlg.Color);
                    break;
                case "--tray":
                    TrayApp.Run();
                    break;
                case "--install":
                    Setup.SetMenu(true);
                    Setup.SetShortcut(true);
                    break;
                case "--uninstall":
                    Setup.UninstallAll();
                    break;
                case "--appicon":
                    IconMaker.WriteAppIcon(args[1]);
                    break;
                case "--bulk":
                    // --bulk <folder> <hex> [--deep]: what "Color many folders" does, for scripts and tests.
                {
                    if (BulkScan.Refusal(args[1], args.Contains("--deep")) is { } refusal)
                        throw new InvalidOperationException(refusal);
                    var targets = BulkScan.Run(args[1], args.Contains("--deep"), recolor: false).Targets;
                    var failed = new List<string>();
                    foreach (var f in targets)
                    {
                        try { FolderIcon.Apply(f, Palette.FromHex(args[2])); }
                        catch (Exception ex) { failed.Add($"{f}: {ex.Message}"); } // one bad folder doesn't stop the rest
                    }
                    if (failed.Count > 0)
                        throw new IOException($"Colored {targets.Count - failed.Count} of {targets.Count} folders. These could not be changed:\n" + string.Join("\n", failed.Take(10)));
                    break;
                }
                case "--screenshots":
                    try { MainForm.SaveScreenshots(args[1], args.Length > 2 ? args[2] : null); }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "error.txt"), ex.ToString()); return 1; }
                    break;
                case "--sheet":
                    StyleSheet.Write(args[1], args[2], Palette.FromHex(args.Length > 3 ? args[3] : "1fb5a8"));
                    break;
            }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Folder Colorizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
