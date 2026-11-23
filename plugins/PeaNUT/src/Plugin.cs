using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("peanut", "PeaNUT",
    Description = "Your UPSes from PeaNUT (Network UPS Tools), from as many PeaNUT servers as you like: a module with their battery, " +
                  "runtime and load, and notifications when one goes on battery, runs low or a server can't be reached.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications", Types = VShellPeanut.UpsModule.TypeName)]

namespace VShellPeanut
{
    public sealed class Plugin : IPlugin
    {
        public const string Id = "peanut";
        public const string Source = "PeaNUT";

        // Integration settings (BarSettings.Integration("peanut"))
        const string KeyServers = "Servers";
        const string KeyRenames = "Renames";
        const string KeyFastSeconds = "FastSeconds";
        const string KeySlowSeconds = "SlowSeconds";
        const string KeyNotifyBattery = "NotifyOnBattery";
        const string KeyNotifyLow = "NotifyLow";
        const string KeyLowAt = "LowAt";
        const string KeyNotifyReplace = "NotifyReplace";
        const string KeyNotifyDown = "NotifyDown";
        const string KeyDelay = "Delay";

        // Notification kinds
        const string KindOnBattery = "PeaNUT.OnBattery";
        const string KindMains = "PeaNUT.Mains";
        const string KindLow = "PeaNUT.LowBattery";
        const string KindReplace = "PeaNUT.Replace";
        const string KindDown = "PeaNUT.Down";
        const string KindUp = "PeaNUT.Up";

        PluginHost _host;

        public void Load(PluginHost host)
        {
            _host = host;

            host.AddModule(new PluginModule
            {
                Type = UpsModule.TypeName,
                Display = "UPS (PeaNUT)",
                ShortName = "UPS",
                Glyph = "",
                Group = "Servers & network",
                Blurb = "Your UPSes from PeaNUT: battery, runtime, load and whether they're on mains or battery. Click for the details.",
                HasPopup = true,
                WidgetSize = new Size(260, 120),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new UpsModule(mgr, h, cfg); },
                Settings = ModuleSettings,
                Needs = delegate (BarSettings s) { return Servers(s).Count == 0 ? "Add a PeaNUT server on the Plugins page first" : null; },
                BarPicture = BarPicture,
                WidgetPicture = WidgetPicture
            });

            host.AddIntegration("PeaNUT", IntegrationPage);
            host.SetUpWhen(delegate (BarSettings s) { return Servers(s).Any(x => x.Enabled && x.BaseUrl.Length > 0); });
            host.OnSettings(Configure);

            host.AddNotifySource(Source);
            host.AddNotifyKind(KindOnBattery, NotifyLevel.Important);
            host.AddNotifyKind(KindMains, NotifyLevel.Normal);
            host.AddNotifyKind(KindLow, NotifyLevel.Alert);
            host.AddNotifyKind(KindReplace, NotifyLevel.Important);
            host.AddNotifyKind(KindDown, NotifyLevel.Alert);
            host.AddNotifyKind(KindUp, NotifyLevel.Normal);
            host.AddNotifications(NotificationsPage);
            host.AddWatcher("PeaNUT", Watch);

            host.AddCommands("PeaNUT", Commands);
            host.OnPaletteOpening(delegate (BarSettings s) { Store.PollNow(); });

            Store.Start(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
            // OnSettings runs at start too, which hands the store its servers.
        }

        public void Unload()
        {
            Store.Stop();
        }

        // ------------------------------------------------------------------ settings

        static SettingsBag Bag(BarSettings s)
        {
            return s.Integration(Id);
        }

        static List<PeanutServer> Servers(BarSettings s)
        {
            return Bag(s).Live(KeyServers, delegate { return new List<PeanutServer>(); });
        }

        static List<UpsRename> Renames(BarSettings s)
        {
            return Bag(s).Live(KeyRenames, delegate { return new List<UpsRename>(); });
        }

