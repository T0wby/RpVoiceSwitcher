using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RpVoiceSwitcher;

/// <summary>
/// Probehören: nimmt gleichzeitig das Mikrofon (ohne Effekt) und das Zielgerät (z. B. CABLE Output, mit Effekt) auf
/// und spielt beides auf dem Standard-Wiedergabegerät ab. Misst außerdem den Lautstärke-Unterschied.
/// </summary>
public sealed class VoiceTester : IDisposable
{
    /// <summary>Restpuffer des Kabels am Anfang verwerfen (enthält sonst alten Ton).</summary>
    private static readonly TimeSpan SkipStart = TimeSpan.FromMilliseconds(300);

    public sealed record Clip(float[] Samples, int SampleRate)
    {
        public double Seconds => SampleRate > 0 ? (double)Samples.Length / SampleRate : 0;
    }

    public Clip? Dry { get; private set; }
    public Clip? Wet { get; private set; }

    /// <summary>Aktueller Pegel (0 … 1) während der Aufnahme – für die Anzeige.</summary>
    public event Action<double, double>? LevelChanged;

    private WasapiOut? _player;

    public string? MicrophoneName { get; private set; }
    public string? TargetName { get; private set; }

    /// <summary>Nimmt <paramref name="duration"/> lang auf. Wirft bei fehlenden Geräten eine verständliche Ausnahme.</summary>
    public async Task RecordAsync(string? micId, string targetDevice, TimeSpan duration, CancellationToken ct)
    {
        Stop();
        Dry = Wet = null;
        using var enumerator = new MMDeviceEnumerator();
        var captures = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

        var target = captures.FirstOrDefault(d => d.FriendlyName.Contains(targetDevice, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"Aufnahmegerät „{targetDevice}“ nicht gefunden.");
        MMDevice? mic = null;
        if (micId != null) mic = captures.FirstOrDefault(d => d.ID.Equals(micId, StringComparison.OrdinalIgnoreCase));
        mic ??= captures.FirstOrDefault(d => d.ID != target.ID && !d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase));
        if (mic == null) throw new InvalidOperationException("Kein Mikrofon gefunden. Ist es angeschlossen?");
        MicrophoneName = mic.FriendlyName;
        TargetName = target.FriendlyName;

        using var dryRec = new Recorder(mic, l => LevelChanged?.Invoke(l, double.NaN));
        using var wetRec = new Recorder(target, l => LevelChanged?.Invoke(double.NaN, l));
        dryRec.Start();
        wetRec.Start();
        try
        {
            await Task.Delay(duration + SkipStart, ct);
        }
        finally
        {
            dryRec.Stop();
            wetRec.Stop();
        }
        Dry = dryRec.ToClip(SkipStart);
        Wet = wetRec.ToClip(SkipStart);
    }

    /// <summary>Hält die Samples eines Geräts als Mono (Mitte aus L/R) fest.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly WasapiCapture _capture;
        private readonly List<float> _samples = new();
        private readonly Action<double> _level;
        private readonly object _lock = new();
        private readonly ManualResetEventSlim _stopped = new(false);

