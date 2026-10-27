using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Sprachtaste des Spiels: Viele Spiele (z. B. nanos world) übertragen nur, solange Push-to-Talk gedrückt ist –
// dann muss das Soundboard die Taste selbst drücken, sonst hört niemand den Sound.
// "Dauersenden" hält die Taste dauerhaft gedrückt (an/aus per Shortcut) – wie ein offenes Mikrofon.
public partial class MainWindow
{
    private readonly PushToTalk _ptt = new();
    /// <summary>Sounds, die gleich nach dem Drücken der Sprachtaste starten (kurzer Vorlauf).</summary>
    private readonly HashSet<string> _pttPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _pttReleaseTimer = new() { Interval = TimeSpan.FromMilliseconds(PttTailMs) };
    /// <summary>Dauersenden: Sprachtaste bleibt gedrückt, bis es wieder ausgeschaltet wird.</summary>
    private bool _pttLatched;

    /// <summary>So lange vor dem Sound wird die Taste gedrückt (Sprachchats brauchen einen Moment zum "Aufmachen").</summary>
    private const int PttLeadMs = 150;
    /// <summary>So lange nach dem letzten Sound bleibt sie gedrückt, damit das Ende nicht abgeschnitten wird.</summary>
    private const int PttTailMs = 400;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private PushToTalk.Binding? PttKey => TestSafe(PushToTalk.Binding.Parse(_settings.PushToTalkKey));

    /// <summary>
    /// Läuft die App mit eigener Einstellungsdatei (--settings, nur für Tests), drückt sie nur F13–F24 –
    /// nie die echte Sprachtaste (z. B. Maus 5) des Nutzers.
    /// </summary>
    private static PushToTalk.Binding? TestSafe(PushToTalk.Binding? b) =>
        AppSettings.OverridePath == null || b is { IsMouse: false, Key: >= Key.F13 and <= Key.F24 } ? b : null;

    private static bool IsTestRun => AppSettings.OverridePath != null;
    /// <summary>Taste, die bei Sounds gedrückt wird – nicht bei offenem Mikrofon im Spiel (da sendet es ohnehin).</summary>
    private PushToTalk.Binding? PttBinding => _settings.PushToTalkEnabled && !OpenMicMode ? PttKey : null;

    private void InitPushToTalk()
    {
        // Hat die App beim letzten Mal eine Taste gedrückt gelassen (Absturz, hart beendet)? Jetzt loslassen.
        PushToTalk.MarkerFile = Path.Combine(Path.GetDirectoryName(PttLogFile)!, IsTestRun ? "sprachtaste-test.gedrueckt" : "sprachtaste.gedrueckt");
        try { Directory.CreateDirectory(Path.GetDirectoryName(PttLogFile)!); } catch { }
        if (PushToTalk.ReleaseLeftover() is PushToTalk.Binding left)
            PttLog($"losgelassen ({left}, hing noch vom letzten Mal)");
        // Notbremse, falls der Prozess ohne ExitApp endet (z. B. Abmelden, unerwarteter Fehler)
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _ptt.ReleaseOnExit();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _ptt.ReleaseOnExit();
        Application.Current.SessionEnding += (_, _) => _ptt.ReleaseOnExit();

        _pttReleaseTimer.Tick += (_, _) =>
        {
            _pttReleaseTimer.Stop();
            if (!_pttLatched && !_soundEngine.AnyPlaying && _pttPending.Count == 0) ReleasePtt("Sound zu Ende");
        };
        UpdatePushToTalkUi();
        Dispatcher.InvokeAsync(ResumeLatchAfterRestart, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Startet einen Sound – bei aktiver Sprachtaste erst Taste drücken, kurz warten, dann abspielen.
    /// Liefert false, wenn das Abspielen sofort scheiterte.
    /// </summary>
    private bool StartSoundWithPtt(string path, Func<bool> play)
    {
        _pttReleaseTimer.Stop();
        if (PttBinding is not PushToTalk.Binding b) return play();
        if (PttActive) return play(); // Dauersenden oder ein anderer Sound – Taste ist schon offen

        _pttPending.Add(path);
        _ = PressThenPlay(path, b, play);
        return true;
    }

    private async Task PressThenPlay(string path, PushToTalk.Binding b, Func<bool> play)
    {
        await WaitForModifiersUp();
        if (!_pttPending.Contains(path)) { SchedulePttRelease(); return; } // inzwischen abgebrochen
        if (!PttActive)
        {
            PressPtt(b, "Sound");
            if (_ptt.IsHeld) await Task.Delay(PttLeadMs); // Spiel nicht vorne: Sound trotzdem gleich abspielen
        }
        if (!_pttPending.Remove(path)) { SchedulePttRelease(); return; }
        if (!play()) { ShowSoundError(_soundEngine.Error); SchedulePttRelease(); }
        _soundStateTimer.Start();
        UpdateSoundPlayingState();
    }

    /// <summary>
    /// Wartet (höchstens knapp eine Sekunde), bis Strg/Alt/Umschalt/Win losgelassen sind. Sonst sieht das Spiel
    /// beim Drücken des Shortcuts z. B. "Strg + Maus 5" statt "Maus 5" – manche Spiele erkennen dann das
    /// Loslassen nicht mehr und senden dauerhaft.
    /// </summary>
    private static async Task WaitForModifiersUp()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        for (int i = 0; i < 60 && (Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C)); i++)
            await Task.Delay(15);
    }

