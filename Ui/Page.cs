namespace FolderColorizer.Ui;

// A page in the main window: title, subtitle and content laid out by Arrange().
abstract class Page : UserControl
{
    protected readonly Label TitleLabel, SubtitleLabel;
    protected float S => DeviceDpi / 96f;
    protected int Pad => (int)(32 * S);
    protected int Top0 => (int)(104 * S);

    protected Page(string title, string subtitle)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        TitleLabel = UiText.Make(title, Theme.Title, Theme.Text);
        SubtitleLabel = UiText.Make(subtitle, Theme.Body, Theme.TextMuted);
        Controls.Add(TitleLabel);
        Controls.Add(SubtitleLabel);
    }

    // Called each time the page is shown.
    public virtual void Activate() { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // Layout runs as early as the base constructor; wait until the page is fully built.
        if (!IsHandleCreated) return;
        TitleLabel.Location = new Point(Pad, (int)(26 * S));
        SubtitleLabel.Location = new Point(Pad + (int)(2 * S), (int)(64 * S));
        SubtitleLabel.MaximumSize = new Size(Math.Max(100, Width - Pad * 2), 0);
        Arrange();
    }

    protected abstract void Arrange();

    // Runs shell work on a background STA thread (thumbnails and folder settings use COM),
    // then reports back on the UI thread.
    protected void RunInBackground(Action work, Action<Exception?> done)
    {
        var t = new Thread(() =>
        {
            Exception? error = null;
            try { work(); }
            catch (Exception ex) { error = ex; }
            if (!IsDisposed) BeginInvoke(() => done(error));
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

    protected void Report(Exception? error, string ok, Label status)
    {
        status.ForeColor = error == null ? Theme.Accent : Theme.Danger;
        status.Text = error == null ? ok : error.Message;
    }
}
