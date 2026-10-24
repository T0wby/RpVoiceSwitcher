using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RpVoiceSwitcher;

/// <summary>Kurzanleitung (F1). Nicht modal, damit man sie neben der App offen lassen kann.</summary>
public sealed class HelpWindow : Window
{
    private static HelpWindow? _open;

    private static readonly (string Title, string[] Lines)[] Sections =
    [
        ("So funktioniert's",
        [
            "Dein Mikrofon wird über „Abhören“ in das virtuelle Kabel (VB-CABLE) geleitet. Equalizer APO legt dort die aktive Stimme drauf.",
            "Im Spiel und in Discord wählst du deshalb „CABLE Output“ als Mikrofon.",
            "Neu hier? Hilfe → Einrichtungs-Assistent führt von null durch Downloads, Installation und Windows-Einstellungen.",
            "Ob alles richtig eingerichtet ist, zeigt Extras → Einrichtung (Strg+Umschalt+E) oder ein Klick unten rechts in der Statusleiste – rote Punkte lassen sich dort meist per Klick beheben.",
            "Einstellungen (Ton, Status-Anzeige, Geräte, Ordner) findest du unter Extras → Einstellungen (Strg+,).",
        ]),
        ("Stimmen",
        [
            "Umschalten: Shortcut einmal drücken – die Stimme bleibt an, bis du eine andere wählst.",
            "Halten: Solange die Taste gedrückt ist, ist die Stimme aktiv. Beim Loslassen geht es zur vorherigen zurück. Ideal für schnelle Wechsel mitten im Satz.",
            "Shortcut ändern: auf das Feld klicken und die Tasten drücken. Entf löscht, Esc bricht ab. Ziffern gelten für beide Zahlenreihen (NumLock an).",
            "Neue Stimme (Strg+N), bearbeiten, kopieren, exportieren und löschen geht direkt an der Stimme. Für die Tonhöhe Graillon im EAPO-Editor einbauen.",
            "Lautstärke: Ist eine Stimme leiser oder lauter als deine normale, gleicht der Regler beim Bearbeiten (✎) das aus. Extras → Probehören (Strg+P) misst den Unterschied und stellt ihn auf Wunsch direkt ein.",
            "Weitergeben: Datei → Stimmen exportieren (Strg+E) packt die Stimmen des Charakters in eine ZIP. Importieren (Strg+O) nimmt .txt und .zip.",
        ]),
        ("Notfall-Taste",
        [
            "Standard: Pause. Gilt in jedem Charakter – sofort normale Stimme, alle Sounds aus, eine gehaltene Stimme wird beendet.",
            "Ändern unter Extras → Einstellungen. Auch im Tray-Menü und unter Extras zu finden.",
        ]),
        ("Charaktere",
        [
            "Jeder Charakter hat seine eigene Stimmenauswahl und eigene Shortcuts. Beim Wechsel sind nur die Tasten des aktiven Charakters belegt.",
            "Wechseln: oben auf den Namen klicken, über das Menü „Charakter“ oder per Rechtsklick auf das Tray-Symbol.",
        ]),
        ("Soundboard",
        [
            "Sounds hören die anderen über dein Mikrofon. „Selbst mithören“ spielt sie zusätzlich auf deinen Kopfhörern.",
            "„Durch Stimmeffekt“: an = der Sound klingt wie deine Stimme (z. B. gedämpft unter der Maske), aus = die anderen hören ihn unverändert.",
            "„Wiederholen“: einmal, mehrmals hintereinander oder endlos (Loop, z. B. für Hintergrundgeräusche).",
            "Läuft ein Sound, stoppt ihn derselbe Shortcut oder ein Klick auf „■ Stoppen“. Fortschritt und Zeit siehst du an der Karte und in der Status-Anzeige.",
            "Alle Sounds stoppen: Strg+. oder der Button im Soundboard. Die Notfall-Taste stoppt auch Loops.",
            "Dauersenden: Für Spiele, die nur Push-to-Talk können (z. B. nanos world). „Dauersenden“ hält die Sprachtaste dauerhaft gedrückt – wie ein offenes Mikrofon. Sprachtaste und Umschalt-Taste stellst du unter Einstellungen → „Sprachtaste im Spiel“ ein. Umschalten per Knopf im Soundboard, Extras-Menü, Tray oder eigener Taste (gilt in allen Charakteren). Die Notfall-Taste schaltet es auch aus. Drückst du die Sprachtaste währenddessen selbst, läuft Dauersenden nach dem Loslassen einfach weiter.",
            "Offenes Mikrofon (zweiter Weg): Stellst du im Spiel den Sprachchat auf offenes Mikrofon (nanos world: Einstellungen → Audio → „Open Mic“), sendet das Spiel selbst dauerhaft – auch alle Sounds. Dann unter Einstellungen → „Sprachtaste im Spiel“ „Offenes Mikrofon“ wählen: Die App hält keine Taste mehr, die Umschalt-Taste tippt die Sprachtaste nur kurz an und schaltet dich im Spiel stumm bzw. wieder an. Den echten Zustand im Spiel kann die App nicht auslesen – sie zählt mit, auch wenn du die Taste selbst drückst. Stimmt die Anzeige nicht, im Soundboard auf ⇄ klicken (dreht nur die Anzeige um).",
            "Kommen Sounds im Spiel nicht an? 1) „Lautstärke für andere“ hochdrehen (bis 200 %, ein Begrenzer verhindert Übersteuern) und „Lautstärke angleichen“ anlassen. 2) Nutzt das Spiel Push-to-Talk (Sprechen nur mit gedrückter Taste, z. B. nanos world), unter Einstellungen → „Sprachtaste im Spiel“ dieselbe Taste wählen (auch Maus 4/5) und „Beim Abspielen von Sounds automatisch drücken“ anhaken. 3) Im Spiel muss CABLE Output als Mikrofon eingestellt sein; Rauschunterdrückung dort möglichst aus.",
        ]),
        ("Tipps",
        [
            "Status-Anzeige und Einblendung sind im Fenster- oder Randlos-Modus von Spielen sichtbar, nicht im exklusiven Vollbild. Größe, Deckkraft und „nur mit Effekt zeigen“ stellst du in den Einstellungen ein.",
            "Streamer: In den Einstellungen unter „Stream (OBS)“ schreibt die App die aktive Stimme in eine Textdatei – in OBS als „Text (GDI+)“ → „Aus Datei lesen“ einbinden.",
            "Updates: Die App sucht beim Start nach neuen Versionen (abschaltbar). Von Hand: Hilfe → Nach Updates suchen.",
            "Fenster schließen (Strg+W) lässt die App im Infobereich weiterlaufen – die Shortcuts bleiben aktiv. Beenden: Strg+Q.",
            "Starke Rauschunterdrückung (z. B. Krisp in Discord) kann Effekte abschwächen.",
            "Kein Effekt im Steam-Sprachchat (z. B. nanos world)? Steam nimmt das Windows-Standardmikrofon. Extras → Einrichtung stellt „CABLE Output“ per Klick als Standard ein; springt es zurück, unter Einstellungen → Geräte „Kabel als Windows-Standardmikrofon halten“ anhaken.",
            "Kein Effekt im Spiel, obwohl die Stimme aktiv ist? Dann hat das Spiel das Kabel vermutlich „exklusiv“ geöffnet – Extras → Einrichtung schaltet den Exklusivmodus ab.",
            "Klappt etwas nicht: Hilfe → Problem melden kopiert eine Diagnose und öffnet GitHub zum Einfügen.",
        ]),
    ];

    private HelpWindow()
    {
        Title = "Kurzanleitung – RP Voice Switcher";
        Width = 620;
        Height = 640;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowTheme.Apply(this);
        var dim = (Brush)Application.Current.Resources["FgDim"];

        var stack = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        foreach (var (title, lines) in Sections)
        {
            stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 16, 0, 6) });
            foreach (var line in lines)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                var bullet = new TextBlock { Text = "•", Foreground = (Brush)Application.Current.Resources["Accent"], Margin = new Thickness(2, 0, 10, 0) };
                DockPanel.SetDock(bullet, Dock.Left);
                row.Children.Add(bullet);
                row.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Foreground = dim, LineHeight = 19 });
                stack.Children.Add(row);
            }
        }

        var close = new Button { Content = "Schließen", IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        var footer = new Border { Padding = new Thickness(24, 10, 24, 14), Child = close };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack, Focusable = false });
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.F1) e.Handled = true; };
        Closed += (_, _) => _open = null;
    }

    public static void ShowFor(Window owner)
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new HelpWindow { Owner = owner.IsVisible ? owner : null };
        _open.Show();
    }
}