    /// <summary>Gerade im Vorlauf (Taste schon gedrückt, Sound noch nicht gestartet)?</summary>
    private bool CancelPendingSound(string path)
    {
        if (!_pttPending.Remove(path)) return false;
        SchedulePttRelease();
        return true;
    }

    /// <summary>Taste kurz nach dem letzten Sound loslassen (nicht bei Dauersenden).</summary>
    private void SchedulePttRelease()
    {
        if (_pttLatched || !PttActive || _soundEngine.AnyPlaying || _pttPending.Count > 0) return;
        if (!_pttReleaseTimer.IsEnabled) _pttReleaseTimer.Start();
    }

    /// <summary>
    /// Sofort loslassen und Dauersenden beenden (Notfall-Taste, Beenden, Sprachtaste ändern).
    /// keepResume: Dauersenden beim nächsten Start fortsetzen (Update-Neustart).
    /// </summary>
    private void ReleasePttNow(bool keepResume = false)
    {
        _pttPending.Clear();
        _pttReleaseTimer.Stop();
        bool wasLatched = _pttLatched;
        _pttLatched = false;
        ReleasePtt("sofort");
        if (!keepResume) RememberLatch(false);
        if (wasLatched) UpdatePttLatchUi();
    }

    /// <summary>Merkt sich, ob Dauersenden an ist – damit es nach einem Update oder Absturz weiterläuft.</summary>
    private void RememberLatch(bool on)
    {
        if (_settings.PttLatchResume == on) return;
        _settings.PttLatchResume = on;
        _settings.Save();
    }

    /// <summary>Beim Start: War Dauersenden vor einem Update/Absturz an, läuft es weiter (gedrückt erst, wenn das Spiel vorne ist).</summary>
    private void ResumeLatchAfterRestart()
    {
        if (!_settings.PttLatchResume || _pttLatched) return;
        // Testläufe arbeiten mit einer Kopie deiner Einstellungen – dort nur fortsetzen, wenn der Test es ausdrücklich will
        if (IsTestRun && Environment.GetEnvironmentVariable("RPVS_TEST_RESUME") != "1") return;
        if (OpenMicMode || PttKey is not PushToTalk.Binding b || !GameGateOn)
        {
            // Ohne „nur im Spiel“ würde die Taste sofort in Windows hängen – dann lieber nicht von selbst
            RememberLatch(false);
            PttLog("Dauersenden nach dem Neustart nicht fortgesetzt (offenes Mikrofon, keine Sprachtaste oder „nur im Spiel“ aus)");
            return;
        }
        _pttLatched = true;
        UpdatePttLatchUi();
        PressPtt(b, "Dauersenden nach Neustart fortgesetzt");
        FlashOverlay("Dauersenden läuft weiter – war vor dem Neustart an");
    }

