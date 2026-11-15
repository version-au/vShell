using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace vShell.Core
{
    /// <summary>
    /// The notification sources watched here every few seconds (the plugins post their own) -
    /// Service status, System (temperatures, disk, battery, memory), rain on the way, and the plugins' Watchers.
    /// Each one remembers what it saw last time and posts to Notify when something changes.
    /// </summary>
    public static class NotifyWatch
    {
        static BarSettings _s;
        static Timer _timer;
        static int _busy;
        static readonly object Gate = new object();

        public static void Configure(BarSettings s)
        {
            _s = s;
            Notify.Configure(s);
            lock (Gate) if (_timer == null) _timer = new Timer(delegate { Tick(); }, null, 4000, 5000);
            NotifyListener.Configure(s);
        }

        // =================================================================
        // The watched sources
        // =================================================================
        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null) return;
                Safe("Services", delegate { Services(s); });
                Safe("System", delegate { SystemChecks(s); });
                foreach (KeyValuePair<string, Action<BarSettings>> w in WatchersNow) { Action<BarSettings> run = w.Value; Safe(w.Key, delegate { run(s); }); }
                Safe("Weather", delegate { Rain(s); });
                Safe("Waiting notices", delegate { DownDelay.Tick(); });
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>The plugins' own checks (name, check) - run here every 5 seconds with the others (Jellyfin's who's playing).</summary>
        public static readonly List<KeyValuePair<string, Action<BarSettings>>> Watchers = new List<KeyValuePair<string, Action<BarSettings>>>();
        static KeyValuePair<string, Action<BarSettings>>[] WatchersNow { get { lock (Watchers) return Watchers.ToArray(); } }

        static readonly Dictionary<string, string> LastError = new Dictionary<string, string>();

        static void Safe(string what, Action a)
        {
            try { a(); lock (LastError) LastError.Remove(what); }
            catch (Exception ex)
            {
                string m = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                string old;
                lock (LastError)
                {
                    if (LastError.TryGetValue(what, out old) && old == m) return;
                    LastError[what] = m;
                }
                Log.Write("Notifications (" + what + "): " + m);
            }
        }

        /// <summary>A cooldown: true (and the time is noted) when key may notify again.</summary>
        static readonly Dictionary<string, DateTime> Last = new Dictionary<string, DateTime>();
        static bool Due(string key, double minutes)
        {
            lock (Last)
            {
                DateTime t;
                if (Last.TryGetValue(key, out t) && (DateTime.UtcNow - t).TotalMinutes < minutes) return false;
                Last[key] = DateTime.UtcNow;
                return true;
            }
        }

        // ---- Service status ----
        sealed class SvcState { public DateTime LastChecked; public int Downs; public bool SawUp; }
        static readonly Dictionary<string, SvcState> Svcs = new Dictionary<string, SvcState>();

        /// <summary>Every Service status check in the bars and widgets (name, check, interval).</summary>
        static List<KeyValuePair<ServiceCheck, int>> AllChecks(BarSettings s)
        {
            var list = new List<KeyValuePair<ServiceCheck, int>>();
            Action<List<ModuleConfig>> add = delegate (List<ModuleConfig> mods)
            {
                if (mods == null) return;
                foreach (ModuleConfig m in mods)
                    if (m != null && m.Type == ModuleTypes.Services && m.Checks != null)
                        foreach (ServiceCheck c in m.Checks) if (c != null && !string.IsNullOrWhiteSpace(c.Target)) list.Add(new KeyValuePair<ServiceCheck, int>(c, m.CheckSeconds));
            };
            foreach (BarPlacement b in s.Bars) add(b.Modules);
            foreach (WidgetSettings w in s.Widgets) add(w.Modules);
            return list;
        }

        static void Services(BarSettings s)
        {
            if (!s.NotifyServices) return;
            foreach (var kv in AllChecks(s))
            {
                ServiceCheck c = kv.Key;
                ServiceStatus st = ServiceMonitor.Status(c, kv.Value);    // keeps it checked
                if (st.Checked == DateTime.MinValue) continue;
                string key = c.Kind + "|" + c.Target;
                SvcState ss;
                lock (Svcs) if (!Svcs.TryGetValue(key, out ss)) { ss = new SvcState(); Svcs[key] = ss; }
                if (st.Checked == ss.LastChecked) continue;
                ss.LastChecked = st.Checked;
                string name = c.Name.Length > 0 ? c.Name : c.Target;
                string url = c.Kind == "Http" ? (c.Target.Contains("://") ? c.Target : "http://" + c.Target) : "";
                if (st.State == ServiceState.Down)
                {
                    ss.Downs++;
                    // two failed checks in a row (one slow reply isn't enough); not when it was never up since vShell started;
                    // then the wait you set (it may be back by then)
                    if (ss.Downs >= 2 && ss.SawUp)
                    {
                        string detail = st.Detail;
                        DownDelay.Down("svc|" + key, s.NotifySvcDelay, delegate { Notify.Post("Services", name + " is down", detail, url, Notify.KindOf("Services.Down")); });
                    }
                }
                else if (st.State == ServiceState.Up || st.State == ServiceState.Slow)
                {
                    if (DownDelay.Up("svc|" + key) == UpResult.WasNotified) Notify.Post("Services", name + " is back up", st.Detail, url, Notify.KindOf("Services.Up"));
                    ss.Downs = 0; ss.SawUp = true;
                }
            }
        }

        // ---- System ----
        static DateTime _cpuHotSince = DateTime.MinValue, _gpuHotSince = DateTime.MinValue, _memHighSince = DateTime.MinValue, _diskAt = DateTime.MinValue;
        static readonly SystemStats Stats = new SystemStats();
        static readonly HashSet<string> DiskLow = new HashSet<string>();
        static bool _batteryWarned;

        static void SystemChecks(BarSettings s)
        {
            double cool = s.NotifySysCooldown;
            if (s.NotifyCpuTemp > 0 || s.NotifyGpuTemp > 0) HwInfo.EnsureFresh();
            if (s.NotifyCpuTemp > 0)
            {
                HwReading r = HwInfo.AutoCpuTemp();
                Hot(r != null ? r.Value : double.NaN, s.NotifyCpuTemp, s.NotifyTempSeconds, ref _cpuHotSince, "cpu", "CPU", cool);
            }
            if (s.NotifyGpuTemp > 0)
            {
                double t;
                if (!Nvml.TryGetTemp(0, out t)) { HwReading r = HwInfo.AutoGpuTemp(); t = r != null ? r.Value : double.NaN; }
                Hot(t, s.NotifyGpuTemp, s.NotifyTempSeconds, ref _gpuHotSince, "gpu", "GPU", cool);
            }
            if (s.NotifyMemory > 0)
            {
                Stats.Update();
                if (Stats.MemPercent >= s.NotifyMemory)
                {
                    if (_memHighSince == DateTime.MinValue) _memHighSince = DateTime.UtcNow;
                    if ((DateTime.UtcNow - _memHighSince).TotalSeconds >= 60 && Due("mem", cool))
                        Notify.Post("System", "Memory is " + Math.Round(Stats.MemPercent) + " % full", Stats.MemUsedGb.ToString("0.0") + " of " + Stats.MemTotalGb.ToString("0.0") + " GB in use", "vshell:taskmgr", Notify.KindOf("System.Warning"));
                }
                else _memHighSince = DateTime.MinValue;
            }
            if (s.NotifyBattery > 0)
            {
                BatteryInfo b = Battery.Read();
                if (b.HasBattery && !b.OnAc && b.Percent >= 0 && b.Percent <= s.NotifyBattery)
                {
                    if (!_batteryWarned)
                    {
                        _batteryWarned = true;
                        Notify.Post("System", "Battery is at " + b.Percent + " %", b.SecondsLeft > 0 ? "About " + (b.SecondsLeft / 3600 > 0 ? b.SecondsLeft / 3600 + " h " : "") + (b.SecondsLeft % 3600 / 60) + " min left" : "Plug in the charger", "", Notify.KindOf("System.Warning"));
                    }
                }
                else if (b.OnAc || b.Percent > s.NotifyBattery + 3) _batteryWarned = false;
            }
            if (s.NotifyDiskFull > 0 && (DateTime.UtcNow - _diskAt).TotalSeconds >= 60)
            {
                _diskAt = DateTime.UtcNow;
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                        double free = 100.0 * d.AvailableFreeSpace / d.TotalSize;
                        double used = 100 - free;
                        string name = d.Name.TrimEnd('\\');
                        if (used > s.NotifyDiskFull)
                        {
                            if (DiskLow.Add(name))
                                Notify.Post("System", "Drive " + name + " is almost full", Math.Round(used) + " % full · " + (d.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " GB left", d.Name, Notify.KindOf("System.Warning"));
                        }
                        else if (used < s.NotifyDiskFull - 2) DiskLow.Remove(name);
                    }
                    catch { }
                }
            }
        }

        static void Hot(double t, int limit, int seconds, ref DateTime since, string key, string what, double cool)
        {
            if (double.IsNaN(t) || t <= 0) return;
            if (t >= limit)
            {
                if (since == DateTime.MinValue) since = DateTime.UtcNow;
                if ((DateTime.UtcNow - since).TotalSeconds >= seconds && Due(key, cool))
                    Notify.Post("System", what + " is at " + Math.Round(t) + " °C", "Over your limit of " + limit + " °C" + (seconds > 0 ? " for " + seconds + " s" : ""), "", Notify.KindOf("System.Warning"));
            }
            else if (t < limit - 3) since = DateTime.MinValue;
        }

        // ---- Weather: rain on the way ----
        static DateTime _rainAt = DateTime.MinValue;
        static HttpClient _rainHttp;

        /// <summary>The place of the first Open-Meteo Weather module in the bars / widgets (null = none).</summary>
        public static ModuleConfig RainPlace(BarSettings s)
        {
            foreach (BarPlacement b in s.Bars) if (b.Modules != null) foreach (ModuleConfig m in b.Modules) if (IsPlace(m)) return m;
            foreach (WidgetSettings w in s.Widgets) if (w.Modules != null) foreach (ModuleConfig m in w.Modules) if (IsPlace(m)) return m;
            return null;
        }

        static bool IsPlace(ModuleConfig m)
        {
            return m != null && m.Type == ModuleTypes.Weather && m.WeatherSource != "HomeAssistant" && !string.IsNullOrWhiteSpace(m.WeatherPlace);
        }

        /// <summary>The forecast being fetched (null = none), and for which place - looked at in a later round, so a slow weather service
        /// never holds up the other checks (it used to wait here for up to 12 s).</summary>
        static System.Threading.Tasks.Task<string> _rainFetch;
        static string _rainWhere = "";

        static void Rain(BarSettings s)
        {
            System.Threading.Tasks.Task<string> f = _rainFetch;
            if (f != null)
            {
                if (!f.IsCompleted) return;          // still on its way: look again next round
                _rainFetch = null;
                string json = f.GetAwaiter().GetResult();       // finished: throws its error (if any) to Safe, which logs it
                if (!s.NotifyRain) return;                      // turned off meanwhile
                string when;
                int chance;
                if (RainSoon(json, s.NotifyRainChance, out when, out chance) && Due("rain", 180))
                    Notify.Post("Weather", "Rain expected around " + when, _rainWhere + (chance > 0 ? " · " + chance + " % chance" : ""), "", Notify.KindOf("Weather.Rain"));
                return;
            }
            if (!s.NotifyRain) return;
            if ((DateTime.UtcNow - _rainAt).TotalMinutes < 15) return;
            _rainAt = DateTime.UtcNow;
            ModuleConfig m = RainPlace(s);
            if (m == null) return;
            if (_rainHttp == null) { _rainHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(12) }; _rainHttp.DefaultRequestHeaders.UserAgent.ParseAdd("vShell/1.0 (rain notification)"); }
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + m.WeatherLat.ToString("0.####", CultureInfo.InvariantCulture) +
                         "&longitude=" + m.WeatherLon.ToString("0.####", CultureInfo.InvariantCulture) +
                         "&current=precipitation&minutely_15=precipitation,precipitation_probability&forecast_minutely_15=6&timezone=auto";
            _rainWhere = m.WeatherPlace;
            _rainFetch = _rainHttp.GetStringAsync(url);
        }

        /// <summary>Not raining now, but rain (≥ 0.1 mm, or the chance at least minChance) in the next hour. when = its local time.</summary>
        public static bool RainSoon(string json, int minChance, out string when, out int chance)
        {
            when = ""; chance = 0;
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement root = d.RootElement, cur, mq, times, mm, pr, pn;
                if (root.TryGetProperty("current", out cur) && cur.TryGetProperty("precipitation", out pn) && pn.ValueKind == JsonValueKind.Number && pn.GetDouble() > 0) return false;
                if (!root.TryGetProperty("minutely_15", out mq) || !mq.TryGetProperty("time", out times) || times.ValueKind != JsonValueKind.Array) return false;
                mq.TryGetProperty("precipitation", out mm);
                mq.TryGetProperty("precipitation_probability", out pr);
                int n = Math.Min(times.GetArrayLength(), 5);
                for (int i = 1; i < n; i++)
                {
                    double mmv = mm.ValueKind == JsonValueKind.Array && i < mm.GetArrayLength() && mm[i].ValueKind == JsonValueKind.Number ? mm[i].GetDouble() : 0;
                    int p = pr.ValueKind == JsonValueKind.Array && i < pr.GetArrayLength() && pr[i].ValueKind == JsonValueKind.Number ? (int)Math.Round(pr[i].GetDouble()) : 0;
                    if (mmv >= 0.1 || p >= minChance)
                    {
                        string t = times[i].GetString() ?? "";
                        DateTime dt;
                        when = DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ? TimeText.Clock(dt) : t;
                        chance = p;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
