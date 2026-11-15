using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

namespace VDeploy
{
    /// <summary>A stack as vDeploy shows it.</summary>
    public sealed class VdStack
    {
        public string HostId = "", Host = "", Name = "", Title = "", Look = "off", Deploying;
        public bool Other, Auto, Drift, HostOnline = true;
        public int Running, Total;
        public List<string> Updates = new List<string>();
        public List<VdContainer> Containers = new List<VdContainer>();

        public bool Problem { get { return !Other && (Look == "warn" || (Look == "off" && Total > 0) || !HostOnline); } }
        public string Key { get { return HostId + "/" + Name; } }
        /// <summary>"running", "2 of 3 running", "stopped", "deploying" …</summary>
        public string StateText
        {
            get
            {
                if (Deploying != null) return Deploying.StartsWith("Redeploying") ? "redeploying…" : Deploying.StartsWith("Rolling") ? "rolling back…" : "deploying…";
                if (!HostOnline) return "host not answering";
                if (Total == 0) return "nothing running";
                if (Running == 0) return "stopped";
                if (Running < Total) return Running + " of " + Total + " running";
                return Look == "warn" ? "running, not healthy" : "running";
            }
        }
    }

    public sealed class VdContainer
    {
        public string Name = "", Service = "", Image = "", State = "", Status = "";
        public double? Cpu;
        public long? Memory;

        /// <summary>Its Compose service, else its name - what vDeploy's API takes (?container=) and what's shown.</summary>
        public string Label { get { return Service.Length > 0 ? Service : Name; } }
        public bool Running { get { return State == "running" || State == "restarting"; } }
        /// <summary>ok / warn / off, as vDeploy colours it.</summary>
        public string Look
        {
            get
            {
                if (State == "running") return Status.Contains("unhealthy") || Status.Contains("health: starting") ? "warn" : "ok";
                if (State == "restarting" || State == "paused") return "warn";
                return State == "exited" && Status.StartsWith("Exited (0)") ? "off" : State == "exited" ? "warn" : "off";
            }
        }
        public string StateText
        {
            get
            {
                string w = State switch { "running" => "running", "restarting" => "restarting", "exited" => "stopped", "paused" => "paused", "created" => "made, not started", "dead" => "dead", _ => State };
                if (State == "running" && Status.Contains("unhealthy")) w += ", unhealthy";
                return w;
            }
        }
    }

    /// <summary>An automation in vDeploy (/api/v1/automations - vDeploy 0.1.5's stage 6).</summary>
    public sealed class VdAutomation
    {
        public string Id = "", Name = "", Schedule;
        public int Steps;
        public bool Running, HasRun, LastOk;
        public DateTime LastUtc;
        public string LastBy = "";

        public string StateText
        {
            get
            {
                if (Running) return "running";
                string s = Schedule ?? "by hand";
                return HasRun ? s + " · last " + (LastOk ? "worked" : "failed") + " " + TimeText.Ago(LastUtc) : s + " · never run";
            }
        }
    }

    public sealed class VdHost
    {
        public string Id = "", Name = "", Address = "", Via = "", Problem = "", Helper = "";
        public bool Online, Seen;
        public double Cpu;
        public long MemUsed, MemTotal, DiskUsed, DiskTotal;
        public List<VdStack> Stacks = new List<VdStack>();
    }

    /// <summary>One look at vDeploy (/api/v1/overview).</summary>
    public sealed class VdSnapshot
    {
        public bool Ok, Checked;
        public string Error = "", Version = "";
        public List<VdHost> Hosts = new List<VdHost>();
        public List<VdAutomation> Automations = new List<VdAutomation>();
        public int Updates;
        public long LastEvent;
        public DateTime AtUtc;

