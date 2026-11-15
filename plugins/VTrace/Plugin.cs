using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vtrace", "vTrace",
    Description = "vTrace's changes and outages: a bar module (an outage, or how many changes today) and a widget with the latest changes - from your vTrace server (this PC's own changes go to it too), or the vTrace app on this PC.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications", Types = "VTraceBar,VTraceList")]

namespace VShellVTrace
{
    /// <summary>
    /// The vTrace plugin - made only with vShell's plugin API. With a vTrace server set up (Settings > Plugins > vTrace: its address and
    /// an app key) it shows what the server found, its notifications, sends your colours, and sends this PC's own changes (Windows' logs)
    /// to it; clicks open the change in your browser. Without one it reads the Windows vTrace app's changes.json, as before (until that
    /// app retires).
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        static string Needs(BarSettings s)
        {
            return VtSettings.Of(s).Configured || TraceData.There ? null : "Set up your vTrace server in Settings > Plugins > vTrace (or run the vTrace app once)";
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
                Blurb = "the latest changes and outages", OnBar = false, Needs = Needs, WidgetSize = new Size(330, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new TraceListModule(mgr, h, cfg); },
                Settings = ListRows, WidgetPicture = WidgetPic
            });
            host.AddIntegration("vTrace", SettingsSection);
            host.SetUpWhen(delegate (BarSettings s) { return VtSettings.Of(s).Configured; });
            host.AddNotifications(BuildNotifications);
            TraceServer.Start(host);
            host.OnSettings(delegate (BarSettings s) { TraceData.AutoMinutes = AutoOf(s); TraceServer.OnSettings(s); });
            TraceData.AutoMinutes = AutoOf(host.Manager.Settings);
            TraceServer.OnSettings(host.Manager.Settings);
            // the Windows app's auto check: once a minute, is a check due? (no server set up, vTrace not open, the last look older than the setting)
            _auto = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
            _auto.Tick += delegate { TraceData.AutoCheck(); };
            _auto.Start();
        }

        DispatcherTimer _auto;

        public void Unload()
        {
            if (_auto != null) { _auto.Stop(); _auto = null; }
            TraceServer.Stop();
        }

        // =================================================================
        // Settings > Plugins > vTrace
        // =================================================================
        static readonly int[] AutoChoices = { 0, 5, 10, 30, 60 };

        static string AutoText(int m) { return m <= 0 ? "Off" : "Every " + m + " minutes"; }

        static int AutoOf(BarSettings s)
        {
            int m = s.Integration("vtrace").Get("AutoMinutes", 0);
            return Array.IndexOf(AutoChoices, m) >= 0 ? m : 0;
        }

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

            p.Header("The vTrace app on this PC (no server)");
            string[] items = Array.ConvertAll(AutoChoices, AutoText);
            p.Combo("Check for changes", items, delegate { return AutoText(AutoOf(p.Settings)); }, delegate (string v)
            {
                int i = Array.IndexOf(items, v);
                p.Settings.Integration("vtrace").Set("AutoMinutes", i < 0 ? 0 : AutoChoices[i]);
                p.Changed();
            });
            p.Note("Only without a vTrace server: the Windows app runs in the background with no window, checks every source once and closes - the same as the refresh button on the vTrace widget and bar module. While the app is open it checks by itself, so this waits.");
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

        static void BarRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TraceView v = TraceView.Of(cfg);
            p.Check("Hide it when nothing is down and nothing changed today", delegate { return cfg.HideIfNone; }, delegate (bool b) { cfg.HideIfNone = b; p.Changed(); });
            p.Header("In its popup");
            p.Number("Changes to show", delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            p.Check("Only issues (outages and changes that failed)", delegate { return v.IssuesOnly; }, delegate (bool b) { v.IssuesOnly = b; p.Changed(); });
            p.Note("Click it for the latest changes (All / Issues at the top switches too); Open vTrace there shows them all - your vTrace server in the browser, or the vTrace app. An outage still going shows in red on the bar. The refresh button has vTrace look now.");
        }

        static void ListRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TraceView v = TraceView.Of(cfg);
            p.Number("Changes to show", delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            p.Check("Today's changes by the hour (a small chart)", delegate { return v.Chart; }, delegate (bool b) { v.Chart = b; p.Changed(); });
            p.Check("Only issues (outages and changes that failed)", delegate { return v.IssuesOnly; }, delegate (bool b) { v.IssuesOnly = b; p.Changed(); });
            p.Note("What vTrace has found - from your vTrace server, or the vTrace app's changes file. All / Issues at its top switches between every change and only the issues; the refresh button has vTrace look now, and the widget says when it last looked. Click a change to open it in vTrace.");
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
