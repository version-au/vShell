using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using vShell.Core;
using JV = vShell.Core.JsonValue;

namespace ClaudeUsage
{
    /// <summary>One usage limit: the 5-hour session, the week, or a model's own week.</summary>
    public sealed class Limit
    {
        public string Key = "";        // stays the same between polls: "session", "weekly_all", "weekly_scoped:Opus"
        public string Kind = "";       // session / weekly_all / weekly_scoped / extra / …
        public string Name = "";       // "5-hour", "Weekly", "Opus weekly"
        public string Short = "";      // "5h", "Wk", "Opus"
        public double Percent;         // 0..100
        public DateTime? ResetsUtc;
        public bool Active;            // the limit Anthropic says you're on now
        public bool Session => Kind == "session";
        public bool Model => Kind == "weekly_scoped";
        public bool Weekly => Kind == "weekly_all";

        /// <summary>The percent now: once the reset time has passed the window has started again at 0.</summary>
        public double Now(DateTime utc) { return ResetsUtc.HasValue && utc >= ResetsUtc.Value ? 0 : Percent; }
        public bool HasReset(DateTime utc) { return ResetsUtc.HasValue && utc >= ResetsUtc.Value; }
    }

    /// <summary>What the last poll found. Immutable once published.</summary>
    public sealed class Snapshot
    {
        public List<Limit> Limits = new List<Limit>();
        public string Plan = "";
        public DateTime FetchedUtc;          // last successful fetch (MinValue = never)
        public string Error = "";             // the last poll's problem, "" when it worked
        public bool SignedIn = true;
        public bool Loading;                  // nothing fetched yet

        public static readonly Snapshot Empty = new Snapshot { Loading = true, FetchedUtc = DateTime.MinValue };
        public bool HasData => FetchedUtc > DateTime.MinValue;
    }

    /// <summary>The settings the client needs, copied on the UI thread so the poll never touches BarSettings.</summary>
    public sealed class Options
    {
        public string Folder = "";
        public bool Renew = true;
        public string UserAgent = UsageClient.DefaultAgent;
    }

    /// <summary>
    /// Reads Claude Code's sign-in (.credentials.json) and asks Anthropic for the plan's usage limits -
    /// the same request Claude Code's /usage makes. Never logs or shows a token.
    /// </summary>
    public static class UsageClient
    {
        public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        public const string TokenUrl = "https://api.anthropic.com/v1/oauth/token";
        public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";   // Claude Code's public OAuth client
        public const string Beta = "oauth-2025-04-20";
        // Anthropic's edge answers 403 to User-Agents it doesn't know, so this is Claude Code's.
        public const string DefaultAgent = "claude-code/2.1.71";
        public const string UsagePage = "https://claude.ai/settings/usage";

        static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        static DateTime _blockedUntilUtc = DateTime.MinValue;   // after a 429

        static HttpClient Http { get { return PluginHttp.Client("Claude usage", false, 20, false); } }

        // ---------- where the sign-in is ----------

        public static string DefaultFolder()
        {
            string env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }

        public static string FolderFor(Options o)
        {
            string f = (o == null ? "" : o.Folder ?? "").Trim().Trim('"');
            if (f.Length == 0) return DefaultFolder();
            return Environment.ExpandEnvironmentVariables(f);
        }

        public static string CredentialsFile(Options o) { return Path.Combine(FolderFor(o), ".credentials.json"); }

        public static bool SignedIn(Options o)
        {
            try { return File.Exists(CredentialsFile(o)); } catch { return false; }
        }

