# vShell  (v0.38)

A Windows 11 bar, widget and desktop app, ported in spirit from **SharpE's SharpBar** (SrcForger/SharpEnviro). It is a
C#/.NET 10 + WPF rewrite - the original is Delphi 2007 code tied to SharpE's skin engine and services. vShell runs **alongside
Explorer** (it is never the Windows shell). GPL-3.0 (derived from SharpEnviro's design).

No NuGet packages (the terminal - WebView2, SSH.NET - is vTerm's, a vSuite app of its own).
Everything else is plain .NET / Win32, plus **vSuite.Core** (below).

## Install

Download `vShell-setup.exe` (always the newest: https://vsuite.vrsion.com/vshell/vShell-setup.exe) and run it. It installs for your
account only (no administrator rights) into `%LOCALAPPDATA%\Programs\vShell`, and vShell then keeps itself up to date. Settings stay in
`%APPDATA%\vShell`, so an existing setup carries straight over. On a first start the **welcome guide** walks through start-up (run at
sign-in, hide the Windows taskbar), a theme, a layout and the plugins you want.

## The vSuite apps

vShell is the centre of a small suite of apps in the same look:

* **[vXplorer](https://vsuite.vrsion.com/)** - a file manager (tabs, preview pane, Windows' own copy engine).
* **[vView](https://vsuite.vrsion.com/)** - a picture, video and sound viewer.

Both read vShell's theme and follow it live - colours, Wallpaper Theme, and the Blur / Frosted background at vShell's opacity.
*Settings > Updates > vSuite apps* shows whether each one is installed and up to date, with **Open**, **Update**, **Install** (downloads
its newest installer) and its own **Include pre-releases**. vShell's Drives module opens drives in vXplorer when it's installed.

The shared code (log, updater, theme reading, the glass background, questions, Options pages) is **vSuite.Core**
(https://github.com/version-au/vSuite.Core), a git submodule in each app.

## Modules

Every module can go on a bar; all but the taskbar, tray and spacer can also go in a desktop widget (Notes and Power flow only live in
widgets). Each module has a **Text size** - S (one line, small), M (two lines where the module has them) or L (one line, large).

### System and shell

| Module | What it does |
|---|---|
| **Start menu** | A Start button that opens a themed menu of every installed program (Start Menu shortcuts plus Store apps) in A-Z sections, with a search box (type to filter, Up/Down + Enter to launch). A left strip has your user/Documents/Downloads/Pictures folders, Settings and a **Power** button (lock, sleep, sign out, restart, shut down). A lone press of the **Windows key** opens it (Win+E, Win+R etc. still work), even over a full-screen app. Icons: the vShell mark, Four tiles, App grid, Orbit, Hexagon, Stacked bars, Diamonds (all in the theme's colours) or a **custom icon**. S / M / L sets the menu's text size. |
| **Application launcher** | Individual shortcuts and **groups**; a group opens a themed popup menu of its shortcuts (tall ones scroll, long names end in "…"). Drop files onto it to add them where you drop, drag to reorder, or use *Edit shortcuts and groups…*. Groups can **sync with a folder** or with a **browser bookmarks folder** (Chrome, Edge, Brave, Vivaldi, Opera - new bookmarks appear, deleted ones go), and take one of 54 **built-in theme-coloured icons** or a custom icon. **Import pins from the Windows taskbar…**. Optional dock animations (see below). |
| **Taskbar** | Open windows, combined per app (always / when full / never) as icons, icon + name or icon + title; live window previews on hover; click an app with several windows to pick one; pinned apps (drag to reorder, drop programs to pin, import Windows' pins, or **the same pins on every taskbar**); progress, badges and attention pulsing; right-click menu (new window, pin, close all). An **animated active-window line** (Slide, Stretch, Fade, Shrink and grow, Hop, Liquid, with bounce) and buttons that grow in, shrink away and slide into place. Store apps (WhatsApp, new Teams …) get their real icons. Optional "only this monitor's windows". |
| **System tray** | Icons drawn on the bar alongside Explorer (vShell takes over Explorer's tray registration, like Cairo). Icons are grouped behind an arrow; **drag an icon out of the popup onto the bar to pin it**, drag along the bar to reorder. Windows 11's own volume, network, battery and microphone icons answer clicks with vShell menus. Balloon notifications in the bar's theme. |
| **Run box** | A search field on a bar or widget - type straight into it, results drop down underneath - and a key (Alt+Space, Ctrl+Alt+Space …) that opens it in the middle of the main monitor. Apps, paths, web addresses, one-word commands, `>commands`, `=sums`, `!g` / `!yt` / your own **!bangs** (unknown ones go to DuckDuckGo), a custom search engine (e.g. SearXNG), and the plugins: `!s` Seerr, `!qb` / `!sab` downloads, `!ko` Komodo, `!u` UniFi. Paste a magnet / .torrent / .nzb address or drop .torrent / .nzb files to add downloads. **Command palette**: type what you want done - "restart jellyfin", "ac 22", "kitchen light off", "pause the printer", "ssh pve1", "lock the pc" - across Home Assistant, Proxmox, Komodo, printers, saved SSH connections and the PC; risky ones ask first; `/` for commands only. |
| **Terminal** | vTerm's terminal (a vSuite app) dropping down from its bar (Ctrl+Alt+T): tabs for PowerShell, PowerShell 7, Command Prompt, WSL and your own profiles, saved SSH connections and an SFTP file panel, in your theme. See *Terminal* below. |
| **Notifications** | The notification history: on a bar a bell with the number not read yet (coloured and glowing for Important / Alert ones); click for the list by day, with ✓ per notification and a History button. In a widget the list. See *Notifications* below. |
| **Volume** | Master volume of the default speakers with a speaker icon (changes with the level, crossed out when muted), as text and/or a meter. Scroll to change, click for Mute and a list of output devices to switch between, plus Sound settings. |
| **Spacer** | Flexible gap (pushes the modules after it to the right) or a fixed-width gap. |

### System monitors

| Module | What it does |
|---|---|
| **CPU / Memory / GPU monitor** | Text plus **Meter**, **Bars**, **Line**, **Ring** or **Tile** (big number over a sparkline) styles; graph length and height, colour, show/hide text. Memory can show used/total GB; GPU can show VRAM. GPU works on any vendor (Windows performance counters), pick the adapter if you have several. Optional **temperature** (see *HWiNFO and temperatures*). |
| **HWiNFO sensor** | Any value HWiNFO publishes (temperatures, fans, voltages, clocks, power …) as text, meter, bars, line, ring or tile, with its own title, unit, decimals and graph range. |
| **Network speed** | Download and upload speed (MB/s or Mbit/s) with a meter / bars / line / ring / tile graph, optional Wi-Fi name and signal. **Interface**: all adapters with internet (added up, VPNs and virtual switches not double-counted) or one adapter of your choice. |
| **Drives (This PC)** | Every drive with its free space and a used-space bar, or a **Ring** per drive with the % used inside. Click a drive to open it in vXplorer (when it's installed) or File Explorer. |
| **Disk space / activity** | Used space of a drive (% or used/total GB) or live disk busy % with read/write speed, as a meter, ring or tile. |
| **Battery** | Charge %, a + while charging, time left in the tooltip. Hides itself on a PC without a battery (switchable). |
| **Top processes** | The busiest programs by CPU or memory (processes of one program added together), 1-5 side by side or as a list, optional fixed width; the tooltip lists more, click opens Task Manager. |
| **Uptime** | How long Windows has been running. |

Every graph has a **warning colour**: past a threshold (automatic at 70 % of its range, or your own) it blends smoothly into the warning
colour - the Secondary Accent unless you pick another - or **colour by load** (blends through three colours you pick). Graph looks: line
thickness, fill under the line (none, light, solid, fade), smooth curves, optional grid lines (dashed at the warning level), the box
behind the graph on or off, and the time shown (1, 5 or 15 minutes). Top processes can also sort by GPU use.

### Info

| Module | What it does |
|---|---|
| **Clock** | Time and/or date, 12-hour by default (or 24-hour), optional seconds, short / medium / long / ISO dates and several layouts. Click it for a **month calendar** like Windows' own (week start, week numbers). |
| **Weather** | Icon and temperature (optionally the condition and today's high / low), details on hover, a 7-day forecast on click. From Open-Meteo (free, no account), or a Home Assistant weather entity with the Home Assistant plugin. |
| **Now playing** | Album art, title and artist of whatever Windows' media controls show (Spotify, browsers, Plex …), with previous / play-pause / next, a progress bar you can click or drag to seek, scroll to change track and a bigger card on click. With the **Jellyfin** plugin, the Jellyfin apps get a progress bar and controls through the server. Can hide when nothing is playing. |
| **Notes** (widgets) | Note, Checklist (due dates, drag to reorder), Markdown or Log. **Stored in** vShell (several notes per widget), a **File** (.md / .txt, syncs both ways, Obsidian-friendly), a **Folder** of notes to switch between (with *New note…*), or **vDrop** (with the Home Assistant plugin) - the vDrop Home Assistant add-on's feed (Inbox / Pinned / Archived, links with preview cards, media, notes; two-way, works offline). Click to type on the desktop; Win+Shift+N quick-note key. |
| **Text label** | A piece of text (bold, size, colour) - headings and notes for widgets. |
| **Service status** | A coloured dot per service (green up, yellow slow, red down) checked by web address, TCP port or ping; click a web address to open it. Copy / paste services between modules. |

### Smart home and servers

These use the connections on **Settings > Plugins** (keys and passwords stored encrypted for your Windows account).

| Module | What it does |
|---|---|
| **Home Assistant entities** (Home Assistant plugin) | Any entities you choose, with their values (live over Home Assistant's WebSocket, polling as a fallback) and optional graphs; click a light, switch, scene, script, button, lock, cover or automation to operate it. Or **Bubble Card buttons**: switches, sliders (brightness, fan speed, cover position, volume) and states, in a list or a row. |
| **Climate control** (Home Assistant plugin) | A Home Assistant climate entity drawn like Bubble Card: on / off, − / target / +, mode, fan speed and preset buttons, and a slider per zone (fan entities). On a bar: the mode icon, room temperature and target; scroll to change it. |
| **Power flow** (widgets) (Home Assistant plugin) | Solar, grid, battery and home as circles joined by lines with moving dots showing which way the power flows; import / export, battery % and a ring round Home showing where its power comes from. From Home Assistant power sensors. |
| **Proxmox VE** (plugin - add it in Settings > Plugins) | Nodes (CPU and memory), guests (VMs and containers, with uptime) or storage. Click a guest for Start / Shut down / Reboot / Stop, **Open shell (SSH)** (a container's console, or SSH into a VM - its address from the guest agent) and, if you allow it, **Delete**. Right-click a node for a shell on it or its web interface. |
| **Komodo** (plugin - add it in Settings > Plugins) | Docker stacks, deployments and containers in [Komodo](https://komo.do): status dot, running / total, ⚠ problems, ⬆ image updates. Start / Stop / Restart / Pause / Redeploy / Pull / Destroy, stacks open to their services, run procedures, **Show log** (follow, filter, copy), **Open shell** on the server or in the container. Run box: `!ko name`. |
| **Downloads** (plugin - add it in Settings > Plugins) | qBittorrent and / or SABnzbd: speed as text, line, bars, meter, ring or tile plus the number of downloads; the list with progress, size and time left - click one to pause / resume. Pause / resume everything, alternative speed limits, drop .torrent / .nzb files to add. Run box: `!qb`, `!sab`. |
| **Seerr requests** (plugin - add it in Settings > Plugins) | [Seerr](https://github.com/seerr-team/seerr) (Overseerr / Jellyseerr): a badge for requests waiting for approval; tabs (pick which) for **Requests** (Approve / Decline), **Recently added**, **Trending** and **Upcoming** (Request, pick seasons), with paged posters. Run box: `!s`, `!s recent`, `!s title`. |
| **UniFi Network** (plugin - add it in Settings > Plugins) | Read-only: the gateway's internet speed, devices (CPU, memory, uptime, clients, ports coloured by link speed, ⚡ PoE) and clients (search, NEW for ones not seen before). Run box: `!u`. |
| **UniFi Protect** (plugin - add it in Settings > Plugins) | Cameras online and the last event on a bar; a snapshot grid, lights, events with thumbnails; click a camera for a big view, Open in Protect, Play in VLC (RTSPS) and PTZ presets. Notifications for doorbell rings and detections, with a snapshot. |
| **Mail** (plugin) | IMAP accounts (Gmail - with Sign in with Google or an app password - Fastmail, your own server): the unread count on a bar, every unread message in a popup or a widget, Mark as read and Delete on each, and a notification listing new mail. |
| **Radarr / Sonarr** (plugins) | Movies and shows: the queue, what's coming out / airing, what was added and what's missing, notifications, and `!ra` / `!so` in the run box to search and add. |
| **Tdarr** (plugin) | What Tdarr is transcoding and health checking: running jobs, nodes (pause, worker limits), library totals, notifications, `!td`. |
| **Nginx Proxy Manager** (plugin) | Proxy hosts on a bar; notifications for certificates running out, config errors, backends down and new versions; `!npm`. |
| **Uptime Kuma** (plugin) | Your monitors: all up or how many are down on a bar, the list in a widget, notifications when one goes down and comes back. |
| **PeaNUT** (plugin) | UPSes from PeaNUT (Network UPS Tools' dashboard), from as many servers as you like: a tile per UPS with a status dot (on mains, on battery, battery low), notifications and palette commands. |
| **Linux updates** (plugin) | Linux Update Dashboard: how many updates each server has, check / upgrade / reboot from vShell with live output, notifications and palette commands. |
| **Claude usage** (plugin) | Your Claude plan's usage limits (the 5-hour session, the week, per-model weeks) on a bar or widget, with a warning before you hit one. |
| **Shelf** (plugin - no connection needed) | Park files, folders, text and pictures on coloured shelves while you work (files stay where they are), drag them out to copy or move them, a Pocket that empties as you drag out, "Add to shelf" in Explorer's right-click menu, and Windows' clipboard history (Win+V) as a tab. On a bar (drop on it, click for the popup) or as a widget. |
| **Fans** (plugin - uses HWiNFO or LibreHardwareMonitor) | Every fan and pump in rpm, found for you: tiles (as the Tile style), rows with graphs, or on a bar (tiles, side by side, one fan, or just the fan), each with a little fan turning at its speed. A notification when a fan stops or runs slower than you set. |
| **3D printer** (plugin - add it in Settings > Plugins) | Klipper + Moonraker (Fluidd / Mainsail), one or more printers. On a bar: a status dot, progress, time left / finish time and temperatures. The panel: the print with its thumbnail, layer, times and filament; Pause / Resume / Cancel / Emergency stop; a **live webcam** (MJPEG, or snapshots); heaters with graphs, preheat presets and Cool down; **Spoolman** spools; power devices and lights; tabs for Files, History, Queue, Macros and a G-code Console. Can hide while the printer is off. |

## Using the bars

* **Move a bar:** drag it by any empty area onto the top or bottom of any monitor. With *Full width* off it sits exactly where you drop it.
* **Reorder modules:** **Ctrl+drag** a module within its bar, or drag it in *Settings > the bar > Modules*.
* **Three zones** (full-width bars): each module sits in the Left, Centre or Right zone; the centre zone stays in the middle of the bar.
* **Layouts:** Classic (full width, outline), Minimal (full width, no outline), Floating (as wide as its modules, away from the edge) or
  Dock (as wide as its modules, against the edge, optionally centred).
* **A full bar fits itself:** when its modules don't fit, graphs get shorter first, then L text shows as M, then everything as S - your
  settings don't change.
* **Reserve window space** (on by default): maximised windows stop at the bar. Off: the bar sits on the desktop behind your windows.
* **Separator style:** Dots, Line, Chips (every module in a chip) or None.
* Right-click an empty part of a bar: Settings, add/remove bar, Task Manager, Run, File Explorer, Exit.
* Popups from a bar or widget (start menu, launcher, tray, calendar, module popups) take its background blur and the widget style.
* Bars and widgets on a monitor that's turned off or unplugged hide until it's back (monitors are remembered by model and port, not
  just DISPLAYn); Settings lists them under "Monitor not connected".
* Floating bars can be **Full width** (with Three zones) or as wide as their modules.
* Apps that draw their own icon live (vXplorer, in your theme's colours) show it on the taskbar's combined and pinned buttons too.

Settings live in `%APPDATA%\vShell\settings.json`. Log: `%APPDATA%\vShell\vshell.log`.

## Widgets

*Settings -> Widgets.* A widget is a box on the desktop that holds one or more modules - add as many as you like, one per module or
several grouped in one box.

* **Move:** drag a widget by any empty part; it snaps to screen edges, bars and other widgets (Alt places freely) and stays where you drop
  it, per monitor. **Ctrl+click** several widgets and drag one to move them together. Right-click for *Widget settings* and *Lock position*.
* **Snap links:** what a widget snapped to is remembered. When a widget changes size, the widgets snapped to it move to keep the gap.
* **Layout:** Stack, Row or Grid; each module in its own card; or tabs. Optional header row (collapsible), an accent edge and accent
  colour, and Comfortable / Compact density. Text size is S / M / L per module, like on bars.
* **Look:** corner radius, background opacity, outline, shadow, **glass edge** (Off / Subtle / Strong) and background blur.
* **Layer:** by default a widget sits on the desktop behind your windows; tick *Keep above other windows* to float it.
* **Slide away at the screen edge:** a widget snapped to an edge can slide off it when the mouse leaves and come back when you push the
  mouse against that edge (Slide / Fade / Slide and fade, speed, bounce, delay, an accent glow showing where it waits). Off by default.
* **Hides with its modules:** a module that hides itself (Now playing with nothing playing, a printer that's off) takes its row with it,
  and a widget whose modules are all hidden hides itself until one comes back.

## Terminal

The terminal is **vTerm**, a vSuite app of its own (install it from Settings > Updates > vSuite apps; the first time it starts it brings
over the profiles and saved connections vShell's terminal had). Add a **Terminal** module to a bar and vTerm's terminal drops down from
the bar, attached to it, sliding out and back.

* Keys: Ctrl+Alt+T (or Ctrl+` / Alt+` / Ctrl+Alt+`) shows / hides it from anywhere; Ctrl+Shift+T new tab, Ctrl+Shift+W close,
  Ctrl+Tab switch, Shift+Esc hide. Ctrl+C copies selected text, Ctrl+V or right-click pastes.
* Hidden, its shells keep running (vTerm stays in the background). A tab can move into a vTerm window of its own and back.
* Saved SSH connections, the SFTP file panel, profiles, text and colours are vTerm's - its Options.
* Proxmox, Komodo and Linux Updates' *Open shell* items, and the palette's "ssh …", open in the drop-down.

## Notifications

*Settings > Notifications.* Cards in any corner of any monitor, three kinds - **Normal** (green, goes after its time), **Important**
(yellow) and **Alert** (red), which stay until closed - and you pick the kind for each sort of notification. Repeats collapse into one
entry with a count. Quiet times: full-screen apps, quiet hours and Windows' Do not disturb.

Sources: downloads, Komodo, Seerr, Proxmox, service status, UniFi Network and Protect, 3D printers, system (temperatures, storage, memory,
battery), Jellyfin playback, rain on the way, **Home Assistant rules** (an entity becomes a state, crosses a number …), notifications
**sent to vShell** (`http://this-pc:8765/notify` with a token - JSON, form or plain text; Gotify / ntfy priorities understood) and
**vDrop** (Gotify-style notifications through the vDrop add-on, with read state synced both ways).

The **history** keeps 500 by default (up to 5000): search, All / Unread, filter by level and source, Mark read and Clear.

## Plugins

*Settings > Plugins* holds the connections, each with a Test button: **Home Assistant** (address + long-lived token; also used by
vDrop), **Proxmox VE** (node addresses + API token), **Komodo** (API key + secret), **qBittorrent**, **SABnzbd**, **Seerr**, **Jellyfin**,
**UniFi** (console address, Network and / or Protect API keys) and **3D printers** (Moonraker address, optional API key, webcam, preheat
presets). The Notifications page folds each source away until its integration is set up.

## Appearance

* **Themes:** a theme is colours + a bar style + a widget style, applied in one click. 35 built-in themes (one for every colour preset)
  plus your own; export / import as `*.vstheme.json`.
* **Colours:** Background (solid or gradient), Main Accent, Secondary Accent and Text, from presets (Mocha, Cyberpunk, Synthwave '84,
  Dracula, Tokyo Night, Gruvbox Dark, Rose Pine, Everforest, Kanagawa …) or your own. Module colours can follow the theme slots
  (`@main`, `@secondary`, `@text`).
* **Wallpaper Theme:** builds the whole theme from the main monitor's wallpaper, and follows it when it changes.
* **Bar styles** (Soft, Sharp, Round, Slim, Bold, Hairline, Neon, Ghost, Retro, the taller Tall, Panel and Pill, or your own) have a look for each layout; **widget styles**
  (Soft card, Outlined, Borderless glass, Sharp, Bubble, Neon, Retro or your own) set corners, outline, shadow and glass edge.
  Each bar / widget can follow its style or keep its own look.
* **Background blur:** Blur or Frosted (like acrylic) - your wallpaper, blurred, under the theme colour at the theme's opacity - or off
  (the wallpaper as it is, through the theme colour). Bars, widgets, popups, notification cards, Settings and the vSuite apps all draw it
  the same way; a plain-colour desktop shows its colour.
* **Match Windows to vShell's theme** (on by default): Windows' light / dark mode and accent colour follow vShell's theme; your own
  Windows colours come back when it's turned off.
* **Time format:** 12-hour by default, or 24-hour.
* **Dock animations:** an icon-only launcher or taskbar can Lift, Glow, Magnify (macOS-style) and Bounce when an app starts.
* The vShell mark is three dots arranged as a v - Main Accent, Secondary Accent and Text - used for the Start button, the exe icon and the
  splash screen.

## Desktop and wallpapers

*Settings > Desktop > Wallpaper* shows your monitors as they're arranged. Set a picture per monitor, the same on all, or one spanned across
all (Windows' own per-monitor wallpaper, made at each monitor's exact size). Fit / Center backgrounds: a blur of the picture, black, the
theme colour, ready-made gradients or your own. With no picture, a colour or gradient fills the monitor. **Slideshows** per monitor from a
folder. *Restore my original wallpapers* puts back exactly what Windows had.

## HWiNFO and temperatures

Windows has no reliable built-in CPU or memory temperature, so those come from **HWiNFO** (the same route Rainmeter uses):

1. In HWiNFO open the **Sensors** window, then its settings (gear) -> **HWiNFO Gadget** tab -> tick **Enable reporting to Gadget**.
   (Do not need Shared Memory.) Needs HWiNFO 7.02 or newer; it must be running.
2. Right-click each sensor reading you want in the Sensors window -> **Report value in Gadget**.
3. In vShell: add an **HWiNFO sensor** module and pick the reading in the list (Refresh re-reads it), or tick
   **Show temperature** on a CPU / Memory / GPU module. Leave the source on *(automatic)* or choose a specific reading.

Automatic temperature: GPU on NVIDIA cards is read straight from the driver (no HWiNFO needed); everything else looks for the
usual HWiNFO readings (CPU Package / Tctl-Tdie, GPU Temperature, DIMM) and shows `n/a` if none is being reported.

## Startup apps

*Settings -> Startup* lists everything Windows starts at sign-in (registry Run keys and the Startup folders). Select entries to
**Enable**, **Disable** or **Remove** them, or **Add** a program. Enable/Disable use the same per-user switch as Task Manager's
Startup tab. Removing "all users" entries needs administrator rights; disabling them does not.

## Runs alongside Explorer

vShell registers its bars as app bars next to Explorer and hides the Windows taskbar. The tray module still works: like Cairo Desktop,
vShell puts its own invisible `Shell_TrayWnd` window in front of Explorer's, so apps send their notification icons (and ITaskbarList
progress and badges) to vShell; everything else is forwarded to Explorer. Panic button: `vShell.exe --restore-taskbar` brings the taskbar
back. If an older version was set as your shell, vShell undoes that at start-up; `vShell.exe --restore-shell` /
`scripts\restore-explorer.ps1` still do it by hand.

## Build

Work happens on the `main` branch (`branch-3`, `build-2` and `build-1` are older lines, kept for history). After cloning, fetch the
shared library: `git submodule update --init`.

Requires the .NET 10 SDK on Windows (and Inno Setup 6 for the installer): double-click **`build.cmd`**. It makes the self-contained,
single-file `dist\vShell.exe`, the plugins that changed, `plugins.json`, `dist\setup\vShell-setup.exe`
(`build.cmd nosetup` skips it) and `dist\release-notes.md` from CHANGELOG.md; `dist\site\` gets exactly the files for the update site.
It also checks the plugin API against `docs\plugin-api.txt` and stops if something plugins use changed.
Quick compile check: `dotnet build vShell.csproj -c Release`. The first build downloads the NuGet packages (needs internet once).

## Updates

vShell checks https://vsuite.vrsion.com for new versions (a minute after start-up, then daily; *Settings > Updates*, optionally
including pre-releases; the tray menu's *Check for updates* opens it). Only the newest release is on the site: `vshell/latest.json`
(version, notes, each file's size and SHA-256), `vShell.exe`, `vShell-setup.exe`, `changelog.md` (the update notice lists every
version's changes since yours) and `vshell/plugins/<plugin API>/` (`plugins.json` and every plugin DLL); a pre-release goes in
`vshell/pre/`. Publishing a version: run `build.cmd` (`build.cmd pre` for a pre-release) and copy everything in `dist\site\` over
the site's root. Plugins only: `plugins.cmd`, then the same. The updater checks the SHA-256, swaps the exe in
place, restarts, and puts the previous version back if the new one doesn't start (that version isn't offered again).

## Known gaps

* Wi-Fi details use `netsh`, so they depend on Windows' English output.
* The Volume module's output-device switch uses an undocumented-but-stable Windows interface.
* The Start menu has no pinned tiles, recent files or jump lists, and does not show Windows search results (files, web).
* GPU memory shows VRAM *in use* only (no total).
* Firefox bookmarks can't be synced into a launcher group (they're kept in a database vShell can't read).
* UniFi Protect's API has no event history, so events are only the ones vShell saw while running.

## Licence

Copyright (C) 2026 Steven (version-au). vShell is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License, version 3 - see [LICENSE](LICENSE). It comes with no warranty. The shared library vSuite.Core is MIT, and
the parts by others are listed in [THIRD-PARTY.txt](THIRD-PARTY.txt).

Source: https://github.com/version-au/vShell · Downloads and updates: https://vsuite.vrsion.com