        void Configure(BarSettings s)
        {
            var bag = Bag(s);
            int fast = Math.Max(2, bag.Get(KeyFastSeconds, 5));
            int slow = Math.Max(5, bag.Get(KeySlowSeconds, 15));
            Store.SetTimes(TimeSpan.FromSeconds(fast), TimeSpan.FromSeconds(slow));
            Store.SetRenames(Renames(s));
            Store.SetServers(Servers(s));
        }

        void IntegrationPage(SettingsWindow.SettingsPage p)
        {
            var bag = Bag(p.Settings);
            var list = Servers(p.Settings);

            p.Header("PeaNUT");
            p.Note("PeaNUT is a web dashboard for Network UPS Tools (NUT). Add every PeaNUT you run - each one can watch " +
                   "any number of UPSes on any number of NUT servers - and they all show in the UPS (PeaNUT) module.");
            p.ListButton("PeaNUT servers… (" + list.Count + ")", delegate { EditServers(p, list); });
            var renames = Renames(p.Settings);
            p.ListButton("Rename UPSes… (" + renames.Count + " renamed)", delegate { EditRenames(p, renames); });

            p.Test(null, async delegate
            {
                var on = list.Where(x => x.Enabled && x.BaseUrl.Length > 0).Select(x => x.Copy()).ToList();
                if (on.Count == 0) return new KeyValuePair<bool, string>(false, "Add a PeaNUT server first.");
                var results = await Task.WhenAll(on.Select(x => Store.Fetch(x)));
                Store.PollNow();
                return new KeyValuePair<bool, string>(results.All(r => r.Ok), string.Join("\n", results.Select(Describe)));
            });

            p.Section("Checking");
            p.Slider("Check every (seconds) while a UPS module is showing", 2, 60,
                () => bag.Get(KeyFastSeconds, 5), v => bag.Set(KeyFastSeconds, (int)v));
            p.Slider("Check every (seconds) otherwise - for the notifications", 5, 300,
                () => bag.Get(KeySlowSeconds, 15), v => bag.Set(KeySlowSeconds, (int)v));
            p.Note("Address: PeaNUT's web address, e.g. http://192.168.1.20:8080 - with its base path if you set BASE_PATH. " +
                   "Username and password: the ones you log in to PeaNUT with (or WEB_USERNAME / WEB_PASSWORD). " +
                   "Leave them empty if PeaNUT runs with AUTH_DISABLED=true.");
        }

        static string Describe(ServerResult r)
        {
            if (!r.Ok) return r.Server.Shown + ": " + r.Error;
            int n = r.Ups.Count;
            string names = string.Join(", ", r.Ups.Select(u => u.Device));
            return r.Server.Shown + ": " + (n == 0 ? "connected, but PeaNUT has no UPS"
                : n == 1 ? "1 UPS (" + names + ")" : n + " UPSes (" + names + ")");
        }

        void EditServers(SettingsWindow.SettingsPage p, List<PeanutServer> list)
        {
            Func<Window, PeanutServer, bool> edit = delegate (Window owner, PeanutServer s) { return EditServer(owner, s); };
            var w = new ListEditorWindow<PeanutServer>(
                "PeaNUT servers",
                "Every PeaNUT here is checked in the background; its UPSes show in the UPS (PeaNUT) module and its notifications.",
                list,
                delegate (PeanutServer s)
                {
                    return s.Shown + "   -   " + s.BaseUrl + (s.User.Length > 0 ? "   (as " + s.User + ")" : "") + (s.Enabled ? "" : "   [off]");
                },
                edit,
                delegate { p.Changed(); });
            w.Owner = p.Window;
            w.AddAction("Add…", delegate (ListEditorWindow<PeanutServer> lw)
            {
                var s = new PeanutServer();
                if (EditServer(lw, s))
                {
                    lw.Items.Add(s);
                    lw.Changed(lw.Items.Count - 1);
                }
            });
            w.AddAction("Test", async delegate (ListEditorWindow<PeanutServer> lw)
            {
                var pick = lw.SelectedItems;
                if (pick.Count == 0) pick = lw.Items;
                if (pick.Count == 0) return;
                var results = await Task.WhenAll(pick.Select(x => Store.Fetch(x.Copy())));
                MessageBox.Show(lw, string.Join("\n\n", results.Select(Describe)), "Test PeaNUT",
                    MessageBoxButton.OK, results.All(r => r.Ok) ? MessageBoxImage.Information : MessageBoxImage.Warning);
            });
            w.ShowDialog();
            p.Redraw();
        }

