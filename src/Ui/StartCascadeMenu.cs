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
    /// The Start menu's Cascading style (0.38.200): vShell's own popup at the Start button - your pins, Programs ›, Recent files ›, your
    /// folders, Settings and Power › - and each › opens a flyout beside its row (Programs › Development › vTerm). Every part is a
    /// PopupWindow in the widget style, with the same rows as the other styles. The flyouts never take the focus, so the keys stay with
    /// the menu: Up / Down, Right or Enter opens a flyout, Left goes back, a letter jumps to the next row starting with it. Right-click a
    /// program for Pin to Start / Unpin from Start, as in the other styles.
    /// </summary>
    sealed class StartCascadeMenu
    {
        /// <summary>One popup of the cascade: the menu itself, or a flyout.</summary>
        sealed class Column
        {
            public PopupWindow Win;
            public StackPanel List;
            public readonly List<AppRow> Rows = new List<AppRow>();
            public int Sel = -1;
            /// <summary>The row whose flyout this is (null for the menu itself).</summary>
            public AppRow From;
            /// <summary>The Pin to Start / Unpin from Start flyout of a right-clicked row.</summary>
            public bool Choice;
        }

        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly FrameworkElement _button;
        readonly Theme _t;
        readonly WidgetStyle _ws;
        readonly ModuleConfig _cfg;
        readonly Action _save;
        readonly List<Column> _cols = new List<Column>();
        readonly Dictionary<AppRow, Action<Column>> _subs = new Dictionary<AppRow, Action<Column>>();
        readonly Dictionary<AppRow, Action> _pinnable = new Dictionary<AppRow, Action>();     // rows a right-click (or the menu key) offers to pin / unpin
        readonly StackPanel _pinPanel = new StackPanel();
        readonly StackPanel _restPanel = new StackPanel();
        readonly List<AppRow> _pinRows = new List<AppRow>();
        readonly List<AppRow> _restRows = new List<AppRow>();
        readonly DispatcherTimer _hover;
        Column _hoverCol;
        AppRow _hoverRow;
        int _keyCol;          // the column the arrow keys move in
        bool _closing;

        public PopupWindow Main { get { return _cols[0].Win; } }

        public StartCascadeMenu(BarManager mgr, IModuleHost bar, FrameworkElement button, Theme t, WidgetStyle ws, ModuleConfig cfg, Action save)
        {
            _mgr = mgr; _bar = bar; _button = button; _t = t; _ws = ws; _cfg = cfg; _save = save;
            _hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _hover.Tick += delegate { _hover.Stop(); HoverSettled(); };

            var main = new Column();
            var stack = new StackPanel { MinWidth = 230 };
            stack.Children.Add(_pinPanel);
            stack.Children.Add(_restPanel);
            main.List = stack;
            main.Win = MakeWindow(stack, null);
            _cols.Add(main);
            BuildPins();
            BuildRest();
            MainRows();

            main.Win.PreviewKeyDown += OnKey;
            main.Win.PreviewTextInput += OnText;
            // a click in one of its flyouts (or one taking the focus) is a click in the menu, not away from it
            main.Win.Partners = IsFlyout;
            main.Win.LogCloseAs = "Start menu (Cascading)";
            main.Win.Closed += delegate { _closing = true; _hover.Stop(); CloseAfter(0); };
        }

        // ---- the popups ----------------------------------------------------------------------------
        PopupWindow MakeWindow(StackPanel list, AppRow beside)
        {
            var scroll = new ScrollViewer
            {
                Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false, MaxWidth = 380     // long names end in "…" instead of widening the menu
            };
            try { scroll.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(WidgetWindow.ScrollXaml)); }
            catch (Exception ex) { Log.Write("Start menu scroll style: " + ex.Message); }
            scroll.Resources["wThumb"] = Theme.Faded(_t.FgDim, 70);
            var w = new PopupWindow(_mgr, _bar, beside ?? _button, _t, scroll);
            if (_ws != null) w.UseWidgetStyle(_t, _ws);
            if (beside == null)
            {
                scroll.MaxHeight = w.ContentRoom();
                return w;
            }
            // a flyout: beside its row, never taking the focus, closed with the menu (not when it loses the focus it never had)
            w.Beside = beside;
            w.NoActivate = true;
            w.ShowActivated = false;
            w.Pinned = true;
            try { w.BackdropMode = PopupGlass.ModeFor(_mgr, _bar, _button); } catch { }
            scroll.MaxHeight = Room(beside);
            return w;
        }

        /// <summary>One of this menu's flyouts (or the right-click choice).</summary>
        bool IsFlyout(IntPtr h)
        {
            for (int i = 1; i < _cols.Count; i++)
                if (new System.Windows.Interop.WindowInteropHelper(_cols[i].Win).Handle == h) return true;
            return false;
        }

        /// <summary>How tall a flyout may be: its monitor's height, less a margin.</summary>
        static double Room(FrameworkElement at)
        {
            try
            {
                Point p = at.PointToScreen(new Point(0, 0));
                MonitorInfo mon = MonitorHelper.FromPoint((int)p.X, (int)p.Y);
                double s = VisualTreeHelper.GetDpi(at).DpiScaleY;
                if (mon != null) return Math.Max(200, mon.Bounds.Height / s - 60);
            }
            catch { }
            return 600;
        }

        void OpenSub(Column col, AppRow row, bool byKey)
        {
            Action<Column> fill;
            if (_closing || !_subs.TryGetValue(row, out fill)) return;
            OpenColumn(col, row, fill, byKey, false);
        }

        /// <summary>A flyout beside row, filled by fill. choice: the Pin to Start / Unpin from Start flyout (not the row's own flyout).</summary>
        void OpenColumn(Column col, AppRow row, Action<Column> fill, bool byKey, bool choice)
        {
            if (_closing) return;
            int ci = _cols.IndexOf(col);
            if (ci < 0) return;
            if (ci + 1 < _cols.Count && _cols[ci + 1].From == row && _cols[ci + 1].Choice == choice)
            {
                if (byKey) { _keyCol = ci + 1; Select(_cols[ci + 1], 0); }
                return;     // already open
            }
            CloseAfter(ci);
            var sub = new Column { From = row, Choice = choice, List = new StackPanel { MinWidth = choice ? 160 : 200 } };
            try { fill(sub); }
            catch (Exception ex) { Log.Write("Start menu: " + ex.Message); }
            if (sub.Rows.Count == 0 && sub.List.Children.Count == 0) sub.List.Children.Add(Hint("Nothing here"));
            sub.Win = MakeWindow(sub.List, row);
            _cols.Add(sub);
            PopupWindow w = sub.Win;
            w.Closed += delegate
            {
                int i = _cols.IndexOf(sub);
                if (i > 0) CloseAfter(i - 1);
            };
            // if Windows gives a flyout the focus after all, the keys still work
            w.PreviewKeyDown += OnKey;
            w.PreviewTextInput += OnText;
            w.Show();
            LoadIcons(sub);
            if (byKey) { _keyCol = _cols.Count - 1; Select(sub, 0); }
        }

        /// <summary>Close the flyouts after this column (-1 never: the menu itself closes with its popup).</summary>
        void CloseAfter(int ci)
        {
            while (_cols.Count > ci + 1)
            {
                Column c = _cols[_cols.Count - 1];
                _cols.RemoveAt(_cols.Count - 1);
                try { c.Win.CloseSafe(); } catch { }
            }
            if (_keyCol > ci) _keyCol = Math.Max(0, ci);
        }

        void Close()
        {
            _closing = true;
            Main.CloseSafe();
        }

        // ---- the menu's rows ------------------------------------------------------------------------------
        void BuildPins()
        {
            Column main = _cols[0];
            foreach (AppRow r in _pinRows) { _pinnable.Remove(r); _subs.Remove(r); }
            _pinPanel.Children.Clear();
            _pinRows.Clear();
            foreach (LauncherEntry e in _cfg.Entries) AddEntry(main, _pinPanel, _pinRows, e, true);
            if (_pinRows.Count == 0) _pinPanel.Children.Add(Hint("Right-click a program to pin it here"));
        }

        void BuildRest()
        {
            Column main = _cols[0];
            _restPanel.Children.Add(Line());
            Add(main, _restPanel, _restRows, "Programs", "", null, delegate (Column c) { FillPrograms(c, ""); });
            if (_cfg.StartRecent) Add(main, _restPanel, _restRows, "Recent files", "", null, FillRecent);
            _restPanel.Children.Add(Line());
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(main, _restPanel, _restRows, Environment.UserName, "", delegate { Launch(profile); }, null);
            Add(main, _restPanel, _restRows, "Documents", "", delegate { Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); }, null);
            Add(main, _restPanel, _restRows, "Downloads", "", delegate { Launch(System.IO.Path.Combine(profile, "Downloads")); }, null);
            Add(main, _restPanel, _restRows, "Pictures", "", delegate { Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)); }, null);
            Add(main, _restPanel, _restRows, "Settings", "", delegate { Launch("ms-settings:"); }, null);
            _restPanel.Children.Add(Line());
            Add(main, _restPanel, _restRows, "Power", "", null, delegate (Column c)
            {
                Add(c, c.List, c.Rows, "Lock", "", delegate { Close(); PowerActions.Lock(); }, null);
                Add(c, c.List, c.Rows, "Sleep", "", delegate { Close(); PowerActions.Sleep(); }, null);
                Add(c, c.List, c.Rows, "Sign out", "", delegate { Close(); PowerActions.SignOut(); }, null);
                Add(c, c.List, c.Rows, "Restart", "", delegate { Close(); PowerActions.Restart(); }, null);
                Add(c, c.List, c.Rows, "Shut down", "", delegate { Close(); PowerActions.ShutDown(); }, null);
            });
        }

        /// <summary>The menu's rows in order for the keys: the pins, then the rest.</summary>
        void MainRows()
        {
            Column main = _cols[0];
            main.Rows.Clear();
            main.Rows.AddRange(_pinRows);
            main.Rows.AddRange(_restRows);
            for (int i = 0; i < main.Rows.Count; i++) main.Rows[i].Index = i;
            main.Sel = -1;
        }

        /// <summary>Pinned / unpinned: the pins at the top again (a flyout from a pin that went closes).</summary>
        void PinsChanged()
        {
            if (_closing) return;
            if (_cols.Count > 1 && _pinRows.Contains(_cols[1].From)) CloseAfter(0);
            BuildPins();
            MainRows();
        }

        /// <summary>A pinned shortcut (starts it) or group (its shortcuts and groups in a flyout). topLevel: one of the pins (right-click unpins it).</summary>
        void AddEntry(Column col, Panel panel, List<AppRow> rows, LauncherEntry en, bool topLevel)
        {
            LauncherEntry e = en;
            var a = new AppEntry { Name = string.IsNullOrEmpty(e.Name) ? (e.IsGroup ? "Group" : e.Path) : e.Name, Target = e.Path, IconTried = true };
            var look = new RowLook();
            Action<Column> sub = null;
            if (e.IsGroup)
            {
                a.Icon = IconUtil.FromIconFile(e.IconPath);
                bool builtIn = IconUtil.GroupGlyph(e.IconGlyph) != null;
                look.Glyph = IconUtil.GroupGlyph(e.IconGlyph) ?? IconUtil.GlyphFolder;
                look.GlyphBrush = !builtIn || e.IconAccent ? _t.MainAccent : _t.Fg;
                look.Chevron = true;
                sub = delegate (Column c)
                {
                    if (LauncherFolders.Sync(e)) _save();     // a synced folder / bookmarks: pick up new / deleted shortcuts
                    foreach (LauncherEntry ch in e.Children) if (ch.IsGroup) AddEntry(c, c.List, c.Rows, ch, false);
                    foreach (LauncherEntry ch in e.Children) if (!ch.IsGroup) AddEntry(c, c.List, c.Rows, ch, false);
                    if (c.Rows.Count == 0) c.List.Children.Add(Hint("Empty group"));
                };
            }
            else
            {
                a.Icon = LauncherModule.EntryIcon(e);
                look.Glyph = LauncherModule.EntryGlyph(e);
                look.GlyphBrush = _t.Fg;
                look.Tip = string.IsNullOrEmpty(e.Name) || e.Name == e.Path ? e.Path : e.Name + "\n" + e.Path;
                look.Open = delegate { Close(); LauncherModule.Run(e); };
            }
            AppRow row = AddRow(col, panel, rows, a, look, sub);
            if (topLevel) _pinnable[row] = delegate { PinChoice(col, row, null, null, e); };
            if (topLevel)
                row.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs ev)
                {
                    ev.Handled = true;
                    PinChoice(col, row, null, null, e);
                };
        }

        /// <summary>
        /// Right-click on a program or a pin: a small flyout beside it with Pin to Start / Unpin from Start (one of the menu's own
        /// popups - a Windows menu doesn't work from the flyouts, which never take the focus). Picking it pins / unpins and closes it;
        /// the other flyouts stay open.
        /// </summary>
        void PinChoice(Column col, AppRow row, string name, string target, LauncherEntry pinned)
        {
            _hover.Stop();
            LauncherEntry cur = pinned ?? StartPins.Find(_cfg, target);
            OpenColumn(col, row, delegate (Column c)
            {
                Add(c, c.List, c.Rows, cur != null ? "Unpin from Start" : "Pin to Start", cur != null ? "" : "", delegate
                {
                    if (cur != null) _cfg.Entries.Remove(cur);
                    else StartPins.Toggle(_cfg, name, target);
                    _save();
                    int i = _cols.IndexOf(col);
                    if (i >= 0) CloseAfter(i);
                    PinsChanged();
                }, null);
            }, false, true);
        }

        /// <summary>A row with a glyph: click runs open, or (sub) opens a flyout filled by sub.</summary>
        AppRow Add(Column col, Panel panel, List<AppRow> rows, string text, string glyph, Action open, Action<Column> sub)
        {
            var look = new RowLook { Glyph = glyph, GlyphBrush = _t.MainAccent, Chevron = sub != null, Open = open };
            return AddRow(col, panel, rows, new AppEntry { Name = text, IconTried = true }, look, sub);
        }

        AppRow AddRow(Column col, Panel panel, List<AppRow> rows, AppEntry a, RowLook look, Action<Column> sub)
        {
            var row = new AppRow(a, _t.MainAccent, null, null, look) { Index = rows.Count };
            if (sub != null) _subs[row] = sub;
            row.MouseEnter += delegate
            {
                int ci = _cols.IndexOf(col);
                if (ci < 0) return;
                _keyCol = ci;
                Select(col, col.Rows.IndexOf(row));
                _hoverCol = col; _hoverRow = row;
                _hover.Stop();
                _hover.Start();
            };
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Activate(col, row, false); };
            // a program (from Programs): right-click pins / unpins it
            if (look == null && !string.IsNullOrEmpty(a.Target))
                row.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    PinChoice(col, row, a.Name, a.Target, null);
                };
            if (look == null && !string.IsNullOrEmpty(a.Target)) _pinnable[row] = delegate { PinChoice(col, row, a.Name, a.Target, null); };
            rows.Add(row);
            panel.Children.Add(row);
            return row;
        }

        /// <summary>A Start Menu folder's sub-folders then its programs; "" = the top (Programs). Show folders off: every program in one list.</summary>
        void FillPrograms(Column c, string folder)
        {
            string pre = folder.Length == 0 ? "" : folder + "\\";
            var subs = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            var here = new List<AppEntry>();
            foreach (AppEntry a in AppCatalog.Get())
            {
                if (!_cfg.StartFolders) { here.Add(a); continue; }
                if (string.Equals(a.Folder, folder, StringComparison.OrdinalIgnoreCase)) here.Add(a);
                else if (a.Folder.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) subs.Add(a.Folder.Substring(pre.Length).Split('\\')[0]);
            }
            foreach (string s in subs)
            {
                string path = pre + s;
                Add(c, c.List, c.Rows, s, IconUtil.GlyphFolder, null, delegate (Column sc) { FillPrograms(sc, path); });
            }
            foreach (AppEntry a in here) AddRow(c, c.List, c.Rows, a, null, null);
        }

        void FillRecent(Column c)
        {
            foreach (RecentFile f in RecentFiles.Get(_cfg.StartRecentCount))
            {
                string link = f.Link;
                var look = new RowLook
                {
                    Glyph = IconUtil.GlyphFile, GlyphBrush = _t.FgDim,
                    Note = f.When.Date == DateTime.Today ? TimeText.Clock(f.When) : TimeText.Day(f.When),
                    Tip = f.Name + "\n" + TimeText.Full(f.When),
                    Open = delegate { Launch(link); }
                };
                AddRow(c, c.List, c.Rows, new AppEntry { Name = f.Name, Target = link }, look, null);
            }
            if (c.Rows.Count == 0) c.List.Children.Add(Hint("No recent files."));
        }

        /// <summary>Icons the background loader hasn't got to yet, a few at a time between UI events.</summary>
        void LoadIcons(Column c)
        {
            var queue = new Queue<AppRow>();
            foreach (AppRow r in c.Rows) if (r.NeedsIcon) queue.Enqueue(r); else r.LoadIcon();
            if (queue.Count == 0) return;
            Action step = null;
            step = delegate
            {
                if (_closing) return;
                for (int n = 0; n < 8 && queue.Count > 0; n++) queue.Dequeue().LoadIcon();
                if (queue.Count > 0) c.List.Dispatcher.BeginInvoke(step, DispatcherPriority.Background);
            };
            c.List.Dispatcher.BeginInvoke(step, DispatcherPriority.Background);
        }

        TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, Foreground = _t.FgDim, Margin = new Thickness(10, 6, 10, 6), TextWrapping = TextWrapping.Wrap };
        }

        Border Line()
        {
            return new Border { Height = 1, Background = _t.Track, Margin = new Thickness(8, 4, 8, 4) };
        }

        // ---- pointer, keys and actions ----------------------------------------------------------------------
        /// <summary>The pointer rested on a row: open its flyout, or close the flyouts after its column.</summary>
        void HoverSettled()
        {
            if (_closing || _hoverRow == null) return;
            int ci = _cols.IndexOf(_hoverCol);
            if (ci < 0) return;
            if (_subs.ContainsKey(_hoverRow)) OpenSub(_hoverCol, _hoverRow, false);
            else CloseAfter(ci);
        }

        void Activate(Column col, AppRow row, bool byKey)
        {
            _hover.Stop();
            if (_subs.ContainsKey(row)) { OpenSub(col, row, byKey); return; }
            if (row.Open != null) { row.Open(); return; }
            Launch(row.Entry.Target);
        }

        void Launch(string target)
        {
            Close();
            StartPins.Open(target);
        }

        void Select(Column c, int i)
        {
            if (c.Sel >= 0 && c.Sel < c.Rows.Count) { c.Rows[c.Sel].Background = Brushes.Transparent; c.Rows[c.Sel].SetActive(false); }
            c.Sel = i >= 0 && i < c.Rows.Count ? i : -1;
            if (c.Sel < 0) return;
            c.Rows[c.Sel].Background = _t.ActiveBg;
            c.Rows[c.Sel].SetActive(true);
            c.Rows[c.Sel].BringIntoView();
        }

        Column KeyColumn { get { return _cols[Math.Max(0, Math.Min(_keyCol, _cols.Count - 1))]; } }

        void OnKey(object sender, KeyEventArgs e)
        {
            Column c = KeyColumn;
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;     // F10 comes as a system key
            switch (k)
            {
                case Key.Down:
                    Select(c, c.Sel < 0 ? 0 : Math.Min(c.Rows.Count - 1, c.Sel + 1));
                    e.Handled = true;
                    break;
                case Key.Up:
                    Select(c, c.Sel < 0 ? c.Rows.Count - 1 : Math.Max(0, c.Sel - 1));
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Enter:
                    if (c.Sel >= 0 && c.Sel < c.Rows.Count && (e.Key == Key.Enter || _subs.ContainsKey(c.Rows[c.Sel])))
                        Activate(c, c.Rows[c.Sel], true);
                    e.Handled = true;
                    break;
                case Key.Apps:
                case Key.F10:
                    // the menu key (or Shift+F10): Pin to Start / Unpin from Start for the row picked
                    Action pin;
                    if ((k == Key.Apps || Keyboard.Modifiers == ModifierKeys.Shift) && c.Sel >= 0 && c.Sel < c.Rows.Count && _pinnable.TryGetValue(c.Rows[c.Sel], out pin))
                    {
                        pin();
                        if (_cols.Count > 0) { _keyCol = _cols.Count - 1; Select(_cols[_cols.Count - 1], 0); }
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Back:
                    int ci = _cols.IndexOf(c);
                    if (ci > 0) { CloseAfter(ci - 1); _keyCol = ci - 1; }
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>A letter: the next row in the column the keys are in whose name starts with it.</summary>
        void OnText(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            Column c = KeyColumn;
            int n = c.Rows.Count;
            for (int k = 1; k <= n; k++)
            {
                int i = ((c.Sel < 0 ? -1 : c.Sel) + k) % n;
                if (c.Rows[i].Entry.Name.StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase)) { Select(c, i); break; }
            }
            e.Handled = true;
        }
    }
}
