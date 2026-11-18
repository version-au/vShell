using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The Home Assistant plugin's settings: the connection, the modules' settings, the notification rules and the editors.</summary>
    static class HaUi
    {
        // =================================================================
        // The Plugins page: the connection
        // =================================================================
        public static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            HaSettings h = HaSettings.Of(p.Settings);
            p.Header("Home Assistant");
            p.Text("Address (for example http://homeassistant.local:8123 or https://ha.example.com)", delegate { return h.Url; }, delegate (string v) { h.Url = v; });
            PasswordBox pw = p.Secret("Access token", "access token", delegate { return h.Token; }, delegate (string v) { h.Token = v; },
                "In Home Assistant open your profile (bottom of the sidebar) > Security > Long-lived access tokens > Create token, and paste it here. " +
                "The token is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return h.IgnoreCert; }, delegate (bool v) { h.IgnoreCert = v; });
            p.Slider("Check every (seconds)", 2, 60, delegate { return h.PollSeconds; }, delegate (double v) { h.PollSeconds = (int)v; });
            p.Text("Run box keyword (without the !)", delegate { return h.Bang; }, delegate (string v) { h.Bang = v; },
                   "!" + h.Bang + " kitchen - the palette's Home Assistant commands for \"kitchen\": turn lights and switches on / off, set a brightness or temperature, " +
                   "run scripts and scenes (Enter does the top one; the ones that ask need Enter twice).");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { h.Token = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return HaClient.TestAsync(h.Url, h.Token, h.IgnoreCert); });
        }

        // =================================================================
        // The Notifications page: the rules, and vDrop's notifications
        // =================================================================
        public static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            HaSettings h = HaSettings.Of(S);
            p.NotifySection("Home Assistant notifications", HaClient.Configured, "Home Assistant");
            var count = p.Txt("", "vsDim", 12.5);
            Action showCount = delegate
            {
                List<HaNotifyRule> rules = h.Rules;
                int on = rules.FindAll(delegate (HaNotifyRule r) { return r.Enabled; }).Count;
                count.Text = rules.Count == 0 ? "No rules yet." : rules.Count + " rule" + (rules.Count > 1 ? "s" : "") + (on < rules.Count ? " (" + on + " on)" : "");
            };
            showCount();
            var row = new DockPanel();
            var edit = p.Btn("Rules…", delegate { EditRules(p, h); showCount(); });
            row.Children.Add(edit);
            count.Margin = new Thickness(12, 0, 0, 0);
            count.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(count);
            p.Row("Rules: when an entity does something, show a notification", row, false,
                "A rule watches one entity: it becomes a state (off, open, home …), goes above or below a number, or changes at all - optionally only once it has stayed that way " +
                "for some minutes. The message can use {name}, {state}, {unit} and {entity}. Clicking the notification can open Home Assistant or toggle the entity. " +
                "Examples: the washing machine's power goes below 5 (W) for 3 minutes → \"Washing is done\"; the garage door is open for 10 minutes; the battery goes above 99.");
            p.Note("Rules work without a Home Assistant module too. Each rule has its own kind (Normal, Important or Alert).");

            p.NotifySection("vDrop notifications (Gotify)", HaClient.Configured, "Home Assistant (vDrop runs in it)");
            p.Check("Show notifications from the vDrop add-on", delegate { return h.VDropNotifications; }, delegate (bool v) { h.VDropNotifications = v; });
            p.Note("Apps on your network send notifications to vDrop the way they would to a Gotify server (vDrop's Notifications tab > Apps & tokens " +
                   "gives each one its address and token). vShell shows the ones you haven't read that arrived since this PC was last connected, then " +
                   "new ones as they come. Priority 0-3 goes to the history only, 4-7 is a Normal card, 8-10 an Alert. Reading one here (clicking it " +
                   "or closing its card) reads it in vDrop too, and reading it on your phone reads it here. Needs the Home Assistant connection.");
        }

        static void EditRules(SettingsWindow.SettingsPage p, HaSettings h)
        {
            Func<Window, HaNotifyRule, bool> edit = delegate (Window owner, HaNotifyRule r)
            {
                var fields = new List<FormField>
                {
                    FormField.Check("On", r.Enabled),
                    new FormField("Entity", r.Entity) { Hint = "For example binary_sensor.front_door or sensor.washer_power." },
                    FormField.Combo("When it", r.When, HaNotifyRule.Whens),
                    new FormField("State / number", r.Value) { Hint = "Becomes: the state (on, off, open, home …). Above / Below: a number in the entity's unit. Changes: leave empty." },
                    new FormField("Only after (minutes)", r.ForMinutes.ToString(CultureInfo.InvariantCulture)) { Hint = "0 = straight away. Otherwise it must stay that way this long." },
                    new FormField("Message", r.Message) { Hint = "Empty = \"{name} is {state}\". You can use {name}, {state}, {unit} and {entity}." },
                    FormField.Combo("Clicking it", r.Click, HaNotifyRule.Clicks),
                    FormField.Combo("Kind", r.Level, ModuleTypes.NotifyLevels)
                };
                if (!FormWindow.Ask(owner, "Home Assistant notification rule", fields)) return false;
                r.Enabled = fields[0].Checked;
                r.Entity = fields[1].Value;
                r.When = fields[2].Value;
                r.Value = fields[3].Value;
                int m;
                r.ForMinutes = int.TryParse(fields[4].Value, out m) ? m : 0;
                r.Message = fields[5].Value;
                r.Click = fields[6].Value;
                r.Level = fields[7].Value;
                r.Fix();
                return true;
            };
            var dlg = new ListEditorWindow<HaNotifyRule>("Home Assistant notification rules",
                "Each rule shows a notification when its entity does something. Double-click one to change it.",
                h.Rules,
                delegate (HaNotifyRule r) { return (r.Enabled ? "" : "(off)  ") + r.Describe() + "  →  " + (r.Message.Length > 0 ? r.Message : "{name} is {state}"); },
                edit, delegate { p.Changed(); });
            dlg.Owner = p.Window;
            dlg.AddAction("Add from Home Assistant…", delegate (ListEditorWindow<HaNotifyRule> w)
            {
                if (!HaClient.Configured) { Questions.Tell(w, "Set the Home Assistant address and token on the Plugins page first.", "Home Assistant"); return; }
                List<string> picked = HaEntityPickerWindow.Pick(w, false);
                if (picked == null || picked.Count == 0) return;
                var r = new HaNotifyRule { Entity = picked[0] };
                if (edit(w, r)) { w.Items.Add(r); w.Changed(w.Items.Count - 1); }
            });
            dlg.AddAction("Add (type the entity)…", delegate (ListEditorWindow<HaNotifyRule> w)
            {
                var r = new HaNotifyRule();
                if (edit(w, r) && r.Entity.Length > 0) { w.Items.Add(r); w.Changed(w.Items.Count - 1); }
            });
            dlg.ShowDialog();
        }

        // =================================================================
        // Module settings
        // =================================================================
        public static void EntitiesSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            HaView v = new HaView(cfg);
            if (!HaClient.Configured) p.Note("Set up the Home Assistant connection first (Plugins page in this window).");
            p.Check("Bubble Card buttons - each entity as a rounded button (switches light up, lights / fans / covers get a slider)", delegate { return v.Bubble; }, delegate (bool on)
            {
                v.Bubble = on;
                p.Redraw();
            });
            if (p.IsWidget)     // on a bar they always sit side by side
            {
                p.Combo("Layout", ModuleTypes.TileLayouts, delegate { return cfg.TileLayout; }, delegate (string s) { cfg.TileLayout = s; p.Redraw(); });
                p.Note("Row puts the " + (v.Bubble ? "buttons" : "entities") + " side by side; List puts one per row.");
            }
            if (v.Bubble && p.IsWidget && cfg.TileLayout != "List")
                p.Slider("Button width (side by side)", 120, 600, delegate { return v.BubbleWidth; }, delegate (double d) { v.BubbleWidth = (int)d; });
            if (!v.Bubble) p.Check("Show names in front of the values", delegate { return cfg.TileNames; }, delegate (bool on) { cfg.TileNames = on; });
            p.ListButton("Choose entities… (" + v.Entities.Count + ")", delegate { EditEntities(p, cfg); });
            p.Note((v.Bubble ? "Each entity's button type (Auto, Switch, Slider, State) is in its settings: Choose entities… > double-click it." + (p.IsWidget ? " " : " On a bar the buttons are as tall as the bar. ") : "") +
                   "Clicking a light, switch, scene, script, button, lock, cover or automation operates it.");
            if (!v.Bubble)
            {
                p.Section("Graphs");
                p.Note("How the entities' graphs look. Each entity's graph (Meter, Bars, Line), colour and range are in its own settings: Choose entities… > double-click it.");
                p.GraphLook(cfg);
            }
        }

        public static void ClimateSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            HaView v = new HaView(cfg);
            if (!HaClient.Configured) p.Note("Set up the Home Assistant connection first (Plugins page in this window).");
            EntityPickRow(p, "Climate entity (the air conditioner, climate.…)", delegate { return v.ClimateEntity; }, delegate (string s) { v.ClimateEntity = s.Trim(); });
            p.ListButton("Zones… (" + v.Zones.Count + ")", delegate { EditZones(p, cfg); p.Redraw(); });
            if (v.Zones.Count > 0) p.Check("Show the zones", delegate { return v.ShowZones; }, delegate (bool on) { v.ShowZones = on; });
            if (!p.IsWidget)
            {
                p.Check("Show the target temperature next to the room temperature", delegate { return v.ShowTarget; }, delegate (bool on) { v.ShowTarget = on; });
                p.Title(cfg, "");
            }
            p.Note("Laid out like Bubble Card: the round icon turns the air conditioner on / off, − and + change the temperature (scrolling does too), and the buttons " +
                   "under it pick the mode and fan speed. Each zone is a fan entity with a slider from 0% (off) to 100% in 10% steps, two to a row - click or drag along it, or click its icon for off / on." +
                   (p.IsWidget ? "" : " On the bar: click for the controls, scroll to change the temperature."));
        }

        public static void PowerFlowSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            HaView v = new HaView(cfg);
            if (!HaClient.Configured) p.Note("Set up the Home Assistant connection first (Plugins page in this window).");
            EntityPickRow(p, "Solar power", delegate { return v.Solar; }, delegate (string s) { v.Solar = s; });
            EntityPickRow(p, "Grid import power", delegate { return v.GridImport; }, delegate (string s) { v.GridImport = s; });
            EntityPickRow(p, "Grid export power", delegate { return v.GridExport; }, delegate (string s) { v.GridExport = s; });
            EntityPickRow(p, "Battery power", delegate { return v.Battery; }, delegate (string s) { v.Battery = s; });
            p.Check("Battery power is positive when discharging - off = positive when charging", delegate { return v.BatteryInvert; }, delegate (bool on) { v.BatteryInvert = on; });
            EntityPickRow(p, "Battery charge (%)", delegate { return v.BatterySoc; }, delegate (string s) { v.BatterySoc = s; });
            EntityPickRow(p, "Home use - optional, empty = worked out from the others", delegate { return v.Home; }, delegate (string s) { v.Home = s; });
            p.Note("Sensors in W or kW. Leave out what you do not have (e.g. no battery). The dots move in the direction the power flows, faster for more power; " +
                   "the ring round Home shows how much of it comes from solar, battery and grid. It is as wide as the widget (set the widget's Width; automatic = 200).");
            p.Section("Look");
            p.Color("Solar colour", delegate { return v.SolarColor; }, delegate (string s) { v.SolarColor = s; }, true, false, Theme.SlotSecondary);
            p.Color("Grid colour", delegate { return v.GridColor; }, delegate (string s) { v.GridColor = s; }, true, false, Theme.SlotMain);
            p.Color("Battery colour", delegate { return v.BatteryColor; }, delegate (string s) { v.BatteryColor = s; }, true, false, Theme.SlotText);
        }

        /// <summary>A Notes module stored in vDrop: what it shows, and Sync now.</summary>
        public static void VDropSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            if (!HaClient.Configured) p.Note("Set up the Home Assistant connection first (Plugins page in this window).");
            p.Note("Shows vDrop (the Home Assistant add-on) like its own page: tabs for All, Links, Media and Notes, a list for Inbox, Pinned, " +
                   "Done or Everything, search, and every item with its preview and picture - newest at the bottom, pinned ones first. Type in " +
                   "the box at the bottom to add a note or a link (Enter adds, Shift+Enter for a new line). Double-click an item to change it; " +
                   "right-click for Open, Copy, Pin, Mark done, Refresh preview and Delete. It all goes both ways, straight away. " +
                   "Uses the Home Assistant connection - its token must belong to an administrator. Items and pictures are kept on this PC too, " +
                   "so it shows offline; changes made offline are sent when Home Assistant is back. Width and height are set below.");
            p.ListButton("Sync now", delegate { VDrop.SyncNow(); });
        }

        /// <summary>A Home Assistant entity: its id in a box, and "Find..." to pick it from the list Home Assistant has.</summary>
        static void EntityPickRow(SettingsWindow.SettingsPage p, string label, Func<string> get, Action<string> set)
        {
            var box = new TextBox { Text = get() ?? "", Width = 260 };
            var find = new Button { Content = "Find…", Margin = new Thickness(6, 0, 0, 0) };
            Action commit = delegate
            {
                if (p.Loading) return;
                string v = box.Text.Trim();
                if (v == (get() ?? "")) return;
                set(v);
                p.Changed();
            };
            box.LostFocus += delegate { commit(); };
            box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
            find.Click += delegate
            {
                if (!HaClient.Configured)
                {
                    Questions.Tell(p.Window, "Set the Home Assistant address and token on the Plugins page first.", "Home Assistant");
                    return;
                }
                List<string> picked = HaEntityPickerWindow.Pick(p.Window, false);
                if (picked == null || picked.Count == 0) return;
                box.Text = picked[0];
                commit();
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(box);
            line.Children.Add(find);
            p.Row(label, line);
        }

        static void EditEntities(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            Func<Window, HaEntityRef, bool> edit = delegate (Window owner, HaEntityRef r)
            {
                var fields = new List<FormField>
                {
                    new FormField("Name (empty = Home Assistant's)", r.Label),
                    new FormField("Unit (empty = its own)", r.Unit),
                    new FormField("Decimals (empty = automatic)", r.Decimals < 0 ? "" : r.Decimals.ToString(CultureInfo.InvariantCulture)),
                    FormField.Check("Clicking it operates it (lights, switches, scenes ...)", r.Clickable),
                    FormField.Combo("Graph next to the value", r.GraphStyle ?? "None", new[] { "None", "Meter", "Bars", "Line", "Dots" }),
                    FormField.Combo("Graph position", r.GraphAlign ?? "Right", new[] { "Left", "Right" }),
                    new FormField("Graph: empty at", r.GraphMin.ToString(CultureInfo.InvariantCulture)),
                    new FormField("Graph: full at", r.GraphMax.ToString(CultureInfo.InvariantCulture)) { Hint = "Numbers are scaled between these two values (for example 0 and 5000 for a power sensor in W). On/off entities draw as full/empty." },
                    new FormField("Graph width", r.GraphWidth.ToString(CultureInfo.InvariantCulture)),
                    new FormField("Graph height (% of the row)", r.GraphHeightPercent.ToString(CultureInfo.InvariantCulture)),
                    new FormField("Graph colour (#89B4FA, @main, @secondary or @text; empty = module colour)", r.GraphColor ?? ""),
                    new FormField("Warning colour (#F38BA8, @main, @secondary or @text; empty = Secondary Accent)", r.WarnColor ?? ""),
                    FormField.Check("Change colour past threshold", r.WarnOn),
                    new FormField("Threshold (in the entity's unit)", r.WarnAt.ToString(CultureInfo.InvariantCulture))
                    {
                        Hint = r.WarnAuto ? "Automatic: 70 % of the graph range (empty at - full at). Type a number to set your own."
                                          : "Your own threshold. Empty it to go back to automatic (70 % of the graph range)."
                    },
                    FormField.Combo("Bubble Card button (Switch = click toggles, Slider = drag for brightness / speed / position / value / volume, State = shows it)",
                                    string.IsNullOrEmpty(r.BubbleType) ? "Auto" : r.BubbleType, new[] { "Auto", "Switch", "Slider", "State" })
                };
                if (!FormWindow.Ask(owner, r.EntityId, fields)) return false;
                r.Label = fields[0].Value;
                r.Unit = fields[1].Value;
                int d;
                r.Decimals = int.TryParse(fields[2].Value, out d) ? Math.Max(0, Math.Min(4, d)) : -1;
                r.Clickable = fields[3].Checked;
                r.GraphStyle = fields[4].Value;
                r.GraphAlign = fields[5].Value;
                double gmin, gmax; int gw, gh;
                r.GraphMin = double.TryParse(fields[6].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out gmin) ? gmin : 0;
                r.GraphMax = double.TryParse(fields[7].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out gmax) ? gmax : 100;
                r.GraphWidth = int.TryParse(fields[8].Value, out gw) ? Math.Max(20, Math.Min(400, gw)) : 60;
                r.GraphHeightPercent = int.TryParse(fields[9].Value, out gh) ? Math.Max(10, Math.Min(100, gh)) : 60;
                r.GraphColor = fields[10].Value.Trim();
                r.WarnColor = fields[11].Value.Trim();
                r.WarnOn = fields[12].Checked;
                double wa;
                string wt = (fields[13].Value ?? "").Trim();
                if (wt.Length == 0) r.WarnAuto = true;                       // empty: back to automatic
                else if (double.TryParse(wt, NumberStyles.Float, CultureInfo.InvariantCulture, out wa) && Math.Abs(wa - r.WarnAt) > 1e-9) { r.WarnAt = wa; r.WarnAuto = false; }
                r.SyncWarn();                                                // follows a changed graph range while automatic
                r.BubbleType = fields[14].Value;
                r.WarnBelow = false;
                return true;
            };
            var dlg = new ListEditorWindow<HaEntityRef>("Home Assistant entities",
                "Shown in this order. Double-click one to change its name, unit, decimals or to add a graph (meter, bars or line).",
                HaView.Of(cfg).Entities,
                delegate (HaEntityRef r) { return (r.Label.Length > 0 ? r.Label + "   -   " : "") + r.EntityId; },
                edit, delegate { p.Changed(); });
            dlg.Owner = p.Window;
            dlg.AddAction("Add from Home Assistant…", delegate (ListEditorWindow<HaEntityRef> w)
            {
                if (!HaClient.Configured) { Questions.Tell(w, "Set the Home Assistant address and token on the Plugins page of Settings first.", "Home Assistant"); return; }
                List<string> picked = HaEntityPickerWindow.Pick(w, true);
                if (picked == null) return;
                foreach (string id in picked)
                {
                    bool have = false;
                    foreach (var e in w.Items) if (string.Equals(e.EntityId, id, StringComparison.OrdinalIgnoreCase)) have = true;
                    if (!have) w.Items.Add(HaEntityRef.Fresh(id));
                }
                w.Changed(w.Items.Count - 1);
            });
            dlg.AddAction("Add by entity id…", delegate (ListEditorWindow<HaEntityRef> w)
            {
                string id = PromptWindow.Ask(w, "Add entity", "Entity id, for example sensor.sigen_plant_pv_power", "");
                if (string.IsNullOrWhiteSpace(id)) return;
                w.Items.Add(HaEntityRef.Fresh(id.Trim()));
                w.Changed(w.Items.Count - 1);
            });
            dlg.ShowDialog();
        }

        /// <summary>Climate module zones: fan entities (AirTouch zones), each with the name to show.</summary>
        static void EditZones(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            Func<Window, ClimateZone, bool> edit = delegate (Window owner, ClimateZone z)
            {
                var fields = new List<FormField>
                {
                    new FormField("Name (empty = the entity's own name)", z.Name),
                    new FormField("Fan entity", z.Entity) { Hint = "For example fan.lounge_zone" }
                };
                if (!FormWindow.Ask(owner, "Zone", fields)) return false;
                z.Name = fields[0].Value.Trim();
                z.Entity = fields[1].Value.Trim();
                return z.Entity.Length > 0;
            };
            var dlg = new ListEditorWindow<ClimateZone>("Climate zones",
                "Each zone is a Home Assistant fan entity, shown as a slider from 0% (off) to 100%. Shown in this order.",
                HaView.Of(cfg).Zones,
                delegate (ClimateZone z) { return (z.Name.Length > 0 ? z.Name + "   -   " : "") + z.Entity; },
                edit, delegate { p.Changed(); });
            dlg.Owner = p.Window;
            dlg.AddAction("Add from Home Assistant…", delegate (ListEditorWindow<ClimateZone> w)
            {
                if (!HaClient.Configured) { Questions.Tell(w, "Set the Home Assistant address and token on the Plugins page of Settings first.", "Home Assistant"); return; }
                List<string> picked = HaEntityPickerWindow.Pick(w, true);
                if (picked == null) return;
                foreach (string id in picked)
                    if (!w.Items.Exists(delegate (ClimateZone x) { return string.Equals(x.Entity, id, StringComparison.OrdinalIgnoreCase); }))
                        w.Items.Add(new ClimateZone { Entity = id });
                w.Changed(w.Items.Count - 1);
            });
            dlg.AddAction("Add by entity id…", delegate (ListEditorWindow<ClimateZone> w)
            {
                var z = new ClimateZone();
                if (!edit(w, z)) return;
                w.Items.Add(z);
                w.Changed(w.Items.Count - 1);
            });
            dlg.ShowDialog();
        }
    }
}
