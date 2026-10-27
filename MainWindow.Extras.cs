using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace RpVoiceSwitcher;

// Extras: OBS-Textdatei, Probehören, Update-Prüfung, Notfall-Eintrag im Menü
public partial class MainWindow
{
    private VoiceTestWindow? _voiceTest;

    /// <summary>Einstellungs-Felder der Extras beim Start füllen.</summary>
    private void InitExtras()
    {
        StatusHideNormalCheck.IsChecked = _settings.StatusOverlayHideNormal;
        StatusSoundsCheck.IsChecked = _settings.StatusOverlayShowSounds;
        StatusScaleSlider.Value = Math.Clamp(_settings.StatusOverlayScale, StatusScaleSlider.Minimum, StatusScaleSlider.Maximum);
        StatusOpacitySlider.Value = Math.Clamp(_settings.StatusOverlayOpacity, StatusOpacitySlider.Minimum, StatusOpacitySlider.Maximum);
        UpdateStatusLookTexts();

        ObsCheck.IsChecked = _settings.ObsFileEnabled;
        ObsTemplateBox.Text = _settings.ObsTemplate;
        RefreshObsTexts();

        UpdateCheck.IsChecked = _settings.CheckForUpdates;
        VersionText.Text = $"Installiert: Version {UpdateChecker.CurrentVersionText} · Updates kommen von github.com/{UpdateChecker.Repository}";

        if (_settings.CheckForUpdates)
        {
            // Etwas warten, damit der Start nicht gebremst wird.
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) => { timer.Stop(); _ = CheckForUpdatesAsync(manual: false); };
            timer.Start();
        }
    }

    // ---------- OBS-Textdatei ----------

    private string? _lastObsText;

    private static string DefaultObsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RP Voice Switcher", "aktive-stimme.txt");

    private string ObsFile => string.IsNullOrWhiteSpace(_settings.ObsFilePath) ? DefaultObsFile : _settings.ObsFilePath!;

    private string ObsText()
    {
        var active = CurrentVoice();
        string voice = active?.Name ?? "Keine Stimme";
        string effect = active == null || active.IsNormal ? "" : active.Name;
        return (_settings.ObsTemplate ?? "{stimme}")
            .Replace("{stimme}", voice, StringComparison.OrdinalIgnoreCase)
            .Replace("{charakter}", _settings.Current.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{effekt}", effect, StringComparison.OrdinalIgnoreCase)
            .Replace("\\n", Environment.NewLine);
    }

    /// <summary>Schreibt die aktive Stimme in die Datei (nur wenn sich der Text geändert hat).</summary>
    private void WriteObsFile(bool force = false)
    {
        if (!_settings.ObsFileEnabled) return;
        string text = ObsText();
        if (!force && text == _lastObsText) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ObsFile)!);
            File.WriteAllText(ObsFile, text, new UTF8Encoding(false));
            _lastObsText = text;
        }
        catch (Exception ex)
        {
            ShowStatusMessage("OBS-Datei konnte nicht geschrieben werden: " + ex.Message);
        }
        if (ObsPreviewText != null) ObsPreviewText.Text = "→ " + text.Replace(Environment.NewLine, " ⏎ ");
    }

    private void RefreshObsTexts()
    {
        ObsFileText.Text = ObsFile;
        ObsPreviewText.Text = "→ " + ObsText().Replace(Environment.NewLine, " ⏎ ");
    }

    private void Obs_Changed(object sender, RoutedEventArgs e)
    {
        _settings.ObsFileEnabled = ObsCheck.IsChecked == true;
        _settings.Save();
        WriteObsFile(force: true);
        if (_settings.ObsFileEnabled) ShowStatusMessage("OBS-Datei: " + ObsFile);
    }

    private void ObsTemplate_LostFocus(object sender, RoutedEventArgs e)
    {
        string template = string.IsNullOrWhiteSpace(ObsTemplateBox.Text) ? "{stimme}" : ObsTemplateBox.Text;
        ObsTemplateBox.Text = template;
        if (template == _settings.ObsTemplate) return;
        _settings.ObsTemplate = template;
        _settings.Save();
        RefreshObsTexts();
        WriteObsFile(force: true);
    }

    private void ChangeObsFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Datei für OBS",
            FileName = Path.GetFileName(ObsFile),
            InitialDirectory = Path.GetDirectoryName(ObsFile),
            Filter = "Textdatei (*.txt)|*.txt",
            DefaultExt = ".txt",
            OverwritePrompt = false,
        };
        if (dlg.ShowDialog(_settingsWindow ?? (Window)this) != true) return;
        _settings.ObsFilePath = dlg.FileName.Equals(DefaultObsFile, StringComparison.OrdinalIgnoreCase) ? null : dlg.FileName;
        _settings.Save();
        RefreshObsTexts();
        WriteObsFile(force: true);
    }

    private void OpenObsFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = Path.GetDirectoryName(ObsFile)!;
        Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", $"\"{dir}\"");
    }

    // ---------- Diagnose / Problem melden ----------

    private string BuildDiagnostics()
    {
        RunSetupCheck();
        return Diagnostics.Build(_settings, _store, _checks.Select(c => c.Result), CurrentVoice()?.Name ?? "keine");
    }

    private static bool CopyToClipboard(string text)
    {
        // Die Zwischenablage ist manchmal kurz von einem anderen Programm belegt.
        for (int i = 0; i < 10; i++)
        {
            try { Clipboard.SetDataObject(text, true); return true; }
            catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(100); }
        }
        return false;
    }

    private void CopyDiagnostics()
    {
        if (CopyToClipboard(BuildDiagnostics()))
            ShowStatusMessage("Diagnose in die Zwischenablage kopiert – mit Strg+V einfügen.");
        else
            MessageBox.Show(this, "Die Zwischenablage ist gerade belegt. Bitte gleich nochmal versuchen.", "Diagnose kopieren");
    }

    private void ReportProblem()
    {
        bool copied = CopyToClipboard(BuildDiagnostics());
        var answer = MessageBox.Show(_setupWindow is { IsVisible: true } ? _setupWindow : this,
            (copied ? "Die Diagnose liegt in der Zwischenablage.\n\n" : "") +
            "Gleich öffnet sich GitHub (Anmeldung nötig). Dort kurz beschreiben, was nicht klappt, und die Diagnose ins Feld „Diagnose“ einfügen (Strg+V).\n\n" +
            "Die Diagnose enthält Geräte- und Einrichtungsinfos, aber keine Charakternamen und nicht deinen Windows-Benutzernamen. Lies sie trotzdem kurz durch, bevor du sie veröffentlichst.",
            "Problem melden", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer == MessageBoxResult.OK) SetupChecker.OpenUrl(Diagnostics.IssueUrl);
    }

    // ---------- Probehören ----------

    private void ShowVoiceTest()
    {
        if (_store == null) return;
        if (_voiceTest is { IsLoaded: true })
        {
            _voiceTest.RefreshVoices();
            _voiceTest.Activate();
            return;
        }
        if (!IsVisible) ShowFromTray();
        _voiceTest = new VoiceTestWindow(this,
            () => ProfileVoices().ToList(),
            CurrentVoice,
            ActivateVoice,
            SetVoiceGain,
            () => _settings.TargetDevice);
        _voiceTest.Closed += (_, _) => _voiceTest = null;
        _voiceTest.Show();
    }

    private void VoiceTest_Click(object sender, RoutedEventArgs e) => ShowVoiceTest();

    // ---------- Updates ----------

    private UpdateChecker.Release? _availableUpdate;
    private bool _checkingUpdate;
    private bool _updateBalloonShown;

    private void UpdateCheck_Changed(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdates = UpdateCheck.IsChecked == true;
        _settings.Save();
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        if (manual) ShowStatusMessage("Suche nach Updates …");
        UpdateChecker.Result result;
        try { result = await UpdateChecker.CheckAsync(); }
        finally { _checkingUpdate = false; }
        if (_exiting) return;

        Window owner = _settingsWindow is { IsVisible: true } ? _settingsWindow : this;
        switch (result.State)
        {
            case UpdateChecker.State.UpdateAvailable when result.Release != null:
                _availableUpdate = result.Release;
                string v = UpdateChecker.Format(result.Release.Version);
                if (!manual && _settings.SkippedUpdateVersion == v) break; // bewusst übersprungen
                UpdateStatusText.Text = $"Update {v} verfügbar";
                UpdateStatusLink.ToolTip = "Klicken zum Installieren";
                UpdateStatusItem.Visibility = UpdateStatusSeparator.Visibility = Visibility.Visible;
                if (manual) OfferUpdate(owner);
                else if (!IsVisible && !_updateBalloonShown)
                {
                    _updateBalloonShown = true;
                    _tray.ShowBalloonTip(4000, "RP Voice Switcher", $"Version {v} ist verfügbar. Fenster öffnen → unten rechts auf „Update“ klicken.", WinForms.ToolTipIcon.Info);
                }
                break;
            case UpdateChecker.State.UpToDate:
                if (manual) MessageBox.Show(owner, $"Du hast die neueste Version ({UpdateChecker.CurrentVersionText}).", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case UpdateChecker.State.NoChannel:
                if (manual) MessageBox.Show(owner, $"Für RP Voice Switcher wurden noch keine Updates veröffentlicht (github.com/{UpdateChecker.Repository}).", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            default:
                if (manual) MessageBox.Show(owner, "Die Update-Prüfung hat nicht geklappt: " + result.Error + "\n\nBesteht eine Internetverbindung?", "Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
        if (manual && result.State != UpdateChecker.State.UpdateAvailable) ShowStatusMessage("Update-Prüfung abgeschlossen.");
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e) => OfferUpdate(this);

    // ---------- Was ist neu (nach einem Update) ----------

    /// <summary>
    /// Einmal nach einem Update zeigen, was sich seit der zuletzt benutzten Version geändert hat.
    /// Frische Installation: nichts zeigen (da läuft der Einrichtungs-Assistent), nur merken.
    /// </summary>
    private void MaybeShowWhatsNew()
    {
        var current = UpdateChecker.CurrentVersion;
        Version? last = UpdateChecker.TryParseVersion(_settings.LastSeenVersion, out var v) ? v : null;
        if (last != null && last >= current) return;

        if (last == null)
        {
            // Testläufe (--settings) ohne ausdrücklich gesetzte Vorversion nicht stören
            if (AppSettings.OverridePath != null) return;
            if (!_settings.SetupWizardDone)
            {
                _settings.LastSeenVersion = UpdateChecker.CurrentVersionText;
                _settings.Save();
                return;
            }
        }

        _settings.LastSeenVersion = UpdateChecker.CurrentVersionText;
        _settings.Save();
        var entries = Changelog.Since(last, current);
        if (entries.Count == 0) return;

        if (IsVisible) ChangelogWindow.ShowWhatsNew(this, entries, last);
        else
        {
            // Im Hintergrund gestartet (Autostart): zeigen, sobald das Fenster das erste Mal aufgeht
            DependencyPropertyChangedEventHandler? handler = null;
            handler = (_, _) =>
            {
                if (!IsVisible) return;
                IsVisibleChanged -= handler;
                ChangelogWindow.ShowWhatsNew(this, entries, last);
            };
            IsVisibleChanged += handler;
        }
    }

    private async void OfferUpdate(Window owner)
    {
        var release = _availableUpdate;
        if (release == null) return;
        string v = UpdateChecker.Format(release.Version);
        string notes = Changelog.ToPlainText(release.Notes);
        if (notes.Length > 700) notes = notes[..700].TrimEnd() + " …";

        var answer = MessageBox.Show(owner,
            $"Version {v} ist verfügbar (installiert: {UpdateChecker.CurrentVersionText}).\n\n" +
            (notes.Length > 0 ? notes + "\n\n" : "") +
            "Ja  =  jetzt herunterladen und installieren (die App startet danach neu)\n" +
            "Nein  =  diese Version überspringen\n" +
            "Abbrechen  =  später",
            "Update verfügbar", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);

        if (answer == MessageBoxResult.No)
        {
            _settings.SkippedUpdateVersion = v;
            _settings.Save();
            UpdateStatusItem.Visibility = UpdateStatusSeparator.Visibility = Visibility.Collapsed;
            return;
        }
        if (answer != MessageBoxResult.Yes) return;

        if (release.InstallerUrl == null)
        {
            SetupChecker.OpenUrl(release.PageUrl); // kein Installer angehängt – Seite öffnen
            return;
        }

        try
        {
            UpdateStatusLink.IsEnabled = false;
            var progress = new Progress<double>(p => UpdateStatusText.Text = $"Update wird geladen … {p * 100:0} %");
            string installer = await UpdateChecker.DownloadAsync(release.InstallerUrl, progress);
            UpdateStatusText.Text = "Update wird installiert …";
            // /SILENT zeigt nur den Fortschritt; der Installer schließt diese App und startet sie danach wieder.
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            _keepLatchOnExit = true; // Dauersenden läuft nach dem Update weiter
            ExitApp();
        }
        catch (Exception ex)
        {
            UpdateStatusLink.IsEnabled = true;
            UpdateStatusText.Text = $"Update {v} verfügbar";
            MessageBox.Show(owner, "Das Update konnte nicht geladen werden: " + ex.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
