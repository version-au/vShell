using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Climate control: a Home Assistant climate entity (the air conditioner) and its zones (fan entities, 0 - 10), drawn the way
    /// Bubble Card does it - a big rounded "bubble" with the mode's colour, the icon in a circle, − / target / + on the right, little
    /// rounded drop-down buttons for the mode and fan speed under it, and a rounded slider for each zone.
    /// On a bar: the mode icon and the room temperature (and the target) - click for the card, scroll to change the target.
    /// In a widget: the card itself.
    /// </summary>
    public sealed class ClimateModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        readonly ClimateCtl _ctl;
        PopupWindow _popup;
        string _shown;

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }

        public ClimateModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            _ctl = new ClimateCtl(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            if (host.IsWidget)
            {
                Padding = new Thickness(2);
                Child = new ClimateCard(_t, cfg, _ctl, 0);
                return;
            }
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            MouseWheel += delegate (object s, MouseWheelEventArgs e) { e.Handled = true; _ctl.Nudge(e.Delta > 0 ? 1 : -1); Refresh(); };
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { _timer.Start(); Refresh(); };
            // (a widget rebuilding can send Unloaded after Loaded: stop only when it's really gone)
            Unloaded += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { if (!IsLoaded) _timer.Stop(); })); };
            IsVisibleChanged += delegate { if (IsVisible && !_timer.IsEnabled) _timer.Start(); };
            Refresh();
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (string.IsNullOrWhiteSpace(HaView.Of(_cfg).ClimateEntity)) { _host.OpenSettings(); return; }
            _popup = new PopupWindow(_mgr, _host, this, _t, new ClimateCard(_t, _cfg, _ctl, 360));
            if (_mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _mgr.ShowPopup(_popup);
        }

        void Refresh()
        {
            HaState st = _ctl.Read();
            string key = (st == null ? "none|" + HaClient.Configured : _ctl.Signature(st) + "|" + HaView.Of(_cfg).ShowTarget) + "|" + _cfg.Title;
            if (key == _shown) return;
            _shown = key;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, TextBlock> T = delegate (string text, Brush b)
            {
                return new TextBlock { Text = text, Foreground = b, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            };
            if (!HaClient.Configured) { row.Children.Add(T("Set up Home Assistant in Settings > Plugins", _t.FgDim)); Child = row; ToolTip = null; _host.ContentChanged(); return; }
            if (string.IsNullOrWhiteSpace(HaView.Of(_cfg).ClimateEntity)) { row.Children.Add(T("Pick a climate entity in Settings", _t.FgDim)); Child = row; ToolTip = null; _host.ContentChanged(); return; }
            if (st == null || st.Unavailable)
            {
                row.Children.Add(ClimateIcons.Circle("thermo", _t.FgDim, Theme.Faded(_t.FgDim, 18), _t.FontSize * 1.55));
                row.Children.Add(T(st == null ? "  …" : "  n/a", _t.FgDim));
                Child = row; ToolTip = HaView.Of(_cfg).ClimateEntity + (st == null ? " - asking Home Assistant…" : " is unavailable"); _host.ContentChanged(); return;
            }
            if (!string.IsNullOrEmpty(_cfg.Title)) row.Children.Add(T(_cfg.Title + "  ", _t.FgDim));
            string mode = st.State;
            Brush col = ClimateCtl.ModeBrush(mode, _t);
            row.Children.Add(ClimateIcons.Circle(ClimateCtl.ModeIcon(mode), col, mode == "off" ? Theme.Faded(_t.FgDim, 18) : Theme.Faded((SolidColorBrush)col, 26), _t.FontSize * 1.55));
            double cur;
            string now = st.AttrNum("current_temperature", out cur) ? ClimateCtl.Deg(cur) : "--°";
            double target = _ctl.Target(st);
            bool showTarget = HaView.Of(_cfg).ShowTarget && mode != "off" && !double.IsNaN(target);
            if (_cfg.ShownTwoLines == true && showTarget)
            {
                // M on a bar: the room temperature over the target
                var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                lines.Children.Add(T("  " + now, _t.Fg));
                lines.Children.Add(T("  → " + ClimateCtl.Deg(target), _t.FgDim));
                row.Children.Add(lines);
            }
            else
            {
                row.Children.Add(T("  " + now, _t.Fg));
                if (showTarget) row.Children.Add(T("  → " + ClimateCtl.Deg(target), _t.FgDim));
            }
            Child = row;
            var tip = new List<string> { (st.Name.Length > 0 ? st.Name : HaView.Of(_cfg).ClimateEntity) + " - " + ClimateCtl.ModeName(mode) + ClimateCtl.ActionText(st) };
            tip.Add("Room " + now + (double.IsNaN(target) ? "" : " · set to " + ClimateCtl.Deg(target)));
            string fan = st.AttrText("fan_mode");
            if (fan.Length > 0) tip.Add("Fan " + ClimateCtl.Nice(fan));
            foreach (ClimateZone z in HaView.Of(_cfg).Zones)
            {
                HaState zs = HaClient.Get(z.Entity);
                tip.Add("  " + ClimateCtl.ZoneName(z, zs) + ": " + ClimateCtl.ZoneText(_ctl.ZoneLevel(z, zs)));
            }
            tip.Add("Click for the controls · scroll to change the temperature");
            ToolTip = string.Join("\n", tip);
            _host.ContentChanged();
        }
    }

    /// <summary>
    /// The talking-to-Home-Assistant part, shared by the bar pill and its card: what the entity reports, with what you just changed
    /// laid over it until Home Assistant catches up (changes are sent a moment after the last click, so several + clicks are one call).
    /// </summary>
    public sealed class ClimateCtl
    {
        readonly ModuleConfig _cfg;
        double _pendTarget = double.NaN;
        DateTime _pendAt;
        DispatcherTimer _send;
        readonly Dictionary<string, KeyValuePair<int, DateTime>> _pendZone = new Dictionary<string, KeyValuePair<int, DateTime>>();
        readonly Dictionary<string, int> _lastOn = new Dictionary<string, int>();
        string _pendMode;
        DateTime _pendModeAt;
        string _lastMode = "";

        public ClimateCtl(ModuleConfig cfg) { _cfg = cfg; }

        public string Entity { get { return (HaView.Of(_cfg).ClimateEntity ?? "").Trim(); } }

        public HaState Read()
        {
            if (Entity.Length == 0) return null;
            HaState st = HaClient.Get(Entity);
            if (st != null && !st.Unavailable && st.State != "off") _lastMode = st.State;
            return st;
        }

        public string Signature(HaState st)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(st.State).Append('|').Append(Mode(st)).Append('|').Append(st.AttrText("current_temperature")).Append('|').Append(Target(st))
              .Append('|').Append(st.AttrText("fan_mode")).Append('|').Append(st.AttrText("hvac_action")).Append('|').Append(st.Name);
            foreach (ClimateZone z in HaView.Of(_cfg).Zones)
            {
                HaState zs = HaClient.Get(z.Entity);
                sb.Append('|').Append(z.Name).Append(':').Append(ZoneLevel(z, zs));
            }
            return sb.ToString();
        }

        // ---- the air conditioner ----
        /// <summary>The mode shown: the one just picked, until Home Assistant reports it (or 8 s).</summary>
        public string Mode(HaState st)
        {
            if (_pendMode != null)
            {
                if (st != null && st.State == _pendMode || (DateTime.UtcNow - _pendModeAt).TotalSeconds > 8) _pendMode = null;
                else return _pendMode;
            }
            return st != null ? st.State : "";
        }

        public double Step(HaState st)
        {
            double s;
            return st != null && st.AttrNum("target_temp_step", out s) && s > 0 ? s : 0.5;
        }

        public double Target(HaState st)
        {
            if (!double.IsNaN(_pendTarget))
            {
                double rep;
                bool caught = st != null && st.AttrNum("temperature", out rep) && Math.Abs(rep - _pendTarget) < 0.01;
                if ((caught && (DateTime.UtcNow - _pendAt).TotalSeconds > 1.5) || (DateTime.UtcNow - _pendAt).TotalSeconds > 10) _pendTarget = double.NaN;
                else return _pendTarget;
            }
            double t;
            return st != null && st.AttrNum("temperature", out t) ? t : double.NaN;
        }

        /// <summary>+ / − (steps of the entity's target_temp_step), kept between its min_temp and max_temp; sent 0.7 s after the last one.</summary>
        public void Nudge(int dir)
        {
            HaState st = Read();
            if (st == null || st.Unavailable || st.State == "off") return;
            double cur = Target(st);
            if (double.IsNaN(cur)) return;
            double step = Step(st), lo, hi;
            if (!st.AttrNum("min_temp", out lo)) lo = 16;
            if (!st.AttrNum("max_temp", out hi)) hi = 30;
            double v = Math.Round((cur + dir * step) / step) * step;
            _pendTarget = Math.Max(lo, Math.Min(hi, v));
            _pendAt = DateTime.UtcNow;
            if (_send == null)
            {
                _send = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _send.Tick += delegate
                {
                    _send.Stop();
                    if (double.IsNaN(_pendTarget)) return;
                    _pendAt = DateTime.UtcNow;
                    HaClient.CallService("climate", "set_temperature", Entity, new Dictionary<string, object> { { "temperature", _pendTarget } });
                };
            }
            _send.Stop();
            _send.Start();
        }

        public void SetMode(string mode)
        {
            _pendMode = mode; _pendModeAt = DateTime.UtcNow;
            HaClient.CallService("climate", "set_hvac_mode", Entity, new Dictionary<string, object> { { "hvac_mode", mode } });
        }

        /// <summary>On = back to the last mode it was in (or the first one it has); off = "off".</summary>
        public void Power(HaState st)
        {
            if (st == null) return;
            if (Mode(st) != "off") { SetMode("off"); return; }
            List<string> modes = st.AttrList("hvac_modes");
            string m = _lastMode.Length > 0 && _lastMode != "off" ? _lastMode : null;
            if (m == null) foreach (string x in modes) if (x != "off") { m = x; break; }
            if (m == null) { HaClient.CallService("climate", "turn_on", Entity); return; }
            SetMode(m);
        }

        public void SetFan(string fan) { HaClient.CallService("climate", "set_fan_mode", Entity, new Dictionary<string, object> { { "fan_mode", fan } }); }
        public void SetPreset(string p) { HaClient.CallService("climate", "set_preset_mode", Entity, new Dictionary<string, object> { { "preset_mode", p } }); }

        // ---- zones (fan entities, 0 - 10) ----
        /// <summary>A zone's level 0 - 10 (the one just set until Home Assistant reports it), -1 = not known yet.</summary>
        public int ZoneLevel(ClimateZone z, HaState zs)
        {
            KeyValuePair<int, DateTime> p;
            if (_pendZone.TryGetValue(z.Entity, out p))
            {
                int rep = Reported(zs);
                if ((rep == p.Key && (DateTime.UtcNow - p.Value).TotalSeconds > 1.5) || (DateTime.UtcNow - p.Value).TotalSeconds > 10) _pendZone.Remove(z.Entity);
                else return p.Key;
            }
            int r = Reported(zs);
            if (r > 0) _lastOn[z.Entity] = r;
            return r;
        }

        static int Reported(HaState zs)
        {
            if (zs == null || zs.Unavailable) return -1;
            if (zs.State == "off") return 0;
            double pc;
            if (zs.AttrNum("percentage", out pc)) return Math.Max(0, Math.Min(10, (int)Math.Round(pc / 10.0)));
            return zs.State == "on" ? 10 : 0;
        }

        /// <summary>0 = off; 1 - 10 = that many tenths open (fan.set_percentage).</summary>
        public void SetZone(ClimateZone z, int level)
        {
            level = Math.Max(0, Math.Min(10, level));
            _pendZone[z.Entity] = new KeyValuePair<int, DateTime>(level, DateTime.UtcNow);
            if (level == 0) HaClient.CallService("fan", "turn_off", z.Entity);
            else
            {
                _lastOn[z.Entity] = level;
                var pct = new Dictionary<string, object> { { "percentage", level * 10 } };
                HaState zs = HaClient.Get(z.Entity);
                if (zs != null && zs.State == "off")
                {
                    // an off zone: turn it on at that opening (some integrations ignore set_percentage while off), then set it again to be sure
                    HaClient.CallService("fan", "turn_on", z.Entity, pct);
                    string id = z.Entity;
                    System.Threading.Tasks.Task.Delay(800).ContinueWith(delegate { HaClient.CallService("fan", "set_percentage", id, pct); });
                }
                else HaClient.CallService("fan", "set_percentage", z.Entity, pct);
            }
        }

        /// <summary>The icon: off ↔ back to where it was (or half open).</summary>
        public void ToggleZone(ClimateZone z, HaState zs)
        {
            int cur = ZoneLevel(z, zs);
            if (cur > 0) { SetZone(z, 0); return; }
            int back;
            SetZone(z, _lastOn.TryGetValue(z.Entity, out back) && back > 0 ? back : 5);
        }

        // ---- words, colours and icons ----
        public static string ZoneName(ClimateZone z, HaState zs)
        {
            if (z.Name.Length > 0) return z.Name;
            if (zs != null && zs.Name.Length > 0) return zs.Name;
            return z.Entity;
        }

        public static string ZoneText(int level) { return level < 0 ? "…" : (level * 10) + "%"; }

        public static string Deg(double v) { return v.ToString("0.#", CultureInfo.CurrentCulture) + "°"; }

        public static string Nice(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('_', ' ');
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        public static string ModeName(string mode)
        {
            switch (mode)
            {
                case "off": return "Off";
                case "cool": return "Cool";
                case "heat": return "Heat";
                case "dry": return "Dry";
                case "fan_only": return "Fan";
                case "auto": return "Auto";
                case "heat_cool": return "Heat / cool";
                default: return Nice(mode);
            }
        }

        public static string ModeIcon(string mode)
        {
            switch (mode)
            {
                case "cool": return "cool";
                case "heat": return "heat";
                case "dry": return "dry";
                case "fan_only": return "fan";
                case "auto": case "heat_cool": return "auto";
                case "off": return "power";
                default: return "thermo";
            }
        }

        static readonly SolidColorBrush CoolC = Fz(0x6C, 0xB6, 0xFF), HeatC = Fz(0xFF, 0x9F, 0x43), DryC = Fz(0xF4, 0xCD, 0x52),
                                        FanC = Fz(0x7E, 0xD9, 0xC3), AutoC = Fz(0xB4, 0x8E, 0xF5);
        static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        public static SolidColorBrush ModeBrush(string mode, Theme t)
        {
            switch (mode)
            {
                case "cool": return CoolC;
                case "heat": return HeatC;
                case "dry": return DryC;
                case "fan_only": return FanC;
                case "auto": case "heat_cool": return AutoC;
                case "off": return t.FgDim;
                default: return t.MainAccent;
            }
        }

        /// <summary>" · Cooling" etc. from hvac_action ("" when it says nothing useful).</summary>
        public static string ActionText(HaState st)
        {
            string a = st.AttrText("hvac_action");
            switch (a)
            {
                case "cooling": return " · Cooling";
                case "heating": return " · Heating";
                case "drying": return " · Drying";
                case "fan": return " · Fan";
                case "idle": return " · Idle";
                default: return "";
            }
        }
    }

    /// <summary>
    /// The card (popup and widget), as Bubble Card's climate card: the row with the state colour behind it while it heats / cools, the
    /// round icon (click: on / off), name and state, and − 24° + in a pill on the right; under it Bubble sub-buttons for the mode, fan
    /// speed and preset; then a Bubble slider for each zone. Updates itself every second.
    /// </summary>
    public sealed class ClimateCard : Border
    {
        readonly Theme _t;
        readonly ModuleConfig _cfg;
        readonly ClimateCtl _ctl;
        readonly DispatcherTimer _timer;
        readonly StackPanel _root = new StackPanel();
        readonly BubbleRow _row;
        readonly TextBlock _target = new TextBlock();
        readonly Border _tempPill;
        readonly WrapPanel _chips = new WrapPanel();
        readonly System.Windows.Controls.Primitives.UniformGrid _zones = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        readonly TextBlock _msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 0) };
        readonly List<ZoneSlider> _sliders = new List<ZoneSlider>();
        string _chipKey, _zoneKey;
        readonly double _k;

        public ClimateCard(Theme t, ModuleConfig cfg, ClimateCtl ctl, double width)
        {
            _t = t; _cfg = cfg; _ctl = ctl;
            if (width > 0) Width = width;
            Child = _root;
            Background = Brushes.Transparent;

            _row = new BubbleRow(t);
            _k = _row.K;
            _row.OnIconClick(delegate { _ctl.Power(_ctl.Read()); Update(); });
            // − 24° + in one pill, as Bubble's .bubble-temperature-container (36 px, the card colour)
            var temp = new StackPanel { Orientation = Orientation.Horizontal };
            temp.Children.Add(BubbleRow.StepButton(t, _k, "−", delegate { _ctl.Nudge(-1); Update(); }));
            _target.FontFamily = t.Font; _target.FontSize = 12 * _k * 1.1; _target.FontWeight = FontWeights.SemiBold; _target.Foreground = t.Fg;
            _target.VerticalAlignment = VerticalAlignment.Center; _target.TextAlignment = TextAlignment.Center; _target.MinWidth = 34 * _k; _target.Padding = new Thickness(4 * _k, 0, 4 * _k, 0);
            temp.Children.Add(_target);
            temp.Children.Add(BubbleRow.StepButton(t, _k, "+", delegate { _ctl.Nudge(1); Update(); }));
            double ph = Math.Round(36 * _k);
            _tempPill = new Border { Child = temp, Height = ph, CornerRadius = new CornerRadius(ph / 2), Background = BubbleRow.CardBrush(t), VerticalAlignment = VerticalAlignment.Center };
            _row.SetRight(_tempPill);
            _row.MouseWheel += delegate (object s, MouseWheelEventArgs e) { e.Handled = true; _ctl.Nudge(e.Delta > 0 ? 1 : -1); Update(); };
            _root.Children.Add(_row);
            _chips.Margin = new Thickness(0, 8 * _k, 0, 0);
            _root.Children.Add(_chips);
            _root.Children.Add(_zones);
            _msg.FontFamily = t.Font; _msg.FontSize = 11.5 * _k; _msg.Foreground = t.FgDim;
            _root.Children.Add(_msg);

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Update(); };
            Loaded += delegate { _timer.Start(); Update(); };
            // (a widget rebuilding can send Unloaded after Loaded: stop only when it's really gone)
            Unloaded += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { if (!IsLoaded) _timer.Stop(); })); };
            IsVisibleChanged += delegate { if (IsVisible && !_timer.IsEnabled) _timer.Start(); };
            Update();
        }

        /// <summary>A Bubble sub-button that opens a list (mode, fan speed, preset).</summary>
        Border Chip(string icon, string text, Brush iconBrush, List<string> choices, string current, Func<string, string> label, Action<string> pick)
        {
            Border b = null;
            b = BubbleRow.SubButton(_t, _k, ClimateIcons.Make(icon, iconBrush, 16 * _k), text, true, delegate
            {
                if (choices == null || choices.Count == 0) return;
                var m = new ContextMenu();
                foreach (string c in choices)
                {
                    string cc = c;
                    var mi = new MenuItem { Header = label(c), IsCheckable = true, IsChecked = c == current };
                    mi.Click += delegate { pick(cc); };
                    m.Items.Add(mi);
                }
                PopupWindow host = Window.GetWindow(this) as PopupWindow;
                if (host != null) host.Pinned = true;
                m.Closed += delegate { if (host != null) host.Pinned = false; };
                m.PlacementTarget = b;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                m.IsOpen = true;
            });
            b.Margin = new Thickness(0, 0, 6 * _k, 6 * _k);
            return b;
        }

        void Update()
        {
            HaState st = _ctl.Read();
            if (!HaClient.Configured || _ctl.Entity.Length == 0 || st == null || st.Unavailable)
            {
                _row.SetOverlay(null, 0);
                _row.SetIcon(ClimateIcons.Make("thermo", _t.Fg, _row.IconSize), false);
                _row.SetText(st != null && st.Name.Length > 0 ? st.Name : (_ctl.Entity.Length > 0 ? _ctl.Entity : "Climate"),
                             !HaClient.Configured ? "Home Assistant isn't set up" : _ctl.Entity.Length == 0 ? "Pick a climate entity in Settings" : st == null ? "Asking Home Assistant…" : "Unavailable");
                _row.Opacity = st != null && st.Unavailable ? 0.5 : 1;
                _target.Text = "--";
                _chips.Children.Clear(); _chipKey = null;
                UpdateZones();
                return;
            }
            _row.Opacity = 1;
            string mode = _ctl.Mode(st);
            bool on = mode != "off";
            SolidColorBrush col = ClimateCtl.ModeBrush(mode, _t);
            // Bubble: the state colour behind the whole card while it is on (heating / cooling / drying / fan)
            _row.SetOverlay(on ? col : null, 0.55);
            _row.SetIcon(ClimateIcons.Make(ClimateCtl.ModeIcon(mode), on ? _t.Fg : _t.Fg, _row.IconSize), on);
            double cur;
            string now = st.AttrNum("current_temperature", out cur) ? ClimateCtl.Deg(cur) : "";
            _row.SetText(st.Name.Length > 0 ? st.Name : _ctl.Entity,
                         ClimateCtl.ModeName(mode) + (on ? ClimateCtl.ActionText(st) : "") + (now.Length > 0 ? " · " + now : ""));
            double target = _ctl.Target(st);
            _target.Text = on && !double.IsNaN(target) ? ClimateCtl.Deg(target) : "--";
            _tempPill.Opacity = on ? 1 : 0.6;
            _tempPill.IsEnabled = on;

            // Bubble sub-buttons: mode, fan speed, preset (only what the entity has)
            List<string> modes = st.AttrList("hvac_modes"), fans = st.AttrList("fan_modes"), presets = st.AttrList("preset_modes");
            string fan = st.AttrText("fan_mode"), preset = st.AttrText("preset_mode");
            string ck = mode + "|" + string.Join(",", modes) + "|" + fan + "|" + string.Join(",", fans) + "|" + preset + "|" + string.Join(",", presets);
            if (ck != _chipKey)
            {
                _chipKey = ck;
                _chips.Children.Clear();
                _chips.Children.Add(Chip(ClimateCtl.ModeIcon(mode), ClimateCtl.ModeName(mode), on ? (Brush)col : _t.FgDim, modes, mode, ClimateCtl.ModeName,
                                         delegate (string m) { _ctl.SetMode(m); Update(); }));
                if (fans.Count > 0)
                    _chips.Children.Add(Chip("fan", fan.Length > 0 ? ClimateCtl.Nice(fan) : "Fan", _t.Fg, fans, fan, ClimateCtl.Nice, delegate (string f) { _ctl.SetFan(f); }));
                if (presets.Count > 0)
                    _chips.Children.Add(Chip("auto", preset.Length > 0 && preset != "none" ? ClimateCtl.Nice(preset) : "Preset", _t.Fg, presets, preset, ClimateCtl.Nice, delegate (string pr) { _ctl.SetPreset(pr); }));
            }
            UpdateZones();
            _msg.Text = HaClient.LastError.Length > 0 ? "Home Assistant: " + HaClient.LastError : "";
            _msg.Visibility = _msg.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateZones()
        {
            bool show = HaView.Of(_cfg).ShowZones && HaView.Of(_cfg).Zones.Count > 0;
            _zones.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            string key = "";
            foreach (ClimateZone z in HaView.Of(_cfg).Zones) key += z.Entity + "=" + z.Name + ";";
            if (key != _zoneKey)
            {
                _zoneKey = key;
                _zones.Children.Clear();
                _sliders.Clear();
                // two columns of zones (one when there's only one)
                _zones.Columns = HaView.Of(_cfg).Zones.Count > 1 ? 2 : 1;
                int i = 0;
                foreach (ClimateZone z in HaView.Of(_cfg).Zones)
                {
                    var s = new ZoneSlider(_t, z, _ctl);
                    bool left = _zones.Columns == 2 && i % 2 == 0, right = _zones.Columns == 2 && i % 2 == 1;
                    s.Margin = new Thickness(right ? 4 * _k : 0, 0, left ? 4 * _k : 0, 8 * _k);
                    i++;
                    _sliders.Add(s);
                    _zones.Children.Add(s);
                }
            }
            HaState ac = _ctl.Read();
            SolidColorBrush col = ac != null && !ac.Unavailable && _ctl.Mode(ac) != "off" ? ClimateCtl.ModeBrush(_ctl.Mode(ac), _t) : _t.MainAccent;
            foreach (ZoneSlider s in _sliders) s.Update(col);
        }
    }

    /// <summary>
    /// A Bubble Card slider for one zone (a fan entity, 0 - 10): the fill grows from the left in the air conditioner's mode colour
    /// (the accent when it is off), the fan icon in its circle (click: off / back on), the name over "7 / 10". Click or drag along it;
    /// scroll for one step.
    /// </summary>
    sealed class ZoneSlider : BubbleRowHost
    {
        readonly ClimateZone _z;
        readonly ClimateCtl _ctl;
        int _level = -1, _preview = -1;
        SolidColorBrush _col;

        public ZoneSlider(Theme t, ClimateZone z, ClimateCtl ctl) : base(t)
        {
            _z = z; _ctl = ctl;
            Row.MakeSlider(delegate (double f) { _preview = (int)Math.Round(f * 10); Draw(); },
                           delegate (double f) { _preview = -1; _ctl.SetZone(_z, (int)Math.Round(f * 10)); Update(_col); });
            Row.OnIconClick(delegate { _ctl.ToggleZone(_z, HaClient.Get(_z.Entity)); Update(_col); });
            Row.MouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                e.Handled = true;
                _ctl.SetZone(_z, Math.Max(0, _level) + (e.Delta > 0 ? 1 : -1));
                Update(_col);
            };
        }

        public void Update(SolidColorBrush col)
        {
            _col = col ?? T.MainAccent;
            HaState zs = HaClient.Get(_z.Entity);
            _level = _ctl.ZoneLevel(_z, zs);
            Row.ToolTip = _z.Entity + (zs != null && zs.Unavailable ? " - unavailable" : "") + "\nClick or drag along it (0 - 100%), scroll for 10% steps, click the icon for off / on";
            Row.Opacity = zs != null && zs.Unavailable ? 0.5 : 1;
            Draw();
        }

        void Draw()
        {
            int lv = _preview >= 0 ? _preview : _level;
            bool on = lv > 0;
            HaState zs = HaClient.Get(_z.Entity);
            Row.SetText(ClimateCtl.ZoneName(_z, zs), ClimateCtl.ZoneText(lv));
            Row.SetIcon(ClimateIcons.Make("fan", T.Fg, Row.IconSize), on);
            if (_preview < 0) Row.SetFill(lv <= 0 ? 0 : lv / 10.0, _col ?? T.MainAccent, 0.85);
            else Row.SetFill(lv / 10.0, _col ?? T.MainAccent, 0.85);
        }
    }

    /// <summary>A Border holding one BubbleRow (so a row type can keep its own state next to it).</summary>
    public class BubbleRowHost : Border
    {
        protected readonly Theme T;
        public readonly BubbleRow Row;
        public BubbleRowHost(Theme t)
        {
            T = t;
            Row = new BubbleRow(t);
            Child = Row;
        }
    }

    /// <summary>Simple drawn icons (24 × 24, like Material Design's): snowflake, flame, droplet, fan, auto, power, thermometer, chevron.</summary>
    public static class ClimateIcons
    {
        public static FrameworkElement Make(string kind, Brush b, double size)
        {
            var c = new Canvas { Width = 24, Height = 24 };
            Action<string, bool> add = delegate (string data, bool fill)
            {
                var p = new Path { Data = Geometry.Parse(data), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                if (fill) p.Fill = b; else { p.Stroke = b; p.StrokeThickness = 2; }
                c.Children.Add(p);
            };
            switch (kind)
            {
                case "cool":
                    {
                        // six arms, each with a small V near its end
                        var sb = new System.Text.StringBuilder();
                        for (int i = 0; i < 6; i++)
                        {
                            double a = i * Math.PI / 3 - Math.PI / 2;
                            Func<double, double, string> pt = delegate (double r, double off)
                            {
                                double x = 12 + r * Math.Cos(a) - off * Math.Sin(a), y = 12 + r * Math.Sin(a) + off * Math.Cos(a);
                                return x.ToString("0.##", CultureInfo.InvariantCulture) + "," + y.ToString("0.##", CultureInfo.InvariantCulture);
                            };
                            sb.Append("M12,12 L").Append(pt(9.5, 0)).Append(' ');
                            sb.Append("M").Append(pt(5.5, -2.6)).Append(" L").Append(pt(7.6, 0)).Append(" L").Append(pt(5.5, 2.6)).Append(' ');
                        }
                        add(sb.ToString(), false);
                        break;
                    }
                case "heat": add("M12,2.5 C13.2,6.2 17.5,8.6 17.5,13.8 A5.5,5.5 0 0 1 6.5,13.8 C6.5,11 8.1,9.3 9.3,7.8 C9.7,10.1 10.8,11.3 12,11.6 C11.7,8.6 11,5.6 12,2.5 Z", true); break;
                case "dry": add("M12,3 C12,3 6,10 6,14.5 A6,6 0 0 0 18,14.5 C18,10 12,3 12,3 Z", true); break;
                case "fan":
                    {
                        const string petal = "M12,12 C9.6,9.2 9.4,4.2 12.6,3.2 C15.8,2.4 16.2,7.4 12,12 Z";
                        for (int i = 0; i < 3; i++)
                        {
                            var p = new Path { Data = Geometry.Parse(petal), Fill = b, RenderTransform = new RotateTransform(i * 120, 12, 12) };
                            c.Children.Add(p);
                        }
                        add("M12,10.2 A1.8,1.8 0 1 1 11.99,10.2 Z", true);
                        break;
                    }
                case "auto":
                    add("M12,3 A9,9 0 1 1 11.99,3", false);
                    add("M8.6,16 L12,7.6 L15.4,16 M9.9,12.9 L14.1,12.9", false);
                    break;
                case "power":
                    add("M7.2,6.6 A7.4,7.4 0 1 0 16.8,6.6", false);
                    add("M12,3 L12,11", false);
                    break;
                case "chevron": add("M7,10 L12,15 L17,10", false); break;
                case "blinds":
                    add("M4,4 L20,4 M5,4 L5,20 M19,4 L19,20", false);
                    add("M6,8 L18,8 M6,11.5 L18,11.5 M6,15 L18,15", false);
                    break;
                default:
                    add("M10,4 A2,2 0 0 1 14,4 L14,13.6 A4,4 0 1 1 10,13.6 Z", false);
                    add("M12,10 L12,16", false);
                    break;
            }
            return new Viewbox { Width = size, Height = size, Child = c, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>The icon in a filled circle (Bubble Card's icon spot).</summary>
        public static FrameworkElement Circle(string kind, Brush fg, Brush bg, double d)
        {
            d = Math.Max(14, d);
            return new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = bg, VerticalAlignment = VerticalAlignment.Center,
                Child = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Make(kind, fg, d * 0.56) }
            };
        }
    }
}
