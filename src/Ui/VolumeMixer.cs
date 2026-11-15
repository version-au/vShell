using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The volume mixer (after EarTrumpet): the output device, the master volume and a slider per app, each with its sound level
    /// moving inside the slider. Opened from the Volume module and the tray's sound icon. It reads Windows' audio only while open:
    /// the levels a dozen times a second, the apps and volumes once a second.
    /// </summary>
    public sealed class VolumeMixer : Border
    {
        static BarManager _mgr;
        static PopupWindow _open;

        /// <summary>Called at start-up (the tray's sound icon opens the mixer without a module of its own).</summary>
        public static void Start(BarManager mgr) { _mgr = mgr; }

        /// <summary>What the mixer shows (a Volume module's settings; the tray's sound icon uses the defaults).</summary>
        public sealed class Options
        {
            /// <summary>Only apps playing right now (idle ones on the default device left out too).</summary>
            public bool PlayingOnly;
            public bool Meters = true, DevicePicker = true;
            /// <summary>The device the mixer is for: its volume is the first row (named after it), its apps come first ("" = the default output device).</summary>
            public string DeviceId = "";
            /// <summary>Upright sliders side by side, one column an app (the mixer in a widget only).</summary>
            public bool Columns;
            /// <summary>Several devices, each with its own volume row (the mixer in a widget only; empty = DeviceId alone).</summary>
            public List<VolumeDeviceItem> Devices = new List<VolumeDeviceItem>();
            /// <summary>The apps' volumes (off = only the devices').</summary>
            public bool ShowApps = true;
            /// <summary>The Sound settings / Windows volume mixer links at the bottom.</summary>
            public bool Links = true;

            public static Options Of(ModuleConfig c)
            {
                return new Options
                {
                    PlayingOnly = c.VolumePlayingOnly, Meters = c.VolumeMeters, DevicePicker = c.VolumeDevicePicker, DeviceId = c.VolumeDeviceId ?? "",
                    Columns = c.VolumeMixerLayout == "Columns", Devices = new List<VolumeDeviceItem>(c.VolumeMixerDevices ?? new List<VolumeDeviceItem>()),
                    ShowApps = c.VolumeShowApps, Links = c.VolumeLinks
                };
            }
        }

        /// <summary>Open the mixer at an element on a bar (or widget), or close it when it's open.</summary>
        public static void Toggle(FrameworkElement anchor, Theme theme, Options options = null)
        {
            if (_open != null && _open.IsOpen) { _open.CloseSafe(); _open = null; return; }
            if (PopupWindow.ClosedJustNow(anchor)) { _open = null; return; }     // this click's press already closed it
            var host = Window.GetWindow(anchor) as IModuleHost;
            if (_mgr == null || host == null) return;
            Theme t = theme ?? host.Theme;
            if (options != null)
            {
                // columns and a list of devices are for the mixer in a widget; the popup keeps rows and the module's one device
                options.Columns = false;
                options.Devices = new List<VolumeDeviceItem>();
            }
            var mixer = new VolumeMixer(t, options ?? new Options(), null);
            var popup = new PopupWindow(_mgr, host, anchor, t, mixer);
            if (_mgr.Settings.WidgetStyle != null) popup.UseWidgetStyle(t, _mgr.Settings.WidgetStyle);
            mixer._popup = popup;
            mixer._scroll.MaxHeight = Math.Max(120, popup.ContentRoom() - 150);     // room for the device and master rows above
            _open = popup;
            _mgr.ShowPopup(popup);
        }

        // =================================================================
        sealed class Row
        {
            public string Key = "";
            public MixerSlider Slider;
            public TextBlock Pct, Label;
            public FrameworkElement Icon;
            /// <summary>The whole row (or column) as it's placed.</summary>
            public Panel Root;
            public bool Muted;
        }

        readonly Theme _t;
        readonly Options _o;
        /// <summary>The widget it sits in (null = a popup): no reading while the widget is covered.</summary>
        readonly IModuleHost _inWidget;
        PopupWindow _popup;
        readonly TextBlock _device;
        /// <summary>The device volume rows (one, or one per device picked for the widget): device id ("" = default), saved name, row.</summary>
        readonly List<Tuple<string, string, Row>> _devRows = new List<Tuple<string, string, Row>>();
        /// <summary>The widget's mixer showing the devices picked for it (each its own row) rather than one device.</summary>
        readonly bool _several;
        readonly StackPanel _apps = new StackPanel();
        readonly ScrollViewer _scroll;
        readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();
        /// <summary>Apps on other devices seen playing since the mixer opened (they stay listed until it closes).</summary>
        readonly HashSet<string> _seen = new HashSet<string>();
        List<AudioApp> _list = new List<AudioApp>();
        string _sig;
        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        int _tick;

        const double SliderW = 170, NameW = 118;
        /// <summary>Columns: each column's width and its upright slider's height.</summary>
        const double ColumnW = 64, ColumnH = 120;

        /// <summary>A mixer: in a popup (inWidget null), or in a widget (VolumeMixerModule).</summary>
        internal VolumeMixer(Theme t, Options o, IModuleHost inWidget)
        {
            _t = t;
            _o = o ?? new Options();
            _inWidget = inWidget;
            var root = new StackPanel { Margin = inWidget != null ? new Thickness(4, 4, 4, 4) : new Thickness(10, 8, 10, 8), MinWidth = _o.Columns ? 0 : 340 };

            // the device: the default output (click to make another the default), or the module's own device (just its name)
            bool own = _o.DeviceId.Length > 0;
            var dev = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 8) };
            dev.Children.Add(Glyph("", t.FgDim, t.FontSize + 2));
            _device = new TextBlock { FontFamily = t.Font, FontSize = t.FontSize, FontWeight = FontWeights.SemiBold, Foreground = t.Fg, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
            dev.Children.Add(_device);
            if (own) dev.ToolTip = "This Volume module's sound device (pick another in its settings)";
            else
            {
                dev.Children.Add(Glyph("", t.FgDim, t.FontSize - 2));
                dev.Cursor = Cursors.Hand;
                dev.ToolTip = "The default output device - click to pick another";
                dev.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                dev.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; DeviceMenu(dev); };
            }
            // several devices picked (the mixer in a widget): each has its own row, named after it - no device line at the top
            _several = inWidget != null && _o.Devices != null && _o.Devices.Count > 0;
            bool several = _several;
            if (_o.DevicePicker && !several) root.Children.Add(dev);

            // the device volumes: the mixer's device, or each device picked
            var devices = new List<VolumeDeviceItem>();
            if (several) devices.AddRange(_o.Devices);
            else devices.Add(new VolumeDeviceItem { Id = _o.DeviceId, Name = "" });
            foreach (VolumeDeviceItem d in devices)
            {
                string id = d.Id ?? "";
                Row r = MakeRow("dev:" + id, Glyph("", t.Fg, t.FontSize + 3), d.Name.Length > 0 ? Audio.ShortName(d.Name) : "All sound",
                                delegate (double v) { Audio.SetVolume(id, (float)v); }, delegate (bool mute) { Audio.SetMute(id, mute); }, null);
                _devRows.Add(Tuple.Create(id, d.Name ?? "", r));
            }

            _scroll = new ScrollViewer();
            if (_o.Columns)
            {
                // columns: the devices' volumes first, a line, then the apps side by side (scrolling sideways when the widget is too narrow)
                var strip = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var dr in _devRows) strip.Children.Add(dr.Item3.Root);
                if (_o.ShowApps)
                {
                    strip.Children.Add(new Border { Width = 1, Background = t.Track, Margin = new Thickness(6, 4, 6, 4) });
                    _apps.Orientation = Orientation.Horizontal;
                    _scroll.Content = _apps;
                    _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    strip.Children.Add(_scroll);
                }
                root.Children.Add(strip);
            }
            else
            {
                foreach (var dr in _devRows) root.Children.Add(dr.Item3.Root);
                if (_o.ShowApps)
                {
                    root.Children.Add(new Border { Height = 1, Background = t.Track, Margin = new Thickness(0, 8, 0, 6) });
                    _scroll.Content = _apps;
                    _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    root.Children.Add(_scroll);
                }
            }
            if (inWidget != null) WidgetWindow.StyleScrollbars(_scroll);

            // the Windows pages
            if (_o.Links)
            {
                var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
                links.Children.Add(Link("Sound settings", "ms-settings:sound"));
                links.Children.Add(Link("Windows volume mixer", "ms-settings:apps-volume"));
                root.Children.Add(links);
            }
            Child = root;

            Refresh(true);
            _timer.Tick += delegate { Tick(); };
            Loaded += delegate { _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); Audio.Release(_list); _list = new List<AudioApp>(); };
        }

        // ---- building --------------------------------------------------------------------------------------
        TextBlock Glyph(string g, Brush b, double size)
        {
            return new TextBlock { Text = g, FontFamily = IconUtil.GlyphFont, FontSize = size, Foreground = b, VerticalAlignment = VerticalAlignment.Center };
        }

        TextBlock Link(string text, string target)
        {
            var l = new TextBlock { Text = text, FontFamily = _t.Font, FontSize = _t.FontSize - 0.5, Foreground = _t.MainAccent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 18, 0) };
            l.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            l.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_popup != null) _popup.CloseSafe();
                SettingsLauncher.Open(target);
            };
            return l;
        }

        /// <summary>
        /// A row: the icon (click to mute), the name, the slider and the percentage - or, in columns, the same upright: the percentage,
        /// the slider, the icon and the name (sub = the device's short name under it, for an app on another device).
        /// </summary>
        Row MakeRow(string key, FrameworkElement icon, string name, Action<double> setVolume, Action<bool> setMute, string sub)
        {
            var row = new Row { Key = key, Icon = icon };
            bool col = _o.Columns;

            var iconHost = new Border { Child = icon, Background = Brushes.Transparent, Cursor = Cursors.Hand, Width = 24, Height = 24, CornerRadius = new CornerRadius(4), HorizontalAlignment = col ? HorizontalAlignment.Center : HorizontalAlignment.Left };
            iconHost.ToolTip = "Click to mute / unmute";
            iconHost.MouseEnter += delegate { iconHost.Background = _t.Hover; };
            iconHost.MouseLeave += delegate { iconHost.Background = Brushes.Transparent; };
            iconHost.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            iconHost.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                row.Muted = !row.Muted;
                setMute(row.Muted);
                Show(row, row.Slider.Value, row.Muted);
            };

            var label = new TextBlock { Text = name, FontFamily = _t.Font, FontSize = col ? _t.FontSize - 1 : _t.FontSize, Foreground = _t.Fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name };
            row.Label = label;

            row.Slider = new MixerSlider(_t, col ? ColumnH : SliderW, _o.Meters, col);
            row.Slider.Changed += delegate (double v)
            {
                setVolume(v);
                if (v > 0 && row.Muted) row.Muted = false;      // turning it up unmutes (Windows does too)
                Show(row, v, row.Muted);
            };
            row.Pct = new TextBlock { FontFamily = _t.Font, FontSize = _t.FontSize - 0.5, Foreground = _t.FgDim, VerticalAlignment = VerticalAlignment.Center, TextAlignment = col ? TextAlignment.Center : TextAlignment.Right };

            if (col)
            {
                var c = new StackPanel { Width = ColumnW, Margin = new Thickness(2, 0, 2, 0) };
                row.Pct.HorizontalAlignment = HorizontalAlignment.Center;
                row.Slider.HorizontalAlignment = HorizontalAlignment.Center;
                row.Slider.Margin = new Thickness(0, 4, 0, 4);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.TextAlignment = TextAlignment.Center;
                label.MaxWidth = ColumnW;
                label.Margin = new Thickness(0, 2, 0, 0);
                c.Children.Add(row.Pct);
                c.Children.Add(row.Slider);
                c.Children.Add(iconHost);
                c.Children.Add(label);
                if (!string.IsNullOrEmpty(sub))
                    c.Children.Add(new TextBlock { Text = sub, FontFamily = _t.Font, FontSize = _t.FontSize - 2, Foreground = _t.FgDim, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = ColumnW });
                row.Root = c;
                return row;
            }

            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameW) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SliderW) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.Children.Add(iconHost);
            label.Margin = new Thickness(4, 0, 8, 0);
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            Grid.SetColumn(row.Slider, 2);
            g.Children.Add(row.Slider);
            Grid.SetColumn(row.Pct, 3);
            g.Children.Add(row.Pct);
            row.Root = g;
            return row;
        }

        void Show(Row row, double volume, bool muted)
        {
            row.Muted = muted;
            if (!row.Slider.Dragging) row.Slider.Value = volume;
            row.Pct.Text = muted ? "muted" : Math.Round(volume * 100) + "%";
            row.Icon.Opacity = muted ? 0.35 : 1;
            row.Slider.Opacity = muted ? 0.5 : 1;
            var glyph = row.Icon as TextBlock;
            if (glyph != null && row.Key.StartsWith("dev:"))
                glyph.Text = muted || volume <= 0 ? "" : volume < 0.34 ? "" : volume < 0.67 ? "" : "";
        }

        FrameworkElement AppIcon(AudioApp a)
        {
            if (a.IsSystem) return Glyph("", _t.Fg, _t.FontSize + 3);
            ImageSource src = a.ExePath.Length > 0 ? IconUtil.FromPath(a.ExePath) : null;
            if (src == null) return Glyph("", _t.Fg, _t.FontSize + 3);
            return new Image { Source = src, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        // ---- refreshing ------------------------------------------------------------------------------------
        void Tick()
        {
            if (_inWidget != null && ModuleVisibility.Covered(_inWidget)) return;      // a widget nobody can see: wait
            if (_o.Meters)
            {
                foreach (var dr in _devRows) dr.Item3.Slider.Peak = Audio.Peak(dr.Item1);
                foreach (AudioApp a in _list)
                {
                    Row r;
                    if (_rows.TryGetValue(a.Key, out r)) r.Slider.Peak = Audio.Peak(a);
                }
            }
            if (++_tick % 12 == 0) Refresh(false);      // about once a second
        }

        /// <summary>The device, the master volume and the apps as they are now (the app rows are made again only when apps came or went).</summary>
        void Refresh(bool device)
        {
            // the device's name at the top, and its short name on its volume row ("Media", "System"); followed every refresh, so a change of
            // the default device shows (the name is looked up at most every 3 seconds)
            string name = Audio.DeviceName(_o.DeviceId);
            _device.Text = name.Length > 0 ? name : _o.DeviceId.Length > 0 ? "This module's sound device isn't connected" : "No output device";
            foreach (var dr in _devRows)
            {
                Row row = dr.Item3;
                string full = Audio.DeviceName(dr.Item1);
                bool there = full.Length > 0;
                // a device that isn't connected keeps its (saved) name, marked off
                string label = there ? Audio.ShortName(full) : dr.Item2.Length > 0 ? Audio.ShortName(dr.Item2) + " (off)" : "All sound";
                if (row.Label.Text != label) { row.Label.Text = label; row.Label.ToolTip = there ? full + " - its volume" : dr.Item2.Length > 0 ? dr.Item2 + " isn't connected" : null; }
                float v; bool m;
                if (Audio.TryGet(dr.Item1, out v, out m)) Show(row, v, m);
                else { row.Pct.Text = "n/a"; row.Slider.Opacity = 0.35; row.Icon.Opacity = 0.35; }
            }
            if (!_o.ShowApps) return;       // only the devices: the apps aren't even read

            List<AudioApp> now = Audio.Apps();
            // the mixer's device(s): the one it's for (or the default), or every device picked for the widget
            var mains = new HashSet<string>();
            bool several = _several;
            if (several) foreach (var dr in _devRows) mains.Add(dr.Item1);
            else mains.Add(_o.DeviceId.Length > 0 ? _o.DeviceId : Audio.DefaultDeviceId());
            // every app on the mixer's device(s) (as Windows' mixer) - or, with "only apps playing", just the ones playing; on other
            // devices only the ones playing. Once seen playing an app stays listed while the mixer is open, so a row doesn't come and go
            // with every pause. Other devices' system sounds never (every device has some). The mixer's device(s) come first.
            var shown = new List<AudioApp>();
            foreach (AudioApp a in now)
            {
                bool onMain = mains.Contains(a.DeviceId);
                if (a.Active && (onMain || !a.IsSystem)) _seen.Add(a.Key);
                if ((onMain && !_o.PlayingOnly) || _seen.Contains(a.Key)) shown.Add(a);
            }
            var mainFirst = new List<AudioApp>();
            foreach (AudioApp a in shown) if (mains.Contains(a.DeviceId)) mainFirst.Add(a);
            foreach (AudioApp a in shown) if (!mains.Contains(a.DeviceId)) mainFirst.Add(a);
            shown = mainFirst;
            // which apps get their device's name: with one device, those on another; with several, all of them (there's no one device at the top)
            Func<AudioApp, bool> labelled = delegate (AudioApp a) { return several || !mains.Contains(a.DeviceId); };
            var keys = new List<string>();
            foreach (AudioApp a in shown) keys.Add(a.Key);
            string sig = string.Join("|", keys);
            if (sig != _sig)
            {
                _sig = sig;
                _apps.Children.Clear();
                _rows.Clear();
                string shownDevice = null;
                foreach (AudioApp a in shown)
                {
                    // the mixer's device's apps come first, under the device at the top; any other device gets a heading (in columns, each
                    // such app has its device's short name under its name instead)
                    if (!_o.Columns && labelled(a) && a.DeviceName != shownDevice)
                    {
                        shownDevice = a.DeviceName;
                        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 2) };
                        head.Children.Add(Glyph("", _t.FgDim, _t.FontSize - 1));
                        head.Children.Add(new TextBlock
                        {
                            Text = a.DeviceName, FontFamily = _t.Font, FontSize = _t.FontSize - 1, Foreground = _t.FgDim, Margin = new Thickness(6, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300
                        });
                        _apps.Children.Add(head);
                    }
                    string key = a.Key;
                    Row r = MakeRow(key, AppIcon(a), a.Name,
                                    delegate (double vol) { AudioApp cur = Find(key); if (cur != null) Audio.SetVolume(cur, (float)vol); },
                                    delegate (bool mute) { AudioApp cur = Find(key); if (cur != null) Audio.SetMute(cur, mute); },
                                    labelled(a) ? Audio.ShortName(a.DeviceName) : null);
                    _rows[key] = r;
                    Panel line = r.Root;
                    // right-click: which device the app plays on (Windows 11; not for system sounds)
                    if (!a.IsSystem && a.Pids.Count > 0 && Audio.AppDevicesAvailable)
                    {
                        line.ToolTip = a.Name + "\nRight-click to choose which device it plays on";
                        line.Background = Brushes.Transparent;
                        line.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; AppDeviceMenu(key, line); };
                    }
                    _apps.Children.Add(line);
                }
                if (shown.Count == 0)
                    _apps.Children.Add(new TextBlock { Text = "No apps are playing sound right now.", FontFamily = _t.Font, FontSize = _t.FontSize, Foreground = _t.FgDim, Margin = new Thickness(4, 2, 0, 2) });
                if (_inWidget != null) _inWidget.ContentChanged();      // apps came or went: the widget fits its new height
            }
            foreach (AudioApp a in now)
            {
                Row r;
                float av; bool am;
                if (_rows.TryGetValue(a.Key, out r) && Audio.TryGet(a, out av, out am)) Show(r, av, am);
            }
            Audio.Release(_list);
            _list = now;
        }

        AudioApp Find(string key)
        {
            foreach (AudioApp a in _list) if (a.Key == key) return a;
            return null;
        }

        /// <summary>Right-click on an app: the devices it can play on (Windows remembers the choice for that app).</summary>
        void AppDeviceMenu(string key, FrameworkElement at)
        {
            AudioApp app = Find(key);
            if (app == null) return;
            string now = Audio.AppDevice(app);
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = app.Name + " plays on", IsEnabled = false });
            Action<string, string> add = delegate (string header, string id)
            {
                var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = id == now };
                item.Click += delegate
                {
                    AudioApp cur = Find(key);
                    if (cur == null) return;
                    if (!Audio.SetAppDevice(cur, id))
                        Questions.Tell(Window.GetWindow(this), "Windows didn't take that for " + cur.Name + " - see the log.", "Volume mixer", MessageBoxImage.Warning);
                    _sig = null;            // it moves to another device's list
                    Refresh(false);
                };
                m.Items.Add(item);
            };
            add("Default output device", "");
            m.Items.Add(new Separator());
            foreach (AudioDevice d in Audio.Devices()) add(d.Name, d.Id);
            m.Items.Add(new Separator());
            m.Items.Add(new MenuItem { Header = "Some apps switch straight away, others when they play the next sound or restart.", IsEnabled = false });
            if (_popup != null) _popup.Pinned = true;
            m.Closed += delegate { if (_popup != null) _popup.Pinned = false; };
            m.PlacementTarget = at;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        void DeviceMenu(FrameworkElement at)
        {
            var m = new ContextMenu();
            foreach (AudioDevice d in Audio.Devices())
            {
                AudioDevice dev = d;
                var item = new MenuItem { Header = d.Name, IsCheckable = true, IsChecked = d.IsDefault };
                item.Click += delegate
                {
                    Audio.SetDefault(dev.Id);
                    _sig = null;            // the apps are listed per device
                    Refresh(true);
                };
                m.Items.Add(item);
            }
            if (m.Items.Count == 0) m.Items.Add(new MenuItem { Header = "No output devices", IsEnabled = false });
            // the popup stays open while its menu is (the menu takes the focus for a moment)
            if (_popup != null) _popup.Pinned = true;
            m.Closed += delegate { if (_popup != null) _popup.Pinned = false; };
            m.PlacementTarget = at;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            m.IsOpen = true;
        }
    }

    /// <summary>
    /// The mixer in a widget (a Volume module with "Show the mixer in the widget"): always there, reading the sound while the widget
    /// can be seen. Sizes itself (no fixed widget rows).
    /// </summary>
    public sealed class VolumeMixerModule : Border, IMultiRow
    {
        public int RowCount { get { return 0; } }

        public VolumeMixerModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            Child = new VolumeMixer(host.Theme.ForModule(cfg), VolumeMixer.Options.Of(cfg), host);
        }
    }

    /// <summary>
    /// A slim themed slider (0..1) with the sound level as a line beside its track: across (the line under it), or upright (vertical =
    /// true: filling from the bottom, the line to its right). Drag, click or scroll to change it.
    /// </summary>
    sealed class MixerSlider : Grid
    {
        readonly Border _track, _fill, _level;
        readonly Ellipse _thumb;
        readonly double _w;
        readonly bool _up;
        double _value, _peak;
        const double Thumb = 12;

        public bool Dragging { get; private set; }
        /// <summary>The value changed by the user (not when set from code).</summary>
        public event Action<double> Changed;

        /// <param name="length">Its width (across) or height (upright).</param>
        public MixerSlider(Theme t, double length, bool meter = true, bool vertical = false)
        {
            _w = length;
            _up = vertical;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            double pad = Thumb / 2;
            if (!vertical)
            {
                Width = length;
                Height = 24;
                VerticalAlignment = VerticalAlignment.Center;
                _track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = t.Track, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(pad, 0, pad, 0) };
                _fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = t.MainAccent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(pad, 0, 0, 0) };
                // the sound level: its own thin line just under the track (inside the accent-coloured part it was hard to see)
                _level = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Theme.Faded(t.Fg, 75), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(pad, 14, 0, 0) };
                _thumb = new Ellipse { Width = Thumb, Height = Thumb, Fill = t.Fg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            }
            else
            {
                Width = 26;
                Height = length;
                _track = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = t.Track, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, pad, 0, pad) };
                _fill = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = t.MainAccent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, pad) };
                _level = new Border { Width = 2, CornerRadius = new CornerRadius(1), Background = Theme.Faded(t.Fg, 75), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, pad) };
                _thumb = new Ellipse { Width = Thumb, Height = Thumb, Fill = t.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            }
            Children.Add(_track);
            Children.Add(_fill);
            if (meter) Children.Add(_level);
            Children.Add(_thumb);
            Draw();

            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Dragging = true;
                CaptureMouse();
                FromPointer(e.GetPosition(this));
            };
            MouseMove += delegate (object s, MouseEventArgs e) { if (Dragging) FromPointer(e.GetPosition(this)); };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; ReleaseMouseCapture(); };
            LostMouseCapture += delegate { Dragging = false; };
            MouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                e.Handled = true;
                Set(_value + (e.Delta > 0 ? 0.02 : -0.02));
            };
        }

        double Usable { get { return Math.Max(1, _w - Thumb); } }

        void FromPointer(Point p) { Set(_up ? (_w - p.Y - Thumb / 2) / Usable : (p.X - Thumb / 2) / Usable); }

        void Set(double v)
        {
            v = Math.Round(Math.Max(0, Math.Min(1, v)), 2);
            if (v == _value) return;
            _value = v;
            Draw();
            Action<double> c = Changed;
            if (c != null) c(v);
        }

        /// <summary>The volume 0..1 (setting it doesn't raise Changed).</summary>
        public double Value
        {
            get { return _value; }
            set { _value = Math.Max(0, Math.Min(1, value)); Draw(); }
        }

        /// <summary>
        /// The sound level 0..1 right now (Windows' peak amplitude), drawn as a line under the track. Shown on a square-root curve:
        /// ordinary listening peaks are small numbers (0.01 - 0.3) that would barely show as they are.
        /// </summary>
        public double Peak
        {
            get { return _peak; }
            set
            {
                double p = Math.Sqrt(Math.Max(0, Math.Min(1, value)));
                if (Math.Abs(p - _peak) < 0.01) return;
                _peak = p;
                if (_up) _level.Height = Usable * _peak;
                else _level.Width = Usable * _peak;
            }
        }

        void Draw()
        {
            if (_up)
            {
                _fill.Height = Usable * _value;
                _level.Height = Usable * _peak;
                _thumb.Margin = new Thickness(0, 0, 0, Usable * _value);
                return;
            }
            _fill.Width = Usable * _value;
            _level.Width = Usable * _peak;
            _thumb.Margin = new Thickness(Usable * _value, 0, 0, 0);
        }
    }
}
