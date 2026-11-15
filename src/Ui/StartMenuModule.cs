using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The vShell mark: three dots arranged as a v - by default the Main Accent, Secondary Accent and Text colours, the same three as the
    /// separator dots. Used as the default Start button, on the splash screen and (as a fixed-colour picture) for the exe icon.
    /// </summary>
    public sealed class VShellMark : FrameworkElement
    {
        public Brush Dot1 = Brushes.SteelBlue, Dot2 = Brushes.Pink, Dot3 = Brushes.White;
        public double Dim = 20;
        /// <summary>The faint v joining the dots (only worth drawing at larger sizes).</summary>
        public bool ShowLine;

        protected override Size MeasureOverride(Size availableSize) { return new Size(Dim, Dim); }

        protected override void OnRender(DrawingContext dc)
        {
            double d = Math.Min(ActualWidth, ActualHeight);
            if (d < 4) return;
            double k = d / 100.0, ox = (ActualWidth - d) / 2, oy = (ActualHeight - d) / 2;
            Func<double, double, Point> P = delegate (double x, double y) { return new Point(ox + x * k, oy + y * k); };
            double r = (d < 24 ? 18 : 16) * k;      // a little bigger when tiny, so the dots stay round blobs
            if (ShowLine)
            {
                var pen = new Pen(Dot3, 3.2 * k) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var g = new StreamGeometry();
                using (StreamGeometryContext c = g.Open())
                {
                    c.BeginFigure(P(25, 29), false, false);
                    c.LineTo(P(50, 73), true, true);
                    c.LineTo(P(75, 29), true, true);
                }
                g.Freeze();
                dc.PushOpacity(0.28);
                dc.DrawGeometry(null, pen, g);
                dc.Pop();
            }
            dc.DrawEllipse(Dot1, null, P(25, 29), r, r);
            dc.DrawEllipse(Dot2, null, P(75, 29), r, r);
            dc.DrawEllipse(Dot3, null, P(50, 73), r, r);
        }
    }

    /// <summary>
    /// The other Start button icons (0.38.20), drawn on a 32 x 32 grid in the theme's Main Accent (Ac), Secondary Accent (Sp) and text (Fg).
    /// </summary>
    public sealed class StartGlyph : FrameworkElement
    {
        public string Kind = "Tiles";
        public Brush Ac = Brushes.SteelBlue, Sp = Brushes.Pink, Fg = Brushes.White;
        public double Dim = 20;

        protected override Size MeasureOverride(Size availableSize) { return new Size(Dim, Dim); }

        /// <summary>The icon for a kind - the vShell mark, or one of these.</summary>
        public static FrameworkElement Make(string kind, Brush ac, Brush sp, Brush fg, double size)
        {
            if (string.IsNullOrEmpty(kind) || kind == "vShell" || kind == "Custom")
                return new VShellMark { Dot1 = ac, Dot2 = sp, Dot3 = fg, Dim = size, ShowLine = size >= 40, VerticalAlignment = VerticalAlignment.Center };
            return new StartGlyph { Kind = kind, Ac = ac, Sp = sp, Fg = fg, Dim = size, VerticalAlignment = VerticalAlignment.Center };
        }

        protected override void OnRender(DrawingContext dc)
        {
            double d = Math.Min(ActualWidth, ActualHeight);
            if (d < 4) return;
            double k = d / 32.0, ox = (ActualWidth - d) / 2, oy = (ActualHeight - d) / 2;
            Func<double, double, Point> P = delegate (double x, double y) { return new Point(ox + x * k, oy + y * k); };
            Action<Brush, double, double, double, double, double> R = delegate (Brush b, double x, double y, double w, double h, double r)
            {
                dc.DrawRoundedRectangle(b, null, new Rect(P(x, y), new Size(w * k, h * k)), r * k, r * k);
            };
            Action<Brush, double, double, double> C = delegate (Brush b, double x, double y, double r) { dc.DrawEllipse(b, null, P(x, y), r * k, r * k); };
            Func<Brush, Pen> Line = delegate (Brush b) { return new Pen(b, Math.Max(1, 2.5 * k)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; };
            switch (Kind)
            {
                case "Tiles":
                    R(Ac, 6, 6, 9, 9, 2); R(Ac, 17, 6, 9, 9, 2); R(Ac, 6, 17, 9, 9, 2); R(Sp, 17, 17, 9, 9, 2);
                    break;
                case "Grid":
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 3; c++) C(r == 1 && c == 1 ? Sp : Ac, 9 + c * 7, 9 + r * 7, 2.6);
                    break;
                case "Orbit":
                    dc.DrawEllipse(null, Line(Ac), P(16, 16), 9 * k, 9 * k);
                    C(Fg, 16, 16, 3.5);
                    C(Sp, 23, 9.5, 3);
                    break;
                case "Hexagon":
                    {
                        var g = new StreamGeometry();
                        using (StreamGeometryContext c = g.Open())
                        {
                            c.BeginFigure(P(16, 4), false, true);
                            c.LineTo(P(26.4, 10), true, true); c.LineTo(P(26.4, 22), true, true); c.LineTo(P(16, 28), true, true);
                            c.LineTo(P(5.6, 22), true, true); c.LineTo(P(5.6, 10), true, true);
                        }
                        g.Freeze();
                        dc.DrawGeometry(null, Line(Ac), g);
                        C(Sp, 16, 16, 4);
                        break;
                    }
                case "Bars":
                    R(Ac, 6, 8, 20, 4, 2); R(Sp, 6, 14, 14, 4, 2); R(Fg, 6, 20, 17, 4, 2);
                    break;
                case "Diamonds":
                    {
                        // four diamonds in a diamond: three in the Main Accent, the bottom one in the Secondary Accent
                        Action<Brush, double, double> D = delegate (Brush b, double cx, double cy)
                        {
                            const double h = 6.2;
                            var g = new StreamGeometry();
                            using (StreamGeometryContext c = g.Open())
                            {
                                c.BeginFigure(P(cx, cy - h), true, true);
                                c.LineTo(P(cx + h, cy), true, true); c.LineTo(P(cx, cy + h), true, true); c.LineTo(P(cx - h, cy), true, true);
                            }
                            g.Freeze();
                            dc.DrawGeometry(b, new Pen(b, Math.Max(0.5, 1.2 * k)) { LineJoin = PenLineJoin.Round }, g);
                        };
                        D(Ac, 16, 8.5); D(Ac, 8.5, 16); D(Ac, 23.5, 16); D(Sp, 16, 23.5);
                        break;
                    }
            }
        }
    }

    /// <summary>Start button on the bar. Opens a themed menu with every installed program, a search box and the power options.</summary>
    public sealed class StartMenuModule : Border
    {
        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        PopupWindow _popup;

        public ModuleConfig Config { get { return _cfg; } }
        public IntPtr MonitorHandle { get { return _bar.MonitorHandle; } }
        public bool IsMenuOpen { get { return _popup != null && _popup.IsOpen; } }

        public StartMenuModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            var t = bar.Theme.ForModule(cfg);

            Padding = new Thickness(6, 3, 6, 3);
            Margin = new Thickness(2, 0, 2, 0);
            CornerRadius = new CornerRadius(4);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            ToolTip = "Start";
            VerticalAlignment = VerticalAlignment.Center;

            double size = ModuleFit.Icon(bar, Math.Max(12, cfg.IconSize), 6);     // shrinks to fit a slim bar
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            ImageSource custom = cfg.StartIcon == "Custom" ? IconUtil.FromIconFile(cfg.IconPath) : null;
            if (custom != null)
            {
                var img = new Image { Source = custom, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                row.Children.Add(img);
            }
            else
            {
                // the chosen icon in the theme's colours (Custom without a file that loads = the vShell mark)
                row.Children.Add(StartGlyph.Make(cfg.StartIcon, t.MainAccent, t.SecondaryAccent, t.Fg, size));
            }

            Child = row;

            MouseEnter += delegate { if (!IsMenuOpen) Background = t.Hover; };
            MouseLeave += delegate { Background = IsMenuOpen ? (Brush)t.ActiveBg : Brushes.Transparent; };
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Toggle(); };
            Unloaded += delegate { Close(); };

            // read the Start Menu folders, ask for the Store apps and load every icon in the background now; build the menu itself a
            // moment later (when nothing else is going on), so even the first click opens it straight away
            StartIcons.Preload();
            var prebuild = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(3) };
            prebuild.Tick += delegate { prebuild.Stop(); if (_alive && _view == null) EnsureView(); };
            prebuild.Start();
            Unloaded += delegate { _alive = false; prebuild.Stop(); if (_view != null) { _view.Dispose(); _view = null; } };
        }

        // the menu is built once and kept (hidden) between opens; only a new theme / bar / module makes a new one
        StartMenuView _view;
        string _viewKey;      // height + widget style + sections the kept menu was built for
        bool _alive = true;

        /// <summary>Build 2: the module's S / M / L sets the menu's text size (the button is just the icon).</summary>
        Theme MenuTheme()
        {
            double fs = _cfg.TextSize == "L" ? 16 : _cfg.TextSize == "S" ? 12 : 14;
            return _bar.Theme.WithFontSize(Math.Round(fs * BarText.Factor(_bar.Theme.Font), 1));
        }

        StartMenuView EnsureView()
        {
            double h = MenuHeight();
            WidgetStyle ws = _mgr.Settings.WidgetStyle;
            Theme mt = MenuTheme();
            string key = h.ToString("0") + "|" + mt.FontSize + "|" + (ws == null ? "" : System.Text.Json.JsonSerializer.Serialize(ws)) +
                         "|" + _cfg.StartPinned + _cfg.StartFolders + _cfg.StartRecent + "|" + _cfg.StartStyle;
            if (_view != null && key != _viewKey) { _view.Dispose(); _view = null; }
            if (_view == null)
            {
                _view = new StartMenuView(mt, h, Close, ws, _cfg, delegate { _mgr.Settings.Save(); });
                _viewKey = key;
            }
            return _view;
        }

        public void Toggle()
        {
            if (IsMenuOpen) Close();
            else Open();
        }

        public void Close()
        {
            if (_popup != null) _popup.CloseSafe();
        }

        /// <summary>Cascading (0.38.200): vShell's own menu at the button, each folder opening in a flyout beside it (StartCascadeMenu).</summary>
        void OpenCascade()
        {
            var menu = new StartCascadeMenu(_mgr, _bar, this, MenuTheme(), _mgr.Settings.WidgetStyle, _cfg, delegate { _mgr.Settings.Save(); });
            PopupWindow popup = menu.Main;
            _popup = popup;
            popup.Closed += delegate { if (_popup == popup) { _popup = null; SetActive(false); } };
            _mgr.ShowPopup(popup);
            popup.ForceForeground();
            SetActive(true);
        }

        void SetActive(bool on)
        {
            Background = on ? (Brush)_bar.Theme.ActiveBg : Brushes.Transparent;
        }

        /// <summary>Menu height: as tall as comfortable, but never taller than the screen minus the bar.</summary>
        double MenuHeight()
        {
            try
            {
                RECT r;
                Native.GetWindowRect(_bar.Handle, out r);
                MonitorInfo mon = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                uint dpi = Native.GetDpiForWindow(_bar.Handle);
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;
                if (mon != null)
                {
                    double avail = (mon.Bounds.Height - r.Height) / scale - 90;
                    return Math.Max(320, Math.Min(660, avail));
                }
            }
            catch { }
            return 600;
        }

        void Open()
        {
            if (_cfg.StartStyle == "Cascade") { OpenCascade(); return; }
            var t = MenuTheme();
            StartMenuView view = EnsureView();
            view.Reset();
            var holder = view.Root.Parent as Border;       // still in the last (closed) popup: take it out
            if (holder != null) holder.Child = null;
            var popup = new PopupWindow(_mgr, _bar, this, t, view.Root);
            // the menu looks like a widget of the current widget style (background, corners, outline, shadow, padding)
            if (_mgr.Settings.WidgetStyle != null) popup.UseWidgetStyle(t, _mgr.Settings.WidgetStyle);
            _popup = popup;
            popup.Activated += delegate { view.FocusSearch(); };
            popup.Closed += delegate
            {
                popup.SetContent(null);                     // the menu is kept for next time
                if (_popup == popup) { _popup = null; SetActive(false); }
            };
            _mgr.ShowPopup(popup);
            popup.ForceForeground();
            SetActive(true);
        }
    }

    // =====================================================================================
    // One row of the program list
    // =====================================================================================
    /// <summary>How a row that isn't a plain program looks and what it does (pinned shortcuts and groups, folders, recent files).</summary>
    sealed class RowLook
    {
        /// <summary>Shown when there is no icon (a group, a folder, a web address).</summary>
        public string Glyph;
        public Brush GlyphBrush;
        /// <summary>A › on the right: it opens a folder / group in the menu.</summary>
        public bool Chevron;
        /// <summary>Dim text on the right (a recent file's time).</summary>
        public string Note;
        /// <summary>A tile (icon over the name) for the Pinned section, instead of a row.</summary>
        public bool Tile;
        /// <summary>What a click / Enter does instead of starting Entry.Target.</summary>
        public Action Open;
        public string Tip;
    }

    sealed class AppRow : Border
    {
        public const double TileWidth = 86;

        public readonly AppEntry Entry;
        public readonly Action Open;
        public int Index;
        readonly Image _img;
        readonly TextBlock _glyph;
        readonly TextBlock _text;
        readonly Brush _mainAccent;

        public AppRow(AppEntry e, Brush accent) : this(e, accent, null, null, null) { }

        public AppRow(AppEntry e, Brush accent, string query, Brush match) : this(e, accent, query, match, null) { }

        /// <summary>query / match: the part of the name that matches the search is drawn in the match brush.</summary>
        public AppRow(AppEntry e, Brush accent, string query, Brush match, RowLook look)
        {
            Entry = e;
            _mainAccent = accent;
            Open = look != null ? look.Open : null;
            bool tile = look != null && look.Tile;
            Padding = tile ? new Thickness(4, 8, 4, 6) : new Thickness(8, 4, 8, 4);
            CornerRadius = new CornerRadius(4);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            if (look != null && look.Tip != null) ToolTip = look.Tip;

            double px = tile ? 30 : 24;
            _img = new Image { Width = px, Height = px, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            if (e.Icon != null) _img.Source = e.Icon;
            var iconBox = new Grid { Width = tile ? 32 : 26, Height = px };
            iconBox.Children.Add(_img);
            if (look != null && look.Glyph != null)
            {
                _glyph = IconUtil.Glyph(look.Glyph, look.GlyphBrush, px * 0.75);
                _glyph.Visibility = e.Icon != null ? Visibility.Collapsed : Visibility.Visible;
                iconBox.Children.Add(_glyph);
            }
            var tb = new TextBlock
            {
                Text = e.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            _text = tb;
            if (tile)
            {
                // icon over a name of up to two lines
                Width = TileWidth;
                iconBox.HorizontalAlignment = HorizontalAlignment.Center;
                tb.Margin = new Thickness(0, 6, 0, 0);
                tb.TextAlignment = TextAlignment.Center;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.Loaded += delegate { tb.MaxHeight = Math.Ceiling(tb.FontSize * 1.34 * 2); };     // the menu's text size is only known once it's in the menu
                var st = new StackPanel();
                st.Children.Add(iconBox);
                st.Children.Add(tb);
                Child = st;
                return;
            }
            int at = string.IsNullOrEmpty(query) || match == null ? -1 : e.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (at >= 0 && at + query.Length <= e.Name.Length)
            {
                tb.Text = null;
                if (at > 0) tb.Inlines.Add(new Run(e.Name.Substring(0, at)));
                tb.Inlines.Add(new Run(e.Name.Substring(at, query.Length)) { Foreground = match, FontWeight = FontWeights.SemiBold });
                if (at + query.Length < e.Name.Length) tb.Inlines.Add(new Run(e.Name.Substring(at + query.Length)));
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(tb, 1);
            g.Children.Add(iconBox);
            g.Children.Add(tb);
            if (look != null && (look.Note != null || look.Chevron))
            {
                TextBlock right = look.Chevron
                    ? IconUtil.Glyph(LauncherModule.GlyphChevronRight, Brushes.Gray, 11)
                    : new TextBlock { Text = look.Note, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
                right.Margin = new Thickness(10, 0, 2, 0);
                if (look.Chevron) { right.ClearValue(TextBlock.ForegroundProperty); right.Opacity = 0.55; }     // the menu's text colour, dimmed
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
            }
            Child = g;
        }

        /// <summary>Hovered or keyboard-selected: the name turns the Main Accent colour.</summary>
        public void SetActive(bool on)
        {
            if (on) _text.Foreground = _mainAccent;
            else _text.ClearValue(TextBlock.ForegroundProperty);      // back to the menu's text colour
        }

        public bool NeedsIcon { get { return Entry.Icon == null && !Entry.IconTried; } }

        public void LoadIcon()
        {
            if (Entry.Icon == null && !Entry.IconTried)
            {
                Entry.IconTried = true;
                try
                {
                    Entry.Icon = Entry.IsStore ? IconUtil.FromParsingName(Entry.Target, 48) : IconUtil.FromPath(Entry.Target);
                }
                catch (Exception ex) { Log.Write("Icon for '" + Entry.Name + "': " + ex.Message); }
            }
            if (Entry.Icon != null) _img.Source = Entry.Icon;
            if (_glyph != null) _glyph.Visibility = Entry.Icon != null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // =====================================================================================
    // Pinning from the menu (right-click a program)
    // =====================================================================================
    /// <summary>The Start menu's pins (ModuleConfig.Entries) - shared by every style: find one, pin / unpin a program, open a target.</summary>
    static class StartPins
    {
        /// <summary>The pinned shortcut (top level, not a group) that starts this target, or null.</summary>
        public static LauncherEntry Find(ModuleConfig cfg, string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            foreach (LauncherEntry e in cfg.Entries)
                if (!e.IsGroup && string.Equals((e.Path ?? "").Trim().Trim('"'), target, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>Pin it (at the end) or, when it is pinned, unpin it. Pinning turns the Classic menu's Pinned section on. true = now pinned.</summary>
        public static bool Toggle(ModuleConfig cfg, string name, string target)
        {
            LauncherEntry e = Find(cfg, target);
            if (e != null) { cfg.Entries.Remove(e); return false; }
            cfg.Entries.Add(new LauncherEntry { Name = name ?? "", Path = target });
            cfg.StartPinned = true;
            return true;
        }

        /// <summary>The right-click menu on a program or a pin: Pin to Start / Unpin from Start (pinned = the pin itself, else found by
        /// the program's target). changed runs after the pins changed (they're saved by then).</summary>
        public static void ShowMenu(FrameworkElement at, Theme t, ModuleConfig cfg, string name, string target, LauncherEntry pinned, Action save, Action changed)
        {
            LauncherEntry cur = pinned ?? Find(cfg, target);
            var m = new ContextMenu();
            var mi = new MenuItem
            {
                Header = cur != null ? "Unpin from Start" : "Pin to Start",
                Icon = IconUtil.Glyph(cur != null ? "" : "", t.MainAccent, 13)
            };
            mi.Click += delegate
            {
                if (cur != null) cfg.Entries.Remove(cur);
                else Toggle(cfg, name, target);
                save();
                changed();
            };
            m.Items.Add(mi);
            m.PlacementTarget = at;
            m.Placement = PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>Open a program, shortcut, file, folder or ms-settings: page.</summary>
        public static void Open(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            if (SettingsLauncher.IsSettingsUri(target)) { SettingsLauncher.Open(target); return; }
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Start menu open '" + target + "': " + ex.Message); }
        }
    }

    // =====================================================================================
    // The menu itself: Classic, Grid or Two panes (Cascading is StartCascade, a real menu)
    // =====================================================================================
    sealed class StartMenuView : IDisposable
    {
        const double MenuWidth = 440, RailWidth = 46;

        /// <summary>What the list shows instead of the home list: a pinned group or Start Menu folder opened in the menu (drill-down),
        /// All programs (Grid's button), or one of Two panes' categories.</summary>
        sealed class Place
        {
            public string Title;
            public LauncherEntry Group;
            /// <summary>The Start Menu folder under Programs ("Accessories\System Tools") when it isn't a group.</summary>
            public string Folder;
            /// <summary>"All" (every program), "Pinned", "Recent", or "" (a group / folder).</summary>
            public string Kind = "";
            /// <summary>A Two panes category: no Back row (the list on the left is the way out).</summary>
            public bool NoBack;
            /// <summary>Which category on the left it is (Two panes): "pinned", "recent", "all" - a group is matched by Group.</summary>
            public string Key = "";
            /// <summary>Where the list was scrolled to before, for Back.</summary>
            public double ReturnOffset;
        }

        /// <summary>A category on the left of Two panes.</summary>
        sealed class CatRow
        {
            public Border Row;
            public TextBlock Label;
            public Place Place;
        }

        readonly Theme _t;
        readonly Action _close;
        readonly ModuleConfig _cfg;
        readonly Action _save;
        /// <summary>Classic, Grid or Panes.</summary>
        readonly string _style;
        readonly double _inner;
        public readonly Grid Root;
        readonly TextBox _search;
        readonly TextBlock _hint;
        readonly Border _searchBox;
        readonly StackPanel _home = new StackPanel();        // Classic: Pinned, Recent files (when on), then every program. Grid: Pinned, Recent files.
        readonly WrapPanel _pinPanel = new WrapPanel();
        readonly Panel _recentPanel;                         // a column (Classic), two columns (Grid)
        readonly List<AppRow> _pinRows = new List<AppRow>();
        readonly List<AppRow> _recentRows = new List<AppRow>();
        readonly StackPanel _placeList = new StackPanel();   // an opened group / folder / category
        readonly List<AppRow> _placeRows = new List<AppRow>();
        readonly List<Place> _trail = new List<Place>();     // the places opened, outermost first
        readonly StackPanel _allList = new StackPanel();     // every program, A-Z (built once, kept)
        readonly StackPanel _findList = new StackPanel();    // search results (small, rebuilt as you type)
        readonly List<AppRow> _allRows = new List<AppRow>();
        readonly List<AppRow> _findRows = new List<AppRow>();
        readonly StackPanel _cats = new StackPanel();        // Two panes: the categories on the left
        readonly List<CatRow> _catRows = new List<CatRow>();
        bool _allDirty = true;
        readonly ScrollViewer _scroll;
        readonly Border _power;
        Border _powerButton;
        List<AppRow> _rows;
        readonly Queue<AppRow> _iconQueue = new Queue<AppRow>();
        int _sel = -1;
        bool _dead;

        /// <summary>Grid and Two panes always have the Pinned part; Classic only with its switch on.</summary>
        bool ShowPinned { get { return _style != "Classic" || _cfg.StartPinned; } }

        public StartMenuView(Theme t, double height, Action close, WidgetStyle ws, ModuleConfig cfg, Action save)
        {
            _t = t; _close = close; _cfg = cfg; _save = save;
            _style = cfg.StartStyle == "Grid" || cfg.StartStyle == "Panes" ? cfg.StartStyle : "Classic";
            // inner rounded parts (rail, search box, power list) follow the widget style's corners, a little smaller
            _inner = ws == null ? 6 : Math.Max(0, Math.Min(14, ws.CornerRadius * 0.55));
            _recentPanel = _style == "Grid" ? (Panel)new UniformGrid { Columns = 2 } : new StackPanel();

            // ---- the search box --------------------------------------------------------------------
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mag = IconUtil.Glyph("", t.FgDim, 14);
            mag.Margin = new Thickness(0, 0, 8, 0);
            _search = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = t.Fg,
                CaretBrush = t.Fg,
                SelectionBrush = t.MainAccent,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null
            };
            _hint = new TextBlock { Text = "Search apps", Foreground = t.FgDim, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_search, 1);
            Grid.SetColumn(_hint, 1);
            sg.Children.Add(mag);
            sg.Children.Add(_search);
            sg.Children.Add(_hint);
            _searchBox = new Border { Background = t.Track, CornerRadius = new CornerRadius(_inner), Padding = new Thickness(10, 7, 10, 7), Child = sg };

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _home,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _power = PowerList(ws);

            if (_style == "Grid") Root = BuildGrid(height);
            else if (_style == "Panes") Root = BuildPanes(height);
            else Root = BuildClassic(height);
            LayoutHome();

            // ---- behaviour ----------------------------------------------------------------------------
            _search.TextChanged += delegate
            {
                _hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                // a search leaves the open folder; clearing it comes back to the full list (Two panes: to the category it was on)
                if (_search.Text.Length > 0 && _style != "Panes") _trail.Clear();
                BuildList();
                _scroll.ScrollToTop();
            };
            Root.PreviewKeyDown += OnKey;
            Root.PreviewTextInput += OnText;
            Root.PreviewMouseDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (_power.Visibility == Visibility.Visible && !IsInside(e.OriginalSource as DependencyObject, _power) && !IsInside(e.OriginalSource as DependencyObject, _powerButton))
                    _power.Visibility = Visibility.Collapsed;
            };
            Root.Loaded += delegate { Root.Dispatcher.BeginInvoke(new Action(FocusSearch), DispatcherPriority.Input); };

            AppCatalog.Changed += OnCatalogChanged;
            BuildCats();
            BuildSections();
            if (_style == "Panes") _trail.Add(DefaultCategory());
            BuildList();
        }

        // ---- the three layouts --------------------------------------------------------------------------
        /// <summary>Classic: a strip on the left (your folders, Settings, Power), then the search box over one list.</summary>
        Grid BuildClassic(double height)
        {
            // wider with bigger text (S / M / L) so the names still fit
            var root = new Grid { Width = Math.Round(Math.Max(410, Math.Min(540, MenuWidth * _t.FontSize / 13.0))), Height = height };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailWidth) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dock = new DockPanel { LastChildFill = false };
            _powerButton = RailButton("", "Power", TogglePower, false);
            DockPanel.SetDock(_powerButton, Dock.Bottom);
            var settings = RailButton("", "Settings", delegate { OpenTarget("ms-settings:"); }, true);
            DockPanel.SetDock(settings, Dock.Bottom);
            dock.Children.Add(_powerButton);
            dock.Children.Add(settings);
            foreach (Border b in FolderButtons()) { DockPanel.SetDock(b, Dock.Top); dock.Children.Add(b); }
            var rail = new Border { Background = _t.Hover, CornerRadius = new CornerRadius(_inner), Margin = new Thickness(0, 0, 8, 0), Child = dock };

            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(main, 1);
            Grid.SetRow(_scroll, 1);
            main.Children.Add(_searchBox);
            main.Children.Add(_scroll);
            Grid.SetColumn(_power, 1);

            root.Children.Add(rail);
            root.Children.Add(main);
            root.Children.Add(_power);
            return root;
        }

        /// <summary>Grid (0.38.199): the search box, your pinned programs six to a row (All programs behind a button), Recent files in two
        /// columns, and you, your folders, Settings and Power along the bottom.</summary>
        Grid BuildGrid(double height)
        {
            double w = 6 * AppRow.TileWidth + 30;
            var root = new Grid { Width = Math.Round(Math.Max(w, Math.Min(w * 1.2, w * _t.FontSize / 14.0))), Height = height };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pinPanel.Width = 6 * AppRow.TileWidth;
            _pinPanel.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(_scroll, 1);
            Border footer = Footer();
            Grid.SetRow(footer, 2);
            Grid.SetRowSpan(_power, 3);
            root.Children.Add(_searchBox);
            root.Children.Add(_scroll);
            root.Children.Add(footer);
            root.Children.Add(_power);
            return root;
        }

        /// <summary>Two panes (0.38.199): Pinned, Recent files, All programs and your pinned groups on the left; what you pick (or the
        /// search) on the right; you, your folders, Settings and Power along the bottom.</summary>
        Grid BuildPanes(double height)
        {
            var root = new Grid { Width = Math.Round(Math.Max(600, Math.Min(780, 640 * _t.FontSize / 14.0))), Height = height };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Round(190 * Math.Max(1, _t.FontSize / 14.0))) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var catScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _cats,
                Padding = new Thickness(4)
            };
            var left = new Border { Background = _t.Hover, CornerRadius = new CornerRadius(_inner), Margin = new Thickness(0, 0, 8, 0), Child = catScroll };
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right, 1);
            Grid.SetRow(_scroll, 1);
            right.Children.Add(_searchBox);
            right.Children.Add(_scroll);
            body.Children.Add(left);
            body.Children.Add(right);

            Border footer = Footer();
            Grid.SetRow(footer, 1);
            Grid.SetRowSpan(_power, 2);
            root.Children.Add(body);
            root.Children.Add(footer);
            root.Children.Add(_power);
            return root;
        }

        /// <summary>Grid's and Two panes' bottom row: your initial and name, then your folders, Settings and Power.</summary>
        Border Footer()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string user = Environment.UserName ?? "";
            var avatar = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = _t.MainAccent,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = user.Length > 0 ? user.Substring(0, 1).ToUpperInvariant() : "?",
                    Foreground = new SolidColorBrush(Color.FromRgb(_t.BgColor.R, _t.BgColor.G, _t.BgColor.B)),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var name = new TextBlock { Text = user, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (Border b in FolderButtons()) buttons.Children.Add(b);
            buttons.Children.Add(RailButton("", "Settings", delegate { OpenTarget("ms-settings:"); }, true));
            _powerButton = RailButton("", "Power", TogglePower, false);
            buttons.Children.Add(_powerButton);
            Grid.SetColumn(buttons, 2);
            g.Children.Add(avatar);
            g.Children.Add(name);
            g.Children.Add(buttons);
            // the power list opens above the Power button
            _power.HorizontalAlignment = HorizontalAlignment.Right;
            _power.Margin = new Thickness(0, 0, 0, 52);
            return new Border { BorderBrush = _t.Track, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = g };
        }

        /// <summary>Your folder, Documents, Downloads and Pictures - buttons that open them.</summary>
        List<Border> FolderButtons()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new List<Border>
            {
                RailButton("", Environment.UserName, delegate { OpenTarget(profile); }, true),
                RailButton("", "Documents", delegate { OpenTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); }, true),
                RailButton("", "Downloads", delegate { OpenTarget(System.IO.Path.Combine(profile, "Downloads")); }, true),
                RailButton("", "Pictures", delegate { OpenTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)); }, true)
            };
        }

        Border PowerList(WidgetStyle ws)
        {
            var pstack = new StackPanel();
            AddPower(pstack, "", "Lock", PowerActions.Lock);
            AddPower(pstack, "", "Sleep", PowerActions.Sleep);
            AddPower(pstack, "", "Sign out", PowerActions.SignOut);
            AddPower(pstack, "", "Restart", PowerActions.Restart);
            AddPower(pstack, "", "Shut down", PowerActions.ShutDown);
            return new Border
            {
                Background = _t.PopupBg,
                BorderBrush = ws != null && ws.ShowBorder ? (Brush)Theme.Faded(_t.Resolve(ws.BorderColor, _t.MainAccent), ws.BorderOpacity) : _t.Track,
                BorderThickness = new Thickness(ws != null && ws.ShowBorder ? Math.Max(1, Math.Min(4, ws.BorderThickness)) : 1),
                CornerRadius = new CornerRadius(ws == null ? 8 : Math.Max(0, Math.Min(16, ws.CornerRadius * 0.7))),
                Padding = new Thickness(4),
                Child = pstack,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 4),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black }
            };
        }

        void TogglePower()
        {
            _power.Visibility = _power.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>What the home list holds (Classic and Grid; Two panes has none - the left list picks what shows).</summary>
        void LayoutHome()
        {
            _home.Children.Clear();
            if (_style == "Panes") return;
            if (_style == "Grid")
            {
                _home.Children.Add(HeadingWithButton("Pinned", "All programs", delegate { OpenPlace(new Place { Title = "All programs", Kind = "All" }); }));
                _home.Children.Add(_pinPanel);
                if (_cfg.StartRecent) { _home.Children.Add(Heading("Recent files")); _home.Children.Add(_recentPanel); }
                return;
            }
            if (_cfg.StartPinned) { _home.Children.Add(Heading("Pinned")); _home.Children.Add(_pinPanel); }
            if (_cfg.StartRecent) { _home.Children.Add(Heading("Recent files")); _home.Children.Add(_recentPanel); }
            if (_cfg.StartPinned || _cfg.StartRecent) _home.Children.Add(Heading("All apps"));
            _home.Children.Add(_allList);
        }

        // ---- Two panes' categories -----------------------------------------------------------------------
        void BuildCats()
        {
            if (_style != "Panes") return;
            _cats.Children.Clear();
            _catRows.Clear();
            AddCat(new Place { Title = "Pinned", Kind = "Pinned", NoBack = true, Key = "pinned" }, "");
            if (_cfg.StartRecent) AddCat(new Place { Title = "Recent files", Kind = "Recent", NoBack = true, Key = "recent" }, "");
            AddCat(new Place { Title = "All programs", Kind = "All", NoBack = true, Key = "all" }, "");
            bool heading = false;
            foreach (LauncherEntry e in _cfg.Entries)
            {
                if (!e.IsGroup) continue;
                if (!heading)
                {
                    heading = true;
                    _cats.Children.Add(new TextBlock { Text = "Groups", Foreground = _t.FgDim, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 12, 0, 4) });
                }
                AddCat(new Place { Title = string.IsNullOrEmpty(e.Name) ? "Group" : e.Name, Group = e, NoBack = true }, IconUtil.GroupGlyph(e.IconGlyph) ?? IconUtil.GlyphFolder);
            }
            HighlightCats();
        }

        void AddCat(Place p, string glyph)
        {
            var row = new Border { Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var g = IconUtil.Glyph(glyph, _t.MainAccent, 14);
            g.Width = 22;
            var label = new TextBlock { Text = p.Title, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            line.Children.Add(g);
            line.Children.Add(label);
            row.Child = line;
            var cat = new CatRow { Row = row, Label = label, Place = p };
            row.MouseEnter += delegate { label.Foreground = _t.MainAccent; };
            row.MouseLeave += delegate { if (!IsCurrent(cat)) label.ClearValue(TextBlock.ForegroundProperty); };
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; ShowCategory(p); };
            _cats.Children.Add(row);
            _catRows.Add(cat);
        }

        static bool Same(Place a, Place b)
        {
            if (a == null || b == null) return false;
            if (a.Group != null || b.Group != null) return a.Group == b.Group;
            return a.Key == b.Key;
        }

        bool IsCurrent(CatRow c)
        {
            return _search.Text.Trim().Length == 0 && _trail.Count > 0 && Same(c.Place, _trail[0]);
        }

        void HighlightCats()
        {
            foreach (CatRow c in _catRows)
            {
                bool on = IsCurrent(c);
                c.Row.Background = on ? (Brush)_t.ActiveBg : Brushes.Transparent;
                if (on) c.Label.Foreground = _t.MainAccent; else c.Label.ClearValue(TextBlock.ForegroundProperty);
                c.Label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        /// <summary>The category Two panes opens on: Pinned when something is pinned, else All programs.</summary>
        Place DefaultCategory()
        {
            string key = _cfg.Entries.Count > 0 ? "pinned" : "all";
            foreach (CatRow c in _catRows) if (c.Place.Key == key) return c.Place;
            return new Place { Title = "All programs", Kind = "All", NoBack = true, Key = "all" };
        }

        void ShowCategory(Place p)
        {
            _trail.Clear();
            _trail.Add(p);
            if (_search.Text.Length > 0) _search.Text = "";      // builds the list (Two panes keeps the trail)
            else BuildList();
            _scroll.ScrollToTop();
        }

        public void Dispose()
        {
            _dead = true;
            AppCatalog.Changed -= OnCatalogChanged;
        }

        public void FocusSearch()
        {
            if (_dead) return;
            _search.Focus();
            Keyboard.Focus(_search);
        }

        void OnCatalogChanged()
        {
            if (_dead) return;
            _allDirty = true;
            double off = _scroll.VerticalOffset;
            BuildList();
            if (_search.Text.Length == 0) _scroll.ScrollToVerticalOffset(off);
        }

        /// <summary>Opening again: empty search, no folder open (Two panes: its first category), top of the list, nothing selected,
        /// power list closed; pinned and recent files fresh.</summary>
        public void Reset()
        {
            if (_dead) return;
            _power.Visibility = Visibility.Collapsed;
            _trail.Clear();
            BuildCats();
            BuildSections();
            if (_style == "Panes") _trail.Add(DefaultCategory());
            if (_search.Text.Length > 0) _search.Text = "";      // shows the full list again
            else BuildList();
            Select(-1, false);
            _scroll.ScrollToTop();
        }

        /// <summary>Pinned / unpinned from the menu: everything that shows pins again, where it was.</summary>
        void Refresh()
        {
            if (_dead) return;
            double off = _scroll.VerticalOffset;
            if (_style == "Panes" && _trail.Count > 0 && _trail[0].Group != null && !_cfg.Entries.Contains(_trail[0].Group)) _trail.Clear();
            LayoutHome();
            BuildCats();
            BuildSections();
            if (_style == "Panes" && _trail.Count == 0) _trail.Add(DefaultCategory());
            BuildList();
            _scroll.UpdateLayout();
            _scroll.ScrollToVerticalOffset(off);
        }

        // ---- building the list ---------------------------------------------------------------------
        void BuildList()
        {
            Select(-1, false);
            _iconQueue.Clear();
            string q = _search.Text.Trim();

            if (q.Length == 0 && _trail.Count > 0)
            {
                _rows = BuildPlace(_trail[_trail.Count - 1]);
                if (_scroll.Content != _placeList) _scroll.Content = _placeList;
            }
            else if (q.Length == 0)
            {
                if (_allDirty && _style == "Classic") BuildAll();
                // Pinned, Recent files and (Classic) the programs are one list for the arrow keys
                var rows = new List<AppRow>(_pinRows);
                rows.AddRange(_recentRows);
                if (_style == "Classic") rows.AddRange(_allRows);
                for (int i = 0; i < rows.Count; i++) rows[i].Index = i;
                _rows = rows;
                if (_scroll.Content != _home) _scroll.Content = _home;
            }
            else
            {
                _findList.Children.Clear();
                _findRows.Clear();
                var hits = new List<KeyValuePair<int, AppEntry>>();
                foreach (AppEntry a in AppCatalog.Get())
                {
                    int s = Score(a.Name, q);
                    if (s > 0) hits.Add(new KeyValuePair<int, AppEntry>(s, a));
                }
                hits.Sort(delegate (KeyValuePair<int, AppEntry> x, KeyValuePair<int, AppEntry> y)
                {
                    if (x.Key != y.Key) return y.Key.CompareTo(x.Key);
                    return string.Compare(x.Value.Name, y.Value.Name, StringComparison.CurrentCultureIgnoreCase);
                });
                for (int i = 0; i < hits.Count && i < 80; i++) AddRow(hits[i].Value, q, _findList, _findRows);
                if (_findRows.Count == 0)
                    _findList.Children.Add(new TextBlock { Text = "No apps match \"" + q + "\".", Foreground = _t.FgDim, Margin = new Thickness(10, 10, 10, 0), TextWrapping = TextWrapping.Wrap });
                _rows = _findRows;
                if (_scroll.Content != _findList) _scroll.Content = _findList;
                if (_rows.Count > 0) Select(0, false);
            }
            if (_style == "Panes") HighlightCats();
            // rows whose icon the background loader hasn't got to yet
            foreach (AppRow r in _rows) if (r.NeedsIcon) _iconQueue.Enqueue(r); else r.LoadIcon();
            PumpIcons();
        }

        /// <summary>
        /// The full A-Z list (only when the programs changed). With "Show folders" on, the programs in a folder are
        /// behind a folder row (in the A-Z with the rest) that opens it.
        /// </summary>
        void BuildAll()
        {
            _allDirty = false;
            _allList.Children.Clear();
            _allRows.Clear();
            var items = new List<KeyValuePair<AppEntry, RowLook>>();
            var folders = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (AppEntry a in AppCatalog.Get())
            {
                if (!_cfg.StartFolders || a.Folder.Length == 0) { items.Add(new KeyValuePair<AppEntry, RowLook>(a, null)); continue; }
                string top = a.Folder.Split('\\')[0];
                if (folders.Add(top)) items.Add(FolderItem(top, top));
            }
            if (folders.Count > 0)
                items.Sort(delegate (KeyValuePair<AppEntry, RowLook> x, KeyValuePair<AppEntry, RowLook> y)
                {
                    return string.Compare(x.Key.Name, y.Key.Name, StringComparison.CurrentCultureIgnoreCase);
                });
            string current = null;
            foreach (var it in items)
            {
                AppEntry a = it.Key;
                if (a.Letter != current)
                {
                    current = a.Letter;
                    _allList.Children.Add(new TextBlock
                    {
                        Text = current,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = _t.SecondaryAccent,     // letter headings in the Secondary Accent colour
                        Margin = new Thickness(10, 8, 0, 2)
                    });
                }
                AddRow(a, null, _allList, _allRows, it.Value);
            }
            if (_allRows.Count == 0)
                _allList.Children.Add(new TextBlock { Text = "No programs found.", Foreground = _t.FgDim, Margin = new Thickness(10, 10, 10, 0), TextWrapping = TextWrapping.Wrap });
        }

        /// <summary>A row for a Start Menu folder (path under Programs) that opens it in the menu.</summary>
        KeyValuePair<AppEntry, RowLook> FolderItem(string path, string name)
        {
            var a = new AppEntry { Name = name, Letter = AppCatalog.LetterOf(name), IconTried = true };
            var look = new RowLook
            {
                Glyph = IconUtil.GlyphFolder, GlyphBrush = _t.MainAccent, Chevron = true,
                Open = delegate { OpenPlace(new Place { Title = name, Folder = path }); }
            };
            return new KeyValuePair<AppEntry, RowLook>(a, look);
        }

        TextBlock Heading(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, _home.Children.Count > 0 ? 14 : 4, 0, 4) };
        }

        /// <summary>A heading with a small button on the right (Grid's "Pinned" + "All programs ›").</summary>
        Grid HeadingWithButton(string text, string button, Action click)
        {
            var g = new Grid { Margin = new Thickness(10, _home.Children.Count > 0 ? 14 : 4, 4, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = button, VerticalAlignment = VerticalAlignment.Center });
            var chev = IconUtil.Glyph(LauncherModule.GlyphChevronRight, _t.FgDim, 10);
            chev.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(chev);
            var b = new Border { Background = _t.Hover, CornerRadius = new CornerRadius(_inner), Padding = new Thickness(10, 3, 8, 3), Cursor = Cursors.Hand, Child = line };
            b.MouseEnter += delegate { b.Background = _t.ActiveBg; };
            b.MouseLeave += delegate { b.Background = _t.Hover; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Grid.SetColumn(b, 1);
            g.Children.Add(b);
            return g;
        }

        TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, Foreground = _t.FgDim, Margin = new Thickness(10, 2, 10, 4), TextWrapping = TextWrapping.Wrap };
        }

        // ---- Pinned and Recent files ---------------------------------------------------------------------
        /// <summary>The Pinned tiles and the recent files on the home list, fresh each time the menu opens.</summary>
        void BuildSections()
        {
            _pinPanel.Children.Clear();
            _pinRows.Clear();
            _recentPanel.Children.Clear();
            _recentRows.Clear();
            if (_style == "Panes") return;      // its categories build their own lists
            if (ShowPinned) AddPinned(_pinPanel, _pinRows, true);
            if (_cfg.StartRecent) AddRecent(_recentPanel, _recentRows);
        }

        /// <summary>Every pinned shortcut and group - as tiles (the home list) or rows (Two panes).</summary>
        void AddPinned(Panel panel, List<AppRow> rows, bool tile)
        {
            int before = rows.Count;
            foreach (LauncherEntry en in _cfg.Entries) AddEntry(en, panel, rows, tile, true);
            if (rows.Count > before) return;
            TextBlock h = Hint("Nothing pinned yet - right-click a program to pin it, or add shortcuts and groups in the Start menu's settings (Edit pinned…).");
            if (panel is WrapPanel) h.Width = panel.Width > 0 ? panel.Width - 20 : 330;      // a wrap panel would give it one endless line
            panel.Children.Add(h);
        }

        void AddRecent(Panel panel, List<AppRow> rows)
        {
            int before = rows.Count;
            foreach (RecentFile f in RecentFiles.Get(_cfg.StartRecentCount))
            {
                string link = f.Link;
                var look = new RowLook
                {
                    Glyph = IconUtil.GlyphFile, GlyphBrush = _t.FgDim,
                    Note = f.When.Date == DateTime.Today ? TimeText.Clock(f.When) : TimeText.Day(f.When),
                    Tip = f.Name + "\n" + TimeText.Full(f.When),
                    Open = delegate { OpenTarget(link); }
                };
                AddRow(new AppEntry { Name = f.Name, Target = link }, null, panel, rows, look);
            }
            if (rows.Count == before) panel.Children.Add(Hint("No recent files."));
        }

        /// <summary>A pinned shortcut (starts it) or group (opens it in the menu) - a tile in the Pinned section, a row inside a group.
        /// topLevel: one of the pins themselves (right-click unpins it), not a shortcut inside a group.</summary>
        void AddEntry(LauncherEntry en, Panel panel, List<AppRow> rows, bool tile, bool topLevel)
        {
            LauncherEntry e = en;
            var a = new AppEntry { Name = string.IsNullOrEmpty(e.Name) ? e.Path : e.Name, Target = e.Path, IconTried = true };
            var look = new RowLook { Tile = tile };
            if (e.IsGroup)
            {
                if (string.IsNullOrEmpty(a.Name)) a.Name = "Group";
                a.Icon = IconUtil.FromIconFile(e.IconPath);
                bool builtIn = IconUtil.GroupGlyph(e.IconGlyph) != null;
                look.Glyph = IconUtil.GroupGlyph(e.IconGlyph) ?? IconUtil.GlyphFolder;
                look.GlyphBrush = !builtIn || e.IconAccent ? _t.MainAccent : _t.Fg;
                look.Chevron = !tile;
                look.Tip = a.Name + " (" + e.Children.Count + ")";
                string title = a.Name;
                look.Open = delegate { OpenPlace(new Place { Title = title, Group = e }); };
            }
            else
            {
                a.Icon = LauncherModule.EntryIcon(e);
                look.Glyph = LauncherModule.EntryGlyph(e);
                look.GlyphBrush = _t.Fg;
                look.Tip = string.IsNullOrEmpty(e.Name) || e.Name == e.Path ? e.Path : e.Name + "\n" + e.Path;
                look.Open = delegate { _close(); LauncherModule.Run(e); };
            }
            AddRow(a, null, panel, rows, look);
            if (topLevel)
            {
                AppRow row = rows[rows.Count - 1];
                row.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs ev) { ev.Handled = true; PinMenu(row, null, null, e); };
            }
        }

        /// <summary>Right-click: Pin to Start / Unpin from Start (pinned = the pin itself, else found by the program's target).</summary>
        void PinMenu(AppRow row, string name, string target, LauncherEntry pinned)
        {
            StartPins.ShowMenu(row, _t, _cfg, name, target, pinned, _save, Refresh);
        }

        // ---- groups, folders and categories opened in the menu ----------------------------------------------
        void OpenPlace(Place p)
        {
            p.ReturnOffset = _scroll.VerticalOffset;
            _trail.Add(p);
            BuildList();
            _scroll.ScrollToTop();
        }

        bool CanGoBack { get { return _trail.Count > 0 && !_trail[_trail.Count - 1].NoBack; } }

        void GoBack()
        {
            if (!CanGoBack) return;
            Place p = _trail[_trail.Count - 1];
            _trail.RemoveAt(_trail.Count - 1);
            BuildList();
            _scroll.UpdateLayout();
            _scroll.ScrollToVerticalOffset(p.ReturnOffset);
        }

        /// <summary>A Back row (with the place's name) - or, for a Two panes category, its name as a heading - then what's inside.
        /// Returns the rows in order, for the arrow keys.</summary>
        List<AppRow> BuildPlace(Place p)
        {
            _placeList.Children.Clear();
            _placeRows.Clear();
            if (!p.NoBack)
            {
                var back = new RowLook { Glyph = LauncherModule.GlyphChevronLeft, GlyphBrush = _t.FgDim, Tip = "Back (Backspace)", Open = GoBack };
                AddRow(new AppEntry { Name = p.Title, IconTried = true }, null, _placeList, _placeRows, back);
                TextElement.SetFontWeight(_placeRows[0], FontWeights.SemiBold);
                _placeList.Children.Add(new Border { Height = 1, Background = _t.Track, Margin = new Thickness(8, 4, 8, 4) });
            }
            else _placeList.Children.Add(new TextBlock { Text = p.Title, FontWeight = FontWeights.SemiBold, FontSize = _t.FontSize + 2, Margin = new Thickness(10, 2, 0, 6) });
            int first = _placeRows.Count;

            var rows = new List<AppRow>();
            if (p.Kind == "All")
            {
                if (_allDirty) BuildAll();
                var parent = _allList.Parent as Panel;
                if (parent != null && parent != _placeList) parent.Children.Remove(_allList);
                _placeList.Children.Add(_allList);
                rows.AddRange(_placeRows);
                rows.AddRange(_allRows);
            }
            else
            {
                if (p.Kind == "Pinned") AddPinned(_placeList, _placeRows, false);
                else if (p.Kind == "Recent") AddRecent(_placeList, _placeRows);
                else if (p.Group != null)
                {
                    if (LauncherFolders.Sync(p.Group)) _save();     // a synced folder / bookmarks: pick up new / deleted shortcuts
                    foreach (LauncherEntry c in p.Group.Children) if (c.IsGroup) AddEntry(c, _placeList, _placeRows, false, false);
                    foreach (LauncherEntry c in p.Group.Children) if (!c.IsGroup) AddEntry(c, _placeList, _placeRows, false, false);
                    if (_placeRows.Count == first) _placeList.Children.Add(Hint("Empty group"));
                }
                else
                {
                    string pre = p.Folder + "\\";
                    var subs = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
                    var here = new List<AppEntry>();
                    foreach (AppEntry a in AppCatalog.Get())
                    {
                        if (string.Equals(a.Folder, p.Folder, StringComparison.OrdinalIgnoreCase)) here.Add(a);
                        else if (a.Folder.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) subs.Add(a.Folder.Substring(pre.Length).Split('\\')[0]);
                    }
                    foreach (string s in subs)
                    {
                        var it = FolderItem(pre + s, s);
                        AddRow(it.Key, null, _placeList, _placeRows, it.Value);
                    }
                    foreach (AppEntry a in here) AddRow(a, null, _placeList, _placeRows, null);
                    if (_placeRows.Count == first) _placeList.Children.Add(Hint("Nothing in this folder."));
                }
                rows.AddRange(_placeRows);
            }
            for (int i = 0; i < rows.Count; i++) rows[i].Index = i;
            return rows;
        }

        static int Score(string name, string q)
        {
            int i = name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) return 0;
            if (i == 0) return 3;
            char prev = name[i - 1];
            return (prev == ' ' || prev == '-' || prev == '_' || prev == '.') ? 2 : 1;
        }

        void AddRow(AppEntry a, string query, Panel panel, List<AppRow> rows) { AddRow(a, query, panel, rows, null); }

        void AddRow(AppEntry a, string query, Panel panel, List<AppRow> rows, RowLook look)
        {
            var row = new AppRow(a, _t.MainAccent, query, _t.SecondaryAccent, look) { Index = rows.Count };
            row.MouseEnter += delegate { if (_rows != null && row.Index < _rows.Count && _rows[row.Index] == row) Select(row.Index, false); };
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Activate(row); };
            // a program (not a folder, group, recent file or Back): right-click pins / unpins it
            if (look == null && !string.IsNullOrEmpty(a.Target))
                row.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; PinMenu(row, a.Name, a.Target, null); };
            rows.Add(row);
            panel.Children.Add(row);
        }

        /// <summary>Click / Enter: the row's own action (a folder, group, pinned shortcut, recent file), or start the program.</summary>
        void Activate(AppRow row)
        {
            if (row.Open != null) row.Open();
            else Launch(row.Entry);
        }

        /// <summary>Loads icons a few at a time between UI events, so the menu opens at once and fills in.</summary>
        void PumpIcons()
        {
            if (_dead) return;
            int n = 0;
            while (_iconQueue.Count > 0 && n < 10)
            {
                AppRow r = _iconQueue.Dequeue();
                bool had = !r.NeedsIcon;
                r.LoadIcon();
                if (!had) n++;           // only real loads count; icons the background loader already has are free
            }
            if (_iconQueue.Count > 0) Root.Dispatcher.BeginInvoke(new Action(PumpIcons), DispatcherPriority.Background);
        }

        // ---- selection and keys --------------------------------------------------------------------------
        void Select(int i, bool scroll)
        {
            if (_rows == null) { _sel = -1; return; }
            if (_sel >= 0 && _sel < _rows.Count) { _rows[_sel].Background = Brushes.Transparent; _rows[_sel].SetActive(false); }
            _sel = (i >= 0 && i < _rows.Count) ? i : -1;
            if (_sel >= 0)
            {
                _rows[_sel].Background = _t.ActiveBg;
                _rows[_sel].SetActive(true);
                if (scroll) _rows[_sel].BringIntoView();
            }
        }

        void Move(int delta)
        {
            if (_rows == null || _rows.Count == 0) return;
            int n = _sel < 0 ? (delta > 0 ? 0 : _rows.Count - 1) : _sel + delta;
            n = Math.Max(0, Math.Min(_rows.Count - 1, n));
            Select(n, true);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.PageDown: Move(8); e.Handled = true; break;
                case Key.PageUp: Move(-8); e.Handled = true; break;
                case Key.Enter:
                    if (_rows != null && _sel >= 0 && _sel < _rows.Count) { Activate(_rows[_sel]); e.Handled = true; }
                    break;
                case Key.Back:
                case Key.Left:
                    // out of an open group / folder (with nothing typed in the search box)
                    if (_search.Text.Length == 0 && CanGoBack) { GoBack(); e.Handled = true; }
                    break;
            }
        }

        /// <summary>Typing anywhere in the menu goes to the search box.</summary>
        void OnText(object sender, TextCompositionEventArgs e)
        {
            if (_search.IsKeyboardFocused || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            _search.Focus();
            _search.Text += e.Text;
            _search.CaretIndex = _search.Text.Length;
            e.Handled = true;
        }

        // ---- actions ---------------------------------------------------------------------------------------
        void Launch(AppEntry a)
        {
            _close();
            StartPins.Open(a.Target);
        }

        void OpenTarget(string target)
        {
            _close();
            StartPins.Open(target);
        }

        // ---- small builders ----------------------------------------------------------------------------------
        Border RailButton(string glyph, string tip, Action click, bool closeFirst)
        {
            var b = new Border
            {
                Width = 38, Height = 38,
                Margin = new Thickness(4, 3, 4, 3),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = IconUtil.Glyph(glyph, _t.MainAccent, 16)
            };
            b.MouseEnter += delegate { b.Background = _t.ActiveBg; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        void AddPower(StackPanel stack, string glyph, string text, Action action)
        {
            var row = new Border { Padding = new Thickness(10, 7, 22, 7), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var g = IconUtil.Glyph(glyph, _t.MainAccent, 14);
            g.Width = 22;
            var label = new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(g);
            line.Children.Add(label);
            row.Child = line;
            row.MouseEnter += delegate { row.Background = _t.Hover; label.Foreground = _t.MainAccent; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; label.ClearValue(TextBlock.ForegroundProperty); };
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _close();
                action();
            };
            stack.Children.Add(row);
        }

        static bool IsInside(DependencyObject d, DependencyObject ancestor)
        {
            while (d != null)
            {
                if (d == ancestor) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }

    /// <summary>
    /// Loads the Start menu's program icons on a background thread (after vShell starts, and again when the list changes), so the
    /// menu opens with its icons already there.
    /// </summary>
    static class StartIcons
    {
        static int _running, _again;
        static bool _hooked;

        public static void Preload()
        {
            if (!_hooked) { _hooked = true; AppCatalog.Changed += delegate { Preload(); }; }
            if (System.Threading.Interlocked.Exchange(ref _running, 1) == 1) { _again = 1; return; }
            var th = new System.Threading.Thread(delegate ()
            {
                try
                {
                    do
                    {
                        _again = 0;
                        foreach (AppEntry e in AppCatalog.Get())
                        {
                            if (e.Icon != null || e.IconTried) continue;
                            try { e.Icon = e.IsStore ? IconUtil.FromParsingName(e.Target, 48) : IconUtil.FromPath(e.Target); }
                            catch (Exception ex) { Log.Write("Icon for '" + e.Name + "': " + ex.Message); }
                            e.IconTried = true;
                        }
                    } while (_again == 1);
                }
                catch (Exception ex) { Log.Write("Start menu icons: " + ex.Message); }
                finally { System.Threading.Interlocked.Exchange(ref _running, 0); }
            });
            th.IsBackground = true;
            th.Priority = System.Threading.ThreadPriority.BelowNormal;
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.Start();
        }
    }
}
