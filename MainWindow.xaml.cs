using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace RpVoiceSwitcher;

public partial class MainWindow : Window
{
    private const string AutostartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartName = "RpVoiceSwitcher";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<VoiceItem> _items = new();
    private readonly HotkeyManager _hotkeys;
    private readonly WinForms.NotifyIcon _tray;
    private readonly VoiceStore? _store;
    private OverlayWindow? _overlay;
    private StatusOverlayWindow? _status;
    private bool _statusEditing;
    private VoiceItem? _capturing;
    private bool _exiting;
    private bool _trayHintShown;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reloadDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainWindow()
    {
        InitializeComponent();
        VoiceList.ItemsSource = _items;

        // Fenster-Handle sofort erzeugen, damit Hotkeys auch funktionieren, wenn das Fenster nie gezeigt wird (Autostart).
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();
        _hotkeys = new HotkeyManager(HwndSource.FromHwnd(helper.Handle));

        SoundCheck.IsChecked = _settings.PlaySound;
        OverlayCheck.IsChecked = _settings.ShowOverlay;
        TrayCheck.IsChecked = _settings.CloseToTray;
        StatusCheck.IsChecked = _settings.ShowStatusOverlay;
        AutostartCheck.IsChecked = IsAutostartEnabled();
        DeviceBox.Text = _settings.TargetDevice;

        _tray = CreateTrayIcon();
        Deactivated += (_, _) => CancelCapture();

        _store = VoiceStore.TryCreate();
        if (_store == null)
        {
            StatusText.Text = "Equalizer APO wurde nicht gefunden. Bitte zuerst installieren.";
            return;
        }

        try
        {
            _store.EnsureInstalled(Path.Combine(AppContext.BaseDirectory, "Voices"));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Konnte nicht in den Equalizer-APO-Ordner schreiben: " + ex.Message;
            return;
        }

        LoadVoices();
        WatchVoicesFolder();
    }

    /// <summary>
    /// Lädt die Liste automatisch neu, sobald im Stimmen-Ordner eine Datei hinzukommt, geändert, umbenannt oder gelöscht wird.
    /// Editoren speichern oft in mehreren Schritten, deshalb wird kurz gewartet und nur einmal neu geladen.
    /// </summary>
    private void WatchVoicesFolder()
    {
        if (_store == null) return;

        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            if (_capturing != null) { _reloadDebounce.Start(); return; } // nicht mitten in der Shortcut-Aufnahme neu laden
            LoadVoices();
        };

