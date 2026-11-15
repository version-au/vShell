using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>How the tray icons are coloured: their own colours, or a theme colour laid over them (Tint).</summary>
    sealed class TrayLook
    {
        public string Mode = "Original";
        public SolidColorBrush Brush;
        /// <summary>Tint: how much of the colour lies over the icon (0..1).</summary>
        public double Strength = 0.5;
    }

    /// <summary>One notification-area icon. Clicks are forwarded to the owning app; dragging starts a pin/unpin drag.</summary>
    sealed class TrayIconView : Border
    {
        readonly TrayIcon _ic;
        public TrayIcon Icon { get { return _ic; } }
        readonly Image _img;
        /// <summary>Tint: the icon's shape, filled with the theme colour.</summary>
        readonly ImageBrush _shape;
        IntPtr _shownHIcon;
        Point _down;
        bool _isDown;
        int _clicks;

        public TrayIconView(TrayIcon ic, ImageSource src, double size, Theme t, TrayLook look, TrayHost host, Action afterClick,
                            Action dragStarted, Action dragEnded)
        {
            _ic = ic;
            _shownHIcon = ic.HIcon;
            Padding = new Thickness(5, 0, 5, 0);
            Background = Brushes.Transparent;
            if (!string.IsNullOrEmpty(ic.Tip)) ToolTip = ic.Tip;
            _img = new Image { Width = size, Height = size, Source = src, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            if (look == null || look.Mode != "Tint" || look.Brush == null) Child = _img;
            else
            {
                // the theme colour over the icon, in the icon's own shape (its see-through parts stay see-through)
                _shape = new ImageBrush(src) { Stretch = Stretch.Uniform };
                var fill = new System.Windows.Shapes.Rectangle
                {
                    Width = size, Height = size, Fill = look.Brush, OpacityMask = _shape, Opacity = look.Strength,
                    VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
                };
                var both = new Grid { VerticalAlignment = VerticalAlignment.Center };
                both.Children.Add(_img);
                both.Children.Add(fill);
                Child = both;
            }

            MouseEnter += delegate { Background = t.Hover; POINT c = Native.CursorPos(); host.MouseEnter(_ic, c.X, c.Y); };
            MouseLeave += delegate
            {
                if (!IsMouseCaptured) Background = Brushes.Transparent;
                POINT c = Native.CursorPos(); host.MouseLeave(_ic, c.X, c.Y);
            };
            LostMouseCapture += delegate { Background = IsMouseOver ? t.Hover : Brushes.Transparent; };

            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _down = e.GetPosition(this);
                _isDown = true;
                _clicks = e.ClickCount;
                CaptureMouse();     // keep receiving moves even if the pointer leaves the icon quickly
            };

            MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!_isDown) { POINT c = Native.CursorPos(); host.MouseMove(_ic, c.X, c.Y); }
                if (!_isDown || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(this);
                if (Math.Abs(p.X - _down.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(p.Y - _down.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDown = false;
                    ReleaseMouseCapture();
                    if (dragStarted != null) dragStarted();
                    try { DragDrop.DoDragDrop(this, new DataObject(TrayModule.DragFormat, _ic.Key), DragDropEffects.Move); }
                    finally { if (dragEnded != null) dragEnded(); }
                }
            };

            // The app only hears about the click once it is certain it is not a drag.
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (!_isDown) return;
                _isDown = false;
                ReleaseMouseCapture();
                Point up = e.GetPosition(this);
                if (up.X < 0 || up.Y < 0 || up.X > ActualWidth || up.Y > ActualHeight) return;   // released outside: not a click
                if (SystemTrayIcons.Click(_ic, false, this)) return;   // Windows' own volume / mic / ... icons (their menu opens here)
                POINT p = Native.CursorPos();
                if (_clicks >= 2) host.LeftDouble(_ic, p.X, p.Y);
                else host.LeftDown(_ic, p.X, p.Y);
                host.LeftUp(_ic, p.X, p.Y);
                if (afterClick != null) afterClick();
            };

            MouseRightButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (SystemTrayIcons.Handles(_ic)) return;
                POINT p = Native.CursorPos(); host.RightDown(_ic, p.X, p.Y);
            };
            MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (SystemTrayIcons.Click(_ic, true, this)) return;
                POINT p = Native.CursorPos(); host.RightUp(_ic, p.X, p.Y);
                if (afterClick != null) afterClick();
            };
        }

        /// <summary>
        /// The app changed its icon or tooltip: update this one in place (making a new one would close a tooltip you're reading, and it
        /// would pop up again - a flash every time an app updates its icon). picture = makes the new picture, only when it changed.
        /// </summary>
        public void Refresh(Func<ImageSource> picture, bool force)
        {
            if (force || _ic.HIcon != _shownHIcon)
            {
                ImageSource src = picture();
                if (src != null)
                {
                    _img.Source = src;
                    if (_shape != null) _shape.ImageSource = src;
                }
                _shownHIcon = _ic.HIcon;
            }
            string tip = string.IsNullOrEmpty(_ic.Tip) ? null : _ic.Tip;
            if (!string.Equals(ToolTip as string, tip)) ToolTip = tip;
        }
    }

    /// <summary>
    /// Notification area. With GroupIcons on, only the pinned icons show on the bar and the rest sit behind an arrow
    /// (click it for a popup). Drag an icon out of the popup onto the bar to pin it; drag a pinned icon onto the arrow to unpin it.
    /// Drag an icon along the bar to move it: the order is saved (ModuleConfig.TrayOrder) and the popup follows it too.
    /// The bar itself is the drop target (BarWindow forwards the drag events here), so dropping anywhere on the bar works.
    /// </summary>
    public sealed class TrayModule : Border
    {
        public const string DragFormat = "vShell.TrayKey";

        readonly BarManager _mgr;
        readonly BarWindow _bar;
        readonly ModuleConfig _cfg;
        readonly StackPanel _pinned = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Border _arrow = new Border();
        readonly TextBlock _arrowGlyph;
        readonly DispatcherTimer _debounce;
        readonly List<TrayIcon> _overflow = new List<TrayIcon>();
        PopupWindow _popup;
        Panel _popupPanel;

        public TrayModule(BarManager mgr, BarWindow bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            Theme t = bar.Theme.ForModule(cfg);

            Margin = new Thickness(2, 0, 2, 0);
            Background = Brushes.Transparent;
            AllowDrop = true;

            bool bottom = bar.Placement.Edge != "Top";
            _arrowGlyph = IconUtil.Glyph(bottom ? IconUtil.GlyphChevronUp : IconUtil.GlyphChevronDown, t.Fg, 11);
            _arrow.Child = _arrowGlyph;
            _arrow.Padding = new Thickness(6, 0, 6, 0);
            _arrow.Background = Brushes.Transparent;
            _arrow.Cursor = Cursors.Hand;
            _arrow.ToolTip = "Show hidden icons (drag icons here to hide them, out of the popup onto the bar to pin them)";
            _arrow.MouseEnter += delegate { _arrow.Background = t.Hover; };
            _arrow.MouseLeave += delegate { _arrow.Background = Brushes.Transparent; };
            _arrow.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            _arrow.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_pinned);
            row.Children.Add(_arrow);
            Child = row;

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _debounce.Tick += delegate { _debounce.Stop(); Rebuild(); };

            // vShell's own network icon: looked at every few seconds while the bar shows it, redrawn only when it changed
            _netTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _netTimer.Tick += delegate { if (_cfg.TrayNetwork && NetState() != _netSig) Rebuild(); };
            Loaded += delegate { if (_cfg.TrayNetwork) _netTimer.Start(); };
            Unloaded += delegate { _netTimer.Stop(); };
        }

        // ---- vShell's own network icon (Windows 11 keeps its own in Explorer's taskbar, where other trays can't get it) ----
        readonly TrayIcon _netIcon = new TrayIcon { Guid = TrayHost.NetworkGuid, Key = TrayHost.OwnNetworkKey };
        readonly DispatcherTimer _netTimer;
        string _netSig = "", _netKind = "none", _netTip = "";
        double _netLevel;
        ImageSource _netImage;

        /// <summary>What the network is now (Wi-Fi + signal, wired, none) as a signature; sets the kind, level and tooltip.</summary>
        string NetState()
        {
            NetSample s = NetworkSampler.Shared.Sample();
            string ssid; int signal;
            Wifi.Get(out ssid, out signal);
            if (!s.Connected) { _netKind = "none"; _netLevel = 0; _netTip = "Not connected"; }
            else if (!string.IsNullOrEmpty(ssid))
            {
                _netKind = "wifi";
                _netLevel = signal < 0 || signal >= 80 ? 1 : signal >= 55 ? 0.72 : signal >= 30 ? 0.48 : 0.26;
                _netTip = ssid + (signal >= 0 ? "  ·  Wi-Fi " + signal + " %" : "  ·  Wi-Fi");
            }
            else { _netKind = "wired"; _netLevel = 1; _netTip = (s.Adapter.Length > 0 ? s.Adapter : "Wired") + "  ·  connected"; }
            return _netKind + "|" + _netLevel + "|" + _netTip;
        }

        /// <summary>
        /// Windows' own network icon is in the tray already (Windows 10, or a Windows that still sends it): then vShell doesn't draw a
        /// second one. Matched by its fixed id, or an Explorer icon whose tooltip is about the network.
        /// </summary>
        static bool WindowsNetworkIcon(TrayHost host)
        {
            foreach (TrayIcon ic in host.Icons)
            {
                if (ic.Hidden) continue;
                if (ic.Guid == TrayHost.NetworkGuid) return true;
                bool explorer = (ic.Key ?? "").IndexOf("\\explorer.exe#", StringComparison.OrdinalIgnoreCase) >= 0;
                string tip = (ic.Tip ?? "").ToLowerInvariant();
                if (explorer && (tip.Contains("internet") || tip.Contains("network") || tip.Contains("wi-fi") || tip.Contains("ethernet"))) return true;
            }
            return false;
        }

        /// <summary>The network glyph (Wi-Fi or wired) at the tray's size; Wi-Fi's arcs lit up to the signal level, the rest faded.</summary>
        static ImageSource NetImage(string glyph, SolidColorBrush fg, double level)
        {
            var tf = new Typeface(IconUtil.GlyphFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 16, Brushes.Black, 1.0);
            Geometry g = ft.BuildGeometry(new Point(0, 0));
            Rect b = g.Bounds;
            if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return null;
            double k = 15.0 / Math.Max(b.Width, b.Height);
            var tg = new TransformGroup();
            tg.Children.Add(new TranslateTransform(-b.X, -b.Y));
            tg.Children.Add(new ScaleTransform(k, k));
            tg.Children.Add(new TranslateTransform((16 - b.Width * k) / 2, (16 - b.Height * k) / 2));
            g.Transform = tg;
            var pic = new DrawingGroup();
            pic.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));    // a steady 16 x 16
            if (level >= 1) pic.Children.Add(new GeometryDrawing(fg, null, g));
            else
            {
                pic.Children.Add(new GeometryDrawing(Theme.Faded(fg, 35), null, g));
                // the arcs spread out from the dot at the bottom middle: a circle round it shows the lit ones
                double h = b.Height * k, bottom = (16 + h) / 2, r = h * level * 1.05;
                var lit = new DrawingGroup { ClipGeometry = new EllipseGeometry(new Point(8, bottom), r, r) };
                lit.Children.Add(new GeometryDrawing(fg, null, g));
                pic.Children.Add(lit);
            }
            return new DrawingImage(pic);
        }

        public void QueueRebuild() { if (!_debounce.IsEnabled) _debounce.Start(); }

        /// <summary>The icons in the order you dragged them into (the rest after them, in the order Windows has them).</summary>
        List<TrayIcon> Ordered(IEnumerable<TrayIcon> icons)
        {
            var list = new List<TrayIcon>(icons);
            if (_cfg.TrayOrder.Count == 0) return list;
            var at = new Dictionary<string, int>();
            for (int i = 0; i < _cfg.TrayOrder.Count; i++) if (!at.ContainsKey(_cfg.TrayOrder[i])) at[_cfg.TrayOrder[i]] = i;
            var orig = new Dictionary<TrayIcon, int>();
            for (int i = 0; i < list.Count; i++) orig[list[i]] = i;
            list.Sort(delegate (TrayIcon a, TrayIcon b)
            {
                int x, y;
                bool ka = at.TryGetValue(a.Key ?? "", out x), kb = at.TryGetValue(b.Key ?? "", out y);
                if (ka && kb) return x.CompareTo(y);
                if (ka != kb) return ka ? -1 : 1;
                return orig[a].CompareTo(orig[b]);
            });
            return list;
        }

        // ---- reordering: where a dragged icon would go, and a thin mark there ----------------------
        Border _mark;

        /// <summary>The position in the bar's icon row the drag is over (0 = before the first).</summary>
        int DropIndex(DragEventArgs e)
        {
            int i = 0;
            foreach (UIElement c in _pinned.Children)
            {
                var v = c as TrayIconView;
                if (v == null) continue;
                Point p = e.GetPosition(v);
                if (p.X < v.ActualWidth / 2) return i;
                i++;
            }
            return i;
        }

        /// <summary>Called by the bar while a tray icon is dragged over it: show where it would land.</summary>
        public void DragHint(DragEventArgs e)
        {
            ClearHint();
            if (OverArrow(e)) return;
            int idx = DropIndex(e), n = 0, pos = 0;
            foreach (UIElement c in _pinned.Children) { if (n == idx) break; pos++; if (c is TrayIconView) n++; }
            _mark = new Border { Width = 2, Height = 16, Background = _bar.Theme.MainAccent, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-1, 0, -1, 0), IsHitTestVisible = false };
            _pinned.Children.Insert(Math.Min(pos, _pinned.Children.Count), _mark);
        }

        public void ClearHint()
        {
            if (_mark != null) { _pinned.Children.Remove(_mark); _mark = null; }
        }

        /// <summary>The icons on the bar and behind the arrow last time (their order and which is where).</summary>
        string _layoutSig = "";

        public void Rebuild()
        {
            var host = _mgr.Tray;
            if (host == null) { _pinned.Children.Clear(); _overflow.Clear(); _layoutSig = ""; return; }

            var icons = new List<TrayIcon>(host.Icons);
            string netBefore = _netSig;
            _netSig = "";
            if (_cfg.TrayNetwork && !WindowsNetworkIcon(host))
            {
                _netSig = NetState();
                _netIcon.Tip = _netTip;
                if (_netSig != netBefore || _netImage == null)
                    _netImage = NetImage(_netKind == "wired" ? IconUtil.NetworkWiredGlyph : IconUtil.NetworkWifiGlyph, _bar.Theme.Fg, _netLevel);
                if (_netImage != null) icons.Add(_netIcon);
            }

            // which icons go where; the same as last time = only pictures / tooltips changed: update them in place
            var pinnedSet = new HashSet<string>(_cfg.PinnedKeys);
            var onBar = new List<TrayIcon>();
            var behind = new List<TrayIcon>();
            TrayLook look = Look();
            var sig = new System.Text.StringBuilder((_cfg.GroupIcons ? "g|" : "a|") + look.Mode + "|" + (look.Brush != null ? look.Brush.Color.ToString() : "") + "|" + look.Strength + "|");
            foreach (var ic in Ordered(icons))
            {
                if (ic.Hidden) continue;
                bool pin = !_cfg.GroupIcons || pinnedSet.Contains(ic.Key);
                (pin ? onBar : behind).Add(ic);
                sig.Append(pin ? "+" : "-").Append(ic.Key).Append(ic.HIcon == IntPtr.Zero && ic != _netIcon ? "(no picture yet)" : "").Append(';');
            }
            if (sig.ToString() == _layoutSig && _mark == null)
            {
                bool netChanged = _netSig != netBefore;
                foreach (UIElement c in _pinned.Children)
                {
                    var v = c as TrayIconView;
                    if (v != null) { TrayIcon ic = v.Icon; v.Refresh(delegate { return IconOf(ic); }, ic == _netIcon && netChanged); }
                }
                if (_popup != null && _popup.IsOpen && !_popup.Pinned && _popupPanel != null)
                    foreach (UIElement c in _popupPanel.Children)
                    {
                        var v = c as TrayIconView;
                        if (v != null) { TrayIcon ic = v.Icon; v.Refresh(delegate { return IconOf(ic); }, ic == _netIcon && netChanged); }
                    }
                return;
            }
            _layoutSig = sig.ToString();

            _pinned.Children.Clear();
            _overflow.Clear();
            _overflow.AddRange(behind);
            foreach (var ic in onBar)
            {
                ImageSource src = IconOf(ic);
                if (src == null) continue;
                var view = new TrayIconView(ic, src, 16, _bar.Theme, look, host, null, null, null);
                ReorderFx.SetKey(view, ic.Key);
                _pinned.Children.Add(view);
            }

            _arrow.Visibility = _cfg.GroupIcons ? Visibility.Visible : Visibility.Collapsed;
            // refresh an open popup, but never while one of its icons is being dragged
            if (_popup != null && _popup.IsOpen && !_popup.Pinned) _popup.SetContent(BuildPopupContent());
            _bar.ContentChanged();
            ReorderFx.PlaySoon(_cfg.Id, _pinned);
        }

        /// <summary>The icon colours set for this module, in the bar's theme colours.</summary>
        TrayLook Look()
        {
            Theme t = _bar.Theme;
            SolidColorBrush b = _cfg.TrayTintColour == "Main Accent" ? t.MainAccent : _cfg.TrayTintColour == "Secondary Accent" ? t.SecondaryAccent : t.Fg;
            return new TrayLook { Mode = _cfg.TrayIconColour, Brush = b, Strength = _cfg.TrayTint / 100.0 };
        }

        /// <summary>An icon's picture: an app's own, or vShell's network icon as it is now.</summary>
        ImageSource IconOf(TrayIcon ic) { return ic == _netIcon ? _netImage : IconUtil.FromHIcon(ic.HIcon); }

        /// <summary>Where an icon is on screen (device px): its button on the bar, in the open popup, or else the arrow it hides behind.</summary>
        public bool TryGetIconRect(TrayIcon ic, out RECT r)
        {
            r = new RECT();
            foreach (UIElement c in _pinned.Children)
            {
                var v = c as TrayIconView;
                if (v != null && v.Icon == ic && ScreenRect(v, out r)) return true;
            }
            if (_popup != null && _popup.IsOpen && _popupPanel != null)
                foreach (UIElement c in _popupPanel.Children)
                {
                    var v = c as TrayIconView;
                    if (v != null && v.Icon == ic && ScreenRect(v, out r)) return true;
                }
            if (_overflow.Contains(ic) && _arrow.Visibility == Visibility.Visible && ScreenRect(_arrow, out r)) return true;
            return false;
        }

        static bool ScreenRect(FrameworkElement e, out RECT r)
        {
            r = new RECT();
            if (e.ActualWidth <= 0 || PresentationSource.FromVisual(e) == null) return false;
            Point a = e.PointToScreen(new Point(0, 0));
            Point b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
            r.Left = (int)Math.Round(a.X); r.Top = (int)Math.Round(a.Y);
            r.Right = (int)Math.Round(b.X); r.Bottom = (int)Math.Round(b.Y);
            return true;
        }

        // ---- popup ------------------------------------------------------------
        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); return; }
            if (PopupWindow.ClosedJustNow(_arrow)) { _popup = null; return; }     // this click's press already closed it
            _popup = new PopupWindow(_mgr, _bar, _arrow, _bar.Theme, BuildPopupContent());
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_bar.Theme, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        FrameworkElement BuildPopupContent()
        {
            var t = _bar.Theme;
            var host = _mgr.Tray;
            var wrap = new WrapPanel { MaxWidth = 6 * 30, Orientation = Orientation.Horizontal };
            _popupPanel = wrap;
            int shown = 0;
            foreach (var ic in _overflow)
            {
                ImageSource src = IconOf(ic);
                if (src == null) continue;
                var v = new TrayIconView(ic, src, 16, t, Look(), host,
                    delegate { if (_popup != null) _popup.CloseSafe(); },
                    delegate { if (_popup != null) _popup.Pinned = true; },
                    delegate { if (_popup != null) { _popup.Pinned = false; _popup.CloseSafe(); } });
                v.Height = 28;
                wrap.Children.Add(v);
                shown++;
            }

            if (shown == 0)
                return new TextBlock { Text = "No hidden icons", Foreground = t.FgDim, Margin = new Thickness(6, 4, 6, 4) };
            return wrap;
        }

        // ---- drag & drop pinning (called by BarWindow for drops anywhere on the bar) ---------------
        public static bool IsTrayDrag(DragEventArgs e) { return e.Data.GetDataPresent(DragFormat); }

        public bool OverArrow(DragEventArgs e)
        {
            if (_arrow.Visibility != Visibility.Visible) return false;
            Point p = e.GetPosition(_arrow);
            return p.X >= 0 && p.X <= _arrow.ActualWidth && p.Y >= 0 && p.Y <= _arrow.ActualHeight;
        }

        public void HandleDrop(DragEventArgs e)
        {
            string key = e.Data.GetData(DragFormat) as string;
            int idx = DropIndex(e);
            ClearHint();
            if (string.IsNullOrEmpty(key)) return;
            ReorderFx.Remember(_cfg.Id, _pinned);

            if (_cfg.GroupIcons && OverArrow(e)) { _cfg.PinnedKeys.Remove(key); _mgr.SettingsChanged(); return; }
            if (_cfg.GroupIcons && !_cfg.PinnedKeys.Contains(key)) _cfg.PinnedKeys.Add(key);

            // the new order: the icons on the bar as they are now, with this one moved to where it was dropped; then every other icon
            var shown = new List<string>();
            foreach (UIElement c in _pinned.Children) { var v = c as TrayIconView; if (v != null) shown.Add(v.Icon.Key); }
            int from = shown.IndexOf(key);
            if (from >= 0) { shown.RemoveAt(from); if (from < idx) idx--; }
            shown.Insert(Math.Max(0, Math.Min(idx, shown.Count)), key);
            var order = new List<string>(shown);
            foreach (string k in _cfg.TrayOrder) if (!order.Contains(k)) order.Add(k);
            if (_mgr.Tray != null) foreach (var ic in _mgr.Tray.Icons) if (ic.Key != null && !order.Contains(ic.Key)) order.Add(ic.Key);
            _cfg.TrayOrder = order;
            _mgr.SettingsChanged();
            Rebuild();
        }
    }
}
