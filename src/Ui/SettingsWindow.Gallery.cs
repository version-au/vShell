using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    public sealed partial class SettingsWindow
    {
        sealed partial class ModulePane
        {
            /// <summary>Widgets: the widget these modules are in (its look for the previews).</summary>
            public Func<WidgetSettings> Widget;

            readonly StackPanel _settingsView = new StackPanel();
            readonly StackPanel _galleryView = new StackPanel();
            /// <summary>Where "Add" puts a module: after this one (the module that was open when "+ Add modules" was clicked), else at the end.</summary>
            ModuleConfig _addAfter;
            /// <summary>Bars with zones: the zone new modules go into.</summary>
            string _addZone = "Left";
            string _category = "All";
            TextBox _search;
            readonly List<KeyValuePair<FrameworkElement, string[]>> _cards = new List<KeyValuePair<FrameworkElement, string[]>>();   // card, (type, name, blurb, group)

            /// <summary>Back to adding modules (the open module is where new ones go after).</summary>
            void ShowGallery()
            {
                if (_sel != null) { _addAfter = _sel; if (ZonesOn) _addZone = string.IsNullOrEmpty(_sel.Zone) ? "Left" : _sel.Zone; }
                _sel = null;
                Refresh(null);
            }

            /// <summary>Settings of the open module, or (none open) the gallery - built fresh each time it shows, cleared when it hides.</summary>
            void ShowViews()
            {
                bool gallery = _sel == null;
                _settingsView.Visibility = gallery ? Visibility.Collapsed : Visibility.Visible;
                _galleryView.Visibility = gallery ? Visibility.Visible : Visibility.Collapsed;
                if (gallery) BuildGalleryView();
                else ClearGallery();
            }

            void ClearGallery()
            {
                _galleryView.Children.Clear();      // the previews unload: their timers stop
                _cards.Clear();
            }

            // =================================================================
            // The gallery
            // =================================================================
            void BuildGalleryView()
            {
                ClearGallery();
                var v = _galleryView;

                // title, search, categories
                var top = new DockPanel { Margin = new Thickness(0, 4, 0, 10), LastChildFill = true };
                _search = new TextBox { Width = 220, ToolTip = "Type to find a module" };
                _search.TextChanged += delegate { ApplyFilter(); };
                var searchRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var lens = Glyph("", 13);
                Ref(lens, TextBlock.ForegroundProperty, "vsDim");
                lens.Margin = new Thickness(0, 0, 8, 0);
                lens.VerticalAlignment = VerticalAlignment.Center;
                searchRow.Children.Add(lens);
                searchRow.Children.Add(_search);
                DockPanel.SetDock(searchRow, Dock.Right);
                top.Children.Add(searchRow);
                var title = new StackPanel();
                title.Children.Add(Txt("Add a module", "vsFg", 18, true));
                var sub = Txt(IsWidget ? "Each card shows roughly how the module looks in a widget - click one to add it."
                                       : "Each tile shows roughly how the module looks on a bar - click one to add it.", "vsDim", 12, false);
                sub.TextWrapping = TextWrapping.Wrap;
                title.Children.Add(sub);
                top.Children.Add(title);
                v.Children.Add(top);

                var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                if (ZonesOn)
                {
                    var zones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var lbl = Txt("Add to", "vsDim", 12.5, false);
                    lbl.Margin = new Thickness(0, 0, 8, 0);
                    lbl.VerticalAlignment = VerticalAlignment.Center;
                    zones.Children.Add(lbl);
                    zones.Children.Add(Switch(ModuleTypes.Zones, delegate { return _addZone; }, delegate (string z) { _addZone = z; }));
                    DockPanel.SetDock(zones, Dock.Right);
                    filters.Children.Add(zones);
                }
                var chips = new WrapPanel();
                var cats = new List<string> { "All" };
                foreach (var g in GroupsNow()) cats.Add(g[0]);
                foreach (string c in cats)
                {
                    bool any = c == "All";
                    if (!any) foreach (var g in GroupsNow()) if (g[0] == c) for (int i = 1; i < g.Length; i++) if (Array.IndexOf(_types, g[i]) >= 0) any = true;
                    if (any) chips.Children.Add(Chip(c));
                }
                filters.Children.Add(chips);
                v.Children.Add(filters);

                // the modules, grouped
                PreviewHost host = MakeHost();
                var used = new HashSet<string>();
                var groups = new List<string[]>(GroupsNow());
                var other = new List<string> { "Other" };
                foreach (string t in _types)
                {
                    bool known = false;
                    foreach (var g in GroupsNow()) if (Array.IndexOf(g, t, 1) >= 1) known = true;
                    if (!known) other.Add(t);
                }
                if (other.Count > 1) groups.Add(other.ToArray());
                var holder = new UniformGrid { Columns = 3 };
                foreach (var g in groups)
                    for (int i = 1; i < g.Length; i++)
                    {
                        string type = g[i];
                        if (Array.IndexOf(_types, type) < 0 || !used.Add(type)) continue;
                        string name = ModuleTypes.Display(type), blurb = Blurb(type);
                        FrameworkElement card = IsWidget ? WidgetCard(host, type, name, blurb) : BarTile(host, type, name, blurb);
                        holder.Children.Add(card);
                        _cards.Add(new KeyValuePair<FrameworkElement, string[]>(card, new[] { type, name, blurb, g[0] }));
                    }
                v.Children.Add(holder);
                var none = Txt("Nothing matches.", "vsDim", 12.5, false);
                none.Visibility = Visibility.Collapsed;
                none.Tag = "none";
                v.Children.Add(none);
                ApplyFilter();
            }

            Border Chip(string cat)
            {
                var tb = new TextBlock { Text = cat, FontSize = 12 };
                var b = new Border { Child = tb, Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 6, 4), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, Tag = cat };
                Action paint = delegate
                {
                    bool on = _category == cat;
                    if (on) { Ref(b, Border.BackgroundProperty, "vsSelBg"); Ref(tb, TextBlock.ForegroundProperty, "vsFg"); tb.FontWeight = FontWeights.SemiBold; }
                    else { Ref(b, Border.BackgroundProperty, "vsCtrl"); Ref(tb, TextBlock.ForegroundProperty, "vsDim"); tb.FontWeight = FontWeights.Normal; }
                };
                paint();
                b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _category = cat;
                    var row = b.Parent as Panel;
                    if (row != null)
                        foreach (UIElement c in row.Children)
                        {
                            var cb = c as Border;
                            if (cb == null) continue;
                            var t = cb.Child as TextBlock;
                            bool on = (string)cb.Tag == _category;
                            if (on) { Ref(cb, Border.BackgroundProperty, "vsSelBg"); if (t != null) { Ref(t, TextBlock.ForegroundProperty, "vsFg"); t.FontWeight = FontWeights.SemiBold; } }
                            else { Ref(cb, Border.BackgroundProperty, "vsCtrl"); if (t != null) { Ref(t, TextBlock.ForegroundProperty, "vsDim"); t.FontWeight = FontWeights.Normal; } }
                        }
                    ApplyFilter();
                };
                return b;
            }

            /// <summary>A small joined switch (the zone to add to) - only this pane's choice, nothing saved.</summary>
            static UIElement Switch(string[] items, Func<string> get, Action<string> set)
            {
                var outer = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true };
                Ref(outer, Border.BorderBrushProperty, "vsCtrlBorder");
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                outer.Child = row;
                var parts = new List<Border>();
                Action paint = delegate
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        bool on = items[i] == get();
                        var tb = (TextBlock)parts[i].Child;
                        if (on) { Ref(parts[i], Border.BackgroundProperty, "vsAccent"); Ref(tb, TextBlock.ForegroundProperty, "vsAccentText"); tb.FontWeight = FontWeights.SemiBold; }
                        else { parts[i].Background = Brushes.Transparent; Ref(tb, TextBlock.ForegroundProperty, "vsFg"); tb.FontWeight = FontWeights.Normal; }
                    }
                };
                foreach (string it in items)
                {
                    string val = it;
                    var part = new Border { Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand, Child = new TextBlock { Text = it, FontSize = 12.5 }, CornerRadius = new CornerRadius(5) };
                    part.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; set(val); paint(); };
                    parts.Add(part);
                    row.Children.Add(part);
                }
                paint();
                return outer;
            }

            void ApplyFilter()
            {
                string q = _search != null ? (_search.Text ?? "").Trim() : "";
                int shown = 0;
                foreach (var kv in _cards)
                {
                    string[] d = kv.Value;
                    bool cat = _category == "All" || d[3] == _category;
                    bool match = q.Length == 0 || d[1].IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || d[2].IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    bool on = cat && match;
                    kv.Key.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    if (on) shown++;
                }
                foreach (UIElement e in _galleryView.Children)
                {
                    var tb = e as TextBlock;
                    if (tb != null && (tb.Tag as string) == "none") tb.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                // the widget grid keeps its columns even when cards hide: rebuild its column count from what shows
                foreach (UIElement e in _galleryView.Children)
                {
                    var ug = e as UniformGrid;
                    if (ug != null) ug.Rows = 0;
                }
            }

            /// <summary>Add a module of this type: after the module that was open (or at the end), into the chosen zone; then open its settings.</summary>
            public void AddModule(string type)
            {
                var m = ModuleConfig.Create(type);
                if (ZonesOn) m.Zone = _addZone;
                var list = _source();
                int at = list.Count;
                if (_addAfter != null && list.Contains(_addAfter) && (!ZonesOn || ZoneIndex(_addAfter) == ZoneIndex(m))) at = list.IndexOf(_addAfter) + 1;
                list.Insert(Math.Max(0, Math.Min(list.Count, at)), m);
                _addAfter = null;
                // a module that fills whatever room it gets (Protect's camera grid): a widget that sizes itself starts at the plugin's size
                PluginModule pm = PluginManager.ModuleOf(type);
                if (IsWidget && pm != null && pm.WidgetSize.Width > 0)
                {
                    WidgetSettings w = Widget != null ? Widget() : null;
                    if (w != null && w.Width == 0 && w.Height == 0) { w.Width = (int)pm.WidgetSize.Width; w.Height = (int)pm.WidgetSize.Height; }
                }
                _w._mgr.SettingsChanged();
                Refresh(m);
            }

            /// <summary>Whether a module needs a connection that isn't set up (the note under its name), or null.</summary>
            string Needs(string type)
            {
                BarSettings s = _w._mgr.Settings;
                switch (type)
                {
                    case ModuleTypes.Hwinfo: return HwInfo.Available ? null : (HwInfo.IsLhm ? "Needs LibreHardwareMonitor running with its web server on" : "Needs HWiNFO (or LibreHardwareMonitor) running with sensors shared");
                    case ModuleTypes.Weather: return Existing(type) != null ? null : "Pick a place after adding it";
                    case ModuleTypes.Terminal: return VTerm.Exe != null ? null : "Needs vTerm (Settings > Updates > vSuite apps)";
                    default:
                        {
                            PluginModule pm = PluginManager.ModuleOf(type);
                            return pm != null && pm.Needs != null ? pm.Needs(s) : null;
                        }
                }
            }

            /// <summary>The first module of this type the user already has (its settings make the preview look like theirs), or null.</summary>
            ModuleConfig Existing(string type)
            {
                BarSettings s = _w._mgr.Settings;
                foreach (BarPlacement b in s.Bars) if (b.Modules != null) foreach (ModuleConfig m in b.Modules) if (m != null && m.Type == type) return m;
                foreach (WidgetSettings w in s.Widgets) if (w.Modules != null) foreach (ModuleConfig m in w.Modules) if (m != null && m.Type == type) return m;
                return null;
            }

            // =================================================================
            // Previews: the real modules, in a stand-in for the bar / widget
            // =================================================================
            PreviewHost MakeHost()
            {
                BarSettings s = _w._mgr.Settings;
                if (IsWidget)
                {
                    WidgetSettings w = Widget != null ? Widget() : null;
                    double fs = w != null ? w.FontSize : 12, cr = w != null ? w.CornerRadius : 13;
                    Theme t = Theme.From(s, fs, cr);
                    if (w != null && !string.IsNullOrWhiteSpace(w.AccentColor)) t = t.WithAccent(t.Resolve(w.AccentColor, t.MainAccent));
                    return new PreviewHost(t, w != null ? Math.Max(16, w.ModuleHeight) : 40, true, _w);
                }
                BarPlacement b = Bar != null ? Bar() : null;
                Theme bt = b != null ? Theme.From(s, b) : Theme.From(s, 12, 13);
                return new PreviewHost(bt, b != null ? b.BarHeight : 40, false, _w);
            }

            /// <summary>Bars: a tile - a picture of the module on a piece of this bar, its name and description under it.</summary>
            FrameworkElement BarTile(PreviewHost host, string type, string name, string blurb)
            {
                Theme t = host.Theme;
                var fit = new Viewbox
                {
                    Child = ModulePics.Bar(type, t), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = 34,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var strip = new Border
                {
                    Height = 58, CornerRadius = new CornerRadius(8, 8, 0, 0), Background = t.MakeBg(-1), Padding = new Thickness(10, 0, 10, 0),
                    ClipToBounds = true, Child = fit
                };
                var col = new StackPanel();
                col.Children.Add(strip);
                var info = new StackPanel { Margin = new Thickness(10, 7, 8, 8) };
                info.Children.Add(Txt(name, "vsFg", 12.5, false));
                if (blurb.Length > 0) info.Children.Add(Txt(blurb, "vsDim", 11, false));
                string need = Needs(type);
                if (need != null) { var n = Txt(need, "vsDim", 10.5, false); n.FontStyle = FontStyles.Italic; n.TextWrapping = TextWrapping.Wrap; info.Children.Add(n); }
                col.Children.Add(info);
                var tile = new Border { Child = col, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Add " + name };
                Ref(tile, Border.BackgroundProperty, "vsCtrl");
                Ref(tile, Border.BorderBrushProperty, "vsLine");
                tile.MouseEnter += delegate { Ref(tile, Border.BorderBrushProperty, "vsAccent"); };
                tile.MouseLeave += delegate { Ref(tile, Border.BorderBrushProperty, "vsLine"); };
                tile.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; AddModule(type); };
                return tile;
            }

            /// <summary>Widgets: a card - the module in a small widget, name and description under it.</summary>
            FrameworkElement WidgetCard(PreviewHost host, string type, string name, string blurb)
            {
                Theme t = host.Theme;
                FrameworkElement live = ModulePics.Widget(type, t);
                var fit = new Viewbox
                {
                    Child = live, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 196, MaxHeight = 138,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
                };
                WidgetStyle ws = _w._mgr.Settings.WidgetStyle;
                var box = new Border
                {
                    Height = 156, CornerRadius = new CornerRadius(Math.Min(16, Math.Max(4, t.Corner.TopLeft))), Background = t.MakeBg(-1),
                    BorderBrush = Theme.Faded(ws != null ? t.Resolve(ws.BorderColor, t.MainAccent) : t.MainAccent, 55), BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(9), ClipToBounds = true, Child = fit
                };
                var col = new StackPanel();
                col.Children.Add(box);
                var info = new StackPanel { Margin = new Thickness(2, 8, 2, 0) };
                info.Children.Add(Txt(name, "vsFg", 13, false));
                if (blurb.Length > 0) info.Children.Add(Txt(blurb, "vsDim", 11, false));
                string need = Needs(type);
                if (need != null) { var n = Txt(need, "vsDim", 10.5, false); n.FontStyle = FontStyles.Italic; n.TextWrapping = TextWrapping.Wrap; info.Children.Add(n); }
                col.Children.Add(info);

                var card = new Border { Child = col, Padding = new Thickness(10), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Add " + name };
                Ref(card, Border.BackgroundProperty, "vsCtrl");
                Ref(card, Border.BorderBrushProperty, "vsLine");
                card.MouseEnter += delegate { Ref(card, Border.BorderBrushProperty, "vsAccent"); };
                card.MouseLeave += delegate { Ref(card, Border.BorderBrushProperty, "vsLine"); };
                card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; AddModule(type); };
                return card;
            }
        }

        /// <summary>What a module preview sits in: the bar's / widget's look and size, without a window of its own.</summary>
        sealed class PreviewHost : IModuleHost
        {
            readonly Window _owner;
            public PreviewHost(Theme theme, double height, bool widget, Window owner)
            {
                Theme = theme; HostHeight = height; IsWidget = widget; _owner = owner;
            }
            public Theme Theme { get; private set; }
            public double HostHeight { get; private set; }
            public bool PopupsBelow { get { return false; } }
            public bool FullWidth { get { return false; } }
            public bool IsWidget { get; private set; }
            public IntPtr Handle { get { try { return new System.Windows.Interop.WindowInteropHelper(_owner).Handle; } catch { return IntPtr.Zero; } } }
            public IntPtr MonitorHandle { get { return Native.MonitorFromWindow(Handle, Native.MONITOR_DEFAULTTONEAREST); } }
            public void ContentChanged() { }
            public void OpenSettings() { }
        }
    }
}
