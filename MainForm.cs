using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FolderColorizer.Ui;

namespace FolderColorizer;

// The settings window: a sidebar with the pages, and the current page on the right.
sealed class MainForm : Form
{
    readonly GlassSidebar sidebar = new() { Dock = DockStyle.Left };
    readonly BackdropPanel content = new() { Dock = DockStyle.Fill };

    // The sidebar is a glass panel floating over the backdrop, inset from the window edges.
    sealed class GlassSidebar : BackdropPanel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            float s = DeviceDpi / 96f;
            Backdrop.PaintGlass(this, e.Graphics, new RectangleF(10 * s, 10 * s, Width - 14 * s, Height - 20 * s), 16 * s,
                Color.FromArgb(150, Theme.Sidebar), Color.FromArgb(34, 255, 255, 255));
        }
    }
    readonly List<(NavButton button, Page page)> pages = [];
    readonly ColorPage colorPage = new();

    public MainForm()
    {
        Text = "Folder Colorizer";
        Icon = new Icon(IconMaker.EnsureAppIcon());
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        float s = DeviceDpi / 96f;
        ClientSize = new Size((int)(1040 * s), (int)(680 * s));
        MinimumSize = new Size((int)(980 * s), (int)(640 * s));
        sidebar.Width = (int)(232 * s);
        sidebar.Padding = new Padding((int)(20 * s), (int)(88 * s), (int)(14 * s), (int)(14 * s));
        sidebar.Paint += PaintBrand;

        Add("Color a folder", IconKind.Palette, colorPage);
        Add("Color many folders", IconKind.Stack, new BulkPage());
        Add("Colored folders", IconKind.List, new FoldersPage(f => { ShowPage(0); colorPage.LoadFolder(f); }, () => ShowPage(0)));
        Add("Settings", IconKind.Gear, new SettingsPage());
        Add("About", IconKind.Info, new AboutPage());

        Controls.Add(content);
        Controls.Add(sidebar);
        ShowPage(0);
    }

    void Add(string title, IconKind icon, Page page)
    {
        var button = new NavButton(title, icon) { Dock = DockStyle.Top, Height = (int)(44 * DeviceDpi / 96f) };
        int index = pages.Count;
        button.Click += (_, _) => ShowPage(index);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        pages.Add((button, page));
        content.Controls.Add(page);
        // Dock=Top stacks the last-added control on top, so insert in reverse.
        sidebar.Controls.Add(button);
        button.BringToFront();
    }

    public void ShowPage(int index)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].button.Selected = i == index;
            pages[i].page.Visible = i == index;
        }
        pages[index].page.Activate();
    }

    void PaintBrand(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        float s = DeviceDpi / 96f;
        using (var logo = IconMaker.DrawFolder(64, Theme.Hex("2F7DF6"), material: FolderMaterial.Liquid))
            g.DrawImage(logo, 26 * s, 28 * s, 36 * s, 36 * s);
        TextRenderer.DrawText(g, "Folder", Theme.BodyStrong, new Point((int)(70 * s), (int)(27 * s)), Theme.Text);
        TextRenderer.DrawText(g, "Colorizer", Theme.Body, new Point((int)(70 * s), (int)(45 * s)), Theme.TextMuted);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Invalidate(true); // the backdrop is stretched to the new size
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    // Hidden switch for checking the UI: --screenshots <dir> [folder] saves every page as PNG.
    public static void SaveScreenshots(string dir, string? folder)
    {
        Directory.CreateDirectory(dir);
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), TopMost = true };
        form.Show();
        void Shot(string name)
        {
            for (int i = 0; i < 40; i++) { Application.DoEvents(); Thread.Sleep(30); } // let background loads finish
            using var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                PrintWindow(form.Handle, hdc, 2); // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
            }
            bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        }
        string[] names = ["1-color", "2-bulk", "3-folders", "4-settings", "5-about"];
        for (int i = 0; i < names.Length; i++) { form.ShowPage(i); Shot(names[i]); }
        if (folder != null && Path.GetDirectoryName(folder) is { } parent)
        {
            form.ShowPage(1);
            ((BulkPage)form.pages[1].page).SetRoot(parent);
            for (int i = 0; i < 100; i++) { Application.DoEvents(); Thread.Sleep(50); } // let the scan finish
            Shot("2-bulk-loaded");
        }
        if (folder != null)
        {
            form.ShowPage(0);
            form.colorPage.LoadFolder(folder);
            Shot("1-color-loaded");
        }
        form.Close();
    }
}
