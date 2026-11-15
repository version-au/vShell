using System;
using System.Collections.Generic;

namespace vShell.Core
{
    public sealed class TaskWindow
    {
        public IntPtr Handle;
        public string Title = "";
        public bool Minimized;
        public bool Active;
        public uint ProcessId;
        public IntPtr Monitor;
        public IntPtr HIcon;       // borrowed from the window - never destroy
        /// <summary>
        /// The window set the "vShell.LiveIcon" property: its own icon changes while it runs (vXplorer draws it in the theme), so the
        /// taskbar uses that icon - its large one, read on every refresh - even when its buttons are combined or pinned.
        /// </summary>
        public bool LiveIcon;
        /// <summary>The property's value: the app counts it up each time it changes its icon (an icon handle's number can be reused).</summary>
        public long LiveIconVersion;
        public IntPtr BigIcon;     // LiveIcon windows only; borrowed, never destroy
        public string ExePath;     // only filled when no window icon could be found
        /// <summary>Store (UWP) app hosted in an ApplicationFrameWindow: its icon comes from the app package, not the window.</summary>
        public bool IsUwp;
        public string Aumid;
        /// <summary>The window asked for attention (FlashWindow).</summary>
        public bool Flashing;
        /// <summary>ITaskbarList progress: TBPF flags (0 none, 1 indeterminate, 2 normal, 4 error, 8 paused) and 0..65534.</summary>
        public int ProgressState, ProgressValue;
        /// <summary>ITaskbarList overlay badge (our own copy of the icon; zero = none) and its description.</summary>
        public IntPtr OverlayIcon;
        public string OverlayText;
        /// <summary>The program behind the window ("" when it can't be read, e.g. an elevated app).</summary>
        public string ProcessPath;
        /// <summary>An AppUserModelID the (non-Store) app set on its window, or "" - apps like Chrome use it to group their windows.</summary>
        public string WindowAumid;
        /// <summary>Which app the window belongs to, for grouping: "aumid:..." or "exe:..." (lower case), "hwnd:..." when unknown.</summary>
        public string AppKey = "";
    }

    /// <summary>Polls the top-level windows and keeps an ordered list of the ones that belong on a taskbar.</summary>
    public sealed class TaskList
    {
        readonly List<TaskWindow> _windows = new List<TaskWindow>();
        public IReadOnlyList<TaskWindow> Windows { get { return _windows; } }
        public readonly HashSet<IntPtr> Ignored = new HashSet<IntPtr>();
        public event Action Changed;

        string _lastSignature = "";
        int _tick;

        // per-window state that arrives through the shell hook / ITaskbarList, possibly before the window is in the list
        readonly HashSet<IntPtr> _flashing = new HashSet<IntPtr>();
        readonly Dictionary<IntPtr, int> _progState = new Dictionary<IntPtr, int>();
        readonly Dictionary<IntPtr, int> _progValue = new Dictionary<IntPtr, int>();
        readonly Dictionary<IntPtr, IntPtr> _overlay = new Dictionary<IntPtr, IntPtr>();
        readonly Dictionary<IntPtr, string> _overlayText = new Dictionary<IntPtr, string>();

        /// <summary>
        /// Registered "TaskbarButtonCreated" message. Apps only start using ITaskbarList (progress, badges) after receiving it.
        /// Sent to every window found at start-up (so apps re-send their current progress / badges to us); Explorer sends it for new
        /// windows itself.
        /// </summary>
        public uint ButtonCreatedMsg;
        bool _initialSent;

        public void SetFlashing(IntPtr h, bool on)
        {
            if (on) { if (h != Native.GetForegroundWindow()) _flashing.Add(h); }
            else _flashing.Remove(h);
        }
        public void SetProgressState(IntPtr h, int state) { if (state == 0) { _progState.Remove(h); _progValue.Remove(h); } else _progState[h] = state; }
        public void SetProgressValue(IntPtr h, int value) { _progValue[h] = Math.Max(0, Math.Min(65534, value)); }
        public void SetOverlay(IntPtr h, IntPtr hIcon)
        {
            IntPtr old;
            if (_overlay.TryGetValue(h, out old)) { Native.DestroyIcon(old); _overlay.Remove(h); }
            if (hIcon != IntPtr.Zero)
            {
                IntPtr copy = Native.CopyIcon(hIcon);       // the app may destroy its icon right after the call
                if (copy != IntPtr.Zero) _overlay[h] = copy;
            }
        }
        public void SetOverlayText(IntPtr h, string text) { if (string.IsNullOrEmpty(text)) _overlayText.Remove(h); else _overlayText[h] = text; }

        static readonly HashSet<string> SkipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"
        };

