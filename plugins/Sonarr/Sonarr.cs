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
    /// <summary>The Sonarr connection, adding shows and notifications - kept in vShell's settings under Integrations["sonarr"].</summary>
    public sealed class SoSettings
    {
        /// <summary>Which episodes a show added from vShell monitors (Sonarr's addOptions.monitor).</summary>
        public static readonly string[] MonitorChoices = { "All episodes", "Future episodes", "Missing episodes", "Existing episodes", "First season", "Latest season", "None" };
        static readonly string[] MonitorValues = { "all", "future", "missing", "existing", "firstSeason", "latestSeason", "none" };
        /// <summary>The choice that means "Sonarr's first quality profile / root folder".</summary>
        public const string First = "The first one in Sonarr";

        readonly SettingsBag _b;
        public SoSettings(BarSettings s) { _b = s.Integration("sonarr"); }
        public static SoSettings Of(BarSettings s) { return new SoSettings(s); }

        /// <summary>Address such as http://192.168.1.20:8989 (with the URL base, if Sonarr has one: http://nas/sonarr)</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>API key (Sonarr: Settings > General), stored encrypted for the current Windows user (ApiKeyPlain is the real one).</summary>
        public string ApiKey { get { return _b.Get("ApiKey", ""); } set { _b.Set("ApiKey", Secret.Protect(value ?? "")); } }
        public string ApiKeyPlain { get { return _b.GetSecret("ApiKey"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>The run box keyword (without the !).</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "so").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "so" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        /// <summary>Adding a show: the quality profile's name ("" = the first one in Sonarr).</summary>
        public string Profile { get { return _b.Get("Profile", ""); } set { _b.Set("Profile", value == First ? "" : value ?? ""); } }
        /// <summary>Adding a show: the root folder's path ("" = the first one in Sonarr).</summary>
        public string RootFolder { get { return _b.Get("RootFolder", ""); } set { _b.Set("RootFolder", value == First ? "" : value ?? ""); } }
        /// <summary>Adding a show: which episodes it monitors (one of MonitorChoices).</summary>
        public string Monitor
        {
            get { string v = _b.Get("Monitor", "All episodes"); return Array.IndexOf(MonitorChoices, v) < 0 ? "All episodes" : v; }
            set { _b.Set("Monitor", value); }
        }
        public string MonitorValue { get { return MonitorValues[Array.IndexOf(MonitorChoices, Monitor)]; } }
        /// <summary>Adding a show: Sonarr searches for its missing (monitored) episodes straight away.</summary>
        public bool SearchOnAdd { get { return _b.Get("SearchOnAdd", true); } set { _b.Set("SearchOnAdd", value); } }
        /// <summary>Adding a show: a folder per season.</summary>
        public bool SeasonFolders { get { return _b.Get("SeasonFolders", true); } set { _b.Set("SeasonFolders", value); } }

        /// <summary>Notices: episodes were downloaded (imported into the library).</summary>
        public bool NotifyImported { get { return _b.Get("NotifyImported", true); } set { _b.Set("NotifyImported", value); } }
        /// <summary>Notices: Sonarr grabbed a release (sent it to the download client).</summary>
        public bool NotifyGrabbed { get { return _b.Get("NotifyGrabbed", false); } set { _b.Set("NotifyGrabbed", value); } }
        /// <summary>Notices: a download failed.</summary>
        public bool NotifyFailed { get { return _b.Get("NotifyFailed", true); } set { _b.Set("NotifyFailed", value); } }
        /// <summary>Notices: a new health warning / error (System > Status in Sonarr).</summary>
        public bool NotifyHealth { get { return _b.Get("NotifyHealth", true); } set { _b.Set("NotifyHealth", value); } }

        public bool AnyNotices { get { return NotifyImported || NotifyGrabbed || NotifyFailed || NotifyHealth; } }
    }

    /// <summary>A Sonarr module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class SoView
    {
        public static readonly string[] Tabs = { "Queue", "Upcoming", "Recently added", "Missing" };

        readonly SettingsBag _b;
        public SoView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static SoView Of(ModuleConfig cfg) { return new SoView(cfg); }

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
        /// <summary>How many rows each view shows.</summary>
        public int Count { get { return Math.Max(1, Math.Min(30, _b.Get("Count", 6))); } set { _b.Set("Count", value); } }
        /// <summary>Upcoming: how many days ahead.</summary>
        public int Days { get { return Math.Max(1, Math.Min(30, _b.Get("Days", 7))); } set { _b.Set("Days", value); } }
        public bool Posters { get { return _b.Get("Posters", true); } set { _b.Set("Posters", value); } }
        /// <summary>Bar: the number in the queue as a badge on the icon.</summary>
        public bool Badge { get { return _b.Get("Badge", true); } set { _b.Set("Badge", value); } }
        /// <summary>Bar: how far the downloads are and the time left.</summary>
        public bool BarProgress { get { return _b.Get("BarProgress", true); } set { _b.Set("BarProgress", value); } }
        /// <summary>Bar: hidden while nothing is in the queue (and Sonarr is fine).</summary>
        public bool HideIdle { get { return _b.Get("HideIdle", false); } set { _b.Set("HideIdle", value); } }
        /// <summary>Show Sonarr's health warnings and errors (System > Status): the line in the widget / popup, the red badge and the tip on a bar.</summary>
        public bool Health { get { return _b.Get("Health", true); } set { _b.Set("Health", value); } }
        /// <summary>The "Sonarr" button (opens Sonarr) at the top.</summary>
        public bool ShowOpen { get { return _b.Get("ShowOpen", true); } set { _b.Set("ShowOpen", value); } }
    }

    /// <summary>A show in Sonarr (or from a search: Id 0 = not in the library).</summary>
    public sealed class SonarrSeries
    {
        public int Id;
        public int TvdbId;
        public string Title = "";
        public string Year = "";
        public string Slug = "";
        public string Poster = "";
        public string Overview = "";
        public string Network = "";
        /// <summary>continuing, ended, upcoming</summary>
        public string Status = "";
        public int Seasons, Episodes, Files;
        /// <summary>The JSON Sonarr gave for it (adding a show sends it back).</summary>
        public string Raw = "";

        public bool InLibrary { get { return Id > 0; } }
        public string Name { get { return Title + (Year.Length > 0 && Year != "0" && !Title.Contains("(" + Year + ")") ? " (" + Year + ")" : ""); } }
    }

    /// <summary>An episode of a show.</summary>
    public sealed class SonarrEpisode
    {
        public int Id;
        public int SeriesId;
        public int Season, Number;
        public string Title = "";
        public string Overview = "";
        /// <summary>When it airs / aired (UTC; MinValue = not known).</summary>
        public DateTime Airs = DateTime.MinValue;
        public bool HasFile, Monitored;
        public SonarrSeries Series = new SonarrSeries();

        public string Code { get { return "S" + Season.ToString("00") + "E" + Number.ToString("00"); } }

        /// <summary>"S02E03", "S02E01-E08", "S01E10, S02E01" for a few episodes (in the order given).</summary>
        public static string Codes(List<SonarrEpisode> eps)
        {
            if (eps.Count == 0) return "";
            if (eps.Count == 1) return eps[0].Code;
            var l = new List<SonarrEpisode>(eps);
            l.Sort(delegate (SonarrEpisode a, SonarrEpisode b) { return a.Season != b.Season ? a.Season.CompareTo(b.Season) : a.Number.CompareTo(b.Number); });
            bool run = true;
            for (int i = 1; i < l.Count && run; i++) if (l[i].Season != l[0].Season || l[i].Number != l[i - 1].Number + 1) run = false;
            if (run) return l[0].Code + "-E" + l[l.Count - 1].Number.ToString("00");
            if (l.Count <= 3) return string.Join(", ", l.ConvertAll(delegate (SonarrEpisode e) { return e.Code; }));
            return l[0].Code + " … " + l[l.Count - 1].Code;
        }

        /// <summary>"tonight 8:00 pm", "tomorrow 9:30 pm", "Thu 8:00 pm", "20 Nov", "yesterday".</summary>
        public static string When(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            DateTime local = utc.ToLocalTime(), today = DateTime.Today;
            string time = TimeText.Clock(local);
            if (local.Date == today) return (local.Hour >= 17 ? "tonight " : "today ") + time;
            if (local.Date == today.AddDays(1)) return "tomorrow " + time;
            if (local.Date == today.AddDays(-1)) return "yesterday";
            if (local.Date > today && local.Date < today.AddDays(7)) return local.ToString("ddd") + " " + time;
            return local.Year == today.Year ? local.ToString("d MMM") : local.ToString("d MMM yyyy");
        }
    }

    /// <summary>Something in Sonarr's queue - a download, with the episodes it brings (a season pack has many).</summary>
    public sealed class SonarrQueueItem
    {
        /// <summary>Sonarr's queue ids of every episode in this download.</summary>
        public List<int> Ids = new List<int>();
        public string DownloadId = "";
        public SonarrSeries Series = new SonarrSeries();
        public List<SonarrEpisode> Episodes = new List<SonarrEpisode>();
        /// <summary>The release's name.</summary>
        public string Release = "";
        /// <summary>The download client's state: downloading, paused, queued, completed, delay, warning, failed …</summary>
        public string Status = "";
        /// <summary>Sonarr's view: ok, warning, error.</summary>
        public string Tracked = "";
        /// <summary>downloading, importPending, importing, imported, failedPending, failed, ignored …</summary>
        public string State = "";
        public string Message = "";
        public string Client = "";
        public string Quality = "";
        public double Size, Left;
        public TimeSpan TimeLeft = TimeSpan.Zero;

        public string Codes { get { return SonarrEpisode.Codes(Episodes); } }
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

    /// <summary>Something that happened in Sonarr (its history) - one per episode.</summary>
    public sealed class SonarrEvent
    {
        public int Id;
        public SonarrSeries Series = new SonarrSeries();
        public SonarrEpisode Episode = new SonarrEpisode();
        /// <summary>grabbed, downloadFolderImported, seriesFolderImported, downloadFailed, episodeFileDeleted, episodeFileRenamed, downloadIgnored</summary>
        public string Kind = "";
        public DateTime Date = DateTime.MinValue;     // UTC
        public string Release = "";
        public string Quality = "";
        public string Message = "";
        public bool Imported { get { return Kind == "downloadFolderImported" || Kind == "seriesFolderImported"; } }
    }

    /// <summary>Episodes of one show shown as one row (a season pack, a few missing episodes, two airing back to back).</summary>
    public sealed class SonarrGroup
    {
        public SonarrSeries Series;
        public List<SonarrEpisode> Episodes = new List<SonarrEpisode>();
        public DateTime When = DateTime.MinValue;
        public string Quality = "";
        public string Release = "";
        public string Codes { get { return SonarrEpisode.Codes(Episodes); } }
        public bool AllFiles { get { return Episodes.Count > 0 && Episodes.TrueForAll(delegate (SonarrEpisode e) { return e.HasFile; }); } }

        /// <summary>Back-to-back episodes of the same show as one group (same = also within the hour, when sameTime).</summary>
        public static List<SonarrGroup> Of(List<SonarrEpisode> eps, bool sameTime)
        {
            var l = new List<SonarrGroup>();
            foreach (SonarrEpisode e in eps)
            {
                SonarrGroup last = l.Count > 0 ? l[l.Count - 1] : null;
                if (last != null && last.Series.Id == e.SeriesId && (!sameTime || Math.Abs((last.When - e.Airs).TotalMinutes) <= 60)) { last.Episodes.Add(e); continue; }
                var g = new SonarrGroup { Series = e.Series, When = e.Airs };
                g.Episodes.Add(e);
                l.Add(g);
            }
            return l;
        }
    }

    /// <summary>A health warning / error (Sonarr: System > Status).</summary>
    public sealed class SonarrHealth
    {
        public string Source = "", Type = "", Message = "", Wiki = "";
        public bool Error { get { return Type == "error"; } }
    }

    public sealed class SonarrSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public string Version = "";
        public List<SonarrQueueItem> Queue = new List<SonarrQueueItem>();
        /// <summary>Episodes airing in the next days, soonest first (only fetched while something shows Sonarr).</summary>
        public List<SonarrEpisode> Upcoming = new List<SonarrEpisode>();
        /// <summary>The latest history (grabs, imports, failures …), newest first - Recently added shows the imports.</summary>
        public List<SonarrEvent> Recent = new List<SonarrEvent>();
        /// <summary>Monitored episodes that aired but aren't downloaded, newest first (only fetched while something shows Sonarr).</summary>
        public List<SonarrEpisode> Missing = new List<SonarrEpisode>();
        public int MissingTotal;
        public string MissingError = "";
        public List<SonarrHealth> Health = new List<SonarrHealth>();
        public bool Ok { get { return Fetched != DateTime.MinValue; } }
        public int Problems { get { return Queue.FindAll(delegate (SonarrQueueItem q) { return q.Problem; }).Count; } }
    }

    /// <summary>
    /// Talks to Sonarr (API v3 - Sonarr 3 and 4) with its API key. Polled in the background: every 15 s while a Sonarr module (or the run
    /// box) shows it, every 2 minutes otherwise when notices are on. Upcoming and Missing are only read while shown (every 5 minutes).
    /// </summary>
    public static class SonarrClient
    {
        static HttpClient _http;
        static string _key = "", _base = "";
        static bool _ignoreCert;
        static readonly object HttpGate = new object();

        static readonly object Gate = new object();
        static SonarrSnapshot _snap = new SonarrSnapshot();
        static Timer _timer;
        static BarSettings _s;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue, _lastPoll = DateTime.MinValue, _retry = DateTime.MinValue, _slowAt = DateTime.MinValue, _setupAt = DateTime.MinValue;
        static string _configuredFor;
        static int _lastEvent = -1;
        static HashSet<string> _seenHealth;
        static List<string> _profiles = new List<string>(), _folders = new List<string>();
        static bool _missingSort = true;
        /// <summary>The shows read so far by id (for history / queue rows Sonarr sends without their show).</summary>
        static readonly Dictionary<int, SonarrSeries> Shows = new Dictionary<int, SonarrSeries>();

        /// <summary>A notice: (kind key, title line, detail, Sonarr page address).</summary>
        public static event Action<string, string, string, string> Notice;
        public static event Action Changed;

        static HttpClient Http(BarSettings s)
        {
            lock (HttpGate)
            {
                SoSettings c = SoSettings.Of(s);
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
            string b = SoSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            SoSettings c = SoSettings.Of(s);
            return !string.IsNullOrWhiteSpace(c.Url) && !string.IsNullOrEmpty(c.ApiKeyPlain);
        }

        static string Api(BarSettings s, string path) { Http(s); return _base + "/api/v3" + path; }

        /// <summary>A page in Sonarr: "" = the shows, "activity/queue", "calendar", "wanted/missing", "system/status" …</summary>
        public static string PageUrl(BarSettings s, string page) { return Base(s) + "/" + (page ?? ""); }

        /// <summary>The show's page in Sonarr (Add new with its name when it isn't in the library).</summary>
        public static string SeriesUrl(BarSettings s, SonarrSeries m)
        {
            if (m == null) return PageUrl(s, "");
            if (m.InLibrary && m.Slug.Length > 0) return PageUrl(s, "series/" + Uri.EscapeDataString(m.Slug));
            if (m.InLibrary) return PageUrl(s, "");
            return PageUrl(s, "add/new?term=" + Uri.EscapeDataString(m.TvdbId > 0 ? "tvdb:" + m.TvdbId : m.Title));
        }

        /// <summary>The quality profiles' names and root folders' paths Sonarr has (read with the polls / Test; empty until then).</summary>
        public static List<string> Profiles { get { lock (Gate) return new List<string>(_profiles); } }
        public static List<string> Folders { get { lock (Gate) return new List<string>(_folders); } }

        // =================================================================
        // Polling
        // =================================================================
        public static void Configure(BarSettings s)
        {
            _s = s;
            SoSettings c = SoSettings.Of(s);
            string id = c.Url.Trim() + "|" + c.ApiKey;
            if (_configuredFor != null && _configuredFor != id)
            {
                // another server / key: start over (no notices for what the new one already has)
                lock (Gate) { _snap = new SonarrSnapshot(); _profiles = new List<string>(); _folders = new List<string>(); }
                lock (Shows) Shows.Clear();
                _lastEvent = -1;
                _seenHealth = null;
                _slowAt = _setupAt = DateTime.MinValue;
                _missingSort = true;
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
                _snap = new SonarrSnapshot();
            }
            lock (Shows) Shows.Clear();
            _s = null;
            _configuredFor = null;
            _lastEvent = -1;
            _seenHealth = null;
            _slowAt = _setupAt = DateTime.MinValue;
        }

        /// <summary>The latest snapshot (asking for it keeps the 15-second polling going).</summary>
        public static SonarrSnapshot Latest
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

        static int _days = 7;
        /// <summary>The most days ahead a Sonarr module wants in Upcoming (each view shows its own days of them).</summary>
        public static int Days { get { return _days; } }

        /// <summary>A module shows this many days of Upcoming: read further ahead from the next poll if that's more than before.</summary>
        public static void WantDays(int days)
        {
            if (days <= _days) return;
            _days = days;
            Refresh();
        }

        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null || !Configured(s)) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 40;
                if (!shown && !SoSettings.Of(s).AnyNotices) return;
                if (DateTime.UtcNow < _retry) return;
                if ((DateTime.UtcNow - _lastPoll).TotalSeconds < (shown ? 14 : 120)) return;
                _lastPoll = DateTime.UtcNow;
                SonarrSnapshot old;
                lock (Gate) old = _snap;
                SonarrSnapshot snap;
                try { snap = ReadAll(s, shown, old); }
                catch (Exception ex)
                {
                    string msg = Why(ex);
                    if (old.Error != msg) Log.Write("Sonarr: " + msg);
                    var keep = new SonarrSnapshot
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
            catch (Exception ex) { Log.Write("Sonarr: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static string Why(Exception ex)
        {
            if (ex is TaskCanceledException) return "Sonarr didn't answer in time";
            if (ex is HttpRequestException && ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        static string Get(BarSettings s, string path)
        {
            HttpClient h = Http(s);
            using (HttpResponseMessage r = h.GetAsync(Api(s, path)).GetAwaiter().GetResult())
            {
                string json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new SonarrException((int)r.StatusCode, Fail(r, json));
                return json;
            }
        }

        sealed class SonarrException : Exception
        {
            public readonly int Code;
            public SonarrException(int code, string msg) : base(msg) { Code = code; }
        }

        static SonarrSnapshot ReadAll(BarSettings s, bool shown, SonarrSnapshot old)
        {
            var snap = new SonarrSnapshot { Fetched = DateTime.UtcNow, Version = old.Version };

            // the queue: one row per download (a season pack's episodes together)
            var byDownload = new Dictionary<string, SonarrQueueItem>();
            using (JsonDocument d = JsonDocument.Parse(Get(s, "/queue?page=1&pageSize=100&includeSeries=true&includeEpisode=true")))
            {
                JsonElement recs;
                if (d.RootElement.TryGetProperty("records", out recs) && recs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in recs.EnumerateArray())
                    {
                        SonarrQueueItem q = ParseQueue(e);
                        SonarrQueueItem have;
                        if (q.DownloadId.Length > 0 && byDownload.TryGetValue(q.DownloadId, out have))
                        {
                            have.Ids.AddRange(q.Ids);
                            have.Episodes.AddRange(q.Episodes);
                            continue;
                        }
                        if (q.DownloadId.Length > 0) byDownload[q.DownloadId] = q;
                        snap.Queue.Add(q);
                    }
            }
            // problems first, then downloading, then the rest as Sonarr has them
            var order = new List<SonarrQueueItem>(snap.Queue);
            snap.Queue.Sort(delegate (SonarrQueueItem a, SonarrQueueItem b)
            {
                int ra = a.Problem ? 0 : a.Downloading ? 1 : 2, rb = b.Problem ? 0 : b.Downloading ? 1 : 2;
                return ra != rb ? ra.CompareTo(rb) : order.IndexOf(a).CompareTo(order.IndexOf(b));
            });

            using (JsonDocument d = JsonDocument.Parse(Get(s, "/history?page=1&pageSize=60&sortKey=date&sortDirection=descending&includeSeries=true&includeEpisode=true")))
            {
                JsonElement recs;
                if (d.RootElement.TryGetProperty("records", out recs) && recs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in recs.EnumerateArray())
                    {
                        SonarrEvent ev = ParseEvent(e);
                        if (ev.Id > 0) snap.Recent.Add(ev);
                    }
            }

            using (JsonDocument d = JsonDocument.Parse(Get(s, "/health")))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                        snap.Health.Add(new SonarrHealth { Source = Str(e, "source"), Type = Str(e, "type"), Message = Str(e, "message"), Wiki = Str(e, "wikiUrl") });

            // Upcoming / Missing (and the version, profiles, folders): only while shown, every 5 minutes
            if (shown && (DateTime.UtcNow - _slowAt).TotalMinutes >= 5)
            {
                _slowAt = DateTime.UtcNow;
                string start = DateTime.UtcNow.AddHours(-12).ToString("yyyy-MM-ddTHH:mm:ssZ"), end = DateTime.UtcNow.Date.AddDays(Math.Max(7, Days) + 1).ToString("yyyy-MM-dd");
                using (JsonDocument d = JsonDocument.Parse(Get(s, "/calendar?unmonitored=false&includeSeries=true&start=" + start + "&end=" + end)))
                    if (d.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in d.RootElement.EnumerateArray()) snap.Upcoming.Add(ParseEpisode(e));
                snap.Upcoming.Sort(delegate (SonarrEpisode a, SonarrEpisode b)
                {
                    int c = a.Airs.CompareTo(b.Airs);
                    if (c != 0) return c;
                    c = a.SeriesId.CompareTo(b.SeriesId);
                    return c != 0 ? c : a.Season != b.Season ? a.Season.CompareTo(b.Season) : a.Number.CompareTo(b.Number);
                });
                try { ReadMissing(s, snap); }
                catch (SonarrException ex) { snap.MissingError = ex.Message; }
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

            // the shows rows came without (older Sonarr leaves includeSeries out on some calls)
            var need = new List<int>();
            Action<int, SonarrSeries> want = delegate (int id, SonarrSeries have) { if (id > 0 && have.Title.Length == 0 && !need.Contains(id)) need.Add(id); };
            foreach (SonarrEvent ev in snap.Recent) want(ev.Series.Id, ev.Series);
            foreach (SonarrQueueItem q in snap.Queue) want(q.Series.Id, q.Series);
            foreach (SonarrEpisode e in snap.Upcoming) want(e.SeriesId, e.Series);
            foreach (SonarrEpisode e in snap.Missing) want(e.SeriesId, e.Series);
            foreach (int id in need.GetRange(0, Math.Min(need.Count, 20))) SeriesById(s, id);
            Func<int, SonarrSeries, SonarrSeries> fill = delegate (int id, SonarrSeries have)
            {
                if (have.Title.Length > 0) return have;
                SonarrSeries m;
                lock (Shows) Shows.TryGetValue(id, out m);
                return m ?? new SonarrSeries { Id = id, Title = "Show " + id };
            };
            foreach (SonarrEvent ev in snap.Recent) { ev.Series = fill(ev.Series.Id, ev.Series); ev.Episode.Series = ev.Series; }
            foreach (SonarrQueueItem q in snap.Queue) { q.Series = fill(q.Series.Id, q.Series); foreach (SonarrEpisode e in q.Episodes) e.Series = q.Series; }
            foreach (SonarrEpisode e in snap.Upcoming) e.Series = fill(e.SeriesId, e.Series);
            foreach (SonarrEpisode e in snap.Missing) e.Series = fill(e.SeriesId, e.Series);
            return snap;
        }

        static void ReadMissing(BarSettings s, SonarrSnapshot snap)
        {
            // newest first: Sonarr 4 calls the sort "episodes.airDateUtc", Sonarr 3 "airDateUtc" - try 4's, then 3's, then Sonarr's own order
            string q = "/wanted/missing?page=1&pageSize=40&monitored=true&includeSeries=true";
            string json = null;
            if (_missingSort)
            {
                foreach (string key in new[] { "episodes.airDateUtc", "airDateUtc" })
                    try { json = Get(s, q + "&sortKey=" + key + "&sortDirection=descending"); break; }
                    catch (SonarrException) { }
                if (json == null) _missingSort = false;
            }
            if (json == null) json = Get(s, q);
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement recs;
                snap.MissingTotal = Int(d.RootElement, "totalRecords");
                if (d.RootElement.TryGetProperty("records", out recs) && recs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in recs.EnumerateArray()) snap.Missing.Add(ParseEpisode(e));
            }
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

        static SonarrSeries SeriesById(BarSettings s, int id)
        {
            try { using (JsonDocument d = JsonDocument.Parse(Get(s, "/series/" + id))) return ParseSeries(d.RootElement, false); }
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

        static string QualityOf(JsonElement e)
        {
            return Str(Obj(Obj(e, "quality"), "quality"), "name");
        }

        static SonarrSeries ParseSeries(JsonElement e, bool keepRaw)
        {
            var m = new SonarrSeries
            {
                Id = Int(e, "id"), TvdbId = Int(e, "tvdbId"), Title = Str(e, "title"), Slug = Str(e, "titleSlug"), Overview = Str(e, "overview"),
                Network = Str(e, "network"), Status = Str(e, "status")
            };
            int y = Int(e, "year");
            m.Year = y > 0 ? y.ToString() : "";
            JsonElement st = Obj(e, "statistics");
            m.Seasons = Int(st, "seasonCount");
            m.Episodes = Int(st, "episodeCount");
            m.Files = Int(st, "episodeFileCount");
            if (m.Seasons == 0) m.Seasons = Int(e, "seasonCount");
            JsonElement imgs;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("images", out imgs) && imgs.ValueKind == JsonValueKind.Array)
                foreach (JsonElement i in imgs.EnumerateArray())
                    if (Str(i, "coverType") == "poster") { m.Poster = Str(i, "remoteUrl"); break; }
            if (m.Poster.Length == 0) m.Poster = Str(e, "remotePoster");
            if (keepRaw) m.Raw = e.GetRawText();
            if (m.Id > 0 && m.Title.Length > 0) lock (Shows) Shows[m.Id] = m;
            return m;
        }

        static SonarrEpisode ParseEpisode(JsonElement e)
        {
            var ep = new SonarrEpisode
            {
                Id = Int(e, "id"), SeriesId = Int(e, "seriesId"), Season = Int(e, "seasonNumber"), Number = Int(e, "episodeNumber"), Title = Str(e, "title"),
                Overview = Str(e, "overview"), Airs = When(e, "airDateUtc"), HasFile = Bool(e, "hasFile"), Monitored = Bool(e, "monitored")
            };
            JsonElement sr = Obj(e, "series");
            if (sr.ValueKind == JsonValueKind.Object) ep.Series = ParseSeries(sr, false);
            if (ep.Series.Id == 0) ep.Series.Id = ep.SeriesId;
            return ep;
        }

        static SonarrQueueItem ParseQueue(JsonElement e)
        {
            var q = new SonarrQueueItem
            {
                DownloadId = Str(e, "downloadId"), Release = Str(e, "title"), Status = Str(e, "status").ToLowerInvariant(), Tracked = Str(e, "trackedDownloadStatus").ToLowerInvariant(),
                State = Str(e, "trackedDownloadState"), Client = Str(e, "downloadClient"), Quality = QualityOf(e), Size = Num(e, "size"), Left = Num(e, "sizeleft")
            };
            q.Ids.Add(Int(e, "id"));
            JsonElement sr = Obj(e, "series");
            if (sr.ValueKind == JsonValueKind.Object) q.Series = ParseSeries(sr, false);
            if (q.Series.Id == 0) q.Series.Id = Int(e, "seriesId");
            JsonElement ep = Obj(e, "episode");
            if (ep.ValueKind == JsonValueKind.Object) q.Episodes.Add(ParseEpisode(ep));
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

        static SonarrEvent ParseEvent(JsonElement e)
        {
            var ev = new SonarrEvent { Id = Int(e, "id"), Kind = Str(e, "eventType"), Date = When(e, "date"), Release = Str(e, "sourceTitle"), Quality = QualityOf(e) };
            JsonElement sr = Obj(e, "series");
            if (sr.ValueKind == JsonValueKind.Object) ev.Series = ParseSeries(sr, false);
            if (ev.Series.Id == 0) ev.Series.Id = Int(e, "seriesId");
            JsonElement ep = Obj(e, "episode");
            if (ep.ValueKind == JsonValueKind.Object) ev.Episode = ParseEpisode(ep);
            ev.Episode.SeriesId = ev.Series.Id;
            ev.Message = Str(Obj(e, "data"), "message");
            return ev;
        }

        // =================================================================
        // Notices
        // =================================================================
        static void Compare(SonarrSnapshot snap, BarSettings s)
        {
            Action<string, string, string, string> n = Notice;
            SoSettings c = SoSettings.Of(s);
            int newest = 0;
            foreach (SonarrEvent ev in snap.Recent) newest = Math.Max(newest, ev.Id);
            if (_lastEvent >= 0 && n != null)
            {
                var fresh = snap.Recent.FindAll(delegate (SonarrEvent ev) { return ev.Id > _lastEvent; });
                fresh.Reverse();     // oldest first
                // one notice per show and kind (a season pack is one notice, not ten)
                var groups = new List<KeyValuePair<string, List<SonarrEvent>>>();
                foreach (SonarrEvent ev in fresh)
                {
                    string k = (ev.Imported ? "imported" : ev.Kind) + "|" + ev.Series.Id;
                    int at = groups.FindIndex(delegate (KeyValuePair<string, List<SonarrEvent>> g) { return g.Key == k; });
                    if (at < 0) groups.Add(new KeyValuePair<string, List<SonarrEvent>>(k, new List<SonarrEvent> { ev }));
                    else groups[at].Value.Add(ev);
                }
                foreach (var g in groups)
                {
                    SonarrEvent first = g.Value[0];
                    var eps = g.Value.ConvertAll(delegate (SonarrEvent ev) { return ev.Episode; }).FindAll(delegate (SonarrEpisode ep) { return ep.Number > 0; });
                    string what = first.Series.Title + (eps.Count > 0 ? " " + SonarrEpisode.Codes(eps) : "");
                    string epTitle = eps.Count == 1 && eps[0].Title.Length > 0 ? eps[0].Title : eps.Count > 1 ? eps.Count + " episodes" : "";
                    string url = SeriesUrl(s, first.Series);
                    try
                    {
                        if (first.Imported && c.NotifyImported)
                            n("Sonarr.Imported", "Downloaded: " + what, Join(epTitle, first.Quality), url);
                        else if (first.Kind == "grabbed" && c.NotifyGrabbed)
                            n("Sonarr.Grabbed", "Grabbed: " + what, Join(first.Quality, first.Release), url);
                        else if (first.Kind == "downloadFailed" && c.NotifyFailed)
                            n("Sonarr.Failed", "Download failed: " + what, first.Message.Length > 0 ? first.Message : first.Release, url);
                    }
                    catch { }
                }
            }
            if (newest > 0 || _lastEvent < 0) _lastEvent = Math.Max(_lastEvent, newest);

            bool firstHealth = _seenHealth == null;
            var now = new HashSet<string>();
            foreach (SonarrHealth h in snap.Health)
            {
                string k = h.Source + "|" + h.Message;
                now.Add(k);
                if (firstHealth || _seenHealth.Contains(k) || !c.NotifyHealth || n == null) continue;
                try { n("Sonarr.Health", "Sonarr " + (h.Error ? "error" : "warning"), h.Message, PageUrl(s, "system/status")); } catch { }
            }
            _seenHealth = now;      // one that goes away and comes back is said again
        }

        static string Join(string a, string b)
        {
            if (a.Length == 0) return b;
            return b.Length == 0 ? a : a + " · " + b;
        }

        // =================================================================
        // Actions
        // =================================================================
        /// <summary>Shows matching the text (in the library or not).</summary>
        public static async Task<List<SonarrSeries>> SearchAsync(BarSettings s, string query)
        {
            HttpClient h = Http(s);
            string json;
            using (HttpResponseMessage r = await h.GetAsync(Api(s, "/series/lookup?term=" + Uri.EscapeDataString(query ?? ""))).ConfigureAwait(false))
            {
                json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
            }
            var list = new List<SonarrSeries>();
            using (JsonDocument d = JsonDocument.Parse(json))
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in d.RootElement.EnumerateArray())
                    {
                        SonarrSeries m = ParseSeries(e, true);
                        if (m.TvdbId > 0) list.Add(m);
                    }
            return list;
        }

        /// <summary>Add a show from a search (with the profile, folder and episodes to monitor from the Plugins page). Null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> AddAsync(BarSettings s, SonarrSeries m)
        {
            try
            {
                HttpClient h = Http(s);
                SoSettings c = SoSettings.Of(s);
                // the profile / folder picked on the Plugins page, or Sonarr's first one (also when the picked one is gone)
                int profile = 0, language = 0;
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
                // Sonarr 3 also wants a language profile (Sonarr 4 has none)
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(await GetAsync(h, Api(s, "/languageprofile")).ConfigureAwait(false)))
                        if (d.RootElement.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in d.RootElement.EnumerateArray()) { language = Int(e, "id"); break; }
                }
                catch { }
                if (profile == 0) return "Sonarr has no quality profile.";
                if (folder.Length == 0) return "Sonarr has no root folder - add one in Sonarr (Settings > Media Management).";

                // the search result as Sonarr gave it, plus how to add it
                string body;
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var w = new Utf8JsonWriter(ms))
                    {
                        w.WriteStartObject();
                        bool type = false;
                        if (m.Raw.Length > 0)
                            using (JsonDocument d = JsonDocument.Parse(m.Raw))
                                foreach (JsonProperty p in d.RootElement.EnumerateObject())
                                {
                                    string k = p.Name;
                                    if (k == "qualityProfileId" || k == "languageProfileId" || k == "rootFolderPath" || k == "monitored" || k == "seasonFolder" ||
                                        k == "addOptions" || k == "path" || k == "id") continue;
                                    if (k == "seriesType") type = true;
                                    p.WriteTo(w);
                                }
                        else
                        {
                            w.WriteString("title", m.Title);
                            w.WriteNumber("tvdbId", m.TvdbId);
                        }
                        if (!type) w.WriteString("seriesType", "standard");
                        w.WriteNumber("qualityProfileId", profile);
                        if (language > 0) w.WriteNumber("languageProfileId", language);
                        w.WriteString("rootFolderPath", folder);
                        w.WriteBoolean("monitored", c.MonitorValue != "none");
                        w.WriteBoolean("seasonFolder", c.SeasonFolders);
                        w.WriteStartObject("addOptions");
                        w.WriteString("monitor", c.MonitorValue);
                        w.WriteBoolean("searchForMissingEpisodes", c.SearchOnAdd && c.MonitorValue != "none");
                        w.WriteBoolean("searchForCutoffUnmetEpisodes", false);
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    body = Encoding.UTF8.GetString(ms.ToArray());
                }
                using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage r = await h.PostAsync(Api(s, "/series"), content).ConfigureAwait(false))
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

        /// <summary>Ask Sonarr to search for these episodes. Null when it worked, otherwise what went wrong.</summary>
        public static Task<string> SearchEpisodesAsync(BarSettings s, List<int> episodeIds)
        {
            return CommandAsync(s, "{\"name\":\"EpisodeSearch\",\"episodeIds\":[" + string.Join(",", episodeIds) + "]}");
        }

        /// <summary>Ask Sonarr to search for a show's missing episodes.</summary>
        public static Task<string> SearchSeriesAsync(BarSettings s, int seriesId)
        {
            return CommandAsync(s, "{\"name\":\"SeriesSearch\",\"seriesId\":" + seriesId + "}");
        }

        /// <summary>Search for every missing episode (Sonarr: Wanted > Missing > Search All).</summary>
        public static Task<string> SearchMissingAsync(BarSettings s)
        {
            return CommandAsync(s, "{\"name\":\"MissingEpisodeSearch\"}");
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
        public static async Task<string> RemoveAsync(BarSettings s, SonarrQueueItem q, bool blocklist)
        {
            try
            {
                HttpClient h = Http(s);
                string opts = "removeFromClient=true&blocklist=" + (blocklist ? "true" : "false");
                HttpRequestMessage req;
                if (q.Ids.Count == 1) req = new HttpRequestMessage(HttpMethod.Delete, Api(s, "/queue/" + q.Ids[0] + "?" + opts));
                else req = new HttpRequestMessage(HttpMethod.Delete, Api(s, "/queue/bulk?" + opts))
                {
                    Content = new StringContent("{\"ids\":[" + string.Join(",", q.Ids) + "]}", Encoding.UTF8, "application/json")
                };
                using (req)
                using (HttpResponseMessage r = await h.SendAsync(req).ConfigureAwait(false))
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
                if (app.Length > 0 && !app.Equals("Sonarr", StringComparison.OrdinalIgnoreCase))
                    return new KeyValuePair<bool, string>(false, "That address is " + app + ", not Sonarr.");
                await Task.Run(delegate { ReadSetup(s); }).ConfigureAwait(false);
                Poke();
                return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - Sonarr " + ver : "") + ".");
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
            if (code == 401 || code == 403) return "Sonarr refused the API key (" + code + ")";
            if (code == 404 && msg.Length == 0) return "Sonarr answered 404 - is the address right (with Sonarr's URL base, if it has one)?";
            return "Sonarr answered " + code + (msg.Length > 0 ? ": " + msg : "");
        }

        /// <summary>How long ago (TimeText.Ago); "" when unknown.</summary>
        public static string Ago(DateTime utc) { return utc == DateTime.MinValue ? "" : TimeText.Ago(utc); }
    }
}
