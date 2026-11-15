using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;
using JsonValue = vShell.Core.JsonValue;

namespace vShell.Lud
{
    /// <summary>
    /// A system's live command output from Linux Update Dashboard (its WebSocket /api/ws/systems/{id}/output), in a window of its own in
    /// the vShell theme. On connecting the dashboard sends what it still has of the last command, then each line as it comes. Buttons for
    /// Check now, Upgrade and Cancel, Keep to the bottom, Copy, Clear. Connects again (every 5 s) if the connection drops. One window per
    /// system: opening it again brings it to the front.
    /// </summary>
    public sealed class LudOutputWindow : Window
    {
        static readonly Dictionary<long, LudOutputWindow> Open = new Dictionary<long, LudOutputWindow>();
        static readonly Regex Ansi = new Regex("\u001B\\[[0-9;?]*[ -/]*[@-~]|\u001B\\][^\u0007\u001B]*(\u0007|\u001B\\\\)|\u001B[()][A-Za-z0-9]", RegexOptions.Compiled);
        const int MaxLines = 5000;

        readonly Theme _t;
        readonly long _id;
        readonly WrapPanel _bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 8) };
        readonly RichTextBox _view = new RichTextBox();
        readonly Paragraph _para = new Paragraph { Margin = new Thickness(0) };
        readonly TextBlock _status = new TextBlock();
        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        bool _follow = true, _lineOpen;
        int _lines;
        string _state = "Connecting…";

        /// <summary>Open (or bring forward) the output of this system.</summary>
        public static void Show(Theme t, long systemId)
        {
            LudOutputWindow w;
            if (Open.TryGetValue(systemId, out w) && w.IsLoaded)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
                return;
            }
            w = new LudOutputWindow(t, systemId);
            Open[systemId] = w;
            w.Closed += delegate { Open.Remove(systemId); };
            w.Show();
            w.Activate();
        }

        /// <summary>Is an output window open (so the poller keeps going quickly)?</summary>
        public static bool AnyOpen { get { return Open.Count > 0; } }

        /// <summary>The plugin is removed: close them all.</summary>
        public static void CloseAll()
        {
            foreach (LudOutputWindow w in new List<LudOutputWindow>(Open.Values)) try { w.Close(); } catch { }
        }

        LudOutputWindow(Theme t, long systemId)
        {
            _t = t; _id = systemId;
            LudSystem x = LudClient.Latest.Find(systemId);
            string name = x != null ? x.Label : "system " + systemId;
            Title = "Output - " + name;
            Width = 860; Height = 560;
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
            // title row: which system, and the close button (drag here to move the window)
            var title = new Grid { Background = Brushes.Transparent, Margin = new Thickness(14, 10, 8, 8) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var head = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(new TextBlock { Text = LudPlugin.Glyph, FontFamily = IconUtil.GlyphFont, FontSize = t.FontSize * 1.1, Foreground = t.MainAccent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            head.Children.Add(new TextBlock
            {
                Text = name + (x != null && x.Os.Length > 0 ? "  ·  " + x.Os : "") + "  ·  output",
                Foreground = t.Fg, FontSize = t.FontSize * 1.08, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(head);
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
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            // the output: selectable text, in a monospaced font
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
            _view.Document.Blocks.Add(_para);
            var holder = new Border { Child = _view, Margin = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(6), ClipToBounds = true };
            root.Children.Add(holder);
            Content = root;

            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            };
            Closed += delegate { _stop.Cancel(); };

            BuildBar();
            ShowStatus();
            Run();
        }

        /// <summary>The toolbar: Check now / Upgrade / Cancel (as the system and the token allow), keep to the bottom, copy, clear, open the dashboard.</summary>
        void BuildBar()
        {
            _bar.Children.Clear();
            BarSettings s = LudPlugin.Settings;
            LudSystem x = LudClient.Latest.Find(_id);
            bool write = s != null && !LudClient.ReadOnly;
            bool actions = write && LudSettings.Of(s).AllowActions;
            if (x != null && write)
            {
                if (!x.Busy) _bar.Children.Add(Pills.Make(_t, "", "Check now", "Look for updates on " + x.Label, false, delegate { LudPlugin.Do("check", _id); }));
                if (!x.Busy && actions && x.Updates > 0)
                    _bar.Children.Add(Pills.Make(_t, "", "Upgrade", "Upgrade " + LudClient.Count(x.Updates, "package") + " (asks first)", false, delegate { LudPlugin.Do("upgrade", _id); }));
                if (x.Busy) _bar.Children.Add(Pills.Make(_t, "", "Cancel", "Ask the dashboard to stop " + x.OpText.TrimEnd('…'), false, delegate { LudPlugin.Do("cancel", _id); }));
            }
            _bar.Children.Add(Pills.Make(_t, "", "Keep to the bottom", "Scroll to the newest line as it comes", false, delegate { _follow = !_follow; BuildBar(); if (_follow) _view.ScrollToEnd(); }, _follow));
            _bar.Children.Add(Pills.Make(_t, "", "Copy", "Copy the output (or the selection)", false, delegate { Copy(); }));
            _bar.Children.Add(Pills.Make(_t, "", "Clear", "Empty the window (the dashboard keeps its copy)", false, delegate { Clear(); }));
            if (s != null) _bar.Children.Add(Pills.Make(_t, "", "Open dashboard", "Open Linux Update Dashboard in the browser", false, delegate { RunPanel.Open(LudClient.WebUrl(s)); }));
            foreach (UIElement e in _bar.Children) { var fe = e as FrameworkElement; if (fe != null) fe.Margin = new Thickness(0, 0, 4, 4); }
        }

        /// <summary>A new snapshot: the buttons follow what's running.</summary>
        internal static void Refresh()
        {
            foreach (LudOutputWindow w in new List<LudOutputWindow>(Open.Values)) { w.BuildBar(); w.ShowStatus(); }
        }

        /// <summary>Connected: the messages as they come; dropped: try again in 5 seconds, until the window closes.</summary>
        async void Run()
        {
            CancellationToken ct = _stop.Token;
            while (!ct.IsCancellationRequested)
            {
                BarSettings s = LudPlugin.Settings;
                if (s == null || !LudClient.Configured(s)) { _state = "Linux Update Dashboard isn't set up (Settings > Plugins)"; ShowStatus(); return; }
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        if (LudSettings.Of(s).IgnoreCert) ws.Options.RemoteCertificateValidationCallback = delegate { return true; };
                        ws.Options.SetRequestHeader("Authorization", "Bearer " + LudSettings.Of(s).TokenPlain);
                        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        _state = "Connecting…";
                        ShowStatus();
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(10));
                            await ws.ConnectAsync(SocketUri(s), timeout.Token);
                        }
                        _state = "Connected - waiting for output";
                        ShowStatus();
                        while (!ct.IsCancellationRequested)
                        {
                            string text = await Receive(ws, ct);
                            if (text == null) break;
                            Handle(text);
                        }
                    }
                }
                catch (OperationCanceledException) { if (ct.IsCancellationRequested) return; _state = "Linux Update Dashboard didn't answer - trying again"; }
                catch (Exception ex)
                {
                    string why = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    if (why.Contains("401")) why = "the API token wasn't accepted";
                    _state = "Not connected: " + why + " - trying again";
                }
                if (ct.IsCancellationRequested) return;
                if (_state.StartsWith("Connected")) _state = "The connection closed - connecting again";
                ShowStatus();
                try { await Task.Delay(5000, ct); } catch { return; }
            }
        }

        Uri SocketUri(BarSettings s)
        {
            string b = LudClient.Base(s);
            if (b.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) b = "wss://" + b.Substring(8);
            else if (b.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) b = "ws://" + b.Substring(7);
            return new Uri(b + "/api/ws/systems/" + _id + "/output");
        }

        /// <summary>One whole text message, or null when the dashboard closed the connection.</summary>
        static async Task<string> Receive(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[16384];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return null;
                    ms.Write(buf, 0, r.Count);
                    if (r.EndOfMessage) break;
                    if (ms.Length > 16 * 1024 * 1024) throw new IOException("message too large");
                }
                return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        static readonly SolidColorBrush Red = Fz(0xEF, 0x4D, 0x4D), Yellow = Fz(0xF5, 0xC5, 0x42), Green = Fz(0x4C, 0xC3, 0x6A);
        static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        /// <summary>A message from the dashboard: reset, started, output, phase, done, error, warning.</summary>
        void Handle(string text)
        {
            JsonDocument d;
            try { d = JsonDocument.Parse(text); } catch { return; }
            using (d)
            {
                JsonElement e = d.RootElement;
                switch (JsonValue.Str(e, "type"))
                {
                    case "reset":
                        Clear();
                        break;
                    case "started":
                        {
                            string pm = JsonValue.Str(e, "pkgManager");
                            Line("$ " + JsonValue.Str(e, "command") + (pm.Length > 0 ? "    (" + pm + ")" : ""), _t.MainAccent);
                            _state = "Running…";
                            break;
                        }
                    case "output":
                        Append(JsonValue.Str(e, "data"), JsonValue.Str(e, "stream") == "stderr" ? Yellow : null);
                        break;
                    case "phase":
                        _state = "Running: " + JsonValue.Str(e, "phase").Replace('_', ' ');
                        break;
                    case "done":
                        {
                            bool ok = JsonValue.Bool(e, "success");
                            Line(ok ? "- finished" : "- failed", ok ? Green : Red);
                            _state = (ok ? "Finished " : "Failed ") + TimeText.Clock(DateTime.Now) + " - waiting for more";
                            break;
                        }
                    case "error":
                        Line(JsonValue.Str(e, "message"), Red);
                        break;
                    case "warning":
                        Line(JsonValue.Str(e, "message"), Yellow);
                        break;
                }
            }
            ShowStatus();
        }

        /// <summary>A whole line of its own (a command, done, an error).</summary>
        void Line(string text, Brush fg)
        {
            if (_lineOpen) Append("\n", null);
            Append(text + "\n", fg);
        }

        /// <summary>Text as it came: colour codes taken out, a carriage return on its own (a progress bar) dropped, kept to the last 5000 lines.</summary>
        void Append(string data, Brush fg)
        {
            if (string.IsNullOrEmpty(data)) return;
            bool atEnd = _follow || _view.VerticalOffset >= _view.ExtentHeight - _view.ViewportHeight - 4;
            string clean = Ansi.Replace(data, "").Replace("\r\n", "\n").Replace("\r", "");
            string[] parts = clean.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) { _para.Inlines.Add(new LineBreak()); _lines++; _lineOpen = false; }
                if (parts[i].Length == 0) continue;
                string l = parts[i].Length > 4000 ? parts[i].Substring(0, 4000) + " …" : parts[i];
                var run = new Run(l);
                if (fg != null) run.Foreground = fg;
                else
                {
                    string low = l.ToLowerInvariant();
                    if (low.StartsWith("e:") || low.Contains("error") || low.Contains("failed")) run.Foreground = Red;
                    else if (low.StartsWith("w:") || low.Contains("warning")) run.Foreground = Yellow;
                }
                _para.Inlines.Add(run);
                _lineOpen = true;
            }
            // too long: drop the oldest lines (a thousand at a time)
            if (_lines > MaxLines)
            {
                int drop = 1000;
                while (drop > 0 && _para.Inlines.FirstInline != null)
                {
                    Inline first = _para.Inlines.FirstInline;
                    if (first is LineBreak) { drop--; _lines--; }
                    _para.Inlines.Remove(first);
                }
            }
            if (atEnd) _view.ScrollToEnd();
        }

        void Clear()
        {
            _para.Inlines.Clear();
            _lines = 0;
            _lineOpen = false;
        }

        void ShowStatus()
        {
            LudSystem x = LudClient.Latest.Find(_id);
            string sys = x == null ? "" : x.Busy ? x.OpText : x.UpdatesText + (x.NeedsReboot ? " · reboot needed" : "");
            _status.Text = _state + (sys.Length > 0 ? "   ·   " + sys : "") + "   ·   Esc close";
            _status.Foreground = _state.StartsWith("Not connected") ? (Brush)Red : _t.FgDim;
        }

        void Copy()
        {
            string sel = _view.Selection != null ? _view.Selection.Text : "";
            if (string.IsNullOrEmpty(sel)) sel = new TextRange(_view.Document.ContentStart, _view.Document.ContentEnd).Text;
            try { Clipboard.SetText(sel ?? ""); _state = "Copied."; ShowStatus(); } catch { }
        }
    }
}
