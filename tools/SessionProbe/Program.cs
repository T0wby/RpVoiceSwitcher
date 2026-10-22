// Zeigt, welche Programme gerade von welchem Aufnahmegerät aufnehmen (Audio-Sitzungen), plus Standardgeräte.
// "SessionProbe watch 120" beobachtet 120 s lang und meldet jede Änderung (wer nimmt gerade aktiv auf?).
using System.Diagnostics;
using NAudio.CoreAudioApi;

using var en = new MMDeviceEnumerator();
foreach (Role role in new[] { Role.Console, Role.Communications })
{
    try { Console.WriteLine($"Standard-Aufnahme ({role}): {en.GetDefaultAudioEndpoint(DataFlow.Capture, role).FriendlyName}"); }
    catch { Console.WriteLine($"Standard-Aufnahme ({role}): -"); }
}

static string Name(uint pid) { try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; } }

List<string> Snapshot(bool activeOnly)
{
    var lines = new List<string>();
    foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
    {
        var sessions = dev.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.GetProcessID == 0) continue;
            if (activeOnly && s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
            lines.Add($"{dev.FriendlyName,-45} <- {Name(s.GetProcessID)} ({s.GetProcessID}) {(activeOnly ? "" : s.State.ToString())}");
        }
    }
    return lines;
}

if (args.Length > 0 && args[0] == "watch")
{
    int seconds = args.Length > 1 ? int.Parse(args[1]) : 60;
    var sw = Stopwatch.StartNew();
    var last = new HashSet<string>();
    Console.WriteLine("Beobachte aktive Aufnahmen ...");
    while (sw.Elapsed.TotalSeconds < seconds)
    {
        var now = Snapshot(true).ToHashSet();
        foreach (var l in now.Except(last)) Console.WriteLine($"{sw.Elapsed.TotalSeconds,5:F1}s  + {l}");
        foreach (var l in last.Except(now)) Console.WriteLine($"{sw.Elapsed.TotalSeconds,5:F1}s  - {l}");
        last = now;
        Thread.Sleep(500);
    }
    return;
}

foreach (var l in Snapshot(false)) Console.WriteLine(l);
