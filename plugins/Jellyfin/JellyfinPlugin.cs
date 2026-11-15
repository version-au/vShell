using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("jellyfin", "Jellyfin", Description = "Who's watching on your Jellyfin server: a module with what's playing (transcoding or not), recently added and the library, Now playing's times and buttons for the Jellyfin apps, notifications, palette commands and !jf in the run box.",
    Glyph = "\uE768", Group = "Media", Author = "version", Adds = "a module, Now playing's times and buttons for the Jellyfin apps, notifications, palette commands, !jf in the run box", Types = "Jellyfin")]

namespace vShell.Ui
{
    /// <summary>
    /// The Jellyfin plugin: the module (who's watching, recently added, the library), and for Now playing the times (a progress bar you
    /// can seek) and buttons for a Jellyfin app Windows knows nothing about. Also the "someone is watching" and "new in the library"
    /// notifications, the palette's pause / stop commands and !jf in the run box.
    /// </summary>
    public sealed class JellyfinPlugin : IPlugin
    {
        public const string Type = "Jellyfin";
        static BarManager _mgr;
        Action<JellyfinSnapshot> _polled;

        public void Load(PluginHost host)
        {
            _mgr = host.Manager;
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Jellyfin", ShortName = "Jellyfin", Glyph = JellyfinList.G(JellyfinList.Glyph), Group = "Smart home & more",
                Blurb = "who's watching, new arrivals", HasPopup = true,
                Needs = delegate (BarSettings s) { return JellyfinClient.Configured(s) ? null : "Needs the Jellyfin connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new JellyfinModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(JellyfinClient.Configured);
            host.AddIntegration("Jellyfin", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(JellyfinClient.Configure);
            host.AddNotifyKind("Jellyfin.Playback", NotifyLevel.Normal);
            host.AddNotifyKind("Jellyfin.Added", NotifyLevel.Normal);
            host.AddNotifySource("Jellyfin");
            host.AddMediaHelper(new JellyfinMedia());
            host.AddWatcher("Jellyfin", Watch);
            _polled = delegate (JellyfinSnapshot snap) { NewInLibrary(snap); };
            JellyfinClient.Polled += _polled;

            host.AddCommands("Jellyfin", Commands);
            // !jf anna: the palette's Jellyfin commands for "anna" (pause / resume / stop what she's watching)
            host.AddBang("Jellyfin", delegate (string key, string q, BarSettings s)
            {
                if (key != JfSettings.Of(s).Bang || !JellyfinClient.Configured(s)) return null;
                return new RunAction { Kind = "Palette", Query = "Jellyfin " + q, Display = "Jellyfin: " + (q.Length == 0 ? "what's playing" : q) };
            }, delegate { }, delegate (BarSettings s) { return JellyfinClient.Configured(s) ? "!" + JfSettings.Of(s).Bang + " Jellyfin" : null; });
        }

        public void Unload()
        {
            _prev = null;
            _seen = null;
            if (_polled != null) JellyfinClient.Polled -= _polled;
            JellyfinClient.Stop();
        }

        // ---- the module's settings and gallery pictures ----
        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            JfView v = JfView.Of(cfg);
            if (!JellyfinClient.Configured(p.Settings)) p.Note("Set up the Jellyfin connection first (Plugins page in this window).");
            p.Check("Show what's playing now", delegate { return v.ShowPlaying; }, delegate (bool on) { v.ShowPlaying = on; });
            p.Check("Show recently added", delegate { return v.ShowRecent; }, delegate (bool on) { v.ShowRecent = on; });
            p.Slider("How many recently added", 1, 20, delegate { return v.Count; }, delegate (double d) { v.Count = (int)d; });
            p.Check("Show the library's totals", delegate { return v.ShowCounts; }, delegate (bool on) { v.ShowCounts = on; });
            p.Check("Show the \"Open Jellyfin\" button", delegate { return v.ShowOpen; }, delegate (bool on) { v.ShowOpen = on; });
            if (!p.IsWidget)
            {
                p.Check("Hide when nobody is playing anything", delegate { return v.HideIdle; }, delegate (bool on) { v.HideIdle = on; });
                p.Title(cfg, "");
            }
            p.Note((p.IsWidget ? "" : "On a bar: the number of people playing something as a badge, and how many of them are transcoding - click it for the list. ") +
                   "Click someone's row to pause / resume or stop it, or open it in Jellyfin; click something recently added to open it. Seeing every " +
                   "session needs an administrator's API key.");
        }

        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(6, ModulePics.G(JellyfinList.Glyph, t.Fg, 15), ModulePics.T(t, "2", t.MainAccent, 12.5, true), ModulePics.T(t, "1 transcoding", StatusColors.Warn, 12.5));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim;
            return ModulePics.Col(5, ModulePics.T(t, "PLAYING NOW", dim, 10),
                                  ModulePics.Row(6, ModulePics.T(t, "Severance \u00B7 S02E05", fg, 12), ModulePics.T(t, "18:04 left", dim, 11)),
                                  ModulePics.Meter(t, 200, 3, 0.62, t.MainAccent),
                                  ModulePics.T(t, "RECENTLY ADDED", dim, 10),
                                  ModulePics.Row(6, ModulePics.T(t, "Dune: Part Two (2024)", fg, 12), ModulePics.T(t, "movie \u00B7 2 h ago", dim, 11)));
        }

