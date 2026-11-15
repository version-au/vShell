using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    public sealed partial class SettingsWindow
    {
        /// <summary>
        /// What a plugin builds its settings with (its Integrations section, its Notifications section, its module settings): the same rows
        /// the rest of this window uses, so a plugin's settings look like everyone else's.
        /// </summary>
        [PluginApi]
        public sealed class SettingsPage
        {
            readonly SettingsWindow _w;
            readonly object _pane;      // the module pane (module settings), else null
            public readonly StackPanel Panel;

            internal SettingsPage(SettingsWindow w, StackPanel p, object pane) { _w = w; Panel = p; _pane = pane; }

            public BarSettings Settings { get { return _w.S; } }
            /// <summary>Module settings: the module is in a widget (false on a bar, and on the Integrations / Notifications pages).</summary>
            public bool IsWidget { get { return _pane is ModulePane && ((ModulePane)_pane).IsWidget; } }
            public Window Window { get { return _w; } }

            public void Header(string text) { _w.Header(Panel, text); }
            public void Note(string text) { _w.Note(Panel, text); }
            public void Text(string label, Func<string> get, Action<string> set, string info = null) { _w.TextRow(Panel, label, get, set, info); }
            /// <summary>A password / key: stored encrypted (Secret.Protect) when you leave the box.</summary>
            public PasswordBox Secret(string label, string what, Func<string> get, Action<string> set, string info = null) { return _w.SecretRow(Panel, label, what, get, set, info); }
            public CheckBox Check(string text, Func<bool> get, Action<bool> set) { return _w.Check(Panel, text, get, set); }
            public void Combo(string label, string[] items, Func<string> get, Action<string> set) { _w.ComboRow(Panel, label, items, get, set); }
            public void Slider(string label, double min, double max, Func<double> get, Action<double> set) { _w.SliderRow(Panel, label, min, max, get, set); }
            /// <summary>"Test connection" and its answer (before = save what's typed first).</summary>
            public void Test(Action before, Func<Task<KeyValuePair<bool, string>>> test) { _w.TestRow(Panel, before, test); }
            /// <summary>A module's title row (bars).</summary>
            public void Title(ModuleConfig cfg, string fallback) { _w.TitleRow(Panel, cfg, fallback); }
            /// <summary>Its page's Notifications tab: a card with this title (ok = its connection is set up; else a line says to fill it in).</summary>
            public void NotifySection(string title, bool ok, string what) { _w.NotifySection(Panel, title, ok, what); }
            public void Delay(Func<int> get, Action<int> set) { _w.DelayRow(Panel, get, set); }
            public void Kind(string label, string key) { _w.KindPick(Panel, label, key); }
            /// <summary>The settings changed (apply them now - text rows and check boxes do this themselves).</summary>
            public void Changed() { _w._mgr.SettingsChanged(); }
            /// <summary>Module settings: show them again (a choice that shows or hides other rows). On its page in Plugins: that page again
            /// (something was added or removed - a printer).</summary>
            public void Redraw()
            {
                if (_pane != null) _w.Dispatcher.BeginInvoke(new Action(delegate { _w.ShowModuleProps((ModulePane)_pane); }));
                else _w.RebuildIntegrations();
            }
            /// <summary>A heading inside module settings ("List", "Text").</summary>
            public void Section(string title) { _w.Section(Panel, title); }
            public void Number(string label, Func<double> get, Action<double> set) { _w.NumberRow(Panel, label, get, set); }
            /// <summary>A colour ("#AARRGGBB" or a theme slot); optional = may be empty (the default), theme = offer the theme's colours;
            /// emptySlot = the theme colour an empty one shows as (Theme.SlotMain ...).</summary>
            public void Color(string label, Func<string> get, Action<string> set, bool optional, bool theme, string emptySlot = null) { _w.ColorRow(Panel, label, get, set, optional, theme, emptySlot); }
            /// <summary>A labelled row around a control of the plugin's own (stacked = the control under the label).</summary>
            public void Row(string label, UIElement control, bool stacked = false, string info = null) { _w.Place(Panel, _w.Row(label, control, stacked, info)); }
            /// <summary>Something of the plugin's own, placed like the other rows.</summary>
            public void Add(UIElement e) { _w.Place(Panel, e); }
            /// <summary>A button on its own line ("Choose entities… (3)").</summary>
            public void ListButton(string caption, Action click) { _w.ListButton(Panel, caption, click); }
            /// <summary>A text in the settings window's colours (brushKey "vsFg" / "vsDim").</summary>
            public TextBlock Txt(string text, string brushKey, double size, bool bold = false) { return SettingsWindow.Txt(text, brushKey, size, bold); }
            /// <summary>A button in the settings window's look.</summary>
            public Button Btn(string text, Action click, bool primary = false) { return SettingsWindow.Btn(text, click, primary); }
            /// <summary>The page is being built (changes made now aren't the user's).</summary>
            public bool Loading { get { return _w._loading; } }
            /// <summary>The graph settings vShell's own gauge modules have (style, size, colours, the warning); extra = rows of the
            /// module's own after the style.</summary>
            public void GraphSettings(ModuleConfig cfg, bool withStyle, Action extra) { _w.GraphSettings(Panel, cfg, withStyle, extra); }
            /// <summary>
            /// The graph look rows on their own (line thickness, fill, curves, box, grid, time shown, colour by load), for a module whose
            /// graphs the plugin draws itself - all of them, whatever the module's Style. Apply them with MetricView.ApplyLook. Since vShell 0.38.139.
            /// </summary>
            public void GraphLook(ModuleConfig cfg) { _w.GraphLookRows(Panel, cfg, true); _w.LoadColorRows(Panel, cfg, true); }
            /// <summary>
            /// The graph look rows that fit one style, as vShell's own modules show them: Line and Tile - line thickness, fill, curves;
            /// Line and Bars - box, its rounding, grid; those three - time shown; Meter and Ring - none. Then, for every style, the graph
            /// colour (cfg.Color; hidden while Colour by load is on) and Colour by load with its three colours. For a module that keeps its
            /// graph style itself (not in cfg.Style). Apply them with MetricView.ApplyLook. Since vShell 0.38.185 (the colours since 0.38.186).
            /// </summary>
            public void GraphLook(ModuleConfig cfg, string style)
            {
                _w.GraphLookRows(Panel, cfg, false, style ?? "");
                if (!cfg.LoadColors) _w.ColorRow(Panel, "Graph colour", delegate { return cfg.Color; }, delegate (string v) { cfg.Color = v; }, true, false);
                _w.LoadColorRows(Panel, cfg, true);
            }
            /// <summary>A labelled row with a button ("Remove this printer" [Remove]); primary = the accent colour.</summary>
            public Button Button(string label, string text, Action click, bool primary = false)
            {
                Button b = Btn(text, click, primary);
                _w.Place(Panel, _w.Row(label, b));
                return b;
            }
            /// <summary>
            /// Go to a plugin's own page in Plugins, on its settings (a link from its module's settings); heading = scroll to the section with
            /// that Header (null = the top). Since vShell 0.38.180.
            /// </summary>
            public void OpenPlugin(string id, string heading = null)
            {
                _w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    _w.OpenPlugin(id, "Connection");
                    if (!string.IsNullOrEmpty(heading)) _w.Dispatcher.BeginInvoke(new Action(delegate { _w.ScrollToHeading(heading); }), System.Windows.Threading.DispatcherPriority.Loaded);
                }));
            }
        }

        /// <summary>Scroll the page shown so the heading with that text is at the top.</summary>
        void ScrollToHeading(string heading)
        {
            var page = _content.Content as DependencyObject;
            TextBlock hit = page != null ? FindText(page, heading) : null;
            if (hit == null) return;
            Point at = hit.TranslatePoint(new Point(0, 0), (UIElement)_content.Content);
            _content.ScrollToVerticalOffset(Math.Max(0, at.Y - 8));
        }

        static TextBlock FindText(DependencyObject d, string text)
        {
            var tb = d as TextBlock;
            if (tb != null && tb.Text == text) return tb;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                TextBlock r = FindText(VisualTreeHelper.GetChild(d, i), text);
                if (r != null) return r;
            }
            return null;
        }

        bool _pluginsHooked;

        /// <summary>
        /// The Plugins page again (a plugin was added or removed, a tab picked …), keeping the scroll position - or, while a plugin's own
        /// page shows, that page (a printer added); back to the list when that plugin has gone.
        /// </summary>
        void RebuildIntegrations()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_page == PgPlugin)
                {
                    if (_pluginId == null || !PluginManager.IsLoaded(_pluginId)) { Navigate(_pluginFrom); return; }
                    double at0 = _content.VerticalOffset;
                    BuildPluginPage();
                    _content.ScrollToVerticalOffset(at0);
                    return;
                }
                StackPanel pg = Page(PgHa);
                double at = _content.Content == pg ? _content.VerticalOffset : 0;
                pg.Children.Clear();
                _cardOf.Remove(pg);
                bool old = _loading;
                _loading = true;
                try { BuildIntegrationsPage(pg); } finally { _loading = old; }
                if (_content.Content == pg) _content.ScrollToVerticalOffset(at);
            }));
        }

        // ---- the Plugins page: Added (each opens its own page) / Get more (search, the catalog by group) ----
        string _pluginSearch = "";
        /// <summary>The tab: Added or Get more (null = Added, or Get more while nothing is added).</summary>
        string _pluginShow;
        StackPanel _pluginList;
        /// <summary>The plugin whose own page shows (or showed last), and the tab picked there (null = its first).</summary>
        string _pluginId, _pluginTab;
        /// <summary>The groups' order in Get more (others follow, A-Z, and Other last).</summary>
        static readonly string[] PluginGroups = { "Smart home", "Servers & network", "Media", "Devices" };

        void BuildPluginsSection(StackPanel p)
        {
            if (!_pluginsHooked)
            {
                _pluginsHooked = true;
                Action changed = delegate { RebuildIntegrations(); };
                PluginManager.Changed += changed;
                Closed += delegate { PluginManager.Changed -= changed; };
            }
            _notesAsInfo.Add(p);
            List<PluginInfo> all;
            lock (PluginStore.Catalog) all = new List<PluginInfo>(PluginStore.Catalog);
            List<PluginInfo> added = all.FindAll(delegate (PluginInfo i) { return PluginManager.IsAdded(i.Id); });
            added.Sort(delegate (PluginInfo a, PluginInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });

            UpdatesBanner(p);
            string show = _pluginShow ?? (added.Count == 0 ? "Get more" : "Added");
            Tabs(p, new[] { "Added", "Get more" }, new[] { "Added (" + added.Count + ")", "Get more (" + (all.Count - added.Count) + ")" }, show,
                 delegate (string k) { _pluginShow = k; RebuildIntegrations(); });
            if (show == "Added") BuildAddedList(p, added);
            else BuildGetMore(p);

            // checking for updates
            Header(p, "Updates");
            int ups = PluginManager.UpdatesAvailable().Count;
            Button check = null;
            check = Btn("Check for updates", async delegate
            {
                check.IsEnabled = false;
                check.Content = "Checking…";
                await PluginManager.CheckUpdatesAsync(false);
                RebuildIntegrations();
            });
            Place(p, Row(UpdatesText(ups), check));
            Check(p, "Update plugins by themselves", delegate { return S.PluginAutoUpdate; }, delegate (bool v) { S.PluginAutoUpdate = v; },
                  "A newer build is downloaded in the background and used from the next start.");
            RefreshPluginCatalog();
        }

        /// <summary>Updates to do (Update all), or done and waiting for a restart (Restart vShell): a banner over the tabs. Nothing otherwise.</summary>
        void UpdatesBanner(StackPanel p)
        {
            List<PluginInfo> ups = PluginManager.UpdatesAvailable();
            if (ups.Count == 0 && !PluginStore.AnyUpdated) return;
            EndSection(p);
            var names = new List<string>();
            foreach (PluginInfo u in ups) names.Add(u.Name + " " + u.Latest.Version);
            string text = ups.Count > 0 ? (ups.Count == 1 ? "An update: " : ups.Count + " updates: ") + string.Join(", ", names)
                                        : "Updated plugins are used once vShell restarts.";
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (ups.Count > 0)
            {
                Button all = null;
                all = Btn(ups.Count == 1 ? "Update" : "Update all", async delegate
                {
                    all.IsEnabled = false;
                    var failed = new List<string>();
                    foreach (PluginInfo u in ups) { string err = await PluginManager.UpdateAsync(u); if (err != null) failed.Add(u.Name + ": " + err); }
                    if (failed.Count > 0) MessageBox.Show(this, "Not updated:\n" + string.Join("\n", failed), "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
                    RebuildIntegrations();
                }, true);
                btns.Children.Add(all);
            }
            if (PluginStore.AnyUpdated)
            {
                var restart = Btn("Restart vShell", delegate
                {
                    string why = PluginManager.Restart();
                    if (why != null) MessageBox.Show(this, why, "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
                }, ups.Count == 0);
                restart.Margin = new Thickness(ups.Count > 0 ? 8 : 0, 0, 0, 0);
                btns.Children.Add(restart);
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Txt(text, "vsFg", 13, false);
            t.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(t);
            Grid.SetColumn(btns, 1);
            g.Children.Add(btns);
            var banner = new Border { Child = g, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 14) };
            Ref(banner, Border.BackgroundProperty, "vsSelBg");
            Ref(banner, Border.BorderBrushProperty, "vsAccent");
            p.Children.Add(banner);
        }

        /// <summary>"Checked 3:12 pm (v0.38.86): 2 updates" / "… all up to date" / "Not checked yet".</summary>
        static string UpdatesText(int count)
        {
            if (PluginStore.CatalogState.Length > 0 && PluginStore.CatalogState != "Reading…") return PluginStore.CatalogState;
            if (PluginStore.CheckedAt == DateTime.MinValue) return "Not checked yet";
            return "Checked " + TimeText.Clock(PluginStore.CheckedAt) + (PluginStore.CatalogFrom.Length > 0 ? " (" + PluginStore.CatalogFrom + ")" : "") + ": " +
                   (count == 0 ? "all up to date" : count == 1 ? "1 update" : count + " updates");
        }

        // ---- Added ----
        void BuildAddedList(StackPanel p, List<PluginInfo> added)
        {
            NewCard(p);
            if (added.Count == 0) Place(p, Txt("No plugins added yet - pick some under Get more.", "vsDim", 12.5, false));
            foreach (PluginInfo info in added) AddedRow(p, info);
            EndSection(p);
        }

        /// <summary>An added plugin: its state in a word and a dot, Set up / Update / Try again when there's something to do; the row opens its page.</summary>
        void AddedRow(StackPanel p, PluginInfo info)
        {
            bool loaded = PluginManager.IsLoaded(info.Id);
            string state = PluginManager.StateOf(info.Id);
            bool busy = state != null && state.StartsWith("Downloading");
            bool? setUp = loaded ? PluginManager.IsSetUp(info.Id) : null;
            string blocked = null, updatedTo = PluginStore.UpdatedTo(info);
            PluginBuild update = updatedTo == null ? PluginStore.UpdateFor(info, out blocked) : null;

            var parts = new List<string>();
            KeyValuePair<string, string>? have = PluginStore.InstalledBuild(info);
            if (have != null && have.Value.Key.Length > 0) parts.Add(have.Value.Key);
            Border dot;
            string word;
            if (!loaded) { parts.Add(state ?? "Not loaded"); dot = Dot(busy ? StatusColors.Warn : StatusColors.Bad); word = busy ? "Downloading" : "Not loaded"; }
            else if (updatedTo != null) { parts.Add("Updated to " + updatedTo + " - restart vShell to use it"); dot = Dot(null, "vsAccent"); word = "Restart vShell to use the update"; }
            else if (PluginManager.NeedsRestart(info)) { parts.Add("Restart vShell to use the new build"); dot = Dot(null, "vsAccent"); word = "Restart vShell to use the new build"; }
            else if (setUp == false) { parts.Add("Not set up yet"); dot = Dot(null, "vsDim"); word = "Not set up yet"; }
            else { parts.Add(Brings(info)); dot = Dot(StatusColors.Good); word = "Working"; }
            dot.ToolTip = word;
            if (blocked != null && loaded) parts.Add(blocked);
            if (info.Author.Length > 0) parts.Add("by " + info.Author);
            if (info.FromFile) parts.Add("added from a file");
            parts.RemoveAll(string.IsNullOrEmpty);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            if (loaded && setUp == false) right.Children.Add(Spaced(Btn("Set up", delegate { OpenPlugin(info.Id, "Connection"); }, true)));
            if (update != null)
            {
                Button upd = null;
                upd = Btn("Update to " + update.Version, async delegate
                {
                    upd.IsEnabled = false;
                    string err = await PluginManager.UpdateAsync(info);
                    if (err != null) MessageBox.Show(this, info.Name + " couldn't be updated: " + err, "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
                }, true);
                right.Children.Add(Spaced(upd));
            }
            if (!loaded && !busy)
            {
                right.Children.Add(Spaced(Btn("Try again", delegate { AddPlugin(info); })));
                right.Children.Add(Spaced(Btn("Remove", delegate { RemovePlugin(info); })));
            }
            right.Children.Add(dot);
            LinkRow(p, info.Glyph, info.Name, string.Join("  ·  ", parts), right, loaded ? (Action)delegate { OpenPlugin(info.Id, null); } : null);
        }

        static Button Spaced(Button b) { b.Margin = new Thickness(0, 0, 8, 0); return b; }

        /// <summary>"Komodo (Docker) module" / "Jellyfin, Now playing modules", or what else it adds.</summary>
        static string Brings(PluginInfo info)
        {
            List<string> mods = PluginManager.ModuleNames(info.Id);
            if (mods.Count == 0) foreach (string t in info.Types) mods.Add(ModuleTypes.Display(t));
            if (mods.Count > 0) return string.Join(", ", mods) + (mods.Count == 1 ? " module" : " modules");
            return info.Adds;
        }

        void RemovePlugin(PluginInfo info)
        {
            string more = info.FromFile ? "\n\nIt was added from a file, so it goes from the list too - add the file again to get it back." : "";
            if (MessageBox.Show(this, "Remove " + info.Name + "?\n\nIts modules show \"add it in Settings\" until you add it again; its settings are kept." + more,
                                "Plugins", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            PluginManager.Remove(info);
        }

        // ---- Get more ----
        void BuildGetMore(StackPanel p)
        {
            var search = new TextBox { Width = 260, Text = _pluginSearch, VerticalContentAlignment = VerticalAlignment.Center };
            var hint = Txt("Search plugins", "vsDim", 12.5, false);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(6, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Visibility = _pluginSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var box = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            box.Children.Add(search);
            box.Children.Add(hint);
            p.Children.Add(box);
            search.TextChanged += delegate
            {
                _pluginSearch = search.Text;
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                FillPluginList();
            };

            _pluginList = new StackPanel();
            NewCard(p);
            Place(p, _pluginList);
            FillPluginList();
            // the release's list (plugins.json) couldn't be read: why
            if (PluginStore.CatalogState.Length > 0 && PluginStore.CatalogState != "Reading…") Place(p, Txt(PluginStore.CatalogState, "vsDim", 12, false));

            Header(p, "From a file");
            var file = Btn("Add from a file…", delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "A vShell plugin (a .dll)", Filter = "vShell plugins (*.dll)|*.dll|All files|*.*" };
                if (dlg.ShowDialog(this) != true) return;
                string why;
                PluginInfo info = PluginStore.Inspect(dlg.FileName, out why);
                if (info == null) { MessageBox.Show(this, why, "Plugins", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                string who = info.Author.Length > 0 ? " by " + info.Author : "";
                string outside = info.Outside.Count == 0 ? "" :
                    "\n\nIt uses parts of vShell that aren't in the plugin API (" + string.Join(", ", info.Outside.GetRange(0, Math.Min(4, info.Outside.Count))) +
                    (info.Outside.Count > 4 ? " …" : "") + "), so it may need a new build for a later vShell.";
                if (MessageBox.Show(this, info.Name + who + "\n" + info.Description + outside + "\n\nA plugin runs inside vShell with the same access to your PC as vShell itself. " +
                                    "Only add plugins from someone you trust.\n\nAdd it?", "Plugins", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                AddPluginFile(dlg.FileName);
            });
            Place(p, Row("A plugin's DLL you have (someone else's, or a build that isn't on the update site yet)", file, false,
                         "It must be built for plugin API " + PluginApi.Version + " (.NET 10, vShell 0.38.135 or later, up to this one).It's copied into vShell's settings " +
                         "folder and shows under Added as \"added from a file\". It keeps working after updates while the plugin API stays the same."));
        }

        static int GroupOrder(string g)
        {
            int i = Array.IndexOf(PluginGroups, g);
            return i >= 0 ? i : g == "Other" ? 1000 : 100;
        }

        /// <summary>Get more's list again (search typed): the plugins not added, by group.</summary>
        void FillPluginList()
        {
            if (_pluginList == null) return;
            _pluginList.Children.Clear();
            List<PluginInfo> all;
            lock (PluginStore.Catalog) all = new List<PluginInfo>(PluginStore.Catalog);
            string q = (_pluginSearch ?? "").Trim();
            all = all.FindAll(delegate (PluginInfo i)
            {
                if (PluginManager.IsAdded(i.Id)) return false;
                if (q.Length == 0) return true;
                foreach (string w in new[] { i.Name, i.Description, i.Group, i.Adds, i.Author, i.Id })
                    if (!string.IsNullOrEmpty(w) && w.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            });
            all.Sort(delegate (PluginInfo a, PluginInfo b)
            {
                int d = GroupOrder(a.Group).CompareTo(GroupOrder(b.Group));
                if (d == 0) d = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
                return d != 0 ? d : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (all.Count == 0)
            {
                _pluginList.Children.Add(Txt(q.Length > 0 ? "No plugin matches \"" + q + "\"." : "Every plugin is added.", "vsDim", 12.5, false));
                return;
            }
            string group = null;
            foreach (PluginInfo info in all)
            {
                if (info.Group != group)
                {
                    group = info.Group;
                    var gh = Txt(group, "vsDim", 12, true);
                    gh.Margin = new Thickness(0, _pluginList.Children.Count == 0 ? 0 : 10, 0, 4);
                    _pluginList.Children.Add(gh);
                }
                _pluginList.Children.Add(PluginRow(info));
            }
        }

        /// <summary>A plugin to get: what it is and brings, and Add.</summary>
        FrameworkElement PluginRow(PluginInfo info)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string state = PluginManager.StateOf(info.Id);
            if (state != null)
            {
                var st = Txt(state, "vsDim", 12.5, false);
                st.MaxWidth = 260;
                st.Margin = new Thickness(0, 0, 10, 0);
                right.Children.Add(st);
            }
            if (info.InRelease) right.Children.Add(Btn("Add", delegate { AddPlugin(info); }, true));

            var name = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Glyph(info.Glyph, 14);
            Ref(g, TextBlock.ForegroundProperty, "vsFg");
            g.Margin = new Thickness(0, 0, 8, 0);
            name.Children.Add(g);
            name.Children.Add(Txt(info.Name, "vsFg", 13, true));
            string tag = (info.Author.Length > 0 ? "by " + info.Author : "");
            if (tag.Length > 0)
            {
                var by = Txt("   " + tag, "vsDim", 11.5, false);
                by.VerticalAlignment = VerticalAlignment.Center;
                name.Children.Add(by);
            }
            var left = new StackPanel();
            left.Children.Add(name);
            var desc = Txt(info.Description, "vsDim", 12, false);
            desc.Margin = new Thickness(22, 2, 12, 0);
            left.Children.Add(desc);
            string brings = Brings(info);
            string adds = brings == info.Adds || info.Adds.Length == 0 ? brings : brings + "; " + info.Adds;
            if (adds.Length > 0)
            {
                var a = Txt("Adds " + adds + ".", "vsDim", 11.5, false);
                a.Opacity = 0.85;
                a.Margin = new Thickness(22, 2, 12, 0);
                left.Children.Add(a);
            }
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(left);
            return row;
        }

        async void RefreshPluginCatalog()
        {
            bool changed = await PluginStore.RefreshCatalogAsync();
            if (changed) RebuildIntegrations();
        }

        async void AddPlugin(PluginInfo info)
        {
            string err = await PluginManager.AddAsync(info);
            if (err != null) MessageBox.Show(this, info.Name + " couldn't be added: " + err, "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
            else PluginManager.OfferRestart(this, info);
        }

        async void AddPluginFile(string file)
        {
            PluginInfo info;
            string err = await PluginManager.AddFileAsync(file, out info);
            if (err != null) MessageBox.Show(this, err, "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
            else PluginManager.OfferRestart(this, info);
        }

        // =================================================================
        // A plugin's own page: Connection / Notifications / Modules
        // =================================================================
        /// <summary>The page a plugin's page was opened from (Plugins or Notifications) - ‹ Back goes there.</summary>
        string _pluginFrom = PgHa;

        /// <summary>
        /// Show a plugin's page; tab = Connection / Notifications / Modules (null = the one picked last for it, else its first);
        /// from = the page ‹ Back returns to.
        /// </summary>
        void OpenPlugin(string id, string tab, string from = PgHa)
        {
            if (!string.Equals(id, _pluginId, StringComparison.OrdinalIgnoreCase)) _pluginTab = null;
            _pluginId = id;
            _pluginFrom = from;
            if (tab != null) _pluginTab = tab;
            Navigate(PgPlugin);
        }

        /// <summary>"Plugins" or "Notifications" - where the plugin's page was opened from, for its breadcrumb.</summary>
        string PluginFromName { get { return _pluginFrom == PgNotify ? "Notifications" : "Plugins"; } }

        void BuildPluginPage()
        {
            StackPanel p = Page(PgPlugin);
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            PluginInfo info = _pluginId != null ? PluginStore.Find(_pluginId) : null;
            Button back = Btn("‹  Back", delegate { Navigate(_pluginFrom); });
            if (info == null || !PluginManager.IsLoaded(info.Id))
            {
                PageHeader(p, PluginFromName, "This plugin isn't loaded.", back);
                return;
            }
            bool old = _loading;
            _loading = true;
            try { BuildPluginPageCore(p, info, back); }
            finally { _loading = old; }
        }

        void BuildPluginPageCore(StackPanel p, PluginInfo info, Button back)
        {
            string blocked = null, updatedTo = PluginStore.UpdatedTo(info);
            PluginBuild update = updatedTo == null ? PluginStore.UpdateFor(info, out blocked) : null;
            var actions = new List<UIElement>();
            if (update != null)
            {
                Button upd = null;
                upd = Btn("Update to " + update.Version, async delegate
                {
                    upd.IsEnabled = false;
                    string err = await PluginManager.UpdateAsync(info);
                    if (err != null) MessageBox.Show(this, info.Name + " couldn't be updated: " + err, "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
                }, true);
                actions.Add(upd);
            }
            actions.Add(Btn("Remove", delegate { RemovePlugin(info); }));
            actions.Add(back);
            PageHeader(p, info.Name, PluginFromName + "  ›  " + info.Name, actions.ToArray());

            // what it is, and its state
            bool? setUp = PluginManager.IsSetUp(info.Id);
            var parts = new List<string>();
            KeyValuePair<string, string>? have = PluginStore.InstalledBuild(info);
            if (have != null && have.Value.Key.Length > 0) parts.Add(have.Value.Key);
            if (info.Author.Length > 0) parts.Add("by " + info.Author);
            if (info.FromFile) parts.Add("added from a file");
            bool attention = true;
            if (updatedTo != null) parts.Add("updated to " + updatedTo + " - restart vShell to use it");
            else if (PluginManager.NeedsRestart(info)) parts.Add("restart vShell to use the new build");
            else if (setUp == false) parts.Add("not set up yet - fill in Connection");
            else attention = false;
            if (blocked != null) parts.Add(blocked);
            var about = new StackPanel { Margin = new Thickness(2, -4, 0, 16) };
            about.Children.Add(Txt(info.Description, "vsDim", 12.5, false));
            var st = Txt(string.Join("  ·  ", parts), attention ? "vsAccent" : "vsDim", 12, false);
            st.Margin = new Thickness(0, 4, 0, 0);
            about.Children.Add(st);
            p.Children.Add(about);

            List<Action<SettingsPage>> conn = PluginManager.IntegrationsOf(info.Id), notes = PluginManager.NotifySectionsOf(info.Id);
            List<PluginModule> mods = PluginManager.ModulesOf(info.Id);
            var keys = new List<string>();
            var names = new List<string>();
            // (a plugin that connects to something says when it's set up; one that doesn't - Shelf - just has settings)
            if (conn.Count > 0) { keys.Add("Connection"); names.Add(setUp == null ? "Settings" : "Connection"); }
            if (notes.Count > 0) { keys.Add("Notifications"); names.Add("Notifications"); }
            if (mods.Count > 0) { keys.Add("Modules"); names.Add(mods.Count == 1 ? "Module" : "Modules (" + mods.Count + ")"); }
            if (keys.Count == 0) { Place(p, Txt("This plugin has no settings of its own.", "vsDim", 12.5, false)); return; }
            string tab = _pluginTab != null && keys.Contains(_pluginTab) ? _pluginTab : keys[0];
            Tabs(p, keys.ToArray(), names.ToArray(), tab, delegate (string k) { _pluginTab = k; BuildPluginPage(); _content.ScrollToTop(); });
            if (tab == "Connection") RunSections(p, conn, info);
            else if (tab == "Notifications") RunSections(p, notes, info);
            else BuildPluginModules(p, mods);
        }

        void RunSections(StackPanel p, List<Action<SettingsPage>> sections, PluginInfo info)
        {
            foreach (Action<SettingsPage> s in sections)
            {
                try { s(new SettingsPage(this, p, null)); }
                catch (Exception ex) { Log.Write("Plugins: " + info.Name + " settings: " + ex.Message); }
                EndSection(p);
            }
        }

        /// <summary>The Modules tab: each module the plugin brings - its picture, where it's used (Edit › opens it there), and adding one.</summary>
        void BuildPluginModules(StackPanel p, List<PluginModule> mods)
        {
            Theme t = Theme.From(S, 12, 13);
            List<MonitorInfo> mons = MonitorHelper.GetAll();
            foreach (PluginModule m0 in mods)
            {
                PluginModule m = m0;
                Header(p, m.Display);
                FrameworkElement pic;
                try { pic = m.OnBar ? ModulePics.Bar(m.Type, t) : ModulePics.Widget(m.Type, t); }
                catch (Exception ex) { Log.Write("Plugins: " + m.Type + " picture: " + ex.Message); pic = Txt(m.Display, "vsFg", 12, false); }
                var fit = new Viewbox { Child = pic, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = m.OnBar ? 30 : 90, MaxWidth = 260 };
                var strip = new Border { CornerRadius = new CornerRadius(6), Background = t.MakeBg(-1), Padding = new Thickness(12, 8, 12, 8), Child = fit };
                Place(p, Row(m.Blurb.Length > 0 ? m.Blurb : m.Display, strip));
                string need = null;
                try { need = m.Needs != null ? m.Needs(S) : null; } catch { }
                if (need != null) Place(p, Txt(need, "vsAccent", 12.5, false));

                // where it is
                var uses = new List<KeyValuePair<string, Action>>();
                for (int i = 0; i < S.Bars.Count; i++)
                {
                    BarPlacement bar = S.Bars[i];
                    foreach (ModuleConfig c in bar.Modules)
                        if (c.Type == m.Type) { ModuleConfig cfg = c; uses.Add(new KeyValuePair<string, Action>("On the bar " + BarShortName(i, bar, mons), delegate { EditModule(bar, null, cfg); })); }
                }
                foreach (WidgetSettings w0 in S.Widgets)
                {
                    WidgetSettings w = w0;
                    foreach (ModuleConfig c in w.Modules)
                        if (c.Type == m.Type) { ModuleConfig cfg = c; uses.Add(new KeyValuePair<string, Action>("In the widget " + w.Name, delegate { EditModule(null, w, cfg); })); }
                }
                if (uses.Count == 0) Place(p, Txt("Not on a bar or widget yet.", "vsDim", 12.5, false));
                foreach (KeyValuePair<string, Action> u in uses) Place(p, Row(u.Key, Btn("Edit ›", u.Value)));

                // adding one
                var places = new List<string>();
                var adds = new List<Action>();
                if (m.OnBar)
                    for (int i = 0; i < S.Bars.Count; i++)
                    {
                        BarPlacement bar = S.Bars[i];
                        places.Add("Bar  ·  " + BarShortName(i, bar, mons));
                        adds.Add(delegate { AddModuleTo(bar, null, m.Type); });
                    }
                if (m.InWidget)
                    foreach (WidgetSettings w0 in S.Widgets)
                    {
                        WidgetSettings w = w0;
                        places.Add("Widget  ·  " + w.Name);
                        adds.Add(delegate { AddModuleTo(null, w, m.Type); });
                    }
                if (places.Count > 0)
                {
                    var cb = new ComboBox { MinWidth = 220 };
                    foreach (string s in places) cb.Items.Add(s);
                    cb.SelectedIndex = 0;
                    var go = Btn("Add", delegate { if (cb.SelectedIndex >= 0) adds[cb.SelectedIndex](); }, true);
                    go.Margin = new Thickness(8, 0, 0, 0);
                    var line = new StackPanel { Orientation = Orientation.Horizontal };
                    line.Children.Add(cb);
                    line.Children.Add(go);
                    Place(p, Row("Add one to", line));
                }
            }
        }

        /// <summary>Open a module's settings in its bar's / widget's module editor (cfg = null: just the editor).</summary>
        void EditModule(BarPlacement bar, WidgetSettings widget, ModuleConfig cfg)
        {
            if (widget != null)
            {
                _selWidget = widget;
                Navigate(PgWidgetModules);
                if (_widPane != null && cfg != null) _widPane.Refresh(cfg);
            }
            else
            {
                _selBar = bar;
                Navigate(PgBarModules);
                if (_barPane != null && cfg != null) _barPane.Refresh(cfg);
            }
        }

        /// <summary>Add a module of this type at the end of a bar / widget and open its settings there.</summary>
        void AddModuleTo(BarPlacement bar, WidgetSettings widget, string type)
        {
            EditModule(bar, widget, null);
            ModulePane pane = widget != null ? _widPane : _barPane;
            if (pane != null) pane.AddModule(type);
        }
    }
}

