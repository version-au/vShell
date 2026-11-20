using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Base of the simple "text plus optional graph" modules (network, disk, battery, volume, uptime, top processes, label).
    /// A subclass only fills in Read(); this class does the layout, theme colours, graph history, tooltip and mouse handling.
    /// </summary>
    [PluginApi]
    public abstract class InfoModule : Border
    {
        protected sealed class Reading
        {
            public string Text = "";
            public string Tip = "";
            /// <summary>0..100, drives the graph.</summary>
            public double Percent;
            /// <summary>Optional second series 0..100, drawn in the Secondary Accent colour (-1 = none).</summary>
            public double Percent2 = -1;
            /// <summary>true = collapse the whole module (e.g. a battery module on a desktop PC).</summary>
            public bool Hide;
            /// <summary>Segoe Fluent Icons glyph shown before the text (empty = no icon).</summary>
            public string Icon = "";
            /// <summary>Ring / Tile styles: the label, number, unit and an optional detail line (empty Number = worked out from Percent).</summary>
            public string Label = "", Number = "", Unit = "%", Detail = "";
            /// <summary>
            /// true = Percent / Percent2 are amounts (MB/s …) and the graph's full scale follows the highest of what it shows, rounded up
            /// (MetricView.ScaleFor), never below ScaleMin. The warning colour is off then. GraphScale has the scale in use.
            /// </summary>
            public bool AutoScale;
            public double ScaleMin = 1;
        }

        /// <summary>The full scale of an auto-scaled graph (in the amounts' unit) after the last update; 0 = a fixed scale.</summary>
        protected double GraphScale { get; private set; }

        protected readonly BarManager Mgr;
        protected readonly IModuleHost Bar;
        protected readonly ModuleConfig Cfg;
        protected readonly TextBlock Txt = new TextBlock();
        readonly TextBlock _icon = new TextBlock();
        readonly History _hist;
        readonly History _hist2;
        bool _wasPlaceholder = true;
        readonly MetricView _view = new MetricView();
        readonly StackPanel _row;
        readonly Grid _rowHost;
        readonly Border _level;
        GaugeFace _gauge;
        DotFace _dots;
        string _shownKey;
        bool _down;

        /// <summary>Offers the "Dots" style (0.38.227: the gauge modules - disk, network, a plugin's built on this - and battery).</summary>
        private protected virtual bool HasDots { get { return HasGauges; } }
        /// <summary>The "Dots" style is shown.</summary>
        bool Dotted { get { return UsesGraph && HasDots && GraphStyle == "Dots"; } }
        /// <summary>Dots as a tile: in a widget, for a module that can be taller than one row (it is IMultiRow and asks for 0 rows then).</summary>
        private protected bool DotTile { get { return Dotted && Bar.IsWidget && this is IMultiRow; } }

        /// <summary>Offers the Ring and Tile styles (disk).</summary>
        protected virtual bool HasGauges { get { return false; } }
        /// <summary>A Tile shows a meter instead of the sparkline.</summary>
        protected virtual bool TileMeter { get { return false; } }
        bool Gauge { get { return HasGauges && UsesGraph && GaugeFace.IsGauge(GraphStyle); } }
        /// <summary>A tile in a widget is taller than one row (subclasses that are IMultiRow use this).</summary>
        protected bool TallTile { get { return (Gauge && GraphStyle == "Tile") || DotTile; } }     // a Dots tile too (0.38.227 - a plugin's module that uses this gets it)

        /// <summary>Offers Style / graph settings.</summary>
        protected virtual bool UsesGraph { get { return false; } }
        /// <summary>The graph style actually drawn (a module can limit the choices).</summary>
        protected virtual string GraphStyle { get { return Cfg.Style; } }
        /// <summary>A left click does something (OnClick).</summary>
        protected virtual bool Clickable { get { return false; } }
        /// <summary>The mouse wheel does something (OnWheel).</summary>
        protected virtual bool Wheelable { get { return false; } }

        protected InfoModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg)
        {
            Mgr = mgr; Bar = bar; Cfg = cfg;
            _hist = mgr.HistoryFor(cfg.Id);
            _hist2 = mgr.HistoryFor(cfg.Id + ".2");
            Background = Brushes.Transparent;

            Txt.VerticalAlignment = VerticalAlignment.Center;
            _icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.FontFamily = IconUtil.GlyphFont;
            _icon.Visibility = Visibility.Collapsed;
            _view.VerticalAlignment = VerticalAlignment.Center;
            _view.Data = _hist;
            _view.Data2 = _hist2;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            row.Children.Add(_icon);
            row.Children.Add(Txt);
            row.Children.Add(_view);
            _row = row;
            // the row, with room for a thin line under it (Volume's sound level) when a module asks for one
            _level = new Border { Height = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _rowHost = new Grid();
            _rowHost.Children.Add(row);
            _rowHost.Children.Add(_level);
            Child = _rowHost;

            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (!Clickable) return;
                e.Handled = true; _down = true;
            };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!Clickable || !_down) return;
                _down = false; e.Handled = true;
                OnClick();
            };
            MouseLeave += delegate { _down = false; };
            MouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                if (!Wheelable) return;
                e.Handled = true;
                OnWheel(e.Delta);
            };
            if (Clickable) Cursor = Cursors.Hand;

            ApplyTheme();
        }

        /// <summary>
        /// A thin line under the row showing a level 0..1 (on a square-root curve, so quiet sound still shows); below 0 hides it.
        /// Not in the gauge styles (Ring / Tile).
        /// </summary>
        private protected void ShowLevel(double level)
        {
            if (level < 0 || Child != _rowHost) { _level.Visibility = Visibility.Collapsed; return; }
            _level.Visibility = Visibility.Visible;
            double w = Math.Max(0, _row.ActualWidth - 16);
            _level.Width = w * Math.Sqrt(Math.Min(1, level));
        }

        /// <summary>The meter / graph as shown now (null in the Text style and the gauge styles) - Volume sets the volume where it's clicked.</summary>
        private protected FrameworkElement ShownGraph
        {
            get { return Child == _rowHost && _view.Visibility == Visibility.Visible && _view.ActualWidth > 4 ? _view : null; }
        }

        /// <summary>The module's text/graph for this moment.</summary>
        protected abstract void Read(Reading r, bool addSample);
        protected virtual void OnClick() { }
        protected virtual void OnWheel(int delta) { }
        /// <summary>Called once the theme is applied (labels can adjust their font here).</summary>
        protected virtual void OnThemed(Theme t) { }
        /// <summary>After the text is set: colour parts of it (e.g. the network arrows).</summary>
        protected virtual void Decorate(TextBlock txt) { }
        /// <summary>The theme this module is drawn with (its own font size included).</summary>
        protected Theme ModTheme;

        /// <summary>The text prefix: the configured Title, or the module's default when none was set.</summary>
        protected string Prefix(string fallback)
        {
            string p = Cfg.Title == null ? fallback : Cfg.Title;
            return p.Length == 0 ? "" : p + " ";
        }

        /// <summary>The label of a Dots / Ring / Tile look: the Text in front, or with it cleared, the module's own name ("C:") - not the
        /// reading, which was the label then (0.38.239).</summary>
        private protected string LabelOf(string fallback)
        {
            string p = Prefix(fallback).Trim();
            return p.Length > 0 ? p : fallback;
        }

        void ApplyTheme()
        {
            var t = Bar.Theme.ForModule(Cfg);
            ModTheme = t;
            if (Gauge)
            {
                if (_gauge == null) _gauge = new GaugeFace(_hist, _hist2);
                _gauge.Setup(GraphStyle, Bar, t, t.Resolve(Cfg.Color, t.MainAccent), Cfg.WarnOn ? Math.Max(0, Math.Min(100, Cfg.WarnAt)) : -1, Cfg.WarnBelow, TileMeter, Cfg.ShownGraphHeightPercent, Cfg.ShowText, Cfg.ShownTileWidth,
                              Cfg.WarnOn ? Theme.WarnFade : 0, t.Warn(Cfg.WarnColor));
                _gauge.Fill2 = t.Resolve(Cfg.Color2, t.SecondaryAccent);     // the second series (network upload), as in the Text styles' graph
                _gauge.Look(Cfg, t);
                Child = _gauge;
                OnThemed(t);
                return;
            }
            if (Dotted)
            {
                if (_dots == null) _dots = new DotFace(_hist, _hist2);
                _dots.Setup(Cfg, Bar, t, DotTile, double.NaN, TileMeter);
                Child = _dots;
                _shownKey = null;
                OnThemed(t);
                return;
            }
            Child = _rowHost;
            _level.Background = Theme.Faded(t.Fg, 75);
            bool graph = UsesGraph && GraphStyle != "Text";
            bool text = !UsesGraph || Cfg.ShowText || !graph;

            _icon.Foreground = t.MainAccent;
            _icon.FontSize = t.FontSize * 1.25;
            Txt.Foreground = t.Fg;
            Txt.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            _view.Visibility = graph ? Visibility.Visible : Visibility.Collapsed;
            _view.Margin = (text && graph) ? new Thickness(8, 0, 0, 0) : new Thickness(0);

            _view.Fill = t.Resolve(Cfg.Color, t.MainAccent);
            _view.Fill2 = t.Resolve(Cfg.Color2, t.SecondaryAccent);   // second series (network upload): Secondary Accent unless set
            ModTheme = t;
            _view.WarnFill = t.Warn(Cfg.WarnColor);
            _view.WarnAt = UsesGraph && Cfg.WarnOn ? Math.Max(0, Math.Min(100, Cfg.WarnAt)) : -1;
            _view.WarnBelow = Cfg.WarnBelow;
            _view.Blend = UsesGraph && Cfg.WarnOn ? Theme.WarnFade : 0;
            _view.Track = t.Track;
            _view.ApplyLook(Cfg, t);
            _view.GraphStyle = GraphStyle;
            _view.GraphW = Cfg.ShownGraphWidth;
            _view.GraphH = Math.Max(6, Bar.HostHeight * Cfg.ShownGraphHeightPercent / 100.0);
            _view.InvalidateMeasure();
            OnThemed(t);
        }

        /// <summary>
        /// Its widget is hidden: Update still reads (speeds come from the change since the last reading) and takes the sample, but
        /// draws nothing (WidgetWindow.UpdateStats, 0.38.224). Internal - not part of the plugin API.
        /// </summary>
        internal bool Quiet;

        /// <summary>Refresh the text and graph. addSample = false when only re-rendering (e.g. after a settings change).</summary>
        public void Update(bool addSample)
        {
            var r = new Reading();
            try { Read(r, addSample); }
            catch (Exception ex) { r.Text = "n/a"; r.Tip = ex.Message; }

            // percent and amounts don't mix in one graph: switching starts it afresh
            bool auto = UsesGraph && r.AutoScale;
            if (_hist.Amounts != auto) { _hist.Clear(); _hist2.Clear(); _hist.Amounts = _hist2.Amounts = auto; }
            if (addSample && UsesGraph) { _hist.Add(r.Percent); if (r.Percent2 >= 0) _hist2.Add(r.Percent2); }
            if (Quiet) return;
            Visibility = r.Hide ? Visibility.Collapsed : Visibility.Visible;
            double scale = 0;
            if (auto)
            {
                // as many samples as the graph shows (bars: one per 3 px, 5 with two series, a few seconds each for longer times; a line or a tile: its time shown)
                int shown = MetricView.SamplesShown(Gauge || Dotted ? "Line" : GraphStyle, Cfg.ShownGraphWidth, r.Percent2 >= 0, Math.Max(60, Cfg.GraphSpan));
                scale = Math.Max(MetricView.ScaleFor(_hist, _hist2, r.ScaleMin, shown), MetricView.ScaleFor(null, null, Math.Max(r.Percent, r.Percent2)));
            }
            GraphScale = scale;
            bool warn = UsesGraph && !auto && Theme.Crossed(r.Percent, Cfg.WarnOn, Cfg.WarnAt, Cfg.WarnBelow);
            if (Gauge)
            {
                string num = string.IsNullOrEmpty(r.Number) ? r.Percent.ToString("0", CultureInfo.CurrentCulture) : r.Number;
                _gauge.FullScale = scale;
                _gauge.Show(string.IsNullOrEmpty(r.Label) ? r.Text : r.Label, num, r.Unit, r.Detail, r.Percent, warn, null, r.Percent2);
                ToolTip = string.IsNullOrEmpty(r.Tip) ? null : r.Tip;
                return;
            }
            if (Dotted && _dots != null)
            {
                string num = string.IsNullOrEmpty(r.Number) ? r.Percent.ToString("0", CultureInfo.CurrentCulture) : r.Number;
                _dots.FullScale = scale;
                _dots.Show(string.IsNullOrEmpty(r.Label) ? r.Text : r.Label, num, r.Unit, r.Detail, warn, r.Percent, r.Percent2);
                ToolTip = string.IsNullOrEmpty(r.Tip) ? null : r.Tip;
                return;
            }
            _view.FullScale = scale;
            if (auto) _view.WarnAt = -1;
            _view.Value = r.Percent;
            _view.Value2 = r.Percent2;
            if (_view.Visibility == Visibility.Visible) _view.InvalidateVisual();
            // the text, icon and tooltip only when something in them changed (each change is a layout pass for the bar)
            string key = r.Text + "|" + r.Icon + "|" + r.Tip + "|" + warn;
            if (key == _shownKey) return;
            _shownKey = key;
            Txt.Text = r.Text;
            Decorate(Txt);
            if (UsesGraph) Txt.Foreground = warn ? (Brush)(ModTheme ?? Bar.Theme).Warn(Cfg.WarnColor) : Bar.Theme.Fg;
            bool icon = !string.IsNullOrEmpty(r.Icon);
            _icon.Text = r.Icon ?? "";
            _icon.Visibility = icon ? Visibility.Visible : Visibility.Collapsed;
            _icon.Margin = new Thickness(0, 0, Txt.Visibility == Visibility.Visible ? 6 : (_view.Visibility == Visibility.Visible ? 8 : 0), 0);
            ToolTip = string.IsNullOrEmpty(r.Tip) ? null : r.Tip;

            // never shrink: stops the module (and an auto-width bar) from twitching as the numbers change - but a placeholder ("…",
            // "n/a", "Measuring…") doesn't count: the first real value starts the width afresh
            bool placeholder = (r.Text ?? "").EndsWith("\u2026") || (r.Text ?? "").EndsWith("n/a");
            if (_wasPlaceholder && !placeholder) Txt.MinWidth = 0;
            _wasPlaceholder = placeholder;
            double keep = Txt.MinWidth;
            Txt.MinWidth = 0;
            Txt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Txt.MinWidth = Math.Max(keep, Math.Ceiling(Txt.DesiredSize.Width) + 2);
        }

        // ---- formatting helpers ------------------------------------------------------------------------
        public static string Bytes(double bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < u.Length - 1) { bytes /= 1024; i++; }
            return (bytes >= 100 || i == 0 ? bytes.ToString("0", CultureInfo.CurrentCulture) : bytes.ToString(bytes >= 10 ? "0.0" : "0.00", CultureInfo.CurrentCulture)) + " " + u[i];
        }

        public static string Speed(double bytesPerSec, bool bits)
        {
            if (bits)
            {
                double b = bytesPerSec * 8;
                if (b >= 1e9) return (b / 1e9).ToString("0.00", CultureInfo.CurrentCulture) + " Gbit/s";
                if (b >= 1e6) return (b / 1e6).ToString(b >= 1e7 ? "0.0" : "0.00", CultureInfo.CurrentCulture) + " Mbit/s";
                return (b / 1e3).ToString("0", CultureInfo.CurrentCulture) + " Kbit/s";
            }
            return Bytes(bytesPerSec) + "/s";
        }

        public static string Duration(TimeSpan t)
        {
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return Math.Max(0, t.Minutes) + "m";
        }
    }

    // =====================================================================
    // Network
    // =====================================================================
    public sealed class NetworkModule : InfoModule, IMultiRow
    {
        /// <summary>A Dots tile in a widget sizes itself (taller than a row); otherwise one row, as before.</summary>
        public int RowCount { get { return DotTile ? 0 : 1; } }
        // one sampler per adapter, shared by every network module watching it ("" = the adapters with a gateway)
        NetworkSampler _net { get { return NetworkSampler.For(Cfg.NetInterface); } }

        protected override bool UsesGraph { get { return true; } }
        protected override bool HasGauges { get { return true; } }

        public NetworkModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        /// <summary>The download arrow in the download (graph) colour, the upload arrow in the upload colour.</summary>
        protected override void Decorate(TextBlock txt)
        {
            Theme t = ModTheme ?? Bar.Theme;
            string s = txt.Text ?? "";
            if (s.IndexOf('\u2193') < 0 && s.IndexOf('\u2191') < 0) return;
            Brush down = t.Resolve(Cfg.Color, t.MainAccent), up = t.Resolve(Cfg.Color2, t.SecondaryAccent);
            txt.Inlines.Clear();
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\u2193' && s[i] != '\u2191') continue;
                if (i > start) txt.Inlines.Add(new Run(s.Substring(start, i - start)));
                txt.Inlines.Add(new Run(s[i].ToString()) { Foreground = s[i] == '\u2193' ? down : up });
                start = i + 1;
            }
            if (start < s.Length) txt.Inlines.Add(new Run(s.Substring(start)));
        }

        protected override void Read(Reading r, bool addSample)
        {
            NetSample s = _net.Sample();
            string txt;
            if (!s.Connected) txt = "No network";
            else
            {
                string d = "↓ " + Speed(s.DownBytesPerSec, Cfg.UseBits), u = "↑ " + Speed(s.UpBytesPerSec, Cfg.UseBits);
                txt = Cfg.ShownLines2 ? d + "\n" + u : d + "  " + u;
            }
            string tip = s.Connected ? s.Adapter + (s.Address.Length > 0 ? "  " + s.Address : "") + "\nDownloaded " + Bytes(s.TotalDown) + ", uploaded " + Bytes(s.TotalUp) + " since the adapter came up" : (string.IsNullOrEmpty(Cfg.NetInterface) ? "No network adapter with a gateway is connected" : Cfg.NetInterface + " isn't connected");

            string wifiText = null;
            if (Cfg.ShowWifi)
            {
                string ssid; int sig;
                Wifi.Get(out ssid, out sig);
                if (ssid.Length > 0)
                {
                    string w = ssid + (sig >= 0 ? " " + sig + "%" : "");
                    wifiText = w;
                    txt = Cfg.ShownLines2 ? w + "\n" + txt.Replace("\n", "  ") : w + "  " + txt;
                    tip = "Wi-Fi: " + ssid + (sig >= 0 ? " (" + sig + "% signal)" : "") + "\n" + tip;
                }
            }
            r.Text = Prefix("") + txt;
            double mbit = s.DownBytesPerSec * 8 / 1e6;
            double ubit = s.UpBytesPerSec * 8 / 1e6;     // upload shares the same scale, drawn in the Secondary Accent colour
            if (Cfg.AutoScale)
            {
                // the speeds themselves, in the unit shown (Mbit/s or MB/s), so the scale comes out as a round number of it
                r.AutoScale = true;
                r.ScaleMin = Cfg.UseBits ? 1 : 0.1;
                r.Percent = Cfg.UseBits ? mbit : s.DownBytesPerSec / 1048576.0;
                r.Percent2 = Cfg.UseBits ? ubit : s.UpBytesPerSec / 1048576.0;
                if (GraphScale > 0) tip += "\nGraph: up to " + GraphScale.ToString("0.##", CultureInfo.CurrentCulture) + (Cfg.UseBits ? " Mbit/s" : " MB/s");
            }
            else
            {
                r.Percent = Math.Max(0, Math.Min(100, mbit / Math.Max(1, Cfg.RangeMax) * 100.0));
                r.Percent2 = Math.Max(0, Math.Min(100, ubit / Math.Max(1, Cfg.RangeMax) * 100.0));
            }
            r.Tip = tip;
            // Ring / Tile: the download speed as the number (the ring fills with it as a share of full scale), upload under it
            r.Label = "Network";
            string dl = s.Connected ? Speed(s.DownBytesPerSec, Cfg.UseBits) : "-";
            int sp = dl.IndexOf(' ');
            r.Number = sp > 0 ? dl.Substring(0, sp) : dl;
            r.Unit = sp > 0 ? dl.Substring(sp + 1) : "";
            r.Detail = s.Connected ? "↑ " + Speed(s.UpBytesPerSec, Cfg.UseBits) : "";
            if (wifiText != null) r.Detail = wifiText + (r.Detail.Length > 0 ? "  " + r.Detail : "");     // Show the Wi-Fi name (Dots, Ring, Tile)
        }
    }

    // =====================================================================
    // Disk
    // =====================================================================
    /// <summary>Shared PDH disk counters (one set for every disk module; read at most about once a second).</summary>
    static class DiskActivity
    {
        static IntPtr _query, _busy, _read, _write;
        static bool _tried;
        static DateTime _at = DateTime.MinValue;
        public static double BusyPercent, ReadBps, WriteBps;
        public static bool Ok;

        public static void Poll()
        {
            if (!_tried)
            {
                _tried = true;
                if (Pdh.Supported)
                {
                    _query = Pdh.OpenQuery();
                    if (_query != IntPtr.Zero)
                    {
                        _busy = Pdh.AddCounter(_query, "\\PhysicalDisk(*)\\% Disk Time");
                        _read = Pdh.AddCounter(_query, "\\PhysicalDisk(*)\\Disk Read Bytes/sec");
                        _write = Pdh.AddCounter(_query, "\\PhysicalDisk(*)\\Disk Write Bytes/sec");
                        Pdh.Collect(_query);
                    }
                }
                _at = DateTime.UtcNow;
                return;
            }
            if (_query == IntPtr.Zero || _busy == IntPtr.Zero) return;
            if ((DateTime.UtcNow - _at).TotalMilliseconds < 800) return;
            _at = DateTime.UtcNow;
            if (!Pdh.Collect(_query)) return;
            bool got = false;
            foreach (var kv in Pdh.ReadArray(_busy)) if (kv.Key == "_Total") { BusyPercent = Math.Max(0, Math.Min(100, kv.Value)); got = true; }
            foreach (var kv in Pdh.ReadArray(_read)) if (kv.Key == "_Total") ReadBps = Math.Max(0, kv.Value);
            foreach (var kv in Pdh.ReadArray(_write)) if (kv.Key == "_Total") WriteBps = Math.Max(0, kv.Value);
            if (got) Ok = true;
        }
    }

    public sealed class DiskModule : InfoModule, IMultiRow
    {
        protected override bool HasGauges { get { return true; } }
        protected override bool TileMeter { get { return Cfg.DiskMode != "Activity"; } }
        public int RowCount { get { return TallTile || DotTile ? 0 : 1; } }
        long _total, _free;
        DateTime _spaceAt = DateTime.MinValue;
        bool _spaceBusy, _spaceOk;
        string _root = "";

        protected override bool UsesGraph { get { return true; } }

        public DiskModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        string Root()
        {
            string d = (Cfg.DiskDrive ?? "").Trim().TrimEnd('\\', '/', ':');
            if (d.Length == 0) return System.IO.Path.GetPathRoot(Environment.SystemDirectory);
            return d.Substring(0, 1).ToUpperInvariant() + ":\\";
        }

        void RefreshSpace(string root)
        {
            _spaceBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                long total = 0, free = 0; bool ok = false;
                try
                {
                    var di = new System.IO.DriveInfo(root);
                    if (di.IsReady) { total = di.TotalSize; free = di.AvailableFreeSpace; ok = true; }
                }
                catch { }
                System.Windows.Application app = System.Windows.Application.Current;
                if (app != null)
                    app.Dispatcher.BeginInvoke(new Action(delegate { _total = total; _free = free; _spaceOk = ok; _root = root; _spaceAt = DateTime.UtcNow; _spaceBusy = false; }));
                else _spaceBusy = false;
            });
        }

        protected override void Read(Reading r, bool addSample)
        {
            if (Cfg.DiskMode == "Activity")
            {
                DiskActivity.Poll();
                if (!DiskActivity.Ok) { r.Text = Prefix("Disk") + "n/a"; r.Tip = "Windows performance counters for the disk are not available."; return; }
                double b = DiskActivity.BusyPercent;
                string rw = "R " + Speed(DiskActivity.ReadBps, false) + "  W " + Speed(DiskActivity.WriteBps, false);
                string head = Prefix("Disk") + b.ToString("0", CultureInfo.CurrentCulture) + "%";
                r.Text = Cfg.ShownLines2 ? head + "\n" + rw : head + (Cfg.ShowDetail ? "  " + rw : "");
                r.Tip = "Disk busy time " + b.ToString("0") + "%\nRead " + Speed(DiskActivity.ReadBps, false) + ", write " + Speed(DiskActivity.WriteBps, false);
                r.Percent = b;
                r.Label = LabelOf("Disk");
                r.Detail = Cfg.ShowDetail ? rw : "";
                return;
            }

            string root = Root();
            if (!_spaceBusy && (root != _root || (DateTime.UtcNow - _spaceAt).TotalSeconds > 15)) RefreshSpace(root);
            string name = root.Substring(0, 2);
            r.Label = LabelOf(name);
            if (!_spaceOk || _total <= 0 || root != _root) { r.Text = Prefix(name) + (root != _root ? "…" : "n/a"); r.Number = root != _root ? "…" : "n/a"; r.Unit = ""; return; }
            double used = _total - _free;
            double pct = used / _total * 100.0;
            r.Percent = pct;
            string gb = (used / 1073741824.0).ToString("0", CultureInfo.CurrentCulture) + "/" + (_total / 1073741824.0).ToString("0", CultureInfo.CurrentCulture) + " GB";
            string pc = pct.ToString("0", CultureInfo.CurrentCulture) + "%";
            r.Text = Cfg.ShownLines2 ? Prefix(name) + pc + "\n" + gb : Prefix(name) + (Cfg.ShowDetail ? gb : pc);
            r.Detail = Cfg.ShowDetail ? gb : "";
            r.Tip = name + "  " + Bytes(_free) + " free of " + Bytes(_total);
        }
    }

    // =====================================================================
    // Battery
    // =====================================================================
    public sealed class BatteryModule : InfoModule, IMultiRow
    {
        protected override bool UsesGraph { get { return true; } }
        private protected override bool HasDots { get { return true; } }
        /// <summary>A Dots tile in a widget sizes itself (taller than a row); otherwise one row, as before.</summary>
        public int RowCount { get { return DotTile ? 0 : 1; } }

        public BatteryModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        protected override void Read(Reading r, bool addSample)
        {
            BatteryInfo b = Battery.Read();
            if (!b.HasBattery)
            {
                r.Hide = Cfg.HideIfNone;
                r.Text = Prefix("BAT") + "none";
                r.Tip = "This PC has no battery";
                return;
            }
            r.Percent = Math.Max(0, b.Percent);
            string state = b.Charging ? "charging" : (b.OnAc ? "plugged in" : "on battery");
            // Dots: "BAT", the percentage, and how it's doing (time left on battery)
            r.Label = LabelOf("BAT");
            r.Number = b.Percent >= 0 ? b.Percent.ToString(CultureInfo.CurrentCulture) : "?";
            r.Unit = b.Percent >= 0 ? "%" : "";
            // as the text's "+": charging, or the time left on battery (nothing while plugged in and full)
            r.Detail = b.Charging ? "charging" : !b.OnAc && b.SecondsLeft > 0 ? Duration(TimeSpan.FromSeconds(b.SecondsLeft)) + " left" : "";
            r.Text = Prefix("BAT") + (b.Percent >= 0 ? b.Percent + "%" : "?") + (b.Charging ? " +" : "");
            r.Tip = (b.Percent >= 0 ? b.Percent + "%, " : "") + state +
                    (!b.OnAc && b.SecondsLeft > 0 ? "\n" + Duration(TimeSpan.FromSeconds(b.SecondsLeft)) + " remaining" : "");
        }
    }

    // =====================================================================
    // Volume
    // =====================================================================
    public sealed class VolumeModule : InfoModule
    {
        protected override bool UsesGraph { get { return true; } }
        /// <summary>Only text or a plain meter (no bar / line graphs).</summary>
        protected override string GraphStyle { get { return Cfg.Style == "Text" ? "Text" : "Meter"; } }
        protected override bool Clickable { get { return true; } }
        protected override bool Wheelable { get { return true; } }

        readonly DispatcherTimer _levelTimer;

        public VolumeModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg)
        {
            // the sound level line: a dozen times a second while the module is on screen (not while covered or locked)
            if (cfg.VolumeLevel)
            {
                _levelTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
                _levelTimer.Tick += delegate
                {
                    if (!IsVisible || ModuleVisibility.Covered(Bar)) { ShowLevel(-1); return; }
                    float v; bool muted;
                    ShowLevel(Audio.TryGet(Dev, out v, out muted) && muted ? 0 : Audio.Peak(Dev));
                };
                Loaded += delegate { _levelTimer.Start(); };
                Unloaded += delegate { _levelTimer.Stop(); ShowLevel(-1); };
            }
            // a middle click mutes / unmutes
            MouseDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Middle || !Cfg.VolumeMiddleMute) return;
                e.Handled = true;
                ToggleMute();
            };
            // a press on the meter sets the volume there (and dragging along it follows) - instead of the click's own action. Handled in
            // the Preview events, so the module's click (and a widget drag) never starts.
            PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                FrameworkElement m = ShownGraph;
                if (m == null) return;
                Point p = e.GetPosition(m);
                if (p.X < -3 || p.X > m.ActualWidth + 3 || p.Y < -4 || p.Y > m.ActualHeight + 4) return;
                e.Handled = true;
                _meterDrag = true;
                CaptureMouse();
                FromMeter(p.X, m);
            };
            PreviewMouseMove += delegate (object s, MouseEventArgs e)
            {
                FrameworkElement m = ShownGraph;
                if (_meterDrag && m != null) FromMeter(e.GetPosition(m).X, m);
            };
            PreviewMouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!_meterDrag) return;
                e.Handled = true;
                _meterDrag = false;
                ReleaseMouseCapture();
            };
            LostMouseCapture += delegate { _meterDrag = false; };
        }

        bool _meterDrag;

        /// <summary>The volume where the meter was pressed (left end = 0 %, right end = 100 %).</summary>
        void FromMeter(double x, FrameworkElement meter)
        {
            double v = Math.Round(Math.Max(0, Math.Min(1, x / Math.Max(1, meter.ActualWidth))), 2);
            Audio.SetVolume(Dev, (float)v);
            Update(false);
        }

        /// <summary>The device this module is for ("" = the default output device).</summary>
        string Dev { get { return Cfg.VolumeDeviceId ?? ""; } }

        void ToggleMute()
        {
            float v; bool muted;
            if (!Audio.TryGet(Dev, out v, out muted)) return;
            Audio.SetMute(Dev, !muted);
            Update(false);
        }

        protected override void Read(Reading r, bool addSample)
        {
            float v; bool muted;
            Opacity = 1;
            if (!Audio.TryGet(Dev, out v, out muted))
            {
                r.Icon = Cfg.ShowIcon ? "\uE74F" : "";
                r.Text = Prefix("") + "n/a";
                r.Tip = Dev.Length == 0 ? "No audio output device"
                                        : (Cfg.VolumeDeviceName.Length > 0 ? Cfg.VolumeDeviceName : "This module's sound device") + " isn't connected";
                return;
            }
            int pct = (int)Math.Round(v * 100);
            if (Cfg.ShowIcon)
            {
                // E74F = muted speaker, E992..E995 = speaker with 0..3 sound waves
                r.Icon = muted || pct == 0 ? "\uE74F" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995";
            }
            string full = Dev.Length == 0 ? Audio.DefaultDeviceName() : Audio.DeviceName(Dev);
            string device = Cfg.VolumeDevice == "Off" ? "" : Cfg.VolumeDevice == "Short" ? Audio.ShortName(full) : full;
            string value = pct + "%";
            if (muted)
            {
                // muted: the word, only the crossed-out icon (falls back to the word without an icon), or the module faded
                if (Cfg.VolumeMuted == "Icon" && Cfg.ShowIcon) value = "";
                else if (Cfg.VolumeMuted == "Dim") Opacity = 0.45;
                else value = "muted";
            }
            r.Text = Prefix("") + (device.Length > 0 ? device + (value.Length > 0 ? " \u00B7 " + value : "") : value);
            r.Percent = muted ? 0 : pct;
            r.Tip = "Volume " + pct + "%" + (muted ? " (muted)" : "") + "\n" + full + (Dev.Length == 0 ? " (the default output device)" : "") +
                    "\n" + ClickText() + (Cfg.VolumeMiddleMute ? "   Middle-click: mute" : "") + "   Scroll: change volume";
        }

        string ClickText()
        {
            switch (Cfg.VolumeClick)
            {
                case "Mute": return "Click: mute / unmute";
                case "Settings": return "Click: sound settings";
                default: return "Click: volume mixer (each app, output device, mute)";
            }
        }

        protected override void OnWheel(int delta)
        {
            float v; bool muted;
            if (!Audio.TryGet(Dev, out v, out muted)) return;
            float step = Cfg.VolumeStep / 100f;
            Audio.SetVolume(Dev, v + (delta > 0 ? step : -step));
            Update(false);
        }

        /// <summary>The volume mixer (the device, the master volume and each app's), mute, or Windows' sound settings.</summary>
        protected override void OnClick()
        {
            switch (Cfg.VolumeClick)
            {
                case "Mute": ToggleMute(); break;
                case "Settings": SettingsLauncher.Open("ms-settings:sound"); break;
                default: VolumeMixer.Toggle(this, ModTheme ?? Bar.Theme, VolumeMixer.Options.Of(Cfg)); break;
            }
        }
    }

    // =====================================================================
    // Uptime
    // =====================================================================
    /// <summary>
    /// How long Windows has been up - counted from the last restart, as awake time, from the last wake-up or from signing in. One line, or
    /// two (the time with "since …" under it; in a widget a big number). A dot shows when Windows is waiting for a restart; a click opens
    /// the details (restart, sleep, sign-in, Fast Startup) with Restart / Sleep buttons.
    /// </summary>
    public sealed class UptimeModule : InfoModule, IMultiRow
    {
        protected override bool Clickable { get { return true; } }

        readonly TextBlock _label = new TextBlock(), _main = new TextBlock(), _since = new TextBlock();
        readonly System.Windows.Shapes.Ellipse _dot = new System.Windows.Shapes.Ellipse { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        PopupWindow _popup;
        string _shown;

        bool Tile { get { return Bar.IsWidget && Cfg.UpTwoLines; } }
        /// <summary>The widget tile sizes itself; otherwise one row.</summary>
        public int RowCount { get { return Tile ? 0 : 1; } }

        public UptimeModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg)
        {
            UptimeInfo.Watch();
        }

        /// <summary>Its own face instead of the plain text row: one line, two lines on a bar, or a tile in a widget.</summary>
        protected override void OnThemed(Theme t)
        {
            foreach (FrameworkElement e in new FrameworkElement[] { _label, _main, _since, _dot })
            {
                var p = e.Parent as Panel;
                if (p != null) p.Children.Remove(e);
            }
            foreach (TextBlock tb in new[] { _label, _main, _since })
            {
                tb.FontFamily = t.Font;
                tb.VerticalAlignment = VerticalAlignment.Center;
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            _main.Foreground = t.Fg;
            _label.Foreground = _since.Foreground = t.FgDim;
            _dot.Fill = t.Warn(Cfg.WarnColor);
            _main.FontWeight = FontWeights.Normal;
            _main.MinWidth = 0;

            var top = new StackPanel { Orientation = Orientation.Horizontal };
            double h = Math.Max(16, Bar.HostHeight - 2);
            if (Tile)
            {
                // a widget tile: "Up" (dim) and the dot, the time big, "since …" under it
                _label.FontSize = Math.Max(7, t.FontSize * 0.92);
                _main.FontSize = t.FontSize * 1.75;
                _main.FontWeight = FontWeights.Bold;
                _since.FontSize = Math.Max(7, t.FontSize * 0.85);
                top.Children.Add(_label);
                top.Children.Add(_dot);
                var tile = new StackPanel { Margin = new Thickness(6, 4, 6, 4), VerticalAlignment = VerticalAlignment.Top };
                tile.Children.Add(top);
                tile.Children.Add(_main);
                tile.Children.Add(_since);
                Child = tile;
            }
            else if (Cfg.UpTwoLines)
            {
                // a bar: the time over "since …", scaled to fit the bar's height
                _main.FontSize = Math.Max(7, Math.Min(t.FontSize * 0.95, h * 0.38));
                _since.FontSize = Math.Max(7, Math.Min(t.FontSize * 0.78, h * 0.3));
                top.Children.Add(_main);
                top.Children.Add(_dot);
                var lines = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                lines.Children.Add(top);
                lines.Children.Add(_since);
                Child = lines;
            }
            else
            {
                _main.FontSize = t.FontSize;
                top.Margin = new Thickness(8, 0, 8, 0);
                top.VerticalAlignment = VerticalAlignment.Center;
                top.Children.Add(_main);
                top.Children.Add(_dot);
                Child = top;
            }
            double d = Math.Max(5, Math.Round((Tile ? _label.FontSize : _main.FontSize) * 0.42));
            _dot.Width = _dot.Height = d;
            _dot.Margin = new Thickness(Math.Round(d * 0.9), 0, 0, 0);
            _shown = null;
        }

        /// <summary>"since today 5:36 PM" - Today / Yesterday in lower case after "since".</summary>
        static string When(DateTime t)
        {
            string day = TimeText.Day(t);
            if (day == "Today" || day == "Yesterday") day = day.ToLowerInvariant();
            return day + " " + TimeText.Clock(t);
        }

        protected override void Read(Reading r, bool addSample)
        {
            DateTime since;
            TimeSpan counted = UptimeInfo.Counted(Cfg.UpFrom, out since);
            string time = UptimeInfo.Format(counted, Cfg.UpFormat, Cfg.UpMinutes);
            string pending = UptimeInfo.RestartPending;
            string sinceLine;
            switch (Cfg.UpFrom)
            {
                case "Awake time": sinceLine = "awake, of " + UptimeInfo.Format(UptimeInfo.Up, "Short", false) + " up"; break;
                case "Last wake-up": sinceLine = "awake since " + When(since); break;
                case "Sign-in": sinceLine = "signed in " + When(since); break;
                default: sinceLine = "since " + When(since); break;
            }

            r.Text = Prefix("Up") + time;
            r.Tip = Tip(pending);

            string key = r.Text + "|" + sinceLine + "|" + (pending != null && Cfg.UpPendingDot);
            if (key == _shown) return;
            _shown = key;
            string label = Cfg.Title == null ? "Up" : Cfg.Title;
            _label.Text = label;
            _label.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _main.Text = Tile ? time : r.Text;
            _since.Text = sinceLine;
            _dot.Visibility = pending != null && Cfg.UpPendingDot ? Visibility.Visible : Visibility.Collapsed;
            if (!Tile)
            {
                // never narrower than it has been: the modules beside it don't shuffle as the minutes tick over
                double keep = _main.MinWidth;
                _main.MinWidth = 0;
                _main.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _main.MinWidth = Math.Max(keep, Math.Ceiling(_main.DesiredSize.Width) + 1);
            }
        }

        string Tip(string pending)
        {
            TimeSpan up = UptimeInfo.Up, awake = UptimeInfo.Awake;
            var sb = new System.Text.StringBuilder();
            sb.Append("Windows has been running for ").Append(UptimeInfo.Format(up, "Long", true));
            sb.Append("\nLast restart: ").Append(TimeText.Stamp(UptimeInfo.Booted));
            if (awake < up - TimeSpan.FromMinutes(1)) sb.Append("\nAwake for ").Append(UptimeInfo.Format(awake, "Long", false)).Append(" of it");
            DateTime woke = UptimeInfo.LastWake;
            if (woke != DateTime.MinValue) sb.Append("\nWoke up: ").Append(TimeText.Stamp(woke));
            if (pending != null) sb.Append("\nRestart needed: ").Append(pending);
            sb.Append("\nClick for details");
            return sb.ToString();
        }

        protected override void OnClick()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (PopupWindow.ClosedJustNow(this)) { _popup = null; return; }     // this click's press already closed it
            Theme t = ModTheme ?? Bar.Theme;
            _popup = new PopupWindow(Mgr, Bar, this, t, BuildPopup(t));
            if (Mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(t, Mgr.Settings.WidgetStyle);
            Mgr.ShowPopup(_popup);
        }

        /// <summary>The details: when it restarted, slept, woke and you signed in, awake time, Fast Startup, a restart waiting; Restart / Sleep.</summary>
        FrameworkElement BuildPopup(Theme t)
        {
            TimeSpan up = UptimeInfo.Up, awake = UptimeInfo.Awake;
            string pending = UptimeInfo.RestartPending;
            var p = new StackPanel { Margin = new Thickness(6), MinWidth = 290, MaxWidth = 380 };
            p.Children.Add(new TextBlock
            {
                Text = "Up " + UptimeInfo.Format(up, "Long", true), FontSize = t.FontSize * 1.15, FontWeight = FontWeights.SemiBold,
                Foreground = t.Fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Action<string, string, Brush> add = delegate (string name, string value, Brush fg)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var n = new TextBlock { Text = name, Foreground = t.FgDim, FontSize = t.FontSize * 0.9, Margin = new Thickness(0, 2, 14, 2) };
                var v = new TextBlock { Text = value, Foreground = fg ?? t.Fg, FontSize = t.FontSize * 0.9, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(n, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
                grid.Children.Add(n); grid.Children.Add(v);
            };
            add("Last restart", TimeText.Full(UptimeInfo.Booted), null);
            int share = up.TotalSeconds > 0 ? (int)Math.Round(100 * awake.TotalSeconds / up.TotalSeconds) : 100;
            add("Awake", UptimeInfo.Format(awake, "Long", false) + " (" + Math.Min(100, share) + "%)", null);
            DateTime slept = UptimeInfo.LastSleep, woke = UptimeInfo.LastWake;
            add("Last sleep", slept != DateTime.MinValue ? TimeText.Stamp(slept) : "not since it started", null);
            if (woke != DateTime.MinValue) add("Woke up", TimeText.Stamp(woke) + " (" + UptimeInfo.Format(DateTime.Now - woke, "Short", false) + " ago)", null);
            DateTime signed = UptimeInfo.SignedIn;
            if (signed != DateTime.MinValue) add("Signed in", TimeText.Stamp(signed), null);
            add("Fast Startup", UptimeInfo.FastStartup ? "On - shutting down doesn't reset the uptime, only Restart does" : "Off - shutting down and starting again resets it", null);
            if (pending != null) add("Restart needed", pending, t.Warn(Cfg.WarnColor));
            p.Children.Add(grid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(Pills.Make(t, "", "Sleep", "Put the PC to sleep", false, delegate
            {
                if (_popup != null) _popup.CloseSafe();
                PowerActions.Sleep();
            }));
            buttons.Children.Add(Pills.Make(t, "", "Restart", "Restart Windows", pending != null, delegate
            {
                if (_popup != null) _popup.CloseSafe();
                if (Questions.YesNo(null, "Restart Windows now? Save anything you're working on first.", "Restart") == MessageBoxResult.Yes)
                    PowerActions.Restart();
            }));
            p.Children.Add(buttons);
            return p;
        }
    }

    // =====================================================================
    // Top processes
    // =====================================================================
    public sealed class ProcessesModule : InfoModule, IMultiRow
    {
        protected override bool Clickable { get { return true; } }

        /// <summary>List layout with more than one program: one program per line, on a bar or in a widget.</summary>
        bool Stacked { get { return Cfg.TileLayout == "List" && ShowCount > 1; } }
        bool OnBar { get { return !Bar.IsWidget; } }
        double _spacing;
        int _fitShow = -1;

        /// <summary>How many programs to show: on a bar, a list only shows as many lines as fit its height (line spacing goes first).</summary>
        int ShowCount { get { return _fitShow > 0 ? _fitShow : Math.Max(1, Math.Min(5, Cfg.ProcShow)); } }

        /// <summary>A list on a bar: fit the lines into the bar - first by closing up the line spacing, then by showing fewer lines.</summary>
        void FitLines(double fontSize)
        {
            _fitShow = -1;
            _spacing = Cfg.ProcSpacing;
            int n = Math.Max(1, Math.Min(5, Cfg.ProcShow));
            if (!OnBar || Cfg.TileLayout != "List" || n < 2) return;
            double line = fontSize * 1.25, room = Math.Max(10, Bar.HostHeight - 2);
            if (n * line + (n - 1) * _spacing <= room) return;
            _spacing = Math.Max(0, (room - n * line) / (n - 1));
            if (n * line <= room) return;
            _fitShow = Math.Max(1, (int)Math.Floor(room / line));
        }

        /// <summary>In a widget a stacked list sizes itself (0); otherwise it is one row.</summary>
        public int RowCount { get { return Stacked ? 0 : 1; } }

        public ProcessesModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        protected override void OnThemed(Theme t)
        {
            // the list has its own text size and line spacing (widgets); on a bar the size is the module's S / M / L
            Txt.FontSize = Math.Max(6, OnBar ? t.FontSize : t.FontSize * Cfg.ProcScale / 100.0);
            FitLines(Txt.FontSize);
            if (Stacked)
            {
                Txt.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                Txt.LineHeight = Txt.FontSize * 1.25 + _spacing;
            }
            // a fixed width: the program names are shortened (Read) so the module doesn't grow and shrink as they change;
            // the CPU % / memory always stays whole
            if (Cfg.ProcWidth > 0)
            {
                Txt.Width = Cfg.ProcWidth;
                Txt.TextTrimming = TextTrimming.None;
                Txt.TextAlignment = TextAlignment.Left;
            }
        }

        protected override void Read(Reading r, bool addSample)
        {
            List<ProcInfo> all = new List<ProcInfo>(ProcessSampler.Latest());
            bool byMem = Cfg.ProcSort == "Memory", byGpu = Cfg.ProcSort == "Gpu";
            all.Sort(delegate (ProcInfo a, ProcInfo b)
            {
                if (byGpu) { int c = b.Gpu.CompareTo(a.Gpu); return c != 0 ? c : b.Cpu.CompareTo(a.Cpu); }
                return byMem ? b.MemMb.CompareTo(a.MemMb) : b.Cpu.CompareTo(a.Cpu);
            });
            if (byGpu && Mgr.Gpu.Unavailable) { r.Text = Prefix("Top") + "GPU n/a"; r.Tip = "GPU use per program can't be read on this PC."; return; }
            // just switched to GPU: the latest sample has no GPU numbers yet (a few seconds) - don't show the CPU order as "0.0%"
            if (all.Count == 0 || (byGpu && !ProcessSampler.HasGpu)) { r.Text = Prefix("Top") + "\u2026"; r.Tip = "Measuring\u2026"; return; }

            int show = ShowCount;
            string prefix = Prefix("Top");
            var names = new List<string>();
            var values = new List<string>();
            for (int i = 0; i < show && i < all.Count; i++)
            {
                ProcInfo p = all[i];
                names.Add(p.Name);
                values.Add(byMem ? Bytes(p.MemMb * 1048576.0)
                    : (byGpu ? p.Gpu : p.Cpu).ToString("0.0", CultureInfo.CurrentCulture) + "%");
            }
            if (Cfg.ProcWidth > 0 && names.Count > 0) FitNames(names, values, Stacked ? "" : prefix);
            var parts = new List<string>();
            for (int i = 0; i < show; i++) parts.Add(i < names.Count ? names[i] + " " + values[i] : "");
            if (Stacked) r.Text = string.Join("\n", parts);              // list: no prefix, the rows speak for themselves
            else r.Text = prefix + string.Join("   ", parts.FindAll(delegate (string x) { return x.Length > 0; }));

            var sb = new System.Text.StringBuilder(byMem ? "Top programs by memory" : byGpu ? "Top programs by GPU" : "Top programs by CPU");
            sb.Append("   (click: Task Manager)");
            for (int i = 0; i < Math.Min(Cfg.ProcCount, all.Count); i++)
            {
                ProcInfo p = all[i];
                sb.Append('\n').Append(p.Name);
                if (p.Count > 1) sb.Append(" (").Append(p.Count).Append(')');
                sb.Append("   ").Append(p.Cpu.ToString("0.0", CultureInfo.CurrentCulture)).Append("%   ").Append(Bytes(p.MemMb * 1048576.0));
                if (byGpu) sb.Append("   GPU ").Append(p.Gpu.ToString("0.0", CultureInfo.CurrentCulture)).Append('%');
            }
            r.Tip = sb.ToString();
        }

        /// <summary>
        /// Fixed width: shorten the program names (with "…") so everything fits in ProcWidth - the values are never cut. On one line
        /// the room left after the prefix, the values and the gaps is shared between the names; in a list each line gets the full width.
        /// </summary>
        void FitNames(List<string> names, List<string> values, string prefix)
        {
            double width = Cfg.ProcWidth - 2;       // a little slack for rounding
            double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            var face = new Typeface(Txt.FontFamily, Txt.FontStyle, Txt.FontWeight, Txt.FontStretch);
            Func<string, double> W = delegate (string s)
            {
                if (string.IsNullOrEmpty(s)) return 0;
                return new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Txt.FontSize, Brushes.Black, dip).WidthIncludingTrailingWhitespace;
            };
            if (Stacked)
            {
                for (int i = 0; i < names.Count; i++) names[i] = Shorten(names[i], width - W(" " + values[i]), W);
                return;
            }
            double fixedW = W(prefix) + W("   ") * (names.Count - 1);
            for (int i = 0; i < values.Count; i++) fixedW += W(" " + values[i]);
            double room = width - fixedW;
            // names that fit in an equal share keep their room for the others
            var fits = new bool[names.Count];
            int left = names.Count;
            bool changed = true;
            while (changed && left > 0)
            {
                changed = false;
                double share = room / left;
                for (int i = 0; i < names.Count; i++)
                {
                    if (fits[i]) continue;
                    double w = W(names[i]);
                    if (w <= share) { fits[i] = true; room -= w; left--; changed = true; }
                }
            }
            if (left == 0) return;
            double each = room / left;
            for (int i = 0; i < names.Count; i++) if (!fits[i]) names[i] = Shorten(names[i], each, W);
        }

        /// <summary>The name cut down (ending in "…") to fit max; at least its first letter and the "…".</summary>
        static string Shorten(string name, double max, Func<string, double> W)
        {
            if (W(name) <= max) return name;
            for (int n = name.Length - 1; n > 1; n--)
            {
                string s = name.Substring(0, n).TrimEnd() + "\u2026";
                if (W(s) <= max) return s;
            }
            return name.Substring(0, 1) + "\u2026";
        }

        protected override void OnClick()
        {
            try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Task Manager: " + ex.Message); }
        }
    }

    // =====================================================================
    // Text label
    // =====================================================================
    public sealed class LabelModule : InfoModule
    {
        public LabelModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        protected override void OnThemed(Theme t)
        {
            Txt.FontSize = Math.Max(6, t.FontSize);     // Build 2: S / M / L sets the size
            Txt.FontWeight = Cfg.LabelBold ? FontWeights.SemiBold : FontWeights.Normal;
            Txt.Foreground = t.Resolve(Cfg.Color, t.Fg);
        }

        protected override void Read(Reading r, bool addSample)
        {
            r.Text = Cfg.LabelText ?? "";
            if (r.Text.Length == 0) r.Text = " ";
        }
    }
}

namespace vShell.Ui
{
    /// <summary>One place that knows which class implements which new module type.</summary>
    public static class ModuleFactory
    {
        public static InfoModule CreateInfo(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            switch (cfg.Type)
            {
                case ModuleTypes.Network: return new NetworkModule(mgr, host, cfg);
                case ModuleTypes.Disk: return new DiskModule(mgr, host, cfg);
                case ModuleTypes.Battery: return new BatteryModule(mgr, host, cfg);
                case ModuleTypes.Volume: return new VolumeModule(mgr, host, cfg);
                case ModuleTypes.Uptime: return new UptimeModule(mgr, host, cfg);
                case ModuleTypes.Processes: return new ProcessesModule(mgr, host, cfg);
                default: return new LabelModule(mgr, host, cfg);
            }
        }

        public static TilesModule CreateTiles(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            switch (cfg.Type)
            {
                default: return new ServicesModule(mgr, host, cfg);
            }
        }
    }
}
