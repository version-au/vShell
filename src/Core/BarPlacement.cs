using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vShell.Core
{
    /// <summary>Where one bar lives and what is on it.</summary>
    public sealed class BarPlacement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Monitor device name, e.g. \\.\DISPLAY2. Empty = primary.</summary>
        public string Monitor { get; set; } = "";
        /// <summary>The monitor itself (MonitorInfo.Key): a bar goes back to it when it's reconnected, whatever DISPLAYn it gets.</summary>
        public string MonitorKey { get; set; } = "";
        /// <summary>"Top" or "Bottom".</summary>
        public string Edge { get; set; } = "Bottom";
        /// <summary>true = the bar reserves its strip of the screen (maximised windows stop at it); false = windows can use the whole screen,
        /// and the bar drops behind a maximised window on its monitor while that window is in front.</summary>
        public bool ReserveSpace { get; set; } = true;
        /// <summary>Horizontal centre of the bar as a fraction (0..1) of the monitor width. Only used when FullWidth is off.</summary>
        public double CenterX { get; set; } = 0.5;
        /// <summary>Bar height in device-independent pixels (96 dpi units).</summary>
        public int BarHeight { get; set; } = 40;
        /// <summary>true = bar spans the whole monitor edge; false = bar is only as wide as its modules.</summary>
        public bool FullWidth { get; set; } = true;
        // ---- per-bar appearance (colours/font family stay global) ------------
        public double FontSize { get; set; } = 12;
        public double CornerRadius { get; set; } = 13;
        /// <summary>Draw a border in the Main Accent colour around the bar.</summary>
        public bool ShowBorder { get; set; } = true;
        /// <summary>Border thickness in DIPs.</summary>
        public double BorderThickness { get; set; } = 3;
        /// <summary>Border opacity in percent (100 = solid colour).</summary>
        public int BorderOpacity { get; set; } = 65;
        /// <summary>Outline colour: empty = Main Accent, a theme slot ("@secondary", "@text") or "#AARRGGBB".</summary>
        public string BorderColor { get; set; } = "";
        /// <summary>Glass edge: a light top line, a darker bottom line and a faint sheen - "Off", "Subtle" or "Strong".</summary>
        public string GlassEdge { get; set; } = "Off";

        public void NormalizeEdge()
        {
            if (GlassEdge != "Subtle" && GlassEdge != "Strong") GlassEdge = "Off";
        }
        /// <summary>true = this bar uses the theme's background transparency; false = its own BackgroundOpacity.</summary>
        public bool UseThemeOpacity { get; set; } = true;
        /// <summary>Background blur for this one: "Theme" (the Appearance setting), "Off", "Blur" or "Acrylic" (Frosted).</summary>
        public string Blur { get; set; } = "Theme";
        public int BackgroundOpacity { get; set; } = 90;
        /// <summary>true = all four corners rounded; false = only the two corners that do not touch the screen edge.</summary>
        public bool RoundAllCorners { get; set; } = false;
        /// <summary>Gap between the bar and the screen edge (DIPs).</summary>
        public int EdgeMargin { get; set; } = 0;
        /// <summary>Takes its look (height, corners, edge gap, outline) from the bar style for its layout; off = its own look.</summary>
        public bool FollowStyle { get; set; } = true;
        /// <summary>Dock layout: always centred on its monitor (wherever along the edge it is dropped).</summary>
        public bool KeepCentered { get; set; } = false;
        /// <summary>Full-width bars: modules sit in a left, a centre (centred on the screen) and a right zone. Off = one row, as before.</summary>
        public bool Zones { get; set; } = false;
        /// <summary>How "Show separator" looks: "Dots", "Line", "Chips" (every module in a chip) or "None". (0.23.0's "Chevron" becomes Dots.)</summary>
        public string SeparatorStyle { get; set; } = "Dots";
        /// <summary>Ordered modules, left to right. null only in settings files from v0.1 (migrated on load).</summary>
        public List<ModuleConfig> Modules { get; set; }

        /// <summary>
        /// Three zones: give every module that has no zone yet one from where it sits - before the first flexible spacer = Left, after the
        /// last = Right, in between = Centre (the first spacer itself Left, the last Right); no flexible spacer = all Left.
        /// Modules that already have a zone keep it.
        /// </summary>
        public void GuessZones()
        {
            if (Modules == null) return;
            int first = -1, last = -1;
            for (int i = 0; i < Modules.Count; i++)
                if (Modules[i] != null && Modules[i].Type == ModuleTypes.Spacer && Modules[i].SpacerWidth <= 0) { if (first < 0) first = i; last = i; }
            for (int i = 0; i < Modules.Count; i++)
            {
                ModuleConfig m = Modules[i];
                if (m == null || !string.IsNullOrEmpty(m.Zone)) continue;
                if (first < 0 || i <= first) m.Zone = "Left";
                else if (i >= last) m.Zone = "Right";
                else m.Zone = "Centre";
            }
        }

        /// <summary>
        /// The bars a first start gets (no settings file yet), both on the main monitor in the default theme:
        /// both with the default look (two corners rounded at 13, 3 px outline at 65 %);
        /// bottom: full width with zones - Start menu on the left, taskbar in the centre, notification bell + tray + clock on the right; reserves its space;
        /// top: only as wide as its modules, at the left, CPU / memory / GPU as line graphs, does not reserve space.
        /// </summary>
        public static List<BarPlacement> FirstRun(string monitor)
        {
            var taskbar = ModuleConfig.Create(ModuleTypes.Taskbar);
            taskbar.OnlyCurrentMonitor = true;
            taskbar.TaskGrouping = "Always";         // one button per app ...
            taskbar.TaskLabel = "Title";             // ... showing the window title
            var tray = ModuleConfig.Create(ModuleTypes.Tray);
            tray.GroupIcons = false;                 // every tray icon on the bar, none hidden behind the arrow
            var clock = ModuleConfig.Create(ModuleTypes.Clock);
            clock.SecondaryDate = true;              // the date in the Secondary Accent colour
            var start = ModuleConfig.Create(ModuleTypes.StartMenu);
            var bell = ModuleConfig.Create(ModuleTypes.Notifications);
            // zones: the Start menu on the left, the taskbar centred on the screen, the bell, tray and clock on the right
            start.Zone = "Left";
            taskbar.Zone = "Centre";
            bell.Zone = "Right"; tray.Zone = "Right"; clock.Zone = "Right";
            var bottom = new BarPlacement
            {
                Monitor = monitor, Edge = "Bottom", FullWidth = true, ReserveSpace = true, Zones = true,
                Modules = new List<ModuleConfig> { start, taskbar, bell, tray, clock }
            };

            var mods = new List<ModuleConfig>();
            foreach (string type in new[] { ModuleTypes.Cpu, ModuleTypes.Memory, ModuleTypes.Gpu })
            {
                var m = ModuleConfig.Create(type);
                m.Style = "Line";
                mods.Add(m);
            }
            var top = new BarPlacement
            {
                Monitor = monitor, Edge = "Top", FullWidth = false, ReserveSpace = false,
                CenterX = 0,                                       // at the left of the screen (kept fully on it)
                // same height, corners (the two away from the edge, 13) and outline (3 px, 65 %) as the bottom bar: the defaults
                Modules = mods
            };
            return new List<BarPlacement> { bottom, top };
        }
    }
}
