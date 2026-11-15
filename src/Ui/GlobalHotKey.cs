using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>A system-wide key combination (RegisterHotKey) on a hidden message-only window.</summary>
    public sealed class GlobalHotKey : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        const int WM_HOTKEY = 0x0312;
        const int Id = 0x5653;

        HwndSource _src;
        bool _on;
        public event Action Pressed;

        /// <summary>false when Windows refused it (another program already uses the combination).</summary>
        public bool Register(uint mods, uint vk)
        {
            try
            {
                var p = new HwndSourceParameters("vShell hot key") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };   // HWND_MESSAGE
                _src = new HwndSource(p);
                _src.AddHook(Hook);
                _on = RegisterHotKey(_src.Handle, Id, mods | MOD_NOREPEAT, vk);
                if (!_on) Log.Write("Hot key: Windows refused it (error " + Marshal.GetLastWin32Error() + ") - another program may use it");
                return _on;
            }
            catch (Exception ex) { Log.Write("Hot key: " + ex.Message); return false; }
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
            {
                handled = true;
                var h = Pressed;
                if (h != null) h();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            try { if (_on && _src != null) UnregisterHotKey(_src.Handle, Id); } catch { }
            _on = false;
            if (_src != null) { try { _src.Dispose(); } catch { } _src = null; }
        }
    }
}
