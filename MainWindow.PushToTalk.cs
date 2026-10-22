using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Sprachtaste des Spiels: Viele Spiele (z. B. nanos world) übertragen nur, solange Push-to-Talk gedrückt ist –
// dann muss das Soundboard die Taste selbst drücken, sonst hört niemand den Sound.
public partial class MainWindow
{
    private readonly PushToTalk _ptt = new();
    /// <summary>Sounds, die gleich nach dem Drücken der Sprachtaste starten (kurzer Vorlauf).</summary>
    private readonly HashSet<string> _pttPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _pttReleaseTimer = new() { Interval = TimeSpan.FromMilliseconds(PttTailMs) };

    /// <summary>So lange vor dem Sound wird die Taste gedrückt (Sprachchats brauchen einen Moment zum "Aufmachen").</summary>
    private const int PttLeadMs = 150;
    /// <summary>So lange nach dem letzten Sound bleibt sie gedrückt, damit das Ende nicht abgeschnitten wird.</summary>
    private const int PttTailMs = 400;

    private PushToTalk.Binding? PttBinding =>
        _settings.PushToTalkEnabled ? PushToTalk.Binding.Parse(_settings.PushToTalkKey) : null;

    private void InitPushToTalk()
    {
        _pttReleaseTimer.Tick += (_, _) =>
        {
            _pttReleaseTimer.Stop();
            if (!_soundEngine.AnyPlaying && _pttPending.Count == 0) _ptt.Release();
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

        if (_ptt.IsHeld) return play(); // läuft schon ein anderer Sound – Taste ist offen

        _ptt.Press(b);
        _pttPending.Add(path);
        _ = Task.Delay(PttLeadMs).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
        {
            if (!_pttPending.Remove(path)) return; // inzwischen abgebrochen
            if (!play()) ShowSoundError(_soundEngine.Error);
            UpdateSoundPlayingState();
        }));
        return true;
    }

    /// <summary>Gerade im Vorlauf (Taste schon gedrückt, Sound noch nicht gestartet)?</summary>
    private bool CancelPendingSound(string path)
    {
        if (!_pttPending.Remove(path)) return false;
        SchedulePttRelease();
        return true;
    }

    /// <summary>Taste kurz nach dem letzten Sound loslassen.</summary>
    private void SchedulePttRelease()
    {
        if (!_ptt.IsHeld || _soundEngine.AnyPlaying || _pttPending.Count > 0) return;
        if (!_pttReleaseTimer.IsEnabled) _pttReleaseTimer.Start();
    }

    /// <summary>Sofort loslassen (Notfall-Taste, Beenden, Einstellung geändert).</summary>
    private void ReleasePttNow()
    {
        _pttPending.Clear();
        _pttReleaseTimer.Stop();
        _ptt.Release();
    }

    // ---------- Einstellung ----------

    private void UpdatePushToTalkUi()
    {
        PttCheck.IsChecked = _settings.PushToTalkEnabled;
        var b = PushToTalk.Binding.Parse(_settings.PushToTalkKey);
        PttKeyButton.Content = b?.ToString() ?? "Taste wählen …";
        PttKeyButton.IsEnabled = _settings.PushToTalkEnabled;
        PttHint.Visibility = _settings.PushToTalkEnabled && b == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PttCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.PushToTalkEnabled = PttCheck.IsChecked == true;
        _settings.Save();
        if (!_settings.PushToTalkEnabled) ReleasePttNow();
        UpdatePushToTalkUi();
        if (_settings.PushToTalkEnabled && PushToTalk.Binding.Parse(_settings.PushToTalkKey) == null)
            PttKey_Click(sender, e);
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
            bool clash = plain == _settings.PanicHotkey || _settings.Current.AllBindings().Any(m => m.Values.Contains(plain));
            if (clash)
            {
                _capture?.SetText("Schon als Shortcut belegt – andere Taste");
                return;
            }
        }
        _settings.PushToTalkKey = binding.Serialize();
        FinishCapture();
        UpdatePushToTalkUi();
        ShowStatusMessage($"Sprachtaste: {binding} – wird gedrückt, solange ein Sound läuft.");
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
}
