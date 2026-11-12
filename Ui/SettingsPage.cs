namespace FolderColorizer.Ui;

sealed class SettingsPage : Page
{
    readonly Card integration = new();
    readonly Label integrationTitle = UiText.Make("Windows integration", Theme.Heading, Theme.Text);
    readonly List<(Label title, Label text, Toggle toggle)> rows = [];
    readonly FlatButton restartExplorer = new("Restart File Explorer", ButtonKind.Ghost, IconKind.Reset);

    readonly Card previewsCard = new();
    readonly Label previewsTitle = UiText.Make("Default look", Theme.Heading, Theme.Text);
    readonly Label previewsText = UiText.Make("Used for new colors from the right-click menu and the shortcut. You can still pick a look per folder.", Theme.Small, Theme.TextMuted);
    readonly Segmented materialChoice = LookChoices.MaterialPicker();
    readonly List<Preview> samples = LookChoices.SamplePictures();
    readonly List<(ChoiceTile tile, PreviewStyle style)> tiles = [];
    readonly FlatButton redrawButton = new("Use for all colored folders", ButtonKind.Secondary, IconKind.Reset);
    readonly Label status = UiText.Make("", Theme.Small, Theme.TextMuted);

    public SettingsPage() : base("Settings", "Changes apply right away.")
    {
        integration.Controls.Add(integrationTitle);
        AddRow("Right-click menu", "Adds “Folder color” to the right-click menu of every folder.",
            Setup.IsMenuOn, Setup.SetMenu);
        AddRow("Keyboard shortcut", "Ctrl+Shift+F in File Explorer opens the color picker for the selected folders. Runs quietly in the background and starts with Windows.",
            Setup.IsShortcutOn, Setup.SetShortcut);
        AddRow("Full right-click menu", "Shows “Folder color” without clicking “Show more options”. Takes effect after File Explorer restarts.",
            Setup.IsClassicMenuOn, Setup.SetClassicMenu);
        integration.Controls.Add(restartExplorer);
        restartExplorer.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "This closes all open File Explorer windows. Continue?", "Folder Colorizer",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Setup.RestartExplorer();
        };

