using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace vShell.Core
{
    /// <summary>One sensor value HWiNFO ("Report value in Gadget") or LibreHardwareMonitor publishes. Plugins: read it, don't change it.</summary>
    [PluginApi]
    public sealed class HwReading
    {
        public int Index;
        public string Sensor = "";   // e.g. "CPU [#0]: AMD Ryzen 7 5800X: Enhanced"
        public string Label = "";    // e.g. "CPU (Tctl/Tdie)"
        public double Value;
        public string Text = "";     // as HWiNFO formats it, e.g. "54.5 °C"
        public string Unit = "";     // text after the number, e.g. "°C"

        public string Key { get { return HwInfo.MakeKey(Sensor, Label); } }
        public bool IsTemperature
        {
            get
            {
                string u = Unit.Trim();
                return u.EndsWith("C", StringComparison.OrdinalIgnoreCase) && u.Length <= 3 && (u.Length == 1 || u[0] == '\u00B0' || u[0] == '\u00BA' || u[0] == ' ');
            }
        }
    }

    /// <summary>
    /// Reads the sensor values HWiNFO publishes in the registry when "Report value in Gadget" is ticked for a sensor
    /// (HKCU\SOFTWARE\HWiNFO64\VSB: Sensor0/Label0/Value0/ValueRaw0, Sensor1, ...). This is the same route Rainmeter uses.
    /// </summary>
    public static class HwInfo
    {
        static readonly string[] KeyPaths = { @"SOFTWARE\HWiNFO64\VSB", @"SOFTWARE\HWiNFO32\VSB" };
        static readonly Regex NameRx = new Regex(@"^(Sensor|Label|ValueRaw|Value)\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex NumRx = new Regex(@"^\s*[-+]?[\d.,]+\s*(.*)$", RegexOptions.Compiled);

        static volatile List<HwReading> _list = new List<HwReading>();
        static volatile Dictionary<string, HwReading> _byKey = new Dictionary<string, HwReading>(StringComparer.OrdinalIgnoreCase);
        static DateTime _last = DateTime.MinValue;
        static int _busy;
        static bool _loadedOnce;

        [PluginApi] public static string MakeKey(string sensor, string label) { return (sensor ?? "") + "|" + (label ?? ""); }

        /// <summary>true when HWiNFO has published at least one value.</summary>
        [PluginApi] public static bool Available { get { EnsureFresh(); return _list.Count > 0; } }

        [PluginApi] public static List<HwReading> All { get { EnsureFresh(); return _list; } }

        /// <summary>Re-read the registry if the last read is older than ~0.9 s (cheap enough to call from every module's update).</summary>
        public static void EnsureFresh()
        {
            if ((DateTime.UtcNow - _last).TotalMilliseconds < 900) return;
            if (!_loadedOnce) { _loadedOnce = true; Refresh(); return; }     // the very first time: wait for it, so modules start with values
            // after that the registry is read in the background (with many sensors it takes a moment); modules show the last values meanwhile
            if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
            _last = DateTime.UtcNow;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Refresh(); }
                finally { System.Threading.Interlocked.Exchange(ref _busy, 0); }
            });
        }

        // ---- where the values come from ----
        static volatile bool _lhm;
        static int _port = 8085;

        /// <summary>LibreHardwareMonitor is the source (else HWiNFO).</summary>
        [PluginApi] public static bool IsLhm { get { return _lhm; } }
        /// <summary>"HWiNFO" or "LibreHardwareMonitor", for messages.</summary>
        [PluginApi] public static string SourceName { get { return _lhm ? "LibreHardwareMonitor" : "HWiNFO"; } }

        /// <summary>Read from "HWiNFO" or "LHM" (LibreHardwareMonitor's web server on that port). Called when settings are applied.</summary>
        public static void Use(string source, int port)
        {
            bool lhm = source == "LHM";
            if (lhm == _lhm && port == _port) return;
            _lhm = lhm;
            _port = port;
            _last = DateTime.MinValue;      // read the new source at the next look
        }

        public static void Refresh()
        {
            _last = DateTime.UtcNow;
            if (_lhm) { RefreshLhm(); return; }
            try
            {
                foreach (string path in KeyPaths)
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(path))
                    {
                        if (k == null) continue;
                        var parsed = ReadKey(k);
                        if (parsed.Count == 0) continue;
                        var map = new Dictionary<string, HwReading>(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in parsed) if (!map.ContainsKey(r.Key)) map[r.Key] = r;
                        _list = parsed;
                        _byKey = map;
                        return;
                    }
                }
                _list = new List<HwReading>();
                _byKey = new Dictionary<string, HwReading>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Write("HWiNFO read: " + ex.Message); }
        }

        // ---- LibreHardwareMonitor: its web server's data.json (Options > Remote Web Server > Run) ----
        static System.Net.Http.HttpClient _http;
        static string _lhmState = "";

        /// <summary>Why there are no LibreHardwareMonitor values ("" when there are) - for Settings.</summary>
        public static string LhmProblem { get { return _lhmState; } }

        static void RefreshLhm()
        {
            var list = new List<HwReading>();
            string state = "";
            try
            {
                if (_http == null) _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                string json = _http.GetStringAsync("http://127.0.0.1:" + _port + "/data.json").GetAwaiter().GetResult();
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
                    WalkLhm(doc.RootElement, new List<string>(), list);
                if (list.Count == 0) state = "LibreHardwareMonitor answered but sent no values.";
            }
            catch (Exception ex)
            {
                state = "LibreHardwareMonitor isn't answering on port " + _port + " (" + (ex.InnerException ?? ex).Message + ").";
            }
            if (state != _lhmState) { _lhmState = state; if (state.Length > 0) Log.Write("Sensors: " + state); else Log.Write("Sensors: reading LibreHardwareMonitor on port " + _port); }
            var map = new Dictionary<string, HwReading>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in list) if (!map.ContainsKey(r.Key)) map[r.Key] = r;
            _list = list;
            _byKey = map;
        }

        static string Str(System.Text.Json.JsonElement n, string name)
        {
            System.Text.Json.JsonElement v;
            return n.ValueKind == System.Text.Json.JsonValueKind.Object && n.TryGetProperty(name, out v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        /// <summary>
        /// The tree is Sensor > the PC > a part (a CPU, a GPU, the motherboard > its chip …) > a group (Temperatures, Fans …) > the values.
        /// A reading's Sensor is "part: group" (so a GPU's core temperature and core clock don't clash), its Label the value's own name.
        /// </summary>
        static void WalkLhm(System.Text.Json.JsonElement n, List<string> path, List<HwReading> list)
        {
            string text = Str(n, "Text");
            System.Text.Json.JsonElement kids;
            bool hasKids = n.TryGetProperty("Children", out kids) && kids.ValueKind == System.Text.Json.JsonValueKind.Array && kids.GetArrayLength() > 0;
            if (!hasKids)
            {
                string value = Str(n, "Value");
                double d;
                if (path.Count >= 2 && text.Length > 0 && TryNum(value, out d))
                {
                    Match um = NumRx.Match(value);
                    list.Add(new HwReading
                    {
                        Index = list.Count, Sensor = path[path.Count - 2] + ": " + path[path.Count - 1], Label = text,
                        Value = d, Text = value.Trim(), Unit = um.Success ? um.Groups[1].Value.Trim() : ""
                    });
                }
                return;
            }
            path.Add(text);
            foreach (System.Text.Json.JsonElement c in kids.EnumerateArray()) WalkLhm(c, path, list);
            path.RemoveAt(path.Count - 1);
        }

        static List<HwReading> ReadKey(RegistryKey k)
        {
            var rows = new Dictionary<int, HwReading>();
            var raw = new Dictionary<int, double>();
            foreach (string name in k.GetValueNames())
            {
                Match m = NameRx.Match(name);
                if (!m.Success) continue;
                int idx;
                if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out idx)) continue;
                string field = m.Groups[1].Value.ToLowerInvariant();
                object v = k.GetValue(name);
                if (v == null) continue;

                HwReading r;
                if (!rows.TryGetValue(idx, out r)) { r = new HwReading { Index = idx }; rows[idx] = r; }
                switch (field)
                {
                    case "sensor": r.Sensor = Convert.ToString(v, CultureInfo.InvariantCulture); break;
                    case "label": r.Label = Convert.ToString(v, CultureInfo.InvariantCulture); break;
                    case "value": r.Text = Convert.ToString(v, CultureInfo.InvariantCulture); break;
                    case "valueraw":
                        {
                            double d;
                            if (TryNum(Convert.ToString(v, CultureInfo.InvariantCulture), out d)) raw[idx] = d;
                            break;
                        }
                }
            }

            var list = new List<HwReading>(rows.Values);
            list.Sort(delegate (HwReading a, HwReading b) { return a.Index.CompareTo(b.Index); });
            foreach (var r in list)
            {
                double d;
                if (raw.TryGetValue(r.Index, out d)) r.Value = d;
                else if (!TryNum(r.Text, out r.Value)) r.Value = 0;
                Match um = NumRx.Match(r.Text ?? "");
                r.Unit = um.Success ? um.Groups[1].Value.Trim() : "";
            }
            list.RemoveAll(delegate (HwReading r) { return string.IsNullOrEmpty(r.Label) && string.IsNullOrEmpty(r.Sensor); });
            return list;
        }

        static bool TryNum(string s, out double d)
        {
            d = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            Match m = Regex.Match(s, @"[-+]?\d[\d.,]*");
            if (!m.Success) return false;
            string t = m.Value;
            // "3,200.5" / "3.200,5" / "54,5": decide which separator is the decimal one
            int comma = t.LastIndexOf(','), dot = t.LastIndexOf('.');
            if (comma >= 0 && dot >= 0) t = comma > dot ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
            else if (comma >= 0) t = t.Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        [PluginApi] public static HwReading Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureFresh();
            HwReading r;
            return _byKey.TryGetValue(key, out r) ? r : null;
        }

        // ---- automatic temperature picking (used when a module has no explicit sensor) -----------
        static HwReading FirstTemp(string[] labelPatterns, string sensorMustContain)
        {
            EnsureFresh();
            foreach (string pat in labelPatterns)
            {
                foreach (var r in _list)
                {
                    if (!r.IsTemperature) continue;
                    if (sensorMustContain != null && r.Sensor.IndexOf(sensorMustContain, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (r.Label.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0) return r;
                }
            }
            return null;
        }

        public static HwReading AutoCpuTemp()
        {
            return FirstTemp(new[] { "CPU Package", "CPU (Tctl/Tdie)", "CPU Die (average)", "CPU Tctl", "Tdie", "Core Max", "CPU" }, null);
        }

        public static HwReading AutoGpuTemp()
        {
            return FirstTemp(new[] { "GPU Temperature", "GPU Core", "GPU" }, null);
        }

        public static HwReading AutoMemoryTemp()
        {
            return FirstTemp(new[] { "DIMM", "Memory", "SPD Hub", "SODIMM" }, null);
        }
    }

    /// <summary>NVIDIA GPU temperature through NVML (nvml.dll ships with the NVIDIA driver). Silently unavailable on other GPUs.</summary>
    public static class Nvml
    {
        delegate int InitFn();
        delegate int GetHandleFn(uint index, out IntPtr dev);
        delegate int GetTempFn(IntPtr dev, int sensor, out uint temp);

        static bool _tried, _ok;
        static GetHandleFn _getHandle;
        static GetTempFn _getTemp;
        static readonly Dictionary<int, IntPtr> Handles = new Dictionary<int, IntPtr>();

        static bool Init()
        {
            if (_tried) return _ok;
            _tried = true;
            try
            {
                IntPtr lib;
                if (!NativeLibrary.TryLoad("nvml.dll", out lib))
                {
                    string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
                    if (!NativeLibrary.TryLoad(System.IO.Path.Combine(pf, @"NVIDIA Corporation\NVSMI\nvml.dll"), out lib)) return false;
                }
                IntPtr pInit, pHandle, pTemp;
                if (!NativeLibrary.TryGetExport(lib, "nvmlInit_v2", out pInit)) return false;
                if (!NativeLibrary.TryGetExport(lib, "nvmlDeviceGetHandleByIndex_v2", out pHandle)) return false;
                if (!NativeLibrary.TryGetExport(lib, "nvmlDeviceGetTemperature", out pTemp)) return false;
                var init = (InitFn)Marshal.GetDelegateForFunctionPointer(pInit, typeof(InitFn));
                _getHandle = (GetHandleFn)Marshal.GetDelegateForFunctionPointer(pHandle, typeof(GetHandleFn));
                _getTemp = (GetTempFn)Marshal.GetDelegateForFunctionPointer(pTemp, typeof(GetTempFn));
                _ok = init() == 0;
            }
            catch (Exception ex) { Log.Write("NVML: " + ex.Message); _ok = false; }
            return _ok;
        }

        /// <summary>Temperature in °C of NVIDIA device <paramref name="index"/> (0-based).</summary>
        public static bool TryGetTemp(int index, out double celsius)
        {
            celsius = 0;
            if (!Init()) return false;
            try
            {
                IntPtr dev;
                if (!Handles.TryGetValue(index, out dev))
                {
                    if (_getHandle((uint)index, out dev) != 0) return false;
                    Handles[index] = dev;
                }
                uint t;
                if (_getTemp(dev, 0, out t) != 0) return false;
                celsius = t;
                return true;
            }
            catch { return false; }
        }
    }
}
