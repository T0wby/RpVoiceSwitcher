using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Zweiter Weg zum Dauersenden: Das Spiel selbst steht auf "offenes Mikrofon" (nanos world: Einstellungen → Audio →
// Open Mic). Dann sendet das Spiel von sich aus, und die Sprachtaste schaltet dort nur stumm/an. Die App muss also
// nichts gedrückt halten – sie tippt die Taste nur kurz an. Den echten Zustand im Spiel kann sie nicht auslesen,
// sie merkt ihn sich: eigenes Antippen und eigene Tastendrücke (außerhalb der App) werden mitgezählt.
public partial class MainWindow
{
    private bool OpenMicMode => _settings.GameOpenMic;
    /// <summary>Angenommener Zustand im Spiel. Start: offen (so startet das Spiel im Open-Mic-Modus).</summary>
    private bool _openMicMuted;
    private bool _openMicTapping;
    private bool _openMicWasDown;
    private readonly DispatcherTimer _openMicWatch = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private bool _openMicWatchWired;

    /// <summary>So lange wird die Taste beim Antippen gehalten – lang genug, dass auch Spiele mit 30 fps es sehen.</summary>
    private const int OpenMicTapMs = 80;

    /// <summary>Sendest du gerade (Dauersenden an bzw. Mikro im Spiel nicht stumm)?</summary>
    private bool IsTransmittingLatched => OpenMicMode ? PttKey != null && !_openMicMuted : _pttLatched;

    /// <summary>Umschalten wurde gewünscht, als das Spiel nicht vorne war (z. B. Klick in der App) – kommt nach.</summary>
    private bool _openMicTapPending;

    private async Task ToggleOpenMic(PushToTalk.Binding b)
    {
        if (_openMicTapping) return;
        bool mute = !_openMicMuted;
        if (!GameAllowsPtt())
        {
            // Im Spiel kommt der Tastendruck nur an, wenn es vorne ist → nachholen, sobald du ins Spiel wechselst
            _openMicMuted = mute;
            _openMicTapPending = !_openMicTapPending; // zweimal umschalten = nichts zu tun
            PttLog(_openMicTapPending ? "Antippen vorgemerkt (Spiel nicht im Vordergrund)" : "vorgemerktes Antippen aufgehoben");
            UpdatePttLatchUi();
            FlashOverlay((mute ? "Mikro im Spiel: stumm" : "Mikro im Spiel: an") + (_openMicTapPending ? " – sobald du ins Spiel wechselst" : ""));
            Beep(mute ? 520 : 880);
            return;
        }
        _openMicTapping = true;
        _openMicMuted = mute;
        UpdatePttLatchUi();
        FlashOverlay(mute ? "Mikro im Spiel: stumm" : "Mikro im Spiel: an – du sendest");
        Beep(mute ? 520 : 880);
        try
        {
            await WaitForModifiersUp();
            await _ptt.Tap(b, OpenMicTapMs);
            PttLog($"angetippt ({b}, Mikro im Spiel jetzt {(mute ? "stumm" : "an")})");
            await Task.Delay(40);
        }
        finally
        {
            _openMicWasDown = PushToTalk.IsDown(b);
            _openMicTapping = false;
        }
    }

    /// <summary>Anzeige stimmt nicht mit dem Spiel überein → nur die Anzeige umdrehen, keine Taste drücken.</summary>
    private void FlipOpenMicState()
    {
        _openMicMuted = !_openMicMuted;
        PttLog($"Anzeige umgedreht (Mikro im Spiel jetzt {(_openMicMuted ? "stumm" : "an")})");
        UpdatePttLatchUi();
        ShowStatusMessage(_openMicMuted ? "Anzeige: Mikro im Spiel stumm" : "Anzeige: Mikro im Spiel an");
    }

    private void PttStateFlip_Click(object sender, RoutedEventArgs e) => FlipOpenMicState();

    /// <summary>Drückst du die Sprachtaste selbst (im Spiel), schaltet das Spiel um – das zählt die App mit.</summary>
    private void UpdateOpenMicWatch()
    {
        if (!_openMicWatchWired)
        {
            _openMicWatchWired = true;
            _openMicWatch.Tick += (_, _) => WatchOwnPresses();
        }
        bool on = OpenMicMode && PttKey != null;
        if (on && !_openMicWatch.IsEnabled) _openMicWasDown = PttKey is PushToTalk.Binding b && PushToTalk.IsDown(b);
        _openMicWatch.IsEnabled = on;
    }

    private void WatchOwnPresses()
    {
        if (!OpenMicMode || PttKey is not PushToTalk.Binding b) { _openMicWatch.Stop(); return; }
        if (_openMicTapping) return;
        if (_openMicTapPending && GameAllowsPtt())
        {
            _openMicTapPending = false;
            _ = TapPending(b);
            return;
        }
        bool down = PushToTalk.IsDown(b);
        // Nur Drücke zählen, die im Spiel landen (nicht z. B. in der App beim Einstellen der Taste)
        bool reachesGame = GameGateOn ? GameInFront() : !OwnWindowInFront();
        if (down && !_openMicWasDown && reachesGame)
        {
            _openMicMuted = !_openMicMuted;
            PttLog($"selbst gedrückt (Mikro im Spiel jetzt {(_openMicMuted ? "stumm" : "an")})");
            UpdatePttLatchUi();
        }
        _openMicWasDown = down;
    }

    private async Task TapPending(PushToTalk.Binding b)
    {
        _openMicTapping = true;
        try
        {
            await WaitForModifiersUp();
            await Task.Delay(150); // Spielfenster hat gerade erst den Fokus bekommen
            await _ptt.Tap(b, OpenMicTapMs);
            PttLog($"vorgemerkt angetippt ({b}, Mikro im Spiel jetzt {(_openMicMuted ? "stumm" : "an")})");
            await Task.Delay(40);
        }
        finally
        {
            _openMicWasDown = PushToTalk.IsDown(b);
            _openMicTapping = false;
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static bool OwnWindowInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private void PttMode_Click(object sender, RoutedEventArgs e)
    {
        bool open = PttModeOpenMic.IsChecked == true;
        if (open == _settings.GameOpenMic) return;
        ReleasePttNow(); // gehaltene Taste aus dem alten Modus nie hängen lassen
        _settings.GameOpenMic = open;
        _settings.Save();
        _openMicMuted = false;
        _openMicTapPending = false;
        PttLog(open ? "Modus: offenes Mikrofon im Spiel (Taste antippen)" : "Modus: Push-to-Talk (Taste halten)");
        UpdatePushToTalkUi();
        RegisterAllBindings();
    }
}
