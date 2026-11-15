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
    /// <summary>Settings > Startup apps.</summary>
    public partial class SettingsWindow
    {
        // =================================================================
        // Startup apps
        // =================================================================
        ListView _startList;
        TextBlock _startStatus;

        void BuildStartupTab(StackPanel p)
        {
            Button enable = SmallButton("Enable"), disable = SmallButton("Disable"), remove = SmallButton("Remove"), refresh = SmallButton("Refresh");
            foreach (var b in new[] { enable, disable, remove, refresh }) b.Margin = new Thickness(6, 0, 0, 0);
            PageHeader(p, "Startup apps", "Programs Windows starts when you sign in - Enable / Disable is the same switch as Task Manager's Startup tab", refresh);

            Header(p, "Apps");
            _startList = new ListView { Height = 280, SelectionMode = SelectionMode.Extended };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Name", Width = 160, DisplayMemberBinding = new Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "Status", Width = 76, DisplayMemberBinding = new Binding("State") });
            gv.Columns.Add(new GridViewColumn { Header = "From", Width = 150, DisplayMemberBinding = new Binding("Source") });
            gv.Columns.Add(new GridViewColumn { Header = "Command", Width = 320, DisplayMemberBinding = new Binding("Command") });
            _startList.View = gv;
            Place(p, _startList);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _startStatus = Txt("", "vsDim", 12, false);
            _startStatus.TextWrapping = TextWrapping.Wrap;
            _startStatus.VerticalAlignment = VerticalAlignment.Center;
            _startStatus.Margin = new Thickness(0, 0, 12, 0);
            enable.Margin = new Thickness(0);
            row.Children.Add(enable);
            row.Children.Add(disable);
            row.Children.Add(remove);
            var bar = new DockPanel();
            DockPanel.SetDock(row, Dock.Right);
            bar.Children.Add(row);
            bar.Children.Add(_startStatus);
            Place(p, bar);

            enable.Click += delegate { ChangeSelected(true); };
            disable.Click += delegate { ChangeSelected(false); };
            refresh.Click += delegate { RefreshStartup(null); };
            remove.Click += delegate
            {
                var items = SelectedStartupItems();
                if (items.Count == 0) { _startStatus.Text = "Select one or more entries first."; return; }
                if (MessageBox.Show(this, "Remove " + (items.Count == 1 ? "\"" + items[0].Name + "\"" : items.Count + " entries") + " from startup?\n\nThe program itself is not uninstalled.",
                                    "Remove startup entry", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                string firstErr = null;
                foreach (var it in items)
                {
                    string err = StartupManager.Remove(it);
                    if (err != null && firstErr == null) firstErr = err;
                }
                RefreshStartup(firstErr);
            };

            // ---- add -----------------------------------------------------------------
            Header(p, "Add a startup app");
            var nameBox = new TextBox { Width = 280 };
            var cmdBox = new TextBox { Width = 360 };
            var browse = new Button { Content = "Browse\u2026", Margin = new Thickness(6, 0, 0, 0) };
            Place(p, Row("Name", nameBox));
            var cmdLine = new StackPanel { Orientation = Orientation.Horizontal };
            cmdLine.Children.Add(cmdBox);
            cmdLine.Children.Add(browse);
            Place(p, Row("Program / command", cmdLine, true));
            Button add = null;
            add = Btn("Add to startup", delegate
            {
                string err = StartupManager.Add(nameBox.Text, cmdBox.Text);
                if (err == null) { nameBox.Text = ""; cmdBox.Text = ""; }
                RefreshStartup(err);
                if (err == null) _startStatus.Text = "Added.";
            }, true);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            Place(p, add);

            browse.Click += delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "Choose a program";
                dlg.Filter = "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != true) return;
                cmdBox.Text = "\"" + dlg.FileName + "\"";
                if (nameBox.Text.Trim().Length == 0) nameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            };

            RefreshStartup(null);
        }

        List<StartupItem> SelectedStartupItems()
        {
            var list = new List<StartupItem>();
            foreach (object o in _startList.SelectedItems)
            {
                var li = o as ListViewItem;
                var row = li == null ? null : li.Content as StartupRow;
                if (row != null) list.Add(row.Item);
            }
            return list;
        }

        void ChangeSelected(bool enable)
        {
            var items = SelectedStartupItems();
            if (items.Count == 0) { _startStatus.Text = "Select one or more entries first."; return; }
            string firstErr = null;
            foreach (var it in items)
            {
                string err = StartupManager.SetEnabled(it, enable);
                if (err != null && firstErr == null) firstErr = err;
            }
            RefreshStartup(firstErr);
        }

        void RefreshStartup(string message)
        {
            if (_startList == null) return;
            _startList.Items.Clear();
            foreach (var it in StartupManager.List())
            {
                var item = new ListViewItem
                {
                    Content = new StartupRow { Name = it.Name, State = it.Enabled ? "Enabled" : "Disabled", Source = it.SourceText, Command = it.Command, Item = it },
                    ToolTip = it.Command
                };
                if (!it.Enabled) item.Opacity = 0.55;
                _startList.Items.Add(item);
            }
            _startStatus.Text = message ?? (_startList.Items.Count == 0 ? "No startup entries found." : _startList.Items.Count + " startup entries.");
        }

    }
}
