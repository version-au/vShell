using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using vShell.Core;

namespace LocalAi
{
    /// <summary>A server from the settings: the main one (its own fields) or one of More servers.</summary>
    public sealed class ServerConfig
    {
        public string Name = "", Url = "", Key = "";
        public bool Main;
        public string Id { get { return (Url ?? "").Trim().TrimEnd('/').ToLowerInvariant(); } }
    }

    public sealed class ModelInfo
    {
        public string Id = "";
        public double SizeGb;
        public bool Loaded;
    }

    public sealed class LoadedModel
    {
        public string Name = "", Device = "";
        public int Ctx;
        public bool Busy;
        public double SizeGb, VramGb;
    }

    /// <summary>What one server said on one look - never changed once made (the modules draw it on the UI thread).</summary>
    public sealed class ServerState
    {
        public ServerConfig Config = new ServerConfig();
        /// <summary>"lemonade", "ollama", "lmstudio", "openai" - "" until it's been reached.</summary>
        public string Kind = "", Version = "";
        public bool Online, Checked;
        public string Error = "";
        public DateTime CheckedUtc, SinceUtc;
        public List<LoadedModel> Loaded = new List<LoadedModel>();
        public List<ModelInfo> Models = new List<ModelInfo>();
        /// <summary>The last answer's speed (Lemonade's own figures, else the last question asked from vShell).</summary>
        public double Tps, TtftSeconds;
        public bool HasSpeed;
        /// <summary>Lemonade's totals since it started.</summary>
        public long Requests, TokensIn, TokensOut;
        public bool HasTotals;
        public string Hardware = "";
        public long StorageFree, StorageTotal;
        /// <summary>Recent answers' speeds (tokens a second), oldest first.</summary>
        public List<double> Speed = new List<double>();

