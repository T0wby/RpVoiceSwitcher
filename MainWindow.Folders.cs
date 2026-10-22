using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RpVoiceSwitcher;

// Einstellbare Ordner für Stimmen und Sounds
public partial class MainWindow
{
    private void RefreshFolderTexts()
    {
        VoicesFolderText.Text = _store?.VoicesDir ?? "(Equalizer APO nicht gefunden)";
        VoicesFolderHint.Text = _store == null || _store.UsesDefaultVoicesDir ? "Standard (Equalizer-APO-Ordner)" : "Eigener Ordner";
        SoundFolderText.Text = SoundLibrary.Folder;
        SoundFolderHint.Text = string.Equals(SoundLibrary.Folder, SoundLibrary.DefaultFolder, StringComparison.OrdinalIgnoreCase)
            ? "Standard (Dokumente)" : "Eigener Ordner";
    }

    private string? PickFolder(string title, string current)
    {
        var dlg = new OpenFolderDialog { Title = title, InitialDirectory = Directory.Exists(current) ? current : "" };
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    /// <summary>Ja = Dateien mitnehmen, Nein = nur umstellen, null = abbrechen.</summary>
    private bool? AskTakeFiles(string what, string target)
    {
        var answer = MessageBox.Show(this,
            $"Sollen deine vorhandenen {what} nach\n{target}\nkopiert werden?\n\nJa = kopieren (der alte Ordner bleibt unverändert)\nNein = nur umstellen",
            "Ordner wechseln", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }

    // ---------- Stimmen ----------

    private void ChangeVoicesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        string? folder = PickFolder("Ordner für die Stimmen wählen", _store.VoicesDir);
        if (folder == null) return;
        if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(_store.ConfigDir), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Bitte nicht den Equalizer-APO-Ordner selbst wählen – dort liegen dessen eigene Dateien.\nEin Unterordner oder ein Ordner z. B. in den Dokumenten passt.", "RP Voice Switcher");
            return;
        }
        SwitchVoicesFolder(folder);
    }

    private void ResetVoicesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null || _store.UsesDefaultVoicesDir) return;
        SwitchVoicesFolder(null);
    }

    private void SwitchVoicesFolder(string? folder)
    {
        if (_store == null) return;
        string target = folder ?? _store.DefaultVoicesDir;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(_store.VoicesDir), StringComparison.OrdinalIgnoreCase)) return;

        bool? take = AskTakeFiles("Stimmen", target);
        if (take == null) return;
        try
        {
            if (take == true) VoiceStore.CopyVoicesTo(_store.VoicesDir, target);
            _settings.VoicesFolder = folder;
            _settings.Save();

            _store = VoiceStore.TryCreate(folder);
            _store?.EnsureInstalled(BundledVoicesDir);
            WatchVoicesFolder();
            LoadVoices();
            EnsureActiveVoiceInProfile();
            RefreshFolderTexts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ordner konnte nicht gewechselt werden: " + ex.Message, "RP Voice Switcher");
        }
    }

    // ---------- Sounds ----------

    private void ChangeSoundFolder_Click(object sender, RoutedEventArgs e)
    {
        string? folder = PickFolder("Ordner für das Soundboard wählen", SoundLibrary.Folder);
        if (folder != null) SwitchSoundFolder(folder);
    }

    private void ResetSoundFolder_Click(object sender, RoutedEventArgs e) => SwitchSoundFolder(null);

    private void SwitchSoundFolder(string? folder)
    {
        string target = folder ?? SoundLibrary.DefaultFolder;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(SoundLibrary.Folder), StringComparison.OrdinalIgnoreCase)) return;

        bool? take = AskTakeFiles("Sounds", target);
        if (take == null) return;
        try
        {
            if (take == true) SoundLibrary.CopyAll(SoundLibrary.Folder, target);
            _settings.SoundFolder = folder;
            _settings.Save();
            SoundLibrary.Folder = target;
            _soundEngine.StopAll();
            PrepareSoundFolder();
            RefreshFolderTexts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ordner konnte nicht gewechselt werden: " + ex.Message, "RP Voice Switcher");
        }
    }
}
