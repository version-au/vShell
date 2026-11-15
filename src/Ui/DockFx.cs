using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Icon animations for the Launcher's and the Taskbar's icons on a bar of any layout (icons only):
    ///   Lift    - the icon under the mouse rises a little and grows slightly (not with Magnify);
    ///   Glow    - a soft Main Accent halo behind it (drawn on the bar's background, under the icons) plus a close glow around its shape;
    ///   Magnify - macOS-style: the icon under the mouse grows most, its neighbours less the further away, and they spread apart;
    ///   Bounce  - the icon bounces when an app is started from it.
    /// Everything is a render transform (the bar's layout never changes), driven frame by frame only while something moves. The bar
    /// window gets transparent room above / beside the bar for the grown icons (see BarWindow.ApplyPlacement); clicks go through it.
    /// </summary>
    public sealed class DockFx
    {
        sealed class Item
        {
            public FrameworkElement Button, Icon;
            public ModuleConfig Cfg;
            public readonly ScaleTransform Scale = new ScaleTransform(1, 1);
            public readonly TranslateTransform Move = new TranslateTransform();
            public DropShadowEffect Glow;
            public System.Windows.Shapes.Ellipse Halo;
            public bool Hover;
            public double HoverV, MagV = 1, MagTarget = 1;
            public double BounceStart = -1;
            // where the icon is in the bar (without any of our transforms), in bar coordinates; K = bar units per icon unit
            public double BaseX, BaseW, BaseY, BaseH, K = 1;
            public bool Live;
        }

        const double BounceHeight = 14, BounceTime = 0.9, LiftHeight = 6, LiftGrow = 0.12, Reach = 2.6;

        readonly UIElement _root;
        readonly Canvas _layer;
        readonly bool _top, _centre;
        readonly List<Item> _items = new List<Item>();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        bool _running;
        double _last;
        Point? _mouse;

        /// <param name="root">the bar's background element (mouse positions are taken relative to it)</param>
        /// <param name="layer">a canvas on the bar's background, under the modules (no hit testing): the glow halos are drawn on it</param>
        /// <param name="top">the bar is along the top edge: icons grow / rise downwards</param>
        /// <param name="centre">in a widget: icons grow about their middle (no edge to grow away from) and lift upwards</param>
        public DockFx(UIElement root, Canvas layer, bool top, bool centre = false)
        {
            _root = root;
            _layer = layer;
            _top = top;
            _centre = centre;
            if (_layer != null) _layer.Children.Clear();
        }

        /// <summary>The module asks for any dock animation at all.</summary>
        public static bool Wanted(ModuleConfig c)
        {
            return c != null && (c.DockLift || c.DockGlow || c.DockMagnify || c.DockBounce);
        }

        /// <summary>The module shows icons only (animations are only for icon-only launchers / taskbars).</summary>
        public static bool IconsOnly(ModuleConfig c)
        {
            if (c == null) return false;
            if (c.Type == ModuleTypes.Launcher) return !c.ShowLabels && !c.ShowGroupLabels;
            if (c.Type == ModuleTypes.Taskbar) return c.TaskLabel == "Icon";
            return false;
        }

        /// <summary>Room (DIPs) the animations of these modules need beyond the bar: away from the edge, and on each side.</summary>
        public static void Room(IEnumerable<ModuleConfig> modules, double barHeight, out double away, out double side)
        {
            away = 0; side = 0;
            double unit = Math.Max(16, barHeight);
            foreach (ModuleConfig c in modules)
            {
                if (c == null || !IconsOnly(c) || !Wanted(c)) continue;
                bool lift = c.DockLift && !c.DockMagnify;
                double grow = c.DockMagnify ? unit * 0.8 * (c.DockMagnifyPercent / 100.0 - 1) : lift ? unit * 0.8 * LiftGrow : 0;
                // the halo reaches past the icon by half of what it is bigger than the icon
                double glow = c.DockGlow ? Math.Max(0, unit * 0.8 * (c.DockGlowSize / 100.0 - 1) / 2 - unit * 0.1) : 0;
                double a = grow + (lift ? LiftHeight : 0) + (c.DockBounce ? BounceHeight : 0) + glow + 4;
                double s = Math.Max(c.DockMagnify ? unit * 1.4 * (c.DockMagnifyPercent / 100.0 - 1) : 0, glow) + 4;
                away = Math.Max(away, a);
                side = Math.Max(side, s);
            }
            away = Math.Ceiling(away);
            side = Math.Ceiling(side);
        }

        /// <summary>Animate this button (icon = the part that glows). Call once, when the button is made.</summary>
        public void Add(FrameworkElement button, FrameworkElement icon, ModuleConfig cfg, Color glow)
        {
            if (button == null || !Wanted(cfg)) return;
            var it = new Item { Button = button, Icon = icon ?? button, Cfg = cfg };
            var g = new TransformGroup();
            g.Children.Add(it.Scale);
            g.Children.Add(it.Move);
            button.RenderTransform = g;
            button.RenderTransformOrigin = new Point(0.5, _centre ? 0.5 : _top ? 0 : 1);
            if (cfg.DockGlow)
            {
                // a close glow hugging the icon's shape, plus a wider halo around it
                it.Glow = new DropShadowEffect { Color = glow, ShadowDepth = 0, BlurRadius = 6 + 10 * cfg.DockGlowSize / 100.0, Opacity = 0, RenderingBias = RenderingBias.Performance };
                if (_layer != null)
                {
                    var brush = new RadialGradientBrush();
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, glow.R, glow.G, glow.B), 0.0));
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, glow.R, glow.G, glow.B), 0.35));
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, glow.R, glow.G, glow.B), 0.7));
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, glow.R, glow.G, glow.B), 1.0));
                    brush.Freeze();
                    it.Halo = new System.Windows.Shapes.Ellipse { Fill = brush, Opacity = 0, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                    _layer.Children.Add(it.Halo);
                }
            }
            button.MouseEnter += delegate { it.Hover = true; Kick(); };
            button.MouseLeave += delegate { it.Hover = false; Kick(); };
            // taskbar buttons come and go (and are moved around): only animate the ones on the bar
            button.Loaded += delegate { if (!_items.Contains(it)) _items.Add(it); };
            button.Unloaded += delegate
            {
                _items.Remove(it);
                it.Hover = false;
                if (it.Halo != null) it.Halo.Visibility = Visibility.Collapsed;
            };
            _items.Add(it);
        }

        /// <summary>An app is being started from this button.</summary>
        public void Bounce(FrameworkElement button)
        {
            foreach (Item it in _items)
                if (it.Button == button && it.Cfg.DockBounce) { it.BounceStart = _clock.Elapsed.TotalSeconds; Kick(); }
        }

        public void MouseMove(Point p) { _mouse = p; if (AnyMagnify()) Kick(); }
        public void MouseLeave() { _mouse = null; foreach (Item it in _items) it.Hover = false; Kick(); }

        bool AnyMagnify()
        {
            foreach (Item it in _items) if (it.Cfg.DockMagnify) return true;
            return false;
        }

        void Kick()
        {
            if (_running) return;
            _running = true;
            _last = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += Tick;
        }

        /// <summary>Stop animating (the bar is closing or rebuilding its modules).</summary>
        public void Stop()
        {
            if (!_running) return;
            CompositionTarget.Rendering -= Tick;
            _running = false;
        }

        void Tick(object sender, EventArgs e)
        {
            try { Step(); }
            catch (Exception ex) { Log.Write("Dock animation: " + ex.Message); Stop(); }
        }

        void Step()
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Max(0.001, Math.Min(0.05, now - _last));
            _last = now;
            double ease = 1 - Math.Exp(-dt * 16);
            bool busy = false;

            // where each icon sits (layout only)
            var live = new List<Item>();
            foreach (Item it in _items)
            {
                it.Live = false;
                var parent = VisualTreeHelper.GetParent(it.Button) as Visual;
                if (parent == null || !it.Button.IsVisible || !it.Button.IsDescendantOf(_root)) continue;
                Vector off = VisualTreeHelper.GetOffset(it.Button);
                Rect r = parent.TransformToAncestor(_root).TransformBounds(new Rect(off.X, off.Y, it.Button.ActualWidth, it.Button.ActualHeight));
                it.BaseX = r.X + r.Width / 2;
                it.BaseW = r.Width;
                it.BaseY = r.Y + r.Height / 2;
                it.BaseH = r.Height;
                it.K = it.Button.ActualWidth > 0 ? r.Width / it.Button.ActualWidth : 1;
                it.Live = true;
                live.Add(it);
            }

            // targets and easing: on a bar how far the mouse is along the row; in a widget (a grid, maybe) how far it is in any direction,
            // so every icon grows by its own distance and moving diagonally is as smooth as along a row
            foreach (Item it in live)
            {
                it.MagTarget = 1;
                if (_mouse.HasValue && it.Cfg.DockMagnify)
                {
                    double dx = _mouse.Value.X - it.BaseX, dy = _centre ? _mouse.Value.Y - it.BaseY : 0;
                    double d = Math.Sqrt(dx * dx + dy * dy) / Math.Max(8, it.BaseW);
                    if (d < Reach) it.MagTarget = 1 + (it.Cfg.DockMagnifyPercent / 100.0 - 1) * (Math.Cos(Math.PI * d / Reach) + 1) / 2;
                }
                it.MagV += (it.MagTarget - it.MagV) * ease;
                double hv = it.Hover ? 1 : 0;
                it.HoverV += (hv - it.HoverV) * ease;
                if (Math.Abs(it.MagTarget - it.MagV) > 0.002) busy = true; else it.MagV = it.MagTarget;
                if (Math.Abs(hv - it.HoverV) > 0.01) busy = true; else it.HoverV = hv;
            }

            // magnified icons push their neighbours apart: along each row (a bar is one row), and in a grid along each column too -
            // every row and column grows evenly to both sides
            var shiftX = new Dictionary<Item, double>();
            var shiftY = new Dictionary<Item, double>();
            foreach (List<Item> row in Lines(live, false)) Spread(row, false, shiftX);
            if (_centre) foreach (List<Item> col in Lines(live, true)) Spread(col, true, shiftY);

            foreach (Item it in live)
            {
                double shift, shiftV;
                if (!shiftX.TryGetValue(it, out shift)) shift = 0;
                if (!shiftY.TryGetValue(it, out shiftV)) shiftV = 0;

                double lift = it.Cfg.DockLift && !it.Cfg.DockMagnify ? LiftHeight * it.HoverV : 0;
                double grow = it.Cfg.DockLift && !it.Cfg.DockMagnify ? 1 + LiftGrow * it.HoverV : 1;
                double bounce = 0;
                if (it.BounceStart >= 0)
                {
                    double t = now - it.BounceStart;
                    if (t >= BounceTime) it.BounceStart = -1;
                    else { bounce = BounceHeight * Math.Abs(Math.Sin(Math.PI * t / (BounceTime / 2))) * (1 - 0.45 * t / BounceTime); busy = true; }
                }
                double s = it.MagV * grow;
                double k = Math.Max(0.1, it.K);
                if (it.Scale.ScaleX != s) { it.Scale.ScaleX = s; it.Scale.ScaleY = s; }
                double mx = shift / k, my = ((_top && !_centre ? 1 : -1) * (lift + bounce) + shiftV) / k;
                if (it.Move.X != mx) it.Move.X = mx;
                if (it.Move.Y != my) it.Move.Y = my;

                if (it.Glow != null)
                {
                    double strength = it.Cfg.DockGlowStrength / 100.0;
                    double o = strength * it.HoverV;
                    if (o < 0.01)
                    {
                        if (it.Icon.Effect == it.Glow) it.Icon.Effect = null;
                        if (it.Halo != null && it.Halo.Visibility == Visibility.Visible) it.Halo.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        it.Glow.Opacity = Math.Min(1, o * 1.2);
                        if (it.Icon.Effect != it.Glow) it.Icon.Effect = it.Glow;
                        PlaceHalo(it, o);
                    }
                }
            }
            if (!busy) Stop();
        }

        /// <summary>The icons in lines: rows (by their Y) or columns (by their X), each line sorted along itself. A bar is one row.</summary>
        List<List<Item>> Lines(List<Item> live, bool columns)
        {
            var lines = new List<List<Item>>();
            if (!_centre && !columns) { var all = new List<Item>(live); all.Sort(delegate (Item a, Item b) { return a.BaseX.CompareTo(b.BaseX); }); lines.Add(all); return lines; }
            var sorted = new List<Item>(live);
            sorted.Sort(delegate (Item a, Item b) { return columns ? a.BaseX.CompareTo(b.BaseX) : a.BaseY.CompareTo(b.BaseY); });
            List<Item> cur = null;
            foreach (Item it in sorted)
            {
                Item last = cur != null ? cur[cur.Count - 1] : null;
                double gap = last == null ? 0 : columns ? Math.Abs(it.BaseX - last.BaseX) : Math.Abs(it.BaseY - last.BaseY);
                double tol = last == null ? 0 : Math.Max(4, (columns ? last.BaseW : last.BaseH) / 2);
                if (cur == null || gap > tol) { cur = new List<Item>(); lines.Add(cur); }
                cur.Add(it);
            }
            foreach (List<Item> l in lines) l.Sort(delegate (Item a, Item b) { return columns ? a.BaseY.CompareTo(b.BaseY) : a.BaseX.CompareTo(b.BaseX); });
            return lines;
        }

        /// <summary>How far each icon in a line moves along it so the grown ones have room (the line grows evenly to both sides).</summary>
        static void Spread(List<Item> line, bool vertical, Dictionary<Item, double> into)
        {
            double total = 0;
            foreach (Item it in line) total += (vertical ? it.BaseH : it.BaseW) * (it.MagV - 1);
            double before = 0;
            foreach (Item it in line)
            {
                double extra = (vertical ? it.BaseH : it.BaseW) * (it.MagV - 1);
                into[it] = before + extra / 2 - total / 2;      // bar units
                before += extra;
            }
        }

        /// <summary>The halo behind / around the icon, where the icon is drawn now (lifted / grown / bounced included).</summary>
        void PlaceHalo(Item it, double opacity)
        {
            if (it.Halo == null || _layer == null || it.Icon.ActualWidth < 1) return;
            Rect r = it.Icon.TransformToVisual(_layer).TransformBounds(new Rect(0, 0, it.Icon.ActualWidth, it.Icon.ActualHeight));
            double d = Math.Max(r.Width, r.Height) * it.Cfg.DockGlowSize / 100.0;
            it.Halo.Width = d;
            it.Halo.Height = d;
            Canvas.SetLeft(it.Halo, r.X + r.Width / 2 - d / 2);
            Canvas.SetTop(it.Halo, r.Y + r.Height / 2 - d / 2);
            it.Halo.Opacity = opacity;
            if (it.Halo.Visibility != Visibility.Visible) it.Halo.Visibility = Visibility.Visible;
        }
    }
}