        _watcher = new FileSystemWatcher(_store.VoicesDir, "*.txt")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, _) => Dispatcher.InvokeAsync(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
        _watcher.Created += changed;
        _watcher.Changed += changed;
        _watcher.Deleted += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
    }

    /// <summary>Start im Hintergrund (Autostart): nur Tray + Hotkeys, kein Fenster.</summary>
    public void InitializeHidden() { /* Handle existiert bereits seit dem Konstruktor */ }

    // ---------- Stimmen ----------

    private void LoadVoices()
    {
        if (_store == null) return;
        _items.Clear();
        string? active = _store.GetActiveFileName();
        foreach (var preset in _store.LoadVoices())
        {
            _items.Add(new VoiceItem(preset)
            {
                IsActive = string.Equals(preset.FileName, active, StringComparison.OrdinalIgnoreCase),
            });
        }
        RegisterAllHotkeys();
        UpdateStatus();
    }

    private void Activate(VoiceItem item)
    {
        if (_store == null) return;
        try
        {
            _store.Activate(item.Preset, _settings.TargetDevice);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Umschalten fehlgeschlagen: " + ex.Message;
            return;
        }

        foreach (var i in _items) i.IsActive = ReferenceEquals(i, item);
        UpdateStatus();

        bool isNormal = item.Preset.FileName.StartsWith("00-", StringComparison.OrdinalIgnoreCase);
        if (_settings.ShowOverlay)
        {
            _overlay ??= new OverlayWindow();
            _overlay.Flash("Stimme: " + item.Name);
        }
        if (_settings.PlaySound)
        {
            int freq = isNormal ? 520 : 880;
            Task.Run(() => { try { Console.Beep(freq, 90); } catch { /* kein Ton verfügbar */ } });
        }
    }

    private void UpdateStatus()
    {
        var active = _items.FirstOrDefault(i => i.IsActive);
        string activeText = active?.Name ?? "keine";
        StatusText.Text = $"Aktive Stimme: {activeText}   ·   Wirkt auf: {_settings.TargetDevice}";
        _tray.Text = Truncate("RP-Stimme: " + activeText, 63);
        UpdateStatusOverlay();
    }

    // ---------- Status-Anzeige ----------

    private static bool IsNormal(VoiceItem item) =>
        item.Preset.FileName.StartsWith("00-", StringComparison.OrdinalIgnoreCase);

    private void UpdateStatusOverlay()
    {
        if (!_settings.ShowStatusOverlay)
        {
            if (_status != null)
            {
                SetStatusEditMode(false);
                _status.Hide();
            }
            return;
        }

        if (_status == null)
        {
            _status = new StatusOverlayWindow();
            _status.PositionChanged += (left, top) =>
            {
                _settings.StatusOverlayLeft = left;
                _settings.StatusOverlayTop = top;
                _settings.Save();
            };
            _status.EditFinished += () => SetStatusEditMode(false);
        }

        var active = _items.FirstOrDefault(i => i.IsActive);
        _status.SetVoice(active?.Name ?? "Keine Stimme", active == null || IsNormal(active));

        if (!_status.IsVisible)
            _status.ShowAt(_settings.StatusOverlayLeft, _settings.StatusOverlayTop);
    }

    private void SetStatusEditMode(bool on)
    {
        if (on && !_settings.ShowStatusOverlay)
        {
            _settings.ShowStatusOverlay = true;
            StatusCheck.IsChecked = true;
            _settings.Save();
            UpdateStatusOverlay();
        }

        _statusEditing = on && _status != null;
        _status?.SetEditMode(_statusEditing);
        MoveStatusButton.Content = _statusEditing ? "Fertig" : "Position anpassen";
    }

    private void StatusOverlay_Changed(object sender, RoutedEventArgs e)
    {
        _settings.ShowStatusOverlay = StatusCheck.IsChecked == true;
        _settings.Save();
        UpdateStatusOverlay();
    }

    private void MoveStatus_Click(object sender, RoutedEventArgs e) => SetStatusEditMode(!_statusEditing);

    private void ResetStatusPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.StatusOverlayLeft = null;
        _settings.StatusOverlayTop = null;
        _settings.Save();
        _status?.MoveTo(null, null);
    }

    private void ToggleStatusOverlay()
    {
        _settings.ShowStatusOverlay = !_settings.ShowStatusOverlay;
        StatusCheck.IsChecked = _settings.ShowStatusOverlay;
        _settings.Save();
        UpdateStatusOverlay();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ---------- Hotkeys ----------

    private void RegisterAllHotkeys()
    {
        _hotkeys.UnregisterAll();
        foreach (var item in _items)
        {
            _settings.Hotkeys.TryGetValue(item.Preset.FileName, out var raw);
            var hk = Hotkey.Parse(raw);
            if (hk is null)
            {
                item.HotkeyText = "Kein Shortcut";
                continue;
            }
            var captured = item;
            bool ok = _hotkeys.Register(hk.Value, () => Activate(captured));

            // Zahlen gelten für beide Tastenreihen: Strg+Alt+1 oben = Strg+Alt+Num 1 (NumLock muss an sein).
            bool aliasOk = hk.Value.NumberAlias is Hotkey alias && _hotkeys.Register(alias, () => Activate(captured));

            string text = hk.Value.ToString() + (aliasOk ? " (auch Num)" : "");
            item.HotkeyText = ok || aliasOk ? text : hk.Value + " (belegt!)";
        }
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VoiceItem item) return;
        CancelCapture();
        _capturing = item;
        _hotkeys.UnregisterAll(); // damit auch bereits belegte Kombis aufgenommen werden können
        item.HotkeyText = "Tasten drücken …";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing == null) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return; // auf die eigentliche Taste warten

        string file = _capturing.Preset.FileName;
        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }
        if (key is Key.Delete or Key.Back)
        {
            _settings.Hotkeys.Remove(file);
            FinishCapture();
            return;
        }

        var hk = new Hotkey(Keyboard.Modifiers, key);
        if (hk.Modifiers == ModifierKeys.None && !hk.IsSafeWithoutModifier)
        {
            _capturing.HotkeyText = "Mit Strg/Alt/Umschalt kombinieren";
            return;
        }

        string serialized = hk.Serialize();
        string? aliasSerialized = hk.NumberAlias?.Serialize();
        foreach (var other in _settings.Hotkeys
                     .Where(kv => (kv.Value == serialized || kv.Value == aliasSerialized)
                                  && !kv.Key.Equals(file, StringComparison.OrdinalIgnoreCase))
                     .Select(kv => kv.Key).ToList())
            _settings.Hotkeys.Remove(other);

        _settings.Hotkeys[file] = serialized;
        FinishCapture();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VoiceItem item) return;
        CancelCapture();
        if (_settings.Hotkeys.Remove(item.Preset.FileName))
        {
            _settings.Save();
            RegisterAllHotkeys();
        }
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        var answer = MessageBox.Show(this,
            "Wie sollen die Shortcuts zurückgesetzt werden?\n\n" +
            "Ja  =  Standard wiederherstellen (Strg+Alt+0 bis 9 in der Reihenfolge der Liste)\n" +
            "Nein  =  Alle Shortcuts löschen\n" +
            "Abbrechen  =  Nichts ändern",
            "Shortcuts zurücksetzen", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return;

        _settings.Hotkeys.Clear();
        if (answer == MessageBoxResult.Yes)
        {
            for (int i = 0; i < Math.Min(10, _items.Count); i++)
                _settings.Hotkeys[_items[i].Preset.FileName] = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D0 + i).Serialize();
        }
        _settings.Save();
        RegisterAllHotkeys();
    }

    private void FinishCapture()
    {
        _capturing = null;
        _settings.Save();
        RegisterAllHotkeys();
    }

    private void CancelCapture()
    {
        if (_capturing == null) return;
        _capturing = null;
        RegisterAllHotkeys();
    }

    // ---------- Buttons & Optionen ----------

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is VoiceItem item) Activate(item);
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        _settings.PlaySound = SoundCheck.IsChecked == true;
        _settings.ShowOverlay = OverlayCheck.IsChecked == true;
        _settings.CloseToTray = TrayCheck.IsChecked == true;
        _settings.Save();
    }

    private void DeviceBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string device = DeviceBox.Text.Trim();
        if (device == _settings.TargetDevice) return;
        _settings.TargetDevice = device;
        _settings.Save();
        var active = _items.FirstOrDefault(i => i.IsActive);
        if (active != null) Activate(active); // voice.txt mit neuem Gerät neu schreiben
        UpdateStatus();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => LoadVoices();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_store != null) Process.Start("explorer.exe", _store.VoicesDir);
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        string editor = Path.Combine(Path.GetDirectoryName(_store.ConfigDir)!, "Editor.exe");
        if (!File.Exists(editor))
        {
            MessageBox.Show("Equalizer-APO-Editor nicht gefunden:\n" + editor, "RP Voice Switcher");
            return;
        }
        // Equalizer APO findet seine Qt-Plugins nur, wenn das Arbeitsverzeichnis der Programmordner ist.
        Process.Start(new ProcessStartInfo(editor)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(editor)!,
        });
    }

    private static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutostartKey);
        return key?.GetValue(AutostartName) != null;
    }

    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AutostartKey);
        if (AutostartCheck.IsChecked == true)
            key.SetValue(AutostartName, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(AutostartName, false);
    }

    // ---------- Tray ----------

    private WinForms.NotifyIcon CreateTrayIcon()
    {
        var tray = new WinForms.NotifyIcon
        {
            Icon = DrawTrayIcon(),
            Text = "RP Voice Switcher",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip(),
        };
        tray.DoubleClick += (_, _) => ShowFromTray();
        tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu(tray.ContextMenuStrip);
        return tray;
    }

    private void BuildTrayMenu(WinForms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        foreach (var item in _items)
        {
            var captured = item;
            var entry = new WinForms.ToolStripMenuItem(item.Name) { Checked = item.IsActive };
            entry.Click += (_, _) => Activate(captured);
            menu.Items.Add(entry);
        }
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var statusEntry = new WinForms.ToolStripMenuItem("Status-Anzeige") { Checked = _settings.ShowStatusOverlay };
        statusEntry.Click += (_, _) => ToggleStatusOverlay();
        menu.Items.Add(statusEntry);
        menu.Items.Add("Fenster öffnen", null, (_, _) => ShowFromTray());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());
    }

    private static System.Drawing.Icon DrawTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF2, 0x8C, 0x28));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            var format = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            g.DrawString("RP", font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, 32, 32), format);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Im Bearbeitungsmodus wäre die Anzeige anklickbar und würde Klicks im Spiel abfangen.
        SetStatusEditMode(false);

        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _tray.ShowBalloonTip(2500, "RP Voice Switcher",
                    "Läuft im Hintergrund weiter, die Shortcuts bleiben aktiv.", WinForms.ToolTipIcon.Info);
                _trayHintShown = true;
            }
            return;
        }
        base.OnClosing(e);
        if (!e.Cancel && !_exiting) ExitApp();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _watcher?.Dispose();
        _reloadDebounce.Stop();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay?.Close();
        _status?.Close();
        Application.Current?.Shutdown();
    }
}
