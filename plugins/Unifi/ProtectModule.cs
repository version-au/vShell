using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// UniFi Protect. On a bar: a camera icon with the cameras online (e.g. 6/6, red when one is down) and the last event
    /// ("Person · Driveway · 2m"); click for the popup. In a widget (and the popup): the cameras' snapshots in a grid (refreshing every
    /// few seconds; click one for a big view with Open in Protect, Play in VLC and PTZ presets), the lights (on / off) and the events seen
    /// since vShell started.
    /// </summary>
    public sealed class ProtectModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _popup;
        string _shown;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public ProtectModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            if (host.IsWidget) { Padding = new Thickness(2); Child = new ProtectView(mgr, _t, cfg, 0); return; }
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { _timer.Start(); Refresh(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            Unifi.Poke();
            _popup = new PopupWindow(_mgr, _host, this, _t, new ProtectView(_mgr, _t, _cfg, 520));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        void Refresh()
        {
            bool ok = Unifi.ProtectConfigured(_mgr.Settings);
            PrSnapshot s = ok ? Unifi.Protect : new PrSnapshot();
            List<PrEvent> evs = Unifi.Events;
            PrEvent last = evs.Count > 0 ? evs[0] : null;
            string key = ok + "|" + s.Fetched.Ticks + "|" + s.Error + "|" + (last != null ? last.Id + last.What : "") + "|" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute) + "|" + Online(s) + "|" + _cfg.Title;
            if (key == _shown) return;
            _shown = key;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b) { return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center }; };
            if (!string.IsNullOrEmpty(_cfg.Title)) row.Children.Add(T(_cfg.Title + " ", _t.FgDim));
            if (!ok) { row.Children.Add(T("Set up UniFi Protect in Settings > Plugins", _t.FgDim)); Child = row; ToolTip = null; _host.ContentChanged(); return; }
            int on = s.Cameras.FindAll(delegate (PrCamera c) { return c.Online; }).Count;
            bool down = s.Ok && on < s.Cameras.Count;
            row.Children.Add(new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize, Foreground = down ? StatusColors.Bad : _t.Fg, VerticalAlignment = VerticalAlignment.Center });
            TextBlock count = !s.Ok ? T(s.Error.Length > 0 ? "  Protect n/a" : "  Protect…", _t.FgDim) : T("  " + on + "/" + s.Cameras.Count, down ? StatusColors.Bad : _t.Fg);
            TextBlock ev = null;
            if (PrView.Of(_cfg).BarEvent && last != null && (DateTime.UtcNow - last.Start).TotalHours < 1)
                ev = T((_cfg.ShownTwoLines == true ? "  " : "   ") + last.What + (last.CameraName.Length > 0 ? " · " + last.CameraName : "") + " · " + TimeText.Ago(last.Start), _t.FgDim);
            if (_cfg.ShownTwoLines == true && ev != null)
            {
                // M on a bar: the cameras online over the last event
                var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                lines.Children.Add(count);
                lines.Children.Add(ev);
                row.Children.Add(lines);
            }
            else { row.Children.Add(count); if (ev != null) row.Children.Add(ev); }
            var tip = new List<string> { "UniFi Protect" + (s.Nvr.Length > 0 ? " · " + s.Nvr : "") };
            if (!s.Ok && s.Error.Length > 0) tip.Add("Can't reach Protect: " + s.Error);
            foreach (PrCamera c in s.Cameras) tip.Add("  " + (c.Online ? "●" : "✕") + " " + c.Name + (c.Online ? "" : " - " + Unifi.Nice(c.State)));
            int k = 0;
            foreach (PrEvent e in evs) { if (k++ >= 5) break; if (k == 1) tip.Add("Latest:"); tip.Add("  " + e.What + (e.CameraName.Length > 0 ? " · " + e.CameraName : "") + " · " + TimeText.Clock(e.Start.ToLocalTime())); }
            tip.Add("Click for the cameras");
            ToolTip = string.Join("\n", tip);
            Child = row;
            _host.ContentChanged();
        }

        static string Online(PrSnapshot s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (PrCamera c in s.Cameras) sb.Append(c.Online ? '1' : '0');
            return sb.ToString();
        }
    }

    /// <summary>The cameras' latest snapshots, shared by every view: asked for again once they're older than wanted (one at a time per camera).</summary>
    public static class ProtectSnaps
    {
        sealed class Shot { public ImageSource Image; public DateTime At = DateTime.MinValue; public bool Busy; public bool High; }
        static readonly Dictionary<string, Shot> Shots = new Dictionary<string, Shot>();
        public static int Version;

        /// <summary>The newest snapshot we have (null = none yet); starts fetching a new one when it's older than maxAge seconds.</summary>
        public static ImageSource Get(string cameraId, double maxAge, bool high, Dispatcher ui)
        {
            Shot s;
            lock (Shots)
            {
                if (!Shots.TryGetValue(cameraId + (high ? "|hq" : ""), out s)) { s = new Shot { High = high }; Shots[cameraId + (high ? "|hq" : "")] = s; }
                if (!s.Busy && (DateTime.UtcNow - s.At).TotalSeconds >= maxAge)
                {
                    s.Busy = true;
                    Shot shot = s;
                    Task.Run(async delegate
                    {
                        byte[] jpg = await Unifi.SnapshotAsync(cameraId, high);
                        ImageSource img = Decode(jpg, high ? 1920 : 640);
                        lock (Shots) { if (img != null) shot.Image = img; shot.At = DateTime.UtcNow; shot.Busy = false; }
                        if (img != null) { Version++; }
                    });
                }
                return s.Image;
            }
        }

        public static ImageSource Decode(byte[] jpg, int width)
        {
            if (jpg == null || jpg.Length < 100) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(jpg);
                if (width > 0) bi.DecodePixelWidth = width;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
    }

    /// <summary>The camera grid, the lights and the recent events (popup and widget).</summary>
    public sealed class ProtectView : Border
    {
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly ModuleConfig _cfg;
        readonly DispatcherTimer _timer;
        readonly UniformGrid _grid = new UniformGrid();
        readonly WrapPanel _lights = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        readonly StackPanel _events = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        readonly TextBlock _status = new TextBlock();
        readonly Dictionary<string, Image> _images = new Dictionary<string, Image>();
        string _gridKey, _lightKey, _eventKey;

        public ProtectView(BarManager mgr, Theme t, ModuleConfig cfg, double width)
        {
            _mgr = mgr; _t = t; _cfg = cfg;
            if (width > 0) Width = width;
            var root = new StackPanel();
            root.Children.Add(_grid);
            root.Children.Add(_lights);
            root.Children.Add(_events);
            _status.Foreground = t.FgDim; _status.FontFamily = t.Font; _status.FontSize = t.FontSize * 0.82; _status.Margin = new Thickness(4, 6, 4, 0); _status.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(_status);
            Child = root;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Update(); };
            Loaded += delegate { _timer.Start(); Update(); };
            Unloaded += delegate { _timer.Stop(); };
            Update();
        }

        List<PrCamera> Shown(PrSnapshot s)
        {
            string only = PrView.Of(_cfg).Cameras.Trim();
            if (only.Length == 0) return s.Cameras;
            var names = new List<string>();
            foreach (string n in only.Split(',')) if (n.Trim().Length > 0) names.Add(n.Trim());
            var list = new List<PrCamera>();
            foreach (string n in names)     // in the order you listed them
                foreach (PrCamera c in s.Cameras) if (string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)) list.Add(c);
            return list;
        }

        void Update()
        {
            bool ok = Unifi.ProtectConfigured(_mgr.Settings);
            PrSnapshot s = ok ? Unifi.Protect : new PrSnapshot();
            if (!ok) { _grid.Children.Clear(); _gridKey = null; _status.Text = "Set up UniFi Protect on the Plugins page in Settings (address and the Protect API key)."; return; }
            if (!s.Ok && s.Cameras.Count == 0) { _status.Text = s.Error.Length > 0 ? "Can't reach Protect: " + s.Error : "Asking Protect…"; return; }
            List<PrCamera> cams = Shown(s);

            // the grid: built when the cameras change; the pictures follow the snapshots
            string gk = PrView.Of(_cfg).Columns + "|" + Width;
            foreach (PrCamera c in cams) gk += "|" + c.Id + c.Name + c.Online;
            if (gk != _gridKey)
            {
                _gridKey = gk;
                _grid.Children.Clear();
                _images.Clear();
                _grid.Columns = Math.Max(1, Math.Min(PrView.Of(_cfg).Columns, Math.Max(1, cams.Count)));
                foreach (PrCamera c in cams) _grid.Children.Add(Tile(c));
            }
            foreach (PrCamera c in cams)
            {
                Image img;
                if (!c.Online || !_images.TryGetValue(c.Id, out img)) continue;
                ImageSource src = ProtectSnaps.Get(c.Id, Math.Max(2, PrView.Of(_cfg).SnapSeconds), false, Dispatcher);
                if (src != null && img.Source != src) img.Source = src;
            }

            // the lights
            string lk = PrView.Of(_cfg).ShowLights + "|";
            foreach (PrLight l in s.Lights) lk += l.Id + l.Name + l.On + l.Forced + l.Online + ";";
            if (lk != _lightKey)
            {
                _lightKey = lk;
                _lights.Children.Clear();
                if (PrView.Of(_cfg).ShowLights)
                    foreach (PrLight l in s.Lights)
                    {
                        PrLight light = l;
                        bool on = l.On || l.Forced;
                        Border pill = Pills.Make(_t, "", l.Name + (on ? " · on" : ""), on ? "Click: back to its own mode (" + Unifi.Nice(l.Mode) + ")" : "Click: turn it on", false, null, on);
                        pill.Margin = new Thickness(0, 0, 6, 4);
                        pill.Opacity = l.Online ? 1 : 0.5;
                        pill.MouseLeftButtonUp += async delegate
                        {
                            string err = await Unifi.LightAsync(light.Id, !on);
                            if (err != null) _status.Text = "Light: " + err;
                            _lightKey = null;
                        };
                        _lights.Children.Add(pill);
                    }
                _lights.Visibility = _lights.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // the events seen since vShell started
            List<PrEvent> evs = PrView.Of(_cfg).ShowEvents ? Unifi.Events : new List<PrEvent>();
            string ek = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute) + "|";
            int n = 0;
            foreach (PrEvent e in evs) { if (n++ >= 6) break; ek += e.Id + e.What + e.Image + ";"; }
            if (ek != _eventKey)
            {
                _eventKey = ek;
                _events.Children.Clear();
                n = 0;
                foreach (PrEvent e in evs)
                {
                    if (n++ >= 6) break;
                    _events.Children.Add(EventRow(e, s));
                }
                _events.Visibility = _events.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            int online = s.Cameras.FindAll(delegate (PrCamera c) { return c.Online; }).Count;
            _status.Text = online + " of " + s.Cameras.Count + " cameras online" + (s.Nvr.Length > 0 ? " · " + s.Nvr : "") + (s.Error.Length > 0 ? " · last update failed: " + s.Error : "") +
                           (cams.Count > 0 ? " · click a camera for the big view" : "");
        }

        FrameworkElement Tile(PrCamera c)
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var g = new Grid { Background = _t.Track };
            g.Children.Add(img);
            if (!c.Online)
                g.Children.Add(new TextBlock { Text = "Offline", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize * 0.9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            // the name on a dark strip along the bottom
            var shade = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0), 90);
            g.Children.Add(new Border
            {
                Background = shade, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 10, 8, 5),
                Child = new TextBlock { Text = (c.Online ? "" : "✕ ") + c.Name, Foreground = Brushes.White, FontFamily = _t.Font, FontSize = _t.FontSize * 0.85, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }
            });
            var box = new Border { Child = g, CornerRadius = new CornerRadius(8), Margin = new Thickness(3), Cursor = Cursors.Hand, ClipToBounds = true, ToolTip = c.Name + (c.Online ? "\nClick for the big view" : " - " + Unifi.Nice(c.State)) };
            box.SizeChanged += delegate
            {
                double w = box.ActualWidth;
                if (w > 10 && Math.Abs(box.Height - w * 9 / 16) > 1) box.Height = Math.Round(w * 9 / 16);
                g.Clip = new RectangleGeometry(new Rect(0, 0, box.ActualWidth, box.ActualHeight), 8, 8);
            };
            if (Width > 0 || double.IsNaN(Width)) box.MinHeight = 60;
            box.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            PrCamera cam = c;
            box.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; ProtectCameraWindow.Show(_mgr, _t, cam); };
            _images[c.Id] = img;
            return box;
        }

        FrameworkElement EventRow(PrEvent e, PrSnapshot s)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ImageSource pic = NotifyCards.Picture(e.Image, 96);
            FrameworkElement thumb;
            if (pic != null) thumb = new Border { Width = 48, Height = 27, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = new Image { Source = pic, Stretch = Stretch.UniformToFill } };
            else thumb = new Border { Width = 48, Height = 27, CornerRadius = new CornerRadius(4), Background = _t.Track, Child = new TextBlock { Text = e.Type == "ring" ? "" : "", FontFamily = IconUtil.GlyphFont, Foreground = _t.FgDim, FontSize = _t.FontSize * 0.8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            thumb.Margin = new Thickness(2, 0, 8, 0);
            g.Children.Add(thumb);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = e.What, Foreground = e.Type == "ring" ? (Brush)StatusColors.Warn : _t.Fg, FontFamily = _t.Font, FontSize = _t.FontSize * 0.92, FontWeight = FontWeights.SemiBold });
            words.Children.Add(new TextBlock { Text = e.CameraName.Length > 0 ? e.CameraName : "Protect", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize * 0.8 });
            Grid.SetColumn(words, 1);
            g.Children.Add(words);
            var when = new TextBlock { Text = TimeText.Ago(e.Start), Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize * 0.78, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            Grid.SetColumn(when, 2);
            g.Children.Add(when);
            var row = new Border { Child = g, Padding = new Thickness(3), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = TimeText.Full(e.Start.ToLocalTime()) + "\nClick for the camera" };
            row.MouseEnter += delegate { row.Background = _t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; };
            row.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs ev)
            {
                ev.Handled = true;
                PrCamera cam = s.Camera(e.CameraId);
                if (cam != null) ProtectCameraWindow.Show(_mgr, _t, cam);
            };
            return row;
        }
    }

    /// <summary>
    /// One camera, big: its snapshot in high quality, refreshing every 2 seconds, with Open in Protect, Play in VLC (the RTSPS stream),
    /// Copy stream link and - for PTZ cameras - Home and presets 1 - 4. One window per camera.
    /// </summary>
    public sealed class ProtectCameraWindow : Window
    {
        static readonly Dictionary<string, ProtectCameraWindow> Open = new Dictionary<string, ProtectCameraWindow>();
        readonly BarManager _mgr;
        readonly Theme _t;
        readonly PrCamera _cam;
        readonly Image _img = new Image { Stretch = Stretch.Uniform };
        readonly TextBlock _status = new TextBlock();
        readonly DispatcherTimer _timer;

        public static void Show(BarManager mgr, Theme t, PrCamera cam)
        {
            ProtectCameraWindow w;
            if (Open.TryGetValue(cam.Id, out w) && w.IsLoaded) { if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Activate(); return; }
            w = new ProtectCameraWindow(mgr, t, cam);
            Open[cam.Id] = w;
            w.Closed += delegate { Open.Remove(cam.Id); };
            w.Show();
            w.Activate();
        }

        ProtectCameraWindow(BarManager mgr, Theme t, PrCamera cam)
        {
            _mgr = mgr; _t = t; _cam = cam;
            Title = cam.Name;
            Width = 960; Height = 620; MinWidth = 420; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Color bg = t.PopupBg.Color;
            Background = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));
            BorderBrush = Theme.Faded(t.MainAccent, 45);
            BorderThickness = new Thickness(1);
            FontFamily = t.Font;

            var root = new DockPanel();
            var title = new Grid { Background = Brushes.Transparent, Margin = new Thickness(14, 10, 8, 8) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = t.FontSize * 1.1, Foreground = t.MainAccent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            name.Children.Add(new TextBlock { Text = cam.Name, Foreground = t.Fg, FontSize = t.FontSize * 1.08, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(name);
            var close = Pills.Make(t, "", "", "Close (Esc)", false, delegate { Close(); });
            Grid.SetColumn(close, 1);
            title.Children.Add(close);
            title.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else { try { DragMove(); } catch { } } };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            // the buttons
            var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 4) };
            Action<Border> add = delegate (Border b) { b.Margin = new Thickness(0, 0, 6, 4); bar.Children.Add(b); };
            add(Pills.Make(t, "", "Open in Protect", "The camera in Protect's web page", false, delegate { OpenUrl(Unifi.ProtectCameraWeb(_mgr.Settings, _cam.Id)); }));
            add(Pills.Make(t, "", "Play in VLC", "The live RTSPS stream in VLC", false, delegate { PlayVlc(); }));
            add(Pills.Make(t, "", "Copy stream link", "The RTSPS address, for any player that takes it", false, delegate { CopyStream(); }));
            if (cam.IsPtz)
            {
                add(Pills.Make(t, "", "Home", "PTZ: back to home", false, delegate { Ptz(-1); }));
                for (int i = 0; i < 4; i++)
                {
                    int slot = i;
                    add(Pills.Make(t, "", "Preset " + (i + 1), "PTZ: go to preset " + (i + 1), false, delegate { Ptz(slot); }));
                }
            }
            DockPanel.SetDock(bar, Dock.Bottom);
            _status.Foreground = t.FgDim; _status.FontSize = t.FontSize * 0.85; _status.Margin = new Thickness(14, 0, 14, 8);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);
            root.Children.Add(bar);
            var frame = new Border { Child = _img, Margin = new Thickness(10, 0, 10, 0), Background = Brushes.Black, CornerRadius = new CornerRadius(6), ClipToBounds = true };
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            root.Children.Add(frame);
            Content = root;

            PreviewKeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Tick(); };
            Loaded += delegate { _timer.Start(); Tick(); };
            Closed += delegate { _timer.Stop(); };
        }

        void Tick()
        {
            ImageSource src = ProtectSnaps.Get(_cam.Id, 2, true, Dispatcher);
            if (src == null) src = ProtectSnaps.Get(_cam.Id, 10, false, Dispatcher);      // the grid's picture until the big one comes
            if (src != null && _img.Source != src) { _img.Source = src; _status.Text = "Snapshot every 2 seconds · " + DateTime.Now.ToString(TimeText.Use24 ? "HH:mm:ss" : "h:mm:ss tt") + "   ·   Esc closes"; }
            else if (src == null) _status.Text = "Asking Protect for a snapshot…";
        }

        static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

        async void Ptz(int slot)
        {
            string err = await Unifi.PtzAsync(_cam.Id, slot);
            _status.Text = err == null ? (slot < 0 ? "Going home…" : "Going to preset " + (slot + 1) + "…") : "PTZ: " + err;
        }

        async void CopyStream()
        {
            try
            {
                string url = await Unifi.StreamAsync(_cam.Id);
                Clipboard.SetText(url);
                _status.Text = "Stream link copied.";
            }
            catch (Exception ex) { _status.Text = "Stream: " + ex.Message; }
        }

        /// <summary>VLC from its usual places; without it the link is copied instead.</summary>
        async void PlayVlc()
        {
            string url;
            try { url = await Unifi.StreamAsync(_cam.Id); }
            catch (Exception ex) { _status.Text = "Stream: " + ex.Message; return; }
            string vlc = null;
            foreach (string pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string p = Path.Combine(pf, "VideoLAN", "VLC", "vlc.exe");
                if (File.Exists(p)) { vlc = p; break; }
            }
            if (vlc == null)
            {
                try { Clipboard.SetText(url); } catch { }
                _status.Text = "VLC isn't installed (in Program Files) - the stream link is copied, paste it into your player.";
                return;
            }
            try { Process.Start(new ProcessStartInfo(vlc, "\"" + url + "\"") { UseShellExecute = false }); _status.Text = "Opening the stream in VLC…"; }
            catch (Exception ex) { _status.Text = "VLC: " + ex.Message; }
        }
    }
}
