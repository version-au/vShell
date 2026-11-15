using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    public sealed class MonitorInfo
    {
        public IntPtr Handle;
        public string DeviceName;
        public RECT Bounds;
        public RECT WorkArea;
        public bool IsPrimary;
        string _key;
        /// <summary>
        /// The monitor itself, not its slot: Windows' device path for it (model and port), e.g. \\?\DISPLAY#DEL4123#5&amp;…&amp;UID4352#{…}.
        /// \\.\DISPLAY2 names get handed out again when monitors come and go (a reconnected monitor can come back as DISPLAY3),
        /// so a bar or widget remembers this too. "" when Windows doesn't say.
        /// </summary>
        public string Key { get { if (_key == null) _key = MonitorHelper.KeyOf(DeviceName); return _key; } }
    }

    public static class MonitorHelper
    {
        // asked many times a second (every placement, the full-screen check, the wallpaper checks): the same answer for a second.
        // MonitorsChanged() forgets it (called as soon as Windows says the monitors changed).
        static readonly object AllGate = new object();
        static List<MonitorInfo> _all;
        static DateTime _allAt;

        /// <summary>The monitors, primary first then left to right (a fresh list each time; the entries are shared).</summary>
        public static List<MonitorInfo> GetAll()
        {
            lock (AllGate)
                if (_all != null && (DateTime.UtcNow - _allAt).TotalSeconds < 1) return new List<MonitorInfo>(_all);
            var list = new List<MonitorInfo>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data)
            {
                var mi = Query(hMon);
                if (mi != null) list.Add(mi);
                return true;
            }, IntPtr.Zero);
            // primary first, then left-to-right: gives stable, predictable ordering
            list.Sort(delegate (MonitorInfo a, MonitorInfo b)
            {
                if (a.IsPrimary != b.IsPrimary) return a.IsPrimary ? -1 : 1;
                return a.Bounds.Left.CompareTo(b.Bounds.Left);
            });
            lock (AllGate) { _all = list; _allAt = DateTime.UtcNow; }
            return new List<MonitorInfo>(list);
        }

        static MonitorInfo Query(IntPtr hMon)
        {
            var info = new MONITORINFOEX();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFOEX));
            if (!Native.GetMonitorInfo(hMon, ref info)) return null;
            return new MonitorInfo
            {
                Handle = hMon,
                DeviceName = info.szDevice,
                Bounds = info.rcMonitor,
                WorkArea = info.rcWork,
                IsPrimary = (info.dwFlags & 1) != 0
            };
        }

        public static MonitorInfo Primary()
        {
            var all = GetAll();
            foreach (var m in all) if (m.IsPrimary) return m;
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Find by device name; falls back to primary when it is not connected.</summary>
        public static MonitorInfo FindOrPrimary(string deviceName)
        {
            return FindOrPrimary(deviceName, null);
        }

        /// <summary>A remembered monitor (key = MonitorInfo.Key, name = its device name) - or the primary while it isn't connected.</summary>
        public static MonitorInfo FindOrPrimary(string deviceName, string key)
        {
            return Find(deviceName, key) ?? Primary();
        }

        /// <summary>
        /// The remembered monitor if it's connected, else null. With a key only that monitor counts (its old DISPLAYn name may belong to
        /// another monitor now); without one (saved before keys), the name. Nothing remembered = the primary.
        /// </summary>
        public static MonitorInfo Find(string deviceName, string key)
        {
            List<MonitorInfo> all = GetAll();
            if (!string.IsNullOrEmpty(key))
            {
                foreach (var m in all) if (string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)) return m;
                // Windows wouldn't say which monitor a slot is: go by the name
                foreach (var m in all) if (m.Key.Length == 0 && string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return m;
                return null;
            }
            if (!string.IsNullOrEmpty(deviceName))
            {
                foreach (var m in all) if (string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return m;
                return null;
            }
            return Primary();
        }

        /// <summary>The remembered monitor is connected (nothing remembered = the primary, always there).</summary>
        public static bool IsConnected(string deviceName, string key) { return Find(deviceName, key) != null; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        /// <summary>The device path of the (first active) monitor on a display slot, e.g. \\.\DISPLAY2 -> \\?\DISPLAY#DEL4123#…</summary>
        static readonly Dictionary<string, KeyValuePair<string, DateTime>> KeyCache = new Dictionary<string, KeyValuePair<string, DateTime>>(StringComparer.OrdinalIgnoreCase);

        internal static string KeyOf(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return "";
            // asked often (every widget / bar placement): a few seconds' cache - short, as a slot can get another monitor when they change
            lock (KeyCache)
            {
                KeyValuePair<string, DateTime> c;
                if (KeyCache.TryGetValue(deviceName, out c) && (DateTime.UtcNow - c.Value).TotalSeconds < 3) return c.Key;
            }
            string key = ReadKey(deviceName);
            lock (KeyCache) KeyCache[deviceName] = new KeyValuePair<string, DateTime>(key, DateTime.UtcNow);
            return key;
        }

        /// <summary>Forget the cached monitors and keys (the monitors just changed).</summary>
        public static void MonitorsChanged()
        {
            lock (KeyCache) KeyCache.Clear();
            lock (AllGate) _all = null;
        }

        static string ReadKey(string deviceName)
        {
            try
            {
                for (uint i = 0; i < 8; i++)
                {
                    var dd = new DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (!EnumDisplayDevices(deviceName, i, ref dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */)) break;
                    if ((dd.StateFlags & 1 /* ACTIVE */) != 0 && !string.IsNullOrEmpty(dd.DeviceID)) return dd.DeviceID;
                }
            }
            catch (Exception ex) { Log.Write("Monitor key " + deviceName + ": " + ex.Message); }
            return "";
        }

        /// <summary>The monitor's scale (1 = 96 dpi, 1.5 = 150 %).</summary>
        public static double ScaleOf(MonitorInfo m)
        {
            if (m == null) return 1;
            try
            {
                uint dx, dy;
                if (Native.GetDpiForMonitor(m.Handle, 0, out dx, out dy) == 0 && dx > 0) return Math.Max(0.5, dx / 96.0);
            }
            catch { }
            return 1;
        }

        public static MonitorInfo FromPoint(int x, int y)
        {
            POINT p; p.X = x; p.Y = y;
            IntPtr h = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
            return Query(h);
        }

        public static MonitorInfo FromHandle(IntPtr hMon)
        {
            foreach (MonitorInfo m in GetAll()) if (m.Handle == hMon) return m;
            return Primary();
        }

        public static IntPtr HandleFromWindow(IntPtr hwnd)
        {
            return Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        }
    }
}
