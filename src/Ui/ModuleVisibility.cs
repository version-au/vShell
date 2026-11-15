using System;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Whether anyone can see a module right now - for a module that animates (Power flow's dots), so it can rest when nobody's looking.
    /// </summary>
    [PluginApi]
    public static class ModuleVisibility
    {
        static bool _locked, _hooked;

        /// <summary>The PC is locked (or the session is disconnected).</summary>
        public static bool Locked { get { Hook(); return _locked; } }

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                Microsoft.Win32.SystemEvents.SessionSwitch += delegate (object s, Microsoft.Win32.SessionSwitchEventArgs e)
                {
                    if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect || e.Reason == Microsoft.Win32.SessionSwitchReason.RemoteDisconnect) _locked = true;
                    else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleConnect || e.Reason == Microsoft.Win32.SessionSwitchReason.RemoteConnect) _locked = false;
                };
            }
            catch { }
        }

        /// <summary>The PC is locked, or a maximised / full-screen window covers the module's (not always-on-top) bar or widget.</summary>
        public static bool Covered(IModuleHost host)
        {
            if (Locked) return true;
            try
            {
                IntPtr me = host.Handle;
                if (me == IntPtr.Zero) return false;
                if ((Native.GetWindowLongAuto(me, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0) return false;
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == me) return false;
                if (Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST) != Native.MonitorFromWindow(me, Native.MONITOR_DEFAULTTONEAREST)) return false;
                if (Native.IsZoomed(fg)) return true;
                // full screen without being "maximised" (games, videos): its rectangle is the whole monitor
                RECT r;
                MonitorInfo m = MonitorHelper.FromHandle(Native.MonitorFromWindow(me, Native.MONITOR_DEFAULTTONEAREST));
                return m != null && Native.GetWindowRect(fg, out r) && r.Left <= m.Bounds.Left && r.Top <= m.Bounds.Top && r.Right >= m.Bounds.Right && r.Bottom >= m.Bounds.Bottom;
            }
            catch { return false; }
        }
    }
}
