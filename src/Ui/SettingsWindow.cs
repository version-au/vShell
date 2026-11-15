using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Code-built settings window: a sidebar of pages made of cards (layout helpers in SettingsWindow.Layout.cs, colours in
    /// SettingsStyle.cs). Every change is applied live to the bars and widgets (debounced by the manager).
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        readonly BarManager _mgr;
        BarSettings S { get { return _mgr.Settings; } }
        bool _loading = true;

        readonly List<Action> _colorRefresh = new List<Action>();
        Slider _opacity;
        TextBox _opacityValue;
        string _selPreset;            // the preset picked last (a saved one can be deleted)
        bool _selPresetIsUser;

        StackPanel _barPanel;
        BarPlacement _selBar, _loadedBar;
        ModulePane _barPane, _widPane;
        StackPanel _widPanel;
        WidgetSettings _selWidget, _loadedWidget;
        WallpaperPalette _shownWall;     // the wallpaper colours the Appearance page shows (rebuilt when the wallpaper changes)
        ModulePane _barStrip, _widStrip;        // the module strip at the top of a bar's / widget's page

        public SettingsWindow(BarManager mgr)
        {
            _mgr = mgr;
            Title = "vShell settings";
            // big by default, never bigger than the screen it opens on
            Width = Math.Min(1180, Math.Max(760, SystemParameters.WorkArea.Width - 80));
            Height = Math.Min(900, Math.Max(520, SystemParameters.WorkArea.Height - 60));
            MinWidth = 760;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true;
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/vshell.ico")); } catch { }

            // the suite's window: its own title row and caption buttons (drag by the top 44 px), the glass with Blur / Frosted
            Background = Brushes.Transparent;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            ApplyChrome();
            BuildFrame();

            _barPanel = Page(PgBar);
            _widPanel = Page(PgWidget);
            BuildAppearancePage();
            BuildDesktopPage();
            BuildStartupTab(Page(PgStartup));
            BuildIntegrationsPage(Page(PgHa));
            BuildNotificationsPage(Page(PgNotify));
            BuildSystemPage();
            BuildUpdatesPage();
            BuildAboutPage();

            if (S.Bars.Count > 0) _selBar = S.Bars[0];
            if (S.Widgets.Count > 0) _selWidget = S.Widgets[0];
            LoadBar();
            LoadWidget();
            RefreshColors();
            _loading = false;
            Navigate(PgAppearance);
            ContentRendered += delegate { Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(LogLayout)); };
        }

        // =================================================================
        // Plugins: the ones added (each opens its own page) and the ones to get
        // =================================================================
        void BuildIntegrationsPage(StackPanel p)
        {
            PageHeader(p, "Plugins", "Connections to local and web services");
            BuildPluginsSection(p);
        }

        /// <summary>A secret (password / API key): a password box that saves it encrypted when you leave it, "Forget", and what is saved.</summary>
        PasswordBox SecretRow(StackPanel p, string label, string what, Func<string> get, Action<string> set, string info = null)
        {
            var status = Txt("", "vsDim", 12, false);
            status.Margin = new Thickness(0, 4, 0, 0);
            var pw = new PasswordBox { Width = 260 };
            Action refresh = delegate { status.Text = string.IsNullOrEmpty(get()) ? "No " + what + " saved." : "A " + what + " is saved. Type a new one to replace it."; };
            pw.LostFocus += delegate
            {
                if (_loading) return;
                string t = pw.Password.Trim();
                if (t.Length == 0) return;
                set(Secret.Protect(t));
                pw.Password = "";
                refresh();
                _mgr.SettingsChanged();
            };
            var forget = Btn("Forget", delegate { set(""); pw.Password = ""; refresh(); _mgr.SettingsChanged(); });
            forget.Margin = new Thickness(6, 0, 0, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(pw);
            row.Children.Add(forget);
            var block = new StackPanel();
            block.Children.Add(row);
            block.Children.Add(status);
            Place(p, Row(label, block, false, info));
            refresh();
            return pw;
        }

        /// <summary>"Test connection" and its answer.</summary>
        void TestRow(StackPanel p, Action beforeTest, Func<System.Threading.Tasks.Task<KeyValuePair<bool, string>>> test)
        {
            var result = Txt("", "vsDim", 12.5, false);
            result.Margin = new Thickness(12, 0, 0, 0);
            result.TextWrapping = TextWrapping.Wrap;
            var b = new Button { Content = "Test connection", VerticalAlignment = VerticalAlignment.Top };
            b.Click += async delegate
            {
                result.SetResourceReference(TextBlock.ForegroundProperty, "vsDim");
                result.Text = "Testing…";
                if (beforeTest != null) beforeTest();
                KeyValuePair<bool, string> r = await test();
                result.Foreground = new SolidColorBrush(r.Key ? Color.FromRgb(0x3F, 0xB9, 0x50) : Color.FromRgb(0xF8, 0x51, 0x49));
                result.Text = r.Value;
            };
            var row = new DockPanel();
            row.Children.Add(b);
            row.Children.Add(result);
            Place(p, row);
        }

        // =================================================================
        // Called by the manager
        // =================================================================
        public void SelectBar(BarPlacement p)
        {
            if (p == null || !S.Bars.Contains(p)) return;
            _selBar = p;
            LoadBar();
            Navigate(PgBar);
        }

        /// <summary>Bars were added / removed / moved: update the list (and the bar's page if it is showing; back to the list when it's gone).</summary>
        public void RefreshBars()
        {
            bool gone = _selBar != null && !S.Bars.Contains(_selBar);
            if (_selBar == null || gone) _selBar = S.Bars.Count > 0 ? S.Bars[0] : null;
            if (gone && (_page == PgBar || _page == PgBarModules)) { LoadBar(); Navigate(PgBars); return; }
            if (_loadedBar != _selBar || _page == PgBar) LoadBar();
            if (_page == PgBarModules && _barPane == null) { Navigate(PgBar); return; }
            RebuildList(PgBars);
            RebuildNav();
        }

        /// <summary>The bars changed their module order (Ctrl+drag) or contents without going through this window.</summary>
        public void RefreshAfterExternalChange()
        {
            if (_barPane != null && _selBar != null) _barPane.SyncIfChanged();
            if (_widPane != null && _selWidget != null) _widPane.SyncIfChanged();
            // the theme's colours changed without this window (Wallpaper Theme after a new wallpaper): re-colour it and its preview card
            if (ThemeSignature() != _chromeSig) ApplyChrome();
            if (S.WallpaperTheme)
            {
                WallpaperPalette w;
                if (!WallpaperColor.TryGetPalette(S, out w)) w = null;
                if (w != _shownWall) BuildAppearancePage();
            }
            // modules moved on the bar itself (Ctrl+drag): the strip on the bar's / widget's page follows
            if (_page == PgBar && _barStrip != null) _barStrip.SyncIfChanged();
            if (_page == PgWidget && _widStrip != null) _widStrip.SyncIfChanged();
        }

        /// <summary>Widgets were added / removed / changed from outside this window.</summary>
        public void RefreshWidgets()
        {
            bool gone = _selWidget != null && !S.Widgets.Contains(_selWidget);
            if (_selWidget == null || gone) _selWidget = S.Widgets.Count > 0 ? S.Widgets[0] : null;
            if (gone && (_page == PgWidget || _page == PgWidgetModules)) { LoadWidget(); Navigate(PgWidgets); return; }
            if (_loadedWidget != _selWidget || _page == PgWidget) LoadWidget();
            if (_page == PgWidgetModules && _loadedWidget == null) { Navigate(PgWidget); return; }
            RebuildList(PgWidgets);
            RebuildNav();
        }

        /// <summary>Settings > Updates (its Updates card shows a check going on).</summary>
        public void ShowUpdates() { Navigate(PgUpdates); }

        /// <summary>Settings > Plugins (the welcome guide's Finish, with plugins picked).</summary>
        public void ShowPlugins() { Navigate(PgHa); }

        public void SelectWidget(WidgetSettings w)
        {
            if (w == null || !S.Widgets.Contains(w)) return;
            _selWidget = w;
            LoadWidget();
            Navigate(PgWidget);
        }

        // =================================================================
        // A bar's page
        // =================================================================
        static string BarLabel(int index, BarPlacement p, List<MonitorInfo> monitors)
        {
            string mon = "Primary monitor";
            for (int i = 0; i < monitors.Count; i++)
                if (string.Equals(monitors[i].DeviceName, p.Monitor, StringComparison.OrdinalIgnoreCase))
                    mon = "Monitor " + (i + 1) + (monitors[i].IsPrimary ? " (primary)" : "");
            if (!string.IsNullOrEmpty(p.Monitor) && mon == "Primary monitor")
            {
                bool primaryByEmpty = false;
                foreach (var m in monitors) if (m.IsPrimary && string.Equals(m.DeviceName, p.Monitor, StringComparison.OrdinalIgnoreCase)) primaryByEmpty = true;
                if (!primaryByEmpty) mon = "disconnected monitor";
            }
            return "Bar " + (index + 1) + "  ·  " + mon + "  ·  " + p.Edge.ToLowerInvariant();
        }

        static Button SmallButton(string text)
        {
            return new Button { Content = text, Margin = new Thickness(0, 0, 0, 4) };
        }

        /// <summary>Monitor picker that stores the device name.</summary>
        /// <summary>
        /// Which monitor a bar / widget is on. Its remembered monitor while it's turned off or unplugged shows as "Not connected" (picking
        /// a connected one moves it there for good).
        /// </summary>
        void MonitorRow(StackPanel p, string label, Func<string> getName, Func<string> getKey, Action<MonitorInfo> set, string note = null)
        {
            var mons = MonitorHelper.GetAll();
            var cb = new ComboBox { MinWidth = 240 };
            MonitorInfo cur = MonitorHelper.Find(getName(), getKey());
            int sel = -1;
            for (int i = 0; i < mons.Count; i++)
            {
                cb.Items.Add("Monitor " + (i + 1) + (mons[i].IsPrimary ? " (primary)" : "") + "  ·  " + mons[i].Bounds.Width + " × " + mons[i].Bounds.Height);
                if (cur != null && mons[i].Handle == cur.Handle) sel = i;
            }
            if (cur == null) { cb.Items.Add("Not connected  ·  hidden until it's back"); sel = cb.Items.Count - 1; }
            if (sel < 0 && cb.Items.Count > 0) sel = 0;
            cb.SelectedIndex = sel;
            cb.SelectionChanged += delegate
            {
                if (_loading || cb.SelectedIndex < 0 || cb.SelectedIndex >= mons.Count) return;
                set(mons[cb.SelectedIndex]);
                _mgr.SettingsChanged();
            };
            Place(p, Row(label, cb, false, note));
        }

    }

    /// <summary>The file written by "Export theme".</summary>
    public sealed class ThemeFile
    {
        public string Format { get; set; } = "vShell-theme";
        public int Version { get; set; } = 1;
        /// <summary>The theme's name (version 2).</summary>
        public string Name { get; set; }
        /// <summary>Colours, background, blur and font.</summary>
        public ThemePreset Theme { get; set; }
        /// <summary>The bar style (version 2; older files have none and leave the bars as they are).</summary>
        public BarStyle BarStyle { get; set; }
        /// <summary>The widget style (version 2).</summary>
        public WidgetStyle WidgetStyle { get; set; }
    }

    /// <summary>One row of the startup list (public so WPF binding can read it).</summary>
    public sealed class StartupRow
    {
        public string Name { get; set; }
        public string State { get; set; }
        public string Source { get; set; }
        public string Command { get; set; }
        public StartupItem Item;
    }
}
