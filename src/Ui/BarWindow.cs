using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// One bar. Positioned in raw device pixels (SetWindowPos) so it behaves on mixed-DPI multi-monitor setups.
    /// Registers as an appbar with Explorer (work-area reservation) when "Reserve window space" is on.
    /// </summary>
    public sealed class BarWindow : Window, IModuleHost
    {
        readonly BarManager _mgr;
        public BarPlacement Placement { get; private set; }
        /// <summary>The bar's visible rectangle on screen (device pixels, outline not included).</summary>
        public RECT BarRect { get; private set; }
        public IntPtr MonitorHandle { get; private set; }
        /// <summary>This bar's theme: shared colours/font family plus this bar's font size and corner radius.</summary>
        public Theme Theme { get; private set; }
        public IntPtr Handle { get { return _hwnd; } }
        public string ActiveMonitorName { get; private set; }

        IntPtr _hwnd;
        HwndSource _source;
        uint _callbackMsg;
        bool _appBarRegistered, _placedOnce;
        /// <summary>Everything the last ApplyPlacement placed the bar by (null = place it whatever: something outside moved it).</summary>
        string _placedKey;
        RECT _lastReserved, _lastGranted;     // the strip last asked of ABM_SETPOS, and what Windows gave
        bool _hasLastReserved;

        readonly Border _root = new Border();
        /// <summary>The bar as drawn (its background and outline), inside the window's room for the shadow.</summary>
        public FrameworkElement VisualBar { get { return _root; } }
        /// <summary>The outline: drawn over the edge of the bar, never part of its layout, so it takes no room from the modules.</summary>
        readonly Border _outline = new Border { IsHitTestVisible = false, Background = null };
        readonly Border _sheen = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };     // the glass edge
        readonly Grid _host = new Grid();
        /// <summary>The blur mode this window was made with (null = no blur: a layered window as before).</summary>
        readonly Glass _glass;
        DockFx _fx;                  // dock animations (Dock layout, icon-only Launcher / Taskbar), null otherwise
        readonly Canvas _fxLayer = new Canvas { IsHitTestVisible = false };     // the dock glow halos: on the bar, under the icons
        double _fxAway, _fxSide;     // room (DIPs) the animations need beyond the bar: away from the edge, and on each side

        /// <summary>The icon animations of this bar (null when no module wants them).</summary>
        public DockFx Fx { get { return _fx; } }       // background blur: the blurred wallpaper under the bar
        readonly ModulePanel _panel = new ModulePanel();
        readonly DispatcherTimer _layoutTimer;

        readonly List<TaskbarModule> _taskMods = new List<TaskbarModule>();
        readonly List<TrayModule> _trayMods = new List<TrayModule>();
        readonly List<MetricModule> _metricMods = new List<MetricModule>();
        readonly List<SensorModule> _sensorMods = new List<SensorModule>();
        readonly List<InfoModule> _infoMods = new List<InfoModule>();
        readonly List<TilesModule> _tileMods = new List<TilesModule>();
        readonly List<StartMenuModule> _startMods = new List<StartMenuModule>();

        /// <summary>The Start menu buttons on this bar.</summary>
        public List<StartMenuModule> StartMenus { get { return _startMods; } }
        readonly List<RunBoxModule> _runMods = new List<RunBoxModule>();
        public List<RunBoxModule> RunBoxes { get { return _runMods; } }
        readonly List<TerminalModule> _termMods = new List<TerminalModule>();
        public List<TerminalModule> Terminals { get { return _termMods; } }

        // bar drag
        bool _dragPending, _dragging;
        POINT _dragStart;
        RECT _dragRect;

        // module drag (Ctrl+drag)
        ModuleHost _modHost;
        Point _modStart;
        bool _modPending, _modDragging;
        double _modDx;

        public BarWindow(BarManager mgr, BarPlacement placement)
        {
            _mgr = mgr;
            Placement = placement;
            _panel.SizeChanged += delegate { PanelResized(); };

            Title = "vShell";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            // clicking away while typing in a run box: done
            Deactivated += delegate { if (_typingOwner != null) { var o = _typingOwner; o.StopTyping(); } };
            Topmost = true;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Left = -32000; Top = -32000; Width = 120; Height = 40;
            _glass = new Glass(mgr);

            _panel.Margin = new Thickness(6, 0, 6, 0);
            _panel.ClipToBounds = true;
            // the dock glow layer sits on the bar's background, under the modules
            var rootContent = new Grid();
            rootContent.Children.Add(_fxLayer);
            rootContent.Children.Add(_panel);
            _root.Child = rootContent;
            _host.Children.Add(_glass);
            _host.Children.Add(_root);
            _host.Children.Add(_sheen);
            _host.Children.Add(_outline);
            Content = _host;

            _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _layoutTimer.Tick += delegate
            {
                _layoutTimer.Stop();
                if (_dragging || _dragPending || _modDragging || _modPending) return;
                ApplyPlacement();
            };

            // the whole bar is a drop target (tray icons dragged out of the popup are pinned wherever they land)
            _root.AllowDrop = true;
            _panel.AllowDrop = true;
            _root.DragOver += Root_DragOver;
            _root.Drop += Root_Drop;
            _root.DragLeave += Root_DragLeave;

            _root.PreviewMouseLeftButtonDown += Root_PreviewMouseLeftButtonDown;
            _root.MouseLeftButtonDown += Root_MouseLeftButtonDown;
            _root.MouseMove += Root_MouseMove;
            _root.MouseLeftButtonUp += Root_MouseLeftButtonUp;
            _root.LostMouseCapture += delegate { CancelDrag(); };
            _root.SizeChanged += delegate { UpdateContentClip(); };
            _root.MouseRightButtonUp += Root_MouseRightButtonUp;
            _root.MouseMove += delegate (object s, MouseEventArgs e) { if (_fx != null) _fx.MouseMove(e.GetPosition(_root)); };
            _root.MouseLeave += delegate { if (_fx != null) _fx.MouseLeave(); };
        }

        // ---- IModuleHost ----------------------------------------------------
        public double HostHeight { get { return Placement.BarHeight; } }
        public bool IsWidget { get { return false; } }

        public bool PopupsBelow { get { return Placement.Edge == "Top"; } }
        public bool FullWidth { get { return Placement.FullWidth; } }
        public void OpenSettings() { _mgr.OpenSettings(Placement); }

        public bool NeedsGpu
        {
            get
            {
                foreach (var m in Placement.Modules) if (ModuleTypes.UsesGpu(m)) return true;
                return false;
            }
        }

        // =================================================================
        // Drag & drop onto the bar (tray pinning)
        // =================================================================
        TrayModule FirstTray() { return _trayMods.Count > 0 ? _trayMods[0] : null; }
        public bool HasTray { get { return _trayMods.Count > 0; } }

        public bool TryGetTrayIconRect(TrayIcon ic, out RECT r)
        {
            r = new RECT();
            foreach (var t in _trayMods) if (t.TryGetIconRect(ic, out r)) return true;
            return false;
        }

        /// <summary>Explorer restarted and forgot every app bar: register again and re-reserve the strip.</summary>
        public void ReRegisterAppBar()
        {
            if (_hwnd == IntPtr.Zero || !Placement.ReserveSpace) return;
            RegisterAppBar();
            _hasLastReserved = false;
            _placedKey = null;
            ApplyPlacement();
        }

        void Root_DragOver(object sender, DragEventArgs e)
        {
            if (!TrayModule.IsTrayDrag(e)) return;
            TrayModule tray = FirstTray();
            e.Effects = tray != null ? DragDropEffects.Move : DragDropEffects.None;
            if (tray != null) tray.DragHint(e);
            e.Handled = true;
        }

        void Root_DragLeave(object sender, DragEventArgs e)
        {
            TrayModule tray = FirstTray();
            if (tray == null) return;
            Point p = e.GetPosition(_root);
            if (p.X < 0 || p.Y < 0 || p.X >= _root.ActualWidth || p.Y >= _root.ActualHeight) tray.ClearHint();
        }

        void Root_Drop(object sender, DragEventArgs e)
        {
            if (!TrayModule.IsTrayDrag(e)) return;
            e.Handled = true;
            var tray = FirstTray();
            if (tray != null) tray.HandleDrop(e);
        }

        // =================================================================
        // Lifetime
        // =================================================================
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_hwnd);

            WinStyle.HideFromTasks(_hwnd, true);
            if (_source != null) _source.AddHook(Hook);
            _mgr.Tasks.Ignored.Add(_hwnd);

            if (Placement.ReserveSpace) RegisterAppBar();
            ApplySettings();
        }

        protected override void OnClosed(EventArgs e)
        {
            _layoutTimer.Stop();
            if (_fx != null) _fx.Stop();
            if (_appBarRegistered)
            {
                var abd = NewAppBarData();
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
                _appBarRegistered = false;
            }
            _mgr.Tasks.Ignored.Remove(_hwnd);
            if (_source != null) _source.RemoveHook(Hook);
            base.OnClosed(e);
        }

        APPBARDATA NewAppBarData()
        {
            var abd = new APPBARDATA();
            abd.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = _hwnd;
            abd.uCallbackMessage = _callbackMsg;
            return abd;
        }

        void RegisterAppBar()
        {
            _callbackMsg = Native.RegisterWindowMessage("vShell.AppBar." + Placement.Id);
            var abd = NewAppBarData();
            Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
            _appBarRegistered = true;
        }

        // ---- typing in a run box on the bar ------------------------------------------------------------
        ITypingModule _typingOwner;

        /// <summary>A run box wants the keyboard: the bar may be activated until it is done (the bar never is otherwise).</summary>
        public void BeginTyping(ITypingModule m)
        {
            if (_typingOwner != null && _typingOwner != m) { var o = _typingOwner; _typingOwner = null; o.StopTyping(); }
            _typingOwner = m;
            if (_hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_NOACTIVATE);
            Native.SetForegroundWindow(_hwnd);
            try { Activate(); } catch { }
        }

        /// <summary>That module is done: the bar goes back to never taking the keyboard.</summary>
        public void EndTyping(ITypingModule m)
        {
            if (_typingOwner != m) return;
            _typingOwner = null;
            if (_hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE);
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            uint m = (uint)msg;
            if (m == Native.WM_MOUSEACTIVATE && _typingOwner == null)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            if (m == Native.WM_DPICHANGED && (_dragging || _modDragging))
            {
                handled = true;     // don't let WPF resize us mid-drag; we re-place on drop
                return IntPtr.Zero;
            }
            if (m == Native.WM_DISPLAYCHANGE || m == Native.WM_DPICHANGED)
            {
                // display scaling changed (on Windows 11 this now arrives too, see WinStyle): let WPF rescale, then re-place the bars
                _placedKey = null;
                _mgr.QueueReposition();
                return IntPtr.Zero;
            }
            if (_callbackMsg != 0 && m == _callbackMsg)
            {
                int code = wParam.ToInt32();
                if (code == Native.ABN_POSCHANGED) { _placedKey = null; RequestPlacement(); }      // another app bar came / went / moved
                else if (code == Native.ABN_FULLSCREENAPP)
                {
                    bool fullscreen = lParam != IntPtr.Zero;
                    if (OnDesktop) return IntPtr.Zero;
                    Native.SetWindowPos(_hwnd, fullscreen ? Native.HWND_BOTTOM : Native.HWND_TOPMOST, 0, 0, 0, 0,
                                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                }
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        // =================================================================
        // Settings / modules
        // =================================================================
        public void ApplySettings()
        {
            _placedKey = null;      // the look changed: place it again whatever (corners, outline, the layout's height)
            var t = Theme.From(_mgr.Settings, Placement);
            t.FontSize = Math.Round(BarText.Small * BarText.Factor(t.Font), 1);     // Build 2: popups and the bar's own text at S (no bar font size any more)
            Theme = t;
            _bgEdge = null;     // UpdateCorners paints the background (the gradient is mirrored on a bottom bar)
            _glass.Refresh(Placement.Blur);
            UpdateCorners();
            TextElement.SetForeground(_root, t.Fg);
            TextElement.SetFontFamily(_root, t.Font);
            TextElement.SetFontSize(_root, t.FontSize);

            _fit = 0;                       // start from the sizes as set; CheckFit steps down only if the bar is too full
            BuildModules();
            UpdateStats(_mgr.Stats, false);
            RequestPlacement();
        }

        /// <summary>All four corners, or only the two that do not touch the screen edge (bottom ones for a top bar, top ones for a bottom bar).</summary>
        string _bgEdge;     // the edge the background was last painted for

        /// <summary>The bar's background; on a bottom bar the gradient is mirrored top-to-bottom (when that is switched on).</summary>
        void UpdateBackground()
        {
            if (Theme == null) return;
            string key = Placement.Edge + "|" + _mgr.Settings.MirrorBottomGradient;
            if (key == _bgEdge) return;
            _bgEdge = key;
            int angle = Theme.GradientAngle;
            if (Placement.Edge != "Top" && _mgr.Settings.MirrorBottomGradient) Theme.GradientAngle = (360 - angle % 360) % 360;
            _root.Background = Theme.MakeBg(Placement.UseThemeOpacity ? -1 : Placement.BackgroundOpacity);
            Theme.GradientAngle = angle;
        }

        void UpdateCorners()
        {
            UpdateBackground();
            double r = Placement.CornerRadius;
            CornerRadius cr;
            if (Placement.RoundAllCorners) cr = new CornerRadius(r);
            else if (Placement.Edge == "Top") cr = new CornerRadius(0, 0, r, r);
            else cr = new CornerRadius(r, r, 0, 0);
            _root.CornerRadius = cr;
            // The outline lives in a margin OUTSIDE the bar: the window is enlarged by the border width on the sides that have
            // room, and the bar keeps exactly its own size and position inside it. Sides touching a screen edge get none.
            Thickness o = new Thickness(_oL / _scale, _oT / _scale, _oR / _scale, _oB / _scale);
            _root.Margin = o;
            _outline.BorderThickness = o;
            if (Theme != null) _outline.BorderBrush = Theme.Faded(Theme.Resolve(Placement.BorderColor, Theme.MainAccent), Placement.BorderOpacity);
            double lt = Math.Max(o.Top, o.Left), rt = Math.Max(o.Top, o.Right), rb = Math.Max(o.Bottom, o.Right), lb = Math.Max(o.Bottom, o.Left);
            _outline.CornerRadius = new CornerRadius(
                cr.TopLeft > 0 ? cr.TopLeft + lt : 0, cr.TopRight > 0 ? cr.TopRight + rt : 0,
                cr.BottomRight > 0 ? cr.BottomRight + rb : 0, cr.BottomLeft > 0 ? cr.BottomLeft + lb : 0);
            _glass.Margin = o;
            _glass.SetCorners(cr);
            _sheen.Margin = o;
            EdgeFx.GlassEdge(_sheen, Placement.GlassEdge, cr);
            UpdateContentClip();
        }

        /// <summary>
        /// The modules are cut at the bar's rounded corners, so a graph at either end follows the corner instead of showing past it.
        /// Only the corner wedges (inside the bar's box, outside its rounded shape) are cut away, so with icon animations on, grown and
        /// lifted icons still reach past the bar everywhere else.
        /// </summary>
        void UpdateContentClip()
        {
            double w = _root.ActualWidth, h = _root.ActualHeight;
            CornerRadius cr = _root.CornerRadius;
            bool round = cr.TopLeft > 0 || cr.TopRight > 0 || cr.BottomRight > 0 || cr.BottomLeft > 0;
            if (!round || w < 1 || h < 1) { _panel.Clip = null; return; }
            // the panel sits inside the bar with a margin: the bar's box in the panel's own coordinates
            var r = new Rect(-_panel.Margin.Left, -_panel.Margin.Top, w, h);
            var wedges = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(r), RoundedRect(r, cr));
            var all = new RectangleGeometry(new Rect(r.Left - 4000, r.Top - 4000, r.Width + 8000, r.Height + 8000));
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, all, wedges);
            clip.Freeze();
            _panel.Clip = clip;
        }

        /// <summary>
        /// The same rounded rectangle a Border draws: each corner's radius is cut down only where the two corners of one side would
        /// overlap (a 30 px high bar with radius 20 on its bottom corners keeps 20, as the bar shows it - not half the height).
        /// </summary>
        static Geometry RoundedRect(Rect r, CornerRadius c)
        {
            double tlx = c.TopLeft, tly = c.TopLeft, trx = c.TopRight, try_ = c.TopRight;
            double brx = c.BottomRight, bry = c.BottomRight, blx = c.BottomLeft, bly = c.BottomLeft;
            double w = r.Width, h = r.Height, k;
            if (tlx + trx > w) { k = w / (tlx + trx); tlx *= k; trx *= k; }
            if (blx + brx > w) { k = w / (blx + brx); blx *= k; brx *= k; }
            if (tly + bly > h) { k = h / (tly + bly); tly *= k; bly *= k; }
            if (try_ + bry > h) { k = h / (try_ + bry); try_ *= k; bry *= k; }
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(r.Left + tlx, r.Top), true, true);
                ctx.LineTo(new Point(r.Right - trx, r.Top), false, false);
                if (trx > 0 && try_ > 0) ctx.ArcTo(new Point(r.Right, r.Top + try_), new Size(trx, try_), 0, false, SweepDirection.Clockwise, false, false);
                ctx.LineTo(new Point(r.Right, r.Bottom - bry), false, false);
                if (brx > 0 && bry > 0) ctx.ArcTo(new Point(r.Right - brx, r.Bottom), new Size(brx, bry), 0, false, SweepDirection.Clockwise, false, false);
                ctx.LineTo(new Point(r.Left + blx, r.Bottom), false, false);
                if (blx > 0 && bly > 0) ctx.ArcTo(new Point(r.Left, r.Bottom - bly), new Size(blx, bly), 0, false, SweepDirection.Clockwise, false, false);
                ctx.LineTo(new Point(r.Left, r.Top + tly), false, false);
                if (tlx > 0 && tly > 0) ctx.ArcTo(new Point(r.Left + tlx, r.Top), new Size(tlx, tly), 0, false, SweepDirection.Clockwise, false, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>"Reserve window space" off: the bar lives on the desktop like a widget - behind every window, no reserved strip.</summary>
        public bool OnDesktop { get { return !Placement.ReserveSpace; } }
        bool _zOnDesktop;

        /// <summary>Keep a desktop-level bar on the desktop (called on placement and by the manager's desktop-layer timer).</summary>
        public void PlaceZ()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (!OnDesktop)
            {
                if (_zOnDesktop)
                {
                    // back to a normal bar: on top of everything again
                    _zOnDesktop = false;
                    Topmost = true;
                    if (!_under) Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                }
                return;
            }
            if (!_zOnDesktop)
            {
                _zOnDesktop = true;
                Topmost = false;
                Native.SetWindowPos(_hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            DesktopLayer.Place(_hwnd, false);
        }

        bool _under;
        /// <summary>true while a full-screen app is in front on this bar's monitor: the bar sits behind everything instead of topmost.</summary>
        public bool Under
        {
            get { return _under; }
            set
            {
                if (_under == value) return;
                _under = value;
                if (_mgr != null) _mgr.BarsMoved();
                if (_hwnd == IntPtr.Zero || OnDesktop) return;     // a desktop-level bar is behind windows anyway
                Native.SetWindowPos(_hwnd, value ? Native.HWND_BOTTOM : Native.HWND_TOPMOST, 0, 0, 0, 0,
                                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        double _scale = 1.0;
        int _oL, _oT, _oR, _oB;      // outline width per side in device pixels
        /// <summary>Wanted outline width in whole device pixels (0 when the outline is off).</summary>
        int OutlinePx()
        {
            if (!Placement.ShowBorder) return 0;
            return (int)Math.Max(1, Math.Round(Math.Max(0.5, Math.Min(12, Placement.BorderThickness)) * _scale));
        }

        void BuildModules()
        {
            if (_fx != null) { _fx.Stop(); _fx = null; }
            _fxLayer.Children.Clear();
            _fxAway = _fxSide = 0;
            // icon animations (any layout): icon-only Launcher / Taskbar modules that ask for them
            DockFx.Room(Placement.Modules, Placement.BarHeight, out _fxAway, out _fxSide);
            if (_fxAway > 0 || _fxSide > 0) _fx = new DockFx(_root, _fxLayer, Placement.Edge == "Top");
            _panel.ClipToBounds = _fx == null;      // grown / lifted icons may reach past the bar
            UpdateContentClip();
            _panel.Zones = Placement.Zones && Placement.FullWidth;
            _panel.Children.Clear();
            _taskMods.Clear();
            _trayMods.Clear();
            _metricMods.Clear();
            _sensorMods.Clear();
            _startMods.Clear();
            _runMods.Clear();
            _termMods.Clear();
            if (_typingOwner != null) { var o = _typingOwner; o.StopTyping(); _typingOwner = null; }
            _infoMods.Clear();
            _tileMods.Clear();

            // Build 2: S / M / L text and graph lengths - as set, or stepped down while the bar is too full (see CheckFit)
            double factor = BarText.Factor(Theme.Font);
            double graphScale = _fit <= 0 ? 1 : Math.Max(0.2, 1 - 0.2 * Math.Min(_fit, 4));
            int textCap = Math.Max(0, _fit - 4);
            foreach (var c in Placement.Modules)
            {
                string size = BarText.Fit(BarText.Cap(string.IsNullOrEmpty(c.TextSize) ? "S" : c.TextSize, textCap), Placement.BarHeight);
                c.ShownFontSize = Math.Round(BarText.Px(size) * factor, 1);
                c.ShownTwoLines = size == "M";
                c.ShownGraphScale = graphScale;
            }

            foreach (var cfg in Placement.Modules)
            {
                FrameworkElement el = null;
                HostKind kind = HostKind.Normal;

                switch (cfg.Type)
                {
                    case ModuleTypes.Taskbar:
                        {
                            var m = new TaskbarModule(_mgr, this, cfg);
                            _taskMods.Add(m);
                            el = m;
                            kind = HostKind.Shrink;
                            break;
                        }
                    case ModuleTypes.Tray:
                        {
                            if (_mgr.Tray == null) break;     // the tray host could not be started; the config is kept
                            var m = new TrayModule(_mgr, this, cfg);
                            _trayMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Launcher:
                        el = new LauncherModule(_mgr, this, cfg);
                        break;
                    case ModuleTypes.StartMenu:
                        {
                            var m = new StartMenuModule(_mgr, this, cfg);
                            _startMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Cpu:
                    case ModuleTypes.Memory:
                    case ModuleTypes.Gpu:
                        {
                            var m = new MetricModule(_mgr, this, cfg);
                            _metricMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Clock:
                        el = new ClockModule(_mgr, this, cfg);
                        break;
                    case ModuleTypes.Weather:
                        el = new WeatherModule(_mgr, this, cfg);
                        break;
                    case ModuleTypes.NowPlaying:
                        el = new NowPlayingModule(_mgr, this, cfg);
                        break;
                    case ModuleTypes.Notifications:
                        el = new NotifyModule(_mgr, this, cfg);
                        break;
                    case ModuleTypes.Hwinfo:
                        {
                            var m = new SensorModule(_mgr, this, cfg);
                            _sensorMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Network:
                    case ModuleTypes.Disk:
                    case ModuleTypes.Battery:
                    case ModuleTypes.Volume:
                    case ModuleTypes.Uptime:
                    case ModuleTypes.Processes:
                    case ModuleTypes.Label:
                        {
                            InfoModule m = ModuleFactory.CreateInfo(_mgr, this, cfg);
                            _infoMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Services:
                        {
                            TilesModule m = ModuleFactory.CreateTiles(_mgr, this, cfg);
                            _tileMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.RunBox:
                        {
                            var m = new RunBoxModule(_mgr, this, cfg);
                            _runMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Terminal:
                        {
                            var m = new TerminalModule(_mgr, this, cfg);
                            _termMods.Add(m);
                            el = m;
                            break;
                        }
                    case ModuleTypes.Drives:
                        el = new DrivesModule(_mgr, this, cfg);
                        break;
                    case ModuleTypes.Spacer:
                        el = new Border();
                        kind = HostKind.Spacer;
                        break;
                    default:
                        if (!ModuleTypes.IsBuiltIn(cfg.Type)) el = PluginManager.CreateModule(_mgr, this, cfg);      // a plugin's (or "add it")
                        if (el is InfoModule) _infoMods.Add((InfoModule)el);       // (a plugin's gauge module: Downloads) updated with the others
                        if (el is TilesModule) _tileMods.Add((TilesModule)el);     // (a plugin's tiles: Home Assistant's entities) too
                        break;
                }
                if (el == null) continue;
                TextElement.SetFontSize(el, cfg.ShownFontSize > 0 ? cfg.ShownFontSize : cfg.FollowFontSize ? Theme.FontSize : cfg.FontSize);   // its S / M / L size; text inside inherits it

                if (cfg.Type != ModuleTypes.Taskbar && cfg.Type != ModuleTypes.Spacer) el.VerticalAlignment = VerticalAlignment.Center;
                // Never taller than the bar: a module that needs more room (two lines of text in a slim bar style, a big icon) is
                // shrunk evenly to fit instead of being cut off. Nothing changes when it fits. (Not the taskbar / tray, which size
                // themselves to the bar, nor spacers.)
                if (cfg.Type != ModuleTypes.Taskbar && cfg.Type != ModuleTypes.Tray && kind != HostKind.Spacer)
                    el = new Viewbox
                    {
                        Child = el, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                        MaxHeight = Math.Max(16, Placement.BarHeight - 2), VerticalAlignment = VerticalAlignment.Center
                    };
                var hostEl = new ModuleHost(cfg, el, kind);
                hostEl.AllowDrop = true;
                _panel.Children.Add(hostEl);
            }
            ApplySeparatorLooks();

            foreach (var m in _taskMods) m.Rebuild();
            foreach (var m in _trayMods) m.Rebuild();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(CheckFit));
        }

        /// <summary>
        /// Build 2: a full bar steps down until its modules fit - first the graphs get shorter (80 %, 60 %, 40 %, 20 % of their set
        /// length), then large text becomes M, then everything S. It starts again from the sizes as set whenever the settings or theme
        /// are applied, so a theme with more room brings everything back.
        /// </summary>
        int _fit;
        const int FitMax = 6;

        /// <summary>The bar got its real width (starting up) or a different one (monitor / settings): fit again from the sizes as set.</summary>
        void PanelResized()
        {
            // the room the bar gives its modules - not the panel's own width: a panel that's too full is as wide as its modules, and that
            // changes with every step down (which looked like "the bar changed width", started again from the sizes as set, and looped
            // forever - vShell stuck on its splash screen)
            double room = PanelRoom();
            if (!Placement.FullWidth || room < 100 || Math.Abs(room - _fitWidth) < 2) return;
            if (_fit > 0)
            {
                // a guard as well: never more than a few fresh starts in a row
                DateTime now = DateTime.UtcNow;
                if ((now - _refitAt).TotalSeconds > 10) _refits = 0;
                _refitAt = now;
                if (++_refits > 4) return;
                _fit = 0; BuildModules();
            }
            else CheckFit();
        }
        double _fitWidth;
        int _refits;
        DateTime _refitAt = DateTime.MinValue;

        /// <summary>The width the bar gives its modules (its slot, less the panel's margin).</summary>
        double PanelRoom()
        {
            Rect slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(_panel);
            double w = slot.Width - _panel.Margin.Left - _panel.Margin.Right;
            return w > 0 && !double.IsInfinity(w) ? w : _panel.ActualWidth;
        }

        void CheckFit()
        {
            if (!Placement.FullWidth || _fit >= FitMax || !IsLoaded) return;
            _panel.UpdateLayout();
            // not placed yet (starting up): the bar isn't its real width - wait for it (PanelResized checks again)
            double room = PanelRoom();
            if (room < 100) return;
            _fitWidth = room;
            if (_panel.Overflow <= 0.5) return;
            _fit++;
            Log.Write("Bar " + Placement.Edge + ": too full by " + Math.Round(_panel.Overflow) + " px - stepping down (" + _fit + ")");
            BuildModules();
        }

        /// <summary>
        /// The separator style: Dots / Line before the modules with "Show separator"; Chips = every module in a rounded chip; None = nothing.
        /// </summary>
        void ApplySeparatorLooks()
        {
            string style = Placement.SeparatorStyle ?? "Dots";
            Brush line = Theme.Faded(Theme.Fg, 35), chip = Theme.Faded(Theme.Fg, 12);
            foreach (UIElement c in _panel.Children)
            {
                var h = c as ModuleHost;
                if (h == null) continue;
                bool spacer = h.Kind == HostKind.Spacer;
                bool sep = h.Config.ShowSeparator && !spacer;
                switch (style)
                {
                    case "Line": h.SetLook(null, sep ? line : null, null); break;
                    case "Chips": h.SetLook(null, null, spacer ? null : chip); break;
                    case "None": h.SetLook(null, null, null); break;
                    default: h.SetLook(sep ? Theme.DotBrushes : null, null, null); break;
                }
            }
        }

        public void UpdateStats(SystemStats st, bool addSample)
        {
            foreach (var m in _metricMods) m.Update(st, _mgr.Gpu, addSample);
            foreach (var m in _sensorMods) m.Update(addSample);
            foreach (var m in _infoMods) m.Update(addSample);
            foreach (var m in _tileMods) m.Update();
            CheckPlace();
            if (!Placement.FullWidth) ContentChanged();   // text width can change a little (ApplyPlacement does nothing when it didn't)
        }

        public void RefreshTasks() { foreach (var m in _taskMods) m.Rebuild(); }
        public void RefreshTray() { foreach (var m in _trayMods) m.QueueRebuild(); }

        /// <summary>Called by modules when their size may have changed; only matters for auto-width bars.</summary>
        public void ContentChanged()
        {
            if (!Placement.FullWidth) RequestPlacement();
            // Three zones: the zones' widths depend on what each module wants (a taskbar with more buttons wants more), so lay them out
            // again. Without this a taskbar measured while it had few buttons kept that small width - a centred taskbar stayed squeezed.
            else if (_panel.Zones) _panel.InvalidateMeasure();
        }

        public void RequestPlacement()
        {
            if (!_layoutTimer.IsEnabled) _layoutTimer.Start();
        }

        // =================================================================
        // Placement
        // =================================================================
        Size MeasureContent()
        {
            _root.Measure(new Size(double.PositiveInfinity, Placement.BarHeight + _root.Margin.Top + _root.Margin.Bottom));
            Size d = _root.DesiredSize;
            d = new Size(Math.Max(0, d.Width - _root.Margin.Left - _root.Margin.Right), Math.Max(0, d.Height - _root.Margin.Top - _root.Margin.Bottom));
            _root.InvalidateMeasure();   // make sure the real layout pass re-measures with the final constraint
            return d;
        }

        /// <summary>Its monitor isn't connected (turned off or unplugged): it's hidden until the monitor is back.</summary>
        public bool Displaced { get; private set; }

        /// <summary>Its monitor went away: hide, and give back the space it reserved. It shows again when ApplyPlacement finds the monitor.</summary>
        void HideAway()
        {
            if (!Displaced) Log.Write("Bar '" + Placement.Edge + "': its monitor isn't connected - hidden until it's back");
            Displaced = true;
            if (_appBarRegistered)
            {
                var rem = NewAppBarData();
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref rem);
                _appBarRegistered = false;
                _hasLastReserved = false;
            }
            _placedKey = null;               // placed again in full when the monitor is back
            _ignoredTaskbar = false;         // and the stuck taskbar is looked at (and logged) afresh
            _hasPlacedWin = false;
            _placedOnce = false;
            BarRect = new RECT();            // nothing snaps or lines up with a hidden bar
            if (Native.IsWindowVisible(_hwnd)) Native.ShowWindow(_hwnd, Native.SW_HIDE);
            _mgr.BarsMoved();
        }

        public void ApplyPlacement()
        {
            if (_hwnd == IntPtr.Zero || _dragging || _modDragging) return;

            // its own monitor - or, while that's disconnected, hidden (the setting stays, so it comes back when the monitor returns)
            MonitorInfo own = MonitorHelper.Find(Placement.Monitor, Placement.MonitorKey);
            if (own == null) { HideAway(); return; }
            if (!string.IsNullOrEmpty(Placement.Monitor))
            {
                if (Placement.MonitorKey.Length == 0) Placement.MonitorKey = own.Key;            // remember the monitor itself from now on
                if (!string.Equals(Placement.Monitor, own.DeviceName, StringComparison.OrdinalIgnoreCase)) Placement.Monitor = own.DeviceName;   // came back as another DISPLAYn
            }
            if (Displaced) Log.Write("Bar '" + Placement.Edge + "': its monitor is back - shown again");
            Displaced = false;
            MonitorInfo mon = own;
            MonitorHandle = mon.Handle;
            ActiveMonitorName = mon.DeviceName;
            bool top = Placement.Edge == "Top";

            // "Reserve window space" switched: join / leave Windows' app-bar list
            if (Placement.ReserveSpace && !_appBarRegistered) { RegisterAppBar(); _hasLastReserved = false; }
            else if (!Placement.ReserveSpace && _appBarRegistered)
            {
                var rem = NewAppBarData();
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref rem);
                _appBarRegistered = false;
                _hasLastReserved = false;
            }

            // Step 1: make sure the window is on the target monitor so we read that monitor's DPI.
            if (!_placedOnce || Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST) != mon.Handle)
            {
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, mon.Bounds.Left + 10, mon.Bounds.Top + 10, 60, 24,
                                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | (OnDesktop ? Native.SWP_NOZORDER : 0));
                _placedOnce = true;
            }
            double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
            if (scale < 0.5) scale = 1.0;
            _scale = scale;

            int h = (int)Math.Round(Placement.BarHeight * scale);
            int m = (int)Math.Round(Placement.EdgeMargin * scale);
            int reserve = h + 2 * m;
            int monW = mon.Bounds.Width;

            int w;
            if (Placement.FullWidth) w = monW - 2 * m;
            else
            {
                Size d = MeasureContent();
                w = (int)Math.Ceiling(d.Width * scale);
                w = Math.Min(w, monW - 2 * m);
            }
            w = Math.Max(w, (int)Math.Round(80 * scale));

            // Everything below is decided by what's known now. The same as when the bar was last placed = nothing to do: no app-bar
            // message to Explorer, no window moves (the once-a-second content check from UpdateStats lands here nearly every time).
            // What moves a bar from outside - another app bar (ABN_POSCHANGED), a monitor or DPI change, Explorer restarting, a drag -
            // clears the key first, so those always place it again.
            int bw = OutlinePx();
            int ra = _fx != null ? (int)Math.Ceiling(_fxAway * scale) : 0, rs = _fx != null ? (int)Math.Ceiling(_fxSide * scale) : 0;
            string key = string.Concat(mon.Handle.ToInt64(), "|", mon.Bounds.Left, ",", mon.Bounds.Top, ",", mon.Bounds.Right, ",", mon.Bounds.Bottom, "|", scale, "|",
                                       w, "|", h, "|", m, "|", top, "|", Placement.FullWidth, "|", Placement.CenterX, "|", Placement.KeepCentered, "|", Styles.LayoutOf(Placement), "|",
                                       _appBarRegistered, "|", bw, "|", ra, "|", rs, "|", _under, "|", OnDesktop);
            if (key == _placedKey && StillWherePut()) return;
            _placedKey = key;

            // Reserved strip along the monitor edge (full monitor width).
            RECT rc;
            rc.Left = mon.Bounds.Left;
            rc.Right = mon.Bounds.Right;
            if (top) { rc.Top = mon.Bounds.Top; rc.Bottom = rc.Top + reserve; }
            else { rc.Bottom = mon.Bounds.Bottom; rc.Top = rc.Bottom - reserve; }

            if (_appBarRegistered)
            {
                var abd = NewAppBarData();
                abd.uEdge = top ? Native.ABE_TOP : Native.ABE_BOTTOM;
                abd.rc = rc;
                Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
                // Windows' own taskbar, hidden by vShell but still holding its strip (Explorer couldn't auto-hide it after a monitor
                // woke up): don't step in by it - the bar would sit a taskbar's height up the screen
                bool pushed = top ? abd.rc.Top != rc.Top : abd.rc.Bottom != rc.Bottom;
                if (pushed && _mgr.Settings.HideWindowsTaskbar && ShellServices.ExplorerTaskbarOnEdge(mon.Bounds, top))
                {
                    if (!_ignoredTaskbar)
                        Log.Write("Bar '" + Placement.Edge + "': Windows' hidden taskbar still holds " + Math.Abs(top ? abd.rc.Top - rc.Top : rc.Bottom - abd.rc.Bottom) +
                                  " px of this edge (it couldn't auto-hide) - the bar stays at the edge");
                    _ignoredTaskbar = true;
                    _pushedSince = DateTime.MinValue;
                    abd.rc = rc;
                }
                else
                {
                    _ignoredTaskbar = false;
                    // pushed in with Windows' taskbar meant to be hidden: likely Explorer still settling after the monitors woke (its
                    // taskbar not on the edge yet) - CheckPlace looks again for a while instead of leaving the bar up the screen
                    if (pushed && _mgr.Settings.HideWindowsTaskbar)
                    {
                        if (_pushedSince == DateTime.MinValue)
                        {
                            _pushedSince = DateTime.UtcNow;
                            Log.Write("Bar '" + Placement.Edge + "': pushed " + Math.Abs(top ? abd.rc.Top - rc.Top : rc.Bottom - abd.rc.Bottom) +
                                      " px in from the edge by another app bar (Explorer's taskbars: " + ShellServices.DescribeTaskbars() + ") - looking again for 2 minutes");
                        }
                    }
                    else _pushedSince = DateTime.MinValue;
                }
                if (top) abd.rc.Bottom = abd.rc.Top + reserve; else abd.rc.Top = abd.rc.Bottom - reserve;
                RECT asked = abd.rc;
                if (!_hasLastReserved || !SameRect(asked, _lastReserved))
                {
                    Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
                    _lastReserved = asked;        // what was asked for: the same again needs no new SETPOS
                    _hasLastReserved = true;
                    _lastGranted = abd.rc;
                    if (!_ignoredTaskbar && !SameRect(asked, _lastGranted) && _mgr.Settings.HideWindowsTaskbar && _pushedSince == DateTime.MinValue)
                    {
                        _pushedSince = DateTime.UtcNow;      // Explorer moving its taskbars between the two messages: CheckPlace looks again
                        Log.Write("Bar '" + Placement.Edge + "': Windows gave a strip " + Math.Abs(top ? _lastGranted.Top - asked.Top : asked.Bottom - _lastGranted.Bottom) +
                                  " px in from the one asked for (Explorer's taskbars: " + ShellServices.DescribeTaskbars() + ") - looking again for 2 minutes");
                    }
                }
                // ABM_SETPOS steps in by the stuck taskbar's strip all over again (0.38.249: the bars sat 48 px up although ABM_QUERYPOS's push
                // was ignored) - with the taskbar ignored the bar goes where it asked
                rc = _ignoredTaskbar ? asked : _lastGranted;
            }

            int x;
            if (Placement.FullWidth) x = mon.Bounds.Left + m;
            else
            {
                // free horizontal placement: anywhere along the edge, kept fully on the monitor
                bool centred = Placement.KeepCentered && Styles.LayoutOf(Placement) == Styles.Dock;
                double cx = mon.Bounds.Left + (centred ? 0.5 : Placement.CenterX) * monW;
                x = (int)Math.Round(cx - w / 2.0);
                int minX = mon.Bounds.Left + m;
                int maxX = mon.Bounds.Right - m - w;
                if (maxX < minX) maxX = minX;
                x = Math.Max(minX, Math.Min(maxX, x));
            }
            int y = top ? rc.Top + m : rc.Bottom - m - h;

            // outline per side: full width where there is room, shrinking to nothing where the bar touches the monitor edge
            _oL = Math.Max(0, Math.Min(bw, x - mon.Bounds.Left));
            _oR = Math.Max(0, Math.Min(bw, mon.Bounds.Right - (x + w)));
            _oT = Math.Max(0, Math.Min(bw, y - mon.Bounds.Top));
            _oB = Math.Max(0, Math.Min(bw, mon.Bounds.Bottom - (y + h)));
            UpdateCorners();     // also: the edge may have changed (bar dragged to the other side)
            // dock animations: transparent room above (or below) and beside the dock for the grown icons; clicks go through it and
            // only the bar itself is reserved
            // the side room never reaches onto the next monitor (a full-width bar gets none)
            int rl = Math.Max(0, Math.Min(rs, x - _oL - mon.Bounds.Left)), rr = Math.Max(0, Math.Min(rs, mon.Bounds.Right - (x + w + _oR)));
            _host.Margin = new Thickness(rl / scale, top ? 0 : ra / scale, rr / scale, top ? ra / scale : 0);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x - _oL - rl, y - _oT - (top ? 0 : ra), w + _oL + _oR + rl + rr, h + _oT + _oB + ra,
                                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | (_under || OnDesktop ? Native.SWP_NOZORDER : 0));
            PlaceZ();
            // what Windows actually made of it (it may round or clamp): the once-a-second check compares with this
            _hasPlacedWin = Native.GetWindowRect(_hwnd, out _placedWin);
            _placedMon = mon.Bounds;
            BarRect = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
            _mgr.OnBarPlaced(this, BarRect);
        }

        RECT _placedWin, _placedMon;
        bool _hasPlacedWin, _ignoredTaskbar;
        DateTime _pushedSince = DateTime.MinValue, _lookedAgain;

        /// <summary>The window is still where ApplyPlacement put it (nothing else moved or resized it).</summary>
        bool StillWherePut()
        {
            if (!_hasPlacedWin) return true;
            RECT now;
            if (!Native.GetWindowRect(_hwnd, out now) || SameRect(now, _placedWin)) return true;
            Log.Write("Bar '" + Placement.Edge + "': moved by something else (" + now.Left + "," + now.Top + " " + (now.Right - now.Left) + "x" + (now.Bottom - now.Top) +
                      ", placed at " + _placedWin.Left + "," + _placedWin.Top + " " + (_placedWin.Right - _placedWin.Left) + "x" + (_placedWin.Bottom - _placedWin.Top) + ") - put back");
            return false;
        }

        /// <summary>
        /// Once a second (from UpdateStats): put the bar back if something moved it - Windows putting windows back where they were when
        /// a monitor returns, a late DPI resize - or its monitor's size changed without a display-change message (one that wakes up
        /// at a lower resolution first). Before this a bar could stay part-way up the screen after a monitor was turned off and on.
        /// </summary>
        void CheckPlace()
        {
            if (_hwnd == IntPtr.Zero || Displaced || _dragging || _modDragging || !_hasPlacedWin) return;
            MonitorInfo own = MonitorHelper.Find(Placement.Monitor, Placement.MonitorKey);
            bool monChanged = own != null && !SameRect(own.Bounds, _placedMon);
            if (monChanged) Log.Write("Bar '" + Placement.Edge + "': its monitor is now " + own.Bounds.Width + "x" + own.Bounds.Height + " at " + own.Bounds.Left + "," + own.Bounds.Top + " - placed again");
            // pushed in from the edge (see ApplyPlacement): ask Windows again every 5 s for the first 2 minutes
            var now = DateTime.UtcNow;
            bool lookAgain = _pushedSince != DateTime.MinValue && now - _pushedSince < TimeSpan.FromMinutes(2) && now - _lookedAgain >= TimeSpan.FromSeconds(5);
            if (lookAgain) { _lookedAgain = now; _hasLastReserved = false; }      // the strip asked for again, too
            if (own == null || monChanged || lookAgain || !StillWherePut()) { _placedKey = null; ApplyPlacement(); }
            if (lookAgain && _pushedSince == DateTime.MinValue) Log.Write("Bar '" + Placement.Edge + "': no longer pushed in - back at the edge");
        }

        static bool SameRect(RECT a, RECT b)        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        // =================================================================
        // Dragging the bar  (drop = nearest monitor + nearest top/bottom edge; horizontal position is kept as dropped)
        // and Ctrl+dragging a module (reorders it within the bar)
        // =================================================================
        static ModuleHost FindHost(DependencyObject d)
        {
            while (d != null)
            {
                var h = d as ModuleHost;
                if (h != null) return h;
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) d = VisualTreeHelper.GetParent(d);
                else d = LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        void Root_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Native.CtrlDown()) return;
            ModuleHost host = FindHost(e.OriginalSource as DependencyObject);
            if (host == null) return;
            _modHost = host;
            _modStart = e.GetPosition(_root);
            _modPending = true;
            _modDragging = false;
            _modDx = 0;
            _root.CaptureMouse();
            e.Handled = true;
        }

        void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            _mgr.CloseOpenPopup();      // a click on empty bar space dismisses an open tray / launcher popup
            _dragStart = Native.CursorPos();
            Native.GetWindowRect(_hwnd, out _dragRect);
            _dragPending = true;
            _root.CaptureMouse();
        }

        void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (_modPending || _modDragging) { ModuleMove(e); return; }
            if (!_dragPending && !_dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }

            POINT c = Native.CursorPos();
            int dx = c.X - _dragStart.X, dy = c.Y - _dragStart.Y;
            if (!_dragging)
            {
                if (Math.Abs(dx) < 6 && Math.Abs(dy) < 6) return;
                _dragging = true;
                Opacity = 0.85;
            }
            Native.SetWindowPos(_hwnd, IntPtr.Zero, _dragRect.Left + dx, _dragRect.Top + dy, 0, 0,
                                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        void ModuleMove(MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }
            if (_modHost == null) return;

            Point p = e.GetPosition(_root);
            double dx = p.X - _modStart.X;
            if (!_modDragging)
            {
                if (Math.Abs(dx) < 5) return;
                _modDragging = true;
                _modHost.Opacity = 0.7;
                Panel.SetZIndex(_modHost, 50);
            }

            int idx = _panel.Children.IndexOf(_modHost);
            Rect slot = _panel.SlotOf(idx);
            double minDx = -slot.Left;
            double maxDx = _panel.ActualWidth - slot.Right;
            if (maxDx < minDx) maxDx = minDx;
            dx = Math.Max(minDx, Math.Min(maxDx, dx));
            _modDx = dx;
            _modHost.RenderTransform = new TranslateTransform(dx, 0);
        }

        void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_modPending || _modDragging)
            {
                bool was = _modDragging;
                ModuleHost host = _modHost;
                double dx = _modDx;
                _root.ReleaseMouseCapture();     // raises LostMouseCapture -> CancelDrag(); locals above survive that
                ResetModuleDrag(host);
                if (was && host != null) CommitModuleMove(host, dx);
                return;
            }

            if (!_dragPending && !_dragging) return;
            bool wasDragging = _dragging;
            POINT c = Native.CursorPos();
            int dragDx = c.X - _dragStart.X;
            int barLeft = _dragRect.Left + dragDx;
            int barWidth = _dragRect.Width;
            _root.ReleaseMouseCapture();
            CancelDrag();
            if (!wasDragging) return;

            MonitorInfo mon = MonitorHelper.FromPoint(c.X, c.Y);
            if (mon == null) { ApplyPlacement(); return; }
            Placement.Monitor = mon.DeviceName;
            Placement.MonitorKey = mon.Key;
            Placement.Edge = (c.Y < mon.Bounds.Top + mon.Bounds.Height / 2) ? "Top" : "Bottom";
            double centre = barLeft + barWidth / 2.0;
            Placement.CenterX = Math.Max(0.0, Math.Min(1.0, (centre - mon.Bounds.Left) / Math.Max(1, mon.Bounds.Width)));
            _mgr.OnBarMoved();
        }

        void ResetModuleDrag(ModuleHost host)
        {
            _modPending = false;
            _modDragging = false;
            _modDx = 0;
            if (host != null)
            {
                host.RenderTransform = null;
                host.Opacity = 1.0;
                Panel.SetZIndex(host, 0);
            }
            _modHost = null;
        }

        void CommitModuleMove(ModuleHost host, double dx)
        {
            int from = _panel.Children.IndexOf(host);
            if (from < 0) return;
            Rect slot = _panel.SlotOf(from);
            double centre = slot.Left + dx + slot.Width / 2.0;
            if (_panel.Zones) { CommitZoneMove(host, centre); return; }

            var others = new List<ModuleHost>();
            int to = 0;
            for (int i = 0; i < _panel.Children.Count; i++)
            {
                var h = _panel.Children[i] as ModuleHost;
                if (h == null || h == host) continue;
                others.Add(h);
                Rect r = _panel.SlotOf(i);
                if (r.Left + r.Width / 2.0 < centre) to++;
            }
            if (to == from) return;     // dropped back where it was

            var list = Placement.Modules;
            list.Remove(host.Config);
            int insertAt;
            if (others.Count == 0) insertAt = 0;
            else if (to == 0) insertAt = Math.Max(0, list.IndexOf(others[0].Config));
            else insertAt = list.IndexOf(others[to - 1].Config) + 1;
            list.Insert(Math.Max(0, Math.Min(list.Count, insertAt)), host.Config);

            _mgr.SettingsChanged();
        }

        /// <summary>Three zones: a Ctrl+drag can move a module into another zone, and places it among that zone's modules.</summary>
        void CommitZoneMove(ModuleHost host, double centre)
        {
            int zi = _panel.ZoneAt(centre);
            string zone = ModuleTypes.Zones[zi];
            var list = Placement.Modules;
            // the first module of that zone (left to right on screen) whose middle is past the drop point goes after it
            ModuleConfig before = null, last = null;
            double best = double.MaxValue;
            for (int i = 0; i < _panel.Children.Count; i++)
            {
                var h = _panel.Children[i] as ModuleHost;
                if (h == null || h == host || h.ZoneIndex != zi) continue;
                Rect r = _panel.SlotOf(i);
                double mid = r.Left + r.Width / 2.0;
                if (mid > centre && mid < best) { best = mid; before = h.Config; }
                last = h.Config;
            }
            bool sameZone = host.Config.Zone == zone || (string.IsNullOrEmpty(host.Config.Zone) && zi == 0);
            list.Remove(host.Config);
            int at;
            if (before != null) at = list.IndexOf(before);
            else if (last != null) at = list.IndexOf(last) + 1;
            else at = list.Count;
            list.Insert(Math.Max(0, Math.Min(list.Count, at)), host.Config);
            host.Config.Zone = zone;
            if (!sameZone) Log.Write("Module " + host.Config.Type + " moved to the " + zone + " zone");
            _mgr.SettingsChanged();
        }

        void CancelDrag()
        {
            bool was = _dragging;
            _dragPending = false;
            _dragging = false;
            if (was) _placedKey = null;     // the window was moved by hand: the next ApplyPlacement puts it where it belongs
            Opacity = 1.0;
            if (_modPending || _modDragging) ResetModuleDrag(_modHost);
            if (was) RequestPlacement();
        }

        // =================================================================
        // Context menu
        // =================================================================
        void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            var m = new ContextMenu();
            Add(m, "Settings…", delegate { _mgr.OpenSettings(Placement); });
            if (Styles.LayoutOf(Placement) == Styles.Dock)
            {
                var centre = new MenuItem { Header = "Keep the dock centred on its monitor", IsCheckable = true, IsChecked = Placement.KeepCentered };
                centre.Click += delegate { Placement.KeepCentered = !Placement.KeepCentered; _mgr.OnBarMoved(); };
                m.Items.Add(centre);
            }
            m.Items.Add(new MenuItem { Header = "Tip: Ctrl+drag a module to move it", IsEnabled = false });
            m.Items.Add(new Separator());
            Add(m, "Restart vShell", delegate { _mgr.RestartVShell(); });
            Add(m, "Exit vShell", delegate { _mgr.Shutdown(); });
            m.PlacementTarget = _root;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        static void Add(ContextMenu m, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }
    }
}
