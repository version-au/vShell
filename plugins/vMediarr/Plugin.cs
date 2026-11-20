using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vmediarr", "vMediarr",
    Description = "Your vMediarr server (movies, series and requests in one app): your vShell colours on its pages, following your theme as you change it, and palette commands to open its pages.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "palette commands, vShell's colours on vMediarr's pages")]

namespace VMediarr
{
    /// <summary>Settings > Plugins > vMediarr.</summary>
    public sealed class VmSettings
    {
        readonly SettingsBag _b;
        VmSettings(SettingsBag b) { _b = b; }
        public static VmSettings Of(BarSettings s) { return new VmSettings(s.Integration(Plugin.Id)); }

        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>The app key (protected).</summary>
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", string.IsNullOrEmpty(value) ? "" : Secret.Protect(value.Trim())); } }
        public bool SendColours { get { return _b.Get("SendColours", true); } set { _b.Set("SendColours", value); } }

        public bool Configured { get { return Url.Length > 0 && Key.Length > 0; } }
    }

    /// <summary>
    /// vMediarr (V's app replacing Seerr, Radarr and Sonarr): sends vShell's colours to its pages (PUT /api/v1/theme, vSuite.Web's WebTheme
    /// fields - as the vDeploy / vPatch plugins) whenever they change, and once vMediarr can be reached (a look every minute until then).
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        public const string Id = "vmediarr";
        public const string Source = "vMediarr";
        public const string Glyph = "";

        static BarSettings _settings;
        static Poller _poll;
        static string _themeSent, _version, _problem;
        static int _movies, _series;
        static DateTime _themeTried = DateTime.MinValue;

        static HttpClient Http { get { return PluginHttp.Client("vmediarr", false, 20); } }

        public void Load(PluginHost host)
        {
            host.AddIntegration(Source, BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return VmSettings.Of(s).Configured; });
            host.OnSettings(OnSettings);
            host.AddCommands(Source, Commands);
            // no module shows it: one look a minute (sends the colours once vMediarr answers, keeps the palette's numbers fresh)
            _poll = new Poller(Source, PollOnce, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }

        public void Unload()
        {
            if (_poll != null) { _poll.Stop(); _poll.Dispose(); _poll = null; }
            _themeSent = null;
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            if (_poll == null) return;
            if (!VmSettings.Of(s).Configured) { _version = null; return; }
            if (!_poll.Running) _poll.Start();
            SendTheme(false);       // a theme change in vShell comes here too
        }

        static async Task PollOnce()
        {
            BarSettings s = _settings;
            if (s == null || !VmSettings.Of(s).Configured) return;
            try
            {
                using var r = Req(HttpMethod.Get, VmSettings.Of(s), "/api/v1/overview");
                using JsonDocument d = JsonDocument.Parse(await Send(r).ConfigureAwait(false));
                _version = d.RootElement.TryGetProperty("version", out var v) ? v.GetString() : "?";
                _movies = d.RootElement.TryGetProperty("movies", out var m) ? m.GetInt32() : 0;
                _series = d.RootElement.TryGetProperty("series", out var se) ? se.GetInt32() : 0;
                _problem = null;
                if (_themeSent == null) SendTheme(false);
            }
            catch (Exception ex) { _problem = Plain(ex); _version = null; }
        }

        // ---------- vShell's colours on vMediarr's pages ----------

        static async void SendTheme(bool always)
        {
            BarSettings s = _settings;
            if (s == null) return;
            VmSettings c = VmSettings.Of(s);
            if (!c.Configured || !c.SendColours) return;
            string json;
            try { json = ThemeJson(s); } catch (Exception ex) { Log.Write("vMediarr: theme: " + ex.Message); return; }
            if (!always && (json == _themeSent || DateTime.UtcNow - _themeTried < TimeSpan.FromSeconds(3))) return;
            _themeTried = DateTime.UtcNow;
            try
            {
                using var r = Req(HttpMethod.Put, c, "/api/v1/theme");
                r.Content = new StringContent(json, Encoding.UTF8, "application/json");
                await Send(r).ConfigureAwait(false);
                _themeSent = json;
            }
            catch (Exception ex) { Log.Write("vMediarr: couldn't send the colours: " + Plain(ex)); }
        }

        /// <summary>vShell's colours (vSuite.Web's WebTheme fields - the same the vDeploy / vPatch plugins send).</summary>
        static string ThemeJson(BarSettings s)
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

        // ---------- vMediarr's API ----------

