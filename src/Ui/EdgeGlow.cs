using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A thin accent glow along the very edge of the screen, where a widget that slid away is waiting: it shows where to push the
    /// mouse to bring it back. Its own small window - always on top, never takes the keyboard or the mouse (clicks go through it) -
    /// positioned in device pixels.
    /// </summary>
    sealed class EdgeGlow : Window
    {
        readonly Border _fill = new Border();
        IntPtr _hwnd;
        RECT _at;
        bool _shown, _placed;
        string _way = "";

        public EdgeGlow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            IsHitTestVisible = false;
            Width = 1; Height = 1;
            Left = -32000; Top = -32000;
            Opacity = 0;
            Content = _fill;
            SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            };
        }

        /// <summary>Glow along this strip of the screen (device px), fading away from the edge on the side "way" points at.</summary>
        public void ShowAt(RECT r, string way, Color accent)
        {
            if (!IsVisible) Show();
            if (_hwnd == IntPtr.Zero) return;
            if (way != _way || !(_fill.Background is LinearGradientBrush) || ((LinearGradientBrush)_fill.Background).GradientStops[0].Color != accent)
            {
                _way = way;
                // brightest at the screen edge, fading towards the middle of the screen
                Point from, to;
                switch (way)
                {
                    case "Left": from = new Point(0, 0.5); to = new Point(1, 0.5); break;
                    case "Right": from = new Point(1, 0.5); to = new Point(0, 0.5); break;
                    case "Up": from = new Point(0.5, 0); to = new Point(0.5, 1); break;
                    default: from = new Point(0.5, 1); to = new Point(0.5, 0); break;
                }
                var g = new LinearGradientBrush { StartPoint = from, EndPoint = to };
                g.GradientStops.Add(new GradientStop(accent, 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(accent.A * 0.45), accent.R, accent.G, accent.B), 0.4));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
                g.Freeze();
                _fill.Background = g;
                // soft ends along the edge, so it reads as a glow rather than a bar
                bool across = way == "Up" || way == "Down";
                var mask = new LinearGradientBrush { StartPoint = across ? new Point(0, 0.5) : new Point(0.5, 0), EndPoint = across ? new Point(1, 0.5) : new Point(0.5, 1) };
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
                mask.GradientStops.Add(new GradientStop(Colors.Black, 0.12));
                mask.GradientStops.Add(new GradientStop(Colors.Black, 0.88));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                mask.Freeze();
                _fill.OpacityMask = mask;
            }
            if (!_placed || r.Left != _at.Left || r.Top != _at.Top || r.Right != _at.Right || r.Bottom != _at.Bottom)
            {
                _at = r; _placed = true;
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top),
                                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            if (!_shown)
            {
                _shown = true;
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
            }
        }

        /// <summary>Fade out (the widget came back, or the glow is turned off).</summary>
        public void Fade()
        {
            if (!_shown) return;
            _shown = false;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        }
    }
}
