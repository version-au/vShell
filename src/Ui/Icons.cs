using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

namespace vShell.Ui
{
    public static class IconUtil
    {
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Segoe Fluent Icons (Windows 11) with Segoe MDL2 Assets as fallback.</summary>
        [PluginApi] public static readonly FontFamily GlyphFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public const string GlyphFolder = "\uE838";
        public const string GlyphFile = "\uE7C3";
        public const string GlyphAdd = "\uE710";
        public const string GlyphChevronUp = "\uE70E";
        public const string GlyphChevronDown = "\uE70D";
        public const string GlyphGlobe = "\uE774";
        public const string NetworkWifiGlyph = "\uE701";
        public const string NetworkWiredGlyph = "\uE839";

        /// <summary>Built-in icons for launcher groups: saved key, name shown, Segoe Fluent Icons glyph. Drawn in the theme's colours.</summary>
        public static readonly Tuple<string, string, string>[] GroupIcons =
        {
            Tuple.Create("apps", "Applications", "\uE71D"),
            Tuple.Create("package", "Package", "\uE7B8"),
            Tuple.Create("star", "Favourites", "\uE734"),
            Tuple.Create("heart", "Heart", "\uEB51"),
            Tuple.Create("internet", "Internet", "\uE774"),
            Tuple.Create("world", "World", "\uE909"),
            Tuple.Create("link", "Links", "\uE71B"),
            Tuple.Create("cloud", "Cloud", "\uE753"),
            Tuple.Create("network", "Network", "\uE839"),
            Tuple.Create("wifi", "Wi-Fi", "\uE701"),
            Tuple.Create("mail", "Mail", "\uE715"),
            Tuple.Create("chat", "Chat", "\uE8F2"),
            Tuple.Create("people", "People", "\uE716"),
            Tuple.Create("media", "Media", "\uE768"),
            Tuple.Create("music", "Music", "\uEC4F"),
            Tuple.Create("headphones", "Headphones", "\uE7F6"),
            Tuple.Create("video", "Video", "\uE714"),
            Tuple.Create("tv", "TV", "\uE7F4"),
            Tuple.Create("photos", "Photos", "\uE91B"),
            Tuple.Create("camera", "Camera", "\uE722"),
            Tuple.Create("microphone", "Microphone", "\uE720"),
            Tuple.Create("games", "Games", "\uE7FC"),
            Tuple.Create("console", "Console", "\uE990"),
            Tuple.Create("puzzle", "Puzzle", "\uEA86"),
            Tuple.Create("documents", "Documents", "\uE8A5"),
            Tuple.Create("edit", "Writing", "\uE70F"),
            Tuple.Create("list", "Lists", "\uE8FD"),
            Tuple.Create("library", "Library", "\uE8F1"),
            Tuple.Create("education", "Education", "\uE7BE"),
            Tuple.Create("calendar", "Calendar", "\uE787"),
            Tuple.Create("calculator", "Calculator", "\uE8EF"),
            Tuple.Create("palette", "Art and design", "\uE790"),
            Tuple.Create("code", "Code", "\uE943"),
            Tuple.Create("terminal", "Terminal", "\uE756"),
            Tuple.Create("devtools", "Developer tools", "\uEC7A"),
            Tuple.Create("tools", "Tools", "\uE90F"),
            Tuple.Create("settings", "Settings", "\uE713"),
            Tuple.Create("admin", "Admin", "\uE7EF"),
            Tuple.Create("security", "Security", "\uE72E"),
            Tuple.Create("key", "Keys", "\uE8D7"),
            Tuple.Create("computer", "Computer", "\uE7F8"),
            Tuple.Create("printer", "Printer", "\uE749"),
            Tuple.Create("download", "Downloads", "\uE896"),
            Tuple.Create("home", "Home", "\uE80F"),
            Tuple.Create("shopping", "Shopping", "\uE719"),
            Tuple.Create("money", "Money", "\uE8C7"),
            Tuple.Create("bank", "Bank", "\uE825"),
            Tuple.Create("health", "Health", "\uE95E"),
            Tuple.Create("travel", "Travel", "\uE709"),
            Tuple.Create("map", "Maps", "\uE707"),
            Tuple.Create("idea", "Ideas", "\uEA80"),
            Tuple.Create("flag", "Flag", "\uE7C1"),
            Tuple.Create("tag", "Tag", "\uE8EC"),
            Tuple.Create("power", "Power", "\uE7E8"),
        };

        /// <summary>The glyph of a built-in group icon, or null when the key is empty / unknown.</summary>
        public static string GroupGlyph(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var g in GroupIcons) if (g.Item1 == key) return g.Item3;
            return null;
        }

