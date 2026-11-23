using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The printers, the preheat presets and the notifications - kept in vShell's settings under Integrations["printer"].</summary>
    public sealed class PtSettings
    {
        /// <summary>Notify every N % (Off = 0).</summary>
        public static readonly string[] Steps = { "Off", "10 %", "25 %", "50 %" };

        readonly SettingsBag _b;
        public PtSettings(BarSettings s) { _b = s.Integration("printer"); }
        public static PtSettings Of(BarSettings s) { return new PtSettings(s); }

        /// <summary>The printers, in the Plugins page's order - the same list every time (the Plugins page edits them in place).</summary>
        public List<PrinterConn> Printers { get { return _b.Live("Printers", delegate { return new List<PrinterConn>(); }); } }
        /// <summary>Preheat buttons: "name nozzle/bed", comma separated.</summary>
        public string Presets { get { return _b.Get("Presets", "PLA 210/60, PETG 240/80, ABS 250/100, TPU 225/50"); } set { _b.Set("Presets", value ?? ""); } }
        /// <summary>The run box keyword (without the !): !3d words shows the palette's printer commands for those words.</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "3d").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "3d" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        public bool NotifyStart { get { return _b.Get("NotifyStart", true); } set { _b.Set("NotifyStart", value); } }
        public bool NotifyDone { get { return _b.Get("NotifyDone", true); } set { _b.Set("NotifyDone", value); } }
        /// <summary>Cancelled, failed, Klipper shut down.</summary>
        public bool NotifyFail { get { return _b.Get("NotifyFail", true); } set { _b.Set("NotifyFail", value); } }
        public bool NotifyPause { get { return _b.Get("NotifyPause", true); } set { _b.Set("NotifyPause", value); } }
        /// <summary>Every N % printed (10, 25 or 50; 0 = off).</summary>
        public int NotifyEvery { get { int v = _b.Get("NotifyEvery", 0); return v == 10 || v == 25 || v == 50 ? v : 0; } set { _b.Set("NotifyEvery", value); } }
        /// <summary>When the print reaches this layer (0 = off).</summary>
        public int NotifyLayer { get { return Math.Max(0, Math.Min(100000, _b.Get("NotifyLayer", 0))); } set { _b.Set("NotifyLayer", value); } }
        /// <summary>After a print, when the bed has cooled to CoolTemp.</summary>
        public bool NotifyCooled { get { return _b.Get("NotifyCooled", false); } set { _b.Set("NotifyCooled", value); } }
        public int CoolTemp { get { return Math.Max(20, Math.Min(80, _b.Get("CoolTemp", 40))); } set { _b.Set("CoolTemp", value); } }
    }

    /// <summary>A printer module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class PtView
    {
        /// <summary>Bar: the time shown - time left, the finish time, both or none.</summary>
        public static readonly string[] BarTimes = { "Left", "Finish", "Both", "None" };
        public static readonly string[] CamModes = { "Live", "Snapshots" };

        readonly SettingsBag _b;
        public PtView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static PtView Of(ModuleConfig cfg) { return new PtView(cfg); }

        /// <summary>Which printer (its Id on the Plugins page); "" = every printer.</summary>
        public string Printer { get { return _b.Get("Printer", ""); } set { _b.Set("Printer", value ?? ""); } }
        /// <summary>Bar: the nozzle and bed temperatures after the progress.</summary>
        public bool BarTemps { get { return _b.Get("BarTemps", true); } set { _b.Set("BarTemps", value); } }
        /// <summary>Bar: Left (time left), Finish (the time it'll be done), Both or None.</summary>
        public string BarTime { get { string v = _b.Get("BarTime", "Left"); return Array.IndexOf(BarTimes, v) < 0 ? "Left" : v; } set { _b.Set("BarTime", value); } }
        public bool ShowCam { get { return _b.Get("ShowCam", true); } set { _b.Set("ShowCam", value); } }
        /// <summary>Hide the module (and a widget with nothing else to show) while its printer can't be reached - back when it is.</summary>
        public bool HideOffline { get { return _b.Get("HideOffline", false); } set { _b.Set("HideOffline", value); } }
        /// <summary>The webcam is folded away (its title line only) - click the line to show it.</summary>
        public bool CamFolded { get { return _b.Get("CamFolded", false); } set { _b.Set("CamFolded", value); } }
        /// <summary>The webcam: "Live" (the MJPEG stream, smooth) or "Snapshots" (one picture every CamSeconds - lighter).</summary>
        public string CamMode { get { string v = _b.Get("CamMode", "Live"); return Array.IndexOf(CamModes, v) < 0 ? "Live" : v; } set { _b.Set("CamMode", value); } }
        /// <summary>Live: at most this many frames a second.</summary>
        public int CamFps { get { return Math.Max(1, Math.Min(30, _b.Get("CamFps", 15))); } set { _b.Set("CamFps", value); } }
        /// <summary>How often the webcam picture is refreshed (seconds).</summary>
        public int CamSeconds { get { return Math.Max(1, Math.Min(60, _b.Get("CamSeconds", 3))); } set { _b.Set("CamSeconds", value); } }
        public bool ShowTemps { get { return _b.Get("ShowTemps", true); } set { _b.Set("ShowTemps", value); } }
        public bool ShowSpool { get { return _b.Get("ShowSpool", true); } set { _b.Set("ShowSpool", value); } }
        public bool ShowPower { get { return _b.Get("ShowPower", true); } set { _b.Set("ShowPower", value); } }
        /// <summary>The Files / History / Queue / Macros / Console tabs.</summary>
        public bool ShowLists { get { return _b.Get("ShowLists", true); } set { _b.Set("ShowLists", value); } }
        /// <summary>The "Fluidd" button (opens the printer's web page).</summary>
        public bool ShowFluidd { get { return _b.Get("ShowFluidd", true); } set { _b.Set("ShowFluidd", value); } }
    }

    /// <summary>A 3D printer running Klipper + Moonraker (Fluidd / Mainsail), as set up on the Plugins page.</summary>
    public sealed class PrinterConn
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Fluidd's address (http://printer.local) or Moonraker's own (http://192.168.1.50:7125).</summary>
        public string Url { get; set; } = "";
        /// <summary>Moonraker API key (only needed when Moonraker asks for one), stored encrypted.</summary>
        public string ApiKey { get; set; } = "";
        public bool IgnoreCert { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>The webcam to show (its name in Fluidd); "" = the first one, "Off" = none.</summary>
        public string Webcam { get; set; } = "";

        public string Shown
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Name.Trim();
                try { return new Uri(Moonraker.Base(this)).Host; } catch { return "Printer"; }
            }
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            if (Name == null) Name = "";
            if (Url == null) Url = "";
            Url = Url.Trim();
            ApiKey = Secret.Protect(ApiKey ?? "");
            if (Webcam == null) Webcam = "";
        }
    }

    public sealed class PrHeater
    {
        /// <summary>Klipper's object name: extruder, heater_bed, "heater_generic chamber", "temperature_sensor mcu_temp".</summary>
        public string Name = "";
        public string Label = "";
        public double Temp, Target, Power;
        /// <summary>A heater (has a target) - else just a temperature sensor.</summary>
        public bool IsHeater;
        /// <summary>Recent temperatures (oldest first) for the small graph; null for sensors.</summary>
        public double[] History;
    }

    public sealed class PrFile
    {
        public string Path = "";            // relative to the gcodes folder
        public double Modified;             // seconds since 1970
        public long Size;
        public string Name { get { int i = Path.LastIndexOf('/'); return i >= 0 ? Path.Substring(i + 1) : Path; } }
    }

    public sealed class PrMeta
    {
        public double Estimated;            // seconds (slicer)
        public double FilamentTotal;        // mm
        public double FilamentWeight;       // g
        public int LayerCount;
        public double ObjectHeight;
        /// <summary>The best thumbnail's path under gcodes ("" = none).</summary>
        public string Thumb = "";
        public string Slicer = "";
    }

    public sealed class PrJob
    {
        public string Id = "", File = "", Status = "";
        public double Start, End, PrintDuration, FilamentUsed;
        public string Thumb = "";
    }

    public sealed class PrQueued { public string Id = "", File = ""; public double Added; }

    public sealed class PrPower
    {
        public string Device = "", Status = "", Type = "";
        public bool Locked;      // locked while printing
        public bool On { get { return Status == "on"; } }
    }

    public sealed class PrSpool
    {
        public int Id;
        public string Name = "", Material = "", Vendor = "", ColorHex = "";
        public double Remaining, Initial, Used;      // g
        public string Label
        {
            get
            {
                var parts = new List<string>();
                if (Vendor.Length > 0) parts.Add(Vendor);
                if (Material.Length > 0) parts.Add(Material);
                if (Name.Length > 0 && Name != Material) parts.Add(Name);
                return parts.Count > 0 ? string.Join(" ", parts) : "Spool " + Id;
            }
        }
    }

    /// <summary>A light Klipper drives: an [output_pin] with light / led / lamp in its name, or an [led] / [neopixel] / [dotstar] strip.</summary>
    public sealed class PtLight
    {
        /// <summary>Klipper's object name ("output_pin caselight", "neopixel chamber").</summary>
        public string Object = "";
        public string Name { get { int sp = Object.IndexOf(' '); return sp >= 0 ? Object.Substring(sp + 1) : Object; } }
        public bool IsPin { get { return Object.StartsWith("output_pin "); } }
        public double Value;      // pins: 0..1; strips: the brightest channel
        public bool On { get { return Value > 0.001; } }
    }

    public sealed class PrCam
    {
        public string Name = "", Snapshot = "", Stream = "", Service = "";
        public bool FlipH, FlipV;
        public int Rotation;
    }

    /// <summary>One printer, as last read.</summary>
    public sealed class PrinterState
    {
        public string PrinterId = "", PrinterName = "", WebUrl = "";
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public bool Ok { get { return Fetched != DateTime.MinValue; } }

        /// <summary>Klipper: ready, startup, shutdown, error, disconnected.</summary>
        public string Klippy = "";
        public string KlippyMessage = "";
        /// <summary>The print: standby, printing, paused, complete, cancelled, error.</summary>
        public string State = "";
        public string Message = "";
        public string File = "";
        public double Progress;            // 0..1
        public double PrintDuration, TotalDuration, FilamentUsed;
        public int Layer, Layers;
        public double Fan = -1;            // part fan 0..1 (-1 = none)
        public readonly List<PrHeater> Heaters = new List<PrHeater>();
        public PrMeta Meta;                // of File (null = not read yet)

        // extras (read every half minute)
        public readonly List<PrPower> Power = new List<PrPower>();
        public PrSpool Spool;
        public bool HasSpoolman;
        public readonly List<PrCam> Cams = new List<PrCam>();
        public readonly List<PrQueued> Queue = new List<PrQueued>();
        public string QueueState = "";
        public bool HasQueue;
        public readonly List<PrJob> History = new List<PrJob>();
        public readonly List<string> Macros = new List<string>();
        public readonly List<PtLight> Lights = new List<PtLight>();

        public bool Ready { get { return Klippy == "ready"; } }
        public bool Printing { get { return State == "printing"; } }
        public bool Paused { get { return State == "paused"; } }
        public bool Busy { get { return Printing || Paused; } }

        public PrHeater Heater(string name) { return Heaters.Find(delegate (PrHeater h) { return h.Name == name; }); }

        /// <summary>Seconds left (-1 = not known): the slicer's estimate when there is one, else from the time so far.</summary>
        public double Remaining
        {
            get
            {
                if (!Busy || Progress <= 0.001) return -1;
                double byProgress = PrintDuration / Progress - PrintDuration;
                if (Meta != null && Meta.Estimated > 0)
                {
                    double bySlicer = Math.Max(0, Meta.Estimated * (1 - Progress));
                    // early on the slicer knows best; later the real pace does
                    return Progress < 0.1 ? bySlicer : (bySlicer + byProgress) / 2;
                }
                return byProgress;
            }
        }

        /// <summary>"Printing", "Paused", "Idle", "Done", "Cancelled", "Error", "Klipper shut down" ...</summary>
        public string StateText
        {
            get
            {
                if (!Ok) return Error.Length > 0 ? "Can't reach it" : "Connecting…";
                switch (Klippy)
                {
                    case "shutdown": return "Klipper shut down";
                    case "error": return "Klipper error";
                    case "startup": return "Klipper starting…";
                    case "disconnected": return "Klipper not connected";
                }
                switch (State)
                {
                    case "printing": return "Printing";
                    case "paused": return "Paused";
                    case "complete": return "Done";
                    case "cancelled": return "Cancelled";
                    case "error": return "Print failed";
                    default: return "Idle";
                }
            }
        }

        /// <summary>good / busy / bad / off - the dot colour.</summary>
        public string Health
        {
            get
            {
                if (!Ok || Error.Length > 0 && Fetched < DateTime.UtcNow.AddSeconds(-60)) return "bad";
                if (Klippy == "shutdown" || Klippy == "error" || State == "error") return "bad";
                if (Klippy != "ready") return "busy";
                if (Paused) return "busy";
                if (Printing) return "good";
                return "off";
            }
        }
    }

    /// <summary>
    /// 3D printers with Klipper + Moonraker (what Fluidd and Mainsail talk to): the print, temperatures, webcam, files, history, the job
    /// queue, Spoolman and power devices, and pause / resume / cancel / emergency stop / temperatures / start a print. Each printer is read
    /// every 2 seconds while a module shows it, every 10 seconds otherwise when its notifications are on. One that can't be reached is
    /// tried less and less often (up to once a minute); opening its panel tries it straight away.
    /// </summary>
    public static class Moonraker
    {
        sealed class Link
        {
            public PrinterConn Conn;
            public HttpClient Http;
            public string HttpKey = "";
            public PrinterState Snap = new PrinterState();
            public int Busy;
            /// <summary>Reads in a row that failed (the printer off): each waits longer before the next try, up to a minute.</summary>
            public int Fails;
            public DateTime LastPoll = DateTime.MinValue, LastExtras = DateTime.MinValue, Retry = DateTime.MinValue;
            public List<string> HeaterNames = new List<string> { "extruder", "heater_bed" }, SensorNames = new List<string>(), LightNames = new List<string>();
            public readonly Dictionary<string, List<double>> Temps = new Dictionary<string, List<double>>();
            public readonly Dictionary<string, PrMeta> Metas = new Dictionary<string, PrMeta>();
            public List<PrFile> Files;
            public DateTime FilesAt = DateTime.MinValue;
            // notifications
            int _lastStep = -1;
            public int LastStep { get { return _lastStep; } set { _lastStep = value; } }
            public bool WatchCool;
            public string CamLog = "", SnapErr = "";
            public DateTime DoneAt;
        }

        static readonly object Gate = new object();
        static readonly Dictionary<string, Link> Links = new Dictionary<string, Link>();
        static readonly Dictionary<string, byte[]> Thumbs = new Dictionary<string, byte[]>();
        static Timer _timer;
        static BarSettings _s;
        static DateTime _wanted = DateTime.MinValue;

        /// <summary>A notice: (printer, kind key "Printer.Done" ..., title, detail, web address, picture file or null).</summary>
        public static event Action<PrinterConn, string, string, string, string, string> Notice;
        public static event Action Changed;

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            foreach (PrinterConn p in PtSettings.Of(s).Printers) if (p != null && p.Enabled && !string.IsNullOrWhiteSpace(p.Url)) return true;
            return false;
        }

        public static void Configure(BarSettings s)
        {
            _s = s;
            lock (Gate)
            {
                var keep = new HashSet<string>();
                List<PrinterConn> printers = PtSettings.Of(s).Printers;
                printers.RemoveAll(delegate (PrinterConn pc) { return pc == null; });
                foreach (PrinterConn p in printers)
                {
                    p.Normalize();      // (an API key typed into settings.json by hand gets encrypted)
                    if (string.IsNullOrWhiteSpace(p.Url)) continue;
                    keep.Add(p.Id);
                    Link l;
                    if (!Links.TryGetValue(p.Id, out l)) { l = new Link(); Links[p.Id] = l; }
                    l.Conn = p;
                }
                foreach (string id in new List<string>(Links.Keys)) if (!keep.Contains(id)) Links.Remove(id);
                if (_timer == null && Links.Count > 0) _timer = new Timer(delegate { Tick(); }, null, 600, 1000);
            }
            Poke(null);
        }

        /// <summary>Every printer (in the Plugins page's order), as last read - and "someone is showing them" (read every 2 s).</summary>
        public static List<PrinterState> All()
        {
            _wanted = DateTime.UtcNow;
            var list = new List<PrinterState>();
            BarSettings s = _s;
            if (s == null) return list;
            lock (Gate)
                foreach (PrinterConn p in PtSettings.Of(s).Printers)
                {
                    Link l;
                    if (p.Enabled && Links.TryGetValue(p.Id, out l)) list.Add(l.Snap);
                }
            return list;
        }

        /// <summary>The plugin was removed: stop polling and forget what was read (Configure starts it again).</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                foreach (Link l in Links.Values) if (l.Http != null) try { l.Http.Dispose(); } catch { }
                Links.Clear();
            }
            _s = null;
        }

        public static PrinterConn Conn(string id)
        {
            lock (Gate) { Link l; return Links.TryGetValue(id ?? "", out l) ? l.Conn : null; }
        }

        /// <summary>Read now (null = every printer).</summary>
        public static void Poke(string id)
        {
            lock (Gate)
                foreach (Link l in Links.Values)
                    if (id == null || l.Conn.Id == id) { l.LastPoll = DateTime.MinValue; l.Retry = DateTime.MinValue; }
        }

        /// <summary>The address Moonraker answers on (with http:// added when missing, no trailing /).</summary>
        public static string Base(PrinterConn c)
        {
            string b = (c == null ? "" : c.Url ?? "").Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        /// <summary>The web page (Fluidd): the address without Moonraker's port 7125.</summary>
        public static string WebUrl(PrinterConn c)
        {
            try
            {
                var u = new Uri(Base(c));
                return u.Port == 7125 ? u.Scheme + "://" + u.Host : Base(c);
            }
            catch { return Base(c); }
        }

        static HttpClient Http(Link l)
        {
            PrinterConn c = l.Conn;
            string key = Base(c) + "|" + c.ApiKey + "|" + c.IgnoreCert;
            if (l.Http == null || key != l.HttpKey)
            {
                var handler = new HttpClientHandler();
                if (c.IgnoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                string k = Secret.Unprotect(c.ApiKey);
                if (!string.IsNullOrEmpty(k)) h.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", k.Trim());
                var old = l.Http;
                l.Http = h; l.HttpKey = key;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return l.Http;
        }

        sealed class MrException : Exception
        {
            public readonly int Code;
            public MrException(string m, int code) : base(m) { Code = code; }
        }

        /// <summary>GET / POST path; returns the "result" part as JSON text. Throws with Moonraker's own message.</summary>
        static async Task<string> Call(Link l, HttpMethod method, string path, string jsonBody = null)
        {
            using (var req = new HttpRequestMessage(method, Base(l.Conn) + path))
            {
                if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using (HttpResponseMessage r = await Http(l).SendAsync(req))
                {
                    string text = await r.Content.ReadAsStringAsync();
                    int code = (int)r.StatusCode;
                    if (!r.IsSuccessStatusCode)
                    {
                        string msg = "";
                        try
                        {
                            using (JsonDocument d = JsonDocument.Parse(text))
                            {
                                JsonElement err = Obj(d.RootElement, "error");
                                msg = err.ValueKind == JsonValueKind.Object ? Str(err, "message") : Str(d.RootElement, "error");
                            }
                        }
                        catch { msg = PlainError(text); }
                        if (code == 401 || code == 403) throw new MrException("Moonraker wants an API key (" + code + ")" + (msg.Length > 0 ? ": " + msg : ""), code);
                        string status = code + (string.IsNullOrEmpty(r.ReasonPhrase) ? "" : " " + r.ReasonPhrase);
                        throw new MrException((msg.Length > 0 ? msg : "Moonraker answered " + status), code);
                    }
                    using (JsonDocument d = JsonDocument.Parse(text))
                    {
                        JsonElement res;
                        return d.RootElement.TryGetProperty("result", out res) ? res.GetRawText() : text;
                    }
                }
            }
        }

        static Task<string> Get(Link l, string path) { return Call(l, HttpMethod.Get, path); }
        static Task<string> Post(Link l, string path, string body = null) { return Call(l, HttpMethod.Post, path, body); }

        /// <summary>
        /// An error answer that isn't Moonraker's JSON: a short plain-text line as it is; a web page (a proxy's "502 Bad Gateway" page)
        /// or anything long / several lines - nothing, so the status code is shown instead of the page.
        /// </summary>
        static string PlainError(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.Length > 160 || text.StartsWith("<") || text.IndexOf('\n') >= 0) return "";
            return text;
        }

        // ---- JSON helpers -------------------------------------------------------------------------------
        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return "";
        }

        static double Num(JsonElement e, string name, double def = 0)
        {
            JsonElement v;
            double n;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return def;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out n)) return n;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
        }

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) ? v : default(JsonElement);
        }

        static string Q(string s) { return Uri.EscapeDataString(s ?? ""); }

        /// <summary>A path under gcodes, each part escaped (for /server/files/gcodes/...).</summary>
        static string PathUrl(string path)
        {
            var parts = (path ?? "").Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        // =================================================================
        // Polling
        // =================================================================
        static void Tick()
        {
            try
            {
                BarSettings s = _s;
                if (s == null) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 8;
                PtSettings ps = PtSettings.Of(s);
                bool notify = ps.NotifyStart || ps.NotifyDone || ps.NotifyFail || ps.NotifyPause || ps.NotifyEvery > 0 || ps.NotifyLayer > 0 || ps.NotifyCooled;
                if (!shown && !notify) return;
                List<Link> links;
                lock (Gate) links = new List<Link>(Links.Values);
                foreach (Link l in links)
                {
                    if (!l.Conn.Enabled) continue;
                    if (DateTime.UtcNow < l.Retry) continue;
                    if ((DateTime.UtcNow - l.LastPoll).TotalSeconds < (shown ? 1.8 : 10)) continue;
                    if (Interlocked.Exchange(ref l.Busy, 1) == 1) continue;
                    l.LastPoll = DateTime.UtcNow;
                    Link me = l;
                    Task.Run(async delegate
                    {
                        try { await Poll(me, shown); }
                        catch (Exception ex) { Log.Write("Printer " + me.Conn.Shown + ": " + ex.Message); }
                        finally { Interlocked.Exchange(ref me.Busy, 0); }
                    });
                }
            }
            catch (Exception ex) { Log.Write("Printers: " + ex.Message); }
        }

        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        static async Task Poll(Link l, bool shown)
        {
            PrinterState prev = l.Snap;
            var now = new PrinterState { PrinterId = l.Conn.Id, PrinterName = l.Conn.Shown, WebUrl = WebUrl(l.Conn) };
            try
            {
                await ReadStatus(l, now);
                // the extras: every half minute (or straight away the first time / after a print starts or ends)
                bool fresh = !prev.Ok || prev.State != now.State || prev.File != now.File;
                if (fresh || (DateTime.UtcNow - l.LastExtras).TotalSeconds > (shown ? 30 : 120))
                {
                    l.LastExtras = DateTime.UtcNow;
                    await ReadExtras(l, now);
                }
                else CopyExtras(prev, now);
                if (now.File.Length > 0) now.Meta = await Meta(l, now.File);
                now.Fetched = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                string msg = ex is TaskCanceledException ? "no answer (timed out)" : ex is HttpRequestException && ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                if (prev.Error != msg) Log.Write("Printer " + l.Conn.Shown + ": " + msg);
                // keep what was known, with the error
                now = Copy(prev);
                now.Error = msg;
                now.PrinterName = l.Conn.Shown;
                // can't be reached (switched off): 5, 10, 20, 40 s, then once a minute (20, 40, 60 s with nothing showing it)
                l.Fails++;
                l.Retry = DateTime.UtcNow.AddSeconds(Math.Min(60, (shown ? 5 : 20) * Math.Pow(2, Math.Min(l.Fails - 1, 6))));
                l.Snap = now;
                Fire(Changed);
                return;
            }
            l.Fails = 0;
            l.Snap = now;
            if (prev.Ok) Compare(l, prev, now);
            Fire(Changed);
        }

        static PrinterState Copy(PrinterState p)
        {
            var n = new PrinterState
            {
                PrinterId = p.PrinterId, PrinterName = p.PrinterName, WebUrl = p.WebUrl, Fetched = p.Fetched, Klippy = p.Klippy, KlippyMessage = p.KlippyMessage,
                State = p.State, Message = p.Message, File = p.File, Progress = p.Progress, PrintDuration = p.PrintDuration, TotalDuration = p.TotalDuration,
                FilamentUsed = p.FilamentUsed, Layer = p.Layer, Layers = p.Layers, Fan = p.Fan, Meta = p.Meta
            };
            n.Heaters.AddRange(p.Heaters);
            n.Lights.AddRange(p.Lights);
            CopyExtras(p, n);
            return n;
        }

        static void CopyExtras(PrinterState from, PrinterState to)
        {
            to.Power.AddRange(from.Power);
            to.Spool = from.Spool; to.HasSpoolman = from.HasSpoolman;
            to.Cams.AddRange(from.Cams);
            to.Queue.AddRange(from.Queue); to.QueueState = from.QueueState; to.HasQueue = from.HasQueue;
            to.History.AddRange(from.History);
            to.Macros.AddRange(from.Macros);
        }

        /// <summary>Klipper's state, the print and the temperatures.</summary>
        static async Task ReadStatus(Link l, PrinterState now)
        {
            // Moonraker itself: is Klipper there?
            string info = await Get(l, "/server/info");
            using (JsonDocument d = JsonDocument.Parse(info))
            {
                now.Klippy = Str(d.RootElement, "klippy_state");
                if (!Bool(d.RootElement, "klippy_connected") && now.Klippy.Length == 0) now.Klippy = "disconnected";
            }
            if (now.Klippy != "ready")
            {
                // shut down / error: Klipper's message (why) - the objects can't be read
                try
                {
                    string pi = await Get(l, "/printer/info");
                    using (JsonDocument d = JsonDocument.Parse(pi)) now.KlippyMessage = Str(d.RootElement, "state_message").Trim();
                }
                catch { }
                return;
            }

            var objs = new List<string> { "webhooks", "print_stats", "virtual_sdcard", "display_status", "heaters", "fan" };
            objs.AddRange(l.HeaterNames);
            objs.AddRange(l.SensorNames);
            objs.AddRange(l.LightNames);
            var q = new StringBuilder();
            foreach (string o in objs) { q.Append(q.Length == 0 ? "?" : "&"); q.Append(Q(o)); }
            string res = await Get(l, "/printer/objects/query" + q);
            using (JsonDocument d = JsonDocument.Parse(res))
            {
                JsonElement st = Obj(d.RootElement, "status");
                JsonElement wh = Obj(st, "webhooks");
                if (wh.ValueKind == JsonValueKind.Object) { now.Klippy = Str(wh, "state"); now.KlippyMessage = Str(wh, "state_message").Trim(); }
                JsonElement ps = Obj(st, "print_stats");
                now.State = Str(ps, "state");
                now.Message = Str(ps, "message");
                now.File = Str(ps, "filename");
                now.PrintDuration = Num(ps, "print_duration");
                now.TotalDuration = Num(ps, "total_duration");
                now.FilamentUsed = Num(ps, "filament_used");
                JsonElement pinfo = Obj(ps, "info");
                now.Layer = (int)Num(pinfo, "current_layer");
                now.Layers = (int)Num(pinfo, "total_layer");
                double dp = Num(Obj(st, "display_status"), "progress");
                double vp = Num(Obj(st, "virtual_sdcard"), "progress");
                now.Progress = Math.Max(0, Math.Min(1, dp > 0 ? dp : vp));
                JsonElement fan = Obj(st, "fan");
                if (fan.ValueKind == JsonValueKind.Object) now.Fan = Num(fan, "speed");

                // which heaters / sensors there are (the next read asks for them all)
                JsonElement hs = Obj(st, "heaters");
                if (hs.ValueKind == JsonValueKind.Object)
                {
                    var heaters = new List<string>();
                    var sensors = new List<string>();
                    JsonElement ah = Obj(hs, "available_heaters"), asn = Obj(hs, "available_sensors");
                    if (ah.ValueKind == JsonValueKind.Array) foreach (JsonElement h in ah.EnumerateArray()) if (h.ValueKind == JsonValueKind.String) heaters.Add(h.GetString());
                    if (asn.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement h in asn.EnumerateArray())
                            if (h.ValueKind == JsonValueKind.String && !heaters.Contains(h.GetString())) sensors.Add(h.GetString());
                    if (heaters.Count > 0) l.HeaterNames = heaters;
                    l.SensorNames = sensors;
                }
                foreach (string name in l.HeaterNames)
                {
                    JsonElement h = Obj(st, name);
                    if (h.ValueKind != JsonValueKind.Object) continue;
                    var ph = new PrHeater { Name = name, Label = Label(name), Temp = Num(h, "temperature"), Target = Num(h, "target"), Power = Num(h, "power"), IsHeater = true };
                    List<double> hist;
                    if (!l.Temps.TryGetValue(name, out hist)) { hist = new List<double>(); l.Temps[name] = hist; }
                    hist.Add(ph.Temp);
                    if (hist.Count > 90) hist.RemoveRange(0, hist.Count - 90);
                    ph.History = hist.ToArray();
                    now.Heaters.Add(ph);
                }
                foreach (string name in l.SensorNames)
                {
                    JsonElement h = Obj(st, name);
                    if (h.ValueKind != JsonValueKind.Object) continue;
                    now.Heaters.Add(new PrHeater { Name = name, Label = Label(name), Temp = Num(h, "temperature") });
                }
                // lights: a pin's value, a strip's brightest channel
                foreach (string name in l.LightNames)
                {
                    JsonElement o = Obj(st, name);
                    if (o.ValueKind != JsonValueKind.Object) continue;
                    var li = new PtLight { Object = name };
                    if (li.IsPin) li.Value = Num(o, "value");
                    else
                    {
                        JsonElement cd = Obj(o, "color_data");
                        if (cd.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement px in cd.EnumerateArray())
                                if (px.ValueKind == JsonValueKind.Array)
                                    foreach (JsonElement ch in px.EnumerateArray())
                                    {
                                        double v;
                                        if (ch.ValueKind == JsonValueKind.Number && ch.TryGetDouble(out v)) li.Value = Math.Max(li.Value, v);
                                    }
                    }
                    now.Lights.Add(li);
                }
            }
        }

        /// <summary>"extruder" -> Nozzle, "heater_bed" -> Bed, "heater_generic chamber" -> Chamber, "temperature_sensor mcu_temp" -> Mcu temp.</summary>
        public static string Label(string name)
        {
            if (name == "extruder") return "Nozzle";
            if (name.StartsWith("extruder") && name.Length > 8) return "Nozzle " + (int.TryParse(name.Substring(8), out int n) ? (n + 1).ToString() : name.Substring(8));
            if (name == "heater_bed") return "Bed";
            int sp = name.IndexOf(' ');
            string s = (sp >= 0 ? name.Substring(sp + 1) : name).Replace('_', ' ').Trim();
            return s.Length == 0 ? name : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Power devices, Spoolman, webcams, the queue, history and macros - each only if the printer has it.</summary>
        static async Task ReadExtras(Link l, PrinterState now)
        {
            // power devices
            try
            {
                string r = await Get(l, "/machine/device_power/devices");
                using (JsonDocument d = JsonDocument.Parse(r))
                {
                    JsonElement devs = Obj(d.RootElement, "devices");
                    if (devs.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in devs.EnumerateArray())
                            now.Power.Add(new PrPower { Device = Str(e, "device"), Status = Str(e, "status"), Type = Str(e, "type"), Locked = Bool(e, "locked_while_printing") });
                }
            }
            catch { }

            // Spoolman: the active spool
            try
            {
                string r = await Get(l, "/server/spoolman/spool_id");
                now.HasSpoolman = true;
                int id = 0;
                using (JsonDocument d = JsonDocument.Parse(r)) id = (int)Num(d.RootElement, "spool_id");
                if (id > 0) now.Spool = await SpoolById(l, id);
            }
            catch { }

            // webcams
            try
            {
                string r = await Get(l, "/server/webcams/list");
                using (JsonDocument d = JsonDocument.Parse(r))
                {
                    JsonElement cams = Obj(d.RootElement, "webcams");
                    if (cams.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in cams.EnumerateArray())
                        {
                            JsonElement en;
                            if (e.TryGetProperty("enabled", out en) && en.ValueKind == JsonValueKind.False) continue;
                            now.Cams.Add(MakeCam(l.Conn, Str(e, "name"), Str(e, "snapshot_url"), Str(e, "stream_url"), Str(e, "service"),
                                                 Bool(e, "flip_horizontal"), Bool(e, "flip_vertical"), (int)Num(e, "rotation")));
                        }
                }
            }
            catch (Exception ex) { LogCams(l, "the webcam list: " + ex.Message); }
            // older Fluidd keeps its cameras in its own settings (database namespace "fluidd", key "cameras")
            if (now.Cams.Count == 0)
                try
                {
                    string r = await Get(l, "/server/database/item?namespace=fluidd&key=cameras");
                    using (JsonDocument d = JsonDocument.Parse(r))
                    {
                        JsonElement v = Obj(d.RootElement, "value");
                        JsonElement list = Obj(v, "cameras");
                        if (list.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in list.EnumerateArray())
                            {
                                JsonElement en;
                                if (e.TryGetProperty("enabled", out en) && en.ValueKind == JsonValueKind.False) continue;
                                string url = Str(e, "url");
                                if (url.Length == 0) url = Str(e, "urlStream");
                                now.Cams.Add(MakeCam(l.Conn, Str(e, "name"), Str(e, "urlSnapshot"), url, Str(e, "type"), Bool(e, "flipX"), Bool(e, "flipY"), (int)Num(e, "rotation")));
                            }
                    }
                }
                catch { }
            LogCams(l, now.Cams.Count == 0 ? "no webcams found" : CamText(now.Cams));

            // the job queue
            try
            {
                string r = await Get(l, "/server/job_queue/status");
                now.HasQueue = true;
                using (JsonDocument d = JsonDocument.Parse(r))
                {
                    now.QueueState = Str(d.RootElement, "queue_state");
                    JsonElement jobs = Obj(d.RootElement, "queued_jobs");
                    if (jobs.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in jobs.EnumerateArray())
                            now.Queue.Add(new PrQueued { Id = Str(e, "job_id"), File = Str(e, "filename"), Added = Num(e, "time_added") });
                }
            }
            catch { }

            // recent prints
            try
            {
                string r = await Get(l, "/server/history/list?limit=20&order=desc");
                using (JsonDocument d = JsonDocument.Parse(r))
                {
                    JsonElement jobs = Obj(d.RootElement, "jobs");
                    if (jobs.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in jobs.EnumerateArray())
                        {
                            var j = new PrJob
                            {
                                Id = Str(e, "job_id"), File = Str(e, "filename"), Status = Str(e, "status"), Start = Num(e, "start_time"), End = Num(e, "end_time"),
                                PrintDuration = Num(e, "print_duration"), FilamentUsed = Num(e, "filament_used")
                            };
                            JsonElement md = Obj(e, "metadata");
                            j.Thumb = BestThumb(md, j.File);
                            now.History.Add(j);
                        }
                }
            }
            catch { }

            // macros (Klipper's gcode_macro objects; the ones starting with _ are helpers)
            if (now.Ready)
                try
                {
                    var lights = new List<string>();
                    string r = await Get(l, "/printer/objects/list");
                    using (JsonDocument d = JsonDocument.Parse(r))
                    {
                        JsonElement objs = Obj(d.RootElement, "objects");
                        if (objs.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in objs.EnumerateArray())
                            {
                                string o = e.ValueKind == JsonValueKind.String ? e.GetString() : "";
                                if (IsLight(o)) lights.Add(o);
                                if (!o.StartsWith("gcode_macro ")) continue;
                                string m = o.Substring(12).Trim();
                                if (m.Length > 0 && !m.StartsWith("_")) now.Macros.Add(m);
                            }
                    }
                    now.Macros.Sort(StringComparer.OrdinalIgnoreCase);
                    lights.Sort(StringComparer.OrdinalIgnoreCase);
                    if (string.Join("|", lights) != string.Join("|", l.LightNames)) Log.Write("Printer " + l.Conn.Shown + ": lights " + (lights.Count == 0 ? "none" : string.Join(", ", lights)));
                    l.LightNames = lights;
                }
                catch { }
        }

        /// <summary>An LED strip, or an output pin whose name says it's a light.</summary>
        static bool IsLight(string o)
        {
            if (o.StartsWith("led ") || o.StartsWith("neopixel ") || o.StartsWith("dotstar ") || o.StartsWith("pca9533 ") || o.StartsWith("pca9632 ")) return true;
            if (!o.StartsWith("output_pin ")) return false;
            string n = o.Substring(11).ToLowerInvariant();
            return n.Contains("light") || n.Contains("led") || n.Contains("lamp");
        }

        static PrCam MakeCam(PrinterConn c, string name, string snapshot, string stream, string service, bool flipH, bool flipV, int rotation)
        {
            // no snapshot address: mjpg-streamer / ustreamer / camera-streamer answer "?action=snapshot" where the stream is "?action=stream"
            if (string.IsNullOrWhiteSpace(snapshot) && !string.IsNullOrWhiteSpace(stream) && stream.Contains("action=stream"))
                snapshot = stream.Replace("action=stream", "action=snapshot");
            return new PrCam
            {
                Name = name ?? "", Snapshot = Resolve(c, snapshot), Stream = Resolve(c, stream), Service = service ?? "", FlipH = flipH, FlipV = flipV, Rotation = rotation
            };
        }

        static string CamText(List<PrCam> cams)
        {
            var parts = new List<string>();
            foreach (PrCam c in cams) parts.Add("\"" + c.Name + "\" (" + (c.Service.Length > 0 ? c.Service + ", " : "") + "snapshot " + (c.Snapshot.Length > 0 ? c.Snapshot : "none") + ", stream " + (c.Stream.Length > 0 ? c.Stream : "none") + ")");
            return "webcams: " + string.Join("; ", parts);
        }

        /// <summary>Log what was found about the webcams - when it changes (not every half minute).</summary>
        static void LogCams(Link l, string text)
        {
            if (l.CamLog == text) return;
            l.CamLog = text;
            Log.Write("Printer " + l.Conn.Shown + ": " + text);
        }

        /// <summary>A webcam address as Fluidd uses it: relative ones are on the web server (not Moonraker's port 7125).</summary>
        static string Resolve(PrinterConn c, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim();
            if (url.Contains("://")) return url;
            string web = WebUrl(c);
            try
            {
                var u = new Uri(web);
                string origin = u.Scheme + "://" + u.Authority;
                return url.StartsWith("/") ? origin + url : origin + "/" + url;
            }
            catch { return web + (url.StartsWith("/") ? "" : "/") + url; }
        }

        /// <summary>The biggest thumbnail up to 400 px wide (its path under gcodes), or "".</summary>
        static string BestThumb(JsonElement meta, string file)
        {
            JsonElement th = Obj(meta, "thumbnails");
            if (th.ValueKind != JsonValueKind.Array) return "";
            string best = ""; double bw = -1;
            foreach (JsonElement t in th.EnumerateArray())
            {
                double w = Num(t, "width");
                string rel = Str(t, "relative_path");
                if (rel.Length == 0) continue;
                bool better = bw < 0 || (w <= 400 && (bw > 400 || w > bw)) || (bw > 400 && w < bw);
                if (better) { bw = w; best = rel; }
            }
            if (best.Length == 0) return "";
            int slash = (file ?? "").LastIndexOf('/');
            return (slash >= 0 ? file.Substring(0, slash + 1) : "") + best;
        }

        static async Task<PrMeta> Meta(Link l, string file)
        {
            PrMeta m;
            lock (l.Metas) if (l.Metas.TryGetValue(file, out m)) return m;
            m = new PrMeta();
            try
            {
                string r = await Get(l, "/server/files/metadata?filename=" + Q(file));
                using (JsonDocument d = JsonDocument.Parse(r))
                {
                    JsonElement e = d.RootElement;
                    m.Estimated = Num(e, "estimated_time");
                    m.FilamentTotal = Num(e, "filament_total");
                    m.FilamentWeight = Num(e, "filament_weight_total");
                    m.LayerCount = (int)Num(e, "layer_count");
                    m.ObjectHeight = Num(e, "object_height");
                    m.Slicer = Str(e, "slicer");
                    m.Thumb = BestThumb(e, file);
                }
            }
            catch { }
            lock (l.Metas)
            {
                if (l.Metas.Count > 200) l.Metas.Clear();
                l.Metas[file] = m;
            }
            return m;
        }

        static async Task<PrSpool> SpoolById(Link l, int id)
        {
            string r = await Post(l, "/server/spoolman/proxy", "{\"request_method\":\"GET\",\"path\":\"/v1/spool/" + id + "\"}");
            using (JsonDocument d = JsonDocument.Parse(r))
            {
                JsonElement e = d.RootElement;
                JsonElement inner = Obj(e, "response");        // Moonraker's "v2" answers wrap it
                if (inner.ValueKind == JsonValueKind.Object) e = inner;
                return ParseSpool(e);
            }
        }

        static PrSpool ParseSpool(JsonElement e)
        {
            JsonElement f = Obj(e, "filament");
            var sp = new PrSpool
            {
                Id = (int)Num(e, "id"), Name = Str(f, "name"), Material = Str(f, "material"), Vendor = Str(Obj(f, "vendor"), "name"),
                ColorHex = Str(f, "color_hex"), Remaining = Num(e, "remaining_weight", -1), Initial = Num(e, "initial_weight", Num(f, "weight", -1)), Used = Num(e, "used_weight")
            };
            if (sp.ColorHex.Length == 0) sp.ColorHex = Str(f, "multi_color_hexes").Split(',')[0];
            return sp;
        }

        // =================================================================
        // Notifications
        // =================================================================
        static void Compare(Link l, PrinterState prev, PrinterState now)
        {
            BarSettings s = _s;
            if (s == null) return;
            PtSettings ps = PtSettings.Of(s);
            PrinterConn c = l.Conn;
            string name = c.Shown, file = Short(now.File.Length > 0 ? now.File : prev.File);
            string url = WebUrl(c);

            // Klipper shut down / error
            if (prev.Klippy == "ready" && (now.Klippy == "shutdown" || now.Klippy == "error"))
            {
                if (ps.NotifyFail) Raise(c, "Printer.Problem", "⚠ " + name + ": Klipper " + (now.Klippy == "shutdown" ? "shut down" : "error"),
                                             FirstLine(now.KlippyMessage), url, null);
                return;
            }
            if (!now.Ready || !prev.Ready) return;

            bool wasBusy = prev.Busy;
            if (now.Printing && !wasBusy && (prev.State != "printing"))
            {
                l.LastStep = 0;
                l.WatchCool = false;
                if (ps.NotifyStart) Raise(c, "Printer.Progress", "▶ " + name + " started printing", file + (now.Meta != null && now.Meta.Estimated > 0 ? " · about " + Duration(now.Meta.Estimated) : ""), url, Picture(l, now));
            }
            else if (now.Paused && prev.Printing)
            {
                if (ps.NotifyPause) Raise(c, "Printer.Problem", "⏸ " + name + " paused", file + (now.Message.Length > 0 ? " · " + FirstLine(now.Message) : ""), url, null);
            }
            else if (now.State == "complete" && wasBusy)
            {
                l.WatchCool = true; l.DoneAt = DateTime.UtcNow;
                if (ps.NotifyDone) Raise(c, "Printer.Done", "✓ " + name + " finished printing",
                                             file + " · " + Duration(now.PrintDuration) + (now.FilamentUsed > 0 ? " · " + Filament(now.FilamentUsed) : ""), url, Picture(l, now));
            }
            else if (now.State == "cancelled" && wasBusy)
            {
                if (ps.NotifyFail) Raise(c, "Printer.Problem", "✕ " + name + ": print cancelled", file + " · after " + Duration(now.PrintDuration), url, null);
            }
            else if (now.State == "error" && prev.State != "error")
            {
                if (ps.NotifyFail) Raise(c, "Printer.Problem", "✕ " + name + ": print failed", file + (now.Message.Length > 0 ? " · " + FirstLine(now.Message) : ""), url, null);
            }

            // every N % and at layer N
            if (now.Printing && prev.Busy)
            {
                if (ps.NotifyEvery > 0)
                {
                    int step = (int)Math.Floor(now.Progress * 100 / ps.NotifyEvery);
                    if (l.LastStep < 0) l.LastStep = step;
                    else if (step > l.LastStep && step * ps.NotifyEvery < 100)
                    {
                        l.LastStep = step;
                        double left = now.Remaining;
                        Raise(c, "Printer.Progress", name + ": " + (step * ps.NotifyEvery) + " % printed",
                              file + (left > 0 ? " · " + Duration(left) + " left · done at " + TimeText.Clock(DateTime.Now.AddSeconds(left)) : ""), url, null);
                    }
                }
                if (ps.NotifyLayer > 0 && prev.Layer < ps.NotifyLayer && now.Layer >= ps.NotifyLayer)
                    Raise(c, "Printer.Progress", name + ": layer " + now.Layer + (now.Layers > 0 ? " of " + now.Layers : ""), file, url, null);
            }

            // the bed has cooled down after a print (safe to take it off)
            if (l.WatchCool && ps.NotifyCooled)
            {
                PrHeater bed = now.Heater("heater_bed");
                if (now.Busy || (DateTime.UtcNow - l.DoneAt).TotalHours > 6 || bed == null) l.WatchCool = false;
                else if (bed.Target <= 0 && bed.Temp <= ps.CoolTemp)
                {
                    l.WatchCool = false;
                    Raise(c, "Printer.Cooled", "❄ " + name + ": the bed has cooled to " + Math.Round(bed.Temp) + " °C", "Safe to take " + file + " off", url, null);
                }
            }
            else if (!ps.NotifyCooled) l.WatchCool = false;
        }

        static void Raise(PrinterConn c, string kind, string title, string detail, string url, string picture)
        {
            var n = Notice;
            if (n != null) try { n(c, kind, title, detail, url, picture); } catch { }
        }

        /// <summary>The current print's thumbnail as a file (for the notification card), or null.</summary>
        static string Picture(Link l, PrinterState now)
        {
            try
            {
                if (now.Meta == null || now.Meta.Thumb.Length == 0) return null;
                byte[] b = ThumbAsync(now.PrinterId, now.Meta.Thumb).GetAwaiter().GetResult();
                if (b == null) return null;
                string dir = Path.Combine(BarSettings.ConfigDir, "printer-thumbs");
                Directory.CreateDirectory(dir);
                foreach (string old in Directory.GetFiles(dir)) try { if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-7)) File.Delete(old); } catch { }
                string f = Path.Combine(dir, l.Conn.Id + "-" + Math.Abs(now.Meta.Thumb.GetHashCode()) + ".png");
                File.WriteAllBytes(f, b);
                return f;
            }
            catch { return null; }
        }

        // =================================================================
        // Formatting
        // =================================================================
        public static string Short(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            int i = file.LastIndexOf('/');
            string n = i >= 0 ? file.Substring(i + 1) : file;
            return n.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 6) : n;
        }

        static string FirstLine(string s)
        {
            s = (s ?? "").Trim();
            int i = s.IndexOf('\n');
            return i > 0 ? s.Substring(0, i).Trim() : s;
        }

        /// <summary>1h 12m / 12m / 45s.</summary>
        public static string Duration(double seconds)
        {
            if (seconds < 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m";
            return t.Seconds + "s";
        }

        /// <summary>Filament in metres (mm in).</summary>
        public static string Filament(double mm)
        {
            return mm >= 1000 ? (mm / 1000).ToString("0.0", CultureInfo.CurrentCulture) + " m" : Math.Round(mm) + " mm";
        }

        // =================================================================
        // Reading on demand
        // =================================================================
        static Link L(string id)
        {
            lock (Gate) { Link l; return Links.TryGetValue(id ?? "", out l) ? l : null; }
        }

        /// <summary>The print files (newest first), read again when older than half a minute (or force).</summary>
        public static async Task<List<PrFile>> FilesAsync(string id, bool force)
        {
            Link l = L(id);
            if (l == null) return new List<PrFile>();
            if (!force && l.Files != null && (DateTime.UtcNow - l.FilesAt).TotalSeconds < 30) return l.Files;
            var list = new List<PrFile>();
            string r = await Get(l, "/server/files/list?root=gcodes");
            using (JsonDocument d = JsonDocument.Parse(r))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        string p = Str(e, "path");
                        if (p.Length == 0) p = Str(e, "filename");
                        if (p.Length == 0) continue;
                        list.Add(new PrFile { Path = p, Modified = Num(e, "modified"), Size = (long)Num(e, "size") });
                    }
            list.Sort(delegate (PrFile a, PrFile b) { return b.Modified.CompareTo(a.Modified); });
            l.Files = list;
            l.FilesAt = DateTime.UtcNow;
            return list;
        }

        /// <summary>A file's slicer details (cached).</summary>
        public static Task<PrMeta> MetaAsync(string id, string file)
        {
            Link l = L(id);
            return l == null ? Task.FromResult(new PrMeta()) : Meta(l, file);
        }

        /// <summary>A thumbnail's picture (path under gcodes), cached; null when there's none.</summary>
        public static async Task<byte[]> ThumbAsync(string id, string thumbPath)
        {
            if (string.IsNullOrEmpty(thumbPath)) return null;
            string key = id + "|" + thumbPath;
            lock (Thumbs) { byte[] b; if (Thumbs.TryGetValue(key, out b)) return b; }
            Link l = L(id);
            if (l == null) return null;
            byte[] data = null;
            try { data = await Http(l).GetByteArrayAsync(Base(l.Conn) + "/server/files/gcodes/" + PathUrl(thumbPath)); }
            catch { }
            lock (Thumbs)
            {
                if (Thumbs.Count > 300) Thumbs.Clear();
                Thumbs[key] = data;
            }
            return data;
        }

        /// <summary>Why the last snapshot failed ("" = it worked).</summary>
        public static string SnapshotError(string id) { Link l = L(id); return l == null ? "" : l.SnapErr; }

        /// <summary>
        /// A webcam picture (JPEG), or null: the snapshot address, else the first frame of the MJPEG stream. Why it failed is logged (once per
        /// reason) and kept for the panel.
        /// </summary>
        public static async Task<byte[]> SnapshotAsync(string id, PrCam cam)
        {
            Link l = L(id);
            if (l == null || cam == null) return null;
            string err = "";
            if (cam.Snapshot.Length > 0)
                try
                {
                    using (HttpResponseMessage r = await GetFollow(l, cam.Snapshot + (cam.Snapshot.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks, HttpCompletionOption.ResponseContentRead, CancellationToken.None))
                    {
                        byte[] b = await r.Content.ReadAsByteArrayAsync();
                        if (r.IsSuccessStatusCode && IsJpegOrPng(b)) { SnapOk(l); return b; }
                        err = "snapshot " + cam.Snapshot + " answered " + (int)r.StatusCode + (r.IsSuccessStatusCode ? " but not with a picture (" + (r.Content.Headers.ContentType != null ? r.Content.Headers.ContentType.MediaType : "?") + ")" : "");
                    }
                }
                catch (Exception ex) { err = "snapshot " + cam.Snapshot + ": " + (ex is TaskCanceledException ? "no answer" : ex.InnerException != null ? ex.InnerException.Message : ex.Message); }
            // the first frame of the MJPEG stream
            if (cam.Stream.Length > 0 && cam.Stream.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                try
                {
                    byte[] f = await FirstFrame(l, cam.Stream);
                    if (f != null) { SnapOk(l); return f; }
                    err += (err.Length > 0 ? "; " : "") + "stream " + cam.Stream + " gave no JPEG frame (WebRTC / HLS streams can't be shown)";
                }
                catch (Exception ex) { err += (err.Length > 0 ? "; " : "") + "stream " + cam.Stream + ": " + (ex is TaskCanceledException || ex is OperationCanceledException ? "no answer" : ex.InnerException != null ? ex.InnerException.Message : ex.Message); }
            if (err.Length == 0) err = "the webcam has no snapshot or stream address";
            if (l.SnapErr != err) { l.SnapErr = err; Log.Write("Printer " + l.Conn.Shown + " webcam: " + err); }
            return null;
        }

        /// <summary>
        /// GET, following redirects itself: .NET won't follow one from https to http, which a web server behind a reverse proxy often sends
        /// (/webcam?action=… -> http://host/webcam/?action=…). A redirect to http on the same host is followed as https.
        /// </summary>
        static async Task<HttpResponseMessage> GetFollow(Link l, string url, HttpCompletionOption how, CancellationToken ct)
        {
            var at = new Uri(url);
            for (int hop = 0; ; hop++)
            {
                HttpResponseMessage r = await Http(l).GetAsync(at, how, ct);
                int code = (int)r.StatusCode;
                if (hop >= 4 || (code != 301 && code != 302 && code != 303 && code != 307 && code != 308) || r.Headers.Location == null) return r;
                Uri next = r.Headers.Location.IsAbsoluteUri ? r.Headers.Location : new Uri(at, r.Headers.Location);
                if (at.Scheme == "https" && next.Scheme == "http" && string.Equals(at.Host, next.Host, StringComparison.OrdinalIgnoreCase))
                    next = new UriBuilder(next) { Scheme = "https", Port = at.Port }.Uri;
                r.Dispose();
                at = next;
            }
        }

        static void SnapOk(Link l)
        {
            if (l.SnapErr.Length > 0) { l.SnapErr = ""; Log.Write("Printer " + l.Conn.Shown + " webcam: pictures coming through"); }
        }

        static bool IsJpegOrPng(byte[] b)
        {
            return b != null && b.Length > 100 && ((b[0] == 0xFF && b[1] == 0xD8) || (b[0] == 0x89 && b[1] == 0x50));
        }

        /// <summary>
        /// Read the webcam's MJPEG stream until cancelled, handing each whole JPEG frame to onFrame (on this background thread). Returns
        /// when the stream ends; throws when it can't be opened or isn't MJPEG (no frame in the first 5 seconds).
        /// </summary>
        public static async Task StreamAsync(string id, PrCam cam, Action<byte[]> onFrame, CancellationToken ct)
        {
            Link l = L(id);
            if (l == null || cam == null || !cam.Stream.StartsWith("http", StringComparison.OrdinalIgnoreCase)) throw new Exception("no MJPEG stream address");
            using (HttpResponseMessage r = await GetFollow(l, cam.Stream, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!r.IsSuccessStatusCode) throw new Exception("the stream answered " + (int)r.StatusCode);
                using (Stream s = await r.Content.ReadAsStreamAsync())
                {
                    var buf = new byte[1 << 20];
                    int len = 0, start = -1, scan = 0;
                    var chunk = new byte[32768];
                    DateTime opened = DateTime.UtcNow;
                    bool any = false;
                    while (!ct.IsCancellationRequested)
                    {
                        // no frame within 5 seconds: not an MJPEG stream (WebRTC / HLS ...)
                        Task<int> read = s.ReadAsync(chunk, 0, chunk.Length, ct);
                        if (!any && await Task.WhenAny(read, Task.Delay(5000, ct)) != read) throw new Exception("no MJPEG frames from " + cam.Stream);
                        int n = await read;
                        if (n <= 0) return;
                        if (len + n > buf.Length)
                        {
                            if (start > 0) { Buffer.BlockCopy(buf, start, buf, 0, len - start); len -= start; scan -= start; start = 0; }
                            if (len + n > buf.Length) { len = 0; start = -1; scan = 0; }        // a frame bigger than 1 MB: start over
                        }
                        Buffer.BlockCopy(chunk, 0, buf, len, n);
                        len += n;
                        for (int i = Math.Max(0, scan); i < len - 1; i++)
                        {
                            if (start < 0) { if (buf[i] == 0xFF && buf[i + 1] == 0xD8) { start = i; i++; } continue; }
                            if (buf[i] == 0xFF && buf[i + 1] == 0xD9)
                            {
                                var frame = new byte[i + 2 - start];
                                Buffer.BlockCopy(buf, start, frame, 0, frame.Length);
                                any = true;
                                try { onFrame(frame); } catch { }
                                // keep what came after this frame
                                int rest = len - (i + 2);
                                Buffer.BlockCopy(buf, i + 2, buf, 0, rest);
                                len = rest; start = -1; i = -1;
                            }
                        }
                        scan = Math.Max(0, len - 1);
                    }
                }
            }
        }

        /// <summary>Read an MJPEG stream until one whole JPEG (FF D8 … FF D9) has come, at most 4 seconds / 3 MB.</summary>
        static async Task<byte[]> FirstFrame(Link l, string url)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            using (HttpResponseMessage r = await GetFollow(l, url, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            {
                if (!r.IsSuccessStatusCode) throw new Exception("answered " + (int)r.StatusCode);
                using (Stream s = await r.Content.ReadAsStreamAsync())
                {
                    var buf = new MemoryStream();
                    var chunk = new byte[16384];
                    int start = -1;
                    while (buf.Length < 3000000)
                    {
                        int n = await s.ReadAsync(chunk, 0, chunk.Length, cts.Token);
                        if (n <= 0) break;
                        long from = Math.Max(0, buf.Length - 1);
                        buf.Write(chunk, 0, n);
                        byte[] all = buf.GetBuffer();
                        int len = (int)buf.Length;
                        for (int i = (int)from; i < len - 1; i++)
                        {
                            if (start < 0 && all[i] == 0xFF && all[i + 1] == 0xD8) { start = i; i++; continue; }
                            if (start >= 0 && all[i] == 0xFF && all[i + 1] == 0xD9)
                            {
                                var frame = new byte[i + 2 - start];
                                Buffer.BlockCopy(all, start, frame, 0, frame.Length);
                                return frame;
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>Spoolman's spools (not archived), for "Change spool".</summary>
        public static async Task<List<PrSpool>> SpoolsAsync(string id)
        {
            var list = new List<PrSpool>();
            Link l = L(id);
            if (l == null) return list;
            string r = await Post(l, "/server/spoolman/proxy", "{\"request_method\":\"GET\",\"path\":\"/v1/spool\",\"query\":\"allow_archived=false\"}");
            using (JsonDocument d = JsonDocument.Parse(r))
            {
                JsonElement e = d.RootElement;
                JsonElement inner = Obj(e, "response");
                if (inner.ValueKind == JsonValueKind.Array) e = inner;
                if (e.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement sp in e.EnumerateArray())
                        if (!Bool(sp, "archived")) list.Add(ParseSpool(sp));
            }
            list.Sort(delegate (PrSpool a, PrSpool b) { return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        // =================================================================
        // Actions - each returns "" or a one-line error
        // =================================================================
        static async Task<string> Do(string id, Func<Link, Task> act)
        {
            Link l = L(id);
            if (l == null) return "That printer isn't set up.";
            try
            {
                await act(l);
                l.LastPoll = DateTime.MinValue;      // read it again straight away
                l.LastExtras = DateTime.MinValue;
                return "";
            }
            catch (Exception ex)
            {
                string msg = ex is TaskCanceledException ? "no answer (timed out)" : ex.Message;
                Log.Write("Printer " + l.Conn.Shown + ": " + msg);
                return msg;
            }
        }

        public static Task<string> PauseAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/printer/print/pause"); }); }
        public static Task<string> ResumeAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/printer/print/resume"); }); }
        public static Task<string> CancelAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/printer/print/cancel"); }); }
        public static Task<string> EmergencyStopAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/printer/emergency_stop"); }); }
        public static Task<string> FirmwareRestartAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/printer/firmware_restart"); }); }
        public static Task<string> KlipperRestartAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/printer/restart"); }); }
        public static Task<string> StartPrintAsync(string id, string file) { return Do(id, delegate (Link l) { return Post(l, "/printer/print/start?filename=" + Q(file)); }); }

        /// <summary>Run G-code (a macro name, M104 S200 ...).</summary>
        public static Task<string> GcodeAsync(string id, string script) { return Do(id, delegate (Link l) { return Post(l, "/printer/gcode/script?script=" + Q(script)); }); }

        public static Task<string> SetTempAsync(string id, string heater, double target)
        {
            return GcodeAsync(id, "SET_HEATER_TEMPERATURE HEATER=" + (heater.Contains(" ") ? heater.Substring(heater.IndexOf(' ') + 1) : heater) +
                                  " TARGET=" + Math.Max(0, Math.Round(target)).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Nozzle and bed to these temperatures (a preset); 0 leaves that one alone.</summary>
        public static async Task<string> PreheatAsync(string id, double nozzle, double bed)
        {
            string err = "";
            if (nozzle > 0) err = await SetTempAsync(id, "extruder", nozzle);
            if (err.Length == 0 && bed > 0) err = await SetTempAsync(id, "heater_bed", bed);
            return err;
        }

        public static Task<string> CoolDownAsync(string id) { return GcodeAsync(id, "TURN_OFF_HEATERS"); }

        /// <summary>A light on (full / white) or off.</summary>
        public static Task<string> LightAsync(string id, PtLight light, bool on)
        {
            if (light.IsPin) return GcodeAsync(id, "SET_PIN PIN=" + light.Name + " VALUE=" + (on ? "1" : "0"));
            string v = on ? "1" : "0";
            return GcodeAsync(id, "SET_LED LED=" + light.Name + " RED=" + v + " GREEN=" + v + " BLUE=" + v + " WHITE=" + v);
        }

        public static Task<string> PowerAsync(string id, string device, bool on)
        {
            return Do(id, delegate (Link l) { return Post(l, "/machine/device_power/device?device=" + Q(device) + "&action=" + (on ? "on" : "off")); });
        }

        public static Task<string> SetSpoolAsync(string id, int spoolId)
        {
            return Do(id, delegate (Link l) { return Post(l, "/server/spoolman/spool_id", spoolId > 0 ? "{\"spool_id\":" + spoolId + "}" : "{}"); });
        }

        public static Task<string> QueueAddAsync(string id, string file) { return Do(id, delegate (Link l) { return Post(l, "/server/job_queue/job?filenames=" + Q(file)); }); }
        public static Task<string> QueueRemoveAsync(string id, string jobId) { return Do(id, delegate (Link l) { return Call(l, HttpMethod.Delete, "/server/job_queue/job?job_ids=" + Q(jobId)); }); }
        public static Task<string> QueueStartAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/server/job_queue/start"); }); }
        public static Task<string> QueuePauseAsync(string id) { return Do(id, delegate (Link l) { return Post(l, "/server/job_queue/pause"); }); }

        /// <summary>The last lines Klipper printed (the console), newest last.</summary>
        public static async Task<List<string>> ConsoleAsync(string id, int count)
        {
            var list = new List<string>();
            Link l = L(id);
            if (l == null) return list;
            try
            {
                string r = await Get(l, "/server/gcode_store?count=" + count);
                using (JsonDocument d = JsonDocument.Parse(r))
                {
                    JsonElement st = Obj(d.RootElement, "gcode_store");
                    if (st.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in st.EnumerateArray())
                        {
                            string msg = Str(e, "message");
                            string type = Str(e, "type");
                            if (msg.Length > 0) list.Add((type == "command" ? "> " : "") + msg);
                        }
                }
            }
            catch { }
            return list;
        }

        // =================================================================
        // Test (Plugins page)
        // =================================================================
        public static async Task<KeyValuePair<bool, string>> TestAsync(PrinterConn c)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Url)) return new KeyValuePair<bool, string>(false, "Fill in the address first.");
            var l = new Link { Conn = c };
            try
            {
                string info = await Get(l, "/server/info");
                string klippy, ver;
                using (JsonDocument d = JsonDocument.Parse(info))
                {
                    klippy = Str(d.RootElement, "klippy_state");
                    ver = Str(d.RootElement, "moonraker_version");
                }
                var extras = new List<string>();
                try { await Get(l, "/server/spoolman/spool_id"); extras.Add("Spoolman"); } catch { }
                try
                {
                    string r = await Get(l, "/machine/device_power/devices");
                    int n = 0;
                    using (JsonDocument d = JsonDocument.Parse(r)) { JsonElement a = Obj(d.RootElement, "devices"); if (a.ValueKind == JsonValueKind.Array) n = a.GetArrayLength(); }
                    if (n > 0) extras.Add(n + " power device" + (n > 1 ? "s" : ""));
                }
                catch { }
                try
                {
                    string r = await Get(l, "/server/webcams/list");
                    int n = 0;
                    using (JsonDocument d = JsonDocument.Parse(r)) { JsonElement a = Obj(d.RootElement, "webcams"); if (a.ValueKind == JsonValueKind.Array) n = a.GetArrayLength(); }
                    if (n > 0) extras.Add(n + " webcam" + (n > 1 ? "s" : ""));
                }
                catch { }
                try { await Get(l, "/server/job_queue/status"); extras.Add("the job queue"); } catch { }
                return new KeyValuePair<bool, string>(true, "Connected to Moonraker" + (ver.Length > 0 ? " " + ver : "") + " - Klipper is " + (klippy.Length > 0 ? klippy : "not connected") +
                                                             (extras.Count > 0 ? ". Found " + string.Join(", ", extras) + "." : "."));
            }
            catch (Exception ex)
            {
                string msg = ex is TaskCanceledException ? "no answer (timed out) - check the address" : ex is HttpRequestException && ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new KeyValuePair<bool, string>(false, msg);
            }
            finally { if (l.Http != null) try { l.Http.Dispose(); } catch { } }
        }

        /// <summary>The temperature presets ("PLA 210/60, PETG 240/80"): name, nozzle, bed.</summary>
        public static List<Tuple<string, double, double>> Presets(BarSettings s)
        {
            var list = new List<Tuple<string, double, double>>();
            foreach (string part in (s == null ? "" : PtSettings.Of(s).Presets).Split(','))
            {
                string p = part.Trim();
                int sp = p.LastIndexOf(' ');
                if (sp <= 0) continue;
                string name = p.Substring(0, sp).Trim();
                string[] t = p.Substring(sp + 1).Split('/');
                double n, b = 0;
                if (!double.TryParse(t[0], NumberStyles.Float, CultureInfo.InvariantCulture, out n)) continue;
                if (t.Length > 1) double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b);
                list.Add(Tuple.Create(name, n, b));
            }
            return list;
        }
    }
}
