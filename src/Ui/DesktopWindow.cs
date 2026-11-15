using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Where "on the desktop" is in the z-order.</summary>
    public static class DesktopLayer
    {
        static IntPtr _host;
        static DateTime _hostAt;

        /// <summary>The window Explorer's desktop icons live in (Progman, or the WorkerW that holds SHELLDLL_DefView). Zero when Explorer is not running.</summary>
        public static IntPtr FindDesktopHost()
        {
            // asked for every widget every 2 seconds: the same answer for a second (a walk over the WorkerW windows otherwise)
            if (_host != IntPtr.Zero && (DateTime.UtcNow - _hostAt).TotalSeconds < 1 && Native.IsWindow(_host)) return _host;
            IntPtr found = IntPtr.Zero;
            IntPtr prog = Native.FindWindow("Progman", null);
            if (prog != IntPtr.Zero)
            {
                found = prog;
                IntPtr w = IntPtr.Zero;
                while ((w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                    if (Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { found = w; break; }
            }
            _host = found; _hostAt = DateTime.UtcNow;
            return found;
        }

        /// <summary>
        /// Put a window on the desktop: just above Explorer's desktop, below every normal window; or above all windows when onTop.
        /// </summary>
        public static void Place(IntPtr hwnd, bool onTop)
        {
            if (hwnd == IntPtr.Zero) return;
            IntPtr after;
            if (onTop) after = Native.HWND_TOPMOST;
            else
            {
                IntPtr host = FindDesktopHost();
                // already right behind the desktop host: nothing to do (every widget is put here again every 2 seconds)
                if (host != IntPtr.Zero && Native.GetWindow(hwnd, Native.GW_HWNDPREV) == host) return;
                after = host != IntPtr.Zero ? host : Native.HWND_BOTTOM;
            }
            Native.SetWindowPos(hwnd, after, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Base of the windows that live on the desktop (widgets, desktop icons): borderless, transparent, never takes focus or shows in
    /// Alt+Tab, positioned in device pixels, and kept where it was put even while WPF adjusts for DPI.
    /// </summary>
    public abstract class DesktopWindow : Window
    {
        protected readonly BarManager Mgr;
        protected IntPtr Hwnd;
        /// <summary>Wanted top-left in device pixels; WantW/WantH only when the size is fixed (otherwise the content decides).</summary>
        protected int WantX, WantY, WantW = -1, WantH = -1;
        protected bool HavePos;
        protected bool Dragging;
        public bool AlwaysOnTop { get; set; }

        bool _syncing;
        DispatcherTimer _settle;
        POINT _dragCursor;
        RECT _dragRect;
        bool _dragMoved;

        protected DesktopWindow(BarManager mgr, string title)
        {
            Mgr = mgr;
            Title = title;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = null;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000; Top = -32000;

            Loaded += delegate
            {
                Enforce();
                PlaceZ();
                _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                int n = 0;
                _settle.Tick += delegate { Enforce(); PlaceZ(); if (++n >= 10) _settle.Stop(); };
                _settle.Start();
            };
            SizeChanged += delegate { if (!_syncing && !Dragging) Enforce(); };
            LocationChanged += delegate { if (!_syncing && !Dragging) Enforce(); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Hwnd = new WindowInteropHelper(this).Handle;
            WinStyle.HideFromTasks(Hwnd, true);
            HwndSource src = HwndSource.FromHwnd(Hwnd);
            if (src != null) src.AddHook(Hook);
            Mgr.Tasks.Ignored.Add(Hwnd);
            OnHandleReady();
            Enforce();
            PlaceZ();
        }

        /// <summary>The window handle exists: set the wanted position here.</summary>
        protected virtual void OnHandleReady() { }

        protected override void OnClosed(EventArgs e)
        {
            if (_settle != null) _settle.Stop();
            Mgr.Tasks.Ignored.Remove(Hwnd);
            base.OnClosed(e);
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)msg == Native.WM_MOUSEACTIVATE && !_keyboard)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);       // clicking a widget / icon must not pull it in front of your windows
            }
            return IntPtr.Zero;
        }

        /// <summary>The window handle (zero until the window has been created).</summary>
        public IntPtr NativeHandle { get { return Hwnd; } }
        public BarManager Manager { get { return Mgr; } }
        /// <summary>Being dragged by hand right now.</summary>
        public bool IsDragging { get { return Dragging; } }

        /// <summary>Set the wanted device-pixel rectangle (w/h &lt;= 0 = keep the size the content decides) and move there.</summary>
        public void SetTarget(int x, int y, int w, int h)
        {
            WantX = x; WantY = y; WantW = w; WantH = h;
            HavePos = true;
            Enforce();
        }

        // ---- typing: a notes widget takes the keyboard while you edit -------------------------------------
        bool _keyboard;
        /// <summary>Taking the keyboard right now (in front of other windows until StopKeyboard).</summary>
        public bool HasKeyboard { get { return _keyboard; } }

        /// <summary>Let this window be activated and come in front of the other windows, so it gets the keyboard.</summary>
        protected void StartKeyboard()
        {
            if (Hwnd == IntPtr.Zero) return;
            if (!_keyboard)
            {
                _keyboard = true;
                long ex = Native.GetWindowLongAuto(Hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLongAuto(Hwnd, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_NOACTIVATE);
                if (!AlwaysOnTop) Native.SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE);   // HWND_TOP
            }
            Native.SetForegroundWindow(Hwnd);
            try { Activate(); } catch { }
        }

        /// <summary>Back to never taking the keyboard, and back on the desktop layer.</summary>
        protected void StopKeyboard()
        {
            if (!_keyboard || Hwnd == IntPtr.Zero) { _keyboard = false; return; }
            _keyboard = false;
            long ex = Native.GetWindowLongAuto(Hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongAuto(Hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE);
            PlaceZ();
        }

        /// <summary>Leave the window's place in the stack alone for now (a right-click menu / popup of its own is open: putting the window
        /// back on top would cover the menu, or make it close).</summary>
        protected virtual bool HoldZ { get { return false; } }

        bool _front;

        /// <summary>
        /// In front of the other windows for now without taking the keyboard itself (a terminal in this widget has it - its text window
        /// rides on this one, so it would stay behind everything otherwise); off = back on the desktop layer.
        /// </summary>
        public void HoldInFront(bool on)
        {
            if (Hwnd == IntPtr.Zero) return;
            if (on)
            {
                _front = true;
                if (!AlwaysOnTop) Native.SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);   // HWND_TOP
                return;
            }
            if (!_front) return;
            _front = false;
            PlaceZ();
        }

        public void PlaceZ()
        {
            if (Hwnd == IntPtr.Zero || _keyboard || _front || HoldZ) return;
            DesktopLayer.Place(Hwnd, AlwaysOnTop);
        }

        /// <summary>Move the window's top-left to x, y (device pixels) - moving along with another window being dragged.</summary>
        public void MoveWithGroup(int x, int y)
        {
            if (Hwnd == IntPtr.Zero) return;
            WantX = x; WantY = y;
            HavePos = true;
            Native.SetWindowPos(Hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        /// <summary>Move/size the window to the wanted rectangle if it is not there.</summary>
        public void Enforce()
        {
            if (Hwnd == IntPtr.Zero || !HavePos || Dragging) return;
            RECT cur;
            if (!Native.GetWindowRect(Hwnd, out cur)) return;
            bool sized = WantW > 0 && WantH > 0;
            bool moved = cur.Left != WantX || cur.Top != WantY;
            bool resized = sized && (cur.Width != WantW || cur.Height != WantH);
            if (!moved && !resized) return;

            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;
            if (Visibility == Visibility.Visible) flags |= Native.SWP_SHOWWINDOW;     // a hidden one (monitor off, nothing to show) stays hidden
            if (!sized) flags |= Native.SWP_NOSIZE;
            Native.SetWindowPos(Hwnd, IntPtr.Zero, WantX, WantY, sized ? WantW : 0, sized ? WantH : 0, flags);

            // tell WPF too, so it does not later move the window back to its old coordinates
            try
            {
                DpiScale d = VisualTreeHelper.GetDpi(this);
                _syncing = true;
                Left = WantX / d.DpiScaleX;
                Top = WantY / d.DpiScaleY;
            }
            catch { }
            finally { _syncing = false; }
        }

        // ---- dragging by hand (device pixels, no window activation) ---------------------------------------
        protected void BeginManualDrag()
        {
            Native.GetCursorPos(out _dragCursor);
            Native.GetWindowRect(Hwnd, out _dragRect);
            Dragging = true;
            _dragMoved = false;
            DragStarted();
        }

        /// <summary>A drag by hand began (widgets: the picked widgets that move along are gathered here).</summary>
        protected virtual void DragStarted() { }

        /// <summary>The dragged window is now dx, dy device pixels from where the drag began.</summary>
        protected virtual void DraggedBy(int dx, int dy) { }

        /// <summary>Follow the mouse. Returns true once the pointer has moved far enough to count as a drag.</summary>
        protected bool UpdateManualDrag()
        {
            if (!Dragging) return false;
            POINT c;
            Native.GetCursorPos(out c);
            int dx = c.X - _dragCursor.X, dy = c.Y - _dragCursor.Y;
            if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 5) return false;
            _dragMoved = true;
            WantX = _dragRect.Left + dx;
            WantY = _dragRect.Top + dy;
            AdjustDrag(ref WantX, ref WantY);
            HavePos = true;
            Native.SetWindowPos(Hwnd, IntPtr.Zero, WantX, WantY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            DraggedBy(WantX - _dragRect.Left, WantY - _dragRect.Top);
            return true;
        }

        /// <summary>A chance to change where a drag puts the window (widgets snap here).</summary>
        protected virtual void AdjustDrag(ref int x, ref int y) { }

        /// <summary>Stop dragging; returns true if the window was actually moved.</summary>
        protected bool EndManualDrag()
        {
            bool moved = _dragMoved;
            Dragging = false;
            _dragMoved = false;
            return moved;
        }
    }
}
