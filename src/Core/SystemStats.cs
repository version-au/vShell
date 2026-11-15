using System;

namespace vShell.Core
{
    /// <summary>CPU and memory usage via GetSystemTimes / GlobalMemoryStatusEx (no performance counters needed).</summary>
    public sealed class SystemStats
    {
        public double CpuPercent { get; private set; }
        public double MemPercent { get; private set; }
        public double MemUsedGb { get; private set; }
        public double MemTotalGb { get; private set; }

        ulong _prevIdle, _prevKernel, _prevUser;
        bool _hasPrev;

        public void Update()
        {
            FILETIME idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                ulong i = idle.Value, k = kernel.Value, u = user.Value;
                if (_hasPrev)
                {
                    ulong dIdle = i - _prevIdle;
                    ulong dTotal = (k - _prevKernel) + (u - _prevUser);   // kernel time already includes idle time
                    if (dTotal > 0)
                        CpuPercent = Math.Max(0, Math.Min(100, 100.0 * (1.0 - (double)dIdle / dTotal)));
                }
                _prevIdle = i; _prevKernel = k; _prevUser = u; _hasPrev = true;
            }
            CpuCounters.Update();       // Task Manager's figures, clock speed, per thread - only while a CPU module wants them

            var ms = new MEMORYSTATUSEX();
            ms.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms))
            {
                MemPercent = ms.dwMemoryLoad;
                MemTotalGb = ms.ullTotalPhys / 1073741824.0;
                MemUsedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
            }
        }
    }
}
