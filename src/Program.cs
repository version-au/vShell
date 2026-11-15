using System;
using System.Threading;
using System.Windows;
using vShell.Core;
using vShell.Ui;

namespace vShell
{
    public static class Program
    {
        /// <summary>Update hand-over: end any other vShell.exe that is stuck closing (never the update watchdog, vShell.old.exe).</summary>
        static void EndStuckVShell()
        {
            int me = Environment.ProcessId;
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("vShell"))
            {
                try
                {
                    if (p.Id == me) continue;
                    Log.Write("Update: ending the previous vShell (pid " + p.Id + "), which didn't close");
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) { Log.Write("Update: could not end pid " + p.Id + ": " + ex.Message); }
                finally { p.Dispose(); }
            }
        }

        static string CrashFile { get { return System.IO.Path.Combine(BarSettings.ConfigDir, "last-crash.txt"); } }

        /// <summary>
        /// vShell is going down from an error nothing caught (on a background thread): write it to the log and start vShell again - unless
        /// it crashed less than 2 minutes ago too (then it stays closed, and Explorer's taskbar comes back, rather than crash over and over).
        /// </summary>
        static void Crashed(Exception ex)
        {
            try
            {
                Log.Write("Crash: " + ex);
                DateTime last = DateTime.MinValue;
                try { DateTime.TryParse(System.IO.File.ReadAllText(CrashFile), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out last); } catch { }
                try { System.IO.File.WriteAllText(CrashFile, DateTime.UtcNow.ToString("o")); } catch { }
                string exe = Updater.ExePath;
                if (exe != null && (DateTime.UtcNow - last).TotalMinutes >= 2)
                {
                    Log.Write("Crash: starting vShell again");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restart --crashed") { UseShellExecute = false });
                }
                else
                {
                    Log.Write("Crash: crashed twice within 2 minutes" + (exe == null ? " (or not running from vShell.exe)" : "") + " - not starting again");
                    try { ShellServices.RestoreExplorerTaskbar(); } catch { }
                }
            }
            catch { }
            finally { try { Log.Flush(); } catch { } }
        }

