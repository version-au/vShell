using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Appearance page: themes (colours + bar style + widget style), the colour presets, the bar style (a look for each bar layout) and
    /// the widget style, each as a gallery of preview cards like the colour presets.
    /// </summary>
    public sealed partial class SettingsWindow
    {
        static string _selTheme;                         // the theme picked last (several can match the same settings)
        static bool _selThemeIsUser;

        readonly List<Action> _galleries = new List<Action>();
        DispatcherTimer _galleryTimer;

        // =================================================================
        // building blocks
        // =================================================================
        /// <summary>A gallery that can be redrawn on its own (when a style is edited the "in use" tick moves without rebuilding the page).</summary>
        void AddGallery(StackPanel p, Action<StackPanel> fill)
        {
            EndSection(p);
            var holder = new StackPanel();
            p.Children.Add(holder);
            Action refresh = delegate { holder.Children.Clear(); fill(holder); };
            _galleries.Add(refresh);
            refresh();
        }

        /// <summary>Redraw the galleries a moment after the last change (not on every slider step).</summary>
        void GalleriesSoon()
        {
            if (_galleryTimer == null)
            {
                _galleryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                _galleryTimer.Tick += delegate
                {
                    _galleryTimer.Stop();
                    bool o = _loading;
                    _loading = true;
                    foreach (Action a in _galleries.ToArray()) a();
                    _loading = o;
                };
            }
            _galleryTimer.Stop();
            _galleryTimer.Start();
        }

        /// <summary>Each gallery's tab (Presets / Saved) once you picked one; until then it opens where the one in use is.</summary>
        static readonly Dictionary<string, string> _galleryTab = new Dictionary<string, string>();

        /// <summary>
        /// Gallery head (Presets / Saved tabs, buttons) and the cards of the tab shown (each gallery has a page of its own). current = the
        /// card in use; one in neither list (your own look, not saved) goes first on Presets. savedEmpty = what Saved says when it's empty.
        /// </summary>
        void Gallery(StackPanel holder, string key, List<UIElement> presets, List<UIElement> saved, UIElement current, string savedEmpty,
                     params UIElement[] buttons)
        {
            string tab;
            if (!_galleryTab.TryGetValue(key, out tab)) tab = current != null && saved.Contains(current) ? "Saved" : "Presets";
            var body = new StackPanel();
            UniformGrid shown = null;
            Action show = delegate
            {
                // let go of the cards first - a card still in the old grid can't go in the new one (WPF throws)
                if (shown != null) shown.Children.Clear();
                shown = null;
                body.Children.Clear();
                if (tab == "Saved" && saved.Count == 0)
                {
                    var none = Txt(savedEmpty, "vsDim", 12, false);
                    none.TextWrapping = TextWrapping.Wrap;
                    none.Margin = new Thickness(2, 2, 0, 12);
                    body.Children.Add(none);
                    return;
                }
                var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 0) };
                if (tab == "Presets" && current != null && !presets.Contains(current) && !saved.Contains(current)) grid.Children.Add(current);
                foreach (UIElement c in tab == "Saved" ? saved : presets) grid.Children.Add(c);
                if (grid.Children.Count > 0) body.Children.Add(grid);
                shown = grid;
            };

            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (UIElement b in buttons)
            {
                var fe = b as FrameworkElement;
                if (fe != null) fe.Margin = new Thickness(6, 0, 0, 0);
                right.Children.Add(b);
            }
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            var tabs = Segmented(new[] { "Presets", "Saved" }, delegate { return tab; },
                                 delegate (string v) { tab = v; _galleryTab[key] = v; show(); }, null, false);
            var tabsFe = (FrameworkElement)tabs;
            tabsFe.HorizontalAlignment = HorizontalAlignment.Left;
            tabsFe.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(tabs);
            holder.Children.Add(head);
            holder.Children.Add(body);
            show();
        }

        /// <summary>The frame of a gallery card: a picture, the name, a tick when it is in use.</summary>
        Border StyleCard(string name, UIElement pic, bool sel, bool user, string tip, Action click)
        {
            var nameRow = new DockPanel { Margin = new Thickness(10, 7, 10, 8) };
            if (sel)
            {
                var tick = Glyph("", 11);     // CheckMark
                Ref(tick, TextBlock.ForegroundProperty, "vsAccent");
                DockPanel.SetDock(tick, Dock.Right);
                nameRow.Children.Add(tick);
            }
            var nt = Txt((user ? "★ " : "") + name, "vsFg", 12.5, false);
            nt.TextTrimming = TextTrimming.CharacterEllipsis;
            nt.TextWrapping = TextWrapping.NoWrap;
            nameRow.Children.Add(nt);

            var stack = new StackPanel();
            stack.Children.Add(new Border { CornerRadius = new CornerRadius(7, 7, 0, 0), ClipToBounds = true, Child = pic });
            stack.Children.Add(nameRow);
            var card = new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(sel ? 2 : 1), Margin = new Thickness(0, 0, 10, 10),
                Child = stack, Cursor = Cursors.Hand, ClipToBounds = true, ToolTip = tip
            };
            Ref(card, Border.BackgroundProperty, "vsCard");
            Ref(card, Border.BorderBrushProperty, sel ? "vsAccent" : "vsLine");
            card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (click != null) click();
            };
            return card;
        }

        // ---- the little pictures ------------------------------------------------------------------------------
        /// <summary>The colours a little picture is drawn in (the welcome guide draws the theme cards too).</summary>
        internal struct Pal { public Color Bg, Bg2, Fg, Ac, Sp; }

        static Color Rgb(Color c) { return Color.FromRgb(c.R, c.G, c.B); }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        internal static Pal PalOf(string bg, string fg, string ac, string sp, string bg2)
        {
            Color b = Theme.Parse(bg, Colors.Black);
            return new Pal
            {
                Bg = b, Fg = Theme.Parse(fg, Colors.White), Ac = Theme.Parse(ac, Colors.SteelBlue),
                Sp = Theme.Parse(sp, Colors.Pink), Bg2 = Theme.Parse(bg2, b)
            };
        }

        /// <summary>The colours in use now (the wallpaper's when Wallpaper Theme is on).</summary>
        Pal CurrentPal() { return CurrentPal(S); }

        internal static Pal CurrentPal(BarSettings S)
        {
            Pal c = PalOf(S.Background, S.Foreground, S.MainAccent, S.SecondaryAccent, S.BgStyle == "Gradient" ? S.Background2 : S.Background);
            WallpaperPalette w;
            if (S.WallpaperTheme && WallpaperColor.TryGetPalette(S, out w))
            {
                c.Bg = w.Background; c.Bg2 = S.BgStyle == "Gradient" ? w.Background2 : w.Background;
                c.Fg = w.Foreground; c.Ac = w.MainAccent; c.Sp = w.SecondaryAccent;
            }
            return c;
        }

        Pal PalOfTheme(LookTheme t) { return PalOfTheme(S, t); }

        internal static Pal PalOfTheme(BarSettings S, LookTheme t)
        {
            if (t.Colours != null)
                return PalOf(t.Colours.Background, t.Colours.Foreground, t.Colours.MainAccent, t.Colours.SecondaryAccent,
                             t.Colours.BgStyle == "Gradient" ? t.Colours.Background2 : t.Colours.Background);
            string[] v;
            if (!string.IsNullOrEmpty(t.ColourPreset) && BarSettings.Presets.TryGetValue(t.ColourPreset, out v))
                return PalOf(v[0], v[1], v[2], v.Length > 3 ? v[3] : S.SecondaryAccent, v.Length > 4 ? v[4] : v[0]);
            return CurrentPal(S);
        }

        /// <summary>
        /// Put a theme into the settings: its colours (a preset or saved colours), bar style and widget style - whatever it holds. The
        /// caller applies the settings (BarManager.SettingsChanged). Used by the Appearance page and the welcome guide.
        /// </summary>
        internal static void ApplyTheme(BarSettings S, LookTheme t)
        {
            if (t.Colours != null) ApplyThemeFields(S, t.Colours);
            else
            {
                string[] p;
                if (!string.IsNullOrEmpty(t.ColourPreset) && BarSettings.Presets.TryGetValue(t.ColourPreset, out p))
                {
                    S.WallpaperTheme = false;
                    S.Background = p[0]; S.Foreground = p[1]; S.MainAccent = p[2];
                    if (p.Length > 3) S.SecondaryAccent = p[3];
                    if (p.Length > 4) S.Background2 = p[4];
                }
            }
            if (t.Bars != null)
            {
                S.BarStyle = Styles.Clone(t.Bars);
                Styles.Fix(S.BarStyle);
                S.BarStyleName = S.BarStyle.Name;
                Styles.ApplyBars(S);
            }
            if (t.Widgets != null)
            {
                S.WidgetStyle = Styles.Clone(t.Widgets);
                S.WidgetStyleName = S.WidgetStyle.Name ?? "";
                Styles.ApplyWidgets(S);
            }
        }

        /// <summary>The theme's colours, bar style and widget style are the ones in use (what it leaves out doesn't count).</summary>
        internal static bool ThemeInUse(BarSettings S, LookTheme t)
        {
            if (t.Colours != null)
            {
                if (!IsCurrentTheme(S, t.Colours.Background, t.Colours.Foreground, t.Colours.MainAccent)) return false;
            }
            else if (!string.IsNullOrEmpty(t.ColourPreset))
            {
                string[] v;
                if (!BarSettings.Presets.TryGetValue(t.ColourPreset, out v) || !IsCurrentTheme(S, v[0], v[1], v[2])) return false;
            }
            if (t.Bars != null && !Styles.Same(S.BarStyle, t.Bars)) return false;
            if (t.Widgets != null && !Styles.Same(S.WidgetStyle, t.Widgets)) return false;
            return true;
        }

        /// <summary>An outline colour setting in a picture: a theme slot, a hex colour, or empty = Main Accent.</summary>
        static Color SlotColour(string spec, Pal c)
        {
            string k = ThemeSlots.Migrate(spec ?? "").Trim().ToLowerInvariant();
            if (k.Length == 0 || k == ThemeSlots.Main) return Rgb(c.Ac);
            if (k == ThemeSlots.Secondary) return Rgb(c.Sp);
            if (k == ThemeSlots.Text) return Rgb(c.Fg);
            return Rgb(Theme.Parse(spec, c.Ac));
        }

        /// <summary>A desktop behind the bars / widgets in a picture.</summary>
        static Brush Desk(Pal c)
        {
            return new LinearGradientBrush(Mix(Rgb(c.Bg), Colors.Black, 0.35), Mix(Rgb(c.Ac), Colors.Black, 0.72), 35);
        }

        /// <summary>A bar drawn small: k = scale, atTop = against the top edge of the picture, narrow = width when it is not full width.</summary>
        static FrameworkElement MiniBar(BarLook l, string layout, Pal c, double k, bool atTop, double narrow)
        {
            if (l == null) l = new BarLook();
            bool full = layout == Styles.Classic || layout == Styles.Minimal;
            double h = Math.Max(6, l.Height * k);
            double r = Math.Min(h / 2, l.CornerRadius * k);
            CornerRadius cr = l.RoundAllCorners ? new CornerRadius(r) : atTop ? new CornerRadius(0, 0, r, r) : new CornerRadius(r, r, 0, 0);
            double m = layout == Styles.Dock ? 0 : layout == Styles.Floating ? Math.Max(2, l.EdgeMargin * k) : Math.Max(0, l.EdgeMargin * k);
            bool border = layout != Styles.Minimal && l.BorderThickness > 0;
            double th = border ? Math.Max(1, Math.Round(l.BorderThickness * k)) : 0;
            Color ac = Rgb(c.Ac), fg = Rgb(c.Fg), oc = SlotColour(l.BorderColor, c);
            Color bc = Color.FromArgb((byte)(255 * Math.Max(0, Math.Min(100, l.BorderOpacity)) / 100), oc.R, oc.G, oc.B);

            double d = Math.Max(3, Math.Round(h * 0.28));
            var content = new DockPanel { Margin = new Thickness(Math.Max(3, h * 0.35), 0, Math.Max(3, h * 0.35), 0), LastChildFill = false };
            var clock = new Border { Width = d * 2, Height = d, CornerRadius = new CornerRadius(d / 2), Background = new SolidColorBrush(Color.FromArgb(0xB0, fg.R, fg.G, fg.B)), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(clock, Dock.Right);
            content.Children.Add(clock);
            content.Children.Add(new Border { Width = d * 2.4, Height = d, CornerRadius = new CornerRadius(d / 2), Background = new SolidColorBrush(ac), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new Border { Width = d * 1.6, Height = d, CornerRadius = new CornerRadius(d / 2), Margin = new Thickness(d * 0.6, 0, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0x55, fg.R, fg.G, fg.B)), VerticalAlignment = VerticalAlignment.Center });

            var bar = new Border
            {
                Height = h, CornerRadius = cr, BorderThickness = new Thickness(th), BorderBrush = new SolidColorBrush(bc),
                Background = new LinearGradientBrush(Rgb(c.Bg), Rgb(c.Bg2), 0), Child = WithGlass(content, l.GlassEdge, cr),
                VerticalAlignment = atTop ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = full ? (atTop ? new Thickness(m, m, m, 0) : new Thickness(m, 0, m, m)) : (atTop ? new Thickness(0, m, 0, 0) : new Thickness(0, 0, 0, m))
            };
            if (!full) { bar.HorizontalAlignment = HorizontalAlignment.Center; bar.Width = narrow; }
            return bar;
        }

        /// <summary>The style's glass edge over a small picture's contents (none = the contents as they are).</summary>
        static UIElement WithGlass(UIElement content, string glass, CornerRadius cr)
        {
            if (glass != "Subtle" && glass != "Strong") return content;
            var g = new Grid();
            var sheen = new Border();
            EdgeFx.GlassEdge(sheen, glass, cr);
            g.Children.Add(sheen);
            g.Children.Add(content);
            return g;
        }

        /// <summary>A widget drawn small: a few rows inside its box.</summary>
        static FrameworkElement MiniWidget(WidgetStyle w, Pal c, double k, double width, int rows)
        {
            if (w == null) w = new WidgetStyle();
            Color ac = Rgb(c.Ac), fg = Rgb(c.Fg), oc = SlotColour(w.BorderColor, c);
            double pad = Math.Max(2, 8 * k);     // Build 2: widget padding is fixed (density sets it), not the style's
            var body = new StackPanel { Margin = new Thickness(pad) };
            for (int i = 0; i < rows; i++)
                body.Children.Add(new Border
                {
                    Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                    Width = (width - 2 * pad) * (i == 0 ? 0.55 : i % 2 == 1 ? 0.85 : 0.7),
                    Margin = new Thickness(0, i == 0 ? 0 : Math.Max(1.5, w.Spacing * k), 0, 0),
                    Background = new SolidColorBrush(i == 0 ? ac : Color.FromArgb(0x70, fg.R, fg.G, fg.B))
                });
            var b = new Border
            {
                Width = width, Child = WithGlass(body, w.GlassEdge, new CornerRadius(Math.Min(14, w.CornerRadius * k))), Background = new LinearGradientBrush(Rgb(c.Bg), Rgb(c.Bg2), 0),
                CornerRadius = new CornerRadius(Math.Min(14, w.CornerRadius * k)),
                BorderThickness = new Thickness(w.ShowBorder ? Math.Max(1, Math.Round(w.BorderThickness * k)) : 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Max(0, Math.Min(100, w.BorderOpacity)) / 100), oc.R, oc.G, oc.B))
            };
            if (w.ShowShadow) b.Effect = new DropShadowEffect { BlurRadius = 7, ShadowDepth = 1.5, Opacity = 0.55, Color = Colors.Black };
            return b;
        }

        /// <summary>A theme's picture: a floating bar at the top, a classic bar at the bottom and a widget, in its colours.</summary>
        internal static UIElement ThemePic(Pal c, BarStyle bars, WidgetStyle widgets)
        {
            var g = new Grid { Height = 84, Background = Desk(c), ClipToBounds = true };
            g.Children.Add(MiniBar(Styles.LookFor(bars, Styles.Floating), Styles.Floating, c, 0.3, true, 72));
            g.Children.Add(MiniBar(Styles.LookFor(bars, Styles.Classic), Styles.Classic, c, 0.3, false, 0));
            var w = MiniWidget(widgets, c, 0.45, 46, 3);
            w.HorizontalAlignment = HorizontalAlignment.Left;
            w.VerticalAlignment = VerticalAlignment.Center;
            w.Margin = new Thickness(12, 0, 0, 0);
            g.Children.Add(w);
            return g;
        }

        /// <summary>A bar style's picture: its four layouts, each against the bottom of a little screen.</summary>
        static UIElement BarStylePic(Pal c, BarStyle st)
        {
            var grid = new UniformGrid { Columns = 2, Rows = 2, Height = 84, Background = new SolidColorBrush(Mix(Rgb(c.Bg), Colors.Black, 0.6)) };
            foreach (string lay in Styles.Layouts)
            {
                var cell = new Grid { Background = Desk(c), Margin = new Thickness(0.5), ClipToBounds = true };
                var label = new TextBlock { Text = lay, FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, c.Fg.R, c.Fg.G, c.Fg.B)), Margin = new Thickness(5, 3, 0, 0) };
                cell.Children.Add(label);
                cell.Children.Add(MiniBar(Styles.LookFor(st, lay), lay, c, 0.27, false, 40));
                grid.Children.Add(cell);
            }
            return grid;
        }

        /// <summary>A widget style's picture: two widgets.</summary>
        static UIElement WidgetStylePic(Pal c, WidgetStyle w)
        {
            var g = new Grid { Height = 84, Background = Desk(c), ClipToBounds = true };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var a = MiniWidget(w, c, 0.5, 58, 4);
            var b = MiniWidget(w, c, 0.5, 42, 2);
            a.VerticalAlignment = VerticalAlignment.Center;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(a);
            row.Children.Add(b);
            g.Children.Add(row);
            return g;
        }

        // =================================================================
        // Themes
        // =================================================================
        LookTheme FindUserTheme(string name)
        {
            foreach (LookTheme t in S.UserThemes)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>Everything the theme holds matches the current settings (what it leaves out doesn't matter).</summary>
        bool ThemeInUse(LookTheme t) { return ThemeInUse(S, t); }

        /// <summary>The theme shown as in use (the one picked last when several match), and whether it is one of yours.</summary>
        LookTheme ThemeInUse(out bool user)
        {
            user = false;
            if (_selTheme != null)
            {
                LookTheme cand = _selThemeIsUser ? FindUserTheme(_selTheme) : Styles.FindBuiltInTheme(_selTheme);
                if (cand != null && ThemeInUse(cand)) { user = _selThemeIsUser; return cand; }
            }
            foreach (LookTheme t in Styles.BuiltInThemes) if (ThemeInUse(t)) return t;
            foreach (LookTheme t in S.UserThemes) if (ThemeInUse(t)) { user = true; return t; }
            return null;
        }

        static string ThemeTip(LookTheme t)
        {
            var parts = new List<string>();
            parts.Add("Colours: " + (t.Colours != null ? "saved colours" : !string.IsNullOrEmpty(t.ColourPreset) ? t.ColourPreset : "left as they are"));
            parts.Add("Bar style: " + (t.Bars == null ? "left as it is" : string.IsNullOrEmpty(t.Bars.Name) ? "saved" : t.Bars.Name));
            parts.Add("Widget style: " + (t.Widgets == null ? "left as it is" : string.IsNullOrEmpty(t.Widgets.Name) ? "saved" : t.Widgets.Name));
            return string.Join("\n", parts);
        }

        void ThemeGallery(StackPanel holder)
        {
            bool inUseUser;
            LookTheme inUse = ThemeInUse(out inUseUser);
            var cards = new List<UIElement>();
            var mine = new List<UIElement>();
            UIElement current = null;
            foreach (LookTheme t in Styles.BuiltInThemes)
            {
                LookTheme tt = t;
                bool sel = t == inUse;
                var c = StyleCard(t.Name, ThemePic(PalOfTheme(t), t.Bars ?? S.BarStyle, t.Widgets ?? S.WidgetStyle), sel, false, ThemeTip(t),
                                  delegate { ApplyLookTheme(tt, false); });
                cards.Add(c);
                if (sel) current = c;
            }
            foreach (LookTheme t in S.UserThemes)
            {
                LookTheme tt = t;
                bool sel = t == inUse;
                var c = StyleCard(t.Name, ThemePic(PalOfTheme(t), t.Bars ?? S.BarStyle, t.Widgets ?? S.WidgetStyle), sel, true, "Your saved theme\n" + ThemeTip(t),
                                  delegate { ApplyLookTheme(tt, true); });
                mine.Add(c);
                if (sel) current = c;
            }
            if (current == null)
                current = StyleCard("Current look (not a theme)", ThemePic(CurrentPal(), S.BarStyle, S.WidgetStyle), true, false,
                                    "Your colours, bar style and widget style as they are now - Save as theme… keeps them", null);
            Gallery(holder, "Themes", cards, mine, current, "No saved themes yet - Save as theme… keeps your colours, bar style and widget style together.");
            var note = Txt("A theme sets the colours, the bar style and the widget style in one click. Each bar takes the look for its own layout; " +
                           "bars and widgets you gave their own look are left as they are.", "vsDim", 12, false);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(2, 0, 0, 0);
            holder.Children.Add(note);
        }

        /// <summary>
        /// A theme / style is about to be applied: bars / widgets that keep their own look are named, and you choose whether they take the
        /// new style too (they then follow it from now on). false = cancelled. Nothing is asked when they all follow the style.
        /// </summary>
        bool AskOwnLooks(bool bars, bool widgets, string title)
        {
            var names = new List<string>();
            int nb = 0, nw = 0;
            var mons = MonitorHelper.GetAll();
            if (bars)
                for (int i = 0; i < S.Bars.Count; i++)
                    if (S.Bars[i] != null && !S.Bars[i].FollowStyle) { nb++; names.Add(BarShortName(i, S.Bars[i], mons) + " (" + Styles.LayoutOf(S.Bars[i]) + ")"); }
            if (widgets)
                foreach (WidgetSettings w in S.Widgets)
                    if (w != null && !w.FollowStyle) { nw++; names.Add("Widget: " + w.Name); }
            if (nb + nw == 0) return true;
            string who = (nb > 0 ? nb + (nb == 1 ? " bar" : " bars") : "") + (nb > 0 && nw > 0 ? " and " : "") + (nw > 0 ? nw + (nw == 1 ? " widget" : " widgets") : "");
            var list = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count && i < 10; i++) list.Append("   •  ").Append(names[i]).Append('\n');
            if (names.Count > 10) list.Append("   …and ").Append(names.Count - 10).Append(" more\n");
            MessageBoxResult r = MessageBox.Show(this,
                who + (nb + nw == 1 ? " has its own look" : " have their own look") + " (not following the style):\n\n" + list +
                "\nApply the new style to " + (nb + nw == 1 ? "it" : "them") + " too?\n\nYes = " + (nb + nw == 1 ? "it follows" : "they follow") +
                " the style from now on\nNo = keep " + (nb + nw == 1 ? "its" : "their") + " own look",
                title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel || r == MessageBoxResult.None) return false;
            if (r == MessageBoxResult.Yes)
            {
                if (bars) foreach (BarPlacement b in S.Bars) if (b != null) b.FollowStyle = true;
                if (widgets) foreach (WidgetSettings w in S.Widgets) if (w != null) w.FollowStyle = true;
            }
            return true;
        }

        void ApplyLookTheme(LookTheme t, bool user)
        {
            if (!AskOwnLooks(t.Bars != null, t.Widgets != null, "Apply theme \"" + t.Name + "\"")) return;
            ApplyTheme(S, t);
            if (t.Colours != null) { _selPreset = null; _selPresetIsUser = false; }
            else if (!string.IsNullOrEmpty(t.ColourPreset) && BarSettings.Presets.ContainsKey(t.ColourPreset)) { _selPreset = t.ColourPreset; _selPresetIsUser = false; }
            _selTheme = t.Name;
            _selThemeIsUser = user;
            _mgr.SettingsChanged();
            ApplyChrome();
            BuildAppearancePage();
            RebuildNav();
            if (_selBar != null) _loadedBar = null;        // the bar / widget pages show the new values next time
        }

        string ColoursDescription()
        {
            if (S.WallpaperTheme) return "from your wallpaper (as they are now)";
            if (_selPreset != null)
            {
                if (_selPresetIsUser) { ThemePreset u = FindUserPreset(_selPreset); if (u != null && IsCurrentTheme(u.Background, u.Foreground, u.MainAccent)) return _selPreset; }
                else { string[] v; if (BarSettings.Presets.TryGetValue(_selPreset, out v) && IsCurrentTheme(v[0], v[1], v[2])) return _selPreset; }
            }
            foreach (var kv in BarSettings.Presets) if (IsCurrentTheme(kv.Value[0], kv.Value[1], kv.Value[2])) return kv.Key;
            return "your own colours";
        }

        void SaveTheme()
        {
            bool inUseUser;
            LookTheme inUse = ThemeInUse(out inUseUser);
            var ticks = new[] { true, true, true };
            // the shapes your bars and widgets have now (each bar's look goes in for its layout)
            BarStyle bars = Styles.CaptureFromBars(S, null);
            WidgetStyle widgets = Styles.CaptureFromWidgets(S);
            string name = PromptWindow.AskWithOptions(this, "Save as theme", "Name", inUse != null && inUseUser ? inUse.Name : "My theme",
                new[]
                {
                    "Colours - " + ColoursDescription() + " (with the background, blur and font)",
                    "Bar style - " + (string.IsNullOrEmpty(bars.Name) ? "your bars as they are now" : bars.Name),
                    "Widget style - " + (string.IsNullOrEmpty(widgets.Name) ? "your widgets as they are now" : widgets.Name)
                }, ticks,
                "Anything left unticked is left as it is when the theme is applied - e.g. a colours-only theme keeps your bars' shapes.");
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (!ticks[0] && !ticks[1] && !ticks[2])
            {
                MessageBox.Show(this, "Tick at least one of colours, bar style or widget style.", "Save as theme", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Styles.FindBuiltInTheme(name) != null)
            {
                MessageBox.Show(this, "\"" + name + "\" is a built-in theme - choose another name.", "Save as theme", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            LookTheme t = FindUserTheme(name);
            if (t == null) { t = new LookTheme(); S.UserThemes.Add(t); }
            _galleryTab.Remove("Themes");       // the gallery opens on Saved, where the new one is
            t.Name = name;
            t.ColourPreset = null;
            if (ticks[0]) { t.Colours = new ThemePreset { Name = name }; FillThemeFields(t.Colours); }
            else t.Colours = null;
            t.Bars = ticks[1] ? bars : null;
            t.Widgets = ticks[2] ? widgets : null;
            // the current styles become what was saved, and bars / widgets that look like it follow it again
            if (ticks[1]) { S.BarStyle = Styles.Clone(bars); S.BarStyleName = bars.Name; RefollowBars(); }
            if (ticks[2]) { S.WidgetStyle = Styles.Clone(widgets); S.WidgetStyleName = widgets.Name ?? ""; RefollowWidgets(); }
            _selTheme = name;
            _selThemeIsUser = true;
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        void DeleteSelectedTheme()
        {
            if (!_selThemeIsUser || _selTheme == null) return;
            LookTheme t = FindUserTheme(_selTheme);
            if (t == null) return;
            if (MessageBox.Show(this, "Delete the theme \"" + t.Name + "\"? (Your current colours and styles stay as they are.)", "Delete theme",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            S.UserThemes.Remove(t);
            _selTheme = null;
            _selThemeIsUser = false;
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        // =================================================================
        // Colour presets
        // =================================================================
        void ColourGallery(StackPanel holder)
        {
            var cards = new List<UIElement>();
            var mine = new List<UIElement>();
            UIElement current = null;
            foreach (var kv in BarSettings.Presets)
            {
                string[] v = kv.Value;
                var c = PresetCard(kv.Key, v[0], v[1], v[2], v.Length > 3 ? v[3] : S.SecondaryAccent, v.Length > 4 ? v[4] : v[0], false);
                cards.Add(c);
                if (current == null && IsCurrentTheme(v[0], v[1], v[2]) && (!_selPresetIsUser || _selPreset == null)) current = c;
            }
            foreach (var u in S.UserPresets)
            {
                var c = PresetCard(u.Name, u.Background, u.Foreground, u.MainAccent, u.SecondaryAccent, u.Background2, true);
                mine.Add(c);
                if (IsCurrentTheme(u.Background, u.Foreground, u.MainAccent) &&
                    (current == null || (_selPresetIsUser && string.Equals(_selPreset, u.Name, StringComparison.OrdinalIgnoreCase)))) current = c;
            }
            if (current == null && !S.WallpaperTheme)    // your own colours, not saved as a preset
                current = PresetCard("Current (not a preset)", S.Background, S.Foreground, S.MainAccent, S.SecondaryAccent, S.Background2, false, true,
                                     delegate { });

            var del = Btn("Delete", delegate { DeleteSelectedPreset(); });
            del.IsEnabled = _selPresetIsUser && _selPreset != null;
            del.ToolTip = "Delete the saved colour preset you picked last";
            Gallery(holder, "Colours", cards, mine, current, "No saved colours yet - Save colours… keeps the colours you have now.",
                    Btn("Save colours…", delegate { SaveCurrentAsPreset(); }), del);
        }

        // =================================================================
        // Bar style
        // =================================================================
        BarStyle FindUserBarStyle(string name)
        {
            foreach (BarStyle b in S.UserBarStyles) if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        WidgetStyle FindUserWidgetStyle(string name)
        {
            foreach (WidgetStyle w in S.UserWidgetStyles) if (string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        void BuildBarStyleSection(StackPanel p)
        {
            Intro(p, "The shape of your bars: height, corners, gap from the screen edge and outline - with a look for each layout " +
                     "(Classic, Floating, Dock, Minimal). Every bar takes the look for its own layout. The buttons work on the style in use: " +
                     "Duplicate saves a copy you can Edit, or shape a bar on its page and click Save Bar Style there.");
            AddGallery(p, BarStyleGallery);

            Header(p, "Your bars");
            var mons = MonitorHelper.GetAll();
            bool anyOwn = false;
            for (int i = 0; i < S.Bars.Count; i++)
            {
                BarPlacement b = S.Bars[i];
                if (!b.FollowStyle) anyOwn = true;
                var right = new StackPanel { Orientation = Orientation.Horizontal };
                var info = Txt(Styles.LayoutOf(b) + "   ·   " + (b.FollowStyle ? "follows the bar style" : "its own look"), b.FollowStyle ? "vsFg" : "vsDim", 12.5, false);
                info.VerticalAlignment = VerticalAlignment.Center;
                right.Children.Add(info);
                var open = Btn("Open", delegate { _selBar = b; LoadBar(); Navigate(PgBar); RebuildNav(); });
                open.Margin = new Thickness(12, 0, 0, 0);
                right.Children.Add(open);
                Place(p, Row(BarShortName(i, b, mons), right));
            }
            if (S.Bars.Count == 0) Note(p, "No bars yet.");
            if (anyOwn)
                ListButton(p, "Make every bar follow the bar style", delegate
                {
                    foreach (BarPlacement b in S.Bars) b.FollowStyle = true;
                    Styles.ApplyBars(S);
                    _mgr.SettingsChanged();
                    _loadedBar = null;
                    BuildAppearancePage();
                });
            Note(p, "A bar's layout comes from its own settings (Bars > the bar > Layout): Classic = full width with an outline, Minimal = full width without one, " +
                    "Floating = only as wide as its modules and away from the screen edge, Dock = only as wide as its modules and against the edge.");

        }

        void BarStyleGallery(StackPanel holder)
        {
            Pal pal = CurrentPal();
            var cards = new List<UIElement>();
            var mine = new List<UIElement>();
            UIElement current = null;
            BarStyle currentUser = null;
            foreach (BarStyle st in Styles.BuiltInBarStyles)
            {
                BarStyle s0 = st;
                bool sel = Styles.Same(S.BarStyle, st);
                var c = StyleCard(st.Name, BarStylePic(pal, st), sel, false, null, delegate { ApplyBarStyle(s0); });
                cards.Add(c);
                if (sel && current == null) current = c;
            }
            foreach (BarStyle st in S.UserBarStyles)
            {
                BarStyle s0 = st;
                bool sel = Styles.Same(S.BarStyle, st);
                var c = StyleCard(st.Name, BarStylePic(pal, st), sel, true, "Your saved bar style", delegate { ApplyBarStyle(s0); });
                mine.Add(c);
                if (sel && current == null) { current = c; currentUser = s0; }
            }
            if (current == null)
                current = StyleCard("Current (not a preset)", BarStylePic(pal, S.BarStyle), true, false, "Your own bar style - Save Bar Style on a bar's page keeps it", null);
            BarStyle u = currentUser;
            Gallery(holder, "Bar styles", cards, mine, current,
                    "No saved bar styles yet - Duplicate the one in use and Edit it, or shape a bar on its page and click Save Bar Style there.",
                    StyleButtons("bar style", u != null, delegate { EditBarStyle(u); }, delegate { RenameBarStyle(u); }, DuplicateBarStyle, delegate
                    {
                        if (MessageBox.Show(this, "Delete the bar style \"" + u.Name + "\"? (Your bars keep their look.)", "Delete bar style", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        S.UserBarStyles.Remove(u);
                        S.BarStyle.Name = "";
                        S.BarStyleName = "";
                        _mgr.SettingsChanged();
                        BuildAppearancePage();
                    }));
        }

        void ApplyBarStyle(BarStyle st)
        {
            if (!AskOwnLooks(true, false, "Apply bar style \"" + st.Name + "\"")) return;
            S.BarStyle = Styles.Clone(st);
            Styles.Fix(S.BarStyle);
            S.BarStyleName = S.BarStyle.Name;
            Styles.ApplyBars(S);
            _mgr.SettingsChanged();
            _loadedBar = null;
            BuildAppearancePage();
        }

        /// <summary>Bars whose look matches the current style's look for their layout follow it (again).</summary>
        void RefollowBars()
        {
            foreach (BarPlacement b in S.Bars)
                if (b != null && !b.FollowStyle && Styles.Same(Styles.Capture(b), Styles.LookFor(S.BarStyle, Styles.LayoutOf(b)))) b.FollowStyle = true;
        }

        void RefollowWidgets()
        {
            foreach (WidgetSettings w in S.Widgets)
                if (w != null && !w.FollowStyle && Styles.Same(Styles.CaptureWidget(w, S.WidgetStyle.Name), S.WidgetStyle)) w.FollowStyle = true;
        }

        /// <summary>
        /// "Save Bar Style" on a bar's page: this bar's look becomes the look for its layout (the other layouts come from your other bars /
        /// the current style), saved under a name and used as the bar style from now on.
        /// </summary>
        void SaveBarStyleFrom(BarPlacement bar)
        {
            string lay = Styles.LayoutOf(bar);
            BarStyle cur = FindUserBarStyle(S.BarStyle.Name);
            string name = AskStyleName("Save Bar Style",
                "Name for the bar style. This bar's look (height, corners, edge gap, outline) is saved as its " + lay + " look; the other layouts " +
                "come from your other bars. Every bar that follows the bar style takes it.", cur != null ? cur.Name : "My bars", true);
            if (name == null) return;
            BarStyle st = Styles.CaptureFromBars(S, bar);
            st.Name = name;
            BarStyle old = FindUserBarStyle(name);
            if (old != null) S.UserBarStyles[S.UserBarStyles.IndexOf(old)] = st; else S.UserBarStyles.Add(st);
            _galleryTab.Remove("Bar styles");
            S.BarStyle = Styles.Clone(st);
            S.BarStyleName = name;
            bar.FollowStyle = true;
            RefollowBars();
            Styles.ApplyBars(S);
            _mgr.SettingsChanged();
            LoadBar();
        }

        /// <summary>"Save Widget Style" on a widget's page: this widget's look, saved under a name and used as the widget style from now on.</summary>
        void SaveWidgetStyleFrom(WidgetSettings w)
        {
            WidgetStyle cur = FindUserWidgetStyle(S.WidgetStyle.Name);
            string name = AskStyleName("Save Widget Style",
                "Name for the widget style (this widget's corners, border, shadow, padding and spacing). Every widget that follows the widget style takes it.",
                cur != null ? cur.Name : "My widgets", false);
            if (name == null) return;
            WidgetStyle st = Styles.CaptureWidget(w, name);
            WidgetStyle old = FindUserWidgetStyle(name);
            if (old != null) S.UserWidgetStyles[S.UserWidgetStyles.IndexOf(old)] = st; else S.UserWidgetStyles.Add(st);
            _galleryTab.Remove("Widget styles");
            S.WidgetStyle = Styles.Clone(st);
            S.WidgetStyleName = name;
            w.FollowStyle = true;
            RefollowWidgets();
            Styles.ApplyWidgets(S);
            _mgr.SettingsChanged();
            LoadWidget();
        }

        /// <summary>Ask for a style name; null = cancelled or a built-in name.</summary>
        string AskStyleName(string title, string label, string suggested, bool bar)
        {
            string name = PromptWindow.Ask(this, title, label, suggested);
            if (name == null) return null;
            name = name.Trim();
            if (name.Length == 0) return null;
            if (bar ? Styles.FindBuiltInBar(name) != null : Styles.FindBuiltInWidget(name) != null)
            {
                MessageBox.Show(this, "\"" + name + "\" is a built-in style - choose another name.", title, MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return name;
        }

        // =================================================================
        // Widget style
        // =================================================================
        void BuildWidgetStyleSection(StackPanel p)
        {
            Intro(p, "The box around every widget: corners, border, shadow and spacing. The buttons work on the style in use: Duplicate saves " +
                     "a copy you can Edit, or shape a widget on its page and click Save Widget Style there.");
            AddGallery(p, WidgetStyleGallery);

            int own = 0;
            foreach (WidgetSettings w in S.Widgets) if (!w.FollowStyle) own++;
            Note(p, own == 0 ? "Every widget follows the widget style. To give one widget its own look, change its corners, border, shadow or spacing on its own page " +
                               "(or turn off \"Follow the widget style\" on its Look tab)."
                             : own + (own == 1 ? " widget has" : " widgets have") + " its own look and is left as it is (Widgets > the widget > Look > Follow the widget style).");
            if (own > 0)
                ListButton(p, "Make every widget follow the widget style", delegate
                {
                    foreach (WidgetSettings w in S.Widgets) w.FollowStyle = true;
                    Styles.ApplyWidgets(S);
                    _mgr.SettingsChanged();
                    BuildAppearancePage();
                });
        }

        void WidgetStyleGallery(StackPanel holder)
        {
            Pal pal = CurrentPal();
            var cards = new List<UIElement>();
            var mine = new List<UIElement>();
            UIElement current = null;
            WidgetStyle currentUser = null;
            foreach (WidgetStyle st in Styles.BuiltInWidgetStyles)
            {
                WidgetStyle s0 = st;
                bool sel = Styles.Same(S.WidgetStyle, st);
                var c = StyleCard(st.Name, WidgetStylePic(pal, st), sel, false, null, delegate { ApplyWidgetStyle(s0); });
                cards.Add(c);
                if (sel && current == null) current = c;
            }
            foreach (WidgetStyle st in S.UserWidgetStyles)
            {
                WidgetStyle s0 = st;
                bool sel = Styles.Same(S.WidgetStyle, st);
                var c = StyleCard(st.Name, WidgetStylePic(pal, st), sel, true, "Your saved widget style", delegate { ApplyWidgetStyle(s0); });
                mine.Add(c);
                if (sel && current == null) { current = c; currentUser = s0; }
            }
            if (current == null)
                current = StyleCard("Current (not a preset)", WidgetStylePic(pal, S.WidgetStyle), true, false, "Your own widget style - Save Widget Style on a widget's page keeps it", null);
            WidgetStyle u = currentUser;
            Gallery(holder, "Widget styles", cards, mine, current,
                    "No saved widget styles yet - Duplicate the one in use and Edit it, or shape a widget on its page and click Save Widget Style there.",
                    StyleButtons("widget style", u != null, delegate { EditWidgetStyle(u); }, delegate { RenameWidgetStyle(u); }, DuplicateWidgetStyle, delegate
                    {
                        if (MessageBox.Show(this, "Delete the widget style \"" + u.Name + "\"? (Your widgets keep their look.)", "Delete widget style", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        S.UserWidgetStyles.Remove(u);
                        S.WidgetStyle.Name = "";
                        S.WidgetStyleName = "";
                        _mgr.SettingsChanged();
                        BuildAppearancePage();
                    }));
        }

        void ApplyWidgetStyle(WidgetStyle st)
        {
            if (!AskOwnLooks(false, true, "Apply widget style \"" + st.Name + "\"")) return;
            S.WidgetStyle = Styles.Clone(st);
            S.WidgetStyleName = S.WidgetStyle.Name ?? "";
            Styles.ApplyWidgets(S);
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }
    }
}
