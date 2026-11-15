using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Themed popup used by the tray and launcher. A real (borderless, topmost) window positioned in device pixels:
    /// directly above a bottom bar / below a top bar, centred on the clicked element, so it can never cover the bar.
    /// Closes when it loses focus, on Esc, or when another popup opens. Set Pinned while a drag started from it is running.
    /// </summary>
    public sealed class PopupWindow : Window
    {
        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly FrameworkElement _anchor;
        readonly Border _card;
        readonly Border _shadow;     // the drop shadow is its own layer behind the card, so changes inside the card don't redraw it
        readonly Grid _shadowHost = new Grid { IsHitTestVisible = false };     // clipped to outside the card (a see-through card shows no shadow through it)
        readonly Glass _glass;       // the background effect (blur / frosted), under the card
        readonly Border _sheen = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };     // the widget style's glass edge
        readonly Theme _t;
        IntPtr _hwnd;
        bool _closed, _syncing;
        int _wantX, _wantY;
        bool _haveWant;
        DispatcherTimer _settle;
        DispatcherTimer _away;
        DispatcherTimer _check;
        bool _prevDown, _loaded;
        string _backdrop = "Off";

        /// <summary>true while the popup is shown (set before Show, cleared on close).</summary>
        [PluginApi] public bool IsOpen { get; private set; }
        /// <summary>While true the popup stays open even if it is deactivated (a drag-and-drop started from it is running).</summary>
        [PluginApi] public bool Pinned { get; set; }
        /// <summary>The background effect: null = the module's / host's (see PopupGlass.ModeFor), "Off", "Blur" or "Acrylic".</summary>
        public string BackdropMode { get; set; }
        /// <summary>0.38.200: a flyout - placed beside this element (a row in another popup: to its right, or its left when there's no
        /// room), its first row level with it, instead of next to the bar. Set before Show.</summary>
        public FrameworkElement Beside { get; set; }
        /// <summary>0.38.200: never takes the focus (a flyout of a menu that keeps it). Set before Show.</summary>
        public bool NoActivate { get; set; }
        /// <summary>0.38.202: other windows that are part of this popup (a menu's flyouts): a click on one, or one coming in front, doesn't
        /// close it.</summary>
        public Func<IntPtr, bool> Partners { get; set; }
        /// <summary>0.38.202: say in the log why it closed by itself (a click away, lost the focus).</summary>
        public string LogCloseAs { get; set; }

        bool IsPartner(IntPtr h)
        {
            try { return h != IntPtr.Zero && Partners != null && Partners(h); }
            catch { return false; }
        }

        void CloseBecause(string why)
        {
            if (LogCloseAs != null) Log.Write(LogCloseAs + " closed: " + why);
            CloseSafe();
        }

        [PluginApi] public PopupWindow(BarManager mgr, IModuleHost bar, FrameworkElement anchor, Theme t, FrameworkElement content)
        {
            _mgr = mgr; _bar = bar; _anchor = anchor; _t = t;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000; Top = -32000;
            Opacity = 0;                      // invisible until it has been moved to its final place
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Title = "vShell popup";

            _card = new Border
            {
                Background = t.PopupBg,
                BorderBrush = t.Track,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Child = content
            };
            _shadow = new Border
            {
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
                CacheMode = new BitmapCache()
            };
            TextElement.SetForeground(_card, t.Fg);
            TextElement.SetFontFamily(_card, t.Font);
            TextElement.SetFontSize(_card, t.FontSize);
            // transparent margin around the card leaves room for the shadow
            var layers = new Grid();
            _shadowHost.Children.Add(_shadow);
            layers.Children.Add(_shadowHost);
            _glass = new Glass(mgr);
            layers.Children.Add(_glass);
            layers.Children.Add(_card);
            layers.Children.Add(_sheen);
            _card.SizeChanged += delegate { UpdateShadowClip(); _glass.Update(); };
            Content = new Border { Padding = new Thickness(8), Child = layers };

            Loaded += delegate
            {
                _loaded = true;
                ApplyBackdrop();
                Reposition();
                if (!NoActivate) Activate();
                // already where it belongs (the usual case): show it now instead of waiting for the position to settle
                RECT now;
                if (Native.GetWindowRect(_hwnd, out now) && _haveWant && now.Left == _wantX && now.Top == _wantY) Opacity = 1;
                // WPF can still resize/move the window while it settles (DPI, SizeToContent). Stay invisible until the
                // position has stopped changing, so the popup never shows up in the wrong place and jumps.
                _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
                int n = 0, lastX = int.MinValue, lastY = int.MinValue, lastW = -1, lastH = -1;
                _settle.Tick += delegate
                {
                    Reposition();
                    RECT r;
                    Native.GetWindowRect(_hwnd, out r);
                    bool stable = r.Left == lastX && r.Top == lastY && r.Width == lastW && r.Height == lastH && r.Left == _wantX && r.Top == _wantY;
                    lastX = r.Left; lastY = r.Top; lastW = r.Width; lastH = r.Height;
                    n++;
                    if (stable || n >= 8) { Opacity = 1; _glass.Update(); }
                    if (n >= 8) _settle.Stop();
                };
                _settle.Start();
                StartAwayWatch();
            };
            // the content changed size (another tab, more rows): place it again against the module - now, and once more after
            // Windows has the new window size (the first call can still see the old one)
            SizeChanged += delegate
            {
                Reposition();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reposition));
            };
            LocationChanged += delegate { if (!_syncing && _haveWant) Place(_wantX, _wantY, false); };
            Deactivated += delegate
            {
                if (Pinned) return;
                IntPtr fg = Native.GetForegroundWindow();
                // still the window in front: Windows says "deactivated" for a moment when you click in a no-activate window beside it
                // (a right-click in the Cascading Start menu's flyouts did that) - not a click away
                if (fg == _hwnd || IsPartner(fg)) return;
                CloseBecause("lost the focus to " + WindowText(fg));
            };
            PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; CloseSafe(); } };
            IsOpen = true;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);

        /// <summary>
        /// Close when the user clicks anywhere away from the popup. Deactivation alone is not enough: the desktop, widgets and
        /// desktop icons are no-activate windows, so clicking them never takes focus away from the popup. Clicks on windows of
        /// this program that are topmost (the bar itself, context menus) are left to their own handlers.
        /// </summary>
        void StartAwayWatch()
        {
            _prevDown = true;     // ignore the click that opened us
            _away = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _away.Tick += delegate
            {
                if (_closed) { _away.Stop(); return; }
                bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;
                bool edge = down && !_prevDown;
                _prevDown = down;
                if (!edge || Pinned) return;
                try
                {
                    POINT p = Native.CursorPos();
                    IntPtr h = WindowFromPoint(p);
                    IntPtr root = h == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(h, 2 /*GA_ROOT*/);
                    if (root == _hwnd || IsPartner(root)) return;
                    if (root != IntPtr.Zero)
                    {
                        uint pid; Native.GetWindowThreadProcessId(root, out pid);
                        bool ours = pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        bool topmost = (Native.GetWindowLongAuto(root, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
                        if (ours && topmost) return;
                    }
                    CloseBecause("a click on " + WindowText(root));
                }
                catch { }
            };
            _away.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int max);

        /// <summary>For the log: a window's class, and whether it is one of vShell's.</summary>
        static string WindowText(IntPtr h)
        {
            if (h == IntPtr.Zero) return "nothing";
            try
            {
                var sb = new System.Text.StringBuilder(128);
                GetClassName(h, sb, sb.Capacity);
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                bool topmost = (Native.GetWindowLongAuto(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
                return sb + (pid == (uint)Environment.ProcessId ? " (vShell" + (topmost ? ", topmost)" : ")") : " (another program)");
            }
            catch { return "?"; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        /// <summary>
        /// Take keyboard focus even when another program is in front (needed when the popup is opened by a hotkey rather than a click on
        /// the bar, because Windows normally refuses focus changes from background programs).
        /// </summary>
        /// <summary>The bar / widget it opened from.</summary>
        public IModuleHost Host { get { return _bar; } }

        /// <summary>Back on top of everything (its bar was just brought over a full-screen app).</summary>
        public void BringToTop()
        {
            if (_hwnd == IntPtr.Zero || _closed) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void ForceForeground()
        {
            if (_hwnd == IntPtr.Zero || _closed) return;
            try
            {
                uint pid;
                IntPtr fg = Native.GetForegroundWindow();
                uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out pid);
                uint me = GetCurrentThreadId();
                bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
                try { Native.SetForegroundWindow(_hwnd); }
                finally { if (attached) AttachThreadInput(me, fgThread, false); }
                Activate();
            }
            catch (Exception ex) { Log.Write("ForceForeground: " + ex.Message); }
        }

        /// <summary>
        /// Look like a widget of the current widget style: the theme's background (colour / gradient / opacity), the style's corners,
        /// border, shadow and padding (used by the Application launcher's group menus).
        /// </summary>
        [PluginApi] public void UseWidgetStyle(Theme t, WidgetStyle ws)
        {
            if (t == null || ws == null) return;
            Brush bg = t.MakeBg(-1);
            _card.Background = bg;
            _card.CornerRadius = new CornerRadius(Math.Max(0, ws.CornerRadius));
            _card.BorderThickness = new Thickness(ws.ShowBorder ? Math.Max(0.5, Math.Min(12, ws.BorderThickness)) : 0);
            _card.BorderBrush = Theme.Faded(t.Resolve(ws.BorderColor, t.MainAccent), ws.BorderOpacity);
            _card.Padding = new Thickness(Math.Max(2, ws.Padding));
            _shadow.CornerRadius = _card.CornerRadius;
            _glass.SetCorners(_card.CornerRadius);
            EdgeFx.GlassEdge(_sheen, Styles.GlassOf(ws), _card.CornerRadius);
            _shadowHost.Visibility = ws.ShowShadow ? Visibility.Visible : Visibility.Collapsed;
            var fx = _shadow.Effect as DropShadowEffect;
            if (fx != null) { fx.BlurRadius = 14; fx.Opacity = 0.4; }
            UpdateShadowClip();
        }

        /// <summary>
        /// The most height (DIPs) the content can take and still fit between the bar and the far edge of its monitor, after the
        /// card's padding / border and the shadow margin. Call it after UseWidgetStyle; a content taller than this should scroll.
        /// </summary>
        [PluginApi] public double ContentRoom()
        {
            try
            {
                RECT bar;
                if (!Native.GetWindowRect(_bar.Handle, out bar)) return double.PositiveInfinity;
                double cx = (bar.Left + bar.Right) / 2.0;
                try
                {
                    Point a1 = _anchor.PointToScreen(new Point(0, 0));
                    Point a2 = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
                    cx = (a1.X + a2.X) / 2.0;
                }
                catch { }
                MonitorInfo mon = MonitorHelper.FromPoint((int)cx, (bar.Top + bar.Bottom) / 2);
                if (mon == null) return double.PositiveInfinity;
                int px = _bar.PopupsBelow ? mon.Bounds.Bottom - bar.Bottom : bar.Top - mon.Bounds.Top;
                double scale = VisualTreeHelper.GetDpi(_anchor).DpiScaleY;
                double chrome = 16 + _card.Padding.Top + _card.Padding.Bottom + _card.BorderThickness.Top + _card.BorderThickness.Bottom;
                return Math.Max(120, px / scale - chrome - 8);     // a little gap from the screen edge
            }
            catch { return double.PositiveInfinity; }
        }

        /// <summary>Blur / frosted under the card when the bar / widget has it (and the module hasn't turned it off for its popup).</summary>
        void ApplyBackdrop()
        {
            string mode = "Off";
            try { mode = BackdropMode ?? PopupGlass.ModeFor(_mgr, _bar, _anchor); } catch (Exception ex) { Log.Write("Popup backdrop: " + ex.Message); }
            _backdrop = mode;
            _glass.SetCorners(_card.CornerRadius);
            _glass.Refresh(mode);
            if (mode != "Off") PopupGlass.See(_t, _card);
        }

        /// <summary>Only the part of the shadow outside the card is drawn.</summary>
        void UpdateShadowClip()
        {
            double w = _card.ActualWidth, h = _card.ActualHeight;
            if (w < 1 || h < 1) return;
            double r = _card.CornerRadius.TopLeft;
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-30, -30, w + 60, h + 60)),
                                            new RectangleGeometry(new Rect(0, 0, w, h), r, r));
            clip.Freeze();
            _shadowHost.Clip = clip;
        }

        /// <summary>Replace the content (e.g. the tray icons changed while the popup is open).</summary>
        [PluginApi] public void SetContent(FrameworkElement content)
        {
            _card.Child = content;
        }

        [PluginApi] public void CloseSafe()
        {
            if (_closed) return;
            _closed = true;
            IsOpen = false;
            _lastAnchor = _anchor;
            _lastClosedAt = DateTime.UtcNow;
            try { Close(); } catch { }
        }

        static FrameworkElement _lastAnchor;
        static DateTime _lastClosedAt = DateTime.MinValue;

        /// <summary>
        /// A popup opened from this element closed a moment ago. Pressing on the module that opened it makes the popup lose the focus and
        /// close before the click arrives - from a widget especially - so that click should leave it closed, not open it again.
        /// </summary>
        public static bool ClosedJustNow(FrameworkElement anchor)
        {
            return anchor != null && anchor == _lastAnchor && (DateTime.UtcNow - _lastClosedAt).TotalMilliseconds < 400;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            if (NoActivate)
                Native.SetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE);
            WinStyle.HideFromTasks(_hwnd, false);
            _mgr.Tasks.Ignored.Add(_hwnd);
            ApplyFix();

            // start on the bar's monitor so WPF lays the content out with that monitor's DPI
            RECT bar;
            Native.GetWindowRect(_bar.Handle, out bar);
            int sx = bar.Left + 4, sy = bar.Top + 4;
            try
            {
                Point a = _anchor.PointToScreen(new Point(0, 0));
                sx = (int)a.X;
            }
            catch { }
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, sx, sy, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            // a moment after opening, make sure it can be seen (popups on one monitor once opened fully see-through until a restart)
            _check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _check.Tick += delegate { _check.Stop(); CheckShown(); };
            _check.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        }

        /// <summary>Still invisible (never laid out, opacity left at 0, or drawing nothing at all): say why in the log and show it.</summary>
        void CheckShown()
        {
            if (_closed || _hwnd == IntPtr.Zero) return;
            try
            {
                bool blank = DrawsNothing();
                if (_loaded && Opacity >= 1 && !blank)
                {
                    if (_healthy == null) _healthy = WpfState();
                    return;
                }
                RECT r;
                Native.GetWindowRect(_hwnd, out r);
                Log.Write("Popup from " + (_anchor == null ? "?" : _anchor.GetType().Name) + " can't be seen: loaded " + _loaded + ", opacity " + Opacity +
                          ", draws " + (blank ? "nothing" : "something") + ", window " + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height +
                          ", content " + Math.Round(_card.ActualWidth) + "x" + Math.Round(_card.ActualHeight) + ", backdrop " + _backdrop +
                          ", render " + RenderCapability.Tier / 0x10000 + ", fix " + _fix + " | " + WpfState() + " | healthy: " + (_healthy ?? "none seen yet"));
                // try one thing at a time and say which one brings it back - the next popups start with that (until a restart)
                var steps = new Tuple<string, Func<bool>>[]
                {
                    Tuple.Create("shown again", (Func<bool>)delegate { Opacity = 1; _card.InvalidateVisual(); InvalidateVisual(); Reposition(); return true; }),
                    Tuple.Create("WPF's drawing turned back on", (Func<bool>)EnableRenderTarget),
                    Tuple.Create("without the shadow's cache", (Func<bool>)delegate { if (_shadow.CacheMode == null) return false; _shadow.CacheMode = null; return true; }),
                    Tuple.Create("without the shadow", (Func<bool>)delegate { if (_shadow.Effect == null) return false; _shadow.Effect = null; _shadowHost.Visibility = Visibility.Collapsed; return true; }),
                    Tuple.Create("without the graphics card", (Func<bool>)delegate { if (RenderModeNow() == RenderMode.SoftwareOnly) return false; DrawInSoftware(); return true; })
                };
                TryStep(steps, 0);
            }
            catch (Exception ex) { Log.Write("Popup check: " + ex.Message); }
        }

        void TryStep(Tuple<string, Func<bool>>[] steps, int i)
        {
            if (_closed) return;
            if (i >= steps.Length) { Log.Write("Popup: still can't be seen after everything | " + WpfState()); return; }
            bool tried;
            try { tried = steps[i].Item2(); }
            catch (Exception ex) { Log.Write("Popup: " + steps[i].Item1 + " failed: " + ex.Message); tried = false; }
            if (!tried) { TryStep(steps, i + 1); return; }
            InvalidateVisual();
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            wait.Tick += delegate
            {
                wait.Stop();
                if (_closed) return;
                if (DrawsNothing()) { Log.Write("Popup: " + steps[i].Item1 + " - still can't be seen"); TryStep(steps, i + 1); return; }
                Log.Write("Popup: can be seen now (" + steps[i].Item1 + ") | " + WpfState());
                if (i >= 2) _fix = Math.Max(_fix, i);
            };
            wait.Start();
        }

        /// <summary>
        /// The step that made a blank popup show (2 = no shadow cache, 3 = no shadow, 4 = no graphics card): the next popups start with
        /// it, until a restart.
        /// </summary>
        static int _fix;
        /// <summary>WPF's state for the first popup that showed fine (to compare with a blank one in the log).</summary>
        static string _healthy;

        void ApplyFix()
        {
            if (_fix >= 2) _shadow.CacheMode = null;
            if (_fix >= 3) { _shadow.Effect = null; _shadowHost.Visibility = Visibility.Collapsed; }
            if (_fix >= 4) DrawInSoftware();
        }

        HwndTarget Target()
        {
            var src = HwndSource.FromHwnd(_hwnd);
            return src == null ? null : src.CompositionTarget;
        }

        RenderMode RenderModeNow()
        {
            HwndTarget t = Target();
            return t == null ? RenderMode.Default : t.RenderMode;
        }

        void DrawInSoftware()
        {
            HwndTarget t = Target();
            if (t != null) t.RenderMode = RenderMode.SoftwareOnly;
        }

        /// <summary>WPF stopped drawing into the window (it thinks it was hidden): turn it back on. false = it wasn't off.</summary>
        bool EnableRenderTarget()
        {
            HwndTarget t = Target();
            if (t == null || !(TargetField(t, "_isRenderTargetEnabled") is bool) || (bool)TargetField(t, "_isRenderTargetEnabled")) return false;
            foreach (MethodInfo m in typeof(HwndTarget).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (m.Name != "UpdateWindowSettings") continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length == 1) { m.Invoke(t, new object[] { true }); return true; }
                if (p.Length == 2) { m.Invoke(t, new object[] { true, null }); return true; }
            }
            return false;
        }

        static object TargetField(HwndTarget t, string name)
        {
            FieldInfo f = typeof(HwndTarget).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(t);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

        /// <summary>WPF's own drawing state for this window (its HwndTarget's flags) and the window's, for the log.</summary>
        string WpfState()
        {
            try
            {
                HwndTarget t = Target();
                if (t == null) return "no render target";
                var sb = new System.Text.StringBuilder("mode " + t.RenderMode);
                foreach (string f in new[] { "_isRenderTargetEnabled", "_isSuspended", "_isSessionDisconnected", "_isMinimized", "_displayDevicesAvailable",
                                             "_needsRePresentOnWake", "_hasRePresentedSinceWake", "_usesPerPixelOpacity", "_disableCookie" })
                    sb.Append(", ").Append(f.TrimStart('_')).Append(' ').Append(TargetField(t, f));
                sb.Append(", visible ").Append(IsWindowVisible(_hwnd));
                sb.Append(", exstyle 0x").Append(Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE).ToString("X"));
                return sb.ToString();
            }
            catch (Exception ex) { return "state: " + ex.Message; }
        }

        /// <summary>The window's picture as Windows has it is all black / see-through.</summary>
        bool DrawsNothing()
        {
            RECT r;
            if (!Native.GetWindowRect(_hwnd, out r) || r.Width <= 0 || r.Height <= 0) return true;
            var bmi = new BitmapInfoHeader { Size = 40, Width = r.Width, Height = -r.Height, Planes = 1, BitCount = 32 };
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero), bits, bmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                bmp = CreateDIBSection(dc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (bmp == IntPtr.Zero) return false;
                old = SelectObject(dc, bmp);
                if (!PrintWindow(_hwnd, dc, 2 /*PW_RENDERFULLCONTENT*/)) return false;
                var px = new int[r.Width * r.Height];
                System.Runtime.InteropServices.Marshal.Copy(bits, px, 0, px.Length);
                for (int i = 0; i < px.Length; i += 7)
                    if ((px[i] & 0xFFFFFF) != 0) return false;
                return true;
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(dc, old);
                if (bmp != IntPtr.Zero) DeleteObject(bmp);
                DeleteDC(dc);
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(Reposition), DispatcherPriority.Background);
        }

        protected override void OnClosed(EventArgs e)
        {
            IsOpen = false;
            _closed = true;
            if (_settle != null) _settle.Stop();
            if (_away != null) _away.Stop();
            if (_check != null) _check.Stop();
            _mgr.Tasks.Ignored.Remove(_hwnd);
            base.OnClosed(e);
        }

        /// <summary>Place the window (already sized) next to the bar: never overlapping it, centred on the anchor, kept on the monitor.</summary>
        void Reposition()
        {
            if (_hwnd == IntPtr.Zero || _closed) return;
            RECT me, bar;
            if (!Native.GetWindowRect(_hwnd, out me)) return;
            Native.GetWindowRect(_bar.Handle, out bar);
            int pw = me.Width, ph = me.Height;
            try
            {
                // WPF's size is the new one even when the window rectangle hasn't caught up yet
                DpiScale d = VisualTreeHelper.GetDpi(this);
                if (ActualWidth > 0 && ActualHeight > 0)
                {
                    pw = (int)Math.Round(ActualWidth * d.DpiScaleX);
                    ph = (int)Math.Round(ActualHeight * d.DpiScaleY);
                }
            }
            catch { }

            if (Beside != null) { PlaceBeside(pw, ph); return; }

            double cx;
            try
            {
                Point a1 = _anchor.PointToScreen(new Point(0, 0));
                Point a2 = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
                cx = (a1.X + a2.X) / 2.0;
            }
            catch { cx = (bar.Left + bar.Right) / 2.0; }

            bool top = _bar.PopupsBelow;
            int x = (int)Math.Round(cx - pw / 2.0);
            int y = top ? bar.Bottom : bar.Top - ph;

            MonitorInfo mon = MonitorHelper.FromPoint((int)cx, (bar.Top + bar.Bottom) / 2);
            if (mon != null)
            {
                x = Math.Max(mon.Bounds.Left, Math.Min(mon.Bounds.Right - pw, x));
                y = Math.Max(mon.Bounds.Top, Math.Min(mon.Bounds.Bottom - ph, y));
            }
            _wantX = x; _wantY = y; _haveWant = true;
            Place(x, y, true);
        }

        /// <summary>A flyout: its card just past the edge of the popup the row is in, its first row level with the row; kept on the monitor.</summary>
        void PlaceBeside(int pw, int ph)
        {
            Point a1, a2;
            try
            {
                a1 = Beside.PointToScreen(new Point(0, 0));
                a2 = Beside.PointToScreen(new Point(Beside.ActualWidth, Beside.ActualHeight));
            }
            catch { return; }     // the row has gone (its menu closed)
            double s = 1;
            try { s = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
            // the card sits 8 DIPs in from the window (room for the shadow); the other popup's card edge is its padding + border past the row
            double edge = _card.Padding.Left + _card.BorderThickness.Left;
            int x = (int)Math.Round(a2.X + (edge + 2 - 8) * s);
            int y = (int)Math.Round(a1.Y - (8 + edge) * s);
            MonitorInfo mon = MonitorHelper.FromPoint((int)((a1.X + a2.X) / 2), (int)((a1.Y + a2.Y) / 2));
            if (mon != null)
            {
                if (x + pw > mon.Bounds.Right) x = (int)Math.Round(a1.X - (edge + 2 - 8) * s) - pw;     // no room on the right: the left
                x = Math.Max(mon.Bounds.Left, Math.Min(mon.Bounds.Right - pw, x));
                y = Math.Max(mon.Bounds.Top, Math.Min(mon.Bounds.Bottom - ph, y));
            }
            _wantX = x; _wantY = y; _haveWant = true;
            Place(x, y, true);
        }

        /// <summary>Move to device-pixel position; also tell WPF (Left/Top) so it does not move the window back to its old coordinates.</summary>
        void Place(int x, int y, bool syncWpf)
        {
            RECT cur;
            Native.GetWindowRect(_hwnd, out cur);
            if (cur.Left != x || cur.Top != y)
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (!syncWpf) return;
            try
            {
                DpiScale d = VisualTreeHelper.GetDpi(this);
                _syncing = true;
                Left = x / d.DpiScaleX;
                Top = y / d.DpiScaleY;
            }
            catch { }
            finally { _syncing = false; }
        }
    }
}
