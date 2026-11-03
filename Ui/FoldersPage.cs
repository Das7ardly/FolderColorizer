namespace FolderColorizer.Ui;

// Every folder this app has colored, with quick actions.
sealed class FoldersPage : Page
{
    readonly FlowLayoutPanel list = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Theme.Bg,
    };
    readonly Card emptyCard = new();
    readonly Label emptyTitle = UiText.Make("No colored folders yet", Theme.Heading, Theme.Text);
    readonly Label emptyText = UiText.Make("Folders you color show up here, so you can change or restore them later.", Theme.Body, Theme.TextMuted);
    readonly FlatButton emptyButton = new("Color a folder", ButtonKind.Primary, IconKind.Palette);
    readonly Action<string> openInColorPage;

    public FoldersPage(Action<string> openInColorPage, Action goColor) : base("Colored folders", "")
    {
        this.openInColorPage = openInColorPage;
        emptyCard.Controls.AddRange([emptyTitle, emptyText, emptyButton]);
        emptyCard.Paint += (_, e) =>
        {
            float side = 40 * S;
            Icons.Draw(e.Graphics, IconKind.Folder, new RectangleF((emptyCard.Width - side) / 2, emptyTitle.Top - side - 14 * S, side, side), Theme.TextMuted, 1.6f);
        };
        emptyButton.Click += (_, _) => goColor();
        Controls.AddRange([list, emptyCard]);
        list.HandleCreated += (_, _) => Theme.DarkScrollBars(list);
        list.Resize += (_, _) => SizeRows();
    }

    public override void Activate() => Reload();

    void Reload()
    {
        list.SuspendLayout();
        foreach (Control c in list.Controls) c.Dispose();
        list.Controls.Clear();
        var folders = FolderList.All()
            .Where(f => Directory.Exists(f) && FolderIcon.ColorOf(f) != null)
            .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var f in folders)
        {
            var row = new FolderRow(f);
            row.Change += () => openInColorPage(f);
            row.Restore += () => RestoreFolder(row, f);
            list.Controls.Add(row);
        }
        list.ResumeLayout();
        SubtitleLabel.Text = folders.Count switch
        {
            0 => "Nothing here yet.",
            1 => "1 folder has a color.",
            _ => $"{folders.Count} folders have a color.",
        };
        list.Visible = folders.Count > 0;
        emptyCard.Visible = folders.Count == 0;
        SizeRows();
        PerformLayout();
    }

    void RestoreFolder(FolderRow row, string folder)
    {
        row.SetBusy(true);
        RunInBackground(() => FolderIcon.Reset(folder), error =>
        {
            if (error != null)
            {
                row.SetBusy(false);
                MessageBox.Show(this, error.Message, "Folder Colorizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else Reload();
        });
    }

    void SizeRows()
    {
        int w = list.ClientSize.Width - (int)(4 * S);
        foreach (Control c in list.Controls)
            c.Size = new Size(Math.Max(200, w), (int)(68 * S));
    }

    protected override void Arrange()
    {
        list.Bounds = new Rectangle(Pad - (int)(4 * S), Top0, Width - Pad * 2 + (int)(8 * S), Height - Top0 - (int)(16 * S));
        emptyCard.Bounds = new Rectangle(Pad, Top0, Width - Pad * 2, (int)(300 * S));
        int cx = emptyCard.Width / 2;
        emptyTitle.Location = new Point(cx - emptyTitle.Width / 2, emptyCard.Height / 2 - (int)(20 * S));
        emptyText.Location = new Point(cx - emptyText.Width / 2, emptyTitle.Bottom + (int)(6 * S));
        emptyButton.Size = emptyButton.GetPreferredSize(Size.Empty);
        emptyButton.Location = new Point(cx - emptyButton.Width / 2, emptyText.Bottom + (int)(18 * S));
    }
}

// One folder: its current icon, name, path, color and actions.
sealed class FolderRow : Card
{
    readonly ImageBox icon = new();
    readonly Label name, path;
    readonly FlatButton change = new("Change", ButtonKind.Secondary, IconKind.Palette);
    readonly FlatButton open = new("Open", ButtonKind.Ghost, IconKind.Open);
    readonly FlatButton restore = new("Restore", ButtonKind.Ghost, IconKind.Reset);
    readonly Color color;
    public event Action? Change, Restore;

    public FolderRow(string folder)
    {
        Margin = new Padding(2, 0, 2, (int)(8 * DeviceDpi / 96f));
        color = FolderIcon.ColorOf(folder) ?? Color.Gray;
        name = UiText.Make(Path.GetFileName(folder), Theme.BodyStrong, Theme.Text);
        path = UiText.Make(Path.GetDirectoryName(folder) ?? folder, Theme.Small, Theme.TextMuted);
        try
        {
            if (FolderIcon.IconOf(folder) is { } file && File.Exists(file))
                using (var ico = new Icon(file, 48, 48)) icon.Image = ico.ToBitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException) { }
        Controls.AddRange([icon, name, path, change, open, restore]);
        change.Click += (_, _) => Change?.Invoke();
        restore.Click += (_, _) => Restore?.Invoke();
        open.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        Paint += (_, e) =>
        {
            // Small colour dot after the name.
            float s = DeviceDpi / 96f, d = 10 * s;
            Theme.Smooth(e.Graphics);
            using var b = new SolidBrush(color);
            e.Graphics.FillEllipse(b, name.Right + 8 * s, name.Top + (name.Height - d) / 2, d, d);
        };
    }

    public void SetBusy(bool busy)
    {
        foreach (var b in new[] { change, open, restore }) b.Enabled = !busy;
        UseWaitCursor = busy;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        float s = DeviceDpi / 96f;
        icon.Bounds = new Rectangle((int)(14 * s), (Height - (int)(44 * s)) / 2, (int)(44 * s), (int)(44 * s));
        foreach (var b in new[] { change, open, restore }) b.Size = b.GetPreferredSize(Size.Empty);
        restore.Location = new Point(Width - restore.Width - (int)(14 * s), (Height - restore.Height) / 2);
        open.Location = new Point(restore.Left - open.Width - (int)(4 * s), restore.Top);
        change.Location = new Point(open.Left - change.Width - (int)(8 * s), restore.Top);
        int textX = icon.Right + (int)(14 * s), textW = Math.Max(60, change.Left - textX - (int)(30 * s));
        name.MaximumSize = new Size(textW, 0);
        path.MaximumSize = new Size(textW + (int)(20 * s), 0);
        name.Location = new Point(textX, Height / 2 - name.Height - (int)(1 * s));
        path.Location = new Point(textX, Height / 2 + (int)(2 * s));
    }
}
