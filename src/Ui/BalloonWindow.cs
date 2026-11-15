using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A tray icon's balloon notification ("Backup complete"), drawn in the bar's theme next to the icon. Tells the app what happened
    /// to it the way Explorer does: NIN_BALLOONSHOW when shown, NIN_BALLOONUSERCLICK when clicked, NIN_BALLOONTIMEOUT when it
    /// times out or is closed with the X, NIN_BALLOONHIDE when it is replaced or taken down.
    /// </summary>
    public sealed class BalloonWindow : Window
    {
        const uint NIN_BALLOONSHOW = 0x402, NIN_BALLOONHIDE = 0x403, NIN_BALLOONTIMEOUT = 0x404, NIN_BALLOONUSERCLICK = 0x405;

        readonly BarManager _mgr;
        readonly TrayHost _host;
        public readonly TrayIcon ForIcon;
        readonly RECT _anchor;
        readonly bool _above;
        readonly DispatcherTimer _timeout;
        IntPtr _hwnd;
        bool _done;

        public BalloonWindow(BarManager mgr, TrayHost host, TrayIcon ic, TrayBalloon b, Theme t, RECT anchor, bool above)
        {
            _mgr = mgr; _host = host; ForIcon = ic; _anchor = anchor; _above = above;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000; Top = -32000;
            Opacity = 0;
            UseLayoutRounding = true;
            Title = "vShell notification";

            // icon: the app's own (NIIF_USER / none) or the standard info / warning / error icon
            ImageSource img = null;
            uint kind = b.Flags & 0xF;
            if (kind == 1 || kind == 2 || kind == 3)
            {
                int id = kind == 1 ? 32516 : kind == 2 ? 32515 : 32513;    // IDI_INFORMATION / IDI_WARNING / IDI_ERROR
                img = IconUtil.FromHIcon(Native.LoadIcon(IntPtr.Zero, new IntPtr(id)));
            }
            else if (kind == 4 && b.Icon != IntPtr.Zero) img = IconUtil.FromHIcon(b.Icon);
            if (img == null) img = IconUtil.FromHIcon(ic.HIcon);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (img != null)
            {
                var im = new Image { Source = img, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                Grid.SetColumn(im, 0);
                grid.Children.Add(im);
            }

            var texts = new StackPanel { MaxWidth = 300 };
            string title = !string.IsNullOrEmpty(b.Title) ? b.Title : ic.Tip;
            if (!string.IsNullOrEmpty(title))
                texts.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
            texts.Children.Add(new TextBlock { Text = b.Text, TextWrapping = TextWrapping.Wrap, Foreground = t.FgDim });
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            var close = new TextBlock { Text = "✕", Foreground = t.FgDim, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand };
            close.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Finish(NIN_BALLOONTIMEOUT); };
            Grid.SetColumn(close, 2);
            grid.Children.Add(close);

            var card = new Border
            {
                Background = t.PopupBg,
                BorderBrush = t.Track,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 10, 10),
                Child = grid,
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black }
            };
            TextElement.SetForeground(card, t.Fg);
            TextElement.SetFontFamily(card, t.Font);
            TextElement.SetFontSize(card, t.FontSize);
            card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Finish(NIN_BALLOONUSERCLICK); };
            Content = new Border { Padding = new Thickness(8), Child = card };

            // Windows ignores the app's timeout since Vista; a few seconds, and not while the pointer is on it
            _timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            _timeout.Tick += delegate { _timeout.Stop(); Finish(NIN_BALLOONTIMEOUT); };
            MouseEnter += delegate { _timeout.Stop(); };
            MouseLeave += delegate { if (!_done) { _timeout.Interval = TimeSpan.FromSeconds(3); _timeout.Start(); } };

            Loaded += delegate
            {
                // let SizeToContent settle, then put it next to the icon and show it
                var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                settle.Tick += delegate
                {
                    settle.Stop();
                    if (_done) return;
                    PlaceNearAnchor();
                    Opacity = 1;
                    Tell(NIN_BALLOONSHOW);
                    _timeout.Start();
                };
                settle.Start();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            WinStyle.HideFromTasks(_hwnd, true);
            _mgr.Tasks.Ignored.Add(_hwnd);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timeout.Stop();
            _mgr.Tasks.Ignored.Remove(_hwnd);
            base.OnClosed(e);
        }

        void PlaceNearAnchor()
        {
            RECT me;
            Native.GetWindowRect(_hwnd, out me);
            int w = me.Width, h = me.Height;
            var mon = MonitorHelper.FromPoint((_anchor.Left + _anchor.Right) / 2, (_anchor.Top + _anchor.Bottom) / 2);
            RECT b = mon != null ? mon.Bounds : _anchor;
            int x = (_anchor.Left + _anchor.Right) / 2 - w / 2;
            x = Math.Max(b.Left + 4, Math.Min(b.Right - w - 4, x));
            int y = _above ? _anchor.Top - h : _anchor.Bottom;
            y = Math.Max(b.Top, Math.Min(b.Bottom - h, y));
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void Tell(uint msg)
        {
            int cx = (_anchor.Left + _anchor.Right) / 2, cy = (_anchor.Top + _anchor.Bottom) / 2;
            try { _host.Post(ForIcon, msg, cx, cy); } catch { }
        }

        /// <summary>Taken down because a new balloon replaced it or the app withdrew it.</summary>
        public void Dismiss() { Finish(NIN_BALLOONHIDE); }

        void Finish(uint msg)
        {
            if (_done) return;
            _done = true;
            _timeout.Stop();
            Tell(msg);
            try { Close(); } catch { }
        }
    }
}
