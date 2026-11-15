using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A Notes store that comes from a plugin (vDrop, the Home Assistant add-on): a Notes module with it shows the plugin's feed instead of
    /// a note. Registered with PluginHost.AddNoteStore.
    /// </summary>
    [PluginApi]
    public sealed class NoteFeedStore
    {
        /// <summary>The ModuleConfig.NoteStore value ("vDrop").</summary>
        public string Id = "";
        /// <summary>Make the feed: (the module's settings, save soon, start typing in this box, stop typing).</summary>
        public Func<ModuleConfig, Action, Action<TextBox>, Action, INoteFeed> Create;
        /// <summary>A module showing it appeared (true) or went (false) - the plugin syncs only while one shows it.</summary>
        public Action<bool> Use;
        /// <summary>Before the first draw: read what's kept on this PC.</summary>
        public Action Prepare;
        /// <summary>The module settings rows for this store (after the store choice).</summary>
        public Action<SettingsWindow.SettingsPage, ModuleConfig> Settings;
    }

    /// <summary>What a plugin's Notes store shows inside the Notes module.</summary>
    [PluginApi]
    public interface INoteFeed
    {
        FrameworkElement View { get; }
        void SetLook(Theme t, NoteLook look, double height);
        /// <summary>Start typing in its add box (the quick-note key, a click on the module).</summary>
        void FocusCompose();
        void StopTyping();
        void AddMenuItems(ContextMenu m, DependencyObject source);
    }

    /// <summary>The plugins' Notes stores.</summary>
    public static class NoteFeeds
    {
        static readonly List<NoteFeedStore> Stores = new List<NoteFeedStore>();

        /// <summary>vShell's own stores (a note kept with the settings, a file, a folder).</summary>
        public static bool IsBuiltIn(string store) { return store == "vShell" || store == "File" || store == "Folder"; }

        public static NoteFeedStore Find(string id) { lock (Stores) return Stores.Find(delegate (NoteFeedStore s) { return s.Id == id; }); }
        public static List<NoteFeedStore> All() { lock (Stores) return new List<NoteFeedStore>(Stores); }
        internal static void Add(NoteFeedStore s) { lock (Stores) { Stores.RemoveAll(delegate (NoteFeedStore x) { return x.Id == s.Id; }); Stores.Add(s); } }
        internal static void Remove(NoteFeedStore s) { lock (Stores) Stores.Remove(s); }
    }
    /// <summary>Modules that add their own items to the widget's right-click menu (source = what was clicked).</summary>
    public interface IWidgetMenu
    {
        void AddMenuItems(ContextMenu menu, DependencyObject source);
    }

    /// <summary>
    /// A note on the desktop (widgets only): Note (text with light formatting), Checklist, Markdown (a rendered page) or Log (quick
    /// time-stamped entries). The text is Markdown, kept in vShell's settings (several notes, one shown at a time) or in a .txt / .md file
    /// that is read and written both ways - a change from another editor shows straight away, and a change made elsewhere while you were
    /// typing is never overwritten without asking.
    /// Typing needs the keyboard: while you edit, the widget comes in front and takes it, and goes back to the desktop afterwards.
    /// </summary>
    public sealed class NotesModule : Border, IMultiRow, IWidgetMenu, ITypingModule
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;

        readonly Border _paper = new Border();
        readonly DockPanel _dock = new DockPanel { LastChildFill = true };
        readonly Grid _top = new Grid();
        readonly TextBlock _title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel _footer = new StackPanel();
        readonly Border _bodyHost = new Border { Background = Brushes.Transparent };
        // the scrollbar's room is always kept (an empty strip when there's nothing to scroll): with Auto the note got wider - and the
        // widget with it - the moment the text grew long enough to need one
        readonly ScrollViewer _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
        };
        NoteLook _look = new NoteLook();
        Theme _t;

        // the note
        string _text = "";
        bool _dirty, _editing;
        TextBox _editor, _input, _itemBox;
        int _itemLine = -1;
        readonly DispatcherTimer _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        // click-to-edit / drag
        bool _downPending;
        Point _downAt;
        // checklist drag to reorder
        int _dragLine = -1;
        Border _dropLine;
        StackPanel _itemsPanel;
        // the file
        FileSystemWatcher _watch;
        readonly DispatcherTimer _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        string _baseline;              // the file's text as last read / written by us
        string _conflict;              // the file's text when it changed elsewhere while we had changes of our own
        bool _bom, _crlf = true, _fileExists;
        DateTime _fileTime;
        string _error;

        public NotesModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            Background = Brushes.Transparent;
            _paper.Child = _dock;
            Child = _paper;
            DockPanel.SetDock(_top, Dock.Top);
            DockPanel.SetDock(_footer, Dock.Bottom);
            _dock.Children.Add(_top);
            _dock.Children.Add(_footer);
            _dock.Children.Add(_bodyHost);
            try { _scroll.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(WidgetWindow.ScrollXaml)); }
            catch (Exception ex) { Log.Write("Notes scroll style: " + ex.Message); }

            _saveTimer.Tick += delegate { _saveTimer.Stop(); SaveNow(); };
            _reloadTimer.Tick += delegate { _reloadTimer.Stop(); ReloadFromFile(false); };
            _bodyHost.MouseLeftButtonDown += Body_MouseDown;
            // a note from a folder: click its name for the other notes there
            _title.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { if (FolderOn) { e.Handled = true; FolderMenu(_title); } };
            _bodyHost.MouseMove += Body_MouseMove;
            _bodyHost.MouseLeftButtonUp += Body_MouseUp;
            _bodyHost.LostMouseCapture += delegate { _downPending = false; };
            _top.MouseWheel += delegate (object s, MouseWheelEventArgs e) { e.Handled = true; Flip(e.Delta > 0 ? -1 : 1); };     // flip through the notes

            bool using_ = false;
            Loaded += delegate
            {
                StartWatch();
                NoteFeedStore fs = FeedStore;
                if (fs != null && fs.Use != null && !using_) { using_ = true; fs.Use(true); }
            };
            Unloaded += delegate
            {
                SaveNow(); StopWatch();
                NoteFeedStore fs = FeedStore;
                if (using_) { using_ = false; if (fs != null && fs.Use != null) fs.Use(false); }
            };
            if (FeedStore != null && FeedStore.Prepare != null) FeedStore.Prepare();      // what's kept on this PC, so the first draw has it

            ApplyLook();
            Load();
            Build();
        }

        public ModuleConfig Config { get { return _cfg; } }

        /// <summary>A widget's tabs: as wide as the widest tab (the text wraps at that width).</summary>
        public void FitWidth(double w)
        {
            if (w > 40 && !(Widget != null && Widget.Cfg.Width > 0)) Width = w;
        }
        /// <summary>As tall as its text (or NoteHeight with scrolling): never a fixed number of rows.</summary>
        public int RowCount { get { return 0; } }
        bool File_ { get { return _cfg.NoteStore == "File" || _cfg.NoteStore == "Folder"; } }
        /// <summary>The store is a plugin's feed (vDrop, the Home Assistant add-on): the module shows it instead of a note - also while
        /// its plugin isn't added (it then says so).</summary>
        bool VDrop_ { get { return !NoteFeeds.IsBuiltIn(_cfg.NoteStore); } }
        NoteFeedStore FeedStore { get { return NoteFeeds.Find(_cfg.NoteStore); } }
        List<NotePage> Pages { get { return _cfg.NotePages; } }
        /// <summary>The plugin's feed (null until built, or while its plugin isn't added).</summary>
        INoteFeed _feed;
        bool Locked { get { return _cfg.NoteLocked; } }
        string Kind { get { return _cfg.NoteKind; } }
        bool TextKind { get { return Kind == "Note" || Kind == "Markdown"; } }
        WidgetWindow Widget { get { return _host as WidgetWindow; } }

        // =================================================================
        // Look
        // =================================================================
        static readonly Dictionary<string, Color> Papers = new Dictionary<string, Color>
        {
            { "Yellow", Color.FromRgb(0xF9, 0xE2, 0xAF) }, { "Mint", Color.FromRgb(0xA6, 0xE3, 0xA1) },
            { "Pink", Color.FromRgb(0xF5, 0xC2, 0xE7) }, { "Blue", Color.FromRgb(0x89, 0xDC, 0xEB) }
        };

        void ApplyLook()
        {
            _t = _host.Theme.ForModule(_cfg);
            Color paper;
            bool own = false;
            if (Papers.TryGetValue(_cfg.NotePaper ?? "", out paper)) own = true;
            else if (_cfg.NotePaper != "Theme" && Theme.TryParse(_cfg.NotePaper, out paper)) own = true;

            var look = new NoteLook { Size = _t.FontSize };
            if (own)
            {
                var bg = new SolidColorBrush(paper); bg.Freeze();
                Brush ink = NoteRender.Contrast(bg);
                Color ic = ((SolidColorBrush)ink).Color;
                _paper.Background = bg;
                _paper.CornerRadius = new CornerRadius(Math.Max(6, Math.Min(14, _t.Radius * 0.8)));
                _paper.Padding = new Thickness(12, 10, 12, 10);
                look.Fg = ink;
                look.Dim = new SolidColorBrush(Color.FromArgb(0xA8, ic.R, ic.G, ic.B));
                look.Accent = ink;
                look.Line = new SolidColorBrush(Color.FromArgb(0x40, ic.R, ic.G, ic.B));
                look.CodeBg = new SolidColorBrush(Color.FromArgb(0x22, ic.R, ic.G, ic.B));
            }
            else
            {
                _paper.Background = null;
                _paper.Padding = new Thickness(2, 0, 2, 0);
                look.Fg = _t.Fg;
                look.Dim = _t.FgDim;
                look.Accent = _t.MainAccent;
                look.Line = Theme.Faded(_t.Fg, 18);
                look.CodeBg = Theme.Faded(_t.Fg, 12);
            }
            switch (_cfg.NoteFont)
            {
                case "Mono": look.Font = new FontFamily("Cascadia Mono, Consolas"); break;
                case "Handwritten": look.Font = new FontFamily("Segoe Print, Ink Free, Segoe UI"); break;
                case "Theme": look.Font = _t.Font; break;
                default:
                    // an installed font picked by name (falls back to the theme font when it isn't there)
                    try { look.Font = new FontFamily(_cfg.NoteFont + ", " + _t.Font.Source); } catch { look.Font = _t.Font; }
                    break;
            }
            _look = look;
            TextElement_Set(_paper, look);
            _scroll.Resources["wThumb"] = look.Line;

            // a fixed widget width: fill it; otherwise the note's own width (text wraps inside it)
            var w = Widget;
            if (w != null && w.Cfg.Width > 0) ClearValue(WidthProperty); else Width = _cfg.NoteWidth;
        }

        static void TextElement_Set(FrameworkElement e, NoteLook look)
        {
            System.Windows.Documents.TextElement.SetForeground(e, look.Fg);
            System.Windows.Documents.TextElement.SetFontFamily(e, look.Font);
            System.Windows.Documents.TextElement.SetFontSize(e, look.Size);
        }

        // =================================================================
        // Load / save
        // =================================================================
        NotePage Page
        {
            get
            {
                if (Pages.Count == 0) Pages.Add(new NotePage());
                _cfg.NoteIndex = Math.Max(0, Math.Min(Pages.Count - 1, _cfg.NoteIndex));
                return Pages[_cfg.NoteIndex];
            }
        }

        void Load()
        {
            if (VDrop_) return;
            _error = null;
            _conflict = null;
            if (!File_) { _text = Page.Text ?? ""; return; }
            try
            {
                NoteFile.Content c = NoteFile.Read(_cfg.NoteActiveFile);
                _fileExists = c.Exists;
                _text = c.Text;
                _baseline = c.Exists ? c.Text : "";
                _bom = c.Bom; _crlf = c.Crlf; _fileTime = c.Written;
            }
            catch (Exception ex)
            {
                _text = ""; _baseline = null; _fileExists = false;
                _error = "Can't read the file: " + ex.Message;
            }
        }

        /// <summary>The text changed (typing): save a moment from now.</summary>
        void Touched()
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>A change that is done in one go (a tick, a new item): save now and redraw.</summary>
        void Commit(NoteText nt)
        {
            _text = nt.ToString();
            _dirty = true;
            SaveNow();
            Build();
        }

        void SaveNow()
        {
            _saveTimer.Stop();
            if (VDrop_) return;
            if (_editing && _editor != null) _text = Norm(_editor.Text);
            if (!_dirty) return;
            if (!File_)
            {
                _dirty = false;
                Page.Text = _text;
                Page.Edited = DateTime.Now;
                _mgr.SaveSoon();
                UpdateFooter();
                return;
            }
            WriteFile();
        }

        static string Norm(string s) { return (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); }

        void WriteFile()
        {
            string path = _cfg.NoteActiveFile;
            if (string.IsNullOrWhiteSpace(path)) { _error = "Choose the file in the module's settings."; UpdateFooter(); return; }
            if (_conflict != null) { UpdateFooter(); return; }       // waiting for "keep mine / take theirs"
            try
            {
                NoteFile.Content cur = NoteFile.Read(path);
                if (cur.Exists && _baseline != null && cur.Text != _baseline && cur.Text != _text)
                {
                    _conflict = cur.Text;     // someone else changed it since we read it: ask
                    UpdateFooter();
                    return;
                }
                NoteFile.Write(path, _text, _bom, _crlf);
                _baseline = _text;
                _dirty = false;
                _fileExists = true;
                _fileTime = DateTime.Now;
                _error = null;
            }
            catch (Exception ex) { _error = "Can't save: " + ex.Message; }
            UpdateFooter();
        }

        // ---- the file changing on disk ----------------------------------------------------------------
        void StartWatch()
        {
            StopWatch();
            if (!File_ || string.IsNullOrWhiteSpace(_cfg.NoteActiveFile)) return;
            try
            {
                string full = Path.GetFullPath(_cfg.NoteActiveFile);
                string dir = Path.GetDirectoryName(full);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                _watch = new FileSystemWatcher(dir, Path.GetFileName(full))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                FileSystemEventHandler h = delegate { Dispatcher.BeginInvoke(new Action(delegate { _reloadTimer.Stop(); _reloadTimer.Start(); })); };
                _watch.Changed += h;
                _watch.Created += h;
                _watch.Deleted += h;
                _watch.Renamed += delegate { Dispatcher.BeginInvoke(new Action(delegate { _reloadTimer.Stop(); _reloadTimer.Start(); })); };
                _watch.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("Notes: can't watch " + _cfg.NoteActiveFile + ": " + ex.Message); }
            ReloadFromFile(false);     // it may have changed while this note was not on screen
        }

        void StopWatch()
        {
            _reloadTimer.Stop();
            if (_watch == null) return;
            try { _watch.EnableRaisingEvents = false; _watch.Dispose(); } catch { }
            _watch = null;
        }

        /// <summary>Read the file again. Our own write, or the same text: nothing. Changed elsewhere: show it - unless we have changes of
        /// our own, then ask (keep mine / take theirs / keep both).</summary>
        void ReloadFromFile(bool force)
        {
            if (!File_) return;
            NoteFile.Content c;
            try { c = NoteFile.Read(_cfg.NoteActiveFile); }
            catch (Exception ex) { _error = "Can't read the file: " + ex.Message; UpdateFooter(); return; }
            if (!c.Exists) { _fileExists = false; UpdateFooter(); return; }
            _fileExists = true;
            _fileTime = c.Written;
            if (!force && (c.Text == _baseline || c.Text == _text)) { _baseline = c.Text; UpdateFooter(); return; }
            bool busy = _dirty || _itemLine >= 0 || (_editing && _editor != null && Norm(_editor.Text) != _text);
            if (busy && !force) { _conflict = c.Text; UpdateFooter(); return; }
            _text = _baseline = c.Text;
            _bom = c.Bom; _crlf = c.Crlf;
            _conflict = null;
            _dirty = false;
            if (_editing && _editor != null)
            {
                int caret = _editor.CaretIndex;
                _editor.Text = _text;
                _editor.CaretIndex = Math.Min(caret, _editor.Text.Length);
                UpdateFooter();
            }
            else Build();
        }

        void ResolveConflict(string how)
        {
            string theirs = _conflict;
            if (theirs == null) return;
            if (how == "theirs")
            {
                _conflict = null;
                _text = _baseline = theirs;
                _dirty = false;
                if (_editing && _editor != null) _editor.Text = _text;
                Build();
                return;
            }
            if (how == "both")
            {
                try { NoteFile.Write(NoteFile.ConflictPath(_cfg.NoteActiveFile, DateTime.Now), theirs, _bom, _crlf); }
                catch (Exception ex) { _error = "Can't save their copy: " + ex.Message; UpdateFooter(); return; }
            }
            _conflict = null;
            _baseline = theirs;          // what is there now: mine replaces it
            _dirty = true;
            SaveNow();
            Build();
        }

        // =================================================================
        // Building
        // =================================================================
        void Build()
        {
            if (VDrop_) { BuildFeed(); return; }
            BuildTop();
            UpdateFooter();
            _itemsPanel = null;
            _dropLine = null;
            FrameworkElement body;
            switch (Kind)
            {
                case "Checklist": body = BuildChecklist(); break;
                case "Log": body = BuildLog(); break;
                default: body = _editing ? BuildEditor() : BuildView(); break;
            }
            _scroll.Content = null;
            if (_cfg.NoteGrow) { _bodyHost.Child = body; }
            else
            {
                _scroll.Height = _cfg.NoteHeight;
                _scroll.Content = body;
                _bodyHost.Child = _scroll;
            }
            _bodyHost.Cursor = TextKind && !_editing && !Locked && _cfg.NoteEditOn != "Menu" ? Cursors.IBeam : null;
        }

        /// <summary>Store vDrop: the module is vDrop's feed (tabs, list, search, items, the add box).</summary>
        void BuildFeed()
        {
            _top.Visibility = Visibility.Collapsed;
            _footer.Children.Clear();
            NoteFeedStore fs = FeedStore;
            if (fs == null)
            {
                // its plugin isn't added: say so (the settings are kept, adding it brings the feed back)
                _scroll.Content = null;
                _bodyHost.Child = new TextBlock
                {
                    Text = _cfg.NoteStore + " comes with a plugin that isn't added - add it in Settings > Plugins.", Foreground = _t.FgDim,
                    FontFamily = _t.Font, FontSize = _t.FontSize, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4)
                };
                _bodyHost.Cursor = null;
                return;
            }
            if (_feed == null)
                _feed = fs.Create(_cfg, _mgr.SaveSoon, delegate (TextBox box) { Keyboard_(box, false); },
                                  delegate { var w = Widget; if (w != null) w.EndTyping(this); });
            // a widget with a set height: the feed fills it (worked out once it's laid out); otherwise the module's own height
            bool fill = Widget != null && Widget.Cfg.Height > 0;
            _feed.SetLook(_t, _look, fill ? (_fillH > 0 ? _fillH : 200) : Math.Max(220, _cfg.NoteHeight));
            _scroll.Content = null;
            _bodyHost.Child = _feed.View;
            _bodyHost.Cursor = null;
            if (fill) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FillHeight));
        }

        double _fillH;
        ScrollViewer _fillHost;

        /// <summary>vDrop in a widget with a set height: make the feed exactly as tall as the room left in the widget below it.</summary>
        void FillHeight()
        {
            if (!VDrop_ || _feed == null || Widget == null || Widget.Cfg.Height <= 0) return;
            ScrollViewer sv = null;
            for (DependencyObject d = VisualTreeHelper.GetParent(this); d != null; d = VisualTreeHelper.GetParent(d))
            {
                sv = d as ScrollViewer;
                if (sv != null) break;
            }
            if (sv == null || sv.ViewportHeight < 40 || !(sv.Content is UIElement)) return;
            if (_fillHost != sv)
            {
                _fillHost = sv;
                sv.SizeChanged += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FillHeight)); };
            }
            double top;
            try { top = _feed.View.TranslatePoint(new Point(0, 0), (UIElement)sv.Content).Y; }
            catch (InvalidOperationException) { return; }
            // what's below the feed inside the module (padding, footer) stays visible too
            double below = _paper.Padding.Bottom + _footer.ActualHeight + _footer.Margin.Top + Margin.Bottom + 6;
            double h = Math.Max(160, Math.Floor(sv.ViewportHeight - top - below));
            if (Math.Abs(h - _feed.View.Height) < 1) return;
            _fillH = h;
            _feed.View.Height = h;
        }

        /// <summary>The .md / .txt files in a folder (not its sub-folders), newest first.</summary>
        public static List<string> FolderNotes(string folder)
        {
            var list = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
                foreach (string f in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".md" || ext == ".txt" || ext == ".markdown") list.Add(f);
                }
                list.Sort(delegate (string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            }
            catch (Exception ex) { Log.Write("Notes folder " + folder + ": " + ex.Message); }
            return list;
        }

        public static bool InFolder(string file, string folder)
        {
            try { return string.Equals(Path.GetFullPath(Path.GetDirectoryName(file)).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        bool FolderOn { get { return _cfg.NoteStore == "Folder"; } }

        /// <summary>The folder's notes as a menu (read now - new files show straight away); picking one shows it here.</summary>
        void FolderMenu(FrameworkElement target)
        {
            List<string> files = FolderNotes(_cfg.NoteFolder);
            var m = new ContextMenu();
            if (string.IsNullOrWhiteSpace(_cfg.NoteFolder)) m.Items.Add(new MenuItem { Header = "Choose the folder in the module's settings", IsEnabled = false });
            else if (!Directory.Exists(_cfg.NoteFolder)) m.Items.Add(new MenuItem { Header = "The folder isn't there: " + _cfg.NoteFolder, IsEnabled = false });
            else if (files.Count == 0) m.Items.Add(new MenuItem { Header = "No .md or .txt files in " + Path.GetFileName(_cfg.NoteFolder.TrimEnd('\\')), IsEnabled = false });
            int n = 0;
            foreach (string f in files)
            {
                if (n++ >= 40) { m.Items.Add(new MenuItem { Header = "+ " + (files.Count - 40) + " more (open the folder)", IsEnabled = false }); break; }
                string me = f;
                bool cur = string.Equals(f, _cfg.NoteActiveFile, StringComparison.OrdinalIgnoreCase);
                var mi = new MenuItem { Header = Path.GetFileNameWithoutExtension(f) + "   ·   " + NoteText.Ago(File.GetLastWriteTime(f), DateTime.Now), IsChecked = cur, ToolTip = f };
                mi.Click += delegate
                {
                    if (string.Equals(me, _cfg.NoteActiveFile, StringComparison.OrdinalIgnoreCase)) return;
                    SaveNow();
                    _cfg.NoteFolderFile = me;
                    _mgr.SettingsChanged();       // the module is built again on the new file
                };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator());
            if (Directory.Exists(_cfg.NoteFolder))
            {
                var add = new MenuItem { Header = "New note…" };
                add.Click += delegate { NewFolderNote(); };
                m.Items.Add(add);
            }
            var open = new MenuItem { Header = "Open the folder" };
            open.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + _cfg.NoteFolder + "\""); } catch { } };
            m.Items.Add(open);
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>Make a new note in the folder (asks its name; .md unless .txt is typed) and show it.</summary>
        void NewFolderNote()
        {
            var f = new List<FormField> { new FormField("Name", "") { Hint = "e.g. Shopping or Ideas.txt - .md is added when there's no .md / .txt" } };
            if (!FormWindow.Ask(Window.GetWindow(this), "New note in " + Path.GetFileName(_cfg.NoteFolder.TrimEnd('\\')), f)) return;
            string name = (f[0].Value ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
            if (name.Length == 0) return;
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext != ".md" && ext != ".txt" && ext != ".markdown") name += ".md";
            string path = Path.Combine(_cfg.NoteFolder, name);
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "");
            }
            catch (Exception ex)
            {
                Questions.Tell(null, "Couldn't make " + path + ": " + ex.Message, "Notes", MessageBoxImage.Warning);
                return;
            }
            SaveNow();
            _cfg.NoteFolderFile = path;
            _mgr.SettingsChanged();       // the module is built again on the new note
        }

        void BuildTop()
        {
            _top.Children.Clear();
            _top.ColumnDefinitions.Clear();
            if (FolderOn)
            {
                // a file from the folder: its name, and the list of the others
                _top.Visibility = Visibility.Visible;
                _top.Margin = new Thickness(0, 0, 0, 6);
                _top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Border pick = null;
                pick = IconButton("\uE70D", "Pick another note from " + _cfg.NoteFolder, delegate { FolderMenu(pick); });
                var icon = new TextBlock { Text = "\uE8B7", FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(9, _look.Size * 0.85), Foreground = _look.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
                _title.Text = string.IsNullOrWhiteSpace(_cfg.NoteFolder) ? "No folder chosen" : string.IsNullOrWhiteSpace(_cfg.NoteActiveFile) ? "Pick a note" : Path.GetFileNameWithoutExtension(_cfg.NoteActiveFile);
                _title.FontWeight = FontWeights.SemiBold;
                _title.TextAlignment = TextAlignment.Left;
                _title.Foreground = _look.Fg;
                _title.Cursor = Cursors.Hand;
                _title.ToolTip = (_cfg.NoteActiveFile ?? "") + "\nClick for the other notes in the folder";
                Grid.SetColumn(_title, 1);
                Grid.SetColumn(pick, 2);
                _top.Children.Add(icon);
                _top.Children.Add(_title);
                _top.Children.Add(pick);
                _top.Background = Brushes.Transparent;
                return;
            }
            _title.Cursor = null; _title.ToolTip = null;
            bool pages = !File_ && Pages.Count > 1;
            _top.Visibility = pages ? Visibility.Visible : Visibility.Collapsed;
            if (!pages) return;
            _top.Margin = new Thickness(0, 0, 0, 6);
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var prev = IconButton("", "Previous note", delegate { Flip(-1); });
            var next = IconButton("", "Next note", delegate { Flip(1); });
            _title.Text = PageTitle(_cfg.NoteIndex) + "   " + (_cfg.NoteIndex + 1) + " / " + Pages.Count;
            _title.FontWeight = FontWeights.SemiBold;
            _title.TextAlignment = TextAlignment.Center;
            _title.Foreground = _look.Fg;
            Grid.SetColumn(_title, 1);
            Grid.SetColumn(next, 2);
            _top.Children.Add(prev);
            _top.Children.Add(_title);
            _top.Children.Add(next);
            _top.Background = Brushes.Transparent;
        }

        string PageTitle(int i)
        {
            NotePage p = Pages[i];
            if (!string.IsNullOrWhiteSpace(p.Title)) return p.Title.Trim();
            foreach (string line in (p.Text ?? "").Replace("\r", "").Split('\n'))
            {
                string t = line.Trim().TrimStart('#', '-', '*', '>', ' ').Replace("[ ] ", "").Replace("[x] ", "").Replace("**", "");
                if (t.Length > 0) return t.Length > 28 ? t.Substring(0, 27) + "…" : t;
            }
            return "Note " + (i + 1);
        }

        Border IconButton(string glyph, string tip, Action act)
        {
            var b = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip,
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(9, _look.Size * 0.85), Foreground = _look.Dim,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += delegate { b.Background = _look.CodeBg; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; act(); };
            return b;
        }

        void UpdateFooter()
        {
            if (VDrop_) { _footer.Children.Clear(); return; }
            _footer.Children.Clear();
            var line = new List<string>();
            if (Kind == "Checklist")
            {
                var nt = new NoteText(_text);
                int n = nt.Items().Count;
                if (n > 0) line.Add(nt.DoneCount() + " of " + n + " done");
            }
            if (_cfg.NoteShowEdited)
            {
                DateTime when = File_ ? _fileTime : Page.Edited;
                if (when > DateTime.MinValue && (!File_ || _fileExists)) line.Add("edited " + NoteText.Ago(when, DateTime.Now));
            }
            if (File_ && !_fileExists && _error == null)
                line.Add(FolderOn && string.IsNullOrWhiteSpace(_cfg.NoteFolder) ? "Choose the folder in the module's settings"
                       : FolderOn && string.IsNullOrWhiteSpace(_cfg.NoteActiveFile) ? "Pick a note from the title line above"
                       : string.IsNullOrWhiteSpace(_cfg.NoteActiveFile) ? "Choose the file in the module's settings" : "New file - it is made when you add something");
            if (line.Count > 0) _footer.Children.Add(Small(string.Join("   ·   ", line), _look.Dim));
            if (_error != null) _footer.Children.Add(Small(_error, _t.SecondaryAccent));
            if (_conflict != null)
            {
                _footer.Children.Add(Small("The file changed somewhere else while you were editing it here.", _t.SecondaryAccent));
                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(TextButton("Keep mine", delegate { ResolveConflict("mine"); }));
                row.Children.Add(TextButton("Take theirs", delegate { ResolveConflict("theirs"); }));
                row.Children.Add(TextButton("Keep both", delegate { ResolveConflict("both"); }));
                row.ToolTip = "Keep both: yours goes in the file, theirs is saved next to it as \"" + Path.GetFileName(NoteFile.ConflictPath(_cfg.NoteActiveFile, DateTime.Now)) + "\"";
                _footer.Children.Add(row);
            }
            _footer.Margin = new Thickness(0, _footer.Children.Count > 0 ? 6 : 0, 0, 0);
        }

        TextBlock Small(string text, Brush fg)
        {
            return new TextBlock { Text = text, Foreground = fg, FontSize = Math.Max(8, _look.Size * 0.82), TextWrapping = TextWrapping.Wrap };
        }

        Border TextButton(string text, Action act)
        {
            var b = new Border
            {
                Background = _look.CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 4),
                Cursor = Cursors.Hand, Child = new TextBlock { Text = text, FontSize = Math.Max(8, _look.Size * 0.85), Foreground = _look.Fg }
            };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; act(); };
            return b;
        }

        // ---- Note / Markdown --------------------------------------------------------------------------
        FrameworkElement BuildView()
        {
            var lines = new NoteText(_text).Lines;
            if (lines.Count == 0 || _text.Trim().Length == 0)
            {
                string hint = Locked ? "Empty" : _cfg.NoteEditOn == "Menu" ? "Right-click > Edit to write" : _cfg.NoteEditOn == "Double-click" ? "Double-click to write" : "Click to write";
                return new TextBlock { Text = hint, Foreground = _look.Dim, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 2, 0, 2) };
            }
            return NoteRender.Render(lines, Kind == "Markdown", _look, Locked ? (Action<int>)null : ToggleLine);
        }

        void ToggleLine(int line)
        {
            var nt = new NoteText(_text);
            NoteText.Item it;
            if (line < 0 || line >= nt.Lines.Count || !NoteText.TryTask(nt.Lines[line], out it)) return;
            nt.SetDone(line, !it.Done);
            Commit(nt);
        }

        FrameworkElement BuildEditor()
        {
            var panel = new StackPanel();
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            bool md = Kind == "Markdown";
            bar.Children.Add(ToolButton("B", "Bold (Ctrl+B)", FontWeights.Bold, FontStyles.Normal, delegate { Wrap("**"); }));
            bar.Children.Add(ToolButton("I", "Italic (Ctrl+I)", FontWeights.Normal, FontStyles.Italic, delegate { Wrap("*"); }));
            if (md) bar.Children.Add(ToolButton("H", "Heading", FontWeights.SemiBold, FontStyles.Normal, delegate { Prefix("# ", true); }));
            bar.Children.Add(ToolGlyph("", "Bulleted list (Ctrl+L)", delegate { Prefix("- ", false); }));
            bar.Children.Add(ToolGlyph("", "Tick box", delegate { Prefix("- [ ] ", false); }));
            if (md)
            {
                bar.Children.Add(ToolGlyph("", "Code", delegate { Wrap("`"); }));
                bar.Children.Add(ToolGlyph("", "Link", delegate { Link(); }));
            }
            bar.Children.Add(TextButton("Done", delegate { StopTyping(); }));
            panel.Children.Add(bar);

            _editor = new TextBox
            {
                Text = _text, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, Foreground = _look.Fg, CaretBrush = _look.Fg, BorderThickness = new Thickness(0),
                FontFamily = Kind == "Markdown" ? _look.Mono : _look.Font, FontSize = Kind == "Markdown" ? _look.Size * 0.95 : _look.Size,
                Padding = new Thickness(0), MinHeight = _look.Size * 3, SelectionBrush = _look.Accent
            };
            _editor.TextChanged += delegate { if (_editing) Touched(); };
            _editor.PreviewKeyDown += Editor_KeyDown;
            panel.Children.Add(_editor);
            panel.Children.Add(Small("Esc or click away to finish · saved as you type", _look.Dim));
            return panel;
        }

        Border ToolButton(string text, string tip, FontWeight w, FontStyle st, Action act)
        {
            var tb = new TextBlock { Text = text, FontWeight = w, FontStyle = st, Foreground = _look.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return Tool(tb, tip, act);
        }

        Border ToolGlyph(string glyph, string tip, Action act)
        {
            var tb = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(9, _look.Size * 0.95), Foreground = _look.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return Tool(tb, tip, act);
        }

        Border Tool(UIElement content, string tip, Action act)
        {
            var b = new Border { Width = 28, Height = 26, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 3, 3), Background = _look.CodeBg, Cursor = Cursors.Hand, ToolTip = tip, Child = content };
            // PreviewMouseDown: keep the text box focused (and its selection) while a button is pressed
            b.PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; act(); if (_editor != null) _editor.Focus(); };
            return b;
        }

        void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Escape || (ctrl && e.Key == Key.Enter)) { e.Handled = true; StopTyping(); return; }
            if (!ctrl) return;
            if (e.Key == Key.B) { e.Handled = true; Wrap("**"); }
            else if (e.Key == Key.I) { e.Handled = true; Wrap("*"); }
            else if (e.Key == Key.L) { e.Handled = true; Prefix("- ", false); }
        }

        /// <summary>Put marks around the selection (or the caret, ready to type).</summary>
        void Wrap(string mark)
        {
            if (_editor == null) return;
            int s = _editor.SelectionStart, n = _editor.SelectionLength;
            string sel = _editor.SelectedText;
            _editor.SelectedText = mark + sel + mark;
            _editor.Select(s + mark.Length, n);
        }

        /// <summary>Start the caret's line with the mark (again = remove it; toggle = a heading cycles off).</summary>
        void Prefix(string mark, bool toggle)
        {
            if (_editor == null) return;
            string t = _editor.Text;
            int caret = _editor.CaretIndex;
            int start = caret == 0 ? 0 : t.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            if (string.CompareOrdinal(t, start, mark, 0, mark.Length) == 0)
            {
                _editor.Text = t.Remove(start, mark.Length);
                _editor.CaretIndex = Math.Max(start, caret - mark.Length);
            }
            else
            {
                _editor.Text = t.Insert(start, mark);
                _editor.CaretIndex = caret + mark.Length;
            }
        }

        void Link()
        {
            if (_editor == null) return;
            string sel = _editor.SelectedText;
            int s = _editor.SelectionStart;
            string ins = "[" + (sel.Length > 0 ? sel : "text") + "](https://)";
            _editor.SelectedText = ins;
            _editor.Select(s + ins.Length - 9, 8);     // the address, ready to type over
        }

        // ---- Checklist ------------------------------------------------------------------------------
        FrameworkElement BuildChecklist()
        {
            var panel = new StackPanel();
            var nt = new NoteText(_text);
            var rows = new StackPanel();
            _itemsPanel = rows;
            var items = nt.Items();
            var order = new List<NoteText.Item>();
            foreach (var it in items) if (!it.Done || _cfg.NoteDone == "Strike") order.Add(it);
            if (_cfg.NoteDone == "Bottom") foreach (var it in items) if (it.Done) order.Add(it);
            // headings in the file show as small titles over the items that follow them (Strike only: the order is the file's)
            int nextHeading = 0;
            DateTime now = DateTime.Now;
            foreach (var it in order)
            {
                if (_cfg.NoteDone != "Bottom")
                    for (; nextHeading < it.Line; nextHeading++)
                    {
                        string l = nt.Lines[nextHeading].Trim();
                        if (l.StartsWith("#")) rows.Children.Add(new TextBlock
                        {
                            Text = l.TrimStart('#').Trim(), FontWeight = FontWeights.SemiBold, Foreground = _look.Dim,
                            FontSize = Math.Max(8, _look.Size * 0.85), Margin = new Thickness(0, rows.Children.Count > 0 ? 6 : 0, 0, 2)
                        });
                    }
                rows.Children.Add(ItemRow(it, now));
                nextHeading = Math.Max(nextHeading, it.Line + 1);
            }
            if (items.Count == 0 && Locked)
                rows.Children.Add(new TextBlock { Text = "Nothing on the list", Foreground = _look.Dim, FontStyle = FontStyles.Italic });

            // reorder: a line shows where the dragged item goes
            var holder = new Grid();
            holder.Children.Add(rows);
            _dropLine = new Border { Height = 2, Background = _look.Accent, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            holder.Children.Add(_dropLine);
            panel.Children.Add(holder);

            if (!Locked)
            {
                FrameworkElement add = InputBox("Add an item", delegate (string text)
                {
                    var n2 = new NoteText(_text);
                    n2.AddItem(text);
                    Commit(n2);
                    FocusInput();
                }, out _input);
                add.Margin = new Thickness(0, items.Count > 0 ? 6 : 0, 0, 0);
                panel.Children.Add(add);
            }
            return panel;
        }

        FrameworkElement ItemRow(NoteText.Item it, DateTime now)
        {
            var g = new Grid { Margin = new Thickness(Math.Min(6, it.Indent) * 14, 2, 0, 2), Background = Brushes.Transparent, Tag = it.Line };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // grip: drag to reorder (shows on hover)
            var grip = new TextBlock
            {
                Text = "", FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(8, _look.Size * 0.8), Foreground = _look.Dim, Opacity = 0,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Cursor = Locked ? null : Cursors.SizeNS, Width = 10
            };
            if (!Locked)
            {
                g.MouseEnter += delegate { grip.Opacity = 0.8; };
                g.MouseLeave += delegate { if (_dragLine < 0) grip.Opacity = 0; };
                int line = it.Line;
                grip.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; _dragLine = line; grip.CaptureMouse(); };
                grip.MouseMove += delegate (object s, MouseEventArgs e) { if (_dragLine >= 0) ShowDrop(e.GetPosition(_itemsPanel).Y); };
                grip.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; grip.ReleaseMouseCapture(); DropItem(e.GetPosition(_itemsPanel).Y); };
                grip.LostMouseCapture += delegate { if (_dragLine >= 0) { _dragLine = -1; if (_dropLine != null) _dropLine.Visibility = Visibility.Collapsed; } };
            }
            g.Children.Add(grip);

            int ln = it.Line;
            bool done = it.Done;
            FrameworkElement box = NoteRender.CheckBox(done, _look, Locked ? (Action)null : delegate
            {
                var nt = new NoteText(_text);
                nt.SetDone(ln, !done);
                Commit(nt);
            });
            box.Margin = new Thickness(0, 1, 8, 0);
            box.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(box, 1);
            g.Children.Add(box);

            FrameworkElement text;
            if (_itemLine == it.Line)
            {
                _itemBox = new TextBox
                {
                    Text = it.Text, Background = Brushes.Transparent, Foreground = _look.Fg, CaretBrush = _look.Fg, BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = _look.Accent, Padding = new Thickness(0), FontFamily = _look.Font, FontSize = _look.Size, SelectionBrush = _look.Accent
                };
                _itemBox.PreviewKeyDown += delegate (object s, KeyEventArgs e)
                {
                    if (e.Key == Key.Enter) { e.Handled = true; FinishItemEdit(true); }
                    else if (e.Key == Key.Escape) { e.Handled = true; FinishItemEdit(false); }
                };
                _itemBox.LostKeyboardFocus += delegate { if (_itemLine >= 0) FinishItemEdit(true); };
                text = _itemBox;
            }
            else
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = done ? _look.Dim : _look.Fg };
                NoteRender.AddInlines(tb.Inlines, it.Text, _look, false);
                if (done) tb.TextDecorations = TextDecorations.Strikethrough;
                if (!Locked)
                    tb.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
                    {
                        if (e.ClickCount != 2) return;
                        e.Handled = true;
                        EditItem(ln);
                    };
                text = tb;
            }
            Grid.SetColumn(text, 2);
            g.Children.Add(text);

            if (it.Due.HasValue)
            {
                bool late = !done && it.Due.Value.Date < now.Date;
                bool today = !done && it.Due.Value.Date == now.Date;
                var due = new TextBlock
                {
                    Text = late ? "overdue" : NoteText.DueText(it.Due.Value, now), FontSize = Math.Max(8, _look.Size * 0.8),
                    Foreground = late || today ? (Brush)_t.SecondaryAccent : _look.Dim, Margin = new Thickness(8, 1, 0, 0),
                    ToolTip = it.Due.Value.ToString("D", CultureInfo.CurrentCulture)
                };
                if (late) due.FontWeight = FontWeights.SemiBold;
                Grid.SetColumn(due, 3);
                g.Children.Add(due);
            }
            return g;
        }

        /// <summary>Where a dragged item would go: before the row under the pointer's half.</summary>
        int DropBefore(double y, out double lineY)
        {
            lineY = 0;
            int before = -1;
            double lastBottom = 0;
            int lastLine = -1;
            foreach (UIElement c in _itemsPanel.Children)
            {
                var row = c as Grid;
                if (row == null || !(row.Tag is int)) continue;
                Point p = row.TranslatePoint(new Point(0, 0), _itemsPanel);
                double h = row.ActualHeight;
                if (before < 0 && y < p.Y + h / 2) { before = (int)row.Tag; lineY = p.Y - 1; }
                lastBottom = p.Y + h;
                lastLine = (int)row.Tag;
            }
            if (before < 0) { before = lastLine + 1; lineY = lastBottom; }
            return before;
        }

        void ShowDrop(double y)
        {
            if (_dropLine == null || _itemsPanel == null) return;
            double ly;
            DropBefore(y, out ly);
            _dropLine.Margin = new Thickness(0, Math.Max(0, ly), 0, 0);
            _dropLine.Visibility = Visibility.Visible;
        }

        void DropItem(double y)
        {
            int from = _dragLine;
            _dragLine = -1;
            if (_dropLine != null) _dropLine.Visibility = Visibility.Collapsed;
            if (from < 0 || _itemsPanel == null) return;
            double ly;
            int before = DropBefore(y, out ly);
            var nt = new NoteText(_text);
            nt.MoveLine(from, before);
            Commit(nt);
        }

        void EditItem(int line)
        {
            if (Locked) return;
            _itemLine = line;
            Build();
            Keyboard_(_itemBox, true);
        }

        void FinishItemEdit(bool keep)
        {
            int line = _itemLine;
            if (line < 0) return;
            _itemLine = -1;
            string text = _itemBox != null ? _itemBox.Text : null;
            _itemBox = null;
            if (keep && text != null)
            {
                var nt = new NoteText(_text);
                if (text.Trim().Length == 0) nt.RemoveLine(line); else nt.SetItemText(line, text);
                Commit(nt);
            }
            else Build();
        }

        // ---- Log ------------------------------------------------------------------------------------
        FrameworkElement BuildLog()
        {
            var panel = new StackPanel();
            if (!Locked)
            {
                FrameworkElement box = InputBox("Jot something, Enter to save", delegate (string text)
                {
                    var nt = new NoteText(_text);
                    nt.AddEntry(text, DateTime.Now);
                    Commit(nt);
                    FocusInput();
                }, out _input);
                box.Margin = new Thickness(0, 0, 0, 8);
                panel.Children.Add(box);
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var nt0 = new NoteText(_text);
            DateTime now = DateTime.Now;
            int row = 0;
            foreach (NoteText.Entry e in nt0.Entries())
            {
                if (row >= _cfg.NoteLogMax) break;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool recent = (now.Date - e.When.Date).Days == 0;
                var when = new TextBlock
                {
                    Text = NoteText.WhenText(e.When, now), Foreground = recent ? _look.Accent : _look.Dim, FontSize = Math.Max(8, _look.Size * 0.82),
                    Margin = new Thickness(0, 2, 10, 3), VerticalAlignment = VerticalAlignment.Top, ToolTip = e.When.ToString("f", CultureInfo.CurrentCulture)
                };
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3), Foreground = _look.Fg };
                NoteRender.AddInlines(text.Inlines, e.Text, _look, false);
                var hit = new Border { Background = Brushes.Transparent, Child = text, Tag = "log:" + e.Line };
                var hit2 = new Border { Background = Brushes.Transparent, Child = when, Tag = "log:" + e.Line };
                Grid.SetRow(hit2, row); Grid.SetRow(hit, row); Grid.SetColumn(hit, 1);
                grid.Children.Add(hit2);
                grid.Children.Add(hit);
                row++;
            }
            if (row == 0)
                panel.Children.Add(new TextBlock { Text = Locked ? "Nothing logged yet" : "Nothing logged yet - type above and press Enter", Foreground = _look.Dim, FontStyle = FontStyles.Italic });
            panel.Children.Add(grid);
            return panel;
        }

        /// <summary>The "add an item" / "jot something" box (with its placeholder): Enter adds, Esc leaves.</summary>
        FrameworkElement InputBox(string placeholder, Action<string> add, out TextBox box)
        {
            var tb = new TextBox
            {
                Background = Brushes.Transparent, Foreground = _look.Fg, CaretBrush = _look.Fg, BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4), FontFamily = _look.Font, FontSize = _look.Size, SelectionBrush = _look.Accent,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var hint = new TextBlock
            {
                Text = placeholder, Foreground = _look.Dim, FontFamily = _look.Font, FontSize = _look.Size, IsHitTestVisible = false,
                Margin = new Thickness(9, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var g = new Grid();
            g.Children.Add(new Border { Background = _look.CodeBg, CornerRadius = new CornerRadius(7) });
            g.Children.Add(hint);
            g.Children.Add(tb);
            tb.TextChanged += delegate { hint.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            tb.PreviewMouseLeftButtonDown += delegate { Keyboard_(tb, false); };
            tb.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && tb.Text.Trim().Length > 0) { e.Handled = true; string v = tb.Text; tb.Text = ""; add(v); }
                else if (e.Key == Key.Escape) { e.Handled = true; tb.Text = ""; StopTyping(); }
            };
            box = tb;
            return g;
        }

        // =================================================================
        // Typing (the widget takes the keyboard while you edit)
        // =================================================================
        /// <summary>Get the keyboard for a box: the widget comes in front and is activated, then the box gets the focus.</summary>
        void Keyboard_(TextBox box, bool selectAll)
        {
            if (box == null) return;
            var w = Widget;
            if (w != null) w.BeginTyping(this);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                box.Focus();
                Keyboard.Focus(box);
                if (selectAll) box.SelectAll(); else box.CaretIndex = box.Text.Length;
            }));
        }

        void FocusInput() { if (_input != null) Keyboard_(_input, false); }

        public void BeginEdit()
        {
            if (VDrop_) { if (_feed != null) _feed.FocusCompose(); return; }
            if (!TextKind || Locked) return;
            if (!_editing) { _editing = true; Build(); }
            Keyboard_(_editor, false);
        }

        /// <summary>Finish typing (Esc, Done, clicking away): save, show the note, and give the keyboard back.</summary>
        public void StopTyping()
        {
            if (VDrop_)
            {
                if (_feed != null) _feed.StopTyping();
                var fw = Widget;
                if (fw != null) fw.EndTyping(this);
                return;
            }
            if (_itemLine >= 0) FinishItemEdit(true);
            if (_editing)
            {
                SaveNow();
                _editing = false;
                _editor = null;
                Build();
            }
            else SaveNow();
            var w = Widget;
            if (w != null) w.EndTyping(this);
        }

        /// <summary>The quick-note key (Win+Shift+N): show this note and start typing in it.</summary>
        public void QuickNote()
        {
            var w = Widget;
            if (w != null) w.ExpandIfCollapsed();
            if (VDrop_) { BeginEdit(); return; }       // the add box
            if (Locked) return;
            if (TextKind) BeginEdit();
            else FocusInput();
        }

        // ---- click to edit / drag the widget ----------------------------------------------------------
        void Body_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (VDrop_) return;
            if (e.Handled || !TextKind || _editing || Locked) return;
            // the scrollbar (its thumb or its empty track): scrolling - not editing, not dragging the widget
            for (DependencyObject d = e.OriginalSource as DependencyObject; d != null && d != _bodyHost; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.ScrollBar) { e.Handled = true; return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
            if (_cfg.NoteEditOn == "Double-click")
            {
                if (e.ClickCount == 2) { e.Handled = true; BeginEdit(); }
                return;          // a single press still drags the widget
            }
            if (_cfg.NoteEditOn != "Click") return;
            // a click (no movement) edits; a drag moves the widget as usual
            e.Handled = true;
            _downPending = true;
            _downAt = e.GetPosition(this);
            _bodyHost.CaptureMouse();
        }

        void Body_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_downPending || e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(this);
            if (Math.Abs(p.X - _downAt.X) + Math.Abs(p.Y - _downAt.Y) < 5) return;
            _downPending = false;
            _bodyHost.ReleaseMouseCapture();
            var w = Widget;
            if (w != null) w.DragFromModule();
        }

        void Body_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_downPending) return;
            _downPending = false;
            _bodyHost.ReleaseMouseCapture();
            e.Handled = true;
            BeginEdit();
        }

        // =================================================================
        // Pages (stored in vShell)
        // =================================================================
        void Flip(int by)
        {
            if (File_ || VDrop_ || Pages.Count < 2) return;
            StopEditingQuietly();
            _cfg.NoteIndex = ((_cfg.NoteIndex + by) % Pages.Count + Pages.Count) % Pages.Count;
            _mgr.SaveSoon();
            Load();
            Build();
        }

        void StopEditingQuietly()
        {
            if (_itemLine >= 0) FinishItemEdit(true);
            SaveNow();
            if (_editing) { _editing = false; _editor = null; }
        }

        void NewPage()
        {
            StopEditingQuietly();
            _cfg.NotePages.Add(new NotePage());
            _cfg.NoteIndex = _cfg.NotePages.Count - 1;
            _mgr.SaveSoon();
            Load();
            Build();
            if (TextKind) BeginEdit(); else FocusInput();
        }

        void RenamePage()
        {
            string name = PromptWindow.Ask(Window.GetWindow(this), "Rename note", "Name (empty = its first line)", Page.Title);
            if (name == null) return;
            Page.Title = name.Trim();
            _mgr.SaveSoon();
            Build();
        }

        void DeletePage()
        {
            string what = PageTitle(_cfg.NoteIndex);
            if (Questions.YesNo(Window.GetWindow(this), "Delete the note \"" + what + "\"?", "Delete note") != MessageBoxResult.Yes) return;
            _editing = false; _editor = null; _dirty = false; _saveTimer.Stop();
            _cfg.NotePages.RemoveAt(_cfg.NoteIndex);
            if (Pages.Count == 0) _cfg.NotePages.Add(new NotePage());
            _cfg.NoteIndex = Math.Min(_cfg.NoteIndex, Pages.Count - 1);
            _mgr.SaveSoon();
            Load();
            Build();
        }

        // =================================================================
        // Right-click menu
        // =================================================================
        public void AddMenuItems(ContextMenu m, DependencyObject source)
        {
            if (VDrop_) { if (_feed != null) _feed.AddMenuItems(m, source); return; }
            int item = -1, entry = -1;
            for (DependencyObject d = source; d != null && d != this; d = ParentOf(d))
            {
                var fe = d as FrameworkElement;
                if (fe == null) continue;
                if (fe.Tag is int && item < 0) item = (int)fe.Tag;
                string tag = fe.Tag as string;
                if (tag != null && tag.StartsWith("log:")) int.TryParse(tag.Substring(4), out entry);
            }
            int before = m.Items.Count;

            if (Kind == "Checklist" && item >= 0 && !Locked)
            {
                int line = item;
                Add(m, "Edit item", delegate { EditItem(line); });
                var due = new MenuItem { Header = "Due date" };
                Sub(due, "Today", delegate { SetDue(line, DateTime.Today); });
                Sub(due, "Tomorrow", delegate { SetDue(line, DateTime.Today.AddDays(1)); });
                Sub(due, "In a week", delegate { SetDue(line, DateTime.Today.AddDays(7)); });
                Sub(due, "Pick a date…", delegate { PickDue(line); });
                due.Items.Add(new Separator());
                Sub(due, "No due date", delegate { SetDue(line, null); });
                m.Items.Add(due);
                Add(m, "Delete item", delegate { var nt = new NoteText(_text); nt.RemoveLine(line); Commit(nt); });
                m.Items.Add(new Separator());
            }
            if (Kind == "Log" && entry >= 0)
            {
                int line = entry;
                Add(m, "Copy entry", delegate
                {
                    var nt = new NoteText(_text);
                    NoteText.Entry en;
                    if (line < nt.Lines.Count && NoteText.TryEntry(nt.Lines[line], out en)) CopyText(en.Text);
                });
                if (!Locked) Add(m, "Delete entry", delegate { var nt = new NoteText(_text); nt.RemoveLine(line); Commit(nt); });
                m.Items.Add(new Separator());
            }

            if (TextKind && !Locked)
            {
                if (_editing) Add(m, "Finish editing", delegate { StopTyping(); });
                else Add(m, "Edit", delegate { BeginEdit(); });
            }
            if (Kind == "Checklist" && !Locked && new NoteText(_text).DoneCount() > 0)
                Add(m, "Remove done items", delegate
                {
                    var nt = new NoteText(_text);
                    int n = nt.DoneCount();
                    if (Questions.YesNo(Window.GetWindow(this), "Remove the " + n + " ticked item" + (n == 1 ? "" : "s") + "?", "Checklist") != MessageBoxResult.Yes) return;
                    nt.RemoveDone();
                    Commit(nt);
                });
            if (!File_)
            {
                Add(m, "New note", NewPage);
                if (Pages.Count > 1)
                {
                    Add(m, "Previous note", delegate { Flip(-1); });
                    Add(m, "Next note", delegate { Flip(1); });
                }
                Add(m, "Rename note…", RenamePage);
                if (Pages.Count > 1 || _text.Trim().Length > 0) Add(m, "Delete note…", DeletePage);
            }
            else if (!string.IsNullOrWhiteSpace(_cfg.NoteActiveFile))
            {
                Add(m, "Open the file", delegate { SaveNow(); NoteRender.Open(_cfg.NoteActiveFile); });
                Add(m, "Show it in its folder", delegate
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + _cfg.NoteActiveFile + "\""); }
                    catch (Exception ex) { Log.Write("Notes show in folder: " + ex.Message); }
                });
                Add(m, "Read the file again", delegate { ReloadFromFile(true); });
            }
            Add(m, "Copy all text", delegate { SaveNow(); CopyText(_text); });
            Add(m, Locked ? "Allow editing" : "Lock (read only)", delegate { _cfg.NoteLocked = !_cfg.NoteLocked; _mgr.SettingsChanged(); });
            if (m.Items.Count > before) m.Items.Add(new Separator());
        }

        static DependencyObject ParentOf(DependencyObject d)
        {
            if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            var ce = d as ContentElement;
            if (ce != null) return ContentOperations.GetParent(ce) ?? LogicalTreeHelper.GetParent(d);
            return LogicalTreeHelper.GetParent(d);
        }

        static void Add(ContextMenu m, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }

        static void Sub(MenuItem parent, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            parent.Items.Add(mi);
        }

        void SetDue(int line, DateTime? due)
        {
            var nt = new NoteText(_text);
            nt.SetDue(line, due);
            Commit(nt);
        }

        void PickDue(int line)
        {
            var nt = new NoteText(_text);
            NoteText.Item it;
            string cur = line < nt.Lines.Count && NoteText.TryTask(nt.Lines[line], out it) && it.Due.HasValue
                ? it.Due.Value.ToString("d", CultureInfo.CurrentCulture) : DateTime.Today.ToString("d", CultureInfo.CurrentCulture);
            string s = PromptWindow.Ask(Window.GetWindow(this), "Due date", "Date (e.g. " + DateTime.Today.AddDays(3).ToString("d", CultureInfo.CurrentCulture) + ")", cur);
            if (s == null) return;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out d)) SetDue(line, d.Date);
            else Questions.Tell(Window.GetWindow(this), "\"" + s + "\" is not a date.", "Due date");
        }

        static void CopyText(string s)
        {
            try { Clipboard.SetText(s ?? ""); } catch (Exception ex) { Log.Write("Notes copy: " + ex.Message); }
        }
    }
}
