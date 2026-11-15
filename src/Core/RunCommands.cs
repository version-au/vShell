using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace vShell.Core
{
    /// <summary>A run box !bang: "!g dog pictures" opens Url with {q} replaced by the search.</summary>
    public sealed class RunBang
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";

        public static List<RunBang> Defaults()
        {
            return new List<RunBang>
            {
                new RunBang { Key = "g", Name = "Google", Url = "https://www.google.com/search?q={q}" },
                new RunBang { Key = "i", Name = "Google Images", Url = "https://www.google.com/search?tbm=isch&q={q}" },
                new RunBang { Key = "yt", Name = "YouTube", Url = "https://www.youtube.com/results?search_query={q}" },
                new RunBang { Key = "w", Name = "Wikipedia", Url = "https://en.wikipedia.org/wiki/Special:Search?search={q}" },
                new RunBang { Key = "gh", Name = "GitHub", Url = "https://github.com/search?q={q}" },
                new RunBang { Key = "m", Name = "Google Maps", Url = "https://www.google.com/maps/search/{q}" },
                new RunBang { Key = "ddg", Name = "DuckDuckGo", Url = "https://duckduckgo.com/?q={q}" }
            };
        }

        /// <summary>The list as text, one per line: "g | Google | https://www.google.com/search?q={q}".</summary>
        public static string ToText(List<RunBang> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (RunBang b in list) sb.Append(b.Key).Append(" | ").Append(b.Name).Append(" | ").Append(b.Url).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>Read the text form back ("key | name | url" or "key | url"); bad lines are skipped.</summary>
        public static List<RunBang> FromText(string text)
        {
            var list = new List<RunBang>();
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split('|');
                if (p.Length < 2) continue;
                string key = p[0].Trim().TrimStart('!').ToLowerInvariant(), url = p[p.Length - 1].Trim(), name = p.Length >= 3 ? p[1].Trim() : key;
                if (key.Length == 0 || key.Contains(" ") || url.Length == 0) continue;
                list.Add(new RunBang { Key = key, Name = name.Length > 0 ? name : key, Url = url });
            }
            return list;
        }
    }

    /// <summary>What a line typed in the run box does.</summary>
    [PluginApi]
    public sealed class RunAction
    {
        /// <summary>"Palette" (/words: the command palette's commands for Query), "Url" (open Target in the browser), "Command" (Target in a terminal),
        /// "Calc" (Display is the answer), "Open" (a path, folder, URI or program: ShellExecute Target), "Run" (a single word: try it as a
        /// program, else search the web), "None".</summary>
        public string Kind = "None";
        public string Target = "";
        public string Query = "";
        /// <summary>What the suggestion line says ("Search Google for dog pictures").</summary>
        public string Display = "";
        /// <summary>Run: the web search to fall back on when it is not a program (empty = none).</summary>
        public string Fallback = "";
    }

    public static class RunParser
    {
        static readonly Regex Domain = new Regex(@"^(?:[a-z0-9-]+\.)+[a-z]{2,}(?::\d+)?(?:/\S*)?$", RegexOptions.IgnoreCase);
        static readonly Regex Scheme = new Regex(@"^[a-z][a-z0-9+.-]{1,30}:\S*$", RegexOptions.IgnoreCase);

        /// <summary>Plugins' !keywords: (keyword typed, the rest, settings) -> an action of the plugin's own kind, or null when it isn't theirs.</summary>
        public static readonly List<Func<string, string, BarSettings, RunAction>> BangParsers = new List<Func<string, string, BarSettings, RunAction>>();
        /// <summary>Plugins' own text (no spaces) that isn't a !keyword - a magnet link, a .torrent address: -> their action, or null.</summary>
        public static readonly List<Func<string, BarSettings, RunAction>> TextParsers = new List<Func<string, BarSettings, RunAction>>();

        public static string EngineUrl(string engine, string q, string custom = null)
        {
            string e = Uri.EscapeDataString(q ?? "");
            switch (engine)
            {
                case "Custom":
                    {
                        string c = (custom ?? "").Trim();
                        if (c.Length == 0) return "https://www.google.com/search?q=" + e;
                        if (!c.Contains("://")) c = "https://" + c;
                        return c.Contains("{q}") ? c.Replace("{q}", e) : c + e;
                    }
                case "DuckDuckGo": return "https://duckduckgo.com/?q=" + e;
                case "Bing": return "https://www.bing.com/search?q=" + e;
                case "None": return "";
                default: return "https://www.google.com/search?q=" + e;
            }
        }

        /// <summary>The name a plain search is shown with: "Google", or the custom engine's host ("searx.example.com").</summary>
        public static string EngineName(BarSettings s)
        {
            if (s.RunEngine != "Custom") return s.RunEngine;
            string c = (s.RunCustomEngine ?? "").Trim();
            if (c.Length == 0) return "Google";
            try { return new Uri((c.Contains("://") ? c : "https://" + c).Replace("{q}", "")).Host; }
            catch { return "the web"; }
        }

        /// <summary>The site of a bang's URL (for a bang with nothing after it: "!yt" opens YouTube).</summary>
        static string Site(string url)
        {
            try { var u = new Uri(url.Replace("{q}", "")); return u.Scheme + "://" + u.Host + "/"; }
            catch { return url.Replace("{q}", ""); }
        }

        [PluginApi] public static RunAction Parse(string text, BarSettings s)
        {
            var a = new RunAction();
            string t = (text ?? "").Trim();
            if (t.Length == 0) return a;

            // /words: only the command palette's commands
            if (t.StartsWith("/"))
            {
                a.Kind = "Palette"; a.Query = t.Substring(1).Trim();
                a.Display = "Commands";
                return a;
            }

            // !bang query
            if (t.StartsWith("!") && t.Length > 1)
            {
                int sp = t.IndexOf(' ');
                string key = (sp < 0 ? t.Substring(1) : t.Substring(1, sp - 1)).ToLowerInvariant();
                string q = sp < 0 ? "" : t.Substring(sp + 1).Trim();
                foreach (Func<string, string, BarSettings, RunAction> plugin in BangParsers.ToArray())
                {
                    RunAction pa = plugin(key, q, s);
                    if (pa != null) return pa;
                }
                foreach (RunBang b in s.RunBangs)
                    if (string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        a.Kind = "Url"; a.Query = q;
                        a.Target = q.Length == 0 ? Site(b.Url) : b.Url.Replace("{q}", Uri.EscapeDataString(q));
                        a.Display = q.Length == 0 ? "Open " + b.Name : "Search " + b.Name + " for " + q;
                        return a;
                    }
                if (s.RunUnknownToDdg)
                {
                    a.Kind = "Url"; a.Query = q;
                    a.Target = "https://duckduckgo.com/?q=" + Uri.EscapeDataString("!" + key + (q.Length > 0 ? " " + q : ""));
                    a.Display = "DuckDuckGo !" + key + (q.Length > 0 ? ": " + q : "");
                    return a;
                }
            }

            // text a plugin knows (a magnet link / .torrent address -> qBittorrent, an .nzb address -> SABnzbd)
            if (!t.Contains(" "))
                foreach (Func<string, BarSettings, RunAction> plugin in TextParsers.ToArray())
                {
                    RunAction pa = plugin(t, s);
                    if (pa != null) return pa;
                }

            // >command: in a terminal
            if (t.StartsWith(">"))
            {
                string cmd = t.Substring(1).Trim();
                if (cmd.Length == 0) return a;
                a.Kind = "Command"; a.Target = cmd; a.Display = "Run in a terminal: " + cmd;
                return a;
            }

            // =sum
            if (t.StartsWith("="))
            {
                double v;
                string err;
                if (Calc.TryEval(t.Substring(1), out v, out err))
                {
                    a.Kind = "Calc"; a.Target = Calc.Format(v); a.Display = "= " + a.Target + "   (Enter copies it)";
                }
                else { a.Kind = "None"; a.Display = err; }
                return a;
            }

            // a URL / web address
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                a.Kind = "Url"; a.Target = t; a.Display = "Open " + t;
                return a;
            }
            if (!t.Contains(" ") && !t.Contains("\\") && Domain.IsMatch(t) && !LooksLikeFile(t))
            {
                a.Kind = "Url"; a.Target = "https://" + t; a.Display = "Open " + t;
                return a;
            }

            // a path, folder, environment variable or URI (ms-settings:, shell:, mailto: ...)
            string expanded = Environment.ExpandEnvironmentVariables(t);
            if (expanded.StartsWith("~")) expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + expanded.Substring(1);
            bool path = expanded.Length >= 2 && ((char.IsLetter(expanded[0]) && expanded[1] == ':') || expanded.StartsWith("\\\\"));
            if (path || t.StartsWith("%") || t.StartsWith("~") || (Scheme.IsMatch(t) && !t.Contains(" ")))
            {
                a.Kind = "Open"; a.Target = expanded; a.Display = "Open " + t;
                return a;
            }

            // one word: a program (notepad, cmd, regedit, an App Paths name); not found -> a web search
            string web = EngineUrl(s.RunEngine, t, s.RunCustomEngine);
            if (!t.Contains(" "))
            {
                a.Kind = "Run"; a.Target = t; a.Fallback = web; a.Display = "Run " + t;
                return a;
            }
            if (web.Length > 0)
            {
                a.Kind = "Url"; a.Target = web; a.Query = t; a.Display = "Search " + EngineName(s) + " for " + t;
                return a;
            }
            a.Kind = "Open"; a.Target = t; a.Display = "Run " + t;
            return a;
        }

        static bool LooksLikeFile(string t)
        {
            string ext = System.IO.Path.GetExtension(t).ToLowerInvariant();
            return ext == ".exe" || ext == ".msc" || ext == ".cpl" || ext == ".bat" || ext == ".cmd" || ext == ".lnk" || ext == ".txt" || ext == ".ps1";
        }
    }

    /// <summary>A small calculator: + - * / % ^, brackets, decimals, pi, e, sqrt / sin / cos / tan / log / ln / abs / round.</summary>
    public static class Calc
    {
        public static string Format(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return v.ToString(CultureInfo.CurrentCulture);
            if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-9 && v != 0)) return v.ToString("G10", CultureInfo.CurrentCulture);
            return Math.Round(v, 10).ToString("#,0.##########", CultureInfo.CurrentCulture);
        }

        public static bool TryEval(string expr, out double value, out string error)
        {
            value = 0; error = null;
            try
            {
                var p = new P((expr ?? "").Replace(" ", "").Replace(",", "").Replace("×", "*").Replace("÷", "/").ToLowerInvariant());
                if (p.S.Length == 0) { error = "Type a sum after =, e.g. =12*7.5"; return false; }
                value = p.Expr();
                if (p.I != p.S.Length) throw new FormatException("unexpected '" + p.S[p.I] + "'");
                return true;
            }
            catch (Exception ex) { error = "Not a sum: " + ex.Message; return false; }
        }

        sealed class P
        {
            public readonly string S;
            public int I;
            public P(string s) { S = s; }
            char C { get { return I < S.Length ? S[I] : '\0'; } }

            public double Expr()
            {
                double v = Term();
                while (C == '+' || C == '-') { char op = S[I++]; double r = Term(); v = op == '+' ? v + r : v - r; }
                return v;
            }

            double Term()
            {
                double v = Power();
                while (C == '*' || C == '/' || C == '%')
                {
                    char op = S[I++];
                    double r = Power();
                    v = op == '*' ? v * r : op == '/' ? v / r : v % r;
                }
                return v;
            }

            double Power()
            {
                double b = Unary();
                if (C == '^') { I++; return Math.Pow(b, Power()); }      // right-associative
                return b;
            }

            double Unary()
            {
                if (C == '-') { I++; return -Unary(); }
                if (C == '+') { I++; return Unary(); }
                return Atom();
            }

            double Atom()
            {
                if (C == '(')
                {
                    I++;
                    double v = Expr();
                    if (C != ')') throw new FormatException("missing )");
                    I++;
                    return v;
                }
                if (char.IsDigit(C) || C == '.')
                {
                    int st = I;
                    while (char.IsDigit(C) || C == '.') I++;
                    if (C == 'e' && I + 1 < S.Length && (char.IsDigit(S[I + 1]) || S[I + 1] == '-' || S[I + 1] == '+'))
                    {
                        I++;
                        if (C == '-' || C == '+') I++;
                        while (char.IsDigit(C)) I++;
                    }
                    return double.Parse(S.Substring(st, I - st), CultureInfo.InvariantCulture);
                }
                if (char.IsLetter(C))
                {
                    int st = I;
                    while (char.IsLetter(C)) I++;
                    string name = S.Substring(st, I - st);
                    if (name == "pi") return Math.PI;
                    if (name == "e") return Math.E;
                    double arg = Atom();
                    switch (name)
                    {
                        case "sqrt": return Math.Sqrt(arg);
                        case "sin": return Math.Sin(arg);
                        case "cos": return Math.Cos(arg);
                        case "tan": return Math.Tan(arg);
                        case "log": return Math.Log10(arg);
                        case "ln": return Math.Log(arg);
                        case "abs": return Math.Abs(arg);
                        case "round": return Math.Round(arg);
                    }
                    throw new FormatException("unknown '" + name + "'");
                }
                throw new FormatException(I < S.Length ? "unexpected '" + S[I] + "'" : "it ends too soon");
            }
        }
    }
}
