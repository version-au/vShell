using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using vShell.Core;

namespace Mail
{
    /// <summary>A mail account as set up on the Plugins page (kept as it is in the settings - a SettingsBag.Live list).</summary>
    public sealed class MailAccount
    {
        public static readonly string[] Securities = { "SSL/TLS", "STARTTLS" };
        public static readonly string[] Auths = { "Password", "Google" };

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>The IMAP server (imap.gmail.com, mail.example.com).</summary>
        public string Host { get; set; } = "";
        public int Port { get; set; } = 993;
        /// <summary>"SSL/TLS" (a secure connection from the start, port 993) or "STARTTLS" (port 143, made secure before logging in).</summary>
        public string Security { get; set; } = "SSL/TLS";
        public string User { get; set; } = "";
        /// <summary>"Password" (LOGIN) or "Google" (Sign in with Google: XOAUTH2 with RefreshToken).</summary>
        public string Auth { get; set; } = "Password";
        /// <summary>Stored encrypted for the Windows account.</summary>
        public string Password { get; set; } = "";
        /// <summary>From Sign in with Google, stored encrypted.</summary>
        public string RefreshToken { get; set; } = "";
        /// <summary>The folder watched ("INBOX").</summary>
        public string Folder { get; set; } = "INBOX";
        /// <summary>Opened by a click on the module or a notification (https://mail.google.com).</summary>
        public string WebUrl { get; set; } = "";
        public bool IgnoreCert { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>What the folder was at the last check (UIDVALIDITY / UIDNEXT): messages from UIDNEXT up are new.</summary>
        public long SeenValidity { get; set; }
        public long SeenNext { get; set; }

        public bool Ssl { get { return Security != "STARTTLS"; } }
        public bool Google { get { return Auth == "Google"; } }
        /// <summary>A way to log in is there (a password, or a Google sign-in).</summary>
        public bool HasCredentials { get { return Google ? RefreshToken.Length > 0 : Password.Length > 0; } }
        /// <summary>The server, the user name and a way to log in - enough to try.</summary>
        public bool Ready { get { return Host.Trim().Length > 0 && User.Trim().Length > 0 && HasCredentials; } }

        public string Shown
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Name.Trim();
                if (!string.IsNullOrWhiteSpace(User)) return User.Trim();
                return string.IsNullOrWhiteSpace(Host) ? "Mail" : Host.Trim();
            }
        }

