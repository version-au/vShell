using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Lists every entity Home Assistant knows, with a filter box, and returns the ones the user picks.</summary>
    public sealed class HaEntityPickerWindow : Window
    {
        sealed class Row
        {
            public string EntityId { get; set; }
            public string Name { get; set; }
            public string State { get; set; }
        }

        readonly List<Row> _all = new List<Row>();
        readonly ListView _list = new ListView { Height = 360, Margin = new Thickness(0, 6, 0, 6) };
        readonly TextBox _filter = new TextBox { Padding = new Thickness(4) };
        readonly TextBlock _status = new TextBlock { Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
        readonly List<string> _result = new List<string>();

        HaEntityPickerWindow(bool multi)
        {
            Title = multi ? "Pick Home Assistant entities" : "Pick a Home Assistant entity";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var p = new StackPanel { Margin = new Thickness(16) };
            p.Children.Add(new TextBlock { Text = "Type to filter by entity id or name (for example \"sigen\" or \"battery\").", Margin = new Thickness(0, 0, 0, 4) });
            p.Children.Add(_filter);

            _list.SelectionMode = multi ? SelectionMode.Extended : SelectionMode.Single;
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Entity id", Width = 300, DisplayMemberBinding = new Binding("EntityId") });
            gv.Columns.Add(new GridViewColumn { Header = "Name", Width = 250, DisplayMemberBinding = new Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "State", Width = 100, DisplayMemberBinding = new Binding("State") });
            _list.View = gv;
            p.Children.Add(_list);
            p.Children.Add(_status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = multi ? "Add selected" : "Use this one", IsDefault = true, Width = 110, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80, Padding = new Thickness(0, 3, 0, 3) };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            p.Children.Add(buttons);
            Content = p;

            _filter.TextChanged += delegate { ApplyFilter(); };
            _list.MouseDoubleClick += delegate { if (!multi) Accept(); };
            Loaded += delegate { _filter.Focus(); Load(); };
        }

        async void Load()
        {
            _status.Text = "Loading entities from Home Assistant…";
            try
            {
                List<HaState> states = await HaClient.ListAsync();
                foreach (HaState s in states) _all.Add(new Row { EntityId = s.EntityId, Name = s.Name, State = s.State + (s.Unit.Length > 0 ? " " + s.Unit : "") });
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _status.Text = "Could not read the entity list: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
        }

        void ApplyFilter()
        {
            string f = _filter.Text.Trim();
            _list.Items.Clear();
            int shown = 0;
            foreach (Row r in _all)
            {
                if (f.Length > 0 &&
                    r.EntityId.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (r.Name ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (shown++ >= 600) break;
                _list.Items.Add(r);
            }
            _status.Text = _all.Count == 0 ? _status.Text : (shown >= 600 ? "Showing the first 600 matches - type more to narrow it down." : shown + " of " + _all.Count + " entities.");
        }

        void Accept()
        {
            _result.Clear();
            foreach (object o in _list.SelectedItems) _result.Add(((Row)o).EntityId);
            if (_result.Count == 0) return;
            DialogResult = true;
        }

        /// <summary>The picked entity ids, or null if cancelled.</summary>
        public static List<string> Pick(Window owner, bool multi)
        {
            var w = new HaEntityPickerWindow(multi);
            Questions.Own(w, owner);
            return w.ShowDialog() == true ? w._result : null;
        }
    }
}
