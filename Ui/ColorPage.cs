namespace FolderColorizer.Ui;

// Pick a folder (browse or drop), preview the exact icon, then apply.
sealed class ColorPage : Page
{
    readonly Card dropCard = new() { Dashed = true, AllowDrop = true };
    readonly Label dropTitle = UiText.Make("Drop a folder here", Theme.Heading, Theme.Text);
    readonly Label dropOr = UiText.Make("or", Theme.Small, Theme.TextMuted);
    readonly FlatButton chooseButton = new("Choose folder…", ButtonKind.Primary, IconKind.Folder);
    readonly Label dropTip = UiText.Make("Tip: in File Explorer, select folders and press Ctrl+Shift+F.", Theme.Small, Theme.TextMuted);

    readonly Card previewCard = new() { AllowDrop = true };
    readonly ImageBox preview = new();
    readonly Label folderName = UiText.Make("", Theme.Heading, Theme.Text);
    readonly Label folderPath = UiText.Make("", Theme.Small, Theme.TextMuted);
    readonly Label currentLabel = UiText.Make("", Theme.Small, Theme.TextMuted);

    readonly Card colorCard = new();
    readonly Label colorHeading = UiText.Make("Color", Theme.Heading, Theme.Text);
    readonly Label colorName = UiText.Make("", Theme.Body, Theme.TextMuted);
    readonly SwatchGrid swatches = new();
    readonly FlatButton customButton = new("Custom color…", ButtonKind.Secondary, IconKind.Brush);
    readonly FlatButton restoreButton = new("Restore default", ButtonKind.Ghost, IconKind.Reset);
    readonly Label lookHeading = UiText.Make("Look", Theme.Heading, Theme.Text);
    readonly Segmented materialChoice = new("Classic", "Frosted", "Liquid glass");
    readonly Segmented styleChoice = new("Window", "Card", "Fan", "Plain");
    readonly Label styleHint = UiText.Make("", Theme.Small, Theme.TextMuted, wrap: true);

    // Segment order -> enum values.
    static readonly FolderMaterial[] Materials = [FolderMaterial.Classic, FolderMaterial.Glass, FolderMaterial.Liquid];
    static readonly PreviewStyle[] Styles = [PreviewStyle.Window, PreviewStyle.Card, PreviewStyle.Fan, PreviewStyle.Plain];
    Look SelectedLook => new(Styles[styleChoice.SelectedIndex], Materials[materialChoice.SelectedIndex]);
    readonly FlatButton applyButton = new("Apply color", ButtonKind.Primary, IconKind.Check);
    readonly FlatButton anotherButton = new("Choose another folder", ButtonKind.Ghost, IconKind.Folder);
    readonly Label status = UiText.Make("", Theme.Small, Theme.Accent);
    readonly ToolTip tips = new();

    string? folder;
    List<Preview> previews = [];

