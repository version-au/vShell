using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Seerr: requests and what's new. On a bar an icon with the number waiting for approval as a badge (and how many are processing /
    /// available) - click it for the popup. In a widget the views themselves: Requests (filters All / Pending / Processing / Available /
    /// Mine, Approve / Decline on pending ones), Recently added, Trending and Upcoming (with a Request button).
    /// </summary>
    public sealed class SeerrModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public SeerrModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            if (SeerrList.MenuOpen) return;
            bool configured = SeerrClient.Configured(_mgr.Settings);
            SeerrSnapshot s = configured ? SeerrClient.Latest : new SeerrSnapshot();
            SrView v = SrView.Of(_cfg);
            string key = (configured ? "c" : "n") + "|" + s.Fetched.Ticks + "|" + s.Error + "|" + SeerrList.Version + "|" + SeerrPosters.Version + "|" + v.Tab + "|" + v.Filter + "|" +
                         string.Join(",", v.TabsShown) + "|" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            if (!force && key == _shownKey) return;
            _shownKey = key;
            if (_host.IsWidget) Child = SeerrList.Build(_mgr, _t, _cfg, s, true);
            else
            {
                Child = BuildBar(s, configured);
                if (_popup != null && _popup.IsOpen) _popup.SetContent(SeerrList.Build(_mgr, _t, _cfg, s, false));
            }
            _host.ContentChanged();
        }

        FrameworkElement BuildBar(SeerrSnapshot s, bool configured)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b)
            {
                return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            };
            if (!configured) { row.Children.Add(T("Set up Seerr in Settings > Plugins", _t.FgDim)); ToolTip = null; return row; }

            // the icon, with the number waiting for approval as a badge
            SrView v = SrView.Of(_cfg);
            int pending = s.Ok ? Math.Max(s.Counts.Pending, s.Pending.Count) : 0;
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            icon.Children.Add(new TextBlock
            {
                Text = SeerrList.G(0xE8B2), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.1, VerticalAlignment = VerticalAlignment.Center,
                Foreground = pending > 0 ? _t.Fg : _t.FgDim, Margin = new Thickness(0, 0, v.Badge && pending > 0 ? 6 : 0, 0)
            });
            if (v.Badge && pending > 0)
            {
                double fs = Math.Max(7, _t.FontSize * 0.62);
                var badge = new Border
                {
                    Background = _t.MainAccent, CornerRadius = new CornerRadius(fs), MinWidth = fs * 1.6, Height = fs * 1.6, Padding = new Thickness(fs * 0.3, 0, fs * 0.3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -fs * 0.5, -fs * 0.2, 0),
                    Child = new TextBlock
                    {
                        Text = pending > 99 ? "99+" : pending.ToString(), FontSize = fs, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(_t.MainAccent),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                icon.Children.Add(badge);
            }
            row.Children.Add(icon);
            if (_cfg.Title != null && _cfg.Title.Length > 0) row.Children.Add(T(" " + _cfg.Title, _t.FgDim));
            if (!s.Ok)
            {
                row.Children.Add(T(s.Error.Length > 0 ? " Seerr n/a" : " Seerr…", _t.FgDim));
                ToolTip = s.Error.Length > 0 ? "Can't reach Seerr: " + s.Error : null;
                return row;
            }
            bool any = false;
            Action<int, string, Brush> part = delegate (int n, string what, Brush b)
            {
                row.Children.Add(T((any ? "   " : " ") , _t.FgDim));
                row.Children.Add(T(n.ToString(), b));
                row.Children.Add(T(" " + what, _t.FgDim));
                any = true;
            };
            if (!v.Badge && pending > 0) part(pending, "pending", SeerrList.Busy);
            if (v.BarProcessing) part(s.Counts.Processing, "processing", _t.Fg);
            if (v.BarAvailable) part(s.Counts.Available, "available", _t.Fg);

            var tip = new List<string> { "Seerr: " + pending + " pending · " + s.Counts.Processing + " processing · " + s.Counts.Available + " available" };
            int k = 0;
            foreach (SeerrRequest q in s.Requests)
            {
                if (k++ >= 5) break;
                if (k == 1) tip.Add("Latest requests:");
                tip.Add("  " + q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : "") + " - " + q.StateText +
                        (q.RequestedBy.Length > 0 ? " · " + q.RequestedBy : "") + " · " + SeerrClient.Ago(q.Created));
            }
            if (s.Error.Length > 0) tip.Add("Last update failed: " + s.Error);
            tip.Add("Click for requests and what's new");
            ToolTip = string.Join("\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            SeerrClient.Poke();
            SeerrList.ResetView(_cfg);
            _popup = new PopupWindow(_mgr, _host, this, _t, SeerrList.Build(_mgr, _t, _cfg, SeerrClient.Latest, false));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>The Seerr views (popup and widget) and their actions.</summary>
    public static class SeerrList
    {
        public static readonly Brush Good = StatusColors.Good, Busy = StatusColors.Warn, Bad = StatusColors.Bad;
        static readonly Brush Blue = Frozen(0x89, 0xB4, 0xFA);
        static Brush Frozen(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        /// <summary>Bumped when something only the views know changes (tab, filter, a pending action, Trending loaded).</summary>
        public static int Version;
        public static bool MenuOpen;

        /// <summary>The tab / filter picked in the view; From* = the settings they started from (a change in Settings starts over).</summary>
        sealed class ViewState
        {
            public string Tab, Filter, FromTab, FromFilter;
            /// <summary>Widget: the biggest it has been with these settings (so switching tabs / filters never shrinks it).</summary>
            public double MaxW, MaxH, AtW;
            /// <summary>Poster views: the first poster shown (the arrows page through), per view - kept while the data refreshes.</summary>
            public readonly Dictionary<string, int> Page = new Dictionary<string, int>();
            public string SizeSig;
        }
        static readonly ConditionalWeakTable<ModuleConfig, ViewState> States = new ConditionalWeakTable<ModuleConfig, ViewState>();
        /// <summary>Request id (or "movie:123" for a new request) -> "approving…" / "Requested ✓" until Seerr shows the change.</summary>
        sealed class Pending { public string Text; public DateTime Until; public bool Done; }
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();

        public static string G(int code) { return ((char)code).ToString(); }

        static void Bump() { Interlocked.Increment(ref Version); }

        static ViewState StateOf(ModuleConfig cfg)
        {
            SrView v = SrView.Of(cfg);
            string tab = v.Tab, filter = v.Filter;
            ViewState vs = States.GetValue(cfg, delegate (ModuleConfig c) { return new ViewState { Tab = tab, Filter = filter, FromTab = tab, FromFilter = filter }; });
            if (vs.FromTab != tab) { vs.Tab = vs.FromTab = tab; }
            if (vs.FromFilter != filter) { vs.Filter = vs.FromFilter = filter; }
            return vs;
        }

        /// <summary>Back to the tab and filter chosen in Settings (the bar's popup opens on them every time).</summary>
        public static void ResetView(ModuleConfig cfg)
        {
            ViewState vs = StateOf(cfg);
            SrView v = SrView.Of(cfg);
            vs.Tab = v.Tab;
            vs.Filter = v.Filter;
        }

        public static Brush StateBrush(string state, Theme t)
        {
            switch (state)
            {
                case "pending": return Busy;
                case "available": return Good;
                case "partly": return Good;
                case "declined":
                case "failed": return Bad;
                default: return Blue;
            }
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

        static Border Pill(Theme t, string glyph, string text, string tip, bool primary, Action click)
        {
            return Pills.Make(t, glyph, text, tip, primary, click);
        }

        static FrameworkElement Poster(Theme t, string url, bool tv, double w)
        {
            double h = Math.Round(w * 1.5);
            var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(Math.Max(3, w / 14)), Background = Theme.Faded(t.Fg, 10), VerticalAlignment = VerticalAlignment.Top };
            ImageSource img = SeerrPosters.Get(url);
            if (img != null) b.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            else b.Child = new TextBlock
            {
                Text = G(tv ? 0xE7F4 : 0xE8B2), FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(8, w * 0.35), Foreground = t.FgDim, Opacity = 0.6,
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
        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, SeerrSnapshot s, bool widget)
        {
            ExpireWaiting();
            BarSettings set = mgr.Settings;
            ViewState vs = StateOf(cfg);
            var root = new StackPanel();
            if (!widget) { root.Width = 440; root.Margin = new Thickness(4); }
            if (!SeerrClient.Configured(set))
            {
                var none = T(t, "Set up Seerr on the Plugins page of Settings (address and API key).", t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(none);
                return root;
            }

            // ---- header: counts, open Seerr ----
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (SrView.Of(cfg).ShowOpen)
            {
                var open = Pill(t, G(0xE8A7), "Seerr", "Open Seerr's requests page", false, delegate { OpenUrl(SeerrClient.PageUrl(set, "", 0)); });
                DockPanel.SetDock(open, Dock.Right);
                head.Children.Add(open);
            }
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(T(t, cfg.Title != null && cfg.Title.Length > 0 ? cfg.Title : "Seerr", t.Fg, 1, true));
            if (s.Ok)
            {
                int pending = Math.Max(s.Counts.Pending, s.Pending.Count);
                title.Children.Add(T(t, "   " + pending + " pending", pending > 0 ? Busy : t.FgDim, 0.88));
                title.Children.Add(T(t, " · " + s.Counts.Processing + " processing · " + s.Counts.Available + " available", t.FgDim, 0.88));
            }
            head.Children.Add(title);
            root.Children.Add(head);

            if (!s.Ok)
            {
                var w = T(t, s.Error.Length > 0 ? "Can't reach Seerr: " + s.Error : "Asking Seerr…", s.Error.Length > 0 ? Bad : t.FgDim);
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

            // ---- tabs ----
            // the ticked tabs (one = just that view, no tab strip)
            List<string> shownTabs = SrView.Of(cfg).TabsShown;
            string tab = shownTabs.Contains(vs.Tab) ? vs.Tab : shownTabs.Contains(SrView.Of(cfg).Tab) ? SrView.Of(cfg).Tab : shownTabs[0];
            if (shownTabs.Count > 1)
            {
                var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                foreach (string name in shownTabs)
                {
                    string n = name;
                    tabs.Children.Add(Chip(t, n, null, n == tab, delegate { vs.Tab = n; Bump(); }));
                }
                root.Children.Add(tabs);
            }

            if (!widget)
            {
                BuildTab(mgr, t, cfg, s, root, vs, widget, tab, null);
                return root;
            }

            // A widget keeps one size: every view it can switch to is built, stacked on top of each other, and all but the
            // chosen one hidden (hidden still takes its room) - so changing tab / filter doesn't resize the widget and push the
            // widgets snapped to it around. It also never shrinks below the biggest it has been with these settings.
            string filter = !SrView.Of(cfg).ShowFilters ? SrView.Of(cfg).Filter : vs.Filter;
            if (filter == "Mine" && s.MeId <= 0) filter = "All";
            var stack = new Grid();
            var tabsToBuild = shownTabs;
            foreach (string tb in tabsToBuild)
            {
                var filters = tb == "Requests" && SrView.Of(cfg).ShowFilters ? new List<string>(SrView.Filters) : new List<string> { tb == "Requests" ? filter : null };
                foreach (string f in filters)
                {
                    if (f == "Mine" && s.MeId <= 0) continue;
                    var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                    BuildTab(mgr, t, cfg, s, panel, vs, widget, tb, f);
                    bool shown = tb == tab && (tb != "Requests" || f == filter);
                    panel.Visibility = shown ? Visibility.Visible : Visibility.Hidden;
                    stack.Children.Add(panel);
                }
            }
            root.Children.Add(stack);

            SrView v = SrView.Of(cfg);
            string sig = v.Count + "|" + v.Posters + "|" + string.Join(",", shownTabs) + "|" + v.ShowFilters + "|" + v.Tab + "|" + v.Filter + "|" + t.FontSize + "|" + v.ShowOpen + "|" + v.PosterRows + "|" + cfg.TextSize;
            if (vs.SizeSig != sig) { vs.SizeSig = sig; vs.MaxW = 0; vs.MaxH = 0; }
            // keep the widest / tallest it has been, so switching tabs doesn't make the widget jump - but not the width when posters are on:
            // the poster grid fills whatever width it gets, so holding on to a width would stop a resized widget from getting narrower
            bool holdW = !SrView.Of(cfg).Posters;
            root.MinWidth = holdW ? vs.MaxW : 0;
            root.MinHeight = vs.MaxH;
            root.SizeChanged += delegate
            {
                // posters: their height follows the width they get, so a new width starts the tallest-so-far again
                // (otherwise a wider / earlier layout leaves empty space under the posters)
                if (!holdW && Math.Abs(root.ActualWidth - vs.AtW) > 2)
                {
                    vs.AtW = root.ActualWidth; vs.MaxH = 0;
                    if (root.MinHeight > 0) { root.MinHeight = 0; return; }     // measures again at its real height
                }
                if (holdW && root.ActualWidth > vs.MaxW) vs.MaxW = root.ActualWidth;
                if (root.ActualHeight > vs.MaxH) vs.MaxH = root.ActualHeight;
            };
            return root;
        }

        static void BuildTab(BarManager mgr, Theme t, ModuleConfig cfg, SeerrSnapshot s, StackPanel into, ViewState vs, bool widget, string tab, string filter)
        {
            switch (tab)
            {
                case "Recently added": BuildRecent(mgr, t, cfg, s, into); break;
                case "Trending":
                case "Upcoming": BuildDiscover(mgr, t, cfg, s, into, tab); break;
                default: BuildRequests(mgr, t, cfg, s, into, vs, widget, filter); break;
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

        /// <summary>The requests a filter shows.</summary>
        public static List<SeerrRequest> Filter(SeerrSnapshot s, string filter)
        {
            switch (filter)
            {
                case "Pending": return new List<SeerrRequest>(s.Pending);
                case "Processing": return s.Requests.FindAll(delegate (SeerrRequest q) { string st = q.State; return st == "approved" || st == "processing" || st == "partly"; });
                case "Available": return s.Requests.FindAll(delegate (SeerrRequest q) { string st = q.State; return st == "available" || st == "partly"; });
                case "Mine": return new List<SeerrRequest>(s.Mine);
                default: return new List<SeerrRequest>(s.Requests);
            }
        }

        static void BuildRequests(BarManager mgr, Theme t, ModuleConfig cfg, SeerrSnapshot s, StackPanel root, ViewState vs, bool widget, string only)
        {
            string filter = only ?? (widget && !SrView.Of(cfg).ShowFilters ? SrView.Of(cfg).Filter : vs.Filter);
            if (filter == "Mine" && s.MeId <= 0) filter = "All";
            if (!widget || SrView.Of(cfg).ShowFilters)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                foreach (string f in SrView.Filters)
                {
                    if (f == "Mine" && s.MeId <= 0) continue;
                    string ff = f;
                    string count = f == "Pending" ? (s.Pending.Count > 0 ? s.Pending.Count.ToString() : null) : null;
                    chips.Children.Add(Chip(t, f, count, f == filter, delegate { vs.Filter = ff; Bump(); }));
                }
                root.Children.Add(chips);
            }
            List<SeerrRequest> list = Filter(s, filter);
            if (list.Count == 0)
            {
                string none = filter == "Pending" ? "Nothing is waiting for approval." : filter == "Mine" ? "You haven't requested anything lately." :
                              filter == "Processing" ? "Nothing is being processed right now." : filter == "Available" ? "None of the latest requests is available yet." : "No requests yet.";
                var nb = T(t, none, t.FgDim, 0.9);
                nb.Margin = new Thickness(2, 4, 0, 4);
                root.Children.Add(nb);
                return;
            }
            int n = 0;
            foreach (SeerrRequest q in list)
            {
                if (n++ >= SrView.Of(cfg).Count)
                {
                    var more = T(t, "+ " + (list.Count - SrView.Of(cfg).Count) + " more in Seerr", t.FgDim, 0.85);
                    more.Margin = new Thickness(4, 3, 0, 0);
                    more.Cursor = Cursors.Hand;
                    more.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(SeerrClient.PageUrl(mgr.Settings, "", 0)); };
                    root.Children.Add(more);
                    break;
                }
                root.Children.Add(RequestRow(mgr, t, cfg, s, q));
            }
        }

        static FrameworkElement RequestRow(BarManager mgr, Theme t, ModuleConfig cfg, SeerrSnapshot s, SeerrRequest q)
        {
            Pending p;
            bool waiting = Waiting.TryGetValue("r" + q.Id, out p);
            if (waiting && p.Done && q.State != "pending") { Waiting.Remove("r" + q.Id); waiting = false; }

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            double ph = t.FontSize * 3.1;
            if (SrView.Of(cfg).Posters)
            {
                var poster = Poster(t, q.Poster, q.IsTv, Math.Round(ph / 1.5));
                ((FrameworkElement)poster).Margin = new Thickness(0, 0, 8, 0);
                g.Children.Add(poster);
            }
            else
            {
                double d = 8;
                var dot = new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = StateBrush(q.State, t), Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(dot);
            }

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(T(t, q.Title, t.Fg, 0.95, true));
            if (q.Year.Length > 0) top.Children.Add(T(t, "  " + q.Year, t.FgDim, 0.85));
            info.Children.Add(top);
            var who = new List<string> { q.KindText };
            if (q.RequestedBy.Length > 0) who.Add(q.RequestedById == s.MeId && s.MeId > 0 ? "you" : q.RequestedBy);
            string ago = SeerrClient.Ago(q.Created);
            if (ago.Length > 0) who.Add(ago);
            info.Children.Add(T(t, string.Join(" · ", who), t.FgDim, 0.8));
            if (SrView.Of(cfg).Posters)
            {
                var st = T(t, waiting ? p.Text : q.StateText, waiting ? Busy : StateBrush(q.State, t), 0.8);
                st.Margin = new Thickness(0, 1, 0, 0);
                info.Children.Add(st);
            }
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            if (!SrView.Of(cfg).Posters) right.Children.Add(T(t, waiting ? p.Text : q.StateText, waiting ? Busy : StateBrush(q.State, t), 0.8));
            FrameworkElement rowRef = null;
            if (q.State == "pending" && s.CanManage && !waiting)
            {
                right.Children.Add(Pill(t, G(0xE73E), "", "Approve", true, delegate { Decide(mgr, rowRef, q, true); }));
                right.Children.Add(Pill(t, G(0xE711), "", "Decline", false, delegate { Decide(mgr, rowRef, q, false); }));
            }
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            var tip = new List<string> { q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : "") + " - " + q.StateText, q.KindText };
            if (q.RequestedBy.Length > 0) tip.Add("Requested by " + q.RequestedBy + (q.Created != DateTime.MinValue ? " " + TimeText.Stamp(q.Created.ToLocalTime()) : ""));
            tip.Add("Click: open in Seerr · right-click: more");
            var row = new Border { Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = string.Join("\n", tip) };
            rowRef = row;
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(SeerrClient.PageUrl(mgr.Settings, q.MediaType, q.TmdbId)); };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; RequestMenu(mgr, row, s, q); };
            return row;
        }

        static void RequestMenu(BarManager mgr, FrameworkElement target, SeerrSnapshot s, SeerrRequest q)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : "") + " - " + q.StateText, IsEnabled = false });
            m.Items.Add(new Separator());
            if (q.State == "pending" && s.CanManage)
            {
                var a = new MenuItem { Header = "Approve" };
                a.Click += delegate { Decide(mgr, target, q, true); };
                m.Items.Add(a);
                var d = new MenuItem { Header = "Decline…" };
                d.Click += delegate { Decide(mgr, target, q, false); };
                m.Items.Add(d);
                m.Items.Add(new Separator());
            }
            var open = new MenuItem { Header = "Open in Seerr" };
            open.Click += delegate { OpenUrl(SeerrClient.PageUrl(mgr.Settings, q.MediaType, q.TmdbId)); };
            m.Items.Add(open);
            var copy = new MenuItem { Header = "Copy the title" };
            copy.Click += delegate { try { Clipboard.SetText(q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : "")); } catch { } };
            m.Items.Add(copy);
            ShowMenu(m, target);
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
            try
            {
                MessageBoxResult r = Questions.YesNo(owner, question, "Seerr");
                return r == MessageBoxResult.Yes;
            }
            finally { if (host != null) host.Pinned = false; _asking = MenuOpen = false; }
        }

        /// <summary>Approve (straight away) or decline (asks first) a pending request.</summary>
        public static async void Decide(BarManager mgr, FrameworkElement target, SeerrRequest q, bool approve)
        {
            if (!approve && !Ask(target, "Decline the request for " + q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : "") +
                                          (q.RequestedBy.Length > 0 ? " by " + q.RequestedBy : "") + "?")) return;
            MenuOpen = false;
            string key = "r" + q.Id;
            Waiting[key] = new Pending { Text = approve ? "approving…" : "declining…", Until = DateTime.UtcNow.AddSeconds(60) };
            Bump();
            string err = await SeerrClient.DecideAsync(mgr.Settings, q.Id, approve);
            if (err != null)
            {
                Waiting.Remove(key);
                Bump();
                MessageBox.Show("Seerr: " + err, "Seerr", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Waiting[key] = new Pending { Text = approve ? "Approved ✓" : "Declined", Until = DateTime.UtcNow.AddSeconds(45), Done = true };
            Bump();
        }

        // ---- Recently added ----
        static void BuildRecent(BarManager mgr, Theme t, ModuleConfig cfg, SeerrSnapshot s, StackPanel root)
        {
            if (s.Recent.Count == 0)
            {
                root.Children.Add(T(t, s.Fetched != DateTime.MinValue && (DateTime.UtcNow - s.Fetched).TotalSeconds < 60 ? "Nothing added yet." : "Asking Seerr…", t.FgDim, 0.9));
                SeerrClient.Poke();
                return;
            }
            var items = new List<SeerrItem>();
            var added = new Dictionary<SeerrItem, DateTime>();
            foreach (SeerrMedia m in s.Recent)
            {
                var it = new SeerrItem { Id = m.TmdbId, MediaType = m.MediaType, Title = m.Title, Year = m.Year, Poster = m.Poster, Status = m.Status };
                items.Add(it);
                added[it] = m.Added;
            }
            AddTitles(mgr, t, cfg, root, "Recently added", items, delegate (SeerrItem it) { DateTime a; return added.TryGetValue(it, out a) ? "added " + SeerrClient.Ago(a) : ""; }, false);
        }

        // ---- Trending / Upcoming ----
        static void BuildDiscover(BarManager mgr, Theme t, ModuleConfig cfg, SeerrSnapshot s, StackPanel root, string which)
        {
            string err;
            List<SeerrItem> items = SeerrClient.DiscoverList(mgr.Settings, which, out err, Bump);
            if (items == null)
            {
                var w = T(t, err != null ? "Seerr: " + err : "Loading " + which.ToLowerInvariant() + "…", err != null ? Bad : t.FgDim, 0.9);
                w.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(w);
                return;
            }
            if (items.Count == 0) { root.Children.Add(T(t, "Nothing to show.", t.FgDim, 0.9)); return; }
            AddTitles(mgr, t, cfg, root, which, items, delegate (SeerrItem it)
            {
                if (which == "Upcoming" && it.Date.Length >= 10)
                {
                    DateTime d;
                    if (DateTime.TryParse(it.Date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) return d.ToString("d MMM");
                }
                return it.Year;
            }, true);
        }

        /// <summary>Titles as a poster grid (posters on) or a list, each with its status or a Request button.</summary>
        static void AddTitles(BarManager mgr, Theme t, ModuleConfig cfg, StackPanel root, string view, List<SeerrItem> items, Func<SeerrItem, string> sub, bool requestable)
        {
            int count = Math.Min(items.Count, SrView.Of(cfg).Count);     // posters too: exactly "How many" (it used to show at least 5)
            if (SrView.Of(cfg).Posters)
            {
                double pw = Math.Max(48, Math.Round(t.FontSize * 5.2));
                // the posters fill the width they're given (a widget you've sized: as many across as fit, sized to fill each row)
                // in a widget you've sized, the posters fill a set number of rows: more items are made than "How many" so a wider
                // layout (or smaller text) can show more; PosterWrap shows the ones that fit
                var wrap = new PosterWrap { BaseW = pw, Count = count, Cfg = cfg, Save = mgr.SaveSoon };
                int made = Math.Min(items.Count, Math.Max(count, 60));
                for (int i = 0; i < made; i++)
                {
                    var tile = Tile(mgr, t, items[i], sub(items[i]), requestable, pw);
                    tile.Margin = new Thickness(0);
                    wrap.Children.Add(tile);
                }
                if (made <= count) { root.Children.Add(wrap); return; }

                // more than fit: an arrow either side pages through them, a page ("How many") at a time
                ViewState vs = StateOf(cfg);
                int last = made - count, off;
                if (!vs.Page.TryGetValue(view, out off)) off = 0;
                off = Math.Max(0, Math.Min(last, off));
                wrap.Offset = off;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Border left = null, right = null;
                Action sync = delegate
                {
                    left.Opacity = wrap.Offset > 0 ? 1 : 0.25; left.IsHitTestVisible = wrap.Offset > 0;
                    right.Opacity = wrap.Offset < last ? 1 : 0.25; right.IsHitTestVisible = wrap.Offset < last;
                };
                Action<int> go = delegate (int dir)
                {
                    int to = Math.Max(0, Math.Min(last, wrap.Offset + dir * count));
                    if (to == wrap.Offset) return;
                    wrap.Offset = to; vs.Page[view] = to;
                    wrap.InvalidateMeasure();
                    sync();
                };
                left = PageArrow(t, 0xE76B, delegate { go(-1); });
                right = PageArrow(t, 0xE76C, delegate { go(1); });
                left.Margin = new Thickness(0, 0, 6, 0); right.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(wrap, 1); Grid.SetColumn(right, 2);
                g.Children.Add(left); g.Children.Add(wrap); g.Children.Add(right);
                sync();
                root.Children.Add(g);
            }
            else
                for (int i = 0; i < count; i++) root.Children.Add(TitleRow(mgr, t, items[i], sub(items[i]), requestable));
        }

        static string Key(SeerrItem it) { return it.MediaType + ":" + it.Id; }

        /// <summary>A round ‹ / › button beside the posters.</summary>
        static Border PageArrow(Theme t, int glyph, Action click)
        {
            double d = Math.Round(Math.Max(22, t.FontSize * 1.9));
            var b = new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x26, t.Fg.Color.R, t.Fg.Color.G, t.Fg.Color.B)),
                Child = new TextBlock
                {
                    Text = G(glyph), FontFamily = IconUtil.GlyphFont, FontSize = Math.Round(d * 0.45), Foreground = t.Fg,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Brush idle = b.Background, hot = new SolidColorBrush(Color.FromArgb(0x55, t.MainAccent.Color.R, t.MainAccent.Color.G, t.MainAccent.Color.B));
            b.MouseEnter += delegate { b.Background = hot; };
            b.MouseLeave += delegate { b.Background = idle; };
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };     // not a widget drag
            b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        static FrameworkElement StatusOrButton(BarManager mgr, Theme t, SeerrItem it, bool requestable, Func<FrameworkElement> target)
        {
            Pending p;
            if (Waiting.TryGetValue(Key(it), out p)) return T(t, p.Text, p.Done ? Good : Busy, 0.78);
            if (requestable && it.CanRequest)
                return Pill(t, "", it.IsTv && it.Status == 4 ? "Request more" : "Request", it.IsTv ? "Choose the seasons to request" : "Request this movie", true,
                            delegate { RequestItem(mgr, target(), it); });
            Brush b = it.Status >= 4 ? Good : it.Status >= 2 ? Blue : t.FgDim;
            return T(t, it.Status <= 1 ? "" : it.StatusText, b, 0.78);
        }

        static FrameworkElement Tile(BarManager mgr, Theme t, SeerrItem it, string sub, bool requestable, double pw)
        {
            var col = new StackPanel { Width = pw, Margin = new Thickness(0, 0, 10, 8) };
            var poster = (Border)Poster(t, it.Poster, it.IsTv, pw);
            col.Tag = poster;         // PosterWrap resizes it
            poster.Cursor = Cursors.Hand;
            // a small corner mark for what's already there
            if (it.Status >= 2)
            {
                var mark = new Border
                {
                    Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = it.Status >= 4 ? Good : Blue, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5)
                };
                var holder = new Grid();
                holder.Children.Add(poster);
                holder.Children.Add(mark);
                col.Children.Add(holder);
            }
            else col.Children.Add(poster);
            poster.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            poster.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(SeerrClient.PageUrl(mgr.Settings, it)); };
            var name = T(t, it.Title, t.Fg, 0.78);
            name.Margin = new Thickness(0, 3, 0, 0);
            col.Children.Add(name);
            if (!string.IsNullOrEmpty(sub)) col.Children.Add(T(t, sub, t.FgDim, 0.72));
            FrameworkElement act = StatusOrButton(mgr, t, it, requestable, delegate { return col; });
            act.HorizontalAlignment = HorizontalAlignment.Left;
            act.Margin = new Thickness(0, 2, 0, 0);
            col.Children.Add(act);
            col.ToolTip = TipFor(it);
            return col;
        }

        static FrameworkElement TitleRow(BarManager mgr, Theme t, SeerrItem it, string sub, bool requestable)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(t, it.Title, t.Fg, 0.92));
            info.Children.Add(T(t, "  " + (it.IsTv ? "TV" : "Movie") + (string.IsNullOrEmpty(sub) ? "" : " · " + sub), t.FgDim, 0.8));
            g.Children.Add(info);
            Border row = null;
            FrameworkElement act = StatusOrButton(mgr, t, it, requestable, delegate { return row; });
            act.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(act, 1);
            g.Children.Add(act);
            row = new Border { Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = TipFor(it) };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(SeerrClient.PageUrl(mgr.Settings, it)); };
            return row;
        }

        static object TipFor(SeerrItem it)
        {
            string head = it.Title + (it.Year.Length > 0 ? " (" + it.Year + ")" : "") + "\n" + (it.IsTv ? "TV show" : "Movie") + " · " + it.StatusText;
            var tb = new TextBlock { Text = head + (it.Overview.Length > 0 ? "\n\n" + it.Overview : "") + "\n\nClick the poster to open it in Seerr", TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
            return tb;
        }

        /// <summary>Request a movie straight away; for a show pick the seasons from a menu first.</summary>
        static async void RequestItem(BarManager mgr, FrameworkElement target, SeerrItem it)
        {
            if (!it.IsTv) { DoRequest(mgr, it, null); return; }
            string key = Key(it);
            Waiting[key] = new Pending { Text = "getting seasons…", Until = DateTime.UtcNow.AddSeconds(20) };
            Bump();
            List<SeerrSeason> seasons;
            try { seasons = await SeerrClient.SeasonsAsync(mgr.Settings, it.Id); }
            catch (Exception ex)
            {
                Waiting.Remove(key);
                Bump();
                MessageBox.Show("Seerr: " + ex.Message, "Seerr", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Waiting.Remove(key);
            Bump();
            var open = seasons.FindAll(delegate (SeerrSeason x) { return x.Status < 2; });
            if (open.Count == 0) { MessageBox.Show("Every season of " + it.Title + " is already requested or available.", "Seerr", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = "Request " + it.Title, IsEnabled = false });
            m.Items.Add(new Separator());
            var all = new MenuItem { Header = open.Count == seasons.Count ? "All seasons (" + seasons.Count + ")" : "All the missing seasons (" + open.Count + ")" };
            List<int> allNums = open.ConvertAll(delegate (SeerrSeason x) { return x.Number; });
            all.Click += delegate { DoRequest(mgr, it, open.Count == seasons.Count ? null : allNums); };
            m.Items.Add(all);
            if (open.Count > 1)
            {
                SeerrSeason last = open[open.Count - 1];
                var latest = new MenuItem { Header = "Only the latest (season " + last.Number + ")" };
                latest.Click += delegate { DoRequest(mgr, it, new List<int> { last.Number }); };
                m.Items.Add(latest);
                m.Items.Add(new Separator());
                foreach (SeerrSeason x in open)
                {
                    SeerrSeason ss = x;
                    var mi = new MenuItem { Header = "Season " + ss.Number + (ss.Episodes > 0 ? "  (" + ss.Episodes + " episodes)" : "") };
                    mi.Click += delegate { DoRequest(mgr, it, new List<int> { ss.Number }); };
                    m.Items.Add(mi);
                }
            }
            m.Items.Add(new Separator());
            var web = new MenuItem { Header = "Choose in Seerr…" };
            web.Click += delegate { OpenUrl(SeerrClient.PageUrl(mgr.Settings, it)); };
            m.Items.Add(web);
            if (target == null || PresentationSource.FromVisual(target) == null) { m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint; m.IsOpen = true; return; }
            ShowMenu(m, target);
        }

        static async void DoRequest(BarManager mgr, SeerrItem it, List<int> seasons)
        {
            MenuOpen = false;
            string key = Key(it);
            Waiting[key] = new Pending { Text = "requesting…", Until = DateTime.UtcNow.AddSeconds(30) };
            Bump();
            string err = await SeerrClient.RequestAsync(mgr.Settings, it, seasons);
            if (err != null)
            {
                Waiting.Remove(key);
                Bump();
                MessageBox.Show("Seerr: " + err, "Seerr", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            it.Status = Math.Max(it.Status, 2);
            Waiting[key] = new Pending { Text = "Requested ✓", Until = DateTime.UtcNow.AddSeconds(90), Done = true };
            SeerrClient.Requested();
            Bump();
        }
    }

    /// <summary>Posters (TMDB's small ones), downloaded once and kept for the session.</summary>
    public static class SeerrPosters
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
                        bmp.DecodePixelWidth = 140;
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

    /// <summary>
    /// The Seerr poster grid: as many posters across as fit the width it's given, each sized so the rows fill it (between a little
    /// under and a good bit over their usual size; past that the rows are centred). Without a set width (a widget that sizes itself)
    /// it's five across at the usual size.
    /// </summary>
    sealed class PosterWrap : Panel
    {
        public double BaseW = 64;
        /// <summary>How many to show without a set width (and how the rows are worked out the first time).</summary>
        public int Count = 8;
        public ModuleConfig Cfg;
        public Action Save;
        /// <summary>The first poster shown (the arrows page through the rest).</summary>
        public int Offset;
        const double Gap = 10, RowGap = 8;
        int _cols = 1, _shown;
        double _cell;

        static void SizeTile(UIElement child, double w)
        {
            var col = child as StackPanel;
            if (col == null || Math.Abs(col.Width - w) < 0.5) return;
            col.Width = w;
            var poster = col.Tag as Border;
            if (poster == null) return;
            poster.Width = w;
            poster.Height = Math.Round(w * 1.5);
            poster.CornerRadius = new CornerRadius(Math.Max(3, w / 14));
        }

        protected override Size MeasureOverride(Size avail)
        {
            int n = InternalChildren.Count;
            double W = avail.Width;
            // Build 2: "How many" posters in the set number of columns; the widget sizes itself to them. In a widget with a set width
            // the posters are sized so the columns fill it (within reason).
            int off = Math.Max(0, Math.Min(Offset, n - 1));
            _shown = Math.Max(0, Math.Min(n - off, Count));
            int rows = Math.Max(1, Cfg != null ? SrView.Of(Cfg).PosterRows : 2);
            _cols = Math.Max(1, (int)Math.Ceiling(Math.Max(1, _shown) / (double)rows));     // "How many" spread over the set rows
            if (double.IsInfinity(W) || W <= 0) _cell = BaseW;
            else _cell = Math.Max(BaseW * 0.6, Math.Min(BaseW * 1.7, (W - Gap * (_cols - 1)) / _cols));
            double total = 0, rowH = 0;
            for (int k = 0; k < n; k++)
            {
                UIElement c = InternalChildren[k];
                int i = k - off;     // its place on this page
                bool on = i >= 0 && i < _shown;
                Visibility vis = on ? Visibility.Visible : Visibility.Collapsed;
                if (c.Visibility != vis) c.Visibility = vis;
                if (!on) { c.Measure(new Size(0, 0)); continue; }
                SizeTile(c, _cell);
                c.Measure(new Size(_cell, double.PositiveInfinity));
                rowH = Math.Max(rowH, c.DesiredSize.Height);
                if (i % _cols == _cols - 1 || i == _shown - 1) { total += rowH + (i == _shown - 1 ? 0 : RowGap); rowH = 0; }
            }
            double gridW = _cols * _cell + Gap * (_cols - 1);
            return new Size(double.IsInfinity(W) ? gridW : Math.Min(W, gridW), total);
        }

        protected override Size ArrangeOverride(Size final)
        {
            int off = Math.Max(0, Math.Min(Offset, InternalChildren.Count - 1)), n = _shown;
            for (int i = 0; i < InternalChildren.Count; i++)
                if (i < off || i >= off + n) InternalChildren[i].Arrange(new Rect(0, 0, 0, 0));     // the ones on other pages
            double gridW = _cols * _cell + Gap * (_cols - 1);
            double x0 = Math.Max(0, (final.Width - gridW) / 2), y = 0;
            for (int r = 0; r * _cols < n; r++)
            {
                double rowH = 0;
                for (int k = 0; k < _cols && r * _cols + k < n; k++) rowH = Math.Max(rowH, InternalChildren[off + r * _cols + k].DesiredSize.Height);
                for (int k = 0; k < _cols && r * _cols + k < n; k++)
                    InternalChildren[off + r * _cols + k].Arrange(new Rect(x0 + k * (_cell + Gap), y, _cell, rowH));
                y += rowH + RowGap;
            }
            return final;
        }
    }
}
