using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Settings > Desktop > Wallpaper: your monitors as they're arranged, each showing its wallpaper. Click one to change it (or drop a
    /// picture / a folder on it); below, its picture, fit, background (behind a Fit / Center picture) and slideshow. "Each monitor",
    /// "Same on all" or "Span across all". Changes are set straight away (a moment after the last one).
    /// </summary>
    public partial class SettingsWindow
    {
        static int _wallSel;                  // the monitor being edited (index in MonitorHelper.GetAll)
        DispatcherTimer _wallTimer;
        TextBlock _wallStatus;

        static readonly int[] SlideMinutes = { 5, 15, 30, 60, 180, 1440 };
        static readonly string[] SlideNames = { "5 min", "15 min", "30 min", "1 hour", "3 hours", "Daily" };

        /// <summary>Set the wallpapers a moment after the last change (so dragging a slider doesn't make pictures for every step).</summary>
        void ApplyWallSoon()
        {
            _mgr.SettingsChanged();
            if (!S.PerMonitorWallpaper) return;
            if (_wallTimer == null)
            {
                _wallTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                _wallTimer.Tick += delegate
                {
                    _wallTimer.Stop();
                    Mouse.OverrideCursor = Cursors.Wait;
                    string err;
                    try { err = _mgr.ApplyWallpapers(); }
                    finally { Mouse.OverrideCursor = null; }
                    _mgr.SettingsChanged();
                    if (_wallStatus != null) { _wallStatus.Text = err ?? ""; _wallStatus.Visibility = err == null ? Visibility.Collapsed : Visibility.Visible; }
                };
            }
            _wallTimer.Stop();
            _wallTimer.Start();
        }

        void RebuildWall() { Dispatcher.BeginInvoke(new Action(BuildDesktopPage)); }

        void BuildWallpaperSection(StackPanel root)
        {
            Header(root, "Wallpaper");
            Check(root, "Let vShell set the wallpapers", delegate { return S.PerMonitorWallpaper; }, delegate (bool v)
            {
                S.PerMonitorWallpaper = v;
                if (v) ApplyWallSoon();
                else { _mgr.RestoreWallpapers(); _mgr.SettingsChanged(); }
                RebuildWall();
            });
            if (!S.PerMonitorWallpaper)
            {
                Note(root, "Turn this on for a picture per monitor (each with its own fit and background), the same picture on all, or one spanned " +
                           "across them - and slideshows. vShell uses Windows' own per-monitor wallpaper, and puts your wallpapers back when you turn it off.");
                return;
            }

            List<MonitorInfo> mons = MonitorHelper.GetAll();
            if (mons.Count == 0) return;
            _wallSel = Math.Max(0, Math.Min(mons.Count - 1, _wallSel));
            bool all = S.WallpaperMode != "Each";

            Place(root, Row("Show", Segmented(WallpaperService.ModeNames,
                delegate { return WallpaperService.ModeNames[Math.Max(0, Array.IndexOf(WallpaperService.Modes, S.WallpaperMode))]; },
                delegate (string v)
                {
                    S.WallpaperMode = WallpaperService.Modes[Math.Max(0, Array.IndexOf(WallpaperService.ModeNames, v))];
                    ApplyWallSoon();
                    RebuildWall();
                })));

            // ---- the monitors, as they're arranged ----
            Place(root, MonitorMap(mons));

            // ---- the one being edited ----
            MonitorInfo mon = mons[_wallSel];
            MonitorWallpaper e = all ? WallpaperService.AllEntry(S) : WallpaperService.EnsureEntry(S, mon);
            string who = all ? (S.WallpaperMode == "Span" ? "Across all monitors" : "All monitors")
                             : "Monitor " + (_wallSel + 1) + (mon.IsPrimary ? " (main)" : "") + "   ·   " + mon.Bounds.Width + " × " + mon.Bounds.Height;
            var title = Txt(who, "vsFg", 14, true);
            title.Margin = new Thickness(0, 10, 0, 2);
            Place(root, title);

            // picture
            var pathBox = new TextBox { Text = e.Path ?? "", VerticalContentAlignment = VerticalAlignment.Center };
            var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
            var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(browse, Dock.Right);
            var prow = new DockPanel();
            prow.Children.Add(clear);
            prow.Children.Add(browse);
            prow.Children.Add(pathBox);
            Action<string> setPath = delegate (string v)
            {
                v = (v ?? "").Trim().Trim('"');
                if (v == (e.Path ?? "")) return;
                e.Path = v;
                ApplyWallSoon();
                RebuildWall();
            };
            pathBox.LostFocus += delegate { if (!_loading) setPath(pathBox.Text); };
            pathBox.KeyDown += delegate (object s2, KeyEventArgs k) { if (k.Key == Key.Enter) setPath(pathBox.Text); };
            browse.Click += delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Wallpaper - " + who,
                    Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files (*.*)|*.*"
                };
                try { if (!string.IsNullOrEmpty(e.Path)) dlg.InitialDirectory = Path.GetDirectoryName(e.Path); } catch { }
                if (dlg.ShowDialog(this) == true) setPath(dlg.FileName);
            };
            clear.Click += delegate { setPath(""); };
            Place(root, Row(string.IsNullOrWhiteSpace(e.SlideFolder) ? "Picture" : "Picture (the slideshow below is used instead)", prow, true));

            bool noPic = WallpaperService.ImageOf(e) == null;

            // fit (only with a picture)
            if (!noPic)
            Place(root, Row(S.WallpaperMode == "Span" ? "Fit (spanned pictures fill the whole desktop)" : "Fit",
                S.WallpaperMode == "Span" ? (UIElement)Txt("Span", "vsFg", 13, false) :
                Segmented(WallpaperService.FitModes, delegate { return e.Fit; }, delegate (string v) { e.Fit = v; ApplyWallSoon(); RebuildWall(); })));

            // background
            if (S.WallpaperMode != "Span" || noPic)
            {
                var sw = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
                foreach (string b in WallpaperService.Backgrounds) sw.Children.Add(Swatch(e, b));
                string bgLabel = !noPic ? "Background (around a Fit or Center picture)"
                    : S.WallpaperMode == "Span" ? "Background (no picture: fills the whole desktop)"
                    : S.WallpaperMode == "Same" ? "Background (no picture: fills every monitor)"
                    : "Background (no picture: fills the whole monitor)";
                Place(root, Row(bgLabel, sw, true));
                if (noPic && (string.IsNullOrEmpty(e.Background) || e.Background == "Blur"))
                    Note(root, "Blur needs a picture - with no picture this keeps the wallpaper the monitor had. Pick a colour or gradient to fill it.");
                if (e.Background == "Solid")
                    ColorRow(root, "Colour", delegate { return e.Color1; }, delegate (string v) { e.Color1 = v; ApplyWallSoon(); }, false, false);
                if (e.Background == "Gradient")
                {
                    ColorRow(root, "From", delegate { return e.Color1; }, delegate (string v) { e.Color1 = v; ApplyWallSoon(); }, false, false);
                    ColorRow(root, "To", delegate { return e.Color2; }, delegate (string v) { e.Color2 = v; ApplyWallSoon(); }, false, false);
                    SliderRow(root, "Angle (0 = left to right, 90 = top to bottom)", 0, 359, delegate { return e.Angle; }, delegate (double v) { e.Angle = (int)v; ApplyWallSoon(); });
                }
            }

            // slideshow
            var sTitle = Txt("Slideshow", "vsFg", 13.5, true);
            sTitle.Margin = new Thickness(0, 12, 0, 2);
            Place(root, sTitle);
            var folderBox = new TextBox { Text = e.SlideFolder ?? "", VerticalContentAlignment = VerticalAlignment.Center };
            var fBrowse = new Button { Content = "Choose folder…", Margin = new Thickness(6, 0, 0, 0) };
            var fClear = new Button { Content = "Off", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(fClear, Dock.Right);
            DockPanel.SetDock(fBrowse, Dock.Right);
            var frow = new DockPanel();
            frow.Children.Add(fClear);
            frow.Children.Add(fBrowse);
            frow.Children.Add(folderBox);
            Action<string> setFolder = delegate (string v)
            {
                v = (v ?? "").Trim().Trim('"');
                if (v == (e.SlideFolder ?? "")) return;
                e.SlideFolder = v;
                e.SlideCurrent = "";
                e.SlideNext = DateTime.MinValue;
                if (v.Length > 0) WallpaperService.Next(e);
                ApplyWallSoon();
                RebuildWall();
            };
            folderBox.LostFocus += delegate { if (!_loading) setFolder(folderBox.Text); };
            folderBox.KeyDown += delegate (object s2, KeyEventArgs k) { if (k.Key == Key.Enter) setFolder(folderBox.Text); };
            fBrowse.Click += delegate
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Slideshow folder - " + who };
                try { if (Directory.Exists(e.SlideFolder)) dlg.InitialDirectory = e.SlideFolder; } catch { }
                if (dlg.ShowDialog(this) == true) setFolder(dlg.FolderName);
            };
            fClear.Click += delegate { setFolder(""); };
            Place(root, Row("Folder of pictures (empty = no slideshow)", frow, true));
            if (!string.IsNullOrWhiteSpace(e.SlideFolder))
            {
                int count = WallpaperService.Pictures(e.SlideFolder).Count;
                Place(root, Row("Change every", Segmented(SlideNames,
                    delegate { int i = Array.IndexOf(SlideMinutes, e.SlideMinutes); return SlideNames[i < 0 ? 2 : i]; },
                    delegate (string v) { e.SlideMinutes = SlideMinutes[Math.Max(0, Array.IndexOf(SlideNames, v))]; e.SlideNext = DateTime.UtcNow.AddMinutes(e.SlideMinutes); _mgr.SettingsChanged(); })));
                Check(root, "Shuffle", delegate { return e.SlideShuffle; }, delegate (bool v) { e.SlideShuffle = v; _mgr.SettingsChanged(); });
                var nb = Btn("Next picture", delegate { if (WallpaperService.Next(e)) { ApplyWallSoon(); RebuildWall(); } });
                nb.HorizontalAlignment = HorizontalAlignment.Left;
                Place(root, nb);
                Note(root, count == 0 ? "There are no pictures in that folder." :
                           count + " picture" + (count == 1 ? "" : "s") + " (jpg, png, bmp, gif, tif, webp). " +
                           (string.IsNullOrEmpty(e.SlideCurrent) ? "" : "Showing " + Path.GetFileName(e.SlideCurrent) + "."));
            }

            _wallStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            Ref(_wallStatus, TextBlock.ForegroundProperty, "vsAccent");
            Place(root, _wallStatus);

            var btns = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var restore = Btn("Restore my original wallpapers", delegate
            {
                _mgr.RestoreWallpapers();
                S.PerMonitorWallpaper = false;
                _mgr.SettingsChanged();
                RebuildWall();
            });
            btns.Children.Add(restore);
            Place(root, btns);
            Note(root, "Click a monitor above to change it, or drop a picture (or a folder, for a slideshow) on it. vShell makes each monitor's picture " +
                       "at its exact size and sets it with Windows' own per-monitor wallpaper, so each can have its own fit and background, and a monitor " +
                       "that's unplugged and plugged back in gets its picture back. Monitors without a picture keep the wallpaper they had.");
        }

        /// <summary>A background choice as a small swatch (its gradient / colour, or a hint of the picture blurred).</summary>
        Border Swatch(MonitorWallpaper e, string b)
        {
            Theme th = Theme.From(S, 12, 0);
            string was = e.Background;
            e.Background = b;
            Brush fill = WallpaperService.BackgroundBrush(e, th);
            e.Background = was;
            if (b == "Black") fill = Brushes.Black;
            if (b == "Blur") fill = new LinearGradientBrush(Color.FromRgb(0x55, 0x60, 0x80), Color.FromRgb(0x30, 0x24, 0x40), 45);
            bool on = (string.IsNullOrEmpty(e.Background) ? "Blur" : e.Background) == b;
            var chip = new Border { Width = 46, Height = 30, CornerRadius = new CornerRadius(6), Background = fill };
            if (b == "Blur") chip.Child = new TextBlock { Text = "blur", Foreground = Brushes.White, FontSize = 10.5, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (b == "Solid" || b == "Gradient") chip.Child = new TextBlock { Text = b == "Solid" ? "colour" : "custom", Foreground = NoteRender.Contrast(fill ?? Brushes.Black), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var stack = new StackPanel { Width = 56 };
            stack.Children.Add(chip);
            var name = Txt(b, "vsFg", 10.5, false);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(name);
            var card = new Border { Child = stack, Padding = new Thickness(4), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(on ? 2 : 1), Cursor = Cursors.Hand };
            Ref(card, Border.BorderBrushProperty, on ? "vsAccent" : "vsLine");
            card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs ev) { ev.Handled = true; if (on) return; e.Background = b; ApplyWallSoon(); RebuildWall(); };
            return card;
        }

        /// <summary>The monitors drawn where they are, each with its wallpaper; click to pick, drop a picture / folder to set it.</summary>
        UIElement MonitorMap(List<MonitorInfo> mons)
        {
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (MonitorInfo m in mons)
            {
                left = Math.Min(left, m.Bounds.Left); top = Math.Min(top, m.Bounds.Top);
                right = Math.Max(right, m.Bounds.Right); bottom = Math.Max(bottom, m.Bounds.Bottom);
            }
            double k = Math.Min(560.0 / Math.Max(1, right - left), 230.0 / Math.Max(1, bottom - top));
            var canvas = new Canvas { Width = (right - left) * k, Height = (bottom - top) * k, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
            bool span = S.WallpaperMode == "Span";
            if (span)
            {
                // one picture across the whole desktop
                MonitorWallpaper allE = WallpaperService.AllEntry(S);
                string img = WallpaperService.ImageOf(allE);
                System.Windows.Media.Imaging.BitmapImage bi = img != null ? WallpaperService.Load(img, 800) : null;
                if (bi != null) canvas.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                else if (WallpaperService.ColourOnly(allE)) canvas.Background = WallpaperService.BackgroundBrush(allE, Theme.From(S, 12, 0)) ?? Brushes.Black;   // the colour / gradient across all of them
            }
            for (int i = 0; i < mons.Count; i++)
            {
                MonitorInfo m = mons[i];
                int idx = i;
                double x = (m.Bounds.Left - left) * k, y = (m.Bounds.Top - top) * k, w = m.Bounds.Width * k, h = m.Bounds.Height * k;
                var g = new Grid { Width = w - 6, Height = h - 6 };
                if (!span)
                {
                    MonitorWallpaper e = WallpaperService.EntryFor(S, m);
                    System.Windows.Media.Imaging.BitmapSource pv = e != null ? WallpaperService.Preview(S, e, m.Bounds.Width, m.Bounds.Height, (int)Math.Ceiling(w * 1.5)) : null;
                    if (pv != null) g.Children.Add(new Image { Source = pv, Stretch = Stretch.Fill });
                    else
                    {
                        var none = Txt("No picture -\nclick to choose", "vsFg", 11, false);
                        none.TextAlignment = TextAlignment.Center; none.Opacity = 0.7;
                        none.HorizontalAlignment = HorizontalAlignment.Center; none.VerticalAlignment = VerticalAlignment.Center;
                        g.Children.Add(none);
                    }
                }
                double d = Math.Max(18, Math.Min(26, h * 0.22));
                g.Children.Add(new Border
                {
                    Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6),
                    Child = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.White, FontSize = d * 0.55, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                bool sel = S.WallpaperMode == "Each" && idx == _wallSel;
                var tile = new Border
                {
                    Child = g, Width = w - 4, Height = h - 4, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(sel ? 3 : 1), ClipToBounds = true,
                    Cursor = Cursors.Hand, AllowDrop = true, ToolTip = "Monitor " + (i + 1) + (m.IsPrimary ? " (main)" : "") + " - " + m.Bounds.Width + " × " + m.Bounds.Height +
                                                                       "\nClick to change it - or drop a picture, or a folder for a slideshow",
                    Background = span ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26))
                };
                Ref(tile, Border.BorderBrushProperty, sel ? "vsAccent" : "vsLine");
                Canvas.SetLeft(tile, x + 2); Canvas.SetTop(tile, y + 2);
                tile.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs ev)
                {
                    ev.Handled = true;
                    if (S.WallpaperMode != "Each") { S.WallpaperMode = "Each"; _mgr.SettingsChanged(); }     // clicking one monitor: edit that one
                    _wallSel = idx;
                    RebuildWall();
                };
                tile.DragOver += delegate (object s, DragEventArgs ev) { ev.Effects = ev.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; ev.Handled = true; };
                tile.Drop += delegate (object s, DragEventArgs ev)
                {
                    var files = ev.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files == null || files.Length == 0) return;
                    MonitorWallpaper target = S.WallpaperMode == "Each" ? WallpaperService.EnsureEntry(S, mons[idx]) : WallpaperService.AllEntry(S);
                    if (Directory.Exists(files[0])) { target.SlideFolder = files[0]; target.SlideCurrent = ""; target.SlideNext = DateTime.MinValue; WallpaperService.Next(target); }
                    else { target.Path = files[0]; target.SlideFolder = ""; }
                    _wallSel = idx;
                    ApplyWallSoon();
                    RebuildWall();
                };
                canvas.Children.Add(tile);
            }
            var frame = new Border { Child = canvas, Padding = new Thickness(10), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Stretch };
            Ref(frame, Border.BackgroundProperty, "vsCard");
            return frame;
        }
    }
}
