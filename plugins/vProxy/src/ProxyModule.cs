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

namespace VProxy
{
    // =====================================================================================
    // The module: on a bar a dot, "vProxy · 41", ⚠ not answering; click -> every host; in a widget the hosts
    // =====================================================================================
    public sealed class ProxyModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _pop;
        string _shown = "", _popShown = "";
        bool _checking;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        SettingsBag B { get { return _cfg.Bag; } }
        bool OnlyNeeding { get { return B.Get("OnlyNeeding", false); } }
        int Count { get { return Math.Max(1, (int)B.Get("Count", 12.0)); } }
        bool ShowCount { get { return B.Get("ShowCount", true); } }

        public ProxyModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            VxSnapshot s = Plugin.Latest;
            string sig = Sig(s) + "|" + _checking;
            if (_pop != null && _pop.IsOpen && sig != _popShown) { _popShown = sig; _pop.SetContent(List(s, true)); }
            if (sig == _shown) return;
            _shown = sig;
            Child = _host.IsWidget ? List(s, false) : BarView(s);
            _host.ContentChanged();
        }

        string Sig(VxSnapshot s)
        {
            var b = new StringBuilder(OnlyNeeding + "|" + Count + "|" + ShowCount + "|" + _cfg.Title + "|" + s.Ok + s.Checked + s.Error + "|" + s.CaddyProblem + "|" + B.Get("ShowUpstream", true));
            foreach (VxCert c in s.Certs) b.Append('|').Append(c.Domain).Append(c.Has).Append(c.Staging).Append(c.DaysLeft);
            foreach (VxHost h in s.Hosts) b.Append('|').Append(h.Id).Append(h.On).Append(h.Checked).Append(h.Answering).Append(h.Watch).Append(h.Lost).Append(h.Upstream);
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

        /// <summary>A host's dot: grey switched off, not checked yet or away (not watched), red not answering, amber lost in vDeploy, green answering.</summary>
        Brush DotOf(VxHost h)
        {
            if (!h.On || !h.Checked || h.Away) return _t.FgDim;
            if (!h.Answering) return StatusColors.Bad;
            return h.Lost.Length > 0 ? StatusColors.Warn : (Brush)StatusColors.Good;
        }

        Brush Overall(VxSnapshot s)
        {
            if (!s.Checked) return _t.FgDim;
            if (!s.Ok || s.Down > 0 || s.CaddyProblem.Length > 0) return StatusColors.Bad;
            return s.Warn ? StatusColors.Warn : (Brush)StatusColors.Good;
        }

        // ---------- on a bar ----------
        FrameworkElement BarView(VxSnapshot s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Dot(Overall(s)));
            string title = (_cfg.Title ?? "").Trim();
            row.Children.Add(T(title.Length > 0 ? title : "vProxy", _t.Fg, _t.FontSize));
            if (!s.Checked) row.Children.Add(T(" …", _t.FgDim, _t.FontSize));
            else if (!s.Ok) row.Children.Add(T(": can't reach it", StatusColors.Bad, _t.FontSize));
            else
            {
                if (ShowCount) row.Children.Add(T(" · " + s.Hosts.Count, _t.FgDim, _t.FontSize));
                if (s.Down > 0) row.Children.Add(T("  ⚠ " + s.Down, StatusColors.Bad, _t.FontSize));
                if (s.CaddyProblem.Length > 0) row.Children.Add(T("  Caddy", StatusColors.Bad, _t.FontSize));
            }
            ToolTip = !s.Ok ? "vProxy: " + (s.Checked ? s.Error : "looking…") : "vProxy: " + Plugin.Summary(s) + "\nClick for the hosts";
            return row;
        }

        // ---------- the list: a widget, and the bar's popup ----------
        FrameworkElement List(VxSnapshot s, bool popup)
        {
            double fs = _host.IsWidget && _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            var root = new StackPanel { Width = popup ? 360 : double.NaN, Margin = popup ? new Thickness(14, 12, 14, 12) : new Thickness(0) };
            if (popup || !_host.IsWidget || B.Get("Heading", true))
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                if (s.Ok)
                {
                    var tiles = new StackPanel { Orientation = Orientation.Horizontal };
                    tiles.Children.Add(Tile(s.Hosts.Count(h => h.On && h.Checked && h.Answering).ToString(), "answering", StatusColors.Good, fs));
                    tiles.Children.Add(Tile(s.Down.ToString(), "not", s.Down > 0 ? StatusColors.Bad : _t.FgDim, fs));
                    VxCert soon = s.Soonest;
                    tiles.Children.Add(Tile(soon == null ? "-" : soon.DaysLeft.ToString(), soon != null && soon.Staging ? "days (test)" : "cert days", soon == null || soon.Staging || soon.DaysLeft < 14 ? StatusColors.Warn : _t.FgDim, fs));
                    DockPanel.SetDock(tiles, Dock.Right);
                    head.Children.Add(tiles);
                }
                var name = T(_host.IsWidget && (_cfg.Title ?? "").Trim().Length > 0 ? _cfg.Title.Trim() : "vProxy", _t.Fg, fs + 1, true);
                name.VerticalAlignment = VerticalAlignment.Top;
                head.Children.Add(name);
                root.Children.Add(head);
            }
            if (!s.Checked) root.Children.Add(T(VxSettings.Of(_mgr.Settings).Configured ? "Asking vProxy…" : "Set up vProxy in Settings > Plugins > vProxy", _t.FgDim, fs));
            else if (!s.Ok) { var e = T(s.Error, StatusColors.Bad, fs - 1); e.TextWrapping = TextWrapping.Wrap; root.Children.Add(e); }
            else
            {
                if (s.CaddyProblem.Length > 0)
                {
                    var c = T((s.CaddyAnswering ? "Caddy didn't take the last change: " : "Caddy isn't answering: ") + s.CaddyProblem, StatusColors.Bad, fs - 1.5);
                    c.TextWrapping = TextWrapping.Wrap;
                    c.Margin = new Thickness(0, 0, 0, 6);
                    root.Children.Add(c);
                }
                // the ones that need you first, then by name
                var hosts = s.Hosts.Where(h => !OnlyNeeding || h.Down || h.Lost.Length > 0)
                    .OrderBy(h => h.Down ? 0 : h.Lost.Length > 0 ? 1 : h.On ? 2 : 3).ThenBy(h => h.Name).ToList();
                int shown = 0;
                foreach (VxHost h in hosts)
                {
                    if (shown >= Count && !popup) break;
                    shown++;
                    root.Children.Add(HostRow(h, fs, popup));
                }
                if (hosts.Count == 0) root.Children.Add(T(OnlyNeeding ? "Every app answers - " + Plugin.Summary(s) : "No hosts in vProxy yet", _t.FgDim, fs - 1));
                if (hosts.Count > shown && !popup) root.Children.Add(T("and " + (hosts.Count - shown) + " more", _t.FgDim, fs - 1.5));
            }
            if (popup)
            {
                var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                var list = new List<Border>
                {
                    Pills.Make(_t, "", _checking ? "Checking…" : "Check now", "Ask every host's app now", false, delegate { CheckNow(); }, false),
                    Pills.Make(_t, Plugin.Glyph, "Open vProxy", "Open vProxy in your browser", false, delegate { Close(); Plugin.Open(Plugin.HomeUrl()); }, false),
                };
                foreach (Border b in list) { b.Margin = new Thickness(0, 0, 6, 6); buttons.Children.Add(b); }
                root.Children.Add(buttons);
                var foot = T((s.Ok ? "Looked " + TimeText.Ago(s.AtUtc) + " · " : "") + "click a host to open it · right-click for more", _t.FgDim, fs - 2);
                foot.Margin = new Thickness(0, 2, 0, 0);
                root.Children.Add(foot);
                var scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                if (_pop != null) scroll.MaxHeight = _pop.ContentRoom();
                WidgetWindow.StyleScrollbars(scroll);
                return scroll;
            }
            return root;
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

        /// <summary>A host: its dot and name, where it goes (dim), and on the right its state.</summary>
        FrameworkElement HostRow(VxHost h, double fs, bool popup)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            TextBlock state;
            if (!h.On) state = T("off", _t.FgDim, fs - 1.5);
            else if (!h.Checked) state = T("checking…", _t.FgDim, fs - 1.5);
            else if (h.Away) state = T("away", _t.FgDim, fs - 1.5);
            else if (!h.Answering) state = T("not answering", StatusColors.Bad, fs - 1.5);
            else if (h.Lost.Length > 0) state = T("vDeploy moved it", StatusColors.Warn, fs - 1.5);
            else state = T(h.Ms + " ms", _t.FgDim, fs - 1.5);
            state.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(state, Dock.Right);
            line.Children.Add(state);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(Dot(DotOf(h), 7));
            name.Children.Add(T(h.Name, h.On ? _t.Fg : _t.FgDim, fs));
            if (popup || _host.IsWidget && B.Get("ShowUpstream", true)) { var up = T("  " + h.Upstream.Replace("http://", ""), _t.FgDim, fs - 2.5); up.Margin = new Thickness(0, 1, 0, 0); name.Children.Add(up); }
            line.Children.Add(name);
            line.ToolTip = h.Name + " → " + h.Upstream + "\n" + h.StateText + (h.Lost.Length > 0 ? "\n" + h.Lost : "") + (h.From.Length > 0 ? "\nFrom vDeploy: " + h.From : "") +
                           "\nClick: open it · right-click: its page in vProxy, check now";
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Close(); if (h.On) Plugin.Open(Plugin.SiteUrl(h)); else Plugin.Open(Plugin.HostPage(h)); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(line, HostMenu(h)); };
            return line;
        }

        ContextMenu HostMenu(VxHost h)
        {
            var m = new ContextMenu();
            m.Items.Add(Item("Open " + h.Name, delegate { Close(); Plugin.Open(Plugin.SiteUrl(h)); }, h.On));
            m.Items.Add(Item("Its page in vProxy", delegate { Close(); Plugin.Open(Plugin.HostPage(h)); }));
            m.Items.Add(Item("Copy its address", delegate { try { Clipboard.SetText(Plugin.SiteUrl(h)); } catch { } }, h.On));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Check every app now", delegate { CheckNow(); }, !_checking));
            return m;
        }

        static MenuItem Item(string text, Action run, bool enabled = true, string tip = null)
        {
            var i = new MenuItem { Header = text, IsEnabled = enabled, ToolTip = tip };
            i.Click += delegate { run(); };
            return i;
        }

        /// <summary>A menu at the pointer; the popup stays open while it's up.</summary>
        void ShowMenu(FrameworkElement target, ContextMenu m)
        {
            PopupWindow host = Window.GetWindow(target) as PopupWindow;
            if (host != null) host.Pinned = true;
            m.Closed += delegate { if (host != null) host.Pinned = false; };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        async void CheckNow()
        {
            _checking = true;
            _shown = _popShown = "";
            Refresh();
            string err = await Plugin.CheckAsync();
            _checking = false;
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, "vProxy couldn't check: " + err, Plugin.Source);
        }

        void Close() { if (_pop != null) { _pop.CloseSafe(); _pop = null; } }

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
            if (!VxSettings.Of(p.Settings).Configured) p.Note("Set up the vProxy connection first (Plugins page in this window).");
            if (!p.IsWidget)
            {
                p.Header("On the bar");
                p.Check("How many hosts", delegate { return b.Get("ShowCount", true); }, delegate (bool v) { b.Set("ShowCount", v); });
                p.Title(cfg, "");
                p.Note("The dot is green when every app answers, red when one doesn't (⚠ how many) or Caddy has a problem, amber for a certificate ending within 14 days, a test certificate, or a host whose vDeploy service moved. Click it for every host: click one to open it, right-click for its page in vProxy.");
            }
            else
            {
                p.Header("In the widget");
                p.Check("The heading (answering, not, certificate days)", delegate { return b.Get("Heading", true); }, delegate (bool v) { b.Set("Heading", v); });
                p.Check("Only hosts that need you (not answering, moved in vDeploy)", delegate { return b.Get("OnlyNeeding", false); }, delegate (bool v) { b.Set("OnlyNeeding", v); });
                p.Check("Where each one goes", delegate { return b.Get("ShowUpstream", true); }, delegate (bool v) { b.Set("ShowUpstream", v); });
                p.Slider("How many", 1, 80, delegate { return b.Get("Count", 12.0); }, delegate (double v) { b.Set("Count", Math.Round(v)); });
                p.Note("The ones that need you come first. Click a host to open it; right-click for its page in vProxy, or to check every app now.");
            }
        }
    }

    /// <summary>The gallery pictures.</summary>
    public static class Pictures
    {
        public static FrameworkElement Bar(Theme t)
        {
            return ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Bad, 8), ModulePics.T(t, "vProxy", t.Fg, 12.5), ModulePics.T(t, "· 41", t.FgDim, 12.5), ModulePics.T(t, "⚠ 1", StatusColors.Bad, 12.5) });
        }

        public static FrameworkElement Widget(Theme t)
        {
            return ModulePics.Col(4, new UIElement[]
            {
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Bad, 7), ModulePics.T(t, "tdarr.vrsion.xyz", t.Fg, 12), ModulePics.T(t, "not answering", StatusColors.Bad, 11) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "jellyfin.vrsion.xyz", t.Fg, 12), ModulePics.T(t, "8 ms", t.FgDim, 11) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "pve.vrsion.xyz", t.Fg, 12), ModulePics.T(t, "20 ms", t.FgDim, 11) })
            });
        }
    }
}
