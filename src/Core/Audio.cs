using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace vShell.Core
{
    public sealed class AudioDevice
    {
        public string Id = "";
        public string Name = "";
        public bool IsDefault;
    }

    /// <summary>
    /// Master volume / mute of the default speakers, and the list of playback devices, through the Windows Core Audio COM API
    /// (per-app volume is in AudioSessions.cs). Call from the UI thread. Every call is wrapped: if audio is unavailable the methods
    /// just report failure.
    /// </summary>
    public static partial class Audio
    {
        // ---- COM interop ----------------------------------------------------------------------------------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore props);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPVARIANT { public ushort vt, w1, w2, w3; public IntPtr p; public IntPtr p2; }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // Undocumented but long-stable; only SetDefaultEndpoint is used, the earlier slots just keep the vtable layout right.
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        class PolicyConfigClient { }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int Slot1();
            [PreserveSig] int Slot2();
            [PreserveSig] int Slot3();
            [PreserveSig] int Slot4();
            [PreserveSig] int Slot5();
            [PreserveSig] int Slot6();
            [PreserveSig] int Slot7();
            [PreserveSig] int Slot8();
            [PreserveSig] int Slot9();
            [PreserveSig] int Slot10();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pv);

        static readonly Guid IidEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static readonly PROPERTYKEY KeyFriendlyName = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
        const int CLSCTX_ALL = 23;
        const int ERender = 0, EMultimedia = 1;

        // ---- state ---------------------------------------------------------------------------------------
        /// <summary>A device's volume control and level meter, kept for 10 seconds ("" = the default device, which can change).</summary>
        sealed class Endpoints
        {
            public IAudioEndpointVolume Vol;
            public IAudioMeterInformation Meter;
            public DateTime At;
        }

        static readonly Dictionary<string, Endpoints> _eps = new Dictionary<string, Endpoints>();
        static Guid _ctx = Guid.Empty;

        /// <summary>A device: the default playback device for "" / null, else the one with this id (null when it isn't there).</summary>
        static IMMDevice Device(IMMDeviceEnumerator en, string id)
        {
            IMMDevice dev;
            int hr = string.IsNullOrEmpty(id) ? en.GetDefaultAudioEndpoint(ERender, EMultimedia, out dev) : en.GetDevice(id, out dev);
            if (hr != 0 || dev == null) return null;
            int state;
            if (!string.IsNullOrEmpty(id) && (dev.GetState(out state) != 0 || state != 1 /* active */)) { Marshal.ReleaseComObject(dev); return null; }
            return dev;
        }

        static Endpoints Endpoint(string id)
        {
            id = id ?? "";
            Endpoints e;
            if (_eps.TryGetValue(id, out e) && e.Vol != null && (DateTime.UtcNow - e.At).TotalSeconds < 10) return e;
            Release(id);
            IMMDeviceEnumerator en = null; IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                dev = Device(en, id);
                if (dev == null) return null;
                Guid iid = IidEndpointVolume;
                object o;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) != 0 || o == null) return null;
                e = new Endpoints { Vol = (IAudioEndpointVolume)o, At = DateTime.UtcNow };
                iid = IidMeter;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) == 0) e.Meter = o as IAudioMeterInformation;
                _eps[id] = e;
                return e;
            }
            catch (Exception ex) { Log.Write("Audio endpoint: " + ex.Message); return null; }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        static void Release(string id)
        {
            Endpoints e;
            if (!_eps.TryGetValue(id ?? "", out e)) return;
            if (e.Vol != null) { try { Marshal.ReleaseComObject(e.Vol); } catch { } }
            if (e.Meter != null) { try { Marshal.ReleaseComObject(e.Meter); } catch { } }
            _eps.Remove(id ?? "");
        }

        static void Release()
        {
            foreach (string id in new List<string>(_eps.Keys)) Release(id);
        }

        // ---- volume ---------------------------------------------------------------------------------------
        /// <summary>Master volume 0..1 and the mute state of the default playback device.</summary>
        public static bool TryGet(out float volume, out bool muted) { return TryGet("", out volume, out muted); }
        public static void SetVolume(float volume) { SetVolume("", volume); }
        public static void SetMute(bool mute) { SetMute("", mute); }

        /// <summary>A device's volume 0..1 and mute ("" = the default device). False when it isn't connected.</summary>
        public static bool TryGet(string deviceId, out float volume, out bool muted)
        {
            volume = 0; muted = false;
            try
            {
                Endpoints e = Endpoint(deviceId);
                if (e == null) return false;
                if (e.Vol.GetMasterVolumeLevelScalar(out volume) != 0) { Release(deviceId); return false; }
                e.Vol.GetMute(out muted);
                return true;
            }
            catch (Exception ex) { Release(deviceId); Log.Write("Audio get: " + ex.Message); return false; }
        }

        public static void SetVolume(string deviceId, float volume)
        {
            try
            {
                Endpoints e = Endpoint(deviceId);
                if (e == null) return;
                volume = Math.Max(0f, Math.Min(1f, volume));
                e.Vol.SetMasterVolumeLevelScalar(volume, ref _ctx);
                if (volume > 0f) e.Vol.SetMute(false, ref _ctx);
            }
            catch (Exception ex) { Release(deviceId); Log.Write("Audio set: " + ex.Message); }
        }

        public static void SetMute(string deviceId, bool mute)
        {
            try
            {
                Endpoints e = Endpoint(deviceId);
                if (e != null) e.Vol.SetMute(mute, ref _ctx);
            }
            catch (Exception ex) { Release(deviceId); Log.Write("Audio mute: " + ex.Message); }
        }

        /// <summary>A device's current level 0..1 ("" = the default device), or 0.</summary>
        public static float Peak(string deviceId)
        {
            try
            {
                Endpoints e = Endpoint(deviceId);
                float p;
                return e != null && e.Meter != null && e.Meter.GetPeakValue(out p) == 0 ? p : 0f;
            }
            catch { Release(deviceId); return 0f; }
        }

        static readonly Dictionary<string, KeyValuePair<DateTime, string>> _names = new Dictionary<string, KeyValuePair<DateTime, string>>();

        /// <summary>A device's name ("" = the default device's), or "" when it isn't connected. Looked up at most every 3 seconds.</summary>
        public static string DeviceName(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return DefaultDeviceName();
            KeyValuePair<DateTime, string> c;
            if (_names.TryGetValue(deviceId, out c) && (DateTime.UtcNow - c.Key).TotalSeconds < 3) return c.Value;
            string name = LookUpName(deviceId);
            _names[deviceId] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, name);
            return name;
        }

        static string LookUpName(string deviceId)
        {
            IMMDeviceEnumerator en = null; IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                dev = Device(en, deviceId);
                return dev == null ? "" : FriendlyName(dev, deviceId);
            }
            catch { return ""; }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        /// <summary>The default playback device's id ("" when there is none).</summary>
        public static string DefaultDeviceId()
        {
            IMMDeviceEnumerator en = null; IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                string id;
                return en.GetDefaultAudioEndpoint(ERender, EMultimedia, out dev) == 0 && dev != null && dev.GetId(out id) == 0 ? id ?? "" : "";
            }
            catch { return ""; }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        // ---- devices --------------------------------------------------------------------------------------
        public static List<AudioDevice> Devices()
        {
            var list = new List<AudioDevice>();
            IMMDeviceEnumerator en = null; IMMDeviceCollection col = null; IMMDevice def = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                string defId = "";
                if (en.GetDefaultAudioEndpoint(ERender, EMultimedia, out def) == 0 && def != null) def.GetId(out defId);

                if (en.EnumAudioEndpoints(ERender, 1 /* active */, out col) != 0 || col == null) return list;
                int n;
                col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d = null; IPropertyStore ps = null;
                    try
                    {
                        if (col.Item(i, out d) != 0 || d == null) continue;
                        string id;
                        d.GetId(out id);
                        string name = id;
                        if (d.OpenPropertyStore(0 /* STGM_READ */, out ps) == 0 && ps != null)
                        {
                            PROPERTYKEY key = KeyFriendlyName;
                            PROPVARIANT pv;
                            if (ps.GetValue(ref key, out pv) == 0)
                            {
                                if (pv.vt == 31 && pv.p != IntPtr.Zero) name = Marshal.PtrToStringUni(pv.p);
                                PropVariantClear(ref pv);
                            }
                        }
                        list.Add(new AudioDevice { Id = id, Name = name, IsDefault = id == defId });
                    }
                    finally
                    {
                        if (ps != null) Marshal.ReleaseComObject(ps);
                        if (d != null) Marshal.ReleaseComObject(d);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Audio devices: " + ex.Message); }
            finally
            {
                if (def != null) Marshal.ReleaseComObject(def);
                if (col != null) Marshal.ReleaseComObject(col);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return list;
        }

        static string _defName = "";
        static DateTime _defNameAt = DateTime.MinValue;

        /// <summary>The default playback device's name ("" when there is none). Looked up at most every 3 seconds.</summary>
        public static string DefaultDeviceName()
        {
            if ((DateTime.UtcNow - _defNameAt).TotalSeconds < 3) return _defName;
            _defNameAt = DateTime.UtcNow;
            IMMDeviceEnumerator en = null; IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                string id;
                if (en.GetDefaultAudioEndpoint(ERender, EMultimedia, out dev) != 0 || dev == null || dev.GetId(out id) != 0) { _defName = ""; return ""; }
                _defName = FriendlyName(dev, id);
            }
            catch { _defName = ""; }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return _defName;
        }

        /// <summary>A device name's first part: "Speakers" from "Speakers (Focusrite USB Audio)", "System" from "System (Elgato Virtual Audio)".</summary>
        public static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? name.Substring(0, p) : name;
        }

        /// <summary>Make a playback device the default for all roles.</summary>
        public static bool SetDefault(string deviceId)
        {
            IPolicyConfig pc = null;
            try
            {
                pc = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(deviceId, role);
                Release();      // the volume now belongs to a different device
                _defNameAt = DateTime.MinValue;
                return true;
            }
            catch (Exception ex) { Log.Write("Audio set default: " + ex.Message); return false; }
            finally { if (pc != null) Marshal.ReleaseComObject(pc); }
        }
    }
}
