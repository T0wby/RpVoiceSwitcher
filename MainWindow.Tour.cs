using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Rundgang durch die App: hebt Schritt für Schritt die echten Bereiche hervor und erklärt sie.
// Start: Hilfe → Rundgang, und einmal automatisch nach dem Einrichtungs-Assistenten.
public partial class MainWindow
{
    private sealed record TourStep(string Title, string Text, Func<FrameworkElement?>? Target = null, int? Tab = null);

    private TourAdorner? _tour;
    private int _tourIndex;
    private List<TourStep>? _tourSteps;

    private List<TourStep> BuildTourSteps() =>
    [
        new("Willkommen bei RP Voice Switcher",
            "Dieser kurze Rundgang zeigt dir alles Wichtige – in gut einer Minute. Mit „Weiter“ oder der Pfeiltaste → geht es voran, " +
            "Esc beendet ihn jederzeit. Du findest ihn später unter Hilfe → Rundgang."),
        new("Charaktere",
            "Jeder Charakter hat eigene Stimmen, eigene Sounds und eigene Shortcuts – beim Wechsel sind nur seine Tasten belegt. " +
            "Klick auf einen Namen wechselt (auch über das Menü „Charakter“ oder das Tray-Symbol). „+ Neu“ legt einen Charakter an.",
            () => ProfileBar, 0),
        new("Stimmen",
            "Das sind die Stimmen des aktiven Charakters. Die orange umrandete ist gerade an. „Aktivieren“ schaltet um, die kleinen Knöpfe " +
            "bearbeiten (Name, Beschreibung, Lautstärke), kopieren, exportieren oder löschen eine Stimme.",
            () => VoiceCard(), 0),
        new("Shortcuts: Umschalten und Halten",
            "„Umschalten“: einmal drücken, die Stimme bleibt an. „Halten“: nur solange die Taste gedrückt ist – ideal für kurze Funksprüche " +
            "oder ein Flüstern. Zum Belegen auf den Knopf klicken und die Kombination drücken, Entf löscht. Die Tasten wirken überall, auch im Spiel.",
            () => VoiceCard(1), 0),
        new("Stimmen verwalten",
            "„+ Neue Stimme“ legt eine eigene an – Stimm-Dateien kannst du auch einfach ins Fenster ziehen. „Stimmen auswählen“ legt fest, " +
            "welche zum Charakter gehören. „Probehören“: kurz sprechen und normal mit Effekt vergleichen, die App misst dabei auch den " +
            "Lautstärke-Unterschied. Im EAPO-Editor baust du z. B. Graillon für Tonhöhe und Formanten ein.",
            () => VoiceToolbar, 0),
        new("Statusleiste",
            "Links siehst du Charakter, aktive Stimme und das Gerät, auf das die Stimmen wirken. Rechts das Soundboard und die Einrichtung: " +
            "Steht dort ein Hinweis, klick ihn an – die Einrichtung prüft die ganze Kette (Mikrofon → Kabel → Spiel) und behebt vieles per Klick.",
            () => MainStatusBar),
        new("Soundboard",
            "Sounds hören die anderen über dein Mikrofon. „+ Sounds hinzufügen“ – oder Audiodateien einfach hierher ziehen. „Sounds auswählen“ " +
            "legt fest, welche Sounds zu diesem Charakter gehören. „Alle stoppen“ (Strg+.) beendet alles sofort.",
            () => SoundToolbar, 1),
        new("Lautstärke",
            "„Lautstärke für andere“ geht bis 200 % – kommt im Sprachchat zu wenig an, hier hochdrehen (ein Begrenzer verhindert Übersteuern). " +
            "„Selbst mithören“ spielt die Sounds zusätzlich auf deinen Kopfhörern. „Lautstärke angleichen“ bringt jeden Sound auf volle Lautstärke.",
            () => SoundVolumeRows, 1),
        new("Ein Sound",
            "▶ spielt ab, derselbe Knopf oder Shortcut stoppt wieder. „Wiederholen“: einmal, mehrmals oder als Endlos-Loop (z. B. Regen). " +
            "„Durch Stimmeffekt“: Der Sound klingt dann wie deine Stimme, also etwa gedämpft unter der Maske. Darunter liegt sein Shortcut.",
            () => SoundCard(), 1),
        new("Sprachtaste im Spiel und Dauersenden",
            "Spiele mit Push-to-Talk (z. B. nanos world) senden nur, solange die Sprechtaste gedrückt ist. Die App drückt sie bei Sounds " +
            "automatisch – und „Dauersenden“ hält sie dauerhaft wie ein offenes Mikrofon, aber nur solange das Spiel vorne ist. " +
            "Taste und Umschalt-Shortcut stellst du unter „Sprachtaste einstellen …“ ein.",
            () => PttRow, 1),
        new("Extras",
            "Hier findest du die Einrichtung und den Assistenten, die Einstellungen (Notfall-Taste, Status-Anzeige, Geräte, Ordner, OBS, Updates), " +
            "Probehören und die Notfall-Taste: Sie schaltet sofort auf „Normal“ und stoppt alle Sounds – in jedem Charakter (Standard: Pause).",
            () => ExtrasMenu, 0),
        new("Hilfe",
            "Kurzanleitung (F1), „Problem melden“ (kopiert eine Diagnose für dich), „Was ist neu“ – und dieser Rundgang.",
            () => HelpMenu),
        new("Fertig!",
            "Noch ein Tipp: Fenster schließen (Strg+W) lässt die App im Infobereich weiterlaufen – die Shortcuts bleiben aktiv, " +
            "die kleine Status-Anzeige zeigt im Spiel, welche Stimme an ist. Viel Spaß beim Spielen!"),
    ];

