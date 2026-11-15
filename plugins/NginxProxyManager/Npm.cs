using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using vShell.Core;
using JsonValue = vShell.Core.JsonValue;

namespace vShell.Npm
{
    /// <summary>The Nginx Proxy Manager connection and its notifications (Settings > Plugins), kept under Integrations["npm"].</summary>
    sealed class NpmSettings
    {
        readonly SettingsBag _b;
        public NpmSettings(BarSettings s) { _b = s.Integration("npm"); }
        public static NpmSettings Of(BarSettings s) { return new NpmSettings(s); }

        /// <summary>The admin address, such as http://192.168.2.90:81.</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        public string Email { get { return _b.Get("Email", ""); } set { _b.Set("Email", (value ?? "").Trim()); } }
        /// <summary>Stored encrypted (PasswordPlain is the real one).</summary>
        public string Password { get { return _b.Get("Password", ""); } set { _b.Set("Password", Secret.Protect(value ?? "")); } }
        public string PasswordPlain { get { return _b.GetSecret("Password"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        public string Bang
        {
            get { string v = _b.Get("Bang", "npm").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "npm" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }

        /// <summary>The signed-in session (a JWT, encrypted) and when it runs out - kept so a restart doesn't need a 2FA code.</summary>
        public string TokenPlain { get { return _b.GetSecret("Token"); } set { _b.SetSecret("Token", value ?? ""); } }
        public string TokenExpires { get { return _b.Get("TokenExpires", ""); } set { _b.Set("TokenExpires", value ?? ""); } }

        public bool NotifyCerts { get { return _b.Get("NotifyCerts", true); } set { _b.Set("NotifyCerts", value); } }
        public double CertDays { get { return Math.Max(1, Math.Min(60, _b.Get("CertDays", 14.0))); } set { _b.Set("CertDays", value); } }
        /// <summary>Only certificates an enabled host uses (an old one nothing uses doesn't nag).</summary>
        public bool OnlyUsedCerts { get { return _b.Get("OnlyUsedCerts", true); } set { _b.Set("OnlyUsedCerts", value); } }
        public bool NotifyErrors { get { return _b.Get("NotifyErrors", true); } set { _b.Set("NotifyErrors", value); } }
        /// <summary>Check each enabled host's backend (forward host and port) from this PC.</summary>
        public bool CheckBackends { get { return _b.Get("CheckBackends", true); } set { _b.Set("CheckBackends", value); } }
        public bool NotifyDown { get { return _b.Get("NotifyDown", true); } set { _b.Set("NotifyDown", value); } }
        public bool NotifyUpdate { get { return _b.Get("NotifyUpdate", true); } set { _b.Set("NotifyUpdate", value); } }

        /// <summary>What has been said and still applies (one key a line), so a restart doesn't say it again.</summary>
        public string Said { get { return _b.Get("Said", ""); } set { _b.Set("Said", value ?? ""); } }
    }

    /// <summary>A module's settings (cfg.Bag).</summary>
    sealed class NpmView
    {
        readonly SettingsBag _b;
        public NpmView(ModuleConfig cfg) { _b = cfg.Bag; }
        public bool Icon { get { return _b.Get("Icon", true); } set { _b.Set("Icon", value); } }
        public bool HideFine { get { return _b.Get("HideFine", false); } set { _b.Set("HideFine", value); } }
    }

    enum Backend { Unchecked, Unknown, Up, Down }

    sealed class NpmHost
    {
        public long Id;
        public List<string> Domains = new List<string>();
        public string ForwardScheme = "http", ForwardHost = "";
        public int ForwardPort;
        public long CertId, AccessListId;
        public bool Enabled, Online = true;
        public bool SslForced, Http2, BlockExploits, Websockets;
        public string NginxErr = "", Modified = "";
        public Backend Backend;

        public string Domain { get { return Domains.Count > 0 ? Domains[0] : "host " + Id; } }
        public string SiteUrl { get { return (CertId > 0 ? "https://" : "http://") + Domain; } }
        public string Target { get { return ForwardScheme + "://" + ForwardHost + ":" + ForwardPort; } }
    }

    sealed class NpmCert
    {
        public long Id;
        public string Name = "", Provider = "";
        public List<string> Domains = new List<string>();
        public DateTime Expires = DateTime.MinValue;
        public bool LetsEncrypt { get { return Provider == "letsencrypt"; } }
        public double DaysLeft { get { return Expires == DateTime.MinValue ? double.PositiveInfinity : (Expires - DateTime.UtcNow).TotalDays; } }

        public string DaysText
        {
            get
            {
                double d = DaysLeft;
                if (double.IsPositiveInfinity(d)) return "no expiry date";
                if (d < 0) return "expired";
                if (d < 1) return "expires today";
                int n = (int)Math.Floor(d);
                return n == 1 ? "1 day left" : n + " days left";
            }
        }
    }

    sealed class NpmAccessList
    {
        public long Id;
        public string Name = "";
    }

    /// <summary>A proxy host to add (the Add a host form).</summary>
    sealed class NewHost
    {
        public List<string> Domains = new List<string>();
        public string Scheme = "http", Host = "";
        public int Port;
        /// <summary>0 = none, -1 = a new Let's Encrypt certificate, otherwise an existing certificate's id.</summary>
        public long CertId;
        public bool ForceSsl = true, Http2 = true, BlockExploits = true, Websockets = true;
        public long AccessListId;
    }

    sealed class NpmSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        /// <summary>Signing in needs a 2FA code (Settings > Plugins, or click the notification).</summary>
        public bool NeedCode;
        public List<NpmHost> Hosts = new List<NpmHost>();
        public List<NpmCert> Certs = new List<NpmCert>();
        public List<NpmAccessList> AccessLists = new List<NpmAccessList>();
        public string Current = "", Latest = "";
        public bool Update;
        public bool Ok { get { return Fetched != DateTime.MinValue; } }

        public NpmCert Cert(long id)
        {
            if (id <= 0) return null;
            foreach (NpmCert c in Certs) if (c.Id == id) return c;
            return null;
        }

        /// <summary>Is the certificate used by an enabled host?</summary>
        public bool Used(NpmCert c)
        {
            foreach (NpmHost h in Hosts) if (h.Enabled && h.CertId == c.Id) return true;
            return false;
        }

        /// <summary>What's wrong with a host ("" = nothing): nginx couldn't load it, the backend is down, its certificate is running out.</summary>
        public string Problem(NpmHost h, double certDays)
        {
            if (!h.Enabled) return "";
            if (!h.Online) return "nginx error" + (h.NginxErr.Length > 0 ? ": " + h.NginxErr : "");
            if (h.Backend == Backend.Down) return "backend down (" + h.ForwardHost + ":" + h.ForwardPort + ")";
            NpmCert c = Cert(h.CertId);
            if (c != null && c.DaysLeft < certDays) return "certificate: " + c.DaysText;
            return "";
        }

        public int Problems(double certDays)
        {
            int n = 0;
            foreach (NpmHost h in Hosts) if (Problem(h, certDays).Length > 0) n++;
            return n;
        }
    }

    /// <summary>Signing in needs a 2FA code.</summary>
    sealed class NeedCodeException : Exception
    {
        public NeedCodeException() : base("Nginx Proxy Manager wants your 2FA code - enter it in Settings > Plugins > Nginx Proxy Manager") { }
    }

    /// <summary>
    /// Talks to Nginx Proxy Manager's API (its admin address + /api). Signs in with the email and password (and a 2FA code when the
    /// account has one), keeps the session token and refreshes it before it runs out. Polled every 30 s while a module or the run box
    /// shows it, every 2 minutes otherwise. Snapshots are swapped in on the UI thread, where the notifications are worked out.
    /// </summary>
    static class NpmClient
    {
        static readonly object Gate = new object();
        static NpmSnapshot _snap = new NpmSnapshot();
        static Poller _poll;
        static BarSettings _s;
        static string _configuredFor;
        static volatile bool _needCode;
        static DateTime _versionAt = DateTime.MinValue;
        static string _current = "", _latest = "";
        static bool _update;
        /// <summary>Failed backend checks in a row, by host id (down after 2).</summary>
        static readonly Dictionary<long, int> _fails = new Dictionary<long, int>();

        /// <summary>A new snapshot (on the UI thread).</summary>
        public static event Action<NpmSnapshot> Changed;

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            NpmSettings c = NpmSettings.Of(s);
            return c.Url.Length > 0 && c.Email.Length > 0 && c.Password.Length > 0;
        }

        static string Base(BarSettings s)
        {
            string b = NpmSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) b = b.Substring(0, b.Length - 4);
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        /// <summary>The admin web UI (a page of it: "/nginx/proxy").</summary>
        public static string AdminUrl(BarSettings s, string page) { return Base(s) + (page ?? "/"); }

        public static void Configure(BarSettings s)
        {
            _s = s;
            NpmSettings c = NpmSettings.Of(s);
            string id = Base(s) + "|" + c.Email + "|" + c.Password;
            if (_configuredFor != null && _configuredFor != id)
            {
                // another server / account: sign in again and start over
                c.TokenPlain = ""; c.TokenExpires = "";
                _needCode = false;
                _versionAt = DateTime.MinValue;
                lock (_fails) _fails.Clear();
                lock (Gate) _snap = new NpmSnapshot();
            }
            _configuredFor = id;
            if (!Configured(s)) return;
            if (_poll == null)
            {
                _poll = new Poller("Nginx Proxy Manager", Poll, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
                _poll.Start();
            }
            else _poll.PollNow();
        }

        public static void Stop()
        {
            if (_poll != null) { _poll.Stop(); _poll = null; }
            lock (Gate) _snap = new NpmSnapshot();
            lock (_fails) _fails.Clear();
            _s = null;
            _configuredFor = null;
        }

        /// <summary>The latest snapshot (asking for it keeps the faster polling going).</summary>
        public static NpmSnapshot Latest
        {
            get
            {
                if (_poll != null) _poll.Want();
                lock (Gate) return _snap;
            }
        }

        public static void PollNow() { if (_poll != null) _poll.PollNow(); }

        static async Task Poll()
        {
            BarSettings s = _s;
            if (s == null || !Configured(s)) return;
            NpmSnapshot old;
            lock (Gate) old = _snap;
            NpmSnapshot snap;
            if (_needCode) snap = Keep(old, "Needs your 2FA code", true);      // no signing in again and again until there's a code
            else
                try { snap = await ReadAll(s).ConfigureAwait(false); }
                catch (NeedCodeException) { _needCode = true; snap = Keep(old, "Needs your 2FA code", true); }
                catch (Exception ex)
                {
                    string msg = Why(ex);
                    if (old.Error != msg) Log.Write("Nginx Proxy Manager: " + msg);
                    snap = Keep(old, msg, false);
                }
            Publish(snap);
        }

        static NpmSnapshot Keep(NpmSnapshot old, string error, bool needCode)
        {
            return new NpmSnapshot
            {
                Error = error, NeedCode = needCode, Fetched = old.Fetched, Hosts = old.Hosts, Certs = old.Certs, AccessLists = old.AccessLists,
                Current = old.Current, Latest = old.Latest, Update = old.Update
            };
        }

        static void Publish(NpmSnapshot snap)
        {
            System.Windows.Application app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_s == null) return;      // removed meanwhile
                lock (Gate) _snap = snap;
                Action<NpmSnapshot> c = Changed;
                if (c != null) try { c(snap); } catch (Exception ex) { Log.Write("Nginx Proxy Manager: " + ex.Message); }
            }));
        }

