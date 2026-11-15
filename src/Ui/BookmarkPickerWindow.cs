using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Pick a browser and one of its bookmark folders, for a launcher group to keep in sync with.</summary>
    public sealed class BookmarkPickerWindow : Window
    {
        readonly ComboBox _browser = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        readonly TreeView _tree = new TreeView();
        readonly Button _ok = new Button { Content = "Keep in sync", Padding = new Thickness(12, 3, 12, 3), IsDefault = true, IsEnabled = false };
        readonly List<LauncherBookmarks.Profile> _profiles;

        /// <summary>The folder picked (set when the window closes with OK).</summary>
        public BookmarkFolder Picked { get; private set; }
        /// <summary>Its name, for naming a new group after it.</summary>
        public string PickedName { get; private set; }

        public BookmarkPickerWindow()
        {
            Title = "Add bookmarks";
            Width = 420;
            Height = 480;
            MinWidth = 320;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var dock = new DockPanel { Margin = new Thickness(14) };
            Content = dock;

            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            top.Children.Add(new TextBlock { Text = "Browser", Margin = new Thickness(0, 0, 0, 3) });
            top.Children.Add(_browser);
            top.Children.Add(new TextBlock { Text = "Folder (its subfolders' bookmarks are included)", Margin = new Thickness(0, 0, 0, 3) });
            dock.Children.Add(top);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(_ok);
            buttons.Children.Add(cancel);
            dock.Children.Add(buttons);
            dock.Children.Add(_tree);

            _profiles = LauncherBookmarks.Profiles();
            foreach (var p in _profiles) _browser.Items.Add(p.Browser);
            if (_profiles.Count == 0)
            {
                _browser.Items.Add("No browser bookmarks found");
                _browser.IsEnabled = false;
                _tree.Items.Add(new TreeViewItem
                {
                    Header = "Chrome, Edge, Brave, Vivaldi and Opera bookmarks can be synced (Firefox's can't).",
                    IsEnabled = false
                });
            }

            _browser.SelectionChanged += delegate { LoadFolders(); };
            _tree.SelectedItemChanged += delegate { _ok.IsEnabled = SelectedNode() != null; };
            _tree.MouseDoubleClick += delegate { if (SelectedNode() != null) Accept(); };
            _ok.Click += delegate { Accept(); };
            if (_profiles.Count > 0) _browser.SelectedIndex = 0;
        }

        LauncherBookmarks.Node SelectedNode()
        {
            var it = _tree.SelectedItem as TreeViewItem;
            return it == null ? null : it.Tag as LauncherBookmarks.Node;
        }

        void LoadFolders()
        {
            _tree.Items.Clear();
            _ok.IsEnabled = false;
            int i = _browser.SelectedIndex;
            if (i < 0 || i >= _profiles.Count) return;
            var roots = LauncherBookmarks.Read(_profiles[i].File);
            if (roots == null)
            {
                _tree.Items.Add(new TreeViewItem { Header = "Couldn't read this browser's bookmarks (see the log).", IsEnabled = false });
                return;
            }
            foreach (var r in roots)
            {
                if (r.Children.Count == 0) continue;       // an empty "Mobile bookmarks" etc.
                var it = Item(r);
                it.IsExpanded = true;
                _tree.Items.Add(it);
            }
        }

        static TreeViewItem Item(LauncherBookmarks.Node folder)
        {
            int count = LauncherBookmarks.MarksIn(folder).Count;
            var it = new TreeViewItem
            {
                Header = (folder.Name.Length > 0 ? folder.Name : "(unnamed)") + "  (" + count + ")",
                Tag = folder
            };
            foreach (var c in folder.Children) if (c.IsFolder) it.Items.Add(Item(c));
            return it;
        }

        void Accept()
        {
            var n = SelectedNode();
            int i = _browser.SelectedIndex;
            if (n == null || i < 0 || i >= _profiles.Count) return;
            Picked = new BookmarkFolder { File = _profiles[i].File, Browser = _profiles[i].Browser, Guid = n.Guid, Folder = n.Path };
            PickedName = n.Name;
            DialogResult = true;
        }
    }
}
