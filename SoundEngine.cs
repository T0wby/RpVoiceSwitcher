using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RpVoiceSwitcher;

/// <summary>
/// Spielt Sounds über einen dauerhaft offenen Mixer ab: einmal ins virtuelle Kabel (hören die anderen)
/// und optional zusätzlich auf das Standard-Ausgabegerät (zum Mithören).
/// </summary>
public sealed class SoundEngine : IDisposable
{
    private sealed class Output : IDisposable
    {
        public required WasapiOut Device { get; init; }
        public required MixingSampleProvider Mixer { get; init; }
        public required VolumeSampleProvider Master { get; init; }
        public required string DeviceId { get; init; }
        public void Dispose() { try { Device.Stop(); } catch { } Device.Dispose(); }
    }

    /// <summary>Gibt die Datei frei, sobald der Sound zu Ende ist (oder gestoppt wird).</summary>
    private sealed class AutoDisposeProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private IDisposable? _owner;
        public AutoDisposeProvider(ISampleProvider source, IDisposable owner, string key, TimeSpan fileDuration, int repeat, bool primary)
        {
            _source = source; _owner = owner; Key = key;
            FileDuration = fileDuration; Repeat = repeat; IsPrimary = primary;
        }
        public string Key { get; }
        public TimeSpan FileDuration { get; }
        public int Repeat { get; }
        /// <summary>Der Ausgang ins Kabel – nur dessen Fortschritt wird angezeigt (Mithören läuft parallel).</summary>
        public bool IsPrimary { get; }
        private long _samplesRead;
        /// <summary>Wie lange der Sound schon läuft (an den tatsächlich gelieferten Samples gemessen, ohne die Datei anzufassen).</summary>
        public TimeSpan Elapsed => TimeSpan.FromSeconds((double)System.Threading.Interlocked.Read(ref _samplesRead)
                                                         / Math.Max(1, _source.WaveFormat.SampleRate * _source.WaveFormat.Channels));
        public WaveFormat WaveFormat => _source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            if (_owner == null) return 0;
            // Stopp wird hier auf dem Audio-Thread umgesetzt – so wird die Datei nie mitten im Lesen geschlossen.
            if (StopRequested) { Close(); return 0; }
            int n = _source.Read(buffer, offset, count);
            System.Threading.Interlocked.Add(ref _samplesRead, n);
            if (n == 0) Close();
            return n;
        }
        private volatile bool _stopRequested;
        public bool StopRequested => _stopRequested;
        public void RequestStop() => _stopRequested = true;
        public bool IsDone => System.Threading.Volatile.Read(ref _owner) == null;
        public void Close() => System.Threading.Interlocked.Exchange(ref _owner, null)?.Dispose();
    }

    private Output? _remote;
    private Output? _monitor;
    private readonly List<AutoDisposeProvider> _playing = new();
    private readonly object _lock = new();

    public string? Error { get; private set; }
    public string? RemoteDeviceName { get; private set; }

    /// <summary>(Neu) starten – z. B. nach Änderung des Geräts oder des Mithörens.</summary>
    public void Start(string remoteDeviceName, bool monitor, double volume, double monitorVolume)
    {
        Stop();
        Error = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            var remote = devices.FirstOrDefault(d => d.FriendlyName.Contains(remoteDeviceName, StringComparison.OrdinalIgnoreCase));
            if (remote == null)
            {
                Error = $"Ausgabegerät \"{remoteDeviceName}\" nicht gefunden – ist VB-CABLE installiert?";
                return;
            }
            RemoteDeviceName = remote.FriendlyName;
            _remote = CreateOutput(remote, volume);

            if (monitor)
            {
                var standard = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                // Wenn das Kabel selbst Standardgerät ist, würde Mithören den Sound doppelt ins Kabel schicken.
                if (standard != null && standard.ID != remote.ID)
                    _monitor = CreateOutput(standard, monitorVolume);
            }
        }
        catch (Exception ex)
        {
            Error = "Soundausgabe konnte nicht gestartet werden: " + ex.Message;
            Stop();
        }
    }

    private static Output CreateOutput(MMDevice device, double volume)
    {
        int rate = device.AudioClient.MixFormat.SampleRate;
        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = true };
        var master = new VolumeSampleProvider(mixer) { Volume = (float)volume };
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
        output.Init(new SoftLimiter(master)); // Lautstärke über 100 % ohne hartes Übersteuern
        output.Play();
        return new Output { Device = output, Mixer = mixer, Master = master, DeviceId = device.ID };
    }

    /// <summary>
    /// Weicher Begrenzer: unter 0,8 unverändert, darüber sanft gegen 1,0 gedrückt statt hart abgeschnitten.
    /// Symmetrisch – die Gegenphase für Sounds ohne Stimmeffekt (L = +x, R = -x) bleibt erhalten.
    /// </summary>
    private sealed class SoftLimiter(ISampleProvider source) : ISampleProvider
    {
        private const float Knee = 0.8f, Room = 1f - Knee;
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count);
            for (int i = offset; i < offset + n; i++)
            {
                float x = buffer[i], a = Math.Abs(x);
                if (a > Knee) buffer[i] = Math.Sign(x) * (Knee + Room * MathF.Tanh((a - Knee) / Room));
            }
            return n;
        }
    }

    public void SetVolume(double volume, double monitorVolume)
    {
        if (_remote != null) _remote.Master.Volume = (float)volume;
        if (_monitor != null) _monitor.Master.Volume = (float)monitorVolume;
    }

    /// <summary>
    /// Für die anderen wird der Sound mono gemacht: mit Stimmeffekt gleichphasig auf L/R ("Mitte" – läuft durch den Effekt),
    /// ohne Stimmeffekt gegenphasig (L = +x, R = -x, "Seite" – voice.txt leitet sie am Effekt vorbei).
    /// </summary>
    private sealed class MidSideProvider(ISampleProvider source, bool bypassEffect) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count);
            for (int i = offset; i + 1 < offset + n; i += 2)
            {
                float m = 0.5f * (buffer[i] + buffer[i + 1]);
                buffer[i] = m;
                buffer[i + 1] = bypassEffect ? -m : m;
            }
            return n;
        }
    }

    /// <summary>
    /// Spielt eine Datei <paramref name="repeat"/>-mal hintereinander (0 = endlos, bis <see cref="Stop"/>).
    /// Wiederholt wird direkt an der Datei, deshalb ohne Lücke.
    /// </summary>
    private sealed class LoopProvider(AudioFileReader reader, int repeat) : ISampleProvider
    {
        private int _played = 1;
        public WaveFormat WaveFormat => reader.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0, emptyRewinds = 0;
            while (total < count)
            {
                int n = reader.Read(buffer, offset + total, count - total);
                if (n > 0) { total += n; emptyRewinds = 0; continue; }
                if (repeat > 0 && _played >= repeat) break;      // genug Durchläufe
                if (reader.Length == 0 || ++emptyRewinds > 1) break; // leere/kaputte Datei: nicht endlos drehen
                reader.Position = 0;
                _played++;
            }
            return total;
        }
    }

    /// <param name="gain">Lautstärke-Faktor nur für diesen Sound (z. B. aus dem automatischen Angleichen).</param>
    public bool Play(string path, bool bypassEffect, int repeat = 1, float gain = 1f)
    {
        if (_remote == null) return false;
        try
        {
            AddTo(_remote, path, repeat, sp => new MidSideProvider(Gain(sp, gain), bypassEffect), primary: true);
            if (_monitor != null) AddTo(_monitor, path, repeat, sp => Gain(sp, gain), primary: false);
            return true;
        }
        catch (Exception ex)
        {
            Error = $"\"{System.IO.Path.GetFileName(path)}\" konnte nicht abgespielt werden: {ex.Message}";
            return false;
        }
    }

    private static ISampleProvider Gain(ISampleProvider sp, float gain) =>
        Math.Abs(gain - 1f) < 0.01f ? sp : new VolumeSampleProvider(sp) { Volume = gain };

    /// <summary>Lautester Wert einer Datei (0 … 1+), für das automatische Angleichen. Liest höchstens 10 Minuten.</summary>
    public static float MeasurePeak(string path)
    {
        using var reader = new AudioFileReader(path);
        var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        long max = (long)reader.WaveFormat.SampleRate * reader.WaveFormat.Channels * 600;
        long done = 0;
        float peak = 0;
        int n;
        while (done < max && (n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
            done += n;
        }
        return peak;
    }

    private void AddTo(Output output, string path, int repeat, Func<ISampleProvider, ISampleProvider>? transform, bool primary)
    {
        var reader = new AudioFileReader(path);
        ISampleProvider sp = repeat == 1 ? reader : new LoopProvider(reader, repeat);
        if (sp.WaveFormat.Channels == 1)
            sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2)
        {
            var mux = new MultiplexingSampleProvider(new[] { sp }, 2);
            mux.ConnectInputToOutput(0, 0);
            mux.ConnectInputToOutput(1, 1);
            sp = mux;
        }
        if (sp.WaveFormat.SampleRate != output.Mixer.WaveFormat.SampleRate)
            sp = new WdlResamplingSampleProvider(sp, output.Mixer.WaveFormat.SampleRate);
        if (transform != null) sp = transform(sp);

        var tracked = new AutoDisposeProvider(sp, reader, path, reader.TotalTime, repeat, primary);
        lock (_lock)
        {
            _playing.RemoveAll(p => p.IsDone); // fertige Sounds aus der Liste nehmen
            _playing.Add(tracked);
        }
        output.Mixer.AddMixerInput(tracked);
    }

    /// <summary>Fortschritt eines laufenden Sounds.</summary>
    public sealed record Progress(string Path, TimeSpan Elapsed, TimeSpan FileDuration, int Repeat)
    {
        /// <summary>Durchgang (1, 2, …) bei Wiederholung.</summary>
        public int Pass => FileDuration > TimeSpan.Zero ? (int)(Elapsed.Ticks / FileDuration.Ticks) + 1 : 1;

        /// <summary>Position im aktuellen Durchgang.</summary>
        public TimeSpan InPass => FileDuration > TimeSpan.Zero ? TimeSpan.FromTicks(Elapsed.Ticks % FileDuration.Ticks) : Elapsed;

        /// <summary>0 … 1 über alle Durchgänge; bei Endlos der aktuelle Durchgang.</summary>
        public double Fraction => FileDuration <= TimeSpan.Zero ? 0
            : Repeat == 0 ? InPass / FileDuration
            : Math.Clamp(Elapsed / (FileDuration * Repeat), 0, 1);
    }

    /// <summary>Alle gerade laufenden Sounds mit Fortschritt (ein Eintrag je Datei).</summary>
    public List<Progress> GetProgress()
    {
        lock (_lock)
        {
            _playing.RemoveAll(p => p.IsDone);
            return _playing.Where(p => p.IsPrimary && !p.StopRequested)
                           .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                           .Select(g => g.OrderBy(p => p.Elapsed).First())
                           .Select(p => new Progress(p.Key, p.Elapsed, p.FileDuration, p.Repeat))
                           .ToList();
        }
    }

    /// <summary>Läuft diese Datei gerade (auf irgendeinem Ausgang)?</summary>
    public bool IsPlaying(string path)
    {
        lock (_lock)
        {
            _playing.RemoveAll(p => p.IsDone);
            return _playing.Any(p => !p.StopRequested && p.Key.Equals(path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool AnyPlaying
    {
        get { lock (_lock) { _playing.RemoveAll(p => p.IsDone); return _playing.Any(p => !p.StopRequested); } }
    }

    /// <summary>Stoppt nur diesen Sound (alle laufenden Durchgänge davon).</summary>
    public void Stop(string path)
    {
        lock (_lock)
            foreach (var p in _playing.Where(p => p.Key.Equals(path, StringComparison.OrdinalIgnoreCase)))
                p.RequestStop(); // liefert beim nächsten Lesen nichts mehr – der Mixer wirft ihn dann selbst raus
    }

    public void StopAll()
    {
        _remote?.Mixer.RemoveAllMixerInputs();
        _monitor?.Mixer.RemoveAllMixerInputs();
        lock (_lock)
        {
            foreach (var p in _playing) p.Close();
            _playing.Clear();
        }
    }

    private void Stop()
    {
        StopAll();
        _remote?.Dispose();
        _monitor?.Dispose();
        _remote = null;
        _monitor = null;
    }

    public void Dispose() => Stop();
}
