using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Proxmox VE: the cluster's nodes (CPU / memory), guests (VMs and containers, with start / shut down / reboot / stop) or storage.
    /// On a bar it is a compact summary - click it for the whole cluster in a popup. In a widget it is a list; click a guest for its
    /// actions. Data comes from ProxmoxClient (connection on the Plugins page of Settings).
    /// </summary>
    public sealed class ProxmoxModule : Border, IMultiRow
    {
        static readonly Brush Up = StatusColors.Good, Busy = StatusColors.Warn, Down = StatusColors.Bad;
        static Brush Frozen(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        /// <summary>Actions sent but not showing yet ("starting…"), by guest id; shared by every Proxmox module.</summary>
        sealed class Pending { public string Text; public string FromStatus; public DateTime Until; }
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();
        static int _waitingVersion;
        /// <summary>A guest's menu or question is open: hold the redraw so the row it belongs to stays put.</summary>
        static bool _menuOpen, _asking;

        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        /// <summary>This module's Proxmox settings (in its Extra).</summary>
        PvView View { get { return PvView.Of(_cfg); } }
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;
        PveSnapshot _snap;

        public int RowCount { get { return _bar.IsWidget ? 0 : 1; } }

        public ProxmoxModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _t = bar.Theme.ForModule(cfg);
            Padding = new Thickness(6, 0, 6, 0);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            if (!bar.IsWidget)
            {
                Cursor = Cursors.Hand;
                // handle the press too, or the bar takes it as the start of a drag and the click never arrives
                MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            }

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { Refresh(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        // =================================================================
        // Refresh
        // =================================================================
        void Refresh()
        {
            if (_menuOpen) return;
            PveSnapshot s = ProxmoxClient.Get();
            ExpireWaiting(s);
            string key = (ProxmoxClient.Configured ? "c" : "n") + "|" + (s == null ? "none" : s.Fetched.Ticks + "|" + s.Error) + "|" + _waitingVersion;
            if (key == _shownKey) return;
            _shownKey = key;
            _snap = s;
            Child = _bar.IsWidget ? BuildWidget(s) : BuildBar(s);
            ToolTip = _bar.IsWidget ? null : TipText(s);
            if (_popup != null && _popup.IsOpen) _popup.SetContent(BuildPopup());
            _bar.ContentChanged();
        }

        static void ExpireWaiting(PveSnapshot s)
        {
            if (Waiting.Count == 0) return;
            var gone = new List<string>();
            foreach (var kv in Waiting)
            {
                PveResource g = s == null ? null : s.Items.Find(delegate (PveResource r) { return r.Id == kv.Key; });
                if (DateTime.UtcNow > kv.Value.Until || (g != null && kv.Value.FromStatus != null && g.Status != kv.Value.FromStatus)) gone.Add(kv.Key);
            }
            foreach (string k in gone) Waiting.Remove(k);
            if (gone.Count > 0) _waitingVersion++;
        }

        /// <summary>Not set up / not answered yet / failed: the one line to show instead, or null when there is data.</summary>
        string Problem(PveSnapshot s)
        {
            if (!ProxmoxClient.Configured) return "Set up Proxmox in Settings > Plugins";
            if (s == null) return "Proxmox…";
            if (s.Fetched == DateTime.MinValue) return s.Error.Length > 0 ? "Proxmox n/a" : "Proxmox…";
            return null;
        }

        string TipText(PveSnapshot s)
        {
            string p = Problem(s);
            if (p != null) return s != null && s.Error.Length > 0 ? "Could not reach Proxmox: " + s.Error : p;
            var lines = new List<string>();
            lines.Add(s.ClusterName.Length > 0 ? "Cluster " + s.ClusterName + (s.Quorate ? "" : " - NOT QUORATE") : "Proxmox VE");
            foreach (PveResource n in s.Nodes(View.Node))
                lines.Add(n.Name + ": " + (n.Running ? "CPU " + ProxmoxClient.Percent(n.Cpu) + " of " + n.MaxCpu + ",  RAM " + ProxmoxClient.Bytes(n.Mem) + " / " + ProxmoxClient.Bytes(n.MaxMem) + ",  up " + ProxmoxClient.Uptime(n.Uptime) : "offline"));
            List<PveResource> gs = s.Guests(View.Node, false, false);
            int run = gs.FindAll(delegate (PveResource g) { return g.Status == "running"; }).Count;
            lines.Add(run + " of " + gs.Count + " guests running");
            if (s.Error.Length > 0) lines.Add("Last update failed: " + s.Error);
            lines.Add("Click for the whole cluster");
            return string.Join("\n", lines);
        }

        // =================================================================
        // Pieces
        // =================================================================
        TextBlock T(string text, Brush b, double scale = 1.0, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = _t.Font, FontSize = Math.Max(7, _t.FontSize * scale),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        /// <summary>A bar filled to fraction (0..1). Star columns share the width, so it stretches with whatever it is given.</summary>
        FrameworkElement Meter(double fraction, Brush fill, double height, double minWidth)
        {
            fraction = Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction));
            if (View.Style == "Dots")
            {
                // a row of dots filling up (vShell's dot meter; stretches as the bar does)
                double dh = Math.Max(5, Math.Round(height * 1.3));
                return new MetricView
                {
                    GraphStyle = "Dots", Value = fraction * 100, Fill = fill, Track = _t.Track, GraphW = minWidth, GraphH = dh,
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, MinWidth = minWidth
                };
            }
            var g = new Grid { Height = height, MinWidth = minWidth, VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
            var track = new Border { Background = _t.Track, CornerRadius = new CornerRadius(height / 2) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (fraction > 0.004) g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
            return g;
        }

        FrameworkElement Dot(Brush b, double size)
        {
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        }

        Brush GuestBrush(PveResource g)
        {
            if (Waiting.ContainsKey(g.Id) || g.Lock.Length > 0) return Busy;
            if (g.Status == "running") return Up;
            if (g.Status == "paused" || g.Status == "suspended") return Busy;
            return _t.FgDim;
        }

        string GuestState(PveResource g)
        {
            Pending p;
            if (Waiting.TryGetValue(g.Id, out p)) return p.Text;
            if (g.Lock.Length > 0) return g.Lock + "…";
            return g.Status;
        }

        static void Cell(Grid g, UIElement e, int row, int col, int span = 1)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            if (span > 1) Grid.SetColumnSpan(e, span);
            g.Children.Add(e);
        }

        // =================================================================
        // Bar: one compact line
        // =================================================================
        FrameworkElement BuildBar(PveSnapshot s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(_cfg.Title)) row.Children.Add(T(_cfg.Title + " ", _t.FgDim));
            string p = Problem(s);
            if (p != null) { row.Children.Add(T(p, _t.FgDim)); return row; }
            bool meter = View.Style != "Text";
            double h = Math.Max(3, Math.Round(_bar.HostHeight * 0.13));
            switch (View.Show)
            {
                case "Guests":
                    {
                        List<PveResource> gs = s.Guests(View.Node, false, View.ShowTemplates);
                        int run = gs.FindAll(delegate (PveResource g) { return g.Status == "running"; }).Count;
                        bool busy = gs.Exists(delegate (PveResource g) { return Waiting.ContainsKey(g.Id) || g.Lock.Length > 0; });
                        row.Children.Add(Dot(busy ? Busy : run > 0 ? Up : _t.FgDim, Math.Round(_t.FontSize * 0.6)));
                        if (View.Node.Length > 0) { var n = T(View.Node + " ", _t.FgDim); row.Children.Add(n); }
                        row.Children.Add(T(run + "/" + gs.Count, _t.Fg));
                        row.Children.Add(T(" running", _t.FgDim));
                        break;
                    }
                case "Storage":
                    {
                        bool first = true;
                        foreach (PveResource st in s.Storage(View.Node))
                        {
                            var name = T(st.Name + " ", _t.FgDim);
                            name.Margin = new Thickness(first ? 0 : 12, 0, 0, 0);
                            row.Children.Add(name);
                            if (meter) row.Children.Add(Meter(st.DiskFraction, _t.MainAccent, h, 34));
                            else row.Children.Add(T(ProxmoxClient.Percent(st.DiskFraction), _t.Fg));
                            first = false;
                        }
                        if (first) row.Children.Add(T("No storage", _t.FgDim));
                        break;
                    }
                default:
                    {
                        bool first = true;
                        foreach (PveResource n in s.Nodes(View.Node))
                        {
                            var name = T(n.Name + " ", _t.FgDim);
                            name.Margin = new Thickness(first ? 0 : 12, 0, 0, 0);
                            row.Children.Add(name);
                            first = false;
                            if (!n.Running) { row.Children.Add(T("offline", Down)); continue; }
                            if (meter)
                            {
                                var two = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 34 };
                                var a = Meter(n.Cpu, _t.MainAccent, h, 34);
                                var b = Meter(n.MemFraction, _t.SecondaryAccent, h, 34);
                                b.Margin = new Thickness(0, Math.Max(2, h * 0.6), 0, 0);
                                two.Children.Add(a);
                                two.Children.Add(b);
                                row.Children.Add(two);
                            }
                            else
                            {
                                row.Children.Add(T(ProxmoxClient.Percent(n.Cpu), _t.MainAccent));
                                row.Children.Add(T(" · ", _t.FgDim));
                                row.Children.Add(T(ProxmoxClient.Percent(n.MemFraction), _t.SecondaryAccent));
                            }
                        }
                        if (first) row.Children.Add(T("No nodes", _t.FgDim));
                        break;
                    }
            }
            if (s.Error.Length > 0) row.Children.Add(T("  !", Down, 1.0, true));     // showing old data: the last read failed
            return row;
        }

        // =================================================================
        // Widget: a list
        // =================================================================
        FrameworkElement BuildWidget(PveSnapshot s)
        {
            var col = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            string p = Problem(s);
            if (p != null)
            {
                var tb = T(s != null && s.Error.Length > 0 ? "Could not reach Proxmox: " + s.Error : p, _t.FgDim);
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MaxWidth = 280;
                col.Children.Add(tb);
                return col;
            }
            switch (View.Show)
            {
                case "Guests": col.Children.Add(GuestList(s.Guests(View.Node, View.RunningOnly, View.ShowTemplates), View.GuestStats, View.Node.Length == 0 && s.Nodes("").Count > 1, s)); break;
                case "Storage": col.Children.Add(StorageList(s, false)); break;
                default: col.Children.Add(NodeList(s, false)); break;
            }
            if (s.Error.Length > 0)
            {
                var e = T("Last update failed: " + s.Error, Down, 0.85);
                e.Margin = new Thickness(0, 6, 0, 0);
                e.TextWrapping = TextWrapping.Wrap;
                e.MaxWidth = 280;
                col.Children.Add(e);
            }
            return col;
        }

        FrameworkElement NodeList(PveSnapshot s, bool detail)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool meter = detail || View.Style != "Text";
            double h = Math.Max(4, Math.Round(_t.FontSize * 0.45));
            List<PveResource> nodes = s.Nodes(detail ? "" : View.Node);
            if (nodes.Count == 0) return T("No nodes (does the token have the PVEAuditor role?)", _t.FgDim);
            foreach (PveResource n in nodes)
            {
                int r = g.RowDefinitions.Count;
                if (r > 0) { g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); r++; }

                // name line: dot, name, uptime
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var head = new DockPanel { LastChildFill = true };
                var up = T(n.Running ? "up " + ProxmoxClient.Uptime(n.Uptime) : "offline", n.Running ? _t.FgDim : Down, 0.85);
                up.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(up, Dock.Right);
                head.Children.Add(up);
                var left = new StackPanel { Orientation = Orientation.Horizontal };
                left.Children.Add(Dot(n.Running ? Up : Down, Math.Round(_t.FontSize * 0.55)));
                left.Children.Add(T(n.Name, _t.Fg, 1.0, true));
                head.Children.Add(left);
                // right-click a node: update it (Proxmox's own upgrade shell) or open the web interface
                head.Background = Brushes.Transparent;
                head.ToolTip = "Right-click for Open shell";
                PveResource node = n;
                head.MouseRightButtonUp += delegate (object s2, MouseButtonEventArgs e2) { e2.Handled = true; ShowNodeMenu(head, node); };
                Cell(g, head, r, 0, 3);
                if (!n.Running) continue;

                if (meter)
                {
                    AddMeterRow(g, "CPU", n.Cpu, _t.MainAccent, h, detail ? ProxmoxClient.Percent(n.Cpu) + " of " + n.MaxCpu : ProxmoxClient.Percent(n.Cpu));
                    AddMeterRow(g, "RAM", n.MemFraction, _t.SecondaryAccent, h, detail ? ProxmoxClient.Bytes(n.Mem) + " / " + ProxmoxClient.Bytes(n.MaxMem) : ProxmoxClient.Percent(n.MemFraction));
                }
                else
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    line.Children.Add(T("CPU ", _t.FgDim));
                    line.Children.Add(T(ProxmoxClient.Percent(n.Cpu), _t.MainAccent));
                    line.Children.Add(T("   RAM ", _t.FgDim));
                    line.Children.Add(T(ProxmoxClient.Percent(n.MemFraction), _t.SecondaryAccent));
                    line.Children.Add(T("  " + ProxmoxClient.Bytes(n.Mem) + " / " + ProxmoxClient.Bytes(n.MaxMem), _t.FgDim, 0.85));
                    Cell(g, line, g.RowDefinitions.Count - 1, 0, 3);
                }
            }
            return g;
        }

        void AddMeterRow(Grid g, string label, double fraction, Brush fill, double h, string value)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int r = g.RowDefinitions.Count - 1;
            var l = T(label, _t.FgDim, 0.85);
            l.Margin = new Thickness(0, 3, 8, 0);
            Cell(g, l, r, 0);
            var m = Meter(fraction, fill, h, 80);
            m.Margin = new Thickness(0, 3, 0, 0);
            Cell(g, m, r, 1);
            var v = T(value, _t.Fg, 0.85);
            v.Margin = new Thickness(8, 3, 0, 0);
            v.MinWidth = 34;
            v.TextAlignment = TextAlignment.Right;
            Cell(g, v, r, 2);
        }

        /// <summary>A group heading like the Nodes list: status dot, bold name, a note on the right ("3/5 running", "offline").</summary>
        FrameworkElement GroupHeading(string name, bool dot, bool online, string right, bool first)
        {
            var head = new DockPanel { Margin = new Thickness(0, first ? 0 : 10, 0, 3) };
            if (!string.IsNullOrEmpty(right) || !online)
            {
                var note = T(online ? right : "offline", online ? _t.FgDim : Down, 0.85);
                note.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(note, Dock.Right);
                head.Children.Add(note);
            }
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            if (dot) left.Children.Add(Dot(online ? Up : Down, Math.Round(_t.FontSize * 0.55)));
            left.Children.Add(T(name, _t.Fg, 1.0, true));
            head.Children.Add(left);
            return head;
        }

        static bool NodeOnline(PveSnapshot snap, string nodeName)
        {
            PveResource node = snap == null ? null : snap.Items.Find(delegate (PveResource q) { return q.Type == "node" && q.Name == nodeName; });
            return node == null || node.Running;
        }

        FrameworkElement StorageList(PveSnapshot s, bool detail)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            double h = Math.Max(4, Math.Round(_t.FontSize * 0.45));
            bool manyNodes = s.Nodes("").Count > 1 && (detail || View.Node.Length == 0);
            bool meter = detail || View.Style != "Text";
            List<PveResource> pools = s.Storage(detail ? "" : View.Node);
            if (pools.Count == 0) return T("No storage", _t.FgDim);
            string lastGroup = null;
            foreach (PveResource st in pools)
            {
                // several nodes: shared pools first under "Shared", then each node's own pools under the node's name
                string group = st.Shared ? "" : st.Node;
                if (manyNodes && group != lastGroup)
                {
                    int n = pools.FindAll(delegate (PveResource q) { return (q.Shared ? "" : q.Node) == group; }).Count;
                    string count = n + (n == 1 ? " pool" : " pools");
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Cell(g, group.Length == 0 ? GroupHeading("Shared", false, true, count, lastGroup == null)
                                              : GroupHeading(group, true, NodeOnline(s, group), count, lastGroup == null),
                         g.RowDefinitions.Count - 1, 0, 3);
                    lastGroup = group;
                }
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int r = g.RowDefinitions.Count - 1;
                var name = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 10, 0) };
                name.Children.Add(T(st.Name, _t.Fg));

                Cell(g, name, r, 0);
                string val = detail ? ProxmoxClient.Bytes(st.Disk) + " / " + ProxmoxClient.Bytes(st.MaxDisk) : ProxmoxClient.Percent(st.DiskFraction);
                if (meter)
                {
                    var m = Meter(st.DiskFraction, _t.MainAccent, h, 80);
                    m.Margin = new Thickness(0, 3, 0, 0);
                    Cell(g, m, r, 1);
                }
                var v = T(val, meter ? _t.Fg : _t.MainAccent, 0.85);
                v.Margin = new Thickness(8, 3, 0, 0);
                v.TextAlignment = TextAlignment.Right;
                v.ToolTip = ProxmoxClient.Bytes(st.Disk) + " used of " + ProxmoxClient.Bytes(st.MaxDisk) + " (" + ProxmoxClient.Percent(st.DiskFraction) + ")";
                Cell(g, v, r, meter ? 2 : 1, meter ? 1 : 2);
            }
            return g;
        }

        FrameworkElement GuestList(List<PveResource> guests, bool stats, bool nodeHeads, PveSnapshot snap)
        {
            if (nodeHeads)
            {
                // grouped by node (node name order), then by id
                guests = new List<PveResource>(guests);
                guests.Sort(delegate (PveResource a, PveResource b)
                {
                    int c = string.Compare(a.Node, b.Node, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : a.VmId.CompareTo(b.VmId);
                });
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // dot
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // id
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // name
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // cpu / state
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // memory
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // uptime
            bool uptime = View.GuestUptime;
            if (guests.Count == 0) return T("No guests", _t.FgDim);
            if (stats)
            {
                // small CPU / MEM labels over their columns
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var hc = T("CPU", _t.FgDim, 0.7, true);
                hc.Margin = new Thickness(10, 0, 0, 1);
                hc.TextAlignment = TextAlignment.Right;
                Cell(g, hc, 0, 3);
                var hm = T("MEM", _t.FgDim, 0.7, true);
                hm.Margin = new Thickness(10, 0, 0, 1);
                hm.TextAlignment = TextAlignment.Right;
                Cell(g, hm, 0, 4);
            }
            string lastNode = null;
            foreach (PveResource x in guests)
            {
                if (nodeHeads && x.Node != lastNode)
                {
                    // the node's name like on the Nodes list: status dot, bold name, how many of its guests run
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    string nodeName = x.Node;
                    List<PveResource> mine = guests.FindAll(delegate (PveResource q) { return q.Node == nodeName; });
                    int run = mine.FindAll(delegate (PveResource q) { return q.Status == "running"; }).Count;
                    Cell(g, GroupHeading(nodeName, true, NodeOnline(snap, nodeName), run + "/" + mine.Count + " running", lastNode == null), g.RowDefinitions.Count - 1, 0, 6);
                    lastNode = x.Node;
                }
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int r = g.RowDefinitions.Count - 1;

                // a background behind the whole row so it highlights on hover and takes the click
                var hit = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, Margin = new Thickness(-4, 0, -4, 0) };
                PveResource guest = x;
                hit.MouseEnter += delegate { hit.Background = _t.Hover; };
                hit.MouseLeave += delegate { hit.Background = Brushes.Transparent; };
                hit.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                hit.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; ShowGuestMenu(hit, guest); };
                hit.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; ShowGuestMenu(hit, guest); };
                hit.ToolTip = GuestTip(x);
                Cell(g, hit, r, 0, 6);

                var pad = new Thickness(0, 2, 0, 2);
                var dot = Dot(GuestBrush(x), Math.Round(_t.FontSize * 0.55));
                dot.IsHitTestVisible = false;
                Cell(g, dot, r, 0);
                var id = T(x.VmId.ToString(CultureInfo.InvariantCulture), _t.FgDim, 0.85);
                id.Margin = new Thickness(0, 2, 8, 2);
                id.IsHitTestVisible = false;
                Cell(g, id, r, 1);
                var name = T((x.Name.Length > 0 ? x.Name : x.Kind) + (x.Template ? " (template)" : ""), x.Status == "running" ? _t.Fg : _t.FgDim);
                name.Margin = pad;
                name.IsHitTestVisible = false;
                Cell(g, name, r, 2);
                if (stats && x.Status == "running" && !Waiting.ContainsKey(x.Id))
                {
                    var cpu = T(ProxmoxClient.Percent(x.Cpu), _t.MainAccent, 0.85);
                    cpu.Margin = new Thickness(10, 2, 0, 2);
                    cpu.MinWidth = 30;
                    cpu.TextAlignment = TextAlignment.Right;
                    cpu.IsHitTestVisible = false;
                    Cell(g, cpu, r, 3);
                    var mem = T(ProxmoxClient.Bytes(x.Mem), _t.SecondaryAccent, 0.85);
                    mem.Margin = new Thickness(10, 2, 0, 2);
                    mem.MinWidth = 52;
                    mem.TextAlignment = TextAlignment.Right;
                    mem.IsHitTestVisible = false;
                    Cell(g, mem, r, 4);
                }
                else if (x.Status != "running" || Waiting.ContainsKey(x.Id))
                {
                    var st = T(GuestState(x), Waiting.ContainsKey(x.Id) ? Busy : _t.FgDim, 0.85);
                    st.Margin = new Thickness(10, 2, 0, 2);
                    st.TextAlignment = TextAlignment.Right;
                    st.IsHitTestVisible = false;
                    Cell(g, st, r, 3, 2);
                }
                if (uptime && x.Status == "running" && x.Uptime > 0 && !Waiting.ContainsKey(x.Id))
                {
                    var up = T(ProxmoxClient.Uptime(x.Uptime), _t.FgDim, 0.85);
                    up.Margin = new Thickness(10, 2, 0, 2);
                    up.MinWidth = 44;
                    up.TextAlignment = TextAlignment.Right;
                    up.IsHitTestVisible = false;
                    up.ToolTip = "Up " + ProxmoxClient.Uptime(x.Uptime);
                    Cell(g, up, r, 5);
                }
            }
            return g;
        }

        string GuestTip(PveResource x)
        {
            var l = new List<string>();
            l.Add(x.Kind + " " + x.VmId + "  " + x.Name + "  on " + x.Node);
            l.Add("Status: " + GuestState(x) + (x.Status == "running" && x.Uptime > 0 ? ", up " + ProxmoxClient.Uptime(x.Uptime) : ""));
            if (x.Status == "running")
                l.Add("CPU " + ProxmoxClient.Percent(x.Cpu) + " of " + x.MaxCpu + ",  RAM " + ProxmoxClient.Bytes(x.Mem) + " / " + ProxmoxClient.Bytes(x.MaxMem));
            l.Add("Click for actions");
            return string.Join("\n", l);
        }

        // =================================================================
        // Popup (bars): the whole cluster
        // =================================================================
        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (!ProxmoxClient.Configured) { _mgr.OpenSettings(null); return; }
            _popup = new PopupWindow(_mgr, _bar, this, _bar.Theme, BuildPopup());
            _mgr.ShowPopup(_popup);
        }

        FrameworkElement BuildPopup()
        {
            PveSnapshot s = _snap;
            var root = new StackPanel { Margin = new Thickness(10, 8, 10, 8), MinWidth = 300, MaxWidth = 460 };
            string p = Problem(s);
            if (p != null)
            {
                var tb = T(s != null && s.Error.Length > 0 ? "Could not reach Proxmox: " + s.Error : p, _t.FgDim);
                tb.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(tb);
                return root;
            }
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (!s.Quorate)
            {
                var q = T("not quorate", Down, 0.85, true);
                DockPanel.SetDock(q, Dock.Right);
                title.Children.Add(q);
            }
            title.Children.Add(T(s.ClusterName.Length > 0 ? "Cluster " + s.ClusterName : "Proxmox VE", _t.Fg, 1.1, true));
            root.Children.Add(title);

            // only what the module's Show picks (nodes, guests or storage), as its widget does
            switch (View.Show)
            {
                case "Guests":
                    List<PveResource> all = s.Guests(View.Node, false, View.ShowTemplates);
                    root.Children.Add(Heading("GUESTS  " + all.FindAll(delegate (PveResource g) { return g.Status == "running"; }).Count + "/" + all.Count + " running", false));
                    var scroll = new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                    scroll.Content = GuestList(s.Guests(View.Node, View.RunningOnly, View.ShowTemplates), true, View.Node.Length == 0 && s.Nodes("").Count > 1, s);
                    WidgetWindow.StyleScrollbars(scroll);       // the suite's thin scrollbar, not Windows' own
                    root.Children.Add(scroll);
                    break;
                case "Storage":
                    root.Children.Add(Heading("STORAGE", false));
                    root.Children.Add(StorageList(s, true));
                    break;
                default:
                    root.Children.Add(Heading("NODES", false));
                    root.Children.Add(NodeList(s, true));
                    break;
            }

            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var open = T("Open web interface", _t.MainAccent, 0.85);
            open.Cursor = Cursors.Hand;
            open.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenWeb(); };
            DockPanel.SetDock(open, Dock.Right);
            foot.Children.Add(open);
            foot.Children.Add(T("Updated " + TimeText.Clock(s.Fetched.ToLocalTime(), true) + (s.Error.Length > 0 ? "  ·  last update failed" : ""), s.Error.Length > 0 ? Down : _t.FgDim, 0.85));
            root.Children.Add(foot);
            return root;
        }

        TextBlock Heading(string text, bool gap)
        {
            var h = T(text, _t.FgDim, 0.8, true);
            h.Margin = new Thickness(0, gap ? 12 : 0, 0, 4);
            return h;
        }

        static void OpenWeb()
        {
            string url = ProxmoxClient.WebUrl;
            if (url.Length == 0) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url + "/") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open Proxmox: " + ex.Message); }
        }

        // =================================================================
        // Guest actions
        // =================================================================
        void ShowGuestMenu(FrameworkElement target, PveResource g)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = g.Kind + " " + g.VmId + "  " + g.Name + "  (" + GuestState(g) + ")", IsEnabled = false });
            m.Items.Add(new Separator());
            bool running = g.Status == "running", paused = g.Status == "paused" || g.Status == "suspended";
            bool waiting = Waiting.ContainsKey(g.Id) || g.Lock.Length > 0;
            if (!View.AllowPower)
                m.Items.Add(new MenuItem { Header = "Start / stop is turned off in this module's settings", IsEnabled = false });
            else if (g.Template)
                m.Items.Add(new MenuItem { Header = "Templates can't be started", IsEnabled = false });
            else
            {
                if (!running && !paused) Item(m, "Start", !waiting, delegate { Act(target, g, "start", "starting…", false); });
                if (paused && g.Type == "qemu") Item(m, "Resume", !waiting, delegate { Act(target, g, "resume", "resuming…", false); });
                if (running || paused)
                {
                    Item(m, "Shut down", !waiting, delegate { Act(target, g, "shutdown", "shutting down…", true); });
                    Item(m, "Reboot", !waiting, delegate { Act(target, g, "reboot", "rebooting…", true); });
                    Item(m, "Stop (force off)", true, delegate { Act(target, g, "stop", "stopping…", true); });
                }
            }
            if (View.AllowDelete)
            {
                m.Items.Add(new Separator());
                if (running || paused) m.Items.Add(new MenuItem { Header = "Delete… (stop it first)", IsEnabled = false });
                else Item(m, "Delete…", !waiting, delegate { Delete(target, g); });
            }
            m.Items.Add(new Separator());
            // a running guest: Open shell - a container's console through its node (pct enter), SSH into a VM (its saved connection, or its
            // address from the guest agent)
            if (running && g.Type == "lxc") Item(m, "Open shell (SSH)", true, delegate { OpenShell(g.Node, "pct enter " + g.VmId); });
            else if (running && !g.Template) Item(m, "Open shell (SSH)", true, delegate { SshGuest(g); });
            Item(m, "Open web interface", true, OpenWeb);

            PopupWindow host = _popup != null && _popup.IsOpen && Window.GetWindow(target) == _popup ? _popup : null;
            if (host != null) host.Pinned = true;      // the menu must not close the popup
            _menuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                // a menu item's Click runs after Closed: let it ask its question first
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!_asking) _menuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>A node's menu: Open shell (an SSH tab in the terminal) and the web interface.</summary>
        void ShowNodeMenu(FrameworkElement target, PveResource n)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = "Node " + n.Name + (n.Running ? "" : "  (offline)"), IsEnabled = false });
            m.Items.Add(new Separator());
            Item(m, "Open shell (SSH)", n.Running, delegate { OpenShell(n.Name, null); });
            Item(m, "Open web interface", true, OpenWeb);

            PopupWindow host = _popup != null && _popup.IsOpen && Window.GetWindow(target) == _popup ? _popup : null;
            if (host != null) host.Pinned = true;      // the menu must not close the popup
            _menuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!_asking) _menuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>An SSH tab to a node (its saved connection, or one made for it); remote = a command there (pct enter 101 ...).</summary>
        void OpenShell(string node, string remote)
        {
            PveSnapshot snap = ProxmoxClient.Latest;
            PveResource n = snap == null ? null : snap.Items.Find(delegate (PveResource x) { return x.Type == "node" && x.Name == node; });
            string host = n != null && n.Ip.Length > 0 ? n.Ip : node;
            TermOpen.Host(_mgr, host, node, "root", remote);
        }

        /// <summary>
        /// An SSH tab to a guest: its saved connection (by name or address) when there is one, else its address from Proxmox (a VM's from its
        /// QEMU guest agent) - with neither, why, and an offer to save a connection for it.
        /// </summary>
        async void SshGuest(PveResource g)
        {
            string name = g.Name.Length > 0 ? g.Name : g.Kind + " " + g.VmId;
            if (TermSsh.Find(_mgr.Settings, name) != null) { TermOpen.Host(_mgr, "", name, "root", null); return; }
            KeyValuePair<string, string> a;
            try { a = await ProxmoxClient.GuestAddressAsync(g); }
            catch (Exception ex) { a = new KeyValuePair<string, string>("", ex.Message); }
            TermOpen.Host(_mgr, a.Key, name, "root", null, a.Value);
        }

        static void Item(ContextMenu m, string text, bool enabled, Action a)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }

        /// <summary>Delete a stopped guest: type its ID to confirm (purge and unattached disks ticked, as in Proxmox).</summary>
        void Delete(FrameworkElement target, PveResource g)
        {
            Window owner = Window.GetWindow(target);
            PopupWindow host = owner as PopupWindow;
            var ticks = new[] { true, true };
            string typed;
            if (host != null) host.Pinned = true;
            _asking = _menuOpen = true;
            try
            {
                typed = PromptWindow.AskWithOptions(owner, "Delete " + g.Kind + " " + g.VmId,
                    "Delete " + g.Kind + " " + g.VmId + " (" + g.Name + ") on " + g.Node + "? Type " + g.VmId + " to confirm.", "",
                    new[] { "Remove it from backup jobs, replication and HA (purge)", "Delete disks that belong to it but aren't attached" },
                    ticks, "This deletes the " + (g.Type == "lxc" ? "container" : "VM") + " and its disks for good - it can't be undone.", "Also");
            }
            finally { if (host != null) host.Pinned = false; _asking = _menuOpen = false; }
            if (typed == null) return;
            if (typed.Trim() != g.VmId.ToString())
            {
                MessageBox.Show("Nothing was deleted: the ID you typed doesn't match " + g.VmId + ".", "Proxmox", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Waiting[g.Id] = new Pending { Text = "deleting…", FromStatus = g.Status, Until = DateTime.UtcNow.AddSeconds(60) };
            _waitingVersion++;
            Refresh();
            Dispatcher disp = Dispatcher;
            bool purge = ticks[0], disks = ticks[1];
            Task.Run(async delegate
            {
                string err = await ProxmoxClient.DeleteAsync(g, purge, disks);
                _ = disp.BeginInvoke(new Action(delegate
                {
                    if (err.Length == 0) return;
                    Waiting.Remove(g.Id);
                    _waitingVersion++;
                    Refresh();
                    MessageBox.Show(err, "Proxmox", MessageBoxButton.OK, MessageBoxImage.Warning);
                }));
            });
        }

        void Act(FrameworkElement target, PveResource g, string action, string waitingText, bool ask)
        {
            Window owner = Window.GetWindow(target);
            PopupWindow host = owner as PopupWindow;
            string what = (action == "stop" ? "Force off" : action == "shutdown" ? "Shut down" : action == "reboot" ? "Reboot" : action) +
                          " " + g.Kind + " " + g.VmId + " (" + g.Name + ") on " + g.Node + "?";
            if (action == "stop") what += "\n\nThis is like pulling the plug: unsaved work inside the guest is lost.";
            if (ask)
            {
                if (host != null) host.Pinned = true;
                MessageBoxResult res;
                _asking = _menuOpen = true;
                try { res = Questions.YesNo(owner, what, "Proxmox"); }
                finally { if (host != null) host.Pinned = false; _asking = _menuOpen = false; }
                if (res != MessageBoxResult.Yes) return;
            }

            _menuOpen = false;
            Waiting[g.Id] = new Pending { Text = waitingText, FromStatus = action == "reboot" ? null : g.Status, Until = DateTime.UtcNow.AddSeconds(action == "reboot" ? 25 : 120) };
            _waitingVersion++;
            Refresh();
            Dispatcher disp = Dispatcher;
            Task.Run(async delegate
            {
                string err = await ProxmoxClient.PowerAsync(g, action);
                _ = disp.BeginInvoke(new Action(delegate
                {
                    if (err.Length == 0) return;
                    Waiting.Remove(g.Id);
                    _waitingVersion++;
                    Refresh();
                    MessageBox.Show(err, "Proxmox", MessageBoxButton.OK, MessageBoxImage.Warning);
                }));
            });
        }
    }
}