        // =================================================================
        // The command palette: pause / resume / stop what's playing, open Jellyfin
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!JellyfinClient.Configured(s)) return;
            const string Src = "Jellyfin";
            foreach (string[] page in new[] { new[] { "Open Jellyfin", JellyfinClient.WebUrl(s) }, new[] { "Open Jellyfin's dashboard", JellyfinClient.DashboardUrl(s) } })
            {
                string url = page[1];
                into.Add(new PaletteCommand
                {
                    Title = page[0], Detail = Src, Glyph = "\uE774", Source = Src, Words = "jellyfin media server web page", Opens = true,
                    Run = delegate { RunPanel.Open(url); return System.Threading.Tasks.Task.FromResult(""); }
                });
            }
            JellyfinSnapshot snap = JellyfinClient.Latest;
            if (snap == null || !snap.Ok) return;
            foreach (JellyfinPlaying p0 in snap.Playing)
            {
                JellyfinPlaying p = p0;
                string who = p.UserName.Length > 0 ? p.UserName : p.DeviceName;
                string detail = Src + " \u00B7 " + p.Shown + " \u00B7 " + p.DeviceName;
                string words = "jellyfin playing watching " + p.UserName + " " + p.DeviceName + " " + p.Client;
                into.Add(new PaletteCommand
                {
                    Title = (p.Paused ? "Resume " : "Pause ") + who + "'s " + (p.Type == "Audio" ? "music" : "video"), Detail = detail, Glyph = p.Paused ? "\uE768" : "\uE769",
                    Source = Src, Words = words + (p.Paused ? " play continue" : " hold"), Boost = 1,
                    Run = async delegate { string err = await JellyfinClient.ControlAsync(s, p.SessionId, "PlayPause"); return err ?? PaletteCommand.Worked((p.Paused ? "Resumed " : "Paused ") + p.Shown); }
                });
                into.Add(new PaletteCommand
                {
                    Title = "Stop " + who + "'s " + (p.Type == "Audio" ? "music" : "video"), Detail = detail, Glyph = "\uE71A", Source = Src, Words = words + " end", Ask = true,
                    Run = async delegate { string err = await JellyfinClient.ControlAsync(s, p.SessionId, "Stop"); return err ?? PaletteCommand.Worked("Stopped " + p.Shown); }
                });
            }
        }

        // =================================================================
        // "New in the library" notices (from the poller, on a worker thread)
        // =================================================================
        static HashSet<string> _seen;

        static void NewInLibrary(JellyfinSnapshot snap)
        {
            BarSettings s = _mgr != null ? _mgr.Settings : null;
            if (s == null || snap.Recent.Count == 0) return;
            JfSettings j = JfSettings.Of(s);
            var now = new HashSet<string>();
            foreach (JellyfinItem it in snap.Recent) now.Add(it.Id);
            HashSet<string> seen = _seen;
            _seen = now;
            if (seen == null || !j.NotifyNew) return;       // the first look: what's there now isn't new
            var fresh = snap.Recent.FindAll(delegate (JellyfinItem it) { return !seen.Contains(it.Id) && (it.Added == DateTime.MinValue || (DateTime.UtcNow - it.Added).TotalDays < 2); });
            if (fresh.Count == 0) return;
            if (fresh.Count > 3)
            {
                Notify.Post("Jellyfin", fresh.Count + " new in Jellyfin", string.Join(", ", fresh.GetRange(0, 3).ConvertAll(delegate (JellyfinItem it) { return it.Shown; })) + " \u2026",
                            JellyfinClient.WebUrl(s), Notify.KindOf("Jellyfin.Added"));
                return;
            }
            foreach (JellyfinItem it in fresh)
                Notify.Post("Jellyfin", "New " + it.Kind + ": " + it.Shown, "Added to Jellyfin", JellyfinClient.ItemUrl(s, it.Id), Notify.KindOf("Jellyfin.Added"));
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            JfSettings j = JfSettings.Of(S);
            p.Header("Jellyfin");
            p.Note("Gives the Now Playing module a progress bar and allows you to seek from a bar or widget.");
            p.Text("Address (for example http://192.168.1.20:8096 or https://jellyfin.example.com)", delegate { return j.Url; }, delegate (string v) { j.Url = v; });
            PasswordBox pw = p.Secret("API key", "API key", delegate { return j.ApiKey; }, delegate (string v) { j.ApiKey = v; },
                                      "In Jellyfin open Dashboard > API Keys, add one and paste it here. It is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return j.IgnoreCert; }, delegate (bool v) { j.IgnoreCert = v; });
            p.Text("This PC's device name in Jellyfin (empty = the computer's name, " + Environment.MachineName + "; see Dashboard > Devices)",
                   delegate { return j.Device; }, delegate (string v) { j.Device = v; });
            p.Text("Run box keyword (without the !)", delegate { return j.Bang; }, delegate (string v) { j.Bang = v; },
                   "!" + j.Bang + " anna - pause / resume or stop what Anna (a user or a device) is playing. !" + j.Bang + " on its own lists what's playing.");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { j.ApiKey = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return JellyfinClient.TestAsync(S); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            JfSettings j = JfSettings.Of(S);
            p.NotifySection("Jellyfin notifications", JellyfinClient.Configured(S), "Jellyfin");
            p.Check("Someone starts playing something", delegate { return j.NotifyPlay; }, delegate (bool v) { j.NotifyPlay = v; p.Redraw(); });
            p.Check("Someone stops", delegate { return j.NotifyStop; }, delegate (bool v) { j.NotifyStop = v; p.Redraw(); });
            if (j.NotifyPlay || j.NotifyStop)
            {
                p.Text("Leave out these users / devices (names, comma separated - empty = none)", delegate { return j.NotifyIgnore; }, delegate (string v) { j.NotifyIgnore = v; },
                       "This PC's own Jellyfin app (the device set on the Connection tab) is always left out. Jellyfin needs an API key that can see every session (an admin's).");
                p.Kind("Playback shows as", "Jellyfin.Playback");
            }
            p.Check("Something new is added to the library (a movie, an episode, an album)", delegate { return j.NotifyNew; }, delegate (bool v) { j.NotifyNew = v; p.Redraw(); });
            if (j.NotifyNew) p.Kind("New in the library shows as", "Jellyfin.Added");
            p.Note("vShell checks every 10 seconds.");
        }

        // =================================================================
        // Notifications: who started / stopped playing (checked every 10 seconds)
        // =================================================================
        static Dictionary<string, JellyfinPlaying> _prev;
        static DateTime _at = DateTime.MinValue;

        static bool Ignored(JfSettings j, JellyfinPlaying p)
        {
            if (j.Device.Length > 0 && string.Equals(p.DeviceName, j.Device, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string x in j.NotifyIgnore.Split(','))
            {
                string n = x.Trim();
                if (n.Length > 0 && (string.Equals(n, p.UserName, StringComparison.OrdinalIgnoreCase) || string.Equals(n, p.DeviceName, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        static string Title(JellyfinPlaying p)
        {
            return (p.Artist.Length > 0 && p.Type != "Movie" ? p.Artist + " - " : "") + p.Title;
        }

        static void Watch(BarSettings s)
        {
            JfSettings j = JfSettings.Of(s);
            if (!(j.NotifyPlay || j.NotifyStop) || !JellyfinClient.Configured(s)) { _prev = null; return; }
            if ((DateTime.UtcNow - _at).TotalSeconds < 10) return;
            _at = DateTime.UtcNow;
            var now = new Dictionary<string, JellyfinPlaying>();
            foreach (JellyfinPlaying p in JellyfinClient.Playing(s)) if (!Ignored(j, p)) now[p.SessionId + "|" + p.ItemId] = p;
            Dictionary<string, JellyfinPlaying> prev = _prev;
            _prev = now;
            if (prev == null) return;
            string web = j.Url.Trim();
            if (web.Length > 0 && !web.Contains("://")) web = "http://" + web;
            string dash = web.Length > 0 ? web.TrimEnd('/') + "/web/#/dashboard" : "";
            if (j.NotifyPlay)
                foreach (var kv in now)
                    if (!prev.ContainsKey(kv.Key))
                    {
                        JellyfinPlaying p = kv.Value;
                        string verb = p.Type == "Audio" ? "is listening to " : "is watching ";
                        Notify.Post("Jellyfin", (p.UserName.Length > 0 ? p.UserName : "Someone") + " " + verb + Title(p), p.DeviceName + (p.Client.Length > 0 ? " · " + p.Client : ""), dash, Notify.KindOf("Jellyfin.Playback"));
                    }
            if (j.NotifyStop)
                foreach (var kv in prev)
                    if (!now.ContainsKey(kv.Key))
                    {
                        JellyfinPlaying p = kv.Value;
                        // the same session moved on to the next item: that's not a stop
                        bool next = false;
                        foreach (var n in now.Values) if (n.SessionId == p.SessionId) { next = true; break; }
                        if (next) continue;
                        Notify.Post("Jellyfin", (p.UserName.Length > 0 ? p.UserName : "Someone") + " stopped " + Title(p), p.DeviceName, dash, Notify.KindOf("Jellyfin.Playback"));
                    }
        }
    }

    /// <summary>Now playing's times for what the Jellyfin apps play (they give Windows the title, not where it is), and the buttons for them.</summary>
    sealed class JellyfinMedia : IMediaHelper
    {
        List<JellyfinPlaying> _jf = new List<JellyfinPlaying>();
        DateTime _at = DateTime.MinValue;
        bool _failed;
        // a Jellyfin client reports its position every few seconds: the time each reported position was first seen
        string _posKey = "";
        DateTime _posAt = DateTime.UtcNow;
        string _artId = "";
        byte[] _art;
        int _artVersion;

        public string Name { get { return "Jellyfin"; } }

        public bool Ready(BarSettings s) { return JellyfinClient.Configured(s); }

        static bool Matches(string a, string b)
        {
            a = (a ?? "").Trim(); b = (b ?? "").Trim();
            if (a.Length == 0 || b.Length == 0) return false;
            return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Fill(MediaInfo info, BarSettings s)
        {
            if ((DateTime.UtcNow - _at).TotalSeconds >= (_failed ? 15 : 2))
            {
                _at = DateTime.UtcNow;
                try { _jf = JellyfinClient.Playing(s); _failed = false; }
                catch (Exception ex)
                {
                    if (!_failed) Log.Write("Jellyfin: " + ex.Message);
                    _failed = true;
                    _jf = new List<JellyfinPlaying>();
                }
            }
            if (_jf.Count == 0) return;

            string device = JfSettings.Of(s).Device;
            if (device.Length == 0) device = Environment.MachineName;
            bool windowsIdle = !info.HasSession || info.Status == 0 || (info.Title.Length == 0 && info.Artist.Length == 0);
            JellyfinPlaying hit = null;
            if (!windowsIdle)
            {
                // what Windows shows: the Jellyfin session playing the same title (this PC's first)
                foreach (JellyfinPlaying j in _jf)
                    if (Matches(j.Title, info.Title) && (hit == null || string.Equals(j.DeviceName, device, StringComparison.OrdinalIgnoreCase))) hit = j;
                if (hit == null) return;
            }
            else
            {
                // Windows shows nothing: this PC's (or the chosen device's) Jellyfin session
                foreach (JellyfinPlaying j in _jf)
                    if (string.Equals(j.DeviceName, device, StringComparison.OrdinalIgnoreCase)) { hit = j; break; }
                if (hit == null) return;
                info.Available = true;
                info.HasSession = true;
                info.HelperOnly = true;
                info.Title = hit.Title;
                info.Artist = hit.Artist;
                info.Album = hit.Album;
                info.App = "Jellyfin" + (hit.Client.Length > 0 ? " (" + hit.Client + ")" : "");
                info.Status = hit.Paused ? 5 : 4;
                if (hit.ImageItemId != _artId)
                {
                    _artId = hit.ImageItemId;
                    _art = JellyfinClient.Image(s, hit.ImageItemId);
                    _artVersion = MediaSession.NewArtVersion();
                }
                info.Art = _art;
                info.ArtVersion = _artVersion;
            }
            if (hit.Duration <= 0) return;
            string key = hit.SessionId + "|" + hit.ItemId + "|" + hit.Position.ToString("F1");
            if (key != _posKey) { _posKey = key; _posAt = DateTime.UtcNow; }
            info.Duration = hit.Duration;
            info.Position = hit.Position;
            info.PosAt = _posAt;
            info.CanSeek = hit.CanSeek || info.CanSeek;
            info.Source = Name;
            info.HelperSession = hit.SessionId;
            if (hit.Paused && info.Status == 4) info.Status = 5;
        }

        public void Send(MediaInfo cur, BarSettings s, string command)
        {
            if (command.StartsWith("seek:"))
            {
                double sec;
                if (double.TryParse(command.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec))
                    JellyfinClient.Command(s, cur.HelperSession, "Seek", sec);
            }
            else JellyfinClient.Command(s, cur.HelperSession, command == "next" ? "NextTrack" : command == "previous" ? "PreviousTrack" : "PlayPause");
            _at = DateTime.MinValue;      // ask Jellyfin again straight away
        }
    }
}
