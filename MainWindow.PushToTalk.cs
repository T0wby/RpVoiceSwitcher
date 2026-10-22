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

    private PushToTalk.Binding? PttKey => PushToTalk.Binding.Parse(_settings.PushToTalkKey);
    private PushToTalk.Binding? PttBinding => _settings.PushToTalkEnabled ? PttKey : null;

    private void InitPushToTalk()
    {
        _pttReleaseTimer.Tick += (_, _) =>
        {
            _pttReleaseTimer.Stop();
            if (!_pttLatched && !_soundEngine.AnyPlaying && _pttPending.Count == 0) ReleasePtt("Sound zu Ende");
        };
        UpdatePushToTalkUi();
    }

    /// <summary>
    /// Startet einen Sound – bei aktiver Sprachtaste erst Taste drücken, kurz warten, dann abspielen.
    /// Liefert false, wenn das Abspielen sofort scheiterte.
    /// </summary>
    private bool StartSoundWithPtt(string path, Func<bool> play)
    {
        _pttReleaseTimer.Stop();
        if (PttBinding is not PushToTalk.Binding b) return play();
        if (_ptt.IsHeld) return play(); // Dauersenden oder ein anderer Sound – Taste ist schon offen

        _pttPending.Add(path);
        _ = PressThenPlay(path, b, play);
        return true;
    }

    private async Task PressThenPlay(string path, PushToTalk.Binding b, Func<bool> play)
    {
        await WaitForModifiersUp();
        if (!_pttPending.Contains(path)) { SchedulePttRelease(); return; } // inzwischen abgebrochen
        if (!_ptt.IsHeld)
        {
            PressPtt(b, "Sound");
            await Task.Delay(PttLeadMs);
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
        if (_pttLatched || !_ptt.IsHeld || _soundEngine.AnyPlaying || _pttPending.Count > 0) return;
        if (!_pttReleaseTimer.IsEnabled) _pttReleaseTimer.Start();
    }

    /// <summary>Sofort loslassen und Dauersenden beenden (Notfall-Taste, Beenden).</summary>
    private void ReleasePttNow()
    {
        _pttPending.Clear();
        _pttReleaseTimer.Stop();
        bool wasLatched = _pttLatched;
        _pttLatched = false;
        ReleasePtt("sofort");
        if (wasLatched) UpdatePttLatchUi();
    }

    private void PressPtt(PushToTalk.Binding b, string why)
    {
        if (_ptt.IsHeld) return;
        _ptt.Press(b);
        PttLog($"gedrückt ({b}, {why})");
    }

    private void ReleasePtt(string why)
    {
        if (!_ptt.IsHeld) return;
        _ptt.Release();
        PttLog($"losgelassen ({why})");
    }

    // ---------- Dauersenden ----------

    private async void TogglePttLatch()
    {
        if (PttKey is not PushToTalk.Binding b)
        {
            Tabs.SelectedIndex = 1;
            PttHint.Text = "Für Dauersenden zuerst die Sprachtaste des Spiels wählen (Knopf „Sprachtaste“).";
            PttHint.Visibility = Visibility.Visible;
            FlashOverlay("Dauersenden: erst Sprachtaste wählen");
            return;
        }

        _pttLatched = !_pttLatched;
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
        PttLatchToggle.Content = _pttLatched ? "● Dauersenden: AN" : "Dauersenden: aus";
        PttLatchToggle.Foreground = _pttLatched ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("Fg");
        PttLatchToggle.Background = _pttLatched
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC2, 0x3B, 0x32))
            : (System.Windows.Media.Brush)FindResource("Card");
        UpdateStatusOverlay();
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

    private static readonly string PttLogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RpVoiceSwitcher", "sprachtaste.log");

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
