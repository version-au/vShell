using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>A module that takes the keyboard while you type in it (a note, a run box); the window calls StopTyping when it loses it.</summary>
    [PluginApi]
    public interface ITypingModule
    {
        void StopTyping();
    }

    /// <summary>
    /// A run box on a bar or in a widget: a search field (just a search icon until you type). Click it and type straight into it - the
    /// suggestions and results drop down under it (over it on a bottom bar). Its keys open the same box in the middle of the main monitor.
    /// "!g dog pictures" searches Google, a plugin's keyword (!s with Seerr) shows its results, an app name starts the app,
    /// a path / URL / ms-settings: opens it, ">ipconfig" runs in a terminal, "=12*7.5" works out the sum; anything else is a web search.
    /// </summary>
    public sealed class RunBoxModule : Border, ITypingModule
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly TextBox _box;
        readonly Popup _drop;
        readonly RunCard _card;
        RunPanel _panel;
        bool _typing;
        QuickRunWindow _quick;

        /// <summary>The plugins' file drops (PluginHost.AddRunBoxDrop): each run box made gets them.</summary>
        internal static readonly List<Action<FrameworkElement, Action<bool, string>, Action<List<string>>>> PluginDrops = new List<Action<FrameworkElement, Action<bool, string>, Action<List<string>>>>();

        /// <summary>The plugins' keywords for the tooltip (", !s Seerr, !ko Komodo"), or "".</summary>
        static string PluginHintText(BarSettings s)
        {
            string r = "";
            foreach (Func<BarSettings, string> hint in RunPanel.PluginHints.ToArray()) { string h = hint(s); if (!string.IsNullOrEmpty(h)) r += ", " + h; }
            return r;
        }

        public RunBoxModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            Theme t = host.Theme.ForModule(cfg);
            _t = t;
            double h = Math.Max(20, Math.Min(32, host.HostHeight - (host.IsWidget ? 6 : 10)));
            Width = cfg.RunWidth;
            Height = h;
            Margin = new Thickness(4, 0, 4, 0);
            CornerRadius = new CornerRadius(h / 2);
            Background = Theme.Faded(t.Fg, 9);
            BorderBrush = Theme.Faded(t.Fg, 22);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.IBeam;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock
            {
                Text = "\uE721", FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(9, t.FontSize * 0.95), Foreground = t.FgDim,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0)
            });
            _box = new TextBox
            {
                Background = Brushes.Transparent, Foreground = t.Fg, CaretBrush = t.Fg, BorderThickness = new Thickness(0), Padding = new Thickness(2, 0, 8, 0),
                FontSize = Math.Max(8, t.FontSize * 0.95), SelectionBrush = t.MainAccent, VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_box, 1);
            g.Children.Add(_box);
            Child = g;

            string keys = cfg.RunHotkey != "Off" ? cfg.RunHotkey : "";
            ToolTip = "Run an app or a command, open a path or web address, !g / !yt / !w ... to search a site" +
                      PluginHintText(mgr.Settings) +
                      ", =sum, >command" + (keys.Length > 0 ? "\nFrom anywhere: " + keys : "");
            MouseEnter += delegate { BorderBrush = Theme.Faded(t.MainAccent, 70); };
            MouseLeave += delegate { if (!_typing) BorderBrush = Theme.Faded(t.Fg, 22); };

            // the drop-down under (or over) the box: suggestions and results; it never takes the keyboard from the box.
            // It looks like a widget of the current widget style (background, corners, outline, shadow, padding).
            _card = new RunCard(mgr, t);
            _drop = new Popup
            {
                PlacementTarget = this, Placement = host.PopupsBelow ? PlacementMode.Bottom : PlacementMode.Top, AllowsTransparency = true,
                StaysOpen = true, Child = _card, HorizontalOffset = -RunCard.Room,
                PopupAnimation = PopupAnimation.None
            };

            // a press on the box (or the icon) takes the keyboard for the bar / widget, then the text box gets it
            PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;     // Ctrl: the bar's / widget's own drag
                StartTyping();
                if (!_box.IsMouseOver) e.Handled = true;
            };
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };     // never a bar / widget drag

            // files dropped from File Explorer that a plugin takes (.torrent / .nzb to the Downloads plugin); the result shows in the drop-down for a moment
            foreach (Action<FrameworkElement, Action<bool, string>, Action<List<string>>> attach in PluginDrops.ToArray())
                attach(this, delegate (bool on, string what)
                {
                    BorderBrush = on ? (Brush)_t.MainAccent : _typing ? Theme.Faded(_t.MainAccent, 90) : Theme.Faded(_t.Fg, 22);
                    if (on) ShowNote(what, false);
                    else if (!_typing && _drop.IsOpen && _noteUntil == DateTime.MaxValue) _drop.IsOpen = false;
                }, delegate (List<string> lines) { ShowNote(string.Join("\n", lines), true); });
            _box.PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; StopTyping(); } };
        }

        public ModuleConfig Config { get { return _cfg; } }

        DateTime _noteUntil;
        System.Windows.Threading.DispatcherTimer _noteTimer;

        /// <summary>A message in the drop-down without taking the keyboard (dropped files); timed = goes after a few seconds.</summary>
        void ShowNote(string text, bool timed)
        {
            if (_typing) { if (_panel != null) _panel.ShowMessage(text); return; }
            if (_panel == null)
            {
                _panel = new RunPanel(_mgr, _t, Math.Max(_cfg.RunWidth, 340), delegate { StopTyping(); }, _box);
                _card.Content = _panel;
            }
            _panel.ShowMessage(text);
            PlaceDrop();
            OpenDrop();
            _noteUntil = timed ? DateTime.UtcNow.AddSeconds(4) : DateTime.MaxValue;
            if (!timed) return;
            if (_noteTimer == null)
            {
                _noteTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _noteTimer.Tick += delegate
                {
                    if (_typing || DateTime.UtcNow < _noteUntil) { if (_typing) _noteTimer.Stop(); return; }
                    _noteTimer.Stop();
                    _drop.IsOpen = false;
                };
            }
            _noteTimer.Start();
        }

        /// <summary>The background effect for this box's drop-down / quick box: the bar's / widget's, unless turned off for its popup.</summary>
        string BackdropMode() { return _cfg.PopupBackdrop ? PopupGlass.ModeFor(_mgr, _host) : "Off"; }

        void OpenDrop()
        {
            _card.SetBackdrop(BackdropMode());
            _drop.IsOpen = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(_card.UpdateBackdrop));
        }

        void StartTyping()
        {
            if (!_typing)
            {
                _typing = true;
                BorderBrush = Theme.Faded(_t.MainAccent, 90);
                var w = _host as WidgetWindow;
                if (w != null) w.BeginTyping(this);
                var b = _host as BarWindow;
                if (b != null) b.BeginTyping(this);
                if (_panel == null)
                {
                    _panel = new RunPanel(_mgr, _t, Math.Max(_cfg.RunWidth, 340), delegate { StopTyping(); }, _box);
                    _card.Content = _panel;
                }
                _panel.Refresh();
                PlaceDrop();
                OpenDrop();
            }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(delegate
            {
                _box.Focus();
                Keyboard.Focus(_box);
            }));
        }

        /// <summary>
        /// The gap between the drop-down and what it opens from: on a bar 10 px past the bar's own top / bottom edge (not just the box),
        /// in a widget 12 px under / over the box so it (and its shadow) stays clear of it. The card has RunCard.Room around it for the shadow.
        /// </summary>
        void PlaceDrop()
        {
            bool below = _host.PopupsBelow;
            double gap = 12, past = 0;
            var b = _host as BarWindow;
            if (b != null && b.VisualBar != null && b.VisualBar.ActualHeight > 0)
            {
                gap = 10;
                try
                {
                    Point top = TranslatePoint(new Point(0, 0), b.VisualBar);
                    past = below ? Math.Max(0, b.VisualBar.ActualHeight - (top.Y + ActualHeight)) : Math.Max(0, top.Y);
                }
                catch { past = 0; }
            }
            double off = past + gap - RunCard.Room;
            _drop.VerticalOffset = below ? off : -off;
        }

        /// <summary>Done (Enter ran something, Esc, or clicked away): close the drop-down, empty the box, give the keyboard back.</summary>
        public void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            _drop.IsOpen = false;
            _box.Text = "";
            BorderBrush = Theme.Faded(_t.Fg, 22);
            Keyboard.ClearFocus();
            var w = _host as WidgetWindow;
            if (w != null) w.EndTyping(this);
            var b = _host as BarWindow;
            if (b != null) b.EndTyping(this);
        }

        /// <summary>The run box keys: the box in the middle of the main monitor, ready to type.</summary>
        public void Open(bool fromHotkey)
        {
            if (_quick != null && _quick.IsVisible) { _quick.TakeFocus(); return; }
            var q = new QuickRunWindow(_mgr, _t, Math.Max(_cfg.RunWidth, 560), BackdropMode());
            q.Closed += delegate { if (_quick == q) _quick = null; };
            _quick = q;
            q.Show();
            q.TakeFocus();
        }
    }

    /// <summary>
    /// The card the run box's list sits on: the current widget style (theme background, corners, outline, shadow, padding), or a
    /// plain popup card when there is no widget style. Room = the space left around it for the shadow.
    /// </summary>
    sealed class RunCard : Grid
    {
        public const double Room = 12;
        readonly Border _card, _shadow;
        readonly Glass _glass;
        readonly Theme _t;
        Brush _plainBg;

        public RunCard(BarManager mgr, Theme t, bool strong = false)
        {
            _t = t;
            Margin = new Thickness(Room);
            WidgetStyle ws = mgr.Settings.WidgetStyle;
            _card = new Border();
            _shadow = new Border { Background = Brushes.Black, IsHitTestVisible = false };
            if (ws != null)
            {
                _card.Background = t.MakeBg(-1);
                _card.CornerRadius = new CornerRadius(Math.Max(0, ws.CornerRadius));
                _card.BorderThickness = new Thickness(ws.ShowBorder ? Math.Max(0.5, Math.Min(12, ws.BorderThickness)) : 0);
                _card.BorderBrush = Theme.Faded(t.Resolve(ws.BorderColor, t.MainAccent), ws.BorderOpacity);
                _card.Padding = new Thickness(Math.Max(4, ws.Padding));
                _shadow.Visibility = ws.ShowShadow ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _card.Background = t.PopupBg;
                _card.CornerRadius = new CornerRadius(strong ? 12 : 8);
                _card.BorderThickness = new Thickness(1);
                _card.BorderBrush = strong ? Theme.Faded(t.MainAccent, 60) : t.Track;
                _card.Padding = new Thickness(strong ? 10 : 6);
            }
            _shadow.CornerRadius = _card.CornerRadius;
            _shadow.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = strong ? 18 : 14, ShadowDepth = 2, Opacity = strong ? 0.5 : 0.4, Color = Colors.Black
            };
            System.Windows.Documents.TextElement.SetForeground(_card, t.Fg);
            System.Windows.Documents.TextElement.SetFontFamily(_card, t.Font);
            System.Windows.Documents.TextElement.SetFontSize(_card, t.FontSize);
            Children.Add(_shadow);
            _glass = new Glass(mgr);
            _glass.SetCorners(_card.CornerRadius);
            Children.Add(_glass);
            Children.Add(_card);
            if (ws != null)
            {
                var sheen = new Border();
                EdgeFx.GlassEdge(sheen, Styles.GlassOf(ws), _card.CornerRadius);     // the widget style's glass edge
                Children.Add(sheen);
            }
            _plainBg = _card.Background;
            // only the part of the shadow outside the card is drawn (a see-through background doesn't show it through)
            _card.SizeChanged += delegate
            {
                double w = _card.ActualWidth, h = _card.ActualHeight, r = _card.CornerRadius.TopLeft;
                _shadow.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-40, -40, w + 80, h + 80)),
                                                    new RectangleGeometry(new Rect(0, 0, w, h), r, r));
            };
        }

        public UIElement Content { get { return _card.Child; } set { _card.Child = value; } }

        /// <summary>The background effect under the card: "Off", "Blur" or "Acrylic" (Frosted).</summary>
        public void SetBackdrop(string mode)
        {
            mode = string.IsNullOrEmpty(mode) ? "Off" : mode;
            _glass.Refresh(mode);
            _card.Background = mode != "Off" && ReferenceEquals(_plainBg, _t.PopupBg) ? _t.MakeBg(-1) : _plainBg;
            _glass.Update();
        }

        /// <summary>The drop-down was moved / opened again: show the part of the effect behind it now.</summary>
        public void UpdateBackdrop() { _glass.Update(); }
    }

    /// <summary>
    /// The run box opened with its keys: a box in the middle of the main monitor (a third of the way down), with the suggestions under
    /// it. Closes after running something, on Esc, when another window comes in front, or when you click anywhere else - also when
    /// Windows didn't let it take the keyboard.
    /// </summary>
    sealed class QuickRunWindow : Window
    {
        readonly RunPanel _panel;
        readonly System.Windows.Threading.DispatcherTimer _watch;
        IntPtr _hwnd, _fgAtOpen;
        bool _closing, _wasActive, _mouseWasDown;
        int _tries;

        public QuickRunWindow(BarManager mgr, Theme t, double width, string backdrop = "Off")
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.Height;
            Width = width + 2 * RunCard.Room + 24;
            Title = "vShell run";
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000; Top = -32000;
            _panel = new RunPanel(mgr, t, width, delegate { CloseSafe(); }, null);
            var card = new RunCard(mgr, t, true) { Content = _panel };
            card.SetBackdrop(backdrop);
            Content = card;
            LocationChanged += delegate { card.UpdateBackdrop(); };
            Deactivated += delegate
            {
                IntPtr fg = Native.GetForegroundWindow();
                // opened over the desktop: Explorer's desktop takes the keyboard back as the Alt key goes up - take it again (a click on
                // the desktop still closes it, through the click watch)
                if (DesktopGrab(fg)) { Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(TakeFocus)); return; }
                Why("lost the keyboard", fg);
                CloseSafe();
            };
            Activated += delegate { _wasActive = true; };
            PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; CloseSafe(); } };
            SizeChanged += delegate { Place(); };
            _fgAtOpen = Native.GetForegroundWindow();
            // Deactivated only comes once the window was active: watch for a click outside it or another window coming in front too
            _watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _watch.Tick += delegate { Watch(); };
            _mouseWasDown = AnyMouseDown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WinStyle.HideFromTasks(_hwnd, false);
            Place();
            _watch.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _watch.Stop();
            base.OnClosed(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        static bool AnyMouseDown()
        {
            return (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;
        }

        void Watch()
        {
            if (_closing || _hwnd == IntPtr.Zero) return;
            IntPtr fg = Native.GetForegroundWindow();
            // still not in front (Windows refused it): keep asking for a moment
            if (!_wasActive && fg != _hwnd && _tries < 6) { _tries++; TakeFocus(); return; }
            if (!_wasActive && fg != _hwnd && _tries == 6) { _tries++; Why("Windows didn't let it take the keyboard", fg); }
            // the desktop took the keyboard back (not a click): take it again
            if (fg != _hwnd && DesktopGrab(fg)) { TakeFocus(); return; }
            // something else came in front (Alt+Tab, a window opening, the Start menu)
            if (fg != _hwnd && fg != IntPtr.Zero && (_wasActive || fg != _fgAtOpen)) { Why("another window came in front", fg); CloseSafe(); return; }
            // a click outside the box
            bool down = AnyMouseDown();
            if (down && !_mouseWasDown)
            {
                RECT r;
                POINT p = Native.CursorPos();
                if (Native.GetWindowRect(_hwnd, out r) && !(p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom)) { CloseSafe(); return; }
            }
            _mouseWasDown = down;
        }

        /// <summary>Centred across the main monitor, its top a third of the way down (it grows downwards).</summary>
        void Place()
        {
            if (_hwnd == IntPtr.Zero) return;
            MonitorInfo m = MonitorHelper.Primary();
            if (m == null) return;
            RECT r;
            if (!Native.GetWindowRect(_hwnd, out r)) return;
            int w = r.Width;
            int x = m.Bounds.Left + (m.Bounds.Width - w) / 2;
            int y = m.Bounds.Top + m.Bounds.Height / 3 - (int)Math.Round(40 * MonitorHelper.ScaleOf(m));
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        /// <summary>Take the keyboard even though another program is in front (it was opened by keys, not a click).</summary>
        public void TakeFocus()
        {
            try
            {
                if (_hwnd != IntPtr.Zero && Native.GetForegroundWindow() != _hwnd)
                {
                    // Windows only lets the program that got the last key press bring a window in front: a harmless key
                    // press (no key, code 0xFF) makes that vShell, then it is asked again with the input threads joined
                    // (also while Alt is still down from the keys: then the Alt going up doesn't open the menu of the window behind, which
                    // would take the keyboard back)
                    if (_tries > 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0)
                    {
                        keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                        keybd_event(0xFF, 0, 2, UIntPtr.Zero);
                    }
                    uint pid;
                    IntPtr fg = Native.GetForegroundWindow();
                    uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out pid);
                    uint me = GetCurrentThreadId();
                    bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
                    try
                    {
                        BringWindowToTop(_hwnd);
                        Native.SetForegroundWindow(_hwnd);
                    }
                    finally { if (attached) AttachThreadInput(me, fgThread, false); }
                }
                Activate();
            }
            catch (Exception ex) { Log.Write("Run box focus: " + ex.Message); }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(delegate { _panel.FocusBox(); }));
        }

        int _regrabs;

        /// <summary>
        /// The desktop (Explorer's Progman / WorkerW) came in front without a click - it does that when the box was opened over it and the
        /// Alt key goes up. Worth taking the keyboard back from (a few times; a click on the desktop closes the box as before).
        /// </summary>
        bool DesktopGrab(IntPtr fg)
        {
            if (_closing || AnyMouseDown() || _regrabs >= 8) return false;
            // the window that was in front when the keys were pressed taking the keyboard back straight away (as the Alt key goes up),
            // or nothing in front for a moment: not something you did - take it again. Later (or another window) closes the box.
            bool early = (DateTime.UtcNow - _opened).TotalMilliseconds < 900;
            string cls = fg == IntPtr.Zero ? "" : Native.GetClass(fg);
            bool desktop = cls == "Progman" || cls == "WorkerW";
            if (!(desktop || (early && (fg == IntPtr.Zero || fg == _fgAtOpen)))) return false;
            _regrabs++;
            return true;
        }

        readonly DateTime _opened = DateTime.UtcNow;

        /// <summary>Log why the box went (and which window was in front), to find what takes it away.</summary>
        void Why(string what, IntPtr fg)
        {
            // only when it goes on its own straight after opening (that's the problem case); closing it normally isn't logged
            if (_closing || (DateTime.UtcNow - _opened).TotalMilliseconds > 1500) return;
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                string proc = "";
                try { using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) proc = p.ProcessName; } catch { }
                Log.Write("Run box: " + what + " - in front: \"" + Native.GetTitle(fg) + "\" [" + Native.GetClass(fg) + "] " + proc +
                          (pid == (uint)Environment.ProcessId ? " (vShell)" : "") + ", tries " + _tries);
            }
            catch { }
        }

        public void CloseSafe()
        {
            if (_closing) return;
            _closing = true;
            _watch.Stop();
            try { Close(); } catch { }
        }
    }

    /// <summary>The open run box: the text box, and under it the suggestions (or a plugin's results).</summary>
    public sealed class RunPanel : StackPanel
    {
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly Action _close;
        readonly TextBox _box;
        readonly StackPanel _list = new StackPanel();
        readonly TextBlock _status;
        readonly List<Row> _rows = new List<Row>();
        int _sel;
        int _hist = -1;            // Up / Down through the history while the box shows a history entry
        /// <summary>The list shows a plugin's own results (Seerr's search): kept until the text changes.</summary>
        bool _held;

        /// <summary>A row in the drop-down (AddRow returns it).</summary>
        public sealed class Row
        {
            [PluginApi] public Border View;
            public Action Run;
            public string Complete;      // what Tab puts in the box
            public string Command;       // a palette command's key
        }

        BarSettings S { get { return _mgr.Settings; } }

        // ---- for plugins' !keywords (PluginHost.AddBang) ----
        /// <summary>A plugin's kind of run box action -> what fills the drop-down for it.</summary>
        internal static readonly Dictionary<string, Action<RunPanel, RunAction>> PluginRows = new Dictionary<string, Action<RunPanel, RunAction>>();
        /// <summary>What an empty box lists for the plugins' keywords ("!ko Komodo").</summary>
        internal static readonly List<Func<BarSettings, string>> PluginHints = new List<Func<BarSettings, string>>();
        /// <summary>A plugin's own state for this box (what's selected, a timer ...), by a key of its choosing.</summary>
        [PluginApi] public readonly Dictionary<string, object> PluginState = new Dictionary<string, object>();
        [PluginApi] public BarSettings Settings { get { return S; } }
        [PluginApi] public Theme Theme { get { return _t; } }
        /// <summary>The text in the box (trimmed).</summary>
        [PluginApi] public string Text { get { return _box.Text.Trim(); } }
        [PluginApi] public int Selected { get { return _sel; } }
        /// <summary>Close the run box (after doing something).</summary>
        [PluginApi] public void Close() { _close(); }
        /// <summary>Fill the drop-down again for the text in the box.</summary>
        [PluginApi] public void Redraw() { Suggest(); }
        /// <summary>Show something of the plugin's own instead of rows (search results with buttons): the list is emptied and kept as the
        /// plugin fills it (AddElement) until the text changes.</summary>
        [PluginApi] public void Hold() { _held = true; _rows.Clear(); _list.Children.Clear(); }
        /// <summary>Still showing what the plugin put there after Hold (false once the text changed).</summary>
        [PluginApi] public bool Held { get { return _held; } }
        [PluginApi] public void AddElement(UIElement e) { _list.Children.Add(e); }

        /// <summary>box = the run box module's own text box (only the list is shown here); null = a box of its own on top.</summary>
        public RunPanel(BarManager mgr, Theme t, double width, Action close, TextBox box)
        {
            _mgr = mgr; _t = t; _close = close;
            Width = width;
            // the palette's commands that arrive while it's open (Home Assistant's entity list) show without typing again
            Action changed = delegate { Dispatcher.BeginInvoke(new Action(PaletteChanged)); };
            Loaded += delegate { Palette.Changed -= changed; Palette.Changed += changed; };
            Unloaded += delegate { Palette.Changed -= changed; };
            _status = new TextBlock { Foreground = t.FgDim, FontSize = Math.Max(8, t.FontSize * 0.85), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 2), Visibility = Visibility.Collapsed };
            if (box != null)
            {
                _box = box;
                Children.Add(_list);
                Children.Add(_status);
                _box.TextChanged += delegate { if (_hist < 0) { _held = false; Suggest(); } };
                _box.PreviewKeyDown += Box_KeyDown;
                _box.PreviewTextInput += delegate { _hist = -1; };
                StartIcons.Preload();
                return;
            }
            _box = new TextBox
            {
                Background = Brushes.Transparent, Foreground = t.Fg, CaretBrush = t.Fg, BorderThickness = new Thickness(0),
                FontSize = t.FontSize * 1.15, Padding = new Thickness(4, 6, 4, 6), SelectionBrush = t.MainAccent, VerticalContentAlignment = VerticalAlignment.Center
            };
            var hint = new TextBlock
            {
                Text = "Run or !search", Foreground = t.FgDim, FontSize = t.FontSize * 1.15, IsHitTestVisible = false,
                Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var field = new Grid();
            field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var glyph = new TextBlock
            {
                Text = "", FontFamily = IconUtil.GlyphFont, FontSize = t.FontSize * 1.1, Foreground = t.MainAccent,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0)
            };
            Grid.SetColumn(hint, 1);
            Grid.SetColumn(_box, 1);
            field.Children.Add(glyph);
            field.Children.Add(hint);
            field.Children.Add(_box);
            var fieldBox = new Border { Child = field, Background = Theme.Faded(t.Fg, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 6) };
            Children.Add(fieldBox);
            Children.Add(_list);
            Children.Add(_status);

            _box.TextChanged += delegate
            {
                hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_hist < 0) { _held = false; Suggest(); }
            };
            _box.PreviewKeyDown += Box_KeyDown;
            _box.PreviewTextInput += delegate { _hist = -1; };       // typing leaves the history
            StartIcons.Preload();
            Palette.Warm(S);
            Suggest();
        }

        /// <summary>New commands to list: show them when the box shows commands now.</summary>
        void PaletteChanged()
        {
            if (!IsVisible || _held) return;
            string text = _box.Text.Trim();
            if (text.Length == 0) return;
            RunAction a = RunParser.Parse(text, S);
            if (!(a.Kind == "Palette" || a.Kind == "Run" || (a.Kind == "Url" && a.Query.Length > 0 && !text.StartsWith("!")))) return;
            int keep = _sel;
            Suggest();
            Select(keep);
        }

        /// <summary>Only a message (no suggestions): files dropped on the run box.</summary>
        public void ShowMessage(string text)
        {
            _rows.Clear();
            _list.Children.Clear();
            Status(text);
        }

        /// <summary>Show the suggestions for what is in the box now (the drop-down opening again).</summary>
        public void Refresh()
        {
            _hist = -1;
            _held = false;
            Palette.Warm(S);
            Suggest();
        }

        public void FocusBox()
        {
            _box.Focus();
            Keyboard.Focus(_box);
            _box.CaretIndex = _box.Text.Length;
        }

        void Box_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    if (_rows.Count > 0 && _sel >= 0 && _sel < _rows.Count && _rows[_sel].Run != null) _rows[_sel].Run();
                    else Execute(_box.Text);
                    break;
                // Down / Up move the highlight down / up the list as it is shown (in an empty box the list is your history)
                case Key.Down:
                    e.Handled = true;
                    if (_rows.Count == 0) { History(-1); break; }
                    Select(_sel + 1);
                    break;
                case Key.Up:
                    e.Handled = true;
                    if (_rows.Count == 0) { History(1); break; }
                    Select(_sel - 1);
                    break;
                case Key.Back:
                case Key.Delete:
                    _hist = -1;
                    break;
                case Key.Tab:
                    e.Handled = true;
                    if (_sel >= 0 && _sel < _rows.Count && !string.IsNullOrEmpty(_rows[_sel].Complete))
                    {
                        _box.Text = _rows[_sel].Complete;
                        _box.CaretIndex = _box.Text.Length;
                    }
                    break;
            }
        }

        /// <summary>Up = older, Down = newer history entry in the box.</summary>
        void History(int dir)
        {
            var h = S.RunHistory;
            if (h.Count == 0) return;
            int next = _hist + dir;
            if (next < -1) next = -1;
            if (next >= h.Count) next = h.Count - 1;
            _hist = next;
            _box.Text = next < 0 ? "" : h[next];
            _box.CaretIndex = _box.Text.Length;
            if (next < 0) Suggest();
            _hist = next;
        }

        // =================================================================
        // Suggestions
        // =================================================================
        void Suggest()
        {
            _rows.Clear();
            _list.Children.Clear();
            Status(null);
            string text = _box.Text.Trim();
            if (text.Length == 0)
            {
                int n = 0;
                foreach (string h in S.RunHistory)
                {
                    if (n++ >= 6) break;
                    string hh = h;
                    AddRow("", hh, "recent", delegate { RunRecent(hh); }, hh);
                }
                string bangs = "";
                foreach (RunBang b in S.RunBangs) if (bangs.Length < 60) bangs += "!" + b.Key + " " + b.Name + "   ";
                foreach (Func<BarSettings, string> hint in PluginHints.ToArray()) { string h = hint(S); if (!string.IsNullOrEmpty(h)) bangs += h + "   "; }
                Status(bangs + "=sum   >command   /command palette");
                Select(0);
                return;
            }

            RunAction a = RunParser.Parse(text, S);
            if (a.Kind == "Palette") { PaletteRows(a.Query, text); return; }
            Action<RunPanel, RunAction> pluginRows;
            if (PluginRows.TryGetValue(a.Kind, out pluginRows)) { pluginRows(this, a); return; }
            if (a.Kind != "None") AddRow(GlyphFor(a), a.Display, null, delegate { Execute(text); }, null);
            else if (!string.IsNullOrEmpty(a.Display)) Status(a.Display);

            // apps from the Start menu whose name matches (start of a word first)
            bool appFirst = false;
            if (!text.StartsWith("!") && !text.StartsWith("=") && !text.StartsWith(">"))
            {
                var hits = new List<AppEntry>();
                foreach (AppEntry e in AppCatalog.Get())
                    if (e.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase)) hits.Add(e);
                foreach (AppEntry e in AppCatalog.Get())
                    if (hits.Count < 12 && !hits.Contains(e) && e.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(e);
                int n = 0;
                foreach (AppEntry e in hits)
                {
                    if (n++ >= 5) break;
                    AppEntry app = e;
                    var row = AddRow("", app.Name, "app", delegate { Launch(app); }, app.Name);
                    if (app.Icon != null) SetIcon(row, app.Icon);
                }
                // an app that starts with the text goes first (Enter starts it), before the web search
                if (hits.Count > 0 && hits[0].Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) && _rows.Count > 1 && a.Kind != "Url" && a.Kind != "Open")
                {
                    Row first = _rows[1];
                    _rows.RemoveAt(1);
                    _rows.Insert(0, first);
                    _list.Children.Remove(first.View);
                    _list.Children.Insert(0, first.View);
                    appFirst = true;
                }
            }
            // the command palette's commands that match ("restart jellyfin", "ac 22"); with two words or more and every word found they go
            // first (Enter does the best one) - before the web search
            if (a.Kind == "Run" || (a.Kind == "Url" && a.Query.Length > 0 && !text.StartsWith("!")))
            {
                int from = _rows.Count;
                List<PaletteHit> cmds = Palette.Find(text, S, 4);
                foreach (PaletteHit h in cmds) PaletteRow(h, text);
                if (cmds.Count > 0 && !appFirst && text.Contains(" ") && cmds[0].Full)
                    for (int i = from; i < _rows.Count; i++)
                    {
                        Row r = _rows[i];
                        _rows.RemoveAt(i);
                        _rows.Insert(i - from, r);
                        _list.Children.Remove(r.View);
                        _list.Children.Insert(i - from, r.View);
                    }
            }
            // earlier entries that match
            int hn = 0;
            foreach (string h in S.RunHistory)
            {
                if (hn >= 3) break;
                if (h.Equals(text, StringComparison.OrdinalIgnoreCase) || h.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string hh = h;
                AddRow("", hh, "recent", delegate { RunRecent(hh); }, hh);
                hn++;
            }
            Select(0);
        }

        static string GlyphFor(RunAction a)
        {
            switch (a.Kind)
            {
                case "Command": return "";
                case "Calc": return "";
                case "Open": return "";
                case "Run": return "";
                default: return "";
            }
        }

        [PluginApi] public Row AddRow(string glyph, string text, string tag, Action run, string complete)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.05, Foreground = _t.FgDim, VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBlock { Text = text, Foreground = _t.Fg, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 1);
            g.Children.Add(tb);
            if (!string.IsNullOrEmpty(tag))
            {
                var tt = new TextBlock { Text = tag, Foreground = _t.FgDim, FontSize = Math.Max(8, _t.FontSize * 0.8), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
                Grid.SetColumn(tt, 2);
                g.Children.Add(tt);
            }
            var b = new Border { Child = g, Padding = new Thickness(6, 5, 6, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var row = new Row { View = b, Run = run, Complete = complete };
            b.MouseEnter += delegate { int i = _rows.IndexOf(row); if (i >= 0) Select(i); };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (row.Run != null) row.Run(); };
            _rows.Add(row);
            _list.Children.Add(b);
            return row;
        }

        static void SetIcon(Row row, ImageSource icon)
        {
            var g = row.View.Child as Grid;
            if (g == null || g.Children.Count == 0) return;
            g.Children.RemoveAt(0);
            var img = new Image { Source = icon, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Insert(0, img);
        }

        [PluginApi] public void Select(int i)
        {
            if (_rows.Count == 0) { _sel = -1; return; }
            _sel = Math.Max(0, Math.Min(_rows.Count - 1, i));
            for (int k = 0; k < _rows.Count; k++) _rows[k].View.Background = k == _sel ? _t.ActiveBg : Brushes.Transparent;
        }

        [PluginApi] public void Status(string text)
        {
            _status.Text = text ?? "";
            _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // =================================================================
        // Running
        // =================================================================
        [PluginApi] public void Remember(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            var h = S.RunHistory;
            h.RemoveAll(delegate (string x) { return string.Equals(x, text, StringComparison.OrdinalIgnoreCase); });
            h.Insert(0, text);
            if (h.Count > 50) h.RemoveRange(50, h.Count - 50);
            _mgr.SaveSoon();
        }

        void Execute(string text)
        {
            RunAction a = RunParser.Parse(text, S);
            if (PluginRows.ContainsKey(a.Kind))
            {
                // a plugin's keyword: its rows do the work (from the history: put it in the box so they show)
                if (_box.Text.Trim() != text.Trim()) { _box.Text = text.Trim(); _box.CaretIndex = _box.Text.Length; }
                return;
            }
            switch (a.Kind)
            {
                case "None": return;
                case "Palette":
                    {
                        // from the history: the best command for it (one that asks first is shown in the box to confirm)
                        List<PaletteHit> hits = a.Query.Length == 0 ? new List<PaletteHit>() : Palette.Find(a.Query, S, 1);
                        if (hits.Count == 0) { if (_box.Text.Trim() != text.Trim()) { _box.Text = text.Trim(); _box.CaretIndex = _box.Text.Length; } return; }
                        if (hits[0].Command.Ask || Palette.Problem(hits[0]) != null)
                        {
                            _box.Text = text.Trim();
                            _box.CaretIndex = _box.Text.Length;
                        }
                        RunPalette(hits[0], text.Trim());
                        return;
                    }
                case "Calc":
                    try { Clipboard.SetText(a.Target); } catch { }
                    Remember(text);
                    Status("Copied " + a.Target);
                    return;
            }
            Remember(text);
            _close();
            try
            {
                switch (a.Kind)
                {
                    case "Command":
                        Process.Start(new ProcessStartInfo("cmd.exe", "/k " + a.Target) { UseShellExecute = true });
                        break;
                    case "Run":
                        try { Process.Start(new ProcessStartInfo(a.Target) { UseShellExecute = true }); }
                        catch (Win32Exception)
                        {
                            if (!string.IsNullOrEmpty(a.Fallback)) Process.Start(new ProcessStartInfo(a.Fallback) { UseShellExecute = true });
                        }
                        break;
                    default:
                        Open(a.Target);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Run box '" + text + "': " + ex.Message);
                Questions.Tell(null, "Windows can't open \"" + text + "\".\n\n" + ex.Message, "Run");
            }
        }

        [PluginApi] public static void Open(string target)
        {
            if (SettingsLauncher.IsSettingsUri(target)) { SettingsLauncher.Open(target); return; }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

        /// <summary>
        /// A history entry clicked: an app started from the list is remembered by its name ("Windows PowerShell"), so that name starts the
        /// app again - as typed text it would be a web search. Anything else runs as it was typed.
        /// </summary>
        void RunRecent(string text)
        {
            foreach (AppEntry e in AppCatalog.Get())
                if (string.Equals(e.Name, text, StringComparison.OrdinalIgnoreCase)) { Launch(e); return; }
            Execute(text);
        }

        void Launch(AppEntry app)
        {
            Remember(app.Name);
            _close();
            try { Open(app.Target); }
            catch (Exception ex) { Log.Write("Run box app '" + app.Name + "': " + ex.Message); }
        }

        // ---- the command palette: /words -> only commands; plain text also lists the ones that match ----
        string _palArmed;        // a command that asks first: Enter once arms it (for this text), Enter again does it
        string _palArmedText;

        void PaletteRows(string q, string text)
        {
            if (q.Length == 0)
            {
                string have = Palette.Summary(S);
                Status("Type what to do - restart jellyfin, ac 22, pause the printer, ssh pve1, lock the pc …" +
                       (have.Length > 0 ? "\n" + have : ""));
                return;
            }
            List<PaletteHit> hits = Palette.Find(q, S, 9);
            foreach (PaletteHit h in hits) PaletteRow(h, text);
            if (hits.Count == 0) Status("No command matches " + q + ".");
            else Status(_palArmed != null && _palArmedText == text ? "Enter again to do it, Esc to leave it" : "Enter: do it");
            Select(0);
        }

        Row PaletteRow(PaletteHit h, string text)
        {
            bool armed = _palArmed == h.Command.Key && _palArmedText == text;
            string title = h.Title;
            Row r = AddRow(h.Command.Glyph, armed ? "Press Enter again: " + char.ToLower(title[0]) + title.Substring(1) : title,
                           armed ? "confirm" : h.Command.Detail, delegate { RunPalette(h, text); }, null);
            r.View.ToolTip = title + "\n" + h.Command.Detail + (h.Command.Ask ? "\nAsks first: Enter twice" : "");
            r.Command = h.Command.Key;
            return r;
        }

        async void RunPalette(PaletteHit h, string text)
        {
            PaletteCommand c = h.Command;
            string why = Palette.Problem(h);
            if (why != null) { Status(why); return; }
            if (c.Ask && !(_palArmed == c.Key && _palArmedText == text))
            {
                _palArmed = c.Key; _palArmedText = text;
                Suggest();
                int at = _rows.FindIndex(delegate (Row x) { return x.Command == c.Key; });
                Select(at >= 0 ? at : 0);
                return;
            }
            _palArmed = null;
            Remember(text.StartsWith("/") ? text : "/" + text);     // from the history it finds the command again
            if (c.Opens)
            {
                _close();
                string e = await Palette.RunAsync(h);
                string done;
                if (e != null && !PaletteCommand.IsWorked(e, out done)) Questions.Tell(null, h.Title + ": " + e, "vShell");
                return;
            }
            string before = _box.Text;
            Status(h.Title + "…");
            string err = await Palette.RunAsync(h);
            string said;
            bool worked = PaletteCommand.IsWorked(err, out said);
            if (err != null && !worked) { Status(c.Source + ": " + err); return; }
            // what the command said it did ("Restarting jellyfin (job 413)") stays a little longer, to be read
            Status(string.IsNullOrEmpty(said) ? h.Title + " ✓" : said + " ✓");
            await System.Threading.Tasks.Task.Delay(string.IsNullOrEmpty(said) ? 800 : 2000);
            if (_box.Text == before) _close();      // not when you've started typing something else
        }
    }
}