        /// <summary>Claude Code installs to look at, for the Find button: this PC's, then each WSL distro's.</summary>
        public static List<string> FindFolders()
        {
            var found = new List<string>();
            try { if (File.Exists(Path.Combine(DefaultFolder(), ".credentials.json"))) found.Add(DefaultFolder()); } catch { }
            foreach (string distro in WslDistros())
            {
                foreach (string root in new[] { @"\\wsl.localhost\" + distro, @"\\wsl$\" + distro })
                {
                    try
                    {
                        if (!Directory.Exists(root)) continue;
                        var homes = new List<string>();
                        string home = Path.Combine(root, "home");
                        if (Directory.Exists(home)) homes.AddRange(Directory.GetDirectories(home));
                        homes.Add(Path.Combine(root, "root"));
                        foreach (string h in homes)
                        {
                            string dir = Path.Combine(h, ".claude");
                            if (File.Exists(Path.Combine(dir, ".credentials.json")) && !found.Contains(dir)) found.Add(dir);
                        }
                        break;   // one of the two roots worked
                    }
                    catch { }
                }
            }
            return found;
        }

        static List<string> WslDistros()
        {
            var list = new List<string>();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("wsl.exe", "-l -q")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, StandardOutputEncoding = Encoding.Unicode
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p == null) return list;
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    foreach (string line in text.Split('\n'))
                    {
                        string d = line.Replace("\0", "").Trim();
                        if (d.Length > 0 && !d.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) list.Add(d);
                    }
                }
            }
            catch { }
            return list;
        }

        // ---------- credentials ----------

        sealed class Login
        {
            public JsonNode Root;
            public string Access = "", Refresh = "", Tier = "", Sub = "";
            public long ExpiresAt;   // ms since 1970, 0 = unknown
            public bool Same(Login other) { return other != null && other.Access == Access && other.Refresh == Refresh; }
        }

        static Login ReadLogin(string file)
        {
            string text;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
            JsonNode root = JsonNode.Parse(text);
            JsonObject o = root?["claudeAiOauth"] as JsonObject;
            if (o == null) return null;
            var l = new Login { Root = root };
            l.Access = Str(o, "accessToken");
            l.Refresh = Str(o, "refreshToken");
            l.Tier = Str(o, "rateLimitTier");
            l.Sub = Str(o, "subscriptionType");
            try { if (o["expiresAt"] != null) l.ExpiresAt = (long)o["expiresAt"].GetValue<double>(); } catch { }
            return l.Access.Length > 0 ? l : null;
        }

        static string Str(JsonObject o, string key)
        {
            try { var n = o[key]; return n == null ? "" : n.GetValue<string>() ?? ""; } catch { return ""; }
        }

