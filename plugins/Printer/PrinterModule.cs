using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// 3D printer (Klipper + Moonraker - Fluidd / Mainsail). On a bar: a dot, the progress, time left and the temperatures - click it for
    /// the printer panel. In a widget: the panel itself (the print with its thumbnail, pause / resume / cancel / emergency stop, the webcam,
    /// temperatures with preheat presets, the Spoolman spool, power devices, and Files / History / Queue / Macros / Console).
    /// </summary>
    public sealed class PrinterModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shownKey;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public PrinterModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            Padding = host.IsWidget ? new Thickness(4, 2, 4, 2) : new Thickness(6, 0, 6, 0);
            VerticalAlignment = VerticalAlignment.Center;
            if (host.IsWidget)
            {
                Child = new PrinterView(mgr, _t, cfg, 0);
                HorizontalAlignment = HorizontalAlignment.Stretch;
                // hide while the printer is off (when chosen) - the widget hides too when nothing else in it shows
                _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
                _timer.Tick += delegate { SyncOffline(); };
                Loaded += delegate { _timer.Start(); SyncOffline(); };
                Unloaded += delegate { _timer.Stop(); };
                SyncOffline();
                return;
            }
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(false); };
            Loaded += delegate { Refresh(true); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh(true);
        }

        /// <summary>The printers this module shows.</summary>
        public static List<PrinterState> Mine(ModuleConfig cfg)
        {
            List<PrinterState> all = Moonraker.All();
            string want = PtView.Of(cfg).Printer;
            if (string.IsNullOrEmpty(want)) return all;
            List<PrinterState> one = all.FindAll(delegate (PrinterState p) { return p.PrinterId == want; });
            return one.Count > 0 ? one : all;
        }

        /// <summary>
        /// Every printer it shows can't be reached (for half a minute, or since vShell started): nothing to show. A printer that's
        /// still connecting doesn't count as off.
        /// </summary>
        public static bool AllOffline(List<PrinterState> ps)
        {
            if (ps.Count == 0) return false;
            foreach (PrinterState p in ps)
            {
                bool off = p.Error.Length > 0 && (!p.Ok || p.Fetched < DateTime.UtcNow.AddSeconds(-30));
                if (!off) return false;
            }
            return true;
        }

        void SyncOffline()
        {
            bool hide = PtView.Of(_cfg).HideOffline && Moonraker.Configured(_mgr.Settings) && AllOffline(Mine(_cfg));
            Visibility want = hide ? Visibility.Collapsed : Visibility.Visible;
            if (Visibility == want) return;
            Visibility = want;
            _host.ContentChanged();
        }

        void Refresh(bool force)
        {
            SyncOffline();
            List<PrinterState> ps = Mine(_cfg);
            var k = new System.Text.StringBuilder(Moonraker.Configured(_mgr.Settings) ? "c" : "n");
            foreach (PrinterState p in ps)
            {
                // what the bar shows (not every read): state, progress, time left, temperatures
                PrHeater n = p.Heater("extruder"), b = p.Heater("heater_bed");
                k.Append("|" + p.PrinterId + p.Ok + p.Error + p.Klippy + p.State + Math.Round(p.Progress * 100) + "|" + (p.Busy ? (int)(p.Remaining / 60) : 0) + "|" +
                         (n != null ? Math.Round(n.Temp) + "/" + n.Target : "") + (b != null ? Math.Round(b.Temp) + "/" + b.Target : "") + p.Health);
            }
            string key = k.ToString();
            if (!force && key == _shownKey) return;
            _shownKey = key;
            Child = BuildBar(ps);
            _host.ContentChanged();
        }

        static TextBlock T(Theme t, string text, Brush b)
        {
            return new TextBlock { Text = text, Foreground = b, FontFamily = t.Font, FontSize = t.FontSize, VerticalAlignment = VerticalAlignment.Center };
        }

        FrameworkElement BuildBar(List<PrinterState> ps)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!Moonraker.Configured(_mgr.Settings)) { row.Children.Add(T(_t, "Set up a printer in Settings > Plugins", _t.FgDim)); ToolTip = null; return row; }
            if (ps.Count == 0) { row.Children.Add(T(_t, "Printer…", _t.FgDim)); return row; }
            var tip = new List<string>();
            bool many = ps.Count > 1;
            string title = _cfg.Title ?? "";
            if (title.Length > 0) row.Children.Add(T(_t, title + " ", _t.FgDim));
            for (int i = 0; i < ps.Count; i++)
            {
                PrinterState p = ps[i];
                if (i > 0) row.Children.Add(T(_t, "   ", _t.FgDim));
                double d = Math.Round(_t.FontSize * 0.6);
                row.Children.Add(new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = PrinterView.HealthBrush(p.Health, _t), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                if (many) row.Children.Add(T(_t, p.PrinterName + " ", _t.FgDim));
                if (p.Ok && p.Ready && p.Busy)
                {
                    row.Children.Add(T(_t, Math.Round(p.Progress * 100) + "%", p.Paused ? (Brush)StatusColors.Warn : _t.Fg));
                    if (p.Paused) row.Children.Add(T(_t, " paused", StatusColors.Warn));
                    double left = p.Remaining;
                    if (left > 0 && PtView.Of(_cfg).BarTime != "None")
                    {
                        string when = TimeText.Clock(DateTime.Now.AddSeconds(left));
                        string tt = PtView.Of(_cfg).BarTime == "Finish" ? " · " + when : PtView.Of(_cfg).BarTime == "Both" ? " · " + Moonraker.Duration(left) + " · " + when : " · " + Moonraker.Duration(left);
                        row.Children.Add(T(_t, tt, _t.FgDim));
                    }
                }
                else row.Children.Add(T(_t, p.StateText, p.Health == "bad" ? (Brush)StatusColors.Bad : _t.FgDim));
                if (PtView.Of(_cfg).BarTemps && p.Ok && p.Ready)
                {
                    PrHeater n = p.Heater("extruder"), b = p.Heater("heater_bed");
                    string temps = (n != null ? "  " + Math.Round(n.Temp) + "°" : "") + (b != null ? (n != null ? " / " : "  ") + Math.Round(b.Temp) + "°" : "");
                    if (temps.Length > 0) row.Children.Add(T(_t, temps, (n != null && n.Target > 0) || (b != null && b.Target > 0) ? _t.MainAccent : _t.FgDim));
                }
                tip.Add(PrinterView.Summary(p));
            }
            tip.Add("Click for the printer");
            ToolTip = string.Join("\n\n", tip);
            return row;
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            Moonraker.Poke(null);      // a printer that was off is tried again now, not at its next (up to a minute away) try
            _popup = new PopupWindow(_mgr, _host, this, _t, new PrinterView(_mgr, _t, _cfg, 470));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }
    }

    /// <summary>The printer panel (popup and widget).</summary>
    public sealed class PrinterView : Border
    {
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly ModuleConfig _cfg;
        readonly DispatcherTimer _timer, _camTimer;
        readonly StackPanel _root = new StackPanel();
        readonly Border _head = new Border(), _status = new Border(), _temps = new Border(), _extras = new Border(), _listHost = new Border();
        readonly Border _camBox = new Border { Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        // the webcam's title line: click it to fold the picture away (remembered per module) or back
        readonly Border _camHead = new Border { Margin = new Thickness(0, 8, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        readonly TextBlock _camTitle = new TextBlock(), _camChev = new TextBlock();
        readonly Image _cam = new Image { Stretch = Stretch.Uniform };
        // under the picture: "Loading the webcam…" / why it can't be shown (until a picture comes)
        readonly TextBlock _camMsg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        readonly WrapPanel _tabs = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        string _sel = "", _tab = "Files";
        string _headKey, _statusKey, _tempKey, _extrasKey, _listKey;
        bool _camBusy;
        string _camFor = "";
        static bool _menuOpen;

        sealed class Typing : ITypingModule { public void StopTyping() { } }
        readonly Typing _typing = new Typing();

        // console (kept while the panel lives)
        readonly TextBox _gcode = new TextBox();
        readonly StackPanel _consoleLines = new StackPanel();

        static readonly Dictionary<string, ImageSource> Pics = new Dictionary<string, ImageSource>();
        static readonly HashSet<string> Loading = new HashSet<string>();

        public PrinterView(BarManager mgr, Theme t, ModuleConfig cfg, double width)
        {
            _mgr = mgr; _t = t; _cfg = cfg;
            if (width > 0) { Width = width; Margin = new Thickness(4); }
            _root.Children.Add(_head);
            _root.Children.Add(_status);
            var camStack = new StackPanel();
            camStack.Children.Add(_cam);
            camStack.Children.Add(_camMsg);
            _camBox.Child = camStack;
            _camBox.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { if (_camStream.Length > 0) e.Handled = true; };
            _camMsg.FontFamily = t.Font; _camMsg.FontSize = Math.Max(7, t.FontSize * 0.82); _camMsg.Foreground = t.FgDim;
            var ch = new DockPanel();
            _camChev.FontFamily = IconUtil.GlyphFont; _camChev.FontSize = Math.Max(8, t.FontSize * 0.7); _camChev.Foreground = t.FgDim;
            _camChev.VerticalAlignment = VerticalAlignment.Center; _camChev.Margin = new Thickness(6, 0, 2, 0);
            DockPanel.SetDock(_camChev, Dock.Right);
            ch.Children.Add(_camChev);
            _camTitle.FontFamily = t.Font; _camTitle.FontSize = Math.Max(7, t.FontSize * 0.85); _camTitle.Foreground = t.FgDim; _camTitle.VerticalAlignment = VerticalAlignment.Center;
            ch.Children.Add(_camTitle);
            _camHead.Child = ch;
            _camHead.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            _camHead.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                PtView.Of(_cfg).CamFolded = !PtView.Of(_cfg).CamFolded;
                _mgr.SaveSoon();
                Refresh(true);
                CamTick();
            };
            _root.Children.Add(_camHead);
            _root.Children.Add(_camBox);
            _root.Children.Add(_temps);
            _root.Children.Add(_extras);
            _root.Children.Add(_tabs);
            _root.Children.Add(_listHost);
            Child = _root;
            _gcode.FontFamily = new FontFamily("Consolas"); _gcode.FontSize = Math.Max(10, t.FontSize * 0.9);
            _gcode.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; SendGcode(); } };
            // in a widget (which never takes the keyboard): ask it for the keyboard while typing in the console box
            _gcode.PreviewMouseLeftButtonDown += delegate
            {
                var ww = Window.GetWindow(this) as WidgetWindow;
                if (ww == null) return;
                ww.BeginTyping(_typing);
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { _gcode.Focus(); Keyboard.Focus(_gcode); }));
            };
            _gcode.LostKeyboardFocus += delegate
            {
                var ww = Window.GetWindow(this) as WidgetWindow;
                if (ww != null) ww.EndTyping(_typing);
            };

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(false); };
            // every second: keep the live stream going (or stopped), or take a snapshot when one is due
            _camTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _camTimer.Tick += delegate { CamTick(); };
            Loaded += delegate { Refresh(true); _timer.Start(); _camTimer.Start(); CamTick(); };
            Unloaded += delegate { _timer.Stop(); _camTimer.Stop(); StopLive(); };
            IsVisibleChanged += delegate { if (!IsVisible) StopLive(); else CamTick(); };
            Refresh(true);
        }

        // =================================================================
        // Helpers
        // =================================================================
        public static Brush HealthBrush(string health, Theme t)
        {
            switch (health) { case "good": return StatusColors.Good; case "busy": return StatusColors.Warn; case "bad": return StatusColors.Bad; default: return t.FgDim; }
        }

        /// <summary>A few lines about a printer (tooltips).</summary>
        public static string Summary(PrinterState p)
        {
            var l = new List<string> { p.PrinterName + ": " + p.StateText };
            if (p.Klippy == "shutdown" || p.Klippy == "error") { if (p.KlippyMessage.Length > 0) l.Add(p.KlippyMessage.Split('\n')[0]); }
            else if (p.Busy)
            {
                l.Add(Moonraker.Short(p.File) + " - " + Math.Round(p.Progress * 100) + "%" + (p.Layers > 0 ? ", layer " + p.Layer + " of " + p.Layers : ""));
                double left = p.Remaining;
                if (left > 0) l.Add(Moonraker.Duration(left) + " left - done at " + TimeText.Clock(DateTime.Now.AddSeconds(left)));
            }
            foreach (PrHeater h in p.Heaters)
                if (h.IsHeater) l.Add(h.Label + " " + Math.Round(h.Temp) + " °C" + (h.Target > 0 ? " → " + Math.Round(h.Target) : ""));
            if (p.Spool != null) l.Add("Spool: " + p.Spool.Label + (p.Spool.Remaining >= 0 ? ", " + Math.Round(p.Spool.Remaining) + " g left" : ""));
            if (p.Error.Length > 0) l.Add("Last update failed: " + p.Error);
            return string.Join("\n", l);
        }

        TextBlock T(string text, Brush b, double scale = 1, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = _t.Font, FontSize = Math.Max(7, _t.FontSize * scale), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        static Border Dot(Brush b, double size)
        {
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        }

        Border Pill(string glyph, string text, string tip, Action click, bool primary = false)
        {
            var b = Pills.Make(_t, glyph, text, tip, primary, click);
            b.Margin = new Thickness(0, 0, 6, 4);
            return b;
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open " + url + ": " + ex.Message); }
        }

        PrinterState Current(List<PrinterState> ps)
        {
            if (ps.Count == 0) return null;
            PrinterState p = ps.Find(delegate (PrinterState x) { return x.PrinterId == _sel; });
            if (p == null) { p = ps[0]; _sel = p.PrinterId; }
            return p;
        }

        /// <summary>Ask first (on top of everything when this is in a widget); the popup stays open meanwhile.</summary>
        bool Ask(string question)
        {
            Window owner = Window.GetWindow(this);
            var host = owner as PopupWindow;
            if (host != null) host.Pinned = true;
            _menuOpen = true;
            try { return Questions.YesNo(owner, question, "3D printer") == MessageBoxResult.Yes; }
            finally { if (host != null) host.Pinned = false; _menuOpen = false; }
        }

        /// <summary>Run an action; its error (if any) is shown.</summary>
        async void Run(Func<Task<string>> act)
        {
            string err;
            try { err = await act(); } catch (Exception ex) { err = ex.Message; }
            if (!string.IsNullOrEmpty(err)) Questions.Tell(null, err, "3D printer", MessageBoxImage.Warning);
            Moonraker.Poke(_sel);
            Refresh(true);
        }

        void Menu(FrameworkElement target, ContextMenu m)
        {
            Window owner = Window.GetWindow(target);
            var host = owner as PopupWindow;
            if (host != null) host.Pinned = true;
            _menuOpen = true;
            m.Closed += delegate
            {
                if (host != null) host.Pinned = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { _menuOpen = false; }));
            };
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        static MenuItem MI(string text, Action click, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += delegate { click(); };
            return mi;
        }

        /// <summary>A thumbnail / picture, loaded once in the background; the panel redraws when it arrives.</summary>
        ImageSource Pic(string printerId, string thumbPath, int width)
        {
            if (string.IsNullOrEmpty(thumbPath)) return null;
            string key = printerId + "|" + thumbPath;
            lock (Pics)
            {
                ImageSource img;
                if (Pics.TryGetValue(key, out img)) return img;
                if (Loading.Contains(key)) return null;
                Loading.Add(key);
            }
            Task.Run(async delegate
            {
                byte[] b = await Moonraker.ThumbAsync(printerId, thumbPath);
                ImageSource img = Decode(b, width);
                lock (Pics) { Pics[key] = img; Loading.Remove(key); if (Pics.Count > 400) Pics.Clear(); }
                _ = Dispatcher.BeginInvoke(new Action(delegate { _statusKey = null; _listKey = null; Refresh(true); }));
            });
            return null;
        }

        static ImageSource Decode(byte[] data, int width)
        {
            if (data == null || data.Length < 50) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(data);
                if (width > 0) bi.DecodePixelWidth = width;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        FrameworkElement Thumb(ImageSource img, double size)
        {
            var b = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(5), Background = Theme.Faded(_t.Fg, 8), Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true
            };
            if (img != null) b.Child = new Image { Source = img, Stretch = Stretch.Uniform };
            else b.Child = new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = size * 0.35, Foreground = _t.FgDim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return b;
        }

        FrameworkElement Bar(double frac, Brush fill, double height)
        {
            var g = new Grid { Height = height, Margin = new Thickness(0, 5, 0, 5) };
            frac = Math.Max(0, Math.Min(1, frac));
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(height / 2), Background = Theme.Faded(_t.Fg, 12) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (frac > 0) g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = fill });
            return g;
        }

        /// <summary>A small temperature graph.</summary>
        FrameworkElement Spark(double[] values, Brush stroke, double w, double h)
        {
            var c = new Canvas { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            if (values == null || values.Length < 2) return c;
            double min = double.MaxValue, max = double.MinValue;
            foreach (double v in values) { min = Math.Min(min, v); max = Math.Max(max, v); }
            if (max - min < 4) { double mid = (max + min) / 2; min = mid - 2; max = mid + 2; }
            var pl = new Polyline { Stroke = stroke, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < values.Length; i++)
                pl.Points.Add(new Point(w * i / (values.Length - 1), h - 1 - (h - 2) * (values[i] - min) / (max - min)));
            c.Children.Add(pl);
            return c;
        }

        // =================================================================
        // Refresh
        // =================================================================
        void Refresh(bool force)
        {
            if (_menuOpen) return;
            List<PrinterState> ps = PrinterModule.Mine(_cfg);
            if (!Moonraker.Configured(_mgr.Settings) || ps.Count == 0)
            {
                string k = "none";
                if (_headKey == k && !force) return;
                _headKey = k; _statusKey = _tempKey = _extrasKey = _listKey = null;
                var none = T(Moonraker.Configured(_mgr.Settings) ? "Connecting to the printer…" : "Set up your printer on the Plugins page of Settings (its Fluidd / Moonraker address).", _t.FgDim);
                none.TextWrapping = TextWrapping.Wrap;
                _head.Child = none;
                _status.Child = null; _temps.Child = null; _extras.Child = null; _listHost.Child = null; _tabs.Children.Clear();
                _camBox.Visibility = Visibility.Collapsed;
                _camHead.Visibility = Visibility.Collapsed;
                return;
            }
            PrinterState p = Current(ps);
            BuildHead(ps, p, force);
            BuildStatus(p, force);
            UpdateCamBox(p);
            BuildTemps(p, force);
            BuildExtras(p, force);
            BuildLists(p, force);
        }

        void BuildHead(List<PrinterState> ps, PrinterState p, bool force)
        {
            var k = new System.Text.StringBuilder(_sel + "|" + PtView.Of(_cfg).ShowFluidd);
            foreach (PrinterState x in ps) k.Append("|" + x.PrinterId + x.PrinterName + x.Health);
            string key = k.ToString();
            if (!force && key == _headKey) return;
            _headKey = key;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (PtView.Of(_cfg).ShowFluidd)
            {
                var web = Pill("", "Fluidd", "Open " + p.WebUrl, delegate { OpenUrl(p.WebUrl); });
                web.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(web, Dock.Right);
                head.Children.Add(web);
            }
            if (ps.Count > 1)
            {
                // one pill per printer
                var pick = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                foreach (PrinterState x in ps)
                {
                    PrinterState me = x;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(Dot(HealthBrush(x.Health, _t), 7));
                    row.Children.Add(T(x.PrinterName, x.PrinterId == _sel ? _t.Fg : _t.FgDim, 0.95, x.PrinterId == _sel));
                    var b = new Border
                    {
                        Child = row, Padding = new Thickness(8, 3, 10, 3), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
                        Background = x.PrinterId == _sel ? (Brush)_t.Hover : Brushes.Transparent
                    };
                    b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };      // (in a widget the press would start a drag)
                    b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; _sel = me.PrinterId; _camFor = ""; _cam.Source = null; Refresh(true); CamTick(); };
                    pick.Children.Add(b);
                }
                head.Children.Add(pick);
            }
            else
            {
                var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                title.Children.Add(Dot(HealthBrush(p.Health, _t), 8));
                title.Children.Add(T(p.PrinterName, _t.Fg, 1, true));
                head.Children.Add(title);
            }
            _head.Child = head;
        }

        // ---- the print ----------------------------------------------------------------------------------------
        void BuildStatus(PrinterState p, bool force)
        {
            // what this part shows (an idle printer doesn't redraw every read)
            string key = p.PrinterId + "|" + p.Ok + "|" + p.Error + "|" + p.Klippy + "|" + p.KlippyMessage + "|" + p.State + "|" + p.Message + "|" + p.File + "|" +
                         Math.Round(p.Progress, 3) + "|" + p.Layer + "/" + p.Layers + "|" + Math.Round(p.PrintDuration / 10) + "|" + Math.Round(p.FilamentUsed / 100) + "|" +
                         Math.Round(p.Fan, 2) + "|" + (p.Meta != null ? p.Meta.Thumb + p.Meta.Estimated : "") + "|" + (p.History.Count > 0 ? p.History[0].Id + p.History[0].Status : "") +
                         "|" + (p.Busy ? (int)(p.Remaining / 60) : 0);
            if (!force && key == _statusKey) return;
            _statusKey = key;
            var box = new StackPanel();
            string id = p.PrinterId;

            if (!p.Ok)
            {
                var e = T(p.Error.Length > 0 ? "Can't reach " + p.PrinterName + ": " + p.Error : "Connecting to " + p.PrinterName + "…", p.Error.Length > 0 ? StatusColors.Bad : _t.FgDim);
                e.TextWrapping = TextWrapping.Wrap;
                box.Children.Add(e);
                _status.Child = box;
                return;
            }
            if (p.Error.Length > 0)
            {
                var e = T("Last update failed: " + p.Error, StatusColors.Warn, 0.85);
                e.TextWrapping = TextWrapping.Wrap;
                box.Children.Add(e);
            }

            // Klipper isn't ready: why, and the restarts
            if (!p.Ready)
            {
                var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
                st.Children.Add(Dot(HealthBrush(p.Health, _t), 9));
                st.Children.Add(T(p.StateText, p.Health == "bad" ? StatusColors.Bad : _t.Fg, 1.05, true));
                box.Children.Add(st);
                if (p.KlippyMessage.Length > 0)
                {
                    var m = T(p.KlippyMessage, _t.FgDim, 0.85);
                    m.TextWrapping = TextWrapping.Wrap; m.TextTrimming = TextTrimming.None;
                    box.Children.Add(m);
                }
                var acts = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                acts.Children.Add(Pill("", "Restart Klipper", "Restart Klipper (reloads printer.cfg)", delegate { Run(delegate { return Moonraker.KlipperRestartAsync(id); }); }));
                acts.Children.Add(Pill("", "Firmware restart", "Restart Klipper and its MCUs (after a shutdown)", delegate { if (Ask("Firmware restart " + p.PrinterName + "?")) Run(delegate { return Moonraker.FirmwareRestartAsync(id); }); }));
                box.Children.Add(acts);
                _status.Child = box;
                return;
            }

            // a print: thumbnail, name, progress, times, controls
            bool job = p.File.Length > 0 && (p.Busy || p.State == "complete" || p.State == "cancelled" || p.State == "error");
            if (job)
            {
                var g = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                var thumb = Thumb(p.Meta != null ? Pic(id, p.Meta.Thumb, 300) : null, 92);
                DockPanel.SetDock(thumb, Dock.Left);
                g.Children.Add(thumb);
                var info = new StackPanel();
                var top = new StackPanel { Orientation = Orientation.Horizontal };
                Brush stc = p.Health == "bad" ? StatusColors.Bad : p.Paused ? StatusColors.Warn : p.Printing ? (Brush)StatusColors.Good : _t.FgDim;
                top.Children.Add(T(p.StateText, stc, 0.9, true));
                if (p.Busy) top.Children.Add(T("   " + Math.Round(p.Progress * 100) + "%" + (p.Layers > 0 ? " · layer " + p.Layer + " of " + p.Layers : ""), _t.FgDim, 0.9));
                info.Children.Add(top);
                var name = T(Moonraker.Short(p.File), _t.Fg, 1, true);
                name.ToolTip = p.File;
                info.Children.Add(name);
                info.Children.Add(Bar(p.State == "complete" ? 1 : p.Progress, p.Paused ? StatusColors.Warn : p.State == "complete" ? (Brush)StatusColors.Good : _t.MainAccent, 6));
                double left = p.Remaining;
                if (p.Busy && left > 0)
                    info.Children.Add(T(Moonraker.Duration(left) + " left · done at " + TimeText.Clock(DateTime.Now.AddSeconds(left)), _t.Fg, 0.9));
                var used = new List<string>();
                if (p.PrintDuration > 0) used.Add((p.Busy ? "Printing for " : "Took ") + Moonraker.Duration(p.PrintDuration));
                if (p.FilamentUsed > 0) used.Add(Moonraker.Filament(p.FilamentUsed) + (p.Meta != null && p.Meta.FilamentTotal > 0 ? " of " + Moonraker.Filament(p.Meta.FilamentTotal) : ""));
                if (p.Fan >= 0 && p.Busy) used.Add("fan " + Math.Round(p.Fan * 100) + "%");
                if (used.Count > 0) info.Children.Add(T(string.Join(" · ", used), _t.FgDim, 0.85));
                if (p.Message.Length > 0 && (p.State == "error" || p.Paused))
                {
                    var m = T(p.Message, p.State == "error" ? StatusColors.Bad : StatusColors.Warn, 0.85);
                    m.TextWrapping = TextWrapping.Wrap; m.TextTrimming = TextTrimming.None;
                    info.Children.Add(m);
                }
                g.Children.Add(info);
                box.Children.Add(g);
            }
            else
            {
                var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                st.Children.Add(T("Ready", _t.Fg, 1, true));
                if (p.History.Count > 0)
                {
                    PrJob last = p.History[0];
                    st.Children.Add(T("   last: " + Moonraker.Short(last.File) + " (" + JobStatus(last.Status) + ")", _t.FgDim, 0.85));
                }
                box.Children.Add(st);
            }

            // controls
            var c = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (p.Printing) c.Children.Add(Pill("", "Pause", "Pause the print", delegate { Run(delegate { return Moonraker.PauseAsync(id); }); }));
            if (p.Paused) c.Children.Add(Pill("", "Resume", "Resume the print", delegate { Run(delegate { return Moonraker.ResumeAsync(id); }); }, true));
            if (p.Busy) c.Children.Add(Pill("", "Cancel", "Cancel the print", delegate { if (Ask("Cancel printing " + Moonraker.Short(p.File) + " on " + p.PrinterName + "?")) Run(delegate { return Moonraker.CancelAsync(id); }); }));
            if (!p.Busy && p.File.Length > 0 && (p.State == "complete" || p.State == "cancelled" || p.State == "error"))
            {
                string f = p.File;
                c.Children.Add(Pill("", "Print again", "Start " + Moonraker.Short(f) + " again", delegate { if (Ask("Print " + Moonraker.Short(f) + " again on " + p.PrinterName + "?")) Run(delegate { return Moonraker.StartPrintAsync(id, f); }); }));
            }
            var stop = Pill("", "Emergency stop", "Stops everything at once (Klipper shuts down - firmware restart after)", delegate
            {
                if (Ask("EMERGENCY STOP " + p.PrinterName + "?\n\nKlipper shuts down straight away (heaters and motors off). It needs a firmware restart after.")) Run(delegate { return Moonraker.EmergencyStopAsync(id); });
            });
            stop.Opacity = p.Busy ? 1 : 0.75;
            c.Children.Add(stop);
            box.Children.Add(c);
            _status.Child = box;
        }

        static string JobStatus(string s)
        {
            switch (s)
            {
                case "completed": return "done";
                case "cancelled": return "cancelled";
                case "error": return "failed";
                case "klippy_shutdown": return "Klipper shut down";
                case "klippy_disconnect": return "Klipper disconnected";
                case "server_exit": return "interrupted";
                case "in_progress": return "printing";
                default: return s.Replace('_', ' ');
            }
        }

        // ---- the webcam ---------------------------------------------------------------------------------------
        PrCam Cam(PrinterState p)
        {
            if (!PtView.Of(_cfg).ShowCam || p == null || p.Cams.Count == 0) return null;
            PrinterConn c = Moonraker.Conn(p.PrinterId);
            string want = c != null ? c.Webcam ?? "" : "";
            if (want == "Off") return null;
            PrCam cam = want.Length > 0 ? p.Cams.Find(delegate (PrCam x) { return string.Equals(x.Name, want, StringComparison.OrdinalIgnoreCase); }) : null;
            return cam ?? p.Cams[0];
        }

        void UpdateCamBox(PrinterState p)
        {
            PrCam cam = Cam(p);
            bool folded = PtView.Of(_cfg).CamFolded;
            _camHead.Visibility = cam == null ? Visibility.Collapsed : Visibility.Visible;
            _camBox.Visibility = cam == null || folded ? Visibility.Collapsed : Visibility.Visible;
            if (cam == null) return;
            _camTitle.Text = "Webcam · " + cam.Name + (folded ? "  (click to show)" : "");
            _camChev.Text = folded ? "\uE76C" : "\uE70D";
            _camHead.ToolTip = folded ? "Show the webcam" : "Fold the webcam away";
            if (folded) return;
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(cam.FlipH ? -1 : 1, cam.FlipV ? -1 : 1));
            if (cam.Rotation != 0) tg.Children.Add(new RotateTransform(cam.Rotation));
            _cam.LayoutTransform = tg;
            _cam.MaxHeight = Width > 0 ? Width * 0.75 : 360;
            _camBox.ToolTip = cam.Name + (cam.Stream.Length > 0 ? " - click for the live stream" : "");
            _camBox.Cursor = cam.Stream.Length > 0 ? Cursors.Hand : null;
            string stream = cam.Stream;
            _camBox.MouseLeftButtonUp -= CamClick;
            _camBox.MouseLeftButtonUp += CamClick;
            _camStream = stream;
            if (_cam.Source == null)
            {
                string why = Moonraker.SnapshotError(p.PrinterId);
                _camMsg.Text = why.Length > 0 ? "Webcam \"" + cam.Name + "\" can't be shown: " + why : "Loading the webcam \"" + cam.Name + "\"…";
                _camMsg.Visibility = Visibility.Visible;
            }
            else _camMsg.Visibility = Visibility.Collapsed;
            if (_cam.Source == null && !_camBusy) CamTick();
        }

        string _camStream = "";
        void CamClick(object s, MouseButtonEventArgs e) { if (_camStream.Length > 0) { e.Handled = true; OpenUrl(_camStream); } }

        // ---- live (the MJPEG stream) ----
        System.Threading.CancellationTokenSource _live;
        string _liveFor = "";
        DateTime _liveFailUntil = DateTime.MinValue, _lastFrameAt = DateTime.MinValue, _lastSnapAt = DateTime.MinValue;
        int _decoding;

        PrCam ShownCam(out string id)
        {
            id = "";
            List<PrinterState> ps = PrinterModule.Mine(_cfg);
            PrinterState p = ps.Find(delegate (PrinterState x) { return x.PrinterId == _sel; });
            if (p != null) id = p.PrinterId;
            return Cam(p);
        }

        void CamTick()
        {
            string id;
            PrCam cam = ShownCam(out id);
            bool shown = cam != null && IsVisible && !PtView.Of(_cfg).CamFolded;
            bool live = shown && PtView.Of(_cfg).CamMode == "Live" && cam.Stream.StartsWith("http", StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow > _liveFailUntil;
            if (!live) StopLive();
            else
            {
                string key = id + "|" + cam.Name + "|" + cam.Stream;
                if (_live == null || _liveFor != key) StartLive(id, cam, key);
                return;
            }
            // snapshots (chosen, or the stream isn't working): one when due
            if (shown && (DateTime.UtcNow - _lastSnapAt).TotalSeconds >= Math.Max(1, PtView.Of(_cfg).CamSeconds) - 0.1) LoadCam();
        }

        void StartLive(string id, PrCam cam, string key)
        {
            StopLive();
            var cts = new System.Threading.CancellationTokenSource();
            _live = cts; _liveFor = key;
            int fps = Math.Max(1, Math.Min(30, PtView.Of(_cfg).CamFps));
            int width = (int)Math.Max(320, Math.Min(1280, (Width > 0 ? Width : ActualWidth > 0 ? ActualWidth : 640) * 1.25));
            string name = cam.Name;
            Task.Run(async delegate
            {
                string err = null;
                try
                {
                    await Moonraker.StreamAsync(id, cam, delegate (byte[] frame)
                    {
                        // at most fps a second, and never more than one picture being decoded / shown at a time
                        DateTime now = DateTime.UtcNow;
                        if ((now - _lastFrameAt).TotalMilliseconds < 1000.0 / fps - 5) return;
                        if (System.Threading.Interlocked.Exchange(ref _decoding, 1) == 1) return;
                        _lastFrameAt = now;
                        ImageSource img = Decode(frame, width);
                        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                        {
                            System.Threading.Interlocked.Exchange(ref _decoding, 0);
                            if (cts.IsCancellationRequested || img == null || _sel != id) return;
                            _cam.Source = img;
                            _camMsg.Visibility = Visibility.Collapsed;
                        }));
                    }, cts.Token);
                }
                catch (Exception ex) { if (!cts.IsCancellationRequested) err = ex is TaskCanceledException ? "no answer" : ex.InnerException != null ? ex.InnerException.Message : ex.Message; }
                _ = Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_live != cts) return;
                    _live = null;
                    if (err != null)
                    {
                        // the stream doesn't work: snapshots for a minute, then try it again
                        Log.Write("Printer webcam \"" + name + "\" live stream: " + err + " - snapshots for now");
                        _liveFailUntil = DateTime.UtcNow.AddSeconds(60);
                    }
                }));
            });
        }

        void StopLive()
        {
            if (_live == null) return;
            try { _live.Cancel(); } catch { }
            _live = null;
            _liveFor = "";
        }

        void LoadCam()
        {
            if (_camBusy || !IsVisible || PtView.Of(_cfg).CamFolded) return;
            List<PrinterState> ps = PrinterModule.Mine(_cfg);
            PrinterState p = ps.Find(delegate (PrinterState x) { return x.PrinterId == _sel; });
            PrCam cam = Cam(p);
            if (cam == null || (cam.Snapshot.Length == 0 && cam.Stream.Length == 0)) return;
            if (_live != null) return;          // the live stream is showing it
            _lastSnapAt = DateTime.UtcNow;
            _camBusy = true;
            string id = p.PrinterId, forKey = id + "|" + cam.Name;
            Task.Run(async delegate
            {
                byte[] b = await Moonraker.SnapshotAsync(id, cam);
                ImageSource img = Decode(b, 960);
                _ = Dispatcher.BeginInvoke(new Action(delegate
                {
                    _camBusy = false;
                    if (img != null && _sel == id) { _cam.Source = img; _camFor = forKey; _camMsg.Visibility = Visibility.Collapsed; }
                    else if (img == null && _cam.Source == null)
                    {
                        string why = Moonraker.SnapshotError(id);
                        if (why.Length > 0) { _camMsg.Text = "Webcam \"" + cam.Name + "\" can't be shown: " + why; _camMsg.Visibility = Visibility.Visible; }
                    }
                }));
            });
        }

        // ---- temperatures -------------------------------------------------------------------------------------
        void BuildTemps(PrinterState p, bool force)
        {
            if (!PtView.Of(_cfg).ShowTemps || !p.Ok || !p.Ready) { _temps.Child = null; _tempKey = null; return; }
            var k = new System.Text.StringBuilder(p.PrinterId);
            foreach (PrHeater h in p.Heaters) k.Append("|" + h.Name + Math.Round(h.Temp, 1) + "/" + h.Target);
            string key = k.ToString();
            if (!force && key == _tempKey) return;
            _tempKey = key;
            string id = p.PrinterId;
            var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var sensors = new List<string>();
            foreach (PrHeater h in p.Heaters)
            {
                if (!h.IsHeater) { if (sensors.Count < 5) sensors.Add(h.Label + " " + Math.Round(h.Temp) + "°"); continue; }
                PrHeater me = h;
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var set = Pill("", "Set", "Set the " + h.Label.ToLowerInvariant() + " temperature", delegate { SetTemp(id, me); });
                set.Margin = new Thickness(0, 0, 0, 0);
                DockPanel.SetDock(set, Dock.Right);
                row.Children.Add(set);
                var lab = T(h.Label, _t.FgDim, 0.9);
                lab.Width = 112;
                DockPanel.SetDock(lab, Dock.Left);
                row.Children.Add(lab);
                bool on = h.Target > 0;
                var val = T(h.Temp.ToString("0.0") + " °C" + (on ? "  → " + Math.Round(h.Target) : ""), on ? (Brush)_t.Fg : _t.FgDim, 0.95, on);
                val.Width = 110;
                DockPanel.SetDock(val, Dock.Left);
                row.Children.Add(val);
                row.Children.Add(Spark(h.History, on ? (Brush)_t.MainAccent : _t.FgDim, 110, 16));
                box.Children.Add(row);
            }
            if (sensors.Count > 0)
            {
                var s = T(string.Join("   ", sensors), _t.FgDim, 0.82);
                s.Margin = new Thickness(0, 2, 0, 2);
                box.Children.Add(s);
            }
            // presets and cool down
            var pre = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var preset in Moonraker.Presets(_mgr.Settings))
            {
                var pr = preset;
                pre.Children.Add(Pill("", pr.Item1, "Heat the nozzle to " + pr.Item2 + " °C" + (pr.Item3 > 0 ? " and the bed to " + pr.Item3 + " °C" : ""),
                                      delegate { Run(delegate { return Moonraker.PreheatAsync(id, pr.Item2, pr.Item3); }); }));
            }
            pre.Children.Add(Pill("", "Cool down", "Turn every heater off", delegate { Run(delegate { return Moonraker.CoolDownAsync(id); }); }));
            box.Children.Add(pre);
            _temps.Child = box;
        }

        void SetTemp(string id, PrHeater h)
        {
            var f = new List<FormField> { new FormField("Target (°C, 0 = off)", Math.Round(h.Target).ToString()) };
            Window owner = Window.GetWindow(this);
            var host = owner as PopupWindow;
            if (host != null) host.Pinned = true;
            _menuOpen = true;
            bool ok;
            try { ok = FormWindow.Ask(owner, h.Label + " temperature", f); }
            finally { if (host != null) host.Pinned = false; _menuOpen = false; }
            double v;
            if (!ok || !double.TryParse(f[0].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return;
            Run(delegate { return Moonraker.SetTempAsync(id, h.Name, v); });
        }

        // ---- Spoolman and power devices ------------------------------------------------------------------------
        void BuildExtras(PrinterState p, bool force)
        {
            var k = new System.Text.StringBuilder(p.PrinterId + "|" + PtView.Of(_cfg).ShowSpool + PtView.Of(_cfg).ShowPower + p.Busy);
            if (p.Spool != null) k.Append("|s" + p.Spool.Id + "/" + Math.Round(p.Spool.Remaining));
            foreach (PrPower d in p.Power) k.Append("|" + d.Device + d.Status);
            foreach (PtLight li in p.Lights) k.Append("|" + li.Object + li.On);
            string key = k.ToString();
            if (!force && key == _extrasKey) return;
            _extrasKey = key;
            string id = p.PrinterId;
            var box = new StackPanel();

            if (PtView.Of(_cfg).ShowSpool && p.HasSpoolman)
            {
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                Border change = null;
                change = Pill("", "Change", "Pick the spool that's loaded", delegate { SpoolMenu(change, id); });
                change.Margin = new Thickness(0);
                DockPanel.SetDock(change, Dock.Right);
                row.Children.Add(change);
                if (p.Spool != null)
                {
                    Brush sw = _t.FgDim;
                    try { if (p.Spool.ColorHex.Length >= 6) sw = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + p.Spool.ColorHex.TrimStart('#').Substring(0, 6))); } catch { }
                    var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = sw, BorderBrush = Theme.Faded(_t.Fg, 30), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(swatch, Dock.Left);
                    row.Children.Add(swatch);
                    var txt = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    txt.Children.Add(T(p.Spool.Label, _t.Fg, 0.92));
                    if (p.Spool.Remaining >= 0)
                    {
                        bool low = p.Spool.Remaining < 100;
                        txt.Children.Add(T("   " + Math.Round(p.Spool.Remaining) + " g left", low ? (Brush)StatusColors.Warn : _t.FgDim, 0.88));
                    }
                    row.Children.Add(txt);
                    row.ToolTip = "Spoolman spool #" + p.Spool.Id + (p.Spool.Initial > 0 && p.Spool.Remaining >= 0 ? " - " + Math.Round(p.Spool.Remaining) + " of " + Math.Round(p.Spool.Initial) + " g left" : "");
                }
                else row.Children.Add(T("No spool selected (Spoolman)", _t.FgDim, 0.9));
                box.Children.Add(row);
            }

            if (PtView.Of(_cfg).ShowPower && (p.Power.Count > 0 || p.Lights.Count > 0))
            {
                var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                // Klipper's lights (output pins with light / led / lamp in the name, LED strips)
                foreach (PtLight li in p.Lights)
                {
                    PtLight me = li;
                    string label = li.Name.Replace('_', ' ');
                    label = label.Length > 0 ? char.ToUpperInvariant(label[0]) + label.Substring(1) : li.Object;
                    var lb = Pills.Make(_t, "\uE781", label + (li.On ? " · on" : " · off"), (li.On ? "Turn " : "Turn on ") + label + (li.On ? " off" : ""), false,
                                        delegate { Run(delegate { return Moonraker.LightAsync(id, me, !me.On); }); }, li.On);
                    lb.Margin = new Thickness(0, 0, 6, 4);
                    w.Children.Add(lb);
                }
                foreach (PrPower d in p.Power)
                {
                    PrPower me = d;
                    bool locked = d.Locked && p.Busy;
                    var b = Pills.Make(_t, d.On ? "" : "", d.Device + (d.On ? " · on" : d.Status == "off" ? " · off" : " · " + d.Status),
                                       locked ? d.Device + " can't be switched while printing" : (d.On ? "Turn " + d.Device + " off" : "Turn " + d.Device + " on"), false, delegate
                    {
                        if (locked) return;
                        if (me.On && !Ask("Turn " + me.Device + " off?")) return;
                        Run(delegate { return Moonraker.PowerAsync(id, me.Device, !me.On); });
                    }, d.On);
                    b.Margin = new Thickness(0, 0, 6, 4);
                    if (locked) b.Opacity = 0.5;
                    w.Children.Add(b);
                }
                box.Children.Add(w);
            }
            _extras.Child = box.Children.Count > 0 ? box : null;
        }

        async void SpoolMenu(FrameworkElement target, string id)
        {
            List<PrSpool> spools;
            try { spools = await Moonraker.SpoolsAsync(id); }
            catch (Exception ex) { Questions.Tell(null, "Spoolman: " + ex.Message, "3D printer", MessageBoxImage.Warning); return; }
            var m = new ContextMenu();
            if (spools.Count == 0) m.Items.Add(new MenuItem { Header = "No spools in Spoolman", IsEnabled = false });
            foreach (PrSpool sp in spools)
            {
                PrSpool me = sp;
                m.Items.Add(MI(sp.Label + (sp.Remaining >= 0 ? "  -  " + Math.Round(sp.Remaining) + " g" : "") + "  (#" + sp.Id + ")", delegate { Run(delegate { return Moonraker.SetSpoolAsync(id, me.Id); }); }));
            }
            m.Items.Add(new Separator());
            m.Items.Add(MI("No spool", delegate { Run(delegate { return Moonraker.SetSpoolAsync(id, 0); }); }));
            Menu(target, m);
        }

        // ---- Files / History / Queue / Macros / Console ----------------------------------------------------------
        void BuildLists(PrinterState p, bool force)
        {
            if (!PtView.Of(_cfg).ShowLists || !p.Ok) { _tabs.Children.Clear(); _listHost.Child = null; _listKey = null; return; }
            var names = new List<string> { "Files", "History" };
            if (p.HasQueue) names.Add("Queue");
            if (p.Macros.Count > 0) names.Add("Macros");
            names.Add("Console");
            if (!names.Contains(_tab)) _tab = "Files";

            string key = p.PrinterId + "|" + _tab + "|" + string.Join(",", names) + "|" + ListKey(p);
            if (!force && key == _listKey) return;
            _listKey = key;

            _tabs.Children.Clear();
            foreach (string n in names)
            {
                string me = n;
                var b = new Border
                {
                    Child = T(n, n == _tab ? _t.Fg : _t.FgDim, 0.92, n == _tab), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 2, 0), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = n == _tab ? (Brush)_t.MainAccent : Brushes.Transparent, Background = Brushes.Transparent
                };
                b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };      // (in a widget the press would start a drag)
                b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; _tab = me; _listKey = null; Refresh(true); if (me == "Console") LoadConsole(); };
                _tabs.Children.Add(b);
            }

            FrameworkElement list;
            switch (_tab)
            {
                case "History": list = HistoryList(p); break;
                case "Queue": list = QueueList(p); break;
                case "Macros": list = MacroList(p); break;
                case "Console": list = ConsolePanel(p); break;
                default: list = FileList(p); break;
            }
            if (_tab == "Console") { _listHost.Child = list; return; }
            _listHost.Child = new ScrollViewer
            {
                Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Round(_t.FontSize * 22), Focusable = false
            };
        }

        string ListKey(PrinterState p)
        {
            switch (_tab)
            {
                case "History": return p.History.Count + (p.History.Count > 0 ? p.History[0].Id + p.History[0].Status : "") + p.Busy;
                case "Queue": return p.QueueState + "|" + p.Queue.Count + (p.Queue.Count > 0 ? p.Queue[0].Id : "") + p.Busy;
                case "Macros": return p.Macros.Count + "|" + p.Busy;
                case "Console": return "console";
                default: return (_files != null ? _files.Count + "|" + _filesFor : "-") + "|" + p.Busy;
            }
        }

        List<PrFile> _files;
        string _filesFor = "";
        bool _filesBusy, _forceFiles;

        FrameworkElement FileList(PrinterState p)
        {
            string id = p.PrinterId;
            var box = new StackPanel();
            if (_files == null || _filesFor != id)
            {
                if (!_filesBusy)
                {
                    _filesBusy = true;
                    bool force = _forceFiles;
                    _forceFiles = false;
                    Task.Run(async delegate
                    {
                        List<PrFile> f = null;
                        string err = "";
                        try { f = await Moonraker.FilesAsync(id, force); } catch (Exception ex) { err = ex.Message; }
                        _ = Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _filesBusy = false;
                            _files = f ?? new List<PrFile>(); _filesFor = id;
                            if (err.Length > 0) Log.Write("Printer files: " + err);
                            _listKey = null; Refresh(true);
                        }));
                    });
                }
                box.Children.Add(T("Reading the files…", _t.FgDim, 0.9));
                return box;
            }
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var re = Pill("", "", "Read the files again", delegate { _files = null; _forceFiles = true; _listKey = null; Refresh(true); });
            DockPanel.SetDock(re, Dock.Right);
            head.Children.Add(re);
            head.Children.Add(T(_files.Count + " file" + (_files.Count == 1 ? "" : "s") + ", newest first", _t.FgDim, 0.85));
            box.Children.Add(head);
            if (_files.Count == 0) box.Children.Add(T("No print files.", _t.FgDim, 0.9));
            int n = 0;
            foreach (PrFile f in _files)
            {
                if (n++ >= 30) { box.Children.Add(T("+ " + (_files.Count - 30) + " more (open Fluidd for all)", _t.FgDim, 0.85)); break; }
                box.Children.Add(FileRow(p, f));
            }
            return box;
        }

        readonly Dictionary<string, PrMeta> _metas = new Dictionary<string, PrMeta>();

        FrameworkElement FileRow(PrinterState p, PrFile f)
        {
            string id = p.PrinterId, path = f.Path;
            PrMeta meta;
            if (!_metas.TryGetValue(id + "|" + path, out meta))
            {
                _metas[id + "|" + path] = null;
                Task.Run(async delegate
                {
                    PrMeta m = await Moonraker.MetaAsync(id, path);
                    _ = Dispatcher.BeginInvoke(new Action(delegate { _metas[id + "|" + path] = m; _listKey = null; Refresh(true); }));
                });
            }
            var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var thumb = Thumb(meta != null ? Pic(id, meta.Thumb, 120) : null, 40);
            DockPanel.SetDock(thumb, Dock.Left);
            g.Children.Add(thumb);
            Border print = null;
            print = Pill("", "", "Print " + f.Name, delegate { if (Ask("Print " + Moonraker.Short(path) + " on " + p.PrinterName + "?")) Run(delegate { return Moonraker.StartPrintAsync(id, path); }); });
            print.Margin = new Thickness(6, 0, 0, 0);
            print.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(print, Dock.Right);
            if (!p.Busy && p.Ready) g.Children.Add(print);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(Moonraker.Short(path), _t.Fg, 0.92));
            var det = new List<string>();
            if (meta != null && meta.Estimated > 0) det.Add(Moonraker.Duration(meta.Estimated));
            if (meta != null && meta.FilamentWeight > 0) det.Add(Math.Round(meta.FilamentWeight) + " g");
            if (f.Modified > 0) det.Add(TimeText.Ago(DateTimeOffset.FromUnixTimeMilliseconds((long)(f.Modified * 1000)).UtcDateTime));
            if (det.Count > 0) info.Children.Add(T(string.Join(" · ", det), _t.FgDim, 0.8));
            g.Children.Add(info);
            var row = new Border { Child = g, Padding = new Thickness(3, 2, 3, 2), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = path + "\nRight-click for more" };
            row.MouseEnter += delegate { row.Background = _t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var m = new ContextMenu();
                m.Items.Add(MI("Print now", delegate { if (Ask("Print " + Moonraker.Short(path) + " on " + p.PrinterName + "?")) Run(delegate { return Moonraker.StartPrintAsync(id, path); }); }, !p.Busy && p.Ready));
                if (p.HasQueue) m.Items.Add(MI("Add to the queue", delegate { Run(delegate { return Moonraker.QueueAddAsync(id, path); }); }));
                m.Items.Add(MI("Copy the name", delegate { try { Clipboard.SetText(path); } catch { } }));
                Menu(row, m);
            };
            return row;
        }

        FrameworkElement HistoryList(PrinterState p)
        {
            string id = p.PrinterId;
            var box = new StackPanel();
            if (p.History.Count == 0) { box.Children.Add(T("No prints yet.", _t.FgDim, 0.9)); return box; }
            foreach (PrJob j in p.History)
            {
                PrJob me = j;
                var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var thumb = Thumb(Pic(id, j.Thumb, 120), 40);
                DockPanel.SetDock(thumb, Dock.Left);
                g.Children.Add(thumb);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var top = new StackPanel { Orientation = Orientation.Horizontal };
                Brush c = j.Status == "completed" ? StatusColors.Good : j.Status == "in_progress" ? _t.MainAccent : j.Status == "cancelled" ? StatusColors.Warn : (Brush)StatusColors.Bad;
                top.Children.Add(Dot(c, 7));
                top.Children.Add(T(Moonraker.Short(j.File), _t.Fg, 0.92));
                info.Children.Add(top);
                var det = new List<string> { JobStatus(j.Status) };
                if (j.PrintDuration > 0) det.Add(Moonraker.Duration(j.PrintDuration));
                if (j.FilamentUsed > 0) det.Add(Moonraker.Filament(j.FilamentUsed));
                if (j.End > 0) det.Add(TimeText.Stamp(DateTimeOffset.FromUnixTimeMilliseconds((long)(j.End * 1000)).LocalDateTime));
                else if (j.Start > 0) det.Add("started " + TimeText.Stamp(DateTimeOffset.FromUnixTimeMilliseconds((long)(j.Start * 1000)).LocalDateTime));
                info.Children.Add(T(string.Join(" · ", det), _t.FgDim, 0.8));
                g.Children.Add(info);
                var row = new Border { Child = g, Padding = new Thickness(3, 2, 3, 2), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = j.File + "\nRight-click to print it again" };
                row.MouseEnter += delegate { row.Background = _t.Hover; };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    var m = new ContextMenu();
                    m.Items.Add(MI("Print again", delegate { if (Ask("Print " + Moonraker.Short(me.File) + " again on " + p.PrinterName + "?")) Run(delegate { return Moonraker.StartPrintAsync(id, me.File); }); }, !p.Busy && p.Ready));
                    if (p.HasQueue) m.Items.Add(MI("Add to the queue", delegate { Run(delegate { return Moonraker.QueueAddAsync(id, me.File); }); }));
                    Menu(row, m);
                };
                box.Children.Add(row);
            }
            return box;
        }

        FrameworkElement QueueList(PrinterState p)
        {
            string id = p.PrinterId;
            var box = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            bool running = p.QueueState == "ready" || p.QueueState == "loading" || p.QueueState == "starting";
            var toggle = running ? Pill("", "Pause queue", "Don't start the next job when this one ends", delegate { Run(delegate { return Moonraker.QueuePauseAsync(id); }); })
                                 : Pill("", "Start queue", "Start the queue (prints the next job when the printer is free)", delegate { Run(delegate { return Moonraker.QueueStartAsync(id); }); }, true);
            toggle.Margin = new Thickness(0);
            DockPanel.SetDock(toggle, Dock.Right);
            head.Children.Add(toggle);
            head.Children.Add(T("Queue " + (p.QueueState.Length > 0 ? p.QueueState : "") + " · " + p.Queue.Count + " job" + (p.Queue.Count == 1 ? "" : "s"), _t.FgDim, 0.88));
            box.Children.Add(head);
            if (p.Queue.Count == 0) box.Children.Add(T("Nothing queued - right-click a file for Add to the queue.", _t.FgDim, 0.88));
            foreach (PrQueued q in p.Queue)
            {
                PrQueued me = q;
                var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var x = Pill("", "", "Take it off the queue", delegate { Run(delegate { return Moonraker.QueueRemoveAsync(id, me.Id); }); });
                x.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(x, Dock.Right);
                g.Children.Add(x);
                g.Children.Add(T(Moonraker.Short(q.File), _t.Fg, 0.92));
                box.Children.Add(g);
            }
            return box;
        }

        FrameworkElement MacroList(PrinterState p)
        {
            string id = p.PrinterId;
            var w = new WrapPanel();
            foreach (string m in p.Macros)
            {
                string me = m;
                w.Children.Add(Pill("", m.Replace('_', ' '), "Run the macro " + m, delegate { Run(delegate { return Moonraker.GcodeAsync(id, me); }); }));
            }
            return w;
        }

        FrameworkElement ConsolePanel(PrinterState p)
        {
            var box = new StackPanel();
            if (_consoleLines.Parent is Panel) ((Panel)_consoleLines.Parent).Children.Remove(_consoleLines);
            if (_gcode.Parent is Panel) ((Panel)_gcode.Parent).Children.Remove(_gcode);
            var lines = new ScrollViewer { Content = _consoleLines, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Round(_t.FontSize * 14), Focusable = false };
            box.Children.Add(lines);
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var send = Pill("", "Send", "Send it to Klipper (Enter)", SendGcode);
            send.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(send, Dock.Right);
            row.Children.Add(send);
            _gcode.ToolTip = "G-code or a macro, e.g. G28, M117 Hello, STATUS";
            row.Children.Add(_gcode);
            box.Children.Add(row);
            if (_consoleLines.Children.Count == 0) LoadConsole();
            return box;
        }

        async void SendGcode()
        {
            string g = _gcode.Text.Trim();
            if (g.Length == 0) return;
            _gcode.Text = "";
            string id = _sel;
            string err = await Moonraker.GcodeAsync(id, g);
            if (err.Length > 0) _consoleLines.Children.Add(T("! " + err, StatusColors.Bad, 0.85));
            LoadConsole();
        }

        async void LoadConsole()
        {
            List<string> lines = await Moonraker.ConsoleAsync(_sel, 30);
            _consoleLines.Children.Clear();
            foreach (string l in lines)
            {
                var tb = T(l, l.StartsWith("> ") ? (Brush)_t.MainAccent : l.StartsWith("!!") ? StatusColors.Bad : _t.FgDim, 0.82);
                tb.FontFamily = new FontFamily("Consolas");
                tb.TextWrapping = TextWrapping.Wrap; tb.TextTrimming = TextTrimming.None;
                _consoleLines.Children.Add(tb);
            }
            var sv = _consoleLines.Parent as ScrollViewer;
            if (sv != null) sv.ScrollToEnd();
        }
    }
}
