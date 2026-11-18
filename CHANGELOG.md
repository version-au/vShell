# vShell changelog

Each version's section is copied into `dist\release-notes.md` by `build.cmd` - paste that into the GitHub release.
Newest first. The heading must start with `## ` and the version number exactly as in `vShell.csproj`.

## 0.38.230 · pre-release

- Dots: Temperature in dots is only offered where there's a temperature - CPU, memory, GPU and HWiNFO sensors (a module that had it set shows Stacked).
- Dots, Taking turns: every turn looks like the number - the value in dots and its unit in the font (network's upload was plain text).
- Details in a bar only shows in a bar's module settings (a widget's Dots tile has its details at the top right).

## 0.38.229 · pre-release

- Dots: Details in a bar - where the clock speed, GB, temperature and so on go: Stacked (two small lines after the number, the new default), Under the label, Temperature in dots, Taking turns or Under the graph. Shorter modules than one long line.

## 0.38.228 · pre-release

- Dots in a bar shows the module's details after the number, as the other styles do: the CPU's clock speed, memory's used / total GB and the GPU's VRAM (More detail), the temperature (in the Secondary Accent colour), network's upload and Wi-Fi name, disk's GB, battery's charging / time left.
- Dots: memory's used / total GB only shows with More detail on, as in the other styles.
- Dots with Colour by load: the newest column and disk space's fill take the load colours too.
- Dots in a widget: Graph height sets how many rows of dots the tile has (100% = 12 rows) - it did nothing before.
- Plugin API: DotFace.Temp (a temperature after the detail).

## 0.38.227 · pre-release

