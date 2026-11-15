# vShell.ClaudeUsage 2.0.0

A vShell plugin that shows your Claude plan's usage limits (the 5-hour session, the week, and per-model weeks when your plan has them) on a bar or widget. It also warns you before you hit one.

Built against vShell 0.38.135, plugin API 2 (.NET 10).

Versions: **2.0.0** is built for .NET 10 (plugin API 2, vShell 0.38.135 and later). 1.0.x were the .NET 8 builds (plugin API 1, vShell 0.38.134 and older) - they no longer load in vShell 0.38.135+, so remove the old one and add the 2.0.0 DLL instead.

## Install

Settings > Plugins > **Add from a file…** > `vShell.ClaudeUsage.dll`, then add the **Claude usage** module (gallery group *Info*).

## What it needs

You need Claude Code signed in on the PC with your Pro or Max account. Run `claude`, then `/login` once. The plugin reads the sign-in Claude Code keeps in `%USERPROFILE%\.claude\.credentials.json` (or `CLAUDE_CONFIG_DIR`), then asks Anthropic for the same numbers Claude Code's `/usage` shows.

- **Claude Code in WSL:** go to Settings > Plugins > Claude usage > Connection and click **Find it**, which also looks in your WSL distros. You can also type a path such as `\\wsl.localhost\Ubuntu\home\you\.claude`.
- **Renewing the sign-in:** Claude Code's sign-in only lasts a few hours. With *Renew the sign-in when it runs out* on (the default), vShell renews it the way Claude Code does and saves the new pair back to Claude Code's file. It only saves if Claude Code hasn't changed the file in the meantime, so the module keeps working when Claude Code isn't open. Turn it off and the module waits for Claude Code to renew the sign-in itself.
- **The endpoint is unofficial:** it's the private endpoint Claude Code uses (`api.anthropic.com/api/oauth/usage`), not a documented API, so Anthropic can change it. The plugin reads both response formats seen so far. Your sign-in is only ever sent to `api.anthropic.com`.

## The module

- **Tiles:** one per limit, e.g. `5h 42%` and `Wk 84%`. The dot is green, turns amber from 80% (adjustable), and red when a limit is reached. Each tile has an optional meter and an optional reset time (`· 3:15 PM`, or `· Fri`).
- **Hover:** shows the percent used, when the limit resets and how long until then, and whether it's the limit you're on now.
- **Click:** opens a card with every limit, its meter and its reset time, plus the plan (e.g. Max 20x) and Refresh / Usage page buttons.
- **After a reset:** once a limit's reset time passes it shows 0% straight away, and the next check fills in the new window.
- **Module settings:** which limits to show (5-hour, weekly, per-model, extra usage), plus text in front (shown before the first dot), names, meters (bar, or a ring like vShell's drive ring with the percent inside it; either with a size), reset time and the amber threshold.

## Notifications

On the plugin's Notifications tab:

- A limit is getting close (default 80%).
- A limit is reached.
- A limit you reached has reset.
- Optionally, the same for the per-model weekly limits.

Each one is said once per window, and that's remembered across restarts. You can choose the level each notification shows as.

## Command palette

- **Claude usage:** shows the current numbers and opens claude.ai/settings/usage.
- **Refresh Claude usage**

## How often it checks

Every 3 minutes while the module is on screen, otherwise every 6 minutes (to keep the notifications going), and right away when you open the card or the palette. Manual refreshes are limited to once every 20 seconds. If Anthropic asks it to slow down (HTTP 429), it backs off for as long as Anthropic says.

## Build

Run `build.cmd`, or `dotnet build -c Release`. You need the .NET 10 SDK. The DLL ends up in `bin\Release\net10.0-windows\`. `sdk\vShell.dll` is referenced with `Private=false`, so it isn't copied next to the plugin.