    /// <summary>
    /// Wacht, solange die App die Sprachtaste hält: Lässt du sie selbst los (kurz selbst gedrückt), drückt die App
    /// sie sofort wieder – Dauersenden bzw. der laufende Sound reißt so nicht ab.
    /// </summary>
    private readonly DispatcherTimer _pttKeepDown = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private bool _pttKeepDownWired;

    /// <summary>
    /// Die App möchte die Taste halten (Sound oder Dauersenden). Wirklich gedrückt ist sie nur, solange das Spiel
    /// im Vordergrund ist (falls eingestellt) – wechselst du z. B. per Alt+Tab raus, lässt die App sie los und
    /// drückt sie beim Zurückwechseln wieder. So hängt sie nie in Windows oder anderen Programmen.
    /// </summary>
    private PushToTalk.Binding? _pttWant;
    private bool PttActive => _pttWant != null;

    private void PressPtt(PushToTalk.Binding b, string why)
    {
        if (_pttWant != null) return;
        _pttWant = b;
        if (GameAllowsPtt())
        {
            _ptt.Press(b);
            PttLog($"gedrückt ({b}, {why})");
        }
        else PttLog($"wartet aufs Spiel ({b}, {why}) – {GameProcessName} ist nicht im Vordergrund");
        ResetPttRefresh();
        _pttTickFront = _ptt.IsHeld;
        if (!_pttKeepDownWired)
        {
            _pttKeepDownWired = true;
            _pttKeepDown.Tick += (_, _) => PttTick();
        }
        _pttKeepDown.Start();
        UpdatePttLatchUi();
    }

    // ---------- Auffrischen ----------
    // Spiele mit Unreal Engine (z. B. nanos world) "vergessen" gehaltene Tasten, wenn sie den Fokus zurückbekommen
    // oder Chat/Menü schließen. Windows meldet die Taste dann weiter als gedrückt – das Spiel sendet aber nicht mehr,
    // und von außen sieht man nichts. Deshalb: nach dem Zurückwechseln kurz warten, nach Enter/Esc/Tab im Spiel die
    // Taste einmal kurz loslassen und neu drücken, und bei Dauersenden regelmäßig auffrischen.

    /// <summary>So lange nach dem Wechsel ins Spiel warten, bevor gedrückt wird (Spiel verarbeitet sonst den Fokuswechsel danach).</summary>
    private const int GameFrontSettleMs = 300;
    /// <summary>Nach Enter/Esc/Tab so lange warten, dann auffrischen (Chat/Menü ist dann zu).</summary>
    private const int FlushKeyRefreshMs = 250;
    /// <summary>So lange ist die Taste beim Auffrischen los – ein Bild im Spiel reicht, mehr hört man nicht.</summary>
    private const int RefreshGapMs = 60;
    /// <summary>Nach dem Auf-/Zugehen eines Menüs im Spiel (Mauszeiger) so lange warten, bis es sich beruhigt hat, dann auffrischen.</summary>
    private const int MenuRefreshMs = 250;
    /// <summary>Standard für das regelmäßige Auffrischen bei Dauersenden.</summary>
    internal const int DefaultPttRefreshSeconds = 20;

    private bool _pttTickFront;
    private long _pttPressAfter, _pttRefreshAt = -1, _pttRepressAt = -1, _pttNextPeriodic = -1;
    private bool _flushKeyDown, _keepDownBrokenLogged;
    private string _refreshWhy = "";
    /// <summary>Zuletzt gesehener Zustand des Mauszeigers (Bit 1 = sichtbar, Bit 2 = ins Fenster gesperrt), -1 = noch keiner.</summary>
    private int _pttCursorMode = -1;
    /// <summary>Zuletzt gesehenes Vordergrundfenster des Spiels.</summary>
    private IntPtr _pttFrontWindow;

