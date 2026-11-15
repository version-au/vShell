using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("seerr", "Seerr", Description = "Requests and what's new in Seerr (Overseerr / Jellyseerr): a module, notifications, and !s in the run box to search, request, approve and decline.",
    Glyph = "\uE8B2", Group = "Media", Author = "version", Adds = "!s in the run box (search, request, approve), notifications, palette commands", Types = "Seerr")]

namespace vShell.Ui
{
    /// <summary>
    /// The Seerr plugin (Overseerr / Jellyseerr): the module, the Integrations and Notifications sections, the notifications, and !s in the
    /// run box (search and request, the requests waiting for approval, what was added lately).
    /// </summary>
    public sealed class SeerrPlugin : IPlugin
    {
        Action<string, string, string, bool> _notice;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.Seerr, Display = "Seerr requests", ShortName = "Seerr", Glyph = "", Group = "Smart home & more",
                Blurb = "requests, new arrivals", HasPopup = true,
                Needs = delegate (BarSettings s) { return SeerrClient.Configured(s) ? null : "Needs the Seerr connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new SeerrModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(SeerrClient.Configured);
            host.AddIntegration("Seerr", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(SeerrClient.Configure);

            host.AddNotifyKind("Seerr.New", NotifyLevel.Normal);
            host.AddNotifyKind("Seerr.Available", NotifyLevel.Normal);
            host.AddNotifySource("Seerr");
            _notice = delegate (string title, string detail, string url, bool bad)
            {
                string k = title != null && title.StartsWith("New request") ? "Seerr.New" : "Seerr.Available";
                Notify.Post("Seerr", title, detail, url, bad ? NotifyLevel.Alert : Notify.KindOf(k));
            };
            SeerrClient.Notice += _notice;

            host.AddBang("Seerr", delegate (string key, string q, BarSettings s)
            {
                SrSettings c = SrSettings.Of(s);
                if (key != c.Bang || string.IsNullOrWhiteSpace(c.Url)) return null;
                return new RunAction
                {
                    Kind = "Seerr", Query = q,
                    Display = q.Length == 0 ? "Seerr: requests waiting for approval" : q.Equals("recent", StringComparison.OrdinalIgnoreCase) ? "Seerr: recently added" : "Search Seerr for " + q
                };
            }, Rows, delegate (BarSettings s) { return SeerrClient.Configured(s) ? "!" + SrSettings.Of(s).Bang + " Seerr" : null; });
            host.AddCommands("Seerr", Commands);
        }

        // =================================================================
        // The command palette: approve / decline what's waiting, open Seerr
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!SeerrClient.Configured(s)) return;
            const string Src = "Seerr";
            string web = SrSettings.Of(s).Url.Trim().TrimEnd('/');
            if (web.Length > 0 && !web.Contains("://")) web = "http://" + web;
            foreach (string[] page in new[] { new[] { "Open Seerr", "" }, new[] { "Open Seerr's requests", "/requests" }, new[] { "Open the requests waiting in Seerr", "/requests?filter=pending" } })
            {
                string url = web + page[1];
                into.Add(new PaletteCommand
                {
                    Title = page[0], Detail = Src, Glyph = "", Source = Src, Words = "seerr overseerr jellyseerr requests web page", Opens = true,
                    Run = delegate { RunPanel.Open(url); return System.Threading.Tasks.Task.FromResult(""); }
                });
            }
            SeerrSnapshot snap = SeerrClient.Latest;
            if (snap == null || !snap.CanManage) return;
            foreach (SeerrRequest r0 in snap.Pending)
            {
                SeerrRequest r = r0;
                string what = r.Title + (r.Year.Length > 0 ? " (" + r.Year + ")" : "");
                string detail = Src + " · " + r.KindText + (r.RequestedBy.Length > 0 ? " · " + r.RequestedBy : "") + (r.Is4k ? " · 4K" : "");
                into.Add(new PaletteCommand
                {
                    Title = "Approve the request for " + what, Detail = detail, Glyph = "", Source = Src, Words = "seerr request pending allow accept " + r.RequestedBy, Boost = 1,
                    Run = async delegate { string err = await SeerrClient.DecideAsync(s, r.Id, true); return err ?? PaletteCommand.Worked("Approved " + what); }
                });
                into.Add(new PaletteCommand
                {
                    Title = "Decline the request for " + what, Detail = detail, Glyph = "", Source = Src, Words = "seerr request pending deny reject " + r.RequestedBy, Ask = true,
                    Run = async delegate { string err = await SeerrClient.DecideAsync(s, r.Id, false); return err ?? PaletteCommand.Worked("Declined " + what); }
                });
            }
        }

