using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The "Ring" and "Tile" faces of the gauge modules (CPU, memory, GPU, disk, HWiNFO sensors).
    /// Ring: a round gauge with the value inside and the label beside it, as tall as the bar / widget row.
    /// Tile: the label, a big number and a sparkline (or a meter) - stacked in a widget, side by side in a bar.
    /// Past the warning level the ring / number / line turn the Secondary Accent colour.
    /// </summary>
    public sealed class GaugeFace : Border
    {
        readonly MetricView _graph = new MetricView();
        readonly TextBlock _label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _detail = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _value = new TextBlock();
        readonly Run _num = new Run(), _unit = new Run();
        Theme _t;
        SolidColorBrush _warn;
        string _style = "Ring";
        bool _meter;

        /// <summary>data2 = an optional second series for the tile's line (network upload), drawn in Fill2.</summary>
        public GaugeFace(History data, History data2 = null)
        {
            _graph.Data = data;
            _graph.Data2 = data2;
            _value.Inlines.Add(_num);
            _value.Inlines.Add(_unit);
            Background = Brushes.Transparent;
        }

        public static bool IsGauge(string style) { return style == "Ring" || style == "Tile"; }

        /// <summary>The module's Graph look (after Setup): the tile's sparkline - line, fill, curves, time shown - and colour by load.</summary>
        internal void Look(ModuleConfig cfg, Theme t)
        {
            _graph.ApplyLook(cfg, t);
            _graph.InvalidateVisual();
        }

        /// <summary>The second series' colour (the tile's line).</summary>
        public Brush Fill2 { set { _graph.Fill2 = value; } }

        /// <summary>Above 0: the values and the tile's line are amounts and this fills the gauge (MetricView.FullScale); the warning is off.</summary>
        public double FullScale
        {
            set
            {
                _graph.FullScale = value;
                if (value > 0) _graph.WarnAt = -1;
            }
        }

        bool _showLabel = true;
        /// <summary>A tile in a widget: at least the widget's module height, its graph filling down to the bottom edge.</summary>
        IModuleHost _fillHost;

        protected override Size MeasureOverride(Size constraint)
        {
            Size s = base.MeasureOverride(constraint);
            if (_fillHost != null)
            {
                double want = _fillHost.HostHeight;     // read each time: it follows the module height while the widget is resized
                if (want > s.Height) s.Height = Math.Min(want, double.IsInfinity(constraint.Height) ? want : constraint.Height);
            }
            return s;
        }

        /// <summary>
        /// Build the face. meter = a tile shows a meter instead of the sparkline (disk space). Ring: sizePercent = its diameter as a percentage
        /// of the bar / module row height; showLabel = the label beside it.
        /// </summary>
        public void Setup(string style, IModuleHost host, Theme t, Brush fill, double warnAt, bool warnBelow, bool meter, double sizePercent, bool showLabel, double tileWidth = 0, double blend = 0, SolidColorBrush warnFill = null)
        {
            _graph.Blend = blend;
            _fillHost = style == "Tile" && host.IsWidget ? host : null;
            _warn = warnFill ?? t.SecondaryAccent;
            _t = t; _style = style; _meter = meter; _showLabel = showLabel;
            _graph.Fill = fill;
            _graph.Track = t.Track;
            _graph.WarnFill = _warn;
            _graph.WarnAt = warnAt;
            _graph.WarnBelow = warnBelow;
            _graph.Face = new Typeface(t.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            _graph.CenterBrush = t.Fg;
            _label.Foreground = t.Fg;
            _detail.Foreground = t.FgDim;
            _value.Foreground = t.Fg;
            _unit.Foreground = t.FgDim;
            _label.FontSize = t.FontSize;
            _detail.FontSize = Math.Max(7, t.FontSize * 0.85);
            _value.FontWeight = FontWeights.Bold;
            _graph.HorizontalAlignment = HorizontalAlignment.Left;
            _graph.VerticalAlignment = VerticalAlignment.Center;
            _graph.ClearValue(FrameworkElement.HeightProperty);
            Padding = new Thickness(6, 0, 6, 0);

            double hh = Math.Max(16, host.HostHeight);
            if (style == "Ring")
            {
                double d = Math.Max(12, Math.Round(hh * Math.Max(20, Math.Min(100, sizePercent)) / 100.0));
                _graph.GraphStyle = "Ring";
                _graph.GraphW = _graph.GraphH = d;
                _graph.CenterSize = Math.Max(6, d * 0.3);
                _graph.NoTrack = false;
                _graph.Margin = new Thickness(0);
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0),
                                              Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed };
                Detach();
                labels.Children.Add(_label);
                labels.Children.Add(_detail);
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(_graph);
                row.Children.Add(labels);
                Child = row;
                return;
            }

            // Tile: the label at the top; under it the sparkline (or, for disk space, a meter) fills the rest of the module - its full width,
            // right down to the bottom edge (of the bar / the module's row) - with the big number drawn over it. Same in a bar and a widget.
            Detach();
            bool bar = !host.IsWidget;
            Padding = new Thickness(0);
            ClearValue(HeightProperty);
            if (bar) Height = Math.Max(16, host.HostHeight - 2);       // as tall as the bar, so the line sits on its bottom edge
            ClearValue(WidthProperty);
            if (tileWidth >= 40) Width = tileWidth;                      // its own width (the label trims, the line follows)
            // in a bar the text scales with the bar's height so nothing is cut off; under 35 px the number goes on the label's line
            double h = Math.Max(16, host.HostHeight - 2);
            bool oneLine = bar && host.HostHeight < 35;
            _label.Foreground = t.FgDim;
            _label.Margin = bar ? new Thickness(6, oneLine ? 1 : 2, oneLine ? 0 : 6, 0) : new Thickness(6, 4, 6, 2);
            _label.VerticalAlignment = VerticalAlignment.Center;
            _value.VerticalAlignment = oneLine ? VerticalAlignment.Center : VerticalAlignment.Top;
            _value.HorizontalAlignment = HorizontalAlignment.Left;
            if (!bar)
            {
                _label.FontSize = Math.Max(7, t.FontSize * 0.92);
                _value.FontSize = t.FontSize * 1.75;
                _value.Margin = new Thickness(6, 0, 6, 0);
            }
            else if (oneLine)
            {
                _label.FontSize = Math.Max(7, Math.Min(t.FontSize * 0.8, h * 0.34));
                _value.FontSize = Math.Max(7, Math.Min(t.FontSize * 0.9, h * 0.38));
                _value.Margin = new Thickness(5, oneLine ? 1 : 0, 6, 0);
            }
            else
            {
                _label.FontSize = Math.Max(7, Math.Min(t.FontSize * 0.78, h * 0.25));
                _value.FontSize = Math.Max(7, Math.Min(t.FontSize * 0.95, h * 0.34));
                _value.Margin = new Thickness(6, -1, 6, 0);
            }
            _unit.FontSize = bar ? _value.FontSize * 0.75 : t.FontSize * 0.95;
            _unit.FontWeight = FontWeights.Normal;
            _graph.GraphStyle = meter ? "Meter" : "Line";
            _graph.NoTrack = !meter;
            _graph.HorizontalAlignment = HorizontalAlignment.Stretch;
            _graph.Margin = new Thickness(0);
            _graph.GraphW = bar ? 64 : 80;                                   // the least width it asks for
            if (meter)
            {
                _graph.VerticalAlignment = VerticalAlignment.Bottom;          // a thin meter along the bottom edge
                _graph.GraphH = bar ? Math.Max(3, Math.Round(host.HostHeight * 0.12)) : 6;
            }
            else
            {
                _graph.VerticalAlignment = VerticalAlignment.Stretch;         // fills from under the label to the bottom
                _graph.GraphH = bar ? 4 : Math.Max(30, Math.Round(t.FontSize * 3.6));
            }
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (!bar && meter) g.RowDefinitions[1].MinHeight = Math.Round(t.FontSize * 3.2);
            Grid.SetRow(_graph, 1);
            if (oneLine)
            {
                // a slim bar: "CPU · 61°C  34%" on one line, the graph under it
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(_label);
                line.Children.Add(_value);
                g.Children.Add(line);
                g.Children.Add(_graph);
            }
            else
            {
                Grid.SetRow(_value, 1);
                g.Children.Add(_label);
                g.Children.Add(_graph);
                g.Children.Add(_value);      // drawn over the line, so it stays readable
            }
            Child = g;
            _graph.InvalidateMeasure();
        }

        void Detach()
        {
            foreach (FrameworkElement e in new FrameworkElement[] { _graph, _label, _detail, _value })
            {
                var p = e.Parent as Panel;
                if (p != null) p.Children.Remove(e);
            }
            Child = null;
        }

        /// <summary>
        /// The values: label ("CPU"), number ("34") and unit ("%"), an optional detail line (ring: under the label; tile: after the label),
        /// the graph position 0..100 and the raw value the warning is judged on. percent2 = the second series now (below 0 = none).
        /// </summary>
        public void Show(string label, string number, string unit, string detail, double percent, bool warn, string temp = null, double percent2 = -1)
        {
            _graph.Value = percent;
            _graph.Value2 = percent2;
            _graph.InvalidateVisual();
            Brush accent = warn ? (Brush)_warn : _t.Fg;
            if (_style == "Ring")
            {
                _graph.Center = number + (unit == "%" ? "%" : "");
                _graph.CenterBrush = warn ? (Brush)_warn : _t.Fg;
                _label.Text = string.IsNullOrEmpty(label) ? (unit != "%" ? number + unit : "") : (unit != "%" ? label + " " + number + unit : label);
                _label.Foreground = warn ? (Brush)_warn : _t.Fg;
                _detail.Inlines.Clear();
                if (!string.IsNullOrEmpty(detail)) _detail.Inlines.Add(new Run(detail));
                if (!string.IsNullOrEmpty(temp))
                {
                    if (!string.IsNullOrEmpty(detail)) _detail.Inlines.Add(new Run(" \u00B7 "));
                    _detail.Inlines.Add(new Run(temp) { Foreground = _t.SecondaryAccent });
                }
                _detail.Visibility = string.IsNullOrEmpty(detail) && string.IsNullOrEmpty(temp) ? Visibility.Collapsed : Visibility.Visible;
                _label.Visibility = string.IsNullOrEmpty(_label.Text) ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
            // label · detail · temperature (the temperature in the Secondary Accent colour)
            _label.Inlines.Clear();
            var parts = new System.Collections.Generic.List<Run>();
            if (!string.IsNullOrEmpty(label)) parts.Add(new Run(label));
            if (!string.IsNullOrEmpty(detail)) parts.Add(new Run(detail));
            if (!string.IsNullOrEmpty(temp)) parts.Add(new Run(temp) { Foreground = _t.SecondaryAccent });
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) _label.Inlines.Add(new Run(" \u00B7 "));
                _label.Inlines.Add(parts[i]);
            }
            _num.Text = number ?? "";
            _unit.Text = string.IsNullOrEmpty(unit) ? "" : (unit == "%" || unit.StartsWith("°") ? unit : " " + unit);
            _value.Foreground = accent;
        }
    }
}
