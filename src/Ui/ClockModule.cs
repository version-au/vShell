using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Clock: time and/or date in one of several layouts, 12 or 24 hour.</summary>
    public sealed class ClockModule : Border
    {
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        readonly BarManager _mgr;
        readonly Theme _theme;
        PopupWindow _popup;
        readonly TextBlock _time = new TextBlock();
        readonly TextBlock _date = new TextBlock();
        readonly DispatcherTimer _timer;
        string _last;

        public ClockModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _bar = bar; _cfg = cfg; _mgr = mgr;
            var t = bar.Theme.ForModule(cfg);
            _theme = t;
            double fs = t.FontSize;
            string layout = cfg.ClockLayout;
            // on a bar (Build 2): S / L are one line, M two (time over date)
            if (cfg.ShownTwoLines.HasValue)
            {
                bool two = cfg.ShownTwoLines.Value;
                if (two) { if (layout == "OneLine") layout = "TimeOverDate"; }
                else if (layout == "TimeOverDate") layout = "OneLine";
                else if (layout == "DateOverTime") layout = "DateTimeLine";      // date then time, one line
                else if (layout == "BigTime") layout = "BigTimeLine";            // large time then small date, one line
            }

            _time.Foreground = t.Fg;
            _date.Foreground = t.Fg;
            _time.FontSize = fs;
            _date.FontSize = fs;
            _time.TextAlignment = TextAlignment.Center;
            _date.TextAlignment = TextAlignment.Center;
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            _date.HorizontalAlignment = HorizontalAlignment.Center;

            _time.FontFamily = t.Font;
            _date.FontFamily = t.Font;
            double tr = 1, dr = 1;      // font size of the time / date relative to the base size

            Panel panel;
            switch (layout)
            {
                case "TimeOnly":
                    panel = new StackPanel { Orientation = Orientation.Vertical };
                    panel.Children.Add(_time);
                    break;
                case "DateOnly":
                    panel = new StackPanel { Orientation = Orientation.Vertical };
                    panel.Children.Add(_date);
                    break;
                case "OneLine":
                    panel = new StackPanel { Orientation = Orientation.Horizontal };
                    _date.Margin = new Thickness(10, 0, 0, 0);
                    _time.VerticalAlignment = VerticalAlignment.Center;
                    _date.VerticalAlignment = VerticalAlignment.Center;
                    panel.Children.Add(_time);
                    panel.Children.Add(_date);
                    break;
                case "DateTimeLine":
                    panel = new StackPanel { Orientation = Orientation.Horizontal };
                    _time.Margin = new Thickness(10, 0, 0, 0);
                    _time.VerticalAlignment = VerticalAlignment.Center;
                    _date.VerticalAlignment = VerticalAlignment.Center;
                    panel.Children.Add(_date);
                    panel.Children.Add(_time);
                    break;
                case "BigTimeLine":
                    panel = new StackPanel { Orientation = Orientation.Horizontal };
                    tr = 1.3;
                    dr = 0.85;
                    _time.FontWeight = FontWeights.SemiBold;
                    _date.Foreground = t.FgDim;
                    _date.Margin = new Thickness(8, 0, 0, 0);
                    _time.VerticalAlignment = VerticalAlignment.Center;
                    _date.VerticalAlignment = VerticalAlignment.Center;
                    panel.Children.Add(_time);
                    panel.Children.Add(_date);
                    break;
                case "DateOverTime":
                    panel = new StackPanel { Orientation = Orientation.Vertical };
                    dr = 0.85;
                    _date.Foreground = t.FgDim;
                    panel.Children.Add(_date);
                    panel.Children.Add(_time);
                    break;
                case "BigTime":
                    panel = new StackPanel { Orientation = Orientation.Vertical };
                    tr = 1.45;
                    _time.FontWeight = FontWeights.SemiBold;
                    dr = 0.8;
                    _date.Foreground = t.FgDim;
                    panel.Children.Add(_time);
                    panel.Children.Add(_date);
                    break;
                default:    // TimeOverDate
                    panel = new StackPanel { Orientation = Orientation.Vertical };
                    dr = 0.85;
                    _date.Foreground = t.FgDim;
                    panel.Children.Add(_time);
                    panel.Children.Add(_date);
                    break;
            }

            // Text size. Two-line layouts start at the largest size that fits the bar height (100% = fills the bar);
            // single-line layouts start at the bar's font size. The slider scales that start size.
            double avail = Math.Max(10, bar.HostHeight - 2);
            bool twoLine = layout == "TimeOverDate" || layout == "DateOverTime" || layout == "BigTime";
            double baseSize = fs;
            if (twoLine)
            {
                _time.FontSize = 100 * tr;
                _date.FontSize = 100 * dr;
                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double h100 = panel.DesiredSize.Height;
                if (h100 > 1) baseSize = Math.Max(6, avail * 100.0 / h100 * 0.96);
            }
            double size = baseSize;     // Build 2: S / M / L sets the size (the old Text size % is gone)
            _time.FontSize = size * tr;
            _date.FontSize = size * dr;
            if (!twoLine)
            {
                // single line: never taller than the bar
                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double h = panel.DesiredSize.Height;
                if (h > avail && h > 1)
                {
                    size *= avail / h;
                    _time.FontSize = size * tr;
                    _date.FontSize = size * dr;
                }
            }

            if (cfg.SecondaryDate) _date.Foreground = t.SecondaryAccent;

            Padding = new Thickness(8, 0, 8, 0);
            Background = System.Windows.Media.Brushes.Transparent;
            Child = panel;

            // a click opens the month calendar
            if (cfg.ClockCalendar)
            {
                Cursor = Cursors.Hand;
                MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
                MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; ToggleCalendar(); };
            }

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += delegate { Tick(); };
            Loaded += delegate { Tick(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Tick();
        }

        void ToggleCalendar()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (PopupWindow.ClosedJustNow(this)) { _popup = null; return; }     // this click's press already closed it
            _popup = new PopupWindow(_mgr, _bar, this, _theme, new CalendarView(_theme, _cfg));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_theme, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        string TimeFormat
        {
            get
            {
                if (_cfg.Use24Hour) return _cfg.ShowSeconds ? "HH:mm:ss" : "HH:mm";
                return _cfg.ShowSeconds ? "h:mm:ss tt" : "h:mm tt";
            }
        }

        string DateText(DateTime now)
        {
            CultureInfo ci = CultureInfo.CurrentCulture;
            switch (_cfg.DateStyle)
            {
                case "Short": return now.ToString(ci.DateTimeFormat.ShortDatePattern, ci);
                case "Long": return now.ToString("dddd, d MMMM yyyy", ci);
                case "ISO": return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                default: return now.ToString("ddd d MMM", ci);
            }
        }

        void Tick()
        {
            DateTime now = DateTime.Now;
            string time = now.ToString(TimeFormat, CultureInfo.CurrentCulture).Trim();
            string date = DateText(now);
            string key = time + "|" + date;
            if (key == _last) return;
            _last = key;

            _time.Text = time;
            _date.Text = date;
            ToolTip = now.ToString("F", CultureInfo.CurrentCulture) + (_cfg.ClockCalendar ? "\nClick for the calendar" : "");

            // grow-only minimum widths so the clock does not wobble as digits change (measure without the old minimum)
            bool grew = Grow(_time) | Grow(_date);
            if (grew) _bar.ContentChanged();
        }

        static bool Grow(TextBlock tb)
        {
            if (tb.Visibility != Visibility.Visible || tb.Parent == null) return false;
            double keep = tb.MinWidth;
            tb.MinWidth = 0;
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(tb.DesiredSize.Width) + 1;
            tb.MinWidth = Math.Max(keep, w);
            return tb.MinWidth > keep;
        }
    }
}
