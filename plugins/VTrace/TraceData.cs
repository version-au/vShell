using System;
using System.Collections.Generic;
using vShell.Core;

namespace VShellVTrace
{
    /// <summary>One change as vTrace has it (the fields the modules show).</summary>
    public sealed class Change
    {
        public string Id = "", Source = "", Title = "", Detail = "", Host = "", Kind = "";
        public DateTime When, Until;
        public bool Failed;
        /// <summary>An outage that's still going.</summary>
        public bool Down { get { return Source == "outage" && Until == DateTime.MinValue; } }
        // an outage's analysis: it went down with others (Group = their row's id; the row has GroupOn "pve" and GroupCount), what it runs
        // on, and the likely cause
        public string Group = "", GroupOn = "", RunsOn = "", Cause = "", CauseWhy = "", CauseId = "";
        /// <summary>vTrace's local AI on it: why an outage happened, and which model said it.</summary>
        public string Explanation = "", AiBy = "";
        public int GroupCount;
        /// <summary>Shown in its group's row, not on its own.</summary>
        public bool Folded { get { return Group.Length > 0; } }
        /// <summary>"with 5 more on pve", or "".</summary>
        public string WithMore { get { return GroupCount > 0 ? "with " + GroupCount + " more" + (GroupOn.Length > 0 ? " on " + GroupOn : "") : ""; } }
    }

    /// <summary>
    /// What the modules ask about vTrace - all from the vTrace server (TraceServer). The Windows vTrace (its changes.json, vTrace.exe
    /// --refresh / --show) retired on 2026-11-15; plugin 2.1.0 dropped it.
    /// </summary>
    public static class TraceData
    {
        /// <summary>A vTrace server is set up.</summary>
        public static bool There { get { return TraceServer.On; } }

        /// <summary>Why the server couldn't be read, or null.</summary>
        public static string Error
        {
            get
            {
                if (!TraceServer.On) return null;
                VtSnapshot s = TraceServer.Latest;
                return s.Checked && !s.Ok ? s.Error : null;
            }
        }

        /// <summary>The last week's changes, newest first.</summary>
        public static List<Change> All { get { return TraceServer.On ? TraceServer.Latest.Changes : new List<Change>(); } }

        /// <summary>vTrace's names for its sources.</summary>
        public static string SourceName(string id)
        {
            switch (id)
            {
                case "outage": return "Outages";
                case "server": return "Servers";
                case "pc": return "PCs";
                case "vdeploy": return "vDeploy";
                case "vpatch": return "vPatch";
                case "komodo": return "Komodo";
                case "proxmox": return "Proxmox";
                case "ha": return "Home Assistant";
                case "unifi": return "UniFi";
                case "npm": return "Proxy Manager";
                default: return id;
            }
        }

        /// <summary>vTrace's page for a change (or its timeline) in the browser.</summary>
        public static void OpenApp(string show = null) { TraceServer.Open(show); }

        /// <summary>Kept for the modules: the server always takes the refresh button (it never "is open" the way the Windows app was).</summary>
        public static bool AppOpen { get { return false; } }

        /// <summary>The server is looking.</summary>
        public static bool Refreshing { get { return TraceServer.On && TraceServer.Looking; } }

        /// <summary>The refresh button: the server looks at every source now.</summary>
        public static void Refresh() { if (TraceServer.On) TraceServer.LookNow(); }

        /// <summary>When the server last looked at every source, or MinValue.</summary>
        public static DateTime LastLook { get { return TraceServer.On ? TraceServer.Latest.Looked : DateTime.MinValue; } }

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
        public static string RefreshTip { get { return "The vTrace server looks every few minutes by itself - the refresh button asks it to look now."; } }

        /// <summary>Last checked over an hour ago (shown in the warning colour).</summary>
        public static bool Stale { get { DateTime d = LastLook; return d != DateTime.MinValue && (DateTime.Now - d).TotalHours >= 1; } }
    }
}
