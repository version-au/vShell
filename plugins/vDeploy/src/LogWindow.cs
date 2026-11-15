using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace VDeploy
{
    /// <summary>
    /// The end of a vDeploy stack's or container's log, in a window of its own in the vShell theme (as the Komodo plugin's). Read-only.
    /// Last 50 / 100 / 500 / 1000 lines, Follow (asks again every 3 s and keeps to the bottom), a filter, times on / off, Copy; lines with
    /// error / fatal / panic are red, warnings yellow. A stack shows every service or one. One window per stack / container: opening it
    /// again brings it to the front.
    /// </summary>
    public sealed class LogWindow : Window
    {
        static readonly Dictionary<string, LogWindow> Open = new Dictionary<string, LogWindow>();
        static readonly int[] Tails = { 50, 100, 500, 1000 };
        // Docker's time at the start of a line (docker logs -t), or after "service-1  | " (compose logs -t)
        static readonly Regex Stamp = new Regex(@"^((?:[^|\r\n]{1,80}\|\s)?)(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z)\s", RegexOptions.Compiled);

        readonly Theme _t;
        readonly VdStack _stack;
        string _service;              // one container's service (or name), or null = the whole stack
        int _tail = 100;
        bool _follow, _stamps, _loading;
        string _text = "", _error = "";
        readonly WrapPanel _bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 8) };
        readonly TextBox _filter = new TextBox();
        readonly RichTextBox _view = new RichTextBox();
        readonly TextBlock _status = new TextBlock();
        readonly DispatcherTimer _timer;

        /// <summary>Open (or bring forward) the log of a stack (box = null: every service) or one of its containers.</summary>
        public static void Show(Theme t, VdStack s, VdContainer box)
        {
            if (s == null) return;
            string key = s.Key + "|" + (box != null ? box.Label : "");
            if (Open.TryGetValue(key, out LogWindow w) && w.IsLoaded)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
                return;
            }
            w = new LogWindow(t, s, box != null ? box.Label : null);
            Open[key] = w;
            w.Closed += delegate { Open.Remove(key); };
            w.Show();
            w.Activate();
        }

        LogWindow(Theme t, VdStack s, string service)
        {
            _t = t; _stack = s; _service = service;
            Title = "Log - " + (service ?? s.Title);
            Width = 900; Height = 580;
            MinWidth = 420; MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = true;
            Color bg = t.PopupBg.Color;
            Background = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));      // a normal window: no see-through
            BorderBrush = Theme.Faded(t.MainAccent, 45);
            BorderThickness = new Thickness(1);
            FontFamily = t.Font;
            WidgetWindow.StyleScrollbars(this);
            Resources["wThumb"] = Theme.Faded(t.FgDim, 70);

            var root = new DockPanel();
            var title = new Grid { Background = Brushes.Transparent, Margin = new Thickness(14, 10, 8, 8) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = Plugin.Glyph, FontFamily = IconUtil.GlyphFont, FontSize = t.FontSize * 1.1, Foreground = t.MainAccent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _name = new TextBlock { Foreground = t.Fg, FontSize = t.FontSize * 1.08, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(_name);
            title.Children.Add(name);
            var close = Pills.Make(t, "\uE711", "", "Close (Esc)", false, delegate { Close(); });
            Grid.SetColumn(close, 1);
            title.Children.Add(close);
            title.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else { try { DragMove(); } catch { } } };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);
            DockPanel.SetDock(_bar, Dock.Top);
            root.Children.Add(_bar);

            _status.Foreground = t.FgDim;
            _status.FontSize = t.FontSize * 0.88;
            _status.Margin = new Thickness(14, 4, 14, 8);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            _view.IsReadOnly = true;
            _view.IsDocumentEnabled = false;
            _view.Background = Theme.Faded(t.Fg, 6);
            _view.Foreground = t.Fg;
            _view.BorderThickness = new Thickness(0);
            _view.Padding = new Thickness(6);
            _view.FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
            _view.FontSize = Math.Max(10, t.FontSize * 0.92);
            _view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _view.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _view.CaretBrush = t.Fg;
            _view.SelectionBrush = t.MainAccent;
            _view.Document.PageWidth = 6000;            // long lines scroll sideways instead of wrapping
            _view.Document.PagePadding = new Thickness(0);
            root.Children.Add(new Border { Child = _view, Margin = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(6), ClipToBounds = true });
            Content = root;

            _filter.Width = 200;
            _filter.Background = t.Track;
            _filter.Foreground = t.Fg;
            _filter.CaretBrush = t.Fg;
            _filter.BorderThickness = new Thickness(0);
            _filter.Padding = new Thickness(8, 3, 8, 3);
            _filter.VerticalContentAlignment = VerticalAlignment.Center;
            _filter.ToolTip = "Only lines containing this";
            _filter.Margin = new Thickness(4, 0, 0, 0);
            _filter.TextChanged += delegate { Render(); };

            PreviewKeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if (e.Key == Key.F5) { e.Handled = true; Load(); }
                else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; _filter.Focus(); _filter.SelectAll(); }
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += delegate { if (_follow) Load(); };
            Loaded += delegate { _timer.Start(); };
            Closed += delegate { _timer.Stop(); };

            Named();
            BuildBar();
            Load();
        }

        readonly TextBlock _name;

        void Named()
        {
            _name.Text = (_service != null ? _service + "  ·  " + _stack.Title : _stack.Title + "  ·  every service") + "  ·  on " + _stack.Host;
            Title = "Log - " + (_service ?? _stack.Title);
        }

        /// <summary>The stack as vDeploy shows it now (its containers change as it's redeployed).</summary>
        VdStack Now { get { return Plugin.Latest.Stacks.FirstOrDefault(x => x.Key == _stack.Key) ?? _stack; } }

        /// <summary>The toolbar: which container (or every service), how many lines, follow, times, the filter, refresh, copy.</summary>
        void BuildBar()
        {
            _bar.Children.Clear();
            Border svc = null;
            svc = Pills.Make(_t, "\uE8FD", _service ?? "Every service", "Which container", false, delegate
            {
                var m = new ContextMenu();
                var all = new MenuItem { Header = "Every service", IsCheckable = true, IsChecked = _service == null };
                all.Click += delegate { _service = null; Named(); BuildBar(); Load(); };
                m.Items.Add(all);
                foreach (VdContainer c in Now.Containers)
                {
                    string n = c.Label;
                    var mi = new MenuItem { Header = n + "   (" + c.StateText + ")", IsCheckable = true, IsChecked = _service == n };
                    mi.Click += delegate { _service = n; Named(); BuildBar(); Load(); };
                    m.Items.Add(mi);
                }
                m.PlacementTarget = svc;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                m.IsOpen = true;
            });
            _bar.Children.Add(svc);
            Border tail = null;
            tail = Pills.Make(_t, "\uE8A4", "Last " + _tail + " lines", "How many lines", false, delegate
            {
                var m = new ContextMenu();
                foreach (int n in Tails)
                {
                    int nn = n;
                    var mi = new MenuItem { Header = n + " lines", IsCheckable = true, IsChecked = _tail == n };
                    mi.Click += delegate { _tail = nn; BuildBar(); Load(); };
                    m.Items.Add(mi);
                }
                m.PlacementTarget = tail;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                m.IsOpen = true;
            });
            _bar.Children.Add(tail);
            _bar.Children.Add(Pills.Make(_t, "\uE8AB", "Follow", "Ask again every 3 seconds and keep to the newest lines", false, delegate { _follow = !_follow; BuildBar(); if (_follow) Load(); }, _follow));
            _bar.Children.Add(Pills.Make(_t, "\uE823", "Times", "Show Docker's time for each line", false, delegate { _stamps = !_stamps; BuildBar(); Render(); }, _stamps));
            if (_filter.Parent is Panel p) p.Children.Remove(_filter);
            _bar.Children.Add(_filter);
            _bar.Children.Add(Pills.Make(_t, "\uE72C", "Refresh", "Ask vDeploy again (F5)", false, delegate { Load(); }));
            _bar.Children.Add(Pills.Make(_t, "\uE8C8", "Copy", "Copy the lines shown (or the selection)", false, delegate { Copy(); }));
            _bar.Children.Add(Pills.Make(_t, "\uE8A7", "Open in vDeploy", "Its page in vDeploy, with Live logs", false, delegate
            {
                VdStack s = Now;
                VdContainer c = _service != null ? s.Containers.FirstOrDefault(x => x.Label == _service) : null;
                Plugin.Open(c != null ? Plugin.ContainerUrl(s, c) : Plugin.PageUrl(s, "logs"));
            }));
            foreach (UIElement e in _bar.Children) if (e is FrameworkElement fe) fe.Margin = new Thickness(fe == _filter ? 4 : 0, 0, 4, 4);
        }

        async void Load()
        {
            if (_loading || Plugin.Settings == null) return;
            _loading = true;
            _status.Text = "Asking vDeploy…";
            try
            {
                VdStack s = Now;
                VdContainer c = _service != null ? s.Containers.FirstOrDefault(x => x.Label == _service) ?? new VdContainer { Service = _service } : null;
                _text = await VdClient.LogAsync(VdSettings.Of(Plugin.Settings), s, c, _tail, CancellationToken.None);
                _error = "";
            }
            catch (Exception ex) { _error = VdClient.Plain(ex); }
            finally { _loading = false; }
            Render();
        }

        static readonly SolidColorBrush Red = Fz(0xEF, 0x4D, 0x4D), Yellow = Fz(0xF5, 0xC5, 0x42);
        static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        /// <summary>A line with Docker's time shown in the viewer's clock, or taken out.</summary>
        string Line(string raw)
        {
            Match m = Stamp.Match(raw);
            if (!m.Success) return raw;
            string rest = raw.Substring(m.Length);
            if (!_stamps) return m.Groups[1].Value + rest;
            string t = m.Groups[2].Value;
            int dot = t.IndexOf('.');
            if (dot > 0 && t.Length - dot - 2 > 7) t = t.Substring(0, dot + 8) + "Z";   // Docker gives 9 digits, .NET reads 7
            if (DateTimeOffset.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset at))
                return m.Groups[1].Value + at.ToLocalTime().ToString(TimeText.Use24 ? "HH:mm:ss" : "h:mm:ss tt") + "  " + rest;
            return raw;
        }

        /// <summary>The lines (filtered, coloured) into the view; keeps to the bottom when following or already at the bottom.</summary>
        void Render()
        {
            bool atEnd = _follow || _view.VerticalOffset >= _view.ExtentHeight - _view.ViewportHeight - 4;
            string f = _filter.Text.Trim();
            string[] lines = (_text ?? "").Replace("\r\n", "\n").Split('\n');
            var para = new Paragraph { Margin = new Thickness(0) };
            int shown = 0, total = 0;
            foreach (string raw0 in lines)
            {
                if (raw0.Length == 0 && total == lines.Length - 1) break;
                total++;
                string raw = Line(raw0);
                if (f.Length > 0 && raw.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string l = raw.Length > 4000 ? raw.Substring(0, 4000) + " …" : raw;
                var run = new Run(l);
                string low = l.ToLowerInvariant();
                if (low.Contains("error") || low.Contains("fatal") || low.Contains("panic") || low.Contains("exception") || low.Contains("traceback")) run.Foreground = Red;
                else if (low.Contains("warn")) run.Foreground = Yellow;
                if (shown > 0) para.Inlines.Add(new LineBreak());
                para.Inlines.Add(run);
                shown++;
            }
            _view.Document.Blocks.Clear();
            _view.Document.Blocks.Add(para);
            if (atEnd) _view.ScrollToEnd();
            string when = DateTime.Now.ToString(TimeText.Use24 ? "HH:mm:ss" : "h:mm:ss tt");
            _status.Text = _error.Length > 0 ? "vDeploy: " + _error
                         : (f.Length > 0 ? shown + " of " + total + " lines match" : total + " lines") + " · checked " + when + (_follow ? " · following" : "") +
                           "   ·   Ctrl+F filter · F5 refresh · Esc close";
            _status.Foreground = _error.Length > 0 ? (Brush)Red : _t.FgDim;
        }

        void Copy()
        {
            string sel = _view.Selection != null ? _view.Selection.Text : "";
            if (string.IsNullOrEmpty(sel)) sel = new TextRange(_view.Document.ContentStart, _view.Document.ContentEnd).Text;
            try { Clipboard.SetText(sel ?? ""); _status.Text = "Copied."; } catch { }
        }
    }
}
