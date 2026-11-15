using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using vShell.Core;

namespace Mail
{
    /// <summary>What a Google sign-in gave: the address signed in with, and the refresh token to keep.</summary>
    public sealed class GoogleSignIn
    {
        public string Email = "", RefreshToken = "";
    }

    /// <summary>
    /// Sign in with Google for Gmail accounts (OAuth 2.0 for a desktop app, with PKCE): the browser does the signing in and comes back to a
    /// page the plugin listens on at 127.0.0.1 for that moment. The refresh token is kept (encrypted) with the account; each check gets an
    /// access token with it and logs in to IMAP with XOAUTH2.
    /// </summary>
    public static class GoogleAuth
    {
        const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        const string TokenUrl = "https://oauth2.googleapis.com/token";
        /// <summary>Full IMAP access (the only scope Gmail's IMAP takes), and the address to show.</summary>
        const string Scope = "https://mail.google.com/ email";

        sealed class Token { public string Access = ""; public DateTime Until; }
        static readonly Dictionary<string, Token> Tokens = new Dictionary<string, Token>();
        static readonly object Gate = new object();

        static string _builtInId, _builtInSecret;

        /// <summary>
        /// The OAuth client vShell comes with (a "Desktop app" one - Google doesn't treat its secret as secret): stamped into the DLL at build
        /// time from plugins\Mail\google-client.txt ("id secret" on one line - not in the repository), so a build without the file has none.
        /// </summary>
        public static bool HasBuiltIn
        {
            get { BuiltIn(); return _builtInId.Length > 0; }
        }

        static void BuiltIn()
        {
            if (_builtInId != null) return;
            string id = "", secret = "";
            try
            {
                foreach (System.Reflection.AssemblyMetadataAttribute at in typeof(GoogleAuth).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false))
                {
                    if (at.Key != "GoogleClient") continue;
                    string[] parts = (at.Value ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) { id = parts[0]; secret = parts[1]; }
                }
            }
            catch { }
            _builtInSecret = secret;
            _builtInId = id;
        }

        /// <summary>The OAuth client in use: your own from the Plugins page, else the one vShell comes with.</summary>
        public static void ClientOf(MailSettings m, out string id, out string secret)
        {
            id = m != null ? m.GoogleClientId : "";
            secret = m != null ? m.GoogleClientSecret : "";
            if (id.Length > 0) return;
            BuiltIn();
            id = _builtInId; secret = _builtInSecret;
            if (id.Length == 0) throw new ImapException("This build of the Mail plugin has no Google OAuth client of its own - put your own client ID and secret under Google sign-in on the Plugins page.");
        }

        /// <summary>The browser's sign-in (up to five minutes): the address and the refresh token, or an exception saying what went wrong.</summary>
        public static async Task<GoogleSignIn> SignInAsync(MailSettings m)
        {
            string clientId, clientSecret;
            ClientOf(m, out clientId, out clientSecret);
            string verifier = Random(64), state = Random(24);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://127.0.0.1:" + port + "/";
            try
            {
                string url = AuthUrl + "?client_id=" + Uri.EscapeDataString(clientId) + "&redirect_uri=" + Uri.EscapeDataString(redirect) + "&response_type=code" +
                             "&scope=" + Uri.EscapeDataString(Scope) + "&code_challenge=" + challenge + "&code_challenge_method=S256&access_type=offline&prompt=consent" +
                             "&state=" + state;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { throw new ImapException("The browser couldn't be opened: " + ex.Message); }
                string code = await WaitForCodeAsync(listener, state, TimeSpan.FromMinutes(5));
                // the code for the tokens
                var form = new Dictionary<string, string>
                {
                    { "code", code }, { "client_id", clientId }, { "client_secret", clientSecret }, { "redirect_uri", redirect },
                    { "grant_type", "authorization_code" }, { "code_verifier", verifier }
                };
                JsonElement j = await PostAsync(form);
                string refresh = JsonValue.Str(j, "refresh_token"), access = JsonValue.Str(j, "access_token"), idToken = JsonValue.Str(j, "id_token");
                if (refresh.Length == 0) throw new ImapException("Google didn't give a lasting sign-in (no refresh token). Remove vShell under your Google account's third-party access and sign in again.");
                string email = EmailOf(idToken);
                if (email.Length == 0) throw new ImapException("Google didn't say which address was signed in.");
                return new GoogleSignIn { Email = email, RefreshToken = refresh };
            }
            finally { try { listener.Stop(); } catch { } }
        }

