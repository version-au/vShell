using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

namespace VShellFans
{
    /// <summary>A Fans module's own settings (its ModuleConfig's bag).</summary>
    public sealed class FanView
    {
        readonly SettingsBag _b;
        FanView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static FanView Of(ModuleConfig cfg) { return new FanView(cfg); }

        /// <summary>The styles, as the CPU module's Style: Tile (as the Tile style), a graph by each fan (Line, Bars, Meter, Ring), or on a
        /// bar just the fan (the fastest speed; a click for the list).</summary>
        public static readonly string[] WidgetStyles = { "Tile", "Line", "Bars", "Meter", "Ring", "Dots" };
        public static readonly string[] BarStyles = { "Tile", "Line", "Bars", "Meter", "Ring", "Dots", "Fan only" };
        public const string FanOnly = "Fan only";

        /// <summary>The style (widget or bar); from 1.0.2's layouts the first time.</summary>
        public string Style(bool widget)
        {
            string[] all = widget ? WidgetStyles : BarStyles;
            string v = _b.Get("Style", "");
            if (Array.IndexOf(all, v) >= 0) return v;
            string graph = _b.Get("RowStyle", "Line");
            if (Array.IndexOf(WidgetStyles, graph) < 1) graph = "Line";
            if (widget) return _b.Get("WidgetLayout", "") == "Rows" ? graph : "Tile";
            string bar = _b.Get("BarLayout", "");
            return bar == "Every fan, side by side" || bar == "One fan with a graph" ? graph : bar.StartsWith("Just the fan") ? FanOnly : "Tile";
        }
        public void SetStyle(string v) { _b.Set("Style", v); }

        /// <summary>Which fans: "" = every fan shown (Settings > Plugins > Fans), else one fan's key.</summary>
        public string Show
        {
            get
            {
                string v = _b.Get<string>("Show", null);
                if (v != null) return v;
                return _b.Get("BarLayout", "") == "One fan with a graph" ? _b.Get("One", "") : "";
            }
            set { _b.Set("Show", value); }
        }
        /// <summary>Tile in a widget: how many across.</summary>
        public int Across
        {
            get { int v = _b.Get("Across", 0); return v >= 1 ? Math.Min(4, v) : _b.Get("WidgetLayout", "") == "Tiles, one column" ? 1 : 2; }
            set { _b.Set("Across", value); }
        }
        /// <summary>Tile on a bar: each tile's width; 0 = about 8 characters.</summary>
        public int TileWidth { get { int v = _b.Get("TileWidth", 0); return v < 40 ? 0 : Math.Min(400, v); } set { _b.Set("TileWidth", value); } }
        /// <summary>The graph's length in px; 0 = the layout's own (Rows: what's left of the row).</summary>
        public int GraphLen { get { return Math.Max(0, Math.Min(400, _b.Get("GraphLen", 0))); } set { _b.Set("GraphLen", value); } }
        /// <summary>The graph's height as a percentage of the row (widget) or the bar; 0 = the layout's own.</summary>
        public int GraphHeight { get { return Math.Max(0, Math.Min(100, _b.Get("GraphHeight", 0))); } set { _b.Set("GraphHeight", value); } }
        /// <summary>A ring's size as a percentage of the row (widget) or the bar's height.</summary>
        public int RingSize { get { return Math.Max(20, Math.Min(100, _b.Get("RingSize", 80))); } set { _b.Set("RingSize", value); } }
        public bool Animate { get { return _b.Get("Animate", true); } set { _b.Set("Animate", value); } }
        /// <summary>The rpm that fills a graph; 0 = automatic (each fan's own, from what it's shown lately).</summary>
        public int FullScale { get { return Math.Max(0, Math.Min(20000, _b.Get("FullScale", 0))); } set { _b.Set("FullScale", value); } }

        /// <summary>On a bar, with a graph by each fan (Line, Bars, Meter, Dots): where the speed goes (1.2.0).</summary>
        public static readonly string[] SpeedPlaces = { "After the graph", "Under the label", "Under the graph" };
        public string SpeedPlace
        {
            get { string v = _b.Get("SpeedPlace", SpeedPlaces[0]); return Array.IndexOf(SpeedPlaces, v) < 0 ? SpeedPlaces[0] : v; }
            set { _b.Set("SpeedPlace", value); }
        }
        /// <summary>The styles that have a graph by each fan on a bar - the ones Speed applies to.</summary>
        public static bool GraphBeside(string style) { return style == "Line" || style == "Bars" || style == "Meter" || style == "Dots"; }
    }

