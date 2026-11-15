using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The module editor page (a bar's or a widget's modules).</summary>
    public partial class SettingsWindow
    {
        // =================================================================
        // The module editor page (a bar's or a widget's modules)
        // =================================================================
        void BuildModulesPage(bool widget)
        {
            string key = widget ? PgWidgetModules : PgBarModules;
            var p = Page(key);
            p.Children.Clear();
            _cardOf.Remove(p);
            if (widget ? _selWidget == null : _selBar == null) return;

            string owner;
            ModulePane pane;
            if (widget)
            {
                var w = _selWidget;
                owner = w.Name;
                pane = new ModulePane(this, delegate { return w.Modules; }, ModuleTypes.WidgetTypes, true);
                pane.Widget = delegate { return w; };
                _widPane = pane;
            }
            else
            {
                var b = _selBar;
                owner = char.ToUpperInvariant(b.Edge[0]) + b.Edge.Substring(1).ToLowerInvariant() + " bar";
                pane = new ModulePane(this, delegate { return b.Modules; }, ModuleTypes.BarTypes, false);
                pane.Bar = delegate { return b; };
                _barPane = pane;
            }
            PageHeader(p, "Modules", (widget ? "Widgets" : "Bars") + "  ›  " + owner + "  ›  Modules",
                       Btn("‹  Back", delegate
                       {
                           if (widget) { LoadWidget(); Navigate(PgWidget); }
                           else { LoadBar(); Navigate(PgBar); }
                       }));
            pane.BuildInto(p);
        }

        /// <summary>
        /// Module editor: the modules as chips in their real order (click to edit, drag to reorder), a gallery to add new ones, and the
        /// selected module's settings in folding sections.
        /// </summary>
        sealed partial class ModulePane
        {
            const string DragFormat = "vShell.ModuleConfig";

            readonly SettingsWindow _w;
            readonly Func<List<ModuleConfig>> _source;
            readonly string[] _types;
            public readonly bool IsWidget;
            public readonly StackPanel Props = new StackPanel();
            readonly WrapPanel _strip = new WrapPanel();
            readonly Border _stripHost = new Border();       // the strip, or (three zones) a column per zone
            TextBlock _hint;
            /// <summary>Bars: the bar these modules are on (three zones show as three columns).</summary>
            public Func<BarPlacement> Bar;
            bool ZonesOn { get { BarPlacement b = Bar != null ? Bar() : null; return b != null && b.Zones && b.FullWidth; } }
            static int ZoneIndex(ModuleConfig m) { return m.Zone == "Centre" ? 1 : m.Zone == "Right" ? 2 : 0; }
            readonly Grid _head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            readonly List<ModuleConfig> _shown = new List<ModuleConfig>();
            ModuleConfig _sel;
            Point _down;
            bool _pressing;

            public ModulePane(SettingsWindow w, Func<List<ModuleConfig>> source, string[] types, bool widget)
            {
                _w = w; _source = source; _types = types; IsWidget = widget;
            }

            /// <summary>The strip on a bar's / widget's page (no gallery or settings there): clicking a module calls this, + Add modules
            /// calls it with null. Set = strip only.</summary>
            public Action<ModuleConfig> Open;
            bool StripOnly { get { return Open != null; } }
            string _tip;

            /// <summary>Just the strip and its hint (a bar's / widget's page).</summary>
            public void BuildStripInto(StackPanel page, string tip)
            {
                _tip = tip;
                page.Children.Add(StripCard());
                AddHint(page);
                Refresh(null);
            }

            public void BuildInto(StackPanel page)
            {
                // the modules in order
                page.Children.Add(StripCard());
                AddHint(page);

                // nothing selected: the gallery to add modules; a module selected: "+ Add modules" and its settings
                var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                addRow.Children.Add(Btn("+  Add modules", delegate { ShowGallery(); }, true));
                _settingsView.Children.Add(addRow);
                _settingsView.Children.Add(_head);
                _settingsView.Children.Add(Props);
                page.Children.Add(_settingsView);
                page.Children.Add(_galleryView);

                Refresh(null);
            }

            void AddHint(StackPanel page)
            {
                _hint = Txt("", "vsDim", 12, false);
                _hint.Margin = new Thickness(2, 6, 0, 14);
                _hint.TextWrapping = TextWrapping.Wrap;
                page.Children.Add(_hint);
            }

            /// <summary>The card the strip sits in: "Modules" and Show on screen (+ Add modules on a bar's / widget's page) above it.</summary>
            Border StripCard()
            {
                var top = new DockPanel { Margin = new Thickness(0, 0, 6, 8), LastChildFill = true };
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                if (StripOnly)
                {
                    var add = Btn("+  Add modules", delegate { Open(null); });
                    add.Margin = new Thickness(0, 0, 6, 0);
                    btns.Children.Add(add);
                }
                Button locate = null;
                locate = Btn("Show on screen", delegate { Locate(locate); });
                locate.ToolTip = "Outline this " + (IsWidget ? "widget" : "bar") + " on the screen for a moment" +
                                 (IsWidget ? " (it comes out if it has slid away)" : "");
                btns.Children.Add(locate);
                DockPanel.SetDock(btns, Dock.Right);
                top.Children.Add(btns);
                top.Children.Add(Txt(IsWidget ? "Modules in the widget" : "Modules on the bar", "vsDim", 12, false));

                var body = new StackPanel();
                body.Children.Add(top);
                body.Children.Add(_stripHost);
                _stripHost.SizeChanged += delegate (object s, SizeChangedEventArgs e) { if (e.WidthChanged) Fit(); };
                var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 4, 4), Child = body };
                Ref(card, Border.BackgroundProperty, "vsCard");
                Ref(card, Border.BorderBrushProperty, "vsLine");
                return card;
            }

            /// <summary>Outline the real bar / widget on the screen for a moment (a widget that slid away comes out for it).</summary>
            void Locate(Button b)
            {
                RECT r = new RECT();
                bool found = false;
                if (IsWidget)
                {
                    WidgetWindow ww = Widget != null ? _w._mgr.WidgetWindowFor(Widget()) : null;
                    if (ww != null && ww.IsVisible)
                    {
                        ww.ComeOut(LocateOutline.Millis + 500);
                        found = ww.TryVisibleRect(out r);
                    }
                }
                else
                {
                    BarWindow bw = Bar != null ? _w._mgr.BarWindowFor(Bar()) : null;
                    if (bw != null && bw.IsVisible)
                    {
                        IntPtr h = new System.Windows.Interop.WindowInteropHelper(bw).Handle;
                        found = h != IntPtr.Zero && Native.GetWindowRect(h, out r) && r.Right > r.Left && r.Bottom > r.Top;
                    }
                }
                if (found)
                {
                    var accent = _w.TryFindResource("vsAccent") as SolidColorBrush;
                    LocateOutline.Flash(r, accent != null ? accent.Color : Color.FromRgb(0x00, 0x78, 0xD4));
                    return;
                }
                // hidden (Show this widget off, or its monitor isn't there): say so on the button for a moment
                b.Content = "Not on screen right now";
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += delegate { t.Stop(); b.Content = "Show on screen"; };
                t.Start();
            }

            // ---- fitting the chips --------------------------------------------------------------------
            /// <summary>How much of each chip's name shows: 0 its full name, 1 its short name, 2 just its glyph (the name is the tooltip).
            /// The most that lets the row of chips (the strip, or each zone on its own) fit on one line; with only glyphs they wrap.</summary>
            readonly int[] _levels = new int[3];
            readonly List<ChipRow> _rows = new List<ChipRow>();

            sealed class ChipRow
            {
                public Panel Panel;
                public int Zone;        // 0 without zones
                public readonly List<ChipName> Chips = new List<ChipName>();
            }

            sealed class ChipName
            {
                public Border Chip;
                public TextBlock Glyph, Name;
                public string Full, Short;

                public void Show(int level)
                {
                    Name.Text = level == 0 ? Full : Short;
                    Name.Visibility = level >= 2 ? Visibility.Collapsed : Visibility.Visible;
                    Glyph.Margin = new Thickness(0, 0, level >= 2 ? 0 : 6, 0);
                }
            }

            void Fit()
            {
                foreach (ChipRow row in _rows)
                {
                    double room = row.Panel.ActualWidth;
                    if (room < 1) continue;     // not laid out yet: Loaded / SizeChanged comes back
                    int want = 2;
                    for (int lv = 0; lv < 2 && want == 2; lv++)
                    {
                        // a little slack before going back to longer names, so a scrollbar coming and going can't flip it to and fro
                        double used = lv < _levels[row.Zone] ? 24 : 0;
                        foreach (ChipName c in row.Chips)
                        {
                            c.Show(lv);
                            c.Chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            used += c.Chip.DesiredSize.Width + c.Chip.Margin.Left + c.Chip.Margin.Right;
                        }
                        if (used <= room) want = lv;
                    }
                    _levels[row.Zone] = want;
                    foreach (ChipName c in row.Chips) c.Show(want);
                }
            }

            public ModuleConfig Selected()
            {
                return _sel != null && _source().Contains(_sel) ? _sel : null;
            }

            // ---- strip --------------------------------------------------------------------------------
            public void Refresh(ModuleConfig select)
            {
                var list = _source();
                // nothing is picked unless asked for (the gallery shows then)
                if (select != null && list.Contains(select)) _sel = select;
                else if (_sel != null && !list.Contains(_sel)) _sel = null;

                _shown.Clear();
                _shown.AddRange(list);
                _strip.Children.Clear();
                _rows.Clear();
                if (ZonesOn) _stripHost.Child = ZoneColumns(list);
                else
                {
                    var row = new ChipRow { Panel = _strip, Zone = 0 };
                    foreach (var m in list) _strip.Children.Add(Chip(m, row));
                    if (list.Count == 0)
                    {
                        var empty = Txt("No modules yet", "vsDim", 12, false);
                        empty.FontStyle = FontStyles.Italic;
                        empty.Margin = new Thickness(2, 6, 0, 10);
                        _strip.Children.Add(empty);
                    }
                    _rows.Add(row);
                    _stripHost.Child = _strip;
                }
                _stripHost.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(Fit));
                if (StripOnly)
                {
                    if (_hint != null)
                        _hint.Text = (ZonesOn ? "Click a module to open its settings; drag it to move it - into another zone to change its zone. "
                                              : "Click a module to open its settings; drag it to move it. ") + _tip;
                    return;
                }
                if (_hint != null)
                    _hint.Text = (list.Count == 0 ? "No modules yet - add some below. " : "") +
                                 (IsWidget ? "Top to bottom (or left to right). Click a module to edit it (again to go back to adding modules), drag it to move it."
                               : ZonesOn ? "The bar's three zones. Click a module to edit it (again to go back to adding modules); drag it to move it - into another zone to change its zone."
                               : "Left to right, as on the bar. Click a module to edit it (again to go back to adding modules), drag it to move it.");
                BuildHead();
                _w.ShowModuleProps(this);
                ShowViews();
            }

            /// <summary>A module's chip: its glyph and name (shortened to fit, see Fit), click to edit it, drag to move it.</summary>
            UIElement Chip(ModuleConfig m, ChipRow row)
            {
                bool on = m == _sel;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var glyph = Glyph(IconUtil.ModuleGlyph(m.Type), 13);
                Ref(glyph, TextBlock.ForegroundProperty, on ? "vsAccent" : "vsDim");
                sp.Children.Add(glyph);
                var name = Txt("", "vsFg", 12.5, on);
                name.TextWrapping = TextWrapping.NoWrap;
                sp.Children.Add(name);
                var chip = new Border
                {
                    Child = sp, Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1), Cursor = Cursors.Hand, AllowDrop = true, Tag = m,
                    ToolTip = ModuleTypes.Display(m.Type) + (StripOnly ? " - click to open its settings, drag to move it" : "")
                };
                var cn = new ChipName { Chip = chip, Glyph = glyph, Name = name, Full = ModuleTypes.Display(m.Type), Short = ModuleTypes.ShortName(m.Type) };
                cn.Show(_levels[row.Zone]);
                row.Chips.Add(cn);
                Ref(chip, Border.BorderBrushProperty, on ? "vsAccent" : "vsCtrlBorder");
                if (on) Ref(chip, Border.BackgroundProperty, "vsSelBg"); else Ref(chip, Border.BackgroundProperty, "vsCtrl");

                chip.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { _down = e.GetPosition(_w); _pressing = true; e.Handled = true; };
                chip.MouseMove += delegate (object s, MouseEventArgs e)
                {
                    if (!_pressing || e.LeftButton != MouseButtonState.Pressed) return;
                    Point now = e.GetPosition(_w);       // the window: the chips may be in the zone columns, not the strip
                    if (Math.Abs(now.X - _down.X) < SystemParameters.MinimumHorizontalDragDistance &&
                        Math.Abs(now.Y - _down.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    _pressing = false;
                    try { DragDrop.DoDragDrop(chip, new DataObject(DragFormat, m), DragDropEffects.Move); }
                    catch (Exception ex) { Log.Write("Module drag: " + ex.Message); }
                };
                chip.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    if (!_pressing) return;
                    _pressing = false;
                    e.Handled = true;
                    if (StripOnly) Open(m);
                    else if (m != _sel) Refresh(m);
                    else ShowGallery();          // the open one again: back to adding modules
                };
                chip.DragOver += delegate (object s, DragEventArgs e)
                {
                    e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;
                };
                chip.DragEnter += delegate { Ref(chip, Border.BorderBrushProperty, "vsAccent"); };
                chip.DragLeave += delegate { Ref(chip, Border.BorderBrushProperty, m == _sel ? "vsAccent" : "vsCtrlBorder"); };
                chip.Drop += delegate (object s, DragEventArgs e)
                {
                    e.Handled = true;
                    var dragged = e.Data.GetData(DragFormat) as ModuleConfig;
                    if (dragged == null || dragged == m) { Refresh(_sel); return; }
                    var list = _source();
                    if (!list.Contains(dragged)) return;
                    bool after = e.GetPosition(chip).X > chip.ActualWidth / 2;
                    list.Remove(dragged);
                    int at = list.IndexOf(m) + (after ? 1 : 0);
                    list.Insert(Math.Max(0, Math.Min(list.Count, at)), dragged);
                    if (ZonesOn) dragged.Zone = string.IsNullOrEmpty(m.Zone) ? "Left" : m.Zone;     // next to a module = in its zone
                    _w._mgr.SettingsChanged();
                    Refresh(dragged);
                };
                return chip;
            }

            /// <summary>Three zones: a column per zone (Left / Centre / Right), each with its modules in order; drop a module on a column
            /// (or next to a module in it) to move it into that zone.</summary>
            UIElement ZoneColumns(List<ModuleConfig> list)
            {
                var g = new Grid();
                string[] names = ModuleTypes.Zones;
                for (int z = 0; z < 3; z++)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var col = new StackPanel();
                    var title = Txt(names[z], "vsDim", 11.5, true);
                    title.Margin = new Thickness(2, 0, 0, 6);
                    col.Children.Add(title);
                    var wrap = new WrapPanel { MinHeight = 34 };
                    var row = new ChipRow { Panel = wrap, Zone = z };
                    foreach (var m in list) if (ZoneIndex(m) == z) wrap.Children.Add(Chip(m, row));
                    if (row.Chips.Count > 0) _rows.Add(row);
                    if (wrap.Children.Count == 0)
                    {
                        var empty = Txt("Drop modules here", "vsDim", 12, false);
                        empty.FontStyle = FontStyles.Italic;
                        empty.Margin = new Thickness(2, 6, 0, 0);
                        wrap.Children.Add(empty);
                    }
                    col.Children.Add(wrap);
                    var zone = new Border
                    {
                        Child = col, Padding = new Thickness(8, 8, 2, 4), Margin = new Thickness(z == 0 ? 0 : 4, 0, z == 2 ? 6 : 4, 6),
                        CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), AllowDrop = true, Background = Brushes.Transparent
                    };
                    zone.BorderBrush = Brushes.Transparent;
                    Ref(zone, Border.BorderBrushProperty, "vsLine");
                    int zi = z;
                    zone.DragOver += delegate (object s, DragEventArgs e)
                    {
                        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
                        e.Handled = true;
                    };
                    zone.DragEnter += delegate { Ref(zone, Border.BorderBrushProperty, "vsAccent"); };
                    zone.DragLeave += delegate { Ref(zone, Border.BorderBrushProperty, "vsLine"); };
                    zone.Drop += delegate (object s, DragEventArgs e)
                    {
                        e.Handled = true;
                        var dragged = e.Data.GetData(DragFormat) as ModuleConfig;
                        var l = _source();
                        if (dragged == null || !l.Contains(dragged)) return;
                        // to the end of that zone (after its last module in the bar's order)
                        l.Remove(dragged);
                        int at = l.Count;
                        for (int i = l.Count - 1; i >= 0; i--) if (ZoneIndex(l[i]) == zi) { at = i + 1; break; }
                        l.Insert(at, dragged);
                        dragged.Zone = names[zi];
                        _w._mgr.SettingsChanged();
                        Refresh(dragged);
                    };
                    Grid.SetColumn(zone, z);
                    g.Children.Add(zone);
                }
                return g;
            }

            static string Ordinal(int n)
            {
                int t = n % 100;
                string suf = (t >= 11 && t <= 13) ? "th" : (n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
                return n + suf;
            }

            void BuildHead()
            {
                _head.Children.Clear();
                _head.ColumnDefinitions.Clear();
                _head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var list = _source();
                if (_sel == null) return;

                var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                title.Children.Add(Txt(ModuleTypes.Display(_sel.Type), "vsFg", 18, true));
                var pos = Txt(Ordinal(list.IndexOf(_sel) + 1) + " of " + list.Count, "vsDim", 12.5, false);
                pos.Margin = new Thickness(10, 3, 0, 0);
                title.Children.Add(pos);
                _head.Children.Add(title);

                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                ModuleConfig cur = _sel;
                var back = Btn(IsWidget ? "↑" : "←", delegate { Move(cur, -1); });
                back.ToolTip = IsWidget ? "Move up" : "Move left";
                var fwd = Btn(IsWidget ? "↓" : "→", delegate { Move(cur, 1); });
                fwd.ToolTip = IsWidget ? "Move down" : "Move right";
                var dup = Btn("Duplicate", delegate
                {
                    var c = CloneJson(cur);
                    c.Id = Guid.NewGuid().ToString("N");
                    var l = _source();
                    l.Insert(l.IndexOf(cur) + 1, c);
                    _w._mgr.SettingsChanged();
                    Refresh(c);
                });
                var rem = Btn("Remove", delegate
                {
                    var l = _source();
                    int i = l.IndexOf(cur);
                    l.Remove(cur);
                    _w._mgr.SettingsChanged();
                    Refresh(l.Count == 0 ? null : l[Math.Max(0, Math.Min(l.Count - 1, i))]);
                });
                foreach (var b in new[] { back, fwd, dup, rem }) { b.Margin = new Thickness(6, 0, 0, 0); btns.Children.Add(b); }
                Grid.SetColumn(btns, 1);
                _head.Children.Add(btns);
            }

            void Move(ModuleConfig cfg, int delta)
            {
                var list = _source();
                int i = list.IndexOf(cfg);
                int j = i + delta;
                if (i < 0 || j < 0 || j >= list.Count) return;
                list.RemoveAt(i);
                list.Insert(j, cfg);
                _w._mgr.SettingsChanged();
                Refresh(cfg);
            }

            // ---- gallery --------------------------------------------------------------------------------
            static readonly string[][] Groups =
            {
                new[] { "System", ModuleTypes.Cpu, ModuleTypes.Memory, ModuleTypes.Gpu, ModuleTypes.Disk, ModuleTypes.Drives, ModuleTypes.Network, ModuleTypes.Hwinfo, ModuleTypes.Processes },
                new[] { "Shell", ModuleTypes.StartMenu, ModuleTypes.Launcher, ModuleTypes.RunBox, ModuleTypes.Terminal, ModuleTypes.Taskbar, ModuleTypes.Tray, ModuleTypes.Notifications, ModuleTypes.Spacer },
                new[] { "Info", ModuleTypes.Clock, ModuleTypes.Notes, ModuleTypes.Weather, ModuleTypes.NowPlaying, ModuleTypes.Battery, ModuleTypes.Volume, ModuleTypes.Uptime, ModuleTypes.Services, ModuleTypes.Label },
                new[] { "Smart home & more" },
            };

            /// <summary>The gallery's groups with the added plugins' modules in theirs (a group of their own when it's a new name).</summary>
            static List<string[]> GroupsNow()
            {
                var l = new List<List<string>>();
                foreach (string[] g in Groups) l.Add(new List<string>(g));
                foreach (PluginModule m in PluginManager.AllModules())
                {
                    List<string> into = l.Find(delegate (List<string> g) { return g[0] == m.Group; });
                    if (into == null) { into = new List<string> { m.Group }; l.Add(into); }
                    if (!into.Contains(m.Type)) into.Add(m.Type);
                }
                l.RemoveAll(delegate (List<string> g) { return g.Count < 2; });      // a group with nothing in it (Smart home & more with no plugins added)
                return l.ConvertAll(delegate (List<string> g) { return g.ToArray(); });
            }

            static string Blurb(string type)
            {
                switch (type)
                {
                    case ModuleTypes.Cpu: return "usage, temperature";
                    case ModuleTypes.Memory: return "RAM in use";
                    case ModuleTypes.Gpu: return "usage, VRAM";
                    case ModuleTypes.Disk: return "space or activity";
                    case ModuleTypes.Drives: return "This PC: every drive, opens in vXplorer";
                    case ModuleTypes.Network: return "download / upload";
                    case ModuleTypes.Hwinfo: return "any HWiNFO or LibreHardwareMonitor reading";
                    case ModuleTypes.Processes: return "busiest programs";
                    case ModuleTypes.StartMenu: return "apps and power";
                    case ModuleTypes.Launcher: return "shortcuts, groups";
                    case ModuleTypes.Taskbar: return "open windows";
                    case ModuleTypes.Tray: return "notification icons";
                    case ModuleTypes.Spacer: return "gap / push right";
                    case ModuleTypes.Weather: return "now + forecast";
                    case ModuleTypes.NowPlaying: return "music, controls";
                    case ModuleTypes.Clock: return "time and date";
                    case ModuleTypes.Battery: return "charge";
                    case ModuleTypes.Volume: return "level, device";
                    case ModuleTypes.Uptime: return "since start-up, restart needed";
                    case ModuleTypes.Label: return "your own text";
                    case ModuleTypes.Notes: return "notes, lists, log";
                    case ModuleTypes.RunBox: return "run, !search, commands";
                    case ModuleTypes.Terminal: return "vTerm drops down from the bar";
                    case ModuleTypes.Notifications: return "history, unread badge";
                    case ModuleTypes.Services: return "up / down dots";
                    default:
                        {
                            PluginModule pm = PluginManager.ModuleOf(type);
                            return pm != null ? pm.Blurb : "";
                        }
                }
            }

            /// <summary>The module order or contents changed without going through this window (Ctrl+drag on a bar): refresh.</summary>
            public void SyncIfChanged()
            {
                var src = _source();
                bool same = _shown.Count == src.Count;
                if (same) for (int i = 0; i < src.Count; i++) if (_shown[i] != src[i]) { same = false; break; }
                if (!same) Refresh(_sel);
            }
        }

    }
}