        /// <summary>A Segoe Fluent Icons glyph for a module type (widget headers).</summary>
        public static string ModuleGlyph(string type)
        {
            switch (type)
            {
                case ModuleTypes.Clock: return "\uE823";
                case ModuleTypes.Weather: return "\uE706";
                case ModuleTypes.NowPlaying: return "\uE8D6";
                case ModuleTypes.Network: return "\uE701";
                case ModuleTypes.Disk: return "\uEDA2";
                case ModuleTypes.Drives: return "\uE7F8";
                case ModuleTypes.Battery: return "\uE83F";
                case ModuleTypes.Volume: return "\uE767";
                case ModuleTypes.Uptime: return "\uE916";
                case ModuleTypes.Services: return "\uE774";
                case ModuleTypes.Gpu: return "\uE7F4";
                case ModuleTypes.Notifications: return "\uEA8F";
                case ModuleTypes.Cpu:
                case ModuleTypes.Memory:
                case ModuleTypes.Hwinfo: return "\uE9D9";
                case ModuleTypes.Label: return "\uE70F";
                case ModuleTypes.Notes: return "\uE70B";
                case ModuleTypes.RunBox: return "\uE721";
                case ModuleTypes.Terminal: return "\uE756";
                case ModuleTypes.StartMenu: return "\uE700";
                default:
                    {
                        PluginModule pm = PluginManager.ModuleOf(type);
                        if (pm != null) return pm.Glyph;
                        PluginInfo pi = PluginStore.OwnerOf(type);
                        return pi != null ? pi.Glyph : "\uE71D";
                    }
            }
        }

        public static TextBlock Glyph(string glyph, Brush foreground, double size)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = GlyphFont,
                FontSize = size,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        public static ImageSource FromHIcon(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            try
            {
                BitmapSource s = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                s.Freeze();
                return s;
            }
            catch { return null; }
        }

        static ImageSource FromShell(string path, uint flags, string cachePrefix)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = cachePrefix + path;
            ImageSource cached;
            if (TryCached(key, out cached)) return cached;

