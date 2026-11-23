using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vproxy", "vProxy",
    Description = "Your vProxy's hosts: whether each app answers, the certificates, and a host that lost its vDeploy service - on a bar or widget, vProxy's notifications here, open any host from the palette - and your vShell colours on vProxy's pages.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications, palette commands", Types = "vProxy")]

namespace VProxy
{
    /// <summary>Settings > Plugins > vProxy.</summary>
    public sealed class VxSettings
    {
        readonly SettingsBag _b;
        VxSettings(SettingsBag b) { _b = b; }
        public static VxSettings Of(BarSettings s) { return new VxSettings(s.Integration(Plugin.Id)); }

        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>The app key (protected).</summary>
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", string.IsNullOrEmpty(value) ? "" : Secret.Protect(value.Trim())); } }
        public bool SendColours { get { return _b.Get("SendColours", true); } set { _b.Set("SendColours", value); } }
        public bool Notifications { get { return _b.Get("Notifications", true); } set { _b.Set("Notifications", value); } }

        public bool Configured { get { return Url.Length > 0 && Key.Length > 0; } }
    }

    public sealed class Plugin : IPlugin
    {
        public const string Id = "vproxy";
        public const string Source = "vProxy";
        public const string Glyph = "";       // Segoe Fluent's globe
        public const string KindProblem = "vProxy.Problem", KindGood = "vProxy.Good";

        /// <summary>The last look (swapped on the UI thread, never changed).</summary>
        public static VxSnapshot Latest = new VxSnapshot();
        public static Poller Poll;
        public static event Action Changed;
        static BarSettings _settings;
        static Dispatcher _ui;
        static long? _seen;               // the last event shown (null = not asked yet: start from now, don't replay)
        static string _themeSent;
        static DateTime _themeTried = DateTime.MinValue;

        public void Load(PluginHost host)
        {
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Latest = new VxSnapshot();
            host.AddModule(new PluginModule
            {
                Type = "vProxy", Display = "vProxy", ShortName = "vProxy", Glyph = Glyph, Group = "Info",
                Blurb = "Your vProxy's hosts: whether each app answers, the certificates - open any of them from here.",
                OnBar = true, InWidget = true, HasPopup = true, WidgetSize = new Size(300, 0),
                Needs = delegate (BarSettings s) { return VxSettings.Of(s).Configured ? null : "Set up vProxy in Settings > Plugins > vProxy"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new ProxyModule(mgr, h, cfg); },
                Settings = ProxyModule.BuildSettings,
                BarPicture = Pictures.Bar,
                WidgetPicture = Pictures.Widget
            });
            host.AddIntegration(Source, BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return VxSettings.Of(s).Configured; });
            host.OnSettings(OnSettings);

            host.AddNotifyKind(KindProblem, NotifyLevel.Alert);
            host.AddNotifyKind(KindGood, NotifyLevel.Normal);
            host.AddNotifySource(Source);
            host.AddNotifications(BuildNotifications);

            host.AddCommands(Source, Commands);
            host.OnPaletteOpening(delegate (BarSettings s) { if (Poll != null && DateTime.UtcNow - Poll.LastPoll > TimeSpan.FromSeconds(20)) Poll.PollNow(); });

            // every 5 seconds while a module shows it, every 30 otherwise (the notifications)
            Poll = new Poller(Source, PollOnce, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }

        public void Unload()
        {
            if (Poll != null) { Poll.Stop(); Poll.Dispose(); Poll = null; }
            Latest = new VxSnapshot();
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            if (Poll == null) return;
            VxSettings c = VxSettings.Of(s);
            if (!c.Configured) { Latest = new VxSnapshot(); _seen = null; return; }
            if (!Poll.Running) Poll.Start();
            else Poll.PollNow();
            SendTheme(false);
        }

        // ---------- looking ----------

