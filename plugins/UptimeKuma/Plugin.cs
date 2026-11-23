using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("uptimekuma", "Uptime Kuma",
    Description = "Your Uptime Kuma monitors: a bar module (all up, or how many are down), a widget with the list, and notifications when one goes down and when it's back.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications", Types = "UptimeKuma,UptimeKumaList")]

namespace UptimeKuma
{
    /// <summary>
    /// The Uptime Kuma plugin - made only with vShell's plugin API (sdk\vShell.dll): it reads Uptime Kuma's /metrics with an API key.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        Action<string, string, string, string, NotifyAbout> _notice;

        static string Needs(BarSettings s) { return KumaSettings.Of(s).Configured ? null : "Needs the Uptime Kuma connection (Plugins)"; }

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = "UptimeKuma", Display = "Uptime Kuma", ShortName = "Uptime", Glyph = "", Group = "Smart home & more",
                Blurb = "all up, or how many are down", HasPopup = true, InWidget = false, Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new KumaBarModule(mgr, h, cfg); },
                Settings = BarSettingsRows, BarPicture = BarPic
            });
            host.AddModule(new PluginModule
            {
                Type = "UptimeKumaList", Display = "Uptime Kuma monitors", ShortName = "Monitors", Glyph = "", Group = "Smart home & more",
                Blurb = "each monitor's status", OnBar = false, Needs = Needs, WidgetSize = new Size(300, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new KumaListModule(mgr, h, cfg); },
                Settings = ListSettingsRows, WidgetPicture = WidgetPic
            });

            host.SetUpWhen(delegate (BarSettings s) { return KumaSettings.Of(s).Configured; });
            host.AddIntegration("Uptime Kuma", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(Kuma.Configure);

            host.AddNotifyKind("UptimeKuma.Down", NotifyLevel.Alert);
            host.AddNotifyKind("UptimeKuma.Up", NotifyLevel.Normal);
            host.AddNotifySource("Uptime Kuma");
            // the poller runs on a worker thread: notifications go out on vShell's own
            _notice = delegate (string title, string detail, string url, string kind, NotifyAbout about)
            {
                Application app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(delegate { Notify.Post("Uptime Kuma", title, detail, url, Notify.KindOf(kind), about); }));
            };
            Kuma.Notice += _notice;
        }

        public void Unload()
        {
            if (_notice != null) Kuma.Notice -= _notice;
            Kuma.Stop();
        }

        // ---- Settings > Plugins ----
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            KumaSettings k = KumaSettings.Of(p.Settings);
            p.Header("Uptime Kuma");
            p.Text("Address (for example http://192.168.1.20:3001 or https://status.example.com)", delegate { return k.Url; }, delegate (string v) { k.Url = v; });
            PasswordBox key = p.Secret("API key", "API key", delegate { return k.KeyStored; }, delegate (string v) { k.KeyStored = v; },
                "In Uptime Kuma open Settings > API Keys > Add API Key and copy the key (it's only shown once). vShell reads Uptime Kuma's /metrics " +
                "with it - every monitor's status, response time and certificate. It's stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return k.IgnoreCert; }, delegate (bool v) { k.IgnoreCert = v; });
            p.Number("Check every (seconds, while a module shows it)", delegate { return k.Seconds; }, delegate (double v) { k.Seconds = (int)Math.Round(v); });
            p.Test(delegate
            {
                if (key.Password.Trim().Length > 0) { k.KeyStored = Secret.Protect(key.Password.Trim()); key.Password = ""; p.Changed(); }
            }, delegate { return Kuma.TestAsync(p.Settings); });
            p.Note("Without a module showing it, vShell checks once a minute for the notifications (not at all with them off).");
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            KumaSettings k = KumaSettings.Of(p.Settings);
            p.NotifySection("Uptime Kuma notifications", k.Configured, "Uptime Kuma");
            p.Check("A monitor goes down (and when it's back up)", delegate { return k.NotifyDown; }, delegate (bool v) { k.NotifyDown = v; p.Changed(); });
            p.Check("Uptime Kuma itself can't be reached (and when it can again)", delegate { return k.NotifyServer; }, delegate (bool v) { k.NotifyServer = v; p.Changed(); });
            p.Delay(delegate { return k.NotifyDelay; }, delegate (int v) { k.NotifyDelay = v; p.Changed(); });
            p.Kind("Down", "UptimeKuma.Down");
            p.Kind("Back up", "UptimeKuma.Up");
            p.Note("Monitors that are already down when vShell starts don't give a notification - only changes do. Uptime Kuma's own notifications keep working as they are.");
        }

        // ---- module settings ----
        static void BarSettingsRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            KumaView v = KumaView.Of(cfg);
            p.Check("Hide it while everything is up", delegate { return cfg.HideIfNone; }, delegate (bool b) { cfg.HideIfNone = b; p.Changed(); });
            p.Header("In its popup");
            ListRows(p, cfg, v);
        }

        static void ListSettingsRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            ListRows(p, cfg, KumaView.Of(cfg));
        }

        static void ListRows(SettingsWindow.SettingsPage p, ModuleConfig cfg, KumaView v)
        {
            p.Combo("Show", new[] { "All", "Not up" }, delegate { return v.Show; }, delegate (string s) { v.Show = s; p.Changed(); });
            p.Check("Response times", delegate { return v.ResponseTimes; }, delegate (bool b) { v.ResponseTimes = b; p.Changed(); });
            p.Check("Certificate days left", delegate { return v.CertDays; }, delegate (bool b) { v.CertDays = b; p.Changed(); });
            p.Number("Certificate warning (days)", delegate { return v.CertWarn; }, delegate (double d) { v.CertWarn = (int)Math.Round(d); p.Changed(); });
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            var g = ModulePics.T(t, "", StatusColors.Bad, 14);
            g.FontFamily = IconUtil.GlyphFont;
            return ModulePics.Row(6, g, ModulePics.T(t, "1 down", StatusColors.Bad, 13));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Func<Brush, string, string, UIElement> row = delegate (Brush b, string name, string ms)
            {
                return ModulePics.Row(8, ModulePics.Dot(b, 8), ModulePics.T(t, name, t.Fg, 12), ModulePics.T(t, ms, t.FgDim, 11));
            };
            return ModulePics.Col(4,
                ModulePics.T(t, "3 up · 1 down", t.FgDim, 11, true),
                row(StatusColors.Good, "Home Assistant", "42 ms"),
                row(StatusColors.Good, "Jellyfin", "18 ms"),
                row(StatusColors.Bad, "NAS", "down"),
                row(StatusColors.Good, "Website", "120 ms"));
        }
    }
}