            ImageSource result = null;
            var fi = new SHFILEINFO();
            Native.SHGetFileInfo(path, 0, ref fi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(SHFILEINFO)), flags);
            if (fi.hIcon != IntPtr.Zero)
            {
                result = FromHIcon(fi.hIcon);
                Native.DestroyIcon(fi.hIcon);
            }
            Remember(key, result);
            return result;
        }

        /// <summary>Small (16px) icon of an executable - used for taskbar buttons whose window has no icon.</summary>
        public static ImageSource FromExe(string path)
        {
            return FromShell(path, Native.SHGFI_ICON | Native.SHGFI_SMALLICON, "S|");
        }

        /// <summary>Large (32px) shell icon of a file, shortcut or folder - used by the launcher. Null for URLs / missing paths.</summary>
        public static ImageSource FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            try { if (!File.Exists(p) && !Directory.Exists(p)) return null; }
            catch { return null; }
            return FromShell(p, Native.SHGFI_ICON, "L|");
        }

        /// <summary>
        /// Custom icon chosen by the user: an image/icon file (.ico .png .jpg .bmp .gif) or a program/DLL/shortcut to take the icon from.
        /// Null when empty or unreadable.
        /// </summary>
        public static ImageSource FromIconFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p;
            try { p = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))); }
            catch { return null; }

            string ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext == ".exe" || ext == ".dll" || ext == ".lnk" || ext == ".url") return FromPath(p);

            string key;
            try
            {
                if (!File.Exists(p)) return null;
                key = "I|" + p + "|" + File.GetLastWriteTimeUtc(p).Ticks;
            }
            catch { return null; }

            ImageSource cached;
            if (TryCached(key, out cached)) return cached;

            ImageSource result = null;
            try
            {
                if (ext == ".ico")
                {
                    BitmapDecoder dec = BitmapDecoder.Create(new Uri(p), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame best = null;
                    foreach (var f in dec.Frames) if (best == null || f.PixelWidth > best.PixelWidth) best = f;
                    if (best != null) { best.Freeze(); result = best; }
                }
                else
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bi.UriSource = new Uri(p);
                    bi.DecodePixelWidth = 96;
                    bi.EndInit();
                    bi.Freeze();
                    result = bi;
                }
            }
            catch (Exception ex) { Log.Write("Icon '" + p + "': " + ex.Message); }
            Remember(key, result);
            return result;
        }

        // ---- icons of Store (packaged) apps: shell items such as "shell:AppsFolder\<AUMID>" -------------------
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [In] ref Guid riid,
                                                       [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        /// <summary>Square icon/tile image of any shell item (used for Store apps). Null when the shell cannot provide one.</summary>
        public static ImageSource FromParsingName(string name, int px) { return FromParsingName(name, px, false); }

        /// <summary>fresh = ignore the cache (e.g. the Recycle Bin icon changes between empty and full).</summary>
        public static ImageSource FromParsingName(string name, int px, bool fresh)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = "P|" + px + "|" + name;
            ImageSource cached;
            if (!fresh && TryCached(key, out cached)) return cached;

            ImageSource result = null;
            try
            {
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                IShellItemImageFactory f;
                SHCreateItemFromParsingName(name, IntPtr.Zero, ref iid, out f);
                try
                {
                    IntPtr hbm;
                    int hr = f.GetImage(new SIZE { cx = px, cy = px }, 0, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero) hr = f.GetImage(new SIZE { cx = px, cy = px }, 0x4 /* SIIGBF_ICONONLY */, out hbm);
                    if (hr == 0 && hbm != IntPtr.Zero)
                    {
                        try { result = FromHBitmapWithAlpha(hbm); }
                        finally { DeleteObject(hbm); }
                    }
                }
                finally { Marshal.ReleaseComObject(f); }
            }
            catch (Exception ex) { if (FailCount(key) == 0) Log.Write("Shell image '" + name + "': " + ex.Message + " (asked again later, less and less often)"); }
            Remember(key, result);
            return result;
        }

        public static void ClearCache() { lock (Cache) Cache.Clear(); }

        /// <summary>The cache is also filled from the Start menu's background icon loader, so it is locked.</summary>
        static bool TryCached(string key, out ImageSource img)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out img)) return true;
                // it failed a moment ago: don't ask Windows again yet (but do later - an icon can fail once while an app updates or
                // Windows is still starting, and stayed blank until vShell restarted)
                Fail f;
                img = null;
                return Failed.TryGetValue(key, out f) && DateTime.UtcNow < f.Next;
            }
        }

        /// <summary>How long a failed icon first waits before it is asked for again (then twice as long each time, up to half an hour).</summary>
        public const int RetrySeconds = 20;
        sealed class Fail { public DateTime Next; public int Count; }
        static readonly Dictionary<string, Fail> Failed = new Dictionary<string, Fail>(StringComparer.OrdinalIgnoreCase);

        static int FailCount(string key) { lock (Cache) { Fail f; return Failed.TryGetValue(key, out f) ? f.Count : 0; } }

        static void Remember(string key, ImageSource result)
        {
            lock (Cache)
            {
                if (result != null) { Cache[key] = result; Failed.Remove(key); return; }
                // an icon that keeps failing (an app that's gone) is asked for less and less often: 20 s, 40 s, 80 s … 30 min
                Fail f;
                if (!Failed.TryGetValue(key, out f)) { f = new Fail(); Failed[key] = f; }
                f.Count++;
                f.Next = DateTime.UtcNow.AddSeconds(Math.Min(1800, RetrySeconds * Math.Pow(2, Math.Min(10, f.Count - 1))));
            }
        }

        /// <summary>Seconds until the next failed icon may be asked for again (RetrySeconds when none is waiting).</summary>
        public static double NextRetrySeconds()
        {
            lock (Cache)
            {
                double best = double.MaxValue;
                foreach (Fail f in Failed.Values) best = Math.Min(best, (f.Next - DateTime.UtcNow).TotalSeconds);
                return best == double.MaxValue ? RetrySeconds : Math.Max(1, best);
            }
        }

        // ---- a 32-bit HBITMAP with its transparency (CreateBitmapSourceFromHBitmap drops the alpha: Store icons got a black box) ----
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        static BitmapSource FromHBitmapWithAlpha(IntPtr hbm)
        {
            try
            {
                BITMAP bm;
                if (GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm) != 0 && bm.bmBitsPixel == 32 && bm.bmWidth > 0 && bm.bmHeight > 0)
                {
                    int w = bm.bmWidth, h = bm.bmHeight;
                    var bmi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                    var px = new byte[w * h * 4];
                    IntPtr dc = GetDC(IntPtr.Zero);
                    int got;
                    try { got = GetDIBits(dc, hbm, 0, (uint)h, px, ref bmi, 0); }
                    finally { ReleaseDC(IntPtr.Zero, dc); }
                    if (got == h)
                    {
                        bool anyAlpha = false;
                        for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                        // the shell hands out premultiplied pixels; a bitmap with no alpha at all is opaque
                        BitmapSource bs = BitmapSource.Create(w, h, 96, 96, anyAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, w * 4);
                        bs.Freeze();
                        return bs;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Icon bitmap: " + ex.Message); }
            BitmapSource plain = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            plain.Freeze();
            return plain;
        }
    }
}
