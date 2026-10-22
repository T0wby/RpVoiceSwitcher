using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace RpVoiceSwitcher;

/// <summary>
/// Hauptfenster. Aufgeteilt in mehrere Dateien:
/// .Voices (Stimmen, Halten, Status-Anzeige) · .Profiles (Charaktere) · .Bindings (Tastenbelegung)
/// · .Sounds (Soundboard) · .Setup (Einrichtungs-Check). Hier: Start, Optionen, Tray, Beenden.
/// </summary>
public partial class MainWindow : Window
{
    private const string AutostartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutostartName = "RpVoiceSwitcher";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HotkeyManager _hotkeys;
    private readonly HoldKeyHook _holdHook;
    private readonly WinForms.NotifyIcon _tray;
    private VoiceStore? _store;
    private bool _initializing = true;
    private bool _exiting;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        VoiceList.ItemsSource = _items;
        SoundList.ItemsSource = _sounds;
        CheckList.ItemsSource = _checks;
        ProfileList.ItemsSource = _profileChips;

        WindowTheme.Apply(this); // App-Icon + dunkle Titelleiste, vor dem Erzeugen des Handles

        // Fenster-Handle sofort erzeugen, damit Hotkeys auch funktionieren, wenn das Fenster nie gezeigt wird (Autostart).
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();
        _hotkeys = new HotkeyManager(HwndSource.FromHwnd(helper.Handle));
        _holdHook = new HoldKeyHook(Dispatcher);

        SoundCheck.IsChecked = _settings.PlaySound;
        OverlayCheck.IsChecked = _settings.ShowOverlay;
        TrayCheck.IsChecked = _settings.CloseToTray;
        StatusCheck.IsChecked = _settings.ShowStatusOverlay;
        AutostartCheck.IsChecked = IsAutostartEnabled();
        DeviceBox.Text = _settings.TargetDevice;
        SoundDeviceBox.Text = _settings.SoundOutputDevice;
        SoundVolumeSlider.Value = _settings.SoundVolume;
        SoundMonitorSlider.Value = _settings.SoundMonitorVolume;
        SoundMonitorCheck.IsChecked = _settings.SoundMonitor;

        _tray = CreateTrayIcon();
        Deactivated += (_, _) => CancelCapture();

        SoundLibrary.Folder = string.IsNullOrWhiteSpace(_settings.SoundFolder) ? SoundLibrary.DefaultFolder : _settings.SoundFolder;
        _store = VoiceStore.TryCreate(_settings.VoicesFolder);
        if (_store != null)
        {
            try
            {
                _store.EnsureInstalled(BundledVoicesDir);
            }
            catch (Exception ex)
            {
                ShowStatusMessage("Konnte nicht in den Equalizer-APO-Ordner schreiben: " + ex.Message);
            }
        }

        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            if (_capture != null) { _reloadDebounce.Start(); return; } // nicht mitten in der Tastenaufnahme neu laden
            LoadVoices();
        };

        InitShell();
        RefreshProfileChips();
        LoadVoices();
        EnsureActiveVoiceInProfile();
        WatchVoicesFolder();
        InitSoundboard();
        RefreshFolderTexts();
        RunSetupCheck();
        WatchDevices();
        _initializing = false;
    }

    private static string BundledVoicesDir => Path.Combine(AppContext.BaseDirectory, "Voices");

    /// <summary>Start im Hintergrund (Autostart): nur Tray, Status-Anzeige und Shortcuts, kein Fenster.</summary>
    public void InitializeHidden() { /* Handle existiert bereits seit dem Konstruktor */ }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Tabs) || _initializing) return;
        CancelCapture();
    }

    // ---------- Optionen ----------

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        _settings.PlaySound = SoundCheck.IsChecked == true;
        _settings.ShowOverlay = OverlayCheck.IsChecked == true;
        _settings.CloseToTray = TrayCheck.IsChecked == true;
        _settings.Save();
    }

    private void DeviceBox_LostFocus(object sender, RoutedEventArgs e) => SetTargetDevice(DeviceBox.Text.Trim());

    private void SetTargetDevice(string device)
    {
        DeviceBox.Text = device;
        if (device == _settings.TargetDevice) return;
        _settings.TargetDevice = device;
        _settings.Save();
        var active = CurrentVoice();
        if (active != null) ApplyVoice(active, quiet: true); // voice.txt mit neuem Gerät neu schreiben
        UpdateStatus();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        Directory.CreateDirectory(_store.VoicesDir);
        Process.Start("explorer.exe", $"\"{_store.VoicesDir}\"");
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        string dir = Path.GetDirectoryName(_store.ConfigDir)!;
        string editor = Path.Combine(dir, "Editor.exe");
        if (!File.Exists(editor))
        {
            MessageBox.Show(this, "Equalizer-APO-Editor nicht gefunden:\n" + editor, "RP Voice Switcher");
            return;
        }
        // Equalizer APO findet seine Qt-Plugins nur, wenn das Arbeitsverzeichnis der Programmordner ist.
        Process.Start(new ProcessStartInfo(editor) { UseShellExecute = true, WorkingDirectory = dir });
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
            Icon = WindowTheme.LoadTrayIcon() ?? DrawTrayIcon(),
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

        if (_settings.Profiles.Count > 1)
        {
            var profiles = new WinForms.ToolStripMenuItem("Charakter: " + _settings.Current.Name);
            foreach (var p in _settings.Profiles)
            {
                string name = p.Name;
                var entry = new WinForms.ToolStripMenuItem(name) { Checked = p == _settings.Current };
                entry.Click += (_, _) => SwitchProfile(name);
                profiles.DropDownItems.Add(entry);
            }
            menu.Items.Add(profiles);
            menu.Items.Add(new WinForms.ToolStripSeparator());
        }

        foreach (var item in ProfileVoices())
        {
            var captured = item;
            var entry = new WinForms.ToolStripMenuItem(item.Name) { Checked = item.IsActive };
            entry.Click += (_, _) => ActivateVoice(captured);
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
        CancelCapture();
        SaveWindowPlacement();

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
        _soundWatcher?.Dispose();
        _reloadDebounce.Stop();
        _soundReloadDebounce.Stop();
        _statusMessageTimer.Stop();
        _deviceDebounce.Stop();
        _deviceWatcher?.Dispose();
        _holdHook.Dispose();
        _hotkeys.Dispose();
        _soundEngine.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay?.Close();
        _status?.Close();
        _setupWindow?.CloseForReal();
        _settingsWindow?.CloseForReal();
        Application.Current?.Shutdown();
    }
}