        /// <summary>An access token for the account (the one from before while it lasts, else a new one with the refresh token).</summary>
        public static async Task<string> AccessTokenAsync(MailAccount a, MailSettings m)
        {
            lock (Gate)
            {
                Token t;
                if (Tokens.TryGetValue(a.Id, out t) && t.Until > DateTime.UtcNow.AddSeconds(60)) return t.Access;
            }
            string refresh = Secret.Unprotect(a.RefreshToken);
            if (refresh.Length == 0) throw new ImapException("Not signed in with Google yet - Sign in with Google on the Plugins page.");
            string clientId, clientSecret;
            ClientOf(m, out clientId, out clientSecret);
            var form = new Dictionary<string, string> { { "refresh_token", refresh }, { "client_id", clientId }, { "client_secret", clientSecret }, { "grant_type", "refresh_token" } };
            JsonElement j = await PostAsync(form);
            string access = JsonValue.Str(j, "access_token");
            double secs = JsonValue.Num(j, "expires_in");
            if (access.Length == 0) throw new ImapException("Google didn't give an access token.");
            lock (Gate) Tokens[a.Id] = new Token { Access = access, Until = DateTime.UtcNow.AddSeconds(double.IsNaN(secs) || secs <= 0 ? 3000 : secs) };
            return access;
        }

        /// <summary>The account was signed out or signed in again: its token is no good any more.</summary>
        public static void Forget(string accountId) { lock (Gate) Tokens.Remove(accountId); }

