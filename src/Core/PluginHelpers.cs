using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>Reading values out of JSON without the checks every time: a missing or odd value is NaN / "".</summary>
    [PluginApi]
    public static class JsonValue
    {
        /// <summary>A number (also "12.5" as text, true / false as 1 / 0), or NaN.</summary>
        public static double Num(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return double.NaN;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.True) return 1;
            if (v.ValueKind == JsonValueKind.False) return 0;
            double d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return double.NaN;
        }

        /// <summary>Text (a number as it's written), or "".</summary>
        public static string Str(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return "";
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "";
        }

        /// <summary>true / false (also "true", "on", 1), or the fallback.</summary>
        public static bool Bool(JsonElement e, string name, bool fallback = false)
        {
            JsonElement v;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out v)) return fallback;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble() != 0;
            if (v.ValueKind == JsonValueKind.String)
            {
                string s = (v.GetString() ?? "").Trim().ToLowerInvariant();
                if (s == "true" || s == "on" || s == "yes" || s == "1") return true;
                if (s == "false" || s == "off" || s == "no" || s == "0") return false;
            }
            return fallback;
        }
    }

    /// <summary>
    /// HttpClients for plugins: one per name (and certificate setting), kept for the whole run like vShell's own - making a new one per
    /// request uses up sockets. They send vShell's User-Agent.
    /// </summary>
    [PluginApi]
    public static class PluginHttp
    {
        static readonly Dictionary<string, HttpClient> Clients = new Dictionary<string, HttpClient>();

        /// <summary>
        /// The client for name ("uptimekuma"). ignoreCert = accept a self-signed certificate (a server at home). followRedirects: it follows
        /// them itself (not https -> http; on a redirect .NET sends the request's headers again, except Authorization) - false for one that
        /// GetFollowAsync steers (it decides each hop, and where an API key header may go).
        /// </summary>
        public static HttpClient Client(string name, bool ignoreCert = false, int timeoutSeconds = 15, bool followRedirects = true)
        {
            string key = (name ?? "") + "|" + ignoreCert + "|" + timeoutSeconds + "|" + followRedirects;
            lock (Clients)
            {
                HttpClient c;
                if (Clients.TryGetValue(key, out c)) return c;
                var h = new HttpClientHandler { AllowAutoRedirect = followRedirects };
                if (ignoreCert) h.ServerCertificateCustomValidationCallback = delegate { return true; };
                c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "vShell/" + Updater.CurrentText);
                Clients[key] = c;
                return c;
            }
        }

        /// <summary>
        /// GET, following redirects - also https -> http, which .NET won't (a reverse proxy in front of a device often does that). headers:
        /// extra request headers (an API key), or null - sent only to the same host and port, and never over plain http after https (a
        /// redirect elsewhere gets the request without them). Use a client with followRedirects: false so every hop comes through here.
        /// </summary>
        public static async Task<HttpResponseMessage> GetFollowAsync(HttpClient client, string url, IDictionary<string, string> headers = null, CancellationToken ct = default(CancellationToken))
        {
            Uri first = new Uri(url), at = first;
            for (int i = 0; i < 6; i++)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, at);
                bool sameServer = string.Equals(at.Host, first.Host, StringComparison.OrdinalIgnoreCase) && at.Port == first.Port &&
                                  !(first.Scheme == Uri.UriSchemeHttps && at.Scheme != Uri.UriSchemeHttps);
                if (headers != null && sameServer) foreach (KeyValuePair<string, string> kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                HttpResponseMessage r = await client.SendAsync(req, ct);
                int code = (int)r.StatusCode;
                if (code < 300 || code > 399 || r.Headers.Location == null) return r;
                Uri next = r.Headers.Location.IsAbsoluteUri ? r.Headers.Location : new Uri(at, r.Headers.Location);
                r.Dispose();
                at = next;
            }
            throw new HttpRequestException("too many redirects");
        }

        /// <summary>GET as text (following redirects); throws on an error status.</summary>
        public static async Task<string> GetStringAsync(HttpClient client, string url, IDictionary<string, string> headers = null, CancellationToken ct = default(CancellationToken))
        {
            using (HttpResponseMessage r = await GetFollowAsync(client, url, headers, ct))
            {
                r.EnsureSuccessStatusCode();
                return await r.Content.ReadAsStringAsync(ct);
            }
        }
    }

    /// <summary>
    /// Asks a server for news in the background: quickly while a module shows it (call Want() each time the module reads the data), slowly
    /// otherwise (for notifications). The poll runs on a worker thread; an exception is logged and it tries again next time.
    /// </summary>
    [PluginApi]
    public sealed class Poller : IDisposable
    {
        readonly Func<Task> _poll;
        readonly string _name;
        CancellationTokenSource _stop;
        readonly SemaphoreSlim _wake = new SemaphoreSlim(0, 1);      // at most one "poll now" waiting: pressing it 5 times is one poll
        long _wantedAt;

        /// <summary>How often while wanted, and while not (TimeSpan.Zero = not at all).</summary>
        public TimeSpan Fast { get; set; }
        public TimeSpan Slow { get; set; }
        /// <summary>How long Want() keeps it fast (30 s).</summary>
        public TimeSpan WantedFor { get; set; }
        /// <summary>When the last poll finished (UTC; MinValue = not yet).</summary>
        public DateTime LastPoll { get; private set; }

        public Poller(string name, Func<Task> poll, TimeSpan fast, TimeSpan slow)
        {
            _name = name ?? "Poller";
            _poll = poll;
            Fast = fast;
            Slow = slow;
            WantedFor = TimeSpan.FromSeconds(30);
            LastPoll = DateTime.MinValue;
        }

        /// <summary>A module wants the data: fast for the next WantedFor (the first call after a quiet spell polls straight away).</summary>
        public void Want()
        {
            bool was = Wanted;
            Interlocked.Exchange(ref _wantedAt, DateTime.UtcNow.Ticks);
            if (!was) PollNow();
        }

        public bool Wanted { get { return DateTime.UtcNow.Ticks - Interlocked.Read(ref _wantedAt) < WantedFor.Ticks; } }

        public bool Running { get { return _stop != null; } }

        /// <summary>Poll now (a setting changed, a button was pressed) instead of waiting.</summary>
        public void PollNow() { try { _wake.Release(); } catch (SemaphoreFullException) { } }

        public void Start()
        {
            if (_stop != null) return;
            var stop = _stop = new CancellationTokenSource();
            Task.Run(async delegate
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try { await _poll(); }
                        catch (Exception ex) { Log.Write(_name + ": " + ex.Message); }
                        LastPoll = DateTime.UtcNow;
                        if (stop.IsCancellationRequested) return;       // stopped during the poll
                        TimeSpan wait = Wanted ? Fast : Slow;
                        try
                        {
                            if (wait <= TimeSpan.Zero) await _wake.WaitAsync(stop.Token);
                            else await _wake.WaitAsync(wait, stop.Token);
                        }
                        catch (OperationCanceledException) { return; }
                    }
                }
                finally { stop.Dispose(); }     // the loop owns it: Stop only cancels (a poll may still be running)
            });
        }

        public void Stop()
        {
            CancellationTokenSource s = _stop;
            _stop = null;
            if (s != null) s.Cancel();
        }

        public void Dispose() { Stop(); }
    }
}
