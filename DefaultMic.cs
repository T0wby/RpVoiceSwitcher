using System;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace RpVoiceSwitcher;

/// <summary>
/// Windows-Standardmikrofon. Viele Programme fragen gar nicht nach einem Gerät, sondern nehmen einfach den Standard –
/// z. B. der Steam-Sprachchat und damit nanos world. Steht dort das echte Mikrofon, hören die anderen dich ohne Effekt.
/// Windows hat zwei Standards: "Standardgerät" (Konsole/Multimedia) und "Standardkommunikationsgerät".
/// </summary>
public static class DefaultMic
{
    public sealed record State(string? DefaultName, bool DefaultIsCable, string? CommsName, bool CommsIsCable, string? CableId, string? CableName)
    {
        public bool Ok => CableId == null || (DefaultIsCable && CommsIsCable);
    }

    public static State Read(string targetDevice = "CABLE Output")
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            var cable = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                          .FirstOrDefault(d => d.FriendlyName.Contains(targetDevice, StringComparison.OrdinalIgnoreCase));
            MMDevice? Get(Role role) { try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, role); } catch { return null; } }
            var console = Get(Role.Console);
            var comms = Get(Role.Communications);
            return new State(console?.FriendlyName, cable != null && console?.ID == cable.ID,
                             comms?.FriendlyName, cable != null && comms?.ID == cable.ID,
                             cable?.ID, cable?.FriendlyName);
        }
        catch { return new State(null, false, null, false, null, null); }
    }

    /// <summary>Kabel als Standard- und Kommunikationsgerät setzen (ohne Administratorrechte). Liefert eine Fehlermeldung oder null.</summary>
    public static string? SetCableAsDefault(string targetDevice = "CABLE Output")
    {
        var state = Read(targetDevice);
        if (state.CableId == null) return $"„{targetDevice}“ wurde nicht gefunden.";
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                int hr = policy.SetDefaultEndpoint(state.CableId, role);
                if (hr != 0) return $"Windows hat das Umstellen abgelehnt (0x{hr:X8}).";
            }
            Marshal.FinalReleaseComObject(policy);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    // Nicht offiziell dokumentiert, aber seit Windows 7 stabil – so stellen auch die Windows-Soundeinstellungen um.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultPeriod, IntPtr period, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
