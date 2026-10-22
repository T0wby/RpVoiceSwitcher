using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Stimmen laden/anzeigen, Umschalten, Halten, Status-Anzeige
public partial class MainWindow
{
    private readonly List<VoiceItem> _allVoices = new();
    private readonly ObservableCollection<VoiceItem> _items = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reloadDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private OverlayWindow? _overlay;
    private StatusOverlayWindow? _status;
    private bool _statusEditing;

    /// <summary>Zuletzt bewusst gewählte Stimme – dahin geht es nach dem Loslassen einer Halte-Taste zurück.</summary>
    private VoiceItem? _baseVoice;

    /// <summary>Stimme, deren Halte-Taste gerade gedrückt ist.</summary>
    private VoiceItem? _holdVoice;

    private IEnumerable<VoiceItem> ProfileVoices() => _allVoices.Where(v => _settings.Current.ContainsVoice(v.FileName));
    private VoiceItem? CurrentVoice() => _allVoices.FirstOrDefault(v => v.IsActive);
    private VoiceItem? NormalVoice() => _allVoices.FirstOrDefault(v => v.IsNormal);

    private void LoadVoices()
    {
        _holdHook.ReleaseHeld();
        _holdVoice = null;
        _allVoices.Clear();
        if (_store != null)
        {
            string? activeFile = _store.GetActiveFileName();
            foreach (var preset in _store.LoadVoices())
                _allVoices.Add(new VoiceItem(preset)
                {
                    IsActive = string.Equals(preset.FileName, activeFile, StringComparison.OrdinalIgnoreCase),
                });
        }
        _baseVoice = CurrentVoice();

        // Aktive Stimme neu schreiben: übernimmt Änderungen an ihrer Datei (z. B. aus dem EAPO-Editor) sofort
        // und hält voice.txt im aktuellen Format.
        var active = CurrentVoice();
        if (active != null && _store != null)
        {
            try { _store.Activate(active.Preset, _settings.TargetDevice); }
            catch { /* wird beim nächsten Umschalten erneut versucht */ }
        }

        RefreshVoiceList();
        RegisterAllBindings();
        UpdateStatus();
    }

    /// <summary>Zeigt die Stimmen des aktuellen Charakters – im Auswahlmodus alle mit Häkchen.</summary>
    private void RefreshVoiceList()
    {
        _items.Clear();
        var profile = _settings.Current;
        foreach (var v in _allVoices)
        {
            v.Selecting = _selectingVoices;
            v.InProfile = profile.ContainsVoice(v.FileName);
            if (_selectingVoices || v.InProfile) _items.Add(v);
        }
    }

