using System;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Appearance › Bar style / Widget style: Edit, Rename, Duplicate and Delete for the style in use, and the editor for a saved style
    /// (its settings straight on the style, a picture that follows them, and every bar / widget that follows the style changes with it).
    /// </summary>
    public sealed partial class SettingsWindow
    {
        /// <summary>The saved bar / widget style being edited (null = the gallery shows), and how it was when Edit was clicked.</summary>
        BarStyle _editBar, _editBarUndo;
        WidgetStyle _editWidget, _editWidgetUndo;
        /// <summary>The bar layout whose look the bar style editor shows.</summary>
        string _editBarLayout = Styles.Classic;

        // ---- small helpers ----
        static string StyleBlurName(string styleBlur)
        {
            return BarSettings.OwnBlurNames[Math.Max(0, Array.IndexOf(BarSettings.OwnBlurModes, styleBlur ?? "Theme"))];
        }

        static string StyleBlurMode(string name)
        {
            string m = BarSettings.OwnBlurModes[Math.Max(0, Array.IndexOf(BarSettings.OwnBlurNames, name))];
            return m == "Theme" ? null : m;
        }

        static string StyleGlassName(string glass) { return glass == "Subtle" || glass == "Strong" ? glass : "Off"; }

        /// <summary>A name not taken yet: "Soft copy", "Soft copy 2" …</summary>
        static string FreeStyleName(string start, Func<string, bool> taken)
        {
            if (!taken(start)) return start;
            for (int i = 2; ; i++) if (!taken(start + " " + i)) return start + " " + i;
        }

        bool BarStyleNameTaken(string name) { return Styles.FindBuiltInBar(name) != null || FindUserBarStyle(name) != null; }
        bool WidgetStyleNameTaken(string name) { return Styles.FindBuiltInWidget(name) != null || FindUserWidgetStyle(name) != null; }

        /// <summary>Edit, Rename, Duplicate and Delete for a style gallery's head. user = the style in use when it is a saved one.</summary>
        UIElement[] StyleButtons(string what, bool user, Action edit, Action rename, Action duplicate, Action delete)
        {
            var e = Btn("Edit", edit);
            e.IsEnabled = user;
            e.ToolTip = "Change the saved " + what + " in use";
            var r = Btn("Rename", rename);
            r.IsEnabled = user;
            r.ToolTip = "Rename the saved " + what + " in use";
            var c = Btn("Duplicate", duplicate);
            c.ToolTip = "Save a copy of the " + what + " in use (a built-in one too), to edit";
            var d = Btn("Delete", delete);
            d.IsEnabled = user;
            d.ToolTip = "Delete the saved " + what + " in use";
            return new UIElement[] { e, r, c, d };
        }

        /// <summary>A picture beside the rows of the editor: a big one and the gallery card's.</summary>
        void EditorPictures(StackPanel p, UIElement big, UIElement small)
        {
            EndSection(p);
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            var a = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = big };
            var b = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = small, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(b, 1);
            g.Children.Add(a);
            g.Children.Add(b);
            p.Children.Add(g);
        }

        // =================================================================
        // Bar style
        // =================================================================
        void EditBarStyle(BarStyle st)
        {
            _editBar = st;
            _editBarUndo = Styles.Clone(st);
            BuildAppearancePage();
        }

        void RenameBarStyle(BarStyle u)
        {
            string name = AskStyleName("Rename bar style", "New name for the bar style \"" + u.Name + "\".", u.Name, true);
            if (name == null || name == u.Name) return;
            BarStyle other = FindUserBarStyle(name);
            if (other != null && other != u)
            {
                MessageBox.Show(this, "There is already a bar style called \"" + other.Name + "\" - choose another name.", "Rename bar style", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool inUse = string.Equals(S.BarStyle.Name, u.Name, StringComparison.OrdinalIgnoreCase);
            u.Name = name;
            if (inUse) { S.BarStyle.Name = name; S.BarStyleName = name; }
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        /// <summary>A copy of the bar style in use, saved under a new name and used from now on (your bars look the same).</summary>
        void DuplicateBarStyle()
        {
            string from = string.IsNullOrEmpty(S.BarStyle.Name) ? "My bars" : S.BarStyle.Name + " copy";
            string name = AskStyleName("Duplicate bar style", "Name for the copy. It is saved with your bar styles and used from now on - click Edit to change it.",
                                       FreeStyleName(from, BarStyleNameTaken), true);
            if (name == null) return;
            if (FindUserBarStyle(name) != null)
            {
                MessageBox.Show(this, "There is already a bar style called \"" + name + "\" - choose another name.", "Duplicate bar style", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            BarStyle st = Styles.Clone(S.BarStyle);
            Styles.Fix(st);
            st.Name = name;
            S.UserBarStyles.Add(st);
            S.BarStyle = Styles.Clone(st);
            S.BarStyleName = name;
            _galleryTab.Remove("Bar styles");       // the gallery opens on Saved, where the copy is
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        /// <summary>The style being edited changed: redraw its pictures, and when it is the one in use, the bars that follow it take it.</summary>
        void BarStyleEdited(Action redraw)
        {
            redraw();
            if (!string.Equals(S.BarStyle.Name, _editBar.Name, StringComparison.OrdinalIgnoreCase)) return;
            S.BarStyle = Styles.Clone(_editBar);
            Styles.ApplyBars(S);
        }

        void BarStyleEditor(StackPanel p)
        {
            BarStyle st = _editBar;
            string lay = Array.IndexOf(Styles.Layouts, _editBarLayout) >= 0 ? _editBarLayout : Styles.Classic;
            BarLook l = Styles.LookFor(st, lay);
            bool inUse = string.Equals(S.BarStyle.Name, st.Name, StringComparison.OrdinalIgnoreCase);

            var undo = Btn("Undo changes", delegate
            {
                int i = S.UserBarStyles.IndexOf(_editBar);
                if (i < 0) return;
                BarStyle kept = Styles.Clone(_editBarUndo);
                S.UserBarStyles[i] = kept;
                if (string.Equals(S.BarStyle.Name, kept.Name, StringComparison.OrdinalIgnoreCase)) { S.BarStyle = Styles.Clone(kept); Styles.ApplyBars(S); }
                _editBar = kept;
                _mgr.SettingsChanged();
                BuildAppearancePage();
            });
            undo.ToolTip = "Put the style back as it was when you clicked Edit";
            var done = Btn("Done", delegate { _editBar = null; BuildAppearancePage(); }, true);
            PageHeader(p, "★ " + st.Name, "Appearance  ›  Bar style  ›  Edit", undo, done);
            Intro(p, "A bar style has a look for each layout - pick one below. " +
                     (inUse ? "Every bar that follows the bar style changes as you go." : "This style isn't in use, so your bars stay as they are."));

            var big = new Border();
            var small = new Border();
            Action redraw = delegate
            {
                Pal pal = CurrentPal();
                var g = new Grid { Height = 120, Background = Desk(pal), ClipToBounds = true };
                g.Children.Add(MiniBar(Styles.LookFor(st, lay), lay, pal, 0.75, false, 240));
                big.Child = g;
                small.Child = BarStylePic(pal, st);
            };
            redraw();
            EditorPictures(p, big, small);

            Tabs(p, Styles.Layouts, Styles.Layouts, lay, delegate (string k) { _editBarLayout = k; BuildAppearancePage(); });
            Action changed = delegate { BarStyleEdited(redraw); };
            Action again = delegate { Dispatcher.BeginInvoke(new Action(delegate { BuildAppearancePage(); })); };     // rows show / hide
            bool floating = lay == Styles.Floating, minimal = lay == Styles.Minimal;

            Header(p, lay + " look");
            SliderRow(p, "Height", 30, 96, delegate { return l.Height; }, delegate (double v) { l.Height = (int)v; changed(); });
            if (floating)
                SliderRow(p, "Gap from the screen edge", 1, 30, delegate { return Math.Max(1, l.EdgeMargin); }, delegate (double v) { l.EdgeMargin = Math.Max(1, (int)v); changed(); });
            SliderRow(p, "Corner radius", 0, 30, delegate { return l.CornerRadius; }, delegate (double v) { l.CornerRadius = v; changed(); },
                      floating ? null : "The two corners away from the screen edge.");
            if (floating)
                Check(p, "Round all four corners", delegate { return l.RoundAllCorners; }, delegate (bool v) { l.RoundAllCorners = v; changed(); },
                      "Off: only the two corners away from the screen edge.");
            ComboRow(p, "Background blur", BarSettings.OwnBlurNames, delegate { return StyleBlurName(l.Blur); }, delegate (string v) { l.Blur = StyleBlurMode(v); changed(); });
            Place(p, Row("Glass edge", Segmented(EdgeFx.GlassEdges, delegate { return StyleGlassName(l.GlassEdge); },
                delegate (string v) { l.GlassEdge = v == "Off" ? null : v; changed(); })));

            if (minimal)
            {
                Note(p, "Minimal bars have no outline.");
                return;
            }
            Header(p, "Outline");
            if (lay != Styles.Classic)
                Check(p, "Show an outline", delegate { return l.BorderThickness > 0; }, delegate (bool v)
                {
                    l.BorderThickness = v ? 2 : 0;
                    changed();
                    again();
                }, "Drawn outward from the bar's edge.");
            if (l.BorderThickness > 0 || lay == Styles.Classic)
            {
                SliderRow(p, "Thickness", 1, 8, delegate { return Math.Max(1, l.BorderThickness); }, delegate (double v) { l.BorderThickness = v; changed(); });
                SliderRow(p, "Opacity (%)", 0, 100, delegate { return l.BorderOpacity; }, delegate (double v) { l.BorderOpacity = (int)v; changed(); });
                ColorRow(p, "Colour (empty = Main Accent)", delegate { return l.BorderColor; }, delegate (string v) { l.BorderColor = v ?? ""; changed(); }, true, false);
            }
        }

        // =================================================================
        // Widget style
        // =================================================================
        void EditWidgetStyle(WidgetStyle st)
        {
            _editWidget = st;
            _editWidgetUndo = Styles.Clone(st);
            BuildAppearancePage();
        }

        void RenameWidgetStyle(WidgetStyle u)
        {
            string name = AskStyleName("Rename widget style", "New name for the widget style \"" + u.Name + "\".", u.Name, false);
            if (name == null || name == u.Name) return;
            WidgetStyle other = FindUserWidgetStyle(name);
            if (other != null && other != u)
            {
                MessageBox.Show(this, "There is already a widget style called \"" + other.Name + "\" - choose another name.", "Rename widget style", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool inUse = string.Equals(S.WidgetStyle.Name, u.Name, StringComparison.OrdinalIgnoreCase);
            u.Name = name;
            if (inUse) { S.WidgetStyle.Name = name; S.WidgetStyleName = name; }
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        /// <summary>A copy of the widget style in use, saved under a new name and used from now on (your widgets look the same).</summary>
        void DuplicateWidgetStyle()
        {
            string from = string.IsNullOrEmpty(S.WidgetStyle.Name) ? "My widgets" : S.WidgetStyle.Name + " copy";
            string name = AskStyleName("Duplicate widget style", "Name for the copy. It is saved with your widget styles and used from now on - click Edit to change it.",
                                       FreeStyleName(from, WidgetStyleNameTaken), false);
            if (name == null) return;
            if (FindUserWidgetStyle(name) != null)
            {
                MessageBox.Show(this, "There is already a widget style called \"" + name + "\" - choose another name.", "Duplicate widget style", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            WidgetStyle st = Styles.Clone(S.WidgetStyle);
            st.Name = name;
            S.UserWidgetStyles.Add(st);
            S.WidgetStyle = Styles.Clone(st);
            S.WidgetStyleName = name;
            _galleryTab.Remove("Widget styles");
            _mgr.SettingsChanged();
            BuildAppearancePage();
        }

        void WidgetStyleEdited(Action redraw)
        {
            redraw();
            if (!string.Equals(S.WidgetStyle.Name, _editWidget.Name, StringComparison.OrdinalIgnoreCase)) return;
            S.WidgetStyle = Styles.Clone(_editWidget);
            Styles.ApplyWidgets(S);
        }

        void WidgetStyleEditor(StackPanel p)
        {
            WidgetStyle st = _editWidget;
            bool inUse = string.Equals(S.WidgetStyle.Name, st.Name, StringComparison.OrdinalIgnoreCase);

            var undo = Btn("Undo changes", delegate
            {
                int i = S.UserWidgetStyles.IndexOf(_editWidget);
                if (i < 0) return;
                WidgetStyle kept = Styles.Clone(_editWidgetUndo);
                S.UserWidgetStyles[i] = kept;
                if (string.Equals(S.WidgetStyle.Name, kept.Name, StringComparison.OrdinalIgnoreCase)) { S.WidgetStyle = Styles.Clone(kept); Styles.ApplyWidgets(S); }
                _editWidget = kept;
                _mgr.SettingsChanged();
                BuildAppearancePage();
            });
            undo.ToolTip = "Put the style back as it was when you clicked Edit";
            var done = Btn("Done", delegate { _editWidget = null; BuildAppearancePage(); }, true);
            PageHeader(p, "★ " + st.Name, "Appearance  ›  Widget style  ›  Edit", undo, done);
            Intro(p, inUse ? "Every widget that follows the widget style changes as you go." : "This style isn't in use, so your widgets stay as they are.");

            var big = new Border();
            var small = new Border();
            Action redraw = delegate
            {
                Pal pal = CurrentPal();
                var g = new Grid { Height = 120, Background = Desk(pal), ClipToBounds = true };
                var w = MiniWidget(st, pal, 1, 200, 5);
                w.HorizontalAlignment = HorizontalAlignment.Center;
                w.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(w);
                big.Child = g;
                small.Child = WidgetStylePic(pal, st);
            };
            redraw();
            EditorPictures(p, big, small);

            Action changed = delegate { WidgetStyleEdited(redraw); };
            Action again = delegate { Dispatcher.BeginInvoke(new Action(delegate { BuildAppearancePage(); })); };

            Header(p, "Look");
            SliderRow(p, "Corner radius", 0, 40, delegate { return st.CornerRadius; }, delegate (double v) { st.CornerRadius = v; changed(); });
            SliderRow(p, "Space between modules", 0, 30, delegate { return st.Spacing; }, delegate (double v) { st.Spacing = (int)v; changed(); });
            Check(p, "Shadow", delegate { return st.ShowShadow; }, delegate (bool v) { st.ShowShadow = v; changed(); });
            ComboRow(p, "Background blur", BarSettings.OwnBlurNames, delegate { return StyleBlurName(st.Blur); }, delegate (string v) { st.Blur = StyleBlurMode(v); changed(); });
            Place(p, Row("Glass edge", Segmented(EdgeFx.GlassEdges, delegate { return StyleGlassName(st.GlassEdge); },
                delegate (string v) { st.GlassEdge = v == "Off" ? null : v; changed(); })));

            Header(p, "Outline");
            Check(p, "Show an outline", delegate { return st.ShowBorder; }, delegate (bool v)
            {
                st.ShowBorder = v;
                if (v && st.BorderThickness <= 0) st.BorderThickness = 1;
                changed();
                again();
            });
            if (st.ShowBorder)
            {
                SliderRow(p, "Thickness", 1, 8, delegate { return Math.Max(1, st.BorderThickness); }, delegate (double v) { st.BorderThickness = v; changed(); });
                SliderRow(p, "Opacity (%)", 0, 100, delegate { return st.BorderOpacity; }, delegate (double v) { st.BorderOpacity = (int)v; changed(); });
                ColorRow(p, "Colour (empty = Main Accent)", delegate { return st.BorderColor; }, delegate (string v) { st.BorderColor = v ?? ""; changed(); }, true, false);
            }
        }
    }
}
