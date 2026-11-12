using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace FolderColorizer;

// The small popup palette. Click a folder, or press 1-9 / 0, C for custom, R to reset, Esc to close.
sealed class PickerForm : Form
{
    sealed record Cell(Rectangle Bounds, string Label, Bitmap? Image, string? Key, Action Run);

    const int Cols = 6;
    readonly string[] folders;
    readonly List<Cell> cells = [];
    readonly bool dark;
    readonly float s;
    readonly string title;
    int hover = -1;
    bool busy;

    public PickerForm(string[] folders)
    {
        this.folders = folders;
        title = folders.Length != 1 ? $"{folders.Length} folders"
            : Path.GetFileName(folders[0].TrimEnd('\\')) is { Length: > 0 } name ? name : folders[0]; // a drive root has no name
        dark = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            ?.GetValue("AppsUseLightTheme") is int light && light == 0;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = dark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xFB, 0xFB, 0xFB);
        ForeColor = dark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
        s = DeviceDpi / 96f;
        Font = new Font("Segoe UI", 9f);

        int pad = S(12), cell = S(50), header = S(30), footer = S(34);
        int y = pad + header;
        var material = AppSettings.Load().Material; // swatches show the look new colors get
        for (int i = 0; i < Palette.Colors.Length; i++)
        {
            var c = Palette.Colors[i];
            var r = new Rectangle(pad + i % Cols * cell, y + i / Cols * cell, cell, cell);
            string? key = i < 10 ? ((i + 1) % 10).ToString() : null;
            cells.Add(new Cell(r, c.Name, IconMaker.DrawFolder(S(32), c.Color, material: material), key, () => Apply(c.Color)));
        }
        int rows = (Palette.Colors.Length + Cols - 1) / Cols;
        int fy = y + rows * cell + S(6);
        int half = Cols * cell / 2;
        cells.Add(new Cell(new Rectangle(pad, fy, half - S(2), footer), "Custom…", null, "C", PickCustom));
        cells.Add(new Cell(new Rectangle(pad + half + S(2), fy, half - S(2), footer), "Restore default", null, "R", Reset));

        ClientSize = new Size(pad * 2 + Cols * cell, fy + footer + pad);
    }

    int S(int v) => (int)Math.Round(v * s);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle);
    }

    public void ShowNear(Point p)
    {
        var area = Screen.FromPoint(p).WorkingArea;
        Location = new Point(
            Math.Clamp(p.X - Width / 2, area.Left, area.Right - Width),
            Math.Clamp(p.Y + S(12), area.Top, area.Bottom - Height));
        Show();
        Native.ForceForeground(Handle);
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!busy) Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); return; }
        string? key = e.KeyCode switch
        {
            >= Keys.D0 and <= Keys.D9 => ((int)(e.KeyCode - Keys.D0)).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => ((int)(e.KeyCode - Keys.NumPad0)).ToString(),
            Keys.C => "C",
            Keys.R => "R",
            _ => null,
        };
        if (key != null)
            cells.FirstOrDefault(c => c.Key == key)?.Run();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = cells.FindIndex(c => c.Bounds.Contains(e.Location));
        if (h != hover) { hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            cells.FirstOrDefault(c => c.Bounds.Contains(e.Location))?.Run();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var dim = dark ? Color.FromArgb(0xA0, 0xA0, 0xA0) : Color.FromArgb(0x70, 0x70, 0x70);
        var hoverFill = dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xEC, 0xEC, 0xEC);
        var buttonFill = dark ? Color.FromArgb(0x35, 0x35, 0x35) : Color.FromArgb(0xF1, 0xF1, 0xF1);

        using (var bold = new Font(Font, FontStyle.Bold))
            TextRenderer.DrawText(g, title, bold, new Rectangle(S(14), S(10), ClientSize.Width - S(28), S(20)), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var small = new Font("Segoe UI", 7f);
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            var r = Rectangle.Inflate(c.Bounds, -S(2), -S(2));
            if (c.Image != null)
            {
                if (i == hover) Fill(g, hoverFill, r, S(6));
                g.DrawImage(c.Image, r.X + (r.Width - c.Image.Width) / 2, r.Y + (r.Height - c.Image.Height) / 2 - S(3));
                if (c.Key != null)
                    TextRenderer.DrawText(g, c.Key, small, new Rectangle(r.X, r.Bottom - S(14), r.Width, S(13)), dim,
                        TextFormatFlags.HorizontalCenter);
            }
            else
            {
                Fill(g, i == hover ? hoverFill : buttonFill, r, S(6));
                TextRenderer.DrawText(g, c.Label, Font, r, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    static void Fill(Graphics g, Color color, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var b = new SolidBrush(color);
        g.FillPath(b, path);
    }

    void Apply(Color color) => ForEachFolder(f => FolderIcon.Apply(f, color));

    void Reset() => ForEachFolder(FolderIcon.Reset);

    void PickCustom()
    {
        busy = true;
        Hide();
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog() == DialogResult.OK)
            Apply(dlg.Color);
        else
            Close();
    }

    // The work runs on its own thread: the tray's UI thread also runs the keyboard hook,
    // and Windows drops a hook whose thread stops responding.
    void ForEachFolder(Action<string> action)
    {
        busy = true;
        Hide();
        var targets = folders;
        var errors = new List<string>();
        StaWorker.Run(() =>
        {
            foreach (var f in targets)
            {
                try { action(f); }
                catch (Exception ex) { errors.Add($"{f}: {ex.Message}"); }
            }
        }, _ =>
        {
            if (errors.Count > 0)
                MessageBox.Show(string.Join("\n", errors), "Folder Colorizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        });
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var c in cells) c.Image?.Dispose();
            Font.Dispose();
        }
        base.Dispose(disposing);
    }
}
