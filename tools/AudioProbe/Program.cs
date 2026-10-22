// Aufruf: AudioProbe "<Teil des Gerätenamens>" <Millisekunden>
// Gibt pro 100 ms den Spitzenpegel aus und am Ende das Maximum.
using NAudio.CoreAudioApi;
using NAudio.Wave;

string name = args.Length > 0 ? args[0] : "CABLE Output";
int ms = args.Length > 1 ? int.Parse(args[1]) : 2000;

using var en = new MMDeviceEnumerator();
var dev = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase));
if (dev == null) { Console.WriteLine("Gerät nicht gefunden: " + name); return 1; }

using var cap = new WasapiCapture(dev);
float windowPeak = 0, total = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
long nextPrint = 100;
cap.DataAvailable += (_, e) =>
{
    var fmt = cap.WaveFormat;
    for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
    {
        float v = fmt.Encoding == WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample == 32
            ? Math.Abs(BitConverter.ToSingle(e.Buffer, i))
            : Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
        windowPeak = Math.Max(windowPeak, v);
    }
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
total = Math.Max(total, windowPeak);
Console.WriteLine($"MAX {total:F3}  ({dev.FriendlyName}, {cap.WaveFormat})");
return 0;
