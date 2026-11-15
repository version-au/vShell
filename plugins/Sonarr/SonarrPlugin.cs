using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("sonarr", "Sonarr", Description = "Your shows in Sonarr: a module with the queue, what's airing, what was added and what's missing, notifications, and !so in the run box to search and add shows.",
    Glyph = "", Group = "Media", Author = "version", Adds = "!so in the run box (search, add), notifications, palette commands", Types = "Sonarr")]

namespace vShell.Ui
{
    /// <summary>
    /// The Sonarr plugin: the module, the Plugins and Notifications sections, the notifications, and !so in the run box (the queue,
    /// or search and add a show).
    /// </summary>
    public sealed class SonarrPlugin : IPlugin
    {
        public const string Type = "Sonarr";
        Action<string, string, string, string> _notice;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Sonarr shows", ShortName = "Sonarr", Glyph = "", Group = "Smart home & more",
                Blurb = "queue, airing soon, missing", HasPopup = true,
                Needs = delegate (BarSettings s) { return SonarrClient.Configured(s) ? null : "Needs the Sonarr connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new SonarrModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(SonarrClient.Configured);
            host.AddIntegration("Sonarr", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(SonarrClient.Configure);

            host.AddNotifyKind("Sonarr.Imported", NotifyLevel.Normal);
            host.AddNotifyKind("Sonarr.Grabbed", NotifyLevel.Normal);
            host.AddNotifyKind("Sonarr.Failed", NotifyLevel.Alert);
            host.AddNotifyKind("Sonarr.Health", NotifyLevel.Important);
            host.AddNotifySource("Sonarr");
            _notice = delegate (string kind, string title, string detail, string url) { Notify.Post("Sonarr", title, detail, url, Notify.KindOf(kind)); };
            SonarrClient.Notice += _notice;

            host.AddBang("Sonarr", delegate (string key, string q, BarSettings s)
            {
                SoSettings c = SoSettings.Of(s);
                if (key != c.Bang || string.IsNullOrWhiteSpace(c.Url)) return null;
                return new RunAction { Kind = "Sonarr", Query = q, Display = q.Length == 0 ? "Sonarr: the queue" : "Search Sonarr for " + q };
            }, Rows, delegate (BarSettings s) { return SonarrClient.Configured(s) ? "!" + SoSettings.Of(s).Bang + " Sonarr" : null; });
            host.AddCommands("Sonarr", Commands);
        }

        // =================================================================
        // The command palette: search for what's missing (all, or a show's), open Sonarr's pages
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!SonarrClient.Configured(s)) return;
            const string Src = "Sonarr";
            into.Add(new PaletteCommand
            {
                Title = "Search for every missing episode", Detail = Src, Glyph = "", Source = Src, Words = "sonarr wanted missing search all episodes shows", Ask = true,
                Run = async delegate { string err = await SonarrClient.SearchMissingAsync(s); return err ?? PaletteCommand.Worked("Sonarr is searching for the missing episodes"); }
            });
            foreach (string[] page in new[] { new[] { "Open Sonarr", "" }, new[] { "Open Sonarr's queue", "activity/queue" }, new[] { "Open Sonarr's calendar", "calendar" },
                                              new[] { "Open Sonarr's missing episodes", "wanted/missing" } })
            {
                string url = SonarrClient.PageUrl(s, page[1]);
                into.Add(new PaletteCommand
                {
                    Title = page[0], Detail = Src, Glyph = "", Source = Src, Words = "sonarr shows tv web page", Opens = true,
                    Run = delegate { RunPanel.Open(url); return System.Threading.Tasks.Task.FromResult(""); }
                });
            }
            SonarrSnapshot snap = SonarrClient.Latest;
            if (snap == null) return;
            // the missing episodes, one command per show
            var byShow = new Dictionary<int, List<SonarrEpisode>>();
            var order = new List<int>();
            foreach (SonarrEpisode e in snap.Missing)
            {
                List<SonarrEpisode> l;
                if (!byShow.TryGetValue(e.SeriesId, out l)) { l = new List<SonarrEpisode>(); byShow[e.SeriesId] = l; order.Add(e.SeriesId); }
                l.Add(e);
            }
            foreach (int id in order)
            {
                List<SonarrEpisode> eps = byShow[id];
                string show = eps[0].Series.Title.Length > 0 ? eps[0].Series.Title : "show " + id;
                var ids = eps.ConvertAll(delegate (SonarrEpisode e) { return e.Id; });
                into.Add(new PaletteCommand
                {
                    Title = "Search for the missing episodes of " + show, Detail = Src + " · " + SonarrEpisode.Codes(eps), Glyph = "", Source = Src,
                    Words = "sonarr tv episode missing download find",
                    Run = async delegate { string err = await SonarrClient.SearchEpisodesAsync(s, ids); return err ?? PaletteCommand.Worked("Sonarr is searching for " + show); }
                });
            }
        }

