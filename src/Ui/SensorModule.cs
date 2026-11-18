using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Shows one value HWiNFO or LibreHardwareMonitor publishes (temperature, fan, voltage, clock, power ...) as text, meter, bars or a line graph.
    /// The graph range is RangeMin..RangeMax (e.g. 30..95 for a CPU temperature).
    /// </summary>
    public sealed class SensorModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _bar;
        readonly ModuleConfig _cfg;
        readonly History _hist;
        readonly TextBlock _text = new TextBlock();
        readonly MetricView _view = new MetricView();
        readonly StackPanel _row;
        GaugeFace _gauge;
        DotFace _dots;

        public int RowCount { get { return _cfg.Style == "Tile" || (Dotted && _bar.IsWidget) ? 0 : 1; } }
        bool Gauge { get { return GaugeFace.IsGauge(_cfg.Style); } }
        /// <summary>The "Dots" style (0.38.227).</summary>
        bool Dotted { get { return _cfg.Style == "Dots"; } }

        /// <summary>The warning level as a percent of the graph range (below 0 = off).</summary>
        double WarnPercent
        {
            get
            {
                double sp = _cfg.RangeMax - _cfg.RangeMin;
                return _cfg.WarnOn && sp > 0 ? Math.Max(0, Math.Min(100, (_cfg.WarnAt - _cfg.RangeMin) / sp * 100.0)) : -1;
            }
        }

        public SensorModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            _mgr = mgr; _bar = bar; _cfg = cfg;
            _hist = mgr.HistoryFor(cfg.Id);

            _text.VerticalAlignment = VerticalAlignment.Center;
            _view.VerticalAlignment = VerticalAlignment.Center;
            _view.Data = _hist;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            row.Children.Add(_text);
            row.Children.Add(_view);
            _row = row;
            Child = row;

            ApplyTheme();
        }

        void ApplyTheme()
        {
            var t = _bar.Theme.ForModule(_cfg);
            if (Gauge)
            {
                double sp = _cfg.RangeMax - _cfg.RangeMin;
                double warnPct = _cfg.WarnOn && sp > 0 ? Math.Max(0, Math.Min(100, (_cfg.WarnAt - _cfg.RangeMin) / sp * 100.0)) : -1;
                if (_gauge == null) _gauge = new GaugeFace(_hist);
                _gauge.Setup(_cfg.Style, _bar, t, t.Resolve(_cfg.Color, t.MainAccent), warnPct, _cfg.WarnBelow, false, _cfg.ShownGraphHeightPercent, _cfg.ShowText, _cfg.ShownTileWidth,
                             _cfg.WarnOn && sp > 0 ? Theme.WarnFade : 0, t.Warn(_cfg.WarnColor));
                _gauge.Look(_cfg, t);
                Child = _gauge;
                return;
            }
            if (Dotted)
            {
                if (_dots == null) _dots = new DotFace(_hist);
                _dots.Setup(_cfg, _bar, t, _bar.IsWidget, WarnPercent);
                Child = _dots;
                return;
            }
            Child = _row;
            _view.ApplyLook(_cfg, t);
            bool graph = _cfg.Style != "Text";
            bool text = _cfg.ShowText || !graph;

            _text.Foreground = t.Fg;
            _text.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            _view.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
            _view.Margin = (text && graph) ? new Thickness(8, 0, 0, 0) : new Thickness(0);

            _view.Fill = t.Resolve(_cfg.Color, t.MainAccent);
            // the threshold is in the sensor's own unit (e.g. 80 for 80 °C); the graph works in percent of its range
            double span = _cfg.RangeMax - _cfg.RangeMin;
            _view.WarnFill = t.Warn(_cfg.WarnColor);
            _view.WarnAt = _cfg.WarnOn && span > 0 ? Math.Max(0, Math.Min(100, (_cfg.WarnAt - _cfg.RangeMin) / span * 100.0)) : -1;
            _view.WarnBelow = _cfg.WarnBelow;
            _view.Blend = _cfg.WarnOn && span > 0 ? Theme.WarnFade : 0;
            _view.Track = t.Track;
            _view.GraphStyle = _cfg.Style;
            _view.GraphW = _cfg.ShownGraphWidth;
            _view.GraphH = Math.Max(6, _bar.HostHeight * _cfg.ShownGraphHeightPercent / 100.0);
            _view.InvalidateMeasure();
        }

        /// <summary>Its widget is hidden: Update takes the sample but draws nothing (WidgetWindow.UpdateStats).</summary>
        internal bool Quiet;

        public void Update(bool addSample)
        {
            HwReading r = HwInfo.Find(_cfg.SensorKey);
            string title = _cfg.SensorTitle;
            string txt;
            string valuePart = null;     // drawn in the Secondary Accent colour
            double pct = 0;

            if (string.IsNullOrEmpty(_cfg.SensorKey))
            {
                txt = HwInfo.SourceName + ": pick a sensor";
            }
            else if (r == null)
            {
                if (string.IsNullOrEmpty(title))
                {
                    int bi = _cfg.SensorKey.IndexOf('|');
                    title = bi >= 0 ? _cfg.SensorKey.Substring(bi + 1) : _cfg.SensorKey;
                }
                txt = title + " n/a";
            }
            else
            {
                if (string.IsNullOrEmpty(title)) title = r.Label;
                string unit = string.IsNullOrEmpty(_cfg.SensorUnit) ? r.Unit : _cfg.SensorUnit;
                string num = r.Value.ToString("F" + _cfg.Decimals, System.Globalization.CultureInfo.CurrentCulture);
                string sep = (unit.Length == 0 || unit[0] == '\u00B0' || unit[0] == '%') ? "" : " ";
                string val = num + sep + unit;
                valuePart = val;
                txt = title.Length == 0 ? val : (_cfg.ShownLines2 ? title + "\n" + val : title + " " + val);
                double span = _cfg.RangeMax - _cfg.RangeMin;
                pct = span <= 0 ? 0 : (r.Value - _cfg.RangeMin) / span * 100.0;
                pct = Math.Max(0, Math.Min(100, pct));
            }

            if (addSample && r != null) _hist.Add(pct);
            if (Quiet) return;          // its widget is hidden: the sample only, nothing drawn (0.38.224)
            if (Dotted && _dots != null)
            {
                string unitD = r == null ? "" : (string.IsNullOrEmpty(_cfg.SensorUnit) ? r.Unit : _cfg.SensorUnit);
                string numD = r == null ? "n/a" : r.Value.ToString("F" + _cfg.Decimals, System.Globalization.CultureInfo.CurrentCulture);
                string labD = string.IsNullOrEmpty(title) ? "" : title;
                if (r == null && string.IsNullOrEmpty(_cfg.SensorKey)) { labD = HwInfo.SourceName; numD = "-"; }
                // which sensor: in a tile only (too long for a bar)
                _dots.Show(labD, numD, unitD, r != null && _bar.IsWidget ? r.Sensor : null, r != null && Theme.Crossed(r.Value, _cfg.WarnOn, _cfg.WarnAt, _cfg.WarnBelow), pct);
                ToolTip = r == null ? txt : r.Sensor + "\n" + r.Label + ": " + r.Text;
                return;
            }
            if (Gauge)
            {
                string unitG = r == null ? "" : (string.IsNullOrEmpty(_cfg.SensorUnit) ? r.Unit : _cfg.SensorUnit);
                string numG = r == null ? "n/a" : r.Value.ToString("F" + _cfg.Decimals, System.Globalization.CultureInfo.CurrentCulture);
                string lab = string.IsNullOrEmpty(title) ? "" : title;
                if (r == null && string.IsNullOrEmpty(_cfg.SensorKey)) { lab = HwInfo.SourceName; numG = "-"; }
                _gauge.Show(lab, numG, unitG, null, pct, r != null && Theme.Crossed(r.Value, _cfg.WarnOn, _cfg.WarnAt, _cfg.WarnBelow));
                ToolTip = r == null ? txt : r.Sensor + "\n" + r.Label + ": " + r.Text;
                return;
            }
            _view.Value = pct;
            _view.InvalidateVisual();
            if (valuePart != null && txt.EndsWith(valuePart, StringComparison.Ordinal))
            {
                _text.Inlines.Clear();
                string head = txt.Substring(0, txt.Length - valuePart.Length);
                if (head.EndsWith("\n", StringComparison.Ordinal)) { _text.Inlines.Add(new Run(head.Substring(0, head.Length - 1))); _text.Inlines.Add(new LineBreak()); }
                else if (head.Length > 0) _text.Inlines.Add(new Run(head));
                _text.Inlines.Add(new Run(valuePart) { Foreground = _bar.Theme.SecondaryAccent });
            }
            else _text.Text = txt;
            _text.Foreground = r != null && Theme.Crossed(r.Value, _cfg.WarnOn, _cfg.WarnAt, _cfg.WarnBelow) ? (Brush)_bar.Theme.ForModule(_cfg).Warn(_cfg.WarnColor) : _bar.Theme.Fg;
            ToolTip = r == null ? txt : r.Sensor + "\n" + r.Label + ": " + r.Text;

            // keep the module from shrinking and growing as the number changes
            double keep = _text.MinWidth;
            _text.MinWidth = 0;     // measure the text alone, otherwise the old minimum is counted again every refresh
            _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(_text.DesiredSize.Width) + 2;
            _text.MinWidth = Math.Max(keep, w);
        }
    }
}
