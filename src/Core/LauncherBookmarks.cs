using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace vShell.Core
{
    /// <summary>
    /// Browser bookmarks for launcher groups: finds the Bookmarks files of Chromium browsers (Chrome, Edge, Brave, Vivaldi, Opera …) and
    /// reads their folders. A group synced to a bookmarks folder gets its bookmarks (subfolders included) in the browser's order.
    /// Firefox keeps its bookmarks in a database vShell can't read, so it isn't listed.
    /// </summary>
    public static class LauncherBookmarks
    {
        /// <summary>One browser profile with bookmarks.</summary>
        public sealed class Profile
        {
            public string Browser;      // "Edge", or "Edge (Work)" when the browser has more than one profile
            public string File;
        }

        /// <summary>A bookmarks folder (Url null) or a bookmark.</summary>
        public sealed class Node
        {
            public string Name = "";
            public string Guid = "";
            public string Path = "";    // folders: "Bookmarks bar/Homelab"
            public string Url;
            public List<Node> Children = new List<Node>();
            public bool IsFolder { get { return Url == null; } }
        }

        /// <summary>A bookmark to show: its name and address.</summary>
        public sealed class Mark
        {
            public string Name;
            public string Url;
        }

        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // browser name, its user data folder, whether it has profile subfolders (Opera keeps one profile in the folder itself)
        static readonly Tuple<string, string, bool>[] Browsers =
        {
            Tuple.Create("Edge", System.IO.Path.Combine(Local, @"Microsoft\Edge\User Data"), true),
            Tuple.Create("Chrome", System.IO.Path.Combine(Local, @"Google\Chrome\User Data"), true),
            Tuple.Create("Brave", System.IO.Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data"), true),
            Tuple.Create("Vivaldi", System.IO.Path.Combine(Local, @"Vivaldi\User Data"), true),
            Tuple.Create("Chromium", System.IO.Path.Combine(Local, @"Chromium\User Data"), true),
            Tuple.Create("Opera", System.IO.Path.Combine(Roaming, @"Opera Software\Opera Stable"), false),
            Tuple.Create("Opera GX", System.IO.Path.Combine(Roaming, @"Opera Software\Opera GX Stable"), false),
        };

        /// <summary>The browser profiles on this PC that have a Bookmarks file.</summary>
        public static List<Profile> Profiles()
        {
            var list = new List<Profile>();
            foreach (var b in Browsers)
            {
                try
                {
                    if (!Directory.Exists(b.Item2)) continue;
                    if (!b.Item3)
                    {
                        string f = System.IO.Path.Combine(b.Item2, "Bookmarks");
                        if (System.IO.File.Exists(f)) list.Add(new Profile { Browser = b.Item1, File = f });
                        continue;
                    }
                    var names = ProfileNames(b.Item2);
                    var found = new List<Profile>();
                    foreach (string dir in Directory.GetDirectories(b.Item2))
                    {
                        string f = System.IO.Path.Combine(dir, "Bookmarks");
                        if (!System.IO.File.Exists(f)) continue;
                        string key = System.IO.Path.GetFileName(dir), shown;
                        if (!names.TryGetValue(key, out shown) || string.IsNullOrWhiteSpace(shown)) shown = key;
                        found.Add(new Profile { Browser = shown, File = f });
                    }
                    found.Sort(delegate (Profile x, Profile y) { return string.Compare(x.Browser, y.Browser, StringComparison.CurrentCultureIgnoreCase); });
                    foreach (var p in found)
                    {
                        p.Browser = found.Count == 1 ? b.Item1 : b.Item1 + " (" + p.Browser + ")";
                        list.Add(p);
                    }
                }
                catch (Exception ex) { Log.Write("Bookmarks: " + b.Item1 + ": " + ex.Message); }
            }
            return list;
        }

        /// <summary>Profile folder -> the name the browser shows for it (from "Local State").</summary>
        static Dictionary<string, string> ProfileNames(string userData)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string ls = System.IO.Path.Combine(userData, "Local State");
                if (!System.IO.File.Exists(ls)) return names;
                using (var doc = JsonDocument.Parse(ReadShared(ls)))
                {
                    JsonElement prof, cache;
                    if (!doc.RootElement.TryGetProperty("profile", out prof) || !prof.TryGetProperty("info_cache", out cache) ||
                        cache.ValueKind != JsonValueKind.Object) return names;
                    foreach (var p in cache.EnumerateObject())
                    {
                        JsonElement n;
                        if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("name", out n) && n.ValueKind == JsonValueKind.String)
                            names[p.Name] = n.GetString();
                    }
                }
            }
            catch (Exception ex) { Log.Write("Bookmarks: profile names in " + userData + ": " + ex.Message); }
            return names;
        }

        /// <summary>The browser may be writing the file - read it without locking it.</summary>
        static string ReadShared(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
                return r.ReadToEnd();
        }

        // ---- reading a Bookmarks file ------------------------------------------------------------------
        static readonly Dictionary<string, Tuple<DateTime, List<Node>>> Cache = new Dictionary<string, Tuple<DateTime, List<Node>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The top folders of a Bookmarks file (bookmarks bar, other bookmarks, mobile bookmarks), or null when it can't be read.</summary>
        public static List<Node> Read(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file)) return null;
                DateTime stamp = System.IO.File.GetLastWriteTimeUtc(file);
                lock (Cache)
                {
                    Tuple<DateTime, List<Node>> c;
                    if (Cache.TryGetValue(file, out c) && c.Item1 == stamp) return c.Item2;
                }
                var roots = new List<Node>();
                using (var doc = JsonDocument.Parse(ReadShared(file)))
                {
                    JsonElement r;
                    if (!doc.RootElement.TryGetProperty("roots", out r) || r.ValueKind != JsonValueKind.Object) return null;
                    foreach (string key in new[] { "bookmark_bar", "other", "synced" })
                    {
                        JsonElement el;
                        if (!r.TryGetProperty(key, out el) || el.ValueKind != JsonValueKind.Object) continue;
                        Node n = ToNode(el, "");
                        if (n != null && n.IsFolder) roots.Add(n);
                    }
                }
                lock (Cache) Cache[file] = Tuple.Create(stamp, roots);
                return roots;
            }
            catch (Exception ex)
            {
                Log.Write("Bookmarks: reading " + file + ": " + ex.Message);
                return null;
            }
        }

        static string Str(JsonElement el, string prop)
        {
            JsonElement v;
            return el.TryGetProperty(prop, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static Node ToNode(JsonElement el, string parentPath)
        {
            string type = Str(el, "type");
            var n = new Node { Name = Str(el, "name"), Guid = Str(el, "guid") };
            if (type == "url")
            {
                n.Url = Str(el, "url");
                return n;
            }
            if (type != "folder") return null;
            n.Path = parentPath.Length == 0 ? n.Name : parentPath + "/" + n.Name;
            JsonElement kids;
            if (el.TryGetProperty("children", out kids) && kids.ValueKind == JsonValueKind.Array)
                foreach (var k in kids.EnumerateArray())
                {
                    if (k.ValueKind != JsonValueKind.Object) continue;
                    Node c = ToNode(k, n.Path);
                    if (c != null) n.Children.Add(c);
                }
            return n;
        }

        /// <summary>The folder a synced group follows: by its guid, or else by where it is.</summary>
        public static Node Find(List<Node> roots, BookmarkFolder f)
        {
            if (roots == null || f == null) return null;
            Node byPath = null;
            var stack = new Stack<Node>(roots);
            while (stack.Count > 0)
            {
                Node n = stack.Pop();
                if (!n.IsFolder) continue;
                if (!string.IsNullOrEmpty(f.Guid) && string.Equals(n.Guid, f.Guid, StringComparison.OrdinalIgnoreCase)) return n;
                if (byPath == null && string.Equals(n.Path, f.Folder ?? "", StringComparison.Ordinal)) byPath = n;
                foreach (var c in n.Children) stack.Push(c);
            }
            return byPath;
        }

        /// <summary>A folder's bookmarks, subfolders included, in the browser's order (bookmarklets left out).</summary>
        public static List<Mark> MarksIn(Node folder)
        {
            var list = new List<Mark>();
            AddMarks(folder, list);
            return list;
        }

        static void AddMarks(Node folder, List<Mark> list)
        {
            if (folder == null) return;
            foreach (var c in folder.Children)
            {
                if (c.IsFolder) { AddMarks(c, list); continue; }
                string url = (c.Url ?? "").Trim();
                if (url.Length == 0 || url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new Mark { Name = string.IsNullOrWhiteSpace(c.Name) ? NameFor(url) : c.Name.Trim(), Url = url });
            }
        }

        /// <summary>A bookmark without a name: its site's name.</summary>
        static string NameFor(string url)
        {
            Uri u;
            if (Uri.TryCreate(url, UriKind.Absolute, out u) && !string.IsNullOrEmpty(u.Host))
                return u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host.Substring(4) : u.Host;
            return url;
        }

        /// <summary>
        /// The bookmarks a group should have, in order, or null when a Bookmarks file can't be read or a folder isn't in it any more
        /// (the browser uninstalled, a profile removed …) - the group's bookmarks are left alone then.
        /// </summary>
        public static List<Mark> Wanted(List<BookmarkFolder> folders)
        {
            var list = new List<Mark>();
            foreach (var f in folders)
            {
                Node n = Find(Read(f.File), f);
                if (n == null) return null;
                f.Folder = n.Path;      // renamed or moved in the browser: show where it is now
                list.AddRange(MarksIn(n));
            }
            return list;
        }
    }
}