    private void ResetPttRefresh()
    {
        _pttPressAfter = 0;
        _pttRefreshAt = _pttRepressAt = -1;
        _flushKeyDown = _keepDownBrokenLogged = false;
        _pttCursorMode = -1;
        _pttFrontWindow = IntPtr.Zero;
        RestartPeriodicRefresh(Environment.TickCount64);
    }

    private void RestartPeriodicRefresh(long now) =>
        _pttNextPeriodic = _settings.PttRefreshSeconds > 0 ? now + _settings.PttRefreshSeconds * 1000L : -1;

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // Menüs im Spiel erkennen, ohne Tasten mitzulesen: Unreal blendet für Menüs, Inventar, Chat usw. den Mauszeiger
    // ein und gibt ihn frei – im Spiel selbst ist er versteckt und ins Fenster gesperrt. Genau bei diesem Umschalten
    // setzt Unreal alle gehaltenen Tasten zurück.
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size; public int Flags; public IntPtr Cursor; public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct ClipRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out ClipRect rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>Bit 1 = Mauszeiger sichtbar, Bit 2 = Mauszeiger in einen Bereich gesperrt (Spiel hat die Maus gefangen).</summary>
    private static int CursorMode()
    {
        int mode = 0;
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (GetCursorInfo(ref info) && (info.Flags & 1) != 0 && info.Cursor != IntPtr.Zero) mode |= 1;
        if (GetClipCursor(out var clip))
        {
            int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
            if (clip.Left > x || clip.Top > y || clip.Right < x + w || clip.Bottom < y + h) mode |= 2;
        }
        return mode;
    }

    private void PttTick()
    {
        if (_pttWant is not PushToTalk.Binding b) { _pttKeepDown.Stop(); return; }
        long now = Environment.TickCount64;
        bool front = GameAllowsPtt();

        if (!front)
        {
            _pttTickFront = false;
            _pttRefreshAt = _pttRepressAt = -1;
            _pttCursorMode = -1;
            _pttFrontWindow = IntPtr.Zero;
            if (_ptt.IsHeld)
            {
                _ptt.Release();
                PttLog("losgelassen (Spiel nicht mehr im Vordergrund)");
                UpdatePttLatchUi();
            }
            return;
        }
        if (!_pttTickFront)
        {
            // gerade ins Spiel zurückgekehrt: erst drücken, wenn das Spiel den Fokuswechsel verarbeitet hat
            _pttTickFront = true;
            _pttPressAfter = now + (GameGateOn ? GameFrontSettleMs : 0);
        }
        if (now < _pttPressAfter) return;

        // Auffrischen, zweiter Teil: Taste wieder drücken
        if (_pttRepressAt >= 0)
        {
            if (now < _pttRepressAt) return;
            _pttRepressAt = -1;
            _ptt.Press(b);
            _keepDownBrokenLogged = false;
            PttLog($"aufgefrischt ({_refreshWhy})");
            return;
        }

        if (!_ptt.IsHeld)
        {
            _ptt.Press(b);
            _keepDownBrokenLogged = false;
            _pttRefreshAt = -1;
            RestartPeriodicRefresh(now); // gerade frisch gedrückt – nicht gleich wieder auffrischen
            PttLog("gedrückt (Spiel wieder im Vordergrund)");
            UpdatePttLatchUi();
            return;
        }

        // Menü im Spiel auf/zu (Mauszeiger erscheint/verschwindet, Maus gefangen/frei) → danach auffrischen.
        // Mehrere Wechsel kurz hintereinander schieben das Auffrischen nur nach hinten.
        // Nur, wenn die App weiß, welches Programm das Spiel ist – sonst würde jeder Fensterwechsel in Windows zählen.
        if (GameGateOn)
        {
            int cursor = CursorMode();
            if (_pttCursorMode >= 0 && cursor != _pttCursorMode)
            {
                _pttRefreshAt = now + MenuRefreshMs;
                _refreshWhy = (cursor & 1) != 0 ? "Mauszeiger im Spiel aufgetaucht – Menü auf" : "Mauszeiger im Spiel verschwunden – Menü zu";
            }
            _pttCursorMode = cursor;

            // Anderes Fenster des Spiels vorne (z. B. Dialog, neu erzeugtes Spielfenster) → auffrischen
            var fg = GetForegroundWindow();
            if (_pttFrontWindow != IntPtr.Zero && fg != _pttFrontWindow)
            {
                _pttRefreshAt = now + MenuRefreshMs;
                _refreshWhy = "anderes Fenster des Spiels im Vordergrund";
            }
            _pttFrontWindow = fg;
        }

        // Enter/Esc/Tab im Spiel (Chat, Menü, Liste) → danach auffrischen
        bool flushKey = IsKeyDown(0x0D) || IsKeyDown(0x1B) || IsKeyDown(0x09);
        if (_flushKeyDown && !flushKey) { _pttRefreshAt = now + FlushKeyRefreshMs; _refreshWhy = "nach Enter/Esc/Tab im Spiel"; }
        _flushKeyDown = flushKey;
        if (_pttLatched && _pttNextPeriodic >= 0 && now >= _pttNextPeriodic && _pttRefreshAt < 0)
        {
            _pttRefreshAt = now; _refreshWhy = "regelmäßig";
        }
        if (_pttRefreshAt >= 0 && now >= _pttRefreshAt && !flushKey)
        {
            // Auffrischen, erster Teil: kurz loslassen
            _pttRefreshAt = -1;
            RestartPeriodicRefresh(now);
            _ptt.Release();
            _pttRepressAt = now + RefreshGapMs;
            return;
        }

        if (_ptt.KeepDown()) PttLog("wieder gedrückt (war selbst losgelassen)");
        else if (_ptt.KeepDownBroken && !_keepDownBrokenLogged)
        {
            _keepDownBrokenLogged = true;
            PttLog("Windows meldet die Taste trotz Drücken weiter als losgelassen – Auffrischen versucht es gleich erneut");
            _pttRefreshAt = now + 1000; _refreshWhy = "Taste kam nicht an";
        }
    }

