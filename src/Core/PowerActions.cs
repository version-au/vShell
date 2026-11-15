using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    /// <summary>Lock / sleep / sign out / restart / shut down. These work the same whether or not Explorer is running.</summary>
    public static class PowerActions
    {
        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

        public static void Lock()
        {
            try { LockWorkStation(); } catch (Exception ex) { Log.Write("Lock: " + ex.Message); }
        }

        public static void Sleep()
        {
            try { SetSuspendState(false, true, false); } catch (Exception ex) { Log.Write("Sleep: " + ex.Message); }
        }

        public static void SignOut() { Shutdown("/l"); }
        public static void Restart() { Shutdown("/r /t 0"); }
        public static void ShutDown() { Shutdown("/s /t 0"); }

        static void Shutdown(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("shutdown " + args + ": " + ex.Message); }
        }
    }
}
