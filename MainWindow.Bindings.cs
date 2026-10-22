using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace RpVoiceSwitcher;

// Tastenbelegung: Umschalten, Halten und Sounds – jeweils pro Charakter
public partial class MainWindow
{
    private enum BindingKind { Toggle, Hold, Sound, Panic, PushToTalk, PttLatch }

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

        // Notfall-Taste zuerst – sie gilt in jedem Charakter und soll nie von etwas anderem verdrängt werden.
        var panic = Hotkey.Parse(_settings.PanicHotkey);
        PanicButton.Content = panic is Hotkey ph ? RegisterToggle(ph, Panic) : "Keine Taste";
        // Im Menü als Hinweis (die Taste ist global, nicht nur im Fenster)
        PanicMenuItem.InputGestureText = panic?.ToString() ?? "";

        // Dauersenden an/aus – ebenfalls global
        var latch = Hotkey.Parse(_settings.PttLatchHotkey);
        PttLatchButton.Content = latch is Hotkey lh ? RegisterToggle(lh, TogglePttLatch) : "Keine Taste";
        PttLatchMenuItem.InputGestureText = latch?.ToString() ?? "";

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
        // Hinweis auf die jeweils andere Zahlenreihe
        bool onNumpad = hk.Key is >= Key.NumPad0 and <= Key.NumPad9;
        return hk + (aliasOk ? (onNumpad ? $" (auch {(int)(hk.Key - Key.NumPad0)})" : " (auch Num)") : "");
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

        // Sprachtaste des Spiels: eine einzelne Taste, auch Buchstaben oder Strg/Alt/Umschalt allein
        if (_capture.Kind == BindingKind.PushToTalk)
        {
            if (key == Key.Escape) { CancelCapture(); return; }
            if (key is Key.Delete or Key.Back)
            {
                _settings.PushToTalkKey = null;
                FinishCapture();
                UpdatePushToTalkUi();
                return;
            }
            if (key is Key.ImeProcessed or Key.DeadCharProcessed or Key.None or Key.LWin or Key.RWin) return;
            CapturePushToTalk(new PushToTalk.Binding(false, key, 0));
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return; // auf die eigentliche Taste warten

        var capture = _capture;
        if (key == Key.Escape) { CancelCapture(); return; }
        if (key is Key.Delete or Key.Back)
        {
            if (capture.Kind == BindingKind.Panic) _settings.PanicHotkey = null;
            else if (capture.Kind == BindingKind.PttLatch) _settings.PttLatchHotkey = null;
            else BindingsFor(capture.Kind).Remove(capture.FileName);
            FinishCapture();
            return;
        }

        var hk = new Hotkey(Keyboard.Modifiers, key);
        if (hk.Modifiers == ModifierKeys.None && !hk.IsSafeWithoutModifier)
        {
            capture.SetText("Mit Strg/Alt/Umschalt kombinieren");
            return;
        }

        string serialized = hk.Serialize();
        string? aliasSerialized = hk.NumberAlias?.Serialize();
        bool Same(string? value) => value != null && (value == serialized || value == aliasSerialized);

        // Die Sprachtaste drückt die App selbst – als Shortcut würde sie sich sonst selbst auslösen.
        if (hk.Modifiers == ModifierKeys.None && PttKey is { IsMouse: false } pk && pk.Key == key)
        {
            capture.SetText("Ist die Sprachtaste des Spiels");
            return;
        }

        bool IsGlobal(string? value) => Same(value) || Hotkey.Parse(value)?.NumberAlias?.Serialize() == serialized;

        if (capture.Kind is BindingKind.Panic or BindingKind.PttLatch)
        {
            if (capture.Kind == BindingKind.PttLatch && IsGlobal(_settings.PanicHotkey))
            {
                capture.SetText("Ist schon die Notfall-Taste");
                return;
            }
            // Notfall-Taste und Dauersenden gelten überall – deshalb aus allen Charakteren entfernen.
            foreach (var profile in _settings.Profiles)
                foreach (var map in profile.AllBindings())
                    foreach (var other in map.Where(kv => Same(kv.Value)).Select(kv => kv.Key).ToList())
                        map.Remove(other);
            if (capture.Kind == BindingKind.Panic)
            {
                if (IsGlobal(_settings.PttLatchHotkey)) _settings.PttLatchHotkey = null;
                _settings.PanicHotkey = serialized;
            }
            else _settings.PttLatchHotkey = serialized;
            FinishCapture();
            return;
        }

        if (IsGlobal(_settings.PanicHotkey))
        {
            capture.SetText("Ist schon die Notfall-Taste");
            return;
        }
        if (IsGlobal(_settings.PttLatchHotkey))
        {
            capture.SetText("Ist schon die Dauersenden-Taste");
            return;
        }

        // Eine Taste gibt es pro Charakter nur einmal – egal ob Umschalten, Halten oder Sound.
        foreach (var map in _settings.Current.AllBindings())
            foreach (var other in map.Where(kv => Same(kv.Value)).Select(kv => kv.Key).ToList())
                map.Remove(other);

        BindingsFor(capture.Kind)[capture.FileName] = serialized;
        FinishCapture();
    }

    // ---------- Notfall-Taste ----------

    private void PanicKey_Click(object sender, RoutedEventArgs e) =>
        StartCapture(BindingKind.Panic, "", t => PanicButton.Content = t);

    private void ClearPanicKey_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        _settings.PanicHotkey = null;
        _settings.Save();
        RegisterAllBindings();
    }

    /// <summary>Sofort normale Stimme, alle Sounds aus, Halten beendet – egal in welchem Charakter.</summary>
    private void Panic()
    {
        _soundEngine.StopAll();
        ReleasePttNow();
        _holdVoice = null;
        var normal = NormalVoice();
        if (normal != null)
        {
            _baseVoice = normal;
            ApplyVoice(normal, quiet: true);
        }
        else
        {
            try { _store?.Deactivate(); } catch { /* nichts mehr zu tun */ }
            foreach (var v in _allVoices) v.IsActive = false;
            _baseVoice = null;
            UpdateStatus();
        }
        _holdHook.ReleaseHeld(); // EndHold greift nicht mehr, weil _holdVoice schon null ist

        if (_settings.ShowOverlay)
        {
            _overlay ??= new OverlayWindow();
            _overlay.Flash("Notfall: normale Stimme · Sounds aus");
        }
        Beep(440);
        ShowStatusMessage("Notfall-Taste: normale Stimme, alle Sounds gestoppt.");
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
        bool ptt = _capture.Kind == BindingKind.PushToTalk;
        MarkCaptureEnded();
        _capture = null;
        _holdHook.Suspended = false;
        RegisterAllBindings();
        if (ptt) UpdatePushToTalkUi();
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
