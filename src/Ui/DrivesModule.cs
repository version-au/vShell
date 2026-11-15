using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Drives (This PC): every drive with its icon, name, a bar for the space used and how much is free - like vXplorer's / Explorer's
    /// This PC. In a widget: a "This PC" heading and a row per drive; on a bar: a chip per drive letter with a small bar (details in the
    /// tooltip). A click opens the drive (the heading: This PC) in vXplorer when it's installed, otherwise File Explorer (module setting).
    /// The drives are read every 30 seconds on a worker thread - a network drive that's gone mustn't hold vShell up.
    /// Settings live in the module's bag: drives.network (bool), drives.text (bool), drives.open ("Automatic" / "vXplorer" / "File Explorer").
    /// </summary>
    public sealed class DrivesModule : Border, IMultiRow
    {
        public static readonly string[] OpenWith = { "Automatic", "vXplorer", "File Explorer" };
        /// <summary>Bars: a used-space bar per drive. Ring: the gauge modules' ring (GaugeFace) per drive - % used inside, the name beside.</summary>
        public static readonly string[] Styles = { "Bars", "Ring" };

        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly StackPanel _list;
        readonly DispatcherTimer _timer;
        string _sig = "";

        public int RowCount { get { return 0; } }   // in a widget: as tall as its drives

        sealed class Drive
        {
            public string Root, Label, Letter;
            public long Total, Free;
            public bool Network;
        }

        public DrivesModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            _list = new StackPanel { Orientation = host.IsWidget ? Orientation.Vertical : Orientation.Horizontal };
            if (host.IsWidget)
            {
                var col = new StackPanel();
                var head = Clickable(new TextBlock
                {
                    Text = string.IsNullOrEmpty(cfg.Title) ? "This PC" : cfg.Title, Foreground = _t.Fg, FontFamily = _t.Font,
                    FontSize = _t.FontSize * 1.05, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4)
                }, "Open This PC", delegate { Open(null); });
                col.Children.Add(head);
                col.Children.Add(_list);
                Child = col;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                MinWidth = 220;
            }
            else
            {
                Child = _list;
                Padding = new Thickness(4, 0, 4, 0);
            }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { Refresh(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
        }

        bool ShowNetwork { get { return _cfg.Bag.Get("drives.network", true); } }
        bool ShowText { get { return _cfg.Bag.Get("drives.text", true); } }
        bool Rings { get { return _cfg.Bag.Get("drives.style", "Bars") == "Ring"; } }
        /// <summary>The ring's diameter as a percentage of the bar / widget row height.</summary>
        double RingSize { get { return Math.Max(30, Math.Min(100, _cfg.Bag.Get("drives.ringsize", 86.0))); } }

        async void Refresh()
        {
            bool network = ShowNetwork;
            List<Drive> drives = await Task.Run(delegate { return Read(network); });
            // only rebuild when something shows differently (no flicker every 30 s)
            var sig = new System.Text.StringBuilder();
            foreach (Drive d in drives) sig.Append(d.Root).Append(d.Label).Append(d.Free / (1L << 26)).Append(d.Total).Append('|');
            if (sig.ToString() == _sig) return;
            _sig = sig.ToString();
            _list.Children.Clear();
            foreach (Drive d in drives) _list.Children.Add(Rings ? Ring(d) : _host.IsWidget ? WidgetRow(d) : BarChip(d));
            if (drives.Count == 0) _list.Children.Add(new TextBlock { Text = "No drives", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize });
            _host.ContentChanged();
        }

        static List<Drive> Read(bool network)
        {
            var r = new List<Drive>();
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    bool net = di.DriveType == DriveType.Network;
                    if (net && !network) continue;
                    if (!di.IsReady) continue;   // an empty card reader / DVD drive
                    string letter = di.Name.Substring(0, 2);
                    string label = ShellName(di.Name);
                    if (string.IsNullOrEmpty(label)) label = (string.IsNullOrEmpty(di.VolumeLabel) ? (net ? "Network drive" : "Local Disk") : di.VolumeLabel) + " (" + letter + ")";
                    r.Add(new Drive { Root = di.Name, Letter = letter, Label = label, Total = di.TotalSize, Free = di.AvailableFreeSpace, Network = net });
                }
                catch { }
            }
            return r;
        }

        // ---- a widget row: icon | name ... free  /  bar ----
        FrameworkElement WidgetRow(Drive d)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var icon = new Image { Width = 26, Height = 26, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, Source = IconUtil.FromPath(d.Root) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new DockPanel();
            if (ShowText && d.Total > 0)
            {
                var free = new TextBlock { Text = Size(d.Free) + " free", Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize * 0.9, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(free, Dock.Right);
                top.Children.Add(free);
            }
            top.Children.Add(new TextBlock { Text = d.Label, Foreground = _t.Fg, FontFamily = _t.Font, FontSize = _t.FontSize, TextTrimming = TextTrimming.CharacterEllipsis });
            right.Children.Add(top);
            right.Children.Add(Meter(d, double.NaN, 6, new Thickness(0, 4, 0, 0)));
            row.Children.Add(right);
            return Clickable(row, Tip(d), delegate { Open(d.Root); });
        }

        // ---- Ring: the gauge modules' ring - % used inside, the name (widget) or letter (bar) beside, the free space under it ----
        FrameworkElement Ring(Drive d)
        {
            double used = d.Total > 0 ? Math.Max(0, Math.Min(100, (d.Total - d.Free) * 100.0 / d.Total)) : 0;
            var face = new GaugeFace(new History());
            face.Setup("Ring", _host, _t, _t.MainAccent, 90, false, false, RingSize, true, 0, 0, StatusColors.Warn as SolidColorBrush);
            face.Show(_host.IsWidget ? d.Label : d.Letter, Math.Round(used).ToString("0"), "%",
                      ShowText && d.Total > 0 ? Size(d.Free) + " free" : null, used, used > 90);
            if (_host.IsWidget) face.Margin = new Thickness(0, 2, 0, 2);
            return Clickable(face, Tip(d), delegate { Open(d.Root); });
        }

        // ---- a bar chip: C: [bar] ----
        FrameworkElement BarChip(Drive d)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 0, 3, 0) };
            row.Children.Add(new TextBlock { Text = d.Letter, Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            row.Children.Add(Meter(d, 34, 6, new Thickness(0)));
            if (ShowText && d.Total > 0)
                row.Children.Add(new TextBlock { Text = Size(d.Free), Foreground = _t.Fg, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
            return Clickable(row, Tip(d), delegate { Open(d.Root); });
        }

        /// <summary>The used-space bar: the accent, or the warning colour when it's over 90 % full.</summary>
        FrameworkElement Meter(Drive d, double width, double height, Thickness margin)
        {
            double used = d.Total > 0 ? Math.Max(0, Math.Min(1, (d.Total - d.Free) / (double)d.Total)) : 0;
            var g = new Grid { Height = height, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
            if (!double.IsNaN(width)) g.Width = width;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, used), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - used), GridUnitType.Star) });
            var track = new Border { Background = _t.Track, CornerRadius = new CornerRadius(height / 2) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            g.Children.Add(new Border { Background = used > 0.9 ? StatusColors.Warn : _t.MainAccent, CornerRadius = new CornerRadius(height / 2) });
            return g;
        }

        string Tip(Drive d)
        {
            return d.Label + (d.Total > 0 ? "\n" + Size(d.Free) + " free of " + Size(d.Total) + " (" + Math.Round((d.Total - d.Free) * 100.0 / d.Total) + "% used)" : "") +
                   "\nClick to open it in " + (UseVXplorer() ? "vXplorer" : "File Explorer");
        }

        FrameworkElement Clickable(FrameworkElement content, string tip, Action click)
        {
            var b = new Border { Child = content, Background = Brushes.Transparent, CornerRadius = new CornerRadius(5), Padding = new Thickness(4, 2, 4, 2), Cursor = Cursors.Hand, ToolTip = tip };
            b.MouseEnter += delegate { b.Background = _t.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        // ---- opening: vXplorer when it's installed (or chosen), else File Explorer ----

        bool UseVXplorer()
        {
            string how = _cfg.Bag.Get("drives.open", "Automatic");
            if (how == "File Explorer") return false;
            return VXplorerExe() != null;
        }

        /// <summary>vXplorer.exe: where its installer put it (the uninstall entry), or the usual place; null when it isn't installed.</summary>
        public static string VXplorerExe()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{9C3E5B71-2A4D-4F8E-B6C1-5E7A9D2F0B84}_is1"))
                {
                    string dir = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "vXplorer.exe"))) return Path.Combine(dir, "vXplorer.exe");
                }
            }
            catch { }
            string usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "vXplorer", "vXplorer.exe");
            return File.Exists(usual) ? usual : null;
        }

        /// <summary>A drive's root, or null for This PC.</summary>
        void Open(string root)
        {
            try
            {
                string vx = UseVXplorer() ? VXplorerExe() : null;
                var psi = vx != null ? new ProcessStartInfo(vx) : new ProcessStartInfo("explorer.exe");
                psi.UseShellExecute = false;
                psi.ArgumentList.Add(root ?? (vx != null ? "This PC" : "shell:MyComputerFolder"));
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("Drives: " + ex.Message); }
        }

        // ---- helpers ----

        static string Size(long bytes)
        {
            double v = bytes;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (v < 10 && i > 0 ? v.ToString("0.0") : v.ToString("0")) + " " + u[i];
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        /// <summary>The name Explorer shows ("Local Disk (C:)", "storage (\\NAS) (S:)").</summary>
        static string ShellName(string root)
        {
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo(root, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(), 0x200 /* SHGFI_DISPLAYNAME */);
                return info.szDisplayName;
            }
            catch { return null; }
        }
    }
}
