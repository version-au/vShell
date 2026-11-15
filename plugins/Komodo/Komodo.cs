using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The Komodo connection and notifications - kept in vShell's settings under Plugins["komodo"].</summary>
    public sealed class KoSettings
    {
        readonly SettingsBag _b;
        public KoSettings(BarSettings s) { _b = s.Integration("komodo"); }
        public static KoSettings Of(BarSettings s) { return new KoSettings(s); }

        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>API key ("K-...") and secret ("S-...", stored encrypted - Secret is the stored text, SecretPlain the real one).</summary>
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", (value ?? "").Trim()); } }
        public string Secret { get { return _b.Get("Secret", ""); } set { _b.Set("Secret", vShell.Core.Secret.Protect(value ?? "")); } }
        public string SecretPlain { get { return _b.GetSecret("Secret"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>The run box keyword (without the !).</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "ko").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "ko" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        /// <summary>Notices: something stops / turns unhealthy / a server can't be reached; an image update; a procedure fails (or one run from vShell finishes).</summary>
        public bool NotifyDown { get { return _b.Get("NotifyDown", true); } set { _b.Set("NotifyDown", value); } }
        /// <summary>Wait this many seconds before saying something is down (it may be back by then); 0 = straight away.</summary>
        public int NotifyDelay { get { return Math.Max(0, Math.Min(1800, _b.Get("NotifyDelay", 0))); } set { _b.Set("NotifyDelay", value); } }
        public bool NotifyUpdates { get { return _b.Get("NotifyUpdates", true); } set { _b.Set("NotifyUpdates", value); } }
        public bool NotifyProcedures { get { return _b.Get("NotifyProcedures", true); } set { _b.Set("NotifyProcedures", value); } }
    }

    /// <summary>A Komodo module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class KoView
    {
        public static readonly string[] Shows = { "Managed", "Containers", "Both" };
        public static readonly string[] Filters = { "All", "Problems" };

        readonly SettingsBag _b;
        public KoView(ModuleConfig cfg) { _b = cfg.Bag; }

        /// <summary>"Managed" (stacks and deployments), "Containers" (every Docker container) or "Both".</summary>
        public string Show { get { string v = _b.Get("Show", "Managed"); return Array.IndexOf(Shows, v) < 0 ? "Managed" : v; } set { _b.Set("Show", value); } }
        /// <summary>"All" or "Problems" (only what isn't running / healthy).</summary>
        public string Filter { get { string v = _b.Get("Filter", "All"); return Array.IndexOf(Filters, v) < 0 ? "All" : v; } set { _b.Set("Filter", value); } }
        /// <summary>Only this server (its name; empty = all).</summary>
        public string Server { get { return _b.Get("Server", ""); } set { _b.Set("Server", value ?? ""); } }
        /// <summary>How many the list shows per server.</summary>
        public int Count { get { return Math.Max(1, Math.Min(100, _b.Get("Count", 15))); } set { _b.Set("Count", value); } }
        public bool ShowProcedures { get { return _b.Get("ShowProcedures", true); } set { _b.Set("ShowProcedures", value); } }
        /// <summary>The "Komodo" button (opens Komodo's web page) at the top of the list.</summary>
        public bool ShowOpen { get { return _b.Get("ShowOpen", true); } set { _b.Set("ShowOpen", value); } }
        /// <summary>Group the list by server (off = one list).</summary>
        public bool Group { get { return _b.Get("Group", true); } set { _b.Set("Group", value); } }
    }

    /// <summary>A stack, deployment, container or procedure in Komodo.</summary>
    public sealed class KoItem
    {
        /// <summary>"stack", "deployment", "container", "procedure".</summary>
        public string Kind = "";
        public string Id = "";              // resource id (containers: the container name)
        public string Name = "";
        public string ServerId = "", ServerName = "";
        /// <summary>Komodo's state word, lower case (running, stopped, unhealthy, exited, down, not_deployed, ok, failed ...).</summary>
        public string State = "";
        public string Status = "";          // Docker's status text ("Up 3 days (healthy)")
        public string Image = "";
        public bool Update;                 // a newer image is available
        /// <summary>Stacks: their services (service, image, update available).</summary>
        public List<KoService> Services = new List<KoService>();
        /// <summary>Containers: the stack / deployment it belongs to (when Komodo manages it), else "".</summary>
        public string Owner = "";
        public long LastRun;                // procedures: ms since 1970

        public string Key { get { return Kind + "|" + (Kind == "container" ? ServerId + "/" + Name : Id); } }

        /// <summary>good / busy / bad / off - the dot colour.</summary>
        public string Health
        {
            get
            {
                switch (State)
                {
                    case "running": case "ok": return "good";
                    case "deploying": case "restarting": case "created": case "removing": case "stopping": case "paused": return "busy";
                    case "unhealthy": case "dead": case "failed": return "bad";
                    case "stopped": case "exited": case "down": case "not_deployed": return Kind == "procedure" ? "off" : "off";
                    default: return "off";
                }
            }
        }
        public bool Running { get { return State == "running"; } }
        public bool Problem { get { return State == "unhealthy" || State == "dead" || State == "restarting" || (Kind == "procedure" && State == "failed"); } }
    }

    public sealed class KoService
    {
        public string Service = "", Image = "", State = "", Status = "", Container = "";
        public bool Update;
    }

    public sealed class KoServer
    {
        public string Id = "", Name = "";
        /// <summary>ok / notok / disabled</summary>
        public string State = "";
        /// <summary>The Periphery address Komodo reaches it at (http://192.168.1.20:8120) - its host is the server's.</summary>
        public string Address = "";
        /// <summary>The host part of Address ("" when there is none, or it's Komodo's own built-in server).</summary>
        public string Host
        {
            get
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(Address)) return "";
                    string a = Address.Contains("://") ? Address : "http://" + Address;
                    string h = new Uri(a).Host;
                    return h == "localhost" || h == "127.0.0.1" || h == "periphery" ? "" : h;
                }
                catch { return ""; }
            }
        }
    }

    public sealed class KoSnapshot
    {
        public readonly List<KoItem> Stacks = new List<KoItem>(), Deployments = new List<KoItem>(), Containers = new List<KoItem>(), Procedures = new List<KoItem>();
        public readonly List<KoServer> Servers = new List<KoServer>();
        public DateTime Fetched = DateTime.MinValue;
        /// <summary>When what it holds last changed (a read that found everything the same keeps the previous one): the list redraws only then.</summary>
        public DateTime Stamp = DateTime.MinValue;
        public string Sig = "";
        public string Error = "";
        public bool Ok { get { return Fetched != DateTime.MinValue; } }

        /// <summary>Everything the list shows, as one string (to tell whether anything changed).</summary>
        public string Signature()
        {
            var b = new StringBuilder();
            Action<List<KoItem>> add = delegate (List<KoItem> l)
            {
                foreach (KoItem i in l)
                {
                    b.Append(i.Key).Append('|').Append(i.State).Append('|').Append(i.Status).Append('|').Append(i.Update).Append('|').Append(i.LastRun).Append('|').Append(i.Services.Count).Append(';');
                    foreach (KoService sv in i.Services) b.Append(sv.Service).Append(sv.State).Append(sv.Update).Append(',');
                }
            };
            add(Stacks); add(Deployments); add(Containers); add(Procedures);
            foreach (KoServer s in Servers) b.Append(s.Id).Append(s.State).Append(';');
            return b.ToString();
        }

        public string ServerName(string id)
        {
            KoServer s = Servers.Find(delegate (KoServer x) { return x.Id == id; });
            return s != null ? s.Name : "";
        }
    }

    /// <summary>
    /// Komodo (komo.do): the stacks, deployments, Docker containers and procedures it manages, and its start / stop / restart / deploy /
    /// pull / run actions. Uses the API key + secret (Settings > API keys in Komodo). Polls every 5 s while something shows it (every 30 s
    /// otherwise, for the notices). Calls are POST /read or /execute with {"type": name, "params": {...}}, which every Komodo version takes.
    /// </summary>
    public static class Komodo
    {
        static readonly object Gate = new object();
        static KoSnapshot _snap = new KoSnapshot();
        static Timer _timer;
        static BarSettings _s;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue, _lastPoll = DateTime.MinValue, _retry = DateTime.MinValue;
        static HttpClient _http;
        static string _httpKey = "";
        /// <summary>Komodo before 2.3 calls ListAllContainers "ListAllDockerContainers".</summary>
        static bool _oldContainers;

        /// <summary>A notice: (title line, detail, Komodo page address, bad).</summary>
        public static event Action<string, string, string, bool> Notice;
        public static event Action Changed;

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            KoSettings k = KoSettings.Of(s);
            return !string.IsNullOrWhiteSpace(k.Url) && !string.IsNullOrWhiteSpace(k.Key) && !string.IsNullOrEmpty(k.SecretPlain);
        }

        public static void Configure(BarSettings s)
        {
            _s = s;
            lock (Gate)
                if (_timer == null && Configured(s)) _timer = new Timer(delegate { Tick(); }, null, 700, 2500);
            Poke();
        }

        /// <summary>The plugin was removed: stop polling and forget what was read (Configure starts it again).</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                _snap = new KoSnapshot();
            }
            if (_http != null) { try { _http.Dispose(); } catch { } _http = null; _httpKey = ""; }
        }

        public static KoSnapshot Latest
        {
            get
            {
                _wanted = DateTime.UtcNow;
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

        static string Base(BarSettings s)
        {
            string b = KoSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        /// <summary>The Komodo page of an item (stack / deployment / procedure / container).</summary>
        public static string PageUrl(BarSettings s, KoItem it)
        {
            string b = Base(s);
            if (it == null) return b;
            switch (it.Kind)
            {
                case "stack": return b + "/stacks/" + Uri.EscapeDataString(it.Id);
                case "deployment": return b + "/deployments/" + Uri.EscapeDataString(it.Id);
                case "procedure": return b + "/procedures/" + Uri.EscapeDataString(it.Id);
                case "container": return b + "/servers/" + Uri.EscapeDataString(it.ServerId) + "/container/" + Uri.EscapeDataString(it.Name);
                default: return b;
            }
        }

        static HttpClient Http(BarSettings s)
        {
            KoSettings k = KoSettings.Of(s);
            string key = Base(s) + "|" + k.Key + "|" + k.Secret + "|" + k.IgnoreCert;
            if (_http == null || key != _httpKey)
            {
                var handler = new HttpClientHandler();
                if (k.IgnoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                h.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", k.Key.Trim());
                h.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Secret", k.SecretPlain);
                var old = _http;
                _http = h; _httpKey = key;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return _http;
        }

        /// <summary>One API call: section = "read" / "execute"; params = a JSON object. Throws with Komodo's own message on failure.</summary>
        static string Call(BarSettings s, string section, string type, string paramsJson)
        {
            string body = "{\"type\":\"" + type + "\",\"params\":" + (string.IsNullOrEmpty(paramsJson) ? "{}" : paramsJson) + "}";
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage r = Http(s).PostAsync(Base(s) + "/" + section, content).GetAwaiter().GetResult())
            {
                string text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (r.IsSuccessStatusCode) return text;
                int code = (int)r.StatusCode;
                string msg = "";
                try { using (JsonDocument d = JsonDocument.Parse(text)) msg = Str(d.RootElement, "error"); } catch { msg = text.Length > 160 ? text.Substring(0, 160) : text; }
                if (code == 401 || code == 403) throw new KoException("Komodo refused the API key (" + code + ")" + (msg.Length > 0 ? ": " + msg : ""), code);
                throw new KoException("Komodo answered " + code + (msg.Length > 0 ? ": " + msg : ""), code);
            }
        }

        sealed class KoException : Exception
        {
            public readonly int Code;
            public KoException(string m, int code) : base(m) { Code = code; }
        }

        static string J(string s) { return JsonSerializer.Serialize(s ?? ""); }

        // ---- JSON helpers -------------------------------------------------------------------------------
        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return "";
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
        }

        static long Long(JsonElement e, string name)
        {
            JsonElement v;
            long n;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out n) ? n : 0;
        }

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) ? v : default(JsonElement);
        }

        /// <summary>"NotDeployed" / "not_deployed" / "Running" -> "not_deployed" / "running".</summary>
        static string Norm(string state)
        {
            if (string.IsNullOrEmpty(state)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < state.Length; i++)
            {
                char c = state[i];
                if (char.IsUpper(c) && i > 0 && state[i - 1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            string r = sb.ToString();
            return r == "not_ok" ? "notok" : r;
        }

        // =================================================================
        // Polling
        // =================================================================
        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null || !Configured(s)) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 15;
                KoSettings k = KoSettings.Of(s);
                bool notify = k.NotifyDown || k.NotifyUpdates || k.NotifyProcedures;
                if (!shown && !notify) return;
                if (DateTime.UtcNow < _retry) return;
                if ((DateTime.UtcNow - _lastPoll).TotalSeconds < (shown ? 4.5 : 30)) return;
                _lastPoll = DateTime.UtcNow;
                KoSnapshot snap;
                try { snap = Read(s); }
                catch (Exception ex)
                {
                    KoSnapshot old;
                    lock (Gate) old = _snap;
                    if (old.Error != ex.Message) Log.Write("Komodo: " + ex.Message);
                    snap = new KoSnapshot { Error = ex.Message, Fetched = old.Fetched };
                    snap.Stacks.AddRange(old.Stacks); snap.Deployments.AddRange(old.Deployments); snap.Containers.AddRange(old.Containers);
                    snap.Procedures.AddRange(old.Procedures); snap.Servers.AddRange(old.Servers);
                    _retry = DateTime.UtcNow.AddSeconds(20);
                    lock (Gate) _snap = snap;
                    Fire(Changed);
                    return;
                }
                KoSnapshot prev;
                lock (Gate) prev = _snap;
                snap.Sig = snap.Signature();
                snap.Stamp = prev.Ok && prev.Sig == snap.Sig && prev.Error.Length == 0 ? prev.Stamp : snap.Fetched;
                lock (Gate) _snap = snap;
                if (prev.Ok) Compare(prev, snap, s);
                Fire(Changed);
            }
            catch (Exception ex) { Log.Write("Komodo: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        static KoSnapshot Read(BarSettings s)
        {
            var snap = new KoSnapshot { Fetched = DateTime.UtcNow };
            // servers (names for everything else)
            using (JsonDocument d = JsonDocument.Parse(Call(s, "read", "ListServers", "{\"limit\":0}")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                        snap.Servers.Add(new KoServer { Id = Str(e, "id"), Name = Str(e, "name"), State = Norm(Str(Obj(e, "info"), "state")), Address = Str(Obj(e, "info"), "address") });

            using (JsonDocument d = JsonDocument.Parse(Call(s, "read", "ListStacks", "{\"limit\":0}")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        JsonElement info = Obj(e, "info");
                        var it = new KoItem
                        {
                            Kind = "stack", Id = Str(e, "id"), Name = Str(e, "name"), ServerId = Str(info, "server_id"), ServerName = Str(info, "server_name"),
                            State = Norm(Str(info, "state")), Status = Str(info, "status")
                        };
                        JsonElement svcs = Obj(info, "services");
                        if (svcs.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement x in svcs.EnumerateArray())
                            {
                                var sv = new KoService { Service = Str(x, "service"), Image = Str(x, "image"), Update = Bool(x, "update_available") };
                                it.Services.Add(sv);
                                if (sv.Update) it.Update = true;
                            }
                        snap.Stacks.Add(it);
                    }

            using (JsonDocument d = JsonDocument.Parse(Call(s, "read", "ListDeployments", "{\"limit\":0}")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        JsonElement info = Obj(e, "info");
                        snap.Deployments.Add(new KoItem
                        {
                            Kind = "deployment", Id = Str(e, "id"), Name = Str(e, "name"), ServerId = Str(info, "server_id"), ServerName = Str(info, "server_name"),
                            State = Norm(Str(info, "state")), Status = Str(info, "status"), Image = Str(info, "image"), Update = Bool(info, "update_available")
                        });
                    }

            try
            {
                using (JsonDocument d = JsonDocument.Parse(Call(s, "read", "ListProcedures", "{\"limit\":0}")))
                    if (d.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in d.RootElement.EnumerateArray())
                        {
                            JsonElement info = Obj(e, "info");
                            snap.Procedures.Add(new KoItem { Kind = "procedure", Id = Str(e, "id"), Name = Str(e, "name"), State = Norm(Str(info, "state")), LastRun = Long(info, "last_run_at") });
                        }
            }
            catch (KoException ex) when (ex.Code == 403) { }      // a service user without procedure rights: just none

            // every container on every server (also the ones Komodo doesn't manage)
            string list = null;
            if (!_oldContainers)
            {
                try { list = Call(s, "read", "ListAllContainers", "{\"limit\":0}"); }
                catch (KoException ex) when (ex.Code == 400 || ex.Code == 404 || ex.Code == 422) { _oldContainers = true; }
            }
            if (list == null)
            {
                try { list = Call(s, "read", "ListAllDockerContainers", "{}"); }
                catch (KoException ex) when (ex.Code == 400 || ex.Code == 404 || ex.Code == 422) { _oldContainers = false; }
            }
            if (list != null)
                using (JsonDocument d = JsonDocument.Parse(list))
                    if (d.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in d.RootElement.EnumerateArray())
                        {
                            var it = new KoItem
                            {
                                Kind = "container", Name = Str(e, "name"), Id = Str(e, "name"), ServerId = Str(e, "server_id"), ServerName = Str(e, "server_name"),
                                State = Norm(Str(e, "state")), Status = Str(e, "status"), Image = Str(e, "image")
                            };
                            if (it.State == "") it.State = "unknown";
                            if (it.Status.IndexOf("unhealthy", StringComparison.OrdinalIgnoreCase) >= 0 && it.State == "running") it.State = "unhealthy";
                            snap.Containers.Add(it);
                        }

            // fill in server names and which stack / deployment a container belongs to
            foreach (KoItem it in AllOf(snap)) if (it.ServerName.Length == 0 && it.ServerId.Length > 0) it.ServerName = snap.ServerName(it.ServerId);
            foreach (KoItem c in snap.Containers)
            {
                KoItem dep = snap.Deployments.Find(delegate (KoItem x) { return x.Name == c.Name && x.ServerId == c.ServerId; });
                if (dep != null) { c.Owner = "deployment " + dep.Name; continue; }
                foreach (KoItem st in snap.Stacks)
                    if (st.ServerId == c.ServerId && (c.Name.StartsWith(st.Name + "-") || c.Name.StartsWith(st.Name + "_"))) { c.Owner = "stack " + st.Name; break; }
            }
            return snap;
        }

        static IEnumerable<KoItem> AllOf(KoSnapshot s)
        {
            foreach (KoItem i in s.Stacks) yield return i;
            foreach (KoItem i in s.Deployments) yield return i;
            foreach (KoItem i in s.Containers) yield return i;
            foreach (KoItem i in s.Procedures) yield return i;
        }

        // ---- notices: what changed since the last look -----------------------------------------------------
        static readonly HashSet<string> _myRuns = new HashSet<string>();

        static void Compare(KoSnapshot a, KoSnapshot b, BarSettings s)
        {
            KoSettings k = KoSettings.Of(s);
            var before = new Dictionary<string, KoItem>();
            foreach (KoItem i in AllOf(a)) before[i.Key] = i;
            foreach (KoItem i in AllOf(b))
            {
                KoItem old;
                if (!before.TryGetValue(i.Key, out old)) continue;
                if (i.Kind == "procedure")
                {
                    if (!k.NotifyProcedures || old.State == i.State || old.State != "running") continue;
                    bool mine = _myRuns.Remove(i.Id);
                    if (i.State == "failed") Raise("✕ Procedure " + i.Name + " failed", "", PageUrl(s, i), true);
                    else if (i.State == "ok" && mine) Raise("✓ Procedure " + i.Name + " finished", "", PageUrl(s, i), false);
                    continue;
                }
                if (i.Kind == "container" && i.Owner.Length > 0) continue;      // its stack / deployment says it
                if (k.NotifyDown && old.State != i.State)
                {
                    bool wasUp = old.State == "running";
                    bool bad = i.State == "unhealthy" || i.State == "dead" || ((i.State == "exited" || i.State == "stopped" || i.State == "down") && wasUp && !Expected(i));
                    string dkey = "ko|" + i.Key, url = PageUrl(s, i);
                    if (bad && (wasUp || old.State == "restarting" || old.State == "deploying"))
                    {
                        // held for the wait you set: if it's running again by then, nothing is said
                        string title = "✕ " + Cap(i.Kind) + " " + i.Name + " is " + i.State.Replace('_', ' '), detail = i.ServerName + (i.Status.Length > 0 ? " · " + i.Status : "");
                        DownDelay.Down(dkey, k.NotifyDelay, delegate { Raise(title, detail, url, true); });
                    }
                    else if (i.State == "running")
                    {
                        UpResult r = DownDelay.Up(dkey);
                        if (r == UpResult.WasNotified || (r == UpResult.None && (old.State == "unhealthy" || old.State == "dead")))
                            Raise("✓ " + Cap(i.Kind) + " " + i.Name + " is running again", i.ServerName, url, false);
                    }
                }
                if (k.NotifyUpdates && i.Update && !old.Update && i.Kind != "container")
                    Raise("⬆ Update available: " + i.Name, i.Kind == "stack" ? Services(i) : i.Image, PageUrl(s, i), false);
            }
            foreach (KoServer sv in b.Servers)
            {
                KoServer old = a.Servers.Find(delegate (KoServer x) { return x.Id == sv.Id; });
                if (old == null || !k.NotifyDown) continue;
                string surl = Base(s) + "/servers/" + Uri.EscapeDataString(sv.Id), sname = sv.Name;
                if (old.State == "ok" && sv.State == "notok")
                    DownDelay.Down("ko|server|" + sv.Id, k.NotifyDelay, delegate { Raise("✕ Server " + sname + " can't be reached", "", surl, true); });
                else if (old.State == "notok" && sv.State == "ok" && DownDelay.Up("ko|server|" + sv.Id) != UpResult.Cancelled)
                    Raise("✓ Server " + sname + " is back", "", surl, false);
            }
        }

        static string Services(KoItem stack)
        {
            var names = new List<string>();
            foreach (KoService sv in stack.Services) if (sv.Update) names.Add(sv.Service);
            return names.Count > 0 ? string.Join(", ", names) : "";
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        // stopped on purpose from vShell: no "stopped" notice for it
        static readonly Dictionary<string, DateTime> _stopping = new Dictionary<string, DateTime>();
        static bool Expected(KoItem i)
        {
            lock (_stopping)
            {
                DateTime until;
                if (_stopping.TryGetValue(i.Key, out until) && DateTime.UtcNow < until) return true;
            }
            return false;
        }

        static void Raise(string title, string detail, string url, bool bad)
        {
            var n = Notice;
            if (n != null) try { n(title, detail, url, bad); } catch (Exception ex) { Log.Write("Komodo notice: " + ex.Message); }
        }

        // =================================================================
        // Actions
        // =================================================================
        /// <summary>
        /// The actions an item offers: (label, request, asks first). Stacks / deployments: Komodo's own; containers: Docker's; procedures: Run.
        /// </summary>
        public static List<string[]> Actions(KoItem it)
        {
            var a = new List<string[]>();
            bool up = it.State == "running" || it.State == "unhealthy" || it.State == "restarting";
            bool paused = it.State == "paused";
            switch (it.Kind)
            {
                case "stack":
                    if (!up && !paused) a.Add(new[] { "Start", "StartStack", "" });
                    if (up) { a.Add(new[] { "Restart", "RestartStack", "ask" }); a.Add(new[] { "Stop", "StopStack", "ask" }); a.Add(new[] { "Pause", "PauseStack", "" }); }
                    if (paused) a.Add(new[] { "Unpause", "UnpauseStack", "" });
                    a.Add(new[] { "-" });
                    a.Add(new[] { it.Update ? "Pull and redeploy (update)" : "Redeploy", "DeployStack", "ask" });
                    a.Add(new[] { "Pull images", "PullStack", "" });
                    a.Add(new[] { "Destroy (docker compose down)", "DestroyStack", "ask" });
                    break;
                case "deployment":
                    if (!up && !paused && it.State != "not_deployed") a.Add(new[] { "Start", "StartDeployment", "" });
                    if (up) { a.Add(new[] { "Restart", "RestartDeployment", "ask" }); a.Add(new[] { "Stop", "StopDeployment", "ask" }); a.Add(new[] { "Pause", "PauseDeployment", "" }); }
                    if (paused) a.Add(new[] { "Unpause", "UnpauseDeployment", "" });
                    if (a.Count > 0) a.Add(new[] { "-" });
                    a.Add(new[] { it.State == "not_deployed" ? "Deploy" : it.Update ? "Pull and redeploy (update)" : "Redeploy", "Deploy", it.State == "not_deployed" ? "" : "ask" });
                    a.Add(new[] { "Pull image", "PullDeployment", "" });
                    a.Add(new[] { "Destroy container", "DestroyDeployment", "ask" });
                    break;
                case "container":
                    if (!up && !paused) a.Add(new[] { "Start", "StartContainer", "" });
                    if (up) { a.Add(new[] { "Restart", "RestartContainer", "ask" }); a.Add(new[] { "Stop", "StopContainer", "ask" }); a.Add(new[] { "Pause", "PauseContainer", "" }); }
                    if (paused) a.Add(new[] { "Unpause", "UnpauseContainer", "" });
                    break;
                case "procedure":
                    a.Add(new[] { "Run procedure", "RunProcedure", "ask" });
                    break;
            }
            return a;
        }

        /// <summary>"Restart stack media?" - the question asked before an action.</summary>
        public static string Question(KoItem it, string label, string service)
        {
            string what = service != null ? "service " + service + " of stack " + it.Name : it.Kind + " " + it.Name;
            string q = label + " " + what + (it.ServerName.Length > 0 && it.Kind != "procedure" ? " on " + it.ServerName : "") + "?";
            if (label.StartsWith("Destroy")) q += "\n\nThe container" + (it.Kind == "stack" && service == null ? "s are" : " is") + " removed (volumes stay). Deploy brings it back.";
            return q;
        }

        /// <summary>Run an action. service = one service of a stack (null = the whole stack). Returns null when Komodo took it, else why not.</summary>
        public static Task<string> RunAsync(KoItem it, string request, string service)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                try
                {
                    string p;
                    switch (it.Kind)
                    {
                        case "stack":
                            p = "{\"stack\":" + J(it.Id) + (service != null ? ",\"services\":[" + J(service) + "]" : "") + "}";
                            break;
                        case "deployment": p = "{\"deployment\":" + J(it.Id) + "}"; break;
                        case "container": p = "{\"server\":" + J(it.ServerId) + ",\"container\":" + J(it.Name) + "}"; break;
                        case "procedure": p = "{\"procedure\":" + J(it.Id) + "}"; lock (_myRuns) _myRuns.Add(it.Id); break;
                        default: return "Nothing to do.";
                    }
                    if (request.StartsWith("Stop") || request.StartsWith("Destroy") || request.StartsWith("Pause"))
                        lock (_stopping) _stopping[it.Key] = DateTime.UtcNow.AddMinutes(5);
                    // older Komodo: StartContainer was StartDockerContainer etc.
                    try { Call(s, "execute", request, p); }
                    catch (KoException ex) when (it.Kind == "container" && (ex.Code == 400 || ex.Code == 404 || ex.Code == 422))
                    {
                        Call(s, "execute", request.Replace("Container", "DockerContainer"), p);
                    }
                    _lastPoll = DateTime.UtcNow.AddSeconds(-3);      // look again in a couple of seconds
                    return (string)null;
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        /// <summary>
        /// The last lines of a stack's / deployment's / container's log (read-only): GetStackLog (all services, or one), GetDeploymentLog,
        /// GetContainerLog (older Komodo: GetDockerContainerLog). Komodo answers with a Log: stdout and stderr, joined here (stderr after).
        /// Returns the text, or throws with Komodo's reason.
        /// </summary>
        public static Task<string> LogAsync(KoItem it, string service, int tail, bool timestamps)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                string extra = ",\"tail\":" + Math.Max(10, Math.Min(5000, tail)) + ",\"timestamps\":" + (timestamps ? "true" : "false");
                string json;
                switch (it.Kind)
                {
                    case "stack":
                        json = Call(s, "read", "GetStackLog", "{\"stack\":" + J(it.Id) + ",\"services\":[" + (string.IsNullOrEmpty(service) ? "" : J(service)) + "]" + extra + "}");
                        break;
                    case "deployment":
                        json = Call(s, "read", "GetDeploymentLog", "{\"deployment\":" + J(it.Id) + extra + "}");
                        break;
                    case "container":
                        {
                            string p = "{\"server\":" + J(it.ServerId) + ",\"container\":" + J(it.Name) + extra + "}";
                            try { json = Call(s, "read", "GetContainerLog", p); }
                            catch (KoException ex) when (ex.Code == 400 || ex.Code == 404 || ex.Code == 422) { json = Call(s, "read", "GetDockerContainerLog", p); }
                            break;
                        }
                    default: throw new InvalidOperationException("A " + it.Kind + " has no log.");
                }
                return LogText(json);
            });
        }

        /// <summary>A Komodo Log ({stdout, stderr, ...}) as plain text.</summary>
        public static string LogText(string json)
        {
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement r = d.RootElement;
                if (r.ValueKind == JsonValueKind.String) return r.GetString() ?? "";
                string o = Str(r, "stdout"), e = Str(r, "stderr");
                if (o.Length > 0 && e.Length > 0) return o.TrimEnd('\n') + "\n" + e;
                return o.Length > 0 ? o : e;
            }
        }

        /// <summary>A stack's services with their containers' state (asked for when the stack is opened in the list).</summary>
        public static Task<List<KoService>> ServicesAsync(KoItem stack)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                var list = new List<KoService>();
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(Call(s, "read", "ListStackServices", "{\"stack\":" + J(stack.Id) + "}")))
                        if (d.RootElement.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in d.RootElement.EnumerateArray())
                            {
                                JsonElement c = Obj(e, "container");
                                var sv = new KoService
                                {
                                    Service = Str(e, "service"), Image = Str(e, "image"), Update = Bool(e, "update_available"),
                                    State = Norm(Str(c, "state")), Status = Str(c, "status"), Container = Str(c, "name")
                                };
                                if (sv.State == "") sv.State = c.ValueKind == JsonValueKind.Object ? "unknown" : "down";
                                if (sv.Status.IndexOf("unhealthy", StringComparison.OrdinalIgnoreCase) >= 0 && sv.State == "running") sv.State = "unhealthy";
                                // newer Komodo keeps "update available" on the stack's list entry only
                                KoService known = stack.Services.Find(delegate (KoService x) { return x.Service == sv.Service; });
                                if (known != null && known.Update) sv.Update = true;
                                list.Add(sv);
                            }
                }
                catch (Exception ex) { Log.Write("Komodo services of " + stack.Name + ": " + ex.Message); }
                return list;
            });
        }

        public static Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            return Task.Run(delegate
            {
                try
                {
                    if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address, the API key and the secret first.");
                    _httpKey = "";
                    string ver = "";
                    try { using (JsonDocument d = JsonDocument.Parse(Call(s, "read", "GetVersion", "{}"))) ver = Str(d.RootElement, "version"); } catch (KoException ex) when (ex.Code != 401 && ex.Code != 403) { }
                    KoSnapshot k = Read(s);
                    lock (Gate) _snap = k;
                    Fire(Changed);
                    int running = k.Containers.FindAll(delegate (KoItem i) { return i.Running; }).Count;
                    return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - Komodo " + ver : "") + ": " + k.Servers.Count + " servers, " + k.Stacks.Count + " stacks, " +
                                                                k.Deployments.Count + " deployments, " + running + " of " + k.Containers.Count + " containers running, " + k.Procedures.Count + " procedures.");
                }
                catch (Exception ex) { return new KeyValuePair<bool, string>(false, ex.Message); }
            });
        }

        public static string Ago(long msSince1970)
        {
            if (msSince1970 <= 0) return "never";
            return TimeText.Ago(DateTimeOffset.FromUnixTimeMilliseconds(msSince1970).UtcDateTime);
        }
    }
}
