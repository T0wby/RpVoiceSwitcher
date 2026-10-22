using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace RpVoiceSwitcher;

// Stimmen direkt in der App anlegen, bearbeiten, kopieren, löschen und importieren
public partial class MainWindow
{
    private void NewVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        var result = VoiceDialog.Ask(this, "Neue Stimme",
            "Legt eine leere Stimme an. Die Effekte baust du danach im EAPO-Editor ein – oder du kopierst eine bestehende Stimme als Vorlage (Kopieren-Button an der Stimme).",
            new VoiceDialog.Result("", "", ""));
        if (result == null) return;
        CreateVoiceFile(result, body: null);
    }

    private void CopyVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null || (sender as FrameworkElement)?.DataContext is not VoiceItem item) return;
        var result = VoiceDialog.Ask(this, "Stimme kopieren",
            $"Erstellt eine Kopie von „{item.Name}“ mit allen Effekten (auch Graillon). Gut, um Varianten auszuprobieren.",
            new VoiceDialog.Result(item.Name + " (Kopie)", item.Description, item.Preset.GraillonHint, item.Preset.GainDb));
        if (result == null) return;
        CreateVoiceFile(result, _store.ReadBody(item.Preset).ToList());
    }

    private void CreateVoiceFile(VoiceDialog.Result result, System.Collections.Generic.List<string>? body)
    {
        try
        {
            string file = _store!.CreateVoice(result.Name, result.Description, result.Graillon, body, result.GainDb);
            AddToCurrentProfile(file);
            LoadVoices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Stimme konnte nicht angelegt werden: " + ex.Message, "RP Voice Switcher");
        }
    }

    /// <summary>Neue Stimmen sollen im aktuellen Charakter direkt sichtbar sein.</summary>
    private void AddToCurrentProfile(string fileName)
    {
        var profile = _settings.Current;
        if (profile.IncludeAllVoices || profile.ContainsVoice(fileName)) return;
        profile.Voices.Add(fileName);
        _settings.Save();
    }

    private void EditVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null || (sender as FrameworkElement)?.DataContext is not VoiceItem item) return;
        // Ist die Stimme gerade aktiv, hört man den Lautstärke-Regler sofort.
        Action<double>? preview = item.IsActive
            ? gain =>
            {
                // Nur solange sie aktiv ist – per Shortcut kann inzwischen umgeschaltet worden sein.
                try { if (item.IsActive) _store.Activate(item.Preset with { GainDb = gain }, _settings.TargetDevice); }
                catch { /* nur Vorschau */ }
            }
            : null;
        var result = VoiceDialog.Ask(this, "Stimme bearbeiten",
            "Ändert Name, Texte und Lautstärke – die Effekte der Stimme bleiben, wie sie sind.",
            new VoiceDialog.Result(item.Name, item.Description, item.Preset.GraillonHint, item.Preset.GainDb), preview);
        if (result == null) return;
        try
        {
            _store.UpdateHeader(item.Preset, result.Name, result.Description, result.Graillon, result.GainDb);
            LoadVoices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Speichern fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    /// <summary>Setzt nur den Lautstärke-Ausgleich (z. B. vom Probehören vorgeschlagen).</summary>
    private void SetVoiceGain(VoiceItem item, double gainDb)
    {
        if (_store == null) return;
        try
        {
            _store.UpdateHeader(item.Preset, item.Name, item.Description, item.Preset.GraillonHint, gainDb);
            LoadVoices();
            ShowStatusMessage($"Lautstärke von „{item.Name}“: {VoiceStore.FormatGain(gainDb)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Speichern fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    // ---------- Exportieren ----------

    private void ExportVoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VoiceItem item) return;
        var dlg = new SaveFileDialog
        {
            Title = $"Stimme „{item.Name}“ exportieren",
            FileName = item.FileName,
            Filter = "Stimme (*.txt)|*.txt",
            DefaultExt = ".txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.Copy(item.Preset.FullPath, dlg.FileName, overwrite: true);
            ShowStatusMessage($"„{item.Name}“ exportiert.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    /// <summary>Alle Stimmen des aktuellen Charakters als ZIP (ohne "Normal" – die hat jeder).</summary>
    private void ExportVoices()
    {
        var voices = ProfileVoices().Where(v => !v.IsNormal).Select(v => v.Preset).ToList();
        if (voices.Count == 0)
        {
            MessageBox.Show(this, "Dieser Charakter hat noch keine eigenen Stimmen zum Exportieren.", "Stimmen exportieren");
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = $"Stimmen von „{_settings.Current.Name}“ exportieren",
            FileName = SafeFileName(_settings.Current.Name) + " - Stimmen.zip",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            VoiceStore.ExportZip(voices, dlg.FileName);
            ShowStatusMessage(voices.Count == 1 ? "1 Stimme exportiert." : $"{voices.Count} Stimmen exportiert.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "Charakter" : clean;
    }

    /// <summary>Importiert .txt-Stimmen und ZIP-Archive. Liefert die Anzahl neuer Stimmen.</summary>
    private int ImportVoiceFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
        if (_store == null) return 0;
        int count = 0;
        foreach (var path in paths)
        {
            try
            {
                if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var file in _store.ImportZip(path)) { AddToCurrentProfile(file); count++; }
                }
                else
                {
                    // Dateien, die schon im Stimmen-Ordner liegen, nicht doppelt anlegen.
                    if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(_store.VoicesDir), StringComparison.OrdinalIgnoreCase))
                        continue;
                    AddToCurrentProfile(_store.ImportVoice(path));
                    count++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"„{Path.GetFileName(path)}“ konnte nicht übernommen werden: {ex.Message}", "RP Voice Switcher");
            }
        }
        if (count > 0)
        {
            LoadVoices();
            ShowStatusMessage(count == 1 ? "1 Stimme importiert." : $"{count} Stimmen importiert.");
        }
        return count;
    }

    private void DeleteVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null || (sender as FrameworkElement)?.DataContext is not VoiceItem item || item.IsNormal) return;
        var answer = MessageBox.Show(this,
            $"Stimme „{item.Name}“ wirklich löschen?\n\nDie Datei wird entfernt und die Stimme verschwindet aus allen Charakteren samt ihrer Shortcuts.",
            "Stimme löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            // Erst wegschalten – sonst verweist Equalizer APO auf eine Datei, die es nicht mehr gibt.
            if (item.IsActive || ReferenceEquals(_baseVoice, item))
            {
                var normal = NormalVoice();
                if (normal != null) ActivateVoice(normal);
            }
            _store.DeleteVoice(item.Preset);

            foreach (var p in _settings.Profiles)
            {
                p.Hotkeys.Remove(item.FileName);
                p.HoldHotkeys.Remove(item.FileName);
                p.Voices.RemoveAll(v => v.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
            }
            _settings.Save();
            LoadVoices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Löschen fehlgeschlagen: " + ex.Message, "RP Voice Switcher");
        }
    }

    // ---------- Drag & Drop von .txt-Stimmen ----------

    private static bool IsVoiceFile(string path) =>
        (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
         Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) && File.Exists(path);

    private void Voices_DragOver(object sender, DragEventArgs e)
    {
        bool ok = _store != null && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsVoiceFile);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Voices_Drop(object sender, DragEventArgs e)
    {
        if (_store == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        ImportVoiceFiles(files.Where(IsVoiceFile));
    }
}