        /// <summary>Set by the plugin: save the settings (called on the UI thread when the session token changed).</summary>
        public static Action Save;

        static void SaveSoon()
        {
            System.Windows.Application app = System.Windows.Application.Current;
            Action save = Save;
            if (app != null && save != null) app.Dispatcher.BeginInvoke(save);
        }

        // =================================================================
        // Signing in
        // =================================================================
        static HttpClient Http(BarSettings s, bool slow)
        {
            bool ic = NpmSettings.Of(s).IgnoreCert;
            return PluginHttp.Client("NginxProxyManager" + (slow ? "-slow" : "") + (ic ? "-ic" : ""), ic, slow ? 180 : 20, true);
        }

        /// <summary>A token to use: the kept one, refreshed when it has under 12 hours left, or a new sign-in.</summary>
        static async Task<string> Token(BarSettings s)
        {
            NpmSettings c = NpmSettings.Of(s);
            string token = c.TokenPlain;
            DateTime exp = Date(c.TokenExpires);
            if (token.Length > 0 && exp > DateTime.UtcNow.AddMinutes(5))
            {
                if (exp > DateTime.UtcNow.AddHours(12)) return token;
                try
                {
                    using (JsonDocument d = await Send(s, HttpMethod.Get, "/tokens", null, token, false).ConfigureAwait(false))
                        return Keep(s, d.RootElement);
                }
                catch (Exception ex) { Log.Write("Nginx Proxy Manager: refreshing the session: " + Why(ex)); }
            }
            return await SignIn(s, null).ConfigureAwait(false);
        }

