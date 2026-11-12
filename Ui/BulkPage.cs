namespace FolderColorizer.Ui;

// Color every uncolored folder inside a chosen folder in one go, with a preview of what will
// change first and an Undo afterwards.
sealed class BulkPage : Page
{
    readonly Card sourceCard = new() { AllowDrop = true };
    readonly Label sourceHeading = UiText.Make("Folders inside", Theme.Heading, Theme.Text);
    readonly Label sourcePath = UiText.Make("Choose or drop a folder, for example Documents or a project folder.", Theme.Body, Theme.TextMuted);
    readonly FlatButton chooseButton = new("Choose folder…", ButtonKind.Secondary, IconKind.Folder);
    readonly Toggle deepToggle = new();
    readonly Label deepLabel = UiText.Make("Include folders inside subfolders", Theme.Body, Theme.Text);
    readonly Toggle recolorToggle = new();
    readonly Label recolorLabel = UiText.Make("Also recolor folders that already have a color", Theme.Body, Theme.Text);
    readonly Label summary = UiText.Make("", Theme.Body, Theme.TextMuted, wrap: true);

    readonly Card colorCard = new();
    readonly Label colorHeading = UiText.Make("Color and look", Theme.Heading, Theme.Text);
    readonly Label colorName = UiText.Make("", Theme.Body, Theme.TextMuted);
    readonly SwatchGrid swatches = new();
    readonly Segmented materialChoice = LookChoices.MaterialPicker();
    readonly Segmented styleChoice = LookChoices.StylePicker();
    readonly List<Preview> samples = LookChoices.SamplePictures(1);
    readonly ImageBox sample = new();
    readonly FlatButton applyButton = new("Color folders", ButtonKind.Primary, IconKind.Check);
    readonly FlatButton cancelButton = new("Stop", ButtonKind.Danger, IconKind.Stop);
    readonly FlatButton undoButton = new("Undo", ButtonKind.Ghost, IconKind.Reset);
    readonly Label status = UiText.Make("", Theme.Small, Theme.TextMuted, wrap: true);

    // What each folder of the last run looked like before, so Undo can put exactly that back:
    // a previous color and look, or (null) Windows' own folder.
    sealed record Before(string Folder, Color? Color, Look Look);

    string? root;
    BulkScan? scan;
    int scanId; // only the newest scan's result is shown
    List<Before> lastRun = [];
    volatile bool stop;
    bool busy;

    public BulkPage() : base("Color many folders", "Give every folder that has no color yet the same color and look.")
    {
        sourceCard.Controls.AddRange([sourceHeading, sourcePath, chooseButton, deepToggle, deepLabel, recolorToggle, recolorLabel, summary]);
        colorCard.Controls.AddRange([colorHeading, colorName, swatches, materialChoice, styleChoice, sample, applyButton, cancelButton, undoButton, status]);
        Controls.AddRange([sourceCard, colorCard]);
        foreach (var t in new[] { deepToggle, recolorToggle }) t.Size = t.GetPreferredSize(Size.Empty);
        cancelButton.Visible = undoButton.Visible = false;
        AutoScroll = true;
        HandleCreated += (_, _) => Theme.DarkScrollBars(this);

        chooseButton.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Color the folders inside this folder", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) SetRoot(dlg.SelectedPath);
        };
        sourceCard.DragEnter += (_, e) => e.Effect = Dropped(e) != null ? DragDropEffects.Link : DragDropEffects.None;
        sourceCard.DragDrop += (_, e) => { if (Dropped(e) is { } p) SetRoot(p); };
        deepToggle.CheckedChanged += (_, _) => Rescan();
        recolorToggle.CheckedChanged += (_, _) => Rescan();
        swatches.SelectionChanged += (_, _) => { colorName.Text = swatches.SelectedName; DrawSample(); };
        materialChoice.SelectedChanged += (_, _) => DrawSample();
        styleChoice.SelectedChanged += (_, _) => DrawSample();
        applyButton.Click += (_, _) => Run();
        cancelButton.Click += (_, _) => { stop = true; cancelButton.Enabled = false; };
        undoButton.Click += (_, _) => Undo();

