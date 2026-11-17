using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The PC is locked, a maximised / full-screen window covers the module's (not always-on-top) bar or widget, or app windows in
        /// front of it hide all but a sliver of it (0.38.224).
        /// </summary>
        public static bool Covered(IModuleHost host)
        {
            if (Locked) return true;
            try
            {
                IntPtr me = host.Handle;
                if (me == IntPtr.Zero) return false;
                if ((Native.GetWindowLongAuto(me, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0) return false;
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != me && Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST) == Native.MonitorFromWindow(me, Native.MONITOR_DEFAULTTONEAREST))
                {
                    if (Native.IsZoomed(fg)) return true;
                    // full screen without being "maximised" (games, videos): its rectangle is the whole monitor
                    RECT r;
                    MonitorInfo m = MonitorHelper.FromHandle(Native.MonitorFromWindow(me, Native.MONITOR_DEFAULTTONEAREST));
                    if (m != null && Native.GetWindowRect(fg, out r) && r.Left <= m.Bounds.Left && r.Top <= m.Bounds.Top && r.Right >= m.Bounds.Right && r.Bottom >= m.Bounds.Bottom) return true;
                }
                return HiddenBehindApps(me);
            }
            catch { return false; }
        }

        // ---- hidden behind app windows (0.38.224, after desk-widgets' IsCovered) -------------------------------------------------
        /// <summary>The app windows the taskbar lists (BarManager sets it): only real windows count - an invisible overlay (a game's,
        /// a recorder's) is never on the taskbar, so it can't make a widget that's on show look hidden.</summary>
        internal static Func<IEnumerable<IntPtr>> AppWindows;
        static readonly Dictionary<IntPtr, KeyValuePair<int, bool>> _hiddenCache = new Dictionary<IntPtr, KeyValuePair<int, bool>>();

        /// <summary>Less than 5 % of the window still shows past the app windows in front of it (checked at most twice a second).</summary>
        internal static bool HiddenBehindApps(IntPtr me)
        {
            Func<IEnumerable<IntPtr>> apps = AppWindows;
            if (apps == null || me == IntPtr.Zero) return false;
            int now = Environment.TickCount;
            KeyValuePair<int, bool> c;
            if (_hiddenCache.TryGetValue(me, out c) && unchecked(now - c.Key) < 500) return c.Value;
            bool hidden = false;
            try { hidden = ComputeHidden(me, apps()); } catch { }
            if (_hiddenCache.Count > 200) _hiddenCache.Clear();
            _hiddenCache[me] = new KeyValuePair<int, bool>(now, hidden);
            return hidden;
        }

        static bool ComputeHidden(IntPtr me, IEnumerable<IntPtr> apps)
        {
            RECT mine;
            if (!Native.GetWindowRect(me, out mine) || mine.Width <= 0 || mine.Height <= 0) return false;
            var wanted = new HashSet<IntPtr>(apps);
            if (wanted.Count == 0) return false;
            // only what's in front of this window: walk the z-order up from it
            var showing = new List<RECT> { mine };
            int guard = 0;
            for (IntPtr w = Native.GetWindow(me, Native.GW_HWNDPREV); w != IntPtr.Zero && guard < 2000; w = Native.GetWindow(w, Native.GW_HWNDPREV), guard++)
            {
                if (!wanted.Contains(w) || !Native.IsWindowVisible(w) || Native.IsIconic(w)) continue;
                int cloaked;
                if (Native.DwmGetWindowAttribute(w, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) continue;   // another virtual desktop
                RECT r;
                if (Native.DwmGetWindowAttribute(w, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, System.Runtime.InteropServices.Marshal.SizeOf(typeof(RECT))) != 0 && !Native.GetWindowRect(w, out r)) continue;
                showing = Subtract(showing, r);
                if (showing.Count == 0) break;
            }
            long left = 0;
            foreach (RECT s in showing) left += (long)s.Width * s.Height;
            return left * 20 < (long)mine.Width * mine.Height;      // under 5 % still showing
        }

        /// <summary>The parts of the rectangles not under cut.</summary>
        internal static List<RECT> Subtract(List<RECT> from, RECT cut)
        {
            var o = new List<RECT>();
            foreach (RECT a in from)
            {
                if (cut.Right <= a.Left || cut.Left >= a.Right || cut.Bottom <= a.Top || cut.Top >= a.Bottom) { o.Add(a); continue; }
                if (cut.Top > a.Top) o.Add(new RECT { Left = a.Left, Top = a.Top, Right = a.Right, Bottom = cut.Top });
                if (cut.Bottom < a.Bottom) o.Add(new RECT { Left = a.Left, Top = cut.Bottom, Right = a.Right, Bottom = a.Bottom });
                int top = Math.Max(a.Top, cut.Top), bottom = Math.Min(a.Bottom, cut.Bottom);
                if (cut.Left > a.Left) o.Add(new RECT { Left = a.Left, Top = top, Right = cut.Left, Bottom = bottom });
                if (cut.Right < a.Right) o.Add(new RECT { Left = cut.Right, Top = top, Right = a.Right, Bottom = bottom });
            }
            return o;
        }
    }
}