        public void Unload()
        {
            if (_notice != null) SonarrClient.Notice -= _notice;
            SonarrClient.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            SoSettings c = SoSettings.Of(S);
            p.Header("Sonarr");
            p.Text("Address (for example http://192.168.1.20:8989 - with Sonarr's URL base if it has one, like http://nas/sonarr)", delegate { return c.Url; }, delegate (string v) { c.Url = v; });
            PasswordBox pw = p.Secret("API key", "API key", delegate { return c.ApiKey; }, delegate (string v) { c.ApiKey = v; },
                                      "In Sonarr open Settings > General and copy the API key. It is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "Search Sonarr from a run box with !" + c.Bang + " Show Title, and add shows from the results.\n" +
                   "!" + c.Bang + " on its own shows the queue (Enter opens the show in Sonarr).");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.ApiKey = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return TestAndRedraw(p); });

            p.Section("Adding shows");
            List<string> profiles = SonarrClient.Profiles, folders = SonarrClient.Folders;
            if (profiles.Count == 0 && folders.Count == 0)
                p.Note("Press Test connection to pick the quality profile and root folder here. Until then shows you add use the first of each in Sonarr.");
            else
            {
                p.Combo("Quality profile", Choices(profiles, c.Profile), delegate { return c.Profile.Length > 0 ? c.Profile : SoSettings.First; }, delegate (string v) { c.Profile = v; });
                p.Combo("Root folder", Choices(folders, c.RootFolder), delegate { return c.RootFolder.Length > 0 ? c.RootFolder : SoSettings.First; }, delegate (string v) { c.RootFolder = v; });
            }
            p.Combo("Episodes to monitor", SoSettings.MonitorChoices, delegate { return c.Monitor; }, delegate (string v) { c.Monitor = v; p.Redraw(); });     // None: nothing to search for
            p.Note("Which episodes of a show you add Sonarr looks for. Future episodes = only the ones that haven't aired yet; None adds the show without looking for anything.");
            if (c.MonitorValue != "none")
                p.Check("Search for the missing episodes as soon as it's added", delegate { return c.SearchOnAdd; }, delegate (bool v) { c.SearchOnAdd = v; });
            p.Check("A folder for each season", delegate { return c.SeasonFolders; }, delegate (bool v) { c.SeasonFolders = v; });
        }

        static async System.Threading.Tasks.Task<KeyValuePair<bool, string>> TestAndRedraw(SettingsWindow.SettingsPage p)
        {
            bool had = SonarrClient.Profiles.Count > 0;
            KeyValuePair<bool, string> r = await SonarrClient.TestAsync(p.Settings);
            if (r.Key && !had && SonarrClient.Profiles.Count > 0) p.Redraw();     // the profile / folder pickers can show now
            return r;
        }

        static string[] Choices(List<string> have, string current)
        {
            var l = new List<string> { SoSettings.First };
            l.AddRange(have);
            if (current.Length > 0 && !l.Contains(current)) l.Add(current);
            return l.ToArray();
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            SoSettings c = SoSettings.Of(S);
            p.NotifySection("Sonarr notifications", SonarrClient.Configured(S), "Sonarr");
            p.Check("Episodes were downloaded (they're in your library)", delegate { return c.NotifyImported; }, delegate (bool v) { c.NotifyImported = v; p.Redraw(); });
            if (c.NotifyImported) p.Kind("Downloads show as", "Sonarr.Imported");
            p.Check("Sonarr grabbed a release (sent it to the download client)", delegate { return c.NotifyGrabbed; }, delegate (bool v) { c.NotifyGrabbed = v; p.Redraw(); });
            if (c.NotifyGrabbed) p.Kind("Grabs show as", "Sonarr.Grabbed");
            p.Check("A download failed", delegate { return c.NotifyFailed; }, delegate (bool v) { c.NotifyFailed = v; p.Redraw(); });
            if (c.NotifyFailed) p.Kind("Failed downloads show as", "Sonarr.Failed");
            p.Check("A new health warning or error (System > Status in Sonarr)", delegate { return c.NotifyHealth; }, delegate (bool v) { c.NotifyHealth = v; p.Redraw(); });
            if (c.NotifyHealth) p.Kind("Health issues show as", "Sonarr.Health");
            p.Note("A season pack is one notification, not one per episode. Notifications work without a Sonarr module too " +
                   "(vShell then checks every 2 minutes instead of every 15 seconds).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            SoView v = new SoView(cfg);
            if (!SonarrClient.Configured(p.Settings)) p.Note("Set up the Sonarr connection first (Plugins page in this window).");
            p.Note("Tabs to show - with only one ticked, just that view shows, without tabs" + (p.IsWidget ? " (and the widget sizes to it)." : "."));
            List<string> shown = v.TabsShown;
            foreach (string tabName in SoView.Tabs)
            {
                string tn = tabName;
                p.Check(tn, delegate { return v.TabsShown.Contains(tn); }, delegate (bool on)
                {
                    List<string> l = v.TabsShown;
                    if (on) { if (!l.Contains(tn)) l.Add(tn); }
                    else if (l.Count > 1) l.Remove(tn);     // at least one stays ticked
                    v.TabsShown = l;
                    p.Redraw();
                });
            }
            if (shown.Count > 1)
                p.Combo(p.IsWidget ? "Opens on" : "Popup opens on", shown.ToArray(), delegate { return v.Tab; }, delegate (string s) { v.Tab = s; });
            p.Slider("How many", 1, 30, delegate { return v.Count; }, delegate (double d) { v.Count = (int)d; });
            if (shown.Contains("Upcoming"))
                p.Slider("Upcoming: days ahead", 1, 30, delegate { return v.Days; }, delegate (double d) { v.Days = (int)d; });
            p.Check("Posters", delegate { return v.Posters; }, delegate (bool on) { v.Posters = on; });
            p.Check("Show the \"Sonarr\" button", delegate { return v.ShowOpen; }, delegate (bool on) { v.ShowOpen = on; });
            p.Check("Show health warnings and errors", delegate { return v.Health; }, delegate (bool on) { v.Health = on; p.Redraw(); });
            if (!p.IsWidget)
            {
                p.Check("Badge with the number in the queue", delegate { return v.Badge; }, delegate (bool on) { v.Badge = on; });
                p.Check("Show how far the downloads are and the time left", delegate { return v.BarProgress; }, delegate (bool on) { v.BarProgress = on; });
                p.Check("Hide while nothing is in the queue", delegate { return v.HideIdle; }, delegate (bool on) { v.HideIdle = on; });
                p.Note(v.Health ? "It still shows when Sonarr has a health error or can't be reached." : "It still shows when Sonarr can't be reached.");
                p.Title(cfg, "");
            }
            p.Note((p.IsWidget ? "" : "On a bar: the icon with a badge for the queue (red when a download needs a look, hover for the list) - click it for the popup. ") +
                   "A season pack is one row in the queue, episodes airing back to back are one row in Upcoming, and Missing has one row per show. " +
                   "Click a show to open it in Sonarr, right-click it for more (remove a download from the queue, search again).");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(4, ModulePics.Badge(ModulePics.G(0xE7F4, t.Fg, 16), "3", t.MainAccent, 8), ModulePics.T(t, "38%", t.Fg, 12.5), ModulePics.T(t, "· 25 min", t.FgDim, 12.5));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            Func<int, string, string, Brush, FrameworkElement> row = delegate (int shade, string name, string state, Brush b)
            {
                var p = ModulePics.Square(ModulePics.Shade(t, shade), 18, 2);
                p.Height = 26;
                return ModulePics.Row(6, p, ModulePics.Col(2, ModulePics.T(t, name, fg, 10.5, true), ModulePics.T(t, state, b, 9.5)));
            };
            return ModulePics.Col(6, ModulePics.Row(4, ModulePics.T(t, "Queue", dim, 11), ModulePics.T(t, "Upcoming", fg, 11, true), ModulePics.T(t, "Missing", dim, 11)),
                                  row(55, "Severance  S03E04", "tonight 8:00 pm", acc), row(75, "The Bear  S05E01-E10", "Fri 9:00 pm", acc));
        }

        // =================================================================
        // The run box: !so alone -> the queue; !so title -> search Sonarr, and add from the results
        // =================================================================
        sealed class BoxState
        {
            public System.Windows.Threading.DispatcherTimer Timer;
            public int SearchGen;
        }

        static void Rows(RunPanel box, RunAction a)
        {
            object o;
            BoxState st;
            if (box.PluginState.TryGetValue("sonarr", out o)) st = (BoxState)o;
            else { st = new BoxState(); box.PluginState["sonarr"] = st; }
            BarSettings S = box.Settings;

            if (a.Query.Length > 0)
            {
                // a title: Enter searches (the results replace the list)
                string query = a.Query;
                box.AddRow("", a.Display, null, delegate { box.Remember(box.Text); Search(box, st, query); }, null);
                box.Select(0);
                return;
            }

            if (st.Timer == null)
            {
                SonarrClient.Poke();
                st.Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += delegate
                {
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != "Sonarr" || now.Query.Length > 0 || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            if (!SonarrClient.Configured(S)) { box.Status("Set up Sonarr on the Plugins page in Settings first."); return; }
            SonarrSnapshot snap = SonarrClient.Latest;
            if (!snap.Ok) { box.Status(snap.Error.Length > 0 ? "Sonarr: " + snap.Error : "Asking Sonarr…"); return; }
            string glyph = SonarrList.G(SonarrList.Glyph), bang = SoSettings.Of(S).Bang;
            int k = 0;
            foreach (SonarrQueueItem q in snap.Queue)
            {
                if (k++ >= 9) break;
                SonarrSeries m = q.Series;
                RunPanel.Row row = box.AddRow(glyph, m.Title + (q.Episodes.Count > 0 ? "  " + q.Codes : ""), q.StateText,
                                              delegate { box.Remember(box.Text); box.Close(); RunPanel.Open(SonarrClient.SeriesUrl(S, m)); }, null);
                if (q.Problem) ((TextBlock)((Grid)row.View.Child).Children[0]).Foreground = StatusColors.Bad;
            }
            if (snap.Queue.Count > 0)
            {
                box.Status(snap.Queue.Count + " in the queue  ·  Enter: open the show in Sonarr  ·  !" + bang + " title to search and add" + (snap.Queue.Count > 9 ? "  ·  " + (snap.Queue.Count - 9) + " more in Sonarr" : ""));
                box.Select(0);
                return;
            }
            // nothing downloading: what's airing next
            k = 0;
            foreach (SonarrGroup g in SonarrGroup.Of(snap.Upcoming.FindAll(delegate (SonarrEpisode e) { return !e.HasFile && e.Airs > DateTime.UtcNow; }), true))
            {
                if (k++ >= 6) break;
                SonarrSeries m = g.Series;
                box.AddRow(glyph, m.Title + "  " + g.Codes, SonarrEpisode.When(g.When), delegate { box.Remember(box.Text); box.Close(); RunPanel.Open(SonarrClient.SeriesUrl(S, m)); }, null);
            }
            box.Status("Nothing in the queue" + (k > 0 ? " - airing soon" : "") + "  ·  !" + bang + " title to search and add");
            box.Select(0);
        }

        // ---- the search results: poster, title, in the library or not, Add / Open in Sonarr ----
        static async void Search(RunPanel box, BoxState st, string query)
        {
            int gen = ++st.SearchGen;
            box.Hold();
            BarSettings S = box.Settings;
            if (!SonarrClient.Configured(S)) { box.Status("Set up Sonarr on the Plugins page in Settings first."); return; }
            box.Status("Searching Sonarr for " + query + "…");
            List<SonarrSeries> items;
            try { items = await SonarrClient.SearchAsync(S, query); }
            catch (Exception ex) { if (gen == st.SearchGen) box.Status("Sonarr: " + ex.Message); return; }
            if (gen != st.SearchGen || !box.Held) return;
            box.Status(items.Count == 0 ? "Nothing found for " + query + "." : null);
            int n = 0;
            foreach (SonarrSeries m in items)
            {
                if (n++ >= 8) break;
                box.AddElement(ResultRow(box, m));
            }
        }

        static FrameworkElement ResultRow(RunPanel box, SonarrSeries m)
        {
            Theme t = box.Theme;
            var g = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var poster = new Border { Width = 36, Height = 54, CornerRadius = new CornerRadius(4), Background = Theme.Faded(t.Fg, 10), VerticalAlignment = VerticalAlignment.Top };
            if (m.Poster.Length > 0)
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(m.Poster);
                    bmp.DecodePixelWidth = 72;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    poster.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                }
                catch { }
            g.Children.Add(poster);

            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = m.Name, Foreground = t.Fg, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var meta = new TextBlock { FontSize = Math.Max(8, t.FontSize * 0.85), Foreground = t.FgDim, Margin = new Thickness(0, 1, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
            var about = new List<string>();
            if (m.Network.Length > 0) about.Add(m.Network);
            if (m.Seasons > 0) about.Add(m.Seasons + (m.Seasons == 1 ? " season" : " seasons"));
            if (m.Status == "ended") about.Add("ended");
            else if (m.Status == "upcoming") about.Add("not out yet");
            if (about.Count > 0) meta.Inlines.Add(new System.Windows.Documents.Run(string.Join(" · ", about) + "  ·  "));
            if (m.InLibrary)
                meta.Inlines.Add(new System.Windows.Documents.Run(m.Episodes > 0 ? "In your library · " + m.Files + " of " + m.Episodes + " episodes" : "In your library")
                    { Foreground = m.Episodes > 0 && m.Files >= m.Episodes ? StatusColors.Good : StatusColors.Warn });
            else meta.Inlines.Add(new System.Windows.Documents.Run("Not in Sonarr"));
            info.Children.Add(meta);
            var buttons = new WrapPanel();
            var msg = new TextBlock { Foreground = t.FgDim, FontSize = Math.Max(8, t.FontSize * 0.85), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            if (!m.InLibrary)
                buttons.Children.Add(Pill(t, "Add", true, delegate { DoAdd(box, m, msg, buttons); }));
            else if (m.Episodes == 0 || m.Files < m.Episodes)
                buttons.Children.Add(Pill(t, "Search for missing", true, delegate { DoSearch(box, m, msg); }));
            buttons.Children.Add(Pill(t, "Open in Sonarr", false, delegate { box.Close(); RunPanel.Open(SonarrClient.SeriesUrl(box.Settings, m)); }));
            info.Children.Add(buttons);
            info.Children.Add(msg);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            if (m.Overview.Length > 0) g.ToolTip = new TextBlock { Text = m.Overview, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            return new Border { Child = g, Padding = new Thickness(4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        }

        static Border Pill(Theme t, string text, bool primary, Action act)
        {
            Border b = Pills.Make(t, "", text, null, primary, act);
            b.Margin = new Thickness(0, 0, 6, 2);
            return b;
        }

        static async void DoAdd(RunPanel box, SonarrSeries m, TextBlock msg, Panel buttons)
        {
            Theme t = box.Theme;
            msg.Visibility = Visibility.Visible;
            msg.Foreground = t.FgDim;
            msg.Text = "Adding…";
            buttons.IsEnabled = false;
            string err = await SonarrClient.AddAsync(box.Settings, m);
            if (err == null)
            {
                SoSettings c = SoSettings.Of(box.Settings);
                msg.Foreground = StatusColors.Good;
                msg.Text = c.SearchOnAdd && c.MonitorValue != "none" ? "Added ✓ - Sonarr is looking for the episodes (" + c.Monitor.ToLowerInvariant() + ")" : "Added ✓";
                buttons.IsEnabled = true;
                buttons.Children.RemoveAt(0);      // Add (Open in Sonarr stays, now on the show's page)
            }
            else
            {
                msg.Foreground = StatusColors.Bad;
                msg.Text = err;
                buttons.IsEnabled = true;
            }
        }

        static async void DoSearch(RunPanel box, SonarrSeries m, TextBlock msg)
        {
            msg.Visibility = Visibility.Visible;
            msg.Foreground = box.Theme.FgDim;
            msg.Text = "Asking Sonarr…";
            string err = await SonarrClient.SearchSeriesAsync(box.Settings, m.Id);
            msg.Foreground = err == null ? StatusColors.Good : StatusColors.Bad;
            msg.Text = err ?? "Searching ✓";
        }
    }
}
