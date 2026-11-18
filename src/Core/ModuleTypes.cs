using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vShell.Core
{
    public static class ModuleTypes
    {
        public const string Taskbar = "Taskbar";
        public const string Tray = "Tray";
        public const string Launcher = "Launcher";
        public const string Cpu = "Cpu";
        public const string Memory = "Memory";
        public const string Gpu = "Gpu";
        public const string Spacer = "Spacer";
        public const string Hwinfo = "Hwinfo";
        public const string Clock = "Clock";
        public const string StartMenu = "StartMenu";
        public const string Network = "Network";
        public const string Disk = "Disk";
        public const string Battery = "Battery";
        public const string Volume = "Volume";
        public const string Uptime = "Uptime";
        public const string Processes = "Processes";
        public const string Label = "Label";
        public const string Services = "Services";
        public const string Weather = "Weather";
        public const string NowPlaying = "NowPlaying";
        public const string Notes = "Notes";
        public const string RunBox = "RunBox";
        /// <summary>From the plugins (registered when they're added): Komodo, Seerr, 3D printers, Downloads, UniFi, Proxmox, Home Assistant.</summary>
        public const string Komodo = "Komodo";
        public const string Seerr = "Seerr";
        public const string Printer = "Printer";
        public const string Downloads = "Downloads";
        public const string UnifiNetwork = "UnifiNetwork";
        public const string UnifiProtect = "UnifiProtect";
        public const string Proxmox = "Proxmox";
        public const string HomeAssistant = "HomeAssistant";
        public const string Climate = "Climate";
        public const string PowerFlow = "PowerFlow";
        public const string Notifications = "Notifications";
        public const string Terminal = "Terminal";
        /// <summary>Drives (This PC): each drive's space, opening in vXplorer when it's installed (DrivesModule).</summary>
        public const string Drives = "Drives";

        /// <summary>The module types built into vShell (plugins add theirs with Register).</summary>
        static readonly string[] BuiltIn =
        {
            StartMenu, Launcher, Taskbar, Spacer, Cpu, Memory, Gpu, Network, Disk, Drives, Battery, Volume, Uptime, Processes, Hwinfo, Clock, Label,
            Services, Tray, Weather, NowPlaying, Notes, RunBox, Notifications,
            Terminal
        };
        static readonly string[] BuiltInWidget =
        {
            StartMenu, Launcher, Cpu, Memory, Gpu, Network, Disk, Drives, Battery, Volume, Uptime, Processes, Hwinfo, Clock, Label,
            Services, Weather, NowPlaying, Notes, RunBox, Notifications
            // (no Terminal: since 0.38.194 the terminal is vTerm's drop-down, from a bar)
        };

        sealed class Extra { public string Type, Display, Short; public bool Bar, Widget; }
        static readonly List<Extra> Extras = new List<Extra>();

        /// <summary>A plugin's module type: its names, and whether it goes on bars / in widgets.</summary>
        public static void Register(string type, string display, string shortName, bool bar, bool widget)
        {
            lock (Extras)
            {
                Extras.RemoveAll(delegate (Extra e) { return e.Type == type; });
                Extras.Add(new Extra { Type = type, Display = display, Short = shortName, Bar = bar, Widget = widget });
            }
        }

        public static void Unregister(string type) { lock (Extras) Extras.RemoveAll(delegate (Extra e) { return e.Type == type; }); }

        /// <summary>One of vShell's own types (not a plugin's, and not one nobody knows).</summary>
        public static bool IsBuiltIn(string type) { return Array.IndexOf(BuiltIn, type) >= 0 || Array.IndexOf(BuiltInWidget, type) >= 0; }

        static string[] With(string[] own, Func<Extra, bool> take)
        {
            var l = new List<string>(own);
            lock (Extras) foreach (Extra e in Extras) if (take(e) && !l.Contains(e.Type)) l.Add(e.Type);
            return l.ToArray();
        }

        static Extra ExtraOf(string type)
        {
            lock (Extras) return Extras.Find(delegate (Extra e) { return e.Type == type; });
        }

        /// <summary>Every module type there is now (built in, and from the plugins that are added).</summary>
        public static string[] All { get { return With(BuiltIn, delegate (Extra e) { return e.Bar || e.Widget; }); } }
        /// <summary>Modules for bars (Notes only lives in widgets).</summary>
        public static string[] BarTypes { get { return With(Array.FindAll(BuiltIn, delegate (string t) { return t != Notes; }), delegate (Extra e) { return e.Bar; }); } }
        /// <summary>Modules that make sense inside a desktop widget (no taskbar / tray / spacer).</summary>
        public static string[] WidgetTypes { get { return With(BuiltInWidget, delegate (Extra e) { return e.Widget; }); } }
        /// <summary>Terminal: the keys that show / hide it from anywhere ("Off" = none).</summary>
        public static readonly string[] TermHotkeys = { "Off", "Ctrl+Alt+T", "Ctrl+`", "Alt+`", "Ctrl+Alt+`" };
        /// <summary>Run box: the keys that open it from anywhere ("Off" = none).</summary>
        public static readonly string[] RunHotkeys = { "Off", "Alt+Space", "Ctrl+Alt+Space", "Ctrl+Shift+Space", "Alt+Shift+Space" };
        /// <summary>Run box: where a plain search (no !bang) goes.</summary>
        public static readonly string[] MediaProgressModes = { "Under text", "Along the bottom", "Off" };
        public static readonly string[] RunEngines = { "Google", "DuckDuckGo", "Bing", "Custom", "None" };
        /// <summary>Notes: what the note is. Note = text with light formatting; Checklist = tick-off items; Markdown = a rendered page;
        /// Log = quick time-stamped entries, newest first.</summary>
        public static readonly string[] NoteKinds = { "Note", "Checklist", "Markdown", "Log" };
        /// <summary>vShell's own Notes stores (plugins add theirs: vDrop).</summary>
        public static readonly string[] NoteStores = { "vShell", "File", "Folder" };
        public static readonly string[] NoteFonts = { "Theme", "Mono", "Handwritten" };
        public static readonly string[] NoteDoneModes = { "Strike", "Hide", "Bottom" };
        public static readonly string[] NoteEditModes = { "Click", "Double-click", "Menu" };
        public static readonly string[] NotePapers = { "Theme", "Yellow", "Mint", "Pink", "Blue", "Own" };
        [PluginApi] public static readonly string[] TileLayouts = { "Row", "List" };
        /// <summary>Volume module: the output device shown, how muted looks, what a click does.</summary>
        public static readonly string[] VolumeDevices = { "Off", "Short", "Full" };
        public static readonly string[] VolumeMutedStyles = { "Text", "Icon", "Dim" };
        public static readonly string[] VolumeClicks = { "Mixer", "Mute", "Settings" };
        public static readonly string[] ServiceKinds = { "Http", "Tcp", "Ping" };
        public static readonly string[] DiskModes = { "Space", "Activity" };
        public static readonly string[] ProcSorts = { "Cpu", "Memory", "Gpu" };

        /// <summary>This module needs the GPU performance counters read (a GPU module, or Top processes sorted by GPU).</summary>
        public static bool UsesGpu(ModuleConfig m)
        {
            return m.Type == Gpu || (m.Type == Processes && m.ProcSort == "Gpu");
        }
        /// <summary>Uptime: what it counts from.</summary>
        public static readonly string[] UpFroms = { "Last restart", "Awake time", "Last wake-up", "Sign-in" };
        public static readonly string[] UpFormats = { "Short", "Long", "Decimal" };
        /// <summary>The clock layouts offered (TimeOverDate is kept for widgets that already use it: shown as "Time and date").</summary>
        public static readonly string[] ClockLayouts = { "TimeOnly", "OneLine", "DateOverTime", "BigTime", "DateOnly" };
        public static readonly string[] ClockLayoutNames = { "Time only", "Time and date", "Date and time", "Large time - small date", "Date only" };
        public static readonly string[] DateStyles = { "Short", "Medium", "Long", "ISO" };
        public static readonly string[] GraphStyles = { "Text", "Meter", "Bars", "Line" };
        /// <summary>Line thickness choices and their widths in DIPs (Normal = the 1.5 from before).</summary>
        public static readonly string[] GraphLines = { "Thin", "Normal", "Thick", "Heavy" };
        public static double LineWidthOf(string name)
        {
            switch (name) { case "Thin": return 1; case "Thick": return 2.5; case "Heavy": return 3.5; default: return 1.5; }
        }
        public static readonly string[] GraphFills = { "None", "Light", "Solid", "Fade" };
        /// <summary>Colour by load: not for the speed graphs (Network, Downloads) - they have a download and an upload colour instead.</summary>
        public static bool HasLoadColors(string type) { return type != Network && type != Downloads; }
        /// <summary>History shown by line / bar graphs, in seconds, and its names.</summary>
        public static readonly int[] GraphSpans = { 60, 300, 900 };
        public static readonly string[] GraphSpanNames = { "1 minute", "5 minutes", "15 minutes" };
        /// <summary>Tray icons: their own colours, or tinted towards a theme colour.</summary>
        public static readonly string[] TrayIconColours = { "Original", "Tint" };
        public static readonly string[] TrayTintColours = { "Text", "Main Accent", "Secondary Accent" };
        /// <summary>The gauge modules (CPU, memory, GPU, disk, HWiNFO) also offer a round "Ring", a "Tile" (label, big number, sparkline) and
        /// "Dots" (dot graph and dot numbers - 0.38.227).</summary>
        public static readonly string[] GaugeStyles = { "Text", "Meter", "Bars", "Line", "Ring", "Tile", "Dots" };
        /// <summary>Dots in a bar: where the details (clock speed, GB, temperature …) go - "dots.details" in the module's bag (0.38.229).</summary>
        [PluginApi] public static readonly string[] DotDetails = { "Stacked", "Under the label", "Temperature in dots", "Taking turns", "Under the graph" };
        /// <summary>The Details in a bar choices a module offers: Temperature in dots only where there's a temperature - CPU, memory, GPU
        /// (Show temperature) and a HWiNFO sensor (often a temperature itself) (0.38.230).</summary>
        public static string[] DotDetailsFor(string type)
        {
            if (type == Cpu || type == Memory || type == Gpu || type == Hwinfo) return DotDetails;
            return Array.FindAll(DotDetails, delegate (string d) { return d != "Temperature in dots"; });
        }
        /// <summary>Battery: the plain graphs and Dots.</summary>
        public static readonly string[] BatteryStyles = { "Text", "Meter", "Bars", "Line", "Dots" };
        public static bool HasGauges(string type) { return type == Cpu || type == Memory || type == Gpu || type == Disk || type == Hwinfo || type == Downloads || type == Network; }
        public static readonly string[] TaskAnims = { "Off", "Slide", "Stretch", "Fade", "Shrink and grow", "Hop", "Liquid" };
        public static readonly string[] TaskAttentions = { "Pulse", "Steady" };
        public static readonly string[] WidgetHideAnims = { "Slide", "Fade", "Slide and fade" };
        public static readonly string[] WidgetHideWays = { "Left", "Right", "Up", "Down" };
        public static readonly string[] TaskAnimBounces = { "None", "Soft", "Springy" };
        /// <summary>Start button icons (all drawn in the theme's colours) - "Custom" = IconPath.</summary>
        public static readonly string[] StartIcons = { "vShell", "Tiles", "Grid", "Orbit", "Hexagon", "Bars", "Diamonds", "Custom" };
        public static readonly string[] StartIconNames = { "vShell mark", "Four tiles", "App grid", "Orbit", "Hexagon", "Stacked bars", "Diamonds", "Custom icon" };
        /// <summary>The Start menu's styles (ModuleConfig.StartStyle) and their names in Settings.</summary>
        public static readonly string[] StartStyles = { "Classic", "Grid", "Panes", "Cascade" };
        public static readonly string[] StartStyleNames = { "Classic", "Grid", "Two panes", "Cascading" };
        public static readonly string[] NotifyCorners = { "Bottom right", "Bottom left", "Top right", "Top left" };
        public static readonly string[] NotifyMonitors = { "Main", "Mouse" };
        public static readonly string[] NotifyGroups = { "By source", "One card" };
        [PluginApi] public static readonly string[] NotifyLevels = { "Normal", "Important", "Alert" };
        /// <summary>The notification history module: which notifications it lists.</summary>
        public static string[] NotifySources
        {
            get
            {
                var l = new List<string> { "All", "Services", "System", "Weather" };
                lock (ExtraSources) foreach (string s in ExtraSources) if (!l.Contains(s)) l.Add(s);
                return l.ToArray();
            }
        }
        /// <summary>Notification sources the plugins add ("Komodo", "Seerr", "3D printer", "Downloads", "UniFi", "Protect", "Proxmox", "Home Assistant", "Jellyfin").</summary>
        public static readonly List<string> ExtraSources = new List<string>();
        /// <summary>CPU also has "Cores" (a small bar per thread - logical processor) and "Dots" (dot graph and dot numbers - 0.38.226, CPU first).</summary>
        public static readonly string[] CpuStyles = { "Text", "Meter", "Bars", "Line", "Ring", "Tile", "Cores", "Dots" };
        public static string[] StylesFor(string type) { return type == Cpu ? CpuStyles : HasGauges(type) ? GaugeStyles : type == Battery ? BatteryStyles : GraphStyles; }

        /// <summary>
        /// Change a module's graph style, keeping each style's own graph width / height: the current size is remembered for the old style and
        /// the new style gets the size it had last time (a Ring that never had one starts at 86 % of the row height; the others keep the
        /// size they have now).
        /// </summary>
        public static void SetGraphStyle(ModuleConfig m, string style)
        {
            if (m == null || style == null || style == m.Style) return;
            if (m.StyleSizes == null) m.StyleSizes = new Dictionary<string, GraphSize>();
            m.StyleSizes[m.Style ?? "Meter"] = new GraphSize { Width = m.GraphWidth, HeightPercent = m.GraphHeightPercent };
            GraphSize g;
            if (m.StyleSizes.TryGetValue(style, out g) && g != null) { m.GraphWidth = g.Width; m.GraphHeightPercent = g.HeightPercent; }
            else if (style == "Ring") m.GraphHeightPercent = 86;
            else if (m.Style == "Ring" || m.Style == "Tile")
            {
                // from a ring / tile to a graph it never had: the size of the graphs it did have, otherwise the defaults
                GraphSize from = null;
                foreach (string k in new[] { "Line", "Bars", "Meter" })
                    if (from == null && m.StyleSizes.TryGetValue(k, out g) && g != null) from = g;
                m.GraphWidth = from != null ? from.Width : 60;
                m.GraphHeightPercent = from != null ? from.HeightPercent : 60;
            }
            m.Style = style;
        }
        /// <summary>Bar zones a module can sit in (when the bar has "Three zones" on).</summary>
        public static readonly string[] Zones = { "Left", "Centre", "Right" };
        /// <summary>How "Show separator" looks on a bar.</summary>
        public static readonly string[] SeparatorStyles = { "Dots", "Line", "Chips", "None" };
        public static readonly string[] TaskGroupings = { "Always", "WhenFull", "Never" };
        public static readonly string[] TaskLabels = { "Icon", "Name", "Title" };
        [PluginApi] public static string Display(string type)
        {
            switch (type)
            {
                case Taskbar: return "Taskbar";
                case Tray: return "System tray";
                case Launcher: return "Application launcher";
                case Cpu: return "CPU monitor";
                case Memory: return "Memory monitor";
                case Gpu: return "GPU monitor";
                case Spacer: return "Spacer (flexible gap)";
                case Hwinfo: return "Hardware sensor (HWiNFO / LHM)";
                case Clock: return "Clock (time and date)";
                case StartMenu: return "Start menu";
                case Network: return "Network speed";
                case Disk: return "Disk space / activity";
                case Drives: return "Drives (This PC)";
                case Battery: return "Battery";
                case Volume: return "Volume";
                case Uptime: return "Uptime";
                case Processes: return "Top processes";
                case Label: return "Text label";
                case Services: return "Service status";
                case Weather: return "Weather";
                case NowPlaying: return "Now playing";
                case Notes: return "Notes";
                case RunBox: return "Run box";
                case Notifications: return "Notifications";
                case Terminal: return "Terminal";
                default:
                    {
                        Extra e = ExtraOf(type);
                        return e != null ? e.Display : type;
                    }
            }
        }

        /// <summary>A short name (widget tabs).</summary>
        [PluginApi] public static string ShortName(string type)
        {
            switch (type)
            {
                case Cpu: return "CPU";
                case Memory: return "Memory";
                case Gpu: return "GPU";
                case Hwinfo: return "Sensor";
                case StartMenu: return "Start";
                case Launcher: return "Apps";
                case Disk: return "Disk";
                case Drives: return "Drives";
                case Processes: return "Processes";
                case Label: return "Text";
                case Services: return "Services";
                case NowPlaying: return "Music";
                case Notifications: return "Notifications";
                case Terminal: return "Terminal";
                default:
                    {
                        Extra e = ExtraOf(type);
                        return e != null && !string.IsNullOrEmpty(e.Short) ? e.Short : Display(type);
                    }
            }
        }

        public static bool IsMetric(string type) { return type == Cpu || type == Memory || type == Gpu; }
        public static bool IsValid(string type) { return Array.IndexOf(All, type) >= 0; }

        /// <summary>
        /// Kept in the settings when loading: a known type, or one vShell doesn't know (a plugin's that isn't added now - adding it back
        /// brings the module back as it was).
        /// </summary>
        public static bool Keep(string type, bool widget)
        {
            if (string.IsNullOrEmpty(type)) return false;
            if (!IsBuiltIn(type)) return true;
            return widget ? Array.IndexOf(BuiltInWidget, type) >= 0 : Array.IndexOf(BuiltIn, type) >= 0;
        }
    }
}
