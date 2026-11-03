using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FolderColorizer;

// A file's preview as Explorer would draw it: a real thumbnail for photos, videos,
// PDFs and so on, otherwise the file type's icon.
sealed record Preview(Bitmap Image, bool IsThumbnail) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

static class Thumbs
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, int flags, out IntPtr hbitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid, out IShellItemImageFactory factory);

    const int SIIGBF_ICONONLY = 0x4, SIIGBF_THUMBNAILONLY = 0x8;
    // OneDrive "online-only" files: asking for a thumbnail could start a download.
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    public static Preview? Get(FileInfo file, int size)
    {
        bool cloudOnly = (file.Attributes & (FileAttributes.Offline | RecallOnDataAccess)) != 0;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(file.FullName, IntPtr.Zero, ref iid, out var factory);
            try
            {
                if (!cloudOnly && factory.GetImage(new Size(size, size), SIIGBF_THUMBNAILONLY, out var thumb) == 0)
                    return new Preview(ToBitmap(thumb), true);
                if (factory.GetImage(new Size(size, size), SIIGBF_ICONONLY, out var icon) == 0)
                    return new Preview(ToBitmap(icon), false);
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException) { }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int Type, Width, Height, WidthBytes;
        public short Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DIBSECTION
    {
        public BITMAP Bm;
        public int HeaderSize, HeaderWidth, HeaderHeight;
        public short HeaderPlanes, HeaderBitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        public int Mask0, Mask1, Mask2;
        public IntPtr Section;
        public int Offset;
    }

    [DllImport("gdi32.dll")]
    static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr h);

    // Image.FromHbitmap drops the alpha channel, so copy the pixels by hand.
    static Bitmap ToBitmap(IntPtr hbitmap)
    {
        try
        {
            if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0 || ds.Bm.BitsPixel != 32 || ds.Bm.Bits == IntPtr.Zero)
                return Image.FromHbitmap(hbitmap);

            int w = ds.Bm.Width, h = ds.Bm.Height, stride = ds.Bm.WidthBytes;
            bool bottomUp = ds.HeaderHeight > 0;
            var pixels = new byte[w * 4 * h];
            for (int y = 0; y < h; y++)
                Marshal.Copy(ds.Bm.Bits + (bottomUp ? h - 1 - y : y) * stride, pixels, y * w * 4, w * 4);

            // Some thumbnails come back with an all-zero alpha channel; treat those as opaque.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length && !hasAlpha; i += 4)
                hasAlpha = pixels[i] != 0;
            if (!hasAlpha)
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 255;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < h; y++)
                Marshal.Copy(pixels, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            return bmp;
        }
        finally
        {
            DeleteObject(hbitmap);
        }
    }
}
