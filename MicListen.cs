using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace RpVoiceSwitcher;

/// <summary>
/// Windows-„Abhören“ (Mikrofon → CABLE Input). Das bleibt gelegentlich hängen – z. B. nach Standby, USB-Wackler oder
/// wenn eine Hersteller-App (Shure MOTIV …) das Mikrofon umstellt: Eingestellt ist alles richtig, aber im Kabel kommt
/// nichts an. Aus- und wieder Einschalten behebt das. Die laufende Weiterleitung ist in Windows eine eigene
/// Audio-Sitzung auf CABLE Input, benannt nach dem Mikrofon – daran sieht man, ob sie läuft und ob Stimme durchkommt.
/// </summary>
public static class MicListen
{
    /// <summary>Eigenschaft „Dieses Gerät als Wiedergabequelle verwenden“.</summary>
    private static readonly Guid ListenFmtid = new("24dbb0fc-9311-4b3d-9cf0-18ff155639d4");

    /// <summary>Schaltet Abhören am Gerät an/aus (wie der Haken in der Systemsteuerung). Liefert eine Fehlermeldung oder null.</summary>
    public static string? SetListen(string deviceId, bool on)
    {
        IntPtr key = IntPtr.Zero, value = IntPtr.Zero;
        DefaultMic.IPolicyConfig? policy = null;
        try
        {
            policy = (DefaultMic.IPolicyConfig)new DefaultMic.PolicyConfigClient();
            key = Marshal.AllocHGlobal(20);       // PROPERTYKEY: GUID + DWORD
            Marshal.Copy(ListenFmtid.ToByteArray(), 0, key, 16);
            Marshal.WriteInt32(key, 16, 1);
            value = Marshal.AllocHGlobal(24);     // PROPVARIANT (64 Bit)
            for (int i = 0; i < 24; i++) Marshal.WriteByte(value, i, 0);
            Marshal.WriteInt16(value, 0, 11);     // VT_BOOL
            Marshal.WriteInt16(value, 8, (short)(on ? -1 : 0));
            int hr = policy.SetPropertyValue(deviceId, 0, key, value);
            return hr == 0 ? null : $"Windows hat das Umschalten abgelehnt (0x{hr:X8}).";
        }
        catch (Exception ex) { return ex.Message; }
        finally
        {
            if (key != IntPtr.Zero) Marshal.FreeHGlobal(key);
            if (value != IntPtr.Zero) Marshal.FreeHGlobal(value);
            if (policy != null) Marshal.FinalReleaseComObject(policy);
        }
    }

    /// <summary>Abhören einmal aus- und wieder einschalten. Liefert eine Fehlermeldung oder null.</summary>
    public static string? Restart(string deviceId)
    {
        if (SetListen(deviceId, false) is string offError) return offError;
        Thread.Sleep(300);
        return SetListen(deviceId, true);
    }

    /// <summary>Die Weiterleitungs-Sitzung auf CABLE Input (gehört dem Windows-Audiodienst, heißt wie das Mikrofon).</summary>
    public static AudioSessionControl? FindSession(MMDevice cableInput, string micName)
    {
        var manager = cableInput.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (!s.IsSystemSoundsSession && string.Equals(s.DisplayName, micName, StringComparison.OrdinalIgnoreCase)) return s;
        }
        return null;
    }

    /// <summary>Für die Diagnose: Läuft die Weiterleitung gerade?</summary>
    public static string Describe()
    {
        if (SetupChecker.FindMicrophoneId() is not string micId) return "nicht eingerichtet";
        using var en = new MMDeviceEnumerator();
        var mic = en.GetDevice(micId);
        var cable = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                      .FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (cable == null) return $"{mic.FriendlyName} → CABLE Input nicht gefunden";
        var session = FindSession(cable, mic.FriendlyName);
        string state = session == null ? "läuft NICHT (keine Sitzung auf CABLE Input)"
            : session.State == AudioSessionState.AudioSessionStateActive ? "läuft" : $"läuft NICHT ({session.State})";
        return $"{mic.FriendlyName} → {cable.FriendlyName}: {state}";
    }

    public static string ReadRecentLog(int lines = 10)
    {
        try { return File.Exists(LogFile) ? string.Join(Environment.NewLine, File.ReadAllLines(LogFile).TakeLast(lines)) : "(keine Einträge)"; }
        catch (Exception ex) { return "(nicht lesbar: " + ex.Message + ")"; }
    }

    // ---------- Protokoll (für die Fehlersuche, nur die letzten Einträge) ----------

    public static string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RpVoiceSwitcher",
        AppSettings.OverridePath != null ? "abhoeren-test.log" : "abhoeren.log");

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 32 * 1024)
                File.WriteAllLines(LogFile, File.ReadAllLines(LogFile).TakeLast(100));
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {text}{Environment.NewLine}");
        }
        catch { /* Protokoll ist nur eine Hilfe */ }
    }
}
