using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Soundboard: Sounds ins virtuelle Kabel (für andere) und optional zum Mithören
public partial class MainWindow
{
    /// <summary>Angezeigte Sounds: die des aktiven Charakters (beim Auswählen alle).</summary>
    private readonly ObservableCollection<SoundItem> _sounds = new();
    /// <summary>Alle Sounds im Sound-Ordner.</summary>
    private readonly List<SoundItem> _allSounds = new();
    private bool _selectingSounds;
    private readonly SoundEngine _soundEngine = new();
    private FileSystemWatcher? _soundWatcher;
    private readonly DispatcherTimer _soundReloadDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void InitSoundboard()
    {
        _soundStateTimer.Tick += (_, _) => UpdateSoundPlayingState();
        _soundReloadDebounce.Tick += (_, _) =>
        {
            _soundReloadDebounce.Stop();
            if (_capture != null) { _soundReloadDebounce.Start(); return; }
            LoadSounds();
        };

        PrepareSoundFolder();
        RestartSoundEngine();
    }

    /// <summary>Ordner anlegen/übernehmen, Start-Sounds erzeugen, laden und beobachten.</summary>
    private void PrepareSoundFolder()
    {
        try
        {
            SoundLibrary.MigrateLegacyFolder();
            SoundLibrary.EnsureStarterSounds();
        }
        catch (Exception ex) { ShowSoundError("Sound-Ordner konnte nicht vorbereitet werden: " + ex.Message); }

        LoadSounds();
        WatchSoundFolder();
    }

    private void WatchSoundFolder()
    {
        _soundWatcher?.Dispose();
        _soundWatcher = null;
        try
        {
            _soundWatcher = new FileSystemWatcher(SoundLibrary.Folder) { EnableRaisingEvents = true };
            FileSystemEventHandler changed = (_, _) => Dispatcher.InvokeAsync(() =>
            {
                _soundReloadDebounce.Stop();
                _soundReloadDebounce.Start();
            });
            _soundWatcher.Created += changed;
            _soundWatcher.Deleted += changed;
            _soundWatcher.Renamed += (s, e) => changed(s, e);
        }
        catch { /* ohne Watcher geht's auch – dann eben ohne automatisches Neuladen */ }
    }

    private void LoadSounds()
    {
        _allSounds.Clear();
        try
        {
            foreach (var file in SoundLibrary.Load())
                _allSounds.Add(new SoundItem(file)
                {
                    ThroughEffect = _settings.SoundsWithEffect.Contains(file.FileName, StringComparer.OrdinalIgnoreCase),
                    Repeat = Math.Max(0, _settings.SoundRepeat.GetValueOrDefault(file.FileName, 1)),
                    IsPlaying = _soundEngine.IsPlaying(file.FullPath),
                });
        }
        catch (Exception ex) { ShowSoundError("Sound-Ordner konnte nicht gelesen werden: " + ex.Message); }
        // Lautstärke im Hintergrund vormessen, damit schon der erste Druck angeglichen klingt
        if (_settings.SoundNormalize) SoundLevels.Prepare(_allSounds.Select(s => s.File.FullPath));
        RefreshSoundList();
        RegisterAllBindings();
    }

    /// <summary>Zeigt die Sounds des aktiven Charakters – im Auswahlmodus alle mit Haken.</summary>
    private void RefreshSoundList()
    {
        var profile = _settings.Current;
        _sounds.Clear();
        foreach (var s in _allSounds)
        {
            s.Selecting = _selectingSounds;
            s.InProfile = profile.ContainsSound(s.FileName);
            if (_selectingSounds || s.InProfile) _sounds.Add(s);
        }
        UpdateSoundHint();
        UpdateSoundStatus();
    }

    private int ProfileSoundCount => _allSounds.Count(s => _settings.Current.ContainsSound(s.FileName));

    private void UpdateSoundHint()
    {
        SelectSoundsButton.Content = _selectingSounds ? "Fertig" : "Sounds auswählen";
        SelectSoundsAllNone.Visibility = _selectingSounds ? Visibility.Visible : Visibility.Collapsed;
        SoundHint.Text = _selectingSounds
            ? $"Hake die Sounds an, die zu „{_settings.Current.Name}“ gehören. Die anderen bleiben im Sound-Ordner und lassen sich jederzeit wieder dazunehmen."
            : _allSounds.Count > 0 && ProfileSoundCount == 0
                ? $"„{_settings.Current.Name}“ hat noch keine Sounds – oben auf „Sounds auswählen“ klicken oder Audiodateien hierher ziehen."
                : "Audiodateien (wav, mp3, m4a, flac …) einfach hierher ziehen. Mit „Durch Stimmeffekt“ klingt ein Sound wie deine Stimme – unter der Maske also auch gedämpft. Ohne Haken hören die anderen ihn unverändert.";
        SelectSoundsMenuItem.Header = _selectingSounds ? "Sound_auswahl beenden" : "S_ounds auswählen";
    }

    // ---------- Sounds auswählen (pro Charakter) ----------

    private void SelectSounds_Click(object sender, RoutedEventArgs e) => SetSelectingSounds(!_selectingSounds);

