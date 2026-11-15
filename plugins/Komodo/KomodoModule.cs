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
    /// Komodo: the Docker stacks, deployments and containers it manages. On a bar a compact line (a dot, running / total, problems,
    /// updates) - click it for the list in a popup. In a widget the list itself. Click an item for start / stop / restart / pause /
    /// redeploy / pull / destroy (asks first for the ones that interrupt), a stack's arrow for its services, a procedure's ▶ to run it.
    /// </summary>
    public sealed class KomodoModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public KomodoModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            if (KomodoList.MenuOpen) return;
            KoSnapshot s = Komodo.Latest;
            // Stamp moves only when something in the list changed (not on every read)
            string key = (Komodo.Configured(_mgr.Settings) ? "c" : "n") + "|" + s.Stamp.Ticks + "|" + s.Ok + "|" + s.Error + "|" + KomodoList.Version;
            if (!force && key == _shownKey) return;
            _shownKey = key;
            if (_host.IsWidget) Child = KomodoList.Build(_mgr, _t, _cfg, s, true, delegate { Refresh(true); });
            else
            {
                Child = BuildBar(s);
                if (_popup != null && _popup.IsOpen) _popup.SetContent(KomodoList.Build(_mgr, _t, _cfg, s, false, delegate { Refresh(true); }));
            }
            _host.ContentChanged();
        }

        FrameworkElement BuildBar(KoSnapshot s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b)
            {
                return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            };
            if (!Komodo.Configured(_mgr.Settings)) { row.Children.Add(T("Set up Komodo in Settings > Plugins", _t.FgDim)); ToolTip = null; return row; }
            if (!s.Ok) { row.Children.Add(T(s.Error.Length > 0 ? "Komodo n/a" : "Komodo…", _t.FgDim)); ToolTip = s.Error.Length > 0 ? "Can't reach Komodo: " + s.Error : null; return row; }

            List<KoItem> items = KomodoList.Pick(s, _cfg, false);
            int running = items.FindAll(delegate (KoItem i) { return i.Running; }).Count;
            int bad = items.FindAll(delegate (KoItem i) { return i.Health == "bad"; }).Count;
            int updates = items.FindAll(delegate (KoItem i) { return i.Update; }).Count;
            int serversDown = s.Servers.FindAll(delegate (KoServer x) { return x.State == "notok"; }).Count;
            Brush dot = bad > 0 || serversDown > 0 ? KomodoList.Bad : items.Exists(delegate (KoItem i) { return i.Health == "busy"; }) ? KomodoList.Busy : running > 0 ? KomodoList.Good : _t.FgDim;
            double d = Math.Round(_t.FontSize * 0.6);
            row.Children.Add(new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = dot, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(T(_cfg.Title != null && _cfg.Title.Length > 0 ? _cfg.Title + " " : "", _t.FgDim));
            row.Children.Add(T(running + "/" + items.Count, _t.Fg));
            row.Children.Add(T(" running", _t.FgDim));
            if (bad > 0) row.Children.Add(T("   ⚠ " + bad, KomodoList.Bad));
            if (serversDown > 0) row.Children.Add(T("   ✕ " + serversDown + " server" + (serversDown > 1 ? "s" : ""), KomodoList.Bad));
            if (updates > 0) row.Children.Add(T("   ⬆ " + updates, _t.MainAccent));

            var tip = new List<string> { "Komodo: " + running + " of " + items.Count + " running" };
            foreach (KoItem i in items) if (i.Health == "bad" || i.Health == "busy") tip.Add("  " + i.Name + " - " + i.State.Replace('_', ' ') + (i.ServerName.Length > 0 ? " on " + i.ServerName : ""));
            if (updates > 0) tip.Add(updates + " with an image update");
            foreach (KoServer x in s.Servers) if (x.State == "notok") tip.Add("Server " + x.Name + " can't be reached");
            if (s.Error.Length > 0) tip.Add("Last update failed: " + s.Error);
            tip.Add("Click for the list");
            ToolTip = string.Join("\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            _popup = new PopupWindow(_mgr, _host, this, _t, KomodoList.Build(_mgr, _t, _cfg, Komodo.Latest, false, delegate { Refresh(true); }));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>The Komodo list (popup and widget) and its actions.</summary>
    public static class KomodoList
    {
        public static readonly Brush Good = StatusColors.Good, Busy = StatusColors.Warn, Bad = StatusColors.Bad;
        static Brush Frozen(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        /// <summary>Bumped whenever something only the list knows changes (pending actions, opened stacks, loaded services).</summary>
        public static int Version;
        public static bool MenuOpen;
        static bool _asking;

        sealed class Pending { public string Text, From; public DateTime Until; }
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();
        static readonly HashSet<string> Open = new HashSet<string>();
        static readonly Dictionary<string, List<KoService>> ServiceCache = new Dictionary<string, List<KoService>>();
        static readonly Dictionary<string, DateTime> ServiceAt = new Dictionary<string, DateTime>();

        public static Brush HealthBrush(string health, Theme t)
        {
            switch (health) { case "good": return Good; case "busy": return Busy; case "bad": return Bad; default: return t.FgDim; }
        }

        /// <summary>The items this module shows (no procedures), filtered and sorted: problems first, then by name.</summary>
        public static List<KoItem> Pick(KoSnapshot s, ModuleConfig cfg, bool withProcedures)
        {
            KoView ko = new KoView(cfg);
            var list = new List<KoItem>();
            if (ko.Show != "Containers") { list.AddRange(s.Stacks); list.AddRange(s.Deployments); }
            if (ko.Show == "Containers") list.AddRange(s.Containers);
            if (ko.Show == "Both") list.AddRange(s.Containers.FindAll(delegate (KoItem c) { return c.Owner.Length == 0; }));     // the ones Komodo doesn't manage
            if (ko.Server.Length > 0) list = list.FindAll(delegate (KoItem i) { return string.Equals(i.ServerName, ko.Server, StringComparison.OrdinalIgnoreCase); });
            if (ko.Filter == "Problems") list = list.FindAll(delegate (KoItem i) { return i.Health != "good" || i.Update; });
            Func<KoItem, int> rank = delegate (KoItem i) { return i.Health == "bad" ? 0 : i.Health == "busy" ? 1 : i.Health == "off" ? 2 : 3; };
            list.Sort(delegate (KoItem a, KoItem b)
            {
                int d = rank(a).CompareTo(rank(b));
                return d != 0 ? d : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (withProcedures && ko.ShowProcedures) list.AddRange(s.Procedures);
            return list;
        }

        static void ExpireWaiting(KoSnapshot s)
        {
            if (Waiting.Count == 0) return;
            var all = new Dictionary<string, KoItem>();
            foreach (KoItem i in s.Stacks) all[i.Key] = i;
            foreach (KoItem i in s.Deployments) all[i.Key] = i;
            foreach (KoItem i in s.Containers) all[i.Key] = i;
            foreach (KoItem i in s.Procedures) all[i.Key] = i;
            var gone = new List<string>();
            foreach (var kv in Waiting)
            {
                KoItem it;
                bool changed = all.TryGetValue(kv.Key.Split('#')[0], out it) && kv.Value.From != null && it.State != kv.Value.From;
                if (DateTime.UtcNow > kv.Value.Until || changed) gone.Add(kv.Key);
            }
            foreach (string k in gone) Waiting.Remove(k);
            if (gone.Count > 0) Version++;
        }

        static TextBlock T(Theme t, string text, Brush b, double scale = 1, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = t.Font, FontSize = Math.Max(7, t.FontSize * scale), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        static Border Dot(Brush b, double size)
        {
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open " + url + ": " + ex.Message); }
        }

        /// <summary>A widget's list height with no stacks opened (per module): opening one scrolls within it.</summary>
        static readonly Dictionary<string, double> ListHeight = new Dictionary<string, double>();

        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, KoSnapshot s, bool widget, Action changed)
        {
            KoView ko = new KoView(cfg);
            ExpireWaiting(s);
            var root = new StackPanel();
            if (!widget) { root.Width = 420; root.Margin = new Thickness(4); }
            if (!Komodo.Configured(mgr.Settings))
            {
                var none = T(t, "Set up Komodo on the Plugins page of Settings (address, API key and secret).", t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(none);
                return root;
            }
            if (!s.Ok)
            {
                var w = T(t, s.Error.Length > 0 ? "Can't reach Komodo: " + s.Error : "Asking Komodo…", s.Error.Length > 0 ? Bad : t.FgDim);
                w.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(w);
                return root;
            }

            // ---- header: counts, open Komodo ----
            List<KoItem> items = Pick(s, cfg, false);
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var openAll = Pill(t, "", "Komodo", "Open Komodo", delegate { OpenUrl(Komodo.PageUrl(mgr.Settings, null)); });
            DockPanel.SetDock(openAll, Dock.Right);
            if (ko.ShowOpen) head.Children.Add(openAll);
            int running = items.FindAll(delegate (KoItem i) { return i.Running; }).Count;
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(T(t, ko.Show == "Containers" ? "Containers" : ko.Show == "Both" ? "Stacks, deployments and containers" : "Stacks and deployments", t.Fg, 1, true));
            title.Children.Add(T(t, "   " + running + " of " + items.Count + " running", t.FgDim, 0.9));
            head.Children.Add(title);
            root.Children.Add(head);
            if (s.Error.Length > 0)
            {
                var e = T(t, "Last update failed: " + s.Error, Busy, 0.85);
                e.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(e);
            }
            if (items.Count == 0)
                root.Children.Add(T(t, ko.Filter == "Problems" ? "No problems - everything is running." : "Nothing to show.", t.FgDim, 0.9));

            // ---- the list: in a widget it scrolls when stacks are opened, instead of the widget growing ----
            StackPanel list = widget ? new StackPanel() : root;
            bool anyOpen = items.Exists(delegate (KoItem i) { return i.Kind == "stack" && Open.Contains(i.Key); });

            // ---- grouped by server (or one list) ----
            var groups = new List<KeyValuePair<string, List<KoItem>>>();
            bool grouped = ko.Group && ko.Server.Length == 0;     // one server picked: nothing to group
            if (grouped)
            {
                var byServer = new Dictionary<string, List<KoItem>>();
                foreach (KoItem i in items)
                {
                    string k = i.ServerName.Length > 0 ? i.ServerName : "(no server)";
                    List<KoItem> l;
                    if (!byServer.TryGetValue(k, out l)) { l = new List<KoItem>(); byServer[k] = l; groups.Add(new KeyValuePair<string, List<KoItem>>(k, l)); }
                    l.Add(i);
                }
                groups.Sort(delegate (KeyValuePair<string, List<KoItem>> a, KeyValuePair<string, List<KoItem>> b) { return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
            }
            else groups.Add(new KeyValuePair<string, List<KoItem>>("", items));

            foreach (var g in groups)
            {
                if (g.Key.Length > 0)
                {
                    KoServer sv = s.Servers.Find(delegate (KoServer x) { return x.Name == g.Key; });
                    var gh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
                    gh.Children.Add(Dot(sv == null ? t.FgDim : sv.State == "ok" ? Good : sv.State == "disabled" ? t.FgDim : Bad, 7));
                    gh.Children.Add(T(t, g.Key, t.FgDim, 0.9, true));
                    if (sv != null && sv.State == "notok") gh.Children.Add(T(t, "  can't be reached", Bad, 0.85));
                    list.Children.Add(gh);
                }
                int n = 0;
                foreach (KoItem it in g.Value)
                {
                    if (n++ >= ko.Count)
                    {
                        var more = T(t, "+ " + (g.Value.Count - ko.Count) + " more", t.FgDim, 0.85);
                        more.Margin = new Thickness(18, 1, 0, 0);
                        list.Children.Add(more);
                        break;
                    }
                    list.Children.Add(ItemRow(mgr, t, it, !grouped, changed));
                    if (it.Kind == "stack" && Open.Contains(it.Key)) AddServices(mgr, t, it, list, changed);
                }
            }

            // ---- procedures ----
            if (ko.ShowProcedures && s.Procedures.Count > 0 && ko.Filter != "Problems" || ko.ShowProcedures && s.Procedures.Exists(delegate (KoItem p) { return p.State == "failed"; }))
            {
                var ph = T(t, "Procedures", t.FgDim, 0.9, true);
                ph.Margin = new Thickness(0, 10, 0, 2);
                list.Children.Add(ph);
                var procs = new List<KoItem>(s.Procedures);
                if (ko.Filter == "Problems") procs = procs.FindAll(delegate (KoItem p) { return p.State == "failed"; });
                procs.Sort(delegate (KoItem a, KoItem b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
                foreach (KoItem p in procs) list.Children.Add(ProcedureRow(mgr, t, p, changed));
            }

            if (widget)
            {
                string id = cfg.Id ?? "";
                double h;
                if (!anyOpen)
                {
                    // nothing opened: this is the widget's size - remember it
                    list.SizeChanged += delegate { if (list.ActualHeight > 0) ListHeight[id] = list.ActualHeight; };
                    root.Children.Add(list);
                }
                else
                {
                    var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
                    scroll.MaxHeight = ListHeight.TryGetValue(id, out h) && h > 40 ? h : Math.Round(t.FontSize * 24);
                    root.Children.Add(scroll);
                }
            }
            return root;
        }

        static Border Pill(Theme t, string glyph, string text, string tip, Action click)
        {
            return Pills.Make(t, glyph, text, tip, false, click);
        }

        static FrameworkElement ItemRow(BarManager mgr, Theme t, KoItem it, bool showServer, Action changed)
        {
            Pending p;
            bool waiting = Waiting.TryGetValue(it.Key, out p);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // a stack opens to its services
            if (it.Kind == "stack")
            {
                bool open = Open.Contains(it.Key);
                var arrow = new TextBlock
                {
                    Text = open ? "" : "", FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(7, t.FontSize * 0.7), Foreground = t.FgDim,
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = open ? "Hide the services" : "Show the services", Background = Brushes.Transparent
                };
                arrow.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                arrow.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (!Open.Remove(it.Key)) { Open.Add(it.Key); LoadServices(it, changed); }
                    Version++;
                    changed();
                };
                g.Children.Add(arrow);
            }
            var dot = Dot(waiting ? Busy : HealthBrush(it.Health, t), 8);
            Grid.SetColumn(dot, 1);
            g.Children.Add(dot);

            var names = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(T(t, it.Name, t.Fg, 0.95));
            string kind = it.Kind == "container" ? (it.Owner.Length > 0 ? "  " + it.Owner : "") : "  " + it.Kind;
            names.Children.Add(T(t, kind, t.FgDim, 0.8));
            if (showServer && it.ServerName.Length > 0) names.Children.Add(T(t, "  · " + it.ServerName, t.FgDim, 0.8));
            if (it.Update)
            {
                var up = T(t, "  ⬆", t.MainAccent, 0.9);
                up.ToolTip = "A newer image is available" + (it.Kind == "stack" ? " (" + UpdatedServices(it) + ")" : "") + " - Redeploy to update";
                names.Children.Add(up);
            }
            Grid.SetColumn(names, 2);
            g.Children.Add(names);

            string state = waiting ? p.Text : it.State.Replace('_', ' ');
            var st = T(t, state, waiting ? Busy : it.Health == "bad" ? Bad : it.Health == "good" ? t.FgDim : t.Fg, 0.85);
            st.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(st, 3);
            g.Children.Add(st);

            var tip = new List<string> { it.Name + " (" + it.Kind + ")" };
            if (it.ServerName.Length > 0) tip.Add("Server: " + it.ServerName);
            if (it.Status.Length > 0) tip.Add(it.Status);
            if (it.Image.Length > 0) tip.Add("Image: " + it.Image);
            if (it.Kind == "stack" && it.Services.Count > 0) tip.Add("Services: " + string.Join(", ", it.Services.ConvertAll(delegate (KoService x) { return x.Service + (x.Update ? " ⬆" : ""); })));
            tip.Add("Click for actions");

            var row = new Border { Child = g, Padding = new Thickness(3, 3, 5, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = string.Join("\n", tip) };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(mgr, row, it, null, changed); };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(mgr, row, it, null, changed); };
            return row;
        }

        static string UpdatedServices(KoItem stack)
        {
            var l = new List<string>();
            foreach (KoService s in stack.Services) if (s.Update) l.Add(s.Service);
            return string.Join(", ", l);
        }

        static void LoadServices(KoItem stack, Action changed)
        {
            DateTime at;
            if (ServiceAt.TryGetValue(stack.Key, out at) && (DateTime.UtcNow - at).TotalSeconds < 4) return;
            ServiceAt[stack.Key] = DateTime.UtcNow;
            Dispatcher d = Dispatcher.CurrentDispatcher;
            Komodo.ServicesAsync(stack).ContinueWith(delegate (System.Threading.Tasks.Task<List<KoService>> task)
            {
                d.BeginInvoke(new Action(delegate
                {
                    ServiceCache[stack.Key] = task.Result;
                    Version++;
                    changed();
                }));
            });
        }

        static void AddServices(BarManager mgr, Theme t, KoItem stack, StackPanel root, Action changed)
        {
            List<KoService> list;
            DateTime at;
            // look again now and then while it is open
            if (!ServiceAt.TryGetValue(stack.Key, out at) || (DateTime.UtcNow - at).TotalSeconds > 8) LoadServices(stack, changed);
            if (!ServiceCache.TryGetValue(stack.Key, out list))
            {
                var w = T(t, "Loading the services…", t.FgDim, 0.85);
                w.Margin = new Thickness(36, 1, 0, 2);
                root.Children.Add(w);
                return;
            }
            if (list.Count == 0)
            {
                var w = T(t, "No services found", t.FgDim, 0.85);
                w.Margin = new Thickness(36, 1, 0, 2);
                root.Children.Add(w);
                return;
            }
            foreach (KoService sv in list)
            {
                string key = stack.Key + "#" + sv.Service;
                Pending p;
                bool waiting = Waiting.TryGetValue(key, out p);
                string health = sv.State == "running" ? "good" : sv.State == "unhealthy" || sv.State == "dead" ? "bad" : sv.State == "restarting" || sv.State == "paused" || sv.State == "created" ? "busy" : "off";
                var g = new Grid { Margin = new Thickness(30, 0, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Dot(waiting ? Busy : HealthBrush(health, t), 6));
                var name = new StackPanel { Orientation = Orientation.Horizontal };
                name.Children.Add(T(t, sv.Service, t.Fg, 0.88));
                if (sv.Update) name.Children.Add(T(t, "  ⬆", t.MainAccent, 0.85));
                Grid.SetColumn(name, 1);
                g.Children.Add(name);
                var st = T(t, waiting ? p.Text : sv.State.Replace('_', ' '), waiting ? Busy : health == "bad" ? Bad : t.FgDim, 0.8);
                st.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(st, 2);
                g.Children.Add(st);
                var row = new Border
                {
                    Child = g, Padding = new Thickness(3, 2, 5, 2), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                    ToolTip = sv.Service + (sv.Container.Length > 0 ? "\nContainer: " + sv.Container : "") + (sv.Status.Length > 0 ? "\n" + sv.Status : "") +
                              (sv.Image.Length > 0 ? "\nImage: " + sv.Image : "") + "\nClick for actions on this service"
                };
                row.MouseEnter += delegate { row.Background = t.Hover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                KoService service = sv;
                row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(mgr, row, stack, service, changed); };
                row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(mgr, row, stack, service, changed); };
                root.Children.Add(row);
            }
        }

        static FrameworkElement ProcedureRow(BarManager mgr, Theme t, KoItem p, Action changed)
        {
            Pending w;
            bool waiting = Waiting.TryGetValue(p.Key, out w);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string health = p.State == "running" || waiting ? "busy" : p.State == "failed" ? "bad" : p.State == "ok" ? "good" : "off";
            var dot = Dot(HealthBrush(health, t), 8);
            Grid.SetColumn(dot, 1);
            g.Children.Add(dot);
            var name = T(t, p.Name, t.Fg, 0.95);
            Grid.SetColumn(name, 2);
            g.Children.Add(name);
            var st = T(t, waiting ? w.Text : p.State == "running" ? "running…" : (p.State == "failed" ? "failed · " : "") + Komodo.Ago(p.LastRun), health == "bad" ? Bad : t.FgDim, 0.8);
            st.Margin = new Thickness(8, 0, 6, 0);
            Grid.SetColumn(st, 3);
            g.Children.Add(st);
            Border run = null;
            run = Pill(t, "", "Run", "Run " + p.Name, delegate { Act(mgr, run, p, Komodo.Actions(p)[0], null, changed); });
            Grid.SetColumn(run, 4);
            if (p.State != "running" && !waiting) g.Children.Add(run);
            var row = new Border
            {
                Child = g, Padding = new Thickness(3, 2, 5, 2), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent,
                ToolTip = p.Name + "\nLast run: " + Komodo.Ago(p.LastRun) + (p.State.Length > 0 ? " (" + p.State + ")" : "") + "\nRight-click: open in Komodo"
            };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ShowMenu(mgr, row, p, null, changed); };
            return row;
        }

        // =================================================================
        // Actions
        // =================================================================
        public static void ShowMenu(BarManager mgr, FrameworkElement target, KoItem it, KoService service, Action changed)
        {
            var m = new ContextMenu();
            string head = service != null ? service.Service + "  (" + it.Name + ")" : it.Name + "  (" + it.Kind + (it.ServerName.Length > 0 ? " on " + it.ServerName : "") + ")";
            m.Items.Add(new MenuItem { Header = head + " - " + (service != null ? service.State : it.State).Replace('_', ' '), IsEnabled = false });
            m.Items.Add(new Separator());
            List<string[]> acts = Komodo.Actions(it);
            if (service != null)
            {
                // one service of a stack: start / restart / stop / pause, redeploy and pull just that service
                bool up = service.State == "running" || service.State == "unhealthy" || service.State == "restarting";
                acts = new List<string[]>();
                if (!up && service.State != "paused") acts.Add(new[] { "Start", "StartStack", "" });
                if (up) { acts.Add(new[] { "Restart", "RestartStack", "ask" }); acts.Add(new[] { "Stop", "StopStack", "ask" }); acts.Add(new[] { "Pause", "PauseStack", "" }); }
                if (service.State == "paused") acts.Add(new[] { "Unpause", "UnpauseStack", "" });
                acts.Add(new[] { "-" });
                acts.Add(new[] { service.Update ? "Pull and redeploy (update)" : "Redeploy", "DeployStack", "ask" });
                acts.Add(new[] { "Pull image", "PullStack", "" });
            }
            foreach (string[] a in acts)
            {
                if (a[0] == "-") { m.Items.Add(new Separator()); continue; }
                string[] act = a;
                var mi = new MenuItem { Header = act[0] };
                mi.Click += delegate { Act(mgr, target, it, act, service, changed); };
                m.Items.Add(mi);
            }
            if (it.Kind == "stack" && service == null)
            {
                bool open = Open.Contains(it.Key);
                var mi = new MenuItem { Header = open ? "Hide the services" : "Show the services" };
                mi.Click += delegate { if (!Open.Remove(it.Key)) { Open.Add(it.Key); LoadServices(it, changed); } Version++; changed(); };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            if (it.Kind != "procedure")
            {
                // shells: on the server (SSH), and inside the container (docker exec over SSH)
                KoSnapshot ks = Komodo.Latest;
                KoServer sv = ks == null ? null : ks.Servers.Find(delegate (KoServer x) { return x.Id == it.ServerId; });
                string shHost = sv != null ? sv.Host : "";
                string serverName = sv != null ? sv.Name : it.ServerName;
                string container = service != null ? service.Container : it.Kind == "container" || it.Kind == "deployment" ? it.Name : "";
                bool alive = (service != null ? service.State : it.State) == "running" || (service != null ? service.State : it.State) == "unhealthy";
                if (container.Length > 0 && alive)
                {
                    var inside = new MenuItem { Header = "Open shell in the container", ToolTip = shHost.Length == 0 ? "Komodo has no address for " + serverName : null };
                    inside.Click += delegate { TermOpen.Host(mgr, shHost, serverName, "root", TermOpen.DockerShell(container)); };
                    m.Items.Add(inside);
                }
                var onServer = new MenuItem { Header = "Open shell on " + (serverName.Length > 0 ? serverName : "the server") };
                onServer.Click += delegate { TermOpen.Host(mgr, shHost, serverName, "root", null); };
                m.Items.Add(onServer);
                var log = new MenuItem { Header = service != null ? "Show this service's log" : "Show log" };
                Theme lt = Theme.From(mgr.Settings, 12.5, 8);
                log.Click += delegate { KomodoLogWindow.Show(lt, it, service != null ? service.Service : null); };
                m.Items.Add(log);
            }
            string url = service != null ? Komodo.PageUrl(mgr.Settings, it) + "/service/" + Uri.EscapeDataString(service.Service) : Komodo.PageUrl(mgr.Settings, it);
            var openWeb = new MenuItem { Header = "Open in Komodo" };
            openWeb.Click += delegate { OpenUrl(url); };
            m.Items.Add(openWeb);

            PopupWindow host = Window.GetWindow(target) as PopupWindow;
            if (host != null) host.Pinned = true;       // the menu must not close the popup
            MenuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                // a menu item's Click runs after Closed: let it ask its question first
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!_asking) MenuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>Do it (asking first when the action says so); "restarting…" shows on the row until Komodo reports a new state.</summary>
        public static void Act(BarManager mgr, FrameworkElement target, KoItem it, string[] act, KoService service, Action changed)
        {
            if (act.Length > 2 && act[2] == "ask")
            {
                Window owner = target != null ? Window.GetWindow(target) : null;
                PopupWindow host = owner as PopupWindow;
                if (host != null) host.Pinned = true;
                MessageBoxResult res;
                _asking = MenuOpen = true;
                string q = Komodo.Question(it, act[0], service != null ? service.Service : null);
                try { res = Questions.YesNo(owner, q, "Komodo"); }
                finally { if (host != null) host.Pinned = false; _asking = MenuOpen = false; }
                if (res != MessageBoxResult.Yes) return;
            }
            MenuOpen = false;
            string key = service != null ? it.Key + "#" + service.Service : it.Key;
            string verb = act[0].ToLowerInvariant();
            string text = verb.StartsWith("restart") ? "restarting…" : verb.StartsWith("stop") ? "stopping…" : verb.StartsWith("start") ? "starting…" :
                          verb.StartsWith("pause") ? "pausing…" : verb.StartsWith("unpause") ? "resuming…" : verb.StartsWith("pull and") || verb.StartsWith("redeploy") || verb.StartsWith("deploy") ? "deploying…" :
                          verb.StartsWith("pull") ? "pulling…" : verb.StartsWith("destroy") ? "destroying…" : verb.StartsWith("run") ? "running…" : "working…";
            // the state it has now: the row goes back to normal once Komodo shows another one (restart / pull / redeploy: after a while)
            bool sameState = verb.StartsWith("restart") || verb.StartsWith("pull") || verb.StartsWith("redeploy");
            Waiting[key] = new Pending { Text = text, From = service != null || sameState ? null : it.State, Until = DateTime.UtcNow.AddSeconds(sameState || service != null ? 20 : 120) };
            Version++;
            changed();
            Dispatcher d = Dispatcher.CurrentDispatcher;
            Komodo.RunAsync(it, act[1], service != null ? service.Service : null).ContinueWith(delegate (System.Threading.Tasks.Task<string> task)
            {
                d.BeginInvoke(new Action(delegate
                {
                    if (service != null) ServiceAt.Remove(it.Key);      // read the services again
                    if (task.Result == null) { changed(); return; }
                    Waiting.Remove(key);
                    Version++;
                    changed();
                    MessageBox.Show(task.Result, "Komodo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }));
            });
        }
    }
}
