using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Row builders: text, numbers, sliders, check boxes, combos, fonts, colours, icons and sensors.</summary>
    public partial class SettingsWindow
    {
        // =================================================================
        // Row builders (every row goes into the card open on the panel)
        // =================================================================
        void TextRow(StackPanel p, string label, Func<string> get, Action<string> set, string info = null)
        {
            var box = new TextBox { Text = get() ?? "", Width = 280 };
            Action commit = delegate
            {
                if (_loading) return;
                set(box.Text.Trim());
                _mgr.SettingsChanged();
            };
            box.LostFocus += delegate { commit(); };
            box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
            Place(p, Row(label, box, false, info));
        }

        /// <summary>Path box + Browse + Clear for an icon file.</summary>
        /// <summary>The Start button icons as small previews in the theme's colours (on its background); click one to use it.</summary>
        void StartIconPicker(StackPanel p, ModulePane pane, ModuleConfig cfg)
        {
            Theme th = Theme.From(S, 12, 0);
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            for (int i = 0; i < ModuleTypes.StartIcons.Length; i++)
            {
                string kind = ModuleTypes.StartIcons[i];
                bool sel = cfg.StartIcon == kind;
                var stack = new StackPanel { Width = 74 };
                var pic = new Border { Height = 46, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(th.BgColor.R, th.BgColor.G, th.BgColor.B)) };
                if (kind == "Custom")
                {
                    ImageSource img = IconUtil.FromIconFile(cfg.IconPath);
                    pic.Child = img != null ? (UIElement)new Image { Source = img, Width = 26, Height = 26 }
                                            : new TextBlock { Text = "\uE8E5", FontFamily = IconUtil.GlyphFont, FontSize = 18, Foreground = th.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                else
                {
                    var g = StartGlyph.Make(kind, th.MainAccent, th.SecondaryAccent, th.Fg, 26);
                    g.HorizontalAlignment = HorizontalAlignment.Center;
                    pic.Child = g;
                }
                stack.Children.Add(pic);
                var name = Txt(ModuleTypes.StartIconNames[i], "vsFg", 11.5, false);
                name.HorizontalAlignment = HorizontalAlignment.Center; name.TextAlignment = TextAlignment.Center; name.TextWrapping = TextWrapping.Wrap;
                name.Margin = new Thickness(0, 5, 0, 0);
                stack.Children.Add(name);
                var card = new Border { Child = stack, Padding = new Thickness(5), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(sel ? 2 : 1), Cursor = Cursors.Hand };
                Ref(card, Border.BackgroundProperty, "vsCard");
                Ref(card, Border.BorderBrushProperty, sel ? "vsAccent" : "vsLine");
                card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (cfg.StartIcon == kind) return;
                    cfg.StartIcon = kind;
                    _mgr.SettingsChanged();
                    Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
                };
                wrap.Children.Add(card);
            }
            Place(p, wrap);
        }

        void IconPickRow(StackPanel p, string label, Func<string> get, Action<string> set)
        {
            var box = new TextBox { Text = get() ?? "", Width = 240 };
            var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
            var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0) };
            Action commit = delegate
            {
                if (_loading) return;
                set(box.Text.Trim().Trim('"'));
                _mgr.SettingsChanged();
            };
            box.LostFocus += delegate { commit(); };
            box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
            browse.Click += delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "Choose an icon";
                dlg.Filter = "Icons and images (*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif|Programs and libraries (*.exe;*.dll)|*.exe;*.dll|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != true) return;
                box.Text = dlg.FileName;
                commit();
            };
            clear.Click += delegate { box.Text = ""; commit(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(box);
            row.Children.Add(browse);
            row.Children.Add(clear);
            Place(p, Row(label, row, true));
        }

        void NumberRow(StackPanel p, string label, Func<double> get, Action<double> set)
        {
            var box = new TextBox { Text = get().ToString(CultureInfo.CurrentCulture), Width = 100 };
            Action commit = delegate
            {
                if (_loading) return;
                double v;
                if (double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v) ||
                    double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    set(v);
                    _mgr.SettingsChanged();
                }
                else box.Text = get().ToString(CultureInfo.CurrentCulture);
            };
            box.LostFocus += delegate { commit(); };
            box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) commit(); };
            Place(p, Row(label, box));
        }

        /// <summary>
        /// Pick one of the values HWiNFO (or LibreHardwareMonitor - the choice above the list) is currently publishing. tempOnly = list
        /// temperatures only; allowAuto = first entry "(automatic)". A saved choice not published right now stays selectable so it is not lost.
        /// </summary>
        void SensorPicker(StackPanel p, string label, Func<string> get, Action<string> set, bool tempOnly, bool allowAuto)
        {
            var keys = new List<string>();
            var combo = new ComboBox { MinWidth = 300, MaxWidth = 520 };
            var status = Txt("", "vsDim", 12, false);
            status.Margin = new Thickness(0, 6, 0, 0);

            Action fill = delegate
            {
                bool old = _loading;
                _loading = true;
                HwInfo.Refresh();
                combo.Items.Clear();
                keys.Clear();
                if (allowAuto) { combo.Items.Add("(automatic)"); keys.Add(""); }
                else { combo.Items.Add("(none)"); keys.Add(""); }

                string cur = get() ?? "";
                bool found = cur.Length == 0;
                int count = 0;
                foreach (var r in HwInfo.All)
                {
                    if (tempOnly && !r.IsTemperature) continue;
                    count++;
                    combo.Items.Add(r.Sensor + "  -  " + r.Label + "   [" + r.Text + "]");
                    keys.Add(r.Key);
                    if (string.Equals(r.Key, cur, StringComparison.OrdinalIgnoreCase)) found = true;
                }
                if (!found)
                {
                    combo.Items.Add("(saved, not reported right now)  " + cur.Replace("|", "  -  "));
                    keys.Add(cur);
                }
                int sel = 0;
                for (int i = 0; i < keys.Count; i++) if (string.Equals(keys[i], cur, StringComparison.OrdinalIgnoreCase)) sel = i;
                combo.SelectedIndex = sel;

                status.Text = HwInfo.Available
                    ? count + " " + HwInfo.SourceName + " reading" + (count == 1 ? "" : "s") + " available."
                    : HwInfo.IsLhm
                    ? HwInfo.LhmProblem + " Run LibreHardwareMonitor (as administrator, for CPU temperatures), then in its menu Options > Remote Web Server > Run " +
                      "(and Options > Run On Windows Startup so it's always there). Its port is under Options > Remote Web Server > Port. Press Refresh afterwards."
                    : "HWiNFO is not publishing any values. Run HWiNFO (Sensors window), open its settings > \"HWiNFO Gadget\", tick \"Enable reporting to Gadget\", " +
                      "then right-click a sensor and choose \"Report value in Gadget\". Press Refresh afterwards.";
                _loading = old;
            };

            // where the values come from (one choice for all of vShell)
            var source = new ComboBox { MinWidth = 200, Margin = new Thickness(0, 0, 0, 6) };
            source.Items.Add("HWiNFO");
            source.Items.Add("LibreHardwareMonitor");
            source.SelectedIndex = S.SensorSource == "LHM" ? 1 : 0;
            var port = new TextBox { Width = 70, Text = S.LhmPort.ToString(), Margin = new Thickness(8, 0, 0, 6), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "LibreHardwareMonitor's web server port (8085 unless you changed it)" };
            var portLabel = Txt("Port", "vsDim", 12, false);
            portLabel.Margin = new Thickness(12, 0, 0, 6);
            portLabel.VerticalAlignment = VerticalAlignment.Center;
            Action showPort = delegate { portLabel.Visibility = port.Visibility = source.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; };
            showPort();
            Action applySource = delegate
            {
                HwInfo.Use(S.SensorSource, S.LhmPort);
                fill();
                _mgr.SettingsChanged();
            };
            source.SelectionChanged += delegate
            {
                if (_loading) return;
                S.SensorSource = source.SelectedIndex == 1 ? "LHM" : "HWiNFO";
                showPort();
                applySource();
            };
            port.LostFocus += delegate
            {
                int n;
                if (!int.TryParse(port.Text.Trim(), out n) || n < 1 || n > 65535) { port.Text = S.LhmPort.ToString(); return; }
                if (n == S.LhmPort) return;
                S.LhmPort = n;
                applySource();
            };
            var srcLine = new StackPanel { Orientation = Orientation.Horizontal };
            srcLine.Children.Add(source);
            srcLine.Children.Add(portLabel);
            srcLine.Children.Add(port);

            combo.SelectionChanged += delegate
            {
                if (_loading) return;
                int i = combo.SelectedIndex;
                if (i < 0 || i >= keys.Count) return;
                set(keys[i]);
                _mgr.SettingsChanged();
            };

            var refresh = new Button { Content = "Refresh", Margin = new Thickness(6, 0, 0, 0) };
            refresh.Click += delegate { fill(); };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(combo);
            line.Children.Add(refresh);
            var block = new StackPanel();
            var from = Txt("Sensors from (for all of vShell)", "vsDim", 12, false);
            from.Margin = new Thickness(0, 0, 0, 4);
            block.Children.Add(from);
            block.Children.Add(srcLine);
            block.Children.Add(line);
            block.Children.Add(status);
            Place(p, Row(label, block, true));
            fill();
        }

        /// <summary>An on/off switch; clicking anywhere on the row flips it.</summary>
        CheckBox Check(StackPanel p, string text, Func<bool> get, Action<bool> set, string info = null)
        {
            var cb = new CheckBox { IsChecked = get() };
            Action changed = delegate
            {
                if (_loading) return;
                set(cb.IsChecked == true);
                _mgr.SettingsChanged();
            };
            cb.Click += delegate { changed(); };
            var wrap = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = Row(text, cb, false, info) };
            wrap.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!cb.IsEnabled || !wrap.IsEnabled) return;
                if (e.OriginalSource is DependencyObject && IsInside((DependencyObject)e.OriginalSource, cb)) return;
                cb.IsChecked = cb.IsChecked != true;
                changed();
            };
            Place(p, wrap);
            return cb;
        }

        /// <summary>A shape setting changed on a bar's / widget's own page: from now on it keeps its own look (the style no longer changes it).</summary>
        Action<T> OwnLook<T>(Func<bool> following, Action stopFollowing, Func<CheckBox> box, Action<T> set)
        {
            return delegate (T v)
            {
                set(v);
                if (!following()) return;
                stopFollowing();
                CheckBox cb = box();
                if (cb != null) { bool o = _loading; _loading = true; cb.IsChecked = false; _loading = o; }
            };
        }

        /// <summary>
        /// A number that can be off (saved as 0): a switch, and the slider for the number while it's on. Turned on again it starts at
        /// fallback (the number isn't kept while it's off).
        /// </summary>
        void OffSliderRow(StackPanel p, string label, double min, double max, double fallback, Func<double> get, Action<double> set, string info = null)
        {
            double start = get();
            bool on = start > 0;
            var cb = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = on ? start : fallback, IsSnapToTickEnabled = true, TickFrequency = 1,
                Width = 180, VerticalAlignment = VerticalAlignment.Center, IsEnabled = on
            };
            TextBox value = SliderNumber(sl, "");
            value.IsEnabled = on;
            sl.ValueChanged += delegate
            {
                if (_loading || cb.IsChecked != true) return;
                set(Math.Round(sl.Value));
                _mgr.SettingsChanged();
            };
            cb.Click += delegate
            {
                if (_loading) return;
                bool now = cb.IsChecked == true;
                sl.IsEnabled = now;
                value.IsEnabled = now;
                set(now ? Math.Round(sl.Value) : 0);
                _mgr.SettingsChanged();
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(cb);
            panel.Children.Add(sl);
            panel.Children.Add(value);
            Place(p, Row(label, panel, false, info));
        }

        void SliderRow(StackPanel p, string label, double min, double max, Func<double> get, Action<double> set, string info = null)
        {
            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = get(),
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Width = 220, VerticalAlignment = VerticalAlignment.Center
            };
            TextBox value = SliderNumber(sl, "");
            sl.ValueChanged += delegate
            {
                if (_loading) return;
                set(Math.Round(sl.Value));
                _mgr.SettingsChanged();
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(sl);
            panel.Children.Add(value);
            Place(p, Row(label, panel, false, info));
        }

        /// <summary>
        /// The number next to a slider: it shows the value, and a click on it lets you type one (Enter or clicking away applies it,
        /// Esc goes back). A typed value is kept within the slider's range and rounded to a whole number like the slider.
        /// </summary>
        TextBox SliderNumber(Slider sl, string suffix)
        {
            var tb = new TextBox
            {
                Width = 52, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Cursor = Cursors.IBeam,
                ToolTip = "Click to type a value (" + Math.Round(sl.Minimum) + " to " + Math.Round(sl.Maximum) + ")"
            };
            tb.MinHeight = 0;
            tb.Template = NumberBoxTemplate();      // plain text with an underline (the settings' text box style draws a full box)
            Ref(tb, TextBox.ForegroundProperty, "vsDim");
            Ref(tb, TextBox.CaretBrushProperty, "vsFg");
            bool editing = false;
            Action show = delegate { if (!editing) tb.Text = ((int)Math.Round(sl.Value)) + suffix; };
            sl.ValueChanged += delegate { show(); };
            show();
            Action<bool> underline = delegate (bool on)
            {
                if (on) Ref(tb, TextBox.BorderBrushProperty, "vsAccent");
                else tb.BorderBrush = Brushes.Transparent;
            };
            tb.MouseEnter += delegate { if (!editing) Ref(tb, TextBox.BorderBrushProperty, "vsCtrlBorder"); };
            tb.MouseLeave += delegate { if (!editing) underline(false); };
            // the first click selects the whole number, ready to type over
            tb.PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (tb.IsKeyboardFocusWithin) return;
                e.Handled = true;
                tb.Focus();
            };
            tb.GotKeyboardFocus += delegate
            {
                editing = true;
                underline(true);
                Ref(tb, TextBox.ForegroundProperty, "vsFg");
                tb.Text = ((int)Math.Round(sl.Value)).ToString();
                tb.SelectAll();
            };
            Action<bool> finish = delegate (bool apply)
            {
                if (!editing) return;
                editing = false;
                if (apply)
                {
                    string s = tb.Text.Replace(suffix.Length > 0 ? suffix : "\0", "").Replace("%", "").Trim();
                    double v;
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        sl.Value = Math.Max(sl.Minimum, Math.Min(sl.Maximum, Math.Round(v)));
                }
                underline(tb.IsMouseOver);
                if (tb.IsMouseOver) Ref(tb, TextBox.BorderBrushProperty, "vsCtrlBorder");
                Ref(tb, TextBox.ForegroundProperty, "vsDim");
                show();
            };
            tb.LostKeyboardFocus += delegate { finish(true); };
            tb.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; finish(true); Keyboard.ClearFocus(); }
                else if (e.Key == Key.Escape) { e.Handled = true; finish(false); Keyboard.ClearFocus(); }
            };
            return tb;
        }

        static ControlTemplate _numberBoxTemplate;
        static ControlTemplate NumberBoxTemplate()
        {
            if (_numberBoxTemplate != null) return _numberBoxTemplate;
            _numberBoxTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>" +
                "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>" +
                "<ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>" +
                "</Border></ControlTemplate>");
            return _numberBoxTemplate;
        }

        // ---- fonts ----------------------------------------------------------------------------------------
        static List<string> _fontNames;

        /// <summary>The installed font families, sorted (read once).</summary>
        static List<string> FontNames()
        {
            if (_fontNames != null) return _fontNames;
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var en = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
                foreach (FontFamily f in Fonts.SystemFontFamilies)
                {
                    string name;
                    if (!f.FamilyNames.TryGetValue(en, out name) || string.IsNullOrWhiteSpace(name)) name = f.Source;
                    if (!string.IsNullOrWhiteSpace(name) && !name.Contains("#")) set.Add(name.Trim());
                }
            }
            catch (Exception ex) { Log.Write("Fonts: " + ex.Message); }
            _fontNames = new List<string>(set);
            return _fontNames;
        }

        /// <summary>
        /// A drop-down of the installed fonts, each shown in its own face (type to jump to one, or type any name). extras = choices listed
        /// first (e.g. Theme / Mono / Handwritten for notes). Picking one applies it; typed text applies on Enter or leaving the box.
        /// </summary>
        ComboBox FontCombo(string[] extras, Func<string> get, Action<string> set)
        {
            var cb = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Width = 260, MaxDropDownHeight = 420, StaysOpenOnEdit = true };
            VirtualizingPanel.SetIsVirtualizing(cb, true);
            VirtualizingPanel.SetVirtualizationMode(cb, VirtualizationMode.Recycling);
            cb.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            var names = new List<string>();
            if (extras != null) names.AddRange(extras);
            foreach (string n in FontNames()) if (!names.Contains(n)) names.Add(n);
            string current = get() ?? "";
            if (current.Length > 0 && !names.Exists(delegate (string n) { return string.Equals(n, current, StringComparison.OrdinalIgnoreCase); })) names.Insert(extras != null ? extras.Length : 0, current);
            bool isExtra = false;
            foreach (string n in names)
            {
                var item = new ComboBoxItem { Content = n };
                isExtra = extras != null && Array.IndexOf(extras, n) >= 0;
                if (!isExtra) try { item.FontFamily = new FontFamily(n); item.FontSize = 14; } catch { }
                cb.Items.Add(item);
            }
            bool o = _loading;
            _loading = true;
            cb.Text = current;
            _loading = o;
            string applied = current;
            Action apply = delegate
            {
                if (_loading) return;
                string f = (cb.Text ?? "").Trim();
                if (f.Length == 0 || f == applied) return;
                foreach (string n in names) if (string.Equals(n, f, StringComparison.OrdinalIgnoreCase)) { f = n; break; }
                applied = f;
                set(f);
                _mgr.SettingsChanged();
            };
            cb.SelectionChanged += delegate
            {
                var item = cb.SelectedItem as ComboBoxItem;
                if (item == null) return;
                // the text box takes the item's text after this event
                cb.Dispatcher.BeginInvoke(new Action(delegate { apply(); }));
            };
            cb.DropDownClosed += delegate { apply(); };
            cb.LostKeyboardFocus += delegate { apply(); };
            cb.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { apply(); cb.IsDropDownOpen = false; } };
            return cb;
        }

        /// <summary>A choice: up to four short items show as joined buttons, more as a drop-down.</summary>
        void ComboRow(StackPanel p, string label, string[] items, Func<string> get, Action<string> set, string info = null)
        {
            bool shortList = items.Length >= 2 && items.Length <= 4;
            foreach (string s in items) if (s.Length > 12) shortList = false;
            if (shortList)
            {
                Place(p, Row(label, Segmented(items, get, set), false, info));
                return;
            }
            var cb = new ComboBox { MinWidth = 180 };
            foreach (string s in items) cb.Items.Add(s);
            cb.SelectedItem = get();
            cb.SelectionChanged += delegate
            {
                if (_loading || cb.SelectedItem == null) return;
                set((string)cb.SelectedItem);
                _mgr.SettingsChanged();
            };
            Place(p, Row(label, cb, false, info));
        }

        /// <summary>
        /// Colour: swatch (opens the colour picker) + value box. optional = empty is allowed (the module's default) and the theme slots
        /// Main Accent / Secondary Accent / Text are offered, so the colour follows the theme. theme = this is one of the theme colours themselves.
        /// </summary>
        void ColorRow(StackPanel p, string label, Func<string> get, Action<string> set, bool optional, bool theme, string emptySlot = null)
        {
            var swatch = new Border
            {
                Width = 30, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Pick a colour"
            };
            Ref(swatch, Border.BorderBrushProperty, "vsCtrlBorder");
            var box = new TextBox { Width = 104 };
            if (optional) box.ToolTip = "Empty = the default.  @main / @secondary / @text = that theme colour (follows theme changes).";
            var chips = new List<KeyValuePair<Border, string>>();

            Action refresh = delegate
            {
                bool old = _loading;
                _loading = true;
                string hex = get();
                string shown = (optional && string.IsNullOrEmpty(hex)) ? SlotHex(emptySlot ?? Theme.SlotMain) : SlotHex(hex);
                swatch.Background = new SolidColorBrush(Theme.Parse(shown, Colors.Gray));
                if (!box.IsKeyboardFocused) box.Text = hex;
                foreach (var kv in chips)
                {
                    bool on = string.Equals(hex, kv.Value, StringComparison.OrdinalIgnoreCase);
                    if (on) { Ref(kv.Key, Border.BorderBrushProperty, "vsAccent"); Ref(kv.Key, Border.BackgroundProperty, "vsSelBg"); }
                    else { Ref(kv.Key, Border.BorderBrushProperty, "vsCtrlBorder"); kv.Key.Background = Brushes.Transparent; }
                }
                _loading = old;
            };
            Action afterSet = theme ? (Action)RefreshColors : refresh;

            Action<string> commit = delegate (string v)
            {
                set(v);
                afterSet();
                _mgr.SettingsChanged();
            };

            swatch.MouseLeftButtonUp += delegate
            {
                string cur = get();
                Color c = Theme.Parse((optional && string.IsNullOrEmpty(cur)) ? SlotHex(emptySlot ?? Theme.SlotMain) : SlotHex(cur), Colors.White);
                using (var dlg = new System.Windows.Forms.ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.AnyColor = true;
                    dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        commit(string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, dlg.Color.R, dlg.Color.G, dlg.Color.B));
                }
            };

            Action fromBox = delegate
            {
                if (_loading) return;
                string t = box.Text.Trim();
                if (optional && t.Length == 0) { commit(""); return; }
                if (optional && Theme.IsSlot(t)) { commit(t.ToLowerInvariant()); return; }
                Color c;
                if (Theme.TryParse(t, out c)) commit(string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B));
                else refresh();   // revert
            };
            box.LostFocus += delegate { fromBox(); };
            box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) fromBox(); };

            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(box);
            if (optional)
            {
                // theme slots: the colour then follows the theme (preset, import, colours from the wallpaper)
                foreach (var slot in new[] { new[] { "Main Accent", Theme.SlotMain, S.MainAccent }, new[] { "Secondary Accent", Theme.SlotSecondary, S.SecondaryAccent }, new[] { "Text", Theme.SlotText, S.Foreground } })
                {
                    string key = slot[1];
                    var chip = Chip(slot[0], Theme.Parse(slot[2], Colors.Gray), false, delegate { commit(key); });
                    chip.ToolTip = "Use the theme's " + slot[0] + " colour (follows the theme)";
                    chips.Add(new KeyValuePair<Border, string>(chip, key));
                    row.Children.Add(chip);
                }
                var reset = new Button { Content = "Reset", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
                reset.Click += delegate { commit(""); };
                row.Children.Add(reset);
            }
            Place(p, Row(label, row, optional));

            refresh();
            if (theme) _colorRefresh.Add(refresh);
        }

        /// <summary>The colour value a setting stands for: a theme slot is looked up in the current theme colours.</summary>
        string SlotHex(string spec)
        {
            if (!Theme.IsSlot(spec)) return spec;
            switch (spec.Trim().ToLowerInvariant())
            {
                case Theme.SlotSecondary:
                case Theme.OldSlotSecondary: return S.SecondaryAccent;
                case Theme.SlotText: return S.Foreground;
                default: return S.MainAccent;
            }
        }

        void RefreshColors()
        {
            bool old = _loading;
            _loading = true;
            foreach (var r in _colorRefresh) r();
            if (_opacity != null)
            {
                Color bg = Theme.Parse(S.Background, Colors.Black);
                _opacity.Value = bg.A * 100.0 / 255.0;      // its number box follows the slider
            }
            _loading = old;
            ApplyChrome();     // the settings window follows the theme too
        }
    }
}
