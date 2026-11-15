using System;
using System.Collections.Generic;
using System.Text.Json;

namespace vShell.Core
{
    /// <summary>How one bar layout looks: size, corners, gap from the screen edge and outline. Never which modules or where.</summary>
    public sealed class BarLook
    {
        public int Height { get; set; } = 40;
        public double CornerRadius { get; set; } = 13;
        public bool RoundAllCorners { get; set; } = false;
        /// <summary>Gap from the screen edge (a Dock always has 0, a Floating bar at least 1).</summary>
        public int EdgeMargin { get; set; } = 0;
        public double BorderThickness { get; set; } = 3;
        public int BorderOpacity { get; set; } = 65;
        /// <summary>Empty = Main Accent.</summary>
        public string BorderColor { get; set; } = "";
        /// <summary>Background blur of bars with this look ("Off", "Blur", "Acrylic"); null = as the theme.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string Blur { get; set; }
        /// <summary>Glass edge of bars with this look ("Subtle", "Strong"); null = off.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string GlassEdge { get; set; }
    }

    /// <summary>
    /// A bar style: a look for each bar layout. Applying it gives every bar (that follows the bar style) the look for its own layout,
    /// so one theme can hold a classic bar at the bottom and a floating bar at the top.
    /// </summary>
    public sealed class BarStyle
    {
        public string Name { get; set; } = "";
        public BarLook Classic { get; set; } = new BarLook();
        public BarLook Floating { get; set; } = new BarLook { RoundAllCorners = true, EdgeMargin = 8 };
        public BarLook Dock { get; set; } = new BarLook();
        public BarLook Minimal { get; set; } = new BarLook { CornerRadius = 0, BorderThickness = 0, BorderOpacity = 0 };
    }

    /// <summary>A widget style: corners, outline, shadow and inner spacing (not size, position, font size or row height).</summary>
    [PluginApi]
    public sealed class WidgetStyle
    {
        public string Name { get; set; } = "";
        public double CornerRadius { get; set; } = 13;
        public bool ShowBorder { get; set; } = true;
        public double BorderThickness { get; set; } = 3;
        public int BorderOpacity { get; set; } = 65;
        public string BorderColor { get; set; } = "";
        public bool ShowShadow { get; set; } = true;
        public int Padding { get; set; } = 8;
        public int Spacing { get; set; } = 4;
        /// <summary>Background blur of widgets with this style ("Off", "Blur", "Acrylic"); null = as the theme.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string Blur { get; set; }
        /// <summary>Glass edge of widgets with this style ("Subtle", "Strong"); null = off. Popups in the widget style show it too.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string GlassEdge { get; set; }
    }

    /// <summary>A theme: colours + bar style + widget style. A part left null is left as it is when the theme is applied.</summary>
    public sealed class LookTheme
    {
        public string Name { get; set; } = "";
        /// <summary>User themes: the colours themselves.</summary>
        public ThemePreset Colours { get; set; }
        /// <summary>Built-in themes: the name of a built-in colour preset (BarSettings.Presets).</summary>
        public string ColourPreset { get; set; }
        public BarStyle Bars { get; set; }
        public WidgetStyle Widgets { get; set; }
    }

    public static class Styles
    {
        public const string Classic = "Classic", Floating = "Floating", Dock = "Dock", Minimal = "Minimal";
        public static readonly string[] Layouts = { Classic, Floating, Dock, Minimal };

        /// <summary>
        /// A bar's layout, from its settings: away from the screen edge = Floating (as wide as its modules, or full width - its Full width
        /// setting); against the edge: full width with an outline = Classic, full width without = Minimal, as wide as its modules = Dock.
        /// (Classic / Minimal / Dock always have no edge gap - Tidy.)
        /// </summary>
        public static string LayoutOf(BarPlacement b)
        {
            if (b.EdgeMargin > 0) return Floating;
            if (b.FullWidth) return b.ShowBorder ? Classic : Minimal;
            return Dock;
        }

        public static BarLook LookFor(BarStyle s, string layout)
        {
            if (s == null) return new BarLook();
            switch (layout)
            {
                case Floating: return s.Floating ?? new BarLook();
                case Dock: return s.Dock ?? new BarLook();
                case Minimal: return s.Minimal ?? new BarLook();
                default: return s.Classic ?? new BarLook();
            }
        }

