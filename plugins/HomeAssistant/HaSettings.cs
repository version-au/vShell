using System;
using System.Collections.Generic;

namespace vShell.Core
{
    /// <summary>The Home Assistant connection, the notification rules and vDrop's notifications - kept under Integrations["homeassistant"].</summary>
    public sealed class HaSettings
    {
        readonly SettingsBag _b;
        public HaSettings(BarSettings s) { _b = s.Integration("homeassistant"); }
        public static HaSettings Of(BarSettings s) { return new HaSettings(s); }

        /// <summary>Address such as http://homeassistant.local:8123</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", value ?? ""); } }
        /// <summary>Long-lived access token, stored encrypted for the current Windows user (see Secret).</summary>
        public string Token { get { return _b.Get("Token", ""); } set { _b.Set("Token", Secret.Protect(value ?? "")); } }
        public bool IgnoreCert { get { return _b.Get("IgnoreCert", false); } set { _b.Set("IgnoreCert", value); } }
        /// <summary>Seconds between updates.</summary>
        public int PollSeconds { get { return Math.Max(2, Math.Min(120, _b.Get("PollSeconds", 5))); } set { _b.Set("PollSeconds", value); } }
        /// <summary>The run box keyword (without the !): !ha words shows the palette's Home Assistant commands for those words.</summary>
        public string Bang
        {
            get { string v = _b.Get("Bang", "ha").Trim().TrimStart('!').ToLowerInvariant(); return v.Length == 0 ? "ha" : v; }
            set { _b.Set("Bang", value ?? ""); }
        }
        /// <summary>The notification rules - the same list every time (the rules editor changes it in place).</summary>
        public List<HaNotifyRule> Rules
        {
            get
            {
                List<HaNotifyRule> l = _b.Live("Rules", delegate { return new List<HaNotifyRule>(); });
                l.RemoveAll(delegate (HaNotifyRule r) { return r == null; });
                return l;
            }
        }
        /// <summary>Show the notifications your network's apps send to the vDrop add-on (Gotify-compatible) - the unread ones since this
        /// PC was last connected, then live; reading one here reads it in vDrop too.</summary>
        public bool VDropNotifications { get { return _b.Get("VDropNotifications", true); } set { _b.Set("VDropNotifications", value); } }
    }

    /// <summary>
    /// A Home Assistant module's settings - kept in its ModuleConfig.Extra: the Entities module (entities, Bubble buttons), Climate
    /// (the entity, zones), Power flow (the sensors and colours), and vDrop's feed on a Notes module (its tab and list).
    /// </summary>
    public sealed class HaView
    {
        readonly SettingsBag _b;
        public HaView(ModuleConfig cfg) { _b = cfg.Bag; }
        public static HaView Of(ModuleConfig cfg) { return new HaView(cfg); }

        static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }

        // ---- Entities ----
        /// <summary>The entities shown - the same list every time (the editor changes it in place).</summary>
        public List<HaEntityRef> Entities
        {
            get
            {
                List<HaEntityRef> l = _b.Live("Entities", delegate { return new List<HaEntityRef>(); });
                l.RemoveAll(delegate (HaEntityRef h) { return h == null || string.IsNullOrWhiteSpace(h.EntityId); });
                foreach (HaEntityRef h in l)
                {
                    h.EntityId = h.EntityId.Trim();
                    h.GraphColor = ThemeSlots.Migrate(h.GraphColor);
                    h.WarnBelow = false;
                    if (h.Label == null) h.Label = "";
                    if (h.Unit == null) h.Unit = "";
                    h.Decimals = Clamp(h.Decimals, -1, 4);
                    if (h.BubbleType != "Switch" && h.BubbleType != "Slider" && h.BubbleType != "State") h.BubbleType = "Auto";
                }
                return l;
            }
        }
        /// <summary>Bubble Card style buttons instead of plain readouts.</summary>
        public bool Bubble { get { return _b.Get("Bubble", false); } set { _b.Set("Bubble", value); } }
        /// <summary>Bubble buttons side by side in a widget (Row): each one's width.</summary>
        public int BubbleWidth { get { return Clamp(_b.Get("BubbleWidth", 200), 120, 600); } set { _b.Set("BubbleWidth", value); } }

