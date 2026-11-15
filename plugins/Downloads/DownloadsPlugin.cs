using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("downloads", "Downloads", Description = "qBittorrent and SABnzbd: a module with the speeds and the queue, notifications, !qb / !sab in the run box, and adding magnet links, .torrent and .nzb files.",
    Glyph = "\uE896", Group = "Media", Author = "version", Adds = "!qb / !sab, magnet links and .torrent / .nzb files, notifications", Types = "Downloads")]

namespace vShell.Ui
{
    /// <summary>
    /// The Downloads plugin (qBittorrent / SABnzbd): the module, the two connections, the notifications, !qb / !sab in the run box, adding a
    /// magnet / .torrent / .nzb (pasted into the run box, or files dropped on it or on the module) and the palette's alternative speed.
    /// </summary>
    public sealed class DownloadsPlugin : IPlugin
    {
        Action<string, string, string> _finished, _failed;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.Downloads, Display = "Downloads (qBittorrent / SABnzbd)", ShortName = "Downloads", Glyph = "", Group = "Smart home & more",
                Blurb = "qBittorrent, SABnzbd", HasPopup = true,
                Needs = delegate (BarSettings s) { return Downloads.QbConfigured(s) || Downloads.SabConfigured(s) ? null : "Needs qBittorrent or SABnzbd (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg)
                {
                    // in a widget the list (or, set to Graph, the bar's look); on a bar the speed with its graph
                    if (h.IsWidget && DlView.Of(cfg).View != "Graph") return new DownloadsListModule(mgr, h, cfg);
                    return new DownloadsModule(mgr, h, cfg);
                },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(delegate (BarSettings s) { return Downloads.QbConfigured(s) || Downloads.SabConfigured(s); });
            host.AddIntegration("Downloads", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(Downloads.Configure);

            host.AddNotifyKind("Downloads.Done", NotifyLevel.Normal);
            host.AddNotifyKind("Downloads.Failed", NotifyLevel.Alert);
            host.AddNotifySource("Downloads");
            BarManager mgr0 = host.Manager;
            _finished = delegate (string client, string name, string cat)
            {
                BarSettings s = mgr0.Settings;
                DlSettings d = DlSettings.Of(s);
                if (!d.Notify || !Downloads.CategoryWanted(d.NotifyCategories, cat)) return;
                Notify.Post("Downloads", "✓ " + name, "Finished · " + client + (string.IsNullOrEmpty(cat) ? "" : " · " + cat), Downloads.ClientUrl(s, client), Notify.KindOf("Downloads.Done"));
            };
            _failed = delegate (string client, string name, string why)
            {
                BarSettings s = mgr0.Settings;
                if (!DlSettings.Of(s).NotifyFailed) return;
                Notify.Post("Downloads", "✕ " + name, "Failed" + (string.IsNullOrEmpty(why) ? "" : ": " + why) + " · " + client, Downloads.ClientUrl(s, client), Notify.KindOf("Downloads.Failed"));
            };
            Downloads.Finished += _finished;
            Downloads.Failed += _failed;

            host.AddCommands("Downloads", Commands);
            host.AddBang("DlList", delegate (string key, string q, BarSettings s)
            {
                DlSettings d = DlSettings.Of(s);
                bool qb = key == d.QbBang && d.QbUrl.Length > 0, sab = key == d.SabBang && d.SabUrl.Length > 0;
                if (!qb && !sab) return null;
                string client = qb ? "qBittorrent" : "SABnzbd";
                return new RunAction { Kind = "DlList", Target = client, Query = q, Display = q.Length == 0 ? client + ": everything in it" : client + ": matching " + q };
            }, ListRows, delegate (BarSettings s)
            {
                DlSettings d = DlSettings.Of(s);
                string h = (Downloads.QbConfigured(s) ? "!" + d.QbBang + " qBittorrent   " : "") + (Downloads.SabConfigured(s) ? "!" + d.SabBang + " SABnzbd" : "");
                return h.Trim().Length > 0 ? h.Trim() : null;
            });
            // a magnet link / .torrent address -> qBittorrent, an .nzb address -> SABnzbd (when they're set up)
            host.AddRunText(new[] { "AddTorrent", "AddNzb" }, delegate (string t, BarSettings s)
            {
                string kind = Downloads.LinkKind(t);
                DlSettings d = DlSettings.Of(s);
                if (kind == "torrent" && d.QbUrl.Length > 0) return new RunAction { Kind = "AddTorrent", Target = t, Display = "Add to qBittorrent" };
                if (kind == "nzb" && d.SabUrl.Length > 0) return new RunAction { Kind = "AddNzb", Target = t, Display = "Add to SABnzbd" };
                return null;
            }, AddRows);
            // .torrent / .nzb files dropped on a run box
            host.AddRunBoxDrop(delegate (FrameworkElement box, Action<bool, string> highlight, Action<List<string>> done)
            {
                DownloadDrop.Attach(box, delegate (bool on) { highlight(on, "Drop to add it: .torrent to qBittorrent, .nzb to SABnzbd"); }, done);
            });
        }

        public void Unload()
        {
            if (_finished != null) Downloads.Finished -= _finished;
            if (_failed != null) Downloads.Failed -= _failed;
            Downloads.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            DlSettings d = DlSettings.Of(S);
            p.Header("qBittorrent");
            p.Note("Drop .torrent files on the Downloads module or a run box, or paste a magnet link into a run box, to add them to qBittorrent.");
            p.Text("Address (for example http://192.168.1.20:8080)", delegate { return d.QbUrl; }, delegate (string v) { d.QbUrl = v; });
            p.Text("User name", delegate { return d.QbUser; }, delegate (string v) { d.QbUser = v; },
                   "The login of qBittorrent's Web UI (Tools > Options > Web UI). Leave the user name empty if the server lets this PC in without a login " +
                   "(\"Bypass authentication for clients in whitelisted IP subnets\"). The password is stored encrypted for your Windows account.");
            PasswordBox qpw = p.Secret("Password", "password", delegate { return d.QbPassword; }, delegate (string v) { d.QbPassword = v; });
            p.Check("Accept a self-signed HTTPS certificate", delegate { return d.QbIgnoreCert; }, delegate (bool v) { d.QbIgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return d.QbBang; }, delegate (string v) { d.QbBang = v; },
                   "!" + d.QbBang + " in a run box lists the torrents (!" + d.QbBang + " name for the matching ones) - Enter on one pauses or resumes it.");
            p.Test(delegate
            {
                if (qpw.Password.Trim().Length > 0) { d.QbPassword = Secret.Protect(qpw.Password.Trim()); qpw.Password = ""; p.Changed(); }
            }, delegate { return Downloads.TestQbAsync(S); });

            p.Header("SABnzbd");
            p.Note("Drop .nzb files on the Downloads module or a run box, or paste an .nzb address into a run box, to add them to SABnzbd.");
            p.Text("Address (for example http://192.168.1.20:8085)", delegate { return d.SabUrl; }, delegate (string v) { d.SabUrl = v; });
            PasswordBox spw = p.Secret("API key", "API key", delegate { return d.SabApiKey; }, delegate (string v) { d.SabApiKey = v; },
                                       "In SABnzbd open Config > General and copy the API Key (not the NZB key). It is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return d.SabIgnoreCert; }, delegate (bool v) { d.SabIgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return d.SabBang; }, delegate (string v) { d.SabBang = v; },
                   "!" + d.SabBang + " in a run box lists the downloads (!" + d.SabBang + " name for the matching ones) - Enter on one pauses or resumes it.");
            p.Test(delegate
            {
                if (spw.Password.Trim().Length > 0) { d.SabApiKey = Secret.Protect(spw.Password.Trim()); spw.Password = ""; p.Changed(); }
            }, delegate { return Downloads.TestSabAsync(S); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            DlSettings d = DlSettings.Of(S);
            p.NotifySection("Download notifications", Downloads.QbConfigured(S) || Downloads.SabConfigured(S), "qBittorrent / SABnzbd");
            p.Check("A download finished", delegate { return d.Notify; }, delegate (bool v) { d.Notify = v; p.Redraw(); });
            if (d.Notify)
            {
                p.Text("Only these categories (names, comma separated - empty = all)", delegate { return d.NotifyCategories; }, delegate (string v) { d.NotifyCategories = v; });
                p.Kind("Finished downloads show as", "Downloads.Done");
            }
            p.Check("A download failed", delegate { return d.NotifyFailed; }, delegate (bool v) { d.NotifyFailed = v; p.Redraw(); });
            if (d.NotifyFailed) p.Kind("Failed downloads show as", "Downloads.Failed");
            p.Note("Notifications work without a Downloads module too (vShell then checks every 30 seconds instead of every 2).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            DlView v = new DlView(cfg);
            if (!Downloads.QbConfigured(p.Settings) && !Downloads.SabConfigured(p.Settings))
                p.Note("Set up qBittorrent and / or SABnzbd first (Plugins page in this window).");
            p.Combo("Show", DlView.Sources, delegate { return v.Source; }, delegate (string s) { v.Source = s; p.Redraw(); });     // SABnzbd alone has no upload speed
            bool list = p.IsWidget && v.View != "Graph";
            if (p.IsWidget)
                p.Combo("In the widget", DlView.Views, delegate { return v.View; }, delegate (string s) { v.View = s; p.Redraw(); });
            if (list || !p.IsWidget)
            {
                p.Section("List" + (p.IsWidget ? "" : " (click the module)"));
                p.Combo("Downloads to list", DlView.Shows, delegate { return v.Show; }, delegate (string s) { v.Show = s; });
                p.Slider("How many per client", 1, 30, delegate { return v.Count; }, delegate (double d) { v.Count = (int)d; });
                p.Note("Active = downloading now; Unfinished = also queued and paused; All = also finished and seeding. Click a download to pause or " +
                       "resume it; the buttons by each client pause / resume everything, turn qBittorrent's alternative speed limits on or off, and open its web page.");
            }
            if (list)
            {
                p.Check("Speed graph along the top", delegate { return cfg.ShowText; }, delegate (bool on) { cfg.ShowText = on; p.Redraw(); });
                if (cfg.ShowText)
                {
                    p.Check("Automatic scale", delegate { return cfg.AutoScale; }, delegate (bool on) { cfg.AutoScale = on; p.Redraw(); });
                    if (!cfg.AutoScale)
                        p.Number("Graph full scale (MB/s)", delegate { return cfg.RangeMax; }, delegate (double d) { cfg.RangeMax = Math.Max(0.1, d); });
                    p.Color("Graph colour", delegate { return cfg.Color; }, delegate (string s) { cfg.Color = s; }, true, false);
                    p.GraphLook(cfg);
                }
                return;
            }
            p.Section("Text");
            p.Check("Show the number of downloads and overall progress", delegate { return v.ShowCount; }, delegate (bool on) { v.ShowCount = on; });
            if (v.Source != "SABnzbd")
                p.Check("Show the upload speed (qBittorrent)", delegate { return v.ShowUpload; }, delegate (bool on) { v.ShowUpload = on; });
            p.Check("Hide when nothing is downloading", delegate { return cfg.HideIfNone; }, delegate (bool on) { cfg.HideIfNone = on; });
            p.Title(cfg, "");     // last of its own rows; the graph's look follows
            p.Note("Line / Bars / Meter / Tile follow the download speed (upload in the second colour); Ring shows how far the whole queue is.");
            p.GraphSettings(cfg, true, delegate
            {
                if (cfg.Style == "Ring") return;
                p.Check("Automatic scale", delegate { return cfg.AutoScale; }, delegate (bool on) { cfg.AutoScale = on; p.Redraw(); });
                if (!cfg.AutoScale)
                    p.Number("Graph full scale (MB/s)", delegate { return cfg.RangeMax; }, delegate (double d) { cfg.RangeMax = Math.Max(0.1, d); });
                else p.Note("The top of the graph follows the fastest speed it shows, rounded up (the tooltip shows it); the warning colour is off.");
            });
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            Brush dim = t.FgDim;
            double fs = 12.5;
            return ModulePics.Row(7, ModulePics.T(t, "↓ 38 MB/s", StatusColors.Good, fs), ModulePics.Spark(48, 16, StatusColors.Good, ModulePics.Wave1), ModulePics.T(t, "3", dim, fs));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim;
            return ModulePics.Col(6, ModulePics.Row(10, ModulePics.T(t, "qBittorrent", fg, 12, true), ModulePics.T(t, "↓ 38 MB/s", StatusColors.Good, 11.5)),
                                  ModulePics.Col(3, ModulePics.T(t, "ubuntu-24.10.iso", fg, 11), ModulePics.Meter(t, 170, 4, 0.72, StatusColors.Good)),
                                  ModulePics.Col(3, ModulePics.T(t, "debian-12.7.iso", fg, 11), ModulePics.Meter(t, 170, 4, 0.3, StatusColors.Good)),
                                  ModulePics.Col(3, ModulePics.T(t, "fedora-41.iso · paused", dim, 11), ModulePics.Meter(t, 170, 4, 0.12, StatusColors.Warn)));
        }

        // =================================================================
        // The run box: !qb / !sab lists, adding a magnet / .torrent / .nzb
        // =================================================================
        sealed class BoxState { public System.Windows.Threading.DispatcherTimer Timer; }

        /// <summary>The client's downloads matching the query, live (every 2 s); Enter / click pauses or resumes one.</summary>
        static void ListRows(RunPanel box, RunAction a)
        {
            object o;
            BoxState st;
            if (box.PluginState.TryGetValue("downloads", out o)) st = (BoxState)o;
            else { st = new BoxState(); box.PluginState["downloads"] = st; }
            string client = a.Target, q = a.Query;
            DlSnapshot snap = Downloads.Latest;
            DlClientState c = client == "SABnzbd" ? snap.Sab : snap.Qb;
            if (st.Timer == null)
            {
                Downloads.Poke();
                st.Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                st.Timer.Tick += delegate
                {
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != "DlList" || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            if (!c.Configured) { box.Status("Set up " + client + " on the Plugins page in Settings first."); return; }
            if (!c.Ok) { box.Status(c.Error.Length > 0 ? client + ": " + c.Error : "Asking " + client + "…"); return; }

            var list = c.Items.FindAll(delegate (DlItem i) { return q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; });
            // downloading first, then queued / paused, finished last
            list.Sort(delegate (DlItem x, DlItem y)
            {
                Func<DlItem, int> r = delegate (DlItem i) { return i.Done ? 3 : i.Paused ? 2 : i.Active ? 0 : 1; };
                return r(x).CompareTo(r(y));
            });
            int n = 0;
            foreach (DlItem it in list)
            {
                if (n++ >= 8) break;
                DlItem item = it;
                string pct = it.Done ? "✓" : Math.Floor(it.Progress * 100) + "%";
                string state = it.StateText + (it.Active && it.Eta >= 0 ? " · " + Downloads.Eta(it.Eta) : "");
                bool canToggle = !it.Done || client == "qBittorrent";
                RunPanel.Row row = box.AddRow(it.Paused ? "" : it.Done ? "" : "", pct + "   " + it.Name, state, canToggle ? (Action)delegate
                {
                    Downloads.Pause(client, item, !item.Paused);
                    box.Status((item.Paused ? "Resuming " : "Pausing ") + item.Name + "…");
                } : null, null);
                row.View.ToolTip = it.Name + "\n" + (canToggle ? (it.Paused ? "Enter / click: resume" : "Enter / click: pause") : "Finished");
            }
            string sum = "↓ " + Downloads.Speed(c.DownSpeed) + (client == "qBittorrent" ? "  ↑ " + Downloads.Speed(c.UpSpeed) : "") +
                         "  ·  " + c.Unfinished.Count + " unfinished" + (c.Paused ? " · paused" : "");
            if (list.Count == 0) box.Status((q.Length > 0 ? "Nothing in " + client + " matches " + q + ".  " : "") + sum);
            else box.Status(sum + (list.Count > 8 ? "  ·  " + (list.Count - 8) + " more - type more of the name" : ""));
            box.Select(0);
        }

        /// <summary>A magnet / .torrent / .nzb address in the box: Enter adds it; or open it with the app Windows uses for it.</summary>
        static void AddRows(RunPanel box, RunAction a)
        {
            string text = box.Text, target = a.Target;
            box.AddRow("", a.Display, null, delegate { Add(box, a, text); }, null);
            box.AddRow("", "Open it instead (the app Windows uses for it)", null, delegate
            {
                box.Remember(text);
                box.Close();
                try { RunPanel.Open(target); } catch (Exception ex) { Log.Write("Run box open: " + ex.Message); }
            }, null);
            box.Select(0);
        }

        static async void Add(RunPanel box, RunAction a, string text)
        {
            bool torrent = a.Kind == "AddTorrent";
            string client = torrent ? "qBittorrent" : "SABnzbd";
            box.Status("Adding to " + client + "…");
            string err = torrent ? await Downloads.QbAddAsync(a.Target) : await Downloads.SabAddAsync(a.Target);
            if (err != null) { box.Status(client + ": " + err); return; }
            box.Remember(text);
            box.Status("Added to " + client + " ✓");
            await Task.Delay(900);
            box.Close();
        }

        // =================================================================
        // The command palette: qBittorrent's alternative speed limits
        // =================================================================
        static Task<string> Done() { return Task.FromResult<string>(null); }

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (Downloads.QbConfigured(s))
                into.Add(new PaletteCommand
                {
                    Title = "qBittorrent: alternative speed limits on / off", Detail = "qBittorrent", Glyph = "", Source = "qBittorrent",
                    Words = "torrent slow turtle speed limit alt toggle", Run = delegate { Downloads.QbToggleAltSpeed(); return Done(); }
                });
        }
    }
}
