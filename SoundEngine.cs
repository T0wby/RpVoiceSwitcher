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
        public AutoDisposeProvider(ISampleProvider source, IDisposable owner) { _source = source; _owner = owner; }
        public WaveFormat WaveFormat => _source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            if (_owner == null) return 0;
            int n = _source.Read(buffer, offset, count);
            if (n == 0) Close();
            return n;
        }
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
        output.Init(master);
        output.Play();
        return new Output { Device = output, Mixer = mixer, Master = master, DeviceId = device.ID };
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

    public bool Play(string path, bool bypassEffect)
    {
        if (_remote == null) return false;
        try
        {
            AddTo(_remote, path, sp => new MidSideProvider(sp, bypassEffect));
            if (_monitor != null) AddTo(_monitor, path, null);
            return true;
        }
        catch (Exception ex)
        {
            Error = $"\"{System.IO.Path.GetFileName(path)}\" konnte nicht abgespielt werden: {ex.Message}";
            return false;
        }
    }

    private void AddTo(Output output, string path, Func<ISampleProvider, ISampleProvider>? transform)
    {
        var reader = new AudioFileReader(path);
        ISampleProvider sp = reader;
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

        var tracked = new AutoDisposeProvider(sp, reader);
        lock (_lock)
        {
            _playing.RemoveAll(p => p.IsDone); // fertige Sounds aus der Liste nehmen
            _playing.Add(tracked);
        }
        output.Mixer.AddMixerInput(tracked);
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
