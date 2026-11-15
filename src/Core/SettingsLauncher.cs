using System;
using System.Diagnostics;
using System.Threading;

namespace vShell.Core
{
    /// <summary>
    /// Opens Windows Settings pages. The ms-settings: links need Explorer's app-activation services, which fail ("Class not registered",
    /// 0x80040900) when vShell is the shell, so each page has a classic Control Panel fallback that does not.
    /// </summary>
    public static class SettingsLauncher
    {
        public static bool IsSettingsUri(string target)
        {
            return target != null && target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Open an ms-settings: page (or fall back to the classic equivalent). Runs off the UI thread because a failing activation can take seconds.</summary>
        public static void Open(string uri)
        {
            ThreadPool.QueueUserWorkItem(delegate { Run(uri); });
        }

        static void Run(string uri)
        {
            string u = (uri ?? "ms-settings:").ToLowerInvariant();
            if (TryStart(uri, null)) return;

            Log.Write("Settings: ms-settings link failed, trying the classic page for " + u);
            if (u.StartsWith("ms-settings:display"))
            {
                if (TryStart("rundll32.exe", "display.dll,ShowAdapterSettings")) return;
            }
            else if (u.StartsWith("ms-settings:sound"))
            {
                if (TryStart("control.exe", "mmsys.cpl")) return;
            }
            else if (u.StartsWith("ms-settings:personalize"))
            {
                if (TryStart("control.exe", "/name Microsoft.Personalization")) return;
            }
            else if (u.StartsWith("ms-settings:network"))
            {
                if (TryStart("control.exe", "ncpa.cpl")) return;
            }
            // anything else (or a failed classic page): the Control Panel itself
            TryStart("control.exe", null);
        }

        static bool TryStart(string file, string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Settings: '" + file + (args != null ? " " + args : "") + "' failed: " + ex.Message);
                return false;
            }
        }
    }
}
