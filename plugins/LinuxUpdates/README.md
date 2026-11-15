# Linux Update Dashboard

Your [Linux Update Dashboard](https://github.com/TheDuffman85/linux-update-dashboard) in vShell: how many updates each Linux server
has (and how many are security updates), which need a reboot, Check now / Upgrade / Full upgrade / Remove unused packages / Reboot
from vShell, a live output window, notifications, palette commands and `!lu` in the run box. Made against the dashboard's API
(`API.md` and its server code, main branch, October 2026). Part of vShell's release from 0.38.132: `build.cmd` / `plugins.cmd` build it
like the others, and it's added from **Settings > Plugins**.

## Build

```
dotnet build plugins\LinuxUpdates\vShell.LinuxUpdates.csproj -c Release
```

## What it does

- **Connection**: the dashboard's address (http://host:3001 - a trailing /api is fine), an API token (`ludash_…`, made in the
  dashboard's Settings > API tokens; stored encrypted), self-signed HTTPS, "Allow upgrades, full upgrades, removing unused packages and
  reboots from vShell" (off by default - then only Check now), "Open the live output when vShell starts an upgrade", run box keyword,
  Test connection (version, systems, updates, and whether the token is read-only).
- **API**: `Authorization: Bearer ludash_…`. `GET /api/systems?scope=visible` (`updateCount`, `securityCount`, `keptBackCount`,
  `needsReboot`, `isReachable`, `lastCheck`, `activeOperation`, `osLifecycleStatus` …), `GET /api/app-update` every 6 hours. Actions:
  `POST /api/systems/{id}/check | upgrade | full-upgrade | autoremove` (each gives a `jobId`, followed with `GET /api/jobs/{id}` every 2 s
  until it's done - the job's `result.status` says how it went), `/reboot` (straight away), `/cancel`, `POST /api/systems/check-all`,
  `POST /api/systems/upgrade-all` (`{"items":[{"systemId":…}]}`).
- **Read-only tokens**: found out once per connection with `POST /api/systems/0/cancel` (there's no system 0, so it does nothing): a
  read-only token gets "This token is read-only". Then the popup says so and offers no actions.
- **Module** "Linux Update Dashboard": `23 updates · 3 security · 1 reboot` (yellow with updates or a reboot; red with security updates or
  a system that can't be reached / checked; "Up to date" otherwise, optionally hidden then). Hover for each system that needs something.
  Click for the list: dot (accent = running, red, yellow, green = up to date, dim = never checked), name and OS, updates / kept back /
  reboot / package issues / end of support / when it was checked (or what's running, or the problem). Click a system: Check now (or
  Cancel), Upgrade N packages…, Full upgrade… (where the dashboard supports it), Remove unused packages…, Reboot…, Show the output, Open
  shell (SSH - a saved connection by name, else its host name and user from the dashboard), Open the dashboard, Copy host name. "The list
  shows: Only ones that need something" leaves out the systems that are up to date. Under the list: Check all, Upgrade all (N)… (the
  systems with updates that aren't left out of Upgrade all on the dashboard), Open Linux Update Dashboard. Redrawn only when something in
  it changed.
- **Live output** (`/api/ws/systems/{id}/output`, a WebSocket with the same token): a window per system with what the dashboard still
  has of the last command, then each line as it comes (the command in the accent colour, stderr yellow, errors red, colour codes taken
  out, up to 5000 lines). Check now / Upgrade / Cancel, Keep to the bottom, Copy, Clear, Open dashboard. Connects again every 5 s if
  the connection drops.
- **Notifications** (said once - what's been said is kept in `Said`; the first look only fills it in): new updates for a system (its
  count went up), new security updates, needs a reboot, distro near / past the end of support, a new dashboard version, a system or the
  dashboard can't be reached or checked (with the usual delay; and when it works again), and an upgrade vShell started finishing (a
  failed one always). Click one for that system's live output.
- **Palette / run box**: per system Check … for updates, Upgrade … (N packages), Full upgrade of …, Reboot …, Cancel … (while busy),
  Show the output of …, Open shell on …; Check all systems, Upgrade all, Open Linux Update Dashboard. `!lu web` = those for "web".
- Polls every 2 minutes, every 20 s while the module, the palette or an output window shows it, every 5 s while something's running.