    private void WatchVoicesFolder()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_store == null || !Directory.Exists(_store.VoicesDir)) return;

        _watcher = new FileSystemWatcher(_store.VoicesDir, "*.txt")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, _) => Dispatcher.InvokeAsync(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
        _watcher.Created += changed;
        _watcher.Changed += changed;
        _watcher.Deleted += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
    }

    // ---------- Umschalten ----------

    /// <summary>Bewusster Wechsel (Shortcut, Klick, Tray). Beendet ein laufendes Halten.</summary>
    private void ActivateVoice(VoiceItem item)
    {
        _holdVoice = null;
        _baseVoice = item;
        ApplyVoice(item, quiet: false);
    }

    private void BeginHold(VoiceItem item)
    {
        if (_holdVoice == null) _baseVoice = CurrentVoice() ?? NormalVoice();
        _holdVoice = item;
        ApplyVoice(item, quiet: true); // kein Ton/keine Einblendung – sonst stört es beim Sprechen
    }

    private void EndHold(VoiceItem item)
    {
        if (!ReferenceEquals(_holdVoice, item)) return; // wurde inzwischen bewusst umgeschaltet
        _holdVoice = null;
        var back = _baseVoice ?? NormalVoice();
        if (back != null) ApplyVoice(back, quiet: true);
    }

    private void ApplyVoice(VoiceItem item, bool quiet)
    {
        if (_store == null) return;
        try
        {
            _store.Activate(item.Preset, _settings.TargetDevice);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Umschalten auf " + item.FileName, ex);
            ShowStatusMessage("Umschalten fehlgeschlagen: " + ex.Message);
            return;
        }

        foreach (var v in _allVoices) v.IsActive = ReferenceEquals(v, item);
        UpdateStatus();
        if (quiet) return;

        if (_settings.ShowOverlay)
        {
            _overlay ??= new OverlayWindow();
            _overlay.Flash("Stimme: " + item.Name);
        }
        Beep(item.IsNormal ? 520 : 880);
    }

    private void Beep(int freq)
    {
        if (!_settings.PlaySound) return;
        Task.Run(() => { try { Console.Beep(freq, 90); } catch { /* kein Ton verfügbar */ } });
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceItem item) ActivateVoice(item);
    }

    private void UpdateStatus()
    {
        var active = CurrentVoice();
        string voice = active?.Name ?? "keine";
        UpdateStatusBar();
        _tray.Text = Truncate((_settings.Profiles.Count > 1 ? _settings.Current.Name + ": " : "RP-Stimme: ") + voice, 63);
        UpdateStatusOverlay();
        WriteObsFile();
        _voiceTest?.RefreshVoices();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ---------- Status-Anzeige ----------

    private void UpdateStatusOverlay()
    {
        if (!_settings.ShowStatusOverlay)
        {
            if (_status != null)
            {
                SetStatusEditMode(false);
                _status.Hide();
            }
            return;
        }

        if (_status == null)
        {
            _status = new StatusOverlayWindow();
            _status.PositionChanged += (left, top) =>
            {
                _settings.StatusOverlayLeft = left;
                _settings.StatusOverlayTop = top;
                _settings.Save();
            };
            _status.EditFinished += () => SetStatusEditMode(false);
        }

        var active = CurrentVoice();
        bool isNormal = active == null || active.IsNormal;
        string text = active?.Name ?? "Keine Stimme";
        if (_settings.Profiles.Count > 1) text = _settings.Current.Name + " · " + text;
        _status.SetVoice(text, isNormal);
        _status.SetLook(_settings.StatusOverlayScale, _settings.StatusOverlayOpacity);
        var sounds = _settings.StatusOverlayShowSounds ? _overlaySounds : new List<StatusOverlayWindow.SoundLine>();
        _status.SetSounds(sounds);

        // Optional nur zeigen, wenn ein Effekt aktiv ist (oder ein Sound läuft) – beim Verschieben aber immer.
        if (_settings.StatusOverlayHideNormal && isNormal && !_statusEditing && sounds.Count == 0)
        {
            _status.Hide();
            return;
        }
        if (!_status.IsVisible)
            _status.ShowAt(_settings.StatusOverlayLeft, _settings.StatusOverlayTop);
    }

    private void StatusLook_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _settings.StatusOverlayScale = Math.Round(StatusScaleSlider.Value, 2);
        _settings.StatusOverlayOpacity = Math.Round(StatusOpacitySlider.Value, 2);
        UpdateStatusLookTexts();
        _settings.Save();
        UpdateStatusOverlay();
    }

    private void UpdateStatusLookTexts()
    {
        StatusScaleText.Text = $"{StatusScaleSlider.Value * 100:0} %";
        StatusOpacityText.Text = $"{StatusOpacitySlider.Value * 100:0} %";
    }

    private void StatusSounds_Changed(object sender, RoutedEventArgs e)
    {
        _settings.StatusOverlayShowSounds = StatusSoundsCheck.IsChecked == true;
        _settings.Save();
        UpdateStatusOverlay();
    }

    private void StatusHideNormal_Changed(object sender, RoutedEventArgs e)
    {
        _settings.StatusOverlayHideNormal = StatusHideNormalCheck.IsChecked == true;
        _settings.Save();
        UpdateStatusOverlay();
    }

    private void SetStatusEditMode(bool on)
    {
        if (on && !_settings.ShowStatusOverlay)
        {
            _settings.ShowStatusOverlay = true;
            StatusCheck.IsChecked = true;
            _settings.Save();
            UpdateStatusOverlay();
        }

        _statusEditing = on && _status != null;
        _status?.SetEditMode(_statusEditing);
        if (_settings.ShowStatusOverlay) UpdateStatusOverlay(); // bei "nur mit Effekt" zum Verschieben einblenden bzw. danach wieder aus
        MoveStatusButton.Content = _statusEditing ? "Fertig" : "Position anpassen";
    }

    private void StatusOverlay_Changed(object sender, RoutedEventArgs e)
    {
        _settings.ShowStatusOverlay = StatusCheck.IsChecked == true;
        _settings.Save();
        UpdateStatusOverlay();
    }

    private void MoveStatus_Click(object sender, RoutedEventArgs e) => SetStatusEditMode(!_statusEditing);

    private void ResetStatusPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.StatusOverlayLeft = null;
        _settings.StatusOverlayTop = null;
        _settings.Save();
        _status?.MoveTo(null, null);
    }

    private void ToggleStatusOverlay()
    {
        _settings.ShowStatusOverlay = !_settings.ShowStatusOverlay;
        StatusCheck.IsChecked = _settings.ShowStatusOverlay;
        _settings.Save();
        UpdateStatusOverlay();
    }
}
