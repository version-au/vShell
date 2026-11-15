using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Jellyfin: who's watching. On a bar the icon with the number of people playing something as a badge - click it for the popup. In a
    /// widget the view itself: what's playing now (with its progress, and whether it's transcoding), recently added and the library's totals.
    /// </summary>
    public sealed class JellyfinModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public JellyfinModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            if (JellyfinList.MenuOpen) return;
            bool configured = JellyfinClient.Configured(_mgr.Settings);
            JellyfinSnapshot s = configured ? JellyfinClient.Latest : new JellyfinSnapshot();
            // the progress moves every poll, so the poll time is in the key
            string key = (configured ? "c" : "n") + "|" + s.Fetched.Ticks + "|" + s.Error + "|" + JellyfinList.Version;
            if (!force && key == _shownKey) return;
            _shownKey = key;
            if (_host.IsWidget) Child = JellyfinList.Build(_mgr, _t, _cfg, s, true);
            else
            {
                Child = BuildBar(s, configured);
                if (_popup != null && _popup.IsOpen) _popup.SetContent(JellyfinList.Build(_mgr, _t, _cfg, s, false));
            }
            _host.ContentChanged();
        }

        FrameworkElement BuildBar(JellyfinSnapshot s, bool configured)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b)
            {
                return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            };
            Visibility = Visibility.Visible;
            if (!configured) { row.Children.Add(T("Set up Jellyfin in Settings > Plugins", _t.FgDim)); ToolTip = null; return row; }

            JfView v = JfView.Of(_cfg);
            int playing = s.Playing.Count;
            if (v.HideIdle && s.Ok && playing == 0 && s.Error.Length == 0) { Visibility = Visibility.Collapsed; return row; }

            // the icon, with how many are playing as a badge
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            icon.Children.Add(new TextBlock
            {
                Text = JellyfinList.G(JellyfinList.Glyph), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.1, VerticalAlignment = VerticalAlignment.Center,
                Foreground = playing > 0 ? _t.Fg : _t.FgDim, Margin = new Thickness(0, 0, playing > 0 ? 6 : 0, 0)
            });
            if (playing > 0)
            {
                double fs = Math.Max(7, _t.FontSize * 0.62);
                icon.Children.Add(new Border
                {
                    Background = _t.MainAccent, CornerRadius = new CornerRadius(fs), MinWidth = fs * 1.6, Height = fs * 1.6, Padding = new Thickness(fs * 0.3, 0, fs * 0.3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -fs * 0.5, -fs * 0.2, 0),
                    Child = new TextBlock
                    {
                        Text = playing > 99 ? "99+" : playing.ToString(), FontSize = fs, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(_t.MainAccent),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }
            row.Children.Add(icon);
            if (_cfg.Title != null && _cfg.Title.Length > 0) row.Children.Add(T(" " + _cfg.Title, _t.FgDim));
            if (!s.Ok)
            {
                row.Children.Add(T(s.Error.Length > 0 ? " Jellyfin n/a" : " Jellyfin…", _t.FgDim));
                ToolTip = s.Error.Length > 0 ? "Can't reach Jellyfin: " + s.Error : null;
                return row;
            }
            int transcoding = s.Playing.FindAll(delegate (JellyfinPlaying p) { return p.Transcoding; }).Count;
            if (transcoding > 0) row.Children.Add(T(" " + transcoding + " transcoding", StatusColors.Warn));

            var tip = new List<string>();
            tip.Add("Jellyfin: " + (playing == 0 ? "nobody is playing anything" : playing == 1 ? "1 playing" : playing + " playing"));
            foreach (JellyfinPlaying p in s.Playing)
                tip.Add("  " + (p.UserName.Length > 0 ? p.UserName : "Someone") + ": " + p.Shown + (p.Paused ? " (paused)" : "") + (p.Transcoding ? " · transcoding" : ""));
            if (s.Error.Length > 0) tip.Add("Last update failed: " + s.Error);
            tip.Add("Click for who's watching and what's new");
            ToolTip = string.Join("\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            JellyfinClient.Poke();
            _popup = new PopupWindow(_mgr, _host, this, _t, JellyfinList.Build(_mgr, _t, _cfg, JellyfinClient.Latest, false));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>The Jellyfin view (popup and widget) and its actions.</summary>
    public static class JellyfinList
    {
        public const int Glyph = 0xE8B2;      // Movies
        static readonly Brush Good = StatusColors.Good, Busy = StatusColors.Warn, Bad = StatusColors.Bad;

        /// <summary>Bumped when something only the view knows changes (a button pressed).</summary>
        public static int Version;
        public static bool MenuOpen;

        public static string G(int code) { return ((char)code).ToString(); }

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
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open " + url + ": " + ex.Message); }
        }

        static TextBlock Heading(Theme t, string text)
        {
            var h = T(t, text, t.FgDim, 0.78, true);
            h.Margin = new Thickness(2, 6, 0, 2);
            return h;
        }

        /// <summary>"1:23:45" / "12:34".</summary>
        public static string Clock(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ts.TotalHours >= 1 ? ((int)ts.TotalHours) + ":" + ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00") : ts.Minutes + ":" + ts.Seconds.ToString("00");
        }

        // =================================================================
        // The view
        // =================================================================
        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, JellyfinSnapshot s, bool widget)
        {
            BarSettings set = mgr.Settings;
            JfView v = JfView.Of(cfg);
            var root = new StackPanel();
            if (!widget) { root.Width = 420; root.Margin = new Thickness(4); }
            if (!JellyfinClient.Configured(set))
            {
                var none = T(t, "Set up Jellyfin on the Plugins page of Settings (its address and an API key).", t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(none);
                return root;
            }

            // ---- header: who's playing, open Jellyfin ----
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (v.ShowOpen)
            {
                var open = Pills.Make(t, G(0xE8A7), "Jellyfin", "Open Jellyfin", false, delegate { OpenUrl(JellyfinClient.WebUrl(set)); });
                DockPanel.SetDock(open, Dock.Right);
                head.Children.Add(open);
            }
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(T(t, cfg.Title != null && cfg.Title.Length > 0 ? cfg.Title : "Jellyfin", t.Fg, 1, true));
            if (s.Ok) title.Children.Add(T(t, "   " + (s.Playing.Count == 0 ? "nobody watching" : s.Playing.Count + " playing"), t.FgDim, 0.88));
            head.Children.Add(title);
            root.Children.Add(head);

            if (!s.Ok)
            {
                var w = T(t, s.Error.Length > 0 ? "Can't reach Jellyfin: " + s.Error : "Asking Jellyfin…", s.Error.Length > 0 ? Bad : t.FgDim);
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

            if (v.ShowPlaying)
            {
                root.Children.Add(Heading(t, "PLAYING NOW"));
                if (s.Playing.Count == 0)
                {
                    var nb = T(t, "Nobody is playing anything.", t.FgDim, 0.88);
                    nb.Margin = new Thickness(2, 2, 0, 4);
                    root.Children.Add(nb);
                }
                foreach (JellyfinPlaying p in s.Playing) root.Children.Add(PlayingRow(mgr, t, p));
            }
            if (v.ShowRecent) BuildRecent(set, t, v, s, root);
            if (v.ShowCounts && s.Movies >= 0) BuildCounts(t, s, root);
            return root;
        }

        static FrameworkElement PlayingRow(BarManager mgr, Theme t, JellyfinPlaying p)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(t, p.Shown, t.Fg, 0.92, true));
            var line = new List<string>();
            if (p.UserName.Length > 0) line.Add(p.UserName);
            if (p.DeviceName.Length > 0) line.Add(p.DeviceName);
            if (p.Client.Length > 0 && p.Client != p.DeviceName) line.Add(p.Client);
            var sub = new StackPanel { Orientation = Orientation.Horizontal };
            sub.Children.Add(T(t, string.Join(" · ", line), t.FgDim, 0.8));
            if (p.PlayMethod.Length > 0)
                sub.Children.Add(T(t, "  " + (p.Transcoding ? "transcoding" : p.PlayMethod == "DirectStream" ? "direct stream" : "direct play"), p.Transcoding ? Busy : Good, 0.8));
            info.Children.Add(sub);
            // a thin bar under it
            double pct = p.Duration > 0 ? Math.Max(0, Math.Min(100, p.Position / p.Duration * 100)) : 0;
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 3, 0, 1) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, pct), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - pct), GridUnitType.Star) });
            var track = new Border { Background = t.Track, CornerRadius = new CornerRadius(1.5) };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { Background = p.Paused ? t.FgDim : (Brush)t.MainAccent, CornerRadius = new CornerRadius(1.5) });
            if (p.Duration > 0) info.Children.Add(bar);
            g.Children.Add(info);
            string right = p.Paused ? "paused" : p.Duration > 0 ? Clock(p.Duration - p.Position) + " left" : "";
            var r = T(t, right, p.Paused ? Busy : t.Fg, 0.8);
            r.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(r, 1);
            g.Children.Add(r);
            var tip = new List<string> { p.Shown, (p.UserName.Length > 0 ? p.UserName : "Someone") + " on " + p.DeviceName + (p.Client.Length > 0 ? " (" + p.Client + ")" : "") };
            if (p.Duration > 0) tip.Add(Clock(p.Position) + " of " + Clock(p.Duration));
            if (p.Transcoding) tip.Add("Transcoding" + (p.TranscodeReasons.Length > 0 ? ": " + p.TranscodeReasons : ""));
            tip.Add("Click or right-click: pause / resume, stop");
            var row = new Border { Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = string.Join("\n", tip), Cursor = Cursors.Hand };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; PlayingMenu(mgr, row, p); };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; PlayingMenu(mgr, row, p); };
            return row;
        }

        static void PlayingMenu(BarManager mgr, FrameworkElement target, JellyfinPlaying p)
        {
            BarSettings s = mgr.Settings;
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = (p.UserName.Length > 0 ? p.UserName + ": " : "") + p.Shown, IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, p.Paused ? "Resume" : "Pause", delegate { Control(s, p, "PlayPause"); });
            Add(m, "Stop", delegate { Control(s, p, "Stop"); });
            m.Items.Add(new Separator());
            Add(m, "Open it in Jellyfin", delegate { OpenUrl(JellyfinClient.ItemUrl(s, p.ItemId)); });
            Add(m, "Open Jellyfin's dashboard", delegate { OpenUrl(JellyfinClient.DashboardUrl(s)); });
            ShowMenu(m, target);
        }

        static async void Control(BarSettings s, JellyfinPlaying p, string command)
        {
            MenuOpen = false;
            string err = await JellyfinClient.ControlAsync(s, p.SessionId, command);
            if (err != null) MessageBox.Show("Jellyfin: " + err, "Jellyfin", MessageBoxButton.OK, MessageBoxImage.Warning);
            System.Threading.Interlocked.Increment(ref Version);
        }

        // ---- recently added ----
        static void BuildRecent(BarSettings set, Theme t, JfView v, JellyfinSnapshot s, StackPanel root)
        {
            root.Children.Add(Heading(t, "RECENTLY ADDED"));
            if (s.Recent.Count == 0)
            {
                var nb = T(t, s.RecentError.Length > 0 ? "Couldn't read the library: " + s.RecentError : "Nothing yet.", s.RecentError.Length > 0 ? Busy : t.FgDim, 0.85);
                nb.TextWrapping = TextWrapping.Wrap;
                nb.Margin = new Thickness(2, 2, 0, 4);
                root.Children.Add(nb);
                return;
            }
            int n = 0;
            foreach (JellyfinItem it0 in s.Recent)
            {
                if (n++ >= v.Count) break;
                JellyfinItem it = it0;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = T(t, it.Shown, t.Fg, 0.88);
                g.Children.Add(name);
                var when = T(t, it.Kind + (it.Added != DateTime.MinValue ? " · " + TimeText.Ago(it.Added) : ""), t.FgDim, 0.78);
                when.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(when, 1);
                g.Children.Add(when);
                var row = new Border
                {
                    Child = g, Padding = new Thickness(3, 2, 4, 2), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                    ToolTip = it.Shown + "\nAdded " + (it.Added != DateTime.MinValue ? TimeText.Stamp(it.Added.ToLocalTime()) : "lately") + "\nClick to open it in Jellyfin"
                };
                row.MouseEnter += delegate { row.Background = t.Hover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(JellyfinClient.ItemUrl(set, it.Id)); };
                root.Children.Add(row);
            }
        }

        // ---- library totals ----
        static void BuildCounts(Theme t, JellyfinSnapshot s, StackPanel root)
        {
            root.Children.Add(Heading(t, "LIBRARY"));
            var wrap = new WrapPanel { Margin = new Thickness(2, 0, 0, 2) };
            Action<int, string> cell = delegate (int number, string what)
            {
                if (number <= 0) return;
                var c = new StackPanel { Margin = new Thickness(0, 0, 16, 2) };
                c.Children.Add(T(t, number.ToString("N0"), t.Fg, 1.05, true));
                c.Children.Add(T(t, what, t.FgDim, 0.75));
                wrap.Children.Add(c);
            };
            cell(s.Movies, "movies");
            cell(s.Series, "shows");
            cell(s.Episodes, "episodes");
            cell(s.Albums, "albums");
            cell(s.Songs, "songs");
            if (wrap.Children.Count == 0) wrap.Children.Add(T(t, "The library is empty.", t.FgDim, 0.85));
            root.Children.Add(wrap);
        }

        static void Add(ContextMenu m, string header, Action click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += delegate { click(); };
            m.Items.Add(mi);
        }

        static void ShowMenu(ContextMenu m, FrameworkElement target)
        {
            PopupWindow host = Window.GetWindow(target) as PopupWindow;
            if (host != null) host.Pinned = true;       // the menu must not close the popup
            MenuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { MenuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }
    }
}
