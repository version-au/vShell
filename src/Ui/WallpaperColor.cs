using System;
using System.IO;
using Microsoft.Win32;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Wallpaper Theme: the main monitor's wallpaper turned into a theme. The colours are worked out by the suite's library
    /// (vSuite.Core's WallpaperColor - the same code gives vXplorer and vView the same colours); vShell knows which picture is on the main
    /// monitor (its own per-monitor pictures and slideshows), so it picks the file.
    /// </summary>
    public static class WallpaperColor
    {
        public static bool TryGetPalette(BarSettings s, out WallpaperPalette p)
        {
            return vSuite.WallpaperColor.TryGetPalette(FindWallpaper(s), out p);
        }

        static string _seen;
        /// <summary>true when the wallpaper file (or which file) changed since the last call - the theme should then be rebuilt.</summary>
        public static bool Changed(BarSettings s)
        {
            string path = FindWallpaper(s);
            string key = "";
            try { if (!string.IsNullOrEmpty(path)) key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; } catch { }
            if (key == _seen) return false;
            bool first = _seen == null;
            _seen = key;
            return !first;
        }
        /// <summary>The main (primary) monitor's picture: its per-monitor wallpaper, else the Windows wallpaper.</summary>
        static string FindWallpaper(BarSettings s)
        {
            try
            {
                if (s.PerMonitorWallpaper)
                {
                    // the picture vShell shows on the main monitor (a slideshow's current one too)
                    MonitorInfo prim = MonitorHelper.Primary();
                    string img = WallpaperService.ImageOf(WallpaperService.EntryFor(s, prim));
                    if (img != null) return img;
                }
                {
                    MonitorInfo prim = MonitorHelper.Primary();
                    string f = prim != null ? WallpaperService.FileFor(prim) : null;
                    if (f != null) return f;
                }
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string p = k == null ? null : k.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(t)) return t;
            }
            catch { }
            return null;
        }


        public static string Hex(System.Windows.Media.Color c) { return vSuite.WallpaperColor.Hex(c); }
    }
}