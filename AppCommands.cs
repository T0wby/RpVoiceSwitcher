using System.Windows.Input;

namespace RpVoiceSwitcher;

/// <summary>
/// Befehle für Menü und Tastenkürzel innerhalb der App (wirken nur, wenn das Fenster aktiv ist –
/// im Gegensatz zu den globalen Stimmen-Shortcuts). Die Kürzeltexte zeigt das Menü automatisch an ("Strg+N" …).
/// </summary>
public static class AppCommands
{
    private static RoutedUICommand C(string text, string name, params InputGesture[] gestures) =>
        new(text, name, typeof(AppCommands), new InputGestureCollection(gestures));

    private static KeyGesture K(Key key, ModifierKeys mods = ModifierKeys.None) => new(key, mods);

    // Datei
    public static readonly RoutedUICommand NewVoice = C("_Neue Stimme …", nameof(NewVoice), K(Key.N, ModifierKeys.Control));
    public static readonly RoutedUICommand ImportVoice = C("Stimme _importieren …", nameof(ImportVoice), K(Key.O, ModifierKeys.Control));
    public static readonly RoutedUICommand AddSounds = C("_Sounds hinzufügen …", nameof(AddSounds), K(Key.O, ModifierKeys.Control | ModifierKeys.Shift));
    public static readonly RoutedUICommand CloseWindow = C("Fenster _schließen", nameof(CloseWindow), K(Key.W, ModifierKeys.Control));
    public static readonly RoutedUICommand Exit = C("_Beenden", nameof(Exit), K(Key.Q, ModifierKeys.Control));

    // Charakter
    public static readonly RoutedUICommand NewProfile = C("_Neuer Charakter …", nameof(NewProfile), K(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
    public static readonly RoutedUICommand RenameProfile = C("_Umbenennen …", nameof(RenameProfile), K(Key.F2));
    public static readonly RoutedUICommand DeleteProfile = C("_Löschen …", nameof(DeleteProfile));
    public static readonly RoutedUICommand SelectVoices = C("Stimmen _auswählen", nameof(SelectVoices));
    public static readonly RoutedUICommand ResetHotkeys = C("Shortcuts _zurücksetzen …", nameof(ResetHotkeys));

    // Ansicht
    public static readonly RoutedUICommand ShowVoices = C("_Stimmen", nameof(ShowVoices), K(Key.D1, ModifierKeys.Control));
    public static readonly RoutedUICommand ShowSoundboard = C("S_oundboard", nameof(ShowSoundboard), K(Key.D2, ModifierKeys.Control));
    public static readonly RoutedUICommand ShowSetup = C("_Einrichtung", nameof(ShowSetup), K(Key.D3, ModifierKeys.Control));
    public static readonly RoutedUICommand ShowSettings = C("Ein_stellungen", nameof(ShowSettings), K(Key.D4, ModifierKeys.Control), K(Key.OemComma, ModifierKeys.Control));
    public static readonly RoutedUICommand Refresh = C("_Aktualisieren", nameof(Refresh), K(Key.F5));

    // Extras
    public static readonly RoutedUICommand StopSounds = C("Alle Sounds _stoppen", nameof(StopSounds), K(Key.OemPeriod, ModifierKeys.Control));
    public static readonly RoutedUICommand OpenEditor = C("Equalizer-APO-_Editor", nameof(OpenEditor));
    public static readonly RoutedUICommand OpenDeviceSelector = C("Equalizer-APO-_Device-Selector", nameof(OpenDeviceSelector));
    public static readonly RoutedUICommand OpenRecording = C("Windows-_Aufnahmegeräte", nameof(OpenRecording));
    public static readonly RoutedUICommand OpenPlayback = C("Windows-_Wiedergabegeräte", nameof(OpenPlayback));

    // Hilfe
    public static readonly RoutedUICommand Help = C("_Kurzanleitung", nameof(Help), K(Key.F1));
    public static readonly RoutedUICommand About = C("_Info über RP Voice Switcher", nameof(About));
}
