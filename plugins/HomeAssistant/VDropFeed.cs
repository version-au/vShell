using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A Notes module on vDrop: vDrop's feed, laid out like vDrop's own page - tabs (Inbox, Links, Media, Notes, Archived), and on Links / Media / Notes a list to pick Inbox /
    /// Pinned / Archived, search, then the items oldest to newest with day dividers (pinned ones first in the inbox), each
    /// with who shared it, from which app and when, its text, and for links the preview card (site, title, description, picture, a
    /// play button for videos). A box at the bottom adds a note or a link. Double-click (or right-click > Edit) changes one;
    /// right-click also has Open, Copy, Pin, Archive, Refresh preview and Delete. Everything goes both ways through VDrop.
    /// </summary>
    public sealed class VDropFeed : DockPanel, INoteFeed
    {
        readonly ModuleConfig _cfg;
        readonly Action _save;
        readonly Action<TextBox> _keyboard;
        readonly Action _endTyping;
        Theme _t;
        NoteLook _look = new NoteLook();

        readonly ScrollViewer _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        readonly StackPanel _list = new StackPanel();
        readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) };
        TextBox _search, _compose, _editText, _editTitle;
        VDropItem _editing;
        bool _later, _built;
        readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly Dictionary<string, List<Action<string>>> _waiting = new Dictionary<string, List<Action<string>>>();

        static readonly string[] Kinds = { "", "links", "media", "notes", "archived" };
        static readonly string[] KindNames = { "Inbox", "Links", "Media", "Notes", "Archived" };
        static readonly string[] Statuses = { "open", "pinned", "archived" };
        static readonly string[] StatusNames = { "Inbox", "Pinned", "Archived" };
        const string ArchiveGlyph = "\uE7B8", UnarchiveGlyph = "\uE7A7";
        bool ArchivedTab { get { return HaView.Of(_cfg).VdKind == "archived"; } }
        /// <summary>The Inbox / Pinned / Archived list only applies on the Links, Media and Notes tabs (Inbox and Archived are lists already).</summary>
        bool KindTab { get { return HaView.Of(_cfg).VdKind == "links" || HaView.Of(_cfg).VdKind == "media" || HaView.Of(_cfg).VdKind == "notes"; } }
        /// <summary>What's shown: "open", "pinned" or "archived".</summary>
        string Showing { get { return ArchivedTab ? "archived" : KindTab ? (HaView.Of(_cfg).VdStatus ?? "open") : "open"; } }
        static readonly Regex UrlRx = new Regex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

        public VDropFeed(ModuleConfig cfg, Action save, Action<TextBox> keyboard, Action endTyping)
        {
            _cfg = cfg; _save = save; _keyboard = keyboard; _endTyping = endTyping;
            LastChildFill = true;
            WidgetWindow.StyleScrollbars(_scroll);
            _scroll.Content = _list;
            _searchTimer.Tick += delegate { _searchTimer.Stop(); BuildList(); };
            Action<string> thumb = OnThumb;
            Action changed = Refresh;
            Loaded += delegate { VDrop.ThumbReady += thumb; VDrop.Changed += changed; Refresh(); };
            Unloaded += delegate { VDrop.ThumbReady -= thumb; VDrop.Changed -= changed; _searchTimer.Stop(); };
        }

        /// <summary>Someone is typing in the feed (an item being changed, or the add box / search with text in it).</summary>
        public bool Typing { get { return _editing != null; } }

        // =================================================================
        // The frame: tabs, the Inbox / Pinned / Archived list, search, the add box
        // =================================================================
        public FrameworkElement View { get { return this; } }

        public void SetLook(Theme t, NoteLook look, double height)
        {
            _t = t; _look = look;
            Height = height;
            _scroll.Resources["wThumb"] = look.Line;
            string q = _search != null ? _search.Text : "", draft = _compose != null ? _compose.Text : "";
            Children.Clear();

            // ---- top: tabs and the list ----
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            int si = Math.Max(0, Array.IndexOf(Statuses, HaView.Of(_cfg).VdStatus));
            var statusBtn = Pill(StatusNames[si] + "  ", false, null);
            ((TextBlock)statusBtn.Child).FontFamily = new FontFamily(look.Font.Source + ", " + IconUtil.GlyphFont.Source);
            statusBtn.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var m = new ContextMenu { PlacementTarget = statusBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                for (int i = 0; i < Statuses.Length; i++)
                {
                    string st = Statuses[i];
                    var mi = new MenuItem { Header = StatusNames[i], IsCheckable = true, IsChecked = HaView.Of(_cfg).VdStatus == st };
                    mi.Click += delegate { HaView.Of(_cfg).VdStatus = st; _save(); SetLook(_t, _look, Height); };
                    m.Items.Add(mi);
                }
                m.IsOpen = true;
            };
            DockPanel.SetDock(statusBtn, Dock.Right);
            if (KindTab) top.Children.Add(statusBtn);       // the Inbox and Archived tabs are lists of their own
            var tabs = new WrapPanel();
            for (int i = 0; i < Kinds.Length; i++)
            {
                string k = Kinds[i];
                Border tab = Pill(KindNames[i], (HaView.Of(_cfg).VdKind ?? "") == k, null);
                tab.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; HaView.Of(_cfg).VdKind = k; _save(); SetLook(_t, _look, Height); };
                tabs.Children.Add(tab);
            }
            top.Children.Add(tabs);
            DockPanel.SetDock(top, Dock.Top);
            Children.Add(top);

            // ---- search ----
            _search = Box("Search", false);
            _search.Text = q;
            _search.TextChanged += delegate { _searchTimer.Stop(); _searchTimer.Start(); };
            _search.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) { _search.Text = ""; e.Handled = true; } };
            var searchBox = Field(_search, "", "Search");
            searchBox.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(searchBox, Dock.Top);
            Children.Add(searchBox);

            // ---- bottom: the add box (and what's going on with the connection) ----
            var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            _status.FontSize = Small; _status.Foreground = look.Dim;
            if (_status.Parent is Panel) ((Panel)_status.Parent).Children.Remove(_status);
            bottom.Children.Add(_status);
            _compose = Box("Paste a link or write a note…", true);
            _compose.Text = draft;
            _compose.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; Send(); }
            };
            var send = Glyph("", look.Size * 1.05, _t.MainAccent);
            var sendBtn = new Border { Child = send, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Add (Enter) - Shift+Enter for a new line", VerticalAlignment = VerticalAlignment.Bottom };
            sendBtn.MouseEnter += delegate { sendBtn.Background = look.Line; };
            sendBtn.MouseLeave += delegate { sendBtn.Background = Brushes.Transparent; };
            sendBtn.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Send(); };
            var composeBox = Field(_compose, null, "Paste a link or write a note…", sendBtn);
            composeBox.Margin = new Thickness(0, 4, 0, 0);
            bottom.Children.Add(composeBox);
            DockPanel.SetDock(bottom, Dock.Bottom);
            Children.Add(bottom);

            Children.Add(_scroll);
            _built = false;
            BuildList();
        }

        double Small { get { return Math.Max(8, _look.Size * 0.82); } }

        Border Pill(string text, bool on, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = Math.Max(8, _look.Size * 0.92), Foreground = on ? _look.Fg : _look.Dim, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            var b = new Border { Child = tb, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 2), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, ToolTip = tip, Background = on ? _look.CodeBg : Brushes.Transparent };
            b.MouseEnter += delegate { if (!on) b.Background = _look.Line; };
            b.MouseLeave += delegate { if (!on) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };     // not a widget drag
            return b;
        }

        TextBox Box(string hint, bool multi)
        {
            var tb = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = _look.Fg, CaretBrush = _look.Fg, FontSize = _look.Size,
                FontFamily = _look.Font, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxHeight = multi ? _look.Size * 7 : double.PositiveInfinity,
                VerticalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 2, 0, 2)
            };
            tb.PreviewMouseLeftButtonDown += delegate { if (!tb.IsKeyboardFocusWithin) _keyboard(tb); };
            tb.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            return tb;
        }

        /// <summary>A rounded box around a text box, with a hint while it's empty (and a glyph / a button).</summary>
        Border Field(TextBox box, string glyph, string hint, UIElement right = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (glyph != null)
            {
                var gl = Glyph(glyph, _look.Size * 0.85, _look.Dim);
                gl.Margin = new Thickness(0, 0, 6, 0);
                g.Children.Add(gl);
            }
            var hintTb = new TextBlock { Text = hint, Foreground = _look.Dim, FontSize = _look.Size, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 0) };
            hintTb.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += delegate { hintTb.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            Grid.SetColumn(hintTb, 1); Grid.SetColumn(box, 1);
            g.Children.Add(hintTb);
            g.Children.Add(box);
            if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
            var b = new Border { Child = g, Background = _look.CodeBg, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 4, 4), Cursor = Cursors.IBeam };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; _keyboard(box); };
            return b;
        }

        TextBlock Glyph(string g, double size, Brush fg)
        {
            return new TextBlock { Text = g, FontFamily = IconUtil.GlyphFont, FontSize = size, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        void Send()
        {
            if (_compose == null) return;
            string text = _compose.Text.Trim();
            if (text.Length == 0) return;
            _compose.Text = "";
            VDrop.Add(text);
            if (Showing != "open") { HaView.Of(_cfg).VdStatus = "open"; if (ArchivedTab) HaView.Of(_cfg).VdKind = ""; _save(); SetLook(_t, _look, Height); }
            ScrollToEnd();
        }

        public void FocusCompose() { if (_compose != null) _keyboard(_compose); }

        // =================================================================
        // The list
        // =================================================================
        /// <summary>vDrop changed: draw it again (after an item being changed here is finished).</summary>
        public void Refresh()
        {
            if (_t == null) return;
            _status.Text = VDrop.Status;
            _status.Visibility = VDrop.Status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_editing != null) { _later = true; return; }
            BuildList();
        }

        bool Matches(VDropItem it, string q)
        {
            switch (Showing)
            {
                case "archived": if (!it.Done) return false; break;
                case "pinned": if (!it.Pinned || it.Done) return false; break;
                default: if (it.Done) return false; break;
            }
            switch (HaView.Of(_cfg).VdKind ?? "")
            {
                case "links": if (it.Type != "link") return false; break;
                case "notes": if (it.Type != "note") return false; break;
                case "media": if (!it.IsMedia) return false; break;
            }
            if (q.Length == 0) return true;
            foreach (string f in new[] { it.Text, it.Title, it.Description, it.Url, it.SiteName, it.Author, it.Device })
                if (f != null && f.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            return false;
        }

        void BuildList()
        {
            if (_t == null) return;
            bool atEnd = !_built || _scroll.VerticalOffset >= _scroll.ScrollableHeight - 40;
            double keep = _scroll.VerticalOffset;
            _list.Children.Clear();
            _waiting.Clear();
            string q = _search != null ? _search.Text.Trim() : "";
            var items = VDrop.Items.FindAll(delegate (VDropItem it) { return Matches(it, q); });
            items.Sort(delegate (VDropItem a, VDropItem b) { return a.Created.CompareTo(b.Created); });     // oldest at the top, newest at the bottom

            if (items.Count == 0)
            {
                string msg = q.Length > 0 ? "Nothing matches that search." :
                             Showing == "archived" ? "Nothing archived yet." :
                             Showing == "pinned" ? "Nothing pinned." :
                             "Nothing here yet. Share a link from your phone (Home Assistant in the share menu) or add one below.";
                _list.Children.Add(new TextBlock { Text = msg, Foreground = _look.Dim, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 8) });
            }
            if (Showing == "open" && q.Length == 0)
            {
                var pinned = items.FindAll(delegate (VDropItem it) { return it.Pinned; });
                if (pinned.Count > 0)
                {
                    _list.Children.Add(Divider("Pinned"));
                    foreach (VDropItem it in pinned) _list.Children.Add(Row(it));
                    items.RemoveAll(delegate (VDropItem it) { return it.Pinned; });
                }
            }
            DateTime lastDay = DateTime.MinValue;
            foreach (VDropItem it in items)
            {
                DateTime day = it.Created.ToLocalTime().Date;
                if (day != lastDay) { _list.Children.Add(Divider(DayLabel(day))); lastDay = day; }
                _list.Children.Add(Row(it));
            }
            _built = true;
            if (atEnd) ScrollToEnd();
            else Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { _scroll.ScrollToVerticalOffset(keep); }));
        }

        void ScrollToEnd()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { _scroll.ScrollToEnd(); }));
        }

        static string DayLabel(DateTime day)
        {
            if (day == DateTime.Today) return "Today";
            if (day == DateTime.Today.AddDays(-1)) return "Yesterday";
            return day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
        }

        static string When(DateTime utc)
        {
            DateTime t = utc.ToLocalTime();
            string time = t.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
            if (t.Date == DateTime.Today) return "Today at " + time;
            if (t.Date == DateTime.Today.AddDays(-1)) return "Yesterday at " + time;
            return t.ToString("d/M/yyyy", CultureInfo.CurrentCulture) + " " + time;
        }

        FrameworkElement Divider(string text)
        {
            var g = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l1 = new Border { Height = 1, Background = _look.Line, VerticalAlignment = VerticalAlignment.Center };
            var l2 = new Border { Height = 1, Background = _look.Line, VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = text, FontSize = Small, FontWeight = FontWeights.SemiBold, Foreground = _look.Dim, Margin = new Thickness(8, 0, 8, 0) };
            Grid.SetColumn(tb, 1); Grid.SetColumn(l2, 2);
            g.Children.Add(l1); g.Children.Add(tb); g.Children.Add(l2);
            return g;
        }

        // ---- one item ----------------------------------------------------------------------------
        static readonly Dictionary<string, string> AppNames = new Dictionary<string, string>
        {
            { "com.android.chrome", "Chrome" }, { "com.google.android.youtube", "YouTube" }, { "com.reddit.frontpage", "Reddit" },
            { "com.twitter.android", "X" }, { "com.instagram.android", "Instagram" }, { "com.zhiliaoapp.musically", "TikTok" },
            { "org.mozilla.firefox", "Firefox" }, { "com.discord", "Discord" }, { "com.facebook.katana", "Facebook" },
            { "com.google.android.apps.maps", "Maps" }, { "com.spotify.music", "Spotify" }, { "com.microsoft.emmx", "Edge" },
            { "com.sec.android.app.sbrowser", "Samsung Internet" }, { "com.google.android.googlequicksearchbox", "Google" }
        };

        static string ViaName(string pkg)
        {
            if (string.IsNullOrEmpty(pkg)) return null;
            string n;
            if (AppNames.TryGetValue(pkg, out n)) return n;
            string last = pkg.Substring(pkg.LastIndexOf('.') + 1);
            return last.Length > 0 ? char.ToUpperInvariant(last[0]) + last.Substring(1) : pkg;
        }

        static string Who(VDropItem it)
        {
            if (!string.IsNullOrEmpty(it.Device)) return it.Device;
            switch (it.Source) { case "vshell": return "vShell"; case "web": return "Web"; case "android": return "Phone"; case "ios": return "iPhone"; default: return "Someone"; }
        }

        FrameworkElement Row(VDropItem it)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 8), Tag = it, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (it.Done && Showing != "archived") g.Opacity = 0.55;

            // who: a round badge for where it came from
            double d = Math.Round(_look.Size * 2.4);
            Brush badge = it.Source == "vshell" ? StatusColors.Good : it.Source == "web" ? (Brush)_t.SecondaryAccent :
                          it.Source == "ios" ? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)) : _t.MainAccent;
            string glyph = it.Source == "vshell" ? "" : it.Source == "web" ? "" : "";
            var av = new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = badge, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0),
                Child = Glyph(glyph, d * 0.45, NoteRender.Contrast(badge))
            };
            g.Children.Add(av);

            var body = new StackPanel();
            Grid.SetColumn(body, 1);
            g.Children.Add(body);

            var meta = new WrapPanel();
            meta.Children.Add(new TextBlock { Text = Who(it), FontWeight = FontWeights.SemiBold, Foreground = _look.Fg, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Bottom });
            string via = ViaName(it.Via);
            if (via != null) meta.Children.Add(new TextBlock { Text = "via " + via, FontSize = Small, Foreground = _look.Dim, Margin = new Thickness(0, 0, 6, 1), VerticalAlignment = VerticalAlignment.Bottom });
            meta.Children.Add(new TextBlock { Text = When(it.Created), FontSize = Small, Foreground = _look.Dim, Margin = new Thickness(0, 0, 6, 1), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = it.Created.ToLocalTime().ToString("F", CultureInfo.CurrentCulture) });
            if (it.Pinned)
            {
                var pin = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
                var pg = Glyph("", Small * 0.9, StatusColors.Warn); pg.Margin = new Thickness(0, 0, 3, 0);
                pin.Children.Add(pg);
                pin.Children.Add(new TextBlock { Text = "Pinned", FontSize = Small, Foreground = StatusColors.Warn });
                meta.Children.Add(pin);
            }
            if (it.New || it.Dirty || it.PinDirty || it.DoneDirty)
                meta.Children.Add(new TextBlock { Text = "  · not synced yet", FontSize = Small, Foreground = _look.Dim, FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
            body.Children.Add(meta);

            if (_editing == it) { body.Children.Add(Editor(it)); return g; }

            if (!string.IsNullOrEmpty(it.Text)) body.Children.Add(Linkified(it.Text));
            else if (it.Url == null && !string.IsNullOrEmpty(it.Title)) body.Children.Add(new TextBlock { Text = it.Title, Foreground = _look.Fg, TextWrapping = TextWrapping.Wrap });
            if (it.Url != null && (it.Text ?? "").IndexOf(it.Url, StringComparison.Ordinal) < 0) body.Children.Add(Linkified(it.Url));
            FrameworkElement em = Embed(it);
            if (em != null) body.Children.Add(em);

            g.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2 && !(e.OriginalSource is Run)) { e.Handled = true; BeginEdit(it); }
            };
            return WithActions(g, it);
        }

        /// <summary>
        /// The item with vDrop's little bar of buttons at its top right while the mouse is over it: Open, Copy, Pin, Archive, Edit,
        /// Refresh preview, Delete (click it twice). The row lights up a little too.
        /// </summary>
        FrameworkElement WithActions(Grid content, VDropItem it)
        {
            content.Margin = new Thickness(6, 4, 6, 6);
            var holder = new Grid { Tag = it };
            var bg = new Border { CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Margin = new Thickness(0, 2, 0, 2) };
            holder.Children.Add(bg);
            holder.Children.Add(content);

            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            var barBox = new Border
            {
                Child = bar, Background = _t.PopupBg ?? _t.Bg, BorderBrush = _look.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, 6, 0), Visibility = Visibility.Collapsed
            };
            barBox.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.35 };
            Panel.SetZIndex(barBox, 1);
            holder.Children.Add(barBox);

            Border del = null;
            bool armed = false;
            Action disarm = delegate
            {
                if (!armed || del == null) return;
                armed = false;
                del.Background = Brushes.Transparent;
                ((TextBlock)del.Child).Foreground = _look.Dim;
                del.ToolTip = "Delete";
            };

            if (it.Url != null) bar.Children.Add(ActionButton("\uE8A7", "Open link", false, delegate { NoteRender.Open(it.Url); }));
            Border copy = null;
            copy = ActionButton("\uE8C8", it.Url != null ? "Copy link" : "Copy text", false, delegate
            {
                Copy(it.Url ?? it.Text ?? "");
                var tb = (TextBlock)copy.Child;
                tb.Text = "\uE73E";                 // a tick for a moment
                var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                back.Tick += delegate { back.Stop(); tb.Text = "\uE8C8"; };
                back.Start();
            });
            bar.Children.Add(copy);
            bar.Children.Add(ActionButton("\uE718", it.Pinned ? "Unpin" : "Pin", it.Pinned, delegate { VDrop.SetPinned(it, !it.Pinned); }));
            bar.Children.Add(it.Done ? ActionButton(UnarchiveGlyph, "Unarchive (back to the inbox)", false, delegate { VDrop.SetDone(it, false); })
                                     : ActionButton(ArchiveGlyph, "Archive", false, delegate { VDrop.SetDone(it, true); }));
            bar.Children.Add(ActionButton("\uE70F", "Edit", false, delegate { BeginEdit(it); }));
            if (it.Url != null && !it.New) bar.Children.Add(ActionButton("\uE72C", "Refresh preview", false, delegate { VDrop.RefreshPreview(it); }));
            del = ActionButton("\uE74D", "Delete", false, delegate
            {
                if (!armed)
                {
                    armed = true;
                    del.Background = StatusColors.Bad;
                    ((TextBlock)del.Child).Foreground = Brushes.White;
                    del.ToolTip = "Click again to delete";
                    return;
                }
                armed = false;
                VDrop.Delete(it);
            });
            bar.Children.Add(del);

            holder.MouseEnter += delegate
            {
                if (_editing == it) return;
                bg.Background = Theme.Faded(_t.Fg, 5);
                barBox.Visibility = Visibility.Visible;
            };
            holder.MouseLeave += delegate
            {
                bg.Background = Brushes.Transparent;
                barBox.Visibility = Visibility.Collapsed;
                disarm();
            };
            return holder;
        }

        Border ActionButton(string glyph, string tip, bool on, Action click)
        {
            var tb = Glyph(glyph, Math.Max(10, _look.Size * 0.95), on ? (Brush)_t.MainAccent : _look.Dim);
            var b = new Border
            {
                Child = tb, Width = Math.Round(_look.Size * 2.1), Height = Math.Round(_look.Size * 1.9), CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip
            };
            b.MouseEnter += delegate { if (b.Background == Brushes.Transparent) { b.Background = _look.Line; tb.Foreground = on ? (Brush)_t.MainAccent : _look.Fg; } };
            b.MouseLeave += delegate { if (b.Background == _look.Line) { b.Background = Brushes.Transparent; tb.Foreground = on ? (Brush)_t.MainAccent : _look.Dim; } };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };     // not a widget drag / double-click edit
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        /// <summary>Text with its web addresses as links.</summary>
        TextBlock Linkified(string text)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = _look.Fg };
            int last = 0;
            foreach (Match m in UrlRx.Matches(text))
            {
                string url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"');
                if (m.Index > last) tb.Inlines.Add(new Run(text.Substring(last, m.Index - last)));
                tb.Inlines.Add(Link(url, url));
                last = m.Index + url.Length;
            }
            if (last < text.Length) tb.Inlines.Add(new Run(text.Substring(last)));
            return tb;
        }

        Hyperlink Link(string text, string url)
        {
            var h = new Hyperlink(new Run(text)) { Foreground = _t.MainAccent, TextDecorations = null, Cursor = Cursors.Hand, ToolTip = url };
            h.MouseEnter += delegate { h.TextDecorations = TextDecorations.Underline; };
            h.MouseLeave += delegate { h.TextDecorations = null; };
            h.Click += delegate { NoteRender.Open(url); };
            return h;
        }

        /// <summary>The link's preview card (or the picture for an image), like vDrop's.</summary>
        FrameworkElement Embed(VDropItem it)
        {
            if (it.Url == null) return null;
            if (it.Preview == "pending")
                return new TextBlock { Text = "Getting the preview…", FontSize = Small, FontStyle = FontStyles.Italic, Foreground = _look.Dim, Margin = new Thickness(0, 4, 0, 0) };
            if (it.Preview != "ok") return null;
            if (it.Type == "image" && it.Thumb != null)
            {
                FrameworkElement pic = Picture(it.Thumb, it.ThumbW, it.ThumbH, delegate { NoteRender.Open(it.Url); }, false);
                pic.Margin = new Thickness(0, 6, 0, 0);
                return pic;
            }
            if (it.Title == null && it.Description == null && it.Thumb == null) return null;

            Brush edge = _t.MainAccent;
            Color c;
            if (!string.IsNullOrEmpty(it.Color) && Theme.TryParse(it.Color, out c)) edge = new SolidColorBrush(c);
            var card = new Border
            {
                BorderBrush = edge, BorderThickness = new Thickness(4, 0, 0, 0), Background = _look.CodeBg, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 520
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.Child = grid;

            var main = new StackPanel();
            grid.Children.Add(main);
            if (it.SiteName != null || it.Favicon != null)
            {
                var prov = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                if (it.Favicon != null)
                {
                    var fav = new Image { Width = Small * 1.3, Height = Small * 1.3, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                    Load(it.Favicon, delegate (string path) { fav.Source = Bitmap(path, 32); });
                    prov.Children.Add(fav);
                }
                if (it.SiteName != null) prov.Children.Add(new TextBlock { Text = it.SiteName, FontSize = Small, Foreground = _look.Dim, VerticalAlignment = VerticalAlignment.Center });
                main.Children.Add(prov);
            }
            if (it.Author != null) main.Children.Add(new TextBlock { Text = it.Author, FontSize = Small, FontWeight = FontWeights.SemiBold, Foreground = _look.Fg, Margin = new Thickness(0, 0, 0, 3), TextWrapping = TextWrapping.Wrap });
            if (it.Title != null)
            {
                var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) };
                title.Inlines.Add(Link(it.Title, it.Url));
                main.Children.Add(title);
            }
            if (it.Description != null)
                main.Children.Add(new TextBlock { Text = it.Description, FontSize = Small, Foreground = _look.Dim, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, MaxHeight = Small * 1.4 * 4 });

            // the picture: under the text (large, a video with a play button), or a square on the right
            bool video = it.Embed != null || it.VideoUrl != null;
            if (it.Thumb != null && (video || it.ThumbStyle == "large"))
            {
                FrameworkElement pic = Picture(it.Thumb, it.ThumbW, it.ThumbH, delegate { NoteRender.Open(it.Url); }, video);
                pic.Margin = new Thickness(0, 8, 0, 0);
                Grid.SetRow(pic, 1); Grid.SetColumnSpan(pic, 2);
                grid.Children.Add(pic);
            }
            else if (it.Thumb != null)
            {
                double s = Math.Round(_look.Size * 6);
                var img = new Image { Width = s, Height = s, Stretch = Stretch.UniformToFill, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand };
                img.Clip = new RectangleGeometry(new Rect(0, 0, s, s), 6, 6);
                Load(it.Thumb, delegate (string path) { img.Source = Bitmap(path, 200); });
                img.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; NoteRender.Open(it.Url); };
                Grid.SetColumn(img, 1);
                grid.Children.Add(img);
            }
            return card;
        }

        /// <summary>A picture at the card's width (rounded corners; a play button over a video); a grey space until it's here.</summary>
        FrameworkElement Picture(string name, int w, int h, Action click, bool play)
        {
            double ratio = w > 0 && h > 0 ? (double)h / w : 0.5625;
            var host = new Grid { Cursor = Cursors.Hand, MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left };
            var space = new Border { Background = _look.Line, CornerRadius = new CornerRadius(6) };
            var img = new Image { Stretch = Stretch.Uniform, MaxHeight = 320 };
            host.Children.Add(space);
            host.Children.Add(img);
            host.SizeChanged += delegate
            {
                if (img.Source == null) space.Height = Math.Min(320, Math.Max(40, host.ActualWidth * ratio));
                if (img.ActualWidth > 0) img.Clip = new RectangleGeometry(new Rect(0, 0, img.ActualWidth, img.ActualHeight), 6, 6);
            };
            img.SizeChanged += delegate { if (img.ActualWidth > 0) img.Clip = new RectangleGeometry(new Rect(0, 0, img.ActualWidth, img.ActualHeight), 6, 6); };
            host.HorizontalAlignment = HorizontalAlignment.Stretch;     // as wide as the card / column allows (up to MaxWidth)
            Load(name, delegate (string path)
            {
                img.Source = Bitmap(path, 640);
                space.Visibility = img.Source != null ? Visibility.Collapsed : Visibility.Visible;
            });
            if (play)
            {
                double d = 46;
                var circle = new Border
                {
                    Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x10, 0x14)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                    Child = Glyph("", 18, Brushes.White)
                };
                host.Children.Add(circle);
            }
            host.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; click(); };
            host.ToolTip = play ? "Play (opens it in your browser)" : "Open";
            return host;
        }

        /// <summary>Give the picture's file to show now, or when it arrives from vDrop.</summary>
        void Load(string name, Action<string> show)
        {
            string path = VDrop.ThumbPath(name);
            if (path != null) { show(path); return; }
            string key = System.IO.Path.GetFileName(name);
            List<Action<string>> l;
            if (!_waiting.TryGetValue(key, out l)) _waiting[key] = l = new List<Action<string>>();
            l.Add(show);
        }

        void OnThumb(string name)
        {
            List<Action<string>> l;
            if (!_waiting.TryGetValue(name, out l)) return;
            _waiting.Remove(name);
            bool atEnd = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 40;
            string path = VDrop.ThumbPath(name);
            if (path == null) return;
            foreach (Action<string> a in l) a(path);
            if (atEnd) ScrollToEnd();
        }

        static ImageSource Bitmap(string path, int decodeWidth)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(path);
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.DecodePixelWidth = decodeWidth;
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception ex) { Log.Write("vDrop picture " + path + ": " + ex.Message); return null; }
        }

        // =================================================================
        // Changing an item
        // =================================================================
        void BeginEdit(VDropItem it)
        {
            if (it == null) return;
            if (_editing != null && _editing != it) FinishEdit(true);
            _editing = it;
            BuildList();
            if (_editText != null) _keyboard(_editText);
        }

        FrameworkElement Editor(VDropItem it)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _editTitle = null;
            if (it.Url != null)
            {
                _editTitle = Box("Title", false);
                _editTitle.Text = it.Title ?? "";
                var tf = Field(_editTitle, null, "Title");
                tf.Margin = new Thickness(0, 0, 0, 4);
                panel.Children.Add(tf);
            }
            _editText = Box(it.Url != null ? "Add a note…" : "Note", true);
            _editText.Text = it.Text ?? "";
            _editText.CaretIndex = _editText.Text.Length;
            panel.Children.Add(Field(_editText, null, it.Url != null ? "Add a note…" : "Note"));
            KeyEventHandler keys = delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; FinishEdit(false); }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; FinishEdit(true); }
            };
            _editText.PreviewKeyDown += keys;
            if (_editTitle != null) _editTitle.PreviewKeyDown += keys;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            Border save = Pill("Save", true, "Ctrl+Enter");
            save.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; FinishEdit(true); };
            Border cancel = Pill("Cancel", false, "Esc");
            cancel.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; FinishEdit(false); };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            return panel;
        }

        void FinishEdit(bool keep)
        {
            VDropItem it = _editing;
            if (it == null) return;
            string text = _editText != null ? _editText.Text.Replace("\r\n", "\n") : it.Text;
            string title = _editTitle != null ? _editTitle.Text.Trim() : null;
            _editing = null; _editText = null; _editTitle = null;
            if (keep && (text != (it.Text ?? "") || (title != null && title != (it.Title ?? ""))))
            {
                if (text.Trim().Length == 0 && it.Url == null) { if (Confirm("The note is empty - delete it?")) VDrop.Delete(it); }
                else VDrop.Edit(it, text, title);
            }
            _later = false;
            BuildList();
            _endTyping();
        }

        /// <summary>The widget lost the keyboard (clicked elsewhere): keep what was typed.</summary>
        public void StopTyping()
        {
            if (_editing != null) FinishEdit(true);
            else if (_later) { _later = false; BuildList(); }
        }

        static bool Confirm(string text)
        {
            return MessageBox.Show(text, "vDrop", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        // =================================================================
        // Right-click
        // =================================================================
        public void AddMenuItems(ContextMenu m, DependencyObject source)
        {
            VDropItem it = null;
            for (DependencyObject d = source; d != null && d != this; d = Parent_(d))
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.Tag is VDropItem) { it = (VDropItem)fe.Tag; break; }
            }
            if (it != null)
            {
                VDropItem x = it;
                if (x.Url != null) Add(m, "Open link", delegate { NoteRender.Open(x.Url); });
                Add(m, x.Url != null ? "Copy link" : "Copy text", delegate { Copy(x.Url ?? x.Text ?? ""); });
                if (x.Url != null && !string.IsNullOrEmpty(x.Text)) Add(m, "Copy text", delegate { Copy(x.Text); });
                Add(m, "Edit", delegate { BeginEdit(x); });
                Add(m, x.Pinned ? "Unpin" : "Pin", delegate { VDrop.SetPinned(x, !x.Pinned); });
                Add(m, x.Done ? "Unarchive (back to the inbox)" : "Archive", delegate { VDrop.SetDone(x, !x.Done); });
                if (x.Url != null && !x.New) Add(m, "Refresh preview", delegate { VDrop.RefreshPreview(x); });
                Add(m, "Delete…", delegate
                {
                    string what = x.Title ?? x.Text ?? x.Url ?? "this";
                    if (what.Length > 60) what = what.Substring(0, 59) + "…";
                    if (Confirm("Delete \"" + what + "\" from vDrop?")) VDrop.Delete(x);
                });
                m.Items.Add(new Separator());
            }
            Add(m, "Add a note or link", FocusCompose);
            Add(m, "Sync with vDrop now", VDrop.SyncNow);
            m.Items.Add(new Separator());
        }

        static void Add(ContextMenu m, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }

        static void Copy(string s)
        {
            try { Clipboard.SetText(s ?? ""); } catch (Exception ex) { Log.Write("vDrop copy: " + ex.Message); }
        }

        static DependencyObject Parent_(DependencyObject d)
        {
            if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            var ce = d as ContentElement;
            if (ce != null) return ContentOperations.GetParent(ce) ?? LogicalTreeHelper.GetParent(d);
            return LogicalTreeHelper.GetParent(d);
        }
    }
}
