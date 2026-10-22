using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace RpVoiceSwitcher;

// Einrichtungs-Assistent: Anbindung an die App
public partial class MainWindow
{
    private SetupWizardWindow? _wizard;
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    /// <summary>Beim ersten Öffnen des Fensters: Assistent zeigen, solange die Einrichtung nicht fertig ist.</summary>
    private void MaybeShowWizardOnStart()
    {
        if (_settings.SetupWizardDone) return;
        if (SetupWizardWindow.IsComplete(SetupChecker.Probe(_settings)))
        {
            // Schon alles eingerichtet (z. B. von Hand) – Assistent nicht aufdrängen.
            _settings.SetupWizardDone = true;
            _settings.Save();
            return;
        }
        ShowSetupWizard();
    }

    private void ShowSetupWizard()
    {
        if (_wizard is { IsLoaded: true }) { _wizard.Activate(); return; }
        if (!IsVisible) ShowFromTray();
        _wizard = new SetupWizardWindow(this, new SetupWizardWindow.Host(
            Probe: WizardProbe,
            AutoSetup: WizardAutoSetup,
            OpenDeviceSelector: () =>
            {
                var store = _store ?? VoiceStore.TryCreate(_settings.VoicesFolder);
                if (store == null) throw new InvalidOperationException("Equalizer APO ist noch nicht installiert.");
                SetupChecker.OpenDeviceSelector(store);
            },
            ScheduleReopenAndRestart: ScheduleReopenAndRestart,
            ActivateDemoVoice: () =>
            {
                var demo = DemoVoice();
                if (demo == null) throw new InvalidOperationException("Keine Stimme gefunden – bitte zuerst Schritt 4 („Automatisch einrichten“).");
                ActivateVoice(demo);
            },
            ActivateNormal: () => { if (NormalVoice() is { } n) ActivateVoice(n); },
            OpenVoiceTest: () =>
            {
                if (_store == null) throw new InvalidOperationException("Equalizer APO ist noch nicht eingerichtet.");
                ShowVoiceTest();
            },
            OpenEditor: () => OpenEditor_Click(this, new RoutedEventArgs()),
            DemoVoiceName: () => DemoVoice()?.Name ?? "Maske",
            GetStep: () => _settings.SetupWizardStep,
            SetStep: step => { _settings.SetupWizardStep = step; _settings.Save(); },
            Finish: () =>
            {
                _settings.SetupWizardDone = true;
                _settings.SetupWizardStep = 0;
                _settings.Save();
                RunSetupCheck();
            }));
        _wizard.Closed += (_, _) => { _wizard = null; RunSetupCheck(); };
        _wizard.Show();
    }

    /// <summary>Die Masken-Stimme als Beispiel – sonst die erste Effekt-Stimme.</summary>
    private VoiceItem? DemoVoice() =>
        _allVoices.FirstOrDefault(v => v.FileName.Equals("01-maske.txt", StringComparison.OrdinalIgnoreCase))
        ?? _allVoices.FirstOrDefault(v => !v.IsNormal);

    private SetupChecker.Status WizardProbe()
    {
        var status = SetupChecker.Probe(_settings);
        // Equalizer APO wurde gerade erst installiert, während die App lief → jetzt anbinden.
        if (status.EapoInstalled && _store == null) TryAttachStore();
        return status;
    }

    /// <summary>Verbindet die App mit Equalizer APO, wenn es erst nach dem Start installiert wurde.</summary>
    private bool TryAttachStore()
    {
        if (_store != null) return true;
        _store = VoiceStore.TryCreate(_settings.VoicesFolder);
        if (_store == null) return false;
        try { _store.EnsureInstalled(BundledVoicesDir); } catch { /* noch keine Schreibrechte – Schritt "Zugriff freigeben" */ }
        LoadVoices();
        EnsureActiveVoiceInProfile();
        WatchVoicesFolder();
        RefreshFolderTexts();
        RunSetupCheck();
        CommandManager_Refresh();
        return true;
    }

    private static void CommandManager_Refresh() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    /// <summary>Schreibrechte (einmal UAC) + Stimmen und config.txt einrichten. Liefert eine Fehlermeldung oder null.</summary>
    private string? WizardAutoSetup()
    {
        if (!TryAttachStore() || _store == null)
            return "Equalizer APO wurde nicht gefunden. Bitte zuerst Schritt 3 erledigen.";

        if (!SetupChecker.Probe(_settings).ConfigWritable)
        {
            try
            {
                SetupChecker.GrantWriteAccess(_store.ConfigDir);
            }
            catch (Win32Exception)
            {
                return "Die Windows-Abfrage wurde abgebrochen. Ohne die Freigabe kann die App die Stimmen nicht umschalten – einfach nochmal versuchen und mit „Ja“ bestätigen.";
            }
            if (!SetupChecker.Probe(_settings).ConfigWritable)
                return "Die Freigabe hat nicht geklappt. Notfalls Extras → Einrichtung → „Rechte freigeben …“ versuchen.";
        }

        try
        {
            _store.EnsureInstalled(BundledVoicesDir); // Stimmen kopieren, "Include: voice.txt" eintragen
            LoadVoices();
            var active = CurrentVoice() ?? NormalVoice();
            if (active != null) ApplyVoice(active, quiet: true);
            WatchVoicesFolder();
            RunSetupCheck();
            return null;
        }
        catch (Exception ex)
        {
            return "Einrichten fehlgeschlagen: " + ex.Message;
        }
    }

    /// <summary>Nach dem Neustart die App (und damit den Assistenten) einmalig wieder öffnen, dann neu starten.</summary>
    private void ScheduleReopenAndRestart()
    {
        _settings.OpenWizardAfterRestart = true;
        _settings.Save();
        // Startet die App ohnehin mit Windows, reicht der Merker – sonst einmalig per RunOnce öffnen.
        if (!IsAutostartEnabled())
            using (var key = Registry.CurrentUser.CreateSubKey(RunOnceKey))
                key.SetValue("RpVoiceSwitcherSetup", $"\"{Environment.ProcessPath}\"");
        SetupChecker.RestartWindows();
        ExitApp();
    }

    /// <summary>Nach dem Neustart aus dem Assistenten: Fenster und Assistent wieder öffnen (auch beim Autostart im Hintergrund).</summary>
    private void ResumeWizardAfterRestart()
    {
        if (!_settings.OpenWizardAfterRestart) return;
        _settings.OpenWizardAfterRestart = false;
        _settings.Save();
        Dispatcher.InvokeAsync(ShowSetupWizard, System.Windows.Threading.DispatcherPriority.Background);
    }
}
