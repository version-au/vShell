using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vShell.Core
{
    /// <summary>A freely movable box on the desktop that holds one or more modules.</summary>
    public sealed class WidgetSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Widget";
        public bool Enabled { get; set; } = true;
        /// <summary>Monitor device name the widget is on. Empty = primary.</summary>
        public string Monitor { get; set; } = "";
        /// <summary>The monitor itself (MonitorInfo.Key): the widget goes back to it when it's reconnected, whatever DISPLAYn it gets.</summary>
        public string MonitorKey { get; set; } = "";
        /// <summary>Position of the widget box's top-left corner (inside the outline; the outline and shadow are left out), in device
        /// pixels from the top-left of its monitor. Before 0.23 this was the window's corner (shadow margin included).</summary>
        public int OffsetX { get; set; } = 80;
        public int OffsetY { get; set; } = 80;
        /// <summary>"Vertical" (modules stacked), "Horizontal" (side by side) or "Grid" (GridColumns across).</summary>
        public string Direction { get; set; } = "Vertical";
        /// <summary>Grid arrangement: modules per row.</summary>
        public int GridColumns { get; set; } = 2;
        /// <summary>Each module in its own card (a slightly lighter rounded box) inside the widget.</summary>
        public bool CardPerModule { get; set; } = false;
        /// <summary>One module at a time with a tab strip naming them; the widget is as big as its tallest module.</summary>
        public bool ShowTabs { get; set; } = false;
        /// <summary>The tab last shown (index into Modules).</summary>
        public int ActiveTab { get; set; } = 0;
        /// <summary>A header row: an icon (from the first module), the widget's name and a collapse button.</summary>
        public bool ShowHeader { get; set; } = false;
        /// <summary>The header's name colour: empty = the theme's text colour, a theme slot or "#AARRGGBB".</summary>
        public string HeaderTextColor { get; set; } = "";
        /// <summary>The header has a collapse arrow and a click on it collapses / expands the widget.</summary>
        public bool Collapsible { get; set; } = false;
        /// <summary>Collapsed to its header (only with ShowHeader and Collapsible).</summary>
        public bool Collapsed { get; set; } = false;
        /// <summary>Shown collapsed to its header right now.</summary>
        [JsonIgnore] public bool IsCollapsed { get { return ShowHeader && Collapsible && Collapsed; } }
        /// <summary>A 4 px accent stripe: "None", "Top" or "Left".</summary>
        public string AccentEdge { get; set; } = "None";
        /// <summary>This widget's own accent: empty = the theme's Main Accent; a theme slot or "#AARRGGBB". Used by the accent edge, the header
        /// icon and as the Main Accent of the widget's graphs.</summary>
        public string AccentColor { get; set; } = "";
        /// <summary>"Comfortable" or "Compact" (about 60 % of the padding, spacing and row height, slightly smaller text).</summary>
        public string Density { get; set; } = "Comfortable";
        /// <summary>Keep the box at its current Width / Height whatever the style, theme or font does (set from the size it had when ticked).</summary>
        public bool KeepSize { get; set; } = false;
        /// <summary>What this widget is snapped to across (left / right) and up / down. null = placed freely. Kept on drop; when a widget it
        /// is linked to changes size (or the snap gap changes) it moves to keep the gap.</summary>
        public SnapLink LinkX { get; set; }
        public SnapLink LinkY { get; set; }
        /// <summary>Height of each module row in DIPs (what a bar's height is to its modules: clocks and graphs size themselves from it).</summary>
        public int ModuleHeight { get; set; } = 40;
        /// <summary>Fixed widget box width in DIPs (the outline is outside it); 0 = size to the modules.</summary>
        public int Width { get; set; } = 0;
        /// <summary>Fixed box height in DIPs (the outline is outside it); 0 = follow the content. Content taller than this scrolls.</summary>
        public int Height { get; set; } = 0;
        public int Padding { get; set; } = 8;
        /// <summary>Gap between modules.</summary>
        public int Spacing { get; set; } = 4;
        public double FontSize { get; set; } = 12;
        public double CornerRadius { get; set; } = 13;
        /// <summary>Background opacity in percent.</summary>
        public int Opacity { get; set; } = 90;
        /// <summary>true = use the theme's background transparency (same as the bars); false = this widget's own Opacity applies.</summary>
        public bool UseThemeOpacity { get; set; } = true;
        /// <summary>Background blur for this one: "Theme" (the Appearance setting), "Off", "Blur" or "Acrylic" (Frosted).</summary>
        public string Blur { get; set; } = "Theme";
        public bool ShowBorder { get; set; } = true;
        /// <summary>Border thickness in DIPs (the border uses the Main Accent colour unless set).</summary>
        public double BorderThickness { get; set; } = 3;
        /// <summary>Border opacity in percent (100 = solid colour).</summary>
        public int BorderOpacity { get; set; } = 65;
        /// <summary>Outline colour: empty = Main Accent, a theme slot ("@secondary", "@text") or "#AARRGGBB".</summary>
        public string BorderColor { get; set; } = "";
        /// <summary>Glass edge: a light top line, a darker bottom line and a faint sheen - "Off", "Subtle" or "Strong".</summary>
        public string GlassEdge { get; set; } = "Off";
        public bool ShowShadow { get; set; } = true;
        /// <summary>false = sits on the desktop behind your windows; true = stays above them.</summary>
        public bool AlwaysOnTop { get; set; } = false;
        /// <summary>true = cannot be dragged.</summary>
        public bool Locked { get; set; } = false;
        /// <summary>Slide away off the screen edge it is snapped to when the mouse leaves; the mouse against that edge brings it back.</summary>
        public bool AutoHide { get; set; } = false;
        /// <summary>Which way it slides: "Left", "Right", "Up" or "Down" (snapped into a corner); empty / not snapped that way = the edge it is snapped to.</summary>
        public string AutoHideWay { get; set; } = "";
        /// <summary>"Slide", "Fade" or "Slide and fade".</summary>
        public string AutoHideAnim { get; set; } = "Slide";
        /// <summary>How long the slide takes (ms).</summary>
        public int AutoHideMs { get; set; } = 300;
        /// <summary>Coming back: None, Soft or Springy (as the taskbar's line).</summary>
        public string AutoHideBounce { get; set; } = "Soft";
        /// <summary>How long after the mouse leaves before it slides away (ms).</summary>
        public int AutoHideDelay { get; set; } = 600;
        /// <summary>While it is away: a thin accent glow along the screen edge where it is (where to push the mouse).</summary>
        public bool AutoHideGlow { get; set; } = true;
        /// <summary>Takes its look (corners, outline, shadow, padding) from the widget style; off = its own look.</summary>
        public bool FollowStyle { get; set; } = true;
        public List<ModuleConfig> Modules { get; set; } = new List<ModuleConfig>();

        public void Normalize()
        {
            // Build 2: no "Keep the box size" - a widget that had it goes back to sizing itself to its modules
            if (KeepSize) { KeepSize = false; Width = 0; Height = 0; }
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(Name)) Name = "Widget";
            if (Array.IndexOf(BarSettings.OwnBlurModes, Blur) < 0) Blur = "Theme";
            if (GlassEdge != "Subtle" && GlassEdge != "Strong") GlassEdge = "Off";
            if (Monitor == null) Monitor = "";
            if (MonitorKey == null) MonitorKey = "";
            if (Direction != "Horizontal" && Direction != "Grid") Direction = "Vertical";
            if (Array.IndexOf(ModuleTypes.WidgetHideAnims, AutoHideAnim) < 0) AutoHideAnim = "Slide";
            if (Array.IndexOf(ModuleTypes.TaskAnimBounces, AutoHideBounce) < 0) AutoHideBounce = "Soft";
            if (AutoHideWay == null || (AutoHideWay.Length > 0 && Array.IndexOf(ModuleTypes.WidgetHideWays, AutoHideWay) < 0)) AutoHideWay = "";
            AutoHideMs = Math.Max(60, Math.Min(1000, AutoHideMs));
            AutoHideDelay = Math.Max(0, Math.Min(5000, AutoHideDelay));
            GridColumns = Math.Max(1, Math.Min(8, GridColumns));
            if (Modules != null) ActiveTab = Math.Max(0, Math.Min(Math.Max(0, Modules.Count - 1), ActiveTab));
            if (AccentEdge != "Top" && AccentEdge != "Left") AccentEdge = "None";
            if (AccentColor == null) AccentColor = "";
            if (HeaderTextColor == null) HeaderTextColor = "";
            HeaderTextColor = ThemeSlots.Migrate(HeaderTextColor);
            AccentColor = ThemeSlots.Migrate(AccentColor);
            if (Density != "Compact") Density = "Comfortable";
            if (LinkX != null && !LinkX.Valid(true)) LinkX = null;
            if (LinkY != null && !LinkY.Valid(false)) LinkY = null;
            if (LinkX != null && LinkX.To == Id) LinkX = null;
            if (LinkY != null && LinkY.To == Id) LinkY = null;
            BorderColor = ThemeSlots.Migrate(BorderColor);
            ModuleHeight = Math.Max(20, Math.Min(200, ModuleHeight));
            if (Width != 0) Width = Math.Max(80, Math.Min(1600, Width));
            if (Height != 0) Height = Math.Max(40, Math.Min(1600, Height));
            Padding = Math.Max(0, Math.Min(40, Padding));
            Spacing = Math.Max(0, Math.Min(40, Spacing));
            if (double.IsNaN(FontSize)) FontSize = 12;
            if (double.IsNaN(CornerRadius)) CornerRadius = 12;
            FontSize = Math.Max(8, Math.Min(32, FontSize));
            CornerRadius = Math.Max(0, Math.Min(50, CornerRadius));
            Opacity = Math.Max(0, Math.Min(100, Opacity));
            BorderOpacity = Math.Max(0, Math.Min(100, BorderOpacity));
            if (double.IsNaN(BorderThickness)) BorderThickness = 1;
            BorderThickness = Math.Max(0.5, Math.Min(12, BorderThickness));
            OffsetX = Math.Max(-20000, Math.Min(20000, OffsetX));
            OffsetY = Math.Max(-20000, Math.Min(20000, OffsetY));
            if (Modules == null) Modules = new List<ModuleConfig>();
            Modules.RemoveAll(delegate (ModuleConfig m) { return m == null || !ModuleTypes.Keep(m.Type, true); });
            foreach (var m in Modules)
            {
                m.Normalize();
                // Build 2: S / M / L text in widgets too - the nearest to the font size it had
                if (string.IsNullOrEmpty(m.TextSize))
                {
                    double had = m.FollowFontSize ? FontSize : m.FontSize;
                    m.TextSize = (m.TwoLines || m.TempOnSecondLine) ? "M" : had >= 16 ? "L" : had >= 13 ? "M" : "S";
                }
            }
        }
    }


    /// <summary>
    /// One snap link of a widget: what it is lined up with on one axis. Rel says how; To is the other widget's Id (widget relations) or the
    /// bar's Id (bar relations); Gap is the space kept, in DIPs (gap relations only).
    /// </summary>
    public sealed class SnapLink
    {
        /// <summary>Across: "RightOf", "LeftOf" (a widget, with the gap), "AlignLeft", "AlignRight", "AlignCentre" (a widget's edge / centre),
        /// "EdgeLeft", "EdgeRight" (the screen edge, with the edge gap), "ScreenCentre".</summary>
        public static readonly string[] XRels = { "RightOf", "LeftOf", "AlignLeft", "AlignRight", "AlignCentre", "EdgeLeft", "EdgeRight", "ScreenCentre" };
        /// <summary>Up / down: "Below", "Above", "AlignTop", "AlignBottom", "AlignMiddle", "EdgeTop", "EdgeBottom", "BarBelow" (under a top
        /// bar), "BarAbove" (over a bottom bar), "ScreenMiddle".</summary>
        public static readonly string[] YRels = { "Below", "Above", "AlignTop", "AlignBottom", "AlignMiddle", "EdgeTop", "EdgeBottom", "BarBelow", "BarAbove", "ScreenMiddle" };

        public string Rel { get; set; } = "";
        public string To { get; set; } = "";
        public int Gap { get; set; } = 0;

        /// <summary>Kept with the widget gap (re-spaced when "Gap between widgets" changes).</summary>
        public bool UsesWidgetGap { get { return Rel == "RightOf" || Rel == "LeftOf" || Rel == "Below" || Rel == "Above"; } }
        /// <summary>Kept with the edge gap (re-spaced when "Gap from the screen edges and bars" changes).</summary>
        public bool UsesEdgeGap { get { return Rel.StartsWith("Edge") || Rel.StartsWith("Bar"); } }
        /// <summary>Lined up with another widget (To is a widget Id).</summary>
        public bool ToWidget { get { return Rel == "RightOf" || Rel == "LeftOf" || Rel == "Below" || Rel == "Above" || Rel.StartsWith("Align"); } }

        public bool Valid(bool across)
        {
            if (Rel == null || To == null) return false;
            if (Array.IndexOf(across ? XRels : YRels, Rel) < 0) return false;
            if (ToWidget && To.Length == 0) return false;
            Gap = Math.Max(-200, Math.Min(400, Gap));
            return true;
        }
    }
}
