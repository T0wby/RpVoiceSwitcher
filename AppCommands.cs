using System.Windows.Input;

namespace RpVoiceSwitcher;

/// <summary>
/// Befehle für Menü und Tastenkürzel innerhalb der App (wirken nur, wenn das Fenster aktiv ist –
/// im Gegensatz zu den globalen Stimmen-Shortcuts). Die Kürzeltexte stehen auf Deutsch, so wie Windows sie anzeigt.
/// </summary>
public static class AppCommands
{
    private static RoutedUICommand C(string text, string name, params InputGesture[] gestures) =>
        new(text, name, typeof(AppCommands), new InputGestureCollection(gestures));

    /// <summary>Tastenkürzel mit deutschem Anzeigetext (WPF würde sonst „Ctrl+OemPeriod“ anzeigen).</summary>
    private static KeyGesture K(Key key, ModifierKeys mods, string display) => new(key, mods, display);

    private const ModifierKeys Ctrl = ModifierKeys.Control, Shift = ModifierKeys.Shift, None = ModifierKeys.None;

    // Datei
    public static readonly RoutedUICommand NewVoice = C("_Neue Stimme …", nameof(NewVoice), K(Key.N, Ctrl, "Strg+N"));
    public static readonly RoutedUICommand ImportVoice = C("Stimme _importieren …", nameof(ImportVoice), K(Key.O, Ctrl, "Strg+O"));
    public static readonly RoutedUICommand ExportVoices = C("Stimmen e_xportieren …", nameof(ExportVoices), K(Key.E, Ctrl, "Strg+E"));
    public static readonly RoutedUICommand AddSounds = C("_Sounds hinzufügen …", nameof(AddSounds), K(Key.O, Ctrl | Shift, "Strg+Umschalt+O"));
    public static readonly RoutedUICommand CloseWindow = C("Fenster _schließen", nameof(CloseWindow), K(Key.W, Ctrl, "Strg+W"));
    public static readonly RoutedUICommand Exit = C("_Beenden", nameof(Exit), K(Key.Q, Ctrl, "Strg+Q"));

    // Charakter
    public static readonly RoutedUICommand NewProfile = C("_Neuer Charakter …", nameof(NewProfile), K(Key.N, Ctrl | Shift, "Strg+Umschalt+N"));
    public static readonly RoutedUICommand RenameProfile = C("_Umbenennen …", nameof(RenameProfile), K(Key.F2, None, "F2"));
    public static readonly RoutedUICommand DeleteProfile = C("_Löschen …", nameof(DeleteProfile));
    public static readonly RoutedUICommand SelectVoices = C("Stimmen _auswählen", nameof(SelectVoices));
    public static readonly RoutedUICommand ResetHotkeys = C("Shortcuts _zurücksetzen …", nameof(ResetHotkeys));

    // Ansicht
    public static readonly RoutedUICommand ShowVoices = C("_Stimmen", nameof(ShowVoices), K(Key.D1, Ctrl, "Strg+1"));
    public static readonly RoutedUICommand ShowSoundboard = C("S_oundboard", nameof(ShowSoundboard), K(Key.D2, Ctrl, "Strg+2"));
    public static readonly RoutedUICommand Refresh = C("_Aktualisieren", nameof(Refresh), K(Key.F5, None, "F5"));

    // Extras
    public static readonly RoutedUICommand ShowSetup = C("_Einrichtung …", nameof(ShowSetup), K(Key.E, Ctrl | Shift, "Strg+Umschalt+E"));
    public static readonly RoutedUICommand SetupWizard = C("Einrichtungs-_Assistent …", nameof(SetupWizard));
    public static readonly RoutedUICommand ShowSettings = C("Ein_stellungen …", nameof(ShowSettings), K(Key.OemComma, Ctrl, "Strg+,"));
    public static readonly RoutedUICommand VoiceTest = C("_Probehören …", nameof(VoiceTest), K(Key.P, Ctrl, "Strg+P"));
    public static readonly RoutedUICommand Panic = C("_Notfall: normale Stimme, Sounds aus", nameof(Panic));
    public static readonly RoutedUICommand TogglePttLatch = C("_Dauersenden an/aus (Sprachtaste halten)", nameof(TogglePttLatch));
    public static readonly RoutedUICommand StopSounds = C("Alle Sounds _stoppen", nameof(StopSounds), K(Key.OemPeriod, Ctrl, "Strg+."));
    public static readonly RoutedUICommand OpenEditor = C("Equalizer-APO-_Editor", nameof(OpenEditor));
    public static readonly RoutedUICommand OpenDeviceSelector = C("Equalizer-APO-_Device-Selector", nameof(OpenDeviceSelector));
    public static readonly RoutedUICommand OpenRecording = C("Windows-_Aufnahmegeräte", nameof(OpenRecording));
    public static readonly RoutedUICommand OpenPlayback = C("Windows-_Wiedergabegeräte", nameof(OpenPlayback));

    // Hilfe
    public static readonly RoutedUICommand Help = C("_Kurzanleitung", nameof(Help), K(Key.F1, None, "F1"));
    public static readonly RoutedUICommand WhatsNew = C("_Was ist neu (alle Versionen) …", nameof(WhatsNew));
    public static readonly RoutedUICommand ReportProblem = C("_Problem melden …", nameof(ReportProblem));
    public static readonly RoutedUICommand CopyDiagnostics = C("_Diagnose kopieren", nameof(CopyDiagnostics));
    public static readonly RoutedUICommand CheckUpdates = C("Nach _Updates suchen …", nameof(CheckUpdates));
    public static readonly RoutedUICommand About = C("_Info über RP Voice Switcher", nameof(About));
}
