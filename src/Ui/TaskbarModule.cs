using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>A button that keeps its own width (an icon-only taskbar button) instead of sharing the row.</summary>
    interface IFixedWidth { }

    /// <summary>
    /// Lays buttons out in a row; either evenly spread (full-width bars) or at their natural width (auto-width bars).
    /// Icon-only buttons (IFixedWidth) keep their own width; the rest share what is left.
    /// Optionally animated: new buttons grow in, closing ones (BeginClose) shrink away, and buttons slide to a new place when the
    /// order changes. Buttons are told apart by KeyOf (the app / window), since a button is rebuilt whenever what it shows changes.
    /// </summary>
    public sealed class TaskPanel : Panel
    {
        public double MaxButtonWidth = 180;
        public bool FillEvenly = true;
        double[] _widths = new double[0];

        /// <summary>Grow in / shrink away; slide when the order changes; how long either takes.</summary>
        public bool AnimOpen, AnimMove;
        public int Millis = 250;
        public Func<UIElement, string> KeyOf;
        /// <summary>A closing button has finished shrinking: take it out.</summary>
        public event Action<UIElement> CloseDone;

        sealed class Slot
        {
            public double Fx, Fw, Fo, Tx, Tw, To, Cx, Cw, Co;
            public DateTime Start;
            public bool Live;
        }
        readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();
        readonly HashSet<UIElement> _closing = new HashSet<UIElement>();
        List<string> _order = new List<string>();
        bool _seeded, _hooked, _closeStarted;

        /// <summary>True while buttons are moving (the active line follows them instead of starting over).</summary>
        public bool Animating { get { return _hooked; } }

        public bool IsClosing(UIElement e) { return _closing.Contains(e); }

        /// <summary>This button's app / window has gone: it stays in the row, shrinking away, until CloseDone.</summary>
        public void BeginClose(UIElement e)
        {
            if (_closing.Add(e)) { _closeStarted = true; e.IsHitTestVisible = false; InvalidateMeasure(); }
        }

        string Key(UIElement c, int i)
        {
            string k = KeyOf != null ? KeyOf(c) : null;
            return string.IsNullOrEmpty(k) ? "#" + i : k;
        }

        protected override Size MeasureOverride(Size available)
        {
            int n = InternalChildren.Count;
            _widths = new double[n];
            if (n == 0) return new Size(0, 0);

            double maxH = 0, total = 0, fixedTotal = 0;
            int flex = 0;
            for (int i = 0; i < n; i++)
            {
                UIElement c = InternalChildren[i];
                if (_closing.Contains(c))
                {
                    // going away: still drawn, but it takes no room
                    c.Measure(new Size(MaxButtonWidth, available.Height));
                    _widths[i] = 0;
                    continue;
                }
                if (c is IFixedWidth)
                {
                    c.Measure(new Size(double.PositiveInfinity, available.Height));
                    _widths[i] = c.DesiredSize.Width;
                    fixedTotal += _widths[i];
                }
                else
                {
                    c.Measure(new Size(MaxButtonWidth, available.Height));
                    _widths[i] = FillEvenly ? MaxButtonWidth : Math.Min(MaxButtonWidth, c.DesiredSize.Width);
                    flex++;
                }
                total += _widths[i];
                maxH = Math.Max(maxH, c.DesiredSize.Height);
            }

            if (!double.IsInfinity(available.Width) && total > available.Width)
            {
                double room = available.Width - fixedTotal;
                if (flex > 0 && room > flex * 24)
                {
                    double each = room / flex;
                    for (int i = 0; i < n; i++)
                        if (!(InternalChildren[i] is IFixedWidth) && !_closing.Contains(InternalChildren[i])) { _widths[i] = each; InternalChildren[i].Measure(new Size(each, available.Height)); }
                }
                else
                {
                    double k = available.Width / total;          // even the icons don't fit: shrink everything
                    for (int i = 0; i < n; i++) { if (_closing.Contains(InternalChildren[i])) continue; _widths[i] *= k; InternalChildren[i].Measure(new Size(_widths[i], available.Height)); }
                }
                total = available.Width;
            }
            return new Size(total, double.IsInfinity(available.Height) ? maxH : available.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int n = InternalChildren.Count;
            _closing.RemoveWhere(delegate (UIElement e) { return !InternalChildren.Contains(e); });
            if (n == 0) { _slots.Clear(); _order.Clear(); return finalSize; }

            var w = new double[n];
            double fixedTotal = 0;
            int flex = 0;
            for (int i = 0; i < n; i++)
            {
                if (_closing.Contains(InternalChildren[i])) continue;
                if (InternalChildren[i] is IFixedWidth) { w[i] = i < _widths.Length ? _widths[i] : InternalChildren[i].DesiredSize.Width; fixedTotal += w[i]; }
                else flex++;
            }
            if (FillEvenly)
            {
                double each = flex == 0 ? 0 : Math.Max(0, Math.Min(MaxButtonWidth, (finalSize.Width - fixedTotal) / flex));
                for (int i = 0; i < n; i++) if (!(InternalChildren[i] is IFixedWidth) && !_closing.Contains(InternalChildren[i])) w[i] = each;
            }
            else
            {
                for (int i = 0; i < n; i++) if (!(InternalChildren[i] is IFixedWidth) && !_closing.Contains(InternalChildren[i])) w[i] = i < _widths.Length ? _widths[i] : MaxButtonWidth;
            }
            double sum = 0;
            for (int i = 0; i < n; i++) sum += w[i];
            if (sum > finalSize.Width && sum > 0)
                for (int i = 0; i < n; i++) w[i] = w[i] * finalSize.Width / sum;

            // where each button should be
            var keys = new string[n];
            var tx = new double[n];
            var seen = new HashSet<string>();
            double x = 0;
            for (int i = 0; i < n; i++)
            {
                string k = Key(InternalChildren[i], i);
                if (!seen.Add(k)) k = k + "#" + i;
                keys[i] = k;
                tx[i] = x;
                if (!_closing.Contains(InternalChildren[i])) x += w[i];
            }

            // what happened: the order of the buttons that were already there changed? something new / gone?
            bool anim = (AnimOpen || AnimMove) && _seeded;
            var order = new List<string>();
            bool added = false;
            for (int i = 0; i < n; i++)
            {
                if (_closing.Contains(InternalChildren[i])) continue;
                if (_slots.ContainsKey(keys[i])) order.Add(keys[i]); else added = true;
            }
            var before = new List<string>();
            var now = new HashSet<string>(order);
            foreach (string k in _order) if (now.Contains(k)) before.Add(k);
            bool reordered = before.Count == order.Count && !SameList(before, order);
            bool trigger = anim && ((AnimMove && reordered) || (AnimOpen && (added || _closeStarted)));
            _closeStarted = false;

            DateTime t = DateTime.UtcNow;
            bool any = false;
            var finished = new List<UIElement>();
            var live = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                UIElement c = InternalChildren[i];
                string k = keys[i];
                live.Add(k);
                bool closing = _closing.Contains(c);
                Slot s;
                bool isNew = !_slots.TryGetValue(k, out s);
                if (isNew)
                {
                    s = new Slot();
                    _slots[k] = s;
                    if (closing) { finished.Add(c); continue; }      // nothing to shrink from
                    bool grow = anim && AnimOpen;
                    s.Cx = grow ? tx[i] + w[i] / 2 : tx[i]; s.Cw = grow ? 0 : w[i]; s.Co = grow ? 0 : 1;
                    s.Fx = s.Cx; s.Fw = s.Cw; s.Fo = s.Co;
                    s.Tx = tx[i]; s.Tw = w[i]; s.To = 1;
                    s.Start = t; s.Live = grow;
                }
                else
                {
                    double gx, gw, go;
                    if (closing)
                    {
                        if (s.To == 0 && s.Tw == 0) { gx = s.Tx; gw = 0; go = 0; }      // already on its way out
                        else { gx = s.Cx + s.Cw / 2; gw = 0; go = 0; }
                    }
                    else { gx = tx[i]; gw = w[i]; go = 1; }
                    if (Math.Abs(gx - s.Tx) > 0.5 || Math.Abs(gw - s.Tw) > 0.5 || Math.Abs(go - s.To) > 0.01)
                    {
                        if (trigger || closing || s.Live)
                        {
                            s.Fx = s.Cx; s.Fw = s.Cw; s.Fo = s.Co;
                            s.Tx = gx; s.Tw = gw; s.To = go;
                            s.Start = t; s.Live = true;
                        }
                        else
                        {
                            s.Tx = s.Cx = s.Fx = gx; s.Tw = s.Cw = s.Fw = gw; s.To = s.Co = s.Fo = go;
                            s.Live = false;
                        }
                    }
                }

                if (s.Live)
                {
                    double p = Math.Max(0, Math.Min(1, (t - s.Start).TotalMilliseconds / Math.Max(60, Millis)));
                    double e = 1 - Math.Pow(1 - p, 3);
                    s.Cx = s.Fx + (s.Tx - s.Fx) * e; s.Cw = s.Fw + (s.Tw - s.Fw) * e; s.Co = s.Fo + (s.To - s.Fo) * Math.Min(1, p * 1.3);
                    if (p >= 1) { s.Cx = s.Tx; s.Cw = s.Tw; s.Co = s.To; s.Live = false; }
                    else any = true;
                }
                c.Arrange(new Rect(s.Cx, 0, Math.Max(0, s.Cw), finalSize.Height));
                if (AnimOpen || c.Opacity < 1) c.Opacity = Math.Max(0, Math.Min(1, s.Co));
                if (closing && !s.Live) finished.Add(c);
            }

            var dead = new List<string>();
            foreach (string k in _slots.Keys) if (!live.Contains(k)) dead.Add(k);
            foreach (string k in dead) _slots.Remove(k);
            _order = new List<string>();
            for (int i = 0; i < n; i++) if (!_closing.Contains(InternalChildren[i]) && _slots.ContainsKey(keys[i])) _order.Add(keys[i]);
            _seeded = true;

            if (any && !_hooked) { _hooked = true; CompositionTarget.Rendering += OnFrame; }
            else if (!any && _hooked) { _hooked = false; CompositionTarget.Rendering -= OnFrame; }
            if (finished.Count > 0)
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    foreach (UIElement e in finished)
                    {
                        _closing.Remove(e);
                        var h = CloseDone;
                        if (h != null) h(e);
                    }
                }), DispatcherPriority.Background);
            return finalSize;
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        void OnFrame(object sender, EventArgs e) { InvalidateArrange(); }

        /// <summary>Stop animating (the taskbar went away).</summary>
        public void StopAnimations()
        {
            if (_hooked) { _hooked = false; CompositionTarget.Rendering -= OnFrame; }
        }
    }

    /// <summary>One app on the taskbar: a pin and / or its open windows.</summary>
    public sealed class TaskGroup
    {
        public string Key = "";
        public PinnedApp Pin;
        public readonly List<TaskWindow> Windows = new List<TaskWindow>();
        public bool Running { get { return Windows.Count > 0; } }
        public bool AnyActive { get { foreach (var w in Windows) if (w.Active) return true; return false; } }
        public bool AnyFlashing { get { foreach (var w in Windows) if (w.Flashing) return true; return false; } }
    }

    /// <summary>
    /// A taskbar button: one window (uncombined), or an app with all its windows (combined), or a pinned app that isn't running.
    /// </summary>
    class TaskButton : Border
    {
        public readonly TaskGroup Group;
        public readonly bool Combined;
        /// <summary>No icon could be had for it (tried again a little later).</summary>
        public bool IconMissing;
        /// <summary>The active app's / window's button (the moving line / highlight go to it).</summary>
        public readonly bool IsActive;
        /// <summary>Icons only: the active line is this wide and centred; NaN = as wide as the button (minus 2 px each side).</summary>
        public readonly double ActiveLineWidth = double.NaN;
        /// <summary>Its app / window has gone; it is shrinking away (opening and closing animation).</summary>
        public bool Closing;

        public TaskButton(TaskbarModule owner, TaskGroup g, bool combined, Theme t, ModuleConfig cfg, double hostHeight)
        {
            Group = g; Combined = combined;
            ReorderFx.SetKey(this, g.Key);
            TaskWindow first = g.Windows.Count > 0 ? g.Windows[0] : null;
            bool active = g.AnyActive, flashing = g.AnyFlashing, running = g.Running;
            IsActive = active && running;
            bool slideLine = owner.Slide != null, slideBg = slideLine && owner.Slide.Highlight;
            string label = LabelFor(g, combined, cfg);
            bool iconOnly = label == null;

            Margin = new Thickness(2, 5, 2, 5);
            Padding = iconOnly ? new Thickness(0) : new Thickness(8, 0, 8, 0);
            CornerRadius = new CornerRadius(Math.Max(3, t.Corner.TopLeft / 2));
            Brush restBg = active ? (slideBg ? Brushes.Transparent : (Brush)t.ActiveBg)
                         : flashing ? AttentionBrush(t, cfg)      // the app wants attention: Secondary Accent, so it differs from the active button
                         : Brushes.Transparent;
            Background = restBg;
            var pulse = restBg as SolidColorBrush;
            if (flashing && !active && pulse != null && !pulse.IsFrozen) Pulse(pulse);
            Cursor = Cursors.Hand;
            if (!cfg.TaskPreviews || !running)
                ToolTip = running ? (first.Title + (string.IsNullOrEmpty(first.OverlayText) ? "" : "\n" + first.OverlayText)) : TaskbarModule.AppName(g);

            // icon, with the app's overlay badge (unread count etc.) and, combined, how many windows it has
            double iconPx = !iconOnly ? 16
                          : cfg.TaskIconSize > 0 ? Math.Max(10, Math.Min(cfg.TaskIconSize, hostHeight - 12))     // your size, shrunk to fit a slim bar
                          : Math.Max(16, Math.Min(Math.Min(36, hostHeight - 12), Math.Round(hostHeight * 0.6)));     // fits the bar: 24 on a 40 high bar
            ImageSource icon = combined || first == null ? TaskbarModule.GroupIcon(g) : TaskbarModule.WindowIcon(first);
            IconMissing = icon == null;
            var iconHost = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = iconOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left, Width = iconPx, Height = iconPx };
            var img = new Image { Width = iconPx, Height = iconPx, Source = icon, Opacity = running || g.Pin == null ? 1 : 0.85 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            iconHost.Children.Add(img);
            TaskWindow withOverlay = null;
            foreach (var w in g.Windows) if (w.OverlayIcon != IntPtr.Zero) { withOverlay = w; break; }
            ImageSource badge = withOverlay != null ? IconUtil.FromHIcon(withOverlay.OverlayIcon) : null;
            if (badge != null)
            {
                var b = new Image
                {
                    Source = badge, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, -3, -3)
                };
                RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.HighQuality);
                iconHost.Children.Add(b);
            }
            else if (combined && g.Windows.Count > 1)
            {
                var count = new Border
                {
                    Background = t.MainAccent, CornerRadius = new CornerRadius(5), MinWidth = 11, Height = 11, Padding = new Thickness(2, 0, 2, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -5, -4),
                    Child = new TextBlock
                    {
                        Text = g.Windows.Count > 9 ? "9+" : g.Windows.Count.ToString(), FontSize = 8.5, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(TaskbarModule.Contrast(t.MainAccent.Color)), HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0)
                    }
                };
                iconHost.Children.Add(count);
            }

            var outer = new Grid();
            if (iconOnly)
            {
                Width = Math.Max(Math.Max(30, iconPx + 12), Math.Round(hostHeight * 0.95));
                outer.Children.Add(iconHost);
            }
            else
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (icon != null || badge != null) iconHost.Margin = new Thickness(0, 0, combined && g.Windows.Count > 1 ? 9 : 6, 0);
                Grid.SetColumn(iconHost, 0);
                var tb = new TextBlock
                {
                    Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = !running || (!combined && first.Minimized) ? (Brush)t.FgDim : t.Fg
                };
                Grid.SetColumn(tb, 1);
                row.Children.Add(iconHost);
                row.Children.Add(tb);
                outer.Children.Add(row);
            }

            // the line along the bottom: long in the Main Accent for the active app, short for a running one, none for a pin
            // (with the moving line on, the taskbar draws the active one itself)
            if (iconOnly) ActiveLineWidth = Math.Max(12, iconPx * 0.75);
            if (running && !(active && slideLine))
            {
                var line = new Border
                {
                    Height = 2.5, CornerRadius = new CornerRadius(1.25), VerticalAlignment = VerticalAlignment.Bottom,
                    Background = active ? (Brush)t.MainAccent : flashing ? (Brush)t.SecondaryAccent : (Brush)Theme.Faded(t.FgDim, 80),
                    Margin = new Thickness(0, 0, 0, -1)
                };
                if (iconOnly || !active) { line.Width = active ? Math.Max(12, iconPx * 0.75) : 6; line.HorizontalAlignment = HorizontalAlignment.Center; }
                else line.Margin = new Thickness(-6, 0, -6, -1);
                outer.Children.Add(line);
            }

            // ITaskbarList progress (downloads, copies...): a thin bar along the bottom of the button
            TaskWindow withProgress = null;
            foreach (var w in g.Windows) if (w.ProgressState != 0) { withProgress = w; break; }
            FrameworkElement prog = withProgress != null ? MakeProgress(withProgress, t, iconOnly) : null;
            if (prog != null) outer.Children.Add(prog);
            Child = outer;

            // dock animations (Dock bar, icons only): the icon lifts / glows / grows; starting a pinned app bounces it
            DockFx fx = iconOnly && DockFx.IconsOnly(owner.Config) ? owner.Fx : null;
            if (fx != null) fx.Add(this, iconHost, owner.Config, t.MainAccent.Color);
            MouseEnter += delegate { if (!active && owner.Config.HoverBox) Background = t.Hover; owner.HoverEnter(this); };
            MouseLeave += delegate { Background = restBg; owner.HoverLeave(this); };
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            // a pin can be dragged along the bar to move it
            IconDrag.MakeDraggable(this, delegate { return owner.Config.Id + "|" + owner.Pins.IndexOf(Group.Pin); },
                                   delegate { return Group.Pin != null; }, delegate { owner.DragStarted(); });
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (IconDrag.JustDragged) return;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (fx != null && (!Group.Running || shift)) fx.Bounce(this);     // starting the app (or a new window of it)
                owner.Click(this, shift);
            };
            MouseDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                owner.MiddleClick(this);
            };
            MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; owner.ShowMenu(this); };
        }

        /// <summary>An app asking for attention: Secondary Accent, steady or slowly pulsing.</summary>
        static Brush AttentionBrush(Theme t, ModuleConfig cfg)
        {
            if (cfg.TaskAttention != "Pulse") return Theme.Faded(t.SecondaryAccent, 45);
            return new SolidColorBrush(t.SecondaryAccent.Color) { Opacity = 0.45 };      // TaskButton starts / stops the pulse with the button
        }

        /// <summary>The attention pulse runs only while the button is on screen (a forever animation would otherwise keep going).</summary>
        void Pulse(SolidColorBrush b)
        {
            Loaded += delegate
            {
                b.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0.12, 0.6, TimeSpan.FromMilliseconds(900))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            };
            Unloaded += delegate { b.BeginAnimation(Brush.OpacityProperty, null); };
        }

        /// <summary>The text on the button (null = icon only).</summary>
        public static string LabelFor(TaskGroup g, bool combined, ModuleConfig cfg)
        {
            TaskWindow first = g.Windows.Count > 0 ? g.Windows[0] : null;
            return !combined ? (first != null ? first.Title : TaskbarModule.AppName(g))
                 : cfg.TaskLabel == "Name" ? TaskbarModule.AppName(g)
                 : cfg.TaskLabel == "Title" ? (g.Running ? TaskbarModule.LatestTitle(g) : TaskbarModule.AppName(g))
                 : null;
        }

        /// <summary>TBPF_INDETERMINATE 1, NORMAL 2, ERROR 4, PAUSED 8.</summary>
        static FrameworkElement MakeProgress(TaskWindow w, Theme t, bool iconOnly)
        {
            int st = w.ProgressState;
            if (st == 0) return null;
            Brush c = (st & 4) != 0 ? new SolidColorBrush(Color.FromRgb(0xE8, 0x48, 0x55))
                    : (st & 8) != 0 ? new SolidColorBrush(Color.FromRgb(0xE8, 0xB3, 0x39))
                    : (Brush)t.SecondaryAccent;     // normal progress in Secondary Accent, apart from the Main Accent active-window underline
            double frac = (st & 1) != 0 && (st & 2) == 0 ? 1.0 : Math.Max(0, Math.Min(1, w.ProgressValue / 65534.0));
            var bar = new Grid { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = iconOnly ? new Thickness(3, 0, 3, 2) : new Thickness(-6, 0, -6, 2) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star) });
            var fill = new Border { Background = c, CornerRadius = new CornerRadius(1.5), Opacity = (st & 1) != 0 && (st & 2) == 0 ? 0.55 : 1 };
            Grid.SetColumn(fill, 0);
            bar.Children.Add(fill);
            return bar;
        }
    }

    sealed class FixedTaskButton : TaskButton, IFixedWidth
    {
        public FixedTaskButton(TaskbarModule owner, TaskGroup g, bool combined, Theme t, ModuleConfig cfg, double hostHeight)
            : base(owner, g, combined, t, cfg, hostHeight) { }
    }

    /// <summary>
    /// The taskbar: open windows, combined per app ("Always", "WhenFull" or "Never"), with pinned apps first.
    /// Hover a running button for live previews; click a combined button with several windows for the list of them.
    /// </summary>
    public sealed class TaskbarModule : Border
    {
        readonly BarManager _mgr;
        readonly BarWindow _bar;
        readonly ModuleConfig _cfg;
        readonly TaskPanel _panel = new TaskPanel();
        readonly DispatcherTimer _hover;
        TaskButton _hoverButton;
        TaskPreviewWindow _preview;
        bool _combinedNow = true;

        /// <summary>This taskbar's settings, and its bar's dock animations (null when there are none).</summary>
        public ModuleConfig Config { get { return _cfg; } }
        /// <summary>The pins this taskbar shows: the shared ones ("Same pins on every taskbar") or its own.</summary>
        public List<PinnedApp> Pins { get { return _cfg.TaskSyncPins ? _mgr.Settings.SharedPins : _cfg.Pins; } }
        public DockFx Fx { get { return _bar.Fx; } }
        /// <summary>The moving active line / highlight (null = each button draws its own, as before).</summary>
        public TaskSlide Slide { get { return _slide; } }
        readonly TaskSlide _slide;
        readonly Grid _root = new Grid();

        public TaskbarModule(BarManager mgr, BarWindow bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _panel.AnimOpen = true;      // buttons always grow in / shrink away and slide to their new place (0.38.105: no longer settings - the launcher and tray slide always too)
            _panel.AnimMove = true;
            _panel.Millis = 250;
            _panel.KeyOf = delegate (UIElement e) { var b = e as TaskButton; return b != null ? b.Group.Key : null; };
            _panel.CloseDone += delegate (UIElement e)
            {
                if (!_panel.Children.Contains(e)) return;
                _panel.Children.Remove(e);
                _bar.ContentChanged();
            };
            Unloaded += delegate { _panel.StopAnimations(); };
            if (cfg.TaskAnim != "Off" || cfg.TaskAnimHighlight)
            {
                _slide = new TaskSlide { Mode = cfg.TaskAnim, Millis = cfg.TaskAnimMs, Bounce = cfg.TaskAnimBounce, Highlight = cfg.TaskAnimHighlight };
                _root.Children.Add(_slide.Back);      // the highlight, behind the buttons
                _root.Children.Add(_panel);
                _root.Children.Add(_slide.Front);     // the line, over them
                _root.LayoutUpdated += delegate { PlaceSlide(); };
                Unloaded += delegate { _slide.Stop(); };
                Child = _root;
            }
            else Child = _panel;
            VerticalAlignment = VerticalAlignment.Stretch;
            _hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _hover.Tick += delegate
            {
                _hover.Stop();
                if (_hoverButton != null && _hoverButton.IsMouseOver && _hoverButton.Group.Running && _cfg.TaskPreviews)
                    OpenPreview(_hoverButton, false);
            };
            // "combine when full": look again when the room changes
            SizeChanged += delegate
            {
                if (_cfg.TaskGrouping != "WhenFull") return;
                if (ShouldCombine() != _combinedNow) Rebuild();
            };
            Unloaded += delegate { ClosePreview(); _hover.Stop(); };

            // drag a pin along the bar to move it; drop programs / shortcuts from File Explorer to pin them where they land
            AllowDrop = true;
            DragEventHandler over = delegate (object s, DragEventArgs e)
            {
                string tok = IconDrag.TokenOf(e);
                bool mine = tok != null && tok.StartsWith(_cfg.Id + "|");
                bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
                if (!mine && !files) return;      // tray icons etc.: the bar's own handling
                e.Effects = mine ? DragDropEffects.Move : DragDropEffects.Copy;
                e.Handled = true;
                IconDrag.ShowMark(_panel, IconDrag.IndexAt(_panel, e, IsPinButton), IsPinButton, _bar.Theme.MainAccent);
            };
            DragEnter += over;
            DragOver += over;
            DragLeave += delegate { IconDrag.HideMark(_panel); };
            Drop += OnDrop;
        }

        static bool IsPinButton(UIElement c) { var b = c as TaskButton; return b != null && b.Group.Pin != null && !b.Closing; }

        internal void DragStarted()
        {
            _hover.Stop();
            ClosePreview();
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            IconDrag.HideMark(_panel);
            int at = IconDrag.IndexAt(_panel, e, IsPinButton);
            List<PinnedApp> pins = Pins;
            string tok = IconDrag.TokenOf(e);
            if (tok != null && tok.StartsWith(_cfg.Id + "|"))
            {
                e.Handled = true;
                int from;
                if (!int.TryParse(tok.Substring(tok.IndexOf('|') + 1), out from) || from < 0 || from >= pins.Count) return;
                if (at > from) at--;
                at = Math.Max(0, Math.Min(pins.Count - 1, at));
                if (at == from) return;
                ReorderFx.Remember(_cfg.Id, _panel);
                PinnedApp p = pins[from];
                pins.RemoveAt(from);
                pins.Insert(at, p);
                _mgr.SettingsChanged();
                return;
            }
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            e.Handled = true;
            at = Math.Max(0, Math.Min(pins.Count, at));
            ReorderFx.Remember(_cfg.Id, _panel);
            foreach (string f in files)
            {
                PinnedApp p = Shortcuts.PinFor(f);
                if (pins.Exists(delegate (PinnedApp x) { return Shortcuts.SameApp(x, p); })) continue;
                pins.Insert(at++, p);
            }
            _mgr.SettingsChanged();
        }

        // =================================================================
        // Building
        // =================================================================
        // buttons by what they show: a button whose app, windows and state haven't changed is kept as it is
        Dictionary<string, TaskButton> _made = new Dictionary<string, TaskButton>();
        Theme _theme;

        /// <summary>Everything a button shows, as one string: same string = the old button can stay.</summary>
        string Sig(TaskGroup g, bool combined, bool fixedW, ModuleConfig cfg)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(fixedW ? 'F' : 'N').Append(combined ? 'C' : 'S').Append(cfg.TaskLabel).Append(cfg.TaskIconSize).Append('|').Append(g.Key).Append('|');
            if (g.Pin != null) sb.Append(g.Pin.Name).Append('>').Append(g.Pin.Path);
            sb.Append('|').Append(TaskButton.LabelFor(g, combined, cfg)).Append('|');
            if (!cfg.TaskPreviews && g.Windows.Count > 0) sb.Append(g.Windows[0].Title).Append('|');      // the tooltip
            foreach (TaskWindow w in g.Windows)
                sb.Append(w.Handle.ToInt64()).Append(w.Active ? 'A' : '-').Append(w.Minimized ? 'M' : '-').Append(w.Flashing ? 'F' : '-')
                  .Append(w.ProgressState).Append(',').Append(w.ProgressValue / 655).Append(',').Append(w.OverlayIcon.ToInt64()).Append(w.OverlayText)
                  .Append(',').Append(w.HIcon.ToInt64()).Append(',').Append(w.BigIcon.ToInt64()).Append(',').Append(w.LiveIconVersion)
                  .Append(',').Append(w.Aumid).Append(';');
            return sb.ToString();
        }

        /// <summary>Send the moving line / highlight to the active button (or away when there is none). Runs after every layout pass.</summary>
        void PlaceSlide()
        {
            if (_slide == null || _theme == null) return;
            TaskButton act = null;
            foreach (UIElement e in _panel.Children) { var b = e as TaskButton; if (b != null && b.IsActive && !b.Closing && b.IsVisible) { act = b; break; } }
            if (act == null) { _slideKey = null; _slide.MoveTo(false, 0, 0, 0, Rect.Empty); return; }
            // the same app's button, just moved (buttons sliding about): follow it instead of starting the move again
            bool follow = _panel.Animating && act.Group.Key == _slideKey;
            _slideKey = act.Group.Key;
            Rect r;
            try { r = act.TransformToAncestor(_root).TransformBounds(new Rect(0, 0, act.ActualWidth, act.ActualHeight)); }
            catch { return; }
            double lx, lr;
            if (double.IsNaN(act.ActiveLineWidth)) { lx = r.Left + 2; lr = r.Right - 2; }
            else { double mid = (r.Left + r.Right) / 2; lx = mid - act.ActiveLineWidth / 2; lr = mid + act.ActiveLineWidth / 2; }
            _slide.MoveTo(true, lx, lr, r.Bottom - 1.5, r, follow);
        }
        string _slideKey;

        public void Rebuild()
        {
            var t = _theme ?? (_theme = _bar.Theme.ForModule(_cfg));
            if (_slide != null) _slide.SetLook(t.MainAccent, t.ActiveBg, Math.Max(3, t.Corner.TopLeft / 2));
            _panel.MaxButtonWidth = _cfg.MaxButtonWidth;
            _panel.FillEvenly = _bar.Placement.FullWidth;
            var made = new Dictionary<string, TaskButton>();
            var want = new List<UIElement>();
            Func<string, Func<TaskButton>, TaskButton> keep = delegate (string sig, Func<TaskButton> make)
            {
                TaskButton b;
                if (made.ContainsKey(sig) || !_made.TryGetValue(sig, out b)) b = make();
                made[sig] = b;
                want.Add(b);
                return b;
            };

            var windows = new List<TaskWindow>();
            foreach (var w in _mgr.Tasks.Windows)
                if (!_cfg.OnlyCurrentMonitor || w.Monitor == _bar.MonitorHandle) windows.Add(w);
            // pins of packaged apps (WhatsApp ...) made inside WindowsApps: turn them into Store app pins (from the Start menu's list)
            bool repaired = false;
            foreach (PinnedApp p in Pins) if (Shortcuts.Repair(p)) repaired = true;
            List<TaskGroup> groups = BuildGroups(windows, _cfg.TaskShowPins ? Pins : null);
            // ... or from the running app's own id
            foreach (TaskGroup g in groups)
                if (g.Pin != null)
                    foreach (TaskWindow w in g.Windows)
                        if (!string.IsNullOrEmpty(w.WindowAumid) && Shortcuts.Repair(g.Pin, w.WindowAumid)) { repaired = true; break; }
            if (repaired) Dispatcher.BeginInvoke(new Action(delegate { _mgr.SettingsChanged(); }));

            _combinedNow = _cfg.TaskGrouping == "Always" || (_cfg.TaskGrouping == "WhenFull" && ShouldCombine(windows.Count));
            bool iconOnly = _cfg.TaskLabel == "Icon";
            foreach (TaskGroup g in groups)
            {
                TaskGroup gg = g;
                if (!g.Running)
                {
                    ModuleConfig io = IconOnly();      // a pin that isn't running: just its icon
                    keep(Sig(g, true, true, io), delegate { return new FixedTaskButton(this, gg, true, t, io, _bar.HostHeight); });
                }
                else if (_combinedNow)
                    keep(Sig(g, true, iconOnly, _cfg), delegate
                    {
                        return iconOnly ? new FixedTaskButton(this, gg, true, t, _cfg, _bar.HostHeight) : new TaskButton(this, gg, true, t, _cfg, _bar.HostHeight);
                    });
                else
                {
                    // one button per window; they stay together, in the app's place
                    foreach (TaskWindow w in g.Windows)
                    {
                        var one = new TaskGroup { Key = g.Key + "|" + w.Handle.ToInt64(), Pin = g.Pin };
                        one.Windows.Add(w);
                        keep(Sig(one, false, false, _cfg), delegate { return new TaskButton(this, one, false, t, _cfg, _bar.HostHeight); });
                    }
                }
            }
            _made = made;

            // opening and closing: a button whose app / window has gone stays where it was and shrinks away
            if (_panel.AnimOpen && _panel.IsLoaded)
            {
                var wantKeys = new HashSet<string>();
                var wantSet = new HashSet<UIElement>(want);
                foreach (UIElement e in want) { var b = e as TaskButton; if (b != null) wantKeys.Add(b.Group.Key); }
                for (int i = 0; i < _panel.Children.Count; i++)
                {
                    var old = _panel.Children[i] as TaskButton;
                    if (old == null || wantSet.Contains(old) || wantKeys.Contains(old.Group.Key)) continue;
                    if (!old.Closing) { old.Closing = true; _panel.BeginClose(old); }
                    want.Insert(Math.Min(i, want.Count), old);
                }
            }

            // only touch the panel (and the bar's layout) when something actually differs
            bool same = want.Count == _panel.Children.Count;
            for (int i = 0; same && i < want.Count; i++) if (!ReferenceEquals(want[i], _panel.Children[i])) same = false;
            if (!same)
            {
                _panel.Children.Clear();
                foreach (UIElement e in want) _panel.Children.Add(e);
                _bar.ContentChanged();
            }
            if (_preview != null) RefreshPreview();
            ReorderFx.PlaySoon(_cfg.Id, _panel);

            // an icon Windows couldn't give: ask again in a little while (the icon cache tries again after IconUtil.RetrySeconds)
            bool missing = false;
            foreach (UIElement e in want) { var b = e as TaskButton; if (b != null && b.IconMissing) missing = true; }
            if (missing && _iconRetry == null)
            {
                // when the next one may be asked again (failing icons wait longer and longer)
                _iconRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Min(1800, IconUtil.NextRetrySeconds()) + 1) };
                _iconRetry.Tick += delegate
                {
                    _iconRetry.Stop();
                    _iconRetry = null;
                    var keepMade = new Dictionary<string, TaskButton>();
                    foreach (var kv in _made) if (!kv.Value.IconMissing) keepMade[kv.Key] = kv.Value;
                    _made = keepMade;
                    Rebuild();
                };
                _iconRetry.Start();
            }
        }

        DispatcherTimer _iconRetry;
        ModuleConfig _iconOnly;
        /// <summary>A copy of the settings with TaskLabel = Icon (pins that aren't running are always icon-only).</summary>
        ModuleConfig IconOnly()
        {
            if (_iconOnly == null) _iconOnly = new ModuleConfig();
            _iconOnly.TaskLabel = "Icon";
            _iconOnly.TaskPreviews = _cfg.TaskPreviews;
            _iconOnly.TaskIconSize = _cfg.TaskIconSize;
            return _iconOnly;
        }

        bool ShouldCombine() { int n = 0; foreach (var w in _mgr.Tasks.Windows) if (!_cfg.OnlyCurrentMonitor || w.Monitor == _bar.MonitorHandle) n++; return ShouldCombine(n); }

        /// <summary>Combine when the separate buttons would get narrower than about half the button width.</summary>
        bool ShouldCombine(int windowCount)
        {
            double room = ActualWidth;
            if (room < 50 || !_bar.Placement.FullWidth)
            {
                MonitorInfo m = MonitorHelper.FromHandle(_bar.MonitorHandle);
                room = m != null ? m.Bounds.Width / Math.Max(1.0, VisualTreeHelper.GetDpi(this).DpiScaleX) * 0.5 : 600;
            }
            double min = Math.Max(70, _cfg.MaxButtonWidth * 0.55);
            int pins = _cfg.TaskShowPins ? Pins.Count : 0;
            double needed = windowCount * min + pins * _bar.HostHeight;
            // a little slack either way so it doesn't flip back and forth at the edge
            return _combinedNow ? needed > room * 0.9 : needed > room;
        }

        /// <summary>Windows grouped per app; pinned apps first (in pin order), then the others in the order they opened.</summary>
        public static List<TaskGroup> BuildGroups(List<TaskWindow> windows, List<PinnedApp> pins)
        {
            var result = new List<TaskGroup>();
            var byKey = new Dictionary<string, TaskGroup>();
            if (pins != null)
                foreach (PinnedApp p in pins)
                {
                    var g = new TaskGroup { Key = "pin:" + (p.AppId.Length > 0 ? p.AppId : p.Path.ToLowerInvariant()), Pin = p };
                    if (byKey.ContainsKey(g.Key)) continue;
                    byKey[g.Key] = g;
                    result.Add(g);
                }
            foreach (TaskWindow w in windows)
            {
                TaskGroup g = null;
                if (pins != null)
                    foreach (TaskGroup pg in result)
                        if (pg.Pin != null && PinMatches(pg.Pin, w)) { g = pg; break; }
                if (g == null)
                {
                    string key = string.IsNullOrEmpty(w.AppKey) ? "hwnd:" + w.Handle.ToInt64() : w.AppKey;
                    if (!byKey.TryGetValue(key, out g))
                    {
                        g = new TaskGroup { Key = key };
                        byKey[key] = g;
                        result.Add(g);
                    }
                }
                g.Windows.Add(w);
            }
            return result;
        }

        static bool PinMatches(PinnedApp p, TaskWindow w)
        {
            if (p.AppId.Length > 0 && string.Equals(p.AppId, w.AppKey, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(w.ProcessPath) && string.Equals(p.Path, w.ProcessPath, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(w.Aumid) && string.Equals(p.Path, "shell:AppsFolder\\" + w.Aumid, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(w.WindowAumid) && string.Equals(p.Path, "shell:AppsFolder\\" + w.WindowAumid, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // =================================================================
        // Names and icons
        // =================================================================
        static readonly Dictionary<string, string> NameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string AppName(TaskGroup g)
        {
            if (g.Pin != null && g.Pin.Name.Length > 0) return g.Pin.Name;
            TaskWindow w = g.Windows.Count > 0 ? g.Windows[0] : null;
            string path = g.Pin != null ? g.Pin.Path : w != null ? w.ProcessPath : null;
            if (w != null && w.IsUwp) return w.Title;
            string n = ProgramName(path);
            if (n.Length > 0) return n;
            return w != null ? w.Title : "App";
        }

        /// <summary>The program's description ("Google Chrome"), else its file name.</summary>
        public static string ProgramName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string cached;
            if (NameCache.TryGetValue(path, out cached)) return cached;
            string name = "";
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) name = "";
                else if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(path);
                    name = (fv.FileDescription ?? "").Trim();
                    if (name.Length == 0) name = (fv.ProductName ?? "").Trim();
                }
                if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
            }
            catch { name = ""; }
            NameCache[path] = name;
            return name;
        }

        public static string LatestTitle(TaskGroup g)
        {
            foreach (var w in g.Windows) if (w.Active) return w.Title;
            return g.Windows.Count > 0 ? g.Windows[g.Windows.Count - 1].Title : AppName(g);
        }

        public static ImageSource GroupIcon(TaskGroup g)
        {
            ImageSource icon = null;
            // an app that draws its icon live (vXplorer in the theme's colours): that, rather than the pin's or the program's fixed icon
            foreach (TaskWindow lw in g.Windows)
                if (lw.LiveIcon && (icon = IconUtil.FromHIcon(lw.BigIcon != IntPtr.Zero ? lw.BigIcon : lw.HIcon)) != null)
                {
                    if (g.Pin != null) LiveIconCache.Remember(g.Pin.Path, lw.LiveIconVersion, icon);   // the pin keeps it once the app closes
                    return icon;
                }
            if (g.Pin != null && g.Windows.Count == 0 && (icon = LiveIconCache.Get(g.Pin.Path)) != null) return icon;
            if (g.Pin != null)
            {
                string p = g.Pin.Path;
                icon = p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? IconUtil.FromParsingName(p, 32) : IconUtil.FromPath(p);
            }
            if (icon == null && g.Pin != null) icon = StartMenuIcon(g.Pin.Path);
            if (icon == null && g.Windows.Count > 0)
            {
                TaskWindow w = g.Windows[0];
                if (!string.IsNullOrEmpty(w.Aumid)) icon = IconUtil.FromParsingName("shell:AppsFolder\\" + w.Aumid, 32);
                if (icon == null && !string.IsNullOrEmpty(w.WindowAumid)) icon = IconUtil.FromParsingName("shell:AppsFolder\\" + w.WindowAumid, 32) ?? StartMenuIcon("shell:AppsFolder\\" + w.WindowAumid);
                if (icon == null && !string.IsNullOrEmpty(w.ProcessPath)) icon = IconUtil.FromPath(w.ProcessPath);
                if (icon == null) icon = WindowIcon(w);
            }
            return icon;
        }

        /// <summary>The icon the Start menu has for a Store app ("shell:AppsFolder\\id"), when Windows won't give one here.</summary>
        static ImageSource StartMenuIcon(string target)
        {
            if (string.IsNullOrEmpty(target) || !target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (AppEntry e in AppCatalog.Get())
                if (e.Icon != null && string.Equals(e.Target, target, StringComparison.OrdinalIgnoreCase)) return e.Icon;
            return null;
        }

        /// <summary>A window's own icon (Store apps: the package's icon).</summary>
        public static ImageSource WindowIcon(TaskWindow w)
        {
            ImageSource icon = null;
            if (w.LiveIcon && w.BigIcon != IntPtr.Zero) icon = IconUtil.FromHIcon(w.BigIcon);   // the sharper one for a live icon
            if (icon == null && !string.IsNullOrEmpty(w.Aumid)) icon = IconUtil.FromParsingName("shell:AppsFolder\\" + w.Aumid, 32);
            if (icon == null) icon = IconUtil.FromHIcon(w.HIcon);
            // a packaged desktop app (WhatsApp ...) without a window icon: the package's icon (its exe inside WindowsApps can't be read)
            if (icon == null && !string.IsNullOrEmpty(w.WindowAumid) && w.WindowAumid.IndexOf('!') > 0)
                icon = IconUtil.FromParsingName("shell:AppsFolder\\" + w.WindowAumid, 32) ?? StartMenuIcon("shell:AppsFolder\\" + w.WindowAumid);
            if (icon == null && !string.IsNullOrEmpty(w.ExePath)) icon = IconUtil.FromExe(w.ExePath);
            if (icon == null && !string.IsNullOrEmpty(w.ProcessPath)) icon = IconUtil.FromExe(w.ProcessPath);
            return icon;
        }

        /// <summary>Black or white, whichever reads better on c.</summary>
        public static Color Contrast(Color c)
        {
            double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            return l > 150 ? Color.FromRgb(0x11, 0x11, 0x1B) : Colors.White;
        }

        // =================================================================
        // Mouse
        // =================================================================
        internal void HoverEnter(TaskButton b)
        {
            _hoverButton = b;
            if (!b.Group.Running || !_cfg.TaskPreviews) { _hover.Stop(); return; }
            // a preview is already up: move straight to this one, like Windows does
            if (_preview != null && _preview.IsOpen && !_preview.Pinned) { OpenPreview(b, false); return; }
            _hover.Stop();
            _hover.Start();
        }

        internal void HoverLeave(TaskButton b)
        {
            if (_hoverButton == b) _hover.Stop();
        }

        internal void Click(TaskButton b, bool shift)
        {
            _hover.Stop();
            TaskGroup g = b.Group;
            if (!g.Running || shift) { ClosePreview(); Launch(g); return; }
            if (g.Windows.Count == 1)
            {
                ClosePreview();
                TaskList.Activate(g.Windows[0]);
                _mgr.Tasks.Refresh();
                return;
            }
            // several windows: the list (with previews) - a second click closes it
            if (_preview != null && _preview.IsOpen && _preview.Key == g.Key && _preview.Pinned) { ClosePreview(); return; }
            OpenPreview(b, true);
        }

        internal void MiddleClick(TaskButton b)
        {
            if (!b.Combined && b.Group.Windows.Count == 1) TaskList.Close(b.Group.Windows[0].Handle);   // a single window's own button: close it (as before)
            else Launch(b.Group);                                                                         // an app: open another window
        }

        // =================================================================
        // Previews / window list
        // =================================================================
        void OpenPreview(TaskButton b, bool byClick)
        {
            if (_preview == null || !_preview.IsOpen)
            {
                _preview = new TaskPreviewWindow(_mgr, _bar.Theme.ForModule(_cfg), _cfg.TaskPreviews, _cfg.TaskPreviewSize);
                TaskPreviewWindow mine = _preview;
                _preview.Closed += delegate { if (_preview == mine) _preview = null; };
            }
            _preview.Pinned = byClick;
            _preview.ShowFor(b.Group.Key, b.Group.Windows, b, _bar.PopupsBelow);
        }

        void ClosePreview()
        {
            if (_preview != null) { try { _preview.Close(); } catch { } _preview = null; }
        }

        /// <summary>The windows changed while a preview is up: point it at the new button for the same app, or close it.</summary>
        void RefreshPreview()
        {
            if (_preview == null || !_preview.IsOpen) return;
            foreach (UIElement c in _panel.Children)
            {
                var b = c as TaskButton;
                if (b != null && !b.Closing && b.Group.Key == _preview.Key && b.Group.Running)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_preview != null && _preview.IsOpen) _preview.ShowFor(b.Group.Key, b.Group.Windows, b, _bar.PopupsBelow);
                    }), DispatcherPriority.Loaded);
                    return;
                }
            }
            ClosePreview();
        }

        // =================================================================
        // Launching and pinning
        // =================================================================
        static string LaunchPath(TaskGroup g, out string args)
        {
            args = "";
            if (g.Pin != null) { args = g.Pin.Args; return g.Pin.Path; }
            if (g.Windows.Count == 0) return null;
            TaskWindow w = g.Windows[0];
            if (w.IsUwp && !string.IsNullOrEmpty(w.Aumid)) return "shell:AppsFolder\\" + w.Aumid;
            if (!string.IsNullOrEmpty(w.ProcessPath) && w.ProcessPath.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) < 0) return w.ProcessPath;
            if (!string.IsNullOrEmpty(w.WindowAumid)) return "shell:AppsFolder\\" + w.WindowAumid;
            return string.IsNullOrEmpty(w.ProcessPath) ? null : w.ProcessPath;
        }

        public static void Launch(TaskGroup g)
        {
            string args;
            string path = LaunchPath(g, out args);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(path)) { UseShellExecute = true, Arguments = args ?? "" };
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                }
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("Taskbar launch " + path + ": " + ex.Message); }
        }

        void Pin(TaskGroup g)
        {
            string args;
            string path = LaunchPath(g, out args);
            if (string.IsNullOrEmpty(path)) return;
            TaskWindow w = g.Windows.Count > 0 ? g.Windows[0] : null;
            var p = new PinnedApp { Name = AppName(g), Path = path, Args = args ?? "", AppId = w != null ? w.AppKey : "" };
            if (p.AppId.StartsWith("hwnd:")) p.AppId = "";
            Pins.Add(p);
            _mgr.SettingsChanged();
        }

        void Unpin(PinnedApp p)
        {
            Pins.Remove(p);
            _mgr.SettingsChanged();
        }

        void MovePin(PinnedApp p, int delta)
        {
            List<PinnedApp> l = Pins;
            int i = l.IndexOf(p), j = i + delta;
            if (i < 0 || j < 0 || j >= l.Count) return;
            l.RemoveAt(i);
            l.Insert(j, p);
            _mgr.SettingsChanged();
        }

        // =================================================================
        // Right-click
        // =================================================================
        internal void ShowMenu(TaskButton b)
        {
            _hover.Stop();
            ClosePreview();
            TaskGroup g = b.Group;
            var m = new ContextMenu();
            m.Items.Add(new MenuItem { Header = AppName(g), IsEnabled = false, FontWeight = FontWeights.SemiBold });
            string args;
            bool canLaunch = !string.IsNullOrEmpty(LaunchPath(g, out args));
            if (canLaunch) AddItem(m, g.Running ? "New window" : "Open", delegate { Launch(g); });
            if (g.Pin != null)
            {
                PinnedApp p = g.Pin;
                AddItem(m, "Unpin from taskbar", delegate { Unpin(p); });
                List<PinnedApp> pins = Pins;
                int i = pins.IndexOf(p);
                if (i > 0) AddItem(m, "Move left", delegate { MovePin(p, -1); });
                if (i >= 0 && i < pins.Count - 1) AddItem(m, "Move right", delegate { MovePin(p, 1); });
            }
            else if (canLaunch) AddItem(m, "Pin to taskbar", delegate { Pin(g); });

            if (g.Windows.Count == 1)
            {
                IntPtr h = g.Windows[0].Handle;
                m.Items.Add(new Separator());
                AddItem(m, "Restore", delegate { TaskList.SysCommand(h, Native.SC_RESTORE); });
                AddItem(m, "Minimize", delegate { TaskList.Minimize(h); });
                AddItem(m, "Maximize", delegate { TaskList.SysCommand(h, Native.SC_MAXIMIZE); });
                m.Items.Add(new Separator());
                AddItem(m, "Close window", delegate { TaskList.Close(h); });
            }
            else if (g.Windows.Count > 1)
            {
                var hs = new List<IntPtr>();
                foreach (var w in g.Windows) hs.Add(w.Handle);
                m.Items.Add(new Separator());
                AddItem(m, "Minimize all windows", delegate { foreach (IntPtr h in hs) if (Native.IsWindowEnabled(h)) TaskList.Minimize(h); });
                AddItem(m, "Close all " + hs.Count + " windows", delegate { foreach (IntPtr h in hs) TaskList.Close(h); });
            }
            m.PlacementTarget = b;
            m.Placement = _bar.PopupsBelow ? System.Windows.Controls.Primitives.PlacementMode.Bottom : System.Windows.Controls.Primitives.PlacementMode.Top;
            m.IsOpen = true;
        }

        static void AddItem(ContextMenu m, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }
    }
}
