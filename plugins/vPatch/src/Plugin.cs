using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vpatch", "vPatch",
    Description = "Your vPatch server's Linux hosts: what's waiting to be updated (and what's security) on a bar or widget, check them from here, vPatch's notifications here - and your vShell colours on vPatch's pages.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications, palette commands", Types = "vPatch")]

namespace VPatch
{
    /// <summary>Settings > Plugins > vPatch.</summary>
    public sealed class VpSettings
    {
        readonly SettingsBag _b;
        VpSettings(SettingsBag b) { _b = b; }
        public static VpSettings Of(BarSettings s) { return new VpSettings(s.Integration(Plugin.Id)); }

        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>The app key (protected).</summary>
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", string.IsNullOrEmpty(value) ? "" : Secret.Protect(value.Trim())); } }
        public bool SendColours { get { return _b.Get("SendColours", true); } set { _b.Set("SendColours", value); } }
        public bool Notifications { get { return _b.Get("Notifications", true); } set { _b.Set("Notifications", value); } }

        public bool Configured { get { return Url.Length > 0 && Key.Length > 0; } }
    }

    public sealed class Plugin : IPlugin
    {
        public const string Id = "vpatch";
        public const string Source = "vPatch";
        public const string Glyph = "";
        public const string KindProblem = "vPatch.Problem", KindSecurity = "vPatch.Security", KindUpdates = "vPatch.Updates", KindGood = "vPatch.Good";

        /// <summary>The last look (swapped on the UI thread, never changed).</summary>
        public static VpSnapshot Latest = new VpSnapshot();
        public static Poller Poll;
        public static event Action Changed;
        static BarSettings _settings;
        static Dispatcher _ui;
        static long? _seen;               // the last event shown (null = not asked yet: start from now, don't replay)
        static string _themeSent;
        static DateTime _themeTried = DateTime.MinValue;

        public static BarSettings Settings { get { return _settings; } }

        public void Load(PluginHost host)
        {
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Latest = new VpSnapshot();
            host.AddModule(new PluginModule
            {
                Type = "vPatch", Display = "vPatch", ShortName = "vPatch", Glyph = Glyph, Group = "Info",
                Blurb = "Your Linux hosts from vPatch: updates waiting, security, restarts needed, hosts not answering - check them from here.",
                OnBar = true, InWidget = true, HasPopup = true, WidgetSize = new Size(300, 0),
                Needs = delegate (BarSettings s) { return VpSettings.Of(s).Configured ? null : "Set up vPatch in Settings > Plugins > vPatch"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new PatchModule(mgr, h, cfg); },
                Settings = PatchModule.BuildSettings,
                BarPicture = Pictures.Bar,
                WidgetPicture = Pictures.Widget
            });
            host.AddIntegration(Source, BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return VpSettings.Of(s).Configured; });
            host.OnSettings(OnSettings);

            host.AddNotifyKind(KindProblem, NotifyLevel.Alert);
            host.AddNotifyKind(KindSecurity, NotifyLevel.Alert);
            host.AddNotifyKind(KindUpdates, NotifyLevel.Normal);
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
            Latest = new VpSnapshot();
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            if (Poll == null) return;
            VpSettings c = VpSettings.Of(s);
            if (!c.Configured) { Latest = new VpSnapshot(); _seen = null; return; }
            if (!Poll.Running) Poll.Start();
            else Poll.PollNow();
            SendTheme(false);
        }

        // ---------- looking ----------

