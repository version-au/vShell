using System;
using System.Collections.Generic;
using System.Threading;

namespace vShell.Core
{
    /// <summary>Fixed-size ring buffer of the most recent samples (0..100).</summary>
    public sealed class History
    {
        /// <summary>15 minutes at a sample a second (the longest "Time shown").</summary>
        public const int Capacity = 900;
        readonly double[] _buf = new double[Capacity];
        int _count, _next;

        [PluginApi] public int Count { get { return _count; } }

        /// <summary>The samples are amounts (an auto-scaled graph), not percent - an InfoModule starts afresh when this changes.</summary>
        internal bool Amounts;

        internal void Clear() { _count = 0; _next = 0; }

        [PluginApi] public void Add(double v)
        {
            _buf[_next] = v;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>Index 0 = oldest stored sample, Count-1 = newest.</summary>
        public double this[int i]
        {
            get
            {
                int start = (_next - _count + Capacity) % Capacity;
                return _buf[(start + i) % Capacity];
            }
        }
    }

    public sealed class GpuAdapter
    {
        public string Luid = "";
        public double Percent;     // busiest engine type, 0..100
        public double VramGb;      // dedicated memory in use
    }

    /// <summary>
    /// GPU usage for any vendor via the Windows "GPU Engine" / "GPU Adapter Memory" performance counters (Windows 10 1709+).
    /// Sampled on a background timer so the bar never stalls; read <see cref="Adapters"/> from anywhere.
    /// </summary>
    public sealed class GpuStats : IDisposable
    {
        readonly object _lock = new object();
        volatile GpuAdapter[] _adapters = new GpuAdapter[0];
        Timer _timer;
        int _busy;
        IntPtr _query, _engine, _memory;
        int _ticks, _failures;
        double _avgMs;
        int _period = 1000;

        volatile Dictionary<int, double> _byPid = new Dictionary<int, double>();
        volatile bool _hasData;

        public GpuAdapter[] Adapters { get { return _adapters; } }
        /// <summary>GPU use per process id (its busiest engine on any adapter, 0..100, like Task Manager) - read-only, replaced each sample.</summary>
        public Dictionary<int, double> ByProcess { get { return _byPid; } }
        /// <summary>Running and has read the counters at least once since it started (ByProcess is real, not just empty).</summary>
        public bool HasData { get { return _hasData && _timer != null; } }
        /// <summary>The GPU counters can't be read on this PC (no PDH, or they failed to open three times).</summary>
        public bool Unavailable { get { return !Pdh.Supported || _failures >= 3; } }
        public bool Running { get { return _timer != null; } }

        public void Start()
        {
            if (_timer != null || !Pdh.Supported) return;
            _period = 1000;
            _avgMs = 0;
            _timer = new Timer(OnTick, null, 0, 1000);
        }

        public void Stop()
        {
            Timer t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
            lock (_lock) { CloseQuery(); }
            _hasData = false;                // the GPU module keeps its last reading; Top processes waits for new data
            _byPid = new Dictionary<int, double>();
        }

        public void Dispose() { Stop(); }

