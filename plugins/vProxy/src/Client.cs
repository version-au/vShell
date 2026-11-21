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

namespace VProxy
{
    /// <summary>A host as vProxy shows it (/api/v1/overview).</summary>
    public sealed class VxHost
    {
        public string Id = "", Name = "", Upstream = "", Why = "", From = "", Lost = "";
        public List<string> Names = new List<string>();
        public bool On = true, Checked, Answering;
        public int Ms;
        public DateTime SinceUtc;

        public bool Down { get { return On && Checked && !Answering; } }

        /// <summary>"answering · 9 ms", "not answering", "switched off", "checking…".</summary>
        public string StateText
        {
            get
            {
                if (!On) return "switched off";
                if (!Checked) return "checking…";
                if (!Answering) return "not answering" + (Why.Length > 0 ? " - " + Why : "");
                return "answering · " + Ms + " ms";
            }
        }
    }

    /// <summary>One of vProxy's certificates.</summary>
    public sealed class VxCert
    {
        public string Domain = "", Provider = "", Problem = "";
        public bool Has, Staging;
        public DateTime NotAfterUtc;
        public int DaysLeft { get { return (int)Math.Floor((NotAfterUtc - DateTime.UtcNow).TotalDays); } }
    }

    /// <summary>One look at vProxy.</summary>
    public sealed class VxSnapshot
    {
        public bool Ok, Checked;
        public string Error = "", Version = "", CaddyProblem = "";
        public bool CaddyAnswering = true;
        public List<VxHost> Hosts = new List<VxHost>();
        public List<VxCert> Certs = new List<VxCert>();
        public long LastEvent;
        public DateTime AtUtc;

        public int Down { get { return Hosts.Count(h => h.Down); } }
        public int On { get { return Hosts.Count(h => h.On); } }
        public int Lost { get { return Hosts.Count(h => h.Lost.Length > 0); } }
        /// <summary>The certificate that ends first (null: none yet).</summary>
        public VxCert Soonest { get { return Certs.Where(c => c.Has).OrderBy(c => c.NotAfterUtc).FirstOrDefault(); } }
        /// <summary>Something wants a look: a certificate ending within 14 days, a test certificate, one not got, or a host that lost its vDeploy service.</summary>
        public bool Warn { get { return Lost > 0 || Certs.Any(c => !c.Has || c.Staging || c.DaysLeft < 14); } }
    }

    /// <summary>Something vProxy did or saw (/api/v1/events).</summary>
    public sealed class VxEvent
    {
        public long Id;
        public string Kind = "", Title = "", Detail = "", Group = "", Link = "", Host = "", Look = "";
        public bool ToVShell;
    }

    /// <summary>vProxy's API with the app key from Settings > Plugins > vProxy.</summary>
    public static class VxClient
    {
        static HttpClient Http { get { return PluginHttp.Client("vproxy", false, 20); } }

        public static string BaseUrl(VxSettings c)
        {
            string u = (c.Url ?? "").Trim().TrimEnd('/');
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            return u;
        }

        static HttpRequestMessage Req(HttpMethod m, VxSettings c, string path)
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
                if ((int)res.StatusCode == 401) throw new InvalidOperationException("vProxy didn't take the key - make one in vProxy's Options > Sign-in > Apps (it starts vx_)");
                if ((int)res.StatusCode == 302 || body.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That address answered with a page, not vProxy's API - is it vProxy's own address (port 8450)?");
                if (!res.IsSuccessStatusCode)
                {
                    string why = "";
                    try { using (var d = JsonDocument.Parse(body)) why = JsonValue.Str(d.RootElement, "message"); } catch { }
                    throw new InvalidOperationException(why.Length > 0 ? why : "vProxy said " + (int)res.StatusCode);
                }
                return body;
            }
        }

