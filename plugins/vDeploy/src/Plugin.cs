using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vdeploy", "vDeploy",
    Description = "Your vDeploy server's Docker stacks: what's running and what has newer images on a bar or widget, !deploy in the run box to update, restart, stop or start one, vDeploy's notifications here - and your vShell colours on vDeploy's pages.",
    Glyph = "\uE898", Group = "Servers & network", Author = "version", Adds = "!deploy in the run box, notifications, palette commands", Types = "vDeploy")]

namespace VDeploy
{
    /// <summary>Settings > Plugins > vDeploy.</summary>
    public sealed class VdSettings
    {
        readonly SettingsBag _b;
        VdSettings(SettingsBag b) { _b = b; }
        public static VdSettings Of(BarSettings s) { return new VdSettings(s.Integration(Plugin.Id)); }

        /// <summary>vTrace can read Url / Key from here (Integrations["vdeploy"]) - keep the names.</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        /// <summary>The app key (protected).</summary>
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", string.IsNullOrEmpty(value) ? "" : Secret.Protect(value.Trim())); } }
        public string Bang { get { return (_b.Get("Bang", "deploy") ?? "").Trim().TrimStart('!').ToLowerInvariant(); } set { _b.Set("Bang", (value ?? "").Trim().TrimStart('!')); } }
        public bool SendColours { get { return _b.Get("SendColours", true); } set { _b.Set("SendColours", value); } }
        public bool Notifications { get { return _b.Get("Notifications", true); } set { _b.Set("Notifications", value); } }

        public bool Configured { get { return Url.Length > 0 && Key.Length > 0; } }
    }

    public sealed class Plugin : IPlugin
    {
        public const string Id = "vdeploy";
        public const string Source = "vDeploy";
        public const string Glyph = "\uE898";
        public const string KindProblem = "vDeploy.Problem", KindUpdate = "vDeploy.Update", KindGood = "vDeploy.Good";

        /// <summary>The last look (swapped on the UI thread, never changed).</summary>
        public static VdSnapshot Latest = new VdSnapshot();
        public static Poller Poll;
        public static event Action Changed;
        static BarSettings _settings;
        static Dispatcher _ui;
        static long? _seen;               // the last event shown (null = not asked yet: start from now, don't replay)
        static string _themeSent;         // what was sent last (sent again only when it changes)
        static DateTime _themeTried = DateTime.MinValue;

        public static BarSettings Settings { get { return _settings; } }

        public void Load(PluginHost host)
        {
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Latest = new VdSnapshot();
            host.AddModule(new PluginModule
            {
                Type = "vDeploy", Display = "vDeploy", ShortName = "vDeploy", Glyph = Glyph, Group = "Info",
                Blurb = "Your Docker stacks from vDeploy: running, problems, newer images - update or restart one from here.",
                OnBar = true, InWidget = true, HasPopup = true, WidgetSize = new Size(300, 0),
                Needs = delegate (BarSettings s) { return VdSettings.Of(s).Configured ? null : "Set up vDeploy in Settings > Plugins > vDeploy"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new DeployModule(mgr, h, cfg); },
                Settings = DeployModule.BuildSettings,
                BarPicture = Pictures.Bar,
                WidgetPicture = Pictures.Widget
            });
            host.AddIntegration(Source, BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return VdSettings.Of(s).Configured; });
            host.OnSettings(OnSettings);

            host.AddNotifyKind(KindProblem, NotifyLevel.Alert);
            host.AddNotifyKind(KindUpdate, NotifyLevel.Normal);
            host.AddNotifyKind(KindGood, NotifyLevel.Normal);
            host.AddNotifySource(Source);
            host.AddNotifications(BuildNotifications);

            host.AddBang(RunBox.Kind, RunBox.Parse, RunBox.Rows, delegate (BarSettings s)
            {
                VdSettings c = VdSettings.Of(s);
                return c.Configured && c.Bang.Length > 0 ? "!" + c.Bang + " vDeploy" : null;
            });
            host.AddCommands(Source, Commands);
            host.OnPaletteOpening(delegate (BarSettings s) { if (Poll != null && DateTime.UtcNow - Poll.LastPoll > TimeSpan.FromSeconds(20)) Poll.PollNow(); });

            // every 5 seconds while a module shows it, every 30 otherwise (the notifications)
            Poll = new Poller(Source, PollOnce, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }

