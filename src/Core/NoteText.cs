using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace vShell.Core
{
    /// <summary>
    /// The text of a note, as lines of Markdown. Checklists are task lines ("- [ ] milk", "- [x] bread", with an optional due date
    /// "📅 2026-10-19" or "due:2026-10-19"); log entries are "- **2026-10-16 14:52** text", newest first. Everything else is kept as it is,
    /// so a file edited elsewhere keeps its other lines.
    /// </summary>
    public sealed class NoteText
    {
        public readonly List<string> Lines = new List<string>();

        static readonly Regex TaskRx = new Regex(@"^(\s*)([-*+])\s\[( |x|X)\]\s?(.*)$");
        static readonly Regex DueRx = new Regex(@"\s*(?:📅|due:)\s*(\d{4}-\d{2}-\d{2})");
        static readonly Regex LogRx = new Regex(@"^\s*[-*]\s+\*\*(\d{4}-\d{2}-\d{2} \d{1,2}:\d{2})\*\*\s?(.*)$");
        public const string LogStamp = "yyyy-MM-dd HH:mm";

        public NoteText(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length > 0) Lines.AddRange(text.Split('\n'));
            // a trailing newline gives an empty last line: drop it (put back on save)
            if (Lines.Count > 0 && Lines[Lines.Count - 1].Length == 0) Lines.RemoveAt(Lines.Count - 1);
        }

        public override string ToString() { return Lines.Count == 0 ? "" : string.Join("\n", Lines) + "\n"; }

        // ---- checklist ------------------------------------------------------------------------------
        public sealed class Item
        {
            public int Line;
            public bool Done;
            public string Text = "";
            public DateTime? Due;
            public int Indent;
        }

        public static bool TryTask(string line, out Item item)
        {
            item = null;
            Match m = TaskRx.Match(line ?? "");
            if (!m.Success) return false;
            item = new Item { Done = m.Groups[3].Value != " ", Indent = m.Groups[1].Value.Replace("\t", "    ").Length / 2 };
            string t = m.Groups[4].Value;
            Match d = DueRx.Match(t);
            if (d.Success)
            {
                DateTime due;
                if (DateTime.TryParseExact(d.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out due)) item.Due = due;
                t = t.Remove(d.Index, d.Length);
            }
            item.Text = t.Trim();
            return true;
        }

        public List<Item> Items()
        {
            var list = new List<Item>();
            for (int i = 0; i < Lines.Count; i++)
            {
                Item it;
                if (TryTask(Lines[i], out it)) { it.Line = i; list.Add(it); }
            }
            return list;
        }

        public int DoneCount() { int n = 0; foreach (Item i in Items()) if (i.Done) n++; return n; }

        public void SetDone(int line, bool done)
        {
            if (line < 0 || line >= Lines.Count) return;
            Match m = TaskRx.Match(Lines[line]);
            if (!m.Success) return;
            Group g = m.Groups[3];
            Lines[line] = Lines[line].Substring(0, g.Index) + (done ? "x" : " ") + Lines[line].Substring(g.Index + 1);
        }

        /// <summary>Change an item's text (its tick and due date stay).</summary>
        public void SetItemText(int line, string text)
        {
            Item it;
            if (line < 0 || line >= Lines.Count || !TryTask(Lines[line], out it)) return;
            Lines[line] = TaskLine(it.Indent, it.Done, Clean(text), it.Due);
        }

        public void SetDue(int line, DateTime? due)
        {
            Item it;
            if (line < 0 || line >= Lines.Count || !TryTask(Lines[line], out it)) return;
            Lines[line] = TaskLine(it.Indent, it.Done, it.Text, due);
        }

        static string TaskLine(int indent, bool done, string text, DateTime? due)
        {
            return new string(' ', indent * 2) + "- [" + (done ? "x" : " ") + "] " + text +
                   (due.HasValue ? " 📅 " + due.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "");
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        /// <summary>A new open item after the last item (or at the end).</summary>
        public void AddItem(string text)
        {
            text = Clean(text);
            if (text.Length == 0) return;
            int at = Lines.Count;
            List<Item> items = Items();
            if (items.Count > 0) at = items[items.Count - 1].Line + 1;
            Lines.Insert(at, TaskLine(0, false, text, null));
        }

        public void RemoveLine(int line) { if (line >= 0 && line < Lines.Count) Lines.RemoveAt(line); }

        /// <summary>Remove every ticked item.</summary>
        public int RemoveDone()
        {
            int n = 0;
            for (int i = Lines.Count - 1; i >= 0; i--)
            {
                Item it;
                if (TryTask(Lines[i], out it) && it.Done) { Lines.RemoveAt(i); n++; }
            }
            return n;
        }

        /// <summary>Move a line to before line 'before' (Lines.Count = to the end).</summary>
        public void MoveLine(int from, int before)
        {
            if (from < 0 || from >= Lines.Count) return;
            before = Math.Max(0, Math.Min(Lines.Count, before));
            if (before == from || before == from + 1) return;
            string s = Lines[from];
            Lines.RemoveAt(from);
            if (before > from) before--;
            Lines.Insert(before, s);
        }

        // ---- log ------------------------------------------------------------------------------------
        public sealed class Entry
        {
            public int Line;
            public DateTime When;
            public string Text = "";
        }

        public static bool TryEntry(string line, out Entry e)
        {
            e = null;
            Match m = LogRx.Match(line ?? "");
            if (!m.Success) return false;
            DateTime when;
            if (!DateTime.TryParseExact(m.Groups[1].Value, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                return false;
            e = new Entry { When = when, Text = m.Groups[2].Value.Trim() };
            return true;
        }

        public List<Entry> Entries()
        {
            var list = new List<Entry>();
            for (int i = 0; i < Lines.Count; i++)
            {
                Entry e;
                if (TryEntry(Lines[i], out e)) { e.Line = i; list.Add(e); }
            }
            return list;
        }

        /// <summary>A new entry at the top: before the first entry, or after the heading / blank lines the text starts with.</summary>
        public void AddEntry(string text, DateTime when)
        {
            text = Clean(text);
            if (text.Length == 0) return;
            string line = "- **" + when.ToString(LogStamp, CultureInfo.InvariantCulture) + "** " + text;
            List<Entry> es = Entries();
            int at;
            if (es.Count > 0) at = es[0].Line;
            else
            {
                at = 0;
                while (at < Lines.Count && (Lines[at].Trim().Length == 0 || Lines[at].TrimStart().StartsWith("#"))) at++;
            }
            Lines.Insert(at, line);
        }

        // ---- display helpers ------------------------------------------------------------------------
        /// <summary>"Today", "Tomorrow", "Mon", "19 Oct" (and the year when it is not this year).</summary>
        public static string DueText(DateTime due, DateTime now)
        {
            int days = (due.Date - now.Date).Days;
            if (days == 0) return "Today";
            if (days == 1) return "Tomorrow";
            if (days == -1) return "Yesterday";
            if (days > 1 && days < 7) return due.ToString("ddd", CultureInfo.CurrentCulture);
            return due.ToString(due.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
        }

        /// <summary>A log time: "14:52" today, "Yesterday", the weekday this week, otherwise the date.</summary>
        public static string WhenText(DateTime when, DateTime now)
        {
            int days = (now.Date - when.Date).Days;
            if (days == 0) return TimeText.Clock(when);
            if (days == 1) return "Yesterday";
            if (days > 1 && days < 7) return when.ToString("ddd", CultureInfo.CurrentCulture);
            return when.ToString(when.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
        }

        /// <summary>"just now", "5 min ago", "2 h ago", "yesterday", "3 days ago", or the date.</summary>
        public static string Ago(DateTime when, DateTime now)
        {
            return TimeText.AgoLocal(when);
        }
    }

    /// <summary>Reading and writing a note file: UTF-8 (keeping a BOM if it had one), keeping its line ends, shared with other editors.</summary>
    public static class NoteFile
    {
        public sealed class Content
        {
            public bool Exists;
            public string Text = "";
            public bool Bom;
            public bool Crlf = true;
            public DateTime Written;
        }

        public static Content Read(string path)
        {
            var c = new Content();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return c;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    byte[] bytes;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    c.Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                    string s = Encoding.UTF8.GetString(bytes, c.Bom ? 3 : 0, bytes.Length - (c.Bom ? 3 : 0));
                    c.Crlf = s.IndexOf("\r\n", StringComparison.Ordinal) >= 0 || s.IndexOf('\n') < 0;
                    c.Text = s.Replace("\r\n", "\n");
                    c.Exists = true;
                    c.Written = File.GetLastWriteTime(path);
                    return c;
                }
                catch (IOException) when (attempt < 4) { System.Threading.Thread.Sleep(60); }
            }
        }

        /// <summary>Write the text (lines joined with \n) in the file's own style. Creates the folder if needed.</summary>
        public static void Write(string path, string text, bool bom, bool crlf)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string s = (text ?? "").Replace("\r\n", "\n");
            if (crlf) s = s.Replace("\n", "\r\n");
            byte[] body = Encoding.UTF8.GetBytes(s);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        if (bom) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                        fs.Write(body, 0, body.Length);
                    }
                    return;
                }
                catch (IOException) when (attempt < 4) { System.Threading.Thread.Sleep(60); }
            }
        }

        /// <summary>"todo (conflict 2026-10-16 1452).md" next to the file.</summary>
        public static string ConflictPath(string path, DateTime now)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + " (conflict " + now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + ")" + Path.GetExtension(path));
        }
    }
}
