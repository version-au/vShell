using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Settings > Updates > vSuite apps: vXplorer, vView, vTerm, vPad and vTrace - whether they're installed, which version, whether a newer one is out - with
    /// Open, Update (starts the app with --update: it updates itself with its own notice and roll-back) and Install (its newest installer). The update site is asked once per Settings window, when the Updates page shows.
    /// </summary>
    public partial class SettingsWindow
    {
        sealed class SuiteApp
        {
            public string Name, Glyph, Blurb, InnoId;
            public string Exe;          // the installed exe, or null
            public Version Installed, Newest;
            public string Error;
            /// <summary>The app's own Include pre-releases (its settings file).</summary>
            public bool Pre;
            /// <summary>What Install is doing now (looking, downloading 40%, the installer is open …), or null.</summary>
            public string Busy;
            /// <summary>The version the app won't install (skipped / rolled back) - no Update button for it.</summary>
            public string Skip = "";
            /// <summary>When Include pre-releases was clicked here: for a few seconds that wins over the file (the app saves it a moment later).</summary>
            public DateTime PreClicked;
            /// <summary>The newest release for each Include pre-releases value, looked up once per Settings window.</summary>
            public readonly System.Collections.Generic.Dictionary<bool, Version> Found = new System.Collections.Generic.Dictionary<bool, Version>();
            public bool Looking;
        }

        static SuiteApp[] SuiteApps()
        {
            return new[]
            {
                new SuiteApp { Name = "vXplorer", Glyph = "", Blurb = "Files and folders in your vShell theme", InnoId = "{9C3E5B71-2A4D-4F8E-B6C1-5E7A9D2F0B84}" },
                new SuiteApp { Name = "vView", Glyph = "", Blurb = "Pictures, videos and sounds in your vShell theme", InnoId = "{4E8B2D6A-7C31-4F9A-9E52-B1D3A6C8F072}" },
                new SuiteApp { Name = VTerm.Name, Glyph = "", Blurb = "The terminal - the Terminal module's drop-down, and windows of its own", InnoId = VTerm.InnoId },
                new SuiteApp { Name = "vPad", Glyph = "\uE70B", Blurb = "A text editor - tabs, syntax colours, Markdown, and files on your servers", InnoId = "{03F65620-3C49-4AC0-A0CF-4B60DF14E4F0}" },
                new SuiteApp { Name = "vTrace", Glyph = "\uE9D9", Blurb = "What changed, and when - your PC, servers, Komodo and Proxmox on one timeline", InnoId = "{36C71FB6-22DE-485C-B2D6-708884B3D771}" },
            };
        }

        /// <summary>Where the app is installed: its installer's record (the folder you picked), else the usual folder; null when it isn't there.</summary>
        static string FindInstalled(SuiteApp a) { return FindSuiteApp(a.Name, a.InnoId); }

        /// <summary>A vSuite app's installed exe (by its installer's id), or null.</summary>
        internal static string FindSuiteApp(string name, string innoId)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + innoId + "_is1"))
                {
                    string dir = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string exe = Path.Combine(dir, name + ".exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            catch { }
            string usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", name, name + ".exe");
            return File.Exists(usual) ? usual : null;
        }

        static Version FileVersionOf(string exe)
        {
            try
            {
                FileVersionInfo fv = FileVersionInfo.GetVersionInfo(exe);
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart);
            }
            catch { return null; }
        }

        void BuildSuiteCard(StackPanel root)
        {
            Header(root, "vSuite apps");
            var list = new StackPanel();
            Place(root, list);
            Note(root, "The apps that share vShell's look. Update asks the app to update itself (its own notice shows the progress, and it goes " +
                       "back to the previous version if the new one doesn't start). Install (for an app that isn't there) downloads its newest installer and starts it. Include pre-releases is each " +
                       "app's own setting (its Options > Updates) - changing it here changes it there, and the other way round.");
            SuiteApp[] apps = SuiteApps();
            Action draw = null;
            bool shown = false;
            draw = delegate
            {
                list.Children.Clear();
                for (int i = 0; i < apps.Length; i++) list.Children.Add(SuiteRow(apps[i], i > 0, delegate { CheckSuite(apps, draw, shown); }, delegate { draw(); }));
            };
            CheckSuite(apps, draw, false);   // on disk only: nothing asked online until the Updates page is shown
            // the update site is asked once the card is first on screen (not every time Settings opens on another page)
            list.IsVisibleChanged += delegate
            {
                if (shown || !list.IsVisible) return;
                shown = true;
                CheckSuite(apps, draw, true);
            };
            // an app's settings file changing, or coming back from an installer: looked at again on disk (the update site only for a value not looked up yet)
            WatchSuite(apps, delegate { CheckSuite(apps, draw, shown); });
            Activated += delegate { CheckSuite(apps, draw, shown); };
        }

        /// <summary>While Settings is open: an app's settings file changing (its Include pre-releases switched there) redraws the card.</summary>
        void WatchSuite(SuiteApp[] apps, Action again)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += delegate { timer.Stop(); again(); };
            foreach (SuiteApp a in apps)
            {
                try
                {
                    string dir = vSuite.SuiteApps.DataFolder(a.Name);
                    if (!Directory.Exists(dir)) continue;
                    var w = new FileSystemWatcher(dir, "settings.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                    FileSystemEventHandler h = delegate { Dispatcher.BeginInvoke(new Action(delegate { timer.Stop(); timer.Start(); })); };
                    w.Changed += h; w.Created += h;
                    w.Renamed += delegate { Dispatcher.BeginInvoke(new Action(delegate { timer.Stop(); timer.Start(); })); };
                    w.EnableRaisingEvents = true;
                    Closed += delegate { w.Dispose(); };
                }
                catch (Exception ex) { Log.Write("Suite watch " + a.Name + ": " + ex.Message); }
            }
            Closed += delegate { timer.Stop(); };
        }

        /// <summary>
        /// What's installed, its version, Include pre-releases and skipped version - read here (small files, on this thread, so the rows are
        /// only ever changed here) - then, with online, the newest installable release for each value not looked up yet (on a worker; the
        /// result is applied back on this thread). Draws the rows.
        /// </summary>
        void CheckSuite(SuiteApp[] apps, Action draw, bool online)
        {
            foreach (SuiteApp a in apps)
            {
                a.Exe = FindInstalled(a);
                a.Installed = a.Exe != null ? FileVersionOf(a.Exe) : null;
                if (a.Exe != null && a.Busy != null && !a.Busy.StartsWith("Downloading") && !a.Busy.StartsWith("Looking")) a.Busy = null;   // installed now
                // what the app itself would install (its own Include pre-releases) - a click here wins while the app saves it
                if ((DateTime.UtcNow - a.PreClicked).TotalSeconds > 5) a.Pre = vSuite.SuiteApps.UpdatePre(a.Name);
                a.Skip = vSuite.SuiteApps.UpdateSkip(a.Name);
                Version v;
                a.Newest = a.Found.TryGetValue(a.Pre, out v) ? v : null;
                if (!online || a.Found.ContainsKey(a.Pre) || a.Looking) continue;
                SuiteApp app = a;
                bool pre = a.Pre;
                app.Looking = true;
                Task.Run(() => vSuite.Updater.NewestReleaseAsync(app.Name, pre)).ContinueWith(delegate (Task<Version> t)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        app.Looking = false;
                        if (t.IsFaulted) app.Error = t.Exception.GetBaseException().Message;
                        else { app.Error = null; app.Found[pre] = t.Result; }
                        CheckSuite(apps, draw, true);
                    }));
                });
            }
            draw();
        }

        UIElement SuiteRow(SuiteApp a, bool line, Action changed, Action redraw)
        {
            var g = new Grid { Margin = new Thickness(0, line ? 10 : 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var gl = Glyph(a.Glyph, 15);
            gl.HorizontalAlignment = HorizontalAlignment.Center;
            Ref(gl, TextBlock.ForegroundProperty, "vsFg");
            var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Child = gl, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            Ref(box, Border.BackgroundProperty, "vsCtrl");
            g.Children.Add(box);

            bool newer = a.Installed != null && a.Newest != null && a.Newest > a.Installed && vSuite.Updater.Text(a.Newest) != a.Skip;
            string state;
            if (a.Exe == null) state = a.Newest != null ? "Not installed - " + vSuite.Updater.Text(a.Newest) + " is out" : "Not installed";
            else if (a.Installed == null) state = "Installed";
            else if (newer) state = vSuite.Updater.Text(a.Installed) + " installed - " + vSuite.Updater.Text(a.Newest) + " is available";
            else if (a.Newest != null && a.Newest > a.Installed) state = vSuite.Updater.Text(a.Installed) + " installed - " + vSuite.Updater.Text(a.Newest) + " was skipped (it didn't start, or you chose to skip it)";
            else if (a.Newest != null || a.Found.ContainsKey(a.Pre)) state = vSuite.Updater.Text(a.Installed) + " installed - the newest";
            else if (a.Error != null) state = vSuite.Updater.Text(a.Installed) + " installed - couldn't check for updates (" + a.Error + ")";
            else state = vSuite.Updater.Text(a.Installed) + " installed" + (a.Looking ? " - checking…" : "");

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Txt(a.Name, "vsFg", 13.5, false));
            if (a.Busy != null) state = a.Busy;
            var s = Txt(state, newer || a.Busy != null ? "vsAccent" : "vsDim", 12, false);
            s.Margin = new Thickness(0, 2, 0, 0);
            s.ToolTip = a.Blurb + (a.Exe != null ? "\n" + a.Exe : "");
            text.Children.Add(s);
            if (a.Exe != null)
            {
                // the app's own Include pre-releases: changed here, it changes in the app (running or not), and the other way round
                var pre = new CheckBox { Content = Txt("Include pre-releases", "vsDim", 12, false), IsChecked = a.Pre, Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                pre.Click += delegate
                {
                    bool on = pre.IsChecked == true;
                    a.Pre = on;
                    a.PreClicked = DateTime.UtcNow;
                    Task.Run(delegate { vSuite.SuiteApps.SetUpdatePre(a.Name, on); }).ContinueWith(delegate { Dispatcher.BeginInvoke(changed); });
                };
                text.Children.Add(pre);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (a.Exe != null)
            {
                buttons.Children.Add(Btn("Open", delegate { StartSuiteApp(a, null); }));
                if (newer)
                {
                    var up = Btn("Update", delegate { StartSuiteApp(a, vSuite.Ui.UpdateCenter.UpdateCommand); }, true);
                    up.Margin = new Thickness(8, 0, 0, 0);
                    buttons.Children.Add(up);
                }
            }
            else
            {
                var install = Btn("Install " + a.Name, delegate { InstallSuiteApp(a, redraw); }, true);
                install.IsEnabled = a.Busy == null || !a.Busy.StartsWith("Downloading") && !a.Busy.StartsWith("Looking");
                buttons.Children.Add(install);
            }
            Grid.SetColumn(buttons, 2);
            g.Children.Add(buttons);
            return g;
        }

        /// <summary>
        /// Install an app that isn't there: the newest release that has its installer, downloaded (progress in the row) and started - you
        /// finish it as usual. No release with an installer: its releases page opens.
        /// </summary>
        void InstallSuiteApp(SuiteApp a, Action redraw)
        {
            a.Busy = "Looking for the installer…";
            redraw();
            DateTime shown = DateTime.MinValue;
            Task.Run(async delegate
            {
                vSuite.SuiteApps.Installer inst = await vSuite.SuiteApps.FindInstallerAsync(a.Name, false);
                if (inst == null) return null;
                Log.Write("Suite: downloading " + inst.FileName);
                return await vSuite.SuiteApps.DownloadInstallerAsync(inst, delegate (double p)
                {
                    if ((DateTime.UtcNow - shown).TotalMilliseconds < 250 && p < 1) return;
                    shown = DateTime.UtcNow;
                    Dispatcher.BeginInvoke(new Action(delegate { a.Busy = "Downloading the installer…  " + Math.Round(p * 100) + "%"; redraw(); }));
                });
            }).ContinueWith(delegate (Task<string> t)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (t.IsFaulted)
                    {
                        Exception ex = t.Exception.GetBaseException();
                        a.Busy = "Couldn't get the installer: " + ex.Message;
                        Log.Write("Suite: install " + a.Name + ": " + ex.Message);
                    }
                    else if (t.Result == null)
                    {
                        a.Busy = "There's no installer on the vSuite website yet - the website is open";
                        try { Process.Start(new ProcessStartInfo(vSuite.Updater.ReleasesPage) { UseShellExecute = true }); }
                        catch (Exception ex) { Log.Write("Suite: open the website: " + ex.Message); }
                    }
                    else
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(t.Result) { UseShellExecute = true });
                            a.Busy = "The installer is open - finish it there";
                            Log.Write("Suite: started " + t.Result);
                        }
                        catch (Exception ex) { a.Busy = "Couldn't start the installer: " + ex.Message; }
                    }
                    redraw();
                }));
            });
        }

        static void StartSuiteApp(SuiteApp a, string arg)
        {
            try
            {
                var psi = new ProcessStartInfo(a.Exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(a.Exe) };
                if (arg != null) psi.ArgumentList.Add(arg);
                Process.Start(psi);
                Log.Write("Suite: started " + a.Name + (arg != null ? " " + arg : ""));
            }
            catch (Exception ex) { Log.Write("Suite: start " + a.Name + ": " + ex.Message); }
        }
    }
}