        public static void SetLookFor(BarStyle s, string layout, BarLook l)
        {
            switch (layout)
            {
                case Floating: s.Floating = l; break;
                case Dock: s.Dock = l; break;
                case Minimal: s.Minimal = l; break;
                default: s.Classic = l; break;
            }
        }

        /// <summary>Give a bar a layout (full width / outline / edge gap as the layout needs).</summary>
        public static void SetLayout(BarPlacement b, string layout, BarStyle style)
        {
            switch (layout)
            {
                case Minimal: b.FullWidth = true; b.ShowBorder = false; break;
                // Floating keeps the width the bar had: from Classic / Minimal it's full width (Three zones and all), from Dock as wide as its modules
                case Floating: if (b.EdgeMargin <= 0) b.EdgeMargin = 8; break;
                case Dock: b.FullWidth = false; b.EdgeMargin = 0; break;
                default: b.FullWidth = true; b.ShowBorder = true; break;
            }
            Tidy(b);
            if (b.FollowStyle && style != null) ApplyLook(b, LookFor(style, layout), layout);
        }

        /// <summary>
        /// Settings a layout doesn't have are switched off, so nothing hidden on the bar's page still changes the bar: only a Floating bar
        /// rounds all four corners; only Floating has a gap from the screen edge; Classic always has an outline, Minimal never.
        /// </summary>
        public static void Tidy(BarPlacement b)
        {
            string l = LayoutOf(b);
            if (l != Floating) b.RoundAllCorners = false;
            if (l != Floating) b.EdgeMargin = 0;
            if (l == Classic && b.BorderThickness <= 0) b.BorderThickness = 1;
        }

        /// <summary>Copy a look onto a bar, keeping the bar's layout what it is.</summary>
        public static void ApplyLook(BarPlacement b, BarLook l, string layout)
        {
            if (l == null) return;
            b.BarHeight = Math.Max(30, l.Height);
            b.CornerRadius = l.CornerRadius;
            b.RoundAllCorners = layout == Floating && l.RoundAllCorners;
            b.BorderThickness = l.BorderThickness;
            b.BorderOpacity = l.BorderOpacity;
            b.BorderColor = l.BorderColor ?? "";
            b.Blur = OwnBlur(l.Blur);
            b.GlassEdge = OwnGlass(l.GlassEdge);
            switch (layout)
            {
                case Minimal: b.FullWidth = true; b.ShowBorder = false; b.EdgeMargin = 0; break;
                case Floating: b.ShowBorder = l.BorderThickness > 0; b.EdgeMargin = Math.Max(1, l.EdgeMargin); break;     // its own Full width stays
                case Dock: b.FullWidth = false; b.ShowBorder = l.BorderThickness > 0; b.EdgeMargin = 0; break;
                default: b.FullWidth = true; b.ShowBorder = true; b.EdgeMargin = 0; if (b.BorderThickness <= 0) b.BorderThickness = 1; break;
            }
        }

        public static BarLook Capture(BarPlacement b)
        {
            return new BarLook
            {
                Height = b.BarHeight, CornerRadius = b.CornerRadius, RoundAllCorners = b.RoundAllCorners, EdgeMargin = b.EdgeMargin,
                BorderThickness = b.ShowBorder ? b.BorderThickness : 0, BorderOpacity = b.BorderOpacity, BorderColor = b.BorderColor ?? "",
                Blur = StyleBlur(b.Blur), GlassEdge = StyleGlass(b.GlassEdge)
            };
        }

        public static void ApplyWidget(WidgetSettings w, WidgetStyle s)
        {
            if (s == null) return;
            w.CornerRadius = s.CornerRadius;
            w.ShowBorder = s.ShowBorder;
            w.BorderThickness = s.BorderThickness;
            w.BorderOpacity = s.BorderOpacity;
            w.BorderColor = s.BorderColor ?? "";
            w.ShowShadow = s.ShowShadow;
            w.Padding = s.Padding;
            w.Spacing = s.Spacing;
            w.Blur = OwnBlur(s.Blur);
            w.GlassEdge = OwnGlass(s.GlassEdge);
        }