    private void SetSelectingSounds(bool on)
    {
        CancelCapture();
        _selectingSounds = on;
        RefreshSoundList();
        if (!on) RegisterAllBindings();
    }

    private void ProfileSound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox box || box.DataContext is not SoundItem item) return;
        var profile = _settings.Current;
        bool want = box.IsChecked == true;
        if (want == profile.ContainsSound(item.FileName)) return; // nur Anzeige aufgebaut, nichts geändert

        // "Alle Sounds" in eine feste Liste umwandeln, sobald etwas abgewählt wird.
        if (profile.IncludeAllSounds)
        {
            profile.IncludeAllSounds = false;
            profile.Sounds = _allSounds.Select(s => s.FileName).ToList();
        }

        profile.Sounds.RemoveAll(s => s.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
        if (want) profile.Sounds.Add(item.FileName);
        item.InProfile = want;
        _settings.Save();
        UpdateSoundStatus();
    }

    /// <summary>Alle an- oder abhaken (im Auswahlmodus).</summary>
    private void SelectAllSounds(bool all)
    {
        var profile = _settings.Current;
        profile.IncludeAllSounds = false;
        profile.Sounds = all ? _allSounds.Select(s => s.FileName).ToList() : new List<string>();
        _settings.Save();
        RefreshSoundList();
    }

    private void SelectAllSounds_Click(object sender, RoutedEventArgs e) => SelectAllSounds(true);
    private void SelectNoSounds_Click(object sender, RoutedEventArgs e) => SelectAllSounds(false);

    /// <summary>Neu hinzugefügte Sounds gehören zum aktiven Charakter (wenn er eine feste Auswahl hat).</summary>
    private void AddToCurrentProfile(IEnumerable<string> fileNames)
    {
        var profile = _settings.Current;
        if (profile.IncludeAllSounds) return;
        foreach (var f in fileNames)
            if (!profile.ContainsSound(f)) profile.Sounds.Add(f);
        _settings.Save();
    }

    /// <summary>Import mit Merken, welche Dateien neu dazukamen.</summary>
    private int ImportSounds(IEnumerable<string> files)
    {
        var before = SoundLibrary.Load().Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = SoundLibrary.Import(files);
        AddToCurrentProfile(SoundLibrary.Load().Select(f => f.FileName).Where(f => !before.Contains(f)));
        return n;
    }

    private void RestartSoundEngine()
    {
        _soundEngine.Start(_settings.SoundOutputDevice, _settings.SoundMonitor, _settings.SoundVolume, _settings.SoundMonitorVolume);
        ShowSoundError(_soundEngine.Error);
    }

    private void ShowSoundError(string? message)
    {
        SoundError.Text = message ?? "";
        SoundError.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        UpdateSoundStatus();
    }

    /// <summary>
    /// Abspielen – oder stoppen, wenn der Sound gerade läuft (einmal, n-mal oder Loop).
    /// Gilt für Klick und Shortcut gleichermaßen: dieselbe Taste nochmal = aus.
    /// </summary>
    private void PlaySound(SoundItem item)
    {
        string path = item.File.FullPath;
        if (_soundEngine.IsPlaying(path) || CancelPendingSound(path))
        {
            _soundEngine.Stop(path);
            item.IsPlaying = false;
            UpdateSoundPlayingState();
            return;
        }
        if (!File.Exists(path)) { LoadSounds(); return; }
        bool throughEffect = item.ThroughEffect;
        int repeat = item.Repeat;
        float gain = _settings.SoundNormalize ? SoundLevels.GainFor(path) : 1f;
        if (!StartSoundWithPtt(path, () => _soundEngine.Play(path, bypassEffect: !throughEffect, repeat: repeat, gain: gain)))
        {
            ShowSoundError(_soundEngine.Error);
            SchedulePttRelease();
            return;
        }
        if (SoundError.Visibility == Visibility.Visible && _soundEngine.Error == null) ShowSoundError(null);
        item.IsPlaying = true;
        _soundStateTimer.Start();
        UpdateSoundPlayingState();
    }

    /// <summary>Hält Abspielen/Stoppen, Fortschrittsbalken und Zeit aktuell – an den Karten und in der Status-Anzeige.</summary>
    private readonly DispatcherTimer _soundStateTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<StatusOverlayWindow.SoundLine> _overlaySounds = new();

    private void UpdateSoundPlayingState()
    {
        var progress = _soundEngine.GetProgress();
        var byPath = progress.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var lines = new List<StatusOverlayWindow.SoundLine>();
        foreach (var s in _allSounds) // auch Sounds anderer Charaktere, die noch laufen
        {
            if (byPath.TryGetValue(s.File.FullPath, out var p))
            {
                s.IsPlaying = true;
                s.Progress = p.Fraction;
                s.TimeText = SoundItem.FormatProgress(p);
                lines.Add(new StatusOverlayWindow.SoundLine(s.Name, p.Fraction, s.TimeText));
            }
            else s.IsPlaying = _pttPending.Contains(s.File.FullPath); // Vorlauf der Sprachtaste zählt schon als "läuft"
        }

        bool overlayChanged = lines.Count != _overlaySounds.Count || lines.Count > 0;
        _overlaySounds = lines;
        if (overlayChanged) UpdateStatusOverlay();
        if (progress.Count == 0 && _pttPending.Count == 0)
        {
            _soundStateTimer.Stop();
            SchedulePttRelease();
        }
    }

    private void SoundRepeat_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundItem item || e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not SoundItem.RepeatChoice choice) return;
        if (choice.Value == _settings.SoundRepeat.GetValueOrDefault(item.FileName, 1)) return; // nur Anzeige aufgebaut
        item.Repeat = choice.Value;
        if (choice.Value == 1) _settings.SoundRepeat.Remove(item.FileName);
        else _settings.SoundRepeat[item.FileName] = choice.Value;
        _settings.Save();
    }

    private void PlaySound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItem item) PlaySound(item);
    }

    private void SoundEffect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundItem item) return;
        _settings.SoundsWithEffect.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
        if (item.ThroughEffect) _settings.SoundsWithEffect.Add(item.FileName);
        _settings.Save();
    }

    private void StopSounds_Click(object sender, RoutedEventArgs e) => StopAllSounds();

    private void StopAllSounds()
    {
        _soundEngine.StopAll();
        // Nur die Sounds stoppen – Dauersenden läuft weiter (aus nur per Umschalt-Taste oder Notfall-Taste)
        _pttPending.Clear();
        _pttReleaseTimer.Stop();
        if (!_pttLatched) ReleasePtt("alle Sounds gestoppt");
        UpdateSoundPlayingState();
    }

    private void SoundNormalize_Click(object sender, RoutedEventArgs e)
    {
        _settings.SoundNormalize = SoundNormalizeCheck.IsChecked == true;
        _settings.Save();
        if (_settings.SoundNormalize) SoundLevels.Prepare(_allSounds.Select(s => s.File.FullPath));
    }

    // ---------- Umbenennen / Löschen ----------

    private void RenameSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundItem item) return;
        CancelCapture();
        string? name = InputDialog.Ask(this, "Sound umbenennen", "Neuer Name des Sounds:", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;

        try
        {
            _soundEngine.StopAll(); // laufende Sounds halten die Datei offen
            string newFile = SoundLibrary.Rename(item.File, name);

            // Shortcuts (in allen Charakteren) und den Effekt-Schalter mitnehmen
            foreach (var p in _settings.Profiles)
            {
                if (p.SoundHotkeys.Remove(item.FileName, out var hk)) p.SoundHotkeys[newFile] = hk;
                if (p.Sounds.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase)) > 0) p.Sounds.Add(newFile);
            }
            if (_settings.SoundsWithEffect.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase)) > 0)
                _settings.SoundsWithEffect.Add(newFile);
            if (_settings.SoundRepeat.Remove(item.FileName, out int repeat)) _settings.SoundRepeat[newFile] = repeat;
            _settings.Save();
            LoadSounds();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Umbenennen fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    private void DeleteSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoundItem item) return;
        CancelCapture();
        var answer = MessageBox.Show(this,
            $"Sound „{item.Name}“ löschen?\n\nDie Datei wandert in den Papierkorb, die Shortcuts dazu werden in allen Charakteren entfernt.",
            "Sound löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _soundEngine.StopAll();
            SoundLibrary.DeleteToRecycleBin(item.File);
            foreach (var p in _settings.Profiles)
            {
                p.SoundHotkeys.Remove(item.FileName);
                p.Sounds.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
            }
            _settings.SoundsWithEffect.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
            _settings.SoundRepeat.Remove(item.FileName);
            _settings.Save();
            LoadSounds();
            ShowStatusMessage($"„{item.Name}“ in den Papierkorb verschoben.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Löschen fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    private void SoundVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _settings.SoundVolume = SoundVolumeSlider.Value;
        _settings.SoundMonitorVolume = SoundMonitorSlider.Value;
        _soundEngine.SetVolume(_settings.SoundVolume, _settings.SoundMonitorVolume);
        _settings.Save();
    }

    private void SoundMonitor_Click(object sender, RoutedEventArgs e)
    {
        _settings.SoundMonitor = SoundMonitorCheck.IsChecked == true;
        _settings.Save();
        RestartSoundEngine();
    }

    private void SoundDeviceBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string device = SoundDeviceBox.Text.Trim();
        if (device.Length == 0 || device == _settings.SoundOutputDevice) return;
        _settings.SoundOutputDevice = device;
        _settings.Save();
        RestartSoundEngine();
    }

    private void OpenSoundFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SoundLibrary.Folder);
        Process.Start("explorer.exe", $"\"{SoundLibrary.Folder}\"");
    }

    private void Sounds_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(SoundLibrary.IsSupported);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Sounds_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        try
        {
            int n = ImportSounds(files);
            if (n == 0) ShowSoundError("Keine unterstützten Audiodateien dabei (wav, mp3, m4a, aac, wma, flac, aiff).");
            LoadSounds();
        }
        catch (Exception ex) { ShowSoundError("Import fehlgeschlagen: " + ex.Message); }
    }
}
