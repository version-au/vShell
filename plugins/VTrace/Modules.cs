using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VShellVTrace
{
    /// <summary>A module's own settings (its ModuleConfig's bag).</summary>
    public sealed class TraceView
    {
        readonly SettingsBag _b;
        TraceView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static TraceView Of(ModuleConfig cfg) { return new TraceView(cfg); }
        public int Rows { get { return Math.Max(1, Math.Min(30, _b.Get("Rows", 6))); } set { _b.Set("Rows", value); } }
        public bool Chart { get { return _b.Get("Chart", true); } set { _b.Set("Chart", value); } }
        /// <summary>Only the issues: outages (still going or back up) and changes that failed.</summary>
        public bool IssuesOnly { get { return _b.Get("IssuesOnly", false); } set { _b.Set("IssuesOnly", value); } }
    }

    /// <summary>Drawing vTrace's changes: the list (a widget, the bar's popup), today's chart, colours.</summary>
    static class TraceDraw
    {
        static readonly SolidColorBrush Teal = Frozen(0x89, 0xDC, 0xEB), Sky = Frozen(0x74, 0xC7, 0xEC), Sand = Frozen(0xF9, 0xE2, 0xAF), Mauve = Frozen(0xCB, 0xA6, 0xF7);

        static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        /// <summary>A change's dot: red when it failed / is an outage, else its source's colour (as in vTrace).</summary>
        public static Brush Dot(Theme t, Change c)
        {
            if (c.Failed || c.Source == "outage") return StatusColors.Bad;
            switch (c.Source)
            {
                case "komodo": return StatusColors.Good;
                case "proxmox": return StatusColors.Warn;
                case "server": return Mauve;
                case "ha": return Teal;
                case "unifi": return Sky;
                case "npm": return Sand;
                default: return t.MainAccent;
            }
        }

        public static List<Change> Today(List<Change> all) { return all.Where(c => c.When.Date == DateTime.Today).ToList(); }
        /// <summary>An issue: an outage (going or over) or a change that failed.</summary>
        public static bool IsIssue(Change c) { return c.Failed || c.Source == "outage"; }
        public static List<Change> Down(List<Change> all) { return all.Where(c => c.Down).ToList(); }

        /// <summary>
        /// Outages still going, one per group (vTrace groups the ones that went down together on the same machine): a group's row, or
        /// one whose row has already come back.
        /// </summary>
        public static List<Change> DownShown(List<Change> all)
        {
            var downIds = new HashSet<string>(all.Where(c => c.Down).Select(c => c.Id));
            return all.Where(c => c.Down && (!c.Folded || !downIds.Contains(c.Group))).ToList();
        }

        /// <summary>A click opens vTrace on that change (the press is handled too, so a widget doesn't start a drag).</summary>
        static void OpensChange(FrameworkElement el, string id)
        {
            el.Cursor = Cursors.Hand;
            el.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            el.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TraceData.OpenApp(id); };
        }

        /// <summary>An outage's tip: what it runs on, the likely cause and why.</summary>
        public static string OutageTip(Change c)
        {
            var l = new List<string> { c.Title + (c.WithMore.Length > 0 ? " (" + c.WithMore + ")" : "") };
            if (c.Detail.Length > 0) l.Add(c.Detail);
            if (c.RunsOn.Length > 0) l.Add("Runs on: " + c.RunsOn);
            if (c.Cause.Length > 0) l.Add(c.Cause + (c.CauseWhy.Length > 0 ? "\n  " + c.CauseWhy : ""));
            if (c.Explanation.Length > 0) l.Add("\nLocal AI" + (c.AiBy.Length > 0 ? " (" + c.AiBy + ")" : "") + ":\n" + Wrap(c.Explanation, 80));
            return string.Join("\n", l);
        }

        /// <summary>Long text in lines of about n characters (a tooltip doesn't wrap by itself).</summary>
        static string Wrap(string text, int n)
        {
            var b = new StringBuilder();
            foreach (string para in text.Replace("\r", "").Split('\n'))
            {
                int line = 0;
                foreach (string w in para.Split(' '))
                {
                    if (line > 0 && line + w.Length + 1 > n) { b.Append('\n'); line = 0; }
                    else if (line > 0) { b.Append(' '); line++; }
                    b.Append(w);
                    line += w.Length;
                }
                b.Append('\n');
            }
            return b.ToString().TrimEnd();
        }

        /// <summary>What's shown, as text: rebuild only when this changes.</summary>
        public static string Signature(List<Change> all, TraceView v)
        {
            bool open = TraceData.AppOpen;
            var b = new StringBuilder(v.Rows + "|" + v.Chart + "|" + v.IssuesOnly + "|" + DateTime.Today.Ticks + "|" + TraceData.Error + "|" + open + "|" + (open ? "" : TraceData.CheckedText() + TraceData.Stale));
            foreach (Change c in all.Take(v.Rows + 40)) b.Append('|').Append(c.Id).Append(c.Title).Append(c.Until.Ticks).Append(c.Group).Append(c.GroupCount).Append(c.Cause).Append(c.Explanation.Length);
            return b.ToString();
        }

        static TextBlock Txt(Theme t, string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = fg, FontFamily = t.Font, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        static string Clock(DateTime d) { return TimeText.Clock(d); }

        /// <summary>
        /// The refresh button (only while vTrace isn't open): vTrace looks at every source once with no window, then closes. Dimmed while
        /// it's looking. after = redraw now.
        /// </summary>
        public static TextBlock RefreshGlyph(Theme t, double size, Action after)
        {
            bool busy = TraceData.Refreshing;
            var g = new TextBlock
            {
                Text = "", FontFamily = IconUtil.GlyphFont, FontSize = size, Foreground = t.MainAccent, VerticalAlignment = VerticalAlignment.Center,
                Cursor = busy ? Cursors.Arrow : Cursors.Hand, Opacity = busy ? 0.4 : 0.9, Background = Brushes.Transparent, Padding = new Thickness(4, 2, 2, 2),
                ToolTip = busy ? "Checking for changes…" : "Check for changes now (vTrace looks once in the background, then closes)"
            };
            g.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            g.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (TraceData.Refreshing) return;
                TraceData.Refresh();
                if (after != null) after();
            };
            return g;
        }

        /// <summary>
        /// The All / Issues switch in the heading: a click shows the other and remembers it (the module's Only issues setting).
        /// </summary>
        static FrameworkElement IssuesSwitch(Theme t, TraceView v, double fs, Action changed)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            foreach (bool issues in new[] { false, true })
            {
                bool on = v.IssuesOnly == issues;
                bool pick = issues;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Cursor = on ? Cursors.Arrow : Cursors.Hand,
                    Background = on ? t.MainAccent.Clone() : Brushes.Transparent,
                    ToolTip = issues ? "Only issues: outages and changes that failed" : "Every change",
                    Child = Txt(t, issues ? "Issues" : "All", on ? t.Fg : t.FgDim, fs - 2, on)
                };
                if (on) b.Background.Opacity = 0.3;
                b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (v.IssuesOnly == pick) return;
                    v.IssuesOnly = pick;
                    if (changed != null) changed();
                };
                sp.Children.Add(b);
            }
            return sp;
        }

        /// <summary>
        /// A heading ("Changes · 12 today" with the All / Issues switch, and while vTrace isn't open: the refresh button and "Checked 2:14
        /// PM · 3 h ago"), outages still going, today's chart, then the latest changes (or only the issues). Click = open vTrace.
        /// switched = the All / Issues switch was clicked (save, redraw).
        /// </summary>
        public static FrameworkElement List(Theme t, List<Change> all, TraceView v, double fs, bool openButton, Action redraw, Action switched)
        {
            var st = new StackPanel();
            if (TraceData.Error != null) { st.Children.Add(Txt(t, TraceData.Error, StatusColors.Warn, fs - 1)); return st; }
            bool issuesOnly = v.IssuesOnly;
            List<Change> listed = issuesOnly ? all.Where(IsIssue).ToList() : all;
            List<Change> today = Today(listed);
            bool closed = !TraceData.AppOpen;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, closed ? 1 : 6), LastChildFill = false };
            if (closed)
            {
                TextBlock r = RefreshGlyph(t, fs, redraw);
                DockPanel.SetDock(r, Dock.Right);
                head.Children.Add(r);
            }
            FrameworkElement sw = IssuesSwitch(t, v, fs, switched);
            DockPanel.SetDock(sw, Dock.Right);
            head.Children.Add(sw);
            head.Children.Add(Txt(t, issuesOnly ? "Issues" : "Changes", t.Fg, fs + 1, true));
            head.Children.Add(Txt(t, "  ·  " + today.Count + " today", t.FgDim, fs - 1));
            st.Children.Add(head);
            if (closed)
            {
                var looked = Txt(t, TraceData.CheckedText(), TraceData.Stale && !TraceData.Refreshing ? StatusColors.Warn : t.FgDim, fs - 2);
                looked.Margin = new Thickness(0, 0, 0, 6);
                looked.ToolTip = "vTrace isn't open, so nothing new comes in by itself - the refresh button checks now.";
                st.Children.Add(looked);
            }
            // outages still going, with their likely cause and the local AI's take: on Issues only (on All they're red rows in the list)
            List<Change> downShown = DownShown(all);
            foreach (Change d in issuesOnly ? downShown : new List<Change>())
            {
                var down = Txt(t, d.Title + (d.WithMore.Length > 0 ? " " + d.WithMore : "") + " - since " + Clock(d.When), StatusColors.Bad, fs, true);
                down.Margin = new Thickness(0, 0, 0, d.Cause.Length > 0 ? 1 : 4);
                down.ToolTip = OutageTip(d) + "\n\nClick to see it in vTrace";
                OpensChange(down, d.Id);
                st.Children.Add(down);
                // what vTrace thinks caused it - "View" opens vTrace on that change
                if (d.Cause.Length > 0)
                {
                    var cause = Txt(t, "", t.FgDim, fs - 2);
                    cause.Inlines.Add(new System.Windows.Documents.Run(d.Cause));
                    cause.Inlines.Add(new System.Windows.Documents.Run("  ·  View") { Foreground = t.MainAccent, FontWeight = FontWeights.SemiBold });
                    cause.Margin = new Thickness(0, 0, 0, 5);
                    cause.ToolTip = OutageTip(d) + "\n\nClick to see " + (d.CauseId.Length > 0 ? "this change" : "the outage") + " in vTrace";
                    OpensChange(cause, d.CauseId.Length > 0 ? d.CauseId : d.Id);
                    st.Children.Add(cause);
                }
                // vTrace's local AI on it: a few lines (the whole of it in the tip)
                if (d.Explanation.Length > 0)
                {
                    var ai = new TextBlock
                    {
                        Text = "AI: " + d.Explanation, Foreground = t.Fg, FontFamily = t.Font, FontSize = fs - 2, TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.WordEllipsis, MaxHeight = (fs - 2) * 1.4 * 3, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 6),
                        ToolTip = OutageTip(d) + "\n\nClick to see it in vTrace"
                    };
                    OpensChange(ai, d.Id);
                    st.Children.Add(ai);
                }
            }
            if (v.Chart) st.Children.Add(Chart(t, today, fs));
            if (all.Count == 0) st.Children.Add(Txt(t, closed ? "Nothing yet - refresh to check now, or open vTrace." : "Nothing yet - vTrace looks every few minutes while it runs.", t.FgDim, fs - 1));
            else if (listed.Count == 0) st.Children.Add(Txt(t, "No issues - nothing went down or failed.", t.FgDim, fs - 1));
            // the same change again and again (a file copied 4 times in a minute) is one row, "×4"
            var rows = new List<Tuple<Change, int>>();
            // (an outage that went down with others is in its group's row - "with 5 more on pve"; one still going is a row on All only)
            var downIds = new HashSet<string>(downShown.Select(d => d.Id));
            // (still going first, so an old one isn't pushed off the end by newer changes)
            IEnumerable<Change> shownRows = listed.Where(c => !c.Down && !(c.Folded && c.Source == "outage"));
            if (!issuesOnly) shownRows = listed.Where(c => c.Down && downIds.Contains(c.Id)).Concat(shownRows);
            foreach (Change c in shownRows)
            {
                if (rows.Count > 0)
                {
                    Change last = rows[rows.Count - 1].Item1;
                    if (last.Source == c.Source && last.Title == c.Title && last.Host == c.Host && (last.When - c.When).TotalMinutes <= 10)
                    { rows[rows.Count - 1] = Tuple.Create(last, rows[rows.Count - 1].Item2 + 1); continue; }
                }
                if (rows.Count >= v.Rows) break;
                rows.Add(Tuple.Create(c, 1));
            }
            foreach (var pair in rows)
            {
                Change c = pair.Item1;
                int times = pair.Item2;
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var dot = new Ellipse { Width = 8, Height = 8, Fill = Dot(t, c), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, fs * 0.45, 0, 0) };
                row.Children.Add(dot);
                var text = new StackPanel();
                text.Children.Add(Txt(t, c.Title + (times > 1 ? "  ×" + times : "") + (c.WithMore.Length > 0 ? "  ·  " + c.WithMore : "") + (c.Down ? "  ·  still down" : ""),
                                      c.Failed || c.Down ? StatusColors.Bad : t.Fg, fs));
                string source = TraceData.SourceName(c.Source);
                text.Children.Add(Txt(t, (c.When.Date == DateTime.Today ? Clock(c.When) : c.When.ToString("ddd") + " " + Clock(c.When)) + (c.Host.Length > 0 ? " · " + c.Host : "") +
                                         (string.Equals(c.Host, source, StringComparison.OrdinalIgnoreCase) ? "" : " · " + source), t.FgDim, fs - 2));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                row.ToolTip = (c.Source == "outage" ? OutageTip(c) : c.Title + (c.Detail.Length > 0 ? "\n" + c.Detail : "")) + "\n\nClick to see it in vTrace";
                row.Background = Brushes.Transparent;      // the whole row takes the click, not just its text
                OpensChange(row, c.Id);
                st.Children.Add(row);
            }
            if (openButton)
            {
                var open = Txt(t, "Open vTrace", t.MainAccent, fs - 1, true);
                open.Margin = new Thickness(0, 8, 0, 0);
                open.Cursor = Cursors.Hand;
                open.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TraceData.OpenApp(); };
                st.Children.Add(open);
            }
            return st;
        }

        /// <summary>Today by the hour: a bar per hour (the hour with an outage in red), 12 AM … now.</summary>
        static FrameworkElement Chart(Theme t, List<Change> today, double fs)
        {
            var counts = new int[24];
            var bad = new bool[24];
            foreach (Change c in today) { counts[c.When.Hour]++; if (c.Source == "outage" || c.Failed) bad[c.When.Hour] = true; }
            int max = Math.Max(1, counts.Max());
            var bars = new Grid { Height = 30, Margin = new Thickness(0, 2, 0, 2) };
            for (int h = 0; h < 24; h++)
            {
                bars.ColumnDefinitions.Add(new ColumnDefinition());
                var r = new Rectangle
                {
                    Height = counts[h] == 0 ? 1.5 : Math.Max(3, 30.0 * counts[h] / max), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0),
                    RadiusX = 1.5, RadiusY = 1.5, Fill = bad[h] ? StatusColors.Bad : h > DateTime.Now.Hour ? t.FgDim : t.MainAccent, Opacity = h > DateTime.Now.Hour ? 0.25 : 0.9
                };
                Grid.SetColumn(r, h);
                bars.Children.Add(r);
            }
            var scale = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            scale.Children.Add(Txt(t, "12 AM", t.FgDim, fs - 3));
            var mid = Txt(t, "12 PM", t.FgDim, fs - 3); mid.HorizontalAlignment = HorizontalAlignment.Center; scale.Children.Add(mid);
            var end = Txt(t, "11 PM", t.FgDim, fs - 3); end.HorizontalAlignment = HorizontalAlignment.Right; scale.Children.Add(end);
            var box = new StackPanel();
            box.Children.Add(bars);
            box.Children.Add(scale);
            return box;
        }
    }

    /// <summary>A timer that keeps going while the module is on screen (widgets rebuild while they fit: Unloaded can come after Loaded).</summary>
    static class ModuleTimer
    {
        public static DispatcherTimer Make(FrameworkElement el, TimeSpan every, Action tick)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = every };
            int gone = 0;
            timer.Tick += delegate
            {
                if (PresentationSource.FromVisual(el) == null) { if (++gone >= 5) timer.Stop(); return; }
                gone = 0;
                tick();
            };
            el.Loaded += delegate { tick(); timer.Start(); };
            el.IsVisibleChanged += delegate { if (el.IsVisible && !timer.IsEnabled) { tick(); timer.Start(); } };
            el.Unloaded += delegate { el.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { if (!el.IsLoaded) timer.Stop(); })); };
            timer.Start();
            return timer;
        }
    }

    /// <summary>On a bar: an outage still going ("Jellyfin down", red), else "12 today". Click: the latest changes in a popup.</summary>
    public sealed class TraceBarModule : Border
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly TextBlock _glyph, _text;
        readonly StackPanel _sp;
        TextBlock _refresh;
        PopupWindow _popup;
        string _shown, _popupShown;

        public TraceBarModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            _glyph = new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _text = new TextBlock { FontFamily = _t.Font, VerticalAlignment = VerticalAlignment.Center };
            _sp = new StackPanel { Orientation = Orientation.Horizontal };
            _sp.Children.Add(_glyph);
            _sp.Children.Add(_text);
            Child = _sp;
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            ModuleTimer.Make(this, TimeSpan.FromSeconds(5), Refresh);
            Refresh();
        }

        void Refresh()
        {
            List<Change> all = TraceData.All;
            List<Change> down = TraceDraw.Down(all), today = TraceDraw.Today(all);
            string text, tip;
            Brush fg = _t.Fg;
            if (TraceData.Error != null) { text = "!"; fg = StatusColors.Warn; tip = TraceData.Error; }
            else if (down.Count > 0)
            {
                // one per group: "pve: 6 down" when they all went down together on one machine
                List<Change> shown = TraceDraw.DownShown(all);
                if (down.Count == 1) text = down[0].Title.Replace(" went down", "") + " down";
                else if (shown.Count == 1 && shown[0].GroupOn.Length > 0) text = down.Count + " down on " + shown[0].GroupOn;
                else text = down.Count + " down";
                fg = StatusColors.Bad;
                tip = string.Join("\n\n", shown.Select(d => TraceDraw.OutageTip(d).Replace(d.Title, d.Title + " - since " + TimeText.Clock(d.When)))) + "\n\nClick for the latest";
            }
            else
            {
                text = today.Count + " today";
                tip = "vTrace: " + today.Count + " change" + (today.Count == 1 ? "" : "s") + " today" + (today.Count > 0 ? "\nLatest: " + today[0].Title + " (" + TimeText.Clock(today[0].When) + ")" : "") + "\nClick for the latest";
            }
            bool closed = !TraceData.AppOpen, busy = TraceData.Refreshing;
            if (closed && TraceData.Error == null) tip += "\n" + TraceData.CheckedText() + " (vTrace isn't open - the refresh button checks now)";
            bool hide = _cfg.HideIfNone && TraceData.Error == null && down.Count == 0 && today.Count == 0;
            string sig = text + "|" + hide + "|" + tip + "|" + closed + "|" + busy;
            if (_popup != null && _popup.IsOpen)
            {
                string ps = TraceDraw.Signature(all, TraceView.Of(_cfg));
                if (ps != _popupShown) { _popupShown = ps; _popup.SetContent(PopupContent(all)); }
            }
            if (sig == _shown) return;
            _shown = sig;
            _text.Text = text;
            _text.Foreground = fg;
            _glyph.Foreground = down.Count > 0 ? fg : _t.MainAccent;
            ToolTip = tip;
            // the refresh button, only while vTrace isn't open (it looks by itself when it is)
            if (_refresh != null) { _sp.Children.Remove(_refresh); _refresh = null; }
            if (closed)
            {
                _refresh = TraceDraw.RefreshGlyph(_t, Math.Max(10, _t.FontSize - 1), Redraw);
                _refresh.Margin = new Thickness(4, 0, 0, 0);
                _sp.Children.Add(_refresh);
            }
            Visibility vis = hide ? Visibility.Collapsed : Visibility.Visible;
            if (Visibility != vis) { Visibility = vis; _host.ContentChanged(); }
        }

        /// <summary>Show a refresh that just started (the bar and an open popup) without waiting for the timer.</summary>
        void Redraw()
        {
            _shown = null;
            _popupShown = null;
            Refresh();
        }

        FrameworkElement PopupContent(List<Change> all)
        {
            var box = new Border { Padding = new Thickness(12, 10, 12, 10), MinWidth = 300, MaxWidth = 460 };
            box.Child = TraceDraw.List(_t, all, TraceView.Of(_cfg), Math.Max(12, _t.FontSize), true, Redraw, delegate { _mgr.SaveSoon(); Redraw(); });
            return box;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            List<Change> all = TraceData.All;
            _popupShown = TraceDraw.Signature(all, TraceView.Of(_cfg));
            _popup = new PopupWindow(_mgr, _host, this, _t, PopupContent(all));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>In a widget: the latest changes (it sizes itself to them). Click: open vTrace.</summary>
    public sealed class TraceListModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        string _shown;

        public int RowCount { get { return 0; } }       // sizes itself

        public TraceListModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(2);
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TraceData.OpenApp(); };
            ModuleTimer.Make(this, TimeSpan.FromSeconds(5), Refresh);
            Refresh();
        }

        void Refresh()
        {
            List<Change> all = TraceData.All;
            TraceView v = TraceView.Of(_cfg);
            string sig = TraceDraw.Signature(all, v) + "|" + DateTime.Now.Hour;
            if (sig == _shown) return;
            _shown = sig;
            double fs = _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            Child = TraceDraw.List(_t, all, v, fs, false, delegate { _shown = null; Refresh(); }, delegate { _mgr.SaveSoon(); _shown = null; Refresh(); });
            _host.ContentChanged();
        }
    }
}
