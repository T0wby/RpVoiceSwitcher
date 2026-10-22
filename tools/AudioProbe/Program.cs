// Aufruf: AudioProbe "<Aufnahmegerät>" <Millisekunden> [mid|side|none] ["<Wiedergabegerät>"]
// Nimmt vom Aufnahmegerät auf und gibt den Spitzenpegel aus. Optional wird gleichzeitig ein 440-Hz-Testton
// auf dem Wiedergabegerät gespielt: "mid" = gleich auf L/R, "side" = gegenphasig (L = +x, R = -x).
using NAudio.CoreAudioApi;
using NAudio.Wave;

string name = args.Length > 0 ? args[0] : "CABLE Output";
int ms = args.Length > 1 ? int.Parse(args[1]) : 2000;
string tone = args.Length > 2 ? args[2] : "none";
string playDevice = args.Length > 3 ? args[3] : "CABLE Input";

using var en = new MMDeviceEnumerator();
var dev = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase));
if (dev == null) { Console.WriteLine("Gerät nicht gefunden: " + name); return 1; }

WasapiOut? output = null;
if (tone is "mid" or "side")
{
    var render = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                   .First(d => d.FriendlyName.Contains(playDevice, StringComparison.OrdinalIgnoreCase));
    output = new WasapiOut(render, AudioClientShareMode.Shared, true, 50);
    output.Init(new ToneProvider(tone == "side"));
    output.Play();
    Thread.Sleep(300);
}

using var cap = new WasapiCapture(dev);
float windowPeak = 0, total = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
long nextPrint = 100;
cap.DataAvailable += (_, e) =>
{
    for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
        windowPeak = Math.Max(windowPeak, Math.Abs(BitConverter.ToSingle(e.Buffer, i)));
    if (sw.ElapsedMilliseconds >= nextPrint)
    {
        Console.WriteLine($"{nextPrint,5} ms  {windowPeak:F3}");
        total = Math.Max(total, windowPeak);
        windowPeak = 0;
        nextPrint += 100;
    }
};
cap.StartRecording();
Thread.Sleep(ms);
cap.StopRecording();
output?.Stop();
total = Math.Max(total, windowPeak);
Console.WriteLine($"MAX {total:F3}  ({dev.FriendlyName}, {cap.WaveFormat})");
return 0;

sealed class ToneProvider(bool side) : ISampleProvider
{
    private long _n;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i += 2)
        {
            float v = 0.3f * MathF.Sin(2 * MathF.PI * 440 * _n++ / 48000f);
            buffer[offset + i] = v;
            buffer[offset + i + 1] = side ? -v : v;
        }
        return count;
    }
}
