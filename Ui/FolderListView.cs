using System.Drawing.Drawing2D;

namespace FolderColorizer.Ui;

// A virtual list of colored folders: it only draws the rows on screen and loads their icons
// as they scroll into view, so thousands of folders cost no more than a screenful. (One
// control per row ran out of Windows handles at around 1,500 folders.)
sealed class FolderListView : ThemedControl
{
    public sealed record Entry(string Folder, string Name, string Parent, Color Color);

    static readonly string[] Actions = ["Change", "Open", "Restore"];
    static readonly IconKind[] ActionIcons = [IconKind.Palette, IconKind.Open, IconKind.Reset];

    IReadOnlyList<Entry> entries = [];
    readonly Dictionary<string, Bitmap?> icons = new(StringComparer.OrdinalIgnoreCase);
    int scroll, hoverRow = -1, hoverAction = -1;
    bool draggingThumb;
    int dragStartY, dragStartScroll;
    readonly ToolTip tip = new();
    string? tipFolder;

    public event Action<string>? Change, Restore;

    public FolderListView()
    {
        TabStop = true;
        Cursor = Cursors.Default;
    }

    public IReadOnlyList<Entry> Entries
    {
        get => entries;
        set { entries = value; scroll = Math.Clamp(scroll, 0, MaxScroll); hoverRow = -1; Invalidate(); }
    }

    int RowHeight => (int)(68 * S);
    int RowGap => (int)(8 * S);
    int Stride => RowHeight + RowGap;
    int ContentHeight => Math.Max(0, entries.Count * Stride - RowGap);
    int MaxScroll => Math.Max(0, ContentHeight - Height);
    int BarWidth => (int)(10 * S);
    bool HasBar => ContentHeight > Height;

