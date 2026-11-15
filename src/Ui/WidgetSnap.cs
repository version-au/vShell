using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// One edge to glow (device pixels): a vertical or horizontal line at At, running From..To. Dir = which side the glow falls off
    /// to: +1 = towards higher x / y (a box's left / top edge), -1 = lower (right / bottom edge).
    /// Key = what it is (the box and edge, and what it lined up with): a new key fades in again; the same one just follows the drag.
    /// </summary>
    struct GlowSpec { public bool Vertical; public int At, From, To, Dir; public double Radius; public string Key; }

    /// <summary>
    /// A soft accent glow along one edge of a widget, on top of everything, that clicks go through: shows which edges a dragged
    /// widget lined up with. Brightest at the edge and fading inward, its ends fading out before the widget's rounded corners;
    /// it fades in while sliding out onto the edge, and fades out quickly.
    /// </summary>
    sealed class SnapGlow : Window
    {
        const double BandDip = 10, SlideDip = 8;
        IntPtr _hwnd;
        readonly Border _band = new Border { IsHitTestVisible = false, Opacity = 0 };
        readonly TranslateTransform _slide = new TranslateTransform();
        readonly GradientStop _tipIn = new GradientStop(Colors.Black, 0.12), _tipOut = new GradientStop(Colors.Black, 0.88);
        readonly Color _c;
        string _key;
        bool _vertical, _shaped;
        int _dir, _gen;
        bool _closeWhenHidden;

        public SnapGlow(Color c)
        {
            _c = c;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 2; Height = 2;
            Left = -32000; Top = -32000;
            Title = "vShell guide";
            _band.RenderTransform = _slide;
            // along the edge: the ends fade out (before the corners)
            var mask = new LinearGradientBrush();
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            mask.GradientStops.Add(_tipIn);
            mask.GradientStops.Add(_tipOut);
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _band.OpacityMask = mask;
            Content = _band;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            WinStyle.HideFromTasks(_hwnd, true);
            long ex = Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE);
        }

        Color A(byte alpha) { return Color.FromArgb((byte)(_c.A * alpha / 255), _c.R, _c.G, _c.B); }

        void Shape(bool vertical, int dir)
        {
            _vertical = vertical; _dir = dir; _shaped = true;
            var mask = (LinearGradientBrush)_band.OpacityMask;
            mask.StartPoint = vertical ? new Point(0.5, 0) : new Point(0, 0.5);
            mask.EndPoint = vertical ? new Point(0.5, 1) : new Point(1, 0.5);
            // across the edge: full accent at the edge, fading inward
            var fill = new LinearGradientBrush();
            double s = dir < 0 ? 1 : 0;
            fill.StartPoint = vertical ? new Point(s, 0.5) : new Point(0.5, s);
            fill.EndPoint = vertical ? new Point(1 - s, 0.5) : new Point(0.5, 1 - s);
            fill.GradientStops.Add(new GradientStop(A(255), 0));
            fill.GradientStops.Add(new GradientStop(A(180), 0.22));
            fill.GradientStops.Add(new GradientStop(A(70), 0.55));
            fill.GradientStops.Add(new GradientStop(A(0), 1));
            fill.Freeze();
            _band.Background = fill;
        }

        public void ShowGlow(GlowSpec g, double scale)
        {
            int band = Math.Max(3, (int)Math.Round(BandDip * scale));
            int len = Math.Max(1, g.To - g.From);
            int a = g.Dir > 0 ? g.At : g.At - band;
            if (!_shaped || g.Vertical != _vertical || g.Dir != _dir) Shape(g.Vertical, g.Dir);
            double fade = Math.Max(0.03, Math.Min(0.45, Math.Max(10, g.Radius + 4) * scale / len));
            _tipIn.Offset = fade;
            _tipOut.Offset = 1 - fade;
            if (!IsVisible) Show();
            if (_hwnd == IntPtr.Zero) return;
            const uint flags =Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW;
            if (g.Vertical) Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, a, g.From, band, len, flags);
            else Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, g.From, a, len, band, flags);
            if (g.Key != _key) { _key = g.Key; Enter(g); }
        }

        void Enter(GlowSpec g)
        {
            _gen++;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _band.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.X = 0; _slide.Y = 0;
            // starts a little inside the widget and slides out onto the edge
            _slide.BeginAnimation(g.Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty,
                                      new DoubleAnimation(g.Dir * SlideDip, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        /// <summary>Fade out (and close afterwards, with close = true).</summary>
        public void HideGlow(bool close = false)
        {
            if (close) _closeWhenHidden = true;
            if (_key == null)
            {
                if (close) { try { Close(); } catch { } }
                return;
            }
            _key = null;
            int gen = ++_gen;
            var a = new DoubleAnimation(_band.Opacity, 0, TimeSpan.FromMilliseconds(100));
            a.Completed += delegate
            {
                if (gen != _gen) return;
                if (_closeWhenHidden) { try { Close(); } catch { } }
                else if (_hwnd != IntPtr.Zero) Native.ShowWindow(_hwnd, Native.SW_HIDE);
            };
            _band.BeginAnimation(OpacityProperty, a);
        }
    }

    /// <summary>
    /// Widgets on the desktop: snapping while dragging (edges line up with other widgets, the snap gap to neighbours,
    /// the edge gap to the screen edges and bars) and aligning / distributing several widgets picked with Ctrl+click.
    /// All in device pixels, on the widgets' boxes (inside the outline; outline and shadow margin left out).
    /// Snap links: on drop, what a widget snapped to is remembered (WidgetSettings.LinkX / LinkY); when a widget changes size, or the snap gap
    /// changes, the widgets linked to it move so the gap / line-up stays (Relayout).
    /// </summary>
    public static class WidgetSnap
    {
        // the edge glows: [0] the dragged widget's left / right edge, [1] the other widget's; [2], [3] the same for top / bottom
        static SnapGlow[] _glows;
        static readonly List<WidgetWindow> Selected = new List<WidgetWindow>();

        // =================================================================
        // Snapping
        // =================================================================
        // Edge kinds (per axis): 0 = none, 1 = left / top, 2 = right / bottom.
        // Show = a line-up with another widget's edge (wins a tie). SPart / SKind = the moving box's edge that glows;
        // TKind / TR / TId / TRad = the other widget's (TKind 0: a screen edge or a bar - only the moving one glows).
        struct Cand
        {
            public int Pos; public bool Show; public int Prio;
            public int SPart, SKind, TKind; public RECT TR; public string TId; public double TRad;
        }
        // the last drag was placed freely (Alt held, or snapping off): no snap links on drop
        static bool _freeDrag;

        /// <summary>A box that moves with the drag, as an offset from the dragged widget's box (device pixels).</summary>
        struct Part { public int DX, DY, W, H; public bool Outer; }

        // a group drag: the whole group's outline (Outer) and each widget in it, relative to the dragged one; null = one widget
        static List<Part> _parts;

        /// <summary>
        /// x, y = the dragged window's top-left; moved to the best snap within reach. Dragging several picked widgets, the group snaps
        /// as one: its outer edges (the leftmost, rightmost, top and bottom widgets) to the screen edges, the bars and the other widgets,
        /// and each widget in it lines up with the widgets that aren't moving.
        /// </summary>
        public static void Snap(BarManager mgr, WidgetWindow me, ref int x, ref int y)
        {
            BarSettings s = mgr.Settings;
            _freeDrag = !s.WidgetSnap || (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (_freeDrag) { HideGuides(); return; }
            RECT win;
            if (!Native.GetWindowRect(me.NativeHandle, out win)) return;
            int inset = me.VisibleInsetPx;
            int w = win.Width - 2 * inset, h = win.Height - 2 * inset;
            int L = x + inset, T = y + inset;
            double scale = Math.Max(0.5, Native.GetDpiForWindow(me.NativeHandle) / 96.0);
            int gap = (int)Math.Round(s.WidgetGap * scale), edge = (int)Math.Round(s.WidgetEdgeGap * scale), reach = (int)Math.Round(9 * scale);

            // what moves: this widget alone, or the group's outline and each widget in it
            var parts = _parts != null && _parts.Count > 0 ? _parts : new List<Part> { new Part { DX = 0, DY = 0, W = w, H = h, Outer = true } };
            Part outer = parts[0];
            foreach (Part pt in parts) if (pt.Outer) { outer = pt; break; }

            var xs = new List<Cand>();
            var ys = new List<Cand>();
            MonitorInfo mon = MonitorHelper.FromPoint(L + outer.DX + outer.W / 2, T + outer.DY + outer.H / 2);
            foreach (WidgetWindow o in mgr.WidgetWindows)
            {
                if (o == me || !o.IsVisible || !o.Cfg.Enabled || o.SlidAway) continue;     // slid away at its screen edge: not there to line up with
                if (_moving != null && _moving.Contains(o)) continue;      // moving along with it: nothing to snap to
                RECT r;
                if (!o.TryVisibleRect(out r)) continue;
                // only widgets on the monitor this one is being dragged on
                MonitorInfo om = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                if (mon != null && om != null && !string.Equals(mon.DeviceName, om.DeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                string oid = o.Cfg.Id;
                double orad = o.Cfg.CornerRadius;
                for (int i = 0; i < parts.Count; i++)
                {
                    // this part's box now; a candidate is where the part's left / top would go, given as the dragged widget's left / top
                    Part pt = parts[i];
                    int pl = L + pt.DX, ptp = T + pt.DY, pw = pt.W, ph = pt.H;
                    // line up edges (the matching edges of both glow)
                    xs.Add(new Cand { Pos = r.Left - pt.DX, Show = true, SPart = i, SKind = 1, TKind = 1, TR = r, TId = oid, TRad = orad });
                    xs.Add(new Cand { Pos = r.Right - pw - pt.DX, Show = true, SPart = i, SKind = 2, TKind = 2, TR = r, TId = oid, TRad = orad });
                    ys.Add(new Cand { Pos = r.Top - pt.DY, Show = true, SPart = i, SKind = 1, TKind = 1, TR = r, TId = oid, TRad = orad });
                    ys.Add(new Cand { Pos = r.Bottom - ph - pt.DY, Show = true, SPart = i, SKind = 2, TKind = 2, TR = r, TId = oid, TRad = orad });
                    // the snap gap to a neighbour beside / above / below (only when they are side by side / one above the other):
                    // the two facing edges glow
                    bool besideEachOther = ptp < r.Bottom + gap * 2 && ptp + ph > r.Top - gap * 2;
                    bool aboveEachOther = pl < r.Right + gap * 2 && pl + pw > r.Left - gap * 2;
                    // (squeezed between two: the one to the left / above wins)
                    if (besideEachOther)
                    {
                        xs.Add(new Cand { Pos = r.Right + gap - pt.DX, Prio = 5, SPart = i, SKind = 1, TKind = 2, TR = r, TId = oid, TRad = orad });
                        xs.Add(new Cand { Pos = r.Left - gap - pw - pt.DX, Prio = 4, SPart = i, SKind = 2, TKind = 1, TR = r, TId = oid, TRad = orad });
                    }
                    if (aboveEachOther)
                    {
                        ys.Add(new Cand { Pos = r.Bottom + gap - pt.DY, Prio = 5, SPart = i, SKind = 1, TKind = 2, TR = r, TId = oid, TRad = orad });
                        ys.Add(new Cand { Pos = r.Top - gap - ph - pt.DY, Prio = 4, SPart = i, SKind = 2, TKind = 1, TR = r, TId = oid, TRad = orad });
                    }
                }
            }
            int oi = parts.IndexOf(outer);

            // the screen edges (edge gap) - for the whole group's outline
            if (mon != null)
            {
                RECT m = mon.Bounds;
                int ow = outer.W, oh = outer.H, ox = outer.DX, oy = outer.DY;
                xs.Add(new Cand { Pos = m.Left + edge - ox, SPart = oi, SKind = 1 });
                xs.Add(new Cand { Pos = m.Right - edge - ow - ox, SPart = oi, SKind = 2 });
                ys.Add(new Cand { Pos = m.Top + edge - oy, SPart = oi, SKind = 1 });
                ys.Add(new Cand { Pos = m.Bottom - edge - oh - oy, SPart = oi, SKind = 2 });
                // the bars on this monitor: keep the edge gap below a top bar / above a bottom bar
                foreach (BarWindow b in mgr.BarWindows)
                {
                    RECT br = b.BarRect;
                    if (br.Width <= 0 || br.Right <= m.Left || br.Left >= m.Right || br.Bottom <= m.Top || br.Top >= m.Bottom) continue;
                    if ((br.Top + br.Bottom) / 2 < (m.Top + m.Bottom) / 2) ys.Add(new Cand { Pos = br.Bottom + edge - oy, SPart = oi, SKind = 1 });
                    else ys.Add(new Cand { Pos = br.Top - edge - oh - oy, SPart = oi, SKind = 2 });
                }
            }

            Cand bx, by;
            bool snapX = Best(xs, L, reach, out bx), snapY = Best(ys, T, reach, out by);
            if (snapX) L = bx.Pos;
            if (snapY) T = by.Pos;
            x = L - inset;
            y = T - inset;

            // the edges that lined up glow: the moving widget's (or the group's) and the other widget's
            EnsureGuides(mgr);
            ShowGlows(0, snapX, bx, true, L, T, parts, me.Cfg.CornerRadius, scale);
            ShowGlows(2, snapY, by, false, L, T, parts, me.Cfg.CornerRadius, scale);
        }

        /// <summary>A box's edge as a glow. kind: 1 = left / top, 2 = right / bottom.</summary>
        static GlowSpec EdgeOf(RECT r, bool vertical, int kind, double radius, string key)
        {
            var g = new GlowSpec { Vertical = vertical, Radius = radius, Dir = kind == 1 ? 1 : -1, Key = key + (vertical ? "|v" : "|h") + kind };
            if (vertical) { g.From = r.Top; g.To = r.Bottom; g.At = kind == 1 ? r.Left : r.Right; }
            else { g.From = r.Left; g.To = r.Right; g.At = kind == 1 ? r.Top : r.Bottom; }
            return g;
        }

        /// <summary>Show (or fade out) one axis' pair of glows for the snap picked on it.</summary>
        static void ShowGlows(int slot, bool snapped, Cand c, bool vertical, int L, int T, List<Part> parts, double radius, double scale)
        {
            if (_glows == null) return;
            if (!snapped || c.SKind == 0 || c.SPart < 0 || c.SPart >= parts.Count) { _glows[slot].HideGlow(); _glows[slot + 1].HideGlow(); return; }
            Part pt = parts[c.SPart];
            var box = new RECT { Left = L + pt.DX, Top = T + pt.DY, Right = L + pt.DX + pt.W, Bottom = T + pt.DY + pt.H };
            string to = c.TKind != 0 ? "w" + c.TId + ":" + c.TKind : "screen";
            _glows[slot].ShowGlow(EdgeOf(box, vertical, c.SKind, radius, "me" + c.SPart + ">" + to), scale);
            if (c.TKind != 0) _glows[slot + 1].ShowGlow(EdgeOf(c.TR, vertical, c.TKind, c.TRad, "w" + c.TId), scale);
            else _glows[slot + 1].HideGlow();
        }

        /// <summary>
        /// Resizing: snap one moving edge. vertical = a left / right edge (pos is an x), otherwise a top / bottom edge (pos is a y).
        /// lowSide = the left / top edge (it snaps to the gap after a neighbour's right / bottom). from..to = the edge's extent, for the guide.
        /// </summary>
        public static int SnapEdge(BarManager mgr, WidgetWindow me, int pos, bool vertical, bool lowSide, int from, int to)
        {
            BarSettings s = mgr.Settings;
            EnsureGuides(mgr);
            int slot = vertical ? 0 : 2;
            SnapGlow ownGlow = _glows[slot], otherGlow = _glows[slot + 1];
            if (!s.WidgetSnap || (Keyboard.Modifiers & ModifierKeys.Alt) != 0) { ownGlow.HideGlow(); otherGlow.HideGlow(); return pos; }
            double scale = Math.Max(0.5, Native.GetDpiForWindow(me.NativeHandle) / 96.0);
            int gap = (int)Math.Round(s.WidgetGap * scale), edge = (int)Math.Round(s.WidgetEdgeGap * scale), reach = (int)Math.Round(9 * scale);
            RECT mine;
            MonitorInfo mon = me.TryVisibleRect(out mine) ? MonitorHelper.FromPoint((mine.Left + mine.Right) / 2, (mine.Top + mine.Bottom) / 2) : null;

            var list = new List<Cand>();
            foreach (WidgetWindow o in mgr.WidgetWindows)
            {
                if (o == me || !o.IsVisible || !o.Cfg.Enabled || o.SlidAway) continue;
                RECT r;
                if (!o.TryVisibleRect(out r)) continue;
                MonitorInfo om = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                if (mon != null && om != null && !string.Equals(mon.DeviceName, om.DeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                int a1 = vertical ? r.Left : r.Top, a2 = vertical ? r.Right : r.Bottom;
                string oid = o.Cfg.Id;
                double orad = o.Cfg.CornerRadius;
                list.Add(new Cand { Pos = a1, Show = true, SKind = 1, TKind = 1, TR = r, TId = oid, TRad = orad });
                list.Add(new Cand { Pos = a2, Show = true, SKind = 1, TKind = 2, TR = r, TId = oid, TRad = orad });
                // the gap to a neighbour on that side (only when they overlap the other way)
                bool overlap = vertical ? (from < r.Bottom + gap * 2 && to > r.Top - gap * 2) : (from < r.Right + gap * 2 && to > r.Left - gap * 2);
                if (overlap) list.Add(new Cand { Pos = lowSide ? a2 + gap : a1 - gap, SKind = 1, TKind = lowSide ? 2 : 1, TR = r, TId = oid, TRad = orad });
            }
            if (mon != null)
            {
                RECT m = mon.Bounds;
                list.Add(new Cand { Pos = vertical ? (lowSide ? m.Left + edge : m.Right - edge) : (lowSide ? m.Top + edge : m.Bottom - edge), SKind = 1 });
                if (!vertical)
                    foreach (BarWindow b in mgr.BarWindows)
                    {
                        RECT br = b.BarRect;
                        if (br.Width <= 0 || br.Right <= m.Left || br.Left >= m.Right || br.Bottom <= m.Top || br.Top >= m.Bottom) continue;
                        bool topBar = (br.Top + br.Bottom) / 2 < (m.Top + m.Bottom) / 2;
                        if (topBar && lowSide) list.Add(new Cand { Pos = br.Bottom + edge, SKind = 1 });
                        if (!topBar && !lowSide) list.Add(new Cand { Pos = br.Top - edge, SKind = 1 });
                    }
            }

            Cand best;
            if (!Best(list, pos, reach, out best)) { ownGlow.HideGlow(); otherGlow.HideGlow(); return pos; }
            // the edge being dragged glows (falling off into the widget), and the other widget's edge it lined up with
            string toKey = best.TKind != 0 ? "w" + best.TId + ":" + best.TKind : "screen" + best.Pos;
            var g = new GlowSpec { Vertical = vertical, At = best.Pos, From = from, To = to, Dir = lowSide ? 1 : -1, Radius = me.Cfg.CornerRadius,
                                   Key = "resize" + (lowSide ? "lo" : "hi") + ">" + toKey };
            ownGlow.ShowGlow(g, scale);
            if (best.TKind != 0) otherGlow.ShowGlow(EdgeOf(best.TR, vertical, best.TKind, best.TRad, "w" + best.TId), scale);
            else otherGlow.HideGlow();
            return best.Pos;
        }

        static bool Best(List<Cand> list, int current, int reach, out Cand best)
        {
            best = new Cand();
            int bestDist = int.MaxValue;
            foreach (Cand c in list)
            {
                int d = Math.Abs(c.Pos - current);
                if (d <= reach && (d < bestDist || (d == bestDist && (c.Prio > best.Prio || (c.Prio == best.Prio && c.Show && !best.Show))))) { best = c; bestDist = d; }
            }
            return bestDist != int.MaxValue;
        }

        static void EnsureGuides(BarManager mgr)
        {
            if (_glows != null) return;
            var sb = (mgr.Settings != null ? (Brush)Theme.From(mgr.Settings, 12, 0).MainAccent : null) as SolidColorBrush;
            Color c = sb != null ? sb.Color : Colors.DeepSkyBlue;
            _glows = new[] { new SnapGlow(c), new SnapGlow(c), new SnapGlow(c), new SnapGlow(c) };
        }

        /// <summary>The drag is over: the glows fade out and close (a new accent colour is picked up next time).</summary>
        public static void HideGuides()
        {
            if (_glows == null) return;
            foreach (SnapGlow g in _glows) { try { g.HideGlow(true); } catch { } }
            _glows = null;
        }

        // =================================================================
        // Several widgets: Ctrl+click to pick, then align / distribute
        // =================================================================
        public static bool IsSelected(WidgetWindow w) { return Selected.Contains(w); }
        public static int SelectedCount { get { Prune(); return Selected.Count; } }

        static BarManager _mgr;

        public static void Toggle(WidgetWindow w)
        {
            _mgr = w.Manager;
            if (Selected.Contains(w)) Selected.Remove(w); else Selected.Add(w);
            w.ShowSelected(Selected.Contains(w));
        }

        public static void ClearSelection()
        {
            foreach (var w in Selected.ToArray()) { try { w.ShowSelected(false); } catch { } }
            Selected.Clear();
        }

        /// <summary>True when every picked widget is locked (so the menu offers Unlock).</summary>
        public static bool AllSelectedLocked
        {
            get
            {
                Prune();
                if (Selected.Count == 0) return false;
                foreach (var w in Selected) if (!w.Cfg.Locked) return false;
                return true;
            }
        }

        /// <summary>Lock or unlock every picked widget in one go.</summary>
        public static void SetSelectedLocked(BarManager mgr, bool locked)
        {
            Prune();
            if (Selected.Count == 0) return;
            foreach (var w in Selected) w.Cfg.Locked = locked;
            mgr.SettingsChanged();
            mgr.WidgetsEditedOutsideSettings();
        }

        static void Prune() { Selected.RemoveAll(delegate (WidgetWindow w) { return !w.IsVisible || !w.Cfg.Enabled; }); }

        sealed class Item { public WidgetWindow W; public RECT R; }

        static List<Item> Items()
        {
            Prune();
            var list = new List<Item>();
            foreach (var w in Selected)
            {
                RECT r;
                if (w.TryVisibleRect(out r)) list.Add(new Item { W = w, R = r });
            }
            return list;
        }

        /// <summary>"left", "right", "top", "bottom", "centre" (same vertical line) or "middle" (same horizontal line).</summary>
        public static void Align(string how)
        {
            List<Item> items = Items();
            if (items.Count < 2) return;
            int minL = int.MaxValue, maxR = int.MinValue, minT = int.MaxValue, maxB = int.MinValue;
            foreach (var i in items)
            {
                minL = Math.Min(minL, i.R.Left); maxR = Math.Max(maxR, i.R.Right);
                minT = Math.Min(minT, i.R.Top); maxB = Math.Max(maxB, i.R.Bottom);
            }
            foreach (var i in items)
            {
                int l = i.R.Left, t = i.R.Top, w = i.R.Width, h = i.R.Height;
                switch (how)
                {
                    case "left": l = minL; break;
                    case "right": l = maxR - w; break;
                    case "top": t = minT; break;
                    case "bottom": t = maxB - h; break;
                    case "centre": l = (minL + maxR) / 2 - w / 2; break;
                    case "middle": t = (minT + maxB) / 2 - h / 2; break;
                }
                i.W.MoveVisibleTo(l, t, false);
            }
            if (_mgr != null) GroupMoved(_mgr, items);
        }

        /// <summary>Spread the selected widgets so the gaps between them are equal (the outer two stay), or exactly the snap gap.</summary>
        public static void Distribute(BarManager mgr, bool horizontal, bool useSnapGap)
        {
            List<Item> items = Items();
            if (items.Count < 2 || (!useSnapGap && items.Count < 3)) return;
            items.Sort(delegate (Item a, Item b) { return horizontal ? a.R.Left.CompareTo(b.R.Left) : a.R.Top.CompareTo(b.R.Top); });
            double scale = Math.Max(0.5, Native.GetDpiForWindow(items[0].W.NativeHandle) / 96.0);
            double gap;
            if (useSnapGap) gap = Math.Round(mgr.Settings.WidgetGap * scale);
            else
            {
                int span = horizontal ? items[items.Count - 1].R.Right - items[0].R.Left : items[items.Count - 1].R.Bottom - items[0].R.Top;
                int sum = 0;
                foreach (var i in items) sum += horizontal ? i.R.Width : i.R.Height;
                gap = (span - sum) / (double)(items.Count - 1);
            }
            double pos = horizontal ? items[0].R.Left : items[0].R.Top;
            foreach (var i in items)
            {
                if (horizontal) i.W.MoveVisibleTo((int)Math.Round(pos), i.R.Top, false);
                else i.W.MoveVisibleTo(i.R.Left, (int)Math.Round(pos), false);
                pos += (horizontal ? i.R.Width : i.R.Height) + gap;
            }
            GroupMoved(mgr, items);
        }

        /// <summary>Widgets moved by Align / Distribute: link them to what they now line up with; others let go of them.</summary>
        static void GroupMoved(BarManager mgr, List<Item> items)
        {
            var moved = new List<WidgetWindow>();
            foreach (var i in items) moved.Add(i.W);
            foreach (WidgetWindow o in mgr.WidgetWindows)
                if (!moved.Contains(o))
                    foreach (WidgetWindow w in moved) LetGo(o, w);
            foreach (WidgetWindow w in moved) Relink(mgr, w);
            mgr.WidgetMoved();
        }

        // =================================================================
        // Snap links
        // =================================================================
        sealed class LCand { public int Pos; public string Rel; public string To; public int Prio; }

        static double ScaleOf(WidgetWindow w) { return Math.Max(0.5, Native.GetDpiForWindow(w.NativeHandle) / 96.0); }

        static bool SameMonitor(MonitorInfo a, RECT r)
        {
            if (a == null) return true;
            MonitorInfo b = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            return b == null || string.Equals(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The boxes of the widgets on screen (inner = inside the outline; otherwise the outline included, as before 0.23).</summary>
        static Dictionary<WidgetWindow, RECT> Boxes(BarManager mgr, bool inner)
        {
            var d = new Dictionary<WidgetWindow, RECT>();
            foreach (WidgetWindow o in mgr.WidgetWindows)
            {
                if (!o.IsVisible || !o.Cfg.Enabled) continue;
                RECT r;
                if (!o.TryVisibleRect(out r)) continue;
                if (o.ExpandedHeightPx > 0) r.Bottom = r.Top + o.ExpandedHeightPx;      // collapsed: placed (and lined up with) as if open
                if (!inner) { int b = o.OutlinePx; r.Left -= b; r.Top -= b; r.Right += b; r.Bottom += b; }
                d[o] = r;
            }
            return d;
        }

        static bool LinksTo(SnapLink l, WidgetWindow w) { return l != null && l.ToWidget && l.To == w.Cfg.Id; }

        /// <summary>
        /// What a box at 'mine' could be linked to. upgrade = the first start after 0.23: only gap neighbours with an edge lined up (and that
        /// line-up), the screen edges and the bars - the widgets were placed by hand, so plain line-ups are likely to be chance.
        /// </summary>
        static void LinkCands(BarManager mgr, WidgetWindow me, RECT mine, Dictionary<WidgetWindow, RECT> boxes, bool upgrade, int tol,
                              List<LCand> xs, List<LCand> ys)
        {
            BarSettings s = mgr.Settings;
            double scale = ScaleOf(me);
            int gap = (int)Math.Round(s.WidgetGap * scale), edge = (int)Math.Round(s.WidgetEdgeGap * scale);
            int L = mine.Left, T = mine.Top, w = mine.Width, h = mine.Height;
            MonitorInfo mon = MonitorHelper.FromPoint(L + w / 2, T + h / 2);
            foreach (var kv in boxes)
            {
                WidgetWindow o = kv.Key;
                if (o == me) continue;
                RECT r = kv.Value;
                if (!SameMonitor(mon, r)) continue;
                string id = o.Cfg.Id;
                bool beside = T < r.Bottom + gap * 2 && T + h > r.Top - gap * 2;
                bool above = L < r.Right + gap * 2 && L + w > r.Left - gap * 2;
                // never link both ways on one axis (each would hold the other in place)
                bool xFree = !LinksTo(o.Cfg.LinkX, me), yFree = !LinksTo(o.Cfg.LinkY, me);
                bool linedX = Math.Abs(L - r.Left) <= tol || Math.Abs(L + w - r.Right) <= tol || Math.Abs(L + w / 2 - (r.Left + r.Right) / 2) <= tol;
                bool linedY = Math.Abs(T - r.Top) <= tol || Math.Abs(T + h - r.Bottom) <= tol || Math.Abs(T + h / 2 - (r.Top + r.Bottom) / 2) <= tol;
                bool gapX = beside && (Math.Abs(L - (r.Right + gap)) <= tol || Math.Abs(L - (r.Left - gap - w)) <= tol);
                bool gapY = above && (Math.Abs(T - (r.Bottom + gap)) <= tol || Math.Abs(T - (r.Top - gap - h)) <= tol);
                bool alignX = !upgrade || gapY, alignY = !upgrade || gapX;
                if (xFree)
                {
                    if (beside && (!upgrade || linedY))
                    {
                        xs.Add(new LCand { Pos = r.Right + gap, Rel = "RightOf", To = id, Prio = 5 });
                        xs.Add(new LCand { Pos = r.Left - gap - w, Rel = "LeftOf", To = id, Prio = 4 });
                    }
                    if (alignX)
                    {
                        xs.Add(new LCand { Pos = r.Left, Rel = "AlignLeft", To = id, Prio = 2 });
                        xs.Add(new LCand { Pos = r.Right - w, Rel = "AlignRight", To = id, Prio = 1 });
                        xs.Add(new LCand { Pos = (r.Left + r.Right) / 2 - w / 2, Rel = "AlignCentre", To = id, Prio = 0 });
                    }
                }
                if (yFree)
                {
                    if (above && (!upgrade || linedX))
                    {
                        ys.Add(new LCand { Pos = r.Bottom + gap, Rel = "Below", To = id, Prio = 5 });
                        ys.Add(new LCand { Pos = r.Top - gap - h, Rel = "Above", To = id, Prio = 4 });
                    }
                    if (alignY)
                    {
                        ys.Add(new LCand { Pos = r.Top, Rel = "AlignTop", To = id, Prio = 2 });
                        ys.Add(new LCand { Pos = r.Bottom - h, Rel = "AlignBottom", To = id, Prio = 1 });
                        ys.Add(new LCand { Pos = (r.Top + r.Bottom) / 2 - h / 2, Rel = "AlignMiddle", To = id, Prio = 0 });
                    }
                }
            }
            if (mon == null) return;
            RECT m = mon.Bounds;
            xs.Add(new LCand { Pos = m.Left + edge, Rel = "EdgeLeft", To = "", Prio = 3 });
            xs.Add(new LCand { Pos = m.Right - edge - w, Rel = "EdgeRight", To = "", Prio = 3 });
            ys.Add(new LCand { Pos = m.Top + edge, Rel = "EdgeTop", To = "", Prio = 3 });
            ys.Add(new LCand { Pos = m.Bottom - edge - h, Rel = "EdgeBottom", To = "", Prio = 3 });
            if (!upgrade)
            {
                xs.Add(new LCand { Pos = (m.Left + m.Right) / 2 - w / 2, Rel = "ScreenCentre", To = "", Prio = 0 });
                ys.Add(new LCand { Pos = (m.Top + m.Bottom) / 2 - h / 2, Rel = "ScreenMiddle", To = "", Prio = 0 });
            }
            foreach (BarWindow b in mgr.BarWindows)
            {
                RECT br = b.BarRect;
                if (br.Width <= 0 || br.Right <= m.Left || br.Left >= m.Right || br.Bottom <= m.Top || br.Top >= m.Bottom) continue;
                if ((br.Top + br.Bottom) / 2 < (m.Top + m.Bottom) / 2) ys.Add(new LCand { Pos = br.Bottom + edge, Rel = "BarBelow", To = b.Placement.Id, Prio = 3 });
                else ys.Add(new LCand { Pos = br.Top - edge - h, Rel = "BarAbove", To = b.Placement.Id, Prio = 3 });
            }
        }

        static LCand Pick(List<LCand> list, int current, int tol)
        {
            LCand best = null;
            int bestDist = int.MaxValue;
            foreach (LCand c in list)
            {
                int d = Math.Abs(c.Pos - current);
                if (d > tol) continue;
                if (best == null || d < bestDist || (d == bestDist && c.Prio > best.Prio)) { best = c; bestDist = d; }
            }
            return best;
        }

        static BarWindow BarById(BarManager mgr, string id)
        {
            foreach (BarWindow b in mgr.BarWindows) if (b.Placement != null && b.Placement.Id == id) return b;
            return null;
        }

        static WidgetWindow WidgetById(Dictionary<WidgetWindow, RECT> boxes, string id, out RECT r)
        {
            foreach (var kv in boxes) if (kv.Key.Cfg.Id == id) { r = kv.Value; return kv.Key; }
            r = new RECT();
            return null;
        }

        /// <summary>A link for the chosen candidate, its gap measured between the boxes as they are now (DIPs).</summary>
        static SnapLink MakeLink(BarManager mgr, LCand c, RECT mine, Dictionary<WidgetWindow, RECT> boxes, MonitorInfo mon, double scale)
        {
            if (c == null) return null;
            var link = new SnapLink { Rel = c.Rel, To = c.To ?? "" };
            int px = 0;
            RECT t;
            switch (c.Rel)
            {
                case "RightOf": if (WidgetById(boxes, c.To, out t) != null) px = mine.Left - t.Right; break;
                case "LeftOf": if (WidgetById(boxes, c.To, out t) != null) px = t.Left - mine.Right; break;
                case "Below": if (WidgetById(boxes, c.To, out t) != null) px = mine.Top - t.Bottom; break;
                case "Above": if (WidgetById(boxes, c.To, out t) != null) px = t.Top - mine.Bottom; break;
                case "EdgeLeft": if (mon != null) px = mine.Left - mon.Bounds.Left; break;
                case "EdgeRight": if (mon != null) px = mon.Bounds.Right - mine.Right; break;
                case "EdgeTop": if (mon != null) px = mine.Top - mon.Bounds.Top; break;
                case "EdgeBottom": if (mon != null) px = mon.Bounds.Bottom - mine.Bottom; break;
                case "BarBelow": { BarWindow b = BarById(mgr, c.To); if (b != null) px = mine.Top - b.BarRect.Bottom; break; }
                case "BarAbove": { BarWindow b = BarById(mgr, c.To); if (b != null) px = b.BarRect.Top - mine.Bottom; break; }
            }
            link.Gap = (int)Math.Round(px / scale);
            return link;
        }

        /// <summary>Link a widget to what its box lines up with exactly right now (after a snapped drop, a resize, Align / Distribute).</summary>
        public static void Relink(BarManager mgr, WidgetWindow me)
        {
            if (!mgr.Settings.WidgetSnap) { me.Cfg.LinkX = null; me.Cfg.LinkY = null; return; }
            Dictionary<WidgetWindow, RECT> boxes = Boxes(mgr, true);
            RECT mine;
            if (!boxes.TryGetValue(me, out mine)) return;
            var xs = new List<LCand>();
            var ys = new List<LCand>();
            LinkCands(mgr, me, mine, boxes, false, 1, xs, ys);
            double scale = ScaleOf(me);
            MonitorInfo mon = MonitorHelper.FromPoint((mine.Left + mine.Right) / 2, (mine.Top + mine.Bottom) / 2);
            me.Cfg.LinkX = MakeLink(mgr, Pick(xs, mine.Left, 1), mine, boxes, mon, scale);
            me.Cfg.LinkY = MakeLink(mgr, Pick(ys, mine.Top, 1), mine, boxes, mon, scale);
        }

        /// <summary>o stops being linked to w (w was moved away).</summary>
        static void LetGo(WidgetWindow o, WidgetWindow w)
        {
            if (LinksTo(o.Cfg.LinkX, w)) o.Cfg.LinkX = null;
            if (LinksTo(o.Cfg.LinkY, w)) o.Cfg.LinkY = null;
        }

        /// <summary>A widget was dropped after a drag: link it to what it snapped to (nothing after an Alt drag); the widgets that were
        /// linked to it stay where they are and let go of it.</summary>
        // the picked widgets moving along with the one being dragged (null = a drag of one widget)
        static HashSet<WidgetWindow> _moving;

        /// <summary>
        /// The widgets that move along when me is dragged: the other picked (Ctrl+click) widgets, when me is one of them - not the locked
        /// ones. Empty when me isn't picked or is the only one. Snapping leaves them out while they move.
        /// </summary>
        public static List<WidgetWindow> StartGroupDrag(WidgetWindow me)
        {
            var list = new List<WidgetWindow>();
            _moving = null;
            _parts = null;
            Prune();
            if (!Selected.Contains(me) || Selected.Count < 2) return list;
            foreach (var w in Selected) if (w != me && !w.Cfg.Locked) list.Add(w);
            _parts = null;
            if (list.Count > 0)
            {
                _moving = new HashSet<WidgetWindow>(list);
                // the group's outline and each widget's box, relative to the dragged one (for snapping the group as one)
                RECT mine;
                if (me.TryVisibleRect(out mine))
                {
                    var parts = new List<Part>();
                    int l = mine.Left, t = mine.Top, r = mine.Right, b = mine.Bottom;
                    parts.Add(new Part());      // the outline, filled in below
                    parts.Add(new Part { DX = 0, DY = 0, W = mine.Width, H = mine.Height });
                    foreach (WidgetWindow f in list)
                    {
                        RECT fr;
                        if (!f.TryVisibleRect(out fr)) continue;
                        parts.Add(new Part { DX = fr.Left - mine.Left, DY = fr.Top - mine.Top, W = fr.Width, H = fr.Height });
                        l = Math.Min(l, fr.Left); t = Math.Min(t, fr.Top); r = Math.Max(r, fr.Right); b = Math.Max(b, fr.Bottom);
                    }
                    parts[0] = new Part { DX = l - mine.Left, DY = t - mine.Top, W = r - l, H = b - t, Outer = true };
                    _parts = parts;
                }
            }
            return list;
        }

        /// <summary>
        /// A group drag was dropped: widgets outside the group that were linked to one in it let go; the followers keep the links among
        /// the group (they moved together) and drop the ones to widgets left behind; the dragged one links to what it snapped to.
        /// </summary>
        public static void DroppedGroup(BarManager mgr, WidgetWindow me, List<WidgetWindow> followers)
        {
            var group = new HashSet<WidgetWindow>(followers);
            group.Add(me);
            _moving = null;
            _parts = null;
            foreach (WidgetWindow o in mgr.WidgetWindows)
            {
                if (group.Contains(o)) continue;
                foreach (WidgetWindow g in group) LetGo(o, g);
            }
            if (_freeDrag)
            {
                // placed freely: keep the links inside the group, drop the ones to widgets left behind
                foreach (WidgetWindow g in group)
                    foreach (WidgetWindow o in mgr.WidgetWindows)
                        if (!group.Contains(o)) LetGo(g, o);
            }
            else
                foreach (WidgetWindow g in group) Relink(mgr, g);     // each links to what it now sits against (in the group or not)
        }

        /// <summary>A drag ended without moving: no group any more.</summary>
        public static void EndGroupDrag() { _moving = null; _parts = null; }

        public static void Dropped(BarManager mgr, WidgetWindow me)
        {
            foreach (WidgetWindow o in mgr.WidgetWindows) if (o != me) LetGo(o, me);
            if (_freeDrag) { me.Cfg.LinkX = null; me.Cfg.LinkY = null; }
            else Relink(mgr, me);
        }

        /// <summary>
        /// First start after 0.23: link the widgets that already sit at the snap gap from a neighbour with an edge lined up, or at the edge gap
        /// from the screen edge / a bar. Looked for on the boxes as they were (outline included) with a pixel or two of slack.
        /// </summary>
        public static void AutoLink(BarManager mgr)
        {
            Dictionary<WidgetWindow, RECT> outer = Boxes(mgr, false), inner = Boxes(mgr, true);
            int made = 0;
            foreach (var kv in outer)
            {
                WidgetWindow me = kv.Key;
                RECT mine = kv.Value, box;
                if (!inner.TryGetValue(me, out box)) continue;
                var xs = new List<LCand>();
                var ys = new List<LCand>();
                double scale = ScaleOf(me);
                int tol = Math.Max(1, (int)Math.Round(2 * scale));
                LinkCands(mgr, me, mine, outer, true, tol, xs, ys);
                MonitorInfo mon = MonitorHelper.FromPoint((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
                if (me.Cfg.LinkX == null) { me.Cfg.LinkX = MakeLink(mgr, Pick(xs, mine.Left, tol), box, inner, mon, scale); if (me.Cfg.LinkX != null) made++; }
                if (me.Cfg.LinkY == null) { me.Cfg.LinkY = MakeLink(mgr, Pick(ys, mine.Top, tol), box, inner, mon, scale); if (me.Cfg.LinkY != null) made++; }
            }
            Log.Write("Widgets: " + made + " snap links found on the first start after 0.23");
        }

        /// <summary>The snap gap changed: snapped widgets take the new gap (Relayout then moves them).</summary>
        public static void Respace(BarSettings s, bool widgetGap, bool edgeGap)
        {
            foreach (WidgetSettings w in s.Widgets)
                foreach (SnapLink l in new[] { w.LinkX, w.LinkY })
                {
                    if (l == null) continue;
                    if (widgetGap && l.UsesWidgetGap) l.Gap = s.WidgetGap;
                    if (edgeGap && l.UsesEdgeGap) l.Gap = s.WidgetEdgeGap;
                }
        }

        /// <summary>
        /// Put every linked widget where its links say (the widgets they are linked to first, so chains follow), kept on its monitor.
        /// Returns false when it could not run now (a widget is being dragged or resized): try again a moment later.
        /// </summary>
        public static bool Relayout(BarManager mgr)
        {
            if (!mgr.Settings.WidgetSnap) return true;
            foreach (WidgetWindow w in mgr.WidgetWindows) if (w.IsDragging || w.Resizing) return false;
            Dictionary<WidgetWindow, RECT> boxes = Boxes(mgr, true);
            var doneX = new Dictionary<WidgetWindow, int>();
            var doneY = new Dictionary<WidgetWindow, int>();
            var busy = new HashSet<string>();
            int moved = 0;
            foreach (var kv in boxes)
            {
                WidgetWindow w = kv.Key;
                if (w.Cfg.LinkX == null && w.Cfg.LinkY == null) continue;
                if (w.Displaced) continue;          // its monitor is away: leave its links for when it's back
                int x = Resolve(mgr, w, true, boxes, doneX, busy), y = Resolve(mgr, w, false, boxes, doneY, busy);
                if (x != kv.Value.Left || y != kv.Value.Top) { w.MoveVisibleTo(x, y, false); moved++; }
            }
            if (moved > 0) mgr.WidgetMoved();
            return true;
        }

        static int Resolve(BarManager mgr, WidgetWindow w, bool across, Dictionary<WidgetWindow, RECT> boxes, Dictionary<WidgetWindow, int> done, HashSet<string> busy)
        {
            int v;
            if (done.TryGetValue(w, out v)) return v;
            RECT r = boxes[w];
            int cur = across ? r.Left : r.Top;
            string key = (across ? "x" : "y") + w.Cfg.Id;
            if (busy.Contains(key)) return cur;          // a loop of links: leave it where it is
            // collapsed and not seen open since start-up: its open height isn't known, so its top stays where it is
            if (!across && w.Cfg.IsCollapsed && w.ExpandedHeightPx <= 0) { done[w] = cur; return cur; }
            busy.Add(key);
            int pos = cur;
            SnapLink link = across ? w.Cfg.LinkX : w.Cfg.LinkY;
            int size = across ? r.Width : r.Height;
            MonitorInfo mon = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            if (link != null)
            {
                double scale = ScaleOf(w);
                int gap = (int)Math.Round(link.Gap * scale);
                if (link.ToWidget)
                {
                    RECT t;
                    WidgetWindow o = WidgetById(boxes, link.To, out t);
                    if (o != null && SameMonitor(mon, t))
                    {
                        int tp = Resolve(mgr, o, across, boxes, done, busy);
                        int ts = across ? t.Width : t.Height;
                        switch (link.Rel)
                        {
                            case "RightOf": case "Below": pos = tp + ts + gap; break;
                            case "LeftOf": case "Above": pos = tp - gap - size; break;
                            case "AlignLeft": case "AlignTop": pos = tp; break;
                            case "AlignRight": case "AlignBottom": pos = tp + ts - size; break;
                            case "AlignCentre": case "AlignMiddle": pos = tp + ts / 2 - size / 2; break;
                        }
                    }
                }
                else if (mon != null)
                {
                    RECT m = mon.Bounds;
                    switch (link.Rel)
                    {
                        case "EdgeLeft": pos = m.Left + gap; break;
                        case "EdgeRight": pos = m.Right - gap - size; break;
                        case "EdgeTop": pos = m.Top + gap; break;
                        case "EdgeBottom": pos = m.Bottom - gap - size; break;
                        case "ScreenCentre": pos = (m.Left + m.Right) / 2 - size / 2; break;
                        case "ScreenMiddle": pos = (m.Top + m.Bottom) / 2 - size / 2; break;
                        case "BarBelow": { BarWindow b = BarById(mgr, link.To); if (b != null && b.BarRect.Width > 0) pos = b.BarRect.Bottom + gap; break; }
                        case "BarAbove": { BarWindow b = BarById(mgr, link.To); if (b != null && b.BarRect.Width > 0) pos = b.BarRect.Top - gap - size; break; }
                    }
                }
            }
            // never pushed off the screen
            if (mon != null)
            {
                int lo = across ? mon.Bounds.Left : mon.Bounds.Top, hi = (across ? mon.Bounds.Right : mon.Bounds.Bottom) - size;
                pos = Math.Max(lo, Math.Min(Math.Max(lo, hi), pos));
            }
            busy.Remove(key);
            done[w] = pos;
            return pos;
        }
    }
}
