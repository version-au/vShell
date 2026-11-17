# vShell - notes for Claude Code

vShell is a Windows 11 bar / widget / desktop app (C# / .NET 10, WPF), started as a port of SharpEnviro's SharpBar. It runs
**alongside Explorer** - never as the Windows shell. Owner: V (Steven, github.com/version-au). Repo: https://github.com/version-au/vShell (GPL-3.0).
Work happens on **`main`** (since 2026-11-03 - it was `branch-4`, "Build 4", from 0.38.135: .NET 10 and plugin API 2); `branch-4`,
`branch-3` (from 0.38.84: the plugin API, .NET 8), `build-2` and `build-1` are the older lines, kept on V's own git server (not on GitHub).

This machine's notes (working folder, remotes): `CLAUDE.local.md` (not in git). Check `git status` / fetch first.

## The shared library: vSuite.Core (since 0.38.167)

- **The suite's design guide** is `vSuite.Core\docs\design-guide.md`: vShell's own windows (Settings, editors) follow it; bars, widgets and popups keep their own drawing.
- **The changelog says which builds were which** (vSuite.Core 1.0.28): every build marks its version's heading in CHANGELOG.md " · pre-release" (`build.cmd pre`) or " · release" (a release replaces the pre-release mark) - commit CHANGELOG.md after each build. Write new headings without a mark.

- vShell, vXplorer and vView share **vSuite.Core** (https://github.com/version-au/vSuite.Core, MIT), here as
  the git submodule `vSuite.Core\` - read its CLAUDE.md. A fresh clone needs `git submodule update --init`. vShell.csproj references
  `vSuite.Core\vSuite.Core` (not vSuite.Shell - no Vanara here) and keeps the submodule's files out of its own compile.
- Moving onto it in small steps, a release each. **Done:** the log (`src/Core/Log.cs` is now a thin wrapper - plugins call
  `vShell.Core.Log` by name, so its plugin API lines stay) and updates (`Updater` / `UpdateInfo` are the library's, aliased in
  `src/GlobalUsings.cs`; Program calls `vSuite.App.Init("vShell", "VSHELL", "https://vsuite.vrsion.com")` (the update site, since 0.38.190 - GitHub before) and sets
  `Updater.BeforeSwap` to bring the plugins along; vShell's own `UpdateCenter` / toast stay) - 0.38.167; questions (0.38.168: `Questions`
  keeps its plugin API and placement (`Own` for widgets) but shows vSuite's `Ask`; `Ask.FallbackTheme = Questions.SuiteTheme`, built from
  vShell's live `BarSettings` via the internal `Questions.CurrentSettings` - the dialog carries the vx.* brushes and Controls.xaml in its
  own resources, so they never restyle bars / widgets; Windows' message box if it fails); the Settings and welcome windows (0.38.168:
  `SettingsStyle.Dress` = `vSuite.Ui.Look.Dress` (Controls.xaml + Forms.xaml on the window, the vx.* brushes) with the vs* names as
  aliases of vx.*; SettingsWindow is borderless (WindowChrome, caption row 44 with min / max / close, `ApplyGlass` = WindowEffects,
  `UpdateMaximized` pads by the overhang), cards use `vx.MenuRadius`; Blur / Frosted = vSuite's `Glass` via `WindowEffects.Look` (0.38.169 - the blurred wallpaper under vsBg, as the bars; never Mica); the old settings XAML is gone - Forms.xaml is it). `Backdrop.cs` keeps `On` / `Check` and `Glass : vSuite.Ui.Glass` (each bar's / widget's own blur via `BarSettings.BlurFor`) - the drawing is the library's (0.38.171); Settings > Updates (0.38.172: `BuildUpdatesPage` - the vShell card + vSuite apps; `ShowUpdates` from the tray's Check for updates) > vSuite apps (`SettingsWindow.Suite.cs`: installed exe from the Inno uninstall key or %LOCALAPPDATA%\Programs, newest via `vSuite.Updater.NewestReleaseAsync`, Update = start the app with `--update`; Install (not installed) = `vSuite.SuiteApps.FindInstallerAsync` (newest release with Name-setup.exe, or the older Name-setup-*.exe - vSuite.Core 1.0.17; since 0.38.189 every app's installer is Name-setup.exe, so `releases/latest/download/Name-setup.exe` always works) + download + run; each app's Include pre-releases = `vSuite.SuiteApps` - read from its settings.json, set via its running copy (`--update-pre on|off` hand-off) or the file; FileSystemWatchers redraw the card). `VShellTray` (vShell's own tray icon) is vSuite's `Ui.TrayIcon` (0.38.207, vSuite.Core 1.0.30 - was Windows Forms' NotifyIcon with a plain menu): the logo drawn by `SetIcon`, the menu dressed with `Questions.SuiteTheme`. `WallpaperColor.cs` only picks the main monitor's picture (vShell's own per-monitor pictures / slideshows) - the colours are `vSuite.WallpaperColor` (0.38.174; `WallpaperPalette` is the library's, aliased in GlobalUsings); `SettingsPalette` is only the Plain fallback. **Next:** the
  (i) help cards and other Settings blocks into OptionsWindowBase for the apps, then
  vShell's own windows (Settings, welcome, editors) on the shared glass and styles - bars, widgets and popups keep their own drawing.
- **The Plugin SDK** (`sdk.zip` = `dist\sdk\`: vShell.dll + plugin-api.txt + docs\plugins.md) is on the update site at
  `vshell/sdk.zip` (Settings > About > Plugin SDK > Download). **Whenever the SDK changes** - the plugin API (anything `[PluginApi]`,
  docs\plugin-api.txt), the plugin API version, or docs\plugins.md - `sdk.zip` must be made again and uploaded: `scripts\site.ps1`
  does it on every build and plugins.cmd, so a `build.cmd upload` (or `plugins.cmd upload`) carries it. A docs-only change between
  builds: tell V, and re-zip + upload it (`plugins.cmd upload`, or zip `dist\sdk\` to `dist\site\vshell\sdk.zip` and run
  vSuite.Core's `site-upload.ps1 -Site dist\site`).
- Never let a vShell plugin API line depend on a vSuite type: check `--plugin-api` against docs\plugin-api.txt after a change.
- A change to a shared piece is made in vSuite.Core (with its tests), pushed there, then `git -C vSuite.Core pull` here and committed
  with vShell's own version bump.

## Build and run

- `build.cmd` (double-click or run it) - `dotnet publish` to `dist\vShell.exe` (self-contained, single file) plus
  every plugin's `dist\vShell.<Name>.dll` + `.dll.sha256`, and `dist\release-notes.md` (from CHANGELOG.md, into latest.json). Needs the .NET 10 SDK. It first brings `vSuite.Core\` to the library's newest main (`vSuite.Core\scripts\suite-core.ps1` - the shared one every app's build.cmd runs, since vSuite.Core 1.0.26;
  if it moved, it commits the submodule as `0.38.N: vsuite.core X` - not pushed; local changes in the submodule are left alone). **Only plugins that changed are built** (`scripts\plugins.ps1`: source hash + plugin API
  in `dist\plugin-builds.txt`, with the release each is in; `build.cmd all` rebuilds all) - an unchanged one stays in its older release
  (plugins.json `in_release` - only GitHub-era vShells used it; the site has every plugin). The installer (`installer\vShell.iss` via
  Inno Setup 6, `dist\setup\`) is **in every release** (V, 2026-11-03), `build.cmd nosetup` skips it.
- **The update site** (0.38.190): updates come from https://vsuite.vrsion.com, not GitHub. build.cmd also writes `dist\site\` (`scripts\site.ps1`
  -> vSuite.Core's `scripts\site-release.ps1`): `vshell\` latest.json (version, notes, plugin_api, each file's size + SHA-256), vShell.exe,
  vShell-setup.exe, changelog.md (= CHANGELOG.md: the notice lists every version since yours), `vshell\plugins\<API>\` (plugins.json + EVERY
  plugin DLL - only the newest is kept on the site). `build.cmd pre` / `plugins.cmd pre` -> `vshell\pre\`. `build.cmd upload` (`pre upload`) puts `dist\site\` on the site itself (vSuite.Core's `site-upload.ps1`, SFTP key `vsuite-site`) - only when V asks for a build; otherwise V copies it over the
  site's root by SFTP (by hand). `PluginStore` reads plugins.json there (`ReadSiteCatalogAsync`); an update stages the new version's
  plugins from its folder (`SiteBuildsAsync`). 0.38.190 was the **bridge** (the last GitHub release, so older
  copies update to it); since then site only - build.cmd makes nothing for GitHub.
- Quick compile check: `dotnet build vShell.csproj -c Release` (and each plugin: `dotnet build plugins\<Name>\vShell.<Name>.csproj -c Release`).
- Close the running vShell before replacing `dist\vShell.exe` (or use its updater).
- Log: `%APPDATA%\vShell\vshell.log` (rotates at 1 MB to `vshell.old.log`; written in batches every ~0.5 s; repeated lines collapse).
- Settings: `%APPDATA%\vShell\settings.json` (`BarSettings` in `src/Core/Settings.cs`, normalised on load).
- No NuGet packages (WebView2 / SSH.NET went with the terminal to vTerm in 0.38.194). Everything is plain .NET / Win32 + vSuite.Core.

## House rules (V's)

- **Only build / package when V asks** ("new build"). Otherwise just make the change and commit it.
- Every user-visible change: bump `<Version>` in `vShell.csproj` (0.38.N - next N once the current one has been built and given to V;
  further changes before the next build go into the same version), add a section at the top of **CHANGELOG.md**
  (`## 0.38.N` - the heading must start with `## ` and the exact version, `build.cmd` copies it into the release
  notes), and test steps in **TESTING.md**. Changelog entries are the release notes: **one short line per change** (what, not how or
  why) - the detail goes in TESTING.md.
- Times shown to the user are **12-hour** by default (use `TimeText.Clock / Stamp`, which follows the Use 24-hour setting).
- UI text and comments are plain, friendly English (see existing strings); settings get a short `Note(...)` explaining them.
- In UI text the connections are **Plugins** (the Settings page is "Plugins", hints say "Settings > Plugins") - built-in ones too. Code names
  keep "Integration" (`AddIntegration`, `BarSettings.Integrations`, `BuildIntegrationsPage`).
- Commit messages: `0.38.N: what changed` (short, lower-case after the colon), one commit per change set.
- **Plugin API additions are V's call.** When something new in the plugin API (`[PluginApi]` on a type / member, a new helper, a
  version bump) would help - a plugin could do more, or a change could be done more cleanly through it - don't just add it: tell V
  what it would add and the benefit (and any cost: API lines to keep, a version bump), and let V decide. Work around it meanwhile.

