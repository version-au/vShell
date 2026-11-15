using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace vShell.Core
{
    /// <summary>
    /// One thing the command palette can do ("Restart stack jellyfin", "Set Living room to 22°", "Pause Voron"), found in the run box by
    /// typing words from its name. Sources (an integration, the terminal, vShell itself) list them from what they last read.
    /// </summary>
    [PluginApi]
    public sealed class PaletteCommand
    {
        /// <summary>What the row says; "{n}" is the number typed (commands that take one), "…" until there is one.</summary>
        public string Title = "";
        /// <summary>The grey text on the right ("Komodo · running").</summary>
        public string Detail = "";
        public string Glyph = "";
        /// <summary>More words it's found by (an entity id, the server, "reboot" for Restart ...).</summary>
        public string Words = "";
        /// <summary>The integration it belongs to ("Home Assistant") - also a word it's found by.</summary>
        public string Source = "";
        /// <summary>Asks first: Enter once arms it, Enter again does it (stop, shut down, cancel a print ...).</summary>
        public bool Ask;
        /// <summary>Takes a number from the text ("a temperature", "a brightness %"); null = takes none.</summary>
        public string Number;
        public double Min = double.MinValue, Max = double.MaxValue;
        /// <summary>Opens something (a window, a terminal, the lock screen): the run box closes first, then it runs.</summary>
        public bool Opens;
        /// <summary>A small nudge up the list (the toggle that changes something before the one that's already so).</summary>
        public int Boost;
        /// <summary>
        /// Does it: the number typed (NaN when it takes none) -> null / "" when done, Worked("…") when done with something to say, else a
        /// one-line error.
        /// </summary>
        public Func<double, Task<string>> Run;

        // marks a Worked(...) result (an error never starts with it)
        const string WorkedMark = "\u0006";

        /// <summary>For Run to return when it worked and has something to say ("Restarting jellyfin (job 413)"): the run box shows it.</summary>
        public static string Worked(string message) { return WorkedMark + (message ?? ""); }

        /// <summary>Whether a result from Run is a Worked(...) one, and what it says.</summary>
        public static bool IsWorked(string result, out string message)
        {
            message = null;
            if (result == null || !result.StartsWith(WorkedMark, StringComparison.Ordinal)) return false;
            message = result.Substring(WorkedMark.Length);
            return true;
        }

        public string Key { get { return Source + "|" + Title; } }

        // the words of the title and of the rest, split once (Find runs on every key press)
        internal List<string> TitleWords, MoreWords;

        /// <summary>The title with the number filled in.</summary>
        public string TitleWith(double n)
        {
            if (Number == null) return Title;
            return Title.Replace("{n}", double.IsNaN(n) ? "…" : n.ToString("0.#", CultureInfo.CurrentCulture));
        }
    }

    /// <summary>A command found for some text, with the number taken from it (NaN = none).</summary>
    public sealed class PaletteHit
    {
        public PaletteCommand Command;
        public double Number = double.NaN;
        public int Score;
        /// <summary>Every word typed was found (not only some of them).</summary>
        public bool Full;
        public string Title { get { return Command.TitleWith(Number); } }
    }

    /// <summary>
    /// The command palette behind the run box: every action vShell can take, from every integration that's set up. Type a few words
    /// ("restart jellyfin", "ac 22", "pause printer", "ssh pve1") in the run box, or start with / to see only commands.
    /// </summary>
    public static class Palette
    {
        /// <summary>Adds its commands to the list (called on the UI thread; it should only read what its client already has).</summary>
        [PluginApi]
        public delegate void Source(BarSettings s, List<PaletteCommand> into);

        static readonly List<KeyValuePair<string, Source>> Sources = new List<KeyValuePair<string, Source>>();
        static List<PaletteCommand> _cache;
        static DateTime _cacheAt = DateTime.MinValue;

        /// <summary>Something new to list arrived (Home Assistant's entities): an open run box shows it.</summary>
        public static event Action Changed;

        /// <summary>Add a source (the name is only for the list of what's there); a second one with the same name replaces it.</summary>
        public static void Register(string name, Source src)
        {
            lock (Sources)
            {
                Sources.RemoveAll(delegate (KeyValuePair<string, Source> kv) { return kv.Key == name; });
                Sources.Add(new KeyValuePair<string, Source>(name, src));
            }
            _cache = null;
        }

        public static void Unregister(string name)
        {
            lock (Sources) Sources.RemoveAll(delegate (KeyValuePair<string, Source> kv) { return kv.Key == name; });
            _cache = null;
        }

        [PluginApi] public static void RaiseChanged()
        {
            _cache = null;
            var h = Changed;
            if (h != null) h();
        }

        /// <summary>The run box opened: the sources start reading what they need (Home Assistant's entity list) - plugins listen.</summary>
        public static event Action<BarSettings> Warming;

        public static void Warm(BarSettings s)
        {
            Action<BarSettings> h = Warming;
            if (h != null) try { h(s); } catch (Exception ex) { Log.Write("Palette warm: " + ex.Message); }
        }

        /// <summary>Every command there is now (made again at most every 2 seconds).</summary>
        public static List<PaletteCommand> All(BarSettings s)
        {
            if (_cache != null && (DateTime.UtcNow - _cacheAt).TotalSeconds < 2) return _cache;
            var list = new List<PaletteCommand>();
            List<KeyValuePair<string, Source>> src;
            lock (Sources) src = new List<KeyValuePair<string, Source>>(Sources);
            foreach (var kv in src)
            {
                try { kv.Value(s, list); }
                catch (Exception ex) { Log.Write("Palette " + kv.Key + ": " + ex.Message); }
            }
            _cache = list;
            _cacheAt = DateTime.UtcNow;
            return list;
        }

        /// <summary>How many commands each source has now ("Home Assistant 214 · Komodo 40").</summary>
        public static string Summary(BarSettings s)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (PaletteCommand c in All(s))
            {
                if (!counts.ContainsKey(c.Source)) { counts[c.Source] = 0; order.Add(c.Source); }
                counts[c.Source]++;
            }
            var parts = new List<string>();
            foreach (string k in order) parts.Add(k + " " + counts[k]);
            return string.Join(" · ", parts);
        }

        // words that don't need to be found ("set the AC to 22")
        static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "to", "my", "please", "at", "of", "in", "for", "and", "now"
        };

        static readonly Regex NumberWord = new Regex(@"^(\d+(?:[.,]\d+)?)(?:°[cf]?|c|f|%|percent)?$", RegexOptions.IgnoreCase);

        static List<string> WordsOf(string text)
        {
            var l = new List<string>();
            foreach (string w in Regex.Split((text ?? "").ToLowerInvariant(), @"[\s·:,()\[\]/\\|""'-]+"))
                if (w.Length > 0) l.Add(w);
            return l;
        }

        /// <summary>The commands that match the text, best first (at most max).</summary>
        public static List<PaletteHit> Find(string text, BarSettings s, int max)
        {
            var hits = new List<PaletteHit>();
            List<string> typed = WordsOf(text);
            if (typed.Count == 0) return hits;
            // a number in the text ("22", "22.5°", "50%") - for the commands that take one
            int numAt = -1;
            double num = double.NaN;
            for (int i = typed.Count - 1; i >= 0; i--)
            {
                Match m = NumberWord.Match(typed[i]);
                if (!m.Success) continue;
                double v;
                if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { numAt = i; num = v; }
                break;
            }

            foreach (PaletteCommand c in All(s))
            {
                if (c.TitleWords == null) { c.TitleWords = WordsOf(c.Title.Replace("{n}", "")); c.MoreWords = WordsOf(c.Words + " " + c.Source); }
                List<string> title = c.TitleWords, more = c.MoreWords;
                int score = 0, found = 0, needed = 0;
                bool all = true;
                for (int i = 0; i < typed.Count; i++)
                {
                    if (i == numAt && c.Number != null) continue;
                    string w = typed[i];
                    bool filler = Filler.Contains(w);
                    if (!filler) needed++;
                    int sc = WordScore(w, title, 3);
                    if (sc == 0) sc = WordScore(w, more, 2);
                    if (sc == 0) { if (!filler) { all = false; break; } continue; }
                    found++;
                    score += sc;
                }
                if (!all || needed == 0 || found == 0) continue;
                // the first word starting the title ("restart ..." for "Restart stack ...")
                if (title.Count > 0 && title[0].StartsWith(typed[0], StringComparison.Ordinal)) score += 2;
                var h = new PaletteHit { Command = c, Score = score * 10 + c.Boost, Full = true };
                if (c.Number != null && numAt >= 0) { h.Number = num; h.Score += 5; }
                hits.Add(h);
            }
            hits.Sort(delegate (PaletteHit x, PaletteHit y)
            {
                int c = y.Score.CompareTo(x.Score);
                return c != 0 ? c : string.Compare(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);
            });
            if (hits.Count > max) hits.RemoveRange(max, hits.Count - max);
            return hits;
        }

        /// <summary>How well a typed word is found among words: the start of one (best), or inside one (3+ letters).</summary>
        static int WordScore(string w, List<string> words, int weight)
        {
            int best = 0;
            foreach (string x in words)
            {
                if (x == w) return weight * 3;
                if (x.StartsWith(w, StringComparison.Ordinal)) best = Math.Max(best, weight * 2);
                else if (w.Length >= 3 && x.Contains(w)) best = Math.Max(best, weight);
            }
            return best;
        }

        /// <summary>Why it can't run as typed (no number, or one out of range), or null.</summary>
        public static string Problem(PaletteHit h)
        {
            PaletteCommand c = h.Command;
            if (c.Number == null) return null;
            if (double.IsNaN(h.Number)) return "Type " + c.Number + " as well.";
            if (h.Number < c.Min || h.Number > c.Max)
                return "Type " + c.Number + " from " + c.Min.ToString("0.#", CultureInfo.CurrentCulture) + " to " + c.Max.ToString("0.#", CultureInfo.CurrentCulture) + ".";
            return null;
        }

        /// <summary>Run a command: checks the number first. Returns null when done, a PaletteCommand.Worked(...) result as it came, else why not.</summary>
        public static async Task<string> RunAsync(PaletteHit h)
        {
            PaletteCommand c = h.Command;
            string why = Problem(h);
            if (why != null) return why;
            try
            {
                string err = await c.Run(h.Number);
                string said;
                if (string.IsNullOrEmpty(err)) { _cache = null; return null; }
                if (PaletteCommand.IsWorked(err, out said)) _cache = null;
                return err;
            }
            catch (Exception ex)
            {
                Log.Write("Palette '" + c.Title + "': " + ex.Message);
                return ex.Message;
            }
        }
    }
}
