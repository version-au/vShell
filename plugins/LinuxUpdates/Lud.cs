using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using vShell.Core;
using JsonValue = vShell.Core.JsonValue;

namespace vShell.Lud
{
    /// <summary>The Linux Update Dashboard connection and its notifications (Settings > Plugins), kept under Integrations["ludash"].</summary>
    sealed class LudSettings
    {
        readonly SettingsBag _b;
        public LudSettings(BarSettings s) { _b = s.Integration("ludash"); }
        public static LudSettings Of(BarSettings s) { return new LudSettings(s); }

        /// <summary>The dashboard's address, such as http://192.168.2.20:3001.</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>The API token (ludash_…), stored encrypted (TokenPlain is the real one).</summary>
        public string Token { get { return _b.Get("Token", ""); } set { _b.Set("Token", Secret.Protect((value ?? "").Trim())); } }
        public string TokenPlain { get { return _b.GetSecret("Token"); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        public string Bang
        {
            get { string v = _b.Get("Bang", "lu").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "lu" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        /// <summary>Upgrades, full upgrades, removing unused packages and reboots from vShell (Check now is always there).</summary>
        public bool AllowActions { get { return _b.Get("AllowActions", false); } set { _b.Set("AllowActions", value); } }
        /// <summary>Open the live output window when vShell starts an upgrade.</summary>
        public bool ShowOutput { get { return _b.Get("ShowOutput", true); } set { _b.Set("ShowOutput", value); } }

        public bool NotifyUpdates { get { return _b.Get("NotifyUpdates", true); } set { _b.Set("NotifyUpdates", value); } }
        public bool NotifySecurity { get { return _b.Get("NotifySecurity", true); } set { _b.Set("NotifySecurity", value); } }
        public bool NotifyReboot { get { return _b.Get("NotifyReboot", true); } set { _b.Set("NotifyReboot", value); } }
        public bool NotifyLifecycle { get { return _b.Get("NotifyLifecycle", true); } set { _b.Set("NotifyLifecycle", value); } }
        public bool NotifyProblems { get { return _b.Get("NotifyProblems", true); } set { _b.Set("NotifyProblems", value); } }
        public int NotifyDelay { get { return Math.Max(0, Math.Min(1800, _b.Get("NotifyDelay", 0))); } set { _b.Set("NotifyDelay", value); } }
        public bool NotifyDone { get { return _b.Get("NotifyDone", true); } set { _b.Set("NotifyDone", value); } }
        public bool NotifyApp { get { return _b.Get("NotifyApp", true); } set { _b.Set("NotifyApp", value); } }

        /// <summary>What has been said and still applies (one key a line), so a restart doesn't say it again.</summary>
        public string Said { get { return _b.Get("Said", ""); } set { _b.Set("Said", value ?? ""); } }
        /// <summary>The first look has been taken (what was already there then isn't announced).</summary>
        public bool Seeded { get { return _b.Get("Seeded", false); } set { _b.Set("Seeded", value); } }
    }

    /// <summary>A module's settings (cfg.Bag).</summary>
    sealed class LudView
    {
        readonly SettingsBag _b;
        public LudView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static readonly string[] Shows = { "All systems", "Only ones that need something" };
        public bool Icon { get { return _b.Get("Icon", true); } set { _b.Set("Icon", value); } }
        public bool HideFine { get { return _b.Get("HideFine", false); } set { _b.Set("HideFine", value); } }
        public string Show { get { string v = _b.Get("Show", Shows[0]); return Array.IndexOf(Shows, v) >= 0 ? v : Shows[0]; } set { _b.Set("Show", value ?? Shows[0]); } }
        public bool OnlyNeeding { get { return Show == Shows[1]; } }
    }

    /// <summary>One system the dashboard looks after (GET /api/systems).</summary>
    sealed class LudSystem
    {
        public long Id;
        public string Name = "", Host = "", User = "", Os = "";
        public int Port = 22, Order;
        public int Updates, Security, KeptBack, Issues;
        public bool NeedsReboot, ExcludeFromAll, FullUpgrade;
        /// <summary>-1 = not known yet, 0 = can't be reached, 1 = reachable.</summary>
        public int Reachable = -1;
        /// <summary>The last check: "success", "warning", "failed" or "" (never checked).</summary>
        public string CheckStatus = "", CheckError = "", CacheAge = "";
        public DateTime Checked = DateTime.MinValue;
        /// <summary>What's running on it ("" = nothing): check, upgrade_all, full_upgrade_all, upgrade_package, autoremove, reboot, package_manager_repair.</summary>
        public string Op = "", OpPhase = "", OpPackage = "";
        /// <summary>A distro support warning: eol, approaching_eol, support_ending, support_ended - or "" (supported, unknown, or dismissed on the dashboard).</summary>
        public string Lifecycle = "";

        public string Label { get { return Name.Length > 0 ? Name : Host.Length > 0 ? Host : "system " + Id; } }
        public bool Busy { get { return Op.Length > 0; } }

        /// <summary>What's wrong ("" = nothing): it can't be reached, or its last check failed.</summary>
        public string Problem
        {
            get
            {
                if (Reachable == 0) return "can't be reached" + (CheckError.Length > 0 ? ": " + CheckError : "");
                if (CheckStatus == "failed") return "last check failed" + (CheckError.Length > 0 ? ": " + CheckError : "");
                return "";
            }
        }

        /// <summary>Something to do: updates, a reboot or a problem.</summary>
        public bool NeedsSomething { get { return Updates > 0 || NeedsReboot || Problem.Length > 0 || Lifecycle.Length > 0; } }

        public string OpText
        {
            get
            {
                if (OpPhase == "queued") return "waiting to upgrade";
                switch (Op)
                {
                    case "": return "";
                    case "check": return "checking…";
                    case "upgrade_all": return "upgrading…";
                    case "full_upgrade_all": return "full upgrade…";
                    case "upgrade_package": return "upgrading " + (OpPackage.Length > 0 ? OpPackage : "packages") + "…";
                    case "autoremove": return "removing unused packages…";
                    case "reboot": return "rebooting…";
                    case "package_manager_repair": return "repairing…";
                    default: return Op.Replace('_', ' ') + "…";
                }
            }
        }

        public string LifecycleText
        {
            get
            {
                switch (Lifecycle)
                {
                    case "": return "";
                    case "eol": case "support_ended": return Os + " is no longer supported";
                    case "approaching_eol": case "support_ending": return Os + " support ends soon";
                    default: return Os + ": " + Lifecycle.Replace('_', ' ');
                }
            }
        }

        public string UpdatesText
        {
            get
            {
                if (Updates == 0) return Reachable == -1 && CheckStatus.Length == 0 ? "not checked yet" : "up to date";
                return LudClient.Count(Updates, "update") + (Security > 0 ? " (" + Security + " security)" : "");
            }
        }
    }

    sealed class LudSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public List<LudSystem> Systems = new List<LudSystem>();
        public string AppCurrent = "", AppLatest = "", AppUrl = "";
        public bool AppUpdate;
        public bool Ok { get { return Fetched != DateTime.MinValue; } }

        public int Updates { get { int n = 0; foreach (LudSystem x in Systems) n += x.Updates; return n; } }
        public int Security { get { int n = 0; foreach (LudSystem x in Systems) n += x.Security; return n; } }
        public int Reboots { get { int n = 0; foreach (LudSystem x in Systems) if (x.NeedsReboot) n++; return n; } }
        public int Problems { get { int n = 0; foreach (LudSystem x in Systems) if (x.Problem.Length > 0) n++; return n; } }
        public bool Busy { get { foreach (LudSystem x in Systems) if (x.Busy) return true; return false; } }

        public LudSystem Find(long id)
        {
            foreach (LudSystem x in Systems) if (x.Id == id) return x;
            return null;
        }
    }

    /// <summary>A check / upgrade vShell started: its job is asked about every 2 seconds until it's done.</summary>
    sealed class LudJob
    {
        public string Id = "", What = "", Name = "";
        public long SystemId;
        public DateTime Started = DateTime.UtcNow;
    }

    /// <summary>
    /// Talks to Linux Update Dashboard's API (its address + /api) with an API token (Authorization: Bearer ludash_…). Polled every 20 s
    /// while a module, the run box or the output window shows it (every 5 s while something's running), every 2 minutes otherwise.
    /// Snapshots are swapped in on the UI thread, where the notifications are worked out.
    /// </summary>
    static class LudClient
    {
        static readonly object Gate = new object();
        static LudSnapshot _snap = new LudSnapshot();
        static Poller _poll;
        static BarSettings _s;
        static string _configuredFor;
        static DateTime _appAt = DateTime.MinValue;
        static string _appCurrent = "", _appLatest = "", _appUrl = "";
        static bool _appUpdate;
        static readonly List<LudJob> Jobs = new List<LudJob>();
        /// <summary>The distro support states that are a warning (the others are "supported" and "unknown").</summary>
        static readonly string[] LifecycleWarnings = { "support_ending", "support_ended", "approaching_eol", "eol" };
        static bool _jobsRunning;
        /// <summary>Poll quickly until then: after Check all / Upgrade all / a check, which the dashboard may not show as running at first.</summary>
        static DateTime _quickUntil = DateTime.MinValue;

        /// <summary>Whether the token is read-only: "yes", "no" or "" (not known yet). Asked once per connection, and learned from a 403.</summary>
        public static volatile string ReadOnlyState = "";
        public static bool ReadOnly { get { return ReadOnlyState == "yes"; } }

        /// <summary>A new snapshot (on the UI thread).</summary>
        public static event Action<LudSnapshot> Changed;
        /// <summary>A job vShell started has finished (on the UI thread): the job, the result ("success", "warning", "failed",
        /// "cancelled"), and what it said.</summary>
        public static event Action<LudJob, string, string> JobDone;

        public static bool Configured(BarSettings s)
        {
            if (s == null) return false;
            LudSettings c = LudSettings.Of(s);
            return c.Url.Length > 0 && c.Token.Length > 0;
        }

        public static string Base(BarSettings s)
        {
            string b = LudSettings.Of(s).Url.Trim().TrimEnd('/');
            if (b.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) b = b.Substring(0, b.Length - 4);
            if (b.Length > 0 && !b.Contains("://")) b = "http://" + b;
            return b;
        }

        /// <summary>The dashboard in the browser.</summary>
        public static string WebUrl(BarSettings s) { return Base(s) + "/"; }

        public static void Configure(BarSettings s)
        {
            _s = s;
            LudSettings c = LudSettings.Of(s);
            string id = Base(s) + "|" + c.Token;
            if (_configuredFor != null && _configuredFor != id)
            {
                // another dashboard / token: start over
                ReadOnlyState = "";
                _appAt = DateTime.MinValue;
                lock (Gate) _snap = new LudSnapshot();
            }
            _configuredFor = id;
            if (!Configured(s)) return;
            if (_poll == null)
            {
                _poll = new Poller("Linux Update Dashboard", Poll, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(2));
                _poll.Start();
            }
            else _poll.PollNow();
        }

        public static void Stop()
        {
            if (_poll != null) { _poll.Stop(); _poll = null; }
            lock (Gate) _snap = new LudSnapshot();
            lock (Jobs) Jobs.Clear();
            _s = null;
            _configuredFor = null;
            ReadOnlyState = "";
        }

        /// <summary>The latest snapshot (asking for it keeps the faster polling going).</summary>
        public static LudSnapshot Latest
        {
            get
            {
                if (_poll != null) _poll.Want();
                lock (Gate) return _snap;
            }
        }

        public static void PollNow() { if (_poll != null) _poll.PollNow(); }

        /// <summary>Something was started: poll every 5 seconds for a couple of minutes, starting now.</summary>
        static void PollSoon()
        {
            _quickUntil = DateTime.UtcNow.AddMinutes(2);
            Poller p = _poll;
            if (p != null) { p.Fast = TimeSpan.FromSeconds(5); p.PollNow(); }
        }

        static async Task Poll()
        {
            BarSettings s = _s;
            if (s == null || !Configured(s)) return;
            LudSnapshot old;
            lock (Gate) old = _snap;
            LudSnapshot snap;
            try { snap = await ReadAll(s).ConfigureAwait(false); }
            catch (Exception ex)
            {
                string msg = Why(ex);
                if (old.Error != msg) Log.Write("Linux Update Dashboard: " + msg);
                snap = new LudSnapshot
                {
                    Error = msg, Fetched = old.Fetched, Systems = old.Systems, AppCurrent = old.AppCurrent, AppLatest = old.AppLatest,
                    AppUrl = old.AppUrl, AppUpdate = old.AppUpdate
                };
            }
            // quicker while something runs, so "upgrading…" goes away soon after it's done
            Poller p = _poll;
            if (p != null) p.Fast = snap.Busy || JobCount > 0 || DateTime.UtcNow < _quickUntil ?TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(20);
            Publish(snap);
        }

        static void Publish(LudSnapshot snap)
        {
            System.Windows.Application app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_s == null) return;      // removed meanwhile
                lock (Gate) _snap = snap;
                Action<LudSnapshot> c = Changed;
                if (c != null) try { c(snap); } catch (Exception ex) { Log.Write("Linux Update Dashboard: " + ex.Message); }
            }));
        }

