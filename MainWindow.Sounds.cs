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
        try { SoundLibrary.EnsureStarterSounds(); }
        catch (Exception ex) { ShowSoundError("Start-Sounds konnten nicht angelegt werden: " + ex.Message); }

        LoadSounds();
        RestartSoundEngine();

        _soundReloadDebounce.Tick += (_, _) =>
        {
            _soundReloadDebounce.Stop();
            if (_capture != null) { _soundReloadDebounce.Start(); return; }
            LoadSounds();
        };
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
            foreach (var file in SoundLibrary.Load()) _sounds.Add(new SoundItem(file));
        }
        catch (Exception ex) { ShowSoundError("Sound-Ordner konnte nicht gelesen werden: " + ex.Message); }
        RegisterAllBindings();
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
    }

    private void PlaySound(SoundItem item)
    {
        if (!File.Exists(item.File.FullPath)) { LoadSounds(); return; }
        if (!_soundEngine.Play(item.File.FullPath)) ShowSoundError(_soundEngine.Error);
        else if (SoundError.Visibility == Visibility.Visible && _soundEngine.Error == null) ShowSoundError(null);
    }

    private void PlaySound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItem item) PlaySound(item);
    }

    private void StopSounds_Click(object sender, RoutedEventArgs e) => _soundEngine.StopAll();

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
        Process.Start("explorer.exe", SoundLibrary.Folder);
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
