using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    /// <summary>
    /// An app playing (or able to play) sound on the default speakers: one or more Windows audio sessions grouped by program (a
    /// browser plays from several processes), or Windows' system sounds. Holds COM objects - Audio.Release(apps) when done.
    /// </summary>
    public sealed class AudioApp
    {
        /// <summary>Device + program: the device's id, then the program's path in lower case ("system" for system sounds, "pid:123"
        /// when the path can't be read).</summary>
        public string Key = "";
        /// <summary>The output device it plays on (an app can be on several - Wave Link's virtual devices, for one).</summary>
        public string DeviceId = "", DeviceName = "";
        public bool OnDefault;
        /// <summary>One of its sessions is playing right now (Windows calls a session inactive a few seconds after it goes quiet).</summary>
        public bool Active;
        public string Name = "";
        /// <summary>The program, for its icon ("" for system sounds).</summary>
        public string ExePath = "";
        public bool IsSystem;
        /// <summary>The processes playing (for choosing which device they play on).</summary>
        public readonly List<uint> Pids = new List<uint>();
        internal readonly List<object> Sessions = new List<object>();     // IAudioSessionControl2 each
    }

    // Per-app volume, after EarTrumpet (MIT, https://github.com/File-New-Project/EarTrumpet): the session interfaces of Windows'
    // Core Audio API (audiopolicy.h / endpointvolume.h). Polled while the volume mixer is open - nothing runs while it's closed.
    public static partial class Audio
    {
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
            [PreserveSig] int RegisterSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
            [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
            [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        // IAudioSessionControl's methods first (same vtable), then IAudioSessionControl2's own
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();      // S_OK (0) = it is, S_FALSE (1) = it isn't
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
            [PreserveSig] int GetMeteringChannelCount(out uint count);
            [PreserveSig] int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray)] float[] peaks);
            [PreserveSig] int QueryHardwareSupport(out uint mask);
        }

        static readonly Guid IidSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        static readonly Guid IidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        const int SessionActive = 1, SessionExpired = 2;

        /// <summary>The default speakers' current level 0..1, or 0.</summary>
        public static float MasterPeak() { return Peak(""); }

        // ---- which device an app plays on (Windows' "App volume and device preferences") ----------------------
        // Undocumented: the WinRT factory Windows.Media.Internal.AudioPolicyConfig, in the layout Windows 11 (21H2+, build 21390) uses -
        // as EarTrumpet does. Its first 19 methods (after IInspectable's 3) aren't used; the slots just keep the vtable right.
        [ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioPolicyConfig21H2
        {
            [PreserveSig] int GetIids(); [PreserveSig] int GetRuntimeClassName(); [PreserveSig] int GetTrustLevel();
            [PreserveSig] int S1(); [PreserveSig] int S2(); [PreserveSig] int S3(); [PreserveSig] int S4(); [PreserveSig] int S5();
            [PreserveSig] int S6(); [PreserveSig] int S7(); [PreserveSig] int S8(); [PreserveSig] int S9(); [PreserveSig] int S10();
            [PreserveSig] int S11(); [PreserveSig] int S12(); [PreserveSig] int S13(); [PreserveSig] int S14(); [PreserveSig] int S15();
            [PreserveSig] int S16(); [PreserveSig] int S17(); [PreserveSig] int S18(); [PreserveSig] int S19();
            [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, IntPtr deviceId);
            [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, out IntPtr deviceId);
            [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
        }

        [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string s, int length, out IntPtr hstring);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int length);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

        const string DevicePrefix = @"\\?\SWD#MMDEVAPI#", RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        static IAudioPolicyConfig21H2 _policy;
        static bool _policyTried;

        static IAudioPolicyConfig21H2 Policy()
        {
            if (_policyTried) return _policy;
            _policyTried = true;
            if (Environment.OSVersion.Version.Build < 21390) return null;      // older Windows has another layout: not offered
            IntPtr cls = IntPtr.Zero, f = IntPtr.Zero;
            try
            {
                const string name = "Windows.Media.Internal.AudioPolicyConfig";
                WindowsCreateString(name, name.Length, out cls);
                Guid iid = typeof(IAudioPolicyConfig21H2).GUID;
                int hr = RoGetActivationFactory(cls, ref iid, out f);
                if (hr != 0 || f == IntPtr.Zero) { Log.Write("Audio: per-app devices not available (0x" + hr.ToString("X8") + ")"); return null; }
                _policy = (IAudioPolicyConfig21H2)Marshal.GetTypedObjectForIUnknown(f, typeof(IAudioPolicyConfig21H2));
            }
            catch (Exception ex) { Log.Write("Audio: per-app devices: " + ex.Message); _policy = null; }
            finally
            {
                if (f != IntPtr.Zero) Marshal.Release(f);
                if (cls != IntPtr.Zero) WindowsDeleteString(cls);
            }
            return _policy;
        }

        /// <summary>Can apps be sent to a device of their own here (Windows 11)?</summary>
        public static bool AppDevicesAvailable { get { return Policy() != null; } }

        /// <summary>The device the app is set to play on ("" = it follows the default device, or it can't be read).</summary>
        public static string AppDevice(AudioApp app)
        {
            IAudioPolicyConfig21H2 p = Policy();
            if (p == null) return "";
            foreach (uint pid in app.Pids)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    if (p.GetPersistedDefaultAudioEndpoint(pid, ERender, EMultimedia, out h) != 0 || h == IntPtr.Zero) continue;
                    int n;
                    string s = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(h, out n), n) ?? "";
                    if (s.StartsWith(DevicePrefix)) s = s.Substring(DevicePrefix.Length);
                    if (s.EndsWith(RenderSuffix)) s = s.Substring(0, s.Length - RenderSuffix.Length);
                    if (s.Length > 0) return s;
                }
                catch { }
                finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
            }
            return "";
        }

        /// <summary>Send the app (all its processes) to a device ("" = back to following the default device). Windows remembers it.</summary>
        public static bool SetAppDevice(AudioApp app, string deviceId)
        {
            IAudioPolicyConfig21H2 p = Policy();
            if (p == null || app.Pids.Count == 0) return false;
            IntPtr h = IntPtr.Zero;
            bool ok = true;
            try
            {
                if (!string.IsNullOrEmpty(deviceId))
                {
                    string full = DevicePrefix + deviceId + RenderSuffix;
                    WindowsCreateString(full, full.Length, out h);
                }
                foreach (uint pid in app.Pids)
                    for (int role = 0; role < 2; role++)      // eConsole and eMultimedia, as Windows' own page does
                        if (p.SetPersistedDefaultAudioEndpoint(pid, ERender, role, h) != 0) ok = false;
            }
            catch (Exception ex) { Log.Write("Audio: app device: " + ex.Message); ok = false; }
            finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
            Log.Write("Audio: " + app.Name + " -> " + (string.IsNullOrEmpty(deviceId) ? "the default device" : deviceId) + (ok ? "" : " (not all of it)"));
            return ok;
        }

        // ---- the apps ----------------------------------------------------------------------------------------
        /// <summary>
        /// The apps with sound sessions on every active output device (expired sessions and ended processes left out): the default
        /// device's first, then the others' by device name; by app name within a device, system sounds last. Release(list) when done.
        /// </summary>
        public static List<AudioApp> Apps()
        {
            var byKey = new Dictionary<string, AudioApp>();
            var order = new List<AudioApp>();
            IMMDeviceEnumerator en = null; IMMDeviceCollection col = null; IMMDevice def = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                string defId = "";
                if (en.GetDefaultAudioEndpoint(ERender, EMultimedia, out def) == 0 && def != null) def.GetId(out defId);
                if (en.EnumAudioEndpoints(ERender, 1 /* active */, out col) != 0 || col == null) return order;
                int n;
                col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d = null;
                    try
                    {
                        if (col.Item(i, out d) != 0 || d == null) continue;
                        string id;
                        d.GetId(out id);
                        AddSessions(d, id ?? "", FriendlyName(d, id), id == defId, byKey, order);
                    }
                    catch (Exception ex) { Log.Write("Audio device sessions: " + ex.Message); }
                    finally { if (d != null) Marshal.ReleaseComObject(d); }
                }
            }
            catch (Exception ex) { Log.Write("Audio sessions: " + ex.Message); }
            finally
            {
                if (def != null) Marshal.ReleaseComObject(def);
                if (col != null) Marshal.ReleaseComObject(col);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            order.Sort(delegate (AudioApp a, AudioApp b)
            {
                if (a.OnDefault != b.OnDefault) return a.OnDefault ? -1 : 1;
                int c = string.Compare(a.DeviceName, b.DeviceName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                if (a.IsSystem != b.IsSystem) return a.IsSystem ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return order;
        }

        /// <summary>One device's sessions, grouped by program into byKey / order.</summary>
        static void AddSessions(IMMDevice d, string devId, string devName, bool isDefault, Dictionary<string, AudioApp> byKey, List<AudioApp> order)
        {
            IAudioSessionManager2 mgr = null; IAudioSessionEnumerator en = null;
            try
            {
                Guid iid = IidSessionManager2;
                object o;
                if (d.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) != 0) return;
                mgr = o as IAudioSessionManager2;
                if (mgr == null || mgr.GetSessionEnumerator(out en) != 0 || en == null) return;
                int n;
                en.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s = null;
                    bool kept = false;
                    try
                    {
                        if (en.GetSession(i, out s) != 0 || s == null) continue;
                        int state;
                        if (s.GetState(out state) != 0 || state == SessionExpired) continue;
                        bool system = s.IsSystemSoundsSession() == 0;
                        uint pid = 0;
                        s.GetProcessId(out pid);
                        string path = system || pid == 0 ? "" : Native.GetProcessPath(pid) ?? "";
                        if (!system && path.Length == 0 && !Alive(pid)) continue;      // the program has ended
                        string key = devId + "|" + (system ? "system" : path.Length > 0 ? path.ToLowerInvariant() : "pid:" + pid);
                        AudioApp app;
                        if (!byKey.TryGetValue(key, out app))
                        {
                            app = new AudioApp
                            {
                                Key = key, ExePath = path, IsSystem = system, Name = system ? "System sounds" : NameOf(s, path, pid),
                                DeviceId = devId, DeviceName = devName, OnDefault = isDefault
                            };
                            byKey[key] = app;
                            order.Add(app);
                        }
                        if (state == SessionActive) app.Active = true;
                        if (!system && pid != 0 && !app.Pids.Contains(pid)) app.Pids.Add(pid);
                        app.Sessions.Add(s);
                        kept = true;
                    }
                    catch (Exception ex) { Log.Write("Audio session: " + ex.Message); }
                    finally { if (!kept && s != null) Marshal.ReleaseComObject(s); }
                }
            }
            finally
            {
                if (en != null) Marshal.ReleaseComObject(en);
                if (mgr != null) Marshal.ReleaseComObject(mgr);
            }
        }

        /// <summary>A device's name as Windows shows it ("Speakers (Focusrite USB Audio)"), or its id.</summary>
        static string FriendlyName(IMMDevice d, string id)
        {
            IPropertyStore ps = null;
            try
            {
                if (d.OpenPropertyStore(0 /* STGM_READ */, out ps) != 0 || ps == null) return id;
                PROPERTYKEY key = KeyFriendlyName;
                PROPVARIANT pv;
                if (ps.GetValue(ref key, out pv) != 0) return id;
                string name = pv.vt == 31 && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : id;
                PropVariantClear(ref pv);
                return name;
            }
            catch { return id; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        /// <summary>Let go of the apps' COM objects.</summary>
        public static void Release(List<AudioApp> apps)
        {
            if (apps == null) return;
            foreach (AudioApp a in apps)
            {
                foreach (object s in a.Sessions) { try { Marshal.ReleaseComObject(s); } catch { } }
                a.Sessions.Clear();
            }
        }

        static bool Alive(uint pid)
        {
            try { using (Process p = Process.GetProcessById((int)pid)) return !p.HasExited; }
            catch { return false; }
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The program's description ("Google Chrome"), else the session's own name, else the file name.</summary>
        static string NameOf(IAudioSessionControl2 s, string path, uint pid)
        {
            if (path.Length > 0)
            {
                string cached;
                if (Names.TryGetValue(path, out cached)) return cached;
                string name = "";
                try { name = (FileVersionInfo.GetVersionInfo(path).FileDescription ?? "").Trim(); } catch { }
                if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
                Names[path] = name;
                return name;
            }
            string display;
            if (s.GetDisplayName(out display) == 0 && !string.IsNullOrWhiteSpace(display) && !display.StartsWith("@")) return display;
            try { using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName; } catch { }
            return "App " + pid;
        }

        // ---- an app's volume, mute and level -------------------------------------------------------------------
        /// <summary>The app's volume 0..1 (its first session's - they're kept together) and whether it's muted.</summary>
        public static bool TryGet(AudioApp app, out float volume, out bool muted)
        {
            volume = 0; muted = false;
            foreach (object s in app.Sessions)
            {
                var v = s as ISimpleAudioVolume;
                try
                {
                    if (v != null && v.GetMasterVolume(out volume) == 0) { v.GetMute(out muted); return true; }
                }
                catch { }
            }
            return false;
        }

        public static void SetVolume(AudioApp app, float volume)
        {
            volume = Math.Max(0f, Math.Min(1f, volume));
            foreach (object s in app.Sessions)
            {
                var v = s as ISimpleAudioVolume;
                try
                {
                    if (v == null) continue;
                    v.SetMasterVolume(volume, ref _ctx);
                    if (volume > 0f) v.SetMute(false, ref _ctx);
                }
                catch (Exception ex) { Log.Write("Audio app volume: " + ex.Message); }
            }
        }

        public static void SetMute(AudioApp app, bool mute)
        {
            foreach (object s in app.Sessions)
            {
                var v = s as ISimpleAudioVolume;
                try { if (v != null) v.SetMute(mute, ref _ctx); }
                catch (Exception ex) { Log.Write("Audio app mute: " + ex.Message); }
            }
        }

        /// <summary>The app's current level 0..1 (the loudest of its sessions, after its own volume).</summary>
        public static float Peak(AudioApp app)
        {
            float max = 0;
            foreach (object s in app.Sessions)
            {
                var m = s as IAudioMeterInformation;
                float p;
                try { if (m != null && m.GetPeakValue(out p) == 0 && p > max) max = p; }
                catch { }
            }
            return max;
        }
    }
}
