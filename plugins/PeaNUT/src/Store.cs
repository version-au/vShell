using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using vShell.Core;

namespace VShellPeanut
{
    /// <summary>
    /// Polls every PeaNUT server in the background and keeps the latest result of each.
    /// Modules, the popup, the palette and the notification watcher only read <see cref="Snapshot"/>.
    /// </summary>
    public static class Store
    {
        static readonly object Gate = new object();
        static PeanutServer[] _servers = new PeanutServer[0];
        static Dictionary<string, ServerResult> _results = new Dictionary<string, ServerResult>();
        static Dictionary<string, string> _renames = new Dictionary<string, string>();   // replaced whole, never changed
        static Poller _poller;
        static int _version;

        /// <summary>Goes up whenever a poll brings something new - modules redraw when it changes.</summary>
        public static int Version => Volatile.Read(ref _version);

        public static void Start(TimeSpan fast, TimeSpan slow)
        {
            if (_poller != null) { _poller.Fast = fast; _poller.Slow = slow; return; }
            _poller = new Poller("PeaNUT", PollAll, fast, slow);
            _poller.Start();
        }

        public static void Stop()
        {
            var p = _poller;
            _poller = null;
            if (p != null) p.Stop();
        }

        public static void SetTimes(TimeSpan fast, TimeSpan slow)
        {
            var p = _poller;
            if (p == null) return;
            p.Fast = fast;
            p.Slow = slow;
        }

        /// <summary>A module is showing: poll at the fast rate for the next 30 s.</summary>
        public static void Want()
        {
            var p = _poller;
            if (p != null) p.Want();
        }

        public static void PollNow()
        {
            var p = _poller;
            if (p != null) p.PollNow();
        }

        /// <summary>Called from OnSettings (UI thread) with the list as saved - takes a copy for the background poller.</summary>
        public static void SetServers(IEnumerable<PeanutServer> servers)
        {
            var copy = servers.Where(s => s != null).Select(s => s.Copy()).ToArray();
            bool changed;
            lock (Gate)
            {
                changed = !Same(_servers, copy);
                _servers = copy;
                var keep = new Dictionary<string, ServerResult>();
                foreach (var s in copy)
                {
                    ServerResult r;
                    if (s.Enabled && _results.TryGetValue(s.Id, out r))
                    {
                        r.Server = s;
                        foreach (var u in r.Ups) u.ServerName = s.Shown;
                        PeanutParse.Name(r.Ups, s, copy.Count(x => x.Enabled) > 1, _renames);
                        keep[s.Id] = r;
                    }
                }
                _results = keep;
            }
            Interlocked.Increment(ref _version);
            if (changed) PollNow();
        }

