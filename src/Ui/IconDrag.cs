using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace vShell.Ui
{
    /// <summary>
    /// Dragging icons along a row to put them in another order (taskbar pins, launcher shortcuts): a press that moves starts a drag
    /// carrying "module id|index"; the row shows a thin accent line where it would land and works out the new position.
    /// </summary>
    public static class IconDrag
    {
        public const string Format = "vShell.IconMove";
        static DateTime _ended = DateTime.MinValue;

        /// <summary>A drag just ended: the button's click that follows must not launch anything.</summary>
        public static bool JustDragged { get { return (DateTime.UtcNow - _ended).TotalMilliseconds < 400; } }

        /// <summary>Let el be dragged; token() says what it is ("module|index"); canStart() false = not now (e.g. not a pin).</summary>
        public static void MakeDraggable(FrameworkElement el, Func<string> token, Func<bool> canStart, Action started)
        {
            Point down = new Point();
            bool pressed = false;
            el.PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                // Ctrl+drag stays the bar's / widget's own module drag
                pressed = (Keyboard.Modifiers & ModifierKeys.Control) == 0 && (canStart == null || canStart());
                down = e.GetPosition(el);
            };
            el.PreviewMouseLeftButtonUp += delegate { pressed = false; };
            el.MouseLeave += delegate (object s, MouseEventArgs e) { if (e.LeftButton != MouseButtonState.Pressed) pressed = false; };
            el.PreviewMouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(el);
                if (Math.Abs(p.X - down.X) < SystemParameters.MinimumHorizontalDragDistance * 1.5 &&
                    Math.Abs(p.Y - down.Y) < SystemParameters.MinimumVerticalDragDistance * 1.5) return;
                pressed = false;
                if (started != null) started();
                try { DragDrop.DoDragDrop(el, new DataObject(Format, token()), DragDropEffects.Move); }
                catch { }
                finally { _ended = DateTime.UtcNow; }
            };
        }

        /// <summary>The token being dragged, or null when the drag isn't one of ours.</summary>
        public static string TokenOf(DragEventArgs e)
        {
            return e.Data.GetDataPresent(Format) ? e.Data.GetData(Format) as string : null;
        }

        /// <summary>Where in the row the mouse is: 0 = before the first counted child; counts = which children are slots.</summary>
        public static int IndexAt(Panel row, DragEventArgs e, Func<UIElement, bool> counts)
        {
            int i = 0;
            bool vertical = row is StackPanel && ((StackPanel)row).Orientation == Orientation.Vertical;
            bool grid = row is System.Windows.Controls.Primitives.UniformGrid;     // a launcher's grid in a widget: only the children on the mouse's row count
            foreach (UIElement c in row.Children)
            {
                if (counts != null && !counts(c)) continue;
                var fe = c as FrameworkElement;
                if (fe == null || fe.ActualWidth <= 0) { i++; continue; }
                Point p = e.GetPosition(fe);
                if (grid)
                {
                    if (p.Y < 0) return i;                                           // this child is on a row below the mouse: after the row above
                    if (p.Y < fe.ActualHeight && p.X < fe.ActualWidth / 2) return i;
                }
                else if (vertical ? p.Y < fe.ActualHeight / 2 : p.X < fe.ActualWidth / 2) return i;
                i++;
            }
            return i;
        }

        // ---- the thin line where it would land ----------------------------------------------------
        sealed class Mark : Adorner
        {
            public double X, Y = double.NaN, H = double.NaN;
            readonly Brush _brush;
            public Mark(UIElement on, Brush brush) : base(on) { _brush = brush; IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext dc)
            {
                var fe = AdornedElement as FrameworkElement;
                double h = !double.IsNaN(H) ? H : fe != null ? fe.ActualHeight : 20;
                double y = !double.IsNaN(Y) ? Y : 0;
                dc.DrawRoundedRectangle(_brush, null, new Rect(X - 1, y + h * 0.15, 2.5, h * 0.7), 1.2, 1.2);
            }
        }

        /// <summary>Show the line before the index-th counted child (or after the last) - on that child's row in a grid.</summary>
        public static void ShowMark(Panel row, int index, Func<UIElement, bool> counts, Brush brush)
        {
            HideMark(row);
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(row);
            if (layer == null) return;
            double x = 0;
            int i = 0;
            FrameworkElement last = null, at = null;
            bool found = false;
            foreach (UIElement c in row.Children)
            {
                if (counts != null && !counts(c)) continue;
                var fe = c as FrameworkElement;
                if (fe == null) continue;
                if (i == index) { x = fe.TranslatePoint(new Point(0, 0), row).X; at = fe; found = true; break; }
                last = fe;
                i++;
            }
            if (!found && last != null) { x = last.TranslatePoint(new Point(last.ActualWidth, 0), row).X; at = last; }
            var m = new Mark(row, brush) { X = x };
            if (row is System.Windows.Controls.Primitives.UniformGrid && at != null) { m.Y = at.TranslatePoint(new Point(0, 0), row).Y; m.H = at.ActualHeight; }
            layer.Add(m);
        }

        public static void HideMark(Panel row)
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(row);
            if (layer == null) return;
            Adorner[] all = layer.GetAdorners(row);
            if (all == null) return;
            foreach (Adorner a in all) if (a is Mark) layer.Remove(a);
        }
    }
}
