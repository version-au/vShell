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
    /// <summary>Settings > Appearance: the theme presets (cards, apply, delete) and export / import / save as a preset.</summary>
    public partial class SettingsWindow
    {
        // =================================================================
        // Appearance
        // =================================================================
        bool IsCurrentTheme(string bg, string fg, string ac) { return IsCurrentTheme(S, bg, fg, ac); }

        internal static bool IsCurrentTheme(BarSettings S, string bg, string fg, string ac)
        {
            if (S.WallpaperTheme) return false;     // the wallpaper's colours are in use, not these
            return string.Equals(S.Background, bg, StringComparison.OrdinalIgnoreCase) && string.Equals(S.MainAccent, ac, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(S.Foreground, fg, StringComparison.OrdinalIgnoreCase);
        }

        static string Hex(Color c, string alphaFrom)
        {
            byte a = Theme.Parse(alphaFrom, Colors.Black).A;
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", a, c.R, c.G, c.B);
        }

        /// <summary>custom = a card for colours that are not a preset (always shown as in use); click = what clicking it does instead of applying it.</summary>
        UIElement PresetCard(string name, string bg, string fg, string ac, string sp, string bg2, bool user, bool custom = false, Action click = null)
        {
            Color cb = Theme.Parse(bg, Colors.Black), cf = Theme.Parse(fg, Colors.White), ca = Theme.Parse(ac, Colors.SteelBlue),
                  cs = Theme.Parse(sp, Colors.Pink), cb2 = Theme.Parse(bg2, cb);
            cb = Color.FromRgb(cb.R, cb.G, cb.B); cb2 = Color.FromRgb(cb2.R, cb2.G, cb2.B);
            bool sel = custom || (!S.WallpaperTheme && IsCurrentTheme(bg, fg, ac));

            var pic = new Grid { Height = 64, Background = new LinearGradientBrush(cb, cb2, 0) };
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            top.Children.Add(new Border { Width = 26, Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ca) });
            top.Children.Add(new Border { Width = 16, Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(cs), Margin = new Thickness(6, 0, 0, 0) });
            pic.Children.Add(top);
            pic.Children.Add(new TextBlock { Text = "12:45", FontSize = 10, Foreground = new SolidColorBrush(cf), Opacity = 0.85, Margin = new Thickness(0, 7, 10, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
            var tasks = new UniformGrid { Columns = 3, Margin = new Thickness(10, 0, 10, 10), VerticalAlignment = VerticalAlignment.Bottom, Height = 15 };
            tasks.Children.Add(new Border { Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, cf.R, cf.G, cf.B)), BorderBrush = new SolidColorBrush(ca), BorderThickness = new Thickness(0, 0, 0, 2) });
            tasks.Children.Add(new Border { Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, cs.R, cs.G, cs.B)), BorderBrush = new SolidColorBrush(cs), BorderThickness = new Thickness(0, 0, 0, 2) });
            pic.Children.Add(tasks);

            var nameRow = new DockPanel { Margin = new Thickness(10, 7, 10, 8) };
            if (sel)
            {
                var tick = Glyph("", 11);
                Ref(tick, TextBlock.ForegroundProperty, "vsAccent");
                DockPanel.SetDock(tick, Dock.Right);
                nameRow.Children.Add(tick);
            }
            var nt = Txt((user ? "★ " : "") + name, sel ? "vsFg" : "vsFg", 12.5, false);
            nt.TextTrimming = TextTrimming.CharacterEllipsis;
            nt.TextWrapping = TextWrapping.NoWrap;
            nameRow.Children.Add(nt);

            var stack = new StackPanel();
            stack.Children.Add(new Border { CornerRadius = new CornerRadius(7, 7, 0, 0), ClipToBounds = true, Child = pic });
            stack.Children.Add(nameRow);
            var card = new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(sel ? 2 : 1), Margin = new Thickness(0, 0, 10, 10),
                Child = stack, Cursor = Cursors.Hand, ClipToBounds = true,
                ToolTip = custom ? null : user ? "Your saved preset" : null
            };
            Ref(card, Border.BackgroundProperty, "vsCard");
            Ref(card, Border.BorderBrushProperty, sel ? "vsAccent" : "vsLine");
            card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (click != null) { click(); return; }
                ApplyPreset(name, user);
            };
            return card;
        }

        void ApplyPreset(string name, bool user)
        {
            if (user)
            {
                ThemePreset u = FindUserPreset(name);
                if (u == null) return;
                ApplyThemeFields(u);
            }
            else
            {
                string[] p;
                if (!BarSettings.Presets.TryGetValue(name, out p)) return;
                S.WallpaperTheme = false;
                S.Background = p[0]; S.Foreground = p[1]; S.MainAccent = p[2];
                if (p.Length > 3) S.SecondaryAccent = p[3];
                if (p.Length > 4) S.Background2 = p[4];
            }
            _selPreset = name;
            _selPresetIsUser = user;
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }


        void DeleteSelectedPreset()
        {
            if (!_selPresetIsUser || string.IsNullOrEmpty(_selPreset)) return;
            string name = _selPreset;
            if (MessageBox.Show(this, "Delete the preset \"" + name + "\"?", "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ThemePreset u = FindUserPreset(name);
            if (u != null) S.UserPresets.Remove(u);
            _selPreset = null;
            _selPresetIsUser = false;
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        // =================================================================
        // Themes: export / import / user presets
        // =================================================================
        void ExportTheme()
        {
            bool inUseUser;
            LookTheme inUse = ThemeInUse(out inUseUser);
            string suggested = inUse != null ? inUse.Name : "my-theme";
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) suggested = suggested.Replace(ch, '-');
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Export theme", Filter = "vShell theme (*.vstheme.json)|*.vstheme.json|JSON (*.json)|*.json", FileName = suggested + ".vstheme.json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                // the whole look: colours (+ background, blur, font), the bar style and the widget style
                string name = System.IO.Path.GetFileName(dlg.FileName).Replace(".vstheme.json", "").Replace(".json", "");
                var t = new ThemePreset { Name = name };
                FillThemeFields(t);
                var file = new ThemeFile { Format = "vShell-theme", Version = 2, Name = name, Theme = t, BarStyle = Styles.CaptureFromBars(S, null), WidgetStyle = Styles.CaptureFromWidgets(S) };
                System.IO.File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(file, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the theme: " + ex.Message, "Export theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void ImportTheme()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import theme", Filter = "vShell theme (*.vstheme.json;*.json)|*.vstheme.json;*.json|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string text = System.IO.File.ReadAllText(dlg.FileName);
                ThemePreset t = null;
                ThemeFile f = null;
                try
                {
                    f = System.Text.Json.JsonSerializer.Deserialize<ThemeFile>(text);
                    if (f != null && f.Theme != null) t = f.Theme;
                }
                catch { }
                if (t == null && (f == null || (f.BarStyle == null && f.WidgetStyle == null)))
                    t = System.Text.Json.JsonSerializer.Deserialize<ThemePreset>(text);      // a bare preset also works
                Color c;
                bool coloursOk = t != null && Theme.TryParse(t.Background ?? "", out c) && Theme.TryParse(t.Foreground ?? "", out c) && Theme.TryParse(t.MainAccent ?? "", out c);
                bool hasStyles = f != null && (f.BarStyle != null || f.WidgetStyle != null);
                if (!coloursOk && !hasStyles)
                {
                    MessageBox.Show(this, "That file is not a vShell theme (the colours could not be read).", "Import theme", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (coloursOk)
                {
                    if (t.BgStyle == null) t.BgStyle = "Solid";
                    if (string.IsNullOrWhiteSpace(t.Background2)) t.Background2 = "#E6313244";
                    if (string.IsNullOrWhiteSpace(t.SecondaryAccent)) t.SecondaryAccent = "#FFF5C2E7";
                }

                if (hasStyles)
                {
                    // a whole theme (0.20+): keep it with your themes and use it
                    string tname = !string.IsNullOrWhiteSpace(f.Name) ? f.Name.Trim() : coloursOk && !string.IsNullOrWhiteSpace(t.Name) ? t.Name.Trim()
                                 : System.IO.Path.GetFileNameWithoutExtension(dlg.FileName).Replace(".vstheme", "");
                    while (Styles.FindBuiltInTheme(tname) != null) tname += " (imported)";
                    var lt = FindUserTheme(tname);
                    if (lt == null) { lt = new LookTheme(); S.UserThemes.Add(lt); }
                    lt.Name = tname;
                    lt.ColourPreset = null;
                    lt.Colours = coloursOk ? t : null;
                    if (lt.Colours != null) lt.Colours.Name = tname;
                    lt.Bars = f.BarStyle;
                    if (lt.Bars != null) { Styles.Fix(lt.Bars); if (Styles.FindBuiltInBar(lt.Bars.Name) != null && !Styles.Same(lt.Bars, Styles.FindBuiltInBar(lt.Bars.Name))) lt.Bars.Name = ""; }
                    lt.Widgets = f.WidgetStyle;
                    if (lt.Widgets != null && Styles.FindBuiltInWidget(lt.Widgets.Name) != null && !Styles.Same(lt.Widgets, Styles.FindBuiltInWidget(lt.Widgets.Name))) lt.Widgets.Name = "";
                    ApplyLookTheme(lt, true);
                    return;
                }
                ApplyThemeFields(t);

                // keep it in the preset list too (never overwrite a built-in name)
                string name = string.IsNullOrWhiteSpace(t.Name) ? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) : t.Name.Trim();
                foreach (string k in BarSettings.Presets.Keys) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) name += " (imported)";
                ThemePreset u = FindUserPreset(name);
                if (u == null) { u = new ThemePreset { Name = name }; S.UserPresets.Add(u); }
                FillThemeFields(u);

                _selPreset = name;
                _selPresetIsUser = true;
                _mgr.SettingsChanged();
                ApplyChrome();
                BuildAppearancePage();
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not read the theme: " + ex.Message, "Import theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        /// <summary>Copy every theme field of a preset / imported file into the live settings.</summary>
        void ApplyThemeFields(ThemePreset u) { ApplyThemeFields(S, u); }

        internal static void ApplyThemeFields(BarSettings S, ThemePreset u)
        {
            S.Background = u.Background; S.Foreground = u.Foreground; S.MainAccent = u.MainAccent;
            if (!string.IsNullOrWhiteSpace(u.FontFamily)) S.FontFamily = u.FontFamily;
            S.BgStyle = u.BgStyle; S.Background2 = u.Background2; S.GradientAngle = u.GradientAngle;
            S.SecondaryAccent = u.SecondaryAccent;
            S.BlurMode = string.IsNullOrEmpty(u.Blur) ? "Off" : u.Blur;
            S.WallpaperTheme = false;     // a preset is fixed colours
            S.Normalize();
        }

        void FillThemeFields(ThemePreset u)
        {
            u.Background = S.Background; u.Foreground = S.Foreground; u.MainAccent = S.MainAccent; u.FontFamily = S.FontFamily;
            u.BgStyle = S.BgStyle; u.Background2 = S.Background2; u.GradientAngle = S.GradientAngle;
            u.SecondaryAccent = S.SecondaryAccent;
            u.Blur = S.BlurMode;
            // Wallpaper Theme on: store the colours it worked out, so the preset keeps them whatever the wallpaper does later
            WallpaperPalette w;
            if (S.WallpaperTheme && WallpaperColor.TryGetPalette(S, out w))
            {
                u.Background = Hex(w.Background, S.Background);
                u.Background2 = Hex(w.Background2, S.Background2);
                u.Foreground = WallpaperColor.Hex(w.Foreground);
                u.MainAccent = WallpaperColor.Hex(w.MainAccent);
                u.SecondaryAccent = WallpaperColor.Hex(w.SecondaryAccent);
            }
        }

        ThemePreset FindUserPreset(string name)
        {
            foreach (var u in S.UserPresets)
                if (string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)) return u;
            return null;
        }

        void SaveCurrentAsPreset(string suggested = "My theme")
        {
            string name = PromptWindow.Ask(this, "Save theme preset", "Name for this preset (colours and font of the current theme):", suggested);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            foreach (string k in BarSettings.Presets.Keys)
                if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "\"" + name + "\" is a built-in preset - choose another name.", "Save theme preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

            ThemePreset u = FindUserPreset(name);
            if (u == null) { u = new ThemePreset { Name = name }; S.UserPresets.Add(u); }
            FillThemeFields(u);
            _galleryTab.Remove("Colours");      // the gallery opens on Saved, where the new one is
            _mgr.SettingsChanged();
            _selPreset = name;
            _selPresetIsUser = true;
            BuildAppearancePage();
        }


    }
}
