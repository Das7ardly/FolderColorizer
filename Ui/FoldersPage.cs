namespace FolderColorizer.Ui;

// Every folder this app has colored, searchable, with per-folder and "all shown" actions.
sealed class FoldersPage : Page
{
    readonly SearchBox search = new();
    readonly FlatButton restoreShown = new("Restore all shown", ButtonKind.Danger, IconKind.Reset);
    readonly FolderListView list = new();
    readonly Card emptyCard = new();
    readonly Label emptyTitle = UiText.Make("No colored folders yet", Theme.Heading, Theme.Text);
    readonly Label emptyText = UiText.Make("Folders you color show up here, so you can change or restore them later.", Theme.Body, Theme.TextMuted);
    readonly FlatButton emptyButton = new("Color a folder", ButtonKind.Primary, IconKind.Palette);
    readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 200 };

    List<FolderListView.Entry> all = [];
    bool busy;
    int loadId;

    public FoldersPage(Action<string> openInColorPage, Action goColor) : base("Colored folders", "")
    {
        emptyCard.Controls.AddRange([emptyTitle, emptyText, emptyButton]);
        emptyCard.Paint += (_, e) =>
        {
            float side = 40 * S;
            Icons.Draw(e.Graphics, IconKind.Folder, new RectangleF((emptyCard.Width - side) / 2, emptyTitle.Top - side - 14 * S, side, side), Theme.TextMuted, 1.6f);
        };
        emptyButton.Click += (_, _) => goColor();
        list.Change += openInColorPage;
        list.Restore += f => RestoreFolders([f], ask: false);
        restoreShown.Click += (_, _) => RestoreFolders(list.Entries.Select(e => e.Folder).ToList(), ask: true);
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); ApplyFilter(); };
        Controls.AddRange([search, restoreShown, list, emptyCard]);
    }

    public override void Activate() => Reload();

    // Reading thousands of desktop.ini files takes a moment, so it runs in the background.
    void Reload()
    {
        int id = ++loadId;
        SubtitleLabel.Text = "Looking up colored folders…";
        List<FolderListView.Entry>? found = null;
        RunInBackground(() =>
        {
            found = [];
            foreach (var f in FolderList.All())
                if (Directory.Exists(f) && FolderIcon.ColorOf(f) is { } c)
                    found.Add(new FolderListView.Entry(f, Path.GetFileName(f) is { Length: > 0 } n ? n : f, Path.GetDirectoryName(f) ?? "", c));
            found.Sort((a, b) => string.Compare(a.Folder, b.Folder, StringComparison.CurrentCultureIgnoreCase));
        }, error =>
        {
            if (id != loadId) return;
            all = found ?? [];
            list.ForgetIcons();
            ApplyFilter();
        });
    }

    void ApplyFilter()
    {
        var term = search.Text.Trim();
        var shown = term.Length == 0 ? all
            : all.Where(e => e.Folder.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
        list.Entries = shown;
        SubtitleLabel.Text = all.Count switch
        {
            0 => "Nothing here yet.",
            1 => "1 folder has a color.",
            _ when shown.Count == all.Count => $"{all.Count:N0} folders have a color.",
            _ => $"{all.Count:N0} folders have a color · {shown.Count:N0} match “{term}”.",
        };
        restoreShown.Text = shown.Count == all.Count ? "Restore all" : $"Restore {shown.Count:N0} shown";
        restoreShown.Enabled = shown.Count > 0 && !busy;
        emptyCard.Visible = all.Count == 0;
        list.Visible = search.Visible = restoreShown.Visible = all.Count > 0;
        PerformLayout();
    }

    void RestoreFolders(List<string> folders, bool ask)
    {
        if (folders.Count == 0 || busy) return;
        if (ask && MessageBox.Show(this,
                $"Restore {folders.Count:N0} folders to their usual Windows icon? Their colors are removed; nothing inside the folders changes.",
                "Folder Colorizer", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        busy = true;
        restoreShown.Enabled = false;
        UseWaitCursor = true;
        int failed = 0;
        RunInBackground(() =>
        {
            int i = 0;
            foreach (var f in folders)
            {
                try { FolderIcon.Reset(f); } catch (Exception) { failed++; }
                int n = ++i;
                if (folders.Count > 1 && n % 10 == 0) PostToUi(() => SubtitleLabel.Text = $"Restoring {n:N0} of {folders.Count:N0}…");
            }
        }, error =>
        {
            busy = false;
            UseWaitCursor = false;
            if (error != null || failed > 0)
                MessageBox.Show(this, error?.Message ?? $"{failed:N0} folders could not be restored (read-only or in use).",
                    "Folder Colorizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reload();
        });
    }

    protected override void Arrange()
    {
        int w = Width - Pad * 2;
        restoreShown.Size = restoreShown.GetPreferredSize(Size.Empty);
        restoreShown.Location = new Point(Width - Pad - restoreShown.Width, Top0 - (int)(2 * S));
        search.Bounds = new Rectangle(Pad, Top0, Math.Min((int)(360 * S), restoreShown.Left - Pad - (int)(12 * S)), (int)(34 * S));
        int listTop = search.Bottom + (int)(14 * S);
        list.Bounds = new Rectangle(Pad, listTop, w, Height - listTop - (int)(16 * S));
        emptyCard.Bounds = new Rectangle(Pad, Top0, w, (int)(300 * S));
        int cx = emptyCard.Width / 2;
        emptyTitle.Location = new Point(cx - emptyTitle.Width / 2, emptyCard.Height / 2 - (int)(20 * S));
        emptyText.Location = new Point(cx - emptyText.Width / 2, emptyTitle.Bottom + (int)(6 * S));
        emptyButton.Size = emptyButton.GetPreferredSize(Size.Empty);
        emptyButton.Location = new Point(cx - emptyButton.Width / 2, emptyText.Bottom + (int)(18 * S));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) searchDelay.Dispose();
        base.Dispose(disposing);
    }
}

// A rounded search field: a borderless TextBox on a drawn background with a search hint.
sealed class SearchBox : Panel
{
    readonly TextBox box = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Muted,
        ForeColor = Theme.Text,
        Font = Theme.Body,
        PlaceholderText = "Search folders",
    };

    public SearchBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.Add(box);
        box.TextChanged += (_, e) => OnTextChanged(e);
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam;
        Click += (_, _) => box.Focus();
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => box.Text; set => box.Text = value ?? ""; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        float s = DeviceDpi / 96f;
        int left = (int)(36 * s);
        box.Bounds = new Rectangle(left, (Height - box.PreferredHeight) / 2, Width - left - (int)(12 * s), box.PreferredHeight);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Backdrop.Paint(this, e.Graphics, blur: false);
        Theme.Smooth(e.Graphics);
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(e.Graphics, Theme.Muted, r, 10 * s);
        Theme.StrokeRound(e.Graphics, box.Focused ? Theme.Accent : Color.FromArgb(40, 255, 255, 255), r, 10 * s, box.Focused ? 1.5f * s : 1f);
        // Magnifying glass.
        using var pen = new Pen(Theme.TextMuted, 1.8f * s);
        float cx = 16 * s, cy = Height / 2f, rad = 6 * s;
        e.Graphics.DrawEllipse(pen, cx - rad, cy - rad - 1 * s, rad * 2, rad * 2);
        e.Graphics.DrawLine(pen, cx + rad * 0.7f, cy + rad * 0.7f - 1 * s, cx + rad * 1.45f, cy + rad * 1.45f - 1 * s);
    }
}