        /// <summary>A style's blur (null = as the theme) as a bar's / widget's own setting, and back.</summary>
        static string OwnBlur(string styleBlur) { return styleBlur == "Off" || styleBlur == "Blur" || styleBlur == "Acrylic" ? styleBlur : "Theme"; }
        static string StyleBlur(string own) { return own == "Off" || own == "Blur" || own == "Acrylic" ? own : null; }
        /// <summary>A style's glass edge (null = off) as a bar's / widget's own setting, and back.</summary>
        static string OwnGlass(string styleGlass) { return styleGlass == "Subtle" || styleGlass == "Strong" ? styleGlass : "Off"; }
        static string StyleGlass(string own) { return own == "Subtle" || own == "Strong" ? own : null; }
        /// <summary>The glass edge popups in this widget style show ("Off", "Subtle", "Strong").</summary>
        public static string GlassOf(WidgetStyle s) { return s == null ? "Off" : OwnGlass(s.GlassEdge); }

        public static WidgetStyle CaptureWidget(WidgetSettings w, string name)
        {
            return new WidgetStyle
            {
                Name = name ?? "", CornerRadius = w.CornerRadius, ShowBorder = w.ShowBorder, BorderThickness = w.BorderThickness, BorderOpacity = w.BorderOpacity,
                BorderColor = w.BorderColor ?? "", ShowShadow = w.ShowShadow, Padding = w.Padding, Spacing = w.Spacing, Blur = StyleBlur(w.Blur),
                GlassEdge = StyleGlass(w.GlassEdge)
            };
        }

        /// <summary>
        /// The bar style your bars show now, one look per layout: the current style's look where a bar of that layout follows it, otherwise
        /// the first bar of that layout, otherwise the current style's look. preferred = this bar's look wins for its own layout.
        /// </summary>
        public static BarStyle CaptureFromBars(BarSettings s, BarPlacement preferred)
        {
            BarStyle st = Clone(s.BarStyle) ?? Clone(BuiltInBarStyles[0]);
            Fix(st);
            foreach (string lay in Layouts)
            {
                if (preferred != null && LayoutOf(preferred) == lay) { SetLookFor(st, lay, Capture(preferred)); continue; }
                bool following = false;
                BarPlacement first = null;
                foreach (BarPlacement b in s.Bars)
                {
                    if (b == null || LayoutOf(b) != lay) continue;
                    if (b.FollowStyle) following = true;
                    if (first == null) first = b;
                }
                if (!following && first != null) SetLookFor(st, lay, Capture(first));
            }
            st.Name = SameIgnoringName(st, s.BarStyle) && s.BarStyle != null ? s.BarStyle.Name ?? "" : "";
            return st;
        }

        /// <summary>The widget style your widgets show now: the current style if any widget follows it, else the first widget's look.</summary>
        public static WidgetStyle CaptureFromWidgets(BarSettings s)
        {
            foreach (WidgetSettings w in s.Widgets) if (w != null && w.FollowStyle) return Clone(s.WidgetStyle);
            foreach (WidgetSettings w in s.Widgets)
                if (w != null)
                {
                    WidgetStyle c = CaptureWidget(w, "");
                    if (SameIgnoringName(c, s.WidgetStyle)) c.Name = s.WidgetStyle.Name ?? "";
                    return c;
                }
            return Clone(s.WidgetStyle);
        }

        static bool SameIgnoringName(BarStyle a, BarStyle b)
        {
            if (a == null || b == null) return a == b;
            BarStyle x = Clone(a), y = Clone(b);
            x.Name = ""; y.Name = "";
            return Json(x) == Json(y);
        }

        static bool SameIgnoringName(WidgetStyle a, WidgetStyle b)
        {
            if (a == null || b == null) return a == b;
            return Json(WithName(a, "")) == Json(WithName(b, ""));
        }

        /// <summary>The current bar style onto every bar that follows it.</summary>
        public static void ApplyBars(BarSettings s)
        {
            foreach (BarPlacement b in s.Bars)
                if (b != null && b.FollowStyle) { string l = LayoutOf(b); ApplyLook(b, LookFor(s.BarStyle, l), l); }
        }

        /// <summary>The current widget style onto every widget that follows it.</summary>
        public static void ApplyWidgets(BarSettings s)
        {
            foreach (WidgetSettings w in s.Widgets)
                if (w != null && w.FollowStyle) ApplyWidget(w, s.WidgetStyle);
        }

        public static T Clone<T>(T o) where T : class
        {
            if (o == null) return null;
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(o));
        }