        static string Keep(BarSettings s, JsonElement e)
        {
            string token = JsonValue.Str(e, "token");
            if (token.Length == 0) throw new Exception("Nginx Proxy Manager didn't send a session token");
            NpmSettings c = NpmSettings.Of(s);
            c.TokenPlain = token;
            c.TokenExpires = JsonValue.Str(e, "expires");
            SaveSoon();
            return token;
        }

        /// <summary>Sign in with the email and password (and the 2FA code, when given and asked for).</summary>
        static async Task<string> SignIn(BarSettings s, string code)
        {
            NpmSettings c = NpmSettings.Of(s);
            string body = "{\"identity\":" + J(c.Email) + ",\"secret\":" + J(c.PasswordPlain) + ",\"scope\":\"user\"}";
            using (JsonDocument d = await Send(s, HttpMethod.Post, "/tokens", body, null, false).ConfigureAwait(false))
            {
                JsonElement e = d.RootElement;
                if (!JsonValue.Bool(e, "requires_2fa")) { _needCode = false; return Keep(s, e); }
                if (string.IsNullOrWhiteSpace(code)) throw new NeedCodeException();
                string b2 = "{\"challenge_token\":" + J(JsonValue.Str(e, "challenge_token")) + ",\"code\":" + J(code.Trim().Replace(" ", "")) + "}";
                using (JsonDocument d2 = await Send(s, HttpMethod.Post, "/tokens/2fa", b2, null, false).ConfigureAwait(false))
                {
                    _needCode = false;
                    return Keep(s, d2.RootElement);
                }
            }
        }

