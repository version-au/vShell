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

namespace VPatch
{
    /// <summary>A host as vPatch shows it (/api/v1/overview).</summary>
    public sealed class VpHost
    {
        public string Id = "", Name = "", Address = "", Group = "", Os = "", Kind = "", Problem = "";
        public bool Checked, Restart, Checking, Upgrading;
        public int Updates, Security;
        public DateTime CheckedUtc;

        public bool Down { get { return Problem.Length > 0 && Updates == 0; } }
        public bool Proxmox { get { return Kind == "proxmox-ve" || Kind == "proxmox-backup"; } }

        /// <summary>"12 updates · 3 security", "up to date", "not answering", "not checked yet", "upgrading…".</summary>
        public string StateText
        {
            get
            {
                if (Upgrading) return "upgrading…";
                if (Checking) return "checking…";
                if (!Checked) return "not checked yet";
                if (Down) return "not answering";
                if (Updates == 0) return Restart ? "up to date · restart needed" : "up to date";
                return Updates + (Updates == 1 ? " update" : " updates") + (Security > 0 ? " · " + Security + " security" : "") + (Restart ? " · restart needed" : "");
            }
        }
    }

    /// <summary>One look at vPatch.</summary>
    public sealed class VpSnapshot
    {
        public bool Ok, Checked;
        public string Error = "", Version = "";
        public List<VpHost> Hosts = new List<VpHost>();
        public int Updates, Security;
        public long LastEvent;
        public DateTime AtUtc;

        public int Waiting { get { return Hosts.Count(h => h.Updates > 0); } }
        public int Restarts { get { return Hosts.Count(h => h.Restart); } }
        public int Down { get { return Hosts.Count(h => h.Down); } }
        /// <summary>The groups in vPatch's upgrade order, with their hosts.</summary>
        public List<KeyValuePair<string, List<VpHost>>> Groups
        {
            get
            {
                var l = new List<KeyValuePair<string, List<VpHost>>>();
                foreach (VpHost h in Hosts)
                {
                    int i = l.FindIndex(g => g.Key == h.Group);
                    if (i < 0) l.Add(new KeyValuePair<string, List<VpHost>>(h.Group, new List<VpHost> { h }));
                    else l[i].Value.Add(h);
                }
                return l;
            }
        }
    }

    /// <summary>Something vPatch did or saw (/api/v1/events).</summary>
    public sealed class VpEvent
    {
        public long Id;
        public string Kind = "", Title = "", Detail = "", Group = "", Link = "", Host = "", Look = "";
        public bool Ok = true, ToVShell;
    }

    /// <summary>vPatch's API with the app key from Settings > Plugins > vPatch.</summary>
    public static class VpClient
    {
        static HttpClient Http { get { return PluginHttp.Client("vpatch", false, 20); } }

        public static string BaseUrl(VpSettings c)
        {
            string u = (c.Url ?? "").Trim().TrimEnd('/');
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            return u;
        }

        static HttpRequestMessage Req(HttpMethod m, VpSettings c, string path)
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
                if ((int)res.StatusCode == 401) throw new InvalidOperationException("vPatch didn't take the key - make one in vPatch's Options > Sign-in > Apps (it starts vp_)");
                if ((int)res.StatusCode == 302 || body.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That address answered with a page, not vPatch's API - is it vPatch (port 8470)?");
                if (!res.IsSuccessStatusCode)
                {
                    string why = "";
                    try { using (var d = JsonDocument.Parse(body)) why = JsonValue.Str(d.RootElement, "message"); } catch { }
                    throw new InvalidOperationException(why.Length > 0 ? why : "vPatch said " + (int)res.StatusCode);
                }
                return body;
            }
        }

        public static async Task<VpSnapshot> OverviewAsync(VpSettings c, CancellationToken ct)
        {
            return ParseOverview(await Send(Req(HttpMethod.Get, c, "/api/v1/overview"), ct).ConfigureAwait(false));
        }

        public static VpSnapshot ParseOverview(string json)
        {
            var snap = new VpSnapshot { Ok = true, Checked = true, AtUtc = DateTime.UtcNow };
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement r = d.RootElement;
                snap.Version = JsonValue.Str(r, "version");
                snap.Updates = (int)JsonValue.Num(r, "updates");
                snap.Security = (int)JsonValue.Num(r, "security");
                snap.LastEvent = (long)JsonValue.Num(r, "lastEvent");
                if (r.TryGetProperty("hosts", out JsonElement hosts) && hosts.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement h in hosts.EnumerateArray())
                    {
                        var x = new VpHost
                        {
                            Id = JsonValue.Str(h, "id"), Name = JsonValue.Str(h, "name"), Address = JsonValue.Str(h, "address"), Group = JsonValue.Str(h, "group"),
                            Os = JsonValue.Str(h, "os"), Kind = JsonValue.Str(h, "kind"), Problem = JsonValue.Str(h, "problem"),
                            Updates = (int)JsonValue.Num(h, "updates"), Security = (int)JsonValue.Num(h, "security"),
                            Restart = JsonValue.Bool(h, "restart", false), Checking = JsonValue.Bool(h, "checking", false), Upgrading = JsonValue.Bool(h, "upgrading", false),
                        };
                        string at = JsonValue.Str(h, "checked");
                        if (at.Length > 0 && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset when)) { x.Checked = true; x.CheckedUtc = when.UtcDateTime; }
                        if (x.Group.Length == 0) x.Group = "Servers";
                        snap.Hosts.Add(x);
                    }
            }
            return snap;
        }

        public static async Task<List<VpEvent>> EventsAsync(VpSettings c, long after, CancellationToken ct)
        {
            return ParseEvents(await Send(Req(HttpMethod.Get, c, "/api/v1/events?after=" + after.ToString(CultureInfo.InvariantCulture)), ct).ConfigureAwait(false));
        }

        public static List<VpEvent> ParseEvents(string json)
        {
            var list = new List<VpEvent>();
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("events", out JsonElement ev) || ev.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement e in ev.EnumerateArray())
                    list.Add(new VpEvent
                    {
                        Id = (long)JsonValue.Num(e, "id"), Kind = JsonValue.Str(e, "kind"), Title = JsonValue.Str(e, "title"), Detail = JsonValue.Str(e, "detail"),
                        Group = JsonValue.Str(e, "group"), Link = JsonValue.Str(e, "link"), Host = JsonValue.Str(e, "host"), Look = JsonValue.Str(e, "look"),
                        Ok = JsonValue.Bool(e, "ok", true), ToVShell = JsonValue.Bool(e, "toVShell", false),
                    });
            }
            return list;
        }

        /// <summary>Check every host (hostId null) or one. null = vPatch is on it, else why not.</summary>
        public static async Task<string> CheckAsync(VpSettings c, string hostId)
        {
            try
            {
                await Send(Req(HttpMethod.Post, c, hostId == null ? "/api/v1/check" : "/api/v1/hosts/" + Uri.EscapeDataString(hostId) + "/check"), CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex) { return Plain(ex); }
        }

        /// <summary>vShell's colours, for vPatch's pages (vSuite.Web's WebTheme fields - the same as vDeploy's).</summary>
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

        public static async Task<string> SendThemeAsync(VpSettings c, string json)
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
            if (ex is TaskCanceledException) return "vPatch didn't answer in time";
            if (ex is HttpRequestException h) return "Couldn't reach vPatch" + (h.InnerException != null ? ": " + h.InnerException.Message : ": " + h.Message);
            if (ex is JsonException) return "vPatch's answer wasn't what was expected - is it vPatch 0.1.3 or newer?";
            return ex.Message;
        }
    }
}