        public static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == b;
            return JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
        }

        static string Json(object o) { return JsonSerializer.Serialize(o); }

        static bool SameLook(BarLook a, BarLook b)
        {
            var x = Clone(a); var y = Clone(b);
            return Json(x) == Json(y);
        }

        /// <summary>
        /// Settings from before styles existed: make the current bar / widget style from the bars and widgets as they are, and let any
        /// bar / widget that looks different from that keep its own look (so nothing changes on screen).
        /// </summary>
        public static void Migrate(BarSettings s)
        {
            foreach (BarPlacement b in s.Bars) if (b != null) Tidy(b);
            if (s.BarStyle == null)
            {
                BarStyle st = Clone(BuiltInBarStyles[0]);
                st.Name = "";
                var seen = new HashSet<string>();
                foreach (BarPlacement b in s.Bars)
                {
                    if (b == null) continue;
                    string l = LayoutOf(b);
                    if (seen.Add(l)) SetLookFor(st, l, Capture(b));
                }
                s.BarStyle = st;
                foreach (BarPlacement b in s.Bars)
                {
                    if (b == null) continue;
                    string l = LayoutOf(b);
                    b.FollowStyle = SameLook(Capture(b), LookFor(st, l));
                }
            }
            if (s.WidgetStyle == null)
            {
                WidgetStyle ws = s.Widgets.Count > 0 && s.Widgets[0] != null ? CaptureWidget(s.Widgets[0], "") : Clone(BuiltInWidgetStyles[0]);
                ws.Name = s.Widgets.Count > 0 ? "" : ws.Name;
                s.WidgetStyle = ws;
                foreach (WidgetSettings w in s.Widgets)
                    if (w != null) w.FollowStyle = Json(CaptureWidget(w, "")) == Json(Clone(WithName(ws, "")));
            }
            Fix(s.BarStyle);
            if (s.WidgetStyle.Name == null) s.WidgetStyle.Name = "";
            s.BarStyleName = s.BarStyle.Name;
            s.WidgetStyleName = s.WidgetStyle.Name;
            s.UserBarStyles.RemoveAll(delegate (BarStyle b) { return b == null || string.IsNullOrWhiteSpace(b.Name); });
            foreach (BarStyle b in s.UserBarStyles) Fix(b);
            s.UserWidgetStyles.RemoveAll(delegate (WidgetStyle w) { return w == null || string.IsNullOrWhiteSpace(w.Name); });
            s.UserThemes.RemoveAll(delegate (LookTheme t) { return t == null || string.IsNullOrWhiteSpace(t.Name) || (t.Colours == null && t.Bars == null && t.Widgets == null && string.IsNullOrEmpty(t.ColourPreset)); });
            foreach (LookTheme t in s.UserThemes) if (t.Bars != null) Fix(t.Bars);
        }

        /// <summary>Fill in any layout a bar style (e.g. from an older or hand-edited file) is missing.</summary>
        public static void Fix(BarStyle st)
        {
            if (st == null) return;
            if (st.Name == null) st.Name = "";
            if (st.Classic == null) st.Classic = new BarLook();
            if (st.Floating == null) st.Floating = new BarLook { RoundAllCorners = true, EdgeMargin = 8 };
            if (st.Dock == null) st.Dock = new BarLook();
            if (st.Minimal == null) st.Minimal = new BarLook { CornerRadius = 0, BorderThickness = 0, BorderOpacity = 0 };
        }

        public static LookTheme FindBuiltInTheme(string name)
        {
            foreach (var t in BuiltInThemes) if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        static WidgetStyle WithName(WidgetStyle w, string name) { var c = Clone(w); c.Name = name; return c; }

        // =================================================================
        // Built-in presets
        // =================================================================
        static BarLook L(int h, double r, bool all, int margin, double th, int op, string colour = "", string glass = null)
        {
            return new BarLook { Height = h, CornerRadius = r, RoundAllCorners = all, EdgeMargin = margin, BorderThickness = th, BorderOpacity = op, BorderColor = colour, GlassEdge = glass };
        }

        static WidgetStyle W(string name, double r, bool border, double th, int op, bool shadow, int pad, int gap, string colour = "", string glass = null)
        {
            return new WidgetStyle { Name = name, CornerRadius = r, ShowBorder = border, BorderThickness = th, BorderOpacity = op, ShowShadow = shadow, Padding = pad, Spacing = gap, BorderColor = colour, GlassEdge = glass };
        }

        // Outline colours are theme slots ("@secondary", "@text") or empty (= Main Accent), so every style works with any colours.
        //                                              height, radius, all corners, edge gap, outline thickness, outline opacity (, colour, glass edge)
        public static readonly BarStyle[] BuiltInBarStyles =
        {
            new BarStyle { Name = "Soft",     Classic = L(40, 13, false, 0, 3, 65, "", "Subtle"),  Floating = L(40, 13, true, 8, 3, 65, "", "Subtle"),   Dock = L(40, 13, false, 0, 3, 65, "", "Subtle"),  Minimal = L(38, 0, false, 0, 0, 0) },
            new BarStyle { Name = "Sharp",    Classic = L(38, 0, false, 0, 2, 100),  Floating = L(38, 4, true, 8, 2, 100),   Dock = L(38, 0, false, 0, 2, 100),  Minimal = L(34, 0, false, 0, 0, 0) },
            new BarStyle { Name = "Round",    Classic = L(42, 20, false, 0, 2, 50, "", "Subtle"),  Floating = L(42, 21, true, 10, 2, 50, "", "Subtle"),  Dock = L(42, 20, false, 0, 2, 50, "", "Subtle"),  Minimal = L(40, 20, false, 0, 0, 0) },
            new BarStyle { Name = "Slim",     Classic = L(30, 8, false, 0, 1, 45),   Floating = L(30, 15, true, 6, 1, 45),   Dock = L(30, 10, false, 0, 1, 45),  Minimal = L(30, 0, false, 0, 0, 0) },
            new BarStyle { Name = "Bold",     Classic = L(48, 16, false, 0, 4, 90),  Floating = L(48, 16, true, 12, 4, 90),  Dock = L(48, 16, false, 0, 4, 90),  Minimal = L(46, 0, false, 0, 0, 0) },
            // one crisp light line
            new BarStyle { Name = "Hairline", Classic = L(34, 0, false, 0, 1, 80, "@text"), Floating = L(34, 7, true, 6, 1, 70, "@text"), Dock = L(34, 8, false, 0, 1, 70, "@text"), Minimal = L(32, 0, false, 0, 0, 0) },
            // a bright outline in the Secondary Accent
            new BarStyle { Name = "Neon",     Classic = L(40, 10, false, 0, 2, 100, "@secondary"), Floating = L(40, 12, true, 10, 2, 100, "@secondary"), Dock = L(40, 12, false, 0, 2, 100, "@secondary"), Minimal = L(38, 0, false, 0, 0, 0) },
            // barely there: almost no outline (floating and dock none) - a strong glass edge gives it its shape
            new BarStyle { Name = "Ghost",    Classic = L(38, 12, false, 0, 1, 18, "", "Strong"),  Floating = L(38, 19, true, 8, 0, 0, "", "Strong"),    Dock = L(38, 12, false, 0, 0, 0, "", "Strong"),   Minimal = L(36, 0, false, 0, 0, 0, "", "Subtle") },
            // chunky outline in the text colour, small corners
            new BarStyle { Name = "Retro",    Classic = L(44, 5, false, 0, 3, 100, "@text"), Floating = L(44, 6, true, 10, 3, 100, "@text"), Dock = L(44, 6, false, 0, 3, 100, "@text"), Minimal = L(42, 0, false, 0, 0, 0) },
            // ---- tall bars (50 - 60): room for M / L text, big icons and graphs ----
            // like Soft, taller
            new BarStyle { Name = "Tall",     Classic = L(52, 14, false, 0, 3, 65, "", "Subtle"),  Floating = L(52, 16, true, 10, 3, 65, "", "Subtle"),  Dock = L(52, 16, false, 0, 3, 65, "", "Subtle"),  Minimal = L(50, 0, false, 0, 0, 0) },
            // a square-cornered panel with a crisp outline
            new BarStyle { Name = "Panel",    Classic = L(56, 0, false, 0, 2, 100), Floating = L(56, 6, true, 12, 2, 100), Dock = L(56, 6, false, 0, 2, 100), Minimal = L(54, 0, false, 0, 0, 0) },
            // fully rounded ends (a pill when floating)
            new BarStyle { Name = "Pill",     Classic = L(58, 24, false, 0, 2, 55, "", "Subtle"),  Floating = L(58, 29, true, 12, 2, 55, "", "Subtle"),  Dock = L(58, 29, false, 0, 2, 55, "", "Subtle"),  Minimal = L(56, 0, false, 0, 0, 0) },
        };

        //                                                           radius, border, thickness, opacity, shadow, padding, spacing (, colour, glass edge)
        public static readonly WidgetStyle[] BuiltInWidgetStyles =
        {
            W("Soft card",        13, true,  3, 65,  true,  8,  4, "", "Subtle"),
            W("Outlined",         6,  true,  2, 100, false, 10, 4),
            W("Borderless glass", 16, false, 2, 50,  false, 10, 6, "", "Strong"),
            W("Sharp",            0,  true,  1, 60,  true,  8,  4),
            W("Bubble",           24, false, 2, 50,  true,  12, 6, "", "Subtle"),
            W("Neon",             10, true,  2, 100, false, 10, 4, "@secondary"),
            W("Retro",            4,  true,  3, 100, true,  10, 4, "@text"),
        };

        static LookTheme T(string name, string colours, string bars, string widgets)
        {
            return new LookTheme { Name = name, ColourPreset = colours, Bars = FindBuiltInBar(bars), Widgets = FindBuiltInWidget(widgets) };
        }

        public static BarStyle FindBuiltInBar(string name)
        {
            foreach (var b in BuiltInBarStyles) if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        public static WidgetStyle FindBuiltInWidget(string name)
        {
            foreach (var w in BuiltInWidgetStyles) if (string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        /// <summary>A theme for every colour preset, each with the bar and widget style that suits it.</summary>
        public static readonly LookTheme[] BuiltInThemes =
        {
            T("Mocha Soft",          "Mocha (dark)",  "Soft",     "Soft card"),
            T("Midnight Ghost",      "Midnight",      "Ghost",    "Borderless glass"),
            T("Nord Hairline",       "Nord",          "Hairline", "Outlined"),
            T("Light Round",         "Light",         "Round",    "Soft card"),
            T("Glass",               "Glass",         "Round",    "Borderless glass"),
            T("High Contrast Bold",  "High contrast", "Bold",     "Outlined"),
            T("Cyberpunk Neon",      "Cyberpunk",     "Neon",     "Neon"),
            T("Neon 2077",           "Neon 2077",     "Sharp",    "Neon"),
            T("Synthwave Glow",      "Synthwave '84", "Round",    "Neon"),
            T("Matrix Sharp",        "Matrix",        "Sharp",    "Outlined"),
            T("Dracula Bold",        "Dracula",       "Bold",     "Soft card"),
            T("Tokyo Night Slim",    "Tokyo Night",   "Slim",     "Outlined"),
            T("Gruvbox Retro",       "Gruvbox Dark",  "Retro",    "Retro"),
            T("Rose Pine Round",     "Rose Pine",     "Round",    "Bubble"),
            T("Everforest Soft",     "Everforest",    "Soft",     "Bubble"),
            T("Kanagawa Round",      "Kanagawa",      "Round",    "Soft card"),
            T("Oceanic Slim",        "Oceanic",       "Slim",     "Borderless glass"),
            T("Blood Moon Sharp",    "Blood Moon",    "Sharp",    "Sharp"),
            T("AMOLED Hairline",     "AMOLED Black",  "Hairline", "Sharp"),
            // ---- 0.38.143: the presets added since, six of them with tall bars (Tall 52, Panel 56, Pill 58) ----
            T("Outrun Pill",         "Outrun",           "Pill",     "Neon"),
            T("Sunset Tall",         "Sunset",           "Tall",     "Bubble"),
            T("Amber CRT Retro",     "Amber CRT",        "Retro",    "Retro"),
            T("Latte Round",         "Catppuccin Latte", "Round",    "Soft card"),
            T("Paper Hairline",      "Paper",            "Hairline", "Outlined"),
            T("Sakura Pill",         "Sakura",           "Pill",     "Bubble"),
            T("Arctic Ghost",        "Arctic",           "Ghost",    "Borderless glass"),
            T("Warm Sunset Soft",    "Warm Sunset",      "Soft",     "Bubble"),
            T("Windows Dark Panel",  "Windows Dark",     "Panel",    "Sharp"),
            T("GitHub Dark Slim",    "GitHub Dark",      "Slim",     "Outlined"),
            T("Monokai Sharp",       "Monokai",          "Sharp",    "Sharp"),
            T("Forest Tall",         "Forest",           "Tall",     "Soft card"),
            T("Monochrome Ghost",    "Monochrome",       "Ghost",    "Borderless glass"),
            T("Deep Purple Bold",    "Deep Purple",      "Bold",     "Bubble"),
            T("Carbon Teal Panel",   "Carbon Teal",      "Panel",    "Outlined"),
            T("Cosmic Blue Neon",    "Cosmic Blue",      "Neon",     "Neon"),
        };
    }
}
