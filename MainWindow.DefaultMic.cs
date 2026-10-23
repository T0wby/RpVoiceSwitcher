using System;
using System.Collections.Generic;
using System.Windows;

namespace RpVoiceSwitcher;

// Windows-Standardmikrofon auf dem Kabel halten – Steam-Sprachchat (z. B. nanos world) & Co. nehmen einfach den Standard.
public partial class MainWindow
{
    private readonly Queue<DateTime> _defaultMicFixes = new();
    private bool _defaultMicGaveUp;

    private void InitDefaultMic()
    {
        KeepDefaultMicCheck.IsChecked = _settings.KeepCableDefaultMic;
        EnforceDefaultMic();
        UpdateDefaultMicText();
    }

    private void KeepDefaultMic_Click(object sender, RoutedEventArgs e)
    {
        _settings.KeepCableDefaultMic = KeepDefaultMicCheck.IsChecked == true;
        _settings.Save();
        _defaultMicGaveUp = false;
        _defaultMicFixes.Clear();
        EnforceDefaultMic();
        UpdateDefaultMicText();
        RunSetupCheck();
    }

    /// <summary>Bei Geräteänderungen aufgerufen: ggf. zurück aufs Kabel stellen.</summary>
    private void EnforceDefaultMic()
    {
        if (!_settings.KeepCableDefaultMic || _defaultMicGaveUp) return;
        if (AppSettings.OverridePath != null) return; // Testläufe ändern keine Windows-Einstellungen
        var state = DefaultMic.Read(_settings.TargetDevice);
        if (state.Ok || state.CableId == null) return;

        // Notbremse gegen ein Hin und Her mit einem Programm, das ständig zurückstellt
        var now = DateTime.UtcNow;
        while (_defaultMicFixes.Count > 0 && now - _defaultMicFixes.Peek() > TimeSpan.FromMinutes(1)) _defaultMicFixes.Dequeue();
        if (_defaultMicFixes.Count >= 3)
        {
            _defaultMicGaveUp = true;
            ShowStatusMessage("Standardmikrofon: Ein anderes Programm stellt ständig zurück – die App hört auf, dagegen anzukämpfen. Siehe Extras → Einrichtung.");
            UpdateDefaultMicText();
            return;
        }
        _defaultMicFixes.Enqueue(now);

        string from = !state.DefaultIsCable ? state.DefaultName ?? "?" : state.CommsName ?? "?";
        if (DefaultMic.SetCableAsDefault(_settings.TargetDevice) is string error)
            ShowStatusMessage("Standardmikrofon konnte nicht umgestellt werden: " + error);
        else
            ShowStatusMessage($"Windows-Standardmikrofon war „{from}“ – zurück auf „{state.CableName}“ gestellt.");
        UpdateDefaultMicText();
    }

    private void UpdateDefaultMicText()
    {
        var state = DefaultMic.Read(_settings.TargetDevice);
        DefaultMicText.Text = state.CableId == null ? "Kabel nicht gefunden."
            : state.Ok ? $"Jetzt: „{state.CableName}“ ist Standard- und Kommunikationsgerät."
            : $"Jetzt: Standard „{state.DefaultName}“, Kommunikation „{state.CommsName}“." + (_defaultMicGaveUp ? " (Zurückstellen pausiert – ein anderes Programm stellt ständig um.)" : "");
    }
}