        static bool EditServer(Window owner, PeanutServer s)
        {
            bool hasPassword = s.Password.Length > 0;
            var fields = new List<FormField>
            {
                new FormField("Name", s.Name) { Hint = "Shown on the module - e.g. Office or Rack. Empty = the host name." },
                new FormField("Address", s.Url) { Hint = "PeaNUT's web address, e.g. http://192.168.1.20:8080 or https://ups.example.com/peanut" },
                new FormField("Username", s.User) { Hint = "Empty if PeaNUT runs with AUTH_DISABLED=true" },
                FormField.Password("Password", hasPassword ? "A password is saved - type a new one to replace it, or leave it empty to keep it" : "PeaNUT's password"),
                FormField.Check("Accept a self-signed HTTPS certificate", s.IgnoreCert),
                FormField.Check("Check this server", s.Enabled)
            };
            if (hasPassword) fields.Add(FormField.Check("Forget the saved password", false));
            if (!FormWindow.Ask(owner, "PeaNUT server", fields)) return false;

            string url = fields[1].Value.Trim();
            if (url.Length == 0) return false;
            s.Name = fields[0].Value.Trim();
            s.Url = url;
            s.User = fields[2].Value.Trim();
            string pw = fields[3].Value;
            if (!string.IsNullOrEmpty(pw)) s.Password = Secret.Protect(pw);
            else if (hasPassword && fields[6].Checked) s.Password = "";
            s.IgnoreCert = fields[4].Checked;
            s.Enabled = fields[5].Checked;
            return true;
        }

        /// <summary>
        /// Every UPS seen now, plus any renamed one that isn't there now. Saves only the renamed ones; an empty name
        /// (or removing it from the list) gives a UPS its automatic name back.
        /// </summary>
        void EditRenames(SettingsWindow.SettingsPage p, List<UpsRename> saved)
        {
            var rows = new List<UpsRename>();
            foreach (var u in Store.AllUps())
            {
                if (rows.Any(x => x.Key == u.Key)) continue;
                var had = saved.FirstOrDefault(x => x.Key == u.Key);
                rows.Add(new UpsRename { Key = u.Key, Name = had != null ? had.Name : "", Auto = u.AutoName });
            }
            var here = new HashSet<string>(rows.Select(x => x.Key));
            foreach (var r in saved)
                if (!here.Contains(r.Key)) rows.Add(new UpsRename { Key = r.Key, Name = r.Name, Auto = r.Auto });

            Action save = delegate
            {
                saved.Clear();
                saved.AddRange(rows.Where(x => x.Name.Trim().Length > 0));
                Store.SetRenames(saved);
                p.Changed();
            };

            Func<Window, UpsRename, bool> edit = delegate (Window owner, UpsRename r)
            {
                string auto = AutoOf(r);
                var fields = new List<FormField>
                {
                    new FormField("Name", r.Name) { Hint = "Shown on the module, in the popup, notifications and the palette. Empty = " + auto }
                };
                if (!FormWindow.Ask(owner, "Rename " + auto, fields)) return false;
                r.Name = fields[0].Value.Trim();
                return true;
            };

            var w = new ListEditorWindow<UpsRename>(
                "Rename UPSes",
                "Every UPS PeaNUT reports now. Give one a name of your own - leave it empty for the automatic name.",
                rows,
                delegate (UpsRename r)
                {
                    string auto = AutoOf(r);
                    return (r.Name.Length > 0 ? r.Name + "   (was " + auto + ")" : auto + "   (automatic)")
                           + (here.Contains(r.Key) ? "" : "   [not there now]");
                },
                edit,
                save);
            w.Owner = p.Window;
            w.ShowDialog();
            save();
            p.Redraw();
        }

