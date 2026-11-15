using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The Tdarr connection and notifications - kept in vShell's settings under Integrations["tdarr"].</summary>
    public sealed class TdSettings
    {
        readonly SettingsBag _b;
        public TdSettings(BarSettings s) { _b = s.Integration("tdarr"); }
        public static TdSettings Of(BarSettings s) { return new TdSettings(s); }

        /// <summary>The server's address, such as http://192.168.1.20:8265 (the web UI's address).</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>API key - only needed when Tdarr has authentication on (Tools > API Keys). Stored encrypted (ApiKeyPlain is the real one).</summary>
        public string ApiKey { get { return _b.Get("ApiKey", ""); } set { _b.Set("ApiKey", Secret.Protect(value ?? "")); } }
        public string ApiKeyPlain { get { return _b.GetSecret("ApiKey"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>The run box keyword (without the !).</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "td").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "td" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }

        /// <summary>Notices: new transcode / health check errors.</summary>
        public bool NotifyErrors { get { return _b.Get("NotifyErrors", true); } set { _b.Set("NotifyErrors", value); } }
        /// <summary>Notices: a node disconnected / came back.</summary>
        public bool NotifyNodes { get { return _b.Get("NotifyNodes", true); } set { _b.Set("NotifyNodes", value); } }
        /// <summary>Notices: Tdarr worked through its whole queue.</summary>
        public bool NotifyDone { get { return _b.Get("NotifyDone", false); } set { _b.Set("NotifyDone", value); } }
        /// <summary>Notices: once a day (the first look after 9 AM) what Tdarr did since the day before.</summary>
        public bool NotifySummary { get { return _b.Get("NotifySummary", false); } set { _b.Set("NotifySummary", value); } }

        public bool AnyNotices { get { return NotifyErrors || NotifyNodes || NotifyDone || NotifySummary; } }
    }

    /// <summary>A Tdarr module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class TdView
    {
        readonly SettingsBag _b;
        public TdView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static TdView Of(ModuleConfig cfg) { return new TdView(cfg); }

        public bool ShowWorkers { get { return _b.Get("ShowWorkers", true); } set { _b.Set("ShowWorkers", value); } }
        public bool ShowNodes { get { return _b.Get("ShowNodes", true); } set { _b.Set("ShowNodes", value); } }
        public bool ShowStats { get { return _b.Get("ShowStats", true); } set { _b.Set("ShowStats", value); } }
        /// <summary>The files that failed (with Requeue).</summary>
        public bool ShowFailed { get { return _b.Get("ShowFailed", true); } set { _b.Set("ShowFailed", value); } }
        /// <summary>How many running jobs the list shows.</summary>
        public int Count { get { return Math.Max(1, Math.Min(20, _b.Get("Count", 6))); } set { _b.Set("Count", value); } }
        /// <summary>Bar: the number of jobs running as a badge on the icon.</summary>
        public bool Badge { get { return _b.Get("Badge", true); } set { _b.Set("Badge", value); } }
        /// <summary>Bar: how many files are waiting.</summary>
        public bool BarQueue { get { return _b.Get("BarQueue", true); } set { _b.Set("BarQueue", value); } }
        /// <summary>Bar: hidden while nothing is running (and Tdarr is fine).</summary>
        public bool HideIdle { get { return _b.Get("HideIdle", false); } set { _b.Set("HideIdle", value); } }
        /// <summary>The "Tdarr" button (opens Tdarr) at the top.</summary>
        public bool ShowOpen { get { return _b.Get("ShowOpen", true); } set { _b.Set("ShowOpen", value); } }
    }

    /// <summary>A job running on a node (a worker that isn't idle).</summary>
    public sealed class TdarrWorker
    {
        public string Id = "", NodeId = "", NodeName = "";
        /// <summary>transcodecpu, transcodegpu, healthcheckcpu, healthcheckgpu</summary>
        public string Type = "";
        public string File = "";
        public string Status = "";
        public string Eta = "";
        public double Percent, Fps, SizeGb, EstGb;

        public string Name { get { try { return System.IO.Path.GetFileName(File); } catch { return File; } } }
        public bool Transcode { get { return Type.StartsWith("transcode"); } }

        /// <summary>"Transcode GPU", "Health check CPU".</summary>
        public string TypeText { get { return TdarrClient.TypeText(Type); } }
    }

    /// <summary>A node (a machine that runs Tdarr's workers).</summary>
    public sealed class TdarrNode
    {
        public string Id = "", Name = "";
        public bool Paused;
        /// <summary>Worker limits by type (transcodecpu …).</summary>
        public Dictionary<string, int> Limits = new Dictionary<string, int>();
        public string Cpu = "", MemUsed = "", MemTotal = "";
        public int Running;

        /// <summary>"Transcode 2 CPU, 1 GPU · Health check 1 CPU" (the limits that aren't 0).</summary>
        public string LimitsText
        {
            get
            {
                Func<string, string, string> part = delegate (string what, string kind)
                {
                    int c, g;
                    Limits.TryGetValue(kind + "cpu", out c);
                    Limits.TryGetValue(kind + "gpu", out g);
                    var l = new List<string>();
                    if (c > 0) l.Add(c + " CPU");
                    if (g > 0) l.Add(g + " GPU");
                    return l.Count == 0 ? "" : what + " " + string.Join(", ", l);
                };
                string t = part("Transcode", "transcode"), h = part("Health check", "healthcheck");
                if (t.Length == 0 && h.Length == 0) return "no workers";
                return t.Length == 0 ? h : h.Length == 0 ? t : t + " · " + h;
            }
        }
    }

    /// <summary>A file in one of Tdarr's error tables.</summary>
    public sealed class TdarrFailed
    {
        /// <summary>Tdarr's id for it (the file's path).</summary>
        public string Id = "";
        /// <summary>true = the health check failed (table 6); false = the transcode failed or was cancelled (table 3).</summary>
        public bool Health;
        public double SizeGb;

        public string Name { get { int i = Math.Max(Id.LastIndexOf('/'), Id.LastIndexOf('\\')); return i >= 0 ? Id.Substring(i + 1) : Id; } }
        public string What { get { return Health ? "health check" : "transcode"; } }
    }

    /// <summary>The library totals (Tdarr's statistics).</summary>
    public sealed class TdarrStats
    {
        public int TranscodeQueue, TranscodeDone, TranscodeErrors, HealthQueue, HealthDone, HealthErrors, Files;
        /// <summary>Space saved by transcoding, in GB.</summary>
        public double SavedGb;
        public int Queue { get { return TranscodeQueue + HealthQueue; } }
        public int Errors { get { return TranscodeErrors + HealthErrors; } }
    }

    public sealed class TdarrSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public string Version = "";
        public List<TdarrNode> Nodes = new List<TdarrNode>();
        /// <summary>The jobs running now (most done first).</summary>
        public List<TdarrWorker> Workers = new List<TdarrWorker>();
        public TdarrStats Stats = new TdarrStats();
        /// <summary>Why the statistics couldn't be read ("" = they could).</summary>
        public string StatsError = "";
        /// <summary>Files that failed (transcode errors first, then health check errors) - only read while something shows Tdarr.</summary>
        public List<TdarrFailed> Failed = new List<TdarrFailed>();
        /// <summary>Why the failed files couldn't be read ("" = they could).</summary>
        public string FailedError = "";
        public bool Ok { get { return Fetched != DateTime.MinValue; } }
    }

    /// <summary>
    /// Talks to the Tdarr server (API v2). Polled in the background: every 5 s while a Tdarr module (or the run box) shows it, every
    /// minute otherwise when notices are on. An API key is only sent when one is set (Tdarr with authentication on).
    /// </summary>
    public static class TdarrClient
    {
        public static readonly string[] Types = { "transcodecpu", "transcodegpu", "healthcheckcpu", "healthcheckgpu" };

        static HttpClient _http;
        static string _key = "", _base = "";
        static bool _ignoreCert;
        static readonly object HttpGate = new object();

        static readonly object Gate = new object();
        static TdarrSnapshot _snap = new TdarrSnapshot();
        static Timer _timer;
        static BarSettings _s;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue, _lastPoll = DateTime.MinValue, _retry = DateTime.MinValue;
        static string _configuredFor;
        /// <summary>For the notices: the errors / queue last seen (-1 = not yet), the nodes seen and the ones gone for how many polls.</summary>
        static int _lastErrors = -1, _lastQueue = -1;
        static Dictionary<string, string> _nodesSeen;
        static readonly Dictionary<string, int> _gone = new Dictionary<string, int>();

        /// <summary>A notice: (kind key, title line, detail, Tdarr address).</summary>
        public static event Action<string, string, string, string> Notice;
        public static event Action Changed;

        public static string TypeText(string type)
        {
            switch (type)
            {
                case "transcodecpu": return "Transcode CPU";
                case "transcodegpu": return "Transcode GPU";
                case "healthcheckcpu": return "Health check CPU";
                case "healthcheckgpu": return "Health check GPU";
                default: return type;
            }
        }

        static HttpClient Http(BarSettings s)
        {
            lock (HttpGate)
            {
                TdSettings c = TdSettings.Of(s);
                string b = Base(s);
                string k = c.ApiKeyPlain;
                bool ignoreCert = c.IgnoreCert;
                if (_http == null || b != _base || k != _key || ignoreCert != _ignoreCert)
                {
                    var handler = new HttpClientHandler();
                    if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                    var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
                    if (k.Length > 0) h.DefaultRequestHeaders.Add("x-api-key", k);
                    h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                    var old = _http;
                    _http = h; _base = b; _key = k; _ignoreCert = ignoreCert;
                    if (old != null) try { old.Dispose(); } catch { }
                }
                return _http;
            }
        }

        static string Base(BarSettings s)
        {
            string b = TdSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        /// <summary>Set up = an address (the API key is optional).</summary>
        public static bool Configured(BarSettings s)
        {
            return s != null && !string.IsNullOrWhiteSpace(TdSettings.Of(s).Url);
        }

        static string Api(BarSettings s, string path) { Http(s); return _base + "/api/v2" + path; }

        /// <summary>Tdarr's web UI.</summary>
        public static string PageUrl(BarSettings s) { return Base(s) + "/"; }

        // =================================================================
        // Polling
        // =================================================================
        public static void Configure(BarSettings s)
        {
            _s = s;
            TdSettings c = TdSettings.Of(s);
            string id = c.Url.Trim() + "|" + c.ApiKey;
            if (_configuredFor != null && _configuredFor != id)
            {
                // another server / key: start over (no notices for what the new one already has)
                lock (Gate) _snap = new TdarrSnapshot();
                Forget();
            }
            _configuredFor = id;
            lock (Gate)
                if (_timer == null && Configured(s)) _timer = new Timer(delegate { Tick(); }, null, 900, 2500);
            Poke();
        }

        static void Forget()
        {
            _lastErrors = _lastQueue = -1;
            _sumFrom = DateTime.MinValue;
            _failedAt = DateTime.MinValue;
            _nodesSeen = null;
            lock (_gone) _gone.Clear();
        }

        /// <summary>The plugin was removed: stop polling and forget what was read (Configure starts it again).</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                _snap = new TdarrSnapshot();
            }
            _s = null;
            _configuredFor = null;
            Forget();
        }

        /// <summary>The latest snapshot (asking for it keeps the 5-second polling going).</summary>
        public static TdarrSnapshot Latest
        {
            get
            {
                bool was = (DateTime.UtcNow - _wanted).TotalSeconds < 30;
                _wanted = DateTime.UtcNow;
                if (_timer == null && _s != null && Configured(_s)) Configure(_s);
                else if (!was) Poke();
                lock (Gate) return _snap;
            }
        }

        public static void Poke()
        {
            _lastPoll = DateTime.MinValue;
            _retry = DateTime.MinValue;
            Timer t = _timer;
            if (t != null) try { t.Change(300, 2500); } catch { }
        }

        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null || !Configured(s)) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 30;
                if (!shown && !TdSettings.Of(s).AnyNotices) return;
                if (DateTime.UtcNow < _retry) return;
                if ((DateTime.UtcNow - _lastPoll).TotalSeconds < (shown ? 4.5 : 60)) return;
                _lastPoll = DateTime.UtcNow;
                TdarrSnapshot old;
                lock (Gate) old = _snap;
                TdarrSnapshot snap;
                try { snap = ReadAll(s, old); }
                catch (Exception ex)
                {
                    string msg = Why(ex);
                    if (old.Error != msg) Log.Write("Tdarr: " + msg);
                    var keep = new TdarrSnapshot
                    {
                        Error = msg, Fetched = old.Fetched, Version = old.Version, Nodes = old.Nodes, Workers = old.Workers, Stats = old.Stats, StatsError = old.StatsError,
                        Failed = old.Failed, FailedError = old.FailedError
                    };
                    _retry = DateTime.UtcNow.AddSeconds(20);
                    lock (Gate) _snap = keep;
                    Fire(Changed);
                    return;
                }
                lock (Gate) _snap = snap;
                Compare(snap, s);
                Fire(Changed);
            }
            catch (Exception ex) { Log.Write("Tdarr: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static string Why(Exception ex)
        {
            if (ex is TaskCanceledException) return "Tdarr didn't answer in time";
            if (ex is HttpRequestException && ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        static string Get(BarSettings s, string path)
        {
            HttpClient h = Http(s);
            using (HttpResponseMessage r = h.GetAsync(Api(s, path)).GetAwaiter().GetResult())
            {
                string json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
                return json;
            }
        }

        static string Post(BarSettings s, string path, string body)
        {
            HttpClient h = Http(s);
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage r = h.PostAsync(Api(s, path), content).GetAwaiter().GetResult())
            {
                string json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
                return json;
            }
        }

        const string StatsBody = "{\"data\":{\"collection\":\"StatisticsJSONDB\",\"mode\":\"getById\",\"docID\":\"statistics\",\"obj\":{}}}";

        static TdarrSnapshot ReadAll(BarSettings s, TdarrSnapshot old)
        {
            var snap = new TdarrSnapshot { Fetched = DateTime.UtcNow, Version = old.Version };
            if (snap.Version.Length == 0)
                try { using (JsonDocument d = JsonDocument.Parse(Get(s, "/status"))) snap.Version = Str(d.RootElement, "version"); } catch { }

            // the nodes (an object by node id), each with its workers (an object by worker id)
            using (JsonDocument d = JsonDocument.Parse(Get(s, "/get-nodes")))
                if (d.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (JsonProperty np in d.RootElement.EnumerateObject())
                    {
                        JsonElement n = np.Value;
                        if (n.ValueKind != JsonValueKind.Object) continue;
                        var node = new TdarrNode { Id = Str(n, "_id"), Name = Str(n, "nodeName"), Paused = Bool(n, "nodePaused") };
                        if (node.Id.Length == 0) node.Id = np.Name;
                        if (node.Name.Length == 0) node.Name = node.Id;
                        JsonElement lim = Obj(n, "workerLimits");
                        foreach (string t in Types) node.Limits[t] = (int)Num(lim, t);
                        JsonElement os = Obj(Obj(n, "resStats"), "os");
                        node.Cpu = Text(os, "cpuPerc"); node.MemUsed = Text(os, "memUsedGB"); node.MemTotal = Text(os, "memTotalGB");
                        JsonElement ws = Obj(n, "workers");
                        if (ws.ValueKind == JsonValueKind.Object)
                            foreach (JsonProperty wp in ws.EnumerateObject())
                            {
                                JsonElement w = wp.Value;
                                if (w.ValueKind != JsonValueKind.Object || Bool(w, "idle")) continue;
                                var wk = new TdarrWorker
                                {
                                    Id = Str(w, "_id"), NodeId = node.Id, NodeName = node.Name, Type = Str(w, "workerType"), File = Str(w, "file"), Status = Str(w, "status"),
                                    Eta = Str(w, "ETA"), Percent = Num(w, "percentage"), Fps = Num(w, "fps"), SizeGb = Num(w, "originalfileSizeInGbytes"), EstGb = Num(w, "estSize")
                                };
                                if (wk.Id.Length == 0) wk.Id = wp.Name;
                                if (wk.File.Length == 0) continue;     // a worker between files
                                if (double.IsNaN(wk.Percent)) wk.Percent = 0;
                                snap.Workers.Add(wk);
                                node.Running++;
                            }
                        snap.Nodes.Add(node);
                    }
            snap.Nodes.Sort(delegate (TdarrNode a, TdarrNode b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            snap.Workers.Sort(delegate (TdarrWorker a, TdarrWorker b) { return b.Percent.CompareTo(a.Percent); });

            try
            {
                using (JsonDocument d = JsonDocument.Parse(Post(s, "/cruddb", StatsBody)))
                {
                    JsonElement e = d.RootElement;
                    snap.Stats = new TdarrStats
                    {
                        TranscodeQueue = Count(e, 1), TranscodeDone = Count(e, 2), TranscodeErrors = Count(e, 3),
                        HealthQueue = Count(e, 4), HealthDone = Count(e, 5), HealthErrors = Count(e, 6),
                        Files = (int)Num(e, "totalFileCount"), SavedGb = Num(e, "sizeDiff")
                    };
                    if (double.IsNaN(snap.Stats.SavedGb)) snap.Stats.SavedGb = 0;
                }
            }
            catch (Exception ex) { snap.StatsError = Why(ex); snap.Stats = old.Stats; }

            // the failed files: only while something shows Tdarr, every 30 s (or straight after a requeue), and only when there are any
            snap.Failed = old.Failed; snap.FailedError = old.FailedError;
            bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 30;
            if (snap.Stats.Errors == 0 && snap.StatsError.Length == 0) { snap.Failed = new List<TdarrFailed>(); snap.FailedError = ""; }
            else if (shown && (DateTime.UtcNow - _failedAt).TotalSeconds >= 30)
            {
                _failedAt = DateTime.UtcNow;
                try
                {
                    var list = new List<TdarrFailed>();
                    if (snap.Stats.TranscodeErrors > 0 || snap.StatsError.Length > 0) list.AddRange(ReadFailed(s, 3, FailedShown));
                    if (snap.Stats.HealthErrors > 0 || snap.StatsError.Length > 0) list.AddRange(ReadFailed(s, 6, FailedShown));
                    snap.Failed = list;
                    snap.FailedError = "";
                }
                catch (Exception ex) { snap.FailedError = Why(ex); }
            }
            return snap;
        }

        /// <summary>How many failed files of each kind the list reads.</summary>
        const int FailedShown = 25;
        static DateTime _failedAt = DateTime.MinValue;

        /// <summary>
        /// A page of one of Tdarr's error tables (3 transcode errors / cancelled, 6 health check errors) - what its Staged / Error tabs show.
        /// </summary>
        static List<TdarrFailed> ReadFailed(BarSettings s, int table, int count)
        {
            string body = "{\"data\":{\"start\":0,\"pageSize\":" + count + ",\"filters\":[],\"sorts\":[],\"opts\":{\"table\":\"table" + table + "\"}}}";
            var list = new List<TdarrFailed>();
            using (JsonDocument d = JsonDocument.Parse(Post(s, "/client/files", body)))
            {
                JsonElement arr = d.RootElement;
                if (arr.ValueKind == JsonValueKind.Object)
                {
                    JsonElement a;
                    if (arr.TryGetProperty("array", out a) || arr.TryGetProperty("files", out a) || arr.TryGetProperty("data", out a)) arr = a;
                }
                if (arr.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement x in arr.EnumerateArray())
                {
                    string id = Str(x, "_id");
                    if (id.Length == 0) id = Str(x, "file");
                    if (id.Length == 0) continue;
                    double mb = Num(x, "file_size");
                    list.Add(new TdarrFailed { Id = id, Health = table == 6, SizeGb = double.IsNaN(mb) ? 0 : mb / 1000 });
                }
            }
            return list;
        }

        /// <summary>One of Tdarr's tables (1 transcode queue, 2 done, 3 errors, 4 health check queue, 5 healthy, 6 health check errors).</summary>
        static int Count(JsonElement e, int table)
        {
            double v = Num(e, "table" + table + "ViewableCount");
            if (double.IsNaN(v) || v <= 0) v = Num(e, "table" + table + "Count");
            return double.IsNaN(v) ? 0 : (int)v;
        }

        // ---- JSON ----
        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        /// <summary>A string or a number, as text.</summary>
        static string Text(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return "";
        }

        static double Num(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return double.NaN;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            double d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return double.NaN;
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
        }

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Object ? v : default(JsonElement);
        }

        // =================================================================
        // Notices
        // =================================================================
        static void Compare(TdarrSnapshot snap, BarSettings s)
        {
            Action<string, string, string, string> n = Notice;
            TdSettings c = TdSettings.Of(s);
            string url = PageUrl(s);

            // new errors (the error tables grew)
            if (snap.StatsError.Length == 0)
            {
                int errors = snap.Stats.Errors;
                if (_lastErrors >= 0 && errors > _lastErrors && c.NotifyErrors && n != null)
                {
                    int more = errors - _lastErrors;
                    try
                    {
                        n("Tdarr.Error", more == 1 ? "Tdarr: a file failed" : "Tdarr: " + more + " files failed",
                          "Transcode errors: " + snap.Stats.TranscodeErrors + " · health check errors: " + snap.Stats.HealthErrors, url);
                    }
                    catch { }
                }
                _lastErrors = errors;

                // the whole queue done (it had files, now none and nothing running)
                int queue = snap.Stats.Queue;
                if (_lastQueue > 0 && queue == 0 && snap.Workers.Count == 0 && c.NotifyDone && n != null)
                    try { n("Tdarr.Done", "Tdarr has finished its queue", snap.Stats.SavedGb > 0 ? InfoBytes(snap.Stats.SavedGb) + " saved so far" : "Nothing left to do", url); } catch { }
                if (queue > 0 || snap.Workers.Count == 0) _lastQueue = queue;
            }

            // nodes (get-nodes only lists connected ones): one missing for two polls in a row disconnected; one seen again after that is back
            var now = new Dictionary<string, string>();
            foreach (TdarrNode node in snap.Nodes) now[node.Id] = node.Name;
            bool say = _nodesSeen != null && c.NotifyNodes && n != null;
            if (_nodesSeen != null)
            {
                foreach (var kv in _nodesSeen)
                {
                    if (now.ContainsKey(kv.Key)) continue;
                    int polls;
                    lock (_gone) { _gone.TryGetValue(kv.Key, out polls); _gone[kv.Key] = ++polls; }
                    if (polls == 2 && say) try { n("Tdarr.Node", "Tdarr node disconnected: " + kv.Value, "Its workers stopped. vShell says when it's back.", url); } catch { }
                }
                foreach (var kv in now)
                {
                    int polls;
                    bool was;
                    lock (_gone) { was = _gone.TryGetValue(kv.Key, out polls); _gone.Remove(kv.Key); }
                    if (was && polls >= 2 && say) try { n("Tdarr.Node", "Tdarr node back: " + kv.Value, "Connected again", url); } catch { }
                }
            }
            else _nodesSeen = new Dictionary<string, string>();
            foreach (var kv in now) _nodesSeen[kv.Key] = kv.Value;     // every node seen this session (a removed one is said once)

            Summary(snap, c, n, url);
        }

        // ---- the daily summary: the totals when the day was started, said the next day after 9 AM ----
        static DateTime _sumFrom = DateTime.MinValue;       // local time the counting started (MinValue = not yet)
        static int _sumDone, _sumErrors;
        static double _sumSaved;

        static void Summary(TdarrSnapshot snap, TdSettings c, Action<string, string, string, string> n, string url)
        {
            if (snap.StatsError.Length > 0) return;
            TdarrStats st = snap.Stats;
            DateTime now = DateTime.Now;
            if (_sumFrom == DateTime.MinValue) { StartDay(st, now); return; }
            if (now < _sumFrom.Date.AddDays(1).AddHours(9)) return;
            int done = st.TranscodeDone + st.HealthDone - _sumDone, errors = st.Errors - _sumErrors;
            double saved = st.SavedGb - _sumSaved;
            DateTime from = _sumFrom;
            StartDay(st, now);
            if (!c.NotifySummary || n == null) return;
            var bits = new List<string>();
            bits.Add(done <= 0 ? "nothing finished" : done == 1 ? "1 file done" : done + " files done");
            if (Math.Abs(saved) >= 0.001) bits.Add(InfoBytes(Math.Abs(saved)) + (saved > 0 ? " saved" : " larger"));
            if (errors > 0) bits.Add(errors == 1 ? "1 failed" : errors + " failed");
            string since = (now - from).TotalHours < 30 ? "the last day" : "since " + TimeText.Stamp(from);
            try { n("Tdarr.Summary", "Tdarr: " + string.Join(", ", bits), "In " + since + " · " + st.Queue + " in the queue now", url); } catch { }
        }

        static void StartDay(TdarrStats st, DateTime now)
        {
            _sumFrom = now;
            _sumDone = st.TranscodeDone + st.HealthDone;
            _sumErrors = st.Errors;
            _sumSaved = st.SavedGb;
        }

        /// <summary>GB as text ("1.2 TB", "350 GB").</summary>
        public static string InfoBytes(double gb)
        {
            if (Math.Abs(gb) >= 1000) return (gb / 1000).ToString("0.##") + " TB";
            if (Math.Abs(gb) >= 1) return gb.ToString("0.#") + " GB";
            return (gb * 1000).ToString("0") + " MB";
        }

        // =================================================================
        // Actions
        // =================================================================
        static async Task<string> PostAsync(BarSettings s, string path, string body)
        {
            try
            {
                HttpClient h = Http(s);
                using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage r = await h.PostAsync(Api(s, path), content).ConfigureAwait(false))
                {
                    string json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return Fail(r, json);
                }
                Poke();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        static string J(string v) { return JsonSerializer.Serialize(v ?? ""); }

        /// <summary>Pause / resume a node (its running jobs finish; it takes no new ones while paused). Null when it worked.</summary>
        public static Task<string> PauseAsync(BarSettings s, TdarrNode node, bool pause)
        {
            return PostAsync(s, "/update-node", "{\"data\":{\"nodeID\":" + J(node.Id) + ",\"nodeUpdates\":{\"nodePaused\":" + (pause ? "true" : "false") + "}}}");
        }

        /// <summary>One more / one fewer worker of a type on a node. Null when it worked.</summary>
        public static Task<string> LimitAsync(BarSettings s, TdarrNode node, string type, bool more)
        {
            return PostAsync(s, "/alter-worker-limit", "{\"data\":{\"nodeID\":" + J(node.Id) + ",\"process\":\"" + (more ? "increase" : "decrease") + "\",\"workerType\":" + J(type) + "}}");
        }

        /// <summary>Stop a running job (the file goes back to Tdarr's queue tables as cancelled). Null when it worked.</summary>
        public static Task<string> KillAsync(BarSettings s, TdarrWorker w)
        {
            return PostAsync(s, "/kill-worker", "{\"data\":{\"nodeID\":" + J(w.NodeId) + ",\"workerID\":" + J(w.Id) + "}}");
        }

        /// <summary>Put a failed file back in its queue (transcode or health check). Null when it worked.</summary>
        public static async Task<string> RequeueAsync(BarSettings s, TdarrFailed f)
        {
            string err = await PostAsync(s, "/cruddb", RequeueBody(f));
            if (err == null) Requeued(new[] { f });
            return err;
        }

        static string RequeueBody(TdarrFailed f)
        {
            string field = f.Health ? "HealthCheck" : "TranscodeDecisionMaker";
            return "{\"data\":{\"collection\":\"FileJSONDB\",\"mode\":\"update\",\"docID\":" + J(f.Id) + ",\"obj\":{\"" + field + "\":\"Queued\"}}}";
        }

        /// <summary>
        /// Put every failed file back in its queue (up to 500 of each kind). Tdarr's bulk update takes them at once; a Tdarr without it gets
        /// them one by one. Returns (how many, null) or (0, why not).
        /// </summary>
        public static Task<KeyValuePair<int, string>> RequeueAllAsync(BarSettings s)
        {
            return Task.Run(async delegate
            {
                try
                {
                    var all = new List<TdarrFailed>();
                    all.AddRange(ReadFailed(s, 3, 500));
                    all.AddRange(ReadFailed(s, 6, 500));
                    if (all.Count == 0) return new KeyValuePair<int, string>(0, null);
                    foreach (bool health in new[] { false, true })
                    {
                        List<TdarrFailed> part = all.FindAll(delegate (TdarrFailed f) { return f.Health == health; });
                        if (part.Count == 0) continue;
                        string paths = "[" + string.Join(",", part.ConvertAll(delegate (TdarrFailed f) { return J(f.Id); })) + "]";
                        string field = health ? "HealthCheck" : "TranscodeDecisionMaker";
                        string err = await PostAsync(s, "/bulk-update-files", "{\"data\":{\"filePaths\":" + paths + ",\"updatedObj\":{\"" + field + "\":\"Queued\"}}}");
                        if (err == null) continue;
                        Log.Write("Tdarr bulk requeue: " + err + " - one by one instead");
                        foreach (TdarrFailed f in part)
                        {
                            string e = await PostAsync(s, "/cruddb", RequeueBody(f));
                            if (e != null) return new KeyValuePair<int, string>(0, e);
                        }
                    }
                    Requeued(all);
                    return new KeyValuePair<int, string>(all.Count, null);
                }
                catch (Exception ex) { return new KeyValuePair<int, string>(0, Why(ex)); }
            });
        }

        /// <summary>Requeued files leave the list straight away (the next read of the error tables confirms it).</summary>
        static void Requeued(IEnumerable<TdarrFailed> files)
        {
            var ids = new HashSet<string>();
            foreach (TdarrFailed f in files) ids.Add(f.Id + "|" + f.Health);
            lock (Gate)
            {
                _snap = new TdarrSnapshot
                {
                    Fetched = _snap.Fetched, Error = _snap.Error, Version = _snap.Version, Nodes = _snap.Nodes, Workers = _snap.Workers, Stats = _snap.Stats, StatsError = _snap.StatsError,
                    Failed = _snap.Failed.FindAll(delegate (TdarrFailed f) { return !ids.Contains(f.Id + "|" + f.Health); }), FailedError = _snap.FailedError
                };
            }
            _failedAt = DateTime.MinValue;
            Fire(Changed);
        }

        /// <summary>Test connection: the version and nodes, or why it failed.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address first.");
                string ver = "";
                int nodes = 0;
                await Task.Run(delegate
                {
                    using (JsonDocument d = JsonDocument.Parse(Get(s, "/status"))) ver = Str(d.RootElement, "version");
                    using (JsonDocument d = JsonDocument.Parse(Get(s, "/get-nodes")))
                        if (d.RootElement.ValueKind == JsonValueKind.Object) foreach (JsonProperty p in d.RootElement.EnumerateObject()) nodes++;
                }).ConfigureAwait(false);
                Poke();
                return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - Tdarr " + ver : "") + ", " + (nodes == 1 ? "1 node" : nodes + " nodes") + ".");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, Why(ex)); }
        }

        static string Fail(HttpResponseMessage r, string body)
        {
            int code = (int)r.StatusCode;
            if (code == 401 || code == 403)
                return "Tdarr wants an API key (" + code + ") - make one in Tdarr under Tools > API Keys, or check the one entered";
            if (code == 404) return "Tdarr answered 404 - is the address the Tdarr server's (port 8265 by default)?";
            string msg = body != null && body.Length > 0 && body.Length < 200 && !body.TrimStart().StartsWith("<") ? ": " + body.Trim() : "";
            return "Tdarr answered " + code + msg;
        }
    }
}
