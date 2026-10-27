using System;
using System.Runtime.InteropServices;

namespace DynamicIsland
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig] int GetChannelCount(out uint c);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    /// <summary>Системная громкость (устройство воспроизведения по умолчанию).</summary>
    static class Audio
    {
        static IAudioEndpointVolume ep;
        static IAudioMeterInformation meter;

        public static void Reset() { ep = null; meter = null; }

        public static float Peak()
        {
            try
            {
                if (meter == null)
                {
                    var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                    en.GetDefaultAudioEndpoint(0, 1, out var dev);
                    var iid = typeof(IAudioMeterInformation).GUID;
                    dev.Activate(ref iid, 23, IntPtr.Zero, out var o);
                    meter = (IAudioMeterInformation)o;
                }
                meter.GetPeakValue(out var p);
                return p;
            }
            catch { meter = null; return -1; }
        }

        static IAudioEndpointVolume Ep()
        {
            if (ep != null) return ep;
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            en.GetDefaultAudioEndpoint(0, 1, out var dev);
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out var o);
            return ep = (IAudioEndpointVolume)o;
        }

        public static float Get() { try { Ep().GetMasterVolumeLevelScalar(out var v); return v; } catch { ep = null; return 0.5f; } }
        public static void Set(float v) { try { var g = Guid.Empty; if (v > 0.001f) Ep().SetMute(false, ref g); Ep().SetMasterVolumeLevelScalar(Math.Clamp(v, 0f, 1f), ref g); } catch { ep = null; } }
        public static bool IsMuted() { try { Ep().GetMute(out var m); return m; } catch { ep = null; return false; } }
        public static void ToggleMute() { try { var g = Guid.Empty; Ep().SetMute(!IsMuted(), ref g); } catch { ep = null; } }
    }
}

namespace DynamicIsland
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    class StartApp { public string Name; public string Id; }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr g, uint flags, out IntPtr c);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr g, uint flags, out IntPtr v);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetSession(int i, out IAudioSessionControl c);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int s);
        [PreserveSig] int GetDisplayName(out IntPtr n);
        [PreserveSig] int SetDisplayName(IntPtr v, ref Guid ctx);
        [PreserveSig] int GetIconPath(out IntPtr p);
        [PreserveSig] int SetIconPath(IntPtr p, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int s);
        [PreserveSig] int GetDisplayName(out IntPtr n);
        [PreserveSig] int SetDisplayName(IntPtr v, ref Guid ctx);
        [PreserveSig] int GetIconPath(out IntPtr p);
        [PreserveSig] int SetIconPath(IntPtr p, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool b);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>Громкость конкретного приложения (того, чья медиасессия сейчас активна).</summary>
    static class AppVolume
    {
        static readonly List<ISimpleAudioVolume> vols = new();
        static readonly List<object> ctrls = new();
        public static bool Has => vols.Count > 0;
        static string key;
        static readonly string[] Stop = { "microsoft", "windows", "music", "media", "player", "exe", "app" };

        public static bool Ensure(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) { vols.Clear(); ctrls.Clear(); key = null; return false; }
            if (aumid != key || vols.Count == 0) Resolve(aumid);
            return vols.Count > 0;
        }

        public static bool Resolve(string aumid)
        {
            vols.Clear(); ctrls.Clear(); key = aumid;
            if (string.IsNullOrEmpty(aumid)) return false;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                en.GetDefaultAudioEndpoint(0, 1, out var dev);
                var iid = typeof(IAudioSessionManager2).GUID;
                dev.Activate(ref iid, 23, IntPtr.Zero, out var o);
                var mgr = (IAudioSessionManager2)o;
                mgr.GetSessionEnumerator(out var se);
                se.GetCount(out int n);
                int me = Environment.ProcessId;
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        se.GetSession(i, out var c);
                        var c2 = (IAudioSessionControl2)c;
                        c2.GetProcessId(out uint pid);
                        if (pid == 0 || pid == me) continue;
                        using var p = Process.GetProcessById((int)pid);
                        if (Match(aumid, p)) { vols.Add((ISimpleAudioVolume)c); ctrls.Add(c); }
                    }
                    catch { }
                }
            }
            catch { }
            return vols.Count > 0;
        }

        static bool Match(string aumid, Process p)
        {
            var a = aumid.ToLowerInvariant();
            var n = p.ProcessName.ToLowerInvariant();
            var exe = Path.GetFileNameWithoutExtension(a);
            if (n == exe || a.Contains(n) && n.Length >= 4) return true;
            string path = null;
            try { path = p.MainModule?.FileName?.ToLowerInvariant(); } catch { }
            foreach (var t in a.Split('!', '_', '.', '-', ' ', ':', '\\', '/'))
            {
                if (t.Length < 4 || Array.IndexOf(Stop, t) >= 0) continue;
                if (n.Contains(t) || (path != null && path.Contains(t))) return true;
            }
            return false;
        }

        public static float Peak()
        {
            float m = -1;
            foreach (var c in ctrls) { try { ((IAudioMeterInformation)c).GetPeakValue(out var p); if (p > m) m = p; } catch { } }
            return m;
        }

        public static float Get()
        {
            foreach (var v in vols) { try { v.GetMasterVolume(out var f); return f; } catch { } }
            return 1f;
        }

        public static void Set(float f)
        {
            f = Math.Clamp(f, 0f, 1f);
            for (int t = 0; t < 2; t++)
            {
                bool ok = true; var g = Guid.Empty;
                foreach (var v in vols)
                {
                    try { if (f > 0.001f) v.SetMute(false, ref g); v.SetMasterVolume(f, ref g); } catch { ok = false; }
                }
                if (ok) return;
                Resolve(key);
            }
        }

        public static bool IsMuted()
        {
            foreach (var v in vols) { try { v.GetMute(out var m); return m; } catch { } }
            return false;
        }

        public static void ToggleMute()
        {
            bool m = IsMuted(); var g = Guid.Empty;
            foreach (var v in vols) { try { v.SetMute(!m, ref g); } catch { } }
        }
    }
}
