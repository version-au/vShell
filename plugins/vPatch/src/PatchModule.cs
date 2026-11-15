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

namespace VPatch
{
    // =====================================================================================
    // The module: on a bar a dot, ⬆ updates · security, a restart sign; click -> the hosts by group; in a widget the hosts
    // =====================================================================================
    public sealed class PatchModule : Border, IMultiRow
    {
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        const string RestartGlyph = "";

        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _pop;
        string _shown = "", _popShown = "";
        readonly HashSet<string> _busy = new HashSet<string>();

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        SettingsBag B { get { return _cfg.Bag; } }
        bool OnlyNeeding { get { return B.Get("OnlyNeeding", false); } }
        bool GroupHosts { get { return B.Get("GroupHosts", true); } }
        int Count { get { return Math.Max(1, (int)B.Get("Count", 12.0)); } }
        bool ShowTotal { get { return B.Get("ShowTotal", true); } }

        public PatchModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            VpSnapshot s = Plugin.Latest;
            string sig = Sig(s) + "|" + string.Join(",", _busy);
            if (_pop != null && _pop.IsOpen && sig != _popShown) { _popShown = sig; _pop.SetContent(List(s, true)); }
            if (sig == _shown) return;
            _shown = sig;
            Child = _host.IsWidget ? List(s, false) : BarView(s);
            _host.ContentChanged();
        }

