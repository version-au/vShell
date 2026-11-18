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
        double _keepNumW, _keepExtraW, _keepColW, _keepColW2, _keepAfterW;
        Brush _tempBrush;
        /// <summary>Where a bar's row puts the details: ModuleTypes.DotDetails (cfg.Bag "dots.details").</summary>
        string _detailMode = "Stacked";

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
            _heightPercent = Math.Max(20, Math.Min(100, cfg.ShownGraphHeightPercent));
            _span = Math.Max(60, Math.Min(History.Capacity, cfg.GraphSpan));
            _keepNumW = 0;
            _keepExtraW = 0; _keepColW = 0; _keepColW2 = 0; _keepAfterW = 0;
            _detailMode = cfg.Bag.Get("dots.details", "Stacked");
            if (Array.IndexOf(ModuleTypes.DotDetailsFor(cfg.Type), _detailMode) < 0) _detailMode = "Stacked";     // (Temperature in dots: only where there's one)
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
            // the number's room never shrinks while the look stays the same, so the module doesn't jiggle as the number changes
            _keepNumW = Math.Max(_keepNumW, Math.Max(NumberWidth(_number), NumberWidth(_number.Length >= 3 ? new string('8', _number.Length) : "100")));
            // as tall as the bar (less a hair), so two lines of details have room; the dots stay centred in it
            double barH = Math.Max(Math.Max(BarCell * BarRows, NumCell * 7), HostH - 4);
            if (!double.IsInfinity(available.Height)) barH = Math.Min(barH, Math.Max(available.Height, Math.Max(BarCell * BarRows, NumCell * 7)));
            double width = Row(null, barH);
            return new Size(Math.Ceiling(width) + 2, barH);
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

        void RenderRow(DrawingContext dc, double w, double h) { Row(dc, h); }

        // ---- a bar's row: where the details go ("Details in a bar", 0.38.229 - the one line it had made the module long) ----
        //   Stacked            the clock speed over the temperature, small, after the number (one detail: one line)
        //   Under the label    the temperature (else the detail) small under the label; the rest in the tooltip
        //   Temperature in dots the temperature as a second dot number in the Secondary Accent colour; the detail in the tooltip
        //                      (no temperature: the detail stacked)
        //   Taking turns       the number, the detail and the temperature in turn every 3 s, small dots under it saying which
        //   Under the graph    a flatter graph (4 rows, as the Fans plugin's) with the details in small type under it

        double SmallSize(double h) { return Math.Max(7, Math.Min(Fs * 0.75, (h - 3) / 2.3)); }
        static bool Has(string s) { return !string.IsNullOrEmpty(s); }

        /// <summary>The temperature as a dot number: "61°C" -> "61°" (the scale letter dropped, as on a thermometer's dial).</summary>
        static string TempDots(string t)
        {
            t = (t ?? "").Trim();
            int deg = t.IndexOf('°');
            return deg > 0 ? t.Substring(0, deg + 1) : t;
        }

        /// <summary>The bar's row: measures (dc null) or draws it in a row h tall; returns its width.</summary>
        double Row(DrawingContext dc, double h)
        {
            bool draw = dc != null;
            string mode = HasExtra ? _detailMode : "";
            string det = Has(_detail) ? _detail : null, tmp = Has(_temp) ? _temp : null;
            double x = 0, small = SmallSize(h);

            // the label - with Under the label, one detail under it
            if (mode == "Under the label")
            {
                string under = tmp ?? det;
                var u = Text(under, small, FontWeights.Normal, tmp != null ? _tempBrush : _t.FgDim);
                Size lab = _showLabel && _label.Length > 0 ? Spaced(_label, LabelSize) : new Size(0, 0);
                _keepColW = Math.Max(_keepColW, Math.Max(lab.Width, u.WidthIncludingTrailingWhitespace));
                if (draw)
                {
                    if (lab.Width > 0)
                    {
                        double top = (h - (lab.Height + u.Height - 1)) / 2;
                        DrawSpaced(dc, _label, LabelSize, new Point(0, top), _t.FgDim);
                        dc.DrawText(u, new Point(0, top + lab.Height - 1));
                    }
                    else dc.DrawText(u, new Point(0, (h - u.Height) / 2));
                }
                x += _keepColW + 8;
            }
            else if (_showLabel && _label.Length > 0)
            {
                var lab = Spaced(_label, LabelSize);
                if (draw) DrawSpaced(dc, _label, LabelSize, new Point(0, (h - lab.Height) / 2), _t.FgDim);
                x += lab.Width + 8;
            }

            // the graph - with Under the graph, flatter, the details under it
            if (mode == "Under the graph")
            {
                var line = Line(det, tmp, small);
                double lineH = small * 1.25;
                int rows = 4;     // as the Fans plugin's automatic graph (4 dots high); two series: 2 each side (0.38.233 - was 3)
                double cell = Math.Max(2, Math.Min(BarCell, (h - lineH - 3) / rows));
                int cols = Columns(_graphW, cell);
                double gw = cols * cell, lw = Width(line);
                double top = (h - (rows * cell + 2 + lineH)) / 2;
                if (draw)
                {
                    DrawField(dc, x, top, cols, rows, cell);
                    DrawLine(dc, line, x, top + rows * cell + 2 + lineH / 2);
                }
                _keepColW2 = Math.Max(_keepColW2, Math.Max(gw, lw));
                x += _keepColW2 + 8;
            }
            else
            {
                double cell = BarCell;
                int cols = Columns(_graphW, cell), rows = BarRows;
                if (draw) DrawField(dc, x, (h - cell * rows) / 2, cols, rows, cell);
                x += cols * cell + 8;
            }

            double nc = NumCell, ny = (h - nc * 7) / 2;

            // Taking turns: one spot for the number, the detail and the temperature
            if (mode == "Taking turns")
            {
                var items = new List<string> { null };      // null = the number itself
                if (det != null) items.Add(det);
                if (tmp != null) items.Add(tmp);
                double room = _keepNumW;
                foreach (string s in items) if (s != null) room = Math.Max(room, TurnWidth(s, nc));
                _keepAfterW = Math.Max(_keepAfterW, room);
                if (draw)
                {
                    int phase = (int)(Environment.TickCount64 / 3000 % items.Count);
                    bool pips = items.Count > 1 && h >= 22;
                    double y = pips ? ny - 2 : ny;
                    string s = items[phase];
                    if (s == null) DrawNumber(dc, x, y, nc);
                    else DrawTurn(dc, s, x, y, nc, s == tmp ? _tempBrush : _on);
                    if (pips)
                    {
                        double pd = Math.Max(2, nc * 1.2);
                        for (int i = 0; i < items.Count; i++)
                            dc.DrawEllipse(i == phase ? _on : _off, null, new Point(x + pd / 2 + i * pd * 2.2, y + nc * 7 + 2 + pd / 2), pd / 2, pd / 2);
                    }
                }
                return x + _keepAfterW;
            }

            if (draw) DrawNumber(dc, x, ny, nc);
            x += Math.Max(_keepNumW, NumberWidth(_number));
            if (mode == "" || mode == "Under the label" || mode == "Under the graph") return x;

            // Temperature in dots: a second dot number (the detail goes to the tooltip); no temperature - the detail as Stacked shows it
            if (mode == "Temperature in dots" && tmp != null)
            {
                string td = TempDots(tmp);
                double tw = _dotNumbers && DotText.CanDraw(td) ? DotText.Width(td, nc) : NumberText(td, nc, _tempBrush).WidthIncludingTrailingWhitespace;
                _keepAfterW = Math.Max(_keepAfterW, tw);
                if (draw)
                {
                    if (_dotNumbers && DotText.CanDraw(td)) DotText.Draw(dc, td, x + nc * 4, ny, nc, _warn ? _warnFill : _tempBrush);
                    else { var f = NumberText(td, nc, _tempBrush); dc.DrawText(f, new Point(x + nc * 4, ny + nc * 7 - f.Baseline)); }
                }
                return x + nc * 4 + _keepAfterW;
            }

            // Stacked: two short lines (one when there's one detail, or the bar is too slim for two)
            bool two = det != null && tmp != null && h >= 24;
            if (two)
            {
                var a = Text(det, small, FontWeights.Normal, _t.FgDim);
                var b = Text(tmp, small, FontWeights.Normal, _tempBrush);
                _keepAfterW = Math.Max(_keepAfterW, Math.Max(a.WidthIncludingTrailingWhitespace, b.WidthIncludingTrailingWhitespace));
                if (draw)
                {
                    double top = (h - (a.Height + b.Height - 2)) / 2;
                    dc.DrawText(a, new Point(x + 7, top));
                    dc.DrawText(b, new Point(x + 7, top + a.Height - 2));
                }
            }
            else
            {
                var line = Line(det, tmp, two || h >= 24 ? ExtraSize : small);
                _keepAfterW = Math.Max(_keepAfterW, Width(line));
                if (draw) DrawLine(dc, line, x + 7, h / 2);
            }
            return x + 7 + _keepAfterW;
        }

        /// <summary>The detail (dim) and " · " + the temperature (Secondary Accent) in one line of text runs.</summary>
        List<FormattedText> Line(string det, string tmp, double size)
        {
            var parts = new List<FormattedText>();
            if (det != null) parts.Add(Text(det, size, FontWeights.Normal, _t.FgDim));
            if (tmp != null)
            {
                if (parts.Count > 0) parts.Add(Text(" · ", size, FontWeights.Normal, _t.FgDim));
                parts.Add(Text(tmp, size, FontWeights.Normal, _tempBrush));
            }
            return parts;
        }

        static double Width(List<FormattedText> parts) { double w = 0; foreach (FormattedText f in parts) w += f.WidthIncludingTrailingWhitespace; return w; }

        static void DrawLine(DrawingContext dc, List<FormattedText> parts, double x, double midY)
        {
            foreach (FormattedText f in parts) { dc.DrawText(f, new Point(x, midY - f.Height / 2)); x += f.WidthIncludingTrailingWhitespace; }
        }

        // A detail taking its turn looks like the number does (0.38.230 - network's upload was text in the font, between dot numbers):
        // its value in dots ("4.2", "↑ 610", "9.9/16.0", "61°C") and a unit of letters after it in the font, small and dim ("GHz", "KB/s",
        // "GB"), as the number's "MB/s". Only what has no dot shapes at all (a Wi-Fi name) stays in the font, as tall as the dots.

        /// <summary>"4.2 GHz" -> ("4.2", "GHz"); "61°C" or "↑ 610" (no letters at the end) -> (itself, null).</summary>
        static void SplitUnit(string s, out string value, out string unit)
        {
            value = s; unit = null;
            int sp = s.LastIndexOf(' ');
            if (sp <= 0 || sp == s.Length - 1) return;
            string tail = s.Substring(sp + 1);
            bool letters = false;
            foreach (char ch in tail) if (char.IsLetter(ch)) letters = true;
            if (!letters) return;
            value = s.Substring(0, sp).TrimEnd();
            unit = tail;
        }

        bool TurnSplit(string s, out string value, out string unit)
        {
            SplitUnit(s, out value, out unit);
            if (!_dotNumbers) return false;
            if (unit != null && DotText.CanDraw(value)) return true;
            unit = null; value = s;
            return DotText.CanDraw(s);
        }

        FormattedText TurnUnit(string unit, double nc) { return Text(unit, Math.Max(7, nc * 7 * 0.8), FontWeights.Normal, _t.FgDim); }

        double TurnWidth(string s, double nc)
        {
            string value, unit;
            if (!TurnSplit(s, out value, out unit)) return Text(s, nc * 7, FontWeights.SemiBold, _on).WidthIncludingTrailingWhitespace;
            return DotText.Width(value, nc) + (unit != null ? nc * 3 + TurnUnit(unit, nc).WidthIncludingTrailingWhitespace : 0);
        }

        void DrawTurn(DrawingContext dc, string s, double x, double y, double nc, Brush b)
        {
            string value, unit;
            if (!TurnSplit(s, out value, out unit))
            {
                var f = Text(s, nc * 7, FontWeights.SemiBold, b);
                dc.DrawText(f, new Point(x, y + nc * 7 - f.Baseline));
                return;
            }
            double right = DotText.Draw(dc, value, x, y, nc, b);
            if (unit == null) return;
            var u = TurnUnit(unit, nc);
            dc.DrawText(u, new Point(right + nc * 3, y + nc * 7 - u.Baseline));
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
            { '↑', new[] { "..#..", ".###.", "#.#.#", "..#..", "..#..", "..#..", "..#.." } },     // ↑ upload (0.38.230)
            { '↓', new[] { "..#..", "..#..", "..#..", "..#..", "#.#.#", ".###.", "..#.." } },     // ↓ download
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
