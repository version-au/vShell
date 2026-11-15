using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Radarr: the queue and what's coming. On a bar an icon with the number in the queue as a badge (and how far the downloads are) -
    /// click it for the popup. In a widget the views themselves: Queue, Upcoming, Recently added and Missing (with a Search button).
    /// </summary>
    public sealed class RadarrModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public RadarrModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = host.IsWidget ? new Thickness(4, 2, 4, 2) : new Thickness(6, 0, 6, 0);
            VerticalAlignment = VerticalAlignment.Center;
            if (!host.IsWidget)
            {
                Cursor = Cursors.Hand;
                MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            }
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(false); };
            Loaded += delegate { Refresh(true); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh(true);
        }

        void Refresh(bool force)
        {
            if (RadarrList.MenuOpen) return;
            bool configured = RadarrClient.Configured(_mgr.Settings);
            RaView v = RaView.Of(_cfg);
            if (configured) RadarrClient.WantDays(v.Days);
            RadarrSnapshot s = configured ? RadarrClient.Latest : new RadarrSnapshot();
            string key = (configured ? "c" : "n") + "|" + s.Fetched.Ticks + "|" + s.Error + "|" + RadarrList.Version + "|" + RadarrPosters.Version + "|" + v.Tab + "|" +
                         string.Join(",", v.TabsShown) + "|" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            if (!force && key == _shownKey) return;
            _shownKey = key;
            if (_host.IsWidget) Child = RadarrList.Build(_mgr, _t, _cfg, s, true);
            else
            {
                Child = BuildBar(s, configured);
                if (_popup != null && _popup.IsOpen) _popup.SetContent(RadarrList.Build(_mgr, _t, _cfg, s, false));
            }
            _host.ContentChanged();
        }

        FrameworkElement BuildBar(RadarrSnapshot s, bool configured)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b)
            {
                return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            };
            Visibility = Visibility.Visible;
            if (!configured) { row.Children.Add(T("Set up Radarr in Settings > Plugins", _t.FgDim)); ToolTip = null; return row; }

            RaView v = RaView.Of(_cfg);
            int queued = s.Queue.Count, problems = s.Problems;
            bool healthBad = v.Health && s.Health.Exists(delegate (RadarrHealth h) { return h.Error; });
            if (v.HideIdle && s.Ok && queued == 0 && !healthBad && s.Error.Length == 0) { Visibility = Visibility.Collapsed; return row; }

            // the icon, with the number in the queue as a badge (red when one needs a look)
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            icon.Children.Add(new TextBlock
            {
                Text = RadarrList.G(RadarrList.Glyph), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.1, VerticalAlignment = VerticalAlignment.Center,
                Foreground = queued > 0 ? _t.Fg : _t.FgDim, Margin = new Thickness(0, 0, v.Badge && queued > 0 ? 6 : 0, 0)
            });
            if (v.Badge && (queued > 0 || healthBad))
            {
                double fs = Math.Max(7, _t.FontSize * 0.62);
                Brush bg = problems > 0 || healthBad ? StatusColors.Bad : (Brush)_t.MainAccent;
                var badge = new Border
                {
                    Background = bg, CornerRadius = new CornerRadius(fs), MinWidth = fs * 1.6, Height = fs * 1.6, Padding = new Thickness(fs * 0.3, 0, fs * 0.3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -fs * 0.5, -fs * 0.2, 0),
                    Child = new TextBlock
                    {
                        Text = queued == 0 ? "!" : queued > 99 ? "99+" : queued.ToString(), FontSize = fs, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(bg),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                icon.Children.Add(badge);
            }
            row.Children.Add(icon);
            if (_cfg.Title != null && _cfg.Title.Length > 0) row.Children.Add(T(" " + _cfg.Title, _t.FgDim));
            if (!s.Ok)
            {
                row.Children.Add(T(s.Error.Length > 0 ? " Radarr n/a" : " Radarr…", _t.FgDim));
                ToolTip = s.Error.Length > 0 ? "Can't reach Radarr: " + s.Error : null;
                return row;
            }

            // how far the downloads are: all of them together, and the longest time left
            List<RadarrQueueItem> down = s.Queue.FindAll(delegate (RadarrQueueItem q) { return q.Downloading; });
            if (!v.Badge && queued > 0) row.Children.Add(T(" " + queued, problems > 0 ? StatusColors.Bad : _t.Fg));
            if (v.BarProgress && down.Count > 0)
            {
                double size = 0, left = 0;
                TimeSpan longest = TimeSpan.Zero;
                foreach (RadarrQueueItem q in down) { size += q.Size; left += q.Left; if (q.TimeLeft > longest) longest = q.TimeLeft; }
                double pct = size > 0 ? (size - left) / size * 100 : 0;
                row.Children.Add(T(" " + Math.Floor(pct) + "%", _t.Fg));
                if (longest > TimeSpan.Zero) row.Children.Add(T(" · " + RadarrQueueItem.Left_(longest), _t.FgDim));
            }

            var tip = new List<string>();
            tip.Add("Radarr: " + (queued == 0 ? "nothing in the queue" : queued + " in the queue" + (problems > 0 ? " (" + problems + " need a look)" : "")));
            int k = 0;
            foreach (RadarrQueueItem q in s.Queue)
            {
                if (k++ >= 6) break;
                tip.Add("  " + q.Movie.Name + " - " + q.StateText);
            }
            RadarrMovie next = RadarrList.NextUp(s);
            if (next != null) { DateTime w; tip.Add("Next: " + next.Name + " · " + next.ReleaseText(out w)); }
            if (v.Health) foreach (RadarrHealth h in s.Health) if (h.Error) tip.Add("Error: " + h.Message);
            if (s.Error.Length > 0) tip.Add("Last update failed: " + s.Error);
            tip.Add("Click for the queue and what's coming");
            ToolTip = string.Join("\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            RadarrClient.Poke();
            RadarrList.ResetView(_cfg);
            _popup = new PopupWindow(_mgr, _host, this, _t, RadarrList.Build(_mgr, _t, _cfg, RadarrClient.Latest, false));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>The Radarr views (popup and widget) and their actions.</summary>
    public static class RadarrList
    {
        /// <summary>
        /// The widget's content, never smaller than it has been (so changing tab or the queue getting shorter doesn't resize the widget and
        /// push the widgets snapped to it around) - but only while the widget sizes itself (Automatic width: unlimited room). A widget
        /// with its own width gives a set width, and the content fits that instead (a size remembered with Automatic width on made
        /// the "Radarr" button and the counts spill off the right).
        /// </summary>
        sealed class KeepSize : StackPanel
        {
            public double KeepW, KeepH;
            /// <summary>The last layout had unlimited room (Automatic width): only then is its size remembered.</summary>
            public bool Free;

            protected override Size MeasureOverride(Size room)
            {
                Free = double.IsInfinity(room.Width);
                Size want = base.MeasureOverride(room);
                if (double.IsInfinity(room.Width)) want.Width = Math.Max(want.Width, KeepW);
                if (double.IsInfinity(room.Height)) want.Height = Math.Max(want.Height, KeepH);
                return want;
            }
        }
        public const int Glyph = 0xE714;
        static readonly Brush Good = StatusColors.Good, Busy = StatusColors.Warn, Bad = StatusColors.Bad;

        /// <summary>Bumped when something only the views know changes (tab, a pending action).</summary>
        public static int Version;
        public static bool MenuOpen;

        sealed class ViewState
        {
            public string Tab, FromTab;
            /// <summary>Widget: the biggest it has been with these settings (so switching tabs never shrinks it).</summary>
            public double MaxW, MaxH;
            public string SizeSig;
        }
        static readonly ConditionalWeakTable<ModuleConfig, ViewState> States = new ConditionalWeakTable<ModuleConfig, ViewState>();
        /// <summary>"m123" (a movie) / "q45" (a download) -> "searching…" / "Searching ✓" until it's old.</summary>
        sealed class Pending { public string Text; public DateTime Until; public bool Done; }
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();

        public static string G(int code) { return ((char)code).ToString(); }

        static void Bump() { Interlocked.Increment(ref Version); }

        static ViewState StateOf(ModuleConfig cfg)
        {
            string tab = RaView.Of(cfg).Tab;
            ViewState vs = States.GetValue(cfg, delegate (ModuleConfig c) { return new ViewState { Tab = tab, FromTab = tab }; });
            if (vs.FromTab != tab) vs.Tab = vs.FromTab = tab;
            return vs;
        }

        /// <summary>Back to the tab chosen in Settings (the bar's popup opens on it every time).</summary>
        public static void ResetView(ModuleConfig cfg) { StateOf(cfg).Tab = RaView.Of(cfg).Tab; }

        /// <summary>The next movie coming out that isn't downloaded yet.</summary>
        public static RadarrMovie NextUp(RadarrSnapshot s)
        {
            foreach (RadarrMovie m in s.Upcoming)
            {
                DateTime w;
                m.ReleaseText(out w);
                if (!m.HasFile && w >= DateTime.Today) return m;
            }
            return null;
        }

        static TextBlock T(Theme t, string text, Brush b, double scale = 1, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = t.Font, FontSize = Math.Max(7, t.FontSize * scale), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open " + url + ": " + ex.Message); }
        }

        static FrameworkElement Poster(Theme t, string url, double w)
        {
            double h = Math.Round(w * 1.5);
            var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(Math.Max(3, w / 14)), Background = Theme.Faded(t.Fg, 10), VerticalAlignment = VerticalAlignment.Center };
            ImageSource img = RadarrPosters.Get(url);
            if (img != null) b.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            else b.Child = new TextBlock
            {
                Text = G(Glyph), FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(8, w * 0.35), Foreground = t.FgDim, Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            return b;
        }

        static void ExpireWaiting()
        {
            if (Waiting.Count == 0) return;
            var gone = new List<string>();
            foreach (var kv in Waiting) if (DateTime.UtcNow > kv.Value.Until) gone.Add(kv.Key);
            foreach (string k in gone) Waiting.Remove(k);
        }

        // =================================================================
        // The views
        // =================================================================
        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, RadarrSnapshot s, bool widget)
        {
            ExpireWaiting();
            BarSettings set = mgr.Settings;
            ViewState vs = StateOf(cfg);
            RaView v = RaView.Of(cfg);
            var root = new KeepSize();
            if (!widget) { root.Width = 420; root.Margin = new Thickness(4); }
            if (!RadarrClient.Configured(set))
            {
                var none = T(t, "Set up Radarr on the Plugins page of Settings (address and API key).", t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(none);
                return root;
            }

            // ---- header: counts, open Radarr ----
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (v.ShowOpen)
            {
                var open = Pills.Make(t, G(0xE8A7), "Radarr", "Open Radarr", false, delegate { OpenUrl(RadarrClient.PageUrl(set, "")); });
                DockPanel.SetDock(open, Dock.Right);
                head.Children.Add(open);
            }
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(T(t, cfg.Title != null && cfg.Title.Length > 0 ? cfg.Title : "Radarr", t.Fg, 1, true));
            if (s.Ok)
            {
                int down = s.Queue.FindAll(delegate (RadarrQueueItem q) { return q.Downloading; }).Count, problems = s.Problems;
                title.Children.Add(T(t, "   " + (s.Queue.Count == 0 ? "nothing in the queue" : s.Queue.Count + " in the queue"), t.FgDim, 0.88));
                if (down > 0) title.Children.Add(T(t, " · " + down + " downloading", t.FgDim, 0.88));
                if (problems > 0) title.Children.Add(T(t, " · " + problems + " need a look", Bad, 0.88));
            }
            head.Children.Add(title);
            root.Children.Add(head);

            if (!s.Ok)
            {
                var w = T(t, s.Error.Length > 0 ? "Can't reach Radarr: " + s.Error : "Asking Radarr…", s.Error.Length > 0 ? Bad : t.FgDim);
                w.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(w);
                return root;
            }
            if (s.Error.Length > 0)
            {
                var e = T(t, "Last update failed: " + s.Error, Busy, 0.85);
                e.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(e);
            }
            // health: errors and warnings in one line, the messages in its tip, click for Radarr's status page
            if (v.Health && s.Health.Count > 0)
            {
                bool err = s.Health.Exists(delegate (RadarrHealth h) { return h.Error; });
                var lines = new List<string>();
                foreach (RadarrHealth h in s.Health) lines.Add((h.Error ? "Error: " : "Warning: ") + h.Message);
                var hl = T(t, G(0xE7BA) + "  " + (s.Health.Count == 1 ? s.Health[0].Message : s.Health.Count + " health issues"), err ? Bad : Busy, 0.82);
                hl.Margin = new Thickness(0, 0, 0, 5);
                hl.Cursor = Cursors.Hand;
                hl.ToolTip = string.Join("\n", lines) + "\n\nClick to open System > Status in Radarr";
                hl.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                hl.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(RadarrClient.PageUrl(set, "system/status")); };
                root.Children.Add(hl);
            }

            // ---- tabs (one ticked = just that view, no tab strip) ----
            List<string> shownTabs = v.TabsShown;
            string tab = shownTabs.Contains(vs.Tab) ? vs.Tab : v.Tab;
            if (shownTabs.Count > 1)
            {
                var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                foreach (string name in shownTabs)
                {
                    string n = name;
                    string count = n == "Queue" && s.Queue.Count > 0 ? s.Queue.Count.ToString() : n == "Missing" && s.MissingTotal > 0 ? s.MissingTotal.ToString() : null;
                    tabs.Children.Add(Chip(t, n, count, n == tab, delegate { vs.Tab = n; Bump(); }));
                }
                root.Children.Add(tabs);
            }

            if (!widget)
            {
                BuildTab(mgr, t, cfg, s, root, tab);
                return root;
            }

            // A widget keeps one size: every tab is built, stacked on top of each other, and all but the chosen one hidden (hidden
            // still takes its room) - so changing tab doesn't resize the widget and push the widgets snapped to it around.
            var stack = new Grid();
            foreach (string tb in shownTabs)
            {
                var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                BuildTab(mgr, t, cfg, s, panel, tb);
                panel.Visibility = tb == tab ? Visibility.Visible : Visibility.Hidden;
                stack.Children.Add(panel);
            }
            root.Children.Add(stack);
            string sig = v.Count + "|" + v.Posters + "|" + string.Join(",", shownTabs) + "|" + v.Tab + "|" + t.FontSize + "|" + v.ShowOpen + "|" + v.Days + "|" + cfg.TextSize;
            if (vs.SizeSig != sig) { vs.SizeSig = sig; vs.MaxW = 0; vs.MaxH = 0; }
            root.KeepW = vs.MaxW;
            root.KeepH = vs.MaxH;
            root.SizeChanged += delegate
            {
                if (root.Free && root.ActualWidth > vs.MaxW) vs.MaxW = root.ActualWidth;
                if (root.ActualHeight > vs.MaxH) vs.MaxH = root.ActualHeight;
            };
            return root;
        }

        static void BuildTab(BarManager mgr, Theme t, ModuleConfig cfg, RadarrSnapshot s, StackPanel into, string tab)
        {
            switch (tab)
            {
                case "Upcoming": BuildUpcoming(mgr, t, cfg, s, into); break;
                case "Recently added": BuildRecent(mgr, t, cfg, s, into); break;
                case "Missing": BuildMissing(mgr, t, cfg, s, into); break;
                default: BuildQueue(mgr, t, cfg, s, into); break;
            }
        }

        static Border Chip(Theme t, string text, string count, bool on, Action click)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(T(t, text, on ? t.Fg : t.FgDim, 0.85, on));
            if (!string.IsNullOrEmpty(count)) row.Children.Add(T(t, " " + count, on ? t.Fg : t.FgDim, 0.78));
            Brush bg = on ? t.ActiveBg : Brushes.Transparent;
            var b = new Border
            {
                Child = row, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(Pills.Radius), Background = bg, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 2), BorderBrush = on ? Theme.Faded(t.MainAccent, 60) : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1.5)
            };
            b.MouseEnter += delegate { if (!on) b.Background = t.Hover; };
            b.MouseLeave += delegate { b.Background = bg; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (!on) click(); };
            return b;
        }

        static void Empty(Theme t, StackPanel root, string text)
        {
            var nb = T(t, text, t.FgDim, 0.9);
            nb.Margin = new Thickness(2, 4, 0, 4);
            nb.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(nb);
        }

        static void More(BarManager mgr, Theme t, StackPanel root, int more, string page)
        {
            var m = T(t, "+ " + more + " more in Radarr", t.FgDim, 0.85);
            m.Margin = new Thickness(4, 3, 0, 0);
            m.Cursor = Cursors.Hand;
            m.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            m.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(RadarrClient.PageUrl(mgr.Settings, page)); };
            root.Children.Add(m);
        }

        /// <summary>A row: poster (or a dot), the title and year, a line under it, something on the right; click opens the movie in Radarr.</summary>
        static Border MovieRow(BarManager mgr, Theme t, ModuleConfig cfg, RadarrMovie m, Brush dot, FrameworkElement sub, FrameworkElement right, string tip, Action<Border> menu)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (RaView.Of(cfg).Posters)
            {
                var poster = (FrameworkElement)Poster(t, m.Poster, Math.Round(t.FontSize * 3.1 / 1.5));
                poster.Margin = new Thickness(0, 0, 8, 0);
                g.Children.Add(poster);
            }
            else
            {
                double d = 8;
                g.Children.Add(new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = dot, Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(T(t, m.Title, t.Fg, 0.95, true));
            if (m.Year.Length > 0) top.Children.Add(T(t, "  " + m.Year, t.FgDim, 0.85));
            info.Children.Add(top);
            if (sub != null) info.Children.Add(sub);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            if (right != null)
            {
                right.VerticalAlignment = VerticalAlignment.Center;
                right.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
            }
            var row = new Border { Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(RadarrClient.MovieUrl(mgr.Settings, m)); };
            if (menu != null) row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; menu(row); };
            return row;
        }

        // ---- Queue ----
        static void BuildQueue(BarManager mgr, Theme t, ModuleConfig cfg, RadarrSnapshot s, StackPanel root)
        {
            if (s.Queue.Count == 0)
            {
                RadarrMovie next = NextUp(s);
                DateTime w;
                Empty(t, root, "Nothing in the queue." + (next != null ? " Next: " + next.Name + " · " + next.ReleaseText(out w) : ""));
                return;
            }
            int max = RaView.Of(cfg).Count, n = 0;
            foreach (RadarrQueueItem q in s.Queue)
            {
                if (n++ >= max) { More(mgr, t, root, s.Queue.Count - max, "activity/queue"); break; }
                root.Children.Add(QueueRow(mgr, t, cfg, q));
            }
        }

        static FrameworkElement QueueRow(BarManager mgr, Theme t, ModuleConfig cfg, RadarrQueueItem q)
        {
            Pending p;
            bool waiting = Waiting.TryGetValue("q" + q.Id, out p);
            Brush stateBrush = q.Problem ? Bad : q.Downloading ? (Brush)t.MainAccent : q.State == "importPending" || q.State == "importing" || q.Status == "completed" ? Good : t.FgDim;
            var sub = new StackPanel();
            var line = new List<string>();
            if (q.Quality.Length > 0) line.Add(q.Quality);
            if (q.Size > 0) line.Add(InfoModule.Bytes(q.Size));
            if (q.Client.Length > 0) line.Add(q.Client);
            sub.Children.Add(T(t, string.Join(" · ", line), t.FgDim, 0.8));
            if (q.Status == "downloading" || q.Status == "paused")
            {
                // a thin bar under it
                var bar = new Grid { Height = 3, Margin = new Thickness(0, 3, 0, 1) };
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, q.Percent), GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - q.Percent), GridUnitType.Star) });
                var track = new Border { Background = t.Track, CornerRadius = new CornerRadius(1.5) };
                Grid.SetColumnSpan(track, 2);
                bar.Children.Add(track);
                bar.Children.Add(new Border { Background = q.Problem ? Bad : q.Status == "paused" ? t.FgDim : (Brush)t.MainAccent, CornerRadius = new CornerRadius(1.5) });
                sub.Children.Add(bar);
            }
            var state = T(t, waiting ? p.Text : q.StateText, waiting ? Busy : stateBrush, 0.8);
            var tip = new List<string> { q.Movie.Name + " - " + q.StateText };
            if (q.Release.Length > 0) tip.Add(q.Release);
            if (q.Message.Length > 0) tip.Add(q.Message);
            tip.Add("Click: open in Radarr · right-click: more");
            return MovieRow(mgr, t, cfg, q.Movie, stateBrush, sub, state, string.Join("\n", tip), delegate (Border row) { QueueMenu(mgr, row, q); });
        }

        static void QueueMenu(BarManager mgr, FrameworkElement target, RadarrQueueItem q)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = q.Movie.Name + " - " + q.StateText, IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, "Open in Radarr", delegate { OpenUrl(RadarrClient.MovieUrl(mgr.Settings, q.Movie)); });
            Add(m, "Open the queue in Radarr", delegate { OpenUrl(RadarrClient.PageUrl(mgr.Settings, "activity/queue")); });
            if (q.Release.Length > 0) Add(m, "Copy the release name", delegate { try { Clipboard.SetText(q.Release); } catch { } });
            m.Items.Add(new Separator());
            Add(m, "Remove from the queue…", delegate { Remove(mgr, target, q, false); });
            Add(m, "Remove and blocklist the release…", delegate { Remove(mgr, target, q, true); });
            ShowMenu(m, target);
        }

        static void Add(ContextMenu m, string header, Action click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += delegate { click(); };
            m.Items.Add(mi);
        }

        static async void Remove(BarManager mgr, FrameworkElement target, RadarrQueueItem q, bool blocklist)
        {
            if (!Ask(target, "Remove " + q.Movie.Name + " from the queue (and the download client)?" +
                             (blocklist ? "\n\nThe release goes on the blocklist, so Radarr won't grab it again." : ""))) return;
            MenuOpen = false;
            string key = "q" + q.Id;
            Waiting[key] = new Pending { Text = "removing…", Until = DateTime.UtcNow.AddSeconds(30) };
            Bump();
            string err = await RadarrClient.RemoveAsync(mgr.Settings, q.Id, blocklist);
            Waiting.Remove(key);
            Bump();
            if (err != null) MessageBox.Show("Radarr: " + err, "Radarr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ---- Upcoming ----
        static void BuildUpcoming(BarManager mgr, Theme t, ModuleConfig cfg, RadarrSnapshot s, StackPanel root)
        {
            RaView v = RaView.Of(cfg);
            DateTime until = DateTime.Today.AddDays(v.Days);
            var list = s.Upcoming.FindAll(delegate (RadarrMovie m) { DateTime w; m.ReleaseText(out w); return w >= DateTime.Today.AddDays(-1) && w <= until; });
            if (list.Count == 0) { Empty(t, root, "Nothing monitored comes out in the next " + v.Days + " days."); return; }
            int n = 0;
            foreach (RadarrMovie m in list)
            {
                if (n++ >= v.Count) { More(mgr, t, root, list.Count - v.Count, "calendar"); break; }
                DateTime w;
                string when = m.ReleaseText(out w);
                FrameworkElement right = m.HasFile ? (FrameworkElement)T(t, "Downloaded", Good, 0.8) : null;
                string tip = m.Name + "\n" + when + (m.HasFile ? " · downloaded" : "") + (m.Overview.Length > 0 ? "\n\n" + Wrap(m.Overview) : "") + "\n\nClick: open in Radarr";
                root.Children.Add(MovieRow(mgr, t, cfg, m, m.HasFile ? Good : t.MainAccent, T(t, when, t.FgDim, 0.8), right, tip, delegate (Border row) { MovieMenu(mgr, row, m); }));
            }
        }

        static string Wrap(string text) { return text.Length > 300 ? text.Substring(0, 297) + "…" : text; }

        // ---- Recently added ----
        static void BuildRecent(BarManager mgr, Theme t, ModuleConfig cfg, RadarrSnapshot s, StackPanel root)
        {
            var seen = new HashSet<int>();
            var list = s.Recent.FindAll(delegate (RadarrEvent e) { return e.Imported && seen.Add(e.Movie.Id); });
            if (list.Count == 0) { Empty(t, root, "Nothing downloaded lately."); return; }
            int max = RaView.Of(cfg).Count, n = 0;
            foreach (RadarrEvent e in list)
            {
                if (n++ >= max) break;
                string sub = (e.Quality.Length > 0 ? e.Quality + " · " : "") + "added " + RadarrClient.Ago(e.Date);
                string tip = e.Movie.Name + "\nDownloaded " + (e.Date != DateTime.MinValue ? TimeText.Stamp(e.Date.ToLocalTime()) : "") + (e.Release.Length > 0 ? "\n" + e.Release : "") + "\n\nClick: open in Radarr";
                RadarrMovie m = e.Movie;
                root.Children.Add(MovieRow(mgr, t, cfg, m, Good, T(t, sub, t.FgDim, 0.8), null, tip, delegate (Border row) { MovieMenu(mgr, row, m); }));
            }
        }

        // ---- Missing ----
        static void BuildMissing(BarManager mgr, Theme t, ModuleConfig cfg, RadarrSnapshot s, StackPanel root)
        {
            if (s.MissingError.Length > 0) { Empty(t, root, s.MissingError); return; }
            if (s.Missing.Count == 0) { Empty(t, root, s.Fetched != DateTime.MinValue ? "Nothing missing - every monitored movie that's out is downloaded." : "Asking Radarr…"); return; }
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 4) };
            Pending all;
            bool allWaiting = Waiting.TryGetValue("all", out all);
            FrameworkElement searchAll = allWaiting ? (FrameworkElement)T(t, all.Text, all.Done ? Good : Busy, 0.8)
                : Pills.Make(t, G(0xE721), "Search all", "Ask Radarr to search for every missing movie", false, delegate { SearchAll(mgr); });
            DockPanel.SetDock(searchAll, Dock.Right);
            head.Children.Add(searchAll);
            head.Children.Add(T(t, s.MissingTotal + " monitored " + (s.MissingTotal == 1 ? "movie is" : "movies are") + " out but not downloaded", t.FgDim, 0.82));
            root.Children.Add(head);
            int max = RaView.Of(cfg).Count, n = 0;
            foreach (RadarrMovie m in s.Missing)
            {
                if (n++ >= max) { More(mgr, t, root, s.MissingTotal - max, "wanted/missing"); break; }
                DateTime w;
                string when = m.ReleaseText(out w);
                RadarrMovie mm = m;
                Pending p;
                FrameworkElement right = Waiting.TryGetValue("m" + m.Id, out p) ? (FrameworkElement)T(t, p.Text, p.Done ? Good : Busy, 0.8)
                    : Pills.Make(t, G(0xE721), "", "Search for it now", true, delegate { SearchMovie(mgr, mm); });
                string tip = m.Name + (when.Length > 0 ? "\n" + when : "") + (m.Overview.Length > 0 ? "\n\n" + Wrap(m.Overview) : "") + "\n\nClick: open in Radarr";
                root.Children.Add(MovieRow(mgr, t, cfg, m, Busy, T(t, when, t.FgDim, 0.8), right, tip, delegate (Border row) { MovieMenu(mgr, row, mm); }));
            }
        }

        static void MovieMenu(BarManager mgr, FrameworkElement target, RadarrMovie mv)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = mv.Name, IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, "Open in Radarr", delegate { OpenUrl(RadarrClient.MovieUrl(mgr.Settings, mv)); });
            if (mv.InLibrary) Add(m, mv.HasFile ? "Search for a better release" : "Search for it now", delegate { SearchMovie(mgr, mv); });
            Add(m, "Copy the title", delegate { try { Clipboard.SetText(mv.Name); } catch { } });
            ShowMenu(m, target);
        }

        public static async void SearchMovie(BarManager mgr, RadarrMovie m)
        {
            MenuOpen = false;
            string key = "m" + m.Id;
            Waiting[key] = new Pending { Text = "asking…", Until = DateTime.UtcNow.AddSeconds(30) };
            Bump();
            string err = await RadarrClient.SearchMovieAsync(mgr.Settings, m.Id);
            if (err != null)
            {
                Waiting.Remove(key);
                Bump();
                MessageBox.Show("Radarr: " + err, "Radarr", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Waiting[key] = new Pending { Text = "Searching ✓", Until = DateTime.UtcNow.AddSeconds(60), Done = true };
            Bump();
        }

        static async void SearchAll(BarManager mgr)
        {
            Waiting["all"] = new Pending { Text = "asking…", Until = DateTime.UtcNow.AddSeconds(30) };
            Bump();
            string err = await RadarrClient.SearchMissingAsync(mgr.Settings);
            if (err != null)
            {
                Waiting.Remove("all");
                Bump();
                MessageBox.Show("Radarr: " + err, "Radarr", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Waiting["all"] = new Pending { Text = "Searching ✓", Until = DateTime.UtcNow.AddSeconds(90), Done = true };
            Bump();
        }

        static void ShowMenu(ContextMenu m, FrameworkElement target)
        {
            PopupWindow host = Window.GetWindow(target) as PopupWindow;
            if (host != null) host.Pinned = true;       // the menu must not close the popup
            MenuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!_asking) MenuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        static bool _asking;

        static bool Ask(FrameworkElement target, string question)
        {
            Window owner = target != null ? Window.GetWindow(target) : null;
            PopupWindow host = owner as PopupWindow;
            if (host != null) host.Pinned = true;
            _asking = MenuOpen = true;
            try { return Questions.YesNo(owner, question, "Radarr") == MessageBoxResult.Yes; }
            finally { if (host != null) host.Pinned = false; _asking = MenuOpen = false; }
        }
    }

    /// <summary>Posters (TMDB's small ones), downloaded once and kept for the session.</summary>
    public static class RadarrPosters
    {
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();
        static readonly HashSet<string> Loading = new HashSet<string>();
        static readonly HashSet<string> Failed = new HashSet<string>();
        // the posters come from the internet (TMDB / TVDB): vShell's shared client for them, with its User-Agent
        static HttpClient Http { get { return PluginHttp.Client("posters"); } }
        /// <summary>Bumped when a poster arrives (lists rebuild).</summary>
        public static int Version;

        /// <summary>The poster, or null for now (it is fetched and Version bumps when it is here).</summary>
        public static ImageSource Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (Cache)
            {
                ImageSource img;
                if (Cache.TryGetValue(url, out img)) return img;
                if (Failed.Contains(url) || !Loading.Add(url)) return null;
                if (Cache.Count > 400) Cache.Clear();
            }
            Task.Run(async delegate
            {
                try
                {
                    byte[] data = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(data))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 92;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    lock (Cache) { Cache[url] = bmp; Loading.Remove(url); }
                }
                catch
                {
                    lock (Cache) { Failed.Add(url); Loading.Remove(url); }
                }
                Interlocked.Increment(ref Version);
            });
            return null;
        }
    }
}
