using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RpVoiceSwitcher;

/// <summary>
/// Automatisches Angleichen: jeder Sound wird auf dieselbe Spitzenlautstärke gebracht (-1 dBFS),
/// damit leise Dateien im Sprachchat nicht untergehen – und laute nicht übersteuern.
/// Die Messung läuft einmal pro Datei im Hintergrund und wird zwischengespeichert.
/// </summary>
public static class SoundLevels
{
    public const float TargetPeak = 0.89f;  // -1 dBFS
    public const float MaxGain = 8f;        // höchstens +18 dB
    public const float MinGain = 0.25f;     // höchstens -12 dB

    private static readonly ConcurrentDictionary<string, (DateTime Modified, float Peak)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task> Running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Faktor für diesen Sound (1, solange noch nicht gemessen – die Messung startet dann im Hintergrund).</summary>
    public static float GainFor(string path)
    {
        var peak = PeakFor(path);
        if (peak is not float p) { Measure(path); return 1f; }
        return p <= 0.0001f ? 1f : Math.Clamp(TargetPeak / p, MinGain, MaxGain);
    }

    /// <summary>Gemessene Spitze oder null (noch nicht gemessen / Datei geändert).</summary>
    public static float? PeakFor(string path)
    {
        try
        {
            if (Cache.TryGetValue(path, out var c) && c.Modified == File.GetLastWriteTimeUtc(path)) return c.Peak;
        }
        catch { /* Datei weg */ }
        return null;
    }

    /// <summary>Misst Dateien im Hintergrund vor, damit der erste Druck schon angeglichen klingt.</summary>
    public static void Prepare(IEnumerable<string> paths)
    {
        foreach (var p in paths) if (PeakFor(p) == null) Measure(p);
    }

    private static void Measure(string path) =>
        Running.GetOrAdd(path, p => Task.Run(() =>
        {
            try
            {
                var modified = File.GetLastWriteTimeUtc(p);
                Cache[p] = (modified, SoundEngine.MeasurePeak(p));
            }
            catch { /* nicht lesbar – bleibt bei Faktor 1 */ }
            finally { Running.TryRemove(p, out _); }
        }));

    /// <summary>Für Tests: Messung abwarten.</summary>
    public static float? MeasureNow(string path)
    {
        Measure(path);
        if (Running.TryGetValue(path, out var t)) t.Wait(TimeSpan.FromSeconds(30));
        return PeakFor(path);
    }
}
