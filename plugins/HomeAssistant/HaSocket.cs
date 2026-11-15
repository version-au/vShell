using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>
    /// Live Home Assistant states over its WebSocket API ("subscribe_entities"): HA sends a change the moment it happens, for just the
    /// entities vShell shows, instead of vShell asking for each of them every few seconds. While it is connected, HaClient stops polling
    /// those entities; if it can't connect (old HA, proxy without WebSocket, network down) the normal polling simply carries on.
    /// </summary>
    static class HaSocket
    {
        static CancellationTokenSource _cts;
        static int _generation;

        /// <summary>Entities whose state currently arrives over the socket (HaClient does not poll these).</summary>
        public static readonly HashSet<string> Live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static volatile bool Connected;

        // the connected session, for service calls (null while not connected); message ids count up across sessions, as HA wants
        static volatile ClientWebSocket _live;
        static SemaphoreSlim _liveLock;
        static CancellationToken _liveCt;
        static int _ids;
        /// <summary>Service calls waiting for their answer: id -> "" done, or Home Assistant's error.</summary>
        static readonly Dictionary<int, TaskCompletionSource<string>> Calls = new Dictionary<int, TaskCompletionSource<string>>();

        /// <summary>
        /// Call a service over the socket (a click on a light, the climate's target …) - quicker than a new HTTP request each time.
        /// null = not connected, nothing was sent (REST can do it); "" = done; anything else = Home Assistant's error.
        /// </summary>
        public static async Task<string> CallService(string domain, string service, string entityId, Dictionary<string, object> data)
        {
            ClientWebSocket ws = _live;
            SemaphoreSlim gate = _liveLock;
            CancellationToken ct = _liveCt;
            if (ws == null || gate == null || !Connected || ws.State != WebSocketState.Open) return null;
            int id = Interlocked.Increment(ref _ids);
            var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Calls) Calls[id] = answer;
            var msg = new Dictionary<string, object>
            {
                { "id", id }, { "type", "call_service" }, { "domain", domain }, { "service", service },
                { "target", new Dictionary<string, object> { { "entity_id", entityId } } }
            };
            if (data != null && data.Count > 0) msg["service_data"] = data;
            try { await Send(ws, JsonSerializer.Serialize(msg), ct, gate); }
            catch
            {
                lock (Calls) Calls.Remove(id);
                return null;        // it didn't go out: REST can do it
            }
            // sent: never send it again over REST (a toggle would toggle twice), just say if no answer came
            if (await Task.WhenAny(answer.Task, Task.Delay(8000)) != answer.Task)
            {
                lock (Calls) Calls.Remove(id);
                return "no answer from Home Assistant";
            }
            return answer.Task.Result;
        }

        /// <summary>(Re)start for a new address / token; null url = stop.</summary>
        public static void Restart(string url, string token, bool ignoreCert)
        {
            if (_cts != null) { try { _cts.Cancel(); } catch { } _cts = null; }
            Connected = false;
            _live = null;
            lock (Live) Live.Clear();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token)) return;
            var cts = new CancellationTokenSource();
            _cts = cts;
            int gen = Interlocked.Increment(ref _generation);
            Task.Run(delegate { return Run(url, token, ignoreCert, cts.Token, gen); });
        }

        static Uri SocketUri(string url)
        {
            string u = url.TrimEnd('/');
            if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = "wss://" + u.Substring(8);
            else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = "ws://" + u.Substring(7);
            return new Uri(u + "/api/websocket");
        }

        static async Task Run(string url, string token, bool ignoreCert, CancellationToken ct, int gen)
        {
            int delay = 5;
            while (!ct.IsCancellationRequested)
            {
                bool authFailed = false, unsupported = false;
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        if (ignoreCert) ws.Options.RemoteCertificateValidationCallback = delegate { return true; };
                        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                            await ws.ConnectAsync(SocketUri(url), connectTimeout.Token);
                        }

                        // auth_required -> auth -> auth_ok
                        await Receive(ws, ct);
                        await Send(ws, JsonSerializer.Serialize(new Dictionary<string, string> { { "type", "auth" }, { "access_token", token } }), ct, null);
                        using (JsonDocument auth = JsonDocument.Parse(await Receive(ws, ct)))
                        {
                            string type = Str(auth.RootElement, "type");
                            if (type != "auth_ok") { authFailed = true; throw new InvalidOperationException("WebSocket login: " + type); }
                        }
                        delay = 5;
                        await Session(ws, ct, gen, out_unsupported => unsupported = out_unsupported);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!ct.IsCancellationRequested) Log.Write("HA WebSocket: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
                finally
                {
                    if (gen == _generation)
                    {
                        Connected = false;
                        lock (Live) Live.Clear();     // polling takes over again
                    }
                }
                if (ct.IsCancellationRequested) return;
                if (authFailed || unsupported) return;        // polling only, until the settings change
                try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch { return; }
                delay = Math.Min(120, delay * 2);
            }
        }

        /// <summary>One connected session: keep the subscription matching the wanted entities and apply the updates.</summary>
        static async Task Session(ClientWebSocket ws, CancellationToken ct, int gen, Action<bool> setUnsupported)
        {
            var sendLock = new SemaphoreSlim(1);
            int subId = 0;
            string subscribedKey = null;
            var pendingResult = new Dictionary<int, string>();     // command id -> what it was
            Connected = true;

            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                if (gen == _generation) { _liveLock = sendLock; _liveCt = stop.Token; _live = ws; }
                // every 2 s: subscribe again if the set of wanted entities changed
                Task watcher = Task.Run(async delegate
                {
                    while (!stop.IsCancellationRequested)
                    {
                        List<string> want = HaClient.WantedIds();
                        want.Sort(StringComparer.OrdinalIgnoreCase);
                        string key = string.Join(",", want);
                        if (key != subscribedKey && want.Count > 0)
                        {
                            int old = subId;
                            int id;
                            id = Interlocked.Increment(ref _ids);
                            lock (pendingResult) pendingResult[id] = "sub";
                            if (old != 0)
                            {
                                int uid = Interlocked.Increment(ref _ids);
                                lock (pendingResult) pendingResult[uid] = "unsub";
                                await Send(ws, "{\"id\":" + uid + ",\"type\":\"unsubscribe_events\",\"subscription\":" + old + "}", stop.Token, sendLock);
                            }
                            subId = id;
                            subscribedKey = key;
                            lock (Live) Live.Clear();
                            await Send(ws, JsonSerializer.Serialize(new Dictionary<string, object> { { "id", id }, { "type", "subscribe_entities" }, { "entity_ids", want } }), stop.Token, sendLock);
                        }
                        try { await Task.Delay(2000, stop.Token); } catch { return; }
                    }
                });

                try
                {
                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        string msg = await Receive(ws, ct);
                        using (JsonDocument d = JsonDocument.Parse(msg))
                        {
                            JsonElement root = d.RootElement;
                            string type = Str(root, "type");
                            int id = root.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : 0;
                            if (type == "result")
                            {
                                // a service call's answer
                                TaskCompletionSource<string> call;
                                lock (Calls) { if (Calls.TryGetValue(id, out call)) Calls.Remove(id); }
                                if (call != null)
                                {
                                    bool done = root.TryGetProperty("success", out JsonElement cs) && cs.ValueKind == JsonValueKind.True;
                                    string why = "failed";
                                    if (!done && root.TryGetProperty("error", out JsonElement err)) { string m = Str(err, "message"); if (m.Length > 0) why = m; }
                                    call.TrySetResult(done ? "" : why);
                                    continue;
                                }
                                string what;
                                lock (pendingResult) { pendingResult.TryGetValue(id, out what); pendingResult.Remove(id); }
                                bool ok = root.TryGetProperty("success", out JsonElement s) && s.ValueKind == JsonValueKind.True;
                                if (what == "sub" && !ok)
                                {
                                    // this Home Assistant doesn't know subscribe_entities (too old): stay with polling
                                    Log.Write("HA WebSocket: subscribe_entities not supported - polling instead");
                                    setUnsupported(true);
                                    return;
                                }
                            }
                            else if (type == "event" && id == subId && gen == _generation && root.TryGetProperty("event", out JsonElement ev))
                                Apply(ev);
                        }
                    }
                }
                finally
                {
                    if (_live == ws) _live = null;
                    // calls still waiting were sent - their answer is lost with the connection (not sent again: a toggle would toggle twice)
                    List<TaskCompletionSource<string>> waiting;
                    lock (Calls) { waiting = new List<TaskCompletionSource<string>>(Calls.Values); Calls.Clear(); }
                    foreach (var w in waiting) w.TrySetResult("the connection to Home Assistant dropped before it answered");
                    stop.Cancel();
                    try { await watcher; } catch { }
                }
            }
        }

        /// <summary>The compressed state format of subscribe_entities: "a" = added (full), "c" = changed ("+" new values, "-" removed attributes), "r" = removed.</summary>
        static void Apply(JsonElement ev)
        {
            if (ev.TryGetProperty("a", out JsonElement added) && added.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in added.EnumerateObject())
                {
                    string state = Str(p.Value, "s");
                    string unit = "", name = "";
                    var set = new Dictionary<string, string>();
                    if (p.Value.TryGetProperty("a", out JsonElement attrs) && attrs.ValueKind == JsonValueKind.Object)
                    {
                        unit = Str(attrs, "unit_of_measurement");
                        name = Str(attrs, "friendly_name");
                        foreach (JsonProperty a in attrs.EnumerateObject()) set[a.Name] = a.Value.GetRawText();
                    }
                    HaClient.PutLive(p.Name, state, unit, name, true, true, set);
                    lock (Live) Live.Add(p.Name);
                }
            if (ev.TryGetProperty("c", out JsonElement changed) && changed.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in changed.EnumerateObject())
                {
                    string state = null, unit = null, name = null;
                    Dictionary<string, string> set = null;
                    List<string> goneList = null;
                    if (p.Value.TryGetProperty("+", out JsonElement plus) && plus.ValueKind == JsonValueKind.Object)
                    {
                        if (plus.TryGetProperty("s", out JsonElement s) && s.ValueKind == JsonValueKind.String) state = s.GetString();
                        if (plus.TryGetProperty("a", out JsonElement attrs) && attrs.ValueKind == JsonValueKind.Object)
                        {
                            set = new Dictionary<string, string>();
                            foreach (JsonProperty a in attrs.EnumerateObject()) set[a.Name] = a.Value.GetRawText();
                            if (attrs.TryGetProperty("unit_of_measurement", out JsonElement u) && u.ValueKind == JsonValueKind.String) unit = u.GetString();
                            if (attrs.TryGetProperty("friendly_name", out JsonElement f) && f.ValueKind == JsonValueKind.String) name = f.GetString();
                        }
                    }
                    if (p.Value.TryGetProperty("-", out JsonElement minus) && minus.TryGetProperty("a", out JsonElement gone) && gone.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement g in gone.EnumerateArray())
                        {
                            if (g.ValueKind != JsonValueKind.String) continue;
                            if (goneList == null) goneList = new List<string>();
                            goneList.Add(g.GetString());
                            if (g.GetString() == "unit_of_measurement") unit = "";
                            if (g.GetString() == "friendly_name") name = "";
                        }
                    HaClient.PutLive(p.Name, state, unit, name, false, false, set, goneList);
                }
            if (ev.TryGetProperty("r", out JsonElement removed) && removed.ValueKind == JsonValueKind.Array)
                foreach (JsonElement r in removed.EnumerateArray())
                {
                    string id = r.GetString();
                    if (id == null) continue;
                    HaClient.PutLive(id, "unavailable", null, null, false, false);
                }
        }

        static string Str(JsonElement e, string name)
        {
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static async Task Send(ClientWebSocket ws, string text, CancellationToken ct, SemaphoreSlim gate)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            if (gate != null) await gate.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, ct); }
            finally { if (gate != null) gate.Release(); }
        }

        static async Task<string> Receive(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[16384];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) throw new IOException("Home Assistant closed the WebSocket");
                    ms.Write(buf, 0, r.Count);
                    if (r.EndOfMessage) break;
                    if (ms.Length > 32 * 1024 * 1024) throw new IOException("WebSocket message too large");
                }
                return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }
    }
}
