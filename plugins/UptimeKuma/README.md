# Uptime Kuma for vShell

A vShell plugin made only with vShell's plugin API: it compiles against `sdk\vShell.dll` (from a vShell build's `dist\sdk`), not
vShell's source.

Versions: **2.0.1** (author shown as "version", nothing else changed) and **2.0.0** are built for .NET 10 (plugin API 2, vShell 0.38.135 and later). 1.0.0 was the .NET 8 build (plugin API 1, vShell
0.38.134 and older) - it no longer loads in vShell 0.38.135+, so remove it and add the 2.0.0 DLL instead.

- **Uptime Kuma** (bar): "All up", or "2 down" / "1 pending" in the warning colours. Click it for the list in a popup. It can hide
  itself while everything is up.
- **Uptime Kuma monitors** (widget): each monitor with a status dot, its response time and how many days its certificate has left
  (in the warning colour under 14 days). Show all, or only the ones that aren't up. Click one to open Uptime Kuma.
- **Notifications**: a monitor goes down (after a wait you set, so a blip doesn't notify) and when it's back up; Uptime Kuma itself
  can't be reached, and when it can again.

## Setting it up

1. In Uptime Kuma (1.21 or later): Settings > API Keys > Add API Key. Copy the key.
2. vShell: Settings > Plugins > **Add from a file…** > `vShell.UptimeKuma.dll`.
3. Its section on the Plugins page: the address (`http://192.168.1.20:3001`) and the API key, then Test connection.
4. Add the module to a bar, or "Uptime Kuma monitors" to a widget. Notifications: Settings > Notifications > Uptime Kuma.

vShell reads `<address>/metrics` with the key (basic auth, the key as the password) - every monitor's status, response time and
certificate days. The key is stored encrypted for your Windows account, and only ever sent to that server.

## Building it

`build.cmd` (needs the .NET 10 SDK) -> `out\vShell.UptimeKuma.dll`. For a new plugin API version, copy the new vShell's `dist\sdk\*`
into `sdk\` and build again. `sdk\plugin-api.txt` lists what the API has; `sdk\plugins.md` explains it.

## Files

- `Plugin.cs` - the manifest (`[assembly: VShellPlugin]`), the modules, the settings and notification sections.
- `Kuma.cs` - the connection: settings, reading /metrics (a `Poller`, `PluginHttp`), and what's changed for notifications.
- `Modules.cs` - the bar module, the widget list, and the popup.