    public ColorPage() : base("Color a folder", "Choose a folder, see exactly how it will look, then apply.")
    {
        AllowDrop = true;
        dropCard.Controls.AddRange([dropTitle, dropOr, chooseButton, dropTip]);
        dropCard.Paint += (_, e) =>
        {
            float s = S, side = 44 * s;
            Icons.Draw(e.Graphics, IconKind.Drop, new RectangleF((dropCard.Width - side) / 2, dropTitle.Top - side - 18 * s, side, side),
                dropCard.Highlight ? Theme.Accent : Theme.TextMuted, 1.6f);
        };
        previewCard.Controls.AddRange([preview, folderName, folderPath, currentLabel]);
        colorCard.Controls.AddRange([colorHeading, colorName, swatches, customButton, restoreButton,
            lookHeading, materialChoice, styleChoice, styleHint, applyButton, anotherButton, status]);
        materialChoice.SelectedChanged += (_, _) => { status.Text = ""; RenderPreview(); };
        styleChoice.SelectedChanged += (_, _) => { status.Text = ""; UpdateHint(); RenderPreview(); };
        Controls.AddRange([dropCard, previewCard, colorCard]);

        chooseButton.Click += (_, _) => Browse();
        anotherButton.Click += (_, _) => Browse();
        swatches.SelectionChanged += (_, _) => { colorName.Text = swatches.SelectedName; status.Text = ""; RenderPreview(); };
        customButton.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = swatches.SelectedColor ?? Color.SteelBlue };
            if (dlg.ShowDialog(this) == DialogResult.OK) swatches.Select(dlg.Color);
        };
        applyButton.Click += (_, _) => Apply();
        restoreButton.Click += (_, _) => Restore();

        foreach (Control c in new Control[] { this, dropCard, previewCard })
        {
            c.DragEnter += OnDragEnter;
            c.DragLeave += (_, _) => SetDropHighlight(false);
            c.DragDrop += OnDragDrop;
        }
        ShowLoaded(false);
    }

    void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose a folder to color", UseDescriptionForTitle = true };
        if (folder != null) dlg.InitialDirectory = Path.GetDirectoryName(folder) ?? "";
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadFolder(dlg.SelectedPath);
    }

    static string? DroppedFolder(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && Directory.Exists(paths[0]) ? paths[0] : null;

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        bool ok = DroppedFolder(e) != null;
        e.Effect = ok ? DragDropEffects.Link : DragDropEffects.None;
        SetDropHighlight(ok);
    }

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (DroppedFolder(e) is { } path) LoadFolder(path);
    }

    void SetDropHighlight(bool on)
    {
        dropCard.Highlight = previewCard.Highlight = on;
        dropCard.Invalidate(true);
        previewCard.Invalidate(true);
    }

    public void LoadFolder(string path)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        previews.ForEach(p => p.Dispose());
        Cursor = Cursors.WaitCursor;
        try { previews = FolderIcon.LoadPreviews(FolderIcon.PickPreviewFiles(folder)); }
        finally { Cursor = Cursors.Default; }

        // Start from the look the folder already has (or the default from Settings).
        var look = FolderIcon.LookOf(folder);
        materialChoice.SetQuietly(Array.IndexOf(Materials, look.Material));
        styleChoice.SetQuietly(Array.IndexOf(Styles, look.Style));
        UpdateHint();
        folderName.Text = Path.GetFileName(folder) is { Length: > 0 } name ? name : folder;
        folderPath.Text = folder;
        tips.SetToolTip(folderPath, folder);
        var current = FolderIcon.ColorOf(folder);
        currentLabel.Text = current == null ? "Not colored yet" : $"Currently {NameOf(current.Value)}";
        restoreButton.Enabled = current != null;
        status.Text = "";
        ShowLoaded(true);
        PerformLayout();
        swatches.Select(current ?? Palette.Colors.First(c => c.Name == "Blue").Color);
        swatches.Focus();
    }

    static string NameOf(Color c) =>
        Palette.Colors.FirstOrDefault(p => p.Color.ToArgb() == c.ToArgb())?.Name ?? $"custom #{Palette.Hex(c).ToUpperInvariant()}";

    void UpdateHint() => styleHint.Text = Styles[styleChoice.SelectedIndex] == PreviewStyle.Plain
        ? "Color only, no file previews."
        : previews.Count == 0
            ? "This folder has no files to preview, so it gets the plain color."
            : "The newest pictures and files inside show through. The icon updates when they change.";

    // Exactly what Apply will write: same color, look and files.
    void RenderPreview()
    {
        if (folder == null || swatches.SelectedColor is not { } color) return;
        var look = SelectedLook;
        preview.Image = IconMaker.DrawFolder(256, color, look.Style == PreviewStyle.Plain ? null : previews, look.Style, look.Material);
    }

    void Apply()
    {
        if (folder == null || swatches.SelectedColor is not { } color) return;
        string target = folder, name = swatches.SelectedName;
        var look = SelectedLook;
        SetBusy(true, "Applying…");
        RunInBackground(() => FolderIcon.Apply(target, color, look), error =>
        {
            SetBusy(false, "");
            Report(error, $"Done. {Path.GetFileName(target)} is now {name.ToLowerInvariant()}.", status);
            if (error == null && target == folder) { currentLabel.Text = $"Currently {NameOf(color)}"; restoreButton.Enabled = true; }
        });
    }

    void Restore()
    {
        if (folder == null) return;
        string target = folder;
        SetBusy(true, "Restoring…");
        RunInBackground(() => FolderIcon.Reset(target), error =>
        {
            SetBusy(false, "");
            Report(error, "Restored. Windows shows its usual folder again.", status);
            if (error == null && target == folder) { currentLabel.Text = "Not colored yet"; restoreButton.Enabled = false; }
        });
    }

    void SetBusy(bool busy, string message)
    {
        foreach (var b in new Control[] { applyButton, customButton, anotherButton, swatches, materialChoice, styleChoice }) b.Enabled = !busy;
        restoreButton.Enabled = !busy && folder != null && FolderIcon.ColorOf(folder) != null;
        status.ForeColor = Theme.TextMuted;
        status.Text = message;
        UseWaitCursor = busy;
    }

    void ShowLoaded(bool loaded)
    {
        dropCard.Visible = !loaded;
        previewCard.Visible = colorCard.Visible = loaded;
        PerformLayout();
    }

    protected override void Arrange()
    {
        int w = Width - Pad * 2, top = Top0;
        int h = Math.Max((int)(380 * S), Height - top - Pad);

        dropCard.Bounds = new Rectangle(Pad, top, w, Math.Min(h, (int)(420 * S)));
        int cx = dropCard.Width / 2, cy = dropCard.Height / 2;
        dropTitle.Location = new Point(cx - dropTitle.Width / 2, cy - (int)(14 * S));
        dropOr.Location = new Point(cx - dropOr.Width / 2, dropTitle.Bottom + (int)(8 * S));
        chooseButton.Size = chooseButton.GetPreferredSize(Size.Empty);
        chooseButton.Location = new Point(cx - chooseButton.Width / 2, dropOr.Bottom + (int)(10 * S));
        dropTip.Location = new Point(cx - dropTip.Width / 2, dropCard.Height - dropTip.Height - (int)(20 * S));

        int pw = (int)(300 * S), gap = (int)(20 * S), ch = Math.Min(h, (int)(500 * S));
        previewCard.Bounds = new Rectangle(Pad, top, pw, ch);
        int inner = pw - (int)(40 * S);
        preview.Bounds = new Rectangle((int)(20 * S), (int)(20 * S), inner, inner);
        folderName.MaximumSize = folderPath.MaximumSize = new Size(inner, 0);
        folderName.Location = new Point((int)(20 * S), preview.Bottom + (int)(14 * S));
        folderPath.Location = new Point((int)(20 * S), folderName.Bottom + (int)(4 * S));
        currentLabel.Location = new Point((int)(20 * S), folderPath.Bottom + (int)(10 * S));

        colorCard.Bounds = new Rectangle(previewCard.Right + gap, top, Math.Max((int)(380 * S), w - pw - gap), ch);
        int x = (int)(24 * S);
        colorHeading.Location = new Point(x, (int)(22 * S));
        colorName.Location = new Point(colorHeading.Right + (int)(8 * S), colorHeading.Top + (int)(2 * S));
        swatches.Size = swatches.GetPreferredSize(Size.Empty);
        swatches.Location = new Point(x - (int)(7 * S), colorHeading.Bottom + (int)(12 * S));
        customButton.Size = customButton.GetPreferredSize(Size.Empty);
        restoreButton.Size = restoreButton.GetPreferredSize(Size.Empty);
        customButton.Location = new Point(x, swatches.Bottom + (int)(14 * S));
        restoreButton.Location = new Point(customButton.Right + (int)(8 * S), customButton.Top);
        lookHeading.Location = new Point(x, customButton.Bottom + (int)(22 * S));
        materialChoice.Size = materialChoice.GetPreferredSize(Size.Empty);
        styleChoice.Size = styleChoice.GetPreferredSize(Size.Empty);
        materialChoice.Location = new Point(x, lookHeading.Bottom + (int)(10 * S));
        styleChoice.Location = new Point(x, materialChoice.Bottom + (int)(8 * S));
        styleHint.MaximumSize = new Size(colorCard.Width - x * 2, 0);
        styleHint.Size = styleHint.GetPreferredSize(new Size(styleHint.MaximumSize.Width, 0));
        styleHint.Location = new Point(x, styleChoice.Bottom + (int)(8 * S));

        applyButton.Size = new Size(applyButton.GetPreferredSize(Size.Empty).Width + (int)(24 * S), (int)(44 * S));
        anotherButton.Size = anotherButton.GetPreferredSize(Size.Empty);
        applyButton.Location = new Point(x, colorCard.Height - applyButton.Height - (int)(44 * S));
        anotherButton.Location = new Point(applyButton.Right + (int)(10 * S), applyButton.Top + (applyButton.Height - anotherButton.Height) / 2);
        status.MaximumSize = new Size(colorCard.Width - x * 2, 0);
        status.Location = new Point(x, applyButton.Bottom + (int)(10 * S));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { previews.ForEach(p => p.Dispose()); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
