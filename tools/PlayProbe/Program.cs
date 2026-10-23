// Spielt jeden Sound des Nutzers einmal stumm (Lautstaerke 0) ab und misst, wann die App ihn fuer beendet haelt:
// Kabel-Ausgang (Fortschritt) und alle Ausgaenge (IsPlaying) getrennt. Aufruf: PlayProbe [Ordner]
using System.Diagnostics;
using System.IO;
using RpVoiceSwitcher;

if (args.Length > 1 && args[0] == "read")
{
    using var r = new NAudio.Wave.AudioFileReader(args[1]);
    var buf = new float[r.WaveFormat.SampleRate * r.WaveFormat.Channels / 10];
    long total = 0; int reads = 0, n;
    var t = Stopwatch.StartNew();
    while ((n = r.Read(buf, 0, buf.Length)) > 0 && t.Elapsed.TotalSeconds < 10) { total += n; reads++; }
    Console.WriteLine($"Format {r.WaveFormat}, gelesen {total} Samples = {total / (double)(r.WaveFormat.SampleRate * r.WaveFormat.Channels):F2} s in {reads} Aufrufen, letzter Read = {n}, Position {r.Position}/{r.Length}, {t.Elapsed.TotalSeconds:F1} s");
    return;
}
string folder = args.Length > 0 ? args[0] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RP Voice Switcher", "Sounds");
using var engine = new SoundEngine();
engine.Start("CABLE Input", monitor: true, volume: 0, monitorVolume: 0);
Console.WriteLine(engine.Error ?? "Ausgaenge offen (Kabel + Mithoeren, beide stumm)");
foreach (var f in Directory.GetFiles(folder).Where(SoundLibrary.IsSupported))
{
    var sw = Stopwatch.StartNew();
    engine.Play(f, bypassEffect: true, repeat: 1);
    double progressEnd = -1, playingEnd = -1;
    TimeSpan dur = TimeSpan.Zero;
    while (sw.Elapsed.TotalSeconds < 60)
    {
        var p = engine.GetProgress();
        if (p.Count > 0) dur = p[0].FileDuration;
        if (progressEnd < 0 && p.Count == 0) progressEnd = sw.Elapsed.TotalSeconds;
        if (playingEnd < 0 && !engine.IsPlaying(f)) playingEnd = sw.Elapsed.TotalSeconds;
        if (progressEnd >= 0 && playingEnd >= 0) break;
        Thread.Sleep(50);
    }
    Console.WriteLine($"{Path.GetFileName(f),-42} Laenge {dur.TotalSeconds,6:F2} s | Kabel fertig nach {progressEnd,6:F2} s | IsPlaying aus nach {playingEnd,6:F2} s");
    engine.StopAll();
}