    private void ReleasePtt(string why)
    {
        _pttKeepDown.Stop();
        _pttRefreshAt = _pttRepressAt = -1;
        bool wanted = _pttWant != null;
        _pttWant = null;
        if (_ptt.IsHeld)
        {
            _ptt.Release();
            PttLog($"losgelassen ({why})");
        }
        else if (wanted) PttLog($"nicht mehr gewünscht ({why})");
        if (wanted) UpdatePttLatchUi();
    }

    // ---------- Dauersenden ----------

    private async void TogglePttLatch()
    {
        if (PttKey is not PushToTalk.Binding b)
        {
            FlashOverlay("Dauersenden: erst die Sprachtaste des Spiels einstellen");
            OpenPttSettings();
            PttHint.Text = "Für Dauersenden zuerst hier die Sprachtaste des Spiels wählen.";
            PttHint.Visibility = Visibility.Visible;
            return;
        }
        if (OpenMicMode) { await ToggleOpenMic(b); return; }

        _pttLatched = !_pttLatched;
        RememberLatch(_pttLatched);
        UpdatePttLatchUi();
        if (_pttLatched)
        {
            _pttReleaseTimer.Stop();
            FlashOverlay("Dauersenden AN – du sprichst dauerhaft");
            Beep(880);
            await WaitForModifiersUp();
            if (_pttLatched) PressPtt(b, "Dauersenden an");
        }
        else
        {
            FlashOverlay("Dauersenden aus – wieder Push-to-Talk");
            Beep(520);
            if (!_soundEngine.AnyPlaying && _pttPending.Count == 0) ReleasePtt("Dauersenden aus");
        }
    }

    private void FlashOverlay(string text)
    {
        ShowStatusMessage(text);
        if (!_settings.ShowOverlay) return;
        _overlay ??= new OverlayWindow();
        _overlay.Flash(text);
    }