        public Recorder(MMDevice device, Action<double> level)
        {
            _level = level;
            _capture = new WasapiCapture(device, true, 50);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, _) => _stopped.Set();
        }

        public void Start() => _capture.StartRecording();

        public void Stop()
        {
            try
            {
                _capture.StopRecording();
                _stopped.Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* Gerät weg */ }
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            var f = _capture.WaveFormat;
            int ch = Math.Max(1, f.Channels);
            int bytesPerSample = f.BitsPerSample / 8;
            int frames = e.BytesRecorded / (bytesPerSample * ch);
            float peak = 0;
            lock (_lock)
            {
                for (int i = 0; i < frames; i++)
                {
                    // Mono = Mittelwert aus L/R. Beim Kabel ist das genau die "Mitte" = die Stimme (trockene Sounds liegen auf der Seite).
                    float m = ch >= 2
                        ? 0.5f * (Read(e.Buffer, i * ch * bytesPerSample, f) + Read(e.Buffer, (i * ch + 1) * bytesPerSample, f))
                        : Read(e.Buffer, i * bytesPerSample, f);
                    _samples.Add(m);
                    peak = Math.Max(peak, Math.Abs(m));
                }
            }
            _level(peak);
        }

        private static float Read(byte[] b, int offset, WaveFormat f)
        {
            // Gemeinsamer Modus liefert praktisch immer 32-Bit-Float (oft als "Extensible" gekennzeichnet).
            bool isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32);
            return f.BitsPerSample switch
            {
                32 when isFloat => BitConverter.ToSingle(b, offset),
                32 => BitConverter.ToInt32(b, offset) / 2147483648f,
                24 => ((b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16)) << 8 >> 8) / 8388608f,
                16 => BitConverter.ToInt16(b, offset) / 32768f,
                _ => 0,
            };
        }

        public Clip ToClip(TimeSpan skip)
        {
            int rate = _capture.WaveFormat.SampleRate;
            lock (_lock)
            {
                int start = Math.Min(_samples.Count, (int)(skip.TotalSeconds * rate));
                return new Clip(_samples.Skip(start).ToArray(), rate);
            }
        }

        public void Dispose()
        {
            _capture.Dispose();
            _stopped.Dispose();
        }
    }

    // ---------- Pegel ----------

    /// <summary>Mittlere Lautstärke der gesprochenen Teile in dBFS (Pausen zählen nicht mit), null = nichts gehört.</summary>
    public static double? SpeechLevelDb(Clip clip)
    {
        int frame = Math.Max(1, clip.SampleRate / 50); // 20 ms
        var levels = new List<double>();
        for (int start = 0; start + frame <= clip.Samples.Length; start += frame)
        {
            double sum = 0;
            for (int i = start; i < start + frame; i++) sum += clip.Samples[i] * clip.Samples[i];
            double rms = Math.Sqrt(sum / frame);
            if (rms > 0) levels.Add(20 * Math.Log10(rms));
        }
        if (levels.Count == 0) return null;
        // Gesprochen = lauter als 25 dB unter dem lautesten Abschnitt und über dem Grundrauschen.
        double loudest = levels.Max();
        var speech = levels.Where(l => l > loudest - 25 && l > -55).ToList();
        if (speech.Count < 5) return null; // weniger als 0,1 s – da wurde nicht gesprochen
        double meanPower = speech.Average(l => Math.Pow(10, l / 10));
        return 10 * Math.Log10(meanPower);
    }

    // ---------- Abspielen ----------

    public enum Playback { Dry, Wet, Both }

    /// <summary>Spielt auf dem Standard-Wiedergabegerät. Gibt eine Fehlermeldung zurück oder null.</summary>
    public string? Play(Playback what, Action? finished)
    {
        Stop();
        if (Dry == null || Wet == null) return "Noch keine Aufnahme.";
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                return $"„{device.FriendlyName}“ ist das Standard-Wiedergabegerät – dann würden die anderen die Aufnahme hören. Bitte Kopfhörer als Standard wählen.";

            int rate = device.AudioClient.MixFormat.SampleRate;
            ISampleProvider Prepare(Clip c)
            {
                ISampleProvider sp = new ClipProvider(c);
                if (c.SampleRate != rate) sp = new WdlResamplingSampleProvider(sp, rate);
                return new MonoToStereoSampleProvider(sp);
            }
            var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)).ToSampleProvider().Take(TimeSpan.FromMilliseconds(600));
            ISampleProvider source = what switch
            {
                Playback.Dry => Prepare(Dry),
                Playback.Wet => Prepare(Wet),
                _ => new ConcatenatingSampleProvider([Prepare(Dry), silence, Prepare(Wet)]),
            };

            var player = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
            player.Init(source);
            player.PlaybackStopped += (_, _) => finished?.Invoke();
            player.Play();
            _player = player;
            return null;
        }
        catch (Exception ex)
        {
            return "Abspielen fehlgeschlagen: " + ex.Message;
        }
    }

    private sealed class ClipProvider(Clip clip) : ISampleProvider
    {
        private int _pos;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, 1);
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, clip.Samples.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(clip.Samples, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }

    public void Stop()
    {
        var p = Interlocked.Exchange(ref _player, null);
        if (p == null) return;
        try { p.Stop(); } catch { /* egal */ }
        p.Dispose();
    }

    public void Dispose() => Stop();
}