        // =================================================================
        // Calls
        // =================================================================
        static HttpClient Http(BarSettings s)
        {
            bool ic = LudSettings.Of(s).IgnoreCert;
            return PluginHttp.Client("LinuxUpdates" + (ic ? "-ic" : ""), ic, 30, true);
        }

        sealed class LudError : Exception
        {
            public readonly int Code;
            public LudError(int code, string msg) : base(msg) { Code = code; }
        }

        static async Task<JsonDocument> Send(BarSettings s, HttpMethod method, string path, string body = null)
        {
            HttpClient h = Http(s);
            using (var req = new HttpRequestMessage(method, Base(s) + "/api" + path))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LudSettings.Of(s).TokenPlain);
                req.Headers.Accept.ParseAdd("application/json");
                if (body != null || method == HttpMethod.Post) req.Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json");
                using (HttpResponseMessage r = await h.SendAsync(req).ConfigureAwait(false))
                {
                    string text = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode)
                    {
                        string msg = Fail((int)r.StatusCode, text);
                        if ((int)r.StatusCode == 403 && msg.IndexOf("read-only", StringComparison.OrdinalIgnoreCase) >= 0) ReadOnlyState = "yes";
                        throw new LudError((int)r.StatusCode, msg);
                    }
                    if (text.TrimStart().StartsWith("<")) throw new LudError(0, "The address answered with a web page, not the API - is it the dashboard's address (port 3001 by default)?");
                    return JsonDocument.Parse(text.Length == 0 ? "null" : text);
                }
            }
        }

        // =================================================================
        // Reading
        // =================================================================
        static async Task<LudSnapshot> ReadAll(BarSettings s)
        {
            var snap = new LudSnapshot();
            using (JsonDocument d = await Send(s, HttpMethod.Get, "/systems?scope=visible").ConfigureAwait(false))
                foreach (JsonElement e in Arr(d.RootElement, "systems"))
                {
                    JsonElement last = Obj(e, "lastCheck"), op = Obj(e, "activeOperation");
                    string os = (JsonValue.Str(e, "osName") + " " + JsonValue.Str(e, "osVersion")).Trim();
                    var x = new LudSystem
                    {
                        Id = (long)Zero(JsonValue.Num(e, "id")), Name = JsonValue.Str(e, "name"), User = JsonValue.Str(e, "username"), Os = os,
                        Host = JsonValue.Str(e, "hostname"), Port = (int)Zero(JsonValue.Num(e, "port")), Order = (int)Zero(JsonValue.Num(e, "dashboardOrder")),
                        Updates = (int)Zero(JsonValue.Num(e, "updateCount")), Security = (int)Zero(JsonValue.Num(e, "securityCount")),
                        KeptBack = (int)Zero(JsonValue.Num(e, "keptBackCount")), Issues = (int)Zero(JsonValue.Num(e, "packageIssueCount")),
                        NeedsReboot = JsonValue.Bool(e, "needsReboot"), ExcludeFromAll = JsonValue.Bool(e, "excludeFromUpgradeAll"),
                        FullUpgrade = JsonValue.Bool(e, "supportsFullUpgrade"), CacheAge = JsonValue.Str(e, "cacheAge"),
                        CheckStatus = JsonValue.Str(last, "status"), CheckError = OneLine(JsonValue.Str(last, "error")),
                        Checked = Date(JsonValue.Str(last, "completedAt")),
                        Op = JsonValue.Str(op, "type"), OpPhase = JsonValue.Str(op, "phase"), OpPackage = JsonValue.Str(op, "packageName"),
                        Lifecycle = JsonValue.Str(e, "osLifecycleStatus")
                    };
                    // every system has a status ("supported", "unknown" …): only the warnings count, and not one dismissed on the dashboard
                    if (Array.IndexOf(LifecycleWarnings, x.Lifecycle) < 0 || JsonValue.Bool(e, "osLifecycleBannerDismissed")) x.Lifecycle = "";
                    double reach = JsonValue.Num(e, "isReachable");
                    x.Reachable = double.IsNaN(reach) ? -1 : reach > 0 ? 1 : reach < 0 ? -1 : 0;
                    if (x.Port <= 0) x.Port = 22;
                    snap.Systems.Add(x);
                }
            snap.Systems.Sort(delegate (LudSystem a, LudSystem b)
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });

            // a new dashboard version: asked every 6 hours (the dashboard caches it for an hour)
            if ((DateTime.UtcNow - _appAt).TotalHours >= 6)
            {
                _appAt = DateTime.UtcNow;
                try
                {
                    using (JsonDocument d = await Send(s, HttpMethod.Get, "/app-update").ConfigureAwait(false))
                    {
                        JsonElement e = d.RootElement;
                        _appCurrent = JsonValue.Str(e, "currentVersion");
                        _appLatest = JsonValue.Str(e, "remoteVersion");
                        _appUrl = JsonValue.Str(e, "releaseUrl");
                        if (_appUrl.Length == 0) _appUrl = JsonValue.Str(e, "repoUrl");
                        _appUpdate = JsonValue.Bool(e, "updateAvailable");
                    }
                }
                catch (Exception ex) { Log.Write("Linux Update Dashboard: version check: " + Why(ex)); }
            }
            snap.AppCurrent = _appCurrent; snap.AppLatest = _appLatest; snap.AppUrl = _appUrl; snap.AppUpdate = _appUpdate;

            if (ReadOnlyState.Length == 0) await ProbeWrite(s).ConfigureAwait(false);
            snap.Fetched = DateTime.UtcNow;
            return snap;
        }

        /// <summary>
        /// Can the token change things? A cancel for system 0 (there's no such system, so nothing happens): a read-only token gets
        /// "This token is read-only" (403) before the dashboard looks for the system; a read/write one gets "not found" or similar.
        /// </summary>
        static async Task ProbeWrite(BarSettings s)
        {
            try
            {
                using (await Send(s, HttpMethod.Post, "/systems/0/cancel").ConfigureAwait(false)) { }
                ReadOnlyState = "no";
            }
            catch (LudError) { if (!ReadOnly) ReadOnlyState = "no"; }      // Send set "yes" for the read-only answer
            catch { }       // couldn't ask: try again next time
        }

        // =================================================================
        // Actions
        // =================================================================
        /// <summary>Start a check / upgrade / full-upgrade / autoremove on a system. Null when it started (its job is then followed).</summary>
        public static async Task<string> StartAsync(BarSettings s, LudSystem x, string what)
        {
            try
            {
                string jobId;
                using (JsonDocument d = await Send(s, HttpMethod.Post, "/systems/" + x.Id + "/" + what).ConfigureAwait(false))
                    jobId = JsonValue.Str(d.RootElement, "jobId");
                if (jobId.Length > 0) AddJob(new LudJob { Id = jobId, What = what, SystemId = x.Id, Name = x.Label });
                PollSoon();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Reboot a system (the dashboard does it straight away and says how it went). Null when it worked.</summary>
        public static async Task<string> RebootAsync(BarSettings s, LudSystem x)
        {
            try
            {
                using (await Send(s, HttpMethod.Post, "/systems/" + x.Id + "/reboot").ConfigureAwait(false)) { }
                PollSoon();
                return null;
            }
            catch (Exception ex) { PollNow(); return Why(ex); }
        }

        /// <summary>Ask the dashboard to stop what's running on a system. Null when it was asked.</summary>
        public static async Task<string> CancelAsync(BarSettings s, LudSystem x)
        {
            try
            {
                using (await Send(s, HttpMethod.Post, "/systems/" + x.Id + "/cancel").ConfigureAwait(false)) { }
                PollNow();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>Check every system (in the background on the dashboard). Null when it started.</summary>
        public static async Task<string> CheckAllAsync(BarSettings s)
        {
            try
            {
                using (await Send(s, HttpMethod.Post, "/systems/check-all").ConfigureAwait(false)) { }
                PollSoon();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        /// <summary>The systems Upgrade all would upgrade: reachable, not busy, with updates, and not left out of Upgrade all on the dashboard.</summary>
        public static List<LudSystem> UpgradeAllList(LudSnapshot snap)
        {
            var l = new List<LudSystem>();
            foreach (LudSystem x in snap.Systems) if (x.Updates > 0 && !x.ExcludeFromAll && !x.Busy && x.Reachable != 0) l.Add(x);
            return l;
        }

        /// <summary>Queue Upgrade all for these systems (the dashboard runs them in its priority order). Null when queued.</summary>
        public static async Task<string> UpgradeAllAsync(BarSettings s, List<LudSystem> systems)
        {
            try
            {
                var b = new StringBuilder("{\"items\":[");
                for (int i = 0; i < systems.Count; i++) b.Append(i > 0 ? "," : "").Append("{\"systemId\":").Append(systems[i].Id.ToString(CultureInfo.InvariantCulture)).Append('}');
                b.Append("]}");
                using (await Send(s, HttpMethod.Post, "/systems/upgrade-all", b.ToString()).ConfigureAwait(false)) { }
                PollSoon();
                return null;
            }
            catch (Exception ex) { return Why(ex); }
        }

        // ---- jobs ----
        static int JobCount { get { lock (Jobs) return Jobs.Count; } }

        /// <summary>The job vShell started on this system and is still following, or null.</summary>
        public static LudJob JobOn(long systemId)
        {
            lock (Jobs) foreach (LudJob j in Jobs) if (j.SystemId == systemId) return j;
            return null;
        }

        static void AddJob(LudJob j)
        {
            bool start;
            lock (Jobs)
            {
                Jobs.Add(j);
                start = !_jobsRunning;
                _jobsRunning = true;
            }
            if (start) Task.Run(FollowJobs);
        }

        /// <summary>Asks about each job every 2 seconds until it's done or failed (or gone, or 30 minutes old).</summary>
        static async Task FollowJobs()
        {
            while (true)
            {
                await Task.Delay(2000).ConfigureAwait(false);
                BarSettings s = _s;
                List<LudJob> now;
                lock (Jobs)
                {
                    if (Jobs.Count == 0 || s == null) { Jobs.Clear(); _jobsRunning = false; return; }
                    now = new List<LudJob>(Jobs);
                }
                foreach (LudJob j in now)
                {
                    string result = null, said = "";
                    try
                    {
                        using (JsonDocument d = await Send(s, HttpMethod.Get, "/jobs/" + Uri.EscapeDataString(j.Id)).ConfigureAwait(false))
                        {
                            JsonElement e = d.RootElement, r = Obj(e, "result");
                            string status = JsonValue.Str(e, "status");
                            if (status == "running" || status.Length == 0) { if ((DateTime.UtcNow - j.Started).TotalMinutes < 30) continue; result = "unknown"; }
                            else
                            {
                                // "done" even when the upgrade itself failed: the result says
                                result = JsonValue.Str(r, "status");
                                if (result.Length == 0) result = status == "failed" ? "failed" : "success";
                                said = JsonValue.Str(r, "error");
                                if (said.Length == 0) said = JsonValue.Str(e, "error");
                                if (said.Length == 0 && j.What == "check" && r.ValueKind == JsonValueKind.Object)
                                {
                                    double n = JsonValue.Num(r, "updateCount"), sec = JsonValue.Num(r, "securityCount");
                                    if (!double.IsNaN(n)) said = n == 0 ? "Up to date" : Count((int)n, "update") + (sec > 0 ? " (" + sec + " security)" : "");
                                }
                                if (said.Length == 0) said = LastLine(JsonValue.Str(r, "output"));
                            }
                        }
                    }
                    catch (LudError e) { if (e.Code == 404) result = "unknown"; else continue; }       // gone (kept 5 minutes after it ends)
                    catch { continue; }      // can't ask right now: next time
                    lock (Jobs) Jobs.Remove(j);
                    Finished(j, result, OneLine(said));
                }
                PollNow();
            }
        }

        static void Finished(LudJob j, string result, string said)
        {
            System.Windows.Application app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_s == null) return;
                Action<LudJob, string, string> d = JobDone;
                if (d != null) try { d(j, result, said); } catch (Exception ex) { Log.Write("Linux Update Dashboard: " + ex.Message); }
            }));
        }

        /// <summary>Test connection: reads the systems, or says why it can't.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the address and the API token first.");
                ReadOnlyState = "";
                _appAt = DateTime.MinValue;
                LudSnapshot snap = await ReadAll(s).ConfigureAwait(false);
                PollNow();
                string what = ReadOnly ? "a read-only token: vShell shows everything, but Check now, upgrades and reboots need a read/write token"
                            : ReadOnlyState == "no" ? "a read/write token" : "";
                return new KeyValuePair<bool, string>(true,
                    "Connected" + (snap.AppCurrent.Length > 0 ? " - Linux Update Dashboard " + snap.AppCurrent : "") + ", " +
                    Count(snap.Systems.Count, "system") + ", " + Count(snap.Updates, "update") +
                    (snap.Security > 0 ? " (" + snap.Security + " security)" : "") + (what.Length > 0 ? ". It's " + what : "") + ".");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, Why(ex)); }
        }

        public static string Count(int n, string what) { return n == 1 ? "1 " + what : n + " " + what + "s"; }

        // ---- helpers ----
        static string Fail(int code, string body)
        {
            string msg = "";
            try
            {
                using (JsonDocument d = JsonDocument.Parse(body))
                {
                    msg = JsonValue.Str(d.RootElement, "error");
                    string more = JsonValue.Str(d.RootElement, "message");
                    if (more.Length > 0 && more != msg) msg = msg.Length > 0 ? msg + " - " + more : more;
                }
            }
            catch { }
            if (code == 401) return "Linux Update Dashboard didn't accept the API token" + (msg.Length > 0 ? " (" + msg + ")" : "");
            if (code == 429) return "Linux Update Dashboard says too many wrong tokens were tried - wait a minute";
            if (code == 404 && msg.Length == 0) return "Linux Update Dashboard answered 404 - is it the dashboard's address (port 3001 by default)?";
            if (msg.Length == 0 && body != null && body.Length < 200 && !body.TrimStart().StartsWith("<")) msg = body.Trim();
            return msg.Length > 0 ? msg : "Linux Update Dashboard answered " + code;
        }

        public static string Why(Exception ex)
        {
            if (ex is TaskCanceledException) return "Linux Update Dashboard didn't answer in time";
            if (ex is HttpRequestException && ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        static string OneLine(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }

        /// <summary>The last line with something on it (the end of an upgrade's output).</summary>
        static string LastLine(string output)
        {
            string[] lines = (output ?? "").Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--) if (lines[i].Trim().Length > 0) return lines[i].Trim();
            return "";
        }

        static double Zero(double v) { return double.IsNaN(v) ? 0 : v; }

        /// <summary>The dashboard's times: "2026-10-30 04:15:00", in UTC.</summary>
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

        static IEnumerable<JsonElement> Arr(JsonElement e, string name)
        {
            JsonElement v;
            if (!(e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v))) yield break;
            if (v.ValueKind == JsonValueKind.Array)
                foreach (JsonElement i in v.EnumerateArray()) yield return i;
        }
    }
}
