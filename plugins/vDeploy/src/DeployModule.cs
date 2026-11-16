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

namespace VDeploy
{
    // =====================================================================================
    // The run box: !deploy -> problems and newer images; !deploy web -> stacks named like it; Enter on one -> its actions
    // =====================================================================================
    static class RunBox
    {
        public const string Kind = "vDeploy";

        sealed class State
        {
            public string Item;          // the stack whose actions are shown ("host/stack"), null = the list
            public string ItemQuery;
            public string Armed;         // an action that asks first: Enter once arms it, again does it
            public DispatcherTimer Timer;
        }

        public static RunAction Parse(string key, string q, BarSettings s)
        {
            VdSettings c = VdSettings.Of(s);
            if (!c.Configured || c.Bang.Length == 0 || key != c.Bang) return null;
            return new RunAction { Kind = Kind, Query = q, Display = q.Length == 0 ? "vDeploy: problems, newer images and automations" : "vDeploy: stacks and automations like " + q };
        }

        public static void Rows(RunPanel box, RunAction a)
        {
            State st = box.PluginState.TryGetValue("vdeploy", out object o) ? (State)o : null;
            if (st == null) { st = new State(); box.PluginState["vdeploy"] = st; }
            if (st.Timer == null)
            {
                Plugin.RefreshNow();
                st.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += delegate
                {
                    if (Plugin.Poll != null) Plugin.Poll.Want();
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != Kind || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; st.Item = null; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            VdSnapshot snap = Plugin.Latest;
            if (!snap.Checked) { box.Status("Asking vDeploy…"); return; }
            if (!snap.Ok) { box.Status("vDeploy: " + snap.Error); return; }
            string q = a.Query;
            if (st.Item != null && st.ItemQuery != q) st.Item = null;
            Theme t = box.Theme;

            if (st.Item != null && st.Item.StartsWith("g:"))
            {
                // an LXC / VM's actions (2.0.0): its update (opens its terminal in vDeploy), then start / reboot / shut down, console, open
                VdGuest g = snap.Guests.FirstOrDefault(x => "g:" + x.Key == st.Item);
                if (g == null) { st.Item = null; box.Redraw(); return; }
                var acts = new List<string[]>();
                if (g.Lxc) acts.Add(new[] { g.Update ? "Update to " + g.Latest : "Run its update", "update", "\uE896", "Updating" });
                acts.AddRange(Plugin.GuestActions(g));
                foreach (string[] act in acts)
                {
                    string[] ac = act;
                    bool ask = ac[1] is "update" or "reboot" or "shutdown";
                    string armKey = g.Key + "|" + ac[1];
                    bool armed = st.Armed == armKey;
                    RunPanel.Row r = box.AddRow(ac[2], armed ? "Press Enter again: " + ac[0].ToLowerInvariant() + " " + g.Name : ac[0], armed ? "confirm" : null, delegate
                    {
                        if (ac[1] == "open") { box.Remember(box.Text); box.Close(); Plugin.Open(Plugin.GuestUrl(g)); return; }
                        if (ac[1] == "console") { box.Remember(box.Text); box.Close(); if (Plugin.Manager != null) Plugin.ConsoleIn(Plugin.Manager, g); return; }
                        if (ask && st.Armed != armKey) { st.Armed = armKey; int keep = box.Selected; box.Redraw(); box.Select(keep); return; }
                        st.Armed = null;
                        RunGuest(box, g, ac);
                    }, null);
                    if (armed) r.View.Background = Theme.Faded(t.MainAccent, 25);
                }
                box.AddRow("\uE72B", "Back to the list", null, delegate { st.Item = null; st.Armed = null; box.Redraw(); }, null);
                box.Status(g.Name + " - " + g.KindText + " " + g.VmId + " on " + g.Node + " - " + g.StateText + (g.AppText.Length > 0 && !g.Update ? " · " + g.AppText : ""));
                box.Select(0);
                return;
            }

            if (st.Item != null)
            {
                VdStack it = snap.Stacks.FirstOrDefault(x => x.Key == st.Item);
                if (it == null) { st.Item = null; box.Redraw(); return; }
                foreach (string[] act in Plugin.Actions(it))
                {
                    string[] ac = act;
                    bool ask = ac[1] is "redeploy" or "restart" or "stop";
                    string armKey = it.Key + "|" + ac[1];
                    bool armed = st.Armed == armKey;
                    RunPanel.Row r = box.AddRow(ac[2], armed ? "Press Enter again: " + ac[0].ToLowerInvariant() + " " + it.Title : ac[0], armed ? "confirm" : null, delegate
                    {
                        if (ac[1] == "open") { box.Remember(box.Text); box.Close(); Plugin.Open(Plugin.PageUrl(it)); return; }
                        if (ask && st.Armed != armKey) { st.Armed = armKey; int keep = box.Selected; box.Redraw(); box.Select(keep); return; }
                        st.Armed = null;
                        Run(box, it, ac);
                    }, null);
                    if (armed) r.View.Background = Theme.Faded(t.MainAccent, 25);
                }
                box.AddRow("\uE72B", "Back to the list", null, delegate { st.Item = null; st.Armed = null; box.Redraw(); }, null);
                box.Status(it.Title + " on " + it.Host + " - " + it.StateText + (it.Updates.Count > 0 ? " · newer: " + string.Join(", ", it.Updates) : "") + (it.Auto ? " · updated at night" : ""));
                box.Select(0);
                return;
            }

            List<VdStack> all = snap.Stacks.ToList();
            List<VdStack> hits = q.Length == 0
                ? all.Where(s => s.Problem || s.Updates.Count > 0 || s.Deploying != null || s.Drift).ToList()
                : all.Where(s => s.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || s.Host.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            hits = hits.OrderBy(s => s.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();
            int n = 0;
            foreach (VdStack s in hits)
            {
                if (n++ >= 9) break;
                VdStack item = s;
                string tag = s.StateText + (s.Updates.Count > 0 ? " · ⬆ newer" : "") + (s.Drift ? " · changed on the host" : "");
                RunPanel.Row r = box.AddRow(s.Problem ? "\uE7BA" : s.Updates.Count > 0 ? "\uE896" : "\uE73E", s.Title + "   " + s.Host, tag, delegate
                {
                    st.Item = item.Key; st.ItemQuery = q; st.Armed = null;
                    box.Redraw();
                }, null);
                try
                {
                    var g = (TextBlock)((Grid)r.View.Child).Children[0];
                    g.Foreground = s.Problem ? StatusColors.Bad : s.Updates.Count > 0 ? (Brush)t.MainAccent : StatusColors.Good;
                }
                catch { }
            }
            // the helper scripts' LXCs / VMs (2.0.0): with no text the ones with an app update, else those named like it
            var guests = snap.Guests.Where(g => q.Length == 0 ? g.Update : g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (g.App ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || g.Node.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                    .OrderBy(g => g.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (VdGuest g0 in guests)
            {
                if (n++ >= 9) break;
                VdGuest g = g0;
                RunPanel.Row r = box.AddRow(g.Update ? "\uE896" : g.Running ? "\uE73E" : "\uE71A", g.Name + "   " + g.Node, g.KindText + " " + g.VmId + " · " + g.StateText, delegate
                {
                    st.Item = "g:" + g.Key; st.ItemQuery = q; st.Armed = null;
                    box.Redraw();
                }, null);
                try
                {
                    var gl = (TextBlock)((Grid)r.View.Child).Children[0];
                    gl.Foreground = g.Update ? (Brush)t.MainAccent : g.Running ? StatusColors.Good : (Brush)t.FgDim;
                }
                catch { }
            }
            bool anyGuests = guests.Count > 0;

            // automations: every one with no text, else those named like it - Enter twice runs one
            var autos = snap.Automations.Where(x => q.Length == 0 || x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(Math.Max(0, 9 - Math.Min(n, 9))).ToList();
            foreach (VdAutomation x in autos)
            {
                VdAutomation au = x;
                string armKey = "auto|" + au.Id;
                bool armed = st.Armed == armKey;
                RunPanel.Row r = box.AddRow("\uE945", armed ? "Press Enter again: run " + au.Name : "Run " + au.Name, armed ? "confirm" : "automation · " + au.StateText, delegate
                {
                    if (au.Running) { box.Status(au.Name + " is running already."); return; }
                    if (st.Armed != armKey) { st.Armed = armKey; int keep = box.Selected; box.Redraw(); box.Select(keep); return; }
                    st.Armed = null;
                    RunAuto(box, au);
                }, null);
                if (armed) r.View.Background = Theme.Faded(t.MainAccent, 25);
            }
            if (hits.Count == 0 && autos.Count == 0 && !anyGuests) box.Status(q.Length > 0 ? "No stack, LXC / VM or automation in vDeploy is called anything like " + q + "." : "Nothing needs you - " + Plugin.Summary(snap) + ".");
            else if (hits.Count == 0 && !anyGuests) box.Status("Enter twice: run it  ·  " + Plugin.Summary(snap));
            else box.Status("Enter: its actions" + (hits.Count > 9 ? "  ·  " + (hits.Count - 9) + " more - type more of the name" : "") + "  ·  " + Plugin.Summary(snap));
            box.Select(0);
        }

        static async void RunGuest(RunPanel box, VdGuest g, string[] act)
        {
            box.Status(act[3] + " " + g.Name + "…");
            string err = act[1] == "update" ? await Plugin.GuestUpdateAsync(g) : await Plugin.GuestActAsync(g, act[1]);
            if (err != null) { box.Status("vDeploy: " + err); return; }
            box.Remember(box.Text);
            box.Status(act[1] == "update" ? "Updating " + (g.App ?? g.Name) + " ✓ - its terminal is open in your browser" : act[3] + " " + g.Name + " ✓");
        }

        static async void RunAuto(RunPanel box, VdAutomation a)
        {
            box.Status("Starting " + a.Name + "…");
            string err = await Plugin.RunAutomationAsync(a);
            if (err != null) { box.Status("vDeploy: " + err); return; }
            box.Remember(box.Text);
            box.Status("vDeploy is running " + a.Name + " ✓ - its page in vDeploy shows each step");
        }

        static async void Run(RunPanel box, VdStack s, string[] act)
        {
            box.Status(act[3] + " " + s.Title + "…");
            string err = await Plugin.ActAsync(s, act[1]);
            if (err != null) { box.Status("vDeploy: " + err); return; }
            box.Remember(box.Text);
            box.Status("vDeploy is on it: " + act[0].ToLowerInvariant() + " " + s.Title + " ✓" + (act[1] == "redeploy" ? " - the health check follows; a notification says how it went if you chose that in vDeploy" : ""));
        }
    }

    // =====================================================================================
    // The module: on a bar a dot, running / total, ⚠ problems, ⬆ newer images; click -> the stacks; in a widget the stacks
    // =====================================================================================
    public sealed class DeployModule : Border, IMultiRow
    {
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
        bool ShowCount { get { return B.Get("ShowCount", true); } }
        /// <summary>vDeploy's automations under the stacks, each with Run (as the Komodo plugin's procedures).</summary>
        bool ShowAutomations { get { return B.Get("Automations", true); } }
        /// <summary>vDeploy's Proxmox nodes and their helper-script LXCs / VMs under the stacks (2.0.0, vDeploy 0.1.25) - V: an option to hide them.</summary>
        bool ShowNodes { get { return B.Get("Nodes", true); } }
        int AppUpdates(VdSnapshot s) { return ShowNodes ? s.AppUpdates : 0; }
        int NodesDown(VdSnapshot s) { return ShowNodes ? s.Nodes.Count(n => n.Seen && !n.Online) : 0; }

        public DeployModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            VdSnapshot s = Plugin.Latest;
            string sig = Sig(s) + "|" + string.Join(",", _busy);
            if (_pop != null && _pop.IsOpen && sig != _popShown) { _popShown = sig; _pop.SetContent(List(s, true)); }
            if (sig == _shown) return;
            _shown = sig;
            Child = _host.IsWidget ? List(s, false) : BarView(s);
            _host.ContentChanged();
        }

        string Sig(VdSnapshot s)
        {
            var b = new StringBuilder(OnlyNeeding + "|" + GroupHosts + "|" + Count + "|" + ShowCount + "|" + ShowAutomations + "|" + ShowNodes + "|" + _cfg.Title + "|" + s.Ok + s.Checked + s.Error + s.Updates + "|" + s.AppUpdates);
            if (ShowNodes)
                foreach (VdNode n in s.Nodes)
                {
                    b.Append("|n").Append(n.Id).Append(n.Online).Append(n.Seen);
                    foreach (VdGuest g in n.Guests) b.Append(",").Append(g.VmId).Append(g.Name).Append(g.Status).Append(g.AppState).Append(g.Latest);
                }
            if (ShowAutomations)
                foreach (VdAutomation a in s.Automations) b.Append("|a").Append(a.Id).Append(a.Name).Append(a.Running).Append(a.HasRun).Append(a.LastOk).Append(a.LastUtc.Ticks).Append(a.Schedule);
            foreach (VdHost h in s.Hosts)
            {
                b.Append('|').Append(h.Id).Append(h.Online);
                foreach (VdStack k in h.Stacks)
                {
                    b.Append(',').Append(k.Name).Append(k.Look).Append(k.Running).Append(k.Total).Append(k.Updates.Count).Append(k.Deploying).Append(k.Drift);
                    if (Unfolded.Contains(k.Key))     // its containers show: their states and use too
                        foreach (VdContainer c in k.Containers) b.Append(';').Append(c.Label).Append(c.State).Append(c.Status).Append(c.Cpu == null ? "" : Math.Round(c.Cpu.Value)).Append(c.Memory >> 22);
                }
            }
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

        Brush DotOf(VdStack s)
        {
            if (s.Deploying != null) return _t.MainAccent;
            if (!s.HostOnline) return StatusColors.Bad;
            return s.Look == "ok" ? StatusColors.Good : s.Look == "warn" ? StatusColors.Warn : (Brush)_t.FgDim;
        }

        Ellipse Dot(Brush b, double size = 8) { return new Ellipse { Width = size, Height = size, Fill = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; }

        // ---------- on a bar ----------
        FrameworkElement BarView(VdSnapshot s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Brush dot = !s.Checked ? _t.FgDim : !s.Ok ? StatusColors.Bad : s.Problems + NodesDown(s) > 0 ? StatusColors.Bad : s.Stacks.Any(x => x.Deploying != null) ? _t.MainAccent : s.Running < s.Total ? StatusColors.Warn : StatusColors.Good;
            row.Children.Add(Dot(dot));
            string title = (_cfg.Title ?? "").Trim();
            if (title.Length > 0) row.Children.Add(T(title + " ", _t.FgDim, _t.FontSize));
            if (!s.Checked) row.Children.Add(T("vDeploy …", _t.Fg, _t.FontSize));
            else if (!s.Ok) row.Children.Add(T("vDeploy: can't reach it", StatusColors.Bad, _t.FontSize));
            else
            {
                if (ShowCount) row.Children.Add(T(s.Running + "/" + s.Total + " running", _t.Fg, _t.FontSize));
                int problems = s.Problems + NodesDown(s), ups = s.Updates + AppUpdates(s);
                if (problems > 0) { var p = T("  ⚠ " + problems, StatusColors.Bad, _t.FontSize); row.Children.Add(p); }
                if (ups > 0) row.Children.Add(T("  ⬆ " + ups, _t.MainAccent, _t.FontSize));
                if (!ShowCount && problems == 0 && ups == 0) row.Children.Add(T("vDeploy", _t.Fg, _t.FontSize));
            }
            ToolTip = !s.Ok ? "vDeploy: " + (s.Checked ? s.Error : "looking…") : "vDeploy: " + Plugin.Summary(s) + "\nClick for the stacks" + (ShowNodes && s.Nodes.Count > 0 ? " and the Proxmox nodes" : "");
            return row;
        }

        // ---------- the list: a widget, and the bar's popup ----------
        FrameworkElement List(VdSnapshot s, bool popup)
        {
            double fs = _host.IsWidget && _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            var root = new StackPanel { Width = popup ? 340 : double.NaN, Margin = popup ? new Thickness(14, 12, 14, 12) : new Thickness(0) };
            if (popup)
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var sum = T(s.Ok ? Plugin.Summary(s) : "", _t.FgDim, fs - 1);
                DockPanel.SetDock(sum, Dock.Right);
                head.Children.Add(sum);
                head.Children.Add(T("vDeploy", _t.Fg, fs + 1, true));
                root.Children.Add(head);
            }
            if (!s.Checked) root.Children.Add(T(VdSettings.Of(_mgr.Settings).Configured ? "Asking vDeploy…" : "Set up vDeploy in Settings > Plugins > vDeploy", _t.FgDim, fs));
            else if (!s.Ok) { var e = T(s.Error, StatusColors.Bad, fs - 1); e.TextWrapping = TextWrapping.Wrap; root.Children.Add(e); }
            else
            {
                int shown = 0, left = 0;
                bool both = ShowNodes && s.Nodes.Count > 0 && s.Hosts.Count > 0;
                if (both) root.Children.Add(Section("Docker Hosts", fs, true));
                foreach (VdHost h in s.Hosts)
                {
                    var stacks = h.Stacks.Where(k => !k.Other && (!OnlyNeeding || k.Problem || k.Updates.Count > 0 || k.Deploying != null)).ToList();
                    if (GroupHosts || popup)
                    {
                        if (stacks.Count == 0 && h.Online && OnlyNeeding) continue;
                        root.Children.Add(HostRow(h, fs, shown == 0 && root.Children.Count <= (popup ? 1 : 0)));
                    }
                    foreach (VdStack k in stacks)
                    {
                        if (shown >= Count && !popup) { left++; continue; }
                        shown++;
                        root.Children.Add(StackRow(k, fs, popup));
                    }
                }
                if (shown == 0 && !(OnlyNeeding && ShowNodes && s.Nodes.Count > 0)) root.Children.Add(T(OnlyNeeding ? "Nothing needs you - " + Plugin.Summary(s) : "No stacks yet", _t.FgDim, fs - 1));

                // the Proxmox nodes and the helper scripts' LXCs / VMs on them (vDeploy 0.1.25)
                if (ShowNodes && s.Nodes.Count > 0)
                {
                    int before = root.Children.Count;
                    if (both) root.Children.Add(Section("Proxmox Nodes", fs, false));
                    int head = root.Children.Count;
                    foreach (VdNode n in s.Nodes)
                    {
                        var guests = n.Guests.Where(g => !OnlyNeeding || g.Update || !g.NodeOnline).ToList();
                        if (guests.Count == 0 && OnlyNeeding && (n.Online || !n.Seen)) continue;
                        if (GroupHosts || popup) root.Children.Add(NodeRow(n, fs, root.Children.Count == 0 || (popup && root.Children.Count <= 1)));
                        foreach (VdGuest g in guests)
                        {
                            if (shown >= Count && !popup) { left++; continue; }
                            shown++;
                            root.Children.Add(GuestRow(g, fs, popup));
                        }
                    }
                    if (root.Children.Count == head && OnlyNeeding)
                    {
                        if (both) root.Children.RemoveRange(before, root.Children.Count - before);
                        if (shown == 0) root.Children.Add(T("Nothing needs you - " + Plugin.Summary(s), _t.FgDim, fs - 1));
                    }
                }
                if (left > 0) root.Children.Add(T("and " + left + " more", _t.FgDim, fs - 1.5));

                // automations, each with Run - only the ones that failed last time when it shows only what needs you
                if (ShowAutomations)
                {
                    var autos = s.Automations.Where(a => !OnlyNeeding || (a.HasRun && !a.LastOk && !a.Running)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    if (autos.Count > 0)
                    {
                        var ah = T("Automations", _t.FgDim, fs - 1, true);
                        ah.Margin = new Thickness(0, 10, 0, 3);
                        root.Children.Add(ah);
                        foreach (VdAutomation a in autos) root.Children.Add(AutomationRow(a, fs));
                    }
                }
            }
            if (popup)
            {
                var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                foreach (Border b in new[]
                {
                    Pills.Make(_t, "\uE8A7", "Open vDeploy", "Open vDeploy in your browser", false, delegate { Close(); Plugin.Open(Plugin.HomeUrl()); }, false),
                    Pills.Make(_t, "\uE896", "Updates", "vDeploy's Updates page", false, delegate { Close(); Plugin.Open(Plugin.HomeUrl("/updates")); }, false),
                    Pills.Make(_t, "\uE72C", "Refresh", "Look again now", false, delegate { Plugin.RefreshNow(); }, false),
                })
                {
                    b.Margin = new Thickness(0, 0, 6, 0);
                    buttons.Children.Add(b);
                }
                root.Children.Add(buttons);
                if (s.Ok) root.Children.Add(T("Checked " + TimeText.Ago(s.AtUtc), _t.FgDim, fs - 2));
                ((FrameworkElement)root.Children[root.Children.Count - 1]).Margin = new Thickness(0, 6, 0, 0);
                var scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                if (_pop != null) scroll.MaxHeight = _pop.ContentRoom();
                WidgetWindow.StyleScrollbars(scroll);
                return scroll;
            }
            return root;
        }

        /// <summary>"Docker Hosts" / "Proxmox Nodes" over their parts, as vDeploy's side panel (only when both show).</summary>
        FrameworkElement Section(string text, double fs, bool first)
        {
            var h = T(text.ToUpperInvariant(), _t.FgDim, fs - 2.5, true);
            h.Margin = new Thickness(0, first ? 0 : 12, 0, 2);
            return h;
        }

        /// <summary>A node, as vDeploy's side panel: its server icon (accent), dot, name, how many LXCs / VMs ("offline" when it isn't answering).</summary>
        FrameworkElement NodeRow(VdNode n, double fs, bool first)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, first ? 0 : 8, 0, 3), Background = Brushes.Transparent };
            bool offline = n.Seen && !n.Online;
            bool busy = _busy.Contains("node|" + n.Id);
            var count = T(busy ? "asking…" : offline ? "offline" : n.Guests.Count.ToString(), offline ? StatusColors.Bad : _t.FgDim, fs - 1.5);
            count.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(count, Dock.Right);
            line.Children.Add(count);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            FrameworkElement icon = VdIcons.Make("server", Px(fs, 16), _t.MainAccent);
            icon.Margin = new Thickness(0, 0, 7, 0);
            name.Children.Add(icon);
            name.Children.Add(Dot(!n.Seen ? _t.FgDim : n.Online ? StatusColors.Good : (Brush)StatusColors.Bad, 7));
            name.Children.Add(T(n.Name, _t.Fg, fs + 0.5, true));
            line.Children.Add(name);
            line.ToolTip = n.Name + " - Proxmox VE " + n.Version + (offline ? " - not answering: " + n.Problem : "") +
                           (n.Others > 0 ? "\n" + n.Others + " other LXCs / VMs aren't shown (only the helper scripts')" : "") +
                           "\nClick: open it in vDeploy · right-click: check for updates, a shell on it, add a LXC / VM";
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Close(); Plugin.Open(Plugin.NodeUrl(n)); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(line, NodeMenu(n)); };
            return line;
        }

        /// <summary>An LXC / VM under its node: its icon, dot, name (⬆ an app update), its state or the update; Update in the popup.</summary>
        FrameworkElement GuestRow(VdGuest g, double fs, bool popup)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            bool busy = _busy.Contains(g.Key);
            if (popup && g.Update && !busy && g.NodeOnline)
            {
                var pill = Pills.Make(_t, "\uE896", "Update", (g.App ?? g.Name) + " " + g.Installed + " → " + g.Latest + " - a snapshot first; its terminal opens in vDeploy", false, delegate { UpdateGuest(g); }, false);
                pill.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(pill, Dock.Right);
                line.Children.Add(pill);
            }
            string text = busy ? "asking…" : g.Update ? g.Installed + " → " + g.Latest : !g.NodeOnline ? "" : g.Running ? (g.Cpu > 0 ? Math.Round(g.Cpu * 100) + "% · " : "") + Mb(g.MemUsed) : "stopped";
            var state = T(text, g.Update && !busy ? _t.MainAccent : _t.FgDim, fs - 1.5);
            state.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(state, Dock.Right);
            line.Children.Add(state);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new Border { Width = Px(fs, 18) });       // under the node's name, as a stack under its host
            FrameworkElement icon = VdIcons.Make(g.Lxc ? "box" : "vm", Px(fs, 13), _t.Fg, 0.75);
            icon.Margin = new Thickness(0, 0, 7, 0);
            name.Children.Add(icon);
            name.Children.Add(Dot(!g.NodeOnline ? _t.FgDim : g.Running ? StatusColors.Good : (Brush)_t.FgDim, 7));
            name.Children.Add(T(g.Name, _t.Fg, fs));
            if (g.Update) name.Children.Add(T(" ⬆", _t.MainAccent, fs - 1));
            line.Children.Add(name);
            line.ToolTip = g.KindText + " " + g.VmId + " " + g.Name + " on " + g.Node + " - " + (g.Running ? "running" : "stopped") +
                           (g.AppText.Length > 0 ? "\n" + g.AppText : "") + (g.Tags.Count > 0 ? "\n" + string.Join(", ", g.Tags) : "") +
                           "\nClick: open it in vDeploy · right-click: " + (g.Lxc ? "update, " : "") + (g.Running ? "reboot, shut down" + (g.Lxc ? ", a console in vTerm" : "") : "start");
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Close(); Plugin.Open(Plugin.GuestUrl(g)); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(line, GuestMenu(g)); };
            return line;
        }

        ContextMenu NodeMenu(VdNode n)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = n.Name + " - " + (n.Seen && !n.Online ? "not answering" : n.Guests.Count + " LXCs / VMs"), IsEnabled = false });
            m.Items.Add(new Separator());
            m.Items.Add(Item("Check for updates", delegate { CheckNode(n); }, n.Online, n.Online ? "Looks at it again and asks each LXC about its app" : "It isn't answering"));
            m.Items.Add(Item("Open shell on " + n.Name, delegate { Plugin.ShellOnNode(_mgr, n); }));
            m.Items.Add(Item("Add a LXC / VM…", delegate { Close(); Plugin.Open(Plugin.HomeUrl("/nodes/" + Uri.EscapeDataString(n.Id) + "/add")); }, true, "vDeploy's page: pick a helper script, its settings, run it"));
            m.Items.Add(Item("Open in vDeploy", delegate { Close(); Plugin.Open(Plugin.NodeUrl(n)); }));
            return m;
        }

