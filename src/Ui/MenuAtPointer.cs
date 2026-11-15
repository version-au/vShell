using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Every right-click menu that opens at the mouse pointer (MousePoint, or AbsolutePoint as the terminal's do) really ends up at the
    /// pointer. Once the terminal's text window (WebView2) is in use, WPF can put these menus in the top-left corner of the screen - even
    /// when told the exact spot - so after a menu opens vShell checks where its window landed and, if it isn't at the pointer, moves the
    /// window there itself (kept on the pointer's monitor). The first time, the log says what WPF did.
    /// </summary>
    public static class MenuAtPointer
    {
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);

        static bool _installed;
        static int _logged;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
        }

        static void OnOpened(object sender, RoutedEventArgs e)
        {
            var m = sender as ContextMenu;
            if (m == null) return;
            PlacementMode how = m.Placement;
            if (how != PlacementMode.MousePoint && how != PlacementMode.AbsolutePoint) return;
            POINT cur = Native.CursorPos();
            // once laid out and placed by WPF: where did its window go?
            m.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { Check(m, how, cur); }));
        }

        static void Check(ContextMenu m, PlacementMode how, POINT cur)
        {
            try
            {
                if (!m.IsOpen) return;
                var src = PresentationSource.FromVisual(m) as HwndSource;
                if (src == null) return;
                IntPtr h = src.Handle;
                RECT r;
                if (!Native.GetWindowRect(h, out r)) return;
                int w = r.Right - r.Left, ht = r.Bottom - r.Top;
                // at the pointer (or nudged a little by WPF to stay on screen): nothing to do
                bool near = cur.X >= r.Left - 48 && cur.X <= r.Right + 48 && cur.Y >= r.Top - 48 && cur.Y <= r.Bottom + 48;
                if (_logged < 3)
                {
                    _logged++;
                    string dpi = "?";
                    try { dpi = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()).ToString(); } catch { }
                    Log.Write("Menus: " + how + " menu opened at " + r.Left + "," + r.Top + " (" + w + "x" + ht + "), pointer at " + cur.X + "," + cur.Y +
                              ", offsets " + Math.Round(m.HorizontalOffset) + "," + Math.Round(m.VerticalOffset) + ", thread DPI awareness " + dpi +
                              (near ? "" : " - moved to the pointer"));
                }
                if (near) return;

                // put it at the pointer, kept inside the pointer's monitor
                int x = cur.X, y = cur.Y;
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                IntPtr mon = Native.MonitorFromPoint(cur, 2 /*MONITOR_DEFAULTTONEAREST*/);
                if (mon != IntPtr.Zero && Native.GetMonitorInfo(mon, ref mi))
                {
                    RECT a = mi.rcWork;
                    if (x + w > a.Right) x = Math.Max(a.Left, cur.X - w);
                    if (y + ht > a.Bottom) y = Math.Max(a.Top, cur.Y - ht);
                }
                Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Write("Menus: " + ex.Message); }
        }
    }
}
