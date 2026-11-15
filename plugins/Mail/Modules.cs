using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace Mail
{
    /// <summary>A module's own settings (kept in its ModuleConfig's bag).</summary>
    public sealed class MailView
    {
        readonly SettingsBag _b;
        MailView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static MailView Of(ModuleConfig cfg) { return new MailView(cfg); }

        /// <summary>The account shown ("" = every account).</summary>
        public string Account { get { return _b.Get("Account", ""); } set { _b.Set("Account", value ?? ""); } }
        /// <summary>How many messages are shown at once (the rest scrolls).</summary>
        public int Count { get { return Math.Max(1, Math.Min(Mail.ListMax, _b.Get("Count", 8))); } set { _b.Set("Count", value); } }
        public bool ShowTime { get { return _b.Get("ShowTime", true); } set { _b.Set("ShowTime", value); } }
        public bool ShowFrom { get { return _b.Get("ShowFrom", true); } set { _b.Set("ShowFrom", value); } }
        /// <summary>On the bar / in the widget: the account's name next to the count (one account chosen).</summary>
        public bool ShowName { get { return _b.Get("ShowName", false); } set { _b.Set("ShowName", value); } }
    }

    /// <summary>Drawing the messages: the list (a widget, the bar's popup), a summary, the signature a module rebuilds on.</summary>
    static class MailDraw
    {
        /// <summary>"Work 2 · Home 1" / "No unread messages".</summary>
        public static string Summary(List<MailState> states)
        {
            var parts = new List<string>();
            foreach (MailState st in states)
            {
                if (st.Pending) parts.Add(st.Account.Shown + " …");
                else if (st.Error != null) parts.Add(st.Account.Shown + " !");
                else parts.Add(st.Account.Shown + " " + st.Unread);
            }
            if (parts.Count == 0) return "No mail account in use";
            return Mail.Unread(states) == 0 && !states.Exists(delegate (MailState s) { return s.Error != null || s.Pending; }) ? "No unread messages" : string.Join(" · ", parts);
        }

        /// <summary>What's shown, as text: rebuild only when this changes.</summary>
        public static string Signature(List<MailState> states, MailView v)
        {
            var b = new StringBuilder(v.Count + "|" + v.ShowTime + v.ShowFrom);
            foreach (MailState st in states)
            {
                b.Append('|').Append(st.Account.Id).Append(':').Append(st.Account.Shown).Append(':');
                if (st.Pending) { b.Append("pending"); continue; }
                if (st.Error != null) { b.Append("error:").Append(st.Error); continue; }
                b.Append(st.Unread);
                foreach (MailMessage m in st.Messages)
                {
                    b.Append(',').Append(m.Uid);
                    if (v.ShowTime) b.Append('@').Append(When(m.Date));
                }
            }
            return b.ToString();
        }

        public static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Mail: open " + url + ": " + ex.Message); }
        }

        /// <summary>"2:05 PM" today, else "Yesterday" / "Mon" / "12 Mar".</summary>
        public static string When(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            DateTime local = utc.ToLocalTime();
            return local.Date == DateTime.Today ? TimeText.Clock(local) : TimeText.Day(local);
        }

        static TextBlock Txt(Theme t, string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = fg, FontFamily = t.Font, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        /// <summary>The scroll position of a list made by List (to keep it across a rebuild).</summary>
        public static double Offset(FrameworkElement list)
        {
            var sv = list as ScrollViewer;
            return sv != null ? sv.VerticalOffset : 0;
        }

        /// <summary>
        /// The list: each account's name with its unread count, then a row per unread message (sender, subject, when, Mark as read and Delete).
        /// v.Count rows show at once, the rest scrolls. Click = open its webmail.
        /// </summary>
        public static FrameworkElement List(Theme t, List<MailState> states, MailView v, double fs, double offset = 0)
        {
            var root = new StackPanel();
            var sv = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            WidgetWindow.StyleScrollbars(sv);
            sv.Resources["wThumb"] = t.FgDim;          // (the thin scrollbar's thumb takes its colour from this - a popup has none of its own)
            Fill(root, t, states, v, fs);
            int count = v.Count;
            // as tall as the first rows (headers among them included), the rest scrolling - sized now, so a rebuilt list never shows at full
            // height for a moment first; checked again once it's on screen (what's wrapped may be a little taller)
            root.Measure(new Size(520, double.PositiveInfinity));
            Fit(sv, root, count, false);
            sv.Loaded += delegate
            {
                Fit(sv, root, count, true);
                if (offset > 0) sv.ScrollToVerticalOffset(offset);
            };
            return sv;
        }

        /// <summary>Limits the list to count message rows (the headers among them included); a scrollbar shows when there are more.</summary>
        static void Fit(ScrollViewer sv, StackPanel root, int count, bool onScreen)
        {
            double h = 0;
            int shown = 0;
            bool more = false;
            foreach (UIElement c in root.Children)
            {
                var fe = c as FrameworkElement;
                if (fe == null) continue;
                bool msg = (fe.Tag as string) == "msg";
                if (msg && shown == count) { more = true; break; }
                h += onScreen ? fe.ActualHeight + fe.Margin.Top + fe.Margin.Bottom : fe.DesiredSize.Height;
                if (msg) shown++;
            }
            sv.MaxHeight = more ? h + 2 : double.PositiveInfinity;
            sv.VerticalScrollBarVisibility = more ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled;
            if (more) root.Margin = new Thickness(0, 0, 4, 0);
        }

        static void Fill(StackPanel root, Theme t, List<MailState> states, MailView v, double fs)
        {
            if (states.Count == 0) { root.Children.Add(Txt(t, "No mail account in use - set one up in Settings > Plugins > Mail", t.FgDim, fs)); return; }
            bool first = true;
            foreach (MailState st in states)
            {
                MailAccount a = st.Account;
                string count = st.Pending ? "checking…" : st.Error != null ? "can't check" : st.Unread == 0 ? "nothing unread" : st.Unread + " unread";
                var head = new Grid { Margin = new Thickness(0, first ? 0 : 8, 0, 3), Background = Brushes.Transparent, Cursor = a.WebUrl.Length > 0 ? Cursors.Hand : Cursors.Arrow };
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = Txt(t, a.Shown, st.Error != null ? StatusColors.Warn : t.Fg, fs * 0.95, true);
                head.Children.Add(name);
                var cnt = Txt(t, count, st.Unread > 0 && st.Error == null ? t.MainAccent : t.FgDim, fs * 0.85);
                cnt.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(cnt, 1);
                head.Children.Add(cnt);
                if (a.WebUrl.Length > 0) head.ToolTip = "Click to open " + a.WebUrl;
                Clickable(head, a.WebUrl);
                root.Children.Add(head);
                first = false;
                if (st.Pending) continue;
                if (st.Error != null)
                {
                    var e = Txt(t, st.Error, StatusColors.Bad, fs * 0.9);
                    e.TextWrapping = TextWrapping.Wrap;
                    e.TextTrimming = TextTrimming.None;
                    root.Children.Add(e);
                    continue;
                }
                foreach (MailMessage m0 in st.Messages)
                {
                    MailMessage m = m0;
                    var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = a.WebUrl.Length > 0 ? Cursors.Hand : Cursors.Arrow, Tag = "msg" };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var text = new TextBlock { FontFamily = t.Font, FontSize = fs, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    string subject = m.Subject.Length > 0 ? m.Subject : "(no subject)";
                    if (v.ShowFrom && m.From.Length > 0)
                    {
                        text.Inlines.Add(new Run(m.From) { Foreground = t.Fg, FontWeight = FontWeights.SemiBold });
                        text.Inlines.Add(new Run("  " + subject) { Foreground = t.Fg });
                    }
                    else text.Inlines.Add(new Run(subject) { Foreground = t.Fg });
                    row.Children.Add(text);
                    if (v.ShowTime)
                    {
                        var when = Txt(t, When(m.Date), t.FgDim, fs * 0.85);
                        when.Margin = new Thickness(10, 0, 0, 0);
                        Grid.SetColumn(when, 1);
                        row.Children.Add(when);
                    }
                    row.ToolTip = (m.From.Length > 0 ? m.From + "\n" : "") + subject + (m.Date != DateTime.MinValue ? "\n" + TimeText.Stamp(m.Date.ToLocalTime()) : "") +
                                  (a.WebUrl.Length > 0 ? "\nClick to open " + a.Shown : "");
                    Clickable(row, a.WebUrl);
                    var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    MailState state = st;
                    Grid r = row;
                    buttons.Children.Add(Button(t, "", "Mark as read", fs, delegate { Act(state, m, false, r); }));
                    buttons.Children.Add(Button(t, "", "Delete", fs, delegate { Act(state, m, true, r); }));
                    Grid.SetColumn(buttons, 2);
                    row.Children.Add(buttons);
                    root.Children.Add(row);
                }
                if (st.Unread > st.Messages.Count) root.Children.Add(Txt(t, "and " + (st.Unread - st.Messages.Count) + " more (the newest " + st.Messages.Count + " are listed)", t.FgDim, fs * 0.85));
            }
        }

        /// <summary>A small glyph button on a row (lit on hover). Its press is handled so the row's click and a widget drag don't see it.</summary>
        static FrameworkElement Button(Theme t, string glyph, string tip, double fs, Action click)
        {
            var g = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(10, fs * 0.85), Foreground = t.FgDim, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Child = g, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
            b.MouseEnter += delegate { b.Background = t.Hover; g.Foreground = t.Fg; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; g.Foreground = t.FgDim; };
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        /// <summary>Mark as read / Delete: the row goes at once (the list redraws on Mail.Changed) and comes back with the reason if the server says no.</summary>
        static async void Act(MailState st, MailMessage m, bool delete, Grid row)
        {
            row.IsHitTestVisible = false;
            string err = delete ? await Mail.DeleteAsync(st.Account, m.Uid) : await Mail.MarkReadAsync(st.Account, m.Uid);
            if (err == null) return;
            Questions.Tell(null, (delete ? "Couldn't delete \"" : "Couldn't mark as read \"") + (m.Subject.Length > 0 ? m.Subject : "(no subject)") + "\" in " + st.Account.Shown + ".\n\n" + err, "Mail");
        }

        /// <summary>(a widget: handle the press too, or it starts a widget drag)</summary>
        static void Clickable(FrameworkElement el, string url)
        {
            el.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            el.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Open(url); };
        }
    }

    /// <summary>A timer that keeps going while the module is on screen (widgets rebuild while they fit: Unloaded can come after Loaded).</summary>
    static class ModuleTimer
    {
        public static DispatcherTimer Make(FrameworkElement el, TimeSpan every, Action tick)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = every };
            int gone = 0;
            timer.Tick += delegate
            {
                if (PresentationSource.FromVisual(el) == null) { if (++gone >= 5) timer.Stop(); return; }
                gone = 0;
                tick();
            };
            el.Loaded += delegate { tick(); timer.Start(); };
            el.IsVisibleChanged += delegate { if (el.IsVisible && !timer.IsEnabled) { tick(); timer.Start(); } };
            el.Unloaded += delegate { el.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { if (!el.IsLoaded) timer.Stop(); })); };
            timer.Start();
            // a message marked read or deleted: redraw now, not at the next tick (a module that's gone lets go of it then)
            Action changed = null;
            changed = delegate
            {
                el.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                {
                    if (PresentationSource.FromVisual(el) == null && !el.IsLoaded) { Mail.Changed -= changed; return; }
                    tick();
                }));
            };
            Mail.Changed += changed;
            return timer;
        }
    }

    /// <summary>On a bar or in a widget: an envelope with the unread count (click: the messages in a popup).</summary>
    public sealed class MailBarModule : Border
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly TextBlock _glyph, _text;
        PopupWindow _popup;
        FrameworkElement _list;
        string _shown, _popupShown;

        public MailBarModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            double fs = cfg.ShownFontSize > 0 ? cfg.ShownFontSize : _t.FontSize;
            Background = Brushes.Transparent;
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = host.IsWidget ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            _glyph = new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = fs, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _text = new TextBlock { FontFamily = _t.Font, FontSize = fs, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_glyph);
            sp.Children.Add(_text);
            Child = sp;
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            ModuleTimer.Make(this, TimeSpan.FromSeconds(2), Refresh);
            Refresh();
        }

        List<MailState> States() { return Mail.For(MailView.Of(_cfg).Account); }

        void Refresh()
        {
            MailView v0 = MailView.Of(_cfg);
            List<MailState> s = States();
            int unread = Mail.Unread(s);
            bool error = s.Exists(delegate (MailState st) { return st.Error != null; });
            bool pending = s.Count > 0 && s.TrueForAll(delegate (MailState st) { return st.Pending; });
            string text, tip = "Mail: " + MailDraw.Summary(s) + "\nClick for the messages";
            Brush fg = _t.Fg;
            if (s.Count == 0) { text = ""; fg = _t.FgDim; tip = "Mail: no account in use - Settings > Plugins > Mail"; }
            else if (pending) { text = "…"; fg = _t.FgDim; }
            else if (unread > 0) { text = unread.ToString(); fg = _t.MainAccent; }
            else if (error) { text = "!"; fg = StatusColors.Warn; }
            else { text = ""; fg = _t.FgDim; }
            // one account chosen, with its name: "Work 3" (just "Work" with nothing unread)
            if (v0.ShowName && v0.Account.Length > 0 && s.Count == 1) text = s[0].Account.Shown + (text.Length > 0 ? " " + text : "");
            // "Hide while there's nothing unread"
            bool hide = _cfg.HideIfNone && s.Count > 0 && !error && !pending && unread == 0;
            string sig = text + "|" + hide + "|" + tip;
            if (_popup != null && _popup.IsOpen)
            {
                string ps = MailDraw.Signature(s, MailView.Of(_cfg));
                if (ps != _popupShown) { _popupShown = ps; _popup.SetContent(PopupContent(s)); }
            }
            if (sig == _shown) return;
            _shown = sig;
            _text.Text = text;
            _text.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _glyph.Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0);
            _text.Foreground = fg;
            _glyph.Foreground = fg;
            ToolTip = tip;
            Visibility v = hide ? Visibility.Collapsed : Visibility.Visible;
            if (Visibility != v) { Visibility = v; _host.ContentChanged(); }
        }

        FrameworkElement PopupContent(List<MailState> s)
        {
            var box = new Border { Padding = new Thickness(12, 10, 12, 10), MinWidth = 300, MaxWidth = 560 };
            box.Child = _list = MailDraw.List(_t, s, MailView.Of(_cfg), Math.Max(12, _t.FontSize), _list != null ? MailDraw.Offset(_list) : 0);
            return box;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; _list = null; return; }
            List<MailState> s = States();
            _list = null;
            _popupShown = MailDraw.Signature(s, MailView.Of(_cfg));
            _popup = new PopupWindow(_mgr, _host, this, _t, PopupContent(s));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>In a widget: the unread messages by account (it sizes itself to them).</summary>
    public sealed class MailListModule : Border, IMultiRow
    {
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        string _shown;

        public int RowCount { get { return 0; } }       // sizes itself

        public MailListModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(2);
            ModuleTimer.Make(this, TimeSpan.FromSeconds(2), Refresh);
            Refresh();
        }

        void Refresh()
        {
            MailView v = MailView.Of(_cfg);
            List<MailState> s = Mail.For(v.Account);
            bool hide = _cfg.HideIfNone && s.Count > 0 && Mail.Unread(s) == 0 && !s.Exists(delegate (MailState st) { return st.Error != null || st.Pending; });
            string sig = MailDraw.Signature(s, v) + "|" + hide;
            if (sig == _shown) return;
            _shown = sig;
            double fs = _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            Child = MailDraw.List(_t, s, v, fs, MailDraw.Offset(Child as FrameworkElement));
            Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            _host.ContentChanged();
        }
    }
}
