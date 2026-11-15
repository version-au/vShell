using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The welcome guide: four short steps shown on a first start (and from Settings > About) - Start-up, Theme, Layout, Plugins. Every
    /// choice is applied to the real bars the moment it is made, so the desktop behind the window is the preview; Back only goes back
    /// to the previous step's controls. Skip, Finish and closing the window all keep what was chosen and end the guide
    /// (BarSettings.WelcomePending). Coloured from the vShell theme like the settings window (SettingsStyle / SettingsPalette).
    /// </summary>
    public sealed class WelcomeWindow : Window
    {
        readonly BarManager _mgr;
        BarSettings S { get { return _mgr.Settings; } }

        const int Steps = 4;
        static readonly string[] StepNames = { "Welcome", "Theme", "Layout", "Plugins" };
        int _step;
        bool _darkChrome;

        readonly StackPanel _dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _count;
        readonly ScrollViewer _body;
        readonly Button _skip, _back, _next;
        readonly VShellMark _mark;

        // the layout step's parts, kept so they can be taken away again
        BarPlacement _topBar;                 // the CPU / memory / GPU bar from the first start (or made here)
        WidgetSettings _widget;               // the system widget made here (clock, CPU, memory, disk)
        readonly List<WindowsPin> _imported = new List<WindowsPin>();   // Windows' pins put on the taskbars by this guide
        readonly HashSet<string> _picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // plugins ticked (added on Finish)

        public WelcomeWindow(BarManager mgr)
        {
            _mgr = mgr;
            Title = "Welcome to vShell";
            Width = Math.Min(820, Math.Max(640, SystemParameters.WorkArea.Width - 120));
            Height = Math.Min(640, Math.Max(480, SystemParameters.WorkArea.Height - 120));
            MinWidth = 600;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true;
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/vshell.ico")); } catch { }


            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Ref(root, Panel.BackgroundProperty, "vsBg");

            // ---- header: the mark, the step's name on the left; "1 of 4" and the dots on the right ----
            var head = new Grid { Margin = new Thickness(28, 22, 28, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _mark = new VShellMark { Dim = 34, ShowLine = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            head.Children.Add(_mark);
            var brand = Txt("vShell", "vsFg", 15, true);
            Grid.SetColumn(brand, 1);
            head.Children.Add(brand);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _count = Txt("", "vsDim", 12.5, false);
            _count.Margin = new Thickness(0, 0, 12, 0);
            right.Children.Add(_count);
            right.Children.Add(_dots);
            Grid.SetColumn(right, 2);
            head.Children.Add(right);
            root.Children.Add(head);

            // ---- the step ----
            _body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(28, 8, 28, 8) };
            Grid.SetRow(_body, 1);
            root.Children.Add(_body);

            // ---- footer: Skip on the left, Back and Next / Finish on the right ----
            var foot = new Grid { Margin = new Thickness(28, 10, 28, 20) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _skip = Btn("Skip the guide", delegate { Close(); });
            _skip.ToolTip = "Close the guide and keep what you've chosen so far. Settings > About shows it again.";
            foot.Children.Add(_skip);
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            _back = Btn("‹  Back", delegate { Go(_step - 1); });
            _back.Margin = new Thickness(0, 0, 8, 0);
            _back.MinWidth = 96;
            _next = Btn("Next  ›", delegate { if (_step == Steps - 1) Finish(); else Go(_step + 1); }, true);
            _next.MinWidth = 110;
            nav.Children.Add(_back);
            nav.Children.Add(_next);
            Grid.SetColumn(nav, 2);
            foot.Children.Add(nav);
            Grid.SetRow(foot, 2);
            root.Children.Add(foot);

            Content = root;
            ApplyChrome();
            Closing += delegate { Done(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            Go(0);
        }

        // =================================================================
        // Chrome (the settings window's colours)
        // =================================================================
        void ApplyChrome()
        {
            // the suite's colours and controls, as the settings window (Windows' own title bar here, so no glass)
            try { _look = SettingsStyle.Dress(this, S, true); _darkChrome = _look.IsDark; }
            catch (Exception ex)
            {
                Log.Write("Welcome look: " + ex.Message);
                SettingsPalette p = SettingsPalette.Plain();
                SettingsStyle.SetBrushes(this, p);
                _darkChrome = p.Dark;
            }
            Theme t = Theme.From(S, 12, 8);
            _mark.Dot1 = t.MainAccent; _mark.Dot2 = t.SecondaryAccent; _mark.Dot3 = t.Fg;
            _mark.InvalidateVisual();
            SetDarkTitleBar();
        }

        /// <summary>The suite theme the window shows (null if it couldn't be made).</summary>
        vSuite.Ui.Theme _look;

        void SetDarkTitleBar()
        {
            // the suite's look: the wallpaper under the theme colour at the theme's opacity (as the bars), dark mode, corners
            if (_look != null && new WindowInteropHelper(this).Handle != IntPtr.Zero)
            {
                try { vSuite.Ui.WindowEffects.Look(this, _look, true); return; }
                catch (Exception ex) { Log.Write("Welcome look: " + ex.Message); }
            }
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;
                int on = _darkChrome ? 1 : 0;
                Native.DwmSetWindowAttribute(h, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref on, 4);
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetDarkTitleBar();
        }

        static void Ref(FrameworkElement e, DependencyProperty prop, string key) { e.SetResourceReference(prop, key); }

        static TextBlock Txt(string text, string brushKey, double size, bool bold)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        static Button Btn(string text, Action click, bool primary = false)
        {
            var b = new Button { Content = text };
            if (primary) b.SetResourceReference(FrameworkElement.StyleProperty, "vsPrimary");
            b.Click += delegate { click(); };
            return b;
        }

        // =================================================================
        // Steps
        // =================================================================
        void Go(int step)
        {
            _step = Math.Max(0, Math.Min(Steps - 1, step));
            _count.Text = "Step " + (_step + 1) + " of " + Steps + "  ·  " + StepNames[_step];
            _dots.Children.Clear();
            for (int i = 0; i < Steps; i++)
            {
                var d = new Border { Width = i == _step ? 18 : 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 5, 0) };
                Ref(d, Border.BackgroundProperty, i == _step ? "vsAccent" : "vsCtrlBorder");
                _dots.Children.Add(d);
            }
            _back.IsEnabled = _step > 0;
            _next.Content = _step == Steps - 1 ? "Finish" : "Next  ›";
            _skip.Visibility = _step == Steps - 1 ? Visibility.Hidden : Visibility.Visible;

            StackPanel page;
            switch (_step)
            {
                case 0: page = StartStep(); break;
                case 1: page = ThemeStep(); break;
                case 2: page = LayoutStep(); break;
                default: page = PluginsStep(); break;
            }
            _body.Content = page;
            _body.ScrollToTop();
        }

        void Redraw() { Dispatcher.BeginInvoke(new Action(delegate { Go(_step); })); }

        /// <summary>The step's title and the line under it.</summary>
        static StackPanel Page(string title, string sub)
        {
            var p = new StackPanel();
            p.Children.Add(Txt(title, "vsFg", 26, true));
            if (!string.IsNullOrEmpty(sub))
            {
                var s = Txt(sub, "vsDim", 13, false);
                s.Margin = new Thickness(0, 4, 0, 0);
                p.Children.Add(s);
            }
            return p;
        }

        /// <summary>A titled card; the rows go into the panel it returns.</summary>
        static StackPanel Card(StackPanel page, string header)
        {
            if (!string.IsNullOrEmpty(header))
            {
                var t = Txt(header, "vsFg", 13.5, true);
                t.Margin = new Thickness(2, 18, 0, 8);
                page.Children.Add(t);
            }
            var body = new StackPanel();
            var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 6, 16, 6), Child = body,
                                    Margin = new Thickness(0, string.IsNullOrEmpty(header) ? 16 : 0, 0, 0) };
            Ref(card, Border.BackgroundProperty, "vsCard");
            Ref(card, Border.BorderBrushProperty, "vsLine");
            page.Children.Add(card);
            return body;
        }

        static void Note(StackPanel card, string text)
        {
            var t = Txt(text, "vsDim", 12, false);
            t.Margin = new Thickness(0, 0, 0, 10);
            card.Children.Add(t);
        }

        /// <summary>A switch row: the text (and a dim line under it) on the left, the switch on the right; the whole row flips it.</summary>
        CheckBox Toggle(StackPanel card, string text, string note, Func<bool> get, Action<bool> set)
        {
            var cb = new CheckBox { IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(Txt(text, "vsFg", 13.5, false));
            if (!string.IsNullOrEmpty(note))
            {
                var n = Txt(note, "vsDim", 12, false);
                n.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(n);
            }
            g.Children.Add(left);
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            var wrap = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = g };
            Action changed = delegate { set(cb.IsChecked == true); };
            cb.Click += delegate { changed(); };
            wrap.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!cb.IsEnabled || !wrap.IsEnabled) return;
                if (e.OriginalSource is DependencyObject && IsInside((DependencyObject)e.OriginalSource, cb)) return;
                cb.IsChecked = cb.IsChecked != true;
                changed();
            };
            card.Children.Add(wrap);
            return cb;
        }

        static bool IsInside(DependencyObject d, DependencyObject parent)
        {
            while (d != null)
            {
                if (d == parent) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>One of a few choices: a dot, a title and a line about it; the picked one has the accent outline.</summary>
        static void Choice(StackPanel card, string title, string about, bool picked, Action pick)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ring = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(picked ? 6 : 1.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            Ref(ring, Border.BorderBrushProperty, picked ? "vsAccent" : "vsDim");
            g.Children.Add(ring);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Txt(title, "vsFg", 13.5, picked));
            var a = Txt(about, "vsDim", 12, false);
            a.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(a);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            var row = new Border { Child = g, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(picked ? 2 : 1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 0, 4), Cursor = Cursors.Hand };
            Ref(row, Border.BackgroundProperty, picked ? "vsSelBg" : "vsCtrl");
            Ref(row, Border.BorderBrushProperty, picked ? "vsAccent" : "vsLine");
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (!picked) pick(); };
            card.Children.Add(row);
        }

        // ---- 1: Welcome ----------------------------------------------------------------------------
        StackPanel StartStep()
        {
            var p = Page("Welcome to vShell",
                         "vShell runs alongside Explorer - it never replaces the Windows shell. It has put a taskbar along the bottom of your main screen " +
                         "(Start, your windows, notifications, the tray and a clock) and a small bar with CPU, memory and GPU graphs at the top.");
            var intro = Txt("A few choices to start with. Everything here is applied as you go, and can be changed later in Settings - click vShell's tray icon, " +
                            "or right-click a bar.", "vsDim", 13, false);
            intro.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(intro);

            StackPanel c = Card(p, "Start-up");
            Toggle(c, "Start vShell when I sign in", "An entry in Startup apps - the same switch as Task Manager's Startup tab.",
                   delegate { return StartupManager.IsSelfEnabled(); },
                   delegate (bool v)
                   {
                       string err = StartupManager.SetSelf(v);
                       if (err != null) Questions.Tell(this, "Could not change vShell's startup entry: " + err, "Welcome");
                   });
            Toggle(c, "Hide the Windows taskbar", "vShell's taskbar takes its place. Off = both are shown.",
                   delegate { return S.HideWindowsTaskbar; }, delegate (bool v) { S.HideWindowsTaskbar = v; _mgr.SettingsChanged(); });
            Toggle(c, "Show vShell in the system tray", "Click it for Settings; right-click it for notifications, updates, Restart vShell and Exit vShell.",
                   delegate { return S.ShowTrayIcon; }, delegate (bool v) { S.ShowTrayIcon = v; _mgr.SettingsChanged(); });
            return p;
        }

        // ---- 2: Theme ------------------------------------------------------------------------------
        StackPanel ThemeStep()
        {
            var p = Page("Pick a theme", "Click one - your bars change as you go. A theme sets the colours, the bar style and the widget style together; " +
                                          "Settings > Appearance has each part on its own, and keeps your own themes.");
            var wrap = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            foreach (LookTheme t in Styles.BuiltInThemes)
            {
                LookTheme tt = t;
                bool sel = SettingsWindow.ThemeInUse(S, t);
                wrap.Children.Add(ThemeCard(t.Name, SettingsWindow.ThemePic(SettingsWindow.PalOfTheme(S, t), t.Bars ?? S.BarStyle, t.Widgets ?? S.WidgetStyle), sel, delegate
                {
                    SettingsWindow.ApplyTheme(S, tt);
                    _mgr.SettingsChanged();
                    ApplyChrome();
                    Redraw();
                }));
            }
            p.Children.Add(wrap);
            return p;
        }

        static Border ThemeCard(string name, UIElement pic, bool sel, Action click)
        {
            var nameRow = new DockPanel { Margin = new Thickness(10, 7, 10, 8) };
            if (sel)
            {
                var tick = new TextBlock { Text = "", FontFamily = IconUtil.GlyphFont, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                Ref(tick, TextBlock.ForegroundProperty, "vsAccent");
                DockPanel.SetDock(tick, Dock.Right);
                nameRow.Children.Add(tick);
            }
            var nt = Txt(name, "vsFg", 12.5, false);
            nt.TextTrimming = TextTrimming.CharacterEllipsis;
            nt.TextWrapping = TextWrapping.NoWrap;
            nameRow.Children.Add(nt);
            var stack = new StackPanel();
            stack.Children.Add(new Border { CornerRadius = new CornerRadius(7, 7, 0, 0), ClipToBounds = true, Child = pic });
            stack.Children.Add(nameRow);
            var card = new Border
            {
                Width = 164, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(sel ? 2 : 1), Margin = new Thickness(0, 0, 10, 10),
                Child = stack, Cursor = Cursors.Hand, ClipToBounds = true
            };
            Ref(card, Border.BackgroundProperty, "vsCard");
            Ref(card, Border.BorderBrushProperty, sel ? "vsAccent" : "vsLine");
            card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return card;
        }

        // ---- 3: Layout -----------------------------------------------------------------------------
        /// <summary>The bar with the taskbar on the main monitor (else the first with a taskbar, else the first bar).</summary>
        BarPlacement MainBar()
        {
            MonitorInfo prim = MonitorHelper.Primary();
            BarPlacement any = null;
            foreach (BarPlacement b in S.Bars)
            {
                if (b == null || b.Modules == null || !b.Modules.Exists(delegate (ModuleConfig m) { return m != null && m.Type == ModuleTypes.Taskbar; })) continue;
                if (any == null) any = b;
                if (prim == null || b.Monitor.Length == 0 || string.Equals(b.Monitor, prim.DeviceName, StringComparison.OrdinalIgnoreCase)) return b;
            }
            return any ?? (S.Bars.Count > 0 ? S.Bars[0] : null);
        }

        /// <summary>The CPU / memory / GPU bar a first start makes (a Top bar with only those on it).</summary>
        BarPlacement FindTopBar()
        {
            if (_topBar != null && S.Bars.Contains(_topBar)) return _topBar;
            foreach (BarPlacement b in S.Bars)
            {
                if (b == null || b.Edge != "Top" || b.Modules == null || b.Modules.Count == 0) continue;
                bool graphs = true;
                foreach (ModuleConfig m in b.Modules)
                    if (m == null || (m.Type != ModuleTypes.Cpu && m.Type != ModuleTypes.Memory && m.Type != ModuleTypes.Gpu)) graphs = false;
                if (graphs) return b;
            }
            return null;
        }

        WidgetSettings FindWidget()
        {
            if (_widget != null && S.Widgets.Contains(_widget)) return _widget;
            return S.Widgets.Count > 0 ? S.Widgets[0] : null;
        }

        /// <summary>Every taskbar module on every bar.</summary>
        List<ModuleConfig> Taskbars()
        {
            var list = new List<ModuleConfig>();
            foreach (BarPlacement b in S.Bars)
                if (b != null && b.Modules != null)
                    foreach (ModuleConfig m in b.Modules) if (m != null && m.Type == ModuleTypes.Taskbar) list.Add(m);
            return list;
        }

        /// <summary>The monitors (other than the main one) that have no taskbar yet.</summary>
        List<MonitorInfo> MonitorsWithoutTaskbar()
        {
            var missing = new List<MonitorInfo>();
            foreach (MonitorInfo mon in MonitorHelper.GetAll())
            {
                if (mon.IsPrimary) continue;
                bool has = false;
                foreach (BarPlacement b in S.Bars)
                    if (b != null && b.Modules != null && string.Equals(b.Monitor, mon.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                        b.Modules.Exists(delegate (ModuleConfig m) { return m != null && m.Type == ModuleTypes.Taskbar; })) has = true;
                if (!has) missing.Add(mon);
            }
            return missing;
        }

        StackPanel LayoutStep()
        {
            var p = Page("Layout", "What goes on your screens. Changes show straight away; Settings > Bars and Settings > Widgets have the rest.");

            BarPlacement top = FindTopBar();
            WidgetSettings widget = FindWidget();
            int layout = widget != null ? 2 : top != null ? 1 : 0;
            StackPanel c = Card(p, "Bars and widgets");
            Choice(c, "Taskbar only", "The bottom bar: Start, your windows, notifications, the tray and a clock.", layout == 0,
                   delegate { SetLayout(0); });
            Choice(c, "Taskbar and system graphs", "Plus the small bar at the top with CPU, memory and GPU as line graphs (it doesn't take window space).", layout == 1,
                   delegate { SetLayout(1); });
            Choice(c, "Taskbar, system graphs and a desktop widget", "Plus a widget at the top right of the desktop, behind your windows: a big clock with the date, CPU and memory tiles " +
                   "and a disk ring. Drag it where you like, right-click it for more.", layout == 2,
                   delegate { SetLayout(2); });

            List<MonitorInfo> mons = MonitorHelper.GetAll();
            c = Card(p, "Monitors");
            if (mons.Count < 2)
                Note(c, "You have one monitor. With more than one, a taskbar can go on each - this guide (Settings > About) shows the choice then.");
            else
            {
                bool every = MonitorsWithoutTaskbar().Count == 0;
                CheckBox sync = null;
                Toggle(c, "Put a taskbar on every monitor",
                       "Each gets a Start button, the windows on that monitor and a clock, in the same look. Notifications and the tray stay on your main screen.",
                       delegate { return every; },
                       delegate (bool v) { SetEveryMonitor(v); if (sync != null) sync.IsEnabled = v; Redraw(); });
                sync = Toggle(c, "Same pins on every taskbar (synced)",
                              "Pin, unpin or reorder on one taskbar and the others follow. Off = each taskbar keeps its own pins.",
                              delegate { return SyncOn(); }, delegate (bool v) { SetSync(v); });
                sync.IsEnabled = every;
                ((Grid)sync.Parent).Margin = new Thickness(28, 8, 0, 8);      // under the switch above it
            }

            c = Card(p, "Pinned apps");
            Toggle(c, "Import the apps pinned to the Windows taskbar",
                   "Puts them on vShell's taskbar" + (mons.Count > 1 ? "s" : "") + ", in Windows' order. Pin more later: right-click an app on the taskbar, or drop a program onto it.",
                   delegate { return _imported.Count > 0; }, delegate (bool v) { ImportPins(v); });
            return p;
        }

        void SetLayout(int layout)
        {
            BarPlacement top = FindTopBar();
            WidgetSettings widget = FindWidget();
            if (layout >= 1 && top == null)
            {
                MonitorInfo prim = MonitorHelper.Primary();
                List<BarPlacement> first = BarPlacement.FirstRun(prim == null ? "" : prim.DeviceName);
                top = first[first.Count - 1];
                if (prim != null) top.MonitorKey = prim.Key;
                if (S.BarStyle != null) { string lay = Styles.LayoutOf(top); Styles.ApplyLook(top, Styles.LookFor(S.BarStyle, lay), lay); }
                _mgr.AddBar(top);
            }
            if (layout == 0 && top != null) _mgr.RemoveBar(top);
            _topBar = top;

            if (layout == 2 && widget == null)
            {
                widget = SystemWidget();
                S.Widgets.Add(widget);
                _mgr.SettingsChanged();
            }
            if (layout < 2 && widget != null)
            {
                S.Widgets.Remove(widget);
                _mgr.SettingsChanged();
            }
            _widget = widget;
            Redraw();
        }

        /// <summary>
        /// The widget the layout step makes: a big clock with the date, CPU and memory as tiles (label, number, sparkline) coloured by load,
        /// and the Windows drive as a ring - each in its own card, one under the other, against the right edge of the main screen near the
        /// top. Its look follows the widget style (FollowStyle), so the theme step keeps it in step with the bars.
        /// </summary>
        WidgetSettings SystemWidget()
        {
            MonitorInfo prim = MonitorHelper.Primary();

            ModuleConfig clock = ModuleConfig.Create(ModuleTypes.Clock);
            clock.TextSize = "L";
            clock.ClockLayout = "BigTime";
            clock.SecondaryDate = true;

            ModuleConfig cpu = Tile(ModuleTypes.Cpu);
            ModuleConfig mem = Tile(ModuleTypes.Memory);

            ModuleConfig disk = ModuleConfig.Create(ModuleTypes.Disk);
            disk.TextSize = "L";
            disk.Style = "Ring";
            disk.GraphWidth = 50;
            disk.GraphHeightPercent = 86;
            disk.ShowDetail = true;
            disk.LoadColors = true;
            disk.WarnOn = true; disk.WarnAt = 70; disk.WarnAuto = true; disk.WarnSetup = true;

            var w = new WidgetSettings
            {
                Name = "System",
                Monitor = prim == null ? "" : prim.DeviceName,
                MonitorKey = prim == null ? "" : prim.Key,
                Direction = "Vertical",
                CardPerModule = true,
                OffsetX = 60, OffsetY = 68,
                LinkX = new SnapLink { Rel = "EdgeRight", Gap = S.WidgetEdgeGap },
                Modules = new List<ModuleConfig> { clock, cpu, mem, disk }
            };
            if (S.WidgetStyle != null) Styles.ApplyWidget(w, S.WidgetStyle);
            return w;
        }

        /// <summary>A CPU / memory module as a tile with a thick, fading sparkline, coloured by load and warning at 70 %.</summary>
        static ModuleConfig Tile(string type)
        {
            ModuleConfig m = ModuleConfig.Create(type);
            m.TextSize = "L";
            m.Style = "Tile";
            m.GraphWidth = 60;
            m.GraphLine = "Thick";
            m.GraphFill = "Fade";
            m.LoadColors = true;
            m.WarnOn = true; m.WarnAt = 70; m.WarnAuto = true; m.WarnSetup = true;
            return m;
        }

        /// <summary>A copy of the main bar for another monitor: its look and layout, with Start, the taskbar and the clock (new ids).</summary>
        static BarPlacement CopyFor(BarPlacement src, MonitorInfo mon)
        {
            var c = JsonSerializer.Deserialize<BarPlacement>(JsonSerializer.Serialize(src));
            c.Id = Guid.NewGuid().ToString("N");
            c.Monitor = mon.DeviceName;
            c.MonitorKey = mon.Key;
            c.Modules = new List<ModuleConfig>();
            if (src.Modules != null)
                foreach (ModuleConfig m in src.Modules)
                {
                    if (m == null || (m.Type != ModuleTypes.StartMenu && m.Type != ModuleTypes.Taskbar && m.Type != ModuleTypes.Clock)) continue;
                    var mc = JsonSerializer.Deserialize<ModuleConfig>(JsonSerializer.Serialize(m));
                    mc.Id = Guid.NewGuid().ToString("N");
                    c.Modules.Add(mc);
                }
            return c;
        }

        void SetEveryMonitor(bool on)
        {
            if (on)
            {
                BarPlacement main = MainBar();
                if (main == null) return;
                foreach (MonitorInfo mon in MonitorsWithoutTaskbar()) _mgr.AddBar(CopyFor(main, mon));
                Log.Write("Welcome: a taskbar on every monitor (" + S.Bars.Count + " bars)");
            }
            else
            {
                // the taskbars on the other monitors go (the main screen's bar stays)
                BarPlacement main = MainBar();
                foreach (BarPlacement b in new List<BarPlacement>(S.Bars))
                {
                    if (b == null || b == main || b.Modules == null) continue;
                    if (b.Monitor.Length == 0 || (main != null && string.Equals(b.Monitor, main.Monitor, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!b.Modules.Exists(delegate (ModuleConfig m) { return m != null && m.Type == ModuleTypes.Taskbar; })) continue;
                    _mgr.RemoveBar(b);
                }
            }
        }

        bool SyncOn()
        {
            List<ModuleConfig> bars = Taskbars();
            if (bars.Count == 0) return false;
            foreach (ModuleConfig m in bars) if (!m.TaskSyncPins) return false;
            return true;
        }

        void SetSync(bool on)
        {
            List<ModuleConfig> bars = Taskbars();
            if (on && S.SharedPins.Count == 0)
            {
                // the main taskbar's pins become the shared ones (each taskbar's own pins are kept for when sync is turned off)
                BarPlacement main = MainBar();
                ModuleConfig from = main == null ? null : main.Modules.Find(delegate (ModuleConfig m) { return m != null && m.Type == ModuleTypes.Taskbar; });
                if (from == null && bars.Count > 0) from = bars[0];
                if (from != null) foreach (PinnedApp own in from.Pins) S.SharedPins.Add(own.Copy());
            }
            foreach (ModuleConfig m in bars) m.TaskSyncPins = on;
            _mgr.SettingsChanged();
        }

        void ImportPins(bool on)
        {
            if (!on)
            {
                // take away what this guide put there (pins you added yourself stay)
                foreach (ModuleConfig m in Taskbars()) m.Pins.RemoveAll(delegate (PinnedApp x) { return _imported.Exists(delegate (WindowsPin wp) { return wp.Matches(x); }); });
                S.SharedPins.RemoveAll(delegate (PinnedApp x) { return _imported.Exists(delegate (WindowsPin wp) { return wp.Matches(x); }); });
                _imported.Clear();
                _mgr.SettingsChanged();
                return;
            }
            List<WindowsPin> pins;
            try
            {
                pins = WindowsPins.Read(delegate (string aumid)
                {
                    foreach (AppEntry e in AppCatalog.Get())
                        if (e.IsStore && string.Equals(e.Target, "shell:AppsFolder\\" + aumid, StringComparison.OrdinalIgnoreCase)) return e.Name;
                    return null;
                });
            }
            catch (Exception ex) { Log.Write("Welcome: import pins: " + ex.Message); pins = new List<WindowsPin>(); }
            if (pins.Count == 0)
            {
                Questions.Tell(this, "Windows' taskbar has no pinned apps vShell can read.", "Welcome");
                Redraw();
                return;
            }
            // every taskbar gets them (its own list), and so does the shared list - whichever is in use shows them
            var lists = new List<List<PinnedApp>>();
            foreach (ModuleConfig m in Taskbars()) lists.Add(m.Pins);
            lists.Add(S.SharedPins);
            foreach (List<PinnedApp> list in lists)
                foreach (WindowsPin wp in pins)
                    if (!list.Exists(delegate (PinnedApp x) { return wp.Matches(x); })) list.Add(wp.ToPin());
            _imported.Clear();
            _imported.AddRange(pins);
            Log.Write("Welcome: " + pins.Count + " pins imported from the Windows taskbar");
            _mgr.SettingsChanged();
        }

        // ---- 4: Plugins ----------------------------------------------------------------------------
        static readonly string[] PluginGroups = { "Smart home", "Servers & network", "Media", "Devices" };

        static int GroupOrder(string g)
        {
            int i = Array.IndexOf(PluginGroups, g);
            return i >= 0 ? i : g == "Other" ? 1000 : 100;
        }

        StackPanel PluginsStep()
        {
            var p = Page("Plugins", "Connect the services you use. Each adds modules for your bars and widgets, notifications and run box keywords. " +
                                     "Tick the ones you want: they're downloaded when you click Finish, then you sign in to each under Settings > Plugins.");
            List<PluginInfo> all;
            lock (PluginStore.Catalog) all = new List<PluginInfo>(PluginStore.Catalog);
            all.RemoveAll(delegate (PluginInfo i) { return !i.InRelease && !i.FromFile; });
            all.Sort(delegate (PluginInfo a, PluginInfo b)
            {
                int g = GroupOrder(a.Group).CompareTo(GroupOrder(b.Group));
                if (g != 0) return g;
                g = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
                return g != 0 ? g : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            string group = null;
            StackPanel c = null;
            foreach (PluginInfo info in all)
            {
                if (c == null || !string.Equals(info.Group, group, StringComparison.OrdinalIgnoreCase)) { group = info.Group; c = Card(p, group); }
                PluginRow(c, info);
            }
            if (all.Count == 0) Note(Card(p, "Plugins"), "No plugins are listed for this build - see Settings > Plugins.");

            c = Card(p, "Good to know");
            foreach (string tip in new[]
            {
                "Right-click a bar for its settings and modules; drag a bar to another edge or monitor.",
                "vShell's tray icon opens Settings; right-click it for notifications, updates, Restart vShell and Exit vShell.",
                "Widgets sit on the desktop behind your windows - Settings > Widgets > Add widget, then add modules to it.",
                "Settings > About shows this guide again."
            })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                Ref(dot, Border.BackgroundProperty, "vsAccent");
                row.Children.Add(dot);
                var t = Txt(tip, "vsFg", 13, false);
                t.MaxWidth = 640;
                row.Children.Add(t);
                c.Children.Add(row);
            }
            return p;
        }

        void PluginRow(StackPanel card, PluginInfo info)
        {
            bool added = PluginManager.IsAdded(info.Id);
            var cb = new CheckBox { IsChecked = added || _picked.Contains(info.Id), IsEnabled = !added, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var gl = new TextBlock { Text = info.Glyph ?? "", FontFamily = IconUtil.GlyphFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Ref(gl, TextBlock.ForegroundProperty, "vsFg");
            var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = gl, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            Ref(box, Border.BackgroundProperty, "vsCtrl");
            g.Children.Add(box);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(Txt(info.Name + (added ? "  ·  added" : ""), "vsFg", 13.5, false));
            string about = string.IsNullOrEmpty(info.Adds) ? info.Description : info.Adds;
            if (!string.IsNullOrEmpty(about))
            {
                var s = Txt(about, "vsDim", 12, false);
                s.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(s);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            Grid.SetColumn(cb, 2);
            g.Children.Add(cb);
            var wrap = new Border { Background = Brushes.Transparent, Cursor = added ? Cursors.Arrow : Cursors.Hand, Child = g, ToolTip = info.Description };
            Action changed = delegate { if (cb.IsChecked == true) _picked.Add(info.Id); else _picked.Remove(info.Id); };
            cb.Click += delegate { changed(); };
            wrap.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!cb.IsEnabled) return;
                if (e.OriginalSource is DependencyObject && IsInside((DependencyObject)e.OriginalSource, cb)) return;
                cb.IsChecked = cb.IsChecked != true;
                changed();
            };
            card.Children.Add(wrap);
        }

        // =================================================================
        // Finish / close
        // =================================================================
        bool _done;

        /// <summary>The guide is over (Finish, Skip, the window closed): it isn't shown at the next start.</summary>
        void Done()
        {
            if (_done) return;
            _done = true;
            if (S.WelcomePending)
            {
                S.WelcomePending = false;
                S.Save();
            }
        }

        async void Finish()
        {
            var picked = new List<PluginInfo>();
            lock (PluginStore.Catalog)
                foreach (PluginInfo i in PluginStore.Catalog) if (_picked.Contains(i.Id) && !PluginManager.IsAdded(i.Id)) picked.Add(i);
            Done();
            if (picked.Count == 0) { Close(); return; }

            _next.IsEnabled = false;
            _back.IsEnabled = false;
            _next.Content = "Adding…";
            var failed = new List<string>();
            foreach (PluginInfo p in picked)
            {
                string err;
                try { err = await PluginManager.AddAsync(p); }
                catch (Exception ex) { err = ex.Message; }
                if (err != null) failed.Add(p.Name + ": " + err);
            }
            Log.Write("Welcome: " + picked.Count + " plugins added" + (failed.Count > 0 ? ", " + failed.Count + " failed" : ""));
            Close();
            _mgr.OpenPluginsPage();
            if (failed.Count > 0) Questions.Tell(null, "Not added:\n" + string.Join("\n", failed) + "\n\nTry again under Settings > Plugins > Get more.", "Welcome");
        }
    }
}
