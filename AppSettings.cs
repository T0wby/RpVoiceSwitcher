using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RpVoiceSwitcher;

/// <summary>Ein Charakter: eigene Stimmenauswahl und eigene Tastenbelegung.</summary>
public sealed class CharacterProfile
{
    public string Name { get; set; } = "Standard";

    /// <summary>true = alle Stimmen (auch neu hinzugefügte) gehören dazu.</summary>
    public bool IncludeAllVoices { get; set; } = true;

    /// <summary>Dateinamen der Stimmen in diesem Profil (nur relevant, wenn <see cref="IncludeAllVoices"/> false ist).</summary>
    public List<string> Voices { get; set; } = new();

    /// <summary>Stimme → Shortcut zum Umschalten.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stimme → Taste, die gehalten werden muss.</summary>
    public Dictionary<string, string> HoldHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sound-Datei → Shortcut zum Abspielen.</summary>
    public Dictionary<string, string> SoundHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ContainsVoice(string fileName) =>
        IncludeAllVoices || VoiceStore.IsNormalFile(fileName) ||
        Voices.Any(v => v.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Alle Belegungen dieses Profils, damit eine Taste nirgends doppelt vergeben wird.</summary>
    public IEnumerable<Dictionary<string, string>> AllBindings()
    {
        yield return Hotkeys;
        yield return HoldHotkeys;
        yield return SoundHotkeys;
    }

    internal void Normalize()
    {
        Hotkeys = new(Hotkeys ?? new(), StringComparer.OrdinalIgnoreCase);
        HoldHotkeys = new(HoldHotkeys ?? new(), StringComparer.OrdinalIgnoreCase);
        SoundHotkeys = new(SoundHotkeys ?? new(), StringComparer.OrdinalIgnoreCase);
        Voices ??= new();
        if (string.IsNullOrWhiteSpace(Name)) Name = "Profil";
    }
}

public sealed class AppSettings
{
    public List<CharacterProfile> Profiles { get; set; } = new();
    public string ActiveProfile { get; set; } = "Standard";

    public string TargetDevice { get; set; } = "CABLE Output";
    public bool PlaySound { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool ShowStatusOverlay { get; set; } = true;
    public double? StatusOverlayLeft { get; set; }
    public double? StatusOverlayTop { get; set; }

    // Soundboard
    public string SoundOutputDevice { get; set; } = "CABLE Input";
    public double SoundVolume { get; set; } = 0.8;
    public bool SoundMonitor { get; set; } = true;
    public double SoundMonitorVolume { get; set; } = 0.5;

    /// <summary>Alte Version (vor Profilen) – wird beim Laden ins Profil "Standard" übernommen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Hotkeys { get; set; }

    [JsonIgnore]
    public CharacterProfile Current =>
        Profiles.FirstOrDefault(p => p.Name.Equals(ActiveProfile, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RpVoiceSwitcher", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppSettings? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
        }
        catch { /* kaputte Datei → Standardwerte */ }

        s ??= new AppSettings();
        s.Profiles ??= new();
        bool migrated = s.Profiles.Count == 0;

        if (migrated)
        {
            var standard = new CharacterProfile { Name = "Standard", IncludeAllVoices = true };
            if (s.Hotkeys is { Count: > 0 } legacy)
                foreach (var kv in legacy) standard.Hotkeys[kv.Key] = kv.Value;
            else
                standard.Hotkeys = DefaultHotkeys();
            s.Profiles.Add(standard);
        }
        s.Hotkeys = null;

        foreach (var p in s.Profiles) p.Normalize();
        if (!s.Profiles.Any(p => p.Name.Equals(s.ActiveProfile, StringComparison.OrdinalIgnoreCase)))
            s.ActiveProfile = s.Profiles[0].Name;

        if (migrated) s.Save(); // neues Format sofort festschreiben
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* Einstellungen sind nicht kritisch */ }
    }

    public static Dictionary<string, string> DefaultHotkeys()
    {
        // Strg+Alt+0 = Normal, Strg+Alt+1..8 = die mitgelieferten Stimmen
        string[] files =
        [
            "00-normal.txt", "01-maske.txt", "02-tief.txt", "03-funk.txt", "04-megafon.txt",
            "05-hoehle.txt", "06-metallhelm.txt", "07-geist.txt", "08-riese.txt",
        ];
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
            map[files[i]] = $"Control, Alt|D{i}";
        return map;
    }
}
