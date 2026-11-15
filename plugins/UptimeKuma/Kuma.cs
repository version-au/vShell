using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using vShell.Core;

namespace UptimeKuma
{
    /// <summary>A monitor's state, as Uptime Kuma's /metrics says it.</summary>
    public enum MonitorState { Down = 0, Up = 1, Pending = 2, Maintenance = 3, Unknown = 9 }

    public sealed class KumaMonitor
    {
        public string Name = "", Type = "", Url = "";
        public MonitorState State = MonitorState.Unknown;
        /// <summary>Last response time in ms (NaN = not known).</summary>
        public double ResponseMs = double.NaN;
        /// <summary>Days until its certificate expires (NaN = no certificate / not known).</summary>
        public double CertDays = double.NaN;
    }

    /// <summary>One read of /metrics: the monitors, or why it couldn't be read.</summary>
    public sealed class KumaSnapshot
    {
        public List<KumaMonitor> Monitors = new List<KumaMonitor>();
        public string Error;
        public DateTime At = DateTime.UtcNow;

        public int Count(MonitorState s) { int n = 0; foreach (KumaMonitor m in Monitors) if (m.State == s) n++; return n; }
    }

    /// <summary>The connection: Settings > Plugins > Uptime Kuma (kept in the settings under Integrations["uptimekuma"]).</summary>
    public sealed class KumaSettings
    {
        public const string Id = "uptimekuma";
        readonly SettingsBag _b;
        KumaSettings(SettingsBag b) { _b = b; }
        public static KumaSettings Of(BarSettings s) { return new KumaSettings(s.Integration(Id)); }

        public string Url { get { return Tidy(_b.Get("Url", "")); } set { _b.Set("Url", Tidy(value)); } }

