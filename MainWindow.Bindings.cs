using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace RpVoiceSwitcher;

// Tastenbelegung: Umschalten, Halten und Sounds – jeweils pro Charakter
public partial class MainWindow
{
    private enum BindingKind { Toggle, Hold, Sound }

    private sealed record Capture(BindingKind Kind, string FileName, Action<string> SetText);

    private Capture? _capture;

    private Dictionary<string, string> BindingsFor(BindingKind kind) => kind switch
    {
        BindingKind.Toggle => _settings.Current.Hotkeys,
        BindingKind.Hold => _settings.Current.HoldHotkeys,
        _ => _settings.Current.SoundHotkeys,
    };

    private void RegisterAllBindings()
    {
        _hotkeys.UnregisterAll();
        _holdHook.Clear();
        var profile = _settings.Current;

        foreach (var item in ProfileVoices())
        {
            var voice = item;

            // Umschalten (RegisterHotKey)
            var toggle = Hotkey.Parse(profile.Hotkeys.GetValueOrDefault(voice.FileName));
            voice.HotkeyText = toggle is Hotkey t ? RegisterToggle(t, () => ActivateVoice(voice)) : "Kein Shortcut";

            // Halten (Tastatur-Hook)
            var hold = Hotkey.Parse(profile.HoldHotkeys.GetValueOrDefault(voice.FileName));
            if (hold is Hotkey h)
            {
                _holdHook.Add(h, () => BeginHold(voice), () => EndHold(voice));
                voice.HoldText = !_holdHook.IsInstalled ? h + " (geht nicht!)"
                    : h + (h.NumberAlias != null ? " (auch Num)" : "");
            }
            else voice.HoldText = "Keine Taste";
        }

        foreach (var sound in _sounds)
        {
            var s = sound;
            var hk = Hotkey.Parse(profile.SoundHotkeys.GetValueOrDefault(s.FileName));
            s.HotkeyText = hk is Hotkey k ? RegisterToggle(k, () => PlaySound(s)) : "Kein Shortcut";
        }
    }

    /// <summary>Registriert einen Shortcut samt Gegenstück auf der anderen Zahlenreihe und liefert den Anzeigetext.</summary>
    private string RegisterToggle(Hotkey hk, Action action)
    {
        bool ok = _hotkeys.Register(hk, action);
        // Zahlen gelten für beide Tastenreihen: Strg+Alt+1 oben = Strg+Alt+Num 1 (NumLock muss an sein).
        bool aliasOk = hk.NumberAlias is Hotkey alias && _hotkeys.Register(alias, action);
        if (!ok && !aliasOk) return hk + " (belegt!)";
        return hk + (aliasOk ? " (auch Num)" : "");
    }

    // ---------- Aufnahme einer neuen Taste ----------

    private void StartCapture(BindingKind kind, string fileName, Action<string> setText)
    {
        CancelCapture();
        _capture = new Capture(kind, fileName, setText);
        _hotkeys.UnregisterAll();   // damit auch bereits belegte Kombis aufgenommen werden können
        _holdHook.Suspended = true;
        setText(kind == BindingKind.Hold ? "Taste drücken …" : "Tasten drücken …");
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceItem item)
            StartCapture(BindingKind.Toggle, item.FileName, t => item.HotkeyText = t);
    }

    private void HoldKey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceItem item)
            StartCapture(BindingKind.Hold, item.FileName, t => item.HoldText = t);
    }

    private void SoundHotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItem item)
            StartCapture(BindingKind.Sound, item.FileName, t => item.HotkeyText = t);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capture == null) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return; // auf die eigentliche Taste warten

        var capture = _capture;
        if (key == Key.Escape) { CancelCapture(); return; }
        if (key is Key.Delete or Key.Back)
        {
            BindingsFor(capture.Kind).Remove(capture.FileName);
            FinishCapture();
            return;
        }

        var hk = new Hotkey(Keyboard.Modifiers, key);
        if (hk.Modifiers == ModifierKeys.None && !hk.IsSafeWithoutModifier)
        {
            capture.SetText("Mit Strg/Alt/Umschalt kombinieren");
            return;
        }

        // Eine Taste gibt es pro Charakter nur einmal – egal ob Umschalten, Halten oder Sound.
        string serialized = hk.Serialize();
        string? aliasSerialized = hk.NumberAlias?.Serialize();
        foreach (var map in _settings.Current.AllBindings())
            foreach (var other in map.Where(kv => kv.Value == serialized || kv.Value == aliasSerialized).Select(kv => kv.Key).ToList())
                map.Remove(other);

        BindingsFor(capture.Kind)[capture.FileName] = serialized;
        FinishCapture();
    }

    private void FinishCapture()
    {
        MarkCaptureEnded();
        _capture = null;
        _holdHook.Suspended = false;
        _settings.Save();
        RegisterAllBindings();
    }

    private void CancelCapture()
    {
        if (_capture == null) return;
        MarkCaptureEnded();
        _capture = null;
        _holdHook.Suspended = false;
        RegisterAllBindings();
    }

    // ---------- Entfernen / Zurücksetzen ----------

    private void RemoveBinding(BindingKind kind, string fileName)
    {
        CancelCapture();
        if (BindingsFor(kind).Remove(fileName))
        {
            _settings.Save();
            RegisterAllBindings();
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceItem item) RemoveBinding(BindingKind.Toggle, item.FileName);
    }

    private void ClearHold_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceItem item) RemoveBinding(BindingKind.Hold, item.FileName);
    }

    private void ClearSoundHotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItem item) RemoveBinding(BindingKind.Sound, item.FileName);
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        var profile = _settings.Current;
        var answer = MessageBox.Show(this,
            $"Wie sollen die Shortcuts von „{profile.Name}“ zurückgesetzt werden?\n\n" +
            "Ja  =  Standard wiederherstellen (Strg+Alt+0 bis 9 in der Reihenfolge der Liste)\n" +
            "Nein  =  Alle Shortcuts löschen (auch Halte-Tasten und Sounds)\n" +
            "Abbrechen  =  Nichts ändern",
            "Shortcuts zurücksetzen", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return;

        foreach (var map in profile.AllBindings()) map.Clear();
        if (answer == MessageBoxResult.Yes)
        {
            var voices = ProfileVoices().Take(10).ToList();
            for (int i = 0; i < voices.Count; i++)
                profile.Hotkeys[voices[i].FileName] = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D0 + i).Serialize();
        }
        _settings.Save();
        RegisterAllBindings();
    }
}
