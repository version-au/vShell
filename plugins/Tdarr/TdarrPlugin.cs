using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("tdarr", "Tdarr", Description = "What Tdarr is transcoding and health checking: a module with the running jobs, the nodes (pause, worker limits) and the library totals, notifications, and !td in the run box.",
    Glyph = "", Group = "Media", Author = "version", Adds = "!td in the run box (jobs, pause / resume nodes), notifications, palette commands", Types = "Tdarr")]

namespace vShell.Ui
{
    /// <summary>
    /// The Tdarr plugin: the module, the Plugins and Notifications sections, the notifications, and !td in the run box (the running jobs,
    /// and pausing / resuming the nodes).
    /// </summary>
    public sealed class TdarrPlugin : IPlugin
    {
        public const string Type = "Tdarr";
        Action<string, string, string, string> _notice;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Tdarr transcoding", ShortName = "Tdarr", Glyph = "", Group = "Smart home & more",
                Blurb = "jobs, nodes, space saved", HasPopup = true,
                Needs = delegate (BarSettings s) { return TdarrClient.Configured(s) ? null : "Needs the Tdarr connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new TdarrModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(TdarrClient.Configured);
            host.AddIntegration("Tdarr", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(TdarrClient.Configure);

            host.AddNotifyKind("Tdarr.Error", NotifyLevel.Important);
            host.AddNotifyKind("Tdarr.Node", NotifyLevel.Alert);
            host.AddNotifyKind("Tdarr.Done", NotifyLevel.Normal);
            host.AddNotifyKind("Tdarr.Summary", NotifyLevel.Normal);
            host.AddNotifySource("Tdarr");
            _notice = delegate (string kind, string title, string detail, string url) { Notify.Post("Tdarr", title, detail, url, Notify.KindOf(kind)); };
            TdarrClient.Notice += _notice;

            host.AddBang("Tdarr", delegate (string key, string q, BarSettings s)
            {
                TdSettings c = TdSettings.Of(s);
                if (key != c.Bang || string.IsNullOrWhiteSpace(c.Url)) return null;
                return new RunAction { Kind = "Tdarr", Query = q, Display = "Tdarr: the running jobs and nodes" };
            }, Rows, delegate (BarSettings s) { return TdarrClient.Configured(s) ? "!" + TdSettings.Of(s).Bang + " Tdarr" : null; });
            host.AddCommands("Tdarr", Commands);
        }

        // =================================================================
        // The command palette: pause / resume the nodes, open Tdarr
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!TdarrClient.Configured(s)) return;
            const string Src = "Tdarr";
            string web = TdarrClient.PageUrl(s);
            into.Add(new PaletteCommand
            {
                Title = "Open Tdarr", Detail = Src, Glyph = "", Source = Src, Words = "tdarr transcode web page", Opens = true,
                Run = delegate { RunPanel.Open(web); return System.Threading.Tasks.Task.FromResult(""); }
            });
            TdarrSnapshot snap = TdarrClient.Latest;
            if (snap == null || !snap.Ok) return;
            foreach (TdarrNode n0 in snap.Nodes)
            {
                TdarrNode n = n0;
                bool pause = !n.Paused;
                into.Add(new PaletteCommand
                {
                    Title = (pause ? "Pause" : "Resume") + " Tdarr node " + n.Name, Detail = Src + " · " + (n.Paused ? "paused" : n.Running + " running"),
                    Glyph = pause ? "" : "", Source = Src, Words = "tdarr node worker transcode " + (pause ? "stop hold" : "start continue unpause"),
                    Run = async delegate { string err = await TdarrClient.PauseAsync(s, n, pause); TdarrClient.Poke(); return err ?? PaletteCommand.Worked((pause ? "Paused " : "Resumed ") + n.Name); }
                });
            }
            if (snap.Stats.Errors > 0)
                into.Add(new PaletteCommand
                {
                    Title = "Requeue every failed file in Tdarr", Detail = Src + " · " + snap.Stats.Errors + " failed", Glyph = "", Source = Src,
                    Words = "tdarr errors failed retry again transcode health check", Ask = true,
                    Run = async delegate
                    {
                        KeyValuePair<int, string> r = await TdarrClient.RequeueAllAsync(s);
                        return r.Value ?? PaletteCommand.Worked(r.Key == 1 ? "1 file is back in the queue" : r.Key + " files are back in the queue");
                    }
                });
            if (snap.Nodes.Count > 1)
                foreach (bool pause0 in new[] { true, false })
                {
                    bool pause = pause0;
                    List<TdarrNode> nodes = snap.Nodes.FindAll(delegate (TdarrNode n) { return n.Paused != pause; });
                    if (nodes.Count == 0) continue;
                    into.Add(new PaletteCommand
                    {
                        Title = (pause ? "Pause" : "Resume") + " every Tdarr node", Detail = Src + " · " + nodes.Count + " nodes", Glyph = pause ? "" : "", Source = Src,
                        Words = "tdarr all nodes workers transcode", Ask = pause,
                        Run = async delegate
                        {
                            foreach (TdarrNode n in nodes) { string err = await TdarrClient.PauseAsync(s, n, pause); if (err != null) return err; }
                            TdarrClient.Poke();
                            return PaletteCommand.Worked((pause ? "Paused " : "Resumed ") + nodes.Count + " nodes");
                        }
                    });
                }
        }