        static string BaseUrl(VmSettings c)
        {
            string u = (c.Url ?? "").Trim().TrimEnd('/');
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            return u;
        }

        static HttpRequestMessage Req(HttpMethod m, VmSettings c, string path)
        {
            var r = new HttpRequestMessage(m, BaseUrl(c) + path);
            string key = Secret.Unprotect(c.Key ?? "");
            if (!string.IsNullOrEmpty(key)) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return r;
        }

        static async Task<string> Send(HttpRequestMessage r)
        {
            using (HttpResponseMessage res = await Http.SendAsync(r, CancellationToken.None).ConfigureAwait(false))
            {
                string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if ((int)res.StatusCode == 401) throw new InvalidOperationException("vMediarr didn't take the key - make one in vMediarr's Options > Sign-in > Apps (it starts vm_)");
                if ((int)res.StatusCode == 302 || body.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That address answered with a page, not vMediarr's API - is it vMediarr (port 8490)?");
                if (!res.IsSuccessStatusCode) throw new InvalidOperationException("vMediarr answered " + (int)res.StatusCode);
                return body;
            }
        }

        static string Plain(Exception ex)
        {
            if (ex is TaskCanceledException) return "vMediarr didn't answer in time";
            if (ex is HttpRequestException h) return "Couldn't reach vMediarr" + (h.InnerException != null ? ": " + h.InnerException.Message : ": " + h.Message);
            if (ex is JsonException) return "vMediarr's answer wasn't what was expected";
            return ex.Message;
        }

        static string HomeUrl(string path = "/") { return _settings == null ? "" : BaseUrl(VmSettings.Of(_settings)) + path; }
        static void Open(string url) { try { if (url.Length > 0) RunPanel.Open(url); } catch { } }

        // ---------- Settings > Plugins > vMediarr ----------

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            VmSettings c = VmSettings.Of(p.Settings);
            p.Header("vMediarr");
            p.Note("vMediarr is movies, series and requests in one app - it answers as Radarr, Sonarr and Seerr, so their plugins keep working pointed at it. This plugin gives its pages your vShell colours.");
            p.Text("Address", delegate { return c.Url; }, delegate (string v) { c.Url = v; }, "e.g. http://192.168.2.41:8490 - the address you open vMediarr at");
            p.Secret("App key", "app key", delegate { return c.Key; }, delegate (string v) { c.Key = v; },
                     "In vMediarr: Options > Sign-in > Apps > Make a key (it starts vm_). Kept encrypted for your Windows user.");
            p.Check("Send vShell's colours to vMediarr", delegate { return c.SendColours; }, delegate (bool v) { c.SendColours = v; if (v) { _themeSent = null; SendTheme(true); } });
            p.Note("vMediarr's pages then look like your vShell theme, and follow it when you change it (vMediarr: Options > Look).");
            p.Test(delegate { }, TestAsync);
        }

        static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            if (_settings == null || !VmSettings.Of(_settings).Configured) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            await PollOnce();
            if (_version == null) return new KeyValuePair<bool, string>(false, _problem ?? "vMediarr didn't answer");
            _themeSent = null;
            SendTheme(true);
            return new KeyValuePair<bool, string>(true, "vMediarr " + _version + " - " + _movies.ToString("N0") + " movies, " + _series.ToString("N0") + " series");
        }

        // ---------- the command palette ----------

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!VmSettings.Of(s).Configured) return;
            string detail = _version != null ? _movies.ToString("N0") + " movies · " + _series.ToString("N0") + " series" : _problem ?? "";
            void Page(string title, string path, string words, string what)
            {
                into.Add(new PaletteCommand
                {
                    Title = title, Detail = what ?? detail, Glyph = Glyph, Source = Source, Words = words,
                    Run = delegate (double n) { Open(HomeUrl(path)); return Task.FromResult<string>(null); }
                });
            }
            Page("Open vMediarr", "/", "movies series radarr sonarr seerr media", null);
            Page("vMediarr: Movies", "/movies", "movies films radarr", null);
            Page("vMediarr: Series", "/series", "series tv shows sonarr anime", null);
            Page("vMediarr: Add a movie or series", "/add", "add search tmdb movie series", "Search TMDB and add it to the library");
            Page("vMediarr: Calendar", "/calendar", "calendar airing releases upcoming", "What's coming out, day by day");
            Page("vMediarr: Wanted", "/wanted", "wanted missing", "Out and monitored, but not on disk");
            Page("vMediarr: Requests", "/requests", "requests seerr", null);
        }
    }
}
