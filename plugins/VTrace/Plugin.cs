using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vtrace", "vTrace",
    Description = "vTrace's changes, outages and Uptime monitors: a bar module (an outage, how many changes today, or all up) and a widget with the latest changes or your monitors - from your vTrace server, and this PC's own changes sent to it.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications", Types = "VTraceBar,VTraceList")]

namespace VShellVTrace
{
    /// <summary>
    /// The vTrace plugin - made only with vShell's plugin API. With a vTrace server set up (Settings > Plugins > vTrace: its address and
    /// an app key) it shows what the server found, its notifications, sends your colours, and sends this PC's own changes (Windows' logs)
    /// to it; clicks open the change in your browser. (The Windows vTrace app retired on 2026-11-15 - 2.1.0 dropped reading it.)
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        static string Needs(BarSettings s)
        {
            return VtSettings.Of(s).Configured ? null : "Set up your vTrace server in Settings > Plugins > vTrace";
        }

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = "VTraceBar", Display = "vTrace", ShortName = "vTrace", Glyph = "", Group = "Servers & network",
                Blurb = "an outage, or changes today", HasPopup = true, InWidget = false, Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new TraceBarModule(mgr, h, cfg); },
                Settings = BarRows, BarPicture = BarPic
            });
            host.AddModule(new PluginModule
            {
                Type = "VTraceList", Display = "vTrace changes", ShortName = "Changes", Glyph = "", Group = "Servers & network",
                Blurb = "the latest changes, outages or Uptime monitors", OnBar = false, Needs = Needs, WidgetSize = new Size(330, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new TraceListModule(mgr, h, cfg); },
                Settings = ListRows, WidgetPicture = WidgetPic
            });
            host.AddIntegration("vTrace", SettingsSection);
            host.SetUpWhen(delegate (BarSettings s) { return VtSettings.Of(s).Configured; });
            host.AddNotifications(BuildNotifications);
            TraceServer.Start(host);
            host.OnSettings(delegate (BarSettings s) { TraceServer.OnSettings(s); });
            TraceServer.OnSettings(host.Manager.Settings);
        }

        public void Unload() { TraceServer.Stop(); }

        // =================================================================
        // Settings > Plugins > vTrace
        // =================================================================
        static void SettingsSection(SettingsWindow.SettingsPage p)
        {
            VtSettings c = VtSettings.Of(p.Settings);
            p.Header("vTrace server");
            p.Note("vTrace keeps one timeline of what changed in your homelab - Proxmox, vDeploy, your servers, Uptime Kuma and more - from a server that never stops looking. vShell shows its changes and outages, its notifications, and sends this PC's own changes to it.");
            p.Text("Address", delegate { return c.Url; }, delegate (string v) { c.Url = v; }, "e.g. http://192.168.1.40:8480 - the address you open vTrace at");
            p.Secret("App key", "app key", delegate { return c.Key; }, delegate (string v) { c.Key = v; },
                     "In vTrace: Options > Sign-in > Apps > Make a key (it starts vt_). Kept encrypted for your Windows user.");
            p.Check("Send this PC's changes to vTrace", delegate { return c.SendPc; }, delegate (bool v) { c.SendPc = v; if (v) _ = TraceServer.SendPcAsync(true); });
            p.Note("Windows updates, programs installed or removed, drivers, starts and shut-downs - read from this PC's Windows logs every 15 minutes (the first time, the last 30 days), so they're in vTrace's timeline with everything else. Nothing else leaves this PC." + (TraceServer.PcStatus.Length > 0 ? "\n" + TraceServer.PcStatus : ""));
            p.Check("Send vShell's colours to vTrace", delegate { return c.SendColours; }, delegate (bool v) { c.SendColours = v; if (v) TraceServer.ThemeChanged(); });
            p.Test(delegate { }, TraceServer.TestAsync);
            p.Note("No vTrace server yet? Install it in a Proxmox node's shell: bash -c \"$(curl -fsSL https://vsuite.vrsion.com/vtrace/lxc.sh)\"");
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            VtSettings c = VtSettings.Of(p.Settings);
            p.NotifySection(TraceServer.Source, c.Configured, "vTrace");
            p.Check("Show vTrace's notifications", delegate { return c.Notifications; }, delegate (bool v) { c.Notifications = v; });
            p.Note("Which ones come here is chosen in vTrace: Options > Notifications (vShell or Both) - outages starting and ending (with their likely cause), changes that fail, sources it can't read. They arrive within 30 seconds (10 while a vTrace module is showing).");
            p.Kind("Something went down shows as", TraceServer.KindOutage);
            p.Kind("A change failed, a source can't be read shows as", TraceServer.KindProblem);
            p.Kind("Back up, a new vTrace shows as", TraceServer.KindBack);
        }

        static readonly string[] ShowChoices = { TraceView.ShowChanges, TraceView.ShowUptime, TraceView.ShowBoth };

        static void ShowRow(SettingsWindow.SettingsPage p, TraceView v)
        {
            p.Combo("Show", ShowChoices, delegate { return v.Show; }, delegate (string s) { v.Show = s; p.Changed(); });
            p.Note("Changes: what vTrace found changed, and outages. Uptime: only vTrace's Uptime monitors, each one's status and answer time (as the Uptime Kuma plugin shows them). Both: the monitors, then the changes. Uptime needs vTrace 0.1.14 or newer.");
        }

        static void UptimeRows(SettingsWindow.SettingsPage p, TraceView v)
        {
            p.Header("Uptime monitors");
            p.Check("Only the ones that aren't up", delegate { return v.NotUpOnly; }, delegate (bool b) { v.NotUpOnly = b; p.Changed(); });
            p.Check("Answer times", delegate { return v.AnswerTimes; }, delegate (bool b) { v.AnswerTimes = b; p.Changed(); });
            p.Check("How much of the last day each was up", delegate { return v.DayPercent; }, delegate (bool b) { v.DayPercent = b; p.Changed(); });
            p.Check("The last checks (a small strip)", delegate { return v.Beats; }, delegate (bool b) { v.Beats = b; p.Changed(); });
            p.Note("All / Not up at the top of the list switches too. Click a monitor to open it in vTrace. vTrace's own notifications say when one goes down and comes back (vTrace: Options > Notifications > An outage starts or ends).");
        }

        static void BarRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TraceView v = TraceView.Of(cfg);
            ShowRow(p, v);
            p.Check("Hide it when nothing is down and nothing changed today (Uptime: while everything is up)", delegate { return cfg.HideIfNone; }, delegate (bool b) { cfg.HideIfNone = b; p.Changed(); });
            p.Header("In its popup");
            p.Number("Changes to show", delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            p.Check("Only issues (outages and changes that failed)", delegate { return v.IssuesOnly; }, delegate (bool b) { v.IssuesOnly = b; p.Changed(); });
            p.Note("Click it for the latest changes (All / Issues at the top switches too); Open vTrace there shows them all in your browser. An outage still going shows in red on the bar. The refresh button has vTrace look now. With Uptime the bar says All up, or how many are down.");
            UptimeRows(p, v);
        }

        static void ListRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TraceView v = TraceView.Of(cfg);
            ShowRow(p, v);
            p.Header("Changes");
            p.Number("Changes to show", delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            p.Check("Today's changes by the hour (a small chart)", delegate { return v.Chart; }, delegate (bool b) { v.Chart = b; p.Changed(); });
            p.Check("Only issues (outages and changes that failed)", delegate { return v.IssuesOnly; }, delegate (bool b) { v.IssuesOnly = b; p.Changed(); });
            p.Note("What your vTrace server has found. All / Issues at its top switches between every change and only the issues; the refresh button has vTrace look now, and the widget says when it last looked. Click a change to open it in vTrace.");
            UptimeRows(p, v);
        }

        static FrameworkElement BarPic(Theme t)
        {
            var g = ModulePics.T(t, "", t.MainAccent, 14);
            g.FontFamily = IconUtil.GlyphFont;
            return ModulePics.Row(6, g, ModulePics.T(t, "12 today", t.Fg, 13));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Func<Brush, string, string, UIElement> row = delegate (Brush b, string title, string when)
            {
                return ModulePics.Row(8, ModulePics.Dot(b, 8), ModulePics.T(t, title, t.Fg, 12), ModulePics.T(t, when, t.FgDim, 11));
            };
            return ModulePics.Col(4,
                ModulePics.T(t, "Changes · 12 today", t.FgDim, 11, true),
                row(StatusColors.Bad, "Jellyfin went down", "9:47 PM"),
                row(StatusColors.Good, "Deploy stack \"media\"", "9:42 PM"),
                row(t.MainAccent, "14 packages upgraded", "8:02 PM"),
                row(StatusColors.Warn, "Backup of VM 104", "7:31 PM"));
        }
    }
}
