using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Tdarr: what it's transcoding / checking. On a bar an icon with the number of jobs running as a badge (and the files waiting) - click
    /// it for the popup. In a widget the view itself: the running jobs, the nodes (pause / resume, worker limits) and the library totals.
    /// </summary>
    public sealed class TdarrModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public TdarrModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            if (TdarrList.MenuOpen) return;
            bool configured = TdarrClient.Configured(_mgr.Settings);
            TdarrSnapshot s = configured ? TdarrClient.Latest : new TdarrSnapshot();
            string key = (configured ? "c" : "n") + "|" + s.Fetched.Ticks + "|" + s.Error + "|" + TdarrList.Version;
            if (!force && key == _shownKey) return;
            _shownKey = key;
            if (_host.IsWidget) Child = TdarrList.Build(_mgr, _t, _cfg, s, true);
            else
            {
                Child = BuildBar(s, configured);
                if (_popup != null && _popup.IsOpen) _popup.SetContent(TdarrList.Build(_mgr, _t, _cfg, s, false));
            }
            _host.ContentChanged();
        }

        FrameworkElement BuildBar(TdarrSnapshot s, bool configured)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b)
            {
                return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            };
            Visibility = Visibility.Visible;
            if (!configured) { row.Children.Add(T("Set up Tdarr in Settings > Plugins", _t.FgDim)); ToolTip = null; return row; }

            TdView v = TdView.Of(_cfg);
            int running = s.Workers.Count;
            if (v.HideIdle && s.Ok && running == 0 && s.Error.Length == 0) { Visibility = Visibility.Collapsed; return row; }

            // the icon, with the number of jobs running as a badge
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            icon.Children.Add(new TextBlock
            {
                Text = TdarrList.G(TdarrList.Glyph), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.1, VerticalAlignment = VerticalAlignment.Center,
                Foreground = running > 0 ? _t.Fg : _t.FgDim, Margin = new Thickness(0, 0, v.Badge && running > 0 ? 6 : 0, 0)
            });
            if (v.Badge && running > 0)
            {
                double fs = Math.Max(7, _t.FontSize * 0.62);
                var badge = new Border
                {
                    Background = _t.MainAccent, CornerRadius = new CornerRadius(fs), MinWidth = fs * 1.6, Height = fs * 1.6, Padding = new Thickness(fs * 0.3, 0, fs * 0.3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -fs * 0.5, -fs * 0.2, 0),
                    Child = new TextBlock
                    {
                        Text = running > 99 ? "99+" : running.ToString(), FontSize = fs, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(_t.MainAccent),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                icon.Children.Add(badge);
            }
            row.Children.Add(icon);
            if (_cfg.Title != null && _cfg.Title.Length > 0) row.Children.Add(T(" " + _cfg.Title, _t.FgDim));
            if (!s.Ok)
            {
                row.Children.Add(T(s.Error.Length > 0 ? " Tdarr n/a" : " Tdarr…", _t.FgDim));
                ToolTip = s.Error.Length > 0 ? "Can't reach Tdarr: " + s.Error : null;
                return row;
            }
            if (!v.Badge && running > 0) row.Children.Add(T(" " + running + " running", _t.Fg));
            if (v.BarQueue && s.Stats.Queue > 0) row.Children.Add(T(" " + s.Stats.Queue + " queued", _t.FgDim));
            bool paused = s.Nodes.Count > 0 && s.Nodes.TrueForAll(delegate (TdarrNode n) { return n.Paused; });
            if (paused) row.Children.Add(T(" paused", StatusColors.Warn));

            var tip = new List<string>();
            tip.Add("Tdarr: " + (running == 0 ? "nothing running" : running + " running") + " · " + s.Stats.Queue + " in the queue" +
                    (s.Stats.Errors > 0 ? " · " + s.Stats.Errors + " errors" : ""));
            int k = 0;
            foreach (TdarrWorker w in s.Workers)
            {
                if (k++ >= 6) break;
                tip.Add("  " + w.Name + " - " + Math.Floor(w.Percent) + "%" + (w.Eta.Length > 0 ? " · " + w.Eta + " left" : ""));
            }
            foreach (TdarrNode n in s.Nodes) if (n.Paused) tip.Add("Node " + n.Name + " is paused");
            if (s.Error.Length > 0) tip.Add("Last update failed: " + s.Error);
            tip.Add("Click for the jobs and nodes");
            ToolTip = string.Join("\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            TdarrClient.Poke();
            _popup = new PopupWindow(_mgr, _host, this, _t, TdarrList.Build(_mgr, _t, _cfg, TdarrClient.Latest, false));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>The Tdarr view (popup and widget) and its actions.</summary>
    public static class TdarrList
    {
        public const int Glyph = 0xE895;
        static readonly Brush Good = StatusColors.Good, Busy = StatusColors.Warn, Bad = StatusColors.Bad;

        /// <summary>Bumped when something only the view knows changes (a pending action).</summary>
        public static int Version;
        public static bool MenuOpen;

        /// <summary>"n:<node>" / "w:<worker>" -> "pausing…" until it's old (or Tdarr shows the change).</summary>
        sealed class Pending { public string Text; public DateTime Until; }
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();

        public static string G(int code) { return ((char)code).ToString(); }

        static void Bump() { Interlocked.Increment(ref Version); }

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

        static void ExpireWaiting()
        {
            if (Waiting.Count == 0) return;
            var gone = new List<string>();
            foreach (var kv in Waiting) if (DateTime.UtcNow > kv.Value.Until) gone.Add(kv.Key);
            foreach (string k in gone) Waiting.Remove(k);
        }

        static TextBlock Heading(Theme t, string text)
        {
            var h = T(t, text, t.FgDim, 0.78, true);
            h.Margin = new Thickness(2, 6, 0, 2);
            return h;
        }

        // =================================================================
        // The view
        // =================================================================
        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, TdarrSnapshot s, bool widget)
        {
            ExpireWaiting();
            BarSettings set = mgr.Settings;
            TdView v = TdView.Of(cfg);
            var root = new StackPanel();
            if (!widget) { root.Width = 420; root.Margin = new Thickness(4); }
            if (!TdarrClient.Configured(set))
            {
                var none = T(t, "Set up Tdarr on the Plugins page of Settings (its address).", t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(none);
                return root;
            }

            // ---- header: counts, open Tdarr ----
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (v.ShowOpen)
            {
                var open = Pills.Make(t, G(0xE8A7), "Tdarr", "Open Tdarr", false, delegate { OpenUrl(TdarrClient.PageUrl(set)); });
                DockPanel.SetDock(open, Dock.Right);
                head.Children.Add(open);
            }
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(T(t, cfg.Title != null && cfg.Title.Length > 0 ? cfg.Title : "Tdarr", t.Fg, 1, true));
            if (s.Ok)
            {
                title.Children.Add(T(t, "   " + (s.Workers.Count == 0 ? "nothing running" : s.Workers.Count + " running"), t.FgDim, 0.88));
                title.Children.Add(T(t, " · " + s.Stats.Queue + " in the queue", t.FgDim, 0.88));
            }
            head.Children.Add(title);
            root.Children.Add(head);

            if (!s.Ok)
            {
                var w = T(t, s.Error.Length > 0 ? "Can't reach Tdarr: " + s.Error : "Asking Tdarr…", s.Error.Length > 0 ? Bad : t.FgDim);
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

            if (v.ShowWorkers) BuildWorkers(mgr, t, v, s, root);
            if (v.ShowNodes) BuildNodes(mgr, t, s, root);
            if (v.ShowFailed && (s.Failed.Count > 0 || (s.FailedError.Length > 0 && s.Stats.Errors > 0))) BuildFailed(mgr, t, v, s, root);
            if (v.ShowStats) BuildStats(t, s, root);
            return root;
        }

        // ---- failed files ----
        static void BuildFailed(BarManager mgr, Theme t, TdView v, TdarrSnapshot s, StackPanel root)
        {
            var head = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
            Pending p;
            bool all = Waiting.TryGetValue("all", out p);
            FrameworkElement requeueAll = all ? (FrameworkElement)T(t, p.Text, Busy, 0.78)
                : Pills.Make(t, G(0xE72C), "Requeue all", "Put every failed file back in its queue (Tdarr tries them again)", false, delegate { RequeueAll(mgr, root); });
            DockPanel.SetDock(requeueAll, Dock.Right);
            requeueAll.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(requeueAll);
            var h = Heading(t, "FAILED (" + s.Stats.Errors + ")");
            h.Margin = new Thickness(2, 0, 0, 0);
            head.Children.Add(h);
            root.Children.Add(head);
            if (s.Failed.Count == 0)
            {
                var e = T(t, "Couldn't read the failed files: " + s.FailedError, Busy, 0.85);
                e.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(e);
                return;
            }
            int n = 0;
            foreach (TdarrFailed f in s.Failed)
            {
                if (n++ >= v.Count)
                {
                    var more = T(t, "+ " + (s.Stats.Errors - v.Count) + " more - Requeue all takes them all", t.FgDim, 0.85);
                    more.Margin = new Thickness(4, 2, 0, 0);
                    root.Children.Add(more);
                    break;
                }
                root.Children.Add(FailedRow(mgr, t, f));
            }
        }

        static FrameworkElement FailedRow(BarManager mgr, Theme t, TdarrFailed f)
        {
            Pending p;
            bool waiting = Waiting.TryGetValue("f:" + f.Id, out p);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(t, f.Name, t.Fg, 0.88));
            info.Children.Add(T(t, (f.Health ? "Health check failed" : "Transcode failed or cancelled") + (f.SizeGb > 0 ? " · " + TdarrClient.InfoBytes(f.SizeGb) : ""), f.Health ? Bad : Busy, 0.76));
            g.Children.Add(info);
            FrameworkElement right = waiting ? (FrameworkElement)T(t, p.Text, Busy, 0.8)
                : Pills.Make(t, G(0xE72C), "", "Requeue it (Tdarr tries the " + f.What + " again)", false, delegate { Requeue(mgr, f); });
            right.Margin = new Thickness(8, 0, 0, 0);
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            var row = new Border
            {
                Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent,
                ToolTip = f.Id + "\n" + (f.Health ? "Health check error" : "Transcode error / cancelled") + "\n\nRight-click: requeue, copy the path"
            };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var m = new ContextMenu();
                m.Items.Add(new MenuItem { Header = f.Name, IsEnabled = false });
                m.Items.Add(new Separator());
                Add(m, "Requeue (try the " + f.What + " again)", delegate { Requeue(mgr, f); });
                Add(m, "Copy the file's path", delegate { try { Clipboard.SetText(f.Id); } catch { } });
                Add(m, "Open Tdarr", delegate { OpenUrl(TdarrClient.PageUrl(mgr.Settings)); });
                ShowMenu(m, row);
            };
            return row;
        }

        static async void Requeue(BarManager mgr, TdarrFailed f)
        {
            MenuOpen = false;
            string key = "f:" + f.Id;
            Waiting[key] = new Pending { Text = "requeuing…", Until = DateTime.UtcNow.AddSeconds(15) };
            Bump();
            string err = await TdarrClient.RequeueAsync(mgr.Settings, f);
            Waiting.Remove(key);
            Bump();
            if (err != null) MessageBox.Show("Tdarr: " + err, "Tdarr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        static async void RequeueAll(BarManager mgr, FrameworkElement target)
        {
            TdarrSnapshot s = TdarrClient.Latest;
            if (!Ask(target, "Put all " + s.Stats.Errors + " failed files back in Tdarr's queues?\n\nTdarr tries the transcode / health check of each again.")) return;
            Waiting["all"] = new Pending { Text = "requeuing…", Until = DateTime.UtcNow.AddSeconds(120) };
            Bump();
            KeyValuePair<int, string> r = await TdarrClient.RequeueAllAsync(mgr.Settings);
            Waiting.Remove("all");
            Bump();
            if (r.Value != null) MessageBox.Show("Tdarr: " + r.Value, "Tdarr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ---- running jobs ----
        static void BuildWorkers(BarManager mgr, Theme t, TdView v, TdarrSnapshot s, StackPanel root)
        {
            root.Children.Add(Heading(t, "RUNNING"));
            if (s.Workers.Count == 0)
            {
                bool paused = s.Nodes.Count > 0 && s.Nodes.TrueForAll(delegate (TdarrNode n) { return n.Paused; });
                var nb = T(t, s.Nodes.Count == 0 ? "No nodes connected." : paused ? "Nothing running - every node is paused." :
                              s.Stats.Queue > 0 ? "Nothing running right now (" + s.Stats.Queue + " waiting)." : "Nothing running - the queue is empty.", t.FgDim, 0.88);
                nb.Margin = new Thickness(2, 2, 0, 4);
                root.Children.Add(nb);
                return;
            }
            int n = 0;
            foreach (TdarrWorker w in s.Workers)
            {
                if (n++ >= v.Count)
                {
                    var more = T(t, "+ " + (s.Workers.Count - v.Count) + " more running", t.FgDim, 0.85);
                    more.Margin = new Thickness(4, 2, 0, 0);
                    root.Children.Add(more);
                    break;
                }
                root.Children.Add(WorkerRow(mgr, t, s, w));
            }
        }

        static FrameworkElement WorkerRow(BarManager mgr, Theme t, TdarrSnapshot s, TdarrWorker w)
        {
            Pending p;
            bool waiting = Waiting.TryGetValue("w:" + w.Id, out p);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(t, w.Name, t.Fg, 0.92, true));
            var line = new List<string> { w.TypeText };
            if (s.Nodes.Count > 1) line.Add(w.NodeName);
            if (w.Fps > 0) line.Add(Math.Round(w.Fps) + " fps");
            if (w.SizeGb > 0) line.Add(TdarrClient.InfoBytes(w.SizeGb) + (w.EstGb > 0 && w.Transcode ? " → ~" + TdarrClient.InfoBytes(w.EstGb) : ""));
            info.Children.Add(T(t, string.Join(" · ", line), t.FgDim, 0.8));
            // a thin bar under it
            double pct = Math.Max(0, Math.Min(100, w.Percent));
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 3, 0, 1) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, pct), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - pct), GridUnitType.Star) });
            var track = new Border { Background = t.Track, CornerRadius = new CornerRadius(1.5) };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { Background = w.Transcode ? (Brush)t.MainAccent : t.SecondaryAccent, CornerRadius = new CornerRadius(1.5) });
            info.Children.Add(bar);
            g.Children.Add(info);
            var right = T(t, waiting ? p.Text : Math.Floor(pct) + "%" + (w.Eta.Length > 0 ? " · " + w.Eta : ""), waiting ? Busy : t.Fg, 0.8);
            right.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            var tip = new List<string> { w.File, w.TypeText + " on " + w.NodeName + " - " + Math.Floor(pct) + "%" };
            if (w.Eta.Length > 0) tip.Add(w.Eta + " left");
            if (w.Status.Length > 0) tip.Add(w.Status);
            tip.Add("Right-click: cancel it, copy the file's path");
            var row = new Border { Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = string.Join("\n", tip) };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; WorkerMenu(mgr, row, w); };
            return row;
        }

        static void WorkerMenu(BarManager mgr, FrameworkElement target, TdarrWorker w)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = w.Name + " - " + Math.Floor(w.Percent) + "%", IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, "Cancel this job…", delegate { Kill(mgr, target, w); });
            Add(m, "Copy the file's path", delegate { try { Clipboard.SetText(w.File); } catch { } });
            Add(m, "Open Tdarr", delegate { OpenUrl(TdarrClient.PageUrl(mgr.Settings)); });
            ShowMenu(m, target);
        }

        static async void Kill(BarManager mgr, FrameworkElement target, TdarrWorker w)
        {
            if (!Ask(target, "Cancel the " + w.TypeText.ToLowerInvariant() + " of " + w.Name + " (" + Math.Floor(w.Percent) + "% done)?\n\nTdarr stops the worker; the file keeps its original.")) return;
            MenuOpen = false;
            string key = "w:" + w.Id;
            Waiting[key] = new Pending { Text = "cancelling…", Until = DateTime.UtcNow.AddSeconds(20) };
            Bump();
            string err = await TdarrClient.KillAsync(mgr.Settings, w);
            if (err != null) { Waiting.Remove(key); Bump(); MessageBox.Show("Tdarr: " + err, "Tdarr", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // ---- nodes ----
        static void BuildNodes(BarManager mgr, Theme t, TdarrSnapshot s, StackPanel root)
        {
            root.Children.Add(Heading(t, s.Nodes.Count == 1 ? "NODE" : "NODES"));
            if (s.Nodes.Count == 0)
            {
                var nb = T(t, "No nodes connected to the server.", Busy, 0.88);
                nb.Margin = new Thickness(2, 2, 0, 4);
                root.Children.Add(nb);
                return;
            }
            foreach (TdarrNode n in s.Nodes) root.Children.Add(NodeRow(mgr, t, n));
        }

        static FrameworkElement NodeRow(BarManager mgr, Theme t, TdarrNode n)
        {
            Pending p;
            bool waiting = Waiting.TryGetValue("n:" + n.Id, out p);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            double d = 8;
            g.Children.Add(new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = n.Paused ? Busy : n.Running > 0 ? (Brush)t.MainAccent : Good,
                Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(T(t, n.Name, t.Fg, 0.92, true));
            top.Children.Add(T(t, "  " + (n.Paused ? "paused" : n.Running > 0 ? n.Running + " running" : "idle"), n.Paused ? Busy : t.FgDim, 0.8));
            info.Children.Add(top);
            var sub = new List<string> { n.LimitsText };
            if (n.Cpu.Length > 0) sub.Add("CPU " + n.Cpu + "%");
            if (n.MemUsed.Length > 0 && n.MemTotal.Length > 0) sub.Add(n.MemUsed + " of " + n.MemTotal + " GB");
            info.Children.Add(T(t, string.Join(" · ", sub), t.FgDim, 0.78));
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            FrameworkElement right = waiting ? (FrameworkElement)T(t, p.Text, Busy, 0.8)
                : Pills.Make(t, G(n.Paused ? 0xE768 : 0xE769), "", n.Paused ? "Resume this node" : "Pause this node (running jobs finish first)", n.Paused, delegate { Pause(mgr, n, !n.Paused); });
            right.Margin = new Thickness(8, 0, 0, 0);
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            var row = new Border
            {
                Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent,
                ToolTip = n.Name + (n.Paused ? " (paused)" : "") + "\nWorkers: " + n.LimitsText + "\n\nRight-click: change the worker limits"
            };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; NodeMenu(mgr, row, n); };
            return row;
        }

        static void NodeMenu(BarManager mgr, FrameworkElement target, TdarrNode n)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = n.Name + (n.Paused ? " (paused)" : ""), IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, n.Paused ? "Resume" : "Pause", delegate { Pause(mgr, n, !n.Paused); });
            m.Items.Add(new Separator());
            foreach (string type in TdarrClient.Types)
            {
                string ty = type;
                int have;
                n.Limits.TryGetValue(ty, out have);
                var sub = new MenuItem { Header = TdarrClient.TypeText(ty) + " workers: " + have };
                var more = new MenuItem { Header = "One more" };
                more.Click += delegate { Limit(mgr, n, ty, true); };
                sub.Items.Add(more);
                var fewer = new MenuItem { Header = "One fewer", IsEnabled = have > 0 };
                fewer.Click += delegate { Limit(mgr, n, ty, false); };
                sub.Items.Add(fewer);
                m.Items.Add(sub);
            }
            ShowMenu(m, target);
        }

        public static async void Pause(BarManager mgr, TdarrNode n, bool pause)
        {
            MenuOpen = false;
            string key = "n:" + n.Id;
            Waiting[key] = new Pending { Text = pause ? "pausing…" : "resuming…", Until = DateTime.UtcNow.AddSeconds(8) };
            Bump();
            string err = await TdarrClient.PauseAsync(mgr.Settings, n, pause);
            if (err != null) { Waiting.Remove(key); Bump(); MessageBox.Show("Tdarr: " + err, "Tdarr", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        static async void Limit(BarManager mgr, TdarrNode n, string type, bool more)
        {
            MenuOpen = false;
            string key = "n:" + n.Id;
            Waiting[key] = new Pending { Text = "changing…", Until = DateTime.UtcNow.AddSeconds(8) };
            Bump();
            string err = await TdarrClient.LimitAsync(mgr.Settings, n, type, more);
            if (err != null) { Waiting.Remove(key); Bump(); MessageBox.Show("Tdarr: " + err, "Tdarr", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // ---- library totals ----
        static void BuildStats(Theme t, TdarrSnapshot s, StackPanel root)
        {
            root.Children.Add(Heading(t, "LIBRARY"));
            if (s.StatsError.Length > 0 && s.Stats.Files == 0)
            {
                var e = T(t, "Couldn't read the totals: " + s.StatsError, Busy, 0.85);
                e.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(e);
                return;
            }
            TdarrStats st = s.Stats;
            var wrap = new WrapPanel { Margin = new Thickness(2, 0, 0, 2) };
            Action<string, string, Brush, string> cell = delegate (string number, string what, Brush b, string tip)
            {
                var c = new StackPanel { Margin = new Thickness(0, 0, 16, 2), ToolTip = tip };
                c.Children.Add(T(t, number, b, 1.05, true));
                c.Children.Add(T(t, what, t.FgDim, 0.75));
                wrap.Children.Add(c);
            };
            cell(st.Queue.ToString(), "in the queue", st.Queue > 0 ? (Brush)t.Fg : t.FgDim, "Transcode queue: " + st.TranscodeQueue + "\nHealth check queue: " + st.HealthQueue);
            cell((st.TranscodeDone + st.HealthDone).ToString(), "done", Good, "Transcoded or not needed: " + st.TranscodeDone + "\nHealthy: " + st.HealthDone);
            cell(st.Errors.ToString(), "errors", st.Errors > 0 ? Bad : t.FgDim, "Transcode errors / cancelled: " + st.TranscodeErrors + "\nHealth check errors: " + st.HealthErrors +
                 "\n\nTdarr's Staged / Error tables have the files");
            if (st.SavedGb != 0) cell(TdarrClient.InfoBytes(st.SavedGb), st.SavedGb > 0 ? "saved" : "larger", st.SavedGb > 0 ? Good : Busy, "Space saved by transcoding, across " + st.Files + " files");
            root.Children.Add(wrap);
        }

        static void Add(ContextMenu m, string header, Action click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += delegate { click(); };
            m.Items.Add(mi);
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
            try { return Questions.YesNo(owner, question, "Tdarr") == MessageBoxResult.Yes; }
            finally { if (host != null) host.Pinned = false; _asking = MenuOpen = false; }
        }
    }
}
