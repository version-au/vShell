using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    public sealed class TrayIcon
    {
        public IntPtr HWnd;
        public uint UID;
        public uint CallbackMessage;
        public IntPtr HIcon;
        public string Tip = "";
        public bool Hidden;
        public uint Version;
        public uint State;
        /// <summary>Identity some apps use instead of hWnd + uID (NIF_GUID).</summary>
        public Guid Guid;
        /// <summary>Stable identity across runs: owner exe path (or window class) + icon id. Used for pinning.</summary>
        public string Key = "";
    }

    /// <summary>A balloon notification from a tray icon (NOTIFYICONDATA szInfo / szInfoTitle / dwInfoFlags / hBalloonIcon).</summary>
    public sealed class TrayBalloon
    {
        public string Title = "", Text = "";
        /// <summary>NIIF_*: low 4 bits = 0 none, 1 info, 2 warning, 3 error, 4 user icon; 0x10 no sound; 0x20 large icon; 0x80 respect quiet time.</summary>
        public uint Flags;
        public IntPtr Icon;
    }

    /// <summary>
    /// Hosts the Windows notification area when vShell is the shell (no Explorer running).
    /// It registers a "Shell_TrayWnd" window; Shell_NotifyIcon() in every application sends its requests
    /// to that window as WM_COPYDATA, which is what we parse here.
    /// </summary>
    public sealed class TrayHost : IDisposable
    {
        const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETFOCUS = 3, NIM_SETVERSION = 4;
        const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_INFO = 0x10, NIF_GUID = 0x20;

        /// <summary>An app sent a balloon notification (NIF_INFO). Text empty = take the current one down.</summary>
        public event Action<TrayIcon, TrayBalloon> Balloon;

        // Windows' own system icons
        public static readonly Guid VolumeGuid = new Guid("7820ae73-23e3-4229-82c1-e41cb67d5b9c");
        public static readonly Guid NetworkGuid = new Guid("7820ae74-23e3-4229-82c1-e41cb67d5b9c");
        public static readonly Guid PowerGuid = new Guid("7820ae75-23e3-4229-82c1-e41cb67d5b9c");
        /// <summary>The key of vShell's own network icon in the Tray module (for pinning / ordering).</summary>
        public const string OwnNetworkKey = "vshell:network";
        const uint NIS_HIDDEN = 0x1;

        readonly List<TrayIcon> _icons = new List<TrayIcon>();
        public IReadOnlyList<TrayIcon> Icons { get { return _icons; } }
        public event Action Changed;

        IntPtr _hwnd, _notifyWnd;
        WndProcDelegate _proc, _childProc;   // keep delegates alive
        bool _classesRegistered;

        public bool IsRunning { get { return _hwnd != IntPtr.Zero; } }

        public IntPtr Handle { get { return _hwnd; } }
        System.Windows.Threading.DispatcherTimer _raise;

        /// <summary>Raised when Explorer restarts (it broadcasts TaskbarCreated itself): app bars must re-register, the taskbar must be re-hidden.</summary>
        public event Action ExplorerRestarted;

        /// <summary>Screen rectangle (device px) of an icon as drawn on a bar, for apps that place their popups next to it.</summary>
        public delegate bool IconRectProvider(TrayIcon ic, out RECT r);
        public IconRectProvider IconRect;

        RECT _hostRect;
        uint _hostEdge = Native.ABE_BOTTOM;
        bool _haveHost;
        uint _taskbarCreatedMsg;
        static DateTime _lastBroadcast = DateTime.MinValue;

        /// <summary>
        /// Sit in front of Explorer's tray window (the way Cairo does): apps looking for Shell_TrayWnd find ours first, so we take their
        /// icons, and pass everything else on to Explorer.
        /// </summary>
        /// <param name="demoteExplorer">Also push Explorer's (hidden) taskbar to the bottom of the z-order so ours is found first.</param>
        public bool Start(bool demoteExplorer)
        {
            if (_hwnd != IntPtr.Zero) return true;

            IntPtr hInst = Native.GetModuleHandle(null);
            _proc = WndProc;
            _childProc = ChildProc;

            var wc = new WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.style = 0x8;  // CS_DBLCLKS
            wc.lpfnWndProc = _proc;
            wc.hInstance = hInst;
            wc.lpszClassName = "Shell_TrayWnd";
            if (Native.RegisterClassEx(ref wc) == 0) return false;

            var wc2 = new WNDCLASSEX();
            wc2.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc2.lpfnWndProc = _childProc;
            wc2.hInstance = hInst;
            wc2.lpszClassName = "TrayNotifyWnd";
            Native.RegisterClassEx(ref wc2);
            _classesRegistered = true;

            _hwnd = Native.CreateWindowEx((uint)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST), "Shell_TrayWnd", "",
                                          Native.WS_POPUP, 0, 0, 200, 40, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) return false;

            _notifyWnd = Native.CreateWindowEx(0, "TrayNotifyWnd", "", Native.WS_CHILD, 0, 0, 200, 40, _hwnd, IntPtr.Zero, hInst, IntPtr.Zero);

            _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            ShellServices.OwnTray = _hwnd;
            if (demoteExplorer)
            {
                IntPtr real = RealTray();
                if (real != IntPtr.Zero)
                    Native.SetWindowPos(real, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            RaiseOverExplorer();
            // Explorer re-raises its own taskbar now and then; FindWindow("Shell_TrayWnd") returns the first one in z-order, so stay on top of it.
            _raise = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _raise.Tick += delegate { RaiseOverExplorer(); };
            _raise.Start();
            return true;
        }

        void RaiseOverExplorer()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (Native.FindWindow("Shell_TrayWnd", null) == _hwnd) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>Explorer's real taskbar window (not ours), or zero.</summary>
        IntPtr RealTray()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
                if (h != _hwnd) return h;
            return IntPtr.Zero;
        }

        /// <summary>Hand a message we do not deal with (app bars, taskbar queries, ...) to Explorer's taskbar.</summary>
        IntPtr Forward(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr real = RealTray();
            if (real == IntPtr.Zero) return Native.DefWindowProc(h, msg, w, l);
            if (msg == 0x0400 + 372) { Native.PostMessage(real, msg, w, l); return IntPtr.Zero; }
            IntPtr result;
            Native.SendMessageTimeout(real, msg, w, l, 2u /*SMTO_ABORTIFHUNG*/, 1500, out result);
            return result;
        }

        /// <summary>Keep the (invisible) Shell_TrayWnd rectangle on the bar so apps position their popups near it.</summary>
        bool _rectPlaced;       // the windows have been put on _hostRect (asked again with the same rectangle = nothing to do)

        public void UpdateRect(RECT r, bool topEdge)
        {
            bool same = _rectPlaced && _haveHost && _hostEdge == (topEdge ? Native.ABE_TOP : Native.ABE_BOTTOM)
                        && _hostRect.Left == r.Left && _hostRect.Top == r.Top && _hostRect.Right == r.Right && _hostRect.Bottom == r.Bottom;
            _hostRect = r;
            _hostEdge = topEdge ? Native.ABE_TOP : Native.ABE_BOTTOM;
            _haveHost = true;
            if (_hwnd == IntPtr.Zero || same) return;
            Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            if (_notifyWnd != IntPtr.Zero)
                Native.SetWindowPos(_notifyWnd, IntPtr.Zero, 0, 0, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            _rectPlaced = true;
        }

        /// <summary>Tell running applications to (re-)add their icons.</summary>
        public static void BroadcastTaskbarCreated()
        {
            uint m = Native.RegisterWindowMessage("TaskbarCreated");
            _lastBroadcast = DateTime.UtcNow;
            Native.PostMessage(Native.HWND_BROADCAST, m, IntPtr.Zero, IntPtr.Zero);
        }

        IntPtr ChildProc(IntPtr h, uint msg, IntPtr w, IntPtr l) { return Native.DefWindowProc(h, msg, w, l); }

        IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
            {
                // someone else (a restarted Explorer) announced a new taskbar: get back in front of it and tell the bars
                if ((DateTime.UtcNow - _lastBroadcast).TotalSeconds > 3)
                {
                    Log.Write("TrayHost: Explorer restarted");
                    RaiseOverExplorer();
                    var ev = ExplorerRestarted;
                    if (ev != null) ev();
                }
                return Native.DefWindowProc(h, msg, w, l);
            }

            if (msg == 0x0047 /*WM_WINDOWPOSCHANGED*/ && l != IntPtr.Zero)
            {
                // something called ShowWindow on "the taskbar" and hit ours: keep it invisible (it sits over the bar and would eat clicks)
                uint flags = (uint)Marshal.ReadInt32(l, IntPtr.Size * 2 + 16);
                if ((flags & Native.SWP_SHOWWINDOW) != 0)
                {
                    long style = Native.GetWindowLongAuto(_hwnd, Native.GWL_STYLE);
                    Native.SetWindowLongAuto(_hwnd, Native.GWL_STYLE, style & ~(long)Native.WS_VISIBLE);
                }
            }

            if (msg == Native.WM_COPYDATA && l != IntPtr.Zero)
            {
                try
                {
                    var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(l, typeof(COPYDATASTRUCT));
                    long kind = cds.dwData.ToInt64();
                    if (kind == 1) return HandleCopyData(l);                 // Shell_NotifyIcon
                    if (kind == 3) return HandleIconRect(cds);              // Shell_NotifyIconGetRect: answered here, Explorer does not know our icons
                    if (kind == 0)
                    {
                        bool handled;
                        IntPtr r = HandleAppBar(cds, w, out handled);       // SHAppBarMessage
                        if (handled) return r;
                    }
                    return Forward(h, msg, w, l);
                }
                catch (Exception ex) { Log.Write("TrayHost: " + ex.Message); return IntPtr.Zero; }
            }
            if ((msg >= 0x0400 || msg == 0x0111 /*WM_COMMAND*/ || msg == 0x001C /*WM_ACTIVATEAPP*/))
                return Forward(h, msg, w, l);
            return Native.DefWindowProc(h, msg, w, l);
        }

        // ---- app bar messages ---------------------------------------------------------------------
        // APPBARMSGDATAV3 (64 bytes, same for 32/64-bit senders):
        //   APPBARDATAV2 abd { int cbSize; uint hWnd; uint uCallback; uint uEdge; RECT rc; long lParam }   (40 bytes)
        //   int dwMessage @40; int pad @44; long hSharedMemory @48; int dwSourceProcessId @56; int pad @60
        const int AMD_SIZE = 64, ABD_SIZE = 40;
        const int ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_GETTASKBARPOS = 5, ABM_ACTIVATE = 6,
                  ABM_GETAUTOHIDEBAR = 7, ABM_WINDOWPOSCHANGED = 9, ABM_GETAUTOHIDEBAREX = 11;

        IntPtr HandleAppBar(COPYDATASTRUCT cds, IntPtr wParam, out bool handled)
        {
            handled = false;
            if (cds.cbData != AMD_SIZE || cds.lpData == IntPtr.Zero) return IntPtr.Zero;
            IntPtr d = cds.lpData;
            uint abHwnd = (uint)Marshal.ReadInt32(d, 4);
            int message = Marshal.ReadInt32(d, 40);
            IntPtr hShared = new IntPtr(Marshal.ReadInt64(d, 48));
            uint srcPid = (uint)Marshal.ReadInt32(d, 56);

            switch (message)
            {
                case ABM_GETTASKBARPOS:
                    {
                        // "where is the taskbar?" - the answer is the bar holding the tray, so popups open next to it
                        if (!_haveHost) return IntPtr.Zero;
                        IntPtr p = Native.SHLockShared(hShared, srcPid);
                        if (p == IntPtr.Zero) return IntPtr.Zero;
                        Marshal.WriteInt32(p, 12, (int)_hostEdge);
                        Marshal.WriteInt32(p, 16, _hostRect.Left);
                        Marshal.WriteInt32(p, 20, _hostRect.Top);
                        Marshal.WriteInt32(p, 24, _hostRect.Right);
                        Marshal.WriteInt32(p, 28, _hostRect.Bottom);
                        Native.SHUnlockShared(p);
                        handled = true;
                        return new IntPtr(1);
                    }
                case ABM_GETSTATE:
                    // our own HideExplorerTaskbar asks about Explorer's taskbar: let Explorer answer that one.
                    // Everyone else is told "not auto-hide", so maximised browsers do not leave a gap for a taskbar that never appears.
                    if (abHwnd == (uint)RealTray().ToInt64()) return IntPtr.Zero;
                    handled = true;
                    return IntPtr.Zero;   // ABS default
                case ABM_GETAUTOHIDEBAR:
                case ABM_GETAUTOHIDEBAREX:
                    handled = true;       // vShell has no auto-hide bars
                    return IntPtr.Zero;
                case ABM_ACTIVATE:
                case ABM_WINDOWPOSCHANGED:
                    handled = true;
                    return new IntPtr(1);
                case ABM_QUERYPOS:
                case ABM_SETPOS:
                    return ForwardAppBarPos(d, wParam, hShared, srcPid, out handled);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// QUERYPOS / SETPOS carry their rectangle in shared memory that belongs to the sender, and Explorer cannot use it when the
        /// message reaches it through us. Copy it into shared memory made for Explorer, forward, then copy Explorer's answer back
        /// (the same thing Cairo / ManagedShell does). This is what keeps maximised windows off vShell's own bars.
        /// </summary>
        IntPtr ForwardAppBarPos(IntPtr amd, IntPtr wParam, IntPtr hShared, uint srcPid, out bool handled)
        {
            handled = false;
            IntPtr real = RealTray();
            if (real == IntPtr.Zero) return IntPtr.Zero;
            uint explorerPid;
            Native.GetWindowThreadProcessId(real, out explorerPid);

            IntPtr oldView = Native.SHLockShared(hShared, srcPid);
            if (oldView == IntPtr.Zero) return IntPtr.Zero;
            IntPtr hNew = Native.SHAllocShared(IntPtr.Zero, ABD_SIZE, explorerPid);
            IntPtr newView = hNew == IntPtr.Zero ? IntPtr.Zero : Native.SHLockShared(hNew, explorerPid);
            if (newView == IntPtr.Zero)
            {
                if (hNew != IntPtr.Zero) Native.SHFreeShared(hNew, explorerPid);
                Native.SHUnlockShared(oldView);
                return IntPtr.Zero;
            }
            byte[] buf = new byte[ABD_SIZE];
            Marshal.Copy(oldView, buf, 0, ABD_SIZE);
            Marshal.Copy(buf, 0, newView, ABD_SIZE);
            Native.SHUnlockShared(newView);

            IntPtr amdCopy = Marshal.AllocHGlobal(AMD_SIZE);
            IntPtr cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(COPYDATASTRUCT)));
            IntPtr result = IntPtr.Zero;
            try
            {
                byte[] a = new byte[AMD_SIZE];
                Marshal.Copy(amd, a, 0, AMD_SIZE);
                Marshal.Copy(a, 0, amdCopy, AMD_SIZE);
                Marshal.WriteInt64(amdCopy, 48, hNew.ToInt64());
                Marshal.WriteInt32(amdCopy, 56, (int)explorerPid);
                var cds = new COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = AMD_SIZE, lpData = amdCopy };
                Marshal.StructureToPtr(cds, cdsPtr, false);
                Native.SendMessageTimeout(real, Native.WM_COPYDATA, wParam, cdsPtr, 2u /*SMTO_ABORTIFHUNG*/, 1500, out result);
                handled = true;

                // Explorer may have adjusted the rectangle: hand its answer back to the sender
                IntPtr back = Native.SHLockShared(hNew, explorerPid);
                if (back != IntPtr.Zero)
                {
                    Marshal.Copy(back, buf, 0, ABD_SIZE);
                    Native.SHUnlockShared(back);
                    Marshal.Copy(buf, 0, oldView, ABD_SIZE);
                }
            }
            finally
            {
                Native.SHUnlockShared(oldView);
                Native.SHFreeShared(hNew, explorerPid);
                Marshal.FreeHGlobal(amdCopy);
                Marshal.FreeHGlobal(cdsPtr);
            }
            return result;
        }

        // ---- Shell_NotifyIconGetRect -----------------------------------------------------------------
        // WINNOTIFYICONIDENTIFIER { int dwMagic; int dwMessage (1 = top-left, 2 = bottom-right); int cbSize; int pad; uint hWnd; uint uID; GUID guidItem }
        IntPtr HandleIconRect(COPYDATASTRUCT cds)
        {
            if (cds.lpData == IntPtr.Zero || cds.cbData < 40) return IntPtr.Zero;
            IntPtr d = cds.lpData;
            int which = Marshal.ReadInt32(d, 4);
            IntPtr hwnd = new IntPtr(Marshal.ReadInt32(d, 16));
            uint uid = (uint)Marshal.ReadInt32(d, 20);
            byte[] g = new byte[16];
            Marshal.Copy(new IntPtr(d.ToInt64() + 24), g, 0, 16);
            TrayIcon ic = Find(hwnd, uid, new Guid(g));
            if (ic == null) return IntPtr.Zero;

            RECT r = new RECT();
            bool have = IconRect != null && IconRect(ic, out r);
            if (!have) { if (!_haveHost) return IntPtr.Zero; r = _hostRect; }
            return which == 1 ? MakeLParam(r.Left, r.Top) : MakeLParam(r.Right, r.Bottom);
        }

        static IntPtr MakeLParam(int lo, int hi) { return new IntPtr(unchecked((int)(((uint)hi << 16) | ((uint)lo & 0xFFFF)))); }

        // SHELLTRAYDATA layout (as sent by shell32): { DWORD unknown; DWORD message; NOTIFYICONDATA (32-bit handle form) }
        IntPtr HandleCopyData(IntPtr lParam)
        {
            var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
            if (cds.dwData.ToInt64() != 1 || cds.lpData == IntPtr.Zero || cds.cbData < 8 + 24) return IntPtr.Zero;

            IntPtr d = cds.lpData;
            int cb = cds.cbData;
            const int N = 8;   // start of NOTIFYICONDATA

            uint message = (uint)RI(d, cb, 4);
            IntPtr hwnd = new IntPtr(RI(d, cb, N + 4));    // 32-bit handle, sign-extended
            uint uid = (uint)RI(d, cb, N + 8);
            uint flags = (uint)RI(d, cb, N + 12);
            uint callback = (uint)RI(d, cb, N + 16);
            IntPtr hIcon = new IntPtr(RI(d, cb, N + 20));
            string tip = RS(d, cb, N + 24, 128);
            uint state = (uint)RI(d, cb, N + 280);
            uint stateMask = (uint)RI(d, cb, N + 284);
            uint version = (uint)RI(d, cb, N + 800);
            Guid guid = Guid.Empty;
            if ((flags & NIF_GUID) != 0 && cb >= N + 952)
            {
                byte[] g = new byte[16];
                Marshal.Copy(new IntPtr(d.ToInt64() + N + 936), g, 0, 16);
                guid = new Guid(g);
            }

            TrayIcon ic = Find(hwnd, uid, guid);
            TrayBalloon balloon = null;
            switch (message)
            {
                case NIM_ADD:
                case NIM_MODIFY:
                    if ((flags & NIF_INFO) != 0)
                    {
                        balloon = new TrayBalloon
                        {
                            Text = RS(d, cb, N + 288, 256),
                            Title = RS(d, cb, N + 804, 64),
                            Flags = (uint)RI(d, cb, N + 932),
                            Icon = new IntPtr(RI(d, cb, N + 952))
                        };
                    }
                    if (ic == null)
                    {
                        if (hwnd == IntPtr.Zero) return IntPtr.Zero;      // a new icon needs a window to talk to
                        ic = new TrayIcon { HWnd = hwnd, UID = uid }; ic.Key = MakeKey(hwnd, uid); _icons.Add(ic);
                    }
                    else if (hwnd != IntPtr.Zero) { ic.HWnd = hwnd; ic.UID = uid; }
                    if (guid != Guid.Empty) ic.Guid = guid;
                    if ((flags & NIF_MESSAGE) != 0) ic.CallbackMessage = callback;
                    if ((flags & NIF_ICON) != 0) ic.HIcon = hIcon;
                    if ((flags & NIF_TIP) != 0) ic.Tip = tip;
                    if ((flags & NIF_STATE) != 0)
                    {
                        ic.State = (ic.State & ~stateMask) | (state & stateMask);
                        ic.Hidden = (ic.State & NIS_HIDDEN) != 0;
                    }
                    break;
                case NIM_DELETE:
                    if (ic != null)
                    {
                        _icons.Remove(ic);
                        var bd = Balloon;
                        if (bd != null) bd(ic, new TrayBalloon { Text = "" });    // its balloon goes with it
                    }
                    break;
                case NIM_SETVERSION:
                    if (ic != null) ic.Version = version;
                    break;
                case NIM_SETFOCUS:
                default:
                    return new IntPtr(1);
            }
            RaiseChanged();
            if (balloon != null && ic != null)
            {
                var b = Balloon;
                if (b != null) b(ic, balloon);
            }
            return new IntPtr(1);   // TRUE
        }

        static string MakeKey(IntPtr hwnd, uint uid)
        {
            string id = null;
            try
            {
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                id = Native.GetProcessPath(pid);
                if (string.IsNullOrEmpty(id)) id = "class:" + Native.GetClass(hwnd);
            }
            catch { id = "unknown"; }
            return id.ToLowerInvariant() + "#" + uid;
        }

        static int RI(IntPtr d, int cb, int off) { return off + 4 <= cb ? Marshal.ReadInt32(d, off) : 0; }
        static string RS(IntPtr d, int cb, int off, int maxChars)
        {
            int avail = Math.Max(0, (cb - off) / 2);
            int n = Math.Min(maxChars, avail);
            if (n <= 0) return "";
            string s = Marshal.PtrToStringUni(new IntPtr(d.ToInt64() + off), n);
            int z = s.IndexOf('\0');
            return z >= 0 ? s.Substring(0, z) : s;
        }

        TrayIcon Find(IntPtr hwnd, uint uid, Guid guid)
        {
            if (guid != Guid.Empty)
                foreach (var i in _icons) if (i.Guid == guid) return i;
            foreach (var i in _icons) if (i.HWnd == hwnd && i.UID == uid) return i;
            return null;
        }

        void RaiseChanged() { var h = Changed; if (h != null) h(); }

        /// <summary>Drop icons whose owner window has gone away (crashed apps never send NIM_DELETE).</summary>
        public void Prune()
        {
            if (_icons.RemoveAll(delegate (TrayIcon i) { return !Native.IsWindow(i.HWnd); }) > 0) RaiseChanged();
        }

        // ---- mouse forwarding ---------------------------------------------------
        /// <summary>Send a mouse message to the icon's owner, following the legacy / version-4 callback conventions.</summary>
        public void Post(TrayIcon ic, uint mouseMsg, int screenX, int screenY)
        {
            if (ic == null || ic.CallbackMessage == 0 || !Native.IsWindow(ic.HWnd)) return;
            // early Windows 11 (before 22H2) crashes its own Volume / Network / Power flyouts when clicked from another tray
            int build = Environment.OSVersion.Version.Build;
            if (build >= 22000 && build < 22621 && (ic.Guid == VolumeGuid || ic.Guid == NetworkGuid || ic.Guid == PowerGuid)) return;
            uint pid; Native.GetWindowThreadProcessId(ic.HWnd, out pid);
            Native.AllowSetForegroundWindow(pid);

            IntPtr wParam, lParam;
            if (ic.Version >= 4)
            {
                wParam = new IntPtr(((screenY & 0xFFFF) << 16) | (screenX & 0xFFFF));
                lParam = new IntPtr(unchecked((int)(((ic.UID & 0xFFFF) << 16) | (mouseMsg & 0xFFFF))));
            }
            else
            {
                wParam = new IntPtr(unchecked((int)ic.UID));
                lParam = new IntPtr(unchecked((int)mouseMsg));
            }
            Native.PostMessage(ic.HWnd, ic.CallbackMessage, wParam, lParam);
        }

        public void LeftDown(TrayIcon ic, int x, int y) { Post(ic, Native.WM_LBUTTONDOWN, x, y); }
        public void LeftUp(TrayIcon ic, int x, int y)
        {
            Post(ic, Native.WM_LBUTTONUP, x, y);
            if (ic != null && ic.Version >= 3) Post(ic, Native.NIN_SELECT, x, y);     // documented for v4; Explorer sends it for v3 too
        }
        public void LeftDouble(TrayIcon ic, int x, int y) { Post(ic, Native.WM_LBUTTONDBLCLK, x, y); }
        public void RightDown(TrayIcon ic, int x, int y) { Post(ic, Native.WM_RBUTTONDOWN, x, y); }
        public void RightUp(TrayIcon ic, int x, int y)
        {
            Post(ic, Native.WM_RBUTTONUP, x, y);
            if (ic != null && ic.Version >= 3) Post(ic, Native.WM_CONTEXTMENU, x, y);
        }
        public void MouseMove(TrayIcon ic, int x, int y) { Post(ic, 0x0200 /*WM_MOUSEMOVE*/, x, y); }
        public void MouseEnter(TrayIcon ic, int x, int y)
        {
            Post(ic, 0x02A1 /*WM_MOUSEHOVER*/, x, y);
            if (ic != null && ic.Version > 3) Post(ic, 0x0406 /*NIN_POPUPOPEN*/, x, y);    // rich hover popups (e.g. Windows Security, OneDrive)
        }
        public void MouseLeave(TrayIcon ic, int x, int y)
        {
            Post(ic, 0x02A3 /*WM_MOUSELEAVE*/, x, y);
            if (ic != null && ic.Version > 3) Post(ic, 0x0407 /*NIN_POPUPCLOSE*/, x, y);
        }

        public void Dispose()
        {
            if (_raise != null) { _raise.Stop(); _raise = null; }
            bool wasAlongside = _hwnd != IntPtr.Zero;
            ShellServices.OwnTray = IntPtr.Zero;
            if (_notifyWnd != IntPtr.Zero) { Native.DestroyWindow(_notifyWnd); _notifyWnd = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero) { Native.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
            if (_classesRegistered)
            {
                IntPtr hInst = Native.GetModuleHandle(null);
                Native.UnregisterClass("TrayNotifyWnd", hInst);
                Native.UnregisterClass("Shell_TrayWnd", hInst);
                _classesRegistered = false;
            }
            if (wasAlongside) BroadcastTaskbarCreated();     // apps hand their icons back to Explorer
        }
    }
}
