using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// UniFi Network, read-only. On a bar: a dot for the gateway (green online, red not), the gateway's download / upload and the number
    /// of clients - click for the popup. In a widget (and the popup): Devices (gateway, switches, access points with CPU / memory, uptime,
    /// clients; click one for its ports) or Clients (searchable: Wi-Fi / wired, IP, where it's connected, since when; new ones marked).
    /// </summary>
    public sealed class UnifiModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shown;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public UnifiModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            if (host.IsWidget) { Padding = new Thickness(2); Child = new UnifiView(mgr, _t, cfg, 0); return; }
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { _timer.Start(); Refresh(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            Unifi.Poke();
            _popup = new PopupWindow(_mgr, _host, this, _t, new UnifiView(_mgr, _t, _cfg, 440));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        void Refresh()
        {
            bool ok = Unifi.NetConfigured(_mgr.Settings);
            UnSnapshot s = ok ? Unifi.Network : new UnSnapshot();
            string key = ok + "|" + s.Fetched.Ticks + "|" + s.Error + "|" + UnView.Of(_cfg).BarRates + UnView.Of(_cfg).BarClients + "|" + _cfg.Title;
            if (key == _shown) return;
            _shown = key;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b) { return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center }; };
            Func<int, Brush, TextBlock> G = delegate (int c, Brush b) { return new TextBlock { Text = ((char)c).ToString(), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 0.95, Foreground = b, VerticalAlignment = VerticalAlignment.Center }; };
            if (!string.IsNullOrEmpty(_cfg.Title)) row.Children.Add(T(_cfg.Title + " ", _t.FgDim));
            if (!ok) { row.Children.Add(T("Set up UniFi in Settings > Plugins", _t.FgDim)); Child = row; ToolTip = null; _host.ContentChanged(); return; }
            UnDevice gw = s.Gateway;
            Brush dot = !s.Ok ? _t.FgDim : gw == null ? _t.FgDim : gw.Online ? StatusColors.Good : StatusColors.Bad;
            row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(G(0xE701, _t.Fg));
            if (!s.Ok)
            {
                row.Children.Add(T(s.Error.Length > 0 ? "  UniFi n/a" : "  UniFi…", _t.FgDim));
                ToolTip = s.Error.Length > 0 ? "Can't reach UniFi Network: " + s.Error : "Asking UniFi Network…";
            }
            else
            {
                bool two = _cfg.ShownTwoLines == true;      // M on a bar: the speeds over the clients
                var rates = new StackPanel { Orientation = Orientation.Horizontal };
                var clients = new StackPanel { Orientation = Orientation.Horizontal };
                if (UnView.Of(_cfg).BarRates && gw != null && gw.RxBps >= 0)
                {
                    rates.Children.Add(T("  ↓ " + Unifi.Rate(gw.RxBps), _t.MainAccent));
                    rates.Children.Add(T("  ↑ " + Unifi.Rate(gw.TxBps), _t.SecondaryAccent));
                }
                if (UnView.Of(_cfg).BarClients)
                {
                    clients.Children.Add(T(two && rates.Children.Count > 0 ? "  " : "   ", _t.FgDim));
                    clients.Children.Add(G(0xE716, _t.FgDim));
                    clients.Children.Add(T(" " + s.Clients.Count, _t.Fg));
                }
                if (two && rates.Children.Count > 0 && clients.Children.Count > 0)
                {
                    var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    lines.Children.Add(rates);
                    lines.Children.Add(clients);
                    row.Children.Add(lines);
                }
                else { row.Children.Add(rates); row.Children.Add(clients); }
                int wifi = s.Clients.FindAll(delegate (UnClient c) { return c.Wireless; }).Count;
                int online = s.Devices.FindAll(delegate (UnDevice d) { return d.Online; }).Count;
                var tip = new List<string> { "UniFi Network" + (s.Site.Length > 0 ? " · " + s.Site : "") };
                if (gw != null)
                    tip.Add(gw.Name + " (" + gw.Model + ") - " + (gw.Online ? "online" : Unifi.Nice(gw.State)) + (gw.Cpu >= 0 ? " · CPU " + Math.Round(gw.Cpu) + " %" : "") +
                            (gw.Mem >= 0 ? " · memory " + Math.Round(gw.Mem) + " %" : "") + (gw.UptimeSec > 0 ? " · up " + Unifi.Uptime(gw.UptimeSec) : ""));
                if (gw != null && gw.RxBps >= 0) tip.Add("Internet: ↓ " + Unifi.Rate(gw.RxBps) + "  ↑ " + Unifi.Rate(gw.TxBps));
                tip.Add(online + " of " + s.Devices.Count + " devices online · " + s.Clients.Count + " clients (" + wifi + " Wi-Fi, " + (s.Clients.Count - wifi) + " wired)");
                foreach (UnDevice d in s.Devices) if (!d.Online) tip.Add("  ✕ " + d.Name + " - " + Unifi.Nice(d.State));
                tip.Add("Click for devices and clients");
                ToolTip = string.Join("\n", tip);
            }
            Child = row;
            _host.ContentChanged();
        }
    }

    /// <summary>The Devices / Clients view (popup and widget). Rebuilds its list when new data arrives; the search box stays.</summary>
    public sealed class UnifiView : Border, ITypingModule
    {
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly ModuleConfig _cfg;
        readonly DispatcherTimer _timer;
        readonly StackPanel _list = new StackPanel();
        readonly StackPanel _tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        readonly TextBox _search = new TextBox();
        readonly TextBlock _status = new TextBlock(), _speed = new TextBlock();
        readonly bool _widget;
        /// <summary>The devices showing their ports: saved with the module, so they're open again next time.</summary>
        List<string> Open { get { return UnView.Of(_cfg).Open; } }
        static readonly Dictionary<string, string> Tabs = new Dictionary<string, string>();
        string _view, _shown;
        bool _menu;

        public UnifiView(BarManager mgr, Theme t, ModuleConfig cfg, double width)
        {
            _mgr = mgr; _t = t; _cfg = cfg;
            // the tab you were on survives the module being rebuilt (a settings change)
            string last;
            string view = UnView.Of(cfg).View;
            _view = view == "Clients" ? "Clients" : view == "All" && Tabs.TryGetValue(cfg.Id ?? "", out last) ? last : "Devices";
            _widget = width <= 0;
            if (width > 0) Width = width;
            var root = new StackPanel();
            // the internet speed through the gateway (widget; on a bar it's on the bar)
            _speed.FontFamily = t.Font; _speed.FontSize = t.FontSize; _speed.Margin = new Thickness(4, 0, 0, 6);
            root.Children.Add(_speed);
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            // the search box, drawn like the run box: rounded, a search icon, the text
            _search.Background = Brushes.Transparent; _search.Foreground = t.Fg; _search.CaretBrush = t.Fg; _search.BorderThickness = new Thickness(0);
            _search.SelectionBrush = t.MainAccent;
            _search.Padding = new Thickness(2, 0, 8, 0); _search.FontFamily = t.Font; _search.FontSize = Math.Max(8, t.FontSize * 0.95);
            _search.VerticalAlignment = VerticalAlignment.Center; _search.VerticalContentAlignment = VerticalAlignment.Center;
            _search.TextChanged += delegate { _shown = null; Build(); };
            _search.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                if (_search.Text.Length > 0) _search.Text = ""; else StopTyping();
            };
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sg.Children.Add(new TextBlock { Text = "\uE721", FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(9, t.FontSize * 0.95), Foreground = t.FgDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) });
            Grid.SetColumn(_search, 1);
            sg.Children.Add(_search);
            double sh = Math.Round(Math.Max(24, t.FontSize * 2.1));
            var box = new Border
            {
                Child = sg, Width = 170, Height = sh, CornerRadius = new CornerRadius(sh / 2), Background = Theme.Faded(t.Fg, 8),
                BorderBrush = Theme.Faded(t.Fg, 22), BorderThickness = new Thickness(1), Cursor = Cursors.IBeam, ToolTip = "Search by name, IP or MAC"
            };
            box.MouseEnter += delegate { box.BorderBrush = Theme.Faded(t.MainAccent, 70); };
            box.MouseLeave += delegate { if (!_search.IsKeyboardFocusWithin) box.BorderBrush = Theme.Faded(t.Fg, 22); };
            _search.GotKeyboardFocus += delegate { box.BorderBrush = Theme.Faded(t.MainAccent, 90); };
            _search.LostKeyboardFocus += delegate { if (!box.IsMouseOver) box.BorderBrush = Theme.Faded(t.Fg, 22); };
            // a widget doesn't take the keyboard until asked: a press on the box asks for it every time (also after clicking away)
            box.PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                StartTyping();
                if (!_search.IsMouseOver) e.Handled = true;
            };
            box.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            DockPanel.SetDock(box, Dock.Right);
            head.Children.Add(box);
            head.Children.Add(_tabs);
            root.Children.Add(head);
            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = width > 0 ? 460 : double.PositiveInfinity };
            root.Children.Add(scroll);
            _status.Foreground = t.FgDim; _status.FontFamily = t.Font; _status.FontSize = t.FontSize * 0.82; _status.Margin = new Thickness(4, 6, 4, 0); _status.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(_status);
            Child = root;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += delegate { Build(); };
            Loaded += delegate { _timer.Start(); Build(); };
            Unloaded += delegate { _timer.Stop(); };
            Build();
        }

        void StartTyping()
        {
            var w = Window.GetWindow(this) as WidgetWindow;
            if (w != null) w.BeginTyping(this);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                _search.Focus();
                Keyboard.Focus(_search);
                _search.CaretIndex = _search.Text.Length;
            }));
        }

        /// <summary>Clicked away (or Esc): give the keyboard back; the search stays.</summary>
        public void StopTyping()
        {
            if (_search.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            var w = Window.GetWindow(this) as WidgetWindow;
            if (w != null) w.EndTyping(this);
        }

        TextBlock T(string text, Brush b, double scale, bool bold = false)
        {
            return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize * scale, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        TextBlock G(int c, Brush b, double scale) { return new TextBlock { Text = ((char)c).ToString(), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * scale, Foreground = b, VerticalAlignment = VerticalAlignment.Center }; }

        void Build()
        {
            if (_menu) return;
            bool ok = Unifi.NetConfigured(_mgr.Settings);
            UnSnapshot s = ok ? Unifi.Network : new UnSnapshot();
            string show = UnView.Of(_cfg).View;
            if (show != "All") _view = show;
            string key = s.Fetched.Ticks + "|" + _view + "|" + show + "|" + UnView.Of(_cfg).BarRates + "|" + _search.Text + "|" + string.Join(",", Open) + "|" + s.Error;
            if (key == _shown) return;
            _shown = key;

            _speed.Visibility = Visibility.Collapsed;

            _tabs.Children.Clear();
            _tabs.Visibility = show == "All" ? Visibility.Visible : Visibility.Collapsed;
            int nd = s.Devices.Count, nc = s.Clients.Count;
            _tabs.Children.Add(Pills.Make(_t, "", "Devices" + (s.Ok ? " (" + nd + ")" : ""), null, false, delegate { _view = "Devices"; Tabs[_cfg.Id ?? ""] = _view; _shown = null; Build(); }, _view == "Devices"));
            _tabs.Children.Add(Pills.Make(_t, "", "Clients" + (s.Ok ? " (" + nc + ")" : ""), null, false, delegate { _view = "Clients"; Tabs[_cfg.Id ?? ""] = _view; _shown = null; Build(); }, _view == "Clients"));
            if (_tabs.Children.Count > 0) ((FrameworkElement)_tabs.Children[0]).Margin = new Thickness(0);
            _list.Children.Clear();
            if (!ok) { _status.Text = "Set up UniFi on the Plugins page in Settings (address and the Network API key)."; return; }
            if (!s.Ok) { _status.Text = s.Error.Length > 0 ? "Can't reach UniFi Network: " + s.Error : "Asking UniFi Network…"; return; }
            string q = _search.Text.Trim();
            if (_view == "Clients") BuildClients(s, q); else BuildDevices(s, q);
        }

        static int Order(UnDevice d) { return d.Kind == "gateway" ? 0 : d.Kind == "switch" ? 1 : d.Kind == "ap" ? 2 : 3; }

        bool Match(string q, params string[] parts)
        {
            if (q.Length == 0) return true;
            foreach (string p in parts) if (!string.IsNullOrEmpty(p) && p.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void BuildDevices(UnSnapshot s, string q)
        {
            var list = s.Devices.FindAll(delegate (UnDevice d) { return Match(q, d.Name, d.Model, d.Ip, d.Mac); });
            list.Sort(delegate (UnDevice a, UnDevice b)
            {
                int o = Order(a).CompareTo(Order(b));
                return o != 0 ? o : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            int shown = 0;
            foreach (UnDevice d in list)
            {
                if (shown++ >= UnView.Of(_cfg).Count) break;
                UnDevice dev = d;
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Brush dot = d.Online ? StatusColors.Good : d.State == "UPDATING" || d.State == "GETTING_READY" || d.State == "ADOPTING" ? StatusColors.Warn : StatusColors.Bad;
                g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) });
                var words = new StackPanel();
                words.Children.Add(T(d.Name.Length > 0 ? d.Name : d.Model, _t.Fg, 0.95, true));
                words.Children.Add(T((d.Kind == "ap" ? "Access point" : Unifi.Nice(d.Kind == "other" ? "device" : d.Kind)) + " · " + d.Model + (d.Ip.Length > 0 ? " · " + d.Ip : "") +
                                     (d.Online ? "" : " · " + Unifi.Nice(d.State)) + (d.Updatable ? " · update available" : ""), _t.FgDim, 0.8));
                Grid.SetColumn(words, 1);
                g.Children.Add(words);
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
                if (d.Cpu >= 0) right.Children.Add(T("CPU " + Math.Round(d.Cpu) + "%  ", _t.FgDim, 0.8));
                if (d.Mem >= 0) right.Children.Add(T("Mem " + Math.Round(d.Mem) + "%  ", _t.FgDim, 0.8));
                if (d.Clients > 0) { right.Children.Add(G(0xE716, _t.FgDim, 0.75)); right.Children.Add(T(" " + d.Clients + "  ", _t.Fg, 0.82)); }
                if (d.UptimeSec > 0) right.Children.Add(T(Unifi.Uptime(d.UptimeSec), _t.FgDim, 0.8));
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
                var row = RowBox(g);
                row.ToolTip = d.Name + "\n" + d.Model + (d.Firmware.Length > 0 ? " · firmware " + d.Firmware : "") + "\n" + (d.Ip.Length > 0 ? d.Ip + " · " : "") + d.Mac +
                              (d.RxBps >= 0 ? "\nUplink ↓ " + Unifi.Rate(d.RxBps) + "  ↑ " + Unifi.Rate(d.TxBps) : "") +
                              (d.Ports.Count > 0 ? "\nClick for its ports" : "") + "\nRight-click to copy its IP / MAC";
                row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (dev.Ports.Count == 0) return;
                    if (!Open.Remove(dev.Id)) Open.Add(dev.Id);
                    _mgr.SaveSoon();          // saved without rebuilding anything
                    _shown = null; Build();
                };
                row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Menu(row, dev.Name, dev.Ip, dev.Mac); };
                _list.Children.Add(row);
                if (Open.Contains(d.Id) && d.Ports.Count > 0) _list.Children.Add(Ports(d));
            }
            int on = s.Devices.FindAll(delegate (UnDevice d) { return d.Online; }).Count;
            UnDevice gw = s.Gateway;
            Status(s, (list.Count > UnView.Of(_cfg).Count ? "+ " + (list.Count - UnView.Of(_cfg).Count) + " more - search to narrow it down.  " : "") +
                      on + " of " + s.Devices.Count + " online");
        }

        /// <summary>The line at the bottom: what's listed, the internet speed (download in the Main Accent, upload in the Secondary Accent,
        /// like the Network module) and when it was updated.</summary>
        void Status(UnSnapshot s, string first)
        {
            UnDevice gw = s.Gateway;
            _status.Inlines.Clear();
            _status.Inlines.Add(new System.Windows.Documents.Run(first));
            if (gw != null && gw.RxBps >= 0)
            {
                _status.Inlines.Add(new System.Windows.Documents.Run(" · Internet "));
                _status.Inlines.Add(new System.Windows.Documents.Run("↓ " + Unifi.Rate(gw.RxBps)) { Foreground = _t.MainAccent });
                _status.Inlines.Add(new System.Windows.Documents.Run("  ↑ " + Unifi.Rate(gw.TxBps)) { Foreground = _t.SecondaryAccent });
            }
            _status.Inlines.Add(new System.Windows.Documents.Run(" · updated " + TimeText.Ago(s.Fetched)));
        }

        /// <summary>A device's ports as little squares: green up, faded down; ⚡ when PoE is on. Hover for the speed.</summary>
        FrameworkElement Ports(UnDevice d)
        {
            var wrap = new WrapPanel();
            var speeds = new SortedSet<int>();
            foreach (UnPort p in d.Ports)
            {
                bool up = p.State == "UP";
                SolidColorBrush col = SpeedBrush(p.SpeedMbps);
                if (up) speeds.Add(SpeedClass(p.SpeedMbps));
                var b = new Border
                {
                    Width = 30, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 4),
                    Background = up ? Theme.Faded(col, 30) : _t.Track,
                    BorderBrush = up ? col : Theme.Faded(_t.FgDim, 40), BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = p.Idx + (p.Poe ? "⚡" : ""), FontSize = _t.FontSize * 0.72, Foreground = up ? _t.Fg : _t.FgDim,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    },
                    ToolTip = "Port " + p.Idx + " - " + (up ? (p.SpeedMbps > 0 ? Speed(p.SpeedMbps) : "up") : "down") +
                              (p.MaxSpeedMbps > 0 ? " (max " + Speed(p.MaxSpeedMbps) + ")" : "") + (p.Connector.Length > 0 ? " · " + p.Connector : "") +
                              (p.Poe ? " · PoE on" + (p.PoeState.Length > 0 ? " (" + p.PoeState.ToLowerInvariant() + ")" : "") : "")
                };
                wrap.Children.Add(b);
            }
            // a key for the speeds in use
            var root = new StackPanel { Margin = new Thickness(26, 2, 6, 6) };
            root.Children.Add(wrap);
            if (speeds.Count > 0)
            {
                var key = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
                foreach (int sc in speeds.Reverse())
                {
                    var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
                    item.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = SpeedBrush(sc), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                    item.Children.Add(T(SpeedName(sc), _t.FgDim, 0.72));
                    key.Children.Add(item);
                }
                root.Children.Add(key);
            }
            return root;
        }

        /// <summary>Port speeds in steps, coloured roughly the way UniFi does: 10 Gbps+ purple, 2.5 / 5 Gbps blue, 1 Gbps green,
        /// 100 Mbps amber, 10 Mbps orange (and unknown grey).</summary>
        static int SpeedClass(int mbps)
        {
            if (mbps >= 10000) return 10000;
            if (mbps >= 2500) return 2500;
            if (mbps >= 1000) return 1000;
            if (mbps >= 100) return 100;
            if (mbps > 0) return 10;
            return 0;
        }

        static string SpeedName(int cls)
        {
            switch (cls)
            {
                case 10000: return "10 Gbps+";
                case 2500: return "2.5 - 5 Gbps";
                case 1000: return "1 Gbps";
                case 100: return "100 Mbps";
                case 10: return "10 Mbps";
                default: return "Unknown";
            }
        }

        static readonly Dictionary<int, SolidColorBrush> SpeedBrushes = new Dictionary<int, SolidColorBrush>();
        static SolidColorBrush SpeedBrush(int mbps)
        {
            int cls = SpeedClass(mbps);
            SolidColorBrush b;
            if (SpeedBrushes.TryGetValue(cls, out b)) return b;
            Color c;
            switch (cls)
            {
                case 10000: c = Color.FromRgb(0xA8, 0x55, 0xF7); break;    // purple (a strong one, like the blue)
                case 2500: c = Color.FromRgb(0x3D, 0x8B, 0xFF); break;     // blue (a strong one - the pale one looked like an unused port)
                case 1000: c = Color.FromRgb(0x6C, 0xCB, 0x7E); break;     // green (the status green)
                case 100: c = Color.FromRgb(0xF5, 0xC5, 0x42); break;      // gold (the status yellow)
                case 10: c = Color.FromRgb(0xFA, 0xB3, 0x87); break;       // orange
                default: c = Color.FromRgb(0x9A, 0x9A, 0xA8); break;       // grey
            }
            b = new SolidColorBrush(c);
            b.Freeze();
            SpeedBrushes[cls] = b;
            return b;
        }

        static string Speed(int mbps) { return mbps >= 1000 ? (mbps / 1000.0).ToString("0.#") + " Gbps" : mbps + " Mbps"; }

        void BuildClients(UnSnapshot s, string q)
        {
            var list = s.Clients.FindAll(delegate (UnClient c) { return Match(q, c.Name, c.Ip, c.Mac); });
            list.Sort(delegate (UnClient a, UnClient b)
            {
                if (a.IsNew != b.IsNew) return a.IsNew ? -1 : 1;
                return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            int shown = 0;
            foreach (UnClient c in list)
            {
                if (shown++ >= UnView.Of(_cfg).Count) break;
                UnClient cl = c;
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = G(c.Wireless ? 0xE701 : 0xE839, c.Wireless ? _t.MainAccent : _t.FgDim, 0.95);
                icon.Margin = new Thickness(2, 0, 8, 0);
                g.Children.Add(icon);
                var words = new StackPanel();
                var name = new StackPanel { Orientation = Orientation.Horizontal };
                name.Children.Add(T(c.Label, _t.Fg, 0.95, true));
                if (c.IsNew)
                    name.Children.Add(new Border
                    {
                        Background = _t.SecondaryAccent, CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = "NEW", FontSize = _t.FontSize * 0.62, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(_t.SecondaryAccent) }
                    });
                words.Children.Add(name);
                UnDevice via = s.Device(c.UplinkId);
                words.Children.Add(T((c.Ip.Length > 0 ? c.Ip : c.Mac) + (via != null ? " · via " + via.Name : "") + (c.Type == "VPN" ? " · VPN" : ""), _t.FgDim, 0.8));
                Grid.SetColumn(words, 1);
                g.Children.Add(words);
                if (c.ConnectedAt > DateTime.MinValue)
                {
                    var since = T(TimeText.Ago(c.ConnectedAt), _t.FgDim, 0.78);
                    since.Margin = new Thickness(8, 0, 2, 0);
                    Grid.SetColumn(since, 2);
                    g.Children.Add(since);
                }
                var row = RowBox(g);
                row.ToolTip = c.Label + "\n" + (c.Wireless ? "Wi-Fi" : c.Type == "VPN" ? "VPN" : "Wired") + (via != null ? " via " + via.Name : "") + "\n" + c.Ip + "  " + c.Mac +
                              (c.ConnectedAt > DateTime.MinValue ? "\nConnected " + TimeText.Full(c.ConnectedAt.ToLocalTime()) : "") + (c.IsNew ? "\nNew on the network in the last hour" : "") +
                              "\nRight-click to copy";
                row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Menu(row, cl.Label, cl.Ip, cl.Mac); };
                _list.Children.Add(row);
            }
            int wifi = s.Clients.FindAll(delegate (UnClient c) { return c.Wireless; }).Count;
            Status(s, (list.Count > UnView.Of(_cfg).Count ? "+ " + (list.Count - UnView.Of(_cfg).Count) + " more - search to narrow it down.  " : "") +
                      wifi + " Wi-Fi · " + (s.Clients.Count - wifi) + " wired");
        }

        Border RowBox(UIElement content)
        {
            var b = new Border { Child = content, Padding = new Thickness(4, 3, 4, 3), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
            b.MouseEnter += delegate { b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            return b;
        }

        /// <summary>Right-click: copy (read-only: nothing here changes UniFi).</summary>
        void Menu(FrameworkElement target, string name, string ip, string mac)
        {
            var m = new ContextMenu();
            Action<string, string> add = delegate (string header, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                var mi = new MenuItem { Header = header + "   " + text };
                mi.Click += delegate { try { Clipboard.SetText(text); } catch { } };
                m.Items.Add(mi);
            };
            add("Copy IP", ip);
            add("Copy MAC", mac);
            add("Copy name", name);
            m.Items.Add(new Separator());
            var web = new MenuItem { Header = "Open UniFi Network" };
            web.Click += delegate { try { Process.Start(new ProcessStartInfo(Unifi.NetworkWeb(_mgr.Settings)) { UseShellExecute = true }); } catch { } };
            m.Items.Add(web);
            PopupWindow host = Window.GetWindow(this) as PopupWindow;
            if (host != null) host.Pinned = true;
            _menu = true;
            m.Closed += delegate { if (host != null) host.Pinned = false; _menu = false; };
            m.PlacementTarget = target;
            m.Placement = PlacementMode.MousePoint;
            m.IsOpen = true;
        }
    }
}
