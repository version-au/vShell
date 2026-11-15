using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("unifi", "UniFi", Description = "UniFi Network (devices, clients, ports) and UniFi Protect (cameras, lights, events) with their official API keys: two modules, notifications with snapshots, and !u in the run box.",
    Glyph = "\uE701", Group = "Servers & network", Author = "version", Adds = "!u in the run box, notifications with snapshots, palette commands", Types = "UnifiNetwork,UnifiProtect")]

namespace vShell.Ui
{
    /// <summary>
    /// The UniFi plugin (Network and Protect, the official APIs with API keys): the two modules, the console's connection, the notifications
    /// (devices offline, new clients, busy access points; doorbell rings and detections with a snapshot) and !u in the run box.
    /// </summary>
    public sealed class UnifiPlugin : IPlugin
    {
        Action<string, string, string> _netNotice;
        Action<PrEvent, string> _protectEvent;
        static BarManager _mgr;
        static readonly HashSet<string> _prSaid = new HashSet<string>();

        public void Load(PluginHost host)
        {
            _mgr = host.Manager;
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.UnifiNetwork, Display = "UniFi Network", ShortName = "Network", Glyph = "", Group = "Smart home & more",
                Blurb = "devices, clients", HasPopup = true,
                Needs = delegate (BarSettings s) { return Unifi.NetConfigured(s) ? null : "Needs the UniFi connection with a Network key (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new UnifiModule(mgr, h, cfg); },
                Settings = NetworkSettings, BarPicture = NetworkBarPic, WidgetPicture = NetworkPic
            });
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.UnifiProtect, Display = "UniFi Protect", ShortName = "Cameras", Glyph = "", Group = "Smart home & more",
                Blurb = "cameras, events", HasPopup = true,
                Needs = delegate (BarSettings s) { return Unifi.ProtectConfigured(s) ? null : "Needs the UniFi connection with a Protect key (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new ProtectModule(mgr, h, cfg); },
                Settings = ProtectSettings, BarPicture = ProtectBarPic, WidgetPicture = ProtectPic,
                WidgetSize = new Size(600, 400)      // the camera grid fills whatever room it gets
            });
            host.SetUpWhen(delegate (BarSettings s) { return Unifi.NetConfigured(s) || Unifi.ProtectConfigured(s); });
            host.AddIntegration("UniFi", IntegrationSection);
            host.AddNotifications(NetworkNotifySection);
            host.AddNotifications(ProtectNotifySection);
            host.OnSettings(Unifi.Configure);

            host.AddNotifyKind("UniFi.DeviceDown", NotifyLevel.Alert);
            host.AddNotifyKind("UniFi.DeviceUp", NotifyLevel.Normal);
            host.AddNotifyKind("UniFi.NewClient", NotifyLevel.Normal);
            host.AddNotifyKind("UniFi.BusyAp", NotifyLevel.Normal);
            host.AddNotifyKind("Protect.Ring", NotifyLevel.Important);
            foreach (string k in new[] { "Protect.Person", "Protect.Vehicle", "Protect.Package", "Protect.Animal", "Protect.Motion" }) host.AddNotifyKind(k, NotifyLevel.Normal);
            host.AddNotifySource("UniFi");
            host.AddNotifySource("Protect");
            _netNotice = delegate (string kind, string title, string detail)
            {
                BarSettings s = _mgr.Settings;
                if (s == null) return;
                Notify.Post("UniFi", title, detail, Unifi.NetworkWeb(s), Notify.KindOf(kind));
            };
            _protectEvent = delegate (PrEvent ev, string what) { ProtectNotice(ev, what); };
            Unifi.NetNotice += _netNotice;
            Unifi.ProtectEvent += _protectEvent;

