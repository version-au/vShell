using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("localai", "Local AI",
    Description = "Your own AI server - Lemonade, Ollama, LM Studio or any OpenAI-style one: what's loaded, whether it's busy and how fast, on a bar or widget; questions from the run box; a heads-up when it goes offline. vTrace uses its settings too.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "notifications, run box", Types = "LocalAi")]

namespace LocalAi
{
    /// <summary>The connection (Settings > Plugins > Local AI): the main server, its model, and More servers.</summary>
    public sealed class AiSettings
    {
        readonly SettingsBag _b;
        AiSettings(SettingsBag b) { _b = b; }
        public static AiSettings Of(BarSettings s) { return new AiSettings(s.Integration(Plugin.Id)); }

        /// <summary>vTrace reads Url / Key / Model / Think from here (Integrations["localai"]) - keep the names.</summary>
        public string Url { get { return _b.Get("Url", ""); } set { _b.Set("Url", (value ?? "").Trim()); } }
        public string Key { get { return _b.Get("Key", ""); } set { _b.Set("Key", string.IsNullOrEmpty(value) ? "" : Secret.Protect(value)); } }
        public string Name { get { return _b.Get("Name", ""); } set { _b.Set("Name", (value ?? "").Trim()); } }
        public string Model { get { return _b.Get("Model", ""); } set { _b.Set("Model", (value ?? "").Trim()); } }
        public bool Think { get { return _b.Get("Think", false); } set { _b.Set("Think", value); } }
        /// <summary>The run box keyword: !ai how do I … ("" = off).</summary>
        public string Bang { get { return (_b.Get("Bang", "ai") ?? "").Trim().TrimStart('!').ToLowerInvariant(); } set { _b.Set("Bang", (value ?? "").Trim().TrimStart('!')); } }
        /// <summary>More servers: "gaming-pc http://192.168.2.50:11434, laptop http://…".</summary>
        public string More { get { return _b.Get("More", ""); } set { _b.Set("More", (value ?? "").Trim()); } }
        public bool NotifyOffline { get { return _b.Get("NotifyOffline", true); } set { _b.Set("NotifyOffline", value); } }
        public bool NotifyOnline { get { return _b.Get("NotifyOnline", true); } set { _b.Set("NotifyOnline", value); } }
        public bool NotifyStorage { get { return _b.Get("NotifyStorage", true); } set { _b.Set("NotifyStorage", value); } }

        public bool Configured { get { return Url.Length > 0; } }

        /// <summary>The main server, then More servers ("name address" or just the address, separated by commas, semicolons or lines).</summary>
        public List<ServerConfig> Servers()
        {
            var list = new List<ServerConfig>();
            if (Url.Length > 0) list.Add(new ServerConfig { Name = Name.Length > 0 ? Name : HostOf(Url), Url = Url, Key = Key, Main = true });
            foreach (string part in More.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] w = part.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                string url = w[w.Length - 1], name = w.Length > 1 ? string.Join(" ", w.Take(w.Length - 1)) : HostOf(url);
                if (list.Any(x => x.Id == url.Trim().TrimEnd('/').ToLowerInvariant())) continue;
                list.Add(new ServerConfig { Name = name, Url = url });
            }
            return list;
        }

        public static string HostOf(string url)
        {
            string u = (url ?? "").Trim();
            int s = u.IndexOf("://", StringComparison.Ordinal);
            if (s >= 0) u = u.Substring(s + 3);
            int end = u.IndexOfAny(new[] { '/', ':' });
            return end > 0 ? u.Substring(0, end) : u;
        }
    }

    public sealed class Plugin : IPlugin
    {
        public const string Id = "localai";
        public const string Source = "Local AI";
        public const string Glyph = "";
        public const string KindOffline = "LocalAi.Offline", KindOnline = "LocalAi.Online", KindStorage = "LocalAi.Storage";

        /// <summary>Every server's last look (the main one first). Swapped, never changed, on the UI thread.</summary>
        public static List<ServerState> Current = new List<ServerState>();
        public static Poller Poll;
        static readonly Dictionary<string, AiServer> _servers = new Dictionary<string, AiServer>();
        static readonly HashSet<string> _toldOffline = new HashSet<string>();
        static readonly Dictionary<string, DateTime> _toldStorage = new Dictionary<string, DateTime>();
        static BarSettings _settings;
        static Dispatcher _ui;
        static DateTime _lastManual = DateTime.MinValue;
        /// <summary>A model being loaded / unloaded ("server|model"), shown as such until it's done.</summary>
        public static readonly HashSet<string> Busy = new HashSet<string>();
        public static event Action Changed;

        public void Load(PluginHost host)
        {
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Current = new List<ServerState>();
            host.AddModule(new PluginModule
            {
                Type = "LocalAi", Display = "Local AI", ShortName = "AI", Glyph = Glyph, Group = "Info",
                Blurb = "Your own AI server: the model that's loaded, whether it's answering, how fast - load or unload models from it.",
                OnBar = true, InWidget = true, HasPopup = true, WidgetSize = new Size(320, 0),
                Needs = delegate (BarSettings s) { return AiSettings.Of(s).Configured ? null : "Set up your AI server in Settings > Plugins > Local AI"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new AiModule(mgr, h, cfg); },
                Settings = AiModule.BuildSettings,
                BarPicture = Pictures.Bar,
                WidgetPicture = Pictures.Widget
            });
            host.AddIntegration(Source, BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return AiSettings.Of(s).Configured; });
            host.OnSettings(OnSettings);

            host.AddNotifyKind(KindOffline, NotifyLevel.Important);
            host.AddNotifyKind(KindOnline, NotifyLevel.Normal);
            host.AddNotifyKind(KindStorage, NotifyLevel.Important);
            host.AddNotifySource(Source);
            host.AddNotifications(BuildNotifications);

            // (the kind must be the RunAction's Kind - the run box finds the rows by it; anything else runs the text as a program)
            host.AddBang(RunBox.Kind, RunBox.Parse, RunBox.Rows, delegate (BarSettings s)
            {
                AiSettings c = AiSettings.Of(s);
                return c.Configured && c.Bang.Length > 0 ? "!" + c.Bang + " Local AI" : null;
            });
            host.AddCommands(Source, Commands);
            host.OnPaletteOpening(delegate (BarSettings s) { if (Poll != null && DateTime.UtcNow - Poll.LastPoll > TimeSpan.FromSeconds(30)) Poll.PollNow(); });

            // a look every 5 seconds while a module shows it, every minute otherwise (for the notifications)
            Poll = new Poller(Source, PollOnce, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        }

        public void Unload()
        {
            if (Poll != null) { Poll.Stop(); Poll.Dispose(); Poll = null; }
            Current = new List<ServerState>();
            lock (_servers) _servers.Clear();
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            if (Poll == null) return;
            if (!AiSettings.Of(s).Configured) { Current = new List<ServerState>(); return; }
            if (!Poll.Running) Poll.Start();
            else Poll.PollNow();
        }

        // ---------- looking ----------

        static List<AiServer> ServersNow()
        {
            if (_settings == null) return new List<AiServer>();
            List<ServerConfig> configs = AiSettings.Of(_settings).Servers();
            lock (_servers)
            {
                var keep = new List<AiServer>();
                foreach (ServerConfig c in configs)
                {
                    string k = c.Id + "|" + c.Key;
                    if (!_servers.TryGetValue(k, out AiServer srv)) _servers[k] = srv = new AiServer(c);
                    srv.Config = c;
                    keep.Add(srv);
                }
                foreach (string k in _servers.Keys.Where(k => !keep.Any(x => x.Config.Id + "|" + x.Config.Key == k)).ToList()) _servers.Remove(k);
                return keep;
            }
        }

        public static AiServer ServerOf(ServerConfig c)
        {
            lock (_servers) return _servers.Values.FirstOrDefault(x => x.Config.Id == c.Id);
        }

        static async Task PollOnce()
        {
            List<AiServer> list = ServersNow();
            if (list.Count == 0) return;
            ServerState[] got = await Task.WhenAll(list.Select(x => x.PollAsync(CancellationToken.None))).ConfigureAwait(false);
            Publish(got.ToList());
        }

        static void Publish(List<ServerState> list)
        {
            if (_ui == null || _ui.CheckAccess()) Apply(list);
            else _ui.BeginInvoke(new Action(delegate { Apply(list); }));
        }

        static void Apply(List<ServerState> list)
        {
            Current = list;
            Tell(list);
            Action a = Changed;
            if (a != null) a();
        }

        /// <summary>Look again now (the card's Refresh, after Load / Unload); at most every 2 seconds.</summary>
        public static void RefreshNow()
        {
            if (Poll == null || DateTime.UtcNow - _lastManual < TimeSpan.FromSeconds(2)) return;
            _lastManual = DateTime.UtcNow;
            Poll.PollNow();
        }

        /// <summary>Load or unload a model on a server; the module shows it as busy meanwhile. null = done, else why not.</summary>
        public static async Task<string> LoadAsync(ServerState s, string model, bool load)
        {
            AiServer srv = ServerOf(s.Config);
            if (srv == null) return "That server isn't in the settings any more";
            string key = s.Config.Id + "|" + model;
            Busy.Add(key);
            Action a = Changed; if (a != null) a();
            try { return await srv.LoadAsync(model, load); }
            finally
            {
                Busy.Remove(key);
                _lastManual = DateTime.MinValue;
                RefreshNow();
            }
        }

        /// <summary>Ask the main server (its picked model, else what's loaded).</summary>
        public static async Task<string> AskAsync(string question, CancellationToken ct)
        {
            if (_settings == null) throw new InvalidOperationException("Set up your AI server in Settings > Plugins > Local AI");
            AiSettings c = AiSettings.Of(_settings);
            ServerConfig main = c.Servers().FirstOrDefault();
            if (main == null) throw new InvalidOperationException("Set up your AI server in Settings > Plugins > Local AI");
            if (ServerOf(main) == null) ServersNow();
            AiServer srv = ServerOf(main);
            string model = ModelFor(c);
            if (model.Length == 0) throw new InvalidOperationException("No model to ask - pick one in Settings > Plugins > Local AI");
            string answer = await srv.AskAsync(model, question, c.Think, ct);
            RefreshNow();
            return answer;
        }

        /// <summary>The model questions go to: the one picked, else the main server's loaded / first one.</summary>
        public static string ModelFor(AiSettings c)
        {
            if (c.Model.Length > 0) return c.Model;
            ServerState s = Current.FirstOrDefault(x => x.Config.Main);
            return s != null ? s.ModelFor("") : "";
        }

        // ---------- notifications: offline / back, storage nearly full ----------

        static void Tell(List<ServerState> list)
        {
            if (_settings == null) return;
            AiSettings c = AiSettings.Of(_settings);
            foreach (ServerState s in list)
            {
                AiServer srv = ServerOf(s.Config);
                if (srv == null) continue;
                string id = s.Config.Id;
                // offline: two looks in a row (one missed look is a hiccup), and only if it was online before
                if (!s.Online && srv.Fails >= 2 && srv.WasOnline && _toldOffline.Add(id) && c.NotifyOffline)
                    Post(KindOffline, s.Name + " is offline", (s.KindName.Length > 0 ? s.KindName + " · " : "") + s.Error, s.Config.Url);
                if (s.Online)
                {
                    if (_toldOffline.Remove(id) && c.NotifyOnline)
                        Post(KindOnline, s.Name + " is back", s.KindName + (s.Loaded.Count > 0 ? " · " + s.Loaded[0].Name + " loaded" : ""), s.Config.Url);
                    srv.WasOnline = true;
                    // storage nearly full: under 10 % or 5 GB free - once a day
                    if (s.StorageTotal > 0 && (s.StorageFree < 5L * 1024 * 1024 * 1024 || s.StorageFree < s.StorageTotal / 10) &&
                        (!_toldStorage.TryGetValue(id, out DateTime at) || DateTime.UtcNow - at > TimeSpan.FromDays(1)))
                    {
                        _toldStorage[id] = DateTime.UtcNow;
                        if (c.NotifyStorage) Post(KindStorage, s.Name + ": model storage nearly full", Gb(s.StorageFree) + " free of " + Gb(s.StorageTotal), s.Config.Url);
                    }
                }
            }
        }

        public static string Gb(long bytes) { return (bytes / 1024.0 / 1024 / 1024).ToString(bytes < 10L * 1024 * 1024 * 1024 ? "0.0" : "0") + " GB"; }

        static void Post(string kind, string title, string detail, string url)
        {
            try { Notify.Post(Source, title, detail, url, Notify.KindOf(kind), true, null); }
            catch (Exception ex) { Log.Write("Local AI: notify failed: " + ex.Message); }
        }

        // ---------- Settings > Plugins > Local AI ----------

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            AiSettings c = AiSettings.Of(p.Settings);
            p.Header("Your AI server");
            p.Note("Lemonade, Ollama, LM Studio or anything with the OpenAI-style API, on this PC or another one - vShell works out which it is. On another PC, let the server take connections from the network (see its host setting). vTrace uses these settings too (Options > Local AI, left empty).");
            p.Text("Address", delegate { return c.Url; }, delegate (string v) { c.Url = v; },
                   "e.g. http://192.168.2.70:13305 (Lemonade), :11434 (Ollama), :1234 (LM Studio) - or its API, …/v1");
            p.Secret("API key", "API key", delegate { return c.Key; }, delegate (string v) { c.Key = v; }, "Only if your server wants one - kept encrypted for your Windows user");
            p.Text("Name", delegate { return c.Name; }, delegate (string v) { c.Name = v; }, "What to call it - empty: its address");
            ServerState main = Current.FirstOrDefault(x => x.Config.Main);
            var models = new List<string>();
            if (main != null) models.AddRange(main.Models.Select(m => m.Id));
            if (c.Model.Length > 0 && !models.Contains(c.Model)) models.Insert(0, c.Model);
            models.Insert(0, "(the loaded one)");
            p.Combo("Model for questions", models.ToArray(), delegate { return c.Model.Length > 0 ? c.Model : "(the loaded one)"; },
                    delegate (string v) { c.Model = v == "(the loaded one)" ? "" : v; });
            p.Button("Find models", "Ask the server what it has", delegate
            {
                Window w = p.Window;
                ServersNow();
                ServerConfig mc = c.Servers().FirstOrDefault();
                AiServer srv = mc != null ? ServerOf(mc) : null;
                if (srv == null) { Questions.Tell(w, "Fill in the address first.", Source); return; }
                srv.PollAsync(CancellationToken.None).ContinueWith(delegate (Task<ServerState> t)
                {
                    if (t.Result.Online) { Publish(Current.Where(x => !x.Config.Main).Prepend(t.Result).ToList()); p.Redraw(); }
                    else Questions.Tell(w, "Couldn't reach it: " + t.Result.Error, Source);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            });
            p.Note("Questions from the run box go to this model; empty: whichever is loaded. Lemonade and Ollama load it when it's asked, if it isn't already.");
            p.Check("Let it think first", delegate { return c.Think; }, delegate (bool v) { c.Think = v; });
            p.Note("For thinking models (Qwen3, Qwen3.5 …): slower, and on a small GPU rarely better. Off, they're asked to answer straight away.");
            p.Text("Run box keyword", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; }, "!ai how do I free docker space - empty: off");
            p.Test(delegate { }, TestAsync);

            p.Section("More servers");
            p.Text("More servers", delegate { return c.More; }, delegate (string v) { c.More = v; },
                   "A name and address each, commas between - e.g. gaming-pc http://192.168.2.50:11434, laptop http://192.168.2.61:1234");
            p.Note("They show in the module's card and widget too (status and Load / Unload). Questions go to the server above. More servers can't have an API key.");
        }

        static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            ServersNow();
            ServerConfig mc = _settings != null ? AiSettings.Of(_settings).Servers().FirstOrDefault() : null;
            AiServer srv = mc != null ? ServerOf(mc) : null;
            if (srv == null) return new KeyValuePair<bool, string>(false, "Fill in the address first.");
            ServerState s = await srv.PollAsync(CancellationToken.None);
            if (!s.Online) return new KeyValuePair<bool, string>(false, "Couldn't reach it: " + s.Error);
            Publish(Current.Where(x => !x.Config.Main).Prepend(s).ToList());
            return new KeyValuePair<bool, string>(true, s.KindName + (s.Version.Length > 0 ? " " + s.Version : "") + " - " +
                (s.Loaded.Count > 0 ? s.Loaded[0].Name + " loaded" : "no model loaded") + ", " + s.Models.Count + " model" + (s.Models.Count == 1 ? "" : "s"));
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            AiSettings c = AiSettings.Of(p.Settings);
            p.NotifySection(Source, c.Configured, "your AI server");
            p.Check("A server goes offline", delegate { return c.NotifyOffline; }, delegate (bool v) { c.NotifyOffline = v; });
            p.Check("It's back", delegate { return c.NotifyOnline; }, delegate (bool v) { c.NotifyOnline = v; });
            p.Check("Its model storage is nearly full (Lemonade)", delegate { return c.NotifyStorage; }, delegate (bool v) { c.NotifyStorage = v; });
            p.Note("Offline means two looks in a row missed (a minute apart, or 5 seconds while a module shows it); only for a server that was online. Nearly full: under 10% or 5 GB free, once a day.");
            p.Kind("Offline shows as", KindOffline);
            p.Kind("Back shows as", KindOnline);
            p.Kind("Storage shows as", KindStorage);
        }

        // ---------- the command palette: load / unload a model, look again ----------

        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!AiSettings.Of(s).Configured) return;
            into.Add(new PaletteCommand
            {
                Title = "Refresh Local AI", Detail = Summary(), Glyph = "", Source = Source, Words = "local ai llm lemonade ollama refresh status",
                Run = delegate (double n) { RefreshNow(); return Task.FromResult(PaletteCommand.Worked("Looking at your AI server")); }
            });
            foreach (ServerState st in Current.Where(x => x.Online && x.CanLoad))
                foreach (ModelInfo m in st.Models)
                {
                    ServerState server = st;
                    string model = m.Id;
                    bool load = !m.Loaded;
                    into.Add(new PaletteCommand
                    {
                        Title = (load ? "Load " : "Unload ") + model, Detail = server.Name + " · " + server.KindName, Glyph = Glyph, Source = Source,
                        Words = "local ai model llm " + (load ? "load" : "unload free") + " " + model + " " + server.Name,
                        Run = async delegate (double n)
                        {
                            string err = await LoadAsync(server, model, load);
                            return err == null ? PaletteCommand.Worked((load ? "Loaded " : "Unloaded ") + model) : "Couldn't: " + err;
                        }
                    });
                }
        }

        /// <summary>"Qwen3.5-4B · idle", "offline" … for the main server.</summary>
        public static string Summary()
        {
            ServerState s = Current.FirstOrDefault(x => x.Config.Main);
            if (s == null) return "Not looked at yet";
            if (!s.Online) return "Offline - " + s.Error;
            return (s.Loaded.Count > 0 ? s.Loaded[0].Name : "No model loaded") + " · " + (s.Busy ? "answering" : "idle");
        }
    }
}
