using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VShellVTrace
{
    /// <summary>Settings > Plugins > vTrace: the vTrace server (its address and an app key), and what this PC does with it.</summary>
    public sealed class VtSettings
    {
        readonly SettingsBag _b;
        VtSettings(SettingsBag b) { _b = b; }
        public static VtSettings Of(BarSettings s) { return new VtSettings(s.Integration("vtrace")); }

        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>The app key (protected).</summary>
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", string.IsNullOrEmpty(value) ? "" : Secret.Protect(value.Trim())); } }
        public bool SendColours { get { return _b.Get("SendColours", true); } set { _b.Set("SendColours", value); } }
        public bool Notifications { get { return _b.Get("Notifications", true); } set { _b.Set("Notifications", value); } }
        /// <summary>Send this PC's own changes (Windows' logs) to the server.</summary>
        public bool SendPc { get { return _b.Get("SendPc", true); } set { _b.Set("SendPc", value); } }
        /// <summary>The newest PC change sent (UTC, round-trip text) - the next send starts from a little before it.</summary>
        public string PcSentUtc { get { return _b.Get("PcSentUtc", ""); } set { _b.Set("PcSentUtc", value ?? ""); } }

        public bool Configured { get { return Url.Length > 0 && Key.Length > 0; } }
    }

    /// <summary>One look at the vTrace server (/api/v1/changes).</summary>
    public sealed class VtSnapshot
    {
        public bool Ok, Checked, Looking;
        public string Error = "", Version = "";
        public List<Change> Changes = new List<Change>();
        public DateTime Looked = DateTime.MinValue;
        public int Problems;
    }

    /// <summary>Something vTrace noticed (/api/v1/events): an outage starting or ending, a change that failed, a source it can't read.</summary>
    public sealed class VtEvent
    {
        public long Id;
        public string Kind = "", Title = "", Detail = "", Group = "", Link = "", Look = "";
        public bool ToVShell;
    }

    /// <summary>The vTrace server's API with the app key from Settings > Plugins > vTrace.</summary>
    public static class VtClient
    {
        static HttpClient Http { get { return PluginHttp.Client("vtrace", false, 30); } }

        public static string BaseUrl(VtSettings c)
        {
            string u = (c.Url ?? "").Trim().TrimEnd('/');
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            return u;
        }

        static HttpRequestMessage Req(HttpMethod m, VtSettings c, string path)
        {
            var r = new HttpRequestMessage(m, BaseUrl(c) + path);
            string key = Secret.Unprotect(c.Key ?? "");
            if (!string.IsNullOrEmpty(key)) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return r;
        }

        static async Task<string> Send(HttpRequestMessage r, CancellationToken ct)
        {
            using (r)
            using (HttpResponseMessage res = await Http.SendAsync(r, ct).ConfigureAwait(false))
            {
                string body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if ((int)res.StatusCode == 401) throw new InvalidOperationException("vTrace didn't take the key - make one in vTrace's Options > Sign-in > Apps (it starts vt_)");
                if ((int)res.StatusCode == 302 || body.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That address answered with a page, not vTrace's API - is it the vTrace server (port 8480)?");
                if (!res.IsSuccessStatusCode)
                {
                    string why = "";
                    try { using (var d = JsonDocument.Parse(body)) why = JsonValue.Str(d.RootElement, "message"); } catch { }
                    throw new InvalidOperationException(why.Length > 0 ? why : "vTrace said " + (int)res.StatusCode);
                }
                return body;
            }
        }

        /// <summary>The last week's changes (with outages still going and their analysis) - what the modules show.</summary>
        public static async Task<VtSnapshot> ChangesAsync(VtSettings c, CancellationToken ct)
        {
            string since = Uri.EscapeDataString(DateTimeOffset.Now.AddDays(-7).ToString("o", CultureInfo.InvariantCulture));
            return ParseChanges(await Send(Req(HttpMethod.Get, c, "/api/v1/changes?since=" + since + "&limit=3000"), ct).ConfigureAwait(false));
        }

        public static VtSnapshot ParseChanges(string json)
        {
            var snap = new VtSnapshot { Ok = true, Checked = true };
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement r = d.RootElement;
                snap.Version = JsonValue.Str(r, "version");
                snap.Looking = JsonValue.Bool(r, "looking", false);
                snap.Looked = Date(JsonValue.Str(r, "looked"));
                if (r.TryGetProperty("sources", out JsonElement src) && src.ValueKind == JsonValueKind.Array)
                    snap.Problems = src.EnumerateArray().Count(s => JsonValue.Bool(s, "problem", false));
                if (r.TryGetProperty("changes", out JsonElement ch) && ch.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in ch.EnumerateArray())
                        snap.Changes.Add(new Change
                        {
                            Id = JsonValue.Str(e, "id"), Source = JsonValue.Str(e, "source"), Title = JsonValue.Str(e, "title"), Detail = JsonValue.Str(e, "detail"),
                            Host = JsonValue.Str(e, "host"), Kind = JsonValue.Str(e, "kind"), When = Date(JsonValue.Str(e, "when")), Until = Date(JsonValue.Str(e, "until")),
                            Failed = JsonValue.Bool(e, "failed", false), Group = JsonValue.Str(e, "group"), GroupOn = JsonValue.Str(e, "groupOn"),
                            GroupCount = (int)JsonValue.Num(e, "groupCount"), RunsOn = JsonValue.Str(e, "runsOn"), Cause = JsonValue.Str(e, "cause"),
                            CauseWhy = JsonValue.Str(e, "causeWhy"), CauseId = JsonValue.Str(e, "causeId"), Explanation = JsonValue.Str(e, "explanation"), AiBy = JsonValue.Str(e, "aiBy"),
                        });
            }
            snap.Changes = snap.Changes.OrderByDescending(x => x.When).ToList();
            return snap;
        }

        /// <summary>An ISO time with its offset -> this PC's local time; "" -> MinValue.</summary>
        static DateTime Date(string s)
        {
            return s.Length > 0 && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset d) ? d.LocalDateTime : DateTime.MinValue;
        }

        public static async Task<List<VtEvent>> EventsAsync(VtSettings c, long? after, CancellationToken ct)
        {
            string path = after == null ? "/api/v1/events?limit=1" : "/api/v1/events?after=" + after.Value.ToString(CultureInfo.InvariantCulture);
            var list = new List<VtEvent>();
            using (JsonDocument d = JsonDocument.Parse(await Send(Req(HttpMethod.Get, c, path), ct).ConfigureAwait(false)))
            {
                if (d.RootElement.TryGetProperty("last", out JsonElement last) && last.ValueKind == JsonValueKind.Number) LastEvent = last.GetInt64();
                // vTrace 0.1.8+: has anything in /changes changed since the last look at it? (null: an older server - always read)
                if (d.RootElement.TryGetProperty("rev", out JsonElement rev) && rev.ValueKind == JsonValueKind.Number)
                {
                    LastProblems = (int)JsonValue.Num(d.RootElement, "problems");
                    LastLooking = JsonValue.Bool(d.RootElement, "looking", false);
                    LastLooked = Date(JsonValue.Str(d.RootElement, "looked"));
                    ChangesKey = rev.GetInt64().ToString(CultureInfo.InvariantCulture) + "|" + LastProblems.ToString(CultureInfo.InvariantCulture);
                }
                else ChangesKey = null;
                if (d.RootElement.TryGetProperty("events", out JsonElement ev) && ev.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in ev.EnumerateArray())
                        list.Add(new VtEvent
                        {
                            Id = (long)JsonValue.Num(e, "id"), Kind = JsonValue.Str(e, "kind"), Title = JsonValue.Str(e, "title"), Detail = JsonValue.Str(e, "detail"),
                            Group = JsonValue.Str(e, "group"), Link = JsonValue.Str(e, "link"), Look = JsonValue.Str(e, "look"), ToVShell = JsonValue.Bool(e, "toVShell", false),
                        });
            }
            return list;
        }

        /// <summary>The server's newest event id, from the last /events answer.</summary>
        public static long LastEvent;
        /// <summary>From the last /events answer: what /changes would say now ("revision|sources with a problem"; null = the server doesn't tell).</summary>
        public static string ChangesKey;
        public static bool LastLooking;
        public static DateTime LastLooked = DateTime.MinValue;
        public static int LastProblems;

        /// <summary>Ask vTrace to look at every source now. null = it's on it, else why not.</summary>
        public static async Task<string> LookAsync(VtSettings c)
        {
            try { await Send(Req(HttpMethod.Post, c, "/api/v1/look"), CancellationToken.None).ConfigureAwait(false); return null; }
            catch (Exception ex) { return Plain(ex); }
        }

        /// <summary>This PC's own changes to the server (POST /api/v1/pc). Returns how many were new there.</summary>
        public static async Task<int> SendPcAsync(VtSettings c, string machine, List<Dictionary<string, object>> changes)
        {
            var r = Req(HttpMethod.Post, c, "/api/v1/pc");
            r.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object> { ["machine"] = machine, ["changes"] = changes }), Encoding.UTF8, "application/json");
            using (JsonDocument d = JsonDocument.Parse(await Send(r, CancellationToken.None).ConfigureAwait(false)))
                return (int)JsonValue.Num(d.RootElement, "added");
        }

        /// <summary>vShell's colours, for vTrace's pages (vSuite.Web's WebTheme fields - as vDeploy's and vPatch's).</summary>
        public static string ThemeJson(BarSettings s)
        {
            Theme t = Theme.From(s, 12.5, s.WidgetStyle != null ? s.WidgetStyle.CornerRadius : 8);
            string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
            var o = new Dictionary<string, object>
            {
                ["Background"] = Hex(t.BgColor), ["Background2"] = Hex(t.Bg2Color), ["Foreground"] = Hex(t.Fg.Color),
                ["MainAccent"] = Hex(t.MainAccent.Color), ["SecondaryAccent"] = Hex(t.SecondaryAccent.Color),
                ["Gradient"] = t.Gradient, ["GradientAngle"] = t.GradientAngle, ["Radius"] = Math.Round(t.Radius, 1),
            };
            return JsonSerializer.Serialize(o);
        }

        public static async Task<string> SendThemeAsync(VtSettings c, string json)
        {
            try
            {
                var r = Req(HttpMethod.Put, c, "/api/v1/theme");
                r.Content = new StringContent(json, Encoding.UTF8, "application/json");
                await Send(r, CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex) { return Plain(ex); }
        }

        public static string Plain(Exception ex)
        {
            if (ex is TaskCanceledException) return "vTrace didn't answer in time";
            if (ex is HttpRequestException h) return "Couldn't reach vTrace" + (h.InnerException != null ? ": " + h.InnerException.Message : ": " + h.Message);
            if (ex is JsonException) return "vTrace's answer wasn't what was expected - is it the vTrace server?";
            return ex.Message;
        }
    }

    /// <summary>
    /// This PC's own changes from Windows' event logs (as the Windows vTrace read them): updates installed or failed, programs installed /
    /// removed (Windows Installer), drivers, starts, shut-downs and who asked for a restart. The vTrace server can't read them (it's on
    /// Linux), so the plugin sends them (POST /api/v1/pc) - nothing else of this PC leaves it.
    /// </summary>
    public static class PcLog
    {
        /// <summary>The changes since 'since', as the API takes them (id, when with its offset, title, detail, body, failed, kind).</summary>
        public static List<Dictionary<string, object>> Read(DateTime sinceUtc)
        {
            var list = new List<Dictionary<string, object>>();
            string at = sinceUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string host = Environment.MachineName;
            Query(list, "System", "Microsoft-Windows-WindowsUpdateClient", "EventID=19 or EventID=20", at, r =>
            {
                string title = Prop(r, 0);
                bool ok = r.Id == 19;
                return Make(r, "System", (ok ? "Windows update installed: " : "Windows update failed: ") + Short(title), ok ? "Windows Update" : "Windows Update · failed", !ok, title, "update", host);
            });
            Query(list, "System", "Microsoft-Windows-Kernel-General", "EventID=12 or EventID=13", at, r =>
                Make(r, "System", r.Id == 12 ? "Started up" : "Shut down", r.Id == 12 ? "Windows started" : "Windows shut down", false, "", r.Id == 12 ? "restart" : "stop", host));
            Query(list, "System", "User32", "EventID=1074", at, r =>
            {
                string proc = System.IO.Path.GetFileName(Prop(r, 0));
                string reason = Prop(r, 2), kind = Prop(r, 4), user = Prop(r, 6);
                string what = kind.IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0 ? "Restart" : kind.IndexOf("off", StringComparison.OrdinalIgnoreCase) >= 0 ? "Shut-down" : "Restart / shut-down";
                return Make(r, "System", what + " asked for by " + (proc.Length > 0 ? proc : "Windows"), reason + (user.Length > 0 ? " · " + user : ""), false, Describe(r), "restart", host);
            });
            Query(list, "Application", "MsiInstaller", "EventID=11707 or EventID=11724 or EventID=11708", at, r =>
            {
                string d = Describe(r);
                Match m = Regex.Match(d, @"Product:\s*(.*?)\s*--");
                string product = m.Success ? m.Groups[1].Value : Prop(r, 0);
                string title = r.Id == 11724 ? "Removed " + product : r.Id == 11708 ? "Install failed: " + product : "Installed " + product;
                return Make(r, "Application", title, "Windows Installer", r.Id == 11708, d, "install", host);
            });
            Query(list, "System", "Microsoft-Windows-UserPnp", "EventID=20001", at, r =>
            {
                string d = Describe(r);
                Match m = Regex.Match(d, @"install driver\s+(\S+)", RegexOptions.IgnoreCase);
                if (!m.Success) return null;
                string inf = m.Groups[1].Value;
                int us = inf.IndexOf(".inf", StringComparison.OrdinalIgnoreCase);
                if (us > 0) inf = inf.Substring(0, us + 4);
                bool ok = d.IndexOf("status: 0x0", StringComparison.OrdinalIgnoreCase) >= 0;
                return Make(r, "System", (ok ? "Driver installed: " : "Driver install failed: ") + inf, "Driver Management", !ok, d, "driver", host);
            });
            return list;
        }

        static void Query(List<Dictionary<string, object>> list, string log, string provider, string ids, string at, Func<EventRecord, Dictionary<string, object>> make)
        {
            string xpath = "*[System[Provider[@Name='" + provider + "'] and (" + ids + ") and TimeCreated[@SystemTime>='" + at + "']]]";
            try
            {
                using (var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath)))
                {
                    int n = 0;
                    for (EventRecord r = reader.ReadEvent(); r != null && n < 2000; r = reader.ReadEvent(), n++)
                        using (r)
                        {
                            Dictionary<string, object> e = null;
                            try { e = make(r); } catch (Exception ex) { Log.Write("vTrace plugin: PC log " + provider + ": " + ex.Message); }
                            if (e != null) list.Add(e);
                        }
                }
            }
            catch (Exception ex) { Log.Write("vTrace plugin: PC log " + log + "/" + provider + ": " + ex.Message); }
        }

        static Dictionary<string, object> Make(EventRecord r, string log, string title, string detail, bool failed, string body, string kind, string host)
        {
            DateTime when = r.TimeCreated ?? DateTime.Now;
            return new Dictionary<string, object>
            {
                ["id"] = log + ":" + r.RecordId, ["when"] = new DateTimeOffset(when).ToString("o", CultureInfo.InvariantCulture),
                ["title"] = title, ["detail"] = detail ?? "", ["failed"] = failed, ["body"] = body ?? "", ["kind"] = kind,
                ["machine"] = host + " " + Environment.UserDomainName,
            };
        }

        static string Prop(EventRecord r, int i)
        {
            try { return r.Properties.Count > i ? Convert.ToString(r.Properties[i].Value, CultureInfo.InvariantCulture) ?? "" : ""; }
            catch { return ""; }
        }

        static string Describe(EventRecord r) { try { return r.FormatDescription() ?? ""; } catch { return ""; } }

        /// <summary>"2026-11 Cumulative Update for Windows 11 Version 25H2 for x64-based Systems (KB5049981)" -> "… Cumulative Update (KB5049981)".</summary>
        public static string Short(string title)
        {
            if (string.IsNullOrEmpty(title)) return "an update";
            string t = Regex.Replace(title, @"\s+for\s+(Windows|x64|ARM64|Microsoft).*?(?=\(KB|\s-\s|$)", " ", RegexOptions.IgnoreCase).Trim();
            return Regex.Replace(t, @"\s{2,}", " ");
        }
    }

    /// <summary>
    /// The plugin with a vTrace server: looks at it (every 10 seconds while a module shows it, every 30 otherwise), shows the events it
    /// marks for vShell as notifications (never older ones at start), sends vShell's colours, and this PC's own changes every 15 minutes.
    /// </summary>
    public static class TraceServer
    {
        public const string Source = "vTrace";
        public const string KindOutage = "vTrace.Outage", KindProblem = "vTrace.Problem", KindBack = "vTrace.Back";

        public static VtSnapshot Latest = new VtSnapshot();
        public static Poller Poll;
        static BarSettings _settings;
        static Dispatcher _ui;
        static BarManager _mgr;
        static long? _seen;
        static string _themeSent;
        static DateTime _themeTried = DateTime.MinValue, _pcTried = DateTime.MinValue;
        static int _pcBusy;
        public static string PcStatus = "";

        /// <summary>A server is set up: the modules show it, not the Windows app's file.</summary>
        public static bool On { get { return _settings != null && VtSettings.Of(_settings).Configured; } }

        public static void Start(PluginHost host)
        {
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            _mgr = host.Manager;
            Latest = new VtSnapshot();
            host.AddNotifyKind(KindOutage, NotifyLevel.Alert);
            host.AddNotifyKind(KindProblem, NotifyLevel.Important);
            host.AddNotifyKind(KindBack, NotifyLevel.Normal);
            host.AddNotifySource(Source);
            Poll = new Poller(Source, PollOnce, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        }

        public static void Stop()
        {
            if (Poll != null) { Poll.Stop(); Poll.Dispose(); Poll = null; }
            Latest = new VtSnapshot();
        }

        public static void OnSettings(BarSettings s)
        {
            _settings = s;
            _changesKey = null;      // it may be another server now: read its changes afresh
            if (Poll == null) return;
            if (!VtSettings.Of(s).Configured) { if (Poll.Running) Poll.Stop(); Latest = new VtSnapshot(); _seen = null; return; }
            if (!Poll.Running) Poll.Start(); else Poll.PollNow();
            SendTheme(false);
        }

        static string _changesKey;
        static DateTime _changesAt = DateTime.MinValue;

        static async Task PollOnce()
        {
            BarSettings s = _settings;
            if (s == null) return;
            VtSettings c = VtSettings.Of(s);
            if (!c.Configured) return;
            // the events first (a small answer): with them the server says whether its changes are still what we have
            List<VtEvent> events = null;
            string key = null;
            try
            {
                events = await VtClient.EventsAsync(c, _seen, CancellationToken.None).ConfigureAwait(false);
                key = VtClient.ChangesKey;
            }
            catch (Exception ex) { Log.Write("vTrace plugin: events: " + VtClient.Plain(ex)); }
            VtSnapshot snap, had = Latest;
            if (key != null && key == _changesKey && had.Ok && DateTime.UtcNow - _changesAt < TimeSpan.FromMinutes(10))
            {
                // nothing changed there: the week of changes isn't downloaded and read again (every 10 minutes it is anyway -
                // the week moves on)
                snap = new VtSnapshot { Ok = true, Checked = true, Version = had.Version, Changes = had.Changes, Problems = VtClient.LastProblems, Looked = VtClient.LastLooked, Looking = VtClient.LastLooking };
            }
            else
            {
                try
                {
                    snap = await VtClient.ChangesAsync(c, CancellationToken.None).ConfigureAwait(false);
                    _changesKey = key;
                    _changesAt = DateTime.UtcNow;
                }
                catch (Exception ex) { snap = new VtSnapshot { Ok = false, Checked = true, Error = VtClient.Plain(ex), Changes = Latest.Changes }; _changesKey = null; }
            }
            if (!snap.Ok) events = null;
            Publish(snap, events);
            if (snap.Ok && _themeSent == null) SendTheme(false);
            if (snap.Ok && c.SendPc && DateTime.UtcNow - _pcTried > TimeSpan.FromMinutes(15)) _ = SendPcAsync(false);
        }

        static void Publish(VtSnapshot snap, List<VtEvent> events)
        {
            if (_ui == null || _ui.CheckAccess()) Apply(snap, events);
            else _ui.BeginInvoke(new Action(delegate { Apply(snap, events); }));
        }

        static void Apply(VtSnapshot snap, List<VtEvent> events)
        {
            Latest = snap;
            if (snap.Ok && events != null)
            {
                // the first look starts from now (older events aren't replayed); after that only as far as the events that
                // actually came - the server sends 200 at a time, and its "last" can be ahead of them
                long upTo = _seen ?? VtClient.LastEvent;
                if (_seen != null)
                {
                    foreach (VtEvent e in events.OrderBy(x => x.Id))
                    {
                        if (e.Id <= upTo) continue;
                        if (e.ToVShell) Tell(e);
                        upTo = e.Id;
                    }
                    if (VtClient.LastEvent < upTo) upTo = VtClient.LastEvent;    // the server's Activity was started again: follow its numbers
                }
                _seen = upTo;
            }
        }

        static void Tell(VtEvent e)
        {
            if (_settings == null || !VtSettings.Of(_settings).Notifications) return;
            string kind = e.Kind == "down" ? KindOutage : e.Look == "bad" || e.Look == "warn" ? KindProblem : KindBack;
            string url = Url(e.Link.Length > 0 ? e.Link : "/activity");
            try { Notify.Post(Source, e.Title, e.Detail, url, Notify.KindOf(kind), true, null); }
            catch (Exception ex) { Log.Write("vTrace plugin: notify failed: " + ex.Message); }
        }

        public static string Url(string path) { return _settings == null ? "" : VtClient.BaseUrl(VtSettings.Of(_settings)) + path; }

        /// <summary>vTrace's page for a change (or its timeline), in the browser.</summary>
        public static void Open(string changeId)
        {
            string url = Url(string.IsNullOrEmpty(changeId) ? "/" : "/change/" + Uri.EscapeDataString(changeId));
            try { if (url.Length > 0) RunPanel.Open(url); } catch (Exception ex) { Log.Write("vTrace plugin: open: " + ex.Message); }
        }

        static DateTime _lookAsked = DateTime.MinValue;
        /// <summary>The server is looking (it said so, or it was asked a moment ago).</summary>
        public static bool Looking { get { return Latest.Looking || DateTime.UtcNow - _lookAsked < TimeSpan.FromSeconds(8); } }

        /// <summary>The refresh button: the server looks at every source now (and the next poll shows what it found).</summary>
        public static async void LookNow()
        {
            if (_settings == null) return;
            _lookAsked = DateTime.UtcNow;
            string err = await VtClient.LookAsync(VtSettings.Of(_settings));
            if (err != null) Log.Write("vTrace plugin: look now: " + err);
            if (Poll != null) Poll.PollNow();
            if (VtSettings.Of(_settings).SendPc) _ = SendPcAsync(true);
        }

        /// <summary>
        /// This PC's changes since the last send (the first time: 30 days), to the server. A little overlap is fine - the server keeps one
        /// of each. Not twice at once; quietly logged when it doesn't work.
        /// </summary>
        public static async Task SendPcAsync(bool now)
        {
            BarSettings s = _settings;
            if (s == null || Interlocked.Exchange(ref _pcBusy, 1) == 1) return;
            _pcTried = DateTime.UtcNow;
            try
            {
                VtSettings c = VtSettings.Of(s);
                DateTime since = DateTime.TryParse(c.PcSentUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime last) ? last.ToUniversalTime().AddHours(-1) : DateTime.UtcNow.AddDays(-30);
                List<Dictionary<string, object>> changes = await Task.Run(() => PcLog.Read(since)).ConfigureAwait(false);
                int added = 0;
                for (int i = 0; i < changes.Count; i += 2000)
                    added += await VtClient.SendPcAsync(c, Environment.MachineName, changes.Skip(i).Take(2000).ToList()).ConfigureAwait(false);
                DateTime sent = DateTime.UtcNow;
                _ = _ui.BeginInvoke(new Action(delegate { VtSettings.Of(s).PcSentUtc = sent.ToString("o", CultureInfo.InvariantCulture); if (_mgr != null) _mgr.SaveSoon(); }));
                PcStatus = "This PC's changes sent " + TimeText.Clock(DateTime.Now) + (added > 0 ? " · " + added + " new" : "");
            }
            catch (Exception ex) { PcStatus = "Couldn't send this PC's changes: " + VtClient.Plain(ex); Log.Write("vTrace plugin: " + PcStatus); }
            finally { Interlocked.Exchange(ref _pcBusy, 0); }
        }

        public static async void SendTheme(bool always)
        {
            BarSettings s = _settings;
            if (s == null) return;
            VtSettings c = VtSettings.Of(s);
            if (!c.Configured || !c.SendColours) return;
            string json;
            try { json = VtClient.ThemeJson(s); } catch (Exception ex) { Log.Write("vTrace plugin: theme: " + ex.Message); return; }
            if (!always && (json == _themeSent || DateTime.UtcNow - _themeTried < TimeSpan.FromSeconds(3))) return;
            _themeTried = DateTime.UtcNow;
            string err = await VtClient.SendThemeAsync(c, json);
            if (err == null) _themeSent = json;
            else Log.Write("vTrace plugin: couldn't send the colours: " + err);
        }

        public static void ThemeChanged() { _themeSent = null; SendTheme(true); }

        public static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            if (_settings == null || !VtSettings.Of(_settings).Configured) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            try
            {
                VtSnapshot s = await VtClient.ChangesAsync(VtSettings.Of(_settings), CancellationToken.None);
                Publish(s, null);
                _themeSent = null;
                SendTheme(true);
                int today = s.Changes.Count(x => x.When.Date == DateTime.Today);
                return new KeyValuePair<bool, string>(true, "vTrace " + s.Version + " - " + today + " change" + (today == 1 ? "" : "s") + " today" + (s.Problems > 0 ? " · " + s.Problems + " source" + (s.Problems == 1 ? "" : "s") + " can't be read" : ""));
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, VtClient.Plain(ex)); }
        }
    }
}
