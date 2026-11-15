using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace vShell.Core
{
    /// <summary>
    /// Detects a lone press of the Windows key (pressed and released with no other key in between) through a low-level keyboard hook.
    /// Combinations such as Win+E are left alone. A lone press is "masked" with an unassigned key so Windows itself
    /// does not also open its own Start menu. Must be created and started on the UI thread.
    /// </summary>
    public sealed class WinKeyHook : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        const uint LLKHF_INJECTED = 0x10;
        const byte VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MASK = 0xE8;     // 0xE8 is unassigned
        const uint KEYEVENTF_KEYUP = 0x2;

        readonly HookProc _proc;
        readonly Dispatcher _ui;
        IntPtr _hook;
        bool _winDown, _other;

        /// <summary>Raised on the UI thread.</summary>
        public event Action Pressed;

        public WinKeyHook()
        {
            _proc = Callback;
            _ui = Dispatcher.CurrentDispatcher;
        }

        public bool Start()
        {
            if (_hook != IntPtr.Zero) return true;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                Log.Write("Windows-key hook failed: error " + Marshal.GetLastWin32Error());
                return false;
            }
            return true;
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0)
                {
                    KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    if ((k.flags & LLKHF_INJECTED) == 0)          // ignore our own masking key and other programs' injected keys
                    {
                        int msg = wParam.ToInt32();
                        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                        bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                        bool isWin = k.vkCode == VK_LWIN || k.vkCode == VK_RWIN;

                        if (isWin)
                        {
                            if (down)
                            {
                                if (!_winDown) { _winDown = true; _other = false; }
                            }
                            else if (up)
                            {
                                bool lone = _winDown && !_other;
                                _winDown = false;
                                if (lone)
                                {
                                    // a key between Win-down and Win-up stops Windows opening its Start menu
                                    keybd_event(VK_MASK, 0, 0, UIntPtr.Zero);
                                    keybd_event(VK_MASK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                    _ui.BeginInvoke(new Action(delegate { Action h = Pressed; if (h != null) h(); }));
                                }
                            }
                        }
                        else if (down && _winDown) _other = true;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Windows-key hook: " + ex.Message); }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
