using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("homeassistant", "Home Assistant", Description = "Your Home Assistant: the Entities (and Bubble buttons), Climate and Power flow modules, notification rules, palette commands, a Home Assistant source for the Weather module, and vDrop (a Notes store, and its notifications).",
    Glyph = "\uE80F", Group = "Smart home", Author = "version", Adds = "notification rules, palette commands, !ha in the run box, a Weather source, vDrop (a Notes store, notifications)", Types = "HomeAssistant,Climate,PowerFlow")]

namespace vShell.Ui
{
    /// <summary>
    /// The Home Assistant plugin: the connection, the Entities / Bubble, Climate and Power flow modules, the notification rules, the palette's
    /// commands, the Weather module's Home Assistant source, and vDrop (the add-on: a Notes store, and its notifications).
    /// </summary>
    public sealed class HaPlugin : IPlugin
    {
        readonly HaWeather _weather = new HaWeather();

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.HomeAssistant, Display = "Home Assistant entities", ShortName = "Home", Glyph = "", Group = "Smart home & more",
                Blurb = "entities, graphs",
                Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg)
                {
                    return HaView.Of(cfg).Bubble ? (FrameworkElement)new HaBubbleModule(mgr, h, cfg) : new HomeAssistantModule(mgr, h, cfg);
                },
                Settings = HaUi.EntitiesSettings, BarPicture = EntitiesBarPic, WidgetPicture = EntitiesPic
            });
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.Climate, Display = "Climate control", ShortName = "Climate", Glyph = "", Group = "Smart home & more",
                Blurb = "air conditioner, zones", HasPopup = true,
                Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new ClimateModule(mgr, h, cfg); },
                Settings = HaUi.ClimateSettings, BarPicture = ClimateBarPic, WidgetPicture = ClimatePic
            });
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.PowerFlow, Display = "Power flow", ShortName = "Power", Glyph = "", Group = "Smart home & more",
                Blurb = "solar, grid, battery", OnBar = false,
                Needs = Needs,
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new PowerFlowModule(mgr, h, cfg); },
                Settings = HaUi.PowerFlowSettings, BarPicture = PowerFlowBarPic, WidgetPicture = Flow
            });
            host.SetUpWhen(delegate (BarSettings s) { return HaClient.Configured; });
            host.AddIntegration("Home Assistant", HaUi.IntegrationSection);
            host.AddNotifications(HaUi.NotifySection);
            host.OnSettings(Configure);
            host.AddNotifySource("Home Assistant");
            host.AddWatcher("Home Assistant", HaRules.Watch);
            host.AddCommands("Home Assistant", HaPalette.Commands);
            // !ha kitchen: the palette's Home Assistant commands for "kitchen" (turn on / off, set, run a script …)
            host.AddBang("HomeAssistant", delegate (string key, string q, BarSettings s)
            {
                if (key != HaSettings.Of(s).Bang || !HaClient.Configured) return null;
                return new RunAction { Kind = "Palette", Query = "Home Assistant " + q, Display = "Home Assistant: " + (q.Length == 0 ? "everything" : q) };
            }, delegate { }, delegate (BarSettings s) { return HaClient.Configured ? "!" + HaSettings.Of(s).Bang + " Home Assistant" : null; });
            host.OnPaletteOpening(delegate (BarSettings s) { HaPalette.Warm(); });

            // the Weather module's Home Assistant source
            host.AddWeatherSource(_weather, HaWeather.Rows);

            // a rule's "Toggle" on its notification
            host.AddCardLinks(delegate (string url)
            {
                if (!url.StartsWith("vshell:ha-toggle:")) return false;
                string id = url.Substring(17);
                HaState st = HaClient.Get(id);
                KeyValuePair<string, string>? svc = HaClient.ClickService(id, st != null ? st.State : "");
                if (svc.HasValue) HaClient.CallService(svc.Value.Key, svc.Value.Value, id);
                return true;
            });

            // vDrop: a Notes store (the module shows vDrop's feed)
            host.AddNoteStore(new NoteFeedStore
            {
                Id = "vDrop",
                Create = delegate (ModuleConfig cfg, Action save, Action<TextBox> startTyping, Action stopTyping) { return new VDropFeed(cfg, save, startTyping, stopTyping); },
                Use = VDrop.Use, Prepare = VDrop.Prepare, Settings = HaUi.VDropSettings
            });
        }

        public void Unload()
        {
            // (vShell takes away what was registered through the host: modules, the weather source, the vDrop store, the card links …)
            VDrop.Notifications(false);
            HaClient.Configure("", "", false, 5);      // no connection: polling, the WebSocket and vDrop stop
        }

        static string Needs(BarSettings s) { return HaClient.Configured ? null : "Needs the Home Assistant connection (Plugins)"; }

        static void Configure(BarSettings s)
        {
            HaSettings h = HaSettings.Of(s);
            HaClient.Configure(h.Url, h.Token, h.IgnoreCert, h.PollSeconds);
            VDrop.Notifications(h.VDropNotifications && HaClient.Configured);     // notifications from the vDrop add-on
        }

        // ---- Add a module pictures ----
        static FrameworkElement EntitiesBarPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            double fs = 12.5;
            return ModulePics.Row(6, ModulePics.T(t, "Lounge", dim, fs), ModulePics.T(t, "21.5 °C", fg, fs), ModulePics.T(t, "Lights", dim, fs), ModulePics.T(t, "On", acc, fs));
        }

        static FrameworkElement EntitiesPic(Theme t)
        {
            return ModulePics.Col(6, ModulePics.Pair(t, "Lounge", "21.5 °C"), ModulePics.Pair(t, "Solar", "4.2 kW"), ModulePics.Pair(t, "Battery", "86 %"), ModulePics.Pair(t, "Front door", "Closed"));
        }

        static FrameworkElement PowerFlowBarPic(Theme t)
        {
            Brush fg = t.Fg;
            double fs = 12.5;
            return ModulePics.Row(6, ModulePics.T(t, "☀ 4.2 kW", StatusColors.Warn, fs), ModulePics.T(t, "⌂ 1.1 kW", fg, fs));
        }

        static FrameworkElement ClimateBarPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim;
            double fs = 12.5;
            SolidColorBrush cool = ClimateCtl.ModeBrush("cool", t);
            return ModulePics.Row(6, ClimateIcons.Circle("cool", cool, Theme.Faded(cool, 26), 20), ModulePics.T(t, "22.5°", fg, fs), ModulePics.T(t, "→ 24°", dim, fs));
        }

        /// <summary>The climate card in small, as Bubble Card draws it: the row with the cooling colour behind it, the icon circle, name and
        /// state, − 24° + in a pill; the mode / fan sub-buttons; two zone sliders.</summary>
        static FrameworkElement ClimatePic(Theme t)
        {
            SolidColorBrush cool = ClimateCtl.ModeBrush("cool", t);
            Brush card = BubbleRow.CardBrush(t);
            const double W = 196;
            Func<FrameworkElement, string, string, Brush, double, FrameworkElement> row = delegate (FrameworkElement icon, string name, string state, Brush layer, double fill)
            {
                var g = new Grid { Width = W, Height = 30 };
                g.Children.Add(new Border { Background = t.Track, CornerRadius = new CornerRadius(15) });
                if (layer != null)
                    g.Children.Add(fill >= 0 ? new Border { Background = layer, Opacity = 0.85, Width = W * fill, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(15, 0, 0, 15) }
                                             : new Border { Background = layer, Opacity = 0.55, CornerRadius = new CornerRadius(15) });
                var ic = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = card, Child = icon, Margin = new Thickness(4, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(ic);
                var words = ModulePics.Col(0, ModulePics.T(t, name, t.Fg, 9.5, true), ModulePics.T(t, state, t.FgDim, 8.5));
                words.Margin = new Thickness(31, 0, 0, 0);
                words.HorizontalAlignment = HorizontalAlignment.Left;
                g.Children.Add(words);
                return g;
            };
            var top = (Grid)row(ClimateIcons.Make("cool", t.Fg, 13), "Living room", "Cool · Cooling · 23.1°", cool, -1);
            var pill = new Border { Background = card, CornerRadius = new CornerRadius(11), Height = 22, Padding = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0),
                                    Child = ModulePics.Row(6, ModulePics.T(t, "−", t.Fg, 11), ModulePics.T(t, "24°", t.Fg, 10.5, true), ModulePics.T(t, "+", t.Fg, 11)) };
            top.Children.Add(pill);
            Func<string, string, Border> chip = delegate (string icon, string text)
            {
                return new Border { Background = t.Track, CornerRadius = new CornerRadius(10), Height = 20, Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 0, 4, 0),
                                    Child = ModulePics.Row(3, ClimateIcons.Make(icon, icon == "cool" ? (Brush)cool : t.Fg, 10), ModulePics.T(t, text, t.Fg, 9), ClimateIcons.Make("chevron", t.FgDim, 8)) };
            };
            return ModulePics.Col(5, top, ModulePics.Row(0, chip("cool", "Cool"), chip("fan", "Auto")),
                       row(ClimateIcons.Make("fan", t.Fg, 12), "Lounge", "60%", cool, 0.6),
                       row(ClimateIcons.Make("fan", t.Fg, 12), "Bedroom", "30%", cool, 0.3));
        }

        static FrameworkElement Flow(Theme t)
        {
            var c = new Canvas { Width = 176, Height = 120 };
            Action<double, double, string, string, Brush> node = delegate (double x, double y, string label, string val, Brush b)
            {
                var e = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), BorderBrush = b, BorderThickness = new Thickness(2.5),
                                     Child = ModulePics.Col(0, ModulePics.T(t, val, t.Fg, 10.5, true), ModulePics.T(t, label, t.FgDim, 9)) };
                ((StackPanel)e.Child).HorizontalAlignment = HorizontalAlignment.Center;
                ((StackPanel)e.Child).VerticalAlignment = VerticalAlignment.Center;
                Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
                c.Children.Add(e);
            };
            Action<double, double, double, double> line = delegate (double x1, double y1, double x2, double y2)
            {
                c.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ModulePics.Shade(t, 90), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 2, 2 } });
            };
            line(23, 23, 88, 60); line(153, 23, 88, 60); line(23, 97, 88, 60); line(153, 97, 88, 60);
            node(0, 0, "Solar", "4.2", StatusColors.Warn);
            node(130, 0, "Grid", "0.3", t.FgDim);
            node(0, 74, "Battery", "86%", StatusColors.Good);
            node(130, 74, "Home", "1.1", t.MainAccent);
            return c;
        }
    }
}
