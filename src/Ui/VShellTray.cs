using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// vShell's own icon in the system tray. With a Tray module it shows there (vShell holds the apps' tray icons, its own too), otherwise
    /// in Windows' tray. Click: Settings. Right-click: notification history, updates, Restart, Exit - in the theme's colours (vSuite's
    /// TrayIcon dresses the menu and closes it on a click elsewhere). The tooltip says when an update is ready.
    /// </summary>
    public sealed class VShellTray : IDisposable
    {
        readonly BarManager _mgr;
        vSuite.Ui.TrayIcon _icon;
        Action _updates;
        /// <summary>The colours the logo was drawn in.</summary>
        string _colours = "";

        public VShellTray(BarManager mgr) { _mgr = mgr; }

        /// <summary>
        /// Show or take away the icon (Settings > System > Show vShell in the system tray); called whenever settings are applied, so the
        /// logo is drawn again when the theme's colours changed.
        /// </summary>
        public void Show(bool on)
        {
            if (on && _icon == null) Create();
            else if (!on && _icon != null) Remove();
            if (_icon != null) Paint();
        }

        void Create()
        {
            try
            {
                _icon = new vSuite.Ui.TrayIcon("vShell") { Menu = BuildMenu, MenuTheme = Questions.SuiteTheme };
                _icon.Clicked += delegate { _mgr.OpenSettings(null); };
                _colours = "";
                _icon.Show();
                _updates = Tip;
                UpdateCenter.Changed += _updates;
                Tip();
            }
            catch (Exception ex)
            {
                Log.Write("vShell tray icon: " + ex.Message);
                Remove();
            }
        }

        void Remove()
        {
            if (_updates != null) { UpdateCenter.Changed -= _updates; _updates = null; }
            if (_icon != null)
            {
                try { _icon.Dispose(); } catch { }
                _icon = null;
            }
            _colours = "";
        }

        /// <summary>The vShell mark in the theme's Main Accent, Secondary Accent and text colours (only when they changed).</summary>
        void Paint()
        {
            try
            {
                Theme t = Theme.From(_mgr.Settings, 12, 0);
                int px = vSuite.Ui.TrayIcon.Size;
                string colours = t.MainAccent.Color + "|" + t.SecondaryAccent.Color + "|" + t.Fg.Color + "|" + px;
                if (colours == _colours) return;
                var mark = new VShellMark { Dot1 = t.MainAccent, Dot2 = t.SecondaryAccent, Dot3 = t.Fg, Dim = px };
                mark.Measure(new Size(px, px));
                mark.Arrange(new Rect(0, 0, px, px));
                var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(mark);
                _icon.SetIcon(rtb);
                _colours = colours;
            }
            catch (Exception ex)
            {
                Log.Write("vShell tray icon colours: " + ex.Message);
                _colours = "failed";      // keep the plain icon; not tried again on every change
            }
        }

        public void Dispose() { Remove(); }

        /// <summary>"vShell 0.38.98", or with "update 0.38.99 ready" once one is waiting.</summary>
        void Tip()
        {
            if (_icon == null) return;
            string t = "vShell " + Updater.CurrentText;
            UpdateInfo u = UpdateCenter.Available;
            if (u != null && !UpdateCenter.IsSkipped(u)) t += " - update " + UpdateCenter.VersionText(u) + " ready";
            _icon.Tip = t;
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            Add(m, "Settings…", delegate { _mgr.OpenSettings(null); });
            Add(m, "Notification history", delegate { NotifyHistoryWindow.ShowNear(_mgr); });
            Add(m, "Check for updates", delegate { _mgr.CheckForUpdates(); });
            m.Items.Add(new Separator());
            Add(m, "Restart vShell", delegate { _mgr.RestartVShell(); });
            Add(m, "Exit vShell", delegate { _mgr.Shutdown(); });
            return m;
        }

        static void Add(ContextMenu m, string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { a(); };
            m.Items.Add(mi);
        }
    }
}
