using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Power flow (widgets): solar, grid, battery and home as circles, joined by lines with dots that move in the direction the power
    /// flows (faster for more power). The home ring shows how much of the home's power comes from solar, battery and grid.
    /// Values come from Home Assistant sensors (see ModuleConfig.Pf*).
    /// </summary>
    public sealed class PowerFlowModule : Border, IMultiRow
    {
        readonly ModuleConfig _cfg;
        readonly PowerFlowView _view;
        readonly DispatcherTimer _data, _anim;
        readonly IModuleHost _host;
        DateTime _last = DateTime.UtcNow;

        public int RowCount { get { return 0; } }       // sizes itself

        public PowerFlowModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _cfg = cfg;
            _host = host;
            Theme t = host.Theme.ForModule(cfg);
            // as wide as the space the widget gives it (measured, so its border and padding are taken into account);
            // an automatic-width widget gives unlimited width: then 200
            _view = new PowerFlowView(t, cfg, 200);
            Child = _view;
            Padding = new Thickness(4);
            Background = Brushes.Transparent;

            _data = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _data.Tick += delegate
            {
                // a module the widget dropped (it rebuilds while fitting a fixed-size box) never comes back: stop for good
                if (PresentationSource.FromVisual(this) == null) { if (++_gone >= 5) { _data.Stop(); _anim.Stop(); } return; }
                _gone = 0;
                Read();
            };
            _anim = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _anim.Tick += delegate
            {
                DateTime now = DateTime.UtcNow;
                double dt = Math.Min(0.2, (now - _last).TotalSeconds);
                _last = now;
                if (_paused) return;            // nobody can see it: don't spend anything on the dots
                if (_view.Step(dt)) _view.DrawDots();
            };
            // in a fixed-size widget the widget rebuilds a few times while fitting, and WPF can then send Unloaded after Loaded - the
            // timers stopped while it was on screen (no readings, no dots). So: stop only when it's really gone, start whenever it shows.
            Loaded += delegate { Read(); _data.Start(); _anim.Start(); };
            IsVisibleChanged += delegate { if (IsVisible && !_data.IsEnabled) { Read(); _data.Start(); } };
            Unloaded += delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { if (!IsLoaded) { _data.Stop(); _anim.Stop(); } }));
            };
            _data.Start();
            Read();
        }

        // ---- pause the animation when nobody can see it ----------------------------------------------------
        bool _paused;
        int _gone;

        /// <summary>The PC is locked, or a maximised / full-screen window covers this (not always-on-top) widget's monitor.</summary>
        bool Hidden()
        {
            return !IsVisible || ModuleVisibility.Covered(_host);
        }

        double W(string entity)
        {
            if (string.IsNullOrWhiteSpace(entity)) return double.NaN;
            return PowerFlowMath.Watts(HaClient.Get(entity));
        }

        void Read()
        {
            double solar = W(HaView.Of(_cfg).Solar), imp = W(HaView.Of(_cfg).GridImport), exp = W(HaView.Of(_cfg).GridExport), bat = W(HaView.Of(_cfg).Battery), home = W(HaView.Of(_cfg).Home);
            if (HaView.Of(_cfg).BatteryInvert && !double.IsNaN(bat)) bat = -bat;
            PowerFlows f = PowerFlowMath.Compute(solar, imp, exp, bat, home);
            f.HasSolar = HaView.Of(_cfg).Solar.Length > 0;
            f.HasGrid = HaView.Of(_cfg).GridImport.Length > 0 || HaView.Of(_cfg).GridExport.Length > 0;
            f.HasBattery = HaView.Of(_cfg).Battery.Length > 0;
            if (HaView.Of(_cfg).BatterySoc.Length > 0)
            {
                HaState s = HaClient.Get(HaView.Of(_cfg).BatterySoc);
                double v;
                if (s != null && !s.Unavailable && s.TryNumber(out v)) f.BatterySoc = v;
            }
            _view.Flows = f;
            _view.Message = !HaClient.Configured ? "Set up Home Assistant in Settings first"
                          : (!f.HasSolar && !f.HasGrid && !f.HasBattery) ? "Pick the sensors in this module's settings" : null;
            _view.Redraw();
            _paused = Hidden();
            // the 30-a-second animation timer only runs while someone can see it (checked every second here)
            if (_paused) { if (_anim.IsEnabled) _anim.Stop(); }
            else if (!_anim.IsEnabled && IsLoaded) { _last = DateTime.UtcNow; _anim.Start(); }
            ToolTip = "Solar " + PowerFlowMath.Format(f.Solar) + "\nGrid: import " + PowerFlowMath.Format(f.GridImport) + ", export " + PowerFlowMath.Format(f.GridExport) +
                      "\nBattery: " + (f.BatteryCharge > 0 ? "charging " + PowerFlowMath.Format(f.BatteryCharge) : f.BatteryDischarge > 0 ? "discharging " + PowerFlowMath.Format(f.BatteryDischarge) : "idle") +
                      (double.IsNaN(f.BatterySoc) ? "" : " (" + Math.Round(f.BatterySoc) + "%)") + "\nHome " + PowerFlowMath.Format(f.Home);
        }
    }

    /// <summary>Draws the power flow diagram (everything in OnRender; the dots move with Step()).</summary>
    public sealed class PowerFlowView : FrameworkElement
    {
        sealed class Line
        {
            public PathGeometry Path;
            public Brush Brush;
            public double Watts;
            public bool Reverse;     // the dots run from the end to the start
            public double Phase;
        }

        readonly Theme _t;
        readonly ModuleConfig _cfg;
        readonly Brush _solar, _grid, _battery, _home, _dim;
        readonly Typeface _face, _faceBold;
        public PowerFlows Flows = new PowerFlows();
        public string Message;

        Line _solarHome, _solarBattery, _solarGrid, _gridHome, _gridBattery, _batteryHome;
        double _builtFor = -1;
        Point _cSolar, _cGrid, _cHome, _cBattery;
        double _r;

        double _size;

        // Three layers, so the 30-times-a-second animation only redraws the dots: the lines (under), the moving dots, and the circles
        // with their text (over). Lines and circles are drawn again only when the readings change (about once a second).
        readonly DrawingVisual _linesVis = new DrawingVisual(), _dotsVis = new DrawingVisual(), _labelsVis = new DrawingVisual();
        Pen[] _pens;          // per line, frozen; made with the geometry
        double _lineW = 1.5;

        protected override int VisualChildrenCount { get { return 3; } }
        protected override Visual GetVisualChild(int index) { return index == 0 ? _linesVis : index == 1 ? (Visual)_dotsVis : _labelsVis; }

        public PowerFlowView(Theme t, ModuleConfig cfg, double size)
        {
            AddVisualChild(_linesVis);
            AddVisualChild(_dotsVis);
            AddVisualChild(_labelsVis);
            _t = t; _cfg = cfg; _size = size;
            _solar = t.Resolve(HaView.Of(cfg).SolarColor, t.SecondaryAccent);
            _grid = t.Resolve(HaView.Of(cfg).GridColor, t.MainAccent);
            _battery = t.Resolve(HaView.Of(cfg).BatteryColor, t.Fg);
            _home = t.Fg;
            _dim = t.FgDim;
            _face = new Typeface(t.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _faceBold = new Typeface(t.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        }

        double Size { get { return _size; } }

        protected override Size MeasureOverride(Size available)
        {
            // Build 2: its size follows the text size (S / M / L), only smaller if the room is smaller
            double natural = Math.Round(Math.Max(10, _t.FontSize) * 18);
            double want = double.IsInfinity(available.Width) || available.Width <= 0 ? natural : Math.Max(160, Math.Min(natural, Math.Floor(available.Width)));
            if (Math.Abs(want - _size) > 0.5) { _size = want; _builtFor = -1; }
            return new Size(_size, Math.Round(_size * 1.08));
        }

        // ---- geometry ----------------------------------------------------------------------------------
        void Build()
        {
            if (_builtFor == Size) return;
            _builtFor = Size;
            double w = Size, h = Math.Round(Size * 1.08), label = Math.Max(14, _t.FontSize * 1.5);
            _r = Math.Round(w * (w < 280 ? 0.15 : 0.135));     // a little roomier when small, for the text inside
            double cx = w / 2, cy = h / 2;
            _cSolar = new Point(cx, label + _r + 2);
            _cBattery = new Point(cx, h - label - _r - 2);
            double edge = 3 + Math.Max(2, w / 130.0) * 0.9;    // room for the thicker ring round Home (and the grid outline)
            _cGrid = new Point(_r + edge, cy);
            _cHome = new Point(w - _r - edge, cy);
            double off = _r * 0.38;     // the curved lines meet the side circles a little above / below their centre

            _solarHome = new Line { Brush = _solar, Path = Curve(OnCircle(_cSolar, 58), new Point(_cHome.X - Side(off), cy - off), true) };
            _solarGrid = new Line { Brush = _solar, Path = Curve(OnCircle(_cSolar, 122), new Point(_cGrid.X + Side(off), cy - off), true) };
            _solarBattery = new Line { Brush = _solar, Path = Straight(new Point(cx, _cSolar.Y + _r), new Point(cx, _cBattery.Y - _r)) };
            _gridHome = new Line { Brush = _grid, Path = Straight(new Point(_cGrid.X + _r, cy), new Point(_cHome.X - _r, cy)) };
            _gridBattery = new Line { Brush = _grid, Path = Curve(new Point(_cGrid.X + Side(off), cy + off), OnCircle(_cBattery, -122), false) };
            _batteryHome = new Line { Brush = _battery, Path = Curve(OnCircle(_cBattery, -58), new Point(_cHome.X - Side(off), cy + off), true) };

            _lineW = Math.Max(1.5, Size / 220.0);
            var pens = new List<Pen>();
            foreach (Line l in Lines()) { var p = new Pen(l.Brush, _lineW); p.Freeze(); pens.Add(p); }
            _pens = pens.ToArray();
        }

        /// <summary>Horizontal distance from a side circle's centre to its edge at height off.</summary>
        double Side(double off) { return Math.Sqrt(Math.Max(0, _r * _r - off * off)); }

        Point OnCircle(Point c, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(c.X + Math.Cos(a) * _r, c.Y + Math.Sin(a) * _r);
        }

        /// <summary>
        /// A smooth bend: fromTopBottom = a is on the solar / battery circle (leave it vertically, arrive at b horizontally);
        /// otherwise a is on a side circle (leave horizontally, arrive at b vertically).
        /// </summary>
        static PathGeometry Curve(Point a, Point b, bool fromTopBottom)
        {
            Point c1, c2;
            if (fromTopBottom)
            {
                c1 = new Point(a.X, a.Y + (b.Y - a.Y) * 0.75);
                c2 = new Point(a.X + (b.X - a.X) * 0.25, b.Y);
            }
            else
            {
                c1 = new Point(a.X + (b.X - a.X) * 0.75, a.Y);
                c2 = new Point(b.X, a.Y + (b.Y - a.Y) * 0.25);
            }
            var fig = new PathFigure { StartPoint = a, IsFilled = false };
            fig.Segments.Add(new BezierSegment(c1, c2, b, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        static PathGeometry Straight(Point a, Point b)
        {
            var fig = new PathFigure { StartPoint = a, IsFilled = false };
            fig.Segments.Add(new LineSegment(b, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        // ---- animation -----------------------------------------------------------------------------------
        void Assign()
        {
            PowerFlows f = Flows;
            _solarHome.Watts = f.SolarToHome;
            _solarGrid.Watts = f.SolarToGrid;
            _solarBattery.Watts = f.SolarToBattery;
            _gridHome.Watts = f.GridToHome;
            // grid <-> battery: drawn from the grid to the battery; battery to grid runs backwards
            _gridBattery.Watts = f.GridToBattery > 0 ? f.GridToBattery : f.BatteryToGrid;
            _gridBattery.Reverse = f.GridToBattery <= 0 && f.BatteryToGrid > 0;
            _batteryHome.Watts = f.BatteryToHome;
        }

        IEnumerable<Line> Lines()
        {
            yield return _gridHome; yield return _solarBattery; yield return _solarHome;
            yield return _solarGrid; yield return _gridBattery; yield return _batteryHome;
        }

        /// <summary>Move the dots; true when something moved (so it needs drawing).</summary>
        public bool Step(double dt)
        {
            if (_builtFor < 0) return false;
            Assign();
            bool moved = false;
            foreach (Line l in Lines())
            {
                if (l.Watts < 1) continue;
                // laps per second: slow for a trickle, quick for several kW
                double speed = 0.18 + Math.Min(1.0, l.Watts / 5000.0) * 0.9;
                l.Phase = (l.Phase + dt * speed) % 1.0;
                moved = true;
            }
            return moved;
        }

        // ---- drawing --------------------------------------------------------------------------------------
        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            Redraw();
        }

        /// <summary>Draw everything again (new readings, new size or new message).</summary>
        public void Redraw()
        {
            Build();
            Assign();
            using (DrawingContext dc = _linesVis.RenderOpen())
            {
                int i = 0;
                foreach (Line l in Lines())
                {
                    dc.PushOpacity(l.Watts >= 1 ? 1.0 : 0.18);
                    dc.DrawGeometry(null, _pens[i++], l.Path);
                    dc.Pop();
                }
            }
            using (DrawingContext dc = _labelsVis.RenderOpen()) DrawLabels(dc);
            DrawDots();
        }

        /// <summary>Only the moving dots (the animation calls this).</summary>
        public void DrawDots()
        {
            if (_builtFor < 0) return;
            using (DrawingContext dc = _dotsVis.RenderOpen())
            {
                foreach (Line l in Lines())
                {
                    if (l.Watts < 1) continue;
                    Point p, tan;
                    l.Path.GetPointAtFractionLength(l.Reverse ? 1 - l.Phase : l.Phase, out p, out tan);
                    dc.DrawEllipse(l.Brush, null, p, _lineW * 2.6, _lineW * 2.6);
                }
            }
        }

        void DrawLabels(DrawingContext dc)
        {
            PowerFlows f = Flows;
            // the text follows S / M / L; it only gets smaller when the room is well under the module's natural size
            double natural = Math.Round(Math.Max(10, _t.FontSize) * 18);
            double fs = Math.Max(9, _t.FontSize * 0.9 * Math.Min(1, Size / (natural * 0.8)));
            double ring = Math.Max(2, Size / 130.0);

            // solar
            Circle(dc, _cSolar, _solar, ring, f.HasSolar);
            Glyph(dc, "", _cSolar, -0.3, _solar, fs * 1.6);
            Text(dc, PowerFlowMath.Format(f.HasSolar ? f.Solar : double.NaN), _cSolar, 0.32, _home, fs, false);
            Text(dc, "Solar", new Point(_cSolar.X, _cSolar.Y - _r - fs * 0.95), 0, _dim, fs * 0.92, false);

            // grid: import / export
            Circle(dc, _cGrid, _grid, ring, f.HasGrid);
            Glyph(dc, "", _cGrid, -0.42, _grid, fs * 1.45);
            Text(dc, "← " + PowerFlowMath.Format(f.HasGrid ? f.GridExport : double.NaN), _cGrid, 0.12, _solar, fs * 0.92, false);
            Text(dc, "→ " + PowerFlowMath.Format(f.HasGrid ? f.GridImport : double.NaN), _cGrid, 0.48, _grid, fs * 0.92, false);
            Text(dc, "Grid", new Point(_cGrid.X, _cGrid.Y + _r + fs * 0.95), 0, _dim, fs * 0.92, false);

            // battery: charge %, charging / discharging
            Circle(dc, _cBattery, _battery, ring, f.HasBattery);
            int level = double.IsNaN(f.BatterySoc) ? 5 : (int)Math.Round(Math.Max(0, Math.Min(100, f.BatterySoc)) / 10.0);
            Glyph(dc, ((char)(0xE850 + level)).ToString(), _cBattery, -0.28, _battery, fs * 1.4);
            if (!double.IsNaN(f.BatterySoc)) Text(dc, Math.Round(f.BatterySoc).ToString("0", CultureInfo.CurrentCulture) + "%", _cBattery, -0.66, _home, fs * 0.92, true);
            Text(dc, "↓ " + PowerFlowMath.Format(f.HasBattery ? f.BatteryCharge : double.NaN), _cBattery, 0.18, _battery, fs * 0.88, false);
            Text(dc, "↑ " + PowerFlowMath.Format(f.HasBattery ? f.BatteryDischarge : double.NaN), _cBattery, 0.52, _battery, fs * 0.88, false);
            Text(dc, "Battery", new Point(_cBattery.X, _cBattery.Y + _r + fs * 0.95), 0, _dim, fs * 0.92, false);

            // home: total, and a ring split by where its power comes from
            Circle(dc, _cHome, _dim, ring * 0.6, true);
            HomeRing(dc, f, ring * 1.6);
            Glyph(dc, "", _cHome, -0.3, _home, fs * 1.5);
            Text(dc, PowerFlowMath.Format(f.Home), _cHome, 0.32, _home, fs, true);
            Text(dc, "Home", new Point(_cHome.X, _cHome.Y + _r + fs * 0.95), 0, _dim, fs * 0.92, false);

            if (!string.IsNullOrEmpty(Message))
                Text(dc, Message, new Point(Size / 2, Math.Round(Size * 1.08) / 2 + _r * 0.9), 0, _dim, fs * 0.85, false);
        }

        void Circle(DrawingContext dc, Point c, Brush b, double thick, bool on)
        {
            dc.PushOpacity(on ? 1.0 : 0.35);
            dc.DrawEllipse(null, new Pen(b, thick), c, _r, _r);
            dc.Pop();
        }

        /// <summary>Arcs round the home circle: solar, battery and grid shares of the home's use.</summary>
        void HomeRing(DrawingContext dc, PowerFlows f, double thick)
        {
            double total = f.SolarToHome + f.BatteryToHome + f.GridToHome;
            if (total < 1) return;
            double start = -90;
            foreach (var part in new[] { new KeyValuePair<double, Brush>(f.SolarToHome, _solar), new KeyValuePair<double, Brush>(f.BatteryToHome, _battery), new KeyValuePair<double, Brush>(f.GridToHome, _grid) })
            {
                if (part.Key <= 0) continue;
                double sweep = part.Key / total * 360.0;
                if (sweep >= 359.9) { dc.DrawEllipse(null, new Pen(part.Value, thick), _cHome, _r, _r); return; }
                Point a = Polar(_cHome, start), b = Polar(_cHome, start + sweep);
                var fig = new PathFigure { StartPoint = a, IsFilled = false };
                fig.Segments.Add(new ArcSegment(b, new Size(_r, _r), 0, sweep > 180, SweepDirection.Clockwise, true));
                var g = new PathGeometry();
                g.Figures.Add(fig);
                dc.DrawGeometry(null, new Pen(part.Value, thick), g);
                start += sweep;
            }
        }

        Point Polar(Point c, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(c.X + Math.Cos(a) * _r, c.Y + Math.Sin(a) * _r);
        }

        /// <summary>Centred text at the circle centre + dy * radius.</summary>
        void Text(DrawingContext dc, string s, Point c, double dy, Brush b, double size, bool bold)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? _faceBold : _face, size, b,
                                       VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y + dy * _r - ft.Height / 2));
        }

        static readonly Typeface GlyphFace = new Typeface(IconUtil.GlyphFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        void Glyph(DrawingContext dc, string g, Point c, double dy, Brush b, double size)
        {
            var ft = new FormattedText(g, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, GlyphFace, size, b,
                                       VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y + dy * _r - ft.Height / 2));
        }
    }
}
