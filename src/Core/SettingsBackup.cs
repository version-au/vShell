using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace vShell.Core
{
    /// <summary>A settings backup in %APPDATA%\vShell\backups.</summary>
    public sealed class BackupInfo
    {
        public string Path = "";
        /// <summary>daily, manual, before-restore, broken (a settings.json that couldn't be read).</summary>
        public string Kind = "";
        /// <summary>Local time it was made.</summary>
        public DateTime When;
        public long Size;

        /// <summary>"Daily", "Made by you" …</summary>
        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case "daily": return "Daily";
                    case "manual": return "Made by you";
                    case "before-restore": return "Before a restore";
                    case "broken": return "Couldn't be read";
                    default: return Kind;
                }
            }
        }
    }

    /// <summary>
    /// Keeps copies of settings.json so one bad file (a power cut half-way through writing it, a hand edit gone wrong) can't lose
    /// everything: a daily copy (the last 7 kept), ones you make, and one before each restore. A settings.json that can't be read at start
    /// is put aside ("broken") and the newest backup that can be read is used instead - before, vShell started from defaults and its first
    /// save wrote over the old file.
    /// </summary>
    public static class SettingsBackup
    {
        const int KeepDaily = 7, KeepOther = 10;

        public static string Dir { get { return Path.Combine(BarSettings.ConfigDir, "backups"); } }

        /// <summary>Set at start when settings.json couldn't be read and this backup was used instead (for the notification).</summary>
        public static BackupInfo RestoredFrom;
        /// <summary>Set at start when settings.json couldn't be read: where the unreadable file was put.</summary>
        public static string BrokenCopy;
        /// <summary>A restore or import is waiting for the restart: the running vShell must not write its settings over it.</summary>
        public static bool SaveBlocked;

        /// <summary>The backups, newest first.</summary>
        public static List<BackupInfo> List()
        {
            var l = new List<BackupInfo>();
            try
            {
                if (!Directory.Exists(Dir)) return l;
                foreach (string f in Directory.GetFiles(Dir, "settings-*.json"))
                {
                    // settings-20261028-153000-daily.json
                    string n = Path.GetFileNameWithoutExtension(f);
                    string[] p = n.Split(new[] { '-' }, 4);
                    DateTime when;
                    if (p.Length < 4 || !DateTime.TryParseExact(p[1] + p[2], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) continue;
                    l.Add(new BackupInfo { Path = f, Kind = p[3].Split('_')[0], When = when, Size = new FileInfo(f).Length });
                }
            }
            catch (Exception ex) { Log.Write("Backups: listing: " + ex.Message); }
            l.Sort(delegate (BackupInfo a, BackupInfo b) { return b.When.CompareTo(a.When); });
            return l;
        }

        /// <summary>Settings read from a file, or null when it isn't vShell settings (or can't be read).</summary>
        public static BarSettings Read(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                if (json.Trim().Length == 0) return null;
                return JsonSerializer.Deserialize<BarSettings>(json, BarSettings.JsonOpts);
            }
            catch { return null; }
        }

        /// <summary>Copy a file into the backups as this kind. Null when it didn't work.</summary>
        static BackupInfo Copy(string from, string kind)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                DateTime now = DateTime.Now;
                string to = Path.Combine(Dir, "settings-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + kind + ".json");
                for (int i = 2; File.Exists(to); i++)      // two in the same second
                    to = Path.Combine(Dir, "settings-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + kind + "_" + i + ".json");
                File.Copy(from, to);
                Prune();
                return new BackupInfo { Path = to, Kind = kind, When = now, Size = new FileInfo(to).Length };
            }
            catch (Exception ex) { Log.Write("Backups: copying " + kind + ": " + ex.Message); return null; }
        }

        /// <summary>The last 7 daily ones; 10 each of before-restore / broken. The ones you made are never removed.</summary>
        static void Prune()
        {
            var seen = new Dictionary<string, int>();
            foreach (BackupInfo b in List())
            {
                string kind = b.Kind;
                int n;
                seen.TryGetValue(kind, out n);
                seen[kind] = ++n;
                bool drop = kind == "daily" ? n > KeepDaily : kind != "manual" && n > KeepOther;
                if (drop) try { File.Delete(b.Path); } catch { }
            }
        }

        /// <summary>
        /// Before settings.json is written: once a day, the file as it is now (when it can be read - a bad one never pushes good backups
        /// out). Called by BarSettings.Save.
        /// </summary>
        public static void Daily(bool on)
        {
            if (!on || (DateTime.Now - _lastDaily).TotalHours < 20 || !File.Exists(BarSettings.ConfigPath)) return;
            foreach (BackupInfo b in List())
                if (b.Kind == "daily") { _lastDaily = b.When; if ((DateTime.Now - b.When).TotalHours < 20) return; break; }
            if (Read(BarSettings.ConfigPath) == null) return;
            BackupInfo made = Copy(BarSettings.ConfigPath, "daily");
            _lastDaily = DateTime.Now;      // also when it failed: tried again tomorrow, not on every save
            if (made != null) Log.Write("Backups: daily copy of the settings made");
        }

        static DateTime _lastDaily = DateTime.MinValue;

        /// <summary>Back up now (the Back up now button): the settings as they are in vShell, saved first. Null when it didn't work.</summary>
        public static BackupInfo Now(BarSettings s)
        {
            s.Save();
            return File.Exists(BarSettings.ConfigPath) ? Copy(BarSettings.ConfigPath, "manual") : null;
        }

        /// <summary>
        /// Put a backup (or an exported file) in as settings.json, keeping the current one as "before-restore" first. vShell must restart
        /// afterwards (SaveBlocked stops it writing the old settings back while it closes). Null when it worked, otherwise why not.
        /// </summary>
        public static string Restore(string path)
        {
            if (Read(path) == null) return "That file isn't vShell settings, or it can't be read.";
            try
            {
                if (File.Exists(BarSettings.ConfigPath) && Read(BarSettings.ConfigPath) != null) Copy(BarSettings.ConfigPath, "before-restore");
                string tmp = BarSettings.ConfigPath + ".tmp";
                File.Copy(path, tmp, true);
                File.Move(tmp, BarSettings.ConfigPath, true);
                SaveBlocked = true;
                Log.Write("Backups: settings restored from " + path);
                return null;
            }
            catch (Exception ex) { return "Couldn't put the settings back: " + ex.Message; }
        }

        /// <summary>
        /// At start, settings.json is there but can't be read: put it aside as "broken" and use the newest backup that can be read (it
        /// becomes settings.json). Returns those settings, or null when there's no usable backup (vShell then starts from defaults, and the
        /// broken file is still kept).
        /// </summary>
        public static BarSettings Recover()
        {
            BackupInfo broken = Copy(BarSettings.ConfigPath, "broken");
            if (broken == null) return null;      // couldn't even keep a copy: leave settings.json alone
            BrokenCopy = broken.Path;
            try { File.Delete(BarSettings.ConfigPath); } catch { }
            Log.Write("Backups: settings.json couldn't be read - kept it as " + broken.Path);
            foreach (BackupInfo b in List())
            {
                if (b.Kind == "broken") continue;
                BarSettings s = Read(b.Path);
                if (s == null) continue;
                try { File.Copy(b.Path, BarSettings.ConfigPath, true); }
                catch (Exception ex) { Log.Write("Backups: putting " + b.Path + " back: " + ex.Message); }
                RestoredFrom = b;
                Log.Write("Backups: using the backup from " + b.When.ToString("g", CultureInfo.CurrentCulture) + " (" + b.Path + ")");
                return s;
            }
            Log.Write("Backups: no backup could be read - starting with new settings");
            return null;
        }
    }
}