        static async Task PollOnce()
        {
            BarSettings s = _settings;
            if (s == null) return;
            VxSettings c = VxSettings.Of(s);
            if (!c.Configured) return;
            VxSnapshot snap;
            try { snap = await VxClient.OverviewAsync(c, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { snap = new VxSnapshot { Ok = false, Checked = true, Error = VxClient.Plain(ex), AtUtc = DateTime.UtcNow }; }
            List<VxEvent> events = null;
            if (snap.Ok && _seen != null && snap.LastEvent > _seen.Value)
            {
                try { events = await VxClient.EventsAsync(c, _seen.Value, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Write("vProxy: events: " + VxClient.Plain(ex)); }
            }
            Publish(snap, events);
            if (snap.Ok && _themeSent == null) SendTheme(false);
        }

        static void Publish(VxSnapshot snap, List<VxEvent> events)
        {
            if (_ui == null || _ui.CheckAccess()) Apply(snap, events);
            else _ui.BeginInvoke(new Action(delegate { Apply(snap, events); }));
        }

        static void Apply(VxSnapshot snap, List<VxEvent> events)
        {
            Latest = snap;
            if (snap.Ok)
            {
                // each host's names and its app's address: Uptime Kuma's monitor of radarr.example.com joins vProxy's "radarr's app isn't answering"
                foreach (VxHost h in snap.Hosts) if (h.Name.Length > 0) Notify.SameThing(Source, Names(h));
                // the first look starts from now (older events aren't replayed); after that only as far as the events that came
                if (_seen == null || snap.LastEvent < _seen.Value) _seen = snap.LastEvent;
                else if (events != null)
                {
                    long upTo = _seen.Value;
                    foreach (VxEvent e in events.OrderBy(x => x.Id))
                    {
                        if (e.Id <= upTo) continue;
                        if (e.ToVShell) Tell(e);
                        upTo = e.Id;
                    }
                    if (events.Count == 0) upTo = Math.Max(upTo, snap.LastEvent);
                    _seen = upTo;
                }
            }
            Action a = Changed;
            if (a != null) a();
        }

        static DateTime _lastManual = DateTime.MinValue;
        public static void RefreshNow()
        {
            if (Poll == null || DateTime.UtcNow - _lastManual < TimeSpan.FromSeconds(2)) return;
            _lastManual = DateTime.UtcNow;
            Poll.PollNow();
        }

        /// <summary>Ask vProxy to check every host's app now. null = done.</summary>
        public static async Task<string> CheckAsync()
        {
            if (_settings == null) return "Set up vProxy first";
            string err = await VxClient.CheckAsync(VxSettings.Of(_settings));
            _lastManual = DateTime.MinValue;
            RefreshNow();
            return err;
        }

        public static string HostPage(VxHost h) { return HomeUrl("/hosts/" + Uri.EscapeDataString(h.Id)); }
        public static string SiteUrl(VxHost h) { return "https://" + h.Name + "/"; }
        public static string HomeUrl(string path = "/") { return _settings == null ? "" : VxClient.BaseUrl(VxSettings.Of(_settings)) + path; }
        public static void Open(string url) { try { if (url.Length > 0) RunPanel.Open(url); } catch { } }

        // ---------- notifications: what vProxy says goes to vShell (its Options > Notifications) ----------

        static void Tell(VxEvent e)
        {
            if (_settings == null || !VxSettings.Of(_settings).Notifications) return;
            string kind = e.Look == "bad" || e.Look == "warn" ? KindProblem : KindGood;
            string url = e.Link.Length > 0 ? HomeUrl(e.Link) : HomeUrl("/activity");
            try
            {
                NotifyAbout about = About(e);
                if (about != null) Notify.Post(Source, e.Title, e.Detail, url, Notify.KindOf(kind), about);
                else Notify.Post(Source, e.Title, e.Detail, url, Notify.KindOf(kind), true, null);
            }
            catch (Exception ex) { Log.Write("vProxy: notify failed: " + ex.Message); }
        }

        /// <summary>A host's names, then its app's address.</summary>
        static string[] Names(VxHost h)
        {
            var l = new List<string> { h.Name };
            foreach (string n in h.Names) if (n.Length > 0 && !l.Contains(n)) l.Add(n);
            if (h.Upstream.Length > 0) l.Add(h.Upstream);
            return l.ToArray();
        }

        /// <summary>What an event is about: its host - down / back up when its app stops or starts answering.</summary>
        static NotifyAbout About(VxEvent e)
        {
            if (string.IsNullOrEmpty(e.Host)) return null;
            VxHost h = Latest.Hosts.Find(delegate (VxHost x) { return string.Equals(x.Name, e.Host, StringComparison.OrdinalIgnoreCase) || x.Names.Contains(e.Host); });
            string[] also = h != null ? Names(h) : new string[0];
            if (e.Kind == "down") return NotifyAbout.Down(e.Host, also);
            if (e.Kind == "back") return NotifyAbout.Up(e.Host, also);
            return NotifyAbout.Of(e.Host, also);
        }

        // ---------- vShell's colours on vProxy's pages ----------

        public static async void SendTheme(bool always)
        {
            BarSettings s = _settings;
            if (s == null) return;
            VxSettings c = VxSettings.Of(s);
            if (!c.Configured || !c.SendColours) return;
            string json;
            try { json = VxClient.ThemeJson(s); } catch (Exception ex) { Log.Write("vProxy: theme: " + ex.Message); return; }
            if (!always && (json == _themeSent || DateTime.UtcNow - _themeTried < TimeSpan.FromSeconds(3))) return;
            _themeTried = DateTime.UtcNow;
            string err = await VxClient.SendThemeAsync(c, json);
            if (err == null) _themeSent = json;
            else Log.Write("vProxy: couldn't send the colours: " + err);
        }

        // ---------- Settings > Plugins > vProxy ----------

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            VxSettings c = VxSettings.Of(p.Settings);
            p.Header("vProxy");
            p.Note("vProxy puts your apps behind proper addresses, from your homelab. vShell shows whether each host's app answers and the certificates, tells you when something stops, and opens any host from the palette.");
            p.Text("Address", delegate { return c.Url; }, delegate (string v) { c.Url = v; }, "e.g. http://192.168.2.90:8450 - vProxy's own address, port 8450");
            p.Secret("App key", "app key", delegate { return c.Key; }, delegate (string v) { c.Key = v; },
                     "In vProxy: Options > Sign-in > Apps > Make a key (it starts vx_). Kept encrypted for your Windows user.");
            p.Check("Send vShell's colours to vProxy", delegate { return c.SendColours; }, delegate (bool v) { c.SendColours = v; if (v) { _themeSent = null; SendTheme(true); } });
            p.Note("vProxy's pages then look like your vShell theme, and follow it when you change it (vProxy: Options > Look).");
            p.Test(delegate { }, TestAsync);
        }

        static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            if (_settings == null) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            VxSettings c = VxSettings.Of(_settings);
            if (!c.Configured) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            try
            {
                VxSnapshot s = await VxClient.OverviewAsync(c, CancellationToken.None);
                Publish(s, null);
                _themeSent = null;
                SendTheme(true);
                return new KeyValuePair<bool, string>(true, "vProxy " + s.Version + " - " + Summary(s));
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, VxClient.Plain(ex)); }
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            VxSettings c = VxSettings.Of(p.Settings);
            p.NotifySection(Source, c.Configured, "vProxy");
            p.Check("Show vProxy's notifications", delegate { return c.Notifications; }, delegate (bool v) { c.Notifications = v; });
            p.Note("Which ones come here is chosen in vProxy: Options > Notifications (vShell or Both) - an app not answering, Caddy, certificates, vDeploy's moves, changes, a new vProxy. They arrive within 30 seconds (5 while a vProxy module is showing).");
            p.Kind("Problems (an app not answering, Caddy, a certificate) show as", KindProblem);
            p.Kind("The rest (back, renewed, followed, changes) show as", KindGood);
        }