        /// <summary>The automatic name to show for a rename row - the NUT device name when it was never seen with one.</summary>
        static string AutoOf(UpsRename r)
        {
            if (r.Auto.Length > 0) return r.Auto;
            int bar = r.Key.LastIndexOf('|');
            return bar >= 0 ? r.Key.Substring(bar + 1) : r.Key;
        }

        void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            // Which UPS: every one, or one picked from what PeaNUT reports now.
            var ups = Store.AllUps();
            var names = new List<string> { "Every UPS" };
            var keys = new List<string> { "" };
            foreach (var u in ups)
            {
                if (keys.Contains(u.Key)) continue;
                keys.Add(u.Key);
                names.Add(u.Name);
            }
            string saved = cfg.Bag.Get(UpsModule.KeyUps, "");
            if (saved.Length > 0 && !keys.Contains(saved))
            {
                keys.Add(saved);
                names.Add(cfg.Bag.Get("UpsName", "The one picked before") + " (not there now)");
            }
            p.Combo("UPS", names.ToArray(),
                () => names[Math.Max(0, keys.IndexOf(cfg.Bag.Get(UpsModule.KeyUps, "")))],
                v =>
                {
                    int i = Math.Max(0, names.IndexOf(v));
                    cfg.Bag.Set(UpsModule.KeyUps, keys[i]);
                    cfg.Bag.Set("UpsName", i == 0 ? "" : names[i]);
                });
            p.Combo("Show", UpsModule.ShowChoices, () => cfg.Bag.Get(UpsModule.KeyShow, "Charge"), v => cfg.Bag.Set(UpsModule.KeyShow, v));
            p.Combo("Layout (when more than one)", ModuleTypes.TileLayouts, () => cfg.TileLayout, v => cfg.TileLayout = v);
            p.Check("Show names", () => cfg.TileNames, v => cfg.TileNames = v);
            p.Check("Battery meter next to each one", () => cfg.Bag.Get(UpsModule.KeyMeter, false), v =>
            {
                cfg.Bag.Set(UpsModule.KeyMeter, v);
                p.Redraw();
            });
            if (cfg.Bag.Get(UpsModule.KeyMeter, false))
            {
                p.Combo("Meter style", new[] { "Bar", "Dots" }, () => cfg.Bag.Get(UpsModule.KeyMeterStyle, "Bar"), v => cfg.Bag.Set(UpsModule.KeyMeterStyle, v));
                p.Color("Meter colour", () => cfg.Color, v => cfg.Color = v, true, false);
            }
            p.Title(cfg, "");
            p.Note("Green = on mains, yellow = on battery (or its battery wants replacing), red = battery low, overloaded or PeaNUT " +
                   "can't be reached, grey = no reading yet. Hover for the readings; click for everything. " +
                   (ups.Count == 0 ? "No UPS has been read yet - the UPS list here fills in once PeaNUT has answered." : ""));
        }

        // ------------------------------------------------------------------ gallery pictures

        static FrameworkElement BarPicture(Theme t)
        {
            return ModulePics.Row(5,
                ModulePics.Dot(StatusColors.Good, 8), ModulePics.T(t, "Office 100%", t.Fg, 11.5),
                ModulePics.Dot(StatusColors.Warn, 8), ModulePics.T(t, "Rack 64%", t.Fg, 11.5));
        }

