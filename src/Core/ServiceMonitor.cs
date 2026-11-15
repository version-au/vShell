using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    public enum ServiceState { Unknown, Up, Slow, Down }

    public sealed class ServiceStatus
    {
        public ServiceState State = ServiceState.Unknown;
        public int Millis;
        public string Detail = "waiting for the first check";
        public DateTime Checked = DateTime.MinValue;
    }

    /// <summary>
    /// Background reachability checks for the "Service status" module. Modules call Status() every second; a check runs when it is due
    /// and somebody is still asking for it, on the thread pool, never on the UI thread.
    /// </summary>
    public static class ServiceMonitor
    {
        sealed class Entry
        {
            public ServiceCheck Check;
            public int Interval;
            public DateTime LastAsked;
            public DateTime LastStarted = DateTime.MinValue;
            public bool Running;
            public ServiceStatus Status = new ServiceStatus();
        }

        static readonly object Gate = new object();
        static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        static Timer _timer;
        static HttpClient _http, _httpInsecure;

        static string KeyOf(ServiceCheck c) { return c.Kind + "|" + c.Target + "|" + c.IgnoreCert; }

        public static ServiceStatus Status(ServiceCheck c, int intervalSeconds)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Target)) return new ServiceStatus { Detail = "no address set" };
            lock (Gate)
            {
                Entry e;
                string key = KeyOf(c);
                if (!Entries.TryGetValue(key, out e)) { e = new Entry { Check = c }; Entries[key] = e; }
                e.Check = c;
                e.Interval = Math.Max(5, intervalSeconds);
                e.LastAsked = DateTime.UtcNow;
                if (_timer == null) _timer = new Timer(delegate { Tick(); }, null, 200, 1000);
                return e.Status;
            }
        }

        static void Tick()
        {
            var due = new List<Entry>();
            lock (Gate)
            {
                var gone = new List<string>();
                foreach (var kv in Entries)
                {
                    Entry e = kv.Value;
                    if ((DateTime.UtcNow - e.LastAsked).TotalSeconds > 30) { gone.Add(kv.Key); continue; }
                    if (!e.Running && (DateTime.UtcNow - e.LastStarted).TotalSeconds >= e.Interval) { e.Running = true; e.LastStarted = DateTime.UtcNow; due.Add(e); }
                }
                foreach (string k in gone) Entries.Remove(k);
                // nobody asks any more: stop ticking (the next Status() starts it again)
                if (Entries.Count == 0 && _timer != null) { try { _timer.Dispose(); } catch { } _timer = null; }
            }
            foreach (Entry e in due)
            {
                Entry cap = e;
                Task.Run(async delegate
                {
                    ServiceStatus s;
                    try { s = await Run(cap.Check); }
                    catch (Exception ex) { s = new ServiceStatus { State = ServiceState.Down, Detail = ex.Message, Checked = DateTime.Now }; }
                    lock (Gate) { cap.Status = s; cap.Running = false; }
                });
            }
        }

        static async Task<ServiceStatus> Run(ServiceCheck c)
        {
            var sw = Stopwatch.StartNew();
            var s = new ServiceStatus { Checked = DateTime.Now };
            string target = c.Target.Trim();
            try
            {
                switch (c.Kind)
                {
                    case "Ping":
                        {
                            using (var ping = new Ping())
                            {
                                PingReply r = await ping.SendPingAsync(target, 3000);
                                if (r.Status == IPStatus.Success) { s.State = ServiceState.Up; s.Millis = (int)r.RoundtripTime; s.Detail = "ping " + r.RoundtripTime + " ms"; }
                                else { s.State = ServiceState.Down; s.Detail = "ping: " + r.Status; }
                            }
                            break;
                        }
                    case "Tcp":
                        {
                            string host = target; int port = 0;
                            int i = target.LastIndexOf(':');
                            if (i > 0 && int.TryParse(target.Substring(i + 1), out port)) host = target.Substring(0, i);
                            if (port <= 0) { s.State = ServiceState.Down; s.Detail = "use host:port"; break; }
                            using (var tcp = new TcpClient())
                            {
                                Task conn = tcp.ConnectAsync(host, port);
                                Task done = await Task.WhenAny(conn, Task.Delay(3000));
                                if (done == conn && tcp.Connected) { s.State = ServiceState.Up; s.Millis = (int)sw.ElapsedMilliseconds; s.Detail = "port " + port + " open, " + s.Millis + " ms"; }
                                else { s.State = ServiceState.Down; s.Detail = done == conn ? "connection refused" : "timed out"; }
                            }
                            break;
                        }
                    default:
                        {
                            string url = target.IndexOf("://", StringComparison.Ordinal) < 0 ? "http://" + target : target;
                            HttpClient http = Client(c.IgnoreCert);
                            using (var cts = new CancellationTokenSource(5000))
                            using (HttpResponseMessage r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                            {
                                s.Millis = (int)sw.ElapsedMilliseconds;
                                int code = (int)r.StatusCode;
                                s.State = code < 500 ? ServiceState.Up : ServiceState.Down;
                                s.Detail = "HTTP " + code + ", " + s.Millis + " ms";
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                s.State = ServiceState.Down;
                Exception inner = ex.InnerException ?? ex;
                s.Detail = ex is TaskCanceledException || ex is OperationCanceledException ? "timed out" : inner.Message;
            }
            if (s.State == ServiceState.Up && s.Millis > 1500) s.State = ServiceState.Slow;
            s.Checked = DateTime.Now;
            return s;
        }

        static HttpClient Client(bool insecure)
        {
            lock (Gate)
            {
                if (insecure)
                {
                    if (_httpInsecure == null)
                    {
                        var h = new HttpClientHandler { AllowAutoRedirect = false };
                        h.ServerCertificateCustomValidationCallback = delegate { return true; };
                        _httpInsecure = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(6) };
                    }
                    return _httpInsecure;
                }
                if (_http == null) _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(6) };
                return _http;
            }
        }
    }
}
