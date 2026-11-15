using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Background blur for bars and widgets: the desktop wallpaper behind each bar / widget, softened.
    ///
    /// Earlier versions asked Windows' compositor to blur whatever was behind the window. On Windows 11 that ignores the window's
    /// rounded shape (a square box showed at the corners) and the plain blur often didn't show anything at all. vShell draws the
    /// blurred wallpaper itself, under the theme colour, inside the bar's / widget's own rounded shape: smooth corners, drop shadows
    /// stay, and nothing has to be made again when it is switched on or off. The wallpaper is blurred once per monitor (small, so it is
    /// cheap) and every bar / widget shows the part of it that lies behind it.
    /// It shows the desktop wallpaper, not the windows behind (bars and widgets normally sit on the desktop or reserve their own strip).
    ///
    /// The drawing itself is the suite's (vSuite.Core's Backdrop and Glass, 0.38.171) - the same code draws vShell's Settings and
    /// vXplorer's / vView's windows, so they all look alike. vShell adds each bar's / widget's own blur setting.
    /// </summary>
    public static class Backdrop
    {
        /// <summary>Blur wanted for bars and widgets ("Blur" or "Acrylic" = Frosted).</summary>
        public static bool On(BarSettings s)
        {
            if (s == null) return false;
            if (s.BlurMode != "Off") return true;
            foreach (BarPlacement b in s.Bars) if (b != null && BarSettings.BlurFor(s, b.Blur) != "Off") return true;
            foreach (WidgetSettings w in s.Widgets) if (w != null && BarSettings.BlurFor(s, w.Blur) != "Off") return true;
            return false;
        }

        /// <summary>Call now and then (the manager's desktop timer): the pictures are made again when the wallpaper or monitors changed.</summary>
        public static void Check(BarSettings s)
        {
            if (On(s)) vSuite.Ui.Backdrop.Check();
        }
    }

    /// <summary>
    /// The blurred wallpaper behind a bar / widget / popup: put it under the background (same corner radius) and it shows the part of the
    /// monitor's blurred wallpaper that lies behind it, following the window as it moves (vSuite's Glass, with the bar's / widget's own
    /// blur setting).
    /// </summary>
    public sealed class Glass : vSuite.Ui.Glass
    {
        readonly BarManager _mgr;

        public Glass(BarManager mgr) { _mgr = mgr; }

        /// <summary>Settings changed: on / off, Blur / Frosted. own = the bar's / widget's own setting ("Theme" = the Appearance one).</summary>
        public new void Refresh(string own)
        {
            base.Refresh(BarSettings.BlurFor(_mgr.Settings, own));
        }
    }
    /// <summary>
    /// The background effect (blur / frosted) for popups: the start menu, module popups, the run box drop-down, notification cards and
    /// history. A Glass layer goes under the popup's card (same corners) and the card's background takes the theme's opacity, so the
    /// popup looks like the bar / widget it came from. Each module can turn it off for its own popup (ModuleConfig.PopupBackdrop).
    /// </summary>
    public static class PopupGlass
    {
        /// <summary>Which module an element belongs to (set on widget modules; bar modules sit in a ModuleHost).</summary>
        public static readonly DependencyProperty ModuleProperty =
            DependencyProperty.RegisterAttached("Module", typeof(ModuleConfig), typeof(PopupGlass), new PropertyMetadata(null));

        public static void SetModule(DependencyObject d, ModuleConfig cfg) { d.SetValue(ModuleProperty, cfg); }
        public static ModuleConfig GetModule(DependencyObject d) { return d == null ? null : d.GetValue(ModuleProperty) as ModuleConfig; }

        /// <summary>The module an element (a module, or something inside one) belongs to, or null.</summary>
        public static ModuleConfig ModuleOf(DependencyObject d)
        {
            int guard = 0;
            while (d != null && guard++ < 200)
            {
                var host = d as ModuleHost;
                if (host != null) return host.Config;
                ModuleConfig c = GetModule(d);
                if (c != null) return c;
                DependencyObject up = null;
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) up = VisualTreeHelper.GetParent(d);
                if (up == null) up = LogicalTreeHelper.GetParent(d);
                if (up == null)
                {
                    var fe = d as FrameworkElement;
                    if (fe != null) up = fe.Parent ?? fe.TemplatedParent;
                }
                d = up;
            }
            return null;
        }

        /// <summary>The effect a bar's / widget's popups get: its own (or the Appearance one); "Off" when there is none.</summary>
        public static string ModeFor(BarManager mgr, IModuleHost host)
        {
            BarSettings s = mgr != null ? mgr.Settings : null;
            if (s == null) return "Off";
            var bar = host as BarWindow;
            if (bar != null && bar.Placement != null) return BarSettings.BlurFor(s, bar.Placement.Blur);
            var widget = host as WidgetWindow;
            if (widget != null && widget.Cfg != null) return BarSettings.BlurFor(s, widget.Cfg.Blur);
            return BarSettings.BlurFor(s, "Theme");
        }

        /// <summary>The effect for a module's popup: the host's, unless the module turned it off for its popup.</summary>
        public static string ModeFor(BarManager mgr, IModuleHost host, FrameworkElement anchor)
        {
            ModuleConfig cfg = ModuleOf(anchor);
            if (cfg != null && !cfg.PopupBackdrop) return "Off";
            return ModeFor(mgr, host);
        }

        /// <summary>
        /// Put the effect under a card: returns the card inside a grid with a Glass under it (same margin and corners), or the card
        /// itself when mode is "Off". The card's background becomes the theme colour at the theme's opacity (a see-through card).
        /// </summary>
        public static FrameworkElement Wrap(BarManager mgr, Theme t, Border card, string mode, string glassEdge = "Off")
        {
            bool fx = !string.IsNullOrEmpty(mode) && mode != "Off";
            bool edge = glassEdge == "Subtle" || glassEdge == "Strong";
            if (card == null || (!fx && !edge)) return card;
            var g = new Grid();
            if (fx)
            {
                var glass = new Glass(mgr) { Margin = card.Margin };
                glass.SetCorners(card.CornerRadius);
                glass.Refresh(mode);
                See(t, card);
                g.Children.Add(glass);
                card.SizeChanged += delegate { glass.SetCorners(card.CornerRadius); glass.Update(); };
            }
            g.Children.Add(card);
            if (edge)
            {
                // the widget style's glass edge, over the card
                var sheen = new Border { Margin = card.Margin };
                EdgeFx.GlassEdge(sheen, glassEdge, card.CornerRadius);
                g.Children.Add(sheen);
            }
            return g;
        }

        /// <summary>The card's background at the theme's opacity (the plain popup background is almost solid, which would hide the effect).</summary>
        public static void See(Theme t, Border card)
        {
            if (t == null || card == null) return;
            if (card.Background == null || ReferenceEquals(card.Background, t.PopupBg)) card.Background = t.MakeBg(-1);
        }
    }
}
