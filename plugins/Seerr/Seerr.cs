using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The Seerr (Overseerr / Jellyseerr) connection and notifications - kept in vShell's settings under Plugins["seerr"].</summary>
    public sealed class SrSettings
    {
        /// <summary>Notices when a request becomes available: nobody's, the ones made as "me", or everyone's.</summary>
        public static readonly string[] NotifyAvailableChoices = { "Off", "Mine", "Everyone" };

        readonly SettingsBag _b;
        public SrSettings(BarSettings s) { _b = s.Integration("seerr"); }
        public static SrSettings Of(BarSettings s) { return new SrSettings(s); }

        /// <summary>Address such as http://seerr.local:5055</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>API key (Seerr: Settings > General), stored encrypted for the current Windows user (ApiKeyPlain is the real one).</summary>
        public string ApiKey { get { return _b.Get("ApiKey", ""); } set { _b.Set("ApiKey", Secret.Protect(value ?? "")); } }
        public string ApiKeyPlain { get { return _b.GetSecret("ApiKey"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>Make requests as this Seerr user (their id; 0 = the API key's user, usually the admin).</summary>
        public int UserId { get { return Math.Max(0, _b.Get("UserId", 0)); } set { _b.Set("UserId", Math.Max(0, value)); } }
        /// <summary>The run box keyword (without the !).</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "s").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "s" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        /// <summary>Notices: a new request waits for approval (only when the API key can approve).</summary>
        public bool NotifyNew { get { return _b.Get("NotifyNew", true); } set { _b.Set("NotifyNew", value); } }
        /// <summary>Notices when a request becomes available: Off, Mine or Everyone.</summary>
        public string NotifyAvailable
        {
            get { string v = _b.Get("NotifyAvailable", "Mine"); return Array.IndexOf(NotifyAvailableChoices, v) < 0 ? "Mine" : v; }
            set { _b.Set("NotifyAvailable", value); }
        }
    }

    /// <summary>A Seerr module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class SrView
    {
        /// <summary>The views (tabs) and the request filters.</summary>
        public static readonly string[] Tabs = { "Requests", "Recently added", "Trending", "Upcoming" };
        public static readonly string[] Filters = { "All", "Pending", "Processing", "Available", "Mine" };

        readonly SettingsBag _b;
        public SrView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static SrView Of(ModuleConfig cfg) { return new SrView(cfg); }

        /// <summary>The tabs shown (in the usual order, at least one). One = just that view, with no tab strip.</summary>
        public List<string> TabsShown
        {
            get
            {
                List<string> l = _b.Get<List<string>>("TabsShown", null);
                // before 0.38.17 there was only "Show the tabs": off = just the view it opened on
                if (l == null) l = _b.Get("ShowTabs", true) ? new List<string>(Tabs) : new List<string> { _b.Get("Tab", "Requests") };
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
            get { string v = _b.Get("Tab", "Requests"); List<string> shown = TabsShown; return shown.Contains(v) ? v : shown[0]; }
            set { _b.Set("Tab", value); }
        }
        /// <summary>The requests it starts with: All, Pending, Processing, Available or Mine.</summary>
        public string Filter { get { string v = _b.Get("Filter", "All"); return Array.IndexOf(Filters, v) < 0 ? "All" : v; } set { _b.Set("Filter", value); } }
        public bool ShowFilters { get { return _b.Get("ShowFilters", true); } set { _b.Set("ShowFilters", value); } }
        /// <summary>How many requests / titles the views show.</summary>
        public int Count { get { return Math.Max(1, Math.Min(40, _b.Get("Count", 8))); } set { _b.Set("Count", value); } }
        /// <summary>Posters: how many rows (the posters per row follow from "How many").</summary>
        public int PosterRows { get { return Math.Max(1, Math.Min(10, _b.Get("PosterRows", 2))); } set { _b.Set("PosterRows", value); } }
        public bool Posters { get { return _b.Get("Posters", true); } set { _b.Set("Posters", value); } }
        /// <summary>Bar: the number waiting for approval as a badge on the icon.</summary>
        public bool Badge { get { return _b.Get("Badge", true); } set { _b.Set("Badge", value); } }
        /// <summary>Bar: also the number being processed / available.</summary>
        public bool BarProcessing { get { return _b.Get("BarProcessing", true); } set { _b.Set("BarProcessing", value); } }
        public bool BarAvailable { get { return _b.Get("BarAvailable", false); } set { _b.Set("BarAvailable", value); } }
        /// <summary>The "Seerr" button (opens Seerr's requests page) at the top.</summary>
        public bool ShowOpen { get { return _b.Get("ShowOpen", true); } set { _b.Set("ShowOpen", value); } }
    }

    /// <summary>A search result from Seerr (a movie or a TV show).</summary>
    public sealed class SeerrItem
    {
        public int Id;                    // TMDB id
        public string MediaType = "";     // "movie" / "tv"
        public string Title = "";
        public string Year = "";
        public string Poster = "";        // full image URL (small) or ""
        public string Overview = "";
        /// <summary>Release / first air date ("2026-11-20"), when known.</summary>
        public string Date = "";
        /// <summary>Seerr's media status: 0 none, 1 unknown, 2 pending, 3 processing (requested), 4 partly available, 5 available.</summary>
        public int Status;
        public bool IsTv { get { return MediaType == "tv"; } }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case 5: return "Available";
                    case 4: return "Partly available";
                    case 3: return "Requested";
                    case 2: return "Pending";
                    default: return "Not requested";
                }
            }
        }
        /// <summary>Can still be requested (not requested / partly available TV).</summary>
        public bool CanRequest { get { return Status <= 1 || (IsTv && Status == 4); } }
    }

    /// <summary>A TV show's seasons, for picking which to request.</summary>
    public sealed class SeerrSeason
    {
        public int Number;
        public string Name = "";
        public int Episodes;
        public int Status;       // as SeerrItem.Status, for this season
    }

    /// <summary>
    /// Talks to Seerr (Overseerr / Jellyseerr) with its API key: search, a show's seasons, and making a request.
    /// </summary>
    public static partial class SeerrClient
    {
        static HttpClient _http;
        static string _key = "";
        static string _base = "";
        static bool _ignoreCert;

        static readonly object HttpGate = new object();

        static HttpClient Http(BarSettings s)
        {
            lock (HttpGate) return HttpLocked(s);
        }

        static HttpClient HttpLocked(BarSettings s)
        {
            SrSettings c = SrSettings.Of(s);
            string b = c.Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            string k = c.ApiKeyPlain;
            bool ignoreCert = c.IgnoreCert;
            if (_http == null || b != _base || k != _key || ignoreCert != _ignoreCert)
            {
                var handler = new HttpClientHandler();
                if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
                if (k.Length > 0) h.DefaultRequestHeaders.Add("X-Api-Key", k);
                h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                var old = _http;
                _http = h; _base = b; _key = k; _ignoreCert = ignoreCert;
                if (old != null) try { old.Dispose(); } catch { }
            }
            return _http;
        }

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            SrSettings c = SrSettings.Of(s);
            return !string.IsNullOrWhiteSpace(c.Url) && !string.IsNullOrEmpty(c.ApiKeyPlain);
        }

        static string Api(string path) { return _base + "/api/v1" + path; }

        /// <summary>The page for a title in Seerr.</summary>
        public static string PageUrl(BarSettings s, SeerrItem it)
        {
            return PageUrl(s, it.IsTv ? "tv" : "movie", it.Id);
        }

        public static string PageUrl(BarSettings s, string mediaType, int tmdbId)
        {
            string b = SrSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            if (tmdbId <= 0) return b + "/requests";
            return b + "/" + (mediaType == "tv" ? "tv" : "movie") + "/" + tmdbId;
        }

        static int StatusOf(JsonElement e)
        {
            JsonElement mi, st;
            if (e.TryGetProperty("mediaInfo", out mi) && mi.ValueKind == JsonValueKind.Object && mi.TryGetProperty("status", out st) && st.ValueKind == JsonValueKind.Number)
                return st.GetInt32();
            return 0;
        }

        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            return e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        /// <summary>Movies and shows matching the text (people are left out).</summary>
        public static async Task<List<SeerrItem>> SearchAsync(BarSettings s, string query)
        {
            HttpClient h = Http(s);
            // Seerr wants the query fully encoded (spaces as %20, and characters such as ' ! ( ) too)
            string url = Api("/search?query=" + Uri.EscapeDataString(query ?? "") + "&page=1&language=en");
            string json;
            using (HttpResponseMessage r = await h.GetAsync(url).ConfigureAwait(false))
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
                    SeerrItem it = ParseItem(e, null);
                    if (it != null) list.Add(it);
                }
            }
            return list;
        }

        /// <summary>A movie / show from a search or discover result (null for people and anything else); type = the media type when the result doesn't say.</summary>
        static SeerrItem ParseItem(JsonElement e, string type)
        {
            string t = Str(e, "mediaType");
            if (t.Length == 0) t = type ?? "";
            if (t != "movie" && t != "tv") return null;
            JsonElement idEl;
            if (!e.TryGetProperty("id", out idEl) || idEl.ValueKind != JsonValueKind.Number) return null;
            var it = new SeerrItem { Id = idEl.GetInt32(), MediaType = t };
            it.Title = t == "movie" ? Str(e, "title") : Str(e, "name");
            if (it.Title.Length == 0) it.Title = Str(e, "originalTitle") + Str(e, "originalName");
            string date = t == "movie" ? Str(e, "releaseDate") : Str(e, "firstAirDate");
            it.Year = date.Length >= 4 ? date.Substring(0, 4) : "";
            it.Date = date;
            string poster = Str(e, "posterPath");
            it.Poster = poster.Length > 0 ? "https://image.tmdb.org/t/p/w92" + poster : "";
            it.Overview = Str(e, "overview");
            it.Status = StatusOf(e);
            return it;
        }

        /// <summary>A show's seasons (season 0 "Specials" left out) with each one's status.</summary>
        public static async Task<List<SeerrSeason>> SeasonsAsync(BarSettings s, int tvId)
        {
            HttpClient h = Http(s);
            string json;
            using (HttpResponseMessage r = await h.GetAsync(Api("/tv/" + tvId)).ConfigureAwait(false))
            {
                json = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new Exception(Fail(r, json));
            }
            var list = new List<SeerrSeason>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                var status = new Dictionary<int, int>();
                JsonElement mi, ss;
                if (doc.RootElement.TryGetProperty("mediaInfo", out mi) && mi.ValueKind == JsonValueKind.Object &&
                    mi.TryGetProperty("seasons", out ss) && ss.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement x in ss.EnumerateArray())
                    {
                        JsonElement n, st;
                        if (x.TryGetProperty("seasonNumber", out n) && x.TryGetProperty("status", out st) && n.ValueKind == JsonValueKind.Number && st.ValueKind == JsonValueKind.Number)
                            status[n.GetInt32()] = st.GetInt32();
                    }
                JsonElement seasons;
                if (doc.RootElement.TryGetProperty("seasons", out seasons) && seasons.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement x in seasons.EnumerateArray())
                    {
                        JsonElement n, ec;
                        if (!x.TryGetProperty("seasonNumber", out n) || n.ValueKind != JsonValueKind.Number) continue;
                        int num = n.GetInt32();
                        if (num == 0) continue;
                        int st;
                        status.TryGetValue(num, out st);
                        list.Add(new SeerrSeason
                        {
                            Number = num, Name = Str(x, "name"), Status = st,
                            Episodes = x.TryGetProperty("episodeCount", out ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : 0
                        });
                    }
            }
            return list;
        }

        /// <summary>Request a movie, or a show (seasons = null: all seasons). Returns null when it worked, otherwise what went wrong.</summary>
        public static async Task<string> RequestAsync(BarSettings s, SeerrItem it, List<int> seasons)
        {
            try
            {
                HttpClient h = Http(s);
                var sb = new StringBuilder();
                sb.Append("{\"mediaType\":\"").Append(it.IsTv ? "tv" : "movie").Append("\",\"mediaId\":").Append(it.Id);
                if (it.IsTv)
                {
                    if (seasons == null || seasons.Count == 0) sb.Append(",\"seasons\":\"all\"");
                    else sb.Append(",\"seasons\":[").Append(string.Join(",", seasons)).Append("]");
                }
                int userId = SrSettings.Of(s).UserId;
                if (userId > 0) sb.Append(",\"userId\":").Append(userId);
                sb.Append("}");
                using (var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json"))
                using (HttpResponseMessage r = await h.PostAsync(Api("/request"), content).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (r.IsSuccessStatusCode) return null;
                    return Fail(r, body);
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Test connection: the server's version, or why it failed.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address and the API key first.");
                HttpClient h = Http(s);
                using (HttpResponseMessage r = await h.GetAsync(Api("/settings/main")).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return new KeyValuePair<bool, string>(false, Fail(r, body));
                }
                string ver = "";
                using (HttpResponseMessage r = await h.GetAsync(Api("/status")).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (r.IsSuccessStatusCode)
                        try { using (JsonDocument d = JsonDocument.Parse(body)) ver = Str(d.RootElement, "version"); } catch { }
                }
                return new KeyValuePair<bool, string>(true, "Connected" + (ver.Length > 0 ? " - Seerr " + ver : "") + ".");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, ex.Message); }
        }

        static string Fail(HttpResponseMessage r, string body)
        {
            string msg = "";
            try { using (JsonDocument d = JsonDocument.Parse(body)) msg = Str(d.RootElement, "message"); } catch { }
            int code = (int)r.StatusCode;
            if (code == 401 || code == 403) return "Seerr refused the API key (" + code + ")" + (msg.Length > 0 ? ": " + msg : "");
            return "Seerr answered " + code + (msg.Length > 0 ? ": " + msg : "");
        }
    }
}