        /// <summary>Filled in enough to check.</summary>
        public bool Usable { get { return Enabled && Ready; } }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            Name = (Name ?? "").Trim();
            Host = (Host ?? "").Trim();
            User = (User ?? "").Trim();
            Folder = string.IsNullOrWhiteSpace(Folder) ? "INBOX" : Folder.Trim();
            WebUrl = (WebUrl ?? "").Trim();
            if (WebUrl.Length > 0 && !WebUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !WebUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) WebUrl = "https://" + WebUrl;
            if (Security != "STARTTLS") Security = "SSL/TLS";
            if (Port <= 0 || Port > 65535) Port = Ssl ? 993 : 143;
            if (Auth != "Google") Auth = "Password";
            Password = Secret.Protect(Password ?? "");        // (one typed into settings.json by hand gets encrypted)
            RefreshToken = Secret.Protect(RefreshToken ?? "");
        }
    }

    /// <summary>The connection: Settings > Plugins > Mail (in the settings under Integrations["mail"]).</summary>
    public sealed class MailSettings
    {
        public const string Id = "mail";
        readonly SettingsBag _b;
        MailSettings(SettingsBag b) { _b = b; }
        public static MailSettings Of(BarSettings s) { return new MailSettings(s.Integration(Id)); }

        /// <summary>The accounts, the same list every time (saved as it is).</summary>
        public List<MailAccount> Accounts { get { return _b.Live("Accounts", delegate { return new List<MailAccount>(); }); } }
        /// <summary>How often to check (seconds).</summary>
        public int Seconds { get { return Math.Max(15, Math.Min(3600, _b.Get("Seconds", 60))); } set { _b.Set("Seconds", value); } }
        public bool NotifyNew { get { return _b.Get("NotifyNew", true); } set { _b.Set("NotifyNew", value); } }
        /// <summary>How many messages one notification lists (the rest is "and N more").</summary>
        public int NotifyLines { get { return Math.Max(1, Math.Min(20, _b.Get("NotifyLines", 5))); } set { _b.Set("NotifyLines", value); } }
        public bool NotifyServer { get { return _b.Get("NotifyServer", false); } set { _b.Set("NotifyServer", value); } }
        /// <summary>Wait this long before saying a server can't be reached.</summary>
        public int NotifyDelay { get { return Math.Max(0, _b.Get("NotifyDelay", 120)); } set { _b.Set("NotifyDelay", value); } }
        /// <summary>Your own Google OAuth client, if you'd rather not use vShell's (empty = vShell's).</summary>
        public string GoogleClientId { get { return _b.Get("GoogleClientId", "").Trim(); } set { _b.Set("GoogleClientId", (value ?? "").Trim()); } }
        public string GoogleClientSecretStored { get { return _b.Get("GoogleClientSecret", ""); } set { _b.Set("GoogleClientSecret", value ?? ""); } }
        public string GoogleClientSecret { get { return _b.GetSecret("GoogleClientSecret"); } }

        public bool Configured
        {
            get { foreach (MailAccount a in Accounts) if (a != null && a.Usable) return true; return false; }
        }
    }

    /// <summary>What one account's folder looked like at the last check (or why it couldn't be checked).</summary>
    public sealed class MailState
    {
        public MailAccount Account;
        public string Error;
        public int Unread;
        /// <summary>The newest unread messages, newest first.</summary>
        public List<MailMessage> Messages = new List<MailMessage>();
        public DateTime At = DateTime.UtcNow;
        /// <summary>Not checked yet.</summary>
        public bool Pending;
        /// <summary>The check itself (what's new, where the folder is up to) - for the notifications.</summary>
        public ImapCheck Check;
    }

    /// <summary>Checks the accounts in the background (fast while a module shows them, at the set interval for notifications).</summary>
    public static class Mail
    {
        /// <summary>How many unread messages are read per account (a popup lists up to this many, scrolling).</summary>
        public const int ListMax = 100;

        static readonly object Gate = new object();
        /// <summary>Each account's last check, as the server gave it (what's shown leaves out Away's messages).</summary>
        static readonly Dictionary<string, MailState> States = new Dictionary<string, MailState>();

        /// <summary>
        /// A message marked read or deleted (2.x): out of the list at once and kept out while the server does it in the background - and
        /// after, until a check that started once it was done no longer has it (a check already under way would have brought it back for a
        /// moment). If the server says no, it comes back.
        /// </summary>
        sealed class Gone
        {
            public long Uid;
            public bool Done;
            public DateTime DoneAt;
        }
        static readonly Dictionary<string, List<Gone>> Away = new Dictionary<string, List<Gone>>();
        /// <summary>The longest a message stays out once the server said yes but checks still list it.</summary>
        static readonly TimeSpan AwayMax = TimeSpan.FromMinutes(2);
        /// <summary>Each account's Trash folder as the server names it ("" = it has none), once looked up.</summary>
        static readonly Dictionary<string, string> Trash = new Dictionary<string, string>();

        /// <summary>A connection kept open after Mark as read / Delete, so the next one is quick (closed after a minute unused).</summary>
        sealed class Session
        {
            public ImapClient Client;
            public string Folder = "";
            public DateTime Used = DateTime.UtcNow;
            public readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);
        }
        static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        static Timer _sessionTimer;
        static readonly TimeSpan SessionIdle = TimeSpan.FromSeconds(60);

        /// <summary>What's shown changed outside a check (a message marked read or deleted): modules redraw now.</summary>
        public static event Action Changed;
        static Poller _poller;
        static BarSettings _s;
        static bool _notifyNew, _notifyServer;
        static int _delay, _lines;
        static string _sig = "";

        /// <summary>A notification to post (title, detail, url, kind key).</summary>
        public static event Action<string, string, string, string> Notice;
        /// <summary>Settings changed by a check (the last seen UIDs): save them.</summary>
        public static Action Save;

        public static bool Configured(BarSettings s) { return s != null && MailSettings.Of(s).Configured; }

        public static void Configure(BarSettings s)
        {
            _s = s;
            MailSettings m = MailSettings.Of(s);
            List<MailAccount> accounts = m.Accounts;
            var sig = new StringBuilder();
            lock (Gate)
            {
                accounts.RemoveAll(delegate (MailAccount a) { return a == null; });
                var keep = new HashSet<string>();
                foreach (MailAccount a in accounts)
                {
                    a.Normalize();
                    keep.Add(a.Id);
                    sig.Append(a.Id).Append('|').Append(a.Enabled).Append('|').Append(a.Host).Append(':').Append(a.Port).Append('|').Append(a.Security)
                       .Append('|').Append(a.User).Append('|').Append(a.Auth).Append('|').Append(a.Password.Length).Append('|').Append(a.RefreshToken.Length)
                       .Append('|').Append(a.Folder).Append('|').Append(a.IgnoreCert).Append('\n');
                }
                foreach (string id in new List<string>(States.Keys)) if (!keep.Contains(id)) States.Remove(id);
                foreach (string id in new List<string>(Trash.Keys)) if (!keep.Contains(id)) Trash.Remove(id);
                foreach (string id in new List<string>(Away.Keys)) if (!keep.Contains(id)) Away.Remove(id);
            }
            _notifyNew = m.NotifyNew; _notifyServer = m.NotifyServer; _delay = m.NotifyDelay; _lines = m.NotifyLines;
            if (!m.Configured) { Stop(); _sig = ""; return; }
            bool changed = sig.ToString() != _sig;
            _sig = sig.ToString();
            if (_poller == null)
            {
                _poller = new Poller("Mail", Poll, TimeSpan.FromSeconds(m.Seconds), TimeSpan.FromSeconds(m.Seconds));
                _poller.Start();
                changed = true;
            }
            _poller.Fast = TimeSpan.FromSeconds(m.Seconds);
            // with no notifications on, nothing to do while no module shows it
            _poller.Slow = _notifyNew || _notifyServer ? TimeSpan.FromSeconds(m.Seconds) : TimeSpan.Zero;
            // an account changed (not a slider elsewhere): check again now
            if (changed) { lock (Gate) { States.Clear(); Trash.Clear(); } CloseSessions(); _poller.PollNow(); }
        }

        public static void Stop()
        {
            if (_poller != null) { _poller.Stop(); _poller = null; }
            lock (Gate) { States.Clear(); Trash.Clear(); Away.Clear(); }
            CloseSessions();
        }

        // ---- the kept connections ----
        /// <summary>The account's open connection with its folder selected (made, or made again when the old one is gone).</summary>
        static async Task<Session> SessionAsync(MailAccount a)
        {
            Session s;
            lock (Gate)
            {
                if (!Sessions.TryGetValue(a.Id, out s)) { s = new Session(); Sessions[a.Id] = s; }
                if (_sessionTimer == null) _sessionTimer = new Timer(delegate { CloseIdle(); }, null, 15000, 15000);
            }
            await s.Lock.WaitAsync();
            s.Used = DateTime.UtcNow;
            if (s.Client != null && s.Client.Broken) { s.Client.Dispose(); s.Client = null; }
            if (s.Client == null)
            {
                var c = new ImapClient();
                try
                {
                    await c.ConnectAsync(a.Host, a.Port, a.Ssl, a.IgnoreCert);
                    await LoginAsync(c, a);
                    await c.CapabilityAsync();        // (what it offers once logged in: MOVE, UIDPLUS)
                }
                catch { c.Dispose(); s.Lock.Release(); throw; }
                s.Client = c;
                s.Folder = "";
            }
            if (s.Folder != a.Folder)
            {
                try { await s.Client.SelectAsync(a.Folder); }
                catch { s.Folder = ""; Drop(s); s.Lock.Release(); throw; }
                s.Folder = a.Folder;
            }
            return s;
        }

        /// <summary>The connection failed: forget it (the next action makes a new one).</summary>
        static void Drop(Session s)
        {
            if (s.Client == null) return;
            if (s.Client.Broken) { s.Client.Dispose(); s.Client = null; s.Folder = ""; }
        }

        static void CloseIdle()
        {
            List<Session> old;
            lock (Gate)
            {
                old = new List<Session>();
                foreach (KeyValuePair<string, Session> kv in new List<KeyValuePair<string, Session>>(Sessions))
                    if (DateTime.UtcNow - kv.Value.Used > SessionIdle) { old.Add(kv.Value); Sessions.Remove(kv.Key); }
            }
            foreach (Session s in old) Close(s);
        }

        static void CloseSessions()
        {
            List<Session> all;
            lock (Gate) { all = new List<Session>(Sessions.Values); Sessions.Clear(); }
            foreach (Session s in all) Close(s);
        }

        static void Close(Session s)
        {
            Task.Run(async delegate
            {
                if (!await s.Lock.WaitAsync(5000)) return;
                try
                {
                    ImapClient c = s.Client;
                    s.Client = null;
                    if (c == null) return;
                    if (!c.Broken) await c.LogoutAsync();
                    c.Dispose();
                }
                catch { }
                finally { s.Lock.Release(); }
            });
        }

        /// <summary>Every account in use (the Plugins page's order) with what was last read. Asking keeps the checks fast.</summary>
        public static List<MailState> All()
        {
            Poller p = _poller;
            if (p != null) p.Want();
            var list = new List<MailState>();
            BarSettings s = _s;
            if (s == null) return list;
            lock (Gate)
                foreach (MailAccount a in MailSettings.Of(s).Accounts)
                {
                    if (a == null || !a.Usable) continue;
                    MailState st;
                    if (!States.TryGetValue(a.Id, out st)) st = new MailState { Account = a, Pending = true };
                    list.Add(Without(st));
                }
            return list;
        }

        /// <summary>The accounts a module shows: all, or the one with this id.</summary>
        public static List<MailState> For(string accountId)
        {
            List<MailState> all = All();
            if (string.IsNullOrEmpty(accountId)) return all;
            return all.FindAll(delegate (MailState st) { return st.Account.Id == accountId; });
        }

        public static int Unread(List<MailState> states)
        {
            int n = 0;
            foreach (MailState st in states) if (st.Error == null) n += st.Unread;
            return n;
        }

        static async Task Poll()
        {
            BarSettings s = _s;
            if (s == null) return;
            List<MailAccount> accounts;
            lock (Gate) accounts = new List<MailAccount>(MailSettings.Of(s).Accounts);
            var tasks = new List<Task>();
            foreach (MailAccount a in accounts) if (a != null && a.Usable) tasks.Add(CheckOne(a));
            await Task.WhenAll(tasks);
        }

        static async Task CheckOne(MailAccount a)
        {
            DateTime started = DateTime.UtcNow;
            MailState st = await ReadAsync(a, a.SeenValidity, a.SeenNext, ListMax);
            lock (Gate)
            {
                States[a.Id] = st;
                // done ones this check has seen the end of (it started after they were done and doesn't list them) are finished with
                List<Gone> away;
                if (st.Error == null && Away.TryGetValue(a.Id, out away))
                {
                    away.RemoveAll(delegate (Gone g)
                    {
                        if (!g.Done) return false;
                        if (DateTime.UtcNow - g.DoneAt > AwayMax) return true;
                        return g.DoneAt <= started && !st.Messages.Exists(delegate (MailMessage m) { return m.Uid == g.Uid; });
                    });
                    if (away.Count == 0) Away.Remove(a.Id);
                }
            }
            Compare(a, st);
        }

        /// <summary>What's shown for an account: its last check without the messages being marked read / deleted (call under Gate).</summary>
        static MailState Without(MailState st)
        {
            List<Gone> away;
            if (st.Pending || st.Error != null || !Away.TryGetValue(st.Account.Id, out away) || away.Count == 0) return st;
            var copy = new MailState { Account = st.Account, Unread = st.Unread, At = st.At, Check = st.Check, Messages = new List<MailMessage>(st.Messages.Count) };
            foreach (MailMessage m in st.Messages)
            {
                if (away.Exists(delegate (Gone g) { return g.Uid == m.Uid; })) { copy.Unread--; continue; }
                copy.Messages.Add(m);
            }
            if (copy.Unread < 0) copy.Unread = 0;
            return copy;
        }

        /// <summary>One check of an account: log in, open the folder, count the unread and read the newest ones' headers.</summary>
        static async Task<MailState> ReadAsync(MailAccount a, long seenValidity, long seenNext, int listMax)
        {
            var st = new MailState { Account = a };
            ImapCheck c = null;
            try
            {
                c = await CheckAsync(a, seenValidity, seenNext, listMax);
                st.Unread = c.Unread;
                st.Messages = c.Messages;
            }
            catch (Exception ex) { st.Error = ex.Message; }
            st.Check = c;
            return st;
        }

        /// <summary>Logs in the account's way: its password, or a Google access token.</summary>
        static async Task LoginAsync(ImapClient client, MailAccount a)
        {
            if (!a.Google) { await client.LoginAsync(a.User, Secret.Unprotect(a.Password)); return; }
            BarSettings s = _s;
            string token = await GoogleAuth.AccessTokenAsync(a, s != null ? MailSettings.Of(s) : null);
            await client.AuthenticateOAuthAsync(a.User, token);
        }

        /// <summary>The IMAP side of a check (throws ImapException with a plain-English reason).</summary>
        static async Task<ImapCheck> CheckAsync(MailAccount a, long seenValidity, long seenNext, int listMax)
        {
            using (var client = new ImapClient())
            {
                await client.ConnectAsync(a.Host, a.Port, a.Ssl, a.IgnoreCert);
                await LoginAsync(client, a);
                ImapCheck c = await client.ExamineAsync(a.Folder);
                List<long> unseen = await client.SearchAsync("UNSEEN");
                c.Unread = unseen.Count;
                // the newest unread for the list, and the unread that came since the last check
                var want = new HashSet<long>();
                for (int i = unseen.Count - 1; i >= 0 && i >= unseen.Count - listMax; i--) want.Add(unseen[i]);
                var fresh = new List<long>();
                bool known = seenNext > 0 && seenValidity == c.UidValidity;
                if (known) foreach (long u in unseen) if (u >= seenNext) fresh.Add(u);
                if (fresh.Count > 200) fresh.RemoveRange(0, fresh.Count - 200);
                foreach (long u in fresh) want.Add(u);
                var uids = new List<long>(want);
                uids.Sort();
                List<MailMessage> msgs = await client.FetchAsync(uids);
                msgs.Sort(delegate (MailMessage x, MailMessage y) { return y.Uid.CompareTo(x.Uid); });
                foreach (MailMessage m in msgs)
                {
                    if (c.Messages.Count < listMax) c.Messages.Add(m);
                    if (known && m.Uid >= seenNext) c.New.Add(m);
                }
                await client.LogoutAsync();
                return c;
            }
        }

        /// <summary>Settings > Plugins > Test connection.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(MailAccount a)
        {
            if (!a.Ready)
                return new KeyValuePair<bool, string>(false, a.Google ? "Sign in with Google first." : "Fill in the server, the user name and the password first.");
            try
            {
                ImapCheck c = await CheckAsync(a, 0, 0, 0);
                return new KeyValuePair<bool, string>(true, "Connected - " + a.Folder + " has " + c.Exists.ToString("N0", CultureInfo.CurrentCulture) + " message" + (c.Exists == 1 ? "" : "s") +
                                                             ", " + c.Unread.ToString("N0", CultureInfo.CurrentCulture) + " unread.");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, ex.Message); }
        }

        /// <summary>Marks a message read (the \Seen flag). Returns null, or why it couldn't.</summary>
        public static Task<string> MarkReadAsync(MailAccount a, long uid) { return ActAsync(a, uid, false); }

        /// <summary>Deletes a message: moved to the server's Trash folder when it has one and offers MOVE, else flagged \Deleted and expunged.</summary>
        public static Task<string> DeleteAsync(MailAccount a, long uid) { return ActAsync(a, uid, true); }

        static async Task<string> ActAsync(MailAccount a, long uid, bool delete)
        {
            // out of the list at once and kept out (a check that's already under way doesn't bring it back); back if the server says no
            Gone gone = new Gone { Uid = uid };
            lock (Gate)
            {
                List<Gone> away;
                if (!Away.TryGetValue(a.Id, out away)) Away[a.Id] = away = new List<Gone>();
                away.RemoveAll(delegate (Gone g) { return g.Uid == uid; });
                away.Add(gone);
            }
            Raise();
            string err = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                err = null;
                Session s;
                try { s = await SessionAsync(a); }
                catch (Exception ex) { err = ex.Message; break; }
                try
                {
                    ImapClient client = s.Client;
                    if (!delete) await client.FlagAsync(uid, "\\Seen");
                    else
                    {
                        string trash;
                        lock (Gate) if (!Trash.TryGetValue(a.Id, out trash)) trash = null;
                        if (trash == null)
                        {
                            trash = "";
                            foreach (ImapFolder f in await client.FoldersAsync()) if (f.IsTrash && !f.NoSelect) { trash = f.Shown; break; }
                            lock (Gate) Trash[a.Id] = trash;
                        }
                        if (trash.Length > 0 && client.Has("MOVE") && !string.Equals(trash, a.Folder, StringComparison.OrdinalIgnoreCase)) await client.MoveAsync(uid, trash);
                        else { await client.FlagAsync(uid, "\\Deleted"); await client.ExpungeAsync(uid); }
                    }
                }
                catch (Exception ex) { err = ex.Message; Drop(s); }
                finally { s.Used = DateTime.UtcNow; s.Lock.Release(); }
                // a kept connection the server had dropped meanwhile: once more on a new one
                if (err == null || s.Client != null) break;
            }
            if (err != null)
            {
                lock (Gate)
                {
                    List<Gone> away;
                    if (Away.TryGetValue(a.Id, out away)) { away.Remove(gone); if (away.Count == 0) Away.Remove(a.Id); }
                }
                Raise();
                return err;
            }
            lock (Gate) { gone.Done = true; gone.DoneAt = DateTime.UtcNow; }
            Poller p = _poller;
            if (p != null) p.PollNow();
            return null;
        }

        static void Raise()
        {
            Action c = Changed;
            if (c != null) try { c(); } catch (Exception ex) { Log.Write("Mail: changed: " + ex.Message); }
        }

        /// <summary>The folders on the server (for Pick a folder).</summary>
        public static async Task<List<ImapFolder>> FoldersAsync(MailAccount a)
        {
            using (var client = new ImapClient())
            {
                await client.ConnectAsync(a.Host, a.Port, a.Ssl, a.IgnoreCert);
                await LoginAsync(client, a);
                List<ImapFolder> list = await client.FoldersAsync();
                await client.LogoutAsync();
                return list;
            }
        }

        // ---- notifications ----
        static void Compare(MailAccount a, MailState st)
        {
            Action<string, string, string, string> notice = Notice;
            string key = "mail:" + a.Id;
            if (st.Error != null)
            {
                if (notice != null && _notifyServer)
                    DownDelay.Down(key, _delay, delegate { notice("Can't check " + a.Shown, st.Error, a.WebUrl, "Mail.Problem"); });
                return;
            }
            if (DownDelay.Up(key) == UpResult.WasNotified && notice != null && _notifyServer) notice(a.Shown + " can be checked again", "", a.WebUrl, "Mail.Problem");
            ImapCheck c = st.Check;
            if (c == null) return;
            if (notice != null && _notifyNew && c.New.Count > 0) notice(Title(a, c.New.Count), Detail(c.New, _lines), a.WebUrl, "Mail.New");
            // remember where the folder is up to (the first check only learns - no notices for what was already there)
            if (a.SeenValidity != c.UidValidity || a.SeenNext != c.UidNext)
            {
                a.SeenValidity = c.UidValidity;
                a.SeenNext = c.UidNext;
                Action save = Save;
                if (save != null) try { save(); } catch (Exception ex) { Log.Write("Mail: save: " + ex.Message); }
            }
        }

        static string Title(MailAccount a, int n)
        {
            return (n == 1 ? "1 new message" : n + " new messages") + " in " + a.Shown;
        }

        /// <summary>"Jane - Lunch?" one per line, then "and 3 more".</summary>
        static string Detail(List<MailMessage> msgs, int lines)
        {
            var sb = new StringBuilder();
            int shown = 0;
            foreach (MailMessage m in msgs)
            {
                if (shown == lines) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Line(m));
                shown++;
            }
            if (msgs.Count > shown) sb.Append('\n').Append("and ").Append(msgs.Count - shown).Append(" more");
            return sb.ToString();
        }

        public static string Line(MailMessage m)
        {
            string from = m.From.Length > 0 ? m.From : "(no sender)";
            string subject = m.Subject.Length > 0 ? m.Subject : "(no subject)";
            return from + " - " + subject;
        }
    }
}