        public void Unload()
        {
            if (_notice != null) SeerrClient.Notice -= _notice;
            SeerrClient.Stop();
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            SrSettings c = SrSettings.Of(S);
            p.Header("Seerr (Overseerr / Jellyseerr)");
            p.Text("Address (for example http://192.168.1.20:5055 or https://seerr.example.com)", delegate { return c.Url; }, delegate (string v) { c.Url = v; });
            PasswordBox pw = p.Secret("API key", "API key", delegate { return c.ApiKey; }, delegate (string v) { c.ApiKey = v; },
                                      "In Seerr open Settings > General and copy the API key. It is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Text("Request as user id (empty or 0 = the API key's user, usually the admin; a user's id is in the address of their Seerr profile page)",
                   delegate { return c.UserId > 0 ? c.UserId.ToString() : ""; },
                   delegate (string v) { int id; c.UserId = int.TryParse(v.Trim(), out id) && id > 0 ? id : 0; });
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "Search Seerr from a run box with !" + c.Bang + " Movie Title, and request movies and shows from the results.\n" +
                   "!" + c.Bang + " on its own lists the requests waiting for approval - Enter on one, then Enter on Approve (Decline needs Enter twice).\n" +
                   "!" + c.Bang + " recent shows what was added to the library lately.");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.ApiKey = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return SeerrClient.TestAsync(S); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            SrSettings c = SrSettings.Of(S);
            p.NotifySection("Seerr notifications", SeerrClient.Configured(S), "Seerr");
            p.Check("A new request is waiting for approval (when the API key can approve)", delegate { return c.NotifyNew; }, delegate (bool v) { c.NotifyNew = v; p.Redraw(); });
            if (c.NotifyNew) p.Kind("New requests show as", "Seerr.New");
            p.Combo("When a request becomes available", SrSettings.NotifyAvailableChoices, delegate { return c.NotifyAvailable; }, delegate (string v) { c.NotifyAvailable = v; p.Redraw(); });
            if (c.NotifyAvailable != "Off")
            {
                p.Note("Mine = the requests made as the \"Request as user id\" user on the Connection tab (or the API key's user).");
                p.Kind("Available requests show as", "Seerr.Available");
            }
            p.Note("Notifications work without a Seerr module too (vShell then checks every 3 minutes instead of every 30 seconds).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            SrView v = new SrView(cfg);
            if (!SeerrClient.Configured(p.Settings)) p.Note("Set up the Seerr connection first (Plugins page in this window).");
            // the tabs to show: tick any; one ticked = just that view with no tabs (a widget sizes to it), more = tabs for those
            p.Note("Tabs to show - with only one ticked, just that view shows, without tabs" + (p.IsWidget ? " (and the widget sizes to it)." : "."));
            List<string> shown = v.TabsShown;
            foreach (string tabName in SrView.Tabs)
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
            bool reqs = shown.Contains("Requests");
            if (reqs) p.Combo("Requests to show", SrView.Filters, delegate { return v.Filter; }, delegate (string s) { v.Filter = s; });
            if (p.IsWidget && reqs) p.Check("Show the filters (All, Pending, Processing, Available, Mine)", delegate { return v.ShowFilters; }, delegate (bool on) { v.ShowFilters = on; });
            p.Slider("How many", 1, 40, delegate { return v.Count; }, delegate (double d) { v.Count = (int)d; });
            p.Check("Posters", delegate { return v.Posters; }, delegate (bool on) { v.Posters = on; p.Redraw(); });
            if (v.Posters)
                p.Slider("Rows of posters (the posters per row follow from How many)", 1, 10, delegate { return v.PosterRows; }, delegate (double d) { v.PosterRows = (int)d; });
            p.Check("Show the \"Seerr\" button", delegate { return v.ShowOpen; }, delegate (bool on) { v.ShowOpen = on; });
            if (!p.IsWidget)
            {
                p.Check("Badge with the number waiting for approval", delegate { return v.Badge; }, delegate (bool on) { v.Badge = on; });
                p.Check("Show the number processing", delegate { return v.BarProcessing; }, delegate (bool on) { v.BarProcessing = on; });
                p.Check("Show the number available", delegate { return v.BarAvailable; }, delegate (bool on) { v.BarAvailable = on; });
                p.Title(cfg, "");
            }
            p.Note((p.IsWidget ? "" : "On a bar: the icon with a badge for the requests waiting for approval (hover for the latest ones) - click it for the popup. ") +
                   "Pending requests have Approve / Decline buttons when the API key is allowed to approve (Decline asks first). Click a request or poster to open it in Seerr, " +
                   "right-click a request for more. Trending and Upcoming have a Request button (shows let you pick the seasons).");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(4, ModulePics.Badge(ModulePics.G(0xE8B2, t.Fg, 16), "2", t.MainAccent, 8), ModulePics.T(t, "3 processing", t.Fg, 12.5));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            var posters = ModulePics.Row(6, ModulePics.Square(ModulePics.Shade(t, 55), 38, 3), ModulePics.Square(ModulePics.Shade(t, 75), 38, 3),
                                            ModulePics.Square(ModulePics.Shade(t, 55), 38, 3), ModulePics.Square(ModulePics.Shade(t, 75), 38, 3));
            foreach (UIElement e in posters.Children) ((Border)e).Height = 56;
            return ModulePics.Col(7, ModulePics.Row(4, ModulePics.T(t, "Requests", fg, 11, true), ModulePics.T(t, "Added", dim, 11), ModulePics.T(t, "Trending", dim, 11)), posters,
                                  ModulePics.Row(6, ModulePics.T(t, "Pending", StatusColors.Warn, 10), ModulePics.T(t, "Ready", StatusColors.Good, 10),
                                                    ModulePics.T(t, "Getting", acc, 10), ModulePics.T(t, "Ready", StatusColors.Good, 10)));
        }

