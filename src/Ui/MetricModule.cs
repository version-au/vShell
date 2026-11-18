using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Draws a usage meter, bar graph or line graph from a History. Only as tall as asked, so the host can centre it in the bar.</summary>
    [PluginApi]
    public sealed class MetricView : FrameworkElement
    {
        public History Data;
        /// <summary>Optional second series (e.g. upload next to download), drawn in Fill2.</summary>
        public History Data2;
        public Brush Fill2 = Brushes.White;
        public double Value2 = -1;     // < 0 = no second series
        public string GraphStyle = "Meter";
        public Brush Fill = Brushes.White;
        public Brush Track = Brushes.Gray;
        public double GraphW = 60;
        public double GraphH = 20;
        public double Value;
        /// <summary>Warning threshold in graph percent (0..100); values past it draw in WarnFill. WarnAt &lt; 0 = off.</summary>
        public double WarnAt = -1;
        public bool WarnBelow;
        public Brush WarnFill;
        /// <summary>
        /// Fade into WarnFill over this many graph percent before the threshold (after it, for WarnBelow); 0 = the old switch, where the
        /// whole graph turns WarnFill once the value is past it. With a fade the colour follows the position: a meter is the accent up to
        /// the fade, blends through it and is WarnFill past the threshold; bar and line graphs the same from the bottom up; a ring takes
        /// the blended colour of its value.
        /// </summary>
        public double Blend;
        /// <summary>Ring: the text in the middle (e.g. "34%"), its colour (past the warning: WarnFill), font and size.</summary>
        public string Center;
        public Brush CenterBrush;
        public Typeface Face = new Typeface("Segoe UI");
        public double CenterSize = 11;
        /// <summary>Line: no track box behind it (a sparkline in a tile).</summary>
        public bool NoTrack;
        /// <summary>Cores: each thread's load 0..100 (a small bar each); empty = the meter instead.</summary>
        internal double[] Cores = new double[0];
        /// <summary>
        /// 0 = Data / Data2 / Value / Value2 are graph percent (0..100). Above 0 they are amounts (MB/s, Mbit/s …) and this amount fills
        /// the graph - ScaleFor works one out from the samples. The warning threshold is in graph percent either way.
        /// </summary>
        public double FullScale;

        const int LineSamples = 60;

        // ---- the look (ApplyLook, from a module's Graph settings; the defaults are the look from before these existed) ----
        internal double LineWidth = 1.5;
        /// <summary>Under a line: None / Light / Solid / Fade.</summary>
        internal string AreaFill = "Light";
        internal bool SmoothLine;
        /// <summary>Line / bars: the box behind the graph (NoTrack, for a tile's sparkline, wins).</summary>
        internal bool ShowBack = true;
        internal double Radius = 3;
        internal bool Grid;
        internal Brush GridBrush;
        /// <summary>Colour by load: low / middle / high (null = off - the graph and warning colours as usual).</summary>
        internal Color[] LoadColors;
        /// <summary>Seconds of history a line / bar graph shows (a sample a second).</summary>
        internal int Span = LineSamples;

        /// <summary>
        /// The module's Graph look settings (SettingsPage.GraphLook): line thickness, fill, curves, box, grid, colour by load and time shown.
        /// Call it after setting the graph up (Fill, Track …), and again when the theme changes. Since vShell 0.38.139.
        /// </summary>
        public void ApplyLook(ModuleConfig cfg, Theme t)
        {
            LineWidth = ModuleTypes.LineWidthOf(cfg.GraphLine);
            AreaFill = cfg.GraphFill ?? "Light";
            SmoothLine = cfg.GraphSmooth;
            ShowBack = cfg.GraphBack;
            Radius = Math.Max(0, cfg.GraphRadius);
            Grid = cfg.GraphGrid;
            Color fg = t.Fg.Color;
            var gb = new SolidColorBrush(Color.FromArgb(46, fg.R, fg.G, fg.B));
            gb.Freeze();
            GridBrush = gb;
            Span = Math.Max(LineSamples, Math.Min(History.Capacity, cfg.GraphSpan));
            if (cfg.LoadColors && ModuleTypes.HasLoadColors(cfg.Type))
            {
                Color main = t.MainAccent.Color;
                LoadColors = new[]
                {
                    string.IsNullOrWhiteSpace(cfg.LoadColor1) ? Color.FromArgb(0x66, main.R, main.G, main.B) : t.Resolve(cfg.LoadColor1, t.MainAccent).Color,
                    string.IsNullOrWhiteSpace(cfg.LoadColor2) ? Color.FromArgb(0xCC, main.R, main.G, main.B) : t.Resolve(cfg.LoadColor2, t.MainAccent).Color,
                    string.IsNullOrWhiteSpace(cfg.LoadColor3) ? t.SecondaryAccent.Color : t.Resolve(cfg.LoadColor3, t.SecondaryAccent).Color
                };
            }
            else LoadColors = null;
        }

        /// <summary>Colour by load: the colour at a graph percent (0 = low, 50 = middle, 100 = high).</summary>
        Color LoadColorAt(double pc)
        {
            double v = Clamp100(pc) / 50.0;
            return v <= 1 ? Mix(LoadColors[0], LoadColors[1], v) : Mix(LoadColors[1], LoadColors[2], v - 1);
        }

        SolidColorBrush LoadBrushAt(double pc)
        {
            var b = new SolidColorBrush(LoadColorAt(pc));
            b.Freeze();
            return b;
        }

        /// <summary>Colour by load along the graph: low at zero, high at full (a meter left to right, bars and lines bottom to top).</summary>
        Brush LoadRange(Point zero, Point full)
        {
            var g = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = zero, EndPoint = full };
            g.GradientStops.Add(new GradientStop(LoadColors[0], 0));
            g.GradientStops.Add(new GradientStop(LoadColors[1], 0.5));
            g.GradientStops.Add(new GradientStop(LoadColors[2], 1));
            g.Freeze();
            return g;
        }

        /// <summary>Bars: how many seconds each bar is - one at 1 minute (as before: as many seconds as fit), more for longer times.</summary>
        internal static int BarBucket(int bars, int span)
        {
            return span <= LineSamples ? 1 : Math.Max(1, (int)Math.Ceiling(span / (double)Math.Max(1, bars)));
        }

        /// <summary>Lines: how many seconds each point is - 60 points for a minute, at most about 90 for longer times.</summary>
        static int LineBucket(int span) { return Math.Max(1, (int)Math.Ceiling(span / 90.0)); }

        /// <summary>How many of the newest samples a graph shows (for the automatic scale).</summary>
        internal static int SamplesShown(string style, double width, bool two, int span)
        {
            if (style != "Bars") return span;
            int n = Math.Max(1, (int)(width / (two ? 5 : 3)));
            return n * BarBucket(n, span);
        }

        /// <summary>The average of the samples ending before index end (count of them: bucket; the oldest are left out when there are none).</summary>
        static bool Avg(History d, int end, int bucket, out double avg)
        {
            avg = 0;
            int from = Math.Max(0, end - bucket), to = Math.Min(d.Count, end);
            if (to <= from) return false;
            double sum = 0;
            for (int i = from; i < to; i++) sum += d[i];
            avg = sum / (to - from);
            return true;
        }

        /// <summary>The box behind a line / bar graph, and the grid on it.</summary>
        void DrawBack(DrawingContext dc, double w, double h)
        {
            if (NoTrack) return;
            if (ShowBack) dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), Radius, Radius);
            if (!Grid) return;
            if (GridBrush != null)
            {
                var pen = new Pen(GridBrush, 1);
                foreach (double f in new[] { 0.25, 0.5, 0.75 })
                {
                    double y = Math.Round(1 + (h - 2) * (1 - f)) + 0.5;
                    dc.DrawLine(pen, new Point(1, y), new Point(w - 1, y));
                }
            }
            // the warning level, dashed (not with colour by load, which has no warning colour)
            if (WarnAt >= 0 && WarnFill != null && LoadColors == null)
            {
                double y = Math.Round(1 + (h - 2) * (1 - Clamp100(WarnAt) / 100.0)) + 0.5;
                var pen = new Pen(WarnFill, 1) { DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0) };
                dc.PushOpacity(0.8);
                dc.DrawLine(pen, new Point(1, y), new Point(w - 1, y));
                dc.Pop();
            }
        }

        /// <summary>From a sample to graph percent.</summary>
        double K { get { return FullScale > 0 ? 100.0 / FullScale : 1; } }

        /// <summary>
        /// An automatic full scale for amounts: the highest of the newest samples (both series; samples = how many the graph shows - a line
        /// shows 60), rounded up to 1, 2 or 5 × 10ⁿ, and never below min. So a burst fills the graph and the scale drops again once it has
        /// scrolled off.
        /// </summary>
        public static double ScaleFor(History data, History data2, double min, int samples = LineSamples)
        {
            double top = Math.Max(Peak(data, samples), Peak(data2, samples));
            return Nice(Math.Max(min > 0 ? min : 0.001, top));
        }

        static double Peak(History h, int samples)
        {
            if (h == null) return 0;
            double top = 0;
            for (int i = Math.Max(0, h.Count - Math.Max(1, samples)); i < h.Count; i++) top = Math.Max(top, h[i]);
            return top;
        }

        static double Nice(double v)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (double f in new[] { 1.0, 2.0, 5.0 }) if (v <= f * p * 1.000001) return f * p;
            return 10 * p;
        }

        bool Warned(double v) { return WarnAt >= 0 && WarnFill != null && (WarnBelow ? v <= WarnAt : v >= WarnAt); }

        /// <summary>The fill for a value: WarnFill once it is past the threshold.</summary>
        Brush FillFor(double v, Brush normal)
        {
            if (WarnAt < 0 || WarnFill == null) return normal;
            return (WarnBelow ? v <= WarnAt : v >= WarnAt) ? WarnFill : normal;
        }

        bool Fades { get { return Blend > 0 && WarnAt >= 0 && WarnFill is SolidColorBrush; } }

        static Color ColorOf(Brush b, Color fallback) { var s = b as SolidColorBrush; return s != null ? s.Color : fallback; }

        static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
                                  (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>How far past the threshold the blend carries on (half the lead-in: 20 before → 10 after).</summary>
        double Tail { get { return Blend / 2; } }

        /// <summary>The blend's two ends in graph percent: from Lo (all normal colour) to Hi (all warning colour), or the other way round
        /// for "warn below".</summary>
        double FadeStart { get { return WarnBelow ? WarnAt + Blend : WarnAt - Blend; } }
        double FadeEnd { get { return WarnBelow ? WarnAt - Tail : WarnAt + Tail; } }

        /// <summary>
        /// How far into the warning colour a value is: 0 = the normal colour, 1 = the warning colour. It starts very gently Blend before
        /// the threshold, gets stronger towards it and is the full warning colour Tail past it (an ease-in curve, so the first part is subtle).
        /// </summary>
        double WarnShare(double v)
        {
            double s = FadeStart, e = FadeEnd;
            if (Math.Abs(e - s) < 1e-6) return (WarnBelow ? v <= WarnAt : v >= WarnAt) ? 1 : 0;
            double t = Math.Max(0, Math.Min(1, (v - s) / (e - s)));
            return t * t;
        }

        /// <summary>The colour of one value (ring, ring text): the blend between the normal and the warning colour.</summary>
        Brush ValueBrush(double v, Brush normal)
        {
            if (!Fades) return FillFor(v, normal);
            var b = new SolidColorBrush(Mix(ColorOf(normal, Colors.White), ColorOf(WarnFill, Colors.Orange), WarnShare(v)));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// The colour by position: a gradient over the graph's full length (from x 0 to x end, or from the bottom y0 up to the top y1)
        /// that is the normal colour up to the fade, blends through it and is the warning colour past the threshold.
        /// </summary>
        Brush RangeBrush(Brush normal, Point zero, Point full)
        {
            if (!Fades) return null;
            Color n = ColorOf(normal, Colors.White), w = ColorOf(WarnFill, Colors.Orange);
            // several stops along the blend, so the ease-in curve shows (two stops would be a straight, short ramp)
            double lo = Math.Min(FadeStart, FadeEnd), hi = Math.Max(FadeStart, FadeEnd);
            var g = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = zero, EndPoint = full };
            const int steps = 10;
            double last = -1;
            for (int k = 0; k <= steps; k++)
            {
                double pc = lo + (hi - lo) * k / steps;
                double off = Math.Max(0, Math.Min(1, pc / 100.0));
                if (off <= last) off = last + 0.00001;
                last = off;
                g.GradientStops.Add(new GradientStop(Mix(n, w, WarnShare(pc)), off));
            }
            g.Freeze();
            return g;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(GraphW, GraphH);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 2 || h < 2) return;
            switch (GraphStyle)
            {
                case "Bars": DrawBars(dc, w, h); break;
                case "Line": DrawLine(dc, w, h); break;
                case "Ring": DrawRing(dc, w, h); break;
                case "Cores": if (Cores.Length > 0) DrawCores(dc, w, h); else DrawMeter(dc, w, h); break;
                default: DrawMeter(dc, w, h); break;
            }
        }

        /// <summary>A bar per thread, side by side, each filling from the bottom (past the warning in WarnFill).</summary>
        void DrawCores(DrawingContext dc, double w, double h)
        {
            int n = Cores.Length;
            double gap = n > 24 ? 0.5 : 1;
            double bw = Math.Max(1, (w - gap * (n - 1)) / n);
            double r = Math.Min(2, bw / 2);
            for (int i = 0; i < n; i++)
            {
                double x = i * (bw + gap);
                dc.DrawRoundedRectangle(Track, null, new Rect(x, 0, bw, h), r, r);
                double fh = h * Clamp100(Cores[i]) / 100.0;
                if (fh >= 1) dc.DrawRoundedRectangle(LoadColors != null ? LoadBrushAt(Cores[i]) : FillFor(Cores[i], Fill), null, new Rect(x, h - fh, bw, fh), r, r);
            }
        }

        static double Clamp100(double v) { return Math.Max(0, Math.Min(100, v)); }

        void DrawMeter(DrawingContext dc, double w, double h)
        {
            bool two = Value2 >= 0;
            double th = Math.Max(4, Math.Min(10, h * 0.4));
            if (two) th = Math.Max(3, Math.Min(7, h * 0.28));
            double gap = two ? 2 : 0;
            double y = (h - (two ? 2 * th + gap : th)) / 2;
            double v1 = Value * K, v2 = Value2 * K;
            Brush b1 = LoadColors != null ? LoadRange(new Point(0, 0), new Point(w, 0)) : RangeBrush(Fill, new Point(0, 0), new Point(w, 0)) ?? FillFor(v1, Fill);
            Meter(dc, w, y, th, v1, b1);
            if (two) Meter(dc, w, y + th + gap, th, v2, RangeBrush(Fill2, new Point(0, 0), new Point(w, 0)) ?? FillFor(v2, Fill2));
        }

        void Meter(DrawingContext dc, double w, double y, double th, double value, Brush fill)
        {
            dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, th), th / 2, th / 2);
            double fw = w * Clamp100(value) / 100.0;
            if (fw >= 1)
            {
                double r = Math.Min(th / 2, fw / 2);
                dc.DrawRoundedRectangle(fill, null, new Rect(0, y, fw, th), r, r);
            }
        }

        void DrawBars(DrawingContext dc, double w, double h)
        {
            DrawBack(dc, w, h);
            if (Data == null || Data.Count == 0) return;
            bool two = Data2 != null && Value2 >= 0;
            double step = two ? 5 : 3, barW = 2;
            int n = (int)(w / step);
            int count = Data.Count;
            // longer times: each bar is the average of a few seconds
            int bucket = BarBucket(n, Span);
            Brush range = LoadColors != null ? LoadRange(new Point(0, h - 1), new Point(0, 1)) : RangeBrush(Fill, new Point(0, h - 1), new Point(0, 1));
            Brush range2 = RangeBrush(Fill2, new Point(0, h - 1), new Point(0, 1));
            double kk = K;
            for (int k = 0; k < n; k++)
            {
                int end = count - (n - 1 - k) * bucket;      // the bar's samples end here (exclusive)
                double avg;
                if (end <= 0 || !Avg(Data, end, bucket, out avg)) continue;
                double pv = avg * kk;
                double bh = Math.Max(1, (h - 2) * Clamp100(pv) / 100.0);
                dc.DrawRectangle(range ?? FillFor(pv, Fill), null, new Rect(k * step + 0.5, h - 1 - bh, barW, bh));
                if (two)
                {
                    int end2 = end - (count - Data2.Count);
                    double avg2;
                    if (end2 > 0 && Avg(Data2, end2, bucket, out avg2))
                    {
                        double pv2 = avg2 * kk;
                        double bh2 = Math.Max(1, (h - 2) * Clamp100(pv2) / 100.0);
                        dc.DrawRectangle(range2 ?? FillFor(pv2, Fill2), null, new Rect(k * step + 0.5 + barW, h - 1 - bh2, barW, bh2));
                    }
                }
            }
        }

        /// <summary>A round gauge: the track, an arc for the value from 12 o'clock clockwise, the value text in the middle.</summary>
        void DrawRing(DrawingContext dc, double w, double h)
        {
            double d = Math.Min(w, h);
            double th = Math.Max(2.5, Math.Round(d * 0.12, 1));
            var c = new Point(w / 2, h / 2);
            double r = d / 2 - th / 2;
            if (r < 2) return;
            dc.DrawEllipse(null, new Pen(Track, th), c, r, r);
            double val = Value * K;
            double v = Clamp100(val);
            bool load = LoadColors != null;
            if ((load || Fades) && v > 0.3)
            {
                // like a meter bent round: each bit of the ring takes the colour of its own position - the graph colour, blending
                // through the fade, the warning colour past the threshold (colour by load: low → middle → high)
                const double step = 1.0;     // percent per piece
                Color n = ColorOf(Fill, Colors.White), wc = ColorOf(WarnFill, Colors.Orange);
                Func<double, Color> colorAt = delegate (double pc) { return load ? LoadColorAt(pc) : Mix(n, wc, WarnShare(pc)); };
                for (double p0 = 0; p0 < v; p0 += step)
                {
                    double p1 = Math.Min(v, p0 + step + (p0 + step < v ? 0.35 : 0));     // a hair of overlap: no seams
                    var b = new SolidColorBrush(colorAt((p0 + Math.Min(v, p0 + step)) / 2));
                    b.Freeze();
                    DrawArc(dc, c, r, th, p0, p1, b);
                }
                // round ends, like the plain ring
                Func<double, Point> at = delegate (double pc) { double a = pc / 100.0 * 2 * Math.PI; return new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)); };
                if (v < 99.9)
                {
                    var b0 = new SolidColorBrush(colorAt(0)); b0.Freeze();
                    var b1 = new SolidColorBrush(colorAt(v)); b1.Freeze();
                    dc.DrawEllipse(b0, null, at(0), th / 2, th / 2);
                    dc.DrawEllipse(b1, null, at(v), th / 2, th / 2);
                }
            }
            else if (v >= 99.9) dc.DrawEllipse(null, new Pen(ValueBrush(val, Fill), th), c, r, r);
            else if (v > 0.3)
            {
                double a = v / 100.0 * 2 * Math.PI;
                var s = new Point(c.X, c.Y - r);
                var e = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(s, false, false);
                    ctx.ArcTo(e, new Size(r, r), 0, v > 50, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, new Pen(ValueBrush(val, Fill), th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
            }
            if (string.IsNullOrEmpty(Center)) return;
            Brush tb = Fades ? (WarnShare(val) > 0 ? ValueBrush(val, CenterBrush ?? Fill) : (CenterBrush ?? Fill)) : Warned(val) ? WarnFill : (CenterBrush ?? Fill);
            double size = Math.Max(6, CenterSize);
            var ft = new FormattedText(Center, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, tb,
                                       VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double room = (r - th / 2) * 2 * 0.86;
            if (ft.Width > room && ft.Width > 0) ft.SetFontSize(Math.Max(5, size * room / ft.Width));
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }

        /// <summary>One piece of the ring, from one percent to another (clockwise from the top), flat ends.</summary>
        static void DrawArc(DrawingContext dc, Point c, double r, double th, double from, double to, Brush b)
        {
            double a0 = from / 100.0 * 2 * Math.PI, a1 = to / 100.0 * 2 * Math.PI;
            var s = new Point(c.X + r * Math.Sin(a0), c.Y - r * Math.Cos(a0));
            var e = new Point(c.X + r * Math.Sin(a1), c.Y - r * Math.Cos(a1));
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(s, false, false);
                ctx.ArcTo(e, new Size(r, r), 0, (to - from) > 50, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(b, th), g);
        }

        void DrawLine(DrawingContext dc, double w, double h)
        {
            DrawBack(dc, w, h);
            if (Data == null) return;
            // a line takes the warning colour while its latest value is past the threshold
            // with a fade: coloured by height (the parts of the line past the threshold are the warning colour)
            // colour by load: coloured by height, low → middle → high
            Brush range = LoadColors != null ? LoadRange(new Point(0, h - 1), new Point(0, 1)) : RangeBrush(Fill, new Point(0, h - 1), new Point(0, 1));
            Brush range2 = RangeBrush(Fill2, new Point(0, h - 1), new Point(0, 1));
            LineSeries(dc, w, h, Data, K, range ?? (Data.Count > 0 ? FillFor(Data[Data.Count - 1] * K, Fill) : Fill));
            if (Data2 != null && Value2 >= 0) LineSeries(dc, w, h, Data2, K, range2 ?? (Data2.Count > 0 ? FillFor(Data2[Data2.Count - 1] * K, Fill2) : Fill2));
        }

        void LineSeries(DrawingContext dc, double w, double h, History data, double k, Brush fill)
        {
            // longer times: each point is the average of a few seconds (60 points for a minute, about 90 for 5 / 15 minutes)
            int bucket = LineBucket(Span);
            int slots = Math.Max(2, Span / bucket);
            int m = Math.Min(slots, (data.Count + bucket - 1) / bucket);
            if (m < 2) return;

            double step = w / (slots - 1);
            var pts = new Point[m];
            for (int i = 0; i < m; i++)
            {
                double avg;
                Avg(data, data.Count - (m - 1 - i) * bucket, bucket, out avg);
                double v = Clamp100(avg * k);
                double x = w - (m - 1 - i) * step;
                double y = 1 + (h - 2) * (1 - v / 100.0);
                pts[i] = new Point(x, y);
            }

            if (AreaFill != "None")
            {
                var area = new StreamGeometry();
                using (StreamGeometryContext ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].X, h - 1), true, true);
                    ctx.LineTo(pts[0], false, false);
                    Trace(ctx, pts, h, false);
                    ctx.LineTo(new Point(pts[m - 1].X, h - 1), false, false);
                }
                area.Freeze();
                if (AreaFill == "Fade")
                {
                    // strong just under the line's highest point, fading out towards the bottom
                    double top = h;
                    foreach (Point pt in pts) top = Math.Min(top, pt.Y);
                    var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, top), EndPoint = new Point(0, h) };
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                    mask.Freeze();
                    dc.PushOpacityMask(mask);
                }
                else dc.PushOpacity(AreaFill == "Solid" ? 0.55 : 0.2);
                dc.DrawGeometry(fill, null, area);
                dc.Pop();
            }

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                Trace(ctx, pts, h, true);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(fill, LineWidth) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
        }

        /// <summary>From pts[0] (already there) through the rest: straight lines, or a smooth curve through every point (Catmull-Rom).</summary>
        void Trace(StreamGeometryContext ctx, Point[] pts, double h, bool stroked)
        {
            int m = pts.Length;
            if (!SmoothLine)
            {
                for (int i = 1; i < m; i++) ctx.LineTo(pts[i], stroked, false);
                return;
            }
            Func<Point, Point> keep = delegate (Point p) { return new Point(p.X, Math.Max(1, Math.Min(h - 1, p.Y))); };     // never past the box
            for (int i = 0; i < m - 1; i++)
            {
                Point p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(m - 1, i + 2)];
                Point c1 = keep(new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6));
                Point c2 = keep(new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6));
                ctx.BezierTo(c1, c2, p2, stroked, true);
            }
        }
    }

    /// <summary>CPU / memory / GPU module: optional text plus a meter, bar graph or line graph, vertically centred in the bar.</summary>
    public sealed class MetricModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        readonly History _hist;
        readonly TextBlock _text = new TextBlock();
        readonly MetricView _view = new MetricView();
        readonly StackPanel _row;
        GaugeFace _gauge;
        DotFace _dots;
        string _shownKey;

        /// <summary>A tile in a widget is taller than one row: it sizes itself (so does Dots in a widget).</summary>
        public int RowCount { get { return _cfg.Style == "Tile" || (Dotted && _bar.IsWidget) ? 0 : 1; } }
        /// <summary>The "Dots" style (CPU only for now).</summary>
        bool Dotted { get { return _cfg.Style == "Dots"; } }
        bool Gauge { get { return GaugeFace.IsGauge(_cfg.Style); } }

        public MetricModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _hist = mgr.HistoryFor(cfg.Id);

            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.TextAlignment = TextAlignment.Left;
            _view.VerticalAlignment = VerticalAlignment.Center;
            _view.Data = _hist;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            row.Children.Add(_text);
            row.Children.Add(_view);
            _row = row;
            Child = row;

            ApplyTheme();
        }

        void ApplyTheme()
        {
            var t = _bar.Theme.ForModule(_cfg);
            if (Gauge)
            {
                if (_gauge == null) _gauge = new GaugeFace(_hist);
                _gauge.Setup(_cfg.Style, _bar, t, t.Resolve(_cfg.Color, t.MainAccent), _cfg.WarnOn ? Math.Max(0, Math.Min(100, _cfg.WarnAt)) : -1, _cfg.WarnBelow, false, _cfg.GraphHeightPercent, _cfg.ShowText, _cfg.ShownTileWidth,
                              _cfg.WarnOn ? Theme.WarnFade : 0, t.Warn(_cfg.WarnColor));
                _gauge.Look(_cfg, t);
                Child = _gauge;
                return;
            }
            if (Dotted)
            {
                if (_dots == null) _dots = new DotFace(_hist);
                _dots.Setup(_cfg, _bar, t);
                Child = _dots;
                _shownKey = null;
                return;
            }
            Child = _row;
            _view.ApplyLook(_cfg, t);
            bool graph = _cfg.Style != "Text";
            bool text = _cfg.ShowText || !graph;

            _text.Foreground = t.Fg;
            _text.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            _view.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
            _view.Margin = (text && graph) ? new Thickness(8, 0, 0, 0) : new Thickness(0);

            _view.Fill = t.Resolve(_cfg.Color, t.MainAccent);
            _view.Fill2 = t.SecondaryAccent;
            _view.WarnFill = t.Warn(_cfg.WarnColor);
            _view.WarnAt = _cfg.WarnOn ? Math.Max(0, Math.Min(100, _cfg.WarnAt)) : -1;
            _view.WarnBelow = _cfg.WarnBelow;
            _view.Blend = _cfg.WarnOn ? Theme.WarnFade : 0;
            _view.Track = t.Track;
            _view.GraphStyle = _cfg.Style;
            _view.GraphW = _cfg.ShownGraphWidth;
            _view.GraphH = Math.Max(6, _bar.HostHeight * _cfg.GraphHeightPercent / 100.0);
            _view.InvalidateMeasure();

            // fixed minimum text width so the module does not jiggle as the numbers change
            string sample;
            switch (_cfg.Type)
            {
                case ModuleTypes.Memory: sample = _cfg.ShowDetail ? "RAM 88.8/88.8 GB" : "RAM 100%"; break;
                case ModuleTypes.Gpu: sample = _cfg.ShowDetail ? "GPU 100% - 88.8 GB" : "GPU 100%"; break;
                default: sample = _cfg.CpuShowSpeed ? "CPU 100% · 8.8 GHz" : "CPU 100%"; break;
            }
            if (_cfg.ShowTemp && !_cfg.ShownTempLine2) sample += " 100\u00B0C";
            var probe = new TextBlock { Text = sample, FontFamily = t.Font, FontSize = t.FontSize };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _text.MinWidth = Math.Ceiling(probe.DesiredSize.Width) + 2;
        }

        /// <summary>Temperature text: the chosen HWiNFO reading, or automatic (GPU: NVIDIA driver first, then HWiNFO).</summary>
        string TempText()
        {
            HwReading r = null;
            if (!string.IsNullOrEmpty(_cfg.TempSensor)) r = HwInfo.Find(_cfg.TempSensor);
            else
            {
                if (_cfg.Type == ModuleTypes.Gpu)
                {
                    double c;
                    if (Nvml.TryGetTemp(Math.Max(0, _cfg.GpuIndex - 1), out c))
                        return c.ToString("0") + "\u00B0C";
                    r = HwInfo.AutoGpuTemp();
                }
                else if (_cfg.Type == ModuleTypes.Memory) r = HwInfo.AutoMemoryTemp();
                else r = HwInfo.AutoCpuTemp();
            }
            if (r == null) return "n/a";
            string unit = string.IsNullOrEmpty(r.Unit) ? "\u00B0C" : r.Unit;
            return r.Value.ToString("0") + unit;
        }

        readonly Queue<double> _recent = new Queue<double>();

        /// <summary>CPU smoothing: the average of the last CpuSmooth seconds (a new reading only counts when addSample - once a second).</summary>
        double Smooth(double v, bool addSample)
        {
            if (_cfg.CpuSmooth <= 1) { _recent.Clear(); return v; }
            if (addSample || _recent.Count == 0)
            {
                _recent.Enqueue(v);
                while (_recent.Count > _cfg.CpuSmooth) _recent.Dequeue();
            }
            double sum = 0;
            foreach (double x in _recent) sum += x;
            return sum / _recent.Count;
        }

        /// <summary>Refresh the displayed values. addSample = false when only re-rendering (e.g. after a settings change).</summary>
        /// <summary>Its widget is hidden: Update takes the sample (the graph stays whole) but draws nothing (WidgetWindow.UpdateStats).</summary>
        internal bool Quiet;

        public void Update(SystemStats st, GpuStats gpu, bool addSample)
        {
            double v;
            string txt;
            switch (_cfg.Type)
            {
                case ModuleTypes.Memory:
                    v = st.MemPercent;
                    txt = _cfg.ShowDetail
                        ? string.Format("RAM {0:0.0}/{1:0.0} GB", st.MemUsedGb, st.MemTotalGb)
                        : string.Format("RAM {0:0}%", v);
                    break;
                case ModuleTypes.Gpu:
                    {
                        GpuAdapter[] a = gpu == null ? new GpuAdapter[0] : gpu.Adapters;
                        int i = Math.Min(a.Length, _cfg.GpuIndex) - 1;
                        if (i < 0) { v = 0; txt = "GPU n/a"; }
                        else
                        {
                            v = a[i].Percent;
                            txt = _cfg.ShowDetail
                                ? string.Format("GPU {0:0}% - {1:0.0} GB", v, a[i].VramGb)
                                : string.Format("GPU {0:0}%", v);
                        }
                        break;
                    }
                default:
                    {
                        // Task Manager's figure (counts turbo boost) when it's on and known; else processor time
                        bool counters = _cfg.CpuTaskManager || _cfg.CpuShowSpeed || _cfg.Style == "Cores";
                        if (counters) CpuCounters.Want();
                        double raw = _cfg.CpuTaskManager && !double.IsNaN(CpuCounters.Utility) ? CpuCounters.Utility : st.CpuPercent;
                        v = Smooth(raw, addSample);
                        txt = string.Format("CPU {0:0}%", v);
                        if (_cfg.CpuShowSpeed && !double.IsNaN(CpuCounters.Ghz)) txt += " · " + CpuCounters.Ghz.ToString("0.0", CultureInfo.CurrentCulture) + " GHz";
                        _view.Cores = _cfg.Style == "Cores" ? CpuCounters.Cores : new double[0];
                        break;
                    }
            }

            if (Quiet) { if (addSample) _hist.Add(v); return; }     // its widget is hidden: the sample only, nothing drawn (0.38.224)
            string temp = _cfg.ShowTemp ? TempText() : null;

            if (Gauge)
            {
                if (addSample) _hist.Add(v);
                string label = _cfg.Type == ModuleTypes.Memory ? "RAM" : _cfg.Type == ModuleTypes.Gpu ? "GPU" : "CPU";
                string detail = null;
                if (_cfg.ShowDetail && _cfg.Type == ModuleTypes.Memory) detail = string.Format("{0:0.0}/{1:0.0} GB", st.MemUsedGb, st.MemTotalGb);
                if (_cfg.ShowDetail && _cfg.Type == ModuleTypes.Gpu && txt.Contains(" - ")) detail = txt.Substring(txt.IndexOf(" - ") + 3);
                if (_cfg.Type == ModuleTypes.Cpu && txt.Contains(" · ")) detail = txt.Substring(txt.IndexOf(" · ") + 3);      // the clock speed
                bool na = txt.EndsWith("n/a");
                _gauge.Show(label, na ? "n/a" : v.ToString("0"), na ? "" : "%", detail, v, Theme.Crossed(v, _cfg.WarnOn, _cfg.WarnAt, _cfg.WarnBelow), temp);
                ToolTip = txt + (temp != null ? "  " + temp : "");
                return;
            }

            if (Dotted && _dots != null)
            {
                if (addSample) _hist.Add(v);
                bool na = txt.EndsWith("n/a");
                string detail = txt.Contains(" · ") ? txt.Substring(txt.IndexOf(" · ") + 3) : null;     // the clock speed
                if (temp != null) detail = detail == null ? temp : detail + " · " + temp;
                _dots.Show(_cfg.Type == ModuleTypes.Memory ? "RAM" : _cfg.Type == ModuleTypes.Gpu ? "GPU" : "CPU", na ? "n/a" : v.ToString("0"), na ? "" : "%", detail,
                           Theme.Crossed(v, _cfg.WarnOn, _cfg.WarnAt, _cfg.WarnBelow));
                ToolTip = txt + (temp != null ? "  " + temp : "");
                return;
            }

            if (addSample) _hist.Add(v);
            _view.Value = v;
            if (_view.Visibility == Visibility.Visible) _view.InvalidateVisual();
            // the text only when it changed (a new text means a new layout pass for the bar)
            bool warn = Theme.Crossed(v, _cfg.WarnOn, _cfg.WarnAt, _cfg.WarnBelow);
            string key = txt + "|" + temp + "|" + warn;
            if (key == _shownKey) return;
            _shownKey = key;
            // the temperature in the Secondary Accent colour
            _text.Inlines.Clear();
            _text.Inlines.Add(new Run(txt));
            if (temp != null)
            {
                if (_cfg.ShownTempLine2) _text.Inlines.Add(new LineBreak()); else _text.Inlines.Add(new Run("  "));
                _text.Inlines.Add(new Run(temp) { Foreground = _bar.Theme.SecondaryAccent });
                txt += (_cfg.ShownTempLine2 ? "\n" : "  ") + temp;
            }
            _text.Foreground = warn ? (Brush)_bar.Theme.ForModule(_cfg).Warn(_cfg.WarnColor) : _bar.Theme.Fg;
            ToolTip = txt;
        }
    }
}
