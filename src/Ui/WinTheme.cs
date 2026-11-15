using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Win32;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// "Match Windows to vShell's theme": Windows' light / dark mode from how light vShell's background is, and Windows' accent colour
    /// from vShell's Main Accent (with its lighter / darker shades worked out the way Settings does). The Windows values are saved the
    /// first time and put back when it is turned off. Only writes when something changed.
    /// </summary>
    public static class WinTheme
    {
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        const string Dwm = @"Software\Microsoft\Windows\DWM";
        const string Desktop = @"Control Panel\Desktop";
        static string _applied = "";

        static string BackupPath { get { return Path.Combine(BarSettings.ConfigDir, "windows-colours-backup.json"); } }

        static readonly object Gate = new object();
        static System.Windows.Threading.DispatcherTimer _soon;
        static bool _pMode, _pAccent, _pLight, _busy, _again;
        static Color _pAc;

        /// <summary>
        /// Bring Windows in line with the settings (called whenever settings / the theme are applied). Only notes what Windows should
        /// look like: the registry and the "colours changed" message to every app happen a moment later on a background thread, once
        /// the changes have stopped, so vShell's own theme goes on at once and dragging a colour picker sends Windows one change.
        /// </summary>
        public static void Apply(BarSettings s)
        {
            try
            {
                bool mode = s.WinMatch && s.WinMatchMode, accent = s.WinMatch && s.WinMatchAccent;
                bool light = false; Color ac = Colors.Transparent;
                if (mode || accent)
                {
                    Theme t = Theme.From(s, 12, 0);
                    light = Luma(t.BgColor) > 0.5; ac = t.MainAccent.Color;
                }
                lock (Gate) { _pMode = mode; _pAccent = accent; _pLight = light; _pAc = ac; }
                if (_soon == null)
                {
                    _soon = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    _soon.Tick += delegate { _soon.Stop(); Kick(); };
                }
                _soon.Stop(); _soon.Start();
            }
            catch (Exception ex) { Log.Write("Windows colours: " + ex.Message); }
        }

        /// <summary>Do the Windows side on a background thread (one at a time; a change that comes in meanwhile runs straight after).</summary>
        static void Kick()
        {
            lock (Gate) { if (_busy) { _again = true; return; } _busy = true; }
            System.Threading.Tasks.Task.Run(delegate
            {
                while (true)
                {
                    bool mode, accent, light; Color ac;
                    lock (Gate) { mode = _pMode; accent = _pAccent; light = _pLight; ac = _pAc; _again = false; }
                    Work(mode, accent, light, ac);
                    lock (Gate) { if (!_again) { _busy = false; return; } }
                }
            });
        }

        static void Work(bool mode, bool accent, bool light, Color ac)
        {
            try
            {
                // first time this run, or one of the two was just turned off: start again from your own Windows colours
                bool dropped = _applied.Length > 0 && ((_applied.StartsWith("True|") && !mode) || (_applied.Split('|').Length > 1 && _applied.Split('|')[1] == "True" && !accent));
                // (at start-up only when one of the two is off now - with both on, the saved copy is kept and nothing flickers back)
                bool firstPartOff = _applied.Length == 0 && !(mode && accent);
                if ((firstPartOff || dropped) && (mode || accent) && File.Exists(BackupPath)) { Restore(); _applied = ""; }
                if (!mode && !accent)
                {
                    if (File.Exists(BackupPath)) Restore();
                    _applied = "";
                    return;
                }
                string key = mode + "|" + accent + "|" + light + "|" + ac;
                if (key == _applied) return;
                Backup();
                if (mode)
                {
                    SetDword(Personalize, "AppsUseLightTheme", light ? 1 : 0);
                    SetDword(Personalize, "SystemUsesLightTheme", light ? 1 : 0);
                }
                if (accent) SetAccent(ac);
                _applied = key;
                Log.Write("Windows colours set: " + (mode ? (light ? "light" : "dark") + " mode" : "") + (mode && accent ? ", " : "") + (accent ? "accent " + ac : ""));
                Broadcast();
            }
            catch (Exception ex) { Log.Write("Windows colours: " + ex.Message); }
        }

        static double Luma(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

        // ---- the accent: its shades, the palette and the DWM colours ----
        static void SetAccent(Color ac)
        {
            // eight shades, lightest to darkest, as Windows keeps them (index 3 = the colour itself)
            double[] steps = { 0.36, 0.24, 0.12, 0, -0.12, -0.24, -0.36, -0.48 };
            var pal = new byte[32];
            var shades = new Color[8];
            for (int i = 0; i < 8; i++)
            {
                shades[i] = Shift(ac, steps[i]);
                pal[i * 4] = shades[i].R; pal[i * 4 + 1] = shades[i].G; pal[i * 4 + 2] = shades[i].B; pal[i * 4 + 3] = 0;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Accent))
            {
                k.SetValue("AccentPalette", pal, RegistryValueKind.Binary);
                k.SetValue("AccentColorMenu", Abgr(shades[3]), RegistryValueKind.DWord);
                k.SetValue("StartColorMenu", Abgr(shades[5]), RegistryValueKind.DWord);
            }
            SetDword(Dwm, "AccentColor", Abgr(shades[3]));
            SetDword(Dwm, "ColorizationColor", Argb(shades[3], 0xC4));
            SetDword(Dwm, "ColorizationAfterglow", Argb(shades[3], 0xC4));
            // stop Windows picking an accent from the wallpaper (it would replace this one)
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Desktop)) k.SetValue("AutoColorization", "0", RegistryValueKind.String);
        }

        /// <summary>Lighter (amount &gt; 0) or darker (&lt; 0) in HSL lightness.</summary>
        static Color Shift(Color c, double amount)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double h = 0, s = 0, l = (max + min) / 2;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6;
            }
            l = Math.Max(0.04, Math.Min(0.96, l + amount));
            Func<double, double, double, double> hue = delegate (double p, double q, double tt)
            {
                if (tt < 0) tt += 1; if (tt > 1) tt -= 1;
                if (tt < 1.0 / 6) return p + (q - p) * 6 * tt;
                if (tt < 0.5) return q;
                if (tt < 2.0 / 3) return p + (q - p) * (2.0 / 3 - tt) * 6;
                return p;
            };
            double rr, gg, bb;
            if (s == 0) rr = gg = bb = l;
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                rr = hue(p, q, h + 1.0 / 3); gg = hue(p, q, h); bb = hue(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb((byte)Math.Round(rr * 255), (byte)Math.Round(gg * 255), (byte)Math.Round(bb * 255));
        }

        static int Abgr(Color c) { return unchecked((int)(0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R)); }
        static int Argb(Color c, byte a) { return unchecked((int)(((uint)a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B)); }

        static void SetDword(string path, string name, int value)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(path)) k.SetValue(name, value, RegistryValueKind.DWord);
        }

        // ---- telling Windows and apps ----
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

        static void Broadcast()
        {
            IntPtr r;
            foreach (string what in new[] { "ImmersiveColorSet", "WindowsThemeElement" })
                SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /*WM_SETTINGCHANGE*/, IntPtr.Zero, what, 0x0002 /*SMTO_ABORTIFHUNG*/, 300, out r);
        }

        // ---- the user's own Windows colours: saved once, put back when this is turned off ----
        sealed class Saved { public string Key = "", Path = "", Name = "", Kind = ""; public string Value = ""; public bool Missing; }

        static readonly string[][] Values =
        {
            new[] { Personalize, "AppsUseLightTheme" }, new[] { Personalize, "SystemUsesLightTheme" },
            new[] { Accent, "AccentPalette" }, new[] { Accent, "AccentColorMenu" }, new[] { Accent, "StartColorMenu" },
            new[] { Dwm, "AccentColor" }, new[] { Dwm, "ColorizationColor" }, new[] { Dwm, "ColorizationAfterglow" },
            new[] { Desktop, "AutoColorization" }
        };

        static void Backup()
        {
            if (File.Exists(BackupPath)) return;
            var list = new List<Saved>();
            foreach (string[] v in Values)
            {
                var sv = new Saved { Path = v[0], Name = v[1] };
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(v[0]))
                {
                    object o = k == null ? null : k.GetValue(v[1]);
                    if (o == null) sv.Missing = true;
                    else
                    {
                        RegistryValueKind kind = k.GetValueKind(v[1]);
                        sv.Kind = kind.ToString();
                        sv.Value = o is byte[] ? Convert.ToBase64String((byte[])o) : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                list.Add(sv);
            }
            Directory.CreateDirectory(BarSettings.ConfigDir);
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { IncludeFields = true }));
        }

        static void Restore()
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(BackupPath), new JsonSerializerOptions { IncludeFields = true });
                if (list != null)
                    foreach (Saved sv in list)
                        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(sv.Path))
                        {
                            if (sv.Missing) { k.DeleteValue(sv.Name, false); continue; }
                            RegistryValueKind kind;
                            if (!Enum.TryParse(sv.Kind, out kind)) continue;
                            if (kind == RegistryValueKind.Binary) k.SetValue(sv.Name, Convert.FromBase64String(sv.Value), kind);
                            else if (kind == RegistryValueKind.DWord) k.SetValue(sv.Name, unchecked((int)long.Parse(sv.Value, System.Globalization.CultureInfo.InvariantCulture)), kind);
                            else k.SetValue(sv.Name, sv.Value, kind);
                        }
                File.Delete(BackupPath);
                Broadcast();
            }
            catch (Exception ex) { Log.Write("Windows colours (restore): " + ex.Message); }
        }
    }
}
