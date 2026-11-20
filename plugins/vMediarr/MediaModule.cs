using System;
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

namespace VMediarr
{
    // =====================================================================================
    // The module: on a bar a dot, ↓ downloading, requests waiting; click -> downloads, requests, coming up; in a widget the same
    // =====================================================================================
    public sealed class MediaModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _pop;
        string _shown = "", _popShown = "";

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        SettingsBag B { get { return _cfg.Bag; } }

        public MediaModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = host.IsWidget ? VerticalAlignment.Top : VerticalAlignment.Center;
            Padding = host.IsWidget ? new Thickness(4, 2, 4, 2) : new Thickness(6, 0, 6, 0);
            if (!host.IsWidget)
            {
                Cursor = Cursors.Hand;
                MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            }
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { Refresh(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        void Refresh()
        {
            if (Plugin.Poll != null && IsVisible) Plugin.Poll.Want();
            VmSnapshot s = Plugin.Latest;
            string sig = Sig(s);
            if (_pop != null && _pop.IsOpen && sig != _popShown) { _popShown = sig; _pop.SetContent(List(s, true)); }
            if (sig == _shown) return;
            _shown = sig;
            Child = _host.IsWidget ? List(s, false) : BarView(s);
            _host.ContentChanged();
        }

        string Sig(VmSnapshot s)
        {
            var b = new StringBuilder(s.Ok + "|" + s.Checked + "|" + s.Error + "|" + s.Downloading + "|" + s.Problems + "|" + s.RequestsWaiting + "|" + _cfg.Title + "|" + B.Get("ShowComing", true) + "|" + B.Get("Count", 8.0));
            foreach (var q in s.Queue) b.Append('|').Append(q.Title).Append(q.State).Append(Math.Round(q.Progress)).Append(q.Message);
            foreach (var c in s.ComingUp) b.Append('|').Append(c.Title).Append(c.Episode);
            return b.ToString();
        }

        TextBlock T(string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text ?? "", Foreground = fg, FontFamily = _t.Font, FontSize = Math.Max(8, size), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        Ellipse Dot(Brush b, double size = 8) { return new Ellipse { Width = size, Height = size, Fill = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; }

        // ---------- on a bar ----------
        FrameworkElement BarView(VmSnapshot s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Brush dot = !s.Checked ? _t.FgDim : !s.Ok || s.Problems > 0 ? StatusColors.Bad : s.RequestsWaiting > 0 ? StatusColors.Warn : s.Downloading > 0 ? _t.MainAccent : (Brush)StatusColors.Good;
            row.Children.Add(Dot(dot));
            string title = (_cfg.Title ?? "").Trim();
            if (title.Length > 0) row.Children.Add(T(title + " ", _t.FgDim, _t.FontSize));
            if (!s.Checked) row.Children.Add(T("vMediarr …", _t.Fg, _t.FontSize));
            else if (!s.Ok) row.Children.Add(T("vMediarr: can't reach it", StatusColors.Bad, _t.FontSize));
            else
            {
                row.Children.Add(T("↓ " + s.Downloading, s.Downloading > 0 ? _t.Fg : _t.FgDim, _t.FontSize));
                if (s.RequestsWaiting > 0) row.Children.Add(T("  · " + s.RequestsWaiting + " asked", StatusColors.Warn, _t.FontSize));
                if (s.Problems > 0) row.Children.Add(T("  ⚠ " + s.Problems, StatusColors.Bad, _t.FontSize));
            }
            ToolTip = !s.Ok ? "vMediarr: " + (s.Checked ? s.Error : "looking…") : "vMediarr: " + Plugin.Summary(s) + "\nClick for the downloads, requests and what's coming";
            return row;
        }

        // ---------- the list: a widget, and the bar's popup ----------
        FrameworkElement List(VmSnapshot s, bool popup)
        {
            double fs = _host.IsWidget && _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            int count = Math.Max(1, (int)B.Get("Count", 8.0));
            var root = new StackPanel { Width = popup ? 360 : double.NaN, Margin = popup ? new Thickness(14, 12, 14, 12) : new Thickness(0) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (s.Ok)
            {
                var tiles = new StackPanel { Orientation = Orientation.Horizontal };
                tiles.Children.Add(Tile(s.Downloading.ToString(), "downloading", s.Downloading > 0 ? _t.MainAccent : _t.FgDim, fs));
                tiles.Children.Add(Tile(s.RequestsWaiting.ToString(), "asked", s.RequestsWaiting > 0 ? StatusColors.Warn : _t.FgDim, fs));
                DockPanel.SetDock(tiles, Dock.Right);
                head.Children.Add(tiles);
            }
            var name = T(_host.IsWidget && (_cfg.Title ?? "").Trim().Length > 0 ? _cfg.Title.Trim() : "Movies & series", _t.Fg, fs + 1, true);
            name.VerticalAlignment = VerticalAlignment.Top;
            Link(name, Plugin.HomeUrl("/activity"));
            head.Children.Add(name);
            root.Children.Add(head);

            if (!s.Checked) { root.Children.Add(T(VmSettings.Of(_mgr.Settings).Configured ? "Asking vMediarr…" : "Set up vMediarr in Settings > Plugins > vMediarr", _t.FgDim, fs)); return root; }
            if (!s.Ok) { var e = T(s.Error, StatusColors.Bad, fs - 1); e.TextWrapping = TextWrapping.Wrap; root.Children.Add(e); return root; }

            if (s.RequestsWaiting > 0)
            {
                var ask = T(s.RequestsWaiting + (s.RequestsWaiting == 1 ? " request is" : " requests are") + " waiting for you", StatusColors.Warn, fs - 1);
                ask.Margin = new Thickness(0, 0, 0, 6);
                Link(ask, Plugin.HomeUrl("/requests"));
                root.Children.Add(ask);
            }

            root.Children.Add(Heading("DOWNLOADING", fs, 0));
            if (s.Queue.Count == 0) root.Children.Add(T("Nothing right now", _t.FgDim, fs - 1));
            foreach (var q in s.Queue.Take(count))
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel();
                left.Children.Add(T(q.Title, q.Problem ? StatusColors.Bad : _t.Fg, fs));
                var bar = new Grid { Height = 3, Margin = new Thickness(0, 3, 0, 0), Background = _t.Track };
                bar.Children.Add(new Border { Background = q.Problem ? StatusColors.Bad : _t.MainAccent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Tag = q.Progress });
                bar.SizeChanged += delegate { foreach (Border b in bar.Children) b.Width = Math.Max(0, bar.ActualWidth * Math.Min(100, (double)b.Tag) / 100.0); };
                left.Children.Add(bar);
                if (q.Message.Length > 0) left.Children.Add(T(q.Message, q.Problem ? StatusColors.Bad : _t.FgDim, fs - 2));
                row.Children.Add(left);
                var right = T(q.Problem ? "!" : q.State == "completed" || q.State == "importing" ? "importing" : Math.Round(q.Progress) + "%" + (q.TimeLeft.Length > 0 ? " · " + q.TimeLeft : ""), _t.FgDim, fs - 1.5);
                right.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(right, 1);
                row.Children.Add(right);
                row.ToolTip = q.Release;
                Link(row, Plugin.HomeUrl(q.Link));
                root.Children.Add(row);
            }

            if (B.Get("ShowComing", true) && s.ComingUp.Count > 0)
            {
                root.Children.Add(Heading("COMING UP", fs, 10));
                foreach (var c in s.ComingUp.Take(count))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(T(c.Title + "  " + c.Episode, _t.Fg, fs));
                    var when = T(When(c.AtUtc), _t.FgDim, fs - 1.5);
                    when.Margin = new Thickness(10, 0, 0, 0);
                    Grid.SetColumn(when, 1);
                    row.Children.Add(when);
                    if (c.Name.Length > 0) row.ToolTip = c.Name;
                    Link(row, Plugin.HomeUrl(c.Link));
                    root.Children.Add(row);
                }
            }
            return root;
        }

        TextBlock Heading(string text, double fs, double top)
        {
            var h = T(text, _t.FgDim, fs - 2, true);
            h.Margin = new Thickness(0, top, 0, 2);
            return h;
        }

        static string When(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            var local = utc.ToLocalTime();
            string day = local.Date == DateTime.Today ? "today" : local.Date == DateTime.Today.AddDays(1) ? "tomorrow" : local.ToString("ddd");
            return day + " " + TimeText.Clock(local);
        }

        FrameworkElement Tile(string n, string label, Brush fg, double fs)
        {
            var p = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            var big = T(n, fg, fs + 6, true);
            big.HorizontalAlignment = HorizontalAlignment.Right;
            var small = T(label, _t.FgDim, fs - 2.5);
            small.HorizontalAlignment = HorizontalAlignment.Right;
            p.Children.Add(big);
            p.Children.Add(small);
            return p;
        }

        /// <summary>A click opens vMediarr there (in a widget: the press handled too, or it starts a widget drag).</summary>
        static void Link(FrameworkElement el, string url)
        {
            el.Cursor = Cursors.Hand;
            el.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            el.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Plugin.Open(url); };
        }

        void TogglePopup()
        {
            if (_pop != null && _pop.IsOpen) { _pop.CloseSafe(); _pop = null; return; }
            Plugin.RefreshNow();
            _pop = new PopupWindow(_mgr, _host, this, _t, new Border());
            if (_mgr.Settings.WidgetStyle != null) _pop.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _popShown = "";
            _pop.SetContent(List(Plugin.Latest, true));
            _mgr.ShowPopup(_pop);
        }

        // ---------- the module's settings ----------
        public static void BuildSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            SettingsBag b = cfg.Bag;
            if (!VmSettings.Of(p.Settings).Configured) p.Note("Set up the vMediarr connection first (Plugins page in this window).");
            if (!p.IsWidget)
            {
                p.Header("On the bar");
                p.Title(cfg, "");
                p.Note("↓ what's downloading, then the requests waiting for you (amber) and ⚠ downloads that failed. The dot: the accent while something downloads, amber with requests waiting, red when something failed or vMediarr can't be reached, green otherwise. Click it for the downloads, the requests and the week's episodes - click one to open it in vMediarr.");
            }
            else p.Header("In the widget");
            p.Check("The week's episodes (coming up)", delegate { return b.Get("ShowComing", true); }, delegate (bool v) { b.Set("ShowComing", v); });
            p.Slider("How many of each", 1, 30, delegate { return b.Get("Count", 8.0); }, delegate (double v) { b.Set("Count", Math.Round(v)); });
        }
    }

    /// <summary>The gallery pictures.</summary>
    public static class Pictures
    {
        public static FrameworkElement Bar(Theme t)
        {
            return ModulePics.Row(6, new UIElement[] { ModulePics.Dot(t.MainAccent, 8), ModulePics.T(t, "↓ 2", t.Fg, 12.5), ModulePics.T(t, "· 1 asked", StatusColors.Warn, 12.5) });
        }

        public static FrameworkElement Widget(Theme t)
        {
            return ModulePics.Col(4, new UIElement[]
            {
                ModulePics.T(t, "DOWNLOADING", t.FgDim, 10, true),
                ModulePics.Row(6, new UIElement[] { ModulePics.T(t, "Glass Coast (2026)", t.Fg, 12), ModulePics.T(t, "64%", t.FgDim, 11) }),
                ModulePics.T(t, "COMING UP", t.FgDim, 10, true),
                ModulePics.Row(6, new UIElement[] { ModulePics.T(t, "Harbour Lights  S03E07", t.Fg, 12), ModulePics.T(t, "today", t.FgDim, 11) })
            });
        }
    }
}
