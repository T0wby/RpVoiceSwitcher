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

    /// <summary>Die "Normal"-Stimme (00-…) gehört immer zu jedem Profil.</summary>
    public static bool IsNormalFile(string fileName) =>
        fileName.StartsWith("00-", StringComparison.OrdinalIgnoreCase);

    public bool IsMainConfigIncludingVoice()
    {
        if (!File.Exists(MainConfigFile)) return false;
        return File.ReadLines(MainConfigFile)
                   .Any(l => l.Trim().Equals("Include: voice.txt", StringComparison.OrdinalIgnoreCase));
    }

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
            var deleted = ReadDeleted();
            foreach (var src in Directory.GetFiles(bundledVoicesDir, "*.txt"))
            {
                var name = Path.GetFileName(src);
                var dst = Path.Combine(VoicesDir, name);
                if (!File.Exists(dst) && !deleted.Contains(name)) // absichtlich gelöschte nicht zurückholen
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
                File.WriteAllText(ActiveFile, sb.ToString(), Utf8NoBom);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    // ---------- Stimmen anlegen, bearbeiten, löschen ----------

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string[] HeaderKeys = ["Name", "Beschreibung", "Graillon"];

    /// <summary>Merkt sich gelöschte mitgelieferte Stimmen (ohne .txt-Endung, damit sie nicht als Stimme gilt).</summary>
    private string DeletedListFile => Path.Combine(VoicesDir, ".geloeschte-stimmen");

    private HashSet<string> ReadDeleted() => File.Exists(DeletedListFile)
        ? new HashSet<string>(File.ReadAllLines(DeletedListFile).Where(l => l.Trim().Length > 0).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private void WriteDeleted(HashSet<string> names) =>
        File.WriteAllLines(DeletedListFile, names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

    /// <summary>Legt eine neue Stimmen-Datei an (z. B. "12-kobold.txt") und gibt den Dateinamen zurück.</summary>
    public string CreateVoice(string name, string description, string graillon, IEnumerable<string>? body = null)
    {
        Directory.CreateDirectory(VoicesDir);
        string fileName = NextFileName(name);
        var lines = BuildHeader(name, description, graillon).ToList();
        var bodyLines = body?.ToList() ?? new List<string>();
        if (bodyLines.Count == 0)
            bodyLines.Add("# Noch keine Effekte – im EAPO-Editor oder hier per Text einbauen.");
        lines.AddRange(bodyLines);
        File.WriteAllLines(Path.Combine(VoicesDir, fileName), lines, Utf8NoBom);

        var deleted = ReadDeleted();
        if (deleted.Remove(fileName)) WriteDeleted(deleted);
        return fileName;
    }

    /// <summary>Ersetzt nur Name/Beschreibung/Graillon-Hinweis – Effekte (auch Graillon selbst) bleiben unberührt.</summary>
    public void UpdateHeader(VoicePreset preset, string name, string description, string graillon)
    {
        var lines = BuildHeader(name, description, graillon).Concat(ReadBody(preset)).ToList();
        File.WriteAllLines(preset.FullPath, lines, Utf8NoBom);
    }

    /// <summary>Alles außer den drei Kopfzeilen – also die eigentlichen Effekte.</summary>
    public IEnumerable<string> ReadBody(VoicePreset preset) =>
        File.ReadAllLines(preset.FullPath).Where(l => !IsHeaderLine(l));

    public void DeleteVoice(VoicePreset preset)
    {
        File.Delete(preset.FullPath);
        var deleted = ReadDeleted();
        if (deleted.Add(preset.FileName)) WriteDeleted(deleted);
    }

    /// <summary>Übernimmt eine .txt-Stimme aus einem anderen Ordner (bei gleichem Namen mit neuer Nummer).</summary>
    public string ImportVoice(string path)
    {
        Directory.CreateDirectory(VoicesDir);
        string fileName = Path.GetFileName(path);
        if (File.Exists(Path.Combine(VoicesDir, fileName)) || IsNormalFile(fileName))
            fileName = NextFileName(Path.GetFileNameWithoutExtension(path));
        File.Copy(path, Path.Combine(VoicesDir, fileName));

        var deleted = ReadDeleted();
        if (deleted.Remove(fileName)) WriteDeleted(deleted);
        return fileName;
    }

    private static bool IsHeaderLine(string raw)
    {
        var line = raw.Trim();
        if (!line.StartsWith('#')) return false;
        line = line.TrimStart('#').Trim();
        return HeaderKeys.Any(k => TryHeader(line, k, out _));
    }

    private static IEnumerable<string> BuildHeader(string name, string description, string graillon)
    {
        static string Clean(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
        yield return "# Name: " + Clean(name);
        if (Clean(description).Length > 0) yield return "# Beschreibung: " + Clean(description);
        if (Clean(graillon).Length > 0) yield return "# Graillon: " + Clean(graillon);
    }

    /// <summary>Nächste freie Nummer + lesbarer Name, z. B. "12-herr-mondgesicht-2.txt".</summary>
    private string NextFileName(string name)
    {
        int next = 1;
        if (Directory.Exists(VoicesDir))
        {
            foreach (var f in Directory.GetFiles(VoicesDir, "*.txt"))
            {
                var stem = Path.GetFileName(f);
                int dash = stem.IndexOf('-');
                if (dash > 0 && int.TryParse(stem[..dash], out int n)) next = Math.Max(next, n + 1);
            }
        }
        string slug = Slug(name);
        string candidate = $"{next:00}-{slug}.txt";
        for (int i = 2; File.Exists(Path.Combine(VoicesDir, candidate)); i++)
            candidate = $"{next:00}-{slug}-{i}.txt";
        return candidate;
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            string part = c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'ß' => "ss",
                _ when char.IsAsciiLetterOrDigit(c) => c.ToString(),
                _ => "-",
            };
            sb.Append(part);
        }
        string slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "stimme" : slug;
    }

    public string? GetActiveFileName()
    {
        if (!File.Exists(ActiveFile)) return null;
        var line = File.ReadLines(ActiveFile)
                       .FirstOrDefault(l => l.Trim().StartsWith(IncludePrefix, StringComparison.OrdinalIgnoreCase));
        return line?.Trim()[IncludePrefix.Length..];
    }
}