    protected override void OnPaintBackground(PaintEventArgs e) => Backdrop.Paint(this, e.Graphics, blur: false);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        scroll = Math.Clamp(scroll, 0, MaxScroll);
    }

    Rectangle RowRect(int i)
    {
        int right = HasBar ? Width - BarWidth - (int)(6 * S) : Width;
        return new Rectangle(0, i * Stride - scroll, right, RowHeight);
    }

    Rectangle ActionRect(Rectangle row, int a)
    {
        int w = (int)(96 * S), h = (int)(36 * S), gap = (int)(4 * S);
        int x = row.Right - (int)(14 * S) - (Actions.Length - a) * w - (Actions.Length - 1 - a) * gap;
        return new Rectangle(x, row.Y + (row.Height - h) / 2, w, h);
    }

    Rectangle ThumbRect()
    {
        if (!HasBar) return Rectangle.Empty;
        int h = Math.Max((int)(40 * S), Height * Height / Math.Max(1, ContentHeight));
        int y = MaxScroll == 0 ? 0 : (Height - h) * scroll / MaxScroll;
        return new Rectangle(Width - BarWidth, y, BarWidth, h);
    }

    void ScrollTo(int value)
    {
        value = Math.Clamp(value, 0, MaxScroll);
        if (value == scroll) return;
        scroll = value;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(scroll - e.Delta * Stride / 120 * 2);
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: ScrollTo(scroll - Stride); break;
            case Keys.Down: ScrollTo(scroll + Stride); break;
            case Keys.PageUp: ScrollTo(scroll - Height); break;
            case Keys.PageDown: ScrollTo(scroll + Height); break;
            case Keys.Home: ScrollTo(0); break;
            case Keys.End: ScrollTo(MaxScroll); break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var thumb = ThumbRect();
        if (HasBar && e.X >= Width - BarWidth)
        {
            if (thumb.Contains(e.Location)) { draggingThumb = true; dragStartY = e.Y; dragStartScroll = scroll; }
            else ScrollTo(scroll + (e.Y < thumb.Y ? -Height : Height));
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (draggingThumb) { draggingThumb = false; base.OnMouseUp(e); return; }
        if (e.Button == MouseButtons.Left && hoverRow >= 0 && hoverAction >= 0 && hoverRow < entries.Count)
        {
            var folder = entries[hoverRow].Folder;
            switch (hoverAction)
            {
                case 0: Change?.Invoke(folder); break;
                case 1: System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); break;
                case 2: Restore?.Invoke(folder); break;
            }
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (draggingThumb)
        {
            var thumb = ThumbRect();
            int track = Math.Max(1, Height - thumb.Height);
            ScrollTo(dragStartScroll + (e.Y - dragStartY) * MaxScroll / track);
            return;
        }
        int row = (e.Y + scroll) / Stride;
        if (row < 0 || row >= entries.Count || !RowRect(row).Contains(e.Location)) row = -1;
        int action = -1;
        if (row >= 0)
            for (int a = 0; a < Actions.Length; a++)
                if (ActionRect(RowRect(row), a).Contains(e.Location)) action = a;
        if (row != hoverRow || action != hoverAction)
        {
            hoverRow = row;
            hoverAction = action;
            Cursor = action >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
            var folder = row >= 0 && action < 0 ? entries[row].Folder : null;
            if (folder != tipFolder) { tipFolder = folder; tip.SetToolTip(this, folder); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverRow = hoverAction = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int first = Math.Max(0, scroll / Stride), last = Math.Min(entries.Count - 1, (scroll + Height) / Stride);
        for (int i = first; i <= last; i++)
            PaintRow(g, i, RowRect(i));
        var thumb = ThumbRect();
        if (!thumb.IsEmpty)
            Theme.FillRound(g, Color.FromArgb(draggingThumb ? 150 : 90, Theme.TextMuted), RectangleF.Inflate(thumb, -2 * S, 0), 4 * S);
    }

    void PaintRow(Graphics g, int i, Rectangle r)
    {
        var entry = entries[i];
        var state = g.Save();
        g.TranslateTransform(r.X, r.Y);
        Backdrop.PaintGlass(this, g, new RectangleF(0.5f, 0.5f, r.Width - 1, r.Height - 1), 14 * S,
            Color.FromArgb(i == hoverRow ? 150 : 118, Theme.Card), Color.FromArgb(i == hoverRow ? 52 : 34, 255, 255, 255));
        g.Restore(state);

        int iconSide = (int)(44 * S);
        var iconRect = new Rectangle(r.X + (int)(14 * S), r.Y + (r.Height - iconSide) / 2, iconSide, iconSide);
        if (IconFor(entry) is { } bmp) g.DrawImage(bmp, iconRect);

        int textX = iconRect.Right + (int)(14 * S);
        int textRight = ActionRect(r, 0).Left - (int)(24 * S);
        var nameSize = TextRenderer.MeasureText(entry.Name, Theme.BodyStrong);
        int nameW = Math.Min(nameSize.Width, textRight - textX - (int)(18 * S));
        TextRenderer.DrawText(g, entry.Name, Theme.BodyStrong, new Rectangle(textX, r.Y + r.Height / 2 - nameSize.Height, Math.Max(0, nameW), nameSize.Height),
            Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        float d = 10 * S;
        using (var dot = new SolidBrush(entry.Color))
            g.FillEllipse(dot, textX + nameW + 8 * S, r.Y + r.Height / 2f - nameSize.Height / 2f - d / 2, d, d);
        TextRenderer.DrawText(g, entry.Parent, Theme.Small, new Rectangle(textX, r.Y + r.Height / 2 + (int)(2 * S), Math.Max(0, textRight - textX + (int)(20 * S)), Theme.Small.Height + 4),
            Theme.TextMuted, TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);

        for (int a = 0; a < Actions.Length; a++)
        {
            var ar = ActionRect(r, a);
            bool hot = i == hoverRow && a == hoverAction;
            if (a == 0 || hot) Theme.FillRound(g, hot ? Theme.Hover : Theme.Muted, ar, 8 * S);
            int icon = (int)(18 * S);
            var size = TextRenderer.MeasureText(Actions[a], Theme.Body);
            int x = ar.X + (ar.Width - icon - (int)(7 * S) - size.Width) / 2;
            Icons.Draw(g, ActionIcons[a], new RectangleF(x, ar.Y + (ar.Height - icon) / 2f, icon, icon), Theme.Text, 1.9f);
            TextRenderer.DrawText(g, Actions[a], Theme.Body, new Rectangle(x + icon + (int)(7 * S), ar.Y, size.Width + 2, ar.Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    // Loaded on first paint and kept; a missing or unreadable icon falls back to a drawn folder,
    // so one bad file can never break the page.
    Bitmap? IconFor(Entry entry)
    {
        if (icons.TryGetValue(entry.Folder, out var cached)) return cached;
        if (icons.Count > 400) ClearIcons(); // only a few screens' worth are ever needed at once
        Bitmap? bmp = null;
        try
        {
            if (FolderIcon.IconOf(entry.Folder) is { } file && File.Exists(file))
                using (var ico = new Icon(file, 48, 48)) bmp = ico.ToBitmap();
        }
        catch (Exception) { }
        bmp ??= IconMaker.DrawFolder(48, entry.Color);
        icons[entry.Folder] = bmp;
        return bmp;
    }

    void ClearIcons()
    {
        foreach (var b in icons.Values) b?.Dispose();
        icons.Clear();
    }

    // Forget a folder's icon, e.g. after it changed.
    public void ForgetIcons() { ClearIcons(); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ClearIcons(); tip.Dispose(); }
        base.Dispose(disposing);
    }
}
