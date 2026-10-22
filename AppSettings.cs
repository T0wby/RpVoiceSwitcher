using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RpVoiceSwitcher;

public sealed class AppSettings
{
    /// <summary>Dateiname der Stimme → Hotkey (Format siehe <see cref="Hotkey.Serialize"/>).</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string TargetDevice { get; set; } = "CABLE Output";
    public bool PlaySound { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool ShowStatusOverlay { get; set; } = true;
    public double? StatusOverlayLeft { get; set; }
    public double? StatusOverlayTop { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RpVoiceSwitcher", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.Hotkeys = new Dictionary<string, string>(s.Hotkeys, StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch { /* kaputte Datei → Standardwerte */ }

        return CreateDefaults();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static AppSettings CreateDefaults()
    {
        var s = new AppSettings();
        // Strg+Alt+0 = Normal, Strg+Alt+1..8 = die mitgelieferten Stimmen
        string[] files =
        [
            "00-normal.txt", "01-maske.txt", "02-tief.txt", "03-funk.txt", "04-megafon.txt",
            "05-hoehle.txt", "06-metallhelm.txt", "07-geist.txt", "08-riese.txt",
        ];
        for (int i = 0; i < files.Length; i++)
            s.Hotkeys[files[i]] = $"Control, Alt|D{i}";
        return s;
    }
}
