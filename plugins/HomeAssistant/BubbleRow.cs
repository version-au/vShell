using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// One Bubble Card row (github.com/Clooos/bubble-card), drawn the way its styles do it, scaled from the module's font size
    /// (12.5 = Bubble's own sizes):
    ///  - a 50 px tall, fully rounded card in the "secondary" background (a shade lighter than the widget);
    ///  - a colour layer over the whole card (a switch that is on: the accent, solid; a light: its own colour at 70 %; a climate entity
    ///    that is heating / cooling: the state colour) and, for sliders, a fill growing from the left;
    ///  - a 38 px icon circle in the darker card colour (the icon at 60 % when off), then the name (13 px semibold) over the state
    ///    (12 px at 70 %), and a slot on the right for buttons (climate: − 24° + in a 36 px pill).
    /// Optionally a slider: drag / click along the card to set 0 - 1 (the icon and the right slot keep their own clicks).
    /// </summary>
    public sealed class BubbleRow : Border
    {
        public readonly double K;          // scale: font size / 12.5
        public readonly double H;          // card height
        readonly Theme _t;
        readonly Border _overlay = new Border(), _fill = new Border { HorizontalAlignment = HorizontalAlignment.Stretch };
        // the slider fill is the whole card scaled from the left: setting its Width from the card's own width made the card unable
        // to get narrower again (a widget that started wide stayed wide until it was rebuilt)
        readonly ScaleTransform _fillScale = new ScaleTransform(0, 1);
        readonly Border _iconBox = new Border();
        readonly TextBlock _name = new TextBlock(), _state = new TextBlock();
        readonly ContentControl _right = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        readonly Grid _layers = new Grid();
        double _frac = -1, _drag = -1;
        bool _dragging;
        Action<double> _preview, _commit;
        Action _iconClick, _click;

        /// <summary>The darker card colour of the icon circle and the right-hand pill (the widget / popup background, made solid).</summary>
        public static SolidColorBrush CardBrush(Theme t)
        {
            Color c = t.PopupBg != null ? t.PopupBg.Color : t.BgColor;
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Max((int)c.A, 230), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        public BubbleRow(Theme t, double height = 0)
        {
            _t = t;
            K = Math.Max(0.6, t.FontSize / 12.5);
            H = height > 0 ? height : Math.Round(50 * K);
            Height = H;
            CornerRadius = new CornerRadius(H / 2);
            Background = t.Track;
            ClipToBounds = true;
            _overlay.CornerRadius = new CornerRadius(H / 2);
            _overlay.Opacity = 0;
            _fill.CornerRadius = new CornerRadius(0);
            _fill.RenderTransform = _fillScale;
            _fill.RenderTransformOrigin = new Point(0, 0.5);
            _layers.Children.Add(_overlay);
            _layers.Children.Add(_fill);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            double ic = Math.Round(Math.Min(H - 8, 38 * K));
            _iconBox.Width = ic; _iconBox.Height = ic;
            _iconBox.CornerRadius = new CornerRadius(ic / 2);
            _iconBox.Background = CardBrush(t);
            _iconBox.Margin = new Thickness((H - ic) / 2);
            _iconBox.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(_iconBox);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4 * K, 0, 16 * K, 0) };
            _name.FontFamily = t.Font; _name.FontSize = 13 * K; _name.FontWeight = FontWeights.SemiBold; _name.Foreground = t.Fg; _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _state.FontFamily = t.Font; _state.FontSize = 12 * K; _state.Foreground = t.Fg; _state.Opacity = 0.7; _state.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(_name);
            names.Children.Add(_state);
            Grid.SetColumn(names, 1);
            g.Children.Add(names);
            _right.Margin = new Thickness(0, 0, 8 * K, 0);
            Grid.SetColumn(_right, 2);
            g.Children.Add(_right);
            _layers.Children.Add(g);
            Child = _layers;

            _iconBox.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { if (_iconClick != null) e.Handled = true; };
            _iconBox.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { if (_iconClick == null) return; e.Handled = true; _iconClick(); };
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.Handled) return;
                if (_commit != null)
                {
                    e.Handled = true;
                    _dragging = true;
                    CaptureMouse();
                    Track(e.GetPosition(this).X);
                }
                else if (_click != null) e.Handled = true;
            };
            MouseMove += delegate (object s, MouseEventArgs e) { if (_dragging) Track(e.GetPosition(this).X); };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (_dragging)
                {
                    e.Handled = true;
                    _dragging = false;
                    ReleaseMouseCapture();
                    double v = _drag;
                    _drag = -1;
                    if (v >= 0 && _commit != null) _commit(v);
                    return;
                }
                if (_click != null && !e.Handled) { e.Handled = true; _click(); }
            };
            LostMouseCapture += delegate { _dragging = false; };
            SizeChanged += delegate
            {
                // clip to the rounded card, so the slider fill gets Bubble's round left end
                _layers.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), H / 2, H / 2);
                DrawFill();
            };
        }

        public void SetIcon(FrameworkElement icon, bool on)
        {
            _iconBox.Child = icon;
            if (icon != null) { icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center; icon.Opacity = on ? 1 : 0.6; }
        }

        /// <summary>Icon size that fits the circle (Bubble: 24 px in 38).</summary>
        public double IconSize { get { return Math.Round(_iconBox.Width * 0.6); } }

        public void SetText(string name, string state)
        {
            _name.Text = name ?? "";
            _state.Text = state ?? "";
            _state.Visibility = string.IsNullOrEmpty(state) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>The colour layer over the whole card (null = none).</summary>
        public void SetOverlay(Brush b, double opacity)
        {
            _overlay.Background = b;
            _overlay.Opacity = b == null ? 0 : opacity;
        }

        /// <summary>A slider's fill: 0 - 1 of the card from the left (negative = none).</summary>
        public void SetFill(double frac, Brush b, double opacity)
        {
            _frac = frac;
            _fill.Background = b;
            _fill.Opacity = opacity;
            DrawFill();
        }

        void DrawFill()
        {
            double f = _drag >= 0 ? _drag : _frac;
            _fillScale.ScaleX = f <= 0 ? 0 : Math.Min(1, f);
        }

        public void SetRight(FrameworkElement e) { _right.Content = e; }

        /// <summary>Make it a slider: preview(0 - 1) while dragging, commit(0 - 1) on release.</summary>
        public void MakeSlider(Action<double> preview, Action<double> commit)
        {
            _preview = preview; _commit = commit;
            Cursor = Cursors.SizeWE;
        }

        public void OnIconClick(Action a) { _iconClick = a; _iconBox.Cursor = a != null ? Cursors.Hand : null; }
        public void OnClick(Action a) { _click = a; if (_commit == null) Cursor = a != null ? Cursors.Hand : null; }

        public bool Dragging { get { return _dragging; } }

        void Track(double x)
        {
            double w = ActualWidth;
            if (w <= 1) return;
            _drag = Math.Max(0, Math.Min(1, x / w));
            DrawFill();
            if (_preview != null) _preview(_drag);
        }

        // ---- the little parts ----
        /// <summary>Bubble's sub-button: 36 px tall, 18 px radius, 12 px text, in the secondary background.</summary>
        public static Border SubButton(Theme t, double k, FrameworkElement icon, string text, bool chevron, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (icon != null) sp.Children.Add(icon);
            if (!string.IsNullOrEmpty(text))
                sp.Children.Add(new TextBlock { Text = text, FontFamily = t.Font, FontSize = 12 * k, Foreground = t.Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(icon != null ? 6 * k : 0, 0, chevron ? 4 * k : 0, 0) });
            if (chevron) sp.Children.Add(ClimateIcons.Make("chevron", t.FgDim, 14 * k));
            double h = Math.Round(36 * k);
            var b = new Border
            {
                Child = sp, Height = h, MinWidth = h, CornerRadius = new CornerRadius(h / 2), Padding = new Thickness(10 * k, 0, 10 * k, 0),
                Background = t.Track, Cursor = Cursors.Hand
            };
            b.MouseEnter += delegate { b.Background = t.Hover; };
            b.MouseLeave += delegate { b.Background = t.Track; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (click != null) click(); };
            return b;
        }

        /// <summary>Bubble climate's − / + button: 34 px, round, no background until hovered.</summary>
        public static Border StepButton(Theme t, double k, string text, Action click)
        {
            double d = Math.Round(34 * k);
            var b = new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(1 * k),
                Child = new TextBlock { Text = text, FontSize = 17 * k, Foreground = t.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2 * k, 0, 0) }
            };
            b.MouseEnter += delegate { b.Background = t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }
    }
}
