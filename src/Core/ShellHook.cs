using System;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    /// <summary>
    /// The window a taskbar uses to hear about other windows, as ManagedShell's TasksService does:
    ///  - shell hook messages (window created / destroyed / activated / title changed / flashing / moved to another monitor /
    ///    entered or left full screen), so the task list updates the moment something happens instead of on the next poll;
    ///  - cloak / uncloak events (virtual desktops, suspended Store apps);
    ///  - ITaskbarList messages (progress bars, overlay badges, "this window is full screen"). Apps find this window through the
    ///    "TaskbandHWND" property of the Shell_TrayWnd they see first, which is vShell's own (see TrayHost).
    /// Must be created on the UI thread.
    /// </summary>
    public sealed class ShellHook : IDisposable
    {
        const int HSHELL_WINDOWCREATED = 1, HSHELL_WINDOWDESTROYED = 2, HSHELL_WINDOWACTIVATED = 4, HSHELL_REDRAW = 6,
                  HSHELL_ENDTASK = 10, HSHELL_WINDOWREPLACED = 13, HSHELL_WINDOWREPLACING = 14, HSHELL_MONITORCHANGED = 16,
                  HSHELL_FULLSCREENENTER = 53, HSHELL_FULLSCREENEXIT = 54,
                  HSHELL_FLASH = 0x8006, HSHELL_RUDEAPPACTIVATED = 0x8004;
        const uint WM_USER = 0x0400;

        /// <summary>Something about the set of windows changed (created, destroyed, title, monitor, cloaked...): refresh the task list.</summary>
        public event Action Changed;
        /// <summary>A window was activated (hwnd may be zero = the desktop).</summary>
        public event Action<IntPtr> Activated;
        /// <summary>A window asks for attention (true) or stopped (false).</summary>
        public event Action<IntPtr, bool> Flash;
        /// <summary>A window entered (true) or left (false) full screen, as told by Windows or by the app itself.</summary>
        public event Action<IntPtr, bool> FullScreen;
        /// <summary>ITaskbarList::SetProgressState (TBPF_* flags).</summary>
        public event Action<IntPtr, int> ProgressState;
        /// <summary>ITaskbarList::SetProgressValue, scaled 0..65534 by Windows.</summary>
        public event Action<IntPtr, int> ProgressValue;
        /// <summary>ITaskbarList::SetOverlayIcon (icon handle owned by the app; zero = remove).</summary>
        public event Action<IntPtr, IntPtr> Overlay;
        public event Action<IntPtr, string> OverlayText;
        /// <summary>Explorer (re)started and broadcast TaskbarCreated.</summary>
        public event Action TaskbarCreated;

        IntPtr _hwnd, _cloakHook;
        WndProcDelegate _proc;
        WinEventProc _cloakProc;
        bool _registered, _classRegistered;
        uint _shellHookMsg, _taskbarCreatedMsg;
        public uint TaskbarButtonCreatedMsg { get; private set; }
        public IntPtr Handle { get { return _hwnd; } }

        const string ClassName = "vShellTaskHookWnd";

        public bool Start()
        {
            if (_hwnd != IntPtr.Zero) return true;
            IntPtr hInst = Native.GetModuleHandle(null);
            _proc = WndProc;
            var wc = new WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.lpfnWndProc = _proc;
            wc.hInstance = hInst;
            wc.lpszClassName = ClassName;
            if (Native.RegisterClassEx(ref wc) == 0) { Log.Write("ShellHook: RegisterClassEx failed"); return false; }
            _classRegistered = true;

            // an ordinary (invisible) top-level window: message-only windows do not receive shell hook messages
            _hwnd = Native.CreateWindowEx((uint)Native.WS_EX_TOOLWINDOW, ClassName, "", Native.WS_POPUP, 0, 0, 0, 0,
                                          IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) { Log.Write("ShellHook: CreateWindowEx failed"); return false; }

            _shellHookMsg = Native.RegisterWindowMessage("SHELLHOOK");
            _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            TaskbarButtonCreatedMsg = Native.RegisterWindowMessage("TaskbarButtonCreated");

            _registered = Native.RegisterShellHookWindow(_hwnd);
            if (!_registered) Log.Write("ShellHook: RegisterShellHookWindow failed");

            _cloakProc = OnCloak;
            _cloakHook = Native.SetWinEventHook(Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _cloakProc, 0, 0,
                                                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            return true;
        }

        void OnCloak(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd != IntPtr.Zero && idObject == 0 && idChild == 0) Raise(Changed);
        }

        static void Raise(Action a) { if (a != null) a(); }

        IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                if (msg == _shellHookMsg && _shellHookMsg != 0)
                {
                    int code = w.ToInt32();
                    switch (code)
                    {
                        case HSHELL_WINDOWACTIVATED:
                        case HSHELL_RUDEAPPACTIVATED:
                            { var a = Activated; if (a != null) a(l); }
                            Raise(Changed);
                            break;
                        case HSHELL_FLASH:
                            { var f = Flash; if (f != null) f(l, true); }
                            Raise(Changed);
                            break;
                        case HSHELL_REDRAW:
                            // title / icon changed; also ends a flash (ManagedShell does the same)
                            { var f = Flash; if (f != null) f(l, false); }
                            Raise(Changed);
                            break;
                        case HSHELL_FULLSCREENENTER:
                        case HSHELL_FULLSCREENEXIT:
                            { var fs = FullScreen; if (fs != null) fs(l, code == HSHELL_FULLSCREENENTER); }
                            break;
                        case HSHELL_WINDOWCREATED:
                        case HSHELL_WINDOWDESTROYED:
                        case HSHELL_ENDTASK:
                        case HSHELL_WINDOWREPLACED:
                        case HSHELL_WINDOWREPLACING:
                        case HSHELL_MONITORCHANGED:
                            Raise(Changed);
                            break;
                    }
                    return IntPtr.Zero;
                }

                if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
                {
                    Raise(TaskbarCreated);
                    return IntPtr.Zero;
                }

                // ITaskbarList / ITaskbarList3 calls, as sent by shell32 inside the app to the TaskbandHWND window
                if (msg >= WM_USER && msg < WM_USER + 100)
                {
                    switch (msg - WM_USER)
                    {
                        case 60:   // MarkFullscreenWindow: wParam = entering, lParam = hwnd
                            { var fs = FullScreen; if (fs != null) fs(l, w != IntPtr.Zero); }
                            return IntPtr.Zero;
                        case 64:   // SetProgressValue: wParam = hwnd, lParam = 0..0xFFFE
                            { var pv = ProgressValue; if (pv != null) pv(w, (int)l.ToInt64()); }
                            return IntPtr.Zero;
                        case 65:   // SetProgressState: wParam = hwnd, lParam = TBPF flags
                            { var ps = ProgressState; if (ps != null) ps(w, (int)l.ToInt64()); }
                            return IntPtr.Zero;
                        case 79:   // SetOverlayIcon: wParam = hwnd, lParam = HICON
                            { var o = Overlay; if (o != null) o(w, l); }
                            return IntPtr.Zero;
                        case 85:   // SetOverlayIcon description: lParam = shared-memory string owned by the window's process
                            { var ot = OverlayText; if (ot != null) ot(w, ReadSharedString(w, l)); }
                            return IntPtr.Zero;
                        case 50: case 67: case 68: case 71: case 72: case 75: case 76: case 77: case 78: case 80: case 81: case 87:
                            return IntPtr.Zero;   // ActivateTab, tabs, thumbnail buttons/clip/tooltip: accepted, not shown
                    }
                }
            }
            catch (Exception ex) { Log.Write("ShellHook: " + ex.Message); }
            return Native.DefWindowProc(h, msg, w, l);
        }

        static string ReadSharedString(IntPtr hwnd, IntPtr hShared)
        {
            if (hShared == IntPtr.Zero) return null;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            IntPtr p = Native.SHLockShared(hShared, pid);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Native.SHUnlockShared(p); }
        }

        // ---- where ITaskbarList calls go ------------------------------------------------------------------
        /// <summary>Make apps' ITaskbarList calls come to us: tag our Shell_TrayWnd and untag Explorer's.</summary>
        public void ClaimTaskband(IntPtr ourTray, IntPtr explorerTray)
        {
            if (_hwnd == IntPtr.Zero || ourTray == IntPtr.Zero) return;
            Native.SetProp(ourTray, "TaskbandHWND", _hwnd);
            if (explorerTray != IntPtr.Zero) Native.RemoveProp(explorerTray, "TaskbandHWND");
        }

        /// <summary>On exit: give Explorer's taskbar its own task band back so progress bars keep working there.</summary>
        public static void ReturnTaskband(IntPtr explorerTray)
        {
            if (explorerTray == IntPtr.Zero) return;
            IntPtr band = IntPtr.Zero;
            Native.EnumChildWindows(explorerTray, delegate (IntPtr c, IntPtr lp)
            {
                if (Native.GetClass(c) == "MSTaskSwWClass") { band = c; return false; }
                return true;
            }, IntPtr.Zero);
            if (band != IntPtr.Zero) Native.SetProp(explorerTray, "TaskbandHWND", band);
        }

        public void Dispose()
        {
            if (_cloakHook != IntPtr.Zero) { Native.UnhookWinEvent(_cloakHook); _cloakHook = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero)
            {
                if (_registered) Native.DeregisterShellHookWindow(_hwnd);
                Native.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_classRegistered)
            {
                Native.UnregisterClass(ClassName, Native.GetModuleHandle(null));
                _classRegistered = false;
            }
        }
    }
}
