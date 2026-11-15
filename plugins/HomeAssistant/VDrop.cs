using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>One vDrop item: a shared link, a note, an image or a video, with its preview.</summary>
    public sealed class VDropItem
    {
        public string Id { get; set; } = "";
        /// <summary>"link", "note", "image" or "video".</summary>
        public string Type { get; set; } = "note";
        public string Url { get; set; }
        /// <summary>The note, or the comment shared with a link.</summary>
        public string Text { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string SiteName { get; set; }
        public string Author { get; set; }
        /// <summary>The preview's accent colour ("#rrggbb"), the card's left edge.</summary>
        public string Color { get; set; }
        /// <summary>File names of the picture / site icon (fetched from vDrop's /thumbs/ and kept on this PC).</summary>
        public string Thumb { get; set; }
        public string Favicon { get; set; }
        public int ThumbW { get; set; }
        public int ThumbH { get; set; }
        /// <summary>"large" = full width under the text, "small" = a square on the right.</summary>
        public string ThumbStyle { get; set; }
        public string VideoUrl { get; set; }
        /// <summary>A playable embed (YouTube, Vimeo …): its provider, or empty.</summary>
        public string Embed { get; set; }
        /// <summary>"pending", "ok", "error" or "none".</summary>
        public string Preview { get; set; } = "none";
        /// <summary>"android", "ios", "web" or "vshell"; the device's name; the app it was shared from.</summary>
        public string Source { get; set; }
        public string Device { get; set; }
        public string Via { get; set; }
        public bool Pinned { get; set; }
        public bool Done { get; set; }
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }

        // ---- here only ----
        /// <summary>Added here, not in vDrop yet.</summary>
        public bool New { get; set; }
        /// <summary>Text / title changed here and not sent yet.</summary>
        public bool Dirty { get; set; }
        public bool PinDirty { get; set; }
        public bool DoneDirty { get; set; }
        public bool RefreshWanted { get; set; }
        /// <summary>Added without an id coming back: the text sent, to match it when it comes back.</summary>
        public string Sent { get; set; }
        public DateTime SentAt { get; set; }

        public bool IsMedia { get { return Type == "image" || Type == "video" || !string.IsNullOrEmpty(Embed); } }
    }

    /// <summary>
    /// Two-way sync between Notes modules (store "vDrop") and the vDrop Home Assistant add-on: every item - notes, links with their
    /// previews, images and videos - shows in vShell, and notes / links added, edited, pinned, archived or deleted on either side
    /// follow on the other. Everything runs through Home Assistant with the same address and token as the rest of vShell:
    ///   - its own WebSocket              "vdrop_changed" events (live), finding the add-on (get_states / ping), an ingress session
    ///   - ingress HTTP + session cookie  /api/changes?since=REV to read, /api/items to add / change / delete, /thumbs/ for pictures
    /// Items are kept in vdrop-items.json and pictures in vdrop-thumbs\, so the feed shows at once (and offline); changes made offline
    /// are sent when it's back. Items live on the UI thread; the network work runs in the background and hands results back.
    /// </summary>
    public static class VDrop
    {
        /// <summary>Every item (any order; the feed sorts and filters). UI thread only.</summary>
        public static readonly List<VDropItem> Items = new List<VDropItem>();
        /// <summary>The items or the status changed (on the UI thread).</summary>
        public static event Action Changed;
        /// <summary>A picture arrived (its file name) - on the UI thread.</summary>
        public static event Action<string> ThumbReady;
        /// <summary>"" = connected and in sync; otherwise what's going on ("Connecting…", a problem).</summary>
        public static string Status { get; private set; } = "Connecting…";

        static readonly object Gate = new object();
        static readonly List<string> Deletes = new List<string>();            // ids deleted here, still to send
        static readonly List<string> ThumbQueue = new List<string>();         // pictures to fetch
        static readonly HashSet<string> ThumbFailed = new HashSet<string>();
        static long _rev;
        static int _users;
        static bool _loaded;
        static string _url = "", _token = "";
        static bool _ignoreCert;
        static CancellationTokenSource _cts;
        static SemaphoreSlim _kick = new SemaphoreSlim(0);
        static Timer _saveTimer;

        // (vdrop-cache.json was the first, notes-only cache: a new file, so the whole feed is fetched once)
        static string CachePath { get { return Path.Combine(BarSettings.ConfigDir, "vdrop-items.json"); } }
        static string ThumbDir { get { return Path.Combine(BarSettings.ConfigDir, "vdrop-thumbs"); } }
        static readonly Regex UrlRx = new Regex(@"https?://\S+", RegexOptions.IgnoreCase);

        // =================================================================
        // Starting / stopping
        // =================================================================
        /// <summary>Home Assistant's address and token (from HaClient; empty = not set up).</summary>
        public static void Configure(string url, string token, bool ignoreCert)
        {
            lock (Gate)
            {
                if (url == _url && token == _token && ignoreCert == _ignoreCert) return;
                _url = url ?? ""; _token = token ?? ""; _ignoreCert = ignoreCert;
            }
            Restart();
        }

        /// <summary>A Notes module on vDrop appeared (true) or went away (false): the sync only runs while one is in use.</summary>
        public static void Use(bool on)
        {
            LoadCache();
            lock (Gate) _users = Math.Max(0, _users + (on ? 1 : -1));
            StartOrStop();
        }
        static Timer _stopTimer;
        static bool _notifOn, _notifHooked;

        /// <summary>vDrop's notifications (Settings > Notifications): they need the connection even without a Notes module on vDrop.</summary>
        public static void Notifications(bool on)
        {
            LoadCache();
            if (!_notifHooked)
            {
                _notifHooked = true;
                // read here (clicked, card closed, Mark all read) -> read in vDrop
                Notify.ItemRead += delegate (NotifyItem item)
                {
                    long id;
                    if (item.Ref == null || !item.Ref.StartsWith("vdrop:") || !long.TryParse(item.Ref.Substring(6), out id)) return;
                    lock (Gate) { if (!ReadQueue.Contains(id)) ReadQueue.Add(id); }
                    SaveSoon();
                    Kick();
                };
            }
            bool changed;
            lock (Gate) { changed = _notifOn != on; _notifOn = on; }
            if (changed) StartOrStop();
        }

        static bool Active { get { lock (Gate) return _users > 0 || _notifOn; } }

        static void StartOrStop()
        {
            bool running;
            lock (Gate) running = _cts != null;
            if (Active) { if (!running) Restart(); return; }
            // a widget being rebuilt goes away and comes straight back: only stop once nothing has used it for a few seconds
            if (_stopTimer == null) _stopTimer = new Timer(delegate { if (!Active) Restart(); });
            _stopTimer.Change(5000, Timeout.Infinite);
        }

        /// <summary>Read the notes kept on this PC (once; UI thread).</summary>
        public static void Prepare() { LoadCache(); }

        /// <summary>Sync now (menu "Sync now").</summary>
        public static void SyncNow() { Kick(); }

        static void Kick() { try { _kick.Release(); } catch { } }

        static void Restart()
        {
            string url, token; bool ignore; int users;
            lock (Gate)
            {
                if (_cts != null) { try { _cts.Cancel(); } catch { } _cts = null; }
                url = _url; token = _token; ignore = _ignoreCert; users = _users + (_notifOn ? 1 : 0);
                if (users == 0) return;
                if (url.Length == 0 || token.Length == 0) { SetStatus("Set up the Home Assistant connection first (Settings > Plugins)."); return; }
                _cts = new CancellationTokenSource();
            }
            CancellationToken ct = _cts.Token;
            _kick = new SemaphoreSlim(0);
            SetStatus("Connecting…");
            Task.Run(delegate { return Run(url, token, ignore, ct); });
        }

        static void SetStatus(string s)
        {
            if (Status == s) return;
            Status = s;
            Ui(delegate { Raise(); });
        }

        static void Ui(Action a)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(a);
        }

        static void Raise() { Action h = Changed; if (h != null) h(); }

        // =================================================================
        // Local changes (UI thread) - called by the feed
        // =================================================================
        /// <summary>Add a note or a link (a URL, or a URL with words) - vDrop works out which.</summary>
        public static VDropItem Add(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            Match m = UrlRx.Match(text);
            var it = new VDropItem
            {
                New = true, Text = text, Type = m.Success ? "link" : "note", Url = m.Success ? m.Value : null, Preview = m.Success ? "pending" : "none",
                Source = "vshell", Device = Environment.MachineName, Created = DateTime.UtcNow, Updated = DateTime.UtcNow
            };
            Items.Add(it);
            Touched();
            return it;
        }

        /// <summary>New text (and title, for links; null = leave it).</summary>
        public static void Edit(VDropItem it, string text, string title)
        {
            if (it == null) return;
            it.Text = text;
            if (title != null) it.Title = title;
            it.Dirty = true;
            it.Updated = DateTime.UtcNow;
            Touched();
        }

        public static void SetPinned(VDropItem it, bool pinned)
        {
            if (it == null || it.Pinned == pinned) return;
            it.Pinned = pinned; it.PinDirty = true;
            Touched();
        }

        public static void SetDone(VDropItem it, bool done)
        {
            if (it == null || it.Done == done) return;
            it.Done = done; it.DoneDirty = true;
            Touched();
        }

        public static void Delete(VDropItem it)
        {
            if (it == null) return;
            Items.Remove(it);
            if (!it.New && it.Id.Length > 0) lock (Gate) { if (!Deletes.Contains(it.Id)) Deletes.Add(it.Id); }
            Touched();
        }

        /// <summary>Ask vDrop to fetch the link's preview again.</summary>
        public static void RefreshPreview(VDropItem it)
        {
            if (it == null || it.New || string.IsNullOrEmpty(it.Url)) return;
            it.RefreshWanted = true;
            it.Preview = "pending";
            Touched();
        }

        static void Touched()
        {
            SaveSoon();
            Kick();
            Raise();
        }

        /// <summary>A picture's file on this PC, or null (then it's fetched, and ThumbReady says when it's there).</summary>
        public static string ThumbPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = Path.GetFileName(name);
            string path = Path.Combine(ThumbDir, name);
            if (File.Exists(path)) return path;
            lock (Gate)
            {
                if (!ThumbFailed.Contains(name) && !ThumbQueue.Contains(name)) ThumbQueue.Add(name);
            }
            Kick();
            return null;
        }

        // =================================================================
        // Reading: /api/changes?since=REV
        // =================================================================
        static async Task Pull(Link link, CancellationToken ct)
        {
            long since;
            lock (Gate) since = _rev;
            KeyValuePair<HttpStatusCode, string> r = await Http(link, HttpMethod.Get, "/api/changes?since=" + since, null, ct);
            if ((int)r.Key >= 300) throw new InvalidOperationException("vDrop answered " + (int)r.Key);
            bool full = false; long rev = since;
            var items = new List<VDropItem>();
            var deleted = new List<string>();
            using (JsonDocument d = JsonDocument.Parse(r.Value))
            {
                JsonElement root = d.RootElement;
                if (root.TryGetProperty("full", out JsonElement f)) full = f.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("rev", out JsonElement rv) && rv.TryGetInt64(out long rr)) rev = rr;
                if (root.TryGetProperty("items", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in arr.EnumerateArray()) { VDropItem it = ParseItem(e); if (it.Id.Length > 0) items.Add(it); }
                if (root.TryGetProperty("deleted", out JsonElement del) && del.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in del.EnumerateArray())
                    {
                        string id = e.ValueKind == JsonValueKind.String ? e.GetString() : Str(e, "id");
                        if (!string.IsNullOrEmpty(id)) deleted.Add(id);
                    }
            }
            if (!full && items.Count == 0 && deleted.Count == 0 && rev == since) return;
            var tcs = new TaskCompletionSource<bool>();
            Ui(delegate { try { Merge(full, items, deleted, rev); } finally { tcs.TrySetResult(true); } });
            await tcs.Task;
        }

        static VDropItem ParseItem(JsonElement e)
        {
            var it = new VDropItem
            {
                Id = Str(e, "id"), Type = Str(e, "type"), Url = Null(Str(e, "url")), Text = Null(Str(e, "text")), Title = Null(Str(e, "title")),
                Description = Null(Str(e, "description")), SiteName = Null(Str(e, "site_name")), Author = Null(Str(e, "author")),
                Color = Null(Str(e, "color")), Thumb = Null(Str(e, "thumb")), Favicon = Null(Str(e, "favicon")), ThumbStyle = Null(Str(e, "thumb_style")),
                VideoUrl = Null(Str(e, "video_url")), Preview = Str(e, "preview"), Source = Null(Str(e, "source")), Device = Null(Str(e, "device")),
                Via = Null(Str(e, "via"))
            };
            if (it.Type.Length == 0) it.Type = it.Url != null ? "link" : "note";
            if (it.Preview.Length == 0) it.Preview = "none";
            it.ThumbW = Int(e, "thumb_w"); it.ThumbH = Int(e, "thumb_h");
            if (e.TryGetProperty("embed", out JsonElement em) && em.ValueKind == JsonValueKind.Object) it.Embed = Str(em, "provider");
            if (string.IsNullOrEmpty(it.Embed)) it.Embed = null;
            it.Pinned = e.TryGetProperty("pinned", out JsonElement p) && p.ValueKind == JsonValueKind.True;
            it.Done = e.TryGetProperty("done", out JsonElement dn) && dn.ValueKind == JsonValueKind.True;
            it.Created = Time(Str(e, "created"));
            it.Updated = Time(Str(e, "updated"));
            if (it.Updated == DateTime.MinValue) it.Updated = it.Created;
            return it;
        }

        static string Null(string s) { return string.IsNullOrEmpty(s) ? null : s; }

        static int Int(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
        }

        static DateTime Time(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                                     System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t) ? t : DateTime.MinValue;
        }

        /// <summary>vDrop's server copy into ours: everything, except what we changed here and haven't sent yet.</summary>
        static void CopyFrom(VDropItem to, VDropItem from)
        {
            to.Id = from.Id; to.Type = from.Type; to.Url = from.Url;
            if (!to.Dirty) { to.Text = from.Text; to.Title = from.Title; }
            to.Description = from.Description; to.SiteName = from.SiteName; to.Author = from.Author; to.Color = from.Color;
            to.Thumb = from.Thumb; to.Favicon = from.Favicon; to.ThumbW = from.ThumbW; to.ThumbH = from.ThumbH; to.ThumbStyle = from.ThumbStyle;
            to.VideoUrl = from.VideoUrl; to.Embed = from.Embed;
            if (!to.RefreshWanted) to.Preview = from.Preview;
            to.Source = from.Source; to.Device = from.Device; to.Via = from.Via;
            if (!to.PinDirty) to.Pinned = from.Pinned;
            if (!to.DoneDirty) to.Done = from.Done;
            if (from.Created > DateTime.MinValue) to.Created = from.Created;
            if (from.Updated > DateTime.MinValue) to.Updated = from.Updated;
            to.New = false;
        }

        static void Merge(bool full, List<VDropItem> items, List<string> deleted, long rev)
        {
            var byId = new Dictionary<string, VDropItem>();
            foreach (VDropItem x in Items) if (!x.New && x.Id.Length > 0) byId[x.Id] = x;
            var seen = new HashSet<string>();
            List<string> gone;
            lock (Gate) gone = new List<string>(Deletes);
            foreach (VDropItem it in items)
            {
                seen.Add(it.Id);
                if (gone.Contains(it.Id)) continue;           // deleted here, on its way
                VDropItem have;
                if (!byId.TryGetValue(it.Id, out have))
                {
                    // one we added whose answer had no id: the same text, not yet linked
                    have = Items.Find(delegate (VDropItem x) { return x.New && x.Sent != null && x.Sent == (it.Text ?? ""); });
                    if (have == null) { have = new VDropItem(); Items.Add(have); }
                    byId[it.Id] = have;
                }
                CopyFrom(have, it);
            }
            foreach (string id in deleted)
            {
                VDropItem have;
                if (byId.TryGetValue(id, out have)) Items.Remove(have);
            }
            if (full) Items.RemoveAll(delegate (VDropItem x) { return !x.New && !seen.Contains(x.Id) && !x.Dirty; });
            lock (Gate) _rev = rev;
            SaveSoon();
            Raise();
        }

        // =================================================================
        // Writing: /api/items
        // =================================================================
        sealed class Job
        {
            public VDropItem It;
            public string Id, Text, Title;
            public bool Add, Fields, Pin, Pinned, DoneSet, Done, Refresh;
        }

        static async Task Push(Link link, CancellationToken ct)
        {
            var jobs = new List<Job>();
            var tcs = new TaskCompletionSource<bool>();
            Ui(delegate
            {
                try
                {
                    foreach (VDropItem x in Items)
                    {
                        if (x.New)
                        {
                            if ((x.Text ?? "").Trim().Length == 0) continue;
                            if (x.Sent != null && (DateTime.UtcNow - x.SentAt).TotalMinutes < 2) continue;     // sent; waiting to see it come back
                            jobs.Add(new Job { It = x, Add = true, Text = x.Text, Title = x.Title ?? "", Pinned = x.Pinned, Done = x.Done });
                        }
                        else if (x.Dirty || x.PinDirty || x.DoneDirty || x.RefreshWanted)
                            jobs.Add(new Job
                            {
                                It = x, Id = x.Id, Text = x.Text ?? "", Title = x.Title ?? "", Fields = x.Dirty, Pin = x.PinDirty, Pinned = x.Pinned,
                                DoneSet = x.DoneDirty, Done = x.Done, Refresh = x.RefreshWanted
                            });
                    }
                }
                finally { tcs.TrySetResult(true); }
            });
            await tcs.Task;

            List<string> deletes;
            lock (Gate) deletes = new List<string>(Deletes);
            foreach (string id in deletes)
            {
                KeyValuePair<HttpStatusCode, string> r = await Http(link, HttpMethod.Delete, "/api/items/" + Uri.EscapeDataString(id), null, ct);
                if ((int)r.Key < 300 || r.Key == HttpStatusCode.NotFound) lock (Gate) Deletes.Remove(id);
                else Log.Write("vDrop: delete " + id + ": " + (int)r.Key + " " + r.Value);
            }

            foreach (Job j in jobs)
            {
                Job job = j;
                if (j.Add)
                {
                    var body = new Dictionary<string, object> { { "text", j.Text }, { "source", "vshell" }, { "device", Environment.MachineName } };
                    if (j.Title.Length > 0) body["title"] = j.Title;
                    KeyValuePair<HttpStatusCode, string> r = await Http(link, HttpMethod.Post, "/api/items", JsonSerializer.Serialize(body), ct);
                    if ((int)r.Key >= 300) { Log.Write("vDrop: add: " + (int)r.Key + " " + r.Value); continue; }
                    VDropItem back = null;
                    try
                    {
                        using (JsonDocument d = JsonDocument.Parse(r.Value))
                        {
                            JsonElement e = d.RootElement;
                            if (e.ValueKind == JsonValueKind.Object && Str(e, "id").Length == 0 && e.TryGetProperty("item", out JsonElement inner)) e = inner;
                            if (e.ValueKind == JsonValueKind.Object && Str(e, "id").Length > 0) back = ParseItem(e);
                        }
                    }
                    catch (JsonException) { }
                    if (back != null && (j.Pinned || j.Done))
                    {
                        var extra = new Dictionary<string, object>();
                        if (j.Pinned) extra["pinned"] = true;
                        if (j.Done) extra["done"] = true;
                        await Http(link, new HttpMethod("PATCH"), "/api/items/" + Uri.EscapeDataString(back.Id), JsonSerializer.Serialize(extra), ct);
                        back.Pinned = j.Pinned; back.Done = j.Done;
                    }
                    VDropItem got = back;
                    Ui(delegate
                    {
                        bool same = (job.It.Text ?? "") == job.Text;
                        if (got != null)
                        {
                            // the change event can bring it back before this answer: keep just the one copy (this one)
                            Items.RemoveAll(delegate (VDropItem x) { return x != job.It && !x.New && x.Id == got.Id; });
                            bool keepText = !same;          // typed more while it was being sent: keep that, send it next
                            string text = job.It.Text;
                            CopyFrom(job.It, got);
                            if (keepText) { job.It.Text = text; job.It.Dirty = true; }
                            else job.It.Dirty = false;
                            job.It.PinDirty = job.It.DoneDirty = false;
                        }
                        else { job.It.Sent = job.Text; job.It.SentAt = DateTime.UtcNow; }
                        SaveSoon();
                        Raise();
                    });
                    continue;
                }

                var fields = new Dictionary<string, object>();
                if (j.Fields)
                {
                    fields["text"] = j.Text;
                    if (j.It.Url != null || j.Title.Length > 0) fields["title"] = j.Title.Length > 0 ? j.Title : null;
                }
                if (j.Pin) fields["pinned"] = j.Pinned;
                if (j.DoneSet) fields["done"] = j.Done;
                if (fields.Count > 0)
                {
                    KeyValuePair<HttpStatusCode, string> r = await Http(link, new HttpMethod("PATCH"), "/api/items/" + Uri.EscapeDataString(j.Id), JsonSerializer.Serialize(fields), ct);
                    if (r.Key == HttpStatusCode.NotFound)
                    {
                        // gone from vDrop meanwhile: add it again rather than lose what was typed
                        Ui(delegate { if (job.Fields) { job.It.New = true; job.It.Sent = null; } else Items.Remove(job.It); SaveSoon(); Kick(); Raise(); });
                        continue;
                    }
                    if ((int)r.Key >= 300) { Log.Write("vDrop: update " + j.Id + ": " + (int)r.Key + " " + r.Value); continue; }
                }
                if (j.Refresh)
                {
                    KeyValuePair<HttpStatusCode, string> r = await Http(link, HttpMethod.Post, "/api/items/" + Uri.EscapeDataString(j.Id) + "/refresh", "{}", ct);
                    if ((int)r.Key >= 300) Log.Write("vDrop: refresh " + j.Id + ": " + (int)r.Key + " " + r.Value);
                }
                Ui(delegate
                {
                    if (job.Fields && (job.It.Text ?? "") == job.Text && (job.It.Title ?? "") == job.Title) job.It.Dirty = false;
                    if (job.Pin && job.It.Pinned == job.Pinned) job.It.PinDirty = false;
                    if (job.DoneSet && job.It.Done == job.Done) job.It.DoneDirty = false;
                    if (job.Refresh) job.It.RefreshWanted = false;
                    SaveSoon();
                });
            }
        }

        // =================================================================
        // Pictures: /thumbs/{file}, kept in vdrop-thumbs\ (names are content hashes, so they never change)
        // =================================================================
        static async Task Thumbs(Link link, CancellationToken ct)
        {
            while (true)
            {
                string name;
                lock (Gate) { if (ThumbQueue.Count == 0) return; name = ThumbQueue[0]; }
                string path = Path.Combine(ThumbDir, name);
                bool ok = false;
                try
                {
                    if (!File.Exists(path))
                    {
                        using (var req = new HttpRequestMessage(HttpMethod.Get, link.Base + link.Entry + "/thumbs/" + Uri.EscapeDataString(name)))
                        {
                            req.Headers.TryAddWithoutValidation("Cookie", "ingress_session=" + link.Session);
                            using (HttpResponseMessage r = await link.Http.SendAsync(req, ct))
                            {
                                if (r.IsSuccessStatusCode)
                                {
                                    byte[] data = await r.Content.ReadAsByteArrayAsync();
                                    Directory.CreateDirectory(ThumbDir);
                                    File.WriteAllBytes(path + ".part", data);
                                    File.Copy(path + ".part", path, true);
                                    File.Delete(path + ".part");
                                    ok = true;
                                }
                                else Log.Write("vDrop: picture " + name + ": " + (int)r.StatusCode);
                            }
                        }
                    }
                    else ok = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Write("vDrop: picture " + name + ": " + ex.Message); }
                lock (Gate) { ThumbQueue.Remove(name); if (!ok) ThumbFailed.Add(name); }
                if (ok) { string n = name; Ui(delegate { Action<string> h = ThumbReady; if (h != null) h(n); }); }
            }
        }

        // =================================================================
        // Notifications from LAN apps (vDrop's Gotify-compatible endpoint) -> vShell's notifications
        // =================================================================
        static readonly List<long> ReadQueue = new List<long>();      // read here, still to tell vDrop
        static long _notifLast = -1;
        static bool _caughtUp, _notifMissing, _foundLogged;
        static int _notFound;
        static DateTime _beatAt = DateTime.MinValue;

        sealed class Note { public long Id; public string App = "", Title, Message = "", Url, Image; public int Priority; public DateTime At; }

        static async Task NotifWork(Link link, CancellationToken ct)
        {
            if (!_notifOn || _notifMissing) return;

            // "a vShell is online" (vDrop then keeps notifications off the phone, if set to)
            if ((DateTime.UtcNow - _beatAt).TotalSeconds >= 55)
            {
                KeyValuePair<HttpStatusCode, string> hb = await Http(link, HttpMethod.Post, "/api/clients/heartbeat",
                    JsonSerializer.Serialize(new Dictionary<string, string> { { "device", Environment.MachineName } }), ct);
                if (hb.Key == HttpStatusCode.NotFound) { _notifMissing = true; Log.Write("vDrop: this add-on version has no notifications (1.4.0 adds them)"); return; }
                _beatAt = DateTime.UtcNow;
            }

            // read here -> read in vDrop
            List<long> read;
            lock (Gate) read = new List<long>(ReadQueue);
            if (read.Count > 0)
            {
                KeyValuePair<HttpStatusCode, string> r = await Http(link, HttpMethod.Post, "/api/notifications/read",
                    JsonSerializer.Serialize(new Dictionary<string, object> { { "ids", read } }), ct);
                if ((int)r.Key < 300 || r.Key == HttpStatusCode.NotFound) { lock (Gate) ReadQueue.RemoveAll(delegate (long x) { return read.Contains(x); }); SaveSoon(); }
            }

            // what's new: unread ones after the newest this PC has had
            long last;
            lock (Gate) last = _notifLast;
            string q = last < 0 ? "?status=unread&limit=1" : "?status=unread&limit=200&after_id=" + last;
            KeyValuePair<HttpStatusCode, string> res = await Http(link, HttpMethod.Get, "/api/notifications" + q, null, ct);
            if (res.Key == HttpStatusCode.NotFound) { _notifMissing = true; return; }
            if ((int)res.Key >= 300) return;
            long lastId = last;
            var notes = new List<Note>();
            using (JsonDocument d = JsonDocument.Parse(res.Value))
            {
                JsonElement root = d.RootElement;
                if (root.TryGetProperty("last_id", out JsonElement li) && li.TryGetInt64(out long l)) lastId = l;
                if (root.TryGetProperty("items", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement e in arr.EnumerateArray())
                    {
                        if (!e.TryGetProperty("id", out JsonElement ie) || !ie.TryGetInt64(out long nid)) continue;
                        if (e.TryGetProperty("read", out JsonElement rd) && rd.ValueKind == JsonValueKind.True) continue;
                        notes.Add(new Note
                        {
                            Id = nid, App = Str(e, "app"), Title = Null(Str(e, "title")), Message = Str(e, "message"), Url = Null(Str(e, "url")),
                            Image = Null(Str(e, "image")), Priority = Int(e, "priority"), At = Time(Str(e, "created"))
                        });
                    }
            }
            bool first = last < 0;
            bool catchUp = !_caughtUp;
            _caughtUp = true;
            if (first)
            {
                // never connected before: start from now (what's there already stays in vDrop's tab)
                lock (Gate) _notifLast = lastId;
                SaveSoon();
                return;
            }
            if (notes.Count == 0)
            {
                if (lastId > last) { lock (Gate) _notifLast = lastId; SaveSoon(); }
                return;
            }
            notes.Sort(delegate (Note a, Note b) { return a.Id.CompareTo(b.Id); });
            var pictures = new Dictionary<long, string>();
            foreach (Note n in notes)
                if (n.Image != null && n.Priority >= 4) { string f = await Picture(link, n); if (f != null) pictures[n.Id] = f; }
            long newest = Math.Max(lastId, notes[notes.Count - 1].Id);
            var tcs = new TaskCompletionSource<bool>();
            Ui(delegate
            {
                try
                {
                    // back after a while with several waiting: one card that says so (they're all in the history)
                    bool summary = catchUp && notes.Count > 3;
                    var apps = new List<string>();
                    foreach (Note n in notes)
                    {
                        NotifyLevel level = n.Priority >= 8 ? NotifyLevel.Alert : NotifyLevel.Normal;
                        bool show = !summary && n.Priority >= 4;           // 0-3: the history only (Gotify's quiet ones)
                        string title = n.Title ?? FirstLine(n.Message);
                        string detail = n.Title != null ? n.Message : Rest(n.Message);
                        if (detail.Length > 400) detail = detail.Substring(0, 399) + "…";
                        string pic;
                        pictures.TryGetValue(n.Id, out pic);
                        Notify.Post(n.App.Length > 0 ? n.App : "vDrop", title, detail, n.Url ?? "", level, true, pic, "vdrop:" + n.Id, show,
                                    n.At > DateTime.MinValue ? n.At : DateTime.UtcNow);
                        if (!apps.Contains(n.App)) apps.Add(n.App);
                    }
                    if (summary)
                        Notify.Post("vDrop", notes.Count + " notifications while this PC was away", "From " + string.Join(", ", apps) + " - they're in the history.",
                                    "vshell:history", NotifyLevel.Normal, false);
                }
                finally { tcs.TrySetResult(true); }
            });
            await tcs.Task;
            lock (Gate) _notifLast = newest;
            SaveSoon();
        }

        static string FirstLine(string s)
        {
            s = (s ?? "").Trim();
            int i = s.IndexOf('\n');
            string line = i < 0 ? s : s.Substring(0, i).Trim();
            return line.Length > 120 ? line.Substring(0, 119) + "…" : line;
        }

        static string Rest(string s)
        {
            s = (s ?? "").Trim();
            int i = s.IndexOf('\n');
            return i < 0 ? "" : s.Substring(i + 1).Trim();
        }

        /// <summary>A notification's picture (a web address on your network) to a file for its card.</summary>
        static async Task<string> Picture(Link link, Note n)
        {
            try
            {
                string ext = n.Image.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
                string path = Path.Combine(ThumbDir, "notify_" + n.Id + ext);
                if (File.Exists(path)) return path;
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    using (HttpResponseMessage r = await link.Http.GetAsync(n.Image, cts.Token))
                    {
                        if (!r.IsSuccessStatusCode) return null;
                        byte[] data = await r.Content.ReadAsByteArrayAsync();
                        if (data.Length == 0 || data.Length > 8 * 1024 * 1024) return null;
                        Directory.CreateDirectory(ThumbDir);
                        File.WriteAllBytes(path, data);
                        return path;
                    }
                }
            }
            catch (Exception ex) { Log.Write("vDrop: notification picture: " + ex.Message); return null; }
        }

        // =================================================================
        // The cache on disk
        // =================================================================
        sealed class Cache
        {
            public int Version { get; set; } = 2;
            public long Rev { get; set; }
            /// <summary>The newest vDrop notification this PC has had (-1 = never connected: the first time skips what's already there).</summary>
            public long NotifLast { get; set; } = -1;
            public List<long> ReadQueue { get; set; } = new List<long>();
            public List<string> Deletes { get; set; } = new List<string>();
            public List<VDropItem> Items { get; set; } = new List<VDropItem>();
        }

        static void LoadCache()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(CachePath)) return;
                Cache c = JsonSerializer.Deserialize<Cache>(File.ReadAllText(CachePath));
                if (c == null) return;
                lock (Gate)
                {
                    _rev = c.Rev;
                    _notifLast = c.NotifLast;
                    ReadQueue.AddRange(c.ReadQueue ?? new List<long>());
                    Deletes.AddRange(c.Deletes ?? new List<string>());
                }
                foreach (VDropItem it in c.Items ?? new List<VDropItem>())
                    if (it != null) { if (it.Id == null) it.Id = ""; Items.Add(it); }
            }
            catch (Exception ex) { Log.Write("vDrop cache: " + ex.Message); }
        }

        static void SaveSoon()
        {
            if (_saveTimer == null) _saveTimer = new Timer(delegate { Ui(SaveCache); });
            _saveTimer.Change(1000, Timeout.Infinite);
        }

        static void SaveCache()
        {
            try
            {
                var c = new Cache();
                lock (Gate) { c.Rev = _rev; c.Deletes = new List<string>(Deletes); c.NotifLast = _notifLast; c.ReadQueue = new List<long>(ReadQueue); }
                foreach (VDropItem it in Items)
                    if (!it.New || (it.Text ?? "").Trim().Length > 0) c.Items.Add(it);
                Directory.CreateDirectory(BarSettings.ConfigDir);
                string tmp = CachePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(c));
                File.Copy(tmp, CachePath, true);
                File.Delete(tmp);
            }
            catch (Exception ex) { Log.Write("vDrop cache: " + ex.Message); }
        }

        // =================================================================
        // The connection
        // =================================================================
        sealed class Link
        {
            public ClientWebSocket Ws;
            public SemaphoreSlim SendLock = new SemaphoreSlim(1);
            public int NextId = 1;
            public readonly Dictionary<int, TaskCompletionSource<JsonElement>> Waiting = new Dictionary<int, TaskCompletionSource<JsonElement>>();
            public HttpClient Http;
            public string Base = "", Entry = "", Session = "";
            /// <summary>The ping waiting for its vdrop_result (request id, answer).</summary>
            public KeyValuePair<string, TaskCompletionSource<JsonElement>> Ping;
            public DateTime SessionAt;
        }

        static async Task Run(string url, string token, bool ignoreCert, CancellationToken ct)
        {
            int delay = 5;
            while (!ct.IsCancellationRequested)
            {
                var link = new Link { Base = url.TrimEnd('/') };
                bool notFound = false;
                try
                {
                    var handler = new HttpClientHandler { UseCookies = false };
                    if (ignoreCert) handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                    link.Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

                    using (link.Ws = new ClientWebSocket())
                    {
                        if (ignoreCert) link.Ws.Options.RemoteCertificateValidationCallback = delegate { return true; };
                        link.Ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                            await link.Ws.ConnectAsync(SocketUri(url), connectTimeout.Token);
                        }
                        await Receive(link.Ws, ct);                    // auth_required
                        await Send(link, JsonSerializer.Serialize(new Dictionary<string, string> { { "type", "auth" }, { "access_token", token } }), ct);
                        using (JsonDocument auth = JsonDocument.Parse(await Receive(link.Ws, ct)))
                            if (Str(auth.RootElement, "type") != "auth_ok") throw new InvalidOperationException("Home Assistant didn't accept the token");

                        using (var session = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            Task reader = ReadLoop(link, session.Token);
                            try
                            {
                                await Request(link, "{\"type\":\"subscribe_events\",\"event_type\":\"vdrop_changed\"}", session.Token);
                                await Request(link, "{\"type\":\"subscribe_events\",\"event_type\":\"vdrop_result\"}", session.Token);
                                // the add-on's ingress path: from its sensor, or by asking vDrop itself
                                link.Entry = await FindEntry(link, session.Token);
                                if (link.Entry.Length == 0) { notFound = true; throw new OperationCanceledException(); }
                                if (_notFound > 0 || !_foundLogged) Log.Write("vDrop: found at " + link.Entry);
                                _notFound = 0; _foundLogged = true;
                                try { await Request(link, "{\"type\":\"subscribe_events\",\"event_type\":\"vdrop_notification\"}", session.Token); }
                                catch (Exception ex) { Log.Write("vDrop: notifications events: " + ex.Message); }
                                _caughtUp = false; _beatAt = DateTime.MinValue; _notifMissing = false;
                                await NewSession(link, session.Token);
                                delay = 5;
                                SetStatus("");
                                await Work(link, session.Token, reader);
                            }
                            finally
                            {
                                session.Cancel();
                                // the reader ends with the connection (closed, cancelled): look at how it ended, or .NET reports its
                                // error later as an "unobserved task error" (the drop itself is already handled above)
                                reader.ContinueWith(delegate (Task t) { return t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested) return;
                    if (notFound)
                    {
                        SetStatus("vDrop isn't answering in Home Assistant (no sensor.vdrop, no answer to a ping) - is the add-on installed and running?");
                        // without the add-on (only the notifications setting on): look again now and then, more slowly each time
                        _notFound++;
                        try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(900, 60 * Math.Pow(2, Math.Min(4, _notFound - 1)))), ct); } catch { return; }
                        continue;
                    }
                    SetStatus("vDrop offline: Home Assistant didn't answer in time - your changes are kept and sent when it's back.");
                }
                catch (Exception ex)
                {
                    string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    Log.Write("vDrop: " + msg);
                    SetStatus("vDrop offline: " + msg + " - your changes are kept and sent when it's back.");
                }
                finally { if (link.Http != null) link.Http.Dispose(); }
                if (ct.IsCancellationRequested) return;
                try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch { return; }
                delay = Math.Min(120, delay * 2);
            }
        }

        /// <summary>Sync, send, keep the session alive - until the connection drops.</summary>
        static async Task Work(Link link, CancellationToken ct, Task reader)
        {
            while (!ct.IsCancellationRequested)
            {
                if (reader.IsCompleted) throw new IOException("the connection to Home Assistant closed");
                if ((DateTime.UtcNow - link.SessionAt).TotalMinutes >= 5) await KeepSession(link, ct);
                await Push(link, ct);
                await Pull(link, ct);
                await Thumbs(link, ct);
                await NotifWork(link, ct);
                // wait for an event / a local change, or a minute
                Task k = _kick.WaitAsync(TimeSpan.FromSeconds(60), ct);
                await Task.WhenAny(k, reader);
                await Task.Delay(300, ct);           // a burst of events / keystrokes: one sync
                while (_kick.CurrentCount > 0) _kick.Wait(0);
            }
        }

        static async Task ReadLoop(Link link, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                string text = await Receive(link.Ws, ct);
                try
                {
                    using (JsonDocument d = JsonDocument.Parse(text))
                    {
                        JsonElement root = d.RootElement;
                        string type = Str(root, "type");
                        if (type == "event")
                        {
                            if (root.TryGetProperty("event", out JsonElement nev) && Str(nev, "event_type") == "vdrop_notification" &&
                                nev.TryGetProperty("data", out JsonElement nd))
                            {
                                // read / deleted in vDrop (the phone, the page): read here too
                                string action = Str(nd, "action");
                                if ((action == "read" || action == "deleted") && nd.TryGetProperty("ids", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array)
                                {
                                    var refs = new List<string>();
                                    foreach (JsonElement x in ids.EnumerateArray()) if (x.TryGetInt64(out long nid)) refs.Add("vdrop:" + nid);
                                    Ui(delegate { foreach (string r in refs) Notify.MarkReadByRef(r); });
                                }
                                if (action == "added") Kick();
                                continue;
                            }
                            if (root.TryGetProperty("event", out JsonElement ev) && Str(ev, "event_type") == "vdrop_result")
                            {
                                if (ev.TryGetProperty("data", out JsonElement data))
                                {
                                    KeyValuePair<string, TaskCompletionSource<JsonElement>> ping;
                                    lock (link.Waiting) ping = link.Ping;
                                    if (ping.Value != null && Str(data, "request_id") == ping.Key) ping.Value.TrySetResult(data.Clone());
                                }
                                continue;
                            }
                            Kick();
                            continue;
                        }
                        if (type == "result" && root.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetInt32(out int id))
                        {
                            TaskCompletionSource<JsonElement> tcs;
                            lock (link.Waiting) { if (link.Waiting.TryGetValue(id, out tcs)) link.Waiting.Remove(id); }
                            if (tcs != null) tcs.TrySetResult(root.Clone());
                        }
                    }
                }
                catch (JsonException) { }
            }
        }

        /// <summary>A WebSocket command and its result (throws when it fails).</summary>
        static async Task<JsonElement> Request(Link link, string jsonWithoutId, CancellationToken ct)
        {
            int id;
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (link.Waiting) { id = link.NextId++; link.Waiting[id] = tcs; }
            await Send(link, "{\"id\":" + id + "," + jsonWithoutId.TrimStart('{'), ct);
            Task done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15), ct));
            if (done != tcs.Task) { lock (link.Waiting) link.Waiting.Remove(id); throw new TimeoutException("Home Assistant didn't answer"); }
            JsonElement r = tcs.Task.Result;
            if (r.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
            {
                string msg = r.TryGetProperty("error", out JsonElement err) ? Str(err, "message") : "";
                throw new InvalidOperationException(msg.Length > 0 ? msg : "Home Assistant refused it");
            }
            return r;
        }

        static async Task NewSession(Link link, CancellationToken ct)
        {
            JsonElement r;
            try { r = await Request(link, "{\"type\":\"supervisor/api\",\"endpoint\":\"/ingress/session\",\"method\":\"post\"}", ct); }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("can't open vDrop (ingress session: " + ex.Message + ") - the Home Assistant token must belong to an administrator");
            }
            string s = r.TryGetProperty("result", out JsonElement res) ? Str(res, "session") : "";
            if (s.Length == 0) throw new InvalidOperationException("Home Assistant gave no ingress session");
            link.Session = s;
            link.SessionAt = DateTime.UtcNow;
        }

        static async Task KeepSession(Link link, CancellationToken ct)
        {
            try
            {
                await Request(link, "{\"type\":\"supervisor/api\",\"endpoint\":\"/ingress/validate_session\",\"method\":\"post\",\"data\":{\"session\":" +
                                    JsonSerializer.Serialize(link.Session) + "}}", ct);
                link.SessionAt = DateTime.UtcNow;
            }
            catch (InvalidOperationException) { await NewSession(link, ct); }
        }

        /// <summary>An ingress HTTP call; a new session and one retry when the session has run out.</summary>
        static async Task<KeyValuePair<HttpStatusCode, string>> Http(Link link, HttpMethod method, string path, string json, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                using (var req = new HttpRequestMessage(method, link.Base + link.Entry + path))
                {
                    req.Headers.TryAddWithoutValidation("Cookie", "ingress_session=" + link.Session);
                    if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage r = await link.Http.SendAsync(req, ct))
                    {
                        string text = await r.Content.ReadAsStringAsync();
                        if ((r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.Forbidden) && attempt == 0)
                        {
                            await NewSession(link, ct);
                            continue;
                        }
                        link.SessionAt = DateTime.UtcNow;      // any use keeps it alive
                        return new KeyValuePair<HttpStatusCode, string>(r.StatusCode, text);
                    }
                }
            }
        }

        /// <summary>
        /// Where vDrop lives (its ingress path): sensor.vdrop's "ingress_entry" - or any sensor with one whose id has "vdrop" in it
        /// (an entity renamed in HA) - and failing that, vDrop's own answer to a "ping" command.
        /// </summary>
        static async Task<string> FindEntry(Link link, CancellationToken ct)
        {
            var seen = new List<string>();
            try
            {
                JsonElement r = await Request(link, "{\"type\":\"get_states\"}", ct);
                string best = "", any = "";
                if (r.TryGetProperty("result", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement st in list.EnumerateArray())
                    {
                        string id = Str(st, "entity_id");
                        if (id.IndexOf("vdrop", StringComparison.OrdinalIgnoreCase) >= 0) seen.Add(id);
                        if (!st.TryGetProperty("attributes", out JsonElement a)) continue;
                        string entry = Str(a, "ingress_entry");
                        if (entry.Length == 0) continue;
                        if (id == "sensor.vdrop") { best = entry; break; }
                        if (id.IndexOf("vdrop", StringComparison.OrdinalIgnoreCase) >= 0 || a.TryGetProperty("api_version", out JsonElement _)) any = entry;
                    }
                if (best.Length == 0) best = any;
                if (best.Length > 0) return best.TrimEnd('/');
            }
            catch (Exception ex) { Log.Write("vDrop: get_states: " + ex.Message); }

            // ask vDrop: {"cmd":"ping"} is answered with a vdrop_result event carrying ingress_entry
            try
            {
                string rid = "vs-ping-" + Environment.TickCount;
                var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (link.Waiting) link.Ping = new KeyValuePair<string, TaskCompletionSource<JsonElement>>(rid, tcs);
                await Request(link, "{\"type\":\"fire_event\",\"event_type\":\"vdrop_command\",\"event_data\":{\"cmd\":\"ping\",\"request_id\":\"" + rid + "\"}}", ct);
                Task done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(6), ct));
                if (done == tcs.Task)
                {
                    JsonElement data = tcs.Task.Result;
                    string entry = Str(data, "ingress_entry");
                    if (entry.Length == 0 && data.TryGetProperty("result", out JsonElement res)) entry = Str(res, "ingress_entry");
                    if (entry.Length > 0) return entry.TrimEnd('/');
                    Log.Write("vDrop: ping answer without ingress_entry: " + data.GetRawText());
                }
                else Log.Write("vDrop: no answer to ping");
            }
            catch (Exception ex) { Log.Write("vDrop: ping: " + ex.Message); }
            Log.Write("vDrop: not found - entities with \"vdrop\" in their id: " + (seen.Count == 0 ? "none" : string.Join(", ", seen)));
            return "";
        }

        // =================================================================
        // Plumbing
        // =================================================================
        static Uri SocketUri(string url)
        {
            string u = url.TrimEnd('/');
            if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = "wss://" + u.Substring(8);
            else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = "ws://" + u.Substring(7);
            return new Uri(u + "/api/websocket");
        }

        static string Str(JsonElement e, string name)
        {
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static async Task Send(Link link, string text, CancellationToken ct)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            await link.SendLock.WaitAsync(ct);
            try { await link.Ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, ct); }
            finally { link.SendLock.Release(); }
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
