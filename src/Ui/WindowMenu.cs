using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A window's own menu (Restore, Move, Size, Minimize, Maximize, Close and anything the app adds) - what Windows' taskbar shows when
    /// you right-click a thumbnail. Its items are greyed the way the window's own title bar menu greys them, and the choice is carried
    /// out on the window (WM_SYSCOMMAND; Restore / Minimize / Close the way the taskbar does them).
    /// </summary>
    public static class WindowMenu
    {
        [DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr h, bool revert);
        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr m);
        [DllImport("user32.dll")] static extern int EnableMenuItem(IntPtr m, uint id, uint flags);
        [DllImport("user32.dll")] static extern bool SetMenuDefaultItem(IntPtr m, uint id, uint byPos);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr m, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr m);
        [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr m, uint flags, int x, int y, IntPtr owner, IntPtr tpm);

        const uint SC_SIZE = 0xF000, SC_MOVE = 0xF010, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_CLOSE = 0xF060, SC_RESTORE = 0xF120;
        const uint MF_BYCOMMAND = 0, MF_ENABLED = 0, MF_GRAYED = 1, MF_DISABLED = 2, MF_STRING = 0, MF_SEPARATOR = 0x800;
        const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
        const long WS_THICKFRAME = 0x40000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
        const int SW_MAXIMIZE = 3;

        static HwndSource _owner;

        /// <summary>
        /// The menu's owner: a hidden window that can take the focus (the previews never do), so a click elsewhere or Esc closes the
        /// menu - as for a tray icon's menu.
        /// </summary>
        static IntPtr Owner()
        {
            if (_owner == null)
            {
                var p = new HwndSourceParameters("vShell window menu")
                {
                    Width = 0, Height = 0, PositionX = -32000, PositionY = -32000,
                    WindowStyle = unchecked((int)0x80000000),        // WS_POPUP
                    ExtendedWindowStyle = 0x80 | 0x8,                // WS_EX_TOOLWINDOW: never on a taskbar; WS_EX_TOPMOST: with the topmost previews
                };
                _owner = new HwndSource(p);
            }
            return _owner.Handle;
        }

        /// <summary>Show the window's menu at the screen point (device pixels) and do what's picked. Returns once the menu is closed.</summary>
        public static void Show(IntPtr window, int x, int y)
        {
            if (window == IntPtr.Zero || !Native.IsWindow(window)) return;
            IntPtr menu = GetSystemMenu(window, false);
            bool own = menu == IntPtr.Zero || GetMenuItemCount(menu) <= 0;
            if (own) menu = Standard();

            bool min = Native.IsIconic(window), max = Native.IsZoomed(window);
            long style = Native.GetWindowLongAuto(window, -16);   // GWL_STYLE
            Enable(menu, SC_RESTORE, min || max);
            Enable(menu, SC_MOVE, !min && !max);
            Enable(menu, SC_SIZE, !min && !max && (style & WS_THICKFRAME) != 0);
            Enable(menu, SC_MINIMIZE, !min && (style & WS_MINIMIZEBOX) != 0);
            Enable(menu, SC_MAXIMIZE, !max && (style & WS_MAXIMIZEBOX) != 0);
            if (own) SetMenuDefaultItem(menu, SC_CLOSE, 0);

            IntPtr owner = Owner();
            Native.SetForegroundWindow(owner);
            int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, owner, IntPtr.Zero);
            Native.PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);   // WM_NULL: so the next menu opens and closes properly
            if (own) DestroyMenu(menu);
            if (cmd != 0) Run(window, (uint)cmd);
        }

        static void Enable(IntPtr menu, uint id, bool on) { EnableMenuItem(menu, id, MF_BYCOMMAND | (on ? MF_ENABLED : MF_GRAYED | MF_DISABLED)); }

        /// <summary>For a window without a menu of its own: the usual items.</summary>
        static IntPtr Standard()
        {
            IntPtr m = CreatePopupMenu();
            AppendMenuW(m, MF_STRING, (UIntPtr)SC_RESTORE, "&Restore");
            AppendMenuW(m, MF_STRING, (UIntPtr)SC_MOVE, "&Move");
            AppendMenuW(m, MF_STRING, (UIntPtr)SC_SIZE, "&Size");
            AppendMenuW(m, MF_STRING, (UIntPtr)SC_MINIMIZE, "Mi&nimize");
            AppendMenuW(m, MF_STRING, (UIntPtr)SC_MAXIMIZE, "Ma&ximize");
            AppendMenuW(m, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(m, MF_STRING, (UIntPtr)SC_CLOSE, "&Close\tAlt+F4");
            return m;
        }

        static void Run(IntPtr h, uint cmd)
        {
            switch (cmd & 0xFFF0)
            {
                case SC_CLOSE: TaskList.Close(h); break;
                case SC_MINIMIZE: TaskList.Minimize(h); break;
                case SC_RESTORE:
                    if (Native.IsIconic(h)) TaskList.BringToFront(h);
                    else { TaskList.SysCommand(h, SC_RESTORE); TaskList.BringToFront(h); }
                    break;
                case SC_MAXIMIZE: Native.ShowWindow(h, SW_MAXIMIZE); TaskList.BringToFront(h); break;
                case SC_MOVE:
                case SC_SIZE:
                    // keyboard move / size: the window has to be in front for the arrow keys to reach it
                    TaskList.BringToFront(h);
                    TaskList.SysCommand(h, cmd);
                    break;
                default: TaskList.SysCommand(h, cmd); break;   // the app's own items (Properties, Always on top …)
            }
        }
    }
}