    private void UpdatePttLatchUi()
    {
        bool on = IsTransmittingLatched;
        PttLatchToggle.Content = OpenMicMode
            ? (on ? "● Mikro im Spiel: AN" : "Mikro im Spiel: stumm")
            : (on ? (PttActive && !_ptt.IsHeld ? "● Dauersenden: AN (wartet aufs Spiel)" : "● Dauersenden: AN") : "Dauersenden: aus");
        PttLatchToggle.ToolTip = OpenMicMode
            ? "Tippt die Sprachtaste kurz an – im Spiel (offenes Mikrofon) schaltet das stumm bzw. wieder an."
            : "Hält die Sprachtaste dauerhaft gedrückt, bis du wieder ausschaltest.";
        PttLatchToggle.Foreground = on ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("Fg");
        PttLatchToggle.Background = on
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC2, 0x3B, 0x32))
            : (System.Windows.Media.Brush)FindResource("Card");
        PttStateFlip.Visibility = OpenMicMode && PttKey != null ? Visibility.Visible : Visibility.Collapsed;
        PttLatchMenuItem.Header = OpenMicMode ? "_Mikro im Spiel an/stumm (Sprachtaste antippen)" : "_Dauersenden an/aus (Sprachtaste halten)";
        UpdatePttSummary();
        UpdateStatusOverlay();
    }

    /// <summary>Kurzinfo im Soundboard: welche Sprachtaste, welche Umschalt-Taste.</summary>
    private void UpdatePttSummary()
    {
        var key = PttKey;
        var latch = Hotkey.Parse(_settings.PttLatchHotkey);
        PttSummary.Text = key == null
            ? "Keine Sprachtaste eingestellt"
            : $"Sprachtaste: {key}" + (latch != null ? $" · Umschalten: {latch}" : "") + (OpenMicMode ? " · Spiel: offenes Mikro" : "")
              + (_gameElevated ? " · ⚠ Spiel läuft als Administrator – App auch als Administrator starten" : "");
    }

    private void OpenPttSettings_Click(object sender, RoutedEventArgs e) => OpenPttSettings();

    /// <summary>Einstellungen öffnen und zum Abschnitt „Sprachtaste im Spiel“ scrollen.</summary>
    private void OpenPttSettings()
    {
        ShowSettingsWindow();
        Dispatcher.InvokeAsync(() => ScrollToSettingsSection(nameof(PttSection)), DispatcherPriority.Loaded);
    }

    private void ClearPttKey_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        ReleasePttNow();
        _settings.PushToTalkKey = null;
        _settings.Save();
        UpdatePushToTalkUi();
    }

    private void PttLatchToggle_Click(object sender, RoutedEventArgs e) => TogglePttLatch();

    private void PttLatchKey_Click(object sender, RoutedEventArgs e) =>
        StartCapture(BindingKind.PttLatch, "", t => PttLatchButton.Content = t);

    private void ClearPttLatchKey_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        _settings.PttLatchHotkey = null;
        _settings.Save();
        RegisterAllBindings();
    }

    // ---------- Einstellung ----------

    private void UpdatePushToTalkUi()
    {
        PttCheck.IsChecked = _settings.PushToTalkEnabled;
        var b = PttKey;
        PttKeyButton.Content = b?.ToString() ?? "Taste wählen …";
        PttHint.Text = "Noch keine Sprachtaste gewählt – auf den Knopf klicken und dieselbe Taste drücken, die du im Spiel zum Sprechen nutzt.";
        PttHint.Visibility = (_settings.PushToTalkEnabled || _pttLatched) && b == null ? Visibility.Visible : Visibility.Collapsed;

        bool open = OpenMicMode;
        PttModeHold.IsChecked = !open;
        PttModeOpenMic.IsChecked = open;
        PttCheck.IsEnabled = !open;
        PttCheck.Opacity = open ? 0.5 : 1;
        PttCheck.Content = open
            ? "Beim Abspielen von Sounds automatisch drücken (bei offenem Mikrofon nicht nötig)"
            : "Beim Abspielen von Sounds automatisch drücken";
        PttLatchLabel.Text = open ? "Mikro an/stumm" : "Dauersenden an/aus";
        PttModeHint.Text = open
            ? "Offenes Mikrofon: Stell im Spiel den Sprachchat auf offenes Mikrofon (nanos world: Einstellungen → Audio → „Open Mic“). " +
              "Dann sendet das Spiel selbst dauerhaft – auch deine Sounds, ohne dass die App etwas drückt. Die Umschalt-Taste tippt die " +
              "Sprachtaste nur kurz an, das schaltet dich im Spiel stumm bzw. wieder an. Die App kann den Zustand im Spiel nicht auslesen; " +
              "sie zählt mit (auch wenn du die Taste selbst drückst). Zeigt sie etwas Falsches an, im Soundboard auf ⇄ klicken."
            : "Dauersenden hält die Sprachtaste gedrückt, bis du es wieder ausschaltest – wie ein offenes Mikrofon. Drückst du die " +
              "Sprachtaste zwischendurch selbst, läuft es einfach weiter. Wechselst du aus dem Spiel raus (Alt+Tab), lässt die App die " +
              "Taste los und drückt sie beim Zurückwechseln wieder. Als Umschalt-Taste eignet sich z. B. Strg + Alt + eine F-Taste.";
        UpdateOpenMicWatch();
        UpdateGameUi();
        UpdatePttLatchUi();
    }

    private void PttCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.PushToTalkEnabled = PttCheck.IsChecked == true;
        _settings.Save();
        if (!_settings.PushToTalkEnabled && !_pttLatched) { _pttPending.Clear(); ReleasePtt("Haken entfernt"); }
        UpdatePushToTalkUi();
        if (_settings.PushToTalkEnabled && PttKey == null) PttKey_Click(sender, e);
    }

    private void PttKey_Click(object sender, RoutedEventArgs e)
    {
        ReleasePttNow();
        StartCapture(BindingKind.PushToTalk, "", t => PttKeyButton.Content = t);
        PttKeyButton.Content = "Sprachtaste drücken (auch Maus 4/5) …";
    }

    /// <summary>Taste/Maustaste für Push-to-Talk übernehmen – prüft, dass sie nicht schon ein Shortcut ist.</summary>
    private void CapturePushToTalk(PushToTalk.Binding binding)
    {
        if (!binding.IsMouse)
        {
            string plain = new Hotkey(ModifierKeys.None, binding.Key).Serialize();
            bool clash = plain == _settings.PanicHotkey || plain == _settings.PttLatchHotkey
                         || _settings.Current.AllBindings().Any(m => m.Values.Contains(plain));
            if (clash)
            {
                _capture?.SetText("Schon als Shortcut belegt – andere Taste");
                return;
            }
        }
        _settings.PushToTalkKey = binding.Serialize();
        FinishCapture();
        UpdatePushToTalkUi();
        ShowStatusMessage($"Sprachtaste: {binding}");
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_capture?.Kind != BindingKind.PushToTalk) return;
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
            CapturePushToTalk(new PushToTalk.Binding(true, Key.None, e.ChangedButton == MouseButton.XButton1 ? 1 : 2));
        }
    }

    // ---------- Protokoll (für die Fehlersuche, nur die letzten Einträge) ----------

    private static string PttLogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RpVoiceSwitcher",
        AppSettings.OverridePath != null ? "sprachtaste-test.log" : "sprachtaste.log");

    private static void PttLog(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PttLogFile)!);
            if (File.Exists(PttLogFile) && new FileInfo(PttLogFile).Length > 64 * 1024)
                File.WriteAllLines(PttLogFile, File.ReadAllLines(PttLogFile).TakeLast(200));
            File.AppendAllText(PttLogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  Sprachtaste {text}{Environment.NewLine}");
        }
        catch { /* Protokoll ist nur eine Hilfe */ }
    }
}
