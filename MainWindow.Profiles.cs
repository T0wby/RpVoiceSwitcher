using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RpVoiceSwitcher;

// Charakter-Profile: eigene Stimmenauswahl und Tastenbelegung pro Charakter
public partial class MainWindow
{
    private readonly ObservableCollection<ProfileChip> _profileChips = new();
    private bool _selectingVoices;

    private void RefreshProfileChips()
    {
        _profileChips.Clear();
        foreach (var p in _settings.Profiles)
            _profileChips.Add(new ProfileChip(p.Name, p == _settings.Current));
        DeleteProfileButton.IsEnabled = _settings.Profiles.Count > 1;
    }

    private void ProfileChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProfileChip chip) SwitchProfile(chip.Name);
    }

    private void SwitchProfile(string name)
    {
        var profile = _settings.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (profile == null) return;

        CancelCapture();
        bool changed = profile != _settings.Current;
        if (changed && _selectingVoices) SetSelectingVoices(false); // Auswahl gilt nur für den bisherigen Charakter
        _holdHook.ReleaseHeld();
        _holdVoice = null;
        _settings.ActiveProfile = profile.Name;
        _settings.Save();

        RefreshProfileChips();
        RefreshVoiceList();
        RegisterAllBindings();

        EnsureActiveVoiceInProfile();
        UpdateStatus();

        if (changed && _settings.ShowOverlay)
        {
            _overlay ??= new OverlayWindow();
            _overlay.Flash("Charakter: " + profile.Name);
        }
    }

    /// <summary>Aktive Stimme gehört nicht zum Charakter → auf Normal zurück, damit man nicht "falsch" klingt.</summary>
    private void EnsureActiveVoiceInProfile()
    {
        var active = CurrentVoice();
        if (active != null && _settings.Current.ContainsVoice(active.FileName)) return;
        var normal = NormalVoice();
        if (normal == null) return;
        _baseVoice = normal;
        ApplyVoice(normal, quiet: true);
    }

    private string? AskProfileName(string title, string initial, CharacterProfile? except = null)
    {
        while (true)
        {
            string? name = InputDialog.Ask(this, title, "Name des Charakters:", initial);
            if (string.IsNullOrWhiteSpace(name)) return null;
            bool taken = _settings.Profiles.Any(p => p != except && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!taken) return name;
            MessageBox.Show(this, $"Es gibt schon einen Charakter „{name}“.", "RP Voice Switcher");
            initial = name;
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = AskProfileName("Neuer Charakter", "");
        if (name == null) return;

        _settings.Profiles.Add(new CharacterProfile { Name = name, IncludeAllVoices = false });
        SwitchProfile(name);
        SetSelectingVoices(true); // direkt Stimmen auswählen lassen
        Tabs.SelectedIndex = 0;
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _settings.Current;
        string? name = AskProfileName("Charakter umbenennen", profile.Name, profile);
        if (name == null || name == profile.Name) return;
        profile.Name = name;
        _settings.ActiveProfile = name;
        _settings.Save();
        RefreshProfileChips();
        UpdateStatus();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Profiles.Count <= 1) return;
        var profile = _settings.Current;
        var answer = MessageBox.Show(this,
            $"Charakter „{profile.Name}“ mit seiner Stimmenauswahl und allen Shortcuts löschen?\n\nDie Stimmen selbst bleiben erhalten.",
            "Charakter löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _settings.Profiles.Remove(profile);
        SetSelectingVoices(false);
        SwitchProfile(_settings.Profiles[0].Name);
    }

    // ---------- Stimmen auswählen ----------

    private void SelectVoices_Click(object sender, RoutedEventArgs e) => SetSelectingVoices(!_selectingVoices);

    private void SetSelectingVoices(bool on)
    {
        CancelCapture();
        _selectingVoices = on;
        SelectVoicesButton.Content = on ? "Fertig" : "Stimmen auswählen";
        VoiceHint.Text = on
            ? $"Hake die Stimmen an, die zu „{_settings.Current.Name}“ gehören. „Normal“ ist immer dabei."
            : "Umschalten = einmal drücken. Halten = nur solange die Taste gedrückt ist.";
        RefreshVoiceList();
        if (!on) RegisterAllBindings();
    }

    private void ProfileVoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VoiceItem item || item.IsNormal) return;
        var profile = _settings.Current;

        // "Alle Stimmen" in eine feste Liste umwandeln, sobald etwas abgewählt wird.
        if (profile.IncludeAllVoices)
        {
            profile.IncludeAllVoices = false;
            profile.Voices = _allVoices.Where(v => !v.IsNormal).Select(v => v.FileName).ToList();
        }

        profile.Voices.RemoveAll(v => v.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
        if (item.InProfile) profile.Voices.Add(item.FileName);
        _settings.Save();
    }
}
