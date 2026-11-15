using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Downloads on a bar (or a widget set to Graph): the download speed of qBittorrent and / or SABnzbd as text plus a graph, ring
    /// (overall progress) or tile. Click it for the list of downloads in a popup.
    /// </summary>
    public sealed class DownloadsModule : InfoModule, IMultiRow
    {
        PopupWindow _popup;
        DateTime _popupShown = DateTime.MinValue;

        public DownloadsModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg)
        {
            DownloadDrop.Attach(this, null, delegate (List<string> lines) { DownloadToast.Lines(mgr, lines); });
        }

        protected override bool UsesGraph { get { return true; } }
        protected override bool HasGauges { get { return true; } }
        protected override bool Clickable { get { return true; } }
        public int RowCount { get { return TallTile ? 0 : 1; } }

        /// <summary>The download arrow in the graph colour, the upload arrow in the upload colour (as the network module).</summary>
        protected override void Decorate(TextBlock txt)
        {
            Theme t = ModTheme ?? Bar.Theme;
            string s = txt.Text ?? "";
            if (s.IndexOf('↓') < 0 && s.IndexOf('↑') < 0) return;
            Brush down = t.Resolve(Cfg.Color, t.MainAccent), up = t.Resolve(Cfg.Color2, t.SecondaryAccent);
            txt.Inlines.Clear();
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '↓' && s[i] != '↑') continue;
                if (i > start) txt.Inlines.Add(new Run(s.Substring(start, i - start)));
                txt.Inlines.Add(new Run(s[i].ToString()) { Foreground = s[i] == '↓' ? down : up });
                start = i + 1;
            }
            if (start < s.Length) txt.Inlines.Add(new Run(s.Substring(start)));
        }

        protected override void Read(Reading r, bool addSample)
        {
            DlSnapshot snap = Downloads.Latest;
            var clients = new List<DlClientState>(snap.Clients(DlView.Of(Cfg).Source));
            if (clients.Count == 0)
            {
                r.Text = "Downloads: set up in Settings > Plugins";
                r.Tip = "Add qBittorrent and / or SABnzbd on the Plugins page of Settings.";
                r.Label = "Downloads"; r.Number = "-"; r.Unit = "";
                return;
            }
            double down = 0, up = 0, size = 0, left = 0;
            int open = 0, active = 0;
            bool anyOk = false, allPaused = true;
            var tip = new List<string>();
            foreach (DlClientState c in clients)
            {
                anyOk |= c.Ok;
                down += c.DownSpeed; up += c.UpSpeed;
                List<DlItem> un = c.Unfinished;
                open += un.Count;
                foreach (DlItem i in un) { size += i.Size; left += i.Left; if (i.Active) active++; }
                if (!c.Paused) allPaused = false;
                string line = c.Client + ": " + (c.Ok ? "↓ " + Downloads.Speed(c.DownSpeed) + (c.Client == "qBittorrent" ? "  ↑ " + Downloads.Speed(c.UpSpeed) : "") +
                                                        ", " + un.Count + " in the queue" + (c.Paused ? " - paused" + (c.PausedFor.Length > 0 ? " (" + c.PausedFor + ")" : "") : "") +
                                                        (c.AltSpeed ? " - alternative speed limits on" : "") +
                                                        (c.FreeSpace >= 0 ? ", " + Downloads.Size(c.FreeSpace) + " free" : "")
                                                      : "not reached yet");
                if (c.Error.Length > 0) line += "\n   Last try failed: " + c.Error;
                tip.Add(line);
            }
            if (clients.Count > 0 && open == 0) allPaused = false;
            double progress = size > 0 ? Math.Max(0, Math.Min(1, 1 - left / size)) : 0;
            tip.Add("Click for the list");
            r.Tip = string.Join("\n", tip);
            r.Hide = Cfg.HideIfNone && anyOk && open == 0;

            if (!anyOk)
            {
                r.Text = clients[0].Error.Length > 0 ? "Downloads n/a" : "Downloads…";
                r.Label = "Downloads"; r.Number = "-"; r.Unit = "";
                return;
            }
            bool upload = DlView.Of(Cfg).ShowUpload && DlView.Of(Cfg).Source != "SABnzbd";     // SABnzbd doesn't upload
            string speed = "↓ " + Downloads.Speed(down) + (upload ? "  ↑ " + Downloads.Speed(up) : "");
            string count = open == 0 ? "idle" : (allPaused ? "paused" : open + (size > 0 ? " · " + Math.Floor(progress * 100) + "%" : ""));
            r.Text = Prefix("") + speed + (DlView.Of(Cfg).ShowCount ? (Cfg.ShownLines2 ? "\n" : "   ") + count : "");

            bool ring = Cfg.Style == "Ring";
            double mb = down / 1048576.0;
            if (!ring && Cfg.AutoScale)
            {
                // the speeds in MB/s - vShell scales the graph to the fastest it shows
                r.AutoScale = true;
                r.ScaleMin = 0.1;
                r.Percent = mb;
                if (upload) r.Percent2 = up / 1048576.0;
                if (GraphScale > 0) tip.Insert(Math.Max(0, tip.Count - 1), "Graph: up to " + GraphScale.ToString("0.##", CultureInfo.CurrentCulture) + " MB/s");
                r.Tip = string.Join("\n", tip);
            }
            else
            {
                r.Percent = ring ? progress * 100 : Math.Max(0, Math.Min(100, mb / Math.Max(0.1, Cfg.RangeMax) * 100));
                if (!ring && upload) r.Percent2 = Math.Max(0, Math.Min(100, up / 1048576.0 / Math.Max(0.1, Cfg.RangeMax) * 100));
            }
            r.Label = "Downloads";
            if (ring)
            {
                r.Number = open == 0 ? "-" : Math.Floor(progress * 100).ToString("0", CultureInfo.CurrentCulture);
                r.Unit = open == 0 ? "" : "%";
                r.Detail = "↓ " + Downloads.Speed(down);
            }
            else
            {
                // Tile: the download speed as the big number (with its arrow), the upload speed and the count on the line above it
                string sp = Downloads.Speed(down);
                int cut = sp.IndexOf(' ');
                r.Number = "↓ " + (cut > 0 ? sp.Substring(0, cut) : sp);
                r.Unit = cut > 0 ? sp.Substring(cut) : "";
                var detail = new List<string>();
                if (DlView.Of(Cfg).ShowUpload) detail.Add("↑ " + Downloads.Speed(up));
                if (DlView.Of(Cfg).ShowCount) detail.Add(count);
                r.Detail = string.Join(" · ", detail);
            }

            // the popup follows (no more than once a second)
            if (_popup != null && _popup.IsOpen && !DownloadsList.MenuOpen && (DateTime.UtcNow - _popupShown).TotalSeconds >= 1)
            {
                _popupShown = DateTime.UtcNow;
                _popup.SetContent(DownloadsList.Build(Mgr, ModTheme ?? Bar.Theme, Cfg, snap, false, delegate { RefreshPopup(); }));
            }
        }

        void RefreshPopup()
        {
            if (_popup == null || !_popup.IsOpen) return;
            _popup.SetContent(DownloadsList.Build(Mgr, ModTheme ?? Bar.Theme, Cfg, Downloads.Latest, false, delegate { RefreshPopup(); }));
        }

        protected override void OnClick()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            Theme t = ModTheme ?? Bar.Theme;
            _popup = new PopupWindow(Mgr, Bar, this, t, DownloadsList.Build(Mgr, t, Cfg, Downloads.Latest, false, delegate { RefreshPopup(); }));
            if (Mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(t, Mgr.Settings.WidgetStyle);
            _popupShown = DateTime.UtcNow;
            Mgr.ShowPopup(_popup);
        }
    }

    /// <summary>Downloads in a widget, as a list: each client's speeds, then its downloads with progress; click one to pause / resume it.</summary>
    public sealed class DownloadsListModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        DateTime _shownAt = DateTime.MinValue;
        int _shownPending = -1;
        // the speed over the last minutes along the top, and the list under it
        readonly History _hist;
        readonly MetricView _graph = new MetricView();
        readonly Border _list = new Border();
        readonly TextBlock _scale;

        public int RowCount { get { return 0; } }

        public DownloadsListModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(4, 2, 4, 2);
            _hist = mgr.HistoryFor(cfg.Id);
            _graph.Data = _hist;
            _graph.GraphStyle = "Line";
            _graph.Fill = _t.Resolve(cfg.Color, _t.MainAccent);
            _graph.Track = _t.Track;
            _graph.ApplyLook(cfg, _t);      // the graph look rows (Settings: line, fill, box, grid, time shown, colour by load)
            _graph.GraphW = 40;
            _graph.GraphH = Math.Max(14, Math.Round(_t.FontSize * 2.2));
            _graph.HorizontalAlignment = HorizontalAlignment.Stretch;
            _scale = new TextBlock
            {
                Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = Math.Max(7, _t.FontSize * 0.75), Margin = new Thickness(0, 2, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Text = cfg.RangeMax.ToString("0.#", CultureInfo.CurrentCulture) + " MB/s"
            };
            if (cfg.AutoScale)
            {
                _graph.FullScale = MetricView.ScaleFor(_hist, null, 0.1, cfg.GraphSpan);
                _scale.Text = _graph.FullScale.ToString("0.##", CultureInfo.CurrentCulture) + " MB/s";
            }
            var top = new Grid { Margin = new Thickness(0, 0, 0, 6), Visibility = cfg.ShowText ? Visibility.Visible : Visibility.Collapsed };
            top.Children.Add(_graph);
            top.Children.Add(_scale);
            var col = new StackPanel();
            col.Children.Add(top);
            col.Children.Add(_list);
            Child = col;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(false); };
            Loaded += delegate { Refresh(true); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh(true);
            DownloadDrop.Attach(this, delegate (bool on) { Background = on ? Theme.Faded(_t.MainAccent, 18) : Brushes.Transparent; },
                                delegate (List<string> lines) { DownloadToast.Lines(mgr, lines); Refresh(true); });
        }

        void Refresh(bool force)
        {
            DlSnapshot s = Downloads.Latest;
            if (!force)
            {
                // one speed sample a second for the graph (of the clients this module shows)
                double down = 0;
                foreach (DlClientState c in s.Clients(DlView.Of(_cfg).Source)) down += c.DownSpeed;
                if (_cfg.AutoScale)
                {
                    // MB/s, with the scale (and its label) following the fastest of the time shown
                    _hist.Add(down / 1048576.0);
                    _graph.FullScale = MetricView.ScaleFor(_hist, null, 0.1, _cfg.GraphSpan);
                    _scale.Text = _graph.FullScale.ToString("0.##", CultureInfo.CurrentCulture) + " MB/s";
                }
                else _hist.Add(Math.Max(0, Math.Min(100, down / 1048576.0 / Math.Max(0.1, _cfg.RangeMax) * 100)));
                _graph.InvalidateVisual();
            }
            if (!force && s.Fetched == _shownAt && DownloadsList.PendingVersion == _shownPending) return;
            if (DownloadsList.MenuOpen) return;
            _shownAt = s.Fetched;
            _shownPending = DownloadsList.PendingVersion;
            _list.Child = DownloadsList.Build(_mgr, _t, _cfg, s, true, delegate { Refresh(true); });
            _host.ContentChanged();
        }
    }

    /// <summary>
    /// Drop .torrent / .nzb files from File Explorer onto a run box or a Downloads module: they go to qBittorrent / SABnzbd (whichever is
    /// set up for that kind). Other files are left alone (the drop is refused for them).
    /// </summary>
    public static class DownloadDrop
    {
        static List<string> Files(DragEventArgs e)
        {
            var list = new List<string>();
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return list;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return list;
            foreach (string f in files) if (Downloads.FileKind(f).Length > 0) list.Add(f);
            return list;
        }

        /// <summary>highlight(true / false) while files that can be added are over it; done gets one line per file when they have gone.</summary>
        public static void Attach(FrameworkElement el, Action<bool> highlight, Action<List<string>> done)
        {
            el.AllowDrop = true;
            DragEventHandler over = delegate (object s, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;      // not files: leave it to the bar (tray icons, launcher)
                bool ok = Files(e).Count > 0;
                e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
                if (highlight != null) highlight(ok);
            };
            // the preview (tunnelling) events, so a text box inside (the run box) doesn't take the files first
            el.PreviewDragEnter += over;
            el.PreviewDragOver += over;
            el.PreviewDragLeave += delegate (object s, DragEventArgs e)
            {
                // moving onto a child (the run box's text) is not leaving
                Point p = e.GetPosition(el);
                if (p.X >= 0 && p.Y >= 0 && p.X < el.ActualWidth && p.Y < el.ActualHeight) return;
                if (highlight != null) highlight(false);
            };
            el.PreviewDrop += async delegate (object s, DragEventArgs e)
            {
                if (highlight != null) highlight(false);
                List<string> files = Files(e);
                if (files.Count == 0) return;
                e.Handled = true;
                List<string> lines = await Downloads.AddFilesAsync(files);
                if (done != null) done(lines);
            };
        }
    }

    /// <summary>The list of downloads (popup and widget).</summary>
    public static class DownloadsList
    {
        static readonly Brush Good = StatusColors.Good, Warn = StatusColors.Warn, Bad = StatusColors.Bad;
        static Brush Frozen(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        /// <summary>Pause / resume sent but not showing yet: id -> wanted "paused" state, until it shows or 8 s pass.</summary>
        sealed class Pending { public bool Pause; public DateTime Until; }
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();
        public static int PendingVersion;
        public static bool MenuOpen;

        static void Want(string key, bool pause)
        {
            Waiting[key] = new Pending { Pause = pause, Until = DateTime.UtcNow.AddSeconds(8) };
            PendingVersion++;
        }

        /// <summary>The paused state to show: what was asked for, until the client shows it (or it took too long).</summary>
        static bool ShownPaused(string key, bool actual)
        {
            Pending p;
            if (!Waiting.TryGetValue(key, out p)) return actual;
            if (p.Pause == actual || DateTime.UtcNow > p.Until) { Waiting.Remove(key); PendingVersion++; return actual; }
            return p.Pause;
        }

        static TextBlock T(Theme t, string text, Brush b, double scale = 1, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = t.Font, FontSize = Math.Max(7, t.FontSize * scale), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        static FrameworkElement Meter(Theme t, double fraction, Brush fill, double height)
        {
            fraction = Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction));
            var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
            var track = new Border { Background = t.Track, CornerRadius = new CornerRadius(height / 2) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (fraction > 0.004) g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
            return g;
        }

        /// <summary>A small text button (Pause all, Alt speed, Open web UI).</summary>
        static Border Pill(Theme t, string glyph, string text, string tip, bool on, Action click)
        {
            return Pills.Make(t, glyph, text, tip, false, click, on);
        }

        static List<DlItem> Pick(DlClientState c, ModuleConfig cfg)
        {
            List<DlItem> list;
            switch (DlView.Of(cfg).Show)
            {
                case "Active": list = c.Items.FindAll(delegate (DlItem i) { return i.Active; }); break;
                case "All": list = new List<DlItem>(c.Items); break;
                default: list = c.Unfinished; break;
            }
            // downloading first, then checking / queued, then paused, then finished (each in the client's own order)
            Func<DlItem, int> rank = delegate (DlItem i)
            {
                if (i.Error) return 1;
                if (i.Done) return 4;
                if (i.Paused) return 3;
                if (i.Active) return 0;
                return 2;
            };
            var order = new Dictionary<DlItem, int>();
            for (int k = 0; k < list.Count; k++) order[list[k]] = k;
            list.Sort(delegate (DlItem a, DlItem b)
            {
                int d = rank(a).CompareTo(rank(b));
                return d != 0 ? d : order[a].CompareTo(order[b]);
            });
            return list;
        }

        static string WebUrl(BarSettings s, string client)
        {
            string u = client == "SABnzbd" ? DlSettings.Of(s).SabUrl : DlSettings.Of(s).QbUrl;
            u = u.Trim();
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            return u;
        }

        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open " + url + ": " + ex.Message); }
        }

        /// <summary>
        /// The list for a snapshot. widget = inside a widget (fills the widget's width; a speed graph at the top) rather than a popup
        /// (fixed width). changed = redraw now (after a click).
        /// </summary>
        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, DlSnapshot snap, bool widget, Action changed)
        {
            BarSettings s = mgr.Settings;
            var root = new StackPanel();
            if (!widget) { root.Width = 400; root.Margin = new Thickness(4); }
            var clients = new List<DlClientState>(snap.Clients(DlView.Of(cfg).Source));
            if (clients.Count == 0)
            {
                var none = T(t, "Add qBittorrent and / or SABnzbd on the Plugins page of Settings.", t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(none);
                return root;
            }

            bool firstClient = true;
            foreach (DlClientState c in clients)
            {
                // ---- the client: name, speeds, pause all / alt speed / web UI ----
                var head = new DockPanel { Margin = new Thickness(0, firstClient ? 0 : 10, 0, 4), LastChildFill = true };
                firstClient = false;
                string client = c.Client;
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(buttons, Dock.Right);
                if (c.Ok)
                {
                    bool paused = ShownPaused(client + "|*", c.Paused);
                    buttons.Children.Add(Pill(t, paused ? "" : "", paused ? "Resume" : "Pause", paused ? "Resume everything in " + client : "Pause everything in " + client, paused,
                        delegate
                        {
                            Want(client + "|*", !paused);
                            Downloads.Pause(client, null, !paused);
                            changed();
                        }));
                    if (client == "qBittorrent")
                        buttons.Children.Add(Pill(t, "", "", c.AltSpeed ? "Alternative speed limits are on - click to turn them off" : "Turn on the alternative speed limits",
                                                  c.AltSpeed, delegate { Downloads.QbToggleAltSpeed(); }));
                }
                string web = WebUrl(s, client);
                buttons.Children.Add(Pill(t, "", "", "Open " + client + "'s web page", false, delegate { OpenUrl(web); }));
                head.Children.Add(buttons);

                var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                title.Children.Add(T(t, client, t.Fg, 1, true));
                if (c.Ok)
                {
                    title.Children.Add(T(t, "   ↓ " + Downloads.Speed(c.DownSpeed), t.Fg, 0.9));
                    if (client == "qBittorrent") title.Children.Add(T(t, "  ↑ " + Downloads.Speed(c.UpSpeed), t.FgDim, 0.9));
                }
                head.Children.Add(title);
                root.Children.Add(head);

                if (!c.Ok)
                {
                    var err = T(t, c.Error.Length > 0 ? "Can't reach it: " + c.Error : "Connecting…", c.Error.Length > 0 ? Bad : t.FgDim, 0.9);
                    err.TextWrapping = TextWrapping.Wrap;
                    root.Children.Add(err);
                    continue;
                }
                var info = new List<string>();
                if (c.Paused) info.Add("Paused" + (c.PausedFor.Length > 0 ? " for " + c.PausedFor : ""));
                if (c.AltSpeed) info.Add("Alternative speed limits on");
                if (c.FreeSpace >= 0) info.Add(Downloads.Size(c.FreeSpace) + " free");
                if (c.Error.Length > 0) info.Add("Last update failed: " + c.Error);
                if (info.Count > 0)
                {
                    var line = T(t, string.Join(" · ", info), c.Error.Length > 0 ? Warn : t.FgDim, 0.85);
                    line.Margin = new Thickness(0, -2, 0, 4);
                    root.Children.Add(line);
                }

                // ---- the downloads ----
                List<DlItem> items = Pick(c, cfg);
                if (items.Count == 0)
                {
                    root.Children.Add(T(t, DlView.Of(cfg).Show == "Active" ? "Nothing downloading" : "Nothing in the queue", t.FgDim, 0.9));
                    continue;
                }
                int max = Math.Max(1, DlView.Of(cfg).Count);
                for (int k = 0; k < items.Count && k < max; k++) root.Children.Add(ItemRow(t, items[k], changed));
                if (items.Count > max)
                {
                    var more = T(t, "+ " + (items.Count - max) + " more", t.FgDim, 0.85);
                    more.Margin = new Thickness(2, 2, 0, 0);
                    more.Cursor = Cursors.Hand;
                    more.ToolTip = "Open " + client + "'s web page";
                    more.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                    more.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenUrl(web); };
                    root.Children.Add(more);
                }
            }
            return root;
        }

        static FrameworkElement ItemRow(Theme t, DlItem it, Action changed)
        {
            string key = it.Client + "|" + it.Id;
            bool paused = ShownPaused(key, it.Paused);
            bool waiting = paused != it.Paused;

            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = T(t, it.Name, it.Done ? t.FgDim : t.Fg, 0.95);
            Grid.SetColumn(name, 0);
            g.Children.Add(name);
            string pct = it.Done ? "✓" : Math.Floor(it.Progress * 1000) / 10 + "%";
            var p = T(t, pct, it.Done ? Good : t.Fg, 0.9);
            p.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(p, 1);
            g.Children.Add(p);

            Brush fill = it.Error ? Bad : paused ? t.FgDim : it.Done ? Good : it.StateText == "stalled" ? Warn : t.MainAccent;
            var meter = Meter(t, it.Progress, fill, 4);
            meter.Margin = new Thickness(0, 3, 0, 2);
            Grid.SetRow(meter, 1);
            Grid.SetColumnSpan(meter, 2);
            g.Children.Add(meter);

            var bits = new List<string>();
            if (waiting) bits.Add(paused ? "pausing…" : "resuming…");
            else bits.Add(it.StateText);
            if (!it.Done && it.Size > 0) bits.Add(Downloads.Size(Math.Max(0, it.Size - it.Left)) + " / " + Downloads.Size(it.Size));
            else if (it.Size > 0) bits.Add(Downloads.Size(it.Size));
            if (!paused && !it.Done && it.Eta >= 0) bits.Add(Downloads.Eta(it.Eta) + " left");
            if (!paused && it.DownSpeed > 0) bits.Add("↓ " + Downloads.Speed(it.DownSpeed));
            if (it.Client == "qBittorrent" && it.UpSpeed > 0) bits.Add("↑ " + Downloads.Speed(it.UpSpeed));
            var sub = T(t, string.Join(" · ", bits), it.Error ? Bad : t.FgDim, 0.8);
            Grid.SetRow(sub, 2);
            Grid.SetColumnSpan(sub, 2);
            g.Children.Add(sub);

            var tip = new List<string> { it.Name };
            if (it.Category.Length > 0) tip.Add("Category: " + it.Category);
            if (it.Client == "qBittorrent") tip.Add("Seeds " + it.Seeds + ", peers " + it.Peers + ", ratio " + it.Ratio.ToString("0.00", CultureInfo.CurrentCulture));
            tip.Add(it.Client + " - " + it.State);
            if (Downloads.Forced(it)) tip.Add("Forced to start (ignores the queue)");
            if (!it.Done || it.Client == "qBittorrent") tip.Add(paused ? "Click to resume" : "Click to pause");
            if (it.Client == "qBittorrent") tip.Add("Right-click: force start, recheck, reannounce, top of the queue, remove");

            var row = new Border { Child = g, Padding = new Thickness(5, 3, 5, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = string.Join("\n", tip) };
            bool canClick = !it.Done || it.Client == "qBittorrent";
            if (canClick)
            {
                row.Cursor = Cursors.Hand;
                row.MouseEnter += delegate { row.Background = t.Hover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    Want(key, !paused);
                    Downloads.Pause(it.Client, it, !paused);
                    changed();
                };
            }
            if (it.Client == "qBittorrent")
                row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TorrentMenu(row, it, paused, key, changed); };
            return row;
        }

        // ---- a torrent's menu ----
        static void TorrentMenu(FrameworkElement target, DlItem it, bool paused, string key, Action changed)
        {
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = it.Name, IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, paused ? "Resume" : "Pause", false, delegate { Want(key, !paused); Downloads.Pause(it.Client, it, !paused); changed(); });
            Add(m, "Force start (ignore the queue)", Downloads.Forced(it), delegate { Act(target, it, "forcestart"); });
            Add(m, "Move to the top of the queue", false, delegate { Act(target, it, "top"); });
            Add(m, "Recheck the downloaded data", false, delegate { Act(target, it, "recheck"); });
            Add(m, "Reannounce to the trackers", false, delegate { Act(target, it, "reannounce"); });
            m.Items.Add(new Separator());
            Add(m, "Remove (keep the files)…", false, delegate
            {
                if (Ask(target, "Remove \"" + it.Name + "\" from qBittorrent?\n\nThe downloaded files stay where they are.")) Act(target, it, "remove");
            });
            Add(m, "Remove and delete the files…", false, delegate
            {
                if (Ask(target, "Remove \"" + it.Name + "\" from qBittorrent and DELETE its files from disk?\n\nThis can't be undone.")) Act(target, it, "removefiles");
            });
            m.Items.Add(new Separator());
            Add(m, "Copy the name", false, delegate { try { Clipboard.SetText(it.Name); } catch { } });
            ShowMenu(m, target);
        }

        static async void Act(FrameworkElement target, DlItem it, string action)
        {
            MenuOpen = false;
            string err = await Downloads.QbActionAsync(it, action);
            PendingVersion++;
            if (err != null) Questions.Tell(Window.GetWindow(target), "qBittorrent: " + err, "Downloads");
        }

        static void Add(ContextMenu m, string header, bool check, Action click)
        {
            var mi = new MenuItem { Header = header, IsCheckable = check, IsChecked = check };
            mi.Click += delegate { click(); };
            m.Items.Add(mi);
        }

        static bool _asking;

        static void ShowMenu(ContextMenu m, FrameworkElement target)
        {
            PopupWindow host = Window.GetWindow(target) as PopupWindow;
            if (host != null) host.Pinned = true;       // the menu must not close the popup
            MenuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(delegate { if (!_asking) MenuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        static bool Ask(FrameworkElement target, string question)
        {
            Window owner = target != null ? Window.GetWindow(target) : null;
            PopupWindow host = owner as PopupWindow;
            if (host != null) host.Pinned = true;
            _asking = MenuOpen = true;
            try { return Questions.YesNo(owner, question, "Downloads") == MessageBoxResult.Yes; }
            finally { if (host != null) host.Pinned = false; _asking = MenuOpen = false; }
        }
    }

    /// <summary>Feedback lines ("✓ x.nzb added to SABnzbd") as a notification card (not kept in the history).</summary>
    public static class DownloadToast
    {
        public static void Lines(BarManager mgr, List<string> lines)
        {
            if (lines == null) return;
            foreach (string l in lines) Notify.Post("Downloads", l, "", "", Notify.KindOf(l.StartsWith("✕") ? "Downloads.Failed" : "Downloads.Done"), false);
        }
    }
}
