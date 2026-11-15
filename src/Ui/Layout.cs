using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    public enum HostKind
    {
        /// <summary>Takes its natural width.</summary>
        Normal,
        /// <summary>Takes its natural width but gives way (shrinks) when the bar is too narrow - the taskbar.</summary>
        Shrink,
        /// <summary>Flexible gap that soaks up whatever width is left (or a fixed gap when FixedWidth > 0).</summary>
        Spacer
    }

    /// <summary>Wrapper around every module on a bar: carries its config and is the unit that gets dragged.</summary>
    public sealed class ModuleHost : Border
    {
        public readonly ModuleConfig Config;
        public readonly HostKind Kind;

        public ModuleHost(ModuleConfig config, FrameworkElement content, HostKind kind)
        {
            Config = config;
            Kind = kind;
            Background = Brushes.Transparent;   // so the whole module area can be grabbed (Ctrl+drag)
            Child = content;
            if (kind == HostKind.Spacer && config.SpacerWidth > 0) Width = config.SpacerWidth;
        }

        Brush[] _dots;
        Brush _line, _chip;
        const double DotRadius = 2.6, DotGap = 8, DotSpace = 18, LineSpace = 13, ChipPad = 9;

        /// <summary>Zone index on a bar with three zones: 0 left, 1 centre, 2 right.</summary>
        public int ZoneIndex { get { return Config.Zone == "Centre" ? 1 : Config.Zone == "Right" ? 2 : 0; } }

        /// <summary>
        /// The separator look (see BarPlacement.SeparatorStyle), all optional: dots = three dots on the left; line = a thin line on the left;
        /// chip = a rounded chip behind the module. Makes the room each needs.
        /// </summary>
        public void SetLook(Brush[] dots, Brush line, Brush chip)
        {
            _dots = dots; _line = line; _chip = chip;
            double left = 0, right = 0;
            if (dots != null) left = DotSpace;
            else if (line != null) left = LineSpace;
            if (chip != null) { left += ChipPad; right += ChipPad; Margin = new Thickness(2, 0, 2, 0); }
            else Margin = new Thickness(0);
            Padding = new Thickness(left, 0, right, 0);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight, cy = h / 2;
            if (w < 1 || h < 1) return;
            if (_chip != null)
            {
                double ch = Math.Max(12, h * 0.74), r = Math.Min(ch / 2, 12);
                dc.DrawRoundedRectangle(_chip, null, new Rect(0, (h - ch) / 2, w, ch), r, r);
            }
            if (_dots != null)
            {
                double cx = DotSpace / 2;
                double step = Math.Max(2 * DotRadius, Math.Min(2 * DotRadius + DotGap - 4, (h - 4 - 2 * DotRadius) / 2));
                for (int i = -1; i <= 1; i++) dc.DrawEllipse(_dots[i + 1], null, new Point(cx, cy + i * step), DotRadius, DotRadius);
            }
            else if (_line != null)
            {
                double lh = Math.Max(8, h * 0.5);
                dc.DrawRectangle(_line, null, new Rect(Math.Floor(LineSpace / 2), cy - lh / 2, 1, lh));
            }
        }

        public bool FixedSpacer { get { return Kind == HostKind.Spacer && Config.SpacerWidth > 0; } }
    }

    /// <summary>
    /// Lays modules out left to right in config order. Normal modules take their natural width, Shrink modules
    /// (taskbar) take what they need but yield when space runs out, and Spacers share any width that is left.
    /// When the available width is unbounded (auto-width bars) everything is simply natural width.
    /// </summary>
    public sealed class ModulePanel : Panel
    {
        double[] _w = new double[0];
        double[] _x;            // three zones: where each child starts (null = one after the other)

        /// <summary>Three zones (left / centre / right, see ModuleHost.ZoneIndex); only on a bounded (full-width) bar.</summary>
        public bool Zones;
        /// <summary>Three zones: where the left zone ends and the right one starts (panel DIPs), from the last measure.</summary>
        public double CentreFrom, CentreTo;
        /// <summary>How much wider the modules that can't shrink are than the bar (0 = they fit), from the last measure.</summary>
        public double Overflow;

        protected override Size MeasureOverride(Size available)
        {
            int n = InternalChildren.Count;
            _w = new double[n];
            _x = null;
            bool bounded = !double.IsInfinity(available.Width);
            if (Zones && bounded) return MeasureZones(available);
            var natural = new Size(double.PositiveInfinity, available.Height);
            var done = new bool[n];
            double used = 0, maxH = 0;

            for (int i = 0; i < n; i++)
            {
                UIElement c = InternalChildren[i];
                var h = c as ModuleHost;
                HostKind kind = h == null ? HostKind.Normal : h.Kind;
                bool measureNow = !bounded || kind == HostKind.Normal || (h != null && h.FixedSpacer);
                if (!measureNow) continue;
                c.Measure(natural);
                _w[i] = c.DesiredSize.Width;
                used += _w[i];
                maxH = Math.Max(maxH, c.DesiredSize.Height);
                done[i] = true;
            }

            Overflow = bounded ? Math.Max(0, used - available.Width) : 0;
            if (bounded)
            {
                double remaining = Math.Max(0, available.Width - used);

                for (int i = 0; i < n; i++)
                {
                    var h = InternalChildren[i] as ModuleHost;
                    if (done[i] || h == null || h.Kind != HostKind.Shrink) continue;
                    h.Measure(new Size(remaining, available.Height));
                    _w[i] = Math.Min(remaining, h.DesiredSize.Width);
                    remaining = Math.Max(0, remaining - _w[i]);
                    maxH = Math.Max(maxH, h.DesiredSize.Height);
                    done[i] = true;
                }

                int spacers = 0;
                for (int i = 0; i < n; i++) if (!done[i]) spacers++;
                double each = spacers > 0 ? remaining / spacers : 0;
                for (int i = 0; i < n; i++)
                {
                    if (done[i]) continue;
                    InternalChildren[i].Measure(new Size(each, available.Height));
                    _w[i] = each;
                }
            }

            double total = 0;
            for (int i = 0; i < n; i++) total += _w[i];
            return new Size(total, double.IsInfinity(available.Height) ? maxH : available.Height);
        }

        /// <summary>
        /// Three zones. Each zone keeps its modules in config order. The centre zone is centred on the bar and is as wide as its modules
        /// (with a flexible spacer in it: as wide as it can be while the sides still fit); the left and right zones get what is left either
        /// side of it. Inside a zone the taskbar gives way and flexible spacers share only that zone's free room; the right zone's modules
        /// sit against the right end.
        /// </summary>
        Size MeasureZones(Size available)
        {
            int n = InternalChildren.Count;
            _x = new double[n];
            double W = available.Width, maxH = 0;
            var natural = new Size(double.PositiveInfinity, available.Height);
            var zone = new int[n];
            var kind = new HostKind[n];
            var want = new double[n];
            double[] fixedW = new double[3], shrinkW = new double[3];
            int[] flex = new int[3];
            for (int i = 0; i < n; i++)
            {
                UIElement c = InternalChildren[i];
                var h = c as ModuleHost;
                zone[i] = h == null ? 0 : h.ZoneIndex;
                kind[i] = h == null ? HostKind.Normal : h.Kind;
                if (kind[i] == HostKind.Spacer && h != null && h.FixedSpacer) kind[i] = HostKind.Normal;
                if (kind[i] == HostKind.Spacer) { flex[zone[i]]++; continue; }
                c.Measure(natural);
                want[i] = c.DesiredSize.Width;
                maxH = Math.Max(maxH, c.DesiredSize.Height);
                if (kind[i] == HostKind.Shrink) shrinkW[zone[i]] += want[i];
                else fixedW[zone[i]] += want[i];
            }
            double needL = fixedW[0] + shrinkW[0], needR = fixedW[2] + shrinkW[2];
            // too full: the centre zone and the wider side (twice, as the centre stays centred) don't fit
            Overflow = Math.Max(0, Math.Max(fixedW[0] + fixedW[1] + fixedW[2], fixedW[1] + 2 * Math.Max(fixedW[0], fixedW[2])) - W);
            double cw;
            if (flex[1] > 0) cw = Math.Max(fixedW[1], W - 2 * Math.Max(needL, needR));
            else cw = Math.Min(fixedW[1] + shrinkW[1], Math.Max(fixedW[1], W - 2 * Math.Max(fixedW[0], fixedW[2])));
            cw = Math.Max(0, Math.Min(W, cw));
            double side = Math.Max(0, (W - cw) / 2);
            CentreFrom = side; CentreTo = side + cw;
            double[] room = { side, cw, side };

            for (int z = 0; z < 3; z++)
            {
                // the taskbar(s) in this zone take what they need of the room left, then the flexible spacers share the rest
                double remaining = Math.Max(0, room[z] - fixedW[z]);
                for (int i = 0; i < n; i++)
                {
                    if (zone[i] != z) continue;
                    if (kind[i] == HostKind.Normal) _w[i] = want[i];
                    else if (kind[i] == HostKind.Shrink)
                    {
                        InternalChildren[i].Measure(new Size(remaining, available.Height));
                        _w[i] = Math.Min(remaining, InternalChildren[i].DesiredSize.Width);
                        remaining = Math.Max(0, remaining - _w[i]);
                    }
                }
                double each = flex[z] > 0 ? remaining / flex[z] : 0;
                double used = 0;
                for (int i = 0; i < n; i++)
                {
                    if (zone[i] != z) continue;
                    if (kind[i] == HostKind.Spacer) { InternalChildren[i].Measure(new Size(each, available.Height)); _w[i] = each; }
                    used += _w[i];
                }
                // left zone from the left end, centre zone centred, right zone against the right end
                double x = z == 0 ? 0 : z == 1 ? side + (cw - used) / 2 : W - used;
                if (z == 2) x = Math.Max(side + cw, x);
                for (int i = 0; i < n; i++)
                {
                    if (zone[i] != z) continue;
                    _x[i] = x;
                    x += _w[i];
                }
            }
            return new Size(W, double.IsInfinity(available.Height) ? maxH : available.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                double w = i < _w.Length ? _w[i] : 0;
                if (_x != null && i < _x.Length) x = _x[i];
                InternalChildren[i].Arrange(new Rect(x, 0, w, finalSize.Height));
                x += w;
            }
            return finalSize;
        }

        /// <summary>Where child i was last arranged (panel coordinates, DIPs).</summary>
        public Rect SlotOf(int index)
        {
            double w = index < _w.Length ? _w[index] : 0;
            if (_x != null && index < _x.Length) return new Rect(_x[index], 0, w, ActualHeight);
            double x = 0;
            for (int i = 0; i < index && i < _w.Length; i++) x += _w[i];
            return new Rect(x, 0, w, ActualHeight);
        }

        /// <summary>Three zones: the zone a point along the panel is in (0 left, 1 centre, 2 right). An empty or narrow centre zone still
        /// takes drops in the middle third of the bar.</summary>
        public int ZoneAt(double x)
        {
            double w = Math.Max(1, ActualWidth);
            double a = Math.Min(CentreFrom, w / 3), b = Math.Max(CentreTo, w * 2 / 3);
            if (CentreTo - CentreFrom > w / 3) { a = CentreFrom; b = CentreTo; }
            return x < a ? 0 : x < b ? 1 : 2;
        }
    }
}
