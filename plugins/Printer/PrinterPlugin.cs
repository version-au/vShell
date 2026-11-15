using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("printer", "3D printers", Description = "Klipper printers through Moonraker (Fluidd / Mainsail): a module with the print, webcam, temperatures, spool and files, notifications, and palette commands.",
    Glyph = "\uE749", Group = "Devices", Author = "version", Adds = "notifications, palette commands, !3d in the run box", Types = "Printer")]

namespace vShell.Ui
{
    /// <summary>
    /// The 3D printers plugin (Klipper + Moonraker - Fluidd / Mainsail): the module, the printers on the Plugins page, the notifications and
    /// the palette's printer commands.
    /// </summary>
    public sealed class PrinterPlugin : IPlugin
    {
        Action<PrinterConn, string, string, string, string, string> _notice;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.Printer, Display = "3D printer (Moonraker)", ShortName = "Printer", Glyph = "", Group = "Smart home & more",
                Blurb = "Klipper print, webcam, temps", HasPopup = true,
                Needs = delegate (BarSettings s) { return Moonraker.Configured(s) ? null : "Needs a printer (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new PrinterModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(Moonraker.Configured);
            host.AddIntegration("3D printers", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(Moonraker.Configure);

            host.AddNotifyKind("Printer.Done", NotifyLevel.Important);
            host.AddNotifyKind("Printer.Problem", NotifyLevel.Alert);
            host.AddNotifyKind("Printer.Progress", NotifyLevel.Normal);
            host.AddNotifyKind("Printer.Cooled", NotifyLevel.Normal);
            host.AddNotifySource("3D printer");
            _notice = delegate (PrinterConn c, string kind, string title, string detail, string url, string picture)
            {
                Notify.Post("3D printer", title, detail, url, Notify.KindOf(kind), true, picture);
            };
            Moonraker.Notice += _notice;

            host.AddCommands("Printers", Commands);
            // !3d pause: the palette's printer commands for "pause" (pause / resume / cancel, preheat, home, lights …)
            host.AddBang("Printer", delegate (string key, string q, BarSettings s)
            {
                if (key != PtSettings.Of(s).Bang || !Moonraker.Configured(s)) return null;
                return new RunAction { Kind = "Palette", Query = "Printer " + q, Display = "3D printer: " + (q.Length == 0 ? "everything" : q) };
            }, delegate { }, delegate (BarSettings s) { return Moonraker.Configured(s) ? "!" + PtSettings.Of(s).Bang + " 3D printer" : null; });
        }

        public void Unload()
        {
            if (_notice != null) Moonraker.Notice -= _notice;
            Moonraker.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        /// <summary>The printers: one card each, Add printer, and the preheat presets they share.</summary>
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            PtSettings ps = PtSettings.Of(p.Settings);
            List<PrinterConn> printers = ps.Printers;
            foreach (PrinterConn pc0 in new List<PrinterConn>(printers))
            {
                PrinterConn pc = pc0;
                p.Header("3D printer: " + pc.Shown);
                p.Text("Name", delegate { return pc.Name; }, delegate (string v) { pc.Name = v.Trim(); });
                p.Text("Address (Fluidd's, e.g. http://printer.local - or Moonraker's, http://192.168.1.50:7125)", delegate { return pc.Url; }, delegate (string v) { pc.Url = v.Trim(); });
                PasswordBox kpw = p.Secret("Moonraker API key (only if it asks for one)", "API key", delegate { return pc.ApiKey; }, delegate (string v) { pc.ApiKey = v; },
                    "Most printers on a home network don't need one (Moonraker trusts your network - [authorization] trusted_clients in moonraker.conf). If Test connection " +
                    "says it wants a key: in Fluidd open Settings > Authentication (or run the API key command) and paste it here. Stored encrypted for your Windows account.");
                p.Check("Accept a self-signed HTTPS certificate", delegate { return pc.IgnoreCert; }, delegate (bool v) { pc.IgnoreCert = v; });
                p.Text("Webcam (its name in Fluidd; empty = the first one, Off = none)", delegate { return pc.Webcam; }, delegate (string v) { pc.Webcam = v.Trim(); });
                p.Check("Use this printer", delegate { return pc.Enabled; }, delegate (bool v) { pc.Enabled = v; });
                p.Test(delegate
                {
                    if (kpw.Password.Trim().Length > 0) { pc.ApiKey = Secret.Protect(kpw.Password.Trim()); kpw.Password = ""; }
                    p.Changed();
                }, delegate { return Moonraker.TestAsync(pc); });
                p.Button("Remove this printer", "Remove", delegate
                {
                    if (Questions.YesNo(p.Window, "Remove " + pc.Shown + " from vShell?", "3D printer") != MessageBoxResult.Yes) return;
                    printers.Remove(pc);
                    p.Changed();
                    p.Redraw();
                });
            }
            p.Header(printers.Count == 0 ? "3D printers (Klipper + Moonraker)" : "More 3D printers");
            p.Button(printers.Count == 0 ? "A printer with Fluidd or Mainsail (Moonraker)" : "Another printer", "+ Add printer", delegate
            {
                var pc = new PrinterConn { Name = printers.Count == 0 ? "Printer" : "Printer " + (printers.Count + 1) };
                pc.Normalize();
                printers.Add(pc);
                p.Changed();
                p.Redraw();
            }, true);
            p.Text("Preheat presets (name nozzle/bed, comma between)", delegate { return ps.Presets; }, delegate (string v) { ps.Presets = v; },
                   "The preheat buttons in the printer panel, e.g. PLA 210/60, PETG 240/80, ABS 250/100. A bed of 0 (PLA 210/0) leaves the bed alone.");
            p.Text("Run box keyword (without the !)", delegate { return ps.Bang; }, delegate (string v) { ps.Bang = v; },
                   "!" + ps.Bang + " pause - the palette's printer commands for \"pause\": pause / resume / cancel the print, preheat, cool down and the rest " +
                   "(the ones that ask need Enter twice). !" + ps.Bang + " on its own lists them all.");
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            PtSettings ps = PtSettings.Of(S);
            p.NotifySection("3D printer notifications", Moonraker.Configured(S), "a printer");
            p.Check("A print finishes (with its thumbnail)", delegate { return ps.NotifyDone; }, delegate (bool v) { ps.NotifyDone = v; p.Redraw(); });
            if (ps.NotifyDone) p.Kind("Finished prints show as", "Printer.Done");
            p.Check("A print is paused (a filament runout, a pause in the G-code ...)", delegate { return ps.NotifyPause; }, delegate (bool v) { ps.NotifyPause = v; p.Redraw(); });
            p.Check("A print is cancelled or fails, or Klipper shuts down", delegate { return ps.NotifyFail; }, delegate (bool v) { ps.NotifyFail = v; p.Redraw(); });
            if (ps.NotifyPause || ps.NotifyFail) p.Kind("Problems (paused, cancelled, failed, Klipper shut down) show as", "Printer.Problem");
            p.Check("A print starts", delegate { return ps.NotifyStart; }, delegate (bool v) { ps.NotifyStart = v; });
            p.Combo("Progress every", PtSettings.Steps, delegate { return ps.NotifyEvery > 0 ? ps.NotifyEvery + " %" : "Off"; },
                    delegate (string v) { int n; ps.NotifyEvery = int.TryParse(v.Replace("%", "").Trim(), out n) ? n : 0; });
            p.Slider("The print reaches this layer - 0 = off", 0, 500, delegate { return ps.NotifyLayer; }, delegate (double v) { ps.NotifyLayer = (int)v; });
            p.Note("A layer notification is handy to check the first layers went down well (e.g. layer 3), or to swap filament at a layer.");
            p.Kind("Progress (started, every N %, a layer) shows as", "Printer.Progress");
            p.Check("After a print, the bed has cooled down (safe to take the print off)", delegate { return ps.NotifyCooled; }, delegate (bool v) { ps.NotifyCooled = v; p.Redraw(); });
            if (ps.NotifyCooled)
            {
                p.Slider("Cooled down at (°C)", 20, 80, delegate { return ps.CoolTemp; }, delegate (double v) { ps.CoolTemp = (int)v; });
                p.Kind("Bed cooled down shows as", "Printer.Cooled");
            }
            p.Note("These work without a printer module too (vShell then checks every 10 seconds instead of every 2).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            PtView v = new PtView(cfg);
            List<PrinterConn> printers = PtSettings.Of(p.Settings).Printers;
            if (!Moonraker.Configured(p.Settings)) p.Note("Set up your printer first (Plugins page in this window).");
            var names = new List<string> { "Every printer" };
            foreach (PrinterConn pc in printers) names.Add(pc.Shown);
            p.Combo("Printer", names.ToArray(), delegate
            {
                PrinterConn sel = printers.Find(delegate (PrinterConn x) { return x.Id == v.Printer; });
                return sel != null ? sel.Shown : "Every printer";
            }, delegate (string s)
            {
                PrinterConn sel = printers.Find(delegate (PrinterConn x) { return x.Shown == s; });
                v.Printer = sel != null ? sel.Id : "";
            });
            if (!p.IsWidget)
            {
                p.Combo("Time on the bar", PtView.BarTimes, delegate { return v.BarTime; }, delegate (string s) { v.BarTime = s; });
                p.Note("Left = time left (1h 12m); Finish = when it'll be done (3:45 PM); Both; None.");
                p.Check("Show the nozzle and bed temperatures on the bar", delegate { return v.BarTemps; }, delegate (bool on) { v.BarTemps = on; });
            }
            p.Check(p.IsWidget ? "Hide while the printer is off (the widget too, when nothing else in it shows)" : "Hide while the printer is off",
                    delegate { return v.HideOffline; }, delegate (bool on) { v.HideOffline = on; });
            p.Check("Show the webcam", delegate { return v.ShowCam; }, delegate (bool on) { v.ShowCam = on; p.Redraw(); });
            if (v.ShowCam)
            {
                p.Combo("Webcam", PtView.CamModes, delegate { return v.CamMode; }, delegate (string s) { v.CamMode = s; p.Redraw(); });
                if (v.CamMode == "Live")
                    p.Slider("Live: at most (frames a second)", 1, 30, delegate { return v.CamFps; }, delegate (double d) { v.CamFps = (int)d; });
                // Live falls back to snapshots when the stream doesn't work, so this one shows either way
                p.Slider(v.CamMode == "Live" ? "Snapshots (when the live stream doesn't work): one every (seconds)" : "Snapshots: one every (seconds)", 1, 30,
                         delegate { return v.CamSeconds; }, delegate (double d) { v.CamSeconds = (int)d; });
                p.Note("Live shows the camera's MJPEG stream - smooth, up to the frames a second set (the camera's own rate is the limit). Snapshots fetches one picture " +
                       "every few seconds - much less network and CPU. Either way nothing is fetched while the webcam is folded away or the panel is closed; if the live " +
                       "stream doesn't work (a WebRTC camera), snapshots are used.");
            }
            p.Check("Show the temperatures (with preheat presets)", delegate { return v.ShowTemps; }, delegate (bool on) { v.ShowTemps = on; });
            p.Check("Show the Spoolman spool", delegate { return v.ShowSpool; }, delegate (bool on) { v.ShowSpool = on; });
            p.Check("Show the lights and power devices", delegate { return v.ShowPower; }, delegate (bool on) { v.ShowPower = on; });
            p.Check("Show Files / History / Queue / Macros / Console", delegate { return v.ShowLists; }, delegate (bool on) { v.ShowLists = on; });
            p.Check("Show the \"Fluidd\" button", delegate { return v.ShowFluidd; }, delegate (bool on) { v.ShowFluidd = on; });
            if (!p.IsWidget) p.Title(cfg, "");
            p.Note((p.IsWidget ? "" : "On a bar: a dot (green printing, amber paused, red a problem), the progress, the time and the temperatures - click it for the panel. ") +
                   "The panel: the print with its thumbnail, Pause / Resume / Cancel / Emergency stop (it asks first), the webcam (click it for the live stream), " +
                   "temperatures with Set and the preheat presets (Plugins page), the Spoolman spool (Change picks another), power devices, and the Files " +
                   "(right-click: print, add to the queue), History (print again), Queue, Macros and Console tabs.");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 9), ModulePics.T(t, "64%", t.Fg, 12.5), ModulePics.T(t, "· 1h 12m", t.FgDim, 12.5), ModulePics.T(t, "210° / 60°", t.MainAccent, 12.5));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            FrameworkElement job = ModulePics.Col(3, ModulePics.T(t, "Printing   64% · layer 120 of 188", StatusColors.Good, 10.5), ModulePics.T(t, "benchy_0.2mm_PLA", fg, 12, true),
                                                  ModulePics.Meter(t, 150, 5, 0.64, acc), ModulePics.T(t, "1h 12m left · done at 3:45 PM", dim, 10.5));
            return ModulePics.Col(6, ModulePics.Row(8, ModulePics.Square(ModulePics.Shade(t, 60), 40, 4), job),
                                  ModulePics.Row(6, ModulePics.T(t, "Nozzle", dim, 11), ModulePics.T(t, "209.8 → 210 °C", fg, 11)),
                                  ModulePics.Row(6, ModulePics.T(t, "Bed", dim, 11), ModulePics.T(t, "60.1 → 60 °C", fg, 11)));
        }

