using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The "Dots" style (CPU 0.38.226; memory, GPU, disk, network, battery, HWiNFO sensors 0.38.227): the readings as columns of dots on a
    /// dim dot field, the newest column in the graph colour, the number drawn in 5 x 7 dots (or in the theme's font - "Numbers in dots"
    /// off) and a small grey upper-case label. Two series (network) mirror round a middle line: download up, upload (its own colour)
    /// down. A meter (disk space) fills the field from the left instead of keeping a history.
    /// In a bar: label, dot graph and number in one row. As a tile (a widget, for modules that can be taller than a row): label (and
    /// the detail on the right), the big number, and the dot graph filling the rest of the module.
    /// </summary>
    /// <remarks>
    /// Plugin API since vShell 0.38.227: a plugin's module can show itself the same way - new DotFace(history[, history2]), Setup from its
    /// ModuleConfig / host / theme (again when they change), Show with each reading. Only the graph (no label or number): MetricView with
    /// GraphStyle "Dots". Numbers alone: DotText.
    /// </remarks>
    [PluginApi]
    public sealed class DotFace : FrameworkElement
    {
        readonly History _data, _data2;
        Theme _t;
        IModuleHost _host;
        Brush _accent, _accent2, _warnFill, _on, _on2, _off;
        Color[] _load;
        bool _tile, _meter, _dotNumbers = true, _showLabel = true;
        double _graphW = 60, _heightPercent = 60;
        int _span = 60;
        double _warnAt = -1;
        bool _warnBelow;
        double _fullScale;
        double _keepNumW, _keepExtraW;
        Brush _tempBrush;

        string _label = "", _number = "", _unit = "", _detail, _temp;
        double _percent, _percent2 = -1;
        bool _warn;

        public DotFace(History data, History data2 = null) { _data = data; _data2 = data2; }

        /// <summary>
        /// The look, from the module's settings and theme (again whenever they change). tile = the widget layout (only for modules that can
        /// be taller than one row); warnPercent = the warning level in graph percent (NaN = the module's WarnAt as a percent);
        /// meter = fill the field with the value now instead of a history (disk space).
        /// </summary>
        public void Setup(ModuleConfig cfg, IModuleHost host, Theme t, bool tile, double warnPercent = double.NaN, bool meter = false)
        {
            _t = t; _host = host; _tile = tile; _meter = meter;
            _accent = t.Resolve(cfg.Color, t.MainAccent);
            _accent2 = t.Resolve(cfg.Color2, t.SecondaryAccent);
            _warnFill = t.Warn(cfg.WarnColor);
            _warnAt = double.IsNaN(warnPercent) ? (cfg.WarnOn ? Math.Max(0, Math.Min(100, cfg.WarnAt)) : -1) : warnPercent;
            _warnBelow = cfg.WarnBelow;
            _on = t.Fg;
            Color fg = t.Fg.Color;
            _off = Frozen(Color.FromArgb(38, fg.R, fg.G, fg.B));
            _on2 = t.FgDim;
            _dotNumbers = cfg.Bag.Get("dots.numbers", true);
            _showLabel = cfg.ShowText;
            _graphW = Math.Max(20, cfg.ShownGraphWidth);
            _heightPercent = Math.Max(20, Math.Min(100, cfg.GraphHeightPercent));
            _span = Math.Max(60, Math.Min(History.Capacity, cfg.GraphSpan));
            _keepNumW = 0;
            _keepExtraW = 0;
            _tempBrush = t.SecondaryAccent;
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
            HorizontalAlignment = _tile ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            VerticalAlignment = _tile ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>Above 0: the samples are amounts (MB/s …) and this fills the graph (MetricView.ScaleFor); 0 = they are percent.</summary>
        public double FullScale { set { _fullScale = value; } }

        /// <summary>
        /// What it shows now: the label ("CPU"), the number ("37") and unit ("%"), an optional detail ("4.2 GHz"), warned or not, and the
        /// value(s) now in graph percent (a meter fills with percent; percent2 = the second series, below 0 = none).
        /// </summary>
        public void Show(string label, string number, string unit, string detail, bool warn, double percent = 0, double percent2 = -1)
        {
            bool resize = (number ?? "").Length != _number.Length || (unit ?? "") != _unit || (detail ?? "").Length != (_detail ?? "").Length || label != _label ||
                          (percent2 >= 0) != (_percent2 >= 0);     // a second series makes the bar's graph taller
            _label = label ?? ""; _number = number ?? ""; _unit = unit ?? ""; _detail = detail; _warn = warn;
            _percent = percent; _percent2 = percent2;
            if (resize && !_tile) InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>
        /// A temperature (or another reading) shown after the detail in the Secondary Accent colour, as the other styles show it; null = none.
        /// Set it before Show. (0.38.228)
        /// </summary>
        public string Temp
        {
            set
            {
                if (value == _temp) return;
                _temp = value;
                if (!_tile) InvalidateMeasure();
                InvalidateVisual();
            }
        }

        bool Two { get { return !_meter && _data2 != null && _data2.Count > 0 && _percent2 >= 0; } }

        // ---- the detail and temperature: after the number in a bar, at the top right in a tile ----

        bool HasExtra { get { return !string.IsNullOrEmpty(_detail) || !string.IsNullOrEmpty(_temp); } }
        double ExtraSize { get { return _tile ? LabelSize : Math.Max(7, Math.Min(Fs * 0.85, HostH * 0.42)); } }

        /// <summary>The detail (dim) and " · " + the temperature (Secondary Accent) as text runs.</summary>
        List<FormattedText> ExtraParts()
        {
            var parts = new List<FormattedText>();
            double size = ExtraSize;
            if (!string.IsNullOrEmpty(_detail)) parts.Add(Text(_detail, size, FontWeights.Normal, _t.FgDim));
            if (!string.IsNullOrEmpty(_temp))
            {
                if (parts.Count > 0) parts.Add(Text(" · ", size, FontWeights.Normal, _t.FgDim));
                parts.Add(Text(_temp, size, FontWeights.Normal, _tempBrush ?? _t.FgDim));
            }
            return parts;
        }

        double ExtraWidth()
        {
            double w = 0;
            foreach (FormattedText f in ExtraParts()) w += f.WidthIncludingTrailingWhitespace;
            return w;
        }

        /// <summary>Draws the parts from x, their text centred on midY; returns the right edge.</summary>
        double DrawExtra(DrawingContext dc, double x, double midY)
        {
            foreach (FormattedText f in ExtraParts())
            {
                dc.DrawText(f, new Point(x, midY - f.Height / 2));
                x += f.WidthIncludingTrailingWhitespace;
            }
            return x;
        }

        // ---- sizes (all from the theme's font size, so a bigger font makes bigger dots) ----

        double Fs { get { return _t != null ? _t.FontSize : 12; } }
        double HostH { get { return _host != null ? Math.Max(16, _host.HostHeight) : 24; } }
        /// <summary>The number's dot pitch: in a bar about the height of the text; in a tile about 2.2 x the font size tall.</summary>
        double NumCell { get { return _tile ? Math.Max(2, Math.Round(Fs * 0.32 * 2) / 2) : Math.Max(1.4, Math.Min(Fs * 0.16, (HostH - 6) / 7.0)); } }
        double LabelSize { get { return _tile ? Math.Max(7, Fs * 0.8) : Math.Max(7, Math.Min(Fs * 0.72, HostH * 0.36)); } }
        /// <summary>The bar's graph rows: 4 (6 with two series, 3 each side of the middle), filling the graph height.</summary>
        int BarRows { get { return Two ? 6 : 4; } }
        double BarCell { get { return Math.Max(2, HostH * _heightPercent / 100.0 / BarRows); } }
        double TileCell { get { return Math.Max(4, Math.Round(Fs * 0.6 * 2) / 2); } }
        /// <summary>A tile's dot rows from Graph height: 100 % = 12 rows, 60 % (the default) = 7, at least 3 - an even number for two series;
        /// a meter (disk space) at most 4, a band. (Before 0.38.228 a tile took as many rows as fitted and Graph height did nothing.)</summary>
        int TileRows
        {
            get
            {
                int rows = Math.Max(3, Math.Min(12, (int)Math.Round(_heightPercent / 100.0 * 12)));
                if (Two) rows = Math.Max(4, rows + rows % 2);
                if (_meter) rows = Math.Min(rows, 4);
                return rows;
            }
        }
        const double Pad = 8;

        protected override Size MeasureOverride(Size available)
        {
            if (_t == null) return new Size(0, 0);
            if (_tile)
            {
                // label line, the number, then at least 4 rows of dots; as tall as the module row asks for at least. As wide as Graph length
                // asks - never what the widget offers (a widget that sizes itself to its content offers its whole room); a wider widget
                // still stretches it (HorizontalAlignment)
                double h = Pad + LabelSize * 1.4 + 6 + NumCell * 7 + 8 + TileCell * TileRows + Pad;
                double w = Math.Max(_graphW, 120) + Pad * 2;
                if (!double.IsInfinity(available.Width)) w = Math.Min(w, available.Width);
                // the detail / temperature on a line of its own when it doesn't fit beside the label
                if (HasExtra && ExtraWidth() > w - Pad * 2 - (_showLabel ? Spaced(_label, LabelSize).Width + 10 : 0)) h += LabelSize * 1.2 + 2;
                return new Size(w, Math.Max(h, HostH));
            }
            double width = 0;
            if (_showLabel && _label.Length > 0) width += Spaced(_label, LabelSize).Width + 8;
            // the number's room never shrinks while the look stays the same, so the module doesn't jiggle as the number changes
            _keepNumW = Math.Max(_keepNumW, Math.Max(NumberWidth(_number), NumberWidth(_number.Length >= 3 ? new string('8', _number.Length) : "100")));
            width += Columns(_graphW, BarCell) * BarCell + 8 + _keepNumW;
            // the detail / temperature after the number (its room never shrinks either, while there is one)
            _keepExtraW = HasExtra ? Math.Max(_keepExtraW, ExtraWidth()) : 0;
            if (_keepExtraW > 0) width += 8 + _keepExtraW;
            return new Size(Math.Ceiling(width) + 2, Math.Max(BarCell * BarRows, NumCell * 7));
        }

        static int Columns(double width, double cell) { return Math.Max(4, (int)Math.Floor(width / cell)); }

        protected override void OnRender(DrawingContext dc)
        {
            if (_t == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));      // the whole module takes clicks / the tooltip
            if (_tile) RenderTile(dc, w, h); else RenderRow(dc, w, h);
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
            int cols = Columns(_graphW, cell), rows = BarRows;
            DrawField(dc, x, (h - cell * rows) / 2, cols, rows, cell);
            x += cols * cell + 8;
            double nc = NumCell;
            DrawNumber(dc, x, (h - nc * 7) / 2, nc);
            if (HasExtra) DrawExtra(dc, x + Math.Max(_keepNumW, NumberWidth(_number)) + 8, h / 2);
        }

        void RenderTile(DrawingContext dc, double w, double h)
        {
            double y = Pad;
            if (_showLabel || HasExtra)
            {
                Size lab = _showLabel ? Spaced(_label, LabelSize) : new Size(0, LabelSize * 1.2);
                if (_showLabel) DrawSpaced(dc, _label, LabelSize, new Point(Pad, y), _t.FgDim);
                double lineH = Math.Max(lab.Height, LabelSize * 1.2);
                if (HasExtra)
                {
                    // the detail and temperature on the label's line, at the right; when they don't fit, under the label on a line of their own
                    double room = w - Pad * 2 - (_showLabel ? lab.Width + 10 : 0), ew = ExtraWidth();
                    if (ew <= room) DrawExtra(dc, w - Pad - ew, y + lineH / 2);
                    else
                    {
                        if (_showLabel) y += lineH + 2;
                        dc.PushClip(new RectangleGeometry(new Rect(Pad, y, Math.Max(0, w - Pad * 2), lineH)));
                        DrawExtra(dc, Pad, y + lineH / 2);
                        dc.Pop();
                    }
                }
                y += lineH + 6;
            }
            double nc = NumCell;
            DrawNumber(dc, Pad, y, nc);
            y += nc * 7 + 8;
            // the graph along the bottom: Graph height's rows (fewer only if the tile is squeezed), as many columns as the width takes
            double cell = TileCell;
            int rows = TileRows;
            int fit = (int)Math.Floor((h - Pad - y) / cell);
            if (fit < rows) rows = Math.Max(Two ? 2 : 1, Two ? fit - fit % 2 : fit);
            int cols = Columns(w - Pad * 2, cell);
            double left = Pad + ((w - Pad * 2) - cols * cell) / 2;
            DrawField(dc, left, h - Pad - rows * cell, cols, rows, cell);
        }

        /// <summary>The value at the end of a column of history (the average of its seconds), in graph percent; below 0 = no samples yet.</summary>
        double ColumnValue(History data, int c, int cols, int bucket)
        {
            int count = data != null ? data.Count : 0;
            int end = count - (cols - 1 - c) * bucket;
            if (end <= 0) return -1;
            int from = Math.Max(0, end - bucket), to = Math.Min(count, end);
            if (to <= from) return -1;
            double sum = 0;
            for (int i = from; i < to; i++) sum += data[i];
            double v = sum / (to - from);
            if (_fullScale > 0) v = v * 100.0 / _fullScale;
            return Math.Max(0, Math.Min(100, v));
        }

        static int Tall(double v, int rows) { return v < 0 ? 0 : Math.Max(1, (int)Math.Round(v / 100.0 * rows)); }

        /// <summary>The dot field: a history (one column per bar, newest on the right in the graph colour), two mirrored, or a meter.</summary>
        void DrawField(DrawingContext dc, double x0, double y0, int cols, int rows, double cell)
        {
            double d = cell * 0.78, pad = (cell - d) / 2, r = d / 2;
            Func<int, int, Brush, bool> dot = delegate (int c, int row, Brush b)
            {
                dc.DrawEllipse(b, null, new Point(x0 + c * cell + pad + r, y0 + row * cell + pad + r), r, r);
                return true;
            };
            Brush newest = _warn ? _warnFill : _accent;

            if (_meter)
            {
                // fills column by column from the left, the last lit dot in the graph colour
                int total = cols * rows, lit = (int)Math.Round(Math.Max(0, Math.Min(100, _percent)) / 100.0 * total);
                if (_percent > 0 && lit == 0) lit = 1;
                for (int c = 0; c < cols; c++)
                    for (int row = 0; row < rows; row++)
                    {
                        int i = c * rows + row;
                        // colour by load: each dot the colour of its place along the fill (low on the left, high on the right)
                        dot(c, row, i >= lit ? _off : _warn ? newest : _load != null ? LoadBrush((c + 1) * 100.0 / cols) : i == lit - 1 ? newest : _on);
                    }
                return;
            }

            int bucket = MetricView.BarBucket(cols, _span);
            if (Two)
            {
                // download up from the middle, upload down from it (in its own colour, dimmer while it isn't the newest)
                int half = rows / 2;
                for (int c = 0; c < cols; c++)
                {
                    int up = Tall(ColumnValue(_data, c, cols, bucket), half), down = Tall(ColumnValue(_data2, c, cols, bucket), half);
                    bool last = c == cols - 1;
                    for (int row = 0; row < half; row++) dot(c, row, half - row <= up ? (last ? newest : _on) : _off);
                    for (int row = 0; row < half; row++) dot(c, half + row, row < down ? (last ? _accent2 : _on2) : _off);
                }
                return;
            }

            for (int c = 0; c < cols; c++)
            {
                double v = ColumnValue(_data, c, cols, bucket);
                int tall = Tall(v, rows);
                for (int row = 0; row < rows; row++)
                {
                    bool lit = rows - row <= tall;
                    // colour by load: every lit dot (the newest column too) the colour of its height; past the warning the newest is still the warning colour
                    Brush b = !lit ? _off
                            : c == cols - 1 && (_load == null || _warn) ? newest
                            : _load != null ? LoadBrush((rows - row) * 100.0 / rows)
                            : (_warnAt >= 0 && Crossed(v)) ? _warnFill
                            : _on;
                    dot(c, row, b);
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
        // The number in dots when it can be ("n/a" can't); the unit in dots only when it's a sign or two ("%", "°C") - a unit of letters
        // ("MB/s", "RPM", "W") stays in the font, small and dim, so it reads as a word.

        bool NumberInDots(string number) { return _dotNumbers && DotText.CanDraw(number); }
        bool UnitInDots { get { return _dotNumbers && _unit.Length > 0 && _unit.Length <= 2 && DotText.CanDraw(_unit); } }
        double UnitCell(double nc) { return _tile ? Math.Max(1.5, nc * 0.6) : nc; }
        /// <summary>Space between the number and its unit; "%" and "°" sit right against it, a word after a gap.</summary>
        double UnitGap(double nc) { return _unit.Length > 0 && (_unit[0] == '%' || _unit[0] == '°') ? nc * 1.5 : nc * 3; }

        FormattedText NumberText(string number, double nc, Brush b) { return Text(number, nc * 7 * 1.25, FontWeights.SemiBold, b); }
        FormattedText UnitText(double nc) { return Text(_unit, Math.Max(7, nc * 7 * (_tile ? 0.6 : 0.8)), FontWeights.Normal, _t.FgDim); }

        double NumberWidth(string number)
        {
            double nc = NumCell;
            double w = NumberInDots(number) ? DotText.Width(number, nc) : NumberText(number, nc, _on).WidthIncludingTrailingWhitespace;
            if (_unit.Length == 0) return w;
            return w + UnitGap(nc) + (UnitInDots ? DotText.Width(_unit, UnitCell(nc)) : UnitText(nc).WidthIncludingTrailingWhitespace);
        }

        void DrawNumber(DrawingContext dc, double x, double y, double nc)
        {
            Brush ink = _warn ? _warnFill : _on;
            double bottom = y + nc * 7;
            double right;
            if (NumberInDots(_number)) right = DotText.Draw(dc, _number, x, y, nc, ink);
            else
            {
                // the theme's font, the number about as tall as the dots would be
                var num = NumberText(_number, nc, ink);
                dc.DrawText(num, new Point(x, bottom - num.Baseline));
                right = x + num.WidthIncludingTrailingWhitespace;
            }
            if (_unit.Length == 0) return;
            double ux = right + UnitGap(nc);
            if (UnitInDots) DotText.Draw(dc, _unit, ux, y, UnitCell(nc), _t.FgDim);       // a tile: smaller, on the number's top line
            else
            {
                var u = UnitText(nc);
                dc.DrawText(u, new Point(ux, bottom - u.Baseline));                       // on the number's baseline
            }
        }

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

    /// <summary>Numbers drawn in dots: 5 x 7 shapes for the digits and the few signs and unit letters readings use (plugin API since 0.38.227).</summary>
    [PluginApi]
    public static class DotText
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
            { '+', new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." } },
            { '/', new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." } },
            { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
            { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
            { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" } },
            { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
            { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
            { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
            { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
            { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" } },
            { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
            { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
            { ' ', new[] { "...", "...", "...", "...", "...", "...", "..." } },
        };

        /// <summary>Whether every character has a dot shape (letters are taken as capitals).</summary>
        public static bool CanDraw(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char ch in s) if (!Glyphs.ContainsKey(char.ToUpperInvariant(ch))) return false;
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