        // =================================================================
        // The run box: !s alone -> the requests waiting for approval (Enter -> Approve / Decline); !s recent -> recently added;
        // !s title -> search Seerr, and request from the results
        // =================================================================
        /// <summary>What one run box shows for !s.</summary>
        sealed class BoxState
        {
            public SeerrRequest Req;        // the request whose actions are shown (null = the list)
            public bool Armed;              // Decline: Enter once arms it, Enter again does it
            public string Done;             // what the last action did (shown once the list is back)
            public readonly HashSet<int> Handled = new HashSet<int>();
            public System.Windows.Threading.DispatcherTimer Timer;
            public int SearchGen;
        }

        static string SrGlyph(bool tv) { return ((char)(tv ? 0xE7F4 : 0xE8B2)).ToString(); }

        static bool ListQuery(string q) { return q.Length == 0 || q.Equals("recent", StringComparison.OrdinalIgnoreCase); }

        static void Rows(RunPanel box, RunAction a)
        {
            object o;
            BoxState st;
            if (box.PluginState.TryGetValue("seerr", out o)) st = (BoxState)o;
            else { st = new BoxState(); box.PluginState["seerr"] = st; }
            BarSettings S = box.Settings;

            if (!ListQuery(a.Query))
            {
                // a title: Enter searches (the results replace the list)
                st.Req = null;
                string query = a.Query;
                box.AddRow("", a.Display, null, delegate { box.Remember(box.Text); Search(box, st, query); }, null);
                box.Select(0);
                return;
            }

            bool recent = a.Query.Length > 0;
            if (st.Timer == null)
            {
                SeerrClient.Poke();
                st.Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                st.Timer.Tick += delegate
                {
                    RunAction now = RunParser.Parse(box.Text, box.Settings);
                    if (now.Kind != "Seerr" || !ListQuery(now.Query) || !box.IsVisible) { st.Timer.Stop(); st.Timer = null; st.Req = null; st.Armed = false; return; }
                    int keep = box.Selected;
                    box.Redraw();
                    box.Select(keep);
                };
                st.Timer.Start();
            }
            if (!SeerrClient.Configured(S)) { box.Status("Set up Seerr on the Plugins page in Settings first."); return; }
            SeerrSnapshot snap = SeerrClient.Latest;
            if (!snap.Ok) { box.Status(snap.Error.Length > 0 ? "Seerr: " + snap.Error : "Asking Seerr…"); return; }
            string url;

            if (recent)
            {
                st.Req = null;
                int n = 0;
                foreach (SeerrMedia m in snap.Recent)
                {
                    if (n++ >= 9) break;
                    string u = SeerrClient.PageUrl(S, m.MediaType, m.TmdbId);
                    box.AddRow(SrGlyph(m.IsTv), m.Title + (m.Year.Length > 0 ? "  (" + m.Year + ")" : ""), (m.IsTv ? "TV" : "Movie") + " · added " + SeerrClient.Ago(m.Added),
                               delegate { box.Remember(box.Text); box.Close(); try { RunPanel.Open(u); } catch { } }, null);
                }
                if (snap.Recent.Count == 0) { SeerrClient.Poke(); box.Status("Asking Seerr for what was added lately…"); }
                else box.Status("Recently added  ·  Enter: open it in Seerr");
                box.Select(0);
                return;
            }

            if (st.Req != null)
            {
                // the chosen request's actions
                SeerrRequest q = st.Req;
                if (snap.CanManage && q.State == "pending" && !st.Handled.Contains(q.Id))
                {
                    box.AddRow("", "Approve", null, delegate { st.Armed = false; Decide(box, st, q, true); }, null);
                    RunPanel.Row dr = box.AddRow("", st.Armed ? "Press Enter again: decline " + q.Title : "Decline", st.Armed ? "confirm" : null, delegate
                    {
                        if (!st.Armed) { st.Armed = true; int keep = box.Selected; box.Redraw(); box.Select(keep); return; }
                        st.Armed = false;
                        Decide(box, st, q, false);
                    }, null);
                    if (st.Armed) dr.View.Background = Theme.Faded(box.Theme.MainAccent, 25);
                }
                url = SeerrClient.PageUrl(S, q.MediaType, q.TmdbId);
                box.AddRow("", "Open in Seerr", null, delegate { box.Remember(box.Text); box.Close(); try { RunPanel.Open(url); } catch { } }, null);
                box.AddRow("", "Back to the list", null, delegate { st.Req = null; st.Armed = false; box.Redraw(); }, null);
                box.Status(q.Title + (q.Year.Length > 0 ? " (" + q.Year + ")" : "") + " · " + q.KindText + (q.RequestedBy.Length > 0 ? " · requested by " + q.RequestedBy : "") +
                           " · " + SeerrClient.Ago(q.Created) + (snap.CanManage ? "" : " · the API key can't approve requests"));
                box.Select(st.Armed ? 1 : 0);
                return;
            }

            // the list: waiting for approval (oldest first); none -> the latest requests
            List<SeerrRequest> pending = snap.Pending.FindAll(delegate (SeerrRequest r) { return !st.Handled.Contains(r.Id); });
            List<SeerrRequest> show = pending.Count > 0 ? pending : snap.Requests;
            int k = 0;
            foreach (SeerrRequest r in show)
            {
                if (k++ >= 9) break;
                SeerrRequest req = r;
                string tag = r.KindText + " · " + (pending.Count > 0 ? SeerrClient.Ago(r.Created) : r.StateText);
                RunPanel.Row row = box.AddRow(SrGlyph(r.IsTv), r.Title + (r.Year.Length > 0 ? "  (" + r.Year + ")" : "") + (r.RequestedBy.Length > 0 ? "   " + r.RequestedBy : ""), tag, delegate
                {
                    if (pending.Count == 0)
                    {
                        string u = SeerrClient.PageUrl(S, req.MediaType, req.TmdbId);
                        box.Remember(box.Text); box.Close(); try { RunPanel.Open(u); } catch { }
                        return;
                    }
                    st.Req = req; st.Armed = false;
                    box.Redraw();
                }, null);
                if (pending.Count > 0) ((TextBlock)((Grid)row.View.Child).Children[0]).Foreground = SeerrList.Busy;
            }
            string done = st.Done;
            st.Done = null;
            string bang = SrSettings.Of(S).Bang;
            if (pending.Count > 0)
                box.Status((done != null ? done + "  ·  " : "") + pending.Count + " waiting for approval  ·  Enter: approve or decline" + (pending.Count > 9 ? "  ·  " + (pending.Count - 9) + " more in Seerr" : ""));
            else
                box.Status((done != null ? done + "  ·  " : "") + "Nothing waiting for approval - the latest requests (Enter opens one)  ·  !" + bang + " recent  ·  !" + bang + " title to search");
            box.Select(0);
        }

