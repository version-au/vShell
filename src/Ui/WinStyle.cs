using System;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>Keeping vShell's own windows (bars, widgets, popups) out of the taskbar, Alt+Tab and Aero Peek.</summary>
    public static class WinStyle
    {
        public static readonly bool IsWindows11 = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;

        const int GW_OWNER = 4, DWMWA_EXCLUDED_FROM_PEEK = 12;

        /// <summary>
        /// The usual way is to make the window a tool window. On Windows 11 a tool window no longer receives WM_DPICHANGED, so it
        /// does not rescale when moved to a monitor with different scaling (dotnet/wpf#10422). There, as ManagedShell does, the
        /// window stays a normal window and its hidden WPF owner (every ShowInTaskbar=false window has one) becomes the tool window
        /// instead - which hides the window just the same. The window must have ShowInTaskbar = false.
        /// </summary>
        public static void HideFromTasks(IntPtr hwnd, bool noActivate)
        {
            if (hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongAuto(hwnd, Native.GWL_EXSTYLE) & ~Native.WS_EX_APPWINDOW;
            if (noActivate) ex |= Native.WS_EX_NOACTIVATE;

            IntPtr owner = IsWindows11 ? Native.GetWindow(hwnd, GW_OWNER) : IntPtr.Zero;
            if (owner != IntPtr.Zero && Native.GetTitle(owner) == "Hidden Window")
            {
                long oex = Native.GetWindowLongAuto(owner, Native.GWL_EXSTYLE);
                Native.SetWindowLongAuto(owner, Native.GWL_EXSTYLE, oex | Native.WS_EX_TOOLWINDOW);
                ex &= ~Native.WS_EX_TOOLWINDOW;
            }
            else ex |= Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongAuto(hwnd, Native.GWL_EXSTYLE, ex);

            int one = 1;
            try { Native.DwmSetWindowAttribute(hwnd, DWMWA_EXCLUDED_FROM_PEEK, ref one, 4); } catch { }
        }
    }
}
