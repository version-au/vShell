# Changelog - PeaNUT for vShell

## 2.0.2
- Author shown as "version" (was "V"). No other changes.

## 2.0.1
- With no UPS to show, the module no longer rebuilds its message every second - only when PeaNUT answers or the settings change.
- Source now lives in vShell's repo (plugins\PeaNUT) and builds with vShell.

## 2.0.0
- Built for .NET 10 and vShell plugin API 2 (vShell 0.38.135 and later). 1.0.x (.NET 8, plugin API 1) no longer loads in
  vShell 0.38.135+; keep it only for vShell 0.38.134 and older.
- Rename UPSes: a list on the PeaNUT plugin page to give any detected UPS your own name, used on the module, in the popup,
  notifications and the command palette.
- Fix: the module's "Text in front" setting now shows, in front of the first UPS.

## 1.0.1
- Fix: 401 through a reverse proxy (e.g. Nginx Proxy Manager with Force SSL). The login is now kept when the proxy redirects
  http to https or to another port on the same host (still never sent to another host, or from https down to http).
- A 401 now says why: a login asked for by the proxy itself (NPM Access List), a ':' or non-ASCII character PeaNUT's API
  can't read, or PeaNUT failing to check a setup-page login behind a proxy (fix: WEB_USERNAME / WEB_PASSWORD on the container).
- Failed checks are written to vShell's log.

## 1.0.0
- First version, built against vShell plugin API 1 (vShell 0.38.86).
- Plugins page section: add any number of PeaNUT servers (name, address with optional base path, username, password kept
  encrypted, self-signed HTTPS, on/off), test them, and set how often they're checked.
- UPS (PeaNUT) module for bars and widgets: one tile per UPS with a status dot and its charge, charge and runtime, runtime,
  load, power or status; optional battery meter; Row / List layout; every UPS or just one. Click for a popup with all the
  readings, grouped by server, with a link to open PeaNUT.
- Notifications: on battery (and mains back), battery low (the UPS's own flag or a charge you pick), replace battery,
  PeaNUT server can't be reached (and back) - with vShell's "wait before saying it's down" and a level for each.
- Command palette: each UPS's status, and "Open PeaNUT" for each server.