## Layout

- `src/Program.cs` - entry; single instance; app-wide handlers (`MenuAtPointer.Install`).
- `src/Core/` - no WPF types: settings (`Settings.cs`: `BarSettings`; `ModuleConfig.cs` (per-module settings, prefixed by module:
  `Term*` terminal, `Note*` notes …); `ModuleTypes.cs` (type names, lists of choices, display names); `BarPlacement.cs`,
  `WidgetSettings.cs`; `SettingsBackup.cs` (daily / manual backups, restore, recovery from an unreadable settings.json)),
  `Log`, `WeatherService` (Open-Meteo, plus plugins' `IWeatherSource`s), notifications (`Notify`, `NotifyWatch`, `NotifyListener` on port 8765), system (`SysInfo`, `SystemStats`, `Metrics`
  (GPU via PDH), `HwInfo` (sensors: HWiNFO's Gadget values in the registry, or LibreHardwareMonitor's web server data.json - `BarSettings.SensorSource` / `LhmPort`), `TaskList`, `ShellHook`, `TrayHost`, `ConPty`), `Native.cs` (P/Invoke), `Secret` (DPAPI for stored keys - plugin API; the work is vSuite.Core's `Secret` since 0.38.199).
- `src/Ui/` - WPF: `BarManager` (owns bars, widgets, timers, ApplyNow on settings change), `BarWindow` (a bar), `WidgetWindow` /
  `DesktopWindow` (desktop widgets - desktop layer, never activated), `PopupWindow` (module popups), one `*Module.cs` per module,
  `SettingsWindow*.cs` (the settings window, one partial file per part: `.System` (System / About / updates / backups), `.Inspector`
  (ShowModuleProps), `.ModuleProps` (per-type settings), `.ModulesPage`, `.Rows` (row builders), `.Themes`, `.Startup`, `.Plugins`,
  `.Notify` …), `Theme`, `Pills`, `Questions` (YesNo / Ask / Tell - use these, not MessageBox, from modules and widgets).

## Plugins (integrations you add / remove)

- `plugins\<Name>\vShell.<Name>.csproj` - a library referencing `vShell.csproj` (not copied); vShell's csproj leaves `plugins\**` out.
  **Plugin API**: plugins (V's too - no InternalsVisibleTo) use only what's marked `[PluginApi]` (a whole type, or single members of
  big classes like `BarSettings`, `ModuleConfig`, `BarManager`). `PluginApi.Version` (src/Core/PluginManifest.cs, 1 since 0.38.84, 2 since 0.38.135 - .NET 10): a
  plugin built against any vShell with the same API loads (`PluginStore.Compatible`; `PluginFileInfo.MissingIn` checks its references).
  build.cmd writes `dist\plugin-api.txt` and compares it with `docs\plugin-api.txt` (scripts\plugin-api.ps1): a line gone / changed
  stops the build - keep the old form, or raise the version and add it to `PluginApi.Since`. A new thing plugins need: mark it, commit
  the updated docs\plugin-api.txt. Every connection is one: Komodo, Seerr, Radarr, Sonarr, Tdarr, 3D printers, Downloads, UniFi,
  Jellyfin, Proxmox, Nginx Proxy Manager, Linux Update Dashboard, vPatch, Home Assistant (with vDrop), Mail (IMAP), Claude usage, PeaNUT and Uptime Kuma
  (`plugins\<Name>`). vShell itself connects to nothing. **Shelf** (0.38.175, own version 1.0.0, plugin API only) is the file shelf + Windows'
  clipboard history (WinRT through COM on an STA thread with a message loop - the Clipboard class refuses MTA) and Explorer's "Add to shelf"
  (`--plugin shelf add`, `PluginHost.AddCommandLine`); its shelves live in `%APPDATA%\vShell\shelf\`. Its edge tabs (1.1.0, `EdgeTabs.cs`:
  a non-activating tabs window, a drawer window, `DragWatch` = a WH_MOUSE_LL guess at a drag starting) hang from a bar via the plugin
  API's `BarManager.Bars()` / `BarsChanged` (`BarInfo`, 0.38.179 - raised from `OnBarPlaced`, bar add / remove, `HideAway`, `Under`). **Fans** (0.38.182, own version 1.0.0, plugin API only): every rpm reading from `HwInfo` (HWiNFO or LHM - `HwInfo` / `HwReading` are plugin API since 0.38.182) as tiles / rows / bar layouts with a drawn `FanIcon` (stepped 10 times a second by one shared timer - not a WPF animation, which made the Settings window's glass jump while dragged), histories via `BarManager.HistoryFor("fans|"+key)`, stop / slow notifications from its 1 s `FanStore` tick. **vTrace** (0.38.205, own version 1.0.0, plugin API only): shows the vTrace app's `%APPDATA%\vTrace\changes.json` (read-only, re-read when it changes) as a bar module (an outage still open, else "12 today") and a widget (latest changes, today by the hour); click = open vTrace. Since 1.1.0 (0.38.208): while vTrace isn't open (no window with the `vTrace.Instance` prop, `TraceData.AppOpen`) a refresh button (widget heading, bar module, popup) runs `vTrace.exe --refresh` (vTrace 0.1.8: one look, no window / tray, closes; `VSHELL_VTRACE_EXE` for tests) and "Checked 2:14 PM · 3 h ago" shows (vTrace's `looked.txt`); Settings > Plugins > vTrace > Check for changes (`AutoMinutes` in the "vtrace" integration bag: 0 / 5 / 10 / 30 / 60) runs the same refresh when the last look is that old (`TraceData.AutoCheck`, a 1-minute timer). 1.2.0 (0.38.211): reads vTrace 0.1.11's outage analysis from changes.json (`Change.Group` / `GroupOn` / `GroupCount` / `RunsOn` / `Cause` / `CauseWhy`) - `TraceDraw.DownShown` (one per group), `OutageTip`, the bar's "6 down on pve". 1.3.0: a click on an outage / its cause ("· View", `CauseId`) / a row opens vTrace on it (`TraceData.OpenApp(id)` = `vTrace.exe --show <id>`, vTrace 0.1.12). 1.4.0: `Change.Explanation` / `AiBy` (vTrace's local AI) - an "AI:" line under a down outage, and in `OutageTip`. 1.5.0 (0.38.218): Only issues (`TraceView.IssuesOnly`, per module; `TraceDraw.IsIssue` = an outage or `Failed`) - a setting and the All / Issues switch in the list's heading (saves via `BarManager.SaveSoon`). 1.5.1: the outages-still-going block (cause, AI) is on Issues only; on All they're the list's first rows (red, "still down"). **2.0.0 (0.38.219): the vTrace server** (`S:\Projects\vTrace`, git.vrsion.xyz/Steven/vTrace, V 2026-11-15): `Server.cs` - `VtSettings` (integration bag "vtrace": Url, Key protected, SendPc, PcSentUtc, SendColours, Notifications), `VtClient` (/api/v1/changes?since=7 days, events, look, pc, theme; Bearer vt_ key), `PcLog` (this PC's Windows event logs - the Windows vTrace's PcSource moved here: updates, installs, drivers, starts / shut-downs, restarts asked; System.Diagnostics.Eventing.Reader from the Windows Desktop runtime, no package), `TraceServer` (Poller 10 s wanted / 30 s; notifications from events marked toVShell, never replayed at start; colours; PC changes POSTed every 15 min and with the refresh, from PcSentUtc - 1 h, first time 30 days). `TraceData` routes to it while `TraceServer.On` (All, Error, OpenApp = the browser at /change/<id>, AppOpen = false so the refresh button always shows and asks the server to look, LastLook = the server's); without a server everything was the Windows app's file. **2.1.0 (0.38.220): server only** - the Windows vTrace retired (V 2026-11-15, its code in `S:\Projects\vTrace-win`): no changes.json, vTrace.exe --refresh / --show, AutoMinutes or window check; `TraceData` is a thin wrapper over `TraceServer` (kept for the modules); vShell's vSuite apps list and the UniFi plugin's "account for vTrace's history" are gone too. **Local AI** (0.38.211, own version 1.0.0, plugin API only, `plugins\LocalAi`): your own AI servers - `AiServer` works out Lemonade (/api/v1/health, /stats, /models, /system-info, /load, /unload) / Ollama (/api/ps, /api/tags, /api/generate keep_alive) / LM Studio (/api/v0/models) / OpenAI-style (/v1/models) from the address and asks /chat/completions (thinking off unless Think); `Plugin` polls (5 s wanted / 60 s), notifications (offline / back / storage), palette Load / Unload, `RunBox` (!ai); `AiModule` = bar text + popup cards / widget cards. Its integration bag "localai" Url / Key / Model / Think is read by vTrace 0.1.16 (keep those names). **vDeploy** (0.38.211, own version 1.0.0, plugin API only, `plugins\vDeploy`): V's vDeploy server (a Linux web app) through its /api/v1 with an app key (`VdClient`: overview, events, stacks/{host}/{stack}/redeploy|start|stop|restart, updates/check, PUT theme); `Plugin` polls (5 s wanted / 30 s), shows the events vDeploy marks `toVShell` as notifications (vDeploy's Options > Notifications chooses - never replays old ones at start), sends `Theme.From(settings)`'s colours whenever they change (`ThemeJson` = vDeploy's WebTheme field names), palette commands, `!deploy` (`RunBox`, Enter twice for update / restart / stop); `DeployModule` = bar dot + running / ⚠ / ⬆, popup / widget of stacks by host (click = its page, right-click = actions). 1.1.0 (0.38.212, as the Komodo plugin's - V): a stack's arrow unfolds its containers (`Unfolded`, shared); right-click a container: start / restart / stop (`?container=<service>` on vDeploy 0.1.9's stack actions), Open shell in the container (`TermOpen.Host` + `TermOpen.DockerShell`, root by default, vTerm's saved connection for the host's address wins), shell on the host, its log; `LogWindow` = a copy of `KomodoLogWindow` on vDeploy's `GET /api/v1/stacks/{host}/{stack}/logs[?container=]` (Docker's -t times shown in your clock or taken out). 1.2.0 (0.38.218): ⬆ on a container whose image is in its stack's `Updates` (vDeploy's image refs; `ImageKey` evens out docker.io/ / library/ / :latest / @digest). 1.3.0: rows as vDeploy's side panel (`SidePanel.razor`) - `VdIcons` draws its Icon.razor paths (host / stack / box / chevrons; 16 box, 1.25 stroke) scaled from its 13 px (`Px`); keep them in step if vDeploy's icons change. **2.0.0 (0.38.222): Proxmox nodes** (vDeploy 0.1.25's /overview `nodes`): `VdNode` / `VdGuest` (only the helper scripts' LXCs / VMs; `app` = its update check), `VdClient.GuestActAsync` (start / shutdown / reboot), `GuestUpdateAsync` (opens vDeploy's terminal page for it), `CheckNodeAsync`; the module's "Nodes" bag setting (V: show or hide them), DOCKER HOSTS / PROXMOX NODES headings when both, `NodeRow` / `GuestRow` / menus (console = `TermOpen.Host` + `pct enter <id>`), !deploy and palette rows; VdIcons "server" / "vm". **Icons are `\uE…` escapes in the source** - 1.0.0's were written as the raw private-use characters and came out empty. Integration bag "vdeploy" Url / Key (for vTrace). **vPatch** (0.38.218, own version 1.0.0, plugin API only, `plugins\vPatch`): V's vPatch server (a Linux web app: updates over SSH) through its /api/v1 with an app key (`VpClient`: overview - hosts in group order with updates / security / restart / problem / checking / upgrading and lastEvent; events; POST check (all) and hosts/{id}/check; PUT theme - vSuite.Web's WebTheme fields, as vDeploy's); `Plugin` polls (5 s wanted / 30 s), shows events vPatch marks `toVShell` (kind by vPatch's group first: security = alert, "bad" = problem - a security update's look is "warn"; never replays at start), sends the colours, palette (Open, Check every host, Upgrade… = vPatch's /upgrade page, Check <host>); `PatchModule` = bar dot + ⬆ n · security + the restart glyph (Segoe Fluent \uE7E8) + ⚠ down, popup / widget of hosts by group with tiles; upgrading itself was on vPatch's pages until 1.1.0 (0.38.223, V): Upgrade (popup, widget button "UpgradeButton", host menu, palette) asks, then `VpClient.UpgradeAsync` POSTs vPatch 0.1.21's /hosts/{id}/upgrade or /upgrade - vPatch's own dry run first, only clean hosts go, removals wait for an OK on its page; `VpHost.Job*` (`WaitingForOk`, `UpgradeFailed` until checked again, `JobText`) and `VpSnapshot.BatchRunning` / `BatchStopped` from /overview. ClaudeUsage / PeaNUT / UptimeKuma (moved in from their own repos) keep their own
  `<Version>` (2.0.0+, never lower - an older version number is never offered as an update) and use only the plugin API.
- One class implements `IPlugin` (`src/Ui/Plugins.cs`): `Load(PluginHost)` registers its parts - `AddModule(PluginModule)` (type, names,
  glyph, gallery group / blurb, Create, Settings, pictures), `AddIntegration` / `AddNotifications` (sections built with
  `SettingsWindow.SettingsPage`), `OnSettings` (its Configure), `AddNotifyKind` / `AddNotifySource`, `AddCommands` (palette), `AddBang`
  (run box keyword + rows on `RunPanel`), `AddRunText` (pasted text it knows - a magnet link), `AddRunBoxDrop` (files dropped on a run box),
  `AddMediaHelper` (an `IMediaHelper`: Now playing's times / art / buttons - Jellyfin), `AddWatcher` (a check on the notification timer).
  `AddWeatherSource`, `AddNoteStore`, `AddCardLinks`, `OnPaletteOpening` (vShell undoes all of them on Remove). Helpers: `PluginHttp`,
  `Poller`, `JsonValue`, `ModuleVisibility`, `WidgetWindow.StyleScrollbars`. A plugin module built on `InfoModule` (Downloads) or `TilesModule` (Home Assistant's entities) is updated with vShell's
  own. `Unload` stops its timers; vShell takes the registrations away.
- Each plugin has a manifest: `[assembly: VShellPlugin("id", "Name", Description, Glyph, Group, Author, Adds, Types)]` (`src/Core/PluginManifest.cs`,
  read without running it). build.cmd writes `dist\plugins.json` from the manifests of the plugins in dist (V's + accepted ones from
  `third-party\`) - attach it to the release; the Plugins page reads it (`PluginStore.RefreshCatalogAsync`). Add from a file takes any
  plugin with a manifest (kept as `plugins\<File>.plugin.json`). A plugin is built for the vShell it references (not its own version).
  For others: `docs\plugins.md`, and `dist\sdk\vShell.dll` + `plugin-api.txt` to compile against.
- **Plugin updates** (0.38.86): plugins.json has each build's version / sha256 / built_for / uses; vShell reads the newest release with
  its plugin API (`PluginStore.RefreshCatalogAsync`, `PluginInfo.Latest`), `UpdateFor` / `UpdateAsync` (used from the next start),
  `PluginManager.CheckUpdatesAsync` (a minute after start + every 6 h; `PluginAutoUpdate`), `PluginManager.Restart` (`--restart`).
  A plugin built against a newer vShell loads if same API and `MissingIn` is empty. `plugins.cmd` = plugins + plugins.json only.
- `PluginStore` (Core): the catalog (id, DLL name, its module types, `InUse` for adding it for people who used it before), files in
  `%APPDATA%\vShell\plugins\vShell.<Name>-<version>.dll`, download from the release of that version (+ .sha256) or from next to vShell.exe.
  `Updater.DownloadAndStageAsync` fetches the added plugins for the new version before swapping the exe.
- `PluginManager` (Ui): loads added plugins at start (from memory - files can be deleted / replaced while running), Add / Remove, and the
  lookups the rest of vShell uses (`ModuleOf`, `CreateModule`, sections). A plugin's module type with the plugin not added stays in the
  settings and shows "add it in Settings" (`ModuleTypes.Keep`).
- New integrations should be plugins: copy `plugins\Komodo`, add a catalog entry, and nothing in vShell's switches; anything new it needs from vShell gets `[PluginApi]`.
  A run box keyword with its own results (posters, buttons) uses `RunPanel.Hold` / `AddElement` (Seerr's search).
- Plugin settings live in a `SettingsBag` (`cfg.Bag` for a module, `s.Integration("id")` for the connection), wrapped in a typed class in
  the plugin (`KoSettings` / `KoView`, `HaSettings` / `HaView` …) - not fields in `BarSettings` / `ModuleConfig`. Moving an
  integration in: list its old fields in its `PluginStore` catalog entry (`OldSettings`, `OldModuleSettings` or per type `OldModuleSettingsByType`, `Secrets`) - see
  docs/module-api.md. Each plugin is added for you once if you used it (`BarSettings.PluginsOffered`). A list of objects edited in place
  (the printers, Home Assistant's entities) is a `SettingsBag.Live` value: the same object every time, saved as it is. Old fields on
  one of vShell's own module types go in `OldModuleSettingsByType` too (vDrop's tab on a Notes module).

## Adding a module (checklist)

`ModuleTypes` const + `All` / `WidgetTypes` + `Display` / `ShortName` (ModuleTypes.cs) · its `ModuleConfig` fields + normalisation
(ModuleConfig.cs) · `BarWindow` and `WidgetWindow` module creation switches · `SettingsWindow`: `PopupModules` (if it has a popup) and the module
inspector `case` (SettingsWindow.Inspector.cs), gallery `Groups` and `Blurb` (SettingsWindow.ModulesPage.cs), `SettingsWindow.Gallery.cs` requirement text · `ModulePics.cs` previews (bar + widget) · `Icons.cs`
glyph · integration: Core client + a `Configure(Settings)` call in `BarManager.ApplyHa` + Plugins page section + Notifications page
section (`NotifySection`, `KindPick`) + `Notify.Kinds` keys + `NotifyWatch.Hook` + `NotifyCards.Glyph` + `ModuleTypes.NotifySources`.

## Things learned the hard way

- **Widgets never take the keyboard** (WS_EX_NOACTIVATE, desktop layer). A text box in a widget must call
  `WidgetWindow.BeginTyping(ITypingModule)` / `EndTyping`. Dialogs / questions from a widget must not be owned by it (they'd open hidden
  and block it) - use `Questions.YesNo` / `Questions.Own` (FormWindow / pickers already do).
- **Clicks in a widget**: a control must also handle `MouseLeftButtonDown` (e.Handled = true), or the press starts a widget drag and the
  click needs two tries. `WidgetWindow` ignores presses on scrollbars.
- **Menus at the pointer**: WPF can place ContextMenus at the screen's top-left in vShell's windows; `MenuAtPointer` (class handler)
  moves any MousePoint / AbsolutePoint menu to the real pointer.
- **Dialogs on the right monitor**: `DialogPlacement` (a CBT hook, installed at start) moves message boxes / pickers and CenterScreen /
  CenterOwner windows to their owner's monitor, else the pointer's. Don't use `MessageBoxOptions.DefaultDesktopOnly` - Windows shows those
  outside vShell, always on the main monitor; `Questions` (no owner / a widget) asks through an invisible topmost helper instead.
- **Bars put back** (0.38.214): `ApplyPlacement` skips when nothing it knows changed (`_placedKey`), so once a second `CheckPlace` (from
  `UpdateStats`) compares the window with what was placed (`_placedWin`, read back after SetWindowPos) and the monitor's bounds - Windows
  putting windows back after a monitor returns could leave a bottom bar part-way up (a user's log, 0.38.212).
  The real cause (0.38.216, the user's screenshot): Explorer re-makes the taskbar of a monitor that wakes up and can fail to auto-hide it
  ("A toolbar is already hidden on this side"): hidden by vShell, it still holds its strip, and ABM_QUERYPOS pushed the bar in by it -
  with Hide the Windows taskbar on, `ShellServices.ExplorerTaskbarOnEdge` makes ApplyPlacement ignore that push.
- **Layout loops**: never `Measure` children inside `ArrangeOverride` with a different size (TermTabsPanel did - froze layout past
  ~6 tabs). A bar's step-down fit uses the bar's room (layout slot), not the overfull panel's width (was an endless loop at start-up).
- **Widget rows**: modules with `IMultiRow.RowCount == 0` size themselves and sit at the top of their row; others get fixed rows.
  A module that hides itself (Visibility Collapsed + `host.ContentChanged()`) takes its row with it; an all-hidden widget hides.
- **Module refreshes**: rebuild only when what's shown changed (content signature / Stamp), not on every poll.
- **Pollers**: Core clients poll fast only while a module shows them (`Latest` / `All()` marks "wanted"), slower for notifications.
- **Terminal = vTerm** (0.38.194): the built-in terminal moved out into vTerm (https://github.com/version-au/vTerm, its CLAUDE.md has the terminal's
  history). vShell keeps only the bar's Terminal module (`TerminalModule`: button + keys) and `VTerm.cs`: `DropFor(bar, cfg, cmd)` works out
  the drop-down's place (the card in device pixels against the bar's edge) and look (bar corners / outline / blur, or the widget style),
  `Send` sends a vSuite `TermRequest` on the TermLink pipe (vTerm not running: starts `vTerm.exe --background` and retries for 15 s; not
  installed: offers Settings > Updates > vSuite apps). `TermOpen.Host` = "open-host" (vTerm asks about saving a connection);
  `TermOpen.Terminal` shows the drop-down and returns null (`TerminalView` is an empty stub kept for that plugin API line); `TermSsh.Find`
  / `All` read vTerm's settings.json Connections. The old `Term*` fields stay in BarSettings for vTerm's first-start import. No terminal
  in widgets (Settings.Normalize removes a widget's Terminal module). Tests: `VSHELL_VTERM_EXE` = a built vTerm.exe; `VTERM_DATA_DIR`
  is honoured for the pipe and the settings file. Shutdown sends "quit-if-idle".
- **HTTP**: one cached `HttpClient` per integration; follow https -> http redirects manually where needed (`Moonraker.GetFollow`).
- vShell is per-monitor DPI aware; window positions are device pixels (Win32), WPF sizes are DIPs.
