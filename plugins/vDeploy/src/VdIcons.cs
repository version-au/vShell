using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VDeploy
{
    /// <summary>
    /// vDeploy's own line icons (its Icon.razor: a 16 x 16 box, 1.25 strokes, round ends) - the host / stack / container ones its side panel
    /// shows, so the widget and popup look like vDeploy's pages.
    /// </summary>
    static class VdIcons
    {
        static readonly Dictionary<string, Geometry> Shapes = new Dictionary<string, Geometry>();

        static Geometry Shape(string name)
        {
            Geometry g;
            if (Shapes.TryGetValue(name, out g)) return g;
            switch (name)
            {
                case "host":
                    var gg = new GeometryGroup();
                    gg.Children.Add(new RectangleGeometry(new Rect(2.5, 2.5, 11, 4.5), 1, 1));
                    gg.Children.Add(new RectangleGeometry(new Rect(2.5, 9, 11, 4.5), 1, 1));
                    gg.Children.Add(Geometry.Parse("M5 4.75h.1M5 11.25h.1"));
                    g = gg;
                    break;
                case "stack": g = Geometry.Parse("M8 2l6 3.2-6 3.2-6-3.2zM2 8.2l6 3.2 6-3.2M2 11l6 3.2 6-3.2"); break;
                case "box": g = Geometry.Parse("M2.5 5L8 2.5 13.5 5v6L8 13.5 2.5 11zM2.5 5L8 7.5 13.5 5M8 7.5v6"); break;
                case "chev-right": g = Geometry.Parse("M6 4l4 4-4 4"); break;
                case "chev-down": g = Geometry.Parse("M4 6l4 4 4-4"); break;
                default: g = Geometry.Empty; break;
            }
            if (g.CanFreeze) g.Freeze();
            Shapes[name] = g;
            return g;
        }

        /// <summary>The icon at size x size (DIPs) in brush, as vDeploy draws it.</summary>
        public static FrameworkElement Make(string name, double size, Brush brush, double opacity = 1)
        {
            var canvas = new Canvas { Width = 16, Height = 16 };
            canvas.Children.Add(new Path
            {
                Data = Shape(name), Stroke = brush, StrokeThickness = 1.25,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            });
            return new Viewbox
            {
                Width = size, Height = size, Child = canvas, Opacity = opacity, VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform, SnapsToDevicePixels = true
            };
        }
    }
}
