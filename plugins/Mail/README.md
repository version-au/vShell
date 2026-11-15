# Mail for vShell

A vShell plugin that checks mail accounts over IMAP - any server that takes a user name and password (Gmail with an app password,
Fastmail, iCloud, Yahoo, a mail server of your own). Made only with vShell's plugin API, with a small IMAP client of its own
(no packages).

- **Mail** (bar or widget): an envelope with the unread count (every account's together, or one account's). Click it for the
  messages in a popup: each account with its unread count, then its unread messages (the newest 100) - who from, the subject and
  when - a set number at once, scrolling for the rest. Each has Mark as read and Delete (to the server's Trash folder when it has
  one and offers MOVE, else flagged \Deleted and expunged). A click on a message or an account opens its webmail. It can hide
  itself while there's nothing unread.
- **Mail messages** (widget): the same list, in the widget.
- **Notifications**: new messages (one notification per check listing them - "Sam - Tomorrow's meeting", then "and 3 more");
  an account can't be checked (after a wait you set), and when it can again.

## Setting it up

1. Settings > Plugins > Mail > **+ Add account**.
   - **Gmail**: Sign in with = Google, then **Sign in with Google**: the browser opens on Google's sign-in, you allow vShell, and the
     address, server and port are filled in (Google may say the app isn't verified - Advanced > Go to vShell). vShell keeps a refresh
     token (encrypted) and logs in to IMAP with XOAUTH2. Your own OAuth client can be used instead (bottom of the Mail section).
   - **Anything else**: the IMAP server (`imap.fastmail.com`), SSL/TLS on port 993 (or STARTTLS on 143), the user name (usually the
     address) and the password, then **Test connection**. Yahoo, iCloud (and Gmail, if you'd rather): make an *app password* in the
     account's security settings (2-step verification on) and use that.
   - Outlook.com no longer takes a password over IMAP (it wants its own OAuth) - not supported yet.
2. **Folder**: INBOX, or **Pick a folder…** to choose another from the server's list.
3. **Open in the browser**: your webmail address - a click on the module, a message or a notification opens it.
4. Add the Mail module to a bar or widget, or Mail messages to a widget. Notifications: Settings > Notifications > Mail.

Each check logs in, opens the folder read-only (EXAMINE), counts the unread messages (UID SEARCH UNSEEN), reads the newest ones'
From / Subject / Date headers (UID FETCH, BODY.PEEK so nothing is marked read), and logs out. New messages are the unread ones with
a UID at or past the UIDNEXT seen at the last check (kept with the account in the settings), so a restart doesn't repeat them and the
first check of a folder only learns where it's up to. Passwords are stored encrypted for your Windows account.

## Files

- `MailPlugin.cs` - the manifest (`[assembly: VShellPlugin]`), the modules, the settings and notification sections.
- `Mail.cs` - the accounts and settings, the checks (a `Poller`), what's new for notifications.
- `Imap.cs` - the IMAP client (TcpClient + SslStream, literals, LOGIN / AUTHENTICATE XOAUTH2 / EXAMINE / UID SEARCH / UID FETCH / LIST,
  STORE / MOVE / EXPUNGE) and the text bits: RFC 2047 encoded words, header dates, modified UTF-7 folder names.
- `GoogleAuth.cs` - Sign in with Google: the browser flow with PKCE and a loopback listener, the token exchange and refresh. The
  OAuth client it comes with is read at build time from `google-client.txt` next to the project (`clientid secret` on one line;
  not in the repository) - a build without the file has none, and people put their own on the Plugins page.
- `Modules.cs` - the bar / widget module with its popup, and the widget list.
