using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>
    /// How much a notification matters: Normal (green, goes after its time), Important (yellow, stays until closed),
    /// Alert (red: errors, something down, too hot - stays until closed).
    /// </summary>
    [PluginApi]
    public enum NotifyLevel { Normal, Important, Alert }

    /// <summary>One notification (shown as a card and kept in the history).</summary>
    [PluginApi]
    public sealed class NotifyItem
    {
        public long Id { get; set; }
        public DateTime At { get; set; }              // UTC
        /// <summary>Where it came from: Downloads, Komodo, Seerr, Home Assistant, Proxmox, Services, System, Jellyfin, Weather, vShell.</summary>
        public string Source { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        /// <summary>What a click opens (a web address, or "vshell:..." for an action of vShell's own); empty = nothing.</summary>
        public string Url { get; set; } = "";
        /// <summary>"Normal", "Important" or "Alert" (see NotifyLevel).</summary>
        public string Level { get; set; } = "";
        /// <summary>Histories from 0.32 only had this (true = what is now an Alert); still written so older versions read the file.</summary>
        public bool Bad { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public NotifyLevel Kind
        {
            get
            {
                NotifyLevel l;
                if (Level.Length > 0 && Enum.TryParse(Level, out l)) return l;
                return Bad ? NotifyLevel.Alert : NotifyLevel.Normal;
            }
        }
        public bool Read { get; set; }
        /// <summary>How many times it came (repeats within a few minutes are one entry: "×3").</summary>
        public int Count { get; set; } = 1;
        /// <summary>The title with its count when it repeated ("Backup failed ×3").</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string TitleShown { get { return Count > 1 ? Title + "  ×" + Count : Title; } }
        /// <summary>A picture shown on the card (a file, e.g. a Protect camera snapshot); "" = none.</summary>
        public string Image { get; set; } = "";
        /// <summary>Where it lives outside vShell, e.g. "vdrop:42" (a vDrop notification): reading it here reads it there too.</summary>
        public string Ref { get; set; } = "";
        /// <summary>Kept in the history (feedback such as "added to SABnzbd" is shown only).</summary>
        [System.Text.Json.Serialization.JsonIgnore] public bool Keep = true;

        // ---- about something (NotifyAbout): notifications from different apps about the same thing are one entry ----
        /// <summary>What it's about, as shown ("nuc01"); "" = nothing in particular.</summary>
        public string Thing { get; set; } = "";
        /// <summary>Every name and address it went by.</summary>
        public List<string> Names { get; set; }
        /// <summary>What each app said (an outage: one line per app, its latest); null = an ordinary notification.</summary>
        public List<NotifySay> Says { get; set; }
        /// <summary>An outage still going (down, not back yet).</summary>
        public bool Open { get; set; }
        /// <summary>When it went down, and came back (UTC; MinValue = not yet).</summary>
        public DateTime Since { get; set; }
        public DateTime Back { get; set; }
        /// <summary>Several apps said it (the lines under it show what each said).</summary>
        [System.Text.Json.Serialization.JsonIgnore] public bool Joined { get { return Says != null && Says.Count > 1; } }

        /// <summary>Its card shows without the sound (an outage that closed); its card has been shown (a held new outage's hasn't).</summary>
        internal bool Quietly, CardShown;
    }

    /// <summary>
    /// Every notification goes through here: it is added to the history, and shown now, or held while a full-screen app is in front,
    /// during quiet hours or while Windows is in Do not disturb / presentation mode (held ones show when that ends). The cards themselves
    /// are drawn by the UI (it listens to Show).
    /// </summary>
    public static class Notify
    {
        static readonly object Gate = new object();
        static List<NotifyItem> _history;
        static readonly List<NotifyItem> Held = new List<NotifyItem>();
        static long _nextId = 1;
        static Timer _saveTimer;
        static BarSettings _s;

        /// <summary>A card to show (raised on any thread).</summary>
        public static event Action<NotifyItem> Show;
        /// <summary>The history changed (added, read, cleared).</summary>
        public static event Action Changed;
        /// <summary>A notification changed quietly (another app joined an outage): its card is updated if it's showing - no new card, no sound.</summary>
        public static event Action<NotifyItem> Updated;
        /// <summary>A notification with a Ref was read here (clicked, its card closed, Mark all read): tell where it came from.</summary>
        [PluginApi] public static event Action<NotifyItem> ItemRead;
        /// <summary>Set by the UI: is a full-screen app (game, video) in front.</summary>
        public static Func<bool> FullScreenNow;

        public static void Configure(BarSettings s) { _s = s; Load(); NotifyNames.Configure(s); }

        /// <summary>The sorts of notification whose kind can be picked on the Notifications page: key, and the kind they have by default.</summary>
        [PluginApi] public static readonly Dictionary<string, NotifyLevel> Kinds = new Dictionary<string, NotifyLevel>
        {
            { "Services.Down", NotifyLevel.Alert }, { "Services.Up", NotifyLevel.Normal },
            { "System.Warning", NotifyLevel.Alert },
            { "Weather.Rain", NotifyLevel.Normal },
        };

        /// <summary>The kind picked for this sort of notification (or its default).</summary>
        [PluginApi] public static NotifyLevel KindOf(string key)
        {
            NotifyLevel def;
            if (!Kinds.TryGetValue(key, out def)) def = NotifyLevel.Normal;
            BarSettings s = _s;
            string v;
            NotifyLevel l;
            if (s != null && s.NotifyKinds != null && s.NotifyKinds.TryGetValue(key, out v) && Enum.TryParse(v, out l)) return l;
            return def;
        }

        static string FilePath { get { return Path.Combine(BarSettings.ConfigDir, "notifications.json"); } }

        static void Load()
        {
            lock (Gate)
            {
                if (_history != null) return;
                _history = new List<NotifyItem>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        List<NotifyItem> l = JsonSerializer.Deserialize<List<NotifyItem>>(File.ReadAllText(FilePath));
                        if (l != null) _history.AddRange(l);
                    }
                }
                catch (Exception ex) { Log.Write("Notifications history: " + ex.Message); }
                foreach (NotifyItem i in _history) if (i.Id >= _nextId) _nextId = i.Id + 1;
            }
        }

        static void SaveSoon()
        {
            lock (Gate)
            {
                if (_saveTimer == null) _saveTimer = new Timer(delegate { Save(); }, null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(2000, Timeout.Infinite);
            }
        }

        static void Save()
        {
            try
            {
                string json;
                lock (Gate) json = JsonSerializer.Serialize(_history);
                Directory.CreateDirectory(BarSettings.ConfigDir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("Notifications history save: " + ex.Message); }
        }

        static void Fire(Action a) { if (a != null) try { a(); } catch (Exception ex) { Log.Write("Notifications: " + ex.Message); } }

        /// <summary>Raise a notification; bad = an Alert, otherwise Normal. keep = false: show it only (not in the history).</summary>
        [PluginApi] public static NotifyItem Post(string source, string title, string detail, string url, bool bad, bool keep = true)
        {
            return Post(source, title, detail, url, bad ? NotifyLevel.Alert : NotifyLevel.Normal, keep);
        }

        /// <summary>Raise a notification at a level. keep = false: show it only (not in the history).</summary>
        [PluginApi] public static NotifyItem Post(string source, string title, string detail, string url, NotifyLevel level, bool keep = true, string image = null)
        {
            return Post(source, title, detail, url, level, keep, image, null, true, DateTime.UtcNow);
        }

        /// <summary>
        /// The full version: reference = where it lives outside vShell ("vdrop:42"); show = false puts it in the history only (no card);
        /// at = when it happened.
        /// </summary>
        [PluginApi] public static NotifyItem Post(string source, string title, string detail, string url, NotifyLevel level, bool keep, string image,
                                      string reference, bool show, DateTime at)
        {
            Load();
            BarSettings s = _s;
            // the same again within a few minutes: count it on the one already there (its card is updated, not another one added).
            // Not ones that live somewhere else (vDrop): each of those has its own read state there.
            int window = s != null ? s.NotifyCollapseMinutes : 10;
            if (keep && window > 0 && string.IsNullOrEmpty(reference))
            {
                NotifyItem same = null;
                lock (Gate)
                {
                    DateTime since = DateTime.UtcNow.AddMinutes(-window);
                    foreach (NotifyItem h in _history)
                    {
                        if (h.At < since) break;           // newest first
                        if (h.Source == (source ?? "") && h.Title == (title ?? "") && h.Level == level.ToString() && string.IsNullOrEmpty(h.Ref)) { same = h; break; }
                    }
                    if (same != null)
                    {
                        same.Count = Math.Max(1, same.Count) + 1;
                        same.At = at; same.Detail = detail ?? ""; same.Url = url ?? ""; same.Image = image ?? ""; same.Read = false;
                        _history.Remove(same);
                        _history.Insert(0, same);
                    }
                }
                if (same != null)
                {
                    SaveSoon(); Fire(Changed);
                    if (show)
                    {
                        string whyQ;
                        if (Quiet(s, out whyQ)) { lock (Gate) if (!Held.Contains(same)) Held.Add(same); }
                        else ShowNow(same);
                    }
                    return same;
                }
            }
            var item = new NotifyItem
            {
                At = at, Source = source ?? "", Title = title ?? "", Detail = detail ?? "", Url = url ?? "",
                Level = level.ToString(), Bad = level == NotifyLevel.Alert, Keep = keep, Image = image ?? "", Ref = reference ?? ""
            };
            int max = s != null ? s.NotifyHistoryMax : 100;
            lock (Gate)
            {
                item.Id = _nextId++;
                if (keep)
                {
                    _history.Insert(0, item);
                    if (_history.Count > max) _history.RemoveRange(max, _history.Count - max);
                }
            }
            if (keep) { SaveSoon(); Fire(Changed); }
            if (!show) return item;
            string why;
            if (Quiet(s, out why))
            {
                lock (Gate) Held.Add(item);
                return item;
            }
            ShowNow(item);
            return item;
        }

        // =================================================================
        // About something: one entry per outage, whichever apps say so
        // =================================================================

        /// <summary>
        /// Raise a notification about something (NotifyAbout: its names, and whether it's down / back up). With Settings > Notifications >
        /// The same thing, once on, a "down" from another app about the same thing joins the outage already there (its card updated quietly,
        /// no new one), and "back up" closes it ("nuc01 is back · down 4 min"). Other states are ordinary notifications that keep the names.
        /// </summary>
        [PluginApi] public static NotifyItem Post(string source, string title, string detail, string url, NotifyLevel level, NotifyAbout about)
        {
            if (about == null || about.Name.Length == 0) return Post(source, title, detail, url, level);
            Load();
            source = source ?? ""; title = title ?? ""; detail = detail ?? ""; url = url ?? "";
            List<string> names = about.All();
            NotifyNames.Learn(source, names);
            BarSettings s = _s;
            bool join = s == null || (s.NotifySameOnce && (s.NotifyApart == null || !s.NotifyApart.Contains(source)));
            if (!join || about.State == NotifyState.Other) return Stamp(Post(source, title, detail, url, level), about, names);

            DateTime now = DateTime.UtcNow;
            TimeSpan window = TimeSpan.FromMinutes(s != null ? s.NotifyJoinMinutes : 5);
            var say = new NotifySay { Source = source, Title = title, Detail = detail, Url = url, At = now, State = about.State.ToString() };
            NotifyItem outage = null;
            lock (Gate)
                foreach (NotifyItem h in _history)
                {
                    if (h.Says == null) continue;
                    if (!h.Open && now - h.At > window) { if (now - h.At > TimeSpan.FromDays(2)) break; continue; }
                    if (NotifyNames.Same(h.Names, names)) { outage = h; break; }
                }

            if (about.State == NotifyState.Down)
            {
                if (outage != null && outage.Open)
                {
                    lock (Gate)
                    {
                        Tell(outage, say, names);
                        if (level > outage.Kind) { outage.Level = level.ToString(); outage.Bad = level == NotifyLevel.Alert; }
                        if (outage.Joined) { outage.Title = outage.Thing + " is down"; outage.Detail = Who(outage) + " · since " + TimeText.Clock(outage.Since.ToLocalTime()); }
                        outage.At = now;
                        _history.Remove(outage);
                        _history.Insert(0, outage);
                    }
                    SaveSoon(); Fire(Changed); UpdateCard(outage);
                    return outage;
                }
                // a new outage
                var item = new NotifyItem
                {
                    At = now, Source = source, Title = title, Detail = detail, Url = url, Level = level.ToString(), Bad = level == NotifyLevel.Alert,
                    Thing = about.Name, Names = new List<string>(names), Says = new List<NotifySay> { say }, Open = true, Since = now, Back = DateTime.MinValue
                };
                Add(item, s);
                int blip = s != null ? s.NotifyBlipSeconds : 0;
                if (blip <= 0) ShowOrHold(item, s);
                else
                    Task.Delay(TimeSpan.FromSeconds(blip)).ContinueWith(delegate { if (item.Open && !item.CardShown) ShowOrHold(item, _s); });
                return item;
            }

            // back up
            if (outage == null) return Stamp(Post(source, title, detail, url, level), about, names);
            if (!outage.Open)
            {
                // another app saying it's back, after the outage closed: a line under it, nothing on screen
                lock (Gate) Tell(outage, say, names);
                SaveSoon(); Fire(Changed); UpdateCard(outage);
                return outage;
            }
            bool closes = s == null || s.NotifyBackCloses;
            // back before its held card showed (Settings: a new outage's card waits): a blip - no card at all
            TimeSpan down = now - outage.Since;
            int wait = s != null ? s.NotifyBlipSeconds : 0;
            bool shown = outage.CardShown || wait <= 0 || down.TotalSeconds > wait + 5;
            lock (Gate)
            {
                Tell(outage, say, names);
                outage.Open = false;
                outage.Back = now;
                if (!shown) outage.Title = outage.Thing + " dropped out for " + Span(down);
                else outage.Title = outage.Thing + " is back · down " + Span(down);
                outage.Detail = TimeText.Clock(outage.Since.ToLocalTime()) + " – " + TimeText.Clock(now.ToLocalTime()) + " · " + Who(outage);
                outage.Level = level.ToString(); outage.Bad = level == NotifyLevel.Alert;
                outage.Url = url.Length > 0 ? url : outage.Url;
                outage.Read = true;           // over: nothing to read
                outage.At = now;
                _history.Remove(outage);
                _history.Insert(0, outage);
            }
            SaveSoon(); Fire(Changed);
            if (!shown) return outage;                         // a blip: back before its card showed - no card at all
            if (closes) { outage.Quietly = true; string why; if (Quiet(s, out why)) UpdateCard(outage); else ShowNow(outage); }
            else { UpdateCard(outage); Stamp(Post(source, title, detail, url, level), about, names); }
            return outage;
        }

        /// <summary>Lists showing the history draw it again (a joined entry unfolded).</summary>
        public static void Touch() { Fire(Changed); }

        /// <summary>A plugin knows these names are one thing (vDeploy: a host and its address) - without a notification.</summary>
        [PluginApi] public static void SameThing(string source, params string[] names) { NotifyNames.Learn(source, names); }

        static NotifyItem Stamp(NotifyItem item, NotifyAbout about, List<string> names)
        {
            if (item == null || item.Says != null) return item;
            lock (Gate) { item.Thing = about.Name; item.Names = new List<string>(names); }
            return item;
        }

        /// <summary>One app's word on an outage: its line (replacing what it said before), and any names it adds.</summary>
        static void Tell(NotifyItem outage, NotifySay say, List<string> names)
        {
            int at = outage.Says.FindIndex(delegate (NotifySay x) { return x.Source == say.Source; });
            if (at >= 0) outage.Says[at] = say;          // its line keeps its place (the apps stay in the order they spoke)
            else outage.Says.Add(say);
            if (outage.Names == null) outage.Names = new List<string>();
            foreach (string n in names) if (!outage.Names.Contains(n)) outage.Names.Add(n);
        }

        /// <summary>"Uptime Kuma, vDeploy and Proxmox".</summary>
        public static string Who(NotifyItem item)
        {
            var l = new List<string>();
            if (item.Says != null) foreach (NotifySay x in item.Says) if (!l.Contains(x.Source)) l.Add(x.Source);
            if (l.Count == 0) return item.Source;
            if (l.Count == 1) return l[0];
            return string.Join(", ", l.GetRange(0, l.Count - 1)) + " and " + l[l.Count - 1];
        }

        /// <summary>"40 s", "4 min", "1 h 12 min".</summary>
        public static string Span(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return Math.Max(1, (int)Math.Round(t.TotalSeconds)) + " s";
            if (t.TotalMinutes < 60) return (int)Math.Round(t.TotalMinutes) + " min";
            int h = (int)t.TotalHours, m = t.Minutes;
            return h + " h" + (m > 0 ? " " + m + " min" : "");
        }

        static void Add(NotifyItem item, BarSettings s)
        {
            int max = s != null ? s.NotifyHistoryMax : 100;
            lock (Gate)
            {
                item.Id = _nextId++;
                _history.Insert(0, item);
                if (_history.Count > max) _history.RemoveRange(max, _history.Count - max);
            }
            SaveSoon(); Fire(Changed);
        }

        static void ShowOrHold(NotifyItem item, BarSettings s)
        {
            string why;
            item.CardShown = true;          // held counts: it shows when the quiet time ends
            if (Quiet(s, out why)) { lock (Gate) if (!Held.Contains(item)) Held.Add(item); return; }
            ShowNow(item);
        }

        /// <summary>Its card is updated where it is (if it's showing) - nothing new pops up.</summary>
        static void UpdateCard(NotifyItem item)
        {
            Action<NotifyItem> a = Updated;
            if (a != null) try { a(item); } catch (Exception ex) { Log.Write("Notification card: " + ex.Message); }
        }

        static void ShowNow(NotifyItem item)
        {
            item.CardShown = true;
            Action<NotifyItem> a = Show;
            if (a != null) try { a(item); } catch (Exception ex) { Log.Write("Notification card: " + ex.Message); }
        }

        /// <summary>Show what was held, once nothing holds it any more (the UI calls this every couple of seconds).</summary>
        public static void ReleaseHeld()
        {
            List<NotifyItem> out_;
            lock (Gate)
            {
                if (Held.Count == 0) return;
            }
            string why;
            if (Quiet(_s, out why)) return;
            lock (Gate) { out_ = new List<NotifyItem>(Held); Held.Clear(); }
            // the newest few; the rest are in the history
            int skip = Math.Max(0, out_.Count - 6);
            for (int i = skip; i < out_.Count; i++) ShowNow(out_[i]);
            if (skip > 0)
                ShowNow(new NotifyItem { Id = -1, At = DateTime.UtcNow, Source = "vShell", Title = "+ " + skip + " more while you were busy", Detail = "They are in the notification history.", Url = "vshell:history", Keep = false });
        }

        public static int HeldCount { get { lock (Gate) return Held.Count; } }

        /// <summary>Is something holding notifications back now (why = what).</summary>
        public static bool Quiet(BarSettings s, out string why)
        {
            why = "";
            if (s == null) return false;
            try
            {
                Func<bool> fs = FullScreenNow;
                if (s.NotifyHoldFullScreen && fs != null && fs()) { why = "a full-screen app is in front"; return true; }
            }
            catch { }
            if (s.NotifyQuiet && InQuietHours(s.NotifyQuietFrom, s.NotifyQuietTo, DateTime.Now)) { why = "quiet hours"; return true; }
            if (s.NotifyFollowWindows && WindowsBusy()) { why = "Windows is in Do not disturb / presentation mode"; return true; }
            return false;
        }

        /// <summary>"23:00".."07:00" (over midnight works).</summary>
        public static bool InQuietHours(string from, string to, DateTime now)
        {
            TimeSpan a, b;
            if (!ParseTime(from, out a) || !ParseTime(to, out b) || a == b) return false;
            TimeSpan t = now.TimeOfDay;
            return a < b ? t >= a && t < b : t >= a || t < b;
        }

        public static bool ParseTime(string text, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            text = (text ?? "").Trim();
            DateTime d;
            string[] formats = { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "htt", "h tt", "H" };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) { t = d.TimeOfDay; return true; }
            return false;
        }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        /// <summary>Windows says not to bother the user now (busy, D3D full screen, presentation mode, quiet time).</summary>
        static bool WindowsBusy()
        {
            try
            {
                int st;
                if (SHQueryUserNotificationState(out st) != 0) return false;
                return st == 2 || st == 3 || st == 4 || st == 6;
            }
            catch { return false; }
        }

        // ---- the history ----
        public static List<NotifyItem> History() { Load(); lock (Gate) return new List<NotifyItem>(_history); }

        public static int Unread()
        {
            Load();
            int n = 0;
            lock (Gate) foreach (NotifyItem i in _history) if (!i.Read) n++;
            return n;
        }

        public static void MarkAllRead()
        {
            Load();
            var read = new List<NotifyItem>();
            lock (Gate) foreach (NotifyItem i in _history) if (!i.Read) { i.Read = true; read.Add(i); }
            if (read.Count > 0) { SaveSoon(); Fire(Changed); TellRead(read); }
        }

        public static void MarkRead(long id)
        {
            Load();
            var read = new List<NotifyItem>();
            lock (Gate) foreach (NotifyItem i in _history) if (i.Id == id && !i.Read) { i.Read = true; read.Add(i); }
            if (read.Count > 0) { SaveSoon(); Fire(Changed); TellRead(read); }
        }

        /// <summary>Read somewhere else (e.g. on the phone in vDrop): read here too, without telling it back.</summary>
        [PluginApi] public static void MarkReadByRef(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return;
            Load();
            bool any = false;
            lock (Gate) foreach (NotifyItem i in _history) if (i.Ref == reference && !i.Read) { i.Read = true; any = true; }
            if (any) { SaveSoon(); Fire(Changed); }
        }

        static void TellRead(List<NotifyItem> items)
        {
            Action<NotifyItem> h = ItemRead;
            if (h == null) return;
            foreach (NotifyItem i in items)
                if (!string.IsNullOrEmpty(i.Ref)) try { h(i); } catch (Exception ex) { Log.Write("Notifications (read): " + ex.Message); }
        }

        public static void Remove(long id)
        {
            Load();
            int n;
            lock (Gate) n = _history.RemoveAll(delegate (NotifyItem i) { return i.Id == id; });
            if (n > 0) { SaveSoon(); Fire(Changed); }
        }

        public static void Clear()
        {
            Load();
            lock (Gate) _history.Clear();
            SaveSoon();
            Fire(Changed);
        }

        /// <summary>How long ago, the way every time in vShell is written (TimeText.Ago).</summary>
        public static string When(DateTime utc) { return TimeText.Ago(utc); }
    }
}
