using System.Diagnostics;

namespace FolderColorizer.Ui;

sealed class AboutPage : Page
{
    readonly Card card = new();
    readonly ImageBox logo = new();
    readonly Label name = UiText.Make("Folder Colorizer", Theme.Title, Theme.Text);
    readonly Label version = UiText.Make("", Theme.Body, Theme.TextMuted);
    readonly Label howTitle = UiText.Make("Three ways to color a folder", Theme.Heading, Theme.Text);
    readonly Label howText = UiText.Make(
        "1.  Right-click a folder, then Folder color.\n" +
        "2.  Select folders in File Explorer and press Ctrl+Shift+F.\n" +
        "3.  Use Color a folder in this window, or drop a folder on it.",
        Theme.Body, Theme.TextMuted, wrap: true);

    readonly Card storage = new();
    readonly Label storageTitle = UiText.Make("Storage", Theme.Heading, Theme.Text);
    readonly Label storageText = UiText.Make("", Theme.Body, Theme.TextMuted);
    readonly FlatButton cleanButton = new("Clean up unused icons", ButtonKind.Secondary, IconKind.Broom);
    readonly FlatButton openButton = new("Open data folder", ButtonKind.Ghost, IconKind.Open);
    readonly Label status = UiText.Make("", Theme.Small, Theme.TextMuted);

    public AboutPage() : base("About", "")
    {
        var v = typeof(Program).Assembly.GetName().Version;
        version.Text = v == null ? "" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        logo.Image = IconMaker.DrawAppIcon(128);
        card.Controls.AddRange([logo, name, version, howTitle, howText]);
        storage.Controls.AddRange([storageTitle, storageText, cleanButton, openButton, status]);
        Controls.AddRange([card, storage]);
        openButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(IconMaker.DataDir);
            Process.Start(new ProcessStartInfo(IconMaker.DataDir) { UseShellExecute = true });
        };
        cleanButton.Click += (_, _) =>
        {
            long freed = 0;
            cleanButton.Enabled = false;
            RunInBackground(() => freed = FolderIcon.CleanUpIcons(FolderList.All()), error =>
            {
                cleanButton.Enabled = true;
                RefreshStorage();
                Report(error, $"Freed {Mb(freed)}.", status);
            });
        };
    }

    public override void Activate()
    {
        status.Text = "";
        RefreshStorage();
    }

    static long IconBytes()
    {
        var dir = Path.Combine(IconMaker.DataDir, "icons");
        return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles().Sum(f => f.Length) : 0;
    }

    static string Mb(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / 1024.0 / 1024:0.0} MB";

    void RefreshStorage()
    {
        var dir = Path.Combine(IconMaker.DataDir, "icons");
        int count = Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0;
        storageText.Text = $"Folder icons use {Mb(IconBytes())} in {count} files. Unused ones are removed automatically; this cleans up right now.";
        PerformLayout();
    }

    protected override void Arrange()
    {
        int w = Width - Pad * 2, x = (int)(24 * S);
        card.Bounds = new Rectangle(Pad, Top0 - (int)(40 * S), w, (int)(250 * S));
        logo.Bounds = new Rectangle(x, (int)(24 * S), (int)(96 * S), (int)(96 * S));
        name.Location = new Point(logo.Right + (int)(20 * S), logo.Top + (int)(18 * S));
        version.Location = new Point(name.Left + (int)(2 * S), name.Bottom + (int)(2 * S));
        howTitle.Location = new Point(x, logo.Bottom + (int)(18 * S));
        howText.MaximumSize = new Size(w - x * 2, 0);
        howText.Size = howText.GetPreferredSize(new Size(w - x * 2, 0));
        howText.Location = new Point(x, howTitle.Bottom + (int)(6 * S));
        card.Height = howText.Bottom + (int)(22 * S);

        storage.Location = new Point(Pad, card.Bottom + (int)(16 * S));
        storage.Width = w;
        storageTitle.Location = new Point(x, (int)(20 * S));
        storageText.MaximumSize = new Size(w - x * 2, 0);
        storageText.Location = new Point(x, storageTitle.Bottom + (int)(4 * S));
        cleanButton.Size = cleanButton.GetPreferredSize(Size.Empty);
        openButton.Size = openButton.GetPreferredSize(Size.Empty);
        cleanButton.Location = new Point(x, storageText.Bottom + (int)(14 * S));
        openButton.Location = new Point(cleanButton.Right + (int)(8 * S), cleanButton.Top);
        status.Location = new Point(openButton.Right + (int)(12 * S), cleanButton.Top + (cleanButton.Height - status.Height) / 2);
        storage.Height = cleanButton.Bottom + (int)(20 * S);
    }
}
