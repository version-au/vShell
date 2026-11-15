using System;
using System.Windows;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Frozen brushes/fonts derived from the settings, shared colours; one instance per bar (font size / radius are per bar).</summary>
    [PluginApi]
    public sealed class Theme
    {
        public SolidColorBrush Bg, PopupBg, Fg, FgDim, MainAccent, Hover, ActiveBg, Track;
        public FontFamily Font;
        public double FontSize;
        public CornerRadius Corner;
        public double Radius;
        // extended theme
        public Color BgColor, Bg2Color;
        public bool Gradient;
        public int GradientAngle = 90;
        /// <summary>The Secondary Accent, and the three brushes of the dots between modules (Main Accent, Secondary Accent and text colour - never the background).</summary>
        public SolidColorBrush SecondaryAccent;
        public Brush[] DotBrushes;

        /// <summary>
        /// The bar / widget background: the solid colour or the two-colour gradient. opacityPercent &lt; 0 = the theme's own transparency,
        /// otherwise that percentage replaces the alpha (both gradient stops).
        /// </summary>
        public Brush MakeBg(int opacityPercent)
        {
            byte a1 = opacityPercent < 0 ? BgColor.A : (byte)Math.Round(Math.Min(100, opacityPercent) * 255.0 / 100.0);
            byte a2 = opacityPercent < 0 ? Bg2Color.A : a1;
            if (!Gradient)
            {
                var sb = new SolidColorBrush(Color.FromArgb(a1, BgColor.R, BgColor.G, BgColor.B));
                sb.Freeze();
                return sb;
            }
            double rad = GradientAngle * Math.PI / 180.0;
            double dx = Math.Cos(rad), dy = Math.Sin(rad);
            var g = new LinearGradientBrush(Color.FromArgb(a1, BgColor.R, BgColor.G, BgColor.B), Color.FromArgb(a2, Bg2Color.R, Bg2Color.G, Bg2Color.B),
                                            new Point(0.5 - dx / 2, 0.5 - dy / 2), new Point(0.5 + dx / 2, 0.5 + dy / 2));
            g.Freeze();
            return g;
        }

        /// <summary>A second shade of a colour for a companion series: lighter for dark colours, darker for light ones (frozen).</summary>
        public static SolidColorBrush Shade(Brush b)
        {
            var sb = b as SolidColorBrush;
            Color c = sb != null ? sb.Color : Colors.Gray;
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            double k = lum > 0.62 ? 0.0 : 255.0;           // blend toward white or black
            double f = lum > 0.62 ? 0.45 : 0.45;
            Color r = Color.FromArgb(c.A, (byte)Math.Round(c.R + (k - c.R) * f), (byte)Math.Round(c.G + (k - c.G) * f), (byte)Math.Round(c.B + (k - c.B) * f));
            var n = new SolidColorBrush(r);
            n.Freeze();
            return n;
        }

        /// <summary>The brush with its alpha multiplied by percent/100 (frozen).</summary>
        public static SolidColorBrush Faded(SolidColorBrush b, int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            Color c = b.Color;
            var f = new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * percent / 100.0), c.R, c.G, c.B));
            f.Freeze();
            return f;
        }

        public static Color Parse(string s, Color fallback)
        {
            try
            {
                object o = ColorConverter.ConvertFromString(s);
                if (o is Color) return (Color)o;
            }
            catch { }
            return fallback;
        }

        /// <summary>Theme slots a module colour can refer to, so it follows theme changes.</summary>
        public const string SlotMain = "@main", SlotSecondary = "@secondary", SlotText = "@text";
        /// <summary>The slot names used before the colours were renamed (still understood).</summary>
        public const string OldSlotMain = "@accent", OldSlotSecondary = "@spare";

        /// <summary>This theme, or a copy with the module's own font size when it does not follow the bar's / widget's.</summary>
        public Theme ForModule(ModuleConfig cfg)
        {
            if (cfg == null) return this;
            // on a bar (Build 2): the S / M / L size the bar is showing it at
            double size = cfg.ShownFontSize > 0 ? cfg.ShownFontSize : cfg.FollowFontSize ? FontSize : cfg.FontSize;
            if (Math.Abs(size - FontSize) < 0.01) return this;
            var t = (Theme)MemberwiseClone();
            t.FontSize = size;
            return t;
        }

        /// <summary>A copy with another text size (the Start menu's S / M / L).</summary>
        public Theme WithFontSize(double size)
        {
            if (Math.Abs(size - FontSize) < 0.01) return this;
            var t = (Theme)MemberwiseClone();
            t.FontSize = size;
            return t;
        }

        /// <summary>A copy with another Main Accent (a widget's own accent colour).</summary>
        public Theme WithAccent(SolidColorBrush accent)
        {
            if (accent == null || accent == MainAccent) return this;
            var t = (Theme)MemberwiseClone();
            t.MainAccent = accent;
            t.DotBrushes = new Brush[] { accent, SecondaryAccent, Fg };
            return t;
        }

        public static bool IsSlot(string spec) { return spec != null && spec.StartsWith("@", StringComparison.Ordinal); }

        /// <summary>A module colour setting: empty = fallback, "@main" / "@secondary" / "@text" = that theme colour, otherwise a colour value.</summary>
        /// <summary>How far before the threshold a graph starts fading into the warning colour (percent of the graph's full scale).</summary>
        public const double WarnFade = 20;

        /// <summary>A module's warning colour: its own, or the Secondary Accent.</summary>
        public SolidColorBrush Warn(string spec) { return Resolve(spec, SecondaryAccent); }

        public SolidColorBrush Resolve(string spec, SolidColorBrush fallback)
        {
            if (string.IsNullOrWhiteSpace(spec)) return fallback;
            string k = spec.Trim().ToLowerInvariant();
            if (k == SlotMain || k == OldSlotMain) return MainAccent;
            if (k == SlotSecondary || k == OldSlotSecondary) return SecondaryAccent;
            if (k == SlotText) return Fg;
            Color c;
            if (TryParse(spec.Trim(), out c)) return B(c);
            return fallback;
        }

        /// <summary>True when a warning threshold is switched on and the value is past it (at or above, or at or below).</summary>
        public static bool Crossed(double value, bool on, double at, bool below)
        {
            if (!on) return false;
            return below ? value <= at : value >= at;
        }

        public static bool TryParse(string s, out Color c)
        {
            try
            {
                object o = ColorConverter.ConvertFromString(s);
                if (o is Color) { c = (Color)o; return true; }
            }
            catch { }
            c = Colors.Transparent;
            return false;
        }

        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Color A(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        /// <summary>Colours and font family come from the shared settings; font size and corner radius belong to the bar.</summary>
        public static Theme From(BarSettings s, BarPlacement bar)
        {
            return From(s, bar.FontSize, bar.CornerRadius);
        }

        /// <summary>Same shared colours and font family, with an explicit font size and corner radius (bars and widgets each have their own).</summary>
        public static Theme From(BarSettings s, double fontSize, double cornerRadius)
        {
            Color bg = Parse(s.Background, Color.FromArgb(0xE6, 0x1E, 0x1E, 0x2E));
            Color bg2 = Parse(s.Background2, Color.FromArgb(0xE6, 0x31, 0x32, 0x44));
            Color fg = Parse(s.Foreground, Color.FromRgb(0xCD, 0xD6, 0xF4));
            Color ac = Parse(s.MainAccent, Color.FromRgb(0x89, 0xB4, 0xFA));

            Color sec = Parse(s.SecondaryAccent, Color.FromRgb(0xF5, 0xC2, 0xE7));

            // Wallpaper Theme: every colour from the main monitor's wallpaper; the opacity stays the one set in Background / Gradient
            if (s.WallpaperTheme)
            {
                WallpaperPalette w;
                if (WallpaperColor.TryGetPalette(s, out w))
                {
                    bg = A(w.Background, bg.A);
                    bg2 = A(w.Background2, bg2.A);
                    fg = w.Foreground;
                    ac = w.MainAccent;
                    sec = w.SecondaryAccent;
                }
            }

            var t = new Theme();
            t.BgColor = bg; t.Bg2Color = bg2;
            t.Gradient = s.BgStyle == "Gradient";
            t.GradientAngle = s.GradientAngle;
            t.SecondaryAccent = B(A(sec, 0xFF));
            t.Bg = B(bg);
            byte popupAlpha = bg.A < (byte)0xF2 ? (byte)0xF2 : bg.A;
            t.PopupBg = B(Color.FromArgb(popupAlpha, bg.R, bg.G, bg.B));
            t.Fg = B(A(fg, 0xFF));
            t.FgDim = B(A(fg, 0x99));
            t.MainAccent = B(A(ac, 0xFF));
            t.DotBrushes = new Brush[] { t.MainAccent, t.SecondaryAccent, B(A(fg, 0xFF)) };
            t.Hover = B(A(fg, 0x22));
            t.ActiveBg = B(A(fg, 0x38));
            t.Track = B(A(fg, 0x30));
            t.Font = new FontFamily("Segoe UI");     // Build 2: the only font (the old Font family setting is ignored)
            t.FontSize = fontSize;
            t.Radius = cornerRadius;
            t.Corner = new CornerRadius(cornerRadius);
            return t;
        }
    }
}
