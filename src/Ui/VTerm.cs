using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The terminal is vTerm, a vSuite app of its own (since 0.38.194). vShell asks it over vSuite's TermLink pipe: the drop-down from a bar
    /// (Terminal module, its keys), Open shell (TermOpen.Host - Komodo, Proxmox, Linux Updates), the palette's connections. vShell works
    /// out where the drop-down goes and how it looks (the bar's edge and width, corners, outline, blur); vTerm owns the window, the tabs
    /// and the shells. vTerm not running: started in the background (--background) and asked again. Not installed: offers Settings >
    /// Updates > vSuite apps, where it installs.
    /// </summary>
    public static class VTerm
    {
        public const string Name = "vTerm";
        /// <summary>vTerm's installer id (installer\vTerm.iss's AppId).</summary>
        public const string InnoId = "{06CD4D4D-4445-41EA-9BDF-29EDF1E0FFFD}";

        /// <summary>vTerm.exe: VSHELL_VTERM_EXE (tests, a build folder), else the installed one; null = not installed.</summary>
        public static string Exe
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("VSHELL_VTERM_EXE");
                if (!string.IsNullOrWhiteSpace(over) && File.Exists(over)) return over;
                return SettingsWindow.FindSuiteApp(Name, InnoId);
            }
        }

        /// <summary>vTerm's pipe: the usual settings folder, or VTERM_DATA_DIR (a test vTerm started from here gets the same variable).</summary>
        static string Pipe
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable("VTERM_DATA_DIR");
                return string.IsNullOrWhiteSpace(dir) ? vSuite.TermLink.DefaultPipe : vSuite.TermLink.PipeName(dir);
            }
        }

        /// <summary>vTerm's profiles (its settings), else the ones this PC has - for the module's "New tabs start with".</summary>
        public static System.Collections.Generic.List<TermProfile> Profiles()
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable("VTERM_DATA_DIR");
                string file = string.IsNullOrWhiteSpace(dir) ? vSuite.SuiteApps.SettingsFile(Name) : Path.Combine(dir, "settings.json");
                if (File.Exists(file))
                    using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)))
                    {
                        System.Text.Json.JsonElement p;
                        if (doc.RootElement.TryGetProperty("Profiles", out p) && p.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var l = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<TermProfile>>(p.GetRawText());
                            if (l != null) l.RemoveAll(delegate (TermProfile x) { return x == null || string.IsNullOrWhiteSpace(x.Name); });
                            if (l != null && l.Count > 0) return l;
                        }
                    }
            }
            catch (Exception ex) { Log.Write("vTerm's profiles: " + ex.Message); }
            return TermProfile.Defaults();
        }

        static bool _starting;

        /// <summary>
        /// Send a request to vTerm (on a worker thread), starting vTerm when it isn't running; done gets the answer on the UI thread (null =
        /// vTerm couldn't be reached - it's said why already).
        /// </summary>
        public static void Send(BarManager mgr, vSuite.TermRequest r, Action<string> done = null)
        {
            System.Windows.Threading.Dispatcher ui = Application.Current.Dispatcher;
            Task.Run(delegate
            {
                string answer = vSuite.TermLink.Send(Pipe, r, 3000);
                if (answer == null)
                {
                    string exe = Exe;
                    if (exe == null)
                    {
                        ui.BeginInvoke(new Action(delegate { NotInstalled(mgr); if (done != null) done(null); }));
                        return;
                    }
                    if (_starting) return;      // a start is under way: this press is dropped (the first one shows it)
                    _starting = true;
                    try
                    {
                        Log.Write("vTerm isn't running - starting it: " + exe);
                        Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                        // up to 15 s for it to start listening (the first start sets up its browser part)
                        var sw = Stopwatch.StartNew();
                        while (answer == null && sw.ElapsedMilliseconds < 15000)
                        {
                            System.Threading.Thread.Sleep(250);
                            answer = vSuite.TermLink.Send(Pipe, r, 3000);
                        }
                    }
                    catch (Exception ex) { Log.Write("vTerm start: " + ex.Message); }
                    finally { _starting = false; }
                    if (answer == null)
                    {
                        ui.BeginInvoke(new Action(delegate
                        {
                            Questions.Tell(null, "vTerm didn't answer, so the terminal can't open. Try starting vTerm on its own (" + exe + ") - if it won't " +
                                                 "start, its log is in %APPDATA%\\vTerm.", "Terminal", MessageBoxImage.Warning);
                            if (done != null) done(null);
                        }));
                        return;
                    }
                }
                if (answer.StartsWith("error:")) Log.Write("vTerm said: " + answer);
                if (done != null) ui.BeginInvoke(new Action(delegate { done(answer); }));
            });
        }

        static DateTime _askedAt = DateTime.MinValue;

        static void NotInstalled(BarManager mgr)
        {
            if ((DateTime.UtcNow - _askedAt).TotalSeconds < 3) return;
            _askedAt = DateTime.UtcNow;
            var res = Questions.Ask(null, "The terminal is vTerm now - a vSuite app of its own, with the same tabs, profiles and saved connections " +
                                          "(it brings vShell's over the first time it starts).\n\nvTerm isn't installed yet. Install it from Settings > " +
                                          "Updates > vSuite apps?", "Terminal", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (res == MessageBoxResult.Yes) mgr.ShowVSuiteApps();
        }

        /// <summary>vShell is closing: vTerm ends too unless it's in use (a window open, or shells running in the drop-down).</summary>
        public static void QuitIfIdle()
        {
            try { vSuite.TermLink.Send(Pipe, new vSuite.TermRequest { Cmd = "quit-if-idle" }, 400); } catch { }
        }

        // =================================================================
        // The drop-down's place and look, worked out from the bar
        // =================================================================
        /// <summary>
        /// A request with the drop-down's place and look for this bar and Terminal module (cfg null = the defaults). Attached (the module's
        /// "Attach it to the bar"): flush against the bar's edge, as wide as the bar's straight edge at most; otherwise centred on the
        /// monitor, under a top bar / over a bottom bar. Height from the module either way.
        /// </summary>
        public static vSuite.TermRequest DropFor(BarManager mgr, BarWindow bar, ModuleConfig cfg, string cmd)
        {
            var r = new vSuite.TermRequest { Cmd = cmd };
            if (cfg == null) cfg = ModuleConfig.Create(ModuleTypes.Terminal);
            r.Profile = cfg.TermProfile ?? "";
            r.HideOnLeave = cfg.TermHideOnLeave;
            if (bar == null) return r;
            RECT hr = bar.BarRect;
            if (hr.Width <= 0 && !Native.GetWindowRect(bar.Handle, out hr)) return r;
            MonitorInfo mon = MonitorHelper.FromPoint((hr.Left + hr.Right) / 2, (hr.Top + hr.Bottom) / 2) ?? MonitorHelper.Primary();
            if (mon == null) return r;
            RECT wa = mon.WorkArea.Width > 0 ? mon.WorkArea : mon.Bounds;
            double scale = Math.Max(0.5, (bar.Handle != IntPtr.Zero ? Native.GetDpiForWindow(bar.Handle) : 96) / 96.0);
            int gap = (int)Math.Round(8 * scale);
            bool attached = cfg.TermAttach, fromTop = bar.PopupsBelow;
            int h = (int)Math.Round(mon.Bounds.Height * Math.Max(15, Math.Min(95, cfg.TermHeight)) / 100.0);
            int w = (int)Math.Round(mon.Bounds.Width * Math.Max(20, Math.Min(100, cfg.TermWidth)) / 100.0);
            if (attached)
            {
                int inset = (int)Math.Round(Math.Max(0, bar.Placement.CornerRadius) * scale);
                w = Math.Min(w, Math.Max(100, hr.Width - 2 * inset));
                h = Math.Min(h, Math.Max(100, (fromTop ? mon.Bounds.Bottom - hr.Bottom : hr.Top - mon.Bounds.Top) - gap));
                r.X = hr.Left + (hr.Width - w) / 2;
                r.Y = fromTop ? hr.Bottom : hr.Top - h;
            }
            else
            {
                w = Math.Min(w, wa.Width - 2 * gap);
                h = Math.Min(h, wa.Height - 2 * gap);
                r.X = wa.Left + (wa.Width - w) / 2;
                int y = fromTop ? Math.Max(wa.Top + gap, hr.Bottom + gap) : Math.Min(wa.Bottom - gap - h, hr.Top - gap - h);
                r.Y = Math.Max(wa.Top, Math.Min(wa.Bottom - h, y));
            }
            r.W = w;
            r.H = h;
            r.FromTop = fromTop;
            r.Attached = attached;
            r.Bar = bar.Handle.ToInt64();

            // the look: an attached drop-down takes the bar's corners and outline, a free one the widget style's
            BarSettings s = mgr.Settings;
            WidgetStyle ws = s.WidgetStyle;
            Theme t = bar.Theme.ForModule(cfg);
            if (attached)
            {
                r.Radius = Math.Max(0, bar.Placement.CornerRadius);
                r.BorderThickness = bar.Placement.ShowBorder ? Math.Max(0.5, Math.Min(12, bar.Placement.BorderThickness)) : 0;
                r.BorderColor = Hex(Theme.Faded(t.Resolve(bar.Placement.BorderColor, t.MainAccent), bar.Placement.BorderOpacity).Color);
            }
            else
            {
                r.Radius = ws != null ? Math.Max(0, ws.CornerRadius) : 10;
                r.BorderThickness = ws != null ? (ws.ShowBorder ? Math.Max(0.5, Math.Min(12, ws.BorderThickness)) : 0) : 1;
                r.BorderColor = ws != null ? Hex(Theme.Faded(t.Resolve(ws.BorderColor, t.MainAccent), ws.BorderOpacity).Color) : Hex(t.Track.Color);
            }
            r.Shadow = ws == null || ws.ShowShadow;
            r.Blur = cfg.PopupBackdrop ? PopupGlass.ModeFor(mgr, bar) : "Off";
            return r;
        }

        static string Hex(Color c) { return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        /// <summary>Is there a Terminal module on any bar? (Without one there's no drop-down to open things in - "Open shell" uses a vTerm window.)</summary>
        public static bool HasTerminalModule(BarManager mgr)
        {
            foreach (BarWindow b in mgr.BarWindows)
                foreach (TerminalModule t in b.Terminals) return true;
            return false;
        }

        /// <summary>The bar and Terminal module the drop-down comes from when it isn't a module asking (Open shell, the palette): the first bar
        /// with a Terminal module, else the first bar.</summary>
        public static vSuite.TermRequest DropForFirst(BarManager mgr, string cmd)
        {
            foreach (BarWindow b in mgr.BarWindows)
                foreach (TerminalModule t in b.Terminals) return DropFor(mgr, b, t.Config, cmd);
            foreach (BarWindow b in mgr.BarWindows) return DropFor(mgr, b, null, cmd);
            return new vSuite.TermRequest { Cmd = cmd };
        }
    }
}
