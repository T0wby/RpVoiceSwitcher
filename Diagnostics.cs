using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RpVoiceSwitcher;

/// <summary>
/// Sammelt alles, was man zum Helfen braucht, als Text (Hilfe → Problem melden / Diagnose kopieren).
/// Pfade im Benutzerordner werden gekürzt, damit der Windows-Benutzername nicht mitgeschickt wird.
/// </summary>
public static class Diagnostics
{
    public const string IssueUrl = "https://github.com/" + UpdateChecker.Repository + "/issues/new?template=problem.yml";

    public static string Build(AppSettings settings, VoiceStore? store, IEnumerable<CheckResult> checks, string activeVoice)
    {
        var sb = new StringBuilder();
        void Section(string title) { sb.AppendLine(); sb.AppendLine("== " + title + " =="); }
        void Safe(Action a) { try { a(); } catch (Exception ex) { sb.AppendLine("(nicht lesbar: " + ex.Message + ")"); } }

        sb.AppendLine($"RP Voice Switcher {UpdateChecker.CurrentVersionText} – Diagnose vom {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        sb.AppendLine($"Programm: {Environment.ProcessPath}");

        Section("Einstellungen");
        sb.AppendLine($"Stimmen wirken auf: {settings.TargetDevice}");
        sb.AppendLine($"Soundboard spielt auf: {settings.SoundOutputDevice} (Mithören: {(settings.SoundMonitor ? "an" : "aus")})");
        sb.AppendLine($"Charaktere: {settings.Profiles.Count}, Stimmen im aktiven: {(settings.Current.IncludeAllVoices ? "alle" : settings.Current.Voices.Count.ToString())}");
        sb.AppendLine($"Aktive Stimme: {activeVoice}");
        sb.AppendLine($"Notfall-Taste: {Hotkey.Parse(settings.PanicHotkey)?.ToString() ?? "keine"}");
        var mic = DefaultMic.Read(settings.TargetDevice);
        sb.AppendLine($"Windows-Standardmikrofon: {mic.DefaultName ?? "?"} / Kommunikation: {mic.CommsName ?? "?"} (Kabel halten: {(settings.KeepCableDefaultMic ? "an" : "aus")})");
        sb.AppendLine($"Sprachtaste: {PushToTalk.Binding.Parse(settings.PushToTalkKey)?.ToString() ?? "keine"} (bei Sounds: {(settings.PushToTalkEnabled ? "an" : "aus")}), Dauersenden-Taste: {Hotkey.Parse(settings.PttLatchHotkey)?.ToString() ?? "keine"}, Spiel: {(settings.GameOpenMic ? "offenes Mikrofon (Taste antippen)" : "Push-to-Talk (Taste halten)")}");
        sb.AppendLine($"Stimmen-Ordner: {store?.VoicesDir ?? "(Equalizer APO nicht gefunden)"}");
        sb.AppendLine($"Sound-Ordner: {SoundLibrary.Folder}");
        sb.AppendLine($"Assistent abgeschlossen: {(settings.SetupWizardDone ? "ja" : "nein")}");

        Section("Einrichtungs-Prüfung");
        foreach (var c in checks)
            sb.AppendLine($"[{c.State switch { CheckState.Ok => "OK", CheckState.Warning => "Hinweis", CheckState.Error => "FEHLER", _ => "Info" }}] {c.Title}: {c.Detail}");

        Section("Geräte");
        Safe(() => { foreach (var line in SetupChecker.DescribeEndpoints()) sb.AppendLine(line); });
        Safe(() =>
        {
            var s = SetupChecker.Probe(settings);
            sb.AppendLine($"Standard-Wiedergabe: {s.DefaultPlayback}");
            sb.AppendLine($"Exklusivmodus am Zielgerät: {(s.Exclusive == null ? "Gerät nicht gefunden" : s.Exclusive.Allowed ? "erlaubt" : "aus")}");
            sb.AppendLine($"Neustart ausstehend: {(s.RestartPending ? "ja" : "nein")}");
        });

        if (store != null)
        {
            Section("voice.txt");
            Safe(() => sb.AppendLine(File.Exists(store.ActiveFile) ? File.ReadAllText(store.ActiveFile).TrimEnd() : "(fehlt)"));
            Section("voice-active.cfg (Anfang)");
            Safe(() => sb.AppendLine(File.Exists(store.ActiveCopyFile)
                // Ohne Kommentare: Name/Beschreibung der Stimme braucht niemand zum Helfen
                ? string.Join(Environment.NewLine, File.ReadLines(store.ActiveCopyFile).Where(l => !l.TrimStart().StartsWith('#')).Take(40).Select(ShortenChunks))
                : "(fehlt)"));
            Section("config.txt");
            Safe(() => sb.AppendLine(File.Exists(store.MainConfigFile)
                ? string.Join(Environment.NewLine, File.ReadLines(store.MainConfigFile).Take(40))
                : "(fehlt)"));
        }

        Section("Letzte Fehler");
        sb.AppendLine(ErrorLog.ReadRecent());

        string text = Anonymize(sb.ToString());
        // Eigene Charakternamen (z. B. in Stimmennamen) nicht mitschicken
        foreach (var name in settings.Profiles.Select(p => p.Name.Trim()).Where(n => n.Length >= 3 && n != "Standard").Distinct())
            text = text.Replace(name, "<Charakter>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>VST-Einstellungen (lange Base64-Blöcke) kürzen – die helfen beim Lesen nicht.</summary>
    private static string ShortenChunks(string line) =>
        line.Length > 300 ? line[..300] + $" … ({line.Length} Zeichen)" : line;

    private static string Anonymize(string text)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) text = text.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length >= 3) text = text.Replace(@"\" + user + @"\", @"\%USERNAME%\", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
