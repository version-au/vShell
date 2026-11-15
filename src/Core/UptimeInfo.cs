using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace vShell.Core
{
    /// <summary>
    /// What the Uptime module shows: when Windows last restarted, how much of that it was awake, the last sleep / wake, when you signed in,
    /// whether Fast Startup is on (then shutting down doesn't reset the uptime - only Restart does) and whether Windows is waiting for a
    /// restart. Everything is cheap to read; the registry checks are cached.
    /// </summary>
    public static class UptimeInfo
    {
        [DllImport("kernel32.dll")] static extern bool QueryUnbiasedInterruptTime(out ulong t);
        [DllImport("kernelbase.dll")] static extern void QueryInterruptTime(out ulong t);
        [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int level, IntPtr inBuf, int inLen, out ulong outBuf, int outLen);
        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")] static extern bool WTSQuerySessionInformation(IntPtr server, int session, int cls, out IntPtr buf, out int bytes);
        [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);

        const int LastWakeTime = 14, LastSleepTime = 15, WTSSessionInfo = 24;

        static DateTime _sleptAt = DateTime.MinValue, _wokeAt = DateTime.MinValue;     // seen while vShell was running (local)
        static bool _hooked;

        /// <summary>Since Windows last started (a restart, or a full shut down with Fast Startup off). Sleep counts.</summary>
        public static TimeSpan Up { get { return TimeSpan.FromMilliseconds(Environment.TickCount64); } }
        public static DateTime Booted { get { return DateTime.Now - Up; } }

        /// <summary>Of the uptime, the time it wasn't asleep / hibernating.</summary>
        public static TimeSpan Awake
        {
            get
            {
                ulong t;
                try { if (QueryUnbiasedInterruptTime(out t)) return TimeSpan.FromTicks((long)t); } catch { }
                return Up;
            }
        }

        /// <summary>Start noticing sleep / wake (more exact than Windows' own figures, which some PCs don't keep).</summary>
        public static void Watch()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                SystemEvents.PowerModeChanged += delegate (object s, PowerModeChangedEventArgs e)
                {
                    if (e.Mode == PowerModes.Suspend) _sleptAt = DateTime.Now;
                    else if (e.Mode == PowerModes.Resume) _wokeAt = DateTime.Now;
                };
            }
            catch (Exception ex) { Log.Write("Uptime: " + ex.Message); }
        }

        /// <summary>Windows' own record of the last sleep / wake since it started (MinValue = none).</summary>
        static DateTime PowerTime(int level)
        {
            try
            {
                ulong at, now;
                if (CallNtPowerInformation(level, IntPtr.Zero, 0, out at, 8) != 0 || at == 0) return DateTime.MinValue;
                QueryInterruptTime(out now);
                if (at > now) return DateTime.MinValue;
                DateTime t = DateTime.Now - TimeSpan.FromTicks((long)(now - at));
                return t < Booted ? DateTime.MinValue : t;
            }
            catch { return DateTime.MinValue; }
        }

        /// <summary>The last time it woke from sleep / hibernate since it started (MinValue = it hasn't slept).</summary>
        public static DateTime LastWake
        {
            get
            {
                DateTime w = PowerTime(LastWakeTime);
                return _wokeAt > w ? _wokeAt : w;
            }
        }

        /// <summary>The last time it went to sleep since it started (MinValue = it hasn't).</summary>
        public static DateTime LastSleep
        {
            get
            {
                DateTime s = PowerTime(LastSleepTime);
                return _sleptAt > s ? _sleptAt : s;
            }
        }

        static DateTime _signIn = DateTime.MinValue;

        /// <summary>When you signed in to this session (MinValue = unknown).</summary>
        public static DateTime SignedIn
        {
            get
            {
                if (_signIn != DateTime.MinValue) return _signIn;
                IntPtr buf;
                int n;
                try
                {
                    if (!WTSQuerySessionInformation(IntPtr.Zero, -1, WTSSessionInfo, out buf, out n)) return DateTime.MinValue;
                    try
                    {
                        // WTSINFOW: two DWORDs, six counters, three names (32 + 17 + 21 wide chars), then the times - LogonTime is the fourth
                        if (n >= 216)
                        {
                            long logon = Marshal.ReadInt64(buf, 200);
                            if (logon > 0) _signIn = DateTime.FromFileTime(logon);
                        }
                    }
                    finally { WTSFreeMemory(buf); }
                }
                catch (Exception ex) { Log.Write("Uptime sign-in: " + ex.Message); }
                return _signIn;
            }
        }

        static bool _fast;
        static DateTime _fastAt = DateTime.MinValue;

        /// <summary>Fast Startup is on: "Shut down" hibernates Windows, so only Restart starts the uptime again.</summary>
        public static bool FastStartup
        {
            get
            {
                if ((DateTime.UtcNow - _fastAt).TotalMinutes < 5) return _fast;
                _fastAt = DateTime.UtcNow;
                try
                {
                    object hb = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
                    object he = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1);
                    _fast = Convert.ToInt32(hb ?? 0) == 1 && Convert.ToInt32(he ?? 1) != 0;
                }
                catch { _fast = false; }
                return _fast;
            }
        }

        static string _pending;
        static DateTime _pendingAt = DateTime.MinValue;

        /// <summary>Why Windows is waiting for a restart ("Windows Update has updates to finish"), or null.</summary>
        public static string RestartPending
        {
            get
            {
                if ((DateTime.UtcNow - _pendingAt).TotalSeconds < 60) return _pending;
                _pendingAt = DateTime.UtcNow;
                _pending = null;
                try
                {
                    if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                        _pending = "Windows Update has updates to finish";
                    else if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                        _pending = "Windows has a feature or update to finish installing";
                }
                catch { }
                return _pending;
            }
        }

        static bool KeyExists(string path)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path)) return k != null;
        }

        /// <summary>
        /// The time a module counts: "Last restart" (the uptime), "Awake time" (the uptime less sleep), "Last wake-up" (since it last woke,
        /// or the uptime if it hasn't slept), "Sign-in" (since you signed in). since = when that started (MinValue for Awake time).
        /// </summary>
        public static TimeSpan Counted(string from, out DateTime since)
        {
            DateTime now = DateTime.Now;
            switch (from)
            {
                case "Awake time":
                    since = DateTime.MinValue;
                    return Awake;
                case "Last wake-up":
                    {
                        DateTime w = LastWake;
                        since = w != DateTime.MinValue ? w : Booted;
                        return now - since;
                    }
                case "Sign-in":
                    {
                        DateTime s = SignedIn;
                        since = s != DateTime.MinValue ? s : Booted;
                        return now - since;
                    }
                default:
                    since = Booted;
                    return Up;
            }
        }

        /// <summary>
        /// A length of time as the module shows it. "Short": 3d 4h (4h 12m, 12m); "Long": 3 days, 4 hours; "Decimal": 3.2d (4.2h, 12m).
        /// minutes = also the minutes when it's a day or more (Short / Long).
        /// </summary>
        public static string Format(TimeSpan t, string format, bool minutes)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int days = (int)t.TotalDays, hours = t.Hours, mins = t.Minutes;
            switch (format)
            {
                case "Long":
                    if (days >= 1) return Plural(days, "day") + ", " + Plural(hours, "hour") + (minutes ? ", " + Plural(mins, "minute") : "");
                    if (t.TotalHours >= 1) return Plural(hours, "hour") + ", " + Plural(mins, "minute");
                    return Plural(mins, "minute");
                case "Decimal":
                    if (days >= 1) return t.TotalDays.ToString("0.0", CultureInfo.CurrentCulture) + "d";
                    if (t.TotalHours >= 1) return t.TotalHours.ToString("0.0", CultureInfo.CurrentCulture) + "h";
                    return mins + "m";
                default:
                    if (days >= 1) return days + "d " + hours + "h" + (minutes ? " " + mins + "m" : "");
                    if (t.TotalHours >= 1) return hours + "h " + mins + "m";
                    return mins + "m";
            }
        }

        static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }
    }
}