    /// <summary>
    /// The Fans module: every fan (and pump) HWiNFO or LibreHardwareMonitor reports in rpm, with a small turning fan, its speed and a
    /// graph. In a widget: tiles (two across or one column) or rows; on a bar: tiles side by side, every fan side by side, one fan, or just
    /// the fan - a click shows them all in a popup. A fan that stops (or runs too slow) turns the warning colour.
    /// </summary>
    public sealed class FanModule : Border, IMultiRow
    {
        public int RowCount { get { return 0; } }       // a widget: it sets its own height

        sealed class Bits
        {
            public Fan Fan;
            public FanIcon Icon;
            public TextBlock Name, Num, Unit;
            public MetricView Graph;
            public Border Box;
        }

        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly double _fs;
        readonly SolidColorBrush _accent;
        readonly List<Bits> _bits = new List<Bits>();
        string _built;
        bool _used;
        PopupWindow _popup;
        FanPanel _popupPanel;

        public FanModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            _fs = cfg.ShownFontSize > 0 ? cfg.ShownFontSize : _t.FontSize;
            _accent = _t.Resolve(cfg.Color, _t.MainAccent);
            Background = Brushes.Transparent;
            if (!host.IsWidget)
            {
                Cursor = Cursors.Hand;
                VerticalAlignment = VerticalAlignment.Center;
                MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            }
            else MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };     // (not a widget drag)
            Loaded += delegate { if (!_used) { _used = true; FanStore.Use(1); FanStore.Ticked += Tick; } Tick(); };
            Unloaded += delegate { if (_used) { _used = false; FanStore.Ticked -= Tick; FanStore.Use(-1); } };
            Tick();
        }

        bool Animate { get { return FanView.Of(_cfg).Animate; } }

        /// <summary>The fans this module shows: every fan shown, or the one picked (a fan picked that's gone: every fan).</summary>
        List<Fan> Wanted()
        {
            List<Fan> shown = FanStore.Shown();
            string one = FanView.Of(_cfg).Show;
            if (one.Length == 0) return shown;
            Fan f = shown.Find(delegate (Fan x) { return x.Key == one; });
            return f != null ? new List<Fan> { f } : shown;
        }

        void Tick()
        {
            List<Fan> fans = Wanted();
            FanView v = FanView.Of(_cfg);
            string style = v.Style(_host.IsWidget);
            string sig = style + "|" + v.Across + "|" + v.TileWidth + "|" + v.GraphLen + "|" + v.GraphHeight + "|" + v.RingSize + "|" + v.SpeedPlace + "|" + _host.HostHeight;
            foreach (Fan f in fans) sig += "\n" + f.Key + "\t" + f.Name;
            if (sig != _built) { _built = sig; Build(fans); }
            bool moving = Animate && !ModuleVisibility.Covered(_host);
            if (_panel != null) _panel.Refresh(moving);
            else if (style != FanView.FanOnly)
            {
                for (int i = 0; i < _bits.Count && i < fans.Count; i++) Show(_bits[i], fans[i], v, moving);
            }
            else ShowIconOnly(fans, moving);
            if (_popupPanel != null && _popup != null && _popup.IsOpen) _popupPanel.Refresh(moving);
        }

        // =================================================================
        // Building
        // =================================================================
        void Build(List<Fan> fans)
        {
            _bits.Clear();
            _panel = null;
            _iconOnly = null;
            ToolTip = null;
            if (fans.Count == 0) { Child = Empty(); return; }
            FanView v = FanView.Of(_cfg);
            string style = v.Style(_host.IsWidget);
            if (_host.IsWidget)
            {
                var panel = new FanPanel(_t, _fs, _accent, style, v.Across, v, _cfg, Wanted);
                Child = panel;
                _panel = panel;
                return;
            }
            double hh = Math.Max(16, _host.HostHeight);
            Height = Math.Max(16, hh - 2);
            if (style == "Tile") Child = BarTiles(fans, hh);
            else if (style == FanView.FanOnly) Child = BarIcon(hh);
            else if (fans.Count == 1) Child = BarOne(fans[0], hh);
            else Child = BarSide(fans, hh);
        }

        FanPanel _panel;

        FrameworkElement Empty()
        {
            var t = new TextBlock
            {
                Text = (_host.IsWidget ? "" : "Fans: ") + (HwInfo.Available ? "no fan speeds from " + HwInfo.SourceName : HwInfo.SourceName + " isn't sharing any values"),
                Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _fs, VerticalAlignment = VerticalAlignment.Center, TextWrapping = _host.IsWidget ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Margin = _host.IsWidget ? new Thickness(6) : new Thickness(6, 0, 6, 0)
            };
            ToolTip = HwInfo.IsLhm
                ? "The fans' speeds come from LibreHardwareMonitor: run it (as administrator) with Options > Remote Web Server > Run."
                : "The fans' speeds come from HWiNFO: in its Sensors window right-click each fan and choose \"Report value in Gadget\".\n" +
                  "Or use LibreHardwareMonitor: a Hardware sensor module's settings > Sensors from.";
            return t;
        }

        Bits MakeBits(Fan f, double iconSize, double nameSize, double numSize, bool ring)
        {
            var b = new Bits { Fan = f, Icon = new FanIcon(iconSize, ring) };
            b.Name = new TextBlock { Text = f.Name, Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = nameSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            b.Num = new TextBlock { Foreground = _t.Fg, FontFamily = _t.Font, FontSize = numSize, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            b.Unit = new TextBlock { Text = " rpm", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = Math.Max(7, numSize * 0.75), VerticalAlignment = VerticalAlignment.Center };
            b.Graph = new MetricView { GraphStyle = "Line", Track = _t.Track, Fill = _accent, WarnFill = StatusColors.Bad };
            b.Graph.ApplyLook(_cfg, _t);
            b.Graph.Data = f.Hist;
            _bits.Add(b);
            return b;
        }

        /// <summary>As the Tile style draws in a bar: the name on top, the speed over a line that runs along the bar's bottom edge.</summary>
        FrameworkElement BarTiles(List<Fan> fans, double hh)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            bool oneLine = hh < 35;
            double h = Math.Max(16, hh - 2);
            foreach (Fan f in fans)
            {
                double labelSize = oneLine ? Math.Max(7, Math.Min(_fs * 0.8, h * 0.34)) : Math.Max(7, Math.Min(_fs * 0.78, h * 0.25));
                double numSize = oneLine ? Math.Max(7, Math.Min(_fs * 0.9, h * 0.38)) : Math.Max(7, Math.Min(_fs * 0.95, h * 0.34));
                Bits b = MakeBits(f, Math.Round(labelSize * 1.05), labelSize, numSize, false);
                b.Graph.NoTrack = true;
                b.Graph.GraphW = 64;
                b.Graph.GraphH = 4;
                b.Graph.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.Graph.VerticalAlignment = VerticalAlignment.Stretch;
                var g = new Grid { Width = FanView.Of(_cfg).TileWidth > 0 ? FanView.Of(_cfg).TileWidth : Math.Round(_fs * 8.2), Height = h, Margin = new Thickness(0, 0, 4, 0) };
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(b.Graph, 1);
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, oneLine ? 1 : 2, 6, 0) };
                b.Icon.Margin = new Thickness(0, 0, 4, 0);
                b.Icon.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(b.Icon);
                head.Children.Add(b.Name);
                g.Children.Add(head);
                g.Children.Add(b.Graph);
                var num = new StackPanel { Orientation = Orientation.Horizontal };
                num.Children.Add(b.Num);
                num.Children.Add(b.Unit);
                if (oneLine)
                {
                    b.Unit.Visibility = Visibility.Collapsed;
                    b.Num.Margin = new Thickness(5, 0, 0, 0);
                    head.Children.Add(num);
                }
                else
                {
                    num.Margin = new Thickness(6, -1, 6, 0);
                    num.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetRow(num, 1);
                    g.Children.Add(num);
                }
                b.Box = new Border { Child = g };
                sp.Children.Add(b.Box);
            }
            return sp;
        }

        /// <summary>Each fan: its own turning fan, its name, a graph (as set) and its speed. A ring has the fan turning in its middle.</summary>
        FrameworkElement BarSide(List<Fan> fans, double hh)
        {
            FanView v = FanView.Of(_cfg);
            string style = v.Style(false);
            bool ring = style == "Ring";
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            foreach (Fan f in fans)
            {
                Bits b = MakeBits(f, Math.Round(_fs * 1.15), _fs * 0.92, _fs, false);
                b.Num.FontWeight = FontWeights.SemiBold;
                var one = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
                FrameworkElement graph = FanPanel.Place(b.Graph, b.Icon, style, v, hh, Math.Round(_fs * 3.6), 45, false);
                if (ring)
                {
                    graph.Margin = new Thickness(0, 0, 6, 0);
                    one.Children.Add(graph);
                    one.Children.Add(b.Name);
                    b.Num.Margin = new Thickness(6, 0, 0, 0);
                    one.Children.Add(b.Num);
                }
                else if (v.SpeedPlace == "Under the label")
                {
                    b.Icon.Margin = new Thickness(0, 0, 6, 0);
                    one.Children.Add(b.Icon);
                    one.Children.Add(Stack(b.Name, SmallSpeed(b, hh)));
                    graph.Margin = new Thickness(6, 0, 0, 0);
                    one.Children.Add(graph);
                }
                else if (v.SpeedPlace == "Under the graph")
                {
                    // the turning fan with its speed under it, then the graph with the fan's name under it (V, 1.2.1)
                    one.Children.Add(FanOverSpeed(b, hh, false));
                    one.Children.Add(GraphOverName(b, graph, hh, null));
                }
                else
                {
                    b.Icon.Margin = new Thickness(0, 0, 6, 0);
                    one.Children.Add(b.Icon);
                    one.Children.Add(b.Name);
                    graph.Margin = new Thickness(6, 0, 6, 0);
                    one.Children.Add(graph);
                    one.Children.Add(b.Num);
                }
                b.Box = new Border { Child = one };
                sp.Children.Add(b.Box);
            }
            return sp;
        }

        FrameworkElement BarOne(Fan f, double hh)
        {
            FanView v = FanView.Of(_cfg);
            Bits b = MakeBits(f, Math.Round(_fs * 1.25), _fs, _fs, false);
            b.Num.FontWeight = FontWeights.SemiBold;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            string title = (_cfg.Title ?? "").Trim();
            if (title.Length > 0) { b.Name.Text = title + " "; b.Name.Margin = new Thickness(0, 0, 4, 0); sp.Children.Add(b.Name); }
            string style = v.Style(false);
            FrameworkElement graph = FanPanel.Place(b.Graph, b.Icon, style, v, hh, Math.Round(_fs * 4.4), 45, false);
            if (style == "Ring")
            {
                graph.Margin = new Thickness(0, 0, 7, 0);
                sp.Children.Add(graph);
                sp.Children.Add(b.Num);
                sp.Children.Add(b.Unit);
            }
            else if (v.SpeedPlace == "Under the label")
            {
                // the fan's name (or the module's title) with the speed under it, then the graph
                b.Icon.Margin = new Thickness(0, 0, 7, 0);
                sp.Children.Add(b.Icon);
                if (title.Length > 0) sp.Children.Remove(b.Name);
                b.Name.Text = title.Length > 0 ? title : f.Name;
                b.Name.Margin = new Thickness(0);
                sp.Children.Add(Stack(b.Name, SmallSpeed(b, hh, true)));
                graph.Margin = new Thickness(7, 0, 0, 0);
                sp.Children.Add(graph);
            }
            else if (v.SpeedPlace == "Under the graph")
            {
                // the turning fan with its speed under it, then the graph with the name (or the module's text in front) under it
                if (title.Length > 0) sp.Children.Remove(b.Name);
                sp.Children.Add(FanOverSpeed(b, hh, true));
                sp.Children.Add(GraphOverName(b, graph, hh, title.Length > 0 ? title : f.Name));
            }
            else
            {
                b.Icon.Margin = new Thickness(0, 0, 7, 0);
                sp.Children.Add(b.Icon);
                sp.Children.Add(b.Num);
                sp.Children.Add(b.Unit);
                graph.Margin = new Thickness(7, 0, 0, 0);
                sp.Children.Add(graph);
            }
            b.Box = new Border { Child = sp };
            return b.Box;
        }

        // ---- Speed under the label / under the graph (1.2.0) ----

        /// <summary>Two things one over the other, centred in the bar, the second tucked close under the first.</summary>
        static StackPanel Stack(FrameworkElement top, FrameworkElement under)
        {
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            top.VerticalAlignment = VerticalAlignment.Top;
            top.HorizontalAlignment = HorizontalAlignment.Left;
            under.HorizontalAlignment = HorizontalAlignment.Left;
            under.Margin = new Thickness(0, -1, 0, 0);
            col.Children.Add(top);
            col.Children.Add(under);
            return col;
        }

        /// <summary>The speed in small type for a second line (with " rpm" when asked), sized so two lines fit the bar.</summary>
        FrameworkElement SmallSpeed(Bits b, double hh, bool unit = false)
        {
            double size = UnderSize(hh);
            b.Num.FontSize = size;
            b.Num.FontWeight = FontWeights.SemiBold;
            b.Num.Margin = new Thickness(0);
            b.Name.FontSize = Math.Max(7, Math.Min(b.Name.FontSize, size * 1.05));
            if (!unit) return b.Num;
            b.Unit.FontSize = Math.Max(7, size * 0.85);
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(b.Num);
            line.Children.Add(b.Unit);
            return line;
        }

        /// <summary>The small type under the label / graph: as vShell's Dots modules size theirs (75 % of the font, two lines fit the bar).</summary>
        double UnderSize(double hh) { return Math.Max(7, Math.Min(_fs * 0.75, (hh - 4 - 3) / 2.3)); }

        /// <summary>Under the graph: the turning fan, shrunk to leave room, centred over the speed in small type.</summary>
        FrameworkElement FanOverSpeed(Bits b, double hh, bool unit)
        {
            FrameworkElement speed = SmallSpeed(b, hh, unit);
            double size = UnderSize(hh);
            double icon = Math.Max(8, Math.Min(Math.Round(_fs * 1.15), Math.Floor(hh - 6 - size * 1.3)));
            b.Icon.Width = b.Icon.Height = icon;
            b.Icon.Margin = new Thickness(0);
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            b.Icon.HorizontalAlignment = HorizontalAlignment.Center;
            speed.HorizontalAlignment = HorizontalAlignment.Center;
            speed.Margin = new Thickness(0, 0, 0, 0);
            col.Children.Add(b.Icon);
            col.Children.Add(speed);
            return col;
        }

        /// <summary>Under the graph: the graph (shortened to leave room) with the fan's name - or name - under it in small type.</summary>
        FrameworkElement GraphOverName(Bits b, FrameworkElement graph, double hh, string name)
        {
            double size = UnderSize(hh);
            FitUnder(b.Graph, hh);
            graph.Margin = new Thickness(0);
            if (name != null) b.Name.Text = name;
            b.Name.FontSize = size;
            b.Name.Margin = new Thickness(0);
            var col = Stack(graph, b.Name);
            return col;
        }

        /// <summary>Under the graph: the graph no taller than leaves room for the line under it in the bar.</summary>
        void FitUnder(MetricView g, double hh)
        {
            double size = UnderSize(hh);
            // as vShell's own Dots modules lay out Under the graph (DotFace): the module as tall as the bar less 4 px, a line of text
            // (size x 1.25) and 3 px under 4 rows of dots, none bigger than the default graph's - so the dots are the same size as the
            // CPU module's beside it (1.2.2; before, its own sum made them a little smaller)
            double cell = Math.Max(2, Math.Min(hh * 0.6 / 4, (hh - 4 - size * 1.25 - 3) / 4));
            g.GraphH = Math.Max(3, cell * 4);
        }

        FanIcon _iconOnly;
        TextBlock _iconText;

        FrameworkElement BarIcon(double hh)
        {
            _iconOnly = new FanIcon(Math.Round(_fs * 1.25)) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _iconText = new TextBlock { FontFamily = _t.Font, FontSize = _fs, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(_iconOnly);
            sp.Children.Add(_iconText);
            return new Border { Child = sp, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(Math.Max(4, _t.Radius * 0.6)), VerticalAlignment = VerticalAlignment.Center };
        }

        // =================================================================
        // Showing the values (once a second)
        // =================================================================
        void Show(Bits b, Fan f, FanView v, bool moving)
        {
            b.Fan = f;
            Brush c = f.Bad ? StatusColors.Bad : _accent;
            b.Num.Text = f.Rpm.ToString("N0");
            b.Num.Foreground = f.Bad ? StatusColors.Bad : _t.Fg;
            b.Name.Foreground = f.Bad ? StatusColors.Bad : _t.FgDim;
            if (b.Icon != null) { b.Icon.Colour(c, _t.Bg); b.Icon.Spin(f.Rpm, moving); }
            FanPanel.Feed(b.Graph, f, v.FullScale, c);
            ToolTip = Tip();
        }

        void ShowIconOnly(List<Fan> fans, bool moving)
        {
            if (_iconOnly == null) return;
            Fan bad = fans.Find(delegate (Fan f) { return f.Bad; });
            Fan top = null;
            foreach (Fan f in fans) if (top == null || f.Rpm > top.Rpm) top = f;
            string title = (_cfg.Title ?? "").Trim();
            if (bad != null)
            {
                _iconOnly.Colour(StatusColors.Bad, _t.Bg);
                _iconOnly.Spin(0, false);
                _iconText.Text = (title.Length > 0 ? title + " " : "") + bad.Name + (bad.Rpm < 50 ? " stopped" : " slow");
                _iconText.Foreground = StatusColors.Bad;
                ((Border)Child).Background = Theme.Faded((SolidColorBrush)StatusColors.Bad, 18);
            }
            else
            {
                _iconOnly.Colour(_accent, _t.Bg);
                _iconOnly.Spin(top != null ? top.Rpm : 0, moving);
                _iconText.Text = (title.Length > 0 ? title + " " : "") + (top != null ? top.Rpm.ToString("N0") : "-");
                _iconText.Foreground = _t.Fg;
                ((Border)Child).Background = Brushes.Transparent;
            }
            ToolTip = Tip();
        }

        static string Tip()
        {
            var lines = new List<string>();
            foreach (Fan f in FanStore.Shown()) lines.Add(f.Name + ": " + f.Rpm.ToString("N0") + " rpm" + (f.Bad ? (f.Rpm < 50 ? " - stopped!" : " - slow!") : ""));
            lines.Add("Click for all your fans");
            return string.Join("\n", lines);
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; _popupPanel = null; return; }
            FanView v = FanView.Of(_cfg);
            _popupPanel = new FanPanel(_t, Math.Max(12, _t.FontSize), _accent, "Tile", 2, v, _cfg, FanStore.Shown) { Width = Math.Round(Math.Max(12, _t.FontSize) * 24) };
            var box = new Border { Padding = new Thickness(10), Child = _popupPanel };
            _popup = new PopupWindow(_mgr, _host, this, _t, box);
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
            _popupPanel.Refresh(Animate);
        }
    }

    /// <summary>The fans as tiles (two across or one column) or rows - a widget's Fans module and the bar button's popup.</summary>
    public sealed class FanPanel : Border
    {
        sealed class Row
        {
            public Fan Fan;
            public FanIcon Icon;
            public TextBlock Name, Num;
            public MetricView Graph;
        }

        readonly Theme _t;
        readonly double _fs;
        readonly SolidColorBrush _accent;
        readonly string _style;
        readonly int _across;
        readonly FanView _view;
        readonly ModuleConfig _cfg;
        readonly int _full;
        readonly Func<List<Fan>> _fans;
        readonly List<Row> _rows = new List<Row>();
        string _built;

        /// <param name="style">Tile (across = how many), or the graph by each fan in rows: Line, Bars, Meter, Ring</param>
        public FanPanel(Theme t, double fs, SolidColorBrush accent, string style, int across, FanView view, ModuleConfig cfg, Func<List<Fan>> fans)
        {
            _t = t; _fs = fs; _accent = accent; _style = style; _across = Math.Max(1, across); _view = view; _cfg = cfg; _full = view.FullScale; _fans = fans;
            Background = Brushes.Transparent;
        }

        /// <summary>
        /// Shape a graph as the module's settings say, for a row (or bar) rowH tall: Line / Bars / Meter with the length and height set
        /// (else defLen and defPct of rowH), or a Ring of the size set - returned with the fan turning in its middle (so don't place the
        /// icon yourself then). stretch = with no length set, the graph takes the room there is.
        /// </summary>
        public static FrameworkElement Place(MetricView g, FanIcon icon, string style, FanView v, double rowH, double defLen, double defPct, bool stretch)
        {
            g.GraphStyle = style;
            g.Margin = new Thickness(0);
            g.VerticalAlignment = VerticalAlignment.Center;
            if (style == "Ring")
            {
                double d = Math.Max(14, Math.Round(rowH * v.RingSize / 100.0));
                g.GraphW = g.GraphH = d;
                g.NoTrack = false;
                g.HorizontalAlignment = HorizontalAlignment.Center;
                var box = new Grid { Width = d, Height = d, VerticalAlignment = VerticalAlignment.Center };
                box.Children.Add(g);
                icon.Width = icon.Height = Math.Max(8, Math.Round(d * 0.46));
                icon.Margin = new Thickness(0);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                box.Children.Add(icon);
                return box;
            }
            // automatic: the layout's own height; Dots at least 60 % - vShell's own Dots graph (4 rows, the same size dots as the CPU module
            // beside it; at 45 % they came out smaller and more of them - 1.2.3)
            double pct = v.GraphHeight > 0 ? v.GraphHeight : style == "Meter" ? 22 : style == "Dots" ? Math.Max(defPct, 60) : defPct;
            g.GraphH = Math.Max(3, Math.Round(rowH * pct / 100.0));
            if (v.GraphLen > 0) { g.GraphW = v.GraphLen; g.HorizontalAlignment = HorizontalAlignment.Left; }
            else { g.GraphW = defLen; g.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left; }
            return g;
        }

        /// <summary>The newest values (and the tiles again if the fans changed).</summary>
        public void Refresh(bool moving)
        {
            List<Fan> fans = _fans();
            string sig = "";
            foreach (Fan f in fans) sig += f.Key + "\t" + f.Name + "\n";
            if (sig != _built) { _built = sig; Build(fans); }
            for (int i = 0; i < _rows.Count && i < fans.Count; i++)
            {
                Row r = _rows[i];
                Fan f = fans[i];
                Brush c = f.Bad ? StatusColors.Bad : _accent;
                r.Num.Text = f.Rpm.ToString("N0");
                r.Num.Foreground = f.Bad ? StatusColors.Bad : _t.Fg;
                r.Name.Foreground = f.Bad ? StatusColors.Bad : _t.FgDim;
                r.Icon.Colour(c, _t.Bg, f.Bad ? StatusColors.Bad : Theme.Faded(_t.Fg, 25));
                r.Icon.Spin(f.Rpm, moving);
                Feed(r.Graph, f, _full, c);
            }
        }

        /// <summary>
        /// A graph's newest point: its history, the speed that fills it - the fan's own Max rpm, else the module's "speed that fills the
        /// graph", else automatic (from what it's shown lately) - and its colour. Colour by load follows the same scale.
        /// </summary>
        public static void Feed(MetricView g, Fan f, int full, Brush colour)
        {
            g.Data = f.Hist;
            g.Value = f.Rpm;
            g.FullScale = f.Max > 0 ? f.Max : full > 0 ? full : MetricView.ScaleFor(f.Hist, null, 500);
            g.Fill = colour;
            g.InvalidateVisual();
        }

        void Build(List<Fan> fans)
        {
            _rows.Clear();
            if (fans.Count == 0)
            {
                Child = new TextBlock { Text = "No fan speeds from " + HwInfo.SourceName + " yet.", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _fs, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap };
                return;
            }
            if (_style != "Tile") { Child = Rows(fans); return; }
            var grid = new UniformGrid { Columns = Math.Max(1, Math.Min(_across, fans.Count)) };
            foreach (Fan f in fans) grid.Children.Add(Tile(f));
            Child = grid;
        }

        Row Make(Fan f, double icon, bool ring)
        {
            var r = new Row { Fan = f, Icon = new FanIcon(icon, ring) };
            r.Name = new TextBlock { Text = f.Name, Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = Math.Max(7, _fs * 0.92), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            r.Num = new TextBlock { Foreground = _t.Fg, FontFamily = _t.Font, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            r.Graph = new MetricView { GraphStyle = "Line", Track = _t.Track, Fill = _accent, WarnFill = StatusColors.Bad };
            r.Graph.ApplyLook(_cfg, _t);
            r.Graph.Data = f.Hist;
            _rows.Add(r);
            return r;
        }

        /// <summary>As the Tile style: the name on top (the fan beside it), the big speed drawn over a line that fills to the bottom.</summary>
        FrameworkElement Tile(Fan f)
        {
            Row r = Make(f, Math.Round(_fs * 1.05), false);
            r.Num.FontSize = _fs * 1.75;
            r.Graph.NoTrack = true;
            r.Graph.GraphW = 60;
            r.Graph.GraphH = Math.Max(24, Math.Round(_fs * 2.6));
            r.Graph.HorizontalAlignment = HorizontalAlignment.Stretch;
            r.Graph.VerticalAlignment = VerticalAlignment.Stretch;
            var g = new Grid { Height = Math.Round(_fs * 5.6) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(7, 4, 7, 0) };
            DockPanel.SetDock(r.Icon, Dock.Right);
            head.Children.Add(r.Icon);
            head.Children.Add(r.Name);
            g.Children.Add(head);
            Grid.SetRow(r.Graph, 1);
            g.Children.Add(r.Graph);
            var num = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7, -2, 7, 0) };
            num.Children.Add(r.Num);
            num.Children.Add(new TextBlock { Text = " rpm", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _fs * 0.85, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(num, 1);
            g.Children.Add(num);
            return new Border
            {
                Child = g, Margin = new Thickness(3), ClipToBounds = true, CornerRadius = new CornerRadius(Math.Max(4, _t.Radius * 0.6)),
                Background = Theme.Faded(_t.Fg, 6), ToolTip = f.Name + " - " + FanStore.Part(f.Sensor)
            };
        }

        /// <summary>A row a fan: the fan (in a ring), its name, a graph in the style set and the speed - or, as a Ring, the ring with the fan in it.</summary>
        FrameworkElement Rows(List<Fan> fans)
        {
            var sp = new StackPanel();
            bool ring = _style == "Ring";
            double rowH = Math.Round(_fs * 3.6);
            foreach (Fan f in fans)
            {
                Row r = Make(f, Math.Round(_fs * 2), !ring);
                r.Num.FontSize = _fs * 1.15;
                FrameworkElement graph = Place(r.Graph, r.Icon, _style, _view, rowH, Math.Round(_fs * 9), 60, true);
                var row = new DockPanel { Height = ring ? Math.Max(rowH, Math.Round(rowH * _view.RingSize / 100.0) + 4) : rowH, LastChildFill = true };
                FrameworkElement lead = ring ? graph : (FrameworkElement)r.Icon;
                lead.Margin = new Thickness(2, 0, 8, 0);
                DockPanel.SetDock(lead, Dock.Left);
                row.Children.Add(lead);
                var name = new StackPanel { Width = Math.Round(_fs * 7.5), VerticalAlignment = VerticalAlignment.Center };
                r.Name.Foreground = _t.Fg;
                name.Children.Add(r.Name);
                name.Children.Add(new TextBlock { Text = FanStore.Part(f.Sensor), Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _fs * 0.8, TextTrimming = TextTrimming.CharacterEllipsis });
                DockPanel.SetDock(name, Dock.Left);
                row.Children.Add(name);
                var num = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                num.Children.Add(r.Num);
                num.Children.Add(new TextBlock { Text = " rpm", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _fs * 0.8, VerticalAlignment = VerticalAlignment.Center });
                DockPanel.SetDock(num, Dock.Right);
                row.Children.Add(num);
                if (ring) row.Children.Add(new Border());          // (the room between the name and the speed)
                else { graph.Margin = new Thickness(8, 0, 8, 0); row.Children.Add(graph); }
                sp.Children.Add(row);
            }
            return sp;
        }
    }
}
