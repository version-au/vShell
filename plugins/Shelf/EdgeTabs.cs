using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VShellShelf
{
    // =====================================================================
    // Win32 bits the edge tabs need
    // =====================================================================
    static class W32
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

        public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);
        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint x, out uint y);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr v);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        public static bool Contains(RECT r, POINT p) { return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom; }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(64);
            return GetClassName(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        /// <summary>A window of vShell's own (a bar, a menu, a dialog, the tabs).</summary>
        public static bool Ours(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid == (uint)Environment.ProcessId;
        }

        /// <summary>A window that won't take the focus (clicks on it leave the app you're in active).</summary>
        public static void NoActivate(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            long ex = GetWindowLongPtr(h, -20).ToInt64();
            SetWindowLongPtr(h, -20, new IntPtr(ex | 0x08000000L /* WS_EX_NOACTIVATE */ | 0x80L /* WS_EX_TOOLWINDOW */));
        }
    }

    /// <summary>The monitors, for the edge tabs' "Monitor" setting and placing them.</summary>
    static class Monitors
    {
        public sealed class Mon
        {
            public IntPtr Handle;
            public string Device = "";
            public bool Primary;
            public W32.RECT Bounds;
            public int Index;
        }

        public static List<Mon> All()
        {
            var l = new List<Mon>();
            W32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr h, IntPtr hdc, IntPtr r, IntPtr d)
            {
                var mi = new W32.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(W32.MONITORINFOEX)) };
                if (W32.GetMonitorInfo(h, ref mi)) l.Add(new Mon { Handle = h, Device = mi.szDevice ?? "", Primary = (mi.dwFlags & 1) != 0, Bounds = mi.rcMonitor });
                return true;
            }, IntPtr.Zero);
            l.Sort(delegate (Mon a, Mon b) { return a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top); });
            for (int i = 0; i < l.Count; i++) l[i].Index = i + 1;
            return l;
        }

        /// <summary>The monitor with that device name (\\.\DISPLAY2), else the main one.</summary>
        public static Mon Find(string device)
        {
            List<Mon> all = All();
            Mon m = all.Find(delegate (Mon x) { return device.Length > 0 && string.Equals(x.Device, device, StringComparison.OrdinalIgnoreCase); });
            return m ?? all.Find(delegate (Mon x) { return x.Primary; }) ?? (all.Count > 0 ? all[0] : null);
        }

        public static string Label(Mon m)
        {
            return "Monitor " + m.Index + (m.Primary ? " (main)" : "") + " - " + m.Bounds.Width + " × " + m.Bounds.Height;
        }

        public static double Scale(IntPtr hMon)
        {
            uint x, y;
            return W32.GetDpiForMonitor(hMon, 0, out x, out y) == 0 && x > 0 ? x / 96.0 : 1.0;
        }
    }

    // =====================================================================
    // The edge tabs
    // =====================================================================
    /// <summary>
    /// The shelves as tabs on a screen edge (any edge of any monitor - against a bar when that edge has one, as the drop-down terminal
    /// is). At rest a thin strip, one colour per shelf; the tabs slide out when a drag reaches the strip, when a drag starts anywhere
    /// (a mouse watch, if on), or when you hover the strip (if on). A tab takes a drop; clicking one - or holding a drag over it - opens
    /// that shelf as a drawer.
    /// </summary>
    public static class EdgeTabs
    {
        enum Look { Rest, Out }

        static BarManager _mgr;
        static TabsWindow _tabs;
        static DrawerWindow _drawer;
        static Look _look = Look.Rest;
        static bool _on, _hidden;
        static DispatcherTimer _hover, _leave, _check;
        static int _hot = -1;
        static DispatcherTimer _spring;
        static string _springId;

        // where it all goes (device pixels), worked out by Measure
        internal sealed class Geo
        {
            public Monitors.Mon Mon;
            public double Scale = 1;
            public string Side = "Right";        // Top / Bottom / Left / Right
            public bool Vertical;                // Left / Right: the tabs are a column
            public int Anchor;                   // the edge line: x for Left / Right, y for Top / Bottom (the bar's outer edge when attached)
            public int SpanStart, SpanEnd;       // the room along the edge
            public BarInfo Bar;                  // the bar it hangs from (Top / Bottom with a bar there), or null
            public Theme Theme;
        }
        static Geo _g;

        static ShelfSettings S { get { return ShelfSettings.Current; } }

        /// <summary>Called at start and on every settings change: on, off, or placed again as set.</summary>
        public static void Apply(BarManager mgr)
        {
            _mgr = mgr;
            bool want = S != null && S.EdgeOn;
            if (!want) { Stop(); return; }
            if (!_on)
            {
                _on = true;
                _mgr.BarsChanged += Rebuild;
                ShelfStore.Changed += Rebuild;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
                _check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _check.Tick += delegate { CheckFullScreen(); };
                _check.Start();
            }
            if (S.EdgeOnDrag) DragWatch.Start(DragStarted, DragEnded); else DragWatch.Stop();
            if (_drawer != null) _drawer.Close(false);
            _look = Look.Rest;
            Rebuild();
        }

        public static void Stop()
        {
            if (!_on) return;
            _on = false;
            DragWatch.Stop();
            if (_mgr != null) _mgr.BarsChanged -= Rebuild;
            ShelfStore.Changed -= Rebuild;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            foreach (DispatcherTimer t in new[] { _check, _hover, _leave, _spring }) if (t != null) t.Stop();
            if (_drawer != null) { _drawer.Close(false); _drawer.ForceClose(); _drawer = null; }
            if (_tabs != null) { _tabs.Close(); _tabs = null; }
        }

        static void DisplayChanged(object o, EventArgs e)
        {
            Application app = Application.Current;
            if (app != null) app.Dispatcher.BeginInvoke(new Action(Rebuild));
        }

        // ---- where ----
        static Geo Measure()
        {
            Monitors.Mon mon = Monitors.Find(S.EdgeMonitor);
            if (mon == null) return null;
            var g = new Geo { Mon = mon, Scale = Monitors.Scale(mon.Handle), Side = S.EdgeSide };
            g.Vertical = g.Side == "Left" || g.Side == "Right";
            List<BarInfo> bars = _mgr.Bars().FindAll(delegate (BarInfo b) { return b.Monitor == mon.Handle; });
            BarInfo top = bars.Find(delegate (BarInfo b) { return b.Edge == "Top"; });
            BarInfo bottom = bars.Find(delegate (BarInfo b) { return b.Edge == "Bottom"; });
            W32.RECT mb = mon.Bounds;
            if (g.Vertical)
            {
                g.Anchor = g.Side == "Right" ? mb.Right : mb.Left;
                g.SpanStart = top != null ? top.Bottom : mb.Top;
                g.SpanEnd = bottom != null ? bottom.Top : mb.Bottom;
                g.Bar = null;
                BarInfo any = top ?? bottom;
                g.Theme = any != null ? any.Theme : Theme.From(_mgr.Settings, 12.5, 8);
            }
            else
            {
                g.Bar = g.Side == "Top" ? top : bottom;
                if (g.Bar != null)
                {
                    // hang from the bar's straight edge (where its corners stop rounding)
                    int inset = (int)Math.Round(g.Bar.CornerRadius * g.Scale);
                    g.Anchor = g.Side == "Top" ? g.Bar.Bottom : g.Bar.Top;
                    g.SpanStart = g.Bar.Left + inset;
                    g.SpanEnd = g.Bar.Right - inset;
                    g.Theme = g.Bar.Theme;
                }
                else
                {
                    g.Anchor = g.Side == "Top" ? mb.Top : mb.Bottom;
                    g.SpanStart = mb.Left;
                    g.SpanEnd = mb.Right;
                    g.Theme = Theme.From(_mgr.Settings, 12.5, 8);
                }
            }
            return g;
        }

        /// <summary>Where a run of 'length' device pixels starts along the edge (Start / Centre / End, kept in the room there is).</summary>
        static int Along(int length)
        {
            int room = _g.SpanEnd - _g.SpanStart;
            int pad = (int)Math.Round(12 * _g.Scale);
            int at = S.EdgeAlign == "Start" ? _g.SpanStart + pad : S.EdgeAlign == "End" ? _g.SpanEnd - pad - length : _g.SpanStart + (room - length) / 2;
            return Math.Max(_g.SpanStart, Math.Min(_g.SpanEnd - length, at));
        }

        // ---- the tabs ----
        internal sealed class Tab
        {
            public string Id;           // a shelf's id, or ShelfView.ClipTab
            public string Name;
            public int Count;
            public SolidColorBrush Color;
            public bool Clip;
        }

        static List<Tab> Tabs()
        {
            var l = new List<Tab>();
            foreach (Shelf s in ShelfStore.Shelves)
                l.Add(new Tab { Id = s.Id, Name = s.Name, Count = s.Items.Count, Color = new SolidColorBrush(Theme.Parse(s.Color, Colors.SteelBlue)) });
            if (S.ClipTab) l.Add(new Tab { Id = ShelfView.ClipTab, Name = "Clipboard", Color = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xAF)), Clip = true });
            return l;
        }

        /// <summary>Draw the tabs again and put them in place (settings, bars, shelves or monitors changed).</summary>
        static void Rebuild()
        {
            if (!_on) return;
            _g = Measure();
            if (_g == null) return;
            if (_tabs == null) _tabs = new TabsWindow();
            if (!_hidden) _tabs.Show();
            Draw(false);
            if (_drawer != null && _drawer.IsOpen) { _drawer.Refresh(); _drawer.Place(_g); }
        }

        /// <summary>Lay the tabs out for the look they're in now; slide = animate from the edge.</summary>
        static void Draw(bool slide)
        {
            if (_tabs == null || _g == null) return;
            List<Tab> tabs = Tabs();
            bool ribbon = S.EdgeStyle == "Ribbon", outNow = _look == Look.Out;
            // The window is the size of both looks together, so going out / back / a tab growing only changes what's drawn in it.
            // (Moving or resizing it showed its old picture in the new place for a frame - the flash / jump.) Its clear parts let the
            // pointer through, so only the strip / the tabs react.
            Size restDip = TabsWindow.SizeFor(_g, tabs, false, ribbon), outDip = TabsWindow.SizeFor(_g, tabs, true, ribbon);
            int restLen = Dev(_g.Vertical ? restDip.Height : restDip.Width), outLen = Dev(_g.Vertical ? outDip.Height : outDip.Width);
            int restThick = Dev(_g.Vertical ? restDip.Width : restDip.Height), outThick = Dev(_g.Vertical ? outDip.Width : outDip.Height);
            int restAt = Along(restLen), outAt = Along(outLen);
            int start = Math.Min(restAt, outAt), len = Math.Max(restAt + restLen, outAt + outLen) - start;
            int thick = Math.Max(restThick, outThick);
            int nowAt = outNow ? outAt : restAt, nowLen = outNow ? outLen : restLen, nowThick = outNow ? outThick : restThick;
            _tabs.Build(_g, tabs, outNow, ribbon, _hot, (nowAt - start) / _g.Scale);
            // the drawer is open: the tabs sit on its far side
            int anchor = _g.Anchor;
            W32.RECT dr;
            if (_drawer != null && _drawer.IsOpen && _drawer.Rect(out dr))
                anchor = _g.Side == "Right" ? dr.Left : _g.Side == "Left" ? dr.Right : _g.Side == "Top" ? dr.Bottom : dr.Top;
            int x, y, w, h;
            if (_g.Vertical) { w = thick; h = len; y = start; x = _g.Side == "Right" ? anchor - thick : anchor; }
            else { w = len; h = thick; x = start; y = _g.Side == "Bottom" ? anchor - thick : anchor; }
            _tabs.PlaceAt(_g.Mon, x, y, w, h);
            // where the strip / tabs are (the pointer checks)
            var hit = new W32.RECT();
            if (_g.Vertical) { hit.Top = nowAt; hit.Bottom = nowAt + nowLen; hit.Left = _g.Side == "Right" ? anchor - nowThick : anchor; hit.Right = hit.Left + nowThick; }
            else { hit.Left = nowAt; hit.Right = nowAt + nowLen; hit.Top = _g.Side == "Bottom" ? anchor - nowThick : anchor; hit.Bottom = hit.Top + nowThick; }
            _tabs.Hit = hit;
            if (slide) _tabs.SlideIn(_g);
        }

        static int Dev(double dip) { return (int)Math.Ceiling(dip * _g.Scale); }

        static bool PointerOnTabs()
        {
            if (_tabs == null) return false;
            W32.POINT p;
            W32.GetCursorPos(out p);
            return W32.Contains(_tabs.Hit, p);
        }

        static void SlideOut()
        {
            if (!_on || _hidden || _look == Look.Out) return;
            _look = Look.Out;
            Draw(true);
            Leave();
        }

        static void Retract()
        {
            if (_look == Look.Rest) return;
            if (_drawer != null && _drawer.IsOpen) return;
            _look = Look.Rest;
            _hot = -1;
            // slide the tabs back into the edge, then the strip fades in (unless they came out again meanwhile)
            if (_tabs != null && _g != null && !_hidden)
                _tabs.SlideAway(_g, delegate
                {
                    if (_look != Look.Rest || _tabs == null) return;
                    Draw(false);
                    _tabs.FadeIn();
                    if (PointerOnTabs()) StripEntered();      // came back onto the strip while they went (no MouseEnter for that)
                });
            else Draw(false);
        }

        /// <summary>Go back in a moment unless the pointer is on the tabs, a drag is still going, or the drawer is open.</summary>
        static void Leave()
        {
            if (_leave == null)
            {
                _leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                _leave.Tick += delegate
                {
                    bool over = PointerOnTabs();
                    bool held = (W32.GetAsyncKeyState(0x01) & 0x8000) != 0;
                    if (over || (held && DragWatch.Dragging) || (_drawer != null && _drawer.IsOpen)) return;
                    _leave.Stop();
                    Retract();
                };
            }
            _leave.Stop();
            _leave.Start();
        }

        // ---- what the tabs window tells us ----
        internal static void StripEntered()
        {
            if (!S.EdgeOnHover || _look == Look.Out) return;
            if (_hover == null)
            {
                _hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _hover.Tick += delegate
                {
                    _hover.Stop();
                    if (PointerOnTabs()) SlideOut();
                };
            }
            _hover.Stop();
            _hover.Start();
        }

        internal static void StripLeft() { if (_hover != null) _hover.Stop(); }

        /// <summary>A drag reached the strip: out at once.</summary>
        internal static void DragOnStrip() { SlideOut(); }

        internal static void TabsLeft() { if (_look == Look.Out) Leave(); }

        internal static void Hot(int i)
        {
            if (_hot == i) return;
            _hot = i;
            if (_look == Look.Out && _tabs != null) _tabs.SetHot(i);
        }

        internal static void TabClicked(Tab t)
        {
            if (_drawer != null && _drawer.IsOpen && _drawer.ShelfId == t.Id) { _drawer.Close(true); return; }
            OpenDrawer(t.Id, false);
        }

        /// <summary>A drag held over a tab: open that shelf's drawer (so you can drop onto its list).</summary>
        internal static void SpringOn(Tab t)
        {
            int ms = S != null ? S.EdgeSpringMs : 650;
            if (ms <= 0) { SpringOff(); return; }       // set to never: a drop on the tab only
            if (_spring == null)
            {
                _spring = new DispatcherTimer();
                _spring.Tick += delegate { _spring.Stop(); if (_springId != null) OpenDrawer(_springId, true); };
            }
            _spring.Interval = TimeSpan.FromMilliseconds(ms);
            _springId = t.Id;
            _spring.Stop();
            _spring.Start();
        }

        internal static void SpringOff() { if (_spring != null) _spring.Stop(); _springId = null; }

        internal static void Dropped(Tab t, DragEventArgs e)
        {
            SpringOff();
            Shelf s = ShelfStore.Find(t.Id);
            if (s == null) { e.Effects = DragDropEffects.None; return; }
            int n = ShelfStore.AddFrom(s, e.Data);
            e.Effects = n > 0 ? ((e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy) : DragDropEffects.None;
            if (_tabs != null) _tabs.Flash(t.Id);
            Leave();
        }

        internal static DragDropEffects EffectFor(Tab t, DragEventArgs e)
        {
            if (t.Clip) return DragDropEffects.None;
            if (e.Data.GetDataPresent(ShelfStore.ItemsFormat))
                return ((e.Data.GetData(ShelfStore.ItemsFormat) as string) ?? "").StartsWith(t.Id + "|") ? DragDropEffects.None : DragDropEffects.Link;
            if (!ShelfStore.CanTake(e.Data)) return DragDropEffects.None;
            if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        // ---- the drawer ----
        static void OpenDrawer(string shelfId, bool dragging)
        {
            if (_g == null) return;
            if (_drawer == null) _drawer = new DrawerWindow();
            _drawer.Open(_g, shelfId, dragging);
            _look = Look.Out;
            Draw(false);
        }

        internal static void DrawerClosed()
        {
            Draw(false);
            Leave();
        }

        internal static bool StayOpen { get { return S != null && S.EdgeStayOpen; } }

        /// <summary>The drawer's pin: the "stays open until you close it" setting itself (and Settings shows it, if open).</summary>
        internal static void SetStayOpen(bool on)
        {
            if (S == null) return;
            S.EdgeStayOpen = on;
            if (_mgr != null) _mgr.SaveSoon();
            Plugin.SettingsShown();
        }

        // ---- a drag anywhere (DragWatch) ----
        static void DragStarted() { SlideOut(); }
        static void DragEnded() { Leave(); }

        // ---- full screen: out of the way ----
        static void CheckFullScreen()
        {
            if (!_on || _g == null || _tabs == null) return;
            bool fs = false;
            foreach (BarInfo b in _mgr.Bars()) if (b.Monitor == _g.Mon.Handle && b.BehindFullScreen) fs = true;
            if (!fs)
            {
                // no bar there to tell us: a window without a title bar covering the whole monitor (a game, a video, a slide show)
                IntPtr fg = W32.GetForegroundWindow();
                W32.RECT r;
                if (fg != IntPtr.Zero && !W32.Ours(fg) && W32.GetWindowRect(fg, out r))
                {
                    string cls = W32.ClassOf(fg);
                    long style = W32.GetWindowLongPtr(fg, -16).ToInt64();
                    bool caption = (style & 0x00C00000L) == 0x00C00000L;
                    W32.RECT m = _g.Mon.Bounds;
                    fs = cls != "Progman" && cls != "WorkerW" && !caption && r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
                }
            }
            if (fs == _hidden) return;
            _hidden = fs;
            if (fs)
            {
                if (_drawer != null) _drawer.Close(false);
                _look = Look.Rest;
                _tabs.Hide();
            }
            else { _tabs.Show(); Draw(false); }
        }
    }

    // =====================================================================
    // The tabs' window
    // =====================================================================
    sealed class TabsWindow : Window
    {
        readonly StackPanel _panel;
        readonly TranslateTransform _slide = new TranslateTransform();
        IntPtr _hwnd;
        readonly List<Border> _items = new List<Border>();
        List<EdgeTabs.Tab> _tabs = new List<EdgeTabs.Tab>();
        EdgeTabs.Geo _g;
        bool _out, _ribbon;
        int _gen;                       // bumped on every Build (a slide-away finishing late does nothing)

        public IntPtr Handle { get { return _hwnd; } }

        /// <summary>Where the strip / tabs are drawn now (device pixels) - the window itself is bigger.</summary>
        public W32.RECT Hit;

        static readonly Duration HotTime = new Duration(TimeSpan.FromMilliseconds(130));

        public TabsWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Title = "vShell Shelf tabs";
            _panel = new StackPanel { RenderTransform = _slide };
            // (no background on the root: the clear part of the window lets the pointer through)
            Content = new Grid { Children = { _panel }, UseLayoutRounding = true, ClipToBounds = true };
            AllowDrop = true;
            SourceInitialized += delegate { _hwnd = new WindowInteropHelper(this).Handle; W32.NoActivate(this); };
            MouseEnter += delegate { if (!_out) EdgeTabs.StripEntered(); };
            MouseLeave += delegate { EdgeTabs.StripLeft(); EdgeTabs.TabsLeft(); };
            DragEnter += delegate { if (!_out) EdgeTabs.DragOnStrip(); };
            DragLeave += delegate { EdgeTabs.TabsLeft(); };
            DragOver += delegate (object o, DragEventArgs e) { if (!e.Handled) { e.Effects = DragDropEffects.None; e.Handled = true; } };
            // a press must not do anything on its own (the click is on MouseUp)
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
        }

        public void PlaceAt(Monitors.Mon mon, int x, int y, int w, int h)
        {
            if (_hwnd == IntPtr.Zero) return;
            // on the monitor first, so the window takes that monitor's scaling before it's sized
            if (W32.MonitorFromWindow(_hwnd, 2) != mon.Handle)
                W32.SetWindowPos(_hwnd, W32.HWND_TOPMOST, mon.Bounds.Left + 10, mon.Bounds.Top + 10, 40, 40, W32.SWP_NOACTIVATE);
            w = Math.Max(1, w); h = Math.Max(1, h);
            // the same place: leave it be (only what's drawn changes - no flash)
            W32.RECT now;
            if (IsVisible && W32.GetWindowRect(_hwnd, out now) && now.Left == x && now.Top == y && now.Width == w && now.Height == h) return;
            W32.SetWindowPos(_hwnd, W32.HWND_TOPMOST, x, y, w, h, W32.SWP_NOACTIVATE | (IsVisible ? W32.SWP_SHOWWINDOW : 0));
        }

        // sizes in DIPs
        const double RestThick = 6, RestLen = 48, RestGap = 4, RestHit = 18, RestPad = 20;
        static readonly Brush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

        static Brush Freeze(Brush b) { b.Freeze(); return b; }
        const double TabThick = 38, TabHot = 48, TabLen = 104, ClipLen = 92, TabGap = 6, FlatLen = 116;
        const double RibThick = 12, RibLen = 84, RibGap = 4;

        const double RibHotV = 150, RibHotH = 40;
        double RibHot { get { return _g.Vertical ? RibHotV : RibHotH; } }

        /// <summary>The size the strip (at rest) or the tabs (out) take, in DIPs (width × height).</summary>
        public static Size SizeFor(EdgeTabs.Geo g, List<EdgeTabs.Tab> tabs, bool outNow, bool ribbon)
        {
            double len = 0, thick;
            if (!outNow)
            {
                thick = RestHit;
                len = tabs.Count * RestLen + RestPad + (tabs.Count > 0 ? RestPad + (tabs.Count - 1) * RestGap : 0);
            }
            else
            {
                thick = ribbon ? (g.Vertical ? RibHotV : RibHotH) : TabHot;
                for (int i = 0; i < tabs.Count; i++)
                    len += (ribbon ? RibLen : g.Vertical ? (tabs[i].Clip ? ClipLen : TabLen) : FlatLen) + (i == 0 ? 0 : (ribbon ? RibGap : TabGap));
            }
            return g.Vertical ? new Size(thick, len) : new Size(len, thick);
        }

        /// <summary>Make the strip (at rest) or the tabs (out), 'offset' DIPs along the window from its start.</summary>
        public void Build(EdgeTabs.Geo g, List<EdgeTabs.Tab> tabs, bool outNow, bool ribbon, int hot, double offset)
        {
            _g = g; _tabs = tabs; _out = outNow; _ribbon = ribbon;
            _gen++;
            _panel.Children.Clear();
            _items.Clear();
            _panel.Orientation = g.Vertical ? Orientation.Vertical : Orientation.Horizontal;
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.X = 0; _slide.Y = 0;
            _panel.BeginAnimation(OpacityProperty, null);
            _panel.Opacity = 1;
            // in its place inside the (bigger) window, against the edge
            Size size = SizeFor(g, tabs, outNow, ribbon);
            _panel.Width = size.Width; _panel.Height = size.Height;
            if (g.Vertical)
            {
                _panel.HorizontalAlignment = g.Side == "Right" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                _panel.VerticalAlignment = VerticalAlignment.Top;
                _panel.Margin = new Thickness(0, offset, 0, 0);
            }
            else
            {
                _panel.HorizontalAlignment = HorizontalAlignment.Left;
                _panel.VerticalAlignment = g.Side == "Bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                _panel.Margin = new Thickness(offset, 0, 0, 0);
            }
            // (an all-but-clear background: the whole strip / tab area takes the pointer and drops, not only what's drawn)
            _panel.Background = HitBrush;
            if (!outNow)
            {
                // the strip drawn thin against the edge, inside a wider area that reacts (easier to find with the pointer)
                for (int i = 0; i < tabs.Count; i++)
                {
                    EdgeTabs.Tab t = tabs[i];
                    var seg = new Border
                    {
                        Background = t.Color, Opacity = t.Clip ? 0.55 : 0.9, CornerRadius = OuterCorners(3),
                        Width = g.Vertical ? RestThick : RestLen, Height = g.Vertical ? RestLen : RestThick,
                        Margin = Gap(i == 0 ? RestPad : RestGap), ToolTip = t.Name
                    };
                    ToEdge(seg);
                    _panel.Children.Add(seg);
                }
                _panel.Children.Add(new Border { Width = g.Vertical ? RestHit : RestPad, Height = g.Vertical ? RestPad : RestHit });
            }
            else
            {
                for (int i = 0; i < tabs.Count; i++)
                {
                    Border b = ribbon ? Ribbon(tabs[i], i == hot) : Index(tabs[i], i == hot);
                    b.Margin = Gap(i == 0 ? 0 : (ribbon ? RibGap : TabGap));
                    Hook(b, tabs[i], i);
                    _items.Add(b);
                    _panel.Children.Add(b);
                }
            }
        }

        Thickness Gap(double g) { return _g.Vertical ? new Thickness(0, g, 0, 0) : new Thickness(g, 0, 0, 0); }

        /// <summary>Rounded on the side away from the edge, square against it.</summary>
        CornerRadius OuterCorners(double r)
        {
            switch (_g.Side)
            {
                case "Right": return new CornerRadius(r, 0, 0, r);
                case "Left": return new CornerRadius(0, r, r, 0);
                case "Top": return new CornerRadius(0, 0, r, r);
                default: return new CornerRadius(r, r, 0, 0);
            }
        }

        /// <summary>The colour stripe on the side away from the edge.</summary>
        Thickness Stripe(double w)
        {
            switch (_g.Side)
            {
                case "Right": return new Thickness(w, 0, 0, 0);
                case "Left": return new Thickness(0, 0, w, 0);
                case "Top": return new Thickness(0, 0, 0, w);
                default: return new Thickness(0, w, 0, 0);
            }
        }

        /// <summary>Lined up against the edge, so a tab that grows grows away from it.</summary>
        void ToEdge(FrameworkElement b)
        {
            if (_g.Vertical) b.HorizontalAlignment = _g.Side == "Right" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            else b.VerticalAlignment = _g.Side == "Bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        }

        Brush TabBg(EdgeTabs.Tab t, bool hot)
        {
            Color c = _g.Theme.PopupBg.Color;
            var bg = new SolidColorBrush(Color.FromArgb(Math.Max(c.A, (byte)235), c.R, c.G, c.B));
            return hot ? (Brush)Theme.Faded(t.Color, 40) : bg;
        }

        Border Index(EdgeTabs.Tab t, bool hot)
        {
            Theme th = _g.Theme;
            double fs = Math.Max(11, Math.Min(15, th.FontSize));
            var name = new TextBlock { Text = t.Name, Foreground = th.Fg, FontFamily = th.Font, FontSize = fs, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var count = new TextBlock { Text = t.Clip ? "" : t.Count.ToString(), Foreground = th.FgDim, FontFamily = th.Font, FontSize = fs * 0.85, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (_g.Vertical)
            {
                // the name reads up the tab on the right edge, down it on the left
                name.LayoutTransform = new RotateTransform(_g.Side == "Right" ? -90 : 90);
                name.MaxWidth = (t.Clip ? ClipLen : TabLen) - 26;
                sp.Orientation = Orientation.Vertical;
                if (_g.Side == "Left") { sp.Children.Add(count); count.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(name); }
                else { sp.Children.Add(name); count.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(count); }
            }
            else
            {
                sp.Orientation = Orientation.Horizontal;
                var dot = ModulePics.Dot(t.Color, 8);
                dot.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(dot);
                name.MaxWidth = FlatLen - 44;
                sp.Children.Add(name);
                count.Margin = new Thickness(5, 0, 0, 0);
                sp.Children.Add(count);
            }
            var b = new Border
            {
                Child = sp, Background = TabBg(t, hot), BorderBrush = t.Color, BorderThickness = Stripe(4), CornerRadius = OuterCorners(10),
                Cursor = Cursors.Hand, Opacity = t.Clip ? 0.85 : 1, ToolTip = TipFor(t)
            };
            double thick = hot ? TabHot : TabThick, along = _g.Vertical ? (t.Clip ? ClipLen : TabLen) : FlatLen;
            b.Width = _g.Vertical ? thick : along;
            b.Height = _g.Vertical ? along : thick;
            ToEdge(b);
            return b;
        }

        Border Ribbon(EdgeTabs.Tab t, bool hot)
        {
            Theme th = _g.Theme;
            var b = new Border { Background = t.Color, CornerRadius = OuterCorners(hot ? 10 : 6), ClipToBounds = true, Cursor = Cursors.Hand, ToolTip = hot ? null : TipFor(t), Opacity = t.Clip && !hot ? 0.7 : 1 };
            double thick = hot ? RibHot : RibThick;
            b.Width = _g.Vertical ? thick : RibLen;
            b.Height = _g.Vertical ? RibLen : thick;
            if (hot)
            {
                // the label, dark on the shelf's colour
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = _g.Vertical ? new Thickness(14, 0, 8, 0) : new Thickness(8, 0, 8, 0) };
                if (!_g.Vertical) sp.HorizontalAlignment = HorizontalAlignment.Center;
                Brush ink = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1C));
                double fs = Math.Max(11, Math.Min(15, th.FontSize));
                sp.Children.Add(new TextBlock { Text = t.Name, Foreground = ink, FontFamily = th.Font, FontSize = fs, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (_g.Vertical && !t.Clip) sp.Children.Add(new TextBlock { Text = t.Count + (t.Count == 1 ? " item" : " items"), Foreground = ink, FontFamily = th.Font, FontSize = fs * 0.85 });
                b.Child = sp;
            }
            ToEdge(b);
            return b;
        }

        static string TipFor(EdgeTabs.Tab t)
        {
            return t.Clip ? "Clipboard - click for Windows' clipboard history" : t.Name + " - " + t.Count + (t.Count == 1 ? " item" : " items") + "\nDrop here to add · click to open";
        }

        void Hook(Border b, EdgeTabs.Tab t, int i)
        {
            b.AllowDrop = true;
            b.MouseEnter += delegate { EdgeTabs.Hot(i); };
            b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; EdgeTabs.TabClicked(t); };
            b.DragEnter += delegate (object o, DragEventArgs e) { EdgeTabs.Hot(i); EdgeTabs.SpringOn(t); };
            b.DragLeave += delegate { EdgeTabs.SpringOff(); };
            b.DragOver += delegate (object o, DragEventArgs e) { e.Effects = EdgeTabs.EffectFor(t, e); e.Handled = true; };
            b.Drop += delegate (object o, DragEventArgs e) { e.Handled = true; EdgeTabs.Dropped(t, e); };
        }

        /// <summary>The tab under the pointer grows out of the edge, the one it left shrinks back (without drawing everything again).</summary>
        public void SetHot(int hot)
        {
            if (!_out) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DependencyProperty grow = _g.Vertical ? WidthProperty : HeightProperty;
            for (int i = 0; i < _items.Count && i < _tabs.Count; i++)
            {
                Border nb = _ribbon ? Ribbon(_tabs[i], i == hot) : Index(_tabs[i], i == hot);
                Border b = _items[i];
                double now = _g.Vertical ? b.ActualWidth : b.ActualHeight;
                double to = _g.Vertical ? nb.Width : nb.Height;
                b.Width = nb.Width; b.Height = nb.Height;
                if (now > 0 && Math.Abs(now - to) > 0.5)
                    b.BeginAnimation(grow, new DoubleAnimation(now, to, HotTime) { EasingFunction = ease });
                b.Background = nb.Background;
                b.CornerRadius = nb.CornerRadius;
                b.Opacity = nb.Opacity;
                b.ToolTip = nb.ToolTip;
                UIElement child = nb.Child;
                nb.Child = null;
                if (child != b.Child && child != null && i == hot && _ribbon)
                {
                    // the ribbon's label fades in as it grows (not squeezed in at full strength)
                    child.Opacity = 0;
                    child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, HotTime) { BeginTime = TimeSpan.FromMilliseconds(40) });
                }
                b.Child = child;
            }
        }

        /// <summary>A short pulse on a tab that just took something.</summary>
        public void Flash(string id)
        {
            int i = _tabs.FindIndex(delegate (EdgeTabs.Tab t) { return t.Id == id; });
            if (i < 0 || i >= _items.Count) return;
            var a = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(450));
            _items[i].BeginAnimation(OpacityProperty, a);
        }

        /// <summary>Slide the tabs out of the edge.</summary>
        public void SlideIn(EdgeTabs.Geo g)
        {
            double d = g.Vertical ? 60 : 50;
            double from = g.Side == "Right" || g.Side == "Bottom" ? d : -d;
            var a = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _slide.BeginAnimation(g.Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, a);
        }

        /// <summary>Slide the tabs back into the edge, then 'done' (unless they were drawn again meanwhile).</summary>
        public void SlideAway(EdgeTabs.Geo g, Action done)
        {
            if (!_out || !IsVisible) { done(); return; }
            int gen = _gen;
            double d = Math.Max(g.Vertical ? 60 : 50, g.Vertical ? _panel.ActualWidth : _panel.ActualHeight);
            double to = g.Side == "Right" || g.Side == "Bottom" ? d : -d;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += delegate { if (gen == _gen) done(); };
            _slide.BeginAnimation(g.Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, a);
        }

        /// <summary>The strip fades in (after the tabs went back).</summary>
        public void FadeIn()
        {
            _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
    }

    // =====================================================================
    // The drawer: one shelf, out of the edge (or the bar)
    // =====================================================================
    sealed class DrawerWindow : Window
    {
        readonly Border _box;
        readonly TranslateTransform _slide = new TranslateTransform();
        IntPtr _hwnd;
        EdgeTabs.Geo _g;
        ShelfView _view;
        bool _pinned, _closing, _prevDown, _forced;
        DispatcherTimer _watch;
        readonly TextBlock _title, _count;
        readonly Border _dot;
        readonly Button _pin, _closeBtn;

        public string ShelfId { get; private set; }
        public bool IsOpen { get; private set; }

        public DrawerWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "vShell Shelf";
            _box = new Border { RenderTransform = _slide, Padding = new Thickness(12, 10, 12, 10) };
            var dock = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
            DockPanel.SetDock(head, Dock.Top);
            _dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _count = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(_dot);
            head.Children.Add(_title);
            head.Children.Add(_count);
            var close = HeadButton("", "Close");
            _closeBtn = close;
            close.Click += delegate { Close(true); };
            DockPanel.SetDock(close, Dock.Right);
            _pin = HeadButton("", "Keep it open until you close it");
            _pin.Click += delegate { EdgeTabs.SetStayOpen(!EdgeTabs.StayOpen); ShowPin(); };
            DockPanel.SetDock(_pin, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(_pin);
            dock.Children.Add(head);
            _box.Child = dock;
            Content = _box;
            SourceInitialized += delegate { _hwnd = new WindowInteropHelper(this).Handle; };
            PreviewKeyDown += delegate (object o, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(true); } };
            // (not when the click that took the focus was on the tabs or another of vShell's windows - a tab swaps the shelf shown)
            Deactivated += delegate { if (!EdgeTabs.StayOpen && !_pinned && IsOpen && !PointerOnOurs()) Close(true); };
            Closing += delegate (object o, System.ComponentModel.CancelEventArgs e) { if (!_forced) { e.Cancel = true; Close(true); } };
        }

        Button HeadButton(string glyph, string tip)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = 11 },
                Width = 28, Height = 26, Margin = new Thickness(4, 0, 0, 0), ToolTip = tip, Cursor = Cursors.Hand,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false
            };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);      // a screen reader says "Close"
            return b;
        }

        static bool PointerOnOurs()
        {
            W32.POINT p;
            return W32.GetCursorPos(out p) && W32.Ours(W32.WindowFromPoint(p));
        }

        void ShowPin()
        {
            bool on = EdgeTabs.StayOpen;
            ((TextBlock)_pin.Content).Text = on ? "" : "";     // pinned / pin
            _pin.Background = on ? (Brush)Theme.Faded(_g != null ? _g.Theme.MainAccent : Brushes.SteelBlue, 35) : Brushes.Transparent;
            _pin.ToolTip = on ? "Stays open until you close it - click to let it close when you click elsewhere" : "Keep it open until you close it";
        }

        public bool Rect(out W32.RECT r)
        {
            r = new W32.RECT();
            return IsOpen && _hwnd != IntPtr.Zero && W32.GetWindowRect(_hwnd, out r);
        }

        public void Open(EdgeTabs.Geo g, string shelfId, bool dragging)
        {
            _g = g;
            bool same = IsOpen && ShelfId == shelfId;
            ShelfId = shelfId;
            Theme t = g.Theme;
            double fs = Math.Max(12, t.FontSize);
            Foreground = t.Fg;
            FontFamily = t.Font;
            FontSize = fs;
            foreach (Button b in new[] { _pin, _closeBtn })
                ((TextBlock)b.Content).Foreground = t.Fg;
            ShowPin();
            Dress(g);
            if (!same)
            {
                // the rows that fit: most of the room along the edge (a side drawer), or about half the monitor (from the top / bottom)
                double rowH = Math.Round(fs * 3.1) + 2;
                double room = g.Vertical ? (g.SpanEnd - g.SpanStart) / g.Scale * 0.75 : (g.Mon.Bounds.Height / g.Scale) * 0.5;
                int rows = (int)Math.Max(4, Math.Min(14, Math.Floor((room - 120) / rowH)));
                _view = new ShelfView(t, fs, shelfId, rows, false);
                _view.Hold = delegate (bool on) { _pinned = on; };
                _view.Used = delegate { if (!EdgeTabs.StayOpen) Close(true); };
                var dock = (DockPanel)_box.Child;
                if (dock.Children.Count > 1) dock.Children.RemoveAt(1);
                dock.Children.Add(_view);
                Head(shelfId, t);
            }
            ShowActivated = !dragging;
            bool was = IsOpen;
            if (_closing)
            {
                // it was sliding away (a click elsewhere just now): stay where it is
                _slide.BeginAnimation(TranslateTransform.XProperty, null);
                _slide.BeginAnimation(TranslateTransform.YProperty, null);
                _slide.X = 0; _slide.Y = 0;
            }
            IsOpen = true;
            _closing = false;
            if (!IsVisible) { Opacity = 0; Show(); }
            UpdateLayout();
            Place(g);
            Opacity = 1;
            if (!was) SlideIn();
            if (!dragging) { Activate(); _view.Focus(); }
            Watch();
        }

        /// <summary>The header again (an item was added or taken off).</summary>
        public void Refresh() { if (_g != null && ShelfId != null) Head(ShelfId, _g.Theme); }

        void Head(string shelfId, Theme t)
        {
            Shelf s = shelfId == ShelfView.ClipTab ? null : ShelfStore.Find(shelfId);
            _title.Text = s != null ? s.Name : "Clipboard";
            _dot.Background = s != null ? new SolidColorBrush(Theme.Parse(s.Color, Colors.SteelBlue)) : t.FgDim;
            _count.Foreground = t.FgDim;
            _count.Text = s != null ? s.Items.Count + (s.Items.Count == 1 ? " item" : " items") : "Windows' clipboard history";
        }

        /// <summary>Square against the edge (and the bar), rounded on the far side; the bar's outline when it hangs from one.</summary>
        void Dress(EdgeTabs.Geo g)
        {
            Theme t = g.Theme;
            Color c = t.PopupBg.Color;
            _box.Background = new SolidColorBrush(Color.FromArgb(Math.Max(c.A, (byte)240), c.R, c.G, c.B));
            double r = g.Bar != null ? g.Bar.CornerRadius : Math.Max(6, t.Radius);
            double bw = g.Bar != null ? g.Bar.BorderThickness : 1;
            _box.BorderBrush = g.Bar != null && g.Bar.Outline != null ? g.Bar.Outline : Theme.Faded(t.Fg, 18);
            switch (g.Side)
            {
                case "Right": _box.CornerRadius = new CornerRadius(r, 0, 0, r); _box.BorderThickness = new Thickness(bw, bw, 0, bw); break;
                case "Left": _box.CornerRadius = new CornerRadius(0, r, r, 0); _box.BorderThickness = new Thickness(0, bw, bw, bw); break;
                case "Top": _box.CornerRadius = new CornerRadius(0, 0, r, r); _box.BorderThickness = new Thickness(bw, 0, bw, bw); break;
                default: _box.CornerRadius = new CornerRadius(r, r, 0, 0); _box.BorderThickness = new Thickness(bw, bw, bw, 0); break;
            }
        }

        /// <summary>Against the edge, lined up with the tabs (Start / Centre / End along it).</summary>
        public void Place(EdgeTabs.Geo g)
        {
            _g = g;
            if (_hwnd == IntPtr.Zero) return;
            if (W32.MonitorFromWindow(_hwnd, 2) != g.Mon.Handle)
            {
                W32.SetWindowPos(_hwnd, W32.HWND_TOPMOST, g.Mon.Bounds.Left + 10, g.Mon.Bounds.Top + 10, 0, 0, W32.SWP_NOSIZE | W32.SWP_NOACTIVATE);
                UpdateLayout();
            }
            W32.RECT now;
            W32.GetWindowRect(_hwnd, out now);
            int w = now.Width, h = now.Height;
            int len = g.Vertical ? h : w;
            int room = g.SpanEnd - g.SpanStart, pad = (int)Math.Round(12 * g.Scale);
            string align = ShelfSettings.Current != null ? ShelfSettings.Current.EdgeAlign : "Centre";
            int at = align == "Start" ? g.SpanStart + pad : align == "End" ? g.SpanEnd - pad - len : g.SpanStart + (room - len) / 2;
            at = Math.Max(g.SpanStart, Math.Min(g.SpanEnd - len, at));
            int x, y;
            if (g.Vertical) { y = at; x = g.Side == "Right" ? g.Anchor - w : g.Anchor; }
            else { x = at; y = g.Side == "Bottom" ? g.Anchor - h : g.Anchor; }
            W32.SetWindowPos(_hwnd, W32.HWND_TOPMOST, x, y, 0, 0, W32.SWP_NOSIZE | W32.SWP_NOACTIVATE);
        }

        void SlideIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            bool v = _g.Vertical;
            double d = Math.Max(60, v ? ActualWidth : ActualHeight);
            double from = _g.Side == "Right" || _g.Side == "Bottom" ? d : -d;
            _slide.BeginAnimation(v ? TranslateTransform.XProperty : TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        }

        /// <summary>Slide back into the edge (animate = false: at once).</summary>
        public void Close(bool animate)
        {
            if (!IsOpen || _closing) return;
            if (_watch != null) _watch.Stop();
            if (!animate || _g == null)
            {
                Done();
                return;
            }
            _closing = true;
            bool v = _g.Vertical;
            double d = Math.Max(60, v ? ActualWidth : ActualHeight);
            double to = _g.Side == "Right" || _g.Side == "Bottom" ? d : -d;
            var a = new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += delegate { if (_closing) Done(); };      // (not if it was opened again meanwhile)
            _slide.BeginAnimation(v ? TranslateTransform.XProperty : TranslateTransform.YProperty, a);
        }

        void Done()
        {
            IsOpen = false;
            _closing = false;
            Hide();
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.X = 0; _slide.Y = 0;
            EdgeTabs.DrawerClosed();
        }

        public void ForceClose()
        {
            _forced = true;
            if (_watch != null) _watch.Stop();
            try { base.Close(); } catch { }
        }

        /// <summary>
        /// Unless it stays open: a click anywhere outside vShell closes it (as a popup does) - but not while something is dragged out of
        /// it or a question from it is up.
        /// </summary>
        void Watch()
        {
            if (_watch == null)
            {
                _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                _watch.Tick += delegate
                {
                    bool down = (W32.GetAsyncKeyState(0x01) & 0x8000) != 0 || (W32.GetAsyncKeyState(0x02) & 0x8000) != 0;
                    bool press = down && !_prevDown;
                    _prevDown = down;
                    if (!press || _pinned || EdgeTabs.StayOpen || !IsOpen) return;
                    W32.POINT p;
                    W32.GetCursorPos(out p);
                    if (W32.Ours(W32.WindowFromPoint(p))) return;       // the drawer, the tabs, its menus, a bar
                    Close(true);
                };
            }
            _prevDown = (W32.GetAsyncKeyState(0x01) & 0x8000) != 0 || (W32.GetAsyncKeyState(0x02) & 0x8000) != 0;
            _watch.Start();
        }
    }

    // =====================================================================
    // Seeing a drag start anywhere
    // =====================================================================
    /// <summary>
    /// Windows doesn't say when a drag starts, so this watches the mouse (a low-level mouse hook): the left button pressed on something
    /// you can drag files or text out of - Explorer's file views and the desktop, a browser's page - and moved a little with it held
    /// counts as a drag starting; the button coming up ends it. A guess, but a cheap one: nothing happens on other windows.
    /// </summary>
    static class DragWatch
    {
        static IntPtr _hook;
        static W32.HookProc _proc;          // kept: the hook calls it
        static Action _started, _ended;
        static bool _down, _candidate;
        static W32.POINT _at;

        public static bool Dragging { get; private set; }

        static readonly HashSet<string> Sources = new HashSet<string>(StringComparer.Ordinal)
        {
            "DirectUIHWND", "SysListView32", "SysTreeView32", "SHELLDLL_DefView",       // Explorer, the desktop, file dialogs
            "Chrome_RenderWidgetHostHWND", "MozillaWindowClass", "Internet Explorer_Server"      // browsers (Edge, Chrome, Firefox), web views
        };

        /// <summary>WPF apps you drag files out of (a WPF window is one "HwndWrapper[app;;…]" window, whatever is in it).</summary>
        static readonly string[] SourcePrefixes = { "HwndWrapper[vXplorer;", "HwndWrapper[vView;" };

        public static void Start(Action started, Action ended)
        {
            _started = started;
            _ended = ended;
            if (_hook != IntPtr.Zero) return;
            _proc = Proc;
            _hook = W32.SetWindowsHookEx(14 /* WH_MOUSE_LL */, _proc, W32.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Write("Shelf: can't watch for drags (" + Marshal.GetLastWin32Error() + ")");
        }

        public static void Stop()
        {
            if (_hook != IntPtr.Zero) { W32.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            Dragging = false;
            _down = false;
        }

        static IntPtr Proc(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                try
                {
                    int msg = w.ToInt32();
                    if (msg == 0x0201)          // WM_LBUTTONDOWN
                    {
                        var info = Marshal.PtrToStructure<W32.MSLLHOOKSTRUCT>(l);
                        _down = true;
                        _at = info.pt;
                        Dragging = false;
                        _candidate = IsSource(info.pt);
                    }
                    else if (msg == 0x0200 && _down && _candidate && !Dragging)      // WM_MOUSEMOVE
                    {
                        var info = Marshal.PtrToStructure<W32.MSLLHOOKSTRUCT>(l);
                        int dx = info.pt.X - _at.X, dy = info.pt.Y - _at.Y;
                        if (dx * dx + dy * dy >= 30 * 30)
                        {
                            GUITHREADINFO gi;
                            bool known = Thread(out gi);
                            if (known && (gi.flags & 0x2 /* GUI_INMOVESIZE */) != 0) _candidate = false;      // a window being moved or sized, not a drag
                            else if (!_needDragDrop || (known && DragDropCapture(gi.capture)))
                            {
                                Dragging = true;
                                Post(_started);
                            }
                            // (else: a WPF app's own drag - a tab moved, a selection box - unless Windows' drag-and-drop starts as it goes on)
                        }
                    }
                    else if (msg == 0x0202)     // WM_LBUTTONUP
                    {
                        _down = false;
                        if (Dragging) { Dragging = false; Post(_ended); }
                    }
                }
                catch { }
            }
            return W32.CallNextHookEx(_hook, code, w, l);
        }

        static void Post(Action a)
        {
            Application app = Application.Current;
            if (a != null && app != null) app.Dispatcher.BeginInvoke(a);
        }

        /// <summary>
        /// The press was on a file view, the desktop or a web page (and not in vShell). A WPF app (vXplorer) is one window whatever is
        /// pressed - its tabs, its selection box, its files - so there only Windows' drag-and-drop starting counts (_needDragDrop).
        /// </summary>
        static bool IsSource(W32.POINT p)
        {
            _needDragDrop = false;
            _thread = 0;
            IntPtr h = W32.WindowFromPoint(p);
            if (h == IntPtr.Zero || W32.Ours(h)) return false;
            uint pid;
            _thread = W32.GetWindowThreadProcessId(h, out pid);
            for (int i = 0; i < 5 && h != IntPtr.Zero; i++)
            {
                string cls = W32.ClassOf(h);
                if (Sources.Contains(cls)) return true;
                foreach (string pre in SourcePrefixes)
                    if (cls.StartsWith(pre, StringComparison.Ordinal)) { _needDragDrop = true; return true; }
                h = W32.GetAncestor(h, 1 /* GA_PARENT */);
            }
            return false;
        }

        static bool _needDragDrop;
        static uint _thread;        // the thread of the window pressed

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO { public int cbSize; public uint flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public W32.RECT caretRect; }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

        /// <summary>What the pressed window's thread is doing (moving a window, who has the mouse).</summary>
        static bool Thread(out GUITHREADINFO gi)
        {
            gi = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return _thread != 0 && GetGUIThreadInfo(_thread, ref gi);
        }

        /// <summary>
        /// Windows' drag-and-drop is running: while it does, the mouse is held by drag-and-drop's own window - not by the app's window,
        /// as it is when a WPF app drags something of its own (a tab, a selection box).
        /// </summary>
        static bool DragDropCapture(IntPtr capture)
        {
            return capture != IntPtr.Zero && !W32.ClassOf(capture).StartsWith("HwndWrapper[", StringComparison.Ordinal);
        }
    }
}
