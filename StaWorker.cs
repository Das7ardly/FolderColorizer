namespace FolderColorizer;

// Runs folder work (thumbnails and folder settings use COM, which wants an STA thread) off the
// UI thread, then reports back on the thread that started it. The tray's UI thread also runs
// the keyboard hook, which Windows silently removes if that thread stalls.
static class StaWorker
{
    public static void Run(Action work, Action<Exception?>? done = null)
    {
        var context = SynchronizationContext.Current;
        var t = new Thread(() =>
        {
            Exception? error = null;
            try { work(); }
            catch (Exception ex) { error = ex; }
            if (done == null) return;
            if (context != null) context.Post(_ => done(error), null);
            else done(error);
        });
        t.SetApartmentState(ApartmentState.STA);
        // Foreground: closing a window must not cut a folder change off halfway.
        t.IsBackground = false;
        t.Start();
    }
}