    /// <summary>Karte einer Stimme (bevorzugt die erste Effekt-Stimme).</summary>
    private FrameworkElement? VoiceCard(int fallbackIndex = 1)
    {
        if (VoiceList.Items.Count == 0) return VoiceList;
        int i = Math.Min(fallbackIndex, VoiceList.Items.Count - 1);
        return VoiceList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement ?? VoiceList;
    }

    private FrameworkElement? SoundCard()
    {
        if (SoundList.Items.Count == 0) return SoundList;
        return SoundList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement ?? SoundList;
    }

    private void StartTour()
    {
        if (_tour != null) { ShowTourStep(0); return; }
        if (!IsVisible) ShowFromTray();
        CancelCapture();
        var host = (UIElement)Content;
        var layer = AdornerLayer.GetAdornerLayer(host);
        if (layer == null) return;
        _tourSteps = BuildTourSteps();
        _tour = new TourAdorner(host);
        _tour.NextClicked += () => { if (_tourIndex >= _tourSteps.Count - 1) EndTour(); else ShowTourStep(_tourIndex + 1); };
        _tour.BackClicked += () => ShowTourStep(_tourIndex - 1);
        _tour.CloseClicked += EndTour;
        layer.Add(_tour);
        ShowTourStep(0);
        if (!_settings.TourSeen) { _settings.TourSeen = true; _settings.Save(); }
    }

    private void ShowTourStep(int index)
    {
        if (_tour == null || _tourSteps == null) return;
        _tourIndex = Math.Clamp(index, 0, _tourSteps.Count - 1);
        var step = _tourSteps[_tourIndex];
        if (step.Tab is int tab && Tabs.SelectedIndex != tab) Tabs.SelectedIndex = tab;
        _tour.HoleProvider = null;
        _tour.SetStep(step.Title, step.Text, _tourIndex, _tourSteps.Count);
        // Nach dem Tab-Wechsel erst das Layout abwarten, dann den Bereich ins Bild holen und hervorheben
        Dispatcher.InvokeAsync(() =>
        {
            if (_tour == null || step.Target?.Invoke() is not FrameworkElement target) { _tour?.InvalidateArrange(); return; }
            target.BringIntoView();
            Dispatcher.InvokeAsync(() =>
            {
                if (_tour == null) return;
                var host = (UIElement)Content;
                _tour.HoleProvider = () =>
                {
                    var t = step.Target?.Invoke();
                    if (t == null || !t.IsVisible) return null;
                    return t.TransformToAncestor(host).TransformBounds(new Rect(t.RenderSize));
                };
                _tour.InvalidateArrange();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

    private void EndTour()
    {
        if (_tour == null) return;
        AdornerLayer.GetAdornerLayer((UIElement)Content)?.Remove(_tour);
        _tour = null;
        _tourSteps = null;
        Tabs.SelectedIndex = 0;
        ShowStatusMessage("Rundgang beendet – jederzeit wieder unter Hilfe → Rundgang.");
    }

    /// <summary>Tasten während des Rundgangs: → / Enter weiter, ← zurück, Esc beenden. Liefert true, wenn behandelt.</summary>
    private bool HandleTourKey(KeyEventArgs e)
    {
        if (_tour == null || _tourSteps == null) return false;
        switch (e.Key)
        {
            case Key.Right: case Key.Enter: case Key.Space:
                if (_tourIndex >= _tourSteps.Count - 1) EndTour(); else ShowTourStep(_tourIndex + 1);
                return true;
            case Key.Left: case Key.Back:
                ShowTourStep(_tourIndex - 1);
                return true;
            case Key.Escape:
                EndTour();
                return true;
        }
        return false;
    }

    /// <summary>Nach dem Einrichtungs-Assistenten einmal anbieten (nicht in Testläufen).</summary>
    private void MaybeStartTourAfterWizard()
    {
        if (_settings.TourSeen || IsTestRun) return;
        Dispatcher.InvokeAsync(StartTour, DispatcherPriority.Background);
    }
}
