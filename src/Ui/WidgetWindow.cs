using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A freely movable box on the desktop holding one or more modules. Drag it by any empty part; the position is remembered.
    /// Theme colours are shared with the bars; font size, corner radius, opacity, padding etc. are the widget's own.
    /// </summary>
    public sealed partial class WidgetWindow : DesktopWindow, IModuleHost
    {
        public readonly WidgetSettings Cfg;
        public Theme Theme { get; private set; }

        readonly Border _box = new Border();
        readonly Border _outer = new Border();
        // inside the box: the header row, the tab strip, then the modules (in a scroller when the height is fixed)
        readonly DockPanel _layout = new DockPanel { LastChildFill = true };
        readonly Border _header = new Border { Background = Brushes.Transparent };
        readonly TextBlock _headIcon = new TextBlock(), _headTitle = new TextBlock(), _headChevron = new TextBlock();
        readonly Border _tabHost = new Border();
        readonly WrapPanel _tabStrip = new WrapPanel();
        readonly Border _bodyHost = new Border();
        Panel _body = new StackPanel();
        // icon animations (an icon-only Launcher that asks for them), null otherwise; the glow halos go on a canvas under everything in the box
        DockFx _fx;
        readonly Canvas _fxLayer = new Canvas { IsHitTestVisible = false };
        /// <summary>The icon animations of this widget (null when no module wants them).</summary>
        public DockFx Fx { get { return _fx; } }
        readonly List<Border> _tabButtons = new List<Border>();
        readonly List<Border> _tabPages = new List<Border>();
        bool _tabsOn, _headerDown;
        double _lastExpandedWidth;
        int _expandedHeightPx;

        /// <summary>
        /// Collapsed: the box's height when it was last open (device pixels), so snapping places it as if open - it rolls up under its
        /// header with the top staying put. 0 = not collapsed, or not seen open yet (started collapsed).
        /// </summary>
        public int ExpandedHeightPx { get { return Cfg.IsCollapsed ? _expandedHeightPx : 0; } }
        // the accent stripe along the top / left edge (over the box, clipped to its corners)
        readonly Border _edge = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        // density: Compact = about 60 % of the padding, spacing and row height
        double _dens = 1, _cardPadV;
        int _rowH = 40;
        readonly Grid _stage = new Grid();

        readonly Grid _shadowHost = new Grid { IsHitTestVisible = false };
        readonly Border _shadow = new Border { Background = Brushes.Black };
        double _shadowPad;
        readonly Border _ring = new Border { Background = null };   // the outline: sits outside the box, in the transparent margin
        readonly Grid _boxHost = new Grid();                        // the blurred wallpaper (background blur) under the box
        readonly Glass _glass;
        readonly Border _sheen = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };     // the glass edge
        // Ctrl+click: picked for align / distribute (a dashed accent frame over the widget)
        readonly System.Windows.Shapes.Rectangle _pick = new System.Windows.Shapes.Rectangle
        {
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 }, Margin = new Thickness(2)
        };
        // dragging a locked widget: an accent outline and "Locked · right-click to unlock" for a moment (0.38.224, after desk-widgets)
        readonly System.Windows.Shapes.Rectangle _lockFlash = new System.Windows.Shapes.Rectangle
        {
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, StrokeThickness = 2, Margin = new Thickness(1)
        };
        readonly Border _lockHint = new Border
        {
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 6, 10), Padding = new Thickness(10, 3, 10, 4), CornerRadius = new CornerRadius(10)
        };
        readonly TextBlock _lockHintText = new TextBlock { Text = "Locked · right-click to unlock", TextTrimming = TextTrimming.CharacterEllipsis };
        Point? _lockedPress;
        int _lockFlashes;
        readonly List<MetricModule> _metricMods = new List<MetricModule>();
        readonly List<SensorModule> _sensorMods = new List<SensorModule>();
        readonly List<StartMenuModule> _startMods = new List<StartMenuModule>();
        readonly List<InfoModule> _infoMods = new List<InfoModule>();
        readonly List<TilesModule> _tileMods = new List<TilesModule>();
        readonly List<NotesModule> _noteMods = new List<NotesModule>();
        ITypingModule _typingOwner;
        // module rows and how many row heights each is (0 = sizes itself): resizing changes their height live
        readonly List<KeyValuePair<Border, int>> _rows = new List<KeyValuePair<Border, int>>();
        // fixed height: the modules scroll inside the box
        readonly ScrollViewer _scroll = new ScrollViewer
        {
            // always keep the (slim) scrollbar's room, so the widget doesn't widen when its modules grow enough to scroll
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Background = Brushes.Transparent
        };
        // resize mode: a layer over the widget with a dashed frame, handles and the size in the middle
        readonly Grid _resizeLayer = new Grid { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Focusable = true };
        readonly System.Windows.Shapes.Rectangle _resizeFrame = new System.Windows.Shapes.Rectangle
        {
            IsHitTestVisible = false, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, Margin = new Thickness(1)
        };
        readonly TextBlock _resizeInfo = new TextBlock { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        readonly Border _resizeCard = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6) };
        readonly List<Border> _handles = new List<Border>();
        bool _resizing, _sizing, _fixedH;
        int _zone;                  // 1 left, 2 right, 4 top, 8 bottom
        POINT _start;
        RECT _startVis;
        double _startH, _scale = 1;
        int _startMH;
        int _origW, _origH, _origMH, _origX, _origY;
        string _origMon, _origKey;
        SnapLink _origLinkX, _origLinkY;

        public List<StartMenuModule> StartMenus { get { return _startMods; } }
        public List<NotesModule> NoteModules { get { return _noteMods; } }
        readonly List<RunBoxModule> _runMods = new List<RunBoxModule>();
        public List<RunBoxModule> RunBoxes { get { return _runMods; } }

        // ---- typing in a notes module ----------------------------------------------------------------
        /// <summary>A notes / run box module wants the keyboard: the widget comes in front and is activated (the previous owner stops typing).</summary>
        [PluginApi] public void BeginTyping(ITypingModule m)
        {
            if (_typingOwner != null && _typingOwner != m) { var o = _typingOwner; _typingOwner = null; o.StopTyping(); }
            _typingOwner = m;
            StartKeyboard();
        }

        /// <summary>That module is done: back to the desktop, no keyboard.</summary>
        [PluginApi] public void EndTyping(ITypingModule m)
        {
            if (_typingOwner != m) return;
            _typingOwner = null;
            StopKeyboard();
            QueueRefit();       // the fit waited while typing (a note's size may have changed)
        }

        /// <summary>A module passes a drag on to the widget (a note's text area: a press that moves drags, a click edits).</summary>
        public void DragFromModule()
        {
            if (Cfg.Locked || _resizing || Dragging) return;
            BeginManualDrag();
            _box.CaptureMouse();
        }

        public void ExpandIfCollapsed() { if (Cfg.IsCollapsed) ToggleCollapsed(); }

        public WidgetWindow(BarManager mgr, WidgetSettings cfg) : base(mgr, "vShell widget")
        {
            Cfg = cfg;
            SizeToContent = SizeToContent.WidthAndHeight;

            BuildHeader();
            DockPanel.SetDock(_header, Dock.Top);
            DockPanel.SetDock(_tabHost, Dock.Top);
            _tabHost.Child = _tabStrip;
            _layout.Children.Add(_header);
            _layout.Children.Add(_tabHost);
            _layout.Children.Add(_bodyHost);
            var boxContent = new Grid();
            boxContent.Children.Add(_fxLayer);
            boxContent.Children.Add(_layout);
            _box.Child = boxContent;
            _box.Background = Brushes.Transparent;
            _box.MouseMove += delegate (object s4, MouseEventArgs e4) { if (_fx != null) _fx.MouseMove(e4.GetPosition(_box)); };
            _box.MouseLeave += delegate { if (_fx != null) _fx.MouseLeave(); };
            Closed += delegate { if (_fx != null) _fx.Stop(); };
            LoadScrollStyle();
            _glass = new Glass(mgr);
            _boxHost.Children.Add(_glass);
            _boxHost.Children.Add(_box);
            _boxHost.Children.Add(_sheen);
            _boxHost.Children.Add(_edge);
            _boxHost.SizeChanged += delegate { UpdateEdgeClip(); };
            _ring.Child = _boxHost;
            _shadowHost.Children.Add(_shadow);
            _stage.Children.Add(_shadowHost);        // drawn first (behind), clipped so it only shows OUTSIDE the box
            _stage.Children.Add(_ring);
            _stage.Children.Add(_pick);
            _lockHint.Child = _lockHintText;
            _stage.Children.Add(_lockFlash);
            _stage.Children.Add(_lockHint);
            BuildResizeLayer();
            _stage.Children.Add(_resizeLayer);
            PreviewKeyDown += delegate (object s3, KeyEventArgs e3)
            {
                if (!_resizing) return;
                if (e3.Key == Key.Escape) { e3.Handled = true; EndResize(false); }
                else if (e3.Key == Key.Enter) { e3.Handled = true; EndResize(true); }
            };
            _stage.SizeChanged += delegate { UpdateShadowClip(); };
            _outer.Child = _stage;                   // transparent margin around the box leaves room for its shadow
            _slideHost.Child = _outer;               // sliding away at the screen edge moves / fades this, not the widget itself
            Content = _slideHost;

            // Ctrl+click anywhere on the widget (modules included): pick / unpick it for align and distribute
            _box.PreviewMouseLeftButtonDown += delegate (object s2, MouseButtonEventArgs e2)
            {
                _headerDown = false;            // the header sets it again when the press is on it
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                e2.Handled = true;
                WidgetSnap.Toggle(this);
            };
            _box.PreviewMouseLeftButtonUp += delegate (object s2, MouseButtonEventArgs e2)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) e2.Handled = true;     // no module click with Ctrl
            };
            _box.MouseLeftButtonDown += Box_MouseLeftButtonDown;
            _box.MouseMove += Box_MouseMove;
            _box.MouseLeftButtonUp += Box_MouseLeftButtonUp;
            _box.LostMouseCapture += delegate { FinishDrag(); };
            _box.MouseRightButtonUp += Box_MouseRightButtonUp;
            // clicking somewhere else while typing in a note: finish typing
            Deactivated += delegate
            {
                if (_typingOwner != null) { var o = _typingOwner; o.StopTyping(); }
                else if (HasKeyboard) StopKeyboard();
            };
            // a new size moves the widgets snapped to this one (and this one, when it is snapped by its right / bottom side)
            _box.SizeChanged += delegate { if (!_resizing) Mgr.QueueWidgetRelayout(); };
        }

        public bool NeedsGpu
        {
            get
            {
                if (!Cfg.Enabled) return false;
                foreach (var m in Cfg.Modules) if (ModuleTypes.UsesGpu(m)) return true;
                return false;
            }
        }

        protected override void OnHandleReady()
        {
            ApplySettings();
            ApplyPosition();
        }

        // =================================================================
        // IModuleHost
        // =================================================================
        public double HostHeight { get { return _rowH; } }
        public bool FullWidth { get { return false; } }
        public bool IsWidget { get { return true; } }
        public IntPtr Handle { get { return Hwnd; } }
        public IntPtr MonitorHandle { get { return Native.MonitorFromWindow(Hwnd, Native.MONITOR_DEFAULTTONEAREST); } }
        /// <summary>
        /// The window sizes itself to its content. A module that hid itself (printer off, nothing playing …) takes its row with it, and a
        /// widget whose modules are all hidden hides itself - it comes back when one shows again.
        /// </summary>
        public void ContentChanged()
        {
            if (_syncQueued) return;
            _syncQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(SyncHidden));
        }
        bool _syncQueued, _selfHidden;

        void SyncHidden()
        {
            _syncQueued = false;
            if (_rows.Count == 0) return;
            bool all = true;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                var el = r.Key.Child as UIElement;
                // a module whose number of rows changed (a plugin's tiles one per row, with servers added): its row follows
                var multi = el as IMultiRow;
                if (multi != null && multi.RowCount != r.Value)
                {
                    int n = multi.RowCount;
                    if (n > 0) { r.Key.Height = _rowH * n + 2 * _cardPadV; r.Key.MinHeight = 0; }
                    else { r.Key.Height = double.NaN; r.Key.MinHeight = _rowH + 2 * _cardPadV; }
                    var fe = el as FrameworkElement;
                    if (fe != null) fe.VerticalAlignment = n == 0 ? VerticalAlignment.Top : VerticalAlignment.Center;
                    _rows[i] = r = new KeyValuePair<Border, int>(r.Key, n);
                }
                bool gone = el != null && el.Visibility == Visibility.Collapsed;
                if (!_tabsOn) r.Key.Visibility = gone ? Visibility.Collapsed : Visibility.Visible;
                if (!gone) all = false;
            }
            bool hide = all && !_resizing && !Dragging;
            if (hide == _selfHidden) return;
            _selfHidden = hide;
            Log.Write("Widget '" + Cfg.Name + "': " + (hide ? "hidden - its modules have nothing to show" : "shown again"));
            ShowOrHide();
        }

        /// <summary>Its monitor isn't connected (turned off or unplugged): hidden until the monitor is back.</summary>
        public bool Away { get { return _away; } }
        bool _away;

        void SetAway(bool away)
        {
            if (away == _away) return;
            _away = away;
            Log.Write("Widget '" + Cfg.Name + "': " + (away ? "its monitor isn't connected - hidden until it's back" : "its monitor is back - shown again"));
            ShowOrHide();
        }

        /// <summary>Hidden while its modules have nothing to show or its monitor is away.</summary>
        void ShowOrHide()
        {
            bool hide = _selfHidden || _away;
            Visibility want = hide ? Visibility.Hidden : Visibility.Visible;
            if (Visibility == want) return;
            Visibility = want;
            if (!hide) { PlaceZ(); Mgr.QueueWidgetRelayout(); }
        }
        public void OpenSettings() { Mgr.OpenWidgetSettings(Cfg); }

        /// <summary>Popups open below the widget when it is in the upper half of its monitor, above it otherwise.</summary>
        public bool PopupsBelow
        {
            get
            {
                RECT r;
                if (Hwnd == IntPtr.Zero || !Native.GetWindowRect(Hwnd, out r)) return true;
                int cy = (r.Top + r.Bottom) / 2;
                MonitorInfo mon = MonitorHelper.FromPoint((r.Left + r.Right) / 2, cy);
                return mon == null || cy < (mon.Bounds.Top + mon.Bounds.Bottom) / 2;
            }
        }

        // =================================================================
        // Look and modules
        // =================================================================
        public void ApplySettings()
        {
            bool compact = Cfg.Density == "Compact";
            _dens = compact ? 0.6 : 1;
            _rowH = RowPx(Cfg.ModuleHeight);
            // Build 2: the widget's own text (header, popups, messages) is S in the chosen font - the old Font size setting is gone
            var t = Theme.From(Mgr.Settings, 12, Cfg.CornerRadius);
            t.FontSize = Math.Round(BarText.Small * BarText.Factor(t.Font), 1);
            // the widget's own accent: its accent stripe, header icon and graphs (not the outline: that keeps the theme's colours)
            Theme baseTheme = t;
            if (!string.IsNullOrWhiteSpace(Cfg.AccentColor)) t = t.WithAccent(t.Resolve(Cfg.AccentColor, t.MainAccent));
            Theme = t;
            AlwaysOnTop = Cfg.AlwaysOnTop || _peekTop;      // slid out from its screen edge: in front for now

            // the theme's background (colour, gradient AND opacity - same as the bars), unless this widget has its own opacity
            _box.Background = t.MakeBg(Cfg.UseThemeOpacity ? -1 : Cfg.Opacity);
            _box.CornerRadius = new CornerRadius(Cfg.CornerRadius);
            _glass.SetCorners(_box.CornerRadius);
            _glass.Refresh(Cfg.Blur);
            double pad = Math.Round(8 * _dens);       // Build 2: padding comes from Comfortable / Compact
            bool edgeTop = Cfg.AccentEdge == "Top", edgeLeft = Cfg.AccentEdge == "Left";
            _box.Padding = new Thickness(pad + (edgeLeft ? 4 : 0), pad + (edgeTop ? 4 : 0), pad, pad);
            _cornerInset = CornerInset(Cfg.CornerRadius, pad + (edgeLeft ? 4 : 0), pad + (edgeTop ? 4 : 0));
            _box.BorderThickness = new Thickness(0);
            _edge.Visibility = edgeTop || edgeLeft ? Visibility.Visible : Visibility.Collapsed;
            _edge.Background = t.MainAccent;
            _edge.HorizontalAlignment = edgeLeft ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            _edge.VerticalAlignment = edgeTop ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            _edge.Width = edgeLeft ? 4 : double.NaN;
            _edge.Height = edgeTop ? 4 : double.NaN;
            UpdateEdgeClip();
            double bw = Cfg.ShowBorder ? Math.Max(0.5, Math.Min(12, Cfg.BorderThickness)) : 0;
            _ring.BorderBrush = Theme.Faded(baseTheme.Resolve(Cfg.BorderColor, baseTheme.MainAccent), Cfg.BorderOpacity);
            EdgeFx.GlassEdge(_sheen, Cfg.GlassEdge, _box.CornerRadius);
            _ring.BorderThickness = new Thickness(bw);
            double cr = Cfg.CornerRadius;
            _ring.CornerRadius = new CornerRadius(cr > 0 ? cr + bw : 0);
            _pick.Stroke = t.MainAccent;
            _pick.RadiusX = _pick.RadiusY = Math.Max(0, cr - 1);
            _lockFlash.Stroke = t.MainAccent;
            _lockFlash.RadiusX = _lockFlash.RadiusY = Math.Max(0, cr);
            _lockHint.Background = t.PopupBg;
            _lockHint.BorderBrush = Theme.Faded(t.MainAccent, 60);
            _lockHint.BorderThickness = new Thickness(1);
            _lockHintText.Foreground = t.Fg;
            _lockHintText.FontFamily = t.Font;
            _lockHintText.FontSize = Math.Max(9, t.FontSize - 1);
            // fixed width / height = the box (the outline sits outside it); 0 = fit the modules
            _box.Width = Cfg.Width > 0 ? Math.Max(20, Cfg.Width) : double.NaN;
            _scroll.Resources["wThumb"] = Theme.Faded(t.FgDim, 70);
            _resizeFrame.Stroke = t.MainAccent;
            _resizeCard.Background = t.PopupBg;
            _resizeCard.BorderBrush = t.MainAccent;
            _resizeCard.BorderThickness = new Thickness(1);
            _resizeInfo.Foreground = t.Fg;
            foreach (Border hb in _handles) { hb.Background = t.MainAccent; hb.BorderBrush = t.PopupBg; }
            // The shadow is a separate layer behind the widget and clipped to the outside of it: a translucent background no longer
            // shows the shadow through it (which made widgets look darker than the bars).
            _box.Effect = null;
            bool shadow = Cfg.ShowShadow;
            _shadow.Effect = shadow ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black } : null;
            _shadow.CornerRadius = _ring.CornerRadius;
            double room = Math.Max(shadow ? 10 : 0, bw);   // the outline grows into the margin
            // icon animations: transparent room around the box for the grown / lifted / bouncing icons (like a bar's), so they aren't cut off
            double fxAway, fxSide, unit = _rowH;
            foreach (ModuleConfig m in Cfg.Modules) if (m != null && m.Type == ModuleTypes.Launcher) unit = Math.Max(unit, m.IconSize + 12);
            DockFx.Room(Cfg.Modules, unit, out fxAway, out fxSide);
            room = Math.Max(room, bw + Math.Ceiling(Math.Max(fxAway, fxSide)));
            _outer.Padding = new Thickness(room - bw);
            _shadowPad = shadow ? room - bw : 0;
            _shadowHost.Margin = new Thickness(-_shadowPad);
            _shadow.Margin = new Thickness(_shadowPad);
            _shadowHost.Visibility = shadow ? Visibility.Visible : Visibility.Collapsed;
            UpdateShadowClip();
            TextElement.SetForeground(_box, t.Fg);
            TextElement.SetFontFamily(_box, t.Font);
            TextElement.SetFontSize(_box, t.FontSize);

            // header: an icon from the first module, the widget's name and the collapse button
            _header.Visibility = Cfg.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
            _headIcon.Text = IconUtil.ModuleGlyph(Cfg.Modules.Count > 0 ? Cfg.Modules[0].Type : "");
            _headIcon.Foreground = t.MainAccent;
            _headIcon.FontSize = t.FontSize * 1.15;
            _headTitle.Text = Cfg.Name;
            _headTitle.Foreground = baseTheme.Resolve(Cfg.HeaderTextColor, t.Fg);
            _headChevron.Foreground = t.FgDim;
            _headChevron.Visibility = Cfg.Collapsible ? Visibility.Visible : Visibility.Collapsed;
            _header.ToolTip = Cfg.Collapsible ? "Click to collapse / expand" : null;
            _headChevron.FontSize = Math.Max(8, t.FontSize * 0.85);
            _header.BorderBrush = Theme.Faded(t.Fg, 14);
            _tabHost.BorderBrush = Theme.Faded(t.Fg, 14);

            _wfit = 0;          // from the sizes as set; CheckFit steps down only if a fixed-size widget is too small
            _fitQuietUntil = DateTime.UtcNow.AddSeconds(1.2);
            BuildModules();
            SetScrolling(Cfg.Height > 0);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(CheckFit));
            QueueRefit();       // and again once it has settled (start-up: its real size and its data)
            if (Cfg.Width > 0 || Cfg.Height > 0)
            {
                // and once more a little later, for data that arrives after that (Home Assistant, Proxmox …)
                var late = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                late.Tick += delegate { late.Stop(); if (_wfit > 0) QueueRefit(); };
                late.Start();
            }
            ApplyCollapsed();
            UpdateStats(Mgr.Stats, false);
            PlaceZ();
            SetupAutoHide();
        }

        int RowPx(int moduleHeight) { return Math.Max(16, (int)Math.Round(moduleHeight * _dens)); }

        /// <summary>Lay out a module row again from the inside out (a tile reads the module height in its own measure).</summary>
        static void Remeasure(DependencyObject d)
        {
            var u = d as UIElement;
            if (u != null) u.InvalidateMeasure();
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) Remeasure(VisualTreeHelper.GetChild(d, i));
        }

        void BuildHeader()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _headIcon.FontFamily = IconUtil.GlyphFont;
            _headIcon.VerticalAlignment = VerticalAlignment.Center;
            _headIcon.Margin = new Thickness(0, 0, 8, 0);
            _headTitle.FontWeight = FontWeights.SemiBold;
            _headTitle.VerticalAlignment = VerticalAlignment.Center;
            _headTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            _headChevron.FontFamily = IconUtil.GlyphFont;
            _headChevron.VerticalAlignment = VerticalAlignment.Center;
            _headChevron.Margin = new Thickness(10, 0, 0, 0);
            _headChevron.Cursor = Cursors.Hand;
            Grid.SetColumn(_headTitle, 1);
            Grid.SetColumn(_headChevron, 2);
            g.Children.Add(_headIcon);
            g.Children.Add(_headTitle);
            g.Children.Add(_headChevron);
            _header.Child = g;
            // a click (no drag) on the header collapses / expands; a drag still moves the widget (see FinishDrag)
            _header.MouseLeftButtonDown += delegate { _headerDown = true; };
            _header.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!_headerDown || !Cfg.Locked) return;     // not locked: the drag code saw the click
                _headerDown = false;
                e.Handled = true;
                ToggleCollapsed();
            };
        }

        void ToggleCollapsed()
        {
            if (!Cfg.ShowHeader || !Cfg.Collapsible) return;
            if (!Cfg.Collapsed)
            {
                _lastExpandedWidth = _box.ActualWidth;
                RECT open;
                if (TryVisibleRect(out open)) _expandedHeightPx = open.Height;
            }
            Cfg.Collapsed = !Cfg.Collapsed;
            ApplyCollapsed();
            Mgr.SaveSoon();
            Mgr.WidgetsEditedOutsideSettings();
        }

        /// <summary>How far the header's icon / chevron move in so a big corner radius doesn't cut into them.</summary>
        double _cornerInset;

        /// <summary>
        /// The extra room a header needs beside a rounded corner: the corner's curve at the height where the header starts (plus 2 px), minus
        /// the padding already there. 0 for small radii.
        /// </summary>
        static double CornerInset(double radius, double padLeft, double padTop)
        {
            if (radius <= 0) return 0;
            double y = padTop + 2;                       // the icon's ink starts a little below the top of the row
            if (y >= radius) return 0;
            double dy = radius - y;
            double x = radius - Math.Sqrt(Math.Max(0, radius * radius - dy * dy)) + 2;
            return Math.Max(0, Math.Ceiling(x - padLeft));
        }

        /// <summary>Collapsed to the header, or showing its modules (keeps its width while collapsed).</summary>
        void ApplyCollapsed()
        {
            bool col = Cfg.IsCollapsed;
            if (col && _bodyHost.Visibility == Visibility.Visible && _box.ActualWidth > 0)      // collapsing now
            {
                _lastExpandedWidth = _box.ActualWidth;
                RECT open;
                if (TryVisibleRect(out open)) _expandedHeightPx = open.Height;
            }
            double gap = Math.Round(Math.Max(4, Cfg.Spacing) * _dens);
            _header.BorderThickness = new Thickness(0, 0, 0, col ? 0 : 1);
            _header.Padding = new Thickness(2 + _cornerInset, 0, 2 + _cornerInset, col ? 0 : Math.Round(6 * _dens));
            _header.Margin = new Thickness(0, 0, 0, col ? 0 : gap);
            _header.MinHeight = Math.Round(22 * _dens);
            _headChevron.Text = col ? "\uE76C" : "\uE70D";
            _bodyHost.Visibility = col ? Visibility.Collapsed : Visibility.Visible;
            _tabHost.Visibility = !col && _tabsOn ? Visibility.Visible : Visibility.Collapsed;
            _box.Height = !col && Cfg.Height > 0 ? Math.Max(20, Cfg.Height) : double.NaN;
            _box.MinWidth = col && Cfg.Width <= 0 && _lastExpandedWidth > 0 ? _lastExpandedWidth : 0;
        }

        void UpdateEdgeClip()
        {
            double w = _boxHost.ActualWidth, h = _boxHost.ActualHeight;
            if (_edge.Visibility != Visibility.Visible || w < 1 || h < 1) { _edge.Clip = null; return; }
            double r = Math.Max(0, Cfg.CornerRadius);
            var g = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            g.Freeze();
            _edge.Clip = g;
        }

        FrameworkElement Create(ModuleConfig cfg)
        {
            switch (cfg.Type)
            {
                case ModuleTypes.StartMenu:
                    {
                        var m = new StartMenuModule(Mgr, this, cfg);
                        _startMods.Add(m);
                        return m;
                    }
                case ModuleTypes.Launcher:
                    return new LauncherModule(Mgr, this, cfg);
                case ModuleTypes.Cpu:
                case ModuleTypes.Memory:
                case ModuleTypes.Gpu:
                    {
                        var m = new MetricModule(Mgr, this, cfg);
                        _metricMods.Add(m);
                        return m;
                    }
                case ModuleTypes.Clock:
                    return new ClockModule(Mgr, this, cfg);
                case ModuleTypes.Weather:
                    return new WeatherModule(Mgr, this, cfg);
                case ModuleTypes.NowPlaying:
                    return new NowPlayingModule(Mgr, this, cfg);
                case ModuleTypes.Notifications:
                    return new NotifyModule(Mgr, this, cfg);
                case ModuleTypes.Volume:
                    if (cfg.VolumeMixerInWidget) return new VolumeMixerModule(Mgr, this, cfg);
                    goto case ModuleTypes.Uptime;
                case ModuleTypes.Network:
                case ModuleTypes.Disk:
                case ModuleTypes.Battery:
                case ModuleTypes.Uptime:
                case ModuleTypes.Processes:
                case ModuleTypes.Label:
                    {
                        InfoModule m = ModuleFactory.CreateInfo(Mgr, this, cfg);
                        _infoMods.Add(m);
                        return m;
                    }
                case ModuleTypes.Services:
                    {
                        TilesModule m = ModuleFactory.CreateTiles(Mgr, this, cfg);
                        _tileMods.Add(m);
                        return m;
                    }
                case ModuleTypes.Hwinfo:
                    {
                        var m = new SensorModule(Mgr, this, cfg);
                        _sensorMods.Add(m);
                        return m;
                    }
                case ModuleTypes.RunBox:
                    {
                        var m = new RunBoxModule(Mgr, this, cfg);
                        _runMods.Add(m);
                        return m;
                    }
                case ModuleTypes.Notes:
                    {
                        var m = new NotesModule(Mgr, this, cfg);
                        _noteMods.Add(m);
                        return m;
                    }
                case ModuleTypes.Drives:
                    return new DrivesModule(Mgr, this, cfg);
            }
            if (ModuleTypes.IsBuiltIn(cfg.Type)) return null;
            FrameworkElement pm = PluginManager.CreateModule(Mgr, this, cfg);      // a plugin's (or "add it")
            if (pm is InfoModule) _infoMods.Add((InfoModule)pm);       // (a plugin's gauge module: Downloads) updated with the others
            if (pm is TilesModule) _tileMods.Add((TilesModule)pm);     // (a plugin's tiles: Home Assistant's entities) too
            return pm;
        }

        /// <summary>
        /// Build 2: a widget with a fixed width / height steps down until its modules fit - graphs shorter first (to 20 %), then L text
        /// becomes M, then everything S. Applying the settings (a theme change, a resize) starts again from the sizes as set.
        /// </summary>
        int _wfit;
        double _overAtLevel;        // how much too big it was at the last level (CheckFit skips levels that change nothing)
        DateTime _fitQuietUntil = DateTime.MinValue;
        System.Windows.Threading.DispatcherTimer _refit;

        /// <summary>Fit again a moment from now (start-up, data arriving): back to the sizes as set, then step down only if still needed.</summary>
        void QueueRefit()
        {
            if (Cfg.Width <= 0 && Cfg.Height <= 0) return;
            if (_refit == null)
            {
                _refit = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                _refit.Tick += delegate
                {
                    _refit.Stop();
                    // never while typing in a module: rebuilding the modules throws the open editor away (a note that grows with its
                    // text gets taller as its editor opens - that re-fitted the widget and the keyboard was gone). EndTyping fits again.
                    if (_resizing || !IsLoaded || _typingOwner != null) return;
                    if (_wfit > 0) { _wfit = 0; Rebuild(); }
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(CheckFit));
                };
            }
            _refit.Stop();
            _refit.Start();
        }

        /// <summary>Build the modules again for a new fit level (its own size changes don't count as "the content changed").</summary>
        void Rebuild()
        {
            _fitQuietUntil = DateTime.UtcNow.AddSeconds(1.2);
            BuildModules();
            SetScrolling(Cfg.Height > 0);
        }

        void CheckFit()
        {
            if (_wfit >= 6 || !IsLoaded || (Cfg.Width <= 0 && Cfg.Height <= 0) || _resizing || _typingOwner != null) return;
            UpdateLayout();
            // not at its real size yet (still starting up): wait - QueueRefit comes back to it
            if ((Cfg.Width > 0 && Math.Abs(_box.ActualWidth - Math.Max(20, Cfg.Width)) > 2) ||
                (Cfg.Height > 0 && !Cfg.IsCollapsed && Math.Abs(_box.ActualHeight - Math.Max(20, Cfg.Height)) > 2))
            {
                Log.Write("Widget '" + Cfg.Name + "' fit: not its real size yet (" + Math.Round(_box.ActualWidth) + "x" + Math.Round(_box.ActualHeight) + ") - later");
                QueueRefit(); return;
            }
            double over = 0;
            if (Cfg.Height > 0 && _bodyHost.Child == _scroll && _scroll.ViewportHeight > 10)
                over = Math.Max(over, _scroll.ExtentHeight - _scroll.ViewportHeight);
            // too wide only counts when the modules sit side by side (a row or a grid): in a list, long text trims - and before a module
            // has its data its text is often longer (entity ids, "Loading…"), which used to step a widget down for good
            if (Cfg.Width > 0 && _bodyHost.ActualWidth > 10 && Cfg.Direction != "Vertical")
            {
                _body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                over = Math.Max(over, _body.DesiredSize.Width - _bodyHost.ActualWidth);
            }
            if (over <= 1) return;      // fits: nothing to do (and nothing to log)
            // skip the levels that can't help: shorter graphs didn't change a thing (no graphs, or not what's too big) - on to the
            // text; L to M with nothing in L - straight to S (each level is a rebuild of every module)
            int next = _wfit + 1;
            if (_wfit >= 1 && _wfit <= 4 && Math.Abs(over - _overAtLevel) < 1) next = 5;
            if (next == 5 && !AnyLargeText()) next = 6;
            Log.Write("Widget '" + Cfg.Name + "' fit: level " + _wfit + ", over by " + Math.Round(over) + " (scroll " + Math.Round(_scroll.ExtentHeight) + "/" + Math.Round(_scroll.ViewportHeight) +
                      ", body " + Math.Round(_body.DesiredSize.Width) + "/" + Math.Round(_bodyHost.ActualWidth) + ") - stepping down" + (next > _wfit + 1 ? " to level " + next : ""));
            _overAtLevel = over;
            _wfit = next;
            Rebuild();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(CheckFit));
        }

        /// <summary>A module set to L text (fit level 5 makes it M).</summary>
        bool AnyLargeText()
        {
            foreach (var c in Cfg.Modules)
            {
                string size = string.IsNullOrEmpty(c.TextSize) ? "M" : c.TextSize;
                if (BarText.Cap(size, 1) != size) return true;
            }
            return false;
        }

        void BuildModules()
        {
            // icon animations: an icon-only Launcher that asks for them (made before the modules, which take it as they are built)
            if (_fx != null) { _fx.Stop(); _fx = null; }
            _fxLayer.Children.Clear();
            double fxAway, fxSide;
            DockFx.Room(Cfg.Modules, _rowH, out fxAway, out fxSide);
            if (fxAway > 0 || fxSide > 0) _fx = new DockFx(_box, _fxLayer, false, true);
            _metricMods.Clear();
            _rows.Clear();
            _sensorMods.Clear();
            _startMods.Clear();
            _infoMods.Clear();
            _tileMods.Clear();
            _noteMods.Clear();
            _runMods.Clear();
            if (_typingOwner != null) { _typingOwner = null; StopKeyboard(); }
            _tabButtons.Clear();
            _tabPages.Clear();
            _tabStrip.Children.Clear();

            // Build 2: S / M / L text (every font drawn to the same room) and graph lengths - as set, or stepped down while a widget
            // with a fixed size is too small for them (see CheckFit)
            double factor = BarText.Factor(Theme.Font);
            double graphScale = _wfit <= 0 ? 1 : Math.Max(0.2, 1 - 0.2 * Math.Min(_wfit, 4));
            int textCap = Math.Max(0, _wfit - 4);
            var diag = new System.Text.StringBuilder();
            foreach (var c in Cfg.Modules)
            {
                // a widget's rows grow to fit M / L (unlike a bar, whose height is fixed), so no stepping down for height here
                string size = BarText.Cap(string.IsNullOrEmpty(c.TextSize) ? "M" : c.TextSize, textCap);
                c.ShownFontSize = Math.Round(BarText.Px(size) * factor, 1);
                c.ShownTwoLines = size == "M";
                c.ShownGraphScale = graphScale;
                diag.Append(" " + c.Type + ":" + (string.IsNullOrEmpty(c.TextSize) ? "-" : c.TextSize) + "->" + size + "@" + c.ShownFontSize);
            }
            Log.Write("Widget '" + Cfg.Name + "' build: fit " + _wfit + ", font " + (Theme.Font != null ? Theme.Font.Source : "?") + " x" + factor.ToString("0.00") +
                      ", box " + Cfg.Width + "x" + Cfg.Height + ", row " + _rowH + ", " + Cfg.Direction + (Cfg.KeepSize ? ", keep size" : "") + " |" + diag);

            var els = new List<KeyValuePair<ModuleConfig, FrameworkElement>>();
            foreach (var cfg in Cfg.Modules)
            {
                FrameworkElement el = Create(cfg);
                if (el == null) continue;
                PopupGlass.SetModule(el, cfg);       // its popups find their module's settings
                TextElement.SetFontSize(el, cfg.ShownFontSize);   // its S / M / L size; text inside inherits it
                els.Add(new KeyValuePair<ModuleConfig, FrameworkElement>(cfg, el));
            }

            bool horizontal = Cfg.Direction == "Horizontal", grid = Cfg.Direction == "Grid";
            bool cards = Cfg.CardPerModule;
            double spacing = Math.Round(Cfg.Spacing * _dens);
            if (cards) spacing = Math.Max(spacing, Math.Round(6 * _dens));
            _tabsOn = Cfg.ShowTabs && els.Count >= 2;
            int active = Math.Max(0, Math.Min(els.Count - 1, Cfg.ActiveTab));

            // tabs: every module in the same cell, only the chosen one visible (the others keep their room, so the widget is as big as
            // its biggest module and does not jump when switching); grid: equal cells, GridColumns across; otherwise a stack or a row
            Panel body;
            if (_tabsOn) body = new Grid();
            else if (grid) body = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = Math.Max(1, Math.Min(Cfg.GridColumns, Math.Max(1, els.Count))), Margin = new Thickness(-spacing / 2)
            };
            else body = new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
            _tabHost.BorderThickness = new Thickness(0, 0, 0, 1);
            _tabHost.Margin = new Thickness(0, 0, 0, Math.Max(spacing, Math.Round(6 * _dens)));

            _cardPadV = cards ? Math.Round(5 * _dens) : 0;
            double cardPadH = cards ? Math.Round(8 * _dens) : 0;
            Brush cardBg = cards ? (Brush)Theme.Faded(Theme.Fg, 7) : Brushes.Transparent;
            double cardR = Math.Max(4, Math.Min(10, Cfg.CornerRadius * 0.7));
            var names = new Dictionary<string, int>();
            int count = 0;
            foreach (var kv in els)
            {
                FrameworkElement el = kv.Value;
                el.VerticalAlignment = VerticalAlignment.Center;
                // stacked modules are all as wide as the widest one - the widget's width when set, otherwise the widest module's
                bool stretch = grid || (Cfg.Width > 0 ? !horizontal || _tabsOn : !horizontal && !_tabsOn);
                el.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                int rows = 1;
                var multi = el as IMultiRow;
                if (multi != null) rows = multi.RowCount;
                // a module that sets its own height (a note, a list) sits at the top of its row - centred, a short one in a tall row got a gap above it
                if (rows == 0) el.VerticalAlignment = VerticalAlignment.Top;
                var row = new Border { Child = el, Background = cardBg };
                if (cards)
                {
                    row.CornerRadius = new CornerRadius(cardR);
                    row.Padding = new Thickness(cardPadH, _cardPadV, cardPadH, _cardPadV);
                }
                // a row is at least as tall as its text needs: two lines at M, one large line at L
                double need = kv.Key.ShownFontSize <= 0 ? 0 : kv.Key.ShownTwoLines == true ? kv.Key.ShownFontSize * 2 * 1.3 : kv.Key.ShownFontSize * 1.35;
                if (rows > 0) row.Height = Math.Max(_rowH * rows, Math.Ceiling(need)) + 2 * _cardPadV;      // 0 = the module decides its own height (at least one row)
                else row.MinHeight = _rowH + 2 * _cardPadV;
                if (_tabsOn)
                {
                    row.Visibility = count == active ? Visibility.Visible : Visibility.Hidden;
                    row.VerticalAlignment = VerticalAlignment.Top;
                    _tabPages.Add(row);
                    AddTab(TabName(kv.Key, names), count);
                }
                else if (grid)
                {
                    row.Margin = new Thickness(spacing / 2);
                    row.VerticalAlignment = VerticalAlignment.Top;
                }
                else if (count > 0) row.Margin = horizontal ? new Thickness(spacing, 0, 0, 0) : new Thickness(0, spacing, 0, 0);
                body.Children.Add(row);
                _rows.Add(new KeyValuePair<Border, int>(row, rows));
                count++;
            }

            if (count == 0)
                body.Children.Add(new TextBlock
                {
                    Text = "Empty widget - add modules in Settings > Widgets",
                    Foreground = Theme.FgDim,
                    Margin = new Thickness(6)
                });
            _body = body;
            ContentChanged();       // a module that starts hidden takes its row with it (and an all-hidden widget hides)
            // contents that change size later (data arriving after start-up): fit again from the sizes as set, a moment later
            body.SizeChanged += delegate { if (DateTime.UtcNow > _fitQuietUntil) QueueRefit(); };
            StyleTabs();
            if (Cfg.Width <= 0 && (_tabsOn || (!horizontal && !grid && count >= 2)))
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(EqualWidths));
        }

        /// <summary>
        /// Auto width with tabs or stacked modules: every module as wide as the widest one (and the tab strip), so a note wraps at that
        /// width and the other modules fill it, instead of each keeping its own width inside the widest one's room.
        /// </summary>
        void EqualWidths()
        {
            var pages = new List<Border>();
            if (_tabsOn) pages.AddRange(_tabPages);
            else foreach (var r in _rows) pages.Add(r.Key);
            if (pages.Count < 2) return;
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            double max = 0;
            foreach (Border pg in pages) { if (pg.Visibility == Visibility.Collapsed) continue; pg.Measure(inf); max = Math.Max(max, pg.DesiredSize.Width); }
            if (_tabsOn) { _tabStrip.Measure(inf); max = Math.Max(max, _tabStrip.DesiredSize.Width); }
            foreach (Border pg in pages)
            {
                pg.HorizontalAlignment = HorizontalAlignment.Stretch;
                var note = pg.Child as NotesModule;
                if (note != null) note.FitWidth(max - pg.Padding.Left - pg.Padding.Right);
                else
                {
                    var fe = pg.Child as FrameworkElement;
                    if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
            }
        }

        /// <summary>A tab's name: the module's title / label text when it has one, otherwise a short type name (numbered when repeated).</summary>
        static string TabName(ModuleConfig cfg, Dictionary<string, int> used)
        {
            string n = null;
            if (!string.IsNullOrWhiteSpace(cfg.TabName)) n = cfg.TabName.Trim();
            else if (cfg.Type == ModuleTypes.Label && !string.IsNullOrWhiteSpace(cfg.LabelText)) n = cfg.LabelText.Trim();
            else if (cfg.Type == ModuleTypes.Hwinfo && !string.IsNullOrWhiteSpace(cfg.SensorTitle)) n = cfg.SensorTitle.Trim();
            else if (!string.IsNullOrWhiteSpace(cfg.Title)) n = cfg.Title.Trim();
            if (string.IsNullOrEmpty(n)) n = ModuleTypes.ShortName(cfg.Type);
            if (n.Length > 18) n = n.Substring(0, 17) + "\u2026";
            int k;
            used.TryGetValue(n, out k);
            used[n] = k + 1;
            return k == 0 ? n : n + " " + (k + 1);
        }

        void AddTab(string name, int index)
        {
            var tb = new TextBlock { Text = name, FontSize = Math.Max(8, Theme.FontSize * 0.95) };
            double d = _dens;
            var b = new Border
            {
                Child = tb, Padding = new Thickness(Math.Round(10 * d), Math.Round(5 * d), Math.Round(10 * d), Math.Round(4 * d)),
                CornerRadius = new CornerRadius(6, 6, 0, 0), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent
            };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                e.Handled = true;           // a tab is not a drag handle
                SelectTab(index);
            };
            _tabStrip.Children.Add(b);
            _tabButtons.Add(b);
        }

        void SelectTab(int index)
        {
            if (index < 0 || index >= _tabPages.Count) return;
            for (int i = 0; i < _tabPages.Count; i++) _tabPages[i].Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
            Cfg.ActiveTab = index;
            StyleTabs();
            Mgr.SaveSoon();         // remembered for next time
        }

        void StyleTabs()
        {
            int active = Math.Max(0, Math.Min(_tabButtons.Count - 1, Cfg.ActiveTab));
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                Border b = _tabButtons[i];
                bool on = i == active;
                b.Background = on ? Theme.Hover : Brushes.Transparent;
                b.BorderBrush = on ? Theme.MainAccent : Brushes.Transparent;
                var tb = b.Child as TextBlock;
                if (tb != null) tb.Foreground = on ? Theme.Fg : Theme.FgDim;
            }
        }

        void UpdateShadowClip()
        {
            double w = _stage.ActualWidth, h = _stage.ActualHeight;
            if (w < 1 || h < 1 || !Cfg.ShowShadow) { _shadowHost.Clip = null; return; }
            double pd = _shadowPad;
            var all = new RectangleGeometry(new Rect(0, 0, w + 2 * pd, h + 2 * pd));
            // the same shape as the outline: square corners stay square (a rounded hole let the black shadow show in the
            // corners of a thick square outline)
            double r = Cfg.CornerRadius > 0 ? Cfg.CornerRadius + (Cfg.ShowBorder ? Math.Max(0.5, Math.Min(12, Cfg.BorderThickness)) : 0) : 0;
            var hole = new RectangleGeometry(new Rect(pd, pd, w, h), r, r);
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, all, hole);
            clip.Freeze();
            _shadowHost.Clip = clip;
        }

        /// <summary>Nobody can see it right now (the last second's tick): its modules took their samples but didn't draw.</summary>
        internal bool Unseen { get { return _unseen; } }
        bool _unseen;

        public void UpdateStats(SystemStats st, bool addSample)
        {
            // hidden (the PC locked, a full-screen window, or app windows in front of all but a sliver of it - 0.38.224): the modules
            // still take every sample, so graphs, averages and speeds stay whole, but nothing is drawn; the first tick it shows again
            // draws everything as it is. Only the 1-second tick is held back - a settings change (addSample false) always draws.
            bool quiet = addSample && !Dragging && !_resizing && (!IsVisible || _away || ModuleVisibility.Covered(this));
            _unseen = quiet;
            foreach (var m in _metricMods) { m.Quiet = quiet; try { m.Update(st, Mgr.Gpu, addSample); } finally { m.Quiet = false; } }
            foreach (var m in _sensorMods) { m.Quiet = quiet; try { m.Update(addSample); } finally { m.Quiet = false; } }
            foreach (var m in _infoMods) { m.Quiet = quiet; try { m.Update(addSample); } finally { m.Quiet = false; } }
            if (!quiet) foreach (var m in _tileMods) m.Update();
        }

        // =================================================================
        // Position
        // =================================================================
        /// <summary>Move to the saved position (the box kept on the monitor).</summary>
        public void ApplyPosition()
        {
            // its own monitor - or, while that's disconnected, hidden (the setting stays, so it comes back when the monitor returns)
            MonitorInfo own = MonitorHelper.Find(Cfg.Monitor, Cfg.MonitorKey);
            if (own == null) { SetAway(true); return; }
            if (!string.IsNullOrEmpty(Cfg.Monitor))
            {
                if (Cfg.MonitorKey.Length == 0) Cfg.MonitorKey = own.Key;            // remember the monitor itself from now on
                if (!string.Equals(Cfg.Monitor, own.DeviceName, StringComparison.OrdinalIgnoreCase)) Cfg.Monitor = own.DeviceName;   // came back as another DISPLAYn
            }
            MonitorInfo mon = own;
            int inset = InsetPx(MonitorHelper.ScaleOf(mon));
            int w = 200, h = 100;
            RECT cur;
            if (Hwnd != IntPtr.Zero && Native.GetWindowRect(Hwnd, out cur) && cur.Width > 20) { w = cur.Width - 2 * inset; h = cur.Height - 2 * inset; }

            // OffsetX / Y = the box's corner (inside the outline): outline and shadow changes leave the box where it is
            int x = mon.Bounds.Left + Cfg.OffsetX, y = mon.Bounds.Top + Cfg.OffsetY;
            x = Math.Max(mon.Bounds.Left, Math.Min(mon.Bounds.Right - Math.Max(1, w), x));
            y = Math.Max(mon.Bounds.Top, Math.Min(mon.Bounds.Bottom - Math.Max(1, h), y));
            WantX = x - inset; WantY = y - inset; HavePos = true;
            Enforce();
            SetAway(false);          // back in its place first, then shown
        }

        /// <summary>Shadow margin + outline: from the window's corner to the box, in DIPs.</summary>
        double InsetDip { get { return _outer.Padding.Left + _ring.BorderThickness.Left; } }
        int InsetPx(double scale) { return (int)Math.Round(InsetDip * scale); }
        double WindowScale { get { return Hwnd == IntPtr.Zero ? 1 : Math.Max(0.5, Native.GetDpiForWindow(Hwnd) / 96.0); } }

        /// <summary>Space between the window's edge and the box (shadow margin and outline), in device pixels.</summary>
        public int VisibleInsetPx { get { return InsetPx(WindowScale); } }

        /// <summary>The outline's width in device pixels (0 = no outline).</summary>
        public int OutlinePx { get { return (int)Math.Round(_ring.BorderThickness.Left * WindowScale); } }

        /// <summary>The box's size right now, in DIPs (for "Keep the box size").</summary>
        public Size BoxSize { get { return new Size(_box.ActualWidth, _box.ActualHeight); } }

        /// <summary>The box on screen (inside the outline; outline and shadow margin left out), device pixels. Snapping, aligning and
        /// snap links all use this.</summary>
        public bool TryVisibleRect(out RECT r)
        {
            r = new RECT();
            if (Hwnd == IntPtr.Zero || !Native.GetWindowRect(Hwnd, out r)) return false;
            int i = VisibleInsetPx;
            r.Left += i; r.Top += i; r.Right -= i; r.Bottom -= i;
            return r.Right > r.Left && r.Bottom > r.Top;
        }

        /// <summary>Move so the box's top-left is at (left, top) and remember it. save = false: keep the position but do not write settings.json
        /// yet (the caller saves once for several widgets).</summary>
        public void MoveVisibleTo(int left, int top, bool save = true)
        {
            if (Hwnd == IntPtr.Zero) return;
            int i = VisibleInsetPx;
            WantX = left - i; WantY = top - i; HavePos = true;
            Native.SetWindowPos(Hwnd, IntPtr.Zero, WantX, WantY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Enforce();
            if (!Displaced) StorePosition();       // a widget away from its (disconnected) monitor keeps its place there
            if (save) Mgr.WidgetMoved();
        }

        /// <summary>Its monitor isn't connected (see Away: it's hidden meanwhile, and comes back when the monitor returns).</summary>
        public bool Displaced { get { return !MonitorHelper.IsConnected(Cfg.Monitor, Cfg.MonitorKey); } }

        public void ShowSelected(bool on) { _pick.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

        protected override void AdjustDrag(ref int x, ref int y) { WidgetSnap.Snap(Mgr, this, ref x, ref y); }

        // ---- several picked widgets dragged together ----
        List<WidgetWindow> _followers;
        List<RECT> _followStart;

        protected override void DragStarted()
        {
            _followers = null; _followStart = null;
            List<WidgetWindow> list = WidgetSnap.StartGroupDrag(this);
            if (list.Count == 0) return;
            _followers = new List<WidgetWindow>();
            _followStart = new List<RECT>();
            foreach (WidgetWindow w in list)
            {
                RECT r;
                if (w.NativeHandle == IntPtr.Zero || !Native.GetWindowRect(w.NativeHandle, out r)) continue;
                _followers.Add(w);
                _followStart.Add(r);
            }
        }

        protected override void DraggedBy(int dx, int dy)
        {
            if (_followers == null) return;
            for (int i = 0; i < _followers.Count; i++)
                try { _followers[i].MoveWithGroup(_followStart[i].Left + dx, _followStart[i].Top + dy); } catch { }
        }

        void SavePosition()
        {
            if (StorePosition()) Mgr.WidgetMoved();
        }

        /// <summary>Moved along with another picked widget: remember where it is now.</summary>
        public void StoreGroupPosition() { StorePosition(); }

        /// <summary>Remember where the box is (its monitor and corner) in the settings.</summary>
        bool StorePosition()
        {
            RECT r;
            if (!Native.GetWindowRect(Hwnd, out r)) return false;
            MonitorInfo mon = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            if (mon == null) return false;
            int i = VisibleInsetPx;
            Cfg.Monitor = mon.DeviceName;
            Cfg.MonitorKey = mon.Key;
            Cfg.OffsetX = r.Left + i - mon.Bounds.Left;
            Cfg.OffsetY = r.Top + i - mon.Bounds.Top;
            WantX = r.Left; WantY = r.Top;
            return true;
        }

        // =================================================================
        // Mouse
        // =================================================================
        void Box_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lockedPress = null;
            if (e.Handled || e.ClickCount > 1) return;
            if (OnScrollBar(e.OriginalSource as DependencyObject)) return;      // a press on a scrollbar (its empty track too) never drags the widget
            if (Cfg.Locked) { _lockedPress = e.GetPosition(this); return; }     // a drag starting here says it's locked (Box_MouseMove)
            BeginManualDrag();
            _box.CaptureMouse();
        }

        /// <summary>A locked widget being dragged: the outline and the hint for a moment, and the "no" cursor - it used to just not move.</summary>
        void FlashLocked()
        {
            int mine = ++_lockFlashes;
            var hold = TimeSpan.FromMilliseconds(900);
            foreach (UIElement el in new UIElement[] { _lockFlash, _lockHint })
            {
                el.Visibility = Visibility.Visible;
                var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(350))) { BeginTime = hold };
                UIElement e2 = el;
                fade.Completed += delegate
                {
                    if (mine != _lockFlashes) return;      // flashed again meanwhile: that one ends it
                    e2.BeginAnimation(OpacityProperty, null);
                    e2.Visibility = Visibility.Collapsed;
                    _box.Cursor = null;
                };
                el.BeginAnimation(OpacityProperty, fade);
            }
            _box.Cursor = Cursors.No;
        }

        /// <summary>The press is on a scrollbar (a note's, a list's): it scrolls, it doesn't move the widget.</summary>
        bool OnScrollBar(DependencyObject d)
        {
            while (d != null && d != _box)
            {
                if (d is System.Windows.Controls.Primitives.ScrollBar) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        void Box_MouseMove(object sender, MouseEventArgs e)
        {
            if (Dragging && e.LeftButton == MouseButtonState.Pressed) UpdateManualDrag();
            if (_lockedPress != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed || !Cfg.Locked) { _lockedPress = null; return; }
                Vector moved = e.GetPosition(this) - _lockedPress.Value;
                if (Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance * 2 || Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance * 2)
                {
                    _lockedPress = null;      // once per press
                    FlashLocked();
                }
            }
        }

        void Box_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _lockedPress = null;
            if (!Dragging) return;
            _box.ReleaseMouseCapture();       // LostMouseCapture finishes the drag
        }

        void FinishDrag()
        {
            if (!Dragging) return;
            WidgetSnap.HideGuides();
            bool header = _headerDown;
            _headerDown = false;
            bool moved = EndManualDrag();
            List<WidgetWindow> followers = _followers;
            _followers = null; _followStart = null;
            if (!moved && header && !_resizing) ToggleCollapsed();     // a click on the header
            if (moved && followers != null && followers.Count > 0)
            {
                // the picked widgets moved together: each keeps its new place; links inside the group stay
                StorePosition();
                foreach (WidgetWindow f in followers) try { f.StoreGroupPosition(); } catch { }
                WidgetSnap.DroppedGroup(Mgr, this, followers);
                Mgr.WidgetMoved();
            }
            else if (moved)
            {
                StorePosition();
                WidgetSnap.Dropped(Mgr, this);      // new snap links (none after an Alt drag); widgets snapped to this one let go of it
                Mgr.WidgetMoved();
            }
            else WidgetSnap.EndGroupDrag();
        }

        void Box_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var m = new ContextMenu();
            // a module under the mouse (a note) adds its own items first
            for (DependencyObject d = e.OriginalSource as DependencyObject; d != null && d != _box; d = ParentOf(d))
            {
                var wm = d as IWidgetMenu;
                if (wm != null) { wm.AddMenuItems(m, e.OriginalSource as DependencyObject); break; }
            }
            AddItem(m, "Widget settings…", delegate { Mgr.OpenWidgetSettings(Cfg); });
            if (Cfg.ShowHeader && Cfg.Collapsible) AddItem(m, Cfg.Collapsed ? "Expand" : "Collapse to the header", delegate { ToggleCollapsed(); });
            bool multi = WidgetSnap.SelectedCount >= 2;
            if (!multi)     // one widget: lock / unlock just this one (several picked: the group item below does it)
                AddItem(m, Cfg.Locked ? "Unlock position" : "Lock position", delegate
                {
                    Cfg.Locked = !Cfg.Locked;
                    Mgr.SettingsChanged();
                    Mgr.WidgetsEditedOutsideSettings();
                });
            if (_resizing) AddItem(m, "Finish resizing", delegate { EndResize(true); });
            else if (!multi)
            {
                var rs = new MenuItem { Header = "Resize", IsEnabled = !Cfg.Locked, ToolTip = Cfg.Locked ? "Unlock the widget first" : null };
                rs.Click += delegate { BeginResize(); };
                ToolTipService.SetShowOnDisabled(rs, true);
                m.Items.Add(rs);
            }
            if (!multi) AddAutoHideItems(m);
            m.Items.Add(new Separator());
            // several widgets picked with Ctrl+click: line them up / spread them out
            if (multi)
            {
                var align = new MenuItem { Header = "Align " + WidgetSnap.SelectedCount + " widgets" };
                AddSub(align, "Left edges", delegate { WidgetSnap.Align("left"); });
                AddSub(align, "Right edges", delegate { WidgetSnap.Align("right"); });
                AddSub(align, "Centres (one above the other)", delegate { WidgetSnap.Align("centre"); });
                align.Items.Add(new Separator());
                AddSub(align, "Top edges", delegate { WidgetSnap.Align("top"); });
                AddSub(align, "Bottom edges", delegate { WidgetSnap.Align("bottom"); });
                AddSub(align, "Middles (side by side)", delegate { WidgetSnap.Align("middle"); });
                m.Items.Add(align);
                var dist = new MenuItem { Header = "Distribute" };
                var h = AddSub(dist, "Horizontally - equal gaps", delegate { WidgetSnap.Distribute(Mgr, true, false); });
                var v = AddSub(dist, "Vertically - equal gaps", delegate { WidgetSnap.Distribute(Mgr, false, false); });
                h.IsEnabled = v.IsEnabled = WidgetSnap.SelectedCount >= 3;
                dist.Items.Add(new Separator());
                AddSub(dist, "Horizontally - snap gap (" + Mgr.Settings.WidgetGap + ")", delegate { WidgetSnap.Distribute(Mgr, true, true); });
                AddSub(dist, "Vertically - snap gap (" + Mgr.Settings.WidgetGap + ")", delegate { WidgetSnap.Distribute(Mgr, false, true); });
                m.Items.Add(dist);
                bool allLocked = WidgetSnap.AllSelectedLocked;
                AddItem(m, (allLocked ? "Unlock " : "Lock ") + WidgetSnap.SelectedCount + " widgets in place", delegate
                {
                    WidgetSnap.SetSelectedLocked(Mgr, !allLocked);
                });
                AddItem(m, "Clear selection", delegate { WidgetSnap.ClearSelection(); });
            }
            else
                m.Items.Add(new MenuItem { Header = "Ctrl+click widgets to select multiple", IsEnabled = false });
            m.PlacementTarget = _box;
            _menuOpen = true;
            m.Closed += delegate { _menuOpen = false; };
            m.IsOpen = true;
        }

        // =================================================================
        // Fixed height: scrolling
        // =================================================================
        /// <summary>
        /// Widget-style thin scrollbars for the scroll areas in el (a module's own list, or a whole window). Their colour is the resource
        /// "wThumb" (a widget sets it; a window of its own sets it too).
        /// </summary>
        [PluginApi]
        public static void StyleScrollbars(FrameworkElement el)
        {
            try { el.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(ScrollXaml)); }
            catch (Exception ex) { Log.Write("Widget scrollbars: " + ex.Message); }
        }

        internal const string ScrollXaml =
