using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;

namespace VShellShelf
{
    /// <summary>One thing in Windows' clipboard history (Win+V): text or a picture.</summary>
    public sealed class ClipEntry
    {
        public string Id = "";
        public DateTime When;           // UTC
        /// <summary>text, image or other.</summary>
        public string Kind = "other";
        public string Text = "";
        /// <summary>The picture as Windows keeps it (PNG / BMP bytes).</summary>
        public byte[] Image;
        public string Formats = "";
        /// <summary>The picture, small (made on the UI thread when first shown).</summary>
        public ImageSource Thumb;
    }

    /// <summary>What was read: Status ok / off (history is turned off in Windows) / denied / error, and the items, newest first.</summary>
    public sealed class ClipSnapshot
    {
        public string Status = "ok";
        public List<ClipEntry> Items = new List<ClipEntry>();
    }

    /// <summary>
    /// Windows' own clipboard history, read through WinRT's Windows.ApplicationModel.DataTransfer.Clipboard (its COM interfaces called
    /// directly, as vShell's Now playing does - no extra DLLs). All WinRT calls run on one background thread. A hidden window hears each
    /// clipboard change (AddClipboardFormatListener) and reads the history again; Latest / Changed are for the UI thread.
    /// </summary>
    public static class ClipHistory
    {
        // ---- WinRT plumbing (the vtable slots were read from Windows' metadata; IInspectable takes 0-5) ----
        [DllImport("combase.dll")] static extern int RoInitialize(int initType);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string source, int length, out IntPtr hstring);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int length);
        [DllImport("shcore.dll")] static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, ref Guid riid, out IntPtr ppv);
        [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetPtrFn(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIntFn(IntPtr self, out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetLongFn(IntPtr self, out long value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetByteFn(IntPtr self, out byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PtrGetByteFn(IntPtr self, IntPtr arg, out byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PtrGetIntFn(IntPtr self, IntPtr arg, out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AtFn(IntPtr self, uint index, out IntPtr value);

        const string ClipboardClass = "Windows.ApplicationModel.DataTransfer.Clipboard";
        const string FormatsClass = "Windows.ApplicationModel.DataTransfer.StandardDataFormats";
        static readonly Guid IID_ClipboardStatics2 = new Guid("D2AC1B6A-D29F-554B-B303-F0452345FE02");
        static readonly Guid IID_FormatsStatics = new Guid("7ED681A1-A880-40C9-B4ED-0BEE1E15F549");
        static readonly Guid IID_AsyncInfo = new Guid("00000036-0000-0000-C000-000000000046");
        static readonly Guid IID_IStream = new Guid("0000000C-0000-0000-C000-000000000046");

        const int Clip_GetHistoryItemsAsync = 6, Clip_ClearHistory = 7, Clip_DeleteItemFromHistory = 8, Clip_SetHistoryItemAsContent = 9, Clip_IsHistoryEnabled = 10;
        const int Result_Status = 6, Result_Items = 7;
        const int Vector_GetAt = 6, Vector_Size = 7;
        const int Item_Id = 6, Item_Timestamp = 7, Item_Content = 8;
        const int View_AvailableFormats = 9, View_Contains = 10, View_GetTextAsync = 12, View_GetHtmlFormatAsync = 15, View_GetBitmapAsync = 18;
        const int Formats_Text = 6, Formats_Html = 8, Formats_Bitmap = 10;
        const int StreamRef_OpenReadAsync = 6;
        const int Async_GetResults = 8, AsyncInfo_Status = 7;

        static IntPtr Slot(IntPtr obj, int slot) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size); }

        static IntPtr GetPtr(IntPtr obj, int slot)
        {
            IntPtr v;
            return Marshal.GetDelegateForFunctionPointer<GetPtrFn>(Slot(obj, slot))(obj, out v) < 0 ? IntPtr.Zero : v;
        }

        static int GetInt(IntPtr obj, int slot, int fallback)
        {
            int v;
            return Marshal.GetDelegateForFunctionPointer<GetIntFn>(Slot(obj, slot))(obj, out v) < 0 ? fallback : v;
        }

        static long GetLong(IntPtr obj, int slot, long fallback)
        {
            long v;
            return Marshal.GetDelegateForFunctionPointer<GetLongFn>(Slot(obj, slot))(obj, out v) < 0 ? fallback : v;
        }

        static bool GetBool(IntPtr obj, int slot)
        {
            byte v;
            return Marshal.GetDelegateForFunctionPointer<GetByteFn>(Slot(obj, slot))(obj, out v) >= 0 && v != 0;
        }

        static IntPtr At(IntPtr vector, uint i)
        {
            IntPtr v;
            return Marshal.GetDelegateForFunctionPointer<AtFn>(Slot(vector, Vector_GetAt))(vector, i, out v) < 0 ? IntPtr.Zero : v;
        }

        static void Release(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

        static IntPtr HString(string s)
        {
            IntPtr h;
            return WindowsCreateString(s, s.Length, out h) < 0 ? IntPtr.Zero : h;
        }

        /// <summary>Read and free an HSTRING.</summary>
        static string Str(IntPtr h)
        {
            if (h == IntPtr.Zero) return "";
            try
            {
                int len;
                IntPtr buf = WindowsGetStringRawBuffer(h, out len);
                return buf == IntPtr.Zero || len <= 0 ? "" : Marshal.PtrToStringUni(buf, len);
            }
            finally { WindowsDeleteString(h); }
        }

        static IntPtr Factory(string cls, Guid iid)
        {
            IntPtr h = HString(cls), f;
            try { return RoGetActivationFactory(h, ref iid, out f) < 0 ? IntPtr.Zero : f; }
            finally { WindowsDeleteString(h); }
        }

        /// <summary>Wait for an IAsyncOperation and return its result (a new reference, or an HSTRING), or zero. Releases the operation.</summary>
        static IntPtr Await(IntPtr op, int timeoutMs)
        {
            if (op == IntPtr.Zero) return IntPtr.Zero;
            IntPtr info = IntPtr.Zero;
            try
            {
                Guid iid = IID_AsyncInfo;
                if (Marshal.QueryInterface(op, in iid, out info) < 0 || info == IntPtr.Zero) return IntPtr.Zero;
                int status = 0, waited = 0;
                while (true)
                {
                    status = GetInt(info, AsyncInfo_Status, 3);
                    if (status != 0 || waited >= timeoutMs) break;      // 0 = still running
                    Pump();
                    Thread.Sleep(5);
                    waited += 5;
                }
                return status == 1 ? GetPtr(op, Async_GetResults) : IntPtr.Zero;    // 1 = completed
            }
            finally
            {
                Release(info);
                Release(op);
            }
        }

        // ---- the worker thread ----
        // Windows' Clipboard class works only on a single-threaded (STA) thread (from a multithreaded one it's RO_E_UNSUPPORTED_FROM_MTA),
        // and putting an item back on the clipboard can make this thread the clipboard's owner - so it keeps a message loop running (a
        // Dispatcher), also while it waits for an answer (Pump), or pasting in another app could hang.
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; public uint lPrivate; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);

        static Dispatcher _worker;
        static bool _busy;
        static IntPtr _clip;                // IClipboardStatics2
        static string _fmtText, _fmtHtml, _fmtBitmap;
        static readonly Dictionary<string, ClipEntry> Seen = new Dictionary<string, ClipEntry>();     // read once: an item's content never changes

        static void Pump()
        {
            MSG m;
            for (int i = 0; i < 50 && PeekMessage(out m, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */); i++)
            {
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        static void Run(Action job)
        {
            if (_worker == null)
            {
                var ready = new ManualResetEventSlim();
                var t = new Thread(delegate ()
                {
                    RoInitialize(0);        // single-threaded (the thread already is)
                    _worker = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                }) { IsBackground = true, Name = "Shelf clipboard" };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                ready.Wait();
            }
            Action wrapped = null;
            wrapped = delegate
            {
                // a job that arrives while another waits for Windows (and pumps messages) comes after it, not inside it
                if (_busy) { _worker.BeginInvoke(DispatcherPriority.Background, wrapped); return; }
                _busy = true;
                try { job(); }
                catch (Exception ex) { Log.Write("Shelf clipboard: " + ex.Message); }
                finally { _busy = false; }
            };
            _worker.BeginInvoke(DispatcherPriority.Normal, wrapped);
        }

        static bool Ready()
        {
            if (_clip != IntPtr.Zero) return true;
            _clip = Factory(ClipboardClass, IID_ClipboardStatics2);
            IntPtr f = Factory(FormatsClass, IID_FormatsStatics);
            if (f != IntPtr.Zero)
            {
                _fmtText = Str(GetPtr(f, Formats_Text));
                _fmtHtml = Str(GetPtr(f, Formats_Html));
                _fmtBitmap = Str(GetPtr(f, Formats_Bitmap));
                Release(f);
            }
            if (_clip == IntPtr.Zero) Log.Write("Shelf clipboard: Windows' clipboard history can't be used here");
            return _clip != IntPtr.Zero;
        }

        // ---- what the UI uses ----

        /// <summary>The last read (null until the first one is done).</summary>
        public static ClipSnapshot Latest;

        /// <summary>A new read is in Latest (on the UI thread).</summary>
        public static event Action Changed;

        static void Publish(ClipSnapshot s)
        {
            Application app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                Latest = s;
                Action h = Changed;
                if (h != null) h();
            }));
        }

        /// <summary>Read the history again (in the background).</summary>
        public static void Refresh()
        {
            Run(delegate { Publish(Read()); });
        }

        static ClipSnapshot Read()
        {
            var snap = new ClipSnapshot();
            if (!Ready()) { snap.Status = "error"; return snap; }
            if (!GetBool(_clip, Clip_IsHistoryEnabled)) { snap.Status = "off"; return snap; }
            IntPtr res = Await(GetPtr(_clip, Clip_GetHistoryItemsAsync), 4000);
            if (res == IntPtr.Zero) { snap.Status = "error"; return snap; }
            IntPtr items = IntPtr.Zero;
            try
            {
                int status = GetInt(res, Result_Status, 3);
                if (status != 0) { snap.Status = status == 1 ? "denied" : status == 2 ? "off" : "error"; return snap; }
                items = GetPtr(res, Result_Items);
                if (items == IntPtr.Zero) return snap;
                int n = GetInt(items, Vector_Size, 0);
                var keep = new HashSet<string>();
                for (uint i = 0; i < n && i < 100; i++)
                {
                    IntPtr it = At(items, i);
                    if (it == IntPtr.Zero) continue;
                    try
                    {
                        string id = Str(GetPtr(it, Item_Id));
                        keep.Add(id);
                        ClipEntry e;
                        if (!Seen.TryGetValue(id, out e))
                        {
                            e = new ClipEntry { Id = id };
                            long ts = GetLong(it, Item_Timestamp, 0);
                            e.When = ts > 0 ? DateTime.FromFileTimeUtc(ts) : DateTime.UtcNow;
                            IntPtr view = GetPtr(it, Item_Content);
                            try { if (view != IntPtr.Zero) ReadContent(view, e); }
                            finally { Release(view); }
                            Seen[id] = e;
                        }
                        snap.Items.Add(e);
                    }
                    finally { Release(it); }
                }
                foreach (string id in new List<string>(Seen.Keys)) if (!keep.Contains(id)) Seen.Remove(id);
            }
            finally
            {
                Release(items);
                Release(res);
            }
            snap.Items.Sort(delegate (ClipEntry a, ClipEntry b) { return b.When.CompareTo(a.When); });
            return snap;
        }

        static bool Has(IntPtr view, string format)
        {
            if (string.IsNullOrEmpty(format)) return false;
            IntPtr h = HString(format);
            try
            {
                byte v;
                return Marshal.GetDelegateForFunctionPointer<PtrGetByteFn>(Slot(view, View_Contains))(view, h, out v) >= 0 && v != 0;
            }
            finally { WindowsDeleteString(h); }
        }

        static void ReadContent(IntPtr view, ClipEntry e)
        {
            // the formats it has, for an item that's neither text nor a picture
            IntPtr fmts = GetPtr(view, View_AvailableFormats);
            if (fmts != IntPtr.Zero)
            {
                var names = new List<string>();
                int n = GetInt(fmts, Vector_Size, 0);
                for (uint i = 0; i < n && i < 20; i++) names.Add(Str(At(fmts, i)));
                Release(fmts);
                e.Formats = string.Join(", ", names);
            }
            if (Has(view, _fmtBitmap))
            {
                e.Image = ReadBitmap(view);
                if (e.Image != null) { e.Kind = "image"; return; }
            }
            if (Has(view, _fmtText))
            {
                e.Text = Str(Await(GetPtr(view, View_GetTextAsync), 3000));
                e.Kind = "text";
                return;
            }
            if (Has(view, _fmtHtml))
            {
                // only HTML: its text, roughly (tags and the CF_HTML header taken out)
                string html = Str(Await(GetPtr(view, View_GetHtmlFormatAsync), 3000));
                int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
                int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
                if (start >= 0 && end > start) html = html.Substring(start + 20, end - start - 20);
                e.Text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Trim();
                e.Text = Regex.Replace(e.Text, "[ \\t]{2,}", " ");
                e.Kind = "text";
            }
        }

        static byte[] ReadBitmap(IntPtr view)
        {
            IntPtr streamRef = Await(GetPtr(view, View_GetBitmapAsync), 3000);
            if (streamRef == IntPtr.Zero) return null;
            IntPtr stream = IntPtr.Zero, istream = IntPtr.Zero;
            try
            {
                stream = Await(GetPtr(streamRef, StreamRef_OpenReadAsync), 3000);
                if (stream == IntPtr.Zero) return null;
                Guid iid = IID_IStream;
                if (CreateStreamOverRandomAccessStream(stream, ref iid, out istream) < 0 || istream == IntPtr.Zero) return null;
                var com = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(istream);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[65536];
                        IntPtr readPtr = Marshal.AllocHGlobal(4);
                        try
                        {
                            while (ms.Length < 16 * 1024 * 1024)
                            {
                                Marshal.WriteInt32(readPtr, 0);
                                com.Read(buf, buf.Length, readPtr);
                                int n = Marshal.ReadInt32(readPtr);
                                if (n <= 0) break;
                                ms.Write(buf, 0, n);
                            }
                        }
                        finally { Marshal.FreeHGlobal(readPtr); }
                        return ms.Length > 0 ? ms.ToArray() : null;
                    }
                }
                finally { Marshal.ReleaseComObject(com); }
            }
            catch (Exception ex) { Log.Write("Shelf clipboard: a picture - " + ex.Message); return null; }
            finally
            {
                Release(istream);
                Release(stream);
                Release(streamRef);
            }
        }

        /// <summary>Find one item by id and do something with it (on the worker thread), then read the history again.</summary>
        static void WithItem(string id, Action<IntPtr> act)
        {
            Run(delegate
            {
                if (!Ready()) return;
                IntPtr res = Await(GetPtr(_clip, Clip_GetHistoryItemsAsync), 4000);
                if (res == IntPtr.Zero) return;
                IntPtr items = GetPtr(res, Result_Items);
                try
                {
                    if (items == IntPtr.Zero) return;
                    int n = GetInt(items, Vector_Size, 0);
                    for (uint i = 0; i < n; i++)
                    {
                        IntPtr it = At(items, i);
                        if (it == IntPtr.Zero) continue;
                        try
                        {
                            if (Str(GetPtr(it, Item_Id)) == id) { act(it); break; }
                        }
                        finally { Release(it); }
                    }
                }
                finally
                {
                    Release(items);
                    Release(res);
                }
                Publish(Read());
            });
        }

        /// <summary>Put an item back on the clipboard (as Win+V does); done(true) when it worked - on the UI thread.</summary>
        public static void CopyBack(string id, Action<bool> done)
        {
            WithItem(id, delegate (IntPtr it)
            {
                int status;
                int hr = Marshal.GetDelegateForFunctionPointer<PtrGetIntFn>(Slot(_clip, Clip_SetHistoryItemAsContent))(_clip, it, out status);
                bool ok = hr >= 0 && status == 0;
                if (!ok) Log.Write("Shelf clipboard: couldn't put it back on the clipboard (" + hr.ToString("x") + ", " + status + ")");
                if (done != null && Application.Current != null) Application.Current.Dispatcher.BeginInvoke(new Action(delegate { done(ok); }));
            });
        }

        /// <summary>Take an item out of Windows' history.</summary>
        public static void Delete(string id)
        {
            WithItem(id, delegate (IntPtr it)
            {
                byte ok;
                Marshal.GetDelegateForFunctionPointer<PtrGetByteFn>(Slot(_clip, Clip_DeleteItemFromHistory))(_clip, it, out ok);
            });
        }

        /// <summary>Clear the history (Windows keeps the items pinned in Win+V).</summary>
        public static void Clear()
        {
            Run(delegate
            {
                if (!Ready()) return;
                GetBool(_clip, Clip_ClearHistory);
                Publish(Read());
            });
        }

        // ---- hearing clipboard changes ----
        static HwndSource _listener;
        static DispatcherTimer _soon;

        /// <summary>Start hearing clipboard changes (the Clipboard tab is on). Reads the history now too.</summary>
        public static void Watch()
        {
            if (_listener == null)
            {
                try
                {
                    var p = new HwndSourceParameters("vShell Shelf clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };   // a message-only window
                    _listener = new HwndSource(p);
                    _listener.AddHook(delegate (IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
                    {
                        if (msg == 0x031D) Soon();       // WM_CLIPBOARDUPDATE
                        return IntPtr.Zero;
                    });
                    if (!AddClipboardFormatListener(_listener.Handle)) Log.Write("Shelf clipboard: can't hear clipboard changes (" + Marshal.GetLastWin32Error() + ")");
                }
                catch (Exception ex) { Log.Write("Shelf clipboard: " + ex.Message); }
            }
            Refresh();
        }

        public static void StopWatching()
        {
            if (_soon != null) _soon.Stop();
            if (_listener == null) return;
            try { RemoveClipboardFormatListener(_listener.Handle); _listener.Dispose(); } catch { }
            _listener = null;
        }

        /// <summary>Windows adds the item to its history a moment after the clipboard changes: read it then.</summary>
        static void Soon()
        {
            if (_soon == null)
            {
                _soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _soon.Tick += delegate { _soon.Stop(); Refresh(); };
            }
            _soon.Stop();
            _soon.Start();
        }

        // ---- pictures ----

        /// <summary>The picture, decoded small for a row (null if it can't be read).</summary>
        public static ImageSource ThumbOf(ClipEntry e, int width)
        {
            if (e.Thumb != null || e.Image == null) return e.Thumb;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = width;
                bi.StreamSource = new MemoryStream(e.Image);
                bi.EndInit();
                bi.Freeze();
                e.Thumb = bi;
            }
            catch (Exception ex) { Log.Write("Shelf clipboard: a picture - " + ex.Message); }
            return e.Thumb;
        }

        /// <summary>The picture full size.</summary>
        public static BitmapSource Full(ClipEntry e)
        {
            if (e.Image == null) return null;
            try
            {
                BitmapDecoder d = BitmapDecoder.Create(new MemoryStream(e.Image), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource f = d.Frames[0];
                f.Freeze();
                return f;
            }
            catch (Exception ex) { Log.Write("Shelf clipboard: a picture - " + ex.Message); return null; }
        }
    }
}