        public void Unload()
        {
            if (_notice != null) TdarrClient.Notice -= _notice;
            TdarrClient.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            TdSettings c = TdSettings.Of(S);
            p.Header("Tdarr");
            p.Text("Address of the Tdarr server (the web UI's, for example http://192.168.1.20:8265)", delegate { return c.Url; }, delegate (string v) { c.Url = v; });
            PasswordBox pw = p.Secret("API key", "API key (only with authentication on)", delegate { return c.ApiKey; }, delegate (string v) { c.ApiKey = v; },
                                      "Only needed when Tdarr has authentication turned on: make one in Tdarr under Tools > API Keys. It is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "!" + c.Bang + " in a run box shows what's running and the nodes - Enter on a node pauses or resumes it.");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.ApiKey = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return TdarrClient.TestAsync(S); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            TdSettings c = TdSettings.Of(S);
            p.NotifySection("Tdarr notifications", TdarrClient.Configured(S), "Tdarr");
            p.Check("A file failed (a transcode or health check error)", delegate { return c.NotifyErrors; }, delegate (bool v) { c.NotifyErrors = v; p.Redraw(); });
            if (c.NotifyErrors) p.Kind("Errors show as", "Tdarr.Error");
            p.Check("A node disconnected or came back", delegate { return c.NotifyNodes; }, delegate (bool v) { c.NotifyNodes = v; p.Redraw(); });
            if (c.NotifyNodes) p.Kind("Nodes show as", "Tdarr.Node");
            p.Check("Tdarr finished its whole queue", delegate { return c.NotifyDone; }, delegate (bool v) { c.NotifyDone = v; p.Redraw(); });
            if (c.NotifyDone) p.Kind("Finished shows as", "Tdarr.Done");
            p.Check("Once a day, what Tdarr did (the first check after 9 AM: files done, space saved, failures)", delegate { return c.NotifySummary; }, delegate (bool v) { c.NotifySummary = v; p.Redraw(); });
            if (c.NotifySummary) p.Kind("The daily summary shows as", "Tdarr.Summary");
            p.Note("Notifications work without a Tdarr module too (vShell then checks every minute instead of every 5 seconds).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            TdView v = new TdView(cfg);
            if (!TdarrClient.Configured(p.Settings)) p.Note("Set up the Tdarr connection first (Plugins page in this window).");
            p.Check("Running jobs", delegate { return v.ShowWorkers; }, delegate (bool on) { v.ShowWorkers = on; p.Redraw(); });
            if (v.ShowWorkers || v.ShowFailed) p.Slider("How many jobs / failed files", 1, 20, delegate { return v.Count; }, delegate (double d) { v.Count = (int)d; });
            p.Check("Nodes (pause / resume, worker limits)", delegate { return v.ShowNodes; }, delegate (bool on) { v.ShowNodes = on; });
            p.Check("Failed files (requeue one, or all)", delegate { return v.ShowFailed; }, delegate (bool on) { v.ShowFailed = on; p.Redraw(); });
            p.Check("Library totals (queue, done, errors, space saved)", delegate { return v.ShowStats; }, delegate (bool on) { v.ShowStats = on; });
            p.Check("Show the \"Tdarr\" button", delegate { return v.ShowOpen; }, delegate (bool on) { v.ShowOpen = on; });
            if (!p.IsWidget)
            {
                p.Check("Badge with the number of jobs running", delegate { return v.Badge; }, delegate (bool on) { v.Badge = on; });
                p.Check("Show how many files are waiting", delegate { return v.BarQueue; }, delegate (bool on) { v.BarQueue = on; });
                p.Check("Hide while nothing is running", delegate { return v.HideIdle; }, delegate (bool on) { v.HideIdle = on; });
                p.Note("It still shows when Tdarr can't be reached.");
                p.Title(cfg, "");
            }
            p.Note((p.IsWidget ? "" : "On a bar: the icon with a badge for the jobs running (hover for the list) - click it for the popup. ") +
                   "Right-click a job to cancel it; the button on a node pauses or resumes it, and right-click a node to change its worker limits.");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(4, ModulePics.Badge(ModulePics.G(0xE895, t.Fg, 16), "2", t.MainAccent, 8), ModulePics.T(t, "312 queued", t.FgDim, 12.5));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            return ModulePics.Col(6,
                ModulePics.Pair(t, "Movie.2024.mkv", "64% · 0:05:12"),
                ModulePics.Meter(t, 176, 4, 0.64, acc),
                ModulePics.Pair(t, "Show.S01E04.mkv", "21% · 0:14:40"),
                ModulePics.Meter(t, 176, 4, 0.21, acc),
                ModulePics.Row(10, ModulePics.T(t, "312", fg, 11, true), ModulePics.T(t, "1,840", StatusColors.Good, 11, true), ModulePics.T(t, "1.2 TB", StatusColors.Good, 11, true)),
                ModulePics.Row(10, ModulePics.T(t, "queued", dim, 9.5), ModulePics.T(t, "done", dim, 9.5), ModulePics.T(t, "saved", dim, 9.5)));
        }

        // =================================================================
        // The run box: !td -> the running jobs, then the nodes (Enter pauses / resumes one)
        // =================================================================
        sealed class BoxState
        {
            public System.Windows.Threading.DispatcherTimer Timer;
            public string Done;
        }

        static void Rows(RunPanel box, RunAction a)
        {
            object o;
            BoxState st;
            if (box.PluginState.TryGetValue("tdarr", out o)) st = (BoxState)o;
            else { st = new BoxState(); box.PluginState["tdarr"] = st; }
            BarSettings S = box.Settings;
            if (st.Timer == null)
            {
                TdarrClient.Poke();
                st.Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += delegate
                {
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != "Tdarr" || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            if (!TdarrClient.Configured(S)) { box.Status("Set up Tdarr on the Plugins page in Settings first."); return; }
            TdarrSnapshot snap = TdarrClient.Latest;
            if (!snap.Ok) { box.Status(snap.Error.Length > 0 ? "Tdarr: " + snap.Error : "Asking Tdarr…"); return; }
            string glyph = TdarrList.G(TdarrList.Glyph), url = TdarrClient.PageUrl(S);
            int k = 0;
            foreach (TdarrWorker w in snap.Workers)
            {
                if (k++ >= 6) break;
                box.AddRow(glyph, w.Name, Math.Floor(w.Percent) + "%" + (w.Eta.Length > 0 ? " · " + w.Eta : "") + " · " + w.TypeText,
                           delegate { box.Remember(box.Text); box.Close(); RunPanel.Open(url); }, null);
            }
            foreach (TdarrNode n in snap.Nodes)
            {
                TdarrNode node = n;
                box.AddRow(((char)(n.Paused ? 0xE768 : 0xE769)).ToString(), (n.Paused ? "Resume " : "Pause ") + n.Name,
                           n.Paused ? "paused" : n.Running > 0 ? n.Running + " running" : "idle", delegate { Pause(box, st, node); }, null);
            }
            string done = st.Done;
            st.Done = null;
            box.Status((done != null ? done + "  ·  " : "") + (snap.Workers.Count == 0 ? "Nothing running" : snap.Workers.Count + " running") + " · " + snap.Stats.Queue + " in the queue" +
                       (snap.Stats.Errors > 0 ? " · " + snap.Stats.Errors + " errors" : "") + "  ·  Enter on a node: pause / resume it");
            box.Select(0);
        }

        static async void Pause(RunPanel box, BoxState st, TdarrNode n)
        {
            box.Status((n.Paused ? "Resuming " : "Pausing ") + n.Name + "…");
            string err = await TdarrClient.PauseAsync(box.Settings, n, !n.Paused);
            if (err != null) { box.Status("Tdarr: " + err); return; }
            box.Remember(box.Text);
            st.Done = (n.Paused ? "Resumed " : "Paused ") + n.Name + " ✓";
            box.Redraw();
        }
    }
}
