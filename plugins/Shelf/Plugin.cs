using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("shelf", "Shelf",
    Description = "A place to park files, folders, text and pictures while you work - drop them on a shelf, drag them out to copy or move them - plus Windows' clipboard history (Win+V) as a tab.",
    Glyph = "\uE8F1", Group = "Desktop", Author = "version", Adds = "Add to shelf in Explorer's right-click menu, the Clipboard tab", Types = "Shelf")]

namespace VShellShelf
{
    /// <summary>
    /// The Shelf plugin - made only with vShell's plugin API: the Shelf module (a bar button with a popup, or a widget), "Add to shelf"
    /// in Explorer's right-click menu (vShell.exe --plugin shelf add "path"), and Windows' clipboard history as a tab.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        public const string Type = "Shelf";
        static BarManager _mgr;

        public void Load(PluginHost host)
        {
            _mgr = host.Manager;
            ShelfSettings.Current = ShelfSettings.Of(host.Manager.Settings);
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Shelf", ShortName = "Shelf", Glyph = "\uE8F1", Group = "Shell",
                Blurb = "park files, text and pictures; clipboard history", HasPopup = true, WidgetSize = new Size(340, 0),
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg)
                {
                    return h.IsWidget ? (FrameworkElement)new ShelfWidgetModule(mgr, h, cfg) : new ShelfBarModule(mgr, h, cfg);
                },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.AddIntegration("Shelf", SettingsSection);
            host.OnSettings(Apply);
            host.AddCommandLine("add", AddFromCommandLine);
        }

        public void Unload()
        {
            EdgeTabs.Stop();
            ClipHistory.StopWatching();
            ExplorerMenu.Remove();
            ShelfStore.SaveNow();
        }

        /// <summary>At start and on every settings change: the Explorer menu and the clipboard watch as set.</summary>
        static void Apply(BarSettings s)
        {
            ShelfSettings k = ShelfSettings.Of(s);
            ShelfSettings.Current = k;
            if (k.ExplorerMenu) ExplorerMenu.Add(); else ExplorerMenu.Remove();
            if (k.ClipTab) ClipHistory.Watch(); else ClipHistory.StopWatching();
            if (_mgr != null) EdgeTabs.Apply(_mgr);
        }

        // =================================================================
        // vShell.exe --plugin shelf add "path" … (Explorer's "Add to shelf": one start per file)
        // =================================================================
        static int _added;
        static DispatcherTimer _addedTimer;

        static void AddFromCommandLine(string[] args)
        {
            var paths = new List<string>();
            foreach (string a0 in args)
            {
                // "C:\" on a command line arrives as C:" (the backslash escapes the quote)
                string a = a0.EndsWith("\"") ? a0.Substring(0, a0.Length - 1) + "\\" : a0;
                if (a.Length == 2 && a[1] == ':') a += "\\";
                if (a.Trim().Length > 0) paths.Add(a.Trim());
            }
            if (paths.Count == 0) return;
            Shelf s = ShelfStore.Default(ShelfSettings.Current != null ? ShelfSettings.Current.AddTo : null);
            int n = ShelfStore.Add(s, paths);
            Log.Write("Shelf: " + n + " added from Explorer to " + s.Name);
            // nothing shows the shelves: say where they went (once for a whole selection - Explorer starts vShell once per file)
            if (n == 0 || _mgr == null || _mgr.Settings.UsesModule(Type)) return;
            _added += n;
            if (_addedTimer == null)
            {
                _addedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _addedTimer.Tick += delegate
                {
                    _addedTimer.Stop();
                    int count = _added;
                    _added = 0;
                    Notify.Post("Shelf", count == 1 ? "Added to your shelf" : count + " added to your shelf",
                                "Add a Shelf module to a bar or a widget (Settings > Bars or Widgets > Add a module) to see your shelves.", "", NotifyLevel.Normal);
                };
            }
            _addedTimer.Stop();
            _addedTimer.Start();
        }

        // =================================================================
        // Settings
        // =================================================================
        static SettingsWindow.SettingsPage _shown;

        /// <summary>A setting changed outside Settings (the drawer's pin): the Shelf page shows it, if it's open.</summary>
        internal static void SettingsShown()
        {
            SettingsWindow.SettingsPage p = _shown;
            if (p != null && p.Window != null && p.Window.IsVisible) p.Redraw();
        }

        static void SettingsSection(SettingsWindow.SettingsPage p)
        {
            _shown = p;
            ShelfSettings k = ShelfSettings.Of(p.Settings);
            p.Header("Shelf");
            p.Check("\"Add to shelf\" in Explorer's right-click menu", delegate { return k.ExplorerMenu; }, delegate (bool v) { k.ExplorerMenu = v; p.Changed(); });
            p.Note("For files, folders and drives. In Windows 11 it's under Show more options (Shift+right-click shows it straight away).");
            var names = new List<string>();
            foreach (Shelf s in ShelfStore.Shelves) names.Add(s.Name);
            p.Combo("Explorer and dropping on the bar button add to", names.ToArray(), delegate { return ShelfStore.Default(k.AddTo).Name; }, delegate (string v)
            {
                Shelf s = ShelfStore.Shelves.Find(delegate (Shelf x) { return x.Name == v; });
                k.AddTo = s != null ? s.Id : "";
                p.Changed();
            });
            p.Combo("Dragging something out of a shelf", new[] { "Copies it", "Moves it", "Does what Explorer does" },
                delegate { return k.DragOut == "Moves" ? "Moves it" : k.DragOut == "Explorer" ? "Does what Explorer does" : "Copies it"; },
                delegate (string v) { k.DragOut = v == "Moves it" ? "Moves" : v.StartsWith("Does") ? "Explorer" : "Copies"; p.Changed(); });
            p.Note("Holding Shift while you drop always moves, Ctrl copies and Alt makes a shortcut. Explorer on its own moves within a drive and copies to another one. " +
                   "What's moved leaves the shelf; a Pocket lets go of everything you drop somewhere.");
            p.Check("A Clipboard tab with Windows' clipboard history (Win+V)", delegate { return k.ClipTab; }, delegate (bool v) { k.ClipTab = v; p.Changed(); });
            if (k.ClipTab)
                p.Check("Close the bar's popup after copying something from it", delegate { return k.CloseAfterCopy; }, delegate (bool v) { k.CloseAfterCopy = v; p.Changed(); });
            p.Note("The Clipboard tab shows what Windows keeps (text and pictures, the last 25). Windows' clipboard history has to be on: Settings > System > Clipboard. " +
                   "Click one to copy it again, or keep it on a shelf (right-click).");

            p.Header("Edge tabs");
            p.Check("Shelf tabs on a screen edge", delegate { return k.EdgeOn; }, delegate (bool v) { k.EdgeOn = v; p.Changed(); p.Redraw(); });
            p.Note("A thin strip on the edge, a colour for each shelf. Drag something to it (or start dragging, or rest the pointer on it) and the tabs slide out: " +
                   "drop on a tab to keep it there, click a tab - or hold a drag over it - to open that shelf. On an edge with a vShell bar the tabs hang from the bar.");
            if (!k.EdgeOn) return;
            List<Monitors.Mon> mons = Monitors.All();
            var monNames = new List<string>();
            foreach (Monitors.Mon m in mons) monNames.Add(Monitors.Label(m));
            p.Combo("Monitor", monNames.ToArray(), delegate
            {
                Monitors.Mon m = Monitors.Find(k.EdgeMonitor);
                return m != null ? Monitors.Label(m) : (monNames.Count > 0 ? monNames[0] : "");
            }, delegate (string v)
            {
                Monitors.Mon m = mons.Find(delegate (Monitors.Mon x) { return Monitors.Label(x) == v; });
                k.EdgeMonitor = m == null || m.Primary ? "" : m.Device;
                p.Changed();
            });
            p.Combo("Edge", new[] { "Top", "Bottom", "Left", "Right" }, delegate { return k.EdgeSide; }, delegate (string v) { k.EdgeSide = v; p.Changed(); });
            p.Combo("Along the edge", new[] { "Start", "Centre", "End" }, delegate { return k.EdgeAlign; }, delegate (string v) { k.EdgeAlign = v; p.Changed(); });
            p.Note("Start is the top of a side edge or the left of the top / bottom edge. Away from the middle of the right edge if you use scrollbars there a lot.");
            p.Combo("Tab style", new[] { "Index tabs", "Ribbon" }, delegate { return k.EdgeStyle; }, delegate (string v) { k.EdgeStyle = v; p.Changed(); });
            p.Note("Index tabs: each shelf's name on its tab. Ribbon: slim colour bars - the one under the pointer opens to show its name.");
            p.Check("Slide out as soon as a drag starts", delegate { return k.EdgeOnDrag; }, delegate (bool v) { k.EdgeOnDrag = v; p.Changed(); });
            p.Note("When you start dragging files in Explorer or on the desktop, or text or a picture in a browser, the tabs come out so you can drop straight on them. " +
                   "Off: they come out when the drag reaches the strip. vShell watches the mouse for this (nothing is kept).");
            string[] springs = { "Quickly (0.3 s)", "0.65 s", "1 s", "1.5 s", "2 s", "3 s", "5 s", "Never" };
            int[] springMs = { 300, 650, 1000, 1500, 2000, 3000, 5000, 0 };
            p.Combo("Open a shelf when a drag rests on its tab", springs, delegate
            {
                int ms = k.EdgeSpringMs, best = 0;
                for (int i = 1; i < springMs.Length; i++) if (Math.Abs(springMs[i] - ms) < Math.Abs(springMs[best] - ms)) best = i;
                return springs[best];
            }, delegate (string v) { k.EdgeSpringMs = springMs[Array.IndexOf(springs, v)]; p.Changed(); });
            p.Note("How long you hold a drag on a tab (the one that grows) before its shelf opens, so you can drop onto its list. " +
                   "Longer - or Never - and you can drop straight onto the tab without the shelf opening.");
            p.Check("Slide out when the pointer rests on the strip", delegate { return k.EdgeOnHover; }, delegate (bool v) { k.EdgeOnHover = v; p.Changed(); });
            p.Check("An opened shelf stays open until you close it", delegate { return k.EdgeStayOpen; }, delegate (bool v) { k.EdgeStayOpen = v; p.Changed(); });
            p.Note("Off: clicking anywhere else closes it, as the bar's popup does. The pin in its corner switches this too. Esc or X always closes it.");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            ShelfModuleView v = ShelfModuleView.Of(cfg);
            const string All = "Every shelf (tabs)", Clip = "Clipboard history";
            var shows = new List<string> { All };
            foreach (Shelf s in ShelfStore.Shelves) shows.Add(s.Name);
            if (ShelfSettings.Current == null || ShelfSettings.Current.ClipTab) shows.Add(Clip);
            p.Combo(p.IsWidget ? "Show" : "Its popup shows", shows.ToArray(), delegate
            {
                if (v.Show == ShelfView.ClipTab) return Clip;
                Shelf s = v.Show.Length > 0 ? ShelfStore.Find(v.Show) : null;
                return s != null ? s.Name : All;
            }, delegate (string val)
            {
                Shelf s = ShelfStore.Shelves.Find(delegate (Shelf x) { return x.Name == val; });
                v.Show = val == Clip ? ShelfView.ClipTab : s != null ? s.Id : "";
                p.Changed();
            });
            p.Slider("Rows tall", 3, 20, delegate { return v.Rows; }, delegate (double d) { v.Rows = (int)Math.Round(d); p.Changed(); });
            if (!p.IsWidget)
            {
                p.Check("Show how many things are on the shelves", delegate { return v.ShowCount; }, delegate (bool b) { v.ShowCount = b; p.Changed(); });
                p.Title(cfg, "");
            }
            p.Note((p.IsWidget ? "" : "Click it for your shelves; drop something on it to keep it (or hold the drag there a moment and the shelves open). ") +
                   "Drag files out to copy them (Shift moves them), double-click opens one, right-click for more. Tabs: right-click a shelf to rename it, " +
                   "colour it, make it a Pocket or delete it. More in Settings > Plugins > Shelf.");
            bool edge = ShelfSettings.Current != null && ShelfSettings.Current.EdgeOn;
            p.Button(edge ? "Edge tabs: your shelves on a screen edge (on)" : "Edge tabs: your shelves as tabs on a screen edge", edge ? "Edge tabs settings ›" : "Set up edge tabs ›",
                     delegate { p.OpenPlugin("shelf", "Edge tabs"); });
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            var g = ModulePics.T(t, "\uE8F1", t.Fg, 14);
            g.FontFamily = IconUtil.GlyphFont;
            return ModulePics.Row(6, g, ModulePics.T(t, "5", t.Fg, 13));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Func<string, string, string, UIElement> row = delegate (string glyph, string name, string sub)
            {
                var gl = ModulePics.T(t, glyph, t.FgDim, 16);
                gl.FontFamily = IconUtil.GlyphFont;
                return ModulePics.Row(8, gl, ModulePics.Col(0, ModulePics.T(t, name, t.Fg, 12), ModulePics.T(t, sub, t.FgDim, 10)));
            };
            return ModulePics.Col(5,
                ModulePics.Row(6, ModulePics.Dot(new System.Windows.Media.SolidColorBrush(Theme.Parse("#E8A33D", System.Windows.Media.Colors.Orange)), 7), ModulePics.T(t, "Pocket", t.Fg, 11),
                                  ModulePics.Dot(new System.Windows.Media.SolidColorBrush(Theme.Parse("#4C9AFF", System.Windows.Media.Colors.SteelBlue)), 7), ModulePics.T(t, "Shelf 3", t.Fg, 11, true),
                                  ModulePics.T(t, "Clipboard", t.FgDim, 11)),
                row("\uE7C3", "Invoice 0412.pdf", "C:\\Users\\you\\Downloads"),
                row("\uE8B7", "Holiday photos", "D:\\Pictures"),
                row("\uE8A5", "Shopping list", "Text · 5 min ago"));
        }
    }

    /// <summary>"Add to shelf" in Explorer's right-click menu (your account only: HKCU\Software\Classes) for files, folders and drives.</summary>
    static class ExplorerMenu
    {
        const string Verb = "vShell.Shelf";
        static readonly string[] Bases = { @"Software\Classes\*\shell\", @"Software\Classes\Directory\shell\", @"Software\Classes\Drive\shell\" };

        static string Command(string exe) { return "\"" + exe + "\" --plugin shelf add \"%1\""; }

        public static void Add()
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            foreach (string b in Bases)
            {
                try
                {
                    using (RegistryKey cmd = Registry.CurrentUser.OpenSubKey(b + Verb + @"\command"))
                        if (cmd != null && (cmd.GetValue("") as string) == Command(exe)) continue;      // already there for this vShell.exe
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(b + Verb))
                    {
                        k.SetValue("", "Add to shelf");
                        k.SetValue("Icon", "\"" + exe + "\",0");
                        k.SetValue("MultiSelectModel", "Player");
                        using (RegistryKey c = k.CreateSubKey("command")) c.SetValue("", Command(exe));
                    }
                }
                catch (Exception ex) { Log.Write("Shelf: Explorer's menu - " + ex.Message); }
            }
        }

        public static void Remove()
        {
            foreach (string b in Bases)
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(b + Verb))
                        if (k == null) continue;
                    Registry.CurrentUser.DeleteSubKeyTree(b + Verb, false);
                }
                catch (Exception ex) { Log.Write("Shelf: Explorer's menu - " + ex.Message); }
            }
        }
    }
}
