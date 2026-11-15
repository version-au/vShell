using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Layout of the settings window (Windows 11 style): a sidebar of pages on the left, each page made of titled cards whose rows have
    /// the label on the left and the control on the right - the suite's look (vSuite.Core, as vXplorer's and vView's Options): its own title
    /// bar and caption buttons, the Blur / Frosted glass, the shared colours and controls (SettingsStyle.Dress - the vs* names are aliases).
    /// </summary>
    public sealed partial class SettingsWindow
    {
        // ---- chrome ---------------------------------------------------------------------------------
        string _chromeSig;
        /// <summary>The suite theme the window shows, and whether it has the glass.</summary>
        vSuite.Ui.Theme _suiteTheme;
        bool _glass;
        Grid _frame;
        Button _maxBtn;
        readonly List<VShellMark> _marks = new List<VShellMark>();

        /// <summary>The vShell logo in the theme's colours (repainted when the theme changes).</summary>
        VShellMark MakeMark(double size)
        {
            var m = new VShellMark { Dim = size, ShowLine = size >= 40, VerticalAlignment = VerticalAlignment.Center };
            _marks.Add(m);
            PaintMarks();
            return m;
        }

        void PaintMarks()
        {
            if (_marks.Count == 0) return;
            Theme t = Theme.From(S, 12, 8);
            foreach (var m in _marks)
            {
                m.Dot1 = t.MainAccent; m.Dot2 = t.SecondaryAccent; m.Dot3 = t.Fg;
                m.InvalidateVisual();
            }
        }

        /// <summary>The theme colours actually in use (Wallpaper Theme included), to notice when they change.</summary>
        string ThemeSignature()
        {
            vSuite.Ui.Theme t = SettingsStyle.SuiteTheme(S);
            return string.Join("|", t.Bg, t.Bg2, t.Fg, t.Accent, t.Secondary, t.Gradient, t.GradientAngle, t.Blur, t.Radius, t.BorderThickness, t.BorderOpacity, t.Border);
        }

        /// <summary>Colour the window from the current vShell theme (called at start and whenever the theme changes): the suite's brushes and glass.</summary>
        void ApplyChrome()
        {
            try { _suiteTheme = SettingsStyle.Dress(this, S, true); }
            catch (Exception ex)
            {
                Log.Write("Settings look: " + ex.Message);
                SettingsStyle.SetBrushes(this, SettingsPalette.Plain());
            }
            _glass = _suiteTheme != null;   // always see-through, as the bars (the wallpaper - blurred or not - under the theme colour)
            _chromeSig = ThemeSignature();
            PaintMarks();
            ApplyGlass();
        }

        /// <summary>
        /// The window's look: Blur / Frosted drawn as the bars draw them (the blurred wallpaper under the theme colour at the theme's
        /// opacity - vSuite's Glass), dark mode, corners, outline, the title area's colour.
        /// </summary>
        void ApplyGlass()
        {
            try
            {
                if (new WindowInteropHelper(this).Handle == IntPtr.Zero || _suiteTheme == null) return;
                vSuite.Ui.WindowEffects.Look(this, _suiteTheme, _glass);
            }
            catch (Exception ex) { Log.Write("Settings glass: " + ex.Message); }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyGlass();
        }

        /// <summary>A borderless window maximised hangs off the screen by its frame: keep the content on it. The max button's glyph follows.</summary>
        void UpdateMaximized()
        {
            bool max = WindowState == WindowState.Maximized;
            if (_frame != null) _frame.Margin = max ? new Thickness(vSuite.Ui.WindowEffects.MaximizedOverhang(new WindowInteropHelper(this).Handle)) : new Thickness(0);
            if (_maxBtn != null) { _maxBtn.Content = max ? "\uE923" : "\uE922"; _maxBtn.ToolTip = max ? "Restore" : "Maximise"; }
        }

        Button CaptionButton(string glyph, string tip, string style, Action click)
        {
            var b = new Button { Content = glyph, ToolTip = tip };
            b.SetResourceReference(FrameworkElement.StyleProperty, style);
            b.Click += delegate { click(); };
            return b;
        }
        /// <summary>The dim text colour as it is right now (for elements built with object initialisers).</summary>
        Brush DimBrush { get { return (Brush)FindResource("vsDim"); } }

        static void Ref(FrameworkElement e, DependencyProperty prop, string key) { e.SetResourceReference(prop, key); }

        static TextBlock Txt(string text, string brushKey, double size, bool bold)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        static TextBlock Glyph(string glyph, double size)
        {
            var t = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            return t;
        }

        // ---- navigation --------------------------------------------------------------------------------
        const string PgAppearance = "appearance", PgBar = "bar", PgBarModules = "barModules", PgWidget = "widget", PgWidgetModules = "widgetModules",
                     PgDesktop = "desktop", PgStartup = "startup", PgHa = "ha", PgNotify = "notify", PgSystem = "system", PgUpdates = "updates", PgAbout = "about",
                     PgNotifyPart = "notifyPart", PgPlugin = "plugin", PgBars = "bars", PgWidgets = "widgets", PgLook = "look";

        readonly Dictionary<string, StackPanel> _pages = new Dictionary<string, StackPanel>();
        ScrollViewer _content;
        StackPanel _nav;
        ScrollViewer _navScroll;
        Border _side;
        string _page = PgAppearance;

        /// <summary>Window frame: sidebar + scrolling content area.</summary>
        void BuildFrame()
        {
            var root = new Grid();
            Ref(root, Panel.BackgroundProperty, "vsBg");
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
            Ref(side, Border.BackgroundProperty, "vsSide");
            Ref(side, Border.BorderBrushProperty, "vsLine");
            var sideDock = new DockPanel();
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 16, 12, 14) };
            var logo = MakeMark(20);
            logo.Margin = new Thickness(0, 0, 9, 0);
            brand.Children.Add(logo);
            brand.Children.Add(Txt("vShell", "vsFg", 17, true));
            brand.Children.Add(Txt("  settings", "vsDim", 13, false));
            DockPanel.SetDock(brand, Dock.Top);
            sideDock.Children.Add(brand);
            _nav = new StackPanel { Margin = new Thickness(8, 0, 8, 12) };
            _navScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _nav };
            sideDock.Children.Add(_navScroll);
            side.Child = sideDock;
            _side = side;
            Grid.SetColumn(side, 0);
            root.Children.Add(side);

            // the page column: a caption row (drag the window by it) with minimise / maximise / close, the page under it
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            caption.Children.Add(CaptionButton("\uE921", "Minimise", "vx.CaptionButton", delegate { WindowState = WindowState.Minimized; }));
            _maxBtn = CaptionButton("\uE922", "Maximise", "vx.CaptionButton", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
            caption.Children.Add(_maxBtn);
            caption.Children.Add(CaptionButton("\uE8BB", "Close", "vx.CloseButton", Close));
            right.Children.Add(caption);
            _content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(_content, 1);
            right.Children.Add(_content);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            TextElementForeground(root);
            _frame = root;
            Content = root;
            StateChanged += delegate { UpdateMaximized(); };
        }

        static void TextElementForeground(FrameworkElement e)
        {
            e.SetResourceReference(TextBlock.ForegroundProperty, "vsFg");
            e.SetResourceReference(Control.ForegroundProperty, "vsFg");
        }

        StackPanel Page(string key)
        {
            StackPanel p;
            if (!_pages.TryGetValue(key, out p))
            {
                p = new StackPanel { Margin = new Thickness(30, 0, 30, 30), MaxWidth = 960 };
                _pages[key] = p;
            }
            return p;
        }

        /// <summary>Show a page and highlight it in the sidebar.</summary>
        void Navigate(string key)
        {
            _page = key;
            if (key == PgBarModules) BuildModulesPage(false);
            else if (key == PgWidgetModules) BuildModulesPage(true);
            else if (key == PgAppearance && _content.Content != Page(key)) BuildAppearancePage();     // bars / widgets / styles may have changed elsewhere
            else if (key == PgLook) BuildAppearancePage();        // one part of Appearance (Theme, Colours …), fresh each time
            else if (key == PgNotify && _content.Content != Page(key))
            {
                // what is set up on the Plugins page (and whether the listener started) may have changed
                StackPanel np = Page(key);
                np.Children.Clear();
                bool o = _loading;
                _loading = true;
                try { BuildNotificationsPage(np); } finally { _loading = o; }
            }
            else if (key == PgHa && _content.Content != Page(key)) RebuildIntegrations();     // a plugin may have been set up on its page
            else if (key == PgNotifyPart) BuildNotifyPart();      // one part of Notifications (Cards, System …), fresh each time
            else if (key == PgPlugin) BuildPluginPage();          // one plugin's own page
            else if (key == PgBars) BuildBarsList();              // the lists: summaries / shown may have changed elsewhere
            else if (key == PgWidgets) BuildWidgetsList();
            _content.Content = Page(key);
            _content.ScrollToTop();
            RebuildNav();
        }

        void RebuildNav()
        {
            if (_nav == null) { Log.Write("Settings: no sidebar to fill"); return; }
            try { RebuildNavCore(); }
            catch (Exception ex) { Log.Write("Settings sidebar: " + ex); }
            if (!_navLogged) { _navLogged = true; Log.Write("Settings: sidebar has " + _nav.Children.Count + " items"); }
        }
        bool _navLogged;

        /// <summary>
        /// Once, after the window first shows: how big the sidebar and its list came out, and the DPI (a tester saw an empty sidebar
        /// and an empty live preview).
        /// </summary>
        void LogLayout()
        {
            try
            {
                DpiScale d = VisualTreeHelper.GetDpi(this);
                var sb = new System.Text.StringBuilder("Settings shown: window " + Math.Round(ActualWidth) + "x" + Math.Round(ActualHeight) +
                    " at " + Math.Round(d.DpiScaleX * 100) + "%, render " + RenderCapability.Tier / 0x10000 + ", sidebar " +
                    (_side == null ? "?" : Math.Round(_side.ActualWidth) + "x" + Math.Round(_side.ActualHeight) + (_side.IsVisible ? "" : " hidden")));
                if (_navScroll != null)
                    sb.Append(", list view " + Math.Round(_navScroll.ViewportWidth) + "x" + Math.Round(_navScroll.ViewportHeight) +
                              " of " + Math.Round(_navScroll.ExtentWidth) + "x" + Math.Round(_navScroll.ExtentHeight) + " at " + Math.Round(_navScroll.VerticalOffset));
                if (_nav != null)
                {
                    sb.Append(", list " + Math.Round(_nav.ActualWidth) + "x" + Math.Round(_nav.ActualHeight) + " with " + _nav.Children.Count + " items");
                    if (_nav.Children.Count > 0)
                    {
                        var f = _nav.Children[0] as FrameworkElement;
                        if (f != null)
                        {
                            Point at = new Point(-1, -1);
                            try { at = f.TranslatePoint(new Point(0, 0), this); } catch { }
                            sb.Append(", first " + Math.Round(f.ActualWidth) + "x" + Math.Round(f.ActualHeight) + " at " + Math.Round(at.X) + "," + Math.Round(at.Y) +
                                      (f.IsVisible ? "" : " hidden") + ", opacity " + f.Opacity);
                        }
                    }
                }
                Log.Write(sb.ToString());
            }
            catch (Exception ex) { Log.Write("Settings layout log: " + ex.Message); }
        }

        void RebuildNavCore()
        {
            _nav.Children.Clear();
            bool onBar = _page == PgBars || _page == PgBar || _page == PgBarModules || _page == PgLook && _lookFrom == PgBars;
            bool onWid = _page == PgWidgets || _page == PgWidget || _page == PgWidgetModules || _page == PgLook && _lookFrom == PgWidgets;

            NavItem("", "Appearance", _page == PgAppearance || _page == PgLook && _lookFrom == PgAppearance, delegate { Navigate(PgAppearance); });
            NavItem("", "Bars", onBar, delegate { Navigate(PgBars); });

            NavItem("", "Widgets", onWid, delegate { Navigate(PgWidgets); });

            NavItem("", "Desktop", _page == PgDesktop, delegate { Navigate(PgDesktop); });
            NavItem("", "Startup apps", _page == PgStartup, delegate { Navigate(PgStartup); });
            NavItem("", "Plugins", _page == PgHa || _page == PgPlugin && _pluginFrom != PgNotify, delegate { Navigate(PgHa); });
            NavItem("\uEA8F", "Notifications", _page == PgNotify || _page == PgNotifyPart || _page == PgPlugin && _pluginFrom == PgNotify, delegate { Navigate(PgNotify); });
            NavItem("", "System", _page == PgSystem, delegate { Navigate(PgSystem); });
            NavItem("\uE895", "Updates", _page == PgUpdates, delegate { Navigate(PgUpdates); });
            NavItem("", "About", _page == PgAbout, delegate { Navigate(PgAbout); });
        }

        static string BarShortName(int index, BarPlacement b, List<MonitorInfo> mons)
        {
            string mon = "";
            for (int i = 0; i < mons.Count; i++)
                if (string.Equals(mons[i].DeviceName, b.Monitor, StringComparison.OrdinalIgnoreCase)) mon = "Monitor " + (i + 1);
            if (mon.Length == 0) mon = "Primary";
            return b.Edge + "  ·  " + mon;
        }

        void NavItem(string glyph, string text, bool selected, Action click)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bar = new Border { CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 9, 0, 9), Visibility = selected ? Visibility.Visible : Visibility.Hidden };
            Ref(bar, Border.BackgroundProperty, "vsAccent");
            var ic = Glyph(glyph, 15);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            Ref(ic, TextBlock.ForegroundProperty, selected ? "vsFg" : "vsDim");
            var tx = Txt(text, selected ? "vsFg" : "vsFg", 13.5, false);
            tx.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(ic, 1); Grid.SetColumn(tx, 2);
            g.Children.Add(bar); g.Children.Add(ic); g.Children.Add(tx);
            _nav.Children.Add(Clickable(g, selected, new Thickness(0, 7, 8, 7), click));
        }

        /// <summary>A hover-highlighted clickable area (sidebar items, chips, tiles).</summary>
        static Border Clickable(UIElement child, bool selected, Thickness padding, Action click)
        {
            var b = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            if (selected) Ref(b, Border.BackgroundProperty, "vsCtrl");
            b.MouseEnter += delegate { if (!selected) Ref(b, Border.BackgroundProperty, "vsCtrl"); };
            b.MouseLeave += delegate { if (!selected) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        // ---- page building blocks ------------------------------------------------------------------------
        /// <summary>Big title, a small line under it, and buttons on the right.</summary>
        void PageHeader(StackPanel p, string title, string sub, params UIElement[] actions)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Txt(title, "vsFg", 26, true));
            if (!string.IsNullOrEmpty(sub)) { var s = Txt(sub, "vsDim", 13, false); s.Margin = new Thickness(0, 3, 0, 0); left.Children.Add(s); }
            g.Children.Add(left);
            var right = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 4, 0, 0) };
            foreach (var a in actions)
            {
                var fe = a as FrameworkElement;
                if (fe != null) fe.Margin = new Thickness(8, 0, 0, 6);
                right.Children.Add(a);
            }
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            p.Children.Add(g);
            EndSection(p);
        }

        static Button Btn(string text, Action click, bool primary = false)
        {
            var b = new Button { Content = text };
            if (primary) b.SetResourceReference(FrameworkElement.StyleProperty, "vsPrimary");
            b.Click += delegate { click(); };
            return b;
        }

        // rows go into the card that was opened last on that panel
        readonly Dictionary<Panel, StackPanel> _cardOf = new Dictionary<Panel, StackPanel>();
        // collapsed module-inspector sections (kept while the window is open)
        static readonly HashSet<string> _collapsed = new HashSet<string>();

        /// <summary>Start a titled card on the page; the following rows go into it.</summary>
        void Header(StackPanel p, string text)
        {
            var t = Txt(text, "vsFg", 13.5, true);
            t.Margin = new Thickness(2, p.Children.Count == 0 ? 0 : 18, 0, 8);
            p.Children.Add(t);
            NewCard(p);
        }

        StackPanel NewCard(StackPanel p)
        {
            var body = new StackPanel();
            var card = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), Child = body };
            Ref(card, Border.CornerRadiusProperty, "vx.MenuRadius");
            Ref(card, Border.BackgroundProperty, "vsCard");
            Ref(card, Border.BorderBrushProperty, "vsLine");
            p.Children.Add(card);
            _cardOf[p] = body;
            return body;
        }

        /// <summary>A card with a clickable title that folds it away (module inspector). An optional control sits in the title.</summary>
        void Section(StackPanel p, string title, UIElement headerControl = null)
        {
            var body = new StackPanel();
            var outer = new StackPanel();
            var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Txt(title, "vsFg", 13.5, true);
            t.Margin = new Thickness(16, 12, 12, 12);
            head.Children.Add(t);
            if (headerControl != null)
            {
                var fe = headerControl as FrameworkElement;
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(headerControl, 1);
                head.Children.Add(headerControl);
            }
            bool closed = _collapsed.Contains(title);
            var chev = Glyph(closed ? "" : "", 10);
            Ref(chev, TextBlock.ForegroundProperty, "vsDim");
            chev.Margin = new Thickness(8, 0, 16, 0);
            Grid.SetColumn(chev, 2);
            head.Children.Add(chev);
            body.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
            head.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource is DependencyObject && IsInside(e.OriginalSource as DependencyObject, headerControl)) return;
                bool nowClosed = body.Visibility == Visibility.Visible;
                body.Visibility = nowClosed ? Visibility.Collapsed : Visibility.Visible;
                chev.Text = nowClosed ? "" : "";
                if (nowClosed) _collapsed.Add(title); else _collapsed.Remove(title);
            };
            outer.Children.Add(head);
            outer.Children.Add(body);
            var card = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), Child = outer };
            Ref(card, Border.CornerRadiusProperty, "vx.MenuRadius");
            Ref(card, Border.BackgroundProperty, "vsCard");
            Ref(card, Border.BorderBrushProperty, "vsLine");
            p.Children.Add(card);
            _cardOf[p] = body;
            // a line between the title and the first row
            body.Tag = "section";
        }

        static bool IsInside(DependencyObject d, UIElement container)
        {
            if (container == null) return false;
            while (d != null)
            {
                if (d == container) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>Following elements are added straight to the page (until the next Header).</summary>
        void EndSection(StackPanel p) { _cardOf.Remove(p); }

        /// <summary>Add a row (or any element) to the card that is open on the panel; opens a plain card if there is none.</summary>
        void Place(Panel p, UIElement e)
        {
            StackPanel card;
            if (!_cardOf.TryGetValue(p, out card))
            {
                var sp = p as StackPanel;
                if (sp == null) { p.Children.Add(e); return; }
                card = NewCard(sp);
            }
            var wrap = new Border { Child = e, Padding = new Thickness(16, 10, 16, 10) };
            if (card.Children.Count > 0 || "section".Equals(card.Tag))
            {
                wrap.BorderThickness = new Thickness(0, 1, 0, 0);
                Ref(wrap, Border.BorderBrushProperty, "vsLine");
            }
            card.Children.Add(wrap);
        }

        /// <summary>"Label (longer explanation)" or "Label - explanation" becomes a label with a small grey hint under it.</summary>
        static void SplitLabel(string text, out string main, out string hint)
        {
            main = text ?? ""; hint = null;
            int dash = main.IndexOf("  - ", StringComparison.Ordinal);
            if (dash < 0) dash = main.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                hint = main.Substring(dash).Trim().TrimStart('-').Trim();
                main = main.Substring(0, dash).Trim();
            }
            int open = main.IndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && main.EndsWith(")", StringComparison.Ordinal))
            {
                string inner = main.Substring(open + 2, main.Length - open - 3);
                if (inner.Length > 12)
                {
                    hint = hint == null ? inner : inner + ". " + hint;
                    main = main.Substring(0, open);
                }
            }
            if (hint != null && hint.Length > 0) hint = char.ToUpperInvariant(hint[0]) + hint.Substring(1);
        }

        /// <summary>
        /// A small (i) after a label: hover it and a card with the help text appears under it (gone when the mouse leaves both).
        /// </summary>
        FrameworkElement InfoIcon(string text)
        {
            var icon = new TextBlock
            {
                Text = "\uE946", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Margin = new Thickness(6, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Help, Background = Brushes.Transparent
            };
            Ref(icon, TextBlock.ForegroundProperty, "vsDim");
            var body = Txt(text, "vsFg", 12.5, false);
            body.TextWrapping = TextWrapping.Wrap;
            body.MaxWidth = 340;
            var card = new Border
            {
                Child = body, Padding = new Thickness(12, 9, 12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 }
            };
            Ref(card, Border.BackgroundProperty, "vsPopup");
            Ref(card, Border.BorderBrushProperty, "vsCtrlBorder");
            var pop = new System.Windows.Controls.Primitives.Popup
            {
                Child = card, PlacementTarget = icon, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, AllowsTransparency = true,
                StaysOpen = true, HorizontalOffset = -12, VerticalOffset = 2, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
            };
            // close a moment after the mouse leaves the icon, unless it went onto the card
            var hide = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hide.Tick += delegate { hide.Stop(); if (!icon.IsMouseOver && !card.IsMouseOver) pop.IsOpen = false; };
            icon.MouseEnter += delegate { hide.Stop(); pop.IsOpen = true; Ref(icon, TextBlock.ForegroundProperty, "vsAccent"); };
            icon.MouseLeave += delegate { hide.Start(); Ref(icon, TextBlock.ForegroundProperty, "vsDim"); };
            card.MouseLeave += delegate { hide.Start(); };
            icon.Unloaded += delegate { hide.Stop(); pop.IsOpen = false; };
            // a click is the icon's (it doesn't flip the switch or open the row it's on); it opens / closes the card too
            icon.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            icon.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; pop.IsOpen = !pop.IsOpen; };
            // the popup lives next to the icon in the window's tree, so the card finds the settings colours (vsPopup, vsFg ...)
            var holder = new Grid { VerticalAlignment = VerticalAlignment.Center, Tag = body };
            holder.Children.Add(icon);
            holder.Children.Add(pop);
            return holder;
        }

        /// <summary>What a row's label is tagged with (a Note can find it and become its (i)).</summary>
        const string LabelTag = "label";

        /// <summary>An (i) at the end of a label; a second one adds its text to the first.</summary>
        void AddInfo(TextBlock label, string text)
        {
            foreach (Inline i in label.Inlines)
            {
                var c = i as InlineUIContainer;
                var holder = c != null ? c.Child as FrameworkElement : null;
                var body = holder != null ? holder.Tag as TextBlock : null;
                if (body != null) { body.Text += "\n\n" + text; return; }
            }
            label.Inlines.Add(new InlineUIContainer(InfoIcon(text)) { BaselineAlignment = BaselineAlignment.Center });
        }

        /// <summary>
        /// The pages where a Note becomes an (i) on the row above it, so the page is the settings and not paragraphs (Plugins, a plugin's
        /// page, Notifications). A note with no row above it (the first thing in a card, under buttons) still shows as text.
        /// </summary>
        readonly HashSet<Panel> _notesAsInfo = new HashSet<Panel>();

        bool AttachInfo(StackPanel p, string text)
        {
            StackPanel card;
            if (!_notesAsInfo.Contains(p) || !_cardOf.TryGetValue(p, out card) || card.Children.Count == 0) return false;
            var wrap = card.Children[card.Children.Count - 1] as Border;
            TextBlock label = wrap != null ? FindLabel(wrap.Child, 0) : null;
            if (label == null) return false;
            AddInfo(label, text);
            return true;
        }

        static TextBlock FindLabel(object e, int depth)
        {
            if (e == null || depth > 6) return null;
            var tb = e as TextBlock;
            if (tb != null) return LabelTag.Equals(tb.Tag) ? tb : null;
            var d = e as Decorator;
            if (d != null) return FindLabel(d.Child, depth + 1);
            var pn = e as Panel;
            if (pn != null)
                foreach (UIElement c in pn.Children)
                {
                    TextBlock f = FindLabel(c, depth + 1);
                    if (f != null) return f;
                }
            return null;
        }

        /// <summary>
        /// A row that opens another page (Windows 11 style): a glyph, the title, a short summary under it, an optional control (a switch,
        /// buttons) and ›. Clicking the row - not the control - opens it; open = null is a row that only shows (no ›).
        /// </summary>
        void LinkRow(StackPanel p, string glyph, string title, string summary, UIElement control, Action open)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var gl = Glyph(glyph ?? "", 15);
            gl.HorizontalAlignment = HorizontalAlignment.Center;
            Ref(gl, TextBlock.ForegroundProperty, "vsFg");
            var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = gl, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            Ref(box, Border.BackgroundProperty, "vsCtrl");
            g.Children.Add(box);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Txt(title, "vsFg", 13.5, false));
            if (!string.IsNullOrEmpty(summary))
            {
                var s = Txt(summary, "vsDim", 12, false);
                s.TextWrapping = TextWrapping.NoWrap;
                s.TextTrimming = TextTrimming.CharacterEllipsis;
                s.ToolTip = summary;
                s.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(s);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (control != null)
            {
                var fe = control as FrameworkElement;
                if (fe != null) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(12, 0, 0, 0); }
                Grid.SetColumn(control, 2);
                g.Children.Add(control);
            }
            if (open != null)
            {
                var chev = Glyph("", 11);
                Ref(chev, TextBlock.ForegroundProperty, "vsDim");
                chev.Margin = new Thickness(14, 0, 2, 0);
                Grid.SetColumn(chev, 3);
                g.Children.Add(chev);
            }
            // the whole row (to the card's edges) lights up under the mouse
            var row = new Border { Child = g, Background = Brushes.Transparent, Margin = new Thickness(-16, -10, -16, -10), Padding = new Thickness(16, 10, 16, 10) };
            if (open != null)
            {
                row.Cursor = Cursors.Hand;
                row.MouseEnter += delegate { Ref(row, Border.BackgroundProperty, "vsCtrl"); };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    if (control != null && e.OriginalSource is DependencyObject && IsInside((DependencyObject)e.OriginalSource, control)) return;
                    e.Handled = true;
                    open();
                };
            }
            Place(p, row);
        }

        /// <summary>A small coloured dot (a plugin's state); brushKey = a settings colour instead (vsDim).</summary>
        static Border Dot(Brush fill, string brushKey = null)
        {
            var d = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), VerticalAlignment = VerticalAlignment.Center };
            if (brushKey != null) Ref(d, Border.BackgroundProperty, brushKey); else d.Background = fill;
            return d;
        }

        /// <summary>An on/off switch on its own (a LinkRow's): set, apply, then after = what else (the summary again).</summary>
        CheckBox Switch(Func<bool> get, Action<bool> set, Action after = null)
        {
            var cb = new CheckBox { IsChecked = get() };
            cb.Click += delegate
            {
                if (_loading) return;
                set(cb.IsChecked == true);
                _mgr.SettingsChanged();
                if (after != null) Dispatcher.BeginInvoke(after);
            };
            return cb;
        }

        /// <summary>Tabs across the page (Added / Get more, a plugin's Connection / Notifications / Modules), the picked one underlined.</summary>
        void Tabs(StackPanel p, string[] keys, string[] names, string current, Action<string> pick)
        {
            EndSection(p);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                bool on = k == current;
                var col = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
                var t = Txt(names[i], on ? "vsFg" : "vsDim", 13.5, on);
                t.TextWrapping = TextWrapping.NoWrap;
                col.Children.Add(t);
                var under = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 7, 0, 0) };
                if (on) Ref(under, Border.BackgroundProperty, "vsAccent"); else under.Background = Brushes.Transparent;
                col.Children.Add(under);
                var tab = new Border { Child = col, Background = Brushes.Transparent, Cursor = on ? Cursors.Arrow : Cursors.Hand, Padding = new Thickness(2, 4, 2, 0) };
                if (!on)
                {
                    tab.MouseEnter += delegate { Ref(t, TextBlock.ForegroundProperty, "vsFg"); };
                    tab.MouseLeave += delegate { Ref(t, TextBlock.ForegroundProperty, "vsDim"); };
                    tab.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; pick(k); };
                }
                row.Children.Add(tab);
            }
            var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 16) };
            Ref(line, Border.BorderBrushProperty, "vsLine");
            p.Children.Add(line);
        }

        /// <summary>Label (+ hint) on the left, control on the right; stacked = control on its own line under the label. info = an (i) after the label with this help.</summary>
        UIElement Row(string label, UIElement control, bool stacked = false, string info = null)
        {
            string main, hint;
            SplitLabel(label, out main, out hint);
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Txt(main, "vsFg", 13.5, false);
            title.Tag = LabelTag;
            left.Children.Add(title);
            if (!string.IsNullOrEmpty(info)) AddInfo(title, info);
            if (!string.IsNullOrEmpty(hint)) { var h = Txt(hint, "vsDim", 12, false); h.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(h); }

            if (stacked)
            {
                var sp = new StackPanel();
                sp.Children.Add(left);
                var fe = control as FrameworkElement;
                if (fe != null) { fe.Margin = new Thickness(0, 8, 0, 0); fe.HorizontalAlignment = HorizontalAlignment.Left; }
                sp.Children.Add(control);
                return sp;
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(left);
            var c = control as FrameworkElement;
            if (c != null) { c.VerticalAlignment = VerticalAlignment.Center; c.Margin = new Thickness(16, 0, 0, 0); }
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        /// <summary>Two to four short choices as joined buttons (instead of a drop-down).</summary>
        /// <summary>save = false: only the window changes (a gallery's Presets / Saved tabs), nothing is saved or applied.</summary>
        UIElement Segmented(string[] items, Func<string> get, Action<string> set, Func<string, Brush> tint = null, bool save = true)
        {
            var outer = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true };
            Ref(outer, Border.BorderBrushProperty, "vsCtrlBorder");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            outer.Child = row;
            var parts = new List<Border>();
            Action paint = delegate
            {
                string cur = get();
                for (int i = 0; i < parts.Count; i++)
                {
                    bool on = string.Equals(items[i], cur, StringComparison.OrdinalIgnoreCase);
                    var tb = (TextBlock)parts[i].Child;
                    Brush c = tint != null ? tint(items[i]) : null;
                    if (c != null)
                    {
                        // its own colour: filled when chosen, the text in it when not
                        parts[i].ClearValue(Border.BackgroundProperty);
                        tb.ClearValue(TextBlock.ForegroundProperty);
                        parts[i].Background = on ? c : Brushes.Transparent;
                        tb.Foreground = on ? NoteRender.Contrast(c) : c;
                        tb.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                    else if (on) { Ref(parts[i], Border.BackgroundProperty, "vsAccent"); Ref(tb, TextBlock.ForegroundProperty, "vsAccentText"); tb.FontWeight = FontWeights.SemiBold; }
                    else { parts[i].Background = Brushes.Transparent; Ref(tb, TextBlock.ForegroundProperty, "vsFg"); tb.FontWeight = FontWeights.Normal; }
                }
            };
            for (int i = 0; i < items.Length; i++)
            {
                string v = items[i];
                var part = new Border { Padding = new Thickness(12, 5, 12, 5), Cursor = Cursors.Hand, Child = new TextBlock { Text = v, FontSize = 12.5 }, CornerRadius = new CornerRadius(5) };
                part.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (_loading) return;
                    set(v);
                    paint();
                    if (save) _mgr.SettingsChanged();
                };
                parts.Add(part);
                row.Children.Add(part);
            }
            paint();
            return outer;
        }

        /// <summary>A small rounded "chip" button with an optional colour dot.</summary>
        static Border Chip(string text, Color? dot, bool on, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (dot.HasValue) sp.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(dot.Value), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(Txt(text, "vsFg", 12, false));
            var b = new Border { Child = sp, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0) };
            if (on) { Ref(b, Border.BorderBrushProperty, "vsAccent"); Ref(b, Border.BackgroundProperty, "vsSelBg"); }
            else { Ref(b, Border.BorderBrushProperty, "vsCtrlBorder"); b.Background = Brushes.Transparent; }
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }
    }
}
