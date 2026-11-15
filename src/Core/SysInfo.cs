using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace vShell.Core
{
    // =====================================================================
    // Network
    // =====================================================================
    public sealed class NetSample
    {
        public double DownBytesPerSec, UpBytesPerSec;
        public string Adapter = "";
        public string Address = "";
        public long TotalDown, TotalUp;
        public bool Connected;
    }

    /// <summary>
    /// Current download / upload speed, from the byte counters of the adapters that have a default gateway (so VPN / virtual switches
    /// are not counted twice). The list of adapters is looked up again only when the network changes (or every 30 s); each sample just
    /// reads their counters. One shared sampler serves every network module.
    /// </summary>
    public sealed class NetworkSampler
    {
        public static readonly NetworkSampler Shared = new NetworkSampler();
        static readonly Dictionary<string, NetworkSampler> ByName = new Dictionary<string, NetworkSampler>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The sampler for one adapter (its name, as Windows shows it), or Shared ("" = the adapters with a gateway, added up).</summary>
        public static NetworkSampler For(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return Shared;
            lock (ByName)
            {
                NetworkSampler s;
                if (!ByName.TryGetValue(adapterName, out s)) { s = new NetworkSampler { _only = adapterName.Trim() }; ByName[adapterName] = s; }
                return s;
            }
        }

        /// <summary>The adapters that are up (not loopback), for picking one: "name" and "name - description · IP".</summary>
        public static List<KeyValuePair<string, string>> Interfaces()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.OperationalStatus != OperationalStatus.Up) continue;
                    string ip = "";
                    try
                    {
                        foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                            if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = ua.Address.ToString(); break; }
                    }
                    catch { }
                    list.Add(new KeyValuePair<string, string>(ni.Name, ni.Name + " - " + ni.Description + (ip.Length > 0 ? " · " + ip : "")));
                }
            }
            catch { }
            list.Sort(delegate (KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        static void AllDirty()
        {
            Shared._listDirty = true;
            lock (ByName) foreach (NetworkSampler s in ByName.Values) s._listDirty = true;
        }

        /// <summary>Only this adapter (its name); "" = every adapter with a default gateway.</summary>
        string _only = "";

        sealed class Adapter { public NetworkInterface Ni; public string Id, Name, Address; }

        long _prevRx, _prevTx;
        string _prevSet = "";
        DateTime _prevAt, _listAt = DateTime.MinValue;
        bool _has;
        NetSample _last = new NetSample();
        List<Adapter> _adapters;
        volatile bool _listDirty = true;
        static bool _hooked;

        public NetworkSampler()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                NetworkChange.NetworkAddressChanged += delegate { AllDirty(); };
                NetworkChange.NetworkAvailabilityChanged += delegate { AllDirty(); };
            }
            catch { }
        }

        List<Adapter> Adapters()
        {
            if (_adapters != null && !_listDirty && (DateTime.UtcNow - _listAt).TotalSeconds < 30) return _adapters;
            _listDirty = false;
            _listAt = DateTime.UtcNow;
            var list = new List<Adapter>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                IPInterfaceProperties ip = ni.GetIPProperties();
                if (_only.Length > 0)
                {
                    // one chosen adapter (a VPN or a second network card included)
                    if (!string.Equals(ni.Name, _only, StringComparison.OrdinalIgnoreCase)) continue;
                }
                else
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    bool gateway = false;
                    foreach (GatewayIPAddressInformation g in ip.GatewayAddresses)
                        if (g.Address != null && !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)) { gateway = true; break; }
                    if (!gateway) continue;
                }
                var a = new Adapter { Ni = ni, Id = ni.Id, Name = ni.Name, Address = "" };
                foreach (UnicastIPAddressInformation ua in ip.UnicastAddresses)
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { a.Address = ua.Address.ToString(); break; }
                list.Add(a);
            }
            _adapters = list;
            return list;
        }

        public NetSample Sample()
        {
            DateTime now = DateTime.UtcNow;
            if (_has && (now - _prevAt).TotalMilliseconds < 400) return _last;     // another network module just asked
            long rx = 0, tx = 0;
            string set = "";
            var s = new NetSample();
            try
            {
                foreach (Adapter a in Adapters())
                {
                    IPv4InterfaceStatistics st;
                    try { st = a.Ni.GetIPv4Statistics(); }
                    catch { _listDirty = true; continue; }      // the adapter went away: look again next time
                    rx += st.BytesReceived; tx += st.BytesSent;
                    set += a.Id + ";";
                    if (s.Adapter.Length == 0) { s.Adapter = a.Name; s.Address = a.Address; }
                }
            }
            catch (Exception ex) { Log.Write("Network sample: " + ex.Message); _listDirty = true; return _last; }

            s.Connected = set.Length > 0;
            s.TotalDown = rx; s.TotalUp = tx;
            if (_has && set == _prevSet)
            {
                double dt = (now - _prevAt).TotalSeconds;
                if (dt > 0.1)
                {
                    s.DownBytesPerSec = Math.Max(0, (rx - _prevRx) / dt);
                    s.UpBytesPerSec = Math.Max(0, (tx - _prevTx) / dt);
                }
                else { s.DownBytesPerSec = _last.DownBytesPerSec; s.UpBytesPerSec = _last.UpBytesPerSec; }
            }
            _prevRx = rx; _prevTx = tx; _prevSet = set; _prevAt = now; _has = true;
            _last = s;
            return s;
        }
    }

    /// <summary>Wi-Fi network name and signal strength, read from "netsh wlan show interfaces" in the background (about every 15 s, only while somebody asks).</summary>
    public static class Wifi
    {
        static readonly object Gate = new object();
        static string _ssid = "";
        static int _signal = -1;
        static DateTime _at = DateTime.MinValue;
        static bool _busy;

        public static void Get(out string ssid, out int signalPercent)
        {
            bool start = false;
            lock (Gate)
            {
                ssid = _ssid; signalPercent = _signal;
                if (!_busy && (DateTime.UtcNow - _at).TotalSeconds > 15) { _busy = true; start = true; }
            }
            if (start) ThreadPool.QueueUserWorkItem(delegate { Refresh(); });
        }

        // ---- the Wi-Fi API (no extra process); "netsh wlan show interfaces" is the fallback ----
        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr handle, ref Guid iface, int opCode, IntPtr reserved, out int size, out IntPtr data, IntPtr opcodeType);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);
        static IntPtr _wlan;
        static bool _wlanFailed;

        /// <summary>SSID and signal quality (0-100, the same number netsh shows) of the first connected Wi-Fi adapter. False = API not usable.</summary>
        static bool TryWlanApi(out string ssid, out int signal)
        {
            ssid = ""; signal = -1;
            if (_wlanFailed) return false;
            try
            {
                if (_wlan == IntPtr.Zero)
                {
                    uint neg;
                    if (WlanOpenHandle(2, IntPtr.Zero, out neg, out _wlan) != 0) { _wlan = IntPtr.Zero; _wlanFailed = true; return false; }
                }
                IntPtr list;
                if (WlanEnumInterfaces(_wlan, IntPtr.Zero, out list) != 0) return false;
                try
                {
                    // WLAN_INTERFACE_INFO_LIST: dwNumberOfItems, dwIndex, then items of 532 bytes (GUID 16, description 512, state 4)
                    int n = Marshal.ReadInt32(list, 0);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr item = list + 8 + i * 532;
                        int state = Marshal.ReadInt32(item, 16 + 512);
                        if (state != 1) continue;           // wlan_interface_state_connected
                        var bytes = new byte[16];
                        Marshal.Copy(item, bytes, 0, 16);
                        var guid = new Guid(bytes);
                        int size; IntPtr data;
                        // wlan_intf_opcode_current_connection = 7 -> WLAN_CONNECTION_ATTRIBUTES
                        if (WlanQueryInterface(_wlan, ref guid, 7, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0 || data == IntPtr.Zero) continue;
                        try
                        {
                            // isState 4, connectionMode 4, profile name 512 (WCHAR[256]) -> association attributes at 520:
                            //   DOT11_SSID { ULONG length; UCHAR ssid[32] } (36), BSS type 4, BSSID 6 (+2 padding), PHY type 4, PHY index 4, signal quality 4
                            int len = Marshal.ReadInt32(data, 520);
                            if (len < 0 || len > 32) len = 0;
                            var ss = new byte[len];
                            if (len > 0) Marshal.Copy(data + 524, ss, 0, len);
                            ssid = System.Text.Encoding.UTF8.GetString(ss);
                            int q = Marshal.ReadInt32(data, 576);
                            signal = q >= 0 && q <= 100 ? q : -1;
                        }
                        finally { WlanFreeMemory(data); }
                        return true;
                    }
                    return true;        // the API works; just no Wi-Fi connected
                }
                finally { WlanFreeMemory(list); }
            }
            catch (Exception ex) { Log.Write("Wi-Fi API: " + ex.Message); _wlanFailed = true; return false; }
        }

        static void Refresh()
        {
            string ssid = ""; int signal = -1;
            if (TryWlanApi(out ssid, out signal))
            {
                lock (Gate) { _ssid = ssid; _signal = signal; _at = DateTime.UtcNow; _busy = false; }
                return;
            }
            ssid = ""; signal = -1;
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    foreach (string raw in text.Split('\n'))
                    {
                        int i = raw.IndexOf(':');
                        if (i <= 0) continue;
                        string key = raw.Substring(0, i).Trim();
                        string val = raw.Substring(i + 1).Trim();
                        if (key == "SSID") ssid = val;
                        else if (key == "Signal")
                        {
                            int v;
                            if (int.TryParse(val.TrimEnd('%').Trim(), out v)) signal = v;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Wi-Fi query: " + ex.Message); }
            lock (Gate) { _ssid = ssid; _signal = signal; _at = DateTime.UtcNow; _busy = false; }
        }
    }

    // =====================================================================
    // Battery
    // =====================================================================
    public sealed class BatteryInfo
    {
        public bool HasBattery;
        public int Percent = -1;
        public bool OnAc, Charging;
        /// <summary>Seconds of charge left on battery power; -1 = unknown.</summary>
        public int SecondsLeft = -1;
    }

    public static class Battery
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        public static BatteryInfo Read()
        {
            var b = new BatteryInfo();
            SYSTEM_POWER_STATUS s;
            if (!GetSystemPowerStatus(out s)) return b;
            b.HasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
            b.OnAc = s.ACLineStatus == 1;
            b.Charging = (s.BatteryFlag & 8) != 0;
            b.Percent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : -1;
            b.SecondsLeft = s.BatteryLifeTime >= 0 ? s.BatteryLifeTime : -1;
            return b;
        }
    }

    // =====================================================================
    // Processes
    // =====================================================================
    public sealed class ProcInfo
    {
        public string Name = "";
        /// <summary>Share of the whole machine's CPU, in percent.</summary>
        public double Cpu;
        public double MemMb;
        /// <summary>GPU use in percent (busiest engine, like Task Manager) - 0 while the GPU counters aren't being read.</summary>
        public double Gpu;
        public int Count;
    }

    /// <summary>Top processes, grouped by program name. Sampled in the background about every 3 s, and only while somebody keeps asking.</summary>
    public static class ProcessSampler
    {
        static readonly object Gate = new object();
        static List<ProcInfo> _latest = new List<ProcInfo>();
        static readonly Dictionary<int, double> PrevCpu = new Dictionary<int, double>();
        static DateTime _prevAt = DateTime.MinValue, _sampledAt = DateTime.MinValue;
        static bool _busy;

        /// <summary>Where the per-process GPU use comes from (vShell's GPU sampler; only running while something shows GPU use).</summary>
        public static GpuStats Gpu;
        static bool _hadGpu;

        /// <summary>The latest sample has real GPU numbers (the GPU sampler was running and had read the counters).</summary>
        public static bool HasGpu { get { lock (Gate) { return _hadGpu; } } }

        public static List<ProcInfo> Latest()
        {
            bool start = false;
            List<ProcInfo> res;
            GpuStats gs = Gpu;
            bool gpuNew = gs != null && gs.HasData;
            lock (Gate)
            {
                res = _latest;
                // GPU numbers just became available: sample now rather than up to 3 s later
                bool due = (DateTime.UtcNow - _sampledAt).TotalSeconds >= 3 || (gpuNew && !_hadGpu);
                if (!_busy && due) { _busy = true; start = true; }
            }
            if (start) ThreadPool.QueueUserWorkItem(delegate { Sample(); });
            return res;
        }

        static void Sample()
        {
            var byName = new Dictionary<string, ProcInfo>(StringComparer.OrdinalIgnoreCase);
            var cpuNow = new Dictionary<int, double>();
            DateTime now = DateTime.UtcNow;
            double dt = _prevAt == DateTime.MinValue ? 0 : (now - _prevAt).TotalSeconds;
            int cores = Math.Max(1, Environment.ProcessorCount);
            GpuStats gs = Gpu;
            Dictionary<int, double> gpu = gs != null && gs.HasData ? gs.ByProcess : null;
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == 0) continue;
                        double cpu = 0;
                        try
                        {
                            double secs = p.TotalProcessorTime.TotalSeconds;
                            cpuNow[p.Id] = secs;
                            double prev;
                            if (dt > 0.2 && PrevCpu.TryGetValue(p.Id, out prev)) cpu = Math.Max(0, (secs - prev) / dt / cores * 100.0);
                        }
                        catch { }
                        ProcInfo info;
                        if (!byName.TryGetValue(p.ProcessName, out info)) { info = new ProcInfo { Name = p.ProcessName }; byName[p.ProcessName] = info; }
                        info.Cpu += cpu;
                        info.MemMb += p.WorkingSet64 / 1048576.0;
                        double g;
                        if (gpu != null && gpu.TryGetValue(p.Id, out g)) info.Gpu = Math.Min(100, info.Gpu + g);
                        info.Count++;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { Log.Write("Process sample: " + ex.Message); }

            var list = new List<ProcInfo>(byName.Values);
            lock (Gate)
            {
                PrevCpu.Clear();
                foreach (var kv in cpuNow) PrevCpu[kv.Key] = kv.Value;
                _prevAt = now;
                _latest = list;
                _hadGpu = gpu != null;
                _sampledAt = DateTime.UtcNow;
                _busy = false;
            }
        }
    }
}
