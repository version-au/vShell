# PeaNUT for vShell

A vShell plugin (plugin API 2) that reads your UPSes from **PeaNUT** - the web dashboard for Network UPS Tools - from as many
PeaNUT servers as you like.

Versions: **2.0.0** is built for .NET 10 (plugin API 2, vShell 0.38.135 and later). 1.0.x were the .NET 8 builds (plugin API 1,
vShell 0.38.134 and older) - they no longer load in vShell 0.38.135+, so remove the old one and add the 2.0.0 DLL instead.

## Add it

- **Ready-made:** `dist\vShell.PeaNUT.dll` → vShell **Settings > Plugins > Add from a file…**
- **Build it yourself:** run `build.cmd` (needs the .NET 10 SDK). The DLL lands in `dist\`.

## Set it up

**Settings > Plugins > PeaNUT > PeaNUT servers…** → **Add…** for each PeaNUT:

| Field | What to put |
|---|---|
| Name | Shown on the module, e.g. *Office* or *Rack* (empty = host name) |
| Address | PeaNUT's web address, e.g. `http://192.168.1.20:8080`. With `BASE_PATH` set, include it: `https://example.com/peanut` |
| Username / Password | Your PeaNUT login (or `WEB_USERNAME` / `WEB_PASSWORD`). Leave empty if PeaNUT runs with `AUTH_DISABLED=true` |
| Self-signed HTTPS | Tick if PeaNUT uses its own certificate (`SSL_CERT_PATH`) |
| Check this server | Untick to pause a server without deleting it |

The password is saved encrypted (Windows DPAPI, like vShell's own secrets). **Test** in the list, or **Test connection** on the page,
says how many UPSes each server reports or exactly what went wrong (wrong password, missing base path, nothing listening, …).

One PeaNUT can itself watch several NUT servers - every UPS on every one shows up.

**Rename UPSes…** on the same page lists every UPS PeaNUT reports. Give one a name of your own and it's used everywhere -
the module, the popup, notifications and the palette. Leave it empty (or remove it) for the automatic name.

## The UPS (PeaNUT) module

Add it to a bar or a widget (gallery group *Servers & network*). One tile per UPS:

- **Dot:** green = on mains, yellow = on battery / replace battery / bypass, red = battery low, overloaded, off, NUT can't reach
  it or PeaNUT can't be reached (then it keeps the last reading), grey = nothing read yet.
- **Show:** Charge, Charge and runtime, Runtime, Load, Power (W) or Status.
- **UPS:** every UPS, or just one (add a module per UPS for separate ones).
- Names on/off, Row or List layout, an optional battery meter with its own colour.
- Hover for the readings; **click** for a popup with everything - battery, runtime, load, power, input/output and battery
  voltage, temperature, model - grouped by server, with **Open PeaNUT ›**.

## Notifications

**Settings > Notifications > PeaNUT notifications:**

- a UPS goes **on battery**, and when **mains power is back**
- **battery low** - the UPS's own LB flag, or (on battery) a charge you choose
- **replace battery** (the UPS's RB flag) - once per UPS until it clears
- a PeaNUT **server can't be reached** (after two failed checks, once it has answered since vShell started), and when it's back

The *wait before saying it's down* applies to going on battery and to unreachable servers, so a short power blip says nothing.
Each kind has its own level (Normal / Important / Alert).

## Command palette

Each UPS shows as *"Office UPS - On battery"* with its charge and runtime (opens PeaNUT), plus **Open PeaNUT <server>**.

## How it talks to PeaNUT

`GET <address>/api/v1/devices?meta=true` with HTTP Basic auth - one request per server per check (5 s while a UPS module is on
screen, 15 s otherwise; both adjustable). Read-only: it never sends commands to a UPS.

## Files

```
vShell.PeaNUT.csproj   build.cmd   CHANGELOG.md
sdk\vShell.dll, plugin-api.txt, plugins.md   (vShell 0.38.135, plugin API 2)
src\Plugin.cs          manifest, Plugins page, notifications, palette
src\Store.cs           background polling of every server
src\UpsModule.cs       the module (a TilesModule)
src\UpsPopup.cs        the click-for-details popup
src\Core\Model.cs      servers, UPS readings, NUT status flags, formatting
src\Core\PeanutParse.cs  PeaNUT JSON, auth header, error wording, UPS names
```
