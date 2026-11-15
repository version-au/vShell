using System;
using System.Collections.Generic;
using System.IO;

namespace vShell.Core
{
    /// <summary>
    /// Launcher groups kept in sync with folders of shortcuts: new shortcuts in the folder are added, deleted ones removed, and the
    /// folder's shortcuts are kept in A-Z order after any shortcuts you added by hand. A shortcut you renamed or gave an icon keeps that.
    /// Groups can follow browser bookmark folders the same way (LauncherBookmarks).
    /// </summary>
    public static class LauncherFolders
    {
        static readonly string[] Exts = { ".lnk", ".url", ".appref-ms", ".exe" };

        /// <summary>The shortcuts in a folder and its subfolders, A-Z by name, uninstallers left out.</summary>
        public static List<string> Scan(string folder)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return files;
            try
            {
                foreach (string f in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                    if (Path.GetFileNameWithoutExtension(f).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    files.Add(f);
                }
            }
            catch (Exception ex) { Log.Write("Launcher folder " + folder + ": " + ex.Message); }
            files.Sort(delegate (string a, string b)
            {
                return string.Compare(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b), StringComparison.CurrentCultureIgnoreCase);
            });
            return files;
        }

        static bool InFolder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            string f = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Bring a group's shortcuts up to date with its folders and bookmark folders. Order: shortcuts added by hand, then the folders'
        /// shortcuts (A-Z), then the bookmarks (in the browser's order). Returns true when anything changed.
        /// </summary>
        public static bool Sync(LauncherEntry group)
        {
            if (group == null || !group.IsGroup) return false;
            bool folders = group.SyncFolders != null && group.SyncFolders.Count > 0;
            bool marks = group.SyncBookmarks != null && group.SyncBookmarks.Count > 0;
            if (!folders && !marks) return false;

            var wanted = new List<string>();
            var wantedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (folders)
                foreach (string folder in group.SyncFolders)
                {
                    if (!Directory.Exists(folder)) continue;     // a missing folder (USB drive, network share) leaves its shortcuts alone
                    foreach (string f in Scan(folder)) if (wantedSet.Add(f)) wanted.Add(f);
                }
            // null when a Bookmarks file can't be read: the bookmarks the group has stay as they are
            List<LauncherBookmarks.Mark> wantedMarks = marks ? LauncherBookmarks.Wanted(group.SyncBookmarks) : null;

            var manual = new List<LauncherEntry>();
            var byPath = new Dictionary<string, LauncherEntry>(StringComparer.OrdinalIgnoreCase);
            var oldMarks = new List<LauncherEntry>();
            var byUrl = new Dictionary<string, Queue<LauncherEntry>>(StringComparer.Ordinal);
            bool changed = false;
            foreach (var c in group.Children)
            {
                if (marks && !string.IsNullOrEmpty(c.BookmarkName))
                {
                    oldMarks.Add(c);
                    Queue<LauncherEntry> q;
                    if (!byUrl.TryGetValue(c.Path ?? "", out q)) byUrl[c.Path ?? ""] = q = new Queue<LauncherEntry>();
                    q.Enqueue(c);
                    continue;
                }
                if (!folders) { manual.Add(c); continue; }
                bool fromFolder = false;
                foreach (string folder in group.SyncFolders)
                    if (Directory.Exists(folder) && InFolder(c.Path, folder)) fromFolder = true;
                if (!fromFolder) { manual.Add(c); continue; }
                if (wantedSet.Contains(c.Path) && !byPath.ContainsKey(c.Path)) byPath[c.Path] = c;
                else changed = true;                       // deleted from the folder (or a duplicate)
            }

            var result = new List<LauncherEntry>(manual);
            foreach (string f in wanted)
            {
                LauncherEntry e;
                if (!byPath.TryGetValue(f, out e))
                {
                    e = new LauncherEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f };
                    changed = true;
                }
                result.Add(e);
            }
            if (wantedMarks == null) result.AddRange(oldMarks);
            else
            {
                foreach (var m in wantedMarks)
                {
                    LauncherEntry e = null;
                    Queue<LauncherEntry> q;
                    if (byUrl.TryGetValue(m.Url, out q) && q.Count > 0) e = q.Dequeue();
                    if (e == null)
                    {
                        e = new LauncherEntry { Name = m.Name, Path = m.Url, BookmarkName = m.Name };
                        changed = true;
                    }
                    else if (e.BookmarkName != m.Name)
                    {
                        // renamed in the browser: follow it, unless you gave it your own name here
                        if (e.Name == e.BookmarkName) e.Name = m.Name;
                        e.BookmarkName = m.Name;
                        changed = true;
                    }
                    result.Add(e);
                }
                foreach (var q in byUrl.Values) if (q.Count > 0) changed = true;     // deleted in the browser
            }
            if (!changed && result.Count != group.Children.Count) changed = true;
            if (!changed)
            {
                // same entries: only the order may differ
                for (int i = 0; i < result.Count; i++) if (!ReferenceEquals(result[i], group.Children[i])) { changed = true; break; }
            }
            if (changed) group.Children = result;
            return changed;
        }

        /// <summary>Sync every synced group of a launcher, groups inside groups too. Returns true when anything changed.</summary>
        public static bool SyncAll(List<LauncherEntry> entries)
        {
            bool changed = false;
            if (entries == null) return false;
            foreach (var e in entries)
            {
                if (!e.IsGroup) continue;
                if (Sync(e)) changed = true;
                if (SyncAll(e.Children)) changed = true;
            }
            return changed;
        }
    }
}
