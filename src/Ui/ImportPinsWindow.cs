using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// "Import pins from the Windows taskbar": what Windows has pinned (icon, name, desktop / Store app), each with a tick box, and whether
    /// to add them after the ones already there or replace those. Ones already there are shown greyed (they are skipped).
    /// </summary>
    public sealed class ImportPinsWindow : Window
    {
        readonly List<CheckBox> _boxes = new List<CheckBox>();
        readonly List<WindowsPin> _pins;
        readonly RadioButton _replace;

        /// <summary>already(pin) = it is in the taskbar / launcher already.</summary>
        ImportPinsWindow(List<WindowsPin> pins, Func<WindowsPin, bool> already, string where)
        {
            _pins = pins;
            Title = "Import pins from the Windows taskbar";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock
            {
                Text = pins.Count == 0 ? "Windows' taskbar has no pinned apps vShell can read."
                                       : "These are pinned to the Windows taskbar, in its order. Tick the ones to add to the " + where + ":",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            var list = new StackPanel();
            foreach (WindowsPin p in pins)
            {
                bool have = already != null && already(p);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                ImageSource icon = p.IsStore ? IconUtil.FromParsingName(p.Path, 32) : IconUtil.FromPath(p.Path);
                if (icon != null)
                {
                    var img = new Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    row.Children.Add(img);
                }
                else row.Children.Add(new Border { Width = 28 });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock
                {
                    Text = (p.IsStore ? "Store app" : p.Target.Length > 0 ? p.Target : p.Path) + (have ? "  -  already there" : ""),
                    Foreground = Brushes.Gray, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340
                });
                row.Children.Add(text);
                var cb = new CheckBox { Content = row, IsChecked = !have, IsEnabled = !have, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                _boxes.Add(cb);
                list.Children.Add(cb);
            }
            root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var add = new RadioButton { Content = "Add them after the ones there now", IsChecked = true, Margin = new Thickness(0, 12, 0, 2), GroupName = "how" };
            _replace = new RadioButton { Content = "Replace the ones there now (in Windows' order)", Margin = new Thickness(0, 2, 0, 0), GroupName = "how" };
            if (pins.Count > 0) { root.Children.Add(add); root.Children.Add(_replace); }
            _replace.Checked += delegate { for (int i = 0; i < _boxes.Count; i++) _boxes[i].IsEnabled = true; };
            add.Checked += delegate
            {
                for (int i = 0; i < _boxes.Count; i++)
                    if (already != null && already(_pins[i])) { _boxes[i].IsEnabled = false; _boxes[i].IsChecked = false; }
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "Import", IsDefault = true, Width = 90, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 8, 0), IsEnabled = pins.Count > 0 };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 90, Padding = new Thickness(0, 3, 0, 3) };
            ok.Click += delegate { DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        /// <summary>Show it; null = cancelled. replace = the user chose to replace what is there.</summary>
        public static List<WindowsPin> Ask(Window owner, Func<WindowsPin, bool> already, string where, out bool replace)
        {
            replace = false;
            List<WindowsPin> pins;
            try
            {
                pins = WindowsPins.Read(delegate (string aumid)
                {
                    foreach (AppEntry e in AppCatalog.Get())
                        if (e.IsStore && string.Equals(e.Target, "shell:AppsFolder\\" + aumid, StringComparison.OrdinalIgnoreCase)) return e.Name;
                    return null;
                });
            }
            catch (Exception ex) { Log.Write("Import pins: " + ex.Message); pins = new List<WindowsPin>(); }
            var w = new ImportPinsWindow(pins, already, where);
            if (owner != null) w.Owner = owner;
            if (w.ShowDialog() != true) return null;
            replace = w._replace.IsChecked == true;
            var chosen = new List<WindowsPin>();
            for (int i = 0; i < w._boxes.Count; i++) if (w._boxes[i].IsChecked == true) chosen.Add(w._pins[i]);
            return chosen;
        }
    }
}
