using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace vShell.Core
{
    /// <summary>One app pinned to Windows' own taskbar.</summary>
    public sealed class WindowsPin
    {
        public string Name = "";
        /// <summary>The shortcut (.lnk) in the pinned folder, or "shell:AppsFolder\AUMID" for a Store app.</summary>
        public string Path = "";
        /// <summary>The program a shortcut starts ("" for a Store app or when it can't be read).</summary>
        public string Target = "";
        public string Aumid = "";
        public bool IsStore { get { return Aumid.Length > 0; } }

        /// <summary>As a vShell taskbar pin (its windows group onto it while it runs).</summary>
        public PinnedApp ToPin()
        {
            if (IsStore) return new PinnedApp { Name = Name, Path = Path, AppId = "aumid:" + Aumid.ToLowerInvariant() };
            return Shortcuts.PinFor(Shortcuts.Keep(Path), Name);
        }

        /// <summary>Is this already that taskbar pin?</summary>
        public bool Matches(PinnedApp p)
        {
            if (p == null) return false;
            if (IsStore) return string.Equals(p.AppId, "aumid:" + Aumid, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Path, Path, StringComparison.OrdinalIgnoreCase);
            if (Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && string.Equals(p.AppId, "exe:" + Target, StringComparison.OrdinalIgnoreCase)) return true;
            return SameFile(p.Path);
        }

        /// <summary>Is this already that launcher shortcut?</summary>
        public bool Matches(LauncherEntry e)
        {
            if (e == null || e.IsGroup) return false;
            if (IsStore) return string.Equals(e.Path, Path, StringComparison.OrdinalIgnoreCase);
            return SameFile(e.Path) || (Target.Length > 0 && string.Equals(e.Path, Target, StringComparison.OrdinalIgnoreCase));
        }

        bool SameFile(string other)
        {
            if (string.IsNullOrEmpty(other)) return false;
            if (string.Equals(other, Path, StringComparison.OrdinalIgnoreCase)) return true;
            try { return string.Equals(System.IO.Path.GetFileName(other), System.IO.Path.GetFileName(Path), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>As a launcher shortcut.</summary>
        public LauncherEntry ToEntry() { return new LauncherEntry { Name = Name, Path = IsStore ? Path : Shortcuts.Keep(Path) }; }
    }

    /// <summary>Shortcut (.lnk) helpers and making pins / launcher entries from files.</summary>
    public static class Shortcuts
    {
        /// <summary>The program a .lnk points at ("" when it can't be read).</summary>
        public static string Target(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return "";
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                string target = sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(sc); System.Runtime.InteropServices.Marshal.ReleaseComObject(sh); } catch { }
                return target ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// A copy of a shortcut from Windows' pinned folder in vShell's own folder (Windows deletes its copy when you unpin it there,
        /// so the vShell pin would stop working). Other files are left where they are.
        /// </summary>
        public static string Keep(string lnk)
        {
            try
            {
                if (!lnk.StartsWith(WindowsPins.Folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(lnk)) return lnk;
                string dir = System.IO.Path.Combine(BarSettings.ConfigDir, "Pinned");
                Directory.CreateDirectory(dir);
                string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(lnk));
                File.Copy(lnk, dest, true);
                return dest;
            }
            catch (Exception ex) { Log.Write("Keep shortcut " + lnk + ": " + ex.Message); return lnk; }
        }

        /// <summary>A file's display name: a shortcut's own name, a program's description, else the file name.</summary>
        public static string NameOf(string path)
        {
            try
            {
                if (Directory.Exists(path)) return System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
                string ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
                if (ext == ".exe")
                {
                    var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(v.FileDescription)) return v.FileDescription.Trim();
                }
                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch { return path; }
        }

        /// <summary>A taskbar pin for a program / shortcut / any file (the shortcut stays the thing that starts; its program groups the windows).</summary>
        public static PinnedApp PinFor(string path, string name = null)
        {
            string target = path;
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string t = Target(path);
                if (!string.IsNullOrEmpty(t)) target = t;
            }
            return new PinnedApp
            {
                Name = string.IsNullOrWhiteSpace(name) ? NameOf(path) : name,
                Path = path,
                AppId = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "exe:" + target.ToLowerInvariant() : ""
            };
        }

        /// <summary>
        /// A pin that points inside C:\Program Files\WindowsApps (a packaged app such as WhatsApp, pinned from its window by older vShell
        /// versions): it can't be started or given an icon from there. Turn it into the Store app's own "shell:AppsFolder\id" pin, using the
        /// id the running window gave (aumid) or the Start menu's list of Store apps. true = it was changed.
        /// </summary>
        public static bool Repair(PinnedApp p, string aumid = null)
        {
            if (p == null || string.IsNullOrEmpty(p.Path)) return false;
            string inside = p.Path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0 ? p.Path
                          : p.AppId.StartsWith("exe:") && p.AppId.IndexOf("\\windowsapps\\", StringComparison.OrdinalIgnoreCase) >= 0 ? p.AppId.Substring(4) : null;
            if (inside == null) return false;
            if (string.IsNullOrEmpty(aumid))
            {
                string family = WindowProps.PackageFamilyFromPath(inside);
                if (family.Length == 0) return false;
                foreach (AppEntry e in AppCatalog.Get())
                    if (e.IsStore && e.Target.StartsWith("shell:AppsFolder\\" + family + "!", StringComparison.OrdinalIgnoreCase))
                    {
                        aumid = e.Target.Substring("shell:AppsFolder\\".Length);
                        break;
                    }
            }
            if (string.IsNullOrEmpty(aumid)) return false;
            p.Path = "shell:AppsFolder\\" + aumid;
            p.Args = "";
            p.AppId = "aumid:" + aumid.ToLowerInvariant();
            Log.Write("Taskbar: pin of " + inside + " now uses the Store app " + aumid);
            return true;
        }

        /// <summary>Is this pin the same app as that one (so an import doesn't add it twice)?</summary>
        public static bool SameApp(PinnedApp a, PinnedApp b)
        {
            if (a == null || b == null) return false;
            if (a.AppId.Length > 0 && string.Equals(a.AppId, b.AppId, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Reads the apps pinned to Windows' taskbar: the shortcuts in "User Pinned\TaskBar", and Windows' own list of them (the Taskband
    /// "Favorites" registry value), which also holds the pinned Store apps and the order they are in. That list's format isn't documented:
    /// the shortcut names and Store app ids are picked out of it in the order they appear; shortcuts it doesn't name go last.
    /// </summary>
    public static class WindowsPins
    {
        public static string Folder
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"); }
        }

        static readonly Regex AumidRx = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.\-]*_[a-z0-9]{13}![A-Za-z0-9.\-_]+$", RegexOptions.Compiled);

        /// <summary>The readable texts in a binary value, in order (UTF-16 runs of 4+ printable characters).</summary>
        static List<string> Strings(byte[] b)
        {
            var list = new List<string>();
            if (b == null) return list;
            for (int start = 0; start < 2; start++)     // both byte alignments
            {
                var sb = new StringBuilder();
                int at = -1;
                for (int i = start; i + 1 < b.Length; i += 2)
                {
                    char c = (char)(b[i] | (b[i + 1] << 8));
                    // names and ids: ASCII and the European letters (bytes read at the wrong alignment come out as CJK-range junk)
                    if ((c >= 0x20 && c < 0x7F) || (c >= 0xA0 && c < 0x2000))
                    {
                        if (sb.Length == 0) at = i;
                        sb.Append(c);
                    }
                    else
                    {
                        if (sb.Length >= 4) list.Add(at.ToString("D8") + "\u0001" + sb);
                        sb.Clear();
                    }
                }
                if (sb.Length >= 4) list.Add(at.ToString("D8") + "\u0001" + sb);
            }
            list.Sort(StringComparer.Ordinal);       // by position in the value
            for (int i = 0; i < list.Count; i++) list[i] = list[i].Substring(list[i].IndexOf('\u0001') + 1);
            return list;
        }

        public static List<WindowsPin> Read(Func<string, string> storeName)
        {
            var result = new List<WindowsPin>();
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);     // file name -> full path
            try
            {
                if (Directory.Exists(Folder))
                    foreach (string f in Directory.GetFiles(Folder, "*.lnk")) files[System.IO.Path.GetFileName(f)] = f;
            }
            catch (Exception ex) { Log.Write("Windows pins folder: " + ex.Message); }

            byte[] blob = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband"))
                    if (k != null) blob = k.GetValue("Favorites") as byte[];
            }
            catch (Exception ex) { Log.Write("Windows pins order: " + ex.Message); }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in Strings(blob))
            {
                string text = s.Trim();
                // a shortcut file named in the list (sometimes with its folder in front)
                int slash = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
                string file = slash >= 0 ? text.Substring(slash + 1) : text;
                string full;
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && files.TryGetValue(file, out full))
                {
                    if (used.Add(full)) result.Add(FromLnk(full));
                    continue;
                }
                // a Store app (Calculator, Terminal ...): its AppUserModelID
                foreach (string part in text.Split(' ', '\t', ';', ','))
                {
                    string id = part.Trim();
                    if (!AumidRx.IsMatch(id) || !used.Add("aumid:" + id)) continue;
                    string name = storeName != null ? storeName(id) : null;
                    if (string.IsNullOrEmpty(name))
                    {
                        string pkg = id.Substring(0, id.IndexOf('_'));
                        int dot = pkg.LastIndexOf('.');
                        name = dot >= 0 ? pkg.Substring(dot + 1) : pkg;
                    }
                    result.Add(new WindowsPin { Name = name, Path = "shell:AppsFolder\\" + id, Aumid = id });
                }
            }
            // shortcuts the list didn't mention: after the others, by name
            var rest = new List<string>();
            foreach (var kv in files) if (!used.Contains(kv.Value)) rest.Add(kv.Value);
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in rest) result.Add(FromLnk(f));
            return result;
        }

        static WindowsPin FromLnk(string lnk)
        {
            return new WindowsPin { Name = System.IO.Path.GetFileNameWithoutExtension(lnk), Path = lnk, Target = Shortcuts.Target(lnk) };
        }
    }
}
