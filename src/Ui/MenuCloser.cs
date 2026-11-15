using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Right-click menus of bars, widgets and modules close when you click anywhere else - also on another program, the desktop or the
    /// Windows taskbar. (vShell's windows never take the focus, so Windows doesn't tell the menu it lost it; WPF only closes it for clicks
    /// inside vShell.) While a menu is open, a mouse press outside vShell, Esc, or another window coming in front closes it.
    /// </summary>
    public static class MenuCloser
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);

        static readonly List<ContextMenu> Open = new List<ContextMenu>();
        static DispatcherTimer _timer;
        static bool _installed, _wasDown, _escDown;
        static IntPtr _fgAtOpen;
        static readonly uint Me = (uint)Environment.ProcessId;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(delegate (object s, RoutedEventArgs e)
            {
                var m = s as ContextMenu;
                if (m == null || Open.Contains(m)) return;
                Open.Add(m);
                if (Open.Count == 1)
                {
                    _wasDown = AnyButton();
                    _escDown = Down(0x1B);
                    _fgAtOpen = Native.GetForegroundWindow();
                }
                if (_timer == null)
                {
                    _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
                    _timer.Tick += delegate { Watch(); };
                }
                _timer.Start();
            }));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(delegate (object s, RoutedEventArgs e)
            {
                Open.Remove(s as ContextMenu);
                if (Open.Count == 0 && _timer != null) _timer.Stop();
            }));
        }

        /// <summary>One of the open menus' windows (the menu itself or a sub-menu)?</summary>
        static bool IsMenuWindow(IntPtr root)
        {
            var src = System.Windows.Interop.HwndSource.FromHwnd(root);
            if (src == null || src.RootVisual == null) return false;
            return HasMenu(src.RootVisual, 0);
        }

        static bool HasMenu(System.Windows.DependencyObject d, int depth)
        {
            if (d is ContextMenu || d is MenuItem) return true;
            if (depth > 12) return false;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
                if (HasMenu(System.Windows.Media.VisualTreeHelper.GetChild(d, i), depth + 1)) return true;
            return false;
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        static bool AnyButton() { return Down(0x01) || Down(0x02) || Down(0x04) || Down(0x05) || Down(0x06); }

        static void Watch()
        {
            Open.RemoveAll(delegate (ContextMenu m) { return m == null || !m.IsOpen; });
            if (Open.Count == 0) { if (_timer != null) _timer.Stop(); return; }

            bool close = false;
            // a press that starts outside vShell (another program, the desktop, Windows' own taskbar)
            bool down = AnyButton();
            if (down && !_wasDown)
            {
                IntPtr h = WindowFromPoint(Native.CursorPos());
                uint pid = 0;
                if (h != IntPtr.Zero) Native.GetWindowThreadProcessId(h, out pid);
                if (pid != Me) close = true;
                else if (!IsMenuWindow(Native.GetAncestor(h, 2))) close = true;      // a vShell bar / widget, not the menu (or its sub-menus)
            }
            _wasDown = down;
            // Esc (the menu often has no keyboard focus to see it)
            bool esc = Down(0x1B);
            if (esc && !_escDown) close = true;
            _escDown = esc;
            // another program came in front (Alt+Tab, a window that popped up)
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != _fgAtOpen && fg != IntPtr.Zero)
            {
                uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                if (pid != Me) close = true;
                else _fgAtOpen = fg;
            }
            if (!close) return;
            foreach (ContextMenu m in Open.ToArray())
                try { m.IsOpen = false; } catch { }
            Open.Clear();
            if (_timer != null) _timer.Stop();
        }
    }
}
