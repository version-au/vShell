using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The UniFi console, the two API keys and the notifications - kept in vShell's settings under Integrations["unifi"].</summary>
    public sealed class UnSettings
    {
        readonly SettingsBag _b;
        public UnSettings(BarSettings s) { _b = s.Integration("unifi"); }
        public static UnSettings Of(BarSettings s) { return new UnSettings(s); }

        /// <summary>The console's address (UDM / UCG / Cloud Key / UNVR), e.g. https://192.168.1.1.</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>UniFi Network API key (Network > Settings > Control Plane > Integrations), stored encrypted (NetKeyPlain is the real one).</summary>
        public string NetKey { get { return _b.Get("NetKey", ""); } set { _b.Set("NetKey", Secret.Protect(value ?? "")); } }
        public string NetKeyPlain { get { return _b.GetSecret("NetKey"); } }
        /// <summary>UniFi Protect API key (Protect > Settings > Control Plane > Integrations), stored encrypted.</summary>
        public string ProtectKey { get { return _b.Get("ProtectKey", ""); } set { _b.Set("ProtectKey", Secret.Protect(value ?? "")); } }
        public string ProtectKeyPlain { get { return _b.GetSecret("ProtectKey"); } }
        /// <summary>Consoles use a self-signed certificate.</summary>
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", true); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>The Network site (name); empty = the first one ("Default").</summary>
        public string Site { get { return _b.Get("Site", ""); } set { _b.Set("Site", (value ?? "").Trim()); } }
        /// <summary>How often the Network data is read while a module shows it.</summary>
        public int PollSeconds { get { return Math.Max(5, Math.Min(300, _b.Get("PollSeconds", 10))); } set { _b.Set("PollSeconds", value); } }
        /// <summary>Run box keyword for UniFi clients / devices.</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "u").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "u" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        // notifications
        public bool NotifyDevices { get { return _b.Get("NotifyDevices", false); } set { _b.Set("NotifyDevices", value); } }
        /// <summary>Wait this many seconds before saying a UniFi device is offline; 0 = straight away.</summary>
        public int NotifyDelay { get { return Math.Max(0, Math.Min(1800, _b.Get("NotifyDelay", 0))); } set { _b.Set("NotifyDelay", value); } }
        public bool NotifyNewClient { get { return _b.Get("NotifyNewClient", false); } set { _b.Set("NotifyNewClient", value); } }
        /// <summary>An access point with this many clients or more (0 = off).</summary>
        public int NotifyApClients { get { return Math.Max(0, Math.Min(500, _b.Get("NotifyApClients", 0))); } set { _b.Set("NotifyApClients", value); } }
        public bool NotifyRing { get { return _b.Get("NotifyRing", false); } set { _b.Set("NotifyRing", value); } }
        public bool NotifyPerson { get { return _b.Get("NotifyPerson", false); } set { _b.Set("NotifyPerson", value); } }
        public bool NotifyVehicle { get { return _b.Get("NotifyVehicle", false); } set { _b.Set("NotifyVehicle", value); } }
        public bool NotifyPackage { get { return _b.Get("NotifyPackage", false); } set { _b.Set("NotifyPackage", value); } }
        public bool NotifyAnimal { get { return _b.Get("NotifyAnimal", false); } set { _b.Set("NotifyAnimal", value); } }
        public bool NotifyMotion { get { return _b.Get("NotifyMotion", false); } set { _b.Set("NotifyMotion", value); } }
        /// <summary>Only these cameras (comma separated names); empty = all.</summary>
        public string NotifyCameras { get { return _b.Get("NotifyCameras", ""); } set { _b.Set("NotifyCameras", value ?? ""); } }
    }

    /// <summary>A UniFi Network module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class UnView
    {
        public static readonly string[] Views = { "All", "Devices", "Clients" };

        readonly SettingsBag _b;
        public UnView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static UnView Of(ModuleConfig cfg) { return new UnView(cfg); }

        /// <summary>The popup / widget view: "All" (Devices and Clients tabs), "Devices" or "Clients".</summary>
        public string View { get { string v = _b.Get("View", "All"); return Array.IndexOf(Views, v) < 0 ? "All" : v; } set { _b.Set("View", value); } }
        /// <summary>On a bar: the gateway's download / upload and the client count.</summary>
        public bool BarRates { get { return _b.Get("BarRates", true); } set { _b.Set("BarRates", value); } }
        public bool BarClients { get { return _b.Get("BarClients", true); } set { _b.Set("BarClients", value); } }
        /// <summary>Rows shown in a list (the rest: "+ n more").</summary>
        public int Count { get { return Math.Max(3, Math.Min(200, _b.Get("Count", 25))); } set { _b.Set("Count", value); } }
        /// <summary>The devices whose ports are shown (opened with a click; kept between runs) - the same list every time.</summary>
        public List<string> Open { get { return _b.Live("Open", delegate { return new List<string>(); }); } }
    }

    /// <summary>A UniFi Protect module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class PrView
    {
        readonly SettingsBag _b;
        public PrView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static PrView Of(ModuleConfig cfg) { return new PrView(cfg); }

        /// <summary>Snapshot columns.</summary>
        public int Columns { get { return Math.Max(1, Math.Min(6, _b.Get("Columns", 2))); } set { _b.Set("Columns", value); } }
        /// <summary>Refresh the snapshots every (seconds).</summary>
        public int SnapSeconds { get { return Math.Max(2, Math.Min(300, _b.Get("SnapSeconds", 10))); } set { _b.Set("SnapSeconds", value); } }
        /// <summary>Only these cameras (comma separated names, in this order); empty = all.</summary>
        public string Cameras { get { return _b.Get("Cameras", ""); } set { _b.Set("Cameras", (value ?? "").Trim()); } }
        public bool ShowEvents { get { return _b.Get("ShowEvents", true); } set { _b.Set("ShowEvents", value); } }
        public bool ShowLights { get { return _b.Get("ShowLights", false); } set { _b.Set("ShowLights", value); } }
        /// <summary>On a bar: the last event next to the camera count.</summary>
        public bool BarEvent { get { return _b.Get("BarEvent", true); } set { _b.Set("BarEvent", value); } }
    }

    // =====================================================================
    // UniFi Network (read-only)
    // =====================================================================
    public sealed class UnPort
    {
        public int Idx;
        public string State = "";          // UP / DOWN
        public string Connector = "";
        public int SpeedMbps, MaxSpeedMbps;
        public bool Poe;                   // PoE switched on for the port
        public string PoeState = "";
    }

    public sealed class UnDevice
    {
        public string Id = "", Name = "", Model = "", Mac = "", Ip = "", State = "", Firmware = "";
        /// <summary>gateway / switch / ap / other (from the features it has).</summary>
        public string Kind = "other";
        public bool Updatable;
        public bool HasStats;
        public long UptimeSec;
        public double Cpu = -1, Mem = -1;
        public double TxBps = -1, RxBps = -1;          // its uplink
        public List<UnPort> Ports = new List<UnPort>();
        public int Clients;                             // clients connected through it (worked out from the client list)
        public bool Online { get { return State == "ONLINE"; } }
    }

    public sealed class UnClient
    {
        public string Id = "", Name = "", Ip = "", Mac = "", Type = "", UplinkId = "";
        public DateTime ConnectedAt = DateTime.MinValue;   // UTC
        /// <summary>Not seen on this network before (since vShell started remembering clients) - in the last hour.</summary>
        public bool IsNew;
        public bool Wireless { get { return Type == "WIRELESS"; } }
        public string Label { get { return Name.Length > 0 ? Name : Ip.Length > 0 ? Ip : Mac; } }
    }

    public sealed class UnSnapshot
    {
        public bool Ok;
        public string Error = "";
        public DateTime Fetched = DateTime.MinValue;
        public string Site = "";
        public readonly List<UnDevice> Devices = new List<UnDevice>();
        public readonly List<UnClient> Clients = new List<UnClient>();
        public UnDevice Gateway
        {
            get { foreach (UnDevice d in Devices) if (d.Kind == "gateway") return d; return null; }
        }
        public UnDevice Device(string id) { foreach (UnDevice d in Devices) if (d.Id == id) return d; return null; }
    }

    // =====================================================================
    // UniFi Protect
    // =====================================================================
    public sealed class PrCamera
    {
        public string Id = "", Name = "", State = "", Model = "";
        public bool IsPtz, IsDoorbell;
        public bool Online { get { return State == "CONNECTED"; } }
    }

    public sealed class PrLight
    {
        public string Id = "", Name = "", State = "", Mode = "";
        public bool On, Forced;
        public bool Online { get { return State == "CONNECTED"; } }
    }

    public sealed class PrEvent
    {
        public string Id = "", Type = "", CameraId = "", CameraName = "";
        public List<string> SmartTypes = new List<string>();
        public DateTime Start = DateTime.UtcNow;           // UTC
        /// <summary>The snapshot saved for it (a file), or "".</summary>
        public string Image = "";
        /// <summary>person / vehicle / package / animal / ring / motion ... : what to call it.</summary>
        public string What
        {
            get
            {
                if (Type == "ring") return "Doorbell";
                if (SmartTypes.Count > 0) return Unifi.Nice(SmartTypes[0]);
                if (Type == "motion") return "Motion";
                return Unifi.Nice(Type);
            }
        }
    }

    public sealed class PrSnapshot
    {
        public bool Ok;
        public string Error = "";
        public DateTime Fetched = DateTime.MinValue;
        public string Nvr = "";
        public readonly List<PrCamera> Cameras = new List<PrCamera>();
        public readonly List<PrLight> Lights = new List<PrLight>();
        public PrCamera Camera(string id) { foreach (PrCamera c in Cameras) if (c.Id == id) return c; return null; }
    }

    /// <summary>
    /// UniFi on the local console with the official APIs and API keys (no user name / password):
    ///  Network: https://console/proxy/network/integration/v1/... (X-API-KEY) - sites, devices (+ statistics/latest, ports), clients.
    ///  Read only. Polled every UnifiPollSeconds while something shows it (or notifications want it).
    ///  Protect: https://console/proxy/protect/integration/v1/... (X-API-KEY) - cameras, lights, NVR, camera snapshots, RTSPS links,
    ///  PTZ presets, light on / off; and the live event / device streams (WebSockets subscribe/events and subscribe/devices) for
    ///  doorbell rings and detections. The official Protect API has no event history, so vShell keeps the events it has seen.
    /// </summary>
    public static class Unifi
    {
        static readonly object Gate = new object();
        static BarSettings _s;
        static Timer _timer;
        static int _busy;
        static UnSnapshot _net = new UnSnapshot();
        static PrSnapshot _pro = new PrSnapshot();
        static DateTime _netWanted = DateTime.MinValue, _proWanted = DateTime.MinValue, _netPoll = DateTime.MinValue, _proPoll = DateTime.MinValue, _detailsPoll = DateTime.MinValue;
        static string _siteId = "";
        static HttpClient _http;
        static string _httpKey = "";
        static readonly List<PrEvent> _events = new List<PrEvent>();
        static readonly Dictionary<string, UnDevice> _details = new Dictionary<string, UnDevice>();
        static Thread _wsThread;
        static string _wsKey = "";
        static volatile bool _wsStop;

        public static event Action NetChanged, ProtectChanged;
        /// <summary>A Protect event has started or changed (rings, detections, motion), and which detection is new in it ("ring", "person" ...).</summary>
        public static event Action<PrEvent, string> ProtectEvent;
        /// <summary>A UniFi Network notice: (key in Notify.Kinds, title, detail).</summary>
        public static event Action<string, string, string, NotifyAbout> NetNotice;

        public static bool NetConfigured(BarSettings s) { if (s == null) return false; UnSettings u = UnSettings.Of(s); return !string.IsNullOrWhiteSpace(u.Url) && !string.IsNullOrEmpty(u.NetKeyPlain); }
        public static bool ProtectConfigured(BarSettings s) { if (s == null) return false; UnSettings u = UnSettings.Of(s); return !string.IsNullOrWhiteSpace(u.Url) && !string.IsNullOrEmpty(u.ProtectKeyPlain); }

        public static void Configure(BarSettings s)
        {
            _s = s;
            lock (Gate)
                if (_timer == null && (NetConfigured(s) || ProtectConfigured(s))) _timer = new Timer(delegate { Tick(); }, null, 900, 2000);
            StartStreams();
        }

        /// <summary>The plugin was removed: stop polling and the Protect streams, and forget what was read (Configure starts it again).</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                _wsStop = true;
                _wsKey = "";
                _wsThread = null;
                _net = new UnSnapshot();
                _pro = new PrSnapshot();
            }
            _s = null;
        }
        public static UnSnapshot Network { get { _netWanted = DateTime.UtcNow; lock (Gate) return _net; } }
        public static PrSnapshot Protect { get { _proWanted = DateTime.UtcNow; lock (Gate) return _pro; } }

        /// <summary>The Protect events seen since vShell started, newest first.</summary>
        public static List<PrEvent> Events { get { lock (Gate) return new List<PrEvent>(_events); } }

        public static void Poke() { _netPoll = DateTime.MinValue; _proPoll = DateTime.MinValue; }

        public static string Base(BarSettings s)
        {
            string b = UnSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "https://" + b;
            return b;
        }

        public static string ProtectWeb(BarSettings s) { return Base(s) + "/protect/dashboard"; }
        public static string ProtectCameraWeb(BarSettings s, string id) { return Base(s) + "/protect/devices/" + Uri.EscapeDataString(id ?? ""); }
        public static string NetworkWeb(BarSettings s) { return Base(s) + "/network/default/dashboard"; }

        static HttpClient Http(BarSettings s)
        {
            string key = Base(s) + "|" + UnSettings.Of(s).IgnoreCert;
            if (_http == null || key != _httpKey)
            {
                var handler = new HttpClientHandler();
                if (UnSettings.Of(s).IgnoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                var old = _http;
                _http = h; _httpKey = key;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return _http;
        }

        /// <summary>GET a JSON path under /proxy/network/integration or /proxy/protect/integration with that app's key.</summary>
        static string Get(BarSettings s, bool protect, string path)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Base(s) + (protect ? "/proxy/protect/integration" : "/proxy/network/integration") + path);
            req.Headers.TryAddWithoutValidation("X-API-KEY", (protect ? UnSettings.Of(s).ProtectKeyPlain : UnSettings.Of(s).NetKeyPlain));
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            using (HttpResponseMessage r = Http(s).SendAsync(req).GetAwaiter().GetResult())
            {
                string text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return text;
                throw new InvalidOperationException(Answer((int)r.StatusCode, text, protect));
            }
        }

        static string Send(BarSettings s, HttpMethod method, string path, string json)
        {
            var req = new HttpRequestMessage(method, Base(s) + "/proxy/protect/integration" + path);
            req.Headers.TryAddWithoutValidation("X-API-KEY", UnSettings.Of(s).ProtectKeyPlain);
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using (HttpResponseMessage r = Http(s).SendAsync(req).GetAwaiter().GetResult())
            {
                string text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return text;
                throw new InvalidOperationException(Answer((int)r.StatusCode, text, true));
            }
        }

        static string Answer(int code, string text, bool protect)
        {
            string msg = "";
            try { using (JsonDocument d = JsonDocument.Parse(text)) msg = Str(d.RootElement, "message"); if (msg.Length == 0) using (JsonDocument d = JsonDocument.Parse(text)) msg = Str(d.RootElement, "error"); } catch { }
            if (code == 401 || code == 403) return (protect ? "Protect" : "UniFi Network") + " refused the API key (" + code + ")";
            if (code == 404) return (protect ? "Protect" : "UniFi Network") + " has no official API here (404) - is the address right, and the app up to date?";
            return (protect ? "Protect" : "UniFi Network") + " answered " + code + (msg.Length > 0 ? ": " + msg : "");
        }

        // ---- polling ----
        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null) return;
                DateTime now = DateTime.UtcNow;
                UnSettings u = UnSettings.Of(s);
                int every = u.PollSeconds;
                bool netNotify = u.NotifyDevices || u.NotifyNewClient || u.NotifyApClients > 0;
                bool netWanted = (now - _netWanted).TotalSeconds < 30;
                if (NetConfigured(s) && (now - _netPoll).TotalSeconds >= (netWanted ? every : netNotify ? Math.Max(30, every) : double.MaxValue))
                {
                    _netPoll = now;
                    UnSnapshot snap = ReadNetwork(s);
                    UnSnapshot old;
                    lock (Gate) { old = _net; _net = snap; }
                    if (snap.Ok) CompareNet(old, snap, s);
                    Fire(NetChanged);
                }
                bool proWanted = (now - _proWanted).TotalSeconds < 30;
                if (ProtectConfigured(s) && (now - _proPoll).TotalSeconds >= (proWanted ? 30 : 120))
                {
                    _proPoll = now;
                    PrSnapshot snap = ReadProtect(s);
                    lock (Gate)
                    {
                        // keep live states the device stream told us since (they are newer)
                        if (!snap.Ok && _pro.Ok) { _pro.Error = snap.Error; snap = null; }
                        if (snap != null) _pro = snap;
                    }
                    Fire(ProtectChanged);
                }
                StartStreams();
            }
            catch (Exception ex) { Log.Write("UniFi: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>All pages of a list: { offset, limit, count, totalCount, data: [...] } (or a plain array).</summary>
        static List<JsonElement> Pages(BarSettings s, string path, List<JsonDocument> keep)
        {
            var all = new List<JsonElement>();
            int offset = 0;
            for (int page = 0; page < 20; page++)
            {
                string sep = path.Contains("?") ? "&" : "?";
                JsonDocument d = JsonDocument.Parse(Get(s, false, path + sep + "offset=" + offset + "&limit=200"));
                keep.Add(d);
                JsonElement root = d.RootElement;
                if (root.ValueKind == JsonValueKind.Array) { foreach (JsonElement e in root.EnumerateArray()) all.Add(e); break; }
                JsonElement data = Obj(root, "data");
                if (data.ValueKind != JsonValueKind.Array) break;
                int n = 0;
                foreach (JsonElement e in data.EnumerateArray()) { all.Add(e); n++; }
                long total = Long(root, "totalCount");
                offset += n;
                if (n == 0 || total <= 0 || offset >= total) break;
            }
            return all;
        }

        static UnSnapshot ReadNetwork(BarSettings s)
        {
            var snap = new UnSnapshot { Fetched = DateTime.UtcNow };
            var keep = new List<JsonDocument>();
            try
            {
                // the site: the one named in the settings, else the first ("Default")
                if (_siteId.Length == 0 || !string.Equals(_siteName, UnSettings.Of(s).Site, StringComparison.OrdinalIgnoreCase))
                {
                    _siteId = ""; _siteName = UnSettings.Of(s).Site;
                    foreach (JsonElement e in Pages(s, "/v1/sites", keep))
                    {
                        string id = Str(e, "id"), name = Str(e, "name"), refName = Str(e, "internalReference");
                        if (_siteId.Length == 0) { _siteId = id; _siteLabel = name; }
                        if (_siteName.Length > 0 && (string.Equals(name, _siteName, StringComparison.OrdinalIgnoreCase) || string.Equals(refName, _siteName, StringComparison.OrdinalIgnoreCase) || id == _siteName))
                        { _siteId = id; _siteLabel = name; break; }
                    }
                    if (_siteId.Length == 0) throw new InvalidOperationException("UniFi Network has no sites");
                }
                snap.Site = _siteLabel;
                string site = "/v1/sites/" + Uri.EscapeDataString(_siteId);
                foreach (JsonElement e in Pages(s, site + "/devices", keep))
                {
                    var d = new UnDevice
                    {
                        Id = Str(e, "id"), Name = Str(e, "name"), Model = Str(e, "model"), Mac = Str(e, "macAddress"), Ip = Str(e, "ipAddress"),
                        State = Str(e, "state").ToUpperInvariant(), Firmware = Str(e, "firmwareVersion"), Updatable = Bool(e, "firmwareUpdatable")
                    };
                    d.Kind = KindOf(e, d.Model);
                    snap.Devices.Add(d);
                }
                // statistics (CPU, memory, uptime, uplink rates) for the ones that are online
                foreach (UnDevice d in snap.Devices)
                {
                    if (!d.Online) continue;
                    try
                    {
                        using (JsonDocument st = JsonDocument.Parse(Get(s, false, site + "/devices/" + Uri.EscapeDataString(d.Id) + "/statistics/latest")))
                        {
                            JsonElement r = st.RootElement;
                            d.HasStats = true;
                            d.UptimeSec = Long(r, "uptimeSec");
                            d.Cpu = Num(r, "cpuUtilizationPct", -1);
                            d.Mem = Num(r, "memoryUtilizationPct", -1);
                            JsonElement up = Obj(r, "uplink");
                            d.TxBps = Num(up, "txRateBps", -1);
                            d.RxBps = Num(up, "rxRateBps", -1);
                        }
                    }
                    catch { }
                }
                // ports (switches / gateways): details now and then
                bool fresh = (DateTime.UtcNow - _detailsPoll).TotalSeconds > 60;
                if (fresh) _detailsPoll = DateTime.UtcNow;
                foreach (UnDevice d in snap.Devices)
                {
                    UnDevice known;
                    if (!fresh && _details.TryGetValue(d.Id, out known)) { d.Ports = known.Ports; continue; }
                    try
                    {
                        using (JsonDocument dd = JsonDocument.Parse(Get(s, false, site + "/devices/" + Uri.EscapeDataString(d.Id))))
                        {
                            JsonElement r = dd.RootElement;
                            if (d.Firmware.Length == 0) d.Firmware = Str(r, "firmwareVersion");
                            JsonElement ports = Obj(Obj(r, "interfaces"), "ports");
                            if (ports.ValueKind == JsonValueKind.Array)
                                foreach (JsonElement p in ports.EnumerateArray())
                                {
                                    JsonElement poe = Obj(p, "poe");
                                    d.Ports.Add(new UnPort
                                    {
                                        Idx = (int)Long(p, "idx"), State = Str(p, "state").ToUpperInvariant(), Connector = Str(p, "connector"),
                                        SpeedMbps = (int)Long(p, "speedMbps"), MaxSpeedMbps = (int)Long(p, "maxSpeedMbps"),
                                        Poe = poe.ValueKind == JsonValueKind.Object && Bool(poe, "enabled"), PoeState = Str(poe, "state")
                                    });
                                }
                            // in port order (1, 2, 3 … 10, 11), whatever order UniFi lists them in
                            d.Ports.Sort(delegate (UnPort a, UnPort b) { return a.Idx.CompareTo(b.Idx); });
                        }
                        _details[d.Id] = d;
                    }
                    catch { }
                }
                foreach (JsonElement e in Pages(s, site + "/clients", keep))
                {
                    var c = new UnClient
                    {
                        Id = Str(e, "id"), Name = Str(e, "name"), Ip = Str(e, "ipAddress"), Mac = Str(e, "macAddress"), Type = Str(e, "type").ToUpperInvariant(),
                        UplinkId = Str(e, "uplinkDeviceId")
                    };
                    DateTime at;
                    if (DateTime.TryParse(Str(e, "connectedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at)) c.ConnectedAt = at;
                    snap.Clients.Add(c);
                    UnDevice up = snap.Device(c.UplinkId);
                    if (up != null) up.Clients++;
                }
                MarkNew(snap);
                snap.Ok = true;
            }
            catch (Exception ex) { snap.Error = ex.Message; }
            finally { foreach (JsonDocument d in keep) d.Dispose(); }
            return snap;
        }
        static string _siteName = "", _siteLabel = "";

        static string KindOf(JsonElement e, string model)
        {
            var f = new List<string>();
            JsonElement fe = Obj(e, "features");
            if (fe.ValueKind == JsonValueKind.Array) foreach (JsonElement x in fe.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) f.Add(x.GetString().ToLowerInvariant());
            if (fe.ValueKind == JsonValueKind.Object) foreach (JsonProperty x in fe.EnumerateObject()) f.Add(x.Name.ToLowerInvariant());
            string m = (model ?? "").ToUpperInvariant();
            if (f.Contains("gateway") || m.StartsWith("UDM") || m.StartsWith("UCG") || m.StartsWith("UXG") || m.StartsWith("USG") || m.StartsWith("UDR") || m.Contains("DREAM") || m.Contains("GATEWAY") || m.StartsWith("UX")) return "gateway";
            if (f.Contains("accesspoint") || m.StartsWith("U6") || m.StartsWith("U7") || m.StartsWith("UAP") || m.StartsWith("E7") || m.Contains("AP")) return "ap";
            if (f.Contains("switching") || m.StartsWith("USW") || m.StartsWith("US-") || m.Contains("SWITCH")) return "switch";
            return "other";
        }

        // ---- clients seen before: kept in a file, so "new" means new to the network, not just since vShell started ----
        static HashSet<string> _known;
        static readonly Dictionary<string, DateTime> _firstSeen = new Dictionary<string, DateTime>();
        static bool _seeded;
        static string KnownPath { get { return Path.Combine(BarSettings.ConfigDir, "unifi-clients.json"); } }

        static void MarkNew(UnSnapshot snap)
        {
            if (_known == null)
            {
                _known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(KnownPath))
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(KnownPath));
                        if (list != null) foreach (string m in list) _known.Add(m);
                        _seeded = _known.Count > 0;
                    }
                }
                catch { }
            }
            bool added = false;
            DateTime now = DateTime.UtcNow;
            foreach (UnClient c in snap.Clients)
            {
                string key = c.Mac.Length > 0 ? c.Mac : c.Id;
                if (key.Length == 0) continue;
                if (_known.Add(key))
                {
                    added = true;
                    if (_seeded) _firstSeen[key] = now;       // the very first time: everything is "known", nothing is new
                }
                DateTime fs;
                c.IsNew = _firstSeen.TryGetValue(key, out fs) && (now - fs).TotalHours < 1;
            }
            if (!_seeded) _seeded = true;
            if (added)
                try { Directory.CreateDirectory(BarSettings.ConfigDir); File.WriteAllText(KnownPath, JsonSerializer.Serialize(new List<string>(_known))); } catch { }
        }

        // ---- network notices: devices offline / back, a new client, a busy access point ----
        static readonly HashSet<string> _busyAps = new HashSet<string>();
        static void CompareNet(UnSnapshot a, UnSnapshot b, BarSettings s)
        {
            UnSettings u = UnSettings.Of(s);
            Action<string, string, string, NotifyAbout> n = NetNotice;
            if (n == null || !a.Ok) return;
            if (u.NotifyDevices)
                foreach (UnDevice d in b.Devices)
                {
                    UnDevice was = a.Device(d.Id);
                    if (was == null) continue;
                    string title = Nice(d.Kind == "ap" ? "Access point" : d.Kind) + " " + d.Name + " is offline", detail = d.Model + (d.Ip.Length > 0 ? " · " + d.Ip : "");
                    // held for the wait you set; back before then (a restart / firmware update) = nothing said
                    if (was.Online && !d.Online) DownDelay.Down("unifi|" + d.Id, u.NotifyDelay, delegate { Try(n, "UniFi.DeviceDown", title, detail, NotifyAbout.Down(d.Name, d.Ip)); });
                    else if (!was.Online && d.Online && DownDelay.Up("unifi|" + d.Id) != UpResult.Cancelled) Try(n, "UniFi.DeviceUp", d.Name + " is back online", d.Model, NotifyAbout.Up(d.Name, d.Ip));
                }
            if (u.NotifyNewClient)
                foreach (UnClient c in b.Clients)
                {
                    if (!c.IsNew) continue;
                    bool had = a.Clients.Exists(delegate (UnClient x) { return x.Mac == c.Mac && x.Mac.Length > 0; });
                    if (!had) Try(n, "UniFi.NewClient", "New device on the network: " + c.Label, (c.Wireless ? "Wi-Fi" : "Wired") + (c.Ip.Length > 0 ? " · " + c.Ip : "") + (c.Mac.Length > 0 ? " · " + c.Mac : ""));
                }
            if (u.NotifyApClients > 0)
                foreach (UnDevice d in b.Devices)
                {
                    if (d.Kind != "ap") continue;
                    bool busy = d.Clients >= u.NotifyApClients;
                    if (busy && _busyAps.Add(d.Id)) Try(n, "UniFi.BusyAp", d.Name + " has " + d.Clients + " clients", "Over your limit of " + u.NotifyApClients);
                    else if (!busy && d.Clients < u.NotifyApClients - 2) _busyAps.Remove(d.Id);
                }
        }

        static void Try(Action<string, string, string, NotifyAbout> n, string k, string t, string d, NotifyAbout about = null) { try { n(k, t, d, about); } catch { } }

        // ---- Protect ----
        static PrSnapshot ReadProtect(BarSettings s)
        {
            var snap = new PrSnapshot { Fetched = DateTime.UtcNow };
            try
            {
                using (JsonDocument d = JsonDocument.Parse(Get(s, true, "/v1/cameras")))
                    foreach (JsonElement e in Items(d.RootElement))
                    {
                        JsonElement ff = Obj(e, "featureFlags");
                        var c = new PrCamera
                        {
                            Id = Str(e, "id"), Name = Str(e, "name"), State = Str(e, "state").ToUpperInvariant(), Model = Str(e, "modelKey"),
                            IsPtz = Bool(ff, "isPtz") || Bool(ff, "hasPtz"), IsDoorbell = Bool(ff, "isDoorbell") || Bool(ff, "hasChime") || Str(e, "type").IndexOf("doorbell", StringComparison.OrdinalIgnoreCase) >= 0
                        };
                        if (c.Name.Length == 0) c.Name = "Camera";
                        snap.Cameras.Add(c);
                    }
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(Get(s, true, "/v1/lights")))
                        foreach (JsonElement e in Items(d.RootElement))
                        {
                            JsonElement lm = Obj(e, "lightModeSettings");
                            snap.Lights.Add(new PrLight
                            {
                                Id = Str(e, "id"), Name = Str(e, "name"), State = Str(e, "state").ToUpperInvariant(), Mode = Str(lm, "mode"),
                                On = Bool(e, "isLightOn"), Forced = Bool(e, "isLightForceEnabled")
                            });
                        }
                }
                catch { }
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(Get(s, true, "/v1/nvrs")))
                    {
                        foreach (JsonElement e in Items(d.RootElement)) { snap.Nvr = Str(e, "name"); break; }
                        if (snap.Nvr.Length == 0 && d.RootElement.ValueKind == JsonValueKind.Object) snap.Nvr = Str(d.RootElement, "name");
                    }
                }
                catch { }
                snap.Ok = true;
            }
            catch (Exception ex) { snap.Error = ex.Message; }
            return snap;
        }

        static IEnumerable<JsonElement> Items(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) { foreach (JsonElement e in root.EnumerateArray()) yield return e; yield break; }
            JsonElement data = Obj(root, "data");
            if (data.ValueKind == JsonValueKind.Array) foreach (JsonElement e in data.EnumerateArray()) yield return e;
        }

        /// <summary>A camera's current snapshot (JPEG bytes), or null.</summary>
        public static Task<byte[]> SnapshotAsync(string cameraId, bool high)
        {
            return Task.Run(async delegate
            {
                // some cameras won't give a high-quality one (or not quickly): then the normal one
                byte[] jpg = await SnapshotOnce(cameraId, high);
                if (high && (jpg == null || jpg.Length < 100)) jpg = await SnapshotOnce(cameraId, false);
                return jpg;
            });
        }

        static Task<byte[]> SnapshotOnce(string cameraId, bool high)
        {
            BarSettings s = _s;
            return Task.Run(async delegate
            {
                if (s == null || !ProtectConfigured(s)) return (byte[])null;
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, Base(s) + "/proxy/protect/integration/v1/cameras/" + Uri.EscapeDataString(cameraId) + "/snapshot" + (high ? "?highQuality=true" : ""));
                    req.Headers.TryAddWithoutValidation("X-API-KEY", UnSettings.Of(s).ProtectKeyPlain);
                    using (HttpResponseMessage r = await Http(s).SendAsync(req))
                    {
                        if (!r.IsSuccessStatusCode) return null;
                        return await r.Content.ReadAsByteArrayAsync();
                    }
                }
                catch { return null; }
            });
        }

        /// <summary>The camera's RTSPS address (high quality; created if Protect has none yet), or throws.</summary>
        public static Task<string> StreamAsync(string cameraId)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                string path = "/v1/cameras/" + Uri.EscapeDataString(cameraId) + "/rtsps-stream";
                string json;
                try { json = Get(s, true, path); }
                catch { json = null; }
                string url = Pick(json);
                if (url.Length == 0) url = Pick(Send(s, HttpMethod.Post, path, "{\"qualities\":[\"high\"]}"));
                if (url.Length == 0) throw new InvalidOperationException("Protect gave no stream address for this camera.");
                return url;
            });
        }

        static string Pick(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                using (JsonDocument d = JsonDocument.Parse(json))
                    foreach (string q in new[] { "high", "medium", "low", "package" })
                    {
                        string u = Str(d.RootElement, q);
                        if (u.Length > 0) return u;
                    }
            }
            catch { }
            return "";
        }

        /// <summary>PTZ: go to a preset slot (-1 = home). Returns null or why not.</summary>
        public static Task<string> PtzAsync(string cameraId, int slot)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                try { Send(s, HttpMethod.Post, "/v1/cameras/" + Uri.EscapeDataString(cameraId) + "/ptz/goto/" + slot, null); return (string)null; }
                catch (Exception ex) { return ex.Message; }
            });
        }

        /// <summary>A light on (forced on) or back to its own mode. Returns null or why not.</summary>
        public static Task<string> LightAsync(string lightId, bool on)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                try
                {
                    Send(s, new HttpMethod("PATCH"), "/v1/lights/" + Uri.EscapeDataString(lightId), "{\"isLightForceEnabled\":" + (on ? "true" : "false") + "}");
                    lock (Gate) foreach (PrLight l in _pro.Lights) if (l.Id == lightId) { l.Forced = on; l.On = on; }
                    Fire(ProtectChanged);
                    _proPoll = DateTime.UtcNow.AddSeconds(-28);
                    return (string)null;
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        // ---- the live streams: events (rings, detections, motion) and device changes ----
        static void StartStreams()
        {
            BarSettings s = _s;
            if (s == null) return;
            bool want = ProtectConfigured(s);
            string key = want ? Base(s) + "|" + UnSettings.Of(s).ProtectKey + "|" + UnSettings.Of(s).IgnoreCert : "";
            lock (Gate)
            {
                if (key == _wsKey && (_wsThread != null || key.Length == 0)) return;
                _wsKey = key;
                _wsStop = true;                     // the old one stops at its next read / reconnect
                _wsThread = null;
                if (!want) return;
                _wsStop = false;
                string k = key;
                _wsThread = new Thread(delegate () { StreamLoop(k); }) { IsBackground = true, Name = "Protect streams" };
                _wsThread.Start();
            }
        }

        static void StreamLoop(string key)
        {
            int wait = 2;
            while (!_wsStop && key == _wsKey)
            {
                BarSettings s = _s;
                using (var cts = new CancellationTokenSource())
                {
                    try
                    {
                        // both streams together: when one drops, both are closed and opened again
                        var events = Task.Run(delegate { Listen(s, "/v1/subscribe/events", key, OnEvent, cts.Token); });
                        var devices = Task.Run(delegate { Listen(s, "/v1/subscribe/devices", key, OnDevice, cts.Token); });
                        int done = Task.WaitAny(events, devices);
                        Task first = done == 0 ? events : devices;
                        if (first.Exception != null) Log.Write("Protect stream: " + first.Exception.GetBaseException().Message);
                        else wait = 2;
                        // the other one is cancelled below and ends with an error nobody waits for: look at it, or .NET logs it as an
                        // "unobserved task error" (with its whole stack) on every reconnect
                        Task other = done == 0 ? devices : events;
                        other.ContinueWith(t => { var unused = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch (Exception ex) { Log.Write("Protect stream: " + ex.Message); }
                    try { cts.Cancel(); } catch { }
                }
                for (int i = 0; i < wait * 10 && !_wsStop && key == _wsKey; i++) Thread.Sleep(100);
                wait = Math.Min(60, wait * 2);
            }
        }

        static void Listen(BarSettings s, string path, string key, Action<JsonElement> handle, CancellationToken stop)
        {
            string url = Base(s).Replace("https://", "wss://").Replace("http://", "ws://") + "/proxy/protect/integration" + path;
            using (var ws = new ClientWebSocket())
            {
                if (UnSettings.Of(s).IgnoreCert) ws.Options.RemoteCertificateValidationCallback = delegate { return true; };
                ws.Options.SetRequestHeader("X-API-KEY", UnSettings.Of(s).ProtectKeyPlain);
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                ws.ConnectAsync(new Uri(url), stop).GetAwaiter().GetResult();
                var buf = new byte[64 * 1024];
                var msg = new MemoryStream();
                while (ws.State == WebSocketState.Open && !_wsStop && key == _wsKey && !stop.IsCancellationRequested)
                {
                    WebSocketReceiveResult r;
                    using (var quiet = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    {
                        quiet.CancelAfter(TimeSpan.FromMinutes(15));      // nothing for 15 minutes: start over
                        r = ws.ReceiveAsync(new ArraySegment<byte>(buf), quiet.Token).GetAwaiter().GetResult();
                    }
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    msg.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    string text = Encoding.UTF8.GetString(msg.ToArray());
                    msg.SetLength(0);
                    try
                    {
                        using (JsonDocument d = JsonDocument.Parse(text)) handle(d.RootElement);
                    }
                    catch (Exception ex) { Log.Write("Protect message: " + ex.Message); }
                }
            }
        }

        /// <summary>{ type: add | update, item: { id, type: ring / motion / smartDetectZone / ..., device, smartDetectTypes, start } }</summary>
        internal static void OnEvent(JsonElement m)
        {
            JsonElement it = Obj(m, "item");
            if (it.ValueKind != JsonValueKind.Object) it = m;
            string id = Str(it, "id");
            if (id.Length == 0) return;
            string type = Str(it, "type");
            var types = new List<string>();
            JsonElement st = Obj(it, "smartDetectTypes");
            if (st.ValueKind == JsonValueKind.Array) foreach (JsonElement x in st.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) types.Add(x.GetString().ToLowerInvariant());
            PrEvent ev;
            var fresh = new List<string>();
            lock (Gate)
            {
                ev = _events.Find(delegate (PrEvent x) { return x.Id == id; });
                if (ev == null)
                {
                    ev = new PrEvent { Id = id, Type = type };
                    long start = Long(it, "start");
                    if (start > 0) ev.Start = DateTimeOffset.FromUnixTimeMilliseconds(start).UtcDateTime;
                    _events.Insert(0, ev);
                    if (_events.Count > 100) _events.RemoveRange(100, _events.Count - 100);
                    if (type == "ring" || type == "motion") fresh.Add(type);
                }
                if (type.Length > 0) ev.Type = type;
                string dev = Str(it, "device");
                if (dev.Length > 0) ev.CameraId = dev;
                PrCamera cam = _pro.Camera(ev.CameraId);
                if (cam != null) ev.CameraName = cam.Name;
                foreach (string t in types) if (!ev.SmartTypes.Contains(t)) { ev.SmartTypes.Add(t); fresh.Add(t); }
            }
            Action<PrEvent, string> h = ProtectEvent;
            if (h != null) foreach (string f in fresh) try { h(ev, f); } catch { }
            Fire(ProtectChanged);
        }

        /// <summary>{ type: update, item: { id, modelKey: camera / light, state, name, isLightOn ... } }: the newest state of a device.</summary>
        internal static void OnDevice(JsonElement m)
        {
            JsonElement it = Obj(m, "item");
            if (it.ValueKind != JsonValueKind.Object) return;
            string id = Str(it, "id"), model = Str(it, "modelKey");
            bool changed = false;
            lock (Gate)
            {
                if (model == "camera" || model == "")
                {
                    PrCamera c = _pro.Camera(id);
                    if (c != null)
                    {
                        string state = Str(it, "state");
                        if (state.Length > 0) { c.State = state.ToUpperInvariant(); changed = true; }
                        string name = Str(it, "name");
                        if (name.Length > 0) { c.Name = name; changed = true; }
                    }
                }
                if (model == "light" || model == "")
                    foreach (PrLight l in _pro.Lights)
                    {
                        if (l.Id != id) continue;
                        JsonElement v;
                        if (it.TryGetProperty("isLightOn", out v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)) { l.On = v.GetBoolean(); changed = true; }
                        if (it.TryGetProperty("isLightForceEnabled", out v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)) { l.Forced = v.GetBoolean(); changed = true; }
                        string state = Str(it, "state");
                        if (state.Length > 0) { l.State = state.ToUpperInvariant(); changed = true; }
                    }
            }
            if (changed) Fire(ProtectChanged);
        }

        /// <summary>Save a snapshot of the camera for an event (for its notification card); keeps the newest 60. Returns the file or "".</summary>
        public static async Task<string> SaveEventSnapshotAsync(PrEvent ev)
        {
            byte[] jpg = await SnapshotAsync(ev.CameraId, false);
            if (jpg == null || jpg.Length < 100) return "";
            try
            {
                string dir = Path.Combine(BarSettings.ConfigDir, "protect");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Safe(ev.Id) + ".jpg");
                File.WriteAllBytes(file, jpg);
                ev.Image = file;
                var old = new List<string>(Directory.GetFiles(dir, "*.jpg"));
                old.Sort(StringComparer.Ordinal);
                for (int i = 0; i < old.Count - 60; i++) try { File.Delete(old[i]); } catch { }
                return file;
            }
            catch { return ""; }
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "event";
        }

        // ---- the Integrations page ----
        public static Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            return Task.Run(delegate
            {
                if (string.IsNullOrWhiteSpace(UnSettings.Of(s).Url)) return new KeyValuePair<bool, string>(false, "Enter the console's address first.");
                if (!NetConfigured(s) && !ProtectConfigured(s)) return new KeyValuePair<bool, string>(false, "Enter a Network and / or a Protect API key first.");
                var parts = new List<string>();
                bool ok = true;
                _siteId = "";
                if (NetConfigured(s))
                {
                    UnSnapshot n = ReadNetwork(s);
                    if (n.Ok)
                    {
                        lock (Gate) _net = n;
                        parts.Add("Network: site " + n.Site + ", " + n.Devices.FindAll(delegate (UnDevice d) { return d.Online; }).Count + " of " + n.Devices.Count + " devices online, " + n.Clients.Count + " clients");
                    }
                    else { ok = false; parts.Add("Network: " + n.Error); }
                }
                if (ProtectConfigured(s))
                {
                    PrSnapshot p = ReadProtect(s);
                    if (p.Ok)
                    {
                        lock (Gate) _pro = p;
                        parts.Add("Protect" + (p.Nvr.Length > 0 ? " (" + p.Nvr + ")" : "") + ": " + p.Cameras.FindAll(delegate (PrCamera c) { return c.Online; }).Count + " of " + p.Cameras.Count + " cameras online, " + p.Lights.Count + " lights");
                    }
                    else { ok = false; parts.Add("Protect: " + p.Error); }
                }
                Fire(NetChanged); Fire(ProtectChanged);
                return new KeyValuePair<bool, string>(ok, string.Join(".  ", parts) + ".");
            });
        }

        // ---- helpers ----
        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        public static string Nice(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('_', ' ');
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        public static string Rate(double bps)
        {
            if (bps < 0) return "–";
            string[] u = { "bps", "Kbps", "Mbps", "Gbps" };
            int i = 0;
            while (bps >= 1000 && i < u.Length - 1) { bps /= 1000; i++; }
            return bps.ToString(bps < 10 && i > 0 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + u[i];
        }

        public static string Uptime(long sec)
        {
            if (sec <= 0) return "";
            long d = sec / 86400, h = sec % 86400 / 3600, m = sec % 3600 / 60;
            return d > 0 ? d + "d " + h + "h" : h > 0 ? h + "h " + m + "m" : m + "m";
        }

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v)) return v;
            return default(JsonElement);
        }

        static string Str(JsonElement e, string name)
        {
            JsonElement v = Obj(e, name);
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return "";
        }

        static long Long(JsonElement e, string name)
        {
            JsonElement v = Obj(e, name);
            long l;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out l)) return l;
            double d;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out d)) return (long)d;
            return 0;
        }

        static double Num(JsonElement e, string name, double def)
        {
            JsonElement v = Obj(e, name);
            double d;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out d)) return d;
            return def;
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v = Obj(e, name);
            return v.ValueKind == JsonValueKind.True;
        }
    }
}
