using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vmediarr", "vMediarr",
    Description = "Your vMediarr server (movies, series and requests in one app): what's downloading, requests waiting for you and the week's episodes on a bar or widget, its notifications here, and your vShell colours on its pages.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications, palette commands, vShell's colours on vMediarr's pages", Types = "vMediarr")]

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
        public bool Notifications { get { return _b.Get("Notifications", true); } set { _b.Set("Notifications", value); } }

        public bool Configured { get { return Url.Length > 0 && Key.Length > 0; } }
    }

    /// <summary>
    /// vMediarr (V's app replacing Seerr, Radarr and Sonarr): the module (downloads, requests waiting, coming up), vMediarr's events as
    /// notifications (never replayed at start), vShell's colours on its pages (PUT /api/v1/theme - WebTheme fields, as the vDeploy / vPatch
    /// plugins), palette commands. Looks every 5 seconds while a module shows it, every 30 otherwise.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        public const string Id = "vmediarr";
        public const string Source = "vMediarr";
        public const string Glyph = "";
        public const string KindProblem = "vMediarr.Problem", KindRequest = "vMediarr.Request", KindReady = "vMediarr.Ready", KindOther = "vMediarr.Other";

        public static VmSnapshot Latest = new VmSnapshot();
        public static Poller Poll;
        static BarSettings _settings;
        static Dispatcher _ui;
        static long? _seen;
        static string _themeSent;
        static DateTime _themeTried = DateTime.MinValue;

        static HttpClient Http { get { return PluginHttp.Client("vmediarr", false, 20); } }

        public void Load(PluginHost host)
        {
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Latest = new VmSnapshot();
            host.AddModule(new PluginModule
            {
                Type = "vMediarr", Display = "vMediarr", ShortName = "vMediarr", Glyph = Glyph, Group = "Info",
                Blurb = "Movies and series from vMediarr: what's downloading, requests waiting for you, and the week's episodes.",
                OnBar = true, InWidget = true, HasPopup = true, WidgetSize = new Size(320, 0),
                Needs = delegate (BarSettings s) { return VmSettings.Of(s).Configured ? null : "Set up vMediarr in Settings > Plugins > vMediarr"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new MediaModule(mgr, h, cfg); },
                Settings = MediaModule.BuildSettings,
                BarPicture = Pictures.Bar,
                WidgetPicture = Pictures.Widget
            });
            host.AddIntegration(Source, BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return VmSettings.Of(s).Configured; });
            host.OnSettings(OnSettings);
            host.AddNotifyKind(KindProblem, NotifyLevel.Alert);
            host.AddNotifyKind(KindRequest, NotifyLevel.Normal);
            host.AddNotifyKind(KindReady, NotifyLevel.Normal);
            host.AddNotifyKind(KindOther, NotifyLevel.Normal);
            host.AddNotifySource(Source);
            host.AddNotifications(BuildNotifications);
            host.AddCommands(Source, Commands);
            Poll = new Poller(Source, PollOnce, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }

        public void Unload()
        {
            if (Poll != null) { Poll.Stop(); Poll.Dispose(); Poll = null; }
            Latest = new VmSnapshot();
            _themeSent = null;
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            if (Poll == null) return;
            if (!VmSettings.Of(s).Configured) { Latest = new VmSnapshot(); _seen = null; return; }
            if (!Poll.Running) Poll.Start();
            else Poll.PollNow();
            SendTheme(false);       // a theme change in vShell comes here too
        }

        // ---------- looking ----------

        static async Task PollOnce()
        {
            BarSettings s = _settings;
            if (s == null) return;
            VmSettings c = VmSettings.Of(s);
            if (!c.Configured) return;
            VmSnapshot snap;
            try
            {
                using (var r = Req(HttpMethod.Get, c, "/api/v1/overview")) snap = VmSnapshot.Parse(await Send(r).ConfigureAwait(false));
            }
            catch (Exception ex) { snap = new VmSnapshot { Ok = false, Checked = true, Error = Plain(ex) }; }
            List<VmEvent> events = null;
            if (snap.Ok && _seen != null && snap.LastEvent > _seen.Value)
            {
                try { using (var r = Req(HttpMethod.Get, c, "/api/v1/events?after=" + _seen.Value)) events = VmEvent.ParseList(await Send(r).ConfigureAwait(false)); }
                catch (Exception ex) { Log.Write("vMediarr: events: " + Plain(ex)); }
            }
            if (_ui == null || _ui.CheckAccess()) Apply(snap, events);
            else _ui.BeginInvoke(new Action(delegate { Apply(snap, events); }));
            if (snap.Ok && _themeSent == null) SendTheme(false);
        }

        static void Apply(VmSnapshot snap, List<VmEvent> events)
        {
            Latest = snap;
            if (!snap.Ok) return;
            // the first look starts from now (older events aren't replayed)
            if (_seen == null || snap.LastEvent < _seen.Value) { _seen = snap.LastEvent; return; }
            if (events == null) return;
            long upTo = _seen.Value;
            foreach (VmEvent e in events.OrderBy(x => x.Id))
            {
                if (e.Id <= upTo) continue;
                if (e.ToVShell) Tell(e);
                upTo = e.Id;
            }
            if (events.Count == 0) upTo = Math.Max(upTo, snap.LastEvent);
            _seen = upTo;
        }

        static DateTime _lastManual = DateTime.MinValue;
        public static void RefreshNow()
        {
            if (Poll == null || DateTime.UtcNow - _lastManual < TimeSpan.FromSeconds(2)) return;
            _lastManual = DateTime.UtcNow;
            Poll.PollNow();
        }

        static void Tell(VmEvent e)
        {
            if (_settings == null || !VmSettings.Of(_settings).Notifications) return;
            string kind = e.Kind == "failed" || e.Look == "bad" ? KindProblem : e.Kind == "requested" ? KindRequest : e.Kind is "imported" or "available" ? KindReady : KindOther;
            try { Notify.Post(Source, e.Title, e.Detail, HomeUrl(e.Kind == "requested" ? "/requests" : e.Link.Length > 0 ? e.Link : "/activity"), Notify.KindOf(kind), true, null); }
            catch (Exception ex) { Log.Write("vMediarr: notify failed: " + ex.Message); }
        }

        public static string Summary(VmSnapshot s)
        {
            if (!s.Checked) return "Not looked at yet";
            if (!s.Ok) return s.Error;
            var bits = new List<string> { s.Movies.ToString("N0") + " movies, " + s.Series.ToString("N0") + " series" };
            bits.Add(s.Downloading == 0 ? "nothing downloading" : s.Downloading + " downloading");
            if (s.RequestsWaiting > 0) bits.Add(s.RequestsWaiting + (s.RequestsWaiting == 1 ? " request" : " requests") + " waiting");
            if (s.Problems > 0) bits.Add(s.Problems + " failed");
            return string.Join(" · ", bits);
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
                using (var r = Req(HttpMethod.Put, c, "/api/v1/theme"))
                {
                    r.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    await Send(r).ConfigureAwait(false);
                }
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
            if (ex is JsonException) return "vMediarr's answer wasn't what was expected - is it vMediarr 0.0.9 or newer?";
            return ex.Message;
        }

        public static string HomeUrl(string path = "/") { return _settings == null ? "" : BaseUrl(VmSettings.Of(_settings)) + (string.IsNullOrEmpty(path) ? "/" : path); }
        public static void Open(string url) { try { if (url.Length > 0) RunPanel.Open(url); } catch { } }

        // ---------- Settings > Plugins > vMediarr ----------

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            VmSettings c = VmSettings.Of(p.Settings);
            p.Header("vMediarr");
            p.Note("vMediarr is movies, series and requests in one app - it answers as Radarr, Sonarr and Seerr, so their plugins keep working pointed at it. This plugin adds its own module and notifications, and gives its pages your vShell colours.");
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
            try
            {
                VmSnapshot s;
                using (var r = Req(HttpMethod.Get, VmSettings.Of(_settings), "/api/v1/overview")) s = VmSnapshot.Parse(await Send(r));
                Apply(s, null);
                _themeSent = null;
                SendTheme(true);
                return new KeyValuePair<bool, string>(true, "vMediarr " + s.Version + " - " + Summary(s));
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, Plain(ex)); }
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            VmSettings c = VmSettings.Of(p.Settings);
            p.NotifySection(Source, c.Configured, "vMediarr");
            p.Check("Show vMediarr's notifications", delegate { return c.Notifications; }, delegate (bool v) { c.Notifications = v; });
            p.Note("Downloads grabbed and imported, failed downloads, requests asked for (waiting for you), approved and ready to watch. They arrive within 30 seconds (5 while a vMediarr module is showing).");
            p.Kind("Problems (a download failed) show as", KindProblem);
            p.Kind("Requests waiting for you show as", KindRequest);
            p.Kind("Here / ready to watch show as", KindReady);
            p.Kind("The rest (grabbed, approved, declined) show as", KindOther);
        }

        // ---------- the command palette ----------

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!VmSettings.Of(s).Configured) return;
            string detail = Summary(Latest);
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
            Page("vMediarr: Activity", "/activity", "downloads queue activity history", null);
            Page("vMediarr: Calendar", "/calendar", "calendar airing releases upcoming", "What's coming out, day by day");
            Page("vMediarr: Wanted", "/wanted", "wanted missing", "Out and monitored, but not on disk");
            Page("vMediarr: Requests", "/requests", "requests seerr approve", Latest.RequestsWaiting > 0 ? Latest.RequestsWaiting + " waiting for you" : null);
        }
    }
}
