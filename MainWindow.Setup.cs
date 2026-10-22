using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RpVoiceSwitcher;

// Einrichtungs-Check
public partial class MainWindow
{
    private const int SetupTabIndex = 2;
    private readonly ObservableCollection<CheckItem> _checks = new();

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

            SetupWarningButton.Visibility = errors > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetupWarningButton.Content = errors == 1 ? "⚠  1 Problem bei der Einrichtung" : $"⚠  {errors} Probleme bei der Einrichtung";
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

    private void SetupWarning_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = SetupTabIndex;

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
