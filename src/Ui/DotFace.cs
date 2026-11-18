using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The "Dots" style (CPU, 0.38.226): the readings as columns of dots on a dim dot field, the newest column in the graph colour, the
    /// number drawn in 5 x 7 dots (or in the theme's font - "Numbers in dots" off) and a small grey upper-case label.
    /// In a bar: label, dot graph and number in one row. In a widget: a tile - label (and the detail on the right), the big number, and
    /// the dot graph filling the rest of the module, as tall as the widget lets it be.
    /// </summary>
    internal sealed class DotFace : FrameworkElement
    {
        readonly History _data;
        Theme _t;
        IModuleHost _host;
        Brush _accent, _warnFill, _on, _off;
        Color[] _load;
        bool _widget, _dotNumbers = true, _showLabel = true;
        double _graphW = 60, _heightPercent = 60;
        int _span = 60;
        double _warnAt = -1;
        bool _warnBelow;

        string _label = "", _number = "", _unit = "", _detail;
        bool _warn;

        public DotFace(History data) { _data = data; }

        /// <summary>The look: from the module's settings and theme (again whenever they change).</summary>
        public void Setup(ModuleConfig cfg, IModuleHost host, Theme t)
        {
            _t = t; _host = host;
            _widget = host.IsWidget;
            _accent = t.Resolve(cfg.Color, t.MainAccent);
            _warnFill = t.Warn(cfg.WarnColor);
            _warnAt = cfg.WarnOn ? Math.Max(0, Math.Min(100, cfg.WarnAt)) : -1;
            _warnBelow = cfg.WarnBelow;
            _on = t.Fg;
            Color fg = t.Fg.Color;
            _off = Frozen(Color.FromArgb(38, fg.R, fg.G, fg.B));
            _dotNumbers = cfg.Bag.Get("dots.numbers", true);
            _showLabel = cfg.ShowText;
            _graphW = Math.Max(20, cfg.ShownGraphWidth);
            _heightPercent = Math.Max(20, Math.Min(100, cfg.GraphHeightPercent));
            _span = Math.Max(60, Math.Min(History.Capacity, cfg.GraphSpan));
            if (cfg.LoadColors && ModuleTypes.HasLoadColors(cfg.Type))
            {
                Color main = t.MainAccent.Color;
                _load = new[]
                {
                    string.IsNullOrWhiteSpace(cfg.LoadColor1) ? Color.FromArgb(0x66, main.R, main.G, main.B) : t.Resolve(cfg.LoadColor1, t.MainAccent).Color,
                    string.IsNullOrWhiteSpace(cfg.LoadColor2) ? Color.FromArgb(0xCC, main.R, main.G, main.B) : t.Resolve(cfg.LoadColor2, t.MainAccent).Color,
                    string.IsNullOrWhiteSpace(cfg.LoadColor3) ? t.SecondaryAccent.Color : t.Resolve(cfg.LoadColor3, t.SecondaryAccent).Color
                };
            }
            else _load = null;
            HorizontalAlignment = _widget ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            VerticalAlignment = _widget ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>What it shows now: the label ("CPU"), the number ("37") and unit ("%"), an optional detail ("4.2 GHz"), warned or not.</summary>
        public void Show(string label, string number, string unit, string detail, bool warn)
        {
            bool resize = (number ?? "").Length != _number.Length || (detail ?? "") != (_detail ?? "") || label != _label;
            _label = label ?? ""; _number = number ?? ""; _unit = unit ?? ""; _detail = detail; _warn = warn;
            if (resize && !_widget) InvalidateMeasure();
            InvalidateVisual();
        }

        // ---- sizes (all from the theme's font size, so a bigger font makes bigger dots) ----

        double Fs { get { return _t != null ? _t.FontSize : 12; } }
        double HostH { get { return _host != null ? Math.Max(16, _host.HostHeight) : 24; } }
        /// <summary>The number's dot pitch: in a bar about the height of the text; in a widget about 2.2 x the font size tall.</summary>
        double NumCell { get { return _widget ? Math.Max(2, Math.Round(Fs * 0.32 * 2) / 2) : Math.Max(1.4, Math.Min(Fs * 0.16, (HostH - 6) / 7.0)); } }
        double LabelSize { get { return _widget ? Math.Max(7, Fs * 0.8) : Math.Max(7, Math.Min(Fs * 0.72, HostH * 0.36)); } }
        /// <summary>The bar's graph: 4 dots tall, filling the graph height.</summary>
        double BarCell { get { return Math.Max(2, HostH * _heightPercent / 100.0 / 4.0); } }
        double WidgetCell { get { return Math.Max(4, Math.Round(Fs * 0.6 * 2) / 2); } }
        const double Pad = 8;

        protected override Size MeasureOverride(Size available)
        {
            if (_t == null) return new Size(0, 0);
            if (_widget)
            {
                // label line, the number, then at least 4 rows of dots; as tall as the module row asks for at least
                double h = Pad + LabelSize * 1.4 + 6 + NumCell * 7 + 8 + WidgetCell * 4 + Pad;
                // as wide as Graph length asks (never what the widget offers: a widget that sizes itself to its content offers its
                // whole room, and the tile took all of it - 0.38.226); a wider widget still stretches it (HorizontalAlignment)
                double w = Math.Max(_graphW, 120) + Pad * 2;
                if (!double.IsInfinity(available.Width)) w = Math.Min(w, available.Width);
                return new Size(w, Math.Max(h, HostH));
            }
            double width = 0;
            if (_showLabel) width += Spaced(_label, LabelSize).Width + 8;
            width += Columns(_graphW, BarCell) * BarCell + 8 + NumberWidth(NumberSample());
            return new Size(Math.Ceiling(width) + 2, Math.Max(BarCell * 4, NumCell * 7));
        }

        /// <summary>Room for "100%" (or the number shown, if longer), so the module doesn't jiggle as the number changes.</summary>
        string NumberSample() { return _number.Length > 3 ? new string('8', _number.Length) : "100"; }

        static int Columns(double width, double cell) { return Math.Max(4, (int)Math.Floor(width / cell)); }

        protected override void OnRender(DrawingContext dc)
        {
            if (_t == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));      // the whole module takes clicks / the tooltip
            if (_widget) RenderTile(dc, w, h); else RenderRow(dc, w, h);
        }

        void RenderRow(DrawingContext dc, double w, double h)
        {
            double x = 0;
            if (_showLabel && _label.Length > 0)
            {
                var lab = Spaced(_label, LabelSize);
                DrawSpaced(dc, _label, LabelSize, new Point(x, (h - lab.Height) / 2), _t.FgDim);
                x += lab.Width + 8;
            }
            double cell = BarCell;
            int cols = Columns(_graphW, cell);
            DrawField(dc, x, (h - cell * 4) / 2, cols, 4, cell);
            x += cols * cell + 8;
            double nc = NumCell;
            DrawNumber(dc, x, (h - nc * 7) / 2, nc, false);
        }

        void RenderTile(DrawingContext dc, double w, double h)
        {
            double y = Pad;
            if (_showLabel)
            {
                var lab = Spaced(_label, LabelSize);
                DrawSpaced(dc, _label, LabelSize, new Point(Pad, y), _t.FgDim);
                if (!string.IsNullOrEmpty(_detail))
                {
                    var det = Text(_detail, LabelSize, FontWeights.Normal, _t.FgDim);
                    double room = w - Pad * 2 - lab.Width - 10;
                    if (room > 20)
                    {
                        det.MaxTextWidth = room;
                        det.Trimming = TextTrimming.CharacterEllipsis;
                        det.MaxLineCount = 1;
                        dc.DrawText(det, new Point(w - Pad - Math.Min(room, det.WidthIncludingTrailingWhitespace), y));
                    }
                }
                y += lab.Height + 6;
            }
            double nc = NumCell;
            DrawNumber(dc, Pad, y, nc, true);
            y += nc * 7 + 8;
            // the graph fills the rest: as many rows as fit (at least 3), as many columns as the width takes
            double cell = WidgetCell;
            int rows = Math.Max(3, Math.Min(12, (int)Math.Floor((h - Pad - y) / cell)));
            int cols = Columns(w - Pad * 2, cell);
            double left = Pad + ((w - Pad * 2) - cols * cell) / 2;
            DrawField(dc, left, h - Pad - rows * cell, cols, rows, cell);
        }

        /// <summary>The dot graph: one column per bar of history (newest on the right, in the graph colour), on a field of dim dots.</summary>
        void DrawField(DrawingContext dc, double x0, double y0, int cols, int rows, double cell)
        {
            double d = cell * 0.78, pad = (cell - d) / 2, r = d / 2;
            int bucket = MetricView.BarBucket(cols, _span);
            int count = _data != null ? _data.Count : 0;
            Brush newest = _warn ? _warnFill : _accent;
            for (int c = 0; c < cols; c++)
            {
                int end = count - (cols - 1 - c) * bucket;
                double v = -1;
                if (end > 0)
                {
                    int from = Math.Max(0, end - bucket), to = Math.Min(count, end);
                    if (to > from) { double sum = 0; for (int i = from; i < to; i++) sum += _data[i]; v = sum / (to - from); }
                }
                int tall = v < 0 ? 0 : Math.Max(1, (int)Math.Round(Math.Max(0, Math.Min(100, v)) / 100.0 * rows));
                for (int row = 0; row < rows; row++)
                {
                    bool lit = rows - row <= tall;
                    Brush b = !lit ? _off
                            : c == cols - 1 ? newest
                            : _load != null ? LoadBrush((rows - row) * 100.0 / rows)
                            : (_warnAt >= 0 && Crossed(v)) ? _warnFill
                            : _on;
                    dc.DrawEllipse(b, null, new Point(x0 + c * cell + pad + r, y0 + row * cell + pad + r), r, r);
                }
            }
        }

        bool Crossed(double v) { return _warnBelow ? v <= _warnAt : v >= _warnAt; }

        Brush LoadBrush(double pc)
        {
            double v = Math.Max(0, Math.Min(100, pc)) / 50.0;
            Color c = v <= 1 ? Mix(_load[0], _load[1], v) : Mix(_load[1], _load[2], v - 1);
            return Frozen(c);
        }

        static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
                                  (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        // ---- the number ----

        /// <summary>Numbers in dots, and this one can be drawn so ("n/a" can't - it's written in the font).</summary>
        bool InDots(string number) { return _dotNumbers && DotText.CanDraw(number + _unit); }

        double NumberWidth(string number)
        {
            double nc = NumCell;
            if (!InDots(number)) return Text(number + _unit, NumCell * 7 * 1.25, FontWeights.SemiBold, _t.Fg).WidthIncludingTrailingWhitespace;
            return DotText.Width(number, nc) + (_unit.Length > 0 ? nc * 1.5 + DotText.Width(_unit, UnitCell(nc)) : 0);
        }

        void DrawNumber(DrawingContext dc, double x, double y, double nc, bool big)
        {
            Brush ink = _warn ? _warnFill : _on;
            if (!InDots(_number))
            {
                // the theme's font, the number about as tall as the dots would be (the unit smaller and dim)
                var num = Text(_number, nc * 7 * 1.25, FontWeights.SemiBold, ink);
                double baseY = y + nc * 7 - num.Baseline;
                dc.DrawText(num, new Point(x, baseY));
                if (_unit.Length > 0)
                {
                    var u = Text(_unit, nc * 7 * 0.7, FontWeights.Normal, _t.FgDim);
                    dc.DrawText(u, new Point(x + num.WidthIncludingTrailingWhitespace + 1, y + nc * 7 - u.Baseline));
                }
                return;
            }
            double right = DotText.Draw(dc, _number, x, y, nc, ink);
            if (_unit.Length > 0)
            {
                // the unit in dim dots: in a widget smaller, on the number's top line; in a bar as big as the number (smaller can't be read)
                DotText.Draw(dc, _unit, right + nc * 1.5, y, UnitCell(nc), _t.FgDim);
            }
        }

        double UnitCell(double nc) { return _widget ? Math.Max(1.5, nc * 0.6) : nc; }

        // ---- text ----

        FormattedText Text(string s, double size, FontWeight weight, Brush b)
        {
            return new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                     new Typeface(_t.Font, FontStyles.Normal, weight, FontStretches.Normal), Math.Max(6, size), b,
                                     VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        /// <summary>The label in capitals with a little space between the letters (its size).</summary>
        Size Spaced(string s, double size)
        {
            s = (s ?? "").ToUpper(CultureInfo.CurrentCulture);
            double w = 0, h = 0, gap = size * 0.12;
            foreach (char ch in s)
            {
                var f = Text(ch.ToString(), size, FontWeights.Normal, _t.FgDim);
                w += f.WidthIncludingTrailingWhitespace + gap;
                h = Math.Max(h, f.Height);
            }
            return new Size(Math.Max(0, w - gap), h);
        }

        void DrawSpaced(DrawingContext dc, string s, double size, Point at, Brush b)
        {
            s = (s ?? "").ToUpper(CultureInfo.CurrentCulture);
            double x = at.X, gap = size * 0.12;
            foreach (char ch in s)
            {
                var f = Text(ch.ToString(), size, FontWeights.Normal, b);
                dc.DrawText(f, new Point(x, at.Y));
                x += f.WidthIncludingTrailingWhitespace + gap;
            }
        }

        static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    /// <summary>Numbers drawn in dots: 5 x 7 shapes for the digits and the few signs and unit letters readings use.</summary>
    internal static class DotText
    {
        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
            { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
            { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
            { '3', new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." } },
            { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
            { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
            { '6', new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." } },
            { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
            { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
            { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." } },
            { '%', new[] { "##...", "##..#", "...#.", "..#..", ".#...", "#..##", "...##" } },
            { '.', new[] { "..", "..", "..", "..", "..", "##", "##" } },
            { ',', new[] { "..", "..", "..", "..", "##", ".#", "#." } },
            { '°', new[] { ".##.", "#..#", "#..#", ".##.", "....", "....", "...." } },
            { '-', new[] { "....", "....", "....", "####", "....", "....", "...." } },
            { '/', new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." } },
            { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
            { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
            { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" } },
            { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
            { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
            { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
            { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
            { ' ', new[] { "...", "...", "...", "...", "...", "...", "..." } },
        };

        /// <summary>Whether every character has a dot shape (letters are taken as capitals).</summary>
        public static bool CanDraw(string s)
        {
            foreach (char ch in s ?? "") if (!Glyphs.ContainsKey(char.ToUpperInvariant(ch))) return false;
            return true;
        }

        static string[] Of(char ch) { string[] g; return Glyphs.TryGetValue(char.ToUpperInvariant(ch), out g) ? g : Glyphs[' ']; }

        /// <summary>The width at this dot pitch (one empty column between characters).</summary>
        public static double Width(string s, double cell)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double w = 0;
            foreach (char ch in s) w += (Of(ch)[0].Length + 1) * cell;
            return w - cell;
        }

        /// <summary>Draws the text with its top-left at x, y; returns the right edge.</summary>
        public static double Draw(DrawingContext dc, string s, double x, double y, double cell, Brush b)
        {
            if (string.IsNullOrEmpty(s)) return x;
            double d = cell * 0.84, r = d / 2, pad = (cell - d) / 2;
            foreach (char ch in s)
            {
                string[] g = Of(ch);
                int w = g[0].Length;
                for (int row = 0; row < 7; row++)
                    for (int c = 0; c < w; c++)
                        if (g[row][c] == '#') dc.DrawEllipse(b, null, new Point(x + c * cell + pad + r, y + row * cell + pad + r), r, r);
                x += (w + 1) * cell;
            }
            return x - cell;
        }
    }
}
