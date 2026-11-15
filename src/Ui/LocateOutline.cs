using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Settings > a bar / widget > Show on screen: an accent outline around the real bar or widget for a couple of seconds, so you can
    /// see which one you're changing. Its own small window - always on top, never takes the keyboard or the mouse (clicks go through it) -
    /// positioned in device pixels, like <see cref="EdgeGlow"/>.
    /// </summary>
    sealed class LocateOutline : Window
    {
        /// <summary>How long it shows (fading in and out included).</summary>
        public const int Millis = 2200;

        static LocateOutline _one;
        readonly System.Windows.Controls.Border _ring = new System.Windows.Controls.Border();
        readonly DispatcherTimer _off = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Millis - 300) };
        IntPtr _hwnd;

        LocateOutline()
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
            _ring.BorderThickness = new Thickness(3);
            _ring.CornerRadius = new CornerRadius(10);
            Content = _ring;
            SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLongAuto(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            };
            _off.Tick += delegate
            {
                _off.Stop();
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
            };
        }

        /// <summary>Outline this rectangle of the screen (device px) - a little outside it, so the bar / widget itself stays clear.</summary>
        public static void Flash(RECT r, Color accent)
        {
            if (_one == null) _one = new LocateOutline();
            _one.FlashAt(r, accent);
        }

        void FlashAt(RECT r, Color accent)
        {
            if (!IsVisible) Show();
            if (_hwnd == IntPtr.Zero) return;
            MonitorInfo mon = MonitorHelper.FromPoint((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            int pad = (int)Math.Round(6 * (mon != null ? MonitorHelper.ScaleOf(mon) : 1.0));
            _ring.BorderBrush = new SolidColorBrush(accent);
            _ring.Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, r.Left - pad, r.Top - pad, (r.Right - r.Left) + 2 * pad, (r.Bottom - r.Top) + 2 * pad,
                                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            // in, a short blink to catch the eye, then it stays until the timer fades it
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));
            BeginAnimation(OpacityProperty, a);
            _off.Stop();
            _off.Start();
        }
    }
}