        /// <summary>"192.168.1.20:3001/" -> "http://192.168.1.20:3001" (and no "/dashboard" or "/metrics" on the end).</summary>
        static string Tidy(string url)
        {
            string u = (url ?? "").Trim().TrimEnd('/');
            if (u.Length == 0) return u;
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = "http://" + u;
            foreach (string tail in new[] { "/dashboard", "/metrics" })
                if (u.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) u = u.Substring(0, u.Length - tail.Length);
            return u.TrimEnd('/');
        }
        /// <summary>The API key as stored (encrypted).</summary>
        public string KeyStored { get { return _b.Get("Key", ""); } set { _b.Set("Key", value ?? ""); } }
        public string Key { get { return _b.GetSecret("Key"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>How often while a module shows it (seconds).</summary>
        public int Seconds { get { return Math.Max(5, Math.Min(600, _b.Get("Seconds", 20))); } set { _b.Set("Seconds", value); } }
        public bool NotifyDown { get { return _b.Get("NotifyDown", true); } set { _b.Set("NotifyDown", value); } }
        public bool NotifyServer { get { return _b.Get("NotifyServer", true); } set { _b.Set("NotifyServer", value); } }
        /// <summary>Wait this long before saying a monitor is down (a blip doesn't notify).</summary>
        public int NotifyDelay { get { return Math.Max(0, _b.Get("NotifyDelay", 60)); } set { _b.Set("NotifyDelay", value); } }

        public bool Configured { get { return Url.Length > 0 && KeyStored.Length > 0; } }
    }

    /// <summary>Reads Uptime Kuma's /metrics in the background (fast while a module shows it, every minute for notifications).</summary>
    public static class Kuma
    {
        static Poller _poller;
        static volatile KumaSnapshot _latest;
        static string _url = "", _key = "";
        static bool _ignoreCert, _notifyDown, _notifyServer;
        static int _delay;
        static Dictionary<string, MonitorState> _last;      // for notifications: what each monitor was at the last read

        /// <summary>A notification to post (title, detail, url, kind key).</summary>
        public static event Action<string, string, string, string> Notice;

        /// <summary>The newest read (null = none yet). Reading it keeps the polling fast.</summary>
        public static KumaSnapshot Latest
        {
            get { Poller p = _poller; if (p != null) p.Want(); return _latest; }
        }

        public static string DashboardUrl { get { return _url.Length > 0 ? _url + "/dashboard" : ""; } }

        public static void Configure(BarSettings s)
        {
            KumaSettings k = KumaSettings.Of(s);
            bool changed = k.Url != _url || k.Key != _key || k.IgnoreCert != _ignoreCert;
            _url = k.Url; _key = k.Key; _ignoreCert = k.IgnoreCert;
            _notifyDown = k.NotifyDown; _notifyServer = k.NotifyServer; _delay = k.NotifyDelay;
            if (!k.Configured) { Stop(); _latest = null; return; }
            if (_poller == null)
            {
                _poller = new Poller("Uptime Kuma", Poll, TimeSpan.FromSeconds(k.Seconds), TimeSpan.FromSeconds(60));
                _poller.Start();
            }
            _poller.Fast = TimeSpan.FromSeconds(k.Seconds);
            // with no notifications on, nothing to do while no module shows it
            _poller.Slow = _notifyDown || _notifyServer ? TimeSpan.FromSeconds(60) : TimeSpan.Zero;
            if (changed) { _last = null; _latest = null; _poller.PollNow(); }
        }

        public static void Stop()
        {
            if (_poller != null) { _poller.Stop(); _poller = null; }
            _last = null;
        }

        static async Task Poll()
        {
            string url = _url, key = _key;
            if (url.Length == 0) return;
            KumaSnapshot snap = await ReadAsync(url, key, _ignoreCert);
            _latest = snap;
            Compare(snap);
        }

        /// <summary>Settings > Plugins > Test connection.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            KumaSettings k = KumaSettings.Of(s);
            if (!k.Configured) return new KeyValuePair<bool, string>(false, "Fill in the address and the API key first.");
            KumaSnapshot snap = await ReadAsync(k.Url, k.Key, k.IgnoreCert);
            if (snap.Error != null) return new KeyValuePair<bool, string>(false, snap.Error);
            return new KeyValuePair<bool, string>(true, "Connected - " + snap.Monitors.Count + " monitor" + (snap.Monitors.Count == 1 ? "" : "s") +
                                                         ", " + snap.Count(MonitorState.Down) + " down.");
        }

        static async Task<KumaSnapshot> ReadAsync(string url, string key, bool ignoreCert)
        {
            var snap = new KumaSnapshot();
            try
            {
                // basic auth: any user name, the API key as the password. followRedirects: false so GetFollowAsync steers every hop -
                // the key only goes to this server
                HttpClient http = PluginHttp.Client("uptimekuma", ignoreCert, 15, false);
                var headers = new Dictionary<string, string> { { "Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + key)) } };
                using (HttpResponseMessage r = await PluginHttp.GetFollowAsync(http, url + "/metrics", headers))
                {
                    int code = (int)r.StatusCode;
                    if (code == 401 || code == 403) { snap.Error = "Uptime Kuma didn't accept the API key (" + code + ")."; return snap; }
                    if (code == 404) { snap.Error = "There's no /metrics at that address - is it Uptime Kuma (1.21 or later)?"; return snap; }
                    if (code >= 400) { snap.Error = "Uptime Kuma answered " + code + " " + r.ReasonPhrase + "."; return snap; }
                    string text = await r.Content.ReadAsStringAsync();
                    Parse(text, snap);
                    if (snap.Monitors.Count == 0 && !text.Contains("monitor_status")) snap.Error = "That address answered, but not with Uptime Kuma's metrics.";
                }
            }
            catch (TaskCanceledException) { snap.Error = "Uptime Kuma didn't answer in time."; }
            catch (HttpRequestException ex) { snap.Error = "Can't reach Uptime Kuma: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }
            catch (Exception ex) { snap.Error = ex.Message; }
            return snap;
        }

        static readonly Regex Line = new Regex(@"^(\w+)\{(.*)\}\s+(\S+)", RegexOptions.Compiled);
        static readonly Regex Label = new Regex(@"(\w+)=""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        /// <summary>Prometheus text: monitor_status{monitor_name="…",…} 1, monitor_response_time …, monitor_cert_days_remaining ….</summary>
        static void Parse(string text, KumaSnapshot snap)
        {
            var byName = new Dictionary<string, KumaMonitor>(StringComparer.Ordinal);
            foreach (string raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#' || !l.StartsWith("monitor_")) continue;
                Match m = Line.Match(l);
                if (!m.Success) continue;
                string metric = m.Groups[1].Value;
                if (metric != "monitor_status" && metric != "monitor_response_time" && metric != "monitor_cert_days_remaining") continue;
                var labels = new Dictionary<string, string>();
                foreach (Match lb in Label.Matches(m.Groups[2].Value)) labels[lb.Groups[1].Value] = lb.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                string name;
                if (!labels.TryGetValue("monitor_name", out name) || name.Length == 0) continue;
                double v;
                if (!double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = double.NaN;
                KumaMonitor mon;
                if (!byName.TryGetValue(name, out mon))
                {
                    mon = new KumaMonitor { Name = name };
                    string s;
                    if (labels.TryGetValue("monitor_type", out s)) mon.Type = s;
                    if (labels.TryGetValue("monitor_url", out s) && s != "null" && s != "https://") mon.Url = s;
                    else if (labels.TryGetValue("monitor_hostname", out s) && s != "null") mon.Url = s;
                    byName[name] = mon;
                    snap.Monitors.Add(mon);
                }
                if (metric == "monitor_status") mon.State = double.IsNaN(v) ? MonitorState.Unknown : (MonitorState)(int)v;
                else if (metric == "monitor_response_time") mon.ResponseMs = v >= 0 ? v : double.NaN;
                else if (metric == "monitor_cert_days_remaining") mon.CertDays = v;
            }
            snap.Monitors.Sort(delegate (KumaMonitor a, KumaMonitor b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
        }

        // ---- notifications ----
        static void Compare(KumaSnapshot snap)
        {
            Action<string, string, string, string> notice = Notice;
            if (notice == null) return;
            // the server itself
            if (snap.Error != null)
            {
                if (_notifyServer) DownDelay.Down("kuma:server", _delay, delegate { notice("Can't reach Uptime Kuma", snap.Error, DashboardUrl, "UptimeKuma.Down"); });
                return;     // keep _last: monitors aren't "back" just because the server is
            }
            if (DownDelay.Up("kuma:server") == UpResult.WasNotified && _notifyServer) notice("Uptime Kuma can be reached again", "", DashboardUrl, "UptimeKuma.Up");

            Dictionary<string, MonitorState> last = _last;
            var now = new Dictionary<string, MonitorState>(StringComparer.Ordinal);
            foreach (KumaMonitor m in snap.Monitors) now[m.Name] = m.State;
            _last = now;
            if (last == null || !_notifyDown) return;       // the first read only learns how things are (no notices at every start)
            foreach (KumaMonitor m in snap.Monitors)
            {
                MonitorState was;
                if (!last.TryGetValue(m.Name, out was)) continue;
                string key = "kuma:" + m.Name;
                KumaMonitor mon = m;
                if (m.State == MonitorState.Down && was != MonitorState.Down)
                    DownDelay.Down(key, _delay, delegate { notice(mon.Name + " is down", Detail(mon), DashboardUrl, "UptimeKuma.Down"); });
                else if (m.State == MonitorState.Up && was != MonitorState.Up)
                {
                    if (DownDelay.Up(key) == UpResult.WasNotified) notice(m.Name + " is back up", Detail(m), DashboardUrl, "UptimeKuma.Up");
                }
            }
        }

        static string Detail(KumaMonitor m)
        {
            string d = m.Url;
            if (!double.IsNaN(m.ResponseMs) && m.State == MonitorState.Up) d += (d.Length > 0 ? " · " : "") + Math.Round(m.ResponseMs) + " ms";
            return d;
        }
    }
}
