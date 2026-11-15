using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Colours and fonts a note is drawn with.</summary>
    [PluginApi]
    public sealed class NoteLook
    {
        public Brush Fg, Dim, Accent, Line, CodeBg;
        public FontFamily Font;
        public FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");
        public double Size = 12;
    }

    /// <summary>
    /// Draws a note's Markdown. full = the Markdown kind: headings, code (inline and blocks), links, quotes, numbered lists and rules
    /// as well; otherwise (the Note kind) only bold / italic / strike-through, bullets and task lines - other marks stay as typed.
    /// Task lines are tickable (toggle gets the line number). Line breaks are kept as typed (it is a note, not a document).
    /// </summary>
    public static class NoteRender
    {
        static readonly Regex Bullet = new Regex(@"^(\s*)[-*+]\s+(.*)$");
        static readonly Regex Number = new Regex(@"^(\s*)(\d{1,3})[.)]\s+(.*)$");
        static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$");

        public static StackPanel Render(IList<string> lines, bool full, NoteLook look, Action<int> toggle)
        {
            var root = new StackPanel();
            bool inCode = false;
            var code = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? "";
                string t = line.Trim();
                if (full && t.StartsWith("```", StringComparison.Ordinal))
                {
                    if (inCode) { root.Children.Add(CodeBlock(code.ToString(), look)); code.Clear(); }
                    inCode = !inCode;
                    continue;
                }
                if (inCode) { if (code.Length > 0) code.Append('\n'); code.Append(line); continue; }
                if (t.Length == 0) { root.Children.Add(new Border { Height = Math.Round(look.Size * 0.55) }); continue; }

                NoteText.Item task;
                if (NoteText.TryTask(line, out task)) { root.Children.Add(TaskRow(task, i, look, toggle, full)); continue; }

                Match m;
                if (full && (m = Heading.Match(t)).Success)
                {
                    int level = m.Groups[1].Value.Length;
                    double f = level == 1 ? 1.45 : level == 2 ? 1.25 : level == 3 ? 1.1 : 1.0;
                    var h = Para(m.Groups[2].Value, look, true);
                    h.FontSize = look.Size * f;
                    h.FontWeight = FontWeights.Bold;
                    h.Margin = new Thickness(0, root.Children.Count > 0 ? look.Size * 0.4 : 0, 0, 2);
                    if (level >= 4) h.Foreground = look.Dim;
                    root.Children.Add(h);
                    continue;
                }
                if (full && (t == "---" || t == "***" || t == "___"))
                {
                    root.Children.Add(new Border { Height = 1, Background = look.Line, Margin = new Thickness(0, 6, 0, 6) });
                    continue;
                }
                if (full && t.StartsWith(">", StringComparison.Ordinal))
                {
                    var q = Para(t.TrimStart('>').Trim(), look, true);
                    q.Foreground = look.Dim;
                    q.FontStyle = FontStyles.Italic;
                    root.Children.Add(new Border
                    {
                        BorderBrush = look.Line, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 1, 0, 1),
                        Margin = new Thickness(0, 1, 0, 1), Child = q
                    });
                    continue;
                }
                if ((m = Bullet.Match(line)).Success)
                {
                    root.Children.Add(ListRow("•", m.Groups[1].Value.Length, m.Groups[2].Value, look, full));
                    continue;
                }
                if (full && (m = Number.Match(line)).Success)
                {
                    root.Children.Add(ListRow(m.Groups[2].Value + ".", m.Groups[1].Value.Length, m.Groups[3].Value, look, true));
                    continue;
                }
                root.Children.Add(Para(line.TrimEnd(), look, full));
            }
            if (inCode && code.Length > 0) root.Children.Add(CodeBlock(code.ToString(), look));
            return root;
        }

        static TextBlock Para(string s, NoteLook look, bool full)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = look.Fg };
            AddInlines(tb.Inlines, s, look, full);
            return tb;
        }

        static FrameworkElement ListRow(string mark, int indent, string text, NoteLook look, bool full)
        {
            var g = new Grid { Margin = new Thickness(Math.Min(6, indent / 2) * 12, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Round(look.Size * 1.3)) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var b = new TextBlock { Text = mark, Foreground = look.Dim };
            var p = Para(text, look, full);
            Grid.SetColumn(p, 1);
            g.Children.Add(b);
            g.Children.Add(p);
            return g;
        }

        static FrameworkElement TaskRow(NoteText.Item task, int line, NoteLook look, Action<int> toggle, bool full)
        {
            var g = new Grid { Margin = new Thickness(Math.Min(6, task.Indent) * 14, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            FrameworkElement box = CheckBox(task.Done, look, toggle == null ? (Action)null : delegate { toggle(line); });
            box.Margin = new Thickness(0, 1, 8, 0);
            box.VerticalAlignment = VerticalAlignment.Top;
            var p = Para(task.Text, look, full);
            if (task.Done) { p.Foreground = look.Dim; p.TextDecorations = TextDecorations.Strikethrough; }
            Grid.SetColumn(p, 1);
            g.Children.Add(box);
            g.Children.Add(p);
            return g;
        }

        /// <summary>A round-cornered tick box; click = act (the widget does not start a drag from it).</summary>
        public static FrameworkElement CheckBox(bool done, NoteLook look, Action act)
        {
            double s = Math.Max(13, Math.Round(look.Size * 1.2));
            var b = new Border
            {
                Width = s, Height = s, CornerRadius = new CornerRadius(Math.Round(s * 0.28)),
                Background = done ? look.Accent : Brushes.Transparent,
                BorderBrush = done ? look.Accent : look.Dim, BorderThickness = new Thickness(done ? 0 : 1.6),
                Cursor = act != null ? Cursors.Hand : null
            };
            if (done)
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 2.5,6.5 L 5.2,9 L 10,3.5"), Stroke = Contrast(look.Accent), StrokeThickness = 1.9,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.Uniform, Margin = new Thickness(s * 0.2)
                };
                b.Child = path;
            }
            if (act != null)
                b.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; act(); };
            return b;
        }

        /// <summary>Dark or light, whichever reads on the colour.</summary>
        [PluginApi] public static Brush Contrast(Brush b)
        {
            var sc = b as SolidColorBrush;
            if (sc == null) return Brushes.White;
            Color c = sc.Color;
            double l = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return l > 0.6 ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)) : Brushes.White;
        }

        static FrameworkElement CodeBlock(string text, NoteLook look)
        {
            return new Border
            {
                Background = look.CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 3, 0, 3),
                Child = new TextBlock { Text = text, FontFamily = look.Mono, FontSize = look.Size * 0.92, Foreground = look.Fg, TextWrapping = TextWrapping.Wrap }
            };
        }

        /// <summary>**bold**, *italic* (and _italic_ in full), ~~strike~~; in full also `code` and [text](link).</summary>
        public static void AddInlines(InlineCollection into, string s, NoteLook look, bool full)
        {
            var sb = new StringBuilder();
            Action flush = delegate { if (sb.Length > 0) { into.Add(new Run(sb.ToString())); sb.Clear(); } };
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                int j;
                if (full && c == '`' && (j = s.IndexOf('`', i + 1)) > i + 1)
                {
                    flush();
                    into.Add(new Run(s.Substring(i + 1, j - i - 1)) { FontFamily = look.Mono, Background = look.CodeBg, FontSize = look.Size * 0.92 });
                    i = j + 1;
                    continue;
                }
                if ((c == '*' || c == '_' || c == '~') && i + 1 < s.Length && s[i + 1] == c && (c != '_' || full))
                {
                    string mark = new string(c, 2);
                    j = s.IndexOf(mark, i + 2, StringComparison.Ordinal);
                    if (j > i + 2)
                    {
                        flush();
                        Span span = c == '~' ? new Span() : (Span)new Bold();
                        if (c == '~') span.TextDecorations = TextDecorations.Strikethrough;
                        AddInlines(span.Inlines, s.Substring(i + 2, j - i - 2), look, full);
                        into.Add(span);
                        i = j + 2;
                        continue;
                    }
                }
                if ((c == '*' || (full && c == '_' && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))) && i + 1 < s.Length && s[i + 1] != ' ' && s[i + 1] != c)
                {
                    j = s.IndexOf(c, i + 1);
                    if (j > i + 1 && s[j - 1] != ' ')
                    {
                        flush();
                        var it = new Italic();
                        AddInlines(it.Inlines, s.Substring(i + 1, j - i - 1), look, full);
                        into.Add(it);
                        i = j + 1;
                        continue;
                    }
                }
                if (full && c == '[')
                {
                    int close = s.IndexOf("](", i + 1, StringComparison.Ordinal);
                    int end = close > 0 ? s.IndexOf(')', close + 2) : -1;
                    if (close > i && end > close)
                    {
                        flush();
                        string text = s.Substring(i + 1, close - i - 1), url = s.Substring(close + 2, end - close - 2).Trim();
                        var h = new Hyperlink(new Run(text)) { Foreground = look.Accent, Cursor = Cursors.Hand };
                        h.ToolTip = url;
                        h.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; Open(url); };
                        into.Add(h);
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            flush();
        }

        [PluginApi] public static void Open(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Note link " + target + ": " + ex.Message); }
        }
    }
}
