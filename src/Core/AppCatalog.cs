using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace vShell.Core
{
    /// <summary>One program shown in the Start menu.</summary>
    public sealed class AppEntry
    {
        public string Name = "";
        /// <summary>Shortcut file (.lnk/.url/.appref-ms), or "shell:AppsFolder\AUMID" for a Store (packaged) app.</summary>
        public string Target = "";
        public bool IsStore;
        /// <summary>Section letter: "A".."Z", another script's letter, or "#".</summary>
        public string Letter = "#";
        /// <summary>The Start Menu folder it is in, under Programs ("Accessories\System Tools"); empty = straight in Programs, or a Store app.</summary>
        public string Folder = "";
        public ImageSource Icon;
        public bool IconTried;
    }

    /// <summary>
    /// The installed programs: shortcuts in the two Start Menu "Programs" folders (read directly) plus Store apps
    /// (asked from PowerShell's Get-StartApps in the background; cached for a while).
    /// </summary>
    public static class AppCatalog
    {
        static readonly object Gate = new object();
        static List<AppEntry> _files = new List<AppEntry>();
        static List<AppEntry> _store = new List<AppEntry>();
        static List<AppEntry> _merged = new List<AppEntry>();
        static DateTime _filesAt = DateTime.MinValue, _storeAt = DateTime.MinValue;
        static bool _storeBusy;

        /// <summary>Raised on the UI thread when the Store app list arrives after the first list was already shown.</summary>
        public static event Action Changed;

        static bool _filesBusy, _filesDirty;
        static readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        /// <summary>
        /// Current list, sorted by name - straight from memory (the Start menu never waits for a folder scan). Only the very first call
        /// reads the Start Menu folders itself; after that they are read again in the background when a shortcut is added, removed or
        /// renamed (folder watchers), or at the latest every 10 minutes, and Changed is raised when the list is different.
        /// </summary>
        public static List<AppEntry> Get()
        {
            bool first;
            lock (Gate) first = _filesAt == DateTime.MinValue && !_filesBusy;
            if (first) RescanFiles();
            else
            {
                bool stale;
                lock (Gate) stale = !_filesBusy && (_filesDirty || (DateTime.UtcNow - _filesAt).TotalMinutes > 10);
                if (stale) System.Threading.ThreadPool.QueueUserWorkItem(delegate { RescanFiles(); });
            }

            bool needStore;
            lock (Gate)
            {
                // asking Windows for the Store apps starts PowerShell (a second or more of CPU): every half hour is plenty
                needStore = !_storeBusy && (DateTime.UtcNow - _storeAt).TotalMinutes > 30;
                if (needStore) _storeBusy = true;
            }
            if (needStore) System.Threading.ThreadPool.QueueUserWorkItem(delegate { LoadStore(); });

            lock (Gate) return new List<AppEntry>(_merged);
        }

        /// <summary>Read the Start Menu folders (any thread); raises Changed when the programs differ from before.</summary>
        static void RescanFiles()
        {
            lock (Gate)
            {
                if (_filesBusy) return;
                _filesBusy = true;
                _filesDirty = false;
            }
            bool changed = false;
            try
            {
                List<AppEntry> files = ScanFiles();
                lock (Gate)
                {
                    // keep icons that were already loaded for the same shortcut
                    var old = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in _files) old[e.Target] = e;
                    changed = files.Count != _files.Count;
                    foreach (var e in files)
                    {
                        AppEntry prev;
                        if (old.TryGetValue(e.Target, out prev)) { e.Icon = prev.Icon; e.IconTried = prev.IconTried; if (prev.Name != e.Name) changed = true; }
                        else changed = true;
                    }
                    _files = files;
                    _filesAt = DateTime.UtcNow;
                    Merge();
                }
                StartWatching();
            }
            catch (Exception ex) { Log.Write("Start menu scan: " + ex.Message); }
            finally { lock (Gate) _filesBusy = false; }
            if (changed) RaiseChanged();
        }

        static void RaiseChanged()
        {
            try
            {
                Application app = Application.Current;
                if (app != null) app.Dispatcher.BeginInvoke(new Action(delegate { Action h = Changed; if (h != null) h(); }));
            }
            catch { }
        }

        /// <summary>Watch both Programs folders: a change marks the list stale and reads it again a moment later.</summary>
        static void StartWatching()
        {
            lock (_watchers)
            {
                if (_watchers.Count > 0) return;
                foreach (Environment.SpecialFolder sf in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
                {
                    try
                    {
                        string root = Environment.GetFolderPath(sf);
                        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                        var w = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                        FileSystemEventHandler on = delegate { Dirty(); };
                        w.Created += on; w.Deleted += on; w.Changed += on;
                        w.Renamed += delegate { Dirty(); };
                        w.EnableRaisingEvents = true;
                        _watchers.Add(w);
                    }
                    catch (Exception ex) { Log.Write("Start menu watch: " + ex.Message); }
                }
            }
        }

        static System.Threading.Timer _dirtyTimer;
        static void Dirty()
        {
            lock (Gate) _filesDirty = true;
            // installers write several shortcuts in a row: read once, a second after the last change
            if (_dirtyTimer == null) _dirtyTimer = new System.Threading.Timer(delegate { RescanFiles(); }, null, 1000, System.Threading.Timeout.Infinite);
            else _dirtyTimer.Change(1000, System.Threading.Timeout.Infinite);
        }

        static void Merge()
        {
            var map = new Dictionary<string, AppEntry>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var e in _files) if (!map.ContainsKey(e.Name)) map[e.Name] = e;
            foreach (var e in _store) if (!map.ContainsKey(e.Name)) map[e.Name] = e;
            var list = new List<AppEntry>(map.Values);
            list.Sort(delegate (AppEntry a, AppEntry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            _merged = list;
        }

        // ---- Start Menu folders ---------------------------------------------------------------
        static List<AppEntry> ScanFiles()
        {
            var list = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Environment.SpecialFolder sf in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            {
                string root;
                try { root = Environment.GetFolderPath(sf); } catch { continue; }
                if (string.IsNullOrEmpty(root)) continue;
                Walk(root, root, list, seen, 0);
            }
            return list;
        }

        static void Walk(string root, string dir, List<AppEntry> list, HashSet<string> seen, int depth)
        {
            if (depth > 6) return;
            try
            {
                string folder = depth == 0 ? "" : Path.GetRelativePath(root, dir);
                foreach (string f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url" && ext != ".appref-ms") continue;
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (IsJunk(name)) continue;
                    if (!seen.Add(name)) continue;       // same app in "this user" and "all users"
                    list.Add(new AppEntry { Name = name, Target = f, Letter = LetterOf(name), Folder = folder });
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string leaf = Path.GetFileName(d);
                    if (string.Equals(leaf, "Startup", StringComparison.OrdinalIgnoreCase)) continue;
                    Walk(root, d, list, seen, depth + 1);
                }
            }
            catch (Exception ex) { Log.Write("Start menu scan '" + dir + "': " + ex.Message); }
        }

        static bool IsJunk(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.ToLowerInvariant();
            return n.Contains("uninstall") || n.EndsWith("readme") || n.Contains("release notes");
        }

        public static string LetterOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#";
            string d;
            try { d = name.Normalize(NormalizationForm.FormD); } catch { d = name; }
            char c = d.Length > 0 ? d[0] : '#';
            if (c >= 'a' && c <= 'z') return char.ToUpperInvariant(c).ToString();
            if (c >= 'A' && c <= 'Z') return c.ToString();
            if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
            return "#";
        }

        // ---- Store apps ----------------------------------------------------------------------------
        static void LoadStore()
        {
            var list = new List<AppEntry>();
            try
            {
                string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                var psi = new ProcessStartInfo(ps,
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ConvertTo-Json -Compress\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    var read = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                        Log.Write("Get-StartApps timed out");
                    }
                    else
                    {
                        string json = (read.Result ?? "").Trim().TrimStart('﻿');
                        if (json.Length > 0) ParseStore(json, list);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Store apps: " + ex.Message); }

            bool changed = false;
            lock (Gate)
            {
                // a failed read keeps the previous list
                if (list.Count > 0 || _store.Count == 0)
                {
                    var old = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in _store) old[e.Target] = e;
                    // the same apps as last time (nearly every time): nobody is told, so no Start menu builds its list again
                    changed = list.Count != _store.Count;
                    foreach (var e in list)
                    {
                        AppEntry prev;
                        if (old.TryGetValue(e.Target, out prev)) { e.Icon = prev.Icon; e.IconTried = prev.IconTried; if (prev.Name != e.Name) changed = true; }
                        else changed = true;
                    }
                    _store = list;
                    if (changed) Merge();
                }
                _storeAt = DateTime.UtcNow;
                _storeBusy = false;
            }
            if (changed) RaiseChanged();
        }

        static void ParseStore(string json, List<AppEntry> list)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object) AddStore(root, list);
                else if (root.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement el in root.EnumerateArray()) AddStore(el, list);
            }
        }

        static void AddStore(JsonElement el, List<AppEntry> list)
        {
            if (el.ValueKind != JsonValueKind.Object) return;
            JsonElement n, id;
            if (!el.TryGetProperty("Name", out n) || !el.TryGetProperty("AppID", out id)) return;
            string name = n.GetString(), appId = id.GetString();
            // packaged apps have an AUMID "PackageFamilyName!AppId"; everything else is a classic program (already read from the folders)
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId) || appId.IndexOf('!') < 0) return;
            if (IsJunk(name)) return;
            list.Add(new AppEntry { Name = name.Trim(), Target = "shell:AppsFolder\\" + appId, IsStore = true, Letter = LetterOf(name) });
        }
    }
}
