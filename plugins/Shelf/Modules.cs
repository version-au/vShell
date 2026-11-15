using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VShellShelf
{
    /// <summary>The Shelf plugin's own settings (Settings > Plugins > Shelf), in its integration bag.</summary>
    public sealed class ShelfSettings
    {
        readonly SettingsBag _b;
        ShelfSettings(SettingsBag b) { _b = b; }
        public static ShelfSettings Of(BarSettings s) { return new ShelfSettings(s.Integration("shelf")); }

        /// <summary>The settings in use (set at load and on every settings change).</summary>
        public static ShelfSettings Current;

        /// <summary>"Add to shelf" in Explorer's right-click menu.</summary>
        public bool ExplorerMenu { get { return _b.Get("ExplorerMenu", true); } set { _b.Set("ExplorerMenu", value); } }
        /// <summary>The shelf Explorer's "Add to shelf" and the bar button add to (its id; "" = the first that isn't a Pocket).</summary>
        public string AddTo { get { return _b.Get("AddTo", ""); } set { _b.Set("AddTo", value); } }
        /// <summary>What dragging out does with no key held: Copies, Moves or Explorer (Explorer decides: a move on the same drive).</summary>
        public string DragOut
        {
            get { string v = _b.Get("DragOut", "Copies"); return v == "Moves" || v == "Explorer" ? v : "Copies"; }
            set { _b.Set("DragOut", value); }
        }
        public bool ClipTab { get { return _b.Get("ClipTab", true); } set { _b.Set("ClipTab", value); } }
        /// <summary>A bar's popup closes after something is copied from the Clipboard tab.</summary>
        public bool CloseAfterCopy { get { return _b.Get("CloseAfterCopy", true); } set { _b.Set("CloseAfterCopy", value); } }

        // ---- the edge tabs ----
        public bool EdgeOn { get { return _b.Get("EdgeOn", false); } set { _b.Set("EdgeOn", value); } }
        /// <summary>The monitor's device name (\\.\DISPLAY2); "" = the main monitor.</summary>
        public string EdgeMonitor { get { return _b.Get("EdgeMonitor", ""); } set { _b.Set("EdgeMonitor", value); } }
        /// <summary>Top, Bottom, Left or Right.</summary>
        public string EdgeSide
        {
            get { string v = _b.Get("EdgeSide", "Right"); return v == "Top" || v == "Bottom" || v == "Left" ? v : "Right"; }
            set { _b.Set("EdgeSide", value); }
        }
        /// <summary>Where along the edge: Start (top / left), Centre or End (bottom / right).</summary>
        public string EdgeAlign
        {
            get { string v = _b.Get("EdgeAlign", "Centre"); return v == "Start" || v == "End" ? v : "Centre"; }
            set { _b.Set("EdgeAlign", value); }
        }
        /// <summary>"Index tabs" or "Ribbon".</summary>
        public string EdgeStyle
        {
            get { return _b.Get("EdgeStyle", "Index tabs") == "Ribbon" ? "Ribbon" : "Index tabs"; }
            set { _b.Set("EdgeStyle", value); }
        }
        /// <summary>Slide out as soon as a drag starts (anywhere), not only when it reaches the edge.</summary>
        public bool EdgeOnDrag { get { return _b.Get("EdgeOnDrag", true); } set { _b.Set("EdgeOnDrag", value); } }
        /// <summary>Slide out when the pointer rests on the strip.</summary>
        public bool EdgeOnHover { get { return _b.Get("EdgeOnHover", true); } set { _b.Set("EdgeOnHover", value); } }
        /// <summary>A shelf opened from the tabs stays open until it's closed (else a click elsewhere closes it).</summary>
        public bool EdgeStayOpen { get { return _b.Get("EdgeStayOpen", false); } set { _b.Set("EdgeStayOpen", value); } }
        /// <summary>How long a drag rests on a tab before that shelf opens, in ms; 0 = never (drop on the tab only).</summary>
        public int EdgeSpringMs
        {
            get { int v = _b.Get("EdgeSpringMs", 650); return v <= 0 ? 0 : Math.Max(200, Math.Min(10000, v)); }
            set { _b.Set("EdgeSpringMs", value); }
        }
    }

    /// <summary>A module's own settings (its ModuleConfig's bag).</summary>
    public sealed class ShelfModuleView
    {
        readonly SettingsBag _b;
        ShelfModuleView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static ShelfModuleView Of(ModuleConfig cfg) { return new ShelfModuleView(cfg); }

        /// <summary>"" = every shelf, with tabs; a shelf's id; or "clipboard".</summary>
        public string Show { get { return _b.Get("Show", ""); } set { _b.Set("Show", value); } }
        /// <summary>How many rows tall the list is.</summary>
        public int Rows { get { return Math.Max(2, Math.Min(30, _b.Get("Rows", 7))); } set { _b.Set("Rows", value); } }
        /// <summary>On a bar: the number of things on the shelves next to the glyph.</summary>
        public bool ShowCount { get { return _b.Get("ShowCount", true); } set { _b.Set("ShowCount", value); } }
    }

    /// <summary>On a bar: the shelf glyph and how many things are on it. Click: the shelves in a popup. Drop on it: onto a shelf; hold a drag over it: the popup opens.</summary>
    public sealed class ShelfBarModule : Border
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly TextBlock _title, _glyph, _text;
        PopupWindow _popup;
        ShelfView _view;
        DispatcherTimer _spring, _dragEnd;
        bool _subscribed, _hot;

        public ShelfBarModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = new Thickness(8, 0, 8, 0);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            // as tall as the bar, so a drop (or a click) anywhere above or below the icon counts
            Height = Math.Max(16, host.HostHeight - 2);
            AllowDrop = true;
            double fs = Math.Max(11, _t.FontSize);
            // the module's Title (Settings), in front - as the other modules show theirs
            _title = new TextBlock { FontFamily = _t.Font, FontSize = fs, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = _t.FgDim };
            _glyph = new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = Math.Round(fs * 1.3), VerticalAlignment = VerticalAlignment.Center, Foreground = _t.Fg };
            _text = new TextBlock { FontFamily = _t.Font, FontSize = fs, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = _t.Fg };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(_title);
            sp.Children.Add(_glyph);
            sp.Children.Add(_text);
            Child = sp;
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Toggle(false); };

            DragEnter += delegate (object o, DragEventArgs e)
            {
                if (!ShelfStore.CanTake(e.Data) && !e.Data.GetDataPresent(ShelfStore.ItemsFormat)) return;
                Hot(true);
                if (_spring == null)
                {
                    _spring = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    _spring.Tick += delegate { _spring.Stop(); if (_popup == null || !_popup.IsOpen) Toggle(true); };
                }
                _spring.Stop();
                _spring.Start();
            };
            DragLeave += delegate { if (_spring != null) _spring.Stop(); Hot(false); };
            DragOver += delegate (object o, DragEventArgs e)
            {
                e.Effects = !ShelfStore.CanTake(e.Data) || e.Data.GetDataPresent(ShelfStore.ItemsFormat) ? DragDropEffects.None :
                            (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link :
                            (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += delegate (object o, DragEventArgs e)
            {
                e.Handled = true;
                if (_spring != null) _spring.Stop();
                Hot(false);
                if (e.Data.GetDataPresent(ShelfStore.ItemsFormat)) { e.Effects = DragDropEffects.None; return; }
                Shelf s = ShelfStore.Default(ShelfSettings.Current != null ? ShelfSettings.Current.AddTo : null);
                int n = ShelfStore.AddFrom(s, e.Data);
                e.Effects = n > 0 ? ((e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy) : DragDropEffects.None;
            };

            Loaded += delegate { Subscribe(true); Refresh(); };
            Unloaded += delegate { Subscribe(false); };
            Refresh();
        }

        void Subscribe(bool on)
        {
            if (on == _subscribed) return;
            _subscribed = on;
            if (on) ShelfStore.Changed += Refresh; else ShelfStore.Changed -= Refresh;
        }

        void Hot(bool on)
        {
            if (on == _hot) return;
            _hot = on;
            Background = on ? (Brush)Theme.Faded(_t.MainAccent, 30) : Brushes.Transparent;
        }

        void Refresh()
        {
            string title = (_cfg.Title ?? "").Trim();
            _title.Text = title;
            _title.Visibility = title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            int n = ShelfStore.TotalCount;
            bool count = ShelfModuleView.Of(_cfg).ShowCount && n > 0;
            _text.Text = count ? n.ToString() : "";
            _text.Visibility = count ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = "Shelf - " + (n == 0 ? "nothing on it yet" : n + " thing" + (n == 1 ? "" : "s") + " on your shelves") +
                      "\nClick to open it · drop files, text or pictures here to keep them";
        }

        /// <summary>Open or close the popup. During a drag (it sprang open) it stays open until the mouse button comes up.</summary>
        void Toggle(bool dragging)
        {
            if (_popup != null && _popup.IsOpen) { if (!dragging) { _popup.CloseSafe(); _popup = null; } return; }
            ShelfModuleView v = ShelfModuleView.Of(_cfg);
            _view = new ShelfView(_t, Math.Max(12, _t.FontSize), v.Show, v.Rows, false);
            var box = new Border { Padding = new Thickness(10, 10, 10, 8), Child = _view };
            PopupWindow popup = new PopupWindow(_mgr, _host, this, _t, box);
            _popup = popup;
            _view.Hold = delegate (bool on) { if (popup.IsOpen) popup.Pinned = on; };
            _view.Used = delegate { if (popup.IsOpen) popup.CloseSafe(); };
            if (_mgr.Settings.WidgetStyle != null) popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(popup);
            if (dragging)
            {
                // the button is still down (the drag): a popup closes on a press outside it, so hold it open until the drag is over
                popup.Pinned = true;
                if (_dragEnd == null)
                {
                    _dragEnd = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    _dragEnd.Tick += delegate
                    {
                        if ((GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0) return;
                        _dragEnd.Stop();
                        if (_popup != null && _popup.IsOpen) _popup.Pinned = false;
                    };
                }
                _dragEnd.Start();
            }
            else _view.Focus();
        }
    }

    /// <summary>In a widget: the shelves (or one shelf, or the Clipboard tab) as a list of the height set.</summary>
    public sealed class ShelfWidgetModule : Border, IMultiRow
    {
        public int RowCount { get { return 0; } }       // it sets its own height

        public ShelfWidgetModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            Theme t = host.Theme.ForModule(cfg);
            ShelfModuleView v = ShelfModuleView.Of(cfg);
            double fs = cfg.ShownFontSize > 0 ? cfg.ShownFontSize : t.FontSize;
            Background = Brushes.Transparent;
            Child = new ShelfView(t, fs, v.Show, v.Rows, true) { MinWidth = 220 };
        }
    }
}