        /// <summary>Called from OnSettings and the rename list with the names the user gave UPSes - renames what's shown now too.</summary>
        public static void SetRenames(IEnumerable<UpsRename> renames)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in renames)
                if (r != null && r.Key.Length > 0 && r.Name.Trim().Length > 0) map[r.Key] = r.Name.Trim();
            lock (Gate)
            {
                if (map.Count == _renames.Count && map.All(p => _renames.TryGetValue(p.Key, out string v) && v == p.Value)) return;
                _renames = map;
                bool many = _servers.Count(x => x.Enabled) > 1;
                foreach (var r in _results.Values) PeanutParse.Name(r.Ups, r.Server, many, map);
            }
            Interlocked.Increment(ref _version);
        }

        static Dictionary<string, string> Renames
        {
            get { lock (Gate) return _renames; }
        }

        static bool Same(PeanutServer[] a, PeanutServer[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                var x = a[i]; var y = b[i];
                if (x.Id != y.Id || x.Url != y.Url || x.User != y.User || x.Password != y.Password
                    || x.IgnoreCert != y.IgnoreCert || x.Enabled != y.Enabled || x.Name != y.Name) return false;
            }
            return true;
        }

        public static PeanutServer[] Servers
        {
            get { lock (Gate) return _servers; }
        }

        /// <summary>The latest result of every enabled server, in the order they were added.</summary>
        public static List<ServerResult> Snapshot()
        {
            lock (Gate)
            {
                var list = new List<ServerResult>();
                foreach (var s in _servers)
                {
                    if (!s.Enabled) continue;
                    ServerResult r;
                    list.Add(_results.TryGetValue(s.Id, out r) ? r : new ServerResult { Server = s });
                }
                return list;
            }
        }

        /// <summary>Every UPS from every enabled server, in order.</summary>
        public static List<UpsReading> AllUps()
        {
            var list = new List<UpsReading>();
            foreach (var r in Snapshot()) list.AddRange(r.Ups);
            return list;
        }

        static async Task PollAll()
        {
            PeanutServer[] servers;
            lock (Gate) servers = _servers;
            var on = servers.Where(s => s.Enabled && s.BaseUrl.Length > 0).ToArray();
            if (on.Length == 0) return;
            ServerResult[] got = await Task.WhenAll(on.Select(s => Fetch(s))).ConfigureAwait(false);
            lock (Gate)
            {
                foreach (var r in got)
                {
                    // Dropped or changed while we were asking: ignore this answer.
                    var now = _servers.FirstOrDefault(s => s.Id == r.Server.Id);
                    if (now == null || !now.Enabled) continue;
                    if (!r.Ok)
                    {
                        // Keep the last good readings (marked stale) so modules still show the UPSes.
                        ServerResult old;
                        if (_results.TryGetValue(r.Server.Id, out old))
                        {
                            foreach (var u in old.Ups)
                            {
                                var s = Clone(u);
                                s.Stale = true;
                                r.Ups.Add(s);
                            }
                        }
                    }
                    r.Server = now;
                    // Renamed while we were asking: name them by what's saved now.
                    if (r.Ok) PeanutParse.Name(r.Ups, now, _servers.Count(x => x.Enabled) > 1, _renames);
                    _results[now.Id] = r;
                }
            }
            Interlocked.Increment(ref _version);
        }

        static UpsReading Clone(UpsReading u)
        {
            return new UpsReading
            {
                ServerId = u.ServerId, ServerName = u.ServerName, NutServer = u.NutServer, Device = u.Device,
                Name = u.Name, AutoName = u.AutoName, Stale = u.Stale, At = u.At, Vars = new Dictionary<string, string>(u.Vars, StringComparer.Ordinal)
            };
        }

        /// <summary>Asks one server for its UPSes. Never throws - a failure comes back as Ok = false with the reason.</summary>
        public static async Task<ServerResult> Fetch(PeanutServer s)
        {
            var r = new ServerResult { Server = s, Checked = DateTime.UtcNow };
            try
            {
                HttpClient http = PluginHttp.Client("PeaNUT", s.IgnoreCert, 10, false);
                string user = s.User.Trim();
                string password = Secret.Unprotect(s.Password ?? "");
                string auth = PeanutParse.BasicAuth(user, password);
                string url = PeanutParse.DevicesUrl(s);
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
                {
                    var got = await Get(http, url, auth, cts.Token).ConfigureAwait(false);
                    using (HttpResponseMessage resp = got.Response)
                    {
                        if (resp.StatusCode != HttpStatusCode.OK)
                        {
                            r.Error = PeanutParse.HttpError(resp.StatusCode);
                            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                                r.Error = Why401(resp, auth, user, password, got.AuthDropped, got.Final, s);
                            else if (got.Final != url)
                                r.Error += " (redirected to " + got.Final + ")";
                            Log.Write("PeaNUT " + s.Shown + ": " + r.Error);
                            return r;
                        }
                        string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        r.Ups = PeanutParse.Devices(json, s, DateTime.UtcNow);
                        PeanutParse.Name(r.Ups, s, Servers.Count(x => x.Enabled) > 1, Renames);
                        r.Ok = true;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                r.Error = "No answer in time - is PeaNUT running at " + s.BaseUrl + "?";
            }
            catch (HttpRequestException ex)
            {
                r.Error = "Can't connect: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
            catch (UriFormatException)
            {
                r.Error = "That address doesn't look right: " + s.Url;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            if (!r.Ok) r.Ups.Clear();
            return r;
        }

        sealed class Got
        {
            public HttpResponseMessage Response;
            public string Final;
            public bool AuthDropped;
        }

        /// <summary>
        /// GET that follows redirects itself and keeps sending the login to the same host - also when a reverse proxy
        /// moves http to https or to another port (vShell's GetFollowAsync drops it then, which PeaNUT answers with 401).
        /// Never to another host, never from https down to http.
        /// </summary>
        static async Task<Got> Get(HttpClient http, string url, string auth, CancellationToken ct)
        {
            Uri first = new Uri(url), at = first;
            bool dropped = false;
            for (int i = 0; i < 6; i++)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, at);
                bool same = string.Equals(at.Host, first.Host, StringComparison.OrdinalIgnoreCase)
                            && !(first.Scheme == Uri.UriSchemeHttps && at.Scheme != Uri.UriSchemeHttps);
                if (auth != null)
                {
                    if (same) req.Headers.TryAddWithoutValidation("Authorization", auth);
                    else dropped = true;
                }
                HttpResponseMessage resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                int code = (int)resp.StatusCode;
                if (code < 300 || code > 399 || resp.Headers.Location == null)
                    return new Got { Response = resp, Final = at.ToString(), AuthDropped = dropped };
                Uri next = resp.Headers.Location.IsAbsoluteUri ? resp.Headers.Location : new Uri(at, resp.Headers.Location);
                resp.Dispose();
                at = next;
            }
            throw new HttpRequestException("too many redirects");
        }

        /// <summary>Works out why a 401 happened, in words that say what to change.</summary>
        static string Why401(HttpResponseMessage resp, string auth, string user, string password, bool dropped, string final, PeanutServer s)
        {
            if (auth == null)
                return "PeaNUT wants a username and password (401) - add them, or run PeaNUT with AUTH_DISABLED=true";
            if (dropped)
                return "Redirected to another host (" + final + "), so the login wasn't sent - put PeaNUT's own address in instead";
            string challenge = resp.Headers.WwwAuthenticate.ToString();
            if (challenge.Length > 0)
                return "The 401 came from something in front of PeaNUT (" + challenge + "), not PeaNUT itself - " +
                       "a reverse proxy or login page wants its own login. Use PeaNUT's direct address (e.g. http://<ip>:8080) instead";
            if (user.Contains(":") || password.Contains(":"))
                return "PeaNUT turned the login down (401): PeaNUT's API cuts a username or password at the first ':' - " +
                       "change the PeaNUT password to one without ':'";
            foreach (char c in user + password)
                if (c > 127)
                    return "PeaNUT turned the login down (401): PeaNUT's API only reads plain ASCII logins - " +
                           "the username or password has an accented or special character it can't read";
            return "PeaNUT turned the username or password down (401) at " + final + ". Check they work on PeaNUT's login page. " +
                   "If they do (or this works with PeaNUT's IP address but not through a reverse proxy): PeaNUT checks a login made on " +
                   "its setup page by calling itself at the proxied address, which often fails from inside its container - set " +
                   "WEB_USERNAME and WEB_PASSWORD on the PeaNUT container (checked directly) and use those, or use the IP address";
        }
    }
}