        static async Task PollOnce()
        {
            BarSettings s = _settings;
            if (s == null) return;
            VpSettings c = VpSettings.Of(s);
            if (!c.Configured) return;
            VpSnapshot snap;
            try { snap = await VpClient.OverviewAsync(c, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { snap = new VpSnapshot { Ok = false, Checked = true, Error = VpClient.Plain(ex), AtUtc = DateTime.UtcNow }; }
            List<VpEvent> events = null;
            if (snap.Ok && _seen != null && snap.LastEvent > _seen.Value)
            {
                try { events = await VpClient.EventsAsync(c, _seen.Value, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Write("vPatch: events: " + VpClient.Plain(ex)); }
            }
            Publish(snap, events);
            if (snap.Ok && _themeSent == null) SendTheme(false);
        }

        static void Publish(VpSnapshot snap, List<VpEvent> events)
        {
            if (_ui == null || _ui.CheckAccess()) Apply(snap, events);
            else _ui.BeginInvoke(new Action(delegate { Apply(snap, events); }));
        }

        static void Apply(VpSnapshot snap, List<VpEvent> events)
        {
            Latest = snap;
            if (snap.Ok)
            {
                // the first look starts from now (older events aren't replayed). After that only as far as the events that actually
                // came: a request that failed, or an answer with part of a long backlog, leaves the rest to be asked for again
                if (_seen == null || snap.LastEvent < _seen.Value) _seen = snap.LastEvent;     // (lower: the server's numbers started again)
                else if (events != null)
                {
                    long upTo = _seen.Value;
                    foreach (VpEvent e in events.OrderBy(x => x.Id))
                    {
                        if (e.Id <= upTo) continue;
                        if (e.ToVShell) Tell(e);
                        upTo = e.Id;
                    }
                    if (events.Count == 0) upTo = Math.Max(upTo, snap.LastEvent);     // nothing after it: up to date
                    _seen = upTo;
                }
            }
            Action a = Changed;
            if (a != null) a();
        }

        /// <summary>Look again now (after an action); at most every 2 seconds.</summary>
        static DateTime _lastManual = DateTime.MinValue;
        public static void RefreshNow()
        {
            if (Poll == null || DateTime.UtcNow - _lastManual < TimeSpan.FromSeconds(2)) return;
            _lastManual = DateTime.UtcNow;
            Poll.PollNow();
        }

        /// <summary>Ask vPatch to check every host (null) or one; looks again soon after. null = vPatch is on it.</summary>
        public static async Task<string> CheckAsync(string hostId)
        {
            if (_settings == null) return "Set up vPatch first";
            string err = await VpClient.CheckAsync(VpSettings.Of(_settings), hostId);
            _lastManual = DateTime.MinValue;
            RefreshNow();
            return err;
        }

        public static string HostUrl(VpHost h) { return HomeUrl("/hosts/" + Uri.EscapeDataString(h.Id)); }
        public static string HomeUrl(string path = "/") { return _settings == null ? "" : VpClient.BaseUrl(VpSettings.Of(_settings)) + path; }
        public static void Open(string url) { try { if (url.Length > 0) RunPanel.Open(url); } catch { } }

        /// <summary>An SSH tab in vTerm to the host (vTerm's saved connection for it, or it offers to make one - root by default).</summary>
        public static void ShellOn(BarManager mgr, VpHost h)
        {
            TermOpen.Host(mgr, h.Address, h.Name, "root", null, h.Address.Length == 0 ? "vPatch has no address for " + h.Name : null);
        }

        // ---------- notifications: what vPatch says goes to vShell (its Options > Notifications) ----------

        static void Tell(VpEvent e)
        {
            if (_settings == null || !VpSettings.Of(_settings).Notifications) return;
            // by vPatch's kind first (a security update is "warn", not a problem); "bad" looks are problems
            string kind = e.Group == "security" ? KindSecurity : e.Look == "bad" || e.Group == "failed" ? KindProblem : e.Group == "updates" || e.Group == "restart" ? KindUpdates : KindGood;
            string url = e.Link.Length > 0 ? HomeUrl(e.Link) : HomeUrl("/activity");
            try { Notify.Post(Source, e.Title, e.Detail, url, Notify.KindOf(kind), true, null); }
            catch (Exception ex) { Log.Write("vPatch: notify failed: " + ex.Message); }
        }

        // ---------- vShell's colours on vPatch's pages ----------

        public static async void SendTheme(bool always)
        {
            BarSettings s = _settings;
            if (s == null) return;
            VpSettings c = VpSettings.Of(s);
            if (!c.Configured || !c.SendColours) return;
            string json;
            try { json = VpClient.ThemeJson(s); } catch (Exception ex) { Log.Write("vPatch: theme: " + ex.Message); return; }
            if (!always && (json == _themeSent || DateTime.UtcNow - _themeTried < TimeSpan.FromSeconds(3))) return;
            _themeTried = DateTime.UtcNow;
            string err = await VpClient.SendThemeAsync(c, json);
            if (err == null) _themeSent = json;
            else Log.Write("vPatch: couldn't send the colours: " + err);
        }

        // ---------- Settings > Plugins > vPatch ----------

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            VpSettings c = VpSettings.Of(p.Settings);
            p.Header("vPatch");
            p.Note("vPatch keeps your Linux hosts up to date, from your homelab. vShell shows what's waiting on each host and can ask vPatch to check them; upgrading happens on vPatch's own pages, with its dry run.");
            p.Text("Address", delegate { return c.Url; }, delegate (string v) { c.Url = v; }, "e.g. http://192.168.2.30:8470 - the address you open vPatch at");
            p.Secret("App key", "app key", delegate { return c.Key; }, delegate (string v) { c.Key = v; },
                     "In vPatch: Options > Sign-in > Apps > Make a key (it starts vp_). Kept encrypted for your Windows user.");
            p.Check("Send vShell's colours to vPatch", delegate { return c.SendColours; }, delegate (bool v) { c.SendColours = v; if (v) { _themeSent = null; SendTheme(true); } });
            p.Note("vPatch's pages then look like your vShell theme, and follow it when you change it (vPatch: Options > Look).");
            p.Test(delegate { }, TestAsync);
        }

        static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            if (_settings == null) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            VpSettings c = VpSettings.Of(_settings);
            if (!c.Configured) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            try
            {
                VpSnapshot s = await VpClient.OverviewAsync(c, CancellationToken.None);
                Publish(s, null);
                _themeSent = null;
                SendTheme(true);
                return new KeyValuePair<bool, string>(true, "vPatch " + s.Version + " - " + Summary(s));
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, VpClient.Plain(ex)); }
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            VpSettings c = VpSettings.Of(p.Settings);
            p.NotifySection(Source, c.Configured, "vPatch");
            p.Check("Show vPatch's notifications", delegate { return c.Notifications; }, delegate (bool v) { c.Notifications = v; });
            p.Note("Which ones come here is chosen in vPatch: Options > Notifications (vShell or Both) - failed upgrades, security updates, hosts not answering, other updates, restarts needed. They arrive within 30 seconds (5 while a vPatch module is showing).");
            p.Kind("Problems (an upgrade failed, a host isn't answering) show as", KindProblem);
            p.Kind("Security updates show as", KindSecurity);
            p.Kind("Other updates and restarts needed show as", KindUpdates);
            p.Kind("The rest (upgraded, back, a new vPatch) show as", KindGood);
        }

