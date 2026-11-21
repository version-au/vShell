using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace vShell.Core
{
    /// <summary>Living alongside Explorer: its taskbar (hidden / shown again), undoing an old "vShell is the shell" setting, minimised windows.</summary>
    public static class ShellServices
    {
        /// <summary>Our own stand-in Shell_TrayWnd when the tray runs alongside Explorer (never treat it as Explorer's taskbar).</summary>
        public static IntPtr OwnTray = IntPtr.Zero;

        /// <summary>Explorer's own Shell_TrayWnd, skipping ours.</summary>
        static IntPtr RealTray()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
                if (h != OwnTray) return h;
            return IntPtr.Zero;
        }

        public static IntPtr ExplorerTaskbarHandle() { return RealTray(); }

        // ------------------------------------------------------------------
        // Get the Windows taskbar out of the way
        // ------------------------------------------------------------------
        static int _savedState = -1;
        static IEnumerable<IntPtr> ExplorerTaskbars()
        {
            foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr h = IntPtr.Zero;
                while ((h = Native.FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                    if (h != OwnTray) yield return h;
            }
        }

        public static void HideExplorerTaskbar()
        {
            IntPtr tray = RealTray();
            if (tray == IntPtr.Zero) return;

            var abd = new APPBARDATA();
            abd.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = tray;
            if (_savedState < 0)
            {
                _savedState = (int)(uint)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);
                if (_savedState == 0) _savedState = Native.ABS_ALWAYSONTOP;
            }
            abd.lParam = new IntPtr(_savedState | Native.ABS_AUTOHIDE);
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
            KeepHidden();
        }

        /// <summary>
        /// One of Explorer's taskbars sits on this edge of this monitor (hidden or not). After a monitor wakes up Explorer can make its
        /// taskbar there again and fail to auto-hide it ("A toolbar is already hidden on this side of your screen"): it then keeps
        /// its strip of space, hidden. A bar asking Windows where it may go is pushed in by that strip - vShell ignores it.
        /// </summary>
        public static bool ExplorerTaskbarOnEdge(RECT mon, bool top)
        {
            int mid = mon.Top + (mon.Bottom - mon.Top) / 2;
            foreach (IntPtr h in ExplorerTaskbars())
            {
                RECT r;
                if (!Native.GetWindowRect(h, out r)) continue;
                if (r.Right <= mon.Left || r.Left >= mon.Right || r.Right - r.Left < (mon.Right - mon.Left) / 2) continue;   // another monitor, or a side taskbar
                if (top ? r.Top <= mon.Top + 4 && r.Bottom < mid : r.Bottom >= mon.Bottom - 4 && r.Top > mid) return true;
            }
            return false;
        }

        /// <summary>Explorer's taskbars for the log: "0,1392 2560x48 hidden; …".</summary>
        public static string DescribeTaskbars()
        {
            var parts = new List<string>();
            foreach (IntPtr h in ExplorerTaskbars())
            {
                RECT r;
                if (!Native.GetWindowRect(h, out r)) continue;
                parts.Add(r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) +
                          (Native.IsWindowVisible(h) ? "" : " hidden"));
            }
            return parts.Count == 0 ? "none" : string.Join("; ", parts);
        }

        /// <summary>Explorer re-shows its taskbar now and then; call this from a timer.</summary>
        public static void KeepHidden()
        {
            foreach (IntPtr h in ExplorerTaskbars())
                if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
        }

        // ---- "default shell" (per-user Winlogon\Shell override): an old vShell could set itself as the shell; undone at start --------
        const string WinlogonKey = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

        /// <summary>The per-user shell override, or null if none is set (Windows then starts Explorer).</summary>
        public static string GetShellOverride()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(WinlogonKey))
                    return k == null ? null : k.GetValue("Shell") as string;
            }
            catch { return null; }
        }

        /// <summary>true if the per-user shell override points at this program.</summary>
        public static bool IsDefaultShell()
        {
            string v = GetShellOverride();
            string me = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(v) || string.IsNullOrEmpty(me)) return false;
            return v.Trim().Trim('"').Equals(me, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Remove the per-user override so Windows starts Explorer again at the next sign-in. Returns an error message or null.</summary>
        public static string RestoreDefaultShell()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(WinlogonKey, true))
                    if (k != null) k.DeleteValue("Shell", false);
                return null;
            }
            catch (Exception ex) { Log.Write("RestoreDefaultShell: " + ex.Message); return ex.Message; }
        }

        public static void RestoreExplorerTaskbar()
        {
            IntPtr tray = RealTray();
            if (tray == IntPtr.Zero) return;
            var abd = new APPBARDATA();
            abd.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = tray;
            int state = _savedState >= 0 ? (_savedState & ~Native.ABS_AUTOHIDE) : Native.ABS_ALWAYSONTOP;
            if (state == 0) state = Native.ABS_ALWAYSONTOP;
            abd.lParam = new IntPtr(state);
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
            foreach (IntPtr h in ExplorerTaskbars()) Native.ShowWindow(h, Native.SW_SHOWNA);
            _savedState = -1;
        }

        // ------------------------------------------------------------------
        // Minimised windows: without Explorer, Windows parks each one as a small caption box at the bottom-left of the screen.
        // Hide them (the bar's taskbar restores windows), and put the setting back on exit.
        // ------------------------------------------------------------------
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MINIMIZEDMETRICS { public uint cbSize; public int iWidth, iHorzGap, iVertGap, iArrange; }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        static extern bool SpiMinMetrics(uint action, uint param, ref MINIMIZEDMETRICS m, uint winIni);
        static int _savedArrange = -1;

        public static void HideMinimizedBoxes()
        {
            try
            {
                var m = new MINIMIZEDMETRICS { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MINIMIZEDMETRICS)) };
                if (!SpiMinMetrics(0x002B /*GET*/, m.cbSize, ref m, 0)) return;
                if (_savedArrange < 0) _savedArrange = m.iArrange;
                m.iArrange = 0x8 /*ARW_HIDE*/;
                bool ok = SpiMinMetrics(0x002C /*SET*/, m.cbSize, ref m, 0x1 | 0x2 /*UPDATEINIFILE|SENDCHANGE*/);
                Log.Write("MinimizedMetrics ARW_HIDE set: " + ok);
            }
            catch (Exception ex) { Log.Write("HideMinimizedBoxes: " + ex.Message); }
        }

        /// <summary>
        /// Backstop for the setting above: move the on-screen caption box of every minimised window to the (-32000,-32000) spot
        /// Windows itself uses when minimised windows are hidden. Cheap; called a few times per second.
        /// </summary>
        public static void ParkMinimized()
        {
            try
            {
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    if (!Native.IsWindowVisible(h) || !Native.IsIconic(h)) return true;
                    RECT r;
                    if (Native.GetWindowRect(h, out r) && r.Left > -30000)
                        Native.SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        public static void RestoreMinimizedBoxes()
        {
            if (_savedArrange < 0) return;
            try
            {
                var m = new MINIMIZEDMETRICS { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MINIMIZEDMETRICS)) };
                if (!SpiMinMetrics(0x002B, m.cbSize, ref m, 0)) return;
                m.iArrange = _savedArrange;
                SpiMinMetrics(0x002C, m.cbSize, ref m, 0);
                _savedArrange = -1;
            }
            catch { }
        }
    }
}
