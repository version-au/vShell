using System;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    /// <summary>
    /// Live window thumbnails drawn by the desktop compositor (the same thing Windows' own taskbar previews use). The thumbnail is
    /// painted by DWM straight onto one of our windows, on top of whatever WPF drew there, so it stays live and costs us nothing.
    /// </summary>
    public static class DwmThumbnail
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PROPS
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public int fVisible;
            public int fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PSIZE { public int x, y; }

        const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref PROPS props);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);

        /// <summary>Link src's picture to dest (a top-level window of ours). Zero on failure.</summary>
        public static IntPtr Register(IntPtr dest, IntPtr src)
        {
            try
            {
                IntPtr t;
                return DwmRegisterThumbnail(dest, src, out t) == 0 ? t : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }

        public static void Unregister(IntPtr thumb)
        {
            if (thumb == IntPtr.Zero) return;
            try { DwmUnregisterThumbnail(thumb); } catch { }
        }

        /// <summary>The source window's size in pixels (0,0 when unknown).</summary>
        public static void SourceSize(IntPtr thumb, out int w, out int h)
        {
            w = h = 0;
            try
            {
                PSIZE s;
                if (DwmQueryThumbnailSourceSize(thumb, out s) == 0) { w = s.x; h = s.y; }
            }
            catch { }
        }

        /// <summary>Show the thumbnail in this rectangle of the destination window (client pixels).</summary>
        public static void Place(IntPtr thumb, int left, int top, int right, int bottom)
        {
            if (thumb == IntPtr.Zero) return;
            var p = new PROPS
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new RECT { Left = left, Top = top, Right = right, Bottom = bottom },
                opacity = 255,
                fVisible = 1,
                fSourceClientAreaOnly = 0
            };
            try { DwmUpdateThumbnailProperties(thumb, ref p); } catch { }
        }
    }
}
