using System;
using System.Collections.Generic;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VShellFans
{
    /// <summary>The Fans plugin's own settings (Settings > Plugins > Fans), in its integration bag.</summary>
    public sealed class FanSettings
    {
        readonly SettingsBag _b;
        FanSettings(SettingsBag b) { _b = b; }
        public static FanSettings Of(BarSettings s) { return new FanSettings(s.Integration("fans")); }

        /// <summary>The settings in use (set at load and on every settings change).</summary>
        public static FanSettings Current;

        public bool NotifyStop { get { return _b.Get("NotifyStop", true); } set { _b.Set("NotifyStop", value); } }
        public bool NotifySlow { get { return _b.Get("NotifySlow", false); } set { _b.Set("NotifySlow", value); } }
        public int SlowRpm { get { return Math.Max(50, Math.Min(10000, _b.Get("SlowRpm", 300))); } set { _b.Set("SlowRpm", value); } }
        public int Seconds { get { return Math.Max(3, Math.Min(600, _b.Get("Seconds", 15))); } set { _b.Set("Seconds", value); } }
        /// <summary>No "stopped" for GPU fans (most stop on their own while the GPU is idle).</summary>
        public bool SkipGpu { get { return _b.Get("SkipGpu", true); } set { _b.Set("SkipGpu", value); } }

        // ---- the fans: order, hidden ones, names, each one's own warning speed ----
        public List<string> Order { get { return Lines("Order"); } set { _b.Set("Order", string.Join("\n", value)); } }
        public List<string> Hidden { get { return Lines("Hidden"); } set { _b.Set("Hidden", string.Join("\n", value)); } }
        public Dictionary<string, string> Names { get { return Pairs("Names"); } set { SetPairs("Names", value); } }
        /// <summary>A fan's own "warn below" (rpm): missing = the setting for all, 0 = never warn about it.</summary>
        public Dictionary<string, string> Mins { get { return Pairs("Mins"); } set { SetPairs("Mins", value); } }
        /// <summary>A fan's top speed (rpm): its graphs fill at it, Colour by load is high there; missing = automatic.</summary>
        public Dictionary<string, string> Maxes { get { return Pairs("Maxes"); } set { SetPairs("Maxes", value); } }
        /// <summary>0rpm mode ("stops on its own"), set by hand for a fan: "1" / "0"; missing = found by itself (a GPU fan, with SkipGpu on).</summary>
        public Dictionary<string, string> OwnStop { get { return Pairs("OwnStop"); } set { SetPairs("OwnStop", value); } }

        List<string> Lines(string key)
        {
            var l = new List<string>();
            foreach (string s in _b.Get(key, "").Split('\n')) if (s.Length > 0) l.Add(s);
            return l;
        }

        Dictionary<string, string> Pairs(string key)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in Lines(key))
            {
                int tab = s.IndexOf('\t');
                if (tab > 0) d[s.Substring(0, tab)] = s.Substring(tab + 1);
            }
            return d;
        }

        void SetPairs(string key, Dictionary<string, string> d)
        {
            var l = new List<string>();
            foreach (KeyValuePair<string, string> kv in d) if (!string.IsNullOrEmpty(kv.Value)) l.Add(kv.Key + "\t" + kv.Value.Replace("\n", " ").Replace("\t", " "));
            _b.Set(key, string.Join("\n", l));
        }
    }

    /// <summary>One fan (or pump): a reading in rpm from HWiNFO or LibreHardwareMonitor.</summary>
    public sealed class Fan
    {
        public string Key = "", Name = "", Sensor = "", Label = "";
        public double Rpm;
        public bool Hidden, Gpu;
        /// <summary>Its top speed as set (Max rpm), or 0 = automatic.</summary>
        public int Max;
        /// <summary>It may read 0 without anything being wrong (a GPU fan, a case fan that stops when cool): never "stopped".</summary>
        public bool StopsAlone;
        /// <summary>StopsAlone was set by hand for this fan (not found by itself).</summary>
        public bool StopsAloneSet;
        /// <summary>Stopped or too slow for the time set: drawn in the warning colour.</summary>
        public bool Bad;
        public History Hist;
    }

    /// <summary>
    /// The fans, read once a second while a Fans module is shown or a notification is on: each one's history (for the graphs, kept by
    /// vShell so a module made again keeps its line) and whether it has stopped or is too slow.
    /// </summary>
    public static class FanStore
    {
        sealed class Track
        {
            public DateTime BadSince = DateTime.MinValue;
            public bool Posted, EverSpun;
            public double LastGood;
        }

        static BarManager _mgr;
        static DispatcherTimer _timer;
        static int _users;
        static readonly Dictionary<string, Track> _track = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        static List<Fan> _all = new List<Fan>();

        /// <summary>After each read (once a second).</summary>
        public static event Action Ticked;

        /// <summary>Every fan, in your order (hidden ones too, marked).</summary>
        public static List<Fan> All { get { return _all; } }

        public static List<Fan> Shown()
        {
            return _all.FindAll(delegate (Fan f) { return !f.Hidden; });
        }

        public static void Start(BarManager mgr) { _mgr = mgr; Read(); Configure(); }

        public static void Stop()
        {
            if (_timer != null) _timer.Stop();
            _users = 0;
        }

        /// <summary>A module is shown (+1) or gone (-1).</summary>
        public static void Use(int d)
        {
            _users = Math.Max(0, _users + d);
            if (d > 0 && _mgr != null) Read();      // the module shows the fans at once, not a second later
            Configure();
        }

        /// <summary>Read the fans again now (settings changed: names, order, hidden).</summary>
        public static void Reread() { if (_mgr != null) Read(); }

        /// <summary>Run the timer only when something needs the fans.</summary>
        public static void Configure()
        {
            FanSettings s = FanSettings.Current;
            bool want = _mgr != null && (_users > 0 || (s != null && (s.NotifyStop || s.NotifySlow || s.Mins.Count > 0)));
            if (want && _timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += delegate { Tick(); };
            }
            if (_timer == null) return;
            if (want && !_timer.IsEnabled) _timer.Start();
            else if (!want && _timer.IsEnabled) _timer.Stop();
        }

        static bool IsRpm(HwReading r) { return string.Equals((r.Unit ?? "").Trim(), "RPM", StringComparison.OrdinalIgnoreCase); }

        /// <summary>The fans as HWiNFO / LibreHardwareMonitor publish them now, named and ordered as set.</summary>
        static void Read()
        {
            FanSettings s = FanSettings.Current;
            Dictionary<string, string> names = s != null ? s.Names : new Dictionary<string, string>();
            List<string> order = s != null ? s.Order : new List<string>();
            var hidden = new HashSet<string>(s != null ? s.Hidden : new List<string>(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> own = s != null ? s.OwnStop : new Dictionary<string, string>();
            Dictionary<string, string> maxes = s != null ? s.Maxes : new Dictionary<string, string>();
            bool skipGpu = s == null || s.SkipGpu;
            var list = new List<Fan>();
            var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<HwReading> all = HwInfo.All;
            foreach (HwReading r in all) if (IsRpm(r)) { int n; labels.TryGetValue(r.Label, out n); labels[r.Label] = n + 1; }
            foreach (HwReading r in all)
            {
                if (!IsRpm(r)) continue;
                string ownName, mark;
                // the same name on two chips ("Fan #1"): which chip, so they can be told apart
                string name = names.TryGetValue(r.Key, out ownName) ? ownName : labels[r.Label] > 1 ? r.Label + " (" + Part(r.Sensor) + ")" : r.Label;
                bool gpu = IsGpu(r.Sensor, r.Label);
                bool set = own.TryGetValue(r.Key, out mark);
                string maxText;
                int max = 0;
                if (maxes.TryGetValue(r.Key, out maxText)) int.TryParse(maxText, out max);
                list.Add(new Fan
                {
                    Key = r.Key, Name = name, Sensor = r.Sensor, Label = r.Label, Rpm = Math.Max(0, r.Value), Hidden = hidden.Contains(r.Key),
                    Gpu = gpu, Max = Math.Max(0, max), StopsAloneSet = set, StopsAlone = set ? mark == "1" : gpu && skipGpu,
                    Hist = _mgr != null ? _mgr.HistoryFor("fans|" + r.Key) : null
                });
            }
            // your order first, then any new fan where the sensors list it
            list.Sort(delegate (Fan a, Fan b)
            {
                int ia = order.FindIndex(delegate (string k) { return string.Equals(k, a.Key, StringComparison.OrdinalIgnoreCase); });
                int ib = order.FindIndex(delegate (string k) { return string.Equals(k, b.Key, StringComparison.OrdinalIgnoreCase); });
                if (ia < 0) ia = 10000 + list.IndexOf(a);
                if (ib < 0) ib = 10000 + list.IndexOf(b);
                return ia.CompareTo(ib);
            });
            foreach (Fan f in list)
            {
                Fan was = _all.Find(delegate (Fan x) { return x.Key == f.Key; });
                if (was != null) f.Bad = was.Bad;
            }
            _all = list;
        }

        static readonly string[] GpuWords = { "GPU", "Radeon", "GeForce", "NVIDIA", "Intel Arc", "Intel(R) Arc", "Quadro" };

        /// <summary>
        /// A fan on a graphics card: "GPU" in its sensor or label (HWiNFO: "GPU [#0]: AMD Radeon …", "GPU Fan"), or the card's own name
        /// (LibreHardwareMonitor names the part - "AMD Radeon RX 6900 XT: Fans" - not "GPU").
        /// </summary>
        public static bool IsGpu(string sensor, string label)
        {
            string s = (sensor ?? "") + " " + (label ?? "");
            foreach (string w in GpuWords) if (s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>"NVIDIA GeForce RTX 3080: Fans" → "NVIDIA GeForce RTX 3080".</summary>
        public static string Part(string sensor)
        {
            string s = sensor ?? "";
            int c = s.LastIndexOf(": ", StringComparison.Ordinal);
            return c > 0 ? s.Substring(0, c) : s;
        }

        static void Tick()
        {
            Read();
            FanSettings s = FanSettings.Current;
            Dictionary<string, string> mins = s != null ? s.Mins : new Dictionary<string, string>();
            DateTime now = DateTime.UtcNow;
            foreach (Fan f in _all)
            {
                if (f.Hist != null) f.Hist.Add(f.Rpm);
                Track t;
                if (!_track.TryGetValue(f.Key, out t)) { t = new Track(); _track[f.Key] = t; }
                if (f.Rpm >= 100) { t.EverSpun = true; t.LastGood = f.Rpm; }

                // its own warning speed, or the one for all (0 = never)
                string own;
                int limit = -1;
                if (mins.TryGetValue(f.Key, out own)) { int n; if (int.TryParse(own, out n)) limit = Math.Max(0, n); }
                bool never = limit == 0;
                if (limit < 0) limit = s != null && s.NotifySlow ? s.SlowRpm : 0;

                // only a fan seen spinning can stop (a header with nothing on it reads 0 all the time)
                bool stopped = !never && t.EverSpun && f.Rpm < 50 && !f.StopsAlone;
                bool slow = !never && !stopped && t.EverSpun && limit > 0 && f.Rpm >= 50 && f.Rpm < limit;
                if (!(stopped || slow))
                {
                    t.BadSince = DateTime.MinValue;
                    t.Posted = false;
                    f.Bad = false;
                    continue;
                }
                if (t.BadSince == DateTime.MinValue) t.BadSince = now;
                int secs = s != null ? s.Seconds : 15;
                if ((now - t.BadSince).TotalSeconds < secs) continue;
                f.Bad = true;
                if (t.Posted || f.Hidden) continue;
                t.Posted = true;
                bool tell = stopped ? (s == null || s.NotifyStop) : (s != null && (s.NotifySlow || mins.ContainsKey(f.Key)));
                if (!tell) continue;
                try
                {
                    if (stopped)
                        Notify.Post("Fans", f.Name + " has stopped",
                                    (t.LastGood > 0 ? "It was at " + t.LastGood.ToString("N0") + " rpm - it's" : "It's") + " been at 0 for " + secs + " seconds.", "",
                                    Notify.KindOf("Fans.Stopped"));
                    else
                        Notify.Post("Fans", f.Name + " is running slow", f.Rpm.ToString("N0") + " rpm - under the " + limit.ToString("N0") + " you set.", "",
                                    Notify.KindOf("Fans.Slow"));
                }
                catch (Exception ex) { Log.Write("Fans: notification - " + ex.Message); }
            }
            Action a = Ticked;
            if (a != null) foreach (Action h in a.GetInvocationList()) { try { ((Action)h)(); } catch (Exception ex) { Log.Write("Fans: " + ex.Message); } }
        }
    }
}
