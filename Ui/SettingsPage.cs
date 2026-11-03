using System.Drawing.Drawing2D;

namespace FolderColorizer.Ui;

sealed class SettingsPage : Page
{
    readonly Card integration = new();
    readonly Label integrationTitle = UiText.Make("Windows integration", Theme.Heading, Theme.Text);
    readonly List<(Label title, Label text, Toggle toggle)> rows = [];
    readonly FlatButton restartExplorer = new("Restart File Explorer", ButtonKind.Ghost, IconKind.Reset);

    readonly Card previewsCard = new();
    readonly Label previewsTitle = UiText.Make("File previews", Theme.Heading, Theme.Text);
    readonly Label previewsText = UiText.Make("How a colored folder shows the newest pictures and files inside it.", Theme.Small, Theme.TextMuted);
    readonly List<(ChoiceTile tile, PreviewStyle style)> tiles = [];
    readonly FlatButton redrawButton = new("Update colored folders", ButtonKind.Secondary, IconKind.Reset);
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

        previewsCard.Controls.AddRange([previewsTitle, previewsText, redrawButton, status]);
        using var demo = DemoPreviews();
        foreach (var (label, style) in new[] { ("Window", PreviewStyle.Window), ("Card", PreviewStyle.Card), ("Fan", PreviewStyle.Fan), ("Plain", PreviewStyle.Plain) })
        {
            var tile = new ChoiceTile(label) { Picture = IconMaker.DrawFolder(128, Palette.Colors[4].Color, demo.Items, style) };
            tile.Click += (_, _) => ChooseStyle(style);
            tiles.Add((tile, style));
            previewsCard.Controls.Add(tile);
        }
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

    public override void Activate()
    {
        var style = AppSettings.Load().PreviewStyle;
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
        status.ForeColor = Theme.TextMuted;
        status.Text = "New colors use this style. Update colored folders to redraw the ones you have.";
    }

    void RedrawAll()
    {
        var folders = FolderList.All().Where(f => Directory.Exists(f) && FolderIcon.ColorOf(f) != null).ToList();
        if (folders.Count == 0) { status.Text = "No colored folders yet."; return; }
        redrawButton.Enabled = false;
        status.ForeColor = Theme.TextMuted;
        int done = 0, failed = 0;
        RunInBackground(() =>
        {
            foreach (var f in folders)
            {
                try { FolderIcon.Redraw(f); } catch (Exception) { failed++; }
                done++;
                BeginInvoke(() => status.Text = $"Updating {done} of {folders.Count}…");
            }
        }, error =>
        {
            redrawButton.Enabled = true;
            Report(error, failed == 0 ? $"Updated {folders.Count} folders." : $"Updated {folders.Count - failed} folders; {failed} could not be changed.", status);
        });
    }

    // A small landscape, so the style tiles show what file previews look like.
    sealed class Demo : IDisposable
    {
        public readonly List<Preview> Items = [];
        public void Dispose() => Items.ForEach(p => p.Dispose());
    }

    static Demo DemoPreviews()
    {
        var demo = new Demo();
        foreach (var (sky, hill) in new[] { ("38BDF8", "16A34A"), ("F59E0B", "7C3AED"), ("F472B6", "0EA5E9") })
        {
            var bmp = new Bitmap(400, 300);
            using var g = Graphics.FromImage(bmp);
            Theme.Smooth(g);
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, 400, 300), Theme.Hex(sky), Color.White, 90f)) g.FillRectangle(b, 0, 0, 400, 300);
            g.FillEllipse(Brushes.LemonChiffon, 270, 50, 70, 70);
            using (var b = new SolidBrush(Theme.Hex(hill))) g.FillEllipse(b, -80, 170, 380, 260);
            using (var b = new SolidBrush(ControlPaint.Dark(Theme.Hex(hill), 0.1f))) g.FillEllipse(b, 160, 190, 360, 240);
            demo.Items.Add(new Preview(bmp, true));
        }
        return demo;
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
        int tw = (int)(118 * S), th = (int)(132 * S), tx = x;
        foreach (var (tile, _) in tiles)
        {
            tile.Bounds = new Rectangle(tx, previewsText.Bottom + (int)(14 * S), tw, th);
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