        string Sig(VpSnapshot s)
        {
            var b = new StringBuilder(OnlyNeeding + "|" + GroupHosts + "|" + Count + "|" + ShowTotal + "|" + _cfg.Title + "|" + s.Ok + s.Checked + s.Error + s.Updates + "|" + s.Security);
            foreach (VpHost h in s.Hosts)
                b.Append('|').Append(h.Id).Append(h.Group).Append(h.Updates).Append(h.Security).Append(h.Restart).Append(h.Problem).Append(h.Checking).Append(h.Upgrading).Append(h.Checked);
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

        TextBlock Glyph(string g, Brush fg, double size)
        {
            return new TextBlock { Text = g, FontFamily = Icons, FontSize = size, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        }

        Ellipse Dot(Brush b, double size = 8) { return new Ellipse { Width = size, Height = size, Fill = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; }

        /// <summary>A host's dot: grey not checked yet, accent checking / upgrading, red not answering, amber updates or a restart, green up to date.</summary>
        Brush DotOf(VpHost h)
        {
            if (h.Checking || h.Upgrading) return _t.MainAccent;
            if (!h.Checked) return _t.FgDim;
            if (h.Down) return StatusColors.Bad;
            return h.Updates > 0 || h.Restart ? StatusColors.Warn : (Brush)StatusColors.Good;
        }

        // ---------- on a bar ----------
        FrameworkElement BarView(VpSnapshot s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Brush dot = !s.Checked ? _t.FgDim : !s.Ok || s.Down > 0 ? StatusColors.Bad : s.Hosts.Any(h => h.Upgrading || h.Checking) ? _t.MainAccent : s.Updates > 0 || s.Restarts > 0 ? StatusColors.Warn : StatusColors.Good;
            row.Children.Add(Dot(dot));
            string title = (_cfg.Title ?? "").Trim();
            if (title.Length > 0) row.Children.Add(T(title + " ", _t.FgDim, _t.FontSize));
            if (!s.Checked) row.Children.Add(T("vPatch …", _t.Fg, _t.FontSize));
            else if (!s.Ok) row.Children.Add(T("vPatch: can't reach it", StatusColors.Bad, _t.FontSize));
            else
            {
                if (s.Updates > 0 || ShowTotal) row.Children.Add(T("⬆ " + s.Updates, _t.Fg, _t.FontSize));
                if (s.Security > 0) row.Children.Add(T(" · " + s.Security, _t.SecondaryAccent, _t.FontSize));
                if (s.Restarts > 0) { var g = Glyph(RestartGlyph, StatusColors.Warn, _t.FontSize - 1); g.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(g); }
                if (s.Down > 0) row.Children.Add(T("  ⚠ " + s.Down, StatusColors.Bad, _t.FontSize));
                if (s.Updates == 0 && !ShowTotal && s.Restarts == 0 && s.Down == 0) row.Children.Add(T("vPatch", _t.Fg, _t.FontSize));
            }
            ToolTip = !s.Ok ? "vPatch: " + (s.Checked ? s.Error : "looking…") : "vPatch: " + Plugin.Summary(s) + "\nClick for the hosts";
            return row;
        }

        // ---------- the list: a widget, and the bar's popup ----------
        FrameworkElement List(VpSnapshot s, bool popup)
        {
            double fs = _host.IsWidget && _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            var root = new StackPanel { Width = popup ? 340 : double.NaN, Margin = popup ? new Thickness(14, 12, 14, 12) : new Thickness(0) };
            if (popup || !_host.IsWidget || B.Get("Heading", true))
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                if (s.Ok)
                {
                    var tiles = new StackPanel { Orientation = Orientation.Horizontal };
                    tiles.Children.Add(Tile(s.Updates.ToString(), "updates", _t.Fg, fs));
                    tiles.Children.Add(Tile(s.Security.ToString(), "security", s.Security > 0 ? _t.SecondaryAccent : _t.FgDim, fs));
                    tiles.Children.Add(Tile(s.Restarts.ToString(), "restart", s.Restarts > 0 ? StatusColors.Warn : _t.FgDim, fs));
                    DockPanel.SetDock(tiles, Dock.Right);
                    head.Children.Add(tiles);
                }
                var name = T(_host.IsWidget && (_cfg.Title ?? "").Trim().Length > 0 ? _cfg.Title.Trim() : "Linux updates", _t.Fg, fs + 1, true);
                name.VerticalAlignment = VerticalAlignment.Top;
                head.Children.Add(name);
                root.Children.Add(head);
            }
            if (!s.Checked) root.Children.Add(T(VpSettings.Of(_mgr.Settings).Configured ? "Asking vPatch…" : "Set up vPatch in Settings > Plugins > vPatch", _t.FgDim, fs));
            else if (!s.Ok) { var e = T(s.Error, StatusColors.Bad, fs - 1); e.TextWrapping = TextWrapping.Wrap; root.Children.Add(e); }
            else
            {
                int shown = 0, left = 0;
                foreach (var g in s.Groups)
                {
                    var hosts = g.Value.Where(h => !OnlyNeeding || h.Updates > 0 || h.Restart || h.Down || h.Upgrading).ToList();
                    if (hosts.Count == 0) continue;
                    if (GroupHosts || popup)
                    {
                        var gh = T(g.Key.ToUpperInvariant() + (g.Value.Any(h => h.Proxmox) ? " · FIRST" : ""), _t.FgDim, fs - 2, true);
                        gh.Margin = new Thickness(0, shown == 0 ? 0 : 8, 0, 2);
                        root.Children.Add(gh);
                    }
                    foreach (VpHost h in hosts)
                    {
                        if (shown >= Count && !popup) { left++; continue; }
                        shown++;
                        root.Children.Add(HostRow(h, fs, popup));
                    }
                }
                if (shown == 0) root.Children.Add(T(OnlyNeeding ? "Nothing needs you - " + Plugin.Summary(s) : "No hosts in vPatch yet", _t.FgDim, fs - 1));
                if (left > 0) root.Children.Add(T("and " + left + " more", _t.FgDim, fs - 1.5));
            }
            if (popup)
            {
                var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                var list = new List<Border>();
                if (s.Ok && s.Waiting > 0)
                    list.Add(Pills.Make(_t, Plugin.Glyph, "Upgrade… (" + s.Waiting + ")", "vPatch's Upgrade page: the dry run of each, then group by group", true, delegate { Close(); Plugin.Open(Plugin.HomeUrl("/upgrade")); }, false));
                list.Add(Pills.Make(_t, "", _busy.Contains("*") ? "Checking…" : "Check now", "Ask every host what's waiting", false, delegate { CheckNow(null); }, false));
                list.Add(Pills.Make(_t, "", "Open vPatch", "Open vPatch in your browser", false, delegate { Close(); Plugin.Open(Plugin.HomeUrl()); }, false));
                foreach (Border b in list) { b.Margin = new Thickness(0, 0, 6, 6); buttons.Children.Add(b); }
                root.Children.Add(buttons);
                var foot = T((s.Ok ? "Looked " + TimeText.Ago(s.AtUtc) + " · " : "") + "right-click a host for more", _t.FgDim, fs - 2);
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

        /// <summary>A host: its dot, name, system (dim), and on the right its restart sign, security count and updates (or its state).</summary>
        FrameworkElement HostRow(VpHost h, double fs, bool popup)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            bool busy = _busy.Contains(h.Id) || h.Checking || h.Upgrading;
            if (busy) right.Children.Add(T(h.Upgrading ? "upgrading…" : "checking…", _t.MainAccent, fs - 1.5));
            else if (!h.Checked) right.Children.Add(T("not checked", _t.FgDim, fs - 1.5));
            else if (h.Down) right.Children.Add(T("not answering", StatusColors.Bad, fs - 1.5));
            else
            {
                if (h.Restart) { var g = Glyph(RestartGlyph, StatusColors.Warn, fs - 2); g.Margin = new Thickness(0, 0, 6, 0); g.ToolTip = "A restart is needed"; right.Children.Add(g); }
                if (h.Security > 0) { var sec = T(h.Security.ToString(), _t.SecondaryAccent, fs - 1); sec.Margin = new Thickness(0, 0, 6, 0); sec.ToolTip = h.Security + " security"; right.Children.Add(sec); }
                right.Children.Add(h.Updates > 0 ? T(h.Updates + " ⬆", _t.Fg, fs - 0.5) : T("✓", StatusColors.Good, fs - 0.5));
            }
            DockPanel.SetDock(right, Dock.Right);
            line.Children.Add(right);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(Dot(DotOf(h), 7));
            name.Children.Add(T(h.Name, _t.Fg, fs));
            if (popup || _host.IsWidget && B.Get("ShowOs", true)) { var os = T("  " + h.Os, _t.FgDim, fs - 2); os.Margin = new Thickness(0, 1, 0, 0); name.Children.Add(os); }
            line.Children.Add(name);
            line.ToolTip = h.Name + " (" + h.Address + ") - " + h.StateText + (h.Problem.Length > 0 ? "\n" + h.Problem : "") +
                           (h.Checked ? "\nChecked " + TimeText.Ago(h.CheckedUtc) : "") + "\nClick: open it in vPatch · right-click: check it now, a shell";
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Close(); Plugin.Open(Plugin.HostUrl(h)); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(line, HostMenu(h)); };
            return line;
        }

        ContextMenu HostMenu(VpHost h)
        {
            var m = new ContextMenu();
            m.Items.Add(Item("Check " + h.Name + " now", delegate { CheckNow(h.Id); }, !h.Checking && !h.Upgrading));
            m.Items.Add(Item("Open in vPatch", delegate { Close(); Plugin.Open(Plugin.HostUrl(h)); }));
            if (h.Updates > 0) m.Items.Add(Item("Upgrade… (in vPatch)", delegate { Close(); Plugin.Open(Plugin.HostUrl(h)); }, true, "Its page in vPatch: Upgrade… shows the dry run first"));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Open shell on " + h.Name, delegate { Plugin.ShellOn(_mgr, h); }, h.Address.Length > 0, "SSH in vTerm"));
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

        async void CheckNow(string hostId)
        {
            string key = hostId ?? "*";
            _busy.Add(key);
            _shown = _popShown = "";
            Refresh();
            string err = await Plugin.CheckAsync(hostId);
            _busy.Remove(key);
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, "vPatch couldn't check: " + err, Plugin.Source);
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
            if (!VpSettings.Of(p.Settings).Configured) p.Note("Set up the vPatch connection first (Plugins page in this window).");
            if (!p.IsWidget)
            {
                p.Header("On the bar");
                p.Check("⬆ 0 when everything is up to date", delegate { return b.Get("ShowTotal", true); }, delegate (bool v) { b.Set("ShowTotal", v); });
                p.Title(cfg, "");
                p.Note("⬆ updates waiting on all your hosts, then · security updates in the Secondary accent, the restart sign when a host needs one, ⚠ hosts not answering. The dot is green when all are up to date, amber with updates, red when a host isn't answering, the accent colour while checking or upgrading. Click it for the hosts by group: click one to open it in vPatch, right-click to check it now or open a shell; Upgrade… opens vPatch's Upgrade page.");
            }
            else
            {
                p.Header("In the widget");
                p.Check("The heading (updates, security, restarts)", delegate { return b.Get("Heading", true); }, delegate (bool v) { b.Set("Heading", v); });
                p.Check("Only hosts that need you (updates, a restart, not answering)", delegate { return b.Get("OnlyNeeding", false); }, delegate (bool v) { b.Set("OnlyNeeding", v); });
                p.Check("Grouped (in vPatch's upgrade order)", delegate { return b.Get("GroupHosts", true); }, delegate (bool v) { b.Set("GroupHosts", v); });
                p.Check("Each host's system", delegate { return b.Get("ShowOs", true); }, delegate (bool v) { b.Set("ShowOs", v); });
                p.Slider("How many", 1, 60, delegate { return b.Get("Count", 12.0); }, delegate (double v) { b.Set("Count", Math.Round(v)); });
                p.Note("Click a host to open it in vPatch; right-click to check it now or open a shell in vTerm.");
            }
        }
    }

    /// <summary>The gallery pictures.</summary>
    public static class Pictures
    {
        public static FrameworkElement Bar(Theme t)
        {
            return ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Warn, 8), ModulePics.T(t, "⬆ 48", t.Fg, 12.5), ModulePics.T(t, "· 11", t.SecondaryAccent, 12.5) });
        }

        public static FrameworkElement Widget(Theme t)
        {
            return ModulePics.Col(4, new UIElement[]
            {
                ModulePics.T(t, "PROXMOX · FIRST", t.FgDim, 10, true),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Warn, 7), ModulePics.T(t, "pve1", t.Fg, 12), ModulePics.T(t, "12 ⬆", t.Fg, 11) }),
                ModulePics.T(t, "SERVERS", t.FgDim, 10, true),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Warn, 7), ModulePics.T(t, "media-vm", t.Fg, 12), ModulePics.T(t, "7 ⬆", t.Fg, 11) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "apps-lxc", t.Fg, 12), ModulePics.T(t, "✓", StatusColors.Good, 11) })
            });
        }
    }
}
