using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The Jellyfin server and its notifications - kept in vShell's settings under Integrations["jellyfin"].</summary>
    public sealed class JfSettings
    {
        readonly SettingsBag _b;
        public JfSettings(BarSettings s) { _b = s.Integration("jellyfin"); }
        public static JfSettings Of(BarSettings s) { return new JfSettings(s); }

        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>API key (Jellyfin: Dashboard > API Keys), stored encrypted for the current Windows user (ApiKeyPlain is the real one).</summary>
        public string ApiKey { get { return _b.Get("ApiKey", ""); } set { _b.Set("ApiKey", Secret.Protect(value ?? "")); } }
        public string ApiKeyPlain { get { return _b.GetSecret("ApiKey"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>The Jellyfin device (client) name of this PC; empty = the computer's name.</summary>
        public string Device { get { return _b.Get("Device", ""); } set { _b.Set("Device", (value ?? "").Trim()); } }
        public bool NotifyPlay { get { return _b.Get("NotifyPlay", false); } set { _b.Set("NotifyPlay", value); } }
        public bool NotifyStop { get { return _b.Get("NotifyStop", false); } set { _b.Set("NotifyStop", value); } }
        /// <summary>Users / devices to leave out (comma separated); this PC's Jellyfin device is always left out.</summary>
        public string NotifyIgnore { get { return _b.Get("NotifyIgnore", ""); } set { _b.Set("NotifyIgnore", value ?? ""); } }
        /// <summary>Notices: something new in the library (a movie, an episode, an album).</summary>
        public bool NotifyNew { get { return _b.Get("NotifyNew", false); } set { _b.Set("NotifyNew", value); } }
        /// <summary>The run box keyword (without the !): !jf words shows the palette's Jellyfin commands for those words.</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "jf").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "jf" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
    }

    /// <summary>A Jellyfin module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class JfView
    {
        readonly SettingsBag _b;
        public JfView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static JfView Of(ModuleConfig cfg) { return new JfView(cfg); }

        /// <summary>The list: who's watching now, recently added, the library's totals.</summary>
        public bool ShowPlaying { get { return _b.Get("ShowPlaying", true); } set { _b.Set("ShowPlaying", value); } }
        public bool ShowRecent { get { return _b.Get("ShowRecent", true); } set { _b.Set("ShowRecent", value); } }
        public bool ShowCounts { get { return _b.Get("ShowCounts", true); } set { _b.Set("ShowCounts", value); } }
        /// <summary>How many recently added items the list shows.</summary>
        public int Count { get { return Math.Max(1, Math.Min(20, _b.Get("Count", 6))); } set { _b.Set("Count", value); } }
        /// <summary>Bar: hidden while nobody is playing anything (and Jellyfin is fine).</summary>
        public bool HideIdle { get { return _b.Get("HideIdle", false); } set { _b.Set("HideIdle", value); } }
        /// <summary>The "Jellyfin" button (opens Jellyfin) at the top.</summary>
        public bool ShowOpen { get { return _b.Get("ShowOpen", true); } set { _b.Set("ShowOpen", value); } }
    }

    /// <summary>A Jellyfin client session that is playing something (from /Sessions).</summary>
    public sealed class JellyfinPlaying
    {
        public string SessionId = "", DeviceName = "", Client = "", UserName = "";
        public string ItemId = "", ImageItemId = "", Title = "", Artist = "", Album = "", Type = "";
        public double Position, Duration;      // seconds
        public bool Paused, CanSeek;
        /// <summary>"DirectPlay", "DirectStream" or "Transcode" ("" = not known).</summary>
        public string PlayMethod = "";
        /// <summary>Why it's transcoding ("VideoCodecNotSupported, AudioCodecNotSupported"), "" = it isn't.</summary>
        public string TranscodeReasons = "";
        /// <summary>An episode: its show and "S02E05" ("" for anything else).</summary>
        public string SeriesName = "", Code = "";

        public bool Transcoding { get { return PlayMethod == "Transcode"; } }
        /// <summary>"Show · S02E05 · Title", or the title (and artist for music).</summary>
        public string Shown
        {
            get
            {
                if (SeriesName.Length > 0) return SeriesName + (Code.Length > 0 ? " · " + Code : "") + " · " + Title;
                return (Type == "Audio" && Artist.Length > 0 ? Artist + " - " : "") + Title;
            }
        }
    }

    /// <summary>Something added to the library lately.</summary>
    public sealed class JellyfinItem
    {
        public string Id = "", Name = "", Type = "", SeriesName = "", Code = "", Year = "";
        public DateTime Added = DateTime.MinValue;      // UTC

        /// <summary>"Show · S02E05 · Title", "Movie (2024)", "Album".</summary>
        public string Shown
        {
            get
            {
                if (SeriesName.Length > 0) return SeriesName + (Code.Length > 0 ? " · " + Code : "") + (Name.Length > 0 ? " · " + Name : "");
                return Name + (Year.Length > 0 && Year != "0" ? " (" + Year + ")" : "");
            }
        }

        public string Kind { get { return Type == "Episode" ? "episode" : Type == "MusicAlbum" ? "album" : Type == "Movie" ? "movie" : Type.ToLowerInvariant(); } }
    }

    /// <summary>What Jellyfin has: sessions playing now, recently added, the totals.</summary>
    public sealed class JellyfinSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public string ServerName = "";
        public List<JellyfinPlaying> Playing = new List<JellyfinPlaying>();
        public List<JellyfinItem> Recent = new List<JellyfinItem>();
        /// <summary>Why Recent couldn't be read ("" = it could).</summary>
        public string RecentError = "";
        public int Movies = -1, Series, Episodes, Albums, Songs;
        public bool Ok { get { return Fetched != DateTime.MinValue; } }
    }

    /// <summary>
    /// Talks to a Jellyfin server with an API key: which clients are playing what and where they are in it, and play / pause /
    /// next / previous / seek for one of them. Used by Now playing for the progress bar (the desktop app doesn't give Windows one).
    /// The calls block: they are made on the media thread.
    /// </summary>
    public static class JellyfinClient
    {
        static HttpClient _http;
        static string _key = "", _base = "";
        static bool _ignoreCert;

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            JfSettings j = JfSettings.Of(s);
            return !string.IsNullOrWhiteSpace(j.Url) && !string.IsNullOrEmpty(j.ApiKeyPlain);
        }

        static string Base(BarSettings s)
        {
            string b = JfSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        static HttpClient Http(BarSettings s)
        {
            JfSettings c = JfSettings.Of(s);
            string b = Base(s);
            string k = c.ApiKeyPlain;
            if (_http == null || b != _base || k != _key || c.IgnoreCert != _ignoreCert)
            {
                var handler = new HttpClientHandler();
                if (c.IgnoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                if (k.Length > 0)
                {
                    h.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "MediaBrowser Token=\"" + k + "\"");
                    h.DefaultRequestHeaders.TryAddWithoutValidation("X-Emby-Token", k);     // older servers
                }
                h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                var old = _http;
                _http = h; _base = b; _key = k; _ignoreCert = c.IgnoreCert;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return _http;
        }

        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static long Long(JsonElement e, string name)
        {
            JsonElement v;
            long n;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out n) ? n : 0;
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
        }

        /// <summary>Every session that has something playing (or paused).</summary>
        public static List<JellyfinPlaying> Playing(BarSettings s)
        {
            var list = new List<JellyfinPlaying>();
            HttpClient h = Http(s);
            string json;
            using (HttpResponseMessage r = h.GetAsync(_base + "/Sessions?activeWithinSeconds=960").GetAwaiter().GetResult())
            {
                json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new Exception("Jellyfin answered " + (int)r.StatusCode);
            }
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement x in doc.RootElement.EnumerateArray())
                {
                    JsonElement item, ps;
                    if (!x.TryGetProperty("NowPlayingItem", out item) || item.ValueKind != JsonValueKind.Object) continue;
                    x.TryGetProperty("PlayState", out ps);
                    var p = new JellyfinPlaying
                    {
                        SessionId = Str(x, "Id"), DeviceName = Str(x, "DeviceName"), Client = Str(x, "Client"), UserName = Str(x, "UserName"),
                        ItemId = Str(item, "Id"), Title = Str(item, "Name"), Album = Str(item, "Album"), Type = Str(item, "Type"),
                        Duration = Long(item, "RunTimeTicks") / 1e7,
                        Position = Long(ps, "PositionTicks") / 1e7, Paused = Bool(ps, "IsPaused"), CanSeek = Bool(ps, "CanSeek"),
                        PlayMethod = Str(ps, "PlayMethod"), SeriesName = Str(item, "SeriesName"), Code = EpisodeCode(item)
                    };
                    JsonElement tr, reasons;
                    if (x.TryGetProperty("TranscodingInfo", out tr) && tr.ValueKind == JsonValueKind.Object && tr.TryGetProperty("TranscodeReasons", out reasons) && reasons.ValueKind == JsonValueKind.Array)
                    {
                        var why = new List<string>();
                        foreach (JsonElement w in reasons.EnumerateArray()) if (w.ValueKind == JsonValueKind.String) why.Add(Spaced(w.GetString()));
                        p.TranscodeReasons = string.Join(", ", why);
                    }
                    JsonElement arts;
                    if (item.TryGetProperty("Artists", out arts) && arts.ValueKind == JsonValueKind.Array)
                    {
                        var names = new List<string>();
                        foreach (JsonElement a in arts.EnumerateArray()) if (a.ValueKind == JsonValueKind.String) names.Add(a.GetString());
                        p.Artist = string.Join(", ", names);
                    }
                    if (p.Artist.Length == 0) p.Artist = Str(item, "AlbumArtist");
                    if (p.Artist.Length == 0) p.Artist = Str(item, "SeriesName");
                    // the picture: the item's own, else its album's
                    JsonElement tags, prim;
                    bool own = item.TryGetProperty("ImageTags", out tags) && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("Primary", out prim);
                    p.ImageItemId = own ? p.ItemId : (Str(item, "AlbumPrimaryImageTag").Length > 0 ? Str(item, "AlbumId") : "");
                    list.Add(p);
                }
            }
            return list;
        }

        /// <summary>"S02E05" for an episode (ParentIndexNumber / IndexNumber), else "".</summary>
        static string EpisodeCode(JsonElement item)
        {
            if (Str(item, "Type") != "Episode") return "";
            long s = Long(item, "ParentIndexNumber"), e = Long(item, "IndexNumber");
            return e > 0 ? "S" + s.ToString("00") + "E" + e.ToString("00") : "";
        }

        /// <summary>"VideoCodecNotSupported" -> "video codec not supported".</summary>
        static string Spaced(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in s ?? "")
            {
                if (char.IsUpper(c) && b.Length > 0) b.Append(' ');
                b.Append(char.ToLowerInvariant(c));
            }
            return b.ToString();
        }

        // =================================================================
        // The module's data: polled with vShell's Poller - every 5 s while a Jellyfin module (or the palette) shows it, every
        // 2 minutes otherwise when "something new" notices are on, not at all otherwise
        // =================================================================
        static Poller _poller;
        static BarSettings _s;
        static JellyfinSnapshot _latest = new JellyfinSnapshot();
        static readonly object Gate = new object();
        static string _userId;
        static DateTime _slowAt = DateTime.MinValue;

        /// <summary>A poll finished (on a worker thread): the "new in the library" notices look at it.</summary>
        public static event Action<JellyfinSnapshot> Polled;

        /// <summary>The latest snapshot (asking for it keeps the 5-second polling going).</summary>
        public static JellyfinSnapshot Latest
        {
            get
            {
                if (_poller != null) _poller.Want();
                lock (Gate) return _latest;
            }
        }

        public static void Configure(BarSettings s)
        {
            _s = s;
            bool on = Configured(s);
            if (!on) { Stop(); return; }
            if (_poller == null)
            {
                _poller = new Poller("Jellyfin", PollAsync, TimeSpan.FromSeconds(5), TimeSpan.Zero);
                _poller.Start();
            }
            _poller.Slow = JfSettings.Of(s).NotifyNew ? TimeSpan.FromMinutes(2) : TimeSpan.Zero;
            _userId = null;
            _poller.PollNow();
        }

        /// <summary>The plugin was removed or isn't set up: stop polling and forget what was read.</summary>
        public static void Stop()
        {
            if (_poller != null) { _poller.Stop(); _poller = null; }
            lock (Gate) _latest = new JellyfinSnapshot();
        }

        /// <summary>Look again now (after a button).</summary>
        public static void Poke() { if (_poller != null) _poller.PollNow(); }

        static Task PollAsync()
        {
            BarSettings s = _s;
            if (s == null || !Configured(s)) return Task.CompletedTask;
            JellyfinSnapshot prev;
            lock (Gate) prev = _latest;
            var snap = new JellyfinSnapshot { ServerName = prev.ServerName };
            try { snap.Playing = Playing(s); }
            catch (Exception ex)
            {
                // keep what was read before, with the reason
                snap = new JellyfinSnapshot { Fetched = prev.Fetched, Error = ex.Message, Playing = prev.Playing, Recent = prev.Recent, RecentError = prev.RecentError,
                                              Movies = prev.Movies, Series = prev.Series, Episodes = prev.Episodes, Albums = prev.Albums, Songs = prev.Songs, ServerName = prev.ServerName };
                lock (Gate) _latest = snap;
                return Task.CompletedTask;
            }
            // the library changes slowly: recently added and the totals every 30 s (and on every slow poll)
            Poller poller = _poller;
            bool slow = (DateTime.UtcNow - _slowAt).TotalSeconds >= 30 || poller == null || !poller.Wanted;
            if (slow)
            {
                _slowAt = DateTime.UtcNow;
                try { snap.Recent = Recent(s, 20); }
                catch (Exception ex) { snap.Recent = prev.Recent; snap.RecentError = ex.Message; }
                try { Counts(s, snap); }
                catch (Exception ex) { Log.Write("Jellyfin counts: " + ex.Message); snap.Movies = prev.Movies; snap.Series = prev.Series; snap.Episodes = prev.Episodes; snap.Albums = prev.Albums; snap.Songs = prev.Songs; }
            }
            else { snap.Recent = prev.Recent; snap.RecentError = prev.RecentError; snap.Movies = prev.Movies; snap.Series = prev.Series; snap.Episodes = prev.Episodes; snap.Albums = prev.Albums; snap.Songs = prev.Songs; }
            snap.Fetched = DateTime.UtcNow;
            lock (Gate) _latest = snap;
            Action<JellyfinSnapshot> polled = Polled;
            if (polled != null && slow) polled(snap);
            return Task.CompletedTask;
        }

        static string GetJson(BarSettings s, string path)
        {
            HttpClient h = Http(s);
            using (HttpResponseMessage r = h.GetAsync(_base + path).GetAwaiter().GetResult())
            {
                string json = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new HttpRequestException("Jellyfin answered " + (int)r.StatusCode);
                return json;
            }
        }

        /// <summary>
        /// The newest movies, episodes and albums. Newer servers answer /Items for the API key itself; older ones want a user, so it asks
        /// again as the first administrator.
        /// </summary>
        static List<JellyfinItem> Recent(BarSettings s, int count)
        {
            const string q = "Items?Recursive=true&SortBy=DateCreated&SortOrder=Descending&IncludeItemTypes=Movie,Episode,MusicAlbum&Fields=DateCreated&EnableImages=false&Limit=";
            string json;
            if (_userId == null)
            {
                try { json = GetJson(s, "/" + q + count); return ParseItems(json); }
                catch (HttpRequestException) { _userId = AdminUser(s); }
            }
            if (string.IsNullOrEmpty(_userId)) throw new Exception("Jellyfin wants a user for the library, and the API key can't list the users.");
            json = GetJson(s, "/Users/" + Uri.EscapeDataString(_userId) + "/" + q + count);
            return ParseItems(json);
        }

        static string AdminUser(BarSettings s)
        {
            try
            {
                using (JsonDocument d = JsonDocument.Parse(GetJson(s, "/Users")))
                {
                    string first = "";
                    if (d.RootElement.ValueKind != JsonValueKind.Array) return "";
                    foreach (JsonElement u in d.RootElement.EnumerateArray())
                    {
                        JsonElement pol;
                        if (first.Length == 0) first = Str(u, "Id");
                        if (u.TryGetProperty("Policy", out pol) && Bool(pol, "IsAdministrator")) return Str(u, "Id");
                    }
                    return first;
                }
            }
            catch (Exception ex) { Log.Write("Jellyfin users: " + ex.Message); return ""; }
        }

        static List<JellyfinItem> ParseItems(string json)
        {
            var list = new List<JellyfinItem>();
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement items;
                if (!d.RootElement.TryGetProperty("Items", out items) || items.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement x in items.EnumerateArray())
                {
                    var it = new JellyfinItem
                    {
                        Id = Str(x, "Id"), Name = Str(x, "Name"), Type = Str(x, "Type"), SeriesName = Str(x, "SeriesName"), Code = EpisodeCode(x),
                        Year = Long(x, "ProductionYear") > 0 ? Long(x, "ProductionYear").ToString() : ""
                    };
                    if (it.Type == "MusicAlbum" && Str(x, "AlbumArtist").Length > 0) it.Name = Str(x, "AlbumArtist") + " - " + it.Name;
                    DateTime added;
                    if (DateTime.TryParse(Str(x, "DateCreated"), System.Globalization.CultureInfo.InvariantCulture,
                                          System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out added)) it.Added = added;
                    list.Add(it);
                }
            }
            return list;
        }

        static void Counts(BarSettings s, JellyfinSnapshot snap)
        {
            using (JsonDocument d = JsonDocument.Parse(GetJson(s, "/Items/Counts")))
            {
                JsonElement r = d.RootElement;
                snap.Movies = (int)Long(r, "MovieCount"); snap.Series = (int)Long(r, "SeriesCount"); snap.Episodes = (int)Long(r, "EpisodeCount");
                snap.Albums = (int)Long(r, "AlbumCount"); snap.Songs = (int)Long(r, "SongCount");
            }
        }

        /// <summary>Jellyfin's web app ("" when not set up), and an item's page in it.</summary>
        public static string WebUrl(BarSettings s) { string b = Base(s); return b.Length == 0 ? "" : b + "/web/"; }
        public static string ItemUrl(BarSettings s, string itemId) { string b = Base(s); return b.Length == 0 ? "" : b + "/web/#/details?id=" + Uri.EscapeDataString(itemId ?? ""); }
        public static string DashboardUrl(BarSettings s) { string b = Base(s); return b.Length == 0 ? "" : b + "/web/#/dashboard"; }

        /// <summary>Play / pause or stop a session, off the UI thread; then look again. Returns null or why not.</summary>
        public static Task<string> ControlAsync(BarSettings s, string sessionId, string command)
        {
            return Task.Run(delegate
            {
                try
                {
                    HttpClient h = Http(s);
                    using (var content = new StringContent(""))
                    using (HttpResponseMessage r = h.PostAsync(_base + "/Sessions/" + Uri.EscapeDataString(sessionId) + "/Playing/" + command, content).GetAwaiter().GetResult())
                        if (!r.IsSuccessStatusCode) return "Jellyfin answered " + (int)r.StatusCode;
                    Poke();
                    return (string)null;
                }
                catch (Exception ex) { return ex.Message; }
            });
        }

        /// <summary>The picture for an item (png / jpeg bytes) or null.</summary>
        public static byte[] Image(BarSettings s, string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            try
            {
                HttpClient h = Http(s);
                using (HttpResponseMessage r = h.GetAsync(_base + "/Items/" + Uri.EscapeDataString(itemId) + "/Images/Primary?maxHeight=300").GetAwaiter().GetResult())
                    return r.IsSuccessStatusCode ? r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() : null;
            }
            catch (Exception ex) { Log.Write("Jellyfin image: " + ex.Message); return null; }
        }

        /// <summary>"PlayPause", "NextTrack", "PreviousTrack", or "Seek" (with the position in seconds) for a session.</summary>
        public static void Command(BarSettings s, string sessionId, string command, double seconds = 0)
        {
            try
            {
                HttpClient h = Http(s);
                string url = _base + "/Sessions/" + Uri.EscapeDataString(sessionId) + "/Playing/" + command;
                if (command == "Seek") url += "?seekPositionTicks=" + (long)(Math.Max(0, seconds) * 1e7);
                using (var content = new StringContent(""))
                using (HttpResponseMessage r = h.PostAsync(url, content).GetAwaiter().GetResult())
                    if (!r.IsSuccessStatusCode) Log.Write("Jellyfin " + command + ": the server answered " + (int)r.StatusCode);
            }
            catch (Exception ex) { Log.Write("Jellyfin " + command + ": " + ex.Message); }
        }

        /// <summary>Test connection: the server's name and version, or why it failed.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address and the API key first.");
                HttpClient h = Http(s);
                string name = "", ver = "";
                using (HttpResponseMessage r = await h.GetAsync(_base + "/System/Info").ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    int code = (int)r.StatusCode;
                    if (code == 401 || code == 403) return new KeyValuePair<bool, string>(false, "Jellyfin refused the API key (" + code + ").");
                    if (!r.IsSuccessStatusCode) return new KeyValuePair<bool, string>(false, "Jellyfin answered " + code + ".");
                    try { using (JsonDocument d = JsonDocument.Parse(body)) { name = Str(d.RootElement, "ServerName"); ver = Str(d.RootElement, "Version"); } } catch { }
                }
                int playing = 0;
                try { playing = await Task.Run(delegate { return Playing(s).Count; }).ConfigureAwait(false); } catch { }
                return new KeyValuePair<bool, string>(true, "Connected" + (name.Length > 0 ? " to " + name : "") + (ver.Length > 0 ? " (Jellyfin " + ver + ")" : "") +
                                                            " - " + playing + " playing now.");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, ex.Message); }
        }
    }
}
