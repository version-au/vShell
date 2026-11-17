using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Owns the services (tasks, tray, stats), the bars, and the settings.</summary>
    public sealed class BarManager
    {
        [PluginApi] public BarSettings Settings { get; private set; }
        public readonly TaskList Tasks = new TaskList();
        public readonly SystemStats Stats = new SystemStats();
        public readonly GpuStats Gpu = new GpuStats();
        public TrayHost Tray { get; private set; }
        public int BarCount { get { return _bars.Count; } }

        /// <summary>The window showing this bar (null when it is not on screen) - the settings window previews it.</summary>
        public BarWindow BarWindowFor(BarPlacement p)
        {
            foreach (var b in _bars) if (b.Placement == p) return b;
            return null;
        }

        /// <summary>The window showing this widget (null when it is hidden).</summary>
        public WidgetWindow WidgetWindowFor(WidgetSettings w)
        {
            foreach (var x in _widgets) if (x.Cfg == w) return x;
            return null;
        }

        readonly List<BarWindow> _bars = new List<BarWindow>();
        readonly Dictionary<string, History> _histories = new Dictionary<string, History>();
        DispatcherTimer _tasksTimer, _statsTimer, _slowTimer, _taskbarTimer, _applyTimer, _repositionTimer, _tasksSoon;
        /// <summary>Shell hook + ITaskbarList receiver (see ShellHook).</summary>
        ShellHook _hook;
        SettingsWindow _settingsWin;
        PopupWindow _popup;
        WinKeyHook _winKey;
        readonly List<WidgetWindow> _widgets = new List<WidgetWindow>();
        bool _shuttingDown;
        /// <summary>The UI thread (BarManager is made on it): the settings timers below belong to it, whoever asks.</summary>
        readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

        // =================================================================
        public void Start()
        {
            MenuCloser.Install();      // right-click menus close when you click outside vShell
            UpdateCenter.NoteStartup();
            ProcessSampler.Gpu = Gpu;  // Top processes' GPU use
            PaletteUi.Install(this);    // the command palette's terminal / vShell / PC commands
            Settings = BarSettings.Load();
            PluginManager.Start(this);  // the plugins added (before the bars, so their modules are there)
            ApplyHa();

            // vShell runs alongside Explorer only: if an older version made it the Windows shell for this account, undo that
            // (Explorer starts again from the next sign-in)
            if (ShellServices.IsDefaultShell())
            {
                string err = ShellServices.RestoreDefaultShell();
                Log.Write(err == null ? "vShell was set as the Windows shell - Explorer is the shell again from the next sign-in" : "Could not restore Explorer as the shell: " + err);
            }

            // take the apps' tray icons by standing in front of Explorer's Shell_TrayWnd
            {
                var host = new TrayHost();
                if (host.Start(Settings.HideWindowsTaskbar)) Tray = host;
                else host.Dispose();
            }
            if (Tray != null)
            {
                Tray.Changed += OnTrayChanged;
                Tray.IconRect = TryGetTrayIconRect;
                Tray.ExplorerRestarted += OnExplorerRestarted;
                Tray.Balloon += OnBalloon;
            }
            _myTray = new VShellTray(this);
            _myTray.Show(Settings.ShowTrayIcon);

            if (Settings.HideWindowsTaskbar)
            {
                ShellServices.HideExplorerTaskbar();
                _taskbarHidden = true;
            }

            if (Settings.Bars.Count == 0)
            {
                // first start (no settings yet): the standard layout
                var prim = MonitorHelper.Primary();
                Settings.Bars.AddRange(BarPlacement.FirstRun(prim == null ? "" : prim.DeviceName));
                Settings.WelcomePending = true;         // the welcome guide opens once the bars are up
                Settings.Save();
                Log.Write("First start: created the default bottom and top bars.");
            }

            Stats.Update();
            Tasks.Refresh();
            foreach (var p in Settings.Bars) CreateBar(p);
            UpdateGpuState();
            UpdateWinKeyHook();

            Tasks.Changed += OnTasksChanged;
            StartShellHook();
            // "hidden behind app windows" counts only the windows the taskbar lists (ModuleVisibility, 0.38.224)
            ModuleVisibility.AppWindows = delegate
            {
                var list = new List<IntPtr>();
                foreach (TaskWindow w in Tasks.Windows) list.Add(w.Handle);
                return list;
            };

            // events drive the task list now; the poll is only a safety net
            _tasksTimer = MakeTimer(_hook != null ? 1500 : 400, delegate { Tasks.Refresh(); });
            _statsTimer = MakeTimer(1000, delegate
            {
                Stats.Update();
                foreach (var b in _bars.ToArray()) b.UpdateStats(Stats, true);
                foreach (var w in _widgets.ToArray()) w.UpdateStats(Stats, true);
            });
            _slowTimer = MakeTimer(2000, delegate
            {
                if (Tray != null) Tray.Prune();
                KeepDesktopLayer();
            });
            // Explorer shows its taskbar again now and then: hide it again within a quarter of a second (it was every 2 s, long enough
            // to see it). Cheap: two window lookups.
            _taskbarTimer = MakeTimer(250, delegate { if (Settings.HideWindowsTaskbar) ShellServices.KeepHidden(); });
            _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _applyTimer.Tick += delegate { _applyTimer.Stop(); ApplyNow(); };
            _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _repositionTimer.Tick += delegate
            {
                _repositionTimer.Stop();
                MonitorHelper.MonitorsChanged();
                RepositionAll();
                SyncWidgets(false);             // a widget whose monitor was off when vShell started is made now it's back
                QueueWidgetRelayout();          // snap links again (widgets back on a monitor that returned)
                if (_settingsWin != null) { _settingsWin.RefreshBars(); _settingsWin.RefreshWidgets(); }   // the "Monitor not connected" groups
                if (Settings.PerMonitorWallpaper) ApplyWallpapers();   // monitor layout changed
                try { NotifyCards.Relayout(); } catch { }               // open cards move back to their own monitor
            };
            // a monitor plugged in / unplugged: bars and widgets go back to their own monitors. Windows sends this when the layout
            // changes; a second pass a few seconds later catches a monitor that finishes waking up after the first one.
            Dispatcher ui = Dispatcher.CurrentDispatcher;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += delegate
            {
                ui.BeginInvoke(new Action(delegate
                {
                    MonitorHelper.MonitorsChanged();      // (its one-second cache: what's asked from now on is the new layout)
                    Log.Write("Monitors changed: " + MonitorHelper.GetAll().Count + " connected");
                    QueueReposition();
                    var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    later.Tick += delegate { later.Stop(); QueueReposition(); };
                    later.Start();
                }));
            };

            SyncWidgets();
            _lastGap = Settings.WidgetGap; _lastEdgeGap = Settings.WidgetEdgeGap;
            AutoLinkWidgetsSoon();
            UpdateNoteHotKey();
            UpdateRunHotKey();
            UpdateTermHotKey();
            UpdateGpuState();
            UpdateWinKeyHook();

            // wallpaper slideshows: every half minute, the ones that are due move on
            var slides = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            slides.Tick += delegate
            {
                if (!Settings.PerMonitorWallpaper) return;
                if (WallpaperService.SlideTick(Settings)) { ApplyWallpapers(); SaveSoon(); }
            };
            slides.Start();

            if (Settings.PerMonitorWallpaper)
            {
                var wp = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                wp.Tick += delegate { wp.Stop(); ApplyWallpapers(); Settings.Save(); };
                wp.Start();
            }

            UpdateCenter.Start(this);
            VolumeMixer.Start(this);
            ShellServices.HideMinimizedBoxes();     // no little caption boxes for minimised windows (restored on exit)
            // minimised windows are parked off-screen as soon as Windows says one is minimising; the timer is only a safety net now
            StartMinimizeHook();
            _parkTimer = MakeTimer(_minHook != IntPtr.Zero ? 1500 : 300, ShellServices.ParkMinimized);
            _fsTimer = MakeTimer(500, CheckFullScreen);
            if (Tray != null)
            {
                var once = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                once.Tick += delegate
                {
                    once.Stop();
                    TrayHost.BroadcastTaskbarCreated();      // apps (re-)send their icons to our Shell_TrayWnd
                };
                once.Start();
            }

            // a first start (or a guide that wasn't finished): the welcome guide, once the splash has gone and the bars are on screen
            if (Settings.WelcomePending)
            {
                var hello = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                hello.Tick += delegate { hello.Stop(); if (Settings.WelcomePending) ShowWelcome(); };
                hello.Start();
            }
        }

        DispatcherTimer _parkTimer, _fsTimer;
        WelcomeWindow _welcome;

        /// <summary>The welcome guide (a first start's steps: start-up, theme, layout, plugins) - one window, brought to the front if open.</summary>
        public void ShowWelcome()
        {
            if (_welcome == null)
            {
                _welcome = new WelcomeWindow(this);
                _welcome.Closed += delegate { _welcome = null; };
                _welcome.Show();
            }
            _welcome.Activate();
        }

        /// <summary>Open Settings on the Plugins page.</summary>
        public void OpenPluginsPage()
        {
            OpenSettings(null);
            if (_settingsWin != null) _settingsWin.ShowPlugins();
        }

        // =================================================================
        // Full-screen apps (ported from ManagedShell's FullScreenHelper)
        // =================================================================
        sealed class FsApp
        {
            public IntPtr Hwnd;
            public IntPtr Monitor;      // zero when it spans every monitor
            public bool FromHook;       // Windows / the app itself said it is full screen: trust that over its size
        }

        readonly List<FsApp> _fsApps = new List<FsApp>();
        readonly List<FsApp> _fsInactive = new List<FsApp>();   // still full screen, but something else on its monitor is in front

        /// <summary>Windows (HSHELL_FULLSCREENENTER/EXIT) or the app (ITaskbarList::MarkFullscreenWindow) told us about a full-screen window.</summary>
        void OnFullScreenNotice(IntPtr hwnd, bool entering)
        {
            _fsInactive.RemoveAll(delegate (FsApp a) { return a.Hwnd == hwnd; });
            bool listed = _fsApps.Exists(delegate (FsApp a) { return a.Hwnd == hwnd; });
            if (entering)
            {
                if (listed) { foreach (var a in _fsApps) if (a.Hwnd == hwnd) a.FromHook = true; }
                else
                {
                    var app = GetFullScreenApp(hwnd, true);
                    if (app != null) _fsApps.Add(app);
                }
            }
            else _fsApps.RemoveAll(delegate (FsApp a) { return a.Hwnd == hwnd; });
            ApplyFullScreen();
        }

        /// <summary>
        /// Full-screen apps (games, video, F11 browsers): the bars on that monitor drop behind them and come back when they go.
        /// A full-screen app stays "full screen" while you work on another monitor, and goes inactive (bars return) only when
        /// something else is brought to the front on its own monitor.
        /// </summary>
        void CheckFullScreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            IntPtr fgMon = MonitorHelper.HandleFromWindow(fg);
            bool skipAdd = false;

            foreach (var app in _fsApps.ToArray())
            {
                FsApp now = GetFullScreenApp(app.Hwnd, app.FromHook);
                if (now != null)
                {
                    if (app.Hwnd == fg)
                    {
                        if (app.Monitor == now.Monitor) { skipAdd = true; continue; }      // still full screen, still in front
                        // moved to another monitor: re-added below
                    }
                    else if (now.Monitor != IntPtr.Zero && fgMon != now.Monitor) continue;   // in front of something on another monitor: keep
                    else
                    {
                        long ex = Native.GetWindowLongAuto(fg, Native.GWL_EXSTYLE);
                        if ((ex & Native.WS_EX_TOPMOST) != 0) continue;    // a topmost popup (volume OSD, overlay) does not count
                        _fsInactive.Add(app);
                    }
                }
                _fsApps.Remove(app);
            }

            foreach (var app in _fsInactive.ToArray())
            {
                FsApp now = GetFullScreenApp(app.Hwnd, app.FromHook);
                if (now == null) _fsInactive.Remove(app);
                else app.Monitor = now.Monitor;
            }

            if (!skipAdd && fg != IntPtr.Zero)
            {
                FsApp back = _fsInactive.Find(delegate (FsApp a) { return a.Hwnd == fg; });
                FsApp add = back != null ? GetFullScreenApp(fg, back.FromHook) : GetFullScreenApp(fg, false);
                if (add != null)
                {
                    if (back != null) { _fsInactive.Remove(back); add.FromHook = back.FromHook; }
                    _fsApps.Add(add);
                }
            }
            ApplyFullScreen();
        }

        /// <summary>A full-screen app (game, video) is in front on some monitor (notifications wait).</summary>
        public bool FullScreenActive { get { return _fsApps.Count > 0; } }

        void ApplyFullScreen()
        {
            // a menu / popup open from a bar (the Start menu with the Windows key, ...): that bar comes over the full-screen app with it
            PopupWindow open = _popup != null && _popup.IsOpen ? _popup : null;
            BarWindow popupBar = open != null ? open.Host as BarWindow : null;
            bool raised = false;
            foreach (var b in _bars)
            {
                bool under = false;
                foreach (var a in _fsApps)
                    if (a.Monitor == IntPtr.Zero || a.Monitor == b.MonitorHandle) { under = true; break; }
                if (under && b == popupBar) { under = false; raised = true; }
                b.Under = under;
            }
            if (raised) open.BringToTop();
        }

        FsApp GetFullScreenApp(IntPtr h, bool fromHook)
        {
            if (!CanFullScreen(h)) return null;
            IntPtr mon = MonitorHelper.HandleFromWindow(h);
            if (fromHook) return new FsApp { Hwnd = h, Monitor = mon, FromHook = true };

            RECT r = EffectiveRect(h);
            var all = MonitorHelper.GetAll();
            foreach (var m in all)
                if (Covers(r, m.Bounds)) return new FsApp { Hwnd = h, Monitor = m.Handle };
            if (all.Count > 1)
            {
                // spread over every monitor (some games / video walls)
                RECT v = all[0].Bounds;
                foreach (var m in all)
                {
                    v.Left = Math.Min(v.Left, m.Bounds.Left); v.Top = Math.Min(v.Top, m.Bounds.Top);
                    v.Right = Math.Max(v.Right, m.Bounds.Right); v.Bottom = Math.Max(v.Bottom, m.Bounds.Bottom);
                }
                if (Covers(r, v)) return new FsApp { Hwnd = h, Monitor = IntPtr.Zero };
            }
            return null;
        }

        static bool Covers(RECT r, RECT m)
        {
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }

        /// <summary>
        /// For windows with a frame (caption + sizing border, or popup + border) the client area is what counts: a maximised normal
        /// window is not full screen, a borderless one sized to the monitor is.
        /// </summary>
        static RECT EffectiveRect(IntPtr h)
        {
            long st = Native.GetWindowLongAuto(h, Native.GWL_STYLE);
            const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_POPUP = 0x80000000, WS_BORDER = 0x00800000;
            RECT r;
            if ((st & (WS_CAPTION | WS_THICKFRAME)) == (WS_CAPTION | WS_THICKFRAME) ||
                (st & (WS_POPUP | WS_THICKFRAME)) == (WS_POPUP | WS_THICKFRAME) ||
                (st & (WS_POPUP | WS_BORDER)) == (WS_POPUP | WS_BORDER))
            {
                Native.GetClientRect(h, out r);
                Native.MapWindowPoints(h, IntPtr.Zero, ref r, 2);
            }
            else Native.GetWindowRect(h, out r);
            return r;
        }

        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

        static bool CanFullScreen(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return false;
            if (Native.GetProp(h, "NonRudeHWND") != IntPtr.Zero) return false;     // the desktop and other shell windows mark themselves
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return false;
            long ex = Native.GetWindowLongAuto(h, Native.GWL_EXSTYLE);
            if (TaskList.IsImmersiveShellWindow(h, ex)) return false;
            string cls = Native.GetClass(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
            const long WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
            if ((ex & WS_EX_LAYERED) != 0 && ((ex & WS_EX_TRANSPARENT) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0)) return false;   // click-through overlays
            return true;
        }

        static DispatcherTimer MakeTimer(int ms, Action tick)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { try { tick(); } catch (Exception ex) { Log.Write("Timer: " + ex); } };
            t.Start();
            return t;
        }

        BarWindow CreateBar(BarPlacement p)
        {
            var w = new BarWindow(this, p);
            _bars.Add(w);
            BarsMoved();
            w.Show();
            if (w.Displaced) w.ApplyPlacement();     // its monitor is off: Show() put it on screen anyway - hide it again
            return w;
        }

        /// <summary>Graph history of one metric module; survives the bars being rebuilt when settings change.</summary>
        [PluginApi] public History HistoryFor(string moduleId)
        {
            History h;
            if (!_histories.TryGetValue(moduleId, out h)) { h = new History(); _histories[moduleId] = h; }
            return h;
        }

        // =================================================================
        // Wallpaper
        // =================================================================
        /// <summary>Apply the per-monitor wallpapers: one spanned image set as the Windows wallpaper. Returns an error message or null.</summary>
        public string ApplyWallpapers()
        {
            return WallpaperService.Apply(Settings);
        }

        /// <summary>Every wallpaper slideshow moves on now.</summary>
        public void NextWallpaper()
        {
            if (WallpaperService.NextAll(Settings)) { ApplyWallpapers(); SaveSoon(); }
        }

        public void RestoreWallpapers()
        {
            WallpaperService.Restore(Settings);
        }

        /// <summary>Show a popup, closing any other one that is open.</summary>
        [PluginApi] public void ShowPopup(PopupWindow p)
        {
            CloseOpenPopup();
            _popup = p;
            p.Closed += delegate
            {
                if (_popup == p) _popup = null;
                // its bar goes back behind a full-screen app (once you click back into it)
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(ApplyFullScreen));
            };
            p.Show();
            ApplyFullScreen();
        }

        /// <summary>A popup from this bar / widget is open (a widget sliding away at its screen edge stays out meanwhile).</summary>
        public bool PopupOpenFor(IModuleHost host) { return _popup != null && _popup.IsOpen && _popup.Host == host; }

        public void CloseOpenPopup()
        {
            var p = _popup;
            _popup = null;
            if (p != null) p.CloseSafe();
        }

        // =================================================================
        // Widgets and desktop icons
        // =================================================================
        /// <summary>Create, update and close widget windows so they match the settings.</summary>
        /// <summary>all = rebuild every widget's look; otherwise only those whose own settings changed.</summary>
        void SyncWidgets(bool all = true)
        {
            foreach (var w in _widgets.ToArray())
                if (!Settings.Widgets.Contains(w.Cfg) || !w.Cfg.Enabled) { _widgets.Remove(w); try { w.Close(); } catch { } }

            foreach (var cfg in Settings.Widgets)
            {
                if (!cfg.Enabled) continue;
                WidgetWindow win = null;
                foreach (var w in _widgets) if (w.Cfg == cfg) win = w;
                bool made = win == null;
                if (made && !MonitorHelper.IsConnected(cfg.Monitor, cfg.MonitorKey)) continue;     // its monitor is off: made when it's back
                if (made)
                {
                    win = new WidgetWindow(this, cfg);
                    _widgets.Add(win);
                    win.Show();                 // builds its modules and moves to its saved position when the window handle exists
                }
                string sig = Sig(cfg);
                if (!made)
                {
                    string old;
                    if (all || !_widgetSig.TryGetValue(cfg, out old) || old != sig) win.ApplySettings();
                    win.ApplyPosition();
                }
                _widgetSig[cfg] = sig;
            }
        }

        /// <summary>Keep widgets and icons on the desktop level and the wallpaper beneath them (Explorer or a click on the desktop can reshuffle it).</summary>
        void KeepDesktopLayer()
        {
            if (Settings.WallpaperTheme && WallpaperColor.Changed(Settings)) SettingsChangedAll();
            Backdrop.Check(Settings);      // blur: follow wallpaper changes
            foreach (var w in _widgets) w.PlaceZ();
            foreach (var b in _bars) if (b.OnDesktop) b.PlaceZ();
        }

        /// <summary>The widgets on screen (for snapping and aligning).</summary>
        public IEnumerable<WidgetWindow> WidgetWindows { get { return _widgets.ToArray(); } }
        public IEnumerable<BarWindow> BarWindows { get { return _bars.ToArray(); } }

        IEnumerable<StartMenuModule> AllStartMenus()
        {
            foreach (var b in _bars)
                foreach (var m in b.StartMenus) yield return m;
            foreach (var w in _widgets)
                foreach (var m in w.StartMenus) yield return m;
        }

        /// <summary>The Start menus on screen (not on a bar or widget hidden while its monitor is off).</summary>
        IEnumerable<StartMenuModule> ShownStartMenus()
        {
            foreach (var b in _bars)
                if (!b.Displaced) foreach (var m in b.StartMenus) yield return m;
            foreach (var w in _widgets)
                if (!w.Away) foreach (var m in w.StartMenus) yield return m;
        }

        public WidgetSettings AddWidget()
        {
            var mon = MonitorHelper.Primary();
            int n = Settings.Widgets.Count;
            var cfg = new WidgetSettings
            {
                Name = "Widget " + (n + 1),
                Monitor = mon == null ? "" : mon.DeviceName,
                MonitorKey = mon == null ? "" : mon.Key,
                OffsetX = 120 + 36 * (n % 8),
                OffsetY = 120 + 36 * (n % 8)
            };
            // a new widget starts empty: add its modules in Settings
            if (Settings.WidgetStyle != null) Styles.ApplyWidget(cfg, Settings.WidgetStyle);     // the widget style in use
            Settings.Widgets.Add(cfg);
            SettingsChanged();
            return cfg;
        }

        public void RemoveWidget(WidgetSettings cfg)
        {
            if (Settings.Widgets.Remove(cfg)) SettingsChanged();
        }

        /// <summary>Open Settings on the Widgets tab with this widget selected.</summary>
        public void OpenWidgetSettings(WidgetSettings cfg)
        {
            OpenSettings(null);
            if (_settingsWin != null && cfg != null) _settingsWin.SelectWidget(cfg);
        }

        public void WidgetMoved() { Settings.Save(); }

        // ---- snap links: widgets linked to one that changed size (or to a gap that changed) move to keep the gap ----
        DispatcherTimer _relayoutTimer;
        int _lastGap = -1, _lastEdgeGap = -1;

        /// <summary>A widget changed size (or a gap / link changed): put the snapped widgets right a moment from now.</summary>
        public void QueueWidgetRelayout()
        {
            if (_relayoutTimer == null)
            {
                _relayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _relayoutTimer.Tick += delegate
                {
                    _relayoutTimer.Stop();
                    bool ran = false;
                    try { ran = WidgetSnap.Relayout(this); }
                    catch (Exception ex) { Log.Write("Widget relayout: " + ex.Message); ran = true; }
                    if (!ran) _relayoutTimer.Start();          // a widget is being dragged / resized: after that
                };
            }
            _relayoutTimer.Stop();
            _relayoutTimer.Start();
        }

        // ---- the quick-note key (Win+Shift+N) ----
        GlobalHotKey _noteKey;

        /// <summary>Listen for Win+Shift+N only while a notes module asks for it.</summary>
        void UpdateNoteHotKey()
        {
            bool want = false;
            foreach (WidgetSettings w in Settings.Widgets)
                if (w.Enabled)
                    foreach (ModuleConfig m in w.Modules) if (m.Type == ModuleTypes.Notes && m.NoteHotkey) want = true;
            if (want && _noteKey == null)
            {
                _noteKey = new GlobalHotKey();
                _noteKey.Pressed += QuickNote;
                _noteKey.Register(GlobalHotKey.MOD_WIN | GlobalHotKey.MOD_SHIFT, 0x4E);      // N; if Windows refuses it, that is logged (and not retried on every apply)
            }
            else if (!want && _noteKey != null) { _noteKey.Dispose(); _noteKey = null; }
        }

        // ---- the run box keys (Ctrl+Alt+Space ...) ----
        GlobalHotKey _runKey;
        string _runKeyCombo = "Off";

        /// <summary>The first run box (bars first, then widgets) with keys set gets them; re-registered when the choice changes.</summary>
        void UpdateRunHotKey()
        {
            string want = "Off";
            foreach (BarPlacement b in Settings.Bars)
                if (want == "Off" && b.Modules != null)
                    foreach (ModuleConfig m in b.Modules) if (m.Type == ModuleTypes.RunBox && m.RunHotkey != "Off") { want = m.RunHotkey; break; }
            foreach (WidgetSettings w in Settings.Widgets)
                if (want == "Off" && w.Enabled)
                    foreach (ModuleConfig m in w.Modules) if (m.Type == ModuleTypes.RunBox && m.RunHotkey != "Off") { want = m.RunHotkey; break; }
            if (want == _runKeyCombo && (_runKey != null) == (want != "Off")) return;
            if (_runKey != null) { _runKey.Dispose(); _runKey = null; }
            _runKeyCombo = want;
            if (want == "Off") return;
            uint mods = 0;
            if (want.Contains("Ctrl")) mods |= GlobalHotKey.MOD_CONTROL;
            if (want.Contains("Alt")) mods |= GlobalHotKey.MOD_ALT;
            if (want.Contains("Shift")) mods |= GlobalHotKey.MOD_SHIFT;
            _runKey = new GlobalHotKey();
            _runKey.Pressed += OpenRunBox;
            Log.Write("Run box keys: " + want + (_runKey.Register(mods, 0x20) ? " registered" : " NOT registered - another program has them"));       // Space
        }

        /// <summary>The run box keys: open the run box that has them, ready to type.</summary>
        void OpenRunBox()
        {
            foreach (BarWindow b in _bars.ToArray())
                foreach (RunBoxModule r in b.RunBoxes) if (r.Config.RunHotkey == _runKeyCombo) { r.Open(true); return; }
            foreach (WidgetWindow w in _widgets.ToArray())
                foreach (RunBoxModule r in w.RunBoxes) if (r.Config.RunHotkey == _runKeyCombo) { r.Open(true); return; }
        }

        // ---- vShell's own tray icon, and what its menu (and the bars' menu) does ----
        VShellTray _myTray;

        /// <summary>Settings > Updates, checking for an update (its Updates card shows how it went).</summary>
        public void CheckForUpdates()
        {
            OpenSettings(null);
            if (_settingsWin != null) _settingsWin.ShowUpdates();
            UpdateCenter.CheckNow(true);
        }

        /// <summary>Settings > Updates, where the vSuite apps are (vTerm not installed: the terminal offers this).</summary>
        public void ShowVSuiteApps()
        {
            OpenSettings(null);
            if (_settingsWin != null) _settingsWin.ShowUpdates();
        }

        /// <summary>Start vShell again (settings saved first).</summary>
        public void RestartVShell()
        {
            string why = PluginManager.Restart();
            if (why != null) Questions.Tell(null, why, "Restart vShell");
        }

        // ---- the terminal keys (Ctrl+Alt+T ...) ----
        GlobalHotKey _termKey;
        string _termKeyCombo = "Off";

        /// <summary>The first Terminal module (on a bar) with keys set gets them.</summary>
        void UpdateTermHotKey()
        {
            string want = "Off";
            foreach (BarPlacement b in Settings.Bars)
                if (want == "Off" && b.Modules != null)
                    foreach (ModuleConfig m in b.Modules) if (m.Type == ModuleTypes.Terminal && m.TermHotkey != "Off") { want = m.TermHotkey; break; }
            if (want == _termKeyCombo && (_termKey != null) == (want != "Off")) return;
            if (_termKey != null) { _termKey.Dispose(); _termKey = null; }
            _termKeyCombo = want;
            if (want == "Off") return;
            uint mods = 0;
            if (want.Contains("Ctrl")) mods |= GlobalHotKey.MOD_CONTROL;
            if (want.Contains("Alt")) mods |= GlobalHotKey.MOD_ALT;
            if (want.Contains("Shift")) mods |= GlobalHotKey.MOD_SHIFT;
            uint vk = want.EndsWith("`") ? 0xC0u : 0x54u;      // VK_OEM_3 (the ` key) or T
            _termKey = new GlobalHotKey();
            _termKey.Pressed += OpenTerminal;
            Log.Write("Terminal keys: " + want + (_termKey.Register(mods, vk) ? " registered" : " NOT registered - another program has them"));
        }

        /// <summary>The terminal keys: show / hide the terminal from the module that has them.</summary>
        void OpenTerminal()
        {
            foreach (BarWindow b in _bars.ToArray())
                foreach (TerminalModule t in b.Terminals) if (t.Config.TermHotkey == _termKeyCombo) { t.Toggle(); return; }
        }

        /// <summary>Win+Shift+N: the first note that has the key on starts typing.</summary>
        void QuickNote()
        {
            foreach (WidgetWindow w in _widgets.ToArray())
                foreach (NotesModule n in w.NoteModules)
                    if (n.Config.NoteHotkey) { n.QuickNote(); return; }
        }

        /// <summary>The gaps changed: snapped widgets take the new gap (when "Re-space snapped widgets" is on).</summary>
        void CheckGapChange()
        {
            bool g = _lastGap >= 0 && Settings.WidgetGap != _lastGap, e = _lastEdgeGap >= 0 && Settings.WidgetEdgeGap != _lastEdgeGap;
            if ((g || e) && Settings.RespaceSnapped) WidgetSnap.Respace(Settings, g, e);
            _lastGap = Settings.WidgetGap;
            _lastEdgeGap = Settings.WidgetEdgeGap;
        }

        /// <summary>First start after 0.23: once the widgets are on screen and laid out, link the ones already snapped together.</summary>
        void AutoLinkWidgetsSoon()
        {
            if (Settings.WidgetBoxVersion >= 2) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            t.Tick += delegate
            {
                t.Stop();
                try { WidgetSnap.AutoLink(this); }
                catch (Exception ex) { Log.Write("Widget auto-link: " + ex.Message); }
                Settings.WidgetBoxVersion = 2;
                Settings.Save();
            };
            t.Start();
        }
        public void WidgetsEditedOutsideSettings() { if (_settingsWin != null) _settingsWin.RefreshWidgets(); }

        // =================================================================
        // Start menu / Windows key
        // =================================================================
        /// <summary>Listen for the Windows key only while some Start menu module wants it.</summary>
        void UpdateWinKeyHook()
        {
            bool need = false;
            foreach (var m in AllStartMenus())
                if (m.Config.UseWinKey) need = true;

            if (need && _winKey == null)
            {
                var hook = new WinKeyHook();
                hook.Pressed += ToggleStartMenu;
                if (hook.Start()) _winKey = hook;
            }
            else if (!need && _winKey != null)
            {
                _winKey.Dispose();
                _winKey = null;
            }
        }

        /// <summary>A lone Windows-key press: close the open menu, else open the one on the monitor where the mouse is.</summary>
        void ToggleStartMenu()
        {
            if (_shuttingDown) return;
            foreach (var m in AllStartMenus())
                if (m.IsMenuOpen) { m.Close(); return; }

            POINT c = Native.CursorPos();
            IntPtr mon = Native.MonitorFromPoint(c, Native.MONITOR_DEFAULTTONEAREST);
            StartMenuModule pick = null;
            foreach (var m in ShownStartMenus())
            {
                if (!m.Config.UseWinKey) continue;
                if (pick == null || m.MonitorHandle == mon) pick = m;
                if (m.MonitorHandle == mon) { pick.Toggle(); return; }
            }
            if (pick != null) pick.Toggle();
        }

        /// <summary>Only sample the GPU performance counters while a GPU module exists.</summary>
        void UpdateGpuState()
        {
            bool need = false;
            foreach (var b in _bars) if (b.NeedsGpu) need = true;
            foreach (var w in _widgets) if (w.NeedsGpu) need = true;
            if (need) Gpu.Start(); else Gpu.Stop();
        }

        // =================================================================
        // Events from services
        // =================================================================
        // =================================================================
        // Shell hook: instant task list updates, flashing, progress, badges, full-screen notices
        // =================================================================
        void StartShellHook()
        {
            var hook = new ShellHook();
            if (!hook.Start()) { hook.Dispose(); return; }
            _hook = hook;
            _tasksSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _tasksSoon.Tick += delegate { _tasksSoon.Stop(); try { Tasks.Refresh(); } catch (Exception ex) { Log.Write("Tasks: " + ex.Message); } };

            hook.Changed += QueueTasksRefresh;
            hook.Activated += delegate (IntPtr h) { QueueTasksRefresh(); CheckFullScreen(); };
            hook.Flash += delegate (IntPtr h, bool on) { Tasks.SetFlashing(h, on); };
            hook.ProgressState += delegate (IntPtr h, int st) { Tasks.SetProgressState(h, st); QueueTasksRefresh(); };
            hook.ProgressValue += delegate (IntPtr h, int v) { Tasks.SetProgressValue(h, v); QueueTasksRefresh(); };
            hook.Overlay += delegate (IntPtr h, IntPtr ic) { Tasks.SetOverlay(h, ic); QueueTasksRefresh(); };
            hook.OverlayText += delegate (IntPtr h, string t) { Tasks.SetOverlayText(h, t); QueueTasksRefresh(); };
            hook.FullScreen += OnFullScreenNotice;
            hook.TaskbarCreated += ClaimTaskband;

            Tasks.ButtonCreatedMsg = hook.TaskbarButtonCreatedMsg;
            ClaimTaskband();
        }

        /// <summary>Point apps' ITaskbarList calls (progress, badges) at our hook window.</summary>
        void ClaimTaskband()
        {
            if (_hook == null || Tray == null) return;
            _hook.ClaimTaskband(Tray.Handle, ShellServices.ExplorerTaskbarHandle());
        }

        void QueueTasksRefresh()
        {
            if (_tasksSoon != null && !_tasksSoon.IsEnabled) _tasksSoon.Start();
        }

        void OnTasksChanged()
        {
            foreach (var b in _bars.ToArray()) b.RefreshTasks();
        }

        bool TryGetTrayIconRect(TrayIcon ic, out RECT r)
        {
            r = new RECT();
            foreach (var b in _bars.ToArray()) if (b.TryGetTrayIconRect(ic, out r)) return true;
            return false;
        }

        /// <summary>
        /// Explorer was restarted (crash, or killed and started again): its new taskbar has forgotten our app bars and is showing again,
        /// and apps have just re-added their tray icons to it. Give it a moment to settle, then hide it, re-register the bars and ask the
        /// apps to send their icons to us again.
        /// </summary>
        void OnExplorerRestarted()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += delegate
            {
                t.Stop();
                if (_shuttingDown) return;
                if (Settings.HideWindowsTaskbar) ShellServices.HideExplorerTaskbar();
                foreach (var b in _bars.ToArray()) b.ReRegisterAppBar();
                ClaimTaskband();
                if (Tray != null) TrayHost.BroadcastTaskbarCreated();
            };
            t.Start();
        }

        BalloonWindow _balloon;

        /// <summary>A tray icon sent a balloon notification: show it beside the icon (one at a time), or take it down.</summary>
        void OnBalloon(TrayIcon ic, TrayBalloon b)
        {
            if (string.IsNullOrEmpty(b.Text))
            {
                if (_balloon != null && _balloon.ForIcon == ic) { _balloon.Dismiss(); _balloon = null; }
                return;
            }
            if (!Settings.ShowTrayBalloons || _bars.Count == 0) return;

            BarWindow bar = null;
            foreach (var x in _bars) if (x.HasTray) { bar = x; break; }
            if (bar == null) bar = _bars[0];
            if (bar.Under) return;     // a full-screen app (game, video) is in front: stay quiet

            RECT barRect;
            Native.GetWindowRect(bar.Handle, out barRect);
            RECT anchor = barRect, icon;
            if (bar.TryGetTrayIconRect(ic, out icon)) { anchor.Left = icon.Left; anchor.Right = icon.Right; }
            else { anchor.Left = barRect.Right - 40; anchor.Right = barRect.Right; }

            if (_balloon != null) _balloon.Dismiss();
            try
            {
                var w = new BalloonWindow(this, Tray, ic, b, bar.Theme, anchor, bar.Placement.Edge != "Top");
                w.Closed += delegate { if (_balloon == w) _balloon = null; };
                _balloon = w;
                w.Show();
            }
            catch (Exception ex) { Log.Write("Balloon: " + ex.Message); }
        }

        void OnTrayChanged()
        {
            foreach (var b in _bars.ToArray()) b.RefreshTray();
        }

        // =================================================================
        // Settings
        // =================================================================
        /// <summary>Called whenever settings (or a bar's module list) change; coalesced, then saved and applied to every bar.</summary>
        public void SettingsChanged()
        {
            if (!_ui.CheckAccess()) { _ui.BeginInvoke(new Action(SettingsChanged)); return; }     // (a plugin's poller / watcher thread)
            if (_applyTimer == null) return;
            _applyTimer.Stop();
            _applyTimer.Start();
        }

        /// <summary>Hand the Home Assistant connection settings to the client (does nothing unless they changed).</summary>
        void ApplyHa()
        {
            TimeText.Use24 = Settings.Use24HourTimes;
            HwInfo.Use(Settings.SensorSource, Settings.LhmPort);
            Pills.Configure(Settings);
            PluginManager.Configure(Settings);       // the plugins' integrations (Komodo, Seerr ...)
            NotifyWatch.Configure(Settings);
            NotifyCards.Hook(this);
        }

        // ---- what changed since the last apply (so a slider in Settings only rebuilds the bar / widget it belongs to) ----
        string _globalSig;
        bool _forceAll = true;
        readonly Dictionary<BarPlacement, string> _barSig = new Dictionary<BarPlacement, string>();
        readonly Dictionary<WidgetSettings, string> _widgetSig = new Dictionary<WidgetSettings, string>();
        DispatcherTimer _saveTimer;

        /// <summary>Something outside the settings file changed what bars / widgets look like (e.g. the wallpaper under Wallpaper Theme): rebuild them all.</summary>
        [PluginApi] public void SettingsChangedAll()
        {
            _forceAll = true;
            SettingsChanged();
        }

        /// <summary>Write settings.json a moment from now (several quick changes - a slider being dragged - become one write).</summary>
        [PluginApi] public void SaveSoon()
        {
            // from another thread (a plugin's poller / watcher): over to the UI thread - a DispatcherTimer made here would belong to a
            // thread that never runs it, and nothing would ever save again
            if (!_ui.CheckAccess()) { _ui.BeginInvoke(new Action(SaveSoon)); return; }
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                _saveTimer.Tick += delegate { _saveTimer.Stop(); Settings.Save(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        static string Sig(object o)
        {
            try { return System.Text.Json.JsonSerializer.Serialize(o); } catch { return Guid.NewGuid().ToString(); }
        }

        /// <summary>Every setting except the bars and widgets themselves (theme, connections, desktop...).</summary>
        string GlobalSig()
        {
            // the settings as JSON with Bars / Widgets taken out of the copy - the settings object itself is left alone: the notification
            // check and the plugins' watchers read it on their own threads meanwhile
            try
            {
                var o = System.Text.Json.JsonSerializer.SerializeToNode(Settings) as System.Text.Json.Nodes.JsonObject;
                if (o == null) return Guid.NewGuid().ToString();
                o.Remove("Bars");
                o.Remove("Widgets");
                return o.ToJsonString() + "|" + Settings.Bars.Count + "|" + Settings.Widgets.Count;
            }
            catch { return Guid.NewGuid().ToString(); }
        }

        // ---- minimise events (park minimised windows straight away instead of polling 3 times a second) ----
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);
        const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, WINEVENT_OUTOFCONTEXT = 0;
        IntPtr _minHook;
        WinEventProc _minProc;      // kept in a field so the garbage collector leaves it alone while Windows calls it
        DispatcherTimer _parkSoon;

        void StartMinimizeHook()
        {
            try
            {
                _parkSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                _parkSoon.Tick += delegate { _parkSoon.Stop(); ShellServices.ParkMinimized(); };
                _minProc = delegate (IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
                {
                    if (idObject != 0) return;          // the window itself, not a part of it
                    _parkSoon.Stop();
                    _parkSoon.Start();                  // after the minimise animation has started
                };
                _minHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, _minProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
            catch (Exception ex) { Log.Write("Minimise hook: " + ex.Message); _minHook = IntPtr.Zero; }
        }

        void StopMinimizeHook()
        {
            if (_minHook != IntPtr.Zero) { try { UnhookWinEvent(_minHook); } catch { } _minHook = IntPtr.Zero; }
            if (_parkSoon != null) _parkSoon.Stop();
        }

        /// <summary>Whether Explorer's taskbar was last hidden (true) or given back (false) - null until the first time.</summary>
        bool? _taskbarHidden;

        void ApplyNow()
        {
            Settings.Normalize();
            SaveSoon();
            ApplyHa();

            // Explorer's taskbar only when "Hide the Windows taskbar" changed: setting its state makes Explorer tell every window
            // that settings changed (Explorer's scrollbars flashed on every change in Settings); the timer keeps it hidden meanwhile
            if (_taskbarHidden != Settings.HideWindowsTaskbar)
            {
                if (Settings.HideWindowsTaskbar) ShellServices.HideExplorerTaskbar();
                else ShellServices.RestoreExplorerTaskbar();
                _taskbarHidden = Settings.HideWindowsTaskbar;
            }

            if (_myTray != null) _myTray.Show(Settings.ShowTrayIcon);
            Backdrop.Check(Settings);      // blur: the blurred wallpapers are made again when the wallpaper / blur kind changed
            WinTheme.Apply(Settings);      // Windows' light / dark mode and accent colour, when matching is on
            CheckGapChange();              // before the signatures, so new link gaps do not rebuild the widgets a second time

            // the theme or anything shared changed: everything; otherwise only the bars / widgets whose own settings changed
            string g = GlobalSig();
            bool all = _forceAll || g != _globalSig;
            _globalSig = g;
            _forceAll = false;
            foreach (var b in _bars.ToArray())
            {
                string s = Sig(b.Placement), old;
                if (all || !_barSig.TryGetValue(b.Placement, out old) || old != s) b.ApplySettings();
                _barSig[b.Placement] = s;
            }
            SyncWidgets(all);
            foreach (var k in new List<BarPlacement>(_barSig.Keys)) if (!Settings.Bars.Contains(k)) _barSig.Remove(k);
            foreach (var k in new List<WidgetSettings>(_widgetSig.Keys)) if (!Settings.Widgets.Contains(k)) _widgetSig.Remove(k);
            UpdateGpuState();
            UpdateWinKeyHook();
            UpdateNoteHotKey();
            UpdateRunHotKey();
            UpdateTermHotKey();
            RepositionAll();
            QueueWidgetRelayout();
            if (_settingsWin != null) _settingsWin.RefreshAfterExternalChange();
        }

        // =================================================================
        // Bars
        // =================================================================
        public void QueueReposition()
        {
            if (_repositionTimer == null) return;
            _repositionTimer.Stop();
            _repositionTimer.Start();
        }

        public void RepositionAll()
        {
            foreach (var b in _bars.ToArray()) b.ApplyPlacement();
            foreach (var w in _widgets.ToArray()) w.ApplyPosition();
        }

        public void OnBarMoved()
        {
            Settings.Save();
            RepositionAll();
            if (_settingsWin != null) _settingsWin.RefreshBars();
        }

        public void AddBar()
        {
            var monitors = MonitorHelper.GetAll();
            if (monitors.Count == 0) return;

            // Prefer a monitor that has no bar yet; otherwise the primary monitor.
            MonitorInfo target = monitors[0];
            foreach (var m in monitors)
            {
                bool used = false;
                foreach (var p in Settings.Bars)
                    if (string.Equals(p.Monitor, m.DeviceName, StringComparison.OrdinalIgnoreCase)) used = true;
                if (!used) { target = m; break; }
            }

            bool bottomTaken = false;
            foreach (var p in Settings.Bars)
                if (string.Equals(p.Monitor, target.DeviceName, StringComparison.OrdinalIgnoreCase) && p.Edge == "Bottom") bottomTaken = true;

            // a new bar starts empty: add its modules in Settings
            var np = new BarPlacement
            {
                Monitor = target.DeviceName,
                MonitorKey = target.Key,
                Edge = bottomTaken ? "Top" : "Bottom",
                Modules = new List<ModuleConfig>()
            };
            if (Settings.Bars.Count > 0)      // new bars get the first one's size; corners and outline are the defaults (13, 3 px at 65 %)
            {
                var first = Settings.Bars[0];
                np.BarHeight = first.BarHeight; np.FontSize = first.FontSize;
                np.EdgeMargin = first.EdgeMargin; np.FullWidth = first.FullWidth;
            }
            // the bar style's look for this bar's layout
            if (Settings.BarStyle != null) { string lay = Styles.LayoutOf(np); Styles.ApplyLook(np, Styles.LookFor(Settings.BarStyle, lay), lay); }
            np.Zones = np.FullWidth;          // a new full-width bar starts with Three zones on
            AddBar(np);
        }

        /// <summary>Put a bar you made up (monitor, edge and modules set) on screen and in the settings.</summary>
        public void AddBar(BarPlacement np)
        {
            Settings.Bars.Add(np);
            CreateBar(np);
            UpdateGpuState();
            UpdateWinKeyHook();
            Settings.Save();
            RepositionAll();
            if (_settingsWin != null) _settingsWin.RefreshBars();
        }

        public void RemoveBar(BarPlacement p)
        {
            if (_bars.Count <= 1) return;
            BarWindow found = null;
            foreach (var b in _bars) if (b.Placement == p) found = b;
            if (found == null) return;
            _bars.Remove(found);
            BarsMoved();
            Settings.Bars.Remove(p);
            found.Close();
            UpdateGpuState();
            UpdateWinKeyHook();
            Settings.Save();
            RepositionAll();
            if (_settingsWin != null) _settingsWin.RefreshBars();
        }

        /// <summary>Open the settings window, optionally jumping to one bar.</summary>
        [PluginApi] public void OpenSettings(BarPlacement select)
        {
            if (_settingsWin == null)
            {
                _settingsWin = new SettingsWindow(this);
                _settingsWin.Closed += delegate { _settingsWin = null; Settings.Save(); };
                _settingsWin.Show();
            }
            if (select != null) _settingsWin.SelectBar(select);
            _settingsWin.Activate();
        }

        /// <summary>The bars shown now (a bar whose monitor is disconnected isn't listed), for a plugin window that sits against one.</summary>
        [PluginApi]
        public List<BarInfo> Bars()
        {
            var l = new List<BarInfo>();
            foreach (BarWindow b in _bars)
            {
                RECT r = b.BarRect;
                if (b.Displaced || r.Width <= 0 || b.Theme == null) continue;
                BarPlacement p = b.Placement;
                l.Add(new BarInfo
                {
                    Handle = b.Handle, Monitor = b.MonitorHandle, Edge = p.Edge == "Top" ? "Top" : "Bottom",
                    Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom,
                    CornerRadius = Math.Max(0, p.CornerRadius),
                    BorderThickness = p.ShowBorder ? Math.Max(0.5, Math.Min(12, p.BorderThickness)) : 0,
                    Outline = Theme.Faded(b.Theme.Resolve(p.BorderColor, b.Theme.MainAccent), p.BorderOpacity),
                    Theme = b.Theme, BehindFullScreen = b.Under
                });
            }
            return l;
        }

        /// <summary>A bar was added, removed, moved, resized, restyled or went behind / came out from behind a full-screen app (ask Bars() again).</summary>
        [PluginApi]
        public event Action BarsChanged;

        bool _barsQueued;

        /// <summary>Tell the plugins the bars changed - once for a burst of changes (a settings change places every bar).</summary>
        public void BarsMoved()
        {
            if (_barsQueued || _shuttingDown) return;
            _barsQueued = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                _barsQueued = false;
                Action h = BarsChanged;
                if (h == null) return;
                foreach (Action a in h.GetInvocationList())
                {
                    try { a(); }
                    catch (Exception ex) { Log.Write("BarsChanged: " + ex.Message); }
                }
            }));
        }

        public void OnBarPlaced(BarWindow bar, RECT rect)
        {
            BarsMoved();
            // Keep the (invisible) Shell_TrayWnd over the bar holding the tray (else the first bar), so apps that ask where the taskbar is
            // open their popups next to it.
            if (Tray == null) return;
            BarWindow host = null;
            foreach (var b in _bars) if (b.HasTray) { host = b; break; }
            if (host == null && _bars.Count > 0) host = _bars[0];
            if (host == bar) Tray.UpdateRect(rect, bar.Placement.Edge == "Top");
        }

        // =================================================================
        // Shutdown
        // =================================================================
        public void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            // a safety net: if closing gets stuck somewhere (a hung window, a driver call), end the process after 12 s anyway -
            // everything that matters (the settings) is saved first
            var net = new System.Threading.Thread(delegate () { System.Threading.Thread.Sleep(12000); Log.Write("Shutdown: still closing after 12 s - ending vShell"); Log.Flush(); Environment.Exit(0); });
            net.IsBackground = true;
            net.Start();
            var clock = Stopwatch.StartNew();
            long last = 0;
            Action<string> step = delegate (string what)
            {
                long now = clock.ElapsedMilliseconds;
                if (now - last > 1000) Log.Write("Shutdown: " + what + " took " + (now - last) + " ms");
                last = now;
            };
            try
            {
                foreach (var t in new[] { _tasksTimer, _statsTimer, _slowTimer, _taskbarTimer, _applyTimer, _repositionTimer })
                    if (t != null) t.Stop();

                Settings.Save();
                step("saving settings");
                if (_myTray != null) { _myTray.Dispose(); _myTray = null; }
                CloseOpenPopup();
                if (_winKey != null) { _winKey.Dispose(); _winKey = null; }
                foreach (var w in _widgets.ToArray()) { try { w.Close(); } catch { } }
                _widgets.Clear();
                Gpu.Stop();
                step("GPU");
                foreach (var b in _bars.ToArray()) b.Close();
                step("bars");
                _bars.Clear();
                if (_settingsWin != null) _settingsWin.Close();

                if (_parkTimer != null) _parkTimer.Stop();
                if (_saveTimer != null) _saveTimer.Stop();
                StopMinimizeHook();
                if (_fsTimer != null) _fsTimer.Stop();
                ShellServices.RestoreMinimizedBoxes();
                if (_balloon != null) { try { _balloon.Close(); } catch { } _balloon = null; }
                VTerm.QuitIfIdle();     // vTerm ends too, unless it's in use (a window, or shells running in its drop-down)
                if (_hook != null) { _hook.Dispose(); _hook = null; }
                step("shell hook");
                if (_tasksSoon != null) _tasksSoon.Stop();
                if (Tray != null) { Tray.Dispose(); Tray = null; }
                step("tray");
                ShellHook.ReturnTaskband(ShellServices.ExplorerTaskbarHandle());
                ShellServices.RestoreExplorerTaskbar();
                step("taskbar");
            }
            catch (Exception ex) { Log.Write("Shutdown: " + ex); }
            Application.Current.Shutdown();
        }
    }
}
