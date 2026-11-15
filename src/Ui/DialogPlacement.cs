using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Questions and windows open on the right monitor. Windows puts a message box (and a file picker) on the main monitor, and a window
    /// that opens "in the middle of the screen" there too - while the module you clicked may be on another one. So the first time one
    /// of vShell's dialogs or centred windows comes up, vShell checks which monitor it's on and, if it's the wrong one, moves it to the
    /// middle of the right one: the monitor of the window it belongs to (the bar, popup or settings window it was asked from), else the
    /// one the mouse pointer is on. A dialog that belongs to an always-on-top window (Questions' helper, for a widget) is put on top and
    /// given the keyboard. Windows that place themselves (bars, popups, menus, the terminal) are left alone.
    /// </summary>
    public static class DialogPlacement
    {
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        const int WH_CBT = 5, HCBT_DESTROYWND = 4, HCBT_ACTIVATE = 5, GW_OWNER = 4;
        static HookProc _proc;      // kept, so the delegate isn't collected while Windows calls it
        static IntPtr _hook;
        static readonly HashSet<IntPtr> Done = new HashSet<IntPtr>();
        /// <summary>A window that should open on a particular monitor (Questions.Own: the widget's), by a point on it.</summary>
        static readonly ConditionalWeakTable<Window, object> Wanted = new ConditionalWeakTable<Window, object>();
        static int _logged;

        /// <summary>Watch the dialogs of this (the UI) thread. Once, at start-up.</summary>
        public static void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = Hook;
            _hook = SetWindowsHookEx(WH_CBT, _proc, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero) Log.Write("Dialogs: couldn't watch where dialogs open (" + Marshal.GetLastWin32Error() + ")");
        }

        /// <summary>Open this window on the monitor this window is on (a widget asking), instead of the pointer's.</summary>
        public static void Prefer(Window dialog, Window on)
        {
            IntPtr h = on != null ? new WindowInteropHelper(on).Handle : IntPtr.Zero;
            RECT r;
            if (h == IntPtr.Zero || !Native.GetWindowRect(h, out r)) return;
            var p = new POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 };
            Wanted.AddOrUpdate(dialog, p);
        }

        /// <summary>The middle of the monitor this window is on (or the pointer's, for none) - where Questions' helper goes.</summary>
        public static POINT Centre(Window on)
        {
            IntPtr h = on != null ? new WindowInteropHelper(on).Handle : IntPtr.Zero;
            IntPtr mon = h != IntPtr.Zero ? Native.MonitorFromWindow(h, 2) : Native.MonitorFromPoint(Native.CursorPos(), 2);
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (mon == IntPtr.Zero || !Native.GetMonitorInfo(mon, ref mi)) return Native.CursorPos();
            return new POINT { X = (mi.rcWork.Left + mi.rcWork.Right) / 2, Y = (mi.rcWork.Top + mi.rcWork.Bottom) / 2 };
        }

        static IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code == HCBT_DESTROYWND) Done.Remove(w);
                else if (code == HCBT_ACTIVATE && !Done.Contains(w)) Place(w);
            }
            catch (Exception ex) { Log.Write("Dialogs: " + ex.Message); }
            return CallNextHookEx(_hook, code, w, l);
        }

        /// <summary>A window is about to be activated for the first time: if it's a dialog or a centred window on the wrong monitor, move it.</summary>
        static void Place(IntPtr h)
        {
            var sb = new StringBuilder(64);
            Native.GetClassName(h, sb, sb.Capacity);
            bool dialog = sb.ToString() == "#32770";        // a message box, a file / folder picker …
            object wanted = null;
            if (!dialog)
            {
                // one of vShell's WPF windows that leaves its place to Windows (CenterScreen / CenterOwner); the others place themselves
                HwndSource src = HwndSource.FromHwnd(h);
                Window win = src != null ? src.RootVisual as Window : null;
                if (win == null || win.WindowStartupLocation == WindowStartupLocation.Manual) return;
                Wanted.TryGetValue(win, out wanted);
            }
            Done.Add(h);

            IntPtr owner = Native.GetWindow(h, GW_OWNER);
            bool owned = owner != IntPtr.Zero && Native.IsWindowVisible(owner);
            IntPtr mon = wanted is POINT ? Native.MonitorFromPoint((POINT)wanted, 2)
                       : owned ? Native.MonitorFromWindow(owner, 2)
                       : Native.MonitorFromPoint(Native.CursorPos(), 2);
            if (owned && (Native.GetWindowLongAuto(owner, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0)
            {
                // asked from a widget (through Questions' always-on-top helper): in front of everything, with the keyboard
                Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE);
                Native.SetForegroundWindow(h);
            }
            Centre(h, mon);
        }

        /// <summary>Move the window to the middle of the monitor's work area - unless it's on that monitor already.</summary>
        static void Centre(IntPtr h, IntPtr mon)
        {
            RECT r;
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (mon == IntPtr.Zero || !Native.GetWindowRect(h, out r) || !Native.GetMonitorInfo(mon, ref mi)) return;
            var mid = new POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 };
            if (Native.MonitorFromPoint(mid, 2) == mon) return;
            RECT a = mi.rcWork;
            int x = a.Left + Math.Max(0, (a.Width - r.Width) / 2), y = a.Top + Math.Max(0, (a.Height - r.Height) / 2);
            if (_logged < 5)
            {
                _logged++;
                var sb = new StringBuilder(64);
                Native.GetClassName(h, sb, sb.Capacity);
                Log.Write("Dialogs: moved a " + sb + " window from " + r + " to " + x + "," + y + " (monitor " + mi.szDevice + ")");
            }
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }
}
