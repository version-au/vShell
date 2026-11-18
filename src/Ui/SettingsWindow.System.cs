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
    /// <summary>The Desktop, System (behaviour, backups), Updates (vShell's and the vSuite apps') and About pages.</summary>
    public partial class SettingsWindow
    {
        // =================================================================
        // Desktop, System, About
        // =================================================================
        void BuildDesktopPage()
        {
            var root = Page(PgDesktop);
            root.Children.Clear();
            _cardOf.Remove(root);
            bool old = _loading;
            _loading = true;
            PageHeader(root, "Desktop", "Your wallpapers (widget snapping and gaps are on the Widgets page)");
            BuildWallpaperSection(root);
            _loading = old;
        }

        void BuildSystemPage()
        {
            var root = Page(PgSystem);
            PageHeader(root, "System", null);


            Header(root, "Behaviour");
            Check(root, "vShell - run at login (Recommended)", delegate { return StartupManager.IsSelfEnabled(); }, delegate (bool v)
            {
                string err = StartupManager.SetSelf(v);
                if (err != null) MessageBox.Show(this, "Could not change vShell's startup entry: " + err, "Run at login", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshStartup(null);     // the Startup apps list shows the change
            });
            Note(root, "Starts vShell when you sign in (an entry in Startup apps).");
            Check(root, "Hide the Windows taskbar", delegate { return S.HideWindowsTaskbar; }, delegate (bool v) { S.HideWindowsTaskbar = v; });
            Check(root, "Tray balloon notifications (shown next to the tray icon)", delegate { return S.ShowTrayBalloons; }, delegate (bool v) { S.ShowTrayBalloons = v; });
            Check(root, "Show vShell in the system tray", delegate { return S.ShowTrayIcon; }, delegate (bool v) { S.ShowTrayIcon = v; },
                  "vShell's icon in the tray (your Tray module, or Windows' when you don't use one): click it for Settings, right-click for " +
                  "notification history, updates, Restart vShell and Exit vShell.");
            Check(root, "Show the splash screen while vShell starts", delegate { return S.ShowSplash; }, delegate (bool v) { S.ShowSplash = v; });

            BuildBackupsCard(root);
        }

        /// <summary>Settings > Updates: vShell's own updates, then the vSuite apps (vXplorer, vView) - as the apps' Options > Updates.</summary>
        void BuildUpdatesPage()
        {
            var root = Page(PgUpdates);
            PageHeader(root, "Updates", "New versions of vShell and the vSuite apps");
            BuildUpdatesCard(root);
            BuildSuiteCard(root);
        }

        /// <summary>Settings > Updates > Updates: version, status, release notes and the buttons; redrawn when UpdateCenter changes.</summary>
        void BuildUpdatesCard(StackPanel root)
        {
            Header(root, "vShell");
            var body = new StackPanel();
            Place(root, body);
            Check(root, "Check for updates automatically (a minute after start-up, then daily)", delegate { return S.UpdateAuto; }, delegate (bool v) { S.UpdateAuto = v; });
            Check(root, "Include pre-releases", delegate { return S.UpdatePre; }, delegate (bool v) { S.UpdatePre = v; });
            Note(root, "Updates come from " + vSuite.App.UpdateSite.Replace("https://", "") + ". The download is checked against the release's SHA-256 checksum, vShell restarts into the " +
                       "new version, and if that doesn't start properly the previous version is put back. Your settings are kept.");

            Action draw = null;
            draw = delegate
            {
                body.Children.Clear();
                UpdateInfo u = UpdateCenter.Available;
                string status;
                if (UpdateCenter.Installing) status = "Downloading " + (u != null ? "vShell " + UpdateCenter.VersionText(u) : "the update") + "…  " + Math.Round(UpdateCenter.Progress * 100) + "%";
                else if (UpdateCenter.Busy) status = "Checking…";
                else if (UpdateCenter.LastError.Length > 0) status = "Could not check: " + UpdateCenter.LastError;
                else if (u != null) status = "vShell " + UpdateCenter.VersionText(u) + (u.PreRelease ? " (pre-release)" : "") + " is available" + (UpdateCenter.IsSkipped(u) ? " - you chose to skip it" : "") + ".";
                else if (UpdateCenter.LastChecked != DateTime.MinValue) status = "You have the latest version (checked " + TimeText.Stamp(UpdateCenter.LastChecked.ToLocalTime()) + ").";
                else if (S.UpdateLastCheck != DateTime.MinValue) status = "Last checked " + S.UpdateLastCheck.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ".";
                else status = "Not checked yet.";

                body.Children.Add(Txt("This is vShell " + Updater.CurrentText + (Updater.ExePath == null ? " (not the published vShell.exe - updates can't be installed)" : ""), "vsFg", 13.5, false));
                var st = Txt(status, u != null && !UpdateCenter.IsSkipped(u) ? "vsAccent" : "vsDim", 12.5, false);
                st.Margin = new Thickness(0, 4, 0, 0);
                st.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(st);

                if (u != null && (u.Notes ?? "").Trim().Length > 0)
                {
                    var notes = Txt(u.Notes.Trim(), "vsFg", 12.5, false);
                    notes.TextWrapping = TextWrapping.Wrap;
                    var sv = new ScrollViewer { Content = notes, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
                    var box = new Border { Child = sv, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
                    Ref(box, Border.BackgroundProperty, "vsCtrl");
                    Ref(box, Border.BorderBrushProperty, "vsLine");
                    box.Margin = new Thickness(0, 8, 0, 0);
                    body.Children.Add(box);
                }

                var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
                var check = Btn(UpdateCenter.Busy ? "Checking…" : "Check now", delegate { UpdateCenter.CheckNow(true); });
                check.IsEnabled = !UpdateCenter.Busy && !UpdateCenter.Installing;
                row.Children.Add(check);
                if (u != null)
                {
                    var install = Btn("Install and restart", delegate
                    {
                        if (MessageBox.Show(this, "Download vShell " + UpdateCenter.VersionText(u) + " and restart vShell now?", "vShell update",
                                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        UpdateCenter.Install(u, this);
                    }, true);
                    install.IsEnabled = !UpdateCenter.Installing && Updater.ExePath != null;
                    install.Margin = new Thickness(8, 0, 0, 0);
                    row.Children.Add(install);
                    if (!UpdateCenter.IsSkipped(u))
                    {
                        var skip = Btn("Skip this version", delegate { UpdateCenter.Skip(u); });
                        skip.Margin = new Thickness(8, 0, 0, 0);
                        row.Children.Add(skip);
                    }
                }
                var site = Btn("vSuite website", delegate { UpdateCenter.OpenPage(u); });
                site.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(site);
                body.Children.Add(row);
            };
            Action onChange = delegate { Dispatcher.BeginInvoke(draw); };
            UpdateCenter.Changed += onChange;
            Closed += delegate { UpdateCenter.Changed -= onChange; };
            draw();
        }

        /// <summary>Settings > System > Backups: the daily copy, the backups (Restore), Back up now, Export / Import, the folder.</summary>
        void BuildBackupsCard(StackPanel root)
        {
            Header(root, "Backups");
            Check(root, "Back up my settings automatically (once a day, the last 7 kept)", delegate { return S.BackupAuto; }, delegate (bool v) { S.BackupAuto = v; });
            var body = new StackPanel();
            Place(root, body);
            Note(root, "A backup is a copy of all your settings: bars, widgets, modules, theme, plugins and saved terminal connections. If your settings " +
                       "ever can't be read when vShell starts, it uses the newest backup by itself and tells you. Restore puts one back and restarts " +
                       "vShell (your settings as they are now are kept as a backup first, so you can go back). Passwords and API keys in an export only " +
                       "work for your Windows account - on another PC, enter them again.");

            Action draw = null;
            draw = delegate
            {
                body.Children.Clear();
                List<BackupInfo> all = SettingsBackup.List();
                if (all.Count == 0)
                    body.Children.Add(Txt("No backups yet - the first daily one is made the next time vShell saves your settings.", "vsDim", 12.5, false));
                int shown = 0;
                foreach (BackupInfo b in all)
                {
                    if (shown++ >= 8) break;
                    BackupInfo backup = b;
                    var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    text.Children.Add(Txt(TimeText.Stamp(b.When), "vsFg", 13, false));
                    text.Children.Add(Txt(b.KindText + " · " + Math.Max(1, b.Size / 1024) + " KB", b.Kind == "broken" ? "vsAccent" : "vsDim", 11.5, false));
                    g.Children.Add(text);
                    if (b.Kind != "broken")
                    {
                        var restore = Btn("Restore", delegate { RestoreSettings(backup.Path, "the backup from " + TimeText.Stamp(backup.When)); });
                        Grid.SetColumn(restore, 1);
                        restore.VerticalAlignment = VerticalAlignment.Center;
                        g.Children.Add(restore);
                    }
                    body.Children.Add(g);
                }
                if (all.Count > 8) body.Children.Add(Txt((all.Count - 8) + " older in the folder", "vsDim", 11.5, false));

                var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
                row.Children.Add(Btn("Back up now", delegate
                {
                    if (SettingsBackup.Now(S) == null) MessageBox.Show(this, "The backup couldn't be made - see the log.", "Backups", MessageBoxButton.OK, MessageBoxImage.Warning);
                    draw();
                }, true));
                var export = Btn("Export…", ExportSettings);
                export.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(export);
                var import = Btn("Import…", ImportSettings);
                import.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(import);
                var folder = Btn("Open the backups folder", delegate
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(SettingsBackup.Dir);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsBackup.Dir) { UseShellExecute = true });
                    }
                    catch (Exception ex) { Log.Write("Backups folder: " + ex.Message); }
                });
                folder.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(folder);
                body.Children.Add(row);
            };
            draw();
        }

        /// <summary>Put settings back from a file (a backup or an import), after asking, then restart vShell.</summary>
        void RestoreSettings(string path, string what)
        {
            if (MessageBox.Show(this, "Put back " + what + " and restart vShell?\n\nYour settings as they are now are kept as a backup first (\"Before a restore\"), " +
                                      "so you can go back.", "Restore settings", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            S.Save();       // what's changed until now goes into the "before a restore" copy
            string err = SettingsBackup.Restore(path);
            if (err != null) { MessageBox.Show(this, err, "Restore settings", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _mgr.RestartVShell();
        }

        void ExportSettings()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export vShell settings", Filter = "vShell settings (*.json)|*.json",
                FileName = "vShell settings " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                S.Save();
                System.IO.File.Copy(BarSettings.ConfigPath, dlg.FileName, true);
                Log.Write("Backups: settings exported to " + dlg.FileName);
            }
            catch (Exception ex) { MessageBox.Show(this, "The settings couldn't be exported: " + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void ImportSettings()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import vShell settings", Filter = "vShell settings (*.json)|*.json|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            if (SettingsBackup.Read(dlg.FileName) == null)
            {
                MessageBox.Show(this, "That file isn't vShell settings, or it can't be read.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RestoreSettings(dlg.FileName, "the settings in " + System.IO.Path.GetFileName(dlg.FileName));
        }

        void BuildAboutPage()
        {
            var root = Page(PgAbout);
            Version ver = typeof(SettingsWindow).Assembly.GetName().Version;
            PageHeader(root, "About", null);

            // the logo, name and version
            var hero = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            var mark = MakeMark(84);
            mark.Margin = new Thickness(4, 0, 22, 0);
            hero.Children.Add(mark);
            var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            heroText.Children.Add(Txt("vShell", "vsFg", 30, true));
            var vline = Txt("Version " + (ver == null ? "?" : ver.Major + "." + ver.Minor + "." + ver.Build), "vsDim", 13, false);
            vline.Margin = new Thickness(0, 2, 0, 0);
            heroText.Children.Add(vline);
            var tag = Txt("Bars and Widgets with a selection of plugins and modules to suit your needs.", "vsDim", 13, false);
            tag.Margin = new Thickness(0, 6, 0, 0);
            heroText.Children.Add(tag);
            var by = Txt("Vibe coded with love by version", "vsDim", 13, false);
            by.Margin = new Thickness(0, 2, 0, 0);
            heroText.Children.Add(by);
            hero.Children.Add(heroText);
            root.Children.Add(hero);
            Header(root, "Getting started");
            Place(root, Row("Welcome guide", Btn("Show it again", delegate { _mgr.ShowWelcome(); }), false,
                            "The steps a first start shows: start-up, theme, layout (bars on every monitor, Windows' pinned apps) and plugins. " +
                            "Every choice is applied as you make it."));
            Header(root, "Files");
            Place(root, Row("Settings and log folder", Btn("Open folder", delegate
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + BarSettings.ConfigDir + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("Open folder: " + ex.Message); }
            })));
            Place(root, Row("Log file (vshell.log)", Btn("Open log", delegate
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BarSettings.LogPath) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("Open log: " + ex.Message); }
            })));
            Place(root, Row("Plugin SDK - create your own plugin", Btn("Download", async delegate
            {
                // vShell.dll + plugin-api.txt + plugins.md, beside each release on the update site (build.cmd puts it there). With
                // Include pre-releases on: the newest of the release and the pre-release (vshell/pre/sdk.zip when that's newer) - before
                // 0.38.227 it was always the release's, so a pre-release's new plugin API couldn't be downloaded
                string sdk = vSuite.App.UpdateSite + "/vshell/sdk.zip";
                if (S.UpdatePre)
                {
                    try
                    {
                        UpdateInfo newest = null;
                        foreach (UpdateInfo u in await vSuite.Updater.ReleasesAsync("vShell", true))
                            if (newest == null || u.Version > newest.Version) newest = u;
                        if (newest != null && !string.IsNullOrEmpty(newest.Folder)) sdk = newest.Folder.TrimEnd('/') + "/sdk.zip";
                    }
                    catch (Exception ex) { Log.Write("Plugin SDK: the update site didn't answer (" + ex.Message + ") - the release's SDK"); }
                }
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sdk) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("Plugin SDK: " + ex.Message); }
            })));
        }

    }
}
