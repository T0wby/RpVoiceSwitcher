using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RpVoiceSwitcher;

public sealed record SoundFile(string FileName, string FullPath, string Name);

/// <summary>Sound-Ordner unter %AppData%\RpVoiceSwitcher\Sounds – vom Nutzer frei befüllbar.</summary>
public static class SoundLibrary
{
    public static readonly string[] Extensions = [".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".aiff"];

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RpVoiceSwitcher", "Sounds");

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static List<SoundFile> Load()
    {
        Directory.CreateDirectory(Folder);
        return Directory.GetFiles(Folder)
            .Where(IsSupported)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SoundFile(Path.GetFileName(p), p, Path.GetFileNameWithoutExtension(p)))
            .ToList();
    }

    /// <summary>Kopiert Dateien in den Sound-Ordner (bei gleichem Namen mit Zähler).</summary>
    public static int Import(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Folder);
        int count = 0;
        foreach (var src in paths.Where(File.Exists).Where(IsSupported))
        {
            string name = Path.GetFileNameWithoutExtension(src), ext = Path.GetExtension(src);
            string dst = Path.Combine(Folder, name + ext);
            for (int i = 2; File.Exists(dst); i++)
                dst = Path.Combine(Folder, $"{name} ({i}){ext}");
            File.Copy(src, dst);
            count++;
        }
        return count;
    }

    /// <summary>Legt beim ersten Start ein paar selbst erzeugte Sounds an (keine Lizenzfragen).</summary>
    public static void EnsureStarterSounds()
    {
        Directory.CreateDirectory(Folder);
        string marker = Path.Combine(Folder, ".starter-sounds");
        if (File.Exists(marker)) return; // nur einmal – gelöschte Sounds kommen nicht zurück

        WriteIfMissing("Messingglocke.wav", () => Bell(1250, 1.6, strikes: [0.0]));
        WriteIfMissing("Ringglocke.wav", () => Bell(1500, 1.2, strikes: [0.0, 0.32, 0.64]));
        WriteIfMissing("Arena-Gong.wav", Gong);
        File.WriteAllText(marker, "Diese Datei verhindert, dass gelöschte Start-Sounds neu erzeugt werden.");
    }

    private static void WriteIfMissing(string name, Func<float[]> make)
    {
        string path = Path.Combine(Folder, name);
        if (!File.Exists(path)) WriteWav(path, make());
    }

    private const int Rate = 48000;

    /// <summary>Kleine Handglocke: unharmonische Teiltöne mit unterschiedlich schnellem Ausklingen.</summary>
    private static float[] Bell(double f0, double ring, double[] strikes)
    {
        double[] ratios = [1.0, 2.0, 2.76, 3.0, 4.07, 5.4, 6.8];
        double[] amps = [1.0, 0.6, 0.45, 0.25, 0.22, 0.12, 0.08];
        double length = strikes.Max() + ring + 0.3;
        var buf = new float[(int)(length * Rate)];
        foreach (double start in strikes)
        {
            int s0 = (int)(start * Rate);
            for (int p = 0; p < ratios.Length; p++)
            {
                double f = f0 * ratios[p], tau = ring / (1 + p * 0.9), phase = p * 1.3;
                for (int i = s0; i < buf.Length; i++)
                {
                    double t = (i - s0) / (double)Rate;
                    double env = Math.Exp(-t / tau) * Math.Min(1, t / 0.002);
                    buf[i] += (float)(amps[p] * env * Math.Sin(2 * Math.PI * f * t + phase));
                }
            }
        }
        return Normalize(buf, 0.7);
    }

    /// <summary>Tiefer Gong: langsam ausklingende, leicht schwebende Teiltöne plus kurzer Schlag.</summary>
    private static float[] Gong()
    {
        double f0 = 98;
        double[] ratios = [1.0, 1.52, 2.16, 2.74, 3.31, 4.12, 5.05, 6.3];
        double[] amps = [1.0, 0.7, 0.55, 0.42, 0.33, 0.25, 0.18, 0.12];
        double length = 5.0;
        var buf = new float[(int)(length * Rate)];
        var rng = new Random(7);
        for (int p = 0; p < ratios.Length; p++)
        {
            double f = f0 * ratios[p], tau = 2.6 / (1 + p * 0.35), beat = 0.4 + p * 0.27;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)Rate;
                double swell = 1 - 0.35 * Math.Exp(-t / 0.25);            // Gong "blüht" kurz auf
                double shimmer = 1 + 0.12 * Math.Sin(2 * Math.PI * beat * t);
                double env = Math.Exp(-t / tau) * Math.Min(1, t / 0.004) * swell * shimmer;
                buf[i] += (float)(amps[p] * env * Math.Sin(2 * Math.PI * f * t + p));
            }
        }
        // Schlaggeräusch: kurzes, tiefpassgefiltertes Rauschen
        double lp = 0;
        for (int i = 0; i < (int)(0.08 * Rate); i++)
        {
            double t = i / (double)Rate;
            lp += 0.08 * ((rng.NextDouble() * 2 - 1) - lp);
            buf[i] += (float)(1.8 * lp * Math.Exp(-t / 0.02));
        }
        return Normalize(buf, 0.75);
    }

    private static float[] Normalize(float[] buf, double peak)
    {
        float max = buf.Max(x => Math.Abs(x));
        if (max <= 0) return buf;
        float k = (float)(peak / max);
        int fade = Rate / 50; // 20 ms ausblenden, damit es am Ende nicht knackt
        for (int i = 0; i < buf.Length; i++)
        {
            float f = i >= buf.Length - fade ? (buf.Length - i) / (float)fade : 1f;
            buf[i] *= k * f;
        }
        return buf;
    }

    private static void WriteWav(string path, float[] samples)
    {
        using var w = new BinaryWriter(File.Create(path));
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (float s in samples)
            w.Write((short)Math.Clamp((int)(s * short.MaxValue), short.MinValue, short.MaxValue));
    }
}