- Memory, GPU, disk, network, battery, HWiNFO sensor and Downloads modules have the Dots style too: network mirrors download and upload round a middle line, disk space fills up from the left.
- The CPU module's Dots tile in a widget no longer starts very wide: Graph length sets its width.
- Plugins can draw in dots too (plugin API: MetricView's Dots style, DotFace, DotText).
- Settings > About > Plugin SDK downloads the newest pre-release's SDK when Include pre-releases is on and it's newer than the release.
- Dots in the plugins: Fans 1.1.0 (a graph style), Home Assistant (an entity's graph), Proxmox (CPU, memory and storage), Claude usage 2.1.0 and PeaNUT 2.1.0 (their meters).

## 0.38.226 · pre-release

- The CPU module has a new style, Dots: its graph as columns of dots with the newest in the graph colour, and the number in dots (Numbers in dots - off = your font). In a widget it's a tile.

## 0.38.225 · release

- The glow on edges that line up while you drag or resize a widget stops where its rounded corners start, instead of running on over them.

## 0.38.224 · pre-release

- A widget hidden behind your windows (or while the PC is locked) stops redrawing its graphs and readings until it shows again - its graphs keep every sample.
- Dragging a locked widget shows that it's locked (an outline and "Locked · right-click to unlock") instead of just not moving.

## 0.38.223 · pre-release

- vTrace plugin 2.1.2: notifications aren't skipped when many pile up while the PC sleeps or the server can't be reached, and they carry on after the server's Activity starts again.
- vDeploy plugin 2.0.1 and vPatch plugin 1.0.1: the same - no notification is skipped when many are waiting or a request fails.
- A settings.json that can't be opened for a moment is tried again; one that can't be read or copied aside is never saved over (vShell says so and saves nothing until it starts again).
- Shelf plugin 1.1.4: a shelves.json that can't be read is kept (shelves.broken-<time>.json) and never saved over with empty shelves.
- vTrace plugin: asks the server whether anything changed and only then reads the week of changes again (needs vTrace 0.1.8; an older server is read as before).
- The Start menu no longer builds its list again every time the Store apps are looked up and nothing changed; they are looked up every half hour, not every 10 minutes.
- Fans plugin 1.0.11: reads its lists from the settings only when they change, not every second.
- Glass (Blur / Frosted): a wallpaper on a network share that has gone away no longer holds the bars and windows for seconds at a time (vSuite.Core 1.0.41).
- vPatch plugin 1.1.0: Upgrade in the popup and widget (and a host's right-click menu) runs vPatch's dry run first, then upgrades only the hosts that are clean (needs vPatch 0.1.21).

## 0.38.222 · pre-release

- vDeploy plugin 2.0.0: vDeploy's Proxmox nodes and their helper-script LXCs / VMs in the popup and widget (a setting hides them), with app updates, start, shut down, reboot and a console in vTerm.
- vDeploy plugin: LXC app updates count in the bar's ⬆, and are in !deploy and the palette.

## 0.38.221 · release

- vTrace plugin 2.1.1: vPatch's changes are named vPatch (vTrace 0.1.4 reads vPatch).

## 0.38.220 · release

- vTrace plugin 2.1.0: only the vTrace server - the Windows vTrace app is retired.
- Settings > Updates > vSuite apps no longer lists vTrace (it's a server app now).
- UniFi plugin: the account for the Windows vTrace's history is gone.

## 0.38.219 · release

- vTrace plugin 2.0.0: shows your vTrace server (its address and an app key in Settings > Plugins > vTrace) - its changes, outages and notifications, the refresh button asks it to look, a click opens the change in your browser - and sends this PC's own changes to it. Without a server it reads the vTrace app as before.

## 0.38.218 · release

- vTrace plugin 1.5.0: the widget and popup can show only issues (outages and failed changes) - All / Issues at the top, or a setting.
- vTrace plugin 1.5.1: on All, outages still going are red rows in the list; their causes and the AI's notes show on Issues.
- vDeploy plugin 1.2.0: a container with a newer image shows ⬆ next to it.
- vDeploy plugin 1.3.0: the widget and popup use vDeploy's own host, stack and container icons, laid out like its side panel.
- vPatch plugin 1.0.0: your vPatch server's Linux hosts - updates waiting, security and restarts on a bar or widget, Check now, vPatch's notifications, and your colours on its pages.

## 0.38.217 · release

- Widgets that are slightly too small step down to fit without rebuilding at sizes that change nothing.
- Home Assistant: no more "unobserved task" errors in the log when the vDrop connection drops.
- 3D printers: a proxy's error page (502 Bad Gateway) shows as a short message, not the whole page in the log.

## 0.38.216 · release

- Bars stay at the screen edge when Windows' hidden taskbar still holds its space after the monitors wake up (a bottom bar sat a taskbar's height up).

## 0.38.215 · release

- Taskbar previews: a preview's right-click menu shows in front of the previews (it could drop behind them).

## 0.38.214 · release

- Bars go back to their place when something else moves them - e.g. after a monitor is turned off and on again they could stay part-way up the screen.

## 0.38.213 · release

- Taskbar previews: right-click a preview for the window's own menu (Restore, Move, Size, Minimize, Maximize, Close), as on Windows' taskbar.

## 0.38.212 (Build 4 - experimental) · release
- vDeploy plugin: show a stack's containers (the arrow), with their state, CPU and memory.
- vDeploy plugin: right-click a container to start, restart or stop it, open a shell in it or on its host in vTerm, or show its log.
- vDeploy plugin: a log window for a stack (every service) or a container - follow, filter, times, copy (needs vDeploy 0.1.9).
- vDeploy plugin: vDeploy's automations in the widget and the popup, each with a Run button (a switch in the module's settings).
- vDeploy plugin: its icons show (they were missing).

## 0.38.211 (Build 4 - experimental) · release
- vTrace plugin: outages that went down together show as one ("with 5 more on pve"), with what vTrace thinks caused them.
- vTrace plugin: click an outage, its cause (View) or a change to open vTrace on it.
- vTrace plugin: shows vTrace's local AI explanation of an outage.
- New plugin, Local AI: your own AI server (Lemonade, Ollama, LM Studio …) on a bar or widget, load / unload models, ask from the run box (!ai), and a heads-up when it goes offline.
- Local AI plugin: !ai questions work in the run box (they ran as a program).
- New plugin, vDeploy: your vDeploy stacks on a bar or widget (running, problems, newer images), update / restart / stop them, !deploy in the run box, vDeploy's notifications, and your vShell colours on vDeploy's pages.

## 0.38.210 (Build 4 - experimental)
- Sonarr and Radarr widgets fit a set width (Automatic width off) - the button and counts no longer spill off the right.

## 0.38.209 (Build 4 - experimental)
- Sonarr and Radarr: show health warnings and errors can be switched off (module settings).

## 0.38.208 (Build 4 - experimental) · release
- vTrace plugin: a refresh button (widget, bar module and its popup) while vTrace isn't open, and when it last checked.
- vTrace plugin: Settings > Plugins > vTrace - check for changes every 5, 10, 30 or 60 minutes while vTrace isn't open.

## 0.38.207 (Build 4 - experimental) · pre-release
- vShell's tray icon menu follows the theme and closes when you click elsewhere.

## 0.38.206 (Build 4 - experimental) · pre-release
- UniFi plugin: an optional View Only account, for vTrace to read UniFi's history.

## 0.38.205 (Build 4 - experimental) · pre-release
- Settings > Updates > vSuite apps lists vTrace (what changed, and when).
- A vTrace plugin: a bar module (an outage, or changes today) and a widget with vTrace's latest changes.

## 0.38.204 (Build 4 - experimental) · release
- Popups stay open when Windows briefly says they lost the focus while they're still in front (the cause of the Cascading menu closing).

## 0.38.203 (Build 4 - experimental) · pre-release
- Start menu (Cascading): right-clicking a program in Programs no longer closes the menu, so you can pin it.

## 0.38.202 (Build 4 - experimental) · pre-release
- Start menu (Cascading): clicking or right-clicking in a flyout no longer closes the whole menu - pinning from Programs works.

## 0.38.201 (Build 4 - experimental) · pre-release
- Start menu (Cascading): right-click a program or a pin and pick Pin to Start / Unpin from Start - it works now.

## 0.38.200 (Build 4 - experimental) · pre-release
- Start menu: the Cascading style is vShell's own menu now - in your theme and widget style, each folder opening in a flyout beside it.
- Start menu: in the Cascading style, right-click a program for Pin to Start / Unpin from Start, as in the other styles.

## 0.38.199 (Build 4 - experimental) · pre-release
- Start menu: three new styles to choose from - Grid, Two panes and Cascading (Start menu settings > Style).
- Start menu: right-click a program to pin it, or a pinned one to unpin it.
- UniFi: its live connection no longer fills vShell's log with an error each time it reconnects.
- Behind the scenes: saved secrets and vTerm's connections are read by the shared library (the same code as vTerm and vPad).

## 0.38.198 (Build 4 - experimental)
- Installing a vSuite app from Settings downloads in the background, so its progress keeps moving (vSuite.Core 1.0.24).
- Settings' scrollbars are the new ones too (easy to grab, wider when pointed at), and its pages keep clear of the window's resize edge.

## 0.38.197 (Build 4 - experimental)
- Settings and its questions: scrollbars easier to grab - never a tiny thumb in a long list, wider when you point at them (vSuite.Core 1.0.23).

## 0.38.196 (Build 4 - experimental) · release
- Settings > Updates > vSuite apps lists vPad, the new text editor: install and update it from there.

## 0.38.195 (Build 4 - experimental)
- Fixed: double-clicking a note that grows with its text, in a widget with a fixed height, lost the keyboard straight away.

## 0.38.194 (Build 4 - experimental)
- The terminal is now vTerm, a vSuite app of its own: the Terminal module drops vTerm down from its bar.
- vTerm brings over your terminal profiles and saved SSH connections the first time it starts.
- Open shell (Proxmox, Komodo, Linux Updates) and the palette's ssh commands open in vTerm's drop-down.
- No terminal in widgets any more (a widget's Terminal module is removed).
- vTerm is in Settings > Updates > vSuite apps; without it, the Terminal module offers to install it.

## 0.38.193 (Build 4 - experimental)
- Adding a plugin no longer takes an old copy lying next to vShell.exe when the update site has a newer one (Fans came as 1.0.3 and wanted updating straight away).

## 0.38.192 (Build 4 - experimental)
- Settings > About > Plugin SDK downloads the SDK from vsuite.vrsion.com instead of GitHub.

## 0.38.191 (Build 4 - experimental)
- The first release from vsuite.vrsion.com only - a test that updating from the site works.

## 0.38.190 (Build 4 - experimental)
- Updates (vShell, its plugins, and vXplorer / vView in Settings > Updates) now come from vsuite.vrsion.com instead of GitHub - no more "GitHub is limiting requests".
- The update notice lists the changes of every version since yours, not just the newest one.
- Settings > Updates: one "vSuite website" button in place of "Release page" / "All releases" (vSuite.Core 1.0.18).

## 0.38.189 (Build 4 - experimental)
- The installer is now called vShell-setup.exe in every release (no version in the name), so the latest one always has the same download link.
- vShell's Settings > Updates > vSuite apps finds installers by the new name too (vSuite.Core 1.0.17).

## 0.38.188 (Build 4 - experimental)
- Wallpapers: your per-monitor wallpapers are set again at start and when monitors change (they failed with "Specified cast is not valid" since 0.38.171).
- The bars' blur and the Wallpaper Theme always read the picture each monitor really shows.
- Shelf 1.1.3: the edge tabs slide out and back smoothly (no flash or jump), and the tab under the pointer grows out instead of snapping.
- Fans 1.0.10: the turning fans are drawn a new way (10 steps a second), so dragging the Settings window with Blur / Frosted stays smooth.

## 0.38.186 (Build 4 - experimental)
- Fans 1.0.4: each fan can have its Max rpm (its graphs and Colour by load scale to it; empty = automatic); Colour by load is back in the graph settings; "Stops alone" is now "0rpm mode".

## 0.38.185 (Build 4 - experimental)
- Sensors can come from LibreHardwareMonitor instead of HWiNFO (temperatures, fans, voltages … for the Hardware sensor module, CPU / GPU / memory temperatures and the heat warnings).
- New plugin: Fans 1.0.3 - your fans' and pump's speeds as tiles, line / bar / meter graphs or rings with a little turning fan, on a bar or in a widget, and a notification when one stops or runs slow (fans that stop on their own, like a GPU's, can be left out).
- Shelf 1.1.2: the edge tabs react to a wider area; clicking another tab while a shelf is open shows that shelf; dragging files out of vXplorer brings them out (moving its tabs doesn't); the pin is the "stays open" setting, also in Settings; how long a drag rests on a tab before its shelf opens (or never); the Shelf module's settings link to the edge tabs.
- Plugins: a plugin without a connection has a Settings tab (not Connection) on its page.
- Plugins: a plugin can read the hardware sensor values (HWiNFO or LibreHardwareMonitor), and a module can show only the graph settings that fit its style.

## 0.38.179 (Build 4 - experimental)
- Shelf 1.1.0: edge tabs - your shelves as tabs on any edge of any monitor (hanging from a bar when that edge has one) that slide out when you drag, or hover; index tabs or a ribbon; a shelf opens from them as a drawer.
- Plugins: a plugin can see where vShell's bars are (for windows that sit against a bar).

## 0.38.178 (Build 4 - experimental)
- New plugin: Shelf 1.0.3 - park files, folders, text and pictures on shelves and drag them out to copy or move them, a Pocket, "Add to shelf" in Explorer's right-click menu, and Windows' clipboard history as a tab (a bar button with a popup, or a widget).
- Plugins: a plugin can be started from Explorer's right-click menu or a shortcut (`vShell.exe --plugin <id> <what> …`) - the running vShell gets it.
## 0.38.174 (Build 4 - experimental)
- Settings > Updates > vSuite apps: GitHub is asked once per Settings window (when the Updates page shows) instead of every time an app saves its settings; no Update button for a version the app won't install; Include pre-releases no longer flips back after a click.
- Updates: Update in vShell's Settings no longer leaves an install waiting for later if the check fails, never installs a version that was skipped or didn't start, and a release without its checksum isn't installed.
- Questions: Enter presses the button that has the focus (Tab moves it).
- The see-through background asks Windows about the wallpaper less often, and a plain-colour desktop shows that colour (not the old picture).
- Settings > Updates > vSuite apps: an app that isn't installed has an Install button - it downloads the app's newest installer and starts it.
- Tidy-up: the Wallpaper Theme's colours come from the suite's library (the same code as vXplorer and vView), and the old settings colours are gone (no change you should notice).

## 0.38.173 (Build 4 - experimental)
- Settings > Updates > vSuite apps: an Include pre-releases switch under each app - it's the app's own setting, kept in step both ways, and what's offered as available follows it.

## 0.38.172 (Build 4 - experimental)
- Settings has an Updates page (as vXplorer's and vView's Options): vShell's updates and the vSuite apps moved there from System. Check for updates in the tray menu opens it.

## 0.38.171 (Build 4 - experimental)
- Settings > System: a vSuite apps card - vXplorer and vView, the version you have, whether a newer one is out, and Open / Update / Get.
- Bars, widgets and popups draw their blur with the same code as Settings, vXplorer and vView (no change you should notice); a plain-colour desktop shows its colour behind them.

## 0.38.170 (Build 4 - experimental)
- Settings and the welcome window: With Blur and Frosted off, the window shows your wallpaper (not blurred) through the theme colour at your opacity, matching the bars and widgets - it was solid.

## 0.38.169 (Build 4 - experimental)
- Settings: Blur and Frosted are drawn as vShell's bars draw them - your blurred wallpaper under the theme colour - so vShell's opacity setting now shows (Windows' Mica hid it).

## 0.38.168 (Build 4 - experimental)
- Questions and messages (shut down a VM, paste settings, update problems …) now show in vShell's theme, like vXplorer's and vView's.
- Settings has vShell's own title bar and the Blur / Frosted glass, and the same colours and controls as vXplorer's and vView's Options.
- The welcome window has the same colours and controls.
## 0.38.167 (Build 4 - experimental)
- vShell's log and updates now come from vSuite.Core, the library vShell shares with vXplorer and vView (no change you should notice).
- A vShell run from a build folder no longer offers to update itself.

## 0.38.166 (Build 4 - experimental)
- Bars and widgets on a monitor that's turned off or unplugged now hide until it's back, instead of moving to the main monitor.
- Settings > Bars and Widgets: a "Monitor not connected" group with an amber dot for the hidden ones.
- A bar's settings: Monitor and Edge choices (Layout > Position); a widget's Monitor shows "Not connected" instead of Monitor 1.

## 0.38.165 (Build 4 - experimental)
- Drives (This PC) module: a Ring style like the Disk module's - a ring per drive with the % used inside and its name and free space beside it, and a ring size setting.

## 0.38.164 (Build 4 - experimental)
- New module: Drives (This PC) - every drive with its free space and a used-space bar, for widgets and bars. Click a drive to open it in vXplorer (when it's installed) or File Explorer.

## 0.38.163 (Build 4 - experimental)
- Tray: clicking the Windows Security icon opens Windows Security; right-click it for a menu of its pages.

## 0.38.162 (Build 4 - experimental)
- Taskbar: a pinned app that draws its icon live (vXplorer in your theme's colours) keeps showing that icon after you close it, instead of the program's fixed one - also after vShell restarts.

## 0.38.161 (Build 4 - experimental)
- Taskbar: apps that draw their own icon live (vXplorer in your theme's colours) show that icon on combined and pinned buttons too, and it updates straight away.

## 0.38.160 (Build 4 - experimental)
- Widget Launcher animations: the widget gets transparent room around its box for the grown, lifted and bouncing icons (as a bar does), so
  they aren't cut off at its edge.

## 0.38.159 (Build 4 - experimental)
- Widget Launcher Magnify: each icon grows by how far the mouse is from it in any direction (smooth diagonally too), with its row spreading
  sideways and its column spreading up and down to make room - not a whole row at a time.
- Launcher in a widget: the icons are the size set (up to 48) and the widget grows to fit them, instead of the icons shrinking to the module height.

## 0.38.158 (Build 4 - experimental)
- Fix: Magnify on a widget's Launcher scattered the icons - now only the row under the mouse grows and spreads, the others stay put.

## 0.38.157 (Build 4 - experimental)
- Launcher in a widget: the icon animations (Lift, Magnify, Glow, Bounce) as on a bar - with icons only.

## 0.38.156 (Build 4 - experimental)
- Launcher in a widget: Arrange - one row (as before), or a grid of so many columns or rows.

## 0.38.155 (Build 4 - experimental)
- Launcher: Add shortcuts… takes several at once (Ctrl or Shift in the file dialog), and keeps the shortcut itself rather than the program it
  points at (its own name, icon and arguments).
- Taskbar pins: Add programs or shortcuts… takes several at once too.

## 0.38.154 (Build 4 - experimental)
- Plugins page: every plugin says who it's by - vShell's own ones "by version", including a build from before that still says "V" inside.

## 0.38.153 (Build 4 - experimental)
- Plugins: the ones that come with vShell are by "version" (was "V") - Claude usage 2.0.2, PeaNUT 2.0.2 and Uptime Kuma 2.0.1 for that alone.
- Mail: Gmail accounts can Sign in with Google (no app password) - the browser does the sign-in and the address, server and port are
  filled in; your own OAuth client can be used instead.
- Mail module: with one account chosen, "Show the account's name next to the count" ("Work 3").

## 0.38.152 (Build 4 - experimental)
- Mail: Mark as read and Delete act at once - the message goes and the count drops straight away (back, with the reason, if the server
  says no), and the connection stays open for a minute so the next one is quick.

## 0.38.151 (Build 4 - experimental)
- Mail: the list no longer shows at full height for a moment when it rebuilds (after a delete, a mark as read or a check).
- Mail: the scrollbar is visible whenever there's more to scroll.

## 0.38.150 (Build 4 - experimental)
- Mail: the popup and the Mail messages widget list every unread message (the newest 100 per account), showing a set number at once and
  scrolling for the rest.
- Mail: Mark as read and Delete buttons on each message (Delete moves it to the server's Trash folder when it has one).

## 0.38.149 (Build 4 - experimental)
- New plugin: Mail - IMAP accounts (Gmail with an app password, Fastmail, your own server): a Mail module with the unread count and the
  messages in a popup, a Mail messages widget, and a notification listing the new messages.

## 0.38.148 (Build 4 - experimental)
- Welcome guide, Layout step: the widget choice now makes a system widget (big clock with the date, CPU and memory tiles, a disk ring - one
  card each) at the top right of the main screen, instead of a clock on its own.

## 0.38.147 (Build 4 - experimental)
- Settings > System: Backups now sits below Behaviour.

## 0.38.146 (Build 4 - experimental)
- Welcome guide on a first start: four steps - start-up (run at sign-in, hide the Windows taskbar, tray icon), theme, layout and plugins - each
  applied as you choose it.
- Layout step: taskbar only / with the system graphs / with a clock widget; a taskbar on every monitor, with synced pins; import the apps pinned
  to the Windows taskbar.
- Plugins step: tick the plugins you want - they're added when you click Finish, and Settings opens on Plugins to set them up.
- Settings > About > Welcome guide: Show it again.

## 0.38.145 (Build 4 - experimental)
- Less work in the background: an auto-width bar is only placed again when something about it changed (not every second), widgets are
  only put back on the desktop layer when they left it, and the monitors are read at most once a second.
- Fix: a plugin saving or applying settings from its own thread could leave settings unsaved until a restart.
- Fix: a notification check or a plugin's watcher could see no bars and widgets for a moment while settings were being applied.
- Plugins: Claude usage, PeaNUT and Uptime Kuma are built with vShell now (2.0.1: Claude usage and PeaNUT fixes, see their changelogs).

## 0.38.144 (Build 4 - experimental)
- Floating bars: a Full width option - full width (less the edge gap) with Three zones, or as wide as its modules.
- Switching a bar to Floating keeps its width: a Classic or Minimal bar becomes a full-width Floating bar.

## 0.38.143 (Build 4 - experimental)
- Themes: 16 new themes, one for each colour preset that didn't have one.
- Bar styles: Tall (52), Panel (56) and Pill (58) - taller bars, used by six of the new themes.

## 0.38.142 (Build 4 - experimental)
- Widgets: a Tile is as tall as the widget's module height, its graph filling down to the bottom.

## 0.38.141 (Build 4 - experimental)
- CPU, Memory and GPU: Style is now at the top of the Graph section in their settings.
- Network and Downloads: no Colour by load (they keep their download and upload colours).

## 0.38.140 (Build 4 - experimental)
- Downloads list: with Automatic scale, the graph's scale covers the whole time shown.
- Plugin API: `ModuleConfig.GraphSpan` (the graph's time shown).

## 0.38.139 (Build 4 - experimental)
- Graphs: line thickness, fill under the line (none, light, solid, fade) and smooth curves.
- Graphs: the box behind line and bar graphs can be turned off, with its own corner rounding.
- Graphs: optional grid lines, with a dashed line at the warning level.
- Graphs: time shown - 1, 5 or 15 minutes.
- Graphs: colour by load - blends through three colours you pick (default: Main Accent faint, Main Accent, Secondary Accent).
- Home Assistant and Downloads: their graphs have the same look settings.
- Plugin API: `MetricView.ApplyLook` and `SettingsPage.GraphLook`, for plugins' own graphs.

## 0.38.138 (Build 4 - experimental)
- Top processes: the GPU note in its settings only shows when sorting by GPU.
- Top processes: GPU use matches Task Manager on PCs with two GPUs.
- Top processes: sorting by GPU shows "Measuring…" until the numbers are in, and "GPU n/a" where they can't be read.

## 0.38.137 (Build 4 - experimental)
- Top processes: can sort by GPU use.

## 0.38.136 (Build 4 - experimental)
- Widgets: lining up while dragging or resizing makes the matching edges glow, instead of a thin guide line.
- Widgets: dragging no longer snaps to the centres of other widgets or the screen, and skips widgets that have slid away.

## 0.38.135 (Build 4 - experimental)
- vShell now runs on .NET 10 (was .NET 8, which stops getting security fixes in November 2026).
- Plugin API 2: plugins are rebuilt for .NET 10 - ones built for older vShells (API 1) need a new build (see the plugin SDK's plugins.md).

## 0.38.134 (Build 3 - experimental)
- Taskbar: pinned apps and icon-only buttons have bigger icons when "Set the icon size myself" is off.
- Linux Update Dashboard: the popup updates while it's open after Check now, Check all or an upgrade - no need to close and reopen it.

## 0.38.133 (Build 3 - experimental)
- Questions and windows (confirmations, messages, settings, log and output windows) open on the monitor you asked from, not always the main one.

## 0.38.132 (Build 3 - experimental)
- Linux Update Dashboard plugin: updates per server on a bar, check / upgrade / reboot, live output, notifications and !lu in the run box.
- Nginx Proxy Manager: now a plugin in the release - add it in Settings > Plugins.
- Proxmox: small CPU and MEM labels over the guest list's columns (popup and widget).
- Nginx Proxy Manager: the host list no longer redraws (and jumps to the top) every 30 seconds - only when something in it changed.
- Nginx Proxy Manager: a host whose backend can't be checked from this PC shows a green ring, not the same dim dot as a disabled host.
- Plugins: settings that do nothing with the current choices are hidden (notification kinds, delays, webcam, graph and more).
- Plugins: settings laid out the same way in every plugin (notification order, Test saves the key first, wording).
- Proxmox, UniFi Network, UniFi Protect and Home Assistant Climate: "Text in front" on a bar.

## 0.38.131 (Build 3 - experimental)
- Jellyfin plugin: no longer claims the Proxmox module (Proxmox modules could say they come from Jellyfin).
- Radarr, Sonarr, Seerr, Tdarr and UniFi: palette commands (search for missing, approve / decline requests, pause nodes, Protect lights, open pages).
- Proxmox (!px), Home Assistant (!ha) and 3D printers (!3d): run box keywords for their palette commands.
- Jellyfin: a module - who's watching (progress, transcoding), recently added and the library's totals.
- Jellyfin: a "new in the library" notification, palette commands to pause / resume / stop what's playing, and !jf in the run box.
- Tdarr: the failed files in the module, with Requeue (one, or all - also in the palette).
- Tdarr: a daily summary notification (files done, space saved, failures).
- Home Assistant: entity graphs start with the last two minutes from Home Assistant's history instead of empty.
- Downloads: right-click a torrent for force start, recheck, reannounce, top of the queue and remove (with or without its files).
- Downloads: the Tile style shows the download and upload speeds.
- Plugin API: History.Count (how many samples a graph has).

## 0.38.130 (Build 3 - experimental)
- Network and Downloads: the speed graph can scale itself to the fastest speed it shows (Automatic scale).
- Plugin API: auto-scaled graphs for plugin modules (InfoModule and MetricView).

## 0.38.129 (Build 3 - experimental)
- Appearance: saved bar and widget styles can be edited, renamed and duplicated (Edit / Rename / Duplicate next to Delete).

## 0.38.128 (Build 3 - experimental)
- Blur background: uses less memory when it reads your wallpaper.

## 0.38.127 (Build 3 - experimental)
- Taskbar: an icon size setting for pinned apps and icon-only buttons.
- Popups that open but draw nothing try one fix at a time until they show, and log which one worked.

## 0.38.126 (Build 3 - experimental)
- Network module: the Tile style's graph shows upload too.

## 0.38.125 (Build 3 - experimental)
- Uptime module: count from the last restart, awake time, the last wake-up or sign-in.
- Uptime module: Short / Long / Decimal formats, and an option to show minutes.
- Uptime module: two lines on a bar, or a big-number tile in a widget.
- Uptime module: a dot when Windows needs a restart.
- Uptime module: click for the details (restart, sleep, sign-in, Fast Startup) with Sleep and Restart buttons.

## 0.38.124 (Build 3 - experimental)
- A widget snapped to other widgets can still slide away at a screen edge or bar it's against.

## 0.38.123 (Build 3 - experimental)
- A widget snapped against a bar (under a top bar, over a bottom bar) can slide away towards it.

## 0.38.122 (Build 3 - experimental)
- A popup that opens but can't be seen shows itself again (and says why in the log).

## 0.38.121 (Build 3 - experimental)
- CPU module: the load now matches Task Manager (option to switch back).
- CPU module: optional smoothing, clock speed ("4.5 GHz"), and a new Cores style with a bar per thread.

## 0.38.120 (Build 3 - experimental)
- Clicking a module again closes its popup (it used to open it again) - Volume mixer, Clock calendar, Weather, Notifications, Now playing, Tray.
- Volume mixer in a widget: pick several sound devices to show, each with its own volume.
- Volume mixer: options to hide the apps and the Sound settings links.

## 0.38.119 (Build 3 - experimental)
- Volume mixer: the top volume row is named after its device ("Media", "System") instead of "All sound".
- Volume module: click or drag on its meter to set the volume there.
- Volume mixer in a widget: new Columns layout with upright sliders side by side.

## 0.38.118 (Build 3 - experimental)
- Volume module: pick its sound device - add several to show different devices' volumes side by side.
- Volume mixer: right-click an app to choose which device it plays on.
- Volume module settings: Show text, meter width and colour only show for the Meter style.

## 0.38.117 (Build 3 - experimental)
- Left-clicking the tray's sound icon opens the volume mixer.
- Volume mixer: the level meters are now easy to see (a line under each slider).
- Volume module: new options - output device name, a live sound level, how muted looks, middle-click mute, what a click does.
- Volume mixer options: only apps playing, level meters, device picker - and the mixer can sit in a widget.

## 0.38.116 (Build 3 - experimental)
- Taskbar: clicking an app with a dialog open brings the dialog to the front.
- Taskbar: minimise and close work like the window's own buttons (and "Save changes?" shows in front).
- The hidden Windows taskbar no longer flashes up for a moment.
- New volume mixer: a slider and level meter for each app, on every output device. Click the Volume module, or right-click the tray's sound icon.

## 0.38.115 (Build 3 - experimental)
- Weather, Now playing and Tray popups now follow your widget style.
- Shorter bar right-click menu: Settings, Restart and Exit.
- Shell mode removed - vShell always runs alongside Explorer.
- Settings > System: no "Windows shell" heading; "Run at login" moved to Behaviour.

## 0.38.114 (Build 3 - experimental)
- Settings backups: daily copies, Back up now, Restore, Export / Import (Settings > System > Backups).
- A damaged settings file no longer loses your setup - vShell uses your newest backup.
- vShell restarts itself after a crash and logs what went wrong.
- The rain notification no longer delays other notifications.
- Messages and questions from bars, widgets and the terminal always show in front.

## 0.38.113 (Build 3 - experimental)
- **Terminal: Hide the button (only the keys open it)** - a new tick in the Terminal module's settings. The module takes no room on the bar (or widget), and its keys (Ctrl+Alt+T …) still drop the terminal down from that bar. It only hides while keys are set, so there's always a way to open it.
- A **collapsible widget snapped to the bottom** (above a bar or the screen's bottom edge, or lined up with another widget's bottom) now rolls up under its header with its **top staying in place**, and opens back down to where it was. Before, collapsing moved it down to the bar and opening it again pushed it off the screen.

## 0.38.112 (Build 3 - experimental)
- Settings > a bar / widget: the module strip is now titled "Modules on the bar" / "Modules in the widget".
- A new **full-width bar** (Add bar) now starts with **Three zones** on. Bars you already have keep their setting.

## 0.38.111 (Build 3 - experimental)
- Settings > a bar / widget: the **live preview is gone**. In its place is a **module strip**: each module as a chip (its icon and name) in the bar's order - or in Left / Centre / Right with Three zones. Click a module to open its settings, drag it to move it (into another zone to change its zone), or **+ Add modules**. With lots of modules the names shorten, then only the icons show (hover for the name), then they wrap - no more scrolling sideways.
- **Show on screen** (on the strip, and on the Modules page): outlines the real bar or widget for a couple of seconds, so you can see which one you're changing. A widget that has slid away comes out for it.

## 0.38.110 (Build 3 - experimental)
- **Widgets with auto width**: modules stacked in one widget are now all as wide as the widest one (bars, graphs and lists fill the width, a note widens to match), instead of each keeping its own width.

## 0.38.109 (Build 3 - experimental)
- **New plugin: Radarr** (Settings > Plugins > Media). Connect it with Radarr's address and API key, then:
  - a **Radarr** module for bars and widgets: on a bar an icon with the number in the queue as a badge (red when a download needs a look), how far the downloads are and the time left - click it for the popup. The popup / widget has tabs **Queue** (progress, state, right-click to remove a download or blocklist it), **Upcoming** (monitored movies coming out in the next days), **Recently added** and **Missing** (a Search button on each, and Search all).
  - **!ra** in the run box shows the queue; **!ra movie title** searches Radarr and **Add**s a movie with the quality profile, root folder and "Available when" you pick on the Plugins page (or Search now for one you have that isn't downloaded).
  - **Notifications** when a movie is downloaded, a download fails, or Radarr has a new health warning or error - and, if you turn it on, when Radarr grabs a release.
- **New plugin: Sonarr** (Settings > Plugins > Media) - the same for your shows (Sonarr 3 and 4):
  - a **Sonarr** module: the queue badge and progress on a bar; tabs **Queue** (a season pack is one row: "S02E01-E10"), **Upcoming** (episodes airing in the next 1-30 days with the day and time, back-to-back episodes as one row), **Recently added** and **Missing** (one row per show with a Search button, and Search all).
  - **!so** in the run box shows the queue; **!so show title** searches Sonarr and **Add**s a show with the quality profile, root folder and **Episodes to monitor** (all, future, missing, first / latest season …) you pick on the Plugins page.
  - **Notifications** for downloaded episodes (a season pack is one notification), failed downloads, health issues and, if you turn it on, grabs.
- **New plugin: Tdarr** (Settings > Plugins > Media). Its address (and an API key only if Tdarr has authentication on), then:
  - a **Tdarr** module: on a bar an icon with the number of jobs running as a badge and how many files are queued. The popup / widget shows the **running jobs** (file, transcode / health check, fps, size, ETA and a progress bar - right-click to cancel one), the **nodes** (pause / resume, right-click to change the worker limits) and the **library totals** (in the queue, done, errors, space saved).
  - **!td** in the run box lists the running jobs and the nodes - Enter on a node pauses or resumes it.
  - **Notifications** when a file fails, a node disconnects or comes back, and (if you turn it on) when Tdarr has finished its whole queue.
## 0.38.108 (Build 3 - experimental)
- Settings > Bars / Widgets: the **Bar style** / **Widget style** link under "Shared by every …" opens that style's page straight away (it opened Appearance); Back goes back to Bars / Widgets.
- Appearance: "Match Windows to vShell's theme" is now called **Match Windows 11 colour to vShell's theme**.
## 0.38.107 (Build 3 - experimental)
- **4 more colour presets**: Warm Sunset (light), Deep Purple, Carbon Teal and Cosmic Blue (dark), each with its own secondary accent and background fade. If they were in your Saved colours they move to Presets.
## 0.38.106 (Build 3 - experimental)
- Appearance: going back to **Presets** after **Saved** (Themes, Colours, Bar style, Widget style) emptied the gallery and left Saved highlighted - it shows the presets again.
## 0.38.105 (Build 3 - experimental)
- Start menu: **Start Menu folders as folders** is now called **Show folders**, and it's on by default (turned on once for Start menus you already have) - programs in a Start Menu folder sit behind a folder row you click to open. Untick it for one A-Z list of every program.
- Taskbar: buttons always grow in / shrink away as windows open and close and always slide to their new place - those two settings are gone. **An app that wants attention** moved up to the main taskbar settings.
- Appearance: Themes, Colours, Bar style and Widget style each have two tabs, **Presets** (the built-in ones) and **Saved** (the ones you saved), instead of one list. They open on the tab of the one in use.
- **12 new colour presets**: light - Catppuccin Latte, Paper, Sakura, Arctic; dark - Windows Dark, GitHub Dark, Monokai, Forest, Monochrome; bright - Outrun, Sunset, Amber CRT.
- Appearance > Colours: **Background fade** (the colour a Gradient background fades into) is now in Your colours with the other four, instead of on the Background page as "Fades into". Every preset has its own.
- Application launcher and Tray: icons always slide to their new place when you rearrange them - the "Icons slide to their new place" setting is gone.
- Start menu settings: the note about Text size now sits right under the Text size row (it was under Icon size).
## 0.38.104 (Build 3 - experimental)
- **Groups inside groups** in the Application launcher: select a group and press **Add group** to put a new group in it (up to 4 levels), or move a group into another one with **In group**. Clicking a group inside the menu opens it in the same menu, with a **Back** row on top (Backspace / Left goes back too).
- **Start menu sections**, all off by default so the menu looks the same until you turn them on (Start menu settings > **In the menu**):
  - **Pinned** - your own shortcuts and groups as tiles at the top, edited with the launcher's editor (**Edit pinned…**). A group opens inside the menu with a Back row.
  - **Recent files** - the files you opened lately (the same list as Explorer's Recent), with when you opened them; choose how many (3-20).
  - **Start Menu folders as folders** - the programs in a Start Menu folder sit behind a folder row in the A-Z list; click it to open the folder (subfolders too), Back or Backspace to come out. Search still finds every program.
## 0.38.103 (Build 3 - experimental)
- The Tray module's settings no longer show Text size - it has no text, so it did nothing there. The same for a Launcher showing no names and a Taskbar of icons only (Icon only, always combined); Text size comes back as soon as they show names / titles.

## 0.38.102 (Build 3 - experimental)
- Tray module icon colours: just **Original** and **Tint** (One colour is gone - a tray set to it uses Tint). Tint starts with the theme's **Main Accent** at **40 %**.

## 0.38.101 (Build 3 - experimental)
- Tray module: **Icon colours** - Original (as before), **Tint** (a theme colour laid over the icons at a strength you pick, so they lean towards your theme and keep some of their own colours) or **One colour** (every icon's shape in the theme colour, like Windows' white tray icons). The colour is the theme's Text, Main Accent or Secondary Accent, and follows the theme.

## 0.38.100 (Build 3 - experimental)
- Tray: a tray icon's tooltip no longer flashes while you hover over it. When an app updated its icon or tooltip (some do every second) every icon was drawn again, closing the tooltip; now only what changed is updated, and the icons are drawn again only when one comes, goes, moves or is pinned / hidden.
- The network icon in the tray does nothing on a left click now; right-click still has the network menu.

## 0.38.99 (Build 3 - experimental)
- vShell's tray icon follows your theme: the vShell mark's three dots in the Main Accent, Secondary Accent and text colours (like the logo in Settings), drawn again when the theme changes.

## 0.38.98 (Build 3 - experimental)
- vShell has its own **tray icon** (in your Tray module, or Windows' tray without one). Click it for Settings; right-click for notification history, Check for updates, **Restart vShell** and **Exit vShell**. Its tooltip says when an update is ready. Turn it off in Settings > System > Show vShell in the system tray.
- A bar's right-click menu has **Restart vShell** above Exit.
- The Tray module has a **network icon** again: Windows 11 keeps its own in Explorer's taskbar where other trays can't get it, so vShell draws one - Wi-Fi with its signal, wired, or faded when not connected. Hover for the network's name, click for the networks Windows can see, right-click for network settings. It's on the bar to start with and can be hidden behind the arrow or moved like any icon. When Windows' own network icon is in the tray, vShell doesn't add a second one. Turn it off with the Tray module's Network icon setting.

## 0.38.97 (Build 3 - experimental)
- Run box: an app you started from it (Windows PowerShell …) shows under recent, and clicking it there starts the app again - it searched the web for its name instead.

## 0.38.96 (Build 3 - experimental)
- Settings > About: a new line under the name - "Bars and Widgets with a selection of plugins and modules to suit your needs." - and "Vibe coded with love by version". The "If something goes wrong" section is gone. Files has **Plugin SDK - create your own plugin** with Download (sdk.zip from the vshell-releases repository).

## 0.38.95 (Build 3 - experimental)
- The **Modules** tab on a bar's or widget's page opens the module editor straight away (no list of modules first). ‹ Back in the editor returns to the tab you were on.
- Settings > **Appearance** is a short list now, like Bars, Widgets and Notifications: the theme in use at the top (its picture and **Change theme ›**), then a row each for **Colours**, **Background**, **Bar style**, **Widget style** and **Match Windows to vShell's theme** with a summary of how each is set, and the time format right on the list. Each row opens its own page (Appearance › Colours …).
- The theme, colour preset and style galleries show all their cards on their own page (no Show all). Explanations are an (i) next to the setting, and the gradient settings only show when the background is a gradient.

## 0.38.94 (Build 3 - experimental)
- Settings > **Bars** and **Widgets** are lists now, like Plugins and Notifications: a row for each bar / widget with a summary of how it's set up (a widget's row has its Show switch), and the bars / widgets no longer fill up the sidebar. Click one for its page.
- A bar's page has tabs - **Layout**, **Look** and **Modules** - and a widget's has **General**, **Layout**, **Look**, **Slide away** and **Modules**. Modules lists what's on it (click one to edit it); Edit modules opens the module editor as before. ‹ Back goes to the list.
- The long explanations are an (i) next to the setting. Background opacity is one switch ("like the theme") with its own slider only when it's off; a widget's width and height have an **Automatic** switch instead of "0 = automatic"; outline, header and slide-away settings only show while they're on.
- Widget snapping and gaps moved from Desktop to the Widgets page (Snapping and gaps), with the widgets they're about.

## 0.38.93 (Build 3 - experimental)
- A plugin's page opened from Settings > Notifications goes back to Notifications with ‹ Back (it went to Plugins). Its title line says "Notifications › <Name>" and the sidebar keeps Notifications highlighted.
- With "Hide the Windows taskbar" on, changing any setting no longer makes Explorer windows' scrollbars flash: vShell only tells Explorer about its taskbar when that setting itself changes. (Match Windows to vShell's theme already only changes Windows' colours when the colour or light / dark actually changes - the log now says when it does.)

## 0.38.92 (Build 3 - experimental)
- Settings > Plugins is shorter: **Added** lists your plugins one per line with a dot for how each is doing (working, not set up, not loaded, waiting for a restart) and Set up / Update when there's something to do; **Get more** has the rest with search, and Add from a file. Updates show as a banner only when there are some.
- Each plugin has its own page (click it): **Connection**, **Notifications** and **Modules** - its connection and its notifications are together now. Modules shows where each of its modules is (Edit › opens it) and adds one to a bar or widget.
- Settings > Notifications is a short list: Cards, Quiet times and History, then what sends them, each with a summary of what's on and a page of its own. Service status, Weather and Sent to vShell have a switch right on the list. A plugin's row opens its page.
- The explanations on these pages are an (i) next to the setting instead of a paragraph under it, and the "0 = off" sliders (system warnings, counting repeats) have a switch instead.

## 0.38.91 (Build 3 - experimental)
- Adding a plugin again with a different build (a newer DLL from a file, or after removing it in the same run) now asks whether to restart vShell - it keeps running the build it loaded first until it restarts. Say no and a notification stays that restarts vShell when you click it, and the plugin's row says "restart vShell to use the new build".
- Plugin SDK: a plugin's popup can ask how much height it has (`PopupWindow.ContentRoom()`), so a long list fits the screen and scrolls instead of using a fixed height.

## 0.38.90 (Build 3 - experimental)
- Plugins with tiles (a row of servers or entities) can colour each tile's dot, rebuild their tiles when the list changes, and open a card at the tile that was clicked instead of at the whole module.
- A widget showing tiles one per row grows or shrinks when tiles come or go (it kept its old height until vShell restarted).
- A command palette command from a plugin can say what it did ("Restarting jellyfin (job 413) ✓") - before, any message was shown as an error.
- Plugin SDK: plugins.md explains tiles and palette messages; plugin-api.txt also lists the parts of a tile and of a reading (they were usable but not listed, so a change to them wasn't caught).

## 0.38.89 (Build 3 - experimental)
- Settings: Plugins has a puzzle piece icon instead of the house.

## 0.38.88 (Build 3 - experimental)
- A new release only brings the plugins that changed: an unchanged plugin stays in the release it's already in, and plugins.json says which one - vShell downloads it from there (so it isn't offered as an update when nothing in it changed).
## 0.38.87 (Build 3 - experimental)
- Settings > Plugins: the long description at the top is gone - the page starts with the updates row and the list.

## 0.38.86 (Build 3 - experimental)
- Plugin updates without a new vShell: a newer build of a plugin in a release is an update for it. Settings > Plugins shows each plugin's version, "Update to …" when there's a newer one (or why it needs a newer vShell), Update all, Check for updates (with when it last checked), and Restart vShell once something's updated.
- "Update plugins by themselves" (on): vShell looks a minute after it starts and every 6 hours, downloads the updates (checked against their SHA-256) and says so - "Restart vShell" on that notification starts it again with them.
- Plugins come from the newest release with this plugin API (pre-releases too when you get those; this version's own always), so a plugin added to a newer release can be added here too - as long as it only uses what this vShell has.
- A plugin built against a newer vShell works when everything it uses is here (it used to be refused).
- plugins.json says each plugin's version, SHA-256, the vShell it was built against and what it uses. plugins.cmd builds just the plugins and plugins.json, for updating plugins in a release.
## 0.38.85 (Build 3 - experimental)
- A plugin added from a file: before an update to a vShell with a different plugin API, the update says it'll need a new build of it (it used to stop working quietly).
- A plugin from the release that's on an older build (the update couldn't download the new one) gets this version's build in the background, used from the next start - it no longer stays on the old one for good.
- vShell's check of what a plugin uses is stricter (a part it can't reach counts as missing; members of list editors are checked too), and build.cmd's plugin API guard sees more: enum and const values, the interfaces a type has, a name that only changed case, and a failed listing.
- Plugin helpers: PluginHttp's clients follow redirects by default (GetFollowAsync steers them with followRedirects: false) and an API key header only goes to the same server, never over plain http after https; Poller can be stopped mid-poll safely, and several "poll now"s while it polls are one poll.
- Start-up looks inside each plugin's DLL once instead of several times.
## 0.38.84 (Build 3 - experimental)
- Plugins have an API now (version 1): a plugin built for vShell 0.38.84 or later keeps working in later vShells while the API version stays the same - no new build for every release. A plugin added from a file stays added through updates. vShell checks that everything a plugin uses is there before loading it, and says why on the Plugins page when it isn't.
- Other people's plugins can do everything V's can: run box keywords, a Weather source, a Notes store, links on notification cards, and the helpers vShell's own plugins use (web requests, polling). V's plugins use only that API too. docs\plugins.md explains it all; a build puts the list of what's in the API (plugin-api.txt) in dist\sdk.
- Add from a file… says when a plugin uses parts of vShell outside the plugin API (it may need a new build for a later vShell).- Power flow in a widget with a fixed width and height showed no readings and no moving dots (it only worked in an automatic-size widget). Climate got the same fix.

## 0.38.83 (Build 2 - experimental)
- Settings > Plugins is easier to find your way around: a search box, All / Added / Not added, and the plugins grouped (Smart home, Servers & network, Media, Devices …). Each one says who made it, what it adds (its modules, keywords, notifications) and whether it's set up - "Set up ›" / "Settings ›" jumps to its section on the page.
- The plugin list comes with each vShell release now (a plugins.json next to the downloads), so a new plugin V puts in a release shows up for everyone without vShell itself changing - V's own, and other people's that V has checked and shares.
- Add from a file… takes anyone's plugin: it shows the plugin's name, author and what it does before asking, and the plugin is listed as "added from a file" (removing it forgets it). It must be built for this vShell version; updates don't bring it along (add its build for the new version again).
- Plugins describe themselves (a manifest in the DLL), and a plugin's own version number can be anything - vShell checks the vShell it was built for. docs\plugins.md explains how to make one; a build puts it and the vShell.dll plugins compile against in dist\sdk.
## 0.38.82 (Build 2 - experimental)
- Jellyfin is now a plugin (vShell.Jellyfin.dll): add or remove it in Settings > Plugins. Now playing works without it as before (Windows' media sessions); with it, what the Jellyfin apps play gets its times (a progress bar you can seek) and the buttons work for a Jellyfin app Windows doesn't know about. The "someone is watching" notifications come with it.
  - If you had Jellyfin set up, it's added for you; its connection (the API key stays encrypted) and notification choices move over by themselves. Going back to 0.38.81 after this loses the Jellyfin settings.
- Plugins can now help Now playing (times, art and buttons for apps Windows knows little about) and run their own notification checks.
- Proxmox VE is now a plugin (vShell.Proxmox.dll): add or remove it in Settings > Plugins. The module (nodes, guests, storage, start / shut down / reboot / stop, delete, Open shell), the connection, the notifications and the palette's guest commands work as before.
  - If you use Proxmox, it's added for you; the connection (the token secret stays encrypted), the notification choices and every Proxmox module's settings move over by themselves. Going back to 0.38.81 after this loses the Proxmox settings.
- Home Assistant is now a plugin (vShell.HomeAssistant.dll): add or remove it in Settings > Plugins. It brings the connection, the Home Assistant entities (and Bubble buttons), Climate control and Power flow modules, the notification rules, the palette's Home Assistant commands, and vDrop.
  - Without it vShell works on its own: the Weather module uses Open-Meteo, notifications from vShell's own sources and the "Notifications sent to vShell" listener still work.
  - With it, the Weather module's "Weather from" gets a Home Assistant choice (a weather entity), the Notes module's "Stored in" gets vDrop (the add-on's feed), and vDrop's notifications come in.
  - If you use Home Assistant, it's added for you; the connection (the token stays encrypted), the rules, vDrop's notifications choice and every module's settings (entities, Bubble buttons, the climate entity and zones, the power sensors and colours, vDrop's tab and list) move over by themselves. Going back to 0.38.81 after this loses them.
  - Removing it: its modules show "Home Assistant - add it in Settings", a Weather module on Home Assistant says to add its plugin, and a Notes module on vDrop says the same - each comes back as it was when you add it again.
- Settings > Plugins is now only plugins: everything that connects to something else comes as one.
## 0.38.81 (Build 2 - experimental)
- Downloads (qBittorrent / SABnzbd) is now a plugin (vShell.Downloads.dll): add or remove "Downloads" at the top of Settings > Plugins. Everything works as before - the module (on a bar with its graph / ring / tile, in a widget as the list), the notifications, !qb / !sab in the run box, pasting a magnet link or a .torrent / .nzb address into the run box, dropping .torrent / .nzb files on the run box or the module, and the palette's alternative speed.
  - If you use qBittorrent or SABnzbd, it's added for you and downloaded on the first start of this version. Their connections, the notification choices and the module settings move over by themselves; the password and API key stay encrypted.
  - Removing it takes away the module (it shows "Downloads - add it in Settings"), the connections, the notifications, the keywords and adding by link / drop; adding it again brings them back as they were.
  - Going back to 0.38.80 after this loses the Downloads connections and module settings.
- UniFi (Network and Protect) is now a plugin too (vShell.Unifi.dll): add or remove "UniFi" in Settings > Plugins. Both modules, the console's connection, the notifications (devices offline, new clients, busy access points; doorbell rings and detections with a snapshot) and !u in the run box work as before.
  - If you use UniFi, it's added for you and downloaded on the first start of this version; the connection (both API keys stay encrypted), the notification choices and the module settings (including the devices you'd opened) move over by themselves.
  - Removing it takes both modules away (they show "UniFi - add it in Settings"), with the connection, notifications and !u; adding it again brings them back as they were. Going back to 0.38.80 after this loses the UniFi settings.
- Plugins: a widget a Protect module is added to (one that sizes itself) still starts at 600 x 400 - plugins can now set that for their modules.

## 0.38.80 (Build 2 - experimental)
- Settings: the Integrations page is now called **Plugins** - the connections (Home Assistant, Proxmox, Komodo, Seerr, printers …) are all there as before. The list at the top for adding and removing the downloadable ones is "Add or remove plugins". Every hint that said "Settings > Integrations" now says "Settings > Plugins".
- 3D printers are now a plugin (vShell.Printer.dll), like Komodo and Seerr: add or remove "3D printers" at the top of Settings > Plugins. Everything works as before - the module (print, webcam, temperatures, spool, files …), the notifications and the palette's printer commands.
  - If you have a printer set up (or a printer module), it's added for you and downloaded on the first start of this version (the module says "getting it…" for a moment). Your printers, the preheat presets, the notification choices and the module settings move over by themselves.
  - Removing it takes away the module (it shows "3D printers - add it in Settings"), the printers' section, the notifications and the commands; adding it again brings them back as they were.
  - Going back to 0.38.79 after this loses the printers and their settings.

## 0.38.79 (Build 2 - experimental)
- Seerr is now a plugin (vShell.Seerr.dll), like Komodo: add or remove it at the top of Settings > Integrations. Everything works as before - the module, the notifications, and !s in the run box (search and request, the requests waiting for approval, !s recent).
  - If you use Seerr (its address set, or a Seerr module), it's added for you and downloaded on the first start of this version (the module says "getting it…" for a moment). Its connection and module settings move over by themselves; the API key stays encrypted.
  - Removing it takes away the module (it shows "Seerr - add it in Settings"), its settings sections, notifications and !s; adding it again brings them back as they were.
  - Going back to 0.38.78 after this loses the Seerr connection and module settings.
- Run box: clicking a plugin's keyword in the history (`!ko …`, `!s …`) puts it back in the box with its list, instead of doing nothing. The run box's tooltip lists the plugins' keywords.

## 0.38.78 (Build 2 - experimental)
- Plugins keep their own settings: an integration's settings and its modules' settings are now stored by the plugin itself (in settings.json under "Integrations" and each module's "Extra") instead of being built into vShell. New integrations can come as a plugin without vShell itself changing.
  - Komodo is the first: its connection, notification and module settings move over by themselves the first time this version starts - nothing to set up again. The API secret stays encrypted.
  - Going back to 0.38.77 after this loses the Komodo connection and module settings (that version doesn't know where they are now).

## 0.38.77 (Build 2 - experimental)
- Run box: a command palette - type what you want done and it's offered as you type: "restart jellyfin", "ac 22", "kitchen light off", "pause the printer", "ssh pve1", "shut down vm 105", "volume 30", "lock the pc". Enter does the top one.
  - From everything that's set up: Home Assistant (lights incl. brightness %, switches, fans, scenes, scripts, buttons, covers, locks, climate temperature and modes), Proxmox (start / shut down / reboot / stop / suspend VMs and containers), Komodo (every stack, deployment, container and procedure action), 3D printers (pause / resume / cancel, emergency stop, preheat presets, cool down, lights, power devices), qBittorrent's alternative speed, saved terminal connections (SSH), and vShell / the PC (settings, a terminal, mark notifications read, lock, sleep, sign out, restart, shut down, mute, volume, play / pause, next, previous).
  - Words can come in any order and partly typed ("rest jelly"); "the", "to", "my" … don't matter. A number in the text goes to the commands that take one (a temperature, a brightness, the volume).
  - Things that are hard to undo (stop, shut down, cancel a print, destroy, unlock …) ask first: Enter once shows "Press Enter again", Enter again does it.
  - Start with / to see only commands ("/" alone shows how many each integration has). With two words or more, matching commands go above the web search; with one word they're listed under the apps.
  - What you ran goes in the run box history and finds the command again from there.
- Integrations: "Add or remove integrations" at the top of the Integrations page - integrations that come as plugins, each with Add / Remove. Komodo is the first: it's no longer built into vShell.exe but a plugin (vShell.Komodo.dll).
  - Add downloads it from the GitHub release of the vShell you're running (checked against its .sha256, like updates); "Add from a file…" takes a plugin DLL you have. A plugin is built for one vShell version, so updates bring the added plugins along before the new vShell starts (and going back to the old version still has its own).
  - Remove takes away its module(s), settings sections, notifications, run box keyword and palette commands, and stops it. Its settings stay: a Komodo module shows "Komodo - add it in Settings" (click it for Settings) and comes back as it was when you add Komodo again.
  - If you used Komodo before (its address set, or a Komodo module), it's added for you - the first start of this version downloads it (the module says "getting it…" for a moment).

## 0.38.76 (Build 2 - experimental)
- Terminal: pop a tab out into a window of its own - right-click the tab > "Pop out into a window", or Ctrl+Shift+O. The shell keeps running through the move (an SSH connection, or a program like Claude Code, carries on as if nothing happened), and the screen and scrollback come along.
  - The window is a normal one: on the taskbar and in Alt+Tab (named after the tab in front), moved by dragging the tab strip, resized from its edges, double-click the strip to fill the screen. Drawn like the drop-down: the widget style's corners, outline and shadow, the blur and the theme colours, following theme changes.
  - Buttons right of the tabs: move this tab back (to the drop-down or the widget it came from), keep on top of other windows (the pin - remembered), minimise, fill the screen / restore, close.
  - It holds tabs like the drop-down: new tabs, the profile menu, the connections / files panel. A tab can pop out again into another window, or move back from the tab's right-click menu.
  - Closing it asks first when a terminal in it is still running (they end with it). Its place and size are remembered for the next one.
- Terminal: xterm.js's serialize add-on (MIT, 0.13.0 for xterm 5.5) is built in, to copy a tab's screen.
- Home Assistant: clicks and changes (lights, switches, Bubble sliders, climate, notification actions …) now go over the live WebSocket vShell already keeps open, instead of a new web request each time - a little quicker. Over REST as before while the WebSocket isn't connected. A call that was sent but got no answer isn't sent again (a toggle would toggle twice); it's noted in the log.

## 0.38.75 (Build 2 - experimental)
- Launcher: a group menu that's too tall for the screen (a big bookmarks folder) now fits between the bar and the screen edge and scrolls - use the mouse wheel or drag the slim scrollbar. It also opens in the usual place next to the bar instead of jumping to the top of the screen.
- Launcher: group menus are at most 350 wide - long names (bookmark titles) end in "…"; hover one for its full name and address.

## 0.38.74 (Build 2 - experimental)
- Launcher: "Add bookmarks…" in Edit shortcuts and groups - keep a group in sync with a browser bookmarks folder (Chrome, Edge, Brave, Vivaldi, Opera; pick the browser / profile and the folder). Its bookmarks (subfolders included) show in the browser's order and open in your default browser; new ones appear and deleted ones go whenever the menu opens. Renamed in the browser follows, unless you gave it your own name in vShell. Works into the selected group or a new group named after the folder, alongside synced folders and hand-added shortcuts. Web addresses without an icon show a globe. (Firefox keeps bookmarks in a database vShell can't read.)
- Launcher: built-in icons for groups - Applications, Internet, Media, Music, Video, Games, Documents, Code, Tools, Settings, Shopping, Travel and more (54), picked from a grid in Edit shortcuts and groups. They're drawn in the theme's text colour (or its accent colour, with "In the theme's accent colour"), so they change with the theme. A custom icon still takes their place; picking a built-in one clears it.

## 0.38.73 (Build 2 - experimental)
- Network speed: "Interface" (module settings) - All with internet (automatic, as before: the adapters with a gateway, added up) or one adapter: Ethernet, Wi-Fi, a second card, a VPN, a virtual switch … (listed with its description and IP). A chosen adapter that isn't connected shows "No network" and stays chosen for when it's back. Each network module can watch its own.

## 0.38.72 (Build 2 - experimental)
- Widgets: a module that sets its own height (a note, a list, the printer panel …) sits at the top of its row. It was centred, so a short note in a widget with a tall row height had an empty gap above it (and a long one didn't).
- Notes (Folder): "New note…" in the title line's list - type a name (.md is added unless you type .txt) and it's made in the folder and shown straight away.

## 0.38.71 (Build 2 - experimental)
- Notes: "Stored in" has Folder as its own choice (vShell, File, Folder, vDrop). Folder: pick a folder of .md / .txt notes - the note shows one at a time with its name in a title line; click it to pick another. File stays one file. Each remembers its own file, so switching between File and Folder keeps both. (A 0.38.70 File note with a folder set moves to Folder by itself.)

## 0.38.70 (Build 2 - experimental)
- Notes (fixed height) and fixed-height widgets: the scrollbar's room is always kept (an empty strip until there's something to scroll), so the widget no longer gets wider the moment the text grows long enough to need a scrollbar.
- Widgets: pressing on a scrollbar (a note's, a list's - the thumb or its empty track) no longer nudges the widget along; it only scrolls. A note set to "Click to edit" doesn't start editing from its scrollbar either.
- Notes (File): "Folder of notes to switch between" (module settings, with Choose the folder…). With it set the note gets a title line - the file's name with a ▾ - click it for the folder's .md / .txt files (newest first, with how long ago each changed) and pick one to show it in the module. The list is read each time it opens, so new files show straight away; "Open the folder" at the bottom. Choosing a folder with no file picked yet starts on its newest note.

## 0.38.69 (Build 2 - experimental)
- Taskbar: "Same pins on every taskbar (synced)" (module settings > Pinned apps). Taskbars with it on share one set of pins - pin, unpin, drag to reorder or drop a program on one and the others follow. The first taskbar to turn it on brings its pins along. Each taskbar's own pins are kept untouched while it syncs: turn it off and that taskbar goes back to the pins it had before.

## 0.38.68 (Build 2 - experimental)
- UniFi: 2.5 / 5 Gbps ports are a stronger blue (#3D8BFF, was a pale #89B4FA that was hard to tell from unused ports) and 10 Gbps+ a stronger purple (#A855F7, was #CBA6F7) - in the port squares and the speed key.

## 0.38.67 (Build 2 - experimental)
Performance pass:
- Log: lines are queued and written together every half second in the background (was: open, append and close the file for every line, often on the UI thread). The same line repeated within 5 seconds is counted instead of written - "(the line above repeated 37 more times)" - so a loop can't flood the log or the disk. Flushed on exit.
- Icons Windows can't give (an app that's gone, e.g. a stale Steam entry): asked again after 20 s, 40 s, 80 s … up to 30 minutes, and logged once - was every 20 s forever, each time a taskbar rebuild and a log line.
- Terminal: the output timer (66 a second) only runs while a shell has output waiting - it ticked all the time, even with no tabs or the drop-down hidden. The text window's position check slows to 4 a second when nothing moves (25 a second while it does), and follows its window's moves straight away.
- Power flow: the 30-a-second animation timer stops while nobody can see it (locked, covered by a full-screen / maximised window), instead of ticking and doing nothing.
- Komodo: the list only redraws when something in it changed (it redrew on every read, every 5 seconds).
- 3D printer: the bar and the panel's print section only redraw when what they show changed (an idle printer redrew every 2 seconds).
- Service status: its 1-second timer stops when no module is watching any service.

## 0.38.66 (Build 2 - experimental)
- 3D printer: "Hide while the printer is off" (module setting) - once its printer can't be reached for half a minute (or from start-up), the module hides; it comes back by itself when the printer answers again (it keeps checking every few seconds).
- Widgets: a module that hides itself takes its row with it (no empty gap), and a widget whose modules are all hidden hides itself, coming back when one shows again (logged). This also applies to Now playing ("Hide when nothing is playing").

## 0.38.65 (Build 2 - experimental)
- 3D printer webcam: Live (new default) - the camera's MJPEG stream, smooth, up to the frames a second set (1-30, default 15). Snapshots - one picture every N seconds as before (less network / CPU). Module settings: Webcam, "Live: at most (frames a second)", "Snapshots: one every (seconds)".
  - The stream only runs while the webcam is on screen (not folded away, panel open, that printer chosen); it stops straight away otherwise.
  - A stream that doesn't work (WebRTC / HLS camera, can't connect) falls back to snapshots for a minute, then tries again (logged).

## 0.38.64 (Build 2 - experimental)
- 3D printer: Klipper's lights get on / off buttons next to the power devices - [output_pin …] pins with light / led / lamp in the name (SET_PIN), and [led] / [neopixel] / [dotstar] strips (SET_LED, white). The log lists the lights it found. (Power devices are Moonraker's own [power …] sections - smart plugs, relays; a printer without them shows only its lights.)
- 3D printer: the webcam has a title line ("Webcam · USB" with an arrow) - click it to fold the picture away, click again to show it. Remembered per module; while folded no pictures are fetched.
- 3D printer in a widget: the Files / History / Queue / Macros / Console tabs, the printer pills and the webcam (live stream) work with one click - the first click's press went to the widget (a drag), so it took two.

## 0.38.63 (Build 2 - experimental)
- 3D printer webcam behind a reverse proxy (https): the web server redirects /webcam?… to /webcam/?… on http, which .NET won't follow from https ("answered 301"). vShell now follows webcam redirects itself, staying on https for the same host.
- 3D printer: heater names get a little more room (Heater bed outer no longer runs into its temperature).

## 0.38.62 (Build 2 - experimental)
- 3D printer webcam:
  - When the snapshot address doesn't give a picture, vShell takes the first frame of the MJPEG stream instead.
  - A webcam with no snapshot address gets one from its stream address (?action=stream -> ?action=snapshot).
  - Older Fluidd: cameras kept in Fluidd's own settings are found too.
  - The panel says "Loading the webcam…" or why it can't be shown (the address it tried and what came back) instead of showing nothing; the log has the webcams found (with their addresses) and why a picture failed (once per reason).
- 3D printer: wider heater names (Heater bed outer … no longer cut off).

## 0.38.61 (Build 2 - experimental)
- Build: no more CS4014 warnings from the 3D printer panel (its background loads hand their results back without waiting).

## 0.38.60 (Build 2 - experimental)
- New: 3D printer module (Klipper + Moonraker - Fluidd / Mainsail). One or more printers.
  - Integrations: "+ Add printer" - name, address (Fluidd's, or Moonraker's :7125), API key only if Moonraker asks for one, self-signed certificate, which webcam, Test connection (finds Spoolman, power devices, webcams, the queue). Preheat presets (PLA 210/60, PETG 240/80 …) shared by every printer.
  - On a bar: a dot (green printing, amber paused, red a problem), progress, time left / finish time (12-hour) and nozzle / bed temperatures; several printers side by side. Click it for the panel.
  - The panel (popup, or the widget itself): the print with its slicer thumbnail, name, progress bar, layer N of M, time left and when it'll be done, time printing, filament used, fan. Pause / Resume / Cancel / Emergency stop (asks first), Print again after a print. Klipper shut down: its message, Restart Klipper and Firmware restart.
  - Webcam: a snapshot refreshed every few seconds (flips / rotation as set in Fluidd); click it for the live stream.
  - Temperatures: every heater with current → target, a small graph and Set; other sensors (chamber, MCU …) in a line; preheat preset buttons and Cool down.
  - Spoolman: the active spool (colour, vendor, material, grams left - amber under 100 g) and Change (pick another spool, or none).
  - Power devices: on / off buttons (asks before turning one off; locked ones can't be switched while printing).
  - Tabs: Files (newest first, thumbnails, time and grams; ▶ prints, right-click: print, add to the queue), History (done / cancelled / failed, time, filament; right-click: print again), Queue (start / pause, remove), Macros (Klipper's, not the _ helpers), Console (send G-code, last lines).
  - Several printers: pills at the top of the panel to switch.
- Notifications (3D printer): print started, finished (with its thumbnail), paused, cancelled / failed / Klipper shut down, every 10 / 25 / 50 %, at layer N, and the bed has cooled down after a print. Work without a module too (checked every 10 seconds).

## 0.38.59 (Build 2 - experimental)
- Fixed: a module in a widget froze when it asked a question (Komodo "Run procedure …?", Proxmox shut down / reboot / force off, Seerr, the delete-a-guest and terminal-connection dialogs). The question belonged to the widget, which sits behind every window and never takes the keyboard, so it opened out of sight and blocked the widget. Questions and dialogs from a widget now open on top of everything; from a popup or a bar they stay with it as before.
- Terminal tabs: a bigger close button - the × is larger and sits in a 20 x 20 click target that lights up under the pointer (it was just the small glyph).

## 0.38.58 (Build 2 - experimental)
- Fixed: with more than about 6 terminal tabs vShell went into an endless layout loop - the tab strip measured narrowed tabs while placing them, which changed their size and started the layout again. While it looped, vShell's menus (a bar's, the terminal's ▾ / …) opened in the top-left corner of the screen and the terminal could stop drawing. The tabs are now sized in one go.
- Right-click menus everywhere (a bar's, modules', the terminal's ▾ / … menus) open at the mouse pointer again. Once the terminal's text window was in use, WPF put them in the top-left corner of the screen - even when told the exact spot. vShell now checks where each "at the pointer" menu landed and, if it isn't at the pointer, moves it there itself (kept on that monitor). The log notes the first few with what WPF did.

## 0.38.57 (Build 2 - experimental)
- Settings: logs how the window came out the first time it opens (size, scaling, the sidebar list and its first item, the live preview) and any error while filling the sidebar - to find why a tester's sidebar and live preview are empty.

## 0.38.56 (Build 2 - experimental)
- Fixed: vShell could hang on its splash screen with a full-width bar that's too full for its modules. Stepping the modules down changed the width of the module row (an overfull row is as wide as its modules), which vShell took for "the bar changed width", so it went back to the sizes as set and stepped down again - forever (the log fills with "Bar …: too full by … px - stepping down (1)"). It now goes by the room the bar gives its modules, and never starts over more than a few times in a row.

## 0.38.55 (Build 2 - experimental)
- Terminal menus (▾ new tab with…, "…" all tabs, My key, a connection's and a file's right-click menu) open at the mouse pointer in both the drop-down and widgets - they were still opening in the top-left corner of the screen. vShell now places them on the screen itself instead of leaving it to WPF.
- Settings: a Terminal module shown in its widget no longer has the Popup section (it has no drop-down for the background effect to apply to).

## 0.38.54 (Build 2 - experimental)
- Terminal in a widget: the tab strip no longer widens the widget as tabs open - they narrow, then go behind the "…" button (the strip was asking for the width of every tab, and a widget grows to what it's asked for).
- Terminal: the ▾ (new tab with…), "…" (all tabs) and My key menus open at the pointer - in the drop-down terminal the ▾ menu was opening at the far left.

## 0.38.53 (Build 2 - experimental)
- A new terminal in a widget is 700 x 600 by default (was 560 x 320).
- Terminal in a widget: click into it and the widget comes in front of other windows (it was staying behind them on the desktop); click elsewhere and it goes back to the desktop. Its own dialogs and the side panel don't send it back.
- Terminal tabs:
  - SSH tabs keep their connection's name (ssh was renaming them to C:\Windows\System32\OpenSSH\ssh.exe). A shell whose title is just its program's path shows the profile's name instead (PowerShell, Command Prompt…).
  - Tabs get narrower as more open (their names trim) instead of widening the widget; when even narrow ones don't fit, the rest go behind a "…" button at the end of the strip - its menu lists every tab. The chosen tab is always shown.
  - The mouse wheel over the tabs moves to the next / previous tab.

## 0.38.52 (Build 2 - experimental)
- Terminal in a widget fixed: the widget gave it one row (40 px) instead of its height, so it showed as a thin strip while its text spilled out over the desktop. It now gets its own height (Terminal settings > height in a widget).
- The terminal's text window now only covers the part of the terminal that's really shown (cut off by its holder, or scrolled in a fixed-size widget) - it never spills outside the widget.

## 0.38.51 (Build 2 - experimental)
- Proxmox guest menus tidied: one "Open shell (SSH)" item.
  - A running container: opens its console (pct enter, through its node) - no more separate "Open console" / "Open shell on <node>" items.
  - A running VM: SSH straight into the VM (new). "Open shell on <node>" is gone from guest menus (right-click the node for that).
- VM Open shell (SSH):
  - Uses your saved terminal connection with the VM's name (or address) when there is one.
  - Otherwise asks the VM's QEMU guest agent (through Proxmox) for its address, then offers to save a connection for it (filled in) or connect without saving.
  - No address (the guest agent isn't installed / running, or the token may not ask it): says why and offers to make a connection for it with the name filled in - type the address once and it's used from then on.
  - The token needs VM.GuestAgent.Audit (Proxmox 9) or VM.Monitor (Proxmox 8) to ask a VM's guest agent - PVEVMAdmin has it; the Integrations info says so.

## 0.38.50 (Build 2 - experimental)
- Terminal: SSH.NET (the file panel's SFTP library) updated from 2024.2.0 to 2026.0.0 - clears the two "known high severity vulnerability" build warnings (both were in its SCP part, which vShell doesn't use, but now they're gone).
- Build: a helper in the file panel renamed so it no longer hides WPF's Parent property (warning CS0108).

## 0.38.49 (Build 2 - experimental)
- Terminal, stages 2 and 3 - SSH connections and files:
  - Side panel (the ☰ button left of the tabs; it remembers open / closed) with two views, Connections and Files.
  - Saved connections: name, folder, host, port, user, sign in with a password, a key file or the SSH agent, an optional jump host, a command to run on connect, a tab colour and the folder the file panel starts in. Kept in vShell's settings (not in the module), so every terminal - on any bar or widget, removed and added again - has them.
  - Passwords and key passphrases are saved encrypted for your Windows account. ssh asks vShell for them itself (SSH_ASKPASS - never typed or shown); a server seen for the first time asks to be trusted in a message box. Without a saved one, ssh asks in the terminal as usual.
  - Click a connection to open it in a new tab (also in the + arrow menu, by folder); right-click for Files, Edit, Duplicate, Copy my key to this server, Delete. Quick connect: type user@host (:port) and Enter. Import adds the hosts in your ~/.ssh/config (folder "SSH config"). Folders fold.
  - Your key: "My key" makes an ed25519 key (~/.ssh/id_ed25519) and copies its public key; "Copy my key to this server" adds it to the server's authorized_keys (signing in once with the password) and offers to sign in with the key from then on.
  - SSH tabs use Windows' own OpenSSH (ssh.exe) in the terminal - keys, agent, known_hosts, jump hosts as usual. A dropped connection stays in its tab: Enter connects again. A tab can have the connection's colour.
  - Files (SFTP) for the SSH tab in front (or a connection's Files): folders and files with sizes, double-click a folder to open it / a file to open it with its app, up, type a path, refresh; upload (button, or drop files and folders from Explorer), download, rename, delete (folders with everything in them, after asking), new folder, copy path, and "Open a terminal here". Uses SSH.NET (a second NuGet package); it checks the server's key (trusted the first time, warned if it changes). It can't go through a jump host or use agent-only keys.
- Proxmox: "Update node" is gone; right-click a node for Open shell (an SSH tab to it), and a guest's menu has Open shell on its node and, for a running container, its console (pct enter).
- Komodo: a stack / deployment / container's menu has Open shell on its server, and Open shell in the container (docker exec over SSH, bash or sh).
- Open shell uses the saved connection for that host (or one with its name); with none it offers to make one (filled in) or connect without saving. It opens in the first Terminal module's drop-down (or a widget terminal), or a drop-down from the first bar when there's no Terminal module.

## 0.38.48 (Build 2 - experimental)
- The run box logging is trimmed back: the log no longer notes every press / opening / keyboard; it still says whether the keys were registered, and why the box closed only when it goes by itself straight after opening.
- Run box keys from any window: the box no longer flashes and goes when an app (File Explorer, a browser ...) is in front. The Alt key going up made that app take the keyboard back. Now vShell presses a harmless key while Alt is down (so the Alt doesn't open the app's menu), and if the window that was in front takes the keyboard back in the first moment, the box takes it again. Clicking another window still closes the box.

## 0.38.47 (Build 2 - experimental)
- Run box keys over the desktop: fixed the box flashing up and going. With the desktop in front, Explorer's desktop took the keyboard back as the Alt key went up, and the box closed. Now the box takes the keyboard back (a click on the desktop still closes it).

## 0.38.46 (Build 2 - experimental)
- More run box logging: when the quick run box opens (and what was in front), when it gets the keyboard, and why it closes - with the window, its class and program that came in front - to find what makes it flash and go.

## 0.38.45 (Build 2 - experimental)
- The log now says which keys the run box and the terminal got from Windows ("Run box keys: Alt+Space registered" / "NOT registered - another program has them"), and notes each press of the run box keys - to find a program that takes them first.

## 0.38.44 (Build 2 - experimental)
- Terminal: fixed - no text and no typing. The text is now drawn by the regular WebView2 control (the one that worked in 0.38.42) in its own borderless window kept exactly over the terminal area of the drop-down / widget, see-through so the vShell card (blur, theme colour) shows around the text. The keyboard goes to it when it opens and when you click it; clicking between the tabs and the text doesn't hide the drop-down. vShell's terminal keys (Ctrl+Shift+T / W, Ctrl+Tab, Shift+Esc) are passed on from it.
- New "See-through text area" setting (on): off = the text area is the theme colour, solid (if the see-through one shows black on your PC).
- Attached to a bar, the width setting now works too: the terminal is centred under the bar, at most the module's width - and never wider than the bar's straight edge, so it starts where the bar's rounded corners stop.
- The log notes the terminal's start-up steps ("Terminal: ...") to help if it doesn't show.

## 0.38.43 (Build 2 - experimental)
- Terminal, made to look like vShell:
  - The drop-down is now drawn like a vShell widget - the widget style's corners, outline, shadow and glass edge, vShell's own blur (Blur / Frosted) under the theme colour - instead of a Windows window with acrylic.
  - Attached to the bar (new, on by default): it drops down flush from the bar, as wide as the bar, square on the bar's side and rounded on the far side, with the bar's corners, outline and glass edge - and slides out of the bar (and back in). Off: it fades in centred on the monitor at the module's width, as before.
  - Tabs look like a widget's tabs: rounded tops, the Main Accent line under the chosen one, the close × shows on hover.
  - Terminal colours from the theme (new "Colours" setting, Theme by default): blue is the Main Accent, magenta the Secondary Accent, red / green / yellow are vShell's status colours, the greys are tinted from the theme's background and text. Or pick One Dark, Dracula, Nord, Solarized or Campbell (the background and text stay the theme's).
  - It follows a theme / widget style change straight away, open or not (it didn't change colours before).
  - In a widget the terminal can now live in the widget itself ("Show the terminal in the widget", on by default; its height and width are settings): click in it to type - the widget takes the keyboard while you do. Its shells survive the widget being rebuilt (a theme change) and end when the module is removed.
  - Shift+Esc hides the drop-down.
  - Uses WebView2's composition control (the one that works inside vShell's see-through windows).

## 0.38.42 (Build 2 - experimental)
- New module: Terminal (stage 1 - local shells). A button on a bar or in a widget; click it (or press Ctrl+Alt+T) and a terminal drops down from that bar (or opens next to the widget), centred on its monitor; again and it hides - your shells keep running while it's hidden.
  - Tabs: Windows PowerShell, PowerShell 7, Command Prompt and WSL (whichever this PC has), plus your own profiles (name | command line | start folder) - e.g. "Proxmox | ssh root@192.168.1.10". + opens a tab with the module's profile, the arrow next to it picks one. Ctrl+Shift+T new tab, Ctrl+Shift+W close, Ctrl+Tab / Ctrl+Shift+Tab switch, middle-click a tab to close it. A shell that exits cleanly closes its tab.
  - A real terminal: Windows' pseudo console (the same as Windows Terminal) with xterm.js drawing it (the display VS Code uses) - colours, ssh, nano, htop, vim, PSReadLine all work. Ctrl+C copies when text is selected (otherwise it stops the command), Ctrl+V or right-click pastes, links open in your browser, the tab shows the shell's title.
  - In the vShell theme: its colours, Main Accent cursor and outline, rounded corners, and with a background effect (Appearance, or the bar's / widget's own) Windows' acrylic behind it with the theme colour over it. The module's "Background effect behind the terminal" turns that off.
  - Module settings: the profile new tabs start with, the keys (Ctrl+Alt+T, Ctrl+`, Alt+`, Ctrl+Alt+` or off), width and height (% of the monitor), hide when you click another window. Shared: the profiles, the font (Cascadia Mono by default) and text size.
  - Built on Microsoft's WebView2 (part of Windows 11): the project now uses the Microsoft.Web.WebView2 NuGet package, so the first build after this downloads it (needs internet once). xterm.js (MIT) is built into vShell.exe.
- Notifications page: each source's card folds away until its integration is set up - the title says "Not set up - add ... on the Integrations page" - and opens by itself once the connection is filled in. Click any source's title to fold / unfold it.

## 0.38.41 (Build 2 - experimental)
- Proxmox: right-click a node's name (in the Nodes list of a widget or the cluster popup) for Update node… - it opens Proxmox's own upgrade shell for that node in your browser (the same as the node's Updates > Upgrade button), running apt dist-upgrade where you can watch and answer it. Proxmox's API can't install updates by itself, so the shell is the way; sign in to the web interface as root@pam if it asks. The menu also has Open web interface.

## 0.38.40 (Build 2 - experimental)
- Proxmox: the Token secret info on the Integrations page now explains the permission for deleting guests (role PVEVMAdmin on /vms, which includes start / stop and adds VM.Allocate), and the module's note mentions it too.
- Test connection now says what the token can do with guests: start / stop, deleting, both, or only look - and which role to add for the rest.

## 0.38.39 (Build 2 - experimental)
- Proxmox: delete a VM or container from a guest's menu (Guests, or the cluster popup). Turn it on in the module's settings ("Allow deleting VMs and containers from vShell" - off by default). Delete… shows for a stopped guest (a running one shows "stop it first"). vShell asks you to type the guest's ID, with two boxes ticked as in Proxmox's own Remove: remove it from backup jobs, replication and HA (purge), and delete disks that belong to it but aren't attached. The row shows "deleting…" until it's gone. The API token needs VM.Allocate (for example the PVEVMAdmin role) on /vms; without it vShell says so.

## 0.38.38 (Build 2 - experimental)
- Moving picked widgets together now snaps the group properly: the group's outer edges (its leftmost, rightmost, top and bottom widgets) snap to the screen edges, the bars and the other widgets (edges, centres and the snap gap), and every widget in the group lines up with the widgets that aren't moving. The guide line runs across the whole group. Before, only the widget under the mouse looked for something to snap to.
- After a group drop, each moved widget links to what it now sits against (including the others in the group), so they keep the gap when one of them changes size.

## 0.38.37 (Build 2 - experimental)
- Glass edge is now part of bar styles and widget styles (and so themes): saving a style or theme keeps it, applying one sets it, and changing it on a bar / widget that follows the style lets that one keep its own look, like the other look settings.
- Built-in styles with a glass edge: bar styles Soft (Subtle), Round (Subtle) and Ghost (Strong, Subtle on Minimal - it gives the near-outline-less bars their shape); widget styles Soft card (Subtle), Borderless glass (Strong) and Bubble (Subtle). So the themes using them get it too: Mocha Soft, Midnight Ghost, Light Round, Glass, Synthwave Glow, Dracula Bold, Rose Pine Round, Everforest Soft, Kanagawa Round and Oceanic Slim. Re-apply a theme or style to pick it up. Saved styles and themes of your own are unchanged (no glass edge).
- Popups in the widget style (start menu, module popups, run box drop-down, notification cards and history) show the widget style's glass edge.
- The style and theme pictures in Appearance show the glass edge.
- The glass edge's description under the setting is gone.

## 0.38.36 (Build 2 - experimental)
- Widgets: drag one of several picked widgets (Ctrl+click to pick) and they all move together, keeping their places relative to each other. Snapping works on the one you drag (the others in the group are left out of it), Alt still places freely, and locked widgets stay where they are. The snap links between the moved widgets stay; widgets left behind that were snapped to one of them let go. Dragging a widget that isn't picked moves just that one, as before.

## 0.38.35 (Build 2 - experimental)
- The gradient outline is gone again (bars and widgets): outlines are one colour, as before 0.38.33. Settings saved with a gradient go back to the outline's own colour.
- The glass edge stays (Look > Glass edge: Off / Subtle / Strong).

## 0.38.34 (Build 2 - experimental)
- Gradient outline, reworked: the angle now turns smoothly. Before, the gradient was stretched over the whole bar, so 0-89 barely changed anything and 90 / 91 jumped. Now it's laid over the outline itself: on a bar against a screen edge (one long outline line) 0 runs the colours along the line, 90 runs them across the line's thickness (inner edge one colour, outer edge the other), and the angles in between mix the two. Widgets get the same smooth turning around all four sides, and the far corners always get the two end colours.
- The gradient's "To" colour shows the Secondary Accent when it's empty (it showed the Main Accent though the Secondary was used).
- The turning outline animation is gone (and its settings).
- Glass edge stays as it was.

## 0.38.33 (Build 2 - experimental)
- Gradient outline for bars and widgets: Outline > "Colour style" Solid (as before) or Gradient. A gradient goes from the outline colour (now "From", empty = Main Accent) to "To" (empty = Secondary Accent) at an angle, at the outline's opacity. "Turn slowly around the outline" makes it rotate - Slow, Medium or Fast (drawn at 30 frames a second to stay light).
- Glass edge for bars and widgets: Look > "Glass edge" Off / Subtle / Strong - a light line along the top, a darker one along the bottom and a faint sheen, so it looks like a pane of glass. Best with Frosted.
- Both are each bar's / widget's own settings for now: no theme, bar style or widget style sets them, and applying one leaves them as they are. Copy and paste of a bar's / widget's look carries them.

## 0.38.32 (Build 2 - experimental)
- Popups get the background effect: when a bar or widget has Blur or Frosted (its own, or the one in Appearance), the popups that open from it get it too - the start menu, the launcher, tray, calendar, weather, now playing, Proxmox, downloads, Komodo, Seerr, climate, UniFi and Protect popups, the bell's list, and the run box's drop-down and quick run box. The theme colour lies over it at the theme's background opacity, the same as the bar.
- Notification cards and the history window get the Appearance background effect too.
- Each module with a popup has "Background effect behind its popup" in its settings (Popup section), on by default: turn it off for the plain, almost solid popup background, as before. Notifications have their own: Settings > Notifications > Cards > "Background effect behind the cards and the history".
- Like the bars and widgets, the effect shows the blurred wallpaper (not the windows behind the popup).

## 0.38.31 (Build 2 - experimental)
- Wallpapers: with no picture chosen, a colour or gradient background now fills the whole monitor - or every monitor with "Same on all", or one gradient across the whole desktop with "Span across all". Clear the picture and pick Midnight, Ocean, your own gradient... and that's the wallpaper. The monitor map previews it.
- With no picture, Fit is hidden (there's nothing to fit), and Blur (which needs a picture) keeps the wallpaper the monitor had, as before - a note says so.

## 0.38.30 (Build 2 - experimental)
- Wallpapers, redone: vShell now sets them with Windows' own per-monitor wallpaper (no more pasting one big picture across the desktop). Each monitor's picture is made at that monitor's exact size, so Fit, Center and Fill look right on every monitor, even mixed sizes and orientations.
- New wallpaper page (Settings > Desktop > Wallpaper): your monitors drawn as they're arranged, each showing its wallpaper. Click one to change it, or drop a picture on it (a folder starts a slideshow).
- Three ways to set them: Each monitor, Same on all, or Span across all (one picture over the whole desktop, using Windows' own Span).
- Background behind a Fit or Center picture: a blur of the picture (the default), black, the theme colour, eight ready-made gradients (Midnight, Ocean, Aurora, Sunset, Dusk, Grape, Peach, Graphite), or your own colour or gradient with an angle.
- Slideshow per monitor (or for all): a folder, every 5 min / 15 min / 30 min / 1 hour / 3 hours / daily, shuffle, and Next picture.
- Monitors are remembered by their stable key, like the bars and widgets: unplug one and plug it back in and it gets its own picture back. A monitor with no picture set keeps the wallpaper it had.
- "Restore my original wallpapers" (or turning it off) puts back exactly what Windows had on each monitor before vShell, including the fit. Old wallpaper files from earlier versions are cleaned up.
- Notifications: the monitor chosen for the cards is now remembered by its stable key too, so the cards stay on that monitor when Windows renumbers the displays, and open cards move back when a monitor returns. Existing settings pick up the key on their own.

## 0.38.29 (Build 2 - experimental)
- Notifications: repeats collapse. The same notification again (same source, title and level) within 10 minutes is one entry with a count - "Backup failed ×3" - and its card is updated (new text, time starts again) instead of another card popping up. It comes back to the top of the history as unread. Settings > Notifications > History sets the minutes (0 = off). vDrop notifications always stay separate.
- Bell badge: the bell itself takes the colour of the most serious unread notification (yellow for Important, red for Alert), and while an Alert is unread the badge glows; a new Alert makes it bounce. "The badge glows…" in the module's settings turns that off.
- Bell list: a ✓ on each unread notification marks just that one read, and a "History" button opens the full history. The list no longer marks everything read when it closes - they stay unread until you mark them (the module's "Closing the list marks them read" brings the old way back).
- History window, rebuilt: search (title, text, source), All / Unread, the levels (Normal, Important, Alert - each on / off), one source from a list, Mark read and Clear for what's showing (Clear needs a second click), day headings, the count kept / unread, and everything that's kept, 150 at a time. Esc clears the search, then closes.
- The history keeps 500 by default (was 100; installs on 100 move to 500) and can keep up to 5000.

## 0.38.28 (Build 2 - experimental)
- Notifications from vDrop: apps on your network send notifications to the vDrop add-on (1.4.0) the way they would to a Gotify server, and vShell shows them with its own notification cards and history. When vShell connects it shows the unread ones that arrived since this PC was last connected (more than three at once: one "N notifications while this PC was away" card, all of them in the history), then new ones live. The very first time it starts from now.
- Gotify priority: 0-3 goes to the history only, 4-7 a Normal card, 8-10 an Alert. The app's name is the source; a click link and a picture come along.
- Read state goes both ways: clicking one, closing its card or Mark all read reads it in vDrop too (and so on the phone); reading it in vDrop or on the phone marks it read here.
- vShell tells vDrop it's online about once a minute, so vDrop can keep notifications off your phone while a PC is on (the add-on's default).
- Settings > Notifications > "vDrop notifications (Gotify)": on by default (needs the Home Assistant connection). Without the add-on it looks for it now and then, less often each time, and stays quiet.
- Notes on vDrop: Kind shows the same buttons as the other stores, with just Note (instead of a list with only one choice in it).

## 0.38.27 (Build 2 - experimental)
- Monitors: bars and widgets go back to their own monitor when it's reconnected. While a monitor is unplugged (or off), its bars and widgets show on the main monitor for now, but they keep their place on their own monitor - snap links and other automatic moves no longer overwrite it - and move back a moment after it returns.
- Each bar and widget now also remembers the monitor itself (its model and port), not just Windows' DISPLAYn slot, which Windows can hand to a different monitor after a reconnect. Existing setups pick this up the first time they start with all monitors connected.
- vShell now notices Windows' "monitors changed" message even without a bar on the changed monitor, and checks again a few seconds later for a monitor that's slow to wake. The log notes each change.
- Dragging a widget while its monitor is away still moves it for good, as you'd expect.

## 0.38.26 (Build 2 - experimental)
- Notification cards: closing a card with its ✕ marks the notifications on it as read (the unread count and the history follow). Clicking one already did; ones that just time out and slide away stay unread.

## 0.38.25 (Build 2 - experimental)
- vDrop feed: the first tab is now Inbox (was All). The list on the right is Inbox, Pinned or Archived (Everything is gone) and only shows on the Links, Media and Notes tabs - the Inbox and Archived tabs are lists of their own. A widget that was on Everything goes back to Inbox.
- Goes with vDrop add-on 1.2.0, which has the same tabs and list.

## 0.38.24 (Build 2 - experimental)
- vDrop: "Done" is now "Archived" - its own tab next to All, Links, Media and Notes (the Inbox list is Inbox, Pinned or Everything). Archive / Unarchive replace Mark done / Back to the inbox, with an archive-box icon instead of the tick. A widget that was showing Done opens on the Archived tab.
- vDrop: in a widget with a set height, the feed fills the widget down to its bottom edge (and follows when the widget is resized). Without one, Height in the module's settings sets it.
- Notes on vDrop: Kind is always Note (the other kinds don't apply to the feed), and "Show when it was last edited", "Start editing with" and Lock are hidden.
- Goes with vDrop add-on 1.1.0 (same wording and tab there); older add-ons work too.

## 0.38.23 (Build 2 - experimental)
- Notes on vDrop is now vDrop's feed, laid out like vDrop's own page: tabs for All, Links, Media and Notes; a list for Inbox, Pinned, Done or Everything; search; then everything shared - oldest at the top, newest at the bottom, with Today / Yesterday / date dividers and pinned items first in the inbox.
- Each item shows who shared it (phone, iPhone, web or vShell, with the device's name), the app it came from ("via Chrome") and when (12-hour), its text with clickable links, and for links vDrop's preview card: site name and icon, author, title, description and picture - large under the text, or a square on the right; videos (YouTube and the like) get a play button that opens them in your browser. Images show as pictures.
- Pictures and site icons are fetched from vDrop and kept on this PC (vdrop-thumbs folder), so they show at once next time and offline.
- The box at the bottom adds a note or a link (Enter adds, Shift+Enter for a new line). Double-click an item to change its text (and a link's title); Ctrl+Enter saves, Esc cancels. Right-click an item: Open link, Copy, Edit, Pin / Unpin, Archive / Unarchive, Refresh preview, Delete.
- Hover an item for vDrop's little button bar at its top right: Open link, Copy (a tick shows it worked), Pin / Unpin (lit when pinned), Mark done / Back to the inbox, Edit, Refresh preview and Delete - Delete turns red and needs a second click. The item lights up slightly under the mouse.
- Changes made here show "not synced yet" until vDrop has them. Switching a Notes widget to vDrop the first time makes it 420 x 520 if it was at the note's default size; Width and Height in its settings change it.
- Widgets that slide away: the right-click menu no longer flashes and disappears. While the widget is out it sits on top of your windows, and vShell's regular tidy-up of the window stacking put it back on top - over its own menu, which then closed. It now leaves a widget alone while its menu or popup is open.
- vDrop: finding the add-on is sturdier. It now looks through Home Assistant's states over its own connection (sensor.vdrop, or any sensor with "vdrop" in its id that has the ingress path - e.g. one renamed in Home Assistant), and failing that asks vDrop directly (a "ping" command, answered with its ingress path). The log says where it found it, or lists the entities with "vdrop" in their id when it can't.

## 0.38.22 (Build 2 - experimental)
- Notes: new "Stored in" choice, vDrop - two-way sync with the vDrop Home Assistant add-on. vDrop's notes are the widget's notes (flip through them with the arrows or the mouse wheel; pinned first, then newest). Add, edit, rename or delete a note here and it changes in vDrop; add or edit one in vDrop (phone, web) and it shows here straight away. Right-click: Pin to the front / Unpin, Sync with vDrop now.
- Uses the Home Assistant connection already set up (no extra address or token) - its token must belong to an administrator. Reads and writes through the add-on's ingress API, with live updates from its "vdrop_changed" events.
- The notes are kept on this PC (vdrop-cache.json), so they show at once and offline; changes made offline are sent when Home Assistant is back. The footer says when it's offline or can't reach vDrop.
- A widget's own vShell notes are kept when you switch it to vDrop (switch back to see them).
- Home Assistant module: losing the connection (or Home Assistant restarting) no longer leaves the module - and its widget - wide afterwards. While it's down an entity shows "n/a" and, during a restart, its long entity id instead of its name; that used to set the width for good. Now the first real value after it's back sizes it again.

## 0.38.21 (Build 2 - experimental)
- Start menu: the module's S / M / L sets the size of the text in the menu (S small, M medium - the default, L large); the menu gets a little wider with bigger text so names still fit.
- Start menu: "Show the word Start next to the icon" is gone - the button is just the icon.

## 0.38.20 (Build 2 - experimental)
- Start menu: a choice of icons - vShell mark (default), Four tiles, App grid, Orbit, Hexagon, Stacked bars, Diamonds - all drawn in your theme's Main Accent, Secondary Accent and text colours, plus Custom icon (an image, or a program / DLL's icon). Settings shows them as previews in the current theme; click one to use it. A Start button that had a custom icon keeps it.
- Start menu: the icon colour setting is gone (the icons follow the theme).

## 0.38.19 (Build 2 - experimental)
- Clock: the "Text size (%)" slider is gone - S / M / L sets the size. Clocks that had it changed go back to the normal size for their S / M / L.
- Clock: "Seconds in the Secondary Accent colour" and "Time separators ( : ) in the Secondary Accent colour" are gone (the time is all one colour). "Date in the Secondary Accent colour" stays.
- Text label: its "Text size (%)" slider is gone too - S / M / L sets the size.
- Appearance: the font choice is gone - Segoe UI is the font everywhere (a font picked before goes back to Segoe UI). "Font and times" is now "Time format", at the bottom of Appearance.

## 0.38.17 (Build 2 - experimental)
- Seerr: tick boxes for the tabs to show (Requests, Recently added, Trending, Upcoming) instead of the View list - in the widget and the bar's popup. With one ticked, just that view shows, without tabs (a widget sizes to it); with more, tabs for the ticked ones, and "Opens on" picks the one it starts on. At least one stays ticked. Existing widgets keep what they showed (all tabs, or their one view). "Requests to show" and the filters only appear when Requests is ticked.

## 0.38.16 (Build 2 - experimental)
- Seerr posters (Recently added, Trending, Upcoming): round ‹ › arrows either side of the posters page through the rest of that view, "How many" at a time (up to 60 titles). An arrow fades out at the start / end; each view remembers its page while the data refreshes. The arrows only appear when there are more titles than "How many". Works in the widget and the bar's popup.

## 0.38.15 (Build 2 - experimental)
- Widgets: "Hide this widget" is no longer in the right-click menu (turn a widget off or remove it in Settings > Widgets).

## 0.38.14 (Build 2 - experimental)
- Seerr widget: "View" now has All as well as Requests, Recently added, Trending and Upcoming (it replaces the "Show the tabs" tick). All shows the tabs, and the widget is as tall as the tallest one; any other view shows only that one, with no tabs, and the widget sizes to it - so a short Upcoming row of posters isn't held as tall as the Requests list. "Requests to show" and the filters only appear when the view includes Requests.

## 0.38.13 (Build 2 - experimental)
- Seerr posters: shows exactly "How many" posters - it used to show at least 5 (2 set gave 5).
- Seerr posters: no more empty space under the posters. The widget kept the tallest it had been (from 2 rows, or a wider size) after you changed "Rows of posters", the text size or its width; it now starts again from the new layout. (Switching tabs still doesn't make it jump.)

## 0.38.12 (Build 2 - experimental)
- "Match Windows to vShell's theme" is on by default (light / dark mode and accent colour). Existing installs get it turned on once when updating; turning it off afterwards sticks, and turning it off puts your own Windows colours back.

## 0.38.11 (Build 2 - experimental)
- Bar styles: M (two lines) now fits bars from about 33 tall (was 37), so the Sharp, Ghost and Hairline styles and most Minimal variants keep M instead of dropping it to S. Slim (30) and Hairline Minimal (32) are still too short for two lines and show M as S.
- Bars and widgets: their own text (popups, menus, widget headers and messages) is S in the chosen font, the same on every style and theme - it used to come from the old Font size setting, which can no longer be changed.
- Widget styles: the preview in Settings no longer shows each style's own padding (widgets take it from Comfortable / Compact now).
- Match Windows to vShell's theme no longer lags: vShell's own theme goes on straight away and Windows follows about half a second later on a background thread, once changes stop (dragging a colour picker sends Windows one change, not dozens). Apps that are slow to answer Windows' "colours changed" message no longer hold vShell up, and starting vShell no longer briefly puts your own Windows colours back before applying the matched ones.

## 0.38.10 (Build 2 - experimental)
- "Keep the box size" / "Keep widget size" is gone - with S / M / L text a widget keeps its size by itself. Widgets that had it go back to sizing themselves to their modules. (A width or height set in Settings or with Resize still works.)
- Power flow: its text follows S / M / L even when the widget's width is set (it only gets smaller when there's much less room than it needs).
- Notes: "Grow with the text" is off for new notes modules.
- Seerr posters: "Rows of posters" instead of columns - "How many" posters spread over that many rows.

## 0.38.9 (Build 2 - experimental)
- Found it (thanks to the log): widgets weren't getting smaller text - the Lights widget was being built at M both times - it was coming up too wide. Before Home Assistant answers, each entity shows its id ("light.door_outside_light …"), which is longer than its name; the module keeps its text from shrinking (so numbers don't make it twitch), so the widget stayed as wide as those ids, which made the text look small in it. Now a placeholder - the entity id with "…", a "set up …" message - doesn't count: the first real value starts the width afresh. The same for the CPU / network / disk … modules' "…" and "n/a".

## 0.38.8 (Build 2 - experimental)
- Diagnostics: widgets write how they size their modules to vshell.log ("Widget '…' build: …" and "Widget '…' fit: …") to trace the small-text-at-start-up problem.

## 0.38.7 (Build 2 - experimental)
- Widgets with a set size still came up with small text: before a module has its data its text is often longer (Home Assistant entity ids, "Loading…"), so a list widget looked too wide, stepped down to S, and nothing changed size afterwards to make it check again. Now a widget whose modules are listed one under another only steps down when it's too short (long text in a list just trims); being too wide only counts for modules side by side (Horizontal / Grid). It also checks once more a few seconds after start-up, once the data is in.

## 0.38.6 (Build 2 - experimental)
- Widgets with a set size no longer start with small text / short graphs. Like the bars, a widget was checked for room before it had its real size (and before its modules had their data), looked too small and stepped everything down. It now waits until the widget is its real size, and checks again a moment after its contents change - going back to the sizes as set first, so data arriving after start-up doesn't leave it stepped down.

## 0.38.5 (Build 2 - experimental)
- Widgets: S / M / L now always change the text. A widget's module height (smaller still with Compact) was making M and L fall back to S - in a widget the row now grows to fit the text instead (Proxmox, Home Assistant, notes and the rest).
- Power flow: its size follows its text size (S / M / L) instead of filling the widget's whole width; it only gets smaller when there isn't room.
- Widgets: no Padding setting - Comfortable / Compact sets it.
- Seerr posters: "Columns of posters" (1 - 10). The widget shows "How many" posters in that many columns and sizes itself to them (a widget with a set width sizes the posters to fill it).
- Settings: the unused two-line / list text size options are gone from the code (the build warnings).

## 0.38.4 (Build 2 - experimental)
Widgets get S / M / L text too, the same as bars.
- Each widget module has a Text size (S / M / L) at the top of its settings; the widget's Font size and the modules' own font sizes are gone, and so are the "two lines" options (M is two lines) and Top programs' list text size. New modules start at M.
- Every font is drawn to take the same room, so a widget is the same size whatever the theme or font. M needs a module height of about 37 and L about 25 - smaller and they show as S.
- Graphs in widgets work as on bars: height from the module height, length from "Graph length".
- A widget with a fixed width or height ("Keep widget size", or set in Settings) that's too small for its modules steps down like a full bar: graphs shorter first (to 20 %), then L text as M, then everything S. A theme change or resize starts again from the sizes as set.
- Existing widgets: each module gets the nearest size (S up to 12, M 13 - 15 or if it was on two lines, L 16 and up).
- The clock in a widget follows S / M / L the same way (Time and date on two lines at M, one line at S and L).

## 0.38.3 (Build 2 - experimental)
- Settings: changing a bar module's zone (Left / Centre / Right) now also moves it in the module list under the live preview.

## 0.38.2 (Build 2 - experimental)
- Bars no longer start with small text and short graphs. When vShell opened, a bar was checked for room before it had its real width, so it looked "too full" and stepped everything down; it now waits for the bar's real width (and fits again whenever the bar's width changes).
- Clock layouts: "Time and date" (was "Time and date on one line"), "Date and time" (was "Date above time" - on one line it's the date, then the time) and "Large time - small date" (was "Large time above small date" - on one line at S and L the time is larger than the date). "Time above date" is gone: "Time and date" at M shows the time above the date. (Widgets that use "Time above date" keep it.)
- Bar module settings: Text size (and Zone, on a bar with zones) are at the top, under Settings.

## 0.38.1 (Build 2 - experimental)
- Text size: the S / M / L description is now in an info icon next to "Size". New modules start at M.
- Two-line (M) layouts for more bar modules: the clock (time over date - S and L show it on one line), weather (temperature over the condition / high-low), UniFi Network (speeds over clients), UniFi Protect (cameras over the last event) and climate control (room temperature over the target). CPU / memory / GPU (temperature under), HWiNFO, network, disk, downloads and Now Playing already had them.
- Top programs on a bar: no "List text size" (the module's S / M / L sets it). A list only shows as many lines as fit the bar: the line spacing closes up first, then fewer lines are shown. With a fixed width, the width no longer changes with S / M / L.
- Network: Ring and Tile styles, like the other graph modules (download speed in the middle, upload under it).
- Downloads: turning "Show the upload speed" back on shows it again (it only showed while something was uploading).

## 0.38.0 (Build 2 - experimental)
Bars get S / M / L text instead of a font size. (Widgets are unchanged for now.)
- Each bar module has a Text size: S (one line, small - 12), M (two lines where the module has them - 14) or L (one line, large - 18). The bar's own Font size setting is gone, and so are the modules' own font size and the "two lines" options on bars (M is two lines).
- Every font is drawn so it takes the same room as Segoe UI - a narrow font a little bigger, a wide one a little smaller - so a module is the same width whatever font or theme you pick.
- M needs a bar about 37 tall and L about 25; on a slimmer bar they show as S.
- Graphs stay as they were: height a percentage of the bar, length as set (now up to 600 - "Graph length"); rings follow the bar height.
- A full bar fits itself: when its modules don't fit, graphs get shorter first (80 %, 60 %, 40 %, down to 20 % of their length), then L text shows as M, then everything as S. Your settings don't change - switch back to a theme with more room and everything returns to what you set.
- Existing bars: each module gets the nearest size - S, or M if it was on two lines, or L if its font was 16 or more.

## 0.37.17
- Windows colours (Settings > Appearance): "Match Windows to vShell's theme".
  - Light / dark mode: Windows (apps and the system) goes light or dark with how light vShell's background is.
  - Accent colour: Windows' accent becomes vShell's Main Accent, with its lighter / darker shades worked out like Settings does - Start, Settings, File Explorer selections, toggles, and title bars when "Show accent colour on title bars" is on. Windows' "pick an accent from my background" is turned off so it doesn't replace it.
  - Follows the theme as it changes (presets, your colours, Wallpaper Theme). Your own Windows colours are saved the first time and put back when it's turned off. A few apps only pick up a new accent after a restart.

## 0.37.16
- Widgets: "Keep widget size" in the right-click menu (under Resize), ticked when it's on. The same as Settings > the widget > Size > Keep the box size: it freezes the size the widget has now so themes, styles and fonts no longer change it; untick it to let the widget size itself to its modules again.

## 0.37.15
- Seerr posters keep the widget's size when the theme changes. In a widget you've sized, the posters now fill a set number of rows (worked out from "How many" the first time) and the number across follows the width. A theme with bigger text shows fewer posters instead of adding a row and making the widget taller; going back to a theme with smaller text shows more again. Resizing the widget does the same: wider = more per row, same rows. Changing "How many" works the rows out again.
- Komodo widget: opening a stack's containers (the drop-down arrows) no longer makes the widget taller - the list keeps the height it had with nothing open and scrolls instead.

## 0.37.14
- Clearer status colours: green #6CCB7E (normal / up / fine), gold #F5C542 (important / slow / busy) and red #EF4D4D (alert / down / problem). The old pastel green and yellow were too alike. Used everywhere status colours appear: notification cards and the Normal / Important / Alert buttons, service dots, Komodo, Proxmox, downloads, UniFi (and its 1 Gbps / 100 Mbps ports), Seerr, the run box and the Komodo log's errors and warnings.

## 0.37.13
- Notification cards can go on any monitor: Settings > Notifications > Monitor lists the main monitor, each monitor by number (with its size), and the one the mouse is on. If the chosen monitor isn't connected, they show on the main one.

## 0.37.12
- Notifications sent to vShell take far more kinds of webhook:
  - bodies sent in pieces (Transfer-Encoding: chunked, which many apps use instead of Content-Length) - these used to arrive empty and were refused with "give a title or a message";
  - PUT and PATCH as well as POST, and senders that wait for "100 Continue";
  - form bodies (title=...&message=...) and plain text bodies (first line = title, the rest = message);
  - JSON with body / text / content / description instead of message, and a priority (Gotify 0 - 10, ntfy 1 - 5 or words like high / urgent) turned into Important / Alert.

## 0.37.11
- Notifications: "Wait before saying it's down (seconds)" for Service status, Proxmox and Komodo (0 - 600, 0 = straight away as before). A down notification is held for that long and only shown if it's still down; if it comes back first, nothing is said - no "down" and no "back" - so a quick restart or a blip doesn't notify. Covers services going down; Proxmox nodes going offline, VMs / containers stopping and Proxmox not being reachable; Komodo stacks / deployments / containers stopping or turning unhealthy and servers not being reachable; and UniFi devices (gateway, switches, access points) going offline - e.g. a firmware update's restart says nothing.
- Seerr posters fill the width: in a widget you've sized, as many posters go across as fit and they're sized so each row fills it (up to about 1.7 × their usual size; past that the grid is centred). Before, posters were a fixed size, at most five across, and centred - so a wider widget only added empty space. A widget that sizes itself still shows five across; the popup fills its width too.

## 0.37.10
- Graph warnings are on by default, with an automatic threshold: 70 % of the graph's range. For CPU, memory, GPU, disk, network and downloads that's 70 %; for a HWiNFO sensor it's 70 % of its "empty at" - "full at" range (e.g. 0 - 2000 → 1400), and it follows the range if you change it. Battery starts at "below 20 %".
  - Type your own threshold and it's kept as yours. Empty the box to go back to automatic; the line under it says which one is in use.
  - Existing modules: any that never had the warning turned on get it, with the automatic threshold. Ones you had already set up keep your threshold. Turning the warning off afterwards stays off.
  - Home Assistant entities added from now on start the same way: warning on, threshold 70 % of the entity's graph range ("Graph: empty at" - "full at"), following the range while automatic. In the entity's edit form, type a threshold to set your own or empty it to go back to automatic. Entities you already have are left as they are. An automatic threshold only colours an entity that has a graph (without one there's no range for it to come from).

## 0.37.9
- Graph warnings fade more gradually. With "Change colour past threshold" on, the colour starts changing very slightly 20 % of the scale before the threshold, gets stronger as it nears it, and becomes the solid warning colour 10 % past it - e.g. threshold 65: a hint from 45 %, mostly there by 65 %, solid from 75 %. Before, it went from start to full in a short straight ramp ending at the threshold, which looked like a hard line on small graphs. Meters, bars, lines, rings and tiles all use it; "warn below" (battery) runs the other way.

## 0.37.8
- Climate control (and Bubble Card buttons in the Home Assistant module): a widget that came up wider when vShell started now goes back to its proper width. A zone slider's fill was sized from the slider's own width, which held the slider - and the widget - at whatever width it first had; locking / unlocking only fixed it because that rebuilds the widget. The fill now scales instead, so it never sets the width.
- UniFi Network: a device you've opened to show its ports (e.g. the router) stays open - it's saved with the module, so the ports show again after closing the popup or restarting vShell. Click it again to close it.
- UniFi Network: ports are coloured by their link speed, roughly as UniFi does - 10 Gbps+ purple, 2.5 / 5 Gbps blue, 1 Gbps green, 100 Mbps amber, 10 Mbps orange - with a small key under them for the speeds in use. Ports that are down stay grey; hover one for its exact speed.
- UniFi Network: ports are listed in number order (1, 2, 3 … 10, 11) - they came in UniFi's own order, which put 10 and 11 after 1.

## 0.37.7
- Graph warnings: the gradient into the warning colour is wider, so it shows clearly. With "Change colour past threshold" ticked the graph now starts blending 20 % of its scale before the threshold (it was 10 %, easy to miss) - e.g. threshold 70 blends from 50 % and is the solid warning colour from 70 %. The settings stay as they are: Graph colour, Warning colour, Change colour past threshold, Threshold.
- Ring graphs blend along the ring like a meter: the graph colour from the top, blending through the fade, the warning colour past the threshold. Before, the whole ring took one colour - so a drive at 73 % with the threshold at 70 was entirely the warning colour. The number in the middle still turns the warning colour past the threshold.

## 0.37.6
- Widgets that slide away at the screen edge: dragging one away from its edge now turns sliding away off (turn it on again from the right-click menu once it's snapped where you want it). Before, it stayed on and could slide the wrong way after being snapped to a different edge - e.g. moved from the bottom to the top, it still tried to slide down. Turning it off puts the widget straight back: in place, fully visible and on its usual layer.
- UniFi Network: the "Show the internet speed" toggle is gone from the widget (it stays for the bar). The internet speed in the line at the bottom of the widget and popup is now coloured like the Network module: download in the Main Accent, upload in the Secondary Accent - and it shows on the Clients list too. On the bar, the download speed is the Main Accent as well (it was green).
- UniFi Network (Show = All): the widget stays on the tab you picked when you change its settings (e.g. Rows to show) - it used to jump back to Devices.

## 0.37.5
- Notifications: the Normal / Important / Alert choice for each kind of notification now uses the same joined buttons as Corner (Bottom right / Bottom left …), each in its notification colour: green Normal, yellow Important, red Alert (filled when chosen, coloured text when not).
- System notifications: "A drive has less free space than (%)" is now "Storage fuller than (%)", the same as the Proxmox storage one. An old setting carries over (10 % free becomes 90 % full).
- Graph warnings are simpler, for every module with a graph (CPU, memory, GPU, disk, network, battery, downloads, HWiNFO sensors and Home Assistant entity graphs):
  - In the Graph section, under Graph colour: Warning colour (the Secondary Accent unless you pick another, like any other colour), then "Change colour past threshold" and Threshold. The separate Warning section is gone.
  - The fade is always on: the graph blends into the warning colour over the last 10 % of the scale before the threshold, and is solid past it. The "Fade into it over" setting and its explanation are removed.
  - Home Assistant entities (double-click one in Choose entities…) have the same: a warning colour, the toggle and the threshold.
- UniFi Network:
  - The search box looks like the run box (rounded, with a search icon) and works every time you click it - before, it stopped taking typing after you clicked away from the widget. Esc clears it, Esc again lets go of the keyboard.
  - Show: All (Devices and Clients tabs), Devices or Clients (just that list, no tabs). New modules start on All.
  - "Show the internet speed" now works in a widget too: Internet ↓ / ↑ along the top.
- UniFi Protect:
  - Adding it to a widget that sizes itself makes the widget 600 × 400 to start with, instead of growing to the cameras' full size.
  - The big camera view no longer stays black: it shows the grid's picture straight away, and falls back to a normal snapshot when the camera won't give a high-quality one.
  - The lights are off by default (turn them on in the module's settings).
- Climate control:
  - The zones are two to a row.
  - Zones show 0% - 100% instead of 0 / 10 - 10 / 10.
  - Clicking a zone's fan icon turns it back on again: an off zone is now turned on (fan.turn_on with the opening) rather than only given a new percentage, which some integrations ignore while it's off.

## 0.37.4
- Notifications page: pick whether each sort of notification shows as Normal (green), Important (yellow) or Alert (red), with three small boxes, each a mini notification card in that colour. The picked one has an accent frame. Covered:
  - Downloads: finished, failed;
  - Komodo: problems, updates available, good news;
  - Seerr: new requests, available requests;
  - Proxmox: problems, good news;
  - Service status: down, back up;
  - System warnings;
  - Jellyfin playback;
  - Weather rain.
- The defaults are what they were before: problems are Alerts, everything else is Normal. Picking the default again clears the choice.
- New module: Climate control (Smart home & more), for a Home Assistant climate entity, drawn to Bubble Card's own styles (github.com/Clooos/bubble-card):
  - a 50 px fully rounded row with the mode's colour behind it while it's on (blue cool, orange heat, yellow dry, teal fan, purple auto);
  - a 38 px icon circle (click it to turn the air conditioner on / off), the name over the state;
  - − / target / + together in a pill on the right (or scroll), sent 0.7 s after the last click, as Bubble does;
  - Bubble sub-buttons (36 px, rounded) for the mode, fan speed and preset, only those the entity has;
  - a Bubble slider for each zone, using fan entities: 0 (off) to 10. The fill grows from the left in the mode's colour. Click or drag along it, scroll for one step, click its icon for off / back on;
  - on a bar: the mode icon, the room temperature and the target. Click for the card, scroll to change the temperature;
  - set up under the module's settings: the climate entity, and Zones… (add the fan entities, with your own names).
- Clock: click it for a month calendar like Windows' own:
  - today's date at the top (click it to come back to today);
  - the month with ‹ › (or scroll);
  - today in an accent circle, the day you click ringed, a tooltip with how many days away it is;
  - a "Date and time settings" button.
  The Clock module's settings let you turn it off, choose which day weeks start on (Windows' setting, Monday or Sunday) and show week numbers.
- Komodo: "Show log" in the right-click menu of a stack, deployment, container or stack service (popup and widget), and in the run box (an item's actions, or `!ko logs name`). It opens a log window in your theme:
  - last 50 / 100 / 500 / 1000 lines, a service picker for stacks;
  - Follow (refreshes every 3 s and keeps to the newest), timestamps on / off, a filter (Ctrl+F), Refresh (F5), Copy;
  - errors in red, warnings in yellow. It only reads the log.
- Home Assistant module: a new "Bubble Card buttons" option. Each entity becomes a Bubble button:
  - Switch: click to toggle. When on, the accent fills the card; a light uses its own colour.
  - Slider: drag along it for light brightness, fan speed, cover position, a number / input number or media volume. The icon toggles it, and scrolling moves 5 %.
  - State: shows it.
  - Auto (the default for each entity) picks the type from the entity's domain; you can change it per entity under Choose entities… (double-click one).
  - In a widget: one per row (List) or side by side with a width setting (Row). On a bar: side by side, as tall as the bar.
- Home Assistant entities now keep their attributes (used by the climate module and the Bubble sliders).
- Graph warnings can fade: a new "Fade into it over" setting next to the warning threshold, for CPU, memory, GPU, disk, network, battery, downloads, HWiNFO sensors and Home Assistant entity graphs. With a fade, the graph is coloured by position: the graph colour up to the fade, blending into the Secondary Accent through it, and solid past the threshold. For example, threshold 70 with fade 15 starts blending at 55 %.
  - Meters blend along their length.
  - Bar and line graphs blend from the bottom up, so only the peaks past the threshold turn the Secondary Accent.
  - Rings take the blended colour of their value.
  - 0 (the default) keeps the old behaviour: the whole graph switches colour once the value is past the threshold.
- UniFi (new, on the Integrations page): the console's address, a Network API key and / or a Protect API key (both stored encrypted), "ignore the certificate" (on by default for the console's own certificate), the site, how often to ask, and Test.
- UniFi Network module (read-only):
  - On a bar: a status dot, the internet speed through the gateway (↓ / ↑) and the number of clients; the tooltip lists the devices. Click for the popup.
  - Devices: the gateway, switches and access points with CPU, memory, uptime and clients. Click one for its ports (⚡ = PoE).
  - Clients: everything connected, with a search box, the device it's on and when it connected. NEW marks a client vShell hasn't seen before.
  - Right-click a row: copy its IP, MAC or name, or open UniFi Network. The gateway being online stands in for the internet status.
  - Notifications (Settings > Notifications, each Normal / Important / Alert): a device going offline and coming back, a new client, an access point with more than N clients.
  - Run box: !u lists offline devices and new clients; !u name finds a device or client, Enter copies its IP.
- UniFi Protect module:
  - On a bar: the cameras online (red when one is down) and the last event for an hour, e.g. "Person · Driveway · 2m".
  - The popup and widget: the cameras' snapshots in a grid (1 - 6 columns, refreshing every few seconds while on screen, only the cameras you list if you want), the lights (click: on / back to their own mode) and the events seen since vShell started, with thumbnails.
  - Click a camera for the big view: a high-quality snapshot every 2 seconds, Open in Protect, Play in VLC (the live RTSPS stream; without VLC the link is copied), Copy stream link, and Home / presets 1 - 4 on PTZ cameras.
  - Notifications for doorbell rings and person, vehicle, package, animal and motion detections, each with its own Normal / Important / Alert and a camera filter. They come with a snapshot, shown on the card and in the notification history.
  - Protect's API has no event history or event thumbnails, so events are only the ones vShell saw while running, and the picture is the camera's snapshot at that moment. The chime isn't included (the API doesn't offer it).

## 0.37.3
- Settings: click the number next to any slider to type a value. Enter or clicking away applies it, Esc cancels. Values outside the slider's range are brought into it.
- Slide away at the screen edge: a new "Accent glow at the screen edge while it is away" option (on by default). A thin glow in the widget's accent colour runs along the screen edge where the widget is waiting, showing where to push the mouse. It isn't shown over full-screen apps, and clicks go through it.
- Fixed: a widget that had slid away no longer disappears from its live preview in Settings.

## 0.37.2
- Top programs, fixed width: only the program names are shortened (with "…"); the CPU % / memory is always shown in full. On one line the room is shared between the names; in a list each line gets the full width.
- Service status > Choose what to watch: Copy and Paste. Copy takes the picked services (Ctrl / Shift+click for several) or the whole list; Paste adds them to another Service status module, skipping ones already there. Works through the clipboard, so also after a restart.
- List editors (services and others) can pick several rows with Ctrl / Shift.
- Bar settings: the layout's description now sits under the Layout choice, above "Follow the bar style".
- Widget settings rearranged:
  - "Arrangement" is now "Layout", and "Follow the widget style" moved into it (below Density);
  - "Border" is now "Outline" ("Show an outline");
  - the Density note and the "Move to top-left of its monitor" button are gone;
  - "Slide away at the screen edge" is the last section.

## 0.37.1
- Top programs module: a "Fixed width" option with a Width slider (40 to 600). The module keeps that width whatever the program names are, cutting long ones off with "…", so the modules next to it no longer move about. Off by default (fits the text, as before).

## 0.37.0
- Widgets can slide away at the screen edge they're snapped to, and come back when you push the mouse against that edge. Off by default.
- Turn it on from the widget's right-click menu ("Slide away at the … edge"). A widget snapped into a corner asks which way to slide (e.g. Up or Left).
- Settings > the widget > "Slide away at the screen edge": on / off, which way (in a corner), animation (Slide, Fade, Slide and fade), speed, bounce when it comes back (None, Soft, Springy) and how long to wait before sliding away.
- While it's out it sits in front of your windows (even a widget that normally lives on the desktop). It stays out while you drag, resize or type in it, or while its menu or one of its popups is open.
- Only the widget's content moves, clipped to its own window, so it never appears on a neighbouring monitor. Clicks go through to whatever is under it while it's hidden.

## 0.36.1
- Active window line: Comet, Dot and Windows 11 taken out (too close to the others). A taskbar set to one of them goes back to Stretch.
- Rearranging icons now slides them into place, using the same animation as the taskbar's buttons (250 ms):
  - taskbar pins when you drag one (previously the bar rebuilt and they jumped);
  - launcher shortcuts and groups;
  - system tray icons.
- Launcher and System tray settings have an "Icons slide to their new place when you rearrange them" tick (on by default).

## 0.36.0
- Taskbar > Active window line animation: six new styles - Shrink and grow, Hop, Comet, Liquid, Dot and Windows 11 (the old line shrinks back to the short running line while the new one grows out of its short line).
- Taskbar > Button animations (new section, on by default):
  - buttons grow in when a window opens and shrink away when it closes, with the other buttons sliding over;
  - buttons slide to their new place when the order changes (dragging a pin, a window moving to another monitor);
  - an app that wants attention can pulse its button's background slowly (Pulse, the default) or stay one colour (Steady).
- The active line follows its button while buttons slide about, instead of starting its move over.

## 0.35.2
- Add a module: the Start menu tile now shows the vShell v mark (in your accent, secondary accent and text colours) instead of a generic menu icon.

## 0.35.1

**Add a module: tiles with drawn previews**
- Bars: the modules are now tiles, three to a row - a small picture of the module on a piece of bar (in your bar's colours), its name and description under it. Click a tile to add it.
- Widgets: the cards stay, with drawn pictures too.
- The previews are drawn pictures with sample values (a CPU graph, 2:05 PM, a forecast, Proxmox meters, Seerr posters …) in your theme's colours, instead of the real modules - quicker to open, nothing running in the background.

## 0.35.0

**Adding modules, with previews**
- A bar's or widget's Modules page no longer opens with a module selected: it opens on **Add a module**.
  - **Bars**: a row per module - its name and description, the module itself on a strip of this bar (your bar's look and height), and Add.
  - **Widgets**: a card per module - the module the way it looks in this widget, its name and description under it.
  - The previews are the real modules, live (graphs move); when you already have one of that kind, the preview uses your settings (your weather place, your Proxmox nodes …). The taskbar and tray need a real bar, so theirs are samples (the taskbar with the icons of your open windows). Modules that need a connection you haven't set up say which one.
  - Search and categories (All, System, Shell, Info, Smart home & more) at the top. Bars with zones: **Add to Left / Centre / Right**.
- Adding a module opens its settings. It goes after the module you were on (or at the end).
- Click a module in the strip / zones for its settings; **+ Add modules** at the top (or clicking the open module again) goes back to adding.

**New bars and widgets start empty**
- "+ Add bar" no longer starts with a taskbar, "+ Add widget" no longer with a clock: they open on Add a module. (A first install's layout is unchanged.)

## 0.34.2

- Taskbar module settings: the "Active window line" section is now called "Active window line animation".

## 0.34.1

**New defaults for the moving taskbar line**
- Stretch, 400 ms, Soft bounce, with the active button's background moving along - for new taskbars and new installs.
- Existing taskbars are switched to these once when 0.34.1 first starts; anything you change afterwards stays.

## 0.34.0

**The taskbar's active line moves**
- When you switch windows, the active-window line under the taskbar buttons now moves to the new button instead of jumping. Taskbar module settings > Active window line:
  - **Moving to another window**: Off (jumps, as before), Slide (glides there), Stretch (the front end runs ahead, the back end catches up - a stretchy line on long moves) or Fade (fades out, fades in under the new button).
  - **Speed** in milliseconds (60-1000, 220 by default).
  - **Bounce**: None, Soft (goes a little past and settles back) or Springy (a couple of small wobbles).
  - **The active button's background moves along with the line** (on / off).
- It follows the button when buttons come and go or change width, shrinks away when no window is active (the desktop is clicked) and grows back under the next one. The short lines under the other running apps stay as they are.
- New taskbars (and new installs) start with Slide; existing taskbars stay Off until you pick one.

## 0.33.1

**Widget corners**
- A widget with square corners (radius 0), a thick outline and its shadow on showed dark marks in the outline's corners: the shadow behind it was cut out with rounded corners. It now has the outline's shape.
- With a big corner radius the header's icon (and the collapse arrow) moved into the curve. They now move in as far as the corner needs.

## 0.33.0

**Three kinds of notification: Normal, Important, Alert**
- **Normal** (green) goes after its time. **Important** (yellow) stays until you close it. **Alert** (red) is for errors, something down, too hot, low battery / disk - it stays until closed too. Settings > Notifications has "Important ones stay" and "Alerts stay" (both on), and a test button for each kind.
- Every notification shows its kind the same way everywhere: a coloured bar at the left of each line on the cards and in the history, the source icon in that colour, and the title in yellow / red for important ones and alerts. The bell's badge takes the colour of the most serious one not read yet.
- With Cards "By source", important ones and alerts each get a card of their own.
- Home Assistant rules have a Kind (Normal / Important / Alert) instead of the Important tick (rules marked important stay important). Notifications sent to vShell take `"level": "normal" / "important" / "alert"` (`"important": true` still works); the Home Assistant example uses level.
- Failed downloads, Komodo / Proxmox / services going down, high temperatures, low disk / memory / battery are Alerts.

**The same colours everywhere**
- Green / yellow / red now come from one place for notifications, Komodo, Proxmox, downloads, service dots, Seerr and the run box (the run box's Seerr results used a different green and the accent colour for errors).

**Buttons**
- The small buttons in modules and popups (Open Komodo, Approve / Decline, Request, Mark all read, Pause all, the run box's Seerr buttons …) are drawn one way now, in the widget style: their corners follow the widget style's rounding, with a thin outline when widgets have one. Seerr's tabs and filters follow the same rounding.

**Times**
- Times are written the same way everywhere outside the Clock module: "just now", "5 min ago", "3 hr ago", then "Yesterday 2:05 PM", "Mon 2:05 PM", "12 Mar 2:05 PM". 12-hour by default; Settings > Appearance > Font and times > Times switches to 24-hour. (Notifications, Seerr, Komodo procedures, Notes' "edited" and log times, service "checked", weather "updated", uptime "started", update "checked".) Clock modules keep their own setting.

## 0.32.2

**Important notifications get their own card**
- With Cards "By source", an important notification (one that stays until closed) now opens a card of its own instead of joining the source's card - so the ordinary one next to it still goes after its time.
- Every line on a card now goes after its own time (counted from when it arrived), not the whole card at once. With "One card" the ordinary lines go and the important ones stay.

## 0.32.1

**New installs: a new default bottom bar**
- The bottom bar a first start creates uses zones: the Start menu on the left, the taskbar in the centre, and the notification bell, tray and clock on the right. Existing settings are not changed.

**Fixes and tidying**
- The notification history window cut off its Clear button.
- Modules list: Notifications moved to Shell, Service status to Info.

## 0.32.0

**Notifications page**
- New **Settings > Notifications** page (under Integrations). The Download, Komodo and Seerr notification settings moved there from the Integrations page, which now only holds the connections. "Notices" are called notifications everywhere now.
- **Cards**: the corner (any of the four), the monitor (the main one, or the one the mouse is on), how many seconds they stay, a card per source or one card for everything, the Windows notification sound. With **Keep important ones** on, failures and down / too-hot alerts stay until you close them. **Show a test notification** / **Show an important one** buttons.
- **Quiet times**: hold notifications while a full-screen game or video is in front, during quiet hours (for example 23:00 to 07:00), and while Windows says not to disturb. Held ones show when it ends (the newest six, plus "+ N more").

**Notification history**
- Every notification is kept (100 by default, Settings > Notifications > History), also after vShell restarts.
- New **Notifications** module: on a bar a bell with the number not read yet (hover for the latest); click it for the history, grouped by day. In a widget the list itself. Click one to open what it is about, right-click to remove it or copy its text; Mark all read and Clear. A module can list one source only.

**New notification sources** (all off until you turn them on)
- **Home Assistant rules**: when an entity becomes a state, goes above / below a number or changes - optionally only after it stays that way for some minutes - show your message ({name}, {state}, {unit}, {entity}). Clicking it can open Home Assistant or toggle the entity. Pick the entity from Home Assistant's list.
- **Notifications sent to vShell**: Home Assistant (or any script) can send a notification to `http://this-pc:8765/notify` with a token. The settings show a ready-made `rest_command` for configuration.yaml.
- **Proxmox**: a node goes offline / comes back, Proxmox can't be reached, a VM or container stops or starts (not the ones you started / stopped from vShell), storage fuller than a set %.
- **Service status**: a service in any Service status module goes down (after two failed checks) and comes back up.
- **System**: CPU / GPU hotter than a set temperature for a set time, a drive low on free space, memory nearly full, laptop battery low - each at most once every few minutes.
- **Jellyfin**: someone starts (or stops) playing something - leaving out this PC and the users / devices you list.
- **Weather**: rain expected within the hour while it's dry now (Open-Meteo, for the place of your first Weather module).

## 0.31.4

**Font boxes show the font again**
- The font drop-downs (Appearance > Font family, Notes > Font) looked empty: Settings' own drop-down look had no room for typed text, so the chosen font's name never showed. It now shows the name, and you can click into it and type.

## 0.31.3

**Font drop-downs**
- Appearance > Font family is now a drop-down of every installed font, each shown in its own typeface. Type to jump to a font, or type any name. Picking one applies it straight away.
- The Notes module's Font is a drop-down too: Theme, Mono and Handwritten as before, followed by every installed font.

## 0.31.2

**Seerr widget keeps one size**
- Switching tabs or filters in a Seerr widget no longer changes its size, so widgets snapped to it don't get pushed around. The widget takes the room its biggest view needs, and never shrinks below the biggest it has been. Changing the module's settings (how many, posters, tabs / filters, text size) works its size out again.

## 0.31.1

**Seerr module fixes**
- Popups move back into place against their module whenever they change size (another tab, more or fewer rows). Before, a popup above a bottom bar could end up overlapping the bar or floating away from it. This applies to every bar popup.
- "Popup opens on" and "Requests to show" now work: the bar's popup opens on the chosen view and filter every time.
- In a widget, changing "View" or "Requests to show" in Settings switches the widget straight away, even with the tabs / filters shown (it used to keep the tab clicked last).

## 0.31.0

**Seerr module (bar and widget)**
- New module **Seerr requests** (gallery: Smart home & more). Uses the Seerr connection on the Integrations page.
- **On a bar**: the Seerr icon with a badge for the requests waiting for approval, plus how many are processing (and, if you like, available). Hover for the latest requests; click for the popup.
- **Popup / widget** with tabs:
  - **Requests**: poster, title and year, movie or TV with the seasons, who asked and when, and the status in colour (pending, approved, processing, partly available, available, declined). Filters: All, Pending, Processing, Available, Mine. Pending ones get **Approve** and **Decline** buttons when the API key is allowed to approve (Decline asks first). Click a request to open it in Seerr; right-click for Approve / Decline / Open / Copy the title.
  - **Recently added**: posters of what arrived in the library lately, with when.
  - **Trending** and **Upcoming**: posters with a **Request** button. Shows open a menu to pick the seasons (all, the missing ones, the latest, or one season).
- Settings per module: the view it opens on, tabs and filters on or off (widget), how many, posters on / off, the Seerr button, and the bar's badge / numbers.

**Seerr notices**
- A notice when a new request is waiting for approval (if the API key can approve), and when a request becomes available: yours, everyone's or off (Integrations > Seerr > Seerr notices). Click a notice to open the title in Seerr. They work without a Seerr module too.

**Run box**
- `!s` on its own lists the requests waiting for approval: Enter on one, then Enter on **Approve** (Decline needs Enter twice). With nothing waiting it shows the latest requests.
- `!s recent` lists what was added lately. `!s title` still searches and requests.

## 0.30.2

**Taskbar icons for Store apps (WhatsApp and similar)**
- Newer Store apps (WhatsApp, new Teams, new Outlook, Windows Terminal ...) open ordinary windows, so vShell didn't know they were Store apps and fell back to their program inside C:\Program Files\WindowsApps - which Windows doesn't let other programs read icons from (or start directly). vShell now asks the running program for its Store app id: icon, launching, grouping and "Pin to taskbar" use the Store app.
- Pins made that way by earlier versions are repaired by themselves (from the running app, or from the Start menu's list of Store apps).
- An icon Windows fails to give once is asked for again after 20 s (it used to stay blank until vShell restarted); a Store app's icon also falls back to the one the Start menu has.
- Store app icons keep their transparency (some showed a dark box or looked blank).

## 0.30.1

**Right-click menus close when you click away**
- The menus of bars, widgets and modules now close when you click anywhere else: another program, the desktop, Windows' taskbar, or another vShell bar / widget. Esc and another window coming in front (Alt+Tab) close them too. (vShell's windows never take the focus, so Windows didn't tell the menu it should go.)

**Start menu over a full-screen app**
- With a full-screen app (game, video) in front, the bars stay hidden behind it as before. Press the Windows key: the Start menu opens and the bar it belongs to comes over the full-screen app with it. Click back into the app (the menu closes): the bar goes behind it again. Works for any popup opened from a bar while a full-screen app is up.

## 0.30.0

**Import pins from the Windows taskbar**
- Taskbar module settings and Application launcher settings: **Import pins from the Windows taskbar…** lists what Windows has pinned (desktop apps and Store apps such as Calculator or Terminal), in Windows' order, with icons and tick boxes. Add them after the ones there now, or replace those (the launcher keeps its groups). Ones already there are skipped.
- The shortcuts are copied into vShell's folder, so unpinning them in Windows later doesn't break the vShell pins. Running windows of an imported app group onto its pin.

**Drag to reorder and drop to add**
- **Taskbar**: drag a pin along the bar to move it (a thin accent line shows where it lands). Drop programs or shortcuts from File Explorer onto the taskbar to pin them where you drop them.
- **Application launcher** (bar or widget): drag a shortcut or group to move it; files dropped on it go where you drop them (before: always at the end).
- **System tray**: drag an icon along the bar to move it; the order is kept (and the hidden icons' popup follows it). "Back to Windows' order" in the tray settings.
- Ctrl+drag still moves the whole module, as before.

**Background blur per bar and widget**
- Each bar's and widget's Look settings have **Background blur**: As the theme / Off / Blur / Frosted. Saved with bar styles, widget styles and themes (a theme saved now remembers each layout's / the widgets' blur).

**Proxmox VE**: "Show each running guest's uptime" - a column with how long each running guest has been up.

**Komodo**: "Show the "Open Komodo" button" - turn off the Komodo button at the top of the list.

**Settings**: SABnzbd: "Drag nzb files onto downloads module or run box to add files for download".

## 0.29.3

**Settings: Integrations page - more (i) help**
- qBittorrent: the text under the heading is now "Drag torrents, or add magnet links into the run module to add downloads into qBittorrent."; the login hints (Web UI, leave the user name empty when the server bypasses the login) are an (i) after "User name"; an (i) after "Run box keyword": "Use !qb in run box to see current torrents."
- SABnzbd: where to find the API key is an (i) after "API key".
- Komodo: no text under the heading; how to make an API key (and a service user) is an (i) after "API key"; an (i) after "Run box keyword" explains !ko, !ko name, the actions and the Enter-twice confirm.

## 0.29.2

**Settings: Integrations page - more (i) help**
- Proxmox VE: the how-to (API token, Privilege Separation, roles) moved into an (i) after "Token secret".
- Seerr: the run box use (!s Movie Title ...) is an (i) after "Run box keyword"; where to find the API key is an (i) after "API key".
- Jellyfin: the text under the heading is now "Gives the Now Playing module a progress bar and allows you to seek from a bar or widget."; where to find the API key is an (i) after "API key".

## 0.29.1

**Settings: Integrations page**
- The page's description is now "Connections to Local and Web services".
- Home Assistant: the how-to text moved into a small (i) after "Access token" - hover it and the explanation pops up (it stays while the mouse is on the card).

## 0.29.0

**New module: Komodo (Docker)** - gallery: Smart home & more > Komodo
- **On a bar**: a dot (green all running, amber something busy, red a problem), `12/14 running`, `⚠ 1` problems, `⬆ 2` image updates, servers that can't be reached. Tooltip lists what isn't right. Click it for the list in a popup (in your widget style).
- **In a widget**: the list itself, grouped by server (server dot red when Komodo can't reach it), problems first.
- **Show**: Managed (Komodo's stacks and deployments), Containers (every Docker container on your servers) or Both (plus the containers Komodo doesn't manage). Which: All or only Problems (not running, unhealthy, or an update waiting). One server or all; how many per server.
- **Click an item** for its actions: Start, Stop, Restart, Pause / Unpause, Redeploy (or "Pull and redeploy (update)" when a newer image is out), Pull, Destroy, Open in Komodo. Stop / Restart / Redeploy / Destroy ask first. The row shows "restarting…" / "deploying…" until Komodo reports the new state.
- **Stacks open** (arrow) to their services with each container's state; a service has its own Start / Stop / Restart / Pause / Redeploy / Pull.
- **Procedures** listed with when they last ran, and a **Run** button (asks first).

**Integrations page: Komodo**
- Address, API key (K-…), secret (S-…, stored encrypted), self-signed certificate, run box keyword, Test connection ("Connected - Komodo x.y: 2 servers, 9 stacks, …"). Works with Komodo 1.x and 2.x.
- Notices (bottom-right, above the downloads notice if both are up): something stops by itself or turns unhealthy (and when it's back), a server can't be reached (and is back), a newer image is available, a procedure fails, a procedure you ran from vShell finishes. Click one to open it in Komodo. Things you stop from vShell don't give a notice.

**Run box**
- `!ko media` - matching stacks, deployments, containers and procedures with their state. Enter on one shows its actions; actions that interrupt need Enter twice. `!ko` alone: everything that isn't right, plus your procedures.

## 0.28.1

**Drop .nzb / .torrent files**
- Drag **.nzb** files from File Explorer onto a **run box** (bar or widget): they're uploaded to SABnzbd. **.torrent** files go to qBittorrent. Several at once work; other files are ignored.
- While you drag over the run box it lights up and says what will happen; afterwards the drop-down shows "✓ name added to SABnzbd" (or why not) for a few seconds.
- The **Downloads module** (bar or widget) takes dropped files too; the result shows in the downloads notice.

## 0.28.0

**New module: Downloads (qBittorrent / SABnzbd)** - gallery: Smart home & more > Downloads
- **On a bar**: `↓ 12.4 MB/s  ↑ 1.1 MB/s   3 · 47%` with a Line / Bars / Meter graph of the speed (upload in the second colour), a **Ring** showing how far the whole queue is, or a **Tile** (big speed, sparkline). Show both clients or one. Options: count and overall progress, upload speed, two lines, hide when idle. Tooltip: each client's speeds, queue, paused / alternative speed limits, free space.
- **Click it**: a popup (in your widget style) with each client and its downloads.
- **In a widget**: the list, with a speed graph along the top (or set the widget to show the bar look instead).
- **The list**: each client's speeds, paused state, free disk space; buttons to pause / resume everything, turn qBittorrent's alternative speed limits on / off and open the web page. Each download: name, progress bar (green finished, amber stalled, grey paused, red error), state, done / size, time left, speed. **Click a download to pause or resume it.** Show Active, Unfinished or All, and how many per client (+ N more).

**Integrations page**
- **qBittorrent**: Web UI address, user name, password (stored encrypted; leave the user empty if the server lets this PC in without a login), self-signed certificate, run box keyword, Test connection. Works with qBittorrent 4 and 5.
- **SABnzbd**: address, API key (stored encrypted), self-signed certificate, run box keyword, Test connection.
- **Download notices**: a notice bottom-right when a download finishes (optionally only some categories) or fails (e.g. SABnzbd "Out of retention"). Several at once collect in one notice; click a line to open that client. Works without a Downloads module too (checked every 30 s).

**Run box**
- `!qb ubuntu` / `!sab movie`: the matching downloads, live, with progress and state; Enter or click pauses / resumes one. `!qb` / `!sab` alone: everything.
- Paste a **magnet link** or a **.torrent** address: "Add to qBittorrent". An **.nzb** address: "Add to SABnzbd". (Or "Open it instead".)

## 0.27.0

**Now playing: progress bar**
- A progress bar showing where the track is, moving smoothly while it plays. Choose **Under text** (a thin bar under the title), **Along the bottom** (the module's bottom edge) or **Off**. Optionally the time next to the buttons (`1:23 / 4:56`).
- Click or drag on the bar to jump to that point, when the player allows it. Hover shows the time you'd jump to.
- The bigger card (click the art or title) has a bigger bar with the position and length under it.
- Works with any player that tells Windows where it is (Spotify, browsers, Windows Media Player, ...).

**Jellyfin integration (Integrations page)**
- Address, API key (stored encrypted), self-signed certificate, this PC's device name (empty = the computer's name), Test connection.
- The Jellyfin desktop app tells Windows the title but not where it is: vShell now asks your Jellyfin server, matches the session by title (this PC's first), and shows its progress. Clicking the bar jumps in Jellyfin.
- When Windows shows nothing but this PC's Jellyfin app is playing, Now playing shows it from Jellyfin (title, artist, album, cover) and the buttons control it through the server.

## 0.26.2

**Run box**
- On a bar, the drop-down now opens 10 px below the bar's bottom edge (top bar) or 10 px above its top edge (bottom bar), instead of just past the field.
- In a widget it sits a little further from the field (12 px), so neither it nor its shadow overlaps the field.

## 0.26.1

**Run box**
- The box the keys open in the middle of the screen now closes when you click anywhere else, Alt+Tab or another window comes in front - you no longer have to click into it first. It also tries harder to take the keyboard straight away.
- Up / Down follow the list as shown: Down moves the highlight down, Up moves it up (in an empty box the list is your history, newest first).
- The centred box and the drop-down under a bar's / widget's run box now look like a widget of the current widget style: the theme background, corners, outline, shadow and padding.

## 0.26.0

**Run box: type straight into it**
- The run box on a bar or in a widget is now a real text field showing just the search icon. Click it and type - the suggestions and results drop down under it (above it on a bottom bar). Enter runs, Esc or clicking elsewhere clears it and gives the keyboard back.
- The keys (Alt+Space, Ctrl+Alt+Space ...) now open the run box **in the middle of the main monitor**, ready to type, wherever the run box module is.
- **Custom search engine**: "Plain search with" has a new **Custom** choice with an address box - e.g. `https://searx.example.com/search?q={q}` for SearXNG. The suggestion shows "Search searx.example.com for ...". Without `{q}` the search is added to the end.

## 0.25.1

**Run box**
- **Alt+Space** can now open the run box (like PowerToys Run). While it is set, Alt+Space no longer opens Windows' little window menu (Restore / Move / Size / Close) - right-click a window's title bar for that menu instead.

## 0.25.0

**New module: Run box (bars and widgets)**
- A search field on a bar or in a widget (gallery: Shell > Run box). Click it - or press **Ctrl+Alt+Space** from anywhere (or Ctrl+Shift+Space / Alt+Shift+Space / off, per run box) - and it opens, ready to type:
  - **!bangs**: `!g dog pictures` searches Google; also `!i` Google Images, `!yt` YouTube, `!w` Wikipedia, `!gh` GitHub, `!m` Google Maps, `!ddg` DuckDuckGo. A bang on its own (`!yt`) opens the site. Edit the list in the module's settings (keyword | name | address with {q}). A bang that isn't in the list goes to DuckDuckGo, which knows thousands (`!amazon`, `!imdb` ...) - can be switched off.
  - **Apps**: type part of a name - matching Start menu apps are suggested with their icons; Enter starts the top one.
  - **Paths, folders and addresses**: `C:\Temp`, `%appdata%`, `~\Downloads`, `github.com`, `https://...`, `ms-settings:sound`, `shell:startup`.
  - **One word** (`notepad`, `regedit`): runs it like Windows' Run box; if Windows doesn't know it, it's searched on the web.
  - **`>ipconfig /all`**: runs the command in a terminal window.
  - **`=12*7.5`**: shows the answer as you type (+ - * / % ^, brackets, pi, sqrt, sin, cos, tan, log, ln, abs, round); Enter copies it.
  - Anything else: a web search with Google, DuckDuckGo or Bing (your choice), or none.
- Suggestions under the box: what Enter will do, matching apps, earlier entries. Up / Down move through them (in an empty box: through your history), Tab fills in the highlighted one, Esc closes.

**Seerr (Overseerr / Jellyseerr)**
- Integrations page: Seerr address, API key (stored encrypted), self-signed certificate, the run box keyword (default `s`), and an optional user id to make requests as. Test connection.
- In a run box, `!s The Terminator` searches Seerr: up to 8 movies and shows with poster, year, type and status (Available, Partly available, Requested, Pending, Not requested). **Request** a movie in one click; for a show pick All seasons or the ones you want (seasons you have or requested are shown and can't be ticked). **Open in Seerr** opens its page.

## 0.24.13

**Windows' own tray icons answer clicks (Windows 11)**
- Windows 11's volume, network, battery and "microphone in use" icons belong to Explorer's new taskbar, which ignores clicks passed on from another tray - so they did nothing in vShell's tray. vShell now answers them itself:
  - **Volume**: click - a menu with a volume slider, Mute and your output devices (pick one to switch), Volume mixer and Sound settings. Right-click - Volume mixer, Sound settings, Sound devices (classic).
  - **Microphone in use**: click - Windows' microphone privacy page (which apps are using it). Right-click - that page and the sound input settings.
  - **Network**: click - available networks. Right-click - network settings, network connections.
  - **Battery / power**: click - power and battery settings. Right-click - those and the classic power options.

## 0.24.12

**Fix: the corner cut matches the bar's corner**
- The cut at a rounded corner was drawn with a smaller radius than the bar itself whenever the radius was more than half the bar's height (e.g. a 30 px bar with radius 20 got a 15 px cut while the bar shows 20), so a graph still showed a sliver past the corner. The cut now uses exactly the corner the bar draws.

## 0.24.11

**Fix: graphs at a rounded corner**
- 0.24.10's cut at the bar's rounded corners was switched off on bars with icon animations (Lift, Glow, Magnify, Bounce), so a Tile's sparkline still showed past the corner there. Now only the corner wedges themselves are cut away - on every bar, animations or not - so graphs follow the curve while grown / lifted icons still reach past the bar elsewhere.

## 0.24.10

**Bars**
- The modules are cut to the bar's rounded shape: a graph (a Tile's sparkline, a line or bar graph) at either end of a bar with rounded corners now follows the corner instead of showing past it. (Not while icon animations are on - grown icons are meant to reach past the bar.)

**Widgets with tabs**
- Every tab is as wide as the widest one (or the tab strip). A note in a tab wraps at that width, and the other modules fill it, instead of each keeping its own width.

## 0.24.9

**Tile in slim bars**
- In a bar the Tile's text now scales with the bar's height, so nothing is cut off in slimmer bar styles, and the big percentage is a little smaller.
- Bars under 35 px tall: the percentage sits on the same line as the label ("CPU · 61°C  34%"), with the sparkline under them down to the bottom edge.

**Bars**
- The lowest bar height is now 30 (was 24). Bars set lower are raised to 30. Of the built-in bar styles only Slim's Minimal look was lower (28) and is now 30; your own saved bar styles (on their own or inside your themes) are raised to 30 too.

## 0.24.8

**Fix**
- Edit modules with three zones on: modules could not be dragged (the drag measured the mouse against the hidden single-row strip, so it never seemed to move). Dragging between and within the zone columns works now.

## 0.24.7

**Tile: module width**
- With the Tile graph style, the module's settings have **Module width** (0 = as wide as its text needs). The sparkline follows the width; a label that no longer fits is shortened with "…". Works in bars and widgets, for CPU, Memory, GPU, Disk and HWiNFO sensors.

## 0.24.6

**Tile graph style: the sparkline fills the module**
- In a bar as well as a widget: the label ("CPU · 61°C") sits at the top, and the sparkline fills everything under it - the module's full width, from just under the label right down to the bottom edge of the bar (or of the module's row in a widget). The big percentage is drawn on top of the line so it stays readable.
- The same for every module with Tile: CPU, Memory, GPU, Disk (activity) and HWiNFO sensors. Disk space shows a thin meter along the bottom edge instead of the line, under the big number.

## 0.24.5

**Three zones in Settings**
- "Three zones (left / centre / right)" is now in the bar page's Layout section.
- With zones on, the bar's Edit modules page shows the modules in three columns - Left, Centre, Right - instead of one row. Drag a module into another column (or next to a module in it) to change its zone; drag within a column to reorder. An empty zone says "Drop modules here".

## 0.24.4

**Fix: taskbar squeezed in a zone**
- With three zones on, a taskbar in the centre (or any) zone could keep a tiny width - narrow buttons - after windows opened or closed, until its zone was changed and back. The zones are now laid out again whenever the taskbar's buttons change, so the zone grows and shrinks with them.

## 0.24.3

**Ring and Tile graph styles**
- The temperature is shown in the Secondary Accent colour (Tile: after the label, "CPU · 61°C"; Ring: under the label).
- Tile in a widget: the sparkline now fills the module - its full width, from the bottom up to just under the label - with the big number over it. (Disk space still shows a meter.)
- "Temperature on a second line" and the other two-line options are hidden for Ring and Tile (they have their own layout). Disk: the GB / read-write detail follows "More detail".

## 0.24.2

**Widget header**
- New **Header text colour** (shown when "Show a header row" is on): the colour of the widget's name in the header - empty = the theme's text colour, or a theme colour (Main / Secondary Accent, Text) or your own. The header icon still takes the widget's accent colour.

## 0.24.1

**Fix**
- A widget's accent colour no longer changes its outline. An outline left empty or set to Main Accent uses the theme's Main Accent again; the widget's accent colour is only for the accent edge, the header icon and its graphs.

## 0.24.0

**New module: Notes (widgets)**
- Add it to a widget from the module gallery (Info > Notes). Four kinds:
  - **Note**: text with light formatting - **bold**, *italic*, ~~crossed out~~, bullets and tick boxes.
  - **Checklist**: tick items off; add them in the box at the bottom; double-click one to change it; drag the handle on its left to move it. Right-click an item for a due date - it turns the Secondary Accent colour on the day and says "overdue" after it. Done items can be crossed out, hidden or moved to the bottom. "2 of 5 done" under the list.
  - **Markdown**: a rendered page - headings, lists, tick boxes, code, links, quotes, rules.
  - **Log**: type in the box at the top and press Enter; each entry gets the time, newest first.
- **Stored in vShell** (with your settings) - one widget can hold several notes: right-click for New note, Rename, Delete, Next / Previous, or scroll on the note's name.
- **Or in a file** (.md / .txt) you pick or create. Read and written both ways: a change from another program shows straight away. If the file changes somewhere else while you're typing, nothing is overwritten - you choose **Keep mine**, **Take theirs** or **Keep both** (theirs is saved next to it). The file stays plain Markdown: checklists as "- [ ] item", log entries as "- **2026-10-16 14:52** text", due dates as "📅 2026-10-19" (the Obsidian Tasks style).
- **Typing on the desktop**: click (or double-click, or right-click > Edit - your choice) to write. While you type, the widget comes in front of your windows and takes the keyboard; Esc, Done or clicking somewhere else puts it back on the desktop. A small toolbar (bold, italic, heading, bullets, tick box, code, link) and Ctrl+B / Ctrl+I / Ctrl+L. Saved as you type. A press that moves still drags the widget.
- **Look**: paper colour (the theme, yellow, mint, pink, blue or your own), font (theme, monospace, handwritten), width, grow with the text or a fixed height that scrolls, "edited 2 h ago".
- **Lock** (read only), **Copy all text**, **Open the file / Show it in its folder** in the right-click menu.
- **Quick-note key**: Win+Shift+N jumps to the note that has it on and starts typing (the log / checklist box, or the end of the note). Off by default.

## 0.23.1

**Graph settings follow the style**
- Ring now has its own settings: **Ring size** (as a % of the bar or module row height), **Show the label beside the ring**, and the graph colour and warning.
- Each graph style remembers its own size. Switch from Line to Ring and back, and the line graph gets its width and height back. A Ring you never sized starts at 86 %.
- Tile hides Show text and the graph width / height (a tile sizes itself). The colour and warning still apply to its sparkline and number.
- The settings below Style change as soon as you pick a style.

**Bars**
- The Chevron separator style is removed. Bars that used it now show Dots.
- A module's Separator setting is now just **Show separator**, with "Change separator style in bar look settings."

**Widgets**
- Tabs can be renamed: with **Show the modules as tabs** on, each module's settings have a **Tab name** (empty = automatic).
- New header setting **Collapsible** (off by default). Only when it's on does the header show the arrow and collapse / expand on a click, and the right-click menu offer Collapse / Expand. With it off, the header is just an icon and a title (and still a drag handle).

## 0.23.0

**Widgets stay put when the look changes**
- A widget's position and size are now its box - the part inside the outline. The outline and the shadow sit outside it, so changing the theme, widget style, outline thickness or shadow no longer moves a widget or changes the gaps between widgets.
- Width and Height in a widget's settings are the box size too (the outline is outside it).
- Your widgets are converted once on the first start of 0.23.0, so they stay exactly where they are.
- Snapping measures the gaps between the boxes, so a thick outline makes a gap look a little smaller.
- New per widget: **Keep the box size** (Size). It freezes the size the widget has now, so styles, themes and fonts no longer change it. Untick it to go back to fitting the modules.

**Snapped widgets stay snapped**
- When you drop a widget, vShell now remembers what it snapped to: another widget (beside, above, below, or an edge or centre lined up), the screen edge or a bar.
- When a widget changes size (a new style or font, a widget collapsing, a longer text), the widgets snapped to it move so the gap stays. Chains follow along.
- A widget snapped to the right or bottom screen edge grows towards the left or up.
- Squeezed between two widgets, it follows the one to its left or above.
- Dragging a widget away lets go of what it was snapped to. Hold Alt while dropping to place it with no snapping at all. Widgets that were snapped to the one you moved stay where they are.
- Align and Distribute snap the widgets to what they line up with afterwards.
- Changing **Gap between widgets** or **Gap from the screen edges and bars** moves the snapped widgets to the new gap. There's a new switch for this, **Re-space snapped widgets when the gap changes** (Appearance > Widgets), on by default.
- Re-spacing never pushes a widget off the screen.
- On the first start of 0.23.0, widgets that already sit at the snap gap from a neighbour with an edge lined up, or at the edge gap from the screen edge or a bar, are snapped together automatically.

**Bars: three zones**
- New for full-width bars: **Three zones (left / centre / right)** (bar page > Layout). The centre zone always stays in the middle of the bar, however wide the sides are.
- Each module has a **Zone** setting (shown when zones are on). You can also Ctrl+drag a module into another zone on the bar.
- When you switch zones on, vShell makes a first guess from your spacers: modules before the first flexible spacer go Left, modules after the last go Right, and modules in between go to the Centre. A bar with no spacers puts everything Left.
- Spacers stay inside their zone: a flexible spacer only fills its own zone's free room, and a fixed spacer is a gap there.
- Switching zones off gives exactly the old single row. The zones are remembered for next time.

**Bars: separator styles**
- New bar setting **Separator style** (Look): Dots (as before), Line, Chevron (powerline segments in alternating colours, each ending in an arrow), Chips (every module in a rounded chip) or None.
- It applies to the modules with **Show separator** ticked; Chips applies to every module. Themes and bar styles don't change it.

**Widgets: header, accent and density**
- **Header row** (off by default): the first module's icon, the widget's name and a collapse button. Click the header to collapse the widget to just the header, and again to open it. A drag on the header still moves the widget. Collapse / Expand is also in the right-click menu, and the state is remembered.
- **Accent edge**: None, Top or Left, a 4 px stripe in the accent colour.
- **Accent colour** per widget (empty = the theme's Main Accent). It colours the stripe, the header icon and the widget's graphs.
- **Density**: Comfortable or Compact. Compact uses about 60 % of the padding, the space between modules and the module height, and slightly smaller text.

**Rings, tiles, grid and tabs**
- New graph styles for CPU, Memory, GPU, Disk and HWiNFO sensors:
  - **Ring**: a round gauge as tall as the bar or the widget row, with the value inside and the label beside it.
  - **Tile**: the label, a big number and a sparkline (a meter for disk space).
  - Both turn the Secondary Accent colour past the warning level.
- Widgets can arrange their modules as a **Grid** (with a column count) as well as a Stack or a Row.
- **Each module in its own card**: each module sits in its own slightly lighter rounded box.
- **Show the modules as tabs**: one module at a time, with a tab strip named after the modules. The widget stays the size of its biggest module, and the last tab you picked is remembered.

## 0.22.2

**Settings**
- On a bar's and a widget's page, the Modules section is now right under the live preview at the top.

**First start (new installs only - your current bars don't change)**
- The bottom bar now ends with a clock at the far right, with the date in the Secondary Accent colour.
- The tray shows all its icons on the bar (not grouped behind an arrow).
- The taskbar combines each app's windows into one button that shows the window title.

## 0.22.1

**Start menu in the widget style**
- The Start menu now looks like a widget of the current widget style: the theme background (with its gradient and opacity), the style's corner radius, outline (colour, thickness, opacity), shadow and padding.
- The rounded parts inside it - the left strip, the search box and the power list - follow the style's corners too, and the power list uses its outline.
- Changing the widget style changes the menu the next time it opens.

## 0.22.0

**Faster Start menu**
- The menu is built once (a few seconds after vShell starts) and kept, instead of being rebuilt with every program on each click - it opens straight away, including the first time.
- Program icons are loaded in the background after start-up, so the menu opens with its icons already there.
- The Start Menu folders are no longer re-read when you click (it used to happen on almost every open). vShell watches the folders and reads them again in the background only when a shortcut is added, removed or renamed; new apps appear in the menu by themselves.
- Searching uses its own short result list, so typing and clearing the search box is quick.
- Popups (Start menu, tray, launcher groups) appear as soon as they are in place instead of waiting a fixed moment, and their drop shadow is drawn once instead of on every change inside them.

**Application launcher groups**
- The group menu now looks like a widget in the current widget style: the theme background (with its gradient and opacity), the style's corners, border, shadow, padding and spacing.

## 0.21.4

**Gradient backgrounds**
- Bottom bars now show the gradient mirrored: if a top bar fades from bottom to top, a bottom bar fades from top to bottom, so the two match (left-to-right gradients stay the same). It follows the bar when you drag it to the other edge.
- Appearance > Background: "Mirror the gradient on bottom bars" (on by default) - untick for the same gradient on every bar. Widgets are not affected.

## 0.21.3

**Icon animations on every bar**
- Lift, Glow, Magnify and Bounce now work on every bar layout (Classic, Floating, Dock, Minimal), not just the Dock. The module settings section is now called "Icon animations".
- They still need icons only: Taskbar "Combined buttons show: Icon only", Launcher with names switched off.
- The bar's window gets transparent room above it for the grown icons; clicks go through it. Magnify's extra room at the sides stays on the bar's own monitor, so a full-width bar gets none - icons right at the screen edge may be cut off at the edge when magnified.

## 0.21.2

**Taskbar and Application launcher**
- New setting "Highlight box behind the icon under the mouse" (on by default) in both modules' settings. Turn it off for no box on hover - nice with the dock animations. The open app's taskbar button keeps its own background.

## 0.21.1

**Dock animations**
- Glow now shows: a soft Main Accent halo behind the icon (on the dock, under the icons, reaching past the dock's edge if it's big), plus a close glow around the icon's shape. Before, it was only a faint shadow that was hard to see.
- New Glow strength (10-100 %) and Glow size (120-300 % of the icon) settings, shown when Glow is on.
- Lift and Magnify are now one or the other: ticking one unticks the other. Magnify size only shows when Magnify is on.

## 0.21.0

**Dock animations**
- On a bar with the Dock layout, the Application launcher and the Taskbar can animate their icons:
  - **Lift**: the icon under the mouse rises a little and grows slightly.
  - **Glow**: a soft Main Accent glow around it.
  - **Magnify**: like the macOS dock - the icon under the mouse grows the most, its neighbours less the further away they are, and they spread apart. The size is adjustable (110-250 %).
  - **Bounce**: the icon bounces when you start an app from it (a launcher shortcut, a pinned app that isn't running, or Shift+click for a new window).
- The settings are in the module's settings under "Dock animations". All four are off by default - switch on the ones you want. They only work with icons only: Taskbar "Combined buttons show: Icon only", and the Launcher with names switched off. Otherwise, or on a bar that isn't a Dock, they are greyed out with the reason.
- The dock's window gets transparent room above it (and to the sides with Magnify) for the grown icons. Clicks go through that room to the windows behind, and only the dock itself is reserved.

**Keep the dock centred**
- New for the Dock layout: "Keep the dock centred on its monitor", on the bar's page and in the bar's right-click menu. With it on, the dock sits in the middle of whichever monitor you drag it to, and stays centred as icons come and go.

## 0.20.4

This release also includes 0.20.3 (modules fit slim bars) - in case it didn't install for you.

**Updates are more reliable**
- 0.20.3 didn't install because the previous vShell took too long to close: the new version found it still running, quit, and the update was rolled back.
- The new version now waits up to 10 seconds for the previous one to finish closing. If it is still running after that (stuck closing - it would otherwise need End task in Task Manager), the new version ends it and starts. The previous version's settings are already saved by then.
- The update helper also ends the previous vShell if it hasn't closed within 20 seconds (from the update after this one, as the helper comes from the version being replaced).
- vShell can no longer hang while closing: it ends itself after 12 seconds whatever is holding it up. If closing takes longer than a second, the log says which step was slow.

## 0.20.3

**Modules fit slim bars**
- Bar modules are never taller than the bar any more. A module that needs more room - two lines of text (CPU / GPU / RAM with the temperature underneath, fans, disks, network), a big icon - is shrunk evenly to fit instead of being cut off. Modules that already fit don't change. The taskbar and tray size themselves to the bar as before.

## 0.20.2

**Bar layouts**
- A bar's page only shows the settings its layout has:
  - Classic: height, corner radius and the outline (always on). No edge gap and no "round all four corners".
  - Minimal: height and corner radius. No outline, no edge gap.
  - Floating: height, gap from the screen edge, corner radius, round all four corners, and an optional outline.
  - Dock: height, corner radius and an optional outline. No edge gap.
- The Full width switch is gone - pick the layout instead.
- Settings a layout doesn't have no longer affect the bar: only Floating bars round all four corners or keep a gap from the screen edge. Existing bars are tidied up the same way when you update.

**Themes and styles**
- Applying a theme, bar style or widget style when some bars or widgets have their own look now asks whether to apply it to them too (Yes = they follow the style from now on, No = they keep their own look, Cancel = don't apply). Nothing is asked when they all follow the style.

**Modules fit slim bars**
- The Start menu and Launcher icons shrink to fit the bar's height, so they are no longer cut off in slim bar styles (e.g. Tokyo Night Slim).

## 0.20.1

**Background blur, rebuilt**
- Blur and Frosted now show your wallpaper behind the bars and widgets, softened. Blur didn't show anything on Windows 11 before, and Frosted had a square box at the rounded corners.
- The corners are smooth now, and widgets keep their drop shadow with blur on. Switching blur on or off no longer makes the bars and widgets flicker.
- Frosted is a little lighter with a fine grain, like Windows' acrylic. Both follow wallpaper changes.
- Blur shows the desktop wallpaper, not windows: a widget kept above other windows shows the blurred wallpaper behind it, not the app.

**Styles**
- Save Bar Style and Save Widget Style buttons at the top of each bar's and widget's page (next to Copy settings). Shape a bar or widget on its own page, then save that look as a style. A bar's look becomes the look for its layout (Classic, Floating, Dock or Minimal).
- Appearance no longer has the "Look for each layout" and "Widget look" settings - looks are set on each bar's and widget's page.
- Save as theme and Export take the shapes your bars and widgets have now (for each layout, the bar that shows it).
- Fewer built-in styles: bar styles Soft, Sharp, Round, Slim, Bold, Hairline, Neon, Ghost and Retro; widget styles Soft card, Outlined, Borderless glass, Sharp, Bubble, Neon and Retro. The 19 themes are paired again from these.

**Appearance**
- Background opacity moved into the Background section, next to the blur.

**Update notice**
- The release notes in the update notice now show in full and scroll (mouse wheel, or the bar on the right), with headings and bullet points.

**Alongside Explorer only**
- Removed the Replace Explorer Shell section from System and the desktop icons (they were only for running vShell as the Windows shell).
- If an older version made vShell your Windows shell, vShell now undoes that when it starts (Explorer is the shell again from the next sign-in).

## 0.20.0

This release also includes 0.19.0 (background blur, performance) and 0.18.2 (download progress in the update notice), which weren't published on their own.

**Themes**
- New Theme section at the top of Appearance. A theme sets the colours, the bar style and the widget style in one click. There are 19 built-in themes, one for every colour preset, each paired with a bar and widget style that suits it - e.g. Nord Hairline, Cyberpunk Neon, Matrix Terminal, Gruvbox Retro, Everforest Cozy, Kanagawa Pill, Midnight Ghost. They show as picture cards like the colour presets.
- Save as theme… saves your current look as your own theme (★). You choose what it includes: colours, bar style, widget style. Anything left out is left as it is when the theme is applied, so a colours-only theme keeps your bars' shapes.
- Export… / Import… now carry the whole theme: colours, bar style and widget style. Older theme files still import as colour presets.

**Bar styles**
- Style outlines use the theme's colour slots (Main Accent, Secondary Accent or Text), so every style works with any colours.
- Bars now have a layout: Classic (full width, with an outline), Minimal (full width, no outline), Floating (only as wide as its modules, away from the screen edge) and Dock (only as wide as its modules, against the screen edge). Pick it at the top of each bar's page.
- A bar style holds a look for every layout: height, corners, gap from the screen edge and outline. Each bar takes the look for its own layout, so one theme can have a Classic bar at the bottom and a Floating bar at the top.
- 12 built-in bar styles: Soft, Sharp, Round, Slim, Bold, Pill (fully round ends), Hairline (one crisp line in the text colour), Neon (bright outline in the Secondary Accent), Terminal (square and compact), Cozy (tall and roomy), Ghost (barely any outline) and Retro (chunky outline, small corners). You can edit the look for each layout and save your own.
- Appearance > Bar style lists your bars with their layout, and whether each follows the style or has its own look.

**Widget styles**
- A widget style sets the box around every widget: corners, border, shadow, padding and spacing. 12 built-in styles to match the bar styles (Soft card, Outlined, Borderless glass, Sharp, Bubble, Pill, Hairline, Neon, Terminal, Cozy, Ghost, Retro), and you can save your own.

**Your own looks are kept**
- Each bar and widget has a "Follow the bar style" / "Follow the widget style" switch. Changing a bar's or widget's corners, height, outline, border, shadow or spacing on its own page turns the switch off, so styles and themes leave it as it is. Turn it back on to take the style again.
- When you update, your bars and widgets keep exactly the look they have now. Any bar or widget that looks different from the others keeps its own look.

**Colours**
- Colour presets are now in their own Colours section under Theme, with Save colours… and Delete next to them.

**Background blur**
- New in Appearance > Background: Background blur - Off, Blur or Frosted (Windows' acrylic look) - behind every bar and widget. The background opacity sets how much of the theme colour lies over it (40-70 % works well).
- Saved with your theme presets and in exported theme files.
- With blur on, bars and widgets are drawn by Windows' compositor directly (lighter than the usual transparent windows); widgets have no drop shadow and the rounded corners are not smoothed.

**Faster and lighter**
- Power flow widget: only the moving dots are redrawn 30 times a second; the animation pauses when the PC is locked or a maximised / full-screen window covers the widget.
- Taskbar: buttons are only rebuilt when what they show changes (a window title changing no longer rebuilds every button), and open previews aren't rebuilt for nothing.
- Settings: changing a bar or widget only rebuilds that bar or widget, and settings.json is written once after a burst of changes instead of on every slider step.
- CPU / memory / network / disk and other text modules only update their text when it changes.
- GPU usage reads less often on PCs where Windows' GPU counters are slow to read.
- Network modules share one reading and only look up the network adapters when the network changes.
- HWiNFO values are read in the background.
- Wi-Fi name and signal come from the Windows Wi-Fi API instead of starting netsh every 15 seconds.
- Home Assistant: live updates over HA's WebSocket - states change the moment HA changes them, without polling each entity every few seconds. Falls back to polling automatically.
- Minimised windows are moved out of the way as soon as they minimise, instead of checking three times a second.
- Starts faster (pre-compiled build).

**Reliability**
- settings.json is replaced in one step when saved, so a crash or power cut can't leave you without it.
- vshell.log is kept to about 1 MB (the previous part goes to vshell.old.log), and writes from different threads no longer collide.

**Updates**
- The update notice now shows the download: click Install and restart and it shows a progress bar with the percentage and MB, then "Checking and installing…" and "Installed - restarting vShell…".
- If the update can't be installed, the notice says why, with Try again and Close.

## 0.18.1

**Taskbar**
- Pinned apps now belong to the taskbar you pin them to. Each bar's taskbar has its own pins; pinning or unpinning on one bar no longer changes another.
- Pins made in 0.18.0 move to the first bar's taskbar.

## 0.18.0

**Taskbar: combined buttons, previews and pinned apps**
- Windows of the same app now share one taskbar button (on by default). Choose Always, When the bar is full or Never in the Taskbar module's settings.
- Combined buttons can show the icon only, the icon and app name, or the icon and window title. A small number on the icon shows how many windows are open.
- Hover a button for live previews of its windows. Click a preview to switch to it, ✕ or middle-click to close it. Preview size is adjustable, and previews can be turned off.
- Click an app with several windows to pick one from the previews (or a list when previews are off). Shift+click or middle-click an app to open a new window of it.
- Pin apps to the taskbar: right-click an app > Pin to taskbar, or add a program or shortcut in the Taskbar module's settings. Pins stay when the app is closed; right-click to unpin or move them left / right.
- Right-click a combined button for New window, Pin / Unpin, Minimize all windows and Close all windows.

## 0.17.1

**Building releases**
- `build.cmd` now also writes `dist\release-notes.md` - this text - ready to paste into the GitHub release, and prints the tag and title to use.

## 0.17.0

**Updates**
- vShell now updates itself from GitHub releases. It checks a minute after start-up and then daily, and shows a notice when a new version is out: Install and restart, What's new, Skip.
- New Updates card in Settings > System: your version, the latest release and its notes, Check now, Install and restart, Skip this version. Options to turn automatic checks off and to include pre-releases.
- Downloads are checked against the release's SHA-256 checksum. vShell restarts into the new version in the same place, so shell mode, run at login and your settings carry on. If the new version won't start, the previous one is put back and that version is skipped.
