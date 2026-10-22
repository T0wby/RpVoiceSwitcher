using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace RpVoiceSwitcher;

/// <summary>
/// "Anwendungen haben alleinige Kontrolle über dieses Gerät" (Exklusivmodus).
/// Öffnet ein Spiel oder Voice-Client das Kabel exklusiv, umgeht Windows Equalizer APO – der Stimmeffekt wäre weg.
/// Deshalb soll der Exklusivmodus an "CABLE Output" aus sein.
/// </summary>
public static class ExclusiveMode
{
    private static readonly Guid FormatId = new("b3f8fa53-0004-438e-9003-51a46e139bfc");
    private static readonly PropertyKey AllowKey = new(FormatId, 3);     // Anwendungen dürfen exklusiv übernehmen
    private static readonly PropertyKey PriorityKey = new(FormatId, 4);  // Exklusive Anwendungen haben Vorrang
    private const short VT_UI4 = 19;
    public const string Argument = "--disable-exclusive";

    public sealed record Info(string DeviceId, string Name, bool Allowed);

    /// <summary>Zustand am Aufnahmegerät, dessen Name <paramref name="deviceName"/> enthält (null = Gerät nicht gefunden).</summary>
    public static Info? Probe(string deviceName)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                   .FirstOrDefault(d => d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase));
            if (device == null) return null;
            // Fehlt der Eintrag, gilt der Windows-Standard: erlaubt.
            bool allowed = !device.Properties.Contains(AllowKey) || Convert.ToInt64(device.Properties[AllowKey].Value) != 0;
            return new Info(device.ID, device.FriendlyName, allowed);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Schaltet den Exklusivmodus ab. Braucht Administratorrechte – läuft im eigenen, erhöhten Prozess.</summary>
    public static int DisableElevatedWorker(string deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            device.GetPropertyInformation(StorageAccessMode.ReadWrite);
            device.Properties.SetValue(AllowKey, new PropVariant { vt = VT_UI4, ulVal = 0 });
            device.Properties.SetValue(PriorityKey, new PropVariant { vt = VT_UI4, ulVal = 0 });
            device.Properties.Commit();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>Startet die App kurz mit Administratorrechten, um den Exklusivmodus abzuschalten. Liefert eine Fehlermeldung oder null.</summary>
    public static string? Disable(string deviceId)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{Argument} \"{deviceId}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (p == null) return "Konnte nicht gestartet werden.";
            p.WaitForExit(20000);
            return p.ExitCode == 0 ? null : "Windows hat die Änderung abgelehnt.";
        }
        catch (Win32Exception)
        {
            return "Die Windows-Abfrage wurde abgebrochen – einfach nochmal versuchen und mit „Ja“ bestätigen.";
        }
    }
}
