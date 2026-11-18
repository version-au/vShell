# Changelog

## 2.1.0 (2026-11-18)
- Meter style Dots: a row of dots filling up as the limit is used (needs vShell 0.38.227 - an older vShell draws the bar).

## 2.0.2 (2026-11-01)
- Author shown as "version" (was "V"). No other changes.

## 2.0.1 (2026-11-01)
- Fix: the reset check (every 5 seconds) now does its work on the UI thread, like the other checks. Before, the first save of its "already told you" marks could happen on vShell's watcher thread and leave vShell's settings timer stuck, so nothing saved until a restart; a mark could also be lost when a limit was reached and reset at the same moment (a repeated notification).
- Fix: the first check waits for the settings, so a custom Claude Code folder (WSL) isn't tried with the default folder first at every start.
- The module only redraws when something shown changed (a new answer, a window ending, the minute) instead of every second.
- Source now lives in vShell's repo (plugins\ClaudeUsage) and builds with vShell.

## 2.0.0 (2026-10-31)
- Built for .NET 10 and vShell plugin API 2 (vShell 0.38.135 and later), against the vShell branch-4 SDK. No feature changes.
- 1.0.x (.NET 8, plugin API 1) no longer loads in vShell 0.38.135+; keep it only for vShell 0.38.134 and older.

## 1.0.3 (2026-10-29)
- Ring style now looks like vShell's drive ring: the ring comes first, sized to the bar, with the percent inside it and the name beside it. There's no dot, since the ring itself turns amber or red.

## 1.0.2 (2026-10-29)
- Meter size setting (50-300%) for the bar or the ring.
- Text in front now sits before the first status dot, not after it.

## 1.0.1 (2026-10-29)
- Text in front of the module (vShell's usual Title setting), shown before the first tile.
- Meter style: the bar, or a ring.

## 1.0.0 (2026-10-29)
- First version: Claude usage module (tiles per limit, card with all limits, settings for which limits and how they look).
- Reads Claude Code's sign-in on this PC, with a custom folder and a Find button that also looks in WSL.
- Renews the sign-in when it has run out and saves it back to Claude Code's file only if the file is unchanged (can be turned off).
- Reads both usage formats: `limits: [...]` (2026-10) and the older `five_hour` / `seven_day`.
- Notifications: getting close, reached, reset (once per window, remembered across restarts).
- Command palette: Claude usage, Refresh Claude usage.
- Backs off on HTTP 429; manual refresh at most every 20 seconds.
