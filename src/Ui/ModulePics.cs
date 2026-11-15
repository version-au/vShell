using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Small drawn pictures of the modules for the Add a module gallery: how each one looks on a bar (one line) and in a widget, in the
    /// theme's colours with made-up sample values. Pictures only - nothing in them runs.
    /// </summary>
    public static class ModulePics
    {
        // ---- bits ----
        [PluginApi] public static TextBlock T(Theme t, string text, Brush b, double size, bool bold = false)
        {
            return new TextBlock { Text = text, Foreground = b, FontFamily = t.Font, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        }

        [PluginApi] public static TextBlock G(int code, Brush b, double size)
        {
            return new TextBlock { Text = ((char)code).ToString(), FontFamily = IconUtil.GlyphFont, FontSize = size, Foreground = b, VerticalAlignment = VerticalAlignment.Center };
        }

        [PluginApi] public static StackPanel Row(double gap, params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < items.Length; i++)
            {
                var fe = items[i] as FrameworkElement;
                if (fe != null && i > 0) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(items[i]);
            }
            return sp;
        }

        [PluginApi] public static StackPanel Col(double gap, params UIElement[] items)
        {
            var sp = new StackPanel();
            for (int i = 0; i < items.Length; i++)
            {
                var fe = items[i] as FrameworkElement;
                if (fe != null && i > 0) fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
                sp.Children.Add(items[i]);
            }
            return sp;
        }

        [PluginApi] public static Shape Spark(double w, double h, Brush b, double[] ys, bool fill = false)
        {
            var pts = new PointCollection();
            for (int i = 0; i < ys.Length; i++) pts.Add(new Point(w * i / (ys.Length - 1), h - ys[i] * h));
            if (!fill) return new Polyline { Points = pts, Stroke = b, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center };
            pts.Insert(0, new Point(0, h));
            pts.Add(new Point(w, h));
            var c = (b as SolidColorBrush) != null ? ((SolidColorBrush)b).Color : Colors.SteelBlue;
            return new Polygon { Points = pts, Fill = new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B)), Stroke = b, StrokeThickness = 1.5, Width = w, Height = h };
        }

        [PluginApi] public static readonly double[] Wave1 = { 0.25, 0.32, 0.22, 0.55, 0.45, 0.7, 0.5, 0.82, 0.62, 0.74 };
        [PluginApi] public static readonly double[] Wave2 = { 0.2, 0.28, 0.4, 0.3, 0.52, 0.48, 0.6, 0.42, 0.66, 0.58 };

        [PluginApi] public static Border Meter(Theme t, double w, double h, double frac, Brush b)
        {
            var g = new Grid { Width = w, Height = h };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(h / 2), Background = t.Track });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(h / 2), Background = b, Width = Math.Max(h, w * frac), HorizontalAlignment = HorizontalAlignment.Left });
            return new Border { Child = g, VerticalAlignment = VerticalAlignment.Center };
        }

        [PluginApi] public static Border Dot(Brush b, double d) { return new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = b, VerticalAlignment = VerticalAlignment.Center }; }

        [PluginApi] public static Border Square(Brush b, double s, double r = 4) { return new Border { Width = s, Height = s, CornerRadius = new CornerRadius(r), Background = b, VerticalAlignment = VerticalAlignment.Center }; }

        [PluginApi] public static Brush Shade(Theme t, int alpha) { return Theme.Faded(t.Fg, alpha); }

        [PluginApi] public static Grid Badge(UIElement under, string text, Brush bg, double fs)
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var fe = under as FrameworkElement;
            if (fe != null) fe.Margin = new Thickness(0, 0, 7, 0);
            g.Children.Add(under);
            g.Children.Add(new Border
            {
                Background = bg, CornerRadius = new CornerRadius(fs), MinWidth = fs * 1.6, Height = fs * 1.6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -fs * 0.5, -fs * 0.3, 0),
                Child = new TextBlock { Text = text, FontSize = fs, FontWeight = FontWeights.Bold, Foreground = NoteRender.Contrast(bg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            return g;
        }

        static FrameworkElement Sun(Theme t, double s)
        {
            var g = new Grid { Width = s, Height = s, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Width = s * 0.55, Height = s * 0.55, Fill = StatusColors.Warn });
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                g.Children.Add(new Line
                {
                    X1 = s / 2 + Math.Cos(a) * s * 0.36, Y1 = s / 2 + Math.Sin(a) * s * 0.36, X2 = s / 2 + Math.Cos(a) * s * 0.48, Y2 = s / 2 + Math.Sin(a) * s * 0.48,
                    Stroke = StatusColors.Warn, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                });
            }
            return g;
        }

        // =================================================================
        // On a bar (one line)
        // =================================================================
        public static FrameworkElement Bar(string type, Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent, sec = t.SecondaryAccent;
            double fs = 12.5;
            switch (type)
            {
                case ModuleTypes.StartMenu: return Row(6, new VShellMark { Dot1 = acc, Dot2 = sec, Dot3 = fg, Dim = 20, VerticalAlignment = VerticalAlignment.Center }, T(t, "Start", fg, fs));
                case ModuleTypes.Launcher: return Row(6, Square(acc, 18), Square(StatusColors.Good, 18), Square(StatusColors.Warn, 18), Square(sec, 18));
                case ModuleTypes.RunBox:
                    return new Border
                    {
                        CornerRadius = new CornerRadius(6), Background = t.Track, Padding = new Thickness(8, 3, 26, 3), VerticalAlignment = VerticalAlignment.Center,
                        Child = Row(6, G(0xE721, dim, 12), T(t, "Run, search, !s …", dim, 11.5))
                    };
                case ModuleTypes.Taskbar:
                    {
                        var r = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        Brush[] c = { acc, StatusColors.Warn, StatusColors.Good, sec };
                        for (int i = 0; i < 4; i++)
                        {
                            var cell = new Grid { Width = 30, Height = 28, Margin = new Thickness(1, 0, 1, 0) };
                            if (i == 0) cell.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = t.ActiveBg });
                            cell.Children.Add(Square(c[i], 15, 3));
                            cell.Children.Add(new Border { Height = 2.5, Width = i == 0 ? 14 : 5, CornerRadius = new CornerRadius(1.25), Background = i == 0 ? acc : Shade(t, 90), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center });
                            r.Children.Add(cell);
                        }
                        return r;
                    }
                case ModuleTypes.Tray: return Row(9, G(0xE70E, dim, 11), G(0xE701, fg, 14), G(0xE767, fg, 14), G(0xE83F, fg, 14));
                case ModuleTypes.Notifications: return Badge(G(0xEA8F, fg, 16), "3", StatusColors.Bad, 8);
                case ModuleTypes.Spacer:
                    return new Border
                    {
                        BorderBrush = Shade(t, 70), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 2, 14, 2),
                        VerticalAlignment = VerticalAlignment.Center, Child = T(t, "⟷  space", dim, 11.5)
                    };
                case ModuleTypes.Cpu: return Row(7, T(t, "CPU", dim, fs), Spark(64, 18, acc, Wave1), T(t, "23%", fg, fs));
                case ModuleTypes.Memory: return Row(7, T(t, "RAM", dim, fs), Meter(t, 56, 7, 0.62, acc), T(t, "9.8 GB", fg, fs));
                case ModuleTypes.Gpu: return Row(7, T(t, "GPU", dim, fs), Spark(64, 18, acc, Wave2), T(t, "41%", fg, fs));
                case ModuleTypes.Disk: return Row(7, T(t, "C:", dim, fs), Meter(t, 56, 7, 0.48, acc), T(t, "238 GB free", fg, fs));
                case ModuleTypes.Drives: return Row(6, T(t, "C:", dim, fs), Meter(t, 30, 6, 0.48, acc), T(t, "D:", dim, fs), Meter(t, 30, 6, 0.93, StatusColors.Warn), T(t, "S:", dim, fs), Meter(t, 30, 6, 0.3, acc));
                case ModuleTypes.Network: return Row(9, T(t, "↓ 4.2 MB/s", StatusColors.Good, fs), T(t, "↑ 210 KB/s", sec, fs));
                case ModuleTypes.Hwinfo: return Row(6, T(t, "CPU", dim, fs), T(t, "54 °C", fg, fs));
                case ModuleTypes.Processes: return Row(6, T(t, "Top", dim, fs), T(t, "chrome 12%", fg, fs));
                case ModuleTypes.Clock: return Row(8, T(t, "2:05 PM", fg, 14, true), T(t, "Sun 18 Oct", sec, fs));
                case ModuleTypes.Weather: return Row(6, Sun(t, 20), T(t, "24°", fg, 13.5), T(t, "Sunny", dim, fs));
                case ModuleTypes.NowPlaying:
                    return Row(8, Square(Shade(t, 60), 24, 4), Col(3, T(t, "Midnight City · M83", fg, 11.5), Meter(t, 110, 2.5, 0.4, acc)), T(t, "⏮  ⏸  ⏭", dim, 11));
                case ModuleTypes.Battery: return Row(5, G(0xE83F, fg, 15), T(t, "87%", fg, fs));
                case ModuleTypes.Volume: return Row(5, G(0xE767, fg, 15), T(t, "64%", fg, fs));
                case ModuleTypes.Uptime: return Row(6, T(t, "Up", dim, fs), T(t, "3d 4h", fg, fs));
                case ModuleTypes.Label: return T(t, "Your text", fg, fs);
                case ModuleTypes.Services: return Row(5, Dot(StatusColors.Good, 8), T(t, "Router", fg, 11.5), Dot(StatusColors.Good, 8), T(t, "NAS", fg, 11.5), Dot(StatusColors.Bad, 8), T(t, "Plex", fg, 11.5));
                case ModuleTypes.Notes: return Row(6, G(0xE70B, acc, 14), T(t, "Shopping", fg, fs));
                case ModuleTypes.Terminal: return Row(6, G(0xE756, fg, 14), T(t, "PS C:\\> _", dim, fs));
                default:
                    {
                        PluginModule pm = PluginManager.ModuleOf(type);
                        if (pm != null && pm.BarPicture != null) return pm.BarPicture(t);
                        return T(t, ModuleTypes.Display(type), fg, fs);
                    }
            }
        }

        // =================================================================
        // In a widget
        // =================================================================
        public static FrameworkElement Widget(string type, Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent, sec = t.SecondaryAccent;
            switch (type)
            {
                case ModuleTypes.Cpu:
                case ModuleTypes.Gpu:
                case ModuleTypes.Memory:
                    {
                        string name = type == ModuleTypes.Cpu ? "CPU" : type == ModuleTypes.Gpu ? "GPU" : "Memory";
                        string val = type == ModuleTypes.Cpu ? "23%" : type == ModuleTypes.Gpu ? "41%" : "9.8 / 32 GB";
                        var head = new DockPanel();
                        var v = T(t, val, fg, 13, true);
                        DockPanel.SetDock(v, Dock.Right);
                        head.Children.Add(v);
                        head.Children.Add(T(t, name, dim, 12.5));
                        return Col(8, head, Spark(176, 70, acc, type == ModuleTypes.Gpu ? Wave2 : Wave1, true));
                    }
                case ModuleTypes.Network:
                    {
                        var g = new Grid { Width = 176, Height = 70 };
                        g.Children.Add(Spark(176, 70, StatusColors.Good, Wave1, true));
                        g.Children.Add(Spark(176, 70, sec, new[] { 0.1, 0.12, 0.08, 0.2, 0.15, 0.18, 0.1, 0.24, 0.16, 0.2 }));
                        return Col(8, Row(12, T(t, "↓ 4.2 MB/s", StatusColors.Good, 12.5), T(t, "↑ 210 KB/s", sec, 12.5)), g);
                    }
                case ModuleTypes.Disk:
                    return Col(9, Row(8, T(t, "C:", dim, 12), Meter(t, 120, 7, 0.48, acc)), Row(8, T(t, "D:", dim, 12), Meter(t, 120, 7, 0.83, StatusColors.Warn)),
                               Row(8, T(t, "E:", dim, 12), Meter(t, 120, 7, 0.21, acc)));
                case ModuleTypes.Drives:
                    return Col(8, T(t, "This PC", fg, 13, true),
                               Col(3, Row(8, T(t, "Local Disk (C:)", fg, 12), T(t, "238 GB free", dim, 11)), Meter(t, 170, 6, 0.48, acc)),
                               Col(3, Row(8, T(t, "Data (D:)", fg, 12), T(t, "96 GB free", dim, 11)), Meter(t, 170, 6, 0.93, StatusColors.Warn)),
                               Col(3, Row(8, T(t, "storage (S:)", fg, 12), T(t, "10 TB free", dim, 11)), Meter(t, 170, 6, 0.3, acc)));
                case ModuleTypes.Clock:
                    return Col(2, T(t, "2:05 PM", fg, 32, true), T(t, "Sunday 18 October", sec, 13));
                case ModuleTypes.Weather:
                    {
                        var days = new UniformGrid4(t, new[] { "Mon", "Tue", "Wed", "Thu" }, new[] { "26°", "22°", "19°", "23°" });
                        return Col(12, Row(10, Sun(t, 34), Col(0, T(t, "24°", fg, 24, true), T(t, "Sunny · Sydney", dim, 11.5))), days);
                    }
                case ModuleTypes.NowPlaying:
                    return Row(10, Square(Shade(t, 60), 64, 6),
                               Col(4, T(t, "Midnight City", fg, 13, true), T(t, "M83", dim, 11.5), Meter(t, 100, 3, 0.4, acc), T(t, "⏮   ⏸   ⏭", fg, 12)));
                case ModuleTypes.Notes:
                    return Col(6, T(t, "Shopping", fg, 13, true), Row(7, Square(acc, 12, 3), T(t, "Milk", dim, 12)), Row(7, Box(t), T(t, "Coffee beans", fg, 12)),
                               Row(7, Box(t), T(t, "Batteries (AA)", fg, 12)), Row(7, Box(t), T(t, "Bin bags", fg, 12)));
                case ModuleTypes.Services:
                    return Col(6, Svc(t, "Router", "4 ms", StatusColors.Good), Svc(t, "NAS", "12 ms", StatusColors.Good), Svc(t, "Plex", "down", StatusColors.Bad), Svc(t, "Home Assistant", "38 ms", StatusColors.Good));
                case ModuleTypes.Notifications:
                    return Col(7, T(t, "Notifications", fg, 12.5, true), Note(t, StatusColors.Good, "✓ ubuntu.iso", "Downloads · 5 min ago"),
                               Note(t, StatusColors.Warn, "New request: Dune", "Seerr · 1 hr ago"), Note(t, StatusColors.Bad, "immich is down", "Komodo · Yesterday"));
                case ModuleTypes.Processes:
                    return Col(5, Pair(t, "chrome", "12%"), Pair(t, "Code", "6%"), Pair(t, "vShell", "1%"), Pair(t, "explorer", "1%"));
                case ModuleTypes.Launcher:
                    {
                        Brush[] c = { acc, StatusColors.Good, StatusColors.Warn, sec, StatusColors.Bad, acc, StatusColors.Good, sec };
                        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Width = 160 };
                        foreach (Brush b in c) { var s = Square(b, 28, 6); s.Margin = new Thickness(4); grid.Children.Add(s); }
                        return grid;
                    }
                default:
                    {
                        PluginModule pm = PluginManager.ModuleOf(type);
                        if (pm != null && pm.WidgetPicture != null) return pm.WidgetPicture(t);
                        // the bar picture, larger
                        var vb = new Viewbox { Child = Bar(type, t), Width = 176, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxHeight = 60 };
                        return vb;
                    }
            }
        }

        static Border Box(Theme t) { return new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), BorderBrush = Shade(t, 110), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center }; }

        [PluginApi] public static FrameworkElement Pair(Theme t, string a, string b)
        {
            var d = new DockPanel { Width = 176 };
            var r = T(t, b, t.Fg, 12);
            DockPanel.SetDock(r, Dock.Right);
            d.Children.Add(r);
            d.Children.Add(T(t, a, t.FgDim, 12));
            return d;
        }

        static FrameworkElement Svc(Theme t, string name, string ms, Brush b)
        {
            var d = new DockPanel { Width = 176 };
            var r = T(t, ms, b == StatusColors.Bad ? b : t.FgDim, 11.5);
            DockPanel.SetDock(r, Dock.Right);
            d.Children.Add(r);
            d.Children.Add(Row(7, Dot(b, 8), T(t, name, t.Fg, 12)));
            return d;
        }

        static FrameworkElement Note(Theme t, Brush level, string title, string sub)
        {
            var d = new DockPanel();
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = level, Margin = new Thickness(0, 1, 7, 1) };
            DockPanel.SetDock(bar, Dock.Left);
            d.Children.Add(bar);
            d.Children.Add(Col(1, T(t, title, level == StatusColors.Good ? t.Fg : level, 12), T(t, sub, t.FgDim, 10.5)));
            return d;
        }

        /// <summary>A four-day forecast row.</summary>
        sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid4(Theme t, string[] days, string[] temps)
            {
                Columns = days.Length;
                Width = 176;
                for (int i = 0; i < days.Length; i++)
                {
                    var c = Col(2, T(t, days[i], t.FgDim, 11), T(t, temps[i], t.Fg, 12));
                    foreach (UIElement e in c.Children) ((FrameworkElement)e).HorizontalAlignment = HorizontalAlignment.Center;
                    Children.Add(c);
                }
            }
        }
    }
}
