using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>What a notification says about its thing: it's down, it's back up, or something else (an update, a warning).</summary>
    [PluginApi]
    public enum NotifyState { Other, Down, Up }

    /// <summary>
    /// What a notification is about: the thing's name as shown ("nuc01") and the other names and addresses it goes by ("192.168.2.133",
    /// "pve-nuc01", "https://radarr.vrsion.xyz" - an address with a port is a service of its own). Notifications from different apps about
    /// the same thing are one entry that grows (Settings > Notifications > The same thing, once); a "back up" closes the outage.
    /// </summary>
    [PluginApi]
    public sealed class NotifyAbout
    {
        public string Name { get; set; } = "";
        public List<string> Also { get; set; } = new List<string>();
        public NotifyState State { get; set; }

        /// <summary>It's down (not answering, offline, stopped).</summary>
        public static NotifyAbout Down(string name, params string[] also) { return Make(NotifyState.Down, name, also); }
        /// <summary>It's back up.</summary>
        public static NotifyAbout Up(string name, params string[] also) { return Make(NotifyState.Up, name, also); }
        /// <summary>Something else about it (kept with its names; not joined).</summary>
        public static NotifyAbout Of(string name, params string[] also) { return Make(NotifyState.Other, name, also); }

        static NotifyAbout Make(NotifyState state, string name, string[] also)
        {
            var a = new NotifyAbout { Name = (name ?? "").Trim(), State = state };
            if (also != null) foreach (string x in also) if (!string.IsNullOrWhiteSpace(x)) a.Also.Add(x.Trim());
            return a;
        }

        /// <summary>Its name and every other name, as given.</summary>
        public List<string> All()
        {
            var l = new List<string>();
            if (Name.Length > 0) l.Add(Name);
            if (Also != null) foreach (string x in Also) if (!string.IsNullOrWhiteSpace(x) && !l.Contains(x)) l.Add(x);
            return l;
        }
    }

    /// <summary>What one app said about an outage (a line under the joined entry).</summary>
    [PluginApi]
    public sealed class NotifySay
    {
        public string Source { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTime At { get; set; }              // UTC
        /// <summary>"Down", "Up" or "Other".</summary>
        public string State { get; set; } = "";
    }

    /// <summary>
    /// Which names are the same thing. Learned from the plugins (each notification's NotifyAbout, and what a plugin knows - vDeploy's host
    /// and its address), from DNS (a local name - no dots, or .lan / .local / .home.arpa / .internal - and its address; never a public
    /// name, which can be a proxy's address shared by many sites), and your own lines (Settings > Notifications > The same thing, once).
    /// Kept in notify-names.json; a learned pair not seen for 30 days is forgotten.
    /// </summary>
    public static class NotifyNames
    {
        public sealed class Fact
        {
            public string From { get; set; } = "";
            public List<string> Names { get; set; } = new List<string>();
            public DateTime Seen { get; set; }
        }

        /// <summary>One thing: its names (the first is the one shown) and who said they go together.</summary>
        public sealed class Thing
        {
            public List<string> Names = new List<string>();
            public List<string> From = new List<string>();
            public bool Mine;
        }

        static readonly object Gate = new object();
        static List<Fact> _facts;
        static List<Fact> _mine = new List<Fact>();
        static Dictionary<string, Thing> _index;
        static DateTime _saved = DateTime.MinValue, _changed = DateTime.MinValue;
        static readonly Dictionary<string, DateTime> Looked = new Dictionary<string, DateTime>();

        static string FilePath { get { return Path.Combine(BarSettings.ConfigDir, "notify-names.json"); } }

        /// <summary>"https://Radarr.vrsion.xyz/" -> "radarr.vrsion.xyz"; "http://192.168.2.3:7878/x" -> "192.168.2.3:7878"; "NUC01." -> "nuc01".</summary>
        public static string Key(string name)
        {
            string s = (name ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0) return "";
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                Uri u;
                if (Uri.TryCreate(s, UriKind.Absolute, out u) && u.Host.Length > 0) return u.Host.TrimEnd('.') + (u.IsDefaultPort ? "" : ":" + u.Port);
                s = s.Substring(scheme + 3);
            }
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            if (s.EndsWith(":80") || s.EndsWith(":443")) s = s.Substring(0, s.LastIndexOf(':'));
            return s.TrimEnd('.');
        }

        static void Load()
        {
            if (_facts != null) return;
            _facts = new List<Fact>();
            try
            {
                if (File.Exists(FilePath))
                {
                    List<Fact> l = JsonSerializer.Deserialize<List<Fact>>(File.ReadAllText(FilePath));
                    if (l != null) _facts.AddRange(l.Where(f => f != null && f.Names != null && f.Names.Count > 1 && DateTime.UtcNow - f.Seen < TimeSpan.FromDays(30)));
                }
            }
            catch (Exception ex) { Log.Write("Notifications (names): " + ex.Message); }
        }

        /// <summary>Your own lines from the settings ("printer, 192.168.2.40, Bambu X1C").</summary>
        public static void Configure(BarSettings s)
        {
            var mine = new List<Fact>();
            if (s != null && s.NotifySame != null)
                foreach (string line in s.NotifySame)
                {
                    var names = (line ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    if (names.Count > 1) mine.Add(new Fact { From = "you", Names = names, Seen = DateTime.UtcNow });
                }
            lock (Gate) { Load(); _mine = mine; _index = null; }
        }

        /// <summary>These names are the same thing, says `from` (a plugin, "DNS"). Cheap to call often: only something new is saved.</summary>
        public static void Learn(string from, IEnumerable<string> names)
        {
            if (names == null) return;
            var shown = names.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var keys = shown.Select(Key).Where(k => k.Length > 0).Distinct().ToList();
            foreach (string k in keys) LookUpSoon(k);
            if (!string.IsNullOrEmpty(from) && from != "DNS" && from != "you") lock (Told) Told.Add(from);
            if (keys.Count < 2) return;
            bool save;
            lock (Gate)
            {
                Load();
                string sig = string.Join("|", keys.OrderBy(k => k, StringComparer.Ordinal));
                Fact had = _facts.FirstOrDefault(f => f.From == from && string.Join("|", f.Names.Select(Key).Distinct().OrderBy(k => k, StringComparer.Ordinal)) == sig);
                DateTime now = DateTime.UtcNow;
                if (had != null)
                {
                    bool stale = now - had.Seen > TimeSpan.FromDays(1);
                    had.Seen = now;
                    save = stale;
                }
                else
                {
                    _facts.Add(new Fact { From = from ?? "", Names = shown, Seen = now });
                    _index = null;
                    save = true;
                }
                if (save) _changed = now;
            }
            if (save) SaveSoon();
        }

        static void SaveSoon()
        {
            lock (Gate) if (DateTime.UtcNow - _saved < TimeSpan.FromSeconds(10)) { DelaySave(); return; }
            Save();
        }

        static bool _saveQueued;
        static void DelaySave()
        {
            if (_saveQueued) return;
            _saveQueued = true;
            Task.Delay(10000).ContinueWith(delegate { _saveQueued = false; Save(); });
        }

        static void Save()
        {
            try
            {
                string json;
                lock (Gate)
                {
                    _saved = DateTime.UtcNow;
                    _facts.RemoveAll(f => DateTime.UtcNow - f.Seen > TimeSpan.FromDays(30));
                    json = JsonSerializer.Serialize(_facts);
                }
                Directory.CreateDirectory(BarSettings.ConfigDir);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex) { Log.Write("Notifications (names) save: " + ex.Message); }
        }

        /// <summary>A local name (no dots, or a home suffix) is looked up in DNS now and then: its address joins it.</summary>
        static void LookUpSoon(string key)
        {
            if (!LocalName(key)) return;
            lock (Looked)
            {
                DateTime last;
                if (Looked.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromHours(6)) return;
                Looked[key] = DateTime.UtcNow;
            }
            Task.Run(async delegate
            {
                try
                {
                    string host = key.Contains(':') ? key.Substring(0, key.IndexOf(':')) : key;
                    IPAddress[] found = await Dns.GetHostAddressesAsync(host);
                    var v4 = found.Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).Distinct().ToList();
                    if (v4.Count == 1) Learn("DNS", new[] { host, v4[0] });
                }
                catch { }       // not known: nothing to learn
            });
        }

        static bool LocalName(string key)
        {
            if (key.Length == 0 || key.Contains(':')) return false;
            IPAddress ip;
            if (IPAddress.TryParse(key, out ip)) return false;
            if (!key.Contains('.')) return key.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
            foreach (string end in new[] { ".lan", ".local", ".home.arpa", ".internal", ".localdomain", ".home" })
                if (key.EndsWith(end, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Every name's thing (built again when what's known changed).</summary>
        static Dictionary<string, Thing> Index()
        {
            if (_index != null) return _index;
            var idx = new Dictionary<string, Thing>(StringComparer.Ordinal);
            foreach (Fact f in _mine.Concat(_facts))
            {
                var keys = f.Names.Select(Key).Where(k => k.Length > 0).Distinct().ToList();
                if (keys.Count == 0) continue;
                // every thing these names are already in becomes one
                var joined = keys.Select(k => { Thing t; return idx.TryGetValue(k, out t) ? t : null; }).Where(t => t != null).Distinct().ToList();
                Thing into = joined.Count > 0 ? joined[0] : new Thing();
                foreach (Thing other in joined.Skip(1))
                {
                    foreach (string n in other.Names) if (!into.Names.Contains(n, StringComparer.OrdinalIgnoreCase)) into.Names.Add(n);
                    foreach (string w in other.From) if (!into.From.Contains(w)) into.From.Add(w);
                    into.Mine |= other.Mine;
                    foreach (string k in other.Names.Select(Key)) idx[k] = into;
                }
                foreach (string n in f.Names) if (!into.Names.Contains(n, StringComparer.OrdinalIgnoreCase) && !into.Names.Any(x => Key(x) == Key(n))) into.Names.Add(n);
                if (!into.From.Contains(f.From)) into.From.Add(f.From);
                if (f.From == "you") into.Mine = true;
                foreach (string k in keys) idx[k] = into;
            }
            _index = idx;
            return idx;
        }

        /// <summary>Do any of these names and any of those name the same thing?</summary>
        public static bool Same(IEnumerable<string> a, IEnumerable<string> b)
        {
            if (a == null || b == null) return false;
            var ka = a.Select(Key).Where(k => k.Length > 0).ToList();
            var kb = b.Select(Key).Where(k => k.Length > 0).ToList();
            if (ka.Count == 0 || kb.Count == 0) return false;
            if (ka.Intersect(kb).Any()) return true;
            lock (Gate)
            {
                Load();
                Dictionary<string, Thing> idx = Index();
                var ta = new HashSet<Thing>(ka.Select(k => { Thing t; return idx.TryGetValue(k, out t) ? t : null; }).Where(t => t != null));
                if (ta.Count == 0) return false;
                return kb.Any(k => { Thing t; return idx.TryGetValue(k, out t) && ta.Contains(t); });
            }
        }

        /// <summary>Every thing with more than one name (for the settings page): yours first, then learned ones by name.</summary>
        public static List<Thing> All()
        {
            lock (Gate)
            {
                Load();
                return Index().Values.Distinct().Where(t => t.Names.Count > 1)
                              .OrderBy(t => t.Mine ? 0 : 1).ThenBy(t => Shown(t), StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        /// <summary>The name a thing is shown by: its first name that isn't an address.</summary>
        public static string Shown(Thing t)
        {
            foreach (string n in t.Names) { IPAddress ip; string k = Key(n); if (!IPAddress.TryParse(k.Contains(':') ? k.Substring(0, k.LastIndexOf(':')) : k, out ip)) return n; }
            return t.Names.Count > 0 ? t.Names[0] : "";
        }

        /// <summary>Forget what was learned (yours stay).</summary>
        public static void Forget()
        {
            lock (Gate) { Load(); _facts.Clear(); _index = null; }
            lock (Looked) Looked.Clear();
            Save();
        }

        /// <summary>Who has told vShell what its notifications are about (for the settings page's ticks).</summary>
        public static List<string> Sources()
        {
            List<string> told;
            lock (Told) told = Told.ToList();
            lock (Gate)
            {
                Load();
                return _facts.Select(f => f.From).Concat(told).Where(f => f.Length > 0 && f != "DNS" && f != "you").Distinct().OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        static readonly HashSet<string> Told = new HashSet<string>();
    }
}
