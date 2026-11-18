using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vShell.Core
{
    /// <summary>An app pinned to the taskbar: shown even when it isn't running; its windows are grouped on it when it is.</summary>
    public sealed class PinnedApp
    {
        public string Name { get; set; } = "";
        /// <summary>What to start: a program, a shortcut (.lnk) or shell:AppsFolder\&lt;AppUserModelID&gt; for Store apps.</summary>
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
        /// <summary>The group key of its windows ("aumid:..." or "exe:..."), so running windows land on this pin.</summary>
        public string AppId { get; set; } = "";

        public PinnedApp Copy() { return new PinnedApp { Name = Name, Path = Path, Args = Args, AppId = AppId }; }
    }

    /// <summary>One shortcut, or (IsGroup) a named group of shortcuts that opens a popup menu. A group can hold groups (MaxDepth levels).</summary>
    public sealed class LauncherEntry
    {
        /// <summary>How deep groups go: a group at this level holds only shortcuts.</summary>
        public const int MaxDepth = 4;

        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
        public bool IsGroup { get; set; }
        /// <summary>Optional custom icon: an image (.ico/.png/.jpg/.bmp/.gif) or a program/DLL to take the icon from. Empty = automatic.</summary>
        public string IconPath { get; set; } = "";
        /// <summary>Groups only: a built-in icon ("games", "media" … see IconUtil.GroupIcons) drawn in the theme's colours. Empty = the folder. A custom icon wins.</summary>
        public string IconGlyph { get; set; } = "";
        /// <summary>The built-in icon in the theme's accent colour instead of its text colour.</summary>
        public bool IconAccent { get; set; }
        public List<LauncherEntry> Children { get; set; } = new List<LauncherEntry>();
        /// <summary>Groups only: folders whose shortcuts this group keeps in sync with (see LauncherFolders).</summary>
        public List<string> SyncFolders { get; set; } = new List<string>();
        /// <summary>Groups only: browser bookmark folders this group keeps in sync with (see LauncherBookmarks).</summary>
        public List<BookmarkFolder> SyncBookmarks { get; set; } = new List<BookmarkFolder>();
        /// <summary>A shortcut added from a synced bookmarks folder: the bookmark's name in the browser when last synced. Empty = not from bookmarks.</summary>
        public string BookmarkName { get; set; } = "";
    }

    /// <summary>A browser bookmarks folder a launcher group follows.</summary>
    public sealed class BookmarkFolder
    {
        /// <summary>The browser profile's Bookmarks file (Chrome, Edge, Brave, Vivaldi, Opera …).</summary>
        public string File { get; set; } = "";
        /// <summary>Shown in the editor, e.g. "Edge (Work)".</summary>
        public string Browser { get; set; } = "";
        /// <summary>The folder's guid in the file - stays the same when the folder is renamed or moved.</summary>
        public string Guid { get; set; } = "";
        /// <summary>Where the folder is, e.g. "Favorites bar/Homelab" - used when the guid isn't found (older files have none).</summary>
        public string Folder { get; set; } = "";
    }

    /// <summary>Names of the theme colour slots a colour setting can hold instead of a colour (it then follows the theme).</summary>
    public static class ThemeSlots
    {
        public const string Main = "@main", Secondary = "@secondary", Text = "@text";

        /// <summary>Settings written before the rename used "@accent" and "@spare".</summary>
        [PluginApi] public static string Migrate(string spec)
        {
            if (spec == null) return "";
            string k = spec.Trim().ToLowerInvariant();
            if (k == "@accent") return Main;
            if (k == "@spare") return Secondary;
            return spec;
        }
    }

    /// <summary>One thing the Service status module watches.</summary>
    public sealed class ServiceCheck
    {
        public string Name { get; set; } = "";
        /// <summary>"Http" (address / URL), "Tcp" (host:port) or "Ping" (host).</summary>
        public string Kind { get; set; } = "Http";
        public string Target { get; set; } = "";
        /// <summary>Accept a self-signed or otherwise untrusted HTTPS certificate.</summary>
        public bool IgnoreCert { get; set; } = false;
    }

    /// <summary>One note of a Notes module stored in vShell.</summary>
    public sealed class NotePage
    {
        public string Title { get; set; } = "";
        /// <summary>The note in Markdown (checklists as "- [ ] item", log entries as "- **yyyy-MM-dd HH:mm** text").</summary>
        public string Text { get; set; } = "";
        public DateTime Edited { get; set; } = DateTime.MinValue;
    }

    /// <summary>A sound device picked for the volume mixer in a widget (its name kept for when it isn't connected).</summary>
    public sealed class VolumeDeviceItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>A graph style's own size (see ModuleConfig.StyleSizes).</summary>
    public sealed class GraphSize
    {
        public int Width { get; set; } = 60;
        public int HeightPercent { get; set; } = 60;
    }

    /// <summary>Configuration of one module instance on one bar. Only the fields relevant to its Type are used.</summary>
    public sealed class ModuleConfig
    {
        [PluginApi] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [PluginApi] public string Type { get; set; } = ModuleTypes.Taskbar;
        /// <summary>Three coloured dots before this module (a separator from the next one).</summary>
        public bool ShowSeparator { get; set; } = false;
        /// <summary>true = use the bar's / widget's font size; false = FontSize below.</summary>
        public bool FollowFontSize { get; set; } = true;
        /// <summary>This module's own font size (used when FollowFontSize is off).</summary>
        public double FontSize { get; set; } = 12;

        // ---- CPU / memory / GPU ----------------------------------------
        /// <summary>"Text", "Meter", "Bars", "Line", "Ring" (round gauge) or "Tile" (label, big number, sparkline).</summary>
        [PluginApi] public string Style { get; set; } = "Meter";
        /// <summary>Tile graph style: the module's width in DIPs (0 = as wide as its text needs).</summary>
        public int TileWidth { get; set; } = 0;
        /// <summary>Widgets showing their modules as tabs: this module's tab name (empty = automatic).</summary>
        public string TabName { get; set; } = "";
        /// <summary>Graph width / height remembered per graph style, so switching style and back keeps each style's own size.</summary>
        public Dictionary<string, GraphSize> StyleSizes { get; set; } = new Dictionary<string, GraphSize>();
        /// <summary>Bars with three zones: "Left", "Centre" or "Right". Empty = not placed yet (worked out from the spacers when zones are switched on).</summary>
        public string Zone { get; set; } = "";
        [PluginApi] public bool ShowText { get; set; } = true;
        /// <summary>Memory: show used/total GB instead of %.  GPU: also show VRAM in use.</summary>
        public bool ShowDetail { get; set; } = false;
        /// <summary>Width of the graph area in DIPs.</summary>
        public int GraphWidth { get; set; } = 60;
        /// <summary>Height of bar/line graphs as a percentage of the bar or widget row height (they are centred vertically); 0 = automatic
        /// (0.38.233, as the Fans plugin's) - ShownGraphHeightPercent is what's drawn.</summary>
        public int GraphHeightPercent { get; set; } = 60;
        /// <summary>Colour: "#AARRGGBB", or a theme slot "@main" / "@secondary" / "@text" that follows the theme. Empty = the module's default.</summary>
        [PluginApi] public string Color { get; set; } = "";
        // ---- weather ----------------------------------------------------
        /// <summary>"OpenMeteo" (free, no account), or a plugin's source - "HomeAssistant" (WeatherEntity, a weather.* entity).</summary>
        public string WeatherSource { get; set; } = "OpenMeteo";
        /// <summary>Open-Meteo: the place picked in the search (shown name) and its coordinates.</summary>
        public string WeatherPlace { get; set; } = "";
        public double WeatherLat { get; set; } = 0;
        public double WeatherLon { get; set; } = 0;
        public bool WeatherFahrenheit { get; set; } = false;
        /// <summary>Open-Meteo: minutes between updates.</summary>
        public int WeatherMinutes { get; set; } = 15;
        [PluginApi] public string WeatherEntity { get; set; } = "";
        public bool WeatherShowCondition { get; set; } = false;
        public bool WeatherShowHighLow { get; set; } = false;
        /// <summary>Widgets: show the forecast under the current weather (TileLayout "List" = a day per line, "Row" = days side by side).</summary>
        public bool WeatherShowForecast { get; set; } = false;
        public int WeatherForecastDays { get; set; } = 7;
        /// <summary>Gap between the forecast days: between the columns (Row) or the lines (List), in DIPs.</summary>
        public int WeatherForecastSpacing { get; set; } = 12;

        // ---- now playing ------------------------------------------------
        public bool MediaShowArt { get; set; } = true;
        public bool MediaShowControls { get; set; } = true;
        /// <summary>Widest the title / artist text may get before it is cut off with "…".</summary>
        public int MediaWidth { get; set; } = 220;
        /// <summary>Text longer than MediaWidth scrolls back and forth (off = cut off with "…").</summary>
        public bool MediaScroll { get; set; } = true;
        /// <summary>The progress bar: "Under text" (a thin bar under the title), "Along the bottom" (the module's bottom edge) or "Off".</summary>
        public string MediaProgress { get; set; } = "Under text";
        /// <summary>Show where it is and how long it is ("1:23 / 4:56") next to the buttons.</summary>
        public bool MediaShowTimes { get; set; } = false;

        // ---- plugin modules ----------------------------------------------------
        /// <summary>A plugin module's own settings (null until it keeps one) - see SettingsBag. Use Bag.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SettingsBag Extra { get; set; }
        [PluginApi] [JsonIgnore] public SettingsBag Bag { get { return Extra ?? (Extra = new SettingsBag()); } }
        /// <summary>Settings this version doesn't have any more (a module's before it went into a plugin): moved into Extra on load, then dropped.</summary>
        [JsonExtensionData] public Dictionary<string, JsonElement> Old { get; set; }


        // ---- Notifications (history) ----------------------------------------
        /// <summary>Which source it lists (All or one of ModuleTypes.NotifySources).</summary>
        public string NfSource { get; set; } = "All";
        public int NfCount { get; set; } = 8;
        /// <summary>Bar: the number not read yet as a badge.</summary>
        public bool NfBadge { get; set; } = true;
        /// <summary>Bar: hide the bell when there is nothing in the history.</summary>
        public bool NfHideEmpty { get; set; } = false;
        /// <summary>Bar: the badge glows while an Alert is unread (and bounces when one arrives).</summary>
        public bool NfPulse { get; set; } = true;
        /// <summary>Bar: closing the list marks what it showed as read (off = they stay unread until marked: ✓ / Mark all read).</summary>
        public bool NfReadOnClose { get; set; } = false;

        /// <summary>Second graph series colour (network upload), same format as Color. Empty = the Secondary Accent.</summary>
        [PluginApi] public string Color2 { get; set; } = "";

        // ---- graph look (any module with a graph; the defaults are the look from before these existed) ----
        /// <summary>Line graphs (and a tile's sparkline): ModuleTypes.GraphLines - Thin / Normal / Thick / Heavy.</summary>
        public string GraphLine { get; set; } = "Normal";
        /// <summary>Under a line: ModuleTypes.GraphFills - None / Light (faint, as before) / Solid / Fade (strong at the line, fading downwards).</summary>
        public string GraphFill { get; set; } = "Light";
        /// <summary>Line graphs: smooth curves instead of straight lines between the samples.</summary>
        public bool GraphSmooth { get; set; } = false;
        /// <summary>Line and bar graphs: the box behind the graph (the track colour).</summary>
        public bool GraphBack { get; set; } = true;
        /// <summary>Line and bar graphs: the box's corner rounding in DIPs.</summary>
        public int GraphRadius { get; set; } = 3;
        /// <summary>Line and bar graphs: faint lines at 25 / 50 / 75 %, and a dashed line at the warning level.</summary>
        public bool GraphGrid { get; set; } = false;
        /// <summary>Line and bar graphs: seconds of history shown (ModuleTypes.GraphSpans: 60, 300, 900). A plugin's own automatic scale:
        /// MetricView.ScaleFor(history, history2, min, GraphSpan) - the peak of what the line shows. Plugins: since vShell 0.38.140.</summary>
        [PluginApi] public int GraphSpan { get; set; } = 60;
        /// <summary>Colour by load: the graph blends from LoadColor1 (empty) through LoadColor2 to LoadColor3 (full), instead of the graph / warning colours.</summary>
        public bool LoadColors { get; set; } = false;
        /// <summary>Same format as Color. Empty = Main Accent, faint (1) / Main Accent, stronger (2) / Secondary Accent (3).</summary>
        public string LoadColor1 { get; set; } = "";
        public string LoadColor2 { get; set; } = "";
        public string LoadColor3 { get; set; } = "";

        // ---- warning threshold (graph modules) --------------------------
        /// <summary>Draw the graph and text in the Secondary Accent colour when the value crosses WarnAt.</summary>
        public bool WarnOn { get; set; } = false;
        /// <summary>Threshold: percent for CPU / memory / GPU / disk / network / battery / volume; the sensor's own unit for HWiNFO.</summary>
        public double WarnAt { get; set; } = 85;
        /// <summary>Old fade setting (the fade is now always on, Theme.WarnFade); kept so old settings load.</summary>
        public double WarnBlend { get; set; } = 0;
        /// <summary>Warning colour (graph and text past the threshold), same format as Color; empty = the Secondary Accent.</summary>
        public string WarnColor { get; set; } = "";
        /// <summary>Battery only: warn when the value is at or below WarnAt instead of at or above.</summary>
        public bool WarnBelow { get; set; } = false;
        /// <summary>The threshold follows the graph: 70 % of its range (HWiNFO: of "empty at" - "full at"). Typing your own turns this off.</summary>
        public bool WarnAuto { get; set; } = true;
        /// <summary>The warning has been given its default once (on, automatic threshold) - so turning it off later stays off.</summary>
        public bool WarnSetup { get; set; } = false;

        /// <summary>The module types with a graph and a warning threshold.</summary>
        public static bool HasWarning(string type)
        {
            switch (type)
            {
                case ModuleTypes.Cpu: case ModuleTypes.Memory: case ModuleTypes.Gpu: case ModuleTypes.Hwinfo:
                case ModuleTypes.Network: case ModuleTypes.Disk: case ModuleTypes.Battery: case ModuleTypes.Downloads:
                    return true;
                default: return false;
            }
        }

        /// <summary>The automatic threshold: 70 % of the graph's range (in the sensor's unit for HWiNFO; percent of the graph otherwise).</summary>
        public double AutoWarnAt()
        {
            if (Type != ModuleTypes.Hwinfo) return 70;
            double v = RangeMin + 0.7 * (RangeMax - RangeMin);
            return Math.Round(v, Math.Abs(v) >= 100 ? 0 : 1);
        }

        /// <summary>Keep an automatic threshold in step with the graph range.</summary>
        public void SyncWarn()
        {
            if (WarnAuto && HasWarning(Type) && Type != ModuleTypes.Battery) WarnAt = AutoWarnAt();
        }
        /// <summary>GPU module: which adapter (1 = the one using the most VRAM).</summary>
        public int GpuIndex { get; set; } = 1;
        /// <summary>Append a temperature to the text. Source: TempSensor (a HWiNFO reading), or automatic when empty.</summary>
        public bool ShowTemp { get; set; } = false;
        /// <summary>CPU: the load as Task Manager shows it (% Processor Utility - counts turbo boost); off = processor time (GetSystemTimes).</summary>
        public bool CpuTaskManager { get; set; } = true;
        /// <summary>CPU: average the load over this many seconds (0 = off - each second as it is).</summary>
        public int CpuSmooth { get; set; } = 0;
        /// <summary>CPU: the current clock speed after the percentage ("4.5 GHz").</summary>
        public bool CpuShowSpeed { get; set; } = false;
        /// <summary>Key of a HWiNFO reading ("sensor|label"); empty = automatic (GPU: NVIDIA driver, then HWiNFO).</summary>
        public string TempSensor { get; set; } = "";
        /// <summary>Put the temperature on a second line under the usage text.</summary>
        public bool TempOnSecondLine { get; set; } = false;

        // ---- clock ---------------------------------------------------------------
        /// <summary>One of ModuleTypes.ClockLayouts.</summary>
        public string ClockLayout { get; set; } = "OneLine";
        public bool Use24Hour { get; set; } = true;
        public bool ShowSeconds { get; set; } = false;
        /// <summary>Clock parts drawn in the Secondary Accent colour.</summary>
        [JsonPropertyName("SpareDate")]   // the saved name from before the rename
        public bool SecondaryDate { get; set; } = false;
        /// <summary>Clock: a click opens the month calendar (like Windows' taskbar clock).</summary>
        public bool ClockCalendar { get; set; } = true;
        /// <summary>Clock calendar: the first day of the week - "Windows" (your Windows setting), "Monday" or "Sunday".</summary>
        public string ClockWeekStart { get; set; } = "Windows";
        /// <summary>Clock calendar: week numbers down the left.</summary>
        public bool ClockWeekNumbers { get; set; } = false;
        /// <summary>"Short" (system short date), "Medium" (Sun 11 Oct), "Long" (Sunday, 11 October 2026) or "ISO" (2026-10-11).</summary>
        public string DateStyle { get; set; } = "Medium";

        // ---- HWiNFO sensor module --------------------------------------
        /// <summary>Key of the HWiNFO reading to show ("sensor|label").</summary>
        public string SensorKey { get; set; } = "";
        /// <summary>Text shown before the value. Empty = HWiNFO's own label.</summary>
        public string SensorTitle { get; set; } = "";
        /// <summary>Replaces the unit HWiNFO reports. Empty = keep HWiNFO's.</summary>
        public string SensorUnit { get; set; } = "";
        public int Decimals { get; set; } = 0;
        /// <summary>Title on the first line, value on the second.</summary>
        public bool TwoLines { get; set; } = false;

        // ---- on a bar: S / M / L text, and what the bar shows right now to fit (not saved) ----
        /// <summary>Bar modules: "S" (one line, small), "M" (two lines), "L" (one line, large). Empty = worked out from the old font size.</summary>
        [PluginApi] public string TextSize { get; set; } = "";
        /// <summary>The font size the bar draws this module at now (0 = the old font size settings: widgets).</summary>
        [PluginApi] [JsonIgnore] public double ShownFontSize { get; set; }
        /// <summary>Two lines now (M) or one (S / L); null = the module's own two-line settings (widgets).</summary>
        [PluginApi] [JsonIgnore] public bool? ShownTwoLines { get; set; }
        /// <summary>How long the graph is drawn now: 1 = as set, less when a full bar needs the room.</summary>
        [PluginApi] [JsonIgnore] public double ShownGraphScale { get; set; } = 1;
        [JsonIgnore] public int ShownGraphWidth { get { return Math.Max(16, (int)Math.Round(GraphWidth * ShownGraphScale)); } }
        /// <summary>The graph height drawn: Graph height, or with 0 (automatic) the style's own - a ring 86 %, the rest 60 % (the defaults).</summary>
        [JsonIgnore] public int ShownGraphHeightPercent { get { return GraphHeightPercent > 0 ? GraphHeightPercent : Style == "Ring" ? 86 : 60; } }
        [JsonIgnore] public double ShownTileWidth { get { return TileWidth <= 0 ? 0 : Math.Max(40, Math.Round(TileWidth * ShownGraphScale)); } }
        [PluginApi] [JsonIgnore] public bool ShownLines2 { get { return ShownTwoLines ?? TwoLines; } }
        [JsonIgnore] public bool ShownTempLine2 { get { return ShownTwoLines ?? TempOnSecondLine; } }
        /// <summary>Value that fills 0% / 100% of the meter and graphs.</summary>
        public double RangeMin { get; set; } = 0;
        [PluginApi] public double RangeMax { get; set; } = 100;
        /// <summary>Speed graphs (network, downloads): the full scale follows the highest speed shown instead of RangeMax.</summary>
        [PluginApi] public bool AutoScale { get; set; } = false;

        // ---- taskbar -----------------------------------------------------
        public int MaxButtonWidth { get; set; } = 180;
        public bool OnlyCurrentMonitor { get; set; } = false;
        /// <summary>Taskbar: "Always" (one button per app), "WhenFull" (combine when the buttons would get too narrow) or "Never".</summary>
        public string TaskGrouping { get; set; } = "Always";
        /// <summary>Taskbar, combined buttons: "Icon" (icon only), "Name" (icon + app name) or "Title" (icon + the latest window's title).</summary>
        public string TaskLabel { get; set; } = "Icon";
        /// <summary>Taskbar: live window previews on hover (and in the window list of a combined button).</summary>
        public bool TaskPreviews { get; set; } = true;
        /// <summary>Width of one preview in DIPs.</summary>
        public int TaskPreviewSize { get; set; } = 220;
        /// <summary>Taskbar: show this taskbar's pinned apps (Pins).</summary>
        public bool TaskShowPins { get; set; } = true;
        /// <summary>Taskbar: the icon size (DIPs) of pins and icon-only buttons; 0 = fits the bar's height.</summary>
        public int TaskIconSize { get; set; } = 0;
        /// <summary>Network speed: the adapter it watches (its name, e.g. "Ethernet"); "" = every adapter with a gateway, added up.</summary>
        public string NetInterface { get; set; } = "";
        /// <summary>Taskbar: the apps pinned to THIS taskbar, in order (each taskbar has its own).</summary>
        public List<PinnedApp> Pins { get; set; } = new List<PinnedApp>();
        /// <summary>
        /// Taskbar: use the pins shared by every taskbar that has this on (BarSettings.SharedPins) instead of its own. Its own Pins are kept
        /// untouched meanwhile, so turning it off brings them back.
        /// </summary>
        public bool TaskSyncPins { get; set; }

        // ---- launcher ----------------------------------------------------
        public int IconSize { get; set; } = 24;
        /// <summary>Show names next to shortcut icons.</summary>
        public bool ShowLabels { get; set; } = false;
        /// <summary>Show names next to group icons (separate from ShowLabels).</summary>
        public bool ShowGroupLabels { get; set; } = false;
        public List<LauncherEntry> Entries { get; set; } = new List<LauncherEntry>();
        /// <summary>Launcher in a widget: "Row" (side by side, the module height), "Columns" (a grid LaunchCount across) or "Rows" (LaunchCount down).</summary>
        public string LaunchLayout { get; set; } = "Row";
        public int LaunchCount { get; set; } = 4;

        /// <summary>Launcher / Taskbar: the highlight box behind the icon under the mouse.</summary>
        public bool HoverBox { get; set; } = true;
        /// <summary>Taskbar: how the active-window line moves to another button - Off (jumps), Slide, Stretch or Fade.</summary>
        public string TaskAnim { get; set; } = "Stretch";
        /// <summary>Taskbar: how long that takes (ms).</summary>
        public int TaskAnimMs { get; set; } = 400;
        /// <summary>Taskbar: None, Soft (overshoots a little) or Springy (a couple of wobbles).</summary>
        public string TaskAnimBounce { get; set; } = "Soft";
        /// <summary>Taskbar: the active button's highlight moves along with the line.</summary>
        public bool TaskAnimHighlight { get; set; } = true;
        /// <summary>Taskbar: an app asking for attention - Pulse (the background slowly pulses) or Steady.</summary>
        public string TaskAttention { get; set; } = "Pulse";

        // ---- dock animations (Launcher / Taskbar on a Dock bar, icons only) ---------
        /// <summary>The hovered icon rises a little and grows slightly (not together with Magnify).</summary>
        public bool DockLift { get; set; } = false;
        /// <summary>A soft glow in the Main Accent around the hovered icon.</summary>
        public bool DockGlow { get; set; } = false;
        /// <summary>How strong the glow is (%).</summary>
        public int DockGlowStrength { get; set; } = 70;
        /// <summary>How far the glow reaches (% of the icon size).</summary>
        public int DockGlowSize { get; set; } = 170;
        /// <summary>macOS-style: the icon under the mouse grows most, its neighbours less the further away they are (not together with Lift).</summary>
        public bool DockMagnify { get; set; } = false;
        /// <summary>How big the icon under the mouse gets (% of its normal size).</summary>
        public int DockMagnifyPercent { get; set; } = 160;
        /// <summary>The icon bounces when you start an app from it.</summary>
        public bool DockBounce { get; set; } = false;

        // ---- run box ----------------------------------------------------------
        /// <summary>The box's width on the bar / in the widget (DIPs).</summary>
        public int RunWidth { get; set; } = 220;
        /// <summary>Keys that open this run box from anywhere (ModuleTypes.RunHotkeys; the first run box that has keys set gets them).</summary>
        public string RunHotkey { get; set; } = "Off";
        /// <summary>Terminal: the keys that show / hide it from anywhere (ModuleTypes.TermHotkeys; the first terminal with keys gets them).</summary>
        public string TermHotkey { get; set; } = "Off";
        /// <summary>Terminal: the profile a new tab starts with (its name; empty = the first).</summary>
        public string TermProfile { get; set; } = "";
        /// <summary>Terminal: width in % of the monitor, height in % of the monitor.</summary>
        public int TermWidth { get; set; } = 70;
        public int TermHeight { get; set; } = 50;
        /// <summary>Terminal: hide it when another window is clicked (drop-down style); off = it stays until hidden.</summary>
        public bool TermHideOnLeave { get; set; } = true;
        /// <summary>Terminal as a button: take no room on the bar / widget - only its keys open it (ignored while the keys are Off).</summary>
        public bool TermHideButton { get; set; } = false;
        /// <summary>Terminal on a bar: drop down flush from the bar, as wide as the bar (off = centred, at TermWidth).</summary>
        public bool TermAttach { get; set; } = true;
        /// <summary>Terminal in a widget: the terminal itself in the widget (off = a button that opens the drop-down).</summary>
        public bool TermInWidget { get; set; } = true;
        /// <summary>Terminal in a widget: its size (DIPs).</summary>
        public int TermWidgetHeight { get; set; } = 600;
        public int TermWidgetWidth { get; set; } = 700;
        /// <summary>This module's popup (menu, list, calendar, run box drop-down ...) gets the bar's / widget's background effect (blur /
        /// frosted) when that is on. Off = the popup's plain background, as before.</summary>
        public bool PopupBackdrop { get; set; } = true;

        // ---- notes (widgets) ------------------------------------------------
        /// <summary>"Note", "Checklist", "Markdown" or "Log" (ModuleTypes.NoteKinds).</summary>
        public string NoteKind { get; set; } = "Note";
        /// <summary>"vShell" (kept in settings.json, several notes possible) or "File" (a .txt / .md file, read and written both ways).</summary>
        public string NoteStore { get; set; } = "vShell";
        /// <summary>Stored in a file: its full path.</summary>
        public string NoteFile { get; set; } = "";
        /// <summary>Notes (Folder): a folder of .txt / .md files - the note shows a title line to switch between them.</summary>
        public string NoteFolder { get; set; } = "";
        /// <summary>Notes (Folder): the file from the folder shown now (File mode's NoteFile is kept separately).</summary>
        public string NoteFolderFile { get; set; } = "";
        /// <summary>The file a File / Folder note reads and writes.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string NoteActiveFile { get { return NoteStore == "Folder" ? NoteFolderFile : NoteFile; } }
        /// <summary>Stored in vShell: the notes (one shown at a time) and which one is shown.</summary>
        public List<NotePage> NotePages { get; set; } = new List<NotePage>();
        public int NoteIndex { get; set; } = 0;
        /// <summary>"Theme" (the widget's background), "Yellow", "Mint", "Pink", "Blue" or "#AARRGGBB" (own colour).</summary>
        public string NotePaper { get; set; } = "Theme";
        /// <summary>"Theme", "Mono" or "Handwritten".</summary>
        public string NoteFont { get; set; } = "Theme";
        /// <summary>Checklist: done items "Strike" (crossed out), "Hide" or "Bottom" (moved below the open ones).</summary>
        public string NoteDone { get; set; } = "Strike";
        /// <summary>Note / Markdown: "Click", "Double-click" or "Menu" (right-click > Edit) starts editing.</summary>
        public string NoteEditOn { get; set; } = "Click";
        /// <summary>Read only on the desktop.</summary>
        public bool NoteLocked { get; set; } = false;
        /// <summary>Show "edited 2 h ago".</summary>
        public bool NoteShowEdited { get; set; } = false;
        /// <summary>The note's width in DIPs (when the widget has no fixed width).</summary>
        public int NoteWidth { get; set; } = 260;
        /// <summary>true = as tall as its text; false = NoteHeight, longer text scrolls.</summary>
        public bool NoteGrow { get; set; } = false;
        public int NoteHeight { get; set; } = 200;
        /// <summary>Log: entries shown at most (older ones stay in the text).</summary>
        public int NoteLogMax { get; set; } = 50;
        /// <summary>Win+Shift+N jumps to this note (the first one that has it on) and starts typing.</summary>
        public bool NoteHotkey { get; set; } = false;

        // ---- tray ----------------------------------------------------------
        /// <summary>true = icons hidden behind an arrow, except the pinned ones.</summary>
        public bool GroupIcons { get; set; } = true;
        public List<string> PinnedKeys { get; set; } = new List<string>();
        /// <summary>Tray: the order you dragged the icons into (icon keys; icons not in it go after, in Windows' order).</summary>
        public List<string> TrayOrder { get; set; } = new List<string>();
        /// <summary>Tray: vShell's own network icon (Windows 11 keeps its network icon in Explorer's taskbar, out of other trays' reach).</summary>
        public bool TrayNetwork { get; set; } = true;
        /// <summary>Tray: the network icon was put on the bar (pinned) the first time; after that it's pinned / hidden like any other icon.</summary>
        public bool TrayNetworkPinned { get; set; } = false;
        /// <summary>Tray: "Original" or "Tint" (a theme colour over the icons at TrayTint %).</summary>
        public string TrayIconColour { get; set; } = "Original";
        /// <summary>Tray: the theme colour for Tint - "Text", "Main Accent" or "Secondary Accent".</summary>
        public string TrayTintColour { get; set; } = "Main Accent";
        /// <summary>Tray: how strong the tint is (percent).</summary>
        public int TrayTint { get; set; } = 40;

        // ---- start menu (also uses IconSize, ShowLabels and Color above) -------------
        /// <summary>Custom button icon: an image (.ico/.png/.jpg/.bmp/.gif) or a program/DLL to take the icon from. Empty = the themed default mark.</summary>
        public string IconPath { get; set; } = "";
        /// <summary>0.38.20: the Start button icon (ModuleTypes.StartIcons); empty when loaded from before = Custom if IconPath is set, else vShell.</summary>
        public string StartIcon { get; set; } = "";
        /// <summary>A lone press of the Windows key opens this menu.</summary>
        public bool UseWinKey { get; set; } = true;
        /// <summary>Start menu: a Pinned section above the programs - its own shortcuts and groups (in Entries, edited like a launcher's).</summary>
        public bool StartPinned { get; set; } = false;
        /// <summary>Start menu: the Start Menu folders show as folders you open (off = every program in one A-Z list).</summary>
        public bool StartFolders { get; set; } = true;
        /// <summary>0.38.105: Show folders was turned on once for modules saved with it off (it became the default).</summary>
        public bool StartFoldersSetup { get; set; } = false;
        /// <summary>Start menu: a Recent files section (from Windows' Recent folder).</summary>
        public bool StartRecent { get; set; } = false;
        /// <summary>Start menu: how many recent files it shows.</summary>
        public int StartRecentCount { get; set; } = 6;
        /// <summary>0.38.199: how the menu looks (ModuleTypes.StartStyles): Classic (one list with a folder strip), Grid (pinned grid, All programs behind a button), Panes (groups on the left, programs on the right) or Cascade (folders open to the side).</summary>
        public string StartStyle { get; set; } = "Classic";

        // ---- network ---------------------------------------------------------------
        /// <summary>Show the Wi-Fi name and signal strength.</summary>
        public bool ShowWifi { get; set; } = false;
        /// <summary>Speeds in bits per second (Mbit/s) instead of bytes (MB/s).</summary>
        public bool UseBits { get; set; } = false;

        // ---- disk ----------------------------------------------------------------
        /// <summary>Drive letter such as "C:". Empty = the Windows drive.</summary>
        public string DiskDrive { get; set; } = "";
        /// <summary>"Space" or "Activity".</summary>
        public string DiskMode { get; set; } = "Space";

        // ---- battery ---------------------------------------------------------------
        /// <summary>Hide the module on a PC without a battery.</summary>
        [PluginApi] public bool HideIfNone { get; set; } = true;

        // ---- volume ----------------------------------------------------------------
        /// <summary>Percent per mouse-wheel notch.</summary>
        public int VolumeStep { get; set; } = 2;
        /// <summary>The sound device this module shows and changes ("" = the default output device, whichever that is).</summary>
        public string VolumeDeviceId { get; set; } = "";
        /// <summary>Its name when picked (shown while it isn't connected).</summary>
        public string VolumeDeviceName { get; set; } = "";
        /// <summary>The device's name next to the volume: Off, Short ("Speakers") or Full ("Speakers (Focusrite USB Audio)").</summary>
        public string VolumeDevice { get; set; } = "Off";
        /// <summary>A line under the module that moves with the sound playing (the default device's level).</summary>
        public bool VolumeLevel { get; set; } = false;
        /// <summary>Muted shows as: Text ("muted"), Icon (the crossed-out speaker only) or Dim (the module faded).</summary>
        public string VolumeMuted { get; set; } = "Text";
        /// <summary>A middle click mutes / unmutes.</summary>
        public bool VolumeMiddleMute { get; set; } = true;
        /// <summary>A left click: Mixer, Mute or Settings (Windows' sound settings).</summary>
        public string VolumeClick { get; set; } = "Mixer";
        /// <summary>The mixer: only apps playing right now (idle ones on the default device left out too).</summary>
        public bool VolumePlayingOnly { get; set; } = false;
        /// <summary>The mixer: the level line under each slider.</summary>
        public bool VolumeMeters { get; set; } = true;
        /// <summary>The mixer: the output device at the top (click it to pick another).</summary>
        public bool VolumeDevicePicker { get; set; } = true;
        /// <summary>In a widget: the mixer itself in the widget, instead of the volume as text / meter.</summary>
        public bool VolumeMixerInWidget { get; set; } = false;
        /// <summary>The mixer in a widget: Rows (sliders across, one app a line) or Columns (upright sliders side by side).</summary>
        public string VolumeMixerLayout { get; set; } = "Rows";
        /// <summary>The mixer in a widget: the sound devices it shows, each with its own volume (empty = the one in VolumeDeviceId).</summary>
        public List<VolumeDeviceItem> VolumeMixerDevices { get; set; } = new List<VolumeDeviceItem>();
        /// <summary>The mixer: the apps' volumes (off = only the device volumes).</summary>
        public bool VolumeShowApps { get; set; } = true;
        /// <summary>The mixer: the Sound settings / Windows volume mixer links at the bottom.</summary>
        public bool VolumeLinks { get; set; } = true;

        // ---- uptime / processes / label ----------------------------------------------
        /// <summary>Text before the value. null = the module's default ("Up", "Top" ...); empty = none.</summary>
        [PluginApi] public string Title { get; set; } = null;
        /// <summary>Processes: how many are listed in the tooltip / popup.</summary>
        public int ProcCount { get; set; } = 8;
        /// <summary>Processes: how many programs are shown on the bar / widget itself (1-5).</summary>
        public int ProcShow { get; set; } = 1;
        /// <summary>Processes: text size of the list in percent of the bar / widget font.</summary>
        public int ProcScale { get; set; } = 100;
        /// <summary>Processes: extra space between the lines of the list, in DIPs.</summary>
        public int ProcSpacing { get; set; } = 0;
        /// <summary>Top programs: a fixed width in DIPs so longer / shorter names don't push the modules next to it about (0 = fits the text).</summary>
        public int ProcWidth { get; set; } = 0;

        // ---- uptime ----------------------------------------------------------------
        /// <summary>Uptime: what it counts from - "Last restart", "Awake time" (less sleep), "Last wake-up" or "Sign-in".</summary>
        public string UpFrom { get; set; } = "Last restart";
        /// <summary>Uptime: "Short" (3d 4h), "Long" (3 days, 4 hours) or "Decimal" (3.2d).</summary>
        public string UpFormat { get; set; } = "Short";
        /// <summary>Uptime: the minutes too once it's a day or more.</summary>
        public bool UpMinutes { get; set; } = false;
        /// <summary>Uptime: two lines - on a bar the time with "since …" under it, in a widget a big number with "since …" under it.</summary>
        public bool UpTwoLines { get; set; } = false;
        /// <summary>Uptime: a dot when Windows is waiting for a restart (Windows Update).</summary>
        public bool UpPendingDot { get; set; } = true;

        // ---- volume ----------------------------------------------------------------
        /// <summary>Show a speaker icon that follows the volume / mute state.</summary>
        public bool ShowIcon { get; set; } = true;
        /// <summary>"Cpu", "Memory" or "Gpu".</summary>
        public string ProcSort { get; set; } = "Cpu";
        public string LabelText { get; set; } = "";
        public bool LabelBold { get; set; } = false;

        // ---- Home Assistant / service status ---------------------------------
        /// <summary>"Row" = side by side (bars, or a wide widget), "List" = one under the other (widgets only).</summary>
        [PluginApi] public string TileLayout { get; set; } = "Row";
        /// <summary>Show the name in front of each value.</summary>
        [PluginApi] public bool TileNames { get; set; } = true;

        public List<ServiceCheck> Checks { get; set; } = new List<ServiceCheck>();
        /// <summary>Seconds between service checks.</summary>
        public int CheckSeconds { get; set; } = 30;

        // ---- spacer ----------------------------------------------------------
        /// <summary>0 = flexible (takes the free space); otherwise a fixed gap in DIPs.</summary>
        public int SpacerWidth { get; set; } = 0;

        public static ModuleConfig Create(string type)
        {
            var m = new ModuleConfig { Type = type, TextSize = "M" };     // Build 2: new bar modules start at M
            switch (type)
            {
                case ModuleTypes.Cpu: m.Style = "Meter"; break;
                case ModuleTypes.Memory: m.Style = "Meter"; m.GraphWidth = 60; break;
                case ModuleTypes.Gpu: m.Style = "Meter"; break;
                case ModuleTypes.Hwinfo: m.Style = "Text"; m.GraphWidth = 60; break;
                case ModuleTypes.Notes: m.NotePages.Add(new NotePage()); break;
                case ModuleTypes.RunBox: m.RunHotkey = "Ctrl+Alt+Space"; break;
                case ModuleTypes.Terminal: m.TermHotkey = "Ctrl+Alt+T"; break;
                case ModuleTypes.Clock:
                    // follow the system's 12/24 hour setting
                    m.Use24Hour = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.IndexOf('t') < 0;
                    break;
                case ModuleTypes.Network: m.Style = "Line"; m.GraphWidth = 50; m.RangeMax = 100; m.AutoScale = true; break;
                case ModuleTypes.Disk: m.Style = "Meter"; m.GraphWidth = 50; break;
                case ModuleTypes.Battery: m.Style = "Text"; break;
                case ModuleTypes.Volume: m.Style = "Text"; m.GraphWidth = 50; break;
                case ModuleTypes.Uptime: m.Style = "Text"; break;
                case ModuleTypes.Processes: m.Style = "Text"; break;
                case ModuleTypes.Label: m.Style = "Text"; m.LabelText = "Label"; break;
                case ModuleTypes.Services:
                    m.Checks.Add(new ServiceCheck { Name = "Router", Kind = "Ping", Target = "192.168.1.1" });
                    break;
                case ModuleTypes.Launcher: m.IconSize = 24; break;
                case ModuleTypes.StartMenu: m.IconSize = 22; break;
                case ModuleTypes.Spacer: m.SpacerWidth = 0; break;
                case ModuleTypes.NowPlaying: m.HideIfNone = false; break;
                case ModuleTypes.Downloads: m.Style = "Line"; m.GraphWidth = 50; m.RangeMax = 50; m.ShowText = true; m.AutoScale = true; break;
            }
            return m;
        }

        public static List<ModuleConfig> DefaultSet()
        {
            return new List<ModuleConfig>
            {
                Create(ModuleTypes.Taskbar),
                Create(ModuleTypes.Spacer),
                Create(ModuleTypes.Cpu),
                Create(ModuleTypes.Memory),
                Create(ModuleTypes.Tray)
            };
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Array.IndexOf(ModuleTypes.StylesFor(Type), Style) < 0) Style = "Meter";
            // the warning is on by default: modules that never had it turned on get it, at 70 % of their graph's range (battery: below 20 %)
            if (!WarnSetup && HasWarning(Type))
            {
                WarnSetup = true;
                if (WarnOn) WarnAuto = false;             // you set this one up yourself: keep your threshold
                else if (Type == ModuleTypes.Battery) { WarnOn = true; WarnAuto = false; WarnBelow = true; WarnAt = 20; }
                else { WarnOn = true; WarnAuto = true; }
            }
            SyncWarn();
            // Show folders is on by default since 0.38.105: Start menus saved before then get it once (untick it again and it stays off)
            if (!StartFoldersSetup) { StartFoldersSetup = true; StartFolders = true; }
            if (TabName == null) TabName = "";
            RunWidth = Clamp(RunWidth, 80, 800);
            if (Array.IndexOf(ModuleTypes.RunHotkeys, RunHotkey) < 0) RunHotkey = "Off";
            if (Array.IndexOf(ModuleTypes.TermHotkeys, TermHotkey) < 0) TermHotkey = "Off";
            if (TermProfile == null) TermProfile = "";
            TermWidth = Math.Max(20, Math.Min(100, TermWidth));
            TermHeight = Math.Max(15, Math.Min(95, TermHeight));
            TermWidgetHeight = Math.Max(120, Math.Min(2000, TermWidgetHeight));
            TermWidgetWidth = Math.Max(240, Math.Min(3000, TermWidgetWidth));
            NetInterface = (NetInterface ?? "").Trim();
            TileWidth = TileWidth < 40 ? 0 : Math.Min(1200, TileWidth);
            if (Array.IndexOf(ModuleTypes.NoteKinds, NoteKind) < 0) NoteKind = "Note";
            if (string.IsNullOrWhiteSpace(NoteStore)) NoteStore = "vShell";
            if (Array.IndexOf(ModuleTypes.NoteStores, NoteStore) < 0) NoteKind = "Note";      // a plugin's store (vDrop's feed): kept while it isn't added
            if (NoteFile == null) NoteFile = "";
            if (NoteFolder == null) NoteFolder = "";
            if (NoteFolderFile == null) NoteFolderFile = "";
            // 0.38.70 had the folder under File: it's its own choice now
            if (NoteStore == "File" && NoteFolder.Length > 0 && NoteFolderFile.Length == 0) { NoteStore = "Folder"; NoteFolderFile = NoteFile; }
            if (NotePages == null) NotePages = new List<NotePage>();
            NotePages.RemoveAll(delegate (NotePage p) { return p == null; });
            foreach (NotePage p in NotePages) { if (p.Title == null) p.Title = ""; if (p.Text == null) p.Text = ""; }
            if (Type == ModuleTypes.Notes && NotePages.Count == 0) NotePages.Add(new NotePage());
            NoteIndex = Math.Max(0, Math.Min(Math.Max(0, NotePages.Count - 1), NoteIndex));
            if (string.IsNullOrWhiteSpace(NotePaper)) NotePaper = "Theme";
            if (string.IsNullOrWhiteSpace(NoteFont)) NoteFont = "Theme";      // Theme / Mono / Handwritten, or an installed font's name
            if (Array.IndexOf(ModuleTypes.NoteDoneModes, NoteDone) < 0) NoteDone = "Strike";
            if (Array.IndexOf(ModuleTypes.NoteEditModes, NoteEditOn) < 0) NoteEditOn = "Click";
            NoteWidth = Clamp(NoteWidth, 120, 1200);
            NoteHeight = Clamp(NoteHeight, 60, 1600);
            NoteLogMax = Clamp(NoteLogMax, 5, 500);
            if (StyleSizes == null) StyleSizes = new Dictionary<string, GraphSize>();
            if (Zone == null || (Zone.Length > 0 && Array.IndexOf(ModuleTypes.Zones, Zone) < 0)) Zone = "";
            GraphWidth = Clamp(GraphWidth, 20, 600);
            if (TextSize != "S" && TextSize != "M" && TextSize != "L") TextSize = "";
            GraphHeightPercent = GraphHeightPercent <= 0 ? 0 : Clamp(GraphHeightPercent, 20, 100);     // 0 = automatic
            if (Array.IndexOf(ModuleTypes.GraphLines, GraphLine) < 0) GraphLine = "Normal";
            if (Array.IndexOf(ModuleTypes.GraphFills, GraphFill) < 0) GraphFill = "Light";
            GraphRadius = Clamp(GraphRadius, 0, 12);
            if (Array.IndexOf(ModuleTypes.GraphSpans, GraphSpan) < 0) GraphSpan = 60;
            if (LoadColor1 == null) LoadColor1 = "";
            if (LoadColor2 == null) LoadColor2 = "";
            if (LoadColor3 == null) LoadColor3 = "";
            GpuIndex = Clamp(GpuIndex, 1, 8);
            MaxButtonWidth = Clamp(MaxButtonWidth, 60, 400);
            if (Array.IndexOf(ModuleTypes.TaskGroupings, TaskGrouping) < 0) TaskGrouping = "Always";
            if (Pins == null) Pins = new List<PinnedApp>();
            Pins.RemoveAll(delegate (PinnedApp p) { return p == null || string.IsNullOrWhiteSpace(p.Path); });
            foreach (PinnedApp p in Pins) { if (p.Name == null) p.Name = ""; if (p.Args == null) p.Args = ""; if (p.AppId == null) p.AppId = ""; }
            if (Array.IndexOf(ModuleTypes.TaskLabels, TaskLabel) < 0) TaskLabel = "Icon";
            TaskPreviewSize = Clamp(TaskPreviewSize, 120, 480);
            TaskIconSize = TaskIconSize <= 0 ? 0 : Clamp(TaskIconSize, 16, 48);
            IconSize = Clamp(IconSize, 16, 48);
            if (LaunchLayout != "Columns" && LaunchLayout != "Rows") LaunchLayout = "Row";
            LaunchCount = Clamp(LaunchCount, 1, 12);
            DockMagnifyPercent = Clamp(DockMagnifyPercent, 110, 250);
            DockGlowStrength = Clamp(DockGlowStrength, 10, 100);
            DockGlowSize = Clamp(DockGlowSize, 120, 300);
            if (DockLift && DockMagnify) DockLift = false;      // one or the other
            SpacerWidth = Clamp(SpacerWidth, 0, 600);
            Decimals = Clamp(Decimals, 0, 4);
            if (ClockWeekStart != "Monday" && ClockWeekStart != "Sunday") ClockWeekStart = "Windows";
            if (ClockLayout != "TimeOverDate" && Array.IndexOf(ModuleTypes.ClockLayouts, ClockLayout) < 0) ClockLayout = "OneLine";
            if (Array.IndexOf(ModuleTypes.DateStyles, DateStyle) < 0) DateStyle = "Medium";
            if (double.IsNaN(RangeMin) || double.IsInfinity(RangeMin)) RangeMin = 0;
            if (double.IsNaN(RangeMax) || double.IsInfinity(RangeMax)) RangeMax = 100;
            if (RangeMax <= RangeMin) RangeMax = RangeMin + 1;
            Color = ThemeSlots.Migrate(Color);
            Color2 = ThemeSlots.Migrate(Color2);
            if (string.IsNullOrWhiteSpace(WeatherSource)) WeatherSource = "OpenMeteo";      // or a plugin's source ("HomeAssistant"), kept while it isn't added
            if (WeatherPlace == null) WeatherPlace = "";
            if (WeatherEntity == null) WeatherEntity = "";
            if (double.IsNaN(WeatherLat) || double.IsInfinity(WeatherLat)) WeatherLat = 0;
            if (double.IsNaN(WeatherLon) || double.IsInfinity(WeatherLon)) WeatherLon = 0;
            WeatherMinutes = Clamp(WeatherMinutes, 5, 120);
            WeatherForecastDays = Clamp(WeatherForecastDays, 2, 7);
            WeatherForecastSpacing = Clamp(WeatherForecastSpacing, 0, 60);
            MediaWidth = Clamp(MediaWidth, 60, 600);
            if (Array.IndexOf(ModuleTypes.MediaProgressModes, MediaProgress) < 0) MediaProgress = "Under text";
            if (Old != null) { PluginStore.MoveOld(Type, Old, this); Old = null; }
            if (Array.IndexOf(ModuleTypes.TaskAnims, TaskAnim) < 0) TaskAnim = "Stretch";      // includes the styles taken out in 0.36.1
            if (Array.IndexOf(ModuleTypes.TaskAnimBounces, TaskAnimBounce) < 0) TaskAnimBounce = "Soft";
            if (Array.IndexOf(ModuleTypes.TaskAttentions, TaskAttention) < 0) TaskAttention = "Pulse";
            TaskAnimMs = Clamp(TaskAnimMs, 60, 1000);
            if (string.IsNullOrWhiteSpace(NfSource)) NfSource = "All";      // a plugin's source is kept while the plugin isn't added
            NfCount = Clamp(NfCount, 1, 50);

            if (double.IsNaN(FontSize) || FontSize <= 0) FontSize = 12;
            FontSize = Math.Max(6, Math.Min(48, FontSize));
            if (Type != ModuleTypes.Battery) WarnBelow = false;     // "warn below" is a battery setting only
            if (TempSensor == null) TempSensor = "";
            if (SensorKey == null) SensorKey = "";
            if (SensorTitle == null) SensorTitle = "";
            if (SensorUnit == null) SensorUnit = "";
            if (IconPath == null) IconPath = "";
            if (PinnedKeys == null) PinnedKeys = new List<string>();
            if (TrayOrder == null) TrayOrder = new List<string>();
            if (TrayIconColour == "One colour") TrayIconColour = "Tint";        // 0.38.101 had a One colour mode
            else if (TrayIconColour == "Original" && TrayTintColour == "Text" && TrayTint == 50) { TrayTintColour = "Main Accent"; TrayTint = 40; }   // 0.38.101's untouched defaults
            if (Array.IndexOf(ModuleTypes.TrayIconColours, TrayIconColour) < 0) TrayIconColour = "Original";
            if (Array.IndexOf(ModuleTypes.TrayTintColours, TrayTintColour) < 0) TrayTintColour = "Text";
            TrayTint = Math.Max(10, Math.Min(90, TrayTint));
            if (Type == ModuleTypes.Tray && !TrayNetworkPinned)
            {
                TrayNetworkPinned = true;
                if (!PinnedKeys.Contains(TrayHost.OwnNetworkKey)) PinnedKeys.Add(TrayHost.OwnNetworkKey);     // on the bar, like Windows' own
            }
            if (DiskDrive == null) DiskDrive = "";
            if (Array.IndexOf(ModuleTypes.DiskModes, DiskMode) < 0) DiskMode = "Space";
            if (Array.IndexOf(ModuleTypes.ProcSorts, ProcSort) < 0) ProcSort = "Cpu";
            if (Array.IndexOf(ModuleTypes.UpFroms, UpFrom) < 0) UpFrom = "Last restart";
            if (Array.IndexOf(ModuleTypes.UpFormats, UpFormat) < 0) UpFormat = "Short";
            if (Array.IndexOf(ModuleTypes.TileLayouts, TileLayout) < 0) TileLayout = "Row";
            VolumeStep = Clamp(VolumeStep, 1, 20);
            CpuSmooth = Clamp(CpuSmooth, 0, 10);
            if (Array.IndexOf(ModuleTypes.VolumeDevices, VolumeDevice) < 0) VolumeDevice = "Off";
            if (VolumeMixerLayout != "Columns") VolumeMixerLayout = "Rows";
            if (VolumeMixerDevices == null) VolumeMixerDevices = new List<VolumeDeviceItem>();
            VolumeMixerDevices.RemoveAll(delegate (VolumeDeviceItem d) { return d == null || string.IsNullOrEmpty(d.Id); });
            if (VolumeDeviceId == null) VolumeDeviceId = "";
            if (VolumeDeviceName == null) VolumeDeviceName = "";
            if (Array.IndexOf(ModuleTypes.VolumeMutedStyles, VolumeMuted) < 0) VolumeMuted = "Text";
            if (Array.IndexOf(ModuleTypes.VolumeClicks, VolumeClick) < 0) VolumeClick = "Mixer";
            ProcCount = Clamp(ProcCount, 1, 30);
            ProcShow = Clamp(ProcShow, 1, 5);
            ProcScale = Clamp(ProcScale, 50, 200);
            ProcSpacing = Clamp(ProcSpacing, 0, 30);
            ProcWidth = ProcWidth <= 0 ? 0 : Clamp(ProcWidth, 40, 600);
            CheckSeconds = Clamp(CheckSeconds, 5, 3600);
            if (LabelText == null) LabelText = "";

            if (Array.IndexOf(ModuleTypes.StartIcons, StartIcon) < 0) StartIcon = !string.IsNullOrWhiteSpace(IconPath) ? "Custom" : "vShell";

            if (Checks == null) Checks = new List<ServiceCheck>();
            Checks.RemoveAll(delegate (ServiceCheck c) { return c == null; });
            foreach (var c in Checks)
            {
                if (c.Name == null) c.Name = "";
                if (c.Target == null) c.Target = "";
                if (Array.IndexOf(ModuleTypes.ServiceKinds, c.Kind) < 0) c.Kind = "Http";
            }
            if (Entries == null) Entries = new List<LauncherEntry>();
            CleanEntries(Entries, 1);
            StartRecentCount = Clamp(StartRecentCount, 3, 20);
            if (Array.IndexOf(ModuleTypes.StartStyles, StartStyle) < 0) StartStyle = "Classic";
        }

        /// <summary>depth: 1 = the launcher itself; groups at LauncherEntry.MaxDepth keep only their shortcuts.</summary>
        static void CleanEntries(List<LauncherEntry> list, int depth)
        {
            list.RemoveAll(delegate (LauncherEntry e) { return e == null; });
            foreach (var e in list)
            {
                if (e.Name == null) e.Name = "";
                if (e.Path == null) e.Path = "";
                if (e.Args == null) e.Args = "";
                if (e.IconPath == null) e.IconPath = "";
                if (e.BookmarkName == null) e.BookmarkName = "";
                if (e.IconGlyph == null) e.IconGlyph = "";
                if (e.Children == null) e.Children = new List<LauncherEntry>();
                if (e.SyncFolders == null) e.SyncFolders = new List<string>();
                if (e.SyncBookmarks == null) e.SyncBookmarks = new List<BookmarkFolder>();
                e.SyncBookmarks.RemoveAll(delegate (BookmarkFolder b) { return b == null || string.IsNullOrEmpty(b.File); });
                if (!e.IsGroup) { e.Children.Clear(); continue; }
                if (depth >= LauncherEntry.MaxDepth) e.Children.RemoveAll(delegate (LauncherEntry c) { return c != null && c.IsGroup; });
                CleanEntries(e.Children, depth + 1);
            }
        }

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }
    }
}
