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
    /// <summary>The Radarr connection, adding movies and notifications - kept in vShell's settings under Integrations["radarr"].</summary>
    public sealed class RaSettings
    {
        /// <summary>When a movie added from vShell counts as available (Radarr's minimumAvailability).</summary>
        public static readonly string[] Availability = { "Announced", "In cinemas", "Released" };
        /// <summary>The choice that means "Radarr's first quality profile / root folder".</summary>
        public const string First = "The first one in Radarr";

        readonly SettingsBag _b;
        public RaSettings(BarSettings s) { _b = s.Integration("radarr"); }
        public static RaSettings Of(BarSettings s) { return new RaSettings(s); }

        /// <summary>Address such as http://192.168.1.20:7878 (with the URL base, if Radarr has one: http://nas/radarr)</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>API key (Radarr: Settings > General), stored encrypted for the current Windows user (ApiKeyPlain is the real one).</summary>
        public string ApiKey { get { return _b.Get("ApiKey", ""); } set { _b.Set("ApiKey", Secret.Protect(value ?? "")); } }
        public string ApiKeyPlain { get { return _b.GetSecret("ApiKey"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>The run box keyword (without the !).</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "ra").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "ra" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        /// <summary>Adding a movie: the quality profile's name ("" = the first one in Radarr).</summary>
        public string Profile { get { return _b.Get("Profile", ""); } set { _b.Set("Profile", value == First ? "" : value ?? ""); } }
        /// <summary>Adding a movie: the root folder's path ("" = the first one in Radarr).</summary>
        public string RootFolder { get { return _b.Get("RootFolder", ""); } set { _b.Set("RootFolder", value == First ? "" : value ?? ""); } }
        /// <summary>Adding a movie: Announced, In cinemas or Released.</summary>
        public string MinAvailability
        {
            get { string v = _b.Get("MinAvailability", "Released"); return Array.IndexOf(Availability, v) < 0 ? "Released" : v; }
            set { _b.Set("MinAvailability", value); }
        }
        /// <summary>Adding a movie: Radarr searches for it straight away.</summary>
        public bool SearchOnAdd { get { return _b.Get("SearchOnAdd", true); } set { _b.Set("SearchOnAdd", value); } }

        /// <summary>Notices: a movie was downloaded (imported into the library).</summary>
        public bool NotifyImported { get { return _b.Get("NotifyImported", true); } set { _b.Set("NotifyImported", value); } }
        /// <summary>Notices: Radarr grabbed a release (sent it to the download client).</summary>
        public bool NotifyGrabbed { get { return _b.Get("NotifyGrabbed", false); } set { _b.Set("NotifyGrabbed", value); } }
        /// <summary>Notices: a download failed.</summary>
        public bool NotifyFailed { get { return _b.Get("NotifyFailed", true); } set { _b.Set("NotifyFailed", value); } }
        /// <summary>Notices: a new health warning / error (System > Status in Radarr).</summary>
        public bool NotifyHealth { get { return _b.Get("NotifyHealth", true); } set { _b.Set("NotifyHealth", value); } }

        public bool AnyNotices { get { return NotifyImported || NotifyGrabbed || NotifyFailed || NotifyHealth; } }
    }

    /// <summary>A Radarr module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class RaView
    {
        public static readonly string[] Tabs = { "Queue", "Upcoming", "Recently added", "Missing" };

        readonly SettingsBag _b;
        public RaView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static RaView Of(ModuleConfig cfg) { return new RaView(cfg); }

        /// <summary>The tabs shown (in the usual order, at least one). One = just that view, with no tab strip.</summary>
        public List<string> TabsShown
        {
            get
            {
                List<string> l = _b.Get<List<string>>("TabsShown", null) ?? new List<string>(Tabs);
                var keep = new List<string>();
                foreach (string t in Tabs) if (l.Contains(t)) keep.Add(t);
                if (keep.Count == 0) keep.AddRange(Tabs);
                return keep;
            }
            set { _b.Set("TabsShown", value ?? new List<string>(Tabs)); }
        }
        /// <summary>The view it opens on (one of the tabs shown).</summary>
        public string Tab
        {
            get { string v = _b.Get("Tab", "Queue"); List<string> shown = TabsShown; return shown.Contains(v) ? v : shown[0]; }
            set { _b.Set("Tab", value); }
        }
        /// <summary>How many movies each view shows.</summary>
        public int Count { get { return Math.Max(1, Math.Min(30, _b.Get("Count", 6))); } set { _b.Set("Count", value); } }
        /// <summary>Upcoming: how many days ahead.</summary>
        public int Days { get { return Math.Max(7, Math.Min(120, _b.Get("Days", 30))); } set { _b.Set("Days", value); } }
        public bool Posters { get { return _b.Get("Posters", true); } set { _b.Set("Posters", value); } }
        /// <summary>Bar: the number in the queue as a badge on the icon.</summary>
        public bool Badge { get { return _b.Get("Badge", true); } set { _b.Set("Badge", value); } }
        /// <summary>Bar: how far the downloads are and the time left.</summary>
        public bool BarProgress { get { return _b.Get("BarProgress", true); } set { _b.Set("BarProgress", value); } }
        /// <summary>Bar: hidden while nothing is in the queue (and Radarr is fine).</summary>
        public bool HideIdle { get { return _b.Get("HideIdle", false); } set { _b.Set("HideIdle", value); } }
        /// <summary>Show Radarr's health warnings and errors (System > Status): the line in the widget / popup, the red badge and the tip on a bar.</summary>
        public bool Health { get { return _b.Get("Health", true); } set { _b.Set("Health", value); } }
        /// <summary>The "Radarr" button (opens Radarr) at the top.</summary>
        public bool ShowOpen { get { return _b.Get("ShowOpen", true); } set { _b.Set("ShowOpen", value); } }
    }

    /// <summary>A movie in Radarr (or from a search: Id 0 = not in the library).</summary>
    public sealed class RadarrMovie
    {
        public int Id;
        public int TmdbId;
        public string Title = "";
        public string Year = "";
        public string Slug = "";
        public string Poster = "";
        public string Overview = "";
        public bool HasFile, Monitored, Available;
        /// <summary>Release dates (the day, no time; MinValue = not known).</summary>
        public DateTime InCinemas = DateTime.MinValue, Digital = DateTime.MinValue, Physical = DateTime.MinValue;
        /// <summary>The JSON Radarr gave for it (adding a movie sends it back).</summary>
        public string Raw = "";

        public bool InLibrary { get { return Id > 0; } }
        public string Name { get { return Title + (Year.Length > 0 && Year != "0" ? " (" + Year + ")" : ""); } }

        /// <summary>The next release from today on ("In cinemas 20 Nov", "Digital 3 Dec"), or the last one; "" when there are none.</summary>
        public string ReleaseText(out DateTime when)
        {
            DateTime today = DateTime.Today, best = DateTime.MinValue;
            string what = "";
            Action<DateTime, string> pick = delegate (DateTime d, string w)
            {
                if (d == DateTime.MinValue) return;
                // the soonest one from today on; with none ahead, the latest one
                bool future = d >= today;
                if (best == DateTime.MinValue || (future && (best < today || d < best)) || (!future && best < today && d > best)) { best = d; what = w; }
            };
            pick(InCinemas, "In cinemas");
            pick(Digital, "Digital");
            pick(Physical, "On disc");
            when = best;
            return best == DateTime.MinValue ? "" : what + " " + When(best);
        }

        public static string When(DateTime d)
        {
            DateTime today = DateTime.Today;
            if (d == today) return "today";
            if (d == today.AddDays(1)) return "tomorrow";
            if (d == today.AddDays(-1)) return "yesterday";
            return d.Year == today.Year ? d.ToString("d MMM") : d.ToString("d MMM yyyy");
        }
    }

    /// <summary>Something in Radarr's queue (a download).</summary>
    public sealed class RadarrQueueItem
    {
        public int Id;
        public RadarrMovie Movie = new RadarrMovie();
        /// <summary>The release's name.</summary>
        public string Release = "";
        /// <summary>The download client's state: downloading, paused, queued, completed, delay, warning, failed …</summary>
        public string Status = "";
        /// <summary>Radarr's view: ok, warning, error.</summary>
        public string Tracked = "";
        /// <summary>downloading, importPending, importing, imported, failedPending, failed, ignored …</summary>
        public string State = "";
        public string Message = "";
        public string Client = "";
        public string Quality = "";
        public double Size, Left;
        public TimeSpan TimeLeft = TimeSpan.Zero;

        public double Percent { get { return Size > 0 ? Math.Max(0, Math.Min(100, (Size - Left) / Size * 100)) : 0; } }
        public bool Problem { get { return Tracked == "warning" || Tracked == "error" || Status == "failed" || Status == "warning" || State == "failed" || State == "failedPending"; } }
        public bool Downloading { get { return Status == "downloading" && !Problem; } }

        /// <summary>"45% · 12 min left", "Waiting to import", "Paused" …</summary>
        public string StateText
        {
            get
            {
                if (Problem) return Status == "failed" || State == "failed" || State == "failedPending" ? "Failed" : "Needs a look";
                switch (State)
                {
                    case "importPending": return "Waiting to import";
                    case "importing": return "Importing";
                    case "imported": return "Imported";
                }
                switch (Status)
                {
                    case "downloading":
                        return Math.Floor(Percent) + "%" + (TimeLeft > TimeSpan.Zero ? " · " + Left_(TimeLeft) + " left" : "");
                    case "paused": return "Paused · " + Math.Floor(Percent) + "%";
                    case "queued": return "Queued";
                    case "delay": return "Delayed";
                    case "completed": return "Downloaded";
                    default: return Status.Length > 0 ? char.ToUpperInvariant(Status[0]) + Status.Substring(1) : "";
                }
            }
        }

        public static string Left_(TimeSpan t)
        {
            if (t.TotalDays >= 1) return (int)t.TotalDays + " d " + t.Hours + " h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes + " min";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + " min";
            return "<1 min";
        }
    }

    /// <summary>Something that happened in Radarr (its history).</summary>
    public sealed class RadarrEvent
    {
        public int Id;
        public RadarrMovie Movie = new RadarrMovie();
        /// <summary>grabbed, downloadFolderImported, movieFolderImported, downloadFailed, movieFileDeleted, movieFileRenamed, downloadIgnored</summary>
        public string Kind = "";
        public DateTime Date = DateTime.MinValue;     // UTC
        public string Release = "";
        public string Quality = "";
        public string Message = "";
        public bool Imported { get { return Kind == "downloadFolderImported" || Kind == "movieFolderImported"; } }
    }

    /// <summary>A health warning / error (Radarr: System > Status).</summary>
    public sealed class RadarrHealth
    {
        public string Source = "", Type = "", Message = "", Wiki = "";
        public bool Error { get { return Type == "error"; } }
    }

    public sealed class RadarrSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public string Version = "";
        public List<RadarrQueueItem> Queue = new List<RadarrQueueItem>();
        /// <summary>Released (or coming out) in the next days, soonest first (only fetched while something shows Radarr).</summary>
        public List<RadarrMovie> Upcoming = new List<RadarrMovie>();
        /// <summary>The latest history (grabs, imports, failures …), newest first - Recently added shows the imports.</summary>
        public List<RadarrEvent> Recent = new List<RadarrEvent>();
        /// <summary>Monitored, released, not downloaded (only fetched while something shows Radarr).</summary>
        public List<RadarrMovie> Missing = new List<RadarrMovie>();
        public int MissingTotal;
        /// <summary>Why Missing couldn't be read ("" = it could).</summary>
        public string MissingError = "";
        public List<RadarrHealth> Health = new List<RadarrHealth>();
        public bool Ok { get { return Fetched != DateTime.MinValue; } }
        public int Problems { get { return Queue.FindAll(delegate (RadarrQueueItem q) { return q.Problem; }).Count; } }
    }

    /// <summary>
    /// Talks to Radarr (API v3) with its API key. Polled in the background: every 15 s while a Radarr module (or the run box) shows it,
    /// every 2 minutes otherwise when notices are on. Upcoming and Missing are only read while shown (every 5 minutes).
    /// </summary>
    public static class RadarrClient
    {
        static HttpClient _http;
        static string _key = "", _base = "";
        static bool _ignoreCert;
        static readonly object HttpGate = new object();

        static readonly object Gate = new object();
        static RadarrSnapshot _snap = new RadarrSnapshot();
        static Timer _timer;
        static BarSettings _s;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue, _lastPoll = DateTime.MinValue, _retry = DateTime.MinValue, _slowAt = DateTime.MinValue, _setupAt = DateTime.MinValue;
        static string _configuredFor;
        static int _lastEvent = -1;
        static HashSet<string> _seenHealth;
        static List<string> _profiles = new List<string>(), _folders = new List<string>();

        /// <summary>A notice: (kind key, title line, detail, Radarr page address).</summary>
        public static event Action<string, string, string, string> Notice;
        public static event Action Changed;

        static HttpClient Http(BarSettings s)
        {
            lock (HttpGate)
            {
                RaSettings c = RaSettings.Of(s);
                string b = Base(s);
                string k = c.ApiKeyPlain;
                bool ignoreCert = c.IgnoreCert;
                if (_http == null || b != _base || k != _key || ignoreCert != _ignoreCert)
                {
                    var handler = new HttpClientHandler();
                    if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                    var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                    if (k.Length > 0) h.DefaultRequestHeaders.Add("X-Api-Key", k);
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
            string b = RaSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            RaSettings c = RaSettings.Of(s);
            return !string.IsNullOrWhiteSpace(c.Url) && !string.IsNullOrEmpty(c.ApiKeyPlain);
        }

        static string Api(BarSettings s, string path) { Http(s); return _base + "/api/v3" + path; }

        /// <summary>A page in Radarr: "" = the movies, "activity/queue", "calendar", "wanted/missing", "system/status" …</summary>
        public static string PageUrl(BarSettings s, string page) { return Base(s) + "/" + (page ?? ""); }

        /// <summary>The movie's page in Radarr (Add new with its name when it isn't in the library).</summary>
        public static string MovieUrl(BarSettings s, RadarrMovie m)
        {
            if (m == null) return PageUrl(s, "");
            if (m.InLibrary && m.Slug.Length > 0) return PageUrl(s, "movie/" + Uri.EscapeDataString(m.Slug));
            if (m.InLibrary && m.TmdbId > 0) return PageUrl(s, "movie/" + m.TmdbId);
            return PageUrl(s, "add/new?term=" + Uri.EscapeDataString(m.TmdbId > 0 ? "tmdb:" + m.TmdbId : m.Title));
        }

        /// <summary>The quality profiles' names and root folders' paths Radarr has (read with the polls / Test; empty until then).</summary>
        public static List<string> Profiles { get { lock (Gate) return new List<string>(_profiles); } }
        public static List<string> Folders { get { lock (Gate) return new List<string>(_folders); } }

        // =================================================================
        // Polling
        // =================================================================
        public static void Configure(BarSettings s)
        {
            _s = s;
            RaSettings c = RaSettings.Of(s);
            string id = c.Url.Trim() + "|" + c.ApiKey;
            if (_configuredFor != null && _configuredFor != id)
            {
                // another server / key: start over (no notices for what the new one already has)
                lock (Gate) { _snap = new RadarrSnapshot(); _profiles = new List<string>(); _folders = new List<string>(); }
                _lastEvent = -1;
                _seenHealth = null;
                _slowAt = _setupAt = DateTime.MinValue;
            }
            _configuredFor = id;
            lock (Gate)
                if (_timer == null && Configured(s)) _timer = new Timer(delegate { Tick(); }, null, 900, 3000);
            Poke();
        }

        /// <summary>The plugin was removed: stop polling and forget what was read (Configure starts it again).</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                _snap = new RadarrSnapshot();
            }
            _s = null;
            _configuredFor = null;
            _lastEvent = -1;
            _seenHealth = null;
            _slowAt = _setupAt = DateTime.MinValue;
        }

        /// <summary>The latest snapshot (asking for it keeps the 15-second polling going).</summary>
        public static RadarrSnapshot Latest
        {
            get
            {
                bool was = (DateTime.UtcNow - _wanted).TotalSeconds < 40;
                _wanted = DateTime.UtcNow;
                if (_timer == null && _s != null && Configured(_s)) Configure(_s);
                else if (!was) Poke();     // shown again after a while: fresh now, with Upcoming / Missing
                lock (Gate) return _snap;
            }
        }

        public static void Poke()
        {
            _lastPoll = DateTime.MinValue;
            _retry = DateTime.MinValue;
            Timer t = _timer;
            if (t != null) try { t.Change(300, 3000); } catch { }
        }

        /// <summary>Upcoming / Missing again on the next poll (after adding or searching).</summary>
        public static void Refresh() { _slowAt = DateTime.MinValue; Poke(); }

        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null || !Configured(s)) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 40;
                if (!shown && !RaSettings.Of(s).AnyNotices) return;
                if (DateTime.UtcNow < _retry) return;
                if ((DateTime.UtcNow - _lastPoll).TotalSeconds < (shown ? 14 : 120)) return;
                _lastPoll = DateTime.UtcNow;
                RadarrSnapshot old;
                lock (Gate) old = _snap;
                RadarrSnapshot snap;
                try { snap = ReadAll(s, shown, old); }
                catch (Exception ex)
                {
                    string msg = Why(ex);
                    if (old.Error != msg) Log.Write("Radarr: " + msg);
                    var keep = new RadarrSnapshot
                    {
                        Error = msg, Fetched = old.Fetched, Version = old.Version, Queue = old.Queue, Upcoming = old.Upcoming, Recent = old.Recent,
                        Missing = old.Missing, MissingTotal = old.MissingTotal, MissingError = old.MissingError, Health = old.Health
                    };
                    _retry = DateTime.UtcNow.AddSeconds(30);
                    lock (Gate) _snap = keep;
                    Fire(Changed);
                    return;
                }
                lock (Gate) _snap = snap;
                Compare(snap, s);
                Fire(Changed);
            }
            catch (Exception ex) { Log.Write("Radarr: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static string Why(Exception ex)
        {
            if (ex is TaskCanceledException) return "Radarr didn't answer in time";
            if (ex is HttpRequestException && ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        static string Get(BarSettings s, string path)
        {
            HttpClient h = Http(s);
            using (HttpResponseMessage r = h.GetAsync(Api(s, path)).GetAwaiter().GetResult())
            {
                string json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new RadarrException((int)r.StatusCode, Fail(r, json));
                return json;
            }
        }

        sealed class RadarrException : Exception
        {
            public readonly int Code;
            public RadarrException(int code, string msg) : base(msg) { Code = code; }
        }

        static RadarrSnapshot ReadAll(BarSettings s, bool shown, RadarrSnapshot old)
        {
            var snap = new RadarrSnapshot { Fetched = DateTime.UtcNow, Version = old.Version };
            using (JsonDocument d = JsonDocument.Parse(Get(s, "/queue?page=1&pageSize=50&includeMovie=true")))
            {
                JsonElement recs;
                if (d.RootElement.TryGetProperty("records", out recs) && recs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in recs.EnumerateArray()) snap.Queue.Add(ParseQueue(e));
            }
            // downloading first, then the rest as Radarr has them
            var order = new List<RadarrQueueItem>(snap.Queue);
            snap.Queue.Sort(delegate (RadarrQueueItem a, RadarrQueueItem b)
            {
                int ra = a.Problem ? 0 : a.Downloading ? 1 : 2, rb = b.Problem ? 0 : b.Downloading ? 1 : 2;
                return ra != rb ? ra.CompareTo(rb) : order.IndexOf(a).CompareTo(order.IndexOf(b));
            });

            using (JsonDocument d = JsonDocument.Parse(Get(s, "/history?page=1&pageSize=50&sortKey=date&sortDirection=descending&includeMovie=true")))
            {
                JsonElement recs;
                if (d.RootElement.TryGetProperty("records", out recs) && recs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in recs.EnumerateArray())
                    {
                        RadarrEvent ev = ParseEvent(e);
                        if (ev.Id > 0) snap.Recent.Add(ev);
                    }
            }

            using (JsonDocument d = JsonDocument.Parse(Get(s, "/health")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                        snap.Health.Add(new RadarrHealth { Source = Str(e, "source"), Type = Str(e, "type"), Message = Str(e, "message"), Wiki = Str(e, "wikiUrl") });

            // the movies the history is about but didn't carry (older Radarr leaves includeMovie out)
            var need = new List<int>();
            foreach (RadarrEvent ev in snap.Recent) if (ev.Movie.Title.Length == 0 && ev.Movie.Id > 0 && !need.Contains(ev.Movie.Id)) need.Add(ev.Movie.Id);
            foreach (RadarrQueueItem q in snap.Queue) if (q.Movie.Title.Length == 0 && q.Movie.Id > 0 && !need.Contains(q.Movie.Id)) need.Add(q.Movie.Id);
            foreach (int id in need.GetRange(0, Math.Min(need.Count, 20)))
            {
                RadarrMovie m = MovieById(s, id);
                if (m == null) continue;
                foreach (RadarrEvent ev in snap.Recent) if (ev.Movie.Id == id && ev.Movie.Title.Length == 0) ev.Movie = m;
                foreach (RadarrQueueItem q in snap.Queue) if (q.Movie.Id == id && q.Movie.Title.Length == 0) q.Movie = m;
            }
            foreach (RadarrEvent ev in snap.Recent) if (ev.Movie.Title.Length == 0) ev.Movie.Title = ev.Release.Length > 0 ? ev.Release : "Movie " + ev.Movie.Id;
            foreach (RadarrQueueItem q in snap.Queue) if (q.Movie.Title.Length == 0) q.Movie.Title = q.Release.Length > 0 ? q.Release : "Movie " + q.Movie.Id;

            // Upcoming / Missing (and the version, profiles, folders): only while shown, every 5 minutes
            if (shown && (DateTime.UtcNow - _slowAt).TotalMinutes >= 5)
            {
                _slowAt = DateTime.UtcNow;
                int days = Math.Max(30, Days);
                string start = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"), end = DateTime.UtcNow.Date.AddDays(days + 1).ToString("yyyy-MM-dd");
                using (JsonDocument d = JsonDocument.Parse(Get(s, "/calendar?unmonitored=false&start=" + start + "&end=" + end)))
                    if (d.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in d.RootElement.EnumerateArray()) snap.Upcoming.Add(ParseMovie(e, false));
                snap.Upcoming.Sort(delegate (RadarrMovie a, RadarrMovie b)
                {
                    DateTime da, db;
                    a.ReleaseText(out da); b.ReleaseText(out db);
                    return da.CompareTo(db);
                });
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(Get(s, "/wanted/missing?page=1&pageSize=30&monitored=true")))
                    {
                        JsonElement recs;
                        snap.MissingTotal = Int(d.RootElement, "totalRecords");
                        if (d.RootElement.TryGetProperty("records", out recs) && recs.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in recs.EnumerateArray()) snap.Missing.Add(ParseMovie(e, false));
                    }
                }
                catch (RadarrException ex)
                {
                    snap.MissingError = ex.Code == 404 ? "Missing needs Radarr 5 or newer." : ex.Message;
                }
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(Get(s, "/system/status"))) snap.Version = Str(d.RootElement, "version");
                }
                catch { }
                if ((DateTime.UtcNow - _setupAt).TotalMinutes >= 30) try { ReadSetup(s); } catch { }
            }
            else
            {
                snap.Upcoming = old.Upcoming; snap.Missing = old.Missing; snap.MissingTotal = old.MissingTotal; snap.MissingError = old.MissingError;
            }
            return snap;
        }

        static int _days = 30;
        /// <summary>The most days ahead a Radarr module wants in Upcoming (each view shows its own days of them).</summary>
        public static int Days { get { return _days; } }

        /// <summary>A module shows this many days of Upcoming: read further ahead from the next poll if that's more than before.</summary>
        public static void WantDays(int days)
        {
            if (days <= _days) return;
            _days = days;
            Refresh();
        }

        static void ReadSetup(BarSettings s)
        {
            var profiles = new List<string>();
            var folders = new List<string>();
            using (JsonDocument d = JsonDocument.Parse(Get(s, "/qualityprofile")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray()) { string n = Str(e, "name"); if (n.Length > 0) profiles.Add(n); }
            using (JsonDocument d = JsonDocument.Parse(Get(s, "/rootfolder")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray()) { string p = Str(e, "path"); if (p.Length > 0) folders.Add(p); }
            lock (Gate) { _profiles = profiles; _folders = folders; }
            _setupAt = DateTime.UtcNow;
        }

        static RadarrMovie MovieById(BarSettings s, int id)
        {
            try { using (JsonDocument d = JsonDocument.Parse(Get(s, "/movie/" + id))) return ParseMovie(d.RootElement, false); }
            catch { return null; }
        }

        // ---- JSON ----
        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static int Int(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return 0;
            int n;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n)) return n;
            return 0;
        }

        static double Num(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
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

        static DateTime When(JsonElement e, string name)
        {
            string t = Str(e, name);
            DateTime d;
            if (t.Length > 0 && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
            return DateTime.MinValue;
        }

        /// <summary>A release date: Radarr sends the day as midnight UTC - kept as that day (not moved by the time zone).</summary>
        static DateTime Day(JsonElement e, string name)
        {
            DateTime d = When(e, name);
            return d == DateTime.MinValue ? d : DateTime.SpecifyKind(d.Date, DateTimeKind.Unspecified);
        }

        static string QualityOf(JsonElement e)
        {
            return Str(Obj(Obj(e, "quality"), "quality"), "name");
        }

        static RadarrMovie ParseMovie(JsonElement e, bool keepRaw)
        {
            var m = new RadarrMovie
            {
                Id = Int(e, "id"), TmdbId = Int(e, "tmdbId"), Title = Str(e, "title"), Slug = Str(e, "titleSlug"), Overview = Str(e, "overview"),
                HasFile = Bool(e, "hasFile"), Monitored = Bool(e, "monitored"), Available = Bool(e, "isAvailable"),
                InCinemas = Day(e, "inCinemas"), Digital = Day(e, "digitalRelease"), Physical = Day(e, "physicalRelease")
            };
            int y = Int(e, "year");
            m.Year = y > 0 ? y.ToString() : "";
            JsonElement imgs;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("images", out imgs) && imgs.ValueKind == JsonValueKind.Array)
                foreach (JsonElement i in imgs.EnumerateArray())
                    if (Str(i, "coverType") == "poster") { m.Poster = Small(Str(i, "remoteUrl")); break; }
            if (m.Poster.Length == 0) m.Poster = Small(Str(e, "remotePoster"));
            if (keepRaw) m.Raw = e.GetRawText();
            return m;
        }

        /// <summary>TMDB's poster at a small size (Radarr gives the original).</summary>
        static string Small(string url)
        {
            if (url.Length == 0) return "";
            int i = url.IndexOf("/t/p/", StringComparison.Ordinal);
            if (i < 0) return url;
            int j = url.IndexOf('/', i + 5);
            return j < 0 ? url : url.Substring(0, i + 5) + "w154" + url.Substring(j);
        }

        static RadarrQueueItem ParseQueue(JsonElement e)
        {
            var q = new RadarrQueueItem
            {
                Id = Int(e, "id"), Release = Str(e, "title"), Status = Str(e, "status").ToLowerInvariant(), Tracked = Str(e, "trackedDownloadStatus").ToLowerInvariant(),
                State = Str(e, "trackedDownloadState"), Client = Str(e, "downloadClient"), Quality = QualityOf(e), Size = Num(e, "size"), Left = Num(e, "sizeleft")
            };
            JsonElement mv = Obj(e, "movie");
            if (mv.ValueKind == JsonValueKind.Object) q.Movie = ParseMovie(mv, false);
            if (q.Movie.Id == 0) q.Movie.Id = Int(e, "movieId");
            TimeSpan left;
            string tl = Str(e, "timeleft");
            if (tl.Length > 0 && TimeSpan.TryParse(tl, CultureInfo.InvariantCulture, out left)) q.TimeLeft = left;
            q.Message = Str(e, "errorMessage");
            JsonElement msgs;
            if (q.Message.Length == 0 && e.TryGetProperty("statusMessages", out msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (JsonElement sm in msgs.EnumerateArray())
                {
                    JsonElement ml;
                    if (sm.TryGetProperty("messages", out ml) && ml.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in ml.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && parts.Count < 3) parts.Add(x.GetString());
                }
                q.Message = string.Join("\n", parts);
            }
            return q;
        }

        static RadarrEvent ParseEvent(JsonElement e)
        {
            var ev = new RadarrEvent { Id = Int(e, "id"), Kind = Str(e, "eventType"), Date = When(e, "date"), Release = Str(e, "sourceTitle"), Quality = QualityOf(e) };
            JsonElement mv = Obj(e, "movie");
            if (mv.ValueKind == JsonValueKind.Object) ev.Movie = ParseMovie(mv, false);
            if (ev.Movie.Id == 0) ev.Movie.Id = Int(e, "movieId");
            ev.Message = Str(Obj(e, "data"), "message");
            return ev;
        }

        // =================================================================
        // Notices
        // =================================================================
        static void Compare(RadarrSnapshot snap, BarSettings s)
        {
            Action<string, string, string, string> n = Notice;
            RaSettings c = RaSettings.Of(s);
            int newest = 0;
            foreach (RadarrEvent ev in snap.Recent) newest = Math.Max(newest, ev.Id);
            if (_lastEvent >= 0 && n != null)
            {
                var fresh = snap.Recent.FindAll(delegate (RadarrEvent ev) { return ev.Id > _lastEvent; });
                fresh.Reverse();     // oldest first
                foreach (RadarrEvent ev in fresh)
                {
                    string url = MovieUrl(s, ev.Movie);
                    try
                    {
                        if (ev.Imported && c.NotifyImported)
                            n("Radarr.Imported", "Downloaded: " + ev.Movie.Name, ev.Quality.Length > 0 ? ev.Quality + " · in your library" : "In your library", url);
                        else if (ev.Kind == "grabbed" && c.NotifyGrabbed)
                            n("Radarr.Grabbed", "Grabbed: " + ev.Movie.Name, (ev.Quality.Length > 0 ? ev.Quality + " · " : "") + ev.Release, url);
                        else if (ev.Kind == "downloadFailed" && c.NotifyFailed)
                            n("Radarr.Failed", "Download failed: " + ev.Movie.Name, ev.Message.Length > 0 ? ev.Message : ev.Release, url);
                    }
                    catch { }
                }
            }
            if (newest > 0 || _lastEvent < 0) _lastEvent = Math.Max(_lastEvent, newest);

            bool first = _seenHealth == null;
            var now = new HashSet<string>();
            foreach (RadarrHealth h in snap.Health)
            {
                string k = h.Source + "|" + h.Message;
                now.Add(k);
                if (first || _seenHealth.Contains(k) || !c.NotifyHealth || n == null) continue;
                try { n("Radarr.Health", "Radarr " + (h.Error ? "error" : "warning"), h.Message, PageUrl(s, "system/status")); } catch { }
            }
            _seenHealth = now;      // one that goes away and comes back is said again
        }

        // =================================================================
        // Actions
        // =================================================================
        /// <summary>Movies matching the text (in the library or not).</summary>
        public static async Task<List<RadarrMovie>> SearchAsync(BarSettings s, string query)
        {
            HttpClient h = Http(s);
            string json;
            using (HttpResponseMessage r = await h.GetAsync(Api(s, "/movie/lookup?term=" + Uri.EscapeDataString(query ?? ""))).ConfigureAwait(false))
            {
                json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
            }
            var list = new List<RadarrMovie>();
            using (JsonDocument d = JsonDocument.Parse(json))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        RadarrMovie m = ParseMovie(e, true);
                        if (m.TmdbId > 0) list.Add(m);
                    }
            return list;
        }

        /// <summary>Add a movie from a search (with the profile, folder and availability from the Plugins page). Null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> AddAsync(BarSettings s, RadarrMovie m)
        {
            try
            {
                HttpClient h = Http(s);
                RaSettings c = RaSettings.Of(s);
                // the profile / folder picked on the Plugins page, or Radarr's first one (also when the picked one is gone)
                int profile = 0;
                string folder = "";
                using (JsonDocument d = JsonDocument.Parse(await GetAsync(h, Api(s, "/qualityprofile")).ConfigureAwait(false)))
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        if (profile == 0) profile = Int(e, "id");
                        if (string.Equals(Str(e, "name"), c.Profile, StringComparison.OrdinalIgnoreCase)) { profile = Int(e, "id"); break; }
                    }
                using (JsonDocument d = JsonDocument.Parse(await GetAsync(h, Api(s, "/rootfolder")).ConfigureAwait(false)))
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        if (folder.Length == 0) folder = Str(e, "path");
                        if (string.Equals(Str(e, "path"), c.RootFolder, StringComparison.OrdinalIgnoreCase)) { folder = Str(e, "path"); break; }
                    }
                if (profile == 0) return "Radarr has no quality profile.";
                if (folder.Length == 0) return "Radarr has no root folder - add one in Radarr (Settings > Media Management).";
                string avail = c.MinAvailability == "Announced" ? "announced" : c.MinAvailability == "In cinemas" ? "inCinemas" : "released";

                // the search result as Radarr gave it, plus how to add it
                var body = new StringBuilder();
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var w = new Utf8JsonWriter(ms))
                    {
                        w.WriteStartObject();
                        if (m.Raw.Length > 0)
                            using (JsonDocument d = JsonDocument.Parse(m.Raw))
                                foreach (JsonProperty p in d.RootElement.EnumerateObject())
                                {
                                    string k = p.Name;
                                    if (k == "qualityProfileId" || k == "rootFolderPath" || k == "monitored" || k == "minimumAvailability" || k == "addOptions" || k == "path" || k == "id") continue;
                                    p.WriteTo(w);
                                }
                        else
                        {
                            w.WriteString("title", m.Title);
                            w.WriteNumber("tmdbId", m.TmdbId);
                            int y;
                            if (int.TryParse(m.Year, out y)) w.WriteNumber("year", y);
                        }
                        w.WriteNumber("qualityProfileId", profile);
                        w.WriteString("rootFolderPath", folder);
                        w.WriteBoolean("monitored", true);
                        w.WriteString("minimumAvailability", avail);
                        w.WriteStartObject("addOptions");
                        w.WriteBoolean("searchForMovie", c.SearchOnAdd);
                        w.WriteString("monitor", "movieOnly");
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    body.Append(Encoding.UTF8.GetString(ms.ToArray()));
                }
                using (var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"))
                using (HttpResponseMessage r = await h.PostAsync(Api(s, "/movie"), content).ConfigureAwait(false))
                {
                    string json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return Fail(r, json);
                    try
                    {
                        using (JsonDocument d = JsonDocument.Parse(json))
                        {
                            m.Id = Int(d.RootElement, "id");
                            string slug = Str(d.RootElement, "titleSlug");
                            if (slug.Length > 0) m.Slug = slug;
                        }
                    }
                    catch { }
                }
                Refresh();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Ask Radarr to search for a movie in the library. Null when it worked, otherwise what went wrong.</summary>
        public static Task<string> SearchMovieAsync(BarSettings s, int movieId)
        {
            return CommandAsync(s, "{\"name\":\"MoviesSearch\",\"movieIds\":[" + movieId + "]}");
        }

        /// <summary>Search for every missing movie (Radarr: Wanted > Missing > Search All).</summary>
        public static Task<string> SearchMissingAsync(BarSettings s)
        {
            return CommandAsync(s, "{\"name\":\"MissingMoviesSearch\"}");
        }

        static async Task<string> CommandAsync(BarSettings s, string json)
        {
            try
            {
                HttpClient h = Http(s);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage r = await h.PostAsync(Api(s, "/command"), content).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return Fail(r, body);
                }
                Poke();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Take a download out of the queue (and out of the download client); blocklist = don't grab that release again.</summary>
        public static async Task<string> RemoveAsync(BarSettings s, int queueId, bool blocklist)
        {
            try
            {
                HttpClient h = Http(s);
                using (HttpResponseMessage r = await h.DeleteAsync(Api(s, "/queue/" + queueId + "?removeFromClient=true&blocklist=" + (blocklist ? "true" : "false"))).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return Fail(r, body);
                }
                Poke();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        static async Task<string> GetAsync(HttpClient h, string url)
        {
            using (HttpResponseMessage r = await h.GetAsync(url).ConfigureAwait(false))
            {
                string json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
                return json;
            }
        }

        /// <summary>Test connection: the version, or why it failed (and the profiles / folders for the Plugins page).</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address and the API key first.");
                HttpClient h = Http(s);
                string ver = "", app = "";
                using (JsonDocument d = JsonDocument.Parse(await GetAsync(h, Api(s, "/system/status")).ConfigureAwait(false)))
                {
                    ver = Str(d.RootElement, "version");
                    app = Str(d.RootElement, "appName");
                }
                if (app.Length > 0 && !app.Equals("Radarr", StringComparison.OrdinalIgnoreCase))
                    return new KeyValuePair<bool, string>(false, "That address is " + app + ", not Radarr.");
                await Task.Run(delegate { ReadSetup(s); }).ConfigureAwait(false);
                Poke();
                return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - Radarr " + ver : "") + ".");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, Why(ex)); }
        }

        static string Fail(HttpResponseMessage r, string body)
        {
            string msg = "";
            try
            {
                using (JsonDocument d = JsonDocument.Parse(body))
                {
                    JsonElement e = d.RootElement;
                    if (e.ValueKind == JsonValueKind.Object) msg = Str(e, "message");
                    else if (e.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in e.EnumerateArray()) { msg = Str(x, "errorMessage"); if (msg.Length > 0) break; }   // validation errors
                }
            }
            catch { }
            int code = (int)r.StatusCode;
            if (code == 401 || code == 403) return "Radarr refused the API key (" + code + ")";
            if (code == 404 && msg.Length == 0) return "Radarr answered 404 - is the address right (with Radarr's URL base, if it has one)?";
            return "Radarr answered " + code + (msg.Length > 0 ? ": " + msg : "");
        }

        /// <summary>How long ago (TimeText.Ago); "" when unknown.</summary>
        public static string Ago(DateTime utc) { return utc == DateTime.MinValue ? "" : TimeText.Ago(utc); }
    }
}
