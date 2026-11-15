using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Edit a launcher module's shortcuts and groups (a group is a named list of shortcuts that opens a popup menu).</summary>
    public sealed class LauncherEditorWindow : Window
    {
        sealed class Node
        {
            public LauncherEntry Entry;
            public List<LauncherEntry> Parent;
        }

        readonly List<LauncherEntry> _entries;
        readonly Action _changed;
        readonly TreeView _tree = new TreeView();
        readonly TextBox _name = new TextBox { Padding = new Thickness(3) };
        readonly TextBox _path = new TextBox { Padding = new Thickness(3) };
        readonly TextBox _args = new TextBox { Padding = new Thickness(3) };
        readonly TextBox _icon = new TextBox { Padding = new Thickness(3) };
        readonly System.Windows.Controls.Image _iconPreview = new System.Windows.Controls.Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
        readonly ComboBox _group = new ComboBox();
        readonly StackPanel _shortcutOnly = new StackPanel();
        readonly StackPanel _groupOnly = new StackPanel();
        readonly TextBlock _syncInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly Button _stopSync = new Button { Content = "Stop syncing", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel _groupIcons = new StackPanel();
        readonly WrapPanel _glyphs = new WrapPanel();
        readonly CheckBox _glyphAccent = new CheckBox { Content = "In the theme's accent colour (otherwise its text colour)", Margin = new Thickness(0, 6, 0, 0) };
        readonly StackPanel _edit = new StackPanel { Margin = new Thickness(12, 0, 0, 0), IsEnabled = false };
        bool _loading;

        public LauncherEditorWindow(List<LauncherEntry> entries, Action changed, string title = null)
        {
            _entries = entries;
            _changed = changed;

            Title = title ?? "Launcher shortcuts and groups";
            if (LauncherFolders.SyncAll(_entries)) _changed();     // synced groups up to date before editing
            Width = 780;
            Height = 620;
            MinWidth = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = grid;

            // ---- left: tree + buttons ---------------------------------------------
            var left = new DockPanel();
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var add = Btn("Add shortcuts…");
            add.ToolTip = "Programs, shortcuts or files - several at once with Ctrl or Shift in the file dialog";
            var addGroup = Btn("Add group");
            var addFolder = Btn("Add folder…");
            addFolder.ToolTip = "Keep a group in sync with a folder of shortcuts (subfolders included): the selected group, or a new group named after the folder";
            var addMarks = Btn("Add bookmarks…");
            addMarks.ToolTip = "Keep a group in sync with a browser bookmarks folder (Chrome, Edge, Brave, Vivaldi, Opera): the selected group, or a new group named after the folder";
            var remove = Btn("Remove");
            var up = Btn("Move up");
            var down = Btn("Move down");
            buttons.Children.Add(add);
            buttons.Children.Add(addGroup);
            buttons.Children.Add(addFolder);
            buttons.Children.Add(addMarks);
            buttons.Children.Add(remove);
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            left.Children.Add(buttons);
            left.Children.Add(_tree);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            // ---- right: editor -----------------------------------------------------------
            _edit.Children.Add(Lbl("Name"));
            _edit.Children.Add(_name);

            _shortcutOnly.Children.Add(Lbl("Path, URL or command"));
            var pathRow = new DockPanel();
            var browse = new Button { Content = "Browse…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            pathRow.Children.Add(browse);
            pathRow.Children.Add(_path);
            _shortcutOnly.Children.Add(pathRow);
            _shortcutOnly.Children.Add(Lbl("Arguments (optional)"));
            _shortcutOnly.Children.Add(_args);
            _edit.Children.Add(_shortcutOnly);
            _edit.Children.Add(Lbl("In group"));
            _edit.Children.Add(_group);

            _groupOnly.Children.Add(Lbl("Synced folders and bookmarks"));
            _groupOnly.Children.Add(_syncInfo);
            _groupOnly.Children.Add(_stopSync);
            _edit.Children.Add(_groupOnly);
            _stopSync.ToolTip = "The group keeps the shortcuts it has now, but no longer follows the folder(s) or bookmarks";
            _stopSync.Click += delegate
            {
                Node n = Selected();
                if (n == null || !n.Entry.IsGroup) return;
                if (n.Entry.SyncFolders != null) n.Entry.SyncFolders.Clear();
                if (n.Entry.SyncBookmarks != null) n.Entry.SyncBookmarks.Clear();
                foreach (var c in n.Entry.Children) c.BookmarkName = "";      // its bookmarks become ordinary shortcuts
                LoadEdit();
                Populate(n.Entry);
                _changed();
            };

            // built-in group icons: glyphs drawn in the bar's theme colours
            _groupIcons.Children.Add(Lbl("Icon (follows the theme's colours)"));
            _glyphs.Children.Add(GlyphButton("", "Default (folder)", IconUtil.GlyphFolder));
            foreach (var g in IconUtil.GroupIcons) _glyphs.Children.Add(GlyphButton(g.Item1, g.Item2, g.Item3));
            _groupIcons.Children.Add(new ScrollViewer { Content = _glyphs, MaxHeight = 152, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _groupIcons.Children.Add(_glyphAccent);
            _edit.Children.Add(_groupIcons);
            RoutedEventHandler accent = delegate
            {
                if (_loading) return;
                Node n = Selected();
                if (n == null || !n.Entry.IsGroup) return;
                n.Entry.IconAccent = _glyphAccent.IsChecked == true;
                _changed();
            };
            _glyphAccent.Checked += accent;
            _glyphAccent.Unchecked += accent;

            _edit.Children.Add(Lbl("Custom icon (optional - .ico, .png, .jpg, or a program to take the icon from)"));
            var iconRow = new DockPanel();
            var iconClear = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            var iconBrowse = new Button { Content = "Browse…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(iconClear, Dock.Right);
            DockPanel.SetDock(iconBrowse, Dock.Right);
            DockPanel.SetDock(_iconPreview, Dock.Left);
            iconRow.Children.Add(iconClear);
            iconRow.Children.Add(iconBrowse);
            iconRow.Children.Add(_iconPreview);
            iconRow.Children.Add(_icon);
            _edit.Children.Add(iconRow);
            iconBrowse.Click += delegate { BrowseForIcon(); };
            iconClear.Click += delegate { _icon.Text = ""; };
            _icon.TextChanged += delegate
            {
                UpdateIconPreview();
                if (_loading) return;
                Node n = Selected();
                if (n == null) return;
                n.Entry.IconPath = _icon.Text.Trim();
                _changed();
            };

            _edit.Children.Add(new TextBlock
            {
                Text = "Groups open a popup menu of their shortcuts when clicked on the bar. Select a group and use \"Add shortcuts…\" to put shortcuts into it (several at once with Ctrl or Shift), " +
                       "or change a shortcut's group above. \"Add group\" with a group selected puts the new group inside it - it opens in the same menu, with a Back row " +
                       "(up to " + LauncherEntry.MaxDepth + " levels). A group shows its icon from the list above (a folder by default); a custom icon takes its place.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 14, 0, 0)
            });
            var editScroll = new ScrollViewer { Content = _edit, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(editScroll, 1);
            grid.Children.Add(editScroll);

            // ---- events ----------------------------------------------------------------------
            _tree.SelectedItemChanged += delegate { LoadEdit(); };
            add.Click += delegate { AddShortcuts(); };
            addGroup.Click += delegate { AddGroup(); };
            addFolder.Click += delegate { AddFolder(); };
            addMarks.Click += delegate { AddBookmarks(); };
            remove.Click += delegate { RemoveSelected(); };
            up.Click += delegate { MoveSelected(-1); };
            down.Click += delegate { MoveSelected(1); };
            browse.Click += delegate { BrowseForPath(); };

            _name.TextChanged += delegate
            {
                if (_loading) return;
                Node n = Selected();
                if (n == null) return;
                n.Entry.Name = _name.Text;
                var item = _tree.SelectedItem as TreeViewItem;
                if (item != null) item.Header = Title2(n.Entry);
                _changed();
            };
            _path.TextChanged += delegate
            {
                if (_loading) return;
                Node n = Selected();
                if (n == null || n.Entry.IsGroup) return;
                n.Entry.Path = _path.Text;
                var item = _tree.SelectedItem as TreeViewItem;
                if (item != null) item.Header = Title2(n.Entry);
                _changed();
            };
            _args.TextChanged += delegate
            {
                if (_loading) return;
                Node n = Selected();
                if (n == null || n.Entry.IsGroup) return;
                n.Entry.Args = _args.Text;
                _changed();
            };
            _group.SelectionChanged += delegate { if (!_loading) MoveToGroup(); };

            Closing += delegate { _changed(); };

            Populate(null);
        }

        static Button Btn(string text)
        {
            return new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4) };
        }

        static TextBlock Lbl(string text)
        {
            return new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 3) };
        }

        static string Title2(LauncherEntry e)
        {
            if (e.IsGroup) return (string.IsNullOrEmpty(e.Name) ? "(unnamed group)" : e.Name) +
                                  (IsSynced(e) ? "  [group, synced]" : "  [group]");
            string n = string.IsNullOrEmpty(e.Name) ? "(unnamed)" : e.Name;
            return string.IsNullOrEmpty(e.Path) ? n : n + "   -   " + e.Path;
        }

        Button GlyphButton(string key, string name, string glyph)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = IconUtil.GlyphFont, FontSize = 18 },
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 4, 4),
                ToolTip = name,
                Tag = key
            };
            b.Click += delegate
            {
                Node n = Selected();
                if (n == null || !n.Entry.IsGroup) return;
                n.Entry.IconGlyph = key;
                if (key.Length > 0 && _icon.Text.Length > 0) _icon.Text = "";      // the custom icon would hide it
                MarkGlyph(key);
                _changed();
            };
            return b;
        }

        /// <summary>Show which built-in icon the group has.</summary>
        void MarkGlyph(string key)
        {
            if (IconUtil.GroupGlyph(key) == null) key = "";
            foreach (var c in _glyphs.Children)
            {
                var b = c as Button;
                if (b == null) continue;
                bool on = (string)b.Tag == key;
                b.BorderThickness = new Thickness(on ? 2 : 1);
                b.BorderBrush = on ? SystemColors.HighlightBrush : SystemColors.ControlDarkBrush;
            }
        }

        static bool IsSynced(LauncherEntry e)
        {
            return (e.SyncFolders != null && e.SyncFolders.Count > 0) || (e.SyncBookmarks != null && e.SyncBookmarks.Count > 0);
        }

        // ---- tree ------------------------------------------------------------------------------
        Node Selected()
        {
            var item = _tree.SelectedItem as TreeViewItem;
            return item == null ? null : item.Tag as Node;
        }

        TreeViewItem MakeItem(LauncherEntry e, List<LauncherEntry> parent)
        {
            return new TreeViewItem { Header = Title2(e), Tag = new Node { Entry = e, Parent = parent } };
        }

        void Populate(LauncherEntry select)
        {
            bool old = _loading;
            _loading = true;
            _tree.Items.Clear();
            TreeViewItem pick = null;
            AddItems(_tree.Items, _entries, select, ref pick);
            _loading = old;
            if (pick != null) pick.IsSelected = true;
            else LoadEdit();
        }

        void AddItems(ItemCollection into, List<LauncherEntry> list, LauncherEntry select, ref TreeViewItem pick)
        {
            foreach (var e in list)
            {
                TreeViewItem it = MakeItem(e, list);
                if (e == select) pick = it;
                into.Add(it);
                if (e.IsGroup)
                {
                    it.IsExpanded = true;
                    AddItems(it.Items, e.Children, select, ref pick);
                }
            }
        }

        // ---- groups inside groups ----------------------------------------------------------------
        /// <summary>The group whose Children this list is (null for the top level).</summary>
        LauncherEntry OwnerOf(List<LauncherEntry> list) { int level; return OwnerOf(_entries, list, 1, out level); }

        static LauncherEntry OwnerOf(List<LauncherEntry> within, List<LauncherEntry> list, int depth, out int level)
        {
            level = 0;
            foreach (var e in within)
            {
                if (!e.IsGroup) continue;
                if (e.Children == list) { level = depth; return e; }
                LauncherEntry inner = OwnerOf(e.Children, list, depth + 1, out level);
                if (inner != null) return inner;
            }
            return null;
        }

        /// <summary>How deep a list is: 0 = the top level, 1 = inside a top-level group …</summary>
        int LevelOf(List<LauncherEntry> list)
        {
            if (list == _entries) return 0;
            int level;
            OwnerOf(_entries, list, 1, out level);
            return level;
        }

        /// <summary>How many levels of groups a group is, counting itself (1 = no groups inside).</summary>
        static int Levels(LauncherEntry g)
        {
            int h = 0;
            foreach (var c in g.Children) if (c.IsGroup) h = Math.Max(h, Levels(c));
            return h + 1;
        }

        static bool Contains(LauncherEntry g, LauncherEntry inner)
        {
            foreach (var c in g.Children) if (c == inner || (c.IsGroup && Contains(c, inner))) return true;
            return false;
        }

        /// <summary>Every group, outer ones first, with a name like "Games › Steam" and its level (1 = top level).</summary>
        static void AllGroups(List<LauncherEntry> list, string prefix, int level, List<Tuple<LauncherEntry, string, int>> into)
        {
            foreach (var e in list)
            {
                if (!e.IsGroup) continue;
                string name = prefix + (string.IsNullOrEmpty(e.Name) ? "(unnamed group)" : e.Name);
                into.Add(Tuple.Create(e, name, level));
                AllGroups(e.Children, name + " › ", level + 1, into);
            }
        }

        // where each "In group" choice puts the selected entry
        readonly List<List<LauncherEntry>> _groupTargets = new List<List<LauncherEntry>>();

        void LoadEdit()
        {
            Node n = Selected();
            _edit.IsEnabled = n != null;
            if (n == null) return;

            bool old = _loading;
            _loading = true;
            _name.Text = n.Entry.Name;
            _icon.Text = n.Entry.IconPath ?? "";
            UpdateIconPreview();
            _shortcutOnly.Visibility = n.Entry.IsGroup ? Visibility.Collapsed : Visibility.Visible;
            _groupOnly.Visibility = n.Entry.IsGroup ? Visibility.Visible : Visibility.Collapsed;
            _groupIcons.Visibility = _groupOnly.Visibility;
            if (n.Entry.IsGroup)
            {
                MarkGlyph(n.Entry.IconGlyph);
                _glyphAccent.IsChecked = n.Entry.IconAccent;
                var lines = new List<string>();
                if (n.Entry.SyncFolders != null) lines.AddRange(n.Entry.SyncFolders);
                if (n.Entry.SyncBookmarks != null)
                    foreach (var b in n.Entry.SyncBookmarks) lines.Add(b.Browser + " bookmarks: " + b.Folder);
                bool synced = lines.Count > 0;
                _syncInfo.Text = synced
                    ? string.Join("\n", lines) + "\n\nNew shortcuts and bookmarks are added and deleted ones removed whenever the menu opens " +
                      "(bookmarks in the browser's order, after the folders' shortcuts). Shortcuts you add by hand stay; renaming one or giving it an icon is kept."
                    : "None. Select this group and use \"Add folder…\" or \"Add bookmarks…\" to keep it in sync with a folder or a browser bookmarks folder.";
                _stopSync.Visibility = synced ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!n.Entry.IsGroup)
            {
                _path.Text = n.Entry.Path;
                _args.Text = n.Entry.Args;
            }

            // "In group": the top level and every group it can go into (a group not into itself, nor deeper than MaxDepth)
            _group.Items.Clear();
            _groupTargets.Clear();
            _group.Items.Add("(top level)");
            _groupTargets.Add(_entries);
            int sel = 0;
            int height = n.Entry.IsGroup ? Levels(n.Entry) : 0;
            var groups = new List<Tuple<LauncherEntry, string, int>>();
            AllGroups(_entries, "", 1, groups);
            foreach (var g in groups)
            {
                if (n.Entry.IsGroup && (g.Item1 == n.Entry || Contains(n.Entry, g.Item1) || g.Item3 + height > LauncherEntry.MaxDepth)) continue;
                if (g.Item1.Children == n.Parent) sel = _group.Items.Count;
                _group.Items.Add(g.Item2);
                _groupTargets.Add(g.Item1.Children);
            }
            _group.SelectedIndex = sel;
            _loading = old;
        }

        // ---- actions ----------------------------------------------------------------------------
        void AddShortcuts()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Choose programs, shortcuts or files (several at once: Ctrl or Shift)";
            dlg.Filter = "All files (*.*)|*.*";
            dlg.Multiselect = true;
            // the shortcut itself, not what it points at: its own name, icon and arguments (and several shortcuts to one program stay several)
            dlg.DereferenceLinks = false;
            if (dlg.ShowDialog(this) != true) return;

            // where to put them: inside the selected group, or next to the selected shortcut, or at the top level
            Node n = Selected();
            List<LauncherEntry> target = _entries;
            int insertAt = -1;
            if (n != null)
            {
                if (n.Entry.IsGroup) target = n.Entry.Children;
                else { target = n.Parent; insertAt = target.IndexOf(n.Entry) + 1; }
            }

            LauncherEntry last = null;
            foreach (string f in dlg.FileNames)
            {
                var e = new LauncherEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f };
                if (insertAt >= 0 && insertAt <= target.Count) { target.Insert(insertAt, e); insertAt++; }
                else target.Add(e);
                last = e;
            }
            Populate(last);
            _changed();
        }

        /// <summary>
        /// Keep a group in sync with a folder of shortcuts (subfolders included): into the selected group, or else a new group named
        /// after the folder. New shortcuts in the folder appear and deleted ones go whenever the group's menu opens.
        /// </summary>
        void AddFolder()
        {
            string folder;
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Choose a folder of shortcuts to keep in sync (e.g. a Start Menu folder or a folder of your own .lnk files)";
                dlg.UseDescriptionForTitle = true;
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                folder = dlg.SelectedPath;
            }
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            if (LauncherFolders.Scan(folder).Count == 0 &&
                MessageBox.Show(this, "There are no shortcuts (.lnk, .url, .exe) in that folder right now. Keep it in sync anyway?", "Add folder",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            string gname = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            LauncherEntry group = TargetGroup(string.IsNullOrEmpty(gname) ? folder : gname);
            if (group.SyncFolders == null) group.SyncFolders = new List<string>();
            bool have = false;
            foreach (string f in group.SyncFolders) if (string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) have = true;
            if (!have) group.SyncFolders.Add(folder);
            LauncherFolders.Sync(group);
            Populate(group);
            _changed();
        }

        /// <summary>The group to sync into: the selected group (or the group of the selected shortcut), or else a new group with this name.</summary>
        LauncherEntry TargetGroup(string newName)
        {
            Node n = Selected();
            LauncherEntry group = null;
            if (n != null && n.Entry.IsGroup) group = n.Entry;
            else if (n != null && n.Parent != _entries) group = OwnerOf(n.Parent);     // a shortcut inside a group
            if (group == null)
            {
                group = new LauncherEntry { IsGroup = true, Name = newName };
                _entries.Add(group);
            }
            return group;
        }

        /// <summary>
        /// Keep a group in sync with a browser bookmarks folder (subfolders included): into the selected group, or else a new group named
        /// after the folder. New bookmarks appear and deleted ones go whenever the group's menu opens.
        /// </summary>
        void AddBookmarks()
        {
            var dlg = new BookmarkPickerWindow { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Picked == null) return;
            BookmarkFolder picked = dlg.Picked;

            LauncherEntry group = TargetGroup(string.IsNullOrEmpty(dlg.PickedName) ? "Bookmarks" : dlg.PickedName);
            if (group.SyncBookmarks == null) group.SyncBookmarks = new List<BookmarkFolder>();
            bool have = false;
            foreach (var b in group.SyncBookmarks)
                if (string.Equals(b.File, picked.File, StringComparison.OrdinalIgnoreCase) &&
                    (picked.Guid.Length > 0 ? string.Equals(b.Guid, picked.Guid, StringComparison.OrdinalIgnoreCase) : b.Folder == picked.Folder)) have = true;
            if (!have) group.SyncBookmarks.Add(picked);
            LauncherFolders.Sync(group);
            Populate(group);
            _changed();
        }

        /// <summary>A new group: inside the selected group, next to the selected shortcut, or at the top level.</summary>
        void AddGroup()
        {
            var g = new LauncherEntry { IsGroup = true, Name = "New group" };
            Node n = Selected();
            List<LauncherEntry> target = _entries;
            int insertAt = -1;
            if (n != null)
            {
                if (n.Entry.IsGroup) target = n.Entry.Children;
                else { target = n.Parent; insertAt = target.IndexOf(n.Entry) + 1; }
            }
            if (LevelOf(target) >= LauncherEntry.MaxDepth)
            {
                MessageBox.Show(this, "Groups go " + LauncherEntry.MaxDepth + " levels deep at most - this group can hold shortcuts, but no more groups.",
                                "Add group", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (insertAt >= 0 && insertAt <= target.Count) target.Insert(insertAt, g);
            else target.Add(g);
            Populate(g);
            _changed();
            _name.Focus();
            _name.SelectAll();
        }

        void RemoveSelected()
        {
            Node n = Selected();
            if (n == null) return;
            n.Parent.Remove(n.Entry);
            Populate(null);
            _changed();
        }

        void MoveSelected(int delta)
        {
            Node n = Selected();
            if (n == null) return;
            int i = n.Parent.IndexOf(n.Entry);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= n.Parent.Count) return;
            n.Parent.RemoveAt(i);
            n.Parent.Insert(j, n.Entry);
            Populate(n.Entry);
            _changed();
        }

        void MoveToGroup()
        {
            Node n = Selected();
            if (n == null) return;
            int gi = _group.SelectedIndex;
            if (gi < 0 || gi >= _groupTargets.Count) return;
            List<LauncherEntry> target = _groupTargets[gi];
            if (target == null || target == n.Parent) return;

            n.Parent.Remove(n.Entry);
            target.Add(n.Entry);
            Populate(n.Entry);
            _changed();
        }

        void UpdateIconPreview()
        {
            _iconPreview.Source = IconUtil.FromIconFile(_icon.Text);
        }

        void BrowseForIcon()
        {
            Node n = Selected();
            if (n == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Choose an icon";
            dlg.Filter = "Icons and images (*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif|Programs and libraries (*.exe;*.dll)|*.exe;*.dll|All files (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            _icon.Text = dlg.FileName;
        }

        void BrowseForPath()
        {
            Node n = Selected();
            if (n == null || n.Entry.IsGroup) return;
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "All files (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            _path.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }
}
