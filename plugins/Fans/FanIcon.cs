using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace VShellFans
{
    /// <summary>
    /// A small drawn fan that turns at a speed following its rpm - far slower than the real one (a turn in 6 s at 1,000 rpm,
    /// never faster than one in 2.4 s) - and stands still at 0. It is stepped by one shared timer, 10 times a second, not by a WPF
    /// animation: while any WPF animation runs, WPF keeps drawing vShell's windows on its own frame loop, and with the turning fans
    /// on, the Settings window's blurred wallpaper jumped as it was dragged. A step is an ordinary change, like a clock's time.
    /// </summary>
    public sealed class FanIcon : Viewbox
    {
        static readonly Geometry Blades = Freeze(Geometry.Parse(
            "M12 12 C11 7 13 3.5 16.5 3.8 C17.6 7 15.5 10.5 12 12 Z " +
            "M12 12 C17 11 20.5 13 20.2 16.5 C17 17.6 13.5 15.5 12 12 Z " +
            "M12 12 C13 17 11 20.5 7.5 20.2 C6.4 17 8.5 13.5 12 12 Z " +
            "M12 12 C7 13 3.5 11 3.8 7.5 C7 6.4 10.5 8.5 12 12 Z"));

        static Geometry Freeze(Geometry g) { g.Freeze(); return g; }

        const int Fps = 10;             // steps a second: each one redraws the whole bar / widget
        const double MinTurn = 2.4;     // seconds a turn, at the fastest (15 degrees a step at 10 a second; past 30 the four blades seem to turn backwards)

        readonly Path _blades;
        readonly Ellipse _hub, _ring;
        readonly RotateTransform _rot = new RotateTransform(0, 12, 12);
        double _step;                   // degrees each tick; 0 = still

        // every turning icon, and the one timer that steps them (running only while there's one)
        static readonly HashSet<FanIcon> Turning = new HashSet<FanIcon>();
        static DispatcherTimer _timer;

        /// <param name="ring">a thin circle round it (rows, the popup)</param>
        public FanIcon(double size, bool ring = false)
        {
            Width = Height = Math.Max(8, size);
            Stretch = Stretch.Uniform;
            var c = new Canvas { Width = 24, Height = 24 };
            _ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 1.2, Visibility = ring ? Visibility.Visible : Visibility.Collapsed };
            Canvas.SetLeft(_ring, 1); Canvas.SetTop(_ring, 1);
            _blades = new Path { Data = Blades, RenderTransform = _rot };
            _hub = new Ellipse { Width = 4.4, Height = 4.4, StrokeThickness = 1.2 };
            Canvas.SetLeft(_hub, 9.8); Canvas.SetTop(_hub, 9.8);
            c.Children.Add(_ring);
            c.Children.Add(_blades);
            c.Children.Add(_hub);
            Child = c;
            IsHitTestVisible = false;
            Unloaded += delegate { Still(); };
        }

        /// <summary>Its colour; hub = what's behind it (the hole in the middle); ring = the circle's colour.</summary>
        public void Colour(Brush fill, Brush hub, Brush ring = null)
        {
            _blades.Fill = fill;
            _hub.Stroke = fill;
            _hub.Fill = hub ?? Brushes.Transparent;
            _ring.Stroke = ring ?? fill;
        }

        /// <summary>Turn as fast as this rpm says (animate = false, or under 50 rpm: still). A new speed carries on from where it is.</summary>
        public void Spin(double rpm, bool animate)
        {
            double secs = animate && rpm >= 50 ? Math.Max(MinTurn, 6000.0 / rpm) : 0;
            if (secs == 0) { Still(); return; }
            _step = 360.0 / (secs * Fps);
            if (Turning.Add(this)) Ticking();
        }

        void Still()
        {
            if (_step == 0) return;
            _step = 0;
            Turning.Remove(this);
            Ticking();
        }

        /// <summary>The timer runs while any fan turns.</summary>
        static void Ticking()
        {
            if (Turning.Count > 0 && _timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / Fps) };
                _timer.Tick += delegate
                {
                    foreach (FanIcon f in Turning) f._rot.Angle = (f._rot.Angle + f._step) % 360;
                };
                _timer.Start();
            }
            else if (Turning.Count == 0 && _timer != null) { _timer.Stop(); _timer = null; }
        }

        /// <summary>The plugin is unloading: stop the timer (the icons go with their modules).</summary>
        internal static void StopAll()
        {
            Turning.Clear();
            Ticking();
        }
    }
}