        public IEnumerable<VdStack> Stacks { get { return Hosts.SelectMany(h => h.Stacks).Where(s => !s.Other); } }
        public int Running { get { return Hosts.Sum(h => h.Stacks.Sum(s => s.Running)); } }
        public int Total { get { return Hosts.Sum(h => h.Stacks.Sum(s => s.Total)); } }
        public int Problems { get { return Stacks.Count(s => s.Problem) + Hosts.Count(h => h.Seen && !h.Online && h.Stacks.Count == 0); } }
    }

    /// <summary>Something vDeploy did or saw (/api/v1/events).</summary>
    public sealed class VdEvent
    {
        public long Id;
        public string Kind = "", Title = "", Detail = "", Group = "", Link = "", Host = "", Stack = "";
        public bool Ok = true, ToVShell;
        public DateTime AtUtc;
    }

    /// <summary>vDeploy's API with the app key from Settings > Plugins > vDeploy.</summary>
    public static class VdClient
    {
        static HttpClient Http { get { return PluginHttp.Client("vdeploy", false, 20); } }

        public static string BaseUrl(VdSettings c)
        {
            string u = (c.Url ?? "").Trim().TrimEnd('/');
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            return u;
        }

        static HttpRequestMessage Req(HttpMethod m, VdSettings c, string path)
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
                if ((int)res.StatusCode == 401) throw new InvalidOperationException("vDeploy didn't take the key - make one in vDeploy's Options > Sign-in > Apps");
                if ((int)res.StatusCode == 302 || body.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That address answered with a page, not vDeploy's API - is it vDeploy (port 8460)?");
                if (!res.IsSuccessStatusCode)
                {
                    string why = "";
                    try { using (var d = JsonDocument.Parse(body)) why = JsonValue.Str(d.RootElement, "message"); } catch { }
                    throw new InvalidOperationException(why.Length > 0 ? why : "vDeploy said " + (int)res.StatusCode);
                }
                return body;
            }
        }

        public static async Task<VdSnapshot> OverviewAsync(VdSettings c, CancellationToken ct)
        {
            string json = await Send(Req(HttpMethod.Get, c, "/api/v1/overview"), ct).ConfigureAwait(false);
            return ParseOverview(json);
        }

