using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace vShell.Core
{
    /// <summary>
    /// Something that knows more about what's playing than Windows (a plugin: Jellyfin), registered in MediaSession.Helpers. Fill sets
    /// Source to its Name (and HelperSession) when it gives the times; when Windows shows nothing it may fill in everything and set HelperOnly.
    /// </summary>
    [PluginApi]
    public interface IMediaHelper
    {
        /// <summary>"Jellyfin" - also what MediaInfo.Source says when the times come from it.</summary>
        string Name { get; }
        /// <summary>It's set up (the poller keeps asking it even when Windows' media sessions are off).</summary>
        bool Ready(BarSettings s);
        /// <summary>Called about every second while something shows Now playing (on the media thread).</summary>
        void Fill(MediaInfo info, BarSettings s);
        /// <summary>A button for what it gave the times of: "toggle", "next", "previous" or "seek:&lt;seconds&gt;".</summary>
        void Send(MediaInfo cur, BarSettings s, string command);
    }

    /// <summary>What is playing right now (a snapshot; replaced, never changed).</summary>
    [PluginApi]
    public sealed class MediaInfo
    {
        /// <summary>true when Windows' media session list could be read at all.</summary>
        public bool Available;
        /// <summary>true when some app has a media session (playing or paused).</summary>
        public bool HasSession;
        public string Title = "", Artist = "", Album = "", App = "";
        /// <summary>Windows' PlaybackStatus: 0 closed, 1 opened, 2 changing, 3 stopped, 4 playing, 5 paused.</summary>
        public int Status;
        /// <summary>The album art as an image file (png / jpeg), or null.</summary>
        public byte[] Art;
        /// <summary>Changes whenever Art changes.</summary>
        public int ArtVersion;

        /// <summary>Where it is in the track and how long the track is (seconds; Duration 0 = not known), Position as of PosAt (UTC).</summary>
        public double Position, Duration;
        public DateTime PosAt = DateTime.UtcNow;
        public bool CanSeek;
        /// <summary>"Windows" (the media session), or the media helper the times (or everything) come from ("Jellyfin").</summary>
        public string Source = "Windows";
        /// <summary>The helper's session the times come from ("" = none).</summary>
        public string HelperSession = "";
        /// <summary>Only the helper knows about it (Windows has no media session for it): the buttons go to the helper.</summary>
        public bool HelperOnly;

        public bool Playing { get { return Status == 4; } }
        public bool HasProgress { get { return Duration > 0.5; } }

        /// <summary>Where it is now: moves on by itself while playing (between the updates).</summary>
        public double PositionNow
        {
            get
            {
                double p = Position;
                if (Playing) p += Math.Max(0, (DateTime.UtcNow - PosAt).TotalSeconds);
                return Duration > 0 ? Math.Max(0, Math.Min(Duration, p)) : Math.Max(0, p);
            }
        }

        /// <summary>1:05 / 1:02:07.</summary>
        public static string Time(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
        }
    }

    /// <summary>
    /// "Now playing" from Windows' media sessions (the same source as the volume flyout's media controls: Spotify, browsers, Plex,
    /// Windows Media Player, ...). Uses the Windows.Media.Control WinRT API directly through its COM interfaces, so no extra packages
    /// are needed. Everything runs on one background thread that polls once a second; the UI just reads <see cref="Current"/>.
    /// If the media sessions cannot be used, the play / next / previous commands fall back to the keyboard media keys.
    /// </summary>
    public static class MediaSession
    {
        // ---- WinRT plumbing ----------------------------------------------------------------------------
        [DllImport("combase.dll")] static extern int RoInitialize(int initType);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string source, int length, out IntPtr hstring);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int length);
        [DllImport("shcore.dll")] static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, ref Guid riid, out IntPtr ppv);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetPtrFn(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIntFn(IntPtr self, out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetLongFn(IntPtr self, out long value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SeekFn(IntPtr self, long position, out IntPtr op);

        const string ManagerClass = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
        static readonly Guid IID_ManagerStatics = new Guid("2050C4EE-11A0-57DE-AED7-C97C70338245");
        static readonly Guid IID_AsyncInfo = new Guid("00000036-0000-0000-C000-000000000046");
        static readonly Guid IID_IStream = new Guid("0000000C-0000-0000-C000-000000000046");

        // vtable slots (IInspectable takes 0-5)
        const int Statics_RequestAsync = 6;
        const int Manager_GetCurrentSession = 6;
        const int Session_SourceAppUserModelId = 6, Session_TryGetMediaPropertiesAsync = 7, Session_GetTimelineProperties = 8, Session_GetPlaybackInfo = 9,
                  Session_TryChangePlaybackPositionAsync = 24,
                  Session_TrySkipNextAsync = 16, Session_TrySkipPreviousAsync = 17, Session_TryTogglePlayPauseAsync = 20;
        const int Props_Title = 6, Props_Artist = 9, Props_AlbumTitle = 10, Props_Thumbnail = 15;
        const int Playback_Status = 7;
        const int Timeline_StartTime = 6, Timeline_EndTime = 7, Timeline_Position = 10, Timeline_LastUpdatedTime = 11;
        const int Async_GetResults = 8, AsyncInfo_Status = 7;
        const int StreamRef_OpenReadAsync = 6;

        static IntPtr Slot(IntPtr obj, int slot) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size); }

        static IntPtr GetPtr(IntPtr obj, int slot)
        {
            IntPtr v;
            int hr = Marshal.GetDelegateForFunctionPointer<GetPtrFn>(Slot(obj, slot))(obj, out v);
            return hr < 0 ? IntPtr.Zero : v;
        }

        static long GetLong(IntPtr obj, int slot, long fallback)
        {
            long v;
            int hr = Marshal.GetDelegateForFunctionPointer<GetLongFn>(Slot(obj, slot))(obj, out v);
            return hr < 0 ? fallback : v;
        }

        static int GetInt(IntPtr obj, int slot, int fallback)
        {
            int v;
            int hr = Marshal.GetDelegateForFunctionPointer<GetIntFn>(Slot(obj, slot))(obj, out v);
            return hr < 0 ? fallback : v;
        }

        static void Release(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

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

        /// <summary>Wait for an IAsyncOperation&lt;object&gt; and return its result (a new reference), or zero. Releases the operation.</summary>
        static IntPtr Await(IntPtr op, int timeoutMs, bool wantResult)
        {
            if (op == IntPtr.Zero) return IntPtr.Zero;
            IntPtr info = IntPtr.Zero, result = IntPtr.Zero;
            try
            {
                Guid iid = IID_AsyncInfo;
                if (Marshal.QueryInterface(op, in iid, out info) < 0 || info == IntPtr.Zero) return IntPtr.Zero;
                int status = 0, waited = 0;
                while (true)
                {
                    status = GetInt(info, AsyncInfo_Status, 3);
                    if (status != 0 || waited >= timeoutMs) break;      // 0 = started (still running)
                    Thread.Sleep(5);
                    waited += 5;
                }
                if (status == 1 && wantResult) result = GetPtr(op, Async_GetResults);   // 1 = completed
                return result;
            }
            finally
            {
                Release(info);
                Release(op);
            }
        }

        // ---- state -----------------------------------------------------------------------------------------
        static readonly object Gate = new object();
        static MediaInfo _current = new MediaInfo();
        static Thread _thread;
        static readonly ConcurrentQueue<string> Commands = new ConcurrentQueue<string>();
        static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        static IntPtr _manager;
        static bool _broken;
        static string _artKey = "";
        static byte[] _art;
        static int _artVersion;
        static DateTime _lastWanted = DateTime.UtcNow;

        /// <summary>The settings (for the media helpers); set by the Now playing modules.</summary>
        public static BarSettings Settings;
        /// <summary>The plugins that know more about what's playing than Windows (Jellyfin): they fill in the times and take the buttons.</summary>
        public static readonly List<IMediaHelper> Helpers = new List<IMediaHelper>();

        /// <summary>A new art version (a helper changed the picture).</summary>
        [PluginApi] public static int NewArtVersion() { return Interlocked.Increment(ref _artVersion); }

        static IMediaHelper[] HelpersNow { get { lock (Helpers) return Helpers.ToArray(); } }

        /// <summary>The latest snapshot. Reading it keeps the poller running (it rests when no module asks for a while).</summary>
        public static MediaInfo Current
        {
            get
            {
                Ensure();
                _lastWanted = DateTime.UtcNow;
                lock (Gate) return _current;
            }
        }

        /// <summary>"toggle", "next", "previous" or "seek:&lt;seconds&gt;".</summary>
        public static void Send(string command)
        {
            Ensure();
            Commands.Enqueue(command);
            Wake.Set();
        }

        static void Ensure()
        {
            if (_thread != null) return;
            lock (Gate)
            {
                if (_thread != null) return;
                _thread = new Thread(Run) { IsBackground = true, Name = "vShell media" };
                _thread.SetApartmentState(ApartmentState.MTA);
                _thread.Start();
            }
        }

        /// <summary>
        /// A marker file guards the first use: if vShell ever stopped while starting the media sessions, the next run leaves them alone
        /// (media keys only) instead of failing again. Delete media-session.off in the settings folder to try again.
        /// </summary>
        static string MarkerPath { get { return Path.Combine(BarSettings.ConfigDir, "media-session.starting"); } }
        static string OffPath { get { return Path.Combine(BarSettings.ConfigDir, "media-session.off"); } }

        static void Run()
        {
            try
            {
                if (File.Exists(MarkerPath))
                {
                    try { File.Move(MarkerPath, OffPath, true); } catch { }
                    Log.Write("Now playing: vShell stopped while starting Windows' media sessions last time - using the media keys only. Delete " + OffPath + " to try again.");
                }
                if (File.Exists(OffPath)) _broken = true;
                else
                {
                    try { File.WriteAllText(MarkerPath, DateTime.Now.ToString("s")); } catch { }
                    RoInitialize(1);        // multithreaded
                    _broken = !StartManager();
                    if (!_broken) Poll();   // one full poll before the guard is lifted
                    try { File.Delete(MarkerPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                _broken = true;
                Log.Write("Now playing: " + ex.Message);
                try { File.Delete(MarkerPath); } catch { }
            }
            if (_broken) lock (Gate) _current = new MediaInfo { Available = false };

            while (true)
            {
                Wake.WaitOne(1000);
                string cmd;
                while (Commands.TryDequeue(out cmd))
                {
                    try { DoCommand(cmd); }
                    catch (Exception ex) { Log.Write("Now playing command: " + ex.Message); }
                }
                if ((DateTime.UtcNow - _lastWanted).TotalSeconds > 10) continue;     // nobody is showing it
                if (_broken && (Settings == null || !Array.Exists(HelpersNow, delegate (IMediaHelper h) { return h.Ready(Settings); }))) continue;
                try { Poll(); }
                catch (Exception ex) { Log.Write("Now playing: " + ex.Message); }
            }
        }

        static bool StartManager()
        {
            IntPtr cls;
            if (WindowsCreateString(ManagerClass, ManagerClass.Length, out cls) < 0) return false;
            IntPtr statics = IntPtr.Zero;
            try
            {
                Guid iid = IID_ManagerStatics;
                int hr = RoGetActivationFactory(cls, ref iid, out statics);
                if (hr < 0 || statics == IntPtr.Zero)
                {
                    Log.Write("Now playing: media sessions are not available (0x" + hr.ToString("X8") + ") - using the media keys only.");
                    return false;
                }
                IntPtr op = GetPtr(statics, Statics_RequestAsync);
                _manager = Await(op, 5000, true);
                if (_manager == IntPtr.Zero) { Log.Write("Now playing: could not open the media session manager."); return false; }
                Log.Write("Now playing: using Windows' media sessions.");
                return true;
            }
            finally
            {
                Release(statics);
                WindowsDeleteString(cls);
            }
        }

        static void Poll()
        {
            var info = new MediaInfo { Available = _manager != IntPtr.Zero };
            IntPtr session = _manager == IntPtr.Zero ? IntPtr.Zero : GetPtr(_manager, Manager_GetCurrentSession);
            if (session == IntPtr.Zero)
            {
                AddHelpers(info);       // Windows has nothing: maybe a helper (Jellyfin) does
                lock (Gate) _current = info;
                return;
            }
            try
            {
                info.HasSession = true;
                info.App = AppName(Str(GetPtr(session, Session_SourceAppUserModelId)));

                IntPtr timeline = GetPtr(session, Session_GetTimelineProperties);
                if (timeline != IntPtr.Zero)
                {
                    try
                    {
                        long start = GetLong(timeline, Timeline_StartTime, 0), end = GetLong(timeline, Timeline_EndTime, 0);
                        long pos = GetLong(timeline, Timeline_Position, 0), at = GetLong(timeline, Timeline_LastUpdatedTime, 0);
                        info.Duration = Math.Max(0, (end - start) / 1e7);
                        info.Position = Math.Max(0, (pos - start) / 1e7);
                        DateTime when = DateTime.UtcNow;
                        if (at > 0) try { when = DateTime.FromFileTimeUtc(at); } catch { }
                        if (when > DateTime.UtcNow || (DateTime.UtcNow - when).TotalHours > 6) when = DateTime.UtcNow;
                        info.PosAt = when;
                        info.CanSeek = info.Duration > 0;
                    }
                    finally { Release(timeline); }
                }

                IntPtr playback = GetPtr(session, Session_GetPlaybackInfo);
                if (playback != IntPtr.Zero)
                {
                    info.Status = GetInt(playback, Playback_Status, 0);
                    Release(playback);
                }

                IntPtr props = Await(GetPtr(session, Session_TryGetMediaPropertiesAsync), 1500, true);
                if (props != IntPtr.Zero)
                {
                    try
                    {
                        info.Title = Str(GetPtr(props, Props_Title));
                        info.Artist = Str(GetPtr(props, Props_Artist));
                        info.Album = Str(GetPtr(props, Props_AlbumTitle));
                        string key = info.App + "|" + info.Title + "|" + info.Artist + "|" + info.Album;
                        if (key != _artKey)
                        {
                            _artKey = key;
                            _art = ReadThumbnail(props);
                            _artVersion++;
                        }
                    }
                    finally { Release(props); }
                }
            }
            finally { Release(session); }

            info.Art = _art;
            info.ArtVersion = _artVersion;
            AddHelpers(info);
            lock (Gate) _current = info;
        }

        // ---- the media helpers: the times for what an app plays that doesn't tell Windows where it is (the Jellyfin desktop app) ----------
        static void AddHelpers(MediaInfo info)
        {
            BarSettings s = Settings;
            if (s == null) return;
            foreach (IMediaHelper h in HelpersNow)
            {
                try
                {
                    if (!h.Ready(s)) continue;
                    h.Fill(info, s);
                    if (info.Source != "Windows") return;      // one helper is enough
                }
                catch (Exception ex) { Log.Write("Now playing " + h.Name + ": " + ex.Message); }
            }
        }

        static IMediaHelper HelperOf(MediaInfo cur)
        {
            if (cur == null || cur.HelperSession.Length == 0) return null;
            return Array.Find(HelpersNow, delegate (IMediaHelper h) { return h.Name == cur.Source; });
        }
        static byte[] ReadThumbnail(IntPtr props)
        {
            IntPtr streamRef = GetPtr(props, Props_Thumbnail);
            if (streamRef == IntPtr.Zero) return null;
            IntPtr stream = IntPtr.Zero, istream = IntPtr.Zero;
            try
            {
                stream = Await(GetPtr(streamRef, StreamRef_OpenReadAsync), 2000, true);
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
                            while (ms.Length < 8 * 1024 * 1024)
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
            catch (Exception ex) { Log.Write("Now playing art: " + ex.Message); return null; }
            finally
            {
                Release(istream);
                Release(stream);
                Release(streamRef);
            }
        }

        /// <summary>"Spotify.exe" -> "Spotify"; "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" -> "ZuneMusic"; Chrome / Edge / Firefox by name.</summary>
        static string AppName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            string s = aumid;
            int bang = s.LastIndexOf('!');
            if (bang >= 0) s = s.Substring(bang + 1);
            int slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
            if (slash >= 0) s = s.Substring(slash + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            string lower = s.ToLowerInvariant();
            if (lower == "chrome") return "Chrome";
            if (lower == "msedge" || lower == "microsoftedge") return "Edge";
            if (lower == "firefox") return "Firefox";
            if (lower.Length > 0) s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return s;
        }

        static void DoCommand(string cmd)
        {
            MediaInfo cur;
            lock (Gate) cur = _current;
            BarSettings s = Settings;
            IMediaHelper helper = s == null ? null : HelperOf(cur);
            if (cmd.StartsWith("seek:"))
            {
                double sec;
                if (!double.TryParse(cmd.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec)) return;
                if (helper != null) helper.Send(cur, s, cmd);
                else if (!_broken && _manager != IntPtr.Zero)
                {
                    IntPtr session = GetPtr(_manager, Manager_GetCurrentSession);
                    if (session != IntPtr.Zero)
                    {
                        try
                        {
                            // the timeline's StartTime is nearly always 0; the position is in 100 ns steps
                            IntPtr op;
                            int hr = Marshal.GetDelegateForFunctionPointer<SeekFn>(Slot(session, Session_TryChangePlaybackPositionAsync))(session, (long)(sec * 1e7), out op);
                            if (hr >= 0) Await(op, 1500, false);
                        }
                        finally { Release(session); }
                    }
                }
                Thread.Sleep(200);
                try { Poll(); } catch { }
                return;
            }
            if (helper != null && cur.HelperOnly)
            {
                // nothing in Windows' media sessions: the helper tells the app (Jellyfin's server)
                helper.Send(cur, s, cmd);
                Thread.Sleep(300);
                try { Poll(); } catch { }
                return;
            }
            int slot = cmd == "next" ? Session_TrySkipNextAsync : cmd == "previous" ? Session_TrySkipPreviousAsync : Session_TryTogglePlayPauseAsync;
            if (!_broken && _manager != IntPtr.Zero)
            {
                IntPtr session = GetPtr(_manager, Manager_GetCurrentSession);
                if (session != IntPtr.Zero)
                {
                    try { Await(GetPtr(session, slot), 1500, false); }
                    finally { Release(session); }
                    Thread.Sleep(150);
                    try { Poll(); } catch { }
                    return;
                }
            }
            // no media session: the keyboard media keys (most players listen to them)
            byte vk = cmd == "next" ? (byte)0xB0 : cmd == "previous" ? (byte)0xB1 : (byte)0xB3;
            keybd_event(vk, 0, 1 /*extended*/, UIntPtr.Zero);
            keybd_event(vk, 0, 1 | 2 /*key up*/, UIntPtr.Zero);
        }
    }
}
