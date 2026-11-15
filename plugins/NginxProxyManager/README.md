# Nginx Proxy Manager

Your [Nginx Proxy Manager](https://nginxproxymanager.com) in vShell: its proxy hosts on a bar, notifications when a certificate is
running out (click to renew), a host has a config error, a backend is down or a new version is out, and `!npm` in the run box. Made
against NPM 2.16's API (`/api/schema`). Part of vShell's release since 0.38.132: `build.cmd` / `plugins.cmd` build it like the others,
and it's added from **Settings > Plugins**.

## Build

```
dotnet build plugins\NginxProxyManager\vShell.NginxProxyManager.csproj -c Release
```

## What it does

- **Connection**: admin address (http://host:81 - a trailing /api is fine), email and password (NPM has no API keys; stored
  encrypted), self-signed HTTPS, run box keyword, Test connection, **Enter 2FA code…**.
- **Signing in**: `POST /tokens`; with 2FA on, `requires_2fa` + a challenge token -> `POST /tokens/2fa` with the code. The session token
  is kept (encrypted, `Token` / `TokenExpires`) and refreshed with `GET /tokens` once it has under 12 hours left, so a code is only needed
  again when the session ran out (vShell off for a day). Until then: a "needs your 2FA code" notification (click it), the module says so.
  A 401 on a call signs in again once.
- **Notifications** (each said once - what's been said is kept in `Said`):
  - Certificate inside N days (default 14; only ones an enabled host uses, unless unticked). Let's Encrypt ones: click -> asks, then
    renews (`POST /nginx/certificates/{id}/renew`, can take a minute) and says how it went. Said again once it has expired.
  - Config error: an enabled host with `meta.nginx_online` false (with `nginx_err`). Said again if the host is changed and still fails.
  - Backend down / back up: vShell connects to each enabled host's forward host:port from this PC (3 s DNS, 4 s connect) - down after two
    failed checks in a row. Names this PC can't look up (Docker container names), localhost and 127.x are skipped (a ring instead of a dot).
  - New version (`/version/check`, every 6 hours).
- **Module** "Nginx Proxy Manager": `18 hosts · 1 problem` (red with a problem; optionally hidden while all's fine). Hover for the
  problems and the next certificate to renew. Click for the list: dot (red = a problem, green = backend answers, green ring = backend not
  checked from this PC, dim = off), domain, -> target and certificate days (or the problem); redrawn only when something in it changed; click a
  host to open it; right-click: open, open in NPM, copy address, edit, disable / enable (asks before disabling), renew certificate, get a
  certificate (hosts without one), delete (asks; when no other host uses its certificate, asks whether to delete that too).
- **Add a host** (the popup's "Add a host…", or `!npm add photos.example.com`): a form with domain names (commas for more), scheme,
  forward host / IP and port, SSL certificate (a new Let's Encrypt one, none, or an existing one - one that already covers the domain,
  such as a wildcard, is picked for you), Force SSL, HTTP/2, block common exploits, websockets, access list. Checked before sending:
  domain names, a domain already on a host, the port. With a new certificate the host is made first, then the certificate is asked for
  (`POST /nginx/certificates`, http challenge) and put on the host (`PUT`), so when Let's Encrypt fails the host still works over http -
  the notification says so, and right-click it > **Get a Let's Encrypt certificate** tries again.
- **Edit a host** (right-click > Edit…, or `!npm edit text`): the same form filled in from the host; saved with `PUT` of only the form's
  fields, so custom locations, advanced config and caching set in NPM stay as they are. Its own domains don't count as taken. A new
  Let's Encrypt certificate: the host keeps its old one until the new one is issued, then gets it.
- **Run box**: `!npm text` lists matching hosts (Enter opens). `!npm edit text`, `!npm disable text`, `!npm enable text`,
  `!npm renew text`, `!npm delete text` for those,
  `!npm add domain` for the form.
- Polls every 2 minutes, every 30 s while the module or run box shows it.

## Testing

1. Add the plugin, fill in address / email / password, Test connection: version, hosts, certificates.
2. With 2FA on the account: Test says to press Enter 2FA code; enter it - the module fills in. Restart vShell: no code asked.
3. Module on a bar: host count; click: the list with dots and certificate days; click a host: the site opens.
4. Right-click a host > Disable: asks, then it shows "off" (and is off in NPM). Enable it again.
5. Set "Days before it runs out" above a certificate's days left: one notification; click it: asks, renews, says renewed (days reset).
6. Stop a backend (a container on a LAN IP): after about two checks, "its backend is down"; start it: "is back up".
7. `!npm` in a run box: hosts; `!npm disable foo`: only enabled hosts matching foo; `!npm renew`: hosts with Let's Encrypt certificates.
8. Popup > Add a host…: fill in a domain that points at NPM, a LAN IP and port, New Let's Encrypt certificate. "Adding …", then
   "Added …" - it opens over https and shows in the list with its certificate days.
9. Add one with a domain that doesn't point at NPM: "Added … - but without a certificate"; it works over http. Right-click it > Get a
   Let's Encrypt certificate once the DNS is right.
10. The form with a domain already on a host, a port of 0, or http:// in the forward host: says what's wrong and offers to change it
    (the values stay filled in).
11. `!npm add test.example.com`: the form opens with it filled in; a domain under a wildcard certificate you have picks that certificate.
12. Right-click a test host > Delete…: asks (No leaves it); Yes with a certificate only it uses: asks about the certificate too. It goes
    from the list (and from NPM, and its certificate from SSL Certificates if you said yes). `!npm delete test`: the same from the run box.
13. Right-click a host > Edit…: the form shows its domains, scheme, host, port, certificate, the four ticks and its access list. Change
    the port and save: the list shows the new target, and NPM has it (with its advanced config / custom locations untouched). Add a second
    domain and pick New Let's Encrypt certificate: "Saving…", then "Saved … with a new certificate". Enter another host's domain: says
    it's taken. `!npm edit foo` opens the same form.
14. Wrong password: Test says it wasn't accepted; the module shows "NPM n/a" with the reason when hovered.