        public static string PlanName(string tier, string sub)
        {
            string t = !string.IsNullOrEmpty(tier) ? tier : sub ?? "";
            if (t.StartsWith("default_claude_", StringComparison.OrdinalIgnoreCase)) t = t.Substring(15);
            else if (t.StartsWith("claude_", StringComparison.OrdinalIgnoreCase)) t = t.Substring(7);
            if (t.Length == 0) return "";
            var words = t.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (char.IsDigit(w[0])) continue;   // "20x" stays
                words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
            }
            return string.Join(" ", words);
        }

        // ---------- the fetch ----------

        /// <summary>Asks for the usage. Never throws: problems come back in Snapshot.Error.</summary>
        public static async Task<Snapshot> FetchAsync(Options o, Snapshot previous)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try { return await FetchCore(o, previous).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Log.Write("Claude usage: " + ex.GetType().Name + " " + ex.Message);
                return Failed(previous, "Something went wrong (" + ex.GetType().Name + ")", true);
            }
            finally { Gate.Release(); }
        }

        static Snapshot Failed(Snapshot prev, string why, bool signedIn)
        {
            var s = new Snapshot { Error = why, SignedIn = signedIn };
            if (prev != null && prev.HasData)
            {
                s.Limits = prev.Limits; s.Plan = prev.Plan; s.FetchedUtc = prev.FetchedUtc;
            }
            else s.FetchedUtc = DateTime.MinValue;
            return s;
        }

        static async Task<Snapshot> FetchCore(Options o, Snapshot prev)
        {
            if (DateTime.UtcNow < _blockedUntilUtc)
                return Failed(prev, "Anthropic asked to slow down - trying again at " + TimeText.Clock(_blockedUntilUtc.ToLocalTime()), true);

            string file = CredentialsFile(o);
            if (!File.Exists(file)) return Failed(prev, "Claude Code isn't signed in on this PC (no " + file + ")", false);
            Login login = ReadLogin(file);
            if (login == null) return Failed(prev, "Claude Code isn't signed in (no Claude sign-in in .credentials.json)", false);

            bool renewed = false;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (login.ExpiresAt > 0 && nowMs > login.ExpiresAt - 60000)
            {
                if (!o.Renew) return Failed(prev, "Claude Code's sign-in has run out - open Claude Code to renew it", false);
                var r = await RenewAsync(file, login, o).ConfigureAwait(false);
                if (r.Item1 == null) return Failed(prev, r.Item2, false);
                login = r.Item1; renewed = true;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                HttpResponseMessage res;
                using (var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + login.Access);
                    req.Headers.TryAddWithoutValidation("User-Agent", Agent(o));
                    req.Headers.TryAddWithoutValidation("anthropic-beta", Beta);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    try { res = await Http.SendAsync(req).ConfigureAwait(false); }
                    catch (TaskCanceledException) { return Failed(prev, "Anthropic didn't answer in time", true); }
                    catch (HttpRequestException ex) { return Failed(prev, "Can't reach Anthropic (" + ex.Message + ")", true); }
                }
                using (res)
                {
                    int code = (int)res.StatusCode;
                    if (res.IsSuccessStatusCode)
                    {
                        string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var snap = new Snapshot { FetchedUtc = DateTime.UtcNow, Plan = PlanName(login.Tier, login.Sub) };
                        using (var doc = JsonDocument.Parse(body)) snap.Limits = Parse(doc.RootElement);
                        if (snap.Limits.Count == 0) snap.Error = "Anthropic sent no limits for this sign-in";
                        return snap;
                    }
                    if (code == 401)
                    {
                        // Claude Code may have renewed it meanwhile; else renew it ourselves, once.
                        Login latest = null;
                        try { latest = ReadLogin(file); } catch { }
                        if (latest != null && latest.Access != login.Access) { login = latest; continue; }
                        if (!renewed && o.Renew)
                        {
                            var r = await RenewAsync(file, latest ?? login, o).ConfigureAwait(false);
                            if (r.Item1 == null) return Failed(prev, r.Item2, false);
                            login = r.Item1; renewed = true; continue;
                        }
                        return Failed(prev, "Anthropic didn't accept Claude Code's sign-in - open Claude Code to sign in again", false);
                    }
                    if (code == 403)
                        return Failed(prev, "No usage for this sign-in (HTTP 403): no Pro/Max subscription, or the request was turned away", true);
                    if (code == 429)
                    {
                        TimeSpan wait = TimeSpan.FromMinutes(5);
                        var ra = res.Headers.RetryAfter;
                        if (ra != null)
                        {
                            if (ra.Delta.HasValue) wait = ra.Delta.Value;
                            else if (ra.Date.HasValue) wait = ra.Date.Value.UtcDateTime - DateTime.UtcNow;
                        }
                        if (wait < TimeSpan.FromMinutes(1)) wait = TimeSpan.FromMinutes(1);
                        if (wait > TimeSpan.FromMinutes(30)) wait = TimeSpan.FromMinutes(30);
                        _blockedUntilUtc = DateTime.UtcNow + wait;
                        return Failed(prev, "Anthropic asked to slow down - trying again at " + TimeText.Clock(_blockedUntilUtc.ToLocalTime()), true);
                    }
                    return Failed(prev, "Anthropic answered HTTP " + code, true);
                }
            }
            return Failed(prev, "The usage request kept failing after renewing the sign-in", false);
        }

        static string Agent(Options o)
        {
            string a = o == null ? "" : (o.UserAgent ?? "").Trim();
            return a.Length > 0 ? a : DefaultAgent;
        }

        /// <summary>
        /// Renews the access token with the refresh token, the way Claude Code does, and writes the new pair back
        /// to Claude Code's file - only if nobody changed the file since it was read (Anthropic rotates refresh
        /// tokens, so a lost one would sign Claude Code out).
        /// </summary>
        static async Task<Tuple<Login, string>> RenewAsync(string file, Login login, Options o)
        {
            // Claude Code may already have done it.
            try
            {
                Login latest = ReadLogin(file);
                if (latest != null && !latest.Same(login))
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (latest.ExpiresAt == 0 || latest.ExpiresAt - 60000 > now) return Tuple.Create(latest, "");
                    login = latest;
                }
            }
            catch { }

            if (login.Refresh.Length == 0)
                return Tuple.Create<Login, string>(null, "Claude Code's sign-in has run out - open Claude Code to renew it");

            string json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" }, { "refresh_token", login.Refresh }, { "client_id", ClientId }
            });
            HttpResponseMessage res;
            using (var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", Agent(o));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                try { res = await Http.SendAsync(req).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    return Tuple.Create<Login, string>(null, "Couldn't renew Claude Code's sign-in (" + ex.GetType().Name + ")");
                }
            }
            using (res)
            {
                if (!res.IsSuccessStatusCode)
                {
                    // Lost a race with Claude Code? Then its new sign-in is in the file.
                    try
                    {
                        Login latest = ReadLogin(file);
                        if (latest != null && !latest.Same(login)) return Tuple.Create(latest, "");
                    }
                    catch { }
                    return Tuple.Create<Login, string>(null,
                        "Couldn't renew Claude Code's sign-in (HTTP " + (int)res.StatusCode + ") - open Claude Code to sign in again");
                }
                string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                string access = "", refresh = "";
                double expiresIn = 3600;
                using (var doc = JsonDocument.Parse(body))
                {
                    var r = doc.RootElement;
                    access = JV.Str(r, "access_token");
                    refresh = JV.Str(r, "refresh_token");
                    double e = JV.Num(r, "expires_in");
                    if (!double.IsNaN(e) && e > 0) expiresIn = e;
                }
                if (access.Length == 0)
                    return Tuple.Create<Login, string>(null, "Couldn't renew Claude Code's sign-in (no token came back)");

                var updated = new Login
                {
                    Root = login.Root.DeepClone(), Access = access, Refresh = refresh.Length > 0 ? refresh : login.Refresh,
                    Tier = login.Tier, Sub = login.Sub,
                    ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(expiresIn * 1000)
                };
                var oauth = updated.Root["claudeAiOauth"] as JsonObject;
                oauth["accessToken"] = updated.Access;
                oauth["refreshToken"] = updated.Refresh;
                oauth["expiresAt"] = updated.ExpiresAt;

                // Write back only if the file still holds what we renewed.
                string tmp = file + ".vshell-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                try
                {
                    Login now = ReadLogin(file);
                    if (now != null && !now.Same(login)) return Tuple.Create(now, "");
                    File.WriteAllText(tmp, updated.Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                    // The old refresh token is spent now, so try hard to save: Claude Code (or a virus scanner)
                    // may hold the file open for a moment.
                    for (int i = 0; ; i++)
                    {
                        try { File.Move(tmp, file, true); break; }
                        catch (IOException) when (i < 20) { await Task.Delay(150).ConfigureAwait(false); }
                        catch (UnauthorizedAccessException) when (i < 20) { await Task.Delay(150).ConfigureAwait(false); }
                    }
                    Log.Write("Claude usage: renewed Claude Code's sign-in");
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    // The old refresh token is used up now; tell the user rather than lose it silently.
                    Log.Write("Claude usage: couldn't save the renewed sign-in: " + ex.GetType().Name);
                    return Tuple.Create<Login, string>(null,
                        "Renewed the sign-in but couldn't save it to Claude Code's file - open Claude Code to sign in again");
                }
                return Tuple.Create(updated, "");
            }
        }

        // ---------- the answer ----------

        /// <summary>
        /// Reads either shape Anthropic has used: "limits": [{kind, percent, resets_at, scope, is_active}]
        /// (2026-10), or the older {"five_hour": {utilization, resets_at}, "seven_day": …}.
        /// </summary>
        public static List<Limit> Parse(JsonElement root)
        {
            var list = new List<Limit>();
            if (root.ValueKind != JsonValueKind.Object) return list;

            JsonElement limits;
            if (root.TryGetProperty("limits", out limits) && limits.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in limits.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    double pct = Pct(e, "percent");
                    if (double.IsNaN(pct)) pct = Pct(e, "utilization");
                    if (double.IsNaN(pct)) continue;
                    string kind = JV.Str(e, "kind");
                    if (kind.Length == 0) continue;
                    string model = "";
                    JsonElement scope, m;
                    if (e.TryGetProperty("scope", out scope) && scope.ValueKind == JsonValueKind.Object &&
                        scope.TryGetProperty("model", out m) && m.ValueKind == JsonValueKind.Object)
                    {
                        model = JV.Str(m, "display_name");
                        if (model.Length == 0) model = JV.Str(m, "id");
                    }
                    var l = Make(kind, model);
                    l.Percent = pct;
                    l.ResetsUtc = Time(e, "resets_at");
                    JsonElement act;
                    l.Active = e.TryGetProperty("is_active", out act) && act.ValueKind == JsonValueKind.True;
                    Add(list, l);
                }
            }
            else
            {
                foreach (JsonProperty p in root.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object || p.Name == "extra_usage") continue;
                    double pct = Pct(p.Value, "utilization");
                    if (double.IsNaN(pct)) pct = Pct(p.Value, "percent");
                    if (double.IsNaN(pct)) continue;
                    Limit l;
                    switch (p.Name)
                    {
                        case "five_hour": l = Make("session", ""); break;
                        case "seven_day": l = Make("weekly_all", ""); break;
                        case "seven_day_opus": l = Make("weekly_scoped", "Opus"); break;
                        case "seven_day_sonnet": l = Make("weekly_scoped", "Sonnet"); break;
                        case "seven_day_oauth_apps": l = Make("weekly_scoped", "Apps"); break;
                        default:
                            if (p.Name.StartsWith("seven_day_")) l = Make("weekly_scoped", Words(p.Name.Substring(10)));
                            else l = Make(p.Name, "");
                            break;
                    }
                    l.Percent = pct;
                    l.ResetsUtc = Time(p.Value, "resets_at");
                    Add(list, l);
                }
            }

            // Extra usage (pay-as-you-go past the plan), when it's on and says how much is used.
            JsonElement extra;
            if (root.TryGetProperty("extra_usage", out extra) && extra.ValueKind == JsonValueKind.Object &&
                JV.Bool(extra, "is_enabled", false) == true)
            {
                double pct = Pct(extra, "utilization");
                if (double.IsNaN(pct)) pct = Pct(extra, "percent");
                if (!double.IsNaN(pct))
                {
                    var l = Make("extra", "");
                    l.Percent = pct;
                    l.ResetsUtc = Time(extra, "resets_at");
                    Add(list, l);
                }
            }

            list.Sort(delegate (Limit a, Limit b) { return Order(a).CompareTo(Order(b)); });
            return list;
        }

        static void Add(List<Limit> list, Limit l)
        {
            foreach (Limit x in list) if (x.Key == l.Key) return;   // first one wins
            list.Add(l);
        }

        static int Order(Limit l)
        {
            switch (l.Kind)
            {
                case "session": return 0;
                case "weekly_all": return 1;
                case "weekly_scoped": return 2;
                case "extra": return 4;
                default: return 3;
            }
        }

        static Limit Make(string kind, string model)
        {
            var l = new Limit { Kind = kind, Key = kind };
            switch (kind)
            {
                case "session": l.Name = "5-hour"; l.Short = "5h"; break;
                case "weekly_all": l.Name = "Weekly"; l.Short = "Wk"; break;
                case "weekly_scoped":
                    string m = model.Length > 0 ? model : "Model";
                    l.Key = kind + ":" + m;
                    l.Name = m + " weekly";
                    l.Short = m;
                    break;
                case "extra": l.Name = "Extra usage"; l.Short = "Extra"; break;
                default:
                    l.Name = Words(kind);
                    l.Short = l.Name.Length > 8 ? l.Name.Substring(0, 8) : l.Name;
                    break;
            }
            return l;
        }

        static string Words(string key)
        {
            string s = key.Replace('_', ' ').Trim();
            return s.Length == 0 ? key : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        static double Pct(JsonElement e, string name)
        {
            JsonElement v;
            if (!e.TryGetProperty(name, out v) || v.ValueKind != JsonValueKind.Number) return double.NaN;
            double d = v.GetDouble();
            if (double.IsNaN(d) || double.IsInfinity(d)) return double.NaN;
            return Math.Max(0, Math.Min(100, d));
        }

        static DateTime? Time(JsonElement e, string name)
        {
            JsonElement v;
            if (!e.TryGetProperty(name, out v)) return null;
            if (v.ValueKind == JsonValueKind.String)
            {
                DateTimeOffset dto;
                if (DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
                    return dto.UtcDateTime;
            }
            else if (v.ValueKind == JsonValueKind.Number)
            {
                double n = v.GetDouble();
                if (n > 1e11) n /= 1000;   // ms
                if (n > 0) return DateTimeOffset.FromUnixTimeSeconds((long)n).UtcDateTime;
            }
            return null;
        }
    }
}
