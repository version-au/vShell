using System;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    /// <summary>Shell properties of a window: the AppUserModelID that identifies which (Store) app it belongs to.</summary>
    public static class WindowProps
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PROPERTYKEY { public Guid fmtid; public uint pid; }

        // PROPVARIANT: vt + 3 reserved words, then a 16-byte union (24 bytes on 64-bit)
        [StructLayout(LayoutKind.Sequential)]
        struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p, p2; }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int Commit();
        }

        [DllImport("shell32.dll")]
        static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

        const ushort VT_LPWSTR = 31;

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint length, System.Text.StringBuilder id);

        /// <summary>
        /// The Store app id of a packaged desktop program (WhatsApp, new Teams, new Outlook, Windows Terminal ...) from its process, or "" for an
        /// ordinary program. These apps have ordinary windows - not ApplicationFrameWindow - and often don't put the id on the window.
        /// </summary>
        public static string GetProcessAumid(uint pid)
        {
            if (pid == 0) return "";
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                uint len = 256;
                var sb = new System.Text.StringBuilder((int)len);
                int rc = GetApplicationUserModelId(h, ref len, sb);
                if (rc == 122 /* ERROR_INSUFFICIENT_BUFFER */) { sb = new System.Text.StringBuilder((int)len); rc = GetApplicationUserModelId(h, ref len, sb); }
                return rc == 0 ? sb.ToString() : "";      // 15703 = APPMODEL_ERROR_NO_APPLICATION: not a packaged app
            }
            catch { return ""; }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// The package family of a program inside C:\Program Files\WindowsApps\ ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm"), or "" (the folder
        /// is Name_Version_Architecture_ResourceId_PublisherId).
        /// </summary>
        public static string PackageFamilyFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int at = path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return "";
            string rest = path.Substring(at + 13);
            int slash = rest.IndexOf('\\');
            string folder = slash >= 0 ? rest.Substring(0, slash) : rest;
            string[] parts = folder.Split('_');
            if (parts.Length < 5) return "";
            return parts[0] + "_" + parts[parts.Length - 1];
        }

        /// <summary>The window's AppUserModelID (e.g. "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), or null.</summary>
        public static string GetAumid(IntPtr hwnd)
        {
            IPropertyStore store = null;
            try
            {
                Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return null;
                var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
                PROPVARIANT v;
                if (store.GetValue(ref key, out v) != 0) return null;
                try { return v.vt == VT_LPWSTR && v.p != IntPtr.Zero ? Marshal.PtrToStringUni(v.p) : null; }
                finally { PropVariantClear(ref v); }
            }
            catch (Exception ex) { Log.Write("AUMID: " + ex.Message); return null; }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }
    }
}
