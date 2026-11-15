# BinaryLane (plugin you add yourself)

Your [BinaryLane](https://www.binarylane.com.au) account in vShell: the credit and how many days it lasts on a bar, and notifications
when it runs low. Not part of vShell's release: `build.cmd` doesn't build `plugins-extra\`, and it isn't in plugins.json.

## Build and add

```
dotnet build plugins-extra\BinaryLane\vShell.BinaryLane.csproj -c Release
```

Then in vShell: **Settings > Plugins > Add from a file…** and pick
`plugins-extra\BinaryLane\bin\Release\net10.0-windows\vShell.BinaryLane.dll`. It only uses the plugin API, so it keeps loading in newer
vShells with the same plugin API.

Versions: **2.0.0** is built for .NET 10 (plugin API 2, vShell 0.38.135 and later). 1.0.0 was the .NET 8 build (plugin API 1, vShell
0.38.134 and older) - it no longer loads in vShell 0.38.135+, so remove it and add the 2.0.0 DLL instead.

## What it does

- **Connection** (Settings > Plugins > BinaryLane): the API token from mPanel (stored encrypted), Test connection, what you spend a month
  (0 = added up from your servers' sizes plus extra memory / disk / IPv4 / transfer - backups aren't counted), and whether charges not
  invoiced yet count as spent (on: credit left = available credit - unbilled total).
- **Notifications** (its Notifications tab):
  - Credit below an amount (AU$, default $20) and / or running out within some days (default 7). Each says once, and again only after
    the credit went back up and dropped again.
  - Billing: a failed payment (unpaid invoices), a server off for non-payment (status `archive`), the account not `active`.
  - Data transfer: a server past a percent of its included transfer (default 90%), once per transfer period.
  - Threshold alerts raised in mPanel (CPU, memory, disk …), once per time it's raised.
  - What's been said is kept in the settings (`Said`), so restarting vShell doesn't repeat it.
- **Module** "BinaryLane credit" (bar or widget): `$42.10 · 23 days` (or just one of them), amber within 1.5x of an alert, red past one.
  Hover for the details; click for a popup with them and Open mPanel.
- Polls api.binarylane.com.au every 15 minutes, every 2 while the module is on screen. Only GETs - it never changes anything.

## API calls

`/v2/customers/my/balance`, `/v2/account`, `/v2/servers`, `/v2/data_usages/current`, `/v2/customers/my/unpaid-payment-failed-invoices`,
`/v2/servers/threshold_alerts` (then `/v2/servers/{id}/threshold_alerts` for those listed).

## Testing

1. Add the plugin: Settings > Plugins > BinaryLane says "Not set up yet". Paste the token, Test connection: servers, credit, not invoiced
   yet, monthly spend and days left. Compare the credit with mPanel.
2. Add the BinaryLane credit module to a bar: `$… · … days`; hover for the details, click for the popup; Open mPanel opens the browser.
3. Set the amount alert above your credit: within a few seconds a "BinaryLane credit is low" notification (red text on the bar). Restart
   vShell: it isn't said again. Set the amount below your credit, then above again: it's said again.
4. Same with Days: set it above the days shown.
5. Monthly spend: enter one - the days left follow it, and the tooltip no longer says "not counting backups".
6. Untick "Count charges not invoiced yet": the credit shown goes up by the unbilled total.
7. Data transfer: set the percent to 50 with a server past that (or check the log) - one notification per server.
8. A wrong token: Test connection says BinaryLane didn't accept it; the module shows "BinaryLane n/a" with the reason when hovered.
9. Remove the plugin: the module says to add it in Settings, nothing is polled any more (log quiet).