@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <ControlTemplate x:Key='wThumbT' TargetType='Thumb'>
    <Border CornerRadius='3' Margin='3,2,0,2' Background='{DynamicResource wThumb}'/>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb><Thumb Template='{StaticResource wThumbT}'/></Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        void LoadScrollStyle()
        {
            try { _scroll.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(ScrollXaml)); }
            catch (Exception ex) { Log.Write("Widget scroll style: " + ex.Message); }
            _scroll.Resources["wThumb"] = Brushes.Gray;
        }

        /// <summary>Put the modules inside the scroller (fixed height) or straight under the header / tabs.</summary>
        void SetScrolling(bool on)
        {
            if (on && _bodyHost.Child == _scroll && _scroll.Content == _body) return;
            if (!on && _bodyHost.Child == _body) return;
            _scroll.Content = null;
            _bodyHost.Child = null;
            if (on) { _scroll.Content = _body; _bodyHost.Child = _scroll; }
            else _bodyHost.Child = _body;
        }

        // =================================================================
        // Resize mode: right-click > Resize, drag the edges
        // =================================================================
        void BuildResizeLayer()
        {
            _resizeLayer.Children.Add(_resizeFrame);
            // eight small handles: corners and the middle of each side
            var spots = new[] { new[] { 0, 0 }, new[] { 1, 0 }, new[] { 2, 0 }, new[] { 0, 1 }, new[] { 2, 1 }, new[] { 0, 2 }, new[] { 1, 2 }, new[] { 2, 2 } };
            foreach (int[] sp in spots)
            {
                var hb = new Border
                {
                    Width = 9, Height = 9, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), IsHitTestVisible = false,
                    HorizontalAlignment = sp[0] == 0 ? HorizontalAlignment.Left : sp[0] == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Right,
                    VerticalAlignment = sp[1] == 0 ? VerticalAlignment.Top : sp[1] == 1 ? VerticalAlignment.Center : VerticalAlignment.Bottom,
                    Margin = new Thickness(-1)
                };
                _handles.Add(hb);
                _resizeLayer.Children.Add(hb);
            }
            var card = new StackPanel();
            card.Children.Add(_resizeInfo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            buttons.Children.Add(CardButton("Done", delegate { EndResize(true); }));
            buttons.Children.Add(CardButton("Cancel", delegate { EndResize(false); }));
            card.Children.Add(buttons);
            _resizeCard.Child = card;
            _resizeLayer.Children.Add(_resizeCard);

            _resizeLayer.MouseMove += Resize_MouseMove;
            _resizeLayer.MouseLeftButtonDown += Resize_MouseDown;
            _resizeLayer.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (_resizeLayer.IsMouseCaptured) _resizeLayer.ReleaseMouseCapture(); };
            _resizeLayer.LostMouseCapture += delegate { FinishSizing(); FinishDrag(); };
            _resizeLayer.MouseRightButtonUp += Box_MouseRightButtonUp;
        }

        TextBlock CardButton(string text, Action a)
        {
            var tb = new TextBlock { Text = text, Margin = new Thickness(6, 0, 6, 0), Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline };
            tb.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            tb.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; a(); };
            tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding { Source = _resizeFrame, Path = new PropertyPath("Stroke") });
            return tb;
        }

        public bool Resizing { get { return _resizing; } }

        void BeginResize()
        {
            if (_resizing || Cfg.Locked) return;
            _resizing = true;
            _origW = Cfg.Width; _origH = Cfg.Height; _origMH = Cfg.ModuleHeight;
            _origX = Cfg.OffsetX; _origY = Cfg.OffsetY; _origMon = Cfg.Monitor; _origKey = Cfg.MonitorKey;
            _origLinkX = Cfg.LinkX; _origLinkY = Cfg.LinkY;
            _resizeLayer.Visibility = Visibility.Visible;
            UpdateResizeInfo();
            try { Activate(); } catch { }
            _resizeLayer.Focus();
            Keyboard.Focus(_resizeLayer);
        }

        /// <summary>keep = save the new size; otherwise put the old size and place back.</summary>
        void EndResize(bool keep)
        {
            if (!_resizing) return;
            if (_resizeLayer.IsMouseCaptured) _resizeLayer.ReleaseMouseCapture();
            _resizing = false;
            _resizeLayer.Visibility = Visibility.Collapsed;
            WidgetSnap.HideGuides();
            if (!keep)
            {
                Cfg.Width = _origW; Cfg.Height = _origH; Cfg.ModuleHeight = _origMH;
                Cfg.OffsetX = _origX; Cfg.OffsetY = _origY; Cfg.Monitor = _origMon; Cfg.MonitorKey = _origKey;
                Cfg.LinkX = _origLinkX; Cfg.LinkY = _origLinkY;
            }
            Mgr.SettingsChanged();
            Mgr.WidgetsEditedOutsideSettings();
        }

        void UpdateResizeInfo()
        {
            string w = Cfg.Width > 0 ? Cfg.Width.ToString() : "auto";
            string h = Cfg.Height > 0 ? Cfg.Height + " (fixed)" : "auto";
            _resizeInfo.Text = w + " × " + h + "\nrow height " + Cfg.ModuleHeight + "\nShift + top / bottom = fixed height";
        }

        int ZoneAt(Point p)
        {
            double w = _resizeLayer.ActualWidth, h = _resizeLayer.ActualHeight;
            double band = Math.Max(7, Math.Min(14, Math.Min(w, h) / 4));
            int z = 0;
            if (p.X < band) z |= 1; else if (p.X > w - band) z |= 2;
            if (p.Y < band) z |= 4; else if (p.Y > h - band) z |= 8;
            return z;
        }

        static Cursor CursorFor(int z)
        {
            bool lr = (z & 3) != 0, tb = (z & 12) != 0;
            if (lr && tb) return ((z & 1) != 0) == ((z & 4) != 0) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            if (lr) return Cursors.SizeWE;
            if (tb) return Cursors.SizeNS;
            return Cursors.SizeAll;
        }

        void Resize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _zone = ZoneAt(e.GetPosition(_resizeLayer));
            if (_zone == 0)
            {
                // the middle: move the widget as usual (snapping included)
                BeginManualDrag();
                _resizeLayer.CaptureMouse();
                return;
            }
            if (!TryVisibleRect(out _startVis)) return;
            _start = Native.CursorPos();
            _scale = Math.Max(0.5, Native.GetDpiForWindow(Hwnd) / 96.0);
            _startH = _box.ActualHeight;
            _startMH = Cfg.ModuleHeight;
            _fixedH = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if ((_zone & 12) != 0 && !_fixedH && Cfg.Height > 0)
            {
                // a plain top / bottom drag goes back to "fit the modules" and changes the row height
                Cfg.Height = 0;
                _box.Height = double.NaN;
                SetScrolling(false);
                UpdateLayout();
                _startH = _box.ActualHeight;
                TryVisibleRect(out _startVis);
            }
            _sizing = true;
            _resizeLayer.CaptureMouse();
        }

        void Resize_MouseMove(object sender, MouseEventArgs e)
        {
            if (Dragging && e.LeftButton == MouseButtonState.Pressed) { UpdateManualDrag(); return; }
            if (!_sizing) { _resizeLayer.Cursor = CursorFor(ZoneAt(e.GetPosition(_resizeLayer))); return; }
            if (e.LeftButton != MouseButtonState.Pressed) return;

            POINT c = Native.CursorPos();
            int dx = c.X - _start.X, dy = c.Y - _start.Y;
            int L = _startVis.Left, T = _startVis.Top, R = _startVis.Right, B = _startVis.Bottom;
            int minW = (int)Math.Round(80 * _scale), minH = (int)Math.Round(40 * _scale);
            if ((_zone & 1) != 0) L = Math.Min(R - minW, WidgetSnap.SnapEdge(Mgr, this, L + dx, true, true, T, B));
            if ((_zone & 2) != 0) R = Math.Max(L + minW, WidgetSnap.SnapEdge(Mgr, this, R + dx, true, false, T, B));
            if ((_zone & 4) != 0) T = Math.Min(B - minH, WidgetSnap.SnapEdge(Mgr, this, T + dy, false, true, L, R));
            if ((_zone & 8) != 0) B = Math.Max(T + minH, WidgetSnap.SnapEdge(Mgr, this, B + dy, false, false, L, R));

            if ((_zone & 3) != 0)
            {
                Cfg.Width = Math.Max(80, Math.Min(1600, (int)Math.Round((R - L) / _scale)));
                _box.Width = Cfg.Width;
                foreach (var r in _rows) if (Cfg.Direction != "Horizontal") r.Key.Child.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            }
            if ((_zone & 12) != 0)
            {
                double hDip = (B - T) / _scale;
                if (_fixedH)
                {
                    Cfg.Height = Math.Max(40, Math.Min(1600, (int)Math.Round(hDip)));
                    _box.Height = Cfg.Height;
                    SetScrolling(true);
                }
                else
                {
                    // spread the change over the module rows (natural-height modules count as one)
                    int units = 0;
                    foreach (var r in _rows) units += Math.Max(1, r.Value);
                    if (Cfg.Direction == "Horizontal") units = 1;
                    int mh = Math.Max(20, Math.Min(200, _startMH + (int)Math.Round((hDip - _startH) / Math.Max(1, units) / _dens)));
                    if (mh != Cfg.ModuleHeight)
                    {
                        Cfg.ModuleHeight = mh;
                        _rowH = RowPx(mh);
                        foreach (var r in _rows)
                        {
                            if (r.Value > 0) r.Key.Height = _rowH * r.Value + 2 * _cardPadV;
                            else { r.Key.MinHeight = _rowH + 2 * _cardPadV; Remeasure(r.Key); }     // a tile fills the new module height
                        }
                    }
                }
            }

            UpdateResizeInfo();
            UpdateLayout();
            // keep the opposite edge still: a left / top drag moves the window as it grows
            int inset = VisibleInsetPx;
            int wpx = (int)Math.Round(_box.ActualWidth * _scale), hpx = (int)Math.Round(_box.ActualHeight * _scale);
            int left = (_zone & 1) != 0 ? _startVis.Right - wpx : _startVis.Left;
            int top = (_zone & 4) != 0 ? _startVis.Bottom - hpx : _startVis.Top;
            WantX = left - inset; WantY = top - inset; HavePos = true;
            Native.SetWindowPos(Hwnd, IntPtr.Zero, WantX, WantY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        void FinishSizing()
        {
            if (!_sizing) return;
            _sizing = false;
            WidgetSnap.HideGuides();
            if (!Displaced) StorePosition();
            WidgetSnap.Relink(Mgr, this);   // its edges may line up with something else now
            Mgr.WidgetMoved();
            Mgr.SettingsChanged();          // rebuilds the modules at the new row height
            Mgr.WidgetsEditedOutsideSettings();
            UpdateResizeInfo();
        }

        static DependencyObject ParentOf(DependencyObject d)
        {
            if (d is Visual) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            var ce = d as ContentElement;
            if (ce != null) return ContentOperations.GetParent(ce) ?? LogicalTreeHelper.GetParent(d);
            return LogicalTreeHelper.GetParent(d);
        }

        static MenuItem AddSub(MenuItem parent, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            parent.Items.Add(mi);
            return mi;
        }

        static void AddItem(ContextMenu m, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }
    }
}
