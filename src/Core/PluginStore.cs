using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>A plugin's build in a release's plugins.json: what it is and where to get it.</summary>
    public sealed class PluginBuild
    {
        /// <summary>Its own version ("0.38.86", "1.0.1").</summary>
        public string Version = "";
        public string Sha256 = "";
        /// <summary>Where to download it (the release's asset), and its .sha256.</summary>
        public string Url = "", ShaUrl = "";
        /// <summary>The vShell it was built against, and the release it's in ("v0.38.86").</summary>
        public string BuiltFor = "", Release = "";
        /// <summary>The parts of vShell it uses (PluginFileInfo.UsesLines): whether it runs here is known before downloading.</summary>
        public string[] Uses = new string[0];
    }

    /// <summary>A plugin vShell knows about: one of V's (built in), one in this vShell release's plugins.json, or one added from a file (its own manifest).</summary>
    public sealed class PluginInfo
    {
        /// <summary>The id kept in settings (BarSettings.Plugins): "komodo".</summary>
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public string Glyph = "";
        /// <summary>Its group on the Plugins page ("Smart home", "Servers & network", "Media", "Devices" …).</summary>
        public string Group = "Other";
        public string Author = "";
        /// <summary>What it brings, short ("a module, notifications, !ko in the run box").</summary>
        public string Adds = "";
        /// <summary>It can be downloaded from this vShell version's release (V's plugins, and others' V has put there).</summary>
        public bool InRelease = true;
        /// <summary>Added from a file (not in the release): vShell keeps its manifest next to its DLL.</summary>
        public bool FromFile;
        /// <summary>From Inspect: what it uses that's public in vShell but not part of the plugin API (it may need a rebuild for a later vShell).</summary>
        public List<string> Outside = new List<string>();
        /// <summary>Its newest build in the releases (the newest release with this plugin API's plugins.json), or null.</summary>
        public PluginBuild Latest;
        /// <summary>The DLL's name without .dll ("vShell.Komodo") - also its assembly name and the release asset's name.</summary>
        public string File = "";
        /// <summary>The module types it brings (a module of one of these with the plugin not added shows "add it in Plugins").</summary>
        public string[] Types = new string[0];
        /// <summary>Settings from before plugins that show it was in use (added for you once).</summary>
        public Func<BarSettings, bool> InUse;
        /// <summary>An author as written in a manifest or a plugins.json: vShell's own plugins said "V" before 0.38.153, now "version".</summary>
        public static string AuthorName(string author)
        {
            string a = (author ?? "").Trim();
            return a == "V" ? "version" : a;
        }

        /// <summary>Its settings from before it kept its own (old field -> its key): moved into Integration(Id) / a module's Extra once, on load.</summary>
        public Dictionary<string, string> OldSettings = new Dictionary<string, string>();
        public Dictionary<string, string> OldModuleSettings = new Dictionary<string, string>();
        /// <summary>The same per module type, for a plugin with more than one (each type takes only its own old fields).</summary>
        public Dictionary<string, Dictionary<string, string>> OldModuleSettingsByType = new Dictionary<string, Dictionary<string, string>>();
        /// <summary>Its integration settings that are secrets: never left in plain text on disk (encrypted on load, like vShell's own).</summary>
        public string[] Secrets = new string[0];
    }

    /// <summary>
    /// The plugins' DLLs: %APPDATA%\vShell\plugins\vShell.Komodo-0.38.77.dll - one per vShell version, because a plugin is built for one exact
    /// version. They come from the update site (vshell/plugins/&lt;plugin API&gt;/: plugins.json + the newest build of each - vshell/pre/plugins/…
    /// for a pre-release), from next to vShell.exe (a fresh build in dist\), or from a file you pick. An update fetches the new version's plugins before swapping the exe;
    /// the old version's files stay until the new one has started (so going back still has them).
    /// </summary>
    public static class PluginStore
    {
        public static readonly List<PluginInfo> Catalog = new List<PluginInfo>
        {
            new PluginInfo
            {
                Group = "Servers & network", Author = "version", Adds = "!ko in the run box, notifications, palette commands",
                Id = "komodo", Name = "Komodo", File = "vShell.Komodo", Glyph = "", Types = new[] { "Komodo" },
                Description = "Docker stacks, deployments, containers and procedures in Komodo (komo.do): a module, notifications, !ko in the run box and palette commands.",
                InUse = delegate (BarSettings s) { return s.IntegrationValue("komodo", "Url", "").Trim().Length > 0 || s.UsesModule("Komodo"); },
                OldSettings = Keys("KoUrl", "Url", "KoKey", "Key", "KoSecret", "Secret", "KoIgnoreCert", "IgnoreCert", "KoBang", "Bang",
                                   "KoNotifyDown", "NotifyDown", "KoNotifyDelay", "NotifyDelay", "KoNotifyUpdates", "NotifyUpdates", "KoNotifyProcedures", "NotifyProcedures"),
                OldModuleSettings = Keys("KoShow", "Show", "KoFilter", "Filter", "KoServer", "Server", "KoCount", "Count",
                                         "KoShowProcedures", "ShowProcedures", "KoShowOpen", "ShowOpen", "KoGroup", "Group"),
                Secrets = new[] { "Secret" }
            },
            new PluginInfo
            {
                Group = "Media", Author = "version", Adds = "!s in the run box (search, request, approve), notifications",
                Id = "seerr", Name = "Seerr", File = "vShell.Seerr", Glyph = "", Types = new[] { "Seerr" },
                Description = "Requests and what's new in Seerr (Overseerr / Jellyseerr): a module, notifications, and !s in the run box to search, request, approve and decline.",
                InUse = delegate (BarSettings s) { return s.IntegrationValue("seerr", "Url", "").Trim().Length > 0 || s.UsesModule("Seerr"); },
                OldSettings = Keys("SeerrUrl", "Url", "SeerrApiKey", "ApiKey", "SeerrIgnoreCert", "IgnoreCert", "SeerrUserId", "UserId", "SeerrBang", "Bang",
                                   "SeerrNotifyNew", "NotifyNew", "SeerrNotifyAvailable", "NotifyAvailable"),
                OldModuleSettings = Keys("SrTab", "Tab", "SrShowTabs", "ShowTabs", "SrTabsShown", "TabsShown", "SrFilter", "Filter", "SrShowFilters", "ShowFilters",
                                         "SrCount", "Count", "SrPosterRows", "PosterRows", "SrPosters", "Posters", "SrBadge", "Badge",
                                         "SrBarProcessing", "BarProcessing", "SrBarAvailable", "BarAvailable", "SrShowOpen", "ShowOpen"),
                Secrets = new[] { "ApiKey" }
            },
            new PluginInfo
            {
                Group = "Media", Author = "version", Adds = "!ra in the run box (search, add), notifications",
                Id = "radarr", Name = "Radarr", File = "vShell.Radarr", Glyph = "", Types = new[] { "Radarr" },
                Description = "Your movies in Radarr: a module with the queue, what's coming out, what was added and what's missing, notifications, and !ra in the run box to search and add movies.",
                Secrets = new[] { "ApiKey" }
            },
            new PluginInfo
            {
                Group = "Media", Author = "version", Adds = "!so in the run box (search, add), notifications",
                Id = "sonarr", Name = "Sonarr", File = "vShell.Sonarr", Glyph = "", Types = new[] { "Sonarr" },
                Description = "Your shows in Sonarr: a module with the queue, what's airing, what was added and what's missing, notifications, and !so in the run box to search and add shows.",
                Secrets = new[] { "ApiKey" }
            },
            new PluginInfo
            {
                Group = "Media", Author = "version", Adds = "!td in the run box (jobs, pause / resume nodes), notifications",
                Id = "tdarr", Name = "Tdarr", File = "vShell.Tdarr", Glyph = "", Types = new[] { "Tdarr" },
                Description = "What Tdarr is transcoding and health checking: a module with the running jobs, the nodes (pause, worker limits) and the library totals, notifications, and !td in the run box.",
                Secrets = new[] { "ApiKey" }
            },
            new PluginInfo
            {
                Group = "Devices", Author = "version", Adds = "notifications, palette commands",
                Id = "printer", Name = "3D printers", File = "vShell.Printer", Glyph = "", Types = new[] { "Printer" },
                Description = "Klipper printers through Moonraker (Fluidd / Mainsail): a module with the print, webcam, temperatures, spool and files, notifications, and palette commands.",
                InUse = delegate (BarSettings s)
                {
                    List<System.Text.Json.JsonElement> l = s.IntegrationValue<List<System.Text.Json.JsonElement>>("printer", "Printers", null);
                    return (l != null && l.Count > 0) || s.UsesModule("Printer");
                },
                OldSettings = Keys("Printers", "Printers", "PrintPresets", "Presets", "PrintNotifyStart", "NotifyStart", "PrintNotifyDone", "NotifyDone",
                                   "PrintNotifyFail", "NotifyFail", "PrintNotifyPause", "NotifyPause", "PrintNotifyEvery", "NotifyEvery", "PrintNotifyLayer", "NotifyLayer",
                                   "PrintNotifyCooled", "NotifyCooled", "PrintCoolTemp", "CoolTemp"),
                OldModuleSettings = Keys("PtPrinter", "Printer", "PtBarTemps", "BarTemps", "PtBarTime", "BarTime", "PtShowCam", "ShowCam", "PtHideOffline", "HideOffline",
                                         "PtCamFolded", "CamFolded", "PtCamMode", "CamMode", "PtCamFps", "CamFps", "PtCamSeconds", "CamSeconds", "PtShowTemps", "ShowTemps",
                                         "PtShowSpool", "ShowSpool", "PtShowPower", "ShowPower", "PtShowLists", "ShowLists", "PtShowFluidd", "ShowFluidd")
                // (a printer's API key is encrypted by the plugin: PrinterConn.Normalize)
            },
            new PluginInfo
            {
                Group = "Media", Author = "version", Adds = "!qb / !sab, magnet links and .torrent / .nzb files, notifications",
                Id = "downloads", Name = "Downloads", File = "vShell.Downloads", Glyph = "", Types = new[] { "Downloads" },
                Description = "qBittorrent and SABnzbd: a module with the speeds and the queue, notifications, !qb / !sab in the run box, and adding magnet links, .torrent and .nzb files.",
                InUse = delegate (BarSettings s)
                {
                    return s.IntegrationValue("downloads", "QbUrl", "").Trim().Length > 0 || s.IntegrationValue("downloads", "SabUrl", "").Trim().Length > 0 || s.UsesModule("Downloads");
                },
                OldSettings = Keys("QbUrl", "QbUrl", "QbUser", "QbUser", "QbPassword", "QbPassword", "QbIgnoreCert", "QbIgnoreCert", "SabUrl", "SabUrl", "SabApiKey", "SabApiKey",
                                   "SabIgnoreCert", "SabIgnoreCert", "DlNotify", "Notify", "DlNotifyFailed", "NotifyFailed", "DlNotifyCategories", "NotifyCategories",
                                   "QbBang", "QbBang", "SabBang", "SabBang"),
                OldModuleSettings = Keys("DlSource", "Source", "DlView", "View", "DlShow", "Show", "DlCount", "Count", "DlShowCount", "ShowCount", "DlShowUpload", "ShowUpload"),
                Secrets = new[] { "QbPassword", "SabApiKey" }
            },
            new PluginInfo
            {
                Group = "Servers & network", Author = "version", Adds = "!u in the run box, notifications with snapshots",
                Id = "unifi", Name = "UniFi", File = "vShell.Unifi", Glyph = "", Types = new[] { "UnifiNetwork", "UnifiProtect" },
                Description = "UniFi Network (devices, clients, ports) and UniFi Protect (cameras, lights, events) with their official API keys: two modules, notifications with snapshots, and !u in the run box.",
                InUse = delegate (BarSettings s) { return s.IntegrationValue("unifi", "Url", "").Trim().Length > 0 || s.UsesModule("UnifiNetwork") || s.UsesModule("UnifiProtect"); },
                OldSettings = Keys("UnifiUrl", "Url", "UnifiNetKey", "NetKey", "UnifiProtectKey", "ProtectKey", "UnifiIgnoreCert", "IgnoreCert", "UnifiSite", "Site",
                                   "UnifiPollSeconds", "PollSeconds", "UnifiBang", "Bang", "NotifyUnifiDevices", "NotifyDevices", "NotifyUnifiDelay", "NotifyDelay",
                                   "NotifyUnifiNewClient", "NotifyNewClient", "NotifyUnifiApClients", "NotifyApClients", "NotifyProtectRing", "NotifyRing",
                                   "NotifyProtectPerson", "NotifyPerson", "NotifyProtectVehicle", "NotifyVehicle", "NotifyProtectPackage", "NotifyPackage",
                                   "NotifyProtectAnimal", "NotifyAnimal", "NotifyProtectMotion", "NotifyMotion", "NotifyProtectCameras", "NotifyCameras"),
                OldModuleSettingsByType = new Dictionary<string, Dictionary<string, string>>
                {
                    { "UnifiNetwork", Keys("UnView", "View", "UnBarRates", "BarRates", "UnBarClients", "BarClients", "UnCount", "Count", "UnOpen", "Open") },
                    { "UnifiProtect", Keys("PrColumns", "Columns", "PrSnapSeconds", "SnapSeconds", "PrCameras", "Cameras", "PrShowEvents", "ShowEvents",
                                           "PrShowLights", "ShowLights", "PrBarEvent", "BarEvent") }
                },
                Secrets = new[] { "NetKey", "ProtectKey" }
            },
            new PluginInfo
            {
                Group = "Media", Author = "version", Adds = "Now playing's times and buttons for the Jellyfin apps, notifications",
                Id = "jellyfin", Name = "Jellyfin", File = "vShell.Jellyfin", Glyph = "",
                Description = "Now playing gets the times (a progress bar you can seek) and working buttons for what the Jellyfin apps play, plus notifications when someone starts or stops watching.",
                InUse = delegate (BarSettings s) { return s.IntegrationValue("jellyfin", "Url", "").Trim().Length > 0; },
                OldSettings = Keys("JellyfinUrl", "Url", "JellyfinApiKey", "ApiKey", "JellyfinIgnoreCert", "IgnoreCert", "JellyfinDevice", "Device",
                                   "NotifyJfPlay", "NotifyPlay", "NotifyJfStop", "NotifyStop", "NotifyJfIgnore", "NotifyIgnore"),
                Secrets = new[] { "ApiKey" }
            },
            new PluginInfo
            {
                Group = "Servers & network", Author = "version", Adds = "notifications, palette commands, Open shell",
                Id = "proxmox", Name = "Proxmox VE", File = "vShell.Proxmox", Glyph = "", Types = new[] { "Proxmox" },
                Description = "Your Proxmox cluster with an API token: a module with nodes, VMs / containers and storage (start, stop, Open shell), notifications and palette commands.",
                InUse = delegate (BarSettings s) { return s.IntegrationValue("proxmox", "Urls", "").Trim().Length > 0 || s.UsesModule("Proxmox"); },
                OldSettings = Keys("PveUrls", "Urls", "PveTokenId", "TokenId", "PveSecret", "Secret", "PveIgnoreCert", "IgnoreCert", "PvePollSeconds", "PollSeconds",
                                   "NotifyPveNodes", "NotifyNodes", "NotifyPveGuests", "NotifyGuests", "NotifyPveDelay", "NotifyDelay", "NotifyPveStorage", "NotifyStorage"),
                OldModuleSettings = Keys("PveShow", "Show", "PveNode", "Node", "PveStyle", "Style", "PveRunningOnly", "RunningOnly", "PveShowTemplates", "ShowTemplates",
                                         "PveGuestStats", "GuestStats", "PveGuestUptime", "GuestUptime", "PveAllowPower", "AllowPower", "PveAllowDelete", "AllowDelete"),
                Secrets = new[] { "Secret" }
            },
            new PluginInfo
            {
                Group = "Smart home", Author = "version", Adds = "notification rules, palette commands, a Weather source, vDrop (a Notes store, notifications)",
                Id = "homeassistant", Name = "Home Assistant", File = "vShell.HomeAssistant", Glyph = "", Types = new[] { "HomeAssistant", "Climate", "PowerFlow" },
                Description = "Your Home Assistant: the Entities (and Bubble buttons), Climate and Power flow modules, notification rules, palette commands, " +
                              "a Home Assistant source for the Weather module, and vDrop (a Notes store, and its notifications).",
                InUse = delegate (BarSettings s)
                {
                    if (s.IntegrationValue("homeassistant", "Url", "").Trim().Length > 0) return true;
                    if (s.UsesModule("HomeAssistant") || s.UsesModule("Climate") || s.UsesModule("PowerFlow")) return true;
                    // a Notes module on vDrop, a Weather module on Home Assistant
                    Predicate<ModuleConfig> uses = delegate (ModuleConfig m) { return m != null && (m.NoteStore == "vDrop" || m.WeatherSource == "HomeAssistant"); };
                    if (s.Widgets != null) foreach (WidgetSettings w in s.Widgets) if (w != null && w.Modules != null && w.Modules.Exists(uses)) return true;
                    if (s.Bars != null) foreach (BarPlacement b in s.Bars) if (b != null && b.Modules != null && b.Modules.Exists(uses)) return true;
                    return false;
                },
                OldSettings = Keys("HaUrl", "Url", "HaToken", "Token", "HaIgnoreCert", "IgnoreCert", "HaPollSeconds", "PollSeconds",
                                   "NotifyHaRules", "Rules", "VDropNotifications", "VDropNotifications"),
                OldModuleSettingsByType = new Dictionary<string, Dictionary<string, string>>
                {
                    { "HomeAssistant", Keys("HaEntities", "Entities", "HaBubble", "Bubble", "HaBubbleWidth", "BubbleWidth") },
                    { "Climate", Keys("ClimateEntity", "ClimateEntity", "ClimateZones", "Zones", "ClimateShowTarget", "ShowTarget", "ClimateShowZones", "ShowZones") },
                    { "PowerFlow", Keys("PfSolar", "Solar", "PfGridImport", "GridImport", "PfGridExport", "GridExport", "PfBattery", "Battery", "PfBatteryInvert", "BatteryInvert",
                                        "PfBatterySoc", "BatterySoc", "PfHome", "Home", "PfSolarColor", "SolarColor", "PfGridColor", "GridColor", "PfBatteryColor", "BatteryColor") },
                    { "Notes", Keys("VdKind", "VdKind", "VdStatus", "VdStatus") }      // vDrop's feed on a Notes module
                },
                Secrets = new[] { "Token" }
            },
            new PluginInfo
            {
                Group = "Internet", Author = "version", Adds = "notifications",
                Id = "mail", Name = "Mail", File = "vShell.Mail", Glyph = "", Types = new[] { "Mail", "MailList" },
                Description = "Your mail accounts over IMAP: a module with the unread count (the messages in a popup), a widget with the list, and a notification listing the new messages."
                // (an account's password is encrypted by the plugin: MailAccount.Normalize)
            }
        };

        static Dictionary<string, string> Keys(params string[] pairs)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
            return d;
        }

        /// <summary>Settings from before a plugin kept its own (BarSettings.Old): into its Integration bag. The rest is dropped, as always.</summary>
        public static void MoveOld(Dictionary<string, System.Text.Json.JsonElement> old, BarSettings s)
        {
            foreach (PluginInfo p in Catalog)
                foreach (KeyValuePair<string, string> k in p.OldSettings)
                {
                    System.Text.Json.JsonElement e;
                    if (!old.TryGetValue(k.Key, out e)) continue;
                    SettingsBag b = s.Integration(p.Id);
                    if (!b.Has(k.Value)) b.Put(k.Value, e);
                }
        }

        /// <summary>The plugins' secrets encrypted (every load, so one typed into settings.json by hand doesn't stay readable).</summary>
        public static void ProtectSecrets(BarSettings s)
        {
            if (s.Integrations == null) return;
            foreach (PluginInfo p in Catalog)
            {
                SettingsBag b;
                if (!s.Integrations.TryGetValue(p.Id, out b) || b == null) continue;
                foreach (string k in p.Secrets)
                {
                    string v = b.Get(k, "");
                    if (v.Length > 0 && !Secret.IsProtected(v)) b.Set(k, Secret.Protect(v));
                }
            }
        }

        /// <summary>The same for a module: its old fields into its Extra (only for the plugin the module's type comes from).</summary>
        public static void MoveOld(string type, Dictionary<string, System.Text.Json.JsonElement> old, ModuleConfig m)
        {
            // the plugin the type comes from; or one that keeps settings on one of vShell's own types (vDrop's tab on a Notes module)
            Dictionary<string, string> keys = null;
            PluginInfo p = OwnerOf(type);
            if (p != null && !p.OldModuleSettingsByType.TryGetValue(type, out keys)) keys = p.OldModuleSettings;
            if (p == null)
                foreach (PluginInfo q in Catalog)
                    if (q.OldModuleSettingsByType.TryGetValue(type, out keys)) break;
            if (keys == null) return;
            foreach (KeyValuePair<string, string> k in keys)
            {
                System.Text.Json.JsonElement e;
                if (old.TryGetValue(k.Key, out e) && !m.Bag.Has(k.Value)) m.Bag.Put(k.Value, e);
            }
        }

        public static PluginInfo Find(string id)
        {
            return Catalog.Find(delegate (PluginInfo p) { return string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase); });
        }

        /// <summary>The plugin a module type comes from (null = a built-in type, or one nobody knows).</summary>
        public static PluginInfo OwnerOf(string type)
        {
            return Catalog.Find(delegate (PluginInfo p) { return Array.IndexOf(p.Types, type) >= 0; });
        }

        /// <summary>The plugins added in settings (set by the UI side; the updater asks it which ones to bring along).</summary>
        public static Func<List<string>> Added = delegate { return new List<string>(); };

        public static string Dir { get { return Path.Combine(BarSettings.ConfigDir, "plugins"); } }

        static string V(Version v) { return v.Major + "." + v.Minor + "." + v.Build; }

        public static string PathFor(PluginInfo p, Version v) { return Path.Combine(Dir, p.File + "-" + V(v) + ".dll"); }

        /// <summary>A DLL of it that the running vShell can use is here.</summary>
        public static bool Have(PluginInfo p) { return Usable(p) != null; }

        /// <summary>
        /// The file to load: the one stored for this version, else the newest one that still works here (same plugin API) - a plugin added from
        /// a file, or one a newer release didn't bring, keeps working after an update. null = none.
        /// </summary>
        public static string Usable(PluginInfo p)
        {
            string mine = PathFor(p, Updater.Current);
            if (File.Exists(mine) && Check(mine, p, Updater.Current) == null) return mine;
            if (!Directory.Exists(Dir)) return null;
            var older = new List<KeyValuePair<Version, string>>();
            foreach (string f in Directory.GetFiles(Dir, p.File + "-*.dll"))
            {
                Version v = VersionOf(f);
                if (v != null && v < Updater.Current) older.Add(new KeyValuePair<Version, string>(v, f));
            }
            older.Sort(delegate (KeyValuePair<Version, string> a, KeyValuePair<Version, string> b) { return b.Key.CompareTo(a.Key); });
            foreach (var o in older) if (Check(o.Value, p, Updater.Current) == null) return o.Value;
            return null;
        }

        /// <summary>
        /// One from the release that runs on an older version's build (the update couldn't get this version's): it works, but this version's
        /// build should be fetched (for the next start) so it doesn't stay on the old one.
        /// </summary>
        public static bool OnOlderBuild(PluginInfo p)
        {
            if (p.FromFile) return false;
            string f = Usable(p);
            return f != null && !string.Equals(f, PathFor(p, Updater.Current), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The vShell version a stored file is kept for ("vShell.Komodo-0.38.84.dll" -> 0.38.84).</summary>
        static Version VersionOf(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file);
            int dash = n.LastIndexOf('-');
            return dash > 0 ? Updater.ParseVersion(n.Substring(dash + 1)) : null;
        }

        /// <summary>
        /// Is this file the plugin's DLL, and can vShell v run it? null = yes, else why not. What counts is the vShell it was compiled
        /// against (its reference to vShell - its own version number can be anything): the same plugin API, not a newer vShell, and (for the
        /// running vShell) everything it uses still here. A plugin from before API versions needs exactly its own vShell.
        /// </summary>
        static string Check(string file, PluginInfo p, Version v)
        {
            // asked several times per file at start (Have, Load, the older files Usable looks at): look inside each file once
            string key = null, r;
            try { var fi = new FileInfo(file); key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + V(v) + "|" + (p != null ? p.File : ""); }
            catch { }
            if (key != null) lock (Checked) if (Checked.TryGetValue(key, out r)) return r.Length == 0 ? null : r;
            r = CheckNow(file, p, v);
            if (key != null) lock (Checked) Checked[key] = r ?? "";
            return r;
        }

        static readonly Dictionary<string, string> Checked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string CheckNow(string file, PluginInfo p, Version v)
        {
            PluginFileInfo n;
            try { n = PluginFileInfo.Read(file); }
            catch (Exception ex) { return "it isn't a .NET DLL (" + ex.Message + ")"; }
            if (p != null && !string.Equals(n.File, p.File, StringComparison.OrdinalIgnoreCase)) return "it's " + n.File + ", not " + p.File;
            return Compatible(n, v);
        }

        static string Compatible(PluginFileInfo n, Version v)
        {
            if (n.BuiltFor == null) return "it isn't a vShell plugin (it doesn't use vShell)";
            Version have = new Version(n.BuiltFor.Major, n.BuiltFor.Minor, Math.Max(0, n.BuiltFor.Build));
            // built against a newer vShell: fine for the running one while it's the same plugin API and everything it uses is here (below) -
            // a plugin can be updated without updating vShell
            if (have > v && (v != Updater.Current || PluginApi.Of(have) != PluginApi.Of(v) || PluginApi.Of(v) == 0))
                return "it's for a newer vShell (" + V(have) + ") - this is " + V(v) + ", update vShell first";
            int apiHave = PluginApi.Of(have), apiHere = PluginApi.Of(v);
            if (apiHave == 0 || apiHere == 0) { if (have != v) return "it's for vShell " + V(have) + ", this is " + V(v); }
            else if (apiHave != apiHere) return "it's for plugin API " + apiHave + " (vShell " + V(have) + "), this vShell has API " + apiHere;
            if (v == Updater.Current)
            {
                List<string> gone = n.MissingIn(typeof(PluginStore).Assembly);
                if (gone.Count > 0 && have > v) return "it needs vShell " + V(have) + " (it uses " + string.Join(", ", gone.GetRange(0, Math.Min(3, gone.Count))) + (gone.Count > 3 ? " …" : "") + ") - update vShell first";
                if (gone.Count > 0) return "it uses parts of vShell this one doesn't have (" + string.Join(", ", gone.GetRange(0, Math.Min(4, gone.Count))) + (gone.Count > 4 ? " …" : "") + ")";
            }
            return null;
        }

        /// <summary>
        /// Get the plugin's DLL for a vShell version: from next to vShell.exe (the running version only), else from the update site - the
        /// newest build for the running version, or (builds: file -> build, from SiteBuildsAsync) the release being updated to. Returns null
        /// when it's in place, else why not.
        /// </summary>
        public static async Task<string> FetchAsync(PluginInfo p, Version v, Dictionary<string, PluginBuild> builds)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string dest = PathFor(p, v);
                if (File.Exists(dest) && Check(dest, p, v) == null) return null;

                // a fresh build next to vShell.exe (dist\) - but not an old copy left beside an installed vShell when the update site
                // has a newer build that runs here (adding Fans gave 1.0.3 from C:\apps\vShell, then offered 1.0.10 straight away)
                string exe = Updater.ExePath;
                if (v == Updater.Current && exe != null)
                {
                    string local = Path.Combine(Path.GetDirectoryName(exe), p.File + ".dll");
                    if (File.Exists(local) && (p.Latest == null || p.Latest.Url.Length == 0)) await RefreshCatalogAsync(true);   // to compare with
                    if (File.Exists(local) && Check(local, p, v) == null && !SiteHasNewer(p, local))
                    {
                        File.Copy(local, dest, true);
                        Log.Write("Plugins: " + p.Name + " " + V(v) + " from next to vShell.exe");
                        return null;
                    }
                }

                // another version (an update getting its plugins ready): that release's builds on the update site
                if (v != Updater.Current)
                {
                    PluginBuild nb;
                    if (builds == null || !builds.TryGetValue(p.File, out nb)) return "vShell " + V(v) + "'s plugins on the update site don't include it";
                    return await DownloadAsync(p, nb.Url, nb.ShaUrl, nb.Sha256, dest, v);
                }

                // the running version: the newest build on the update site (only the newest is kept there), when it runs here
                if (p.Latest == null || p.Latest.Url.Length == 0) await RefreshCatalogAsync(true);
                PluginBuild b = p.Latest;
                if (b == null || b.Url.Length == 0)
                    return CatalogState.Length > 0 ? CatalogState : "it isn't on the update site for this vShell's plugin API";
                List<string> gone = PluginFileInfo.MissingIn(typeof(PluginStore).Assembly, PluginFileInfo.UsesFrom(b.Uses));
                if (gone.Count > 0)
                    return "the build on the update site (" + b.Version + ") needs a newer vShell" + (b.BuiltFor.Length > 0 ? " (" + b.BuiltFor + ")" : "") + " - update vShell first";
                return await DownloadAsync(p, b.Url, b.ShaUrl, b.Sha256, dest, v);
            }
            catch (Exception ex)
            {
                string m = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Log.Write("Plugins: " + p.Name + " " + V(v) + ": " + m);
                return m;
            }
        }

        /// <summary>
        /// The update site has a newer build of the plugin than the DLL at local (its own version), and that build runs on this vShell.
        /// Unknown (no catalog - offline, or not read yet) = false: the local copy is used.
        /// </summary>
        static bool SiteHasNewer(PluginInfo p, string local)
        {
            try
            {
                PluginBuild b = p.Latest;
                if (b == null || b.Url.Length == 0) return false;
                Version site = Updater.ParseVersion(b.Version), mine = PluginFileInfo.Read(local).Version;
                if (site == null || mine == null || site <= mine) return false;
                if (PluginFileInfo.MissingIn(typeof(PluginStore).Assembly, PluginFileInfo.UsesFrom(b.Uses)).Count > 0) return false;
                Log.Write("Plugins: " + p.Name + " " + mine + " next to vShell.exe is older than the update site's " + site + " - using the site's");
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Download a plugin's DLL to dest: checked against its SHA-256 (sha, else the .sha256 file at hashUrl) and that it's the plugin and
        /// runs on vShell v, then put in place (a loaded plugin runs from memory: its file can be replaced). null = done, else why not.
        /// </summary>
        static async Task<string> DownloadAsync(PluginInfo p, string url, string hashUrl, string sha, string dest, Version v)
        {
            string tmp = dest + ".download";
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
                {
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "vShell/" + Updater.CurrentText);
                    string expected = string.IsNullOrEmpty(sha) ? null : sha;
                    if (expected == null && !string.IsNullOrEmpty(hashUrl))
                    {
                        expected = Updater.FirstHex64(await http.GetStringAsync(hashUrl));
                        if (expected == null) return "the release's " + p.File + ".dll.sha256 has no SHA-256 in it";
                    }
                    byte[] data = await http.GetByteArrayAsync(url);
                    File.WriteAllBytes(tmp, data);
                    if (expected != null && !Updater.HashFile(tmp).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(tmp);
                        return "the download doesn't match the release's SHA-256, so it wasn't used";
                    }
                }
                string why = Check(tmp, p, v);
                if (why != null) { TryDelete(tmp); return "the download isn't right: " + why; }
                TryDelete(dest);
                File.Move(tmp, dest);
                Log.Write("Plugins: downloaded " + p.Name + " for " + V(v));
                return null;
            }
            catch (Exception ex)
            {
                TryDelete(tmp);
                return ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            }
        }
        /// <summary>What a DLL you picked is, before it's added (null + why when it can't be used).</summary>
        public static PluginInfo Inspect(string file, out string why)
        {
            why = null;
            try
            {
                PluginFileInfo n = PluginFileInfo.Read(file);
                PluginInfo known = Catalog.Find(delegate (PluginInfo p) { return string.Equals(p.File, n.File, StringComparison.OrdinalIgnoreCase); });
                PluginInfo info = known ?? n.Manifest;
                if (info == null) { why = n.File + " isn't a vShell plugin (it has no plugin manifest: [assembly: VShellPlugin(…)])."; return null; }
                string bad = Check(file, info, Updater.Current);
                if (bad != null) { why = "That file can't be used: " + bad + "."; return null; }
                if (known == null && Catalog.Exists(delegate (PluginInfo p) { return string.Equals(p.Id, info.Id, StringComparison.OrdinalIgnoreCase); }))
                { why = "Another plugin already uses the id \"" + info.Id + "\"."; return null; }
                info.Outside = n.OutsideApi(typeof(PluginStore).Assembly);
                return info;
            }
            catch (BadImageFormatException) { why = "That file isn't a .NET DLL."; return null; }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        /// <summary>A plugin DLL you picked: which one it is (out), copied into place. null = done, else why not. One vShell doesn't
        /// know (not V's, not in the release) is remembered by its manifest, next to its DLL.</summary>
        public static string FromFile(string file, out PluginInfo which)
        {
            string why;
            which = Inspect(file, out why);
            if (which == null) return why;
            try
            {
                Directory.CreateDirectory(Dir);
                if (!Catalog.Contains(which))
                {
                    which.FromFile = true;
                    which.InRelease = false;
                    SaveManifest(which);
                    lock (Catalog) Catalog.Add(which);
                }
                File.Copy(file, PathFor(which, Updater.Current), true);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---- manifests and catalogs on disk -------------------------------------------------------------------------
        /// <summary>The JSON form of a plugin's manifest (plugins.json, a remembered manifest).</summary>
        sealed class Entry
        {
            public string id { get; set; } = "";
            public string name { get; set; } = "";
            public string file { get; set; } = "";
            public string description { get; set; } = "";
            public string glyph { get; set; } = "";
            public string group { get; set; } = "";
            public string author { get; set; } = "";
            public string adds { get; set; } = "";
            public string[] types { get; set; } = new string[0];
            // in plugins.json (build.cmd): the build in the release
            public string version { get; set; } = "";
            public string sha256 { get; set; } = "";
            public string built_for { get; set; } = "";
            public string[] uses { get; set; } = new string[0];
            /// <summary>The vShell version whose release has the DLL: an unchanged plugin isn't built again, so it stays in an older one.</summary>
            public string in_release { get; set; } = "";
            // filled in when it's read from a release (where to download it)
            public string url { get; set; } = "";
            public string sha_url { get; set; } = "";
            public string release { get; set; } = "";
        }

        sealed class CatalogFile
        {
            public string vshell { get; set; } = "";
            /// <summary>The release it was read from (in vShell's own copy).</summary>
            public string release { get; set; } = "";
            /// <summary>Its plugin API version.</summary>
            public int api { get; set; }
            public List<Entry> plugins { get; set; } = new List<Entry>();
        }

        static Entry ToEntry(PluginInfo p)
        {
            return new Entry { id = p.Id, name = p.Name, file = p.File, description = p.Description, glyph = p.Glyph, group = p.Group, author = p.Author, adds = p.Adds, types = p.Types };
        }

        static PluginInfo FromEntry(Entry e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.id) || string.IsNullOrWhiteSpace(e.file)) return null;
            return new PluginInfo
            {
                Id = e.id.Trim().ToLowerInvariant(), Name = e.name ?? e.id, File = e.file.Trim(), Description = e.description ?? "", Glyph = e.glyph ?? "",
                Group = string.IsNullOrWhiteSpace(e.group) ? "Other" : e.group, Author = PluginInfo.AuthorName(e.author), Adds = e.adds ?? "", Types = e.types ?? new string[0],
                Latest = string.IsNullOrEmpty(e.url) ? null : new PluginBuild
                {
                    Version = e.version ?? "", Sha256 = (e.sha256 ?? "").ToLowerInvariant(), Url = e.url, ShaUrl = e.sha_url ?? "", BuiltFor = e.built_for ?? "",
                    Release = e.release ?? "", Uses = e.uses ?? new string[0]
                }
            };
        }

        static string ManifestPath(PluginInfo p) { return Path.Combine(Dir, p.File + ".plugin.json"); }

        static void SaveManifest(PluginInfo p)
        {
            try { File.WriteAllText(ManifestPath(p), System.Text.Json.JsonSerializer.Serialize(ToEntry(p), new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { Log.Write("Plugins: keep " + p.Name + "'s manifest: " + ex.Message); }
        }

        /// <summary>vShell's copy of the plugin list it last read (with where to download each build).</summary>
        static string CatalogCachePath { get { return Path.Combine(Dir, "catalog.json"); } }

        /// <summary>Into the catalog: one we know (V's) gets marked as in the release; a new one is added (downloadable).</summary>
        static void Merge(PluginInfo p, bool inRelease, bool fromFile)
        {
            lock (Catalog)
            {
                PluginInfo have = Catalog.Find(delegate (PluginInfo x) { return string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase); });
                if (have != null)
                {
                    if (inRelease) { have.InRelease = true; have.FromFile = false; }
                    if (have.Description.Length == 0) have.Description = p.Description;
                    if (p.Latest != null) have.Latest = p.Latest;
                    return;
                }
                p.InRelease = inRelease;
                p.FromFile = fromFile;
                Catalog.Add(p);
            }
        }

        static bool _loadedDisk;

        /// <summary>At start, before the settings are read: the plugins added from files, and this version's release catalog as last read.</summary>
        public static void LoadKnown()
        {
            if (_loadedDisk) return;
            _loadedDisk = true;
            try
            {
                if (!Directory.Exists(Dir)) return;
                foreach (string f in Directory.GetFiles(Dir, "*.plugin.json"))
                {
                    try { PluginInfo p = FromEntry(System.Text.Json.JsonSerializer.Deserialize<Entry>(File.ReadAllText(f))); if (p != null) Merge(p, false, true); }
                    catch (Exception ex) { Log.Write("Plugins: " + Path.GetFileName(f) + ": " + ex.Message); }
                }
                if (File.Exists(CatalogCachePath))
                {
                    CatalogFile c = System.Text.Json.JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(CatalogCachePath));
                    if (c != null && c.api == PluginApi.Version) { ReadCatalog(c); CatalogFrom = c.release ?? ""; }
                }
            }
            catch (Exception ex) { Log.Write("Plugins: " + ex.Message); }
        }

        static int ReadCatalog(CatalogFile c)
        {
            int n = 0;
            if (c != null && c.plugins != null)
                foreach (Entry e in c.plugins) { PluginInfo p = FromEntry(e); if (p != null) { Merge(p, true, false); n++; } }
            return n;
        }

        /// <summary>Pre-releases count too (Settings > Updates > Include pre-releases) - set by PluginManager.</summary>
        public static Func<bool> IncludePre = delegate { return false; };

        /// <summary>The state of the release's list: "" read, "Reading…", or why it couldn't be read.</summary>
        public static string CatalogState { get; private set; } = "";
        /// <summary>The release the plugin list came from ("v0.38.86"), and when it was last checked (local time; MinValue = not yet).</summary>
        public static string CatalogFrom { get; private set; } = "";
        public static DateTime CheckedAt { get; private set; } = DateTime.MinValue;
        static DateTime _catalogAt = DateTime.MinValue;

        /// <summary>vShell's plugins for a release on the update site and a plugin API: vshell/plugins/2/ (vshell/pre/plugins/2/).</summary>
        static string PluginFolder(UpdateInfo release, int api) { return release.Folder + "plugins/" + api + "/"; }

        static HttpClient SiteHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "vShell/" + Updater.CurrentText);
            http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            return http;
        }

        /// <summary>
        /// A release's plugins.json on the update site for plugin API 'api', each build with its download address (the DLL beside it).
        /// null when there's none for that API. Throws on failure.
        /// </summary>
        static async Task<CatalogFile> ReadSiteCatalogAsync(HttpClient http, UpdateInfo release, int api)
        {
            string folder = PluginFolder(release, api);
            using (HttpResponseMessage r = await http.GetAsync(folder + "plugins.json"))
            {
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("the update site answered HTTP " + (int)r.StatusCode);
                CatalogFile c = System.Text.Json.JsonSerializer.Deserialize<CatalogFile>(await r.Content.ReadAsStringAsync());
                if (c == null || c.plugins == null || c.api != api) return null;
                string tag = release.Tag + (release.PreRelease ? " pre-release" : "");
                foreach (Entry e in c.plugins)
                {
                    e.url = folder + Uri.EscapeDataString(e.file + ".dll");
                    e.sha_url = "";
                    e.release = tag;
                }
                c.release = tag;
                return c;
            }
        }

        /// <summary>The builds of a release being updated to, for its plugin API (file -> build). Throws on failure.</summary>
        static async Task<Dictionary<string, PluginBuild>> SiteBuildsAsync(UpdateInfo release, int api)
        {
            var d = new Dictionary<string, PluginBuild>(StringComparer.OrdinalIgnoreCase);
            using (HttpClient http = SiteHttp())
            {
                CatalogFile c = await ReadSiteCatalogAsync(http, release, api);
                if (c != null)
                    foreach (Entry e in c.plugins)
                    {
                        PluginInfo p = FromEntry(e);
                        if (p != null && p.Latest != null) d[p.File] = p.Latest;
                    }
            }
            return d;
        }

        /// <summary>
        /// Read the plugin list from the update site: the newest release's (the pre-release's only if you get those - this version's own
        /// always) plugins.json for this plugin API - the plugins anyone can add, and the newest build of each (a plugin can be updated by
        /// putting a new build there, without a new vShell). Once in a while, or now (force). true = something changed.
        /// </summary>
        public static async Task<bool> RefreshCatalogAsync(bool force = false)
        {
            if (!force && (DateTime.UtcNow - _catalogAt).TotalMinutes < 30) return false;
            _catalogAt = DateTime.UtcNow;
            string before = CatalogSignature();
            CatalogState = "Reading…";
            try
            {
                List<UpdateInfo> releases = await Updater.ReleasesAsync("vShell", true);
                bool pre = IncludePre();
                using (HttpClient http = SiteHttp())
                {
                    foreach (UpdateInfo r in releases)      // newest first
                    {
                        if (r.PreRelease && !pre && r.Version != Updater.Current) continue;
                        CatalogFile c;
                        try { c = await ReadSiteCatalogAsync(http, r, PluginApi.Version); }
                        catch (Exception ex) { Log.Write("Plugins: " + r.Tag + "'s plugins.json: " + ex.Message); continue; }
                        if (c == null) continue;        // no plugins for this plugin API there
                        ReadCatalog(c);
                        CatalogFrom = c.release;
                        Directory.CreateDirectory(Dir);
                        File.WriteAllText(CatalogCachePath, System.Text.Json.JsonSerializer.Serialize(c));
                        CatalogState = "";
                        CheckedAt = DateTime.Now;
                        return CatalogSignature() != before;
                    }
                }
                CatalogState = "";      // no release has a plugin list for this plugin API (yet)
                CheckedAt = DateTime.Now;
                return false;
            }
            catch (Exception ex)
            {
                CatalogState = "Couldn't read the list of plugins from the update site: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                _catalogAt = DateTime.UtcNow.AddMinutes(-25);      // try again in a few minutes
                return false;
            }
        }

        static string CatalogSignature()
        {
            var b = new System.Text.StringBuilder();
            lock (Catalog) foreach (PluginInfo p in Catalog) b.Append(p.Id).Append(':').Append(p.Latest != null ? p.Latest.Sha256 : "").Append('|');
            return b.ToString();
        }

        // ---- updates ------------------------------------------------------------------------------------------------
        /// <summary>Updated in this run (id -> the version now on disk): used from the next start.</summary>
        static readonly Dictionary<string, string> Updated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string UpdatedTo(PluginInfo p) { string v; lock (Updated) return Updated.TryGetValue(p.Id, out v) ? v : null; }
        public static bool AnyUpdated { get { lock (Updated) return Updated.Count > 0; } }

        static readonly Dictionary<string, KeyValuePair<string, string>> Installed = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The build on disk (the one used): its version and SHA-256, or null.</summary>
        public static KeyValuePair<string, string>? InstalledBuild(PluginInfo p)
        {
            string file = Usable(p);
            if (file == null) return null;
            string key;
            try { var fi = new FileInfo(file); key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; } catch { return null; }
            KeyValuePair<string, string> b;
            lock (Installed) if (Installed.TryGetValue(key, out b)) return b;
            try
            {
                PluginFileInfo n = PluginFileInfo.Read(file);
                b = new KeyValuePair<string, string>(n.Version == null ? "" : V(n.Version), Updater.HashFile(file).ToLowerInvariant());
                lock (Installed) Installed[key] = b;
                return b;
            }
            catch { return null; }
        }

        static Version Ver(string s) { Version v; return Version.TryParse(s ?? "", out v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null; }

        /// <summary>
        /// The newer build to update to, or null (up to date, not in a release, not added). blocked: there's a newer build, but it needs
        /// a newer vShell (it uses parts this one doesn't have) - why, for the Plugins page.
        /// </summary>
        public static PluginBuild UpdateFor(PluginInfo p, out string blocked)
        {
            blocked = null;
            PluginBuild b = p.Latest;
            // (a plugins.json from before 0.38.86 doesn't say which build it has: no update can be told from it)
            if (b == null || b.Url.Length == 0 || b.Sha256.Length == 0) return null;
            KeyValuePair<string, string>? have = InstalledBuild(p);
            if (have == null) return null;
            if (b.Sha256.Length > 0 && string.Equals(b.Sha256, have.Value.Value, StringComparison.OrdinalIgnoreCase)) return null;   // the same file
            Version vb = Ver(b.Version), vh = Ver(have.Value.Key);
            if (vb != null && vh != null && vb < vh) return null;       // older than what's here (a test build)
            if (b.Sha256.Length == 0 && vb != null && vh != null && vb == vh) return null;
            List<string> gone = PluginFileInfo.MissingIn(typeof(PluginStore).Assembly, PluginFileInfo.UsesFrom(b.Uses));
            if (gone.Count > 0)
            {
                blocked = b.Version + " needs a newer vShell" + (b.BuiltFor.Length > 0 ? " (" + b.BuiltFor + ")" : "") + " - it uses " + string.Join(", ", gone.GetRange(0, Math.Min(3, gone.Count))) + (gone.Count > 3 ? " …" : "");
                return null;
            }
            return b;
        }

        /// <summary>Download its newer build over the one on disk (the loaded one keeps running until the next start). null = done.</summary>
        public static async Task<string> UpdateAsync(PluginInfo p)
        {
            string blocked;
            PluginBuild b = UpdateFor(p, out blocked);
            if (b == null) return blocked ?? "it's up to date";
            Directory.CreateDirectory(Dir);
            string err = await DownloadAsync(p, b.Url, b.ShaUrl, b.Sha256, PathFor(p, Updater.Current), Updater.Current);
            if (err != null) { Log.Write("Plugins: update " + p.Name + " to " + b.Version + ": " + err); return err; }
            lock (Updated) Updated[p.Id] = b.Version;
            Log.Write("Plugins: " + p.Name + " updated to " + b.Version + " (from " + b.Release + ") - used from the next start");
            return null;
        }
        /// <summary>
        /// build.cmd: write dir\plugins.json - every plugin DLL in dir that has a manifest and is built for this vShell (V's, and others' put
        /// there to share). Returns a line per DLL (what was listed, what was left out and why).
        /// </summary>
        public static List<string> WriteCatalog(string dir)
        {
            var lines = new List<string>();
            var c = new CatalogFile { vshell = V(Updater.Current), api = PluginApi.Version };
            // scripts\plugins.ps1's list (file|what it was built from|the release it went into): an unchanged plugin stays in an older release
            var inRelease = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string builds = Path.Combine(dir, "plugin-builds.txt");
            if (File.Exists(builds))
                foreach (string l in File.ReadAllLines(builds)) { string[] p = l.Split('|'); if (p.Length == 3) inRelease[p[0]] = p[2]; }
            foreach (string f in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    PluginFileInfo n = PluginFileInfo.Read(f);
                    if (n.BuiltFor == null) continue;      // not a plugin (WebView2 …)
                    PluginInfo known = Catalog.Find(delegate (PluginInfo p) { return string.Equals(p.File, n.File, StringComparison.OrdinalIgnoreCase); });
                    PluginInfo m = n.Manifest ?? known;
                    if (m == null) { lines.Add(Path.GetFileName(f) + ": left out - it has no plugin manifest"); continue; }
                    string bad = Check(f, m, Updater.Current);
                    if (bad != null) { lines.Add(Path.GetFileName(f) + ": left out - " + bad); continue; }
                    if (known != null && n.Manifest == null) m = known;
                    Entry entry = ToEntry(m);
                    // the build: so vShell can tell an update, and whether it runs on it, before downloading
                    entry.version = n.Version == null ? "" : V(n.Version);
                    entry.sha256 = Updater.HashFile(f).ToLowerInvariant();
                    entry.built_for = n.BuiltForText;
                    entry.uses = n.UsesLines();
                    string ir;
                    entry.in_release = inRelease.TryGetValue(Path.GetFileName(f), out ir) ? ir : V(Updater.Current);
                    c.plugins.Add(entry);
                    lines.Add(Path.GetFileName(f) + ": " + m.Name + " " + entry.version + " (" + m.Id + "), plugin API " + n.Api +
                              (entry.in_release != V(Updater.Current) ? " - unchanged, in the " + entry.in_release + " release" : ""));
                    // for V checking someone's plugin: what it uses that may change in any release (it'd need a rebuild then)
                    List<string> outside = n.OutsideApi(typeof(PluginStore).Assembly);
                    if (outside.Count > 0) lines.Add("    uses parts outside the plugin API: " + string.Join(", ", outside));
                }
                catch (BadImageFormatException) { }
                catch (Exception ex) { lines.Add(Path.GetFileName(f) + ": left out - " + ex.Message); }
            }
            File.WriteAllText(Path.Combine(dir, "plugins.json"), System.Text.Json.JsonSerializer.Serialize(c, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return lines;
        }

        /// <summary>The updater: fetch the added plugins for the new version from the update site, before the exe is swapped. "" = all there.</summary>
        public static async Task<string> StageForAsync(UpdateInfo u)
        {
            var missing = new List<string>();
            int newApi = ReleaseApi(u);
            Dictionary<string, PluginBuild> builds = null;
            string buildsError = null;
            foreach (string id in Added())
            {
                PluginInfo p = Find(id);
                if (p == null) continue;
                if (p.FromFile)
                {
                    // it stays and keeps working while the plugin API is the same - say so now if the new version's isn't
                    string file = Usable(p);
                    if (file == null) continue;
                    int has;
                    try { has = PluginFileInfo.Read(file).Api; } catch { continue; }
                    if (newApi > 0 && has != newApi)
                        missing.Add(p.Name + ": added from a file, for plugin API " + has + " - vShell " + V(u.Version) + " has API " + newApi +
                                    ", so it needs a new build of it (add that from a file after updating)");
                    continue;
                }
                if (builds == null && buildsError == null)
                {
                    try { builds = await SiteBuildsAsync(u, newApi); }
                    catch (Exception ex) { buildsError = ex.InnerException != null ? ex.InnerException.Message : ex.Message; }
                }
                string err = buildsError != null ? "couldn't read vShell " + V(u.Version) + "'s plugin list (" + buildsError + ")" : await FetchAsync(p, u.Version, builds);
                if (err != null) missing.Add(p.Name + ": " + err);
            }
            return string.Join("; ", missing);
        }

        /// <summary>The plugin API of the version being updated to: its latest.json's plugin_api, else what this vShell knows of it.</summary>
        static int ReleaseApi(UpdateInfo u)
        {
            string s;
            int api;
            if (u.Fields != null && u.Fields.TryGetValue("plugin_api", out s) && int.TryParse(s, out api) && api > 0) return api;
            return PluginApi.Of(u.Version);
        }

        /// <summary>Remove all of a plugin's files (every version) - and one added from a file is forgotten (it can't be downloaded again).</summary>
        public static void Delete(PluginInfo p)
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                foreach (string f in Directory.GetFiles(Dir, p.File + "-*.dll")) TryDelete(f);
                if (p.FromFile) { TryDelete(ManifestPath(p)); lock (Catalog) Catalog.Remove(p); }
            }
            catch (Exception ex) { Log.Write("Plugins: delete " + p.Name + ": " + ex.Message); }
        }

        /// <summary>Once this version has started properly: older versions' files aren't needed for going back any more.</summary>
        public static void TidyOld()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                // per plugin: the files for older versions go, except the newest one when there's none for this version (it may be the one in use)
                var groups = new Dictionary<string, List<KeyValuePair<Version, string>>>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(Dir, "*.dll"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    int dash = n.LastIndexOf('-');
                    Version v = dash > 0 ? Updater.ParseVersion(n.Substring(dash + 1)) : null;
                    if (v == null || v > Updater.Current) continue;
                    List<KeyValuePair<Version, string>> g;
                    if (!groups.TryGetValue(n.Substring(0, dash), out g)) groups[n.Substring(0, dash)] = g = new List<KeyValuePair<Version, string>>();
                    g.Add(new KeyValuePair<Version, string>(v, f));
                }
                foreach (var g in groups.Values)
                {
                    g.Sort(delegate (KeyValuePair<Version, string> a, KeyValuePair<Version, string> b) { return b.Key.CompareTo(a.Key); });
                    for (int i = 1; i < g.Count; i++) TryDelete(g[i].Value);       // [0]: this version's, or the newest older one
                }
                foreach (string f in Directory.GetFiles(Dir, "*.download")) TryDelete(f);
            }
            catch { }
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    }
}