        /// <summary>After start-up: say when vShell came back from a crash, or had to use a settings backup.</summary>
        static void StartNotices(bool afterCrash)
        {
            try
            {
                if (afterCrash)
                    Notify.Post("vShell", "vShell closed unexpectedly and started again",
                                "What went wrong is in the log (%APPDATA%\\vShell\\vshell.log, look for \"Crash:\").", "", NotifyLevel.Important);
                if (SettingsBackup.RestoredFrom != null)
                    Notify.Post("vShell", "Your settings couldn't be read - vShell used a backup",
                                "The backup from " + TimeText.Stamp(SettingsBackup.RestoredFrom.When) + ". The file that couldn't be read is kept in the backups folder " +
                                "(Settings > System > Backups).", "", NotifyLevel.Alert);
                else if (SettingsBackup.BrokenCopy != null)
                    Notify.Post("vShell", "Your settings couldn't be read - vShell started fresh",
                                "There was no backup to use. The file that couldn't be read is kept in the backups folder (Settings > System > Backups).", "", NotifyLevel.Alert);
            }
            catch (Exception ex) { Log.Write("Start notices: " + ex.Message); }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            // the suite's shared library: which app this is (its folder %APPDATA%\vShell, vshell.log, the releases repo)
            vSuite.App.Init("vShell", "VSHELL", "https://vsuite.vrsion.com");
            // the suite's themed questions (Questions -> vSuite's Ask) in vShell's own theme
            vSuite.Ui.Ask.FallbackTheme = vShell.Ui.Questions.SuiteTheme;
            // an update brings the plugins you've added along for the new version (a missing one is fetched again once it runs)
            Updater.BeforeSwap = async delegate (UpdateInfo u)
            {
                string plug = await PluginStore.StageForAsync(u);
                return plug.Length > 0 ? "plugins not brought along - " + plug : "";
            };

            // Update: this is the previous vShell.exe, started to hand over to the new one (no window, no single-instance lock)
            // build.cmd: vShell.exe --plugin-api dist\plugin-api.txt -> the plugin API (what's marked [PluginApi]), one line per type / member
            if (args.Length >= 2 && string.Equals(args[0], "--plugin-api", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var lines = new System.Collections.Generic.List<string> { "# vShell plugin API " + PluginApi.Version + " (vShell " + Updater.CurrentText + ")" };
                    lines.AddRange(PluginFileInfo.Surface(typeof(PluginApi).Assembly));
                    System.IO.File.WriteAllLines(args[1], lines);
                    return 0;
                }
                catch (Exception ex) { try { System.IO.File.WriteAllText(args[1] + ".failed.txt", ex.ToString()); } catch { } return 1; }
            }            // build.cmd: vShell.exe --plugin-catalog dist -> dist\plugins.json (the plugins in dist, from their manifests) and plugins.txt
            if (args.Length >= 2 && string.Equals(args[0], "--plugin-catalog", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    System.Collections.Generic.List<string> lines = PluginStore.WriteCatalog(args[1]);
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(args[1], "plugins.txt"), lines);
                    return 0;
                }
                catch (Exception ex) { try { System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "plugins.txt"), "Failed: " + ex.Message); } catch { } return 1; }
            }            if (args.Length >= 3 && string.Equals(args[0], "--update-watchdog", StringComparison.OrdinalIgnoreCase))
                return Updater.RunWatchdog(args);

            // Emergency helper: bring the normal Windows taskbar back (e.g. after a crash with it hidden).
            foreach (string a in args)
            {
                if (string.Equals(a, "--restore-taskbar", StringComparison.OrdinalIgnoreCase))
                {
                    ShellServices.RestoreExplorerTaskbar();
                    return 0;
                }
            }

            bool created;
            using (var mutex = new Mutex(true, "vShell.SingleInstance.v1", out created))
            {
                if (!created && Updater.IsUpdateRestart)
                {
                    // started by an update while the previous vShell is still closing: wait for it rather than quitting (which would
                    // look like a failed update and roll it back)
                    Log.Write("Update: the previous vShell is still closing - waiting for it");
                    try { created = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { created = true; }      // it was ended without letting go: ours now
                    if (!created)
                    {
                        // still there: it is stuck closing (its settings were saved before it started to close) - end it, then go on
                        EndStuckVShell();
                        try { created = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                        catch (AbandonedMutexException) { created = true; }
                    }
                }
                if (!created && Array.Exists(args, delegate (string x) { return string.Equals(x, "--restart", StringComparison.OrdinalIgnoreCase); }))
                {
                    // Restart vShell (after plugin updates): the old one is closing - wait for it
                    try { created = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
                    catch (AbandonedMutexException) { created = true; }
                }
                // "--plugin <id> <verb> args…" (Explorer's right-click menu, a shortcut): for a plugin in the running vShell
                string[] pluginWords = HandOff.PluginWords(args);
                if (!created && pluginWords != null)
                {
                    // vShell may still be starting (it listens once its plugins are loaded): wait a while for it
                    if (!HandOff.Send(pluginWords, 20000)) Log.Write("Hand-off: the running vShell didn't answer - \"" + string.Join(" ", pluginWords) + "\" not done");
                    return 0;
                }
                if (!created) return 0;

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                vShell.Ui.MenuAtPointer.Install();      // menus "at the pointer" really at the pointer (see MenuAtPointer)
                vShell.Ui.DialogPlacement.Install();    // questions and centred windows on the monitor they were asked from (see DialogPlacement)
                var mgr = new BarManager();
                vShell.Ui.Questions.CurrentSettings = delegate { return mgr.Settings; };   // the questions' colours, as the bars show them

                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("Unhandled: " + e.Exception);
                    e.Handled = true;
                };
                // an error on a background thread (a timer, a poller) ends the process - say why in the log, and start again
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Crashed(e.ExceptionObject as Exception); };
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += delegate (object s, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
                {
                    Log.Write("Unobserved task error: " + e.Exception);
                    e.SetObserved();
                };
                bool afterCrash = Array.Exists(args, delegate (string x) { return string.Equals(x, "--crashed", StringComparison.OrdinalIgnoreCase); });
                app.SessionEnding += delegate { mgr.Shutdown(); };
                app.Startup += delegate
                {
                    // splash first, so it is painted before the (blocking) start-up work runs
                    SplashWindow splash = null;
                    try
                    {
                        BarSettings peek = BarSettings.Load();
                        if (peek.ShowSplash) { splash = new SplashWindow(peek); splash.Show(); }
                    }
                    catch (Exception ex) { Log.Write("Splash: " + ex.Message); splash = null; }

                    app.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { mgr.Start(); }
                        catch (Exception ex)
                        {
                            Log.Write("Start failed: " + ex);
                            try { mgr.Shutdown(); } catch { }
                        }
                        if (splash != null) splash.Finish(TimeSpan.FromSeconds(1.4));
                        StartNotices(afterCrash);
                        // the plugins are loaded: take hand-offs from later starts, and do this start's own --plugin words
                        HandOff.Listen(delegate (string[] words)
                        {
                            app.Dispatcher.BeginInvoke(new Action(delegate { PluginManager.RunCommandLine(words); }));
                        });
                        if (pluginWords != null) PluginManager.RunCommandLine(pluginWords);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };
                app.Run();
                // don't let a stray thread keep a closed vShell alive (an update waits for this process to end)
                try { mutex.ReleaseMutex(); } catch { }
                Environment.Exit(0);
                return 0;
            }
        }
    }
}
