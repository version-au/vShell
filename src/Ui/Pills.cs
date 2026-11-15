using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The status colours everything uses: green = fine / normal, yellow = busy / important, red = a problem / alert
    /// (notifications, Komodo, Seerr, Proxmox, downloads, service dots, the run box).
    /// </summary>
    [PluginApi]
    public static class StatusColors
    {
        public static readonly Brush Good = Frozen(0x6C, 0xCB, 0x7E);
        public static readonly Brush Warn = Frozen(0xF5, 0xC5, 0x42);
        public static readonly Brush Bad = Frozen(0xEF, 0x4D, 0x4D);
        static Brush Frozen(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        /// <summary>The colour of a notification level.</summary>
        public static Brush Of(NotifyLevel level)
        {
            switch (level) { case NotifyLevel.Alert: return Bad; case NotifyLevel.Important: return Warn; default: return Good; }
        }
    }

    /// <summary>
    /// The small buttons inside modules and popups ("Open Komodo", Approve, Request, Mark all read …), all drawn the same way and in the
    /// widgets' style: their corner rounding follows the widget style's (about half of it), with a thin outline when widgets have one.
    /// </summary>
    [PluginApi]
    public static class Pills
    {
        static WidgetStyle _ws;

        public static void Configure(BarSettings s) { _ws = s != null ? s.WidgetStyle : null; }

        /// <summary>The corner rounding of pills and chips.</summary>
        public static double Radius { get { return _ws == null ? 6 : Math.Max(2, Math.Min(10, _ws.CornerRadius * 0.5)); } }

        /// <summary>
        /// A pill. primary = the main action (accent background); on = a toggle that is on (a light accent background, accent text).
        /// glyph and text may each be empty.
        /// </summary>
        public static Border Make(Theme t, string glyph, string text, string tip, bool primary, Action click, bool on = false)
        {
            glyph = glyph ?? ""; text = text ?? "";
            Brush fg = primary ? NoteRender.Contrast(t.MainAccent) : on ? t.MainAccent : t.Fg;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph.Length > 0)
                row.Children.Add(new TextBlock
                {
                    Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(8, t.FontSize * 0.8), Foreground = fg, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, text.Length > 0 ? 5 : 0, 0)
                });
            if (text.Length > 0)
                row.Children.Add(new TextBlock
                {
                    Text = text, FontFamily = t.Font, FontSize = Math.Max(7, t.FontSize * 0.82), Foreground = fg, VerticalAlignment = VerticalAlignment.Center
                });
            Brush bg = primary ? t.MainAccent : on ? Theme.Faded(t.MainAccent, 22) : t.Track;
            var b = new Border
            {
                Child = row, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(Radius), Background = bg, Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrEmpty(tip) ? null : tip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0)
            };
            if (_ws != null && _ws.ShowBorder)
            {
                b.BorderThickness = new Thickness(1);
                b.BorderBrush = Theme.Faded(t.Resolve(_ws.BorderColor, t.MainAccent), Math.Min(60, Math.Max(25, _ws.BorderOpacity / 2)));
            }
            b.MouseEnter += delegate { if (primary) b.Opacity = 0.85; else b.Background = t.Hover; };
            b.MouseLeave += delegate { b.Opacity = 1; b.Background = bg; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (click != null) click(); };
            return b;
        }
    }
}
