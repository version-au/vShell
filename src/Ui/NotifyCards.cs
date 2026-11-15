using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The notification cards: one per source (stacked) or one for everything, in the corner and on the monitor chosen in Settings >
    /// Notifications. A card goes after the chosen time (not while the mouse is over it, and not while it shows an important one with
    /// "keep important ones" on). Click a line to open what it is about.
    /// </summary>
    public static class NotifyCards
    {
        static BarManager _mgr;
        static bool _hooked;
        static DispatcherTimer _tick;
        /// <summary>The open cards, oldest first (that's the stacking order).</summary>
        static readonly List<NotifyCard> Cards = new List<NotifyCard>();

        public static void Hook(BarManager mgr)
        {
            _mgr = mgr;
            if (_hooked) return;
            _hooked = true;
            Dispatcher ui = Application.Current.Dispatcher;
            Notify.FullScreenNow = delegate
            {
                // the list belongs to the UI thread; ask it there (quickly)
                try { return ui.CheckAccess() ? mgr.FullScreenActive : (bool)ui.Invoke(new Func<bool>(delegate { return mgr.FullScreenActive; }), TimeSpan.FromMilliseconds(500)); }
                catch { return false; }
            };
            Notify.Show += delegate (NotifyItem item) { ui.BeginInvoke(new Action(delegate { Add(item); })); };
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _tick.Tick += delegate
            {
                System.Threading.Tasks.Task.Run(delegate { try { Notify.ReleaseHeld(); } catch { } });
            };
            _tick.Start();
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern bool PlaySound(string name, IntPtr module, uint flags);

        static void Add(NotifyItem item)
        {
            try
            {
                BarSettings s = _mgr.Settings;
                // By source: a card per source - and an important one (that stays until closed) on a card of its own, so the
                // ordinary ones next to it still go after their time. One card: everything together (each line goes after its own time).
                NotifyLevel level = item.Kind;
                // a repeat (the count went up): update its line where it is, instead of another line / card
                NotifyCard showing = Cards.Find(delegate (NotifyCard c) { return c.IsLoaded && c.HasLine(item.Id); });
                if (showing != null) { showing.UpdateLine(item); Relayout(); return; }
                bool one = s.NotifyGroup == "One card";
                // By source: normal ones share the source's card; important ones and alerts each get a card of their own
                string key = one ? "" : item.Source + (level != NotifyLevel.Normal ? "|" + level : "");
                NotifyCard card = Cards.Find(delegate (NotifyCard c) { return c.Key == key && c.IsLoaded; });
                bool fresh = card == null;
                if (fresh)
                {
                    card = new NotifyCard(_mgr, key, one ? "" : item.Source, one ? (NotifyLevel?)null : level);
                    card.Closed += delegate { Cards.Remove(card); Relayout(); };
                    Cards.Add(card);
                }
                card.AddLine(item);
                if (fresh) card.Show();
                Relayout();
                if (s.NotifySound) try { PlaySound("Notification.Default", IntPtr.Zero, 0x00010000 | 0x0001 | 0x0002); } catch { }   // SND_ALIAS | SND_ASYNC | SND_NODEFAULT
            }
            catch (Exception ex) { Log.Write("Notification card: " + ex.Message); }
        }

        /// <summary>Put the cards in the chosen corner, stacked away from it in the order they opened.</summary>
        public static void Relayout()
        {
            if (_mgr == null) return;
            BarSettings s = _mgr.Settings;
            MonitorInfo m = null;
            if (s.NotifyMonitor == "Mouse") { POINT p = Native.CursorPos(); m = MonitorHelper.FromPoint(p.X, p.Y); }
            else if (s.NotifyMonitor != "Main")
            {
                // a chosen monitor, found by its stable key first (like the bars and widgets), then by name; the main one when it isn't connected
                m = MonitorHelper.Find(s.NotifyMonitor, s.NotifyMonitorKey);
                if (m != null && string.IsNullOrEmpty(s.NotifyMonitorKey) && !string.IsNullOrEmpty(m.Key)) s.NotifyMonitorKey = m.Key;   // older settings: remember its key
                else if (m != null && !string.Equals(m.DeviceName, s.NotifyMonitor, StringComparison.OrdinalIgnoreCase)) s.NotifyMonitor = m.DeviceName;   // renumbered: follow it
            }
            if (m == null) m = MonitorHelper.Primary();
            if (m == null) return;
            bool top = s.NotifyCorner.StartsWith("Top"), left = s.NotifyCorner.EndsWith("left");
            double edge = 8;
            double y = double.NaN;
            foreach (NotifyCard c in Cards)
            {
                if (!c.IsLoaded || c.ActualHeight <= 0) continue;
                var src = PresentationSource.FromVisual(c);
                double sx = src != null ? src.CompositionTarget.TransformToDevice.M11 : 1, sy = src != null ? src.CompositionTarget.TransformToDevice.M22 : 1;
                double l = m.WorkArea.Left / sx, r = m.WorkArea.Right / sx, t = m.WorkArea.Top / sy, b = m.WorkArea.Bottom / sy;
                c.Left = left ? l + edge : r - c.ActualWidth - edge;
                if (double.IsNaN(y)) y = top ? t + edge : b - edge;
                if (top) { c.Top = y; y += c.ActualHeight - 8; }        // the cards' 12 px margins overlap a little
                else { c.Top = y - c.ActualHeight; y -= c.ActualHeight - 8; }
            }
        }

        /// <summary>The plugins' own notification links ("vshell:ha-toggle:light.kitchen"): true = it was theirs (and done).</summary>
        public static readonly List<Func<string, bool>> LinkHandlers = new List<Func<string, bool>>();
        static Func<string, bool>[] LinkHandlersNow { get { lock (LinkHandlers) return LinkHandlers.ToArray(); } }

        /// <summary>Open what a notification is about.</summary>
        public static void Open(BarManager mgr, NotifyItem item)
        {
            if (item.Id > 0) Notify.MarkRead(item.Id);
            string url = item.Url ?? "";
            if (url.Length == 0) return;
            try
            {
                if (url == "vshell:history") { NotifyHistoryWindow.ShowNear(mgr); return; }
                if (url == "vshell:taskmgr") { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); return; }
                foreach (Func<string, bool> handler in LinkHandlersNow) if (handler(url)) return;     // a plugin's own link (vshell:ha-toggle:…)
                if (url.StartsWith("vshell:")) return;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("Notification open " + url + ": " + ex.Message); }
        }

        /// <summary>Stays until closed (Important / Alert with their "stay" setting on).</summary>
        public static bool Sticky(BarSettings s, NotifyLevel level)
        {
            return (level == NotifyLevel.Important && s.NotifyKeepImportant) || (level == NotifyLevel.Alert && s.NotifyKeepAlerts);
        }

        /// <summary>The title colour: Normal = the text colour, Important = yellow, Alert = red.</summary>
        /// <summary>A notification's picture (a file), read once and frozen; null when there is none or it can't be read.</summary>
        [PluginApi] public static ImageSource Picture(string file, int decodeWidth)
        {
            if (string.IsNullOrEmpty(file)) return null;
            try
            {
                if (!System.IO.File.Exists(file)) return null;
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;       // the file is not kept open
                bi.UriSource = new Uri(file);
                bi.DecodePixelWidth = decodeWidth;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static Brush TitleBrush(NotifyLevel level, Theme t)
        {
            return level == NotifyLevel.Normal ? t.Fg : StatusColors.Of(level);
        }

        /// <summary>The level's colour as a thin bar at a line's left edge.</summary>
        public static Border LevelBar(NotifyLevel level, bool dim)
        {
            return new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = StatusColors.Of(level), Margin = new Thickness(0, 1, 8, 1), Opacity = dim ? 0.5 : 1 };
        }

        /// <summary>The card look (the widget style when there is one), shared with the history window.</summary>
        /// <summary>The background effect for the cards and the history: the Appearance one, unless turned off for notifications.</summary>
        public static string BackdropMode(BarManager mgr)
        {
            BarSettings s = mgr.Settings;
            return s.NotifyBackdrop ? BarSettings.BlurFor(s, "Theme") : "Off";
        }

        public static Border CardBorder(BarManager mgr, Theme t)
        {
            var card = new Border
            {
                Background = t.PopupBg, BorderBrush = Theme.Faded(t.MainAccent, 70), BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(12), Width = 340,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4 }
            };
            WidgetStyle ws = mgr.Settings.WidgetStyle;
            if (ws != null)
            {
                card.Background = t.MakeBg(-1);
                card.CornerRadius = new CornerRadius(Math.Max(0, ws.CornerRadius));
                card.BorderThickness = new Thickness(ws.ShowBorder ? Math.Max(0.5, Math.Min(12, ws.BorderThickness)) : 0);
                card.BorderBrush = Theme.Faded(t.Resolve(ws.BorderColor, t.MainAccent), ws.BorderOpacity);
                if (!ws.ShowShadow) card.Effect = null;
            }
            return card;
        }

        /// <summary>The glyph for a source.</summary>
        public static string Glyph(string source)
        {
            int c;
            switch (source)
            {
                case "Downloads": c = 0xE896; break;
                case "Komodo": c = 0xE7B8; break;
                case "3D printer": c = 0xE749; break;
                case "Seerr": c = 0xE8B2; break;
                case "Home Assistant": c = 0xE80F; break;
                case "Proxmox": c = 0xE7F4; break;
                case "Services": c = 0xE774; break;
                case "System": c = 0xE9D9; break;
                case "Jellyfin": c = 0xE768; break;
                case "Weather": c = 0xE753; break;     // cloud
                case "UniFi": c = 0xE701; break;       // wi-fi
                case "Protect": c = 0xE722; break;     // camera
                default: c = 0xEA8F; break;      // bell
            }
            return ((char)c).ToString();
        }
    }

    /// <summary>One card: a header (the source, or "Notifications") and up to six lines.</summary>
    public sealed class NotifyCard : Window
    {
        public readonly string Key;
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly StackPanel _lines = new StackPanel();
        readonly DispatcherTimer _timer;
        /// <summary>The source shown in the header ("" = one card for every source).</summary>
        readonly string _source;

        /// <summary>A line: when it goes (MaxValue = it stays until closed).</summary>
        sealed class LineInfo { public DateTime Until; public long Id; }

        public NotifyCard(BarManager mgr, string key, string source, NotifyLevel? level)
        {
            _mgr = mgr; Key = key; _source = source ?? "";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "vShell notification";
            Left = -32000; Top = -32000;

            _t = Theme.From(mgr.Settings, 12.5, 10);
            Border card = NotifyCards.CardBorder(mgr, _t);
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var close = new TextBlock { Text = "✕", Foreground = _t.FgDim, Cursor = Cursors.Hand, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Close (marks them read)" };
            close.MouseLeftButtonUp += delegate
            {
                // closing the card means you've seen them: they count as read (ones that just time out stay unread)
                foreach (UIElement el in _lines.Children)
                {
                    var fe = el as FrameworkElement;
                    var li = fe != null ? fe.Tag as LineInfo : null;
                    if (li != null && li.Id > 0) Notify.MarkRead(li.Id);
                }
                Close();
            };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock
            {
                Text = NotifyCards.Glyph(_source), FontFamily = IconUtil.GlyphFont, FontSize = 12.5, Foreground = level.HasValue ? StatusColors.Of(level.Value) : _t.MainAccent,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0)
            });
            title.Children.Add(new TextBlock { Text = _source.Length > 0 ? _source : "Notifications", Foreground = _t.Fg, FontFamily = _t.Font, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            head.Children.Add(title);
            var col = new StackPanel();
            col.Children.Add(head);
            col.Children.Add(_lines);
            card.Child = col;
            Content = PopupGlass.Wrap(mgr, _t, card, NotifyCards.BackdropMode(mgr), Styles.GlassOf(mgr.Settings.WidgetStyle));

            // each line goes after its own time (not while the mouse is over the card: then they all wait a little longer)
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
            _timer.Tick += delegate
            {
                DateTime now = DateTime.UtcNow;
                var gone = new List<FrameworkElement>();
                foreach (UIElement el in _lines.Children)
                {
                    var fe = el as FrameworkElement;
                    var li = fe != null ? fe.Tag as LineInfo : null;
                    if (li == null || li.Until == DateTime.MaxValue) continue;
                    if (IsMouseOver) { if (li.Until < now.AddSeconds(2)) li.Until = now.AddSeconds(2); }
                    else if (now >= li.Until) gone.Add(fe);
                }
                foreach (FrameworkElement fe in gone) RemoveLine(fe);
            };
            Loaded += delegate { NotifyCards.Relayout(); _timer.Start(); };
            SizeChanged += delegate { NotifyCards.Relayout(); };
        }

        public void AddLine(NotifyItem item)
        {
            _lines.Children.Add(Line(item));
            while (_lines.Children.Count > 6) RemoveLine((FrameworkElement)_lines.Children[0]);
        }

        /// <summary>This card shows that notification.</summary>
        public bool HasLine(long id)
        {
            foreach (UIElement el in _lines.Children)
            {
                var li = (el as FrameworkElement) != null ? ((FrameworkElement)el).Tag as LineInfo : null;
                if (li != null && li.Id == id && id > 0) return true;
            }
            return false;
        }

        /// <summary>It came again: the line shows the new count / text and starts its time again.</summary>
        public void UpdateLine(NotifyItem item)
        {
            for (int i = 0; i < _lines.Children.Count; i++)
            {
                var fe = _lines.Children[i] as FrameworkElement;
                var li = fe != null ? fe.Tag as LineInfo : null;
                if (li == null || li.Id != item.Id) continue;
                _lines.Children.RemoveAt(i);
                _lines.Children.Insert(i, Line(item));
                return;
            }
        }

        FrameworkElement Line(NotifyItem item)
        {
            NotifyLevel level = item.Kind;
            bool sticky = NotifyCards.Sticky(_mgr.Settings, level);
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Background = Brushes.Transparent, LastChildFill = true };
            var bar = NotifyCards.LevelBar(level, false);
            DockPanel.SetDock(bar, Dock.Left);
            line.Children.Add(bar);
            var text = new StackPanel();
            line.Children.Add(text);
            if (item.Url.Length > 0) { line.Cursor = Cursors.Hand; line.ToolTip = item.Url.StartsWith("vshell:") ? "Click to " + (item.Url == "vshell:history" ? "open the history" : item.Url == "vshell:restart" ? "restart vShell" : item.Url.StartsWith("vshell:ha-toggle") ? "toggle it" : "open it") : "Open " + item.Url; }
            text.Children.Add(new TextBlock
            {
                Text = item.TitleShown, FontFamily = _t.Font, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Foreground = NotifyCards.TitleBrush(level, _t)
            });
            string detail = item.Detail;
            if (_source.Length == 0 && item.Source.Length > 0) detail = item.Source + (detail.Length > 0 ? " · " + detail : "");
            if (!string.IsNullOrEmpty(detail))
                text.Children.Add(new TextBlock { Text = detail, FontFamily = _t.Font, FontSize = 11.5, Foreground = _t.FgDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            // a picture (a Protect camera snapshot) under the text
            ImageSource pic = NotifyCards.Picture(item.Image, 280);
            if (pic != null)
            {
                var img = new Image { Source = pic, Stretch = Stretch.Uniform, MaxWidth = 280, MaxHeight = 170, HorizontalAlignment = HorizontalAlignment.Left };
                var frame = new Border { Child = img, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 5, 0, 1), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
                img.Loaded += delegate { frame.Clip = new RectangleGeometry(new Rect(0, 0, img.ActualWidth, img.ActualHeight), 6, 6); };
                text.Children.Add(frame);
            }
            line.Tag = new LineInfo { Until = sticky ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(Math.Max(2, _mgr.Settings.NotifySeconds)), Id = item.Id };
            line.MouseLeftButtonUp += delegate
            {
                NotifyCards.Open(_mgr, item);
                RemoveLine(line);
            };
            return line;
        }

        void RemoveLine(FrameworkElement line)
        {
            _lines.Children.Remove(line);
            if (_lines.Children.Count == 0) Close();
        }
    }

    /// <summary>
    /// The notification history in a window of its own (the bell's "History", Settings, "+ N more…"): search, All / Unread, the levels,
    /// one source, Mark read / Clear for what's showing, and everything that's kept (a page at a time).
    /// </summary>
    public sealed class NotifyHistoryWindow : Window
    {
        static NotifyHistoryWindow _open;
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly Border _card;
        readonly TextBox _search;
        readonly StackPanel _list = new StackPanel();
        readonly ScrollViewer _scroll;
        readonly StackPanel _filters = new StackPanel();
        readonly TextBlock _count;
        // the filters (kept while the window is open; the list is built again as notifications change)
        bool _unreadOnly;
        readonly HashSet<NotifyLevel> _levels = new HashSet<NotifyLevel> { NotifyLevel.Normal, NotifyLevel.Important, NotifyLevel.Alert };
        string _source = "";
        int _show = 150;
        bool _clearArmed;
        readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

        public static void ShowNear(BarManager mgr)
        {
            if (_open != null && _open.IsLoaded) { _open.Activate(); return; }
            _open = new NotifyHistoryWindow(mgr);
            _open.Closed += delegate { _open = null; };
            _open.Show();
            _open.Activate();
        }

        NotifyHistoryWindow(BarManager mgr)
        {
            _mgr = mgr;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "vShell notifications";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _t = Theme.From(mgr.Settings, 12.5, 10);
            _card = NotifyCards.CardBorder(mgr, _t);
            _card.Width = 560;
            Content = PopupGlass.Wrap(mgr, _t, _card, NotifyCards.BackdropMode(mgr), Styles.GlassOf(mgr.Settings.WidgetStyle));

            var col = new DockPanel { LastChildFill = true };
            // ---- header ----
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var close = new TextBlock { Text = "✕", Foreground = _t.FgDim, Cursor = Cursors.Hand, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Close (Esc)", VerticalAlignment = VerticalAlignment.Center };
            close.MouseLeftButtonUp += delegate { Close(); };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            _count = new TextBlock { Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(_count, Dock.Right);
            head.Children.Add(_count);
            head.Children.Add(new TextBlock { Text = "Notifications", Foreground = _t.Fg, FontFamily = _t.Font, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(head, Dock.Top);
            col.Children.Add(head);

            // ---- search ----
            _search = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = _t.Fg, CaretBrush = _t.Fg, FontFamily = _t.Font, FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            var hint = new TextBlock { Text = "Search", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = 13, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
            _search.TextChanged += delegate { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _searchTimer.Stop(); _searchTimer.Start(); };
            _searchTimer.Tick += delegate { _searchTimer.Stop(); _show = 150; BuildList(); };
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sg.Children.Add(new TextBlock { Text = ((char)0xE721).ToString(), FontFamily = IconUtil.GlyphFont, FontSize = 12, Foreground = _t.FgDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetColumn(hint, 1); Grid.SetColumn(_search, 1);
            sg.Children.Add(hint); sg.Children.Add(_search);
            var searchBox = new Border { Child = sg, Background = Theme.Faded(_t.Fg, 10), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(searchBox, Dock.Top);
            col.Children.Add(searchBox);

            // ---- filters + actions ----
            _filters.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(_filters, Dock.Top);
            col.Children.Add(_filters);

            // ---- the list ----
            _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 560 };
            try { _scroll.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(WidgetWindow.ScrollXaml)); _scroll.Resources["wThumb"] = Theme.Faded(_t.Fg, 25); }
            catch { }
            col.Children.Add(_scroll);
            _card.Child = col;

            BuildFilters();
            BuildList();
            Action changed = delegate { Dispatcher.BeginInvoke(new Action(delegate { BuildFilters(); BuildList(); })); };
            Notify.Changed += changed;
            Closed += delegate { Notify.Changed -= changed; _searchTimer.Stop(); };
            Deactivated += delegate { if (!NotifyList.MenuOpen) Close(); };
            PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) { if (_search.Text.Length > 0) _search.Text = ""; else Close(); e.Handled = true; } };
            Loaded += delegate { _search.Focus(); };
        }

        Border Chip(string text, bool on, Action click, Brush dot = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (dot != null) row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = dot, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = on ? 1 : 0.4 });
            row.Children.Add(new TextBlock { Text = text, FontFamily = _t.Font, FontSize = 12, Foreground = on ? _t.Fg : _t.FgDim, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal });
            var b = new Border
            {
                Child = row, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 4), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
                Background = on ? Theme.Faded(_t.Fg, 14) : Brushes.Transparent, BorderBrush = Theme.Faded(_t.Fg, 18), BorderThickness = new Thickness(1)
            };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        List<NotifyItem> Filtered(List<NotifyItem> all)
        {
            string q = _search != null ? _search.Text.Trim() : "";
            return all.FindAll(delegate (NotifyItem i)
            {
                if (_unreadOnly && i.Read) return false;
                if (!_levels.Contains(i.Kind)) return false;
                if (_source.Length > 0 && i.Source != _source) return false;
                if (q.Length > 0 && (i.Title ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    (i.Detail ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0 && (i.Source ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
                return true;
            });
        }

        void BuildFilters()
        {
            _filters.Children.Clear();
            List<NotifyItem> all = Notify.History();
            var row1 = new WrapPanel();
            row1.Children.Add(Chip("All", !_unreadOnly, delegate { _unreadOnly = false; Rebuild(); }));
            row1.Children.Add(Chip("Unread", _unreadOnly, delegate { _unreadOnly = true; Rebuild(); }));
            row1.Children.Add(new Border { Width = 1, Height = 18, Background = Theme.Faded(_t.Fg, 18), Margin = new Thickness(4, 0, 10, 4), VerticalAlignment = VerticalAlignment.Center });
            foreach (NotifyLevel lv in new[] { NotifyLevel.Normal, NotifyLevel.Important, NotifyLevel.Alert })
            {
                NotifyLevel l = lv;
                row1.Children.Add(Chip(lv.ToString(), _levels.Contains(lv), delegate
                {
                    if (_levels.Contains(l)) { if (_levels.Count > 1) _levels.Remove(l); }
                    else _levels.Add(l);
                    Rebuild();
                }, StatusColors.Of(lv)));
            }
            // the sources there are (the newest first)
            var sources = new List<string>();
            foreach (NotifyItem i in all) if (i.Source.Length > 0 && !sources.Contains(i.Source)) sources.Add(i.Source);
            sources.Sort(StringComparer.CurrentCultureIgnoreCase);
            if (_source.Length > 0 && !sources.Contains(_source)) _source = "";
            var src = new ComboBox { MinWidth = 140, Margin = new Thickness(4, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            src.Items.Add("All sources");
            foreach (string s in sources) src.Items.Add(s);
            src.SelectedItem = _source.Length > 0 ? (object)_source : "All sources";
            src.SelectionChanged += delegate { _source = src.SelectedIndex <= 0 ? "" : (string)src.SelectedItem; _show = 150; BuildList(); };
            src.DropDownOpened += delegate { NotifyList.MenuOpen = true; };
            src.DropDownClosed += delegate { NotifyList.MenuOpen = false; };
            row1.Children.Add(src);
            _filters.Children.Add(row1);

            var row2 = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            acts.Children.Add(Pills.Make(_t, "", "Mark read", "Mark everything showing as read", false, delegate
            {
                foreach (NotifyItem i in Filtered(Notify.History())) if (!i.Read) Notify.MarkRead(i.Id);
            }));
            acts.Children.Add(Pills.Make(_t, "", _clearArmed ? "Click again to clear" : "Clear", "Remove everything showing from the history", _clearArmed, delegate
            {
                if (!_clearArmed) { _clearArmed = true; BuildFilters(); var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; t.Tick += delegate { t.Stop(); _clearArmed = false; BuildFilters(); }; t.Start(); return; }
                _clearArmed = false;
                foreach (NotifyItem i in Filtered(Notify.History())) Notify.Remove(i.Id);
            }));
            DockPanel.SetDock(acts, Dock.Right);
            row2.Children.Add(acts);
            row2.Children.Add(new TextBlock { Text = "Click one to open it, ✓ to mark it read, right-click for more.", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _filters.Children.Add(row2);
        }

        void Rebuild() { _show = 150; BuildFilters(); BuildList(); }

        void BuildList()
        {
            if (NotifyList.MenuOpen)
            {
                // a right-click menu is open: draw it once that's closed
                var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                later.Tick += delegate { later.Stop(); if (IsLoaded) { BuildFilters(); BuildList(); } };
                later.Start();
                return;
            }
            double keep = _scroll.VerticalOffset;
            _list.Children.Clear();
            List<NotifyItem> all = Notify.History();
            List<NotifyItem> items = Filtered(all);
            int unread = all.FindAll(delegate (NotifyItem i) { return !i.Read; }).Count;
            _count.Text = (items.Count == all.Count ? all.Count + " kept" : items.Count + " of " + all.Count) + (unread > 0 ? "  ·  " + unread + " unread" : "");
            if (items.Count == 0)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = all.Count == 0 ? "No notifications yet." : "Nothing matches.", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = 12.5,
                    FontStyle = FontStyles.Italic, Margin = new Thickness(4, 8, 0, 8)
                });
                return;
            }
            DateTime lastDay = DateTime.MinValue;
            int n = 0;
            foreach (NotifyItem i in items)
            {
                if (n++ >= _show)
                {
                    int left = items.Count - _show;
                    var more = Pills.Make(_t, "", "Show " + Math.Min(150, left) + " more (" + left + " older)", null, false, delegate { _show += 150; BuildList(); });
                    more.Margin = new Thickness(0, 8, 0, 4);
                    more.HorizontalAlignment = HorizontalAlignment.Center;
                    _list.Children.Add(more);
                    break;
                }
                DateTime day = i.At.ToLocalTime().Date;
                if (day != lastDay)
                {
                    _list.Children.Add(new TextBlock
                    {
                        Text = day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("dddd d MMMM"),
                        Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, lastDay == DateTime.MinValue ? 0 : 10, 0, 3)
                    });
                    lastDay = day;
                }
                _list.Children.Add(NotifyList.Row(_mgr, _t, i, null));
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { _scroll.ScrollToVerticalOffset(keep); }));
        }
    }
}
