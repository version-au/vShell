using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("radarr", "Radarr", Description = "Your movies in Radarr: a module with the queue, what's coming out, what was added and what's missing, notifications, and !ra in the run box to search and add movies.",
    Glyph = "", Group = "Media", Author = "version", Adds = "!ra in the run box (search, add), notifications, palette commands", Types = "Radarr")]

namespace vShell.Ui
{
    /// <summary>
    /// The Radarr plugin: the module, the Plugins and Notifications sections, the notifications, and !ra in the run box (the queue,
    /// or search and add a movie).
    /// </summary>
    public sealed class RadarrPlugin : IPlugin
    {
        public const string Type = "Radarr";
        Action<string, string, string, string> _notice;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Radarr movies", ShortName = "Radarr", Glyph = "", Group = "Smart home & more",
                Blurb = "queue, upcoming, missing", HasPopup = true,
                Needs = delegate (BarSettings s) { return RadarrClient.Configured(s) ? null : "Needs the Radarr connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new RadarrModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(RadarrClient.Configured);
            host.AddIntegration("Radarr", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(RadarrClient.Configure);

            host.AddNotifyKind("Radarr.Imported", NotifyLevel.Normal);
            host.AddNotifyKind("Radarr.Grabbed", NotifyLevel.Normal);
            host.AddNotifyKind("Radarr.Failed", NotifyLevel.Alert);
            host.AddNotifyKind("Radarr.Health", NotifyLevel.Important);
            host.AddNotifySource("Radarr");
            _notice = delegate (string kind, string title, string detail, string url) { Notify.Post("Radarr", title, detail, url, Notify.KindOf(kind)); };
            RadarrClient.Notice += _notice;

            host.AddBang("Radarr", delegate (string key, string q, BarSettings s)
            {
                RaSettings c = RaSettings.Of(s);
                if (key != c.Bang || string.IsNullOrWhiteSpace(c.Url)) return null;
                return new RunAction { Kind = "Radarr", Query = q, Display = q.Length == 0 ? "Radarr: the queue" : "Search Radarr for " + q };
            }, Rows, delegate (BarSettings s) { return RadarrClient.Configured(s) ? "!" + RaSettings.Of(s).Bang + " Radarr" : null; });
            host.AddCommands("Radarr", Commands);
        }

        // =================================================================
        // The command palette: search for what's missing, open Radarr's pages
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!RadarrClient.Configured(s)) return;
            const string Src = "Radarr";
            into.Add(new PaletteCommand
            {
                Title = "Search for every missing movie", Detail = Src, Glyph = "", Source = Src, Words = "radarr wanted missing search all movies", Ask = true,
                Run = async delegate { string err = await RadarrClient.SearchMissingAsync(s); return err ?? PaletteCommand.Worked("Radarr is searching for the missing movies"); }
            });
            foreach (string[] page in new[] { new[] { "Open Radarr", "" }, new[] { "Open Radarr's queue", "activity/queue" }, new[] { "Open Radarr's calendar", "calendar" },
                                              new[] { "Open Radarr's missing movies", "wanted/missing" } })
            {
                string url = RadarrClient.PageUrl(s, page[1]);
                into.Add(new PaletteCommand
                {
                    Title = page[0], Detail = Src, Glyph = "", Source = Src, Words = "radarr movies web page", Opens = true,
                    Run = delegate { RunPanel.Open(url); return System.Threading.Tasks.Task.FromResult(""); }
                });
            }
            RadarrSnapshot snap = RadarrClient.Latest;
            if (snap == null) return;
            foreach (RadarrMovie m0 in snap.Missing)
            {
                RadarrMovie m = m0;
                into.Add(new PaletteCommand
                {
                    Title = "Search for " + m.Name, Detail = Src + " · missing", Glyph = "", Source = Src, Words = "radarr movie missing download find",
                    Run = async delegate { string err = await RadarrClient.SearchMovieAsync(s, m.Id); return err ?? PaletteCommand.Worked("Radarr is searching for " + m.Name); }
                });
            }
        }

