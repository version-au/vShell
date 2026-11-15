using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("komodo", "Komodo", Description = "Docker stacks, deployments, containers and procedures in Komodo (komo.do): a module, notifications, !ko in the run box and palette commands.",
    Glyph = "\uE7B8", Group = "Servers & network", Author = "version", Adds = "!ko in the run box, notifications, palette commands", Types = "Komodo")]

namespace vShell.Ui
{
    /// <summary>
    /// The Komodo plugin: everything vShell has for Komodo, registered with the host - the module, the Integrations and Notifications
    /// sections, !ko in the run box, the palette's commands and the notifications.
    /// </summary>
    public sealed class KomodoPlugin : IPlugin
    {
        Action<string, string, string, bool> _notice;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.Komodo, Display = "Komodo (Docker)", ShortName = "Komodo", Glyph = "", Group = "Smart home & more",
                Blurb = "Docker stacks, containers", HasPopup = true,
                Needs = delegate (BarSettings s) { return Komodo.Configured(s) ? null : "Needs the Komodo connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new KomodoModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(Komodo.Configured);
            host.AddIntegration("Komodo", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(Komodo.Configure);

            host.AddNotifyKind("Komodo.Problem", NotifyLevel.Alert);
            host.AddNotifyKind("Komodo.Update", NotifyLevel.Normal);
            host.AddNotifyKind("Komodo.Good", NotifyLevel.Normal);
            host.AddNotifySource("Komodo");
            _notice = delegate (string title, string detail, string url, bool bad)
            {
                string k = bad ? "Komodo.Problem" : title != null && title.StartsWith("⬆") ? "Komodo.Update" : "Komodo.Good";
                Notify.Post("Komodo", title, detail, url, Notify.KindOf(k));
            };
            Komodo.Notice += _notice;

            host.AddCommands("Komodo", Commands);
            host.AddBang("Komodo", delegate (string key, string q, BarSettings s)
            {
                if (key != KoSettings.Of(s).Bang || string.IsNullOrWhiteSpace(KoSettings.Of(s).Url)) return null;
                return new RunAction { Kind = "Komodo", Query = q, Display = q.Length == 0 ? "Komodo: problems and procedures" : "Komodo: matching " + q };
            }, Rows, delegate (BarSettings s) { return Komodo.Configured(s) ? "!" + KoSettings.Of(s).Bang + " Komodo" : null; });
        }

        public void Unload()
        {
            if (_notice != null) Komodo.Notice -= _notice;
            Komodo.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            KoSettings k = KoSettings.Of(S);
            p.Header("Komodo");
            p.Text("Address (for example http://192.168.1.20:9120 or https://komodo.example.com)", delegate { return k.Url; }, delegate (string v) { k.Url = v.Trim(); });
            p.Text("API key (K-…)", delegate { return k.Key; }, delegate (string v) { k.Key = v.Trim(); });
            PasswordBox kpw = p.Secret("API secret (S-…)", "secret", delegate { return k.Secret; }, delegate (string v) { k.Secret = v; },
                   "In Komodo open Settings > API Keys > New API Key (or create one on a Service User's page, giving it only the permissions you want vShell to have). " +
                   "Copy the key (starts with K-) into the box above and the secret (starts with S-) here - the secret is only shown once. " +
                   "The secret is stored encrypted for your Windows account. Starting, stopping and redeploying need Execute permission on those stacks / deployments.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return k.IgnoreCert; }, delegate (bool v) { k.IgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return k.Bang; }, delegate (string v) { k.Bang = v; },
                   "!" + k.Bang + " - everything that isn't running properly (stopped, unhealthy, image update waiting) plus your procedures.\n" +
                   "!" + k.Bang + " media - stacks, deployments, containers and procedures whose name contains \"media\", with their state.\n" +
                   "Enter on one shows its actions: Start, Stop, Restart, Pause, Redeploy / Pull and redeploy, Pull, Destroy, Run (procedures), Open in Komodo. " +
                   "Actions that interrupt (stop, restart, redeploy, destroy, run) need Enter twice. \"Back to the list\" returns; typing more searches again.");
            p.Test(delegate
            {
                if (kpw.Password.Trim().Length > 0) { k.Secret = Secret.Protect(kpw.Password.Trim()); kpw.Password = ""; p.Changed(); }
            }, delegate { return Komodo.TestAsync(S); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            KoSettings k = KoSettings.Of(S);
            p.NotifySection("Komodo notifications", Komodo.Configured(S), "Komodo");
            p.Check("A stack / deployment / container stops by itself or turns unhealthy, or a server can't be reached (and when it's back)", delegate { return k.NotifyDown; }, delegate (bool v) { k.NotifyDown = v; p.Redraw(); });
            if (k.NotifyDown) p.Delay(delegate { return k.NotifyDelay; }, delegate (int v) { k.NotifyDelay = v; });
            p.Check("A newer image is available for a stack or deployment", delegate { return k.NotifyUpdates; }, delegate (bool v) { k.NotifyUpdates = v; p.Redraw(); });
            if (k.NotifyUpdates) p.Kind("Updates show as", "Komodo.Update");
            p.Check("A procedure fails (and one you ran from vShell finishes)", delegate { return k.NotifyProcedures; }, delegate (bool v) { k.NotifyProcedures = v; p.Redraw(); });
            if (k.NotifyDown || k.NotifyProcedures)
            {
                p.Kind("Problems (stopped, unhealthy, can't be reached, a procedure failed) show as", "Komodo.Problem");
                p.Kind("Good news (running again, back, a procedure finished) shows as", "Komodo.Good");
            }
            p.Note("Notifications work without a Komodo module too (vShell then checks every 30 seconds instead of every 5). Stopping something from vShell doesn't give a notification.");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            KoView ko = new KoView(cfg);
            if (!Komodo.Configured(p.Settings)) p.Note("Set up the Komodo connection first (Plugins page in this window).");
            p.Combo("Show", KoView.Shows, delegate { return ko.Show; }, delegate (string v) { ko.Show = v; });
            p.Note("Managed = Komodo's stacks and deployments; Containers = every Docker container on your servers; Both = stacks, deployments and the containers Komodo doesn't manage.");
            p.Combo("Which", KoView.Filters, delegate { return ko.Filter; }, delegate (string v) { ko.Filter = v; });
            var servers = new List<string> { "All servers" };
            foreach (KoServer sv in Komodo.Latest.Servers) if (!servers.Contains(sv.Name)) servers.Add(sv.Name);
            if (ko.Server.Length > 0 && !servers.Contains(ko.Server)) servers.Add(ko.Server);
            p.Combo("Server", servers.ToArray(), delegate { return ko.Server.Length == 0 ? "All servers" : ko.Server; },
                    delegate (string v) { ko.Server = v == "All servers" ? "" : v; p.Redraw(); });     // grouping only matters for all servers
            bool grouped = ko.Group && ko.Server.Length == 0;
            if (ko.Server.Length == 0) p.Check("Group by server", delegate { return ko.Group; }, delegate (bool v) { ko.Group = v; p.Redraw(); });
            p.Check("Show the procedures (with a Run button)", delegate { return ko.ShowProcedures; }, delegate (bool v) { ko.ShowProcedures = v; });
            p.Check("Show the \"Komodo\" button", delegate { return ko.ShowOpen; }, delegate (bool v) { ko.ShowOpen = v; });
            p.Slider(grouped ? "How many per server" : "How many", 1, 100, delegate { return ko.Count; }, delegate (double v) { ko.Count = (int)v; });
            if (!p.IsWidget) p.Title(cfg, "");
            p.Note((p.IsWidget ? "" : "On a bar: a dot (green all running, amber busy, red a problem), running / total, ⚠ problems and ⬆ image updates - click it for the list. ") +
                   "In the list click a stack, deployment or container for Start / Stop / Restart / Pause / Redeploy / Pull / Destroy (asks first for the ones that interrupt), " +
                   "a stack's arrow for its services (each with its own actions), and Run for a procedure.");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            double fs = 12.5;
            return ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 9), ModulePics.T(t, "18/19 running", t.Fg, fs), ModulePics.T(t, "⚠ 1", StatusColors.Bad, fs), ModulePics.T(t, "⬆ 2", t.MainAccent, fs));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim;
            return ModulePics.Col(5, ModulePics.T(t, "docker01", dim, 11),
                                  ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "media", fg, 12), ModulePics.T(t, "running", dim, 11)),
                                  ModulePics.Row(6, ModulePics.Dot(StatusColors.Bad, 7), ModulePics.T(t, "immich", fg, 12), ModulePics.T(t, "unhealthy", StatusColors.Bad, 11)),
                                  ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "pihole ⬆", fg, 12), ModulePics.T(t, "running", dim, 11)),
                                  ModulePics.Row(6, ModulePics.Dot(ModulePics.Shade(t, 80), 7), ModulePics.T(t, "grafana", fg, 12), ModulePics.T(t, "stopped", dim, 11)));
        }

        // =================================================================
        // The command palette: the actions of every stack, deployment, container and procedure
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!Komodo.Configured(s)) return;
            KoSnapshot snap = Komodo.Latest;
            if (snap == null || !snap.Ok) return;
            const string Src = "Komodo";
            var items = new List<KoItem>();
            items.AddRange(snap.Stacks);
            items.AddRange(snap.Deployments);
            items.AddRange(snap.Containers.FindAll(delegate (KoItem c) { return c.Owner.Length == 0; }));
            items.AddRange(snap.Procedures);
            foreach (KoItem it0 in items)
            {
                KoItem it = it0;
                string detail = Src + (it.ServerName.Length > 0 ? " · " + it.ServerName : "") + " · " + it.State.Replace('_', ' ') + (it.Update ? " · update" : "");
                foreach (string[] act in Komodo.Actions(it))
                {
                    if (act[0] == "-") continue;
                    string[] ac = act;
                    string label = ac[0];
                    string title = it.Kind == "procedure" ? "Run procedure " + it.Name : label + " " + it.Kind + " " + it.Name;
                    string glyph = label.StartsWith("Stop") || label.StartsWith("Destroy") ? "" : label.StartsWith("Restart") ? "" :
                                   label.StartsWith("Pull") || label.Contains("deploy") || label.StartsWith("Deploy") ? "" : "";
                    string syn = label.StartsWith("Restart") ? " reboot" : label.StartsWith("Stop") ? " shut down" : label.StartsWith("Start") ? " boot up" : label.Contains("update") ? " upgrade" : "";
                    into.Add(new PaletteCommand
                    {
                        Title = title, Detail = detail, Glyph = glyph, Source = Src, Ask = ac.Length > 2 && ac[2] == "ask",
                        Words = it.ServerName + " docker" + syn + (it.Kind == "stack" ? " compose" : ""),
                        Boost = label.StartsWith("Restart") || label.StartsWith("Start") ? 1 : 0,
                        Run = async delegate { string err = await Komodo.RunAsync(it, ac[1], null); Komodo.Poke(); return err; }
                    });
                }
            }
        }

        // =================================================================
        // The run box: !ko name -> matching stacks / deployments / containers / procedures; Enter on one -> its actions
        // =================================================================
        /// <summary>What one run box shows for !ko.</summary>
        sealed class BoxState
        {
            public KoItem Item;          // the item whose actions are shown (null = the list)
            public string ItemQuery;     // ... for this query (typing something else goes back to the list)
            public string Armed;         // an action that asks first: Enter once arms it, Enter again does it
            public System.Windows.Threading.DispatcherTimer Timer;
        }

        static void Rows(RunPanel box, RunAction a)
        {
            object o;
            BoxState st;
            if (box.PluginState.TryGetValue("komodo", out o)) st = (BoxState)o;
            else { st = new BoxState(); box.PluginState["komodo"] = st; }
            BarSettings S = box.Settings;
            Theme t = box.Theme;

            string q = a.Query;
            // "!ko logs immich": Enter on a match opens its log straight away
            bool logs = false;
            foreach (string w in new[] { "logs ", "log " })
                if (q.StartsWith(w, StringComparison.OrdinalIgnoreCase)) { logs = true; q = q.Substring(w.Length).Trim(); break; }
            if (q.Equals("logs", StringComparison.OrdinalIgnoreCase) || q.Equals("log", StringComparison.OrdinalIgnoreCase)) { logs = true; q = ""; }
            KoSnapshot snap = Komodo.Latest;
            if (st.Timer == null)
            {
                Komodo.Poke();
                st.Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += delegate
                {
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != "Komodo" || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; st.Item = null; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            if (!Komodo.Configured(S)) { box.Status("Set up Komodo on the Plugins page in Settings first."); return; }
            if (!snap.Ok) { box.Status(snap.Error.Length > 0 ? "Komodo: " + snap.Error : "Asking Komodo…"); return; }
            if (st.Item != null && st.ItemQuery != q) st.Item = null;

            if (st.Item != null)
            {
                // the chosen item's actions (fresh state from the latest snapshot)
                KoItem it = FindKo(snap, st.Item) ?? st.Item;
                st.Item = it;
                foreach (string[] act in Komodo.Actions(it))
                {
                    if (act[0] == "-") continue;
                    string[] ac = act;
                    string armKey = it.Key + "|" + ac[1];
                    bool armed = st.Armed == armKey;
                    bool ask = ac.Length > 2 && ac[2] == "ask";
                    RunPanel.Row r = box.AddRow(ac[1].StartsWith("Stop") || ac[1].StartsWith("Destroy") ? "" : ac[1].StartsWith("Restart") ? "" : ac[1].StartsWith("Deploy") || ac[1].StartsWith("Pull") ? "" : "",
                                   armed ? "Press Enter again: " + ac[0].ToLowerInvariant() + " " + it.Name : ac[0], armed ? "confirm" : null, delegate
                    {
                        if (ask && st.Armed != armKey) { st.Armed = armKey; int keep = box.Selected; box.Redraw(); box.Select(keep); return; }
                        st.Armed = null;
                        RunKo(box, it, ac);
                    }, null);
                    if (armed) r.View.Background = Theme.Faded(t.MainAccent, 25);
                }
                if (it.Kind != "procedure")
                {
                    KoItem li = it;
                    box.AddRow("", "Show log", "last 100 lines", delegate { box.Remember(box.Text); box.Close(); KomodoLogWindow.Show(t, li, null); }, null);
                }
                string url = Komodo.PageUrl(S, it);
                box.AddRow("", "Open in Komodo", null, delegate { box.Remember(box.Text); box.Close(); try { RunPanel.Open(url); } catch { } }, null);
                box.AddRow("", "Back to the list", null, delegate { st.Item = null; st.Armed = null; box.Redraw(); }, null);
                box.Status(it.Name + " (" + it.Kind + (it.ServerName.Length > 0 ? " on " + it.ServerName : "") + ") - " + it.State.Replace('_', ' ') +
                           (it.Status.Length > 0 ? " · " + it.Status : "") + (it.Update ? " · image update available" : ""));
                box.Select(0);
                return;
            }

            // the list: everything matching the text; with no text the problems and the procedures
            var all = new List<KoItem>();
            all.AddRange(snap.Stacks);
            all.AddRange(snap.Deployments);
            all.AddRange(snap.Containers.FindAll(delegate (KoItem c) { return c.Owner.Length == 0; }));
            if (!logs) all.AddRange(snap.Procedures);
            List<KoItem> hits = q.Length == 0 && !logs
                ? all.FindAll(delegate (KoItem i) { return i.Kind == "procedure" || i.Health == "bad" || i.Health == "busy" || i.Update || (i.Health == "off" && i.Kind != "container"); })
                : all.FindAll(delegate (KoItem i) { return i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; });
            hits.Sort(delegate (KoItem x, KoItem y)
            {
                bool px = x.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase), py = y.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase);
                if (px != py) return px ? -1 : 1;
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            int n = 0;
            foreach (KoItem it in hits)
            {
                if (n++ >= 9) break;
                KoItem item = it;
                string glyph = it.Kind == "procedure" ? "" : it.Health == "good" ? "" : it.Health == "bad" ? "" : "";
                string tag = it.Kind + " · " + (it.Kind == "procedure" ? (it.State == "running" ? "running" : Komodo.Ago(it.LastRun)) : it.State.Replace('_', ' ')) + (it.Update ? " · ⬆" : "");
                RunPanel.Row r = box.AddRow(logs ? "" : glyph, it.Name + (it.ServerName.Length > 0 ? "   " + it.ServerName : ""), tag, delegate
                {
                    if (logs) { box.Remember(box.Text); box.Close(); KomodoLogWindow.Show(t, item, null); return; }
                    st.Item = item; st.ItemQuery = q; st.Armed = null;
                    box.Redraw();
                }, null);
                if (it.Health == "bad") ((TextBlock)((Grid)r.View.Child).Children[0]).Foreground = StatusColors.Bad;
                else if (it.Health == "good") ((TextBlock)((Grid)r.View.Child).Children[0]).Foreground = StatusColors.Good;
            }
            int running = snap.Containers.FindAll(delegate (KoItem c) { return c.Running; }).Count;
            if (hits.Count == 0) box.Status(q.Length > 0 ? "Nothing in Komodo matches " + q + "." : "No problems - " + running + " containers running.");
            else box.Status((logs ? "Enter: open its log" : "Enter: actions for it   ·   \"logs name\" opens a log") + (hits.Count > 9 ? "  ·  " + (hits.Count - 9) + " more - type more of the name" : "") + "  ·  " + running + " of " + snap.Containers.Count + " containers running");
            box.Select(0);
        }

        static KoItem FindKo(KoSnapshot s, KoItem it)
        {
            Predicate<KoItem> same = delegate (KoItem x) { return x.Key == it.Key; };
            return s.Stacks.Find(same) ?? s.Deployments.Find(same) ?? s.Containers.Find(same) ?? s.Procedures.Find(same);
        }

        static async void RunKo(RunPanel box, KoItem it, string[] act)
        {
            box.Status(act[0] + " " + it.Name + "…");
            string err = await Komodo.RunAsync(it, act[1], null);
            if (err != null) { box.Status("Komodo: " + err); return; }
            box.Remember(box.Text);
            box.Status("Komodo is on it: " + act[0].ToLowerInvariant() + " " + it.Name + " ✓");
        }
    }
}