        static async void Decide(RunPanel box, BoxState st, SeerrRequest q, bool approve)
        {
            box.Status((approve ? "Approving " : "Declining ") + q.Title + "…");
            string err = await SeerrClient.DecideAsync(box.Settings, q.Id, approve);
            if (err != null) { box.Status("Seerr: " + err); return; }
            box.Remember(box.Text);
            st.Handled.Add(q.Id);
            st.Req = null;
            st.Done = (approve ? "Approved " : "Declined ") + q.Title + " ✓";
            box.Redraw();
        }

        // ---- the search results: poster, title, state, Request / Open in Seerr ----
        static async void Search(RunPanel box, BoxState st, string query)
        {
            int gen = ++st.SearchGen;
            box.Hold();
            BarSettings S = box.Settings;
            if (!SeerrClient.Configured(S)) { box.Status("Set up Seerr on the Plugins page in Settings first."); return; }
            box.Status("Searching Seerr for " + query + "…");
            List<SeerrItem> items;
            try { items = await SeerrClient.SearchAsync(S, query); }
            catch (Exception ex) { if (gen == st.SearchGen) box.Status("Seerr: " + ex.Message); return; }
            if (gen != st.SearchGen || !box.Held) return;
            box.Status(items.Count == 0 ? "Nothing found for " + query + "." : null);
            int n = 0;
            foreach (SeerrItem it in items)
            {
                if (n++ >= 8) break;
                box.AddElement(ResultRow(box, it));
            }
        }

