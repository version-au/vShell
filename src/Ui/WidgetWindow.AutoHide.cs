using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Slide away at the screen edge: a widget snapped to a screen edge can slide off that edge when the mouse leaves it and slide
    /// back when the mouse is pushed against the edge in front of it. Slide / Fade / Slide and fade, with the taskbar line's bounce
    /// and speed settings. The window stays where it is - only its content moves, clipped by the window, so it never shows up on a
    /// monitor next door - and a hidden widget is see-through, so clicks go to whatever is under it.
    /// While it is out it sits above other windows (even a widget that normally lives on the desktop), and it stays out while you
    /// use it: dragging, resizing, typing in it, its right-click menu or one of its popups open.
    /// </summary>
    public sealed partial class WidgetWindow
    {
        readonly TranslateTransform _hideMove = new TranslateTransform();
        /// <summary>The window's content: it holds the widget (_outer) and is what slides / fades.</summary>
        readonly Border _slideHost = new Border();

        DispatcherTimer _hideTick;
        bool _hidden, _peekTop, _menuOpen;

        /// <summary>Slid away at its screen edge right now (dragging another widget doesn't line up with it).</summary>
        public bool SlidAway { get { return _hidden; } }
        EdgeGlow _edgeGlow;
        DateTime _mouseLeftAt = DateTime.UtcNow;
        DateTime _outUntil = DateTime.MinValue;

        /// <summary>Settings > Show on screen: out (if it slid away) and staying out for this long, wherever the mouse is.</summary>
        public void ComeOut(int ms)
        {
            _outUntil = DateTime.UtcNow.AddMilliseconds(ms);
            if (_hidden) Reveal(true);
        }

        /// <summary>
        /// The ways this widget can slide away: towards each screen edge it is snapped to ("Left", "Right", "Up", "Down"). Snapped under a
        /// top bar / over a bottom bar counts as that edge - it slides away towards the bar. So does an edge / bar it sits against while
        /// snapped to other widgets (a widget in a row along the top of the screen).
        /// </summary>
        public List<string> HideWays()
        {
            var l = new List<string>();
            if (Cfg.LinkX != null)
            {
                if (Cfg.LinkX.Rel == "EdgeLeft") l.Add("Left");
                else if (Cfg.LinkX.Rel == "EdgeRight") l.Add("Right");
            }
            if (Cfg.LinkY != null)
            {
                if (Cfg.LinkY.Rel == "EdgeTop" || Cfg.LinkY.Rel == "BarBelow") l.Add("Up");
                else if (Cfg.LinkY.Rel == "EdgeBottom" || Cfg.LinkY.Rel == "BarAbove") l.Add("Down");
            }
            AddTouchedEdges(l);
            return l;
        }

        /// <summary>The screen edges (or the bars along them) the widget's box is against, within the edge gap and a little slack.</summary>
        void AddTouchedEdges(List<string> l)
        {
            RECT vis;
            if (!TryVisibleRect(out vis)) return;
            MonitorInfo mon = MonitorHelper.FromPoint((vis.Left + vis.Right) / 2, (vis.Top + vis.Bottom) / 2);
            if (mon == null) return;
            double scale = Math.Max(0.5, Native.GetDpiForWindow(Hwnd) / 96.0);
            int near = (int)Math.Round((Math.Max(0, Mgr.Settings.WidgetEdgeGap) + 8) * scale);
            RECT m = mon.Bounds;
            int top = m.Top, bottom = m.Bottom;
            foreach (BarWindow b in Mgr.BarWindows)
            {
                RECT br = b.BarRect;
                if (br.Width <= 0 || br.Right <= vis.Left || br.Left >= vis.Right || br.Bottom <= m.Top || br.Top >= m.Bottom) continue;
                if ((br.Top + br.Bottom) / 2 < (m.Top + m.Bottom) / 2) top = Math.Max(top, br.Bottom);
                else bottom = Math.Min(bottom, br.Top);
            }
            if (vis.Left - m.Left <= near && !l.Contains("Left")) l.Add("Left");
            if (m.Right - vis.Right <= near && !l.Contains("Right")) l.Add("Right");
            if (vis.Top - top <= near && !l.Contains("Up")) l.Add("Up");
            if (bottom - vis.Bottom <= near && !l.Contains("Down")) l.Add("Down");
        }

        /// <summary>The way it slides: the edge it was set to slide into, while it is still snapped to that edge (otherwise null).</summary>
        string HideWay()
        {
            List<string> ways = HideWays();
            if (ways.Count == 0) return null;
            if (string.IsNullOrEmpty(Cfg.AutoHideWay)) return ways[0];      // turned on in Settings without picking: the first edge
            return ways.Contains(Cfg.AutoHideWay) ? Cfg.AutoHideWay : null;
        }

        public static string EdgeName(string way)
        {
            switch (way)
            {
                case "Left": return "left";
                case "Right": return "right";
                case "Up": return "top";
                default: return "bottom";
            }
        }

        /// <summary>Start / stop watching the mouse (after the settings changed).</summary>
        void SetupAutoHide()
        {
            if (_slideHost.RenderTransform != _hideMove) _slideHost.RenderTransform = _hideMove;
            bool on = Cfg.AutoHide;     // the tick turns it off if the widget is no longer at its edge
            if (on)
            {
                if (_hideTick == null)
                {
                    _hideTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                    _hideTick.Tick += delegate { HideTick(); };
                    Closed += delegate { _hideTick.Stop(); if (_edgeGlow != null) { _edgeGlow.Close(); _edgeGlow = null; } };
                }
                _mouseLeftAt = DateTime.UtcNow;
                _hideTick.Start();
            }
            else
            {
                if (_hideTick != null) _hideTick.Stop();
                if (_edgeGlow != null) _edgeGlow.Fade();
                // back exactly as it was: in place, fully visible, on its usual layer
                Reveal(false);
                bool wasPeek = _peekTop;
                _peekTop = false;
                AlwaysOnTop = Cfg.AlwaysOnTop;
                if (wasPeek) PlaceZ();
            }
        }

        /// <summary>Turn sliding away on / off from the right-click menu (way = the edge picked, null = keep).</summary>
        void SetAutoHide(bool on, string way)
        {
            Cfg.AutoHide = on;
            if (way != null) Cfg.AutoHideWay = way;
            SetupAutoHide();
            Mgr.SettingsChanged();
            Mgr.WidgetsEditedOutsideSettings();
        }

        /// <summary>The right-click menu's items for it.</summary>
        void AddAutoHideItems(ContextMenu m)
        {
            List<string> ways = HideWays();
            if (Cfg.AutoHide && ways.Count > 0)
            {
                if (ways.Count > 1)
                {
                    var which = new MenuItem { Header = "Slides away" };
                    foreach (string w in ways)
                    {
                        string ww = w;
                        var mi = AddSub(which, w + "  (" + EdgeName(w) + " edge)", delegate { SetAutoHide(true, ww); });
                        mi.IsCheckable = true;
                        mi.IsChecked = ww == HideWay();
                    }
                    m.Items.Add(which);
                }
                AddItem(m, "Stop sliding away", delegate { SetAutoHide(false, null); });
                return;
            }
            if (ways.Count == 0)
            {
                var off = new MenuItem
                {
                    Header = "Slide away at the screen edge", IsEnabled = false,
                    ToolTip = "Put the widget against a screen edge or a bar first (drag it there)"
                };
                ToolTipService.SetShowOnDisabled(off, true);
                m.Items.Add(off);
                return;
            }
            if (ways.Count == 1)
            {
                string w0 = ways[0];
                AddItem(m, "Slide away at the " + EdgeName(w0) + " edge", delegate { SetAutoHide(true, w0); });
                return;
            }
            // snapped into a corner: which way?
            var pick = new MenuItem { Header = "Slide away at the screen edge" };
            pick.Items.Add(new MenuItem { Header = "Which way should it slide?", IsEnabled = false });
            foreach (string w in ways)
            {
                string ww = w;
                AddSub(pick, w + "  (into the " + EdgeName(w) + " edge)", delegate { SetAutoHide(true, ww); });
            }
            m.Items.Add(pick);
        }

        /// <summary>Its right-click menu or a popup of its own is open: don't re-stack it (sliding away keeps it on top while it's out,
        /// and putting it on top again covered the menu / made it close - the menu that flashed and went away).</summary>
        protected override bool HoldZ { get { return _menuOpen || Mgr.PopupOpenFor(this); } }

        /// <summary>Something is being done with the widget: don't slide it away now.</summary>
        bool InUse()
        {
            return _resizing || _sizing || Dragging || HasKeyboard || _menuOpen || Mgr.PopupOpenFor(this) || _box.IsMouseCaptured || DateTime.UtcNow < _outUntil;
        }

        void HideTick()
        {
            if (Hwnd == IntPtr.Zero || !IsVisible) return;
            if (!Cfg.AutoHide) { SetupAutoHide(); return; }
            if (Dragging || _resizing || _sizing) { _mouseLeftAt = DateTime.UtcNow; return; }     // wait until it's put down
            string way = HideWay();
            if (way == null)
            {
                // moved away from the edge it slid into (or onto a different edge): sliding away turns off; turn it on again from the
                // right-click menu at its new edge
                Cfg.AutoHideWay = "";
                SetAutoHide(false, null);
                return;
            }
            if (string.IsNullOrEmpty(Cfg.AutoHideWay)) Cfg.AutoHideWay = way;
            RECT vis;
            if (!TryVisibleRect(out vis)) return;
            MonitorInfo mon = MonitorHelper.FromPoint((vis.Left + vis.Right) / 2, (vis.Top + vis.Bottom) / 2);
            if (mon == null) return;
            POINT c = Native.CursorPos();
            DateTime now = DateTime.UtcNow;
            UpdateGlow(vis, mon, way);

            if (_hidden)
            {
                if (AtEdge(c, vis, mon.Bounds, way)) Reveal(true);
                return;
            }
            if (InUse() || InKeep(c, vis, mon.Bounds, way)) { _mouseLeftAt = now; return; }
            if ((now - _mouseLeftAt).TotalMilliseconds >= Math.Max(0, Cfg.AutoHideDelay)) Conceal();
        }

        /// <summary>The accent glow at the screen edge while the widget is away (not over a full-screen app).</summary>
        void UpdateGlow(RECT vis, MonitorInfo mon, string way)
        {
            bool want = _hidden && Cfg.AutoHideGlow && !Mgr.FullScreenActive;
            if (!want) { if (_edgeGlow != null) _edgeGlow.Fade(); return; }
            int th = Math.Max(3, (int)Math.Round(6 * MonitorHelper.ScaleOf(mon)));
            RECT m = mon.Bounds, r = new RECT();
            switch (way)
            {
                case "Left": r.Left = m.Left; r.Right = m.Left + th; r.Top = vis.Top; r.Bottom = vis.Bottom; break;
                case "Right": r.Left = m.Right - th; r.Right = m.Right; r.Top = vis.Top; r.Bottom = vis.Bottom; break;
                case "Up": r.Top = m.Top; r.Bottom = m.Top + th; r.Left = vis.Left; r.Right = vis.Right; break;
                default: r.Top = m.Bottom - th; r.Bottom = m.Bottom; r.Left = vis.Left; r.Right = vis.Right; break;
            }
            if (_edgeGlow == null) _edgeGlow = new EdgeGlow();
            _edgeGlow.ShowAt(r, way, Theme.MainAccent.Color);
        }

        /// <summary>The mouse is pushed against the screen edge, in front of the widget.</summary>
        static bool AtEdge(POINT c, RECT vis, RECT m, string way)
        {
            switch (way)
            {
                case "Left": return c.X <= m.Left + 1 && c.Y >= vis.Top && c.Y <= vis.Bottom;
                case "Right": return c.X >= m.Right - 2 && c.Y >= vis.Top && c.Y <= vis.Bottom;
                case "Up": return c.Y <= m.Top + 1 && c.X >= vis.Left && c.X <= vis.Right;
                default: return c.Y >= m.Bottom - 2 && c.X >= vis.Left && c.X <= vis.Right;
            }
        }

        /// <summary>The mouse is over the widget, or between it and its edge (a little slack all round).</summary>
        static bool InKeep(POINT c, RECT vis, RECT m, string way)
        {
            int l = vis.Left, t = vis.Top, r = vis.Right, b = vis.Bottom;
            switch (way)
            {
                case "Left": l = m.Left; break;
                case "Right": r = m.Right; break;
                case "Up": t = m.Top; break;
                default: b = m.Bottom; break;
            }
            const int slack = 10;
            return c.X >= l - slack && c.X <= r + slack && c.Y >= t - slack && c.Y <= b + slack;
        }

        IEasingFunction RevealEase()
        {
            switch (Cfg.AutoHideBounce)
            {
                case "Soft": return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
                case "Springy": return new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 7 };
                default: return new CubicEase { EasingMode = EasingMode.EaseOut };
            }
        }

        /// <summary>Slide / fade away.</summary>
        void Conceal()
        {
            string way = HideWay();
            if (way == null || _hidden) return;
            _hidden = true;
            _slideHost.IsHitTestVisible = false;
            var dur = new Duration(TimeSpan.FromMilliseconds(Math.Max(60, Cfg.AutoHideMs)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            bool slide = Cfg.AutoHideAnim != "Fade", fade = Cfg.AutoHideAnim != "Slide";
            double dx = 0, dy = 0;
            double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
            if (way == "Left") dx = -w; else if (way == "Right") dx = w; else if (way == "Up") dy = -h; else dy = h;
            var a = new DoubleAnimation(slide ? dx : 0, dur) { EasingFunction = ease };
            a.Completed += delegate
            {
                if (!_hidden) return;
                // fully away: back to its usual layer (behind your windows, unless it is always on top)
                _peekTop = false;
                AlwaysOnTop = Cfg.AlwaysOnTop;
                PlaceZ();
            };
            _hideMove.BeginAnimation(TranslateTransform.XProperty, a);
            _hideMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(slide ? dy : 0, dur) { EasingFunction = ease });
            if (fade) _slideHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            else { _slideHost.BeginAnimation(OpacityProperty, null); _slideHost.Opacity = 1; }
        }

        /// <summary>Slide / fade back in (animate = false: straight back, when sliding away is turned off).</summary>
        void Reveal(bool animate)
        {
            if (!animate && !_hidden && !_peekTop && _hideMove.X == 0 && _hideMove.Y == 0 && _slideHost.Opacity >= 1 && _slideHost.IsHitTestVisible) return;
            _hidden = false;
            _slideHost.IsHitTestVisible = true;
            _mouseLeftAt = DateTime.UtcNow;
            if (!animate)
            {
                _hideMove.BeginAnimation(TranslateTransform.XProperty, null);
                _hideMove.BeginAnimation(TranslateTransform.YProperty, null);
                _slideHost.BeginAnimation(OpacityProperty, null);
                _hideMove.X = _hideMove.Y = 0;
                _slideHost.Opacity = 1;
                return;
            }
            // out in front of your windows while it is out
            if (!AlwaysOnTop) { _peekTop = true; AlwaysOnTop = true; PlaceZ(); }
            var dur = new Duration(TimeSpan.FromMilliseconds(Math.Max(60, Cfg.AutoHideMs)));
            bool slide = Cfg.AutoHideAnim != "Fade";
            // fading back in has no overshoot to show; a slide uses the bounce
            _hideMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur) { EasingFunction = slide ? RevealEase() : null });
            _hideMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = slide ? RevealEase() : null });
            _slideHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
