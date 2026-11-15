using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VShellShelf
{
    /// <summary>
    /// A shelf as you see it - in a bar module's popup or in a widget: the tabs (each shelf, the Clipboard tab, +), the items and a
    /// footer. Files are dragged in and out (Explorer copies or moves them), text and pictures become cards, the Clipboard tab shows
    /// Windows' clipboard history.
    /// </summary>
    public sealed class ShelfView : Border
    {
        public const string ClipTab = "clipboard";
        /// <summary>The tab a popup opens on (the one used last).</summary>
        static string _lastTab;

        readonly Theme _t;
        readonly double _fs;
        readonly string _only;          // null = the tabs; else one shelf's id, or ClipTab
        readonly int _rows;
        readonly bool _widget;

        /// <summary>A popup keeps itself open while something is dragged out of it (true), and goes back to normal after (false).</summary>
        public Action<bool> Hold;
        /// <summary>Something was put back on the clipboard from the Clipboard tab (a popup closes).</summary>
        public Action Used;

        string _tab;
        readonly HashSet<string> _sel = new HashSet<string>();
        string _anchor;
        readonly Dictionary<string, Border> _rowsById = new Dictionary<string, Border>();
        readonly HashSet<string> _missing = new HashSet<string>();
        readonly WrapPanel _tabs;
        readonly StackPanel _list;
        readonly ScrollViewer _scroll;
        readonly DockPanel _foot;
        readonly TextBlock _status;
        string _note;
        DispatcherTimer _noteTimer, _springTimer;
        Point _press;
        object _pressed;            // the row item pressed (a ShelfItem or a ClipEntry), until the drag starts or the button comes up
        bool _subscribed;

        double RowH { get { return Math.Round(_fs * 3.1); } }

        public ShelfView(Theme t, double fontSize, string only, int rows, bool widget)
        {
            _t = t;
            _fs = Math.Max(10, fontSize);
            _only = string.IsNullOrEmpty(only) ? null : only;
            _rows = Math.Max(2, Math.Min(30, rows));
            _widget = widget;
            Background = Brushes.Transparent;
            Focusable = true;
            FocusVisualStyle = null;
            AllowDrop = true;
            if (!widget) Width = Math.Round(_fs * 30);

            var dock = new DockPanel { LastChildFill = true };
            _tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(_tabs, Dock.Top);
            if (_only == null) dock.Children.Add(_tabs);

            _foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
            DockPanel.SetDock(_foot, Dock.Bottom);
            _status = new TextBlock { Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _fs * 0.85, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            dock.Children.Add(_foot);

            _list = new StackPanel();
            _scroll = new ScrollViewer
            {
                Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = _rows * (RowH + 2), Background = Brushes.Transparent
            };
            WidgetWindow.StyleScrollbars(_scroll);
            dock.Children.Add(_scroll);
            Child = dock;

            _tab = _only ?? _lastTab;
            Loaded += delegate { Subscribe(true); Draw(); };
            Unloaded += delegate { Subscribe(false); };
            DragOver += delegate (object o, DragEventArgs e) { e.Effects = EffectFor(e, DropShelf()); e.Handled = true; };
            Drop += delegate (object o, DragEventArgs e) { DropOn(DropShelf(), e); };
            PreviewKeyDown += OnKey;
            PreviewMouseDown += delegate { if (!_widget) Focus(); };
            MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ListMenu(); };
            // (a widget: a press anywhere on it mustn't start dragging the widget)
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            Draw();
        }

        void Subscribe(bool on)
        {
            if (on == _subscribed) return;
            _subscribed = on;
            if (on) { ShelfStore.Changed += Draw; ClipHistory.Changed += ClipChanged; }
            else { ShelfStore.Changed -= Draw; ClipHistory.Changed -= ClipChanged; }
        }

        void ClipChanged() { if (_tab == ClipTab) Draw(); }

        static bool ClipOn { get { return ShelfSettings.Current != null && ShelfSettings.Current.ClipTab; } }

        /// <summary>The shelf shown (null on the Clipboard tab).</summary>
        Shelf Cur
        {
            get
            {
                if (_tab == ClipTab) return null;
                return (_tab != null ? ShelfStore.Find(_tab) : null) ?? ShelfStore.Shelves[0];
            }
        }

        /// <summary>Where a drop on the list goes: the shelf shown; on the Clipboard tab the shelf Explorer adds to.</summary>
        Shelf DropShelf()
        {
            return Cur ?? ShelfStore.Default(ShelfSettings.Current != null ? ShelfSettings.Current.AddTo : null);
        }

        // =================================================================
        // Drawing
        // =================================================================
        void Draw()
        {
            if (_tab == ClipTab && !ClipOn) _tab = null;
            if (_tab != ClipTab && (_tab == null || ShelfStore.Find(_tab) == null)) _tab = ShelfStore.Shelves[0].Id;
            if (_only == null) _lastTab = _tab;
            DrawTabs();
            _list.Children.Clear();
            _rowsById.Clear();
            _foot.Children.Clear();
            if (_tab == ClipTab) DrawClipboard();
            else DrawShelf(Cur);
        }

        TextBlock Txt(string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = fg, FontFamily = _t.Font, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        TextBlock Glyph(string g, Brush fg, double size)
        {
            return new TextBlock { Text = g, FontFamily = IconUtil.GlyphFont, Foreground = fg, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        SolidColorBrush ShelfBrush(Shelf s) { return new SolidColorBrush(Theme.Parse(s.Color, Colors.SteelBlue)); }

        void DrawTabs()
        {
            if (_only != null) return;
            _tabs.Children.Clear();
            foreach (Shelf s0 in ShelfStore.Shelves)
            {
                Shelf s = s0;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = ModulePics.Dot(ShelfBrush(s), Math.Round(_fs * 0.6));
                dot.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(dot);
                sp.Children.Add(Txt(s.Name, _t.Fg, _fs * 0.92, s.Id == _tab));
                if (s.Items.Count > 0)
                {
                    var c = Txt(" " + s.Items.Count, _t.FgDim, _fs * 0.85);
                    sp.Children.Add(c);
                }
                Border tab = Tab(sp, s.Id == _tab, ShelfBrush(s), (s.Pocket ? "A Pocket: what you drag out of it leaves it\n" : "") + "Right-click for its name, colour and more");
                tab.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Show(s.Id); };
                tab.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TabMenu(s); };
                // drop on a tab: onto that shelf; hold a drag over it a moment to open it
                tab.AllowDrop = true;
                tab.DragEnter += delegate (object o, DragEventArgs e) { Spring(s.Id); };
                tab.DragLeave += delegate { StopSpring(); };
                tab.DragOver += delegate (object o, DragEventArgs e) { e.Effects = EffectFor(e, s); e.Handled = true; };
                tab.Drop += delegate (object o, DragEventArgs e) { StopSpring(); DropOn(s, e); };
                _tabs.Children.Add(tab);
            }
            if (ClipOn)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var g = Glyph("", _t.FgDim, _fs * 0.9);
                g.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(g);
                sp.Children.Add(Txt("Clipboard", _t.Fg, _fs * 0.92, _tab == ClipTab));
                Border tab = Tab(sp, _tab == ClipTab, _t.MainAccent, "Windows' clipboard history (Win+V)");
                tab.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Show(ClipTab); };
                tab.AllowDrop = true;
                tab.DragOver += delegate (object o, DragEventArgs e) { e.Effects = DragDropEffects.None; e.Handled = true; };
                _tabs.Children.Add(tab);
            }
            Border add = Tab(Glyph("", _t.FgDim, _fs * 0.8), false, _t.FgDim, "A new shelf");
            add.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; NewShelf(); };
            _tabs.Children.Add(add);
        }

        Border Tab(UIElement content, bool on, SolidColorBrush color, string tip)
        {
            var b = new Border
            {
                Child = content, CornerRadius = new CornerRadius(Math.Max(4, _t.Radius * 0.6)), Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 4), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip,
                Background = on ? (Brush)Theme.Faded(color, 30) : Brushes.Transparent,
                BorderBrush = on ? (Brush)Theme.Faded(color, 70) : Theme.Faded(_t.Fg, 18)
            };
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            return b;
        }

        void Show(string tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _sel.Clear();
            _anchor = null;
            if (tab == ClipTab && ClipHistory.Latest == null) ClipHistory.Refresh();
            Draw();
        }

        void Spring(string tab)
        {
            StopSpring();
            _springTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _springTimer.Tick += delegate { StopSpring(); Show(tab); };
            _springTimer.Start();
        }

        void StopSpring() { if (_springTimer != null) { _springTimer.Stop(); _springTimer = null; } }

        /// <summary>A short note in the footer for a few seconds ("Copied - paste it with Ctrl+V").</summary>
        void Note(string text)
        {
            _note = text;
            if (_noteTimer == null)
            {
                _noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _noteTimer.Tick += delegate { _noteTimer.Stop(); _note = null; Draw(); };
            }
            _noteTimer.Stop();
            _noteTimer.Start();
            Draw();
        }

        Border FootButton(string glyph, string text, string tip)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null)
            {
                var g = Glyph(glyph, _t.Fg, _fs * 0.8);
                g.Margin = new Thickness(0, 0, text != null ? 5 : 0, 0);
                sp.Children.Add(g);
            }
            if (text != null) sp.Children.Add(Txt(text, _t.Fg, _fs * 0.85));
            var b = new Border
            {
                Child = sp, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(Math.Max(4, _t.Radius * 0.6)),
                Background = Theme.Faded(_t.Fg, 8), Cursor = Cursors.Hand, ToolTip = tip
            };
            b.MouseEnter += delegate { b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = Theme.Faded(_t.Fg, 8); };
            // (no Handled on the press here: a button's own press handler wouldn't run - the view marks presses handled for widgets)
            DockPanel.SetDock(b, Dock.Right);
            return b;
        }

        void Hint(string text)
        {
            var h = Txt(text, _t.FgDim, _fs * 0.92);
            h.TextWrapping = TextWrapping.Wrap;
            h.TextTrimming = TextTrimming.None;
            h.Margin = new Thickness(4, 8, 4, 8);
            _list.Children.Add(h);
        }

        // ---- a shelf ----
        void DrawShelf(Shelf s)
        {
            _sel.RemoveWhere(delegate (string id) { return !s.Items.Exists(delegate (ShelfItem i) { return i.Id == id; }); });
            if (s.Items.Count == 0)
                Hint(s.Pocket ? "Your Pocket is empty. Drop files, folders, text or pictures here to gather them, then drag them all out together - they leave the Pocket once they're dropped."
                              : "Drop files, folders, text or pictures here" + (_widget ? "." : ", or paste with Ctrl+V.") + " Files stay where they are - drag them out to copy them (Shift moves them).");
            foreach (ShelfItem i in s.Items) _list.Children.Add(ItemRow(s, i));

            if (s.Items.Count > 0)
            {
                Border all = FootButton("", "Drag all", "Drag this to take everything on the shelf at once");
                all.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _press = e.GetPosition(this);
                    _pressed = s;
                    all.CaptureMouse();         // a quick drag off the small button still counts
                };
                all.MouseMove += delegate (object o, MouseEventArgs e)
                {
                    if (!(_pressed is Shelf) || e.LeftButton != MouseButtonState.Pressed || !Moved(e)) return;
                    _pressed = null;
                    all.ReleaseMouseCapture();
                    DragOut(s, new List<ShelfItem>(s.Items));
                };
                all.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    all.ReleaseMouseCapture();
                    if (_pressed is Shelf) { _pressed = null; Note("Drag \"Drag all\" to where you want everything"); }
                };
                _foot.Children.Add(all);
            }
            if (!_widget)
            {
                Border paste = FootButton("", null, "Paste what you copied onto this shelf (Ctrl+V)");
                paste.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; PasteHere(); };
                _foot.Children.Add(paste);
            }
            int n = s.Items.Count;
            _status.Text = _note ?? (n == 0 ? (s.Pocket ? "Pocket" : s.Name) : n + " item" + (n == 1 ? "" : "s") + (_sel.Count > 1 ? " · " + _sel.Count + " selected" : ""));
            _foot.Children.Add(_status);
        }

        Border ItemRow(Shelf s, ShelfItem item)
        {
            double h = RowH, pic = h - 8;
            var row = new Border { Height = h, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(Math.Max(4, _t.Radius * 0.6)), Cursor = Cursors.Hand };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pic + 10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var picBox = new Grid { Width = pic, Height = pic, HorizontalAlignment = HorizontalAlignment.Left };
            string glyph = item.Kind == "folder" ? "" : item.Kind == "text" ? "" : item.Kind == "image" ? "" : item.Kind == "link" ? "" : "";
            var gl = Glyph(glyph, _t.FgDim, pic * 0.6);
            var img = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            picBox.Children.Add(gl);
            picBox.Children.Add(img);
            g.Children.Add(picBox);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string name = item.Kind == "link" ? LinkName(item) : item.Name;
            var nameT = Txt(name, _t.Fg, _fs);
            var subT = Txt(Sub(item), _t.FgDim, _fs * 0.8);
            texts.Children.Add(nameT);
            texts.Children.Add(subT);
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            row.Child = g;
            row.ToolTip = item.Path + "\nDrag it out to copy it (Shift moves, Alt makes a shortcut) · double-click opens it · right-click for more";
            _rowsById[item.Id] = row;
            Paint(row, item.Id);
            if (_missing.Contains(item.Id)) MarkMissing(nameT, subT);

            int px = (int)Math.Ceiling(pic * 2);
            ShellThumbs.Get(item.Path, px, delegate (ImageSource source, bool there)
            {
                if (source != null) { img.Source = source; gl.Visibility = Visibility.Collapsed; }
                if (!there) { _missing.Add(item.Id); MarkMissing(nameT, subT); }
                else if (_missing.Remove(item.Id)) { nameT.Opacity = 1; subT.Text = Sub(item); subT.Foreground = _t.FgDim; }
            });

            row.MouseEnter += delegate { if (!_sel.Contains(item.Id)) row.Background = _t.Hover; };
            row.MouseLeave += delegate { Paint(row, item.Id); };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (!_widget) Focus();
                if (e.ClickCount == 2) { _pressed = null; Open(item); return; }
                _press = e.GetPosition(this);
                _pressed = item;
            };
            row.MouseMove += delegate (object o, MouseEventArgs e)
            {
                if (_pressed != item || e.LeftButton != MouseButtonState.Pressed || !Moved(e)) return;
                _pressed = null;
                if (!_sel.Contains(item.Id)) { _sel.Clear(); _sel.Add(item.Id); _anchor = item.Id; Repaint(); }
                DragOut(s, Selected(s));
            };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_pressed != item) return;
                _pressed = null;
                Click(s, item);
            };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (!_sel.Contains(item.Id)) { _sel.Clear(); _sel.Add(item.Id); _anchor = item.Id; Repaint(); }
                ItemMenu(s, Selected(s));
            };
            return row;
        }

        void MarkMissing(TextBlock name, TextBlock sub)
        {
            name.Opacity = 0.55;
            sub.Text = "Not there any more - moved, renamed or deleted (right-click to find it)";
            sub.Foreground = StatusColors.Warn;
        }

        static string Sub(ShelfItem i)
        {
            if (i.Kind == "text") return "Text · " + TimeText.Ago(i.Added);
            if (i.Kind == "image") return "Picture · " + TimeText.Ago(i.Added);
            if (i.Kind == "link") return LinkUrl(i);
            string f = i.Folder;
            return f.Length > 0 ? f : i.Kind == "folder" ? "Drive" : "";
        }

        /// <summary>The address in a link card (.url).</summary>
        static string LinkUrl(ShelfItem i)
        {
            try
            {
                foreach (string l in File.ReadAllLines(i.Path))
                    if (l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return l.Substring(4).Trim();
            }
            catch { }
            return "";
        }

        static string LinkName(ShelfItem i) { return System.IO.Path.GetFileNameWithoutExtension(i.Path); }

        void Paint(Border row, string id)
        {
            row.Background = _sel.Contains(id) ? (Brush)Theme.Faded(_t.MainAccent, 30) : Brushes.Transparent;
        }

        void Repaint()
        {
            foreach (KeyValuePair<string, Border> kv in _rowsById) Paint(kv.Value, kv.Key);
            Shelf s = Cur;
            if (s != null && _note == null)
            {
                int n = s.Items.Count;
                _status.Text = n + " item" + (n == 1 ? "" : "s") + (_sel.Count > 1 ? " · " + _sel.Count + " selected" : "");
            }
        }

        bool Moved(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            return Math.Abs(p.X - _press.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _press.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        List<ShelfItem> Selected(Shelf s)
        {
            return s.Items.FindAll(delegate (ShelfItem i) { return _sel.Contains(i.Id); });
        }

        void Click(Shelf s, ShelfItem item)
        {
            ModifierKeys m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0)
            {
                if (!_sel.Remove(item.Id)) _sel.Add(item.Id);
                _anchor = item.Id;
            }
            else if ((m & ModifierKeys.Shift) != 0 && _anchor != null)
            {
                int a = s.Items.FindIndex(delegate (ShelfItem i) { return i.Id == _anchor; });
                int b = s.Items.IndexOf(item);
                if (a < 0) a = b;
                _sel.Clear();
                for (int k = Math.Min(a, b); k <= Math.Max(a, b); k++) _sel.Add(s.Items[k].Id);
            }
            else
            {
                _sel.Clear();
                _sel.Add(item.Id);
                _anchor = item.Id;
            }
            Repaint();
        }

        // =================================================================
        // Dragging out, dropping in
        // =================================================================

        /// <summary>
        /// Drag items to Explorer or any app: they go as files (Explorer copies them - or moves them with Shift, or as the setting says -
        /// and makes a shortcut with Alt), a text card as its text too. Afterwards what's gone from where it was (moved) leaves the
        /// shelf, and a Pocket lets go of everything that was dropped.
        /// </summary>
        void DragOut(Shelf s, List<ShelfItem> items)
        {
            var paths = new StringCollection();
            foreach (ShelfItem i in items) if (ShelfStore.Exists(i.Path)) paths.Add(i.Path);
            if (paths.Count == 0) { Note(items.Count == 1 ? "It isn't there any more" : "They aren't there any more"); return; }
            var d = new DataObject();
            d.SetFileDropList(paths);
            string how = ShelfSettings.Current != null ? ShelfSettings.Current.DragOut : "Copies";
            // what Explorer does with no key held: copy (1) or move (2) - Shift / Ctrl / Alt still choose
            if (how != "Explorer") d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(how == "Moves" ? 2 : 1)));
            if (items.Count == 1 && items[0].Kind == "text")
            {
                try { d.SetText(File.ReadAllText(items[0].Path)); } catch { }
            }
            else if (items.Count == 1 && items[0].Kind == "link")
            {
                string url = LinkUrl(items[0]);
                if (url.Length > 0) d.SetText(url);
            }
            var ids = new List<string> { s.Id };
            foreach (ShelfItem i in items) ids.Add(i.Id);
            d.SetData(ShelfStore.ItemsFormat, string.Join("|", ids));

            DragDropEffects r = DragDropEffects.None;
            if (Hold != null) Hold(true);
            try { r = DragDrop.DoDragDrop(this, d, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (COMException ex) { Log.Write("Shelf: drag - " + ex.Message); }
            finally { if (Hold != null) Hold(false); }

            // Explorer may still be copying / moving: look a moment later
            bool dropped = r != DragDropEffects.None;
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            later.Tick += delegate
            {
                later.Stop();
                var go = items.FindAll(delegate (ShelfItem i) { return s.Items.Contains(i) && ((s.Pocket && dropped) || !ShelfStore.Exists(i.Path)); });
                if (go.Count > 0) ShelfStore.Remove(s, go);
            };
            later.Start();
        }

        /// <summary>What dropping here does: remember it (a link) - never a move, so the place it came from keeps it.</summary>
        static DragDropEffects EffectFor(DragEventArgs e, Shelf target)
        {
            if (target == null) return DragDropEffects.None;
            if (e.Data.GetDataPresent(ShelfStore.ItemsFormat))
            {
                string v = e.Data.GetData(ShelfStore.ItemsFormat) as string ?? "";
                if (v.StartsWith(target.Id + "|")) return DragDropEffects.None;      // its own items, back onto it
                return DragDropEffects.Link;
            }
            if (!ShelfStore.CanTake(e.Data)) return DragDropEffects.None;
            if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        void DropOn(Shelf s, DragEventArgs e)
        {
            e.Handled = true;
            DragDropEffects eff = EffectFor(e, s);
            if (eff == DragDropEffects.None) { e.Effects = DragDropEffects.None; return; }
            int n = ShelfStore.AddFrom(s, e.Data);
            e.Effects = n > 0 ? eff : DragDropEffects.None;
            if (n > 0)
            {
                if (_only == null && _tab != s.Id) { _tab = s.Id; _sel.Clear(); }
                Note(n == 1 ? "On " + s.Name : n + " on " + s.Name);
            }
        }

        void PasteHere()
        {
            Shelf s = DropShelf();
            int n = ShelfStore.Paste(s);
            if (n > 0 && _tab != s.Id && _only == null) _tab = s.Id;
            Note(n > 0 ? (n == 1 ? "Pasted onto " + s.Name : n + " pasted onto " + s.Name) : "There's nothing to paste");
        }

        void OnKey(object o, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            Shelf s = Cur;
            if (ctrl && e.Key == Key.V) { e.Handled = true; PasteHere(); return; }
            if (s == null) return;
            if (ctrl && e.Key == Key.A) { e.Handled = true; _sel.Clear(); foreach (ShelfItem i in s.Items) _sel.Add(i.Id); Repaint(); }
            else if (e.Key == Key.Delete && _sel.Count > 0) { e.Handled = true; ShelfStore.Remove(s, Selected(s)); }
            else if (e.Key == Key.Enter && _sel.Count > 0) { e.Handled = true; foreach (ShelfItem i in Selected(s)) Open(i); }
        }

        // =================================================================
        // Menus and actions
        // =================================================================
        static void Item(ItemsControl m, string text, Action a, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }

        /// <summary>A question or a dialog from a popup: it stays open meanwhile (it closes when it loses focus).</summary>
        T Asking<T>(Func<T> ask)
        {
            if (Hold != null) Hold(true);
            try { return ask(); }
            finally { if (Hold != null) Hold(false); }
        }

        void Pop(ContextMenu m)
        {
            m.PlacementTarget = this;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        void ItemMenu(Shelf s, List<ShelfItem> items)
        {
            if (items.Count == 0) return;
            var m = new ContextMenu();
            bool one = items.Count == 1;
            List<ShelfItem> there = items.FindAll(delegate (ShelfItem i) { return ShelfStore.Exists(i.Path); });
            Item(m, one ? "Open" : "Open all " + items.Count, delegate { foreach (ShelfItem i in there) Open(i); }, there.Count > 0);
            Item(m, "Show in folder", delegate { ShowInFolder(there[0]); }, there.Count > 0);
            m.Items.Add(new Separator());
            Item(m, one ? "Copy" : "Copy " + items.Count + " files", delegate
            {
                var sc = new StringCollection();
                foreach (ShelfItem i in there) sc.Add(i.Path);
                try { Clipboard.SetFileDropList(sc); Note("Copied - paste it in Explorer"); } catch (Exception ex) { Log.Write("Shelf: copy - " + ex.Message); }
            }, there.Count > 0);
            Item(m, one ? "Copy path" : "Copy paths", delegate
            {
                var l = new List<string>();
                foreach (ShelfItem i in items) l.Add(i.Path);
                try { Clipboard.SetText(string.Join(Environment.NewLine, l)); Note(one ? "Path copied" : "Paths copied"); } catch { }
            });
            if (ShelfStore.Shelves.Count > 1)
            {
                var to = new MenuItem { Header = "Move to" };
                foreach (Shelf t0 in ShelfStore.Shelves)
                {
                    if (t0 == s) continue;
                    Shelf t = t0;
                    Item(to, t.Name, delegate { ShelfStore.MoveTo(s, t, items); });
                }
                m.Items.Add(to);
            }
            m.Items.Add(new Separator());
            if (one && !there.Contains(items[0]) && !items[0].IsCard) Item(m, "Find it…", delegate { Asking(delegate { Locate(items[0]); return true; }); });
            Item(m, one ? "Take off the shelf" : "Take " + items.Count + " off the shelf", delegate { ShelfStore.Remove(s, items); });
            Pop(m);
        }

        void ListMenu()
        {
            Shelf s = Cur;
            if (s == null) return;
            var m = new ContextMenu();
            Item(m, "Paste here", PasteHere);
            Item(m, "Select all", delegate { _sel.Clear(); foreach (ShelfItem i in s.Items) _sel.Add(i.Id); Repaint(); }, s.Items.Count > 0);
            List<ShelfItem> gone = s.Items.FindAll(delegate (ShelfItem i) { return _missing.Contains(i.Id); });
            Item(m, "Take off the ones that aren't there" + (gone.Count > 0 ? " (" + gone.Count + ")" : ""), delegate { ShelfStore.Remove(s, gone); }, gone.Count > 0);
            m.Items.Add(new Separator());
            TabItems(m, s);
            Pop(m);
        }

        void TabMenu(Shelf s)
        {
            var m = new ContextMenu();
            TabItems(m, s);
            Pop(m);
        }

        void TabItems(ContextMenu m, Shelf s)
        {
            Item(m, "Rename…", delegate
            {
                string n = Asking(delegate { return NameDialog.Ask(this, _t, "Rename the shelf", s.Name); });
                if (n != null) ShelfStore.Rename(s, n);
            });
            var col = new MenuItem { Header = "Colour" };
            for (int k = 0; k < ShelfStore.Colors.Length; k++)
            {
                string c = ShelfStore.Colors[k];
                var mi = new MenuItem { Header = ShelfStore.ColorNames[k], Icon = ModulePics.Dot(new SolidColorBrush(Theme.Parse(c, Colors.Gray)), 10), IsChecked = string.Equals(s.Color, c, StringComparison.OrdinalIgnoreCase) };
                mi.Click += delegate { ShelfStore.SetColor(s, c); };
                col.Items.Add(mi);
            }
            m.Items.Add(col);
            var pocket = new MenuItem { Header = "A Pocket (what you drag out leaves it)", IsCheckable = true, IsChecked = s.Pocket };
            pocket.Click += delegate { ShelfStore.SetPocket(s, pocket.IsChecked); };
            m.Items.Add(pocket);
            int at = ShelfStore.Shelves.IndexOf(s);
            if (_only == null)
            {
                Item(m, "Move left", delegate { ShelfStore.Shift(s, -1); }, at > 0);
                Item(m, "Move right", delegate { ShelfStore.Shift(s, 1); }, at < ShelfStore.Shelves.Count - 1);
            }
            m.Items.Add(new Separator());
            Item(m, "Clear" + (s.Items.Count > 0 ? " (" + s.Items.Count + ")" : ""), delegate
            {
                if (Asking(delegate { return Questions.YesNo(Window.GetWindow(this), "Take everything off " + s.Name + "?\n\nFiles and folders stay where they are - only the text and picture cards are deleted.", "Shelf"); }) == MessageBoxResult.Yes)
                    ShelfStore.Clear(s);
            }, s.Items.Count > 0);
            Item(m, "Delete this shelf", delegate
            {
                if (s.Items.Count == 0 || Asking(delegate { return Questions.YesNo(Window.GetWindow(this), "Delete the shelf " + s.Name + " and what's on it?\n\nFiles and folders stay where they are.", "Shelf"); }) == MessageBoxResult.Yes)
                    ShelfStore.Delete(s);
            }, ShelfStore.Shelves.Count > 1);
            if (_only == null)
            {
                m.Items.Add(new Separator());
                Item(m, "New shelf…", NewShelf);
            }
        }

        void NewShelf()
        {
            string n = Asking(delegate { return NameDialog.Ask(this, _t, "A new shelf", ""); });
            if (n == null) return;
            Shelf s = ShelfStore.NewShelf(n);
            if (_only == null) { _tab = s.Id; Draw(); }
        }

        void Locate(ShelfItem item)
        {
            string start = item.Folder;
            while (start.Length > 0 && !Directory.Exists(start)) start = System.IO.Path.GetDirectoryName(start) ?? "";
            string found = null;
            if (item.Kind == "folder")
            {
                var d = new Microsoft.Win32.OpenFolderDialog { Title = "Where is " + item.Name + " now?", InitialDirectory = start };
                if (d.ShowDialog() == true) found = d.FolderName;
            }
            else
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Where is " + item.Name + " now?", InitialDirectory = start, FileName = item.Name };
                if (d.ShowDialog() == true) found = d.FileName;
            }
            if (found == null) return;
            ShellThumbs.Forget(item.Path);
            _missing.Remove(item.Id);
            ShelfStore.Relocate(item, found);
        }

        static void Open(ShelfItem i)
        {
            try { Process.Start(new ProcessStartInfo(i.Path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Shelf: open " + i.Path + " - " + ex.Message); }
        }

        static void ShowInFolder(ShelfItem i)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + i.Path + "\"") { UseShellExecute = false }); }
            catch (Exception ex) { Log.Write("Shelf: show " + i.Path + " - " + ex.Message); }
        }

        // =================================================================
        // The Clipboard tab
        // =================================================================
        void DrawClipboard()
        {
            ClipSnapshot c = ClipHistory.Latest;
            if (c == null) { Hint("Reading Windows' clipboard history…"); ClipHistory.Refresh(); }
            else if (c.Status == "off")
            {
                Hint("Windows' clipboard history is turned off. Turn it on (Settings > System > Clipboard > Clipboard history) and what you copy shows here - the same list as Win+V.");
                Border on = FootButton("", "Turn it on", "Open Windows' clipboard settings");
                on.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenClipSettings(); };
                _foot.Children.Add(on);
            }
            else if (c.Status == "denied") Hint("Windows didn't let vShell read the clipboard history.");
            else if (c.Status == "error") Hint("Windows' clipboard history can't be read here (vshell.log may say why).");
            else if (c.Items.Count == 0) Hint("Nothing copied yet - what you copy shows here (the same list as Win+V).");
            else
            {
                foreach (ClipEntry e in c.Items) _list.Children.Add(ClipRow(e));
                Border clear = FootButton("", "Clear", "Clear Windows' clipboard history (what's pinned in Win+V stays)");
                clear.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs ev)
                {
                    ev.Handled = true;
                    if (Asking(delegate { return Questions.YesNo(Window.GetWindow(this), "Clear Windows' clipboard history?\n\nWhat you've pinned in Win+V stays.", "Clipboard"); }) == MessageBoxResult.Yes) ClipHistory.Clear();
                };
                _foot.Children.Add(clear);
            }
            if (c != null && c.Status == "ok")
            {
                Border set = FootButton("", null, "Windows' clipboard settings");
                set.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; OpenClipSettings(); };
                _foot.Children.Add(set);
            }
            int n = c != null ? c.Items.Count : 0;
            _status.Text = _note ?? (c == null || c.Status != "ok" ? "Clipboard" : n + " in the history");
            _foot.Children.Add(_status);
        }

        static void OpenClipSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:clipboard") { UseShellExecute = true }); } catch { }
        }

        Border ClipRow(ClipEntry e)
        {
            double h = RowH, pic = h - 8;
            var row = new Border { Height = h, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(Math.Max(4, _t.Radius * 0.6)), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(e.Kind == "image" ? pic * 1.6 + 10 : pic + 10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (e.Kind == "image")
            {
                var img = new Image { Source = ClipHistory.ThumbOf(e, (int)Math.Ceiling(pic * 3.2)), Stretch = Stretch.Uniform, Height = pic, MaxWidth = pic * 1.6, HorizontalAlignment = HorizontalAlignment.Left };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                g.Children.Add(img);
            }
            else
            {
                var gl = Glyph(e.Kind == "text" ? "" : "", _t.FgDim, pic * 0.55);
                gl.HorizontalAlignment = HorizontalAlignment.Left;
                gl.Margin = new Thickness(pic * 0.2, 0, 0, 0);
                g.Children.Add(gl);
            }
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string text = e.Kind == "text" ? OneLine(e.Text) : e.Kind == "image" ? "Picture" : (e.Formats.Length > 0 ? e.Formats : "Something copied");
            texts.Children.Add(Txt(text.Length > 0 ? text : "(empty text)", _t.Fg, _fs));
            texts.Children.Add(Txt("Copied " + TimeText.Ago(e.When), _t.FgDim, _fs * 0.8));
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            row.Child = g;
            row.ToolTip = (e.Kind == "text" ? Cut(e.Text, 600) + "\n\n" : "") + "Click to copy it again · drag it into an app · right-click for more";

            row.MouseEnter += delegate { row.Background = _t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; _press = ev.GetPosition(this); _pressed = e; };
            row.MouseMove += delegate (object o, MouseEventArgs ev)
            {
                if (_pressed != e || ev.LeftButton != MouseButtonState.Pressed || !Moved(ev)) return;
                _pressed = null;
                DragClip(e);
            };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs ev)
            {
                ev.Handled = true;
                if (_pressed != e) return;
                _pressed = null;
                CopyBack(e);
            };
            row.MouseRightButtonUp += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; ClipMenu(e); };
            return row;
        }

        static string OneLine(string s)
        {
            string t = (s ?? "").Trim();
            int nl = t.IndexOfAny(new[] { '\r', '\n' });
            string first = nl >= 0 ? t.Substring(0, nl).Trim() + "  …" : t;
            return Cut(first, 200);
        }

        static string Cut(string s, int n) { return s.Length > n ? s.Substring(0, n) + "…" : s; }

        void CopyBack(ClipEntry e)
        {
            ClipHistory.CopyBack(e.Id, delegate (bool ok)
            {
                Note(ok ? "Copied - paste it with Ctrl+V" : "Windows didn't take it");
                if (ok && Used != null && ShelfSettings.Current != null && ShelfSettings.Current.CloseAfterCopy) Used();
            });
        }

        void ClipMenu(ClipEntry e)
        {
            var m = new ContextMenu();
            Item(m, "Copy", delegate { CopyBack(e); });
            var keep = new MenuItem { Header = "Keep on a shelf" };
            foreach (Shelf s0 in ShelfStore.Shelves)
            {
                Shelf s = s0;
                Item(keep, s.Name, delegate { if (Keep(s, e)) Note("Kept on " + s.Name); });
            }
            m.Items.Add(keep);
            m.Items.Add(new Separator());
            Item(m, "Delete from the history", delegate { ClipHistory.Delete(e.Id); });
            Pop(m);
        }

        static bool Keep(Shelf s, ClipEntry e)
        {
            if (e.Kind == "image") return ShelfStore.AddImage(s, ClipHistory.Full(e)) != null;
            if (e.Kind == "text") return ShelfStore.AddText(s, e.Text) != null;
            return false;
        }

        /// <summary>Drag a clipboard item into an app: text as text; a picture as a picture and as a .png file.</summary>
        void DragClip(ClipEntry e)
        {
            var d = new DataObject();
            if (e.Kind == "text") d.SetText(e.Text ?? "");
            else if (e.Kind == "image")
            {
                BitmapSource full = ClipHistory.Full(e);
                if (full == null) return;
                d.SetImage(full);
                try
                {
                    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vShell Shelf");
                    Directory.CreateDirectory(dir);
                    string file = System.IO.Path.Combine(dir, "Picture " + e.When.ToLocalTime().ToString("yyyy-MM-dd HH.mm.ss") + ".png");
                    if (!File.Exists(file))
                    {
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(full));
                        using (var fs = File.Create(file)) enc.Save(fs);
                    }
                    d.SetFileDropList(new StringCollection { file });
                }
                catch (Exception ex) { Log.Write("Shelf clipboard: a picture to drag - " + ex.Message); }
            }
            else return;
            if (Hold != null) Hold(true);
            try { DragDrop.DoDragDrop(this, d, DragDropEffects.Copy); }
            catch (COMException ex) { Log.Write("Shelf: drag - " + ex.Message); }
            finally { if (Hold != null) Hold(false); }
        }
    }

    /// <summary>A small box asking for a name (a new shelf, renaming one). Owned as Questions does: never hidden behind a widget.</summary>
    static class NameDialog
    {
        public static string Ask(FrameworkElement from, Theme t, string title, string initial)
        {
            var w = new Window
            {
                Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false, Background = t.PopupBg, Foreground = t.Fg, FontFamily = t.Font, FontSize = Math.Max(12, t.FontSize)
            };
            var sp = new StackPanel { Margin = new Thickness(16), Width = 300 };
            sp.Children.Add(new TextBlock { Text = "Name", Foreground = t.FgDim, Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Text = initial ?? "", Padding = new Thickness(4, 3, 4, 3) };
            sp.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            w.Content = sp;
            string result = null;
            ok.Click += delegate
            {
                string v = box.Text.Trim();
                if (v.Length == 0) { box.Focus(); return; }
                result = v.Length > 40 ? v.Substring(0, 40) : v;
                w.DialogResult = true;
            };
            w.Loaded += delegate { box.Focus(); box.SelectAll(); };
            Questions.Own(w, Window.GetWindow(from));
            if (w.Owner != null) w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            try { w.ShowDialog(); } catch (Exception ex) { Log.Write("Shelf: name box - " + ex.Message); }
            return result;
        }
    }
}
