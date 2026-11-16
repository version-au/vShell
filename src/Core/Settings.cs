using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vShell.Core
{
    /// <summary>Wallpaper for one monitor (Monitor "*" = the one for all, in "Same on all" / "Span across all").</summary>
    public sealed class MonitorWallpaper
    {
        /// <summary>Monitor device name, e.g. \\.\DISPLAY2 ("*" = all monitors).</summary>
        public string Monitor { get; set; } = "";
        /// <summary>The monitor itself (MonitorInfo.Key): it gets its picture back after a reconnect, whatever DISPLAYn it is.</summary>
        public string MonitorKey { get; set; } = "";
        public string Path { get; set; } = "";
        /// <summary>"Fill", "Fit", "Stretch", "Center" or "Tile".</summary>
        public string Fit { get; set; } = "Fill";
        /// <summary>Behind a Fit / Center picture: "Blur", "Black", "Theme", a ready-made gradient, "Solid" (Color1) or "Gradient" (Color1 -> Color2 at Angle).</summary>
        public string Background { get; set; } = "Blur";
        public string Color1 { get; set; } = "#FF1E2230";
        public string Color2 { get; set; } = "#FF3A4466";
        public int Angle { get; set; } = 135;
        /// <summary>Slideshow: a folder of pictures (empty = off), how often, in order or shuffled; the one showing and when the next is due.</summary>
        public string SlideFolder { get; set; } = "";
        public int SlideMinutes { get; set; } = 30;
        public bool SlideShuffle { get; set; } = true;
        public string SlideCurrent { get; set; } = "";
        public DateTime SlideNext { get; set; } = DateTime.MinValue;
    }

    /// <summary>A monitor's Windows wallpaper from before vShell set it (to put back).</summary>
    public sealed class SavedWallpaper
    {
        /// <summary>Windows' id for the monitor (its device path).</summary>
        public string MonitorId { get; set; } = "";
        public string Path { get; set; } = "";
    }

    /// <summary>A named, user-saved set of theme colours (and optionally font).</summary>
    public sealed class ThemePreset
    {
        public string Name { get; set; } = "";
        public string Background { get; set; } = "#E61E1E2E";
        public string Foreground { get; set; } = "#FFCDD6F4";
        [JsonPropertyName("Accent")]   // the saved name from before the rename
        public string MainAccent { get; set; } = "#FF89B4FA";
        public string FontFamily { get; set; } = "";
        // ---- extended theme (all optional; older presets / files simply leave the defaults) ----
        public string BgStyle { get; set; } = "Solid";
        /// <summary>The second (gradient) colour.</summary>
        public string Background2 { get; set; } = "#E6313244";
        public int GradientAngle { get; set; } = 90;
        /// <summary>Secondary Accent: warnings, second graph series, attention (one of the separator dots).</summary>
        [JsonPropertyName("Spare")]   // the saved name from before the rename
        public string SecondaryAccent { get; set; } = "#FFF5C2E7";
        /// <summary>Background blur ("Off", "Blur", "Acrylic"); presets saved before it existed have none.</summary>
        public string Blur { get; set; } = "Off";
    }

    public sealed class BarSettings
    {
        // ---- theme (shared by all bars) --------------------------------------
        public string Background { get; set; } = "#E61E1E2E";   // #AARRGGBB
        public string Foreground { get; set; } = "#FFCDD6F4";
        [JsonPropertyName("Accent")]   // the saved name from before the rename
        public string MainAccent { get; set; } = "#FF89B4FA";
        public string FontFamily { get; set; } = "Segoe UI";

        // ---- extended theme -----------------------------------------------------
        /// <summary>"Solid" or "Gradient" (Background to Background2).</summary>
        public string BgStyle { get; set; } = "Solid";
        /// <summary>The second colour of the gradient background.</summary>
        public string Background2 { get; set; } = "#E6313244";
        /// <summary>0 = left to right, 90 = top to bottom, 180 = right to left, 270 = bottom to top.</summary>
        public int GradientAngle { get; set; } = 90;
        /// <summary>Bars along the bottom edge show the gradient upside down (mirrored), so top and bottom bars mirror each other.</summary>
        public bool MirrorBottomGradient { get; set; } = true;
        /// <summary>Secondary Accent: warnings, second graph series, attention (one of the separator dots).</summary>
        [JsonPropertyName("Spare")]   // the saved name from before the rename
        public string SecondaryAccent { get; set; } = "#FFF5C2E7";
        /// <summary>Wallpaper Theme: background, gradient, text, Main and Secondary Accent are all worked out from the main monitor's
        /// wallpaper (the colours above are kept for when it is off). The background opacity still comes from Background.</summary>
        public bool WallpaperTheme { get; set; } = false;
        /// <summary>Background blur behind every bar and widget: "Off", "Blur" or "Acrylic" (frosted). The background opacity sets how much
        /// of the theme colour lies over it.</summary>
        public string BlurMode { get; set; } = "Off";
        /// <summary>Widgets snap to each other, the screen edges and the bars while dragged (Alt = place freely).</summary>
        public bool WidgetSnap { get; set; } = true;
        /// <summary>Gap between widgets the snapping keeps (DIPs); also used by "Distribute with snap gap".</summary>
        public int WidgetGap { get; set; } = 12;
        /// <summary>Gap the snapping keeps from the screen edges and the bars (DIPs).</summary>
        public int WidgetEdgeGap { get; set; } = 12;
        /// <summary>Changing either gap moves the snapped (linked) widgets to the new gap.</summary>
        public bool RespaceSnapped { get; set; } = true;
        /// <summary>0 = widget positions / sizes from before 0.23 (the window corner, outline included): converted once on load.
        /// 1 = converted, snap links not looked for yet. 2 = done. A new settings file starts at 2.</summary>
        public int WidgetBoxVersion { get; set; } = 0;
        /// <summary>0.34.1 set every taskbar's moving line to Stretch, 400 ms, Soft, background along - once (true = done).</summary>
        public bool TaskAnimDefaultsSet { get; set; } = false;
        /// <summary>Show the vShell splash card while starting.</summary>
        public bool ShowSplash { get; set; } = true;
        /// <summary>The welcome guide (start-up, theme, layout, plugins) is still to be shown: set on a first start, cleared when it is finished,
        /// skipped or closed. Settings > About shows the guide again any time.</summary>
        public bool WelcomePending { get; set; } = false;

        /// <summary>0.18.0 only: pins shared by every taskbar. Moved into the first Taskbar module's own Pins on load, never written again.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PinnedApp> PinnedApps { get; set; }

        // ---- updates (the update site, see vSuite.Updater) ---------------------------
        /// <summary>Check for a new version a minute after start-up and about once a day.</summary>
        public bool UpdateAuto { get; set; } = true;
        /// <summary>Offer pre-releases too.</summary>
        public bool UpdatePre { get; set; } = false;
        /// <summary>Keep a copy of the settings once a day (Settings > System > Backups; the last 7 are kept).</summary>
        public bool BackupAuto { get; set; } = true;
        /// <summary>Plugins: download a newer build by itself when one is in a release (used from the next start).</summary>
        public bool PluginAutoUpdate { get; set; } = true;
        /// <summary>A version the user chose to skip ("0.16.1").</summary>
        public string UpdateSkip { get; set; } = "";
        public DateTime UpdateLastCheck { get; set; } = DateTime.MinValue;
        /// <summary>Older settings: "accent from the wallpaper" / "tint the background". Either one turns on the Wallpaper Theme.</summary>
        [JsonPropertyName("AccentFromWallpaper"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyAccentFromWallpaper { get; set; }
        [JsonPropertyName("BackgroundFromWallpaper"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyBackgroundFromWallpaper { get; set; }

        /// <summary>Themes saved by the user (colours + font).</summary>
        public List<ThemePreset> UserPresets { get; set; } = new List<ThemePreset>();

        // ---- themes, bar styles, widget styles ------------------------------------------
        /// <summary>The bar style in use (a look per bar layout); null in files from before styles = made from the bars on load.</summary>
        public BarStyle BarStyle { get; set; }
        [PluginApi] public WidgetStyle WidgetStyle { get; set; }
        /// <summary>Names of the bar / widget style preset in use ("" = changed since / not a preset).</summary>
        public string BarStyleName { get; set; } = "";
        public string WidgetStyleName { get; set; } = "";
        public List<BarStyle> UserBarStyles { get; set; } = new List<BarStyle>();
        public List<WidgetStyle> UserWidgetStyles { get; set; } = new List<WidgetStyle>();
        public List<LookTheme> UserThemes { get; set; } = new List<LookTheme>();

        // ---- wallpaper ----------------------------------------------------------
        /// <summary>true = vShell builds the desktop wallpaper from one image per monitor.</summary>
        public bool PerMonitorWallpaper { get; set; } = false;
        public List<MonitorWallpaper> Wallpapers { get; set; } = new List<MonitorWallpaper>();
        /// <summary>"Each" (a picture per monitor), "Same" (one picture on every monitor) or "Span" (one picture across them all).</summary>
        public string WallpaperMode { get; set; } = "Each";
        /// <summary>0.38.30: Windows' per-monitor wallpapers from before vShell set them, and Windows' fit (to put back). null = not saved.</summary>
        public List<SavedWallpaper> WallpaperOriginals { get; set; }
        public int WallpaperOriginalPosition { get; set; } = 4;
        /// <summary>Windows' own wallpaper settings from before we changed them (to restore). null = not saved.</summary>
        public string OriginalWallpaper { get; set; }
        public string OriginalWallpaperStyle { get; set; }
        public string OriginalWallpaperTile { get; set; }

        // ---- run box (shared by every run box) -------------------------------------
        /// <summary>!bang commands: keyword (without the !), name and a URL with {q} where the search goes.</summary>
        public List<RunBang> RunBangs { get; set; }
        // ---- the terminal before vTerm (0.38.194): kept so vTerm can bring them over the first time it starts ----
        /// <summary>Terminal profiles (null = the ones this PC has: PowerShell, Command Prompt, WSL ...).</summary>
        public List<TermProfile> TermProfiles { get; set; }
        /// <summary>Terminal text: font (empty = Cascadia Mono, then Consolas) and size.</summary>
        public string TermFont { get; set; } = "";
        public int TermFontSize { get; set; } = 13;
        /// <summary>Terminal colours: "Theme" (from the vShell theme) or a named scheme (vTerm's).</summary>
        public string TermColors { get; set; } = "Theme";
        /// <summary>Terminal: the text area is see-through (the card's blur / theme colour shows); off = the theme colour, solid.</summary>
        public bool TermSeeThrough { get; set; } = true;
        /// <summary>Terminal: saved SSH connections (shared by every terminal).</summary>
        public List<TermConnection> TermConnections { get; set; } = new List<TermConnection>();
        /// <summary>Terminal: the side panel (connections / files) is open.</summary>
        public bool TermPanelOpen { get; set; } = true;
        /// <summary>Terminal: where a popped-out tab's window was last ("x,y,w,h" in screen pixels; "" = centred on the monitor).</summary>
        public string TermPopRect { get; set; } = "";
        /// <summary>Terminal: popped-out windows are kept on top of other windows.</summary>
        public bool TermPopTopmost { get; set; }
        /// <summary>Where a plain search goes: "Google", "DuckDuckGo", "Bing", "Custom" (RunCustomEngine) or "None" (then only apps / paths / commands).</summary>
        public string RunEngine { get; set; } = "Google";
        /// <summary>The "Custom" search engine's address with {q} for the search (e.g. a SearXNG server).</summary>
        public string RunCustomEngine { get; set; } = "";
        /// <summary>A !bang that is not in the list is handed to DuckDuckGo (which knows thousands).</summary>
        public bool RunUnknownToDdg { get; set; } = true;
        /// <summary>The last things run (newest first), for the Up / Down keys and the suggestions.</summary>
        public List<string> RunHistory { get; set; } = new List<string>();

        /// <summary>The pins shared by the taskbars with "Same pins on every taskbar" on.</summary>
        public List<PinnedApp> SharedPins { get; set; } = new List<PinnedApp>();

        /// <summary>
        /// The plugins added (PluginStore ids: "komodo"). null = settings from before plugins: the ones that were in use are added for you
        /// (see Normalize).
        /// </summary>
        public List<string> Plugins { get; set; }
        /// <summary>The plugins already added for you if you used them (once each - one you removed stays removed).</summary>
        public List<string> PluginsOffered { get; set; }

        /// <summary>A bar or widget has a module of this type.</summary>
        [PluginApi] public bool UsesModule(string type)
        {
            if (Bars != null) foreach (BarPlacement b in Bars) if (b != null && b.Modules != null && b.Modules.Exists(delegate (ModuleConfig m) { return m != null && m.Type == type; })) return true;
            if (Widgets != null) foreach (WidgetSettings w in Widgets) if (w != null && w.Modules != null && w.Modules.Exists(delegate (ModuleConfig m) { return m != null && m.Type == type; })) return true;
            return false;
        }

        /// <summary>The plugin integrations' own settings, by plugin id ("komodo") - see SettingsBag. Use Integration(id).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, SettingsBag> Integrations { get; set; }
        /// <summary>Settings this version doesn't have any more (an integration's before it went into a plugin): moved into Integrations on load, then dropped.</summary>
        [JsonExtensionData] public Dictionary<string, JsonElement> Old { get; set; }

        /// <summary>A plugin integration's settings (made empty the first time).</summary>
        [PluginApi] public SettingsBag Integration(string id)
        {
            if (Integrations == null) Integrations = new Dictionary<string, SettingsBag>();
            SettingsBag b;
            if (!Integrations.TryGetValue(id, out b) || b == null) Integrations[id] = b = new SettingsBag();
            return b;
        }

        /// <summary>A plugin integration's setting, without making its settings if it has none yet.</summary>
        [PluginApi] public T IntegrationValue<T>(string id, string key, T def)
        {
            SettingsBag b;
            return Integrations != null && Integrations.TryGetValue(id, out b) && b != null ? b.Get(key, def) : def;
        }

        /// <summary>Times outside the Clock module (notifications, last checked, added …): 24-hour (14:05) instead of 12-hour (2:05 PM).</summary>
        public bool Use24HourTimes { get; set; } = false;

        /// <summary>Where temperatures, fans, voltages … come from: "HWiNFO" (its Gadget values) or "LHM" (LibreHardwareMonitor's web server).</summary>
        public string SensorSource { get; set; } = "HWiNFO";
        /// <summary>The port LibreHardwareMonitor's web server listens on (Options > Remote Web Server).</summary>
        public int LhmPort { get; set; } = 8085;

        // ---- notifications (all sources) -------------------------------------
        /// <summary>Where the cards appear: Bottom right / Bottom left / Top right / Top left.</summary>
        public string NotifyCorner { get; set; } = "Bottom right";
        /// <summary>The kind (Normal / Important / Alert) chosen for each sort of notification (Notify.Kinds); missing = its default.</summary>
        public Dictionary<string, string> NotifyKinds { get; set; } = new Dictionary<string, string>();
        /// <summary>Which monitor: Main or Mouse (the one the mouse is on).</summary>
        public string NotifyMonitor { get; set; } = "Main";
        /// <summary>The chosen monitor's stable key (its device path), so it's found again when Windows renumbers the displays.</summary>
        public string NotifyMonitorKey { get; set; } = "";
        /// <summary>Seconds a card stays (the mouse over it keeps it).</summary>
        public int NotifySeconds { get; set; } = 8;
        /// <summary>Important notifications stay until closed.</summary>
        public bool NotifyKeepImportant { get; set; } = true;
        /// <summary>Alerts (errors, something down, too hot) stay until closed.</summary>
        public bool NotifyKeepAlerts { get; set; } = true;
        /// <summary>By source (a card per source, stacked) or One card (everything together).</summary>
        public string NotifyGroup { get; set; } = "By source";
        public bool NotifySound { get; set; } = false;
        /// <summary>Notification cards and the history window get the Appearance background effect (blur / frosted) when it is on.</summary>
        public bool NotifyBackdrop { get; set; } = true;
        /// <summary>Hold them while a full-screen app (game, video) is in front; they show when it closes.</summary>
        public bool NotifyHoldFullScreen { get; set; } = true;
        public bool NotifyQuiet { get; set; } = false;
        public string NotifyQuietFrom { get; set; } = "23:00";
        public string NotifyQuietTo { get; set; } = "07:00";
        /// <summary>Also hold them while Windows says not to disturb (Do not disturb / presentation mode / a D3D full-screen game).</summary>
        public bool NotifyFollowWindows { get; set; } = true;
        /// <summary>How many the history keeps.</summary>
        public int NotifyHistoryMax { get; set; } = 500;
        /// <summary>0.38.29: the history went from 100 to 500 by default (installs still on 100 are moved up once).</summary>
        public bool NotifyHistoryUp { get; set; }
        /// <summary>The same notification again (same source, title and level) within this many minutes: one entry with a count ("×3"),
        /// its card updated instead of another one. 0 = off.</summary>
        public int NotifyCollapseMinutes { get; set; } = 10;

        /// <summary>Listen for notifications sent by Home Assistant (or anything else) to http://this-pc:port/notify.</summary>
        public bool NotifyListen { get; set; } = false;
        public int NotifyListenPort { get; set; } = 8765;
        /// <summary>The token a sender must give (stored encrypted).</summary>
        public string NotifyListenToken { get; set; } = "";
        /// <summary>Match Windows to vShell's theme: light / dark mode and / or the accent colour (your own are put back when it's off).</summary>
        public bool WinMatch { get; set; } = true;
        /// <summary>0.38.12: Match Windows became on by default - turned on once for installs that had it off from before.</summary>
        public bool WinMatchSetup { get; set; }
        public bool WinMatchMode { get; set; } = true;
        public bool WinMatchAccent { get; set; } = true;
        // Service status
        public bool NotifyServices { get; set; } = false;
        /// <summary>Wait this many seconds before saying a service is down; 0 = after two failed checks, as before.</summary>
        public int NotifySvcDelay { get; set; } = 0;
        // System (0 = off)
        public int NotifyCpuTemp { get; set; } = 0;
        public int NotifyGpuTemp { get; set; } = 0;
        /// <summary>A temperature must stay over its limit this many seconds.</summary>
        public int NotifyTempSeconds { get; set; } = 30;
        /// <summary>Old setting (a drive has less free space than this %): moved to NotifyDiskFull when loaded.</summary>
        public int NotifyDiskFree { get; set; } = 0;
        /// <summary>A drive is fuller than this % (0 = off) - like the Proxmox storage one.</summary>
        public int NotifyDiskFull { get; set; } = 0;
        public int NotifyBattery { get; set; } = 0;
        public int NotifyMemory { get; set; } = 0;
        /// <summary>The same system warning at most once every this many minutes.</summary>
        public int NotifySysCooldown { get; set; } = 10;

        // Weather
        public bool NotifyRain { get; set; } = false;
        /// <summary>Rain chance (%) that counts.</summary>
        public int NotifyRainChance { get; set; } = 60;

        // ---- widgets ------------------------------------------------------------
        public List<WidgetSettings> Widgets { get; set; } = new List<WidgetSettings>();

        // ---- behaviour ------------------------------------------------------
        public bool HideWindowsTaskbar { get; set; } = true;
        /// <summary>Show tray icons' balloon notifications ("Backup complete") next to the tray.</summary>
        public bool ShowTrayBalloons { get; set; } = true;
        /// <summary>vShell's own icon in the system tray (Settings, the run box, Restart, Exit …).</summary>
        public bool ShowTrayIcon { get; set; } = true;

        public List<BarPlacement> Bars { get; set; } = new List<BarPlacement>();

        // ---- v0.1 global settings; only read to migrate old files, never written again ----
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BarHeight { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FullWidth { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? TaskButtonMaxWidth { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowTaskbar { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowSystemTray { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowCpu { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowMemory { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? TaskbarOnlyCurrentMonitor { get; set; }
        // ---- v0.2 global appearance; now per bar. Read once to migrate, then never written again ----
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? FontSize { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? CornerRadius { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? EdgeMargin { get; set; }

        // ------------------------------------------------------------------
        [PluginApi] public static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vShell"); }
        }
        public static string ConfigPath { get { return Path.Combine(ConfigDir, "settings.json"); } }
        public static string LogPath { get { return Path.Combine(ConfigDir, "vshell.log"); } }

        internal static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

        public static BarSettings Load()
        {
            PluginStore.LoadKnown();      // plugins added from files, this release's list (the settings' moves and InUse look in it)
            BarSettings s = null;
            try
            {
                // renamed from SharpBarWin: carry the old settings over once
                string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SharpBarWin", "settings.json");
                if (!File.Exists(ConfigPath) && File.Exists(old))
                {
                    Directory.CreateDirectory(ConfigDir);
                    File.Copy(old, ConfigPath);
                }
            }
            catch (Exception ex) { Log.Write("Settings migrate failed: " + ex.Message); }
            bool there = false;
            // a file another program has open for a moment (a sync or backup tool, a virus scanner) isn't a broken one: try again
            for (int tries = 0; tries < 6 && s == null; tries++)
            {
                try
                {
                    there = File.Exists(ConfigPath);
                    if (there)
                        s = JsonSerializer.Deserialize<BarSettings>(File.ReadAllText(ConfigPath), JsonOpts);
                    break;
                }
                catch (IOException ex) when (tries < 5) { Log.Write("Settings load: " + ex.Message + " - trying again"); System.Threading.Thread.Sleep(250); }
                catch (Exception ex) { Log.Write("Settings load failed: " + ex.Message); break; }
            }
            // there but unreadable: never start from defaults over it - keep it aside and use the newest good backup
            if (s == null && there)
            {
                s = SettingsBackup.Recover();
                // not even a copy could be kept: settings.json is left alone, and nothing may be saved over it this run
                if (s == null && SettingsBackup.BrokenCopy == null)
                {
                    SettingsBackup.Unreadable = true;
                    SettingsBackup.SaveBlocked = true;
                    Log.Write("Settings: settings.json can't be read and couldn't be copied aside - running on new settings, saving nothing over it");
                }
            }
            if (s == null) s = new BarSettings { WidgetBoxVersion = 2 };      // first start: nothing to convert
            if (s.WidgetBoxVersion < 1) s.MigrateWidgetBoxes();
            s.Normalize();
            return s;
        }

        /// <summary>
        /// 0.23: a widget's position and size are its box (inside the outline) instead of the window (shadow margin and outline included),
        /// so outline and shadow changes no longer move it. The old window corner sat max(shadow ? 10 : 0, outline) DIPs outside the box;
        /// the old fixed size included the outline on both sides.
        /// </summary>
        public void MigrateWidgetBoxes()
        {
            if (Widgets != null)
                foreach (WidgetSettings w in Widgets)
                {
                    if (w == null) continue;
                    double bw = w.ShowBorder ? Math.Max(0.5, Math.Min(12, double.IsNaN(w.BorderThickness) ? 1 : w.BorderThickness)) : 0;
                    double pad = Math.Max(w.ShowShadow ? 10 : 0, bw);
                    MonitorInfo mon = MonitorHelper.FindOrPrimary(w.Monitor);
                    double scale = MonitorHelper.ScaleOf(mon);
                    int inset = (int)Math.Round(pad * scale);
                    w.OffsetX += inset;
                    w.OffsetY += inset;
                    int b2 = (int)Math.Round(2 * bw);
                    if (w.Width > 0) w.Width = Math.Max(80, w.Width - b2);
                    if (w.Height > 0) w.Height = Math.Max(40, w.Height - b2);
                }
            WidgetBoxVersion = 1;
            Log.Write("Widgets: positions and sizes converted to the box (0.23)");
        }

        public void Save()
        {
            if (SettingsBackup.SaveBlocked) return;      // a restore is waiting for the restart: don't write the old settings over it
            try
            {
                Directory.CreateDirectory(ConfigDir);
                SettingsBackup.Daily(BackupAuto);
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
                File.Move(tmp, ConfigPath, true);      // replaces the old file in one step: there is never a moment without settings.json
            }
            catch (Exception ex) { Log.Write("Settings save failed: " + ex.Message); }
        }

        public void Normalize()
        {
            FontFamily = "Segoe UI";     // Build 2: the only font
            if (SensorSource != "LHM") SensorSource = "HWiNFO";
            if (LhmPort < 1 || LhmPort > 65535) LhmPort = 8085;
            if (BgStyle != "Gradient") BgStyle = "Solid";
            if (Array.IndexOf(BlurModes, BlurMode) < 0) BlurMode = "Off";
            if (string.IsNullOrWhiteSpace(Background2)) Background2 = "#E6313244";
            GradientAngle = ((GradientAngle % 360) + 360) % 360;
            if (string.IsNullOrWhiteSpace(SecondaryAccent)) SecondaryAccent = "#FFF5C2E7";
            if (LegacyAccentFromWallpaper == true || LegacyBackgroundFromWallpaper == true) WallpaperTheme = true;
            LegacyAccentFromWallpaper = null; LegacyBackgroundFromWallpaper = null;
            if (Bars == null) Bars = new List<BarPlacement>();
            WidgetGap = Math.Max(0, Math.Min(100, WidgetGap));
            WidgetEdgeGap = Math.Max(0, Math.Min(100, WidgetEdgeGap));

            if (UpdateSkip == null) UpdateSkip = "";
            if (UserBarStyles == null) UserBarStyles = new List<BarStyle>();
            if (UserWidgetStyles == null) UserWidgetStyles = new List<WidgetStyle>();
            if (UserThemes == null) UserThemes = new List<LookTheme>();
            // bars are at least 30 high (0.24.9): saved bar styles - on their own, in themes and the one in use - too
            var styles = new List<BarStyle>(UserBarStyles);
            foreach (LookTheme lt in UserThemes) if (lt != null && lt.Bars != null) styles.Add(lt.Bars);
            if (BarStyle != null) styles.Add(BarStyle);
            foreach (BarStyle bs in styles)
                if (bs != null)
                    foreach (BarLook l in new[] { bs.Classic, bs.Floating, bs.Dock, bs.Minimal })
                        if (l != null && l.Height < 30) l.Height = 30;
            if (BarStyleName == null) BarStyleName = "";
            if (WidgetStyleName == null) WidgetStyleName = "";
            if (Widgets == null) Widgets = new List<WidgetSettings>();
            Styles.Migrate(this);
            // pins are per taskbar now: the old shared list goes to the first taskbar
            if (PinnedApps != null)
            {
                if (PinnedApps.Count > 0)
                {
                    bool moved = false;
                    foreach (BarPlacement b in Bars)
                    {
                        if (moved || b == null || b.Modules == null) continue;
                        foreach (ModuleConfig m in b.Modules)
                            if (m != null && m.Type == ModuleTypes.Taskbar)
                            {
                                if (m.Pins == null) m.Pins = new List<PinnedApp>();
                                m.Pins.AddRange(PinnedApps);
                                moved = true;
                                break;
                            }
                    }
                }
                PinnedApps = null;
            }
            // once: every taskbar gets the moving line's new defaults (later changes in Settings are kept)
            if (!TaskAnimDefaultsSet && Bars != null)
            {
                foreach (BarPlacement b in Bars)
                    if (b != null && b.Modules != null)
                        foreach (ModuleConfig m in b.Modules)
                            if (m != null && m.Type == ModuleTypes.Taskbar)
                            {
                                m.TaskAnim = "Stretch"; m.TaskAnimMs = 400; m.TaskAnimBounce = "Soft"; m.TaskAnimHighlight = true;
                            }
                TaskAnimDefaultsSet = true;
            }
            if (RunBangs == null) RunBangs = RunBang.Defaults();
            RunBangs.RemoveAll(delegate (RunBang b) { return b == null || string.IsNullOrWhiteSpace(b.Key) || string.IsNullOrWhiteSpace(b.Url); });
            if (Array.IndexOf(ModuleTypes.RunEngines, RunEngine) < 0) RunEngine = "Google";
            if (RunCustomEngine == null) RunCustomEngine = "";
            if (RunHistory == null) RunHistory = new List<string>();
            if (TermProfiles != null) TermProfiles.RemoveAll(delegate (TermProfile tp) { return tp == null || string.IsNullOrWhiteSpace(tp.Command); });
            if (TermProfiles != null && TermProfiles.Count == 0) TermProfiles = null;
            if (TermFont == null) TermFont = "";
            if (TermPopRect == null) TermPopRect = "";
            TermFontSize = Math.Max(8, Math.Min(32, TermFontSize));
            if (string.IsNullOrWhiteSpace(TermColors)) TermColors = "Theme";
            if (SharedPins == null) SharedPins = new List<PinnedApp>();
            SharedPins.RemoveAll(delegate (PinnedApp sp) { return sp == null || string.IsNullOrWhiteSpace(sp.Path); });
            foreach (PinnedApp sp in SharedPins) { if (sp.Name == null) sp.Name = ""; if (sp.Args == null) sp.Args = ""; if (sp.AppId == null) sp.AppId = ""; }
            if (TermConnections == null) TermConnections = new List<TermConnection>();
            TermConnections.RemoveAll(delegate (TermConnection tc) { return tc == null || string.IsNullOrWhiteSpace(tc.Host); });
            foreach (TermConnection tc in TermConnections) tc.Normalize();
            // the terminal is vTerm's drop-down from a bar now (0.38.194): a Terminal module in a widget goes
            if (Widgets != null)
                foreach (WidgetSettings w in Widgets)
                    if (w != null && w.Modules != null && w.Modules.RemoveAll(delegate (ModuleConfig m) { return m != null && m.Type == ModuleTypes.Terminal; }) > 0)
                        Log.Write("Settings: a widget's Terminal module removed (the terminal is vTerm's drop-down from a bar now)");
            if (RunHistory.Count > 50) RunHistory.RemoveRange(50, RunHistory.Count - 50);
            if (Old != null) { PluginStore.MoveOld(Old, this); Old = null; }
            PluginStore.ProtectSecrets(this);
            NotifySvcDelay = Math.Max(0, Math.Min(1800, NotifySvcDelay));
            if (Array.IndexOf(ModuleTypes.NotifyCorners, NotifyCorner) < 0) NotifyCorner = "Bottom right";
            if (string.IsNullOrWhiteSpace(NotifyMonitor)) NotifyMonitor = "Main";
            if (NotifyMonitorKey == null || NotifyMonitor == "Main" || NotifyMonitor == "Mouse") NotifyMonitorKey = "";     // "Main", "Mouse" or a monitor's device name
            if (Array.IndexOf(ModuleTypes.NotifyGroups, NotifyGroup) < 0) NotifyGroup = "By source";
            NotifySeconds = Math.Max(2, Math.Min(120, NotifySeconds));
            if (!NotifyHistoryUp) { if (NotifyHistoryMax == 100) NotifyHistoryMax = 500; NotifyHistoryUp = true; }
            NotifyHistoryMax = Math.Max(10, Math.Min(5000, NotifyHistoryMax));
            NotifyCollapseMinutes = Math.Max(0, Math.Min(240, NotifyCollapseMinutes));
            if (NotifyQuietFrom == null) NotifyQuietFrom = "23:00";
            if (NotifyQuietTo == null) NotifyQuietTo = "07:00";

            NotifyListenPort = Math.Max(1024, Math.Min(65535, NotifyListenPort));
            if (NotifyListenToken == null) NotifyListenToken = "";
            NotifyListenToken = Secret.Protect(NotifyListenToken);
            NotifyCpuTemp = Math.Max(0, Math.Min(120, NotifyCpuTemp));
            NotifyGpuTemp = Math.Max(0, Math.Min(120, NotifyGpuTemp));
            NotifyTempSeconds = Math.Max(0, Math.Min(600, NotifyTempSeconds));
            if (NotifyDiskFree > 0 && NotifyDiskFull == 0) NotifyDiskFull = 100 - Math.Min(50, NotifyDiskFree);
            NotifyDiskFree = 0;
            NotifyDiskFull = Math.Max(0, Math.Min(100, NotifyDiskFull));
            NotifyBattery = Math.Max(0, Math.Min(90, NotifyBattery));
            NotifyMemory = Math.Max(0, Math.Min(100, NotifyMemory));
            NotifySysCooldown = Math.Max(1, Math.Min(1440, NotifySysCooldown));
            NotifyRainChance = Math.Max(10, Math.Min(100, NotifyRainChance));
            if (UserPresets == null) UserPresets = new List<ThemePreset>();
            // the saved copies of colours that are built-in presets now (only while they are unchanged)
            UserPresets.RemoveAll(delegate (ThemePreset p)
            {
                if (p == null) return true;
                foreach (string[] n in NowPresets)
                    if (string.Equals(p.Name, n[0], StringComparison.OrdinalIgnoreCase) && string.Equals(p.Background, n[1], StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.MainAccent, n[2], StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            });
            if (!WinMatchSetup) { WinMatch = true; WinMatchSetup = true; }
            if (Wallpapers == null) Wallpapers = new List<MonitorWallpaper>();
            Wallpapers.RemoveAll(delegate (MonitorWallpaper w) { return w == null || string.IsNullOrEmpty(w.Monitor); });
            foreach (MonitorWallpaper w in Wallpapers)
            {
                if (w.MonitorKey == null) w.MonitorKey = "";
                if (w.Path == null) w.Path = "";
                if (Array.IndexOf(new[] { "Fill", "Fit", "Stretch", "Center", "Tile" }, w.Fit) < 0) w.Fit = "Fill";
                if (string.IsNullOrEmpty(w.Background)) w.Background = "Blur";
                if (string.IsNullOrEmpty(w.Color1)) w.Color1 = "#FF1E2230";
                if (string.IsNullOrEmpty(w.Color2)) w.Color2 = "#FF3A4466";
                w.Angle = ((w.Angle % 360) + 360) % 360;
                if (w.SlideFolder == null) w.SlideFolder = "";
                if (w.SlideCurrent == null) w.SlideCurrent = "";
                w.SlideMinutes = Math.Max(1, Math.Min(10080, w.SlideMinutes));
            }
            if (WallpaperMode != "Same" && WallpaperMode != "Span") WallpaperMode = "Each";
            foreach (var w in Wallpapers)
            {
                if (w.Path == null) w.Path = "";
                if (Array.IndexOf(new[] { "Fill", "Fit", "Stretch", "Center", "Tile" }, w.Fit) < 0) w.Fit = "Fill";
            }
            // ---- widgets ----------------------------------------------------
            if (Widgets == null) Widgets = new List<WidgetSettings>();
            Widgets.RemoveAll(delegate (WidgetSettings w) { return w == null; });
            foreach (var w in Widgets) w.Normalize();

            UserPresets.RemoveAll(delegate (ThemePreset p) { return p == null || string.IsNullOrWhiteSpace(p.Name); });
            foreach (var p in UserPresets)
            {
                if (p.Background == null) p.Background = "#E61E1E2E";
                if (p.Foreground == null) p.Foreground = "#FFCDD6F4";
                if (p.MainAccent == null) p.MainAccent = "#FF89B4FA";
                if (p.FontFamily == null) p.FontFamily = "";
                if (p.BgStyle != "Gradient") p.BgStyle = "Solid";
                if (string.IsNullOrWhiteSpace(p.Background2)) p.Background2 = "#E6313244";
                if (string.IsNullOrWhiteSpace(p.SecondaryAccent)) p.SecondaryAccent = "#FFF5C2E7";
            }

            foreach (var b in Bars)
            {
                if (b.Edge != "Top" && b.Edge != "Bottom") b.Edge = "Bottom";
                if (b.Monitor == null) b.Monitor = "";
                if (b.MonitorKey == null) b.MonitorKey = "";
                b.BorderColor = ThemeSlots.Migrate(b.BorderColor);
                b.BorderOpacity = Math.Max(0, Math.Min(100, b.BorderOpacity));
                b.BackgroundOpacity = Math.Max(0, Math.Min(100, b.BackgroundOpacity));
                if (string.IsNullOrEmpty(b.Id)) b.Id = Guid.NewGuid().ToString("N");
                if (double.IsNaN(b.CenterX)) b.CenterX = 0.5;
                b.CenterX = Math.Max(0.0, Math.Min(1.0, b.CenterX));

                if (b.Modules == null)
                {
                    // settings written by v0.1: global flags -> per-bar modules
                    if (BarHeight.HasValue) b.BarHeight = BarHeight.Value;
                    if (FullWidth.HasValue) b.FullWidth = FullWidth.Value;
                    b.Modules = new List<ModuleConfig>();
                    if (ShowTaskbar ?? true)
                    {
                        var t = ModuleConfig.Create(ModuleTypes.Taskbar);
                        if (TaskButtonMaxWidth.HasValue) t.MaxButtonWidth = TaskButtonMaxWidth.Value;
                        t.OnlyCurrentMonitor = TaskbarOnlyCurrentMonitor ?? false;
                        b.Modules.Add(t);
                    }
                    b.Modules.Add(ModuleConfig.Create(ModuleTypes.Spacer));
                    if (ShowCpu ?? true) b.Modules.Add(ModuleConfig.Create(ModuleTypes.Cpu));
                    if (ShowMemory ?? true) b.Modules.Add(ModuleConfig.Create(ModuleTypes.Memory));
                    if (ShowSystemTray ?? true) b.Modules.Add(ModuleConfig.Create(ModuleTypes.Tray));
                }

                // v0.2 -> v0.3: font size / corner radius / edge margin used to be global
                if (FontSize.HasValue) b.FontSize = FontSize.Value;
                if (CornerRadius.HasValue) b.CornerRadius = CornerRadius.Value;
                if (EdgeMargin.HasValue) b.EdgeMargin = EdgeMargin.Value;
                if (double.IsNaN(b.FontSize)) b.FontSize = 12;
                if (double.IsNaN(b.CornerRadius)) b.CornerRadius = 0;
                b.FontSize = Math.Max(8, Math.Min(28, b.FontSize));
                b.CornerRadius = Math.Max(0, Math.Min(40, b.CornerRadius));
                b.EdgeMargin = Math.Max(0, Math.Min(40, b.EdgeMargin));

                b.BarHeight = Math.Max(30, Math.Min(120, b.BarHeight));
                b.Modules.RemoveAll(delegate (ModuleConfig m) { return m == null || !ModuleTypes.Keep(m.Type, false); });
                foreach (var m in b.Modules)
                {
                    m.Normalize();
                    // Build 2: bar modules have S / M / L text instead of a font size - the nearest to what they had
                    if (string.IsNullOrEmpty(m.TextSize))
                    {
                        double had = m.FollowFontSize ? b.FontSize : m.FontSize;
                        m.TextSize = (m.TwoLines || m.TempOnSecondLine) ? "M" : had >= 16 ? "L" : "S";
                    }
                }
                if (Array.IndexOf(ModuleTypes.SeparatorStyles, b.SeparatorStyle) < 0) b.SeparatorStyle = "Dots";
                if (Array.IndexOf(OwnBlurModes, b.Blur) < 0) b.Blur = "Theme";
                b.NormalizeEdge();
                if (b.Zones) b.GuessZones();
            }

            // an integration that became a plugin (Komodo 0.38.77, Seerr 0.38.79) is added for you once if you used it
            if (PluginsOffered == null) PluginsOffered = Plugins == null ? new List<string>() : new List<string> { "komodo" };
            if (Plugins == null) Plugins = new List<string>();
            foreach (PluginInfo p in PluginStore.Catalog)
            {
                if (PluginsOffered.Contains(p.Id)) continue;
                PluginsOffered.Add(p.Id);
                if (p.InUse != null && p.InUse(this) && !Plugins.Contains(p.Id)) Plugins.Add(p.Id);
            }
            Plugins.RemoveAll(delegate (string id) { return string.IsNullOrWhiteSpace(id); });

            FontSize = null; CornerRadius = null; EdgeMargin = null;
            BarHeight = null; FullWidth = null; TaskButtonMaxWidth = null; ShowTaskbar = null;
            ShowSystemTray = null; ShowCpu = null; ShowMemory = null; TaskbarOnlyCurrentMonitor = null;
        }

        public static readonly string[] BlurModes = { "Off", "Blur", "Acrylic" };
        /// <summary>A bar's / widget's own blur: the theme's, or its own.</summary>
        public static readonly string[] OwnBlurModes = { "Theme", "Off", "Blur", "Acrylic" };
        public static readonly string[] OwnBlurNames = { "As the theme", "Off", "Blur", "Frosted" };
        /// <summary>The blur a bar / widget actually gets.</summary>
        public static string BlurFor(BarSettings s, string own)
        {
            if (own == "Off" || own == "Blur" || own == "Acrylic") return own;
            return s == null ? "Off" : s.BlurMode;
        }

        public static readonly Dictionary<string, string[]> Presets = new Dictionary<string, string[]>
        {
            // name -> { background, text, main accent, secondary accent, gradient (second background colour) }
            // The secondary accent is the "look at this" colour: second graph series, warnings past a threshold, flashing buttons, progress.
            { "Mocha (dark)",     new[] { "#E61E1E2E", "#FFCDD6F4", "#FF89B4FA", "#FFF5C2E7", "#E6313244" } },
            { "Midnight",         new[] { "#F0101018", "#FFE6E6F0", "#FF7C5CFF", "#FF2EE6C5", "#F01C1A30" } },
            { "Nord",             new[] { "#E62E3440", "#FFECEFF4", "#FF88C0D0", "#FFEBCB8B", "#E63B4252" } },
            { "Light",            new[] { "#F2F3F3F7", "#FF202124", "#FF0A64D6", "#FFD9480F", "#F2E2E5EE" } },
            { "Glass",            new[] { "#99202020", "#FFFFFFFF", "#FF60CDFF", "#FFFFB86B", "#99303048" } },
            { "High contrast",    new[] { "#FF000000", "#FFFFFFFF", "#FFFFD400", "#FF00E5FF", "#FF000000" } },
            // ---- neon ----
            { "Cyberpunk",        new[] { "#EB0D0221", "#FFE0F7FF", "#FFFF2A6D", "#FF05D9E8", "#EB1F0A45" } },
            { "Neon 2077",        new[] { "#F00B0C10", "#FFEDEDED", "#FFFCEE0A", "#FF00F0FF", "#F02A0E14" } },
            { "Synthwave '84",    new[] { "#EB262335", "#FFF4EEFF", "#FFFF7EDB", "#FF36F9F6", "#EB34294F" } },
            { "Matrix",           new[] { "#F0000A00", "#FFC8F5C8", "#FF00FF41", "#FFFFB000", "#F0001F08" } },
            { "Outrun",           new[] { "#EB0B0723", "#FFFFE9F5", "#FFFF3F8E", "#FFFFB54C", "#EB2A0B3D" } },
            { "Sunset",           new[] { "#EB1C1426", "#FFFBE7DC", "#FFFF7849", "#FFB46CFF", "#EB3A1F33" } },
            { "Amber CRT",        new[] { "#F00F0A00", "#FFFFC66B", "#FFFFB000", "#FFFF5F1F", "#F01F1400" } },
            // ---- light ----
            { "Catppuccin Latte", new[] { "#F2EFF1F5", "#FF4C4F69", "#FF1E66F5", "#FFFE640B", "#F2DCE0E8" } },
            { "Paper",            new[] { "#F2F7F4EE", "#FF2B2B2B", "#FF2F6F5E", "#FFC2553A", "#F2ECE6DA" } },
            { "Sakura",           new[] { "#F2FFF0F3", "#FF4A2F3A", "#FFD6336C", "#FF7048E8", "#F2FFDEE6" } },
            { "Arctic",           new[] { "#E6E8F1F8", "#FF1D2B3A", "#FF0094C6", "#FFE0457B", "#E6D3E4F0" } },
            { "Warm Sunset",      new[] { "#FFFFF4E6", "#FF4A2C1F", "#FFDD9836", "#FF0F8B8D", "#FFFFE2C2" } },
            // ---- dark ----
            { "Dracula",          new[] { "#EB282A36", "#FFF8F8F2", "#FFBD93F9", "#FFFFB86C", "#EB44475A" } },
            { "Tokyo Night",      new[] { "#EB1A1B26", "#FFC0CAF5", "#FF7AA2F7", "#FFFF9E64", "#EB24283B" } },
            { "Gruvbox Dark",     new[] { "#EB282828", "#FFEBDBB2", "#FF83A598", "#FFFE8019", "#EB3C3836" } },
            { "Rose Pine",        new[] { "#EB191724", "#FFE0DEF4", "#FFC4A7E7", "#FFF6C177", "#EB26233A" } },
            { "Everforest",       new[] { "#EB2D353B", "#FFD3C6AA", "#FFA7C080", "#FFE69875", "#EB3D484D" } },
            { "Kanagawa",         new[] { "#EB1F1F28", "#FFDCD7BA", "#FF7E9CD8", "#FFFFA066", "#EB2A2A37" } },
            { "Oceanic",          new[] { "#EB0F1C2E", "#FFD8E6F3", "#FF4FC3F7", "#FFFFCA28", "#EB16304A" } },
            { "Blood Moon",       new[] { "#EB140A0D", "#FFF2E1E4", "#FFE5383B", "#FFFFB703", "#EB2E1018" } },
            { "AMOLED Black",     new[] { "#FF000000", "#FFE6E6E6", "#FF3D8BFF", "#FFFF5C8A", "#FF121212" } },
            { "Windows Dark",     new[] { "#F0202020", "#FFFFFFFF", "#FF60CDFF", "#FFFF99A4", "#F02C2C2C" } },
            { "GitHub Dark",      new[] { "#EB0D1117", "#FFE6EDF3", "#FF2F81F7", "#FFF78166", "#EB161B22" } },
            { "Monokai",          new[] { "#EB272822", "#FFF8F8F2", "#FFA6E22E", "#FFF92672", "#EB3E3D32" } },
            { "Forest",           new[] { "#EB0F1A14", "#FFDCE8DC", "#FF4CAF7A", "#FFE0B252", "#EB1A2E22" } },
            { "Monochrome",       new[] { "#EB18181A", "#FFE4E4E7", "#FFA1A1AA", "#FFFFFFFF", "#EB27272A" } },
            { "Deep Purple",      new[] { "#FF1A0F24", "#FFEDE7F6", "#FF9A36DD", "#FFFFC857", "#FF2E1840" } },
            { "Carbon Teal",      new[] { "#FF0D0F10", "#FFE3E8EA", "#FF1ABC9C", "#FFFF6B6B", "#FF1C2326" } },
            { "Cosmic Blue",      new[] { "#FF0C0F1C", "#FFC9D2E8", "#FF3AE2FF", "#FFFF4FD8", "#FF1B2142" } },
        };

        /// <summary>Saved colours that are now built-in presets (were in Saved before 0.38.107): name, background, main accent.</summary>
        static readonly string[][] NowPresets =
        {
            new[] { "Deep Purple", "#FF1A0F24", "#FF9A36DD" },
            new[] { "Warm Sunset", "#FFFFF4E6", "#FFDD9836" },
            new[] { "Carbon Teal", "#FF0D0F10", "#FF1ABC9C" },
            new[] { "Cosmic Blue", "#FF0C0F1C", "#FF3AE2FF" },
        };
    }
}