        public void Unload()
        {
            if (_notice != null) RadarrClient.Notice -= _notice;
            RadarrClient.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            RaSettings c = RaSettings.Of(S);
            p.Header("Radarr");
            p.Text("Address (for example http://192.168.1.20:7878 - with Radarr's URL base if it has one, like http://nas/radarr)", delegate { return c.Url; }, delegate (string v) { c.Url = v; });
            PasswordBox pw = p.Secret("API key", "API key", delegate { return c.ApiKey; }, delegate (string v) { c.ApiKey = v; },
                                      "In Radarr open Settings > General and copy the API key. It is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "Search Radarr from a run box with !" + c.Bang + " Movie Title, and add movies from the results.\n" +
                   "!" + c.Bang + " on its own shows the queue (Enter opens one in Radarr).");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.ApiKey = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return TestAndRedraw(p); });

            p.Section("Adding movies");
            List<string> profiles = RadarrClient.Profiles, folders = RadarrClient.Folders;
            if (profiles.Count == 0 && folders.Count == 0)
                p.Note("Press Test connection to pick the quality profile and root folder here. Until then movies you add use the first of each in Radarr.");
            else
            {
                p.Combo("Quality profile", Choices(profiles, c.Profile), delegate { return c.Profile.Length > 0 ? c.Profile : RaSettings.First; }, delegate (string v) { c.Profile = v; });
                p.Combo("Root folder", Choices(folders, c.RootFolder), delegate { return c.RootFolder.Length > 0 ? c.RootFolder : RaSettings.First; }, delegate (string v) { c.RootFolder = v; });
            }
            p.Combo("Available when", RaSettings.Availability, delegate { return c.MinAvailability; }, delegate (string v) { c.MinAvailability = v; });
            p.Note("When Radarr counts a movie you add as available, and starts looking for it (Radarr calls it Minimum availability).");
            p.Check("Search for a movie as soon as it's added", delegate { return c.SearchOnAdd; }, delegate (bool v) { c.SearchOnAdd = v; });
        }

        static async System.Threading.Tasks.Task<KeyValuePair<bool, string>> TestAndRedraw(SettingsWindow.SettingsPage p)
        {
            bool had = RadarrClient.Profiles.Count > 0;
            KeyValuePair<bool, string> r = await RadarrClient.TestAsync(p.Settings);
            if (r.Key && !had && RadarrClient.Profiles.Count > 0) p.Redraw();     // the profile / folder pickers can show now
            return r;
        }

        static string[] Choices(List<string> have, string current)
        {
            var l = new List<string> { RaSettings.First };
            l.AddRange(have);
            if (current.Length > 0 && !l.Contains(current)) l.Add(current);
            return l.ToArray();
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            RaSettings c = RaSettings.Of(S);
            p.NotifySection("Radarr notifications", RadarrClient.Configured(S), "Radarr");
            p.Check("A movie was downloaded (it's in your library)", delegate { return c.NotifyImported; }, delegate (bool v) { c.NotifyImported = v; p.Redraw(); });
            if (c.NotifyImported) p.Kind("Downloads show as", "Radarr.Imported");
            p.Check("Radarr grabbed a release (sent it to the download client)", delegate { return c.NotifyGrabbed; }, delegate (bool v) { c.NotifyGrabbed = v; p.Redraw(); });
            if (c.NotifyGrabbed) p.Kind("Grabs show as", "Radarr.Grabbed");
            p.Check("A download failed", delegate { return c.NotifyFailed; }, delegate (bool v) { c.NotifyFailed = v; p.Redraw(); });
            if (c.NotifyFailed) p.Kind("Failed downloads show as", "Radarr.Failed");
            p.Check("A new health warning or error (System > Status in Radarr)", delegate { return c.NotifyHealth; }, delegate (bool v) { c.NotifyHealth = v; p.Redraw(); });
            if (c.NotifyHealth) p.Kind("Health issues show as", "Radarr.Health");
            p.Note("Notifications work without a Radarr module too (vShell then checks every 2 minutes instead of every 15 seconds).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            RaView v = new RaView(cfg);
            if (!RadarrClient.Configured(p.Settings)) p.Note("Set up the Radarr connection first (Plugins page in this window).");
            p.Note("Tabs to show - with only one ticked, just that view shows, without tabs" + (p.IsWidget ? " (and the widget sizes to it)." : "."));
            List<string> shown = v.TabsShown;
            foreach (string tabName in RaView.Tabs)
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
                p.Slider("Upcoming: days ahead", 7, 120, delegate { return v.Days; }, delegate (double d) { v.Days = (int)d; });
            p.Check("Posters", delegate { return v.Posters; }, delegate (bool on) { v.Posters = on; });
            p.Check("Show the \"Radarr\" button", delegate { return v.ShowOpen; }, delegate (bool on) { v.ShowOpen = on; });
            p.Check("Show health warnings and errors", delegate { return v.Health; }, delegate (bool on) { v.Health = on; p.Redraw(); });
            if (!p.IsWidget)
            {
                p.Check("Badge with the number in the queue", delegate { return v.Badge; }, delegate (bool on) { v.Badge = on; });
                p.Check("Show how far the downloads are and the time left", delegate { return v.BarProgress; }, delegate (bool on) { v.BarProgress = on; });
                p.Check("Hide while nothing is in the queue", delegate { return v.HideIdle; }, delegate (bool on) { v.HideIdle = on; });
                p.Note(v.Health ? "It still shows when Radarr has a health error or can't be reached." : "It still shows when Radarr can't be reached.");
                p.Title(cfg, "");
            }
            p.Note((p.IsWidget ? "" : "On a bar: the icon with a badge for the queue (red when a download needs a look, hover for the list) - click it for the popup. ") +
                   "Click a movie to open it in Radarr, right-click it for more (remove a download from the queue, search again). Missing has a Search button for each movie and Search all.");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(4, ModulePics.Badge(ModulePics.G(0xE714, t.Fg, 16), "2", t.MainAccent, 8), ModulePics.T(t, "64%", t.Fg, 12.5), ModulePics.T(t, "· 12 min", t.FgDim, 12.5));
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
            return ModulePics.Col(6, ModulePics.Row(4, ModulePics.T(t, "Queue", fg, 11, true), ModulePics.T(t, "Upcoming", dim, 11), ModulePics.T(t, "Missing", dim, 11)),
                                  row(55, "Dune: Part Three", "64% · 12 min left", acc), row(75, "The Long Walk", "Waiting to import", StatusColors.Good));
        }

        // =================================================================
        // The run box: !ra alone -> the queue; !ra title -> search Radarr, and add from the results
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
            if (box.PluginState.TryGetValue("radarr", out o)) st = (BoxState)o;
            else { st = new BoxState(); box.PluginState["radarr"] = st; }
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
                RadarrClient.Poke();
                st.Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += delegate
                {
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != "Radarr" || now.Query.Length > 0 || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            if (!RadarrClient.Configured(S)) { box.Status("Set up Radarr on the Plugins page in Settings first."); return; }
            RadarrSnapshot snap = RadarrClient.Latest;
            if (!snap.Ok) { box.Status(snap.Error.Length > 0 ? "Radarr: " + snap.Error : "Asking Radarr…"); return; }
            string glyph = RadarrList.G(RadarrList.Glyph), bang = RaSettings.Of(S).Bang;
            int k = 0;
            foreach (RadarrQueueItem q in snap.Queue)
            {
                if (k++ >= 9) break;
                RadarrMovie m = q.Movie;
                RunPanel.Row row = box.AddRow(glyph, m.Name, q.StateText, delegate { box.Remember(box.Text); box.Close(); RunPanel.Open(RadarrClient.MovieUrl(S, m)); }, null);
                if (q.Problem) ((TextBlock)((Grid)row.View.Child).Children[0]).Foreground = StatusColors.Bad;
            }
            if (snap.Queue.Count > 0)
            {
                box.Status(snap.Queue.Count + " in the queue  ·  Enter: open it in Radarr  ·  !" + bang + " title to search and add" + (snap.Queue.Count > 9 ? "  ·  " + (snap.Queue.Count - 9) + " more in Radarr" : ""));
                box.Select(0);
                return;
            }
            // nothing downloading: what's coming out next
            k = 0;
            foreach (RadarrMovie m in snap.Upcoming)
            {
                DateTime w;
                string when = m.ReleaseText(out w);
                if (m.HasFile || w < DateTime.Today) continue;
                if (k++ >= 6) break;
                RadarrMovie mm = m;
                box.AddRow(glyph, m.Name, when, delegate { box.Remember(box.Text); box.Close(); RunPanel.Open(RadarrClient.MovieUrl(S, mm)); }, null);
            }
            box.Status("Nothing in the queue" + (k > 0 ? " - coming out soon" : "") + "  ·  !" + bang + " title to search and add");
            box.Select(0);
        }

        // ---- the search results: poster, title, in the library or not, Add / Open in Radarr ----
        static async void Search(RunPanel box, BoxState st, string query)
        {
            int gen = ++st.SearchGen;
            box.Hold();
            BarSettings S = box.Settings;
            if (!RadarrClient.Configured(S)) { box.Status("Set up Radarr on the Plugins page in Settings first."); return; }
            box.Status("Searching Radarr for " + query + "…");
            List<RadarrMovie> items;
            try { items = await RadarrClient.SearchAsync(S, query); }
            catch (Exception ex) { if (gen == st.SearchGen) box.Status("Radarr: " + ex.Message); return; }
            if (gen != st.SearchGen || !box.Held) return;
            box.Status(items.Count == 0 ? "Nothing found for " + query + "." : null);
            int n = 0;
            foreach (RadarrMovie m in items)
            {
                if (n++ >= 8) break;
                box.AddElement(ResultRow(box, m));
            }
        }

        static FrameworkElement ResultRow(RunPanel box, RadarrMovie m)
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
            var meta = new TextBlock { FontSize = Math.Max(8, t.FontSize * 0.85), Foreground = t.FgDim, Margin = new Thickness(0, 1, 0, 4) };
            if (m.InLibrary)
                meta.Inlines.Add(new System.Windows.Documents.Run(m.HasFile ? "In your library · downloaded" : "In your library · not downloaded yet") { Foreground = m.HasFile ? StatusColors.Good : StatusColors.Warn });
            else meta.Inlines.Add(new System.Windows.Documents.Run("Not in Radarr"));
            info.Children.Add(meta);
            var buttons = new WrapPanel();
            var msg = new TextBlock { Foreground = t.FgDim, FontSize = Math.Max(8, t.FontSize * 0.85), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            if (!m.InLibrary)
                buttons.Children.Add(Pill(t, "Add", true, delegate { DoAdd(box, m, msg, buttons); }));
            else if (!m.HasFile)
                buttons.Children.Add(Pill(t, "Search now", true, delegate { DoSearch(box, m, msg); }));
            buttons.Children.Add(Pill(t, "Open in Radarr", false, delegate { box.Close(); RunPanel.Open(RadarrClient.MovieUrl(box.Settings, m)); }));
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

        static async void DoAdd(RunPanel box, RadarrMovie m, TextBlock msg, Panel buttons)
        {
            Theme t = box.Theme;
            msg.Visibility = Visibility.Visible;
            msg.Foreground = t.FgDim;
            msg.Text = "Adding…";
            buttons.IsEnabled = false;
            string err = await RadarrClient.AddAsync(box.Settings, m);
            if (err == null)
            {
                msg.Foreground = StatusColors.Good;
                msg.Text = RaSettings.Of(box.Settings).SearchOnAdd ? "Added ✓ - Radarr is looking for it" : "Added ✓";
                buttons.IsEnabled = true;
                buttons.Children.RemoveAt(0);      // Add (Open in Radarr stays, now on the movie's page)
            }
            else
            {
                msg.Foreground = StatusColors.Bad;
                msg.Text = err;
                buttons.IsEnabled = true;
            }
        }

        static async void DoSearch(RunPanel box, RadarrMovie m, TextBlock msg)
        {
            msg.Visibility = Visibility.Visible;
            msg.Foreground = box.Theme.FgDim;
            msg.Text = "Asking Radarr…";
            string err = await RadarrClient.SearchMovieAsync(box.Settings, m.Id);
            msg.Foreground = err == null ? StatusColors.Good : StatusColors.Bad;
            msg.Text = err ?? "Searching ✓";
        }
    }
}
