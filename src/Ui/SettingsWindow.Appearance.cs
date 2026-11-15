using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Settings > Appearance: the theme in use (a picture and Change theme), then a row each for Colours, Background, Bar style, Widget
    /// style and Windows colours with a summary - each opens its own page (Appearance › Colours …) - and the time format right on the list.
    /// </summary>
    public sealed partial class SettingsWindow
    {
        /// <summary>The Appearance page that shows: theme, colours, background, barstyle, widgetstyle or windows.</summary>
        string _lookPart = "theme";
        /// <summary>Where Back goes from that page: Appearance, or Bars / Widgets when their Bar style / Widget style link opened it.</summary>
        string _lookFrom = PgAppearance;

        static readonly string LookGlyph = char.ConvertFromUtf32(0xE790), BackgroundGlyph = char.ConvertFromUtf32(0xE7F4),
                               WindowsGlyph = char.ConvertFromUtf32(0xE713);
        static readonly string[] BlurNames = { "Off", "Blur", "Frosted" };

        void OpenLook(string part, string from = PgAppearance)
        {
            _lookPart = part;
            _lookFrom = from;
            _editBar = null;        // a style page opens on its gallery, not an edit left open
            _editWidget = null;
            Navigate(PgLook);
        }

        /// <summary>The Appearance list and the part page again (a theme, preset or style was picked, a switch changed what shows).</summary>
        void BuildAppearancePage()
        {
            _galleries.Clear();
            _colorRefresh.Clear();
            _opacity = null;
            _opacityValue = null;
            WallpaperPalette wall = null;
            if (S.WallpaperTheme && !WallpaperColor.TryGetPalette(S, out wall)) wall = null;
            _shownWall = wall;
            bool old = _loading;
            _loading = true;
            try
            {
                BuildAppearanceList();
                BuildLookPart(wall);
            }
            finally { _loading = old; }
            RefreshColors();
        }

        // ---- summaries ----
        string BackgroundSummary()
        {
            int op = (int)Math.Round(Theme.Parse(S.Background, Colors.Black).A * 100.0 / 255.0);
            string blur = BlurNames[Math.Max(0, Array.IndexOf(BarSettings.BlurModes, S.BlurMode))];
            return op + " % opacity  ·  " + (S.BgStyle == "Gradient" ? "gradient, " + AngleText(S.GradientAngle) : "solid") + "  ·  " +
                   (blur == "Off" ? "no blur" : blur + " blur");
        }

        static string AngleText(int a)
        {
            switch (((a % 360) + 360) % 360)
            {
                case 0: return "left to right";
                case 90: return "top to bottom";
                case 180: return "right to left";
                case 270: return "bottom to top";
                default: return a + "°";
            }
        }

        string FollowSummary(int own, int all, string what)
        {
            if (all == 0) return "no " + what + "s yet";
            if (own == 0) return all == 1 ? "your " + what + " follows it" : "every " + what + " follows it";
            int follow = all - own;
            return (follow == 1 ? "1 " + what + " follows it" : follow + " " + what + "s follow it") + ", " +
                   (own == 1 ? "1 has its own look" : own + " have their own look");
        }

        string WindowsSummary()
        {
            if (!S.WinMatch) return "Off - Windows keeps its own colours";
            var parts = new List<string>();
            if (S.WinMatchAccent) parts.Add("accent colour from the Main Accent");
            if (S.WinMatchMode) parts.Add("light / dark mode from the background");
            return parts.Count == 0 ? "On, but nothing picked to match" : char.ToUpperInvariant(parts[0][0]) + string.Join("  ·  ", parts).Substring(1);
        }

        /// <summary>Four small squares: background, text, Main Accent, Secondary Accent as they are now.</summary>
        UIElement ColourChips()
        {
            Pal c = CurrentPal();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (Color col in new[] { c.Bg, c.Fg, c.Ac, c.Sp })
            {
                var b = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(4, 0, 0, 0), Background = new SolidColorBrush(Rgb(col)) };
                Ref(b, Border.BorderBrushProperty, "vsCtrlBorder");
                row.Children.Add(b);
            }
            row.IsHitTestVisible = false;      // a click on them opens the row
            return row;
        }

        // =================================================================
        // The list
        // =================================================================
        void BuildAppearanceList()
        {
            var p = Page(PgAppearance);
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            PageHeader(p, "Appearance", "Themes, colours and the shape of every bar and widget",
                       Btn("Save as theme…", delegate { SaveTheme(); }),
                       Btn("Import…", delegate { ImportTheme(); }),
                       Btn("Export…", delegate { ExportTheme(); }));

            // the theme in use: its picture, name and what it's made of
            bool inUseUser;
            LookTheme inUse = ThemeInUse(out inUseUser);
            NewCard(p);
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var pic = new Border { Width = 260, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = ThemePic(CurrentPal(), S.BarStyle, S.WidgetStyle), VerticalAlignment = VerticalAlignment.Center };
            hero.Children.Add(pic);
            var info = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Txt("Theme in use", "vsDim", 12, false));
            var name = Txt(inUse != null ? (inUseUser ? "★ " : "") + inUse.Name : "Your own look", "vsFg", 20, true);
            name.Margin = new Thickness(0, 2, 0, 0);
            info.Children.Add(name);
            var made = Txt("Colours " + (S.WallpaperTheme ? "from your wallpaper" : ColoursDescription()) + "  ·  bar style " + StyleText(S.BarStyleName) +
                           "  ·  widget style " + StyleText(S.WidgetStyleName), "vsDim", 12.5, false);
            made.Margin = new Thickness(0, 6, 0, 0);
            info.Children.Add(made);
            var change = Btn("Change theme ›", delegate { OpenLook("theme"); }, true);
            change.HorizontalAlignment = HorizontalAlignment.Left;
            change.Margin = new Thickness(0, 12, 0, 0);
            info.Children.Add(change);
            Grid.SetColumn(info, 1);
            hero.Children.Add(info);
            Place(p, hero);

            Header(p, "Look");
            LinkRow(p, LookGlyph, "Colours", S.WallpaperTheme ? "From your wallpaper - they follow it when it changes" : ColoursDescription(),
                    ColourChips(), delegate { OpenLook("colours"); });
            LinkRow(p, BackgroundGlyph, "Background", BackgroundSummary(), null, delegate { OpenLook("background"); });
            int ownBars = 0, ownWidgets = 0;
            foreach (BarPlacement b in S.Bars) if (!b.FollowStyle) ownBars++;
            foreach (WidgetSettings w in S.Widgets) if (!w.FollowStyle) ownWidgets++;
            LinkRow(p, BarGlyph, "Bar style", StyleText(S.BarStyleName) + "  ·  " + FollowSummary(ownBars, S.Bars.Count, "bar"), null, delegate { OpenLook("barstyle"); });
            LinkRow(p, WidgetGlyph, "Widget style", StyleText(S.WidgetStyleName) + "  ·  " + FollowSummary(ownWidgets, S.Widgets.Count, "widget"), null,
                    delegate { OpenLook("widgetstyle"); });

            Header(p, "Windows and time");
            LinkRow(p, WindowsGlyph, "Match Windows 11 colour to vShell's theme", WindowsSummary(),
                    Switch(delegate { return S.WinMatch; }, delegate (bool v) { S.WinMatch = v; }, delegate { BuildAppearancePage(); }),
                    delegate { OpenLook("windows"); });
            // Build 2: no font choice - everything is Segoe UI (text size is S / M / L per module)
            ComboRow(p, "Times", new[] { "12-hour", "24-hour" },
                     delegate { return S.Use24HourTimes ? "24-hour" : "12-hour"; }, delegate (string v) { S.Use24HourTimes = v == "24-hour"; },
                     "How notifications, \"last checked\", \"added\" … show times. 12-hour: 2:05 PM, 24-hour: 14:05. Each Clock module has its own setting.");
        }

        // =================================================================
        // A part's page
        // =================================================================
        void BuildLookPart(WallpaperPalette wall)
        {
            var p = Page(PgLook);
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            string from = _lookFrom;
            Button back = Btn("‹  Back", delegate { Navigate(from); });
            switch (_lookPart)
            {
                case "colours": LookColours(p, back, wall); break;
                case "background": LookBackground(p, back); break;
                case "barstyle":
                    if (_editBar != null && S.UserBarStyles.Contains(_editBar)) { BarStyleEditor(p); break; }
                    _editBar = null;
                    PageHeader(p, "Bar style", (from == PgBars ? "Bars" : "Appearance") + "  ›  Bar style", back);
                    BuildBarStyleSection(p);
                    break;
                case "widgetstyle":
                    if (_editWidget != null && S.UserWidgetStyles.Contains(_editWidget)) { WidgetStyleEditor(p); break; }
                    _editWidget = null;
                    PageHeader(p, "Widget style", (from == PgWidgets ? "Widgets" : "Appearance") + "  ›  Widget style", back);
                    BuildWidgetStyleSection(p);
                    break;
                case "windows": LookWindows(p, back); break;
                default: LookThemes(p, back); break;
            }
        }

        /// <summary>A line of explanation under a page's title.</summary>
        void Intro(StackPanel p, string text)
        {
            EndSection(p);
            var t = Txt(text, "vsDim", 12.5, false);
            t.Margin = new Thickness(2, -6, 0, 16);
            p.Children.Add(t);
        }

        void LookThemes(StackPanel p, Button back)
        {
            LookTheme selUser = _selThemeIsUser && _selTheme != null ? FindUserTheme(_selTheme) : null;
            var delTheme = Btn("Delete theme", delegate { DeleteSelectedTheme(); });
            delTheme.IsEnabled = selUser != null;
            delTheme.ToolTip = "Delete the saved theme you picked last";
            PageHeader(p, "Theme", "Appearance  ›  Theme",
                       Btn("Save as theme…", delegate { SaveTheme(); }),
                       Btn("Import…", delegate { ImportTheme(); }),
                       Btn("Export…", delegate { ExportTheme(); }),
                       delTheme, back);
            AddGallery(p, ThemeGallery);
        }

        void LookColours(StackPanel p, Button back, WallpaperPalette wall)
        {
            PageHeader(p, "Colours", "Appearance  ›  Colours", back);
            Intro(p, "Background, text and accents - shared by every bar and widget.");

            Header(p, "From your wallpaper");
            Check(p, "Colours from your wallpaper", delegate { return S.WallpaperTheme; }, delegate (bool v)
            {
                S.WallpaperTheme = v;
                if (v) { _selPreset = null; _selPresetIsUser = false; }
                Dispatcher.BeginInvoke(new Action(delegate { BuildAppearancePage(); }));    // the wallpaper's colours, colour rows on / off
            }, "Background, gradient, text and both accents are taken from the main monitor's wallpaper and follow it when it changes. " +
               "The background opacity is still yours, and your own colours are kept for when it's off.");
            if (S.WallpaperTheme)
            {
                if (wall != null)
                {
                    var wrow = new Grid();
                    wrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    wrow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                    var wcard = PresetCard("From your wallpaper", Hex(wall.Background, S.Background), WallpaperColor.Hex(wall.Foreground), WallpaperColor.Hex(wall.MainAccent),
                                           WallpaperColor.Hex(wall.SecondaryAccent), Hex(wall.Background2, S.Background2), false, true, delegate { });
                    var wfe = wcard as FrameworkElement;
                    if (wfe != null) wfe.Margin = new Thickness(0);
                    wrow.Children.Add(wcard);
                    var save = Btn("Save as preset…", delegate { SaveCurrentAsPreset("Wallpaper theme"); }, true);
                    save.HorizontalAlignment = HorizontalAlignment.Left;
                    save.VerticalAlignment = VerticalAlignment.Center;
                    save.Margin = new Thickness(16, 0, 0, 0);
                    Grid.SetColumn(save, 1);
                    wrow.Children.Add(save);
                    Place(p, wrow);
                }
                else Place(p, Txt("The wallpaper could not be read (none set, or an unsupported picture), so your own colours are used.", "vsDim", 12.5, false));
            }

            // colour presets
            AddGallery(p, ColourGallery);

            Header(p, "Your colours");
            ColorRow(p, "Background", delegate { return S.Background; }, delegate (string v) { S.Background = v; }, false, true);
            ColorRow(p, "Background fade", delegate { return S.Background2; }, delegate (string v) { S.Background2 = v; }, false, true);
            Note(p, "The colour the background fades into when Appearance > Background > Style is Gradient.");
            ColorRow(p, "Text", delegate { return S.Foreground; }, delegate (string v) { S.Foreground = v; }, false, true);
            ColorRow(p, "Main Accent", delegate { return S.MainAccent; }, delegate (string v) { S.MainAccent = v; }, false, true);
            Note(p, "The active window, graphs and outlines.");
            ColorRow(p, "Secondary Accent", delegate { return S.SecondaryAccent; }, delegate (string v) { S.SecondaryAccent = v; }, false, true);
            Note(p, "Warnings, a second graph series and things that want your attention.");
            StackPanel colourCard;
            if (S.WallpaperTheme && _cardOf.TryGetValue(p, out colourCard))
            {
                for (int i = 0; i < colourCard.Children.Count; i++)
                    colourCard.Children[i].IsEnabled = false;      // the wallpaper decides them (your own are kept for when it is off)
                Place(p, Txt("These are your own colours, used again when Colours from your wallpaper is off.", "vsDim", 12, false));
            }
        }

        void LookBackground(StackPanel p, Button back)
        {
            PageHeader(p, "Background", "Appearance  ›  Background", back);
            Intro(p, "How the background of the bars and widgets that follow the theme looks.");

            Header(p, "Background");
            _opacity = new Slider { Minimum = 0, Maximum = 100, Width = 220, VerticalAlignment = VerticalAlignment.Center };
            _opacityValue = SliderNumber(_opacity, "%");
            _opacity.ValueChanged += delegate
            {
                if (_loading) return;
                Color c = Theme.Parse(S.Background, Colors.Black);
                byte a = (byte)Math.Round(_opacity.Value * 255.0 / 100.0);
                S.Background = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", a, c.R, c.G, c.B);
                RefreshColors();
                _mgr.SettingsChanged();
            };
            var opPanel = new StackPanel { Orientation = Orientation.Horizontal };
            opPanel.Children.Add(_opacity);
            opPanel.Children.Add(_opacityValue);
            Place(p, Row("Opacity", opPanel, false, "For the bars and widgets that follow the theme's opacity. Lower it to see more of the blur."));
            ComboRow(p, "Style", new[] { "Solid", "Gradient" }, delegate { return S.BgStyle; }, delegate (string v)
            {
                S.BgStyle = v;
                Dispatcher.BeginInvoke(new Action(delegate { BuildAppearancePage(); }));     // the gradient rows show / hide
            });
            if (S.BgStyle == "Gradient")
            {
                Note(p, "It fades from Background into Background fade - both are in Appearance > Colours.");
                SliderRow(p, "Direction (°)", 0, 360, delegate { return S.GradientAngle; }, delegate (double v) { S.GradientAngle = (int)v % 360; },
                          "0 = left to right, 90 = top to bottom.");
                Check(p, "Mirror it on bottom bars", delegate { return S.MirrorBottomGradient; }, delegate (bool v) { S.MirrorBottomGradient = v; },
                      "A top bar's gradient upside down, so top and bottom bars match.");
            }

            Header(p, "Blur");
            ComboRow(p, "Background blur", BlurNames,
                     delegate { int i = Array.IndexOf(BarSettings.BlurModes, S.BlurMode); return BlurNames[Math.Max(0, i)]; },
                     delegate (string v) { S.BlurMode = BarSettings.BlurModes[Math.Max(0, Array.IndexOf(BlurNames, v))]; },
                     "Blur shows your wallpaper behind the bars and widgets, softened; Frosted is lighter with a fine grain, like Windows' acrylic. " +
                     "The opacity above decides how much of the theme colour lies over it - try 40-70 %. It shows the desktop wallpaper " +
                     "(not the windows behind a widget kept above them). Saved with your colour presets and themes.");
        }

        void LookWindows(StackPanel p, Button back)
        {
            PageHeader(p, "Windows colours", "Appearance  ›  Windows colours", back);
            Intro(p, "Windows' own colours (Start, Settings, File Explorer …) can follow vShell's theme.");

            Header(p, "Match Windows");
            Check(p, "Match Windows 11 colour to vShell's theme", delegate { return S.WinMatch; }, delegate (bool v)
            {
                S.WinMatch = v;
                Dispatcher.BeginInvoke(new Action(delegate { BuildAppearancePage(); }));
            }, "Windows follows the theme as it changes - presets, your own colours and colours from your wallpaper. Your Windows colours are " +
               "saved the first time and put back when you turn this off.");
            if (!S.WinMatch) return;
            Check(p, "Light / dark mode", delegate { return S.WinMatchMode; }, delegate (bool v) { S.WinMatchMode = v; },
                  "From how light vShell's background is.");
            Check(p, "Accent colour", delegate { return S.WinMatchAccent; }, delegate (bool v) { S.WinMatchAccent = v; },
                  "vShell's Main Accent - Start, Settings, File Explorer and title bars when they use it. This turns off Windows' \"pick from my " +
                  "background\". A few apps only pick up a new accent when they're restarted.");
        }
    }
}