        public void Unload()
        {
            if (Poll != null) { Poll.Stop(); Poll.Dispose(); Poll = null; }
            Latest = new VdSnapshot();
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            if (Poll == null) return;
            VdSettings c = VdSettings.Of(s);
            if (!c.Configured) { Latest = new VdSnapshot(); _seen = null; return; }
            if (!Poll.Running) Poll.Start();
            else Poll.PollNow();
            SendTheme(false);
        }

        // ---------- looking ----------

        static async Task PollOnce()
        {
            BarSettings s = _settings;
            if (s == null) return;
            VdSettings c = VdSettings.Of(s);
            if (!c.Configured) return;
            VdSnapshot snap;
            try { snap = await VdClient.OverviewAsync(c, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { snap = new VdSnapshot { Ok = false, Checked = true, Error = VdClient.Plain(ex), AtUtc = DateTime.UtcNow }; }
            if (snap.Ok)
            {
                try { snap.Automations = await VdClient.AutomationsAsync(c, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Write("vDeploy: automations: " + VdClient.Plain(ex)); }
            }
            List<VdEvent> events = null;
            if (snap.Ok && (_seen == null || snap.LastEvent > _seen.Value))
            {
                try { events = await VdClient.EventsAsync(c, _seen ?? snap.LastEvent, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Write("vDeploy: events: " + VdClient.Plain(ex)); }
            }
            Publish(snap, events);
            if (snap.Ok && _themeSent == null) SendTheme(false);
        }

        static void Publish(VdSnapshot snap, List<VdEvent> events)
        {
            if (_ui == null || _ui.CheckAccess()) Apply(snap, events);
            else _ui.BeginInvoke(new Action(delegate { Apply(snap, events); }));
        }

        static void Apply(VdSnapshot snap, List<VdEvent> events)
        {
            Latest = snap;
            if (snap.Ok)
            {
                bool first = _seen == null;
                long upTo = Math.Max(_seen ?? 0, snap.LastEvent);
                if (events != null)
                {
                    foreach (VdEvent e in events.OrderBy(x => x.Id))
                    {
                        if (!first && e.ToVShell) Tell(e);
                        upTo = Math.Max(upTo, e.Id);
                    }
                }
                _seen = upTo;
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

        /// <summary>Do something to a stack; looks again soon after so the change shows. null = vDeploy took it.</summary>
        public static async Task<string> ActAsync(VdStack s, string action)
        {
            if (_settings == null) return "Set up vDeploy first";
            string err = await VdClient.ActAsync(VdSettings.Of(_settings), s, action);
            _lastManual = DateTime.MinValue;
            RefreshNow();
            return err;
        }

        /// <summary>Start an automation; looks again soon after. null = vDeploy started it.</summary>
        public static async Task<string> RunAutomationAsync(VdAutomation a)
        {
            if (_settings == null) return "Set up vDeploy first";
            string err = await VdClient.RunAutomationAsync(VdSettings.Of(_settings), a);
            _lastManual = DateTime.MinValue;
            RefreshNow();
            return err;
        }

        /// <summary>Start, stop or restart one container; looks again soon after. null = done.</summary>
        public static async Task<string> ContainerActAsync(VdStack s, VdContainer c, string action)
        {
            if (_settings == null) return "Set up vDeploy first";
            string err = await VdClient.ContainerActAsync(VdSettings.Of(_settings), s, c, action);
            _lastManual = DateTime.MinValue;
            RefreshNow();
            return err;
        }

        public static VdHost HostOf(VdStack s) { return Latest.Hosts.FirstOrDefault(h => h.Id == s.HostId); }

        /// <summary>An SSH tab in vTerm to the stack's host (vTerm's saved connection for it, or it offers to make one - root by default).</summary>
        public static void ShellOnHost(BarManager mgr, VdStack s)
        {
            VdHost h = HostOf(s);
            string addr = h != null ? h.Address : "";
            TermOpen.Host(mgr, addr, s.Host, "root", null, addr.Length == 0 ? "vDeploy has no address for " + s.Host : null);
        }

        /// <summary>A shell inside a container (docker exec over SSH to its host, in vTerm).</summary>
        public static void ShellIn(BarManager mgr, VdStack s, VdContainer c)
        {
            VdHost h = HostOf(s);
            string addr = h != null ? h.Address : "";
            TermOpen.Host(mgr, addr, s.Host, "root", TermOpen.DockerShell(c.Name), addr.Length == 0 ? "vDeploy has no address for " + s.Host : null);
        }

        /// <summary>A container's own view in vDeploy (in its host's tab).</summary>
        public static string ContainerUrl(VdStack s, VdContainer c) { return PageUrl(s) + "?c=" + Uri.EscapeDataString(c.Label); }

        public static string PageUrl(VdStack s, string view = null)
        {
            if (_settings == null) return "";
            return VdClient.BaseUrl(VdSettings.Of(_settings)) + "/stacks/" + Uri.EscapeDataString(s.HostId) + "/" + Uri.EscapeDataString(s.Name) + (view != null ? "?view=" + view : "");
        }

        public static string HomeUrl(string path = "/") { return _settings == null ? "" : VdClient.BaseUrl(VdSettings.Of(_settings)) + path; }

        public static void Open(string url) { try { if (url.Length > 0) RunPanel.Open(url); } catch { } }

        // ---------- notifications: what vDeploy says goes to vShell (its Options > Notifications) ----------

        static void Tell(VdEvent e)
        {
            if (_settings == null || !VdSettings.Of(_settings).Notifications) return;
            string kind = !e.Ok ? KindProblem : e.Kind == "update" ? KindUpdate : KindGood;
            string url = e.Link.Length > 0 ? HomeUrl(e.Link) : HomeUrl("/activity");
            try { Notify.Post(Source, e.Title, e.Detail, url, Notify.KindOf(kind), true, null); }
            catch (Exception ex) { Log.Write("vDeploy: notify failed: " + ex.Message); }
        }

        // ---------- vShell's colours on vDeploy's pages ----------

        /// <summary>Send vShell's theme to vDeploy when it changed since the last time (or always, when asked).</summary>
        public static async void SendTheme(bool always)
        {
            BarSettings s = _settings;
            if (s == null) return;
            VdSettings c = VdSettings.Of(s);
            if (!c.Configured || !c.SendColours) return;
            string json;
            try { json = VdClient.ThemeJson(s); } catch (Exception ex) { Log.Write("vDeploy: theme: " + ex.Message); return; }
            if (!always && (json == _themeSent || DateTime.UtcNow - _themeTried < TimeSpan.FromSeconds(3))) return;
            _themeTried = DateTime.UtcNow;
            string err = await VdClient.SendThemeAsync(c, json);
            if (err == null) _themeSent = json;
            else Log.Write("vDeploy: couldn't send the colours: " + err);
        }

        // ---------- Settings > Plugins > vDeploy ----------

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            VdSettings c = VdSettings.Of(p.Settings);
            p.Header("vDeploy");
            p.Note("vDeploy runs your Docker stacks on every host, from your homelab. vShell shows them and can update, restart, stop or start them.");
            p.Text("Address", delegate { return c.Url; }, delegate (string v) { c.Url = v; }, "e.g. http://192.168.2.27:8460 - the address you open vDeploy at");
            p.Secret("App key", "app key", delegate { return c.Key; }, delegate (string v) { c.Key = v; },
                     "In vDeploy: Options > Sign-in > Apps > Make a key (it starts vd_). Kept encrypted for your Windows user.");
            p.Text("Run box keyword", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "!" + (c.Bang.Length > 0 ? c.Bang : "deploy") + " - problems and newer images; !" + (c.Bang.Length > 0 ? c.Bang : "deploy") + " web - stacks named like web. Enter: Update, Restart, Stop / Start, Open in vDeploy. Empty: off");
            p.Check("Send vShell's colours to vDeploy", delegate { return c.SendColours; }, delegate (bool v) { c.SendColours = v; if (v) { _themeSent = null; SendTheme(true); } });
            p.Note("vDeploy's pages then look like your vShell theme, and follow it when you change it (vDeploy: Options > Look).");
            p.Test(delegate { }, TestAsync);
        }

        static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            if (_settings == null) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            VdSettings c = VdSettings.Of(_settings);
            if (!c.Configured) return new KeyValuePair<bool, string>(false, "Fill in the address and key first.");
            try
            {
                VdSnapshot s = await VdClient.OverviewAsync(c, CancellationToken.None);
                Publish(s, null);
                _themeSent = null;
                SendTheme(true);
                return new KeyValuePair<bool, string>(true, "vDeploy " + s.Version + " - " + s.Hosts.Count + " host" + (s.Hosts.Count == 1 ? "" : "s") + ", " +
                    s.Stacks.Count() + " stacks" + (s.Updates > 0 ? ", " + s.Updates + " with newer images" : ""));
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, VdClient.Plain(ex)); }
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            VdSettings c = VdSettings.Of(p.Settings);
            p.NotifySection(Source, c.Configured, "vDeploy");
            p.Check("Show vDeploy's notifications", delegate { return c.Notifications; }, delegate (bool v) { c.Notifications = v; });
            p.Note("Which ones come here is chosen in vDeploy: Options > Notifications (vShell or Both) - failed deploys, newer images, the night's updates, hosts not answering, stacks changed outside vDeploy. They arrive within 30 seconds (5 while a vDeploy module is showing).");
            p.Kind("Problems (a deploy failed or was put back, a host isn't answering) show as", KindProblem);
            p.Kind("Newer images show as", KindUpdate);
            p.Kind("The rest (deployed, updated at night, back) show as", KindGood);
        }

        // ---------- the command palette ----------

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!VdSettings.Of(s).Configured) return;
            VdSnapshot snap = Latest;
            into.Add(new PaletteCommand
            {
                Title = "Open vDeploy", Detail = snap.Ok ? Summary(snap) : snap.Error, Glyph = Glyph, Source = Source, Words = "docker stacks deploy compose",
                Run = delegate (double n) { Open(HomeUrl()); return Task.FromResult<string>(null); }
            });
            into.Add(new PaletteCommand
            {
                Title = "Check vDeploy for newer images", Detail = snap.Updates > 0 ? snap.Updates + " stacks have newer images now" : "Asks each image's registry", Glyph = "\uE895", Source = Source,
                Words = "docker images updates pull",
                Run = async delegate (double n) { string err = await VdClient.CheckUpdatesAsync(VdSettings.Of(s)); return err == null ? PaletteCommand.Worked("vDeploy is checking") : "vDeploy: " + err; }
            });
            if (!snap.Ok) return;
            foreach (VdAutomation a0 in snap.Automations)
            {
                VdAutomation a = a0;
                into.Add(new PaletteCommand
                {
                    Title = "Run " + a.Name, Detail = Source + " automation · " + a.Steps + (a.Steps == 1 ? " step" : " steps") + " · " + a.StateText, Glyph = Glyph, Source = Source,
                    Ask = true, Words = "automation procedure run", Boost = 1,
                    Run = async delegate (double n)
                    {
                        string err = await RunAutomationAsync(a);
                        return err == null ? PaletteCommand.Worked("vDeploy is running " + a.Name) : "vDeploy: " + err;
                    }
                });
            }
            foreach (VdStack st0 in snap.Stacks)
            {
                VdStack st = st0;
                string detail = Source + " · " + st.Host + " · " + st.StateText + (st.Updates.Count > 0 ? " · newer images" : "");
                foreach (string[] act in Actions(st))
                {
                    string[] a = act;
                    into.Add(new PaletteCommand
                    {
                        Title = a[0] + " " + st.Title, Detail = detail, Glyph = a[2], Source = Source, Ask = a[1] != "open",
                        Words = st.Host + " docker compose stack" + (a[1] == "redeploy" ? " pull upgrade update" : a[1] == "restart" ? " reboot" : ""),
                        Boost = a[1] == "redeploy" && st.Updates.Count > 0 ? 2 : a[1] == "restart" ? 1 : 0,
                        Run = async delegate (double n)
                        {
                            if (a[1] == "open") { Open(PageUrl(st)); return null; }
                            string err = await ActAsync(st, a[1]);
                            return err == null ? PaletteCommand.Worked(a[3] + " " + st.Title) : "vDeploy: " + err;
                        }
                    });
                }
            }
        }

        /// <summary>A stack's actions: [label, action, glyph, "doing" words].</summary>
        public static List<string[]> Actions(VdStack s)
        {
            var l = new List<string[]>();
            if (s.Other) { l.Add(new[] { "Open in vDeploy", "open", "\uE8A7", "Opening" }); return l; }
            l.Add(new[] { s.Updates.Count > 0 ? "Update" : "Redeploy", "redeploy", s.Updates.Count > 0 ? "\uE896" : "\uE895", s.Updates.Count > 0 ? "Updating" : "Redeploying" });
            l.Add(new[] { "Restart", "restart", "\uE72C", "Restarting" });
            if (s.Running > 0) l.Add(new[] { "Stop", "stop", "\uE71A", "Stopping" });
            else l.Add(new[] { "Start", "start", "\uE768", "Starting" });
            l.Add(new[] { "Open in vDeploy", "open", "\uE8A7", "Opening" });
            return l;
        }

        public static string Summary(VdSnapshot s)
        {
            if (!s.Checked) return "Not looked at yet";
            if (!s.Ok) return s.Error;
            var bits = new List<string> { s.Running + " of " + s.Total + " running" };
            if (s.Problems > 0) bits.Add(s.Problems + (s.Problems == 1 ? " problem" : " problems"));
            if (s.Updates > 0) bits.Add(s.Updates + " with newer images");
            return string.Join(" · ", bits);
        }
    }
}