        public void Refresh()
        {
            _tick++;
            var found = new List<IntPtr>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (IsTaskWindow(h)) found.Add(h);
                return true;
            }, IntPtr.Zero);

            var foundSet = new HashSet<IntPtr>(found);
            _windows.RemoveAll(delegate (TaskWindow w) { return !foundSet.Contains(w.Handle); });

            var known = new HashSet<IntPtr>();
            foreach (var w in _windows) known.Add(w.Handle);
            foreach (var h in found)
                if (!known.Contains(h))
                {
                    _windows.Add(new TaskWindow { Handle = h });
                    if (ButtonCreatedMsg != 0 && !_initialSent)
                        Native.SendNotifyMessage(h, ButtonCreatedMsg, IntPtr.Zero, IntPtr.Zero);
                }
            _initialSent = true;
            PruneState(foundSet);

            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                IntPtr root = Native.GetAncestor(fg, Native.GA_ROOTOWNER);
                if (root != IntPtr.Zero) fg = root;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var w in _windows)
            {
                w.Title = Native.GetTitle(w.Handle);
                w.Minimized = Native.IsIconic(w.Handle);
                w.Active = w.Handle == fg;
                w.Monitor = MonitorHelper.HandleFromWindow(w.Handle);
                if (w.ProcessId == 0) { uint pid; Native.GetWindowThreadProcessId(w.Handle, out pid); w.ProcessId = pid; }

                if (w.Active) _flashing.Remove(w.Handle);
                w.Flashing = _flashing.Contains(w.Handle);
                int v;
                w.ProgressState = _progState.TryGetValue(w.Handle, out v) ? v : 0;
                w.ProgressValue = _progValue.TryGetValue(w.Handle, out v) ? v : 0;
                IntPtr ov;
                w.OverlayIcon = _overlay.TryGetValue(w.Handle, out ov) ? ov : IntPtr.Zero;
                string ot;
                w.OverlayText = _overlayText.TryGetValue(w.Handle, out ot) ? ot : null;

                if (w.Aumid == null || (w.IsUwp && w.Aumid.Length == 0 && _tick % 10 == 0))
                {
                    w.IsUwp = Native.GetClass(w.Handle) == "ApplicationFrameWindow";
                    w.Aumid = w.IsUwp ? (WindowProps.GetAumid(w.Handle) ?? "") : "";
                }

                if (w.ProcessPath == null) w.ProcessPath = Native.GetProcessPath(w.ProcessId) ?? "";
                if (w.WindowAumid == null)
                {
                    w.WindowAumid = w.IsUwp ? "" : (WindowProps.GetAumid(w.Handle) ?? "");
                    // a packaged desktop app (WhatsApp, new Teams ...): its Store id from the process, so its icon, launching and pins work
                    if (!w.IsUwp && w.WindowAumid.Length == 0) w.WindowAumid = WindowProps.GetProcessAumid(w.ProcessId);
                }
                w.AppKey = KeyFor(w);

                w.LiveIconVersion = Native.GetProp(w.Handle, "vShell.LiveIcon").ToInt64();
                w.LiveIcon = w.LiveIconVersion != 0;
                if (w.LiveIcon)
                {
                    IntPtr big;
                    Native.SendMessageTimeout(w.Handle, Native.WM_GETICON, new IntPtr(1), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 40, out big);   // ICON_BIG
                    w.BigIcon = big;
                }
                else w.BigIcon = IntPtr.Zero;

                if (w.HIcon == IntPtr.Zero || _tick % 20 == 0 || w.LiveIcon)
                {
                    IntPtr ic = FetchIcon(w.Handle);
                    if (ic != IntPtr.Zero) { w.HIcon = ic; w.ExePath = null; }
                    else if (w.HIcon == IntPtr.Zero && w.ExePath == null) w.ExePath = Native.GetProcessPath(w.ProcessId) ?? "";
                }

                sb.Append(w.Handle.ToInt64()).Append('|').Append(w.Title).Append('|')
                  .Append(w.Minimized ? 1 : 0).Append(w.Active ? 1 : 0).Append('|')
                  .Append(w.HIcon.ToInt64()).Append(',').Append(w.BigIcon.ToInt64()).Append(',').Append(w.LiveIconVersion).Append('|').Append(w.Monitor.ToInt64()).Append('|')
                  .Append(w.Flashing ? 1 : 0).Append(w.ProgressState).Append(',').Append(w.ProgressValue / 655).Append('|')
                  .Append(w.OverlayIcon.ToInt64()).Append(w.OverlayText).Append('|').Append(w.Aumid).Append('|').Append(w.AppKey).Append(';');
            }

            string sig = sb.ToString();
            if (sig != _lastSignature)
            {
                _lastSignature = sig;
                var h = Changed;
                if (h != null) h();
            }
        }

        /// <summary>The grouping key: the Store app's id, else the window's own AppUserModelID, else the program's path.</summary>
        public static string KeyFor(TaskWindow w)
        {
            if (w.IsUwp) return !string.IsNullOrEmpty(w.Aumid) ? "aumid:" + w.Aumid.ToLowerInvariant() : "hwnd:" + w.Handle.ToInt64();
            if (!string.IsNullOrEmpty(w.WindowAumid)) return "aumid:" + w.WindowAumid.ToLowerInvariant();
            if (!string.IsNullOrEmpty(w.ProcessPath)) return "exe:" + w.ProcessPath.ToLowerInvariant();
            return "hwnd:" + w.Handle.ToInt64();
        }

        /// <summary>Forget hook state of windows that are gone.</summary>
        void PruneState(HashSet<IntPtr> alive)
        {
            _flashing.RemoveWhere(delegate (IntPtr h) { return !alive.Contains(h) && !Native.IsWindow(h); });
            foreach (var d in new System.Collections.IDictionary[] { _progState, _progValue, _overlayText })
            {
                var dead = new List<IntPtr>();
                foreach (IntPtr h in d.Keys) if (!Native.IsWindow(h)) dead.Add(h);
                foreach (var h in dead) d.Remove(h);
            }
            var deadOv = new List<IntPtr>();
            foreach (var kv in _overlay) if (!Native.IsWindow(kv.Key)) deadOv.Add(kv.Key);
            foreach (var h in deadOv) SetOverlay(h, IntPtr.Zero);
        }

        bool IsTaskWindow(IntPtr h)
        {
            if (Ignored.Contains(h)) return false;
            if (!Native.IsWindowVisible(h)) return false;

            long ex = Native.GetWindowLongAuto(h, Native.GWL_EXSTYLE);
            bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
            if ((ex & Native.WS_EX_NOACTIVATE) != 0 && !appWindow) return false;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero && !appWindow) return false;

            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return false;
            if (Native.GetProp(h, "ITaskList_Deleted") != IntPtr.Zero) return false;     // ITaskbarList::DeleteTab
            if (IsImmersiveShellWindow(h, ex)) return false;

            if (Native.GetWindowTextLength(h) == 0) return false;
            if (SkipClasses.Contains(Native.GetClass(h))) return false;
            return true;
        }

        /// <summary>Store-app frames and shell surfaces without a window edge are part of the shell UI, not apps (ManagedShell's rule).</summary>
        public static bool IsImmersiveShellWindow(IntPtr h, long exStyle)
        {
            string cls = Native.GetClass(h);
            if (cls == "ApplicationFrameWindow" || cls == "Windows.UI.Core.CoreWindow" || cls == "StartMenuSizingFrame" || cls == "Shell_LightDismissOverlay")
                return (exStyle & 0x100 /*WS_EX_WINDOWEDGE*/) == 0;
            return false;
        }

        static IntPtr FetchIcon(IntPtr h)
        {
            IntPtr r;
            // ICON_SMALL2 = 2, ICON_SMALL = 0, ICON_BIG = 1
            foreach (int kind in new[] { 2, 0, 1 })
            {
                r = IntPtr.Zero;
                Native.SendMessageTimeout(h, Native.WM_GETICON, new IntPtr(kind), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 40, out r);
                if (r != IntPtr.Zero) return r;
            }
            IntPtr c = Native.GetClassLongAuto(h, -34);   // GCL_HICONSM
            if (c != IntPtr.Zero) return c;
            return Native.GetClassLongAuto(h, -14);       // GCL_HICON
        }

        // ---- actions -------------------------------------------------------
        /// <summary>A taskbar click: the window in front minimises, any other comes to the front.</summary>
        public static void Activate(TaskWindow w)
        {
            if (w.Active && !w.Minimized) { Minimize(w.Handle); return; }
            BringToFront(w.Handle);
        }

        /// <summary>
        /// Bring a window to the front (restoring it if minimised) - or the dialog it has open (its last active popup), so a "Save as"
        /// or "Save changes?" isn't left behind it.
        /// </summary>
        public static void BringToFront(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            IntPtr target = Front(h);
            Native.SetForegroundWindow(target);
            if (Native.GetForegroundWindow() != target) Native.SwitchToThisWindow(target, true);
        }

        /// <summary>The window to give the focus to: its open dialog, if it has a visible one.</summary>
        static IntPtr Front(IntPtr h)
        {
            IntPtr p = Native.GetLastActivePopup(h);
            return p != IntPtr.Zero && Native.IsWindowVisible(p) ? p : h;
        }

        /// <summary>
        /// Minimise the way the window's own button does (SC_MINIMIZE - apps that go to the tray instead still do). A window that is
        /// disabled behind a dialog isn't minimised (it couldn't be restored properly): its dialog comes to the front instead.
        /// </summary>
        public static void Minimize(IntPtr h)
        {
            if (!Native.IsWindowEnabled(h)) { BringToFront(h); return; }
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            Native.AllowSetForegroundWindow(pid);
            SysCommand(h, Native.SC_MINIMIZE);
        }

        public static void SysCommand(IntPtr h, uint cmd) { Native.PostMessage(h, Native.WM_SYSCOMMAND, new IntPtr((long)cmd), IntPtr.Zero); }

        /// <summary>Close the way the window's X does (SC_CLOSE), with it in front first, so a "Save changes?" it asks shows.</summary>
        public static void Close(IntPtr h)
        {
            Native.SetForegroundWindow(Front(h));
            SysCommand(h, Native.SC_CLOSE);
        }
    }
}
