using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace vShell.Ui
{
    /// <summary>
    /// Icons sliding to their new place after you drag one somewhere else (taskbar pins, launcher shortcuts, tray icons).
    /// A drop changes the settings, and the bar is then built again with new icons, so: just before the change, Remember where every
    /// icon of that module is on screen (by key); after each rebuild, Play moves each icon back to where it was and lets it glide to
    /// its new place. A rebuild during the glide carries on from where it had got to.
    /// The same 250 ms ease-out as the taskbar's buttons.
    /// </summary>
    public static class ReorderFx
    {
        public const int Millis = 250;

        /// <summary>Which icon this is, across rebuilds (an app, a shortcut, a tray icon).</summary>
        public static readonly DependencyProperty KeyProperty =
            DependencyProperty.RegisterAttached("ReorderKey", typeof(string), typeof(ReorderFx), new PropertyMetadata(null));
        public static void SetKey(UIElement e, string key) { if (e != null) e.SetValue(KeyProperty, key); }
        public static string GetKey(UIElement e) { return e == null ? null : e.GetValue(KeyProperty) as string; }

        static readonly DependencyProperty MoveProperty =
            DependencyProperty.RegisterAttached("ReorderMove", typeof(TranslateTransform), typeof(ReorderFx), new PropertyMetadata(null));

        sealed class Memo
        {
            public Dictionary<string, Point> Pos;      // screen, device px
            public DateTime At;
            public DateTime? Played;
        }
        static readonly Dictionary<string, Memo> _memo = new Dictionary<string, Memo>();

        /// <summary>Note where this module's icons are now (scope = the module's id). Call just before changing their order.</summary>
        public static void Remember(string scope, Panel panel)
        {
            if (string.IsNullOrEmpty(scope) || panel == null) return;
            var pos = new Dictionary<string, Point>();
            foreach (UIElement c in panel.Children)
            {
                string k = GetKey(c);
                Point p;
                if (k == null || pos.ContainsKey(k) || !ScreenPos(c, out p)) continue;
                pos[k] = p;
            }
            _memo[scope] = new Memo { Pos = pos, At = DateTime.UtcNow };
        }

        /// <summary>A move is waiting to be shown (or is being shown) for this module.</summary>
        public static bool Pending(string scope)
        {
            Memo m;
            if (string.IsNullOrEmpty(scope) || !_memo.TryGetValue(scope, out m)) return false;
            if (Expired(m)) { _memo.Remove(scope); return false; }
            return true;
        }

        static bool Expired(Memo m)
        {
            DateTime now = DateTime.UtcNow;
            if (m.Played.HasValue) return (now - m.Played.Value).TotalMilliseconds >= Millis;
            return (now - m.At).TotalMilliseconds > 3000;      // never got shown
        }

        /// <summary>After a rebuild has been laid out: glide the icons from where they were to where they are now.</summary>
        public static void Play(string scope, Panel panel)
        {
            Memo m;
            if (string.IsNullOrEmpty(scope) || panel == null || !_memo.TryGetValue(scope, out m)) return;
            if (Expired(m)) { _memo.Remove(scope); return; }
            PresentationSource src = PresentationSource.FromVisual(panel);
            if (src == null || src.CompositionTarget == null) return;          // not on screen yet: the next rebuild tries again
            DateTime now = DateTime.UtcNow;
            if (!m.Played.HasValue) m.Played = now;
            double p = Math.Min(1, (now - m.Played.Value).TotalMilliseconds / Millis);
            double left = 1 - (1 - Math.Pow(1 - p, 3));                       // how much of the way is still to go
            var dur = TimeSpan.FromMilliseconds(Math.Max(16, Millis * (1 - p)));
            Matrix fromDevice = src.CompositionTarget.TransformFromDevice;

            foreach (UIElement c in panel.Children)
            {
                string k = GetKey(c);
                Point was;
                if (k == null || !m.Pos.TryGetValue(k, out was)) continue;
                TranslateTransform tt = MoveOf(c);
                if (tt == null) continue;
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.X = 0; tt.Y = 0;
                Point isNow;
                if (!ScreenPos(c, out isNow)) continue;
                Vector d = fromDevice.Transform(was - isNow) * left;
                if (Math.Abs(d.X) < 0.5 && Math.Abs(d.Y) < 0.5) continue;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                if (Math.Abs(d.X) >= 0.5) tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(d.X, 0, dur) { EasingFunction = ease });
                if (Math.Abs(d.Y) >= 0.5) tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(d.Y, 0, dur) { EasingFunction = ease });
            }
        }

        /// <summary>Play once the panel has been laid out (call at the end of a module's Rebuild).</summary>
        public static void PlaySoon(string scope, Panel panel)
        {
            if (!Pending(scope)) return;
            panel.Dispatcher.BeginInvoke(new Action(delegate { Play(scope, panel); }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>The element's own sliding offset, added alongside any transform it already has (the Dock's grow / lift).</summary>
        static TranslateTransform MoveOf(UIElement c)
        {
            var tt = c.GetValue(MoveProperty) as TranslateTransform;
            if (tt != null) return tt;
            tt = new TranslateTransform();
            Transform cur = c.RenderTransform;
            var group = cur as TransformGroup;
            if (cur == null || cur == Transform.Identity || (cur.Value.IsIdentity && !(cur is TransformGroup))) c.RenderTransform = tt;
            else if (group != null && !group.IsFrozen) group.Children.Add(tt);
            else
            {
                var g = new TransformGroup();
                g.Children.Add(cur);
                g.Children.Add(tt);
                c.RenderTransform = g;
            }
            c.SetValue(MoveProperty, tt);
            return tt;
        }

        static bool ScreenPos(UIElement c, out Point p)
        {
            p = new Point();
            var fe = c as FrameworkElement;
            if (fe == null || fe.ActualWidth <= 0 || PresentationSource.FromVisual(fe) == null) return false;
            try { p = fe.PointToScreen(new Point(0, 0)); return true; }
            catch { return false; }
        }
    }
}
