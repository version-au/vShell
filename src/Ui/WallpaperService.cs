using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// vShell's wallpapers, set with Windows' own per-monitor wallpaper (IDesktopWallpaper - what Settings > Personalisation uses).
    /// Each monitor gets a picture made at exactly its size: the image placed by its fit (Fill, Fit, Stretch, Center, Tile) over its
    /// background (black, the theme, a blurred copy of the image, a colour or a gradient), so every monitor can have its own fit and
    /// background - Windows itself only has one fit for all. The pictures are cached by what went into them, so changing one monitor
    /// (or a slideshow moving on) only makes that one again. "Span across all" uses Windows' own span with the original image.
    /// Monitors are remembered by MonitorInfo.Key (the monitor itself), so a reconnected monitor gets its picture back.
    /// </summary>
    public static class WallpaperService
    {
        public static readonly string[] FitModes = { "Fill", "Fit", "Stretch", "Center", "Tile" };
        public static readonly string[] Modes = { "Each", "Same", "Span" };
        public static readonly string[] ModeNames = { "Each monitor", "Same on all", "Span across all" };
        public const string AllMonitors = "*";

        /// <summary>Backgrounds (behind a Fit / Center picture): "Black", "Blur", "Theme", "Solid", "Gradient" or one of the presets.</summary>
        public static readonly string[] Backgrounds = { "Blur", "Black", "Theme", "Midnight", "Ocean", "Aurora", "Sunset", "Dusk", "Grape", "Peach", "Graphite", "Solid", "Gradient" };
        /// <summary>The ready-made gradients: two colours and an angle.</summary>
        public static readonly Dictionary<string, string[]> Gradients = new Dictionary<string, string[]>
        {
            { "Midnight", new[] { "#0F2027", "#2C5364", "135" } },
            { "Ocean",    new[] { "#1A2980", "#26D0CE", "135" } },
            { "Aurora",   new[] { "#0B3D2E", "#3CBA92", "160" } },
            { "Sunset",   new[] { "#FF7E5F", "#FEB47B", "120" } },
            { "Dusk",     new[] { "#2C3E50", "#FD746C", "135" } },
            { "Grape",    new[] { "#41295A", "#2F0743", "135" } },
            { "Peach",    new[] { "#ED4264", "#FFEDBC", "135" } },
            { "Graphite", new[] { "#232526", "#414345", "180" } },
        };
        static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

        static string CacheDir { get { return Path.Combine(BarSettings.ConfigDir, "wallpapers"); } }

        // =================================================================
        // Windows' per-monitor wallpaper
        // =================================================================
        enum DesktopWallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(DesktopWallpaperPosition position);
            DesktopWallpaperPosition GetPosition();
            void SetSlideshow(IntPtr items);
            IntPtr GetSlideshow();
            void SetSlideshowOptions(int options, uint slideshowTick);
            void GetSlideshowOptions(out int options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
            int GetStatus();
            void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }

        static readonly Guid DesktopWallpaperClsid = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

        // by its CLSID, not 'new' of a class of ours: Windows gives the whole process one wallpaper object, and the bars' blur (vSuite's
        // Glass) makes it first - .NET reuses that wrapper, and a cast from our own class failed ("Specified cast is not valid")
        static IDesktopWallpaper Desktop() { return vSuite.ComObject.Create<IDesktopWallpaper>(DesktopWallpaperClsid); }

        /// <summary>A connected monitor as Windows' wallpaper knows it (its id) and as vShell knows it.</summary>
        sealed class Screen { public string Id; public MonitorInfo Mon; }

        static List<Screen> Screens(IDesktopWallpaper dw)
        {
            var list = new List<Screen>();
            List<MonitorInfo> mons = MonitorHelper.GetAll();
            uint n = dw.GetMonitorDevicePathCount();
            for (uint i = 0; i < n; i++)
            {
                string id;
                RECT r;
                try { id = dw.GetMonitorDevicePathAt(i); r = dw.GetMonitorRECT(id); }
                catch { continue; }                     // a monitor Windows remembers but isn't connected
                if (string.IsNullOrEmpty(id) || r.Right <= r.Left) continue;
                MonitorInfo mon = mons.Find(delegate (MonitorInfo m) { return string.Equals(m.Key, id, StringComparison.OrdinalIgnoreCase); })
                               ?? mons.Find(delegate (MonitorInfo m) { return m.Bounds.Left == r.Left && m.Bounds.Top == r.Top && m.Bounds.Right == r.Right && m.Bounds.Bottom == r.Bottom; });
                if (mon != null) list.Add(new Screen { Id = id, Mon = mon });
            }
            return list;
        }

        /// <summary>The entry a monitor uses (in "Same" / "Span": the one for all).</summary>
        public static MonitorWallpaper EntryFor(BarSettings s, MonitorInfo m)
        {
            if (s.WallpaperMode == "Same" || s.WallpaperMode == "Span") return AllEntry(s);
            if (m == null) return null;
            MonitorWallpaper byName = null;
            foreach (MonitorWallpaper e in s.Wallpapers)
            {
                if (e.Monitor == AllMonitors) continue;
                if (!string.IsNullOrEmpty(e.MonitorKey) && string.Equals(e.MonitorKey, m.Key, StringComparison.OrdinalIgnoreCase)) return e;
                if (string.IsNullOrEmpty(e.MonitorKey) && string.Equals(e.Monitor, m.DeviceName, StringComparison.OrdinalIgnoreCase)) byName = e;
            }
            if (byName != null && m.Key.Length > 0) byName.MonitorKey = m.Key;      // remember the monitor itself from now on
            return byName;
        }

        /// <summary>The entry for a monitor, made if it isn't there yet (Settings).</summary>
        public static MonitorWallpaper EnsureEntry(BarSettings s, MonitorInfo m)
        {
            MonitorWallpaper e = EntryFor(s, m);
            if (e != null) return e;
            e = new MonitorWallpaper { Monitor = m.DeviceName, MonitorKey = m.Key };
            s.Wallpapers.Add(e);
            return e;
        }

        public static MonitorWallpaper AllEntry(BarSettings s)
        {
            MonitorWallpaper e = s.Wallpapers.Find(delegate (MonitorWallpaper x) { return x.Monitor == AllMonitors; });
            if (e == null)
            {
                // the first time: start from the primary monitor's picture
                MonitorInfo prim = MonitorHelper.Primary();
                MonitorWallpaper from = null;
                if (prim != null) foreach (MonitorWallpaper x in s.Wallpapers) if (x.Monitor != AllMonitors && string.Equals(x.MonitorKey, prim.Key, StringComparison.OrdinalIgnoreCase)) from = x;
                e = new MonitorWallpaper { Monitor = AllMonitors };
                if (from != null) { e.Path = from.Path; e.Fit = from.Fit; e.Background = from.Background; e.Color1 = from.Color1; e.Color2 = from.Color2; e.Angle = from.Angle; }
                s.Wallpapers.Add(e);
            }
            return e;
        }

        /// <summary>The picture an entry shows now: its slideshow's current one, else its image.</summary>
        public static string ImageOf(MonitorWallpaper e)
        {
            if (e == null) return null;
            if (!string.IsNullOrWhiteSpace(e.SlideFolder) && !string.IsNullOrEmpty(e.SlideCurrent) && File.Exists(e.SlideCurrent)) return e.SlideCurrent;
            if (!string.IsNullOrWhiteSpace(e.SlideFolder) && Directory.Exists(e.SlideFolder))
            {
                List<string> files = Pictures(e.SlideFolder);
                if (files.Count > 0) { e.SlideCurrent = files[0]; return e.SlideCurrent; }
            }
            return !string.IsNullOrWhiteSpace(e.Path) && File.Exists(e.Path) ? e.Path : null;
        }

        /// <summary>No picture, but a colour or gradient background chosen: that fills the whole monitor (or desktop, spanned).</summary>
        public static bool ColourOnly(MonitorWallpaper e)
        {
            if (e == null || ImageOf(e) != null) return false;
            string b = string.IsNullOrEmpty(e.Background) ? "Blur" : e.Background;
            return b != "Blur";
        }

        /// <summary>Set the wallpapers. Returns an error message, or null.</summary>
        public static string Apply(BarSettings s)
        {
            try
            {
                IDesktopWallpaper dw = Desktop();
                List<Screen> screens = Screens(dw);
                if (screens.Count == 0) return "No monitors found.";

                // before 0.38.30 vShell set one spanned picture and saved the wallpaper from before that: that's the one to put back
                if (s.WallpaperOriginals == null && s.OriginalWallpaper != null)
                {
                    s.WallpaperOriginals = new List<SavedWallpaper>();
                    foreach (Screen sc in screens) s.WallpaperOriginals.Add(new SavedWallpaper { MonitorId = sc.Id, Path = s.OriginalWallpaper });
                    switch ((s.OriginalWallpaperStyle ?? "10").Trim())
                    {
                        case "6": s.WallpaperOriginalPosition = (int)DesktopWallpaperPosition.Fit; break;
                        case "2": s.WallpaperOriginalPosition = (int)DesktopWallpaperPosition.Stretch; break;
                        case "22": s.WallpaperOriginalPosition = (int)DesktopWallpaperPosition.Span; break;
                        case "0": s.WallpaperOriginalPosition = (s.OriginalWallpaperTile ?? "0").Trim() == "1" ? (int)DesktopWallpaperPosition.Tile : (int)DesktopWallpaperPosition.Center; break;
                        default: s.WallpaperOriginalPosition = (int)DesktopWallpaperPosition.Fill; break;
                    }
                    s.OriginalWallpaper = null; s.OriginalWallpaperStyle = null; s.OriginalWallpaperTile = null;
                    try { foreach (string old in Directory.GetFiles(BarSettings.ConfigDir, "wallpaper-*.png")) try { File.Delete(old); } catch { } } catch { }
                }
                // Windows' own wallpapers, saved once, to put back when vShell stops setting them
                if (s.WallpaperOriginals == null)
                {
                    s.WallpaperOriginals = new List<SavedWallpaper>();
                    foreach (Screen sc in screens)
                    {
                        string cur = "";
                        try { cur = dw.GetWallpaper(sc.Id) ?? ""; } catch { }
                        s.WallpaperOriginals.Add(new SavedWallpaper { MonitorId = sc.Id, Path = cur });
                    }
                    try { s.WallpaperOriginalPosition = (int)dw.GetPosition(); } catch { s.WallpaperOriginalPosition = (int)DesktopWallpaperPosition.Fill; }
                }

                if (s.WallpaperMode == "Span")
                {
                    MonitorWallpaper all = AllEntry(s);
                    string img = ImageOf(all);
                    if (img == null && ColourOnly(all))
                    {
                        // no picture: the colour / gradient made the size of the whole desktop, spanned across it
                        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
                        foreach (Screen sc in screens)
                        {
                            l = Math.Min(l, sc.Mon.Bounds.Left); t = Math.Min(t, sc.Mon.Bounds.Top);
                            r = Math.Max(r, sc.Mon.Bounds.Right); b = Math.Max(b, sc.Mon.Bounds.Bottom);
                        }
                        img = Render(all, null, r - l, b - t, Theme.From(s, 12, 0));
                        if (img == null) return "Couldn't make the background.";
                        dw.SetPosition(DesktopWallpaperPosition.Span);
                        dw.SetWallpaper(null, img);
                        Prune(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { img });
                        return null;
                    }
                    if (img == null) return "Choose a picture (or a colour or gradient background) to span across your monitors.";
                    dw.SetPosition(DesktopWallpaperPosition.Span);
                    dw.SetWallpaper(null, Path.GetFullPath(img));
                    Prune(new HashSet<string>());
                    return null;
                }

                // each monitor its own picture, made at its size: Windows "Fill" then shows it pixel for pixel
                dw.SetPosition(DesktopWallpaperPosition.Fill);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool any = false;
                Theme th = Theme.From(s, 12, 0);
                foreach (Screen sc in screens)
                {
                    MonitorWallpaper e = EntryFor(s, sc.Mon);
                    string img = ImageOf(e);
                    string file = null;
                    if (img != null || ColourOnly(e))
                    {
                        file = Render(e, img, sc.Mon.Bounds.Width, sc.Mon.Bounds.Height, th);    // no picture: the colour / gradient fills it
                        any = true;
                    }
                    if (file == null)
                    {
                        // nothing chosen for this one: its own Windows wallpaper from before
                        SavedWallpaper o = s.WallpaperOriginals.Find(delegate (SavedWallpaper x) { return string.Equals(x.MonitorId, sc.Id, StringComparison.OrdinalIgnoreCase); });
                        if (o != null && !string.IsNullOrEmpty(o.Path) && File.Exists(o.Path)) file = o.Path;
                    }
                    if (file == null) continue;
                    used.Add(file);
                    string now = "";
                    try { now = dw.GetWallpaper(sc.Id) ?? ""; } catch { }
                    if (!string.Equals(now, file, StringComparison.OrdinalIgnoreCase)) dw.SetWallpaper(sc.Id, file);
                }
                Prune(used);
                return any ? null : "Choose a picture (or a colour or gradient background) for at least one monitor.";
            }
            catch (Exception ex) { Log.Write("Wallpaper apply: " + ex); return ex.Message; }
        }

        /// <summary>Put back the wallpapers Windows had before vShell set them.</summary>
        public static string Restore(BarSettings s)
        {
            try
            {
                if (s.WallpaperOriginals == null) return null;
                IDesktopWallpaper dw = Desktop();
                try { dw.SetPosition((DesktopWallpaperPosition)Math.Max(0, Math.Min(5, s.WallpaperOriginalPosition))); } catch { }
                foreach (Screen sc in Screens(dw))
                {
                    SavedWallpaper o = s.WallpaperOriginals.Find(delegate (SavedWallpaper x) { return string.Equals(x.MonitorId, sc.Id, StringComparison.OrdinalIgnoreCase); });
                    if (o != null && !string.IsNullOrEmpty(o.Path) && File.Exists(o.Path)) dw.SetWallpaper(sc.Id, o.Path);
                }
                s.WallpaperOriginals = null;
                Prune(new HashSet<string>());
                return null;
            }
            catch (Exception ex) { Log.Write("Wallpaper restore: " + ex.Message); return ex.Message; }
        }

        /// <summary>The picture on a monitor now, as a file (what Windows shows there), or null.</summary>
        public static string FileFor(MonitorInfo m)
        {
            try
            {
                IDesktopWallpaper dw = Desktop();
                foreach (Screen sc in Screens(dw))
                    if (sc.Mon.Handle == m.Handle || string.Equals(sc.Mon.DeviceName, m.DeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        string f = dw.GetWallpaper(sc.Id);
                        return !string.IsNullOrEmpty(f) && File.Exists(f) ? f : null;
                    }
            }
            catch { }
            return null;
        }

        /// <summary>vShell's own picture for a monitor is set (exact size, so draw it as Fill).</summary>
        public static bool IsOurs(string file)
        {
            return !string.IsNullOrEmpty(file) && file.StartsWith(CacheDir, StringComparison.OrdinalIgnoreCase);
        }

        static void Prune(HashSet<string> keep)
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                foreach (string f in Directory.GetFiles(CacheDir, "*.png"))
                    if (!keep.Contains(f) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalMinutes > 2)
                        try { File.Delete(f); } catch { }
            }
            catch { }
        }

        // =================================================================
        // Making a monitor's picture
        // =================================================================
        /// <summary>The finished picture for one monitor (cached by everything that goes into it).</summary>
        static string Render(MonitorWallpaper e, string img, int w, int h, Theme th)
        {
            if (w <= 0 || h <= 0) return null;
            string bg = BackgroundKey(e, th);
            string sig = (img == null ? "none" : img + "|" + File.GetLastWriteTimeUtc(img).Ticks) + "|" + w + "x" + h + "|" + (e.Fit ?? "Fill") + "|" + bg;
            string file = Path.Combine(CacheDir, Hash(sig) + ".png");
            if (File.Exists(file)) { try { File.SetLastWriteTimeUtc(file, DateTime.UtcNow); } catch { } return file; }
            BitmapSource bmp = Compose(e, img, w, h, th);
            if (bmp == null) return null;
            Directory.CreateDirectory(CacheDir);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            string tmp = file + ".tmp";
            using (var fs = File.Create(tmp)) enc.Save(fs);
            File.Copy(tmp, file, true);
            File.Delete(tmp);
            return file;
        }

        /// <summary>A small preview of what a monitor would show (Settings), maxWidth pixels wide.</summary>
        public static BitmapSource Preview(BarSettings s, MonitorWallpaper e, int monW, int monH, int maxWidth)
        {
            string img = ImageOf(e);
            if ((img == null && !ColourOnly(e)) || monW <= 0 || monH <= 0) return null;
            double k = Math.Min(1, (double)maxWidth / monW);
            return Compose(e, img, Math.Max(8, (int)(monW * k)), Math.Max(8, (int)(monH * k)), Theme.From(s, 12, 0), k);
        }

        static string BackgroundKey(MonitorWallpaper e, Theme th)
        {
            string b = string.IsNullOrEmpty(e.Background) ? "Blur" : e.Background;
            if (b == "Theme") return b + th.BgColor + th.Bg2Color;
            if (b == "Solid") return b + e.Color1;
            if (b == "Gradient") return b + e.Color1 + e.Color2 + e.Angle;
            return b;
        }

        /// <summary>The background for a monitor's picture (null = black).</summary>
        public static Brush BackgroundBrush(MonitorWallpaper e, Theme th)
        {
            string b = string.IsNullOrEmpty(e.Background) ? "Blur" : e.Background;
            Color c1, c2;
            switch (b)
            {
                case "Black": case "Blur": return null;
                case "Theme": return Grad(Opaque(th.BgColor), Opaque(th.Bg2Color), 135);
                case "Solid": return new SolidColorBrush(Theme.TryParse(e.Color1, out c1) ? Opaque(c1) : Color.FromRgb(0x20, 0x20, 0x28));
                case "Gradient":
                    return Grad(Theme.TryParse(e.Color1, out c1) ? Opaque(c1) : Color.FromRgb(0x0F, 0x20, 0x27),
                                Theme.TryParse(e.Color2, out c2) ? Opaque(c2) : Color.FromRgb(0x2C, 0x53, 0x64), e.Angle);
            }
            string[] g;
            if (Gradients.TryGetValue(b, out g) && Theme.TryParse(g[0], out c1) && Theme.TryParse(g[1], out c2)) return Grad(c1, c2, int.Parse(g[2]));
            return null;
        }

        static Color Opaque(Color c) { return Color.FromRgb(c.R, c.G, c.B); }

        static LinearGradientBrush Grad(Color a, Color b, int angle)
        {
            // an angle in degrees (0 = left to right, 90 = top to bottom), across the whole picture
            double r = angle * Math.PI / 180, dx = Math.Cos(r) / 2, dy = Math.Sin(r) / 2;
            var g = new LinearGradientBrush(a, b, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
            g.Freeze();
            return g;
        }

        /// <summary>Draw the background and the picture by its fit (scale &lt; 1: a preview, so Center / Tile shrink with it).</summary>
        static BitmapSource Compose(MonitorWallpaper e, string imgPath, int w, int h, Theme th, double scale = 1)
        {
            var r = new Rect(0, 0, w, h);
            if (imgPath == null)
            {
                // no picture: the background on its own, over the whole picture
                Brush only = BackgroundBrush(e, th);
                var bv = new DrawingVisual();
                using (DrawingContext dc = bv.RenderOpen()) dc.DrawRectangle(only ?? Brushes.Black, null, r);
                var brt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                brt.Render(bv);
                brt.Freeze();
                return brt;
            }
            BitmapImage img = Load(imgPath, Math.Max(w, h) > 1200 ? 0 : Math.Max(w, 64) * 2);
            if (img == null) return null;
            string fit = string.IsNullOrEmpty(e.Fit) ? "Fill" : e.Fit;
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);      // smooth when the picture is scaled
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, r);
                bool covers = fit == "Fill" || fit == "Stretch" || fit == "Tile";
                if (!covers)
                {
                    Brush bgb = BackgroundBrush(e, th);
                    if (bgb != null) dc.DrawRectangle(bgb, null, r);
                    else if ((string.IsNullOrEmpty(e.Background) ? "Blur" : e.Background) == "Blur") DrawBlur(dc, img, r);
                }
                Draw(dc, img, r, fit, scale);
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>The picture itself, filling the screen, very blurred and a little darker - behind a Fit / Center picture.</summary>
        static void DrawBlur(DrawingContext dc, BitmapImage img, Rect r)
        {
            // small first (cheap), then a real blur on that, then stretched up smooth
            int sw = Math.Max(16, (int)(r.Width / 24)), sh = Math.Max(16, (int)(r.Height / 24));
            var small = new DrawingVisual();
            using (DrawingContext sdc = small.RenderOpen()) Draw(sdc, img, new Rect(0, 0, sw, sh), "Fill", 1);
            var smallBmp = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
            smallBmp.Render(small);
            smallBmp.Freeze();
            const int pad = 4;
            var image = new Image { Source = smallBmp, Stretch = Stretch.Fill, Width = sw + 2 * pad, Height = sh + 2 * pad, Effect = new BlurEffect { Radius = 3.5, KernelType = KernelType.Gaussian } };
            var canvas = new Canvas { Width = sw, Height = sh, ClipToBounds = true };
            Canvas.SetLeft(image, -pad); Canvas.SetTop(image, -pad);
            canvas.Children.Add(image);
            canvas.Measure(new Size(sw, sh));
            canvas.Arrange(new Rect(0, 0, sw, sh));
            canvas.UpdateLayout();
            var blurred = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
            blurred.Render(canvas);
            blurred.Freeze();
            dc.DrawImage(blurred, r);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), null, r);      // a little darker, so the picture stands out
        }

        internal static BitmapImage Load(string path) { return Load(path, 0); }

        internal static BitmapImage Load(string path, int decodeWidth)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(Path.GetFullPath(path));
                if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex) { Log.Write("Wallpaper image '" + path + "': " + ex.Message); return null; }
        }

        internal static void Draw(DrawingContext dc, BitmapImage img, Rect r, string fit) { Draw(dc, img, r, fit, 1); }

        internal static void Draw(DrawingContext dc, BitmapImage img, Rect r, string fit, double scale)
        {
            double iw = img.PixelWidth, ih = img.PixelHeight;
            if (iw <= 0 || ih <= 0) return;
            dc.PushClip(new RectangleGeometry(r));
            switch (fit)
            {
                case "Stretch":
                    dc.DrawImage(img, r);
                    break;
                case "Center":
                    {
                        // its own size (smaller on a preview); a picture bigger than the screen is cut, like Windows does
                        double cw = iw * scale, ch = ih * scale;
                        dc.DrawImage(img, new Rect(r.Left + (r.Width - cw) / 2, r.Top + (r.Height - ch) / 2, cw, ch));
                        break;
                    }
                case "Tile":
                    {
                        double tw = Math.Max(4, iw * scale), thh = Math.Max(4, ih * scale);
                        for (double y = r.Top; y < r.Bottom; y += thh)
                            for (double x = r.Left; x < r.Right; x += tw)
                                dc.DrawImage(img, new Rect(x, y, tw, thh));
                        break;
                    }
                default:    // Fill (cover) or Fit (contain)
                    {
                        double sx = r.Width / iw, sy = r.Height / ih;
                        double sc = fit == "Fit" ? Math.Min(sx, sy) : Math.Max(sx, sy);
                        double dw = iw * sc, dh = ih * sc;
                        dc.DrawImage(img, new Rect(r.Left + (r.Width - dw) / 2, r.Top + (r.Height - dh) / 2, dw, dh));
                        break;
                    }
            }
            dc.Pop();
        }

        static string Hash(string s)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
                return BitConverter.ToString(b, 0, 10).Replace("-", "").ToLowerInvariant();
            }
        }

        // =================================================================
        // Slideshow
        // =================================================================
        /// <summary>The pictures in a folder (sorted by name).</summary>
        public static List<string> Pictures(string folder)
        {
            var list = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
                foreach (string f in Directory.GetFiles(folder))
                    if (Array.IndexOf(ImageExts, Path.GetExtension(f).ToLowerInvariant()) >= 0) list.Add(f);
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Write("Slideshow folder " + folder + ": " + ex.Message); }
            return list;
        }

        static readonly Random Rnd = new Random();

        /// <summary>Move an entry's slideshow on to its next picture. true = it changed.</summary>
        public static bool Next(MonitorWallpaper e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.SlideFolder)) return false;
            List<string> files = Pictures(e.SlideFolder);
            if (files.Count == 0) return false;
            string next;
            int at = files.FindIndex(delegate (string f) { return string.Equals(f, e.SlideCurrent, StringComparison.OrdinalIgnoreCase); });
            if (e.SlideShuffle && files.Count > 1)
            {
                do next = files[Rnd.Next(files.Count)]; while (string.Equals(next, e.SlideCurrent, StringComparison.OrdinalIgnoreCase));
            }
            else next = files[(at + 1) % files.Count];
            e.SlideCurrent = next;
            e.SlideNext = DateTime.UtcNow.AddMinutes(Math.Max(1, e.SlideMinutes));
            return true;
        }

        /// <summary>Slideshows that are due move on (the manager calls this every half minute). true = set the wallpapers again.</summary>
        public static bool SlideTick(BarSettings s)
        {
            if (!s.PerMonitorWallpaper) return false;
            bool changed = false;
            DateTime now = DateTime.UtcNow;
            foreach (MonitorWallpaper e in Active(s))
            {
                if (string.IsNullOrWhiteSpace(e.SlideFolder) || e.SlideMinutes <= 0) continue;
                if (e.SlideNext == DateTime.MinValue) { e.SlideNext = now.AddMinutes(e.SlideMinutes); continue; }
                if (now >= e.SlideNext) changed |= Next(e);
            }
            return changed;
        }

        /// <summary>Every slideshow in use moves on now ("Next wallpaper"). true = something changed.</summary>
        public static bool NextAll(BarSettings s)
        {
            bool changed = false;
            foreach (MonitorWallpaper e in Active(s)) changed |= Next(e);
            return changed;
        }

        /// <summary>The entries in use with the current mode.</summary>
        static List<MonitorWallpaper> Active(BarSettings s)
        {
            var list = new List<MonitorWallpaper>();
            if (s.WallpaperMode == "Same" || s.WallpaperMode == "Span") { list.Add(AllEntry(s)); return list; }
            foreach (MonitorInfo m in MonitorHelper.GetAll())
            {
                MonitorWallpaper e = EntryFor(s, m);
                if (e != null && !list.Contains(e)) list.Add(e);
            }
            return list;
        }
    }
}
