using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>The Proxmox connection and its notifications - kept in vShell's settings under Integrations["proxmox"].</summary>
    public sealed class PvSettings
    {
        readonly SettingsBag _b;
        public PvSettings(BarSettings s) { _b = s.Integration("proxmox"); }
        public static PvSettings Of(BarSettings s) { return new PvSettings(s); }

        /// <summary>One or more addresses (any node of the cluster), separated by commas, e.g. https://pve1:8006, https://pve2:8006</summary>
        public string Urls { get { return _b.Get("Urls", ""); } set { _b.Set("Urls", value ?? ""); } }
        /// <summary>API token ID, e.g. root@pam!vshell</summary>
        public string TokenId { get { return _b.Get("TokenId", ""); } set { _b.Set("TokenId", value ?? ""); } }
        /// <summary>The token's secret, stored encrypted for the current Windows user.</summary>
        public string Secret { get { return _b.Get("Secret", ""); } set { _b.Set("Secret", vShell.Core.Secret.Protect(value ?? "")); } }
        /// <summary>Proxmox uses a self-signed certificate unless you set one up.</summary>
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", true); } set { _b.Set("IgnoreCert", value); } }
        public int PollSeconds { get { return Math.Max(2, Math.Min(120, _b.Get("PollSeconds", 5))); } set { _b.Set("PollSeconds", value); } }
        public bool NotifyNodes { get { return _b.Get("NotifyNodes", false); } set { _b.Set("NotifyNodes", value); } }
        public bool NotifyGuests { get { return _b.Get("NotifyGuests", false); } set { _b.Set("NotifyGuests", value); } }
        /// <summary>Wait this many seconds before saying a node / VM / Proxmox itself is down; 0 = straight away.</summary>
        public int NotifyDelay { get { return Math.Max(0, Math.Min(1800, _b.Get("NotifyDelay", 0))); } set { _b.Set("NotifyDelay", value); } }
        /// <summary>Storage fuller than this % (0 = off).</summary>
        public int NotifyStorage { get { return Math.Max(0, Math.Min(100, _b.Get("NotifyStorage", 0))); } set { _b.Set("NotifyStorage", value); } }
        /// <summary>The run box keyword (without the !): !px words shows the palette's Proxmox commands for those words.</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "px").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "px" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
    }

    /// <summary>A Proxmox module's settings - kept in its ModuleConfig.Extra.</summary>
    public sealed class PvView
    {
        public static readonly string[] Shows = { "Nodes", "Guests", "Storage" };
        public static readonly string[] Styles = { "Text", "Meter" };

        readonly SettingsBag _b;
        public PvView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static PvView Of(ModuleConfig cfg) { return new PvView(cfg); }

        /// <summary>What the module shows: "Nodes" (CPU / memory), "Guests" (VMs and containers) or "Storage".</summary>
        public string Show { get { string v = _b.Get("Show", "Nodes"); return Array.IndexOf(Shows, v) < 0 ? "Nodes" : v; } set { _b.Set("Show", value); } }
        /// <summary>Only this node ("" = the whole cluster).</summary>
        public string Node { get { return _b.Get("Node", ""); } set { _b.Set("Node", value ?? ""); } }
        /// <summary>"Text" (percentages) or "Meter" (small bars) for nodes and storage.</summary>
        public string Style { get { string v = _b.Get("Style", "Meter"); return Array.IndexOf(Styles, v) < 0 ? "Meter" : v; } set { _b.Set("Style", value); } }
        public bool RunningOnly { get { return _b.Get("RunningOnly", false); } set { _b.Set("RunningOnly", value); } }
        public bool ShowTemplates { get { return _b.Get("ShowTemplates", false); } set { _b.Set("ShowTemplates", value); } }
        /// <summary>Guests: show each one's CPU and memory (widgets / popup).</summary>
        public bool GuestStats { get { return _b.Get("GuestStats", true); } set { _b.Set("GuestStats", value); } }
        /// <summary>Guests: show how long each running one has been up.</summary>
        public bool GuestUptime { get { return _b.Get("GuestUptime", false); } set { _b.Set("GuestUptime", value); } }
        /// <summary>Allow start / shut down / reboot / stop from vShell (always asks first, except Start).</summary>
        public bool AllowPower { get { return _b.Get("AllowPower", true); } set { _b.Set("AllowPower", value); } }
        /// <summary>Allow deleting a (stopped) VM / container from vShell - off by default; asks for its ID first.</summary>
        public bool AllowDelete { get { return _b.Get("AllowDelete", false); } set { _b.Set("AllowDelete", value); } }
    }
    /// <summary>One thing in the Proxmox cluster: a node, a VM (qemu), a container (lxc) or a storage pool on a node.</summary>
    public sealed class PveResource
    {
        public string Type = "";        // node, qemu, lxc, storage
        public string Id = "";          // node/pve1, qemu/100, lxc/101, storage/pve1/local
        public string Node = "";
        public int VmId;
        public string Name = "";
        public string Status = "";      // node: online / offline;  guest: running / stopped / paused;  storage: available
        public double Cpu;              // 0..1 of MaxCpu
        public int MaxCpu;
        public double Mem, MaxMem, Disk, MaxDisk;
        public long Uptime;
        public bool Template;
        public bool Shared;
        public string Lock = "";
        /// <summary>Nodes: the address Proxmox has for it (cluster status), "" when not known.</summary>
        public string Ip = "";

        public bool IsGuest { get { return Type == "qemu" || Type == "lxc"; } }
        public bool Running { get { return Status == "running" || Status == "online"; } }
        public double MemFraction { get { return MaxMem > 0 ? Mem / MaxMem : 0; } }
        public double DiskFraction { get { return MaxDisk > 0 ? Disk / MaxDisk : 0; } }
        /// <summary>"VM" or "CT".</summary>
        public string Kind { get { return Type == "lxc" ? "CT" : "VM"; } }
        public string Label { get { return IsGuest ? VmId + " " + (Name.Length > 0 ? Name : Kind) : Name; } }
    }

    /// <summary>The whole cluster as last read.</summary>
    public sealed class PveSnapshot
    {
        public readonly List<PveResource> Items = new List<PveResource>();
        public string ClusterName = "";
        public bool Quorate = true;
        public string Version = "";
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";

        public List<PveResource> Nodes(string only)
        {
            var l = Items.FindAll(delegate (PveResource r) { return r.Type == "node" && Match(r, only); });
            l.Sort(delegate (PveResource a, PveResource b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return l;
        }

        public List<PveResource> Guests(string only, bool runningOnly, bool templates)
        {
            var l = Items.FindAll(delegate (PveResource r)
            {
                return r.IsGuest && Match(r, only) && (templates || !r.Template) && (!runningOnly || r.Status == "running");
            });
            l.Sort(delegate (PveResource a, PveResource b) { return a.VmId.CompareTo(b.VmId); });
            return l;
        }

        /// <summary>Storage pools; a shared one (NFS, Ceph...) is listed by every node but shown once.</summary>
        public List<PveResource> Storage(string only)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var l = new List<PveResource>();
            foreach (PveResource r in Items)
            {
                if (r.Type != "storage" || !Match(r, only) || r.MaxDisk <= 0) continue;
                if (r.Shared && !seen.Add(r.Name)) continue;
                l.Add(r);
            }
            l.Sort(delegate (PveResource a, PveResource b)
            {
                int c = string.Compare(a.Shared ? "" : a.Node, b.Shared ? "" : b.Node, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return l;
        }

        public List<string> NodeNames()
        {
            var l = new List<string>();
            foreach (PveResource r in Nodes("")) l.Add(r.Name);
            return l;
        }

        static bool Match(PveResource r, string only)
        {
            return string.IsNullOrEmpty(only) || string.Equals(r.Node, only, StringComparison.OrdinalIgnoreCase) || (r.Type == "storage" && r.Shared);
        }
    }

    /// <summary>
    /// Talks to the Proxmox VE API with an API token. Any node of a cluster answers for the whole cluster (/cluster/resources), so one
    /// address is enough; more can be given and the next is tried when one does not answer. Modules call Get() as they redraw; the
    /// cluster is then polled in the background for as long as somebody keeps asking. Nothing here blocks the UI thread.
    /// </summary>
    public static class ProxmoxClient
    {
        static readonly object Gate = new object();
        static string[] _urls = new string[0];
        static int _active;
        static string _tokenId = "", _secret = "";
        static bool _ignoreCert;
        static int _seconds = 5;
        static HttpClient _http;
        static Timer _timer;
        static int _busy;
        static DateTime _wanted = DateTime.MinValue;
        static PveSnapshot _snap;
        static int _generation;

        public static bool Configured { get { return _urls.Length > 0 && _tokenId.Length > 0 && _secret.Length > 0; } }

        /// <summary>The address answering right now (for "open the web interface").</summary>
        public static string WebUrl { get { lock (Gate) return _urls.Length == 0 ? "" : _urls[_active % _urls.Length]; } }

        /// <summary>"pve.local" -> https://pve.local:8006 ; a full address is kept as it is.</summary>
        public static string CleanUrl(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) return "";
            bool scheme = url.IndexOf("://", StringComparison.Ordinal) >= 0;
            if (!scheme)
            {
                string host = url;
                int slash = host.IndexOf('/');
                if (slash >= 0) host = host.Substring(0, slash);
                bool port = host.StartsWith("[") ? host.Contains("]:") : host.IndexOf(':') >= 0;
                url = "https://" + url + (port || slash >= 0 ? "" : ":8006");
            }
            if (url.EndsWith("/api2/json", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 10);
            return url.TrimEnd('/');
        }

        public static string[] SplitUrls(string urls)
        {
            var l = new List<string>();
            foreach (string part in (urls ?? "").Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string u = CleanUrl(part);
                if (u.Length > 0 && !l.Contains(u)) l.Add(u);
            }
            return l.ToArray();
        }

        /// <summary>Token ID "user@realm!name"; someone pasting "user@realm!name=secret" into it gets the secret split off.</summary>
        public static void SplitToken(string tokenId, string secret, out string id, out string sec)
        {
            id = (tokenId ?? "").Trim();
            sec = (secret ?? "").Trim();
            int eq = id.IndexOf('=');
            if (eq > 0)
            {
                if (sec.Length == 0) sec = id.Substring(eq + 1).Trim();
                id = id.Substring(0, eq).Trim();
            }
            if (id.StartsWith("PVEAPIToken=", StringComparison.OrdinalIgnoreCase)) id = id.Substring(12);
        }

        static HttpClient MakeHttp(string id, string secret, bool ignoreCert, int timeoutSeconds)
        {
            var handler = new HttpClientHandler();
            if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "PVEAPIToken=" + id + "=" + secret);
            return http;
        }

        /// <summary>Apply the connection settings (only a change does anything).</summary>
        public static void Configure(string urls, string tokenId, string storedSecret, bool ignoreCert, int seconds)
        {
            string[] list = SplitUrls(urls);
            string id, secret;
            SplitToken(tokenId, Secret.Unprotect(storedSecret), out id, out secret);
            seconds = Math.Max(2, Math.Min(300, seconds));
            lock (Gate)
            {
                bool same = string.Join(",", list) == string.Join(",", _urls) && id == _tokenId && secret == _secret && ignoreCert == _ignoreCert && seconds == _seconds;
                if (same && (_http != null || !Configured)) return;
                _urls = list; _tokenId = id; _secret = secret; _ignoreCert = ignoreCert; _seconds = seconds;
                _active = 0;
                _snap = null;
                _generation++;
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                if (_http != null) { _http.Dispose(); _http = null; }
                if (!Configured) return;
                _http = MakeHttp(id, secret, ignoreCert, 8);
                _timer = new Timer(delegate { Tick(false); }, null, TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(_seconds));
            }
        }

        /// <summary>The latest view of the cluster, or null before the first answer. Calling this keeps the polling going.</summary>
        public static PveSnapshot Get()
        {
            bool start;
            PveSnapshot s;
            lock (Gate)
            {
                if (!Configured) return null;
                start = _snap == null && (DateTime.UtcNow - _wanted).TotalSeconds > 8;
                _wanted = DateTime.UtcNow;
                s = _snap;
            }
            if (start) Task.Run(delegate { Tick(true); });
            return s;
        }

        /// <summary>The last view without starting anything (settings uses it for the node list).</summary>
        public static PveSnapshot Latest { get { lock (Gate) return _snap; } }

        /// <summary>Read again soon (after a start / stop, so the new state shows quickly).</summary>
        public static void RefreshSoon(int delayMs)
        {
            Task.Delay(delayMs).ContinueWith(delegate { Tick(true); });
        }

        static void Tick(bool force)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                HttpClient http; string[] urls; int start, gen;
                lock (Gate)
                {
                    if (!force && (DateTime.UtcNow - _wanted).TotalSeconds > 30) return;     // nobody is showing it
                    http = _http; urls = _urls; start = _active; gen = _generation;
                }
                if (http == null || urls.Length == 0) return;

                string lastError = "";
                for (int i = 0; i < urls.Length; i++)
                {
                    int idx = (start + i) % urls.Length;
                    try
                    {
                        PveSnapshot s = FetchAsync(http, urls[idx]).GetAwaiter().GetResult();
                        lock (Gate)
                        {
                            if (gen != _generation) return;
                            _snap = s;
                            _active = idx;
                        }
                        return;
                    }
                    catch (Exception ex) { lastError = Describe(ex); }
                }
                lock (Gate)
                {
                    if (gen != _generation) return;
                    var keep = new PveSnapshot();
                    if (_snap != null)
                    {
                        keep.Items.AddRange(_snap.Items);
                        keep.ClusterName = _snap.ClusterName; keep.Quorate = _snap.Quorate; keep.Version = _snap.Version;
                        keep.Fetched = _snap.Fetched;
                    }
                    keep.Error = lastError;
                    _snap = keep;
                }
            }
            catch (Exception ex) { Log.Write("Proxmox poll: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static string Describe(Exception ex)
        {
            if (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            if (ex is TaskCanceledException) return "no answer (timed out)";
            if (ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        static async Task<string> GetAsync(HttpClient http, string url)
        {
            using (HttpResponseMessage r = await http.GetAsync(url))
            {
                if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new InvalidOperationException("the API token was rejected (401)");
                if (r.StatusCode == System.Net.HttpStatusCode.Forbidden) throw new InvalidOperationException("the API token is not allowed to read this (403)");
                string body = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)r.StatusCode + " " + r.ReasonPhrase);
                return body;
            }
        }

        static async Task<PveSnapshot> FetchAsync(HttpClient http, string baseUrl)
        {
            string res = await GetAsync(http, baseUrl + "/api2/json/cluster/resources");
            string status = null;
            try { status = await GetAsync(http, baseUrl + "/api2/json/cluster/status"); } catch { }
            PveSnapshot s = Parse(res, status);
            s.Fetched = DateTime.UtcNow;
            return s;
        }

        // ---- parsing (public so it can be tested) ------------------------------------------------------
        public static PveSnapshot Parse(string resourcesJson, string clusterStatusJson)
        {
            var s = new PveSnapshot();
            using (JsonDocument d = JsonDocument.Parse(resourcesJson))
            {
                JsonElement data;
                if (d.RootElement.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in data.EnumerateArray())
                    {
                        var r = new PveResource();
                        r.Type = Str(e, "type");
                        if (r.Type != "node" && r.Type != "qemu" && r.Type != "lxc" && r.Type != "storage") continue;
                        r.Id = Str(e, "id");
                        r.Node = Str(e, "node");
                        r.VmId = (int)Num(e, "vmid");
                        r.Status = Str(e, "status");
                        r.Cpu = Num(e, "cpu");
                        r.MaxCpu = (int)Num(e, "maxcpu");
                        r.Mem = Num(e, "mem");
                        r.MaxMem = Num(e, "maxmem");
                        r.Disk = Num(e, "disk");
                        r.MaxDisk = Num(e, "maxdisk");
                        r.Uptime = (long)Num(e, "uptime");
                        r.Template = Num(e, "template") > 0;
                        r.Shared = Num(e, "shared") > 0;
                        r.Lock = Str(e, "lock");
                        if (r.Type == "node") r.Name = r.Node;
                        else if (r.Type == "storage") r.Name = Str(e, "storage");
                        else r.Name = Str(e, "name");
                        if (double.IsNaN(r.Cpu) || r.Cpu < 0) r.Cpu = 0;
                        s.Items.Add(r);
                    }
            }
            if (!string.IsNullOrEmpty(clusterStatusJson))
            {
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(clusterStatusJson))
                    {
                        JsonElement data;
                        if (d.RootElement.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in data.EnumerateArray())
                            {
                                if (Str(e, "type") == "cluster")
                                {
                                    s.ClusterName = Str(e, "name");
                                    JsonElement q;
                                    if (e.TryGetProperty("quorate", out q)) s.Quorate = Num(e, "quorate") > 0;
                                }
                                else if (Str(e, "type") == "node")
                                {
                                    // the node's address (for "Open shell")
                                    string name = Str(e, "name"), ip = Str(e, "ip");
                                    PveResource n = s.Items.Find(delegate (PveResource x) { return x.Type == "node" && x.Name == name; });
                                    if (n != null) n.Ip = ip;
                                }
                            }
                    }
                }
                catch { }
            }
            return s;
        }

        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            if (!e.TryGetProperty(name, out v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return "";
        }

        static double Num(JsonElement e, string name)
        {
            JsonElement v;
            if (!e.TryGetProperty(name, out v)) return 0;
            double d;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out d)) return d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            if (v.ValueKind == JsonValueKind.True) return 1;
            return 0;
        }

        /// <summary>Proxmox's own reason from an error answer ({"message": "..."}), else fallback (the HTTP reason).</summary>
        static string MessageOf(string body, string fallback)
        {
            try
            {
                using (JsonDocument d = JsonDocument.Parse(body))
                {
                    JsonElement m;
                    if (d.RootElement.TryGetProperty("message", out m) && m.ValueKind == JsonValueKind.String) return m.GetString();
                }
            }
            catch { }
            return fallback;
        }

        // ---- start / stop ------------------------------------------------------------------------------
        /// <summary>Guests started / stopped from vShell lately (id -> when): the notifications leave them out.</summary>
        public static readonly Dictionary<string, DateTime> ByVShell = new Dictionary<string, DateTime>();

        /// <summary>start, shutdown (clean), reboot, stop (pull the plug), suspend, resume. Returns "" or a one-line error.</summary>
        public static async Task<string> PowerAsync(PveResource g, string action)
        {
            if (g == null || !g.IsGuest) return "Not a VM or container.";
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _urls.Length == 0 ? "" : _urls[_active % _urls.Length]; }
            if (http == null) return "Proxmox is not set up (Settings > Plugins).";
            lock (ByVShell) ByVShell[g.Id] = DateTime.UtcNow;     // no "stopped" notification for what vShell stopped
            try
            {
                string path = url + "/api2/json/nodes/" + Uri.EscapeDataString(g.Node) + "/" + g.Type + "/" + g.VmId + "/status/" + action;
                using (var content = new FormUrlEncodedContent(new Dictionary<string, string>()))
                using (HttpResponseMessage r = await http.PostAsync(path, content))
                {
                    if (r.StatusCode == System.Net.HttpStatusCode.Forbidden || r.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        return "The API token is not allowed to do that. It needs the VM.PowerMgmt permission (for example the PVEVMUser role) on /vms.";
                    if (!r.IsSuccessStatusCode)
                    {
                        string body = await r.Content.ReadAsStringAsync();
                        string why = r.ReasonPhrase ?? "";
                        why = MessageOf(body, why);
                        return "Proxmox answered HTTP " + (int)r.StatusCode + ": " + why.Trim();
                    }
                }
                RefreshSoon(1500);
                RefreshSoon(6000);
                return "";
            }
            catch (Exception ex) { return "Could not reach Proxmox: " + Describe(ex); }
        }

        // ---- delete ------------------------------------------------------------------------------------
        /// <summary>
        /// Delete a (stopped) VM / container. purge = also remove it from backup jobs, replication and HA; unreferenced = also delete
        /// disks that belong to it but aren't attached. Returns "" or a one-line error. Proxmox does it as a task; the list updates after.
        /// </summary>
        public static async Task<string> DeleteAsync(PveResource g, bool purge, bool unreferenced)
        {
            if (g == null || !g.IsGuest) return "Not a VM or container.";
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _urls.Length == 0 ? "" : _urls[_active % _urls.Length]; }
            if (http == null) return "Proxmox is not set up (Settings > Plugins).";
            lock (ByVShell) ByVShell[g.Id] = DateTime.UtcNow;     // no "stopped / gone" notification for what vShell deleted
            try
            {
                var q = new List<string>();
                if (purge) q.Add("purge=1");
                if (unreferenced) q.Add("destroy-unreferenced-disks=1");
                string path = url + "/api2/json/nodes/" + Uri.EscapeDataString(g.Node) + "/" + g.Type + "/" + g.VmId + (q.Count > 0 ? "?" + string.Join("&", q) : "");
                using (HttpResponseMessage r = await http.DeleteAsync(path))
                {
                    if (r.StatusCode == System.Net.HttpStatusCode.Forbidden || r.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        return "The API token is not allowed to delete guests. It needs the VM.Allocate permission (for example the PVEVMAdmin role) on /vms.";
                    if (!r.IsSuccessStatusCode)
                    {
                        string body = await r.Content.ReadAsStringAsync();
                        string why = r.ReasonPhrase ?? "";
                        why = MessageOf(body, why);
                        return "Proxmox answered HTTP " + (int)r.StatusCode + ": " + why.Trim();
                    }
                }
                RefreshSoon(2000);
                RefreshSoon(8000);
                return "";
            }
            catch (Exception ex) { return "Could not reach Proxmox: " + Describe(ex); }
        }

        // ---- a guest's address -------------------------------------------------------------------------
        /// <summary>
        /// A running guest's IPv4 address, for SSH: a VM's from the QEMU guest agent, a container's from Proxmox. Key = the address ("" =
        /// none), Value = why there's none (a readable line).
        /// </summary>
        public static async Task<KeyValuePair<string, string>> GuestAddressAsync(PveResource g)
        {
            if (g == null || !g.IsGuest) return new KeyValuePair<string, string>("", "Not a VM or container.");
            HttpClient http; string url;
            lock (Gate) { http = _http; url = _urls.Length == 0 ? "" : _urls[_active % _urls.Length]; }
            if (http == null) return new KeyValuePair<string, string>("", "Proxmox is not set up (Settings > Plugins).");
            bool vm = g.Type == "qemu";
            string path = url + "/api2/json/nodes/" + Uri.EscapeDataString(g.Node) + "/" + g.Type + "/" + g.VmId +
                          (vm ? "/agent/network-get-interfaces" : "/interfaces");
            try
            {
                using (HttpResponseMessage r = await http.GetAsync(path))
                {
                    string body = await r.Content.ReadAsStringAsync();
                    if (r.StatusCode == System.Net.HttpStatusCode.Forbidden || r.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        return new KeyValuePair<string, string>("", vm
                            ? "The API token is not allowed to ask the guest agent. It needs VM.GuestAgent.Audit (Proxmox 9) or VM.Monitor (Proxmox 8) on /vms - for example the PVEVMAdmin role."
                            : "The API token is not allowed to read the container's network. It needs VM.Audit on /vms.");
                    if (!r.IsSuccessStatusCode)
                    {
                        string why = r.ReasonPhrase ?? "";
                        why = MessageOf(body, why);
                        why = why.Trim();
                        if (vm && why.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0)
                            return new KeyValuePair<string, string>("", "The QEMU guest agent isn't running in " + g.Label + ", so Proxmox doesn't know its address.\n\n" +
                                "Install it in the VM (e.g. apt install qemu-guest-agent, then systemctl start qemu-guest-agent) and tick Options > QEMU Guest Agent in Proxmox.");
                        return new KeyValuePair<string, string>("", "Proxmox answered HTTP " + (int)r.StatusCode + ": " + why);
                    }
                    var found = new List<string>();
                    using (JsonDocument d = JsonDocument.Parse(body))
                    {
                        JsonElement data;
                        if (!d.RootElement.TryGetProperty("data", out data)) data = default(JsonElement);
                        JsonElement list = data;
                        if (vm && data.ValueKind == JsonValueKind.Object) data.TryGetProperty("result", out list);
                        if (list.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement nic in list.EnumerateArray())
                            {
                                if (nic.ValueKind != JsonValueKind.Object) continue;
                                string name = Str(nic, "name");
                                if (name == "lo" || name.StartsWith("docker") || name.StartsWith("br-") || name.StartsWith("veth") || name.StartsWith("virbr")) continue;
                                if (vm)
                                {
                                    JsonElement ips;
                                    if (!nic.TryGetProperty("ip-addresses", out ips) || ips.ValueKind != JsonValueKind.Array) continue;
                                    foreach (JsonElement ip in ips.EnumerateArray())
                                        if (ip.ValueKind == JsonValueKind.Object && Str(ip, "ip-address-type") == "ipv4") found.Add(Str(ip, "ip-address"));
                                }
                                else
                                {
                                    string inet = Str(nic, "inet");
                                    int slash = inet.IndexOf('/');
                                    found.Add(slash > 0 ? inet.Substring(0, slash) : inet);
                                }
                            }
                    }
                    foreach (string ip in found)
                        if (ip.Length > 0 && !ip.StartsWith("127.") && !ip.StartsWith("169.254.")) return new KeyValuePair<string, string>(ip, "");
                    return new KeyValuePair<string, string>("", g.Label + " has no IPv4 address that Proxmox can see" + (vm ? " (from its guest agent)." : "."));
                }
            }
            catch (Exception ex) { return new KeyValuePair<string, string>("", "Could not reach Proxmox: " + Describe(ex)); }
        }

        // ---- test --------------------------------------------------------------------------------------
        /// <summary>Check the connection (and whether the token may start / stop guests). Returns whether it worked and a message.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(string urls, string tokenId, string storedOrPlainSecret, bool ignoreCert)
        {
            string[] list = SplitUrls(urls);
            string id, secret;
            SplitToken(tokenId, Secret.Unprotect(storedOrPlainSecret), out id, out secret);
            if (list.Length == 0) return new KeyValuePair<bool, string>(false, "Enter the Proxmox address first.");
            if (id.Length == 0 || id.IndexOf('!') < 0) return new KeyValuePair<bool, string>(false, "Enter the API token ID, like root@pam!vshell.");
            if (secret.Length == 0) return new KeyValuePair<bool, string>(false, "Enter the token's secret first.");
            var results = new List<string>();
            bool any = false;
            using (HttpClient http = MakeHttp(id, secret, ignoreCert, 8))
            {
                foreach (string url in list)
                {
                    try
                    {
                        string ver = "";
                        using (JsonDocument d = JsonDocument.Parse(await GetAsync(http, url + "/api2/json/version")))
                        {
                            JsonElement data;
                            if (d.RootElement.TryGetProperty("data", out data)) ver = Str(data, "version");
                        }
                        PveSnapshot s = Parse(await GetAsync(http, url + "/api2/json/cluster/resources"), await SafeGet(http, url + "/api2/json/cluster/status"));
                        int nodes = s.Nodes("").Count, guests = s.Guests("", false, false).Count;
                        string msg = "Proxmox VE " + ver + (s.ClusterName.Length > 0 ? ", cluster \"" + s.ClusterName + "\"" : "") +
                                     ", " + nodes + (nodes == 1 ? " node" : " nodes") + ", " + guests + (guests == 1 ? " guest" : " guests");
                        if (nodes == 0) msg += " - the token can't see the nodes; give it the PVEAuditor role on /";
                        string power = await PowerPermission(http, url);
                        if (power.Length > 0) msg += ". " + power;
                        results.Add((list.Length > 1 ? url + ": " : "") + "Connected: " + msg);
                        any = true;
                        if (list.Length == 1) break;
                    }
                    catch (Exception ex) { results.Add((list.Length > 1 ? url + ": " : "") + "Could not connect: " + Describe(ex)); }
                }
            }
            return new KeyValuePair<bool, string>(any, string.Join("\n", results));
        }

        static async Task<string> SafeGet(HttpClient http, string url)
        {
            try { return await GetAsync(http, url); } catch { return null; }
        }

        /// <summary>What the token may do with guests (start / stop, delete) and what to add; "" when it can't be told.</summary>
        static async Task<string> PowerPermission(HttpClient http, string url)
        {
            try
            {
                using (JsonDocument d = JsonDocument.Parse(await GetAsync(http, url + "/api2/json/access/permissions")))
                {
                    JsonElement data;
                    if (!d.RootElement.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Object) return "";
                    bool power = false, allocate = false;
                    foreach (JsonProperty path in data.EnumerateObject())
                    {
                        if (path.Name != "/" && path.Name != "/vms" && !path.Name.StartsWith("/vms/") && !path.Name.StartsWith("/pool/")) continue;
                        JsonElement p;
                        if (path.Value.ValueKind != JsonValueKind.Object) continue;
                        if (path.Value.TryGetProperty("VM.PowerMgmt", out p)) power = true;
                        if (path.Value.TryGetProperty("VM.Allocate", out p)) allocate = true;
                    }
                    if (power && allocate) return "Start / stop and deleting guests allowed.";
                    if (power) return "Start / stop allowed. To also delete guests give it the PVEVMAdmin role (VM.Allocate) on /vms.";
                    if (allocate) return "Deleting guests allowed, but not start / stop: give it the PVEVMUser role (or VM.PowerMgmt) on /vms.";
                    return "This token can only look: to start / stop guests give it the PVEVMUser role (or VM.PowerMgmt) on /vms; to also delete them, PVEVMAdmin.";
                }
            }
            catch { return ""; }
        }

        // ---- formatting ----------------------------------------------------------------------------------
        /// <summary>"12.3 GiB", "512 MiB".</summary>
        public static string Bytes(double b)
        {
            if (b <= 0) return "0";
            string[] u = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return b.ToString(b < 10 && i > 0 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + u[i];
        }

        /// <summary>"12d 4h", "3h 20m", "45m".</summary>
        public static string Uptime(long seconds)
        {
            if (seconds <= 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return t.Hours + "h " + t.Minutes + "m";
            return Math.Max(1, t.Minutes) + "m";
        }

        public static string Percent(double fraction)
        {
            return Math.Round(Math.Max(0, fraction) * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
        }
    }
}
