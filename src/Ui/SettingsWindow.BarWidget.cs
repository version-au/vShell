using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A bar's page (Layout / Look / Modules) and a widget's page (General / Layout / Look / Slide away / Modules): the header with
    /// its buttons and a one-line summary, the module strip, then the picked tab. Explanations are an (i) next to the setting.
    /// </summary>
    public sealed partial class SettingsWindow
    {
        /// <summary>The one-line summary under a page's title.</summary>
        void StateLine(StackPanel p, string text)
        {
            var t = Txt(text, "vsDim", 12.5, false);
            t.Margin = new Thickness(2, -8, 0, 14);
            p.Children.Add(t);
        }

        // =================================================================
        // A bar's page
        // =================================================================
        void LoadBar()
        {
            _loadedBar = _selBar;
            var p = _barPanel;
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            _barPane = null;
            _barStrip = null;
            Button back = Btn("‹  Back", delegate { Navigate(PgBars); });
            if (_selBar == null)
            {
                PageHeader(p, "Bars", "There are no bars.", Btn("+ Add bar", AddBarAndOpen, true), back);
                return;
            }
            var bar = _selBar;
            var mons = MonitorHelper.GetAll();
            var paste = Btn("Paste", delegate
            {
                if (_copiedBar == null || ReferenceEquals(_copiedBar, bar)) return;
                var r = MessageBox.Show(this, "Paste the copied bar settings onto this bar?\n\nYes = settings and modules\nNo = settings only (keep this bar's modules)",
                                        "Paste settings", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;
                PasteBar(_copiedBar, bar, r == MessageBoxResult.Yes);
                _mgr.SettingsChanged();
                LoadBar();
            });
            paste.IsEnabled = _copiedBar != null;
            var remove = Btn("Remove bar", delegate
            {
                if (MessageBox.Show(this, "Remove this bar and its modules?", "Remove bar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _mgr.RemoveBar(bar);
            });
            remove.IsEnabled = S.Bars.Count > 1;
            PageHeader(p, BarTitle(bar), "Bars  ›  " + BarShortName(S.Bars.IndexOf(bar), bar, mons),
                       Btn("Save Bar Style", delegate { SaveBarStyleFrom(bar); }),
                       Btn("Copy settings", delegate { _copiedBar = CloneJson(bar); paste.IsEnabled = true; }), paste, remove, back);
            StateLine(p, BarSummary(bar, mons));

            _barStrip = new ModulePane(this, delegate { return bar.Modules; }, ModuleTypes.BarTypes, false);
            _barStrip.Bar = delegate { return bar; };
            _barStrip.Open = delegate (ModuleConfig m) { EditModule(bar, null, m); };
            _barStrip.BuildStripInto(p, "Drag the bar by an empty area to move it to another edge or monitor. Ctrl+drag a module on the bar to reorder it there.");

            // Modules opens the module editor (‹ Back there comes back to the tab you were on)
            string[] keys = { "Layout", "Look", "Modules" };
            string tab = _barTab == "Look" ? "Look" : "Layout";
            Tabs(p, keys, new[] { "Layout", "Look", "Modules (" + bar.Modules.Count + ")" }, tab, delegate (string k)
            {
                if (k == "Modules") { EditModule(bar, null, null); return; }
                _barTab = k;
                LoadBar();
            });
            if (tab == "Look") BarLookTab(p, bar);
            else BarLayoutTab(p, bar);
        }

        void BarLayoutTab(StackPanel p, BarPlacement bar)
        {
            CheckBox follow = null;
            Func<CheckBox> followBox = delegate { return follow; };
            Func<bool> following = delegate { return bar.FollowStyle; };
            Action stop = delegate { bar.FollowStyle = false; };
            string layout = Styles.LayoutOf(bar);
            bool isFloating = layout == Styles.Floating, isDock = layout == Styles.Dock;

            Header(p, "Position");
            MonitorRow(p, "Monitor", delegate { return bar.Monitor; }, delegate { return bar.MonitorKey; }, delegate (MonitorInfo m)
            {
                bar.Monitor = m.DeviceName; bar.MonitorKey = m.Key;
                Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); RebuildNav(); }));        // its summary (and "Not connected") follow
            }, "Dragging the bar onto another monitor changes this too. While its monitor is turned off or unplugged the bar is hidden (and " +
               "gives back the space it reserved), and it comes back when the monitor does.");
            string[] edgeNames = { "Top", "Bottom" };
            ComboRow(p, "Edge", edgeNames, delegate { return bar.Edge == "Top" ? "Top" : "Bottom"; }, delegate (string v)
            {
                bar.Edge = v;
                Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); RebuildNav(); }));        // its name ("Top bar") follows
            }, "The screen edge it sits on - dragging it near the top or bottom of a monitor changes this too.");

            Header(p, "Layout");
            ComboRow(p, "Layout", Styles.Layouts, delegate { return Styles.LayoutOf(bar); }, delegate (string v)
            {
                Styles.SetLayout(bar, v, S.BarStyle);
                Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); }));     // full width / edge gap / outline rows follow
            }, "Classic: full width with an outline. Floating: away from the screen edge - full width or only as wide as its modules (its Full width " +
               "setting; it keeps the width the bar had). Dock: only as wide as its modules, against the screen edge (the two corners away from the " +
               "edge are rounded). Minimal: full width, no outline. Only the settings this layout has are shown.");
            if (isFloating)
                Check(p, "Full width", delegate { return bar.FullWidth; }, delegate (bool v)
                {
                    bar.FullWidth = v;
                    Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); }));      // Three zones shows / hides
                }, "On: across the whole screen, less the gap from the screen edge at each end, with Three zones. Off: only as wide as its modules.");
            follow = Check(p, "Follow the bar style", delegate { return bar.FollowStyle; }, delegate (bool v)
            {
                bar.FollowStyle = v;
                if (v) { string lay = Styles.LayoutOf(bar); Styles.ApplyLook(bar, Styles.LookFor(S.BarStyle, lay), lay); Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); })); }
            }, "This bar takes the bar style's look for its layout (Appearance > Bar style); off = its own look. Changing height, corners, edge gap " +
               "or outline gives this bar its own look; Save Bar Style (top right) keeps that look as the " + layout + " look of a bar style you can use in themes.");
            if (bar.FullWidth)
                Check(p, "Three zones", delegate { return bar.Zones; }, delegate (bool v)
                {
                    bar.Zones = v;
                    if (v) bar.GuessZones();       // first guess from the spacers; zones set before are kept
                    Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); }));      // the modules' Zone setting shows / hides
                }, "Left, centre and right - the centre zone stays in the middle of the bar. The module editor shows the zones as columns: drag a module " +
                   "into another column to change its zone (or set its Zone, or Ctrl+drag it on the bar). A flexible spacer only fills the free room " +
                   "of its own zone. Turn zones off to go back to one row (the zones are remembered).");

            Header(p, "Size and space");
            SliderRow(p, "Height", 30, 96, delegate { return bar.BarHeight; }, OwnLook<double>(following, stop, followBox, delegate (double v) { bar.BarHeight = (int)v; }));
            if (isFloating)
                SliderRow(p, "Gap from the screen edge", 1, 30, delegate { return Math.Max(1, bar.EdgeMargin); },
                          OwnLook<double>(following, stop, followBox, delegate (double v) { bar.EdgeMargin = Math.Max(1, (int)v); }));
            Check(p, "Reserve window space", delegate { return bar.ReserveSpace; }, delegate (bool v) { bar.ReserveSpace = v; },
                  "Maximised windows stop at the bar. Off: the bar sits on the desktop like a widget, behind your windows.");
            if (isDock)
                Check(p, "Keep the dock centred on its monitor", delegate { return bar.KeepCentered; }, delegate (bool v) { bar.KeepCentered = v; },
                      "Also in the bar's right-click menu.");
        }

        void BarLookTab(StackPanel p, BarPlacement bar)
        {
            Func<CheckBox> noBox = delegate { return null; };     // Follow the bar style is on the Layout tab
            Func<bool> following = delegate { return bar.FollowStyle; };
            Action stop = delegate { bar.FollowStyle = false; };
            string layout = Styles.LayoutOf(bar);
            bool isFloating = layout == Styles.Floating, isDock = layout == Styles.Dock, isMinimal = layout == Styles.Minimal;

            Header(p, "Look");
            // Build 2: no bar font size - each module has S / M / L text (its own settings)
            SliderRow(p, "Corner radius", 0, 30, delegate { return bar.CornerRadius; }, OwnLook<double>(following, stop, noBox, delegate (double v) { bar.CornerRadius = v; }),
                      isFloating ? null : "The two corners away from the screen edge.");
            if (isFloating)
                Check(p, "Round all four corners", delegate { return bar.RoundAllCorners; },
                      OwnLook<bool>(following, stop, noBox, delegate (bool v) { bar.RoundAllCorners = v; }), "Off: only the two corners away from the screen edge.");
            Check(p, "Background opacity like the theme", delegate { return bar.UseThemeOpacity; }, delegate (bool v)
            {
                bar.UseThemeOpacity = v;
                Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); }));     // its own opacity shows / hides
            }, "Off: this bar has an opacity of its own.");
            if (!bar.UseThemeOpacity)
                SliderRow(p, "Background opacity (%)", 0, 100, delegate { return bar.BackgroundOpacity; }, delegate (double v) { bar.BackgroundOpacity = (int)v; });
            ComboRow(p, "Background blur", BarSettings.OwnBlurNames,
                     delegate { return BarSettings.OwnBlurNames[Math.Max(0, Array.IndexOf(BarSettings.OwnBlurModes, bar.Blur))]; },
                     OwnLook<string>(following, stop, noBox, delegate (string v) { bar.Blur = BarSettings.OwnBlurModes[Math.Max(0, Array.IndexOf(BarSettings.OwnBlurNames, v))]; }));
            ComboRow(p, "Separators", ModuleTypes.SeparatorStyles, delegate { return bar.SeparatorStyle; }, delegate (string v) { bar.SeparatorStyle = v; },
                     "Dots and Line go before the modules that have \"Show separator\" ticked; Chips puts every module in a rounded chip; None hides " +
                     "them. Themes and bar styles leave this as it is.");
            Place(p, Row("Glass edge", Segmented(EdgeFx.GlassEdges, delegate { return bar.GlassEdge; },
                OwnLook<string>(following, stop, noBox, delegate (string v) { bar.GlassEdge = v; }))));

            if (isMinimal) return;
            Header(p, "Outline");
            if (isFloating || isDock)
                Check(p, "Show an outline", delegate { return bar.ShowBorder; },
                      OwnLook<bool>(following, stop, noBox, delegate (bool v)
                      {
                          bar.ShowBorder = v;
                          if (v && bar.BorderThickness <= 0) bar.BorderThickness = 1;
                          Dispatcher.BeginInvoke(new Action(delegate { LoadBar(); }));     // the outline rows show / hide
                      }), "Drawn outward from the bar's edge.");
            if (bar.ShowBorder || layout == Styles.Classic)
            {
                SliderRow(p, "Thickness", 1, 8, delegate { return bar.BorderThickness; }, OwnLook<double>(following, stop, noBox, delegate (double v) { bar.BorderThickness = v; }));
                SliderRow(p, "Opacity (%)", 0, 100, delegate { return bar.BorderOpacity; }, OwnLook<double>(following, stop, noBox, delegate (double v) { bar.BorderOpacity = (int)v; }));
                ColorRow(p, "Colour (empty = Main Accent)", delegate { return bar.BorderColor; }, OwnLook<string>(following, stop, noBox, delegate (string v) { bar.BorderColor = v; }), true, false);
            }
        }

        // =================================================================
        // A widget's page
        // =================================================================
        void LoadWidget()
        {
            _loadedWidget = _selWidget;
            var p = _widPanel;
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            _widPane = null;
            _widStrip = null;
            Button back = Btn("‹  Back", delegate { Navigate(PgWidgets); });
            if (_selWidget == null)
            {
                PageHeader(p, "Widgets", "There are no widgets.", Btn("+ Add widget", AddWidgetAndOpen, true), back);
                return;
            }
            var w = _selWidget;
            var mons = MonitorHelper.GetAll();
            var paste = Btn("Paste", delegate
            {
                if (_copiedWidget == null) return;
                var r = MessageBox.Show(this, "Paste the copied widget settings onto \"" + w.Name + "\"?\n\nYes = settings and modules\nNo = settings only (keep this widget's modules)",
                                        "Paste settings", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;
                PasteWidget(_copiedWidget, w, r == MessageBoxResult.Yes);
                _mgr.SettingsChanged();
                LoadWidget();
            });
            paste.IsEnabled = _copiedWidget != null;
            var remove = Btn("Remove widget", delegate
            {
                if (MessageBox.Show(this, "Remove the widget \"" + w.Name + "\" and its modules?", "Remove widget", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _selWidget = null;
                _mgr.RemoveWidget(w);
                RefreshWidgets();
                Navigate(PgWidgets);
            });
            PageHeader(p, w.Name, "Widgets  ›  " + w.Name,
                       Btn("Save Widget Style", delegate { SaveWidgetStyleFrom(w); }),
                       Btn("Copy settings", delegate { _copiedWidget = CloneJson(w); paste.IsEnabled = true; }), paste, remove, back);
            StateLine(p, WidgetSummary(w, mons, true));

            _widStrip = new ModulePane(this, delegate { return w.Modules; }, ModuleTypes.WidgetTypes, true);
            _widStrip.Widget = delegate { return w; };
            _widStrip.Open = delegate (ModuleConfig m) { EditModule(null, w, m); };
            _widStrip.BuildStripInto(p, "Drag the widget by any empty part to move it; right-click it to lock or hide it, or Resize to drag its edges.");

            // Modules opens the module editor (‹ Back there comes back to the tab you were on)
            string[] keys = { "General", "Layout", "Look", "Slide away", "Modules" };
            string tab = Array.IndexOf(keys, _widTab) >= 0 && _widTab != "Modules" ? _widTab : keys[0];
            Tabs(p, keys, new[] { "General", "Layout", "Look", "Slide away", "Modules (" + w.Modules.Count + ")" }, tab, delegate (string k)
            {
                if (k == "Modules") { EditModule(null, w, null); return; }
                _widTab = k;
                LoadWidget();
            });
            if (tab == "Layout") WidgetLayoutTab(p, w);
            else if (tab == "Look") WidgetLookTab(p, w);
            else if (tab == "Slide away") WidgetSlideTab(p, w);
            else WidgetGeneralTab(p, w);
        }

        void WidgetGeneralTab(StackPanel p, WidgetSettings w)
        {
            Header(p, "General");
            TextRow(p, "Name", delegate { return w.Name; }, delegate (string v) { w.Name = v.Length == 0 ? "Widget" : v; });
            Check(p, "Show this widget", delegate { return w.Enabled; }, delegate (bool v) { w.Enabled = v; });
            MonitorRow(p, "Monitor", delegate { return w.Monitor; }, delegate { return w.MonitorKey; }, delegate (MonitorInfo m)
            {
                w.Monitor = m.DeviceName; w.MonitorKey = m.Key; w.OffsetX = 80; w.OffsetY = 80; w.LinkX = null; w.LinkY = null;
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); RebuildNav(); }));     // its summary (and "Not connected") follow
            }, "Dragging the widget onto another monitor changes this too. While its monitor is turned off or unplugged the widget is hidden, " +
               "and it comes back when the monitor does.");
            Check(p, "Keep above other windows", delegate { return w.AlwaysOnTop; }, delegate (bool v) { w.AlwaysOnTop = v; },
                  "Off: it sits on the desktop, behind your windows.");
            Check(p, "Lock position", delegate { return w.Locked; }, delegate (bool v) { w.Locked = v; }, "It can't be dragged (also in its right-click menu).");
        }

        void WidgetLayoutTab(StackPanel p, WidgetSettings w)
        {
            Func<CheckBox> noBox = delegate { return null; };     // Follow the widget style is on the Look tab
            Func<bool> following = delegate { return w.FollowStyle; };
            Action stop = delegate { w.FollowStyle = false; };

            Header(p, "Arrangement");
            string[] arrNames = { "Stack", "Row", "Grid" }, arrValues = { "Vertical", "Horizontal", "Grid" };
            ComboRow(p, "Modules", arrNames, delegate { int i = Array.IndexOf(arrValues, w.Direction); return arrNames[Math.Max(0, i)]; }, delegate (string v)
            {
                w.Direction = arrValues[Math.Max(0, Array.IndexOf(arrNames, v))];
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));     // the column count shows / hides
            });
            if (w.Direction == "Grid")
                SliderRow(p, "Columns", 1, 6, delegate { return w.GridColumns; }, delegate (double v) { w.GridColumns = (int)v; });
            Check(p, "Each module in its own card", delegate { return w.CardPerModule; }, delegate (bool v) { w.CardPerModule = v; });
            Check(p, "Show as tabs", delegate { return w.ShowTabs; }, delegate (bool v) { w.ShowTabs = v; },
                  "One module at a time, with a tab strip to switch; the widget is as big as its biggest module. Rename a tab in its module's settings " +
                  "(Modules > the module > Tab name).");
            ComboRow(p, "Density", new[] { "Comfortable", "Compact" }, delegate { return w.Density; }, delegate (string v) { w.Density = v; });

            Header(p, "Size");
            // Build 2: no "Keep the box size" - with S / M / L text a widget keeps its size by itself
            WidgetWindow hw = _mgr.WidgetWindowFor(w);
            Size now = hw != null ? hw.BoxSize : new Size(300, 200);
            Check(p, "Automatic width", delegate { return w.Width == 0; }, delegate (bool v)
            {
                w.Width = v ? 0 : (int)Math.Max(80, Math.Min(1200, Math.Round(now.Width > 0 ? now.Width : 300)));
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));     // the width slider shows / hides
            }, "Fits the modules. Off: a width of its own (the outline is outside it).");
            if (w.Width > 0)
                SliderRow(p, "Width", 80, 1200, delegate { return w.Width; }, delegate (double v) { w.Width = (int)v; });
            Check(p, "Automatic height", delegate { return w.Height == 0; }, delegate (bool v)
            {
                w.Height = v ? 0 : (int)Math.Max(40, Math.Min(1200, Math.Round(now.Height > 0 ? now.Height : 200)));
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));
            }, "Fits the modules. Off: a fixed box - longer content scrolls.");
            if (w.Height > 0)
                SliderRow(p, "Height", 40, 1200, delegate { return w.Height; }, delegate (double v) { w.Height = (int)v; });
            SliderRow(p, "Module height", 20, 200, delegate { return w.ModuleHeight; }, delegate (double v) { w.ModuleHeight = (int)v; },
                      "Clocks and graphs size themselves from it.");
            // Build 2: no padding setting - Comfortable / Compact sets it
            SliderRow(p, "Space between modules", 0, 30, delegate { return w.Spacing; }, OwnLook<double>(following, stop, noBox, delegate (double v) { w.Spacing = (int)v; }));
            // Build 2: no widget font size - each module has S / M / L text
            Place(p, Txt("Or right-click the widget > Resize and drag its edges: left / right set the width; top / bottom make the module rows taller or " +
                         "shorter (graphs and clocks grow with them); hold Shift while dragging top / bottom for a fixed height instead.", "vsDim", 12, false));
        }

        void WidgetLookTab(StackPanel p, WidgetSettings w)
        {
            CheckBox follow = null;
            Func<CheckBox> box = delegate { return follow; };
            Func<bool> following = delegate { return w.FollowStyle; };
            Action stop = delegate { w.FollowStyle = false; };

            Header(p, "Look");
            follow = Check(p, "Follow the widget style", delegate { return w.FollowStyle; }, delegate (bool v)
            {
                w.FollowStyle = v;
                if (v && S.WidgetStyle != null) { Styles.ApplyWidget(w, S.WidgetStyle); Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); })); }
            }, "Takes its corners, outline, shadow and spacing from the widget style (Appearance > Widget style). Changing them here gives this widget " +
               "its own look and turns this off; Save Widget Style (top right) keeps that look as a widget style you can use in themes.");
            SliderRow(p, "Corner radius", 0, 40, delegate { return w.CornerRadius; }, OwnLook<double>(following, stop, box, delegate (double v) { w.CornerRadius = v; }));
            Check(p, "Background opacity like the theme", delegate { return w.UseThemeOpacity; }, delegate (bool v)
            {
                w.UseThemeOpacity = v;
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));     // its own opacity shows / hides
            }, "Off: this widget has an opacity of its own.");
            if (!w.UseThemeOpacity)
                SliderRow(p, "Background opacity (%)", 0, 100, delegate { return w.Opacity; }, delegate (double v) { w.Opacity = (int)v; });
            Check(p, "Shadow", delegate { return w.ShowShadow; }, OwnLook<bool>(following, stop, box, delegate (bool v) { w.ShowShadow = v; }));
            ComboRow(p, "Background blur", BarSettings.OwnBlurNames,
                     delegate { return BarSettings.OwnBlurNames[Math.Max(0, Array.IndexOf(BarSettings.OwnBlurModes, w.Blur))]; },
                     OwnLook<string>(following, stop, box, delegate (string v) { w.Blur = BarSettings.OwnBlurModes[Math.Max(0, Array.IndexOf(BarSettings.OwnBlurNames, v))]; }),
                     "As the theme = the Background blur on the Appearance page. Blur / Frosted show the wallpaper behind this widget, softened; " +
                     "the background opacity decides how much of it shows. Saved with widget styles and themes.");
            Place(p, Row("Glass edge", Segmented(EdgeFx.GlassEdges, delegate { return w.GlassEdge; },
                OwnLook<string>(following, stop, box, delegate (string v) { w.GlassEdge = v; }))));

            Header(p, "Header and accent");
            Check(p, "Show a header row", delegate { return w.ShowHeader; }, delegate (bool v)
            {
                w.ShowHeader = v;
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));
            }, "The first module's icon, the widget's name and a collapse button. The header icon takes the accent colour below.");
            if (w.ShowHeader)
            {
                Check(p, "Collapsible", delegate { return w.Collapsible; }, delegate (bool v)
                {
                    w.Collapsible = v;
                    Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));
                }, "An arrow on the header: click the header to collapse the widget to it, and again to open it.");
                if (w.Collapsible)
                    Check(p, "Collapsed to the header", delegate { return w.Collapsed; }, delegate (bool v) { w.Collapsed = v; });
                ColorRow(p, "Header text colour (empty = the theme's text colour)", delegate { return w.HeaderTextColor; }, delegate (string v) { w.HeaderTextColor = v; }, true, false);
            }
            ComboRow(p, "Accent edge", new[] { "None", "Top", "Left" }, delegate { return w.AccentEdge; }, delegate (string v) { w.AccentEdge = v; });
            ColorRow(p, "Accent colour (empty = the theme's Main Accent)", delegate { return w.AccentColor; }, delegate (string v) { w.AccentColor = v; }, true, false);
            Note(p, "The accent colour is used by the accent edge, the header icon and the widget's graphs. The outline keeps its own colour (empty / Main Accent = the theme's).");

            Header(p, "Outline");
            Check(p, "Show an outline", delegate { return w.ShowBorder; }, OwnLook<bool>(following, stop, box, delegate (bool v)
            {
                w.ShowBorder = v;
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));     // the outline rows show / hide
            }));
            if (w.ShowBorder)
            {
                SliderRow(p, "Thickness", 1, 8, delegate { return w.BorderThickness; }, OwnLook<double>(following, stop, box, delegate (double v) { w.BorderThickness = v; }));
                SliderRow(p, "Opacity (%)", 0, 100, delegate { return w.BorderOpacity; }, OwnLook<double>(following, stop, box, delegate (double v) { w.BorderOpacity = (int)v; }));
                ColorRow(p, "Colour (empty = Main Accent)", delegate { return w.BorderColor; }, OwnLook<string>(following, stop, box, delegate (string v) { w.BorderColor = v; }), true, false);
            }
        }

        void WidgetSlideTab(StackPanel p, WidgetSettings w)
        {
            WidgetWindow hw = _mgr.WidgetWindowFor(w);
            List<string> ways = hw != null ? hw.HideWays() : new List<string>();
            Header(p, "Slide away at the screen edge");
            Check(p, "Slide away when the mouse leaves it", delegate { return w.AutoHide; }, delegate (bool v)
            {
                w.AutoHide = v;
                Dispatcher.BeginInvoke(new Action(delegate { LoadWidget(); }));     // how it moves shows / hides
            }, "Push the mouse against the screen edge to bring it back. While it is out it sits in front of your windows; it stays out while you " +
               "drag, resize or type in it, or have its menu or a popup open.");
            Place(p, Txt(ways.Count == 0 ? "This widget isn't against a screen edge or a bar right now, so it stays put. Drag it against an edge or a bar to snap it there." :
                         "Against the " + string.Join(" and ", ways.ConvertAll(WidgetWindow.EdgeName)) + " edge" + (ways.Count > 1 ? "s" : "") + ".",
                         w.AutoHide && ways.Count == 0 ? "vsAccent" : "vsDim", 12, false));
            if (!w.AutoHide) return;

            Header(p, "How it moves");
            if (ways.Count > 1)
                ComboRow(p, "Slides towards", ways.ToArray(), delegate { return ways.Contains(w.AutoHideWay) ? w.AutoHideWay : ways[0]; }, delegate (string v) { w.AutoHideWay = v; });
            ComboRow(p, "Animation", ModuleTypes.WidgetHideAnims, delegate { return w.AutoHideAnim; }, delegate (string v) { w.AutoHideAnim = v; });
            SliderRow(p, "Speed (ms)", 60, 1000, delegate { return w.AutoHideMs; }, delegate (double v) { w.AutoHideMs = (int)v; }, "Lower is faster.");
            ComboRow(p, "Bounce when it comes back", ModuleTypes.TaskAnimBounces, delegate { return w.AutoHideBounce; }, delegate (string v) { w.AutoHideBounce = v; });
            SliderRow(p, "Wait before sliding away (ms)", 0, 3000, delegate { return w.AutoHideDelay; }, delegate (double v) { w.AutoHideDelay = (int)v; });
            Check(p, "Glow at the edge while it's away", delegate { return w.AutoHideGlow; }, delegate (bool v) { w.AutoHideGlow = v; },
                  "A thin accent glow along the screen edge shows where it is and where to push the mouse.");
        }
    }
}
