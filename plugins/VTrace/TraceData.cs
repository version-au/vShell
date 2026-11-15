using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using vShell.Core;
using vShell.Ui;

namespace VShellVTrace
{
    /// <summary>One change as vTrace saved it (the fields the modules show).</summary>
    public sealed class Change
    {
        public string Id = "", Source = "", Title = "", Detail = "", Host = "", Kind = "";
        public DateTime When, Until;
        public bool Failed;
        /// <summary>An outage that's still going.</summary>
        public bool Down { get { return Source == "outage" && Until == DateTime.MinValue; } }
        // an outage's analysis (vTrace 0.1.11+; empty from older ones): it went down with others (Group = their row's id; the row has
        // GroupOn "pve" and GroupCount), what it runs on, and the likely cause
        public string Group = "", GroupOn = "", RunsOn = "", Cause = "", CauseWhy = "", CauseId = "";
        /// <summary>vTrace's local AI on it (0.1.14+): why an outage happened, and which model said it.</summary>
        public string Explanation = "", AiBy = "";
        public int GroupCount;
        /// <summary>Shown in its group's row, not on its own.</summary>
        public bool Folded { get { return Group.Length > 0; } }
        /// <summary>"with 5 more on pve", or "".</summary>
        public string WithMore { get { return GroupCount > 0 ? "with " + GroupCount + " more" + (GroupOn.Length > 0 ? " on " + GroupOn : "") : ""; } }
    }

    /// <summary>
    /// vTrace's changes, read from its changes.json (%APPDATA%\vTrace, or VTRACE_DATA_DIR) - read only, again whenever vTrace writes it
    /// (after each of its looks). vShell never reads your servers itself for this: vTrace does, and this shows what it found.
    /// </summary>
    public static class TraceData
    {
        static readonly object Gate = new object();
        static List<Change> _all = new List<Change>();
        static DateTime _stamp = DateTime.MinValue;
        static string _error;

