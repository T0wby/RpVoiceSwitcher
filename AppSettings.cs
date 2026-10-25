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

    /// <summary>true = alle Sounds im Sound-Ordner gehören dazu (Standard, auch für ältere Einstellungen).</summary>
    public bool IncludeAllSounds { get; set; } = true;

    /// <summary>Dateinamen der Sounds in diesem Profil (nur relevant, wenn <see cref="IncludeAllSounds"/> false ist).</summary>
    public List<string> Sounds { get; set; } = new();

    public bool ContainsVoice(string fileName) =>
        IncludeAllVoices || VoiceStore.IsNormalFile(fileName) ||
        Voices.Any(v => v.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    public bool ContainsSound(string fileName) =>
        IncludeAllSounds || Sounds.Any(s => s.Equals(fileName, StringComparison.OrdinalIgnoreCase));

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
        Sounds ??= new();
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

    /// <summary>Größe der Status-Anzeige (1 = 100 %).</summary>
    public double StatusOverlayScale { get; set; } = 1.0;

    /// <summary>Deckkraft der Status-Anzeige (0,3 … 1).</summary>
    public double StatusOverlayOpacity { get; set; } = 1.0;

    /// <summary>Status-Anzeige nur zeigen, wenn eine Effekt-Stimme aktiv ist.</summary>
    public bool StatusOverlayHideNormal { get; set; }

    /// <summary>Laufende Sounds mit Fortschritt in der Status-Anzeige zeigen.</summary>
    public bool StatusOverlayShowSounds { get; set; } = true;

    /// <summary>
    /// Notfall-Taste: gilt in jedem Charakter, schaltet sofort auf "Normal" und stoppt alle Sounds.
    /// null = keine. Standard: Pause-Taste (stört in Spielen praktisch nie).
    /// </summary>
    public string? PanicHotkey { get; set; } = "None|Pause";

    // Stream (OBS): aktive Stimme in eine Textdatei schreiben
    public bool ObsFileEnabled { get; set; }
    public string? ObsFilePath { get; set; }
    public string ObsTemplate { get; set; } = "{stimme}";

    // Einrichtungs-Assistent
    public bool SetupWizardDone { get; set; }
    public int SetupWizardStep { get; set; }
    public bool OpenWizardAfterRestart { get; set; }

    /// <summary>Kabel als Windows-Standardmikrofon halten (stellt es zurück, wenn ein anderes Programm umstellt).</summary>
    public bool KeepCableDefaultMic { get; set; }

    // Updates
    public bool CheckForUpdates { get; set; } = true;
    public string? SkippedUpdateVersion { get; set; }
    /// <summary>Version, deren "Was ist neu" zuletzt gezeigt wurde (null = noch nie, z. B. vor 1.9).</summary>
    public string? LastSeenVersion { get; set; }

    // Soundboard
    public string SoundOutputDevice { get; set; } = "CABLE Input";
    public double SoundVolume { get; set; } = 0.8;
    public bool SoundMonitor { get; set; } = true;
    public double SoundMonitorVolume { get; set; } = 0.5;

    /// <summary>Jeden Sound automatisch auf dieselbe (volle) Lautstärke bringen.</summary>
    public bool SoundNormalize { get; set; } = true;

    /// <summary>Sprachtaste (Push-to-Talk) des Spiels drücken, solange ein Sound läuft.</summary>
    public bool PushToTalkEnabled { get; set; }
    /// <summary>z. B. "Key|V" oder "Mouse|1" (Maus 4).</summary>
    public string? PushToTalkKey { get; set; }
    /// <summary>Globaler Shortcut: Sprachtaste dauerhaft halten an/aus ("Dauersenden"), wie die Notfall-Taste für alle Charaktere.</summary>
    public string? PttLatchHotkey { get; set; }
    /// <summary>
    /// Spiel steht auf "offenes Mikrofon" (z. B. nanos world: Audio → Open Mic): Die Sprachtaste schaltet dort
    /// stumm/an. Dann tippt die App sie nur an, statt sie zu halten, und drückt bei Sounds nichts.
    /// </summary>
    public bool GameOpenMic { get; set; }
    /// <summary>Sprachtaste nur drücken, solange das Spiel (PttGameProcess) im Vordergrund ist.</summary>
    public bool PttOnlyInGame { get; set; } = true;
    /// <summary>Prozessname des Spiels ohne ".exe" (Standard: nanos world).</summary>
    public string PttGameProcess { get; set; } = "NanosWorld-Win64-Shipping";

    /// <summary>Sounds, die durch den aktiven Stimmeffekt laufen sollen (Standard: ohne Effekt).</summary>
    public List<string> SoundsWithEffect { get; set; } = new();

    /// <summary>Sound-Datei → wie oft hintereinander (0 = endlos, fehlt = einmal).</summary>
    public Dictionary<string, int> SoundRepeat { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Eigener Ordner für Stimmen; null = Equalizer-APO-Ordner\rp-voices.</summary>
    public string? VoicesFolder { get; set; }

    /// <summary>Eigener Ordner für Sounds; null = Dokumente\RP Voice Switcher\Sounds.</summary>
    public string? SoundFolder { get; set; }

    // Fensterposition (wie Windows-Programme sie sich merken)
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    [JsonIgnore]
    public static string SettingsFile => FilePath;

    /// <summary>Alte Version (vor Profilen) – wird beim Laden ins Profil "Standard" übernommen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Hotkeys { get; set; }

    [JsonIgnore]
    public CharacterProfile Current =>
        Profiles.FirstOrDefault(p => p.Name.Equals(ActiveProfile, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];

    /// <summary>Für Tests: eigene Einstellungsdatei statt der echten (Startparameter --settings &lt;Datei&gt;).</summary>
    public static string? OverridePath { get; set; }

    private static string FilePath => OverridePath ?? Path.Combine(
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
        s.SoundRepeat = new(s.SoundRepeat ?? new(), StringComparer.OrdinalIgnoreCase);
        s.SoundsWithEffect ??= new();
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