        // =================================================================
        // The command palette: pause / resume / cancel, preheat, cool down, lights, power
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!Moonraker.Configured(s)) return;
            const string Src = "Printer";
            foreach (PrinterState p0 in Moonraker.All())
            {
                PrinterState p = p0;
                if (!p.Ok) continue;
                string id = p.PrinterId, name = p.PrinterName;
                string detail = Src + " · " + p.StateText + (p.Busy ? " · " + Math.Floor(p.Progress * 100) + "%" : "");
                const string words = "3d printer print klipper";
                if (p.Printing)
                    into.Add(new PaletteCommand { Title = "Pause the print on " + name, Detail = detail, Glyph = "", Source = Src, Words = words, Run = delegate { return Moonraker.PauseAsync(id); } });
                if (p.Paused)
                    into.Add(new PaletteCommand { Title = "Resume the print on " + name, Detail = detail, Glyph = "", Source = Src, Words = words + " continue unpause", Run = delegate { return Moonraker.ResumeAsync(id); } });
                if (p.Busy)
                    into.Add(new PaletteCommand { Title = "Cancel the print on " + name, Detail = detail, Glyph = "", Source = Src, Words = words + " stop abort", Ask = true, Run = delegate { return Moonraker.CancelAsync(id); } });
                if (p.Ready)
                {
                    into.Add(new PaletteCommand { Title = "Emergency stop " + name, Detail = detail, Glyph = "", Source = Src, Words = words + " estop halt kill", Ask = true, Run = delegate { return Moonraker.EmergencyStopAsync(id); } });
                    if (!p.Busy)
                    {
                        foreach (Tuple<string, double, double> pre in Moonraker.Presets(s))
                        {
                            Tuple<string, double, double> pr = pre;
                            into.Add(new PaletteCommand
                            {
                                Title = "Preheat " + name + " for " + pr.Item1, Detail = Src + " · " + pr.Item2 + "° / " + pr.Item3 + "°", Glyph = "", Source = Src,
                                Words = words + " heat warm temperature", Run = delegate { return Moonraker.PreheatAsync(id, pr.Item2, pr.Item3); }
                            });
                        }
                        into.Add(new PaletteCommand { Title = "Cool down " + name, Detail = detail, Glyph = "", Source = Src, Words = words + " heaters off cold", Run = delegate { return Moonraker.CoolDownAsync(id); } });
                    }
                    foreach (PtLight l0 in p.Lights)
                    {
                        PtLight l = l0;
                        bool on = l.On;
                        into.Add(new PaletteCommand
                        {
                            Title = "Turn " + (on ? "off " : "on ") + name + " " + l.Name, Detail = Src + " · light " + (on ? "on" : "off"), Glyph = "", Source = Src,
                            Words = words + " light lamp led " + (on ? "off" : "on"), Run = delegate { return Moonraker.LightAsync(id, l, !on); }
                        });
                    }
                }
                else if (p.Klippy == "shutdown" || p.Klippy == "error")
                {
                    into.Add(new PaletteCommand { Title = "Firmware restart " + name, Detail = detail, Glyph = "", Source = Src, Words = words + " restart klipper reboot", Ask = true, Run = delegate { return Moonraker.FirmwareRestartAsync(id); } });
                }
                foreach (PrPower pw0 in p.Power)
                {
                    PrPower pw = pw0;
                    if (pw.Locked && p.Busy) continue;
                    bool on = pw.On;
                    into.Add(new PaletteCommand
                    {
                        Title = "Turn " + (on ? "off " : "on ") + name + " " + pw.Device, Detail = Src + " · power " + pw.Status, Glyph = "", Source = Src,
                        Words = words + " power plug " + (on ? "off" : "on"), Ask = on, Run = delegate { return Moonraker.PowerAsync(id, pw.Device, !on); }
                    });
                }
            }
        }
    }
}