        public string Name { get { return Config.Name; } }
        public bool Busy { get { return Loaded.Any(l => l.Busy); } }
        public bool CanLoad { get { return Kind == "lemonade" || Kind == "ollama"; } }
        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case "lemonade": return "Lemonade";
                    case "ollama": return "Ollama";
                    case "lmstudio": return "LM Studio";
                    case "openai": return "OpenAI-style";
                    default: return "";
                }
            }
        }
        /// <summary>The model a question goes to: the one picked, else the loaded one, else the first there is.</summary>
        public string ModelFor(string picked)
        {
            if (!string.IsNullOrEmpty(picked)) return picked;
            if (Loaded.Count > 0) return Loaded[0].Name;
            return Models.Count > 0 ? Models[0].Id : "";
        }
    }

    /// <summary>
    /// One server: works out what it is (Lemonade, Ollama, LM Studio, or another OpenAI-style one) from the address - the server
    /// ("http://192.168.2.70:13305") or its API ("…/api/v1", "…/v1") - reads how it is, loads / unloads models where it can, and asks it.
    /// </summary>
    public sealed class AiServer
    {
        public ServerConfig Config;
        readonly HttpClient _http;
        string _root = "", _api = "", _kind = "", _version = "";
        DateTime _modelsAt = DateTime.MinValue, _infoAt = DateTime.MinValue, _versionAt = DateTime.MinValue;
        List<ModelInfo> _models = new List<ModelInfo>();
        string _hardware = "";
        long _free, _total;
        long _lastRequests = -1;
        readonly List<double> _speed = new List<double>();
        double _askTps;
        bool _askSpeed;
        public int Fails;
        public bool WasOnline;
        public DateTime SinceUtc = DateTime.UtcNow;

        public AiServer(ServerConfig c)
        {
            Config = c;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string key = Secret.Unprotect(c.Key ?? "");
            if (key.Length > 0) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        /// <summary>The server's root: the address without the API part.</summary>
        public static string Root(string url)
        {
            string u = (url ?? "").Trim().TrimEnd('/');
            if (u.Length > 0 && !u.Contains("://")) u = "http://" + u;
            foreach (string tail in new[] { "/chat/completions", "/models", "/api/v1", "/api/v0", "/v1", "/api" })
                if (u.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) u = u.Substring(0, u.Length - tail.Length);
            return u.TrimEnd('/');
        }

        async Task<string> Get(string url, CancellationToken ct)
        {
            using (HttpResponseMessage r = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                int code = (int)r.StatusCode;
                if (code == 401 || code == 403) throw new InvalidOperationException("it wants an API key (" + code + ")");
                if (!r.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + code);
                return await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        async Task<string> TryGet(string url, CancellationToken ct)
        {
            try { return await Get(url, ct).ConfigureAwait(false); }
            catch (InvalidOperationException) { throw; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
            catch (HttpRequestException ex) when (ex.InnerException != null) { throw; }      // can't connect at all: no point trying the other kinds
            catch (Exception) { return null; }
        }

        static bool Has(JsonElement e, string name) { return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement unused); }

        /// <summary>Which kind of server it is, from what answers: Lemonade's /api/v1/health, Ollama's /api/version, LM Studio's /api/v0/models, else /v1/models.</summary>
        async Task Detect(CancellationToken ct)
        {
            _root = Root(Config.Url);
            string t = await TryGet(_root + "/api/v1/health", ct).ConfigureAwait(false);
            if (t != null && Json(t, d => Has(d, "model_loaded") || Has(d, "all_models_loaded") || Has(d, "max_models")))
            { _kind = "lemonade"; _api = _root + "/api/v1"; return; }
            t = await TryGet(_root + "/api/version", ct).ConfigureAwait(false);
            if (t != null && Json(t, d => Has(d, "version")))
            { _kind = "ollama"; _api = _root + "/v1"; return; }
            t = await TryGet(_root + "/api/v0/models", ct).ConfigureAwait(false);
            if (t != null && Json(t, d => Has(d, "data")))
            { _kind = "lmstudio"; _api = _root + "/v1"; return; }
            foreach (string b in new[] { _root + "/v1", Regex.Replace(Config.Url.Trim().TrimEnd('/'), "/models$", "") })
            {
                t = await TryGet(b + "/models", ct).ConfigureAwait(false);
                if (t != null && Json(t, d => Has(d, "data") || Has(d, "models"))) { _kind = "openai"; _api = b; return; }
            }
            throw new InvalidOperationException("it answered, but not as Lemonade, Ollama, LM Studio or an OpenAI-style server");
        }

        static bool Json(string text, Func<JsonElement, bool> test)
        {
            try { using (JsonDocument d = JsonDocument.Parse(text)) return test(d.RootElement); }
            catch { return false; }
        }

        static string Str(JsonElement e, string n)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(n, out JsonElement v)) return "";
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "";
        }

        static double Num(JsonElement e, string n)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(n, out JsonElement v)) return 0;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;
        }

        static bool Bool(JsonElement e, string n) { return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out JsonElement v) && v.ValueKind == JsonValueKind.True; }

        static IEnumerable<JsonElement> Arr(JsonElement e, string n)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out JsonElement a) && a.ValueKind == JsonValueKind.Array)
                foreach (JsonElement x in a.EnumerateArray()) yield return x;
        }

        /// <summary>One look: how the server is now. Never throws - an error is in the state.</summary>
        public async Task<ServerState> PollAsync(CancellationToken ct)
        {
            var s = new ServerState { Config = Config, CheckedUtc = DateTime.UtcNow, Checked = true };
            try
            {
                if (_kind.Length == 0) await Detect(ct).ConfigureAwait(false);
                switch (_kind)
                {
                    case "lemonade": await PollLemonade(s, ct).ConfigureAwait(false); break;
                    case "ollama": await PollOllama(s, ct).ConfigureAwait(false); break;
                    case "lmstudio": await PollLmStudio(s, ct).ConfigureAwait(false); break;
                    default: await PollOpenAi(s, ct).ConfigureAwait(false); break;
                }
                s.Online = true;
                if (Fails > 0 || !WasOnline) SinceUtc = DateTime.UtcNow;
                Fails = 0;
            }
            catch (Exception ex)
            {
                s.Online = false;
                s.Error = Plain(ex);
                if (Fails == 0 && WasOnline) SinceUtc = DateTime.UtcNow;
                Fails++;
                _kind = "";      // it may come back as something else (or after a restart on another port's API) - work it out again
            }
            s.Kind = _kind.Length > 0 ? _kind : s.Kind;
            s.Version = _version;
            s.Models = _models;
            s.Hardware = _hardware;
            s.StorageFree = _free; s.StorageTotal = _total;
            if (!s.HasSpeed && _askSpeed) { s.Tps = _askTps; s.HasSpeed = true; }
            lock (_speed) s.Speed = new List<double>(_speed);
            s.SinceUtc = SinceUtc;
            return s;
        }

        static string Plain(Exception ex)
        {
            if (ex is TaskCanceledException) return "no answer in time";
            if (ex is HttpRequestException h && h.InnerException != null) return "can't reach it (" + h.InnerException.Message.TrimEnd('.') + ")";
            return ex.Message;
        }

        void AddSpeed(double tps)
        {
            if (tps <= 0 || double.IsNaN(tps) || double.IsInfinity(tps)) return;
            lock (_speed)
            {
                _speed.Add(Math.Round(tps, 1));
                if (_speed.Count > 48) _speed.RemoveAt(0);
            }
        }

        bool Due(DateTime at, TimeSpan every) { return DateTime.UtcNow - at > every; }

        // ---- Lemonade: /api/v1/health (what's loaded, busy), /stats (speed, totals), /models, /system-info ----
        async Task PollLemonade(ServerState s, CancellationToken ct)
        {
            string health = await Get(_api + "/health", ct).ConfigureAwait(false);
            using (JsonDocument d = JsonDocument.Parse(health))
            {
                _version = Str(d.RootElement, "version");
                foreach (JsonElement m in Arr(d.RootElement, "all_models_loaded"))
                {
                    string type = Str(m, "type");
                    if (type.Length > 0 && type != "llm") continue;      // the speech / image models Lemonade keeps too
                    int ctx = 0;
                    if (m.TryGetProperty("recipe_options", out JsonElement ro)) ctx = (int)Num(ro, "ctx_size");
                    s.Loaded.Add(new LoadedModel { Name = Str(m, "model_name"), Ctx = ctx, Device = Str(m, "device").ToUpperInvariant(), Busy = Bool(m, "is_busy") || Bool(m, "is_streaming") });
                }
                if (s.Loaded.Count == 0 && Str(d.RootElement, "model_loaded").Length > 0) s.Loaded.Add(new LoadedModel { Name = Str(d.RootElement, "model_loaded") });
            }
            string stats = null;
            try { stats = await Get(_api + "/stats", ct).ConfigureAwait(false); } catch { }
            if (stats != null)
                using (JsonDocument d = JsonDocument.Parse(stats))
                {
                    JsonElement r = d.RootElement;
                    s.Tps = Num(r, "tokens_per_second");
                    s.TtftSeconds = Num(r, "time_to_first_token");
                    s.HasSpeed = s.Tps > 0;
                    s.Requests = (long)Num(r, "request_count_total");
                    s.TokensIn = (long)Num(r, "input_tokens_total");
                    s.TokensOut = (long)Num(r, "output_tokens_total");
                    s.HasTotals = Has(r, "request_count_total");
                    // a new answer since the last look: its speed goes into the chart
                    if (_lastRequests >= 0 && s.Requests > _lastRequests) AddSpeed(s.Tps);
                    _lastRequests = s.Requests;
                }
            if (Due(_modelsAt, TimeSpan.FromMinutes(1)))
            {
                var list = new List<ModelInfo>();
                using (JsonDocument d = JsonDocument.Parse(await Get(_api + "/models", ct).ConfigureAwait(false)))
                    foreach (JsonElement m in Arr(d.RootElement, "data"))
                    {
                        if (m.TryGetProperty("downloaded", out JsonElement dl) && dl.ValueKind == JsonValueKind.False) continue;      // in its catalogue, not on the server
                        string id = Str(m, "id");
                        if (Regex.IsMatch(Str(m, "recipe") + " " + LabelsOf(m), @"\b(whispercpp|kokoro|sd-cpp|moonshine|transcription|tts|image|embeddings?|reranking)\b", RegexOptions.IgnoreCase)) continue;
                        if (id.Length > 0) list.Add(new ModelInfo { Id = id, SizeGb = Num(m, "size") });
                    }
                _models = list;
                _modelsAt = DateTime.UtcNow;
            }
            foreach (ModelInfo m in _models) m.Loaded = s.Loaded.Any(l => l.Name == m.Id);
            if (Due(_infoAt, TimeSpan.FromMinutes(10)))
            {
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(await Get(_api + "/system-info", ct).ConfigureAwait(false)))
                    {
                        JsonElement r = d.RootElement;
                        var parts = new List<string>();
                        string cpu = Str(r, "Processor");
                        string gpu = "";
                        if (r.TryGetProperty("devices", out JsonElement dev))
                            foreach (string k in new[] { "nvidia_gpu", "amd_gpu" })
                                foreach (JsonElement g in Arr(dev, k))
                                    if (Bool(g, "available") && Str(g, "name").Length > 0) { gpu = Str(g, "name"); break; }
                        if (gpu.Length > 0) parts.Add(gpu);
                        if (cpu.Length > 0) parts.Add(Regex.Replace(cpu, @"\s+with\s+.*$", ""));
                        string mem = Str(r, "Physical Memory");
                        if (mem.Length > 0) parts.Add(mem.Replace(".00 GB", " GB") + " memory");
                        _hardware = string.Join(" · ", parts);
                        if (r.TryGetProperty("model_storage", out JsonElement st)) { _free = (long)Num(st, "free_bytes"); _total = (long)Num(st, "total_bytes"); }
                    }
                    _infoAt = DateTime.UtcNow;
                }
                catch { _infoAt = DateTime.UtcNow; }
            }
        }

        static string LabelsOf(JsonElement m)
        {
            var l = new List<string>();
            foreach (JsonElement x in Arr(m, "labels")) if (x.ValueKind == JsonValueKind.String) l.Add(x.GetString());
            return string.Join(" ", l);
        }

        // ---- Ollama: /api/ps (loaded, in memory / VRAM), /api/tags (downloaded), /api/version ----
        async Task PollOllama(ServerState s, CancellationToken ct)
        {
            using (JsonDocument d = JsonDocument.Parse(await Get(_root + "/api/ps", ct).ConfigureAwait(false)))
                foreach (JsonElement m in Arr(d.RootElement, "models"))
                {
                    double size = Num(m, "size") / 1e9, vram = Num(m, "size_vram") / 1e9;
                    s.Loaded.Add(new LoadedModel
                    {
                        Name = Str(m, "name"), Ctx = (int)Num(m, "context_length"), SizeGb = size, VramGb = vram,
                        Device = vram <= 0 ? "CPU" : vram >= size * 0.99 ? "GPU" : Math.Round(vram / Math.Max(0.01, size) * 100) + "% GPU"
                    });
                }
            if (Due(_modelsAt, TimeSpan.FromMinutes(1)))
            {
                var list = new List<ModelInfo>();
                using (JsonDocument d = JsonDocument.Parse(await Get(_root + "/api/tags", ct).ConfigureAwait(false)))
                    foreach (JsonElement m in Arr(d.RootElement, "models"))
                        if (Str(m, "name").Length > 0 && !Regex.IsMatch(Str(m, "name"), "embed", RegexOptions.IgnoreCase))
                            list.Add(new ModelInfo { Id = Str(m, "name"), SizeGb = Num(m, "size") / 1e9 });
                _models = list;
                _modelsAt = DateTime.UtcNow;
            }
            foreach (ModelInfo m in _models) m.Loaded = s.Loaded.Any(l => l.Name == m.Id);
            if (Due(_versionAt, TimeSpan.FromMinutes(10)))
            {
                try { using (JsonDocument d = JsonDocument.Parse(await Get(_root + "/api/version", ct).ConfigureAwait(false))) _version = Str(d.RootElement, "version"); } catch { }
                _versionAt = DateTime.UtcNow;
            }
        }

        // ---- LM Studio: /api/v0/models (each with its state, loaded or not) ----
        async Task PollLmStudio(ServerState s, CancellationToken ct)
        {
            var list = new List<ModelInfo>();
            using (JsonDocument d = JsonDocument.Parse(await Get(_root + "/api/v0/models", ct).ConfigureAwait(false)))
                foreach (JsonElement m in Arr(d.RootElement, "data"))
                {
                    if (Str(m, "type") == "embeddings") continue;
                    string id = Str(m, "id");
                    bool loaded = Str(m, "state") == "loaded";
                    list.Add(new ModelInfo { Id = id, Loaded = loaded });
                    if (loaded) s.Loaded.Add(new LoadedModel { Name = id, Ctx = (int)Num(m, "loaded_context_length") > 0 ? (int)Num(m, "loaded_context_length") : (int)Num(m, "max_context_length") });
                }
            _models = list;
        }

        // ---- anything else OpenAI-style: /models (no idea what's loaded) ----
        async Task PollOpenAi(ServerState s, CancellationToken ct)
        {
            if (!Due(_modelsAt, TimeSpan.FromMinutes(1))) { await Get(_api + "/models", ct).ConfigureAwait(false); return; }      // still there?
            var list = new List<ModelInfo>();
            using (JsonDocument d = JsonDocument.Parse(await Get(_api + "/models", ct).ConfigureAwait(false)))
                foreach (JsonElement m in Arr(d.RootElement, "data").Concat(Arr(d.RootElement, "models")))
                {
                    string id = Str(m, "id"); if (id.Length == 0) id = Str(m, "name");
                    if (id.Length > 0) list.Add(new ModelInfo { Id = id });
                }
            _models = list;
            _modelsAt = DateTime.UtcNow;
        }

        // =====================================================================================
        // Load / unload (Lemonade and Ollama), ask
        // =====================================================================================

        async Task<string> Post(string url, string json, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource(timeout))
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
            {
                // the shared client's 8 s is for looks; a model takes longer to load or answer
                using (HttpResponseMessage r = await Long.SendAsync(req, cts.Token).ConfigureAwait(false))
                {
                    string text = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode)
                    {
                        string why = "";
                        try { using (JsonDocument d = JsonDocument.Parse(text)) if (d.RootElement.TryGetProperty("error", out JsonElement e)) why = e.ValueKind == JsonValueKind.String ? e.GetString() : Str(e, "message"); } catch { }
                        throw new InvalidOperationException("the server said " + (int)r.StatusCode + (why.Length > 0 ? ": " + why : ""));
                    }
                    return text;
                }
            }
        }

        HttpClient _long;
        HttpClient Long
        {
            get
            {
                if (_long == null)
                {
                    _long = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                    if (_http.DefaultRequestHeaders.Authorization != null) _long.DefaultRequestHeaders.Authorization = _http.DefaultRequestHeaders.Authorization;
                }
                return _long;
            }
        }

        /// <summary>Load a model (it stays loaded), or unload it. null = done, else why not.</summary>
        public async Task<string> LoadAsync(string model, bool load)
        {
            try
            {
                if (_kind.Length == 0) await Detect(CancellationToken.None);
                if (_kind == "lemonade")
                    await Post(_api + (load ? "/load" : "/unload"), JsonSerializer.Serialize(new Dictionary<string, object> { { "model_name", model } }), TimeSpan.FromMinutes(10));
                else if (_kind == "ollama")
                    await Post(_root + "/api/generate", JsonSerializer.Serialize(new Dictionary<string, object> { { "model", model }, { "keep_alive", load ? (object)"60m" : 0 } }), TimeSpan.FromMinutes(10));
                else return KindNameOf(_kind) + " doesn't let other apps load or unload models - it loads one when it's asked";
                return null;
            }
            catch (Exception ex) { return Plain(ex); }
        }

        static string KindNameOf(string k) { return new ServerState { Kind = k }.KindName; }

        /// <summary>Ask: the answer's text (a thinking model's thinking left out), and how long it took.</summary>
        public async Task<string> AskAsync(string model, string question, bool think, CancellationToken ct)
        {
            if (_kind.Length == 0) await Detect(ct);
            var body = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new object[]
                    {
                        new Dictionary<string, string> { { "role", "system" }, { "content", "You're a helpful assistant on someone's Windows PC, asked from their taskbar. Answer clearly and briefly - a few sentences, or a short list or command when that's what's asked." } },
                        new Dictionary<string, string> { { "role", "user" }, { "content", question } }
                    } },
                { "max_tokens", think ? 4000 : 1000 },
                { "temperature", 0.4 },
                { "stream", false }
            };
            if (!think) { body["chat_template_kwargs"] = new Dictionary<string, object> { { "enable_thinking", false } }; body["think"] = false; }
            var sw = Stopwatch.StartNew();
            string text = await Post(_api + "/chat/completions", JsonSerializer.Serialize(body), TimeSpan.FromMinutes(5));
            sw.Stop();
            string answer = "";
            using (JsonDocument d = JsonDocument.Parse(text))
            {
                JsonElement r = d.RootElement;
                foreach (JsonElement c in Arr(r, "choices"))
                {
                    answer = c.TryGetProperty("message", out JsonElement m) ? Str(m, "content") : Str(c, "text");
                    break;
                }
                // its speed, for a server that doesn't say (Lemonade does, in /stats)
                if (_kind != "lemonade" && r.TryGetProperty("usage", out JsonElement u))
                {
                    double outTok = Num(u, "completion_tokens");
                    if (outTok > 0 && sw.Elapsed.TotalSeconds > 0) { _askTps = outTok / sw.Elapsed.TotalSeconds; _askSpeed = true; AddSpeed(_askTps); }
                }
            }
            answer = Regex.Replace(answer, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            answer = Regex.Replace(answer, @"^[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
            if (answer.Length == 0) throw new InvalidOperationException(think ? "it only thought and gave no answer - switch off Let it think" : "it gave an empty answer - try a bigger model");
            return answer;
        }
    }
}
