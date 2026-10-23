using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

/// <summary>
/// Einrichtungs-Assistent für Nutzer, die noch gar nichts haben: führt Schritt für Schritt durch Downloads,
/// Installation, Neustart und die Windows-Einstellungen. Jeder Schritt prüft selbst, ob er erledigt ist.
/// </summary>
public sealed class SetupWizardWindow : Window
{
    public enum StepState { Done, Open, Info, Optional }

    /// <summary>Was der Assistent von der App braucht.</summary>
    public sealed record Host(
        Func<SetupChecker.Status> Probe,
        Func<string?> AutoSetup,                 // Schreibrechte + Stimmen einrichten, liefert Fehlermeldung oder null
        Action OpenDeviceSelector,
        Action ScheduleReopenAndRestart,
        Action ActivateDemoVoice,
        Action ActivateNormal,
        Action OpenVoiceTest,
        Action OpenEditor,
        Func<string> DemoVoiceName,
        Func<int> GetStep,
        Action<int> SetStep,
        Action Finish);

    private sealed record StepAction(string Label, Action Run, string? Tooltip = null);

    private sealed record Step(
        string Title,
        string Intro,
        string[] Instructions,
        Func<SetupChecker.Status, (StepState State, string Detail)> Evaluate,
        StepAction[] Actions,
        string? Tip = null);

    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6B));
    private static readonly Brush OpenBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x28));
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x9C, 0xE8));

    private readonly Host _host;
    private readonly List<Step> _steps;
    private readonly StackPanel _stepList = new();
    private readonly TextBlock _header = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _counter = new() { FontSize = 12 };
    private readonly Border _badge = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _badgeText = new() { FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _body = new();
    private readonly Button _back = new() { Content = "Zurück", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _next = new() { Content = "Weiter", MinWidth = 100, IsDefault = true };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private int _index;
    private SetupChecker.Status _status = new();

    public SetupWizardWindow(Window owner, Host host)
    {
        _host = host;
        Owner = owner.IsVisible ? owner : null;
        Title = "Einrichtungs-Assistent – RP Voice Switcher";
        Width = 900;
        Height = 660;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Language = owner.Language;
        WindowTheme.Apply(this);
        var dim = (Brush)Application.Current.Resources["FgDim"];
        _counter.Foreground = dim;

        _steps = BuildSteps();

        // Links: Schrittliste
        var listScroll = new ScrollViewer { Content = _stepList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var left = new Border
        {
            Background = (Brush)Application.Current.Resources["Card"],
            Padding = new Thickness(10, 14, 10, 14),
            Width = 260,
            Child = listScroll,
        };

        // Rechts: aktueller Schritt
        _badge.Child = _badgeText;
        var content = new StackPanel { Margin = new Thickness(28, 22, 28, 18) };
        content.Children.Add(_counter);
        content.Children.Add(_header);
        content.Children.Add(_badge);
        content.Children.Add(_detail);
        content.Children.Add(_body);
        var right = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

        var later = new Button { Content = "Später weitermachen", MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Schließen – beim nächsten Öffnen geht es an dieser Stelle weiter" };
        later.Click += (_, _) => Close();
        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) =>
        {
            if (_index == _steps.Count - 1) { _host.Finish(); Close(); }
            else Go(_index + 1);
        };
        var navButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        navButtons.Children.Add(_back);
        navButtons.Children.Add(_next);
        var footerGrid = new Grid();
        footerGrid.Children.Add(later);
        footerGrid.Children.Add(navButtons);
        var footer = new Border
        {
            Child = footerGrid,
            Padding = new Thickness(20, 10, 20, 12),
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };

        var main = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(left, Dock.Left);
        main.Children.Add(footer);
        main.Children.Add(left);
        main.Children.Add(right);
        Content = main;

        _refresh.Tick += (_, _) => Refresh();
        Activated += (_, _) => Refresh(); // z. B. nach dem Zurückkommen aus den Windows-Soundeinstellungen
        Loaded += (_, _) => { Refresh(); _refresh.Start(); };
        Closed += (_, _) => _refresh.Stop();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        _index = Math.Clamp(_host.GetStep(), 0, _steps.Count - 1);
        _status = _host.Probe();
        Render();
    }

    // ---------- Inhalt ----------

    private List<Step> BuildSteps() =>
    [
        new("Willkommen",
            "Dieser Assistent führt dich von null bis zur ersten verstellten Stimme im Spiel. Dauer: etwa 15 Minuten, dazu einmal Windows neu starten.",
            [
                "Du brauchst: dein Mikrofon bzw. Headset (angeschlossen), Internet und Administratorrechte für zwei kleine Installationen.",
                "So funktioniert es: Windows leitet dein Mikrofon in ein virtuelles Kabel (VB-CABLE). Equalizer APO legt dort den Stimmeffekt drauf. Im Spiel und in Discord wählst du das Kabel als Mikrofon – und RP Voice Switcher schaltet per Tastendruck zwischen den Stimmen um.",
                "Jeder Schritt prüft selbst, ob er erledigt ist – dann erscheint ein grüner Haken. Du kannst jederzeit aufhören: Beim nächsten Öffnen geht es an derselben Stelle weiter.",
            ],
            _ => (StepState.Info, ""),
            []),

        new("Virtuelles Kabel (VB-CABLE)",
            "Das kostenlose VB-CABLE ist ein unsichtbares Audiokabel: Was hineingeht („CABLE Input“), kommt als Mikrofon wieder heraus („CABLE Output“).",
            [
                "Auf „Download-Seite öffnen“ klicken und dort das „VB-CABLE Driver Pack“ für Windows herunterladen (ZIP-Datei).",
                "Die ZIP-Datei per Rechtsklick → „Alle extrahieren …“ in einen Ordner entpacken.",
                "Im entpackten Ordner „VBCABLE_Setup_x64.exe“ mit Rechtsklick → „Als Administrator ausführen“ starten.",
                "„Install Driver“ klicken und bestätigen. Fertig – den Neustart machen wir gesammelt in Schritt 5.",
            ],
            s => s.CableInstalled
                ? (StepState.Done, "Gefunden: „CABLE Input“ und „CABLE Output“.")
                : (StepState.Open, "Noch nicht gefunden. Nach der Installation erscheint hier automatisch ein Haken (kann ein paar Sekunden dauern)."),
            [new("Download-Seite öffnen", () => SetupChecker.OpenUrl(SetupChecker.CableUrl))]),

        new("Equalizer APO installieren",
            "Equalizer APO legt die Effekte auf das Kabel. Wichtig ist, dass es am richtigen Gerät hängt: an „CABLE Output“.",
            [
                "Auf „Download-Seite öffnen“ klicken und bei SourceForge den grünen „Download“-Button nehmen.",
                "Den Installer (EqualizerAPO-x64-…exe) starten und durchklicken.",
                "Am Ende öffnet sich der „Configurator“: Im Reiter „Capture devices“ (Aufnahmegeräte) NUR „CABLE Output“ anhaken. Im Reiter „Playback devices“ nichts anhaken.",
                "Mit „OK“ bestätigen. Fragt er nach einem Neustart: „Nein“ bzw. später – erst Schritt 4 erledigen.",
            ],
            s => !s.CableInstalled && !s.EapoInstalled
                    ? (StepState.Open, "Bitte zuerst Schritt 2 (VB-CABLE) – sonst gibt es „CABLE Output“ im Configurator noch nicht.")
                : !s.EapoInstalled
                    ? (StepState.Open, "Equalizer APO ist noch nicht installiert.")
                : !s.EapoOnTarget
                    ? (StepState.Open, "Equalizer APO ist installiert, hängt aber noch nicht an „CABLE Output“. „Device Selector öffnen“, im Reiter „Capture devices“ „CABLE Output“ anhaken, OK.")
                    : (StepState.Done, $"Installiert und an „{s.TargetName}“ eingerichtet."),
            [
                new("Download-Seite öffnen", () => SetupChecker.OpenUrl(SetupChecker.EapoUrl)),
                new("Device Selector öffnen", () => _host.OpenDeviceSelector(), "Falls du im Configurator das falsche Gerät angehakt hast"),
            ]),

        new("Zugriff für die App freigeben",
            "Equalizer APO liegt im Ordner „Programme“. Damit RP Voice Switcher dort die Stimmen umschalten darf, braucht es einmalig Schreibrechte. Ein Klick erledigt alles – Windows fragt dabei nach Administratorrechten.",
            [
                "„Automatisch einrichten“ klicken und die Windows-Abfrage mit „Ja“ bestätigen.",
                "Dabei werden auch die mitgelieferten Stimmen (Maske, Funkgerät, Megafon …) eingerichtet.",
            ],
            s => !s.EapoInstalled
                    ? (StepState.Open, "Erst Equalizer APO installieren (Schritt 3).")
                : s.ConfigWritable && s.IncludeOk
                    ? (StepState.Done, "Freigegeben, die Stimmen sind eingerichtet.")
                    : (StepState.Open, s.ConfigWritable ? "Rechte sind da, die Stimmen fehlen noch – „Automatisch einrichten“ klicken." : "Noch keine Schreibrechte."),
            [new("Automatisch einrichten", AutoSetup)]),

        new("Windows neu starten",
            "Equalizer APO wird erst nach einem Neustart aktiv. Wichtig: „Neu starten“ wählen, nicht „Herunterfahren“ – beim Herunterfahren startet Windows wegen des Schnellstarts nicht wirklich neu.",
            [
                "Alles speichern, was offen ist.",
                "„Jetzt neu starten“ klicken. RP Voice Switcher öffnet sich nach dem Neustart von selbst, und der Assistent macht hier weiter.",
            ],
            s => !s.EapoOnTarget
                    ? (StepState.Open, "Erst Schritt 3 abschließen.")
                : s.RestartPending
                    ? (StepState.Open, "Neustart steht noch aus.")
                    : (StepState.Done, "Equalizer APO ist aktiv."),
            [new("Jetzt neu starten", Restart)]),

        new("Mikrofon ins Kabel leiten",
            "Jetzt kommt deine Stimme ins Kabel. Dafür nutzt Windows die Funktion „Abhören“.",
            [
                "„Aufnahmegeräte öffnen“ klicken.",
                "Dein echtes Mikrofon doppelklicken (z. B. Headset oder USB-Mikrofon – nicht „CABLE Output“).",
                "Reiter „Abhören“: Haken bei „Dieses Gerät als Wiedergabequelle verwenden“.",
                "Bei „Wiedergabe über dieses Gerät“ „CABLE Input (VB-Audio Virtual Cable)“ auswählen.",
                "„OK“ klicken. Der Schritt hakt sich dann von selbst ab.",
            ],
            s => s.MicToCable != null
                    ? (StepState.Done, $"„{s.MicToCable}“ wird ins Kabel geleitet.")
                : s.MicDisconnected != null
                    ? (StepState.Open, $"„{s.MicDisconnected}“ ist richtig eingestellt, aber gerade nicht angeschlossen.")
                : s.MicListensElsewhere != null
                    ? (StepState.Open, $"„{s.MicListensElsewhere}“ wird abgehört, aber nicht auf „CABLE Input“ – bei „Wiedergabe über dieses Gerät“ das Kabel wählen.")
                    : (StepState.Open, "Noch kein Mikrofon wird ins Kabel geleitet."),
            [new("Aufnahmegeräte öffnen", () => SetupChecker.OpenSoundPanel(1))],
            "Hörst du dich danach doppelt in den Kopfhörern? Dann ist beim Abhören versehentlich der Kopfhörer statt „CABLE Input“ gewählt."),

        new("Exklusivmodus abschalten",
            "Manche Spiele und Voice-Programme übernehmen das Mikrofon „exklusiv“. Dann umgeht Windows Equalizer APO – und der Stimmeffekt ist plötzlich weg. Das verhindert ein Haken an „CABLE Output“.",
            [
                "„Automatisch abschalten“ klicken und die Windows-Abfrage mit „Ja“ bestätigen.",
                "Oder von Hand: „Aufnahmegeräte öffnen“ → „CABLE Output“ doppelklicken → Reiter „Erweitert“ → beide Haken unter „Exklusiver Modus“ entfernen → OK.",
            ],
            s => s.Exclusive == null
                    ? (StepState.Open, "„CABLE Output“ wurde nicht gefunden – erst Schritt 2.")
                : s.Exclusive.Allowed
                    ? (StepState.Open, $"An „{s.Exclusive.Name}“ dürfen Programme noch die alleinige Kontrolle übernehmen.")
                    : (StepState.Done, "Aus – kein Programm kann den Effekt mehr umgehen."),
            [
                new("Automatisch abschalten", DisableExclusive),
                new("Aufnahmegeräte öffnen", () => SetupChecker.OpenSoundPanel(1)),
            ]),

        new("Wiedergabegerät prüfen",
            "Nach der Installation von VB-CABLE stellt Windows manchmal „CABLE Input“ als Lautsprecher ein. Dann hörst du nichts mehr – und die anderen hören deine PC-Sounds.",
            [
                "„Wiedergabegeräte öffnen“ klicken.",
                "Deine Kopfhörer bzw. Lautsprecher anklicken und „Als Standard“ wählen.",
            ],
            s => s.DefaultPlayback == null
                    ? (StepState.Open, "Kein Wiedergabegerät gefunden.")
                : s.DefaultPlaybackIsCable
                    ? (StepState.Open, $"Standard ist gerade „{s.DefaultPlayback}“ – bitte deine Kopfhörer wählen.")
                    : (StepState.Done, $"Standard: „{s.DefaultPlayback}“."),
            [new("Wiedergabegeräte öffnen", () => SetupChecker.OpenSoundPanel(0))]),

        new("Ausprobieren",
            "Schalte eine Stimme ein und hör dir an, wie du bei den anderen klingst.",
            [
                $"„{_host.DemoVoiceName()} einschalten“ klicken.",
                "„Probehören öffnen“ → „Aufnehmen“, ein paar Sätze sprechen → „Abwechselnd“ vergleicht normal und mit Effekt.",
                "Danach „Zurück auf Normal“. Im Spiel schaltest du später per Tastenkombination um (Standard: Strg+Alt+0 bis 9) – im Hauptfenster kannst du sie ändern.",
            ],
            _ => (StepState.Info, ""),
            [
                new($"{_host.DemoVoiceName()} einschalten", () => _host.ActivateDemoVoice()),
                new("Probehören öffnen", () => _host.OpenVoiceTest()),
                new("Zurück auf Normal", () => _host.ActivateNormal()),
            ],
            "Notfall-Taste: Pause schaltet jederzeit sofort auf die normale Stimme und stoppt alle Sounds."),

        new("Im Spiel und in Discord",
            "Zum Schluss sagst du deinen Programmen, dass sie das Kabel als Mikrofon nehmen sollen.",
            [
                "Discord: Einstellungen → Sprache & Video → Eingabegerät „CABLE Output (VB-Audio Virtual Cable)“.",
                "Discord: Rauschunterdrückung auf „Standard“ oder „Keine“ stellen – „Krisp“ bügelt die Effekte sonst glatt.",
                "Im Spiel bzw. RP-Client in den Spracheinstellungen ebenfalls „CABLE Output“ als Mikrofon wählen.",
                "Kann man im Spiel kein Mikrofon wählen (z. B. Steam-Sprachchat, nanos world), nimmt es das Windows-Standardmikrofon – „Kabel als Windows-Standard setzen“ klicken.",
                "Sendet das Spiel nur mit gedrückter Sprechtaste: unter Einstellungen → „Sprachtaste im Spiel“ dieselbe Taste einstellen, dann drückt die App sie auch für Sounds.",
            ],
            s => s.DefaultMic is not { CableId: not null } mic
                    ? (StepState.Info, "")
                : mic.Ok
                    ? (StepState.Done, $"„{mic.CableName}“ ist Windows-Standardmikrofon.")
                    : (StepState.Open, $"Windows-Standardmikrofon ist gerade „{(mic.DefaultIsCable ? mic.CommsName : mic.DefaultName)}“ – Steam & Co. hören dich dann ohne Effekt."),
            [
                new("Kabel als Windows-Standard setzen", () =>
                {
                    if (DefaultMic.SetCableAsDefault() is string error)
                        MessageBox.Show(this, "Umstellen hat nicht geklappt: " + error, "Einrichtungs-Assistent");
                }),
                new("Aufnahmegeräte öffnen", () => SetupChecker.OpenSoundPanel(1)),
            ]),

        new("Optional: Graillon für die Tonhöhe",
            "Für deutlich tiefere oder höhere Stimmen (Riese, Kobold …) gibt es das kostenlose Plugin Graillon. Für Maske, Funkgerät & Co. brauchst du es nicht.",
            [
                "„Download-Seite öffnen“ → „Graillon 3 Free“ herunterladen und die ZIP entpacken.",
                "Den Installer starten und die „VST2“-Version (64-bit) mitinstallieren, z. B. nach C:\\Program Files\\VSTPlugins.",
                "In eine Stimme einbauen: die Stimme aktivieren → „EAPO-Editor öffnen“ → „+“ → Plugins → VST plugin → Graillon-Datei wählen → Pitch und Formant einstellen → speichern.",
            ],
            s => s.Graillon != null
                ? (StepState.Done, "Gefunden: " + s.Graillon)
                : (StepState.Optional, "Nicht installiert – kein Problem, wenn du es nicht brauchst."),
            [
                new("Download-Seite öffnen", () => SetupChecker.OpenUrl("https://www.auburnsounds.com/products/Graillon.html")),
                new("EAPO-Editor öffnen", () => _host.OpenEditor()),
            ],
            "Geschafft! „Fertig“ schließt den Assistenten. Du findest ihn jederzeit wieder unter Hilfe → Einrichtungs-Assistent."),
    ];

    // ---------- Aktionen ----------

    private void AutoSetup()
    {
        string? error = _host.AutoSetup();
        Refresh();
        if (error != null)
            MessageBox.Show(this, error, "Zugriff freigeben", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DisableExclusive()
    {
        var info = _host.Probe().Exclusive;
        if (info == null)
        {
            MessageBox.Show(this, "„CABLE Output“ wurde nicht gefunden – bitte zuerst VB-CABLE installieren (Schritt 2).", "Exklusivmodus");
            return;
        }
        if (!info.Allowed) return;
        if (ExclusiveMode.Disable(info.DeviceId) is string error)
            MessageBox.Show(this, error, "Exklusivmodus", MessageBoxButton.OK, MessageBoxImage.Warning);
        Refresh();
    }

    private void Restart()
    {
        var answer = MessageBox.Show(this,
            "Windows jetzt neu starten?\n\nBitte vorher alles speichern. RP Voice Switcher öffnet sich danach von selbst, und der Assistent macht weiter.",
            "Neu starten", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _host.SetStep(_index);
        _host.ScheduleReopenAndRestart();
    }

    // ---------- Anzeige ----------

    private void Go(int index)
    {
        _index = Math.Clamp(index, 0, _steps.Count - 1);
        _host.SetStep(_index);
        Render();
    }

    private void Refresh()
    {
        if (!IsLoaded) return;
        _status = _host.Probe();
        RenderStepList();
        RenderStatus();
    }

    private void Render()
    {
        var step = _steps[_index];
        _counter.Text = $"Schritt {_index + 1} von {_steps.Count}";
        _header.Text = step.Title;

        _body.Children.Clear();
        var dim = (Brush)Application.Current.Resources["FgDim"];
        _body.Children.Add(new TextBlock { Text = step.Intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12), LineHeight = 20 });

        for (int i = 0; i < step.Instructions.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            bool numbered = _index != 0; // Willkommen = Aufzählung, sonst Anleitung zum Abarbeiten
            var mark = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = numbered ? (Brush)Application.Current.Resources["Accent"] : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = numbered ? (i + 1).ToString() : "•", FontWeight = FontWeights.SemiBold,
                    Foreground = numbered ? Brushes.Black : (Brush)Application.Current.Resources["Accent"],
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(new TextBlock { Text = step.Instructions[i], TextWrapping = TextWrapping.Wrap, LineHeight = 20, VerticalAlignment = VerticalAlignment.Center });
            _body.Children.Add(row);
        }

        if (step.Actions.Length > 0)
        {
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var a in step.Actions)
            {
                var b = new Button { Content = a.Label, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), ToolTip = a.Tooltip };
                b.Click += (_, _) =>
                {
                    try { a.Run(); }
                    catch (Exception ex) { MessageBox.Show(this, "Das hat nicht geklappt: " + ex.Message, "RP Voice Switcher"); }
                    Refresh();
                };
                actions.Children.Add(b);
            }
            _body.Children.Add(actions);
        }

        if (step.Tip != null)
            _body.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock { Text = "Tipp: " + step.Tip, TextWrapping = TextWrapping.Wrap, Foreground = dim },
            });

        _back.IsEnabled = _index > 0;
        _next.Content = _index == _steps.Count - 1 ? "Fertig" : "Weiter";
        RenderStepList();
        RenderStatus();
    }

    private void RenderStatus()
    {
        var (state, detail) = _steps[_index].Evaluate(_status);
        _badge.Visibility = state == StepState.Info ? Visibility.Collapsed : Visibility.Visible;
        (_badge.Background, _badgeText.Text) = state switch
        {
            StepState.Done => (OkBrush, "✓ Erledigt"),
            StepState.Optional => (InfoBrush, "Optional"),
            _ => (OpenBrush, "Noch offen"),
        };
        _detail.Text = detail;
        _detail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _detail.Foreground = (Brush)Application.Current.Resources["FgDim"];
    }

    private string? _listSignature;

    private void RenderStepList()
    {
        // Nur neu aufbauen, wenn sich etwas geändert hat – sonst gingen Klicks während der Aktualisierung verloren.
        string signature = _index + ":" + string.Join(",", _steps.Select(s => s.Evaluate(_status).State));
        if (signature == _listSignature) return;
        _listSignature = signature;
        _stepList.Children.Clear();
        var dim = (Brush)Application.Current.Resources["FgDim"];
        for (int i = 0; i < _steps.Count; i++)
        {
            int index = i;
            var (state, _) = _steps[i].Evaluate(_status);
            var icon = new TextBlock
            {
                Width = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Text = state switch { StepState.Done => "✓", StepState.Open => (i + 1).ToString(), _ => "•" },
                Foreground = state switch { StepState.Done => OkBrush, StepState.Open => OpenBrush, _ => dim },
            };
            var text = new TextBlock { Text = _steps[i].Title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Width = 212 }; // feste Breite = linksbündig im Button
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(text);
            var button = new Button
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(i == _index ? 2 : 0),
                BorderBrush = (Brush)Application.Current.Resources["Accent"],
                Background = i == _index ? (Brush)Application.Current.Resources["CardActive"] : Brushes.Transparent,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Schritt {i + 1}: {_steps[i].Title}");
            button.Click += (_, _) => Go(index);
            _stepList.Children.Add(button);
        }
    }

    /// <summary>Ist alles Wichtige erledigt? (Dann muss der Assistent beim ersten Start nicht aufgehen.)</summary>
    public static bool IsComplete(SetupChecker.Status s) =>
        s.CableInstalled && s.EapoInstalled && s.EapoOnTarget && !s.RestartPending && s.ConfigWritable && s.IncludeOk &&
        s.MicToCable != null && s.DefaultPlayback != null && !s.DefaultPlaybackIsCable && s.Exclusive is { Allowed: false };
}
