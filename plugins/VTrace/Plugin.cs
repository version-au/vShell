using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("vtrace", "vTrace",
    Description = "vTrace's changes and outages: a bar module (an outage, or how many changes today) and a widget with the latest changes. Needs the vTrace app.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "", Types = "VTraceBar,VTraceList")]

namespace VShellVTrace
{
    /// <summary>
    /// The vTrace plugin - made only with vShell's plugin API: it shows what the vTrace app found (its changes.json), so it needs vTrace
    /// installed and run once. Click either module to open vTrace.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        static string Needs(BarSettings s) { return TraceData.There ? null : "Needs the vTrace app (Settings > Updates > vSuite apps), run once"; }

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = "VTraceBar", Display = "vTrace", ShortName = "vTrace", Glyph = "", Group = "Servers & network",
                Blurb = "an outage, or changes today", HasPopup = true, InWidget = false, Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new TraceBarModule(mgr, h, cfg); },
                Settings = BarRows, BarPicture = BarPic
            });
            host.AddModule(new PluginModule
            {
                Type = "VTraceList", Display = "vTrace changes", ShortName = "Changes", Glyph = "", Group = "Servers & network",
                Blurb = "the latest changes and outages", OnBar = false, Needs = Needs, WidgetSize = new Size(330, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new TraceListModule(mgr, h, cfg); },
                Settings = ListRows, WidgetPicture = WidgetPic
            });
            host.AddIntegration("vTrace", SettingsSection);
            host.OnSettings(delegate (BarSettings s) { TraceData.AutoMinutes = AutoOf(s); });
            TraceData.AutoMinutes = AutoOf(host.Manager.Settings);
            // the auto check: once a minute, is a check due? (vTrace not open, the last look older than the setting)
            _auto = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
            _auto.Tick += delegate { TraceData.AutoCheck(); };
            _auto.Start();
        }

        DispatcherTimer _auto;

        public void Unload() { if (_auto != null) { _auto.Stop(); _auto = null; } }

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
            p.Header("While vTrace isn't open");
            string[] items = Array.ConvertAll(AutoChoices, AutoText);
            p.Combo("Check for changes", items, delegate { return AutoText(AutoOf(p.Settings)); }, delegate (string v)
            {
                int i = Array.IndexOf(items, v);
                p.Settings.Integration("vtrace").Set("AutoMinutes", i < 0 ? 0 : AutoChoices[i]);
                p.Changed();
            });
            p.Note("vTrace runs in the background with no window, checks every source once and closes - the same as the refresh button on the vTrace widget and bar module. While vTrace is open it checks by itself (its Options set how often), so this waits.");
        }

        static void BarRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TraceView v = TraceView.Of(cfg);
            p.Check("Hide it when nothing is down and nothing changed today", delegate { return cfg.HideIfNone; }, delegate (bool b) { cfg.HideIfNone = b; p.Changed(); });
            p.Header("In its popup");
            p.Number("Changes to show", delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            p.Check("Only issues (outages and changes that failed)", delegate { return v.IssuesOnly; }, delegate (bool b) { v.IssuesOnly = b; p.Changed(); });
            p.Note("Click it for the latest changes (All / Issues at the top switches too); Open vTrace there shows them all. An outage still going shows in red on the bar. While vTrace isn't open, the refresh button (on the bar and in the popup) has it check once in the background, then close.");
        }

        static void ListRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TraceView v = TraceView.Of(cfg);
            p.Number("Changes to show", delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            p.Check("Today's changes by the hour (a small chart)", delegate { return v.Chart; }, delegate (bool b) { v.Chart = b; p.Changed(); });
            p.Check("Only issues (outages and changes that failed)", delegate { return v.IssuesOnly; }, delegate (bool b) { v.IssuesOnly = b; p.Changed(); });
            p.Note("What vTrace has found, read from its changes file. All / Issues at its top switches between every change and only the issues. While vTrace is open it looks by itself; while it isn't, the refresh button has it check once in the background (no window), then close - and the widget says when it last checked. Click to open vTrace.");
        }

        static FrameworkElement BarPic(Theme t)
        {
            var g = ModulePics.T(t, "", t.MainAccent, 14);
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
