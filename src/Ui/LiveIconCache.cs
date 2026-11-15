using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The last live icon of a pinned app (one that sets "vShell.LiveIcon", like vXplorer drawing its icon in the theme), so its pin keeps
    /// showing it once the app is closed - instead of the program's fixed icon. Kept in memory and as a PNG in
    /// %APPDATA%\vShell\cache\live-icons (one per pin path), written only when the app's icon changes, so it lasts across restarts.
    /// </summary>
    public static class LiveIconCache
    {
        static readonly Dictionary<string, ImageSource> Icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, long> Versions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string Folder { get { return Path.Combine(BarSettings.ConfigDir, "cache", "live-icons"); } }

        static string FileFor(string pinPath)
        {
            uint h = 2166136261;
            foreach (char c in pinPath.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return Path.Combine(Folder, h.ToString("x8") + ".png");
        }

        /// <summary>The pin's app is showing this live icon (version = its LiveIcon counter): remember it, on disk when it's new.</summary>
        public static void Remember(string pinPath, long version, ImageSource icon)
        {
            if (string.IsNullOrEmpty(pinPath) || icon == null) return;
            long seen;
            if (Versions.TryGetValue(pinPath, out seen) && seen == version && Icons.ContainsKey(pinPath)) return;
            Versions[pinPath] = version;
            Icons[pinPath] = icon;
            Missing.Remove(pinPath);
            var bmp = icon as BitmapSource;
            if (bmp == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                string file = FileFor(pinPath), tmp = file + ".tmp";
                using (var fs = File.Create(tmp)) enc.Save(fs);
                File.Move(tmp, file, true);
            }
            catch (Exception ex) { Log.Write("Live icon cache: " + ex.Message); }
        }

        /// <summary>The last live icon seen for this pin, or null (then the program's own icon is used).</summary>
        public static ImageSource Get(string pinPath)
        {
            if (string.IsNullOrEmpty(pinPath)) return null;
            ImageSource icon;
            if (Icons.TryGetValue(pinPath, out icon)) return icon;
            if (Missing.Contains(pinPath)) return null;
            try
            {
                string file = FileFor(pinPath);
                if (File.Exists(file))
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.CacheOption = BitmapCacheOption.OnLoad;   // don't keep the file open
                    b.UriSource = new Uri(file);
                    b.EndInit();
                    b.Freeze();
                    Icons[pinPath] = b;
                    return b;
                }
            }
            catch (Exception ex) { Log.Write("Live icon cache: " + ex.Message); }
            Missing.Add(pinPath);
            return null;
        }
    }
}
