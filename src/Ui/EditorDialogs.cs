using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>One line of a FormWindow: a text box, a drop-down (Choices) or a check box (IsBool).</summary>
    [PluginApi]
    public sealed class FormField
    {
        public string Label = "";
        public string Value = "";
        public string[] Choices;
        public bool IsBool;
        /// <summary>A password box (the value isn't shown).</summary>
        public bool IsPassword;
        public string Hint = "";

        public bool Checked { get { return Value == "1"; } set { Value = value ? "1" : "0"; } }
        public FormField(string label, string value) { Label = label; Value = value ?? ""; }
        public static FormField Combo(string label, string value, string[] choices) { return new FormField(label, value) { Choices = choices }; }
        public static FormField Check(string label, bool value) { return new FormField(label, value ? "1" : "0") { IsBool = true }; }
        public static FormField Password(string label, string hint) { return new FormField(label, "") { IsPassword = true, Hint = hint ?? "" }; }
    }

    /// <summary>Small modal dialog with several labelled fields. Values are written back into the fields on OK.</summary>
    [PluginApi]
    public sealed class FormWindow : Window
    {
        readonly List<FormField> _fields;
        readonly List<Control> _controls = new List<Control>();

        FormWindow(string title, List<FormField> fields)
        {
            _fields = fields;
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var p = new StackPanel { Margin = new Thickness(16) };
            foreach (FormField f in fields)
            {
                if (f.IsBool)
                {
                    var cb = new CheckBox { Content = f.Label, IsChecked = f.Checked, Margin = new Thickness(0, 4, 0, 4) };
                    _controls.Add(cb);
                    p.Children.Add(cb);
                }
                else
                {
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    row.Children.Add(new TextBlock { Text = f.Label, Width = 130, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                    Control c;
                    if (f.Choices != null)
                    {
                        var cb = new ComboBox();
                        foreach (string s in f.Choices) cb.Items.Add(s);
                        cb.SelectedItem = f.Value;
                        if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
                        c = cb;
                    }
                    else if (f.IsPassword) c = new PasswordBox { Password = f.Value, Padding = new Thickness(3) };
                    else c = new TextBox { Text = f.Value, Padding = new Thickness(3) };
                    _controls.Add(c);
                    row.Children.Add(c);
                    p.Children.Add(row);
                }
                if (f.Hint.Length > 0)
                    p.Children.Add(new TextBlock { Text = f.Hint, Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80, Padding = new Thickness(0, 3, 0, 3) };
            ok.Click += delegate { Collect(); DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            p.Children.Add(buttons);
            Content = p;

            Loaded += delegate { if (_controls.Count > 0 && _controls[0] is TextBox) { _controls[0].Focus(); ((TextBox)_controls[0]).SelectAll(); } };
        }

        void Collect()
        {
            for (int i = 0; i < _fields.Count; i++)
            {
                Control c = _controls[i];
                if (c is CheckBox) _fields[i].Checked = ((CheckBox)c).IsChecked == true;
                else if (c is ComboBox) _fields[i].Value = ((ComboBox)c).SelectedItem as string ?? _fields[i].Value;
                else if (c is TextBox) _fields[i].Value = ((TextBox)c).Text.Trim();
                else if (c is PasswordBox) _fields[i].Value = ((PasswordBox)c).Password;
            }
        }

        /// <summary>true = OK (the fields now hold the new values).</summary>
        public static bool Ask(Window owner, string title, List<FormField> fields)
        {
            var w = new FormWindow(title, fields);
            Questions.Own(w, owner);
            return w.ShowDialog() == true;
        }
    }

    /// <summary>A list with Add / Edit / Remove / Up / Down, used for the Home Assistant entities and the services to watch.</summary>
    [PluginApi]
    public sealed class ListEditorWindow<T> : Window where T : class
    {
        readonly List<T> _list;
        readonly Func<T, string> _describe;
        readonly Func<Window, T, bool> _edit;
        readonly Action _changed;
        readonly ListBox _box = new ListBox { Height = 260, Margin = new Thickness(0, 0, 0, 8), SelectionMode = SelectionMode.Extended };
        readonly WrapPanel _top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        readonly WrapPanel _bottom = new WrapPanel();

        public ListEditorWindow(string title, string intro, List<T> list, Func<T, string> describe, Func<Window, T, bool> edit, Action changed)
        {
            _list = list; _describe = describe; _edit = edit; _changed = changed;
            Title = title;
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var p = new StackPanel { Margin = new Thickness(16) };
            if (intro.Length > 0)
                p.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
            p.Children.Add(_top);
            p.Children.Add(_box);

            AddBottom("Edit…", delegate { EditSelected(); });
            AddBottom("Remove", delegate { RemoveSelected(); });
            AddBottom("Move up", delegate { Move(-1); });
            AddBottom("Move down", delegate { Move(1); });
            p.Children.Add(_bottom);

            var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Width = 80, Padding = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            close.Click += delegate { Close(); };
            p.Children.Add(close);
            Content = p;

            _box.MouseDoubleClick += delegate { EditSelected(); };
            Refresh(-1);
        }

        Button MakeButton(string caption, Action act)
        {
            var b = new Button { Content = caption, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4) };
            b.Click += delegate { act(); };
            return b;
        }

        void AddBottom(string caption, Action act) { _bottom.Children.Add(MakeButton(caption, act)); }

        /// <summary>A button above the list (for the "Add ..." actions).</summary>
        public void AddAction(string caption, Action<ListEditorWindow<T>> act) { AddActionButton(caption, act); }

        /// <summary>The same, returning the button (to enable / disable it).</summary>
        public Button AddActionButton(string caption, Action<ListEditorWindow<T>> act)
        {
            Button b = MakeButton(caption, delegate { act(this); });
            _top.Children.Add(b);
            return b;
        }

        public List<T> Items { get { return _list; } }

        /// <summary>The picked rows (Ctrl / Shift+click picks several), top to bottom.</summary>
        public List<T> SelectedItems
        {
            get
            {
                var idx = new List<int>();
                foreach (object o in _box.SelectedItems) { int i = _box.Items.IndexOf(o); if (i >= 0 && !idx.Contains(i)) idx.Add(i); }
                idx.Sort();
                var r = new List<T>();
                foreach (int i in idx) if (i < _list.Count) r.Add(_list[i]);
                return r;
            }
        }

        /// <summary>Redraw the list and tell the settings something changed.</summary>
        public void Refresh(int select)
        {
            _box.Items.Clear();
            foreach (T t in _list) _box.Items.Add(_describe(t));
            if (select >= 0 && select < _box.Items.Count) _box.SelectedIndex = select;
        }

        public void Changed(int select) { Refresh(select); _changed(); }

        void EditSelected()
        {
            int i = _box.SelectedIndex;
            if (i < 0 || i >= _list.Count) return;
            if (_edit(this, _list[i])) Changed(i);
        }

        void RemoveSelected()
        {
            int i = _box.SelectedIndex;
            if (i < 0 || i >= _list.Count) return;
            _list.RemoveAt(i);
            Changed(Math.Min(i, _list.Count - 1));
        }

        void Move(int d)
        {
            int i = _box.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= _list.Count) return;
            T t = _list[i]; _list[i] = _list[j]; _list[j] = t;
            Changed(j);
        }
    }
}
