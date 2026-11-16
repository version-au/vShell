# What to try in v0.3

Build with build.cmd, run dist\vShell.exe. Your existing settings are migrated: the old global font size / corner radius / edge margin are copied to every bar.

1. **Per-bar look** - Settings -> Bars -> pick a bar. Font size, Corner radius, Edge margin are now there (different per bar). Colours/font family stay shared.
2. **Corner mode** - set a corner radius, untick "Round all four corners": a top bar rounds its bottom corners, a bottom bar its top corners. Drag the bar to the other edge: the rounded corners swap.
3. **Presets** - set colours, "Save as preset…", give a name; it shows with a star in the list. "Delete" removes the selected saved preset.
4. **Launcher** - "Show names next to group icons" is separate from shortcuts. In "Edit shortcuts and groups…" select a group (or shortcut) and set a custom icon (png/ico/jpg or an exe).
5. **Tray popup (shell mode)** - on a bottom bar the popup should open directly above the bar and not cover the arrow; on a top bar below it. Click the arrow again, press Esc, or click elsewhere to close it.
6. **Pinning** - open the tray popup, drag an icon out and drop it anywhere on the bar: it should stay on the bar. Drag it onto the arrow to hide it again.
7. **Temperatures** - CPU/Memory/GPU module -> "Show temperature". NVIDIA GPU works without HWiNFO; CPU/RAM need HWiNFO (see README "HWiNFO and temperatures").
8. **HWiNFO sensor module** - add "HWiNFO sensor", pick a reading, try Style = Line with a range (e.g. 30 to 95 for a temperature).

## Start menu module (added after v0.3)

9. **Start menu** - Settings -> Bars -> Add module -> "Start menu". Click the button: a menu opens above (bottom bar) or below (top bar) it, centred on the button and kept on screen. Programs appear A-Z; icons fill in a moment after it opens. Store apps appear a few seconds after the very first open (they are read from PowerShell's Get-StartApps; if none ever appear, check the log).
10. **Search** - just start typing; Enter launches the top hit, Up/Down moves, Esc closes. Clicking a program launches it and closes the menu.
11. **Power** - the power button (bottom of the left strip) shows Lock / Sleep / Sign out / Restart / Shut down. Try Lock first. Restart and Shut down act immediately (Windows still asks apps to save).
12. **Windows key** - tap and release the Windows key by itself: the menu opens; tap again: it closes. Windows' own Start menu should NOT also appear (alongside Explorer). Win+E / Win+R must still work. Untick "Open with the Windows key" to give the key back.
13. **Icon** - default is a dot grid in the Main Accent; change "Default icon colour", or set a custom icon (png/ico/jpg or an exe) and clear it again.

## Widgets and desktop icons (v0.4)

14. **Widgets** - Settings -> Widgets -> "Add widget": a boxed clock appears near the top-left. Drag it somewhere, restart the app: it comes back there. Add a second widget; add several modules (CPU, Memory, Clock) to one widget and try "Stack the modules" vertical / horizontal, Module height, Corner radius, Background opacity, border/shadow.
15. **Widget layer** - a widget should sit behind your windows (and stay visible when you show the desktop). "Keep above other windows" floats it. Clicking a widget must not bring it in front of your windows or steal focus. Alongside Explorer this is the part most likely to need a tweak - tell me what you see.
16. **Widget extras** - right-click a widget: Widget settings / Lock position / Hide this widget. Put a Start menu or Launcher in a widget: its popup should open next to it (below if the widget is in the upper half of the screen, above otherwise). Settings -> Widgets -> change "Monitor".
17. **Desktop icons (shell mode)** - Settings -> Desktop -> Desktop icons: This PC, Recycle Bin, Documents, Pictures are on. Double-click to open; drag to move (snaps to a grid, remembered); icon size, names on/off, monitor. Right-click -> Hide this icon (the tick in Settings follows).
18. **Recycle Bin** - delete a file, the icon should change to "full" within a few seconds; right-click -> "Empty Recycle Bin" (Windows asks to confirm) -> icon goes back to empty.
19. **Alongside Explorer** - the "Desktop icons" section in Settings -> Desktop is greyed out, and no icons are drawn.

## New modules (v0.5)

20. **Add them** - Settings -> Bars (or Widgets) -> Add module: Network speed, Disk, Battery, Volume, Uptime, Top processes, Text label. Each has its own settings underneath the module list.
21. **Network** - run a download: the arrows should show the speed and the line graph should follow it. Tick "Show the Wi-Fi name" on a laptop/Wi-Fi PC.
22. **Disk** - tick "Two lines" to put the % over the GB (Space) or the read/write speeds (Activity). Space shows the Windows drive; type another drive letter (e.g. D). Switch to Activity, copy a large file: busy % and read/write speeds move (the first second shows "n/a").
23. **Battery** - on a desktop it should disappear; untick "Hide this module" to see it say "none". On a laptop unplug/plug the charger.
24. **Volume** - Style is Text or Meter with a width slider (no height, no bar/line graphs). A speaker icon follows the level and shows muted. Scroll the wheel over it; click it: Mute/Unmute and your playback devices (the current one ticked); pick another and check Windows switches.
25. **Top processes** - hover for the list; click opens Task Manager. Try Sort by Memory, and "Programs shown on the bar / widget" 1-5; set Layout = List (bar or widget) and use "List text size" and "List line spacing" to fit them.
26. **Home Assistant** - Settings -> System -> Home Assistant: address (e.g. http://homeassistant.local:8123), paste a long-lived token, "Test connection". Then add a "Home Assistant entities" module -> "Choose entities…" -> "Add from Home Assistant…", filter, add a few. Click a light/switch to toggle it. In a widget try Layout = List.
27. **Service status** - "Choose what to watch…": add e.g. Http `status.example.com`, Tcp `192.168.1.x:22`, Ping your router. Dots turn green/red within one check interval; hover for the details; click an Http one to open it.

Report back: build errors (paste them), anything that looks/behaves wrong, and the log file if something is missing.

## v0.5.4
28. Turn on the bar outline and raise the thickness: the bar's content and position must not move; the outline grows outward (the bar window is enlarged by the border width on each side). Same for a widget's border.
29. Open the Start menu, then click the desktop (or a widget / desktop icon): the menu must close. Clicking the Start button again still toggles it.

## v0.5.5
30. Shell mode first start: no "Windows cannot find 'C:\Program'" error (Run-key entries with unquoted paths containing spaces are now resolved properly; check the log for "Startup cmd failed" lines if one still fails).
31. Minimise a window: no small caption box appears at the bottom-left; restoring from the bar's taskbar still works.

## v0.5.6
32. Full-width bar with the outline on and edge margin 0: no outline on the sides touching the screen edge (nothing spills onto the neighbouring monitor); the rounded corners still get their outline.
33. The app is now called vShell (vShell.exe). First start copies settings from %APPDATA%\SharpBarWin to %APPDATA%\vShell. If you had set SharpBarWin as the shell, use Settings -> Windows shell again so the registry points to vShell.exe.

## v0.5.7
34. Minimise a window: no caption box at the bottom-left (two mechanisms: ARW_HIDE setting + a 300 ms sweep that parks minimised boxes off-screen). Check vshell.log for "MinimizedMetrics ARW_HIDE set: True".

## v0.5.8
35. Widgets tab: "Width" slider (0 = automatic, otherwise 80-800). Set two widgets to the same width: they match exactly (outline included), heights follow their content.

## v0.5.9
36. Network module graph (Meter / Bars / Line): download in the graph colour, upload in a second shade of the same colour (lighter for dark colours, darker for light ones), both on the same scale ("Graph full scale"). Meter shows two thin stacked meters; Bars shows paired bars; Line overlays two lines.

## v0.5.10
37. Run a full-screen game / F11 video / borderless-fullscreen app: bars on that monitor go behind it (checked every 0.5 s); alt-tab out or exit and they return on top. Bars on other monitors are unaffected unless that monitor is covered too. (Widgets are not affected yet.)

## v0.5.11
38. Home Assistant module -> Choose entities -> double-click an entity: "Graph next to the value" (None / Meter / Bars / Line), empty-at / full-at values, width, height %, colour. Each entity can have its own graph; on/off entities draw 100/0. Line and Bars build up history while vShell runs.

## v0.5.12
39. HA entity edit -> "Graph position": Left puts the graph before the text; Right puts it after (in a widget with Layout "List" it is pinned to the right edge of the row, so a fixed-width widget lines all graphs up).

## v0.5.13
40. Widget background now matches the bars exactly (same colour and transparency) unless "Use my own background opacity" is ticked. The widget shadow is drawn only outside the widget, so it no longer darkens a translucent background.

## v0.5.14
41. Bars and widgets: "Use theme background opacity" (on by default) follows the theme's Background opacity slider. Untick it and the "Background opacity (%)" slider below sets that one bar / widget only.

## v0.5.16
42. Bars tab and Widgets tab: "Copy settings" / "Paste settings" next to Add / Remove. Copy one bar (or widget), select another and Paste: Yes = look and modules, No = look only. Name, monitor and position of the target are kept; pasted modules get their own ids. The copy is remembered until vShell closes.

## v0.6.0 - theming
43. Theme tab -> Background style: Solid / Gradient. "Gradient colour" is the second colour, "Gradient angle" 0 = left-right, 90 = top-bottom. Bars and widgets follow it; opacity applies to both colours.
44. Theme tab -> "Secondary Accent colour" (a fifth theme colour, saved in presets and theme files).
45. Select a module on a bar or widget -> "Three coloured dots before this module": smaller dots in Main Accent, Secondary Accent and Text colour, drawn on the LEFT of the module (vertical stack, bars only - widgets no longer have this option).
46. (replaced in v0.10.2 by the Wallpaper Theme - see below)
47. Theme tab -> Share a theme: Export theme... writes *.vstheme.json; Import theme... applies it and adds it to the preset list.
48. Shell mode: right-click an empty part of the desktop -> menu with Display settings, Personalize, vShell settings, Task Manager, Run. (Alongside Explorer, Explorer's own desktop menu is used.)
49. Alongside Explorer: restart vShell with a Tray module on the bar. Within ~2 s the icons of running apps (Discord, Steam, OneDrive, ...) appear in the Tray module. Click/right-click an icon. Settings, Display settings and Personalize still open. Quit vShell: the icons reappear in Explorer's tray (`vShell.exe --restore-taskbar` to see it).
50. As the shell: the Tray module works as before (vShell's own native tray host).
51. Alongside Explorer, with the Tray module on: maximise a window -> it must stop at vShell's bars, not slide under them (app-bar space is now passed to Explorer the way Cairo does it).
52. Maximise Chrome/Firefox -> no 1-2 px gap at the screen edge (apps are no longer told the taskbar auto-hides).
53. Click a tray icon whose app opens its own flyout next to the icon (OneDrive, Teams, 1Password, EarTrumpet, Windows Security...) -> the flyout opens beside the icon on the vShell bar, not at the top-left of the screen. Hover an icon -> rich tooltips appear.
54. Restart Explorer (Task Manager -> Windows Explorer -> Restart). Within ~2 s the Windows taskbar is hidden again, maximised windows still respect the bars, and the tray icons come back to vShell.

## v0.8 (taskbar / tray features ported from Cairo's ManagedShell)
55. Taskbar reacts instantly: open, close and rename windows (e.g. change browser tab) - buttons update straight away, not up to half a second later.
56. Flashing: start something that asks for attention while it is in the background (a chat message, an installer finishing, `cmd` running `timeout 3 & echo ^G`) -> its button turns Main Accent-coloured; clicking it / activating the window clears it.
57. Store apps: open Settings, Calculator, Photos, Microsoft Store -> their buttons show the app's own icon instead of a blank/generic one.
58. Progress: download a file in Edge/Chrome or copy a big file in Explorer -> thin progress bar along the bottom of the button (red on error, yellow when paused).
59. Badges: Discord / Teams / Outlook with unread items -> small badge on the button's icon; hover shows the badge description.
60. Full screen: game / F11 browser / full-screen video on one monitor -> that monitor's bars drop behind it. Click onto another monitor -> bars there stay, the game's monitor stays clear. Alt+Tab to another window on the game's monitor -> bars come back. Maximised (not full-screen) windows must NOT hide the bars.
61. Balloons: an app's tray notification (e.g. a backup tool's "Backup complete") -> themed balloon beside the tray icon for ~7 s; click it -> the app reacts; X closes it. Settings -> System -> "Show tray icons' balloon notifications" turns them off. No balloons while a full-screen app is in front.
62. Shell mode: Network, Power/Battery and Safely Remove Hardware icons appear in the tray (Windows' own); Windows' Volume icon does not (use the Volume module).
63. Windows 11, monitors with different scaling (e.g. 100% and 150%): bars, widgets and popups are sharp and correctly sized on both; change a monitor's scaling while vShell runs -> bars re-place themselves. Bars still do not appear in Alt+Tab or the taskbar.

## v0.9 (the Secondary Accent colour)
64. Network module with a graph: upload is drawn in the Secondary Accent colour next to the Main Accent download.
65. Taskbar: a flashing button is tinted / underlined in Secondary Accent (the active button stays Main Accent); download / copy progress bars are Secondary Accent (red on error, yellow when paused).
66. Thresholds: CPU / memory / GPU / disk / network / battery / HWiNFO sensor -> "Warn past threshold". Set CPU to 20 % and load the PC: text and meter turn Secondary Accent; bar graphs colour only the bars past it; a line graph turns Secondary Accent while the latest value is past it. Battery: switching it on starts at "20, at or below". HWiNFO thresholds are in the sensor's unit (80 = 80 °C).
67. Home Assistant: double-click an entity -> the last three fields set a threshold in the entity's unit; the value (and its graph) turn Secondary Accent past it.
68. Clock: "Seconds", "Time separators" and "Date" in the Secondary Accent colour each work on their own and together, in 12 and 24 hour.
69. Start menu: the letter headings are Secondary Accent; typing a search highlights the matching part of each name in Secondary Accent (bold).
70. Colour slots: on any module colour (graph, meter, label text, Start icon, HA graph colour) and on the bar outline / widget border, the Main Accent / Secondary Accent / Text buttons store @main / @secondary / @text. Change the theme preset -> those colours follow; a hex colour stays fixed.
71. Presets: every built-in preset sets a matching Secondary Accent and gradient colour. New: Cyberpunk, Neon 2077, Synthwave '84, Matrix, Dracula, Tokyo Night, Gruvbox Dark, Rose Pine, Everforest, Kanagawa, Oceanic, Blood Moon, AMOLED Black.


## v0.10 (new settings window) - not compiled or run on Windows yet, please report anything odd
1. Open Settings: sidebar on the left, Appearance page first, colours follow your theme. A dark theme gives a dark title bar.
2. Click a preset card: the bars change and the card gets a tick and Main Accent outline. Save as preset -> a starred card appears; click it,
   then Delete preset removes it. Export / Import a theme file.
3. Change Main Accent / Secondary Accent / Background: the settings window re-colours itself too. Try a light preset too, to check readability.
4. Sidebar -> a bar: live preview at the top (it should move as the clock ticks). Change Height / Corner radius - the preview follows.
   Copy settings on one bar, Paste on another.
5. Edit modules ›: drag a chip onto another chip (left half = before, right half = after). Use ← / → , Duplicate, Remove. Type in the
   filter box, click a tile to add a module. Fold a section (click its title) - it stays folded for other modules.
6. Ctrl+drag a module on the real bar while the module editor is open: the chips should follow.
7. Widgets: hide a widget with "Show this widget" - the preview says it is not shown; show it again - the preview comes back.
8. Startup apps, Home Assistant (Test connection), System, Desktop (wallpapers, desktop icons greyed out alongside Explorer), About
   (Open folder / Open log).
9. Shrink the window to its minimum size: nothing should be cut off badly (pages scroll).

## v0.10.1
1. Appearance: only the preset in use shows (or "Current (not a preset)" for your own colours). "Show all N" with the arrow expands
   the gallery, "Show less" folds it again. Picking a preset with the gallery open keeps it open.
2. The theme colours are now called Main Accent and Secondary Accent everywhere (settings, chips, tooltips, README). Your saved settings,
   presets and theme files from earlier versions still load - check your colours are unchanged after upgrading. Module colours set to the
   old "@accent" / "@spare" become "@main" / "@secondary" automatically.
3. Warning section of any graph module: "Warn past threshold" and the threshold only; "Warn below threshold" is on the Battery module
   alone (and gone from Home Assistant entities). A non-battery module that had "below" ticked now warns at or above.
4. Every module (except Spacer) has a Font section: "Follow Bar's font size" (bars) / "Follow Widget's font size" (widgets), on by
   default. Untick it: a Font size slider appears, starting at the bar's / widget's size. Change it - only that module's text changes.
   Clock: two-line layouts still size themselves to the bar height (the Text size % still applies). Popups (Start menu, tray, launcher
   groups) keep the bar's size.

## v0.10.2 (Wallpaper Theme)
1. Appearance -> Colours: the last row is "Wallpaper Theme". Turn it on: a "Wallpaper theme" card appears at the top; the bars,
   widgets and this window change to the wallpaper's colours; the colour rows (except opacity) and the gradient colour are greyed out.
2. Check the log for "Wallpaper theme from <file>: bg ..., accent ..., secondary ..., text ..." - tell me which colours look wrong
   for your wallpaper and I can tune the rules.
3. Try a dark wallpaper, a bright one (e.g. a beach or sky - the theme should go light with dark text), and a mostly black-and-white one.
4. Change the wallpaper: the theme follows within a few seconds.
5. With per-monitor wallpapers on, the primary monitor's picture is used.
6. "Save as preset…" next to the card: a starred preset with those exact colours. "Turn off" (or picking any preset) returns to
   your own colours.

## v0.10.3
1. Wallpaper Theme on, Settings open: change the wallpaper (Settings -> Desktop, or in Windows). The settings window re-colours itself
   and the "From your wallpaper" card on Appearance shows the new colours, along with the bars and widgets.

## v0.10.5 (Reserve window space)
1. Settings -> a bar -> Size -> "Reserve window space" (on by default). Maximise a window on that monitor: it stops at the bar.
2. Turn it off: the bar becomes part of the desktop, like a widget - maximised windows fill the monitor, and any window can cover the
   bar. Clicking the bar does not bring it forward; it shows again when the windows over it are moved or minimised (or Win+D).
   Its modules (Start menu, tray, clock, popups) still work when you can see it.
3. Turn it back on: on top of everything again, and maximised windows stop at it. Check alongside Explorer and in shell mode
   (shell mode: the bar must stay above the wallpaper and must not flicker).

## v0.10.6 (System page)
1. System -> Windows shell -> "vShell - run at login (Recommended)". Turn it on: Startup apps lists "vShell" (Enabled); sign out/in and
   vShell starts by itself. Turn it off: the entry is gone from Startup apps (and from Task Manager's Startup tab).
   If you already had your own startup entry or shortcut for vShell, the switch shows on, and turning it off removes that too.
2. "Replace Explorer Shell" holds the status line and the Set / Restore buttons as before.
3. Startup apps alongside Explorer: no "Start these apps when vShell runs as the shell" switch, just the list (Windows starts them).
   In shell mode the switch is there at the top.

## v0.10.7 (network colours)
1. Network module: the ↓ arrow is Main Accent, the ↑ arrow Secondary Accent (two-line layout and with Wi-Fi shown too).
2. Module editor -> Network -> Graph: "Download colour" and "Upload colour" (empty = Main / Secondary Accent). Pick colours: the graph
   series and the arrows both change. Reset puts them back to the theme colours. Warnings still turn the text Secondary Accent;
   the arrows keep their own colours.

## v0.10.8
1. CPU / Memory / GPU with "Show temperature": the temperature is in the Secondary Accent colour (same line and second line).
2. HWiNFO sensor module: the value (number + unit) is Secondary Accent, the title stays the text colour.
3. (see v0.10.9 below for "Add folder…")

## v0.10.9 (synced launcher folders)
1. Launcher -> Edit shortcuts and groups -> "Add folder…": pick a folder of shortcuts. With nothing or a top-level shortcut selected,
   a new group named after the folder appears ("[group, synced]"); with a group selected, that group starts following the folder.
   You can add several folders to one group.
2. Put a new shortcut in the folder and delete another, then click the group on the bar: the menu shows the change straight away.
3. Rename a synced shortcut or give it an icon: kept after the next sync. Shortcuts you added by hand to the group stay (listed first).
4. Select the group: "Synced folders" lists them; "Stop syncing" keeps the current shortcuts but stops following the folder(s).
5. A folder that is missing (unplugged drive) leaves its shortcuts as they are until it is back.

## v0.10.10
1. Settings -> a bar with CPU / HWiNFO / network modules: the live preview stays still while the values update (no up/down jiggle),
   also for a bar that is not full width (it may get wider/narrower, but not move vertically). Same for the widget preview.

## v0.10.11
1. Full-width bar: the live preview shows the left part of the bar at its real size (no scaling) and the text no longer jiggles.
   Bars that fit are shown whole, as before.

## v0.10.12 (previews at real size)
1. Bar and widget previews are always drawn at real size - no jiggling text.
2. A full-width bar: a horizontal scrollbar under the preview pans left / right (or Shift+mouse wheel over it).
3. A tall widget (over ~320 px on its page, ~260 px in the module editor): a vertical scrollbar pans up / down; the mouse wheel scrolls
   the preview until its end, then the page carries on scrolling.

## v0.11.0 (Weather and Now playing modules)
Weather
1. Edit modules -> gallery "Info" -> Weather. It says "Set a location". In its settings type your suburb, press Find (or Enter), pick
   from the list if there are several. Within a few seconds: icon + temperature. Hover: place, condition, feels like, humidity, wind,
   update time. Click: today + up to 6 days (icon, high, low, chance of rain in Main Accent).
2. Units °C / °F, "Update every", "Show the condition", "Today's high and low" all change the module.
3. Weather from: Home Assistant -> the entity list shows your weather.* entities (needs the HA connection). Pick one: temperature
   and condition from it; the forecast comes from weather.get_forecasts (daily, or twice-daily folded into days).
4. Icons: sun / moon Secondary Accent, clouds text colour, rain / snow / lightning Main Accent. At night (Open-Meteo) clear = moon.
5. No network: "n/a" with the reason on hover; it retries every minute and keeps the last good values.
Now playing
6. Add "Now playing", play something in Spotify / a browser / Media Player: art, "Title – Artist", and the three buttons. Pause in the
   app: the play/pause button follows within a second. Buttons and scrolling over the module control the player.
7. Click the art or text: card with big art, title, artist, album, buttons and "Playing in <app>".
8. Settings: art on/off, buttons on/off, two lines, text width, hide when nothing is playing.
9. Check the log for "Now playing: using Windows' media sessions". If vShell ever closes by itself while starting it, the next start
   uses the media keys only (log says so); delete media-session.off in the settings folder to try again - and tell me.

## v0.11.1
1. Now playing: previous / play-pause / next buttons work with a single click (on the bar, in a widget and in the card). Scrolling over
   the module no longer changes track.
2. Weather: a single click opens the forecast.
3. Weather in a widget: "The forecast in the widget" -> the forecast appears under the current weather; "Days" 2-7; "Forecast layout"
   Row = days side by side, List = a day per line. The widget grows to fit.
4. No build warning about WeatherGlyph.Drop.

## v0.11.2
1. Weather in a widget with the forecast: "Spacing" (0-60, default 12) - the gap between the days side by side (Row) or between the
   lines (List). The widget resizes to fit.

## v0.11.3
1. Now playing with a title longer than "Text width": it waits 2 s, scrolls slowly to the end, waits, scrolls back, and repeats.
   Short titles stay still. Two-line layout: title and artist each scroll on their own if needed.
2. "Scroll long titles back and forth" off: long titles are cut off with … as before.

## v0.12.0 (logo, icon, splash)
1. dist\vShell.exe shows the three-dot icon in Explorer, Task Manager and Alt+Tab; the settings window has it in its title bar.
2. Start menu module without a custom icon: the three dots (icon colour / Secondary Accent / Text). Change the preset: the dots follow.
   "Icon colour - the first of the three dots" changes only the first dot. At icon size 40+ the faint v line shows.
3. Start vShell: a card in the middle of the main screen fades in (mark in the theme colours, "vShell", version, moving line), stays at
   least ~1.4 s, fades out when the bars are up. It must not take focus or appear in the taskbar / Alt+Tab.
4. Settings -> System -> "Show the splash screen while vShell starts" off: next start has no splash.

## v0.12.1 (first start)
To try it without losing your setup: close vShell, rename %APPDATA%\vShell\settings.json to settings.old.json, start vShell
(rename it back afterwards).
1. Bottom bar, main monitor, full width: Start menu, taskbar, (gap), tray at the right. 3 px outline at 65 %, only the two top corners
   rounded, maximised windows stop above it.
2. Top bar, main monitor, at the left, as wide as its modules: CPU, memory, GPU as line graphs, same height as the bottom bar,
   all corners rounded (13), right against the top edge (no gap). Bottom bar corner radius 13 too.
   It reserves no space: it sits on the desktop and windows cover it.
3. Default theme (Mocha). A second start keeps whatever you changed.

## v0.12.3 (default look)
1. First start: the top bar also rounds only its two corners away from the screen edge (radius 13) and has the 3 px / 65 % outline.
2. Settings -> + Add bar: the new bar has radius 13 on the two corners away from the edge, and the 3 px outline at 65 %
   (height, font size, full width and edge margin still copied from the first bar).
3. + Add widget: corner radius 13, border 3 px at 65 %.
4. Existing bars and widgets keep their own settings.

## v0.12.4
1. Settings -> About: the logo (three dots in the theme colours), "vShell", the version and a one-line description at the top.
2. The settings sidebar shows a small logo before "vShell settings". Change the preset or turn on the Wallpaper Theme: both logos
   follow the colours.

## v0.13.0 (Power flow widget)
1. Widget -> Edit modules -> gallery "Smart home & more" -> Power flow (not offered for bars).
2. Pick the sensors with Find…: solar power, grid import, grid export, battery power (positive = charging), battery charge %. Leave
   "Home use" empty to have it worked out (solar + import + discharge - export - charge).
3. Check against Home Assistant: the numbers in each circle; the dots run solar -> home / battery / grid, grid -> home / battery,
   battery -> home / grid, only on lines that carry power (others are faded), faster for more power.
4. The ring round Home is split solar / battery / grid in their colours. Battery icon fills with the charge %.
5. Charging shows under ↓ on the battery, discharging under ↑. If they are the wrong way round, tick "Battery power is positive when
   discharging".
6. Look: Size slider; Solar / Grid / Battery colours (defaults Secondary Accent / Main Accent / Text). Hover the widget for a summary.

## v0.13.1
1. Power flow: no Size setting any more. It is as wide as the widget: set the widget's Width (Size card on the widget page) and the
   diagram grows / shrinks with it. A widget with automatic width shows it 200 wide.

## v0.13.2
1. Power flow in a widget with a set Width (try 250, 400, 600): the whole diagram fits, the Home circle and its ring are not cut off.

## v0.14.0 (widget snapping, align / distribute)
1. Settings -> Desktop (subtitle "Wallpapers, widgets and desktop icons"): a "Widgets" card under the wallpaper: snap on/off, gap between
   widgets (12), gap from the screen edges and bars (12).
2. Drag a widget near another: it snaps when its left / right / top / bottom edge or centre lines up (a thin Main Accent guide line shows
   across both), and to the set gap beside / above / below a neighbour. Near the screen edges it snaps to the edge gap, below a top bar /
   above a bottom bar too, and to the screen's centre lines. The guides disappear when you let go.
3. Hold Alt while dragging: no snapping.
4. Ctrl+click widgets (anywhere on them, even on a module): a dashed accent frame marks each; Ctrl+click again to unpick.
   Right-click one: "Align N widgets" (left / right / centres, top / bottom / middles) and "Distribute" (equal gaps needs 3+;
   "snap gap" spaces them exactly by the setting, 2+). "Clear selection" removes the frames. With fewer than 2 picked the menu shows a hint.
5. Desktop icons card: the note about shell mode always shows; "Show settings" with the arrow opens the icon settings, "Hide settings"
   folds them away.

## v0.14.1 - lock several widgets at once

- Ctrl+click two or more widgets, right-click one: the menu shows "Lock N widgets in place".
- Click it: all picked widgets become locked (none can be dragged; check each one's "Lock position" in settings shows ticked).
- Right-click again with the same pick: it now says "Unlock N widgets in place" (shown only when every picked widget is locked). Click it: all unlock.
- Mixed pick (some locked, some not): the menu says "Lock …" and locks the rest.
- Ctrl+click still picks/unpicks locked widgets.

## v0.14.2 - right-click menu tidy

- Right-click a widget with nothing (or one widget) picked: "Lock position" / "Unlock position" for that widget, plus a greyed hint "Ctrl+click widgets to select multiple".
- Ctrl+click 2+ widgets and right-click: no single Lock/Unlock item; only "Lock N widgets in place" (or "Unlock N widgets in place" when all picked are locked) alongside Align / Distribute / Clear selection.


## v0.15.0 - Proxmox VE module, Integrations page

Settings
1. Sidebar: "Home Assistant" is now "Integrations". The page has a Home Assistant card (same as before; existing address/token still there) and a Proxmox VE card.
2. Proxmox card: make a token in Proxmox (Datacenter > Permissions > API Tokens). With Privilege Separation ticked, add API Token Permissions: / PVEAuditor, /vms PVEVMUser.
3. Enter an address (e.g. 192.168.1.10 becomes https://192.168.1.10:8006), the token ID (root@pam!vshell) and the secret; leave "Accept a self-signed certificate" on.
4. Test connection: "Connected: Proxmox VE 8.x, cluster "name", N nodes, N guests. Start / stop allowed." (or a note that the token can only look).
5. Put two node addresses separated by a comma, one of them wrong: the test lists each; the module keeps working through the good one.

Module (gallery "Smart home & more" > Proxmox VE, bars and widgets)
1. Show = Nodes, bar, Style Meter: each node's name with two small bars (CPU Main Accent, RAM Secondary Accent). Style Text: "12% · 45%". Offline node shows "offline" in red.
2. Hover: cluster name, each node's CPU / RAM / uptime, guests running. Click: popup with Nodes, Guests (grouped by node), Storage, "Open web interface".
3. In the popup click a guest: menu with Start (stopped) or Shut down / Reboot / Stop (running). Start goes straight away; the others ask Yes/No first. The dot turns yellow with "starting…" until Proxmox reports the new state.
4. Token without VM.PowerMgmt: the action shows a message saying which permission is missing.
5. Show = Guests, bar: dot + "6/8 running". Widget: one line per guest (dot, id, name, CPU %, memory); click or right-click a guest for the menu. "Hide stopped guests", "Show templates", "Show each guest's CPU and memory" work.
6. Show = Storage: each pool with a meter / percentage; a shared pool (NFS, Ceph...) appears once, marked "shared" in widgets when there are several nodes.
7. Node = a single node: only that node's figures / guests / storage (plus shared storage).
8. "Allow start, shut down, reboot and stop" off: the guest menu says it is turned off.
9. Widgets: dragging the widget by its Proxmox list still works everywhere except on guest rows (those take the click).
10. Pull the network / stop the node vShell talks to: the module keeps the last figures with a red "!" (bar) or "Last update failed" line (widget).

## v0.15.1
1. Proxmox widget, Show = Guests, Node = All nodes (cluster with 2+ nodes): guests are grouped under a heading per node - status dot, bold node name, "3/5 running" (or "offline" in red) on the right - like the Nodes list. The bar popup's guest list uses the same headings, and is now sorted by node so each node's heading appears once.
2. Node = one node: no headings (just that node's guests).
3. Widget snapping with two monitors: dragging a widget only lines up with / shows guide lines for widgets on the same monitor; widgets on the other monitor are ignored. Screen-edge, centre and bar gaps work as before.

## v0.15.2
1. Proxmox, Show = Storage, Node = All nodes (2+ nodes), widget: shared pools (NFS, Ceph...) first under a "Shared" heading, then each node's own pools under a heading with the node's status dot, bold name and "N pools" (or "offline" in red) - same look as the guest headings.
2. The bar popup's Storage section is grouped the same way.
3. Node = one node: no headings, that node's pools plus the shared ones.

## v0.16.0 - resizable widgets
1. Right-click a widget > Resize (greyed out when the widget is locked; with 2+ widgets Ctrl-picked use a single widget's menu after Clear selection).
2. Resize mode: dashed accent frame, 8 handles, a card in the middle with the size ("260 × auto", row height) and Done / Cancel. Modules can't be clicked while it is on.
3. Cursor changes over the edges / corners (↔ ↕ ⤡) and to the move cross in the middle; dragging the middle moves the widget as usual (with snapping).
4. Left / right edges: width changes live; the opposite edge stays put. Settings > widget > Size > Width shows the new number afterwards.
5. Top / bottom edges (plain drag): the module rows get taller / shorter, so graphs, clocks and icons grow; when you let go the modules are rebuilt at the new row height. "Module height" in Settings matches.
6. Shift held when you start a top / bottom drag: fixed height instead - the box is exactly that tall; content longer than it scrolls (mouse wheel, thin scrollbar on the right). A Proxmox guest list is a good test. Settings > Height shows it; set it back to 0 for automatic.
7. A plain top / bottom drag on a fixed-height widget turns the fixed height off again (back to fitting the modules).
8. Edges snap to other widgets' edges (same monitor), the widget gap, the screen edge gap, the screen centre and the bars, with the guide line. Alt = no snapping.
9. Done (or Enter) keeps the size; Cancel (or Esc) puts back the size and place from before Resize. (Enter / Esc only if the widget has keyboard focus - the buttons always work.)
10. Right-click while resizing: "Finish resizing" in the menu.

## v0.17.0 - updater (GitHub releases of version-au/vshell-releases)
Setup: build.cmd now also writes dist\vShell.exe.sha256. Install this 0.17.0 by hand (older versions have no updater).
1. Settings > System > Updates: "This is vShell 0.17.0", status, Check now / All releases; "Check for updates automatically" (on) and "Include pre-releases" (off).
2. No releases yet: Check now -> "You have the latest version" (or a clear error if the repo isn't public / GitHub is unreachable).
3. Bump the csproj to 0.17.1, build, create release v0.17.1 in vshell-releases with vShell.exe + vShell.exe.sha256 attached.
4. Restart the old 0.17.0 (or Check now): about a minute after start-up a notice bottom-right "vShell 0.17.1 is available" with the notes, Install and restart / What's new / Skip / ✕.
5. Install and restart: progress % on the Updates card (if open); vShell closes and comes back as 0.17.1 within a few seconds (About shows 0.17.1). Settings, bars, widgets unchanged.
6. Shell mode: the same, the bars come back; startup apps are NOT started a second time.
7. Skip: the notice doesn't come back for that version (the Updates card still shows it as "you chose to skip it"; Install still works there).
8. Pre-release: a release with "Set as a pre-release" is only offered with "Include pre-releases" ticked.
9. Wrong checksum (edit the .sha256 on the release): "doesn't match ... wasn't installed", nothing changed.
10. vShell in a folder you can't write to (e.g. Program Files without admin): a message saying so, nothing changed.
11. vShell.old.exe is deleted a couple of minutes after a successful update.
12. Rollback (hard to fake): if the new version quits while starting, the old one comes back with a message, and that version is skipped.

## v0.17.1
1. Run build.cmd: after the build it prints "Release notes: ...\dist\release-notes.md" and "tag: v0.17.1   title: vShell 0.17.1".
2. dist\release-notes.md holds just the 0.17.1 section of CHANGELOG.md (no heading). Paste it into the release description.
3. Bump the version without adding a CHANGELOG section: the file says "(No notes for x.y.z in CHANGELOG.md yet.)" and the build warns in yellow.
4. This release is also the first real update test: publish it with vShell.exe + vShell.exe.sha256 and let the installed 0.17.0 update itself.

## v0.18.0 - taskbar grouping, previews, pinned apps
Settings > bar > Taskbar module: "Combine buttons of the same app" (Always / When the bar is full / Never), "Combined buttons show" (Icon only / Icon and app name / Icon and window title), Button width, Previews (on, size), Pinned apps (show, list with ▲ ▼ Remove, Add a program or shortcut…).
1. Always + Icon only (default): one square icon button per app; 2+ windows show a small count on the icon; a short line under running apps, a longer Main Accent line under the active app.
2. Icon and app name / window title: wider buttons with the text; the count sits next to the icon.
3. Never: one button per window like before (middle-click still closes that window). When the bar is full: separate buttons until they would get narrower than about half the button width, then combined; goes back when there's room.
4. Hover a running button ~0.5 s: live previews above it (below a top bar) - video keeps playing in them. Moving to the next button switches straight away; moving away closes it. Preview size slider works; previews off = tooltips as before.
5. Click an app with 2+ windows: the previews stay open until you click elsewhere; click a preview to switch to that window (minimised ones are restored); ✕ / middle-click closes one (the list updates). Previews off: a plain list instead.
6. Click an app with one window: switches / minimises like before. Shift+click or middle-click: new window of that app.
7. Right-click an app: name, New window, Pin to taskbar / Unpin from taskbar, Move left / right (pins), Restore / Minimize / Maximize / Close window (one window) or Minimize all / Close all N windows.
8. Pin a running app, close it: its icon stays (slightly faded); click it to start it; when it runs, its windows combine onto the pin in the same place.
9. Settings > Add a program or shortcut (.exe or .lnk): appears on every taskbar; ▲ ▼ reorder; Remove. "Show pinned apps" off hides them on that taskbar.
10. Store apps (Calculator, Settings…) group and pin too (pinned as shell:AppsFolder\...). Chrome / Edge profile windows group by their own app id.
11. Progress bars, overlay badges and flashing (attention) still show on the combined button.
12. Two monitors with "Only windows on this bar's monitor": each taskbar groups its own windows; pins show on both.

## v0.18.1 - pins per taskbar
1. Two bars with a Taskbar module: pin an app on bar A - it appears only on bar A. Pin a different app on bar B - only on B.
2. Unpin on A: gone from A; B unchanged. Move left / right only reorders that bar's pins.
3. Settings > bar A > Taskbar module > Pinned apps lists only A's pins (Add / ▲ ▼ / Remove affect only A).
4. Upgrading from 0.18.0: the pins you had appear on the first bar's taskbar only.

## v0.18.2 - progress in the update notice
1. With a newer release published, wait for the notice (or restart vShell), click Install and restart: the buttons are replaced by "Downloading… 42% (31.0 of 74.0 MB)" and a progress bar in the Main Accent.
2. When the download finishes: "Checking and installing…", then "Installed - restarting vShell…" for about a second, then vShell restarts into the new version.
3. The notice stays in the bottom-right corner while its height changes.
4. Make it fail (e.g. edit the release's .sha256): the notice shows "The update didn't install: …" in red with Try again / Close (no separate message box).
5. ✕ while downloading only hides the notice; the download carries on (an error then comes as a message box).
6. Settings > System > Updates > Install and restart still works as before (progress on the card, errors in a message box).

## v0.19.0 - blur + performance
Blur
1. Settings > Appearance > Background > Background blur: Off / Blur / Frosted. Switching it: bars and widgets disappear for a moment and come back (they are made again).
2. Blur / Frosted: what's behind each bar and widget shows softened; lower the background opacity (40-70 %) to see more. Shape: rounded corners as set (two or four), outline included; nothing blurred outside the shape.
3. Widgets have no shadow with blur on; clicking, dragging, snapping, resizing, Ctrl+click selection all still work. Bars: modules, dragging, reserve space as before.
4. Save as preset with blur on, switch blur off, apply the preset: blur comes back. Export / import a theme keeps it. Built-in presets don't change the blur.
5. Off: everything looks exactly as in 0.18.
Performance (should look and behave the same, just lighter)
6. Power flow: dots move smoothly; lock the PC or maximise a window over it -> Task Manager shows vShell's CPU dropping; unlock / restore -> dots move again.
7. Taskbar: a browser / media player whose title keeps changing - buttons don't flicker; hover previews stay put; Title / Never modes still show the new titles.
8. Settings: drag a slider of one widget - only that widget updates; theme colours still update everything; settings.json is written about a second after you stop.
9. CPU / memory / network / disk / battery / volume text and graphs update every second as before; network with two modules shows the same numbers in both; unplug / switch network -> the adapter name updates.
10. HWiNFO module / temperatures: values update as before (first values straight away).
11. Network module with Wi-Fi shown: name and signal %; if the Wi-Fi API fails it falls back to netsh.
12. Home Assistant: entities change straight away when they change in HA (no 5 s wait). Restart HA: vShell keeps the last values, reconnects, carries on. A HA behind a proxy without WebSocket still works (polling).
13. Minimise a window (shell mode): no caption box left on screen.
14. Start-up feels quicker; the build is bigger (pre-compiled). First build downloads the pre-compile tool once.
15. Log: vshell.log never grows past about 1 MB.

## v0.20.0 - themes, bar styles, widget styles
(the 0.18.2 and 0.19.0 tests above still apply - this release ships all three.)
Upgrade
1. Start 0.20 with your existing settings: every bar and widget looks exactly as before. Appearance > Bar style shows each bar with its layout; bars that differ from the first bar of the same layout say "its own look". Widgets that differ from the first widget have their own look too.
Themes
2. Appearance: Theme section at the top shows "Current look (not a theme)" (or the theme in use). Show all: 19 built-in theme cards (one per colour preset), each a picture with its colours, a floating bar at the top, a classic bar at the bottom and a widget.
3. Click Nord Sharp: colours change to Nord, bars that follow the style become Sharp (each for its layout), widgets that follow become Sharp. The card gets the tick; Colours shows Nord in use; Bar style shows Sharp; Widget style shows Sharp. Bars / widgets with their own look don't change.
4. Save as theme…: dialog with name + Include Colours / Bar style / Widget style (each with what it is now). Untick Bar style, save "Test": a ★ Test card. Apply another theme, then Test: colours and widgets come back, the bars keep the other theme's shape.
5. Untick everything -> message, nothing saved. A built-in theme name -> message. Delete theme deletes the selected ★ theme only.
6. Export… -> .vstheme.json with Name, Theme, BarStyle, WidgetStyle. Import it (rename the theme first if you like): appears as a ★ theme and is applied. Import an old (0.19) theme file: works as before (a colour preset).
Bar style
7. Bar style gallery: 12 built-in cards, each showing Classic / Floating / Dock / Minimal. Neon's outline is in the Secondary Accent, Hairline's and Retro's in the text colour (the pictures show it; the outline colour row shows the slot). Click Round: the bars change; the bar pages show the new values.
8. "Edit the look for": Classic / Floating / Dock / Minimal switches the rows below without reloading the page. Dock has no edge gap row; Minimal has no outline rows; Floating's gap starts at 1. "Used by:" lists the bars with that layout.
9. Move a slider: every following bar with that layout changes live; after a moment the gallery shows "Current (not a preset)". Save bar style… -> ★ card, in use; Delete removes it (bars keep their look).
Bars
10. A bar's page: Layout Classic / Floating / Dock / Minimal at the top. Classic -> full width with an outline; Minimal -> full width, no outline; Floating -> only as wide as its modules, gap from the edge; Dock -> no gap. A following bar takes the style's look for the new layout.
11. Change the bar's height / corner radius / edge margin / outline thickness etc.: "Follow the bar style" unticks at once (no page reload); applying a theme / bar style no longer changes this bar. Tick it again: the bar takes the style's look.
12. Full width off / on and Show outline (on a full-width bar) change the layout; the page reloads to show it.
13. "Make every bar follow the bar style" (shown when one has its own look): all bars take the style.
Widgets
14. Widget style gallery: 12 cards; click Bubble: following widgets get round corners, no border, shadow, more padding.
15. Widget look rows (corners, shadow, border, thickness, opacity, colour, padding, spacing) change every following widget live.
16. A widget's page: "Follow the widget style" at the top. Changing corners / shadow / border / padding / spacing unticks it; font size, width, height and opacity don't. Ticking it again applies the style.
17. New bar / new widget: gets the current style straight away.
18. Settings saved and restarted: the style, the themes you saved and each bar's / widget's follow switch are kept.
19. Go through every built-in theme on a dark and the Light preset: bars and widgets look tidy (no clipped outlines, Pill / Cozy corners round at the bar's height).

## v0.20.1 - blur rebuilt, Save Bar / Widget Style, alongside Explorer only
Blur
1. Appearance > Background: Background opacity is the first row; then Style, Gradient colour, Gradient angle, Background blur.
2. Blur: every bar and widget shows the wallpaper behind it, softened, under the theme colour. Lower the opacity to 40-60 % to see it clearly. The part shown lines up with the wallpaper around it.
3. Frosted: the same, a little lighter with a fine grain. No square box at the rounded corners in either mode; corners are smooth; widgets keep their shadow.
4. Drag a widget around: the blurred wallpaper inside it follows (it shows what's behind it at the new place). Move a bar to another edge / monitor: same.
5. Change the Windows wallpaper (and its Fit / Fill / Span setting): within a couple of seconds the blur shows the new picture. Per-monitor wallpapers (Desktop page): each monitor's bars / widgets show their own picture.
6. Switch Off / Blur / Frosted: changes straight away without bars and widgets disappearing.
Styles
7. Appearance has no "Look for each layout" or "Widget look" cards. Bar style and Widget style galleries still apply styles; Delete removes a saved one.
8. A bar's page: Save Bar Style next to Copy settings. Change a floating bar's height / corners / outline (Follow unticks), Save Bar Style "Test": the bar style becomes ★ Test; the bar follows it again; other floating bars that follow the style take the same look; Classic / Dock / Minimal bars don't change.
9. A widget's page: Save Widget Style. Change corners / border, save "Mine": ★ Mine is the widget style; widgets that follow it take it; widgets with their own look don't change.
10. Save as theme with Bar style ticked, after giving a bar its own look: applying the theme later gives bars of that layout that look.
11. Built-in lists: 9 bar styles, 7 widget styles, 19 themes; no Pill / Terminal / Cozy bar styles and no Pill / Hairline / Terminal / Cozy / Ghost widget styles.
Update notice
12. With a release whose notes are long, the notice shows all of it in a box about 230 px high that scrolls with the mouse wheel (no click on the notice needed) and has a thin bar on the right. Headings bold, "- " lines as bullets.
Alongside Explorer
13. System page: no Replace Explorer Shell card. Desktop page: no desktop icons section.
14. If vShell was set as your shell (HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon "Shell" = vShell.exe), starting vShell removes it (see vshell.log).

## v0.20.2 - layout settings, ask before restyling, modules fit slim bars
1. Bar page, Layout Classic: Size has Height + Reserve space only; Look has Font size, Corner radius (two corners), opacity rows; Outline has Thickness / Opacity / Colour (no Show outline switch). No Full width switch, no edge margin, no Round all four corners.
2. Minimal: no Outline card at all; no edge gap; no round all.
3. Floating: Gap from the screen edge (1-30), Round all four corners, Show an outline; untick Show an outline -> the thickness / opacity / colour rows disappear.
4. Dock: no edge gap, no round all; Show an outline as for Floating.
5. Switch a bar Floating (round all on, gap 10) -> Classic: square-edge corners at the screen edge, no gap; back to Floating: gap 8 (or the style's), round all as the style says.
6. Upgrade: a full-width bar that had an edge margin or round-all-corners now sits against the edge with two rounded corners.
7. Give one bar its own look (change its height), then click a theme: a box names that bar (and its layout) and asks to apply the style to it too. Yes: it takes the theme's look and Follow is ticked again. No: it keeps its look. Cancel: nothing changes. Same with a widget, and with the Bar style / Widget style galleries (they only ask about bars / widgets respectively).
8. All bars and widgets following: no question.
9. Tokyo Night Slim (30 px bars) with the Start menu icon size 32: the icon shrinks to fit inside the bar; Launcher icons too. Taller bars: the size you set.

## v0.20.3 - modules shrink to fit the bar
1. A bar with CPU / GPU / RAM (temperature on a second line), HWiNFO fans (two lines), Disk and Network (two lines): apply Tokyo Night Slim (30 px). Both lines show, a little smaller, nothing cut off at the top or bottom; graphs and meters stay inside the bar.
2. Back to a taller style (e.g. Mocha Soft): everything is its normal size again.
3. Still work on the shrunk modules: tooltips, clicks (Start menu, launcher, volume, disk opens), Ctrl+drag to reorder, popups open in the right place.
4. Auto-width (Floating / Dock) bars: the width follows the shrunk modules.

## v0.20.4 - reliable update hand-over
1. Update to 0.20.4 (install and restart): the new version starts (log: "Update: now running 0.20.4"). If the old one is stuck closing, the log shows "the previous vShell is still closing - waiting for it", then "ending the previous vShell (pid …)", and the new one starts - no End task needed; only one vShell in Task Manager afterwards.
2. Then the 0.20.3 tests (modules shrink to fit slim bars).
3. Right-click the bar > Exit: vShell closes; nothing named vShell left in Task Manager after a few seconds.
4. Next update after this one (0.20.4 -> newer): if closing ever takes long, the log names the slow step ("Shutdown: <step> took N ms"), and after 20 s the watchdog ends the old process.

## v0.21.0 - dock animations, keep the dock centred
Setup: a bar with Layout = Dock, a Launcher with names off and a Taskbar with "Combined buttons show: Icon only". The animations are off by default: nothing animates until you tick them in the module settings; then tick Lift, Glow, Magnify and Bounce.
1. Hover a launcher icon: it rises a few pixels and glows in the Main Accent; neighbours grow a little less, icons spread apart; move along the dock: a smooth wave; leave the dock: everything settles back.
2. Grown icons above the dock are fully visible (not cut off at the bar's edge); clicking just above the dock where nothing is drawn reaches the window behind.
3. Click a launcher shortcut: the icon bounces twice while the app starts. Click a pinned taskbar app that isn't running: bounces. Click a running app: no bounce (it just activates). Shift+click: bounces.
4. Module settings > Dock animations: switch each off -> that effect stops (Magnify off -> Lift grows the icon slightly instead). Magnify size 110-250 % changes the size.
5. Launcher: tick "Show names next to shortcut icons" -> the Dock animations rows grey out with "Turn off the names…"; untick -> enabled again. Taskbar: "Icon and app name" -> greyed with "Set Combined buttons show to Icon only…".
6. On a Classic / Floating / Minimal bar (or a widget): greyed with "Only on a bar with the Dock layout", and nothing animates.
7. Top dock: icons grow and rise downwards.
8. Taskbar previews still open on hover and sit next to the button; tooltips, right-click menus, Ctrl+drag of modules still work.
9. Keep centred: bar page (Dock) and right-click > "Keep the dock centred on its monitor". On: the dock jumps to the middle; drag it to the other monitor -> centred there; open / close apps -> stays centred. Off: it stays where you drop it.
10. Maximised windows stop at the dock (only the dock height is reserved, not the room above it).

## v0.21.1 - visible glow, glow settings, Lift or Magnify
1. Dock, icon-only launcher / taskbar, Glow on: hovering an icon shows a clear Main Accent halo behind it (the icon itself stays clear on top) that fades in and out.
2. Glow strength 10 % -> faint, 100 % -> strong; Glow size 120 % -> tight around the icon, 300 % -> wide (reaching past the dock edge, still not cut off).
3. Glow with Magnify / Lift / Bounce: the halo follows the icon as it grows, rises and bounces.
4. Tick Lift while Magnify is on: Magnify unticks (and its size slider disappears). Tick Magnify while Lift is on: Lift unticks.
5. Old settings with both on: Magnify stays, Lift is switched off.

## v0.21.2 - hover box switch
1. Launcher settings: untick "Highlight box behind the icon under the mouse" -> hovering shortcuts / groups shows no box (tooltips, clicks, group menus as before). Tick -> the box is back.
2. Taskbar settings: untick the same -> no box on hover over buttons; the active app's button still has its background; previews still open.
3. Works on any bar and in widgets, with or without dock animations.

## v0.21.3 - icon animations on every layout
1. Classic (full width) bar, icon-only Taskbar: tick Lift / Glow / Bounce / Magnify -> they work like on the Dock; grown icons show above the bar, not cut off.
2. Floating and Minimal bars: the same. Top bars: icons grow / rise downwards.
3. Full-width bar with Magnify: the bar's window doesn't spill onto the next monitor (drag a window across the monitor edge next to the bar - nothing in the way).
4. Maximised windows still stop at the bar's own height; clicking the room above the bar (where nothing is drawn) reaches the window.
5. Launcher / Taskbar with names: Icon animations greyed with the reason; on any layout.

## v0.21.4 - mirrored gradient on bottom bars
1. Appearance > Background: Style Gradient, angle 270 (bottom to top). A top bar fades bottom -> top; a bottom bar fades top -> bottom (mirror images).
2. Angle 0 (left to right): both bars the same. Angle 45: the bottom bar's runs at -45 (mirrored).
3. Drag a bar from the top edge to the bottom: its gradient flips when it lands.
4. Untick "Mirror the gradient on bottom bars": every bar shows the gradient at the set angle. Widgets never change.

## v0.22.0 - faster Start menu, launcher groups in the widget style
Start menu
1. Start vShell, wait ~5 s, click Start: the menu appears at once, full list with icons (no gradual filling in).
2. Close and open repeatedly (also with the Windows key): instant every time; search box empty, list at the top, nothing selected, power list closed.
3. Type a search: results with the match highlighted, first one selected, Enter launches. Clear the search: the full list is back immediately.
4. Install / uninstall an app or add a shortcut to the Start Menu folder: within a couple of seconds it appears / disappears in the menu (even while it's open).
5. Store apps still appear (a few seconds after start-up the first time).
6. Change the theme: the menu shows the new colours next time.
7. Tray popup and launcher group menus: appear in place without a delay or a jump; shadows look the same as before.
Launcher groups
8. Click a launcher group: the menu has the widget style's corners, border (colour / thickness / opacity), shadow and padding, and the theme background (gradient / opacity as the widgets). Rows are spaced by the style's spacing.
9. Change the widget style (e.g. Bubble, Sharp): the next group menu follows it. A see-through background doesn't show a dark shadow through it.

## v0.22.1 - Start menu in the widget style
1. Open Start: corners, outline and shadow match the widgets; the background is the theme background (gradient and opacity like the widgets).
2. Appearance > Widget style: Sharp -> square menu with a thin outline; Bubble -> very round, no outline; Borderless glass -> no outline, no shadow. Each time the next open follows it.
3. The left strip, search box and power list are rounded to match (square with Sharp). The power list has the style's outline.
4. Still instant to open after the change (the menu is rebuilt once for the new style).

## v0.22.2 - Modules card first, new first-start layout
1. Settings > a bar: preview, then Modules ("Edit modules ›"), then Layout, Size, Look, Outline. Same for a widget: preview, Modules, then Widget style, General, Size...
2. First start (rename %APPDATA%\vShell\settings.json first, put it back afterwards): bottom bar = Start, taskbar, gap, tray, clock at the far right.
   - Tray: every icon on the bar, no arrow.
   - Clock: time over date, date in the Secondary Accent colour.
   - Taskbar: one button per app with the window title (Combine: Always, Combined buttons show: Icon and window title).
3. Existing settings: nothing changes on the bars.

## v0.23.0 - widget boxes, snap links, zones, separators, widget header / accent / density, rings, tiles, grid, tabs
Upgrade (keep a copy of %APPDATA%\vShell\settings.json first)
1. Start 0.23.0 over your current layout. Every widget is exactly where it was, the same size. The log has "positions and sizes converted to the box".
2. About 3 s later the log has "N snap links found". Widgets that were snapped together (gap and an edge lined up), or at the edge gap from the screen edge or a bar, are now linked (test 6).
3. Restart: nothing moves, and no conversion line is logged again.
Widget box
4. Change the theme / widget style back and forth (with and without outline and shadow), and set the outline thickness from 1 to 8. The widget boxes don't move. Auto-sized widgets still grow and shrink with padding and font, from their top-left.
5. Width 300 on a widget with a 3 px outline: the box inside the outline is 300 wide. Resize mode shows the box size, and its edges snap to the boxes.
6. Keep the box size: tick it, then change the font size or the widget style. The box size stays the same (content that no longer fits scrolls). Untick it: the widget fits its modules again, and Width / Height are back to 0.
Snap links
7. Drop widget B below A (snapped at the gap, left edges lined up). Raise A's font size or add a module: B moves down and keeps the gap. Shrink A: B moves up. Add C below B: C follows B, which follows A.
8. Snap a widget to the right screen edge, then make it wider: it grows to the left and stays at the edge gap. The same for the bottom edge (it grows up), and below a top bar.
9. Drop B between A (left) and C (right) so both gaps fit: B follows A.
10. Drag B away and drop it in free space: it no longer follows A. Drop B with Alt held next to A: no snap and no link. Drag A away: B stays where it is.
11. Appearance > Widgets: change "Gap between widgets" from 12 to 24. The linked widgets move apart to 24, and widgets at the screen edges / bars move with "Gap from the screen edges". Untick "Re-space snapped widgets..." and change the gap: nothing moves (new drops use the new gap).
12. With a gap that would push a widget off the screen: it stops at the screen edge.
13. Ctrl+click 3 widgets, Align left, then Distribute (snap gap). Grow the top one: the others follow.
14. Snapping off: nothing follows anything (links are kept and come back when snapping is on again).
Zones
15. Bottom bar (Start, taskbar, flexible spacer, tray, clock): tick Three zones. Start / taskbar Left, tray / clock Right; no gap or jump compared with before.
16. Set the clock's Zone to Centre: it sits in the middle of the screen and stays there as taskbar buttons come and go. The taskbar gives way before it reaches the clock.
17. Ctrl+drag the clock into the right part of the bar: it moves to the Right zone (its Zone setting says Right). Ctrl+drag it back to the middle: Centre. Ctrl+drag within a zone reorders as before.
18. A flexible spacer in the Left zone between two modules: it pushes only within the left zone (never into the centre). A fixed 40 px spacer: a 40 px gap.
19. Untick Three zones: exactly the old bar. Tick it again: the zones you set are still there.
20. Floating / Dock bars (not full width): no zones option.
Separators
21. Separator style Dots: as before on the modules with Show separator. Line: a thin line instead. None: nothing.
22. Chevron: the ticked modules have coloured segments (alternating accent / grey), each with an arrow into the next module. With zones, segments don't run across zones.
23. Chips: every module (not spacers) is in a rounded chip, with a little room between chips. Taskbar and tray still work inside them.
24. Apply a theme: the separator style doesn't change.
Widget header, accent, density
25. Show a header row: the first module's icon (in the accent colour), the name, and a chevron. Click the header: the widget collapses to the header, keeping its width, and the widgets below it move up (if snapped). Click again: it opens. Drag the header: the widget moves, no collapse. Locked widget: a click still collapses.
26. Right-click > Collapse / Expand works too. Restart: the collapsed state is kept.
27. Accent edge Top / Left: a 4 px stripe along that edge, clipped to the rounded corners. The content moves 4 px away from it.
28. Accent colour (e.g. green): the stripe, header icon, outline (unless the outline has its own colour) and the widget's graphs are green; the bars keep the theme accent.
29. Density Compact: smaller padding, gaps and rows, and slightly smaller text. Comfortable: as before.
Rings, tiles, grid, tabs
30. CPU Style Ring in a bar: a ring as tall as the bar with "34%" inside, "CPU" beside it (temperature under it if on). Past the warning level the ring, the number and the label turn the Secondary Accent colour.
31. Ring in a widget: as tall as the module row. Memory with used / total GB: the GB under the label. HWiNFO sensor (°C): the number inside, "Label 61°C" beside it.
32. Tile in a widget: label, big number, sparkline across the width, and it's taller than one row (not cut off). Disk space: a meter instead of a sparkline; disk activity: a sparkline. Tile in a bar: label over number with a short sparkline, fitting the bar.
33. Network / Battery / Volume: no Ring / Tile choice.
34. Widget Arrangement Grid, 2 columns, 4 tile modules: 2 × 2 equal cells with the module spacing between them. Columns 3: three across. Row and Stack as before.
35. Each module in its own card: lighter rounded boxes around each module, also in Grid and Tabs.
36. Show the modules as tabs (3 modules): a tab strip with their names (Label text / title when set), only one module is shown, and the widget is the size of the biggest. Clicking a tab switches without the widget changing size. Restart: the same tab is open. With a header, collapsing hides the tabs too.

## v0.23.1 - ring settings, per-style graph size, no Chevron, tab names, Collapsible
1. CPU module, Style Ring: the settings show Ring size, "Show the label beside the ring", the colour and the warning (no graph width). Ring size 50 -> half the bar height, 100 -> full height. Untick the label: ring only (the tooltip still has the text).
2. Line with width 120 / height 40 %: switch to Ring (86 %), set it to 70, then back to Line -> 120 / 40 %. Back to Ring -> 70 %.
3. Style Tile: no Show text, width or height rows; colour and warning are there and work.
4. The rows change straight away when you pick another style (no need to reselect the module). Same for Disk and HWiNFO.
5. Bar Look > Separator style offers Dots / Line / Chips / None. A bar saved with Chevron in 0.23.0 shows Dots.
6. A bar module's Separator section: "Show separator" + "Change separator style in bar look settings."
7. Widget with tabs: select a module -> Tab section with Tab name. Type "Solar" -> the tab reads Solar. Empty -> automatic name again. Without tabs: no Tab section.
8. Header on, Collapsible off (default): no arrow, a click on the header does nothing (a drag still moves the widget), no Collapse in the right-click menu, and the widget is never shown collapsed.
9. Collapsible on: arrow, click collapses / expands, "Collapsed to the header" in settings and Collapse / Expand in the right-click menu.

## v0.24.0 - Notes module
Setup
1. Settings > a widget > Modules > Info > Notes: an empty note "Click to write". Notes is not offered for bars.
Note / Markdown
2. Click the note: the widget comes in front of your windows, a toolbar and a text box appear, typing works straight away. Esc / Done / click on another window: it shows the formatted note and goes back behind your windows (or stays on top if "Keep above other windows").
3. Press on the note and drag: the widget moves (no editing).
4. Toolbar and Ctrl+B / I / L: bold, italic, bullet; Markdown kind also Heading, Code, Link. "- [ ] x" shows a tick box; clicking it ticks it (saved).
5. Start editing with Double-click / Menu: a single click does nothing (drags); double-click or right-click > Edit starts.
6. Markdown kind: # / ## / ### headings, `code`, ``` blocks, [link](https://example.com) opens the browser, > quote, ---, 1. lists.
Checklist
7. Type in "Add an item", Enter: added, the box stays ready for the next. Tick / untick. "x of y done" updates.
8. Double-click an item: edit it in place, Enter saves, Esc cancels, empty text removes it.
9. Hover an item: a handle on the left; drag it up / down: the line shows where it goes, it moves there.
10. Right-click an item > Due date > Today: "Today" in the Secondary Accent colour; Tomorrow / weekday / date; a past date: "overdue". Pick a date… accepts your usual date format. No due date removes it.
11. Done items: Strike / Hide / Bottom. Right-click > Remove done items asks first.
Log
12. Type, Enter: a new entry at the top with the time (accent colour today; Yesterday / weekday / date later). Right-click an entry: Copy / Delete. "Show at most" limits the list.
Stored in vShell
13. Restart vShell: the text is still there. Right-click > New note: a second note, the name row with 1 / 2 appears; arrows and scrolling on it flip between them; Rename; Delete asks first.
File
14. Stored in File > Choose or make a file… > type a new name: "New file - it is made when you add something"; type: the file appears with your text (open it in Notepad).
15. Change the file in Notepad and save: the note updates within a second.
16. Start typing in the note, then change and save the file in Notepad before you finish: "The file changed somewhere else..." with Keep mine / Take theirs / Keep both. Keep both: yours in the file, theirs in "name (conflict date time).md".
17. A checklist file edited in Obsidian / on GitHub keeps its other lines (headings show as small titles over the items).
18. A UTF-8 file with a BOM / CRLF line ends keeps them.
Look and more
19. Paper Yellow: a yellow note with dark text (tick boxes dark); Own: any colour, the text turns light or dark to suit. Font Mono / Handwritten.
20. Width slider; untick Grow: fixed height with a scroll bar. Show when it was last edited.
21. Lock: no editing, no add box, ticks don't change; right-click > Allow editing.
22. Quick-note key on, press Win+Shift+N anywhere: the note's widget comes in front, ready to type (log: the entry box; checklist: the add box). If Windows refuses the key, the log says so.
23. Notes in tabs / with a header / collapsed: the quick-note key expands a collapsed widget first.

## v0.24.1 - accent colour leaves the outline alone
1. Widget with an outline colour left empty, accent colour green, accent edge Top: the stripe is green, the outline is the theme's Main Accent.
2. Outline colour "Main Accent" (@main): also the theme's Main Accent, not green. Secondary Accent / Text / an own colour: unchanged.
3. Header icon and the widget's graphs are still green.

## v0.24.2 - header text colour
1. Widget page, header row off: no Header text colour row. Tick Show a header row: the row appears under the header settings.
2. Pick Secondary Accent: the widget's name turns that colour; the icon keeps the accent colour (same as the accent edge). Empty: the theme's text colour again.
3. Copy / Paste widget settings carries it over.

## v0.24.3 - Ring / Tile polish
1. CPU, Show temperature on, Style Tile in a widget: "CPU · 61°C" with 61°C in the Secondary Accent colour; the line graph spans the whole module width, from its bottom edge up to just under that label, with the big "34%" over it. Resize the widget wider / the row taller: the graph fills it.
2. Style Ring: the temperature under "CPU" is in the Secondary Accent colour too.
3. With Ring or Tile, no "Temperature on a second line" (CPU / Memory / GPU), "Title and value on two lines" (HWiNFO) or "Two lines" (Disk) option; back to Line: they return.
4. Disk Tile (space): still a meter under the number. Memory with GB: "RAM · 19.5/31.9 GB · 45°C".

## v0.24.4 - taskbar in a zone keeps its width
1. Three zones on, taskbar Zone Centre. Restart vShell: once your windows are listed the centre zone is as wide as the buttons need (up to the room between the side zones), buttons at their normal width.
2. Open and close several windows: the centre zone grows and shrinks each time, staying centred; no narrow buttons.
3. Taskbar in the Left / Right zone: the same.

## v0.24.5 - zones in Settings
1. Bar page: "Three zones" sits in Layout (under the layout notes), not in Size. Floating / Dock bars: not shown.
2. Zones on > Edit modules: three columns Left / Centre / Right with the modules of each; the hint says so. Zones off: the usual single row.
3. Drag the clock from Right onto the Centre column: it moves to the Centre zone (the bar updates; its Zone setting says Centre). Drop it on the left half of a module in a column: it goes before that module, in that zone.
4. Drag within a column: reorders as before. An empty column shows "Drop modules here" and accepts drops.
5. Ctrl+drag on the bar into another zone, then reopen Edit modules: the columns match.

## v0.24.6 - Tile sparkline fills the module
1. Bar, CPU Style Tile with temperature: "CPU · 61°C" at the top; the line graph spans the module's whole width and reaches the bar's bottom edge; "34%" sits over it, readable. Slim bar styles: still fits (shrunk evenly).
2. Widget: the same inside the module's row - full width, down to the row's bottom; wider widget = wider graph.
3. Memory, GPU, HWiNFO sensor, Disk activity: the same layout. Disk space: big number with a thin meter along the bottom edge.
4. Warning level passed: the number turns the Secondary Accent colour, the line too.

## v0.24.7 - Tile module width
1. CPU Style Tile: Graph section shows "Module width (0 = as wide as its text)" (no graph width / height). 200: the module is 200 wide on the bar, the line spans it. 0: back to the text's width.
2. Narrow (e.g. 60) with temperature on: the label is cut with "…", the number and line still show.
3. Switch to Line and back to Tile: the width is kept. Same for Memory, GPU, Disk, HWiNFO, and in a widget.

## v0.24.8 - dragging in the zone columns
1. Zones on > Edit modules: press a module and drag - it lifts after a few pixels; drop on another column: its zone changes; drop next to a module: it goes there.
2. Zones off: dragging in the single row still works; clicking a module still selects it.

## v0.24.9 - Tile in slim bars, 30 px minimum
1. Bar height 30-34 (e.g. the Slim style), CPU Tile with temperature: one line "CPU · 61°C 34%" at the top, the line graph under it to the bottom edge; nothing clipped.
2. Height 35-40: label on top, a smaller percentage over the graph; nothing clipped. Taller bars: the same, text no bigger than before.
3. Bar page: the Height slider starts at 30. A bar that was at 24-29 is 30 after updating. Apply the Slim style: its Minimal bars are 30. A bar style / theme you saved with a lower bar: applying it gives 30.
4. Widgets: the Tile looks as in 0.24.8.

## v0.24.10 - rounded clip, equal tab widths
1. Floating bar with round corners (e.g. radius 20, all four corners), a CPU Tile (or Line graph) as the first and the last module: the graph's ends follow the curve; nothing shows outside the bar. Classic bottom bar (top corners round): the same at the top corners.
2. Turn on an icon animation (Magnify) on that bar: magnified icons still reach past the bar (no clip then).
3. Widget with tabs: a Notes module (width 260) and a CPU Tile / clock: switching tabs, each page is as wide as the widest; the note wraps at that width; the clock / tile fill it. A long tab strip: pages at least as wide as the strip.
4. Widget with a fixed width: tabs fill it as before.

## v0.24.11 - corner cut with icon animations on
1. A bar with rounded corners and an icon animation on (e.g. Magnify on the taskbar), CPU Tile at the very start / end: the sparkline's fill follows the curve; nothing shows outside the corner.
2. Magnify / Lift on that bar: icons still grow past the bar's edge (except right inside a corner).
3. Bars without animations: as 0.24.10.

## v0.24.12 - corner cut = the bar's corner
1. Classic bar, height 30, corner radius 20, outline 3, CPU Tile as the first module: the sparkline and its fill stop exactly at the curve inside the outline (no sliver past it). Same at the right end.
2. Radius 8 / 30 / 40 and heights 30-60: always follows the corner as drawn.

## v0.24.13 - Windows' own tray icons
1. Click the volume icon in vShell's tray: a menu with a slider (changes the volume live), Mute / Unmute, the output devices (the current one ticked; pick another to switch), Volume mixer, Sound settings.
2. Right-click it: Open Volume mixer / Sound settings / Sound devices (classic) - each opens.
3. Use the microphone (e.g. Voice Recorder) so the mic icon appears: click - Settings > Privacy > Microphone; right-click - that and Sound settings.
4. Network icon (if shown): click - available networks; right-click - Network settings, Network connections. Battery icon on a laptop: click - power settings.
5. Other apps' tray icons: clicks and right-clicks go to the app as before.
6. Note: the microphone icon is recognised by its tooltip (English, German, French...); if a language isn't recognised it behaves as before - say which.

## v0.25.0 - Run box and Seerr
Run box
1. Add a Run box to a bar (Shell > Run box): a rounded field "Run or !search  Ctrl+Alt+Space". Click it: a box opens under (top bar) / above (bottom bar) it with the cursor in it. Same in a widget.
2. Press Ctrl+Alt+Space with another app in front: the run box opens and takes the keyboard at once. Change the keys to Off: they stop working.
3. Empty box: your recent entries and a line listing the bangs. Up / Down: walks the history in the box.
4. `!g dog pictures` + Enter: Google in your default browser. `!yt` alone: YouTube. `!amazon lego`: DuckDuckGo hands it to Amazon. Untick the DuckDuckGo option: `!amazon lego` becomes a normal web search.
5. Type `note`: Notepad (and other matches) suggested with icons; Enter starts the first. Tab fills in the highlighted name.
6. `C:\Windows`, `%appdata%`, `github.com`, `ms-settings:display`, `shell:startup`: each opens.
7. `regedit` / `notepad`: start. `asdfgh`: not a program, so it's searched on the web.
8. `>ipconfig /all`: a command window with the output, left open.
9. `=12*7.5`: "= 90" shown while typing; Enter copies 90 (paste somewhere). `=2^10`: 1,024. `=abc`: says it isn't a sum.
10. `dog pictures`: Search Google...; set the engine to DuckDuckGo / Bing: those instead.
11. Bang list: add `r | Reddit | https://www.reddit.com/search/?q={q}`, click away; `!r cats` works. "Put back the built-in list" restores it. Clear the run history empties it.
12. Esc or clicking elsewhere closes the box.
Seerr
13. Integrations > Seerr: address + API key, Test connection: "Connected - Seerr x.y.z". Wrong key: says it was refused.
14. `!s The Terminator` + Enter: results with posters, year, Movie / TV show and the status in colour. Hover a result: its overview.
15. A movie not requested: Request -> "Requested ✓"; check Seerr's Requests page. Something already available: no Request button; Open in Seerr opens its page.
16. A show: Request… -> All seasons ticked, the seasons listed (ones you have / requested greyed). Untick All and tick S2 -> Request: only season 2 is requested.
17. User id set to another user's id: the request shows under that user in Seerr (if your Seerr allows the admin key to request for others).
18. Keyword changed to `seerr`: `!seerr dune` searches; the run box tooltip and hint follow.

## v0.25.1 - Alt+Space for the run box
1. Run box settings > "Open it from anywhere with": Alt+Space. Press Alt+Space over any app: the run box opens, ready to type.
2. Set it back to Ctrl+Alt+Space: Alt+Space opens the window menu again.
3. If another program (e.g. PowerToys Run) already has Alt+Space, vShell's log says Windows refused it - turn it off there or pick other keys.

## v0.26.0 - Type in the run box, centred box for the keys, custom search engine
1. Run box on a top bar: only a search icon in the field. Click it: the cursor is in the field, the suggestions drop down under it. Type `note`: the list updates under the box as you type.
2. Enter on a suggestion: runs it, the drop-down closes and the field empties. Esc: same without running. Click another window: same.
3. Bottom bar: the list opens above the field. Widget: under it, and the widget comes in front while typing; clicking away puts it back on the desktop.
4. Up / Down, Tab, history, `=sum`, `>command`, `!g`: as before. `!s dune` + Enter: Seerr results in the drop-down; Request / Request… (season checkboxes) work and typing still goes into the field.
5. While typing on the bar, other apps' windows aren't hidden and the bar doesn't jump. After Esc, clicking the bar's other modules doesn't take focus from your app.
6. Press the keys (e.g. Alt+Space) with another app in front: a box opens centred on the main monitor, a third of the way down, with the cursor in it. Esc / click elsewhere / Enter closes it. Pressing the keys again while open just focuses it.
7. Run box settings > Plain search with: Custom -> an address box appears. Enter `https://searx.example.com/search?q={q}`; `dog pictures` shows "Search searx.example.com for dog pictures" and opens that address with `dog%20pictures`. A one-word unknown name falls back to the same engine. Switch back to Google: the address box hides (the address is kept).
8. Custom with an empty address: searches Google.

## v0.26.1 - Run box closing, arrow keys, widget style
1. Press the run box keys: the centred box opens with the cursor in it (type straight away). Without clicking it: click the desktop or another window -> it closes. Open again, Alt+Tab -> it closes. Open again, press the Windows key -> it closes.
2. Empty box: history listed, top one highlighted. Down: the highlight moves down the list; Up: back up. Enter runs the highlighted one. Same with typed text and its suggestions.
3. Widget style with a big corner radius, thick coloured outline, shadow: the centred box, and the drop-down from a bar run box and a widget run box, have the same corners, outline, shadow, padding and theme background (gradient / opacity). Change the widget style: the next opening follows it.
4. Bottom bar: the drop-down still sits just above the field, aligned with its left edge; top bar: just under it.

## v0.26.2 - Run box drop-down spacing
1. Top bar (try a thick one, e.g. 48, and a slim one, 30): click the run box - the drop-down's card starts 10 px below the bar's bottom edge (outline included), its left edge lined up with the field.
2. Bottom bar: the card ends 10 px above the bar's top edge.
3. Floating bar with a margin from the screen edge: same 10 px from the bar itself.
4. Run box in a widget: the card is 12 px under the field; the field and its outline stay fully visible, no shadow over it.

## v0.27.0 - Now playing progress bar and Jellyfin
1. Spotify (or YouTube in Edge/Chrome) playing: a thin accent bar under the title fills as the song plays, smoothly (updates every half second). Pause: it stops. Next track: starts from 0.
2. Click halfway along the bar: the song jumps to about halfway. Drag along it: the fill follows, hover/drag tooltip shows the time; release jumps there.
3. Module settings > Progress bar: Along the bottom -> a thin bar along the bottom edge of the module, full width. Off -> none. "Show the time": `1:23 / 4:56` before the buttons.
4. Click the art: the bigger card has a 4 px bar with position on the left and length on the right, moving live; click it to jump.
5. A player that gives Windows no times: no bar (nothing broken), the rest as before.
Jellyfin
6. Integrations > Jellyfin: address + API key (Dashboard > API Keys), Test connection: "Connected to <server> (Jellyfin 10.x) - N playing now". Wrong key: "refused the API key".
7. Play music in the Jellyfin desktop app: the title shows (as before) and now the bar too. The card says "· times from Jellyfin". Click the bar: Jellyfin jumps (the bar may take a couple of seconds to settle as the app reports back).
8. Pause in the app: the bar stops within ~2 s.
9. If the bar doesn't appear: check Dashboard > Devices for this PC's name; enter it as the device name. (Matching is by title first, so it usually works without it.)
10. Play something from Jellyfin on another device (TV) with the same title as nothing here: not shown unless that device's name is entered.
11. If Windows has no media session for the app (Now playing says Nothing playing) but the Jellyfin app on this PC plays: it is shown from Jellyfin with the cover; play/pause, next, previous work through the server.
12. Jellyfin server off: Now playing keeps working from Windows alone; one line in the log, it tries again every 15 s.

## v0.28.0 - Downloads: qBittorrent and SABnzbd
Setup
1. Settings > Integrations > qBittorrent: address of the Web UI (http://server:8080), user + password, Test connection: "Connected - qBittorrent v5.x, N torrents". Wrong password: "wrong user name or password". (Several wrong tries make qBittorrent ban the address for a while - it says so.)
2. With "Bypass authentication for clients in whitelisted IP subnets" on in qBittorrent and the user name empty here: connects without a login.
3. SABnzbd: address + API key, Test: "Connected - SABnzbd 4.x, N in the queue". Wrong key: "SABnzbd refused the API key".
Bar module
4. Add Downloads to a bar: `↓ x MB/s  ↑ y MB/s   2 · 47%` with a line graph. Arrows in the download / upload colours. Tooltip lists both clients.
5. Styles: Bars, Meter (speed vs. full scale, default 50 MB/s), Ring (overall queue progress, "47%" inside), Tile (big speed number, MB/s, sparkline, "2 · 47%" under it).
6. Show: qBittorrent only / SABnzbd only - the numbers follow. Hide when nothing is downloading: disappears when both queues are empty.
7. Click: popup in the widget style. Each client: name, speeds, Pause / Resume all, turtle (alt speed: highlighted when on), globe (web page). Downloads below with progress bars.
8. Click a downloading torrent: shows "pausing…" then paused (grey). Click again: resumes. Same for a SABnzbd job. Pause all: everything pauses; the header says Paused.
9. Turtle: qBittorrent's alternative speed limits switch (check in its Web UI).
10. Lists: Active / Unfinished / All; "How many per client" 3 -> "+ N more" (click opens the web page).
Widget
11. Downloads in a widget: speed graph along the top (scale shown top-right), then the list; updates every second or two. "In the widget: Graph" -> the bar look instead.
Notices
12. Let a torrent finish: notice bottom-right "✓ name · Finished · qBittorrent · category". A SABnzbd job finishing (after unpacking): the same. Two at once: one notice, two lines. Click a line: opens that client's page.
13. Categories "movies,tv": a finished "iso" torrent gives no notice.
14. A SABnzbd job failing: "✕ name · Failed: <reason>". Turn failed notices off: none.
15. Remove the module from all bars and widgets: notices still come (up to 30 s late).
Run box
16. `!qb` -> torrents listed with % and state, newest state every 2 s, summary line with speeds. `!qb ubu` -> filtered. Enter on one: pauses / resumes it (status says so), box stays open.
17. `!sab` -> the SABnzbd queue.
18. Paste a magnet link: "Add to qBittorrent" first, "Open it instead" second. Enter: "Added to qBittorrent ✓", box closes, it appears in qBittorrent. Paste https://.../x.nzb: "Add to SABnzbd" - appears in the queue.
19. Server off: module shows "Downloads n/a" (or the last numbers with "Last update failed" in the popup); retries every 15 s; one line in the log.

## v0.28.1 - Drop .nzb / .torrent files
1. Drag an .nzb from File Explorer over a run box on a bar: the outline turns the accent colour and the drop-down says "Drop to add it…". Move off: it goes away.
2. Drop it: "✓ name.nzb added to SABnzbd" in the drop-down for ~4 s; the job appears in SABnzbd's queue (named after the file).
3. Drop a .torrent: "added to qBittorrent", it appears in qBittorrent.
4. Drop three files at once (2 .nzb + 1 .torrent): three lines. Drop a .txt: the cursor shows "not allowed", nothing happens.
5. Same on a run box in a widget, and on the Downloads module (bar: notice bottom-right; widget list: notice, and the list highlights while dragging).
6. SABnzbd not set up: dragging an .nzb shows "not allowed".
7. Drop while typing in the run box: the result replaces the suggestions; typing again brings them back.

## v0.29.0 - Komodo
Setup
1. Komodo: Settings > API keys > create; vShell Settings > Integrations > Komodo: address, key, secret, Test connection: "Connected - Komodo x.y: N servers, N stacks, N deployments, N of N containers running, N procedures." Wrong secret: "Komodo refused the API key (401)".
Bar
2. Add Komodo to a bar: green dot, "12/14 running". Stop a stack in Komodo's own UI: the count drops within ~5 s; an unhealthy container: red dot and "⚠ 1"; the tooltip names it.
3. A stack with a newer image: "⬆ 1". A server offline: "✕ 1 server", red dot.
4. Click: popup list grouped by server; Komodo button opens the web UI.
Actions
5. Click a running stack: menu with Restart / Stop / Pause / Redeploy / Pull images / Destroy / Show the services / Open in Komodo. Restart -> asks -> Yes: "restarting…" on the row (amber), back to running after.
6. Stop: asks, "stopping…", then stopped (grey) - and no "stopped" notice. Start: starts without asking.
7. A deployment not deployed: Deploy (no question). A plain container (Show: Containers): Start / Restart / Stop / Pause.
8. Stack arrow: services with their states (sonarr down etc.). Click a service: Restart just that one - only that container restarts in Komodo's Updates.
9. Stack with "⬆": menu says "Pull and redeploy (update)"; after it, ⬆ goes away.
10. Procedures: last run "2h ago"; Run -> asks -> "running…"; when it finishes a notice "✓ Procedure backup finished"; a failing one: "✕ Procedure backup failed".
Widget
11. Komodo in a widget: the same list; Which: Problems -> only what isn't right ("No problems - everything is running." when all good). Server: one server only. Group by server off: one list with the server after each name.
Notices
12. docker stop a container on the server (not from vShell): "✕ Stack media is exited/stopped" (or unhealthy) notice; start it: "✓ … is running again". Server off: "✕ Server nas can't be reached".
13. Downloads notice and Komodo notice at the same time: Komodo's sits above the downloads one.
Run box
14. `!ko` -> problems + procedures. `!ko jelly` -> matching items with kind and state. Enter -> actions; Enter on Restart -> "Press Enter again: restart media" (highlighted); Enter -> "Komodo is on it ✓". "Back to the list" returns. Typing more goes back to the list.
15. Older Komodo (1.x, ListAllDockerContainers): containers still listed.

## v0.29.1 - Info popup on the Integrations page
1. Settings > Integrations: the header under the title reads "Connections to Local and Web services".
2. Home Assistant: no paragraph under the heading any more; "Access token" has a small (i) after it.
3. Hover the (i): it turns the accent colour and a card appears under it with the long-lived token instructions, in the settings' light / dark colours.
4. Move the mouse onto the card: it stays. Move away from both: it goes after a moment. Switch the settings theme light / dark: the card follows.

## v0.29.2 - More (i) help on the Integrations page
1. Proxmox VE: no paragraph under the heading; hover the (i) after "Token secret": the token / Privilege Separation / PVEAuditor / PVEVMUser instructions.
2. Seerr: no paragraph under the heading; (i) after "Run box keyword": "Search Seerr from a run box with !s Movie Title, ..." (with your keyword); (i) after "API key": Settings > General.
3. Jellyfin: under the heading "Gives the Now Playing module a progress bar and allows you to seek from a bar or widget."; (i) after "API key": Dashboard > API Keys.
4. Each card stays while the mouse is on it and follows the light / dark settings theme.

## v0.29.3 - (i) help for qBittorrent, SABnzbd and Komodo
1. qBittorrent: under the heading "Drag torrents, or add magnet links into the run module to add downloads into qBittorrent."; (i) after "User name" with the login hints; (i) after "Run box keyword": "Use !qb in run box to see current torrents." (your keyword).
2. SABnzbd: no paragraph under the heading; (i) after "API key": Config > General > API Key.
3. Komodo: no paragraph under the heading; (i) after "API key (K-…)": how to create the key and secret; (i) after "Run box keyword": the !ko usages on separate lines.

## v0.30.0 - Import pins, drag to reorder, per-bar blur, guest uptime
Import
1. Taskbar module settings > Pinned apps > Import pins from the Windows taskbar…: the list shows Windows' pinned apps in its order (Explorer, browsers, Store apps like Calculator with their icons). Untick one, Import: the rest appear on vShell's taskbar after the existing pins.
2. Import again: everything already there is greyed "already there". Replace: the pins are replaced, in Windows' order.
3. Start an imported app: its window groups onto the pin. Unpin it from Windows' taskbar: the vShell pin still starts it.
4. Application launcher settings > Import pins…: same list; shortcuts added to the launcher; Replace keeps groups.
Drag
5. Taskbar: drag a pin left / right: an accent line shows the spot; drop: it moves (and stays after restart). Clicking a pin still starts it; a drag doesn't start it.
6. Drop a .exe / .lnk from File Explorer between two pins: pinned there. Dropping one already pinned: nothing added.
7. Launcher on a bar and in a widget: drag a shortcut to a new spot; drop files from Explorer in the middle: they land there.
8. Tray: drag an icon along the bar: line shows where; drop: it moves; restart vShell: the order stays. Drag from the hidden-icons popup onto the bar at a spot: pinned there. "Back to Windows' order" resets.
9. Ctrl+drag a module: still moves the module.
Blur
10. Appearance blur Off; one bar's Look > Background blur: Frosted -> only that bar frosted. A widget: Blur -> only that widget. "As the theme": follows the Appearance setting again.
11. Save as theme, change the blurs, apply the theme: the saved blurs come back.
Proxmox / Komodo / SAB
12. Proxmox module (Guests) > "Show each running guest's uptime": a column "3d 4h" in the widget / popup.
13. Komodo module > untick "Show the Open Komodo button": the button is gone from the widget / popup.
14. Integrations > SABnzbd shows the drag-nzb note.

## v0.30.1 - Menus close on click-away, bar over full-screen with the Start menu
1. Right-click a bar, a widget, a taskbar button, a tray icon area: the menu opens. Click the desktop: it closes. Open again, click another program's window: closes. Click another vShell bar / widget: closes. Esc: closes. Alt+Tab: closes.
2. Clicking an item in the menu (and opening a sub-menu) still works.
3. Full-screen game / F11 video on a monitor with a bar: the bar is hidden. Press the Windows key: Start menu opens and its bar shows over the game. Click into the game: menu closes, bar hides again.
4. Same with a bar module's popup (e.g. clock / Proxmox) clicked... (only reachable when the bar shows) - the bar stays over the app while the popup is open.
5. A borderless full-screen game: works as above. An exclusive full-screen game (rare now) may minimise when the menu takes the focus - that's the game, not vShell.

## v0.30.2 - Store app icons on the taskbar
1. Start WhatsApp (Store version): its taskbar button shows the WhatsApp icon (no dark box). Right-click > Pin to taskbar: Settings > Taskbar > Pinned apps shows its path as shell:AppsFolder\5319275A.WhatsAppDesktop_…!App. Close WhatsApp: the pin keeps its icon; click it: WhatsApp starts and its window lands on the pin.
2. A WhatsApp pin made with 0.30.1 or older (path in C:\Program Files\WindowsApps): after starting 0.30.2 the path becomes shell:AppsFolder\… by itself (log: "Taskbar: pin of … now uses the Store app …") and the icon shows.
3. Same for new Teams / new Outlook / Windows Terminal if you have them.
4. Ordinary programs (Chrome, Explorer, Steam): icons and grouping unchanged.
5. Calculator / Photos (older Store apps): unchanged, icons with transparent corners.

## v0.31.0 - Seerr module, notices, !s requests
Set up Seerr on the Integrations page first (address + API key); an admin key shows Approve / Decline.
Bar
1. Add "Seerr requests" to a bar. The film icon shows; with requests pending a badge with the number; "N processing" after it. Hover: counts and the 5 latest requests.
2. Module settings: untick the badge -> "N pending" as text instead; tick "Show the number available" -> added.
3. Click: popup opens on Requests (or the view chosen in "Popup opens on").
Requests
4. Posters, title + year, "Movie" / "TV · S1-3", who asked, "2h ago", coloured status. Filters All / Pending / Processing / Available / Mine change the list (Mine = your requests; hidden if Seerr doesn't say who the key is).
5. A pending request: ✓ approves (shows "approving…", then "Approved ✓", and moves out of Pending within ~30 s). ✕ asks, then declines.
6. Click a request: opens it in Seerr in the browser. Right-click: menu with Approve / Decline / Open / Copy the title; the popup stays open while the menu is up.
7. A non-admin API key: no buttons on pending ones.
Other tabs
8. Recently added: posters of the newest library items with "added 2d ago". Click a poster: opens in Seerr.
9. Trending / Upcoming: "Loading…" briefly, then posters. Request on a movie: "requesting…" -> "Requested ✓"; it appears under Requests. Request on a show: menu with All / Only the latest / each season / Choose in Seerr…
10. Module settings: Posters off -> plain lists with dots. "How many" changes the number. Widget: tabs off -> only the chosen view; filters off -> only the chosen filter.
Notices
11. Have someone else request something (or request with another user): within ~30 s (3 min without a module) a "Seerr" notice "New request: …" bottom-right; click opens it.
12. One of your requests finishes downloading: "Now available: …". Setting Everyone: also other people's. Off: none.
13. Notices from downloads / Komodo / Seerr at once stack above each other.
Run box
14. `!s` + nothing: pending requests (amber icons). Enter: Approve / Decline / Open in Seerr / Back. Enter on Approve: "Approved … ✓" and it leaves the list. Decline: first Enter shows "Press Enter again", second declines.
15. No pending: the latest requests with their status; Enter opens one in Seerr.
16. `!s recent`: recently added; Enter opens. `!s dune`: Enter still searches as before.
17. Wrong API key: bar says "Seerr n/a" / popup "Can't reach Seerr: Seerr refused the API key (403)".

## v0.31.1 - Seerr popup position and default view
1. Seerr on a bottom bar: open the popup, click Trending, Requests, Recently added: the popup keeps sitting just above the bar, centred on the icon, at every size. Same on a top bar (just below it).
2. Other popups that change size (Komodo opening a stack's services, Proxmox): also stay against the bar.
3. Module settings > Popup opens on: Trending. Click the icon: Trending. Click another tab, close, open again: Trending again.
4. Requests to show: Pending. Open the popup: the Pending filter is selected.
5. Widget with tabs on: change View in Settings: the widget switches to it. Click its tabs: they switch. Change Requests to show: the widget's filter follows.

## v0.31.2 - Seerr widget size
1. Seerr widget with tabs and filters on, another widget snapped below / beside it. Click through Requests, Recently added, Trending, Upcoming and each filter: the widget stays the same size and the snapped widget doesn't move.
2. Trending loading for the first time may grow the widget once; after that it stays put.
3. A request gets approved and leaves Pending: the widget doesn't shrink.
4. Change "How many" or turn posters off: the widget resizes to fit the new settings, then stays that size.
5. Tabs off: only the chosen view, sized to it.

## v0.31.3 - Font drop-downs
1. Settings > Appearance > Font family: a drop-down showing the current font. Open it: every installed font, each drawn in its own face; scrolling is smooth.
2. Pick one: bars and widgets change straight away. Type "cons" with the list closed: jumps to Consolas; Enter applies it.
3. Type a name that isn't installed and press Enter: saved; bars fall back to the default font (no crash).
4. Save a theme, change the font, apply the theme: its font comes back and the drop-down shows it after reopening Settings.
5. Notes module > Font: Theme / Mono / Handwritten first, then the fonts. Pick e.g. Georgia: the note uses it. Back to Theme: the Appearance font.
6. A settings file from an older version (NoteFont Mono / Handwritten): unchanged.

## v0.31.4 - Font box text
1. Appearance > Font family: the box shows the current font's name. Pick another: its name shows. Click into the box, type "Cons", Enter: Consolas, and the name stays.
2. Notes > Font: shows Theme (or Mono / Handwritten / the font picked).
3. The other drop-downs in Settings (not editable) look and work as before.

## v0.32.0 - Notifications page, history, more sources
Page
1. Settings: "Notifications" under Integrations. Integrations no longer has notification sections (a note points to the new page).
2. Show a test notification: a card bottom right. Corner Top left: the next one top left. Monitor Mouse: it opens on the monitor the mouse is on.
3. Seconds 3: it goes after 3 s (not while the mouse is over it). Show an important one: stays until ✕ (Keep important ones on); off: goes like the others.
4. Cards One card: test + a download finishing land on one "Notifications" card, each line with its source. By source: separate cards stacked.
5. Sound on: the Windows notification sound plays.
Quiet
6. Start a full-screen game / F11 video, trigger a notification (test button via a second monitor, or a download finishing): no card. Leave full screen: it shows within ~2 s.
7. Quiet hours covering now: held; untick: they show. Windows Do not disturb on (+ "also when Windows says"): held (may not catch every case).
History module
8. Add Notifications to a bar: bell, badge with the unread count. Hover: latest 5. Click: history grouped by Today / Yesterday; closing it clears the badge.
9. Click a Seerr / Komodo / download one: opens its page. Right-click: Mark as read / Remove / Copy. Clear empties it. Restart vShell: history still there.
10. Widget with the module: the list; Show = Komodo: only Komodo ones.
Sources
11. Home Assistant > Rules… > Add from Home Assistant: pick a light, When Becomes, on. Turn the light on: "<name> is on". Clicking it Toggle: the light switches.
12. A rule Below 5 for 1 minute on a power sensor: notifies only after a minute below 5; once until it goes above again.
13. Notifications sent to vShell: tick it (a token appears), Windows firewall asks - allow on private. From another PC: curl -X POST http://<pc>:8765/notify -H "Authorization: Bearer <token>" -d "{\"title\":\"Hi\"}" -> {"ok":true} and a card. Wrong token: 401. The rest_command example works from HA.
14. Proxmox: tick nodes / guests. Stop a VM in Proxmox's web UI: "VM 101 name stopped". Stop one from vShell: no notification. Storage at 80 with the slider at 70: one notification.
15. Service status: tick it; stop a watched service: "X is down" after two checks; start it: "X is back up".
16. System: CPU limit just under the current temperature (HWiNFO running), 5 s: notification; not again within the cooldown. Disk free 99 %: each fixed drive once.
17. Jellyfin: tick start; play something on a TV: "<user> is watching <title>". Playing on this PC (the Jellyfin device on Integrations): nothing.
18. Weather: tick rain with a Weather module set to a place; when rain is forecast within the hour a notification "Rain expected around HH:mm" (at most every 3 hours).

## v0.32.1 - Default layout, history window, module groups
1. Rename %AppData%\vShell\settings.json (keep it!) and start vShell: the bottom bar has zones on - Start on the left, the taskbar centred on the screen, bell + tray + clock on the right. The top bar is as before. Put your settings file back afterwards.
2. Settings > Bars > that bar: Zones is ticked; the modules show their zones.
3. Settings > Notifications > Open the history (with a few notifications): Mark all read and Clear are fully visible.
4. Add module: Notifications is under Shell, Service status under Info; Smart home & more has no Notifications / Service status.

## v0.32.2 - Important notifications separate
1. Cards By source, Keep important ones on, 5 seconds. Show a test notification, then Show an important one: two vShell cards stacked. After 5 s the test one goes; the important one stays until ✕.
2. Two test notifications a few seconds apart: one card, two lines; each line goes 5 s after it came in; the card goes with the last.
3. Mouse over a card: its lines wait; move away: they go about 2 s later.
4. One card: test + important: one card; the test line goes after 5 s, the important line stays.
5. Keep important ones off: the important one goes like the others.

## v0.33.0 - Notification kinds, colours, buttons, times
Kinds
1. Settings > Notifications: three test buttons. Normal: green bar, goes after its time. Important: yellow bar + yellow title, own card, stays until closed. Alert: red, own card, stays.
2. Untick "Important ones stay": the important test goes like a normal one. Same for alerts.
3. History (bell popup / widget): each line has its coloured bar and icon; read ones are dimmer. Bell badge: red with an unread alert, yellow with an unread important one, otherwise green.
4. Home Assistant > Rules: a rule from 0.32 that was ticked Important shows Kind Important. Set Kind Alert: it notifies in red.
5. curl ... -d "{\"title\":\"x\",\"level\":\"alert\"}": a red card. "important": true: yellow.
Colours / buttons
6. Komodo, Proxmox, downloads, service dots, Seerr: green / yellow / red the same shades as the notifications.
7. Widget style corner radius 20: pills in Komodo / Seerr / Downloads / the history and Seerr's tabs get rounder; radius 0: square. Widget outline on: pills have a thin outline.
8. Run box !s dune: Request / Open in Seerr look like the other pills; "Requested ✓" green; an error red.
Times
9. Notifications history: "5 min ago", "Yesterday 2:05 PM". Hover: "Saturday 17 October 2026, 11:45 PM".
10. Appearance > Font and times > Times 24-hour: "Yesterday 14:05". Clock modules unchanged.
11. Seerr requests "3 hr ago"; Komodo procedure "Yesterday 3:00 AM"; Service status hover "Checked 2:05:09 PM"; Weather "Updated 2:05 PM"; Uptime hover "Started Mon 9:12 AM"; Notes "edited 5 min ago".

## v0.33.1 - Widget corners
1. Widget style: corner radius 0, outline 8, shadow on, Frosted: the outline's corners are clean (no dark marks).
2. Radius 40 (padding 8): the header icon sits clear of the curve; the collapse arrow on the right too. Radius 10: the header looks as before.
3. Collapse the widget at radius 40: icon and arrow still clear of the corners.

## v0.34.0 - Moving taskbar line
1. Taskbar module > Active window line > Slide. Click between apps on the taskbar (and Alt+Tab): the line glides from button to button, also over a long distance; with titles shown it stretches / shrinks to each button's width.
2. Stretch: on a long move the line first stretches towards the new button then shrinks onto it. Fade: fades out, then in under the new button.
3. Speed 60: nearly instant; 1000: slow. Bounce None / Soft / Springy: smooth stop / small overshoot / wobbles.
4. Click the desktop: the line shrinks away; click an app: it grows under it.
5. Open / close windows while one is active: the line follows its button as the buttons shift.
6. Tick "background moves along": the highlighted box slides with the line; untick: each button has its own background again.
7. Off: as in 0.33 (the line jumps). Icons-only dock bar: the line is the short centred one, and the dock animations still work.
8. Two bars with taskbars ("only this monitor's windows"): the line on the other monitor's bar shrinks away when the active window is elsewhere.

## v0.34.1 - Moving line defaults
1. Start 0.34.1 over 0.34.0 settings: every taskbar's Active window line shows Stretch, 400, Soft, background moves along ticked.
2. Change one to Fade, restart vShell: it stays Fade.
3. Add a new Taskbar module: Stretch / 400 / Soft / background on.

## v0.34.2 - Rename
1. Taskbar module settings: the section reads "Active window line animation".

## v0.35.0 - Add modules with previews
1. Settings > a bar > Modules: no module selected; "Add a module" shows a row per module with a live preview on a strip in this bar's look. CPU / memory / network graphs move.
2. You have a Weather / Proxmox / Seerr module somewhere: its preview shows your data. Without Komodo set up: its row says "Needs the Komodo connection".
3. Search "clo": only Clock. Chip Smart home & more: only those.
4. Click Clock (row or Add): it's added after the module you came from (or at the end) and its settings open. "+ Add modules" goes back; clicking the open module in the strip again also goes back.
5. Bar with zones: "Add to" Centre, add Clock: it appears in the Centre column.
6. A widget > Modules: cards in a 3-column grid with each module as it looks in the widget (forecast, Proxmox meters, Seerr posters …). Click one: added, settings open.
7. "+ Add bar": the new bar is empty and its Modules page opens on the gallery. "+ Add widget": same.
8. Clicking a preview never operates the module (the run box, the bell, Now playing buttons): it adds it.
9. Open a module's settings: the previews stop (Komodo / Seerr polling slows back down).

## v0.35.1 - Preview tiles, drawn
1. A bar > Modules: tiles in rows of three, each with a drawn picture on a strip in the bar's background colour, name and description under it. Click: added, settings open.
2. A widget > Modules: cards with drawn widget pictures (forecast, meters, posters, checklist, power flow …).
3. Change the theme colours: the pictures follow (accent, text, background).
4. Search and categories, Add to zone, + Add modules: as in 0.35.0.

## v0.35.2 - Start menu preview
1. A bar > Modules: the Start menu tile shows the three-dot v mark in the accent, secondary accent and text colours, next to "Start".
2. Change the theme colours: the v mark follows.

## v0.36.0 - Taskbar animations
1. Settings > the taskbar > "Moving to another window": try each new style, switching between apps far apart and next to each other:
   - Shrink and grow: shrinks to the middle of the old button, grows out of the middle of the new one.
   - Hop: arcs up over the buttons in between and lands (with Bounce Soft / Springy it overshoots).
   - Comet: a short trail fades behind it.
   - Liquid: gets thinner while stretched out, slightly thicker as it settles.
   - Dot: becomes a round dot, rolls, opens out.
   - Windows 11: the old app's line shrinks back to its short line; the new app's grows from its short line.
2. Click the desktop: the line shrinks away in every style; click an app: it grows back.
3. Open a new window (not pinned): its button grows in and the buttons after it slide over. Close it: it shrinks away and the others close the gap. Nothing flickers or jumps.
4. Full-width taskbar: opening / closing a window makes all buttons resize smoothly.
5. Drag a pin to a new place: the buttons slide into their new order.
6. The active line stays under its button while buttons slide (no restarts or lag).
7. A program that flashes for attention (e.g. a chat message while it's in the background): with Pulse its button pulses slowly; Steady = a flat colour as before. Clicking it stops the pulse.
8. Untick "Buttons grow in…" and "Buttons slide…": everything jumps into place as in 0.35.
9. CPU stays low when nothing is moving (Task Manager), apart from a pulsing button.

## v0.36.1 - Fewer line styles, sliding rearrange
1. Taskbar > "Moving to another window": the list is Off, Slide, Stretch, Fade, Shrink and grow, Hop, Liquid. A taskbar that was on Comet / Dot / Windows 11 is now on Stretch.
2. Drag a taskbar pin two places along: the buttons in between slide over and the dragged one glides into its slot (no jump).
3. Launcher: drag a shortcut to another place: same sliding. Drop a file onto the launcher: the icons after it slide along to make room.
4. System tray: drag an icon along the bar: the others slide. Drag one out of the hidden-icons popup onto the bar: the bar's icons slide to make room.
5. On a Dock bar with grow / lift on: rearranging still slides and the hover grow keeps working afterwards.
6. Untick "Icons slide…" (launcher / tray) or "Buttons slide…" (taskbar): they jump into place as before.

## v0.37.0 - Widgets sliding away at the screen edge
1. A widget not snapped to an edge: right-click shows "Slide away at the screen edge" greyed out, with a tooltip to snap it first.
2. Drag a widget against the left edge (it snaps). Right-click > "Slide away at the left edge". Move the mouse away: after about 0.6 s it slides off to the left.
3. Push the mouse against the left edge level with where it was: it slides back in with a small bounce. Move away: it slides off again.
4. Snap a widget into the top-right corner. Right-click > "Slide away at the screen edge" > the menu asks "Which way should it slide?" with Up and Right. Pick Up: it slides up; the top edge brings it back. Right-click > "Slides away" switches to Right.
5. Settings > the widget > "Slide away at the screen edge": Fade, Slide and fade, speed 1000 vs 100, bounce None / Soft / Springy, wait 0 vs 3000 all take effect.
6. While it's out: drag it (it doesn't slide away mid-drag), open its right-click menu, open a popup from one of its modules, type in a note module: it stays out until you're done and the mouse leaves.
7. A desktop widget ("Keep above other windows" off) with a maximised window over it: pushing the mouse to the edge brings it out in front of the window; when it slides away it goes back behind.
8. Hidden: clicking where it was reaches the window / desktop underneath.
9. Two monitors side by side, widget snapped to the shared edge: when it slides away it doesn't appear on the other monitor. (Bringing it back needs the mouse to stop at that edge, which is harder on a shared edge - move slowly.)
10. Drag the widget away from the edge: it stops sliding away and stays put (the setting is kept for when it's snapped again).
11. "Stop sliding away" in the right-click menu: it comes straight back and stays.

## v0.37.1 - Top programs width
1. A bar with Top programs between two modules: tick "Fixed width" (starts at 160). As the busiest program changes, the modules either side stay still.
2. Narrow it (e.g. 80): long names end in "…". Widen it: there's empty room at the end, no stretching.
3. List layout with 3 programs: every line is cut to the same width.
4. Untick: back to fitting the text.

## v0.37.2 - Names cut, services copy / paste, settings tidy
1. Top programs with Fixed width 120 on a bar, Programs shown 2 (Row): long names end in "…" but both values (e.g. 12.4%) are whole. Sort by Memory: the sizes are whole too.
2. Same with List layout: each line's name is cut, its value is whole.
3. Service status > Choose what to watch: pick two rows with Ctrl+click > Copy ("2 services copied"). Open another Service status module's list > Paste: the two are added. Paste again: "Everything copied is already in this list."
4. Copy with nothing picked: the whole list is copied. Restart vShell, open a list: Paste is enabled and works.
5. Bar settings > Layout: the description (Classic / Minimal / Floating / Dock) is under the Layout box, then the Follow the bar style tick.
6. Widget settings: General, Layout (Modules, Columns, cards, tabs, Density, Follow the widget style), Header and accent, Size, Look, Outline ("Show an outline"), Slide away at the screen edge. No "Move to top-left" button.

## v0.37.3 - Typing slider values, edge glow, preview fix
1. Any slider in Settings (e.g. a widget's Padding): hover the number (a faint underline), click it: the number is selected. Type 14, Enter: the slider jumps to 14 and the widget updates. Type 999: it goes to the slider's maximum. Type abc or press Esc: nothing changes.
2. Appearance > Background opacity: the number (with %) can be typed in the same way.
3. A widget sliding away at the left edge: once it's away a soft accent glow runs along the left screen edge, as tall as the widget. Push the mouse there: the widget comes back and the glow fades.
4. Change the widget's accent colour: the glow follows. Untick "Accent glow…": no glow.
5. Start a full-screen game / video: no glow over it.
6. With the widget slid away, open its settings page: the live preview still shows it.

## v0.37.4 - Normal / Important / Alert per notification
1. Settings > Notifications: under each source's ticks there are rows of three boxes (Normal / Important / Alert, green / yellow / red stripes). The default one has the accent frame: failed downloads, problems, down and system warnings on Alert, the rest on Normal.
2. Service status: set "Down shows as" to Important. Make a watched service fail: the card is yellow (and stays, with "Important ones stay" on). Back up: Normal / green.
3. Komodo "Updates available" set to Important: an update notification is yellow; a container going unhealthy is still red.
4. Weather rain set to Alert: the rain notification is red and the bell's badge turns red.
5. Restart vShell: the choices are kept. Click the default box again: it goes back to following the default.
6. Add a Climate control module to a bar: pick your climate entity, then Zones… > Add from Home Assistant: your 4 zone fans. The bar shows the mode icon (coloured), room temperature and → target. Scroll: the target changes, and after a moment the AC follows.
7. Click it: the bubble is tinted in the mode colour. − / + change the target. Click the round icon: the AC turns off (grey); again: back to its last mode. The mode button lists only your AC's modes; the fan button its fan speeds.
8. Zones: drag along a zone slider to 7: shows "7 / 10" and the fan entity goes to 70 %. Click its icon: Off; again: back to 7. Scroll on it: one step. Changes made in Home Assistant show within a few seconds.
9. Put the module in a widget: the same card, as wide as the widget.
10. Click a Clock: the month calendar opens (today circled). ‹ › and scrolling move months, clicking the top date comes back, clicking a faded day of the next month goes there. Settings > the clock > Weeks start on Sunday / week numbers: the grid follows. Untick "Click the clock…": clicking does nothing.
11. Komodo popup: right-click a stack > Show log: the log window opens with the last 100 lines of all services; the service button picks one. Follow: new lines appear, kept to the bottom. Filter "error": only those lines. Timestamps: the times appear. Copy: pasted text matches. Opening the same log again brings the window forward.
12. Right-click a stack's service > "Show this service's log"; a container > Show log; a deployment > Show log.
13. Run box: `!ko immich` > Enter > Show log. `!ko logs immich` > Enter: opens the log straight away.
14. Climate card: compare with a Bubble Card climate card in Home Assistant: same proportions (rounded row, icon circle, name / state, − 24° + pill), the mode colour behind the row while it's on.
15. Home Assistant module > tick "Bubble Card buttons" (in a widget, Layout List): a switch shows as a rounded button; click it: the accent fills it, again: back. A light: a slider; drag to 40 %: the light dims, the fill shows its colour; click the icon: off / on. A sensor: name over its value, no click.
16. Choose entities… > a light > Bubble Card button = Switch: it now toggles on click instead of sliding.
17. Layout Row in a widget: buttons side by side at "Button width"; on a bar: side by side, as tall as the bar (one line when the bar is slim).
18. CPU module, Meter style: Warning on, threshold 70, fade 15. Load the CPU (or set the threshold to 20 / fade 10 to see it at idle): the meter is the accent up to about 55 %, blends to the Secondary Accent by 70 %, solid after it.
19. Same with Bars and Line: the bars / line are the accent low down and turn the Secondary Accent from the fade up; only the peaks change colour.
20. Ring style: the ring's colour blends as the value moves through 55 - 70 %, and the number in the middle follows.
21. Battery (warn below 20, fade 10): the meter is the Secondary Accent at 0 - 20 %, blends back to the accent by 30 %.
22. HWiNFO sensor 0 - 100 °C, threshold 80, fade 10 (°C): blends from 70 °C. A Home Assistant entity graph with a fade in its unit: the same.
23. Fade 0: the whole graph switches colour past the threshold, as before.

UniFi (needs a console with Network and / or Protect, and API keys from Settings > Control Plane > Integrations in each app):
24. Settings > Integrations > UniFi: enter the address (https://192.168.1.1), the keys, Test: it lists the site, the device count and the camera count. A wrong key says so.
25. Add UniFi Network to a bar: the dot, ↓ / ↑ and the client count; the tooltip lists the devices. Turn off the speed / clients in its settings: they go.
26. Click it: Devices with CPU, memory, uptime, clients. Click a switch: its ports, lit ones in the accent, ⚡ on PoE. Clients: search a name, an IP, a MAC.
27. Connect a new phone: NEW next to it, and a "New client" notification. Unplug an access point: "offline" notification (Alert), and "back" when it returns.
28. Run box: !u (offline / new), !u lounge (matches), Enter copies the IP.
29. Add UniFi Protect to a widget: the camera grid, refreshing at the set seconds; columns 1 - 6; "Only these cameras" with two names in reverse order shows just those, in that order.
30. Click a camera: the big view refreshes every 2 s. Open in Protect opens the camera page; Play in VLC opens the stream (without VLC: the link is copied); Copy stream link. A PTZ camera: Home and the presets move it.
31. A floodlight: click its pill, it turns on; click again, back to its own mode.
32. Walk past a camera with person detection: a notification with the snapshot, the event in the module's list (with a thumbnail) and on the bar for an hour. Ring the doorbell: an Important notification. Turn a type off, or set the camera filter to a different camera: no notification.
33. On a bar: unplug a camera, the count goes red (5/6).

## v0.37.5 - Notification buttons, storage warning, graph warning colour
1. Settings > Notifications: every "… show as" row has Normal / Important / Alert as joined buttons like Corner, in green / yellow / red (the chosen one filled); clicking one sticks after reopening Settings.
2. System notifications: "Storage fuller than (%)". Set it just under a drive's current use: a "Drive C: is almost full" notification with "% full". If you had the old free-space setting on, it shows as 100 minus it.
3. CPU module settings > Graph: Graph colour, Warning colour, Change colour past threshold, Threshold - no Warning section, no fade setting.
4. Threshold 20 at idle: the graph fades into the Secondary Accent from 10 % and is solid past 20 %. Pick a red Warning colour: graph and text use it.
5. Same on memory, network, battery (warn below), HWiNFO sensor and a Home Assistant entity graph (its warning colour is in the entity's edit form).
6. UniFi Network widget: click the search box, type, click the desktop, click the box again: you can type again. Esc clears, Esc again lets go.
7. Show = Devices: just devices, no tabs; Clients: just clients; All: the tabs. Internet speed on: ↓ / ↑ at the top of the widget.
8. Add UniFi Protect to a new empty widget: it starts at 600 × 400. Click a camera: a picture straight away, then sharper ones every 2 s.
9. A new Protect module has no light buttons until "Show the lights" is on.
10. Climate: zones two to a row, showing 0% - 100%. Click a zone's fan icon: off; click again: back on at its last opening.

## v0.37.6 - Slide away turns off when moved
1. A widget snapped to the bottom, sliding away: drag it up off the edge and let go. It stays visible, and the right-click menu offers "Slide away at the … edge" again (it's off).
2. Snap it to the top: it doesn't slide until you turn it on from the right-click menu; then it slides up, cleanly.
3. A widget in a corner (top and right), sliding right: drag it along the right edge to the bottom-right corner - still snapped right, it keeps sliding right.
4. Ticking "Slide away" in Settings for a widget that isn't snapped to an edge: it un-ticks itself.
5. UniFi Network widget, Show = All, on Clients: change Rows to show in Settings - it stays on Clients.
6. UniFi Network widget settings: no internet speed toggle. The bottom line shows "Internet ↓ … ↑ …" in the Main / Secondary Accent on both tabs. On a bar, ↓ is the Main Accent.

## v0.37.7 - Wider warning gradient
1. CPU module, Meter, threshold 30 with "Change colour past threshold" on, load the CPU to ~40 %: the meter blends from the graph colour at 10 % to the warning colour at 30 %, solid after.
2. Bars / Line: only the parts between 10 and 30 % blend; peaks past 30 % are the warning colour. Unticked: no warning colour at all.
3. Disk module, Ring, threshold 70, a drive at ~73 %: the ring is the graph colour to about 50 %, blends to the warning colour by 70 %, and only the last bit is solid warning colour. No seams between pieces.

## v0.37.8 - Climate widget width at start
1. The climate widget (auto width), locked: close and reopen vShell - it comes up at its normal width (a moment wider at most while Home Assistant connects, then back).
2. Drag a zone slider: the fill still grows from the left with a round end; 0 % no fill, 100 % full.
3. UniFi Network: click the gateway to show its ports, restart vShell: the ports are still showing. Click it again, restart: closed.
4. UniFi Network: open a switch - a 1 Gbps device green, a 100 Mbps one amber, a 2.5 G / 10 G uplink blue / purple; the key under the ports lists just those speeds.

## v0.37.9 - Gentler warning fade
1. CPU, Line, threshold 65: lines under 45 % are the graph colour; 45 - 65 % pick up the warning colour gradually (barely at first); 65 - 75 % finish the blend; above 75 % solid. No visible hard edge.
2. Same with Tile, Bars, Meter and Ring; HWiNFO sensor with its threshold in °C (the blend spans 20 % / 10 % of its range).
3. Battery warn below 20: solid warning colour under 10 %, blending back to the graph colour by 40 %.

## v0.37.10 - Warnings on by default
1. After updating: a CPU module that had no warning now has "Change colour past threshold" on, threshold 70, "Automatic" under it. One you had set (e.g. 65) keeps 65 and says "Your own threshold".
2. HWiNFO sensor with range 0 - 2000: threshold 1400. Change "full at" to 1000: the threshold box shows 700 straight away.
3. Type 1500: "Your own threshold"; change the range - it stays 1500. Empty the box: back to automatic (70 % of the range).
4. Untick "Change colour past threshold", restart vShell: still off.
5. New battery module: warns below 20 %.
6. Home Assistant module > Choose entities > add a sensor: double-click it - "Change colour past threshold" ticked, threshold 70 (range 0 - 100, "Automatic"). Set full at 5000: threshold 3500. Type 4000: kept. Empty it: back to 3500. Entities added before the update are unchanged.

## v0.37.11 - Wait before saying it's down
1. Settings > Notifications: a "Wait before saying it's down" slider under Komodo, Proxmox and Service status.
2. Service status, wait 60: stop a service for 30 s and start it again - no notification. Stop it for 2 minutes - "is down" about a minute after the second failed check, then "is back up" when it returns.
3. Proxmox, wait 60: restart a VM (back within a minute) - nothing; stop it - "stopped" after a minute.
4. Komodo, wait 60: restart a stack - nothing; stop a container by hand - "is exited" after a minute; start it - "running again".
5. Wait 0: everything as before.
6. UniFi, wait 120: restart an access point - no notification if it's back within 2 minutes; unplug it - "offline" after 2 minutes, "back online" when it returns.
7. Seerr widget with posters on: resize it wider - more posters per row, rows filling the width; narrower - fewer per row, no posters cut off. Text under the posters trims to the poster width.

## v0.37.12 - More kinds of webhook
1. An app's webhook with a JSON body {"title":"Test","message":"works","source":"MyApp"} and the bearer token: a card "Test" under MyApp.
2. Same app with a text body "{{title}}\n{{body}}": first line as the title, the rest as the message.
3. curl with -H "Transfer-Encoding: chunked", with -X PUT, and with a form body: all show a card. A wrong token: "wrong or missing token".

## v0.37.13 - Notifications on any monitor
1. Settings > Notifications > Monitor: Main monitor, Monitor 1, Monitor 2 … and "The monitor the mouse is on". Pick Monitor 2, press Test: normal - the card shows on monitor 2 in the chosen corner.
2. Unplug that monitor: cards show on the main one; the setting says "A monitor that isn't connected".

## v0.37.14 - Clearer status colours
1. Settings > Notifications: Test normal / important / alert - green, gold, red, easy to tell apart. The Normal / Important / Alert buttons use the same three.
2. Service status dots, Komodo / Proxmox states, a Komodo log with errors and warnings: the new green / gold / red.

## v0.37.15 - Seerr posters keep their rows
1. A sized Seerr widget with 16 posters in 2 rows: switch to a theme with bigger text - still 2 rows, fewer posters, the widget stays the same height. Switch back: 16 again.
2. Make the widget wider: more posters per row, still 2 rows. Change "How many" to 24: the rows are worked out again (e.g. 3).
3. Komodo widget: open a stack with several containers - the widget stays the same height and the list gets a scrollbar; close it - the scrollbar goes.

## v0.37.16 - Keep widget size in the menu
1. Right-click a widget: "Keep widget size" under Resize. Tick it, change theme - the widget keeps its size. Settings shows "Keep the box size" ticked.
2. Untick it: the widget sizes itself to its modules again.

## v0.37.17 - Match Windows to vShell
1. Settings > Appearance > Windows colours: tick "Match Windows to vShell's theme". A dark vShell theme: Windows goes dark; its accent (Settings toggles, File Explorer selection) becomes the Main Accent.
2. Switch to a light preset: Windows goes light. Change the Main Accent: Windows' accent follows.
3. Untick "Accent colour" only: Windows' own accent comes back, dark mode stays. Untick the main switch: your original light / dark mode and accent are back.

## v0.38.0 (Build 2) - S / M / L on bars
1. Bar module settings: "Text size" S / M / L instead of Font; the bar has no Font size. Existing modules came across as S (or M / L as above).
2. Set CPU to M on a 40-tall bar: two lines (temperature under it). On a 30-tall bar: shows as S.
3. Change the font (Settings > Appearance) to a narrow and then a wide one: module widths stay about the same.
4. Fill a bar with modules and long graphs, switch to a theme / font that needs more room: graphs shorten first, then text gets smaller; nothing spills off the bar. Switch back: everything returns to as set. (The log says "too full by … - stepping down".)
5. Widgets look and size exactly as before.

## v0.38.1 (Build 2)
1. A bar module's settings: "Size" S / M / L with an info icon (hover for the description). Add a new module: it's M.
2. Clock with Time over date at S: one line; M: two lines. Weather, UniFi, Protect, climate at M on a 40-tall bar: two lines.
3. Top programs, List, 3 programs, on a 30-tall bar: fewer lines if they don't fit; fixed width stays the same at S / M / L.
4. Network module > Style: Text, Meter, Bars, Line, Ring, Tile.
5. Downloads: untick and tick "Show the upload speed" - "↑ 0 B/s" shows even when nothing is uploading.

## v0.38.2 (Build 2)
1. Close and reopen vShell: bar text and graphs are their full size straight away.
2. Clock: Layout lists Time only, Time and date, Date and time, Large time - small date, Date only. Date and time at S: "Tue 20 Oct  7:15 PM" on one line; Large time - small date at L: big time, smaller date after it.
3. Taskbar module settings: Text size right under Settings, above the icon animation.

## v0.38.3 (Build 2)
1. A bar with zones: pick a module, change its Zone - it moves on the bar and in the module list below the preview.

## v0.38.4 (Build 2)
1. Widget module settings: Text size S / M / L at the top; no Font section; the widget has no Font size.
2. Switch theme / font: widgets keep their size (apart from padding / outline differences).
3. A widget with Keep widget size on, made small: graphs shorten and text steps down instead of scrolling (lists still scroll when needed). Make it bigger: back to as set.
4. Graph length in a widget's CPU module changes the graph's length, as on a bar.

## v0.38.5 (Build 2)
1. A widget (Compact too) with Proxmox, Home Assistant entities, Notes: S / M / L each change the text size; rows get taller for M / L.
2. Power flow in a wide widget: a sensible size, bigger at L, smaller at S.
3. Widget settings: no Padding slider; Comfortable / Compact change the padding.
4. Seerr widget, posters on: Columns 4, How many 12 - three rows of four, the widget sized to fit.

## v0.38.6 (Build 2)
1. Close and reopen vShell: widgets with Keep widget size / a set size show their text and graphs at full size once loaded (no permanent S).
2. Make such a widget too small: it steps down; make it bigger again: back to as set.

## Presets with S / M / L text (0.38.11)
1. Apply the Sharp, Ghost and Hairline bar styles in turn with a module set to M - it stays two lines on each.
2. Apply Slim - M modules show as one small line (bar too short), and go back to two lines when another style is applied.
3. Open a Proxmox / Komodo popup on a bar and a widget header after switching themes and fonts - text size stays the same.
4. Settings > Styles: the widget style previews all have the same inner padding.

## Match Windows to vShell's theme - no lag (0.38.11)
1. Turn on Match Windows (mode and accent). Switch between a dark and a light theme - vShell changes at once, Windows (Start, Explorer) follows about half a second later.
2. Drag the Main Accent colour picker around - vShell stays smooth; Windows' accent catches up once you stop.
3. Restart vShell with matching on - Windows doesn't flash back to your own colours on start-up.
4. Turn matching off - your own Windows colours come back.

## Match Windows on by default (0.38.12)
1. Update an install that had matching off - after start-up Windows' mode and accent follow vShell's theme, and the setting shows on.
2. Turn it off, restart vShell - it stays off and your own Windows colours are back.

## Seerr poster count and height (0.38.13)
1. Seerr widget, posters on, How many 2, Rows 1 - two posters.
2. How many 9, Rows 2 then Rows 1 - the widget gets shorter, no empty space under the posters.
3. Resize the widget wider and narrower - the height follows the posters each time.
4. Switch between tabs - the widget doesn't jump in height.

## Seerr widget View: All or one (0.38.14)
1. Seerr widget, View = All - the four tabs show; the widget is as tall as the tallest tab.
2. View = Upcoming, posters on, Rows 1 - no tabs, just the posters, no empty space under them.
3. View = Requests - the list with its filters (if ticked); "Requests to show" is in Settings.
4. View = Trending - "Requests to show" and the filters tick are hidden in Settings.
5. An existing widget with "Show the tabs" off comes up on its old view; with it on, View shows All.

## Widget menu without Hide (0.38.15)
1. Right-click a widget - no "Hide this widget" at the bottom, and no empty separator line left behind.

## Seerr poster arrows (0.38.16)
1. Seerr widget, posters on, View = Upcoming, How many 5 - arrows either side; the left one is faded.
2. Click › - the next 5 posters; ‹ is now bright. Keep going to the end - › fades and the last page is full.
3. Wait for a data refresh (or switch tabs and back) - it stays on the same page.
4. Click an arrow - the widget doesn't start dragging, and its size doesn't change.
5. How many 40 with fewer titles than that - no arrows.
6. Bar popup on Trending - the arrows work there too.

## Seerr tab tick boxes (0.38.17)
1. Seerr widget: untick Requests and Recently added - tabs for Trending and Upcoming only; "Opens on" lists just those two.
2. Untick Trending too - just Upcoming, no tabs, and the widget shrinks to it.
3. Try to untick the last one - it stays ticked.
4. Requests unticked - "Requests to show" and the filters tick are hidden.
5. Bar Seerr popup: untick a tab - it's gone from the popup's tabs.
6. An existing widget that was on a single view comes up with only that tab ticked.

## Clock without size / colour options (0.38.18)
1. Clock settings: no Text size slider; under Colours only "Date in the Secondary Accent colour".
2. A clock that had seconds / separators in the accent colour - the time is now all the text colour.
3. A clock or text label that had Text size at another % - it's back to its S / M / L size; S / M / L still change it.

## Segoe UI only, Time format (0.38.19)
1. Settings > Appearance: no font picker; "Time format" is the last section, after the widget styles.
2. With another font picked before updating - bars, widgets and popups are all Segoe UI.
3. Notes modules still have their own font choice ("Theme" = Segoe UI).

## Start button icons (0.38.20)
1. Start menu settings > Icon: eight previews in the current theme's colours; vShell mark is selected on a new Start module.
2. Click each - the bar's Start button changes straight away, at the set icon size, and stays sharp from 16 to 48.
3. Change the theme - the Start icon and the previews follow it.
4. Custom icon: the Browse / Clear row appears; with no file the bar shows the vShell mark; pick an .ico or .exe - it shows, and its preview shows it too.
5. An existing Start module with a custom icon comes up on Custom; one without comes up on vShell mark.
6. No icon colour row any more.

## Start menu text size (0.38.21)
1. Start module settings: no "Show the word Start" tick; a note says S / M / L sizes the menu text.
2. Set S, open the menu - small text; M - medium; L - large, and the menu is wider.
3. A Start button that showed "Start" before is now just the icon.

## Notes synced with vDrop (0.38.22)
1. Notes widget > Stored in: vDrop. Within a few seconds vDrop's notes show (flip with the arrows); no status line under the note.
2. Edit a note in vShell - within a couple of seconds the same note in vDrop shows the change.
3. Add a note in vDrop (web or phone) - it appears in vShell at once, at the front.
4. Edit a note in vDrop while not typing in vShell - vShell updates. While typing in vShell, it waits until you finish.
5. Right-click > New note, type - it appears in vDrop. Rename - vDrop's title changes. Delete note - gone from vDrop too.
6. Pin to the front - the note moves first, and is pinned in vDrop; pin one in vDrop - it moves first here.
7. Mark a note done in vDrop - it leaves vShell.
8. Stop Home Assistant (or unplug the network): the footer says vDrop is offline; edit a note; bring it back - the edit reaches vDrop.
9. Restart vShell offline - the notes still show.
10. With a non-admin token - the footer says the token must belong to an administrator. Without the add-on - it says there's no sensor.vdrop.
11. Switch the widget back to vShell - its own notes are still there.

## Home Assistant widget width after a disconnect (0.38.22)
1. A widget with the Home Assistant module (names on). Restart Home Assistant (or stop it for a minute).
2. While it's down the tiles show n/a (and maybe entity ids) and the widget may widen.
3. Once it's back and the values return, the widget shrinks back to its usual width within a few seconds.

## Right-click on a sliding widget (0.38.23)
1. A widget set to slide away at a screen edge (e.g. Notes). Bring it out and right-click it - the menu opens and stays open.
2. Leave the menu open for 10 seconds - it stays (it used to vanish within a couple of seconds).
3. Pick an item - it works; the widget slides away later as usual once the mouse leaves.

## vDrop feed in Notes (0.38.23)
1. Notes widget on vDrop: tabs All / Links / Media / Notes at the top, Inbox on the right, a search box, the feed, and the add box at the bottom. It opens scrolled to the newest item.
2. A link shared from the phone: shows the device name, "via Chrome" (or the app), Today at h:mm pm, the link, then the preview card with the site, title, description and picture; the picture appears a moment later the first time, at once after that.
3. A YouTube link: the picture has a play button; clicking opens it in the browser.
4. Tabs: Links shows only links, Media only images / videos / YouTube, Notes only notes. Inbox list: Pinned, Done, Everything filter the same way.
5. Search narrows it as you type; Esc clears it.
6. Type a note in the add box, Enter - it appears at the bottom ("not synced yet" briefly) and in vDrop. Paste a URL - it becomes a link and its preview fills in.
7. Double-click a note, change it, Ctrl+Enter - changed in vDrop. Esc cancels.
8. Right-click an item: Pin moves it to the Pinned section; Mark done takes it out of the inbox (shows under Done); Delete asks, then removes it from vDrop too; Copy link / Open link work.
9. Add / change / pin something in vDrop's own page - it shows here within a second or two.
10. Restart vShell with Home Assistant off - the feed and its pictures still show.
11. Hover an item: a small button bar appears at its top right (and the row lights up). Open, Copy (tick for a second), Pin (turns accent-coloured), Done, Edit, Refresh (links), Delete - first click turns it red, second deletes; moving off the item cancels it.

## vDrop Archived tab and fill (0.38.24)
1. Tabs read All, Links, Media, Notes, Archived; the list on the right offers Inbox, Pinned, Everything (it's hidden on the Archived tab).
2. Hover an item: the archive-box button archives it - it leaves the inbox and shows on the Archived tab, where the button puts it back.
3. In vDrop's own page the same item shows as archived.
4. A widget with a fixed height (e.g. 680): the feed reaches the bottom of the widget, the add box at the bottom; resize the widget - it follows.
5. A widget without a fixed height: the feed is the module's Height.
6. Notes settings on vDrop: Kind shows only Note; no "Show when it was last edited", "Start editing with" or Lock.

## vDrop Inbox tab and list (0.38.25)
1. Tabs read Inbox, Links, Media, Notes, Archived. On Inbox and Archived there's no list on the right.
2. On Links / Media / Notes the list offers Inbox, Pinned, Archived - e.g. Links + Archived shows only archived links.
3. Adding something while on Archived (or Pinned) goes back to the Inbox so you see it.

## Closing a notification card marks it read (0.38.26)
1. Send two notifications (e.g. the webhook test). The Notifications module shows 2 unread.
2. Close the card with ✕ - the unread count drops by 2 and they show as read in the history.
3. Let another one time out on its own - it stays unread.

## Monitors unplugged and back (0.38.27)
1. Start vShell with all monitors connected (so each bar / widget learns its monitor).
2. Unplug (or turn off) a second monitor: its bars and widgets come over to the main monitor.
3. Plug it back in: within a few seconds they go back to where they were on it - including widgets snapped to its edges or to each other.
4. Restart vShell while the monitor is unplugged, then plug it in - they still go back.
5. While it's unplugged, drag one of its widgets somewhere on the main monitor - that one stays on the main monitor after it's back.
6. vshell.log has a "Monitors changed: N connected" line for each change.

## vDrop notifications (0.38.28, with vDrop 1.4.0)
1. In vDrop > Notifications > Apps & tokens, add an app and copy its URL.
2. With vShell running: `curl -X POST "http://HA-IP:8486/message?token=TOKEN" -H "Content-Type: application/json" -d "{\"title\":\"Test\",\"message\":\"Hello\",\"priority\":5}"` - a Normal card in vShell, and it's in vDrop's tab (unread).
3. Close the card with ✕ - in vDrop it turns read. Send another, mark it read in vDrop - it's read in vShell's history.
4. Priority 9 - an Alert card. Priority 2 - no card, but it's in the history.
5. Close vShell, send five, start vShell - one "5 notifications while this PC was away" card; all five in the history.
6. Phone: set the notify service in the add-on; with vShell running nothing goes to the phone; quit vShell, wait 3 minutes, send one - it arrives on the phone.
7. Settings > Notifications: turning "Show notifications from the vDrop add-on" off stops them.

## Notifications: repeats, badge, history (0.38.29)
1. Send the same webhook notification three times within a minute: one card whose title ends "×3", updated each time; the history has one entry "×3" at the top.
2. Set "Count repeats as one within" to 0 - three separate entries again.
3. Send an Alert: the bell turns red and the badge glows; the next Alert makes it bounce. An Important one (no Alert unread): yellow bell, steady badge.
4. Open the bell list: ✓ on an unread one marks only that one; closing the list leaves the rest unread. History opens the history window.
5. History window: type in search - it narrows; Unread; turn Normal off - only Important and Alert; pick a source; Mark read / Clear act on what's showing; with 300+ kept "Show 150 more" appears.
6. Settings > Notifications > History: "How many to keep" goes to 5000; an install that had 100 now shows 500.


## Wallpapers and notification monitor (0.38.30)
1. Settings > Desktop > Wallpaper: tick "Let vShell set the wallpapers". The monitor map shows your layout. Click monitor 2, Browse a picture - only monitor 2 changes.
2. Fit = Fit on a portrait picture: try Blur, Black, Ocean, Sunset, and Gradient (change From / To / Angle) - the edges update a moment after each change, and the tile previews match.
3. Drag a picture from Explorer onto a monitor tile - it's set. Drag a folder - a slideshow starts; Next picture changes it; set 5 min and wait.
4. Same on all: one picture on every monitor, each at its own size. Span across all: one picture over the whole desktop.
5. Unplug a monitor and plug it back in (or turn it off/on): it gets its own picture back, not another monitor's.
6. Restore my original wallpapers: Windows' wallpapers come back exactly as before (check Windows Settings > Personalization > Background shows them).
7. Notifications > Cards > Monitor: pick monitor 2. Change the display order / reconnect so Windows renumbers it - cards still show on that same monitor. With a card open, unplug and replug - it moves back.

## Wallpapers: colour / gradient with no picture (0.38.31)
1. Each monitor: pick a monitor, Clear its picture, pick Sunset - that monitor is a full sunset gradient; the others are unchanged.
2. Gradient: change From / To / Angle - the whole monitor follows.
3. Same on all, no picture, Ocean - every monitor is the gradient. Span across all, no picture, Gradient at 0° - one gradient flows left to right across all the monitors (the map shows it).
4. Back to Blur with no picture - the note shows, and that monitor gets its own old wallpaper back.

## Background effect on popups (0.38.32)
1. Appearance: Blur on, background opacity about 60 %. Open the start menu - blurred wallpaper behind it, theme colour over it. Frosted: the fine grain shows too.
2. Try the launcher, tray, clock calendar, bell list, Seerr / Downloads / Komodo popups, and the run box drop-down and its hotkey box.
3. A module's settings > Popup > untick "Background effect behind its popup" - that popup is back to the plain, almost solid background; the others keep the effect.
4. A bar with its own Blur set to Off (Appearance on): its popups have no effect. A widget with its own Frosted (Appearance off): its popups are frosted.
5. Send a notification - the card is blurred; open the history - blurred. Settings > Notifications > untick the background effect - both plain again.
6. Move a monitor's notification setting so cards open on another monitor - the blur matches that monitor's wallpaper.

## Gradient outline and glass edge (0.38.33)
1. Settings > Bars > a bar with an outline (Floating or Dock: tick Show an outline, thickness 2-3). Outline > Colour style: Gradient - the outline goes from Main Accent to Secondary Accent.
2. Set From / To to other colours, move the Angle - it follows. Opacity still fades it.
3. Tick "Turn slowly around the outline": the colours rotate; try Slow / Fast. Watch CPU in Task Manager - it should stay low.
4. Back to Solid - the outline is one colour again, exactly as before.
5. Look > Glass edge: Subtle, then Strong - a light top line, darker bottom line, faint sheen. Try it with Blur off, Blur and Frosted.
6. The same on a widget's page (Outline and Look sections).
7. Apply a theme or a bar style from Appearance - the gradient and glass edge stay as you set them.

## Gradient outline rework (0.38.34)
1. A bar against the top or bottom of the screen, outline 3: Gradient. Move the angle slowly from 0 to 180 - the colours shift smoothly the whole way (no jump at 90 / 91). At 90 the line is one colour on its inner edge and the other on its outer edge.
2. A floating bar (outline all round) and a widget: the angle turns smoothly; the far corners get the two colours.
3. Gradient with To empty: the colour box shows the Secondary Accent.
4. "Turn slowly" is gone from the outline settings.

## Gradient outline removed (0.38.35)
1. A bar and a widget that had a gradient outline: the outline is one colour again (its Colour, empty = Main Accent); no Colour style / To / Angle rows.
2. Glass edge still works on bars and widgets.

## Moving picked widgets together (0.38.36)
1. Ctrl+click three widgets (they show the pick outline). Drag one of them - all three move together and keep their spacing.
2. While dragging, it snaps to other (unpicked) widgets and the screen edges, not to the ones moving with it. Hold Alt - no snapping.
3. Lock one of the picked widgets, drag another - the locked one stays put, the rest move.
4. Restart vShell - the moved widgets are where you dropped them.
5. A widget that was snapped to one of the moved ones (and not picked) stays where it is and no longer follows it when that one resizes.
6. Drag a widget that isn't picked - only that one moves. Right-click > Clear selection, then drag - only that one moves.

## Glass edge in styles and themes (0.38.37)
1. Appearance: the Glass theme's picture shows the glass edge on its bars and widget. Apply it - bars and widgets that follow the styles get Subtle bars and a Strong widget edge.
2. Apply Matrix Sharp - the glass edge goes (Sharp / Outlined have none).
3. On a bar that follows the bar style, change Glass edge - it stops following (its own look), like changing the corner radius.
4. Save a bar style / widget style / theme with a glass edge, apply another, then yours - the glass edge comes back.
5. With a widget style that has a glass edge: the start menu, a module popup, the run box drop-down, a notification card and the history window show it.
6. The Glass edge rows have no description under them.

## Group drag snapping (0.38.38)
1. Pick two widgets side by side (Ctrl+click). Drag the left one so the group's right edge nears the screen edge - the right widget snaps to the edge gap.
2. Drag the group under an unpicked widget - the group's top snaps to the gap below it, and its left / right edges line up with it (guide line).
3. A picked widget in the middle of the group lines up with an unpicked widget's edge or centre.
4. Drag the group towards a top / bottom bar - it keeps the edge gap.
5. Alt while dragging - no snapping. Single widget drags snap as before.

## Proxmox: delete a guest (0.38.39)
1. Proxmox module settings: tick "Allow deleting VMs and containers from vShell".
2. Click a running guest - "Delete… (stop it first)" is greyed out. Stop it.
3. Click it again > Delete… - type a wrong ID: nothing is deleted. Again with the right ID: the row shows "deleting…" and the guest goes from the list; check Proxmox (also gone from backup jobs with purge ticked).
4. A token without VM.Allocate: vShell says the token isn't allowed.
5. Untick the setting - Delete… is gone from the menu.

## Proxmox permission info (0.38.40)
1. Integrations > Proxmox: hover the (i) on Token secret - it explains PVEVMUser for start / stop and PVEVMAdmin for deleting.
2. Test connection with a PVEVMUser token: "Start / stop allowed. To also delete guests give it the PVEVMAdmin role..."; with PVEVMAdmin: "Start / stop and deleting guests allowed."

## Proxmox: Update node (0.38.41)
1. A Proxmox widget showing Nodes (or the cluster popup): right-click a node's name > Update node… - the browser opens the upgrade shell for that node and apt dist-upgrade starts.
2. An offline node: Update node… is greyed out.
3. In the popup, the menu doesn't close the popup.

## Terminal module, stage 1 (0.38.42)
1. build.cmd - the first build downloads the WebView2 package (internet needed once). No build errors.
2. Add the Terminal module to a bar. Click it: the terminal drops down under a top bar / over a bottom bar, centred, with a Windows PowerShell tab ready to type in.
3. Type `dir`, `ping 1.1.1.1`, `Get-Process | Out-GridView` - output and colours right; resize by changing Width / Height in the module settings.
4. Ctrl+Alt+T from another app: it shows; again: it hides. Click another window: it hides (turn off "Hide it when you click another window" and it stays).
5. Hide it while `ping -t 1.1.1.1` runs, show it again - still running.
6. + opens another tab; the arrow lists the profiles (PowerShell 7 / WSL if installed). Ctrl+Tab switches, Ctrl+Shift+W closes, middle-click closes. Type `exit` - the tab closes.
7. Select text: Ctrl+C copies it (then Ctrl+C without a selection stops a running command). Ctrl+V / right-click pastes. Click a URL - it opens in the browser.
8. Profiles: add "Proxmox | ssh root@<your node>" - the arrow menu has it; it opens an SSH session (ssh, htop, nano work).
9. Appearance blur on: acrylic behind the terminal, theme colour over it; module setting "Background effect behind the terminal" off: solid theme colour. Change theme: the terminal follows next time it opens.
10. Exit vShell - no PowerShell / conhost processes left behind (Task Manager).
11. A widget with the Terminal module: click opens the terminal next to it.

## Notifications: sources fold until set up (0.38.42)
1. Settings > Notifications with e.g. Jellyfin not set up: its card is folded, "Not set up - add Jellyfin on the Integrations page" in its title. Click the title - it opens (with the note).
2. Fill in Jellyfin on the Integrations page, go back to Notifications - its card is open.
3. A set-up source: click the title to fold it; it stays folded while the window is open.

## Terminal look (0.38.43)
1. Terminal on a top bar, "Attach it to the bar" on: it slides down out of the bar, flush, as wide as the bar, square at the top, rounded at the bottom, the bar's outline on the sides and bottom. Click the module / Ctrl+Alt+T: it slides back up. On a bottom bar it slides up out of the bar.
2. "Attach it to the bar" off: it fades in centred, at Width %, in the widget style (corners, outline, shadow, glass edge).
3. Appearance blur Blur / Frosted: the blurred wallpaper shows behind the terminal text; off: the theme colour at its opacity.
4. Change the theme with the terminal open: tabs, background, text, cursor and the 16 colours change straight away. `Write-Host red -f Red; Write-Host blue -f Blue; Write-Host magenta -f Magenta` - blue is the Main Accent, magenta the Secondary.
5. Colours: Dracula - the colours change (background stays the theme's).
6. Tabs: the chosen tab has the accent underline; hover shows the ×.
7. Add the Terminal module to a widget: the terminal is in the widget ("Click to open Windows PowerShell"); click it, type. Click on the desktop and back: still there. Change the theme: the widget rebuilds, the shell is still running. Remove the module: its shells end (Task Manager).
8. Widget module with "Show the terminal in the widget" off: a button that drops the terminal down.

## Terminal fix (0.38.44)
1. Ctrl+Alt+T: the PowerShell prompt shows, you can type straight away, output appears.
2. Click a tab, then the text: it stays open; click another app: it hides (when that's on).
3. See-through: the blur / theme colour shows behind the text. If the text area is black, untick "See-through text area" - it becomes the theme colour.
4. Attached, top bar with rounded bottom corners: at 100 % width the terminal starts where the corners stop rounding. Width 50 %: centred under the bar.
5. The slide: the text appears once the terminal is in place (not left behind while it slides).
6. Terminal in a widget: click it - you can type; move the widget - the text follows.
7. If anything doesn't show: %APPDATA%\vShell\vshell.log, lines starting "Terminal".

## Terminal: SSH connections and files (0.38.49)
1. build.cmd - downloads SSH.NET the first time (internet). 0.38.50+: no NU1903 "vulnerability" warnings for SSH.NET and no CS0108 warning.
2. Open the terminal, click ☰ (left of the tabs): the side panel with Connections / Files.
3. New: a connection with Host, User, Sign in with Password, a password. OK - it's in the list. Click it: a tab connects without asking for the password (first time: a box asks to trust the server). If the terminal asks for the password anyway, tell me (askpass not used).
4. Remove the Terminal module, add it to another bar / widget - the connection is still there.
5. Quick connect: type root@<host> + Enter - a tab connects (asks in the terminal).
6. Import: hosts from ~/.ssh/config appear under "SSH config".
7. My key > Make my SSH key; right-click a connection > Copy my key to this server; answer Yes to signing in with the key; connect again - no password.
8. Disconnect (type exit in an SSH tab, or drop the network): the tab says disconnected; Enter reconnects.
9. Files: with an SSH tab in front, Files lists its home folder. Double-click folders, Up, type a path. Drop a file from Explorer on it - uploads. Right-click a file: Download, Open with its app, Rename, Delete. New folder. Right-click a folder > Open a terminal here.
10. Edit a connection's colour (#ff8800): its tab and dot use it.
11. Proxmox: right-click a node > Open shell - offers to save a connection for its IP, then connects. A running container > Open console (pct enter).
12. Komodo: a running container / stack service > Open shell in the container; Open shell on the server.

## Proxmox: SSH into a guest (0.38.51)
1. Right-click a running container in Guests - only "Open shell (SSH)" (no "Open shell on <node>" / "Open console"); it opens the container's console (pct enter) through the node's connection.
2. Right-click a running VM with the QEMU guest agent running (Options > QEMU Guest Agent ticked) > Open shell (SSH) - offers to save a connection with the VM's address filled in, then connects.
3. A VM without the guest agent - a message says why and offers to make a connection with its name; type the address, OK - it connects. Next time it goes straight in.
4. Token with only PVEVMUser - the message names the permission it needs for the guest agent.
5. Stopped guests and templates have no Open shell (SSH); no guest menu has Open shell on <node> (the node's own menu does).

## Terminal in a widget (0.38.52)
1. A widget with a Terminal module (Show the terminal in the widget on) - the widget is the terminal's height (700 x 600 by default - an existing module keeps its saved size) with the side panel and tabs; the text sits inside it, nothing outside the widget.
2. Change the height / width in a widget setting - the widget follows.
3. Give the widget a fixed height smaller than the terminal - the text is cut to the widget, never drawn over the desktop.
4. Remove the module / move the widget - no text window left behind.
5. Open an SSH tab from a saved connection - the tab shows the connection's name, not ssh.exe's path. A PowerShell / Command Prompt tab shows the profile name.
6. Keep opening tabs in the widget - they narrow (names trim with …); the widget doesn't get wider. Past ~80 px each, a "…" button appears after the + / ▾ buttons; its menu lists every tab, click one to switch. The chosen tab always stays visible.
7. Close tabs - they widen again and the "…" button goes away. Wheel over the tabs - next / previous tab.
8. Same in the drop-down terminal.
9. A window over the terminal widget: click into the terminal (or a tab) - the widget comes in front with the text; type. Click another window - the widget goes back to the desktop layer. Edit a connection from the side panel - the widget stays in front while the dialog is open.

## Too-full bar (0.38.56)
1. A full-width bar with more modules than fit (e.g. several wide graphs + a long taskbar on a narrow monitor) - vShell starts; the log shows a few "too full … stepping down (1)", "(2)" … lines, then stops; the modules step down (shorter graphs, then smaller text).
2. Change the theme / bar settings - it fits again from the sizes as set, no endless log lines.

## Menus at the pointer (0.38.58)
1. Open the terminal, click in it, type something. Then right-click a bar - its menu opens at the pointer, not the top-left corner.
2. Same for a module's right-click menu (Proxmox guest, Komodo, a widget) and the terminal's ▾ / … menus, in the drop-down and in a widget.
3. On a second monitor with different scaling - still at the pointer.
4. The log has up to three "Menus: … menu opened at …, pointer at …" lines - send them if a menu is still in the wrong place.
5. Open 10+ terminal tabs (drop-down and widget) - they narrow then overflow into "…"; the terminal keeps drawing and typing works; right-click a bar - its menu is at the pointer. CPU stays quiet (Task Manager: vShell near 0 % while idle with many tabs).

## Questions from widgets (0.38.59)
1. Komodo in a widget: click a procedure's Run - the "Run procedure …?" question is on top, in front of other windows; Yes runs it, No doesn't; the widget keeps working.
2. Proxmox in a widget: Shut down / Reboot / Stop a guest - same. Delete… (type the ID) - the dialog is on top and takes typing.
3. Terminal in a widget: side panel > New / Edit a connection - the dialog is on top and takes typing.
4. The same from a bar's popup - the question opens over the popup as before.

## 3D printer - Moonraker (0.38.60)
1. Settings > Integrations > + Add printer: name, Fluidd's address. Test connection - "Connected to Moonraker … Klipper is ready. Found Spoolman, N power devices, N webcams, the job queue."
2. Add the 3D printer module to a bar: dot + state (Idle / Printing 45% · 1h 12m) + nozzle / bed temps; hover for details. Try "Time on the bar" Finish / Both (12-hour).
3. Click it: the panel. Idle: "Ready · last: …". Webcam shows and refreshes; click it opens the stream.
4. Temperatures: Set on the nozzle (e.g. 200) - target shows, graph climbs. PLA preset heats nozzle and bed; Cool down turns them off.
5. Spoolman: the active spool with grams left; Change > pick another - it updates (and in Fluidd).
6. Power devices: toggle a light / relay; turning off asks first.
7. Files: newest first with thumbnails and times; ▶ on one asks, then prints. Right-click > Add to the queue - shows in Queue.
8. While printing: thumbnail, name, progress bar, layer, time left / done at, filament. Pause → amber, Resume. Cancel asks. (Emergency stop asks; needs Firmware restart after - only test if you mean it.)
9. History: the last prints; right-click > Print again.
10. Macros: buttons for your macros; click runs one. Console: type G28 / M117 Hi, Enter - the line and Klipper's reply show. In a widget, clicking the box lets you type.
11. Notifications page > 3D printer: start / finish (with thumbnail) / pause / cancel; set every 25 % and layer 3 - those come during a print; bed cooled down after.
12. A second printer (or the same one added twice): pills at the top of the panel switch between them; the bar shows both.
13. Widget: add the module to a widget - the panel is in the widget; questions (Cancel …) open on top.

## Printer webcam live (0.38.65)
1. Module settings > Webcam: Live, 15 fps - the picture moves smoothly in the popup and the widget.
2. Fold the webcam away / close the popup - Task Manager > vShell network drops to ~0.
3. 30 fps vs 5 fps - visibly smoother / choppier; CPU stays reasonable.
4. Snapshots, every 3 s - back to one picture every 3 seconds.
5. Two printers: switching printers switches the stream.

## Hide while the printer is off (0.38.66)
1. Widget with only the 3D printer module: tick "Hide while the printer is off". Turn the printer off - about half a minute later the widget disappears (log: "Widget '…': hidden - its modules have nothing to show").
2. Turn the printer on - within a few seconds of Moonraker answering, the widget is back where it was.
3. Same widget with another module too (e.g. a clock): the printer row goes, the widget stays (no gap).
4. Bar: the module hides / comes back on the bar.
5. Start vShell with the printer off - the widget doesn't show until the printer is on.

## Performance pass (0.38.67)
1. vshell.log still fills as before (lines appear within about half a second); a repeated line shows once plus "(the line above repeated N more times)".
2. Exit vShell - the last lines (Shutdown …) are in the log.
3. Terminal: output still appears instantly when typing / running commands (ping -t, a long dir); a widget terminal follows the widget when dragged.
4. Power flow dots animate; lock the PC / maximise a window over it and back - they carry on.
5. Komodo list still updates when a container stops / starts; a stale taskbar icon is logged once.
6. Task Manager: vShell's idle CPU with a terminal widget, power flow and printer widget should be lower than 0.38.66.

## Synced taskbar pins (0.38.69)
1. Two taskbars with different pins. Turn "Same pins on every taskbar" on for the first - its pins stay.
2. Turn it on for the second - it now shows the first one's pins.
3. Pin / unpin / reorder (drag) / drop a .exe on either - the other follows.
4. Turn it off on the second - its original pins come back; the first keeps the shared ones.
5. Settings > the taskbar's Pinned apps list shows the shared pins while synced (add / move / remove there affects every synced taskbar).

## Note scrollbar and folder notes (0.38.70)
0. A note with a fixed height, short text: keep typing until it scrolls - the widget's width doesn't change as the scrollbar appears.
1. A long note in a widget: drag the scrollbar thumb, click its empty track, press on its left edge - the widget doesn't move.
2. Notes module > Stored in: Folder > Choose the folder… (a folder with a few .md / .txt files) - the note shows the newest one, with its name in a title line.
3. Click the name (or ▾): the folder's notes, newest first, current one ticked. Pick another - it shows; edits save to that file.
4. Make a new .md in the folder in Explorer, open the list again - it's there.
5. Open the folder - Explorer opens it.
6. Switch Stored in between File and Folder - File shows its own file, Folder the note it was on; neither changes the other.
7. A short note and a long one in the same folder: both start right under the title line (no gap above the short one).
8. Title line > New note… > "Ideas" - Ideas.md is made in the folder and shown (empty); "todo.txt" makes a .txt. An existing name just switches to it.

## Network interface (0.38.73)
1. Network module > Interface: the list shows the adapters that are up (name - description · IP).
2. Pick Ethernet: speeds and the tooltip are only Ethernet's. Pick Wi-Fi / a VPN: only that one.
3. Two network modules on different adapters show different speeds.
4. Disable the chosen adapter - "No network"; enable it - speeds come back.

## Launcher bookmarks (0.38.74)
1. Launcher > Edit shortcuts and groups > nothing selected > Add bookmarks…: the browsers / profiles you have are listed; pick one - its folders show with how many bookmarks each has.
2. Pick a folder > Keep in sync: a new group named after it, "[group, synced]", with the bookmarks in the browser's order. The group's "Synced folders and bookmarks" shows "Edge bookmarks: Favorites bar/…".
3. Click the group on the bar: the bookmarks show with globe icons; click one - it opens in the default browser.
4. In the browser add a bookmark to the folder, delete one, reorder two: open the group's menu again - it follows (may take a few seconds for the browser to save).
5. Rename a bookmark in the browser - the menu follows. Rename it in vShell's editor, then rename again in the browser - vShell's name stays.
6. Rename / move the folder in the browser - the group still follows it (the editor shows the new place).
7. Select a group that already syncs a folder and Add bookmarks… into it: the folder's shortcuts first, then the bookmarks. Hand-added shortcuts stay at the top.
8. Stop syncing: the bookmarks stay as ordinary shortcuts; new bookmarks no longer appear.
9. A PC / profile with no Chromium browser: the picker says no bookmarks were found.

## Launcher group icons (0.38.74)
1. Edit shortcuts and groups > select a group: an "Icon" grid shows (not for shortcuts), Default (folder) outlined.
2. Click Games: the group on the bar shows the controller in the theme's text colour; the Games button is outlined. Hover the grid - each has its name.
3. Tick "In the theme's accent colour" - the icon turns the accent colour. Change the theme / accent - the icon follows.
4. Give the group a custom icon (Browse…) - it replaces the built-in one; pick a built-in icon again - the custom icon is cleared.
5. Default (folder) - back to the folder. Close and reopen the editor / restart vShell - the choices are kept.
6. The editor is scrollable on the right if it doesn't fit; a small bar / dock still sizes the icon down.

## Launcher long group menus (0.38.75)
1. Sync a group with a bookmarks folder that has more bookmarks than fit on the screen; click the group on the bar.
2. The menu opens against the bar (above a bottom bar / below a top bar), centred on the group, like a short group - not at the top of the screen.
3. It stops just short of the far screen edge with a slim scrollbar; the mouse wheel scrolls it, and dragging the scrollbar works. The last bookmarks can be reached and opened.
4. A short group shows no scrollbar and looks as before. Try on a top bar and on a second monitor with a different scale.
5. A bookmark with a very long title: the menu stays at most 350 wide and the title ends in "…" (also with the scrollbar showing). Hover it - the tooltip has the full name and the address. Short names aren't cut.

## Terminal pop-out (0.38.76)
1. Open the drop-down terminal with two tabs; in one run something that keeps writing (`ping -t 1.1.1.1`) and scroll back a bit of history.
2. Right-click that tab > Pop out into a window: a window opens (centred on the monitor the first time) with the tab; the history and colours are there and the ping carries on without a gap. The drop-down keeps the other tab.
3. Pop out the drop-down's last tab with Ctrl+Shift+O: the drop-down hides, a second window opens a little down / right of the first.
4. Run `claude` (or vim / htop) in a tab, pop it out: it redraws at the new size and keeps working - type into it.
5. An SSH tab: pop it out - still connected, typing works; the connections / files panel follows it.
6. Window: drag it by the empty part of the tab strip; resize from each edge and corner; double-click the strip - fills the work area (square corners, no gap), again - back. Minimise, then click it on the taskbar / Alt+Tab to it: the text is back and takes the typing. Alt+Tab shows "<tab name> - vShell terminal".
7. Pin: the window stays above other windows (text included); unpin. Open a new pop-out - it remembers the pin and the last place / size (also after a restart).
8. The back-arrow button: the tab goes back into the drop-down (which opens) with its screen; the window closes if that was its last tab. From a widget's terminal: back goes into the widget.
9. Close a window with a running tab: it asks; No keeps it, Yes ends the shell. A window whose tabs have all ended closes without asking.
10. Change the theme / widget style: the windows follow. Exit vShell with a window open: no question, the shells end.
11. Multi-monitor / mixed scaling: move a window to the other monitor - the text stays sharp and in place.

## Home Assistant calls over the WebSocket (0.38.76)
1. Click a light / switch in a Home Assistant module: it toggles and the module follows at once. Same for a Bubble slider (brightness), the climate module (target + / -, mode, fan, a zone slider) and a notification's toggle action.
2. Toggle quickly several times: each click toggles once - never twice.
3. Something that fails (set an entity to a value it refuses, e.g. a number outside its range): the log has "HA <domain>.<service> <entity>: <Home Assistant's error>".
4. Block the WebSocket (e.g. a proxy without WebSocket, or restart Home Assistant and click during the restart): clicks still work over REST once HA answers again.

## Command palette (0.38.77)
1. Open the run box (click it, or its keys) and type `/`: the line under it lists how many commands each integration has (Home Assistant may arrive a second after opening).
2. `/kitchen` (a light's name): Turn on / Turn off / Set … to …% rows, with the light's state on the right; the one that changes something is first. Enter on Turn off: the light goes off, the box shows ✓ and closes.
3. Without the slash: `kitchen light off` - the command is the top row (above the web search); Enter does it. `firefox` still puts the app first, with any matching commands below it.
4. Numbers: `ac 22` (or `set the living room to 22.5°`) sets the climate entity's target; `kitchen 40%` dims the light; `volume 30` sets the PC volume. A number out of range (`ac 99`) says what's allowed instead of doing it. `ac` alone shows "Set … to …°" - Enter says to type a temperature.
5. Ask first: `stop vm 105` / `restart stack jellyfin` / `cancel print` - Enter once: the row says "Press Enter again"; Enter again does it. Typing something else in between disarms it.
6. Proxmox: a stopped VM shows Start, a running one Shut down / Reboot / Stop / Suspend; after doing one the state on the right follows within a few seconds.
7. Komodo: `redeploy immich`, `pull stack media` - the same actions as `!ko`, Komodo's answer (or error) shows in the box.
8. Printer: while printing, `pause voron` / `resume`; idle: `preheat pla` (from the presets), `cool down`, a light on / off.
9. `ssh pve1` (a saved connection): the box closes and the terminal opens a tab to it. `lock the pc` locks; `restart the pc` asks first.
10. Run a command, then open the box with nothing typed: it's in the history as `/kitchen light off`; clicking it does it again (one that asks first is put in the box to confirm).
11. An integration that isn't set up adds nothing; with none set up only the vShell / PC / sound / media commands show. Typing stays quick with a big Home Assistant (hundreds of entities).

## Integrations as plugins - Komodo (0.38.77)
Release: attach dist\vShell.Komodo.dll and dist\vShell.Komodo.dll.sha256 to the v0.38.77 release, next to vShell.exe and its .sha256.
1. Upgrade with Komodo set up and a Komodo module on a bar / in a widget: the first start shows "Komodo - getting it…" on the module for a moment, then the module is back as before. The log has "Plugins: downloaded Komodo 0.38.77" and "Plugins: loaded Komodo". %APPDATA%\vShell\plugins has vShell.Komodo-0.38.77.dll.
2. Settings > Integrations: "Add or remove integrations" at the top lists Komodo as Added with Remove; the Komodo connection section is further down as before. Notifications page: the Komodo section is there; `!ko` and palette commands (`restart stack …`) work.
3. Remove (answer Yes): the Komodo section goes from Integrations and Notifications, the module shows "Komodo - add it in Settings" (click it: Settings opens), Add a module no longer offers Komodo, `!ko` is a DuckDuckGo bang / nothing, no Komodo notifications or polling (log quiet). The plugins folder no longer has the DLL.
4. Add again: Downloading… then Added; the module is back with its settings (Show, Server, Group …) and the connection as it was - no need to type the key again.
5. Add with no internet (or before the release has the DLL): it says why ("Couldn't get it: …") with Try again. Put vShell.Komodo.dll next to vShell.exe and press Try again - it's used from there.
6. "Add from a file…": pick dist\vShell.Komodo.dll - asks first (a plugin has full access), then added. A DLL from another version says which version it's for and isn't used; a random DLL says vShell doesn't know it.
7. Restart vShell with Komodo added: no download, it loads from the plugins folder straight away (the module shows at once).
8. An update (once there's a newer release with the plugin attached): after updating, the plugins folder has the new version's DLL, Komodo works straight away, and the old version's DLL is gone a few minutes later.
9. A fresh settings folder (no Komodo ever set up): Komodo is Not added; nothing is downloaded.

## Plugin settings - Komodo (0.38.78)
Release: attach dist\vShell.Komodo.dll and its .sha256 to the v0.38.78 release as before.
1. Back up %APPDATA%\vShell\settings.json, then upgrade from 0.38.77 with Komodo set up: the Komodo module shows the same as before (Show, Which, Server, Group, procedures, Open button, how many). Settings > Integrations > Komodo has the address, key, "A secret is saved", the certificate tick and the keyword as before; Test connection works.
2. settings.json (once vShell has saved, e.g. after changing any setting): no `"Ko…"` fields any more; there's `"Integrations": { "komodo": { "Url": …, "Secret": "<encrypted text>" … } }` and the Komodo module has `"Extra": { "Show": …, "Count": … }`. Other modules have no Extra.
3. Notifications page > Komodo: the ticks and the wait are as they were; stopping a container in Komodo still gives a notification (after the wait).
4. Change module settings (Show = Containers, Server, How many per server = 3): the module follows straight away, and after restarting vShell they're still set.
5. `!ko` in the run box (or the keyword you set) works; change the keyword on the Integrations page and the new one works.
6. Remove Komodo and add it again: the connection and the module's settings are still there.
7. Put a plain-text `"Secret": "S-…"` into the komodo section of settings.json by hand (vShell closed) and start: it works, and the file has it encrypted again.

## Seerr as a plugin (0.38.79)
Release: attach dist\vShell.Seerr.dll and dist\vShell.Seerr.dll.sha256 to the v0.38.79 release (with Komodo's and the exe as usual).
1. Back up %APPDATA%\vShell\settings.json, then upgrade from 0.38.78 with Seerr set up and a Seerr module: the first start shows "Seerr - getting it…" on the module for a moment (or loads straight away from next to vShell.exe), then it's as before - same tabs, the view it opens on, how many, posters / rows, filters, the Seerr button. The log has "Plugins: loaded Seerr". Komodo is still added.
2. Settings > Integrations: "Add or remove integrations" lists Seerr as Added. The Seerr section (now further down, after Downloads) has the address, "An API key is saved", the certificate tick, the keyword, the user id; Test connection works.
3. Module settings (Seerr module): untick tabs down to one (the last can't be unticked), "Opens on" shows only with two or more; Posters off hides "Rows of posters"; the module follows each change, and after a restart they're still set. On a bar: the badge / processing / available ticks and the title.
4. Notifications page > Seerr: the tick, "When a request becomes available" and the two kinds are as they were. A new request in Seerr gives a notification (within 30 s with the module showing, 3 minutes without).
5. Run box: `!s` lists the requests waiting for approval (Enter on one: Approve / Decline - Decline needs Enter twice; Back to the list); `!s recent` lists what was added; `!s dune` shows "Search Seerr for dune" - Enter shows the results with posters, Request (shows: pick the seasons) and Open in Seerr. Typing again goes back to the suggestions. An empty run box lists "!s Seerr" with the other keywords, and the run box's tooltip mentions it.
6. Click a `!s …` or `!ko …` entry in the run box history: it goes back in the box and shows its list.
7. Remove Seerr (Yes): the module shows "Seerr - add it in Settings", the Integrations / Notifications sections go, `!s` goes to DuckDuckGo, no Seerr polling in the log. Add again: all back as it was, without typing the key again.
8. A fresh settings folder: neither Komodo nor Seerr is added; nothing is downloaded. Removing Komodo and restarting doesn't add it back.

## Integrations page renamed Plugins (0.38.80)
1. Settings: the side list has "Plugins" where "Integrations" was; the page's title is Plugins, the section at the top is "Add or remove plugins" (Komodo and Seerr with Add / Remove), the rest of the page as before. Remove / Add from a file ask with the title "Plugins".
2. Remove Seerr: the module's tooltip says "Settings > Plugins brings it back …"; its module settings say to add it on the Plugins page (Add or remove plugins, at the top).
3. Hints: a Proxmox / UniFi / printer / Downloads / Home Assistant module with nothing set up says "Set up … in Settings > Plugins"; Add a module's "Needs … (Plugins)"; the Notifications page's "Not set up - add … on the Plugins page". UniFi's own key hint still says Control Plane > Integrations (that's UniFi's menu).

## 3D printers as a plugin (0.38.80)
Release: attach dist\vShell.Printer.dll and dist\vShell.Printer.dll.sha256 to the v0.38.80 release (with Komodo's, Seerr's and the exe).
1. Back up %APPDATA%\vShell\settings.json, then upgrade from 0.38.79 with a printer set up and a printer module: the module shows "3D printers - getting it…" for a moment (or loads straight away from next to vShell.exe), then as before - the print, the webcam (live / snapshots, the frames a second), temperatures, spool, power, the lists if shown, hide while off. "Plugins: loaded 3D printers" in the log.
2. Settings > Plugins: "Add or remove plugins" lists 3D printers as Added. The printer cards (name, address, "No API key saved" or "An API key is saved", certificate, webcam, Use this printer, Test connection) and the preheat presets are there as before.
3. Rename the printer, change its webcam, untick / tick "Use this printer": the module follows; restart vShell: the changes are kept.
4. + Add printer: a new "Printer 2" card appears (the page redraws, keeping its place); give it an address, Test connection; restart - it's still there. Remove it (Yes): the card goes, and stays gone after a restart.
5. Module settings: the Printer choice lists the printers (and Every printer); the bar's time / temperatures; webcam mode and the two sliders; the Show ticks. Each change shows straight away and is kept after a restart. Fold the webcam in the panel, restart: still folded.
6. Notifications page > 3D printer: the ticks, Progress every, the layer and cooled-down sliders and the four kinds are as they were. Start a print: "started printing" (and "finished" with its thumbnail at the end).
7. Command palette: with the printer on, type "pause the printer" / "preheat pla" / "cool down" / a light's name - the commands are there and work (cancel / emergency stop ask first).
8. Remove 3D printers (Yes): the module shows "3D printers - add it in Settings", the printer cards and notifications section go, no printer commands in the palette, no printer polling in the log. Add again: all back with the printers as they were.

## Downloads as a plugin (0.38.81)
Release: attach dist\vShell.Downloads.dll and its .sha256 (with the other plugins and the exe).
1. Back up %APPDATA%\vShell\settings.json, then upgrade from 0.38.80 with qBittorrent / SABnzbd set up and a Downloads module on a bar and in a widget: both show as before (the bar's speed, graph / ring / tile and count; the widget's list, its speed graph and How many). "Plugins: loaded Downloads" in the log.
2. Settings > Plugins: Downloads is Added; the qBittorrent and SABnzbd sections (address, user, "A password is saved" / "An API key is saved", certificate, keyword, Test connection) are as before.
3. Module settings on the bar: Show, the List rows, the Text ticks, Hide when nothing is downloading, the graph style and size and Graph full scale. In a widget: "In the widget" List / Graph swaps the module and the settings shown. Each change shows straight away and is kept after a restart.
4. Run box: `!qb` and `!sab` list the downloads (live, Enter pauses / resumes one); paste a magnet link: "Add to qBittorrent" and "Open it instead" - Enter adds it ("Added to qBittorrent ✓"). An .nzb address goes to SABnzbd. Clicking the magnet in the history puts it back in the box.
5. Drag a .torrent file over a run box: it shows "Drop to add it: .torrent to qBittorrent, .nzb to SABnzbd"; drop it: added. Drop one on the Downloads module (bar and widget) too.
6. A download finishing gives the notification (with the categories filter); the palette's "qBittorrent: alternative speed limits on / off" works.
7. Remove Downloads (Yes): the module shows "Downloads - add it in Settings", the sections go, `!qb` goes to DuckDuckGo, pasting a magnet offers no Add, dropping a .torrent on the run box does nothing. Add again: all back.

## UniFi as a plugin (0.38.81)
Release: attach dist\vShell.Unifi.dll and its .sha256 too.
1. Upgrade from 0.38.80 with UniFi set up and a UniFi Network module (and a Protect one if you have it): they show as before - the bar's speed / clients, the popup / widget Devices and Clients tabs, a device you'd opened still shows its ports; Protect's camera grid, snapshots, lights and events. "Plugins: loaded UniFi" in the log.
2. Settings > Plugins: UniFi is Added; the UniFi section (address, "A key is saved" for each key, certificate, site, Check every, keyword, Test connection) is as before.
3. Module settings for both: Show / Rows / the bar ticks (Network); Columns, Refresh every, Only these cameras (with "Your cameras: …"), lights, events, the bar's last event (Protect). Changes show straight away and are kept after a restart. Open a device's ports, restart: still open.
4. Notifications page: the UniFi Network and UniFi Protect sections are as they were. A device going offline / a new client / a ring or person (with its snapshot) gives the notification.
5. Run box: `!u` alone lists offline devices and new clients; `!u phone` finds clients and devices; Enter copies the IP. An empty run box lists "!u UniFi" with the other keywords.
6. Add a Protect module to a widget that sizes itself: the widget becomes 600 x 400.
7. Remove UniFi (Yes): both modules show "UniFi - add it in Settings", the sections go, `!u` goes to DuckDuckGo, no UniFi polling or Protect stream in the log. Add again: all back.

## Jellyfin as a plugin (0.38.82)
Release: attach dist\vShell.Jellyfin.dll and its .sha256 too.
1. Upgrade from 0.38.81 with Jellyfin set up: "Plugins: loaded Jellyfin" in the log; Settings > Plugins lists Jellyfin as Added, its section (address, "An API key is saved", certificate, this PC's device name, Test connection) as before.
2. Play something in the Jellyfin desktop app: Now playing shows the title with a progress bar ("· times from Jellyfin" under it in the popup); dragging / clicking the bar seeks; play / pause works.
3. Play in a Jellyfin app that Windows has no media session for (the web player on this PC as the device): Now playing still shows it with Jellyfin's art; the buttons and seeking go to Jellyfin.
4. Spotify / a browser video: Now playing as always (no Jellyfin times).
5. Notifications > Jellyfin: the ticks and the leave-out list as before; someone else starting to play gives the notification (within about 10 seconds).
6. Remove Jellyfin (Yes): its sections go, Now playing keeps working from Windows (no progress bar for the Jellyfin desktop app), no Jellyfin notifications. Add again: all back.

## Proxmox as a plugin (0.38.82)
Release: attach dist\vShell.Proxmox.dll and its .sha256 too.
1. Upgrade from 0.38.81 with Proxmox set up and Proxmox modules (Nodes / Guests / Storage): they show as before. "Plugins: loaded Proxmox VE" in the log.
2. Settings > Plugins: Proxmox VE is Added; its section (addresses, token ID, "A secret is saved", certificate, Update every, Test connection - it says what the token can do) as before.
3. Module settings: Show (the rows below change with it), Node (the cluster's nodes), the Guests ticks (CPU and memory in a widget only), Style, Allow power / delete. Kept after a restart.
4. Start / shut down a guest from the module (asks first except Start); delete a stopped one (asks for its ID); Open shell on a node and on a container / VM.
5. Notifications page > Proxmox as before; a guest stopped from Proxmox's web page gives the notification after the wait (not when stopped from vShell).
6. Palette: "restart <vm name>" / "shut down vm 105" work (asking first).
7. Remove Proxmox VE (Yes): the modules show "Proxmox VE - add it in Settings", no Proxmox polling, no palette commands. Add again: all back.

## Home Assistant as a plugin (0.38.82)
Release: attach dist\vShell.HomeAssistant.dll and its .sha256 too (with every other plugin and the exe).
1. Back up settings.json, upgrade from 0.38.81 with Home Assistant set up: "Plugins: loaded Home Assistant" in the log; Settings > Plugins lists it as Added, its section (address, "An access token is saved", certificate, Update every, Test connection) as before.
2. The Home Assistant entities module (plain and Bubble), Climate (with its zones) and Power flow show as before. Their settings (Choose entities…, the Bubble ticks and width, the climate entity / Zones…, the power sensors with Find…, the colours) work and are kept after a restart. A light toggles from the module.
3. Weather module: "Weather from" lists Open-Meteo and Home Assistant; Home Assistant shows the weather entity list (Refresh), the module shows that entity's weather and forecast.
4. Notes module on vDrop: the feed shows as before (tabs, lists, add a note, right-click items); its settings show the vDrop text and Sync now. "Stored in" lists vShell / File / Folder / vDrop.
5. Notifications page: Home Assistant (Rules…, with the count) and vDrop notifications sections as before; "Notifications sent to vShell" (the listener) is its own section and works with the plugin removed too. A rule fires; one with Click = Toggle toggles its entity when its card is clicked.
6. Palette: "kitchen light off", "ac 22" work.
7. Remove Home Assistant (Yes): its modules show "Home Assistant - add it in Settings"; a Weather module on Home Assistant says "Add its plugin in Settings > Plugins" (its settings show "HomeAssistant (add its plugin…)" in Weather from); a Notes module on vDrop says vDrop comes with a plugin that isn't added; no Home Assistant polling / WebSocket / vDrop sync in the log; the listener still takes notifications. Add again: everything back as it was.
8. A fresh settings folder: no plugins added; Add a module has no "Smart home & more" group; Weather offers only Open-Meteo; Notes offers vShell / File / Folder.

## Plugins page and the release's plugin list (0.38.83)
Release: attach dist\plugins.json with the exe and every plugin's .dll and .sha256 (build.cmd prints what plugins.json lists).
1. Settings > Plugins: a search box and All / Added / Not added at the top; the plugins under Smart home, Servers & network, Media, Devices. Typing "media" / "!ko" / "docker" finds the right ones; "No plugin matches" for nonsense. Added shows only added ones; Not added the rest.
2. Each row: icon, name, description, "Adds the … module(s); …". An added one says "Added" with "Settings ›" (jumps down to its section), or "Added - not set up yet" with "Set up ›" (Home Assistant with no address, for example). Not added: "Add".
3. With the release published (plugins.json attached): the list reads it (nothing visible changes for V's plugins). Without internet: the page still lists V's plugins; a line under the list says the list couldn't be read.
4. Add from a file… with a third-party plugin built for this version (e.g. a test "Hello" plugin): it shows "Hello by Someone - A test plugin…" and asks; after Yes it's in the Other group as "by Someone · added from a file", Added, and its module is in Add a module. Restart vShell: still there. Remove it: it's gone from the list (and its files from %APPDATA%\vShell\plugins).
5. Add from a file… with a plugin built for another vShell version: "That file can't be used: it's for vShell 0.38.82, this is 0.38.83." A random .NET DLL: "… isn't a vShell plugin (it has no plugin manifest…)".
6. build.cmd: dist\plugins.json lists the eight plugins with their groups; dist\sdk has vShell.dll and plugins.md. A DLL in third-party\ built for this version is copied to dist with a .sha256 and listed; one for another version is left out with the reason in plugins.txt.

## Power flow in a fixed-size widget (0.38.84)
1. A widget with a fixed width and height (e.g. 310 x 342) with the Power flow module: the readings show within a couple of seconds and the dots move. Make the box smaller so it steps down: still updating. Set it back to automatic size: still updating.
2. The Climate widget at a fixed size: the temperature and zones still update every second.

## The plugin API (0.38.84)
1. build.cmd: "Plugin API: unchanged" (or "N new"); dist\sdk has vShell.dll, plugins.md and plugin-api.txt; plugins.txt says "plugin API 1" for each of V's plugins and lists nothing "outside the plugin API".
2. Every plugin still works as before (they no longer use vShell's internals): Komodo / Seerr / UniFi / Downloads run box keywords (!ko, !s, !u, !qb, a magnet link), Home Assistant's Weather source (pick it in a Weather module), vDrop in a Notes module, a Home Assistant rule's Toggle on its notification, the palette's Home Assistant commands, Power flow's dots stopping while a full-screen window covers it, Komodo's log window scrollbars.
3. Remove Home Assistant: the Weather module's Home Assistant source and the vDrop store go; add it again: they're back.
4. Add from a file… with a plugin built against this vShell.dll: accepted. One built for 0.38.83: "That file can't be used: it's for vShell 0.38.83, this is 0.38.84."
5. Next release: a plugin added from a file is still added and loads after the update (no new build needed).
6. To see the guard: remove a [PluginApi] member (e.g. Theme.Fg) and run build.cmd - it stops with "PLUGIN API CHANGED" and the line. Put it back.

## Plugin review fixes (0.38.85)
1. build.cmd: "Plugin API: unchanged" (docs\plugin-api.txt now lists enum / const values and vShell interfaces, e.g. "NotifyLevel :: const NotifyLevel Alert = 2", "TilesModule (class : Border, IMultiRow)").
2. Every plugin loads as before (log: "Plugins: loaded … 0.38.85"); nothing says "uses parts of vShell this one doesn't have".
3. Older build: delete %APPDATA%\vShell\plugins\vShell.Komodo-0.38.85.dll while vShell is closed (keep the 0.38.84 one), start vShell: Komodo works, and the log says "Komodo is on an older build - getting the one for 0.38.85" then "this version's build is here (used from the next start)".
4. Start-up isn't slower with all eight plugins added (the Plugins log lines come within a second or two).

## Plugin updates (0.38.86)
Release: build.cmd, then a release v0.38.86 with everything as usual (plugins.json now has versions and SHA-256s).
1. Settings > Plugins: each added plugin shows its version ("0.38.86 · …"); the row at the top says "Checked 9:37 pm (v0.38.86…): all up to date" after Check for updates. "Update plugins by themselves" is on.
2. A plugin update without a new vShell: change something small in a plugin (e.g. Komodo's module tooltip), bump vShell.csproj's Version (the plugins take it), run plugins.cmd, and replace plugins.json and all the plugin .dll / .dll.sha256 files on the v0.38.86 release. In vShell 0.38.86: Check for updates -> "Update all (8)" / "Update to 0.38.87" on each; Update -> "Updated to 0.38.87 - restart vShell to use it" and a Restart vShell button; Restart vShell -> it closes and comes back, the log says "Plugins: loaded Komodo 0.38.86" from the new file, and Check for updates says all up to date.
3. By themselves: same as 2 but don't press anything - within a minute of starting (or 6 hours later) a notification "8 plugins were updated … restart vShell to use them"; clicking Restart on it restarts vShell.
4. With "Update plugins by themselves" off: the notification says "Plugin updates: … - update them in Settings > Plugins" (once per build), nothing is downloaded.
5. A plugin build that uses something a vShell doesn't have (built against a newer vShell with a new API part): its row says "(… needs a newer vShell (0.38.87) - it uses …)" and there's no Update button.
6. Uptime Kuma (built against 0.38.85) still adds from a file and works on 0.38.86. Put its DLL in third-party\ and run plugins.cmd: it's in plugins.json and can be added from the list.

## Plugins page (0.38.87)
1. Settings > Plugins: under "Add or remove plugins" there's no description text - the Check for updates row comes straight after the heading.

## Only changed plugins in a release (0.38.88)
1. build.cmd with no plugin changed since 0.38.87: each plugin says "Plugin unchanged: … (stays in the 0.38.87 release)", dist\release\ has only vShell.exe, vShell.exe.sha256 and plugins.json, and plugins.txt says "unchanged, in the 0.38.87 release" for each.
2. Make a release v0.38.88 from dist\release\ only. Settings > Plugins > Check for updates: "all up to date" (no plugin offered as an update); Remove and Add a plugin again - it downloads (from the v0.38.87 release, see the log).
3. Change something in one plugin (plugins\Komodo), build.cmd: only Komodo is built and in dist\release\; its plugins.json entry has in_release 0.38.88, the others keep 0.38.87.
4. build.cmd all: every plugin is built again and in dist\release\.
5. plugins.cmd with nothing changed: "0 built", dist\release\ has only plugins.json.

## Plugins icon (0.38.89)
1. Settings: Plugins in the list on the left shows a puzzle piece (not a house), lined up with the other icons.

## Tiles and palette messages for plugins (0.38.90)
1. build.cmd: "Plugin API: unchanged" (docs\plugin-api.txt already has the 23 new lines: TilesModule+Tile, InfoModule+Reading, TilesModule.Rebuild / SetDot, PaletteCommand.Worked / IsWorked); dist\sdk\plugin-api.txt has them, and dist\sdk\plugins.md has the "Tiles (TilesModule)" and "Command palette results" sections.
2. Service status module (now uses SetDot): dots are still green / yellow / red as before, on a bar and in a widget.
3. Home Assistant entities in a widget with Tile layout = List: still one entity per row, graphs as before; add an entity in Settings - the widget gets one row taller.
4. The third-party plugin built against 0.38.90 (subclassing TilesModule, no wrapper): add / remove a server on its side - the tiles follow within a poll, and in a widget's list layout the widget's height follows without restarting. Clicking a tile opens its card at that tile.
5. Run box: a palette command that returns PaletteCommand.Worked("…") shows "… ✓" for about two seconds and closes; one that returns null shows "Title ✓" and closes as before; one that returns an error shows "Source: error" and stays open.
6. A plugin using Rebuild / SetDot / Worked added from a file on 0.38.89: refused with "uses parts of vShell this one doesn't have" (not a crash).

## Restart after adding a newer build (0.38.91)
1. Add a plugin from a file (Settings > Plugins > Add from a file). Change something in it, rebuild it, and add the new DLL from a file without restarting: a question says vShell is still running the build it loaded earlier and asks "Restart vShell now?". Yes: vShell restarts and the new build is loaded (log: "Plugins: loaded …").
2. Same again, but answer No: a notification "<Name> needs a restart" shows (its tooltip says "Click to restart vShell"), and the plugin's row says "Added - restart vShell to use the new build". Clicking the notification restarts vShell; after that the row just says "Added".
3. Add the same DLL again (no change): no question, no notification.
4. Remove a plugin from the list and Add it again in the same run (it's downloaded again, same build): no question - it works straight away as before.
5. Add a plugin that wasn't loaded yet in this run: no question, it works without a restart.

## Popup height for plugins (0.38.91)
1. A plugin built against 0.38.91 that sets its list's `MaxHeight = popup.ContentRoom()`: on a bottom bar the popup reaches up to just below the top of the screen and scrolls the rest; on a top bar it reaches down to just above the bottom. Try a second monitor with a different scale.
2. Application launcher group menus (already use it): a long bookmarks group still fits the screen and scrolls as before.
3. That plugin added from a file on 0.38.90: refused with "uses parts of vShell this one doesn't have" (not a crash).

## Shorter Plugins and Notifications pages (0.38.92)
1. **Plugins - Added**: Settings > Plugins opens on Added (or on Get more when nothing is added). Each plugin is one row: its version and what it brings (or "Not set up yet", "Didn't load: …", "Restart vShell …") and a dot - green working, grey not set up, red not loaded, accent waiting for a restart. Hover the dot for its state in a word.
2. A plugin that isn't set up shows **Set up** - it opens the plugin's page on Connection. One with an update shows **Update to …**. One that didn't load shows **Try again** and **Remove** (it has no page).
3. Updates: with an update waiting, a banner over the tabs says which, with Update / Update all; once updated it says to restart, with Restart vShell. With nothing to do there's no banner. The Updates card at the bottom has the last check and Check for updates (it says "Checking…" while it looks), and "Update plugins by themselves" with its (i).
4. **Get more**: the plugins not added, by group, with search (typing keeps the focus in the box). Add moves it to Added. "Add from a file…" is at the bottom of this tab.
5. **A plugin's page**: click a row (not its buttons) - "Plugins › Komodo" with its description, version and state, Remove and ‹ Back, and tabs Connection / Notifications / Modules (only the ones it has). Remove asks first and goes back to the list. ‹ Back, or Plugins in the sidebar, goes back to the list - which shows the new state (set one up, come back: its dot is green).
6. Connection: the same settings as before (Test connection works, a secret saves). 3D printers: add / remove a printer - the page redraws and stays on Connection.
7. Notifications tab: the plugin's notification settings as plain cards (they don't fold any more). With the connection not filled in, a line in the accent colour says "<Name> isn't set up yet - fill in Connection first".
8. Modules tab: each module with its picture and blurb, every bar / widget it's on with **Edit ›** (opens that module's settings in the module editor), "Not on a bar or widget yet" when it's nowhere, and "Add one to" (pick a bar or widget, Add: it's added at the end and its settings open). A widget-only module only offers widgets.
9. **Notifications**: the page is three rows (Cards, Quiet times, History) with a summary each, then What sends them (Service status, System, Weather, Sent to vShell) and From plugins (a row for each plugin with notifications), then Try them (the three tests and Open the history). Times in the Quiet times summary follow the 12 / 24-hour setting.
10. The switches on Service status, Weather and Sent to vShell turn them on / off without opening them, and the summary changes. Turning Sent to vShell on the first time makes a token (open it: the token box has it).
11. Each row opens "Notifications › Cards" (etc.) with ‹ Back; the sidebar keeps Notifications highlighted. Every setting from before is there and works. A plugin's row opens that plugin's page on its Notifications tab.
12. System: CPU, GPU, storage, memory and battery each have a switch and a slider instead of "0 = off". Off saves 0 (no warning); on again starts at 90 °C / 85 °C / 95 % / 90 % / 15 %. Same for "Count repeats as one within" on History (on again = 10 minutes). Existing values show as they were.
13. **(i) instead of notes**: on these pages (and on each plugin's page) the grey explanation paragraphs are an (i) after the setting's label - hover it (or click it) for the text; clicking it doesn't flip the switch on that row or open the row. A note with no setting above it (first in a card, under buttons) still shows as text. Bars, widgets and the module editor still show their notes as before.

## Back from a plugin's page (0.38.93)
1. Settings > Notifications, click a plugin under From plugins: its page opens on Notifications, the title line says "Notifications › <Name>" and Notifications stays highlighted in the sidebar. ‹ Back goes to the Notifications page.
2. Remove the plugin from that page: you're back on Notifications (without its row).
3. Settings > Plugins, click a plugin: "Plugins › <Name>", Plugins highlighted, ‹ Back goes to the Plugins list as before.

## No scrollbar flash in Explorer (0.38.93)
1. "Hide the Windows taskbar" on, a File Explorer window open with a scrollbar: change settings that aren't colours (a module option, a bar's height, a switch on Notifications) - Explorer's scrollbars don't flash.
2. Turn "Hide the Windows taskbar" off: Windows' taskbar comes back; on again: it hides. Restart Explorer (Task Manager) with it on: the taskbar is hidden again.
3. Match Windows to vShell's theme on: change the Main Accent - Windows' accent follows (Explorer may redraw then, that's expected) and the log has "Windows colours set: …". Changing anything else adds no such line.

## Bars and Widgets as lists, pages with tabs (0.38.94)
1. **Sidebar**: Bars and Widgets are single items (no list of bars / widgets under them). Each stays highlighted on its list, an item's page and the module editor.
2. **Bars list**: a row per bar - "Top bar" with monitor · layout · height · modules · reserves space · follows the bar style (or its own look), a green dot (orange with a monitor unplugged). + Add bar adds one and opens its page. Bar style row opens Appearance.
3. **A bar's page**: "Bars › Top · Monitor 1", the summary line, live preview, tabs Layout / Look / Modules (n). Every setting from before is there: Layout (layout, follow the bar style, three zones), Size and space (height, edge gap on Floating, reserve space, keep centred on Dock); Look (corners, round all four on Floating, opacity, blur, separators, glass edge), Outline (not on Minimal). Explanations are (i)s.
4. Background opacity like the theme: off shows "Background opacity (%)"; on hides it and the bar follows the theme again. Same on a widget's Look tab.
5. Changing a shape setting on Look (corners, blur, outline) turns Follow the bar style off (check it on the Layout tab).
6. Modules tab: each module (with its zone when zones are on) - click opens it in the module editor; Edit modules › opens the editor. ‹ Back in the editor returns to the Modules tab; ‹ Back on the bar's page returns to the list. Remove bar goes back to the list.
7. **Widgets list**: a card per monitor (one "Your widgets" card with one monitor; unplugged ones last), each widget with its summary and a Show switch - flipping it hides / shows the widget and the summary says Hidden. + Add widget adds one and opens it.
8. **Snapping and gaps** is on the Widgets page (not Desktop any more): snap while dragging, both gaps, re-space - all work as before, with (i)s. Desktop's page is just wallpapers.
9. **A widget's page**: tabs General / Layout / Look / Slide away / Modules (n). Name changes show in the title next time. Layout: Columns only for Grid; Automatic width / height - turning one off starts at the widget's current size and shows its slider; on again sizes to the modules.
10. Look: Show a header row reveals Collapsible / header colour; Show an outline reveals its thickness / opacity / colour. Slide away: off shows only the switch and where it's snapped; on shows how it moves (Slides towards only when snapped into a corner).
11. Right-click a bar > Settings, or a widget > Settings: opens that bar's / widget's page directly; ‹ Back goes to its list. A plugin page's Modules tab > Edit › still opens the module editor.

## Modules tab opens the editor (0.38.95)
1. A bar's page on Look: click Modules (n) - the module editor opens ("Bars › Top bar › Modules"). ‹ Back returns to the bar's page on Look.
2. A widget's page on Slide away: click Modules (n) - the editor opens; ‹ Back returns to Slide away. Add a module in the editor, ‹ Back: the tab says the new count.

## Appearance as a list (0.38.95)
1. **The list**: Settings > Appearance shows the theme in use (picture, name - "Your own look" when it isn't a theme - and what it's made of) with Change theme ›, then Look (Colours with four colour squares, Background, Bar style, Widget style) and Windows and time (Match Windows with its switch, Times 12-hour / 24-hour with an (i)). Save as theme…, Import… and Export… are at the top.
2. Each summary is right: Background says the opacity, solid / gradient and its direction, and the blur; Bar / Widget style say how many follow the style; Match Windows says what it matches (or Off). Change something on a part's page, ‹ Back: the list shows it.
3. **Theme**: "Appearance › Theme" with every theme card (yours with ★), the one in use ticked. Clicking one applies it (asks first when some bars / widgets have their own look). Delete theme works on a saved one you picked. Your own look that isn't a theme shows first as "Current look (not a theme)".
4. **Colours**: Colours from your wallpaper (switch with an (i)) - on shows the wallpaper's colours with Save as preset… and greys out Your colours; off brings them back. Presets shows every preset with Save colours… / Delete. Main / Secondary Accent have (i)s. Picking a colour changes the bars and widgets straight away.
5. **Background**: opacity slider (and typing a number), Style Solid / Gradient - Gradient shows Direction and Mirror it on bottom bars; Solid hides them. Background blur Off / Blur / Frosted.
6. **Bar style** / **Widget style**: every style card, Delete for a saved one, the list of your bars with Open, and Make every bar / widget follow the style when some have their own look.
7. **Windows colours**: the switch; on shows Light / dark mode and Accent colour. Turning it off from the list's switch works too and the summary says Off.
8. The sidebar keeps Appearance highlighted on every part's page; ‹ Back always goes to the list.

## About page text (0.38.96)
1. Settings > About: under "vShell" and the version, "Bars and Widgets with a selection of plugins and modules to suit your needs." then "Vibe coded with love by version". Files (Open folder, Open log) is the last section - no "If something goes wrong".
2. Files: under Log file, "Plugin SDK" with "Create your own plugin" under it and Download - it opens the browser and downloads sdk.zip.

## Recent apps in the run box (0.38.97)
1. Run box on a bar: type "powershell", start Windows PowerShell from the list. Click in the run box again: Windows PowerShell is under recent - click it (or Enter on it): PowerShell starts again, no web search.
2. Same in the run box pop-up (Alt+Space).
3. Typing "powershell" again shows the recent entry under the app; it starts the app too.
4. Recent entries that aren't apps still work as before: a web search ("weather sydney"), a URL, !s something, =2+2, >ipconfig, /restart jellyfin.

## vShell tray icon, Restart vShell (0.38.98)
1. The vShell logo is in the Tray module (or Windows' tray with no Tray module); hover: "vShell 0.38.98". It can be pinned / hidden like other icons.
2. Click it: Settings opens. Right-click: Settings…, Notification history, Check for updates, Restart vShell, Exit vShell. The menu opens at the pointer and closes on a click elsewhere.
3. Notification history: the history window opens.
4. Check for updates: Settings opens on System and the Updates card shows the check and its result. With an update waiting the icon's tooltip says "update … ready".
5. Restart vShell (tray menu and a bar's right-click menu, just above Exit): vShell closes and starts again with the same settings; bars, widgets and tray icons come back.
6. Settings > System > Show vShell in the system tray off: the icon goes; on: it comes back. After a vShell restart it stays as set. Exiting vShell takes the icon away (no ghost icon left).

## Network icon in the Tray module (0.38.98)
1. A bar with a Tray module: a network icon is on the bar (not behind the arrow) the first time. On Wi-Fi it's the Wi-Fi fan with as many arcs lit as the signal (all at 80 % or more); on a cable it's the wired icon; unplugged / Wi-Fi off it's the Wi-Fi fan faded.
2. Hover: the Wi-Fi network's name and signal ("Home  ·  Wi-Fi 72 %"), or the adapter name and "connected", or "Not connected".
3. Click: Windows' list of networks opens. Right-click: Available networks, Network and internet settings, Network connections.
4. Turn Wi-Fi off / pull the cable: within a few seconds the icon changes; back on: it changes back. It isn't redrawn while nothing changes.
5. Drag it onto the arrow: it hides with the others; out of the popup onto the bar: back. Drag it along the bar: its place is kept.
6. Tray module settings > Network icon off: it goes; on: it's back.
7. No double icon: where Windows' own network icon reaches the tray (Windows 10, or with vShell's tray icons from Windows' own), only that one shows - vShell's isn't added.

## Tray icon in the theme's colours (0.38.99)
1. vShell's tray icon is the three-dot vShell mark in the theme's Main Accent, Secondary Accent and text colours - the same as the logo at the top of Settings.
2. Pick another theme / colour preset, or change the Main Accent: the tray icon changes with it straight away. Colours from your wallpaper: it follows the wallpaper's colours.
3. Sharp at 100 %, 125 %, 150 % display scaling (drawn at Windows' tray icon size). Turning the icon off and on again keeps the colours.

## Tray tooltips steady, network left-click (0.38.100)
1. Hover over tray icons (on the bar and in the arrow's popup) for 10 s or more each: the tooltip stays up, no flashing. Try an app that updates its icon often (a CPU / network monitor, a sync app while syncing) - its picture still changes, and a tooltip that changes text shows the new text.
2. Icons still come and go as apps start / quit, and dragging, pinning and hiding work as before.
3. The network icon: left-click does nothing; right-click shows Available networks / Network and internet settings / Network connections. Its picture still follows Wi-Fi signal / wired / not connected.

## Tray icon colours (0.38.101)
1. Tray module settings > Icon colours: Original (default) - icons as before; Colour / Tint strength don't show.
2. Tint: Colour (Text / Main Accent / Secondary Accent) and Tint strength show. The icons take on the colour - at 10 % hardly, at 90 % nearly all of it - keeping their shapes and some detail; see-through parts stay see-through (no coloured squares).
3. One colour: Colour shows (no strength). Every icon is its shape in that colour, crisp, on the bar and in the arrow's popup; the network icon too.
4. Change the theme / Main Accent: the icons follow. An app changing its icon (a sync spinner, a badge) still shows the change in the chosen colours. Tooltips, clicks, dragging and pinning work as before.

## Tray icon colours: Original or Tint (0.38.102)
1. Tray module settings > Icon colours shows two choices, Original and Tint. Picking Tint shows Colour set to Main Accent and Tint strength at 40 %.
2. A tray that was on One colour in 0.38.101 comes up on Tint. One left on Original in 0.38.101 (never changed) starts Tint at Main Accent / 40 % when you switch to it.
3. Tint with Text / Main Accent / Secondary Accent and 10-90 %: the icons lean towards that colour, see-through parts stay see-through.

## No text size for the Tray module (0.38.103)
1. Edit modules on a bar > the Tray module: Settings starts with Zone (with three zones) / its tray settings - no Text size row. Other modules (Clock, CPU …) still have Text size.
2. Launcher: with both "Show names next to shortcut icons" and "… group icons" off, no Text size; tick either: Text size appears at the top straight away; untick both: it goes.
3. Taskbar: Combine buttons Always + Combined buttons show Icon only: no Text size. Change Combine to When the bar is full / Never, or the buttons to show app name / window title: Text size appears (those show text). Back to Always + Icon only: it goes.

## Groups inside groups, Start menu sections (0.38.104)
1. Launcher: Edit shortcuts and groups > select a group > Add group: "New group" appears inside it. Add shortcuts to it. On the bar, click the outer group: the inner group shows with its icon and a ›. Click it: the menu shows its shortcuts with a Back row (its name) on top. Back / Backspace / Left returns. Clicking a shortcut starts it and closes the menu.
2. Make groups 4 deep: Add group on the 4th level says no more groups fit. "In group" on a group lists only places it fits (not itself or its own groups). Move a group to the top level and back.
3. A synced group (Add folder… / Add bookmarks…) inside another group still picks up new shortcuts when opened.
4. Start menu with nothing ticked under "In the menu": it looks and works exactly as before.
5. Tick Pinned > Edit pinned…: add shortcuts and a group (with a group inside). The menu shows a Pinned heading with tiles, then All apps. Click a tile: it starts. Click the group tile: it opens in the menu with Back; the inner group opens further. Up / Down / Enter go through the tiles too.
6. Tick Recent files: open a couple of files in Notepad / Office, open the menu: they're at the top of Recent files with the time (12-hour unless Use 24-hour is on), older ones show Yesterday / the day. Folders aren't listed. Click one: it opens. Change How many.
7. Tick Start Menu folders as folders: folder rows (folder icon, ›) sit in the A-Z among the programs; programs in folders no longer show on their own. Open one, then a subfolder; Back returns to where the list was scrolled. Typing searches every program (inside folders too); clearing the search goes back to the full list.

## Start menu "Show folders" (0.38.105)
1. Start menu settings > In the menu: the folders option reads "Show folders" with a note under it explaining it.
2. Add a new Start menu module: Show folders is ticked and the A-Z list shows folder rows. Untick it: every program in one A-Z list again.
3. With a settings.json from 0.38.104 where the option was off: start vShell - the Start menu shows folders and Show folders is ticked. Untick it, restart vShell: it stays off.
4. Start menu settings: "Text size (S / M / L) sets the size of the text in the menu." is right under the Text size row, not under Icon size. Other modules have no such note.

## Taskbar animations always on, attention setting moved (0.38.105)
1. Taskbar settings: no "Button animations" section; "An app that wants attention" (with its note) sits under "Only windows on this bar's monitor". Pulse / Steady still work.
2. Open and close windows: buttons grow in / shrink away (also on a taskbar that had the old setting off). Drag a pin to a new place: the buttons slide over.
3. Launcher and Tray settings: no "Icons slide to their new place" setting. Drag a launcher icon / a tray icon along the bar: the others slide over (also on one that had the old setting off).

## Background fade in Your colours (0.38.105)
1. Appearance > Colours > Your colours: Background fade sits under Background (with its note). Pick a preset: Background fade changes to the preset's.
2. Background > Style Gradient: the bars fade from Background into Background fade; change Background fade: the bars follow. The Background page has no colour row of its own any more, just the note.
3. With Colours from your wallpaper on, Background fade is greyed with the others. Save colours… then pick the saved preset: its fade comes back too.

## Presets / Saved tabs (0.38.105)
1. Appearance (the themes under the heading), Colours, Bar style, Widget style: each gallery has Presets / Saved tabs where the title was; the buttons (Save colours…, Delete) stay on the right.
2. Presets shows only the built-in ones (plus "Current (not a preset)" first when your look matches none). Saved shows only yours (★); with none it says how to save one.
3. With a saved one in use, the page opens on Saved; with a built-in one, on Presets. Click the other tab: it switches straight away and nothing is saved or redrawn on the bars; it stays on that tab when the page is rebuilt (pick a card, change a colour).
4. Save colours… / Save as theme… / Save Bar Style / Save Widget Style: that gallery shows Saved with the new one ticked. Delete a saved one: it goes from Saved.

## 12 new colour presets (0.38.105)
1. Appearance > Colours > Presets: Outrun, Sunset, Amber CRT after Matrix; Catppuccin Latte, Paper, Sakura, Arctic next; Windows Dark, GitHub Dark, Monokai, Forest, Monochrome after AMOLED Black.
2. Pick each: bars, widgets, popups and the settings window take its colours; text is readable on the light ones (Latte, Paper, Sakura, Arctic) and the badge / warning colour stands out. With Background > Style Gradient, each fades into its Background fade.

## Switching back to Presets (0.38.106)
1. Appearance > Colours: click Saved, then Presets, then Saved, then Presets again. Each click shows that tab's cards and highlights that tab; nothing goes blank.
2. Same on the themes (Appearance), Bar style and Widget style, with and without saved ones (Saved with none shows the "No saved … yet" line, then Presets comes back).

## 4 more colour presets (0.38.107)
1. Start vShell with your settings: Appearance > Colours > Saved no longer has Deep Purple, Warm Sunset, Carbon Teal, Cosmic Blue (v theme, vShell style 1 / 2 stay).
2. Presets: Warm Sunset after Arctic; Deep Purple, Carbon Teal, Cosmic Blue at the end. Pick each: secondary accent is gold (Deep Purple), teal (Warm Sunset), coral (Carbon Teal), magenta (Cosmic Blue); with a Gradient background each fades into its own colour.
3. If one of the four was in use, it's ticked on Presets after the start.

## Style links on Bars / Widgets (0.38.108)
1. Settings > Bars > Shared by every bar > Bar style: the Bar style page opens (title "Bar style", "Bars › Bar style" under it), Bars stays highlighted on the left. Back goes to Bars.
2. Settings > Widgets > Shared by every widget > Widget style: the same, with Widgets.
3. Appearance > Bar style / Widget style still say "Appearance › …", highlight Appearance and Back goes to Appearance. Pick a style card on a page opened from Bars: it stays on that page and Back still goes to Bars.
4. Appearance > Windows and time: the row says "Match Windows 11 colour to vShell's theme"; open it - the tick box on that page says the same.

## Radarr plugin (0.38.109)
1. Settings > Plugins: Radarr is in Media. Add it, open it: enter the address (http://host:7878) and the API key (Radarr: Settings > General), Test connection says "Connected - Radarr 5.x". After the test, "Adding movies" has Quality profile and Root folder pickers with Radarr's own (plus "The first one in Radarr").
2. A wrong key: Test says "Radarr refused the API key (401)"; a wrong address / URL base: "Radarr answered 404 - is the address right …". Sonarr's address: "That address is Sonarr, not Radarr."
3. Add a Radarr module to a bar: the icon, a badge with the queue count when something is downloading (red if one has a warning / failed), "64% · 12 min" next to it. Hover: the queue, the next release, health errors. Untick Badge / progress in its settings and they go. "Hide while nothing is in the queue": it disappears with an empty queue, comes back when something is grabbed (or Radarr has a health error / can't be reached).
4. Click it: the popup with Queue / Upcoming / Recently added / Missing (Queue and Missing show their counts on the tab). Queue rows have a progress bar; right-click > Remove from the queue… asks first and the download goes from Radarr and the client. Upcoming lists monitored movies with "In cinemas / Digital / On disc <date>" within "Upcoming: days ahead". Missing: the magnifier button says "asking…" then "Searching ✓" and Radarr's Activity shows the search; Search all does the same for all of them. Click any movie: its page in Radarr.
5. Untick tabs in the module settings: one left = just that view, no tab strip. Posters off: coloured dots instead.
6. Put it in a widget: the views show in the widget; switching tabs doesn't change the widget's size; buttons and tabs work with one click (no widget drag).
7. Run box: !ra = the queue (Enter opens the movie in Radarr); with an empty queue, what's coming out next. !ra dune + Enter: results with posters. One not in Radarr has Add: "Added ✓ - Radarr is looking for it" and it shows up in Radarr with the profile / folder / availability from the Plugins page. One you have but isn't downloaded has Search now. Change the keyword on the Plugins page: the new one works and the hint follows.
8. Notifications (Plugins > Radarr > Notifications): let Radarr import a movie - "Downloaded: Title (Year)" with its quality; a failed download - "Download failed: …" as an alert; turn on Grabbed and grab one - "Grabbed: …". Restart vShell: nothing old is announced again. Notifications come without a Radarr module too (within 2 minutes).
9. Remove the plugin: the module says to add it in Settings, no more polling of Radarr in the log.
## Sonarr plugin (0.38.109)
1. Settings > Plugins > Media > Sonarr: address (http://host:8989), API key, Test connection says "Connected - Sonarr 4.x" (or 3.x). After the test, "Adding shows" has Quality profile / Root folder pickers; Radarr's address says "That address is Radarr, not Sonarr."
2. A bar module: badge with the queue count, progress and time left, hover lists the queue as "Show S02E03 - 45% · 12 min left" and the next episode. Hide while nothing is in the queue works as Radarr's.
3. Popup Queue: a season pack is one row ("S01E01-E08", "8 episodes"); right-click > Remove from the queue… takes the whole pack out of Sonarr and the client.
4. Upcoming: episodes in the next "days ahead" with "tonight 8:00 pm" / "Thu 9:00 pm" (12-hour unless Use 24-hour is on); two episodes of a show airing together are one row; one that aired in the last 12 hours shows "Aired" until it's downloaded. Recently added: imports, a season pack as one row.
5. Missing: the total, one row per show with its missing episodes; the magnifier searches those episodes ("Searching ✓", visible in Sonarr's Activity); right-click > Search for all of <show>'s missing episodes; Search all. On Sonarr 3 the list is newest first too.
6. Run box: !so = the queue (or what's airing next); !so severance + Enter: results with network, seasons, "In your library · 18 of 19 episodes" / "Not in Sonarr". Add: the show appears in Sonarr with the profile / folder / Episodes to monitor from the Plugins page (on Sonarr 3 too - it needs a language profile, the first one is used). Search for missing on one you have.
7. Notifications: an episode imported - "Downloaded: Show S02E03" with its title and quality; a season pack - one "Downloaded: Show S01E01-E08" with "8 episodes". Failed / health / grabbed as Radarr's.
## Tdarr plugin (0.38.109)
1. Settings > Plugins > Media > Tdarr: the server's address (http://host:8265), no API key: Test connection says "Connected - Tdarr 2.x, 1 node". With authentication on in Tdarr and no key: "Tdarr wants an API key (401) …"; with a key from Tools > API Keys it connects.
2. Bar module: the icon, a badge with the jobs running, "312 queued"; with every node paused "paused" in amber. Hover: the jobs with % and ETA. "Hide while nothing is running" hides it when idle.
3. Popup: RUNNING lists each job (file name, Transcode CPU / GPU or Health check, fps, "4.2 GB → ~2.1 GB", progress bar, "64% · 0:05:12") updating every few seconds; right-click > Cancel this job… asks, and the job stops in Tdarr. NODES: a dot (amber paused), the worker limits, CPU / memory; the pause button pauses it in Tdarr (running jobs finish), again resumes; right-click > Transcode GPU workers > One more / One fewer changes Tdarr's limit. LIBRARY: in the queue / done / errors / saved match Tdarr's home page.
4. Module settings: untick Running jobs / Nodes / Library totals and those parts go; in a widget the same view, buttons work with one click.
5. Run box: !td lists the running jobs (Enter opens Tdarr) and "Pause <node>" / "Resume <node>" rows - Enter does it, "Paused <node> ✓" in the status line.
6. Notifications: make a file fail (a broken file in a health check) - "Tdarr: a file failed" with the error counts. Stop a node - "Tdarr node disconnected: <name>" after about two checks, start it - "Tdarr node back". Turn on "finished its whole queue" and let a small queue run out - "Tdarr has finished its queue". Restarting vShell doesn't repeat any of them.
## Equal module widths in auto-width widgets (0.38.110)
1. A widget with Width = auto, stacked vertically, with a Clock, a CPU graph and a Memory bar: all three are as wide as the widest one (the graph and bar fill the width) - before, each kept its own width.
2. Add a Note to that widget: the note widens to the widest module (or, if the note is widest, the others fill its width).
3. Horizontal and Grid widgets, tabbed widgets and widgets with a fixed width look as before.
## Module strip instead of the live preview (0.38.111)
1. Settings > a bar (no zones): under the summary there's no live preview any more - a "Modules, as on the bar" card shows a chip per module (glyph + name) in the bar's order, with + Add modules and Show on screen on the right.
2. Click a chip: the Modules page opens with that module's settings. + Add modules opens the Modules page on the gallery. Drag a chip onto another: the module moves on the real bar straight away.
3. A full-width bar with Three zones: the strip shows Left / Centre / Right columns; drag a chip into another column - its zone changes on the bar. Turn Three zones off on the Layout tab: the strip becomes one row.
4. Lots of modules (or make the Settings window narrow): the names shorten (CPU, Music, Start …), then only glyphs show (hover for the name), then the chips wrap onto more lines - never a sideways scrollbar. Widen the window: the names come back. With zones, a crowded Right zone goes to glyphs on its own while Left keeps its names.
5. Show on screen: an accent outline blinks around the real bar for about 2 seconds, then fades; clicks go through it. On a second monitor it outlines the bar there.
6. Settings > a widget: the same strip ("Modules, in the widget's order"). Show on screen outlines the widget. A widget set to slide away that has slid away comes out, is outlined and stays out a moment, then slides away again as usual.
7. A widget with Show this widget off: Show on screen says "Not on screen right now" for 2 seconds.
8. The Modules page (Modules tab): no live preview; the strip has Show on screen too, and clicking / dragging chips works as before.
9. Ctrl+drag a module on the bar itself while its Settings page is open: the strip follows.
## Module strip titles (0.38.112)
1. Settings > a widget: the module strip's title reads "Modules in the widget".
2. Settings > a bar: the module strip's title reads "Modules on the bar".
## New full-width bars start with Three zones (0.38.112)
1. With a full-width first bar, Settings > Bars > Add bar: the new bar's Layout tab has Three zones on, and its module strip shows Left / Centre / Right.
2. With a floating or dock first bar (or a Floating / Dock bar style): Add bar - the new bar is not full width and Three zones is off.
3. Existing bars keep their Three zones setting.

## Collapsing a widget snapped to the bottom (0.38.113)
1. Snap a widget with a header and Collapsible on to the left edge, just above the bottom bar. Collapse it: it rolls up under its header and the header stays where the top was (it doesn't drop down to the bar).
2. Expand it: it opens back down to exactly where it was, above the bar - not off the screen.
3. Same with a widget snapped to the screen's bottom edge (no bar), and one lined up with another widget's bottom.
4. A widget snapped below the collapsed one stays where it is while it's collapsed.
5. Restart vShell with the widget collapsed: it stays where it is; expand it - it opens and snaps back above the bar.
6. A widget snapped to the top of the screen still collapses / expands as before.

## Terminal: hide the button (0.38.113)
1. Settings > a bar's Terminal module: tick "Hide the button (only the keys open it)" - the terminal icon goes from the bar and the modules close up; the note says the keys still open it, and the Title row goes.
2. Press the keys (Ctrl+Alt+T by default): the terminal drops down from that bar as before (attached to it if Attach is on); again and it hides.
3. Set the keys to Off with the tick on: the button comes back on the bar, and the note says to pick keys first. Pick keys again: it goes.
4. Untick it: the button (and its title) are back.
5. A widget with a Terminal button (Show the terminal in the widget off): the same tick; with it on, a widget holding only that module hides, and the keys still open the terminal on the widget's monitor.
6. Restart vShell with the tick on: the button stays hidden and the keys work.

## Settings backups (0.38.114)
1. Settings > System: a Backups section right under Updates, with "Back up my settings automatically" ticked. Change any setting: a "Daily" backup appears (in %APPDATA%\vShell\backups, and in the list after reopening Settings). Change more: no second daily one the same day.
2. Back up now: a "Made by you" entry at the top of the list.
3. Change something visible (a bar's colour), then Restore the backup from before it: asks, vShell restarts, the old colour is back. The list now has a "Before a restore" entry; Restore that: your change is back.
4. Export…: saves "vShell settings <date>.json" where you pick. Import… that file: asks, restarts with it. Import a file that isn't vShell settings (any other .json / .txt): says it isn't, nothing changes.
5. Damaged settings: close vShell, make a backup exist (step 2), then open settings.json in Notepad, delete half of it and save. Start vShell: your bars come back as in the backup, a notification says your settings couldn't be read and a backup was used, and the backups list has a "Couldn't be read" entry (no Restore button on it).
6. Same with no backups at all (move the backups folder away first): vShell starts with new settings, the notification says it started fresh, and the damaged file is kept in backups.
7. "Open the backups folder" opens it in Explorer. More than 7 daily backups (change the dates on the files to older days): only the newest 7 stay after the next one; "Made by you" ones are never removed.

## Crash restart and messages in front (0.38.114)
1. Normal use: nothing changes; "Crash:" never appears in the log.
2. (If you can make vShell crash on a background thread) it starts again by itself within a few seconds, with a notification "vShell closed unexpectedly and started again"; the log has "Crash:" with the error. A second crash within 2 minutes: it stays closed and the Windows taskbar comes back.
3. A widget with Notes: Delete the note / Remove done items / a wrong due date - the question or message shows in front of your windows (not hidden behind them) and the widget isn't stuck.
4. A run box: type something Windows can't open - the message shows in front. A palette command that fails: same.
5. NPM plugin (rebuilt): Disable / Delete / Renew ask with the question as the message and the action as the title (they were swapped).

## Rain notification in the background, settings files split (0.38.114)
1. Notifications > Weather: rain on, a Weather module with a place. With rain coming in the next hour, the "Rain expected around …" notification still comes (within 15 minutes and a few seconds of starting).
2. Unplug the network: the log has one "Notifications (Weather): …" line; other notifications (a service going down) still come on time.
3. The settings code was moved into more files (no changes in it): every Settings page (Appearance and presets, a bar's / widget's modules and module settings, Startup apps, Plugins, Notifications, System with Updates and Backups, About) opens and works as before.

## Popups in the widget style (0.38.115)
1. Click a Weather module on a bar: the forecast popup has the same background, corners, border, shadow (and glass edge, if on) as the Clock's calendar popup and your widgets - not a plain dark card with a thin outline.
2. Change the widget style (Settings > Appearance / Styles: corners, border, background opacity or gradient) and open it again: it follows.
3. Give the Weather module its own font size or text colour: the popup's text uses them too.
4. Now playing (click the module for the bigger card) and the Tray's arrow (hidden icons): the same widget-style look.

## Shorter bar menu (0.38.115)
1. Right-click an empty part of a bar: Settings…, the Ctrl+drag tip, then Restart vShell and Exit vShell - no Add bar, Remove this bar, Task Manager, Run… or File Explorer.
2. A dock-style bar: "Keep the dock centred on its monitor" is still there.
3. As the Windows shell: both exit items (start Explorer / leaves no shell) are still there.
4. Settings > Bars: Add bar and removing a bar still work.

## Shell mode removed (0.38.115)
1. Start vShell as usual: bars, widgets, tray icons (your Tray module), the taskbar module with progress / badges, and hiding Windows' taskbar all work as before.
2. Settings > System: no "Windows shell" heading - the page starts with Updates, then Backups, then Behaviour with "vShell - run at login (Recommended)" first. Ticking / unticking it still adds / removes vShell in Startup apps.
3. Settings > Startup apps: the list, Enable / Disable / Remove and Refresh work; no "Start these apps when vShell runs as the shell" option.
4. A bar with "Reserve window space": maximised windows stop at the bar; untick it - they go under it again. Two bars on the same edge each keep their space.
5. Widgets stay on the desktop layer (behind windows, in front of the wallpaper); Show desktop (Win+D) leaves them visible as before.
6. Restart Explorer (Task Manager > Windows Explorer > Restart): within a few seconds the Windows taskbar hides again, the bars keep their space and the tray icons come back to vShell.
7. Per-monitor wallpapers and a slideshow still change the wallpaper.
8. vShell's tray icon menu and a bar's menu: only "Exit vShell" (no "Exit and start Windows Explorer" / "leaves no shell!").
9. Exit vShell: Windows' taskbar comes back, and no File Explorer window opens.

## Taskbar actions like Explorer's (0.38.116)
1. Notepad: type something, File > Save as… (dialog open). Click another app, then click Notepad on the vShell taskbar: the Save as dialog is in front (not hidden behind Notepad's window). Same from its thumbnail preview.
2. With that dialog still open, click Notepad's button while it's in front: nothing minimises (Notepad can't be used until the dialog closes) - the dialog stays in front.
3. Without a dialog: click the app in front - it minimises (with the usual animation); click again - it comes back.
4. An app that minimises to the tray (e.g. Discord / Steam with that option): taskbar right-click > Minimize does what its own minimise button does.
5. Notepad with unsaved text, behind other windows: right-click its button > Close window - Notepad comes to the front and asks "Save changes?". Middle-click on a thumbnail and the thumbnail's X: the same.
6. Close all windows of a group with unsaved changes: each one asks, in front.
7. "Hide the Windows taskbar" on: restart Explorer, or open something that makes Explorer show its taskbar - it disappears again almost at once (it used to stay for up to 2 seconds).

## Volume mixer (0.38.116)
1. Click the Volume module on a bar: a popup in the widget style - the default output device at the top (click it for the list), "All sound" with the master slider, then a row per app (icon, name, slider, %), then Sound settings / Windows volume mixer.
2. Play something (YouTube in Brave, Jellyfin): the light part inside its slider moves with the sound; the "All sound" one too. Pause: it stops.
3. Drag an app's slider, click on the track, or scroll on it: that app gets quieter / louder (check in Windows' own Volume mixer); the % follows. The other apps don't change.
4. Click an app's icon: it mutes (icon and slider fade, "muted"); click again: back. Drag a muted app's slider up: it unmutes.
5. Change the volume with the keyboard keys while the mixer is open: "All sound" follows within a second.
6. Wave Link: apps routed to Browser / Media / Game / Voice chat show under a heading with that device's name once they play, and stay listed until the mixer closes. System sounds and idle apps on those devices aren't listed; everything on the default device is.
7. Click the device name at the top and pick another: it becomes the default output, and its apps are listed.
8. Start / close an app that plays sound while the mixer is open: it appears / goes within a second.
9. Close the mixer (click elsewhere, or the module again): it closes; vShell's CPU use goes back to what it was (nothing is read while it's closed).
10. Tray module, Windows 11's sound icon: right-click > "Volume mixer" (first, in bold) opens the same mixer at the tray; "Windows volume mixer" opens Windows' page. Left-click: the small volume menu has "Volume mixer" too.
11. A Volume module in a widget: clicking it opens the mixer next to the widget.

## Mixer from the tray icon, visible level meters (0.38.117)
1. Left-click Windows' sound icon in the Tray module: the volume mixer opens (not the old slider menu). Click it again: it closes. Right-click: the menu still has "Volume mixer" first.
2. Play music (Jellyfin, YouTube): a thin line under that app's slider (and under "All sound" when it plays on the default device) moves with the sound, clearly visible in your theme. Quiet passages still move it a little; silence / pause: it drops to nothing.
3. The meter doesn't depend on the slider: turn an app down - its slider fill shrinks, the level line still shows what it plays.

## Volume module options (0.38.117)
1. Settings > a bar's Volume module: sections "On the bar", "Mouse" and "Volume mixer". (The old note about clicking to mute is gone.)
2. Show the output device: Short - "System · 64%" (Wave Link) / "Speakers · 64%"; Full - the whole name; Off - only the percentage (the device is in the tooltip either way). Switch the device in the mixer: the name follows within a few seconds.
3. Show the sound level: play music - a thin line under the module moves with it; pause - it drops; mute - it stays at nothing. Lock the PC / a full-screen game over the bar: it stops (no CPU use). Off: no line.
4. When muted, show: Text - "muted"; Icon - only the crossed-out speaker (with the speaker icon on; without it, "muted" anyway); Dim - the module faded, the percentage still shown.
5. Clicking it: Mixer - the mixer; Mute - mutes / unmutes; Settings - Windows' sound settings. With Mute / Settings the "Volume mixer" section of the settings goes away (the tray's sound icon still opens the mixer).
6. Middle-click on the module: mutes / unmutes (whatever the click does); untick the option: middle-click does nothing.
7. Volume mixer section: "Only apps playing right now" on - idle apps (Steam, System sounds) aren't listed until they make a sound; "Show the sound levels" off - no lines under the sliders; "Show the output device at the top" off - the mixer starts with "All sound".
8. A widget with a Volume module: tick "Show the volume mixer in the widget" - the widget shows the mixer itself (device, All sound, the apps), sliders / mute / device picker work with one click, and the widget grows / shrinks as apps come and go. Untick: the volume as text / meter again (with the bar options). Cover the widget (maximised window, Win+L): the levels stop until it's seen again.

## Volume module per device, app output device (0.38.118)
1. Settings > a Volume module: "Sound device" lists "Default output device" and every connected device (full names - you have two "Speakers"). Default: the module follows whichever device Windows plays on.
2. Pick "Game (Elgato Virtual Audio)": the module shows Game's volume (70% now), scrolling / middle-click mute / the level line change Game - not System. "Show the sound device's name: Short" shows "Game · 70%".
3. Add a second Volume module set to "Browser": both on the bar, each showing and changing its own device.
4. Click a device-bound module (click = Mixer): the mixer's top shows that device (no picker arrow - it's set in the module), "All sound" is that device's volume, its apps are listed first.
5. Unplug / disable a picked device: its module shows "n/a", the tooltip says it isn't connected; Settings shows it as "<name> (not connected)" until you pick another. Plug it back in: it works again.
6. Style: Text - no "Show text" / meter width / colour rows; switch to Meter - they appear.
7. Volume mixer: right-click an app (Brave): "Brave Browser plays on" with Default output device and every device; the current one is ticked (Browser for you). Pick Media: Brave moves under the Media heading within a second, and Wave Link shows it on Media. Pick "Default output device": back to following the default. Windows' own Settings > Sound > Volume mixer shows the same choice.
8. System sounds have no right-click menu. The tray's sound icon mixer has the same right-click on apps.

## Mixer's device row named after the device (0.38.119)
1. A Volume module set to Media: click it - the first slider row says "Media" (hover: the full name), not "All sound".
2. A module on the default device (or the tray's sound icon): the row says the default device's short name ("System" with Wave Link). Pick another device at the top: the row's name changes with it.
3. A device that isn't connected: the row says "All sound" and the top says it isn't connected.

## Click the meter, mixer columns in a widget (0.38.119)
1. A Volume module with Style = Meter (bar or widget): click near the right end of the meter - the volume jumps to about that (95 %); click the middle - 50 %. Press and drag along it - it follows. The mixer doesn't open (clicking the icon / text still does what "Clicking it" says).
2. In a widget: pressing on the meter doesn't drag the widget.
3. Style = Text: clicking does what "Clicking it" says, as before. The settings note about the meter only shows for Meter.
4. A widget with the mixer in it: Layout = Columns - the device's column first (its short name), a line, then a column per app: percentage at the top, an upright slider with the level line beside it, the icon (click to mute) and the name; an app on another device has that device's short name under it. Drag / click / scroll a slider: up is louder. Right-click a column: the "plays on" menu.
5. Many apps in a fixed-width widget: the app columns scroll sideways. Layout = Rows: as before. The popup mixer (bar / tray) always uses rows.

## Mixer closes on a second click (0.38.120)
1. A Volume module in a widget (text / meter, not the mixer in the widget): click its icon or text - the mixer opens. Click it again - it closes and stays closed. A third click opens it again.
2. The same on a bar, and from the tray's sound icon.
3. Open the mixer, click somewhere else (the desktop, another window): it closes; then click the module: it opens (not swallowed).
4. The same click-again-to-close for the Clock's calendar, Weather's forecast, Notifications' history, Now playing's card and the Tray's arrow - on a bar and in a widget.

## Mixer in a widget: several devices, no apps, no links (0.38.120)
1. A widget's Volume module with "Show the volume mixer in the widget": the "Sound device" dropdown is replaced by a "Sound devices" section - a tick per device (and any picked one that isn't connected, marked so). None ticked: the mixer shows the default device as before.
2. Tick Game, Browser and Media: the mixer shows three volume rows named Game / Browser / Media (no device line at the top), each with its own slider, mute and level; the order stays as listed in Settings whatever order you tick them in. Columns layout: three device columns.
3. With devices ticked, the apps on those devices are listed under headings with their device's name (in columns: the device's short name under each app).
4. Volume mixer > untick "Show apps": only the device rows - no line, no apps, and "Only apps playing" goes from the settings. Tick it again: the apps are back.
5. Untick "Show the Sound settings / Windows volume mixer links": the links at the bottom go (in the widget and in the popup from a bar).
6. Unplug a ticked device: its row says "<name> (off)" and n/a, faded; plug it back in: it works again within a second.
7. A bar's Volume module (popup mixer): still one device with the picker at the top; the device list only applies to the widget's mixer.

## CPU module: Task Manager figure, smoothing, clock speed, Cores (0.38.121)
1. A CPU module next to Task Manager's Performance tab: the percentages match (within a point or two, as both sample once a second). Settings > the CPU module > untick "Measure like Task Manager": the number drops back to the old (usually lower) figure.
2. "Smooth the load over" 5 seconds: the number and graph change gently instead of jumping every second; Off: as before.
3. "Show the clock speed": "CPU 16% · 4.5 GHz" - it rises under load and drops when idle (compare Task Manager's Speed). Ring / Tile styles: the speed is the small line under the number.
4. Style = Cores: a row of small bars, one per thread (16 on this PC), each filling up with its load; a busy thread's bar is tall while the others stay low. Past the warning threshold a bar takes the warning colour. Meter width sets the row's width; works on a bar and in a widget.
5. Without any CPU module using these, vShell doesn't read the counters (no extra CPU use).

## Popups that open but can't be seen (0.38.122)
Seen once on monitor 1 only: Start, Clock, Notifications etc. opened (they took the keyboard) but drew nothing at all, until vShell was restarted.
1. Normal use: open Start, the Clock, Notifications, Downloads on each monitor - they look and behave as before, and the log has no "Popup from ... can't be seen" lines.
2. If popups on a monitor go invisible again: a moment after clicking, the popup should appear anyway. Either way the log has a "Popup from <module> can't be seen: loaded …, opacity …, draws …" line and then "can be seen now" or "still can't be seen" - send that part of the log.

## Widgets snapped to a bar can slide away (0.38.123)
1. Drag a widget (Climate Control) against a bottom bar until it snaps above it. Right-click it: "Slide away at the bottom edge" is there (it used to be greyed out - only a bare screen edge counted). Turn it on, move the mouse away: it slides down towards the bar and the accent glow shows along the bottom of the screen. Push the mouse against the bottom of the screen (over the bar, in front of the widget): it slides back.
2. The same under a top bar: "Slide away at the top edge", it slides up towards the bar and comes back with the mouse against the top of the screen.
3. Snapped against a bar and a side edge (a corner): the menu asks which way, both are offered; Settings > the widget lists "Slides towards" with both.
4. Turned on, then drag it away from the bar (not against any edge): sliding away turns itself off as before. Settings > the widget without a snap says "isn't snapped to a screen edge or a bar".
5. A widget snapped to a bare screen edge (Notes at the top of the ultrawide) slides away as before.

## Widgets snapped to each other slide away at the edge they're against (0.38.124)
1. Snap a widget beside another one along the top of the screen (e.g. right of Notes, both at the top): its own link is to Notes, but right-click > "Slide away at the top edge" is offered. Turn it on: it slides up and comes back with the mouse against the top, in front of it.
2. The same for a widget stacked above another one whose bottom sits on a bottom bar, or against the left / right edge in a column: the edge it's against is offered.
3. Against two edges (a corner, or the right edge and a bar): the menu asks which way; Settings > the widget says "Against the right and bottom edges" and "Slides towards" lists both.
4. A widget in the middle of the screen (nothing within the edge gap + a few pixels): the item stays greyed out, Settings says it "isn't against a screen edge or a bar".
5. Turned on, then drag it away from that edge: sliding away turns itself off as before.

## Uptime module: counts from, formats, two lines, restart dot, details (0.38.125)
1. Unchanged settings: it looks as before ("Up 3d 4h", one line). Hover: running for (days, hours, minutes), last restart, "Click for details".
2. Settings > the Uptime module > Counts from: **Last restart** = the same as Task Manager's Up time. **Awake time**: after the PC has slept, smaller than Last restart by the time asleep (the tooltip says "Awake for … of it"). **Last wake-up**: put the PC to sleep for a minute and wake it - it starts again from 0m; before any sleep it equals Last restart. **Sign-in**: since you signed in (a minute or so less than Last restart after a fresh start).
3. Format: Short "3d 4h", Long "3 days, 4 hours" (singular "1 day, 1 hour"), Decimal "3.2d" (under a day "4.2h", under an hour "12m"). "Show minutes too": "3d 4h 12m" / "3 days, 4 hours, 12 minutes" once it's a day or more.
4. Two lines on a bar: "Up 3d 4h" with "since today 5:36 PM" under it in smaller dim text, fitting the bar's height (try a slim bar too); Awake time says "awake, of 3d 4h up", Last wake-up "awake since …", Sign-in "signed in …". The modules beside it don't move as the minutes change.
5. Two lines in a widget: a tile - "Up" small and dim, the time big and bold, "since …" under it; the widget row grows to fit. "Text in front" empty hides the "Up" line.
6. Restart needed: on a PC with a Windows Update waiting for a restart (Settings > Windows Update says "Restart required"), a small dot in the warning colour appears after the time within a minute; the tooltip and popup say "Restart needed: Windows Update has updates to finish" and the popup's Restart button is the highlighted one. Untick "Dot when Windows needs a restart": no dot (the popup still says it). Nothing pending: no dot.
7. Click the module (bar and widget): the details popup - Up (long form), Last restart (full date), Awake (with %), Last sleep ("not since it started" before any), Woke up (after a sleep, with how long ago), Signed in, Fast Startup (On / Off with what that means - compare Control Panel > Power Options > "Choose what the power buttons do"). Click the module again: it closes.
8. Popup > Sleep: the popup closes and the PC sleeps. Popup > Restart: asks "Restart Windows now?" - No does nothing, Yes restarts.
9. Times follow the "Use 24-hour times" setting.

## Network Tile shows upload (0.38.126)
1. A Network module, Style = Tile (bar and widget): the line now has two series - download in the graph colour (Main Accent) and upload in the upload colour (Secondary Accent, or the module's second colour if set), on the same scale as the Line style. Start an upload (copy a big file to a NAS): the upload line rises.
2. Ring style and the Text / Line / Bars styles look as before.
3. Downloads plugin, Tile style with "Show upload" ticked: its upload line shows too; unticked, only download.
4. CPU / Disk / HWiNFO tiles: still one line (Disk space: one meter).

## Taskbar icon size (0.38.127)
1. Settings > a bar > Taskbar module: "Set the icon size myself" is off by default and the taskbar looks as before (icons fit the bar's height).
2. Tick it: an "Icon size" slider (16-48, starts at 24) shows. Move it: pinned apps that aren't running change size straight away; with "Combined buttons show" = Icon only, the running apps' buttons do too. The buttons get wider for big icons so they don't touch.
3. "Icon and app name" / "Icon and window title", or "Never" combine: buttons with text keep their small 16 px icons; pins that aren't running still follow the slider.
4. A slim bar with a big size (e.g. 48 on a 30 px bar): the icons shrink to fit inside the bar instead of spilling out.
5. Dock animations (Dock bar, icons only): Lift / Glow / Magnify / Bounce still work at the new size; the active line under the icon scales with it.
6. Untick it: back to the automatic size.

## Popups that draw nothing - find which part (0.38.127)
Seen again on 30 Oct on every monitor and bar: "loaded True, opacity 1, draws nothing ... still can't be seen after that" for Start, UniFi and Usage. Everything else (bars, widgets, a terminal window opened after it) still drew. A dump ruled out leaked popups and WPF thinking the monitors were off.
1. Normal use: popups look and behave as before.
2. When it happens again: the first blank popup tries, about a quarter of a second apart: showing it again, turning WPF's drawing back on, no shadow cache, no shadow, no graphics card - it should show up within about two seconds. The log has a "Popup from ... can't be seen" line (with WPF's state for the blank popup and for a good one earlier), a line per step tried, then "can be seen now (<step>)". Popups after that open straight away. Send that part of the log - the step that worked tells us the cause.
3. Check that the popups after it still look right (they may have no shadow, or be drawn without the graphics card, until a restart).

## Blur background reads a small wallpaper (0.38.128)
The blur behind bars / widgets used to load Windows' wallpaper at full size (30 MB or more for a 4K photo, held by the graphics side after it was drawn) only to shrink it to 1/8. It now loads it at twice the blurred size; Center and Tile still load it in full (they draw it at its own size).
1. Blur or Frosted on a bar / widget, with a Windows wallpaper (not one of vShell's): the blur looks as before on every monitor - same colours, same place behind each bar / widget.
2. Windows' wallpaper fit Fill, Fit, Stretch and Span: each still matches the desktop. Center and Tile: as before.
3. Change the wallpaper: the blur follows within a few seconds.
4. Task Manager > Details > vShell.exe, Memory (private working set), shortly after start: compare with 0.38.127 on the same wallpaper - lower, most with a large photo.

## Edit, rename and duplicate saved styles (0.38.129)
Appearance > Bar style and Widget style have Edit, Rename, Duplicate and Delete next to the Presets / Saved tabs. They all work on the style in use (the ticked card). Edit / Rename / Delete need a saved (★) style; Duplicate works on any, built-in too.
1. Bar style: pick a built-in (e.g. Soft), click Duplicate: the name box suggests "Soft copy"; OK - the gallery opens on Saved with "★ Soft copy" ticked, and your bars look the same. A built-in name (e.g. "Sharp") or one already saved is refused.
2. Click Edit: the page shows the style's picture (big: the layout picked, small: all four), tabs Classic / Floating / Dock / Minimal and that layout's rows. Change Height, Corner radius, Blur, Glass edge on Classic: the pictures follow, and every Classic bar that follows the bar style changes as you drag.
3. Floating tab: Gap from the screen edge and Round all four corners show (only there). Dock / Floating: turn Show an outline off - the outline rows go; on again - thickness 2. Minimal: no outline rows. You can edit a layout none of your bars use (e.g. Dock) - nothing on screen changes, the small picture does.
4. Undo changes: everything goes back to how it was when you clicked Edit (bars too). Done: back to the gallery, the card shows the new look. Close and reopen Settings / restart vShell: the changes are kept.
5. Rename: give it a new name - the card, the Appearance summary ("bar style …") and the editor title show it; the bars stay as they are. Renaming to another saved style's name is refused.
6. Bars with their own look (Follow the bar style off) don't change while you edit.
7. Leave the editor with the sidebar, then open Appearance > Bar style again: it opens on the gallery.
8. Widget style: the same with Duplicate / Edit / Rename / Delete - Corner radius, Space between modules, Shadow, Blur, Glass edge, Show an outline / Thickness / Opacity / Colour; widgets that follow the widget style change as you go.
9. Themes keep their own copy of a style: editing a saved style doesn't change a saved theme that has it.

## Automatic graph scale for Network and Downloads (0.38.130)
Network and Downloads have "Automatic scale" in the Graph section: the top of the graph is the fastest speed it shows (the last minute for a line / tile, what fits for bars), rounded up to 1, 2, 5, 10, 20, 50 … (at least 1 Mbit/s / 0.1 MB/s). New modules have it on; existing ones keep their fixed scale until you tick it.
1. Network module, Style Line: tick Automatic scale - "Graph full scale" goes away. Start a big download: the line fills the graph; hover: the tooltip says "Graph: up to 200 Mbit/s" (or MB/s with "Show speeds in Mbit/s" off - the scale is then a round number of MB/s). Stop it: about a minute later the scale drops back and the small traffic fills the graph again.
2. Bars, Meter, Tile, Ring: the same - Ring / Meter fill as a share of the recent top, the tile's line scales too. Upload (second colour) uses the same scale.
3. Untick it: the fixed full scale is back (the graph starts afresh, it doesn't show the old samples on the wrong scale). With it on, "Change colour past threshold" does nothing.
4. Add a new Network module: Automatic scale is on.
5. Downloads on a bar (Line / Bars / Meter / Tile): Automatic scale in its settings, with a note; the tooltip has "Graph: up to … MB/s" above "Click for the list". Ring (queue progress) has no scale setting and is as before.
6. Downloads widget as a List with "Speed graph along the top": Automatic scale there; the label in the graph's corner shows the scale ("2 MB/s", "20 MB/s") and follows it.
7. Plugin API: build.cmd's plugin API list has the new lines (InfoModule GraphScale, Reading AutoScale / ScaleMin, MetricView FullScale / ScaleFor, ModuleConfig AutoScale) and nothing removed - still API 1.

## Jellyfin plugin no longer claims the Proxmox module (0.38.131)
The Jellyfin plugin's manifest listed the Proxmox module type as its own, so vShell could think a Proxmox module came from Jellyfin.
1. Settings > Plugins: the Jellyfin card doesn't list a Proxmox module.
2. With Proxmox not added and a Proxmox module on a bar: its settings say it comes from the Proxmox VE plugin (not Jellyfin), with Proxmox's icon.
3. dist\plugins.json after a build: jellyfin's "types" doesn't contain Proxmox.

## Palette commands for Radarr, Sonarr, Seerr, Tdarr and UniFi (0.38.131)
Open the command palette (or type / in a run box) with each plugin set up:
1. Radarr: "search for every missing movie" (asks first - Enter twice), "Search for <movie>" for each missing movie (once a Radarr module has shown Missing), "Open Radarr's queue / calendar / missing movies".
2. Sonarr: "search for every missing episode" (asks first), "Search for the missing episodes of <show>" (one per show, with the episode codes), "Open Sonarr's queue / calendar / missing episodes".
3. Seerr: "Open Seerr / requests / requests waiting"; with an API key that can manage requests, "Approve the request for <title>" and "Decline …" (asks first) for each waiting request - after it, the request is gone from the list and from the module.
4. Tdarr: "Open Tdarr", "Pause / Resume Tdarr node <name>" (the word follows its state), and with two nodes or more "Pause every Tdarr node" (asks first) / "Resume every Tdarr node".
5. UniFi: "Open UniFi Network / Protect", "Open camera <name> in Protect", and for each Protect light that's online "Turn on <light>" (forced on) / "Turn off <light>" (back to its own mode).
6. A plugin that isn't set up adds none.

## Run box keywords for Proxmox, Home Assistant and 3D printers (0.38.131)
Each has "Run box keyword" on its Plugins page section (px, ha, 3d by default); the run box's empty hint lists them.
1. !px web - Proxmox's palette commands matching "web" (Start / Shut down / Reboot / Stop …); the ones that ask need Enter twice. !px on its own lists the first ones.
2. !ha kitchen - Home Assistant's commands for "kitchen"; !ha light 40 sets a brightness like the palette does.
3. !3d pause - the printer commands for "pause"; !3d preheat pla etc.
4. Change a keyword (e.g. px to pve): !pve works, !px doesn't. With the plugin not set up, the keyword does nothing (it's a web bang or a search again).

## Jellyfin module (0.38.131)
Needs the Jellyfin connection (an administrator's API key sees every session). Add "Jellyfin" from the module gallery (Smart home & more).
1. On a bar: the film icon, dim while nobody plays anything; with someone playing, a badge with the number, and "1 transcoding" in amber when one of them is transcoding. Hover: who plays what, paused / transcoding. Hide when nobody is playing anything: it goes away and comes back when someone starts.
2. Click it: the popup - PLAYING NOW (title - for an episode "Show · S02E05 · Title" -, user · device · app, "direct play" / "direct stream" in green or "transcoding" in amber, a progress bar, "18:04 left" or "paused"); hover a row for the position and, when transcoding, why (video codec not supported …).
3. Click a playing row: Pause / Resume and Stop work on that device within a few seconds; Open it in Jellyfin opens the item's page, Open Jellyfin's dashboard the dashboard.
4. RECENTLY ADDED: the newest movies, episodes and albums (how many: the module's setting) with "movie · 2 h ago"; click one to open it in Jellyfin. On an older server that wants a user, it asks as the first administrator; if it can't, the section says why (the rest still works).
5. LIBRARY: movies, shows, episodes, albums, songs (the ones with any).
6. In a widget: the same view, updating every few seconds while it shows.
7. Module settings: each section on / off, how many recently added, the Open button; Title on a bar.
8. Notifications page > Jellyfin: "Something new is added to the library" - add a movie in Jellyfin: within about 2 minutes "New movie: Name (2024)" (click opens it); four or more at once give one "5 new in Jellyfin" card. Nothing on the first look after start.
9. Palette: "Pause Anna's video" / "Resume …" and "Stop Anna's video" (asks first) for each session, "Open Jellyfin" / "Open Jellyfin's dashboard". !jf anna in a run box shows the same for Anna; !jf on its own lists them. The keyword is on the Plugins page.
10. Now playing's Jellyfin times / buttons work as before.

## Tdarr: failed files, requeue, daily summary (0.38.131)
Needs files in Tdarr's error tables (a transcode error / cancelled, or a health check error).
1. Tdarr module (widget, or the bar's popup): a FAILED (n) section between the nodes and the library totals - each file's name, "Transcode failed or cancelled" (amber) or "Health check failed" (red) and its size; hover for the full path. With no errors the section isn't there. Module setting "Failed files" turns it off; "How many jobs / failed files" limits the rows ("+ n more - Requeue all takes them all").
2. The requeue button on a row (or right-click > Requeue): "requeuing…", then the row goes; in Tdarr the file is back in the transcode (or health check) queue and gets picked up. Right-click > Copy the file's path.
3. Requeue all (top right of the section) asks first, then puts every failed file back (up to 500 of each kind) - Tdarr's error tables empty, the queue grows. An older Tdarr without bulk update: it does them one by one (the log says so).
4. Palette: "Requeue every failed file in Tdarr" (asks first; only while there are errors), says how many went back.
5. If Tdarr can't list the files (an older version), the section says "Couldn't read the failed files: …" and the rest still works.
6. Notifications > Tdarr: "Once a day, what Tdarr did" - leave vShell running past 9 AM the next day: "Tdarr: 42 files done, 18.5 GB saved, 2 failed", "In the last day · 120 in the queue now". Nothing the first day (it starts counting when vShell starts); restarting vShell starts counting again.

## Home Assistant graphs start with their history (0.38.131)
An Entities module with a graph on a number sensor (a temperature, power in W) or an on / off entity.
1. Restart vShell: the graph is full straight away (the last two minutes, from Home Assistant's recorder), then carries on live - no empty graph filling up from the right.
2. A sensor that changed during those two minutes: the step shows where it changed. One that didn't: a flat line at its value.
3. An entity the recorder leaves out (excluded in configuration.yaml), or Home Assistant unreachable at start: the graph fills up as before; the log says why for the unreachable case.
4. Changing the module's settings (a rebuild) keeps the graph as it was - no second backfill.

## Downloads: a torrent's actions, speeds on the tile (0.38.131)
1. Downloads list (widget, or the bar's popup), a qBittorrent torrent: left click still pauses / resumes. Right-click: its name, then Pause / Resume, Force start (ticked while it's forced - click again to turn it off), Move to the top of the queue, Recheck the downloaded data, Reannounce to the trackers, Remove (keep the files)…, Remove and delete the files…, Copy the name. The list doesn't redraw (or the popup close) while the menu or a question is open.
2. Force start: the torrent shows qBittorrent's forced state within a couple of seconds (hover: "Forced to start"); with queueing on it runs even past the active limit.
3. Top of the queue with torrent queueing off in qBittorrent: a message saying queueing is off. With it on: the torrent moves to the top.
4. Recheck: the state goes to checking, then back. Reannounce: more peers soon after (qBittorrent's Trackers tab shows the announce).
5. Remove (keep the files) asks first, then the torrent is gone from the list and from qBittorrent; the files stay. Remove and delete the files asks first (says it can't be undone), then removes it and its files.
6. SABnzbd jobs: no right-click menu (left click pauses / resumes as before).
7. Downloads module, Style Tile (bar and widget): the big number is "↓ 38 MB/s"; the line above says "Downloads · ↑ 2.1 MB/s · 3 · 45%". "Show the upload speed" off: no ↑ part; "Show the number of downloads" off: no count.

## Plugin settings tidy-up (0.38.132)
The rule now: a row only shows when it can do something. Ticking / unticking a box that others depend on redraws the page (it keeps its scroll position).
On the Notifications tab each checkbox is followed by its own rows (delay, filter, kind); a kind shared by several checkboxes comes after the last of them and shows while any is on. Kinds tied to a "0 = off" slider always show (a slider can't redraw while you drag it).
1. **Notifications tab, every plugin** - untick a notification: its "… show as" kind row (and its delay / filter rows) disappears; tick it: they come back, in the same place.
   - Komodo: Delay only with "stops / unhealthy" on; Updates kind under the image-update box; Problems / Good news kinds after the procedures box, while either "stops" or "procedure" is on.
   - Proxmox: Delay and Good news only with nodes or guests on; Problems always (the storage slider uses it).
   - 3D printers: order is now Finishes, Paused, Cancelled/fails (+ Problems kind), Starts, Progress every, Layer (+ Progress kind), Bed cooled (+ "Cooled down at" and its kind only while ticked).
   - Downloads: "Only these categories" and its kind only with "A download finished" ticked.
   - Jellyfin: "Leave out…" and "Playback shows as" only while start or stop is ticked; "vShell checks every 10 seconds" note at the end.
   - Seerr: "Available" set to Off hides the "Mine = …" note and its kind.
   - UniFi Network: Delay, Offline and Back online only with the device box ticked. Protect: "Only these cameras" hides when every event box is off.
   - Home Assistant: a note under Rules says they work without a module.
2. **Connection tab** - Proxmox and Home Assistant: type a new secret / token and press Test connection straight away: it's saved (the box empties) and the test uses it. Their slider now reads "Check every (seconds)". Seerr: "Request as user id" is now above the run box keyword. Komodo: the help (i) is on the API secret row. Downloads: both run box keywords have help, and the two intro notes read properly.
3. **Komodo module** - pick one Server: "Group by server" disappears and the list has no server header; the slider reads "How many". Back to All servers, grouping on: "How many per server". The button setting reads `Show the "Komodo" button`.
4. **Sonarr Connection** - Episodes to monitor = None: "Search for the missing episodes…" disappears; any other choice: it's back.
5. **Printer module** - untick "Show the webcam": Webcam, the fps / snapshot sliders and the note disappear. Webcam = Snapshots: only "Snapshots: one every"; Live: the fps slider plus "Snapshots (when the live stream doesn't work)".
6. **Downloads module** - Show = SABnzbd (on a bar or a Graph widget): "Show the upload speed" disappears and the bar shows no ↑ even if it was ticked before. List widget: untick "Speed graph along the top": Graph full scale / Graph colour disappear.
7. **Proxmox module** - "Allow deleting…" off: the long Delete note is gone; on: it shows.
8. **Home Assistant** - Entities module on a bar: no Layout row (in a widget it's there, with a one-line note); the not-set-up note shows when Home Assistant isn't connected. Climate module: no zones set up = no "Show the zones" box; add a zone in Zones… and it appears.
9. **Text in front** - Proxmox, UniFi Network, UniFi Protect and Climate on a bar: set "Text in front" (e.g. PVE): it shows dim before the module's text; empty = nothing. Not offered in widgets.
10. **Questions** - 3D printers > Remove this printer, and Home Assistant's "Add from Home Assistant…" / "Find…" with no connection: the boxes open over the settings window as before (now through Questions).
11. **Merged with 0.38.128 - 0.38.131** - Tdarr: "The daily summary shows as" only while the daily summary is ticked. Jellyfin: "New in the library shows as" only while "Something new is added" is ticked. Downloads List widget: Automatic scale, Graph full scale and Graph colour only with "Speed graph along the top" ticked (full scale only with Automatic scale off). Proxmox and Home Assistant Connection: the run box keyword row is still there, between the slider and Test connection.

## Nginx Proxy Manager: in the release, host list redraws and dots (0.38.132)
0. Settings > Plugins: Nginx Proxy Manager is listed (Servers & network) and Add downloads it from the 0.38.132 release. Added from a file before (1.0.0)? Remove it and add it again from the list - the 1.0.0 file counts as newer, so it isn't offered as an update.
1. Open the Nginx Proxy Manager popup with enough hosts to scroll, scroll halfway down and wait a couple of minutes (it checks every 30 s): it stays where you put it, no flicker.
2. With the popup open, disable a host in NPM's own web page (or from the run box): within ~30 s the popup redraws with that host dim and "off", and keeps your scroll place.
3. Dots: an enabled host whose backend answers = filled green; one forwarded to a Docker container name or localhost = a green ring (hover: "Backend not checked - this PC can't reach …"); a disabled host = dim filled dot; a problem = red.
4. Settings > Notifications > Nginx Proxy Manager, untick "Check each host's backend from this PC": every enabled host without a problem shows a ring, and hover says "Backend checks are off".
5. Stop a backend (e.g. a container NPM forwards to by IP): after two checks the row turns red ("backend down"), hover says it doesn't answer; start it again: back to filled green.

## Proxmox: CPU / MEM labels over the guest list (0.38.132)
1. Proxmox module on a bar, click it: under GUESTS, small dim "CPU" and "MEM" sit at the top of the list, right-aligned over the CPU % and memory columns. With several nodes they're above the first node's name. They scroll with the list.
2. Proxmox widget, Show = Guests, "Show each guest's CPU and memory" on: the same labels at the top. Turn that setting off: the labels go too.
3. Uptime column on ("Show each running guest's uptime"): the labels still line up with CPU and MEM; nothing over the uptime column.

## Linux Update Dashboard plugin (0.38.132)
1. **Add it** - Settings > Plugins: Linux Update Dashboard is listed (Servers & network). Add it, fill in the address (http://host:3001) and an API token from the dashboard's Settings > API tokens. Test connection: version, number of systems and updates, and "a read-only token …" or "a read/write token". A wrong token: "didn't accept the API token". A wrong port (a web page, not the API): says so.
2. **Module** - add "Linux Update Dashboard" to a bar: e.g. `23 updates · 3 security · 1 reboot` - yellow with updates, red with security updates or a system that can't be reached, normal "Up to date" when there's nothing. Hover: one line per system that needs something, and when it was asked. "Hide while everything's up to date" hides it then.
3. **Popup** - click it: the version, a summary line, one row per system (dot, name and OS, then updates / kept back / reboot needed / checked … ago; red for a problem). The list shows = Only ones that need something: the up-to-date ones go (a supported distro doesn't count as needing something - only one near or past its end of support, and not once that warning is dismissed on the dashboard). Rows don't say "supported". Leave it open: it doesn't flicker or jump; a check on the dashboard changes its row within ~20 s.
4. **Read-only token** - with one: the popup says "Read-only API token…", a system's menu has only Show the output / Open shell / Open dashboard / Copy host name, no Check all. Palette: no Check / Upgrade / Reboot commands.
5. **Check now** (read/write token, upgrades still off) - click a system > Check now: its row turns accent "checking…", then shows the new count. Upgrade / Reboot show as one greyed "Upgrades and reboots are off (Settings > Plugins)" item. Check all: every row checks.
6. **Upgrades on** - Settings > Plugins > tick "Allow upgrades…". A system with updates > Upgrade N packages…: asks; Yes opens the live output (with "Open the live output…" ticked) and the row says "upgrading…". When done: a "… is upgraded" notification (plus "It needs a reboot" when it does); the count drops. Untick "An upgrade started from vShell finishes" in Notifications: no notification for one that works, still one for one that fails.
7. **Output window** - from a system's menu > Show the output: the last command's output (the command in the accent colour), then live lines while it runs. Keep to the bottom on: follows; off: stays where you scroll. Copy, Clear work; Check now / Upgrade / Cancel follow what's running. Stop the dashboard: "Not connected … trying again" in red, and it reconnects by itself when it's back. Open it again from the palette: the same window comes to the front.
8. **Cancel and reboot** - start an upgrade, then Cancel in the menu or the output window: the dashboard stops it ("… was cancelled"). Reboot… on a system: asks (says if the dashboard doesn't think it needs one); "… is rebooting"; it can't be reached for a while, then is back.
9. **Upgrade all** - popup > Upgrade all (N)…: asks with the list of systems; Yes queues them; rows go "waiting to upgrade" / "upgrading…" in the dashboard's order. Systems left out of Upgrade all on the dashboard aren't in the list.
10. **Notifications** - the first time it connects nothing is said for what's already there. Then: a new update on a system says "N updates for …" (a security one says "N security updates for …" as an alert), once; a new reboot needed, once; a system that goes offline (after the delay) "… can't be reached", and "… can be checked again" when it's back. Restart vShell: nothing is said again. Click a system's notification: its live output opens.
11. **Palette and run box** - `!lu web`: the commands for systems with "web" in the name (Check, Upgrade, Reboot need Enter twice where they ask). `!lu` alone lists all. Open shell on …: an SSH tab (a saved connection by the system's name, else its host and user from the dashboard).
12. **Remove** - Settings > Plugins > Remove: open output windows close, the module says to add it in Settings.

## Questions and windows on the right monitor (0.38.133)
Needs two or more monitors. Put a bar and a widget on the monitor that is NOT the main one.
1. **From a bar's popup** - Linux Update Dashboard (or Proxmox) on that bar: click a system > Upgrade… (or a guest > Shut down): the question opens on that monitor, over the popup, and Enter / Esc answer it.
2. **From a widget** - the same module in a widget there: the question opens on the widget's monitor, in front of every window, and takes the keyboard straight away (type Y / N or Enter). The widget doesn't freeze. With the mouse moved to the main monitor first (keyboard / palette): it still opens where the widget is.
3. **From the palette** - open the run box on the second monitor, run a command that asks or fails (a 3D printer action with the printer off): the message is on the monitor the pointer is on.
4. **Windows** - open Settings from a bar on the second monitor: it opens in the middle of that monitor. Komodo's log window, Linux Update Dashboard's output window and the notification history open on the pointer's monitor too. A file picker from Settings (Import theme…) opens over the Settings window.
5. **Nothing else moves** - bars, popups, menus, the terminal and widgets stay exactly where they were; a dialog already on the right monitor isn't moved. The log has "Dialogs: moved a … window" for the first few it moved.
6. **SSH questions** - connect to a new server in the terminal (one whose key isn't known yet) with the pointer on the second monitor: "trust this server?" opens there, on top.
7. **Mixed scaling** - monitors with different scale (100 % / 150 %): a question moved to the other monitor is the right size there and fully on screen.

## Linux Update Dashboard: the popup keeps up while it's open (0.38.134)
1. **Check now** - open the popup, click a system > Check now and leave the popup open: the row goes to "starting…" / "checking…" (blue dot) within a second or two, then back to its updates with "checked just now" - no closing and reopening.
2. **Check all** - click Check all with the popup open: the rows change as the dashboard checks them (vShell asks every 5 seconds for two minutes after it).
3. **Upgrade** - Upgrade a system, then open the popup while it runs: "upgrading <package>…" follows the package being installed, and the row shows the new count when it's done.
4. **Menu open** - open a system's menu and wait: the popup isn't redrawn under the menu; once it's closed (click away or pick something) the popup catches up within a second.

## Taskbar: bigger icons that fit the bar (0.38.134)
With "Set the icon size myself" off (taskbar module, icons only):
1. **Slim bar** - a 40 high bar: pinned and running apps' icons are 24 (were 19); a 36 high bar: 22 (were 17).
2. **Tall bar** - a 60 high bar: 36 (were 29); taller bars stay at 36.
3. **Very slim bar** - a 28-30 high bar: icons are 16-18 and not cut off at the top / bottom of their buttons.
4. **Unchanged** - with "Set the icon size myself" on, the slider size is used as before; buttons with text keep 16 icons.

## .NET 10 and plugin API 2 (0.38.135, branch-4)
Build with build.cmd (needs the .NET 10 SDK - 10.0.402 is installed). Every plugin is built again (the API changed).
1. **Build** - build.cmd says "Plugin API: now version 2 (was 1) - plugins need a rebuild for it"; commit the updated docs\plugin-api.txt (first line "# vShell plugin API 2 (vShell 0.38.135)"). dist\sdk has the new vShell.dll, plugins.md (with "Moving a plugin from .NET 8 (API 1) to .NET 10 (API 2)") and plugin-api.txt. plugins.txt says "plugin API 2" for V's plugins.
2. **Runs the same** - start dist\vShell.exe: bars, widgets, taskbar, tray, Start menu, Now playing (media session), terminal (WebView2, an SSH connection and its file panel), colour picker, settings window all work as before. The log has no new errors.
3. **Plugins** - every added plugin loads (log: "Plugins: loaded … 0.38.135") and works; Settings > Plugins shows them up to date.
4. **API 1 plugins** - Add from a file… with a plugin built for 0.38.134 or older (e.g. third-party\vShell.UptimeKuma.dll, before it's rebuilt): "it's for plugin API 1 (vShell 0.38.1xx), this vShell has API 2". The Add from a file row's (i) says ".NET 10, vShell 0.38.135 or later".
5. **Third-party DLLs** - ClaudeUsage, PeaNUT and UptimeKuma in third-party\ are API 1 builds: they need .NET 10 builds from their authors before they go in the release's plugins.json.
6. **Older vShells** - a vShell 0.38.134 (API 1) checking for plugin updates once the 0.38.135 release is up: it doesn't offer the API 2 builds (plugins.json says api 2) and keeps its plugins. Its updater does offer vShell 0.38.135, and after updating the plugins come from the 0.38.135 release and load.
7. **Performance** - start-up time and idle CPU / memory are the same or a little lower than 0.38.134.
8. **plugins-extra** - BinaryLane and Files theme are 2.0.0 (.NET 10, API 2), built by hand as before (not by build.cmd, not in plugins.json). In vShell 0.38.135 the 1.0.0 ones added from a file say they're for plugin API 1; Remove them, Add from a file… the 2.0.0 DLLs (`plugins-extra\<Name>\bin\Release\net10.0-windows\`), and each works as in its README's Testing list.

## Widget edge glow when lining up (0.38.136)
Have a few widgets on one monitor, with "Snap widgets while dragging" on (Settings > Widgets).
1. **Edges line up** - drag a widget so its left edge lines up with another's: a soft accent glow appears just inside the left edge of both widgets (brightest at the edge, fading inward, its ends fading before the rounded corners). It fades in while sliding out onto the edge (about a quarter of a second). Same for right, top and bottom edges.
2. **No centre snapping** - drag a widget so its centre passes another widget's centre, or the screen's centre: it doesn't snap there and nothing glows (edges, the snap gap and the edge gap still snap).
3. **Snap gap** - drop a widget beside / below another at the snap gap: the two facing edges glow (e.g. its left and the other's right).
4. **Screen and bars** - snap to the edge gap from the screen edge or a bar: only the dragged widget's edge glows.
5. **Following the drag** - snapped on one axis, keep dragging along it: the glow follows without fading in again; snap to a different widget or edge and it fades in on the new one. Move out of reach: it fades out quickly.
6. **Drop / Alt** - drop the widget: the glows fade out. Hold Alt while dragging: no glows (placed freely).
7. **Resizing** - resize a widget by an edge or corner until it lines up: the edge being dragged and the other widget's edge glow (both edges on a corner).
8. **Slid away** - a widget set to slide away at its screen edge, slid away: drag another widget past where it sits - it doesn't snap to it and nothing glows for it. Bring it out (mouse to the edge): lining up with it glows again.
9. **Group drag** - Ctrl+click several widgets and drag them: the group's outer edges / each widget's edges glow against the widgets that aren't moving.
10. **Look** - glows use the Main Accent colour (a new accent shows on the next drag), let clicks through, never take focus and don't show in Alt+Tab; check on a 150% / 200% monitor too (band about 10 px at 100%).

## Top processes by GPU (0.38.137)
1. **Sort by Gpu** - a Top processes module (bar or widget), Settings: Sort by = Gpu (the note under it explains it). It shows the programs using the GPU most with their GPU %, e.g. "dwm 2.1%". Start a game or a video / a GPU benchmark: it moves to the top within a few seconds and its % roughly matches Task Manager's GPU column (Processes tab) - a program started after vShell can take up to ~30 s to show up.
2. **Tooltip** - hover: "Top programs by GPU", each line has CPU %, memory and "GPU n%". With Sort by CPU / Memory the tooltip has no GPU part (as before).
3. **Only when needed** - with no GPU module anywhere, a Top processes module on CPU / Memory: the GPU counters aren't read (vShell's CPU use in Task Manager as before). Switch it to Gpu: they start; back to CPU: they stop (a GPU module elsewhere keeps them running).
4. **With a GPU module** - a GPU module and a Top processes on Gpu at the same time: both update as usual.
5. **Old settings** - a settings.json with ProcSort "Cpu" or "Memory" loads unchanged; a made-up value falls back to Cpu.

## Top processes GPU note (0.38.138)
1. **Note follows Sort by** - a Top processes module's settings with Sort by = CPU or Memory: no GPU note. Pick Gpu: the note "Shows each program's busiest GPU engine, like Task Manager's GPU column." appears under Sort by; back to CPU / Memory: it goes. Switching between CPU and Memory doesn't redraw the page.
2. **Two GPUs** - on a PC with integrated + dedicated graphics (a laptop, or a desktop with the iGPU on), a program using both (a browser, a game): its GPU % is its busiest engine on either GPU - the same as Task Manager's GPU column, never the two added up.
3. **Switching to Gpu** - set Sort by to Gpu: the module shows "Top …" (tooltip "Measuring…") for a second or two, then the GPU list - never the CPU order with "0.0%" on every program.
4. **No GPU counters** - (hard to get on Windows 11; a VM without a GPU, or check the code path) where the GPU Engine counters can't be read: the module shows "GPU n/a", tooltip "GPU use per program can't be read on this PC.", like the GPU module's "GPU n/a".
5. **GPU module keeps its value** - a GPU module and a Top processes on Gpu: switch the Top processes to CPU and back a few times - the GPU module keeps showing its reading throughout (no "n/a" flash).

## Graph look (0.38.139)
Applies to CPU, Memory, GPU, HWiNFO sensor, Network, Disk, Battery and the Downloads plugin's module (bar and widget) - all in the module's Graph section. Home Assistant's entity graphs and the Downloads list's graph get them too (see 12-14).
1. **Defaults look the same** - existing graph modules after updating: line, bars, meter, ring, tile, cores look as before (line ends are now rounded - barely visible).
2. **Line thickness** - Style Line: Line thickness Thin / Normal / Thick / Heavy changes the line (1 / 1.5 / 2.5 / 3.5 px). Also on a Tile (its sparkline).
3. **Fill under the line** - None (just the line), Light (as before), Solid (stronger), Fade (strong just under the highest point, fading to nothing at the bottom). Tile too.
4. **Smooth curves** - on: the line is a soft curve through the readings, never going above the top or below the bottom of the box (try CPU spikes).
5. **Background box** - Line / Bars: untick it - no box, just the line / bars; the corner rounding slider hides. Tick it: Box corner rounding 0 = square, 12 = very round.
6. **Grid lines** - Line / Bars: faint lines at 25 / 50 / 75 %; with the warning on, a dashed line in the warning colour at the threshold (not with Colour by load). Not on a Tile.
7. **Time shown** - Line: 5 minutes - the line now covers 5 minutes (about 75 points, a few seconds each); 15 minutes the same (90 points). It starts empty and fills from the right. Bars at 1 minute: one bar a second as before; at 5 / 15 minutes the bars cover the whole time. Network on Automatic scale: the scale follows the highest speed in the time shown.
8. **Colour by load** - tick it: three colour rows appear (Graph colour hides; Network / Downloads keep their download / upload colours). Defaults: low = Main Accent faint, middle = Main Accent stronger, high = Secondary Accent. Meter: blends left to right; Bars / Line: bottom to top; Ring: around the ring; Cores: each core's colour by its load; Tile: its sparkline / meter. Pick your own colours, Reset brings the defaults back. Untick: back to graph / warning colours.
9. **Warning with colour by load** - the graph ignores the warning colour, but the text still turns the warning colour past the threshold.
10. **Settings follow the style** - switching Style shows only the rows for it (Meter / Ring / Cores: only Colour by load; Text: none).
11. **Widgets and bars** - all of the above in a bar and in a widget, at 100 % and 150 % scaling.
12. **Home Assistant** - an Entities module (not Buttons): its settings have a "Graphs" section with all the rows (line, fill, curves, box, grid, time shown, colour by load), whatever the entities' own graph styles. Entities with a Line / Bars / Meter graph follow them; colour by load wins over an entity's own graph colour. A new graph still starts with the last 2 minutes from Home Assistant's history.
13. **Downloads list** - Downloads in a widget, "In the widget" = List, "Speed graph along the top" on: under Graph colour come the same rows; the graph along the top follows them (time shown: 5 / 15 minutes, colour by load …). With Automatic scale the scale follows the last minute (a burst older than that may sit at the top).
14. **Plugin API** - build.cmd says "Plugin API: 2 new"; docs\plugin-api.txt gains `MetricView :: void ApplyLook(ModuleConfig, Theme)` and `SettingsWindow+SettingsPage :: void GraphLook(ModuleConfig)` (still API 2). The Home Assistant and Downloads plugins are rebuilt (0.38.139); an older vShell (0.38.138) doesn't offer these builds (they use ApplyLook - MissingIn), and keeps its 0.38.135 ones.

## Downloads list scale and time shown (0.38.140)
1. **Scale follows the time shown** - Downloads in a widget, List, speed graph on, Automatic scale on, Time shown = 5 minutes. Make a burst (start a fast download, then pause it): for the next 5 minutes the line keeps the burst inside the graph and the scale label (top right, "n MB/s") stays at the burst's level; after 5 minutes it drops back. At 1 minute it behaves as before.
2. **Plugin API** - build.cmd says "Plugin API: 1 new"; docs\plugin-api.txt gains `ModuleConfig :: int GraphSpan { get; set; }` (still API 2). Only the Downloads plugin is rebuilt (0.38.140); Home Assistant stays in the 0.38.139 release.

## CPU / Memory / GPU: Style in the Graph section (0.38.141)
1. **Where it is** - a CPU, Memory or GPU module's settings: the top part no longer has Style / Show text; the CPU / memory / GPU options come first, then Temperature, then the Graph section starting with Style (and its note for Ring / Tile), Show text (Show the label beside the ring for Ring), then the size, look and colour rows - like Network, Disk, Battery and HWiNFO.
2. **Cores** - CPU, Style = Cores: the Cores note is right under Style in the Graph section.
3. **Switching style** - changing Style redraws the page with the rows for the new style, still in the Graph section.

## No Colour by load on Network / Downloads (0.38.141)
1. **Settings** - Network, Downloads on a bar, Downloads in a widget (Graph, and List with the speed graph on): the Graph rows have no "Colour by load" and no low / middle / high colours. Download / upload colours (and the list's Graph colour) are as before.
2. **Turned on before** - a Network / Downloads module that had Colour by load on in 0.38.139 / 0.38.140: its graph is back in the download / upload colours (the old setting is ignored).
3. **Others keep it** - CPU, Memory, GPU, HWiNFO, Disk, Battery and Home Assistant's Graphs section still have Colour by load.

## Tiles fill the widget's module height (0.38.142)
A widget with CPU, Memory and GPU modules set to Style = Tile (also try Disk, Network, Battery, HWiNFO as Tiles).
1. **Taller module height** - Settings > Widgets > the widget > Module height 120: each tile is 120 tall; the label stays at the top, the big number under it, and the graph fills from under the label right down to the bottom edge of the tile (with cards on: to the bottom of the card's inside).
2. **Default / small module height** - Module height 40 (or 20): the tile keeps its own height (label, number and a graph about 3.6 lines of text tall) - it doesn't get squashed.
3. **Resizing by dragging** - drag the widget's top or bottom edge (not Shift = fixed height): the module height changes and the tiles grow / shrink with it while dragging, the graph staying on the bottom edge.
4. **Disk space tile** - Disk (Space) as a Tile: its meter sits along the bottom edge of the taller tile.
5. **Grid / horizontal widgets** - Arrangement Grid or Horizontal: tiles are at least the module height, and line up along their bottom edges.
6. **Bars** - a Tile on a bar is unchanged (as tall as the bar).

## New themes and tall bar styles (0.38.143)
Settings > Appearance (Themes / Bar styles). Save your current look first (Save as theme…) to get back to it.
1. **Bar styles** - the bar style gallery has Tall, Panel and Pill after Retro. Apply each: a Classic bar becomes 52 / 56 / 58 tall (Minimal 50 / 54 / 56); Floating and Dock bars the same heights. Tall: soft corners, subtle glass edge; Panel: square, crisp outline (floating: small corners); Pill: very round (floating: fully round ends).
2. **Tall bars and modules** - with a 52-58 bar: S / M / L text all fit (M two lines, L one large line), taskbar buttons, tray icons, the clock, graphs (Graph height %), the start button and the run box sit centred; popups open next to the taller bar; windows don't go under it (work area).
3. **New themes** - the theme gallery has 16 more after AMOLED Hairline: Outrun Pill, Sunset Tall, Amber CRT Retro, Latte Round, Paper Hairline, Sakura Pill, Arctic Ghost, Warm Sunset Soft, Windows Dark Panel, GitHub Dark Slim, Monokai Sharp, Forest Tall, Monochrome Ghost, Deep Purple Bold, Carbon Teal Panel, Cosmic Blue Neon. Each card's picture shows its colours, bar and widget style; its tooltip names them.
4. **Applying** - click each: colours, bar style and widget style change together; the six tall ones (Outrun Pill, Sakura Pill, Sunset Tall, Forest Tall, Windows Dark Panel, Carbon Teal Panel) make the bars taller. The card shows as the one in use afterwards.
5. **Light themes** - Latte Round, Paper Hairline, Warm Sunset Soft, Sakura Pill, Arctic Ghost: text is readable on the bar, in popups, widgets and the settings window.

## Full-width Floating bars with Three zones (0.38.144)
Settings > Bars > a bar > Layout tab.
1. **Classic to Floating** - a Classic bar (Three zones on): Layout = Floating. It stays full width, moves 8 px away from the screen edge (left, right and the edge it's on) and takes the Floating look (all four corners rounded with the usual bar styles). The Layout tab shows "Full width" ticked and "Three zones" under it; the zones work as on Classic (centre in the middle, flexible spacers fill their own zone, the Zone setting on each module, the module editor's columns).
2. **Full width off** - untick Full width: the bar shrinks to its modules (as Floating was before) and "Three zones" disappears from the Layout tab; the modules' Zone setting and the editor's zone columns go too. Tick it again: full width and zones come back as they were (zones are remembered).
3. **Dock to Floating** - a Dock bar: Layout = Floating - it stays as wide as its modules (Full width unticked), with the edge gap. No Three zones.
4. **Floating to others** - a full-width Floating bar: Layout = Classic / Minimal - full width, against the edge (gap 0); Layout = Dock - as wide as its modules. Dock never shows Full width or Three zones.
5. **Gap from the screen edge** - on a full-width Floating bar: the slider changes the gap at both ends and from the edge; windows stop at the bar (Reserve window space) with the gap included.
6. **Themes and bar styles** - apply a theme / bar style while a bar is Floating full width: it keeps full width (and its zones) and gets the style's Floating look; an auto-width Floating bar stays auto-width.
7. **Existing bars** - after updating, Classic, Minimal and Dock bars and auto-width Floating bars look and behave as before (nothing switches layout by itself).

## Claude usage 2.0.1 and PeaNUT 2.0.1 (0.38.144 - the plugins moved into plugins\)
Build with build.cmd: dist\vShell.ClaudeUsage.dll, vShell.PeaNUT.dll and vShell.UptimeKuma.dll are built from plugins\ now (third-party\ is empty).
1. **Claude usage: a reset notification saves** - with Settings > Notifications > Claude usage > "A limit I reached has reset" on and a limit at 100% (or temporarily lower "Close means" so a "getting close" notice fires), wait for the window to end: the notice comes, and %APPDATA%\vShell\settings.json is written within a second (its date changes) - then change any setting: it still saves (before, a save from this check could leave settings unsaved until a restart).
2. **Claude usage: a custom folder at start** - set a Claude Code folder (e.g. a WSL one) on the Plugins page, restart vShell: the module shows the numbers straight away, and vshell.log has no "isn't signed in on this PC (no C:\Users\...\.claude...)" line from the start.
3. **Claude usage: still updates** - the tiles still change when a new answer comes (Refresh on the card, or wait 3 minutes), the "checked 2m ago" tooltip text moves on by the minute, a reached limit goes to 0% when its window ends, and the ring / bar meters repaint; with a Claude usage module on a bar, vShell's CPU use at idle is no higher than without it.
4. **PeaNUT: no UPS** - turn every PeaNUT server off (or point one at a wrong address): the module shows its message ("UPS: every PeaNUT server is turned off" / "UPS: can't reach ...") and the message still changes when a server is turned back on or answers again.

## Less background work, settings timers from plugin threads (0.38.145)
1. **Auto-width bars stay put** - a Floating (Full width off) or Dock bar: leave it for a minute - it doesn't move or flicker. Then make it wider: open more windows with a taskbar on it, or let a clock / CPU text grow - the bar grows within about a second as before. Drag the bar to another spot or edge and let go: it lands where it was dropped. Change the bar style / height / corners / outline in Settings: it follows.
2. **Other app bars** - with a Floating bar on screen, show and hide the Windows taskbar (Settings > System > Hide the Windows taskbar), or open another program's app bar: the vShell bar moves off / back onto the strip as before.
3. **Monitors** - unplug / plug a monitor, or change its scaling: bars and widgets go to the right place (the monitor list is cached for one second and cleared on the change).
4. **Widgets on the desktop layer** - widgets sit behind your windows as before; click the desktop, press Win+D twice, change the wallpaper: they are still there (put back only when they left the layer). "Keep above other windows" widgets stay on top.
5. **Tray popups** - with the tray on a Floating bar, an app's tray-icon popup (volume, OneDrive) still opens next to the bar.
6. **Settings from a plugin thread** - (needs a plugin that calls SaveSoon / SettingsChanged from its poller: Claude usage 2.0.0 did) - settings keep saving after it; with 2.0.1 nothing to see.
7. **Applying settings while notifications check** - drag a slider in Settings for a few seconds with Service status checks and a Weather module set up: no "is down" / "is back up" surprises, the log has no "Notifications (...)" errors, and the bars don't all rebuild on a bar-only change (e.g. one bar's height) - only that bar does.

## Welcome guide (0.38.146)
A first start (rename %APPDATA%\vShell\settings.json first, put it back afterwards) - or Settings > About > Welcome guide > Show it again.
1. **It opens** - about two seconds after the bars appear (after the splash), a "Welcome to vShell" window, coloured like Settings, step 1 of 4 with four dots at the top right. Close it with the X or Esc, start vShell again: it does not come back (settings.json has "WelcomePending": false). It does come back from Settings > About.
2. **Welcome** - the three switches work straight away: Start vShell when I sign in (Task Manager > Startup shows vShell on / off), Hide the Windows taskbar (Explorer's taskbar comes and goes), Show vShell in the system tray.
3. **Theme** - the built-in themes as cards with their pictures, the one in use ticked. Click one: the bars, the widgets and the guide window itself change colour at once; the card gets the tick. Settings > Appearance shows the same theme in use afterwards.
4. **Layout** - Taskbar only: the top CPU / memory / GPU bar goes; Taskbar and system graphs: it comes back; ... and a clock widget: a Clock widget appears near the top-left of the main screen (and goes again with the other two). Back / Next keep the choice.
5. **Every monitor** (two or more monitors) - tick Put a taskbar on every monitor: each other monitor gets a bar in the same look with Start, that monitor's windows and a clock (no bell, no tray). Untick: those bars go, the main one stays. With one monitor the card just says so.
6. **Synced pins** - only enabled with every-monitor on. Tick it: Same pins on every taskbar is on for every taskbar module (Settings > Bars > a bar > Taskbar > Pinned apps shows "Synced"); pin an app on one bar and it shows on the others.
7. **Import pins** - tick Import the apps pinned to the Windows taskbar: Windows' pins appear on every vShell taskbar in Windows' order (Store apps too). Untick: the imported ones go, pins you added yourself stay. With nothing pinned in Windows a message says so.
8. **Plugins** - the catalog by group with a switch each; plugins already added show "added" and can't be unticked. Tick two, Finish: the button says "Adding…", the plugins download and load (modules appear in the gallery), the guide closes and Settings opens on Plugins. Finish with none ticked just closes.
9. **Skip** - on steps 1-3 "Skip the guide" closes it, keeping what was chosen so far (theme, layout, pins). It isn't shown on the last step.
10. **Upgrade** - an existing settings.json: no guide at start (WelcomePending stays false).

## System page order (0.38.147)
1. **Backups below Behaviour** - Settings > System: Updates, then Behaviour (run at login, hide the Windows taskbar, tray balloons, tray icon, splash), then Backups at the bottom. Everything in Backups still works (back up now, restore, the list).

## Welcome guide: system widget (0.38.148)
Settings > About > Welcome guide > Show it again (or a first start), step 3 Layout.
1. **The widget** - pick "Taskbar, system graphs and a desktop widget": a widget named System appears against the right edge of the main screen (the edge gap from Settings > Widgets), near the top: a big clock with the date in the secondary accent, then CPU and Memory as tiles (name, big number, thick fading sparkline coloured by load), then the Windows drive as a ring with the used / total GB - each module in its own card, one under the other. Settings > Widgets lists it with those four modules; the warning on CPU / memory / disk is on at 70 %.
2. **Theme** - go Back to Theme and click another: the widget's corners, border, padding and blur follow the theme (it has "Follow the widget style" on).
3. **Away again** - pick Taskbar only or Taskbar and system graphs: the widget goes; pick the widget choice again: a fresh one appears in the same place. Widgets you made yourself are left alone (the guide only ever removes the one it made, or the first widget when it made none).
4. **Snap off** - with Settings > Widgets > snapping off, the widget appears near the top-left instead (no edge link) and can be dragged anywhere.

## Mail plugin (0.38.149)
Settings > Plugins > Mail (add it from the list - a fresh build has it next to vShell.exe in dist\).
1. **An account** - + Add account: server (imap.gmail.com / imap.fastmail.com / your own), SSL/TLS on 993, the user name and the password (an app password for Gmail, Yahoo, iCloud), Test connection: "Connected - INBOX has N messages, M unread". A wrong password says the server didn't accept it (and mentions app passwords); a wrong server says it can't be reached; a self-signed certificate says to tick Accept a self-signed certificate. Switching Connection to STARTTLS swaps the port to 143 (and back to 993), unless you typed another port.
2. **Pick a folder…** - lists the server's folders (INBOX first, Gmail's [Gmail]/… ones decoded, non-ASCII names readable); pick one and the Folder box follows. Typing a folder name works too.
3. **Mail module** on a bar: an envelope with the unread count in the main accent (just the envelope when nothing's unread, "!" in the warning colour when an account can't be checked, "…" while the first check runs). Hover: "Mail: Work 2 · Home 1". Click: a popup with each account (name, "2 unread"), then the newest unread messages - sender in bold, the subject, when it came (the time today, else Yesterday / Mon / 12 Mar) - and "and N more" past the number set. A click on a message or an account opens the webmail address from the Plugins page (nothing with it empty). "Hide it while there's nothing unread" hides it.
4. **In a widget** - the Mail module shows the same envelope (centred) and popup; Mail messages shows the list in the widget and sizes itself; both follow the widget's font size.
5. **Module settings** - Account: Every account / one of them (the count and list follow); Messages listed per account; Who it's from / When it came off and on - the popup and the list rebuild only when something changed (watch it with the popup open: it doesn't flicker).
6. **Updates** - send yourself a message: within Check every (default 60 s; 15 s minimum) the count goes up and the message is in the list; read it in your mail app: it goes again. Mark one unread there: it comes back.
7. **Notifications** - Settings > Notifications > Mail: New messages on. Send yourself two messages: one notification "2 new messages in Work" with "Sender - Subject" on two lines (the source is Mail, the kind Normal - change it to Important). Send more than "Messages listed in one notification": the extra ones are "and N more". Messages that were in the folder when the account was added don't notify (the first check only learns where the folder is up to - SeenNext in settings.json). Restart vShell: no repeat of old ones. A message read before vShell checks isn't listed.
8. **Can't be checked** - turn on "An account can't be checked", set the wait to 0, make the password wrong: "Can't check Work" (Alert) with the reason; put it right: "Work can be checked again". Off: nothing is said (the module still shows "!").
9. **Two accounts** - add a second: the bar count is both together; the popup lists them in the Plugins page's order; Remove one (it asks): its messages go. Untick Use this account: it's left out everywhere.
10. **Nothing to do** - with notifications off and no Mail module anywhere, the log has no "Mail" checks (none run); add a module: they start.

## Mail: scrolling list, Mark as read / Delete (0.38.150)
1. **Scrolling** - with more unread messages than "Messages shown at once" (module settings, default 8): the popup is as tall as that many rows (the account headers among them included) and scrolls for the rest, with the theme's scrollbar; the wheel scrolls it. With fewer, no scrollbar and no empty space. Past 100 unread in one account, "and N more (the newest 100 are listed)" at the end.
2. **Scroll position kept** - scroll down, wait for a check (or mark one read): the list rebuilds without jumping back to the top. Close and open the popup: it starts at the top.
3. **Mark as read** - the tick on a row: the row fades, within a moment it's gone and the count is one less; in your mail app the message is read. The next check agrees (no flicker back).
4. **Delete** - the bin on a row: on Gmail, Fastmail, iCloud (servers with a Trash folder and MOVE) the message is in Trash afterwards; on a plain Dovecot / other without one it's flagged deleted and expunged (gone from the folder). A folder that is itself the Trash: expunged.
5. **Errors** - with a wrong password (change it in Settings, then press a button): the row comes back and a message box says what went wrong ("Couldn't mark as read "…" in Work."). The buttons don't start a widget drag or open the webmail.
6. **Widget list** - Mail messages: the same scrolling (the widget stays the height of the rows shown), the same buttons.

## Mail: no flash, visible scrollbar (0.38.151)
1. **No flash** - with more unread than "Messages shown at once": open the popup - it comes up at its final height straight away; delete or mark one read - the row goes and the list stays the same height (no tall list for a moment first). The widget list the same.
2. **Scrollbar** - whenever there's more to scroll, a thin scrollbar in the dim text colour shows at the right of the list (in the popup and in a widget), the rows stopping a little short of it; drag its thumb or use the wheel. With everything fitting, no scrollbar and no gap.

## Mail: quicker Mark as read / Delete (0.38.152)
1. **At once** - press the tick or the bin: the row is gone and the envelope's count is one less the same instant (no fade, no wait for the server). The widget list and a second Mail module elsewhere follow at once too.
2. **Several in a row** - mark five read one after another: each goes at once, and after the first the server round trip is short (the connection is kept; vshell.log has no new login per click). A minute after the last one the connection is closed (a mail server's own log shows a logout; nothing to see in vShell).
3. **The server says no** - make the folder a wrong name in Settings (or the password wrong after the first action), press a button: the row comes back and a message box says why. Put it right: works again without a restart.
4. **A dropped connection** - after an action, wait past the server's idle limit (or take the network down and up): the next button press still works (a new connection is made) - at most one message box in the unlucky case the server drops it mid-way.
5. **Checks still right** - after the actions, the next check (Check every) agrees with what's shown: nothing comes back.

## Mail module: the account's name (0.38.153)
1. **The toggle** - a Mail module's settings: with Account = Every account there's no name toggle; pick one account and "Show the account's name next to the count" appears under it (and goes again with Every account).
2. **On the bar / widget** - tick it: the module reads "Work 3" (the name, then the count in the accent); with nothing unread just "Work"; while an account can't be checked "Work !". Untick: back to the count alone. The popup is unchanged.

## Mail: Sign in with Google (0.38.153)
1. **Sign in** - + Add account, Sign in with = Google: the server (imap.gmail.com, 993, SSL/TLS) and the webmail address fill in, the password row goes and "Sign in with Google" appears. Press it: the browser opens on Google's account chooser; pick the Gmail account; Google says the app isn't verified - Advanced > Go to vShell; allow "Read, compose, send and permanently delete all your email"; the tab says "Signed in - you can close this tab". Back in Settings the button reads "Signed in as you@gmail.com", the Address and Name are filled in, a Sign out button is there.
2. **It works** - Test connection: "Connected - INBOX has …"; a Mail module shows the count; Pick a folder… lists the [Gmail] folders; Mark as read / Delete work; notifications come. Nothing asks for a password. Restart vShell: still signed in (the refresh token is in settings.json, encrypted).
3. **Cancelled** - Sign in with Google, then close the tab or press Cancel on Google's page: a message says the sign-in was cancelled (or that nothing came back within five minutes); the account is unchanged. A stray request on that port (a second tab) is ignored.
4. **Sign out** - the Sign out button: "Not signed in yet", the module says it can't check ("Not signed in with Google yet…"). Sign in again: back to normal. Taking vShell away under the Google account's third-party access: the next check says the sign-in has expired - sign in again.
5. **Own client** - Google sign-in section at the bottom of Mail: a client ID and secret of your own; Sign in again: Google's consent screen shows your project's name. Wrong ones: "Google doesn't know this OAuth client".
6. **Password still works** - Sign in with = Password on another account: as before (the app password note now mentions Google too).

## Plugins page: "by version" on every plugin (0.38.154)
1. **Added plugins** - Settings > Plugins: each added plugin's card (Jellyfin, Nginx Proxy Manager, Claude usage, …) says "by version" in its line of details - also while the plugin's own DLL is still an older build that says "V" inside (0.38.148 and earlier builds); after the plugins update it still says "by version".
2. **Get more** - the list of plugins to add: "by version" next to each of vShell's own; a plugin added from a file by someone else shows their name as before.

## Launcher and taskbar pins: several shortcuts at once (0.38.155)
1. **Launcher** - a Launcher module's settings > Edit shortcuts and groups… > Add shortcuts…: in the file dialog Ctrl-click (or Shift-click) several .lnk files in a Start Menu folder (e.g. %ProgramData%\Microsoft\Windows\Start Menu\Programs) and Open: every one appears, in that order, inside the selected group (or after the selected shortcut, or at the end). Each has the shortcut's own name and icon, and launches with the shortcut's arguments; the path shown is the .lnk.
2. **Still one** - pick a single file: as before. A shortcut to a Store app or a folder: added and launches.
3. **Taskbar pins** - a Taskbar module's settings > Add programs or shortcuts…: Ctrl-click several .exe / .lnk files and Open: all pinned, in order; one file: as before.

## Launcher in a widget: columns or rows (0.38.156)
1. **Arrange** - a Launcher module in a widget > its settings: Arrange = One row / Columns / Rows (a bar's Launcher has no such row). Columns + 4: the shortcuts sit in a grid four across, as many rows as needed, every cell the same size, icons alone centred in their cell, named ones lined up on the left; the widget grows to fit (the module sizes itself, at the top of its row). Rows + 3: three down, as many columns as needed. One row: as before, side by side in the module height.
2. **Still works** - click a shortcut: it launches; a group: its menu opens next to it. Drag a shortcut to another cell: the mark shows at the cell it'd land in (on that row, not always the first), drop: it moves there. Drop files from Explorer onto the grid: they go where dropped.
3. **Live** - change the count slider: the grid follows at once; switch back to One row: back to the row height.

## Launcher in a widget: icon animations (0.38.157)
1. **Settings** - a Launcher module in a widget, names off (icons only): its settings now have the Icon animations section like a bar's Launcher - Lift, Magnify (+ size), Glow (+ strength, size), Bounce; greyed with the icons-only hint while names are on. The note says the icons grow inside the widget.
2. **Lift / Magnify** - move the mouse over the icons: Lift raises the one under the mouse a little and grows it about its middle; Magnify grows it and its neighbours less, spreading them apart along the row (in a Columns / Rows grid the same within a row). Nothing is cut off with a bit of widget padding; without padding a grown icon may touch the widget's edge (expected).
3. **Glow** - a soft accent halo behind the icon under the mouse, under the icons, following a lifted one; off: no halo.
4. **Bounce** - click a shortcut: it bounces upwards while the app starts. Groups open their menu as before.
5. **Bars unchanged** - a bar's Launcher / Taskbar animations behave as before (grow away from the bar's edge, transparent room beside the bar).
6. **Rebuilds** - change the widget's style or a setting: the animations keep working on the new icons; remove the launcher from the widget: nothing animates, no errors in the log.

## Widget Launcher: Magnify fixed (0.38.158)
1. **A grid** - a widget Launcher arranged in Columns (say 4) with icons only and Magnify on: move the mouse along one row - the icon under it grows most, its neighbours on that row less, and that row spreads a little; the other rows don't move or grow. Move down to the next row: that row takes over, the previous one settles back. Leave the widget: everything settles.
2. **One row** - Arrange = One row: the same as on a bar (grow about the middle, neighbours spread).
3. **Bars** - a bar's Launcher / Taskbar Magnify as before.

## Widget Launcher: Magnify in any direction (0.38.159)
1. **Any direction** - in a Columns or Rows grid: move the mouse diagonally between icons - each icon grows by its own distance from the mouse (the nearest most, the ones around it less in a ring), with no jump when crossing from one row or column to the next; the icons in the grown one's row move apart sideways and the ones in its column move apart up and down, so nothing overlaps. Up and down a column: one icon at a time, not the whole row. Leave: all settle.
2. **Icon size** - a widget Launcher's Icon size slider: at 48 the icons really are 48 px (compare with the Taskbar's 48), the module and the widget grow to fit them; at 16 they shrink and the row is still at least the module height. One row and a Columns / Rows grid alike. A bar's Launcher still shrinks its icons to fit the bar.

## Widget Launcher animations: nothing cut off (0.38.160)
1. **Room** - a widget Launcher with Magnify (or Lift / Bounce / Glow) on and an icon at the edge of the grid: hover it - the grown icon reaches past the widget's box and is drawn whole, over the transparent margin (not cut off at the box edge). Bounce an edge icon: whole. Glow's halo: whole.
2. **Placement** - the widget sits where it did (the box, not the margin, is what snaps and is placed): drag it against a screen edge or another widget - the box lines up as before. Turn the animations off: the margin goes back to the shadow's.
3. **Shadow / outline** - with the shadow and an outline on: both still drawn right around the box (no black fill in the margin).

## Live taskbar icons (0.38.161)
1. **vXplorer** (0.1.9 or newer) - start it with vShell's taskbar set to combine buttons: its button shows vXplorer's icon in your theme's colours (folder outline in the Main Accent, the three dots), not the exe's fixed blue one. Change the theme in vShell: the button's icon re-colours within a second or two.
2. **Pinned** - pin vXplorer, start it: while it runs the pinned button shows the themed icon; close it: the pin's own (fixed) icon again.
3. **Not combined** - turn combining off: each vXplorer window's button follows the theme too, and the icon is sharp at large icon sizes (the window's large icon is used).
4. **Other apps** - nothing changes for apps that don't set the `vShell.LiveIcon` window property: pinned and combined buttons keep the program's icon.

## Pinned live icons stay (0.38.162)
1. Pin vXplorer, start it (the pin shows the themed icon), change the theme (it re-colours), close vXplorer: the pin keeps the new themed icon - not vXplorer.exe's fixed blue one.
2. Restart vShell with vXplorer closed: the pin still shows the themed icon (kept in %APPDATA%\vShell\cache\live-icons).
3. Change the theme while vXplorer is closed: the pin keeps the icon from when it last ran, until vXplorer is started again.
4. Other pinned apps: their own icons as before.

## Windows Security tray icon (0.38.163)
1. Left-click the Windows Security (shield) icon in vShell's tray (or the tray popup): Windows Security opens on its home page. Before, nothing happened - SecurityHealthSystray ignores clicks forwarded from another tray, like Windows' volume / network icons.
2. Right-click it: a vShell menu - Open Windows Security (bold), then Virus & threat protection, Firewall & network protection, App & browser control, Device security, Protection history. Each opens that page.
3. Hover it: its tooltip still shows as before (hover is still passed to the app).
4. Other tray icons (OneDrive, Discord ...): clicks still go to their apps.

## Drives module (0.38.164)
1. Settings > Modules > add "Drives (This PC)" (in System) to a widget: "This PC", then a row per drive - its icon, name ("Local Disk (C:)"),
   how much is free, and a bar for the space used (the warning colour over 90% full). Network drives too.
2. Click a drive: it opens in vXplorer when vXplorer is installed (with its installer), otherwise File Explorer. Click "This PC": This PC.
   Hover: a tooltip with free / total / % used and what it opens in.
3. Its settings: Show network drives (off: only local ones), Show how much space is free, Open drives with (Automatic / vXplorer /
   File Explorer), Title (the heading's text).
4. On a bar: a chip per drive - "C:" and a small bar (and the free space when that's on); click to open.
5. Plug in a USB stick: it appears within 30 seconds; take it out: it goes. A network drive that's offline doesn't hold vShell up.

## Drives module: Ring style (0.38.165)
1. Drives module's settings > Style > Ring: each drive becomes a ring like the Disk module's Ring - % used inside, the drive's name
   beside it (on a bar: its letter) and "… GB free" under the name (when Show how much space is free is on).
2. A drive over 90% full: its ring, % and name turn the warning colour.
3. Ring size (% of the row height) shows only for Ring and changes the rings' size. Back to Bars: as before.
4. Clicking a ring opens the drive (vXplorer or File Explorer, as set); its tooltip as before.

## Monitor turned off or unplugged (0.38.166)
1. With bars and widgets on a second monitor, turn that monitor off (or unplug it). Within a second or so they disappear - nothing
   lands on the main monitor, and windows maximised on the main monitor don't change size (the hidden bar gives back its reserved space).
2. Settings > Bars: the bar is under "Monitor not connected" with an amber dot ("Its monitor isn't connected - hidden until it's back").
   Settings > Widgets: the same group, an amber dot before each widget's Show switch - also when only one monitor is left.
3. Open that bar: Layout tab > Position > Monitor says "Not connected · hidden until it's back". Changing its modules or look works.
   Pick Monitor 1: the bar appears there at once, leaves the "Monitor not connected" group, and stays there when the other monitor
   comes back. Same for a widget (General tab > Monitor; it starts near the monitor's top-left).
4. Bar Position > Edge: Top / Bottom moves it to that edge (its name "Top bar" / "Bottom bar" follows).
5. Turn the monitor back on: the bars and widgets left on it come back in their places (reserved space too, snap links kept), and the
   groups in Settings (if open) update.
6. Start vShell while that monitor is off: its bars and widgets don't flash up on the main monitor; they appear when it's turned on.
7. Win key with a Start menu on a hidden bar and one on a shown bar: the shown one opens.
8. Dragging a bar or widget onto another monitor still changes its Monitor setting (check its page).

## Log and updates from vSuite.Core (0.38.167)
1. vShell starts and runs as before; %APPDATA%\vShell\vshell.log is written as before (the times now show milliseconds). A line
   repeated in a loop still shows once plus "(the line above repeated N more times)". Settings > System > Open log opens it.
2. Settings > System > Updates: "This is vShell 0.38.167", Check now works and shows the newest release (or that you're up to date).
3. With a newer release published: the update notice, Install - vShell closes, the new one starts, your plugins come along (Settings >
   Plugins: each one loaded, none to fetch again), and the log has "Update: staged …" then "Update: now running …".
4. Plugins load as before (every one you've added shows in Settings > Plugins without errors).
5. A vShell run from bin\Release (not the published vShell.exe): Settings > System says updates can't be installed there.

## Themed questions (0.38.168)
1. Anything that asks or tells you something shows a window in vShell's colours (and glass with Blur / Frosted on) instead of Windows'
   grey message box: e.g. Settings > Bars > a bar > Paste settings (Yes / No / Cancel), a Proxmox VM's Shut down (a warning: Yes in the
   warning colour, a warning glyph), Settings > System > Restore a backup.
2. Enter picks the highlighted button (Yes / OK), Esc picks No or Cancel. Clicking works as before.
3. From a widget (e.g. a Proxmox widget's VM action): the question opens in front of everything on the widget's monitor and takes the
   keyboard - not hidden behind windows. From a bar or popup: on that bar's monitor.
4. Wallpaper Theme on: the question takes the same colours as the bars. Change the theme: the next question has the new colours.
5. A plugin's own questions (Seerr, Komodo …) look the same way.

## Settings in the suite's look (0.38.168)
1. Open Settings: no Windows title bar - the window's top row is vShell's own, with minimise / maximise / close at the top right (close
   turns red on hover). Drag the window by the top strip (the empty part above the page, or the logo area). Resize from the edges.
2. Blur or Frosted on (Appearance > Background): the window is glass - the wallpaper's colour shows through, like vXplorer's Options.
   Off: solid. Change it with Settings open: the window follows.
3. Maximise: the window fills the screen with nothing cut off at the edges; the button shows Restore.
4. Cards follow the widget style's corner radius (a square widget style: square cards). Every page's buttons, switches, sliders,
   drop-downs, text boxes and lists look as before and work as before; Change theme / colours: the window re-colours straight away.
5. Wallpaper Theme on: Settings takes the same colours as the bars.
6. The welcome window (Settings > About > Show the welcome guide, or a first start) has the same colours and controls.

## Settings' glass follows the opacity (0.38.169)
1. Appearance > Background: Frosted, opacity 90%: Settings shows a hint of the blurred wallpaper behind its colour, the same as a bar.
2. Lower the opacity (e.g. 30%): more of the blurred wallpaper shows - Settings and the bars change together. Blur instead of Frosted:
   no grain, a little less soft. Off: solid.
3. Move Settings around and onto another monitor: the blurred picture follows (the part of the wallpaper behind the window).
4. Change the wallpaper: within a few seconds Settings shows the new one.

## Blur and Frosted off still show the opacity (0.38.170)
1. Appearance > Background: Off (no Blur / Frosted), opacity 50%: Settings shows the wallpaper - sharp, not blurred - through its
   colour, the same as a bar over the desktop. 100%: solid. Blur / Frosted on again: blurred as before.
2. The welcome guide (About > Show the welcome guide) looks the same way.

## vSuite apps card; one blur for everything (0.38.171)
1. Settings > System > vSuite apps: vXplorer and vView with "0.1.N installed - the newest" (or "… is available", in the accent colour,
   with an Update button). Not installed: "Not installed" and Get vXplorer / Get vView (opens the releases page).
2. Open starts the app. Update (when a newer one is out): the app opens (or the open one is used) and its update notice shows the
   download, then it restarts on the new version.
3. Bars, widgets, popups, the start menu, the run box and the drop-down terminal with Blur / Frosted: the same as before - the blurred
   wallpaper behind, following moves, a bar's or widget's own Blur setting respected. A new wallpaper: picked up within a few seconds.
4. A plain-colour desktop (Personalisation > Background > Solid colour) with Blur on: the bars show that colour behind them.

## An Updates page in Settings (0.38.172)
1. Settings: Updates is in the page list (between System and About). It has a vShell card (version, Check now, Install, the two
   switches and the note) and the vSuite apps card (vXplorer, vView). System now has Behaviour and Backups only.
2. The tray icon's menu > Check for updates: Settings opens on Updates and the check runs (the vShell card shows it).

## Include pre-releases per vSuite app (0.38.173)
1. Settings > Updates > vSuite apps: under vXplorer and vView an Include pre-releases switch showing each app's own setting (its Options >
   Updates). vShell's own switch (in the vShell card) is separate.
2. With vXplorer open on Options > Updates: switch it in vShell - vXplorer's switch follows at once. Switch it in vXplorer - vShell's
   follows within a second. Close vXplorer, switch it in vShell, open vXplorer: its Options shows the new setting.
3. "… is available" and Update follow the app's switch: with it off a pre-release isn't offered.

## Tidy-up: Wallpaper Theme from the library (0.38.174)
1. Wallpaper Theme on: the bars, widgets and Settings take the same colours from the wallpaper as before (the log's "Wallpaper theme
   from …" line shows the same colours for the same picture). Change the wallpaper (or a slideshow moves on): the colours follow.
2. Settings and the welcome window look as before.

## Install a vSuite app from vShell (0.38.174)
1. On a PC without vView (or after uninstalling it): Settings > Updates > vSuite apps > vView shows "Not installed" and Install vView.
   Click it: "Looking for the installer…", then "Downloading the installer… N%", then the installer opens ("The installer is open -
   finish it there"). Finish it, come back to Settings: vView shows as installed with its version.
2. No release with an installer yet: the releases page opens and the row says so.
3. GitHub limiting requests (many checks in an hour): the row says "GitHub is limiting requests right now - try again later".

## vSuite apps card fixes (0.38.174)
1. Open Settings on another page, then switch to Updates: only then the rows say "checking…" (vshell.log has no GitHub lookups
   before). Close vXplorer / change its options with Settings open: the card follows, no new lookups.
2. Tick Include pre-releases for vXplorer (running): it stays ticked, and vXplorer's Options > Updates shows it on.
3. A vXplorer version that didn't start (rolled back): the card says it was skipped and has no Update button.
4. A question: Tab to the other button, Enter - that button's answer is taken.

## Plugin hand-off: vShell.exe --plugin (0.38.175)
Nothing uses it until a plugin registers a verb (the Shelf plugin's "add" will) - until then these check the plumbing.
1. With vShell running, run `vShell.exe --plugin shelf add C:\Windows` from a command prompt: it returns at once, nothing opens, and
   vshell.log has "Plugins: --plugin shelf add - Shelf isn't added" plus a notification "The shelf plugin isn't added" (it isn't yet).
2. Close vShell, run the same command: vShell starts as usual, and once its bars are up the same log line and notification appear.
3. Start vShell twice normally (no --plugin): the second start still just ends, nothing else happens.
4. A plugin with a verb (once Shelf has one): the words reach it in the running vShell, with the paths as given (spaces kept when quoted).

## Shelf plugin 1.0.0 (0.38.175)
Add it: Settings > Plugins > Shelf > Add (from a fresh build it's next to vShell.exe). Then Add a module > Shell > Shelf, on a bar and in a widget.
1. **Bar button** - a shelf glyph (the number of things on your shelves next to it once there are some). Click: a popup with the tabs
   Pocket, Shelf, Clipboard and +. Esc / clicking elsewhere closes it.
2. **Drop in** - drag a file, a folder and a drive from Explorer onto the popup's list: they appear with Explorer's thumbnails / icons and
   their folder underneath; the files don't move. Drag some text from a browser: a text card; a web address (or a picture dragged
   from a browser - browsers send its address): a link card. Copy image in the browser, then Ctrl+V in the popup: a picture card.
   Drop the same file again: no second copy.
3. **Spring-open** - close the popup, drag a file over the bar button and hold it there: the popup opens after half a second and stays
   open while you move onto it; drop it on the Pocket tab. Drop a file straight on the bar button: it goes to the shelf set in
   Settings > Plugins > Shelf ("Explorer and dropping on the bar button add to").
4. **Drag out** - drag a file from the shelf to the desktop: copied (the original stays, it stays on the shelf). With Shift held: moved
   (the original is gone, and a couple of seconds later it leaves the shelf). Alt: a shortcut. Set "Dragging something out" to Moves it
   and try again with no key. Text card into Notepad: its text; into Explorer: a .txt file.
5. **Pocket** - put three files from different folders in the Pocket, select them (Ctrl / Shift click, or Ctrl+A), drag them to a
   folder: copied there, and the Pocket is empty a couple of seconds later. "Drag all" in the footer takes everything.
6. **Between shelves** - drag an item onto another shelf's tab (or hold it over a tab to open that shelf): it moves there.
7. **Missing** - put a file on a shelf, then rename it in Explorer and open the popup again: greyed, "Not there any more". Right-click >
   Find it… and pick the renamed file: it's back with its new name. List right-click > "Take off the ones that aren't there".
8. **Menus** - right-click an item: Open, Show in folder, Copy (paste in Explorer copies the file), Copy path, Move to, Take off the
   shelf. Right-click a tab: Rename…, Colour, A Pocket, Move left / right, Clear (asks), Delete this shelf (asks), New shelf…. The
   popup stays open while the name box / questions are up. Ctrl+V in the popup pastes what you copied (files, a picture, text).
9. **Clipboard tab** - copy a few bits of text and a screenshot (Win+Shift+S): they appear at the top within a second, with the time.
   Click one: "Copied - paste it with Ctrl+V" and the popup closes (setting) - paste it somewhere. Drag text into Notepad, a picture
   into Paint / Explorer. Right-click: Keep on a shelf (a card), Delete from the history (gone from Win+V too). Clear asks, keeps Win+V's
   pinned ones. Turn Windows' clipboard history off (Settings > System > Clipboard): the tab says so with a Turn it on button.
   Also: after copying an item back, paste it in another app a few times - nothing hangs.
10. **Explorer menu** - right-click a file (Show more options, or Shift+right-click): "Add to shelf" with vShell's icon; it lands on the
    shelf. Select 10 files and do it: all 10 arrive. A drive (This PC > C:): "C:\" arrives. Turn the setting off: the entry goes;
    on: it's back. Remove the plugin: the entry goes. With no Shelf module anywhere: one notification "N added to your shelf".
11. **Widget** - Show: Every shelf (tabs), one shelf, or Clipboard history; Rows tall. Drag files in and out of the widget; clicking
    and dragging items must not move the widget. Right-click works as in the popup.
12. Restart vShell: the shelves, their items, colours and names are all still there (%APPDATA%\vShell\shelf\shelves.json, cards in
    shelf\cards).

## Shelf 1.0.1: title and a bigger icon (0.38.176)
1. A Shelf bar module with a Title set (its settings): the title shows in front of the icon, dimmed like other modules' titles. No title:
   just the icon (and the count).
2. The icon is a bit larger than the bar's text (about 1.3 times); on a slim bar it still fits (shrinks with the rest).

## Shelf 1.0.2: Drag all (0.38.177)
1. A shelf with a few files, in the bar's popup: press Drag all and drag it to a folder - every file on the shelf comes along (copied;
   Shift moves them). A Pocket empties a couple of seconds later.
2. The same from a Shelf widget (the widget doesn't move). A quick flick off the small button still starts the drag.
3. Just clicking Drag all: the footer says to drag it. Paste and the Clipboard tab's Clear / settings buttons still work.

## Shelf 1.0.3: a bigger drop area on the bar (0.38.178)
1. Drag a file over the Shelf bar button, above or below the icon (anywhere in the bar's height, and a little either side): it lights
   up across the bar's full height, holding there opens the popup, dropping adds it.
2. Clicking anywhere in that area opens the popup. The icon and title stay centred; a slim and a tall bar both look right.

## Shelf 1.1.0: edge tabs (0.38.179)
Settings > Plugins > Shelf > Edge tabs: tick "Shelf tabs on a screen edge". The rows below it appear.
1. **Rest** - Right edge, Centre: a thin strip on the right edge, one colour bar per shelf (plus grey for Clipboard). It doesn't take
   clicks away from what's under it apart from the strip itself, and never takes the focus.
2. **Hover** - rest the pointer on the strip: after a moment the tabs slide out (names on them, the count under). Move away: they go
   back in a second later. Untick "Slide out when the pointer rests on the strip": hovering does nothing.
3. **Drag reaches the strip** - drag a file from Explorer to the strip: the tabs slide out at once; drop it on a tab: that tab pulses,
   the file is on that shelf (check the bar's popup), the original stays put. The tabs go back after you let go.
4. **Drag starts** - with "Slide out as soon as a drag starts" on: start dragging a file in Explorer, on the desktop, or text / a picture
   in a browser - the tabs are out before you get there. Dragging a window by its title bar, or in other apps: nothing. Off: only as in 3.
5. **Hold a drag over a tab** - the drawer opens for that shelf (without taking the focus); drop onto its list. The Clipboard tab doesn't
   take drops.
6. **Drawer** - click a tab: that shelf slides out of the edge with its list (thumbnails, menus, Drag all, Paste, Ctrl+V), and the tabs
   move to its outer side. Click the same tab, X or Esc: it slides back. Click another tab: that shelf instead.
   - "An opened shelf stays open until you close it" off: clicking anywhere outside vShell closes it; right-click menus, Rename… /
     questions and dragging things out of it don't. On (or the pin in its corner): only X / Esc / its tab close it.
7. **Ribbon** - Tab style Ribbon: slim colour bars when out; the one under the pointer opens into a label with its name and count.
8. **Edges and monitors** - try Left (names read down), Top and Bottom (tabs hang down / stand up, names across), Along the edge Start /
   End, and another monitor. With a vShell bar on the top (or bottom) edge of that monitor: the strip and tabs sit right under (over)
   the bar and the drawer comes out of the bar the way the drop-down terminal does - square against it, its outline and corners.
   Move the bar, change its height, turn it off: the tabs follow within a moment. Unplug / change monitors: they move to the main one.
9. **Full screen** - play a video full screen / a game on that monitor: the strip and tabs hide; they're back when you leave it.
10. Untick "Shelf tabs on a screen edge": everything goes. Remove the plugin: same, and nothing is left watching the mouse.
## Shelf 1.1.1: edge tabs fixes (0.38.180)
1. **Settings tab** - Settings > Plugins > Shelf: the first tab says Settings. Komodo / Jellyfin / any connection plugin still says Connection.
2. **Link** - a Shelf module's settings (bar or widget): "Set up edge tabs ›" (or "Edge tabs settings ›" when on) opens Plugins > Shelf
   scrolled to Edge tabs; ‹ Back goes to Plugins.
3. **Bigger area** - the strip is drawn as before (a little longer per shelf) but the pointer or a drag reacts within about 18 px of the
   edge and 20 px past each end, gaps included.
4. **Swap shelves** - click Pocket's tab: its drawer opens. Click another shelf's tab, then the Clipboard tab: the drawer shows each in
   turn and stays open (with "stays open" off). A click outside vShell still closes it.
5. **vXplorer** - "Slide out as soon as a drag starts" on: drag a file out of vXplorer's list - the tabs come out. Drag one of vXplorer's
   tabs to move it, draw a selection box, or move its window by the title bar: they don't. Explorer as before.
6. **Pin** - Settings > Plugins > Shelf open, "An opened shelf stays open until you close it" ticked: open a shelf from the tabs, click
   the pin (filled = pinned) to unpin it: the tick in Settings goes, and a click elsewhere now closes the drawer. Pin it again: ticked.

## Shelf 1.1.2: when a shelf opens from its tab (0.38.181)
Settings > Plugins > Shelf > Edge tabs > "Open a shelf when a drag rests on its tab" (0.65 s as before).
1. 3 s: drag a file onto a tab and hold it - the tab grows, the shelf opens only after 3 s; drop before that: it's on that shelf, no drawer.
2. Never: holding never opens it; dropping on the tab still adds. Clicking a tab still opens its shelf.
3. Quickly (0.3 s): opens almost at once.

## LibreHardwareMonitor as the sensor source (0.38.182)
Install LibreHardwareMonitor, run it as administrator, and in its menu turn on Options > Remote Web Server > Run (port 8085).
1. A Hardware sensor module's settings: "Sensors from" above the list - pick LibreHardwareMonitor. The list fills with its values, named
   "part: group - value" (e.g. "AMD Ryzen 7 5800X: Temperatures - Core (Tctl/Tdie)"), and says how many are available. Pick one: the
   module shows it, updating each second.
2. Port: change LHM's port, type the same in vShell's Port box and leave the box: the list fills again. A wrong port, or LHM closed:
   the line under the list says it isn't answering on that port and how to turn the web server on; the module shows "-".
3. CPU / GPU / Memory modules with a temperature on Automatic: they pick LHM's CPU (Tctl/Tdie or CPU Package), GPU Core and DIMM
   temperatures. Notifications > the CPU / GPU heat warnings use them too.
4. Back to HWiNFO: everything reads HWiNFO again; a module set to an LHM sensor shows "(saved, not reported right now)" in its picker.
5. The log has "Sensors: reading LibreHardwareMonitor on port 8085" once (and one line when it stops answering), not one every second.

## Fans plugin 1.0.0 (0.38.182)
Settings > Plugins > Get more > Fans > Add (or add vShell.Fans.dll from a file). HWiNFO: each fan ticked "Report value in Gadget" (or LibreHardwareMonitor).
1. **Settings tab** - Plugins > Fans: "Your fans" lists every rpm reading (Chassis1, CPU, Chassis2, M.2 Fan) with its speed now. Rename one
   ("CPU" → "CPU fan", leave the box), untick one, move one up / down: the modules follow within a second. Empty name = its own again.
2. **Widget** - add a Fans module to a widget: Tiles, two across - each fan's name (dim) with a small fan turning on the right, the speed in
   big numbers over a line that fills to the bottom. Layout Tiles, one column: a stack. Rows: a fan in a ring, name and chip, a graph
   (Graph style Line / Bars / Meter) and the speed. The Graph section (line, fill, time shown …) changes the lines.
3. **Bar** - add one to a bar: Tiles side by side (as the Tile style in a bar; a bar under 35 px: name and speed on one line). Every fan,
   side by side: one fan, then name / graph / speed each. One fan with a graph: The fan picks which. Just the fan: the fastest speed.
   Click any: a popup with every fan as tiles, updating each second; click again or elsewhere closes it.
4. **The turning fan** - faster for a faster fan (about a turn a second at 1,000 rpm), still at 0. Untick "A turning fan by each speed": all
   still. A full-screen app in front: still. Task Manager: vShell's CPU barely moves with the fans showing.
5. **Stopped** - unplug / stop a fan (or set its speed to 0 in Fan Control): after 15 seconds its tile turns the warning colour, Just the fan
   says "<name> stopped" in red, and a notification "<name> has stopped - It was at 1,345 rpm …" (Notifications tab: A fan stops). Turn it
   back on: back to normal; stopping again notifies again. A header that reads 0 from the start never warns. GPU fans don't (the box).
6. **Slow** - Notifications tab: tick "A fan runs slower than", 1,200 rpm: a fan under it for the time set gives "<name> is running slow".
   A fan's own Warn below (Settings tab) wins; 0 there = never for that fan.
7. Remove the plugin: its modules say "add it in Settings"; no timer keeps running.

## Fans 1.0.1: fans that stop on their own (0.38.183)
1. Settings > Plugins > Fans > Settings: a Stops alone column. GPU fans (HWiNFO "GPU [#0] …", LibreHardwareMonitor "AMD Radeon … : Fans",
   anything with GPU / Radeon / GeForce / NVIDIA / Intel Arc) are ticked by themselves; the tooltip says found by itself or set by you.
2. Tick it on a case fan (Chassis2) and stop that fan: no "stopped" warning or red tile. Untick a GPU fan: it warns when it stops.
   Ticking a box back to what was found makes it automatic again (the tooltip says found by itself).
3. Notifications tab: untick "Not for GPU fans": GPU fans that you haven't marked lose their tick; ones you set keep yours.

## Fans 1.0.2: graph size, Ring (0.38.184)
1. A Fans module in a widget, Layout Rows: Graph style Line / Bars / Meter / Ring. Line, Bars, Meter: Graph length (0 = fills the row)
   and Graph height (% of the row, 0 = 60 %; a meter thin) change it at once. Ring: Ring size (% of the row) - the fan turns in the
   middle of the ring, which fills as the fan speeds up; a bigger ring makes the row taller.
2. On a bar, Every fan, side by side: each fan has its own turning fan now (not just the first). Graph style, length / height (% of the bar)
   and Ring the same. One fan with a graph: the same rows.
3. Tiles layouts don't show these rows (their line fills the tile).

## Fans 1.0.3: settings as the CPU module's (0.38.185)
1. A Fans module's settings (widget): Fans (Every fan / one of them), A turning fan by each speed; then Graph: Style first
   (Tile, Line, Bars, Meter, Ring), then for Tile: Tiles across 1-4 (the tile look rows: line thickness, fill, curves, time shown);
   Line: length, height, then thickness / fill / curves / box / rounding / grid / time shown; Bars: length, height, box / rounding /
   grid / time shown (no line rows); Meter: length, height and nothing else; Ring: Ring size only. Then Speed that fills the graph
   (not for Fan only), Graph colour, and the button to Settings > Plugins > Fans.
2. On a bar the same, with Style also Fan only (no graph rows), and Tile width instead of Tiles across. Fans set to one fan with a graph
   style: that fan alone with its graph (the old "One fan with a graph").
3. A module set up with 1.0.2 keeps its look (Tiles two across / one column, Rows with its graph style, the bar layouts).

## Fans 1.0.4: Colour by load (0.38.186)
1. A Fans module, Style Line / Bars / Meter / Ring / Tile: after the look rows, Graph colour, then Colour by load. Tick it: Graph colour
   goes, the Low / Middle / High colours appear, and the graphs blend from low to high as the fan speeds up (to the speed that fills it).
2. Style Fan only: just Fan colour (no graph rows).
3. vShell's own modules (CPU …) are unchanged.
4. **Max rpm** - Settings > Plugins > Fans > Settings: a Max rpm column. Type 2000 for the CPU fan (leave the box): its graphs fill at
   2,000 rpm (a fan at 1,000 is half way), and with Colour by load on it reaches the high colour at 2,000. Empty the box: automatic again
   (scaled to what it's shown lately). Under 100 or text: emptied. A fan with a Max rpm ignores the module's "Speed that fills the graph".
5. **Names** - the column says 0rpm mode (tooltip: switches itself off when not needed, 0 rpm is normal); Notifications tab: "No notification
   for 0rpm GPU fans", its note about 0rpm mode.

## Shelf 1.1.3: smooth edge tabs (0.38.187)
1. Edge tabs on (Settings > Plugins > Shelf), slide out on hover. Rest the pointer on the strip: the tabs slide out of the edge in one
   smooth move - no flash of the thin strip in the wrong place, no jump. Try the Right, Left, Top and Bottom edges, and a Top / Bottom
   edge with a bar there.
2. Move along the tabs: the one under the pointer grows out of the edge (and the one you left shrinks back) instead of snapping.
   Ribbon style: the ribbon grows and its name fades in.
3. Move away: after a moment the tabs slide back into the edge and the thin strip fades in. Come back onto the strip while they're
   going: they come out again.
4. Next to the strip / tabs (the empty space a little further from the edge) clicks still reach the window underneath.
5. Drag a file to the edge: out at once, drop on a tab, rest on a tab to open its drawer - as before. With a drawer open, the tabs sit on
   its far side; close it: they go back to the edge.
6. A full-screen video / game: the tabs hide, and come back when it ends.
## Wallpapers set again (0.38.188, vSuite.Core 1.0.14)
1. Settings > Wallpaper with per-monitor wallpapers on: restart vShell. The log (%APPDATA%\vShell\vshell.log) has no
   "Wallpaper apply: System.InvalidCastException" line, and each monitor shows its picture.
2. Change one monitor's picture or fit in Settings: it changes on that monitor at once (no error in the log).
3. A slideshow: Next wallpaper (or wait for it) moves on.
4. Unplug / turn off a monitor and back: its wallpaper comes back.
5. Bars with Blur / Frosted: the blur shows the picture on the bar's own monitor (not the Windows wallpaper from the registry).
6. Wallpaper Theme on: the colours come from the main monitor's picture.
## Fans 1.0.10: stepped fans, smooth Settings drag (0.38.188)
1. A Fans module on a bar with the turning fans showing (several fans), Settings open with Frosted (or Blur) on. Drag the Settings window
   around slowly and quickly: the blurred wallpaper behind it moves with the window, no jumping - as with the turning fans switched off.
   The fans keep turning while you drag.
2. The fans turn fairly smoothly (10 steps a second), always the right way round: a slow fan (500 rpm) about a turn in 12 s, 1,000 rpm in
   6 s, 2,500 rpm and faster in 2.4 s. A fan speeding up / slowing down changes speed without a jump back.
3. A fan that stops (0 rpm, or under 50): its icon stands still; when it starts again it turns on from where it stood.
4. Turning fans switched off in the module's settings: all still. A full-screen app in front: still; back on the desktop: turning again.
5. Fans in a widget, in the bar's popup and in rows: all turn the same way.
6. Remove the Fans plugin (Settings > Plugins) and add it again: the fans turn as above, no errors in the log. Task Manager: vShell's CPU
   with the fans turning is no higher than with 1.0.4.

## 0.38.189: installer without the version in its name
1. build.cmd: dist\setup\ and dist\release\ have vShell-setup.exe (no version number); run it - it installs / upgrades as before, and
   Programs and Features shows the right version.
2. Attach it to the release: https://github.com/version-au/vShell-releases/releases/latest/download/vShell-setup.exe downloads it.
3. vShell's Settings > Updates > vSuite apps with vShell not installed: Install finds the new vShell-setup.exe (and an older
   release's vShell-setup-<version>.exe while the newest release still has the old name).

## 0.38.190: updates from vsuite.vrsion.com (the bridge release)
Publish: build.cmd as usual. dist\release\ goes to GitHub one last time (copies before 0.38.190 only know GitHub); dist\site\ is copied
over the site's root (vshell\latest.json, vShell.exe, vShell-setup.exe, changelog.md, vshell\plugins\2\ with plugins.json and every
plugin DLL). vXplorer and vView bridge the same way with their own builds.
1. https://vsuite.vrsion.com/vshell/latest.json opens in a browser and shows 0.38.190; .../vshell/vShell-setup.exe downloads.
2. A copy on 0.38.189: its update (from GitHub) installs 0.38.190 with your plugins, as before.
3. On 0.38.190, Settings > Updates > Check now: "You have the latest version" - no GitHub error. The log has no "GitHub" lines.
   Click it many times in a row: never "limiting requests". "vSuite website" opens the site.
4. Settings > Plugins: the list reads "Checked … (v0.38.190)"; Add a plugin you don't have: it downloads (from vshell/plugins/2/).
   Remove it again.
5. A newer test build (say 0.38.191, built with build.cmd and copied to the site): the notice comes up and its notes list the changes
   of 0.38.191 AND 0.38.190 (every version since yours). Install and restart: vShell comes back on 0.38.191 with all your plugins.
6. Pre-release: build.cmd pre, copy dist\site\ up (it goes to vshell\pre\). Include pre-releases off: not offered; on: offered (marked
   pre-release). Settings > Plugins with pre-releases on reads the list from "… pre-release".
7. A plugin only (plugins.cmd, copy dist\site\ up): Settings > Plugins > Check for updates offers the new build.
8. Settings > Updates > vSuite apps: vXplorer and vView show their versions from the site (once theirs are up); Install for one that
   isn't installed downloads its Name-setup.exe from the site and starts it.
9. The site down / unreachable (e.g. a wrong address in VSHELL_UPDATE_SITE): Check now says it couldn't reach the update site; nothing else
   breaks.
10. While a new vShell.exe is still being copied up (latest.json already new): Install says the download doesn't match the checksum -
   try again; nothing is changed. (Copy latest.json last to avoid it.)

## 0.38.191: the first site-only release
1. On 0.38.190 (from GitHub or the site), with 0.38.191 copied to the site: tray > Check for updates (or wait for the check a minute
   after start): 0.38.191 is offered and the notice lists the changes of 0.38.191 and 0.38.190.
2. Install: vShell restarts on 0.38.191 (Settings > About) with all your plugins; the log shows it read vsuite.vrsion.com, no api.github.com.
3. Settings > Updates > vSuite apps: vXplorer 0.1.50 and vView 0.1.18 show as the newest; Update starts each one updating from the site.
4. A copy still on 0.38.189 (GitHub only): it updates to 0.38.190 from GitHub, then to 0.38.191 from the site.

## 0.38.192: the Plugin SDK from the update site
1. Settings > About > Plugin SDK > Download: the browser downloads sdk.zip from https://vsuite.vrsion.com/vshell/sdk.zip (not GitHub).
   It holds vShell.dll, plugin-api.txt (its first line names this vShell's version) and plugins.md.
2. build.cmd: dist\site\vshell\sdk.zip is made with the build (and by plugins.cmd); build.cmd upload puts it on the site.
3. With VSHELL_UPDATE_SITE set to another address, Download opens that site's /vshell/sdk.zip instead.

## 0.38.193: adding a plugin with an old copy next to vShell.exe
1. Put an older build of a plugin next to vShell.exe (e.g. Fans 1.0.3 as vShell.Fans.dll), with a newer one on the update site (1.0.10).
   Settings > Plugins > add Fans: it comes as 1.0.10, and no update is offered for it. The log says the copy next to vShell.exe is older
   than the update site's - using the site's.
2. A fresh dist\ build (the plugin next to vShell.exe as new as the site's, or newer): it's used from there, as before (from next to vShell.exe).
3. Offline (no plugin list from the site): the copy next to vShell.exe is used.

## 0.38.194: the terminal is vTerm
vTerm must be installed, or set VSHELL_VTERM_EXE to a built vTerm.exe before starting vShell. vShell and vTerm talk over
vSuite's TermLink pipe (vSuite.Core 1.0.20).
1. No vTerm (rename it away / no VSHELL_VTERM_EXE): click a bar's Terminal module - "vTerm isn't installed yet…"; Yes opens Settings >
   Updates, where vSuite apps lists vTerm with Install. The gallery's Terminal card says "Needs vTerm". The module's settings say so too,
   with "Go to vSuite apps".
2. vTerm installed, not running: click the Terminal module - vTerm starts in the background (its log: "starting in the background (for
   vShell)") and drops down from the bar within a few seconds (the first start sets up its browser part). No vTerm window or taskbar button.
3. It drops down flush from the bar (top bar: down; bottom bar: up), as wide as the bar's straight edge at most (the module's Width), the
   module's Height; the bar's corners, outline, shadow and blur (Popup > Background effect off: no blur). Attach it to the bar off: a free
   card centred on the monitor, under / over the bar, in the widget style. Theme colours follow a theme change.
4. Click the module again / press its keys (Ctrl+Alt+T): it slides away. Again: the same tabs, shells still running. Hide it when you click
   another window on: clicking elsewhere hides it; off: it stays.
5. "New tabs start with" lists vTerm's profiles; the first tab of a fresh drop-down uses it.
6. Proxmox (a node / guest > Open shell), Komodo (a server / container > Open shell), Linux Updates: an SSH tab in the drop-down, using
   vTerm's saved connection for that host; none saved = vTerm asks Save one / Connect / Cancel. A guest without an address: vTerm says why
   and offers to make a connection with its name.
7. Palette: "ssh <name>" lists vTerm's saved connections (added in vTerm, they show here); picking one opens it in the drop-down. "Open a
   terminal" shows the drop-down.
8. Existing settings with a Terminal module in a widget: it's gone after starting (the log says so); the widget gallery has no Terminal.
9. Close vShell with nothing running in the drop-down (and no vTerm window): vTerm ends too. With shells running there, it stays.
10. vShell.exe is smaller (no WebView2 / SSH.NET); `--plugin-api` matches docs\plugin-api.txt (only the header's version changed).

## 0.38.195: typing in a note that grows, in a fixed-height widget
1. A widget with a fixed height (Settings > Widgets > Height) holding a Notes module with "Grow with the text" on and a note longer
   than the widget (it scrolls). Double-click near the bottom of the note: the editor opens and keeps the keyboard - type, wait a few
   seconds, keep typing. (Before: the widget re-fitted itself 1.5 s later, rebuilt its modules and the editor was gone.)
2. Esc / click away: the note shows again; the widget fits itself again a moment later (its log may say "fit: … stepping down" then,
   as before - never while typing).
3. A fixed-height note (Grow off) on another tab: typing works as before.

## 0.38.196: vPad in vSuite apps. Settings > Updates > vSuite apps: vPad is listed after vTerm with its blurb. Not installed: Install finds vPad-setup.exe on the update site
   (once vPad has a release there) and runs it. Installed: its version, Open, and Update when the site has a newer one.

## 0.38.197: scrollbars (vSuite.Core 1.0.23)
1. Settings: a long page (Modules, Plugins): the scrollbar's thumb is at least ~40 px, widens when pointed at; bars and widgets are unchanged.

## 0.38.198: the update's progress bar (vSuite.Core 1.0.24)
1. Update to a newer release from this version (the running copy downloads it): the notice's bar and percentage move steadily with the
   download, and the window stays responsive during "Checking and installing…". (Updating TO this version still shows the old behaviour -
   it's the version doing the download that matters.)
2. Settings / Options made small enough to scroll: its scrollbar is the thin one that widens when pointed at, with a ~40 px thumb, and sits a
   little in from the window's right edge - grabbing it never starts a resize.

## 0.38.199: the log. With UniFi Protect set up, leave vShell running for a day: vshell.log has no "Unobserved task error ... Unifi.Listen" blocks (there were
   5-46 a day, one per reconnect). A reconnect still logs its one-line reason ("Protect stream: …").
## 0.38.199: Start menu styles and pinning from the menu
1. Settings > the Start menu module > Style: Classic / Grid / Two panes / Cascading, with a note under it for each. Classic is the menu as
   before (its Pinned switch is only shown for Classic - the others always have Pinned; Edit pinned… shows for all of them).
2. Grid: search box, "Pinned" with your pins six to a row and "All programs ›" on the right; Recent files (if on) in two columns; you,
   your folders, Settings and Power along the bottom (Power opens its list above the button, on the right). All programs: a Back row
   (Backspace / Left / click) back to the grid; folders open inside as before. Typing searches.
3. Two panes: Pinned / Recent files (if on) / All programs and a "Groups" heading with each pinned group on the left; the one shown is
   highlighted and its name is a heading on the right. Opens on Pinned (All programs when nothing is pinned). A group / folder opened
   inside has a Back row; Backspace never leaves the category. Typing searches (no category highlighted); clearing the search goes back.
4. Cascading: the Start button opens a menu at the button: your pins (or "Right-click a program to pin it here"), Programs › its folders
   (each opening to the side) and programs, Recent files ›, your folder, Documents, Downloads, Pictures, Settings, Power › (lock, sleep,
   sign out, restart, shut down). Clicking the Start button again closes it (doesn't reopen); Esc / a click elsewhere closes it. With the
   Windows key it opens with the first item picked and the arrow keys / Enter work at once. A program named with an "_" shows the "_".
5. Pinning: in Classic / Grid / Two panes, right-click a program (in All programs, a folder or search results) > Pin to Start - it shows
   in Pinned straight away (Classic: the Pinned section turns on). Right-click a pinned one > Unpin from Start - it goes. Recent files,
   folders and Back rows have no menu. The pins are the same list as Edit pinned… (and saved).
6. Cascading: right-click a program under Programs: "Pinned" appears on its right (again: it goes); the menu stays open. It's at the top
   the next time the menu opens. Right-click a pin at the top: it goes from the menu straight away.
7. Text size S / M / L: every style's text follows it (Grid / Two panes get a little wider with L).
## 0.38.200: the Cascading Start menu, rebuilt
1. Start menu module > Style > Cascading. Click Start: a popup in the widget style (same background, corners, border, blur and rows as the
   other styles) above / below the button: your pins (or "Right-click a program to pin it here"), a line, Programs ›, Recent files › (if
   on), a line, your folder, Documents, Downloads, Pictures, Settings, a line, Power ›.
2. Point at Programs: after a moment a flyout opens beside it, its first row level with Programs, in the same style. Point at a folder in
   it: the next flyout opens beside that row. Point back at another row in an earlier popup: the flyouts after it close (a moment later).
   Moving the pointer diagonally across a row on the way into a flyout doesn't close it. Clicking a folder row opens its flyout at once.
3. A bar on the right edge of a monitor: the flyouts open to the left. A long folder (Programs) scrolls instead of running off the screen.
4. Clicking a program starts it and closes everything; a click outside, Esc, or the Start button again closes the menu and all flyouts.
5. Keys (Windows key or a click to open): Up / Down move in the deepest open popup, Right / Enter open a folder's flyout and move into it,
   Left / Backspace go back, Enter starts a program, a letter jumps to the next row starting with it. The flyouts never take the focus
   (the menu keeps the keys; no flicker of the window title / taskbar).
6. Right-click a program in a flyout: Pin to Start / Unpin from Start (the same menu as the other styles). Pinning: it appears in the pins
   at the top straight away; the flyouts stay open. Right-click a pin at the top > Unpin from Start: it goes (a flyout opened from that pin
   closes).
## 0.38.201: pinning in the Cascading Start menu
1. Cascading style. Programs › a folder › right-click a program: a small flyout opens beside it (same style as the menu) with "Pin to
   Start". Click it: the flyout closes, the program is in the pins at the top straight away, the Programs flyouts stay open.
2. Right-click it again: the flyout says "Unpin from Start"; click it: it goes from the top. Right-click a pin at the top: "Unpin from
   Start" beside it; click: it goes (a pinned group's flyout closes if it was open).
3. Pointing at another row instead closes the little flyout (a moment later); a click outside closes everything.
4. Keys: Down to a program, the menu key (or Shift+F10) opens the flyout with the choice picked; Enter pins / unpins; Left goes back.
5. Settings > the module > Edit pinned… shows the same pins.
## 0.38.202: clicks in the Cascading menu's flyouts
1. Cascading. Programs › a folder › right-click a program: the "Pin to Start" flyout opens and the menu stays open (before: everything
   closed). Click it: pinned (at the top), the other flyouts still open.
2. Click a folder row inside a flyout (instead of pointing at it): its flyout opens, the menu stays open.
3. A click outside the menu (desktop, another program) still closes the menu and every flyout; so do Esc and the Start button.
4. If the menu closes by itself unexpectedly, vshell.log says why: "Start menu (Cascading) closed: a click on <window class> (...)" or
   "lost the focus to ..." - send that line.
## 0.38.203: the Cascading menu stays open when another vShell window takes the focus
1. Cascading. Programs › (a folder ›) right-click a program: the menu stays open and "Pin to Start" opens beside it; click it: pinned.
   0.38.202's log showed the menu "lost the focus to HwndWrapper[vShell...] (vShell, topmost)" there - one of vShell's own windows.
2. vshell.log now has one line saying which window that was: "Start menu (Cascading): the focus went to <handle> <type> '<title>' -
   kept open (menu + flyouts: ...)". Send it - it shows what took the focus.
3. A click on the desktop / another program, Alt+Tab to another program, Esc and the Start button still close the menu.
## 0.38.204: popups and a moment's "lost the focus"
1. Cascading Start menu: right-click a program in Programs (and in a folder's flyout) > Pin to Start - the menu stays open and it pins
   (0.38.203 kept it open for any vShell window taking the focus; 0.38.202's log showed it was the menu itself, still in front).
2. Every popup still closes when you really leave it: a click on the desktop / another program, Alt+Tab, Esc. Try the Start menu (each
   style), a launcher group, the calendar, the volume mixer, a tray overflow.
3. The Cascading menu still logs why when it closes by itself ("Start menu (Cascading) closed: …"); the "focus went to" line is gone.

## 0.38.205: vTrace in vSuite apps. Settings > Updates > vSuite apps: vTrace is listed after vPad with its blurb. Not installed: Install finds vTrace-setup.exe on the
   update site once vTrace has a release there. Installed: its version, Open, and Update when the site has a newer one.

## 0.38.205: the vTrace plugin (plugins\VTrace, 1.0.0)
1. Settings > Plugins: vTrace (Servers & network). Without vTrace run once: its modules say "Needs the vTrace app (Settings > Updates > vSuite apps), run once".
2. Bar module "vTrace": "12 today" (glyph in the accent); with an outage open in vTrace: "Jellyfin down" in red, tooltip with since when. Click: a popup
   with "Changes · 12 today", today's hours as bars (an outage's hour red), the latest changes (repeats "×4"), "Open vTrace". Hide when
   nothing is down and nothing changed today: it goes.
3. Widget module "vTrace changes": the same list (rows and the chart in its settings); click it: vTrace opens (or comes to the front).
4. vTrace looks for new changes: within ~5 s the modules show them (they read vTrace's changes.json when it changes).

## 0.38.206: UniFi plugin - Account for vTrace's history. Settings > Plugins > UniFi: under the run box keyword a heading "Account for vTrace's history
   (optional)" with Username and Its password (the (i) says how to make a local View Only account); a note that vShell doesn't use it. Fill
   them in, Test: saved (the password encrypted, as the keys). vShell's UniFi modules work exactly as before, with or without it.

## 0.38.207: vShell's tray icon (vSuite.Core 1.0.30 TrayIcon)
1. Settings > System > Show vShell in the system tray on (no Tray module on a bar, so it's in Windows' tray): the logo in the theme's colours;
   change the accent - the logo follows. Tooltip "vShell 0.38.207" (+ "update … ready").
2. Click: Settings. Right-click: Settings…, Notification history, Check for updates, Restart vShell, Exit vShell - in the theme's colours (as the
   Settings window's menus, not Windows' plain menu), at the pointer. Click the desktop / another program / the taskbar, or Esc: it closes.
3. Restart Explorer: the icon comes back. Switch the setting off: the icon goes.

## 0.38.208 - vTrace plugin: refresh while vTrace isn't open (needs vTrace 0.1.8)
1. Close vTrace. The vTrace widget: a refresh button (circling arrows) at the right of "Changes · N today", and under it "Checked 2:14 PM · 3 h ago"
   (amber when over an hour ago; "Not checked yet" with no looked.txt - then changes.json's time is used).
2. Click refresh: the line says "Checking for changes…", the button dims; no vTrace window or tray icon appears (Task Manager shows vTrace.exe
   briefly). When it ends (a minute or so), new changes show within a few seconds and "Checked … · just now". Clicking the widget elsewhere still opens vTrace.
3. The bar module: the same button after "N today" (click it = refresh, not the popup); the tooltip ends with the checked line. Its popup: the
   button and the checked line as in the widget.
4. Open vTrace (or have it minimised to the tray): within a couple of seconds the buttons and the checked line go from all three.
   Close vTrace: they come back.
5. Testing with a built vTrace: VSHELL_VTRACE_EXE = its vTrace.exe (else %LOCALAPPDATA%\Programs\vTrace\vTrace.exe).
6. Settings > Plugins > vTrace: "Check for changes" - Off (at first), Every 5 / 10 / 30 / 60 minutes. Pick 5 with vTrace closed: within a minute
   or so a check runs if the last was over 5 minutes ago (the widget says "Checking for changes…", then "Checked … · just now"), then every
   5 minutes. Open vTrace: no checks from vShell while it's open. Off: none. vTrace not installed: nothing happens, no message.

## 0.38.209 - Sonarr / Radarr: health warnings on or off
1. A Sonarr module's settings (bar and widget): "Show health warnings and errors" (on at first). With a health issue in Sonarr (System > Status):
   on - the widget / popup shows the "⚠ … health issues" line, a bar shows the red "!" badge with an empty queue, its tip lists the errors,
   and "Hide while nothing is in the queue" still shows it for a health error.
2. Off: no health line in the widget / popup, no red "!" badge or error lines in the tip (the badge is the queue's colour), and "Hide while
   nothing is in the queue" hides it despite a health error (the note under it says only "can't be reached" then).
3. The same for Radarr. Health notifications are separate (Settings > Notifications > Sonarr / Radarr) - unchanged.

## 0.38.210 - Sonarr / Radarr widgets with a set width
1. A widget with a Sonarr (or Radarr) module, Automatic width on: it sizes itself; switch tabs - it keeps its size.
2. Turn Automatic width off (a set width narrower than it was): the header fits - the "Sonarr" button at the right edge, the counts beside
   the name (cut with … if needed), nothing past the widget's edge. No need to toggle the Sonarr button any more.
3. Turn Automatic width back on: it sizes to its content again (not to the set width it had).
4. The same for Radarr. Seerr / Tdarr unchanged.

## 0.38.211 - vTrace plugin 1.2.0: grouped outages and their likely cause (needs vTrace 0.1.11)
1. With outages that went down together (vTrace groups them: within 5 minutes, on the same machine): the vTrace widget / popup shows one red
   line "jellyfin-lxc went down with 5 more on pve - since 9:14 PM", and under it, dimmer, "Most likely: Restarted … (Proxmox, 9:12 PM)".
   Hover either: what it runs on ("Runs on: jellyfin → pve"), the cause and why.
2. The list: a grouped outage that's over is one row, "… was down for 6 min · with 5 more on pve"; the others aren't listed on their own.
   Hover an outage row: the same tip.
3. The bar: "6 down on pve" when they're all one group (one outage: "Jellyfin down"; several groups: "8 down"); the tip lists each group
   with what it runs on and the cause.
4. With an older vTrace (no analysis in changes.json): as before - every outage on its own, no cause line.
5. Plugin 1.3.0 (needs vTrace 0.1.12): the cause line ends "· View" (accent). Click it: vTrace opens (or comes forward, from the tray too) on that
   change, picked. Click the red outage line: vTrace on the outage. Click any row in the widget or the bar's popup: vTrace on that change (the widget
   doesn't start a drag). With vTrace 0.1.11 or older: it just opens.
6. Plugin 1.4.0 (vTrace 0.1.14, Local AI on): an outage still going that vTrace's local AI explained shows "AI: …" under the cause - up to 3
   lines, cut with …; click: vTrace on the outage. Every outage's tip (widget row, red line, bar) ends with "Local AI (<model>, time):" and the
   whole answer, wrapped. An explanation that arrives later shows within a few seconds.

## 0.38.211 - the Local AI plugin 1.0.0 (Lemonade, Ollama, LM Studio, OpenAI-style)
1. Settings > Plugins > Add: Local AI. Its page: Address (http://192.168.1.70:13305, or …/api/v1 - either), API key, Name, Model for
   questions ("(the loaded one)" + the server's models - Find models fills it), Let it think first (off), Run box keyword (ai), Test
   ("Lemonade 11.9.0 - Qwen3.5-4B-GGUF loaded, 4 models"), and More servers ("gaming-pc http://192.168.1.50:11434, …").
2. A bar module (gallery: Info > Local AI): dot (green idle, accent answering, red offline) + "Qwen3.5-4B · idle"; options: the model,
   idle / answering, the last answer's speed, a title. Hover: every server, its model, the last answer's speed. Click: a card per server -
   name, Lemonade 11.9.0, state; the loaded model (ctx 12k · GPU); a speed chart once there are 2+ answers and "14 tok/s · 76.4 s to the
   first word"; "N answers · 9.7k in · 463 out, since it started"; hardware and "75 GB free for models" (Lemonade); Models with Load /
   Unload (Lemonade: unloads the one loaded; Ollama: kept an hour) - "Loading…" while it works, an error in a message; Refresh, Settings.
3. A widget: the same cards (every server, or only the main one), the speed chart, and optionally the models with Load / Unload.
4. Run box: "!ai" alone lists the servers; "!ai what does docker system prune remove?" + Enter: "Asking Qwen3.5-4B…", then the answer under
   the box (scrolls past 360 px) with Copy, and "Qwen3.5-4B · 2.9 s". A thinking model is asked not to think (Let it think first off).
5. Palette: "Refresh Local AI", "Load <model>" / "Unload <model>" for Lemonade / Ollama servers.
6. Notifications (Settings > Notifications > Local AI): offline (two missed looks, only after it was online), back, model storage nearly
   full (Lemonade: < 10% or 5 GB, once a day).
7. vTrace 0.1.16 with its Local AI fields empty uses these (Address, API key, Model, Let it think first).
8. Plugin 1.0.1: "!ai what does docker system prune remove?" shows one row "Ask Qwen3.5-4B: …" (Enter); Enter asks - no "Windows can't
   find" any more (the bang was added under another kind than its RunAction's, so the run box ran the text as a program).
9. Looks: every 5 s while a module shows it, every minute otherwise. Ollama: /api/ps, /api/tags; LM Studio: /api/v0/models (no Load /
   Unload); other OpenAI-style: /v1/models ("it doesn't say which is loaded").

## 0.38.211 - the vDeploy plugin 1.0.0 (needs vDeploy 0.1.5)
1. In vDeploy: Options > Sign-in > Apps > Make a key (your password) - copy it. In vShell: Settings > Plugins > Add: vDeploy. Its page:
   Address (http://192.168.1.27:8460 - "192.168.1.27:8460" works too), App key (vd_…), Run box keyword (deploy), Send vShell's colours
   (on), Test: "vDeploy 0.1.5 - 1 host, 4 stacks, 1 with newer images". A wrong key: "vDeploy didn't take the key …"; a wrong port:
   "Couldn't reach vDeploy: …"; another web page at the address: "answered with a page, not vDeploy's API".
2. Colours: after Test (and whenever vShell's theme changes) vDeploy's open pages change to vShell's colours within a few seconds;
   vDeploy's Options > Look says "Sent by vShell …". Off here: nothing more is sent (vDeploy keeps the last ones until its own switch is off).
3. A bar module (gallery: Info > vDeploy): dot (green all running, amber something stopped / starting, red a problem or a host not
   answering, accent while a deploy runs) + "14/15 running", "⚠ 1", "⬆ 2". Hover: the summary. Click: the stacks by host - dot, name,
   ⬆ newer, ✎ changed on the host, running / total; Update on one with newer images; click a stack = its page in vDeploy; right-click =
   Update / Redeploy…, Restart…, Stop… / Start, Open in vDeploy (the … ones ask). Open vDeploy, Updates, Refresh.
4. A widget: the stacks grouped by host (or not), "Only stacks that need you", How many; right-click a stack for its actions.
5. Run box: "!deploy" = stacks with problems, newer images, a deploy running or changed on the host; "!deploy web" = stacks / hosts
   named like it. Enter: Update (or Redeploy), Restart, Stop / Start, Open in vDeploy, Back to the list; Update / Restart / Stop need
   Enter twice. "vDeploy is on it: …" - and vDeploy's Activity has "Redeployed web on docker-test · from <the key's name>".
6. Notifications: vDeploy's Options > Notifications decides which go to vShell (vShell / Both). They arrive within 30 s (5 s while a
   module shows); only new ones - starting vShell doesn't replay old ones. Settings > Notifications > vDeploy: the switch and the three
   kinds (problems, newer images, the rest). Click one: its page in vDeploy.
7. Palette: "Open vDeploy", "Check vDeploy for newer images", and per stack "Update / Redeploy …", "Restart …", "Stop / Start …",
   "Open … in vDeploy".

## 0.38.212 - the vDeploy plugin 1.1.0: containers, shells, logs (as the Komodo plugin's; needs vDeploy 0.1.9 for logs and containers)
1. The bar module's popup / the widget: each stack row has an arrow - click it: its containers under it (dot, service, CPU % · memory
   while running, else stopped / unhealthy …); click again (or right-click > Hide the containers) to fold them. Folded / unfolded is the
   same in every vDeploy popup and widget, until vShell closes.
2. Right-click a container: "<service> (<stack>) - running", Restart… / Stop… (ask), or Start; Open shell in the container (vTerm's drop-
   down, SSH to the host - vTerm's saved connection for its address, or it offers to make one, root by default - then `docker exec -it
   <container> bash/sh`; greyed out when it isn't running); Open shell on <host>; Show its log; Open in vDeploy (the container's view).
   Restart one: "asking…" on its row, then its state; vDeploy's Activity has "Restarted …".
3. Click a container: its log window. Right-click a stack: Update / Redeploy…, Restart…, Stop… / Start, Show / Hide the containers,
   Show its log (every service), Open shell on <host>, Open in vDeploy.
4. The log window: "<service> · <stack> · on <host>" (or "<stack> · every service"), Every service / one container, Last 50 / 100 /
   500 / 1000 lines, Follow (every 3 s, keeps to the bottom), Times (Docker's, in your clock - off: taken out; a stack's lines keep
   "web-1  | "), a filter (Ctrl+F), Refresh (F5), Copy, Open in vDeploy; error / fatal lines red, warnings yellow; Esc closes. The same
   stack / container again brings its window forward. An older vDeploy: "This vDeploy is too old for logs here - update it to 0.1.9".
5. The icons: the plugin's glyph (Settings > Plugins, the gallery), the popup's buttons, the run box rows and the palette all show icons
   (1.0.0 had empty ones).
6. Automations: the module's settings (bar and widget) have "Show the automations (with a Run button)", on at first. Under the stacks:
   "Automations", each with a dot (green last run worked, red failed, grey never run, accent running), its name, when it last ran ("failed ·
   2 h ago") or its schedule, and ▶ Run. Run asks "Run Nightly tidy now? (5 steps)", then the row says running… until vDeploy's next look
   shows it done; vDeploy's Activity has "Ran …". Click the row: the automation in vDeploy; right-click: Run now, Open in vDeploy. With
   "Only stacks that need you" only automations whose last run failed show. Off: no automations.

## 0.38.213 - a preview's window menu
1. Hover a taskbar button (or click an app with several windows): right-click a preview - the window's own menu at the pointer, as
   right-clicking a thumbnail on Windows' taskbar: Restore / Move / Size / Minimize / Maximize / Close, plus anything the app adds
   (a console's Properties / Defaults, PowerToys' Always on top). Greyed as the title bar's menu: a normal window - Restore greyed; maximised -
   Restore on, Move / Size / Maximize greyed; minimised - Restore on, Minimize / Move / Size greyed; a dialog with no maximise box -
   Maximize greyed. Previews off (the list): the same on a row.
2. Pick each: Restore / Maximize bring it to the front; Minimize; Close (asks to save if it has changes); Move / Size: it comes to the
   front in keyboard move / size mode (arrow keys, Enter). The previews close once something is picked.
3. The previews stay open while the menu is (even past them); Esc or a click elsewhere closes the menu - then the previews.

## 0.38.214 - bars put back after a monitor returns
1. Turn a bar's monitor off (or unplug it) and on again a few times, waiting a little in between: the bar is hidden while it's away, then
   comes back at its edge - a bottom bar right at the bottom, not part-way up. Within a second of anything moving it, it's back.
2. The log: "Bar 'Bottom': moved by something else (x,y WxH, placed at x,y WxH) - put back" or "its monitor is now WxH at x,y - placed
   again" when that happened. Nothing like that every second while nothing changes (a bar left alone writes nothing).
3. Dragging a bar to the other edge / another monitor still works (the check waits while you drag); a dock / auto-width bar still
   grows and shrinks with its content.

## 0.38.215 - the preview menu in front
1. Hover a taskbar button, right-click a preview: the window's menu shows in front of the previews and stays there (also over a
   preview with several windows, and with an app that changes its title meanwhile). Pick an item / Esc: the previews close as before.

## 0.38.216 - bars not pushed up by Windows' hidden taskbar
1. With Hide the Windows taskbar on, let the monitors sleep and wake (or turn one off and on). If Explorer shows "A toolbar is already
   hidden on this side of your screen", the bottom bar still sits right at the bottom (before: 63 px up at 150%, until vShell restarted);
   the log says "Bar 'Bottom': Windows' hidden taskbar still holds 63 px of this edge (it couldn't auto-hide) - the bar stays at the edge".
2. With Hide the Windows taskbar off, a bar on the taskbar's edge still sits next to Windows' taskbar, not over it.
3. Another app bar on the same edge (not Explorer's) still moves a vShell bar in as before.

## 0.38.217 - fewer widget rebuilds, quieter log
1. A fixed-size widget a little too short for its modules, with no graphs and no L text (e.g. Proxmox / vDeploy / Uptime Kuma lists): the
   log shows "fit: level 0 … stepping down", "fit: level 1 … stepping down to level 6" and one more build - 3 builds, not 7 - and it
   ends up the same (all S). A widget with graphs still shortens its graphs step by step first.
2. A widget with an L-text module that's too small: it still goes L -> M (level 5) before S.
3. Home Assistant with vDrop on: restart Home Assistant (or drop the network). The log has "vDrop: the connection to Home Assistant
   closed" and reconnects - and no "Unobserved task error … Home Assistant closed the WebSocket" stack trace a while later.
4. A 3D printer behind a proxy that's down (502 page): the log has one line "Printer Name: Moonraker answered 502 Bad Gateway",
   not the proxy's HTML page; the printer card shows the same text. A plain-text error from Moonraker is still shown as it is.

## 0.38.218 - vTrace: only issues
1. vTrace widget: the heading has "All · Issues" on the right (left of the refresh button while vTrace is closed). Click Issues: the
   heading says "Issues · N today", the chart and rows show only outages (going or over) and failed changes; outages still going stay
   at the top as before. Clicking the switch doesn't open vTrace or start a widget drag.
2. Restart vShell: the widget is still on Issues (saved). Settings > the widget's vTrace changes module: "Only issues" is ticked; untick
   it - the widget shows All again.
3. The bar's vTrace module: open its popup, click Issues - the popup redraws with only issues; close and reopen - still Issues. Its
   settings ("In its popup") have the same "Only issues" tick. The bar text itself is unchanged ("6 down" / "12 today").
4. With no issues in the list: "No issues - nothing went down or failed." shows under the heading.
5. (1.5.1) With an outage still going: on All there's no block at the top with its cause / "AI:" note - it's the first row of the list,
   red, "… went down · still down" (even if it's older than the rows shown); its tooltip still has the cause and the AI's note. On
   Issues the block at the top is back (title - since, cause · View, AI:).

## 0.38.218 - vDeploy plugin 1.2.0: ⬆ on containers
1. A stack with a newer image (⬆ on its row): unfold it (the arrow) in the widget and the popup - the container(s) running that image
   show a small ⬆ after their name; the others don't. Its tooltip adds "A newer image is out - Update on its stack pulls it".
2. Images written differently still match: `nginx` vs `docker.io/library/nginx:latest`, `jellyfin/jellyfin` vs
   `jellyfin/jellyfin:latest`, a registry with a port (`192.168.1.10:5000/app`) - only the same image and tag.
3. After Update on the stack (and vDeploy's next look) the ⬆ goes from the stack and its containers.

## 0.38.218 - vDeploy plugin 1.3.0: vDeploy's side panel look
1. vDeploy widget (Group by host on) and the bar's popup, side by side with vDeploy's side panel: each host is its server icon (accent),
   a dot (green / amber if a stack isn't healthy / red not answering), its name in bold and its stack count on the right ("offline" in
   red when it isn't answering).
2. Each stack: vDeploy's chevron (only with containers; click folds / unfolds as before, no drag in a widget), the layered stack icon,
   its dot, name, ⬆ / ✎, running / total (and Update in the popup).
3. Unfolded: each container indented under the stack icon - the cube icon, its dot, its name (dim), CPU · memory on the right; ⬆ as 1.2.0.
4. Bigger / smaller text (S / M / L or the bar font): the icons scale with it and stay sharp.

## 0.38.218 - vPatch plugin 1.0.0
1. Settings > Plugins > Add vPatch; its page: Address (http://<vPatch>:8470), App key (vPatch: Options > Sign-in > Apps > Make a key -
   vp_…), Test: "vPatch 0.1.3 - 48 updates on 5 hosts · 11 security …" (a wrong key: "vPatch didn't take the key …"; vDeploy's
   address: "That address answered with a page, not vPatch's API"). Send vShell's colours: vPatch's pages take your theme (vPatch:
   Options > Look says "Sent by vShell …"), and again when you change it.
2. A vPatch module on a bar: the dot (green all up to date, amber updates / restart, red a host not answering, accent while checking
   or upgrading), ⬆ 48, · 11 in the Secondary accent, the restart sign, ⚠ 1 for a host not answering; the tooltip sums it up.
3. Click it: the popup - Linux updates with updates / security / restart numbers; the groups in vPatch's order ("PROXMOX · FIRST"
   when a Proxmox node is in it), each host's dot, name, system, restart sign, security count and updates (✓ up to date, "not
   answering", "checking…", "upgrading…"). Upgrade… (n) opens vPatch's Upgrade page; Check now asks every host (each row turns
   "checking…", then its new numbers); Open vPatch. Click a host: its page in vPatch. Right-click: Check <host> now, Open in vPatch,
   Upgrade… (its page), Open shell on <host> (vTerm).
4. A widget: the same list (heading, Only hosts that need you, Grouped, Each host's system, How many - the module's settings).
5. Notifications: vPatch's Options > Notifications choose (vShell or Both); Settings > Notifications > vPatch: Show vPatch's
   notifications, and how each kind shows (problems and security as alerts). An upgrade that fails / a host that stops answering
   arrives within 30 s (5 with a module showing); clicking it opens the host in vPatch. Nothing old is replayed when vShell starts.
6. The command palette: Open vPatch, Check every host for updates, Upgrade hosts in vPatch… (when there are updates), Check <host> for
   updates for each host.
7. Automatic: none in vShell. Tried against a running vPatch 0.1.3 from a scratch program using the plugin's own client - the
   overview (5 hosts in 2 groups, the summary), events (kind, look, toVShell), Check one host ("checking…" on the next look), the
   colours accepted - and vShell's plugin API check on the DLL (34 vShell types, nothing outside the plugin API). Not tried: loaded
   in vShell itself (bar, popup, widget, notifications) - V's desktop.
## 0.38.219 - vTrace plugin 2.0.0: the vTrace server
1. Settings > Plugins > vTrace: vTrace server - Address (http://<the LXC>:8480), App key (vTrace: Options > Sign-in > Apps), Send this
   PC's changes (on), Send vShell's colours (on), Test: "vTrace 0.1.0 - n changes today". A wrong key: "vTrace didn't take the key …";
   nothing there: "Couldn't reach vTrace …". (Checked against a vTrace server on this PC with a test key - 2026-11-15.)
2. The bar module and widget show the server's changes (the last week) within 10 s: outages still going in red with the likely cause
   and "with n more on <node>", the All / Issues switch, today's chart, "Checked <time> · n min ago" from the server's last look.
3. The refresh button asks the server to look now (dimmed a few seconds, "Checking for changes…"); the tip says the server looks by
   itself. A click on a change / an outage / its cause opens <server>/change/<id> in the browser; Open vTrace opens its timeline.
4. This PC's changes: within a minute of setting it up (then every 15 minutes, and with the refresh button) the server gets the last 30
   days of Windows updates, installs, drivers, starts / shut-downs (432 on this PC); sending again adds nothing. In vTrace they're under
   "PCs" with the PC's name. Settings > Plugins > vTrace shows "This PC's changes sent <time> · n new".
5. Notifications: in vTrace, Options > Notifications > An outage starts or ends = vShell (or Both): an outage shows as an alert, a
   failed change / a source that can't be read as important, back up as normal (Settings > Notifications > vTrace sets the looks);
   older events aren't replayed when vShell starts. Show vTrace's notifications off: none.
6. No server set up: everything as before with the vTrace app (its file, the refresh that runs vTrace.exe --refresh, Check for changes).
## 0.38.220 - the Windows vTrace retired
1. Settings > Plugins > vTrace: only the vTrace server (address, key, this PC's changes, colours, Test) and a line with the install command -
   no "The vTrace app on this PC" / Check for changes.
2. No server set up: the vTrace modules in the gallery say "Set up your vTrace server in Settings > Plugins > vTrace"; a widget already
   placed says the same.
3. With a server: as 0.38.219 (changes, outages, refresh = the server looks, clicks open the browser, notifications, this PC's changes).
4. Settings > Updates > vSuite apps: vXplorer, vView, vTerm, vPad - no vTrace.
5. Settings > Plugins > UniFi: no "Account for vTrace's history" block; the rest as before.
## 0.38.221 - vTrace plugin 2.1.1
1. With vTrace 0.1.4 reading vPatch: the vTrace widget's / popup's rows from vPatch name their source vPatch (not "vpatch").

## 0.38.222 - vDeploy plugin 2.0.0: Proxmox nodes (needs vDeploy 0.1.25)
1. A vDeploy module (bar popup or widget) with vDeploy 0.1.25+ that has Proxmox nodes: "DOCKER HOSTS" over the hosts and stacks, "PROXMOX NODES"
   over each node (server icon in the accent, dot, name, how many LXCs / VMs or "offline") and its helper-script LXCs / VMs (box or screen
   icon, dot - green running, grey stopped, name, ⬆ when its app has an update; right: "1.34.3 → 1.35.0" in the accent, else CPU · memory,
   or "stopped"). The headings only show when there are both. Hover: kind, ID, node, state, the app check ("updates via apt / its own" …).
2. Popup: Update on an LXC with an update -> asks -> vDeploy's terminal page for it opens in the browser (a snapshot first, raised if its
   script asks). Click an LXC / VM: vDeploy's node page with it picked; click a node: its page.
3. Right-click an LXC / VM: Update to x… / Run its update… (LXCs), Reboot… / Shut down… (asks) or Start, Open console in the LXC (vTerm:
   SSH root@node, `pct enter <id>` - vTerm offers to save the connection), Open shell on <node>, Open in vDeploy. "asking…" while it
   works; a refusal (e.g. the node not answering) says why.
4. Right-click a node: Check for updates (asks vDeploy to look again and check its apps; "asking…" for a moment), Open shell on it, Add
   a LXC / VM… (vDeploy's page), Open in vDeploy.
5. Module settings: "Show the Proxmox nodes and their LXCs / VMs" (on) - off: only the stacks, and the bar's ⬆ / ⚠ leave the nodes out.
   "Only stacks that need you": only LXCs with an update and nodes not answering.
6. Bar: ⬆ counts stacks with newer images + LXC apps with an update; ⚠ includes nodes not answering; tooltip "… · 1 LXC app update".
7. !deploy: with no text also the LXCs with an update; !deploy vault - LXCs / VMs named like it (or their app or node). Enter: its
   actions (Update / Reboot / Shut down - Enter twice; Start; Console in; Open in vDeploy).
8. Palette: "Update Vaultwarden to 1.35.0", "Reboot pihole", "Shut down …", "Console in …", "Open in vDeploy" per LXC / VM.
9. Notifications: vDeploy's "Vaultwarden 1.35.0 is out" (app-update) shows as the "Newer images" kind.
10. An older vDeploy (no nodes in /overview): everything as 1.3.0.