using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The Clock module's calendar (like the one Windows shows from its taskbar clock): today's long date at the top (click it to come
    /// back to today), the month with ‹ › arrows (the mouse wheel moves a month too), and the days in a grid - today in a filled accent
    /// circle, the day you click in a ring, the other months' days faded. Optional week numbers; the week starts on the Windows setting
    /// unless the module says Monday / Sunday.
    /// </summary>
    public sealed class CalendarView : Border
    {
        readonly Theme _t;
        readonly ModuleConfig _cfg;
        readonly TextBlock _today = new TextBlock(), _month = new TextBlock();
        readonly UniformGrid _days = new UniformGrid();
        readonly Grid _gridHost = new Grid();
        readonly DispatcherTimer _midnight;
        DateTime _shown;              // the first of the month on show
        DateTime _picked;             // the day clicked (MinValue = none)
        DateTime _lastToday;
        double _fs;

        public CalendarView(Theme t, ModuleConfig cfg)
        {
            _t = t; _cfg = cfg;
            _fs = t.FontSize;
            DateTime now = DateTime.Today;
            _shown = new DateTime(now.Year, now.Month, 1);
            _picked = DateTime.MinValue;
            _lastToday = now;
            Padding = new Thickness(4, 2, 4, 2);
            Background = Brushes.Transparent;

            var root = new StackPanel { Width = Math.Round(_fs * (cfg.ClockWeekNumbers ? 25 : 22.5)) };
            // today, as Windows writes it at the top of its calendar
            _today.FontFamily = t.Font; _today.FontSize = _fs * 1.1; _today.FontWeight = FontWeights.SemiBold; _today.Foreground = t.Fg;
            _today.Cursor = Cursors.Hand; _today.ToolTip = "Back to today"; _today.Margin = new Thickness(4, 2, 0, 10);
            _today.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; GoToday(); };
            root.Children.Add(_today);
            root.Children.Add(new Border { Height = 1, Background = Theme.Faded(t.Fg, 12), Margin = new Thickness(0, 0, 0, 8) });

            // the month and the arrows
            var head = new Grid { Margin = new Thickness(4, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _month.FontFamily = t.Font; _month.FontSize = _fs * 1.02; _month.FontWeight = FontWeights.SemiBold; _month.Foreground = t.Fg; _month.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(_month);
            var arrows = new StackPanel { Orientation = Orientation.Horizontal };
            arrows.Children.Add(Arrow("", "Previous month", -1));
            arrows.Children.Add(Arrow("", "Next month", 1));
            Grid.SetColumn(arrows, 1);
            head.Children.Add(arrows);
            root.Children.Add(head);

            _gridHost.Children.Add(_days);
            root.Children.Add(_gridHost);
            _gridHost.MouseWheel += delegate (object s, MouseWheelEventArgs e) { e.Handled = true; Move(e.Delta > 0 ? -1 : 1); };

            // date and time settings, as Windows' own calendar has under it
            var foot = Pills.Make(t, "", "Date and time settings", "Open Windows' date and time settings", false, delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); } catch { }
            });
            foot.HorizontalAlignment = HorizontalAlignment.Left;
            foot.Margin = new Thickness(2, 10, 0, 2);
            root.Children.Add(foot);
            Child = root;

            // past midnight: today moves on
            _midnight = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
            _midnight.Tick += delegate { if (DateTime.Today != _lastToday) { _lastToday = DateTime.Today; Build(); } };
            Loaded += delegate { _midnight.Start(); };
            Unloaded += delegate { _midnight.Stop(); };
            Build();
        }

        Border Arrow(string glyph, string tip, int dir)
        {
            double d = Math.Round(_fs * 2.3);
            var b = new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip,
                Child = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = _fs * 0.85, Foreground = _t.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            b.MouseEnter += delegate { b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Move(dir); };
            return b;
        }

        void Move(int months) { _shown = _shown.AddMonths(months); Build(); }

        void GoToday()
        {
            DateTime now = DateTime.Today;
            _shown = new DateTime(now.Year, now.Month, 1);
            _picked = DateTime.MinValue;
            Build();
        }

        DayOfWeek FirstDay
        {
            get
            {
                switch (_cfg.ClockWeekStart)
                {
                    case "Monday": return DayOfWeek.Monday;
                    case "Sunday": return DayOfWeek.Sunday;
                    default: return CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                }
            }
        }

        void Build()
        {
            CultureInfo ci = CultureInfo.CurrentCulture;
            DateTime today = DateTime.Today;
            _today.Text = today.ToString("dddd, d MMMM", ci);
            _month.Text = _shown.ToString("MMMM yyyy", ci);
            bool weeks = _cfg.ClockWeekNumbers;
            _days.Columns = weeks ? 8 : 7;
            _days.Children.Clear();
            DayOfWeek first = FirstDay;

            // the day names
            if (weeks) _days.Children.Add(Cell("", _t.FgDim, 0.8, false));
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)first + i) % 7);
                string n = ci.DateTimeFormat.GetShortestDayName(dow);
                if (n.Length > 2) n = n.Substring(0, 2);
                _days.Children.Add(Cell(n, _t.FgDim, 0.85, false));
            }

            // six weeks from the start of the week holding the 1st
            int back = ((int)_shown.DayOfWeek - (int)first + 7) % 7;
            DateTime d = _shown.AddDays(-back);
            for (int w = 0; w < 6; w++)
            {
                if (weeks)
                {
                    int wk = ci.Calendar.GetWeekOfYear(d.AddDays(3), CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);   // ISO-style
                    _days.Children.Add(Cell(wk.ToString(ci), Theme.Faded(_t.FgDim, 70), 0.75, false));
                }
                for (int i = 0; i < 7; i++)
                {
                    _days.Children.Add(Day(d, today));
                    d = d.AddDays(1);
                }
            }
        }

        FrameworkElement Cell(string text, Brush b, double scale, bool bold)
        {
            return new TextBlock
            {
                Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _fs * scale, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4)
            };
        }

        FrameworkElement Day(DateTime d, DateTime today)
        {
            bool inMonth = d.Month == _shown.Month, isToday = d == today, picked = d == _picked;
            double size = Math.Round(_fs * 2.55);
            var num = new TextBlock
            {
                Text = d.Day.ToString(CultureInfo.CurrentCulture), FontFamily = _t.Font, FontSize = _fs * 0.95,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isToday ? NoteRender.Contrast(_t.MainAccent) : inMonth ? _t.Fg : Theme.Faded(_t.FgDim, 60),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Brush rest = isToday ? (Brush)_t.MainAccent : Brushes.Transparent;
            var b = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = num, Background = rest, Margin = new Thickness(0, 1, 0, 1),
                BorderThickness = new Thickness(picked && !isToday ? 1.5 : 0), BorderBrush = _t.MainAccent, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            int diff = (int)(d - today).TotalDays;
            b.ToolTip = d.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture) +
                        (diff == 0 ? " · today" : diff == 1 ? " · tomorrow" : diff == -1 ? " · yesterday" : diff > 0 ? " · in " + diff + " days" : " · " + (-diff) + " days ago");
            b.MouseEnter += delegate { if (!isToday) b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = rest; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _picked = d;
                if (!inMonth) _shown = new DateTime(d.Year, d.Month, 1);     // a day of the month before / after: go there
                Build();
            };
            return b;
        }
    }
}
