using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RpVoiceSwitcher;

// Einrichtungs-Check
public partial class MainWindow
{
    private readonly ObservableCollection<CheckItem> _checks = new();
    private PanelWindow? _setupWindow;
    private PanelWindow? _settingsWindow;

    /// <summary>Hängt Einrichtung und Einstellungen aus dem Hauptfenster in eigene Fenster um (Menü Extras).</summary>
    private void CreatePanelWindows()
    {
        DetachedPanels.Children.Remove(SetupPanel);
        DetachedPanels.Children.Remove(SettingsPanel);
        _setupWindow = new PanelWindow(this, "Einrichtung – RP Voice Switcher", SetupPanel, 720, 720);
        _setupWindow.Activated += (_, _) => RunSetupCheck(); // z. B. nach Änderungen in den Windows-Soundeinstellungen
        _settingsWindow = new PanelWindow(this, "Einstellungen – RP Voice Switcher", SettingsPanel, 720, 640);
    }

    private void ShowSetupWindow()
    {
        RunSetupCheck();
        _setupWindow?.ShowAndActivate();
    }

    private void ShowSettingsWindow()
    {
        RefreshFolderTexts();
        _settingsWindow?.ShowAndActivate();
    }
    private DeviceWatcher? _deviceWatcher;
    private readonly System.Windows.Threading.DispatcherTimer _deviceDebounce = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    /// <summary>Bei An-/Abstecken von Geräten neu prüfen und das Soundboard auf das (neue) Standardgerät setzen.</summary>
    private void WatchDevices()
    {
        _deviceDebounce.Tick += (_, _) =>
        {
            _deviceDebounce.Stop();
            RunSetupCheck();
            RestartSoundEngine();
        };
        _deviceWatcher = new DeviceWatcher(() => Dispatcher.InvokeAsync(() =>
        {
            if (_exiting) return;
            _deviceDebounce.Stop();
            _deviceDebounce.Start();
        }));
    }

    private void RunSetupCheck()
    {
        _checks.Clear();
        try
        {
            var results = SetupChecker.Run(_store, _settings, RepairConfig, SetTargetDevice);
            foreach (var r in results) _checks.Add(new CheckItem(r));

            int errors = results.Count(r => r.State == CheckState.Error);
            int warnings = results.Count(r => r.State == CheckState.Warning);
            SetupSummary.Text = errors + warnings == 0
                ? "Alles eingerichtet. Im Spiel und in Discord „CABLE Output“ als Mikrofon wählen."
                : $"{errors} Problem(e), {warnings} Hinweis(e). Klicke bei den roten Punkten auf den Button daneben.";

            SetupStatusText.Text = errors == 0 ? (warnings == 0 ? "✓ Einrichtung OK" : $"Einrichtung: {warnings} Hinweis(e)") : (errors == 1 ? "⚠ 1 Problem bei der Einrichtung" : $"⚠ {errors} Probleme bei der Einrichtung");
            SetupStatusLink.Foreground = errors > 0 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x80, 0x7A)) : (System.Windows.Media.Brush)FindResource(errors + warnings == 0 ? "FgDim" : "Accent");
        }
        catch (Exception ex)
        {
            SetupSummary.Text = "Prüfung fehlgeschlagen: " + ex.Message;
        }
    }

    private void RepairConfig()
    {
        _store?.EnsureInstalled(BundledVoicesDir);
        var active = CurrentVoice() ?? NormalVoice();
        if (active != null) ApplyVoice(active, quiet: true);
    }

    private void RunSetupCheck_Click(object sender, RoutedEventArgs e) => RunSetupCheck();

    private void SetupWarning_Click(object sender, RoutedEventArgs e) => ShowSetupWindow();

    private void Fix_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CheckItem item || item.Result.Fix == null) return;
        try
        {
            item.Result.Fix();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Das hat nicht geklappt: " + ex.Message, "RP Voice Switcher");
        }
        // Automatische Reparaturen sofort sichtbar machen; bei geöffneten Windows-Dialogen
        // wird beim Zurückkehren ins Fenster erneut geprüft (Activated-Event).
        RunSetupCheck();
    }
}