        // ---- Climate ----
        /// <summary>The Home Assistant climate entity (the air conditioner).</summary>
        public string ClimateEntity { get { return _b.Get("ClimateEntity", ""); } set { _b.Set("ClimateEntity", value ?? ""); } }
        /// <summary>Zones - fan entities, each set from 0 (off) to 10. The same list every time.</summary>
        public List<ClimateZone> Zones
        {
            get
            {
                List<ClimateZone> l = _b.Live("Zones", delegate { return new List<ClimateZone>(); });
                l.RemoveAll(delegate (ClimateZone z) { return z == null || string.IsNullOrWhiteSpace(z.Entity); });
                foreach (ClimateZone z in l) if (z.Name == null) z.Name = "";
                return l;
            }
        }
        /// <summary>On a bar: show the target temperature next to the room temperature.</summary>
        public bool ShowTarget { get { return _b.Get("ShowTarget", true); } set { _b.Set("ShowTarget", value); } }
        /// <summary>Show the zones (popup / widget).</summary>
        public bool ShowZones { get { return _b.Get("ShowZones", true); } set { _b.Set("ShowZones", value); } }

        // ---- Power flow: Home Assistant power sensors (W or kW). Import / export are both 0 or more ----
        public string Solar { get { return _b.Get("Solar", ""); } set { _b.Set("Solar", value ?? ""); } }
        public string GridImport { get { return _b.Get("GridImport", ""); } set { _b.Set("GridImport", value ?? ""); } }
        public string GridExport { get { return _b.Get("GridExport", ""); } set { _b.Set("GridExport", value ?? ""); } }
        /// <summary>Battery power: positive = charging (BatteryInvert turns that round).</summary>
        public string Battery { get { return _b.Get("Battery", ""); } set { _b.Set("Battery", value ?? ""); } }
        public bool BatteryInvert { get { return _b.Get("BatteryInvert", false); } set { _b.Set("BatteryInvert", value); } }
        public string BatterySoc { get { return _b.Get("BatterySoc", ""); } set { _b.Set("BatterySoc", value ?? ""); } }
        /// <summary>Optional: the home's use; empty = worked out from the others.</summary>
        public string Home { get { return _b.Get("Home", ""); } set { _b.Set("Home", value ?? ""); } }
        /// <summary>Colours (same format as Color); empty = Secondary Accent (solar), Main Accent (grid), Text (battery).</summary>
        public string SolarColor { get { return ThemeSlots.Migrate(_b.Get("SolarColor", "")); } set { _b.Set("SolarColor", value ?? ""); } }
        public string GridColor { get { return ThemeSlots.Migrate(_b.Get("GridColor", "")); } set { _b.Set("GridColor", value ?? ""); } }
        public string BatteryColor { get { return ThemeSlots.Migrate(_b.Get("BatteryColor", "")); } set { _b.Set("BatteryColor", value ?? ""); } }

        // ---- vDrop's feed (a Notes module stored in vDrop) ----
        /// <summary>The tab: "" all, "links", "media", "notes" or "archived".</summary>
        public string VdKind
        {
            get
            {
                if (_b.Get("VdStatus", "open") == "done") return "archived";      // 0.38.24: Done became the Archived tab
                string v = _b.Get("VdKind", "");
                return v == "links" || v == "media" || v == "notes" || v == "archived" ? v : "";
            }
            set { _b.Set("VdKind", value ?? ""); }
        }
        /// <summary>The list: "open" (inbox), "pinned" or "archived".</summary>
        public string VdStatus
        {
            get { string v = _b.Get("VdStatus", "open"); return v == "pinned" || v == "archived" ? v : "open"; }      // ("all" / Everything went in 0.38.25)
            set { _b.Set("VdStatus", value ?? "open"); }
        }
    }
}
