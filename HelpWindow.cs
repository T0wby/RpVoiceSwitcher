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
            "Ob alles richtig eingerichtet ist, zeigt der Tab „Einrichtung“ (Strg+3) – rote Punkte lassen sich dort meist per Klick beheben.",
        ]),
        ("Stimmen",
        [
            "Umschalten: Shortcut einmal drücken – die Stimme bleibt an, bis du eine andere wählst.",
            "Halten: Solange die Taste gedrückt ist, ist die Stimme aktiv. Beim Loslassen geht es zur vorherigen zurück. Ideal für schnelle Wechsel mitten im Satz.",
            "Shortcut ändern: auf das Feld klicken und die Tasten drücken. Entf löscht, Esc bricht ab. Ziffern gelten für beide Zahlenreihen (NumLock an).",
            "Neue Stimme (Strg+N), bearbeiten, kopieren und löschen geht direkt an der Stimme. Für die Tonhöhe Graillon im EAPO-Editor einbauen.",
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
            "Alle Sounds stoppen: Strg+. oder der Button im Soundboard.",
        ]),
        ("Tipps",
        [
            "Status-Anzeige und Einblendung sind im Fenster- oder Randlos-Modus von Spielen sichtbar, nicht im exklusiven Vollbild.",
            "Fenster schließen (Strg+W) lässt die App im Infobereich weiterlaufen – die Shortcuts bleiben aktiv. Beenden: Strg+Q.",
            "Starke Rauschunterdrückung (z. B. Krisp in Discord) kann Effekte abschwächen.",
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
