using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

namespace VShellShelf
{
    /// <summary>
    /// Pictures for shelf items: the thumbnail Explorer shows (a photo, a video frame, a PDF page) or the file's icon, from
    /// IShellItemImageFactory - and whether the item is still there. Worked out on one background thread (a network path or a big
    /// video can take a moment); the answer comes back on the UI thread. Pictures are kept for the session.
    /// </summary>
    public static class ShellThumbs
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, IntPtr buf);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        static readonly Guid IID_Factory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        sealed class Job
        {
            public string Path;
            public int Px;
            public Action<ImageSource, bool> Done;
        }

        static readonly BlockingCollection<Job> Jobs = new BlockingCollection<Job>();
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static Thread _thread;

        /// <summary>The picture (null = none: show a glyph) and whether it's there, to done on the UI thread.</summary>
        public static void Get(string path, int px, Action<ImageSource, bool> done)
        {
            if (_thread == null)
            {
                _thread = new Thread(Work) { IsBackground = true, Name = "Shelf thumbnails" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
            Jobs.Add(new Job { Path = path, Px = px, Done = done });
        }

        /// <summary>Forget a path's picture (a file found again somewhere else, a card changed).</summary>
        public static void Forget(string path)
        {
            lock (Cache)
                foreach (string k in new List<string>(Cache.Keys))
                    if (k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)) Cache.Remove(k);
        }

        static void Work()
        {
            foreach (Job j in Jobs.GetConsumingEnumerable())
            {
                bool there = ShelfStore.Exists(j.Path);
                ImageSource img = null;
                string key = j.Path + "|" + j.Px;
                lock (Cache) Cache.TryGetValue(key, out img);
                if (img == null && there)
                {
                    try { img = Make(j.Path, j.Px); }
                    catch (Exception ex) { Log.Write("Shelf: a thumbnail for " + j.Path + " - " + ex.Message); }
                    if (img != null) lock (Cache) Cache[key] = img;
                }
                Application app = Application.Current;
                if (app == null) continue;
                ImageSource done = img;
                Job job = j;
                app.Dispatcher.BeginInvoke(new Action(delegate { job.Done(done, there); }));
            }
        }

        static ImageSource Make(string path, int px)
        {
            Guid iid = IID_Factory;
            IShellItemImageFactory f;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out f) < 0 || f == null) return null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                // 0 = a thumbnail if there is one, else the icon, fitted into the size
                if (f.GetImage(new SIZE { cx = px, cy = px }, 0, out hbm) < 0 || hbm == IntPtr.Zero) return null;
                return FromHBitmap(hbm);
            }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                Marshal.ReleaseComObject(f);
            }
        }

        /// <summary>
        /// A 32-bit DIB section as a frozen BitmapSource, keeping its transparency (CreateBitmapSourceFromHBitmap loses it). A
        /// thumbnail with no alpha at all (a photo) is taken as opaque.
        /// </summary>
        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            bool x64 = IntPtr.Size == 8;
            IntPtr buf = Marshal.AllocHGlobal(128);
            try
            {
                int got = GetObject(hbm, x64 ? 104 : 84, buf);         // DIBSECTION
                if (got <= 0) return null;
                int w = Marshal.ReadInt32(buf, 4), h = Marshal.ReadInt32(buf, 8), stride = Marshal.ReadInt32(buf, 12);
                int bpp = Marshal.ReadInt16(buf, 18);
                IntPtr bits = Marshal.ReadIntPtr(buf, x64 ? 24 : 20);
                if (w <= 0 || h <= 0 || bits == IntPtr.Zero || bpp != 32)
                {
                    BitmapSource b = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    b.Freeze();
                    return b;
                }
                int bmih = x64 ? 32 : 24;
                int biHeight = got >= bmih + 12 ? Marshal.ReadInt32(buf, bmih + 8) : h;
                var px = new byte[stride * h];
                if (biHeight > 0)
                {
                    // bottom-up: the first row in memory is the bottom one
                    for (int y = 0; y < h; y++) Marshal.Copy(bits + (h - 1 - y) * stride, px, y * stride, stride);
                }
                else Marshal.Copy(bits, px, 0, px.Length);
                bool alpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { alpha = true; break; }
                BitmapSource bs = BitmapSource.Create(w, h, 96, 96, alpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, stride);
                bs.Freeze();
                return bs;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }
}