        static FrameworkElement WidgetPicture(Theme t)
        {
            return ModulePics.Col(7,
                ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 8), ModulePics.T(t, "Office", t.Fg, 12.5, true), ModulePics.T(t, "Online", t.FgDim, 11.5)),
                ModulePics.Meter(t, 200, 6, 1.0, t.MainAccent),
                ModulePics.Row(6, ModulePics.Dot(StatusColors.Warn, 8), ModulePics.T(t, "Rack", t.Fg, 12.5, true), ModulePics.T(t, "On battery · 23m", t.FgDim, 11.5)),
                ModulePics.Meter(t, 200, 6, 0.64, t.MainAccent));
        }

        // ------------------------------------------------------------------ notifications

        void NotificationsPage(SettingsWindow.SettingsPage p)
        {
            var bag = Bag(p.Settings);
            bool ok = Servers(p.Settings).Count > 0;
            p.NotifySection("PeaNUT notifications", ok, "a PeaNUT server");
            if (!ok) return;
            p.Check("A UPS goes on battery (and when mains power is back)", () => bag.Get(KeyNotifyBattery, true), v => bag.Set(KeyNotifyBattery, v));
            p.Check("A UPS's battery is low", () => bag.Get(KeyNotifyLow, true), v => bag.Set(KeyNotifyLow, v));
            p.Slider("… or, on battery, its charge is at or below (%) - 0 = only when the UPS says it's low", 0, 90,
                () => bag.Get(KeyLowAt, 0), v => bag.Set(KeyLowAt, (int)v));
            p.Check("A UPS says its battery needs replacing", () => bag.Get(KeyNotifyReplace, true), v => bag.Set(KeyNotifyReplace, v));
            p.Check("A PeaNUT server can't be reached (and when it's back)", () => bag.Get(KeyNotifyDown, true), v => bag.Set(KeyNotifyDown, v));
            p.Delay(() => bag.Get(KeyDelay, 0), v => bag.Set(KeyDelay, v));
            p.Kind("On battery shows as", KindOnBattery);
            p.Kind("Mains back shows as", KindMains);
            p.Kind("Battery low shows as", KindLow);
            p.Kind("Replace battery shows as", KindReplace);
            p.Kind("Can't reach PeaNUT shows as", KindDown);
            p.Kind("PeaNUT back shows as", KindUp);
            p.Note("The wait is for going on battery and for a server that can't be reached: a power blip shorter than it says nothing. " +
                   "A server only counts as unreachable after two failed checks in a row, once it has answered since vShell started.");
        }

        sealed class UpsWatch
        {
            public bool LowSaid;
            public bool ReplaceSaid;
            public DateTime Seen;
        }

        sealed class ServerWatch
        {
            public DateTime Checked;
            public int Fails;
            public bool SawUp;
        }

        readonly Dictionary<string, UpsWatch> _ups = new Dictionary<string, UpsWatch>();
        readonly Dictionary<string, ServerWatch> _srv = new Dictionary<string, ServerWatch>();

        /// <summary>Runs every 5 s with vShell's notifications (on its own thread) and posts what changed since the last poll.</summary>
        void Watch(BarSettings s)
        {
            var bag = Bag(s);
            bool nBattery = bag.Get(KeyNotifyBattery, true);
            bool nLow = bag.Get(KeyNotifyLow, true);
            int lowAt = bag.Get(KeyLowAt, 0);
            bool nReplace = bag.Get(KeyNotifyReplace, true);
            bool nDown = bag.Get(KeyNotifyDown, true);
            int delay = Math.Max(0, bag.Get(KeyDelay, 0));

            foreach (var r in Store.Snapshot())
            {
                if (r.Checked == DateTime.MinValue) continue;
                ServerWatch sw;
                if (!_srv.TryGetValue(r.Server.Id, out sw)) { sw = new ServerWatch(); _srv[r.Server.Id] = sw; }
                if (sw.Checked == r.Checked) continue;   // nothing new from this server
                sw.Checked = r.Checked;

                string url = r.Server.BaseUrl;
                string downKey = "peanut|down|" + r.Server.Id;
                if (!r.Ok)
                {
                    sw.Fails++;
                    if (nDown && sw.Fails >= 2 && sw.SawUp)
                    {
                        string name = r.Server.Shown, why = r.Error;
                        DownDelay.Down(downKey, delay, delegate
                        {
                            Notify.Post(Source, "Can't reach PeaNUT " + name, why, url, Notify.KindOf(KindDown), NotifyAbout.Down("PeaNUT " + name, url));
                        });
                    }
                    continue;   // stale readings: don't judge the UPSes on them
                }

                if (DownDelay.Up(downKey) == UpResult.WasNotified)
                    Notify.Post(Source, "PeaNUT " + r.Server.Shown + " is back", Describe(r), url, Notify.KindOf(KindUp), NotifyAbout.Up("PeaNUT " + r.Server.Shown, url));
                sw.Fails = 0;
                sw.SawUp = true;

                foreach (var u in r.Ups)
                {
                    if (!u.Known || u.Unreachable) continue;   // no real reading: don't judge it
                    UpsWatch uw;
                    if (!_ups.TryGetValue(u.Key, out uw)) { uw = new UpsWatch(); _ups[u.Key] = uw; }
                    uw.Seen = DateTime.UtcNow;

                    // On battery / mains back
                    string obKey = "peanut|ob|" + u.Key;
                    if (u.OnBattery && nBattery)
                    {
                        string title = u.Name + " is on battery", detail = Fmt.Summary(u);
                        DownDelay.Down(obKey, delay, delegate
                        {
                            Notify.Post(Source, title, detail, url, Notify.KindOf(KindOnBattery));
                        });
                    }
                    else if (DownDelay.Up(obKey) == UpResult.WasNotified && !u.OnBattery)
                    {
                        Notify.Post(Source, "Mains power is back - " + u.Name, u.StatusText + " · " + Fmt.Pct(u.Charge), url, Notify.KindOf(KindMains));
                    }

                    // Low battery: the UPS's own LB flag, or on battery at/below the chosen charge
                    double c = u.Charge;
                    bool low = u.LowBattery || (lowAt > 0 && u.OnBattery && !double.IsNaN(c) && c <= lowAt);
                    if (low && nLow && !uw.LowSaid)
                    {
                        uw.LowSaid = true;
                        Notify.Post(Source, u.Name + " - battery low", Fmt.Summary(u), url, Notify.KindOf(KindLow));
                    }
                    else if (!low && (!u.OnBattery || double.IsNaN(c) || c > lowAt + 5))
                    {
                        uw.LowSaid = false;
                    }

                    // Replace battery
                    if (u.ReplaceBattery && nReplace && !uw.ReplaceSaid)
                    {
                        uw.ReplaceSaid = true;
                        string model = u.Model;
                        Notify.Post(Source, u.Name + " - replace its battery",
                            "The UPS reports its battery needs replacing" + (model.Length > 0 ? " (" + model + ")" : "") + ".", url, Notify.KindOf(KindReplace));
                    }
                    else if (!u.ReplaceBattery)
                    {
                        uw.ReplaceSaid = false;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ command palette

        void Commands(BarSettings s, List<PaletteCommand> into)
        {
            foreach (var r in Store.Snapshot())
            {
                string url = r.Server.BaseUrl;
                if (url.Length == 0) continue;
                foreach (var u in r.Ups)
                {
                    string sum = Fmt.Summary(u);
                    into.Add(new PaletteCommand
                    {
                        Title = u.Name + " UPS - " + u.StatusText,
                        Detail = "PeaNUT" + (sum.Length > 0 ? " · " + sum : "") + " · open PeaNUT",
                        Glyph = "",
                        Source = "PeaNUT",
                        Words = "ups battery power mains nut peanut " + u.Device + " " + u.ServerName + " " + u.Model,
                        Opens = true,
                        Run = delegate { NoteRender.Open(url); return Task.FromResult<string>(null); }
                    });
                }
                into.Add(new PaletteCommand
                {
                    Title = "Open PeaNUT " + r.Server.Shown,
                    Detail = r.Ok ? "PeaNUT · " + url : "PeaNUT · " + (r.Error.Length > 0 ? r.Error : url),
                    Glyph = "",
                    Source = "PeaNUT",
                    Words = "peanut nut ups dashboard web open " + r.Server.Shown,
                    Opens = true,
                    Run = delegate { NoteRender.Open(url); return Task.FromResult<string>(null); }
                });
            }
        }
    }
}