        static async Task<JsonElement> PostAsync(Dictionary<string, string> form)
        {
            HttpClient http = PluginHttp.Client("mail-google", false, 30);
            string text;
            int code;
            try
            {
                using (HttpResponseMessage r = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form)))
                {
                    code = (int)r.StatusCode;
                    text = await r.Content.ReadAsStringAsync();
                }
            }
            catch (TaskCanceledException) { throw new ImapException("Google didn't answer in time."); }
            catch (HttpRequestException ex) { throw new ImapException("Can't reach Google: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
            JsonElement j;
            try { j = JsonDocument.Parse(text).RootElement.Clone(); }
            catch { throw new ImapException("Google answered " + code + " with something that isn't JSON."); }
            string err = JsonValue.Str(j, "error");
            if (code >= 400 || err.Length > 0)
            {
                string desc = JsonValue.Str(j, "error_description");
                if (err == "invalid_grant") throw new ImapException("The Google sign-in has expired or was taken away - Sign in with Google again on the Plugins page.");
                if (err == "invalid_client") throw new ImapException("Google doesn't know this OAuth client" + (desc.Length > 0 ? " (" + desc + ")" : "") + " - check the client ID and secret on the Plugins page.");
                throw new ImapException("Google said no: " + (desc.Length > 0 ? desc : err.Length > 0 ? err : code.ToString()));
            }
            return j;
        }

        /// <summary>Waits for the browser to come back with ?code=…&amp;state=… (a plain HTTP request on the loopback port), and answers it.</summary>
        static async Task<string> WaitForCodeAsync(TcpListener listener, string state, TimeSpan wait)
        {
            using (var cts = new CancellationTokenSource(wait))
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await listener.AcceptTcpClientAsync(cts.Token); }
                    catch (OperationCanceledException) { throw new ImapException("No sign-in came back from the browser within five minutes."); }
                    using (c)
                    using (NetworkStream s = c.GetStream())
                    {
                        string request = await ReadRequestAsync(s, cts.Token);
                        if (request == null) continue;
                        // "GET /?code=…&state=… HTTP/1.1"
                        int sp1 = request.IndexOf(' '), sp2 = sp1 < 0 ? -1 : request.IndexOf(' ', sp1 + 1);
                        string path = sp1 < 0 || sp2 < 0 ? "" : request.Substring(sp1 + 1, sp2 - sp1 - 1);
                        if (path == "/favicon.ico") { await AnswerAsync(s, 404, ""); continue; }
                        Dictionary<string, string> q = Query(path);
                        string code, err, st;
                        q.TryGetValue("code", out code); q.TryGetValue("error", out err); q.TryGetValue("state", out st);
                        if (st != state) { await AnswerAsync(s, 400, Page("That sign-in isn't the one vShell is waiting for.", false)); continue; }
                        if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(code))
                        {
                            string why = err == "access_denied" ? "The sign-in was cancelled." : "Google said: " + (err ?? "no code");
                            await AnswerAsync(s, 200, Page(why, false));
                            throw new ImapException(why);
                        }
                        await AnswerAsync(s, 200, Page("Signed in - you can close this tab and go back to vShell.", true));
                        return code;
                    }
                }
            }
        }

        static async Task<string> ReadRequestAsync(NetworkStream s, CancellationToken ct)
        {
            var buf = new byte[8192];
            var sb = new StringBuilder();
            while (sb.Length < 16384)
            {
                int n;
                try { n = await s.ReadAsync(buf, 0, buf.Length, ct); } catch { return null; }
                if (n <= 0) return null;
                sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                int end = sb.ToString().IndexOf("\r\n", StringComparison.Ordinal);
                if (end >= 0) return sb.ToString(0, end);
            }
            return null;
        }

        static async Task AnswerAsync(NetworkStream s, int code, string html)
        {
            byte[] body = Encoding.UTF8.GetBytes(html);
            string head = "HTTP/1.1 " + code + (code == 200 ? " OK" : code == 404 ? " Not Found" : " Bad Request") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length +
                          "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(head);
            try { await s.WriteAsync(h, 0, h.Length); await s.WriteAsync(body, 0, body.Length); await s.FlushAsync(); } catch { }
        }

        static string Page(string text, bool ok)
        {
            return "<!doctype html><html><head><meta charset='utf-8'><title>vShell</title></head><body style='font-family:Segoe UI,sans-serif;background:#1e1e1e;color:#eee;display:flex;" +
                   "align-items:center;justify-content:center;height:100vh;margin:0'><div style='text-align:center'><div style='font-size:40px'>" + (ok ? "&#10003;" : "&#10007;") +
                   "</div><p style='font-size:18px'>" + WebUtility.HtmlEncode(text) + "</p></div></body></html>";
        }

        static Dictionary<string, string> Query(string path)
        {
            var d = new Dictionary<string, string>();
            int q = path.IndexOf('?');
            if (q < 0) return d;
            foreach (string part in path.Substring(q + 1).Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                d[Uri.UnescapeDataString(part.Substring(0, eq))] = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
            }
            return d;
        }

        /// <summary>The address in an ID token (its middle part is base64url JSON with "email").</summary>
        static string EmailOf(string idToken)
        {
            try
            {
                string[] parts = (idToken ?? "").Split('.');
                if (parts.Length < 2) return "";
                string p = parts[1].Replace('-', '+').Replace('_', '/');
                while (p.Length % 4 != 0) p += "=";
                using (JsonDocument d = JsonDocument.Parse(Convert.FromBase64String(p))) return JsonValue.Str(d.RootElement, "email").Trim();
            }
            catch { return ""; }
        }

        static string Random(int bytes)
        {
            return Base64Url(RandomNumberGenerator.GetBytes(bytes));
        }

        static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
