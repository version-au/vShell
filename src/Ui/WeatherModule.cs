using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// A small weather picture drawn with shapes (no icon font has a good set): sun / moon in the Secondary Accent colour, clouds in the
    /// text colour, rain / snow / lightning in the Main Accent colour.
    /// </summary>
    public sealed class WeatherGlyph : FrameworkElement
    {
        public WeatherKind Kind;
        public bool IsDay = true;
        public Brush Sun, Cloud, Rain;

        protected override Size MeasureOverride(Size available)
        {
            double s = double.IsNaN(Width) ? 16 : Width;
            return new Size(s, double.IsNaN(Height) ? s : Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 2) return;
            double k = size / 24.0;           // drawn on a 24 x 24 grid
            dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
            dc.PushTransform(new ScaleTransform(k, k));

            Brush sun = Sun ?? Brushes.Gold, cloud = Cloud ?? Brushes.White, drop = Rain ?? Brushes.SteelBlue;
            var thin = new Pen(drop, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var sunPen = new Pen(sun, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var cloudPen = new Pen(cloud, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            switch (Kind)
            {
                case WeatherKind.Clear:
                    if (IsDay) DrawSun(dc, 12, 12, 4.6, sun, sunPen, 1.0);
                    else DrawMoon(dc, 12, 12, 7.5, sun);
                    break;
                case WeatherKind.PartlyCloudy:
                    if (IsDay) DrawSun(dc, 8.5, 8.5, 3.6, sun, sunPen, 0.8);
                    else DrawMoon(dc, 9, 8.5, 5.5, sun);
                    DrawCloud(dc, 3.5, 2.5, 0.85, cloud);
                    break;
                case WeatherKind.Cloudy:
                case WeatherKind.Unknown:
                    DrawCloud(dc, 0, 0, 1.0, cloud);
                    break;
                case WeatherKind.Fog:
                    DrawCloud(dc, 0, -3, 0.9, cloud);
                    for (int i = 0; i < 3; i++) dc.DrawLine(cloudPen, new Point(4 + (i % 2) * 2, 17 + i * 2.6), new Point(20 - ((i + 1) % 2) * 2, 17 + i * 2.6));
                    break;
                case WeatherKind.Drizzle:
                    DrawCloud(dc, 0, -3, 1.0, cloud);
                    for (int i = 0; i < 3; i++) dc.DrawEllipse(drop, null, new Point(7.5 + i * 4.5, 19.5 + (i % 2) * 1.8), 1.1, 1.1);
                    break;
                case WeatherKind.Rain:
                    DrawCloud(dc, 0, -3, 1.0, cloud);
                    for (int i = 0; i < 3; i++) dc.DrawLine(thin, new Point(8.5 + i * 4.2, 17), new Point(7 + i * 4.2, 21.5));
                    break;
                case WeatherKind.Sleet:
                    DrawCloud(dc, 0, -3, 1.0, cloud);
                    dc.DrawLine(thin, new Point(8.5, 17), new Point(7, 21.5));
                    dc.DrawEllipse(drop, null, new Point(12.5, 19.5), 1.2, 1.2);
                    dc.DrawLine(thin, new Point(17, 17), new Point(15.5, 21.5));
                    break;
                case WeatherKind.Snow:
                case WeatherKind.Hail:
                    DrawCloud(dc, 0, -3, 1.0, cloud);
                    for (int i = 0; i < 3; i++)
                    {
                        double x = 7.5 + i * 4.5, y = 19 + (i % 2) * 2;
                        if (Kind == WeatherKind.Hail) dc.DrawEllipse(drop, null, new Point(x, y), 1.4, 1.4);
                        else
                        {
                            dc.DrawLine(thin, new Point(x - 1.5, y), new Point(x + 1.5, y));
                            dc.DrawLine(thin, new Point(x - 0.8, y - 1.3), new Point(x + 0.8, y + 1.3));
                            dc.DrawLine(thin, new Point(x - 0.8, y + 1.3), new Point(x + 0.8, y - 1.3));
                        }
                    }
                    break;
                case WeatherKind.Thunder:
                    DrawCloud(dc, 0, -3, 1.0, cloud);
                    var bolt = new StreamGeometry();
                    using (StreamGeometryContext g = bolt.Open())
                    {
                        g.BeginFigure(new Point(13.5, 14.5), true, true);
                        g.LineTo(new Point(9.5, 19.5), true, false);
                        g.LineTo(new Point(12, 19.5), true, false);
                        g.LineTo(new Point(10.5, 23.5), true, false);
                        g.LineTo(new Point(15.5, 17.8), true, false);
                        g.LineTo(new Point(12.8, 17.8), true, false);
                        g.LineTo(new Point(14.5, 14.5), true, false);
                    }
                    bolt.Freeze();
                    dc.DrawGeometry(drop, null, bolt);
                    break;
                case WeatherKind.Windy:
                    dc.DrawLine(cloudPen, new Point(3, 9), new Point(16, 9));
                    dc.DrawLine(thin, new Point(3, 13), new Point(20, 13));
                    dc.DrawLine(cloudPen, new Point(5, 17), new Point(15, 17));
                    dc.DrawEllipse(null, cloudPen, new Point(17.5, 7.2), 1.8, 1.8);
                    break;
            }
            dc.Pop();
            dc.Pop();
        }

        static void DrawSun(DrawingContext dc, double cx, double cy, double r, Brush fill, Pen rays, double rayScale)
        {
            dc.DrawEllipse(fill, null, new Point(cx, cy), r, r);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4, r1 = r + 2.0 * rayScale, r2 = r + 4.2 * rayScale;
                dc.DrawLine(rays, new Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1), new Point(cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2));
            }
        }

        static void DrawMoon(DrawingContext dc, double cx, double cy, double r, Brush fill)
        {
            var g = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(cx, cy), r, r),
                new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85));
            g.Freeze();
            dc.DrawGeometry(fill, null, g);
        }

        /// <summary>A cloud about 19 x 10 units, its top-left near (2.5 + dx, 7 + dy) at scale 1.</summary>
        static void DrawCloud(DrawingContext dc, double dx, double dy, double s, Brush fill)
        {
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            g.Children.Add(new EllipseGeometry(new Point(8.5, 13), 4.6, 4.6));
            g.Children.Add(new EllipseGeometry(new Point(13.5, 10.5), 5.8, 5.8));
            g.Children.Add(new EllipseGeometry(new Point(18, 13.8), 3.8, 3.8));
            g.Children.Add(new RectangleGeometry(new Rect(5, 13, 16.5, 4.6), 2.3, 2.3));
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(s, s, 12, 12));
            tg.Children.Add(new TranslateTransform(dx, dy));
            g.Transform = tg;
            g.Freeze();
            dc.DrawGeometry(fill, null, g);
        }
    }

    /// <summary>
    /// Weather: icon + temperature (and optionally the condition and today's high / low). Hover for details, click for the forecast.
    /// Data from Open-Meteo or a Home Assistant weather entity (see WeatherService); the module only shows the cached report.
    /// </summary>
    public sealed class WeatherModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly WeatherGlyph _icon = new WeatherGlyph();
        readonly TextBlock _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        readonly DispatcherTimer _timer;
        WeatherReport _shown;
        string _shownKey;
        PopupWindow _popup;
        readonly StackPanel _forecast;     // widgets: the forecast under the current weather (null when not shown)

        /// <summary>With the forecast shown in a widget the module sizes itself (0); otherwise one row.</summary>
        public int RowCount { get { return _forecast != null ? 0 : 1; } }

        public WeatherModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _t = bar.Theme.ForModule(cfg);
            _text.Foreground = _t.Fg;
            _text.FontFamily = _t.Font;
            _text.FontSize = _t.FontSize;
            double iconSize = Math.Round(Math.Min(bar.HostHeight * 0.72, _t.FontSize * 1.9));
            _icon.Width = iconSize;
            _icon.Height = iconSize;
            _icon.Margin = new Thickness(0, 0, 6, 0);
            _icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Sun = _t.SecondaryAccent;
            _icon.Cloud = _t.Fg;
            _icon.Rain = _t.MainAccent;

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_icon);
            row.Children.Add(_text);
            if (bar.IsWidget && cfg.WeatherShowForecast)
            {
                _forecast = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
                TextElement_SetSize(_forecast, _t.FontSize);
                var col = new StackPanel();
                col.Children.Add(row);
                col.Children.Add(_forecast);
                Child = col;
            }
            else Child = row;
            Padding = new Thickness(6, 0, 6, 0);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;

            // handle the press too, or the bar / widget takes it as the start of a drag and the click never arrives
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { Refresh(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        string Deg(double v)
        {
            return double.IsNaN(v) ? "--°" : Math.Round(v).ToString("0", CultureInfo.CurrentCulture) + "°";
        }

        void Refresh()
        {
            WeatherReport r = WeatherService.Get(_cfg);
            string key = r == null ? "none" : r.Fetched.Ticks + "|" + r.Error;
            if (key == _shownKey) return;
            _shownKey = key;
            _shown = r;

            if (!WeatherService.IsSetUp(_cfg))
            {
                _icon.Kind = WeatherKind.PartlyCloudy;
                _icon.IsDay = true;
                IWeatherSource src = WeatherService.SourceOf(_cfg);
                _text.Text = src != null ? src.SetUpText : WeatherService.SourceMissing(_cfg) ? "Add its plugin in Settings > Plugins" : "Set a location";
                ToolTip = "Choose where the weather comes from in Settings (this module's settings).";
            }
            else if (r == null || r.Fetched == DateTime.MinValue)
            {
                _icon.Kind = WeatherKind.Cloudy;
                _text.Text = r != null && r.Error.Length > 0 ? "n/a" : "…";
                ToolTip = r != null && r.Error.Length > 0 ? "Could not get the weather: " + r.Error : "Getting the weather…";
            }
            else
            {
                _icon.Kind = r.Kind;
                _icon.IsDay = r.IsDay;
                string txt = Deg(r.Temp), more = "";
                if (_cfg.WeatherShowCondition && r.Condition.Length > 0) more += (more.Length > 0 ? "  " : "") + r.Condition;
                WeatherDay today = r.Days.Find(delegate (WeatherDay d) { return d.Date == DateTime.Today; });
                if (_cfg.WeatherShowHighLow && today != null) more += (more.Length > 0 ? "  " : "") + "↑" + Deg(today.High) + " ↓" + Deg(today.Low);
                // M on a bar: the temperature over the rest
                if (more.Length > 0) txt += (_cfg.ShownTwoLines == true ? "\n" : "  ") + more;
                _text.Text = txt;
                ToolTip = TipText(r, today);
            }
            _icon.InvalidateVisual();
            if (_forecast != null)
            {
                _forecast.Children.Clear();
                if (r != null && r.Fetched != DateTime.MinValue)
                    _forecast.Children.Add(ForecastView(r, _t, _cfg.WeatherForecastDays, _cfg.TileLayout == "Row", _cfg.WeatherForecastSpacing));
            }
            if (_popup != null && _popup.IsOpen) _popup.SetContent(BuildPopup());
            _bar.ContentChanged();
        }

        string TipText(WeatherReport r, WeatherDay today)
        {
            var lines = new List<string>();
            lines.Add(r.Place);
            string now = r.Condition + (r.Condition.Length > 0 ? ", " : "") + Deg(r.Temp) + TrimUnit(r.TempUnit);
            if (!double.IsNaN(r.Feels)) now += " (feels like " + Deg(r.Feels) + ")";
            lines.Add(now);
            var extra = new List<string>();
            if (today != null) extra.Add("High " + Deg(today.High) + ", low " + Deg(today.Low));
            if (!double.IsNaN(r.Humidity)) extra.Add("Humidity " + Math.Round(r.Humidity) + "%");
            if (!double.IsNaN(r.Wind)) extra.Add("Wind " + Math.Round(r.Wind) + " " + r.WindUnit);
            if (extra.Count > 0) lines.Add(string.Join("  ·  ", extra));
            lines.Add("Updated " + TimeText.Clock(r.Fetched.ToLocalTime()) + " from " + r.Source + "  -  click for the forecast");
            if (r.Error.Length > 0) lines.Add("Last update failed: " + r.Error);
            return string.Join("\n", lines);
        }

        static string TrimUnit(string u) { return string.IsNullOrEmpty(u) ? "" : u.Replace("°", ""); }

        // ---- forecast popup --------------------------------------------------------------------------------
        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (PopupWindow.ClosedJustNow(this)) { _popup = null; return; }     // this click's press already closed it
            if (_shown == null || _shown.Fetched == DateTime.MinValue) { _mgr.OpenSettings(null); return; }
            // the module's own theme (its font / colours), drawn in the widget style like the other popups
            _popup = new PopupWindow(_mgr, _bar, this, _t, BuildPopup());
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        FrameworkElement BuildPopup()
        {
            Theme t = _t;
            WeatherReport r = _shown;
            var root = new StackPanel { Margin = new Thickness(8, 6, 8, 6), MinWidth = 240 };
            if (r == null) return root;

            root.Children.Add(new TextBlock { Text = r.Place, FontWeight = FontWeights.SemiBold, Foreground = t.Fg, Margin = new Thickness(0, 0, 0, 6) });
            var now = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            now.Children.Add(new WeatherGlyph { Kind = r.Kind, IsDay = r.IsDay, Width = 44, Height = 44, Sun = t.SecondaryAccent, Cloud = t.Fg, Rain = t.MainAccent, Margin = new Thickness(0, 0, 12, 0) });
            var nowText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nowText.Children.Add(new TextBlock { Text = Deg(r.Temp), FontSize = t.FontSize * 2.1, FontWeight = FontWeights.SemiBold, Foreground = t.Fg });
            string sub = r.Condition;
            if (!double.IsNaN(r.Feels)) sub += (sub.Length > 0 ? "  ·  " : "") + "feels like " + Deg(r.Feels);
            if (sub.Length > 0) nowText.Children.Add(new TextBlock { Text = sub, Foreground = t.FgDim });
            now.Children.Add(nowText);
            root.Children.Add(now);

            root.Children.Add(ForecastView(r, t, 7, false, 4));

            root.Children.Add(new TextBlock
            {
                Text = "Updated " + TimeText.Clock(r.Fetched.ToLocalTime()) + "  ·  " + r.Source,
                Foreground = t.FgDim, FontSize = Math.Max(9, t.FontSize * 0.85), Margin = new Thickness(0, 10, 0, 0)
            });
            return root;
        }

        static void TextElement_SetSize(DependencyObject d, double size) { System.Windows.Documents.TextElement.SetFontSize(d, size); }

        /// <summary>The forecast: a day per line (day, icon, high, low, chance of rain), or the days side by side.</summary>
        static FrameworkElement ForecastView(WeatherReport r, Theme t, int maxDays, bool sideBySide, double spacing)
        {
            double icon = Math.Round(Math.Max(18, t.FontSize * 1.7));
            var days = new List<WeatherDay>();
            foreach (WeatherDay d in r.Days) if (d.Date >= DateTime.Today && days.Count < maxDays) days.Add(d);
            if (days.Count == 0) return new TextBlock { Text = "No forecast available.", Foreground = t.FgDim };

            var grid = new Grid();
            if (sideBySide)
            {
                for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < days.Count; i++)
                {
                    WeatherDay d = days[i];
                    if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(spacing) });     // gap between days
                    int c = grid.ColumnDefinitions.Count;
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = icon + 18 });
                    string day = d.Date == DateTime.Today ? "Today" : d.Date.ToString("ddd", CultureInfo.CurrentCulture);
                    AddCell(grid, 0, c, new TextBlock { Text = day, Foreground = t.FgDim, HorizontalAlignment = HorizontalAlignment.Center });
                    AddCell(grid, 1, c, new WeatherGlyph { Kind = d.Kind, Width = icon, Height = icon, Sun = t.SecondaryAccent, Cloud = t.Fg, Rain = t.MainAccent, Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Center });
                    AddCell(grid, 2, c, new TextBlock { Text = Deg2(d.High) + " / " + Deg2(d.Low), Foreground = t.Fg, HorizontalAlignment = HorizontalAlignment.Center });
                    if (d.RainChance >= 0)
                        AddCell(grid, 3, c, new TextBlock { Text = d.RainChance + "%", Foreground = t.MainAccent, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Chance of rain" });
                }
                return grid;
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < days.Count; i++)
            {
                WeatherDay d = days[i];
                if (i > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(spacing) });     // gap between lines
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string day = d.Date == DateTime.Today ? "Today" : d.Date.ToString("ddd", CultureInfo.CurrentCulture);
                AddCell(grid, row, 0, new TextBlock { Text = day, Foreground = t.Fg, MinWidth = 48, VerticalAlignment = VerticalAlignment.Center });
                AddCell(grid, row, 1, new WeatherGlyph { Kind = d.Kind, Width = icon, Height = icon, Sun = t.SecondaryAccent, Cloud = t.Fg, Rain = t.MainAccent, Margin = new Thickness(4, 2, 12, 2) });
                AddCell(grid, row, 2, new TextBlock { Text = Deg2(d.High), Foreground = t.Fg, MinWidth = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
                AddCell(grid, row, 3, new TextBlock { Text = Deg2(d.Low), Foreground = t.FgDim, MinWidth = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
                if (d.RainChance >= 0)
                    AddCell(grid, row, 4, new TextBlock { Text = d.RainChance + "%", Foreground = t.MainAccent, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Chance of rain" });
            }
            return grid;
        }

        static string Deg2(double v) { return double.IsNaN(v) ? "--°" : Math.Round(v).ToString("0", CultureInfo.CurrentCulture) + "°"; }

        static void AddCell(Grid g, int row, int col, UIElement e)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }
    }
}