        LookChoices.Show(materialChoice, styleChoice, AppSettings.Load().DefaultLook);
        swatches.Select(Palette.Colors.First(c => c.Name == "Blue").Color);
        UpdateApply();
    }

    static string? Dropped(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] p && p.Length > 0 && Directory.Exists(p[0]) ? p[0] : null;

    public void SetRoot(string path)
    {
        root = Path.GetFullPath(path);
        sourcePath.Text = root;
        sourcePath.ForeColor = Theme.Text;
        lastRun = [];
        undoButton.Visible = false;
        Rescan();
    }

    void Rescan()
    {
        if (root == null || busy) return;
        int id = ++scanId;
        scan = null;
        UpdateApply();
        if (BulkScan.Refusal(root, deepToggle.Checked) is { } refusal)
        {
            summary.ForeColor = Theme.Danger;
            summary.Text = refusal;
            PerformLayout();
            return;
        }
        summary.ForeColor = Theme.TextMuted;
        summary.Text = "Looking at the folders…";
        string r = root;
        bool deep = deepToggle.Checked, recolor = recolorToggle.Checked;
        BulkScan? result = null;
        RunInBackground(() => result = BulkScan.Run(r, deep, recolor), error =>
        {
            if (id != scanId) return; // the folder or the options changed meanwhile
            if (error != null) { Report(error, "", summary); return; }
            scan = result!;
            summary.ForeColor = Theme.TextMuted;
            var parts = new List<string>
            {
                scan.Targets.Count switch { 0 => "Nothing to color here.", 1 => "1 folder will be colored.", var n => $"{n} folders will be colored." },
            };
            if (scan.AlreadyColored > 0) parts.Add($"{scan.AlreadyColored} already have a color and stay as they are.");
            if (scan.Customized > 0) parts.Add($"{scan.Customized} with their own icon (set by Windows or another app) are left alone.");
            if (scan.HitLimit) parts.Add($"Stopped counting at {BulkScan.Limit}; choose a smaller folder for the rest.");
            if (scan.Targets.Count > 0)
                parts.Add("For example: " + string.Join(", ", scan.Targets.Take(4).Select(Path.GetFileName)) + (scan.Targets.Count > 4 ? "…" : "."));
            summary.Text = string.Join(" ", parts);
            UpdateApply();
            PerformLayout();
        });
    }

    void UpdateApply()
    {
        int n = scan?.Targets.Count ?? 0;
        applyButton.Text = n switch { 0 => "Color folders", 1 => "Color 1 folder", _ => $"Color {n} folders" };
        applyButton.Enabled = n > 0 && !busy && swatches.SelectedColor != null;
        PerformLayout();
    }

    // Shows the chosen color and look on a sample picture standing in for "the files inside".
    void DrawSample()
    {
        if (swatches.SelectedColor is not { } c) return;
        var style = LookChoices.StyleAt(styleChoice);
        sample.Image = IconMaker.DrawFolder(128, c, style == PreviewStyle.Plain ? null : samples, style, LookChoices.MaterialAt(materialChoice));
    }

    void Run()
    {
        if (scan is not { Targets.Count: > 0 } || swatches.SelectedColor is not { } color) return;
        var targets = scan.Targets.ToList();
        var look = new Look(LookChoices.StyleAt(styleChoice), LookChoices.MaterialAt(materialChoice));
        var done = new List<Before>();
        int failed = 0;
        stop = false;
        SetBusy(true);
        RunInBackground(() =>
        {
            foreach (var f in targets)
            {
                if (stop) break;
                var before = new Before(f, FolderIcon.ColorOf(f), FolderIcon.LookOf(f));
                try { FolderIcon.Apply(f, color, look); done.Add(before); }
                catch (Exception) { failed++; }
                int count = done.Count + failed;
                PostToUi(() => status.Text = $"Coloring {count} of {targets.Count}…");
            }
        }, error =>
        {
            SetBusy(false);
            lastRun = done;
            undoButton.Visible = done.Count > 0;
            var text = $"Colored {done.Count} of {targets.Count} folders." + (stop ? " Stopped." : "") +
                       (failed > 0 ? $" {failed} could not be changed (read-only or in use)." : "") +
                       " File Explorer shows the new colors in a moment.";
            Report(error, text, status);
            Rescan();
        });
    }

    // Puts every folder of the last run back the way it was: its previous color and look, or
    // Windows' own folder if it had no color.
    void Undo()
    {
        var folders = lastRun.ToList();
        if (folders.Count == 0) return;
        SetBusy(true);
        RunInBackground(() =>
        {
            int i = 0;
            foreach (var b in folders)
            {
                try
                {
                    if (b.Color is { } previous) FolderIcon.Apply(b.Folder, previous, b.Look);
                    else FolderIcon.Reset(b.Folder);
                }
                catch (Exception) { }
                int n = ++i;
                PostToUi(() => status.Text = $"Restoring {n} of {folders.Count}…");
            }
        }, error =>
        {
            SetBusy(false);
            lastRun = [];
            undoButton.Visible = false;
            Report(error, $"Restored {folders.Count} folders.", status);
            Rescan();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) samples.ForEach(p => p.Dispose());
        base.Dispose(disposing);
    }

    void SetBusy(bool on)
    {
        busy = on;
        foreach (var c in new Control[] { chooseButton, deepToggle, recolorToggle, swatches, materialChoice, styleChoice, undoButton }) c.Enabled = !on;
        cancelButton.Visible = on;
        cancelButton.Enabled = true;
        status.ForeColor = Theme.TextMuted;
        UseWaitCursor = on;
        UpdateApply();
    }

    protected override void Arrange()
    {
        int w = Width - Pad * 2, x = (int)(24 * S);
        sourceCard.Location = new Point(Pad, Top0);
        sourceCard.Width = w;
        sourceHeading.Location = new Point(x, (int)(18 * S));
        chooseButton.Size = chooseButton.GetPreferredSize(Size.Empty);
        chooseButton.Location = new Point(w - chooseButton.Width - x, (int)(14 * S));
        sourcePath.MaximumSize = new Size(chooseButton.Left - x - (int)(16 * S), 0);
        sourcePath.Location = new Point(x, sourceHeading.Bottom + (int)(4 * S));
        deepToggle.Location = new Point(x, sourcePath.Bottom + (int)(14 * S));
        deepLabel.Location = new Point(deepToggle.Right + (int)(8 * S), deepToggle.Top + (deepToggle.Height - deepLabel.Height) / 2);
        recolorToggle.Location = new Point(x, deepToggle.Bottom + (int)(6 * S));
        recolorLabel.Location = new Point(recolorToggle.Right + (int)(8 * S), recolorToggle.Top + (recolorToggle.Height - recolorLabel.Height) / 2);
        summary.MaximumSize = new Size(w - x * 2, 0);
        summary.Size = summary.GetPreferredSize(new Size(w - x * 2, 0));
        summary.Location = new Point(x, recolorToggle.Bottom + (int)(12 * S));
        sourceCard.Height = (summary.Text.Length > 0 ? summary.Bottom : recolorToggle.Bottom) + (int)(18 * S);

        colorCard.Location = new Point(Pad, sourceCard.Bottom + (int)(14 * S));
        colorCard.Width = w;
        colorHeading.Location = new Point(x, (int)(18 * S));
        colorName.Location = new Point(colorHeading.Right + (int)(8 * S), colorHeading.Top + (int)(2 * S));
        swatches.Size = swatches.GetPreferredSize(Size.Empty);
        swatches.Location = new Point(x - (int)(7 * S), colorHeading.Bottom + (int)(8 * S));
        int side = (int)(112 * S);
        sample.Bounds = new Rectangle(w - side - x, colorHeading.Bottom + (int)(4 * S), side, side);
        materialChoice.Size = materialChoice.GetPreferredSize(Size.Empty);
        styleChoice.Size = styleChoice.GetPreferredSize(Size.Empty);
        materialChoice.Location = new Point(x, swatches.Bottom + (int)(8 * S));
        styleChoice.Location = new Point(materialChoice.Right + (int)(10 * S), materialChoice.Top);
        if (styleChoice.Right > w - x) styleChoice.Location = new Point(x, materialChoice.Bottom + (int)(8 * S));
        applyButton.Size = new Size(applyButton.GetPreferredSize(Size.Empty).Width + (int)(20 * S), (int)(42 * S));
        applyButton.Location = new Point(x, styleChoice.Bottom + (int)(16 * S));
        cancelButton.Size = cancelButton.GetPreferredSize(Size.Empty);
        undoButton.Size = undoButton.GetPreferredSize(Size.Empty);
        cancelButton.Location = new Point(applyButton.Right + (int)(10 * S), applyButton.Top + (applyButton.Height - cancelButton.Height) / 2);
        undoButton.Location = cancelButton.Location;
        status.MaximumSize = new Size(w - x * 2 - (int)(260 * S), 0);
        status.Size = status.GetPreferredSize(new Size(status.MaximumSize.Width, 0));
        status.Location = new Point((cancelButton.Visible || undoButton.Visible ? cancelButton.Right : applyButton.Right) + (int)(14 * S),
            applyButton.Top + (applyButton.Height - status.Height) / 2);
        colorCard.Height = applyButton.Bottom + (int)(20 * S);
        AutoScrollMinSize = new Size(0, colorCard.Bottom + Pad);
    }
}
