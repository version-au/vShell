using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace vShell.Ui
{
    /// <summary>Tiny modal "enter a text" dialog (optionally with a few tick boxes under it).</summary>
    [vShell.Core.PluginApi]
    public sealed class PromptWindow : Window
    {
        readonly TextBox _box = new TextBox { Padding = new Thickness(4), Margin = new Thickness(0, 4, 0, 12) };
        readonly StackPanel _panel = new StackPanel { Margin = new Thickness(16) };
        readonly StackPanel _buttons;
        string _result;

        PromptWindow(string title, string label, string initial)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            _box.Text = initial ?? "";
            _panel.Children.Add(_box);

            _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80, Padding = new Thickness(0, 3, 0, 3) };
            ok.Click += delegate { _result = _box.Text; DialogResult = true; };
            _buttons.Children.Add(ok);
            _buttons.Children.Add(cancel);
            _panel.Children.Add(_buttons);
            Content = _panel;

            Loaded += delegate { _box.Focus(); _box.SelectAll(); };
        }

        /// <summary>Returns the entered text, or null if cancelled.</summary>
        public static string Ask(Window owner, string title, string label, string initial)
        {
            var w = new PromptWindow(title, label, initial);
            if (owner != null) w.Owner = owner;
            return w.ShowDialog() == true ? w._result : null;
        }

        /// <summary>
        /// A name plus tick boxes ("Include: ..."). ticks holds the starting state of each box and, after OK, what was ticked.
        /// Returns the entered text, or null if cancelled.
        /// </summary>
        public static string AskWithOptions(Window owner, string title, string label, string initial, string[] options, bool[] ticks, string note, string heading = "Include")
        {
            var w = new PromptWindow(title, label, initial);
            w.Width = 460;
            if (owner != null) w.Owner = owner;
            int at = w._panel.Children.IndexOf(w._buttons);
            if (!string.IsNullOrEmpty(heading)) w._panel.Children.Insert(at++, new TextBlock { Text = heading, Margin = new Thickness(0, 0, 0, 6) });
            var boxes = new CheckBox[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                boxes[i] = new CheckBox
                {
                    Content = new TextBlock { Text = options[i], TextWrapping = TextWrapping.Wrap },
                    IsChecked = i < ticks.Length && ticks[i], Margin = new Thickness(0, 0, 0, 8)
                };
                w._panel.Children.Insert(at++, boxes[i]);
            }
            if (!string.IsNullOrEmpty(note))
                w._panel.Children.Insert(at++, new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 14) });
            if (w.ShowDialog() != true) return null;
            for (int i = 0; i < options.Length && i < ticks.Length; i++) ticks[i] = boxes[i].IsChecked == true;
            return w._result;
        }
    }
}
