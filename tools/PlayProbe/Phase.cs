using System;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

/// <summary>Spielt einen Mitte- oder Seite-Ton ins Kabel und misst am CABLE Output, ob L und R gleich- oder gegenphasig ankommen.</summary>
static class Phase
{
    sealed class Tone(bool side) : ISampleProvider
    {
        long _n;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] b, int o, int c)
        {
            for (int i = 0; i < c; i += 2) { float v = 0.3f * MathF.Sin(2 * MathF.PI * 440 * _n++ / 48000f); b[o + i] = v; b[o + i + 1] = side ? -v : v; }
            return c;
        }
    }

    public static void Run(bool side)
    {
        using var en = new MMDeviceEnumerator();
        var render = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).First(d => d.FriendlyName.Contains("CABLE Input"));
        var cap = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).First(d => d.FriendlyName.Contains("CABLE Output"));
        using var o = new WasapiOut(render, AudioClientShareMode.Shared, true, 50);
        o.Init(new Tone(side)); o.Play(); Thread.Sleep(400);
        using var c = new WasapiCapture(cap);
        double lr = 0, ll = 0, rr = 0;
        c.DataAvailable += (_, e) =>
        {
            for (int i = 0; i + 8 <= e.BytesRecorded; i += 8)
            {
                float l = BitConverter.ToSingle(e.Buffer, i), r = BitConverter.ToSingle(e.Buffer, i + 4);
                lr += l * r; ll += l * l; rr += r * r;
            }
        };
        c.StartRecording(); Thread.Sleep(1000); c.StopRecording(); o.Stop();
        double corr = lr / Math.Sqrt(Math.Max(1e-12, ll * rr));
        Console.WriteLine($"{(side ? "Seite (L=+x, R=-x)" : "Mitte (L=R)      ")}  gespielt auf {render.FriendlyName}: Korrelation L/R am Ausgang {corr:+0.00;-0.00}  (RMS L {Math.Sqrt(ll / 48000):F3}, R {Math.Sqrt(rr / 48000):F3})  Format {c.WaveFormat}");
    }
}
