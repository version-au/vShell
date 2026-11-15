using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Base of the modules that show several small readouts ("tiles"): Home Assistant entities and the service status dots.
    /// On a bar the tiles sit side by side; in a widget they can also be stacked one per row (Cfg.TileLayout = "List").
    /// </summary>
    [PluginApi]
    public abstract class TilesModule : Border, IMultiRow
    {
        /// <summary>One tile (part of the plugin API with TilesModule). Root is the whole tile: anchor a PopupWindow to it to open a card at the tile.</summary>
        protected sealed class Tile
        {
            public Border Root;
            public TextBlock Text;
            public Ellipse Dot;
            public MetricView Meter;
            public Action Click;
            /// <summary>Showing real data (not "…" / an entity id / a message): only then is its width kept from shrinking.</summary>
            public bool Settled;
        }

        protected readonly BarManager Mgr;
        protected readonly IModuleHost Bar;
        protected readonly ModuleConfig Cfg;
        protected readonly List<Tile> Tiles = new List<Tile>();
        readonly StackPanel _panel = new StackPanel();
        protected readonly Theme T;

        bool Stacked { get { return Cfg.TileLayout == "List" && Bar.IsWidget; } }

        /// <summary>How many widget rows this module needs (1 on a bar, or side by side).</summary>
        public virtual int RowCount { get { return Stacked ? Math.Max(1, Tiles.Count) : 1; } }

        protected TilesModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            Mgr = mgr; Bar = bar; Cfg = cfg; T = bar.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            _panel.Orientation = Stacked ? Orientation.Vertical : Orientation.Horizontal;
            _panel.VerticalAlignment = VerticalAlignment.Center;
            Child = _panel;
        }

        /// <summary>Call from the subclass constructor once its own fields are ready.</summary>
        protected void Build()
        {
            BuildTiles();
            if (Tiles.Count == 0) AddTile(false, false, null);
            Update();
        }

        /// <summary>
        /// The set of tiles changed (servers came or went): throws the tiles away, calls BuildTiles and Update again, and tells the bar or
        /// widget (a widget with the tiles one per row gets taller or shorter).
        /// </summary>
        protected void Rebuild()
        {
            _panel.Children.Clear();
            Tiles.Clear();
            _panel.Orientation = Stacked ? Orientation.Vertical : Orientation.Horizontal;
            Build();
            Bar.ContentChanged();
        }

        protected abstract void BuildTiles();
        public abstract void Update();

        protected Brush FillBrush()
        {
            return T.Resolve(Cfg.Color, T.MainAccent);
        }

        protected Tile AddTile(bool dot, bool meter, Action click)
        {
            var tile = new Tile { Click = click };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (dot)
            {
                tile.Dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 6, 0), Fill = T.FgDim, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(tile.Dot);
            }
            tile.Text = new TextBlock { Foreground = T.Fg, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(tile.Text);
            if (meter)
            {
                tile.Meter = new MetricView
                {
                    GraphStyle = "Meter",
                    Fill = FillBrush(),
                    Track = T.Track,
                    GraphW = Cfg.ShownGraphWidth,
                    GraphH = Math.Max(6, Bar.HostHeight * Cfg.GraphHeightPercent / 100.0),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(tile.Meter);
            }

            tile.Root = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(8, 0, 8, 0),
                Child = row,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Stacked) tile.Root.Height = Bar.HostHeight;

            if (click != null)
            {
                tile.Root.Cursor = Cursors.Hand;
                bool down = false;
                tile.Root.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; down = true; };
                tile.Root.MouseLeave += delegate { down = false; };
                tile.Root.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    if (!down) return;
                    down = false; e.Handled = true;
                    click();
                };
            }
            _panel.Children.Add(tile.Root);
            Tiles.Add(tile);
            return tile;
        }

        /// <summary>Set a tile's text without letting it shrink (so a row of tiles does not twitch as numbers change).</summary>
        protected static void SetText(Tile t, string text, Brush fg, string tip, bool settled = true)
        {
            t.Text.Text = text;
            t.Text.Foreground = fg;
            t.Root.ToolTip = string.IsNullOrEmpty(tip) ? null : tip;
            // the first real value after a placeholder ("…", the entity id before Home Assistant answers, a message) starts the width
            // afresh - otherwise the longer placeholder kept the module (and its widget) wide for good
            if (!t.Settled) t.Text.MinWidth = 0;
            t.Settled = settled;
            double keep = t.Text.MinWidth;
            t.Text.MinWidth = 0;
            t.Text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            t.Text.MinWidth = Math.Max(keep, Math.Ceiling(t.Text.DesiredSize.Width) + 2);
        }

        /// <summary>Colour a tile's dot (StatusColors.Good / Warn / Bad, or T.FgDim for unknown). Nothing happens for a tile made without one.</summary>
        protected static void SetDot(Tile t, Brush fill)
        {
            if (t.Dot != null) t.Dot.Fill = fill;
        }
    }

    // =====================================================================
    // Home Assistant entities
    // =====================================================================
    public sealed class ServicesModule : TilesModule
    {
        static readonly Brush Up = StatusColors.Good, Slow = StatusColors.Warn, Down = StatusColors.Bad;

        static Brush Frozen(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        public ServicesModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { Build(); }

        protected override void BuildTiles()
        {
            foreach (ServiceCheck c in Cfg.Checks)
            {
                ServiceCheck cap = c;
                Action click = null;
                if (c.Kind == "Http" && c.Target.Trim().Length > 0)
                {
                    click = delegate
                    {
                        string url = cap.Target.Trim();
                        if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "http://" + url;
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch (Exception ex) { Log.Write("Open " + url + ": " + ex.Message); }
                    };
                }
                AddTile(true, false, click);
            }
        }

        public override void Update()
        {
            if (Cfg.Checks.Count == 0)
            {
                SetText(Tiles[0], "Services: add something to watch in Settings", T.FgDim, null, false);
                return;
            }
            for (int i = 0; i < Tiles.Count && i < Cfg.Checks.Count; i++)
            {
                ServiceCheck c = Cfg.Checks[i];
                ServiceStatus s = ServiceMonitor.Status(c, Cfg.CheckSeconds);
                Brush dot;
                switch (s.State)
                {
                    case ServiceState.Up: dot = Up; break;
                    case ServiceState.Slow: dot = Slow; break;
                    case ServiceState.Down: dot = Down; break;
                    default: dot = T.FgDim; break;
                }
                SetDot(Tiles[i], dot);
                string name = c.Name.Length > 0 ? c.Name : c.Target;
                string tip = name + "\n" + c.Kind + " " + c.Target + "\n" + s.Detail +
                             (s.Checked > DateTime.MinValue ? "\nChecked " + TimeText.Clock(s.Checked, true) : "") +
                             (Tiles[i].Click != null ? "\nClick to open" : "");
                SetText(Tiles[i], Cfg.TileNames ? name : "", T.Fg, tip);
                Tiles[i].Text.Visibility = Cfg.TileNames ? Visibility.Visible : Visibility.Collapsed;
                if (Tiles[i].Dot != null && !Cfg.TileNames) Tiles[i].Dot.Margin = new Thickness(0);
            }
        }
    }
}
