using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The last lines of a Komodo stack's / deployment's / container's log, in a window of its own in the vShell theme. Read-only.
    /// Tail 50 / 100 / 500 / 1000 lines, Follow (asks again every 3 s and keeps to the bottom), a filter, timestamps on / off, Copy;
    /// lines saying error / fatal / panic are red, warnings yellow. A stack can show all its services or one. One window per item:
    /// opening the same one again brings it to the front.
    /// </summary>
    public sealed class KomodoLogWindow : Window
    {
        static readonly Dictionary<string, KomodoLogWindow> Open = new Dictionary<string, KomodoLogWindow>();
        static readonly int[] Tails = { 50, 100, 500, 1000 };

        readonly Theme _t;
        readonly KoItem _it;
        string _service;              // a stack's service, or null = all of them
        int _tail = 100;
        bool _follow, _stamps, _loading;
        string _text = "", _error = "";
        readonly WrapPanel _bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 8) };
        readonly TextBox _filter = new TextBox();
        readonly RichTextBox _view = new RichTextBox();
        readonly TextBlock _status = new TextBlock();
        readonly DispatcherTimer _timer;
        List<KoService> _services;

        /// <summary>Open (or bring forward) the log of this item; service = one service of a stack (null = all).</summary>
        public static void Show(Theme t, KoItem it, string service)
        {
            if (it == null || it.Kind == "procedure") return;
            string key = it.Key + "|" + (service ?? "");
            KomodoLogWindow w;
            if (Open.TryGetValue(key, out w) && w.IsLoaded)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
                return;
            }
            w = new KomodoLogWindow(t, it, service);
            Open[key] = w;
            w.Closed += delegate { Open.Remove(key); };
            w.Show();
            w.Activate();
        }

        KomodoLogWindow(Theme t, KoItem it, string service)
        {
            _t = t; _it = it; _service = service;
            Title = "Log - " + (service ?? it.Name);
            Width = 860; Height = 560;
            MinWidth = 420; MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = true;
            Topmost = false;
            Color bg = t.PopupBg.Color;
            Background = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));      // a normal window: no see-through
            BorderBrush = Theme.Faded(t.MainAccent, 45);
            BorderThickness = new Thickness(1);
            FontFamily = t.Font;
            WidgetWindow.StyleScrollbars(this);
            Resources["wThumb"] = Theme.Faded(t.FgDim, 70);

            var root = new DockPanel();
            // title row: what it is, and the close button (drag here to move the window)
            var title = new Grid { Background = Brushes.Transparent, Margin = new Thickness(14, 10, 8, 8) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = t.FontSize * 1.1, Foreground = t.MainAccent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            name.Children.Add(new TextBlock
            {
                Text = (service != null ? service + "  ·  " + it.Name : it.Name) + "  ·  " + it.Kind + (it.ServerName.Length > 0 ? " on " + it.ServerName : ""),
                Foreground = t.Fg, FontSize = t.FontSize * 1.08, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(name);
            var close = Pills.Make(t, "", "", "Close (Esc)", false, delegate { Close(); });
            Grid.SetColumn(close, 1);
            title.Children.Add(close);
            title.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else { try { DragMove(); } catch { } } };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);
            DockPanel.SetDock(_bar, Dock.Top);
            root.Children.Add(_bar);

            _status.Foreground = t.FgDim;
            _status.FontSize = t.FontSize * 0.88;
            _status.Margin = new Thickness(14, 4, 14, 8);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            // the log: selectable text, in a monospaced font
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
            var holder = new Border { Child = _view, Margin = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(6), ClipToBounds = true };
            root.Children.Add(holder);
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

            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if (e.Key == Key.F5) { e.Handled = true; Load(); }
                else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; _filter.Focus(); _filter.SelectAll(); }
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += delegate { if (_follow) Load(); };
            Loaded += delegate { _timer.Start(); };
            Closed += delegate { _timer.Stop(); };

            BuildBar();
            Load();
            if (it.Kind == "stack" && service == null) LoadServices();
        }

        async void LoadServices()
        {
            try
            {
                _services = await Komodo.ServicesAsync(_it);
                BuildBar();
            }
            catch { }
        }

        /// <summary>The toolbar: service (stacks), tail, follow, timestamps, the filter, refresh, copy.</summary>
        void BuildBar()
        {
            _bar.Children.Clear();
            Func<Border, Border> first = delegate (Border b) { b.Margin = new Thickness(0, 0, 4, 4); return b; };
            if (_it.Kind == "stack")
            {
                Border svc = null;
                svc = Pills.Make(_t, "", _service ?? "All services", "Which service", false, delegate
                {
                    var m = new ContextMenu();
                    var all = new MenuItem { Header = "All services", IsCheckable = true, IsChecked = _service == null };
                    all.Click += delegate { _service = null; BuildBar(); Load(); };
                    m.Items.Add(all);
                    if (_services != null)
                        foreach (KoService sv in _services)
                        {
                            string n = sv.Service;
                            var mi = new MenuItem { Header = n + "   (" + sv.State.Replace('_', ' ') + ")", IsCheckable = true, IsChecked = _service == n };
                            mi.Click += delegate { _service = n; BuildBar(); Load(); };
                            m.Items.Add(mi);
                        }
                    m.PlacementTarget = svc;
                    m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    m.IsOpen = true;
                });
                _bar.Children.Add(first(svc));
            }
            Border tail = null;
            tail = Pills.Make(_t, "", "Last " + _tail + " lines", "How many lines", false, delegate
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
            _bar.Children.Add(_bar.Children.Count == 0 ? first(tail) : tail);
            _bar.Children.Add(Pills.Make(_t, "", "Follow", "Ask again every 3 seconds and keep to the newest lines", false, delegate { _follow = !_follow; BuildBar(); if (_follow) Load(); }, _follow));
            _bar.Children.Add(Pills.Make(_t, "", "Timestamps", "Show Docker's time for each line", false, delegate { _stamps = !_stamps; BuildBar(); Load(); }, _stamps));
            if (_filter.Parent is Panel) ((Panel)_filter.Parent).Children.Remove(_filter);
            _bar.Children.Add(_filter);
            _bar.Children.Add(Pills.Make(_t, "", "Refresh", "Ask Komodo again (F5)", false, delegate { Load(); }));
            _bar.Children.Add(Pills.Make(_t, "", "Copy", "Copy the lines shown (or the selection)", false, delegate { Copy(); }));
            foreach (UIElement e in _bar.Children) { var fe = e as FrameworkElement; if (fe != null && fe.Margin.Bottom == 0) fe.Margin = new Thickness(fe.Margin.Left, 0, 0, 4); }
        }

        async void Load()
        {
            if (_loading) return;
            _loading = true;
            _status.Text = "Asking Komodo…";
            try
            {
                _text = await Komodo.LogAsync(_it, _service, _tail, _stamps);
                _error = "";
            }
            catch (Exception ex) { _error = ex.Message; }
            finally { _loading = false; }
            Render();
        }

        static readonly SolidColorBrush Red = Fz(0xEF, 0x4D, 0x4D), Yellow = Fz(0xF5, 0xC5, 0x42);
        static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        /// <summary>The lines (filtered, coloured) into the view; keeps to the bottom when following or already at the bottom.</summary>
        void Render()
        {
            bool atEnd = _follow || _view.VerticalOffset >= _view.ExtentHeight - _view.ViewportHeight - 4;
            string f = _filter.Text.Trim();
            string[] lines = (_text ?? "").Replace("\r\n", "\n").Split('\n');
            var para = new Paragraph { Margin = new Thickness(0) };
            int shown = 0, total = 0;
            foreach (string raw in lines)
            {
                if (raw.Length == 0 && total == lines.Length - 1) break;
                total++;
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
            _status.Text = _error.Length > 0 ? "Komodo: " + _error
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
