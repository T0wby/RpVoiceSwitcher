using System;
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
    private readonly ObservableCollection<SoundItem> _sounds = new();
    private readonly SoundEngine _soundEngine = new();
    private FileSystemWatcher? _soundWatcher;
    private readonly DispatcherTimer _soundReloadDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void InitSoundboard()
    {
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
        _sounds.Clear();
        try
        {
            foreach (var file in SoundLibrary.Load())
                _sounds.Add(new SoundItem(file)
                {
                    ThroughEffect = _settings.SoundsWithEffect.Contains(file.FileName, StringComparer.OrdinalIgnoreCase),
                });
        }
        catch (Exception ex) { ShowSoundError("Sound-Ordner konnte nicht gelesen werden: " + ex.Message); }
        RegisterAllBindings();
        UpdateSoundStatus();
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

    private void PlaySound(SoundItem item)
    {
        if (!File.Exists(item.File.FullPath)) { LoadSounds(); return; }
        if (!_soundEngine.Play(item.File.FullPath, bypassEffect: !item.ThroughEffect)) ShowSoundError(_soundEngine.Error);
        else if (SoundError.Visibility == Visibility.Visible && _soundEngine.Error == null) ShowSoundError(null);
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

    private void StopSounds_Click(object sender, RoutedEventArgs e) => _soundEngine.StopAll();

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
                if (p.SoundHotkeys.Remove(item.FileName, out var hk)) p.SoundHotkeys[newFile] = hk;
            if (_settings.SoundsWithEffect.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase)) > 0)
                _settings.SoundsWithEffect.Add(newFile);
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
            foreach (var p in _settings.Profiles) p.SoundHotkeys.Remove(item.FileName);
            _settings.SoundsWithEffect.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
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
            int n = SoundLibrary.Import(files);
            if (n == 0) ShowSoundError("Keine unterstützten Audiodateien dabei (wav, mp3, m4a, aac, wma, flac, aiff).");
            LoadSounds();
        }
        catch (Exception ex) { ShowSoundError("Import fehlgeschlagen: " + ex.Message); }
    }
}
