using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>
    /// Notifications sent to vShell: a tiny web server on the chosen port that takes
    ///   POST /notify  {"title": "...", "message": "...", "url": "...", "level": "normal|important|alert"}   ("important": true = level important)
    /// (or GET /notify?title=...&amp;message=...) with "Authorization: Bearer &lt;token&gt;" or ?token=. Home Assistant's rest_command can
    /// call it from any automation. A plain TcpListener, so it needs no admin rights (Windows asks once whether to let it through the firewall).
    /// </summary>
    public static class NotifyListener
    {
        static TcpListener _listener;
        static int _port;
        static string _token = "";
        static CancellationTokenSource _cts;
        static readonly object Gate = new object();

        /// <summary>Why it isn't listening ("" = listening or off).</summary>
        public static string Problem { get; private set; } = "";
        public static bool Listening { get { lock (Gate) return _listener != null; } }

        public static string NewToken()
        {
            byte[] b = new byte[18];
            RandomNumberGenerator.Fill(b);
            return Convert.ToBase64String(b).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
        }

        public static void Configure(BarSettings s)
        {
            bool on = s != null && s.NotifyListen;
            string token = on ? Secret.Unprotect(s.NotifyListenToken) : "";
            int port = on ? s.NotifyListenPort : 0;
            lock (Gate)
            {
                if (on && _listener != null && port == _port && token == _token) return;
                if (!on && _listener == null) { Problem = ""; return; }
                Stop();
                if (!on) return;
                if (token.Length == 0) { Problem = "No token yet - press New token."; return; }
                try
                {
                    var l = new TcpListener(IPAddress.Any, port);
                    l.Start();
                    _listener = l; _port = port; _token = token;
                    _cts = new CancellationTokenSource();
                    CancellationToken ct = _cts.Token;
                    Task.Run(delegate { return Loop(l, ct); });
                    Problem = "";
                    Log.Write("Notifications: listening on port " + port);
                }
                catch (Exception ex)
                {
                    Problem = "Can't listen on port " + port + ": " + ex.Message;
                    Log.Write("Notifications: " + Problem);
                }
            }
        }

        static void Stop()
        {
            if (_cts != null) { try { _cts.Cancel(); } catch { } _cts = null; }
            if (_listener != null) { try { _listener.Stop(); } catch { } _listener = null; }
        }

        static async Task Loop(TcpListener l, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await l.AcceptTcpClientAsync(ct); }
                catch { return; }
                TcpClient cap = c;
                _ = Task.Run(delegate { return Serve(cap); });
            }
        }

        static async Task Serve(TcpClient c)
        {
            using (c)
            {
                c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
                NetworkStream ns = c.GetStream();
                int code; string body;
                try
                {
                    var buf = new MemoryStream();
                    var chunk = new byte[4096];
                    int headerEnd = -1;
                    using (var cts = new CancellationTokenSource(5000))
                    {
                        while (headerEnd < 0)
                        {
                            int n = await ns.ReadAsync(chunk, 0, chunk.Length, cts.Token);
                            if (n <= 0) return;
                            buf.Write(chunk, 0, n);
                            if (buf.Length > 65536) { await Reply(ns, 413, "{\"ok\":false,\"error\":\"too big\"}"); return; }
                            headerEnd = Find(buf.GetBuffer(), (int)buf.Length);
                        }
                        string head = Encoding.UTF8.GetString(buf.GetBuffer(), 0, headerEnd);
                        int length = 0;
                        bool chunked = false, expect = false;
                        foreach (string line in head.Split('\n'))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out length);
                            else if (line.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) && line.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) chunked = true;
                            else if (line.StartsWith("Expect:", StringComparison.OrdinalIgnoreCase) && line.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0) expect = true;
                        }
                        // some senders wait to be told to go ahead before sending the body
                        if (expect && buf.Length == headerEnd + 4)
                        {
                            byte[] go = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                            await ns.WriteAsync(go, 0, go.Length);
                        }
                        string content;
                        if (chunked)
                        {
                            // a body sent in pieces (no Content-Length): read until the empty last piece, then join them
                            while (!EndsChunked(buf.GetBuffer(), headerEnd + 4, (int)buf.Length) && buf.Length < 70000)
                            {
                                int n = await ns.ReadAsync(chunk, 0, chunk.Length, cts.Token);
                                if (n <= 0) break;
                                buf.Write(chunk, 0, n);
                            }
                            content = Dechunk(buf.GetBuffer(), headerEnd + 4, (int)buf.Length);
                        }
                        else
                        {
                            length = Math.Max(0, Math.Min(65536, length));
                            while (buf.Length - (headerEnd + 4) < length)
                            {
                                int n = await ns.ReadAsync(chunk, 0, chunk.Length, cts.Token);
                                if (n <= 0) break;
                                buf.Write(chunk, 0, n);
                            }
                            content = Encoding.UTF8.GetString(buf.GetBuffer(), headerEnd + 4, (int)Math.Min(length, buf.Length - headerEnd - 4));
                        }
                        Handle(head, content, out code, out body);
                    }
                }
                catch (Exception ex) { code = 400; body = "{\"ok\":false,\"error\":" + JsonSerializer.Serialize(ex.Message) + "}"; }
                try { await Reply(ns, code, body); } catch { }
            }
        }

        static bool EndsChunked(byte[] b, int start, int len)
        {
            string s = Encoding.ASCII.GetString(b, start, Math.Max(0, len - start));
            return s.StartsWith("0\r\n", StringComparison.Ordinal) || s.Contains("\r\n0\r\n\r\n") || (s.StartsWith("0\r\n\r\n", StringComparison.Ordinal));
        }

        /// <summary>Join a chunked body ("size\r\ndata\r\n ... 0\r\n\r\n").</summary>
        public static string Dechunk(byte[] b, int start, int len)
        {
            var outp = new MemoryStream();
            int i = start;
            while (i < len)
            {
                int eol = -1;
                for (int k = i; k + 1 < len; k++) if (b[k] == 13 && b[k + 1] == 10) { eol = k; break; }
                if (eol < 0) break;
                string sz = Encoding.ASCII.GetString(b, i, eol - i).Split(';')[0].Trim();
                int size;
                if (!int.TryParse(sz, System.Globalization.NumberStyles.HexNumber, null, out size) || size <= 0) break;
                int dataStart = eol + 2;
                int take = Math.Min(size, len - dataStart);
                if (take <= 0) break;
                outp.Write(b, dataStart, take);
                i = dataStart + size + 2;
            }
            return Encoding.UTF8.GetString(outp.ToArray());
        }

        static int Find(byte[] b, int len)
        {
            for (int i = 0; i + 3 < len; i++) if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
            return -1;
        }

        /// <summary>Work out the request (public so it can be tested): code + JSON answer; posts the notification when it is fine.</summary>
        public static void Handle(string head, string content, out int code, out string body)
        {
            string[] lines = head.Replace("\r", "").Split('\n');
            string[] first = lines[0].Split(' ');
            string method = first.Length > 0 ? first[0].ToUpperInvariant() : "";
            string target = first.Length > 1 ? first[1] : "/";
            string path = target, query = "";
            int q = target.IndexOf('?');
            if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int e = pair.IndexOf('=');
                string k = Uri.UnescapeDataString((e >= 0 ? pair.Substring(0, e) : pair).Replace('+', ' '));
                string v = e >= 0 ? Uri.UnescapeDataString(pair.Substring(e + 1).Replace('+', ' ')) : "";
                args[k] = v;
            }
            string auth = "";
            foreach (string l in lines)
                if (l.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) auth = l.Substring(14).Trim();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) auth = auth.Substring(7).Trim();
            string tok;
            if (auth.Length == 0 && args.TryGetValue("token", out tok)) auth = tok;

            if (!path.TrimEnd('/').Equals("/notify", StringComparison.OrdinalIgnoreCase)) { code = 404; body = "{\"ok\":false,\"error\":\"use /notify\"}"; return; }
            string want;
            lock (Gate) want = _token;
            if (want.Length == 0 || !FixedEquals(auth, want)) { code = 401; body = "{\"ok\":false,\"error\":\"wrong or missing token\"}"; return; }

            string title = "", message = "", url = "", source = "";
            bool important = false;
            string level = "";
            string trimmed = (content ?? "").Trim();
            bool hasBody = method != "GET" && trimmed.Length > 0;
            string ctype = "";
            foreach (string l in lines)
                if (l.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)) ctype = l.Substring(13).Trim().ToLowerInvariant();
            // a form body (title=...&message=...) reads like the query
            if (hasBody && !trimmed.StartsWith("{") && (ctype.StartsWith("application/x-www-form-urlencoded") || (trimmed.Contains("=") && !trimmed.Contains(" ") && !trimmed.Contains("\n"))))
            {
                foreach (string pair in trimmed.Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int e = pair.IndexOf('=');
                    string k = Uri.UnescapeDataString((e >= 0 ? pair.Substring(0, e) : pair).Replace('+', ' '));
                    string v = e >= 0 ? Uri.UnescapeDataString(pair.Substring(e + 1).Replace('+', ' ')) : "";
                    if (!k.Equals("token", StringComparison.OrdinalIgnoreCase)) args[k] = v;
                }
                hasBody = false;
            }
            if (hasBody && trimmed.StartsWith("{"))
            {
                using (JsonDocument d = JsonDocument.Parse(trimmed))
                {
                    JsonElement r = d.RootElement, v;
                    if (r.TryGetProperty("title", out v) && v.ValueKind == JsonValueKind.String) title = v.GetString();
                    if (r.TryGetProperty("message", out v) && v.ValueKind == JsonValueKind.String) message = v.GetString();
                    // other common names: body / text / content (Discord) / description
                    foreach (string alt in new[] { "body", "text", "content", "description" })
                        if (string.IsNullOrEmpty(message) && r.TryGetProperty(alt, out v) && v.ValueKind == JsonValueKind.String) message = v.GetString();
                    if (r.TryGetProperty("priority", out v) && string.IsNullOrEmpty(level))
                    {
                        // Gotify / ntfy style priority: high numbers / words = louder
                        string pr = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                        level = PriorityLevel(pr);
                    }
                    if (r.TryGetProperty("url", out v) && v.ValueKind == JsonValueKind.String) url = v.GetString();
                    if (r.TryGetProperty("source", out v) && v.ValueKind == JsonValueKind.String) source = v.GetString();
                    if (r.TryGetProperty("important", out v)) important = v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true");
                    if (r.TryGetProperty("level", out v) && v.ValueKind == JsonValueKind.String) level = v.GetString();
                }
            }
            else if (hasBody)
            {
                // a plain text body: the first line is the title, the rest the message
                string[] parts = trimmed.Replace("\r", "").Split(new[] { '\n' }, 2);
                title = parts[0];
                message = parts.Length > 1 ? parts[1] : "";
                string at;
                if (args.TryGetValue("title", out at) && at.Length > 0) { message = trimmed; title = at; }
                args.TryGetValue("source", out source);
                args.TryGetValue("level", out level);
            }
            else
            {
                args.TryGetValue("title", out title);
                args.TryGetValue("message", out message);
                args.TryGetValue("url", out url);
                args.TryGetValue("source", out source);
                string imp;
                important = args.TryGetValue("important", out imp) && (imp == "1" || imp.Equals("true", StringComparison.OrdinalIgnoreCase));
                args.TryGetValue("level", out level);
            }
            title = (title ?? "").Trim(); message = (message ?? "").Trim(); url = (url ?? "").Trim();
            if (title.Length == 0 && message.Length == 0) { code = 400; body = "{\"ok\":false,\"error\":\"give a title or a message\"}"; return; }
            if (title.Length == 0) { title = message; message = ""; }
            // only web pages may be opened from a sent notification
            if (url.Length > 0 && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = "";
            if (title.Length > 200) title = title.Substring(0, 200);
            if (message.Length > 1000) message = message.Substring(0, 1000);
            string src = string.IsNullOrWhiteSpace(source) ? "Home Assistant" : source.Trim();
            if (src.Length > 40) src = src.Substring(0, 40);
            NotifyLevel lv;
            if (!Enum.TryParse((level ?? "").Trim(), true, out lv) || !Enum.IsDefined(typeof(NotifyLevel), lv)) lv = important ? NotifyLevel.Important : NotifyLevel.Normal;
            Notify.Post(src, title, message, url, lv);
            code = 200; body = "{\"ok\":true}";
        }

        /// <summary>A priority (Gotify 0 - 10, ntfy 1 - 5 / min..urgent, or words) as a level.</summary>
        static string PriorityLevel(string p)
        {
            p = (p ?? "").Trim().ToLowerInvariant();
            double n;
            if (double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n))
                return n >= 8 ? "alert" : n >= 4 ? "important" : "normal";
            switch (p)
            {
                case "urgent": case "max": case "critical": case "emergency": case "alert": case "error": return "alert";
                case "high": case "important": case "warning": case "warn": return "important";
                default: return "";
            }
        }

        static bool FixedEquals(string a, string b)
        {
            byte[] x = Encoding.UTF8.GetBytes(a ?? ""), y = Encoding.UTF8.GetBytes(b ?? "");
            return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
        }

        static async Task Reply(NetworkStream ns, int code, string body)
        {
            string reason = code == 200 ? "OK" : code == 401 ? "Unauthorized" : code == 404 ? "Not Found" : code == 413 ? "Payload Too Large" : "Bad Request";
            byte[] b = Encoding.UTF8.GetBytes(body);
            string head = "HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: application/json\r\nContent-Length: " + b.Length + "\r\nConnection: close\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(head);
            await ns.WriteAsync(h, 0, h.Length);
            await ns.WriteAsync(b, 0, b.Length);
        }
    }
}
