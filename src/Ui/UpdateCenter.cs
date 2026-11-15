using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Update checks and installs: checks the update site (vsuite.vrsion.com) a minute after start-up and then about once a day (Settings > Updates),
    /// shows a small notice when a new version is out, and runs the install (which restarts vShell).
    /// </summary>
    public static class UpdateCenter
    {
        static BarManager _mgr;
        static DispatcherTimer _timer;
        static UpdateToast _toast;
        static string _toastShownFor = "";

        public static UpdateInfo Available { get; private set; }
        public static string LastError { get; private set; } = "";
        public static DateTime LastChecked { get; private set; } = DateTime.MinValue;
        public static bool Busy { get; private set; }
        public static bool Installing { get; private set; }
        /// <summary>Download progress 0..1 while installing.</summary>
        public static double Progress { get; private set; }
        /// <summary>Something above changed (the settings page redraws its Updates card).</summary>
        public static event Action Changed;

        /// <summary>true when this start is the new version being started by the update (don't run the startup apps again).</summary>
        public static bool StartedByUpdate { get; private set; }

        public static void Start(BarManager mgr)
        {
            _mgr = mgr;
            string failed = Updater.TakeFailedVersion();
            if (failed != null)
            {
                // the update would not start and the watchdog put this version back: don't offer it again
                mgr.Settings.UpdateSkip = failed;
                mgr.Settings.Save();
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate
                {
                    Questions.Tell(null, "vShell " + failed + " did not start properly, so vShell went back to " + Updater.CurrentText +
                                   ".\n\nThat version will not be offered again; a newer one will.", "vShell update", MessageBoxImage.Warning);
                }), DispatcherPriority.ApplicationIdle);
            }
            Updater.ReportStarted();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            bool first = true;
            _timer.Tick += delegate
            {
                // a minute after start-up, then about once a day (looked at every hour)
                _timer.Interval = TimeSpan.FromHours(1);
                BarSettings s = _mgr.Settings;
                bool due = first || (DateTime.UtcNow - s.UpdateLastCheck).TotalHours >= 20;
                first = false;
                if (!s.UpdateAuto || Busy || Installing || !due) return;
                CheckNow(false);
            };
            _timer.Start();
        }

        /// <summary>Mark that this run was started by an update (call before Start, from BarManager.Start).</summary>
        public static void NoteStartup() { StartedByUpdate = Updater.IsUpdateRestart; }

        static void RaiseChanged() { if (Changed != null) { try { Changed(); } catch (Exception ex) { Log.Write("Update UI: " + ex.Message); } } }

        /// <summary>Check the update site now. byUser = from the Check button (also shows a skipped version).</summary>
        public static async void CheckNow(bool byUser)
        {
            if (_mgr == null || Busy || Installing) return;
            Busy = true;
            LastError = "";
            RaiseChanged();
            try
            {
                UpdateInfo u = await Updater.CheckAsync(_mgr.Settings.UpdatePre);
                Available = u;
                LastChecked = DateTime.UtcNow;
                _mgr.Settings.UpdateLastCheck = LastChecked;
                _mgr.Settings.Save();
                if (u != null && !byUser && !IsSkipped(u)) ShowToast(u);
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Log.Write("Update check: " + LastError);
            }
            finally
            {
                Busy = false;
                RaiseChanged();
            }
        }

        public static string VersionText(UpdateInfo u) { return u.Version.Major + "." + u.Version.Minor + "." + u.Version.Build; }

        public static bool IsSkipped(UpdateInfo u)
        {
            return u != null && _mgr != null && _mgr.Settings.UpdateSkip == VersionText(u);
        }

        public static void Skip(UpdateInfo u)
        {
            if (u == null || _mgr == null) return;
            _mgr.Settings.UpdateSkip = VersionText(u);
            _mgr.Settings.Save();
            CloseToast();
            RaiseChanged();
        }

        public static void OpenPage(UpdateInfo u)
        {
            string url = u != null && u.PageUrl.Length > 0 ? u.PageUrl : Updater.ReleasesPage;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open releases: " + ex.Message); }
        }

        /// <summary>"" while downloading; "install" once downloaded (checking, swapping); "restart" just before vShell closes.</summary>
        public static string Stage { get; private set; } = "";
        /// <summary>Why the last install failed ("" = it didn't).</summary>
        public static string InstallError { get; private set; } = "";

        /// <summary>
        /// Download and install, then restart vShell. From the notice (owner null) the notice shows the progress and any error itself;
        /// from Settings (owner = the window) errors come in a message box.
        /// </summary>
        public static async void Install(UpdateInfo u, Window owner)
        {
            if (u == null || _mgr == null || Installing) return;
            Installing = true;
            Progress = 0;
            Stage = "";
            InstallError = "";
            if (owner != null) CloseToast();
            RaiseChanged();
            Dispatcher disp = Dispatcher.CurrentDispatcher;
            string err = await Updater.DownloadAndStageAsync(u, delegate (double p)
            {
                disp.BeginInvoke(new Action(delegate
                {
                    Progress = p;
                    if (p >= 1) Stage = "install";
                    RaiseChanged();
                }));
            });
            if (err != null)
            {
                Installing = false;
                InstallError = err;
                RaiseChanged();
                if (owner != null || _toast == null) Questions.Tell(owner, err, "vShell update", MessageBoxImage.Warning);
                return;
            }
            // show "Restarting…" for a moment, then go: the watchdog starts the new version as soon as this process has gone
            Progress = 1;
            Stage = "restart";
            RaiseChanged();
            await System.Threading.Tasks.Task.Delay(900);
            _mgr.Shutdown();
        }

        // ---- the notice ------------------------------------------------------------------------------
        static void ShowToast(UpdateInfo u)
        {
            if (_toastShownFor == VersionText(u)) return;       // once per version per run
            _toastShownFor = VersionText(u);
            CloseToast();
            try
            {
                _toast = new UpdateToast(_mgr, u);
                _toast.Closed += delegate { _toast = null; };
                _toast.Show();
            }
            catch (Exception ex) { Log.Write("Update notice: " + ex.Message); }
        }

        static void CloseToast()
        {
            if (_toast != null) { try { _toast.Close(); } catch { } _toast = null; }
        }
    }

    /// <summary>"vShell 0.16.1 is available" in the bars' theme, bottom-right of the main screen (above a bottom bar).</summary>
    public sealed class UpdateToast : Window
    {
        /// <summary>The release notes (markdown) as plain, readable text: headings and **bold** lines in bold, "- " as bullets.</summary>
        static UIElement NotesText(string md, Theme t)
        {
            var panel = new StackPanel();
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;
                bool head = false, bullet = false;
                string text = line.Trim();
                if (text.StartsWith("#")) { text = text.TrimStart('#').Trim(); head = true; }
                if (text.StartsWith("**") && text.EndsWith("**") && text.Length > 4) { text = text.Substring(2, text.Length - 4); head = true; }
                if (text.StartsWith("- ") || text.StartsWith("* ")) { text = text.Substring(2); bullet = true; }
                text = text.Replace("**", "").Replace("`", "");
                var tb = new TextBlock
                {
                    Text = bullet ? "•  " + text : text, Foreground = t.Fg, FontFamily = t.Font, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = new Thickness(bullet ? 4 : 0, head && panel.Children.Count > 0 ? 8 : 2, 0, 0)
                };
                panel.Children.Add(tb);
            }
            return panel;
        }

        public UpdateToast(BarManager mgr, UpdateInfo u)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "vShell update";

            Theme t = Theme.From(mgr.Settings, 12.5, 10);
            var card = new Border
            {
                Background = t.PopupBg, BorderBrush = Theme.Faded(t.MainAccent, 70), BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(12), Width = 330,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4 }
            };
            var col = new StackPanel();
            var head = new DockPanel();
            var close = new TextBlock { Text = "✕", Foreground = t.FgDim, Cursor = Cursors.Hand, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Later" };
            close.MouseLeftButtonUp += delegate { Close(); };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(new TextBlock
            {
                Text = "vShell " + UpdateCenter.VersionText(u) + (u.PreRelease ? " (pre-release)" : "") + " is available",
                Foreground = t.Fg, FontFamily = t.Font, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
            });
            col.Children.Add(head);
            col.Children.Add(new TextBlock
            {
                Text = "You have " + Updater.CurrentText + ".", Foreground = t.FgDim, FontFamily = t.Font, FontSize = 12, Margin = new Thickness(0, 2, 0, 0)
            });
            string notes = (u.Notes ?? "").Trim();
            if (notes.Length > 0)
            {
                // the whole release notes, scrollable (mouse wheel or the thin bar on the right)
                var scroll = new ScrollViewer
                {
                    Content = NotesText(notes, t), MaxHeight = 230, Margin = new Thickness(0, 8, -6, 0),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false, Background = Brushes.Transparent, Padding = new Thickness(0, 0, 6, 0)
                };
                try { scroll.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(WidgetWindow.ScrollXaml)); }
                catch (Exception ex) { Log.Write("Update notes scroll style: " + ex.Message); }
                scroll.Resources["wThumb"] = Theme.Faded(t.FgDim, 70);
                col.Children.Add(scroll);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(Button("Install and restart", t, true, delegate { StartInstall(); }));
            buttons.Children.Add(Button("What's new", t, false, delegate { UpdateCenter.OpenPage(u); }));
            buttons.Children.Add(Button("Skip", t, false, delegate { UpdateCenter.Skip(u); }));
            col.Children.Add(buttons);

            // ---- progress (shown once Install is clicked) ----
            var progress = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            var status = new TextBlock { Foreground = t.Fg, FontFamily = t.Font, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            progress.Children.Add(status);
            var track = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
            var fillCol = new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) };
            var restCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            track.ColumnDefinitions.Add(fillCol);
            track.ColumnDefinitions.Add(restCol);
            var trackBg = new Border { Background = t.Track, CornerRadius = new CornerRadius(3) };
            Grid.SetColumnSpan(trackBg, 2);
            track.Children.Add(trackBg);
            track.Children.Add(new Border { Background = t.MainAccent, CornerRadius = new CornerRadius(3) });
            progress.Children.Add(track);
            var errorButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            errorButtons.Children.Add(Button("Try again", t, true, delegate { StartInstall(); }));
            errorButtons.Children.Add(Button("Close", t, false, delegate { Close(); }));
            progress.Children.Add(errorButtons);
            col.Children.Add(progress);
            card.Child = col;
            Content = card;

            double mb = u.FileSize > 0 ? u.FileSize / 1048576.0 : 0;
            Action draw = delegate
            {
                if (!UpdateCenter.Installing && UpdateCenter.InstallError.Length == 0) return;
                buttons.Visibility = Visibility.Collapsed;
                progress.Visibility = Visibility.Visible;
                double p = Math.Max(0, Math.Min(1, UpdateCenter.Progress));
                fillCol.Width = new GridLength(Math.Max(0.0001, p), GridUnitType.Star);
                restCol.Width = new GridLength(Math.Max(0.0001, 1 - p), GridUnitType.Star);
                bool failed = !UpdateCenter.Installing && UpdateCenter.InstallError.Length > 0;
                track.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
                errorButtons.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
                status.Foreground = failed ? StatusColors.Bad : (Brush)t.Fg;
                if (failed) status.Text = "The update didn't install: " + UpdateCenter.InstallError;
                else if (UpdateCenter.Stage == "restart") status.Text = "Installed - restarting vShell…";
                else if (UpdateCenter.Stage == "install") status.Text = "Checking and installing…";
                else status.Text = "Downloading…  " + Math.Round(p * 100) + "%" +
                                   (mb > 0 ? "  (" + (p * mb).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " of " + mb.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " MB)" : "");
            };
            Action onChange = delegate { Dispatcher.BeginInvoke(draw); };
            UpdateCenter.Changed += onChange;
            Closed += delegate { UpdateCenter.Changed -= onChange; };
            _startInstall = delegate
            {
                errorButtons.Visibility = Visibility.Collapsed;
                buttons.Visibility = Visibility.Collapsed;
                progress.Visibility = Visibility.Visible;
                status.Foreground = t.Fg;
                status.Text = "Starting the download…";
                track.Visibility = Visibility.Visible;
                UpdateCenter.Install(u, null);
            };

            Loaded += delegate { PlaceCorner(); };
            SizeChanged += delegate { if (IsLoaded) PlaceCorner(); };     // keeps its bottom edge in place when the progress / an error changes its height
        }

        /// <summary>Bottom-right of the main screen's work area (bars that reserve space are left alone).</summary>
        void PlaceCorner()
        {
            {
                MonitorInfo m = MonitorHelper.Primary();
                if (m == null) return;
                var src = PresentationSource.FromVisual(this);
                double sx = src != null ? src.CompositionTarget.TransformToDevice.M11 : 1, sy = src != null ? src.CompositionTarget.TransformToDevice.M22 : 1;
                Left = m.WorkArea.Right / sx - ActualWidth - 8;
                Top = m.WorkArea.Bottom / sy - ActualHeight - 8;
            }
        }

        Action _startInstall;
        void StartInstall() { if (_startInstall != null) _startInstall(); }

        static Border Button(string text, Theme t, bool main, Action click)
        {
            var b = new Border
            {
                Child = new TextBlock { Text = text, Foreground = main ? t.Bg : t.Fg, FontFamily = t.Font, FontSize = 12 },
                Background = main ? t.MainAccent : t.Track, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand
            };
            if (main) ((TextBlock)b.Child).Foreground = new SolidColorBrush(Contrast(t.MainAccent.Color));
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        /// <summary>Black or white, whichever reads better on c.</summary>
        static Color Contrast(Color c)
        {
            double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            return l > 150 ? Color.FromRgb(0x11, 0x11, 0x1B) : Colors.White;
        }
    }
}