        /// <summary>Sign in with a 2FA code (from Settings or the notification). Null when it worked.</summary>
        public static async Task<string> EnterCodeAsync(BarSettings s, string code)
        {
            try
            {
                await SignIn(s, code).ConfigureAwait(false);
                PollNow();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        static async Task<JsonDocument> Send(BarSettings s, HttpMethod method, string path, string body, string token, bool slow)
        {
            HttpClient h = Http(s, slow);
            using (var req = new HttpRequestMessage(method, Base(s) + "/api" + path))
            {
                if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.ParseAdd("application/json");
                if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage r = await h.SendAsync(req).ConfigureAwait(false))
                {
                    string text = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) throw new NpmError((int)r.StatusCode, Fail((int)r.StatusCode, text, path));
                    return JsonDocument.Parse(text.Length == 0 ? "null" : text);
                }
            }
        }

        sealed class NpmError : Exception
        {
            public readonly int Code;
            public NpmError(int code, string msg) : base(msg) { Code = code; }
        }

        /// <summary>A call with the session; a 401 (the session ended) signs in again once.</summary>
        static async Task<JsonDocument> Call(BarSettings s, HttpMethod method, string path, string body = null, bool slow = false)
        {
            string token = await Token(s).ConfigureAwait(false);
            try { return await Send(s, method, path, body, token, slow).ConfigureAwait(false); }
            catch (NpmError e)
            {
                if (e.Code != 401) throw;
                NpmSettings.Of(s).TokenPlain = "";
                token = await SignIn(s, null).ConfigureAwait(false);
                return await Send(s, method, path, body, token, slow).ConfigureAwait(false);
            }
        }

