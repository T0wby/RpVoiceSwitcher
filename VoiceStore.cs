using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RpVoiceSwitcher;

public sealed class VoicePreset
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string GraillonHint { get; init; } = "";
}

/// <summary>
/// Kapselt den Zugriff auf den Equalizer-APO-Config-Ordner:
/// rp-voices\*.txt sind die Stimmen, voice.txt verweist per Include auf die aktive.
/// </summary>
public sealed class VoiceStore
{
    public const string VoicesFolderName = "rp-voices";
    private const string IncludePrefix = "Include: " + VoicesFolderName + "\\";

    public string ConfigDir { get; }
    public string VoicesDir => Path.Combine(ConfigDir, VoicesFolderName);
    public string ActiveFile => Path.Combine(ConfigDir, "voice.txt");
    public string MainConfigFile => Path.Combine(ConfigDir, "config.txt");

    private VoiceStore(string configDir) => ConfigDir = configDir;

    public static VoiceStore? TryCreate()
    {
        string? dir = null;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                       .OpenSubKey(@"SOFTWARE\EqualizerAPO");
            dir = key?.GetValue("ConfigPath") as string;
        }
        catch { /* Fallback unten */ }

        dir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "config");
        return Directory.Exists(dir) ? new VoiceStore(dir) : null;
    }

    /// <summary>Kopiert mitgelieferte Stimmen (ohne vorhandene zu überschreiben) und hängt voice.txt in config.txt ein.</summary>
    public void EnsureInstalled(string bundledVoicesDir)
    {
        Directory.CreateDirectory(VoicesDir);

        if (Directory.Exists(bundledVoicesDir))
        {
            foreach (var src in Directory.GetFiles(bundledVoicesDir, "*.txt"))
            {
                var dst = Path.Combine(VoicesDir, Path.GetFileName(src));
                if (!File.Exists(dst))
                    File.Copy(src, dst);
            }
        }

        if (File.Exists(MainConfigFile))
        {
            var lines = File.ReadAllLines(MainConfigFile);
            bool included = lines.Any(l => l.Trim().Equals("Include: voice.txt", StringComparison.OrdinalIgnoreCase));
            if (!included)
                File.AppendAllText(MainConfigFile, Environment.NewLine + "Include: voice.txt" + Environment.NewLine);
        }

        if (!File.Exists(ActiveFile))
            File.WriteAllText(ActiveFile, "# RP Voice Switcher: keine Stimme aktiv" + Environment.NewLine);
    }

    public List<VoicePreset> LoadVoices()
    {
        var result = new List<VoicePreset>();
        if (!Directory.Exists(VoicesDir)) return result;

        foreach (var path in Directory.GetFiles(VoicesDir, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(path), desc = "", graillon = "";
            foreach (var raw in File.ReadLines(path).Take(20))
            {
                var line = raw.Trim();
                if (!line.StartsWith('#')) continue;
                line = line.TrimStart('#').Trim();
                if (TryHeader(line, "Name", out var v)) name = v;
                else if (TryHeader(line, "Beschreibung", out v)) desc = v;
                else if (TryHeader(line, "Graillon", out v)) graillon = v;
            }
            result.Add(new VoicePreset
            {
                FileName = Path.GetFileName(path),
                FullPath = path,
                Name = name,
                Description = desc,
                GraillonHint = graillon,
            });
        }
        return result;
    }

    private static bool TryHeader(string line, string key, out string value)
    {
        value = "";
        if (!line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) return false;
        value = line[(key.Length + 1)..].Trim();
        return true;
    }

    public void Activate(VoicePreset preset, string targetDevice)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Wird von RP Voice Switcher verwaltet - Aenderungen hier werden ueberschrieben.");
        sb.AppendLine("# Aktiv: " + preset.Name);
        if (!string.IsNullOrWhiteSpace(targetDevice))
            sb.AppendLine("Device: " + targetDevice.Trim());
        sb.AppendLine(IncludePrefix + preset.FileName);
        sb.AppendLine("Device: all");

        // Equalizer APO liest die Datei bei Änderungen sofort neu ein; kurz wiederholen, falls sie gerade gesperrt ist.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(ActiveFile, sb.ToString(), Encoding.ASCII);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    public string? GetActiveFileName()
    {
        if (!File.Exists(ActiveFile)) return null;
        var line = File.ReadLines(ActiveFile)
                       .FirstOrDefault(l => l.Trim().StartsWith(IncludePrefix, StringComparison.OrdinalIgnoreCase));
        return line?.Trim()[IncludePrefix.Length..];
    }
}
