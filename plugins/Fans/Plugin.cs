using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("fans", "Fans",
    Description = "Your fans' and pump's speeds from HWiNFO or LibreHardwareMonitor - as tiles or graphs with a little turning fan, on a bar or in a widget - and a notification when one stops.",
    Glyph = "", Group = "Devices", Author = "version", Adds = "Notifications when a fan stops or runs slow", Types = "Fans")]

namespace VShellFans
{
    /// <summary>
    /// The Fans plugin - made only with vShell's plugin API: the Fans module (every fan HWiNFO / LibreHardwareMonitor reports in rpm, as
    /// tiles, rows or on a bar) and notifications when a fan stops or runs slower than set.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        public const string Type = "Fans";

        public void Load(PluginHost host)
        {
            FanSettings.Current = FanSettings.Of(host.Manager.Settings);
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Fans", ShortName = "Fans", Glyph = "", Group = "System",
                Blurb = "fan and pump speeds, with graphs", HasPopup = true, WidgetSize = new Size(340, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new FanModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic,
                Needs = delegate (BarSettings s) { return HwInfo.Available ? null : "Needs HWiNFO or LibreHardwareMonitor sharing its sensors"; }
            });
            host.AddIntegration("Fans", SettingsSection);
            host.AddNotifications(NotifySection);
            host.AddNotifyKind("Fans.Stopped", NotifyLevel.Alert);
            host.AddNotifyKind("Fans.Slow", NotifyLevel.Important);
            host.AddNotifySource("Fans");
            host.OnSettings(delegate (BarSettings s)
            {
                FanSettings.Current = FanSettings.Of(s);
                FanStore.Reread();
                FanStore.Configure();
            });
            FanStore.Start(host.Manager);
        }

        public void Unload() { FanIcon.StopAll(); FanStore.Stop(); }

        // =================================================================
        // Settings > Plugins > Fans
        // =================================================================
        static void SettingsSection(SettingsWindow.SettingsPage p)
        {
            FanSettings k = FanSettings.Of(p.Settings);
            p.Header("Your fans");
            FanStore.Reread();
            List<Fan> all = FanStore.All;
            p.Note("Every fan and pump " + HwInfo.SourceName + " reports in rpm. " + (HwInfo.IsLhm
                ? "LibreHardwareMonitor shares all of them."
                : "In HWiNFO's Sensors window, right-click each fan and choose \"Report value in Gadget\" (or use LibreHardwareMonitor: a Hardware sensor module's settings > Sensors from)."));
            if (all.Count == 0)
            {
                p.Add(p.Txt(HwInfo.Available ? "No fan speeds yet." : HwInfo.SourceName + " isn't sharing any values right now.", "vsDim", 12.5));
                return;
            }
            var head = Line(p, p.Txt("Show", "vsDim", 12), p.Txt("Name", "vsDim", 12), p.Txt("Now", "vsDim", 12), p.Txt("Max rpm", "vsDim", 12), p.Txt("0rpm mode", "vsDim", 12), p.Txt("Warn below", "vsDim", 12), null, null);
            p.Add(head);
            for (int i = 0; i < all.Count; i++)
            {
                Fan f = all[i];
                int at = i;
                var show = new CheckBox { IsChecked = !f.Hidden, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Show it in the Fans modules" };
                show.Click += delegate
                {
                    List<string> hidden = k.Hidden;
                    hidden.RemoveAll(delegate (string x) { return string.Equals(x, f.Key, StringComparison.OrdinalIgnoreCase); });
                    if (show.IsChecked != true) hidden.Add(f.Key);
                    k.Hidden = hidden;
                    p.Changed();
                };
                var name = new TextBox { Text = f.Name, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = f.Sensor + " - " + f.Label + "\nEmpty = its own name" };
                name.LostFocus += delegate
                {
                    Dictionary<string, string> names = k.Names;
                    string v = name.Text.Trim();
                    if (v.Length == 0 || v == f.Label) names.Remove(f.Key); else names[f.Key] = v;
                    k.Names = names;
                    p.Changed();
                };
                string own;
                var min = new TextBox { Text = k.Mins.TryGetValue(f.Key, out own) ? own : "", VerticalContentAlignment = VerticalAlignment.Center,
                                        ToolTip = "Its own warning speed in rpm (a notification when it runs slower).\nEmpty = the setting for all fans, 0 = never warn about this one." };
                min.LostFocus += delegate
                {
                    Dictionary<string, string> mins = k.Mins;
                    int n;
                    if (int.TryParse(min.Text.Trim(), out n) && n >= 0) mins[f.Key] = n.ToString(); else { mins.Remove(f.Key); min.Text = ""; }
                    k.Mins = mins;
                    p.Changed();
                };
                var max = new TextBox { Text = k.Maxes.TryGetValue(f.Key, out own) ? own : "", VerticalContentAlignment = VerticalAlignment.Center,
                                        ToolTip = "Its top speed in rpm: its graphs fill at this speed, and Colour by load reaches its high colour there.\n" +
                                                  "Empty = automatic (the graphs scale to what it's shown lately)." };
                max.LostFocus += delegate
                {
                    Dictionary<string, string> maxes = k.Maxes;
                    int n;
                    if (int.TryParse(max.Text.Trim().Replace(",", ""), out n) && n >= 100) { maxes[f.Key] = Math.Min(30000, n).ToString(); max.Text = maxes[f.Key]; }
                    else { maxes.Remove(f.Key); max.Text = ""; }
                    k.Maxes = maxes;
                    p.Changed();
                    FanStore.Reread();
                };
                var alone = new CheckBox
                {
                    IsChecked = f.StopsAlone, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "0rpm mode: the fan switches itself off when it isn't needed (a GPU fan while the GPU is idle, a case fan while it's cool),\n" +
                              "so 0 rpm is normal for it - no \"stopped\" notification or warning colour.\n" +
                              (f.StopsAloneSet ? "Set by you." : f.Gpu ? "Found by itself: it's on the graphics card." : "Found by itself: not a GPU fan.")
                };
                alone.Click += delegate
                {
                    Dictionary<string, string> marks = k.OwnStop;
                    bool on = alone.IsChecked == true;
                    bool auto = f.Gpu && k.SkipGpu;
                    if (on == auto) marks.Remove(f.Key); else marks[f.Key] = on ? "1" : "0";      // the same as found: back to automatic
                    k.OwnStop = marks;
                    p.Changed();
                    FanStore.Reread();
                };
                Button up = p.Btn("↑", delegate { Move(p, k, at, -1); });
                Button down = p.Btn("↓", delegate { Move(p, k, at, 1); });
                up.IsEnabled = i > 0;
                down.IsEnabled = i < all.Count - 1;
                p.Add(Line(p, show, name, p.Txt(f.Rpm.ToString("N0") + " rpm", "vsDim", 12.5), max, alone, min, up, down));
            }
            p.Note("Untick a fan to leave it out (a header with nothing on it, say). Rename one to what it is - \"Front intake\" - and move them into the order you want. " +
                   "Max rpm: the fan's top speed - its graphs fill at it and Colour by load reaches its high colour there (empty = automatic, scaled to what it's shown lately). " +
                   "0rpm mode: for a fan that switches itself off when it isn't needed - 0 rpm is normal for it, so it's never \"stopped\"; ticked by itself for fans on " +
                   "the graphics card, tick or untick any fan to change that. Warn below gives that fan its own speed for the slow-fan notification.");
        }

        /// <summary>One row of the fans table: show, name, now, max rpm, 0rpm mode, warn below, up, down.</summary>
        static Grid Line(SettingsWindow.SettingsPage p, UIElement show, UIElement name, UIElement now, UIElement max, UIElement alone, UIElement min, UIElement up, UIElement down)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            foreach (double w in new[] { 52.0, -1, 84, 80, 84, 84, 40, 40 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            UIElement[] cells = { show, name, now, max, alone, min, up, down };
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null) continue;
                var fe = cells[i] as FrameworkElement;
                if (fe != null) { fe.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
                Grid.SetColumn(cells[i], i);
                g.Children.Add(cells[i]);
            }
            return g;
        }

        static void Move(SettingsWindow.SettingsPage p, FanSettings k, int at, int by)
        {
            var keys = new List<string>();
            foreach (Fan f in FanStore.All) keys.Add(f.Key);
            int to = at + by;
            if (at < 0 || to < 0 || at >= keys.Count || to >= keys.Count) return;
            string x = keys[at]; keys[at] = keys[to]; keys[to] = x;
            k.Order = keys;
            p.Changed();
            FanStore.Reread();
            p.Redraw();
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            FanSettings k = FanSettings.Of(p.Settings);
            p.NotifySection("Fan notifications", HwInfo.Available, HwInfo.SourceName);
            p.Check("A fan stops", delegate { return k.NotifyStop; }, delegate (bool v) { k.NotifyStop = v; p.Changed(); });
            p.Note("Only a fan that was turning: a header with nothing on it, reading 0 all along, never counts.");
            p.Check("No notification for 0rpm GPU fans", delegate { return k.SkipGpu; }, delegate (bool v) { k.SkipGpu = v; p.Changed(); });
            p.Note("Most graphics cards switch their fans off while the GPU is idle (0rpm mode), so 0 rpm is normal for them. A fan on the graphics card is found by its " +
                   "name (GPU, Radeon, GeForce, NVIDIA, Intel Arc) and gets 0rpm mode by itself. Any fan can be set by hand - 0rpm mode, on the Settings tab - and that wins over this.");
            p.Check("A fan runs slower than", delegate { return k.NotifySlow; }, delegate (bool v) { k.NotifySlow = v; p.Changed(); });
            p.Number("… rpm", delegate { return k.SlowRpm; }, delegate (double v) { k.SlowRpm = (int)Math.Round(v); p.Changed(); });
            p.Note("A fan can have its own speed instead (Warn below, on the Settings tab) - or 0 there to never warn about it.");
            p.Number("For at least (seconds)", delegate { return k.Seconds; }, delegate (double v) { k.Seconds = (int)Math.Round(v); p.Changed(); });
            p.Kind("Stopped", "Fans.Stopped");
            p.Kind("Slow", "Fans.Slow");
            p.Note("The Fans modules show a stopped or slow fan in the warning colour once this time has passed, whether or not you're told about it.");
        }

        // =================================================================
        // The module's settings
        // =================================================================
        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            FanView v = FanView.Of(cfg);
            bool widget = p.IsWidget;
            if (!widget) p.Title(cfg, "");

            // which fans (as the CPU module's own options, first)
            List<Fan> shown = FanStore.Shown();
            const string Every = "Every fan";
            var names = new List<string> { Every };
            foreach (Fan f in shown) names.Add(f.Name);
            p.Combo("Fans", names.ToArray(), delegate
            {
                Fan f = shown.Find(delegate (Fan x) { return x.Key == v.Show; });
                return f != null ? f.Name : Every;
            }, delegate (string s)
            {
                Fan f = shown.Find(delegate (Fan x) { return x.Name == s; });
                v.Show = f != null ? f.Key : "";
                p.Changed();
            });
            p.Note("Every fan: all the ones ticked in Settings > Plugins > Fans, in that order. Or just one of them.");
            p.Check("A turning fan by each speed", delegate { return v.Animate; }, delegate (bool b) { v.Animate = b; p.Changed(); });
            p.Note("It turns slower than the real fan - faster as the fan speeds up - and stops when it does. Off: a still fan (a little less work for your PC). " +
                   "It also stands still while a full-screen app is in front.");

            // the Graph section, as the CPU module's: Style first, then its size, then the look for that style
            p.Section("Graph");
            string style = v.Style(widget);
            p.Combo("Style", widget ? FanView.WidgetStyles : FanView.BarStyles, delegate { return style; }, delegate (string s) { v.SetStyle(s); p.Changed(); p.Redraw(); });
            string of = widget ? "the row" : "the bar";
            switch (style)
            {
                case "Tile":
                    p.Note(widget ? "Tile: each fan's name, its speed in big numbers and a line of the last while under it - as the Tile style draws a sensor."
                                  : "Tile: as the Tile style draws in a bar - the name, the speed over a line along the bar's bottom edge (on one line in a slim bar). Click for every fan in a popup.");
                    if (widget) p.Slider("Tiles across", 1, 4, delegate { return v.Across; }, delegate (double d) { v.Across = (int)Math.Round(d); p.Changed(); });
                    else p.Slider("Tile width (0 = about 8 characters)", 0, 300, delegate { return v.TileWidth; }, delegate (double d) { v.TileWidth = d < 40 ? 0 : (int)Math.Round(d); p.Changed(); });
                    break;
                case "Ring":
                    p.Note("Ring: a round gauge by each fan with the fan turning in its middle; it fills as the fan speeds up." + (widget ? "" : " Click for every fan in a popup."));
                    p.Slider("Ring size (% of " + of + "'s height)", 20, 100, delegate { return v.RingSize; }, delegate (double d) { v.RingSize = (int)Math.Round(d); p.Changed(); });
                    break;
                case FanView.FanOnly:
                    p.Note("Fan only: the turning fan and the fastest speed - or which fan stopped, in the warning colour. Click for every fan in a popup.");
                    break;
                default:
                    p.Note(style + ": by each fan its turning fan, its name, the graph and the speed." + (widget ? "" : " Click for every fan in a popup.") +
                           (style == "Dots" ? " Dots: the speed over time as columns of dots, the newest in the graph colour (needs vShell 0.38.227)." : ""));
                    p.Slider("Graph length (0 = automatic)", 0, 300, delegate { return v.GraphLen; }, delegate (double d) { v.GraphLen = (int)Math.Round(d); p.Changed(); });
                    p.Slider("Graph height (% of " + of + ", 0 = automatic)", 0, 100, delegate { return v.GraphHeight; }, delegate (double d) { v.GraphHeight = (int)Math.Round(d); p.Changed(); });
                    p.Note(widget ? "Automatic: the graph takes the room left in the row, 60 % of its height (a meter: a thin bar)."
                                  : "Automatic: about 4 characters long and 45 % of the bar's height (a meter: a thin bar).");
                    break;
            }
            if (style != FanView.FanOnly)
            {
                p.Number("Speed that fills the graph (rpm, 0 = automatic)", delegate { return v.FullScale; }, delegate (double d) { v.FullScale = (int)Math.Round(d); p.Changed(); });
                p.Note("For fans without their own Max rpm (Settings > Plugins > Fans) - a fan with one always fills its graph at it. Automatic: scaled to what it's shown lately.");
                p.GraphLook(cfg, style);       // the look rows for this style, the graph colour and Colour by load - as the CPU module's
                // (vShell 0.38.185's GraphLook(cfg, style) has no colour rows: the graph colour here, so there's always one)
                if (typeof(BarManager).Assembly.GetName().Version < new Version(0, 38, 186))
                    p.Color("Graph colour", delegate { return cfg.Color; }, delegate (string s) { cfg.Color = s; p.Changed(); }, true, false);
            }
            else p.Color("Fan colour", delegate { return cfg.Color; }, delegate (string s) { cfg.Color = s; p.Changed(); }, true, false);

            // the warning: the plugin's notifications (stopped / slow) - the same rule turns a fan the warning colour here
            p.Button("A stopped or slow fan shows in the warning colour", "Names, order and warnings ›", delegate { p.OpenPlugin("fans"); });
        }
        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            var icon = new FanIcon(14);
            icon.Colour(t.MainAccent, t.Bg);
            return ModulePics.Row(6, icon, ModulePics.T(t, "1,240", t.Fg, 13), ModulePics.T(t, "860", t.Fg, 13));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Func<string, string, UIElement> tile = delegate (string name, string rpm)
            {
                var icon = new FanIcon(12);
                icon.Colour(t.MainAccent, t.Bg);
                return ModulePics.Col(1, ModulePics.Row(6, ModulePics.T(t, name, t.FgDim, 10), icon), ModulePics.T(t, rpm + " rpm", t.Fg, 15, true));
            };
            return ModulePics.Col(6, ModulePics.Row(18, tile("CPU fan", "1,240"), tile("Pump", "2,650")), ModulePics.Row(18, tile("Front", "860"), tile("Rear", "910")));
        }
    }
}