            host.AddBang("UniFi", delegate (string key, string q, BarSettings s)
            {
                if (key != UnSettings.Of(s).Bang || !Unifi.NetConfigured(s)) return null;
                return new RunAction { Kind = "UniFi", Query = q, Display = q.Length == 0 ? "UniFi: devices that are offline, and new clients" : "UniFi: matching " + q };
            }, Rows, delegate (BarSettings s) { return Unifi.NetConfigured(s) ? "!" + UnSettings.Of(s).Bang + " UniFi" : null; });
            host.AddCommands("UniFi", Commands);
        }

        // =================================================================
        // The command palette: Protect's lights on / off, open Network / Protect (and a camera)
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            const string Src = "UniFi";
            Func<string, string, string, PaletteCommand> open = delegate (string title, string url, string words)
            {
                return new PaletteCommand
                {
                    Title = title, Detail = Src, Glyph = "", Source = Src, Words = "unifi ubiquiti web page " + words, Opens = true,
                    Run = delegate { RunPanel.Open(url); return System.Threading.Tasks.Task.FromResult(""); }
                };
            };
            if (Unifi.NetConfigured(s)) into.Add(open("Open UniFi Network", Unifi.NetworkWeb(s), "network devices clients"));
            if (!Unifi.ProtectConfigured(s)) return;
            into.Add(open("Open UniFi Protect", Unifi.ProtectWeb(s), "protect cameras nvr"));
            PrSnapshot pro = Unifi.Protect;
            if (pro == null || !pro.Ok) return;
            foreach (PrCamera c in pro.Cameras) into.Add(open("Open camera " + c.Name + " in Protect", Unifi.ProtectCameraWeb(s, c.Id), "protect camera " + (c.IsDoorbell ? "doorbell" : "")));
            foreach (PrLight l0 in pro.Lights)
            {
                PrLight l = l0;
                if (!l.Online) continue;
                bool on = !l.Forced;
                into.Add(new PaletteCommand
                {
                    Title = (on ? "Turn on " : "Turn off ") + l.Name, Detail = Src + " · Protect light" + (l.Forced ? " · forced on" : ""), Glyph = "", Source = Src,
                    Words = "protect floodlight light lamp " + (on ? "switch on force" : "switch off back to motion"), Boost = 1,
                    Run = async delegate { string err = await Unifi.LightAsync(l.Id, on); return err ?? PaletteCommand.Worked(l.Name + (on ? " is on" : " is back to its own mode")); }
                });
            }
        }

        public void Unload()
        {
            if (_netNotice != null) Unifi.NetNotice -= _netNotice;
            if (_protectEvent != null) Unifi.ProtectEvent -= _protectEvent;
            Unifi.Stop();
        }

        // =================================================================
        // UniFi Protect: doorbell rings and detections, with a snapshot on the card
        // =================================================================
        static async void ProtectNotice(PrEvent ev, string what)
        {
            BarSettings s = _mgr == null ? null : _mgr.Settings;
            if (s == null) return;
            UnSettings u = UnSettings.Of(s);
            string key, title;
            switch (what)
            {
                case "ring": if (!u.NotifyRing) return; key = "Protect.Ring"; title = "Someone's at the door"; break;
                case "person": if (!u.NotifyPerson) return; key = "Protect.Person"; title = "Person"; break;
                case "vehicle": if (!u.NotifyVehicle) return; key = "Protect.Vehicle"; title = "Vehicle"; break;
                case "package": if (!u.NotifyPackage) return; key = "Protect.Package"; title = "Package"; break;
                case "animal": if (!u.NotifyAnimal) return; key = "Protect.Animal"; title = "Animal"; break;
                case "motion": if (!u.NotifyMotion) return; key = "Protect.Motion"; title = "Motion"; break;
                default: return;
            }
            // only the cameras asked for
            string only = u.NotifyCameras.Trim();
            if (only.Length > 0)
            {
                bool hit = false;
                foreach (string c in only.Split(','))
                    if (c.Trim().Length > 0 && string.Equals(c.Trim(), ev.CameraName, StringComparison.OrdinalIgnoreCase)) hit = true;
                if (!hit) return;
            }
            lock (_prSaid)
            {
                if (!_prSaid.Add(ev.Id + "|" + what)) return;
                if (_prSaid.Count > 500) _prSaid.Clear();
            }
            string where = ev.CameraName.Length > 0 ? ev.CameraName : "Protect";
            if (what != "ring") title += " · " + where;
            string image = "";
            try { image = await Unifi.SaveEventSnapshotAsync(ev); } catch { }
            Notify.Post("Protect", title, (what == "ring" ? where + " · " : "") + TimeText.Clock(ev.Start.ToLocalTime(), false),
                        Unifi.ProtectCameraWeb(s, ev.CameraId), Notify.KindOf(key), true, image);
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            UnSettings u = UnSettings.Of(S);
            p.Header("UniFi (Network and Protect)");
            p.Text("Console address (for example https://192.168.1.1 - your UDM, UCG, Cloud Key or UNVR)", delegate { return u.Url; }, delegate (string v) { u.Url = v; });
            PasswordBox npw = p.Secret("Network API key", "key", delegate { return u.NetKey; }, delegate (string v) { u.NetKey = v; },
                "In UniFi Network open Settings > Control Plane > Integrations and create an API key. vShell only reads with it (devices, statistics, clients). Stored encrypted for your Windows account.");
            PasswordBox ppw = p.Secret("Protect API key", "key", delegate { return u.ProtectKey; }, delegate (string v) { u.ProtectKey = v; },
                "In UniFi Protect open Settings > Control Plane > Integrations and create an API key. Protect's keys can't be made read-only: vShell uses it for cameras, snapshots, " +
                "stream links, events, lights on / off and PTZ presets. Create it as a user with only the access you want vShell to have.");
            p.Check("Accept the console's self-signed HTTPS certificate", delegate { return u.IgnoreCert; }, delegate (bool v) { u.IgnoreCert = v; });
            p.Text("Network site (empty = the first one)", delegate { return u.Site; }, delegate (string v) { u.Site = v; });
            p.Slider("Check Network every (seconds)", 5, 120, delegate { return u.PollSeconds; }, delegate (double v) { u.PollSeconds = (int)v; });
            p.Header("Account for vTrace's history (optional)");
            p.Text("Username of a local View Only account", delegate { return u.HistUser; }, delegate (string v) { u.HistUser = v; });
            PasswordBox hpw = p.Secret("Its password", "password", delegate { return u.HistPassword; }, delegate (string v) { u.HistPassword = v; },
                "vShell doesn't use this - the vTrace app does: UniFi only gives its history (System Log: admin changes, firmware updates, devices dropping off, " +
                "internet drops) to a signed-in user, not to an API key. Make a local account just for it: UniFi OS > Admins & Users > add an admin, " +
                "\"Restrict to local access only\", role View Only - it can't change anything. Stored encrypted for your Windows account.");
            p.Note("Leave these empty if you don't use vTrace - vTrace then notices device changes itself while it runs.");            p.Text("Run box keyword (without the !)", delegate { return u.Bang; }, delegate (string v) { u.Bang = v; },
                   "!" + u.Bang + " phone - UniFi clients and devices whose name, IP or MAC contains \"phone\": where they're connected, their IP. Enter copies the IP.");
            p.Test(delegate
            {
                if (npw.Password.Trim().Length > 0) { u.NetKey = Secret.Protect(npw.Password.Trim()); npw.Password = ""; }
                if (ppw.Password.Trim().Length > 0) { u.ProtectKey = Secret.Protect(ppw.Password.Trim()); ppw.Password = ""; }
                if (hpw.Password.Trim().Length > 0) { u.HistPassword = hpw.Password.Trim(); hpw.Password = ""; }
                p.Changed();
            }, delegate { return Unifi.TestAsync(S); });
        }

        static void NetworkNotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            UnSettings u = UnSettings.Of(S);
            p.NotifySection("UniFi Network notifications", Unifi.NetConfigured(S), "UniFi Network");
            p.Check("A device (gateway, switch, access point) goes offline (and when it's back)", delegate { return u.NotifyDevices; }, delegate (bool v) { u.NotifyDevices = v; p.Redraw(); });
            if (u.NotifyDevices)
            {
                p.Delay(delegate { return u.NotifyDelay; }, delegate (int v) { u.NotifyDelay = v; });
                p.Kind("Offline shows as", "UniFi.DeviceDown");
                p.Kind("Back online shows as", "UniFi.DeviceUp");
            }
            p.Check("A device that has never been on the network before joins", delegate { return u.NotifyNewClient; }, delegate (bool v) { u.NotifyNewClient = v; p.Redraw(); });
            if (u.NotifyNewClient) p.Kind("New devices show as", "UniFi.NewClient");
            p.Slider("An access point has this many clients or more - 0 = off", 0, 100, delegate { return u.NotifyApClients; }, delegate (double v) { u.NotifyApClients = (int)v; });
            p.Kind("Busy access points show as", "UniFi.BusyAp");
            p.Note("Notifications work without a UniFi Network module too (vShell then checks every 30 seconds instead of as often as \"Check Network every\" on the Connection tab). vShell remembers the devices it has seen, " +
                   "so \"never been on the network before\" holds across restarts; the first check only learns them.");
        }

        static void ProtectNotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            UnSettings u = UnSettings.Of(S);
            p.NotifySection("UniFi Protect notifications", Unifi.ProtectConfigured(S), "UniFi Protect");
            p.Check("Doorbell rings", delegate { return u.NotifyRing; }, delegate (bool v) { u.NotifyRing = v; p.Redraw(); });
            if (u.NotifyRing) p.Kind("Rings show as", "Protect.Ring");
            p.Check("A person is detected", delegate { return u.NotifyPerson; }, delegate (bool v) { u.NotifyPerson = v; p.Redraw(); });
            if (u.NotifyPerson) p.Kind("People show as", "Protect.Person");
            p.Check("A vehicle is detected", delegate { return u.NotifyVehicle; }, delegate (bool v) { u.NotifyVehicle = v; p.Redraw(); });
            if (u.NotifyVehicle) p.Kind("Vehicles show as", "Protect.Vehicle");
            p.Check("A package is detected", delegate { return u.NotifyPackage; }, delegate (bool v) { u.NotifyPackage = v; p.Redraw(); });
            if (u.NotifyPackage) p.Kind("Packages show as", "Protect.Package");
            p.Check("An animal is detected", delegate { return u.NotifyAnimal; }, delegate (bool v) { u.NotifyAnimal = v; p.Redraw(); });
            if (u.NotifyAnimal) p.Kind("Animals show as", "Protect.Animal");
            p.Check("Any motion (can be a lot)", delegate { return u.NotifyMotion; }, delegate (bool v) { u.NotifyMotion = v; p.Redraw(); });
            if (u.NotifyMotion) p.Kind("Motion shows as", "Protect.Motion");
            if (u.NotifyRing || u.NotifyPerson || u.NotifyVehicle || u.NotifyPackage || u.NotifyAnimal || u.NotifyMotion)
                p.Text("Only these cameras (names, comma separated - empty = all)", delegate { return u.NotifyCameras; }, delegate (string v) { u.NotifyCameras = v; });
            p.Note("Events come in live from Protect the moment they start, with a snapshot from that camera on the card (click it to open the camera in Protect). " +
                   "Protect's own API has no event history, so only events while vShell is running are shown. Detections follow the smart detections set up on each camera in Protect.");
        }

        static void NetworkSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            UnView v = new UnView(cfg);
            if (!Unifi.NetConfigured(p.Settings)) p.Note("Set up UniFi first: Plugins page in this window (the console's address and a Network API key).");
            p.Combo(p.IsWidget ? "Show" : "The popup shows", UnView.Views, delegate { return v.View; }, delegate (string s) { v.View = s; });
            p.Note("All = Devices and Clients tabs; Devices or Clients = just that list, without tabs.");
            p.Slider("Rows to show", 5, 100, delegate { return v.Count; }, delegate (double d) { v.Count = (int)d; });
            if (!p.IsWidget)
            {
                p.Check("Show the internet speed (↓ / ↑ through the gateway)", delegate { return v.BarRates; }, delegate (bool on) { v.BarRates = on; });
                p.Check("Show the number of clients", delegate { return v.BarClients; }, delegate (bool on) { v.BarClients = on; });
                p.Title(cfg, "");
            }
            p.Note("Read-only. Devices: the gateway, switches and access points with CPU, memory, uptime and clients - click one for its ports (⚡ = PoE). " +
                   "Clients: everything connected, with a search box; NEW marks one vShell hasn't seen before. Right-click a row to copy its IP, MAC or name. " +
                   "Notifications for devices going offline, new clients and busy access points: Settings > Notifications. In the run box: !" + UnSettings.Of(p.Settings).Bang + " name.");
        }

        static void ProtectSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            PrView v = new PrView(cfg);
            if (!Unifi.ProtectConfigured(p.Settings)) p.Note("Set up UniFi first: Plugins page in this window (the console's address and a Protect API key).");
            p.Slider("Columns of cameras", 1, 6, delegate { return v.Columns; }, delegate (double d) { v.Columns = (int)d; });
            p.Slider("Refresh the snapshots every (seconds)", 2, 120, delegate { return v.SnapSeconds; }, delegate (double d) { v.SnapSeconds = (int)d; });
            p.Text("Only these cameras, in this order (names, comma separated; empty = all)", delegate { return v.Cameras; }, delegate (string s) { v.Cameras = s; });
            var names = new List<string>();
            foreach (PrCamera c in Unifi.Protect.Cameras) names.Add(c.Name);
            if (names.Count > 0) p.Note("Your cameras: " + string.Join(", ", names));
            p.Check("Show the lights (click one to turn it on / back to its own mode)", delegate { return v.ShowLights; }, delegate (bool on) { v.ShowLights = on; });
            p.Check("Show the recent events", delegate { return v.ShowEvents; }, delegate (bool on) { v.ShowEvents = on; });
            if (!p.IsWidget)
            {
                p.Check("Show the last event next to the cameras online (for an hour)", delegate { return v.BarEvent; }, delegate (bool on) { v.BarEvent = on; });
                p.Title(cfg, "");
            }
            p.Note("Snapshots refresh only while the cameras are on screen. Click a camera for the big view: Open in Protect, Play in VLC (the live stream) and, " +
                   "for PTZ cameras, Home and presets 1 - 4. Protect's API has no event history, so the events are the ones seen since vShell started. " +
                   "Notifications for rings and detections (with a snapshot): Settings > Notifications.");
        }

        // ---- Add a module pictures ----
        static FrameworkElement NetworkBarPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            double fs = 12.5;
            return ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 9), ModulePics.G(0xE701, fg, 14), ModulePics.T(t, "↓ 42 Mb/s", StatusColors.Good, fs), ModulePics.T(t, "↑ 6 Mb/s", acc, fs), ModulePics.T(t, "38", dim, fs));
        }

        static FrameworkElement ProtectBarPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim;
            double fs = 12.5;
            return ModulePics.Row(6, ModulePics.G(0xE722, fg, 14), ModulePics.T(t, "6/6", fg, fs), ModulePics.T(t, "Person · Driveway · 2m", dim, fs));
        }

        static FrameworkElement NetworkPic(Theme t)
        {
            Func<string, string, string, double, FrameworkElement> dev = delegate (string name, string kind, string clients, double cpu)
            {
                var g = new Grid { Width = 196 };
                g.Children.Add(ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 7), ModulePics.Col(0, ModulePics.T(t, name, t.Fg, 9.5, true), ModulePics.T(t, kind + " · " + clients + " clients", t.FgDim, 8.5))));
                var m = ModulePics.Meter(t, 40, 4, cpu, t.MainAccent);
                m.HorizontalAlignment = HorizontalAlignment.Right;
                g.Children.Add(m);
                return g;
            };
            var pills = ModulePics.Row(4, new Border { Background = t.ActiveBg, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 2, 7, 2), Child = ModulePics.T(t, "Devices", t.Fg, 8.5) },
                               new Border { Background = t.Track, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 2, 7, 2), Child = ModulePics.T(t, "Clients", t.FgDim, 8.5) });
            return ModulePics.Col(5, pills, dev("Dream Machine", "Gateway", "38", 0.22), dev("Office switch", "Switch", "12", 0.1), dev("Lounge AP", "Access point", "17", 0.35));
        }

        static FrameworkElement ProtectPic(Theme t)
        {
            Func<string, Color, FrameworkElement> cam = delegate (string name, Color c)
            {
                var g = new Grid { Width = 94, Height = 53, Margin = new Thickness(2) };
                g.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush(c, Color.FromRgb(20, 24, 30), 60) });
                g.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(5, 2, 5, 2), CornerRadius = new CornerRadius(0, 0, 5, 5),
                                            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Child = ModulePics.T(t, name, Brushes.White, 8) });
                return g;
            };
            return ModulePics.Col(2, ModulePics.Row(0, cam("Driveway", Color.FromRgb(70, 96, 80)), cam("Front door", Color.FromRgb(96, 84, 64))),
                          ModulePics.Row(0, cam("Backyard", Color.FromRgb(60, 82, 104)), cam("Garage", Color.FromRgb(84, 84, 92))));
        }

        // =================================================================
        // The run box: !u name -> matching clients and devices (where they're connected, their IP); Enter copies the IP
        // =================================================================
        static void Rows(RunPanel box, RunAction a)
        {
            string q = a.Query;
            UnSnapshot snap = Unifi.Network;
            if (!snap.Ok) { Unifi.Poke(); box.Status(snap.Error.Length > 0 ? "UniFi: " + snap.Error : "Asking UniFi Network…"); return; }
            Func<string[], bool> hit = delegate (string[] parts)
            {
                if (q.Length == 0) return false;
                foreach (string p in parts) if (!string.IsNullOrEmpty(p) && p.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            };
            int n = 0;
            foreach (UnDevice d in snap.Devices)
            {
                if (q.Length == 0 ? d.Online : !hit(new[] { d.Name, d.Model, d.Ip, d.Mac })) continue;     // nothing typed: the offline ones
                if (n++ >= 9) break;
                string ip = d.Ip, name = d.Name;
                box.AddRow(d.Online ? "" : "", d.Name + "   " + d.Model, (d.Online ? "online" : Unifi.Nice(d.State)) + (ip.Length > 0 ? " · " + ip : "") + (d.Clients > 0 ? " · " + d.Clients + " clients" : ""), delegate
                {
                    if (ip.Length == 0) return;
                    try { Clipboard.SetText(ip); } catch { }
                    box.Remember(box.Text);
                    box.Status("Copied " + ip + " (" + name + ")");
                }, null);
            }
            foreach (UnClient c in snap.Clients)
            {
                if (q.Length == 0 ? !c.IsNew : !hit(new[] { c.Name, c.Ip, c.Mac })) continue;
                if (n++ >= 9) break;
                UnDevice via = snap.Device(c.UplinkId);
                string ip = c.Ip, label = c.Label;
                box.AddRow(c.Wireless ? "" : "", c.Label + (c.IsNew ? "   (new)" : ""), (ip.Length > 0 ? ip : c.Mac) + (via != null ? " · via " + via.Name : ""), delegate
                {
                    if (ip.Length == 0) return;
                    try { Clipboard.SetText(ip); } catch { }
                    box.Remember(box.Text);
                    box.Status("Copied " + ip + " (" + label + ")");
                }, null);
            }
            if (n == 0) box.Status(q.Length > 0 ? "Nothing in UniFi matches " + q + "." : "Type part of a name, IP or MAC.");
            else box.Status("Enter: copy the IP   ·   " + snap.Devices.FindAll(delegate (UnDevice d) { return d.Online; }).Count + " of " + snap.Devices.Count + " devices online · " + snap.Clients.Count + " clients");
            box.Select(0);
        }
    }
}
