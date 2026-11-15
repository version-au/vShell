using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace UptimeKuma
{
    /// <summary>A module's own settings (kept in its ModuleConfig's bag).</summary>
    public sealed class KumaView
    {
        readonly SettingsBag _b;
        KumaView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static KumaView Of(ModuleConfig cfg) { return new KumaView(cfg); }

        /// <summary>"All" or "Not up" (down, pending, maintenance).</summary>
        public string Show { get { string v = _b.Get("Show", "All"); return v == "Not up" ? v : "All"; } set { _b.Set("Show", value); } }
        public bool ResponseTimes { get { return _b.Get("ResponseTimes", true); } set { _b.Set("ResponseTimes", value); } }
        public bool CertDays { get { return _b.Get("CertDays", true); } set { _b.Set("CertDays", value); } }
        /// <summary>A certificate with fewer days than this left shows in the warning colour.</summary>
        public int CertWarn { get { return Math.Max(1, _b.Get("CertWarn", 14)); } set { _b.Set("CertWarn", value); } }
    }

    /// <summary>Drawing the monitors: the list (a widget, the bar's popup), a summary, colours.</summary>
    static class KumaDraw
    {
        public static Brush StateBrush(Theme t, MonitorState s)
        {
            switch (s)
            {
                case MonitorState.Up: return StatusColors.Good;
                case MonitorState.Down: return StatusColors.Bad;
                case MonitorState.Pending: return StatusColors.Warn;
                case MonitorState.Maintenance: return t.SecondaryAccent;
            }
            return t.FgDim;
        }

        public static string StateText(MonitorState s)
        {
            switch (s)
            {
                case MonitorState.Up: return "up";
                case MonitorState.Down: return "down";
                case MonitorState.Pending: return "pending";
                case MonitorState.Maintenance: return "maintenance";
            }
            return "unknown";
        }

        /// <summary>"12 up · 1 down · 1 pending".</summary>
        public static string Summary(KumaSnapshot s)
        {
            var parts = new List<string>();
            foreach (MonitorState st in new[] { MonitorState.Down, MonitorState.Pending, MonitorState.Maintenance, MonitorState.Up })
            {
                int n = s.Count(st);
                if (n > 0) parts.Add(n + " " + StateText(st));
            }
            return parts.Count == 0 ? "No monitors" : string.Join(" · ", parts);
        }

        /// <summary>What's shown, as text: rebuild only when this changes.</summary>
        public static string Signature(KumaSnapshot s, KumaView v)
        {
            if (s == null) return "none";
            if (s.Error != null) return "error:" + s.Error;
            var b = new StringBuilder(v.Show + v.ResponseTimes + v.CertDays + v.CertWarn);
            foreach (KumaMonitor m in s.Monitors)
                b.Append('|').Append(m.Name).Append(':').Append((int)m.State).Append(':')
                 .Append(v.ResponseTimes && !double.IsNaN(m.ResponseMs) ? Math.Round(m.ResponseMs).ToString() : "")
                 .Append(':').Append(v.CertDays && !double.IsNaN(m.CertDays) ? Math.Floor(m.CertDays).ToString() : "");
            return b.ToString();
        }

        public static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Uptime Kuma: open " + url + ": " + ex.Message); }
        }

        static TextBlock Txt(Theme t, string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = fg, FontFamily = t.Font, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        /// <summary>The list: a summary line, then a row per monitor (dot, name, response time, certificate days). Click = open Uptime Kuma.</summary>
        public static FrameworkElement List(Theme t, KumaSnapshot s, KumaView v, double fs)
        {
            var root = new StackPanel();
            if (s == null) { root.Children.Add(Txt(t, "Reading Uptime Kuma…", t.FgDim, fs)); return root; }
            if (s.Error != null)
            {
                var e = Txt(t, s.Error, StatusColors.Bad, fs);
                e.TextWrapping = TextWrapping.Wrap;
                e.TextTrimming = TextTrimming.None;
                root.Children.Add(e);
                return root;
            }
            var head = Txt(t, Summary(s), s.Count(MonitorState.Down) > 0 ? StatusColors.Bad : t.FgDim, fs * 0.9, true);
            head.Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(head);
            int shown = 0;
            foreach (KumaMonitor m in s.Monitors)
            {
                if (v.Show == "Not up" && m.State == MonitorState.Up) continue;
                shown++;
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var dot = ModulePics.Dot(StateBrush(t, m.State), Math.Max(6, fs * 0.6));
                dot.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(dot);
                var name = Txt(t, m.Name, m.State == MonitorState.Down ? StatusColors.Bad : t.Fg, fs);
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                if (v.ResponseTimes)
                {
                    string ms = m.State == MonitorState.Up && !double.IsNaN(m.ResponseMs) ? Math.Round(m.ResponseMs) + " ms" : m.State != MonitorState.Up ? StateText(m.State) : "";
                    var rt = Txt(t, ms, t.FgDim, fs * 0.9);
                    rt.Margin = new Thickness(10, 0, 0, 0);
                    Grid.SetColumn(rt, 2);
                    row.Children.Add(rt);
                }
                if (v.CertDays && !double.IsNaN(m.CertDays))
                {
                    int days = (int)Math.Floor(m.CertDays);
                    var cd = Txt(t, days < 0 ? "cert expired" : days + " d", days < v.CertWarn ? StatusColors.Warn : t.FgDim, fs * 0.9);
                    cd.Margin = new Thickness(10, 0, 0, 0);
                    cd.ToolTip = days < 0 ? "Its certificate has expired" : "Its certificate expires in " + days + " day" + (days == 1 ? "" : "s");
                    Grid.SetColumn(cd, 3);
                    row.Children.Add(cd);
                }
                row.ToolTip = m.Name + " - " + StateText(m.State) + (m.Url.Length > 0 ? "\n" + m.Url : "") + "\nClick to open Uptime Kuma";
                // (a widget: handle the press too, or it starts a widget drag)
                row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Open(Kuma.DashboardUrl); };
                root.Children.Add(row);
            }
            if (shown == 0) root.Children.Add(Txt(t, v.Show == "Not up" ? "Everything is up" : "No monitors", t.FgDim, fs));
            return root;
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

    /// <summary>On a bar: "All up" or "2 down" (click: the list in a popup).</summary>
    public sealed class KumaBarModule : Border
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly TextBlock _glyph, _text;
        PopupWindow _popup;
        string _shown, _popupShown;

        public KumaBarModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            _glyph = new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _text = new TextBlock { FontFamily = _t.Font, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_glyph);
            sp.Children.Add(_text);
            Child = sp;
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            ModuleTimer.Make(this, TimeSpan.FromSeconds(2), Refresh);
            Refresh();
        }

        void Refresh()
        {
            KumaSnapshot s = Kuma.Latest;
            string text, tip;
            Brush fg = _t.Fg;
            if (s == null) { text = "…"; tip = "Uptime Kuma - reading"; }
            else if (s.Error != null) { text = "!"; fg = StatusColors.Warn; tip = s.Error; }
            else
            {
                int down = s.Count(MonitorState.Down), pending = s.Count(MonitorState.Pending);
                text = down > 0 ? down + " down" : pending > 0 ? pending + " pending" : "All up";
                fg = down > 0 ? StatusColors.Bad : pending > 0 ? StatusColors.Warn : _t.Fg;
                tip = "Uptime Kuma: " + KumaDraw.Summary(s) + "\nClick for the list";
            }
            // "Hide when everything's up"
            bool hide = _cfg.HideIfNone && s != null && s.Error == null && s.Count(MonitorState.Down) == 0 && s.Count(MonitorState.Pending) == 0;
            string sig = text + "|" + hide + "|" + tip;
            if (_popup != null && _popup.IsOpen)
            {
                string ps = KumaDraw.Signature(s, KumaView.Of(_cfg));
                if (ps != _popupShown) { _popupShown = ps; _popup.SetContent(PopupContent(s)); }
            }
            if (sig == _shown) return;
            _shown = sig;
            _text.Text = text;
            _text.Foreground = fg;
            _glyph.Foreground = fg;
            ToolTip = tip;
            Visibility v = hide ? Visibility.Collapsed : Visibility.Visible;
            if (Visibility != v) { Visibility = v; _host.ContentChanged(); }
        }

        FrameworkElement PopupContent(KumaSnapshot s)
        {
            var box = new Border { Padding = new Thickness(12, 10, 12, 10), MinWidth = 260, MaxWidth = 460 };
            box.Child = KumaDraw.List(_t, s, KumaView.Of(_cfg), Math.Max(12, _t.FontSize));
            return box;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            KumaSnapshot s = Kuma.Latest;
            _popupShown = KumaDraw.Signature(s, KumaView.Of(_cfg));
            _popup = new PopupWindow(_mgr, _host, this, _t, PopupContent(s));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>In a widget: the list of monitors (it sizes itself to them).</summary>
    public sealed class KumaListModule : Border, IMultiRow
    {
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        string _shown;

        public int RowCount { get { return 0; } }       // sizes itself

        public KumaListModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(2);
            ModuleTimer.Make(this, TimeSpan.FromSeconds(2), Refresh);
            Refresh();
        }

        void Refresh()
        {
            KumaSnapshot s = Kuma.Latest;
            KumaView v = KumaView.Of(_cfg);
            string sig = KumaDraw.Signature(s, v);
            if (sig == _shown) return;
            _shown = sig;
            double fs = _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            Child = KumaDraw.List(_t, s, v, fs);
            _host.ContentChanged();
        }
    }
}
