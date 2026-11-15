using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A plugin: one class in its DLL implements this. Load registers what it brings through the host (modules, settings sections,
    /// notifications, run box keywords, palette commands); vShell takes those away again on Remove and calls Unload to stop the rest
    /// (timers, connections).
    /// </summary>
    [PluginApi]
    public interface IPlugin
    {
        void Load(PluginHost host);
        void Unload();
    }

    /// <summary>A module type a plugin brings: what the gallery, the settings window, bars and widgets need to know about it.</summary>
    [PluginApi]
    public sealed class PluginModule
    {
        public string Type = "", Display = "", ShortName = "", Glyph = "";
        /// <summary>Its group in Add a module ("Smart home & more").</summary>
        public string Group = "Smart home & more";
        public string Blurb = "";
        public bool OnBar = true, InWidget = true;
        /// <summary>It opens a popup (gets "Background effect behind its popup").</summary>
        public bool HasPopup;
        /// <summary>What it still needs before it works ("Needs the Komodo connection (Plugins)"), or null.</summary>
        public Func<BarSettings, string> Needs;
        public Func<BarManager, IModuleHost, ModuleConfig, FrameworkElement> Create;
        /// <summary>Its part of the module settings (the inspector).</summary>
        public Action<SettingsWindow.SettingsPage, ModuleConfig> Settings;
        /// <summary>The Add a module pictures (null = its name / the bar picture larger).</summary>
        public Func<Theme, FrameworkElement> BarPicture, WidgetPicture;
        /// <summary>A widget that sizes itself starts at this size when the module is added to it (one that fills whatever room it gets); 0 = no change.</summary>
        public Size WidgetSize;
    }

    /// <summary>What a plugin registers its parts with (one per plugin each time it's loaded).</summary>
    [PluginApi]
    public sealed class PluginHost
    {
        internal readonly PluginInfo Info;
        internal readonly List<PluginModule> Modules = new List<PluginModule>();
        internal readonly List<KeyValuePair<string, Action<SettingsWindow.SettingsPage>>> Integrations = new List<KeyValuePair<string, Action<SettingsWindow.SettingsPage>>>();
        internal readonly List<Action<SettingsWindow.SettingsPage>> NotifySections = new List<Action<SettingsWindow.SettingsPage>>();
        internal readonly List<Action<BarSettings>> Configures = new List<Action<BarSettings>>();
        internal readonly List<string> NotifyKinds = new List<string>(), Sources = new List<string>(), PaletteSources = new List<string>(), BangKinds = new List<string>();
        internal readonly List<Func<string, string, BarSettings, RunAction>> BangParsers = new List<Func<string, string, BarSettings, RunAction>>();
        internal readonly List<Func<BarSettings, string>> BangHints = new List<Func<BarSettings, string>>();
        internal readonly List<Func<string, BarSettings, RunAction>> TextParsers = new List<Func<string, BarSettings, RunAction>>();
        internal readonly List<Action<FrameworkElement, Action<bool, string>, Action<List<string>>>> RunDrops = new List<Action<FrameworkElement, Action<bool, string>, Action<List<string>>>>();
        internal readonly List<IMediaHelper> MediaHelpers = new List<IMediaHelper>();
        internal readonly List<KeyValuePair<string, Action<BarSettings>>> Watchers = new List<KeyValuePair<string, Action<BarSettings>>>();
        internal Func<BarSettings, bool> SetUpCheck;
        internal readonly List<KeyValuePair<IWeatherSource, Action<SettingsWindow.SettingsPage, ModuleConfig>>> WeatherSources = new List<KeyValuePair<IWeatherSource, Action<SettingsWindow.SettingsPage, ModuleConfig>>>();
        internal readonly List<NoteFeedStore> NoteStores = new List<NoteFeedStore>();
        internal readonly List<Func<string, bool>> CardLinks = new List<Func<string, bool>>();
        internal readonly List<Action<BarSettings>> PaletteOpening = new List<Action<BarSettings>>();
        internal readonly Dictionary<string, Action<string[]>> CommandLines = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);

        internal PluginHost(PluginInfo info, BarManager mgr) { Info = info; Manager = mgr; }

        public BarManager Manager { get; private set; }
        /// <summary>The plugin API of this vShell (PluginApi.Version).</summary>
        public int ApiVersion { get { return PluginApi.Version; } }
        public string Name { get { return Info.Name; } }

        public void AddModule(PluginModule m) { Modules.Add(m); }

        /// <summary>A section on its page in Settings > Plugins, Connection tab (the connection settings).</summary>
        public void AddIntegration(string title, Action<SettingsWindow.SettingsPage> build) { Integrations.Add(new KeyValuePair<string, Action<SettingsWindow.SettingsPage>>(title, build)); }

        /// <summary>A section on its page's Notifications tab (the Notifications page has a row that opens it).</summary>
        public void AddNotifications(Action<SettingsWindow.SettingsPage> build) { NotifySections.Add(build); }

        /// <summary>Called with the settings at start and whenever they change (where BarManager hands the integrations their connection).</summary>
        public void OnSettings(Action<BarSettings> configure) { Configures.Add(configure); }

        /// <summary>A sort of notification (its key, "Komodo.Problem", and the level it has until you pick another).</summary>
        public void AddNotifyKind(string key, NotifyLevel level) { Notify.Kinds[key] = level; NotifyKinds.Add(key); }

        /// <summary>A notification source the history module can list on its own ("Komodo").</summary>
        public void AddNotifySource(string name) { Sources.Add(name); }

        public void AddCommands(string name, Palette.Source source) { PaletteSources.Add(name); Palette.Register(name, source); }

        /// <summary>
        /// A run box keyword: parse gets (keyword typed, the rest, settings) and returns an action of its own kind (or null when it isn't
        /// its keyword); rows fills the drop-down for that kind; hint is shown in an empty run box ("!ko Komodo", null = none).
        /// </summary>
        public void AddBang(string kind, Func<string, string, BarSettings, RunAction> parse, Action<RunPanel, RunAction> rows, Func<BarSettings, string> hint)
        {
            BangKinds.Add(kind);
            BangParsers.Add(parse);
            if (hint != null) BangHints.Add(hint);
            RunPanel.PluginRows[kind] = rows;
        }

        /// <summary>
        /// Text in the run box that isn't a !keyword but the plugin knows (a magnet link, a .torrent address): parse gets the text (no spaces)
        /// and returns an action of one of its kinds, or null; rows fills the drop-down for those kinds.
        /// </summary>
        public void AddRunText(string[] kinds, Func<string, BarSettings, RunAction> parse, Action<RunPanel, RunAction> rows)
        {
            TextParsers.Add(parse);
            foreach (string k in kinds) { BangKinds.Add(k); RunPanel.PluginRows[k] = rows; }
        }

        /// <summary>
        /// Files dropped on a run box: attach(the run box, highlight(on, what dropping does) while they're over it, done(a line per file)) -
        /// the plugin hooks the drag events itself (only for the files it takes).
        /// </summary>
        public void AddRunBoxDrop(Action<FrameworkElement, Action<bool, string>, Action<List<string>>> attach) { RunDrops.Add(attach); }

        /// <summary>Something that knows more about what's playing than Windows (Jellyfin): Now playing asks it for the times and sends it the buttons.</summary>
        public void AddMediaHelper(IMediaHelper helper) { MediaHelpers.Add(helper); }

        /// <summary>A source for the Weather module ("Home Assistant"): its forecast, and its rows in the Weather module's settings (when it's picked).</summary>
        public void AddWeatherSource(IWeatherSource source, Action<SettingsWindow.SettingsPage, ModuleConfig> settings) { WeatherSources.Add(new KeyValuePair<IWeatherSource, Action<SettingsWindow.SettingsPage, ModuleConfig>>(source, settings)); }

        /// <summary>A store the Notes module can show instead of its own notes (vDrop's feed).</summary>
        public void AddNoteStore(NoteFeedStore store) { NoteStores.Add(store); }

        /// <summary>Its own links on notification cards ("vshell:ha-toggle:…"): the handler gets each link clicked and returns true when it was one of its.</summary>
        public void AddCardLinks(Func<string, bool> handler) { CardLinks.Add(handler); }

        /// <summary>The command palette is about to open: a moment to get its commands' data fresh.</summary>
        public void OnPaletteOpening(Action<BarSettings> warm) { PaletteOpening.Add(warm); }

        /// <summary>Whether it's set up (its connection filled in) - the Plugins page shows "Not set up yet" and Set up until it is.</summary>
        public void SetUpWhen(Func<BarSettings, bool> check) { SetUpCheck = check; }

        /// <summary>A check run every 5 seconds with the notification sources' (name, check) - for notifications that need asking (who's playing).</summary>
        public void AddWatcher(string name, Action<BarSettings> check) { Watchers.Add(new KeyValuePair<string, Action<BarSettings>>(name, check)); }

        /// <summary>
        /// Something vShell is started with for it: "vShell.exe --plugin &lt;its id&gt; &lt;verb&gt; args…" (from Explorer's right-click menu, a
        /// shortcut) - run gets the args, on the UI thread, in the running vShell (a second start hands them over and ends).
        /// </summary>
        public void AddCommandLine(string verb, Action<string[]> run) { CommandLines[verb] = run; }
    }

    /// <summary>
    /// The plugins added in settings: loads their DLLs (from memory, so a file can be replaced or deleted while vShell runs), fetches
    /// missing ones, and Add / Remove from the Plugins page. Everything else in vShell asks here for the plugins' parts.
    /// </summary>
    public static class PluginManager
    {
        sealed class Loaded
        {
            public PluginInfo Info;
            public IPlugin Plugin;
            public PluginHost Host;
        }

        static readonly Dictionary<string, Loaded> _loaded = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);
        // a .NET assembly can't be unloaded from vShell's context: a plugin removed and added again in one run uses the same one
        static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        // the sha256 of the DLL each of those came from - a different file for it now = it needs a restart
        static readonly Dictionary<string, string> _typeHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> _state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static BarManager _mgr;
        static bool _hooked;

        /// <summary>A plugin was added, removed, or its state changed (the Plugins page redraws).</summary>
        public static event Action Changed;

        static void RaiseChanged() { var h = Changed; if (h != null) h(); }

        static BarSettings S { get { return _mgr.Settings; } }

        /// <summary>The ids added in settings (never null).</summary>
        static List<string> AddedIds { get { if (S.Plugins == null) S.Plugins = new List<string>(); return S.Plugins; } }

        /// <summary>At start, with the settings loaded and before the bars are made: load the added plugins that are here, fetch the rest.</summary>
        public static void Start(BarManager mgr)
        {
            _mgr = mgr;
            PluginStore.Added = delegate { return new List<string>(AddedIds); };
            PluginStore.IncludePre = delegate { return S.UpdatePre; };
            if (!_hooked)
            {
                _hooked = true;
                // a plugin refers to "vShell": that's this program (also when it's one single-file exe)
                AssemblyLoadContext.Default.Resolving += delegate (AssemblyLoadContext ctx, AssemblyName name)
                {
                    return string.Equals(name.Name, "vShell", StringComparison.OrdinalIgnoreCase) ? typeof(PluginManager).Assembly : null;
                };
            }
            foreach (string id in new List<string>(AddedIds))
            {
                PluginInfo p = PluginStore.Find(id);
                if (p == null) continue;
                if (PluginStore.Have(p))
                {
                    Load(p);
                    if (PluginStore.OnOlderBuild(p)) FetchForNextStart(p);
                }
                else Fetch(p);
            }
            Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(delegate { PluginStore.TidyOld(); });
            if (!_checking)
            {
                _checking = true;
                // "Restart vShell" on the plugins-updated notification
                lock (NotifyCards.LinkHandlers) NotifyCards.LinkHandlers.Add(delegate (string url)
                {
                    if (url != "vshell:restart") return false;
                    string why = Restart();
                    if (why != null) Questions.Tell(null, why, "vShell");
                    return true;
                });
                UpdateChecks();
            }
        }

        /// <summary>Get a missing plugin in the background and load it when it's here (bars / widgets are made again).</summary>
        static async void Fetch(PluginInfo p)
        {
            _state[p.Id] = "Downloading…";
            RaiseChanged();
            string err = await PluginStore.FetchAsync(p, Updater.Current, null);
            await Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (err != null) _state[p.Id] = "Couldn't get it: " + err;
                else if (IsAdded(p.Id) && Load(p)) _mgr.SettingsChangedAll();
                RaiseChanged();
            }));
        }

        /// <summary>
        /// It runs on an older version's build (the update couldn't get this version's): get this version's in the background - it's used from
        /// the next start (a loaded plugin can't be swapped while vShell runs).
        /// </summary>
        static async void FetchForNextStart(PluginInfo p)
        {
            Log.Write("Plugins: " + p.Name + " is on an older build - getting the one for " + Updater.CurrentText);
            string err = await PluginStore.FetchAsync(p, Updater.Current, null);
            Log.Write("Plugins: " + p.Name + (err == null ? ": this version's build is here (used from the next start)" : ": still on the older build - " + err));
        }

        /// <summary>Load its DLL and let it register. false = it didn't load (the reason is its state).</summary>
        static bool Load(PluginInfo p)
        {
            if (_loaded.ContainsKey(p.Id)) return true;
            try
            {
                Type t;
                if (!_types.TryGetValue(p.Id, out t))
                {
                    string file = PluginStore.Usable(p);
                    if (file == null) throw new InvalidOperationException("there's no build of it this vShell can use");
                    byte[] data = File.ReadAllBytes(file);
                    Assembly asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(data));
                    t = Array.Find(asm.GetTypes(), delegate (Type x) { return typeof(IPlugin).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface; });
                    if (t == null) throw new InvalidOperationException("there's no plugin class in " + p.File + ".dll");
                    _types[p.Id] = t;
                    _typeHashes[p.Id] = Convert.ToHexString(SHA256.HashData(data));
                }
                var plugin = (IPlugin)Activator.CreateInstance(t);
                var host = new PluginHost(p, _mgr);
                plugin.Load(host);
                foreach (PluginModule m in host.Modules) ModuleTypes.Register(m.Type, m.Display, m.ShortName, m.OnBar, m.InWidget);
                lock (ModuleTypes.ExtraSources) foreach (string src in host.Sources) if (!ModuleTypes.ExtraSources.Contains(src)) ModuleTypes.ExtraSources.Add(src);
                foreach (var f in host.BangParsers) RunParser.BangParsers.Add(f);
                foreach (var f in host.TextParsers) RunParser.TextParsers.Add(f);
                foreach (var h in host.BangHints) RunPanel.PluginHints.Add(h);
                foreach (var d in host.RunDrops) RunBoxModule.PluginDrops.Add(d);
                lock (MediaSession.Helpers) MediaSession.Helpers.AddRange(host.MediaHelpers);
                lock (NotifyWatch.Watchers) NotifyWatch.Watchers.AddRange(host.Watchers);
                foreach (var w in host.WeatherSources)
                {
                    lock (WeatherService.Sources) WeatherService.Sources.Add(w.Key);
                    if (w.Value != null) SettingsWindow.WeatherRows[w.Key.Id] = w.Value;
                }
                foreach (NoteFeedStore n in host.NoteStores) NoteFeeds.Add(n);
                lock (NotifyCards.LinkHandlers) NotifyCards.LinkHandlers.AddRange(host.CardLinks);
                foreach (Action<BarSettings> w in host.PaletteOpening) Palette.Warming += w;
                _loaded[p.Id] = new Loaded { Info = p, Plugin = plugin, Host = host };
                foreach (Action<BarSettings> c in host.Configures) c(S);
                _state.Remove(p.Id);
                Log.Write("Plugins: loaded " + p.Name + " " + Updater.CurrentText);
                return true;
            }
            catch (Exception ex)
            {
                Exception e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                if (e is ReflectionTypeLoadException) e = ((ReflectionTypeLoadException)e).LoaderExceptions[0] ?? e;
                _state[p.Id] = "Didn't load: " + e.Message;
                Log.Write("Plugins: " + p.Name + " didn't load: " + e);
                return false;
            }
        }

        static void Unload(Loaded l)
        {
            try { l.Plugin.Unload(); } catch (Exception ex) { Log.Write("Plugins: " + l.Info.Name + " unload: " + ex.Message); }
            PluginHost h = l.Host;
            foreach (PluginModule m in h.Modules) ModuleTypes.Unregister(m.Type);
            lock (ModuleTypes.ExtraSources) foreach (string s in h.Sources) ModuleTypes.ExtraSources.Remove(s);
            foreach (var f in h.BangParsers) RunParser.BangParsers.Remove(f);
            foreach (var f in h.TextParsers) RunParser.TextParsers.Remove(f);
            foreach (var x in h.BangHints) RunPanel.PluginHints.Remove(x);
            foreach (var d in h.RunDrops) RunBoxModule.PluginDrops.Remove(d);
            lock (MediaSession.Helpers) foreach (var m in h.MediaHelpers) MediaSession.Helpers.Remove(m);
            lock (NotifyWatch.Watchers) foreach (var w in h.Watchers) NotifyWatch.Watchers.Remove(w);
            foreach (var w in h.WeatherSources)
            {
                lock (WeatherService.Sources) WeatherService.Sources.Remove(w.Key);
                SettingsWindow.WeatherRows.Remove(w.Key.Id);
            }
            foreach (NoteFeedStore n in h.NoteStores) NoteFeeds.Remove(n);
            lock (NotifyCards.LinkHandlers) foreach (var c in h.CardLinks) NotifyCards.LinkHandlers.Remove(c);
            foreach (Action<BarSettings> w in h.PaletteOpening) Palette.Warming -= w;
            foreach (string k in h.BangKinds) RunPanel.PluginRows.Remove(k);
            foreach (string n in h.PaletteSources) Palette.Unregister(n);
            _loaded.Remove(l.Info.Id);
        }

        /// <summary>
        /// "--plugin &lt;id&gt; &lt;verb&gt; args…" (words = id, verb, args): give the args to the plugin that registered the verb (AddCommandLine).
        /// On the UI thread. A plugin that isn't added (or loaded) gets a notification instead.
        /// </summary>
        public static void RunCommandLine(string[] words)
        {
            if (words == null || words.Length < 2) return;
            string id = words[0], verb = words[1];
            string[] rest = new string[words.Length - 2];
            Array.Copy(words, 2, rest, 0, rest.Length);
            Loaded l;
            Action<string[]> run;
            if (!_loaded.TryGetValue(id, out l))
            {
                PluginInfo p = PluginStore.Find(id);
                string name = p != null ? p.Name : id;
                Log.Write("Plugins: --plugin " + id + " " + verb + " - " + name + " isn't " + (IsAdded(id) ? "loaded" : "added"));
                Notify.Post("vShell", "The " + name + " plugin isn't " + (IsAdded(id) ? "running" : "added"),
                            IsAdded(id) ? "It didn't load - Settings > Plugins says why." : "Add it in Settings > Plugins, then try again.", "", NotifyLevel.Normal);
                return;
            }
            if (!l.Host.CommandLines.TryGetValue(verb, out run))
            {
                Log.Write("Plugins: --plugin " + id + " " + verb + " - " + l.Info.Name + " has no \"" + verb + "\"");
                return;
            }
            try { run(rest); }
            catch (Exception ex) { Log.Write("Plugins: " + l.Info.Name + " " + verb + ": " + ex); }
        }

        // =================================================================
        // Add / remove (the Plugins page)
        // =================================================================
        public static bool IsLoaded(string id) { return _loaded.ContainsKey(id); }
        public static bool IsAdded(string id) { return AddedIds.Exists(delegate (string x) { return string.Equals(x, id, StringComparison.OrdinalIgnoreCase); }); }

        /// <summary>Downloading…, Didn't load: …, or null when there's nothing to say.</summary>
        public static string StateOf(string id) { string s; return _state.TryGetValue(id, out s) ? s : null; }

        /// <summary>Add a plugin: fetch its DLL when it isn't here, load it, make the bars again. null = done, else why not.</summary>
        public static async Task<string> AddAsync(PluginInfo p)
        {
            if (!IsAdded(p.Id)) AddedIds.Add(p.Id);
            _mgr.SaveSoon();
            if (!PluginStore.Have(p))
            {
                _state[p.Id] = "Downloading…";
                RaiseChanged();
                string err = await PluginStore.FetchAsync(p, Updater.Current, null);
                if (err != null) { _state[p.Id] = "Couldn't get it: " + err; RaiseChanged(); return err; }
            }
            bool ok = Load(p);
            _mgr.SettingsChangedAll();
            RaiseChanged();
            return ok ? null : StateOf(p.Id);
        }

        /// <summary>Add a plugin from a DLL you picked. null = done, else why not. p = the plugin it was (null when it wasn't one).</summary>
        public static Task<string> AddFileAsync(string file, out PluginInfo p)
        {
            string err = PluginStore.FromFile(file, out p);
            if (err != null) return Task.FromResult(err);
            // (a plugin already loaded in this run keeps the copy it has - a newer build of the same version is used from the next start)
            return AddAsync(p);
        }

        /// <summary>
        /// Its DLL here isn't the one loaded in this run (added again from a newer file, or a newer build fetched): vShell still runs the
        /// old one until it restarts (.NET can't unload it).
        /// </summary>
        public static bool NeedsRestart(PluginInfo p)
        {
            string loadedHash;
            if (p == null || !_types.ContainsKey(p.Id) || !_typeHashes.TryGetValue(p.Id, out loadedHash)) return false;
            string file = PluginStore.Usable(p);
            if (file == null) return false;
            try { return !string.Equals(loadedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { Log.Write("Plugins: " + p.Name + " restart check: " + ex.Message); return false; }
        }

        /// <summary>Ask whether to restart now to use the build just added; no = a notification that restarts vShell when clicked.</summary>
        public static void OfferRestart(Window owner, PluginInfo p)
        {
            if (!NeedsRestart(p)) return;
            Log.Write("Plugins: " + p.Name + " was added again with a different build - it's used from the next start");
            if (Questions.YesNo(owner, "vShell is still running the " + p.Name + " plugin it loaded earlier - the build you added is used once vShell restarts.\n\nRestart vShell now?",
                                "Plugins") == MessageBoxResult.Yes)
            {
                string why = Restart();
                if (why == null) return;
                Questions.Tell(null, why, "vShell");
            }
            Notify.Post("vShell", p.Name + " needs a restart", "The build you added is used once vShell restarts - click to restart now.", "vshell:restart", NotifyLevel.Normal);
        }

        /// <summary>Take a plugin away: its modules show "add it again" (their settings are kept), its parts go, its files are deleted.</summary>
        public static void Remove(PluginInfo p)
        {
            AddedIds.RemoveAll(delegate (string x) { return string.Equals(x, p.Id, StringComparison.OrdinalIgnoreCase); });
            Loaded l;
            if (_loaded.TryGetValue(p.Id, out l)) Unload(l);
            _state.Remove(p.Id);
            PluginStore.Delete(p);
            _mgr.SaveSoon();
            _mgr.SettingsChangedAll();
            RaiseChanged();
        }

        // =================================================================
        // Updates: a newer build of a plugin in a release (no new vShell needed)
        // =================================================================
        static bool _checking;
        static readonly HashSet<string> _told = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A minute after start, then every 6 hours: look for newer builds (and download them, with "Update plugins by themselves").</summary>
        static async void UpdateChecks()
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            while (true)
            {
                try { await CheckUpdatesAsync(true); }
                catch (Exception ex) { Log.Write("Plugins: update check: " + ex.Message); }
                await Task.Delay(TimeSpan.FromHours(6));
            }
        }

        /// <summary>The added plugins with a newer build to update to.</summary>
        public static List<PluginInfo> UpdatesAvailable()
        {
            var l = new List<PluginInfo>();
            foreach (string id in new List<string>(AddedIds))
            {
                PluginInfo p = PluginStore.Find(id);
                string blocked;
                if (p != null && PluginStore.UpdateFor(p, out blocked) != null) l.Add(p);
            }
            return l;
        }

        /// <summary>
        /// Read the plugin list now. background: with "Update plugins by themselves" the updates are downloaded (and a notification says
        /// to restart); without, a notification says which there are (once per build). Returns how many updates there are (or were done).
        /// </summary>
        public static async Task<int> CheckUpdatesAsync(bool background)
        {
            bool changed = await PluginStore.RefreshCatalogAsync(true);
            List<PluginInfo> ups = UpdatesAvailable();
            if (background && ups.Count > 0)
            {
                if (S.PluginAutoUpdate)
                {
                    var done = new List<string>();
                    foreach (PluginInfo p in ups) if (await PluginStore.UpdateAsync(p) == null) done.Add(p.Name + " " + PluginStore.UpdatedTo(p));
                    if (done.Count > 0)
                        Notify.Post("vShell", done.Count == 1 ? "A plugin was updated" : done.Count + " plugins were updated",
                                    string.Join(", ", done) + " - restart vShell to use " + (done.Count == 1 ? "it" : "them") + ".", "vshell:restart", NotifyLevel.Normal);
                }
                else
                {
                    var news = new List<string>();
                    foreach (PluginInfo p in ups)
                    {
                        string key = p.Id + "@" + p.Latest.Sha256;
                        if (_told.Add(key)) news.Add(p.Name + " " + p.Latest.Version);
                    }
                    if (news.Count > 0)
                        Notify.Post("vShell", "Plugin updates", string.Join(", ", news) + " - update them in Settings > Plugins.", "", NotifyLevel.Normal);
                }
            }
            if (changed || ups.Count > 0) RaiseChanged();
            return ups.Count;
        }

        /// <summary>Update one plugin (used from the next start). null = done, else why not.</summary>
        public static async Task<string> UpdateAsync(PluginInfo p)
        {
            string err = await PluginStore.UpdateAsync(p);
            RaiseChanged();
            return err;
        }

        /// <summary>Start vShell again (its settings are saved first) - the updated plugins load. null = going, else why it can't.</summary>
        public static string Restart()
        {
            string exe = Updater.ExePath;
            if (exe == null) return "vShell isn't running from vShell.exe here - close it and start it again yourself.";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restart") { UseShellExecute = false }); }
            catch (Exception ex) { return "vShell couldn't start again: " + ex.Message; }
            _mgr.Shutdown();
            return null;
        }

        // =================================================================
        // The plugins' parts, for the rest of vShell
        // =================================================================
        static IEnumerable<PluginHost> Hosts { get { foreach (Loaded l in new List<Loaded>(_loaded.Values)) yield return l.Host; } }

        public static PluginModule ModuleOf(string type)
        {
            foreach (PluginHost h in Hosts) foreach (PluginModule m in h.Modules) if (m.Type == type) return m;
            return null;
        }

        public static List<PluginModule> AllModules()
        {
            var l = new List<PluginModule>();
            foreach (PluginHost h in Hosts) l.AddRange(h.Modules);
            return l;
        }

        static PluginHost HostOf(string id) { Loaded l; return id != null && _loaded.TryGetValue(id, out l) ? l.Host : null; }

        /// <summary>One loaded plugin's connection sections (its page's Connection tab); empty when it has none or isn't loaded.</summary>
        public static List<Action<SettingsWindow.SettingsPage>> IntegrationsOf(string id)
        {
            var l = new List<Action<SettingsWindow.SettingsPage>>();
            PluginHost h = HostOf(id);
            if (h != null) foreach (var s in h.Integrations) l.Add(s.Value);
            return l;
        }

        /// <summary>One loaded plugin's notification sections (its page's Notifications tab).</summary>
        public static List<Action<SettingsWindow.SettingsPage>> NotifySectionsOf(string id)
        {
            PluginHost h = HostOf(id);
            return h != null ? new List<Action<SettingsWindow.SettingsPage>>(h.NotifySections) : new List<Action<SettingsWindow.SettingsPage>>();
        }

        /// <summary>The module types one loaded plugin brings (its page's Modules tab).</summary>
        public static List<PluginModule> ModulesOf(string id)
        {
            PluginHost h = HostOf(id);
            return h != null ? new List<PluginModule>(h.Modules) : new List<PluginModule>();
        }

        /// <summary>The loaded plugins with notification settings, by name (Notifications page: a row each).</summary>
        public static List<PluginInfo> WithNotifications()
        {
            var l = new List<PluginInfo>();
            foreach (Loaded x in new List<Loaded>(_loaded.Values)) if (x.Host.NotifySections.Count > 0) l.Add(x.Info);
            l.Sort(delegate (PluginInfo a, PluginInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return l;
        }

        /// <summary>A loaded plugin is set up (true / false), or null when it can't tell (it has no check, or isn't loaded).</summary>
        public static bool? IsSetUp(string id)
        {
            Loaded l;
            if (!_loaded.TryGetValue(id, out l) || l.Host.SetUpCheck == null) return null;
            try { return l.Host.SetUpCheck(S); } catch { return null; }
        }

        /// <summary>What a loaded plugin brings, for the Plugins page: its modules' names.</summary>
        public static List<string> ModuleNames(string id)
        {
            var names = new List<string>();
            Loaded l;
            if (_loaded.TryGetValue(id, out l)) foreach (PluginModule m in l.Host.Modules) names.Add(m.Display);
            return names;
        }

        /// <summary>The settings were applied (BarManager.ApplyHa): hand them to the plugins.</summary>
        public static void Configure(BarSettings s)
        {
            foreach (PluginHost h in Hosts)
                foreach (Action<BarSettings> c in h.Configures)
                    try { c(s); } catch (Exception ex) { Log.Write("Plugins: " + h.Name + " settings: " + ex.Message); }
        }

        /// <summary>
        /// A module that isn't one of vShell's own: the plugin's (when it's added), "add it in Plugins" (a plugin's module with the plugin
        /// not added - its settings are kept), or null (a type nobody knows).
        /// </summary>
        public static FrameworkElement CreateModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            PluginModule m = ModuleOf(cfg.Type);
            if (m != null && m.Create != null)
            {
                try { return m.Create(mgr, host, cfg); }
                catch (Exception ex) { Log.Write("Plugins: " + cfg.Type + " module: " + ex); return Missing(host, cfg.Type + " couldn't start", ex.Message); }
            }
            PluginInfo p = PluginStore.OwnerOf(cfg.Type);
            if (p == null) return null;
            string st = StateOf(p.Id);
            return Missing(host, p.Name + (st != null && st.StartsWith("Downloading") ? " - getting it…" : " - add it in Settings"),
                           st ?? "The " + p.Name + " plugin isn't added. Settings > Plugins brings it back with this module as it was.");
        }

        static FrameworkElement Missing(IModuleHost host, string text, string tip)
        {
            Theme t = host.Theme;
            var b = new Border
            {
                Child = new TextBlock { Text = text, Foreground = t.FgDim, FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip
            };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; host.OpenSettings(); };
            return b;
        }
    }
}
