using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The Home Assistant module with "Bubble Card buttons" on: each entity is a Bubble Card button (see BubbleRow).
    ///  - Switch: click the card to toggle; on = the accent behind the whole card (a light: its own colour at 70 %).
    ///  - Slider: drag along the card - light brightness, fan speed, cover position, a number / input_number, media volume; the icon
    ///    toggles it.
    ///  - State: just shows it.
    ///  Auto picks Slider for lights, fans, covers, numbers and media players, Switch for switches, booleans, locks, scenes, scripts,
    ///  buttons and automations, State for the rest.
    /// In a widget: one per row (List) or side by side, wrapping (Row). On a bar: side by side, as tall as the bar.
    /// </summary>
    public sealed class HaBubbleModule : TilesModule
    {
        readonly Panel _host;
        readonly List<Item> _items = new List<Item>();

        sealed class Item
        {
            public HaEntityRef Ref;
            public BubbleRow Row;
            public string Type;            // Switch / Slider / State
            public double Pend = -1;       // a slider value just set, until Home Assistant has it
            public DateTime PendAt;
            public double Preview = -1;
        }

        public override int RowCount { get { return Bar.IsWidget ? 0 : 1; } }

        public HaBubbleModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg)
        {
            bool list = Bar.IsWidget && Cfg.TileLayout == "List";
            if (list) _host = new StackPanel();
            else if (Bar.IsWidget) _host = new WrapPanel();
            else _host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Child = _host;
            Build();
        }

        static string Auto(string id)
        {
            switch (HaClient.Domain(id))
            {
                case "light": case "fan": case "cover": case "input_number": case "number": case "media_player": return "Slider";
                case "switch": case "input_boolean": case "lock": case "scene": case "script": case "button": case "input_button": case "automation": case "group": case "humidifier": case "siren":
                    return "Switch";
                default: return "State";
            }
        }

        protected override void BuildTiles()
        {
            _host.Children.Clear();
            _items.Clear();
            bool list = Bar.IsWidget && Cfg.TileLayout == "List";
            double barH = Math.Max(24, Bar.HostHeight - 4);
            foreach (HaEntityRef r in HaView.Of(Cfg).Entities)
            {
                var it = new Item { Ref = r };
                it.Type = string.IsNullOrEmpty(r.BubbleType) || r.BubbleType == "Auto" ? Auto(r.EntityId) : r.BubbleType;
                it.Row = Bar.IsWidget ? new BubbleRow(T) : new BubbleRow(T, barH);
                double k = it.Row.K;
                if (Bar.IsWidget)
                {
                    it.Row.Margin = list ? new Thickness(0, 0, 0, 8 * k) : new Thickness(0, 0, 8 * k, 8 * k);
                    if (!list) it.Row.Width = Math.Round(Math.Max(120, HaView.Of(Cfg).BubbleWidth) * k);
                }
                else
                {
                    it.Row.Margin = new Thickness(3, 0, 3, 0);
                    it.Row.MinWidth = it.Type == "Slider" ? Math.Round(150 * k) : 0;
                }
                Item cap = it;
                if (it.Type == "Switch" && r.Clickable)
                    it.Row.OnClick(delegate { Toggle(cap); });
                if (it.Type == "Slider")
                {
                    it.Row.MakeSlider(delegate (double f) { cap.Preview = f; Draw(cap); },
                                      delegate (double f) { cap.Preview = -1; SetValue(cap, f); Draw(cap); });
                    if (r.Clickable) it.Row.OnIconClick(delegate { Toggle(cap); });
                    it.Row.MouseWheel += delegate (object s, MouseWheelEventArgs e)
                    {
                        e.Handled = true;
                        double cur = Fraction(cap, HaClient.Get(cap.Ref.EntityId));
                        if (cur < 0) cur = 0;
                        SetValue(cap, Math.Max(0, Math.Min(1, cur + (e.Delta > 0 ? 0.05 : -0.05))));
                        Draw(cap);
                    };
                }
                _items.Add(it);
                _host.Children.Add(it.Row);
            }
            // TilesModule wants a tile to show its messages in: an empty one, out of sight
            AddTile(false, false, null);
            if (Tiles.Count > 0) Tiles[0].Root.Visibility = Visibility.Collapsed;
        }

        void Toggle(Item it)
        {
            HaState s = HaClient.Get(it.Ref.EntityId);
            var svc = HaClient.ClickService(it.Ref.EntityId, s == null ? "" : s.State);
            if (svc.HasValue) HaClient.CallService(svc.Value.Key, svc.Value.Value, it.Ref.EntityId);
        }

        /// <summary>The entity's value as 0 - 1 (the one just set while Home Assistant catches up); -1 = no value.</summary>
        double Fraction(Item it, HaState s)
        {
            if (it.Pend >= 0)
            {
                if ((DateTime.UtcNow - it.PendAt).TotalSeconds < 8) return it.Pend;
                it.Pend = -1;
            }
            if (s == null || s.Unavailable) return -1;
            double v;
            switch (HaClient.Domain(it.Ref.EntityId))
            {
                case "light":
                    if (s.State != "on") return 0;
                    return s.AttrNum("brightness", out v) ? v / 255.0 : 1;
                case "fan":
                    if (s.State != "on") return 0;
                    return s.AttrNum("percentage", out v) ? v / 100.0 : 1;
                case "cover":
                    if (s.AttrNum("current_position", out v)) return v / 100.0;
                    return s.State == "open" ? 1 : 0;
                case "media_player":
                    return s.AttrNum("volume_level", out v) ? v : 0;
                case "input_number":
                case "number":
                    {
                        double lo, hi;
                        if (!s.TryNumber(out v)) return -1;
                        if (!s.AttrNum("min", out lo)) lo = 0;
                        if (!s.AttrNum("max", out hi)) hi = 100;
                        return hi > lo ? Math.Max(0, Math.Min(1, (v - lo) / (hi - lo))) : 0;
                    }
                default: return -1;
            }
        }

        void SetValue(Item it, double f)
        {
            f = Math.Max(0, Math.Min(1, f));
            it.Pend = f; it.PendAt = DateTime.UtcNow;
            string id = it.Ref.EntityId;
            int pct = (int)Math.Round(f * 100);
            HaState s = HaClient.Get(id);
            switch (HaClient.Domain(id))
            {
                case "light":
                    if (pct == 0) HaClient.CallService("light", "turn_off", id);
                    else HaClient.CallService("light", "turn_on", id, new Dictionary<string, object> { { "brightness_pct", pct } });
                    break;
                case "fan":
                    if (pct == 0) HaClient.CallService("fan", "turn_off", id);
                    else HaClient.CallService("fan", "set_percentage", id, new Dictionary<string, object> { { "percentage", pct } });
                    break;
                case "cover": HaClient.CallService("cover", "set_cover_position", id, new Dictionary<string, object> { { "position", pct } }); break;
                case "media_player": HaClient.CallService("media_player", "volume_set", id, new Dictionary<string, object> { { "volume_level", Math.Round(f, 2) } }); break;
                case "input_number":
                case "number":
                    {
                        double lo, hi, step;
                        if (s == null || !s.AttrNum("min", out lo)) lo = 0;
                        if (s == null || !s.AttrNum("max", out hi)) hi = 100;
                        if (s == null || !s.AttrNum("step", out step) || step <= 0) step = 1;
                        double v = lo + f * (hi - lo);
                        v = Math.Round(v / step) * step;
                        HaClient.CallService(HaClient.Domain(id), "set_value", id, new Dictionary<string, object> { { "value", v } });
                        break;
                    }
            }
        }

        static bool IsOn(HaState s)
        {
            if (s == null || s.Unavailable) return false;
            switch (s.State)
            {
                case "on": case "open": case "opening": case "unlocked": case "playing": case "home": case "heat": case "cool": case "heat_cool": case "auto": case "dry": case "fan_only":
                    return true;
                default: return false;
            }
        }

        /// <summary>A light's own colour (rgb_color), or null.</summary>
        static Color? LightColor(HaState s)
        {
            if (s == null || HaClient.Domain(s.EntityId) != "light" || s.State != "on") return null;
            string raw = s.AttrText("rgb_color");
            if (raw.Length < 5) return null;
            string[] p = raw.Trim('[', ']', ' ').Split(',');
            if (p.Length < 3) return null;
            byte r, g, b;
            if (byte.TryParse(p[0].Trim(), out r) && byte.TryParse(p[1].Trim(), out g) && byte.TryParse(p[2].Trim(), out b)) return Color.FromRgb(r, g, b);
            return null;
        }

        /// <summary>An icon for the entity: drawn ones (fan, thermometer, droplet, climate modes, blinds) or a Segoe glyph.</summary>
        FrameworkElement Icon(HaState s, string id, double size)
        {
            string d = HaClient.Domain(id);
            Func<string, FrameworkElement> G = delegate (string glyph)
            {
                return new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = size * 0.9, Foreground = T.Fg };
            };
            string unit = s != null ? s.Unit : "";
            string dc = s != null ? s.AttrText("device_class") : "";
            switch (d)
            {
                case "light": return G("\uEA80");
                case "fan": return ClimateIcons.Make("fan", T.Fg, size);
                case "climate": return ClimateIcons.Make(ClimateCtl.ModeIcon(s != null ? s.State : ""), T.Fg, size);
                case "cover": return ClimateIcons.Make("blinds", T.Fg, size);
                case "lock": return G(s != null && s.State == "unlocked" ? "\uE785" : "\uE72E");
                case "media_player": return G("\uE768");
                case "scene": case "script": case "automation": return G("\uE945");
                case "person": case "device_tracker": return G("\uE77B");
                case "switch": case "input_boolean": case "button": case "input_button": return G("\uE7E8");
            }
            if (dc == "temperature" || unit == "°C" || unit == "°F") return ClimateIcons.Make("thermo", T.Fg, size);
            if (dc == "humidity") return ClimateIcons.Make("dry", T.Fg, size);
            if (dc == "power" || dc == "energy" || unit == "W" || unit == "kW" || unit == "kWh") return G("\uE945");
            return G("\uE80F");
        }

        public override void Update()
        {
            if (HaView.Of(Cfg).Entities.Count == 0 || !HaClient.Configured)
            {
                Tiles[0].Root.Visibility = Visibility.Visible;
                SetText(Tiles[0], HaView.Of(Cfg).Entities.Count == 0 ? "Home Assistant: add entities in Settings" : "Home Assistant: set the address and token in Settings > Plugins", T.FgDim, null);
                var holder = Tiles[0].Root.Parent as Panel;
                if (holder != _host) { if (holder != null) holder.Children.Remove(Tiles[0].Root); _host.Children.Add(Tiles[0].Root); }
                return;
            }
            foreach (Item it in _items) if (!it.Row.Dragging) Draw(it);
        }

        void Draw(Item it)
        {
            HaEntityRef r = it.Ref;
            HaState s = HaClient.Get(r.EntityId);
            BubbleRow row = it.Row;
            string name = r.Label.Length > 0 ? r.Label : (s != null && s.Name.Length > 0 ? s.Name : r.EntityId);
            bool on = IsOn(s);
            row.Opacity = s != null && s.Unavailable ? 0.5 : 1;
            row.SetIcon(Icon(s, r.EntityId, row.IconSize), on || it.Type == "State");
            string state = s == null ? "…" : HomeAssistantModule.Format(s, r);
            Color? lc = LightColor(s);
            if (it.Type == "Slider")
            {
                double f = it.Preview >= 0 ? it.Preview : Fraction(it, s);
                if (f >= 0)
                {
                    string d = HaClient.Domain(r.EntityId);
                    if (d == "input_number" || d == "number") { if (it.Preview >= 0 && s != null) state = Number(s, f); }
                    else state = f > 0 ? Math.Round(f * 100) + " %" : d == "cover" ? "Closed" : d == "media_player" ? "0 %" : "Off";
                }
                Brush fill = lc.HasValue ? new SolidColorBrush(lc.Value) : (Brush)T.MainAccent;
                row.SetFill(Math.Max(0, f), fill, lc.HasValue ? 0.7 : 0.9);
                row.SetOverlay(null, 0);
            }
            else if (it.Type == "Switch")
            {
                // Bubble: a switch that is on gets the accent (a light its own colour at 70 %) behind the whole card
                if (on) row.SetOverlay(lc.HasValue ? new SolidColorBrush(lc.Value) : (Brush)T.MainAccent, lc.HasValue ? 0.7 : 0.9);
                else row.SetOverlay(null, 0);
            }
            else row.SetOverlay(null, 0);
            bool tall = !Bar.IsWidget && row.H < 40 * row.K;
            if (tall) row.SetText(name + "  ·  " + state, null);
            else row.SetText(name, state);
            row.ToolTip = (s != null && s.Name.Length > 0 ? s.Name + "\n" : "") + r.EntityId + (s != null ? ": " + s.State + (s.Unit.Length > 0 ? " " + s.Unit : "") : "") +
                          (it.Type == "Switch" && r.Clickable ? "\nClick to toggle" : it.Type == "Slider" ? "\nDrag along it to set" + (r.Clickable ? " · click the icon to toggle" : "") : "");
        }

        static string Number(HaState s, double f)
        {
            double lo, hi;
            if (!s.AttrNum("min", out lo)) lo = 0;
            if (!s.AttrNum("max", out hi)) hi = 100;
            double v = lo + f * (hi - lo);
            return v.ToString("0.#", CultureInfo.CurrentCulture) + (s.Unit.Length > 0 ? " " + s.Unit : "");
        }
    }
}
