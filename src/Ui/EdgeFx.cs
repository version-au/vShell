using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The glass edge for bars and widgets (a light line along the top, a darker one along the bottom and a faint sheen, so a bar or widget looks like glass).
    /// </summary>
    public static class EdgeFx
    {
        public static readonly string[] GlassEdges = { "Off", "Subtle", "Strong" };

        /// <summary>
        /// The glass edge on an overlay (same size and corners as the bar / widget background, on top of it, not hit-testable):
        /// a light line along the top, a darker line along the bottom and a faint sheen over the top part.
        /// </summary>
        public static void GlassEdge(Border overlay, string level, CornerRadius cr)
        {
            if (overlay == null) return;
            overlay.IsHitTestVisible = false;
            if (level != "Subtle" && level != "Strong")
            {
                overlay.Visibility = Visibility.Collapsed;
                overlay.Background = null;
                overlay.BorderBrush = null;
                return;
            }
            bool strong = level == "Strong";
            byte line = strong ? (byte)0x8C : (byte)0x50, shade = strong ? (byte)0x60 : (byte)0x38, sheen = strong ? (byte)0x1C : (byte)0x0E;
            var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(line, 255, 255, 255), 0));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(line / 3), 255, 255, 255), 0.35));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(shade, 0, 0, 0), 1));
            edge.Freeze();
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(sheen, 255, 255, 255), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.8));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(sheen * 1.5), 0, 0, 0), 1));
            fill.Freeze();
            overlay.Background = fill;
            overlay.BorderBrush = edge;
            overlay.BorderThickness = new Thickness(1);
            overlay.CornerRadius = cr;
            overlay.Visibility = Visibility.Visible;
        }
    }
}