        // =================================================================
        // Reading
        // =================================================================
        static async Task<NpmSnapshot> ReadAll(BarSettings s)
        {
            var snap = new NpmSnapshot();
            using (JsonDocument d = await Call(s, HttpMethod.Get, "/nginx/proxy-hosts").ConfigureAwait(false))
                foreach (JsonElement e in Arr(d.RootElement))
                {
                    JsonElement meta = Obj(e, "meta");
                    var h = new NpmHost
                    {
                        Id = (long)Zero(JsonValue.Num(e, "id")), ForwardScheme = JsonValue.Str(e, "forward_scheme"), ForwardHost = JsonValue.Str(e, "forward_host"),
                        ForwardPort = (int)Zero(JsonValue.Num(e, "forward_port")), CertId = (long)Zero(JsonValue.Num(e, "certificate_id")),
                        Enabled = JsonValue.Bool(e, "enabled", true), Online = JsonValue.Bool(meta, "nginx_online", true),
                        NginxErr = JsonValue.Str(meta, "nginx_err"), Modified = JsonValue.Str(e, "modified_on"),
                        AccessListId = (long)Zero(JsonValue.Num(e, "access_list_id")), SslForced = JsonValue.Bool(e, "ssl_forced"),
                        Http2 = JsonValue.Bool(e, "http2_support"), BlockExploits = JsonValue.Bool(e, "block_exploits"),
                        Websockets = JsonValue.Bool(e, "allow_websocket_upgrade")
                    };
                    if (h.ForwardScheme.Length == 0) h.ForwardScheme = "http";
                    foreach (JsonElement n in Arr(e, "domain_names")) if (n.ValueKind == JsonValueKind.String) h.Domains.Add(n.GetString());
                    snap.Hosts.Add(h);
                }
            snap.Hosts.Sort(delegate (NpmHost a, NpmHost b) { return string.Compare(a.Domain, b.Domain, StringComparison.OrdinalIgnoreCase); });

            using (JsonDocument d = await Call(s, HttpMethod.Get, "/nginx/certificates").ConfigureAwait(false))
                foreach (JsonElement e in Arr(d.RootElement))
                {
                    var c = new NpmCert
                    {
                        Id = (long)Zero(JsonValue.Num(e, "id")), Name = JsonValue.Str(e, "nice_name"), Provider = JsonValue.Str(e, "provider"),
                        Expires = Date(JsonValue.Str(e, "expires_on"))
                    };
                    foreach (JsonElement n in Arr(e, "domain_names")) if (n.ValueKind == JsonValueKind.String) c.Domains.Add(n.GetString());
                    if (c.Name.Length == 0) c.Name = c.Domains.Count > 0 ? c.Domains[0] : "certificate " + c.Id;
                    snap.Certs.Add(c);
                }

            // access lists (for the Add a host form)
            try
            {
                using (JsonDocument d = await Call(s, HttpMethod.Get, "/nginx/access-lists").ConfigureAwait(false))
                    foreach (JsonElement e in Arr(d.RootElement))
                        snap.AccessLists.Add(new NpmAccessList { Id = (long)Zero(JsonValue.Num(e, "id")), Name = JsonValue.Str(e, "name") });
            }
            catch (Exception ex) { Log.Write("Nginx Proxy Manager: access lists: " + Why(ex)); }

            // a new version: asked every 6 hours (NPM asks GitHub)
            if ((DateTime.UtcNow - _versionAt).TotalHours >= 6)
            {
                _versionAt = DateTime.UtcNow;
                try
                {
                    using (JsonDocument d = await Call(s, HttpMethod.Get, "/version/check").ConfigureAwait(false))
                    {
                        _current = JsonValue.Str(d.RootElement, "current");
                        _latest = JsonValue.Str(d.RootElement, "latest");
                        _update = JsonValue.Bool(d.RootElement, "update_available");
                    }
                }
                catch (Exception ex) { Log.Write("Nginx Proxy Manager: version check: " + Why(ex)); }
            }
            snap.Current = _current; snap.Latest = _latest; snap.Update = _update;

            if (NpmSettings.Of(s).CheckBackends) await Probe(snap).ConfigureAwait(false);
            snap.Fetched = DateTime.UtcNow;
            return snap;
        }

