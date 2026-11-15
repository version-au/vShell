using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mail
{
    /// <summary>A folder on the server (LIST): its name as the server knows it, and as shown.</summary>
    public sealed class ImapFolder
    {
        public string Name = "", Shown = "";
        public bool NoSelect;
        /// <summary>Its attributes as listed: \HasNoChildren \Trash …</summary>
        public string Attrs = "";
        public bool IsTrash { get { return Attrs.IndexOf("\\Trash", StringComparison.OrdinalIgnoreCase) >= 0; } }
    }

    /// <summary>One message's headers (From, Subject, when it came).</summary>
    public sealed class MailMessage
    {
        public long Uid;
        public string From = "", Subject = "";
        /// <summary>When it came (UTC; MinValue = not known).</summary>
        public DateTime Date = DateTime.MinValue;
    }

    /// <summary>What a folder looks like after one check.</summary>
    public sealed class ImapCheck
    {
        public long UidValidity, UidNext;
        public int Exists, Unread;
        /// <summary>The newest unread messages (newest first), as many as were asked for.</summary>
        public List<MailMessage> Messages = new List<MailMessage>();
        /// <summary>The unread ones that came since the last check (newest first) - empty on the first check of a folder.</summary>
        public List<MailMessage> New = new List<MailMessage>();
    }

    public sealed class ImapException : Exception
    {
        public ImapException(string message) : base(message) { }
    }

    /// <summary>One response line from the server: its tag ("*", "+" or ours), the text, and any literals ({n} + bytes) it carried.</summary>
    sealed class ImapResponse
    {
        public string Tag = "", Text = "";
        /// <summary>Each literal in Text is replaced by a \u0001: the n-th one is Literals[n].</summary>
        public List<string> Literals = new List<string>();
        public bool Ok { get { return Text.StartsWith("OK", StringComparison.OrdinalIgnoreCase); } }
        public bool No { get { return Text.StartsWith("NO", StringComparison.OrdinalIgnoreCase); } }
        public bool Bad { get { return Text.StartsWith("BAD", StringComparison.OrdinalIgnoreCase); } }
        /// <summary>The server's words after OK / NO / BAD (without a [CODE]).</summary>
        public string Reason
        {
            get
            {
                string t = Text;
                int sp = t.IndexOf(' ');
                t = sp < 0 ? "" : t.Substring(sp + 1).Trim();
                if (t.StartsWith("[")) { int e = t.IndexOf(']'); if (e > 0) t = t.Substring(e + 1).Trim(); }
                return t;
            }
        }
    }

    /// <summary>
    /// A small IMAP client (RFC 3501) over TcpClient + SslStream: enough to log in, look at a folder, count and read the unread messages'
    /// headers, and list the folders. One connection per check - nothing stays open.
    /// </summary>
    sealed class ImapClient : IDisposable
    {
        static readonly Regex LiteralEnd = new Regex(@"\{(\d+)\}$", RegexOptions.Compiled);
        static readonly Encoding Latin1 = Encoding.Latin1;

        TcpClient _tcp;
        Stream _stream;
        int _tagNo;
        readonly byte[] _buf = new byte[16384];
        int _bufLen, _bufPos;
        CancellationToken _ct;
        string _host = "";

        public string Capabilities = "";
        /// <summary>How long one command (or the connecting) may take.</summary>
        public TimeSpan Timeout = TimeSpan.FromSeconds(30);
        /// <summary>The connection is gone (a command failed on the wire): don't use it again.</summary>
        public bool Broken { get; private set; }

        /// <summary>A timed cancellation for one command: _ct is set for its reads and writes.</summary>
        CancellationTokenSource Timed()
        {
            var cts = new CancellationTokenSource(Timeout);
            _ct = cts.Token;
            return cts;
        }

        public async Task ConnectAsync(string host, int port, bool ssl, bool ignoreCert)
        {
            _host = host;
            _tcp = new TcpClient();
            using (Timed())
            {
                try { await _tcp.ConnectAsync(host, port, _ct); }
                catch (OperationCanceledException) { throw new ImapException("Can't reach " + host + ":" + port + " (no answer in time)."); }
                catch (SocketException ex) { throw new ImapException("Can't reach " + host + ":" + port + " - " + ex.Message); }
                _stream = _tcp.GetStream();
                if (ssl) await StartTlsAsync(ignoreCert);
                ImapResponse hello = await ReadResponseAsync();
                if (hello.Tag != "*" || hello.Text.StartsWith("BYE", StringComparison.OrdinalIgnoreCase))
                    throw new ImapException("That isn't an IMAP server (" + Short(hello.Text) + ").");
                if (hello.Text.IndexOf("[CAPABILITY", StringComparison.OrdinalIgnoreCase) >= 0)
                    Capabilities = Between(hello.Text, "[CAPABILITY", "]");
            }
            if (!ssl)
            {
                // STARTTLS: the server must offer it - a password never goes over a plain connection
                if (Capabilities.Length == 0) await CapabilityAsync();
                if (Capabilities.IndexOf("STARTTLS", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new ImapException("The server doesn't offer STARTTLS on port " + port + " - try SSL/TLS on port 993.");
                ImapResponse r = await RunAsync("STARTTLS");
                if (!r.Ok) throw new ImapException("STARTTLS was refused: " + r.Reason);
                using (Timed()) await StartTlsAsync(ignoreCert);
                Capabilities = "";
            }
        }

        async Task StartTlsAsync(bool ignoreCert)
        {
            var ssl = new SslStream(_stream, false, delegate (object o, System.Security.Cryptography.X509Certificates.X509Certificate c,
                                                             System.Security.Cryptography.X509Certificates.X509Chain ch, SslPolicyErrors errors)
            { return errors == SslPolicyErrors.None || ignoreCert; });
            try { await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = _host }, _ct); }
            catch (OperationCanceledException) { throw new ImapException("The secure connection to " + _host + " took too long."); }
            catch (Exception ex)
            {
                string m = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                if (m.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0 || ex is System.Security.Authentication.AuthenticationException)
                    throw new ImapException("The server's certificate isn't trusted (" + m + "). A self-signed one: tick Accept a self-signed certificate.");
                throw new ImapException("No secure connection to " + _host + ": " + m);
            }
            _stream = ssl;
            _bufLen = _bufPos = 0;
        }

        public async Task CapabilityAsync()
        {
            List<ImapResponse> all = await RunAllAsync("CAPABILITY");
            foreach (ImapResponse r in all)
                if (r.Tag == "*" && r.Text.StartsWith("CAPABILITY", StringComparison.OrdinalIgnoreCase)) Capabilities = r.Text.Substring(10).Trim();
        }

        public async Task LoginAsync(string user, string password)
        {
            if (Capabilities.Length == 0) await CapabilityAsync();
            if (Capabilities.IndexOf("LOGINDISABLED", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new ImapException("The server doesn't allow logging in with a password (LOGINDISABLED). Gmail: choose Sign in with Google instead.");
            string u = Quoted(user), p = Quoted(password);
            ImapResponse r;
            if (u != null && p != null) r = await RunAsync("LOGIN " + u + " " + p);
            else
            {
                // a user name or password with characters a quoted string can't hold: sent as literals
                byte[] ub = Encoding.UTF8.GetBytes(user), pb = Encoding.UTF8.GetBytes(password);
                string tag = NextTag();
                using (Timed())
                {
                    await WriteAsync(tag + " LOGIN {" + ub.Length + "}\r\n");
                    await ContinueAsync(tag);
                    await WriteAsync(ub, " {" + pb.Length + "}\r\n");
                    await ContinueAsync(tag);
                    await WriteAsync(pb, "\r\n");
                    r = await ReadTaggedAsync(tag, null);
                }
            }
            if (!r.Ok)
            {
                string why = r.Reason;
                throw new ImapException("The server didn't accept the user name or password" + (why.Length > 0 ? " (" + Short(why) + ")" : "") +
                                        ". Gmail and many others need an app password here, not the normal one.");
            }
        }

        /// <summary>Logs in with an OAuth access token (AUTHENTICATE XOAUTH2 - Gmail, Outlook.com).</summary>
        public async Task AuthenticateOAuthAsync(string user, string accessToken)
        {
            // (the greeting's list may be the short one from before TLS: ask once more before saying no)
            if (Capabilities.IndexOf("XOAUTH2", StringComparison.OrdinalIgnoreCase) < 0) await CapabilityAsync();
            if (Capabilities.IndexOf("XOAUTH2", StringComparison.OrdinalIgnoreCase) < 0)
                throw new ImapException("The server doesn't take a Google sign-in (no XOAUTH2) - use a password with it.");
            string blob = Convert.ToBase64String(Encoding.UTF8.GetBytes("user=" + user + "\u0001auth=Bearer " + accessToken + "\u0001\u0001"));
            string tag = NextTag();
            ImapResponse r;
            using (Timed())
            {
                await WriteAsync(tag + " AUTHENTICATE XOAUTH2 " + blob + "\r\n");
                while (true)
                {
                    r = await ReadResponseAsync();
                    if (r.Tag == tag) break;
                    // a refusal comes as "+ <base64 JSON>" wanting an empty line before the NO
                    if (r.Tag == "+") { await WriteAsync("\r\n"); continue; }
                    if (r.Tag == "*" && r.Text.StartsWith("BYE", StringComparison.OrdinalIgnoreCase)) { Broken = true; throw new ImapException("The server closed the connection: " + Short(r.Text.Substring(3).Trim())); }
                }
            }
            if (!r.Ok) throw new ImapException("The server didn't accept the Google sign-in" + (r.Reason.Length > 0 ? " (" + Short(r.Reason) + ")" : "") + ". Sign in with Google again on the Plugins page.");
        }

        /// <summary>Opens a folder read-only (EXAMINE): UIDVALIDITY, UIDNEXT and how many messages it has.</summary>
        public async Task<ImapCheck> ExamineAsync(string folder)
        {
            var c = new ImapCheck();
            List<ImapResponse> all = await RunAllAsync("EXAMINE " + QuotedFolder(folder));
            ImapResponse last = all[all.Count - 1];
            if (!last.Ok) throw new ImapException("The folder \"" + folder + "\" can't be opened: " + (last.Reason.Length > 0 ? Short(last.Reason) : "the server said no") + ".");
            foreach (ImapResponse r in all)
            {
                if (r.Tag != "*") { Code(r.Text, "UIDVALIDITY", ref c.UidValidity); Code(r.Text, "UIDNEXT", ref c.UidNext); continue; }
                Code(r.Text, "UIDVALIDITY", ref c.UidValidity);
                Code(r.Text, "UIDNEXT", ref c.UidNext);
                Match m = Regex.Match(r.Text, @"^(\d+) EXISTS$");
                if (m.Success) c.Exists = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            return c;
        }

        static void Code(string text, string name, ref long into)
        {
            Match m = Regex.Match(text, @"\[" + name + @" (\d+)\]");
            if (m.Success) long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out into);
        }

        /// <summary>UID SEARCH with these words ("UNSEEN", "UNSEEN UID 500:*"): the UIDs, lowest first.</summary>
        public async Task<List<long>> SearchAsync(string what)
        {
            var uids = new List<long>();
            List<ImapResponse> all = await RunAllAsync("UID SEARCH " + what);
            ImapResponse last = all[all.Count - 1];
            if (!last.Ok) throw new ImapException("The search failed: " + Short(last.Reason));
            foreach (ImapResponse r in all)
            {
                if (r.Tag != "*") continue;
                if (r.Text.StartsWith("SEARCH", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string s in r.Text.Substring(6).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        long u;
                        if (long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out u)) uids.Add(u);
                    }
                }
                else if (r.Text.StartsWith("ESEARCH", StringComparison.OrdinalIgnoreCase))
                {
                    // a server that answers the new way: ... ALL 1:3,7
                    Match m = Regex.Match(r.Text, @"\bALL ([\d:,]+)");
                    if (m.Success) foreach (string part in m.Groups[1].Value.Split(','))
                    {
                        string[] ab = part.Split(':');
                        long a, b;
                        if (!long.TryParse(ab[0], NumberStyles.None, CultureInfo.InvariantCulture, out a)) continue;
                        if (ab.Length == 1) { uids.Add(a); continue; }
                        if (!long.TryParse(ab[1], NumberStyles.None, CultureInfo.InvariantCulture, out b)) continue;
                        for (long x = Math.Min(a, b); x <= Math.Max(a, b) && uids.Count < 100000; x++) uids.Add(x);
                    }
                }
            }
            uids.Sort();
            return uids;
        }

        /// <summary>From, Subject and the date of these messages (any order).</summary>
        public async Task<List<MailMessage>> FetchAsync(List<long> uids)
        {
            var list = new List<MailMessage>();
            if (uids.Count == 0) return list;
            var set = new StringBuilder();
            foreach (long u in uids) { if (set.Length > 0) set.Append(','); set.Append(u.ToString(CultureInfo.InvariantCulture)); }
            List<ImapResponse> all = await RunAllAsync("UID FETCH " + set + " (UID INTERNALDATE BODY.PEEK[HEADER.FIELDS (FROM SUBJECT DATE)])");
            ImapResponse last = all[all.Count - 1];
            if (!last.Ok) throw new ImapException("Reading the messages failed: " + Short(last.Reason));
            foreach (ImapResponse r in all)
            {
                if (r.Tag != "*" || r.Text.IndexOf(" FETCH ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Match um = Regex.Match(r.Text, @"\bUID (\d+)");
                if (!um.Success) continue;
                var m = new MailMessage { Uid = long.Parse(um.Groups[1].Value, CultureInfo.InvariantCulture) };
                Match dm = Regex.Match(r.Text, "INTERNALDATE \"([^\"]*)\"");
                if (dm.Success) m.Date = MailText.InternalDate(dm.Groups[1].Value);
                string header = r.Literals.Count > 0 ? r.Literals[0] : "";
                if (header.Length == 0)
                {
                    // a server that sends a short header as a quoted string
                    Match qm = Regex.Match(r.Text, "HEADER\\.FIELDS[^\\]]*\\] \"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (qm.Success) header = qm.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
                Dictionary<string, string> h = MailText.Headers(header);
                string v;
                if (h.TryGetValue("subject", out v)) m.Subject = MailText.Decode(v);
                if (h.TryGetValue("from", out v)) m.From = MailText.Sender(MailText.Decode(v));
                if (m.Date == DateTime.MinValue && h.TryGetValue("date", out v)) m.Date = MailText.HeaderDate(v);
                list.Add(m);
            }
            return list;
        }

        /// <summary>Every folder (LIST "" "*"), the way the server names them and as shown.</summary>
        public async Task<List<ImapFolder>> FoldersAsync()
        {
            var list = new List<ImapFolder>();
            List<ImapResponse> all = await RunAllAsync("LIST \"\" \"*\"");
            ImapResponse last = all[all.Count - 1];
            if (!last.Ok) throw new ImapException("The folders can't be listed: " + Short(last.Reason));
            foreach (ImapResponse r in all)
            {
                if (r.Tag != "*" || !r.Text.StartsWith("LIST ", StringComparison.OrdinalIgnoreCase)) continue;
                Match m = Regex.Match(r.Text, "^LIST \\((.*?)\\) (?:\"(?:[^\"\\\\]|\\\\.)*\"|NIL) (.+)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string name = m.Groups[2].Value.Trim();
                if (name == "\u0001") name = r.Literals.Count > 0 ? r.Literals[0] : "";
                else if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"') name = name.Substring(1, name.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                if (name.Length == 0) continue;
                var f = new ImapFolder { Name = name, Shown = MailText.FromUtf7(name), Attrs = m.Groups[1].Value, NoSelect = m.Groups[1].Value.IndexOf("\\Noselect", StringComparison.OrdinalIgnoreCase) >= 0 };
                list.Add(f);
            }
            list.Sort(delegate (ImapFolder a, ImapFolder b)
            {
                bool ai = a.Shown.Equals("INBOX", StringComparison.OrdinalIgnoreCase), bi = b.Shown.Equals("INBOX", StringComparison.OrdinalIgnoreCase);
                if (ai != bi) return ai ? -1 : 1;
                return string.Compare(a.Shown, b.Shown, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        public bool Has(string capability) { return (" " + Capabilities + " ").IndexOf(" " + capability + " ", StringComparison.OrdinalIgnoreCase) >= 0; }

        /// <summary>Opens a folder for changes (SELECT).</summary>
        public async Task SelectAsync(string folder)
        {
            ImapResponse r = await RunAsync("SELECT " + QuotedFolder(folder));
            if (!r.Ok) throw new ImapException("The folder \"" + folder + "\" can't be opened: " + (r.Reason.Length > 0 ? Short(r.Reason) : "the server said no") + ".");
        }

        /// <summary>Sets a flag on a message (\Seen, \Deleted).</summary>
        public async Task FlagAsync(long uid, string flag)
        {
            ImapResponse r = await RunAsync("UID STORE " + uid.ToString(CultureInfo.InvariantCulture) + " +FLAGS.SILENT (" + flag + ")");
            if (!r.Ok) throw new ImapException("The server wouldn't change the message: " + Short(r.Reason));
        }

        /// <summary>Moves a message to another folder (the server must offer MOVE).</summary>
        public async Task MoveAsync(long uid, string folder)
        {
            ImapResponse r = await RunAsync("UID MOVE " + uid.ToString(CultureInfo.InvariantCulture) + " " + QuotedFolder(folder));
            if (!r.Ok) throw new ImapException("The server wouldn't move the message: " + Short(r.Reason));
        }

        /// <summary>Removes the messages flagged \Deleted - only this one with UIDPLUS, else every one so flagged in the folder.</summary>
        public async Task ExpungeAsync(long uid)
        {
            ImapResponse r = await RunAsync(Has("UIDPLUS") ? "UID EXPUNGE " + uid.ToString(CultureInfo.InvariantCulture) : "EXPUNGE");
            if (!r.Ok) throw new ImapException("The server wouldn't remove the message: " + Short(r.Reason));
        }

        public async Task LogoutAsync()
        {
            try { await RunAsync("LOGOUT"); } catch { }
        }

        // ---- the wire ----
        string NextTag() { return "V" + (++_tagNo).ToString("D3", CultureInfo.InvariantCulture); }

        /// <summary>A command: its tagged answer (the untagged lines are read and dropped).</summary>
        async Task<ImapResponse> RunAsync(string command)
        {
            List<ImapResponse> all = await RunAllAsync(command);
            return all[all.Count - 1];
        }

        /// <summary>A command: every line it answered with, the tagged one last.</summary>
        async Task<List<ImapResponse>> RunAllAsync(string command)
        {
            if (Broken) throw new ImapException("The connection to the server was lost.");
            string tag = NextTag();
            var all = new List<ImapResponse>();
            ImapResponse last;
            using (Timed())
            {
                await WriteAsync(tag + " " + command + "\r\n");
                last = await ReadTaggedAsync(tag, all);
            }
            all.Add(last);
            if (last.Bad) throw new ImapException("The server didn't understand a command (" + Short(last.Reason) + ").");
            return all;
        }

        /// <summary>Reads until the line tagged with ours (BYE from the server ends it too).</summary>
        async Task<ImapResponse> ReadTaggedAsync(string tag, List<ImapResponse> untagged)
        {
            while (true)
            {
                ImapResponse r = await ReadResponseAsync();
                if (r.Tag == tag) return r;
                if (r.Tag == "*" && r.Text.StartsWith("BYE", StringComparison.OrdinalIgnoreCase)) { Broken = true; throw new ImapException("The server closed the connection: " + Short(r.Text.Substring(3).Trim())); }
                if (untagged != null) untagged.Add(r);
            }
        }

        /// <summary>Waits for the server's "+" (go on with the literal).</summary>
        async Task ContinueAsync(string tag)
        {
            while (true)
            {
                ImapResponse r = await ReadResponseAsync();
                if (r.Tag == "+") return;
                if (r.Tag == tag) throw new ImapException("The server didn't accept the login: " + Short(r.Reason));
            }
        }

        async Task WriteAsync(string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            try { await _stream.WriteAsync(b, 0, b.Length, _ct); await _stream.FlushAsync(_ct); }
            catch (OperationCanceledException) { Broken = true; throw new ImapException("The server took too long to answer."); }
            catch (IOException ex) { Broken = true; throw new ImapException("The connection to the server was lost: " + ex.Message); }
        }

        async Task WriteAsync(byte[] data, string then)
        {
            try { await _stream.WriteAsync(data, 0, data.Length, _ct); }
            catch (OperationCanceledException) { Broken = true; throw new ImapException("The server took too long to answer."); }
            catch (IOException ex) { Broken = true; throw new ImapException("The connection to the server was lost: " + ex.Message); }
            await WriteAsync(then);
        }

        /// <summary>One response: a line, plus the bytes of each literal it ends with (and the line that goes on after it).</summary>
        async Task<ImapResponse> ReadResponseAsync()
        {
            var r = new ImapResponse();
            var sb = new StringBuilder();
            while (true)
            {
                string line = await ReadLineAsync();
                Match m = LiteralEnd.Match(line);
                if (!m.Success) { sb.Append(line); break; }
                sb.Append(line, 0, m.Index).Append('\u0001');
                int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (n > 4 * 1024 * 1024) throw new ImapException("The server sent more than expected (" + n + " bytes).");
                byte[] data = await ReadBytesAsync(n);
                r.Literals.Add(MailText.Bytes(data));
            }
            string full = sb.ToString();
            int sp = full.IndexOf(' ');
            r.Tag = sp < 0 ? full : full.Substring(0, sp);
            r.Text = sp < 0 ? "" : full.Substring(sp + 1);
            return r;
        }

        async Task<string> ReadLineAsync()
        {
            var ms = new MemoryStream();
            while (true)
            {
                int b = await ReadByteAsync();
                if (b < 0) throw new ImapException("The connection to the server was lost.");
                if (b == '\n') break;
                if (b != '\r') ms.WriteByte((byte)b);
                if (ms.Length > 1024 * 1024) throw new ImapException("The server sent a line that never ends.");
            }
            return Latin1.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        async Task<byte[]> ReadBytesAsync(int n)
        {
            var data = new byte[n];
            int got = 0;
            while (got < n)
            {
                if (_bufPos >= _bufLen && !await FillAsync()) throw new ImapException("The connection to the server was lost.");
                int take = Math.Min(n - got, _bufLen - _bufPos);
                Buffer.BlockCopy(_buf, _bufPos, data, got, take);
                _bufPos += take;
                got += take;
            }
            return data;
        }

        async Task<int> ReadByteAsync()
        {
            if (_bufPos >= _bufLen && !await FillAsync()) return -1;
            return _buf[_bufPos++];
        }

        async Task<bool> FillAsync()
        {
            _bufPos = 0;
            try { _bufLen = await _stream.ReadAsync(_buf, 0, _buf.Length, _ct); }
            catch (OperationCanceledException) { Broken = true; throw new ImapException("The server took too long to answer."); }
            catch (IOException ex) { Broken = true; throw new ImapException("The connection to the server was lost: " + ex.Message); }
            catch (ObjectDisposedException) { Broken = true; throw new ImapException("The connection to the server was lost."); }
            if (_bufLen <= 0) Broken = true;
            return _bufLen > 0;
        }

        /// <summary>A quoted string, or null when it needs a literal (8-bit, CR / LF).</summary>
        static string Quoted(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c > 127 || c == '\r' || c == '\n' || c == '\0') return null;
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string QuotedFolder(string folder)
        {
            string q = Quoted(MailText.ToUtf7(folder));
            return q ?? "\"INBOX\"";
        }

        static string Between(string s, string a, string b)
        {
            int i = s.IndexOf(a, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            i += a.Length;
            int j = s.IndexOf(b, i, StringComparison.Ordinal);
            return (j < 0 ? s.Substring(i) : s.Substring(i, j - i)).Trim();
        }

        static string Short(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }

        public void Dispose()
        {
            try { if (_stream != null) _stream.Dispose(); } catch { }
            try { if (_tcp != null) _tcp.Dispose(); } catch { }
        }
    }

    /// <summary>Mail's text: encoded words (RFC 2047), header lines, dates, and folder names (modified UTF-7).</summary>
    public static class MailText
    {
        static readonly Regex Word = new Regex(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", RegexOptions.Compiled);
        static readonly Regex BetweenWords = new Regex(@"(\?=)\s+(=\?)", RegexOptions.Compiled);

        static MailText()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        /// <summary>Header bytes as text: UTF-8 when they are, else Latin-1 (never a failure).</summary>
        public static string Bytes(byte[] data)
        {
            try { return new UTF8Encoding(false, true).GetString(data); }
            catch { return Encoding.Latin1.GetString(data); }
        }

        /// <summary>Header lines (folded ones joined) by lower-case name.</summary>
        public static Dictionary<string, string> Headers(string text)
        {
            var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string name = null;
            var value = new StringBuilder();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if ((line[0] == ' ' || line[0] == '\t') && name != null) { value.Append(' ').Append(line.Trim()); continue; }
                if (name != null) h[name] = value.ToString();
                int c = line.IndexOf(':');
                if (c <= 0) { name = null; continue; }
                name = line.Substring(0, c).Trim().ToLowerInvariant();
                value.Clear().Append(line.Substring(c + 1).Trim());
            }
            if (name != null) h[name] = value.ToString();
            return h;
        }

        /// <summary>=?UTF-8?B?...?= and =?iso-8859-1?Q?...?= words decoded.</summary>
        public static string Decode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("=?", StringComparison.Ordinal) < 0) return s ?? "";
            s = BetweenWords.Replace(s, "$1$2");        // the space between two encoded words isn't part of the text
            return Word.Replace(s, delegate (Match m)
            {
                try
                {
                    string cs = m.Groups[1].Value;
                    int star = cs.IndexOf('*');         // =?utf-8*en?...
                    if (star > 0) cs = cs.Substring(0, star);
                    Encoding enc = EncodingOf(cs);
                    byte[] bytes;
                    if (m.Groups[2].Value == "B" || m.Groups[2].Value == "b") bytes = Convert.FromBase64String(m.Groups[3].Value);
                    else
                    {
                        var ms = new MemoryStream();
                        string q = m.Groups[3].Value;
                        for (int i = 0; i < q.Length; i++)
                        {
                            char c = q[i];
                            if (c == '_') ms.WriteByte((byte)' ');
                            else if (c == '=' && IsHex(q, i + 1)) { ms.WriteByte(Convert.ToByte(q.Substring(i + 1, 2), 16)); i += 2; }
                            else ms.WriteByte((byte)c);
                        }
                        bytes = ms.ToArray();
                    }
                    return enc.GetString(bytes);
                }
                catch { return m.Value; }
            });
        }

        static bool IsHex(string s, int at)
        {
            return at + 1 < s.Length && Uri.IsHexDigit(s[at]) && Uri.IsHexDigit(s[at + 1]);
        }

        static Encoding EncodingOf(string name)
        {
            try { return Encoding.GetEncoding(name.Trim()); } catch { return Encoding.UTF8; }
        }

        /// <summary>"Jane Doe" from "Jane Doe &lt;jane@example.com&gt;" (the address when there's no name).</summary>
        public static string Sender(string from)
        {
            string s = (from ?? "").Trim();
            if (s.Length == 0) return "";
            int lt = s.IndexOf('<');
            if (lt >= 0)
            {
                string name = s.Substring(0, lt).Trim().Trim('"').Trim();
                if (name.Length > 0) return name;
                int gt = s.IndexOf('>', lt);
                return gt > lt ? s.Substring(lt + 1, gt - lt - 1).Trim() : s.Substring(lt + 1).Trim();
            }
            return s.Trim('"');
        }

        static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        /// <summary>IMAP's INTERNALDATE "01-Nov-2026 10:00:00 +1100" as UTC (MinValue when it can't be read).</summary>
        public static DateTime InternalDate(string s)
        {
            Match m = Regex.Match(s ?? "", @"^\s*(\d{1,2})-(\w{3})-(\d{4}) (\d\d):(\d\d):(\d\d) ([+-])(\d\d)(\d\d)");
            if (!m.Success) return DateTime.MinValue;
            int mon = Array.IndexOf(Months, m.Groups[2].Value.ToLowerInvariant());
            if (mon < 0) return DateTime.MinValue;
            try
            {
                var local = new DateTime(int.Parse(m.Groups[3].Value), mon + 1, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), DateTimeKind.Unspecified);
                var off = new TimeSpan(int.Parse(m.Groups[8].Value), int.Parse(m.Groups[9].Value), 0);
                if (m.Groups[7].Value == "-") off = -off;
                return new DateTimeOffset(local, off).UtcDateTime;
            }
            catch { return DateTime.MinValue; }
        }

        /// <summary>A Date: header ("Sun, 1 Nov 2026 10:00:00 +1100 (AEDT)") as UTC.</summary>
        public static DateTime HeaderDate(string s)
        {
            string t = Regex.Replace(s ?? "", @"\s*\([^)]*\)\s*$", "").Trim();
            t = Regex.Replace(t, @"([+-]\d\d)(\d\d)$", "$1:$2");
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out d)) return d.UtcDateTime;
            return DateTime.MinValue;
        }

        // ---- modified UTF-7 (RFC 3501 5.1.3): folder names ----
        public static string FromUtf7(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s ?? "";
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c != '&') { sb.Append(c); i++; continue; }
                int end = s.IndexOf('-', i + 1);
                if (end < 0) { sb.Append(s.Substring(i)); break; }
                if (end == i + 1) { sb.Append('&'); i = end + 1; continue; }
                string b64 = s.Substring(i + 1, end - i - 1).Replace(',', '/');
                while (b64.Length % 4 != 0) b64 += "=";
                try
                {
                    byte[] bytes = Convert.FromBase64String(b64);
                    sb.Append(Encoding.BigEndianUnicode.GetString(bytes, 0, bytes.Length - bytes.Length % 2));
                }
                catch { sb.Append(s, i, end - i + 1); }
                i = end + 1;
            }
            return sb.ToString();
        }

        public static string ToUtf7(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            var run = new StringBuilder();
            Action flush = delegate
            {
                if (run.Length == 0) return;
                string b64 = Convert.ToBase64String(Encoding.BigEndianUnicode.GetBytes(run.ToString())).TrimEnd('=').Replace('/', ',');
                sb.Append('&').Append(b64).Append('-');
                run.Clear();
            };
            foreach (char c in s)
            {
                if (c == '&') { flush(); sb.Append("&-"); }
                else if (c >= 0x20 && c <= 0x7E) { flush(); sb.Append(c); }
                else run.Append(c);
            }
            flush();
            return sb.ToString();
        }
    }
}
