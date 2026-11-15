using System;
using System.Windows;
using System.Text.Json;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The plain light colours the settings / welcome windows fall back on if the suite's styles can't be loaded (SettingsStyle.Dress
    /// gives them the theme normally), so Windows' default controls stay readable.
    /// </summary>
    public sealed class SettingsPalette
    {
        public Color Bg, Side, Card, Ctrl, CtrlHover, CtrlBorder, Line, Fg, Dim, MainAccent, AccentText, Popup, SelBg;
        public bool Dark;

        /// <summary>A plain light palette: used if the control styles could not be loaded, so Windows' default controls stay readable.</summary>
        public static SettingsPalette Plain()
        {
            var p = new SettingsPalette();
            p.Dark = false;
            p.Bg = Color.FromRgb(0xF3, 0xF3, 0xF3); p.Side = Color.FromRgb(0xEA, 0xEA, 0xEA); p.Card = Colors.White;
            p.Popup = Colors.White; p.Ctrl = Color.FromRgb(0xF0, 0xF0, 0xF0); p.CtrlHover = Color.FromRgb(0xE5, 0xE5, 0xE5);
            p.CtrlBorder = Color.FromRgb(0xC8, 0xC8, 0xC8); p.Line = Color.FromRgb(0xE6, 0xE6, 0xE6);
            p.Fg = Color.FromRgb(0x1A, 0x1A, 0x1A); p.Dim = Color.FromRgb(0x60, 0x60, 0x60);
            p.MainAccent = Color.FromRgb(0x00, 0x5F, 0xB8); p.AccentText = Colors.White; p.SelBg = Color.FromArgb(0x30, 0x00, 0x5F, 0xB8);
            return p;
        }
    }

    public static class SettingsStyle
    {
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // =================================================================
        // The suite's look (vSuite.Core, 0.38.168): the settings and welcome windows take the same colours, controls and glass as
        // vXplorer's and vView's Options. The vs* names the settings code uses are aliases of the suite's vx.* brushes.
        // =================================================================

        /// <summary>vShell's theme as the suite's windows take it: the bars' colours (Wallpaper Theme included), Blur / Frosted, the widget style.</summary>
        public static vSuite.Ui.Theme SuiteTheme(BarSettings s)
        {
            vSuite.VShellColours c;
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(s)))
                c = vSuite.VShellThemeSource.FromJson(doc.RootElement);
            Theme t = Theme.From(s, 12, 8);
            c.Background = Hex(t.BgColor);
            c.Background2 = Hex(t.Bg2Color);
            c.Foreground = Hex(((SolidColorBrush)t.Fg).Color);
            c.MainAccent = Hex(((SolidColorBrush)t.MainAccent).Color);
            c.SecondaryAccent = Hex(((SolidColorBrush)t.SecondaryAccent).Color);
            return vSuite.Ui.Theme.From(c);
        }

        static string Hex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B); }

        /// <summary>
        /// The suite's styles (Controls.xaml + Forms.xaml, on this window only) and the theme's brushes, with the vs* names as aliases.
        /// glass = the window shows the Blur / Frosted glass (its background keeps vShell's transparency); otherwise everything is opaque.
        /// Call again whenever the theme changes. Returns the theme used.
        /// </summary>
        public static vSuite.Ui.Theme Dress(FrameworkElement root, BarSettings s, bool glass)
        {
            vSuite.Ui.Theme t = SuiteTheme(s);
            vSuite.Ui.Look.Dress(root, t, glass, true);
            ResourceDictionary r = root.Resources;
            r["vsBg"] = r["vx.Bg"]; r["vsSide"] = r["vx.Side"]; r["vsCard"] = r["vx.Panel"]; r["vsPopup"] = r["vx.Popup"];
            r["vsCtrl"] = r["vx.Ctrl"]; r["vsCtrlHover"] = r["vx.CtrlHover"]; r["vsCtrlBorder"] = r["vx.CtrlBorder"]; r["vsLine"] = r["vx.Line"];
            r["vsFg"] = r["vx.Fg"]; r["vsDim"] = r["vx.FgDim"]; r["vsAccent"] = r["vx.Accent"]; r["vsAccentText"] = r["vx.OnAccent"]; r["vsSelBg"] = r["vx.SelBg"];
            // vShell's primary button keeps its name
            if (!r.Contains("vsPrimary") && root.TryFindResource("vx.Primary") is Style primary) r["vsPrimary"] = primary;
            return t;
        }

        /// <summary>Publish the palette's brushes on the window (live: everything refers to them dynamically).</summary>
        public static void SetBrushes(FrameworkElement root, SettingsPalette p)
        {
            var r = root.Resources;
            r["vsBg"] = B(p.Bg); r["vsSide"] = B(p.Side); r["vsCard"] = B(p.Card); r["vsPopup"] = B(p.Popup);
            r["vsCtrl"] = B(p.Ctrl); r["vsCtrlHover"] = B(p.CtrlHover); r["vsCtrlBorder"] = B(p.CtrlBorder); r["vsLine"] = B(p.Line);
            r["vsFg"] = B(p.Fg); r["vsDim"] = B(p.Dim); r["vsAccent"] = B(p.MainAccent); r["vsAccentText"] = B(p.AccentText); r["vsSelBg"] = B(p.SelBg);
        }
    }
}
