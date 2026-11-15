using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>One Home Assistant entity as last read.</summary>
    public sealed class HaState
    {
        public string EntityId = "";
        public string State = "";
        public string Unit = "";
        public string Name = "";
        public DateTime Fetched = DateTime.MinValue;
        /// <summary>The entity's attributes, each as its raw JSON (current_temperature, hvac_modes, percentage ...).</summary>
        public Dictionary<string, string> Attr = new Dictionary<string, string>();

        /// <summary>An attribute as text ("" = missing / null; lists and objects come back as JSON).</summary>
        public string AttrText(string name)
        {
            string raw;
            if (Attr == null || !Attr.TryGetValue(name, out raw) || raw == null) return "";
            try
            {
                using (JsonDocument d = JsonDocument.Parse(raw))
                {
                    JsonElement e = d.RootElement;
                    if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
                    if (e.ValueKind == JsonValueKind.Null) return "";
                    return e.GetRawText();
                }
            }
            catch { return ""; }
        }

        /// <summary>A number attribute (current_temperature, temperature, percentage ...).</summary>
        public bool AttrNum(string name, out double v)
        {
            v = 0;
            string raw;
            if (Attr == null || !Attr.TryGetValue(name, out raw) || raw == null) return false;
            raw = raw.Trim().Trim('"');
            return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        /// <summary>A list attribute (hvac_modes, fan_modes, preset_modes ...).</summary>
        public List<string> AttrList(string name)
        {
            var l = new List<string>();
            string raw;
            if (Attr == null || !Attr.TryGetValue(name, out raw) || raw == null) return l;
            try
            {
                using (JsonDocument d = JsonDocument.Parse(raw))
                    if (d.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in d.RootElement.EnumerateArray())
                            if (e.ValueKind == JsonValueKind.String) l.Add(e.GetString());
            }
            catch { }
            return l;
        }

        public bool Unavailable { get { return State == "unavailable" || State == "unknown" || State == ""; } }

        public bool TryNumber(out double v)
        {
            return double.TryParse(State, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }
    }

    /// <summary>
    /// Talks to Home Assistant's REST API with a long-lived access token. Modules just call Get(entityId) every second; the first call
    /// for an entity starts fetching it, and it is then polled in the background (every few seconds) for as long as somebody keeps asking.
    /// Nothing here blocks the UI thread.
    /// </summary>
    public static class HaClient
    {
        static readonly object Gate = new object();
        static readonly Dictionary<string, HaState> States = new Dictionary<string, HaState>();
        static readonly Dictionary<string, DateTime> Wanted = new Dictionary<string, DateTime>();
        static readonly Dictionary<string, DateTime> Fetching = new Dictionary<string, DateTime>();

        static string _url = "", _token = "";
        static bool _ignoreCert;
        static int _seconds = 5;
        static HttpClient _http;
        static Timer _timer;
        static int _busy;

        /// <summary>The last problem talking to Home Assistant ("" = fine so far).</summary>
        public static string LastError { get; private set; } = "";

        public static bool Configured { get { return _url.Length > 0 && _token.Length > 0; } }
        public static string Url { get { return _url; } }

        static string CleanUrl(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return "";
            if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "http://" + url;
            return url.TrimEnd('/');
        }

        /// <summary>Apply the connection settings (safe to call as often as you like; only a change does anything).</summary>
        public static void Configure(string url, string storedToken, bool ignoreCert, int seconds)
        {
            url = CleanUrl(url);
            string token = Secret.Unprotect(storedToken);
            seconds = Math.Max(2, Math.Min(120, seconds));
            lock (Gate)
            {
                bool same = url == _url && token == _token && ignoreCert == _ignoreCert && seconds == _seconds;
                if (same && _http != null) return;

                _url = url; _token = token; _ignoreCert = ignoreCert; _seconds = seconds;
                States.Clear();
                Fetching.Clear();
                LastError = "";

                if (_timer != null) { _timer.Dispose(); _timer = null; }
                if (_http != null) { _http.Dispose(); _http = null; }
                HaSocket.Restart(null, null, false);
                VDrop.Configure(url, token, ignoreCert);      // Notes synced with the vDrop add-on (only runs while a Notes module uses it)
                if (!Configured) return;
                HaSocket.Restart(url, token, ignoreCert);     // live updates; the polling below covers anything it doesn't

                var handler = new HttpClientHandler();
                if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _timer = new Timer(delegate { Tick(); }, null, TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(_seconds));
            }
        }

        /// <summary>The latest state of an entity when it's known already (a module watches it), without starting to poll it.</summary>
        public static HaState Peek(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return null;
            lock (Gate) { HaState s; return States.TryGetValue(entityId.Trim(), out s) ? s : null; }
        }

        /// <summary>The latest state of an entity, or null when not (yet) known. Calling this is what keeps the entity being polled.</summary>
        public static HaState Get(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return null;
            entityId = entityId.Trim();
            bool start = false;
            HaState s;
            lock (Gate)
            {
                if (!Configured) return null;
                Wanted[entityId] = DateTime.UtcNow;
                States.TryGetValue(entityId, out s);
                DateTime when;
                if (s == null && (!Fetching.TryGetValue(entityId, out when) || (DateTime.UtcNow - when).TotalSeconds > 10))
                {
                    Fetching[entityId] = DateTime.UtcNow;
                    start = true;
                }
            }
            if (start) Task.Run(delegate { FetchOne(entityId).Wait(); });
            return s;
        }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var ids = new List<string>();
                lock (Gate)
                {
                    var stale = new List<string>();
                    foreach (var kv in Wanted)
                    {
                        if ((DateTime.UtcNow - kv.Value).TotalSeconds > 30) stale.Add(kv.Key);
                        else if (!IsLive(kv.Key)) ids.Add(kv.Key);       // arriving over the WebSocket: no need to ask
                    }
                    foreach (string k in stale) { Wanted.Remove(k); States.Remove(k); }
                }
                if (ids.Count == 0) return;
                var tasks = new List<Task>();
                var gate = new SemaphoreSlim(4);
                foreach (string id in ids)
                {
                    string cap = id;
                    tasks.Add(Task.Run(async delegate
                    {
                        await gate.WaitAsync();
                        try { await FetchOne(cap); }
                        finally { gate.Release(); }
                    }));
                }
                Task.WaitAll(tasks.ToArray());
            }
            catch (Exception ex) { Log.Write("HA poll: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static bool IsLive(string id)
        {
            if (!HaSocket.Connected) return false;
            lock (HaSocket.Live) return HaSocket.Live.Contains(id);
        }

        /// <summary>The entities some module asked for in the last 30 s (the WebSocket subscribes to these).</summary>
        internal static List<string> WantedIds()
        {
            var l = new List<string>();
            lock (Gate)
                foreach (var kv in Wanted)
                    if ((DateTime.UtcNow - kv.Value).TotalSeconds <= 30) l.Add(kv.Key);
            return l;
        }

        /// <summary>
        /// A state from the WebSocket. null fields = unchanged (full = it is a complete new state: missing unit / name become empty).
        /// </summary>
        internal static void PutLive(string id, string state, string unit, string name, bool full, bool isNew,
                                     Dictionary<string, string> attrSet = null, List<string> attrGone = null)
        {
            lock (Gate)
            {
                HaState old;
                States.TryGetValue(id, out old);
                var s = new HaState
                {
                    EntityId = id,
                    State = state ?? (old != null ? old.State : ""),
                    Unit = unit ?? (full || old == null ? "" : old.Unit),
                    Name = name ?? (full || old == null ? "" : old.Name),
                    Fetched = DateTime.UtcNow
                };
                // attributes: a full state replaces them; a change adds / removes some
                s.Attr = full || old == null || old.Attr == null ? new Dictionary<string, string>() : new Dictionary<string, string>(old.Attr);
                if (attrSet != null) foreach (var kv in attrSet) s.Attr[kv.Key] = kv.Value;
                if (attrGone != null) foreach (string g in attrGone) s.Attr.Remove(g);
                States[id] = s;
            }
            LastError = "";
        }

        static HaState ParseState(JsonElement e)
        {
            var s = new HaState();
            if (e.TryGetProperty("entity_id", out JsonElement id)) s.EntityId = id.GetString() ?? "";
            if (e.TryGetProperty("state", out JsonElement st)) s.State = st.GetString() ?? "";
            if (e.TryGetProperty("attributes", out JsonElement at) && at.ValueKind == JsonValueKind.Object)
            {
                if (at.TryGetProperty("unit_of_measurement", out JsonElement u) && u.ValueKind == JsonValueKind.String) s.Unit = u.GetString() ?? "";
                if (at.TryGetProperty("friendly_name", out JsonElement n) && n.ValueKind == JsonValueKind.String) s.Name = n.GetString() ?? "";
                foreach (JsonProperty a in at.EnumerateObject()) s.Attr[a.Name] = a.Value.GetRawText();
            }
            s.Fetched = DateTime.UtcNow;
            return s;
        }

        /// <summary>
        /// An entity's states since fromUtc (Home Assistant's recorder: the state at fromUtc first, then each change), oldest first, as
        /// (when it changed, UTC; the state). Empty when there's no history (or Home Assistant can't be reached) - a graph then just fills up.
        /// </summary>
        public static async Task<List<KeyValuePair<DateTime, string>>> HistoryAsync(string entityId, DateTime fromUtc)
        {
            var list = new List<KeyValuePair<DateTime, string>>();
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _url; }
            if (http == null || string.IsNullOrWhiteSpace(entityId)) return list;
            try
            {
                string from = fromUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z";
                string path = url + "/api/history/period/" + Uri.EscapeDataString(from) + "?filter_entity_id=" + Uri.EscapeDataString(entityId.Trim()) +
                              "&minimal_response&no_attributes";
                using (HttpResponseMessage r = await http.GetAsync(path).ConfigureAwait(false))
                {
                    if (!r.IsSuccessStatusCode) return list;
                    using (JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false)))
                    {
                        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                        foreach (JsonElement series in doc.RootElement.EnumerateArray())
                        {
                            if (series.ValueKind != JsonValueKind.Array) continue;
                            foreach (JsonElement e in series.EnumerateArray())
                            {
                                JsonElement st, at;
                                if (!e.TryGetProperty("state", out st) || st.ValueKind != JsonValueKind.String) continue;
                                if (!e.TryGetProperty("last_changed", out at) || at.ValueKind != JsonValueKind.String) continue;
                                DateTime when;
                                if (!DateTime.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when)) continue;
                                list.Add(new KeyValuePair<DateTime, string>(when < fromUtc ? fromUtc : when, st.GetString() ?? ""));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Home Assistant history of " + entityId + ": " + ex.Message); }
            list.Sort(delegate (KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) { return a.Key.CompareTo(b.Key); });
            return list;
        }

        static async Task FetchOne(string entityId)
        {
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _url; }
            if (http == null) return;
            try
            {
                using (HttpResponseMessage r = await http.GetAsync(url + "/api/states/" + Uri.EscapeDataString(entityId)))
                {
                    if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        lock (Gate) States[entityId] = new HaState { EntityId = entityId, State = "unknown", Name = entityId + " (not found)", Fetched = DateTime.UtcNow };
                        return;
                    }
                    if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        LastError = "Home Assistant rejected the access token (401).";
                        return;
                    }
                    r.EnsureSuccessStatusCode();
                    string json = await r.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        HaState s = ParseState(doc.RootElement);
                        if (s.EntityId.Length == 0) s.EntityId = entityId;
                        lock (Gate) { if (_http == http) States[entityId] = s; }
                    }
                    LastError = "";
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                lock (Gate) { HaState old; if (States.TryGetValue(entityId, out old)) old.State = "unavailable"; }
            }
        }

        /// <summary>Check the connection. Returns a one-line message and whether it worked.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(string url, string storedOrPlainToken, bool ignoreCert)
        {
            url = CleanUrl(url);
            string token = Secret.Unprotect(storedOrPlainToken);
            if (url.Length == 0) return new KeyValuePair<bool, string>(false, "Enter the Home Assistant address first.");
            if (token.Length == 0) return new KeyValuePair<bool, string>(false, "Enter an access token first.");
            try
            {
                var handler = new HttpClientHandler();
                if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) })
                {
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (HttpResponseMessage r = await http.GetAsync(url + "/api/"))
                    {
                        if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized) return new KeyValuePair<bool, string>(false, "Reached Home Assistant, but the access token was rejected (401).");
                        if (!r.IsSuccessStatusCode) return new KeyValuePair<bool, string>(false, "Home Assistant answered HTTP " + (int)r.StatusCode + ".");
                        string body = await r.Content.ReadAsStringAsync();
                        string msg = "Connected.";
                        try
                        {
                            using (JsonDocument d = JsonDocument.Parse(body))
                                if (d.RootElement.TryGetProperty("message", out JsonElement m)) msg = "Connected: " + m.GetString();
                        }
                        catch { }
                        return new KeyValuePair<bool, string>(true, msg);
                    }
                }
            }
            catch (Exception ex)
            {
                string m = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new KeyValuePair<bool, string>(false, "Could not connect: " + m);
            }
        }

        /// <summary>Every entity Home Assistant knows (for the entity picker). Throws on failure.</summary>
        public static async Task<List<HaState>> ListAsync()
        {
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _url; }
            if (http == null) throw new InvalidOperationException("Home Assistant is not set up yet (Settings > Plugins).");
            var list = new List<HaState>();
            using (HttpResponseMessage r = await http.GetAsync(url + "/api/states"))
            {
                r.EnsureSuccessStatusCode();
                string json = await r.Content.ReadAsStringAsync();
                using (JsonDocument doc = JsonDocument.Parse(json))
                    foreach (JsonElement e in doc.RootElement.EnumerateArray()) list.Add(ParseState(e));
            }
            list.Sort(delegate (HaState a, HaState b) { return string.CompareOrdinal(a.EntityId, b.EntityId); });
            return list;
        }

        /// <summary>The raw JSON of one entity's state (with all its attributes).</summary>
        public static async Task<string> GetStateJsonAsync(string entityId)
        {
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _url; }
            if (http == null) throw new InvalidOperationException("Home Assistant is not set up (Settings > Plugins).");
            using (HttpResponseMessage r = await http.GetAsync(url + "/api/states/" + Uri.EscapeDataString(entityId)))
            {
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException(entityId + " was not found in Home Assistant");
                r.EnsureSuccessStatusCode();
                return await r.Content.ReadAsStringAsync();
            }
        }

        /// <summary>POST JSON to a Home Assistant API path (e.g. "/api/services/weather/get_forecasts?return_response") and return the answer.</summary>
        public static async Task<string> PostJsonAsync(string path, string json)
        {
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _url; }
            if (http == null) throw new InvalidOperationException("Home Assistant is not set up (Settings > Plugins).");
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage r = await http.PostAsync(url + path, content))
            {
                string text = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)r.StatusCode + " " + text);
                return text;
            }
        }

        // ---- actions -------------------------------------------------------------------------------------
        public static string Domain(string entityId)
        {
            int i = (entityId ?? "").IndexOf('.');
            return i > 0 ? entityId.Substring(0, i) : "";
        }

        /// <summary>The service that a click on this entity should call ("domain", "service"), or null when a click does nothing.</summary>
        public static KeyValuePair<string, string>? ClickService(string entityId, string currentState)
        {
            string d = Domain(entityId);
            switch (d)
            {
                case "light": case "switch": case "input_boolean": case "fan": case "automation": case "group": case "humidifier":
                    return new KeyValuePair<string, string>("homeassistant", "toggle");
                case "cover": return new KeyValuePair<string, string>("cover", "toggle");
                case "scene": case "script": return new KeyValuePair<string, string>(d, "turn_on");
                case "button": case "input_button": return new KeyValuePair<string, string>(d, "press");
                case "lock": return new KeyValuePair<string, string>("lock", currentState == "locked" ? "unlock" : "lock");
                default: return null;
            }
        }

        /// <summary>
        /// Call a service with extra data (temperature, hvac_mode, percentage ...). It goes over the live WebSocket when that's connected
        /// (no new HTTP request per click), otherwise over REST. The new state arrives over the socket by itself; otherwise it's fetched.
        /// </summary>
        public static void CallService(string domain, string service, string entityId, Dictionary<string, object> data)
        {
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _url; }
            if (http == null) return;
            Task.Run(async delegate
            {
                try
                {
                    string sock = await HaSocket.CallService(domain, service, entityId, data);
                    if (sock == null)
                    {
                        // not connected: REST
                        var body = new Dictionary<string, object> { { "entity_id", entityId } };
                        if (data != null) foreach (var kv in data) body[kv.Key] = kv.Value;
                        using (var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"))
                        using (HttpResponseMessage r = await http.PostAsync(url + "/api/services/" + domain + "/" + service, content))
                        {
                            if (!r.IsSuccessStatusCode) LastError = "Service " + domain + "." + service + " answered HTTP " + (int)r.StatusCode;
                        }
                    }
                    else if (sock.Length > 0)
                    {
                        LastError = "Service " + domain + "." + service + ": " + sock;
                        Log.Write("HA " + domain + "." + service + " " + entityId + ": " + sock);
                    }
                    if (!IsLive(entityId)) await FetchOne(entityId);       // show the new state straight away
                }
                catch (Exception ex) { LastError = ex.Message; }
            });
        }

        public static void CallService(string domain, string service, string entityId)
        {
            CallService(domain, service, entityId, null);
        }
    }
}
