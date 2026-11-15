using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("mail", "Mail",
    Description = "Your mail accounts over IMAP: a module with the unread count (the messages in a popup), a widget with the list, and a notification listing the new messages.",
    Glyph = "", Group = "Internet", Author = "version", Adds = "notifications", Types = "Mail,MailList")]

namespace Mail
{
    /// <summary>
    /// The Mail plugin - made only with vShell's plugin API: it checks IMAP folders for unread messages (any server that takes a user name and
    /// password - Gmail with an app password, Fastmail, a mail server of your own).
    /// </summary>
    public sealed class MailPlugin : IPlugin
    {
        Action<string, string, string, string> _notice;

        static string Needs(BarSettings s) { return Mail.Configured(s) ? null : "Needs a mail account (Plugins)"; }

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = "Mail", Display = "Mail", ShortName = "Mail", Glyph = "", Group = "Smart home & more",
                Blurb = "unread count, the messages in a popup", HasPopup = true, Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new MailBarModule(mgr, h, cfg); },
                Settings = BarSettingsRows, BarPicture = BarPic, WidgetPicture = BarPic
            });
            host.AddModule(new PluginModule
            {
                Type = "MailList", Display = "Mail messages", ShortName = "Messages", Glyph = "", Group = "Smart home & more",
                Blurb = "the unread messages by account", OnBar = false, Needs = Needs, WidgetSize = new Size(320, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new MailListModule(mgr, h, cfg); },
                Settings = ListSettingsRows, WidgetPicture = WidgetPic
            });

            host.SetUpWhen(Mail.Configured);
            host.AddIntegration("Mail", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(Mail.Configure);

            host.AddNotifyKind("Mail.New", NotifyLevel.Normal);
            host.AddNotifyKind("Mail.Problem", NotifyLevel.Alert);
            host.AddNotifySource("Mail");
            // the checks run on a worker thread: notifications go out on vShell's own
            _notice = delegate (string title, string detail, string url, string kind)
            {
                Application app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(delegate { Notify.Post("Mail", title, detail, url, Notify.KindOf(kind)); }));
            };
            Mail.Notice += _notice;
            BarManager mgr0 = host.Manager;
            Mail.Save = delegate { mgr0.SaveSoon(); };
        }

        public void Unload()
        {
            if (_notice != null) Mail.Notice -= _notice;
            Mail.Save = null;
            Mail.Stop();
        }

        // ---- Settings > Plugins ----
        /// <summary>The accounts: one card each, Add account, and how often to check.</summary>
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            MailSettings m = MailSettings.Of(p.Settings);
            List<MailAccount> accounts = m.Accounts;
            foreach (MailAccount a0 in new List<MailAccount>(accounts))
            {
                MailAccount a = a0;
                p.Header("Mail: " + a.Shown);
                p.Text("Name (as shown)", delegate { return a.Name; }, delegate (string v) { a.Name = v.Trim(); });
                p.Combo("Sign in with", MailAccount.Auths, delegate { return a.Auth; }, delegate (string v)
                {
                    a.Auth = v;
                    if (a.Google && a.Host.Length == 0) { a.Host = "imap.gmail.com"; a.Port = 993; a.Security = "SSL/TLS"; }
                    if (a.Google && a.WebUrl.Length == 0) a.WebUrl = "https://mail.google.com";
                    p.Changed();
                    p.Redraw();
                });
                if (a.Google)
                {
                    bool signedIn = a.RefreshToken.Length > 0;
                    p.Button(signedIn ? "Signed in as " + (a.User.Length > 0 ? a.User : "a Google account") : "Not signed in yet", signedIn ? "Sign in again" : "Sign in with Google",
                             delegate { SignIn(p, a); });
                    if (signedIn) p.Button("Forget the Google sign-in (the account stays)", "Sign out", delegate { a.RefreshToken = ""; GoogleAuth.Forget(a.Id); p.Changed(); p.Redraw(); });
                    p.Note("Your browser opens on Google's sign-in: pick the Gmail account and allow vShell to read and change your mail. Google may say the app isn't " +
                           "verified - Advanced > Go to vShell gets past it. vShell keeps a sign-in token (encrypted for your Windows account) and fills in the rest below.");
                }
                p.Text("IMAP server (imap.gmail.com, imap.fastmail.com, mail.example.com)", delegate { return a.Host; }, delegate (string v) { a.Host = v.Trim(); });
                p.Combo("Connection", MailAccount.Securities, delegate { return a.Security; }, delegate (string v)
                {
                    bool wasSsl = a.Ssl;
                    a.Security = v;
                    // the usual port for each, unless one was typed
                    if (a.Ssl && !wasSsl && a.Port == 143) a.Port = 993;
                    if (!a.Ssl && wasSsl && a.Port == 993) a.Port = 143;
                    p.Changed();
                    p.Redraw();
                });
                p.Number("Port", delegate { return a.Port; }, delegate (double v) { a.Port = (int)Math.Round(v); });
                p.Text(a.Google ? "Address (filled in by the sign-in)" : "User name (usually the address)", delegate { return a.User; }, delegate (string v) { a.User = v.Trim(); });
                PasswordBox pw = a.Google ? null : p.Secret("Password", "password", delegate { return a.Password; }, delegate (string v) { a.Password = v; },
                    "Yahoo, iCloud and most others want an app password here (made in the account's security settings, with 2-step verification " +
                    "on), not the one you sign in with. Gmail: an app password works too, or choose Sign in with Google above. Outlook.com no longer takes a password " +
                    "over IMAP at all. It's stored encrypted for your Windows account and only ever sent to this server.");
                p.Text("Folder", delegate { return a.Folder; }, delegate (string v) { a.Folder = v.Trim().Length == 0 ? "INBOX" : v.Trim(); });
                p.Button("The folder watched for new messages", "Pick a folder…", delegate { PickFolder(p, a, pw); });
                p.Text("Open in the browser (your webmail - https://mail.google.com; empty = nothing)", delegate { return a.WebUrl; }, delegate (string v) { a.WebUrl = v.Trim(); });
                p.Check("Accept a self-signed certificate", delegate { return a.IgnoreCert; }, delegate (bool v) { a.IgnoreCert = v; });
                p.Check("Use this account", delegate { return a.Enabled; }, delegate (bool v) { a.Enabled = v; });
                p.Test(delegate { Typed(a, pw); p.Changed(); }, delegate { return Mail.TestAsync(a); });
                p.Button("Remove this account", "Remove", delegate
                {
                    if (Questions.YesNo(p.Window, "Remove " + a.Shown + " from vShell?", "Mail") != MessageBoxResult.Yes) return;
                    accounts.Remove(a);
                    p.Changed();
                    p.Redraw();
                });
            }
            p.Header(accounts.Count == 0 ? "Mail (IMAP)" : "More accounts");
            p.Button(accounts.Count == 0 ? "A mail account on any IMAP server (Gmail with Sign in with Google, or a password)" : "Another account", "+ Add account", delegate
            {
                var a = new MailAccount();
                a.Normalize();
                accounts.Add(a);
                p.Changed();
                p.Redraw();
            }, true);
            p.Number("Check every (seconds)", delegate { return m.Seconds; }, delegate (double v) { m.Seconds = (int)Math.Round(v); p.Changed(); });
            p.Note("Each check logs in, counts the unread messages in the folder and reads the newest ones' sender and subject, then logs out - nothing stays " +
                   "connected. With notifications off and no module showing it, nothing is checked at all.");
            p.Header("Google sign-in");
            if (!GoogleAuth.HasBuiltIn) p.Note("This build of the plugin has no Google OAuth client of its own: Sign in with Google needs yours below.");
            p.Text(GoogleAuth.HasBuiltIn ? "Your own OAuth client ID (optional - empty = vShell's own)" : "Your OAuth client ID", delegate { return m.GoogleClientId; }, delegate (string v) { m.GoogleClientId = v; },
                   "Sign in with Google uses an OAuth client vShell comes with. If you'd rather use one of your own (Google Cloud console > APIs & Services > Credentials " +
                   "> OAuth client ID, type Desktop app, with the https://mail.google.com/ scope on its consent screen), put its ID and secret here and sign in again.");
            p.Secret("Its client secret", "client secret", delegate { return m.GoogleClientSecretStored; }, delegate (string v) { m.GoogleClientSecretStored = v; });
        }

        /// <summary>A password typed but not yet saved (the box still has it): keep it, so Test and Pick a folder use it.</summary>
        static void Typed(MailAccount a, PasswordBox pw)
        {
            if (pw != null && pw.Password.Trim().Length > 0) { a.Password = Secret.Protect(pw.Password.Trim()); pw.Password = ""; }
        }

        static async void SignIn(SettingsWindow.SettingsPage p, MailAccount a)
        {
            GoogleSignIn g;
            try { g = await GoogleAuth.SignInAsync(MailSettings.Of(p.Settings)); }
            catch (Exception ex) { Questions.Tell(p.Window, ex.Message, "Mail - Sign in with Google"); return; }
            a.RefreshToken = Secret.Protect(g.RefreshToken);
            a.User = g.Email;
            if (a.Name.Length == 0) a.Name = g.Email;
            if (a.Host.Length == 0 || a.Host.EndsWith("gmail.com", StringComparison.OrdinalIgnoreCase)) { a.Host = "imap.gmail.com"; a.Port = 993; a.Security = "SSL/TLS"; }
            if (a.WebUrl.Length == 0) a.WebUrl = "https://mail.google.com";
            GoogleAuth.Forget(a.Id);
            p.Changed();
            p.Redraw();
        }

        static async void PickFolder(SettingsWindow.SettingsPage p, MailAccount a, PasswordBox pw)
        {
            Typed(a, pw);
            if (!a.Ready)
            {
                Questions.Tell(p.Window, a.Google ? "Sign in with Google first." : "Fill in the server, the user name and the password first.", "Mail");
                return;
            }
            List<ImapFolder> folders;
            try { folders = await Mail.FoldersAsync(a); }
            catch (Exception ex) { Questions.Tell(p.Window, ex.Message, "Mail"); return; }
            var names = new List<string>();
            foreach (ImapFolder f in folders) if (!f.NoSelect) names.Add(f.Shown);
            if (names.Count == 0) { Questions.Tell(p.Window, "The server didn't list any folders.", "Mail"); return; }
            string current = names.Contains(a.Folder) ? a.Folder : names[0];
            var fields = new List<FormField> { FormField.Combo("Folder", current, names.ToArray()) };
            if (!FormWindow.Ask(p.Window, "Pick the folder to watch - " + a.Shown, fields)) return;
            string chosen = fields[0].Value.Trim();
            if (chosen.Length == 0) return;
            a.Folder = chosen;
            p.Changed();
            p.Redraw();
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            MailSettings m = MailSettings.Of(p.Settings);
            p.NotifySection("Mail notifications", m.Configured, "a mail account");
            p.Check("New messages (one notification per check, listing them)", delegate { return m.NotifyNew; }, delegate (bool v) { m.NotifyNew = v; p.Changed(); });
            p.Number("Messages listed in one notification (then \"and N more\")", delegate { return m.NotifyLines; }, delegate (double v) { m.NotifyLines = (int)Math.Round(v); p.Changed(); });
            p.Kind("New messages", "Mail.New");
            p.Check("An account can't be checked (and when it can again)", delegate { return m.NotifyServer; }, delegate (bool v) { m.NotifyServer = v; p.Changed(); });
            p.Delay(delegate { return m.NotifyDelay; }, delegate (int v) { m.NotifyDelay = v; p.Changed(); });
            p.Kind("Can't be checked", "Mail.Problem");
            p.Note("Only messages that are still unread when vShell checks are listed - one you've already opened on your phone isn't. What's in the folder " +
                   "when an account is added (or vShell starts for the first time with it) doesn't give a notification, only what comes after.");
        }

        // ---- module settings ----
        static void AccountRow(SettingsWindow.SettingsPage p, MailView v)
        {
            List<MailAccount> accounts = MailSettings.Of(p.Settings).Accounts;
            if (!Mail.Configured(p.Settings)) p.Note("Set up a mail account first (Plugins page in this window).");
            var names = new List<string> { "Every account" };
            foreach (MailAccount a in accounts) names.Add(a.Shown);
            p.Combo("Account", names.ToArray(), delegate
            {
                MailAccount sel = accounts.Find(delegate (MailAccount x) { return x.Id == v.Account; });
                return sel != null ? sel.Shown : "Every account";
            }, delegate (string s)
            {
                MailAccount sel = accounts.Find(delegate (MailAccount x) { return x.Shown == s; });
                v.Account = sel != null ? sel.Id : "";
                p.Changed();
                p.Redraw();
            });
        }

        static void BarSettingsRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            MailView v = MailView.Of(cfg);
            AccountRow(p, v);
            if (v.Account.Length > 0)
                p.Check("Show the account's name next to the count", delegate { return v.ShowName; }, delegate (bool b) { v.ShowName = b; p.Changed(); });
            p.Check("Hide it while there's nothing unread", delegate { return cfg.HideIfNone; }, delegate (bool b) { cfg.HideIfNone = b; p.Changed(); });
            p.Header("In its popup");
            ListRows(p, v);
            p.Note("The envelope with the unread count (every account's together) - click it for the messages: the newest " + Mail.ListMax + " unread per account, with Mark as read " +
                   "and Delete on each (Delete moves it to the server's Trash folder when it has one). A click on a message or an account opens its webmail (Plugins page).");
        }

        static void ListSettingsRows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            MailView v = MailView.Of(cfg);
            AccountRow(p, v);
            p.Check("Hide it while there's nothing unread", delegate { return cfg.HideIfNone; }, delegate (bool b) { cfg.HideIfNone = b; p.Changed(); });
            ListRows(p, v);
        }

        static void ListRows(SettingsWindow.SettingsPage p, MailView v)
        {
            p.Number("Messages shown at once (scroll for the rest)", delegate { return v.Count; }, delegate (double d) { v.Count = (int)Math.Round(d); p.Changed(); });
            p.Check("Who it's from", delegate { return v.ShowFrom; }, delegate (bool b) { v.ShowFrom = b; p.Changed(); });
            p.Check("When it came", delegate { return v.ShowTime; }, delegate (bool b) { v.ShowTime = b; p.Changed(); });
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            var g = ModulePics.T(t, "", t.MainAccent, 14);
            g.FontFamily = IconUtil.GlyphFont;
            return ModulePics.Row(6, g, ModulePics.T(t, "3", t.MainAccent, 13));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Func<string, string, string, UIElement> row = delegate (string from, string subject, string when)
            {
                return ModulePics.Row(8, ModulePics.T(t, from, t.Fg, 11.5, true), ModulePics.T(t, subject, t.Fg, 11.5), ModulePics.T(t, when, t.FgDim, 10.5));
            };
            return ModulePics.Col(4,
                ModulePics.Row(8, ModulePics.T(t, "Work", t.Fg, 12, true), ModulePics.T(t, "2 unread", t.MainAccent, 11)),
                row("Sam", "Tomorrow's meeting", "2:05 PM"),
                row("Jenkins", "Build #412 passed", "11:20 AM"),
                ModulePics.Row(8, ModulePics.T(t, "Home", t.Fg, 12, true), ModulePics.T(t, "1 unread", t.MainAccent, 11)),
                row("Alex", "Photos from the weekend", "Yesterday"));
        }
    }
}
