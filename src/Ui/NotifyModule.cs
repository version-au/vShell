using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The notification history. On a bar a bell with the number not read yet - click it for the list (opening it marks them read).
    /// In a widget the list itself. Click a notification to open what it is about; right-click to remove it.
    /// </summary>
    public sealed class NotifyModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        PopupWindow _popup;
        bool _dirty = true;
        int _lastAlerts = -1;           // -1: just built (an Alert already there doesn't bounce on start)
        readonly DispatcherTimer _timer;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public NotifyModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
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
            Action changed = delegate { _dirty = true; };
            Loaded += delegate { Notify.Changed += changed; _dirty = true; Refresh(); _timer.Start(); };
            Unloaded += delegate { Notify.Changed -= changed; _timer.Stop(); };
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(); };
            Refresh();
        }

        void Refresh()
        {
            if (NotifyList.MenuOpen) return;
            // "5m ago" moves on: build again once a minute too
            string key = (_dirty ? "d" : "") + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute) + "|" + NotifyList.Version;
            if (key == _shownKey && !_dirty) return;
            _dirty = false;
            _shownKey = key;
            if (_host.IsWidget) Child = NotifyList.Build(_mgr, _t, _cfg, true, null);
            else
            {
                Child = BuildBar();
                if (_popup != null && _popup.IsOpen) _popup.SetContent(PopupList());
            }
            _host.ContentChanged();
        }

        FrameworkElement BuildBar()
        {
            List<NotifyItem> items = NotifyList.Pick(_cfg);
            int unread = items.FindAll(delegate (NotifyItem i) { return !i.Read; }).Count;
            NotifyLevel top = NotifyLevel.Normal;
            foreach (NotifyItem i in items) if (!i.Read && i.Kind > top) top = i.Kind;
            int alerts = items.FindAll(delegate (NotifyItem i) { return !i.Read && i.Kind == NotifyLevel.Alert; }).Count;
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Center };
            // the bell takes the colour of the most serious unread one (Important / Alert)
            icon.Children.Add(new TextBlock
            {
                Text = ((char)0xEA8F).ToString(), FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.1, VerticalAlignment = VerticalAlignment.Center,
                Foreground = unread == 0 ? _t.FgDim : top != NotifyLevel.Normal ? StatusColors.Of(top) : _t.Fg,
                Margin = new Thickness(0, 0, _cfg.NfBadge && unread > 0 ? 6 : 0, 0)
            });
            if (_cfg.NfBadge && unread > 0)
            {
                double fs = Math.Max(7, _t.FontSize * 0.62);
                var badge = new Border
                {
                    Background = StatusColors.Of(top), CornerRadius = new CornerRadius(fs), MinWidth = fs * 1.6, Height = fs * 1.6, Padding = new Thickness(fs * 0.3, 0, fs * 0.3, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -fs * 0.5, -fs * 0.2, 0),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    Child = new TextBlock
                    {
                        Text = unread > 99 ? "99+" : unread.ToString(), FontSize = fs, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(StatusColors.Of(top)),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                icon.Children.Add(badge);
                if (_cfg.NfPulse && alerts > 0)
                {
                    // an Alert unread: the badge breathes; a new one arriving makes it bounce a few times
                    var breathe = new System.Windows.Media.Animation.DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
                    badge.BeginAnimation(OpacityProperty, breathe);
                    if (_lastAlerts >= 0 && alerts > _lastAlerts)
                    {
                        var scale = new ScaleTransform(1, 1);
                        badge.RenderTransform = scale;
                        var bounce = new System.Windows.Media.Animation.DoubleAnimation(1, 1.45, TimeSpan.FromMilliseconds(220))
                        {
                            AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3),
                            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
                    }
                }
            }
            _lastAlerts = alerts;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
            if (_cfg.Title != null && _cfg.Title.Length > 0)
                row.Children.Add(new TextBlock { Text = " " + _cfg.Title, Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center });
            Visibility = _cfg.NfHideEmpty && items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            var tip = new List<string> { unread > 0 ? unread + " new notification" + (unread > 1 ? "s" : "") : "No new notifications" };
            int n = 0;
            foreach (NotifyItem i in items)
            {
                if (n++ >= 5) break;
                tip.Add("  " + (i.Read ? "" : "• ") + i.TitleShown + "  (" + i.Source + ", " + Notify.When(i.At) + ")");
            }
            int held = Notify.HeldCount;
            string why;
            if (Notify.Quiet(_mgr.Settings, out why)) tip.Add("Holding new ones: " + why + (held > 0 ? " (" + held + " waiting)" : ""));
            tip.Add("Click for the history");
            ToolTip = string.Join("\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (PopupWindow.ClosedJustNow(this)) { _popup = null; return; }     // this click's press already closed it
            _popupList = null;
            _popup = new PopupWindow(_mgr, _host, this, _t, new Border());
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _popup.SetContent(PopupList());          // after the style: its room on the monitor counts the card's padding
            _mgr.ShowPopup(_popup);
            // seen now (an option: by default they stay unread until you mark them - the ✓ on each, or Mark all read)
            PopupWindow p = _popup;
            if (_cfg.NfReadOnClose) p.Closed += delegate { MarkShownRead(); };
        }

        FrameworkElement _popupList;

        /// <summary>The popup's list: no taller than the room between the bar and its monitor's far edge, the rows scrolling - and built
        /// again (a new one, a minute on) where it was scrolled to.</summary>
        FrameworkElement PopupList()
        {
            var old = _popupList != null ? _popupList.Tag as ScrollViewer : null;
            double offset = old != null ? old.VerticalOffset : 0;
            _popupList = NotifyList.Build(_mgr, _t, _cfg, false, delegate { if (_popup != null) _popup.CloseSafe(); }, _popup != null ? _popup.ContentRoom() : double.PositiveInfinity, offset);
            return _popupList;
        }

        void MarkShownRead()
        {
            foreach (NotifyItem i in NotifyList.Pick(_cfg)) if (!i.Read) Notify.MarkRead(i.Id);
        }
    }

    /// <summary>The history list (bar popup, widget, history window).</summary>
    public static class NotifyList
    {
        public static bool MenuOpen;
        public static int Version;

        /// <summary>The notifications a module lists (cfg null = all).</summary>
        public static List<NotifyItem> Pick(ModuleConfig cfg)
        {
            List<NotifyItem> all = Notify.History();
            if (cfg == null || cfg.NfSource == "All") return all;
            return all.FindAll(delegate (NotifyItem i) { return i.Source == cfg.NfSource; });
        }

        static TextBlock T(Theme t, string text, Brush b, double scale = 1, bool bold = false, bool wrap = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = t.Font, FontSize = Math.Max(7, t.FontSize * scale), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
        }

        static Border Pill(Theme t, string text, string tip, Action click)
        {
            return Pills.Make(t, "", text, tip, false, click);
        }

        /// <summary>The bar's popup keeps at most this many (the rest: + N older, a click opens the history).</summary>
        const int PopupMost = 100;

        /// <summary>
        /// The list. The bar's popup (not a widget, a module's cfg): 400 wide, at most `room` tall (PopupWindow.ContentRoom) with the
        /// heading staying put and the rows scrolling (wheel or scrollbar), scrolled to `offset`; the ScrollViewer is its Tag.
        /// </summary>
        public static FrameworkElement Build(BarManager mgr, Theme t, ModuleConfig cfg, bool widget, Action close, double room = double.PositiveInfinity, double offset = 0)
        {
            var root = new StackPanel();
            Panel list = root;
            FrameworkElement result = root;
            bool popup = !widget && cfg != null;
            if (popup)
            {
                // the bar's popup: a fixed width; the history window: as wide as its card (a fixed width there pushed Clear off the edge)
                list = new StackPanel();
                var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
                WidgetWindow.StyleScrollbars(scroll);
                scroll.Resources["wThumb"] = Theme.Faded(t.FgDim, 70);
                if (offset > 0) scroll.Loaded += delegate { scroll.ScrollToVerticalOffset(offset); };
                var grid = new Grid { Width = 400, Margin = new Thickness(4), Tag = scroll };
                if (!double.IsInfinity(room)) grid.MaxHeight = Math.Max(120, room - 8);     // less its margin
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(scroll, 1);
                grid.Children.Add(root);
                grid.Children.Add(scroll);
                result = grid;
            }
            List<NotifyItem> items = Pick(cfg);
            int count = cfg != null ? cfg.NfCount : 30;
            if (!widget) count = Math.Max(count, 15);
            if (popup) count = PopupMost;          // it scrolls: all of them, up to a point

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (items.Exists(delegate (NotifyItem i) { return !i.Read; }))
                buttons.Children.Add(Pill(t, "Mark all read", "Mark every notification as read", delegate { foreach (NotifyItem i in items) if (!i.Read) Notify.MarkRead(i.Id); }));
            if (items.Count > 0)
                buttons.Children.Add(Pill(t, "Clear", "Remove " + (cfg == null || cfg.NfSource == "All" ? "every notification" : "the " + cfg.NfSource + " notifications") + " from the history", delegate
                {
                    if (cfg == null || cfg.NfSource == "All") Notify.Clear();
                    else foreach (NotifyItem i in items) Notify.Remove(i.Id);
                }));
            // the bar's list: open the full history (search, filters, all of it)
            if (!widget && cfg != null)
                buttons.Children.Add(Pill(t, "History", "Open the notification history: search, filter and everything kept", delegate
                {
                    if (close != null) close();
                    NotifyHistoryWindow.ShowNear(mgr);
                }));
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(T(t, cfg != null && cfg.Title != null && cfg.Title.Length > 0 ? cfg.Title : "Notifications", t.Fg, 1, true));
            if (cfg != null && cfg.NfSource != "All") title.Children.Add(T(t, "  " + cfg.NfSource, t.FgDim, 0.85));
            head.Children.Add(title);
            root.Children.Add(head);

            string why;
            if (Notify.Quiet(mgr.Settings, out why))
            {
                int held = Notify.HeldCount;
                var q = T(t, "New ones are held - " + why + (held > 0 ? " (" + held + " waiting)" : ""), StatusColors.Warn, 0.82, false, true);
                q.Margin = new Thickness(0, 0, 0, 4);
                root.Children.Add(q);
            }
            if (items.Count == 0)
            {
                var none = T(t, "No notifications.", t.FgDim, 0.9);
                none.Margin = new Thickness(2, 2, 0, 4);
                list.Children.Add(none);
                return result;
            }
            DateTime lastDay = DateTime.MinValue;
            int n = 0;
            foreach (NotifyItem i in items)
            {
                if (n++ >= count)
                {
                    var more = T(t, "+ " + (items.Count - count) + " older" + (popup ? " - in the history" : ""), t.FgDim, 0.82);
                    more.Margin = new Thickness(4, 3, 0, 0);
                    if (popup)
                    {
                        more.Cursor = Cursors.Hand;
                        more.ToolTip = "Open the notification history";
                        more.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                        more.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                        {
                            e.Handled = true;
                            if (close != null) close();
                            NotifyHistoryWindow.ShowNear(mgr);
                        };
                    }
                    list.Children.Add(more);
                    break;
                }
                DateTime day = i.At.ToLocalTime().Date;
                if (day != lastDay && !widget)
                {
                    var dh = T(t, day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("dddd d MMM"), t.FgDim, 0.8, true);
                    dh.Margin = new Thickness(2, lastDay == DateTime.MinValue ? 0 : 6, 0, 2);
                    list.Children.Add(dh);
                    lastDay = day;
                }
                list.Children.Add(Row(mgr, t, i, close));
            }
            return result;
        }

        public static FrameworkElement Row(BarManager mgr, Theme t, NotifyItem i, Action close)
        {
            var g = new Grid();
            NotifyLevel level = i.Kind;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(NotifyCards.LevelBar(level, i.Read));
            var glyph = new TextBlock
            {
                Text = NotifyCards.Glyph(i.Source), FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(8, t.FontSize * 0.9), VerticalAlignment = VerticalAlignment.Top,
                Foreground = StatusColors.Of(level), Opacity = i.Read ? 0.55 : 1, Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetColumn(glyph, 1);
            g.Children.Add(glyph);
            var info = new StackPanel();
            info.Children.Add(T(t, i.TitleShown, NotifyCards.TitleBrush(level, t), 0.92, !i.Read, true));
            string sub = i.Source + (i.Detail.Length > 0 ? " · " + i.Detail : "");
            info.Children.Add(T(t, sub, t.FgDim, 0.78, false, true));
            ImageSource pic = NotifyCards.Picture(i.Image, 160);
            if (pic != null)
            {
                var img = new Image { Source = pic, Stretch = Stretch.Uniform, MaxWidth = 150, MaxHeight = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 1) };
                info.Children.Add(img);
            }
            Grid.SetColumn(info, 2);
            g.Children.Add(info);
            var when = T(t, Notify.When(i.At), t.FgDim, 0.75);
            when.VerticalAlignment = VerticalAlignment.Top;
            when.Margin = new Thickness(8, 2, 0, 0);
            Grid.SetColumn(when, 3);
            g.Children.Add(when);
            // ✓ : mark just this one read (without opening it)
            if (!i.Read)
            {
                double d = Math.Round(Math.Max(18, t.FontSize * 1.6));
                var tick = new Border
                {
                    Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Mark as read",
                    Child = new TextBlock
                    {
                        Text = ((char)0xE73E).ToString(), FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(8, t.FontSize * 0.8), Foreground = t.FgDim,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                tick.MouseEnter += delegate { tick.Background = t.MainAccent; ((TextBlock)tick.Child).Foreground = NoteRender.Contrast(t.MainAccent); };
                tick.MouseLeave += delegate { tick.Background = Brushes.Transparent; ((TextBlock)tick.Child).Foreground = t.FgDim; };
                tick.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                tick.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Notify.MarkRead(i.Id); };
                Grid.SetColumn(tick, 4);
                g.Children.Add(tick);
            }

            var row = new Border
            {
                Child = g, Padding = new Thickness(3, 3, 4, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent,
                Cursor = i.Url.Length > 0 ? Cursors.Hand : Cursors.Arrow,
                ToolTip = i.Kind + " · " + TimeText.Full(i.At.ToLocalTime()) + (i.Url.Length > 0 && !i.Url.StartsWith("vshell:") ? "\nClick: open " + i.Url : "") + "\nRight-click: remove"
            };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                NotifyCards.Open(mgr, i);
                if (i.Url.Length > 0 && close != null) close();
            };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var m = new ContextMenu();
                if (!i.Read) { var r = new MenuItem { Header = "Mark as read" }; r.Click += delegate { Notify.MarkRead(i.Id); }; m.Items.Add(r); }
                var del = new MenuItem { Header = "Remove" };
                del.Click += delegate { Notify.Remove(i.Id); };
                m.Items.Add(del);
                var copy = new MenuItem { Header = "Copy the text" };
                copy.Click += delegate { try { Clipboard.SetText(i.Title + (i.Detail.Length > 0 ? "\n" + i.Detail : "")); } catch { } };
                m.Items.Add(copy);
                PopupWindow host = Window.GetWindow(row) as PopupWindow;
                if (host != null) host.Pinned = true;
                MenuOpen = true;
                m.Closed += delegate
                {
                    if (host != null) host.Pinned = false;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { MenuOpen = false; Version++; }));
                };
                m.PlacementTarget = row;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                m.IsOpen = true;
            };
            return row;
        }
    }
}
