using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    /// <summary>Minimal Performance Data Helper (pdh.dll) wrapper - used for the GPU counters, no NuGet package needed.</summary>
    internal static class Pdh
    {
        const uint PDH_FMT_DOUBLE = 0x00000200;
        const uint PDH_MORE_DATA = 0x800007D2;

        [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQuery(string dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
        static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
        static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr query);

        public static bool Supported { get { return IntPtr.Size == 8; } }

        public static IntPtr OpenQuery()
        {
            IntPtr q;
            return PdhOpenQuery(null, IntPtr.Zero, out q) == 0 ? q : IntPtr.Zero;
        }

        public static IntPtr AddCounter(IntPtr query, string path)
        {
            IntPtr c;
            return PdhAddEnglishCounter(query, path, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero;
        }

        public static bool Collect(IntPtr query) { return PdhCollectQueryData(query) == 0; }

        public static void Close(IntPtr query) { if (query != IntPtr.Zero) PdhCloseQuery(query); }

        /// <summary>All instances of a wildcard counter as (instance name, value). Instances without valid data are skipped.</summary>
        public static List<KeyValuePair<string, double>> ReadArray(IntPtr counter)
        {
            var result = new List<KeyValuePair<string, double>>();
            if (counter == IntPtr.Zero || !Supported) return result;

            uint size = 0, count = 0;
            uint r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
            if (r != PDH_MORE_DATA || size == 0) return result;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, ref count, buf);
                if (r != 0) return result;

                // PDH_FMT_COUNTERVALUE_ITEM_W on x64: { LPWSTR name @0; DWORD CStatus @8; double value @16 } = 24 bytes
                const int itemSize = 24, statusOff = 8, valueOff = 16;
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + (long)i * itemSize);
                    int status = Marshal.ReadInt32(p, statusOff);
                    if (status != 0 && status != 1) continue;   // PDH_CSTATUS_VALID_DATA / NEW_DATA
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, 0));
                    if (name == null) continue;
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, valueOff));
                    result.Add(new KeyValuePair<string, double>(name, v));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return result;
        }
    }
}