        // ---------- the command palette ----------

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!VxSettings.Of(s).Configured) return;
            VxSnapshot snap = Latest;
            into.Add(new PaletteCommand
            {
                Title = "Open vProxy", Detail = snap.Ok ? Summary(snap) : snap.Error, Glyph = Glyph, Source = Source, Words = "proxy reverse caddy nginx hosts certificate",
                Run = delegate (double n) { Open(HomeUrl()); return Task.FromResult<string>(null); }
            });
            into.Add(new PaletteCommand
            {
                Title = "Check every app behind vProxy", Detail = "vProxy asks each host's app now", Glyph = "", Source = Source, Words = "proxy apps answering refresh",
                Run = async delegate (double n) { string err = await CheckAsync(); return err == null ? PaletteCommand.Worked("vProxy checked every app") : "vProxy: " + err; }
            });
            if (!snap.Ok) return;
            foreach (VxHost h0 in snap.Hosts.Where(x => x.On))
            {
                VxHost h = h0;
                into.Add(new PaletteCommand
                {
                    Title = "Open " + h.Name, Detail = Source + " · " + h.StateText + " · → " + h.Upstream, Glyph = Glyph, Source = Source,
                    Words = string.Join(" ", h.Names) + " " + h.Id + " site web",
                    Run = delegate (double n) { Open(SiteUrl(h)); return Task.FromResult<string>(null); }
                });
            }
        }

        public static string Summary(VxSnapshot s)
        {
            if (!s.Checked) return "Not looked at yet";
            if (!s.Ok) return s.Error;
            var bits = new List<string> { s.Hosts.Count + (s.Hosts.Count == 1 ? " host" : " hosts") };
            if (s.Down > 0) bits.Add(s.Down + " not answering");
            else if (s.On > 0) bits.Add("all answering");
            if (s.CaddyProblem.Length > 0) bits.Add(s.CaddyAnswering ? "Caddy didn't take a change" : "Caddy isn't answering");
            VxCert soon = s.Soonest;
            if (soon != null) bits.Add("certificate " + soon.DaysLeft + " days" + (soon.Staging ? " (test)" : ""));
            if (s.Lost > 0) bits.Add(s.Lost + " lost in vDeploy");
            return string.Join(" · ", bits);
        }
    }
}