        ContextMenu GuestMenu(VdGuest g)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = g.Name + "  (" + g.KindText + " " + g.VmId + " on " + g.Node + ") - " + g.StateText, IsEnabled = false });
            m.Items.Add(new Separator());
            bool up = g.NodeOnline;
            if (g.Lxc)
                m.Items.Add(Item(g.Update ? "Update to " + g.Latest + "…" : "Run its update…", delegate { UpdateGuest(g); }, up,
                    "A snapshot first, its cores and memory raised if its script asks for more - its terminal opens in vDeploy"));
            if (g.Running)
            {
                m.Items.Add(Item("Reboot…", delegate { if (Questions.YesNo(null, "Reboot " + g.Name + " (" + g.KindText + " " + g.VmId + " on " + g.Node + ")?", Plugin.Source) == MessageBoxResult.Yes) GuestAct(g, "reboot"); }, up));
                m.Items.Add(Item("Shut down…", delegate { if (Questions.YesNo(null, "Shut down " + g.Name + " (" + g.KindText + " " + g.VmId + " on " + g.Node + ")? A clean shutdown - it's turned off if it hasn't stopped after 2 minutes.", Plugin.Source) == MessageBoxResult.Yes) GuestAct(g, "shutdown"); }, up));
            }
            else m.Items.Add(Item("Start", delegate { GuestAct(g, "start"); }, up));
            m.Items.Add(new Separator());
            if (g.Lxc) m.Items.Add(Item("Open console in the LXC", delegate { Plugin.ConsoleIn(_mgr, g); }, g.Running && up, g.Running ? "pct enter " + g.VmId + " over SSH to " + g.Node + ", in vTerm" : "It isn't running"));
            VdNode n = Plugin.NodeOf(g);
            if (n != null) m.Items.Add(Item("Open shell on " + g.Node, delegate { Plugin.ShellOnNode(_mgr, n); }));
            m.Items.Add(Item("Open in vDeploy", delegate { Close(); Plugin.Open(Plugin.GuestUrl(g)); }));
            return m;
        }

        async void UpdateGuest(VdGuest g)
        {
            string what = (g.App ?? g.Name) + (g.Update ? " " + g.Installed + " → " + g.Latest : "");
            if (Questions.YesNo(null, "Update " + what + " (LXC " + g.VmId + " on " + g.Node + ")?\n\nA snapshot first, its cores and memory raised if its script asks for more (put back after). Its terminal opens in vDeploy to watch it.", Plugin.Source) != MessageBoxResult.Yes) return;
            _busy.Add(g.Key);
            _shown = _popShown = "";
            Refresh();
            Close();
            string err = await Plugin.GuestUpdateAsync(g);
            _busy.Remove(g.Key);
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, "vDeploy couldn't update " + g.Name + ": " + err, Plugin.Source);
        }

        async void GuestAct(VdGuest g, string action)
        {
            _busy.Add(g.Key);
            _shown = _popShown = "";
            Refresh();
            string err = await Plugin.GuestActAsync(g, action);
            _busy.Remove(g.Key);
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, err, Plugin.Source);
        }

        async void CheckNode(VdNode n)
        {
            string key = "node|" + n.Id;
            _busy.Add(key);
            _shown = _popShown = "";
            Refresh();
            string err = await Plugin.CheckNodeAsync(n);
            await System.Threading.Tasks.Task.Delay(3000);
            _busy.Remove(key);
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, "vDeploy couldn't check " + n.Name + ": " + err, Plugin.Source);
        }

        /// <summary>Stacks whose containers are shown (every popup and widget of the plugin, until vShell closes).</summary>
        static readonly HashSet<string> Unfolded = new HashSet<string>();

        /// <summary>vDeploy's icons are drawn for its 13 px side panel: the same proportions at this font size.</summary>
        static double Px(double fs, double px) { return System.Math.Round(px * fs / 13.0, 1); }

        /// <summary>A host's dot, as vDeploy's side panel: grey before it's been seen, red not answering, amber a stack not healthy, else green.</summary>
        Brush HostDot(VdHost h)
        {
            if (!h.Seen) return _t.FgDim;
            if (!h.Online) return StatusColors.Bad;
            return h.Stacks.Any(k => k.Look == "warn") ? StatusColors.Warn : (Brush)StatusColors.Good;
        }

        /// <summary>A host, as in vDeploy's side panel: its server icon (accent), dot, name, and its stacks' count ("offline" when it isn't answering).</summary>
        FrameworkElement HostRow(VdHost h, double fs, bool first)
        {
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, first ? 0 : 8, 0, 3) };
            bool offline = h.Seen && !h.Online;
            var count = T(offline ? "offline" : h.Stacks.Count(k => !k.Other).ToString(), offline ? StatusColors.Bad : _t.FgDim, fs - 1.5);
            count.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(count, Dock.Right);
            line.Children.Add(count);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            FrameworkElement icon = VdIcons.Make("host", Px(fs, 16), _t.MainAccent);
            icon.Margin = new Thickness(0, 0, 7, 0);
            name.Children.Add(icon);
            name.Children.Add(Dot(HostDot(h), 7));
            name.Children.Add(T(h.Name, _t.Fg, fs + 0.5, true));
            line.Children.Add(name);
            if (offline) line.ToolTip = h.Name + " isn't answering";
            return line;
        }

        FrameworkElement StackRow(VdStack k, double fs, bool popup)
        {
            bool open = Unfolded.Contains(k.Key) && k.Containers.Count > 0;
            var block = new StackPanel();
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            bool busy = _busy.Contains(k.Key) || k.Deploying != null;
            if (popup && k.Updates.Count > 0 && !busy)
            {
                var pill = Pills.Make(_t, "\uE896", "Update", "Pull the newest images and start them, with the health check: " + string.Join(", ", k.Updates), false, delegate { Act(k, "redeploy"); }, false);
                pill.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(pill, Dock.Right);
                line.Children.Add(pill);
            }
            var state = T(busy ? (k.Deploying != null ? k.StateText : "asking…") : k.Running + "/" + k.Total, k.Problem ? StatusColors.Bad : _t.FgDim, fs - 1.5);
            state.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(state, Dock.Right);
            line.Children.Add(state);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            // the arrow: its containers under it (vDeploy's side panel chevron), then the stack icon - as vDeploy's side panel
            var chev = new Border
            {
                Width = Px(fs, 14), Height = Px(fs, 18), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
                Child = k.Containers.Count == 0 ? null : VdIcons.Make(open ? "chev-down" : "chev-right", Px(fs, 10), _t.FgDim),
                Cursor = k.Containers.Count == 0 ? null : Cursors.Hand,
                ToolTip = k.Containers.Count == 0 ? null : open ? "Hide the containers" : "Show the containers"
            };
            if (k.Containers.Count > 0)
            {
                chev.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                chev.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Fold(k); };
            }
            name.Children.Add(chev);
            FrameworkElement stackIcon = VdIcons.Make("stack", Px(fs, 13), _t.Fg, 0.75);
            stackIcon.Margin = new Thickness(0, 0, 7, 0);
            name.Children.Add(stackIcon);
            name.Children.Add(Dot(DotOf(k), 7));
            name.Children.Add(T(k.Title, _t.Fg, fs));
            if (k.Updates.Count > 0) name.Children.Add(T(" ⬆", _t.MainAccent, fs - 1));
            if (k.Drift) name.Children.Add(T(" ✎", StatusColors.Warn, fs - 1));
            line.Children.Add(name);
            line.ToolTip = k.Title + " on " + k.Host + " - " + k.StateText +
                           (k.Updates.Count > 0 ? "\nNewer images: " + string.Join(", ", k.Updates) : "") +
                           (k.Drift ? "\nIts files were changed on the host outside vDeploy" : "") +
                           (k.Auto ? "\nUpdated by itself at night" : "") +
                           "\nClick: open it in vDeploy · the arrow: its containers · right-click: update, restart, stop, its log, a shell on " + k.Host;
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Close(); Plugin.Open(Plugin.PageUrl(k)); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(line, StackMenu(k)); };
            block.Children.Add(line);
            if (open)
                foreach (VdContainer c in k.Containers) block.Children.Add(ContainerRow(k, c, fs));
            return block;
        }

        /// <summary>An automation: its dot (accent running, red last run failed, green worked, grey never run), name, when / schedule, and Run.</summary>
        FrameworkElement AutomationRow(VdAutomation a, double fs)
        {
            string key = "auto|" + a.Id;
            bool busy = _busy.Contains(key) || a.Running;
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            if (!busy)
            {
                var run = Pills.Make(_t, "", "Run", "Run " + a.Name + " now (" + a.Steps + (a.Steps == 1 ? " step)" : " steps)"), false, delegate { RunAuto(a); }, false);
                run.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(run, Dock.Right);
                line.Children.Add(run);
            }
            string when = busy ? "running…" : a.HasRun ? (a.LastOk ? "" : "failed · ") + TimeText.Ago(a.LastUtc) : a.Schedule ?? "never run";
            var state = T(when, a.HasRun && !a.LastOk && !busy ? StatusColors.Bad : _t.FgDim, fs - 1.5);
            state.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(state, Dock.Right);
            line.Children.Add(state);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new TextBlock { Text = "", Width = 14 });
            name.Children.Add(Dot(busy ? _t.MainAccent : !a.HasRun ? _t.FgDim : a.LastOk ? StatusColors.Good : (Brush)StatusColors.Bad, 7));
            name.Children.Add(T(a.Name, _t.Fg, fs));
            line.Children.Add(name);
            line.ToolTip = a.Name + " - " + a.StateText + "\n" + a.Steps + (a.Steps == 1 ? " step" : " steps") + (a.Schedule != null ? " · " + a.Schedule : " · run by hand") +
                           "\nClick: open it in vDeploy · Run: run it now";
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Close(); Plugin.Open(Plugin.HomeUrl("/automations/" + Uri.EscapeDataString(a.Id))); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var m = new ContextMenu();
                m.Items.Add(new MenuItem { Header = a.Name + " - " + a.StateText, IsEnabled = false });
                m.Items.Add(new Separator());
                m.Items.Add(Item("Run now", delegate { RunAuto(a); }, !busy, busy ? "It's running" : null));
                m.Items.Add(Item("Open in vDeploy", delegate { Close(); Plugin.Open(Plugin.HomeUrl("/automations/" + Uri.EscapeDataString(a.Id))); }));
                ShowMenu(line, m);
            };
            return line;
        }

        async void RunAuto(VdAutomation a)
        {
            if (Questions.YesNo(null, "Run " + a.Name + " now? (" + a.Steps + (a.Steps == 1 ? " step)" : " steps)"), Plugin.Source) != MessageBoxResult.Yes) return;
            string key = "auto|" + a.Id;
            _busy.Add(key);
            _shown = _popShown = "";
            Refresh();
            string err = await Plugin.RunAutomationAsync(a);
            _busy.Remove(key);
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, "vDeploy couldn't run " + a.Name + ": " + err, Plugin.Source);
        }

        void Fold(VdStack k)
        {
            if (!Unfolded.Remove(k.Key)) Unfolded.Add(k.Key);
            _shown = _popShown = "";
            Refresh();
        }

        /// <summary>A container under its stack: its state dot, service, state, CPU and memory. Click: its log; right-click: everything.</summary>
        FrameworkElement ContainerRow(VdStack k, VdContainer c, double fs)
        {
            // indented past the stack's icon, as vDeploy's side panel
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(Px(fs, 32), 1, 0, 1), Background = Brushes.Transparent };
            bool busy = _busy.Contains(k.Key + "|" + c.Label);
            string use = c.Running && c.Cpu != null ? Math.Round(c.Cpu.Value, 1) + "%" + (c.Memory != null ? " · " + Mb(c.Memory.Value) : "") : c.StateText;
            var state = T(busy ? "asking…" : use, c.Look == "warn" ? StatusColors.Warn : _t.FgDim, fs - 2);
            state.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(state, Dock.Right);
            line.Children.Add(state);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            FrameworkElement icon = VdIcons.Make("box", Px(fs, 11), _t.Fg, 0.5);
            icon.Margin = new Thickness(0, 0, 7, 0);
            name.Children.Add(icon);
            name.Children.Add(Dot(c.Look == "ok" ? StatusColors.Good : c.Look == "warn" ? StatusColors.Warn : (Brush)_t.FgDim, 7));
            name.Children.Add(T(c.Label, _t.FgDim, fs - 1));
            bool newer = HasNewer(k, c);
            if (newer) name.Children.Add(T(" ⬆", _t.MainAccent, fs - 2));
            line.Children.Add(name);
            line.ToolTip = c.Label + " (" + k.Title + " on " + k.Host + ") - " + c.StateText + (c.Status.Length > 0 ? " · " + c.Status : "") + "\n" + c.Image +
                           (newer ? "\nA newer image is out - Update on its stack pulls it" : "") +
                           "\nClick: its log · right-click: restart, stop, a shell in it, its log";
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            line.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; LogWindow.Show(Theme.From(_mgr.Settings, 12.5, 8), k, c); };
            line.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(line, ContainerMenu(k, c)); };
            return line;
        }

        /// <summary>The container runs one of the images vDeploy found newer for its stack.</summary>
        static bool HasNewer(VdStack k, VdContainer c)
        {
            if (k.Updates.Count == 0 || c.Image.Length == 0) return false;
            string mine = ImageKey(c.Image);
            foreach (string u in k.Updates) if (ImageKey(u) == mine) return true;
            return false;
        }

        /// <summary>
        /// An image as one form: "jellyfin/jellyfin", "docker.io/jellyfin/jellyfin:latest" and "index.docker.io/…" are the same; "nginx" =
        /// "docker.io/library/nginx:latest". A digest (@sha256:…) is left off.
        /// </summary>
        static string ImageKey(string image)
        {
            string s = image.Trim().ToLowerInvariant();
            int at = s.IndexOf('@');
            if (at >= 0) s = s.Substring(0, at);
            int slash = s.LastIndexOf('/'), colon = s.LastIndexOf(':');
            if (colon <= slash) s += ":latest";                 // no tag (a colon before the last / is a registry's port)
            foreach (string reg in new[] { "docker.io/", "index.docker.io/", "registry-1.docker.io/" })
                if (s.StartsWith(reg)) { s = s.Substring(reg.Length); break; }
            if (s.StartsWith("library/")) s = s.Substring(8);
            return s;
        }

        static string Mb(long bytes) { return bytes >= 1L << 30 ? Math.Round(bytes / (double)(1L << 30), 1) + " GB" : Math.Round(bytes / (double)(1L << 20)) + " MB"; }

        static MenuItem Item(string text, Action click, bool enabled = true, string tip = null)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled, ToolTip = tip };
            mi.Click += delegate { click(); };
            return mi;
        }

        /// <summary>A stack's menu: update / redeploy, restart, stop / start, its containers, its log, a shell on its host, open it.</summary>
        ContextMenu StackMenu(VdStack k)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = k.Title + "  (" + k.Host + ") - " + k.StateText, IsEnabled = false });
            m.Items.Add(new Separator());
            foreach (string[] act in Plugin.Actions(k))
            {
                string[] a = act;
                if (a[1] == "open") continue;
                m.Items.Add(Item(a[0] + (a[1] is "redeploy" or "restart" or "stop" ? "…" : ""), delegate
                {
                    if (a[1] is "redeploy" or "restart" or "stop" && Questions.YesNo(null, a[0] + " " + k.Title + " on " + k.Host + "?", Plugin.Source) != MessageBoxResult.Yes) return;
                    Act(k, a[1]);
                }));
            }
            if (k.Containers.Count > 0)
            {
                m.Items.Add(new Separator());
                m.Items.Add(Item(Unfolded.Contains(k.Key) ? "Hide the containers" : "Show the containers", delegate { Fold(k); }));
            }
            m.Items.Add(new Separator());
            if (!k.Other) m.Items.Add(Item("Show its log (every service)", delegate { LogWindow.Show(Theme.From(_mgr.Settings, 12.5, 8), k, null); }));
            m.Items.Add(Item("Open shell on " + k.Host, delegate { Plugin.ShellOnHost(_mgr, k); }));
            m.Items.Add(Item("Open in vDeploy", delegate { Close(); Plugin.Open(Plugin.PageUrl(k)); }));
            return m;
        }

        /// <summary>A container's menu: start / restart / stop, a shell in it (vTerm), a shell on its host, its log, open it in vDeploy.</summary>
        ContextMenu ContainerMenu(VdStack k, VdContainer c)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = c.Label + "  (" + k.Title + ") - " + c.StateText, IsEnabled = false });
            m.Items.Add(new Separator());
            if (c.Running)
            {
                m.Items.Add(Item("Restart…", delegate { if (Questions.YesNo(null, "Restart " + c.Label + " (" + k.Title + " on " + k.Host + ")?", Plugin.Source) == MessageBoxResult.Yes) BoxAct(k, c, "restart"); }));
                m.Items.Add(Item("Stop…", delegate { if (Questions.YesNo(null, "Stop " + c.Label + " (" + k.Title + " on " + k.Host + ")?", Plugin.Source) == MessageBoxResult.Yes) BoxAct(k, c, "stop"); }));
            }
            else m.Items.Add(Item("Start", delegate { BoxAct(k, c, "start"); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Open shell in the container", delegate { Plugin.ShellIn(_mgr, k, c); }, c.Running, c.Running ? "docker exec over SSH to " + k.Host + ", in vTerm" : "It isn't running"));
            m.Items.Add(Item("Open shell on " + k.Host, delegate { Plugin.ShellOnHost(_mgr, k); }));
            m.Items.Add(Item("Show its log", delegate { LogWindow.Show(Theme.From(_mgr.Settings, 12.5, 8), k, c); }));
            m.Items.Add(Item("Open in vDeploy", delegate { Close(); Plugin.Open(Plugin.ContainerUrl(k, c)); }));
            return m;
        }

        /// <summary>A menu at the pointer; the popup stays open while it's up (as the Komodo plugin's).</summary>
        void ShowMenu(FrameworkElement target, ContextMenu m)
        {
            PopupWindow host = Window.GetWindow(target) as PopupWindow;
            if (host != null) host.Pinned = true;
            m.Closed += delegate { if (host != null) host.Pinned = false; };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        async void BoxAct(VdStack k, VdContainer c, string action)
        {
            string key = k.Key + "|" + c.Label;
            _busy.Add(key);
            _shown = _popShown = "";
            Refresh();
            string err = await Plugin.ContainerActAsync(k, c, action);
            _busy.Remove(key);
            _shown = _popShown = "";
            Refresh();
            if (err != null) Questions.Tell(null, "vDeploy couldn't " + action + " " + c.Label + ": " + err, Plugin.Source);
        }

        async void Act(VdStack k, string action)
        {
            _busy.Add(k.Key);
            Refresh();
            string err = await Plugin.ActAsync(k, action);
            _busy.Remove(k.Key);
            Refresh();
            if (err != null) Questions.Tell(null, "vDeploy couldn't " + action + " " + k.Title + ": " + err, Plugin.Source);
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
            if (!VdSettings.Of(p.Settings).Configured) p.Note("Set up the vDeploy connection first (Plugins page in this window).");
            if (!p.IsWidget)
            {
                p.Header("On the bar");
                p.Check("Running / total", delegate { return b.Get("ShowCount", true); }, delegate (bool v) { b.Set("ShowCount", v); });
                p.Title(cfg, "");
                p.Note("The dot is green when everything runs, amber when something's stopped or starting, red for a problem (unhealthy, a host not answering) and the accent colour while a deploy runs. ⚠ problems, ⬆ stacks with newer images. Click it for the stacks: click one to open it in vDeploy, its arrow for its containers, right-click a stack or a container for its actions, a shell in vTerm and its log; Update for newer images.");
            }
            else
            {
                p.Header("In the widget");
                p.Check("Only stacks that need you (problems, newer images, deploying)", delegate { return b.Get("OnlyNeeding", false); }, delegate (bool v) { b.Set("OnlyNeeding", v); });
                p.Check("Grouped by host", delegate { return b.Get("GroupHosts", true); }, delegate (bool v) { b.Set("GroupHosts", v); });
                p.Slider("How many", 1, 60, delegate { return b.Get("Count", 12.0); }, delegate (double v) { b.Set("Count", Math.Round(v)); });
                p.Note("Click a stack to open it in vDeploy; its arrow shows its containers; right-click a stack or a container for its actions, a shell in vTerm and its log.");
            }
            p.Check("Show the Proxmox nodes and their LXCs / VMs", delegate { return b.Get("Nodes", true); }, delegate (bool v) { b.Set("Nodes", v); });
            p.Note("vDeploy's Proxmox nodes under the stacks, with the LXCs and VMs the helper scripts made: ⬆ when an app has an update (Update opens its terminal in vDeploy), right-click for start, shut down, reboot and a console in vTerm. Off: only the Docker stacks (and the bar's ⬆ / ⚠ leave them out).");
            p.Check("Show the automations (with a Run button)", delegate { return b.Get("Automations", true); }, delegate (bool v) { b.Set("Automations", v); });
            p.Note("vDeploy's automations under the stacks - Run asks first, then vDeploy runs it (its page shows each step). With \"Only stacks that need you\", only automations whose last run failed show.");
        }
    }

    /// <summary>The gallery pictures.</summary>
    public static class Pictures
    {
        public static FrameworkElement Bar(Theme t)
        {
            return ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 8), ModulePics.T(t, "14/15 running", t.Fg, 12.5), ModulePics.T(t, "⬆ 2", t.MainAccent, 12.5) });
        }

        public static FrameworkElement Widget(Theme t)
        {
            return ModulePics.Col(4, new UIElement[]
            {
                ModulePics.T(t, "docker-vm", t.FgDim, 10.5, true),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "jellyfin ⬆", t.Fg, 12), ModulePics.T(t, "1/1", t.FgDim, 10.5) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Warn, 7), ModulePics.T(t, "immich", t.Fg, 12), ModulePics.T(t, "3/4", t.FgDim, 10.5) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "arrstack", t.Fg, 12), ModulePics.T(t, "6/6", t.FgDim, 10.5) })
            });
        }
    }
}
