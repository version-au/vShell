using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>One download: a torrent in qBittorrent or a job in SABnzbd's queue.</summary>
    public sealed class DlItem
    {
        public string Client = "";        // "qBittorrent" / "SABnzbd"
        public string Id = "";            // torrent hash / nzo_id
        public string Name = "";
        public string Category = "";
        /// <summary>0..1</summary>
        public double Progress;
        public double Size, Left;         // bytes
        public double DownSpeed, UpSpeed; // bytes per second (SABnzbd: only the job being downloaded has the queue's speed)
        /// <summary>Seconds to go (-1 = not known).</summary>
        public long Eta = -1;
        /// <summary>The client's own state word (downloading, stalledDL, uploading, pausedDL, Queued, Downloading, Paused ...).</summary>
        public string State = "";
        public bool Paused, Done, Active, Error;
        public int Seeds, Peers;
        public double Ratio;
        public long Added;                // unix seconds (qBittorrent)

        /// <summary>A short word for the state, for the list.</summary>
        public string StateText
        {
            get
            {
                if (Error) return "error";
                if (Paused) return Done ? "done" : "paused";
                if (Done) return Client == "qBittorrent" ? (UpSpeed > 0 ? "seeding" : "done") : "done";
                string s = State.ToLowerInvariant();
                if (s.StartsWith("stalled")) return "stalled";
                if (s.StartsWith("queued") || s == "queued") return "queued";
                if (s.StartsWith("checking") || s.StartsWith("metadl")) return "checking";
                if (s.StartsWith("forced")) return "forced";
                if (s == "grabbing" || s == "fetching") return "fetching";
                return "downloading";
            }
        }
    }

    /// <summary>One client's state at one moment.</summary>
    public sealed class DlClientState
    {
        public string Client = "";
        public bool Configured;
        /// <summary>Has answered at least once.</summary>
        public bool Ok;
        public string Error = "";
        public double DownSpeed, UpSpeed;      // bytes / s
        /// <summary>Everything paused (SABnzbd's queue pause; qBittorrent: every unfinished torrent paused).</summary>
        public bool Paused;
        /// <summary>qBittorrent's alternative speed limits are on.</summary>
        public bool AltSpeed;
        /// <summary>SABnzbd: the time it is paused for (empty = not / no timer).</summary>
        public string PausedFor = "";
        /// <summary>Free space where it downloads to (bytes, -1 = not known).</summary>
        public double FreeSpace = -1;
        public readonly List<DlItem> Items = new List<DlItem>();

        public List<DlItem> Unfinished { get { return Items.FindAll(delegate (DlItem i) { return !i.Done; }); } }
    }

    public sealed class DlSnapshot
    {
        public DlClientState Qb = new DlClientState { Client = "qBittorrent" };
        public DlClientState Sab = new DlClientState { Client = "SABnzbd" };
        public DateTime Fetched = DateTime.MinValue;

        public IEnumerable<DlClientState> Clients(string source)
        {
            if (source != "SABnzbd" && Qb.Configured) yield return Qb;
            if (source != "qBittorrent" && Sab.Configured) yield return Sab;
        }
    }

    /// <summary>qBittorrent, SABnzbd and the download notifications - kept in vShell's settings under Integrations["downloads"].</summary>
    public sealed class DlSettings
    {
        readonly SettingsBag _b;
        public DlSettings(BarSettings s) { _b = s.Integration("downloads"); }
        public static DlSettings Of(BarSettings s) { return new DlSettings(s); }

        public string QbUrl { get { return _b.Get("QbUrl", ""); } set { _b.Set("QbUrl", (value ?? "").Trim()); } }
        /// <summary>Empty = no login (the server lets this address in without one).</summary>
        public string QbUser { get { return _b.Get("QbUser", ""); } set { _b.Set("QbUser", (value ?? "").Trim()); } }
        /// <summary>Stored encrypted for the current Windows user (QbPasswordPlain is the real one).</summary>
        public string QbPassword { get { return _b.Get("QbPassword", ""); } set { _b.Set("QbPassword", Secret.Protect(value ?? "")); } }
        public string QbPasswordPlain { get { return _b.GetSecret("QbPassword"); } }
        public bool QbIgnoreCert { get { return _b.Get("QbIgnoreCert", false); } set { _b.Set("QbIgnoreCert", value); } }
        public string SabUrl { get { return _b.Get("SabUrl", ""); } set { _b.Set("SabUrl", (value ?? "").Trim()); } }
        /// <summary>SABnzbd: Config > General > API Key; stored encrypted (SabApiKeyPlain is the real one).</summary>
        public string SabApiKey { get { return _b.Get("SabApiKey", ""); } set { _b.Set("SabApiKey", Secret.Protect(value ?? "")); } }
        public string SabApiKeyPlain { get { return _b.GetSecret("SabApiKey"); } }
        public bool SabIgnoreCert { get { return _b.Get("SabIgnoreCert", false); } set { _b.Set("SabIgnoreCert", value); } }
        /// <summary>A notice when a download finishes / fails.</summary>
        public bool Notify { get { return _b.Get("Notify", true); } set { _b.Set("Notify", value); } }
        public bool NotifyFailed { get { return _b.Get("NotifyFailed", true); } set { _b.Set("NotifyFailed", value); } }
        /// <summary>Only notice finished downloads in these categories (comma separated; empty = all).</summary>
        public string NotifyCategories { get { return _b.Get("NotifyCategories", ""); } set { _b.Set("NotifyCategories", value ?? ""); } }
        /// <summary>Run box keywords (without the !).</summary>
        public string QbBang { get { return Bang("QbBang", "qb"); } set { _b.Set("QbBang", value ?? ""); } }
        public string SabBang { get { return Bang("SabBang", "sab"); } set { _b.Set("SabBang", value ?? ""); } }

        string Bang(string key, string def)
        {
            string v = _b.Get(key, def).Trim().TrimStart('!').ToLowerInvariant();
            return v.Length == 0 ? def : v;
        }
    }

    /// <summary>A Downloads module's settings - kept in its ModuleConfig.Extra (the graph's style, size, colours and scale are vShell's own).</summary>
    public sealed class DlView
    {
        /// <summary>Which clients a module shows.</summary>
        public static readonly string[] Sources = { "Both", "qBittorrent", "SABnzbd" };
        /// <summary>In a widget: the list of downloads, or the bar look (text / graph / ring / tile).</summary>
        public static readonly string[] Views = { "List", "Graph" };
        public static readonly string[] Shows = { "Active", "Unfinished", "All" };

        readonly SettingsBag _b;
        public DlView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static DlView Of(ModuleConfig cfg) { return new DlView(cfg); }

        /// <summary>"Both", "qBittorrent" or "SABnzbd".</summary>
        public string Source { get { return Pick("Source", Sources); } set { _b.Set("Source", value); } }
        /// <summary>In a widget: "List" (the downloads) or "Graph" (the same look as on a bar).</summary>
        public string View { get { return Pick("View", Views); } set { _b.Set("View", value); } }
        /// <summary>The list: "Active" (downloading), "Unfinished" (also queued / paused) or "All" (also finished / seeding).</summary>
        public string Show { get { string v = _b.Get("Show", "Unfinished"); return Array.IndexOf(Shows, v) < 0 ? "Unfinished" : v; } set { _b.Set("Show", value); } }
        /// <summary>How many downloads the list shows per client (the rest: "+ N more").</summary>
        public int Count { get { return Math.Max(1, Math.Min(30, _b.Get("Count", 5))); } set { _b.Set("Count", value); } }
        /// <summary>Bar: show the number of downloads and the overall progress after the speeds.</summary>
        public bool ShowCount { get { return _b.Get("ShowCount", true); } set { _b.Set("ShowCount", value); } }
        /// <summary>Bar: also the upload speed (qBittorrent).</summary>
        public bool ShowUpload { get { return _b.Get("ShowUpload", true); } set { _b.Set("ShowUpload", value); } }

        string Pick(string key, string[] from) { string v = _b.Get(key, from[0]); return Array.IndexOf(from, v) < 0 ? from[0] : v; }
    }
    /// <summary>
    /// qBittorrent (Web UI API v2) and SABnzbd (its API key) on a server: speeds, the queues, pause / resume, adding a magnet /
    /// .torrent / .nzb, and a notice when something finishes or fails. One background timer polls every 2 s while a module shows
    /// it (every 30 s otherwise, for the finished notices), slower when a client can't be reached.
    /// </summary>
    public static class Downloads
    {
        static readonly object Gate = new object();
        static DlSnapshot _snap = new DlSnapshot();
        static Timer _timer;
        static BarSettings _s;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue, _lastPoll = DateTime.MinValue;

        /// <summary>Something finished (client, name, category) or failed (client, name, why) - raised on the timer thread.</summary>
        public static event Action<string, string, string> Finished;
        public static event Action<string, string, string> Failed;
        /// <summary>A new snapshot arrived (timer thread).</summary>
        public static event Action Changed;

        public static bool QbConfigured(BarSettings s) { return s != null && !string.IsNullOrWhiteSpace(DlSettings.Of(s).QbUrl); }
        public static bool SabConfigured(BarSettings s) { if (s == null) return false; DlSettings d = DlSettings.Of(s); return !string.IsNullOrWhiteSpace(d.SabUrl) && !string.IsNullOrEmpty(d.SabApiKeyPlain); }

        /// <summary>Start (or re-read the settings). Called by the manager when settings change.</summary>
        public static void Configure(BarSettings s)
        {
            _s = s;
            lock (Gate)
            {
                if (_timer == null && (QbConfigured(s) || SabConfigured(s)))
                    _timer = new Timer(delegate { Tick(); }, null, 500, 2000);
            }
            _qbLoggedIn = false;
            Poke();
        }

        /// <summary>The plugin was removed: stop polling and forget what was read (Configure starts it again).</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                _snap = new DlSnapshot();
            }
            _s = null;
        }

        /// <summary>"torrent" for a magnet link or an http(s) address of a .torrent file, "nzb" for an .nzb address, otherwise "".</summary>
        public static string LinkKind(string t)
        {
            t = (t ?? "").Trim();
            if (t.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return "torrent";
            if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "";
            try
            {
                string path = new Uri(t).AbsolutePath;
                if (path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return "torrent";
                if (path.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)) return "nzb";
            }
            catch { }
            return "";
        }

        /// <summary>A finished download's category is one to notify about (list = comma separated; empty = all).</summary>
        public static bool CategoryWanted(string list, string cat)
        {
            if (string.IsNullOrWhiteSpace(list)) return true;
            foreach (string c in list.Split(','))
                if (string.Equals(c.Trim(), cat ?? "", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The client's web page (for a notification's click).</summary>
        public static string ClientUrl(BarSettings s, string client)
        {
            DlSettings d = DlSettings.Of(s);
            string url = (client == "SABnzbd" ? d.SabUrl : d.QbUrl).Trim();
            if (url.Length > 0 && !url.Contains("://")) url = "http://" + url;
            return url;
        }

        /// <summary>The latest snapshot. Reading it keeps the fast polling on.</summary>
        public static DlSnapshot Latest
        {
            get
            {
                _wanted = DateTime.UtcNow;
                lock (Gate) return _snap;
            }
        }

        /// <summary>Poll straight away (after an action).</summary>
        public static void Poke()
        {
            _lastPoll = DateTime.MinValue;
            Timer t = _timer;
            if (t != null) try { t.Change(250, 2000); } catch { }
        }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 10;
                DlSettings d = DlSettings.Of(s);
                bool notify = d.Notify || d.NotifyFailed;
                if (!shown && !notify) return;
                double every = shown ? 1.8 : 30;
                if ((DateTime.UtcNow - _lastPoll).TotalSeconds < every) return;
                _lastPoll = DateTime.UtcNow;
                Poll(s);
            }
            catch (Exception ex) { Log.Write("Downloads: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static DateTime _qbRetry = DateTime.MinValue, _sabRetry = DateTime.MinValue;

        static void Poll(BarSettings s)
        {
            DlSnapshot old;
            lock (Gate) old = _snap;
            var snap = new DlSnapshot { Fetched = DateTime.UtcNow };

            snap.Qb.Configured = QbConfigured(s);
            if (snap.Qb.Configured)
            {
                if (DateTime.UtcNow < _qbRetry) { CopyState(old.Qb, snap.Qb); }
                else
                {
                    try { QbRead(s, snap.Qb); snap.Qb.Ok = true; }
                    catch (Exception ex)
                    {
                        CopyState(old.Qb, snap.Qb);
                        if (snap.Qb.Error != ex.Message) Log.Write("qBittorrent: " + ex.Message);
                        snap.Qb.Error = ex.Message;
                        _qbRetry = DateTime.UtcNow.AddSeconds(15);
                    }
                }
            }
            snap.Sab.Configured = SabConfigured(s);
            if (snap.Sab.Configured)
            {
                if (DateTime.UtcNow < _sabRetry) { CopyState(old.Sab, snap.Sab); }
                else
                {
                    try { SabRead(s, snap.Sab); snap.Sab.Ok = true; }
                    catch (Exception ex)
                    {
                        CopyState(old.Sab, snap.Sab);
                        if (snap.Sab.Error != ex.Message) Log.Write("SABnzbd: " + ex.Message);
                        snap.Sab.Error = ex.Message;
                        _sabRetry = DateTime.UtcNow.AddSeconds(15);
                    }
                }
            }
            lock (Gate) _snap = snap;
            if (snap.Qb.Ok && snap.Qb.Error.Length == 0) CheckQbFinished(snap.Qb, s);
            var c = Changed;
            if (c != null) try { c(); } catch { }
        }

        static void CopyState(DlClientState from, DlClientState to)
        {
            to.Ok = from.Ok; to.Error = from.Error; to.Paused = from.Paused; to.AltSpeed = from.AltSpeed; to.FreeSpace = from.FreeSpace; to.PausedFor = from.PausedFor;
            to.Items.AddRange(from.Items);
        }

        // ---- shared JSON helpers ----------------------------------------------------------------------
        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return "";
        }

        static double Num(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return 0;
            double d;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out d)) return d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return false;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.String) return v.GetString() == "True" || v.GetString() == "true" || v.GetString() == "1";
            return false;
        }

        static string Base(string url)
        {
            string b = (url ?? "").Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        // =================================================================
        // qBittorrent
        // =================================================================
        static HttpClient _qb;
        static CookieContainer _qbCookies;
        static string _qbKey = "";
        static bool _qbLoggedIn;
        /// <summary>Web API 2.11+ (qBittorrent 5) renamed pause / resume to stop / start.</summary>
        static bool _qbStopStart;
        static bool _qbVersionKnown;

        static HttpClient Qb(BarSettings s, out string b)
        {
            DlSettings d = DlSettings.Of(s);
            b = Base(d.QbUrl);
            string key = b + "|" + d.QbUser + "|" + d.QbPassword + "|" + d.QbIgnoreCert;
            if (_qb == null || key != _qbKey)
            {
                _qbCookies = new CookieContainer();
                var handler = new HttpClientHandler { CookieContainer = _qbCookies, UseCookies = true };
                if (d.QbIgnoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                // qBittorrent checks that requests come from its own page (CSRF protection): say we do
                try { h.DefaultRequestHeaders.Referrer = new Uri(b + "/"); } catch { }
                h.DefaultRequestHeaders.TryAddWithoutValidation("Origin", b);
                var old = _qb;
                _qb = h; _qbKey = key; _qbLoggedIn = false; _qbVersionKnown = false;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return _qb;
        }

        static void QbLogin(BarSettings s)
        {
            string b;
            HttpClient h = Qb(s, out b);
            DlSettings d = DlSettings.Of(s);
            if (string.IsNullOrEmpty(d.QbUser)) { _qbLoggedIn = true; return; }       // "bypass authentication" on the server
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", d.QbUser),
                new KeyValuePair<string, string>("password", d.QbPasswordPlain)
            });
            using (HttpResponseMessage r = h.PostAsync(b + "/api/v2/auth/login", form).GetAwaiter().GetResult())
            {
                string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if ((int)r.StatusCode == 403) throw new Exception("qBittorrent refused the login (403) - too many failed logins ban the address for a while");
                if (!r.IsSuccessStatusCode) throw new Exception("qBittorrent answered " + (int)r.StatusCode);
                if (body.Trim().StartsWith("Fails")) throw new Exception("wrong user name or password");
            }
            _qbLoggedIn = true;
        }

        /// <summary>GET (or POST with a form) against the API, logging in first / again when needed.</summary>
        static string QbCall(BarSettings s, string path, IList<KeyValuePair<string, string>> form = null, bool allow404 = false)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!_qbLoggedIn) QbLogin(s);
                string b;
                HttpClient h = Qb(s, out b);
                HttpResponseMessage r = form == null ? h.GetAsync(b + path).GetAwaiter().GetResult()
                                                     : h.PostAsync(b + path, new FormUrlEncodedContent(form)).GetAwaiter().GetResult();
                using (r)
                {
                    string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    int code = (int)r.StatusCode;
                    if (code == 403 && attempt == 0) { _qbLoggedIn = false; continue; }     // the session ran out: log in again
                    if (code == 403) throw new Exception("qBittorrent refused vShell (403) - check the user name and password");
                    if (code == 404 && allow404) return null;
                    if (!r.IsSuccessStatusCode) throw new Exception("qBittorrent answered " + code);
                    return body;
                }
            }
            return null;
        }

        static void QbVersion(BarSettings s)
        {
            if (_qbVersionKnown) return;
            string v = (QbCall(s, "/api/v2/app/webapiVersion") ?? "").Trim();
            var parts = v.Split('.');
            int major, minor;
            if (parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor))
                _qbStopStart = major > 2 || (major == 2 && minor >= 11);
            _qbVersionKnown = true;
        }

        static void QbRead(BarSettings s, DlClientState st)
        {
            QbVersion(s);
            string info = QbCall(s, "/api/v2/transfer/info");
            using (JsonDocument d = JsonDocument.Parse(info))
            {
                st.DownSpeed = Num(d.RootElement, "dl_info_speed");
                st.UpSpeed = Num(d.RootElement, "up_info_speed");
            }
            try { st.AltSpeed = (QbCall(s, "/api/v2/transfer/speedLimitsMode") ?? "").Trim() == "1"; } catch { }
            string list = QbCall(s, "/api/v2/torrents/info?sort=added_on&reverse=true");
            using (JsonDocument d = JsonDocument.Parse(list))
            {
                if (d.RootElement.ValueKind != JsonValueKind.Array) return;
                foreach (JsonElement t in d.RootElement.EnumerateArray())
                {
                    string state = Str(t, "state");
                    var it = new DlItem
                    {
                        Client = "qBittorrent", Id = Str(t, "hash"), Name = Str(t, "name"), Category = Str(t, "category"), State = state,
                        Progress = Num(t, "progress"), Size = Num(t, "size"), Left = Num(t, "amount_left"),
                        DownSpeed = Num(t, "dlspeed"), UpSpeed = Num(t, "upspeed"), Seeds = (int)Num(t, "num_seeds"), Peers = (int)Num(t, "num_leechs"),
                        Ratio = Num(t, "ratio"), Added = (long)Num(t, "added_on")
                    };
                    long eta = (long)Num(t, "eta");
                    it.Eta = eta >= 8640000 || eta < 0 ? -1 : eta;      // 8640000 = "infinity"
                    it.Paused = state.StartsWith("paused") || state.StartsWith("stopped");
                    it.Error = state == "error" || state == "missingFiles";
                    it.Done = it.Progress >= 0.9999 || state.EndsWith("UP") || state == "uploading";
                    it.Active = !it.Paused && !it.Done && !state.StartsWith("queued");
                    st.Items.Add(it);
                }
            }
            List<DlItem> open = st.Unfinished;
            st.Paused = open.Count > 0 && open.TrueForAll(delegate (DlItem i) { return i.Paused; });
            // the free space only comes with maindata, which lists every torrent again: once a minute is plenty
            if ((DateTime.UtcNow - _qbFreeAt).TotalSeconds > 60)
            {
                _qbFreeAt = DateTime.UtcNow;
                try
                {
                    string free = QbCall(s, "/api/v2/sync/maindata?rid=0", null, true);
                    if (free != null)
                        using (JsonDocument d = JsonDocument.Parse(free))
                        {
                            JsonElement ss;
                            if (d.RootElement.TryGetProperty("server_state", out ss)) _qbFree = Num(ss, "free_space_on_disk");
                        }
                }
                catch { }
            }
            st.FreeSpace = _qbFree > 0 ? _qbFree : -1;
        }

        static DateTime _qbFreeAt = DateTime.MinValue;
        static double _qbFree = -1;

        // the torrents that were still downloading last time (to notice the moment one finishes)
        static HashSet<string> _qbOpen;

        static void CheckQbFinished(DlClientState st, BarSettings s)
        {
            var now = new HashSet<string>();
            foreach (DlItem i in st.Items) if (!i.Done && !i.Error) now.Add(i.Id);
            if (_qbOpen != null)
                foreach (DlItem i in st.Items)
                {
                    if (!_qbOpen.Contains(i.Id)) continue;
                    if (i.Done) Raise(Finished, "qBittorrent", i.Name, i.Category);
                    else if (i.Error) Raise(Failed, "qBittorrent", i.Name, "error: " + i.State);
                }
            _qbOpen = now;
        }

        static void Raise(Action<string, string, string> ev, string a, string b, string c)
        {
            if (ev != null) try { ev(a, b, c); } catch (Exception ex) { Log.Write("Downloads notice: " + ex.Message); }
        }

        static string Hashes(DlItem it) { return it == null ? "all" : it.Id; }

        /// <summary>Pause (pause = true) or resume one torrent, or all of them (item null).</summary>
        public static void QbPause(DlItem it, bool pause)
        {
            BarSettings s = _s;
            Task.Run(delegate
            {
                try
                {
                    QbVersion(s);
                    var form = new[] { new KeyValuePair<string, string>("hashes", Hashes(it)) };
                    string verb = pause ? (_qbStopStart ? "stop" : "pause") : (_qbStopStart ? "start" : "resume");
                    if (QbCall(s, "/api/v2/torrents/" + verb, form, true) == null)
                    {
                        // guessed the API wrong: the other name
                        _qbStopStart = !_qbStopStart;
                        verb = pause ? (_qbStopStart ? "stop" : "pause") : (_qbStopStart ? "start" : "resume");
                        QbCall(s, "/api/v2/torrents/" + verb, form);
                    }
                }
                catch (Exception ex) { Log.Write("qBittorrent pause: " + ex.Message); }
                Poke();
            });
        }

        /// <summary>A torrent forced to start (it ignores the queue limits): qBittorrent's forcedDL / forcedUP / forcedMetaDL.</summary>
        public static bool Forced(DlItem it) { return it != null && it.State.StartsWith("forced", StringComparison.OrdinalIgnoreCase); }

        /// <summary>
        /// qBittorrent, one torrent: "forcestart" (on, or off again when it's forced), "recheck" (check the data on disk), "reannounce"
        /// (ask the trackers for peers now), "top" (top of the queue), "remove" (keep the files) or "removefiles" (delete them too).
        /// Null when it worked, otherwise what went wrong.
        /// </summary>
        public static Task<string> QbActionAsync(DlItem it, string action)
        {
            BarSettings s = _s;
            return Task.Run(delegate
            {
                try
                {
                    var hashes = new KeyValuePair<string, string>("hashes", it.Id);
                    switch (action)
                    {
                        case "forcestart": QbCall(s, "/api/v2/torrents/setForceStart", new[] { hashes, new KeyValuePair<string, string>("value", Forced(it) ? "false" : "true") }); break;
                        case "recheck": QbCall(s, "/api/v2/torrents/recheck", new[] { hashes }); break;
                        case "reannounce": QbCall(s, "/api/v2/torrents/reannounce", new[] { hashes }); break;
                        case "top": QbCall(s, "/api/v2/torrents/topPrio", new[] { hashes }); break;
                        case "remove":
                        case "removefiles":
                            QbCall(s, "/api/v2/torrents/delete", new[] { hashes, new KeyValuePair<string, string>("deleteFiles", action == "removefiles" ? "true" : "false") });
                            break;
                        default: return "Unknown action " + action;
                    }
                    Poke();
                    return (string)null;
                }
                catch (Exception ex)
                {
                    Log.Write("qBittorrent " + action + ": " + ex.Message);
                    // topPrio answers 409 when queueing is off
                    if (action == "top" && ex.Message.Contains("409")) return "qBittorrent's torrent queueing is off (Options > BitTorrent > Torrent Queueing), so there's no queue to move it in.";
                    return ex.Message;
                }
            });
        }

        public static void QbToggleAltSpeed()
        {
            BarSettings s = _s;
            Task.Run(delegate
            {
                try { QbCall(s, "/api/v2/transfer/toggleSpeedLimitsMode", new KeyValuePair<string, string>[0]); }
                catch (Exception ex) { Log.Write("qBittorrent speed limits: " + ex.Message); }
                Poke();
            });
        }

        /// <summary>Add a magnet link or a .torrent address. Returns null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> QbAddAsync(string url)
        {
            BarSettings s = _s;
            if (!QbConfigured(s)) return "Set up qBittorrent in Settings > Plugins first.";
            return await Task.Run(delegate
            {
                try
                {
                    string r = QbCall(s, "/api/v2/torrents/add", new[] { new KeyValuePair<string, string>("urls", url) });
                    Poke();
                    if (r != null && r.Trim().StartsWith("Fails")) return "qBittorrent didn't accept it.";
                    return (string)null;
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        /// <summary>
        /// form-data; name="x"; filename="y" with the quotes: .NET leaves them out, and some servers' form readers then miss the file.
        /// </summary>
        static void Quoted(HttpContent part, string field, string fileName)
        {
            string safe = (fileName ?? "file").Replace("\"", "'").Replace("\r", "").Replace("\n", "");
            part.Headers.TryAddWithoutValidation("Content-Disposition", "form-data; name=\"" + field + "\"; filename=\"" + safe + "\"");
        }

        /// <summary>Upload a .torrent file from this PC. Returns null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> QbAddFileAsync(string path)
        {
            BarSettings s = _s;
            if (!QbConfigured(s)) return "Set up qBittorrent in Settings > Plugins first.";
            return await Task.Run(delegate
            {
                try
                {
                    byte[] data = System.IO.File.ReadAllBytes(path);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        if (!_qbLoggedIn) QbLogin(s);
                        string b;
                        HttpClient h = Qb(s, out b);
                        using (var form = new MultipartFormDataContent())
                        {
                            var file = new ByteArrayContent(data);
                            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
                            Quoted(file, "torrents", System.IO.Path.GetFileName(path));
                            form.Add(file);
                            using (HttpResponseMessage r = h.PostAsync(b + "/api/v2/torrents/add", form).GetAwaiter().GetResult())
                            {
                                string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                if ((int)r.StatusCode == 403 && attempt == 0) { _qbLoggedIn = false; continue; }
                                if ((int)r.StatusCode == 415) return "qBittorrent says it isn't a valid torrent file.";
                                if (!r.IsSuccessStatusCode) return "qBittorrent answered " + (int)r.StatusCode;
                                if (body.Trim().StartsWith("Fails")) return "qBittorrent didn't accept it (already added?).";
                                Poke();
                                return (string)null;
                            }
                        }
                    }
                    return "qBittorrent refused vShell (403)";
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        // =================================================================
        // SABnzbd
        // =================================================================
        static HttpClient _sab;
        static string _sabKey = "";

        static HttpClient Sab(BarSettings s)
        {
            DlSettings d = DlSettings.Of(s);
            string key = Base(d.SabUrl) + "|" + d.SabIgnoreCert;
            if (_sab == null || key != _sabKey)
            {
                var handler = new HttpClientHandler();
                if (d.SabIgnoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                var old = _sab;
                _sab = h; _sabKey = key;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return _sab;
        }

        static string SabCall(BarSettings s, string query)
        {
            HttpClient h = Sab(s);
            DlSettings d = DlSettings.Of(s);
            string url = Base(d.SabUrl) + "/api?" + query + "&output=json&apikey=" + Uri.EscapeDataString(d.SabApiKeyPlain);
            using (HttpResponseMessage r = h.GetAsync(url).GetAwaiter().GetResult())
            {
                string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new Exception("SABnzbd answered " + (int)r.StatusCode);
                if (body.Contains("\"error\"") && body.IndexOf("API Key", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("SABnzbd refused the API key");
                return body;
            }
        }

        /// <summary>"0:12:34" / "1:02:03:04" (days) -> seconds; -1 when not known.</summary>
        static long SabTime(string t)
        {
            if (string.IsNullOrEmpty(t)) return -1;
            long total = 0;
            foreach (string p in t.Split(':'))
            {
                long n;
                if (!long.TryParse(p, out n)) return -1;
                total = total * 60 + n;
            }
            // a days part (d:h:m:s) multiplies by 60 instead of 24 above: fix it
            string[] parts = t.Split(':');
            if (parts.Length == 4)
            {
                long d0, h0, m0, s0;
                if (long.TryParse(parts[0], out d0) && long.TryParse(parts[1], out h0) && long.TryParse(parts[2], out m0) && long.TryParse(parts[3], out s0))
                    total = ((d0 * 24 + h0) * 60 + m0) * 60 + s0;
            }
            return total;
        }

        static HashSet<string> _sabSeen;
        static DateTime _sabHistoryAt = DateTime.MinValue;
        static int _sabLastCount = -1;

        static void SabRead(BarSettings s, DlClientState st)
        {
            string q = SabCall(s, "mode=queue&limit=50");
            using (JsonDocument d = JsonDocument.Parse(q))
            {
                JsonElement queue;
                if (!d.RootElement.TryGetProperty("queue", out queue)) throw new Exception("SABnzbd sent something unexpected");
                st.DownSpeed = Num(queue, "kbpersec") * 1024;
                st.Paused = Bool(queue, "paused");
                st.PausedFor = st.Paused ? Str(queue, "pause_int") : "";
                if (st.PausedFor == "0") st.PausedFor = "";
                double gb = Num(queue, "diskspace1");
                st.FreeSpace = gb > 0 ? gb * 1024 * 1024 * 1024 : -1;
                JsonElement slots;
                bool first = true;
                if (queue.TryGetProperty("slots", out slots) && slots.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement j in slots.EnumerateArray())
                    {
                        string status = Str(j, "status");
                        var it = new DlItem
                        {
                            Client = "SABnzbd", Id = Str(j, "nzo_id"), Name = Str(j, "filename"), Category = Str(j, "cat"), State = status,
                            Progress = Num(j, "percentage") / 100.0, Size = Num(j, "mb") * 1048576, Left = Num(j, "mbleft") * 1048576,
                            Eta = SabTime(Str(j, "timeleft"))
                        };
                        it.Paused = status == "Paused" || st.Paused;
                        it.Active = !it.Paused && (status == "Downloading" || status == "Fetching" || status == "Grabbing");
                        if (it.Active && first) { it.DownSpeed = st.DownSpeed; first = false; }
                        if (it.Eta == 0 && !it.Active) it.Eta = -1;
                        st.Items.Add(it);
                    }
            }

            // the history: what finished / failed. Read when the queue got shorter, and once a minute anyway.
            int count = st.Items.Count;
            bool look = _sabSeen == null || count < _sabLastCount || (DateTime.UtcNow - _sabHistoryAt).TotalSeconds > 60;
            _sabLastCount = count;
            if (!look) return;
            _sabHistoryAt = DateTime.UtcNow;
            string hist = SabCall(s, "mode=history&limit=15");
            using (JsonDocument d = JsonDocument.Parse(hist))
            {
                JsonElement h, slots;
                if (!d.RootElement.TryGetProperty("history", out h) || !h.TryGetProperty("slots", out slots) || slots.ValueKind != JsonValueKind.Array) return;
                bool seeding = _sabSeen == null;
                if (seeding) _sabSeen = new HashSet<string>();
                foreach (JsonElement j in slots.EnumerateArray())
                {
                    string id = Str(j, "nzo_id"), status = Str(j, "status");
                    // still being unpacked / repaired: not done yet - look at it again next time
                    if (status != "Completed" && status != "Failed") continue;
                    if (_sabSeen.Contains(id)) continue;
                    _sabSeen.Add(id);
                    if (seeding) continue;     // the history from before vShell was watching: no notices
                    if (status == "Completed") Raise(Finished, "SABnzbd", Str(j, "name"), Str(j, "category"));
                    else Raise(Failed, "SABnzbd", Str(j, "name"), Str(j, "fail_message"));
                }
            }
        }

        /// <summary>Pause / resume one job, or the whole queue (item null).</summary>
        public static void SabPause(DlItem it, bool pause)
        {
            BarSettings s = _s;
            Task.Run(delegate
            {
                try
                {
                    if (it == null) SabCall(s, "mode=" + (pause ? "pause" : "resume"));
                    else SabCall(s, "mode=queue&name=" + (pause ? "pause" : "resume") + "&value=" + Uri.EscapeDataString(it.Id));
                }
                catch (Exception ex) { Log.Write("SABnzbd pause: " + ex.Message); }
                Poke();
            });
        }

        /// <summary>Add an .nzb address. Returns null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> SabAddAsync(string url)
        {
            BarSettings s = _s;
            if (!SabConfigured(s)) return "Set up SABnzbd in Settings > Plugins first.";
            return await Task.Run(delegate
            {
                try
                {
                    string r = SabCall(s, "mode=addurl&name=" + Uri.EscapeDataString(url));
                    Poke();
                    using (JsonDocument d = JsonDocument.Parse(r))
                        if (d.RootElement.TryGetProperty("status", out JsonElement ok) && ok.ValueKind == JsonValueKind.False) return "SABnzbd didn't accept it.";
                    return (string)null;
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        /// <summary>Upload an .nzb file from this PC. Returns null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> SabAddFileAsync(string path)
        {
            BarSettings s = _s;
            if (!SabConfigured(s)) return "Set up SABnzbd in Settings > Plugins first.";
            return await Task.Run(delegate
            {
                try
                {
                    byte[] data = System.IO.File.ReadAllBytes(path);
                    string name = System.IO.Path.GetFileName(path);
                    string url = Base(DlSettings.Of(s).SabUrl) + "/api?mode=addfile&output=json&nzbname=" + Uri.EscapeDataString(System.IO.Path.GetFileNameWithoutExtension(path)) +
                                 "&apikey=" + Uri.EscapeDataString(DlSettings.Of(s).SabApiKeyPlain);
                    using (var form = new MultipartFormDataContent())
                    {
                        var file = new ByteArrayContent(data);
                        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-nzb");
                        Quoted(file, "name", name);
                        form.Add(file);
                        using (HttpResponseMessage r = Sab(s).PostAsync(url, form).GetAwaiter().GetResult())
                        {
                            string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            if (!r.IsSuccessStatusCode) return "SABnzbd answered " + (int)r.StatusCode;
                            using (JsonDocument d = JsonDocument.Parse(body))
                            {
                                JsonElement ok, err;
                                if (d.RootElement.TryGetProperty("error", out err) && err.ValueKind == JsonValueKind.String) return "SABnzbd: " + err.GetString();
                                if (d.RootElement.TryGetProperty("status", out ok) && ok.ValueKind == JsonValueKind.False) return "SABnzbd didn't accept it.";
                            }
                            Poke();
                            return (string)null;
                        }
                    }
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        /// <summary>Add files dropped on vShell: .torrent -> qBittorrent, .nzb -> SABnzbd. Returns one line per file ("✓ name" / "✕ name: why").</summary>
        public static async Task<List<string>> AddFilesAsync(IEnumerable<string> paths)
        {
            var lines = new List<string>();
            foreach (string p in paths)
            {
                string kind = FileKind(p);
                if (kind.Length == 0) continue;
                string err = kind == "torrent" ? await QbAddFileAsync(p) : await SabAddFileAsync(p);
                string name = System.IO.Path.GetFileName(p);
                lines.Add(err == null ? "✓ " + name + " added to " + (kind == "torrent" ? "qBittorrent" : "SABnzbd") : "✕ " + name + ": " + err);
            }
            return lines;
        }

        /// <summary>"torrent" / "nzb" for a file vShell can hand to a client that is set up, otherwise "".</summary>
        public static string FileKind(string path)
        {
            string ext = (System.IO.Path.GetExtension(path ?? "") ?? "").ToLowerInvariant();
            if (ext == ".torrent" && QbConfigured(_s)) return "torrent";
            if (ext == ".nzb" && SabConfigured(_s)) return "nzb";
            return "";
        }

        /// <summary>Pause or resume through whichever client the item is in (item null + client = everything in that client).</summary>
        public static void Pause(string client, DlItem it, bool pause)
        {
            if (client == "SABnzbd") SabPause(it, pause); else QbPause(it, pause);
        }

        // =================================================================
        // Test connection (settings page)
        // =================================================================
        public static Task<KeyValuePair<bool, string>> TestQbAsync(BarSettings s)
        {
            return Task.Run(delegate
            {
                try
                {
                    if (!QbConfigured(s)) return new KeyValuePair<bool, string>(false, "Enter the address first.");
                    _qbKey = "";        // a fresh login with what is in the boxes now
                    string ver = (QbCall(s, "/api/v2/app/version") ?? "").Trim();
                    string info = QbCall(s, "/api/v2/torrents/info");
                    int n = 0;
                    using (JsonDocument d = JsonDocument.Parse(info)) if (d.RootElement.ValueKind == JsonValueKind.Array) n = d.RootElement.GetArrayLength();
                    _qbRetry = DateTime.MinValue;
                    Poke();
                    return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - qBittorrent " + ver : "") + ", " + n + " torrents.");
                }
                catch (Exception ex) { return new KeyValuePair<bool, string>(false, ex.Message); }
            });
        }

        public static Task<KeyValuePair<bool, string>> TestSabAsync(BarSettings s)
        {
            return Task.Run(delegate
            {
                try
                {
                    if (!SabConfigured(s)) return new KeyValuePair<bool, string>(false, "Enter the address and the API key first.");
                    string ver = "";
                    try { using (JsonDocument d = JsonDocument.Parse(SabCall(s, "mode=version"))) ver = Str(d.RootElement, "version"); } catch { }
                    int n = 0;
                    using (JsonDocument d = JsonDocument.Parse(SabCall(s, "mode=queue&limit=1")))
                        if (d.RootElement.TryGetProperty("queue", out JsonElement q)) n = (int)Num(q, "noofslots_total");
                    _sabRetry = DateTime.MinValue;
                    Poke();
                    return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - SABnzbd " + ver : "") + ", " + n + " in the queue.");
                }
                catch (Exception ex) { return new KeyValuePair<bool, string>(false, ex.Message); }
            });
        }

        // ---- formatting ---------------------------------------------------------------------------------
        public static string Size(double bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < u.Length - 1) { bytes /= 1024; i++; }
            return (bytes >= 100 || i == 0 ? bytes.ToString("0", CultureInfo.CurrentCulture) : bytes.ToString(bytes >= 10 ? "0.0" : "0.00", CultureInfo.CurrentCulture)) + " " + u[i];
        }

        public static string Speed(double bps) { return Size(bps) + "/s"; }

        public static string Eta(long seconds)
        {
            if (seconds < 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m";
            return t.Seconds + "s";
        }
    }
}