        // ---------- the command palette ----------

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!VpSettings.Of(s).Configured) return;
            VpSnapshot snap = Latest;
            into.Add(new PaletteCommand
            {
                Title = "Open vPatch", Detail = snap.Ok ? Summary(snap) : snap.Error, Glyph = Glyph, Source = Source, Words = "linux updates patch apt dnf hosts",
                Run = delegate (double n) { Open(HomeUrl()); return Task.FromResult<string>(null); }
            });
            into.Add(new PaletteCommand
            {
                Title = "Check every host for updates", Detail = "vPatch asks each host what's waiting", Glyph = "", Source = Source, Words = "linux updates apt dnf refresh",
                Run = async delegate (double n) { string err = await CheckAsync(null); return err == null ? PaletteCommand.Worked("vPatch is checking every host") : "vPatch: " + err; }
            });
            if (snap.Ok && snap.Waiting > 0)
                into.Add(new PaletteCommand
                {
                    Title = "Upgrade hosts in vPatch…", Detail = snap.Waiting + (snap.Waiting == 1 ? " host has" : " hosts have") + " updates - opens vPatch's Upgrade page (the dry run first)", Glyph = Glyph, Source = Source,
                    Words = "linux upgrade patch apt dnf", Boost = 1,
                    Run = delegate (double n) { Open(HomeUrl("/upgrade")); return Task.FromResult<string>(null); }
                });
            if (!snap.Ok) return;
            foreach (VpHost h0 in snap.Hosts)
            {
                VpHost h = h0;
                into.Add(new PaletteCommand
                {
                    Title = "Check " + h.Name + " for updates", Detail = Source + " · " + h.Group + " · " + h.StateText, Glyph = "", Source = Source, Words = h.Os + " linux updates",
                    Run = async delegate (double n) { string err = await CheckAsync(h.Id); return err == null ? PaletteCommand.Worked("vPatch is checking " + h.Name) : "vPatch: " + err; }
                });
            }
        }

        public static string Summary(VpSnapshot s)
        {
            if (!s.Checked) return "Not looked at yet";
            if (!s.Ok) return s.Error;
            var bits = new List<string>();
            if (s.Updates == 0) bits.Add(s.Hosts.Count + (s.Hosts.Count == 1 ? " host" : " hosts") + ", up to date");
            else bits.Add(s.Updates + (s.Updates == 1 ? " update" : " updates") + " on " + s.Waiting + (s.Waiting == 1 ? " host" : " hosts"));
            if (s.Security > 0) bits.Add(s.Security + " security");
            if (s.Restarts > 0) bits.Add(s.Restarts + (s.Restarts == 1 ? " restart needed" : " restarts needed"));
            if (s.Down > 0) bits.Add(s.Down + " not answering");
            return string.Join(" · ", bits);
        }
    }
}
