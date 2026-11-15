using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Application launcher: individual shortcuts and groups (a group opens a themed popup menu of shortcuts).</summary>
    public sealed class LauncherModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        /// <summary>One row (a StackPanel), or in a widget a grid of so many columns or rows (a UniformGrid).</summary>
        readonly Panel _panel;
        readonly bool _grid;
        PopupWindow _popup;
        LauncherEntry _popupFor;

        /// <summary>In a widget the launcher sizes itself: a grid to its rows, one row to its icons (big icons make the widget taller, not smaller icons).</summary>
        public int RowCount { get { return _bar != null && _bar.IsWidget ? 0 : 1; } }

        public LauncherModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _grid = bar.IsWidget && cfg.LaunchLayout != "Row";
            if (_grid)
            {
                var g = new UniformGrid { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch };
                if (cfg.LaunchLayout == "Rows") g.Rows = cfg.LaunchCount; else g.Columns = cfg.LaunchCount;
                _panel = g;
            }
            else _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Child = _panel;
            Margin = new Thickness(2, 0, 2, 0);
            Background = Brushes.Transparent;
            AllowDrop = true;
            DragEventHandler over = delegate (object s, DragEventArgs e)
            {
                string tok = IconDrag.TokenOf(e);
                bool mine = tok != null && tok.StartsWith(_cfg.Id + "|");
                bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
                if (!mine && !files) return;
                e.Effects = mine ? DragDropEffects.Move : DragDropEffects.Copy;
                e.Handled = true;
                if (_cfg.Entries.Count > 0) IconDrag.ShowMark(_panel, IconDrag.IndexAt(_panel, e, null), null, _bar.Theme.MainAccent);
            };
            DragEnter += over;
            DragOver += over;
            DragLeave += delegate { IconDrag.HideMark(_panel); };
            Drop += OnDrop;
            Rebuild();
        }

        // ---- launching ------------------------------------------------------------
        public static void Run(LauncherEntry e)
        {
            try
            {
                string p = Environment.ExpandEnvironmentVariables((e.Path ?? "").Trim().Trim('"'));
                if (p.Length == 0) return;
                var psi = new ProcessStartInfo(p) { UseShellExecute = true };
                if (!string.IsNullOrWhiteSpace(e.Args)) psi.Arguments = e.Args;
                try
                {
                    if (File.Exists(p))
                    {
                        string dir = System.IO.Path.GetDirectoryName(p);
                        if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
                    }
                }
                catch { }
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("Launch '" + e.Name + "': " + ex.Message); }
        }

        // ---- building ---------------------------------------------------------------
        public void Rebuild()
        {
            _panel.Children.Clear();

            if (_cfg.Entries.Count == 0)
            {
                var add = MakeButton(null, "Launcher is empty - drop files here or add shortcuts in Settings", IconUtil.GlyphAdd, null, _cfg.IconSize, false);
                add.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; _bar.OpenSettings(); };
                _panel.Children.Add(add);
                return;
            }

            // icon animations: icons only, on a bar or in a widget
            BarWindow bw = _bar as BarWindow;
            WidgetWindow ww = _bar as WidgetWindow;
            DockFx fx = !DockFx.IconsOnly(_cfg) ? null : bw != null ? bw.Fx : ww != null ? ww.Fx : null;
            Color glow = _bar.Theme.MainAccent.Color;
            foreach (var entry in _cfg.Entries)
            {
                LauncherEntry en = entry;
                string tip = en.IsGroup ? en.Name + " (" + en.Children.Count + ")" : (string.IsNullOrEmpty(en.Name) ? en.Path : en.Name);
                ImageSource img = EntryIcon(en);
                string glyph = en.IsGroup ? (IconUtil.GroupGlyph(en.IconGlyph) ?? IconUtil.GlyphFolder) : EntryGlyph(en);
                Border b = MakeButton(en, tip, glyph, img, _cfg.IconSize, en.IsGroup ? _cfg.ShowGroupLabels : _cfg.ShowLabels);
                if (en.IsGroup) b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (IconDrag.JustDragged) return; ShowGroup(b, en); };
                else b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (IconDrag.JustDragged) return; if (fx != null) fx.Bounce(b); Run(en); };
                // drag a shortcut / group along the launcher to move it
                IconDrag.MakeDraggable(b, delegate { return _cfg.Id + "|" + _cfg.Entries.IndexOf(en); }, null,
                                       delegate { if (_popup != null && _popup.IsOpen) _popup.CloseSafe(); });
                if (fx != null)
                {
                    var row = b.Child as Panel;
                    fx.Add(b, row != null && row.Children.Count > 0 ? row.Children[0] as FrameworkElement : null, _cfg, glow);
                }
                ReorderFx.SetKey(b, (en.IsGroup ? "group:" : "") + en.Name + "|" + en.Path);
                _panel.Children.Add(b);
            }
            ReorderFx.PlaySoon(_cfg.Id, _panel);
        }

        /// <summary>The custom icon if one is set (and readable), otherwise the shortcut's own shell icon; groups fall back to the folder glyph.</summary>
        internal static ImageSource EntryIcon(LauncherEntry en)
        {
            ImageSource custom = IconUtil.FromIconFile(en.IconPath);
            if (custom != null) return custom;
            return en.IsGroup ? null : IconUtil.FromPath(en.Path);
        }

        /// <summary>The glyph for a shortcut without an icon: a globe for web addresses (bookmarks), a page otherwise.</summary>
        internal static string EntryGlyph(LauncherEntry en)
        {
            string p = (en.Path ?? "").Trim();
            bool web = p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            return web ? IconUtil.GlyphGlobe : IconUtil.GlyphFile;
        }

        Border MakeButton(LauncherEntry entry, string tooltip, string glyph, ImageSource image, double size, bool label)
        {
            var t = _bar.Theme.ForModule(_cfg);
            var b = new Border
            {
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(1, 3, 1, 3),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += delegate { if (_cfg.HoverBox) b.Background = t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // in a grid every cell is the same size: icons alone sit in the middle of theirs, named ones line up on the left
            if (_grid) row.HorizontalAlignment = label && entry != null && !string.IsNullOrEmpty(entry.Name) ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            // a group's built-in icon can take the accent colour
            Brush fg = entry != null && entry.IsGroup && entry.IconAccent && IconUtil.GroupGlyph(entry.IconGlyph) != null ? t.MainAccent : t.Fg;
            // on a bar the icon shrinks to fit a slim bar; in a widget it is the size set, and the widget grows to fit it
            row.Children.Add(MakeIcon(glyph, image, _bar.IsWidget ? size : ModuleFit.Icon(_bar, size, 12), fg));
            if (label && entry != null && !string.IsNullOrEmpty(entry.Name))
            {
                row.Children.Add(new TextBlock
                {
                    Text = entry.Name,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = t.Fg
                });
            }
            b.Child = row;
            return b;
        }

        static FrameworkElement MakeIcon(string glyph, ImageSource image, double size, Brush fg)
        {
            if (image != null)
            {
                var img = new Image { Source = image, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            var g = IconUtil.Glyph(glyph, fg, Math.Max(12, size * 0.8));
            g.Width = size;
            g.Height = size;
            g.TextAlignment = TextAlignment.Center;
            return g;
        }

        // ---- group popup ------------------------------------------------------------------
        // A group inside the group opens in the same menu (drill-down): its shortcuts take the menu's place, with a Back row on top.
        readonly List<LauncherEntry> _trail = new List<LauncherEntry>();     // the groups opened inside _popupFor, outermost first
        readonly StackPanel _menu = new StackPanel { MinWidth = 180 };
        ScrollViewer _menuScroll;
        double _gap, _rowRadius;

        public const string GlyphChevronRight = "", GlyphChevronLeft = "";

        void ShowGroup(FrameworkElement anchor, LauncherEntry group)
        {
            if (_popup != null && _popup.IsOpen)
            {
                bool same = _popupFor == group;
                _popup.CloseSafe();
                if (same) return;      // clicking the group again just closes its menu
            }
            _popupFor = group;
            _trail.Clear();
            var t = _bar.Theme;

            // the menu looks like a widget of the current widget style (background, corners, border, shadow, padding, spacing)
            WidgetStyle ws = _mgr.Settings.WidgetStyle;
            _gap = ws != null ? Math.Max(0, ws.Spacing / 2.0) : 0;
            _rowRadius = ws != null ? Math.Max(2, Math.Min(10, ws.CornerRadius / 2)) : 4;
            var holder = _menu.Parent as ScrollViewer;     // still in the last (closed) menu: take it out
            if (holder != null) holder.Content = null;

            // a long group (a big bookmarks folder) scrolls instead of running off the screen
            var scroll = new ScrollViewer
            {
                Content = _menu, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
                MaxWidth = 350     // long names (bookmark titles) end in "…" instead of widening the menu
            };
            try { scroll.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(WidgetWindow.ScrollXaml)); }
            catch (Exception ex) { Log.Write("Launcher scroll style: " + ex.Message); }
            scroll.Resources["wThumb"] = Theme.Faded(t.FgDim, 70);
            _menuScroll = scroll;
            FillMenu();
            _popup = new PopupWindow(_mgr, _bar, anchor, t, scroll);
            if (ws != null) _popup.UseWidgetStyle(t, ws);
            scroll.MaxHeight = _popup.ContentRoom();
            // Backspace / Left goes back out of a group opened inside the menu
            _popup.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if ((e.Key == Key.Back || e.Key == Key.Left) && _trail.Count > 0) { e.Handled = true; _trail.RemoveAt(_trail.Count - 1); FillMenu(); }
            };
            _mgr.ShowPopup(_popup);
        }

        /// <summary>The menu for the innermost open group: a Back row (inside another group), then its groups and shortcuts.</summary>
        void FillMenu()
        {
            var t = _bar.Theme;
            LauncherEntry group = _trail.Count > 0 ? _trail[_trail.Count - 1] : _popupFor;
            if (LauncherFolders.Sync(group)) _mgr.Settings.Save();     // a synced folder / bookmarks: pick up new / deleted shortcuts
            _menu.Children.Clear();

            if (_trail.Count > 0)
            {
                var back = MenuRow(IconUtil.Glyph(GlyphChevronLeft, t.FgDim, 12), string.IsNullOrEmpty(group.Name) ? "Back" : group.Name, "Back", false, delegate
                {
                    _trail.RemoveAt(_trail.Count - 1);
                    FillMenu();
                });
                var name = ((DockPanel)back.Child).Children[1] as TextBlock;
                if (name != null) name.FontWeight = FontWeights.SemiBold;
                _menu.Children.Add(back);
                _menu.Children.Add(new Border { Height = 1, Background = t.Track, Margin = new Thickness(6, 3 + _gap, 6, 3) });
            }
            if (group.Children.Count == 0)
                _menu.Children.Add(new TextBlock { Text = "Empty group", Foreground = t.FgDim, Margin = new Thickness(10, 6, 10, 6) });

            foreach (var child in group.Children)
            {
                LauncherEntry c = child;
                if (c.IsGroup)
                {
                    Brush fg = c.IconAccent && IconUtil.GroupGlyph(c.IconGlyph) != null ? t.MainAccent : t.Fg;
                    var icon = MakeIcon(IconUtil.GroupGlyph(c.IconGlyph) ?? IconUtil.GlyphFolder, EntryIcon(c), 20, fg);
                    _menu.Children.Add(MenuRow(icon, string.IsNullOrEmpty(c.Name) ? "Group" : c.Name, c.Name + " (" + c.Children.Count + ")", true, delegate
                    {
                        _trail.Add(c);
                        FillMenu();
                    }));
                }
                else
                {
                    var icon = MakeIcon(EntryGlyph(c), EntryIcon(c), 20, t.Fg);
                    string tip = string.IsNullOrEmpty(c.Name) || c.Name == c.Path ? c.Path : c.Name + "\n" + c.Path;     // the full name, when it's cut short
                    _menu.Children.Add(MenuRow(icon, string.IsNullOrEmpty(c.Name) ? c.Path : c.Name, tip, false, delegate
                    {
                        if (_popup != null) _popup.CloseSafe();
                        Run(c);
                    }));
                }
            }
            if (_menuScroll != null) _menuScroll.ScrollToTop();
        }

        /// <summary>One row of the group menu: icon, name (ends in "…" when too long), a › for a group.</summary>
        Border MenuRow(FrameworkElement icon, string text, string tip, bool chevron, Action click)
        {
            var t = _bar.Theme;
            var row = new Border
            {
                Padding = new Thickness(8, 5, chevron ? 6 : 14, 5),
                Margin = new Thickness(0, _menu.Children.Count > 0 ? _gap : 0, 0, 0),
                CornerRadius = new CornerRadius(_rowRadius),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip
            };
            row.MouseEnter += delegate { row.Background = t.Hover; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };

            var line = new DockPanel { LastChildFill = true };     // the name gets the room left, so it can end in "…"
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
            var name = new TextBlock
            {
                Text = text,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = t.Fg,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (chevron)
            {
                var arrow = IconUtil.Glyph(GlyphChevronRight, t.FgDim, 11);
                arrow.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(arrow, Dock.Right);
                line.Children.Add(arrow);
            }
            line.Children.Add(name);
            row.Child = line;
            row.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return row;
        }

        // ---- drop files to add shortcuts ---------------------------------------------------------
        void OnDrop(object sender, DragEventArgs e)
        {
            IconDrag.HideMark(_panel);
            int at = _cfg.Entries.Count > 0 ? IconDrag.IndexAt(_panel, e, null) : 0;
            string tok = IconDrag.TokenOf(e);
            if (tok != null && tok.StartsWith(_cfg.Id + "|"))
            {
                e.Handled = true;
                int from;
                if (!int.TryParse(tok.Substring(tok.IndexOf('|') + 1), out from) || from < 0 || from >= _cfg.Entries.Count) return;
                if (at > from) at--;
                at = Math.Max(0, Math.Min(_cfg.Entries.Count - 1, at));
                if (at == from) return;
                ReorderFx.Remember(_cfg.Id, _panel);
                LauncherEntry moved = _cfg.Entries[from];
                _cfg.Entries.RemoveAt(from);
                _cfg.Entries.Insert(at, moved);
                _mgr.SettingsChanged();
                return;
            }
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            e.Handled = true;
            at = Math.Max(0, Math.Min(_cfg.Entries.Count, at));
            ReorderFx.Remember(_cfg.Id, _panel);
            foreach (string f in files)
            {
                string name;
                try
                {
                    name = Directory.Exists(f) ? System.IO.Path.GetFileName(f.TrimEnd('\\', '/')) : System.IO.Path.GetFileNameWithoutExtension(f);
                }
                catch { name = f; }
                _cfg.Entries.Insert(at++, new LauncherEntry { Name = name, Path = f });
            }
            _mgr.SettingsChanged();
        }
    }
}
