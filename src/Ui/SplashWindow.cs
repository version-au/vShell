using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A small card in the middle of the main screen while vShell starts: the vShell mark in the theme's colours, the name, the version
    /// and a moving line. It fades in, stays at least a moment, then fades out once the bars are up. Never takes focus.
    /// </summary>
    public sealed class SplashWindow : Window
    {
        readonly DateTime _shown = DateTime.UtcNow;
        bool _closing;

        public SplashWindow(BarSettings s)
        {
            Theme t = Theme.From(s, 13, 16);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = "vShell";
            Opacity = 0;
            UseLayoutRounding = true;

            Color bg = t.BgColor;
            var cardBg = new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B));     // opaque, whatever the bar transparency

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new VShellMark
            {
                Dot1 = t.MainAccent, Dot2 = t.SecondaryAccent, Dot3 = t.Fg, Dim = 104, ShowLine = true,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "vShell", FontFamily = t.Font, FontSize = 34, FontWeight = FontWeights.SemiBold, Foreground = t.Fg,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Version v = typeof(SplashWindow).Assembly.GetName().Version;
            stack.Children.Add(new TextBlock
            {
                Text = (v == null ? "" : "version " + v.Major + "." + v.Minor + "." + v.Build + "  ·  ") + "starting",
                FontFamily = t.Font, FontSize = 12.5, Foreground = t.FgDim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 18)
            });

            // a short accent segment gliding along a faint track
            var track = new Border { Width = 160, Height = 3, CornerRadius = new CornerRadius(1.5), Background = t.Track, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center };
            var glide = new TranslateTransform(-48, 0);
            var seg = new Border { Width = 48, Height = 3, CornerRadius = new CornerRadius(1.5), Background = t.MainAccent, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = glide };
            track.Child = seg;
            stack.Children.Add(track);
            var move = new DoubleAnimation(-48, 160, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            glide.BeginAnimation(TranslateTransform.XProperty, move);

            var card = new Border
            {
                Background = cardBg, BorderBrush = t.Track, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(56, 40, 56, 34), Child = stack,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4, Color = Colors.Black }
            };
            Content = new Border { Padding = new Thickness(24), Child = card };

            Loaded += delegate { BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { WinStyle.HideFromTasks(new WindowInteropHelper(this).Handle, true); } catch { }
        }

        /// <summary>Fade out and close - but not before it has been visible for minShown (so it does not just flash).</summary>
        public void Finish(TimeSpan minShown)
        {
            if (_closing) return;
            _closing = true;
            TimeSpan wait = minShown - (DateTime.UtcNow - _shown);
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            var t = new DispatcherTimer { Interval = wait };
            t.Tick += delegate
            {
                t.Stop();
                var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(320));
                fade.Completed += delegate { try { Close(); } catch { } };
                BeginAnimation(OpacityProperty, fade);
            };
            t.Start();
        }
    }
}