        static DateTime When(JsonElement e, string name)
        {
            string s = JsonValue.Str(e, name);
            return s.Length > 0 && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset t) ? t.UtcDateTime : DateTime.MinValue;
        }

        public static async Task<VxSnapshot> OverviewAsync(VxSettings c, CancellationToken ct)
        {
            return ParseOverview(await Send(Req(HttpMethod.Get, c, "/api/v1/overview"), ct).ConfigureAwait(false));
        }

        public static VxSnapshot ParseOverview(string json)
        {
            var snap = new VxSnapshot { Ok = true, Checked = true, AtUtc = DateTime.UtcNow };
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement r = d.RootElement;
                snap.Version = JsonValue.Str(r, "version");
                snap.LastEvent = (long)JsonValue.Num(r, "lastEvent");
                if (r.TryGetProperty("caddy", out JsonElement caddy) && caddy.ValueKind == JsonValueKind.Object)
                {
                    snap.CaddyProblem = JsonValue.Str(caddy, "problem");
                    snap.CaddyAnswering = JsonValue.Bool(caddy, "answering", true);
                }
                if (r.TryGetProperty("certificates", out JsonElement certs) && certs.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement x in certs.EnumerateArray())
                    {
                        DateTime end = When(x, "notAfter");
                        snap.Certs.Add(new VxCert
                        {
                            Domain = JsonValue.Str(x, "domain"), Provider = JsonValue.Str(x, "provider"), Problem = JsonValue.Str(x, "problem"),
                            Has = end > DateTime.MinValue, NotAfterUtc = end, Staging = JsonValue.Bool(x, "staging", false),
                        });
                    }
                if (r.TryGetProperty("sites", out JsonElement sites) && sites.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement h in sites.EnumerateArray())
                    {
                        var x = new VxHost
                        {
                            Id = JsonValue.Str(h, "id"), Upstream = JsonValue.Str(h, "upstream"), Why = JsonValue.Str(h, "why"),
                            From = JsonValue.Str(h, "from"), Lost = JsonValue.Str(h, "lost"), On = JsonValue.Bool(h, "on", true), Ms = (int)JsonValue.Num(h, "ms"),
                            SinceUtc = When(h, "since"),
                        };
                        if (h.TryGetProperty("names", out JsonElement names) && names.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement n in names.EnumerateArray()) if (n.ValueKind == JsonValueKind.String) x.Names.Add(n.GetString());
                        x.Name = x.Names.FirstOrDefault() ?? x.Id;
                        if (h.TryGetProperty("answering", out JsonElement a) && (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False)) { x.Checked = true; x.Answering = a.GetBoolean(); }
                        snap.Hosts.Add(x);
                    }
            }
            return snap;
        }

        public static async Task<List<VxEvent>> EventsAsync(VxSettings c, long after, CancellationToken ct)
        {
            return ParseEvents(await Send(Req(HttpMethod.Get, c, "/api/v1/events?after=" + after.ToString(CultureInfo.InvariantCulture)), ct).ConfigureAwait(false));
        }

        public static List<VxEvent> ParseEvents(string json)
        {
            var list = new List<VxEvent>();
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("events", out JsonElement ev) || ev.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement e in ev.EnumerateArray())
                    list.Add(new VxEvent
                    {
                        Id = (long)JsonValue.Num(e, "id"), Kind = JsonValue.Str(e, "kind"), Title = JsonValue.Str(e, "title"), Detail = JsonValue.Str(e, "detail"),
                        Group = JsonValue.Str(e, "group"), Link = JsonValue.Str(e, "link"), Host = JsonValue.Str(e, "host"), Look = JsonValue.Str(e, "look"),
                        ToVShell = JsonValue.Bool(e, "toVShell", false),
                    });
            }
            return list;
        }

        /// <summary>Check every host's app now. null = done, else why not.</summary>
        public static async Task<string> CheckAsync(VxSettings c)
        {
            try { await Send(Req(HttpMethod.Post, c, "/api/v1/check"), CancellationToken.None).ConfigureAwait(false); return null; }
            catch (Exception ex) { return Plain(ex); }
        }

        /// <summary>vShell's colours, for vProxy's pages (vSuite.Web's WebTheme fields - the same as vDeploy's and vPatch's).</summary>
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

        public static async Task<string> SendThemeAsync(VxSettings c, string json)
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
            if (ex is TaskCanceledException) return "vProxy didn't answer in time";
            if (ex is HttpRequestException h) return "Couldn't reach vProxy" + (h.InnerException != null ? ": " + h.InnerException.Message : ": " + h.Message);
            if (ex is JsonException) return "vProxy's answer wasn't what was expected";
            return ex.Message;
        }
    }
}