        previewsCard.Controls.AddRange([previewsTitle, previewsText, materialChoice, redrawButton, status]);
        for (int i = 0; i < LookChoices.Styles.Length; i++)
        {
            var style = LookChoices.Styles[i];
            var tile = new ChoiceTile(LookChoices.StyleNames[i]);
            tile.Click += (_, _) => ChooseStyle(style);
            tiles.Add((tile, style));
            previewsCard.Controls.Add(tile);
        }
        materialChoice.SelectedChanged += (_, _) =>
        {
            var settings = AppSettings.Load();
            settings.Material = LookChoices.MaterialAt(materialChoice);
            settings.Save();
            DrawTiles();
            ShowDefaultChanged();
        };
        redrawButton.Click += (_, _) => RedrawAll();
        Controls.AddRange([integration, previewsCard]);
        AutoScroll = true;
        HandleCreated += (_, _) => Theme.DarkScrollBars(this);
    }

    void AddRow(string title, string text, bool value, Action<bool> apply)
    {
        var t = UiText.Make(title, Theme.BodyStrong, Theme.Text);
        var d = UiText.Make(text, Theme.Small, Theme.TextMuted, wrap: true);
        var toggle = new Toggle();
        toggle.Size = toggle.GetPreferredSize(Size.Empty);
        toggle.SetQuietly(value);
        toggle.CheckedChanged += (_, _) =>
        {
            try
            {
                UseWaitCursor = true;
                apply(toggle.Checked);
            }
            catch (Exception ex)
            {
                toggle.SetQuietly(!toggle.Checked);
                MessageBox.Show(this, ex.Message, "Folder Colorizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        };
        rows.Add((t, d, toggle));
        integration.Controls.AddRange([t, d, toggle]);
    }

    void DrawTiles()
    {
        var material = LookChoices.MaterialAt(materialChoice);
        foreach (var (tile, style) in tiles)
        {
            tile.Picture?.Dispose();
            tile.Picture = IconMaker.DrawFolder(128, Palette.Colors[5].Color, samples, style, material);
            tile.Invalidate();
        }
    }

    void ShowDefaultChanged()
    {
        status.ForeColor = Theme.TextMuted;
        status.Text = "New colors get this look. Folders you already colored keep theirs.";
    }

    public override void Activate()
    {
        var settings = AppSettings.Load();
        var style = settings.PreviewStyle;
        materialChoice.SetQuietly(Array.IndexOf(LookChoices.Materials, settings.Material));
        DrawTiles();
        foreach (var (tile, s) in tiles) tile.Selected = s == style;
        rows[0].toggle.SetQuietly(Setup.IsMenuOn);
        rows[1].toggle.SetQuietly(Setup.IsShortcutOn);
        rows[2].toggle.SetQuietly(Setup.IsClassicMenuOn);
    }

    void ChooseStyle(PreviewStyle style)
    {
        var settings = AppSettings.Load();
        if (settings.PreviewStyle == style) return;
        settings.PreviewStyle = style;
        settings.Save();
        foreach (var (tile, s) in tiles) tile.Selected = s == style;
        ShowDefaultChanged();
    }

    // Gives every colored folder the default look, replacing looks picked per folder, so it asks first.
    void RedrawAll()
    {
        var folders = FolderList.All().Where(f => Directory.Exists(f) && FolderIcon.ColorOf(f) != null).ToList();
        if (folders.Count == 0) { status.Text = "No colored folders yet."; return; }
        if (MessageBox.Show(this, $"Give all {folders.Count} colored folders this look? Looks you picked for single folders are replaced; their colors stay.",
                "Folder Colorizer", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        redrawButton.Enabled = false;
        status.ForeColor = Theme.TextMuted;
        int done = 0, failed = 0;
        var look = AppSettings.Load().DefaultLook;
        RunInBackground(() =>
        {
            foreach (var f in folders)
            {
                try { FolderIcon.Redraw(f, look); } catch (Exception) { failed++; }
                int n = ++done;
                PostToUi(() => status.Text = $"Updating {n} of {folders.Count}…");
            }
        }, error =>
        {
            redrawButton.Enabled = true;
            Report(error, failed == 0 ? $"Updated {folders.Count} folders." : $"Updated {folders.Count - failed} folders; {failed} could not be changed.", status);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) samples.ForEach(p => p.Dispose());
        base.Dispose(disposing);
    }

    protected override void Arrange()
    {
        int w = Width - Pad * 2, x = (int)(24 * S);
        integration.Location = new Point(Pad, Top0);
        integration.Width = w;
        integrationTitle.Location = new Point(x, (int)(18 * S));
        int y = integrationTitle.Bottom + (int)(12 * S);
        foreach (var (title, text, toggle) in rows)
        {
            toggle.Location = new Point(w - toggle.Width - x, y + (int)(4 * S));
            title.Location = new Point(x, y);
            text.MaximumSize = new Size(toggle.Left - x - (int)(24 * S), 0);
            text.Size = text.GetPreferredSize(new Size(text.MaximumSize.Width, 0));
            text.Location = new Point(x, title.Bottom + (int)(2 * S));
            y = text.Bottom + (int)(13 * S);
        }
        restartExplorer.Size = restartExplorer.GetPreferredSize(Size.Empty);
        restartExplorer.Location = new Point(x - (int)(10 * S), y - (int)(12 * S));
        integration.Height = restartExplorer.Bottom + (int)(14 * S);

        previewsCard.Location = new Point(Pad, integration.Bottom + (int)(14 * S));
        previewsCard.Width = w;
        previewsTitle.Location = new Point(x, (int)(18 * S));
        previewsText.Location = new Point(x, previewsTitle.Bottom + (int)(2 * S));
        materialChoice.Size = materialChoice.GetPreferredSize(Size.Empty);
        materialChoice.Location = new Point(x, previewsText.Bottom + (int)(12 * S));
        int tw = (int)(118 * S), th = (int)(132 * S), tx = x;
        foreach (var (tile, _) in tiles)
        {
            tile.Bounds = new Rectangle(tx, materialChoice.Bottom + (int)(10 * S), tw, th);
            tx += tw + (int)(12 * S);
        }
        redrawButton.Size = redrawButton.GetPreferredSize(Size.Empty);
        redrawButton.Location = new Point(x, tiles[0].tile.Bottom + (int)(12 * S));
        status.MaximumSize = new Size(w - redrawButton.Right - x * 2, 0);
        status.Location = new Point(redrawButton.Right + (int)(12 * S), redrawButton.Top + (redrawButton.Height - status.Height) / 2);
        previewsCard.Height = redrawButton.Bottom + (int)(16 * S);
        AutoScrollMinSize = new Size(0, previewsCard.Bottom + Pad);
    }
}