        void OnTick(object state)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { lock (_lock) { if (_timer != null) Sample(); } }
            catch (Exception ex) { Log.Write("GPU sample: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        void CloseQuery()
        {
            if (_query != IntPtr.Zero) Pdh.Close(_query);
            _query = _engine = _memory = IntPtr.Zero;
            _ticks = 0;
        }

        bool Open()
        {
            IntPtr q = Pdh.OpenQuery();
            if (q == IntPtr.Zero) return false;
            IntPtr eng = Pdh.AddCounter(q, "\\GPU Engine(*)\\Utilization Percentage");
            if (eng == IntPtr.Zero) { Pdh.Close(q); return false; }
            _memory = Pdh.AddCounter(q, "\\GPU Adapter Memory(*)\\Dedicated Usage");
            _query = q;
            _engine = eng;
            Pdh.Collect(q);   // prime: utilisation is a rate and needs two samples
            return true;
        }

        void Sample()
        {
            if (_failures >= 3) return;     // counters not available on this machine; stop trying
            if (_query == IntPtr.Zero)
            {
                if (!Open()) _failures++;
                return;                      // first real data arrives on the next tick
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!Pdh.Collect(_query)) { CloseQuery(); return; }
            _ticks++;
            // The "GPU Engine" counters have an instance per process and engine; on a busy PC reading them can take a noticeable slice of
            // CPU. When it does, read every 2 seconds instead of every second (the value is kept in between).
            double ms = sw.Elapsed.TotalMilliseconds;
            _avgMs = _avgMs <= 0 ? ms : _avgMs * 0.8 + ms * 0.2;
            int want = _avgMs > 60 ? 2000 : _avgMs < 25 ? 1000 : _period;
            if (want != _period && _timer != null) { _period = want; try { _timer.Change(want, want); } catch { } }

            var eng = Pdh.ReadArray(_engine);
            if (eng.Count == 0)
            {
                if (_ticks > 4) CloseQuery();
                return;                      // keep the previous values
            }
            var mem = Pdh.ReadArray(_memory);

            // utilisation: sum the instances of each engine type per adapter, adapter load = busiest engine type
            // per process like Task Manager's GPU column: sum per adapter and engine type, then its busiest one on any adapter
            var perLuid = new Dictionary<string, Dictionary<string, double>>();
            var perPid = new Dictionary<(int, string, string), double>();
            foreach (var kv in eng)
            {
                string luid = ParseLuid(kv.Key);
                if (luid == null) continue;
                Dictionary<string, double> types;
                if (!perLuid.TryGetValue(luid, out types)) { types = new Dictionary<string, double>(); perLuid[luid] = types; }
                string type = ParseEngineType(kv.Key);
                double sum;
                types.TryGetValue(type, out sum);
                types[type] = sum + kv.Value;

                int pid = ParsePid(kv.Key);
                if (pid <= 0 || kv.Value <= 0) continue;
                var key = (pid, luid, type);
                perPid.TryGetValue(key, out sum);
                perPid[key] = sum + kv.Value;
            }
            var byPid = new Dictionary<int, double>();
            foreach (var kv in perPid)
            {
                double best;
                byPid.TryGetValue(kv.Key.Item1, out best);
                byPid[kv.Key.Item1] = Math.Min(100, Math.Max(best, kv.Value));
            }
            _byPid = byPid;
            _hasData = true;

            var memByLuid = new Dictionary<string, double>();
            foreach (var kv in mem)
            {
                string luid = ParseLuid(kv.Key);
                if (luid == null) continue;
                double cur;
                memByLuid.TryGetValue(luid, out cur);
                memByLuid[luid] = cur + kv.Value;
            }

            var luids = new HashSet<string>(perLuid.Keys);
            foreach (string l in memByLuid.Keys) luids.Add(l);

            var list = new List<GpuAdapter>();
            foreach (string luid in luids)
            {
                var a = new GpuAdapter { Luid = luid };
                Dictionary<string, double> types;
                if (perLuid.TryGetValue(luid, out types))
                    foreach (double v in types.Values) a.Percent = Math.Max(a.Percent, v);
                a.Percent = Math.Max(0, Math.Min(100, a.Percent));
                double bytes;
                if (memByLuid.TryGetValue(luid, out bytes)) a.VramGb = bytes / 1073741824.0;
                list.Add(a);
            }
            list.Sort(delegate (GpuAdapter x, GpuAdapter y)
            {
                int c = y.VramGb.CompareTo(x.VramGb);
                return c != 0 ? c : y.Percent.CompareTo(x.Percent);
            });
            _adapters = list.ToArray();

            if (_ticks >= 30) CloseQuery();   // re-open now and then so new processes' engine instances are picked up
        }

        // "pid_1234_luid_0x00000000_0x0000F3A2_phys_0_eng_0_engtype_3D"  or  "luid_0x00000000_0x0000F3A2_phys_0"
        static string ParseLuid(string name)
        {
            int i = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int start = i + 5;
            int end = name.IndexOf("_phys", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = name.Length;
            return name.Substring(start, end - start);
        }

        static int ParsePid(string name)
        {
            if (!name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)) return 0;
            int end = name.IndexOf('_', 4);
            int pid;
            return end > 4 && int.TryParse(name.Substring(4, end - 4), out pid) ? pid : 0;
        }

        static string ParseEngineType(string name)
        {
            int i = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            return i < 0 ? "" : name.Substring(i + 8);
        }
    }
}
