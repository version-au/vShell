using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The settings of single module types (taskbar, weather, run box, notes …) and the inspector's shared rows (graphs, warnings, titles, services).</summary>
    public partial class SettingsWindow
    {
        /// <summary>Weather module: where the data comes from (Open-Meteo place search, or a Home Assistant weather entity) and what to show.</summary>
        /// <summary>
        /// "Icon animations" for the Launcher / Taskbar on a bar (any layout): only with icons only - otherwise shown greyed out with the reason.
        /// </summary>
        void DockAnimations(ModulePane pane, StackPanel p, ModuleConfig cfg, string iconsOnlyHint)
        {
            Section(p, "Icon animations");
            bool icons = DockFx.IconsOnly(cfg);
            StackPanel card;
            int first = _cardOf.TryGetValue(p, out card) ? card.Children.Count : 0;
            // Lift and Magnify are one or the other: ticking one unticks the other
            Action reload = delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); };
            Check(p, "Lift - the icon under the mouse rises a little and grows slightly (not with Magnify)", delegate { return cfg.DockLift; }, delegate (bool v)
            {
                cfg.DockLift = v;
                if (v && cfg.DockMagnify) { cfg.DockMagnify = false; reload(); }
            });
            Check(p, "Magnify - the icon under the mouse grows, its neighbours a little less, like the macOS dock (not with Lift)", delegate { return cfg.DockMagnify; }, delegate (bool v)
            {
                cfg.DockMagnify = v;
                if (v && cfg.DockLift) { cfg.DockLift = false; }
                reload();
            });
            if (cfg.DockMagnify)
                SliderRow(p, "Magnify size (%)", 110, 250, delegate { return cfg.DockMagnifyPercent; }, delegate (double v) { cfg.DockMagnifyPercent = (int)v; });
            Check(p, "Glow - a soft Main Accent glow around the icon under the mouse", delegate { return cfg.DockGlow; }, delegate (bool v) { cfg.DockGlow = v; reload(); });
            if (cfg.DockGlow)
            {
                SliderRow(p, "Glow strength (%)", 10, 100, delegate { return cfg.DockGlowStrength; }, delegate (double v) { cfg.DockGlowStrength = (int)v; });
                SliderRow(p, "Glow size (% of the icon)", 120, 300, delegate { return cfg.DockGlowSize; }, delegate (double v) { cfg.DockGlowSize = (int)v; });
            }
            Check(p, "Bounce - the icon bounces when you start an app from it", delegate { return cfg.DockBounce; }, delegate (bool v) { cfg.DockBounce = v; });
            if (!icons)
            {
                if (_cardOf.TryGetValue(p, out card))
                    for (int i = first; i < card.Children.Count; i++) card.Children[i].IsEnabled = false;
                Note(p, iconsOnlyHint);
            }
            else if (pane.IsWidget) Note(p, "In a widget the icons grow about their middle, inside the widget - give it a little padding (its widget style) so the " +
                                            "grown ones don't touch its edge.");
            else Note(p, "Works on every bar layout. The bar gets transparent room above it (and beside it for Magnify, where there's room on the monitor) " +
                         "for the grown icons; clicks go through it to the windows behind.");
        }

        /// <summary>
        /// The module shows text, so its Text size does something: not a Spacer or the Tray, a Launcher showing no names, or a Taskbar of icons
        /// only (Icon only, and always combined - uncombined buttons show their window's title).
        /// </summary>
        static bool ShowsText(ModuleConfig cfg)
        {
            switch (cfg.Type)
            {
                case ModuleTypes.Spacer:
                case ModuleTypes.Tray: return false;
                case ModuleTypes.Launcher: return cfg.ShowLabels || cfg.ShowGroupLabels;
                case ModuleTypes.Taskbar: return !(cfg.TaskLabel == "Icon" && cfg.TaskGrouping == "Always");
                default: return true;
            }
        }

        void TaskbarProps(ModulePane pane, StackPanel p, ModuleConfig cfg)
        {
            var groupNames = new[] { "Always", "When the bar is full", "Never" };
            ComboRow(p, "Combine buttons of the same app", groupNames,
                     delegate { int i = Array.IndexOf(ModuleTypes.TaskGroupings, cfg.TaskGrouping); return groupNames[Math.Max(0, i)]; },
                     delegate (string v)
                     {
                         cfg.TaskGrouping = ModuleTypes.TaskGroupings[Math.Max(0, Array.IndexOf(groupNames, v))];
                         Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // Text size shows / hides
                     });
            var labelNames = new[] { "Icon only", "Icon and app name", "Icon and window title" };
            ComboRow(p, "Combined buttons show", labelNames,
                     delegate { int i = Array.IndexOf(ModuleTypes.TaskLabels, cfg.TaskLabel); return labelNames[Math.Max(0, i)]; },
                     delegate (string v)
                     {
                         cfg.TaskLabel = ModuleTypes.TaskLabels[Math.Max(0, Array.IndexOf(labelNames, v))];
                         Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // dock animations on / greyed, Text size shows / hides
                     });
            Check(p, "Set the icon size myself", delegate { return cfg.TaskIconSize > 0; }, delegate (bool v)
            {
                cfg.TaskIconSize = v ? 24 : 0;
                Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the slider shows / hides
            });
            if (cfg.TaskIconSize > 0)
                SliderRow(p, "Icon size", 16, 48, delegate { return cfg.TaskIconSize; }, delegate (double v) { cfg.TaskIconSize = (int)v; });
            Note(p, "For pinned apps and icon-only buttons (buttons with text keep small icons). Off = the icons fit the bar's height. " +
                    "A size too big for the bar shrinks to fit.");
            Check(p, "Highlight box behind the button under the mouse (the open app's button keeps its own background)", delegate { return cfg.HoverBox; }, delegate (bool v) { cfg.HoverBox = v; });
            SliderRow(p, "Button width (buttons with text)", 80, 320, delegate { return cfg.MaxButtonWidth; }, delegate (double v) { cfg.MaxButtonWidth = (int)v; });
            Check(p, "Only windows on this bar's monitor", delegate { return cfg.OnlyCurrentMonitor; }, delegate (bool v) { cfg.OnlyCurrentMonitor = v; });
            ComboRow(p, "An app that wants attention", ModuleTypes.TaskAttentions, delegate { return cfg.TaskAttention; }, delegate (string v) { cfg.TaskAttention = v; });
            Note(p, "Pulse = its button's background slowly pulses in the Secondary Accent until you look at it. Steady = it just stays that colour.");

            DockAnimations(pane, p, cfg, "Set \"Combined buttons show\" to Icon only to use these.");

            Section(p, "Active window line animation");
            ComboRow(p, "Moving to another window", ModuleTypes.TaskAnims, delegate { return cfg.TaskAnim; }, delegate (string v) { cfg.TaskAnim = v; });
            Note(p, "Off = the line jumps to the new button. Slide = it glides there. Stretch = the front end runs ahead and the back end catches up. " +
                    "Fade = it fades out and fades in under the new button. Shrink and grow = it shrinks away and grows out of the middle of the new button. " +
                    "Hop = it arcs up over the buttons in between. Liquid = it stretches and gets thinner, thickening again as it lands. " +
                    "With no window active (you clicked the desktop) it shrinks away.");
            SliderRow(p, "Speed (milliseconds - lower is faster)", 60, 1000, delegate { return cfg.TaskAnimMs; }, delegate (double v) { cfg.TaskAnimMs = (int)v; });
            ComboRow(p, "Bounce", ModuleTypes.TaskAnimBounces, delegate { return cfg.TaskAnimBounce; }, delegate (string v) { cfg.TaskAnimBounce = v; });
            Note(p, "None = a smooth stop. Soft = goes a little past and settles back. Springy = a couple of small wobbles.");
            Check(p, "The active button's background moves along with the line", delegate { return cfg.TaskAnimHighlight; }, delegate (bool v) { cfg.TaskAnimHighlight = v; });

            Section(p, "Previews");
            Check(p, "Live window previews when you hover a button", delegate { return cfg.TaskPreviews; }, delegate (bool v) { cfg.TaskPreviews = v; });
            SliderRow(p, "Preview size", 120, 480, delegate { return cfg.TaskPreviewSize; }, delegate (double v) { cfg.TaskPreviewSize = (int)v; });
            Note(p, "Click an app with several windows to pick one (with previews, or as a list when previews are off). Shift+click or middle-click an app opens a new window of it; " +
                    "middle-click a single window's own button (\"Never\" combine) to close it. In the previews, ✕ or middle-click closes a window.");

            Section(p, "Pinned apps");
            Check(p, "Show pinned apps", delegate { return cfg.TaskShowPins; }, delegate (bool v) { cfg.TaskShowPins = v; });
            Check(p, "Same pins on every taskbar (synced)", delegate { return cfg.TaskSyncPins; }, delegate (bool v)
            {
                cfg.TaskSyncPins = v;
                // the first taskbar to sync brings its pins along; this taskbar's own pins stay as they were, for when it's turned off
                if (v && S.SharedPins.Count == 0) foreach (PinnedApp own in cfg.Pins) S.SharedPins.Add(own.Copy());
                Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
            });
            Note(p, cfg.TaskSyncPins
                ? "Synced: the pins below are shared by every taskbar with this on - pin, unpin or reorder on one and the others follow. Turn it off and this taskbar goes back to the pins it had before."
                : "On = this taskbar uses the pins shared by every taskbar with it on (the first one to turn it on brings its pins). This taskbar's own pins are kept and come back when it's turned off.");
            List<PinnedApp> pins = cfg.TaskSyncPins ? S.SharedPins : cfg.Pins;
            if (pins.Count == 0)
                Note(p, "Nothing pinned yet. Right-click an app on the taskbar > Pin to taskbar, or add a program or shortcut below.");
            for (int i = 0; i < pins.Count; i++)
            {
                PinnedApp pin = pins[i];
                int at = i;
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                var up = Btn("▲", delegate { pins.RemoveAt(at); pins.Insert(at - 1, pin); _mgr.SettingsChanged(); Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); });
                var down = Btn("▼", delegate { pins.RemoveAt(at); pins.Insert(at + 1, pin); _mgr.SettingsChanged(); Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); });
                var rem = Btn("Remove", delegate { pins.Remove(pin); _mgr.SettingsChanged(); Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); });
                up.IsEnabled = at > 0;
                down.IsEnabled = at < pins.Count - 1;
                foreach (var b in new[] { up, down, rem }) { b.Margin = new Thickness(6, 0, 0, 0); btns.Children.Add(b); }
                DockPanel.SetDock(btns, Dock.Right);
                row.Children.Add(btns);
                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(Txt(pin.Name.Length > 0 ? pin.Name : TaskbarModule.ProgramName(pin.Path), "vsFg", 13, false));
                var path = Txt(pin.Path, "vsDim", 11, false);
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                name.Children.Add(path);
                row.Children.Add(name);
                Place(p, row);
            }
            ListButton(p, "Add programs or shortcuts…", delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pin to the taskbar (several at once: Ctrl or Shift)",
                    Filter = "Programs and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
                    DereferenceLinks = false,
                    Multiselect = true
                };
                if (dlg.ShowDialog(this) != true) return;
                foreach (string f in dlg.FileNames)
                {
                    string target = f;
                    if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string t = ShortcutTarget(f);
                        if (!string.IsNullOrEmpty(t)) target = t;
                    }
                    pins.Add(new PinnedApp
                    {
                        Name = TaskbarModule.ProgramName(target),
                        Path = f,
                        AppId = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "exe:" + target.ToLowerInvariant() : ""
                    });
                }
                _mgr.SettingsChanged();
                Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
            });
            ListButton(p, "Import pins from the Windows taskbar…", delegate
            {
                bool replace;
                List<WindowsPin> chosen = ImportPinsWindow.Ask(this, delegate (WindowsPin wp) { return pins.Exists(delegate (PinnedApp x) { return wp.Matches(x); }); }, "taskbar", out replace);
                if (chosen == null) return;
                if (replace) pins.Clear();
                foreach (WindowsPin wp in chosen)
                    if (!pins.Exists(delegate (PinnedApp x) { return wp.Matches(x); })) pins.Add(wp.ToPin());
                _mgr.SettingsChanged();
                Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
            });
            Note(p, "These pins belong to this taskbar only (another bar's taskbar has its own). A pin turns into the app's button while it runs (its windows are combined on it). " +
                    "Drag a pin along the bar to move it, drop programs or shortcuts from File Explorer onto the taskbar to pin them, right-click a pin to unpin it.");
        }

        /// <summary>The program a .lnk points at ("" when it can't be read).</summary>
        static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return "";
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                string target = sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sc); System.Runtime.InteropServices.Marshal.ReleaseComObject(sh); } catch { }
                return target ?? "";
            }
            catch { return ""; }
        }

        /// <summary>The plugins' weather sources' rows in the Weather module's settings (by the source's Id).</summary>
        internal static readonly Dictionary<string, Action<SettingsPage, ModuleConfig>> WeatherRows = new Dictionary<string, Action<SettingsPage, ModuleConfig>>();

        void WeatherProps(ModulePane pane, StackPanel p, ModuleConfig cfg)
        {
            // Open-Meteo, or a plugin's source (Home Assistant's weather entities) with its own settings
            IWeatherSource own = WeatherService.SourceOf(cfg);
            var names = new List<string> { "Open-Meteo (free)" };
            IWeatherSource[] sources;
            lock (WeatherService.Sources) sources = WeatherService.Sources.ToArray();
            foreach (IWeatherSource src in sources) names.Add(src.Name);
            string missing = WeatherService.SourceMissing(cfg) ? cfg.WeatherSource + " (add its plugin in Settings > Plugins)" : null;
            if (missing != null) names.Add(missing);
            ComboRow(p, "Weather from", names.ToArray(),
                     delegate { return own != null ? own.Name : missing ?? "Open-Meteo (free)"; },
                     delegate (string v)
                     {
                         IWeatherSource pick = Array.Find(sources, delegate (IWeatherSource x) { return x.Name == v; });
                         if (pick != null) cfg.WeatherSource = pick.Id;
                         else if (v != missing) cfg.WeatherSource = "OpenMeteo";
                         Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
                     });

            if (cfg.WeatherSource == "OpenMeteo")
            {
                // ---- place search ----
                var box = new TextBox { Width = 220, Text = cfg.WeatherPlace };
                var find = new Button { Content = "Find", Margin = new Thickness(6, 0, 0, 0) };
                var results = new ComboBox { MinWidth = 300, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
                var status = Txt(cfg.WeatherPlace.Length > 0
                                     ? "Now: " + cfg.WeatherPlace + string.Format(CultureInfo.InvariantCulture, "  ({0:0.00}, {1:0.00})", cfg.WeatherLat, cfg.WeatherLon)
                                     : "Type a town or suburb and press Find.", "vsDim", 12, false);
                status.Margin = new Thickness(0, 6, 0, 0);
                var found = new List<GeoPlace>();
                Action<GeoPlace> pick = delegate (GeoPlace g)
                {
                    cfg.WeatherPlace = g.Region.Length > 0 && g.Region != g.Name ? g.Name + ", " + g.Region : g.Name;
                    cfg.WeatherLat = g.Lat;
                    cfg.WeatherLon = g.Lon;
                    box.Text = cfg.WeatherPlace;
                    status.Text = "Now: " + g.Display + string.Format(CultureInfo.InvariantCulture, "  ({0:0.00}, {1:0.00})", g.Lat, g.Lon);
                    _mgr.SettingsChanged();
                };
                Action search = async delegate
                {
                    string q = box.Text.Trim();
                    if (q.Length < 2) { status.Text = "Type at least two letters."; return; }
                    status.Text = "Searching…";
                    find.IsEnabled = false;
                    try
                    {
                        List<GeoPlace> list = await WeatherService.SearchPlaces(q);
                        found.Clear();
                        found.AddRange(list);
                        results.Items.Clear();
                        foreach (GeoPlace g in list) results.Items.Add(g.Display + string.Format(CultureInfo.InvariantCulture, "   ({0:0.00}, {1:0.00})", g.Lat, g.Lon));
                        if (list.Count == 0) { status.Text = "No place called \"" + q + "\" was found."; results.Visibility = Visibility.Collapsed; }
                        else if (list.Count == 1) { results.Visibility = Visibility.Collapsed; pick(list[0]); }
                        else
                        {
                            results.Visibility = Visibility.Visible;
                            status.Text = list.Count + " places found - pick yours:";
                            results.IsDropDownOpen = true;
                        }
                    }
                    catch (Exception ex) { status.Text = "Search failed: " + ex.Message; }
                    finally { find.IsEnabled = true; }
                };
                find.Click += delegate { search(); };
                box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; search(); } };
                results.SelectionChanged += delegate
                {
                    int i = results.SelectedIndex;
                    if (i >= 0 && i < found.Count) pick(found[i]);
                };
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(box);
                line.Children.Add(find);
                var block = new StackPanel();
                block.Children.Add(line);
                block.Children.Add(status);
                block.Children.Add(results);
                Place(p, Row("Location", block, true));

                ComboRow(p, "Units", new[] { "°C", "°F" }, delegate { return cfg.WeatherFahrenheit ? "°F" : "°C"; },
                         delegate (string v) { cfg.WeatherFahrenheit = v == "°F"; });
                SliderRow(p, "Update every (minutes)", 5, 60, delegate { return cfg.WeatherMinutes; }, delegate (double v) { cfg.WeatherMinutes = (int)v; });
                Note(p, "Weather data by Open-Meteo.com - free, no account or key needed.");
            }
            else if (own != null)
            {
                Action<SettingsPage, ModuleConfig> rows;
                if (WeatherRows.TryGetValue(own.Id, out rows)) rows(new SettingsPage(this, p, pane), cfg);
            }

            Section(p, "Show");
            Check(p, "The condition - e.g. Partly cloudy", delegate { return cfg.WeatherShowCondition; }, delegate (bool v) { cfg.WeatherShowCondition = v; });
            Check(p, "Today's high and low", delegate { return cfg.WeatherShowHighLow; }, delegate (bool v) { cfg.WeatherShowHighLow = v; });
            if (pane.IsWidget)
            {
                Check(p, "The forecast in the widget - under the current weather", delegate { return cfg.WeatherShowForecast; }, delegate (bool v)
                {
                    cfg.WeatherShowForecast = v;
                    Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
                });
                if (cfg.WeatherShowForecast)
                {
                    SliderRow(p, "Days", 2, 7, delegate { return cfg.WeatherForecastDays; }, delegate (double v) { cfg.WeatherForecastDays = (int)v; });
                    ComboRow(p, "Forecast layout", new[] { "List", "Row" }, delegate { return cfg.TileLayout == "Row" ? "Row" : "List"; }, delegate (string v) { cfg.TileLayout = v; });
                    SliderRow(p, "Spacing - between the days (Row) or the lines (List)", 0, 60, delegate { return cfg.WeatherForecastSpacing; }, delegate (double v) { cfg.WeatherForecastSpacing = (int)v; });
                }
            }
            Note(p, "Hover for details; click the module for the 7-day forecast.");
        }

        // ---- copy / paste of bar and widget settings (kept in memory while the app runs) ----
        static BarPlacement _copiedBar;
        static WidgetSettings _copiedWidget;

        static T CloneJson<T>(T o)
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(o));
        }

        static List<ModuleConfig> CloneModules(List<ModuleConfig> src)
        {
            var list = new List<ModuleConfig>();
            if (src == null) return list;
            foreach (var m in src)
            {
                var c = CloneJson(m);
                c.Id = Guid.NewGuid().ToString("N");     // own id = own graph history
                list.Add(c);
            }
            return list;
        }

        /// <summary>Everything except what identifies the bar and where it sits (id, monitor, edge, position).</summary>
        static void PasteBar(BarPlacement from, BarPlacement to, bool withModules)
        {
            to.BarHeight = from.BarHeight; to.FullWidth = from.FullWidth;
            to.FontSize = from.FontSize; to.CornerRadius = from.CornerRadius; to.RoundAllCorners = from.RoundAllCorners;
            to.ShowBorder = from.ShowBorder; to.BorderThickness = from.BorderThickness; to.BorderOpacity = from.BorderOpacity;
            to.BorderColor = from.BorderColor;
            to.GlassEdge = from.GlassEdge;
            to.UseThemeOpacity = from.UseThemeOpacity; to.BackgroundOpacity = from.BackgroundOpacity;
            to.EdgeMargin = from.EdgeMargin; to.ReserveSpace = from.ReserveSpace;
            to.Zones = from.Zones; to.SeparatorStyle = from.SeparatorStyle;
            if (withModules) to.Modules = CloneModules(from.Modules);
        }

        /// <summary>Everything except the widget's id, name, monitor, position and whether it is shown.</summary>
        static void PasteWidget(WidgetSettings from, WidgetSettings to, bool withModules)
        {
            to.Direction = from.Direction; to.ModuleHeight = from.ModuleHeight; to.Width = from.Width; to.Height = from.Height;
            to.Padding = from.Padding; to.Spacing = from.Spacing;
            to.FontSize = from.FontSize; to.CornerRadius = from.CornerRadius;
            to.Opacity = from.Opacity; to.UseThemeOpacity = from.UseThemeOpacity;
            to.ShowBorder = from.ShowBorder; to.BorderThickness = from.BorderThickness; to.BorderOpacity = from.BorderOpacity;
            to.BorderColor = from.BorderColor;
            to.GlassEdge = from.GlassEdge;
            to.ShowShadow = from.ShowShadow; to.AlwaysOnTop = from.AlwaysOnTop; to.Locked = from.Locked;
            to.GridColumns = from.GridColumns; to.CardPerModule = from.CardPerModule; to.ShowTabs = from.ShowTabs;
            to.ShowHeader = from.ShowHeader; to.Collapsible = from.Collapsible; to.HeaderTextColor = from.HeaderTextColor; to.AccentEdge = from.AccentEdge; to.AccentColor = from.AccentColor;
            to.Density = from.Density; to.KeepSize = from.KeepSize;
            to.AutoHideAnim = from.AutoHideAnim; to.AutoHideMs = from.AutoHideMs; to.AutoHideBounce = from.AutoHideBounce; to.AutoHideDelay = from.AutoHideDelay; to.AutoHideGlow = from.AutoHideGlow;
            if (withModules) to.Modules = CloneModules(from.Modules);
        }

        /// <summary>The run box's settings (width and keys are this box's; the rest is shared by every run box).</summary>
        void RunBoxProps(StackPanel p, ModuleConfig cfg, ModulePane pane)
        {
            SliderRow(p, "Width", 80, 600, delegate { return cfg.RunWidth; }, delegate (double v) { cfg.RunWidth = (int)v; });
            ComboRow(p, "Open it from anywhere with", ModuleTypes.RunHotkeys, delegate { return cfg.RunHotkey; }, delegate (string v) { cfg.RunHotkey = v; });
            Note(p, "Click the box and type in it (the keys open a box in the middle of the main monitor): an app name, a path or folder (C:\\Temp, %appdata%), a web address, ms-settings:..., " +
                    "!g dog pictures to search a site, =12*7.5 for a sum (Enter copies it), >ipconfig to run a command in a terminal. Anything else is a " +
                    "web search. Up / Down: earlier entries. Tab: fill in the highlighted suggestion. If two run boxes use the same keys, the first one gets them.");

            Section(p, "Searches (shared by every run box)");
            ComboRow(p, "Plain search (no !bang) with", ModuleTypes.RunEngines, delegate { return S.RunEngine; }, delegate (string v)
            {
                bool was = S.RunEngine == "Custom";
                S.RunEngine = v;
                if (was != (v == "Custom")) Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // show / hide the address box
            });
            if (S.RunEngine == "Custom")
            {
                TextRow(p, "Search address", delegate { return S.RunCustomEngine; }, delegate (string v) { S.RunCustomEngine = v; });
                Note(p, "Your search engine's address with {q} where the search goes, e.g. https://searx.example.com/search?q={q} (SearXNG). " +
                        "Without {q} the search is added to the end.");
            }
            Check(p, "A !bang that is not in the list goes to DuckDuckGo (it knows thousands: !amazon, !imdb, !reddit ...)", delegate { return S.RunUnknownToDdg; }, delegate (bool v) { S.RunUnknownToDdg = v; });
            var box = new TextBox
            {
                Text = RunBang.ToText(S.RunBangs), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 140, MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Margin = new Thickness(0, 4, 0, 4)
            };
            box.LostFocus += delegate
            {
                if (_loading) return;
                S.RunBangs = RunBang.FromText(box.Text);
                box.Text = RunBang.ToText(S.RunBangs);
                _mgr.SettingsChanged();
            };
            Place(p, Txt("!bangs - one per line: keyword | name | address with {q} where the search goes", "vsFg", 12.5, false));
            Place(p, box);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var reset = Btn("Put back the built-in list", delegate
            {
                S.RunBangs = RunBang.Defaults();
                box.Text = RunBang.ToText(S.RunBangs);
                _mgr.SettingsChanged();
            });
            var clear = Btn("Clear the run history (" + S.RunHistory.Count + ")", delegate { S.RunHistory.Clear(); _mgr.SettingsChanged(); });
            clear.Margin = new Thickness(6, 0, 0, 0);
            btns.Children.Add(reset);
            btns.Children.Add(clear);
            Place(p, btns);
            Note(p, "Plugins have their own keywords (Seerr, Komodo, qBittorrent ...) - set them up, and change them, on the Plugins page.");
        }

        /// <summary>The Notes module's settings.</summary>
        void NotesProps(StackPanel p, ModuleConfig cfg)
        {
            Action again = delegate { ModulePane pane = _propsPane; Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); };
            bool vdrop = !NoteFeeds.IsBuiltIn(cfg.NoteStore);      // a plugin's store (vDrop's feed)
            if (vdrop && cfg.NoteKind != "Note") cfg.NoteKind = "Note";      // a feed is notes and links: no checklist / log / Markdown page
            if (vdrop)
            {
                // the same buttons as the other stores, with Note as the only one
                Place(p, Row("Kind", Segmented(new[] { "Note" }, delegate { return "Note"; }, delegate (string v) { })));
                Note(p, "On " + cfg.NoteStore + " the module shows " + cfg.NoteStore + "'s feed, so Note is the only kind.");
            }
            else ComboRow(p, "Kind", ModuleTypes.NoteKinds, delegate { return cfg.NoteKind; }, delegate (string v) { cfg.NoteKind = v; again(); });
            if (!vdrop) switch (cfg.NoteKind)
            {
                case "Checklist": Note(p, "Tick items off, add them in the box at the bottom, double-click one to change it, drag the handle on its left to move it. Right-click an item for a due date (it turns the Secondary Accent colour on the day, and says overdue after it)."); break;
                case "Markdown": Note(p, "Shows Markdown nicely: headings, bold, italic, lists, tick boxes, code, links, quotes. Editing shows the Markdown itself."); break;
                case "Log": Note(p, "Type in the box at the top and press Enter: each entry gets the time, newest first."); break;
                default: Note(p, "Text with light formatting: **bold**, *italic*, ~~crossed out~~, - bullets and - [ ] tick boxes (the toolbar puts them in for you)."); break;
            }

            Section(p, "Stored in");
            // vShell's own stores, and the plugins' (vDrop, with the Home Assistant plugin)
            var stores = new List<string>(ModuleTypes.NoteStores);
            foreach (NoteFeedStore fs in NoteFeeds.All()) stores.Add(fs.Id);
            if (!stores.Contains(cfg.NoteStore)) stores.Add(cfg.NoteStore);
            ComboRow(p, "Stored in", stores.ToArray(), delegate { return cfg.NoteStore; }, delegate (string v)
            {
                // a feed (pictures, previews) wants more room than a note: the first time, a bigger size if it's at the note's default
                bool feed = !NoteFeeds.IsBuiltIn(v);
                if (feed && NoteFeeds.IsBuiltIn(cfg.NoteStore))
                {
                    if (cfg.NoteWidth <= 260) cfg.NoteWidth = 420;
                    if (cfg.NoteHeight <= 200) cfg.NoteHeight = 520;
                }
                if (feed) cfg.NoteKind = "Note";
                if (v == "Folder" && cfg.NoteFolder.Length > 0 && string.IsNullOrEmpty(cfg.NoteFolderFile))
                {
                    List<string> files = NotesModule.FolderNotes(cfg.NoteFolder);
                    if (files.Count > 0) cfg.NoteFolderFile = files[0];
                }
                cfg.NoteStore = v; again();
            });
            if (cfg.NoteStore == "File")
            {
                TextRow(p, "File (.md or .txt)", delegate { return cfg.NoteFile; }, delegate (string v) { cfg.NoteFile = v.Trim().Trim('"'); });
                ListButton(p, "Choose or make a file…", delegate
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Note file - pick one or type a new name", Filter = "Markdown and text (*.md;*.txt)|*.md;*.txt|All files (*.*)|*.*",
                        OverwritePrompt = false, CheckFileExists = false, FileName = string.IsNullOrEmpty(cfg.NoteFile) ? "notes.md" : System.IO.Path.GetFileName(cfg.NoteFile)
                    };
                    if (!string.IsNullOrEmpty(cfg.NoteFile))
                        try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(cfg.NoteFile); } catch { }
                    if (dlg.ShowDialog(this) != true) return;
                    cfg.NoteFile = dlg.FileName;
                    _mgr.SettingsChanged();
                    again();
                });
                Note(p, "Read and written both ways: a change from another program (Notepad, Obsidian, a OneDrive / Dropbox folder) shows " +
                        "straight away, and if the file changes while you are typing here you choose: keep mine, take theirs, or keep both. " +
                        "A new file is made when you first add something. Checklists are saved as \"- [ ] item\", log entries as \"- **date time** text\".");
            }
            else if (cfg.NoteStore == "Folder")
            {
                TextRow(p, "Folder (.md / .txt notes)", delegate { return cfg.NoteFolder; }, delegate (string v) { cfg.NoteFolder = v.Trim().Trim('"'); });
                ListButton(p, "Choose the folder…", delegate
                {
                    var fd = new Microsoft.Win32.OpenFolderDialog { Title = "A folder with .md / .txt notes" };
                    string start = cfg.NoteFolder;
                    if (!string.IsNullOrEmpty(start) && System.IO.Directory.Exists(start)) fd.InitialDirectory = start;
                    if (fd.ShowDialog(this) != true) return;
                    cfg.NoteFolder = fd.FolderName;
                    // no note yet, or one outside the folder: start with the newest note in it
                    if (string.IsNullOrEmpty(cfg.NoteFolderFile) || !NotesModule.InFolder(cfg.NoteFolderFile, cfg.NoteFolder))
                    {
                        List<string> files = NotesModule.FolderNotes(cfg.NoteFolder);
                        cfg.NoteFolderFile = files.Count > 0 ? files[0] : "";
                    }
                    _mgr.SettingsChanged();
                    again();
                });
                Note(p, "The note shows one file from the folder at a time, with its name in a title line - click it for the folder's .md and .txt files (newest first) " +
                        "and pick another. The list is read each time you open it, so new files appear straight away.");
                Note(p, "Read and written both ways: a change from another program (Notepad, Obsidian, a OneDrive / Dropbox folder) shows " +
                        "straight away, and if the file changes while you are typing here you choose: keep mine, take theirs, or keep both. " +
                        "A new file is made when you first add something. Checklists are saved as \"- [ ] item\", log entries as \"- **date time** text\".");
            }
            else if (vdrop)
            {
                NoteFeedStore fs = NoteFeeds.Find(cfg.NoteStore);
                if (fs == null) Note(p, cfg.NoteStore + " comes with a plugin that isn't added - add it on the Plugins page (Add or remove plugins, at the top).");
                else if (fs.Settings != null) fs.Settings(new SettingsPage(this, p, _propsPane), cfg);
            }
            else
                Note(p, "Kept with vShell's settings. One widget can hold several notes: right-click the note for New note, Rename, Delete " +
                        "and Next / Previous (or scroll on the note's name).");

            Section(p, "Look");
            string[] papers = ModuleTypes.NotePapers;
            ComboRow(p, "Paper", papers, delegate { return cfg.NotePaper != null && cfg.NotePaper.StartsWith("#") ? "Own" : cfg.NotePaper; }, delegate (string v)
            {
                if (v == "Own") { if (cfg.NotePaper == null || !cfg.NotePaper.StartsWith("#")) cfg.NotePaper = "#FFF9E2AF"; }
                else cfg.NotePaper = v;
                again();
            });
            if (cfg.NotePaper != null && cfg.NotePaper.StartsWith("#"))
                ColorRow(p, "Paper colour", delegate { return cfg.NotePaper; }, delegate (string v) { cfg.NotePaper = string.IsNullOrEmpty(v) ? "Theme" : v; }, false, false);
            Place(p, Row("Font (Theme = Segoe UI; or any installed font)", FontCombo(ModuleTypes.NoteFonts, delegate { return cfg.NoteFont; }, delegate (string v) { cfg.NoteFont = v; })));
            SliderRow(p, "Width (when the widget has no fixed width)", 120, 800, delegate { return cfg.NoteWidth; }, delegate (double v) { cfg.NoteWidth = (int)v; });
            if (!vdrop)
                Check(p, "Grow with the text (off = a fixed height, longer notes scroll)", delegate { return cfg.NoteGrow; }, delegate (bool v) { cfg.NoteGrow = v; again(); });
            if (vdrop)
                SliderRow(p, "Height (when the widget has no fixed height - with one, the feed fills it)", 60, 1000, delegate { return cfg.NoteHeight; }, delegate (double v) { cfg.NoteHeight = (int)v; });
            else if (!cfg.NoteGrow)
                SliderRow(p, "Height", 60, 1000, delegate { return cfg.NoteHeight; }, delegate (double v) { cfg.NoteHeight = (int)v; });
            if (cfg.NoteKind == "Checklist")
                ComboRow(p, "Done items", ModuleTypes.NoteDoneModes, delegate { return cfg.NoteDone; }, delegate (string v) { cfg.NoteDone = v; });
            if (cfg.NoteKind == "Log")
                SliderRow(p, "Show at most (entries)", 5, 500, delegate { return cfg.NoteLogMax; }, delegate (double v) { cfg.NoteLogMax = (int)v; });
            if (!vdrop) Check(p, "Show when it was last edited", delegate { return cfg.NoteShowEdited; }, delegate (bool v) { cfg.NoteShowEdited = v; });

            Section(p, "Editing");
            if (!vdrop && (cfg.NoteKind == "Note" || cfg.NoteKind == "Markdown"))
                ComboRow(p, "Start editing with", ModuleTypes.NoteEditModes, delegate { return cfg.NoteEditOn; }, delegate (string v) { cfg.NoteEditOn = v; });
            if (!vdrop) Check(p, "Lock - read only on the desktop", delegate { return cfg.NoteLocked; }, delegate (bool v) { cfg.NoteLocked = v; });
            Check(p, "Quick-note key: Win+Shift+N jumps here and starts typing (the first note that has it on)", delegate { return cfg.NoteHotkey; }, delegate (bool v) { cfg.NoteHotkey = v; });
            Note(p, "While you type, the widget comes in front of your windows and takes the keyboard; Esc, Done or clicking somewhere else " +
                    "puts it back on the desktop. Everything is saved as you type. A click that moves drags the widget as usual.");
        }

        /// <summary>The graph style choice (each style keeps its own graph size) and Show text; the rows below follow the style.</summary>
        void StyleRows(StackPanel p, ModuleConfig cfg)
        {
            ComboRow(p, "Style", ModuleTypes.StylesFor(cfg.Type), delegate { return cfg.Style; }, delegate (string v)
            {
                ModuleTypes.SetGraphStyle(cfg, v);
                ModulePane pane = _propsPane;
                Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the graph rows for this style
            });
            if (ModuleTypes.HasGauges(cfg.Type))
            {
                if (cfg.Style == "Ring")
                    Note(p, "Ring: a round gauge with the value inside and the label beside it. Its size, colour and warning are below.");
                else if (cfg.Style == "Tile")
                    Note(p, "Tile: the label, a big number and a sparkline (a meter for disk space). Set its width below (0 = as wide as its text); in a widget it is taller " +
                            "than one row; try it with Arrangement > Grid. Its colour and warning are below.");
            }
            if (cfg.Style == "Dots")
            {
                Note(p, "Dots: the readings as columns of dots on a dim dot field, the newest in the graph colour, with a small grey label. In a bar it's one " +
                        "row; in a widget it's a tile - the label, a big number and the dot graph filling the rest (taller widgets get more rows).");
                Check(p, "Numbers in dots", delegate { return cfg.Bag.Get("dots.numbers", true); }, delegate (bool v) { cfg.Bag.Set("dots.numbers", v); },
                      "The number drawn in dots, like the graph. Off = the number in your font (easier to read when small).");
                Check(p, "Show the label", delegate { return cfg.ShowText; }, delegate (bool v) { cfg.ShowText = v; });
            }
            else if (cfg.Style == "Ring")
                Check(p, "Show the label beside the ring", delegate { return cfg.ShowText; }, delegate (bool v) { cfg.ShowText = v; });
            else if (cfg.Style != "Tile")
                Check(p, "Show text", delegate { return cfg.ShowText; }, delegate (bool v) { cfg.ShowText = v; });
        }

        void Note(StackPanel p, string text)
        {
            if (AttachInfo(p, text)) return;       // Plugins / Notifications: an (i) on the row above
            var t = Txt(text, "vsDim", 12, false);
            t.TextWrapping = TextWrapping.Wrap;
            Place(p, t);
        }

        void ListButton(StackPanel p, string caption, Action click)
        {
            var b = Btn(caption, click);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            Place(p, b);
        }

        /// <summary>The "Graph" section shared by the graph-capable modules, then the "Warning" section.</summary>
        void GraphSettings(StackPanel p, ModuleConfig cfg, bool withStyle, Action extra, string warnUnit = null)
        {
            Section(p, "Graph");
            if (withStyle) StyleRows(p, cfg);
            if (extra != null) extra();
            // each style keeps its own width / height (switching style and back brings them back); a Tile sizes itself, a Ring is round
            if (cfg.Style == "Ring")
                SliderRow(p, "Ring size (% of the bar / module row height)", 20, 100, delegate { return cfg.GraphHeightPercent; }, delegate (double v) { cfg.GraphHeightPercent = (int)v; });
            else if (cfg.Style == "Tile")
                SliderRow(p, "Module width (0 = as wide as its text)", 0, 600, delegate { return cfg.TileWidth; }, delegate (double v) { cfg.TileWidth = v < 40 ? 0 : (int)v; });
            else
            {
                SliderRow(p, "Graph length", 20, 600, delegate { return cfg.GraphWidth; }, delegate (double v) { cfg.GraphWidth = (int)v; });
                SliderRow(p, "Graph height (%)", 20, 100, delegate { return cfg.GraphHeightPercent; }, delegate (double v) { cfg.GraphHeightPercent = (int)v; });
            }
            GraphLookRows(p, cfg);
            if (cfg.Type == ModuleTypes.Network || cfg.Type == ModuleTypes.Downloads)
            {
                ColorRow(p, "Download colour (graph and arrow; empty = Main Accent)", delegate { return cfg.Color; }, delegate (string v) { cfg.Color = v; }, true, false);
                ColorRow(p, "Upload colour (graph and arrow; empty = Secondary Accent)", delegate { return cfg.Color2; }, delegate (string v) { cfg.Color2 = v; }, true, false, Theme.SlotSecondary);
            }
            else if (!cfg.LoadColors || cfg.Style == "Text") ColorRow(p, "Graph colour", delegate { return cfg.Color; }, delegate (string v) { cfg.Color = v; }, true, false);
            LoadColorRows(p, cfg);
            WarnRow(p, cfg, warnUnit ?? (cfg.Type == ModuleTypes.Network ? "% of full scale" : "%"));
        }

        /// <summary>The graph's look for its style: line thickness, fill and curves (line, tile), box and grid (line, bars), time shown.
        /// all = every row, whatever the style (a plugin's graphs, each with its own style - SettingsPage.GraphLook); style = the rows for
        /// that style instead of cfg.Style's (a plugin keeping its own style - SettingsPage.GraphLook(cfg, style)).</summary>
        internal void GraphLookRows(StackPanel p, ModuleConfig cfg, bool all = false, string style = null)
        {
            ModulePane pane = _propsPane;
            Action again = delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); };
            string st = style ?? cfg.Style;
            bool line = all || st == "Line" || st == "Tile";
            bool boxed = all || st == "Line" || st == "Bars";
            bool dots = st == "Dots";     // only the time shown
            if (line)
            {
                ComboRow(p, "Line thickness", ModuleTypes.GraphLines, delegate { return cfg.GraphLine; }, delegate (string v) { cfg.GraphLine = v; });
                ComboRow(p, "Fill under the line", ModuleTypes.GraphFills, delegate { return cfg.GraphFill; }, delegate (string v) { cfg.GraphFill = v; },
                         "Light = a faint fill (as before). Solid = a stronger one. Fade = strong just under the line, fading out towards the bottom.");
                Check(p, "Smooth curves", delegate { return cfg.GraphSmooth; }, delegate (bool v) { cfg.GraphSmooth = v; },
                      "A soft curve through the readings instead of straight lines between them.");
            }
            if (boxed)
            {
                Check(p, "Background box", delegate { return cfg.GraphBack; }, delegate (bool v) { cfg.GraphBack = v; again(); },
                      "The box behind the graph. Off = just the line or bars.");
                if (cfg.GraphBack)
                    SliderRow(p, "Box corner rounding", 0, 12, delegate { return cfg.GraphRadius; }, delegate (double v) { cfg.GraphRadius = (int)v; });
                Check(p, "Grid lines", delegate { return cfg.GraphGrid; }, delegate (bool v) { cfg.GraphGrid = v; },
                      "Faint lines at a quarter, half and three quarters, and a dashed line at the warning level when the warning is on.");
            }
            if (line || boxed || dots)
            {
                ComboRow(p, "Time shown", ModuleTypes.GraphSpanNames, delegate
                {
                    int i = Array.IndexOf(ModuleTypes.GraphSpans, cfg.GraphSpan);
                    return ModuleTypes.GraphSpanNames[i < 0 ? 0 : i];
                }, delegate (string v)
                {
                    int i = Array.IndexOf(ModuleTypes.GraphSpanNames, v);
                    cfg.GraphSpan = ModuleTypes.GraphSpans[i < 0 ? 0 : i];
                }, all
                    ? "Line and bar graphs. Longer times average a few seconds per point / bar; at 1 minute bars are a second each, as many as fit."
                    : st == "Bars" || st == "Dots"
                    ? "At 1 minute each bar is a second, as many as fit. Longer times fit the whole time in: each bar is the average of a few seconds."
                    : "Longer times average a few seconds per point, so the line keeps its shape. The graph fills up as it goes (it starts empty).");
            }
        }

        /// <summary>Colour by load: on, and its three colours.</summary>
        internal void LoadColorRows(StackPanel p, ModuleConfig cfg, bool all = false)
        {
            if ((!all && cfg.Style == "Text") || !ModuleTypes.HasLoadColors(cfg.Type)) return;
            ModulePane pane = _propsPane;
            Check(p, "Colour by load", delegate { return cfg.LoadColors; }, delegate (bool v)
            {
                cfg.LoadColors = v;
                Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the three colours show / hide
            }, "The graph blends from the low colour (empty) through the middle colour to the high colour (full), instead of one colour " +
               "that changes past the warning level. The text still turns the warning colour past the threshold.");
            if (!cfg.LoadColors) return;
            ColorRow(p, "Low colour (empty = Main Accent, faint)", delegate { return cfg.LoadColor1; }, delegate (string v) { cfg.LoadColor1 = v; }, true, false);
            ColorRow(p, "Middle colour (empty = Main Accent, stronger)", delegate { return cfg.LoadColor2; }, delegate (string v) { cfg.LoadColor2 = v; }, true, false);
            ColorRow(p, "High colour (empty = Secondary Accent)", delegate { return cfg.LoadColor3; }, delegate (string v) { cfg.LoadColor3 = v; }, true, false, Theme.SlotSecondary);
        }

        /// <summary>Warning colour and threshold, in the Graph section: past the threshold the graph and text are the warning colour (fading into it just before).</summary>
        Action _refreshWarn;

        void WarnRow(StackPanel p, ModuleConfig cfg, string unit)
        {
            _refreshWarn = null;
            ColorRow(p, "Warning colour", delegate { return cfg.WarnColor; }, delegate (string v) { cfg.WarnColor = v; }, true, false, Theme.SlotSecondary);
            Check(p, "Change colour past threshold", delegate { return cfg.WarnOn; }, delegate (bool v)
            {
                // a battery warns when low: give it a sensible start the first time
                if (v && !cfg.WarnOn && cfg.Type == ModuleTypes.Battery && !cfg.WarnBelow && cfg.WarnAt == 85) { cfg.WarnBelow = true; cfg.WarnAt = 20; }
                cfg.WarnOn = v;
            });
            if (cfg.Type == ModuleTypes.Battery)
                NumberRow(p, "Threshold (" + unit + ")", delegate { return cfg.WarnAt; }, delegate (double v) { cfg.WarnAt = v; });
            else
            {
                // automatic = 70 % of the graph's range until you type your own; empty the box to go back to automatic
                var box = new TextBox { Width = 100 };
                var how = Txt("", "vsDim", 11.5, false);
                how.Margin = new Thickness(0, 2, 0, 0);
                Action show = delegate
                {
                    box.Text = cfg.WarnAt.ToString(CultureInfo.CurrentCulture);
                    how.Text = cfg.WarnAuto ? "Automatic: 70 % of the graph's range. Type a number to set your own." : "Your own threshold. Empty the box to go back to automatic (70 % of the graph's range).";
                };
                Action commit = delegate
                {
                    if (_loading) return;
                    string text = box.Text.Trim();
                    double v;
                    if (text.Length == 0) { cfg.WarnAuto = true; cfg.SyncWarn(); _mgr.SettingsChanged(); }
                    else if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    {
                        if (Math.Abs(v - cfg.WarnAt) > 1e-9) { cfg.WarnAt = v; cfg.WarnAuto = false; _mgr.SettingsChanged(); }
                    }
                    show();
                };
                box.LostFocus += delegate { commit(); };
                box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
                show();
                _refreshWarn = show;
                Place(p, Row("Threshold (" + unit + ")", box));
                Place(p, how);
            }
            if (cfg.Type == ModuleTypes.Battery)
                Check(p, "Warn below threshold - e.g. battery low; off = warn at or above it", delegate { return cfg.WarnBelow; }, delegate (bool v) { cfg.WarnBelow = v; });
        }

        /// <summary>The text shown in front of the value; the module's own default is shown until you change it.</summary>
        void TitleRow(StackPanel p, ModuleConfig cfg, string fallback)
        {
            TextRow(p, "Text in front (empty = none)", delegate { return cfg.Title == null ? fallback : cfg.Title; }, delegate (string v) { cfg.Title = v; });
        }

        void EditServices(ModuleConfig cfg)
        {
            Func<Window, ServiceCheck, bool> edit = delegate (Window owner, ServiceCheck c)
            {
                var fields = new List<FormField>
                {
                    new FormField("Name", c.Name),
                    FormField.Combo("Check by", c.Kind, ModuleTypes.ServiceKinds),
                    new FormField("Address", c.Target) { Hint = "Http: a web address such as status.example.com or http://192.168.1.10:8123.   Tcp: host:port such as 192.168.1.5:22.   Ping: a host name or IP address." },
                    FormField.Check("Accept a self-signed HTTPS certificate", c.IgnoreCert)
                };
                if (!FormWindow.Ask(owner, "Service to watch", fields)) return false;
                c.Name = fields[0].Value;
                c.Kind = fields[1].Value;
                c.Target = fields[2].Value;
                c.IgnoreCert = fields[3].Checked;
                return c.Target.Length > 0;
            };
            var dlg = new ListEditorWindow<ServiceCheck>("Services to watch",
                "Each one gets a coloured dot. Checks run in the background, never on the bar's own thread.",
                cfg.Checks,
                delegate (ServiceCheck c) { return (c.Name.Length > 0 ? c.Name : c.Target) + "   -   " + c.Kind + " " + c.Target; },
                edit, delegate { _mgr.SettingsChanged(); });
            dlg.Owner = this;
            dlg.AddAction("Add\u2026", delegate (ListEditorWindow<ServiceCheck> w)
            {
                var c = new ServiceCheck();
                if (!edit(w, c)) return;
                w.Items.Add(c);
                w.Changed(w.Items.Count - 1);
            });
            // copy the picked services (or all of them) and paste them into another Service status module - also through the clipboard,
            // so it works after a restart too
            Button pasteBtn = null;
            dlg.AddAction("Copy", delegate (ListEditorWindow<ServiceCheck> w)
            {
                List<ServiceCheck> pick = w.SelectedItems;
                if (pick.Count == 0) pick = w.Items;
                if (pick.Count == 0) return;
                _copiedChecks = CloneJson(pick);
                try { Clipboard.SetText(System.Text.Json.JsonSerializer.Serialize(new ServiceClip { VShellServices = pick })); } catch { }
                if (pasteBtn != null) pasteBtn.IsEnabled = true;
                MessageBox.Show(w, (pick.Count == 1 ? "1 service copied." : pick.Count + " services copied.") +
                                "\n\nOpen another Service status module's list and click Paste to add them there.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            pasteBtn = dlg.AddActionButton("Paste", delegate (ListEditorWindow<ServiceCheck> w)
            {
                List<ServiceCheck> from = ClipboardChecks() ?? _copiedChecks;
                if (from == null || from.Count == 0) return;
                int added = 0;
                foreach (ServiceCheck c in CloneJson(from))
                {
                    // the same check already in this list: not again
                    if (w.Items.Exists(delegate (ServiceCheck x) { return string.Equals(x.Kind, c.Kind, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Target, c.Target, StringComparison.OrdinalIgnoreCase); })) continue;
                    w.Items.Add(c);
                    added++;
                }
                if (added > 0) w.Changed(w.Items.Count - 1);
                else MessageBox.Show(w, "Everything copied is already in this list.", "Paste", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            pasteBtn.IsEnabled = _copiedChecks != null || ClipboardChecks() != null;
            dlg.ShowDialog();
        }

        static List<ServiceCheck> _copiedChecks;

        /// <summary>What Copy puts on the clipboard (so a paste can tell it from any other text).</summary>
        sealed class ServiceClip { public List<ServiceCheck> VShellServices { get; set; } }

        /// <summary>Services copied from vShell that are on the clipboard now (null = none).</summary>
        static List<ServiceCheck> ClipboardChecks()
        {
            try
            {
                if (!Clipboard.ContainsText()) return null;
                string s = Clipboard.GetText();
                if (s == null || s.IndexOf("VShellServices", StringComparison.Ordinal) < 0) return null;
                var clip = System.Text.Json.JsonSerializer.Deserialize<ServiceClip>(s);
                if (clip == null || clip.VShellServices == null || clip.VShellServices.Count == 0) return null;
                clip.VShellServices.RemoveAll(delegate (ServiceCheck c) { return c == null || string.IsNullOrWhiteSpace(c.Target); });
                foreach (ServiceCheck c in clip.VShellServices)
                {
                    if (c.Name == null) c.Name = "";
                    if (Array.IndexOf(ModuleTypes.ServiceKinds, c.Kind) < 0) c.Kind = "Http";
                }
                return clip.VShellServices.Count > 0 ? clip.VShellServices : null;
            }
            catch { return null; }
        }

    }
}
