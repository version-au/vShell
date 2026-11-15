using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>A request in Seerr (someone asked for a movie / show).</summary>
    public sealed class SeerrRequest
    {
        public int Id;
        /// <summary>The request: 1 pending approval, 2 approved, 3 declined, 4 failed, 5 completed.</summary>
        public int Status;
        /// <summary>The title's media status: 1 unknown, 2 pending, 3 processing, 4 partly available, 5 available.</summary>
        public int MediaStatus;
        public string MediaType = "";
        public int TmdbId;
        public string Title = "";
        public string Year = "";
        public string Poster = "";
        public List<int> Seasons = new List<int>();
        public string RequestedBy = "";
        public int RequestedById;
        public DateTime Created = DateTime.MinValue;     // UTC
        public DateTime Updated = DateTime.MinValue;     // UTC
        public bool Is4k;

        public bool IsTv { get { return MediaType == "tv"; } }
        public string Key { get { return MediaType + ":" + TmdbId; } }

        /// <summary>"pending", "declined", "failed", "available", "partly", "processing" or "approved".</summary>
        public string State
        {
            get
            {
                if (Status == 1) return "pending";
                if (Status == 3) return "declined";
                if (Status == 4) return "failed";
                if (MediaStatus == 5) return "available";
                if (MediaStatus == 4) return "partly";
                if (MediaStatus == 3) return "processing";
                return Status == 5 ? "available" : "approved";
            }
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case "pending": return "Pending approval";
                    case "declined": return "Declined";
                    case "failed": return "Failed";
                    case "available": return "Available";
                    case "partly": return "Partly available";
                    case "processing": return "Processing";
                    default: return "Approved";
                }
            }
        }

        /// <summary>"Movie" / "TV · S1-3" / "TV · S2, S4".</summary>
        public string KindText
        {
            get
            {
                if (!IsTv) return Is4k ? "Movie · 4K" : "Movie";
                return "TV" + (Seasons.Count > 0 ? " · " + SeasonText(Seasons) : "") + (Is4k ? " · 4K" : "");
            }
        }

        public static string SeasonText(List<int> seasons)
        {
            var l = new List<int>(seasons);
            l.Sort();
            bool run = l.Count > 2;
            for (int i = 1; i < l.Count && run; i++) if (l[i] != l[i - 1] + 1) run = false;
            if (run) return "S" + l[0] + "-" + l[l.Count - 1];
            return string.Join(", ", l.ConvertAll(delegate (int n) { return "S" + n; }));
        }
    }

    /// <summary>Something in the library (Recently added).</summary>
    public sealed class SeerrMedia
    {
        public string MediaType = "";
        public int TmdbId;
        public string Title = "";
        public string Year = "";
        public string Poster = "";
        public int Status;
        public DateTime Added = DateTime.MinValue;   // UTC
        public bool IsTv { get { return MediaType == "tv"; } }
    }

    public sealed class SeerrCounts
    {
        public int Total, Movie, Tv, Pending, Approved, Declined, Processing, Available;
    }

    public sealed class SeerrSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public SeerrCounts Counts = new SeerrCounts();
        /// <summary>The newest requests (everyone's) plus every pending one, newest first.</summary>
        public List<SeerrRequest> Requests = new List<SeerrRequest>();
        /// <summary>The requests waiting for approval, oldest first.</summary>
        public List<SeerrRequest> Pending = new List<SeerrRequest>();
        /// <summary>The newest requests made by "me" (Request as user id, or the API key's user).</summary>
        public List<SeerrRequest> Mine = new List<SeerrRequest>();
        /// <summary>Recently added to the library (only fetched while something shows Seerr).</summary>
        public List<SeerrMedia> Recent = new List<SeerrMedia>();
        public int MeId;
        public string MeName = "";
        /// <summary>The API key's user can approve / decline (Admin or Manage Requests).</summary>
        public bool CanManage;
        public bool Ok { get { return Fetched != DateTime.MinValue; } }
    }

    /// <summary>
    /// Seerr's requests and library, polled in the background: every 30 s while a Seerr module (or the run box) shows them, every
    /// 3 minutes otherwise when notices are on. Titles and posters come from Seerr's movie / tv pages (cached), posters from TMDB.
    /// </summary>
    public static partial class SeerrClient
    {
        static readonly object Gate = new object();
        static SeerrSnapshot _snap = new SeerrSnapshot();
        static Timer _timer;
        static BarSettings _s;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue, _lastPoll = DateTime.MinValue, _retry = DateTime.MinValue, _meAt = DateTime.MinValue;
        static string _meFor = "";
        static int _meId;
        static string _meName = "";
        static bool _canManage;
        static HashSet<int> _seenPending;
        static readonly Dictionary<int, int> _lastMedia = new Dictionary<int, int>();

        sealed class TitleInfo { public string Title = "", Year = "", Poster = ""; }
        static readonly Dictionary<string, TitleInfo> Titles = new Dictionary<string, TitleInfo>();

        /// <summary>A notice: (title line, detail, Seerr page address, bad).</summary>
        public static event Action<string, string, string, bool> Notice;
        public static event Action Changed;

        static string _configuredFor;

        public static void Configure(BarSettings s)
        {
            _s = s;
            SrSettings c = SrSettings.Of(s);
            string id = c.Url.Trim() + "|" + c.ApiKey;
            if (_configuredFor != null && _configuredFor != id)
            {
                // another server / key: start over (no notices for what the new one already has)
                lock (Gate) _snap = new SeerrSnapshot();
                _seenPending = null;
                lock (_lastMedia) _lastMedia.Clear();
                ForgetDiscover();
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
                _snap = new SeerrSnapshot();
            }
            _s = null;
            _configuredFor = null;
            _seenPending = null;
            lock (_lastMedia) _lastMedia.Clear();
        }

        /// <summary>The latest snapshot (asking for it keeps the 30-second polling going).</summary>
        public static SeerrSnapshot Latest
        {
            get
            {
                _wanted = DateTime.UtcNow;
                if (_timer == null && _s != null && Configured(_s)) Configure(_s);
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

        static void Fire(Action a) { if (a != null) try { a(); } catch { } }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                BarSettings s = _s;
                if (s == null || !Configured(s)) return;
                bool shown = (DateTime.UtcNow - _wanted).TotalSeconds < 40;
                SrSettings c = SrSettings.Of(s);
                bool notify = c.NotifyNew || c.NotifyAvailable != "Off";
                if (!shown && !notify) return;
                if (DateTime.UtcNow < _retry) return;
                if ((DateTime.UtcNow - _lastPoll).TotalSeconds < (shown ? 29 : 180)) return;
                _lastPoll = DateTime.UtcNow;
                SeerrSnapshot snap;
                try { snap = ReadAll(s, shown); }
                catch (Exception ex)
                {
                    string msg = ex is TaskCanceledException ? "Seerr didn't answer in time" : ex.Message;
                    SeerrSnapshot old;
                    lock (Gate) old = _snap;
                    if (old.Error != msg) Log.Write("Seerr: " + msg);
                    var keep = new SeerrSnapshot
                    {
                        Error = msg, Fetched = old.Fetched, Counts = old.Counts, Requests = old.Requests, Pending = old.Pending, Mine = old.Mine,
                        Recent = old.Recent, MeId = old.MeId, MeName = old.MeName, CanManage = old.CanManage
                    };
                    _retry = DateTime.UtcNow.AddSeconds(30);
                    lock (Gate) _snap = keep;
                    Fire(Changed);
                    return;
                }
                if (!shown)
                {
                    SeerrSnapshot old;
                    lock (Gate) old = _snap;
                    snap.Recent = old.Recent;      // not fetched for notices alone
                }
                lock (Gate) _snap = snap;
                Compare(snap, s);
                Fire(Changed);
            }
            catch (Exception ex) { Log.Write("Seerr: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static string Get(BarSettings s, string path)
        {
            HttpClient h = Http(s);
            using (HttpResponseMessage r = h.GetAsync(Api(path)).GetAwaiter().GetResult())
            {
                string json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
                return json;
            }
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

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Object ? v : default(JsonElement);
        }

        static DateTime When(JsonElement e, string name)
        {
            string t = e.ValueKind == JsonValueKind.Object ? Str(e, name) : "";
            DateTime d;
            if (t.Length > 0 && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
            return DateTime.MinValue;
        }

        static SeerrSnapshot ReadAll(BarSettings s, bool shown)
        {
            // who the API key is (once in a while): "Mine" and whether Approve / Decline are allowed
            SrSettings c = SrSettings.Of(s);
            string keyId = c.Url + "|" + c.ApiKey;
            if (_meFor != keyId || (DateTime.UtcNow - _meAt).TotalMinutes > 30)
            {
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(Get(s, "/auth/me")))
                    {
                        _meId = Int(d.RootElement, "id");
                        _meName = Str(d.RootElement, "displayName");
                        if (_meName.Length == 0) _meName = Str(d.RootElement, "username") + Str(d.RootElement, "plexUsername");
                        int perms = Int(d.RootElement, "permissions");
                        _canManage = (perms & (2 | 16)) != 0;
                    }
                    _meFor = keyId;
                    _meAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    if (_meFor != keyId) { _meId = 0; _meName = ""; _canManage = true; }   // unknown: offer the buttons, Seerr says no if it isn't allowed
                    if (ex.Message.Contains("refused")) throw;
                }
            }
            var snap = new SeerrSnapshot { Fetched = DateTime.UtcNow, CanManage = _canManage, MeName = _meName };
            snap.MeId = c.UserId > 0 ? c.UserId : _meId;

            using (JsonDocument d = JsonDocument.Parse(Get(s, "/request/count")))
            {
                JsonElement r = d.RootElement;
                snap.Counts = new SeerrCounts
                {
                    Total = Int(r, "total"), Movie = Int(r, "movie"), Tv = Int(r, "tv"), Pending = Int(r, "pending"), Approved = Int(r, "approved"),
                    Declined = Int(r, "declined"), Processing = Int(r, "processing"), Available = Int(r, "available")
                };
            }
            List<SeerrRequest> latest = ReadRequests(s, "/request?take=40&skip=0&filter=all&sort=added&sortDirection=desc");
            snap.Pending = ReadRequests(s, "/request?take=50&skip=0&filter=pending&sort=added&sortDirection=asc");
            if (snap.MeId > 0)
            {
                snap.Mine = ReadRequests(s, "/request?take=30&skip=0&filter=all&sort=modified&sortDirection=desc&requestedBy=" + snap.MeId);
                snap.Mine = snap.Mine.FindAll(delegate (SeerrRequest q) { return q.RequestedById == snap.MeId; });   // older Seerr ignores requestedBy
            }
            var byId = new Dictionary<int, SeerrRequest>();
            foreach (SeerrRequest q in latest) byId[q.Id] = q;
            foreach (SeerrRequest q in snap.Pending) if (!byId.ContainsKey(q.Id)) byId[q.Id] = q;
            snap.Requests = new List<SeerrRequest>(byId.Values);
            snap.Requests.Sort(delegate (SeerrRequest a, SeerrRequest b) { return b.Created.CompareTo(a.Created); });

            if (shown)
            {
                using (JsonDocument d = JsonDocument.Parse(Get(s, "/media?take=24&skip=0&filter=allavailable&sort=mediaAdded")))
                {
                    JsonElement res;
                    if (d.RootElement.TryGetProperty("results", out res) && res.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in res.EnumerateArray())
                        {
                            var m = new SeerrMedia { MediaType = Str(e, "mediaType"), TmdbId = Int(e, "tmdbId"), Status = Int(e, "status"), Added = When(e, "mediaAddedAt") };
                            if (m.Added == DateTime.MinValue) m.Added = When(e, "createdAt");
                            if (m.TmdbId > 0 && (m.MediaType == "movie" || m.MediaType == "tv")) snap.Recent.Add(m);
                        }
                }
            }

            // titles and posters (Seerr's requests only carry the TMDB id)
            var want = new List<KeyValuePair<string, int>>();
            Action<string, int> need = delegate (string type, int id)
            {
                string k = type + ":" + id;
                lock (Titles) if (Titles.ContainsKey(k)) return;
                if (!want.Exists(delegate (KeyValuePair<string, int> x) { return x.Key == type && x.Value == id; })) want.Add(new KeyValuePair<string, int>(type, id));
            };
            foreach (SeerrRequest q in snap.Requests) need(q.MediaType, q.TmdbId);
            foreach (SeerrRequest q in snap.Mine) need(q.MediaType, q.TmdbId);
            foreach (SeerrMedia m in snap.Recent) need(m.MediaType, m.TmdbId);
            if (want.Count > 60) want.RemoveRange(60, want.Count - 60);
            if (want.Count > 0)
            {
                var tasks = new List<Task>();
                var gate = new SemaphoreSlim(6);
                foreach (var w in want)
                {
                    var item = w;
                    tasks.Add(Task.Run(async delegate
                    {
                        await gate.WaitAsync().ConfigureAwait(false);
                        try { await LoadTitle(s, item.Key, item.Value).ConfigureAwait(false); }
                        catch { }
                        finally { gate.Release(); }
                    }));
                }
                try { Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(25)); } catch { }
            }
            Action<SeerrRequest> fill = delegate (SeerrRequest q)
            {
                TitleInfo ti;
                lock (Titles) Titles.TryGetValue(q.Key, out ti);
                if (ti != null) { q.Title = ti.Title; q.Year = ti.Year; q.Poster = ti.Poster; }
                if (q.Title.Length == 0) q.Title = (q.IsTv ? "TV show " : "Movie ") + q.TmdbId;
            };
            snap.Requests.ForEach(fill);
            snap.Pending.ForEach(fill);
            snap.Mine.ForEach(fill);
            foreach (SeerrMedia m in snap.Recent)
            {
                TitleInfo ti;
                lock (Titles) Titles.TryGetValue(m.MediaType + ":" + m.TmdbId, out ti);
                if (ti != null) { m.Title = ti.Title; m.Year = ti.Year; m.Poster = ti.Poster; }
                if (m.Title.Length == 0) m.Title = (m.IsTv ? "TV show " : "Movie ") + m.TmdbId;
            }
            return snap;
        }

        static async Task LoadTitle(BarSettings s, string type, int id)
        {
            HttpClient h = Http(s);
            using (HttpResponseMessage r = await h.GetAsync(Api("/" + type + "/" + id)).ConfigureAwait(false))
            {
                if (!r.IsSuccessStatusCode) return;
                string json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (JsonDocument d = JsonDocument.Parse(json))
                {
                    JsonElement e = d.RootElement;
                    var ti = new TitleInfo { Title = type == "tv" ? Str(e, "name") : Str(e, "title") };
                    if (ti.Title.Length == 0) ti.Title = Str(e, "originalTitle") + Str(e, "originalName");
                    string date = type == "tv" ? Str(e, "firstAirDate") : Str(e, "releaseDate");
                    ti.Year = date.Length >= 4 ? date.Substring(0, 4) : "";
                    string poster = Str(e, "posterPath");
                    ti.Poster = poster.Length > 0 ? "https://image.tmdb.org/t/p/w92" + poster : "";
                    lock (Titles) Titles[type + ":" + id] = ti;
                }
            }
        }

        static List<SeerrRequest> ReadRequests(BarSettings s, string path)
        {
            var list = new List<SeerrRequest>();
            using (JsonDocument d = JsonDocument.Parse(Get(s, path)))
            {
                JsonElement res;
                if (!d.RootElement.TryGetProperty("results", out res) || res.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement e in res.EnumerateArray())
                {
                    JsonElement media = Obj(e, "media"), by = Obj(e, "requestedBy");
                    var q = new SeerrRequest
                    {
                        Id = Int(e, "id"), Status = Int(e, "status"), Created = When(e, "createdAt"), Updated = When(e, "updatedAt"),
                        MediaStatus = Int(media, "status"), TmdbId = Int(media, "tmdbId"), MediaType = media.ValueKind == JsonValueKind.Object ? Str(media, "mediaType") : "",
                        RequestedById = Int(by, "id")
                    };
                    if (q.MediaType.Length == 0) q.MediaType = Str(e, "type");
                    JsonElement k4;
                    q.Is4k = e.TryGetProperty("is4k", out k4) && k4.ValueKind == JsonValueKind.True;
                    if (q.Is4k && media.ValueKind == JsonValueKind.Object && Int(media, "status4k") > 0) q.MediaStatus = Int(media, "status4k");
                    if (by.ValueKind == JsonValueKind.Object)
                    {
                        q.RequestedBy = Str(by, "displayName");
                        if (q.RequestedBy.Length == 0) q.RequestedBy = Str(by, "username");
                        if (q.RequestedBy.Length == 0) q.RequestedBy = Str(by, "plexUsername");
                        if (q.RequestedBy.Length == 0) q.RequestedBy = Str(by, "jellyfinUsername");
                        if (q.RequestedBy.Length == 0) q.RequestedBy = Str(by, "email");
                    }
                    JsonElement ss;
                    if (e.TryGetProperty("seasons", out ss) && ss.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in ss.EnumerateArray())
                        {
                            int n = Int(x, "seasonNumber");
                            if (n > 0 && !q.Seasons.Contains(n)) q.Seasons.Add(n);
                        }
                    if (q.Id > 0 && q.TmdbId > 0) list.Add(q);
                }
            }
            return list;
        }

        /// <summary>Notices: a new request waiting for approval; a request (mine / anyone's) became available.</summary>
        static void Compare(SeerrSnapshot snap, BarSettings s)
        {
            Action<string, string, string, bool> n = Notice;
            SrSettings c = SrSettings.Of(s);
            bool first = _seenPending == null;
            if (first) _seenPending = new HashSet<int>();
            foreach (SeerrRequest q in snap.Pending)
            {
                if (_seenPending.Add(q.Id) && !first && c.NotifyNew && snap.CanManage && q.RequestedById != snap.MeId && n != null)
                    try { n("New request: " + q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : ""), q.KindText + " · requested by " + (q.RequestedBy.Length > 0 ? q.RequestedBy : "someone"), PageUrl(s, q.MediaType, q.TmdbId), false); } catch { }
            }

            var all = new Dictionary<int, SeerrRequest>();
            foreach (SeerrRequest q in snap.Requests) all[q.Id] = q;
            foreach (SeerrRequest q in snap.Mine) all[q.Id] = q;
            var said = new HashSet<string>();
            foreach (SeerrRequest q in all.Values)
            {
                int before;
                bool known;
                lock (_lastMedia)
                {
                    known = _lastMedia.TryGetValue(q.Id, out before);
                    _lastMedia[q.Id] = q.MediaStatus;
                }
                if (!known || first || n == null) continue;
                bool nowAvail = q.MediaStatus == 5 && before < 5;
                bool nowPartly = q.IsTv && q.MediaStatus == 4 && before < 4;
                if (!nowAvail && !nowPartly) continue;
                bool mine = snap.MeId > 0 && q.RequestedById == snap.MeId;
                if (c.NotifyAvailable == "Off" || (c.NotifyAvailable == "Mine" && !mine)) continue;
                if (!said.Add(q.Key)) continue;
                try
                {
                    n((nowAvail ? "Now available: " : "Partly available: ") + q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : ""),
                      q.KindText + (mine ? " · your request" : q.RequestedBy.Length > 0 ? " · requested by " + q.RequestedBy : ""), PageUrl(s, q.MediaType, q.TmdbId), false);
                }
                catch { }
            }
        }

        // =================================================================
        // Approve / decline
        // =================================================================
        /// <summary>"approve" or "decline" a request. Null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> DecideAsync(BarSettings s, int requestId, bool approve)
        {
            try
            {
                HttpClient h = Http(s);
                using (var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
                using (HttpResponseMessage r = await h.PostAsync(Api("/request/" + requestId + "/" + (approve ? "approve" : "decline")), content).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return Fail(r, body);
                }
                _lastPoll = DateTime.MinValue;
                Poke();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>After a request from vShell: look again soon.</summary>
        public static void Requested() { Poke(); }

        // =================================================================
        // Discover (Trending / Upcoming), on demand and cached for 10 minutes
        // =================================================================
        sealed class Found { public DateTime At; public List<SeerrItem> Items; public string Error; public bool Busy; }
        static readonly Dictionary<string, Found> Discover = new Dictionary<string, Found>();

        /// <summary>"Trending" or "Upcoming": the cached list (null while loading the first time; error in error). done runs (on a pool thread) when a fetch finishes.</summary>
        public static List<SeerrItem> DiscoverList(BarSettings s, string which, out string error, Action done)
        {
            error = null;
            Found f;
            lock (Discover)
            {
                Discover.TryGetValue(which, out f);
                bool stale = f == null || (DateTime.UtcNow - f.At).TotalMinutes > (f.Error != null ? 0.5 : 10);
                if (stale && (f == null || !f.Busy))
                {
                    if (f == null) { f = new Found(); Discover[which] = f; }
                    f.Busy = true;
                    Found ff = f;
                    Task.Run(async delegate
                    {
                        List<SeerrItem> items = null;
                        string err = null;
                        try { items = await DiscoverAsync(s, which).ConfigureAwait(false); }
                        catch (Exception ex) { err = ex is TaskCanceledException ? "Seerr didn't answer in time" : ex.Message; }
                        lock (Discover)
                        {
                            ff.Busy = false; ff.At = DateTime.UtcNow; ff.Error = err;
                            if (items != null) ff.Items = items;
                        }
                        if (done != null) try { done(); } catch { }
                    });
                }
                error = f.Error;
                return f.Items;
            }
        }

        public static void ForgetDiscover() { lock (Discover) Discover.Clear(); }

        static async Task<List<SeerrItem>> DiscoverAsync(BarSettings s, string which)
        {
            var list = new List<SeerrItem>();
            if (which == "Upcoming")
            {
                List<SeerrItem> movies = await DiscoverPage(s, "/discover/movies/upcoming?page=1", "movie").ConfigureAwait(false);
                List<SeerrItem> tv = await DiscoverPage(s, "/discover/tv/upcoming?page=1", "tv").ConfigureAwait(false);
                for (int i = 0; i < Math.Max(movies.Count, tv.Count); i++)
                {
                    if (i < movies.Count) list.Add(movies[i]);
                    if (i < tv.Count) list.Add(tv[i]);
                }
            }
            else list = await DiscoverPage(s, "/discover/trending?page=1", null).ConfigureAwait(false);
            return list;
        }

        static async Task<List<SeerrItem>> DiscoverPage(BarSettings s, string path, string type)
        {
            HttpClient h = Http(s);
            string json;
            using (HttpResponseMessage r = await h.GetAsync(Api(path + "&language=en")).ConfigureAwait(false))
            {
                json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
            }
            var list = new List<SeerrItem>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement res;
                if (!doc.RootElement.TryGetProperty("results", out res) || res.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement e in res.EnumerateArray())
                {
                    SeerrItem it = ParseItem(e, type);
                    if (it != null) list.Add(it);
                }
            }
            return list;
        }

        /// <summary>How long ago (TimeText.Ago); "" when unknown.</summary>
        public static string Ago(DateTime utc) { return utc == DateTime.MinValue ? "" : TimeText.Ago(utc); }
    }
}
