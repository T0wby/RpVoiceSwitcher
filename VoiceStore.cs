using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RpVoiceSwitcher;

public sealed record VoicePreset
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string GraillonHint { get; init; } = "";

    /// <summary>Lautstärke-Ausgleich in dB (Kopfzeile "# Lautstaerke: +3 dB"), damit alle Stimmen gleich laut ankommen.</summary>
    public double GainDb { get; init; }
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
    public string VoicesDir { get; }
    public string DefaultVoicesDir => Path.Combine(ConfigDir, VoicesFolderName);
    public bool UsesDefaultVoicesDir => string.Equals(Path.GetFullPath(VoicesDir), Path.GetFullPath(DefaultVoicesDir), StringComparison.OrdinalIgnoreCase);
    public string ActiveFile => Path.Combine(ConfigDir, "voice.txt");
    public string MainConfigFile => Path.Combine(ConfigDir, "config.txt");

    /// <summary>
    /// Bereinigte Kopie der aktiven Stimme. Liegt im Config-Ordner, damit Equalizer APO (läuft als LOCAL SERVICE)
    /// sie immer lesen kann – egal, in welchem Ordner die Stimmen selbst liegen.
    /// </summary>
    public string ActiveCopyFile => Path.Combine(ConfigDir, "voice-active.cfg");

    /// <summary>Virtueller Kanal für Sounds, die am Stimmeffekt vorbei sollen.</summary>
    private const string DryChannel = "RPS";
    private const string ActiveMarker = "# Datei: ";

    private VoiceStore(string configDir, string? voicesDir)
    {
        ConfigDir = configDir;
        VoicesDir = string.IsNullOrWhiteSpace(voicesDir) ? Path.Combine(configDir, VoicesFolderName) : voicesDir;
    }

    /// <summary>Die "Normal"-Stimme (00-…) gehört immer zu jedem Profil.</summary>
    public static bool IsNormalFile(string fileName) =>
        fileName.StartsWith("00-", StringComparison.OrdinalIgnoreCase);

    public bool IsMainConfigIncludingVoice()
    {
        if (!File.Exists(MainConfigFile)) return false;
        return File.ReadLines(MainConfigFile)
                   .Any(l => l.Trim().Equals("Include: voice.txt", StringComparison.OrdinalIgnoreCase));
    }

    public static VoiceStore? TryCreate(string? voicesDir = null)
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
        return Directory.Exists(dir) ? new VoiceStore(dir, voicesDir) : null;
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
            double gain = 0;
            foreach (var raw in File.ReadLines(path).Take(20))
            {
                var line = raw.Trim();
                if (!line.StartsWith('#')) continue;
                line = line.TrimStart('#').Trim();
                if (TryHeader(line, "Name", out var v)) name = v;
                else if (TryHeader(line, "Beschreibung", out v)) desc = v;
                else if (TryHeader(line, "Graillon", out v)) graillon = v;
                else if (TryGainHeader(line, out v)) gain = ParseGain(v);
            }
            result.Add(new VoicePreset
            {
                FileName = Path.GetFileName(path),
                FullPath = path,
                Name = name,
                Description = desc,
                GraillonHint = graillon,
                GainDb = gain,
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

    public const double MaxGainDb = 12;

    private static bool TryGainHeader(string line, out string value) =>
        TryHeader(line, "Lautstaerke", out value) || TryHeader(line, "Lautstärke", out value);

    /// <summary>"+3 dB", "-2,5", "1.5dB" → Zahl, begrenzt auf ±12 dB.</summary>
    public static double ParseGain(string text)
    {
        var t = text.Replace("dB", "", StringComparison.OrdinalIgnoreCase).Replace(',', '.').Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && double.IsFinite(g)
            ? Math.Clamp(Math.Round(g * 2) / 2, -MaxGainDb, MaxGainDb)
            : 0;
    }

    public static string FormatGain(double db) =>
        (db > 0 ? "+" : "") + db.ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " dB";

    /// <summary>
    /// Schaltet eine Stimme aktiv. Aufbau von voice.txt:
    /// 1. Mitte/Seite trennen: Die Stimme (Mono-Mikro) liegt identisch auf L und R = "Mitte".
    ///    Sounds "ohne Stimmeffekt" spielt das Soundboard gegenphasig (L = +x, R = -x) = "Seite".
    /// 2. Die Seite wird in einem virtuellen Kanal geparkt, die Stimme wirkt nur auf L und R.
    /// 3. Danach wird die Seite unverändert wieder dazugemischt.
    /// Die Stimme selbst wird als bereinigte Kopie eingebunden ("Channel: all" → "Channel: L R"),
    /// sonst würden Filter nach "Channel: all" auch den geparkten Sound treffen.
    /// </summary>
    public void Activate(VoicePreset preset, string targetDevice)
    {
        var copy = new StringBuilder();
        copy.AppendLine("# Automatisch erzeugt aus: " + preset.FullPath);
        copy.AppendLine("# Bitte nicht hier bearbeiten, sondern die Original-Stimme.");
        foreach (var line in File.Exists(preset.FullPath) ? File.ReadAllLines(preset.FullPath) : Array.Empty<string>())
            copy.AppendLine(SanitizeLine(line));

        var sb = new StringBuilder();
        sb.AppendLine("# Wird von RP Voice Switcher verwaltet - Aenderungen hier werden ueberschrieben.");
        sb.AppendLine("# Aktiv: " + preset.Name);
        sb.AppendLine(ActiveMarker + preset.FileName);
        if (!string.IsNullOrWhiteSpace(targetDevice))
            sb.AppendLine("Device: " + targetDevice.Trim());
        sb.AppendLine($"Copy: {DryChannel}=0.5*L+-0.5*R");
        sb.AppendLine("Copy: L=0.5*L+0.5*R R=0.5*L+0.5*R");
        sb.AppendLine("Channel: L R");
        sb.AppendLine("Include: " + Path.GetFileName(ActiveCopyFile));
        if (Math.Abs(preset.GainDb) >= 0.05)
        {
            // Lautstärke-Ausgleich nur auf die Stimme (L/R) – trockene Sounds im geparkten Kanal bleiben unverändert.
            string f = Math.Pow(10, preset.GainDb / 20).ToString("0.#####", CultureInfo.InvariantCulture);
            sb.AppendLine($"# Lautstaerke-Ausgleich {FormatGain(preset.GainDb)}");
            sb.AppendLine($"Copy: L={f}*L R={f}*R");
        }
        sb.AppendLine("Channel: all");
        sb.AppendLine($"Copy: L=L+{DryChannel} R=R+{DryChannel}");
        sb.AppendLine("Device: all");

        WriteWithRetry(ActiveCopyFile, copy.ToString());
        WriteWithRetry(ActiveFile, sb.ToString());
    }

    /// <summary>Kein Effekt mehr (z. B. Notfall-Taste, wenn es keine "Normal"-Stimme gibt).</summary>
    public void Deactivate() =>
        WriteWithRetry(ActiveFile, "# RP Voice Switcher: keine Stimme aktiv" + Environment.NewLine);

    /// <summary>Hält die Stimme auf L/R und verhindert, dass sie den Gerätebereich von voice.txt verlässt.</summary>
    private static string SanitizeLine(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("Channel:", StringComparison.OrdinalIgnoreCase) &&
            t["Channel:".Length..].Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return "Channel: L R";
        if (t.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
            return "# (entfernt) " + t;
        return line;
    }

    private static void WriteWithRetry(string path, string content)
    {
        // Equalizer APO liest die Dateien bei Änderungen sofort neu ein; kurz wiederholen, falls sie gerade gesperrt sind.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, content, Utf8NoBom);
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
    private static readonly string[] HeaderKeys = ["Name", "Beschreibung", "Graillon", "Lautstaerke", "Lautstärke"];

    /// <summary>Kopiert alle Stimmen in einen anderen Ordner (vorhandene Dateien bleiben unberührt).</summary>
    public static int CopyVoicesTo(string fromDir, string toDir)
    {
        Directory.CreateDirectory(toDir);
        int n = 0;
        if (!Directory.Exists(fromDir)) return 0;
        foreach (var src in Directory.GetFiles(fromDir, "*.txt"))
        {
            var dst = Path.Combine(toDir, Path.GetFileName(src));
            if (File.Exists(dst)) continue;
            File.Copy(src, dst);
            n++;
        }
        return n;
    }

    /// <summary>Merkt sich gelöschte mitgelieferte Stimmen (ohne .txt-Endung, damit sie nicht als Stimme gilt).</summary>
    private string DeletedListFile => Path.Combine(VoicesDir, ".geloeschte-stimmen");

    private HashSet<string> ReadDeleted() => File.Exists(DeletedListFile)
        ? new HashSet<string>(File.ReadAllLines(DeletedListFile).Where(l => l.Trim().Length > 0).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private void WriteDeleted(HashSet<string> names) =>
        File.WriteAllLines(DeletedListFile, names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

    /// <summary>Legt eine neue Stimmen-Datei an (z. B. "12-kobold.txt") und gibt den Dateinamen zurück.</summary>
    public string CreateVoice(string name, string description, string graillon, IEnumerable<string>? body = null, double gainDb = 0)
    {
        Directory.CreateDirectory(VoicesDir);
        string fileName = NextFileName(name);
        var lines = BuildHeader(name, description, graillon, gainDb).ToList();
        var bodyLines = body?.ToList() ?? new List<string>();
        if (bodyLines.Count == 0)
            bodyLines.Add("# Noch keine Effekte – im EAPO-Editor oder hier per Text einbauen.");
        lines.AddRange(bodyLines);
        File.WriteAllLines(Path.Combine(VoicesDir, fileName), lines, Utf8NoBom);

        var deleted = ReadDeleted();
        if (deleted.Remove(fileName)) WriteDeleted(deleted);
        return fileName;
    }

    /// <summary>Ersetzt nur die Kopfzeilen (Name, Texte, Lautstärke) – Effekte (auch Graillon selbst) bleiben unberührt.</summary>
    public void UpdateHeader(VoicePreset preset, string name, string description, string graillon, double gainDb)
    {
        var lines = BuildHeader(name, description, graillon, gainDb).Concat(ReadBody(preset)).ToList();
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

    private static IEnumerable<string> BuildHeader(string name, string description, string graillon, double gainDb)
    {
        static string Clean(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
        yield return "# Name: " + Clean(name);
        if (Clean(description).Length > 0) yield return "# Beschreibung: " + Clean(description);
        if (Clean(graillon).Length > 0) yield return "# Graillon: " + Clean(graillon);
        if (Math.Abs(gainDb) >= 0.05)
            yield return "# Lautstaerke: " + (gainDb > 0 ? "+" : "") + gainDb.ToString("0.#", CultureInfo.InvariantCulture) + " dB";
    }

    // ---------- Export / Import als ZIP ----------

    /// <summary>Packt Stimmen in eine ZIP-Datei (zum Weitergeben oder Sichern).</summary>
    public static void ExportZip(IEnumerable<VoicePreset> voices, string zipPath)
    {
        string tmp = zipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in voices)
            {
                if (!File.Exists(v.FullPath) || !used.Add(v.FileName)) continue;
                zip.CreateEntryFromFile(v.FullPath, v.FileName, CompressionLevel.Optimal);
            }
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    /// <summary>Übernimmt alle .txt-Stimmen aus einer ZIP-Datei. Liefert die neuen Dateinamen.</summary>
    public List<string> ImportZip(string zipPath)
    {
        var imported = new List<string>();
        string tmpDir = Path.Combine(Path.GetTempPath(), "RpVoiceSwitcher-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                // Nur Stimmen-Dateien, ohne Unterordner-Pfade (schützt auch vor "..\"-Tricks in der ZIP)
                string name = Path.GetFileName(entry.FullName);
                if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || entry.Length > 1_000_000) continue;
                string tmpFile = Path.Combine(tmpDir, name);
                entry.ExtractToFile(tmpFile, overwrite: true);
                string existing = Path.Combine(VoicesDir, name);
                if (File.Exists(existing) && (IsNormalFile(name) || File.ReadAllText(existing) == File.ReadAllText(tmpFile)))
                    continue; // "Normal" bzw. genau diese Stimme gibt es schon
                imported.Add(ImportVoice(tmpFile));
            }
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { /* egal */ }
        }
        return imported;
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
        foreach (var raw in File.ReadLines(ActiveFile))
        {
            var line = raw.Trim();
            if (line.StartsWith(ActiveMarker, StringComparison.OrdinalIgnoreCase)) return line[ActiveMarker.Length..].Trim();
            if (line.StartsWith(IncludePrefix, StringComparison.OrdinalIgnoreCase)) return line[IncludePrefix.Length..]; // ältere Version
        }
        return null;
    }
}
