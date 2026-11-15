using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Now playing: album art, title and artist of whatever Windows' media controls show (Spotify, browsers, Plex, ...), with
    /// previous / play-pause / next buttons. Click the art or text for a bigger card. Data from <see cref="MediaSession"/>.
    /// </summary>
    public sealed class NowPlayingModule : Border
    {
        const string GlyphPrev = "", GlyphNext = "", GlyphPlay = "", GlyphPause = "", GlyphMusic = "";

        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly Border _art = new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _note = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _artist = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel _texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        readonly Marquee _titleBox, _artistBox;
        readonly StackPanel _buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _playGlyph;
        readonly DispatcherTimer _timer;
        readonly ProgressLine _progress;
        readonly TextBlock _times;
        ProgressLine _popProgress;
        TextBlock _popPos, _popLen;
        MediaInfo _shown;
        int _artVersion = -1;
        ImageSource _artImage;
        PopupWindow _popup;

        public NowPlayingModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _t = bar.Theme.ForModule(cfg);
            MediaSession.Settings = mgr.Settings;      // for the media helpers (the Jellyfin plugin)
            double h = bar.HostHeight;
            double artSize = Math.Round(Math.Max(14, Math.Min(h - 6, _t.FontSize * (cfg.ShownLines2 ? 2.8 : 1.9))));

            _art.Width = artSize;
            _art.Height = artSize;
            _art.Margin = new Thickness(0, 0, 8, 0);
            _art.Background = _t.Track;
            _note.Text = GlyphMusic;
            _note.FontFamily = IconUtil.GlyphFont;
            _note.FontSize = artSize * 0.55;
            _note.Foreground = _t.MainAccent;

            _title.Foreground = _t.Fg;
            _artist.Foreground = _t.FgDim;
            // explicit font, so the text can be measured before it is on screen (to know whether it has to scroll)
            _title.FontFamily = _t.Font;
            _title.FontSize = _t.FontSize;
            _artist.FontFamily = _t.Font;
            _artist.FontSize = cfg.ShownLines2 ? _t.FontSize * 0.85 : _t.FontSize;
            _titleBox = new Marquee(_title, cfg.MediaWidth, cfg.MediaScroll);
            _artistBox = new Marquee(_artist, cfg.MediaWidth, cfg.MediaScroll);
            _texts.Children.Add(_titleBox);
            if (cfg.ShownLines2) _texts.Children.Add(_artistBox);      // one line: "Title - Artist" in the title block

            if (cfg.MediaShowControls)
            {
                _buttons.Margin = new Thickness(6, 0, 0, 0);
                _buttons.Children.Add(MakeButton(GlyphPrev, "Previous", "previous", out _));
                _buttons.Children.Add(MakeButton(GlyphPause, "Play / pause", "toggle", out _playGlyph));
                _buttons.Children.Add(MakeButton(GlyphNext, "Next", "next", out _));
            }

            // where it is in the track: a thin bar under the title, or along the module's bottom edge; click it to jump there
            if (cfg.MediaProgress != "Off")
            {
                bool bottom = cfg.MediaProgress == "Along the bottom";
                _progress = new ProgressLine(_t, bottom ? 2.5 : 3, bottom ? 6 : 8);
                _progress.Visibility = Visibility.Collapsed;
                if (bottom)
                {
                    _progress.VerticalAlignment = VerticalAlignment.Bottom;
                    _progress.Margin = new Thickness(-6, 0, -6, 0);     // the module's whole width (past its padding)
                }
                else _progress.Margin = new Thickness(0, 1, 0, 0);
            }
            if (cfg.MediaShowTimes)
            {
                _times = new TextBlock
                {
                    Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = Math.Max(8, _t.FontSize * 0.85), VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed
                };
                _times.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (cfg.MediaShowArt) row.Children.Add(_art);
            row.Children.Add(_texts);
            if (_times != null) row.Children.Add(_times);
            row.Children.Add(_buttons);
            if (_progress != null && cfg.MediaProgress == "Along the bottom")
            {
                var g = new Grid();
                g.Children.Add(row);
                g.Children.Add(_progress);
                Child = g;
            }
            else
            {
                if (_progress != null) _texts.Children.Add(_progress);
                Child = row;
            }
            Padding = new Thickness(6, 0, 6, 0);
            Background = Brushes.Transparent;

            _art.Cursor = Cursors.Hand;
            _texts.Cursor = Cursors.Hand;
            // handle the press too, or the bar / widget takes it as the start of a drag and the click never arrives
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            _art.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            _texts.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += delegate { Refresh(); UpdateProgress(); };
            Loaded += delegate { Refresh(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        Border MakeButton(string glyph, string tip, string command, out TextBlock text)
        {
            text = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = Math.Max(10, _t.FontSize * 0.95), Foreground = _t.Fg, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Child = text, Padding = new Thickness(5, 3, 5, 3), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += delegate { b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; MediaSession.Send(command); };
            return b;
        }

        void Refresh()
        {
            MediaInfo m = MediaSession.Current;
            bool same = _shown != null && _shown.HasSession == m.HasSession && _shown.Status == m.Status && _shown.Title == m.Title &&
                        _shown.Artist == m.Artist && _shown.ArtVersion == m.ArtVersion && _shown.Available == m.Available &&
                        _shown.HasProgress == m.HasProgress;
            if (same) { _shown = m; return; }      // newer times, nothing else to redraw
            _shown = m;

            bool idle = !m.HasSession || (m.Title.Length == 0 && m.Artist.Length == 0) || m.Status == 0;
            Visibility = idle && _cfg.HideIfNone ? Visibility.Collapsed : Visibility.Visible;

            if (idle)
            {
                _title.Text = m.Available ? "Nothing playing" : "Media controls";
                _title.Foreground = _t.FgDim;
                _artist.Text = "";
                ToolTip = m.Available ? "Nothing is playing" : "Windows' media sessions are not available here - the buttons send the keyboard media keys.";
            }
            else
            {
                _title.Foreground = _t.Fg;
                if (_cfg.ShownLines2) { _title.Text = m.Title; _artist.Text = m.Artist; }
                else _title.Text = m.Artist.Length > 0 && m.Title.Length > 0 ? m.Title + "  –  " + m.Artist : (m.Title.Length > 0 ? m.Title : m.Artist);
                _artistBox.Visibility = m.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                ToolTip = m.Title + (m.Artist.Length > 0 ? "\n" + m.Artist : "") + (m.Album.Length > 0 ? "\n" + m.Album : "") +
                          (m.App.Length > 0 ? "\n\nPlaying in " + m.App : "") + "\nClick for more";
            }
            _titleBox.Update();
            _artistBox.Update();
            if (_playGlyph != null) _playGlyph.Text = m.Playing ? GlyphPause : GlyphPlay;

            if (m.ArtVersion != _artVersion)
            {
                _artVersion = m.ArtVersion;
                _artImage = Decode(m.Art, 256);
            }
            if (_artImage != null && !idle)
                _art.Child = new Image { Source = _artImage, Stretch = Stretch.UniformToFill };
            else _art.Child = _note;

            if (_popup != null && _popup.IsOpen) _popup.SetContent(BuildPopup());
            UpdateProgress();
            _bar.ContentChanged();
        }

        /// <summary>The progress bar(s) and times, from the latest position (it moves on between the updates while playing).</summary>
        void UpdateProgress()
        {
            MediaInfo m = _shown;
            bool idle = m == null || !m.HasSession || (m.Title.Length == 0 && m.Artist.Length == 0) || m.Status == 0;
            bool show = !idle && m.HasProgress;
            double pos = show ? m.PositionNow : 0, len = show ? m.Duration : 0;
            if (_progress != null)
            {
                var v = show ? Visibility.Visible : Visibility.Collapsed;
                if (_progress.Visibility != v) { _progress.Visibility = v; _bar.ContentChanged(); }
                if (show) _progress.Set(pos / len, m.CanSeek, len);
            }
            if (_times != null)
            {
                var v = show ? Visibility.Visible : Visibility.Collapsed;
                if (_times.Visibility != v) { _times.Visibility = v; _bar.ContentChanged(); }
                if (show) _times.Text = MediaInfo.Time(pos) + " / " + MediaInfo.Time(len);
            }
            if (_popProgress != null && _popup != null && _popup.IsOpen)
            {
                _popProgress.Set(show ? pos / len : 0, show && m.CanSeek, len);
                _popPos.Text = show ? MediaInfo.Time(pos) : "";
                _popLen.Text = show ? MediaInfo.Time(len) : "";
            }
        }

        static ImageSource Decode(byte[] data, int px)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = new MemoryStream(data);
                bi.DecodePixelWidth = px;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex) { Log.Write("Now playing art: " + ex.Message); return null; }
        }

        // ---- bigger card ---------------------------------------------------------------------------------
        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (PopupWindow.ClosedJustNow(this)) { _popup = null; return; }     // this click's press already closed it
            _popup = new PopupWindow(_mgr, _bar, this, _bar.Theme, BuildPopup());
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_bar.Theme, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        FrameworkElement BuildPopup()
        {
            Theme t = _bar.Theme;
            MediaInfo m = _shown ?? new MediaInfo();
            bool idle = !m.HasSession || (m.Title.Length == 0 && m.Artist.Length == 0);
            var root = new StackPanel { Margin = new Thickness(8), Width = 240 };

            var art = new Border { Width = 224, Height = 224, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = t.Track, Margin = new Thickness(0, 0, 0, 10) };
            if (_artImage != null && !idle) art.Child = new Image { Source = _artImage, Stretch = Stretch.UniformToFill };
            else art.Child = new TextBlock { Text = GlyphMusic, FontFamily = IconUtil.GlyphFont, FontSize = 64, Foreground = t.MainAccent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(art);

            root.Children.Add(new TextBlock { Text = idle ? "Nothing playing" : m.Title, FontWeight = FontWeights.SemiBold, FontSize = t.FontSize * 1.15, Foreground = t.Fg, TextWrapping = TextWrapping.Wrap });
            if (!idle && m.Artist.Length > 0) root.Children.Add(new TextBlock { Text = m.Artist, Foreground = t.Fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (!idle && m.Album.Length > 0) root.Children.Add(new TextBlock { Text = m.Album, Foreground = t.FgDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

            // progress, with where it is and how long it is under it; click / drag on the bar to jump
            _popProgress = null;
            if (!idle && m.HasProgress)
            {
                _popProgress = new ProgressLine(t, 4, 12) { Margin = new Thickness(0, 10, 0, 0) };
                root.Children.Add(_popProgress);
                var times = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                _popPos = new TextBlock { Foreground = t.FgDim, FontSize = Math.Max(9, t.FontSize * 0.85), HorizontalAlignment = HorizontalAlignment.Left };
                _popLen = new TextBlock { Foreground = t.FgDim, FontSize = Math.Max(9, t.FontSize * 0.85), HorizontalAlignment = HorizontalAlignment.Right };
                times.Children.Add(_popPos);
                times.Children.Add(_popLen);
                root.Children.Add(times);
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateProgress));
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 2) };
            foreach (var spec in new[] { new[] { GlyphPrev, "previous", "Previous" }, new[] { m.Playing ? GlyphPause : GlyphPlay, "toggle", "Play / pause" }, new[] { GlyphNext, "next", "Next" } })
            {
                string cmd = spec[1];
                bool main = cmd == "toggle";
                var tb = new TextBlock { Text = spec[0], FontFamily = IconUtil.GlyphFont, FontSize = main ? 20 : 16, Foreground = main ? t.MainAccent : t.Fg, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Child = tb, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = spec[2], Margin = new Thickness(4, 0, 4, 0) };
                b.MouseEnter += delegate { b.Background = t.Hover; };
                b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
                b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; MediaSession.Send(cmd); };
                buttons.Children.Add(b);
            }
            root.Children.Add(buttons);
            if (!idle && m.App.Length > 0)
                root.Children.Add(new TextBlock { Text = "Playing in " + m.App + (m.Source != "Windows" && !m.App.StartsWith(m.Source) ? " · times from " + m.Source : ""), Foreground = t.FgDim, FontSize = Math.Max(9, t.FontSize * 0.85), HorizontalAlignment = HorizontalAlignment.Center });
            return root;
        }
    }

    /// <summary>
    /// A thin progress bar (track + accent fill). When the player allows it, a click or a drag on it jumps to that point
    /// (the hit area is taller than the bar, so it is easy to catch).
    /// </summary>
    public sealed class ProgressLine : Grid
    {
        readonly Border _track, _fill;
        double _fraction, _length;
        bool _canSeek, _dragging;

        public ProgressLine(Theme t, double thickness, double hitHeight)
        {
            Height = hitHeight;
            Background = Brushes.Transparent;       // the whole hit area takes the mouse
            _track = new Border
            {
                Height = thickness, CornerRadius = new CornerRadius(thickness / 2), Background = Theme.Faded(t.Fg, 18),
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
            _fill = new Border
            {
                Height = thickness, CornerRadius = new CornerRadius(thickness / 2), Background = t.MainAccent,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Width = 0
            };
            Children.Add(_track);
            Children.Add(_fill);
            SizeChanged += delegate { Draw(); };
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;       // never a bar / widget drag
                if (!_canSeek) return;
                _dragging = true;
                CaptureMouse();
                Follow(e);
            };
            MouseMove += delegate (object s, MouseEventArgs e) { if (_dragging) Follow(e); };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (!_dragging) return;
                _dragging = false;
                ReleaseMouseCapture();
                double f = Fraction(e);
                MediaSession.Send("seek:" + (f * _length).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            };
            LostMouseCapture += delegate { _dragging = false; };
        }

        double Fraction(MouseEventArgs e)
        {
            double w = ActualWidth;
            return w <= 0 ? 0 : Math.Max(0, Math.Min(1, e.GetPosition(this).X / w));
        }

        void Follow(MouseEventArgs e)
        {
            _fraction = Fraction(e);
            ToolTip = MediaInfo.Time(_fraction * _length);
            Draw();
        }

        /// <summary>0..1 through the track; canSeek = clicking jumps; length in seconds (for the tooltip and the jump).</summary>
        public void Set(double fraction, bool canSeek, double length)
        {
            _canSeek = canSeek;
            _length = length;
            Cursor = canSeek ? Cursors.Hand : null;
            if (_dragging) return;      // the drag shows where it will jump to
            _fraction = double.IsNaN(fraction) ? 0 : Math.Max(0, Math.Min(1, fraction));
            ToolTip = canSeek ? "Click or drag to jump" : null;
            Draw();
        }

        void Draw()
        {
            _fill.Width = Math.Max(0, ActualWidth * _fraction);
        }
    }

    /// <summary>
    /// A line of text at most maxWidth wide. Longer text scrolls slowly to its end and back (pausing at both ends), or, with
    /// scrolling off, is cut off with "…".
    /// </summary>
    public sealed class Marquee : Border
    {
        const double PixelsPerSecond = 30, PauseSeconds = 2;
        readonly TextBlock _text;
        readonly double _max;
        readonly bool _scroll;
        readonly Canvas _canvas = new Canvas();
        readonly TranslateTransform _move = new TranslateTransform();
        string _key;

        public Marquee(TextBlock text, double maxWidth, bool scroll)
        {
            _text = text;
            _max = maxWidth;
            _scroll = scroll;
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Left;
            if (!scroll)
            {
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.MaxWidth = maxWidth;
                Child = text;
                return;
            }
            text.TextTrimming = TextTrimming.None;
            text.RenderTransform = _move;
            ClipToBounds = true;
            _canvas.Children.Add(text);      // a canvas lets the text be wider than the box (only the box clips it)
            Child = _canvas;
        }

        /// <summary>Call after the text changed: sizes the box and starts / stops the scrolling.</summary>
        public void Update()
        {
            if (!_scroll) return;
            _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(_text.DesiredSize.Width), h = Math.Ceiling(_text.DesiredSize.Height);
            string key = (_text.Text ?? "") + "|" + w;
            if (key == _key) return;
            _key = key;

            _canvas.Width = w;
            _canvas.Height = h;
            _move.BeginAnimation(TranslateTransform.XProperty, null);
            _move.X = 0;
            if (w <= _max) { Width = w; return; }

            Width = _max;
            double over = w - _max;
            double travel = Math.Max(1.0, over / PixelsPerSecond);
            var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseSeconds))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-over, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseSeconds + travel))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-over, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PauseSeconds * 2 + travel))));
            Timeline.SetDesiredFrameRate(anim, 30);      // smooth enough, half the work
            _move.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}