        public static string Folder
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("VTRACE_DATA_DIR");
                return string.IsNullOrWhiteSpace(d) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vTrace") : d;
            }
        }

        static string File_ { get { return Path.Combine(Folder, "changes.json"); } }

        /// <summary>vTrace has run here (its changes file is there), or a vTrace server is set up.</summary>
        public static bool There { get { return TraceServer.On || File.Exists(File_); } }

        /// <summary>Why the changes couldn't be read, or null.</summary>
        public static string Error
        {
            get
            {
                if (TraceServer.On) { VtSnapshot s = TraceServer.Latest; return s.Checked && !s.Ok ? s.Error : null; }
                lock (Gate) return _error;
            }
        }

        /// <summary>Every change, newest first (read again if vTrace wrote the file since).</summary>
        public static List<Change> All
        {
            get
            {
                if (TraceServer.On) return TraceServer.Latest.Changes;
                lock (Gate)
                {
                    try
                    {
                        if (!File.Exists(File_)) { _all = new List<Change>(); _error = null; return _all; }
                        DateTime t = File.GetLastWriteTimeUtc(File_);
                        if (t != _stamp)
                        {
                            string text;
                            using (var fs = new FileStream(File_, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                            var list = new List<Change>();
                            using (JsonDocument d = JsonDocument.Parse(text))
                                foreach (JsonElement e in d.RootElement.EnumerateArray())
                                    list.Add(new Change
                                    {
                                        Id = Str(e, "Id"), Source = Str(e, "Source"), Title = Str(e, "Title"), Detail = Str(e, "Detail"), Host = Str(e, "Host"),
                                        Kind = Str(e, "Kind"), When = Date(e, "When"), Until = Date(e, "Until"),
                                        Failed = e.TryGetProperty("Failed", out JsonElement f) && f.ValueKind == JsonValueKind.True,
                                        Group = Str(e, "Group"), GroupOn = Str(e, "GroupOn"), RunsOn = Str(e, "RunsOn"), Cause = Str(e, "Cause"), CauseWhy = Str(e, "CauseWhy"), CauseId = Str(e, "CauseId"), Explanation = Str(e, "Explanation"), AiBy = Str(e, "AiBy"),
                                        GroupCount = e.TryGetProperty("GroupCount", out JsonElement gc) && gc.ValueKind == JsonValueKind.Number ? gc.GetInt32() : 0
                                    });
                            _all = list.OrderByDescending(c => c.When).ToList();
                            _stamp = t;
                            _error = null;
                        }
                    }
                    catch (IOException) { }      // vTrace is writing it: the last read stays a moment longer
                    catch (Exception ex) { _error = "vTrace's changes couldn't be read: " + ex.Message; Log.Write("vTrace plugin: " + ex.Message); }
                    return _all;
                }
            }
        }

        static string Str(JsonElement e, string n) { return e.TryGetProperty(n, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; }

        static DateTime Date(JsonElement e, string n)
        {
            if (!e.TryGetProperty(n, out JsonElement v) || v.ValueKind != JsonValueKind.String) return DateTime.MinValue;
            return DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d) ? d : DateTime.MinValue;
        }

        /// <summary>vTrace's names for its sources.</summary>
        public static string SourceName(string id)
        {
            switch (id)
            {
                case "outage": return "Outages";
                case "server": return "Servers";
                case "pc": return TraceServer.On ? "PCs" : "This PC";
                case "vdeploy": return "vDeploy";
                case "komodo": return "Komodo";
                case "proxmox": return "Proxmox";
                case "ha": return "Home Assistant";
                case "unifi": return "UniFi";
                case "npm": return "Proxy Manager";
                default: return id;
            }
        }

        /// <summary>Where vTrace is installed (or the running one's exe), or null.</summary>
        static string Exe()
        {
            string exe = Environment.GetEnvironmentVariable("VSHELL_VTRACE_EXE");
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe)) return exe;
            exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "vTrace", "vTrace.exe");
            if (File.Exists(exe)) return exe;
            try
            {
                Process running = Process.GetProcessesByName("vTrace").FirstOrDefault();
                if (running != null && running.MainModule != null) return running.MainModule.FileName;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Open vTrace (it comes to the front if it's running), or say where to get it. show = a change's id: vTrace opens on it (vTrace
        /// 0.1.12+ - an older one just opens).
        /// </summary>
        public static void OpenApp(string show = null)
        {
            if (TraceServer.On) { TraceServer.Open(show); return; }
            string exe = Exe();
            if (exe == null) { Questions.Tell(null, "vTrace isn't installed. Install it from Settings > Updates > vSuite apps.", "vTrace"); return; }
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                if (!string.IsNullOrEmpty(show)) { psi.ArgumentList.Add("--show"); psi.ArgumentList.Add(show); }
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("vTrace plugin: open vTrace: " + ex.Message); }
        }

        // ---- the refresh button (only while vTrace isn't open) ----

        static Process _refresh;
        static DateTime _openChecked = DateTime.MinValue;
        static bool _open;

        /// <summary>
        /// vTrace is open (its window, shown or in the tray): it looks for changes itself, so there's no refresh button. A refresh's
        /// vTrace (--refresh) has no window, so it doesn't count. Checked at most every 2 seconds.
        /// </summary>
        public static bool AppOpen
        {
            get
            {
                if (TraceServer.On) return false;      // the server: the refresh button asks it to look now
                if ((DateTime.UtcNow - _openChecked).TotalSeconds < 2) return _open;
                bool found = false;
                EnumWindows((h, l) => { if (GetProp(h, "vTrace.Instance") != IntPtr.Zero) { found = true; return false; } return true; }, IntPtr.Zero);
                _open = found;
                _openChecked = DateTime.UtcNow;
                return found;
            }
        }

        /// <summary>A refresh started here is still going.</summary>
        public static bool Refreshing
        {
            get
            {
                if (TraceServer.On) return TraceServer.Looking;
                try { return _refresh != null && !_refresh.HasExited; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Look for new changes now: vTrace.exe --refresh looks at every source once, with no window or tray icon, and closes; the
        /// modules show the new changes when it has written them.
        /// </summary>
        public static void Refresh() { Refresh(false); }

        /// <summary>Settings > Plugins > vTrace > Check for changes: every so many minutes while vTrace isn't open, 0 = off.</summary>
        public static int AutoMinutes;

        /// <summary>Once a minute: a check when one is due (not while vTrace is open or a check is going; quietly if it isn't installed).</summary>
        public static void AutoCheck()
        {
            if (TraceServer.On || AutoMinutes <= 0 || Refreshing || AppOpen) return;
            DateTime last = LastLook;
            if (last != DateTime.MinValue && (DateTime.Now - last).TotalMinutes < AutoMinutes - 0.5) return;
            Refresh(true);
        }

        static void Refresh(bool quiet)
        {
            if (TraceServer.On) { TraceServer.LookNow(); return; }
            if (Refreshing) return;
            string exe = Exe();
            if (exe == null) { if (!quiet) Questions.Tell(null, "vTrace isn't installed. Install it from Settings > Updates > vSuite apps.", "vTrace"); return; }
            try { _refresh = Process.Start(new ProcessStartInfo(exe, "--refresh") { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception ex) { Log.Write("vTrace plugin: refresh: " + ex.Message); }
        }

        /// <summary>When vTrace last looked at every source (its looked.txt, else when changes.json was written), or MinValue.</summary>
        public static DateTime LastLook
        {
            get
            {
                if (TraceServer.On) return TraceServer.Latest.Looked;
                try
                {
                    string f = Path.Combine(Folder, "looked.txt");
                    if (File.Exists(f) && DateTime.TryParse(File.ReadAllText(f).Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d)) return d.ToLocalTime();
                    if (File.Exists(File_)) return File.GetLastWriteTime(File_);
                }
                catch { }
                return DateTime.MinValue;
            }
        }

        /// <summary>"Checked 2:14 PM · 3 h ago" (or "Not checked yet").</summary>
        public static string CheckedText()
        {
            if (Refreshing) return "Checking for changes…";
            DateTime d = LastLook;
            if (d == DateTime.MinValue) return "Not checked yet";
            TimeSpan ago = DateTime.Now - d;
            string age = ago.TotalMinutes < 1 ? "just now" : ago.TotalMinutes < 60 ? (int)ago.TotalMinutes + " min ago" :
                         ago.TotalHours < 24 ? (int)ago.TotalHours + " h ago" : (int)ago.TotalDays + " day" + ((int)ago.TotalDays == 1 ? "" : "s") + " ago";
            return "Checked " + (d.Date == DateTime.Today ? TimeText.Clock(d) : d.ToString("ddd") + " " + TimeText.Clock(d)) + " · " + age;
        }

        /// <summary>The tip on the "Checked …" line.</summary>
        public static string RefreshTip
        {
            get
            {
                return TraceServer.On ? "The vTrace server looks every few minutes by itself - the refresh button asks it to look now."
                                      : "vTrace isn't open, so nothing new comes in by itself - the refresh button checks now.";
            }
        }

        /// <summary>Last checked over an hour ago (shown in the warning colour).</summary>
        public static bool Stale { get { DateTime d = LastLook; return d != DateTime.MinValue && (DateTime.Now - d).TotalHours >= 1; } }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr GetProp(IntPtr h, string name);
    }
}