        public static VdSnapshot ParseOverview(string json)
        {
            var snap = new VdSnapshot { Ok = true, Checked = true, AtUtc = DateTime.UtcNow };
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement r = d.RootElement;
                snap.Version = JsonValue.Str(r, "version");
                snap.Updates = (int)JsonValue.Num(r, "updates");
                snap.LastEvent = (long)JsonValue.Num(r, "lastEvent");
                if (r.TryGetProperty("hosts", out JsonElement hosts) && hosts.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement h in hosts.EnumerateArray())
                    {
                        var host = new VdHost
                        {
                            Id = JsonValue.Str(h, "id"), Name = JsonValue.Str(h, "name"), Address = JsonValue.Str(h, "address"), Via = JsonValue.Str(h, "via"),
                            Problem = JsonValue.Str(h, "problem"), Helper = JsonValue.Str(h, "helper"),
                            Online = JsonValue.Bool(h, "online", false), Seen = JsonValue.Bool(h, "seen", false), Cpu = JsonValue.Num(h, "cpu"),
                            MemUsed = (long)JsonValue.Num(h, "memUsed"), MemTotal = (long)JsonValue.Num(h, "memTotal"),
                            DiskUsed = (long)JsonValue.Num(h, "diskUsed"), DiskTotal = (long)JsonValue.Num(h, "diskTotal"),
                        };
                        if (h.TryGetProperty("stacks", out JsonElement stacks) && stacks.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement s in stacks.EnumerateArray())
                            {
                                var st = new VdStack
                                {
                                    HostId = host.Id, Host = host.Name, HostOnline = host.Online || !host.Seen, Name = JsonValue.Str(s, "name"), Title = JsonValue.Str(s, "title"),
                                    Look = JsonValue.Str(s, "look"), Other = JsonValue.Bool(s, "other", false), Auto = JsonValue.Bool(s, "auto", false),
                                    Drift = JsonValue.Bool(s, "drift", false), Running = (int)JsonValue.Num(s, "running"), Total = (int)JsonValue.Num(s, "total"),
                                };
                                string dep = JsonValue.Str(s, "deploying");
                                st.Deploying = dep.Length > 0 ? dep : null;
                                if (st.Title.Length == 0) st.Title = st.Name;
                                if (s.TryGetProperty("updates", out JsonElement up) && up.ValueKind == JsonValueKind.Array)
                                    foreach (JsonElement u in up.EnumerateArray()) if (u.ValueKind == JsonValueKind.String) st.Updates.Add(u.GetString());
                                if (s.TryGetProperty("containers", out JsonElement cs) && cs.ValueKind == JsonValueKind.Array)
                                    foreach (JsonElement x in cs.EnumerateArray())
                                        st.Containers.Add(new VdContainer
                                        {
                                            Name = JsonValue.Str(x, "name"), Service = JsonValue.Str(x, "service"), Image = JsonValue.Str(x, "image"),
                                            State = JsonValue.Str(x, "state"), Status = JsonValue.Str(x, "status"),
                                            Cpu = x.TryGetProperty("cpu", out JsonElement cpu) && cpu.ValueKind == JsonValueKind.Number ? cpu.GetDouble() : (double?)null,
                                            Memory = x.TryGetProperty("memory", out JsonElement mem) && mem.ValueKind == JsonValueKind.Number ? mem.GetInt64() : (long?)null,
                                        });
                                host.Stacks.Add(st);
                            }
                        snap.Hosts.Add(host);
                    }
            }
            return snap;
        }

        /// <summary>Events after an id (none: the newest 50).</summary>
        public static async Task<List<VdEvent>> EventsAsync(VdSettings c, long? after, CancellationToken ct)
        {
            string json = await Send(Req(HttpMethod.Get, c, "/api/v1/events" + (after != null ? "?after=" + after.Value.ToString(CultureInfo.InvariantCulture) : "")), ct).ConfigureAwait(false);
            return ParseEvents(json);
        }

        public static List<VdEvent> ParseEvents(string json)
        {
            var list = new List<VdEvent>();
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("events", out JsonElement ev) || ev.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement e in ev.EnumerateArray())
                {
                    var x = new VdEvent
                    {
                        Id = (long)JsonValue.Num(e, "id"), Kind = JsonValue.Str(e, "kind"), Title = JsonValue.Str(e, "title"), Detail = JsonValue.Str(e, "detail"),
                        Group = JsonValue.Str(e, "group"), Link = JsonValue.Str(e, "link"), Host = JsonValue.Str(e, "host"), Stack = JsonValue.Str(e, "stack"),
                        Ok = JsonValue.Bool(e, "ok", true), ToVShell = JsonValue.Bool(e, "toVShell", false),
                    };
                    if (DateTimeOffset.TryParse(JsonValue.Str(e, "at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at)) x.AtUtc = at.UtcDateTime;
                    list.Add(x);
                }
            }
            return list;
        }

        /// <summary>redeploy (also an update: the newest images), start, stop, restart. null = vDeploy took it, else why not.</summary>
        public static async Task<string> ActAsync(VdSettings c, VdStack s, string action)
        {
            try
            {
                string json = await Send(Req(HttpMethod.Post, c, "/api/v1/stacks/" + Uri.EscapeDataString(s.HostId) + "/" + Uri.EscapeDataString(s.Name) + "/" + action), CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex) { return Plain(ex); }
        }

        /// <summary>start, stop or restart one container of a stack (vDeploy 0.1.9). null = done, else why not.</summary>
        public static async Task<string> ContainerActAsync(VdSettings c, VdStack s, VdContainer box, string action)
        {
            try
            {
                await Send(Req(HttpMethod.Post, c, "/api/v1/stacks/" + Uri.EscapeDataString(s.HostId) + "/" + Uri.EscapeDataString(s.Name) + "/" + action +
                    "?container=" + Uri.EscapeDataString(box.Label)), CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex) { return Plain(ex); }
        }

        /// <summary>The end of a container's log (box = null: the whole stack's, every service), with Docker's times (vDeploy 0.1.9).</summary>
        public static async Task<string> LogAsync(VdSettings c, VdStack s, VdContainer box, int lines, CancellationToken ct)
        {
            string path = "/api/v1/stacks/" + Uri.EscapeDataString(s.HostId) + "/" + Uri.EscapeDataString(s.Name) + "/logs?lines=" + lines.ToString(CultureInfo.InvariantCulture) +
                          (box != null ? "&container=" + Uri.EscapeDataString(box.Label) : "");
            string json;
            try { json = await Send(Req(HttpMethod.Get, c, path), ct).ConfigureAwait(false); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("page, not")) { throw new InvalidOperationException("This vDeploy is too old for logs here - update it to 0.1.9 or newer (Options > Updates)"); }
            using (JsonDocument d = JsonDocument.Parse(json)) return JsonValue.Str(d.RootElement, "text");
        }

        /// <summary>vDeploy's automations (an older vDeploy without them: none).</summary>
        public static async Task<List<VdAutomation>> AutomationsAsync(VdSettings c, CancellationToken ct)
        {
            string json;
            try { json = await Send(Req(HttpMethod.Get, c, "/api/v1/automations"), ct).ConfigureAwait(false); }
            catch (InvalidOperationException) { return new List<VdAutomation>(); }     // an older vDeploy: its not-found page
            return ParseAutomations(json);
        }

        public static List<VdAutomation> ParseAutomations(string json)
        {
            var list = new List<VdAutomation>();
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("automations", out JsonElement all) || all.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement a in all.EnumerateArray())
                {
                    var x = new VdAutomation
                    {
                        Id = JsonValue.Str(a, "id"), Name = JsonValue.Str(a, "name"), Steps = (int)JsonValue.Num(a, "steps"),
                        Running = JsonValue.Bool(a, "running", false),
                    };
                    string sch = JsonValue.Str(a, "schedule");
                    x.Schedule = sch.Length > 0 ? sch : null;
                    if (a.TryGetProperty("last", out JsonElement last) && last.ValueKind == JsonValueKind.Object)
                    {
                        x.HasRun = true;
                        x.LastOk = JsonValue.Bool(last, "ok", false);
                        x.LastBy = JsonValue.Str(last, "by");
                        if (DateTimeOffset.TryParse(JsonValue.Str(last, "started"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at)) x.LastUtc = at.UtcDateTime;
                    }
                    list.Add(x);
                }
            }
            return list;
        }

        /// <summary>Start an automation. null = vDeploy started it, else why not.</summary>
        public static async Task<string> RunAutomationAsync(VdSettings c, VdAutomation a)
        {
            try { await Send(Req(HttpMethod.Post, c, "/api/v1/automations/" + Uri.EscapeDataString(a.Id) + "/run"), CancellationToken.None).ConfigureAwait(false); return null; }
            catch (Exception ex) { return Plain(ex); }
        }

        public static async Task<string> CheckUpdatesAsync(VdSettings c)
        {
            try { await Send(Req(HttpMethod.Post, c, "/api/v1/updates/check"), CancellationToken.None).ConfigureAwait(false); return null; }
            catch (Exception ex) { return Plain(ex); }
        }

        /// <summary>vShell's colours, for vDeploy's pages (the names are vDeploy's WebTheme fields).</summary>
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

        public static async Task<string> SendThemeAsync(VdSettings c, string json)
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
            if (ex is TaskCanceledException) return "vDeploy didn't answer in time";
            if (ex is HttpRequestException h) return "Couldn't reach vDeploy" + (h.InnerException != null ? ": " + h.InnerException.Message : ": " + h.Message);
            if (ex is JsonException) return "vDeploy's answer wasn't what was expected - is it vDeploy 0.1.5 or newer?";
            return ex.Message;
        }
    }
}
