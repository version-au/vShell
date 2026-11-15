using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The taskbar's window previews: live thumbnails of an app's windows (or a plain list when previews are off), above a bottom bar /
    /// below a top bar. Opened by hovering a button (closes when the mouse moves away) or by clicking an app with several windows
    /// ("Pinned": stays until you click elsewhere). Never takes the focus.
    ///
    /// The window is NOT transparent (no AllowsTransparency): DWM thumbnails need a normal window to draw into. Windows 11 rounds its corners.
    /// </summary>
    public sealed class TaskPreviewWindow : Window
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

        readonly BarManager _mgr;
        readonly Theme _t;
        readonly bool _thumbs;
        readonly int _size;
        IntPtr _hwnd;
        bool _pending;
        readonly Dictionary<IntPtr, IntPtr> _thumb = new Dictionary<IntPtr, IntPtr>();      // window -> DWM thumbnail
        readonly List<KeyValuePair<IntPtr, FrameworkElement>> _spots = new List<KeyValuePair<IntPtr, FrameworkElement>>();
        List<TaskWindow> _windows = new List<TaskWindow>();
        FrameworkElement _anchor;
        bool _below;
        readonly DispatcherTimer _watch;
        int _away;

        public string Key { get; private set; }
        /// <summary>Opened by a click: stays open until a click somewhere else (a hover preview closes when the mouse leaves).</summary>
        public bool Pinned { get; set; }
        public bool IsOpen { get; private set; }

        public TaskPreviewWindow(BarManager mgr, Theme t, bool thumbnails, int size)
        {
            _mgr = mgr; _t = t; _thumbs = thumbnails; _size = Math.Max(120, size);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = t.PopupBg;
            BorderBrush = t.Track;
            BorderThickness = new Thickness(1);
            UseLayoutRounding = true;
            Title = "vShell previews";
            Left = -32000; Top = -32000;

            SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                WinStyle.HideFromTasks(_hwnd, true);
                int round = 2;     // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2 (Windows 11; ignored before)
                try { Native.DwmSetWindowAttribute(_hwnd, 33, ref round, 4); } catch { }
                _mgr.Tasks.Ignored.Add(_hwnd);
                if (_pending) { _pending = false; Build(); }
            };
            LayoutUpdated += delegate { PlaceThumbs(); };
            Closed += delegate
            {
                IsOpen = false;
                _watch.Stop();
                foreach (IntPtr th in _thumb.Values) DwmThumbnail.Unregister(th);
                _thumb.Clear();
                if (_hwnd != IntPtr.Zero) _mgr.Tasks.Ignored.Remove(_hwnd);
            };

            _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _watch.Tick += delegate { Watch(); };
        }

        /// <summary>Show (or update) the previews of these windows next to the anchor button.</summary>
        string _shownSig;

        static string WindowsSig(List<TaskWindow> windows)
        {
            var sb = new System.Text.StringBuilder();
            foreach (TaskWindow w in windows)
                sb.Append(w.Handle.ToInt64()).Append(w.Active ? 'A' : '-').Append(w.Minimized ? 'M' : '-').Append(w.HIcon.ToInt64()).Append(w.Title).Append('\n');
            return sb.ToString();
        }

        public void ShowFor(string key, List<TaskWindow> windows, FrameworkElement anchor, bool below)
        {
            // a window's menu is open over it: leave it as it is. Opening the menu changes the active window, the taskbar refreshes,
            // and rebuilding here put the (topmost) previews back on top - over the menu.
            if (_menuOpen) return;
            // already showing exactly this (the taskbar refreshed for something else): nothing to redo
            string sig = WindowsSig(windows);
            if (IsOpen && _hwnd != IntPtr.Zero && key == Key && sig == _shownSig && ReferenceEquals(anchor, _anchor) && below == _below) { _away = 0; return; }
            _shownSig = sig;
            Key = key;
            _windows = new List<TaskWindow>(windows);
            _anchor = anchor;
            _below = below;
            _away = 0;
            if (!IsOpen)
            {
                IsOpen = true;
                _pending = true;
                Show();
                _watch.Start();
                return;
            }
            if (_hwnd == IntPtr.Zero) { _pending = true; return; }
            Build();
        }

        // =================================================================
        // Content
        // =================================================================
        void Build()
        {
            // thumbnails: keep the ones still wanted, drop the rest, add the new ones
            var want = new HashSet<IntPtr>();
            if (_thumbs) foreach (TaskWindow w in _windows) want.Add(w.Handle);
            foreach (IntPtr h in new List<IntPtr>(_thumb.Keys))
                if (!want.Contains(h)) { DwmThumbnail.Unregister(_thumb[h]); _thumb.Remove(h); }
            foreach (IntPtr h in want)
                if (!_thumb.ContainsKey(h)) { IntPtr th = DwmThumbnail.Register(_hwnd, h); if (th != IntPtr.Zero) _thumb[h] = th; }

            _spots.Clear();
            var root = new Border { Padding = new Thickness(6) };
            if (_thumbs)
            {
                double maxW = 1200;
                MonitorInfo m = AnchorMonitor();
                if (m != null) maxW = m.WorkArea.Width / Dpi() * 0.92;
                var wrap = new WrapPanel { MaxWidth = maxW, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (TaskWindow w in _windows) wrap.Children.Add(Card(w));
                root.Child = wrap;
            }
            else
            {
                var list = new StackPanel { MinWidth = 220 };
                foreach (TaskWindow w in _windows) list.Children.Add(Row(w));
                root.Child = list;
            }
            Content = root;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!IsOpen) return;
                UpdateLayout();
                PlaceWindow();
                PlaceThumbs();
            }), DispatcherPriority.Loaded);
        }

        TextBlock CloseButton(TaskWindow w)
        {
            var x = new TextBlock
            {
                Text = "✕", Foreground = _t.FgDim, FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Close this window", Padding = new Thickness(3, 0, 3, 0)
            };
            x.MouseEnter += delegate { x.Foreground = _t.Fg; };
            x.MouseLeave += delegate { x.Foreground = _t.FgDim; };
            x.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            x.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TaskList.Close(w.Handle); };
            return x;
        }

        FrameworkElement Header(TaskWindow w, double width)
        {
            var head = new DockPanel { Width = width, LastChildFill = true };
            var x = CloseButton(w);
            DockPanel.SetDock(x, Dock.Right);
            head.Children.Add(x);
            var img = new Image { Source = TaskbarModule.WindowIcon(w), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            DockPanel.SetDock(img, Dock.Left);
            head.Children.Add(img);
            head.Children.Add(new TextBlock
            {
                Text = w.Title, Foreground = w.Minimized ? _t.FgDim : _t.Fg, FontFamily = _t.Font, FontSize = Math.Max(11, _t.FontSize * 0.95),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            });
            return head;
        }

        /// <summary>A preview: title row and the live picture.</summary>
        FrameworkElement Card(TaskWindow w)
        {
            double boxW = _size, boxH = Math.Round(_size * 0.68);
            double pw = boxW, ph = Math.Round(boxW * 0.56);
            IntPtr th;
            if (_thumb.TryGetValue(w.Handle, out th))
            {
                int sw, sh;
                DwmThumbnail.SourceSize(th, out sw, out sh);
                if (sw > 0 && sh > 0)
                {
                    double k = Math.Min(boxW / sw, boxH / sh);
                    pw = Math.Max(40, Math.Round(sw * k));
                    ph = Math.Max(30, Math.Round(sh * k));
                }
            }
            double width = Math.Max(pw, 150);

            var col = new StackPanel();
            col.Children.Add(Header(w, width));
            // behind the live picture: the app's icon (shows if the window has no picture, e.g. some minimised apps)
            var spot = new Border
            {
                Width = pw, Height = ph, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Background = Theme.Faded(_t.FgDim, 10), CornerRadius = new CornerRadius(3),
                Child = new Image { Source = TaskbarModule.WindowIcon(w), Width = 32, Height = 32, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            col.Children.Add(spot);
            if (_thumb.ContainsKey(w.Handle)) _spots.Add(new KeyValuePair<IntPtr, FrameworkElement>(w.Handle, spot));

            return Clickable(new Border
            {
                Child = col, Padding = new Thickness(8, 6, 8, 8), Margin = new Thickness(3), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = w.Active ? (Brush)_t.MainAccent : Brushes.Transparent, Background = Brushes.Transparent
            }, w);
        }

        /// <summary>Previews off: one line per window.</summary>
        FrameworkElement Row(TaskWindow w)
        {
            var b = new Border
            {
                Child = Header(w, double.NaN), Padding = new Thickness(8, 6, 6, 6), Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(5),
                MaxWidth = 380, Background = Brushes.Transparent,
                BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = w.Active ? (Brush)_t.MainAccent : Brushes.Transparent
            };
            return Clickable(b, w);
        }

        Border Clickable(Border b, TaskWindow w)
        {
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Bring(w);
                Close();
            };
            b.MouseDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                TaskList.Close(w.Handle);
            };
            // right-click: the window's own menu, as on Windows' taskbar thumbnails
            b.MouseRightButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                POINT c = Native.CursorPos();
                _menuOpen = true;
                try { WindowMenu.Show(w.Handle, c.X, c.Y); }
                catch (Exception ex) { Log.Write("Window menu: " + ex.Message); }
                finally { _menuOpen = false; }
                b.Background = Brushes.Transparent;
                if (IsOpen) Close();
                _mgr.Tasks.Refresh();
            };
            return b;
        }

        /// <summary>A window's menu is open (from a right-click): the previews stay while it is.</summary>
        bool _menuOpen;

        /// <summary>Switch to the window (restoring it if minimised) - unlike a taskbar click, never minimises it.</summary>
        void Bring(TaskWindow w)
        {
            TaskList.BringToFront(w.Handle);
            _mgr.Tasks.Refresh();
        }

        // =================================================================
        // Placing
        // =================================================================
        double Dpi()
        {
            try { return VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { return 1; }
        }

        bool AnchorRect(out RECT r)
        {
            r = new RECT();
            if (_anchor == null || PresentationSource.FromVisual(_anchor) == null || _anchor.ActualWidth <= 0) return false;
            Point a = _anchor.PointToScreen(new Point(0, 0));
            Point b = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
            r.Left = (int)a.X; r.Top = (int)a.Y; r.Right = (int)b.X; r.Bottom = (int)b.Y;
            return true;
        }

        MonitorInfo AnchorMonitor()
        {
            RECT a;
            return AnchorRect(out a) ? MonitorHelper.FromPoint((a.Left + a.Right) / 2, (a.Top + a.Bottom) / 2) : MonitorHelper.Primary();
        }

        void PlaceWindow()
        {
            RECT a;
            if (_hwnd == IntPtr.Zero || !AnchorRect(out a)) return;
            double k = Dpi();
            int w = (int)Math.Round(ActualWidth * k), h = (int)Math.Round(ActualHeight * k);
            int x = (a.Left + a.Right) / 2 - w / 2;
            int y = _below ? a.Bottom + 6 : a.Top - h - 6;
            MonitorInfo m = MonitorHelper.FromPoint((a.Left + a.Right) / 2, (a.Top + a.Bottom) / 2);
            if (m != null)
            {
                x = Math.Max(m.Bounds.Left + 4, Math.Min(m.Bounds.Right - w - 4, x));
                y = Math.Max(m.Bounds.Top + 4, Math.Min(m.Bounds.Bottom - h - 4, y));
            }
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        /// <summary>Put each live thumbnail over its placeholder (DWM draws in window pixels).</summary>
        void PlaceThumbs()
        {
            if (_hwnd == IntPtr.Zero || _spots.Count == 0) return;
            double k = Dpi();
            foreach (var kv in _spots)
            {
                IntPtr th;
                if (!_thumb.TryGetValue(kv.Key, out th)) continue;
                FrameworkElement spot = kv.Value;
                if (!spot.IsVisible || PresentationSource.FromVisual(spot) == null) continue;
                Point p;
                try { p = spot.TransformToAncestor(this).Transform(new Point(0, 0)); } catch { continue; }
                int l = (int)Math.Round(p.X * k), t = (int)Math.Round(p.Y * k);
                DwmThumbnail.Place(th, l, t, l + (int)Math.Round(spot.ActualWidth * k), t + (int)Math.Round(spot.ActualHeight * k));
            }
        }

        // =================================================================
        // When to close
        // =================================================================
        void Watch()
        {
            if (!IsOpen || _menuOpen) return;
            POINT c = Native.CursorPos();
            RECT me, a;
            bool overMe = _hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out me) && In(me, c, 0);
            bool overAnchor = AnchorRect(out a) && In(a, c, 6);
            if (overMe || overAnchor) { _away = 0; return; }
            _away++;
            if (!Pinned)
            {
                if (_away >= 3) Close();         // ~0.35 s after the mouse left
                return;
            }
            // opened by a click: a click anywhere else closes it
            if ((GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0) Close();
        }

        static bool In(RECT r, POINT p, int slack)
        {
            return p.X >= r.Left - slack && p.X < r.Right + slack && p.Y >= r.Top - slack && p.Y < r.Bottom + slack;
        }
    }
}