        static FrameworkElement ResultRow(RunPanel box, SeerrItem it)
        {
            Theme t = box.Theme;
            var g = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var poster = new Border { Width = 36, Height = 54, CornerRadius = new CornerRadius(4), Background = Theme.Faded(t.Fg, 10), VerticalAlignment = VerticalAlignment.Top };
            if (it.Poster.Length > 0)
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(it.Poster);
                    bmp.DecodePixelWidth = 72;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    poster.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                }
                catch { }
            g.Children.Add(poster);

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = it.Title + (it.Year.Length > 0 ? "  (" + it.Year + ")" : ""), Foreground = t.Fg, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Brush stc = it.Status >= 4 ? StatusColors.Good : it.Status >= 2 ? StatusColors.Warn : t.FgDim;
            var meta = new TextBlock { FontSize = Math.Max(8, t.FontSize * 0.85), Foreground = t.FgDim, Margin = new Thickness(0, 1, 0, 4) };
            meta.Inlines.Add(new System.Windows.Documents.Run((it.IsTv ? "TV show" : "Movie") + "  ·  "));
            meta.Inlines.Add(new System.Windows.Documents.Run(it.StatusText) { Foreground = stc });
            info.Children.Add(meta);
            var buttons = new WrapPanel();
            var msg = new TextBlock { Foreground = t.FgDim, FontSize = Math.Max(8, t.FontSize * 0.85), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            var seasonsHost = new StackPanel();
            if (it.CanRequest)
                buttons.Children.Add(Pill(t, it.IsTv ? "Request…" : "Request", true, delegate
                {
                    if (it.IsTv) ShowSeasons(box, it, seasonsHost, msg);
                    else DoRequest(box, it, null, msg, buttons);
                }));
            buttons.Children.Add(Pill(t, "Open in Seerr", false, delegate { box.Close(); try { RunPanel.Open(SeerrClient.PageUrl(box.Settings, it)); } catch (Exception ex) { Log.Write("Seerr open: " + ex.Message); } }));
            info.Children.Add(buttons);
            info.Children.Add(seasonsHost);
            info.Children.Add(msg);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);
            if (it.Overview.Length > 0) g.ToolTip = new TextBlock { Text = it.Overview, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            return new Border { Child = g, Padding = new Thickness(4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        }

        static Border Pill(Theme t, string text, bool primary, Action act)
        {
            Border b = Pills.Make(t, "", text, null, primary, act);
            b.Margin = new Thickness(0, 0, 6, 2);
            return b;
        }

        static async void ShowSeasons(RunPanel box, SeerrItem it, StackPanel host, TextBlock msg)
        {
            Theme t = box.Theme;
            host.Children.Clear();
            msg.Visibility = Visibility.Visible;
            msg.Text = "Getting the seasons…";
            List<SeerrSeason> seasons;
            try { seasons = await SeerrClient.SeasonsAsync(box.Settings, it.Id); }
            catch (Exception ex) { msg.Text = "Seerr: " + ex.Message; return; }
            msg.Visibility = Visibility.Collapsed;
            var all = new CheckBox { Content = "All seasons", IsChecked = true, Focusable = false, Foreground = t.Fg, Margin = new Thickness(0, 4, 0, 2) };
            host.Children.Add(all);
            var boxes = new List<KeyValuePair<CheckBox, int>>();
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (SeerrSeason ss in seasons)
            {
                bool have = ss.Status >= 2;
                var cb = new CheckBox
                {
                    Content = "S" + ss.Number + (have ? " (" + (ss.Status >= 5 ? "have" : "requested") + ")" : ""), IsEnabled = !have,
                    IsChecked = false, Focusable = false, Foreground = have ? t.FgDim : t.Fg, Margin = new Thickness(0, 2, 10, 2)
                };
                cb.Checked += delegate { all.IsChecked = false; };
                boxes.Add(new KeyValuePair<CheckBox, int>(cb, ss.Number));
                wrap.Children.Add(cb);
            }
            all.Checked += delegate { foreach (var kv in boxes) kv.Key.IsChecked = false; };
            host.Children.Add(wrap);
            var buttons = new WrapPanel();
            buttons.Children.Add(Pill(t, "Request", true, delegate
            {
                List<int> pick = null;
                if (all.IsChecked != true)
                {
                    pick = new List<int>();
                    foreach (var kv in boxes) if (kv.Key.IsChecked == true) pick.Add(kv.Value);
                    if (pick.Count == 0) { msg.Visibility = Visibility.Visible; msg.Text = "Tick the seasons you want (or All seasons)."; return; }
                }
                DoRequest(box, it, pick, msg, buttons);
            }));
            host.Children.Add(buttons);
        }

        static async void DoRequest(RunPanel box, SeerrItem it, List<int> seasons, TextBlock msg, Panel buttons)
        {
            Theme t = box.Theme;
            msg.Visibility = Visibility.Visible;
            msg.Foreground = t.FgDim;
            msg.Text = "Requesting…";
            buttons.IsEnabled = false;
            string err = await SeerrClient.RequestAsync(box.Settings, it, seasons);
            if (err == null)
            {
                msg.Foreground = StatusColors.Good;
                msg.Text = "Requested ✓";
            }
            else
            {
                msg.Foreground = StatusColors.Bad;
                msg.Text = err;
                buttons.IsEnabled = true;
            }
        }
    }
}