        /// <summary>Each enabled host's backend: does its forward host take a connection on its port (from this PC)? Down after two
        /// failed checks in a row; Unknown when this PC can't look the name up (a Docker container name) or it's NPM's own machine.</summary>
        static async Task Probe(NpmSnapshot snap)
        {
            var jobs = new List<Task>();
            foreach (NpmHost h in snap.Hosts)
            {
                if (!h.Enabled || h.ForwardPort <= 0) continue;
                NpmHost host = h;
                jobs.Add(Task.Run(async delegate
                {
                    Backend b = await Check(host.ForwardHost, host.ForwardPort).ConfigureAwait(false);
                    int fails = 0;
                    lock (_fails)
                    {
                        if (b == Backend.Down) { _fails.TryGetValue(host.Id, out fails); _fails[host.Id] = ++fails; }
                        else _fails.Remove(host.Id);
                    }
                    host.Backend = b == Backend.Down && fails < 2 ? Backend.Up : b;     // one failed check isn't down yet
                }));
            }
            await Task.WhenAll(jobs).ConfigureAwait(false);
        }

        static async Task<Backend> Check(string host, int port)
        {
            string hl = (host ?? "").Trim().ToLowerInvariant();
            if (hl.Length == 0 || hl == "localhost" || hl.StartsWith("127.") || hl == "::1") return Backend.Unknown;
            IPAddress[] addrs;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                try { addrs = await Dns.GetHostAddressesAsync(hl, cts.Token).ConfigureAwait(false); }
                catch { return Backend.Unknown; }
            }
            if (addrs.Length == 0) return Backend.Unknown;
            using (var tcp = new TcpClient(addrs[0].AddressFamily))
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                try { await tcp.ConnectAsync(addrs[0], port, cts.Token).ConfigureAwait(false); return Backend.Up; }
                catch { return Backend.Down; }
            }
        }

        // =================================================================
        // Actions
        // =================================================================
        /// <summary>Turn a proxy host on / off. Null when it worked.</summary>
        public static async Task<string> EnableAsync(BarSettings s, NpmHost h, bool on)
        {
            try
            {
                using (await Call(s, HttpMethod.Post, "/nginx/proxy-hosts/" + h.Id + (on ? "/enable" : "/disable")).ConfigureAwait(false)) { }
                PollNow();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Delete a proxy host (for good). Null when it worked.</summary>
        public static async Task<string> DeleteHostAsync(BarSettings s, NpmHost h)
        {
            try
            {
                using (await Call(s, HttpMethod.Delete, "/nginx/proxy-hosts/" + h.Id).ConfigureAwait(false)) { }
                PollNow();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Delete a certificate. Null when it worked.</summary>
        public static async Task<string> DeleteCertAsync(BarSettings s, long certId)
        {
            try
            {
                using (await Call(s, HttpMethod.Delete, "/nginx/certificates/" + certId).ConfigureAwait(false)) { }
                PollNow();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Renew a certificate (Let's Encrypt; can take a minute). Null when it worked.</summary>
        public static async Task<string> RenewAsync(BarSettings s, long certId)
        {
            try
            {
                using (await Call(s, HttpMethod.Post, "/nginx/certificates/" + certId + "/renew", null, true).ConfigureAwait(false)) { }
                PollNow();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>
        /// Add a proxy host. With a new Let's Encrypt certificate the host is made first (served over http), then the certificate is asked
        /// for and put on it - so when Let's Encrypt fails, the host is still there. Returns (the new host's id or 0, why adding failed,
        /// why the certificate failed); the errors are null when that part worked.
        /// </summary>
        public static async Task<Tuple<long, string, string>> AddHostAsync(BarSettings s, NewHost n)
        {
            long id;
            try
            {
                JsonObject body = Body(n, 0);
                body["caching_enabled"] = false;
                body["advanced_config"] = "";
                body["locations"] = new JsonArray();
                body["meta"] = new JsonObject();
                using (JsonDocument d = await Call(s, HttpMethod.Post, "/nginx/proxy-hosts", body.ToJsonString()).ConfigureAwait(false))
                    id = (long)Zero(JsonValue.Num(d.RootElement, "id"));
            }
            catch (Exception ex) { return Tuple.Create(0L, Why(ex), (string)null); }
            PollNow();
            if (n.CertId != -1) return Tuple.Create(id, (string)null, (string)null);
            string certErr = await RequestCertAsync(s, id, n.Domains, n.ForceSsl, n.Http2).ConfigureAwait(false);
            return Tuple.Create(id, (string)null, certErr);
        }

        /// <summary>
        /// Change a proxy host to what the form says (only the form's fields: its custom locations, advanced config and caching stay as they
        /// are). With a new Let's Encrypt certificate, the host keeps its old one until the new one is there. Returns (why saving failed,
        /// why the certificate failed); null when that part worked.
        /// </summary>
        public static async Task<Tuple<string, string>> UpdateHostAsync(BarSettings s, NpmHost h, NewHost n)
        {
            try
            {
                using (await Call(s, HttpMethod.Put, "/nginx/proxy-hosts/" + h.Id, Body(n, h.CertId).ToJsonString()).ConfigureAwait(false)) { }
            }
            catch (Exception ex) { return Tuple.Create(Why(ex), (string)null); }
            PollNow();
            if (n.CertId != -1) return Tuple.Create((string)null, (string)null);
            return Tuple.Create((string)null, await RequestCertAsync(s, h.Id, n.Domains, n.ForceSsl, n.Http2).ConfigureAwait(false));
        }

        /// <summary>The form's fields as JSON. keepCert = the certificate to keep while a new one is asked for (CertId -1).</summary>
        static JsonObject Body(NewHost n, long keepCert)
        {
            long cert = n.CertId == -1 ? keepCert : n.CertId;
            return new JsonObject
            {
                ["domain_names"] = Strings(n.Domains),
                ["forward_scheme"] = n.Scheme,
                ["forward_host"] = n.Host,
                ["forward_port"] = n.Port,
                ["certificate_id"] = cert > 0 ? cert : 0,
                ["ssl_forced"] = cert > 0 && n.ForceSsl,
                ["http2_support"] = cert > 0 && n.Http2,
                ["block_exploits"] = n.BlockExploits,
                ["allow_websocket_upgrade"] = n.Websockets,
                ["access_list_id"] = n.AccessListId
            };
        }

        /// <summary>Get a new Let's Encrypt certificate for a host's domains and put it on the host (http challenge; can take a minute).
        /// Null when it worked.</summary>
        public static async Task<string> RequestCertAsync(BarSettings s, long hostId, List<string> domains, bool forceSsl, bool http2)
        {
            try
            {
                var cert = new JsonObject
                {
                    ["provider"] = "letsencrypt",
                    ["domain_names"] = Strings(domains),
                    ["meta"] = new JsonObject { ["dns_challenge"] = false }
                };
                long certId;
                using (JsonDocument d = await Call(s, HttpMethod.Post, "/nginx/certificates", cert.ToJsonString(), true).ConfigureAwait(false))
                    certId = (long)Zero(JsonValue.Num(d.RootElement, "id"));
                if (certId <= 0) return "Nginx Proxy Manager didn't say which certificate it made";
                var put = new JsonObject { ["certificate_id"] = certId, ["ssl_forced"] = forceSsl, ["http2_support"] = http2 };
                using (await Call(s, HttpMethod.Put, "/nginx/proxy-hosts/" + hostId, put.ToJsonString()).ConfigureAwait(false)) { }
                PollNow();
                return null;
            }
            catch (Exception ex) { PollNow(); return Why(ex); }
        }

        static JsonArray Strings(List<string> items)
        {
            var a = new JsonArray();
            foreach (string i in items) a.Add(i);
            return a;
        }

        /// <summary>Test connection: signs in and counts the hosts and certificates, or says why it can't.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address, email and password first.");
                NpmSnapshot snap = await ReadAll(s).ConfigureAwait(false);
                _needCode = false;
                PollNow();
                int soon = 0;
                foreach (NpmCert c in snap.Certs) if (c.DaysLeft < NpmSettings.Of(s).CertDays) soon++;
                return new KeyValuePair<bool, string>(true,
                    "Connected" + (snap.Current.Length > 0 ? " - Nginx Proxy Manager " + snap.Current : "") + ", " +
                    Count(snap.Hosts.Count, "proxy host") + ", " + Count(snap.Certs.Count, "certificate") +
                    (soon > 0 ? " (" + soon + " running out soon)" : "") + ".");
            }
            catch (NeedCodeException) { _needCode = true; return new KeyValuePair<bool, string>(false, "Your account has 2FA: press Enter 2FA code below."); }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, Why(ex)); }
        }

        public static string Count(int n, string what) { return n == 1 ? "1 " + what : n + " " + what + "s"; }

        // ---- helpers ----
        static string Fail(int code, string body, string path)
        {
            string msg = "";
            try
            {
                using (JsonDocument d = JsonDocument.Parse(body))
                    msg = JsonValue.Str(Obj(d.RootElement, "error"), "message");
            }
            catch { }
            if (code == 401 && path.StartsWith("/tokens")) return "Nginx Proxy Manager didn't accept the email / password" + (msg.Length > 0 ? " (" + msg + ")" : "");
            if (code == 404) return "Nginx Proxy Manager answered 404 - is the address its admin page's (port 81 by default)?";
            if (msg.Length == 0 && body != null && body.Length < 200 && !body.TrimStart().StartsWith("<")) msg = body.Trim();
            return "Nginx Proxy Manager answered " + code + (msg.Length > 0 ? ": " + msg : "");
        }

        public static string Why(Exception ex)
        {
            if (ex is TaskCanceledException) return "Nginx Proxy Manager didn't answer in time";
            if (ex is HttpRequestException && ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        static string J(string v) { return JsonSerializer.Serialize(v ?? ""); }
        static double Zero(double v) { return double.IsNaN(v) ? 0 : v; }

        static DateTime Date(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d) ? d : DateTime.MinValue;
        }

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Object ? v : default(JsonElement);
        }

        static IEnumerable<JsonElement> Arr(JsonElement e, string name = null)
        {
            JsonElement v = e;
            if (name != null && !(e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v))) yield break;
            if (v.ValueKind == JsonValueKind.Array)
                foreach (JsonElement i in v.EnumerateArray()) yield return i;
        }
    }
}
