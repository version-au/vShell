using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using vShell.Core;
using vShell.Ui;

[assembly: vShell.Core.VShellPlugin("ludash", "Linux Update Dashboard", Description = "Your Linux Update Dashboard with an API token: how many updates each Linux server has, check / upgrade / reboot from vShell, live output, notifications and palette commands.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "update / reboot / problem notifications, palette commands, !lu in the run box, live output", Types = "LinuxUpdates")]

namespace vShell.Lud
{
    /// <summary>
    /// The Linux Update Dashboard plugin: the module (updates per system, check / upgrade / reboot), the Plugins and Notifications
    /// sections, the notifications, the palette's commands (also !lu in the run box) and the live output window.
    /// </summary>
    public sealed class LudPlugin : IPlugin
    {
        public const string Type = "LinuxUpdates";
        public const string Source = "Linux Update Dashboard";
        /// <summary>The palette's source name (a word it's found by, so "!lu web" can be "Linux updates web").</summary>
        const string Src = "Linux updates";
        public const string Glyph = "";
        const string OutputLink = "ludash:output:";
        static BarSettings _s;
        static BarManager _mgr;
        Action<LudSnapshot> _changed;
        Action<LudJob, string, string> _done;

        /// <summary>The settings in use (null = not loaded).</summary>
        internal static BarSettings Settings { get { return _s; } }

        public void Load(PluginHost host)
        {
            _mgr = host.Manager;
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Linux Update Dashboard", ShortName = "Linux updates", Glyph = Glyph, Group = "Smart home & more",
                Blurb = "updates per server, upgrade, reboot", HasPopup = true,
                Needs = delegate (BarSettings s) { return LudClient.Configured(s) ? null : "Needs the Linux Update Dashboard connection (Plugins)"; },
                Create = delegate (BarManager m, IModuleHost h, ModuleConfig cfg) { return new LudModule(m, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = BarPic
            });
            host.SetUpWhen(LudClient.Configured);
            host.AddIntegration("Linux Update Dashboard", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(delegate (BarSettings s) { _s = s; LudClient.Configure(s); });

            host.AddNotifyKind("LUD.Security", NotifyLevel.Alert);
            host.AddNotifyKind("LUD.Updates", NotifyLevel.Normal);
            host.AddNotifyKind("LUD.Problem", NotifyLevel.Important);
            host.AddNotifyKind("LUD.Good", NotifyLevel.Normal);
            host.AddNotifyKind("LUD.Done", NotifyLevel.Normal);
            host.AddNotifySource(Source);
            host.AddCardLinks(delegate (string url)
            {
                long id;
                if (url == null || !url.StartsWith(OutputLink) || !long.TryParse(url.Substring(OutputLink.Length), out id)) return false;
                ShowOutput(id);
                return true;
            });
            host.AddCommands(Src, Commands);
            // !lu web: the palette's Linux Update Dashboard commands for "web" (check / upgrade / reboot … - Enter twice for the ones that ask)
            host.AddBang(Type, delegate (string key, string q, BarSettings s)
            {
                if (!LudClient.Configured(s) || key != LudSettings.Of(s).Bang) return null;
                return new RunAction { Kind = "Palette", Query = Src + " " + q, Display = "Linux Update Dashboard: " + (q.Length == 0 ? "every system" : q) };
            }, delegate { }, delegate (BarSettings s) { return LudClient.Configured(s) ? "!" + LudSettings.Of(s).Bang + " Linux Update Dashboard" : null; });

            _changed = delegate (LudSnapshot snap)
            {
                Check(snap);
                LudOutputWindow.Refresh();
                if (LudOutputWindow.AnyOpen) { LudSnapshot keep = LudClient.Latest; }     // keeps the quicker polling going while one is open
            };
            LudClient.Changed += _changed;
            _done = JobFinished;
            LudClient.JobDone += _done;
        }

        public void Unload()
        {
            if (_changed != null) LudClient.Changed -= _changed;
            if (_done != null) LudClient.JobDone -= _done;
            LudOutputWindow.CloseAll();
            LudClient.Stop();
            _s = null;
            _mgr = null;
        }

        // =================================================================
        // Notifications: what applies now gets a key; a key that wasn't there last time is said. Update counts are said when they go
        // up. Keys are kept in the settings, so a restart doesn't say them again; the very first look only fills them in.
        // =================================================================
        static void Check(LudSnapshot snap)
        {
            if (_s == null) return;
            LudSettings c = LudSettings.Of(_s);
            string web = LudClient.WebUrl(_s);
            int wait = c.NotifyDelay;
            var said = new HashSet<string>(c.Said.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));

            if (!snap.Ok || snap.Error.Length > 0)
            {
                // nothing new to go on: keep what was said; the dashboard itself can't be reached
                if (snap.Error.Length > 0 && c.NotifyProblems)
                {
                    string err = snap.Error;
                    DownDelay.Down("lud|reach", wait, delegate { Notify.Post(Source, "Linux Update Dashboard can't be reached", err, web, Notify.KindOf("LUD.Problem")); });
                }
                return;
            }
            if (DownDelay.Up("lud|reach") == UpResult.WasNotified && c.NotifyProblems)
                Notify.Post(Source, "Linux Update Dashboard can be reached again", LudClient.Count(snap.Systems.Count, "system"), web, Notify.KindOf("LUD.Good"));

            bool quiet = !c.Seeded;      // the first look: what's there already isn't news
            var now = new HashSet<string>();
            foreach (LudSystem x in snap.Systems)
            {
                LudSystem sys = x;
                string url = OutputLink + x.Id;
                if (x.Updates > 0)
                {
                    int before = Counted(said, "upd:" + x.Id + ":"), secBefore = Counted(said, "sec:" + x.Id + ":");
                    now.Add("upd:" + x.Id + ":" + x.Updates);
                    if (x.Security > 0) now.Add("sec:" + x.Id + ":" + x.Security);
                    if (!quiet && x.Security > secBefore && c.NotifySecurity)
                        Notify.Post(Source, LudClient.Count(x.Security, "security update") + " for " + x.Label,
                                    LudClient.Count(x.Updates, "update") + " in all" + (x.Os.Length > 0 ? " · " + x.Os : ""), url, Notify.KindOf("LUD.Security"));
                    else if (!quiet && x.Updates > before && c.NotifyUpdates)
                        Notify.Post(Source, LudClient.Count(x.Updates, "update") + " for " + x.Label,
                                    (before > 0 ? (x.Updates - before) + " new" : "Ready to install") + (x.Os.Length > 0 ? " · " + x.Os : ""), url, Notify.KindOf("LUD.Updates"));
                }
                if (x.NeedsReboot)
                {
                    string k = "reboot:" + x.Id;
                    now.Add(k);
                    if (!quiet && !said.Contains(k) && c.NotifyReboot)
                        Notify.Post(Source, x.Label + " needs a reboot", "After its updates" + (x.Os.Length > 0 ? " · " + x.Os : ""), url, Notify.KindOf("LUD.Updates"));
                }
                if (x.Lifecycle.Length > 0)
                {
                    string k = "eol:" + x.Id + ":" + x.Lifecycle;
                    now.Add(k);
                    if (!quiet && !said.Contains(k) && c.NotifyLifecycle)
                        Notify.Post(Source, x.Label + ": " + x.LifecycleText, "Time to move it to a newer release.", web, Notify.KindOf("LUD.Updates"));
                }

                string problem = x.Problem;
                string key = "lud|" + x.Id;
                if (problem.Length > 0)
                {
                    if (c.NotifyProblems)
                        DownDelay.Down(key, wait, delegate { Notify.Post(Source, sys.Label + (sys.Reachable == 0 ? " can't be reached" : " can't be checked"), problem, web, Notify.KindOf("LUD.Problem")); });
                }
                else if (DownDelay.Up(key) == UpResult.WasNotified && c.NotifyProblems)
                    Notify.Post(Source, x.Label + " can be checked again", x.UpdatesText, web, Notify.KindOf("LUD.Good"));
            }
            if (snap.AppUpdate && snap.AppLatest.Length > 0)
            {
                string k = "app:" + snap.AppLatest;
                now.Add(k);
                if (!quiet && !said.Contains(k) && c.NotifyApp)
                    Notify.Post(Source, "Linux Update Dashboard " + snap.AppLatest + " is out", "You have " + (snap.AppCurrent.Length > 0 ? snap.AppCurrent : "an older one") + ".",
                                snap.AppUrl.Length > 0 ? snap.AppUrl : "https://github.com/TheDuffman85/linux-update-dashboard/releases", Notify.KindOf("LUD.Updates"));
            }

            var list = new List<string>(now);
            list.Sort(StringComparer.Ordinal);
            string joined = string.Join("\n", list);
            if (joined == c.Said && !quiet) return;
            c.Said = joined;
            c.Seeded = true;
            if (_mgr != null) _mgr.SaveSoon();
        }

        /// <summary>The number in a said key that starts with prefix ("upd:3:" -> 12), or 0.</summary>
        static int Counted(HashSet<string> said, string prefix)
        {
            foreach (string k in said)
            {
                int n;
                if (k.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(k.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            }
            return 0;
        }

        /// <summary>A job vShell started has finished: say how it went (a failed check too; a check that worked shows in the module).</summary>
        static void JobFinished(LudJob j, string result, string said)
        {
            if (_s == null) return;
            LudSettings c = LudSettings.Of(_s);
            string url = OutputLink + j.SystemId;
            bool failed = result == "failed";
            if (j.What == "check")
            {
                if (failed) Notify.Post(Source, "Checking " + j.Name + " didn't work", said.Length > 0 ? said : "Click for the output.", url, Notify.KindOf("LUD.Problem"));
                return;
            }
            if (!c.NotifyDone && !failed) return;
            string what = j.What == "full-upgrade" ? "Full upgrade of " : j.What == "autoremove" ? "Removing unused packages on " : "Upgrading ";
            string title = result == "cancelled" ? what + j.Name + " was cancelled"
                         : failed ? what + j.Name + " failed"
                         : j.What == "autoremove" ? "Removed unused packages on " + j.Name
                         : j.Name + (j.What == "full-upgrade" ? " has had its full upgrade" : " is upgraded") + (result == "warning" ? " (with warnings)" : "");
            LudSystem x = LudClient.Latest.Find(j.SystemId);
            string detail = said.Length > 0 ? said : "Click for the output.";
            if (!failed && x != null && x.NeedsReboot) detail = "It needs a reboot. " + detail;
            Notify.Post(Source, title, detail, url, Notify.KindOf(failed ? "LUD.Problem" : "LUD.Done"));
        }

        // =================================================================
        // Actions (from the popup, the output window and the palette)
        // =================================================================
        /// <summary>The live output window of a system.</summary>
        internal static void ShowOutput(long id)
        {
            if (_s == null) return;
            LudOutputWindow.Show(Theme.From(_s, 12.5, 8), id);
        }

        /// <summary>An SSH tab to a system (its saved connection by name, else its address and user from the dashboard).</summary>
        internal static void OpenShell(LudSystem x)
        {
            if (_mgr == null || x == null) return;
            TermOpen.Host(_mgr, x.Host, x.Label, x.User.Length > 0 ? x.User : "root", null);
        }

        /// <summary>Do it, and say so in a notification when it didn't work.</summary>
        internal static async void Do(string what, long id)
        {
            string err = await Act(what, id, true);
            if (!string.IsNullOrEmpty(err))
            {
                LudSystem x = LudClient.Latest.Find(id);
                Notify.Post(Source, Verb(what) + (x != null ? " " + x.Label : "") + " didn't work", err, null, NotifyLevel.Important);
            }
        }

        static string Verb(string what)
        {
            switch (what)
            {
                case "check": return "Checking";
                case "upgrade": return "Upgrading";
                case "full-upgrade": return "The full upgrade of";
                case "autoremove": return "Removing unused packages on";
                case "reboot": return "Rebooting";
                case "cancel": return "Cancelling on";
                case "checkall": return "Checking all systems";
                case "upgradeall": return "Upgrade all";
                default: return what;
            }
        }

        /// <summary>
        /// check, upgrade, full-upgrade, autoremove, reboot, cancel (a system's id), checkall, upgradeall. ask = ask first (the palette
        /// asks itself). Null when it worked or started, "" when you said no, otherwise why not.
        /// </summary>
        internal static async Task<string> Act(string what, long id, bool ask)
        {
            BarSettings S = _s;
            if (S == null || !LudClient.Configured(S)) return "Linux Update Dashboard isn't set up (Settings > Plugins)";
            if (LudClient.ReadOnly) return "The API token is read-only - make a read/write one in Linux Update Dashboard (Settings > API tokens)";
            LudSettings c = LudSettings.Of(S);
            bool big = what != "check" && what != "cancel" && what != "checkall";
            if (big && !c.AllowActions) return "Upgrades and reboots from vShell are off (Settings > Plugins > Linux Update Dashboard)";
            LudSnapshot snap = LudClient.Latest;

            if (what == "checkall") return await LudClient.CheckAllAsync(S);
            if (what == "upgradeall")
            {
                List<LudSystem> all = LudClient.UpgradeAllList(snap);
                if (all.Count == 0) return "Nothing to upgrade";
                int n = 0;
                foreach (LudSystem x in all) n += x.Updates;
                var names = new List<string>();
                foreach (LudSystem x in all) names.Add(x.Label + " (" + x.Updates + ")");
                if (ask && Questions.YesNo(null, "Upgrade " + LudClient.Count(n, "package") + " on " + LudClient.Count(all.Count, "system") + "?\n\n" + string.Join(", ", names) +
                                           "\n\nThe dashboard runs them in its Upgrade all order.", "Upgrade all") != MessageBoxResult.Yes) return "";
                return await LudClient.UpgradeAllAsync(S, all);
            }

            LudSystem sys = snap.Find(id);
            if (sys == null) return "That system isn't on the dashboard any more";
            if (what == "cancel")
            {
                if (!sys.Busy) return "Nothing is running on " + sys.Label;
                return await LudClient.CancelAsync(S, sys);
            }
            if (sys.Busy) return sys.Label + " is busy (" + sys.OpText.TrimEnd('…') + ")";
            if (what == "reboot")
            {
                if (ask && Questions.YesNo(null, "Reboot " + sys.Label + " now?" + (sys.NeedsReboot ? "" : "\n\nThe dashboard doesn't say it needs one."), "Reboot", MessageBoxImage.Warning) != MessageBoxResult.Yes) return "";
                string err = await LudClient.RebootAsync(S, sys);
                if (err == null) Notify.Post(Source, sys.Label + " is rebooting", "The dashboard checks it again once it's back.", null, NotifyLevel.Normal);
                return err;
            }
            if (ask && what == "upgrade" && Questions.YesNo(null, "Upgrade " + LudClient.Count(sys.Updates, "package") + " on " + sys.Label + "?" +
                                                             (sys.Security > 0 ? " (" + sys.Security + " security)" : ""), "Upgrade") != MessageBoxResult.Yes) return "";
            if (ask && what == "full-upgrade" && Questions.YesNo(null, "Full upgrade of " + sys.Label + "? It can also install new packages and remove old ones " +
                                                                  "(apt full-upgrade, dnf distro-sync …).", "Full upgrade", MessageBoxImage.Warning) != MessageBoxResult.Yes) return "";
            if (ask && what == "autoremove" && Questions.YesNo(null, "Remove the packages " + sys.Label + " doesn't need any more?", "Remove unused packages") != MessageBoxResult.Yes) return "";
            string e = await LudClient.StartAsync(S, sys, what);
            if (e == null && what != "check" && c.ShowOutput) ShowOutput(sys.Id);
            return e;
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            LudSettings c = LudSettings.Of(S);
            p.Header("Linux Update Dashboard");
            p.Text("Address (for example http://192.168.1.20:3001)", delegate { return c.Url; }, delegate (string v) { c.Url = v; });
            PasswordBox pw = p.Secret("API token", "ludash_…", delegate { return c.Token; }, delegate (string v) { c.Token = v; },
                "In Linux Update Dashboard open Settings > API tokens and make one (it's shown once - copy it). A read-only token is enough to see " +
                "the updates; Check now, upgrades and reboots need a read/write one. Test connection tells you which you have. Stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Check("Allow upgrades, full upgrades, removing unused packages and reboots from vShell", delegate { return c.AllowActions; }, delegate (bool v) { c.AllowActions = v; p.Redraw(); });
            if (c.AllowActions)
                p.Check("Open the live output when vShell starts an upgrade", delegate { return c.ShowOutput; }, delegate (bool v) { c.ShowOutput = v; });
            p.Note("Off: the module and the palette only check for updates. On: they can upgrade, reboot and so on - vShell asks first each time.");
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "!" + c.Bang + " web - check, upgrade, reboot or show the output of the systems with \"web\" in their name (the ones that ask need Enter twice). " +
                   "!" + c.Bang + " on its own lists them all.");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.Token = pw.Password.Trim(); pw.Password = ""; p.Changed(); }
            }, delegate { return LudClient.TestAsync(S); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            LudSettings c = LudSettings.Of(S);
            p.NotifySection("Linux Update Dashboard notifications", LudClient.Configured(S), Source);
            p.Check("New updates for a system", delegate { return c.NotifyUpdates; }, delegate (bool v) { c.NotifyUpdates = v; p.Redraw(); });
            p.Check("New security updates", delegate { return c.NotifySecurity; }, delegate (bool v) { c.NotifySecurity = v; p.Redraw(); });
            if (c.NotifySecurity) p.Kind("Security updates show as", "LUD.Security");
            p.Check("A system needs a reboot", delegate { return c.NotifyReboot; }, delegate (bool v) { c.NotifyReboot = v; p.Redraw(); });
            p.Check("A system's distro is near or past the end of its support", delegate { return c.NotifyLifecycle; }, delegate (bool v) { c.NotifyLifecycle = v; p.Redraw(); });
            p.Check("A new version of Linux Update Dashboard is out", delegate { return c.NotifyApp; }, delegate (bool v) { c.NotifyApp = v; p.Redraw(); });
            if (c.NotifyUpdates || c.NotifyReboot || c.NotifyLifecycle || c.NotifyApp) p.Kind("Updates, reboots and new versions show as", "LUD.Updates");
            p.Note("Said when a system's count goes up (a new update), not again for the same ones. What's already there when you set it up isn't said.");
            p.Check("A system (or the dashboard) can't be reached, or its check fails", delegate { return c.NotifyProblems; }, delegate (bool v) { c.NotifyProblems = v; p.Redraw(); });
            if (c.NotifyProblems)
            {
                p.Delay(delegate { return c.NotifyDelay; }, delegate (int v) { c.NotifyDelay = v; });
                p.Kind("Problems show as", "LUD.Problem");
                p.Kind("Working again shows as", "LUD.Good");
            }
            p.Check("An upgrade started from vShell finishes", delegate { return c.NotifyDone; }, delegate (bool v) { c.NotifyDone = v; p.Redraw(); });
            if (c.NotifyDone) p.Kind("Finished upgrades show as", "LUD.Done");
            p.Note("One that fails is always said. vShell asks the dashboard every 2 minutes (every 20 seconds while a module shows it). " +
                   "Click a system's notification for its live output.");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            var v = new LudView(cfg);
            if (!LudClient.Configured(p.Settings)) p.Note("Set up the Linux Update Dashboard connection first (Plugins page in this window).");
            p.Check("Icon", delegate { return v.Icon; }, delegate (bool on) { v.Icon = on; });
            p.Check("Hide while everything's up to date", delegate { return v.HideFine; }, delegate (bool on) { v.HideFine = on; });
            p.Combo("The list shows", LudView.Shows, delegate { return v.Show; }, delegate (string s) { v.Show = s; });
            if (!p.IsWidget) p.Title(cfg, "");
            p.Note("Shows how many updates your systems have (yellow; red with security updates or a system that can't be reached), and how many " +
                   "need a reboot. Click for the list: click a system for Check now, Upgrade, Reboot, its live output and Open shell (SSH). " +
                   "Upgrades and reboots are turned on in Settings > Plugins > Linux Update Dashboard.");
        }

        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(6, ModulePics.G(0xE895, t.MainAccent, 16), ModulePics.T(t, "23 updates · 1 reboot", StatusColors.Warn, 12.5));
        }

        // =================================================================
        // The command palette (and !lu): check / upgrade / reboot / output / shell for each system
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!LudClient.Configured(s)) return;
            LudSnapshot snap = LudClient.Latest;
            if (!snap.Ok) return;
            LudSettings c = LudSettings.Of(s);
            bool write = !LudClient.ReadOnly, actions = write && c.AllowActions;
            Func<string, long, Func<double, Task<string>>> act = delegate (string what, long id)
            {
                return async delegate
                {
                    string err = await Act(what, id, false);
                    return err == null ? PaletteCommand.Worked(Verb(what) + "…") : err;
                };
            };
            foreach (LudSystem x0 in snap.Systems)
            {
                LudSystem x = x0;
                string detail = Src + " · " + (x.Busy ? x.OpText : x.UpdatesText + (x.NeedsReboot ? " · reboot needed" : ""));
                string words = x.Name + " " + x.Host + " " + x.Os + " linux server system";
                if (write && !x.Busy)
                    into.Add(new PaletteCommand { Title = "Check " + x.Label + " for updates", Detail = detail, Glyph = "", Source = Src, Words = words + " refresh look", Run = act("check", x.Id) });
                if (actions && !x.Busy && x.Updates > 0)
                    into.Add(new PaletteCommand { Title = "Upgrade " + x.Label + " (" + LudClient.Count(x.Updates, "package") + ")", Detail = detail, Glyph = "", Source = Src, Words = words + " update install apt dnf", Ask = true, Boost = 1, Run = act("upgrade", x.Id) });
                if (actions && !x.Busy && x.FullUpgrade)
                    into.Add(new PaletteCommand { Title = "Full upgrade of " + x.Label, Detail = detail, Glyph = "", Source = Src, Words = words + " dist-upgrade full-upgrade distro-sync", Ask = true, Run = act("full-upgrade", x.Id) });
                if (actions && !x.Busy)
                    into.Add(new PaletteCommand { Title = "Reboot " + x.Label, Detail = detail, Glyph = "", Source = Src, Words = words + " restart", Ask = true, Boost = x.NeedsReboot ? 1 : 0, Run = act("reboot", x.Id) });
                if (write && x.Busy)
                    into.Add(new PaletteCommand { Title = "Cancel " + x.OpText.TrimEnd('…') + " on " + x.Label, Detail = detail, Glyph = "", Source = Src, Words = words + " stop abort", Ask = true, Run = act("cancel", x.Id) });
                long id = x.Id;
                into.Add(new PaletteCommand { Title = "Show the output of " + x.Label, Detail = detail, Glyph = "", Source = Src, Words = words + " log live console", Opens = true, Run = delegate { ShowOutput(id); return Task.FromResult<string>(null); } });
                into.Add(new PaletteCommand { Title = "Open shell on " + x.Label, Detail = detail, Glyph = "", Source = Src, Words = words + " ssh terminal", Opens = true, Run = delegate { OpenShell(x); return Task.FromResult<string>(null); } });
            }
            if (write)
                into.Add(new PaletteCommand { Title = "Check all systems for updates", Detail = Src, Glyph = "", Source = Src, Words = "every refresh linux servers", Run = act("checkall", 0) });
            int n = LudClient.UpgradeAllList(snap).Count;
            if (actions && n > 0)
                into.Add(new PaletteCommand { Title = "Upgrade all (" + LudClient.Count(n, "system") + ")", Detail = Src, Glyph = "", Source = Src, Words = "every update install linux servers", Ask = true, Run = act("upgradeall", 0) });
            string web = LudClient.WebUrl(s);
            into.Add(new PaletteCommand { Title = "Open Linux Update Dashboard", Detail = Src, Glyph = "", Source = Src, Words = "web page dashboard", Opens = true, Run = delegate { RunPanel.Open(web); return Task.FromResult<string>(null); } });
        }
    }

    /// <summary>On a bar (or a widget): "23 updates · 1 reboot", yellow with updates, red with security updates or a problem; click for the list.</summary>
    public sealed class LudModule : InfoModule
    {
        PopupWindow _popup;
        ScrollViewer _scroll;
        string _popupSig;
        ContextMenu _menu;      // a system's menu while it's open (the popup isn't redrawn under it)
        int _level;     // 0 fine, 1 updates / reboots, 2 security updates or something's wrong

        public LudModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        protected override bool Clickable { get { return true; } }

        protected override void Read(Reading r, bool addSample)
        {
            var v = new LudView(Cfg);
            r.Icon = v.Icon ? LudPlugin.Glyph : "";
            BarSettings s = Mgr.Settings;
            _level = 0;
            if (!LudClient.Configured(s)) { r.Text = Prefix("") + "Linux updates: set up in Settings > Plugins"; r.Tip = "Add the Linux Update Dashboard connection on the Plugins page of Settings."; return; }
            LudSnapshot snap = LudClient.Latest;
            if (!snap.Ok) { r.Text = Prefix("") + (snap.Error.Length > 0 ? "Updates n/a" : "Updates…"); r.Tip = snap.Error; return; }

            int updates = snap.Updates, security = snap.Security, reboots = snap.Reboots, problems = snap.Problems;
            if (updates > 0 || reboots > 0) _level = 1;
            if (security > 0 || problems > 0) _level = 2;
            var parts = new List<string>();
            parts.Add(updates > 0 ? LudClient.Count(updates, "update") : "Up to date");
            if (security > 0) parts.Add(security + " security");
            if (reboots > 0) parts.Add(reboots + " reboot");
            if (problems > 0) parts.Add(LudClient.Count(problems, "problem"));
            r.Text = Prefix("") + string.Join(" · ", parts);
            r.Hide = v.HideFine && _level == 0 && snap.Error.Length == 0 && !snap.Busy;

            var tip = new List<string>();
            foreach (LudSystem x in snap.Systems)
            {
                if (x.Busy) tip.Add(x.Label + ": " + x.OpText);
                else if (x.Problem.Length > 0) tip.Add(x.Label + ": " + x.Problem);
                else if (x.Updates > 0 || x.NeedsReboot) tip.Add(x.Label + ": " + x.UpdatesText + (x.NeedsReboot ? " · reboot needed" : ""));
            }
            if (tip.Count == 0) tip.Add("All " + LudClient.Count(snap.Systems.Count, "system") + " up to date");
            if (snap.AppUpdate) tip.Add("Linux Update Dashboard " + snap.AppLatest + " is out");
            tip.Add("Asked " + TimeText.Clock(snap.Fetched.ToLocalTime()) + (snap.Error.Length > 0 ? " - last try failed: " + snap.Error : ""));
            r.Tip = string.Join("\n", tip);

            // the popup is redrawn when what it shows changed (not on every check, nor while its menu is open), keeping its scroll place
            if (_menu != null && !_menu.IsOpen) _menu = null;
            if (_popup != null && _popup.IsOpen && _menu == null && Sig(snap) != _popupSig)
            {
                double at = _scroll != null ? _scroll.VerticalOffset : 0;
                _popup.SetContent(Card(ModTheme ?? Bar.Theme));
                if (at > 0 && _scroll != null) _scroll.ScrollToVerticalOffset(at);
            }
        }

        /// <summary>What the popup shows, as text: it's redrawn only when this changes.</summary>
        string Sig(LudSnapshot snap)
        {
            var b = new StringBuilder();
            BarSettings s = Mgr.Settings;
            b.Append(snap.Ok).Append('|').Append(snap.Error).Append('|').Append(snap.AppCurrent).Append('|').Append(snap.AppUpdate ? snap.AppLatest : "")
             .Append('|').Append(LudClient.ReadOnlyState).Append('|').Append(LudSettings.Of(s).AllowActions).Append('|').Append(new LudView(Cfg).Show);
            foreach (LudSystem x in snap.Systems)
            {
                LudJob j = LudClient.JobOn(x.Id);
                b.Append('\n').Append(x.Id).Append(' ').Append(x.Label).Append(' ').Append(x.Os).Append(' ').Append(x.Updates).Append(' ').Append(x.Security)
                 .Append(' ').Append(x.KeptBack).Append(' ').Append(x.Issues).Append(' ').Append(x.NeedsReboot).Append(' ').Append(x.FullUpgrade).Append(' ').Append(x.Reachable)
                 .Append(' ').Append(x.CheckStatus).Append(' ').Append(x.CheckError).Append(' ').Append(x.Checked.Ticks).Append(' ').Append(x.OpText)
                 .Append(' ').Append(x.Lifecycle).Append(' ').Append(x.Host).Append(' ').Append(x.User).Append(' ').Append(x.Port).Append(' ').Append(TimeText.Ago(x.Checked == DateTime.MinValue ? DateTime.UtcNow : x.Checked)).Append(' ').Append(j != null ? j.What : "");
            }
            return b.ToString();
        }

        protected override void Decorate(TextBlock txt) { Colour(); }
        protected override void OnThemed(Theme t) { Colour(); }

        void Colour()
        {
            Theme t = ModTheme ?? Bar.Theme;
            Txt.Foreground = _level == 2 ? StatusColors.Bad : _level == 1 ? StatusColors.Warn : (Brush)t.Fg;
        }

        protected override void OnClick()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            Theme t = ModTheme ?? Bar.Theme;
            _popup = new PopupWindow(Mgr, Bar, this, t, Card(t));
            if (Mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(t, Mgr.Settings.WidgetStyle);
            if (_scroll != null) _scroll.MaxHeight = _popup.ContentRoom();
            Mgr.ShowPopup(_popup);
        }

        void Close() { if (_popup != null) { _popup.CloseSafe(); _popup = null; } }

        TextBlock Line(Theme t, string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontFamily = t.Font, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        TextBlock Link(Theme t, string text, Action click)
        {
            TextBlock l = Line(t, text, t.MainAccent, t.FontSize);
            l.Cursor = Cursors.Hand;
            l.Margin = new Thickness(0, 0, 18, 0);
            l.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            l.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return l;
        }

        /// <summary>The popup: every system with its updates, reboot and state; click one for what you can do with it.</summary>
        FrameworkElement Card(Theme t)
        {
            BarSettings s = Mgr.Settings;
            LudSnapshot snap = LudClient.Latest;
            LudSettings c = LudSettings.Of(s);
            bool write = !LudClient.ReadOnly, actions = write && c.AllowActions;
            bool onlyNeeding = new LudView(Cfg).OnlyNeeding;
            _popupSig = Sig(snap);
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 12), MinWidth = 320, MaxWidth = 480 };
            TextBlock head = Line(t, "Linux Update Dashboard" + (snap.AppCurrent.Length > 0 ? "  " + snap.AppCurrent : ""), t.Fg, t.FontSize + 2, true);
            head.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(head);
            if (snap.AppUpdate && snap.AppLatest.Length > 0) sp.Children.Add(Line(t, snap.AppLatest + " is out", StatusColors.Warn, t.FontSize));
            if (snap.Error.Length > 0)
            {
                TextBlock e = Line(t, snap.Error, StatusColors.Bad, t.FontSize);
                e.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(e);
            }
            if (snap.Ok)
            {
                int needing = 0;
                foreach (LudSystem x in snap.Systems) if (x.Updates > 0) needing++;
                string sum = snap.Updates > 0
                    ? LudClient.Count(snap.Updates, "update") + (snap.Security > 0 ? " (" + snap.Security + " security)" : "") + " on " + needing + " of " + LudClient.Count(snap.Systems.Count, "system")
                    : "All " + LudClient.Count(snap.Systems.Count, "system") + " up to date";
                if (snap.Reboots > 0) sum += " · " + snap.Reboots + (snap.Reboots == 1 ? " needs" : " need") + " a reboot";
                sp.Children.Add(Line(t, sum, t.FgDim, t.FontSize - 1));
            }
            if (LudClient.ReadOnly)
            {
                TextBlock ro = Line(t, "Read-only API token: Check now, upgrades and reboots need a read/write one.", t.FgDim, t.FontSize - 1.5);
                ro.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(ro);
            }

            var list = new StackPanel();
            int shown = 0;
            foreach (LudSystem x in snap.Systems)
            {
                if (onlyNeeding && !x.NeedsSomething && !x.Busy) continue;
                list.Children.Add(SystemRow(t, x, write, actions));
                shown++;
            }
            if (shown == 0)
                list.Children.Add(Line(t, !snap.Ok ? "Asking Linux Update Dashboard…" : snap.Systems.Count == 0 ? "No systems on the dashboard" : "Nothing needs doing", t.FgDim, t.FontSize));
            _scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
            if (_popup != null) _scroll.MaxHeight = _popup.ContentRoom();
            sp.Children.Add(_scroll);

            var links = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (snap.Ok && write) links.Children.Add(Link(t, "Check all", delegate { LudPlugin.Do("checkall", 0); }));
            int all = snap.Ok ? LudClient.UpgradeAllList(snap).Count : 0;
            if (actions && all > 0) links.Children.Add(Link(t, "Upgrade all (" + all + ")…", delegate { Close(); LudPlugin.Do("upgradeall", 0); }));
            links.Children.Add(Link(t, "Open Linux Update Dashboard", delegate { Close(); RunPanel.Open(LudClient.WebUrl(s)); }));
            sp.Children.Add(links);
            return sp;
        }

        FrameworkElement SystemRow(Theme t, LudSystem x, bool write, bool actions)
        {
            LudJob job = LudClient.JobOn(x.Id);
            bool busy = x.Busy || job != null;
            string problem = x.Problem;
            // the dot: accent = something's running, red = a problem or security updates, yellow = updates / a reboot / old distro, green = up to date, dim = not checked yet
            Brush dot = busy ? t.MainAccent : problem.Length > 0 || x.Security > 0 ? StatusColors.Bad
                      : x.Updates > 0 || x.NeedsReboot || x.Lifecycle.Length > 0 ? StatusColors.Warn
                      : x.Reachable == -1 && x.CheckStatus.Length == 0 ? (Brush)t.FgDim : StatusColors.Good;

            var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0) });
            var text = new StackPanel { Margin = new Thickness(0, 2, 4, 2) };
            text.Children.Add(Line(t, x.Label + (x.Os.Length > 0 ? "   " + x.Os : ""), t.Fg, t.FontSize));
            Brush detailFg = t.FgDim;
            string detail;
            if (busy) { detail = x.Busy ? x.OpText : "starting…"; detailFg = t.MainAccent; }
            else if (problem.Length > 0) { detail = problem; detailFg = StatusColors.Bad; }
            else
            {
                var d = new List<string> { x.UpdatesText };
                if (x.KeptBack > 0) d.Add(x.KeptBack + " kept back");
                if (x.NeedsReboot) d.Add("reboot needed");
                if (x.Issues > 0) d.Add(LudClient.Count(x.Issues, "package issue"));
                if (x.Lifecycle.Length > 0) d.Add(x.LifecycleText);
                if (x.Checked != DateTime.MinValue) d.Add("checked " + TimeText.Ago(x.Checked));
                detail = string.Join("  ·  ", d);
                if (x.Security > 0) detailFg = StatusColors.Bad;
                else if (x.Updates > 0 || x.NeedsReboot) detailFg = StatusColors.Warn;
            }
            text.Children.Add(Line(t, detail, detailFg, t.FontSize - 1.5));
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            g.ToolTip = x.Label + (x.Host.Length > 0 ? "\n" + (x.User.Length > 0 ? x.User + "@" : "") + x.Host + (x.Port != 22 ? ":" + x.Port : "") : "") +
                        (x.Os.Length > 0 ? "\n" + x.Os : "") + "\n" + x.UpdatesText + (x.NeedsReboot ? "\nNeeds a reboot" : "") +
                        (x.Checked != DateTime.MinValue ? "\nLast check: " + TimeText.Stamp(x.Checked.ToLocalTime()) + (x.CheckStatus.Length > 0 ? " (" + x.CheckStatus + ")" : "") : "") +
                        (x.CheckError.Length > 0 ? "\n" + x.CheckError : "") + "\nClick for what you can do";

            var menu = new ContextMenu();
            Action<string, bool, Action> item = delegate (string header, bool enabled, Action run)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += delegate { run(); };
                menu.Items.Add(mi);
            };
            long id = x.Id;
            if (write)
            {
                if (busy) item("Cancel " + (x.Busy ? x.OpText.TrimEnd('…') : "it"), x.Busy, delegate { LudPlugin.Do("cancel", id); });
                else item("Check now", true, delegate { LudPlugin.Do("check", id); });
                if (actions)
                {
                    item(x.Updates > 0 ? "Upgrade " + LudClient.Count(x.Updates, "package") + "…" : "Upgrade (nothing to upgrade)", !busy && x.Updates > 0, delegate { Close(); LudPlugin.Do("upgrade", id); });
                    if (x.FullUpgrade) item("Full upgrade…", !busy, delegate { Close(); LudPlugin.Do("full-upgrade", id); });
                    item("Remove unused packages…", !busy, delegate { Close(); LudPlugin.Do("autoremove", id); });
                    item(x.NeedsReboot ? "Reboot (needed)…" : "Reboot…", !busy, delegate { Close(); LudPlugin.Do("reboot", id); });
                }
                else item("Upgrades and reboots are off (Settings > Plugins)", false, delegate { });
                menu.Items.Add(new Separator());
            }
            item("Show the output", true, delegate { Close(); LudPlugin.ShowOutput(id); });
            item("Open shell (SSH)", x.Host.Length > 0, delegate { Close(); LudPlugin.OpenShell(x); });
            item("Open Linux Update Dashboard", true, delegate { Close(); RunPanel.Open(LudClient.WebUrl(Mgr.Settings)); });
            if (x.Host.Length > 0) item("Copy host name", true, delegate { try { Clipboard.SetText(x.Host); } catch { } });
            menu.Opened += delegate { _menu = menu; };
            menu.Closed += delegate { if (_menu == menu) _menu = null; };
            g.ContextMenu = menu;

            g.MouseEnter += delegate { g.Background = t.Hover; };
            g.MouseLeave += delegate { g.Background = Brushes.Transparent; };
            g.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; };
            g.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs ev)
            {
                ev.Handled = true;
                menu.PlacementTarget = g;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            };
            return g;
        }
    }
}
