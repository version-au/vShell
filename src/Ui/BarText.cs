using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace vShell.Ui
{
    /// <summary>
    /// Build 2: bar text comes in three sizes - S (one line, small), M (two lines) and L (one line, large) - instead of a font size.
    /// Every font is drawn so it takes the same room as the reference font (Segoe UI): a narrow font a little bigger, a wide one a
    /// little smaller, so changing the font or the theme never changes how wide a module is.
    /// </summary>
    public static class BarText
    {
        public const double Small = 12, Medium = 14, Large = 18;
        const string Sample = "0123456789 %.:/ CPUMemDiskNet abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        static readonly Dictionary<string, double> Factors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        static double _ref = -1;

        /// <summary>The size in the reference font for S / M / L.</summary>
        public static double Px(string size)
        {
            switch (size) { case "L": return Large; case "M": return Medium; default: return Small; }
        }

        /// <summary>How much to scale a font so it takes the reference font's room (0.75 - 1.3).</summary>
        public static double Factor(FontFamily font)
        {
            if (font == null) return 1;
            string key = font.Source ?? "";
            double f;
            lock (Factors) if (Factors.TryGetValue(key, out f)) return f;
            f = 1;
            try
            {
                if (_ref < 0) _ref = Width(new FontFamily("Segoe UI"));
                double w = Width(font);
                if (_ref > 0 && w > 0) f = Math.Max(0.75, Math.Min(1.3, _ref / w));
            }
            catch { f = 1; }
            lock (Factors) Factors[key] = f;
            return f;
        }

        static double Width(FontFamily font)
        {
            var ft = new FormattedText(Sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                       new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 100, Brushes.Black, 1.0);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>The size a module can have on a bar this tall: M needs room for two lines, L for one large one (else S).</summary>
        public static string Fit(string size, double barHeight)
        {
            if (size == "M" && barHeight < Medium * 2 * 1.15) return "S";      // two lines need about 33
            if (size == "L" && barHeight < Large * 1.35) return "S";
            return size == "M" || size == "L" ? size : "S";
        }

        /// <summary>A full bar's text cap: level 0 = as set, 1 = L becomes M, 2 = everything S.</summary>
        public static string Cap(string size, int level)
        {
            if (level >= 2) return "S";
            if (level == 1 && size == "L") return "M";
            return size;
        }
    }
}
