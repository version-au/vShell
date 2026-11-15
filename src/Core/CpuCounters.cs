using System;
using System.Collections.Generic;
using System.Globalization;

namespace vShell.Core
{
    /// <summary>
    /// The CPU numbers Task Manager shows, from Windows' performance counters (Processor Information): the load as "% Processor Utility"
    /// (which counts turbo boost - Task Manager's figure since Windows 8, capped at 100), the clock speed (base frequency x "% Processor
    /// Performance") and each thread's load. Read only while a CPU module asks for them (Want); otherwise the query is closed.
    /// </summary>
    public static class CpuCounters
    {
        static IntPtr _query, _util, _perf, _freq;
        static bool _broken;
        static DateTime _wanted = DateTime.MinValue;

        /// <summary>The whole CPU's load 0..100 like Task Manager (NaN while not known yet / not available).</summary>
        public static double Utility { get; private set; } = double.NaN;
        /// <summary>The current clock speed in GHz (NaN while not known).</summary>
        public static double Ghz { get; private set; } = double.NaN;
        /// <summary>Each thread's load 0..100, in processor order (empty while not known).</summary>
        public static double[] Cores { get; private set; } = new double[0];

        /// <summary>A CPU module shows these: keep reading them (for the next 5 seconds).</summary>
        public static void Want() { _wanted = DateTime.UtcNow; }

        /// <summary>Called every second with the other stats.</summary>
        public static void Update()
        {
            if ((DateTime.UtcNow - _wanted).TotalSeconds > 5) { Close(); return; }
            if (_broken || !Pdh.Supported) return;
            try
            {
                if (_query == IntPtr.Zero)
                {
                    _query = Pdh.OpenQuery();
                    if (_query == IntPtr.Zero) { _broken = true; Log.Write("CPU counters: no query"); return; }
                    _util = Pdh.AddCounter(_query, @"\Processor Information(*)\% Processor Utility");
                    _perf = Pdh.AddCounter(_query, @"\Processor Information(_Total)\% Processor Performance");
                    _freq = Pdh.AddCounter(_query, @"\Processor Information(_Total)\Processor Frequency");
                    if (_util == IntPtr.Zero) { _broken = true; Log.Write("CPU counters: % Processor Utility isn't available"); Close(); return; }
                    Pdh.Collect(_query);        // rate counters need two readings: the first is the baseline
                    return;
                }
                if (!Pdh.Collect(_query)) return;

                // load: _Total, and each "package,thread" instance ("0,0", "0,1" ...), not the per-package "0,_Total"
                var cores = new List<KeyValuePair<int, double>>();
                foreach (KeyValuePair<string, double> kv in Pdh.ReadArray(_util))
                {
                    double v = Math.Max(0, Math.Min(100, kv.Value));
                    if (kv.Key == "_Total") { Utility = v; continue; }
                    int comma = kv.Key.IndexOf(',');
                    int pkg, thread;
                    if (comma < 0 || !int.TryParse(kv.Key.Substring(0, comma), out pkg) ||
                        !int.TryParse(kv.Key.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out thread)) continue;
                    cores.Add(new KeyValuePair<int, double>(pkg * 4096 + thread, v));
                }
                cores.Sort(delegate (KeyValuePair<int, double> a, KeyValuePair<int, double> b) { return a.Key.CompareTo(b.Key); });
                var list = new double[cores.Count];
                for (int i = 0; i < list.Length; i++) list[i] = cores[i].Value;
                Cores = list;

                // speed: base MHz x performance %
                double perf = First(_perf), freq = First(_freq);
                Ghz = !double.IsNaN(perf) && !double.IsNaN(freq) && freq > 0 ? freq * perf / 100.0 / 1000.0 : double.NaN;
            }
            catch (Exception ex) { Log.Write("CPU counters: " + ex.Message); _broken = true; Close(); }
        }

        static double First(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return double.NaN;
            List<KeyValuePair<string, double>> v = Pdh.ReadArray(counter);
            return v.Count > 0 ? v[0].Value : double.NaN;
        }

        static void Close()
        {
            if (_query == IntPtr.Zero) return;
            Pdh.Close(_query);
            _query = _util = _perf = _freq = IntPtr.Zero;
            Utility = Ghz = double.NaN;
            Cores = new double[0];
        }
    }
}
