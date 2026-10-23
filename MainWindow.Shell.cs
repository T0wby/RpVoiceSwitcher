using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RpVoiceSwitcher;

// Windows-üblicher Rahmen: Menüleiste, Tastenkürzel, Statusleiste, Fensterposition, Hilfe/Info
public partial class MainWindow
{
    private readonly DispatcherTimer _statusMessageTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private string? _statusMessage;
    private DateTime _captureEndedAt;

    private void InitShell()
    {
        void Bind(RoutedUICommand cmd, Action run, Func<bool>? canRun = null) =>
            CommandBindings.Add(new CommandBinding(cmd, (_, _) => run(), (_, e) => e.CanExecute = canRun?.Invoke() ?? true));
        RoutedEventArgs Args() => new();

        // Datei
        Bind(AppCommands.NewVoice, () => NewVoice_Click(this, Args()), () => _store != null);
        Bind(AppCommands.ImportVoice, ImportVoices, () => _store != null);
        Bind(AppCommands.ExportVoices, ExportVoices, () => _store != null);
        Bind(AppCommands.AddSounds, AddSoundsFromDialog);
        Bind(AppCommands.CloseWindow, Close);
        Bind(AppCommands.Exit, ExitApp);
        // Charakter
        Bind(AppCommands.NewProfile, () => NewProfile_Click(this, Args()));
        Bind(AppCommands.RenameProfile, () => RenameProfile_Click(this, Args()));
        Bind(AppCommands.DeleteProfile, () => DeleteProfile_Click(this, Args()), () => _settings.Profiles.Count > 1);
        Bind(AppCommands.SelectVoices, () => { Tabs.SelectedIndex = 0; SelectVoices_Click(this, Args()); });
        Bind(AppCommands.ResetHotkeys, () => ResetHotkeys_Click(this, Args()));
        // Ansicht
        Bind(AppCommands.ShowVoices, () => Tabs.SelectedIndex = 0);
        Bind(AppCommands.ShowSoundboard, () => Tabs.SelectedIndex = 1);
        Bind(AppCommands.ShowSetup, ShowSetupWindow);
        Bind(AppCommands.SetupWizard, ShowSetupWizard);
        Bind(AppCommands.ShowSettings, ShowSettingsWindow);
        Bind(AppCommands.Refresh, () => { LoadVoices(); LoadSounds(); RunSetupCheck(); ShowStatusMessage("Aktualisiert."); });
        // Extras
        Bind(AppCommands.VoiceTest, ShowVoiceTest, () => _store != null);
        Bind(AppCommands.Panic, Panic);
        Bind(AppCommands.StopSounds, StopAllSounds);
        Bind(AppCommands.TogglePttLatch, TogglePttLatch);
        Bind(AppCommands.OpenEditor, () => OpenEditor_Click(this, Args()), () => _store != null);
        Bind(AppCommands.OpenDeviceSelector, () => { if (_store != null) SetupChecker.OpenDeviceSelector(_store); }, () => _store != null);
        Bind(AppCommands.OpenRecording, () => SetupChecker.OpenSoundPanel(1));
        Bind(AppCommands.OpenPlayback, () => SetupChecker.OpenSoundPanel(0));
        // Hilfe
        Bind(AppCommands.Help, () => HelpWindow.ShowFor(this));
        Bind(AppCommands.CheckUpdates, () => _ = CheckForUpdatesAsync(manual: true));
        Bind(AppCommands.ReportProblem, ReportProblem);
        Bind(AppCommands.WhatsNew, () => ChangelogWindow.ShowAll(this));
        Bind(AppCommands.CopyDiagnostics, CopyDiagnostics);
        Bind(AppCommands.About, ShowAbout);

        _statusMessageTimer.Tick += (_, _) => { _statusMessageTimer.Stop(); _statusMessage = null; UpdateStatusBar(); };
        RestoreWindowPlacement();
        CreatePanelWindows(); // nach den Befehlen, damit die Fenster sie übernehmen
    }

    // ---------- Menüs ----------

    private void CharacterMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, CharacterMenu)) return;
        SelectVoicesMenuItem.Header = _selectingVoices ? "Stimmen_auswahl beenden" : "Stimmen _auswählen";

        // Charakterliste hinter dem Trenner neu aufbauen
        int start = CharacterMenu.Items.IndexOf(CharacterListSeparator) + 1;
        while (CharacterMenu.Items.Count > start) CharacterMenu.Items.RemoveAt(start);
        for (int i = 0; i < _settings.Profiles.Count; i++)
        {
            string name = _settings.Profiles[i].Name;
            var item = new MenuItem
            {
                Header = (i < 9 ? $"_{i + 1}  " : "    ") + name.Replace("_", "__"),
                IsCheckable = true,
                IsChecked = _settings.Profiles[i] == _settings.Current,
            };
            item.Click += (_, _) => SwitchProfile(name);
            CharacterMenu.Items.Add(item);
        }
    }

    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        StatusOverlayMenuItem.IsChecked = _settings.ShowStatusOverlay;
        FlashMenuItem.IsChecked = _settings.ShowOverlay;
        BeepMenuItem.IsChecked = _settings.PlaySound;
    }

    private void StatusOverlayMenu_Click(object sender, RoutedEventArgs e) => ToggleStatusOverlay();

    private void FlashMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowOverlay = FlashMenuItem.IsChecked;
        OverlayCheck.IsChecked = _settings.ShowOverlay;
        _settings.Save();
    }

    private void BeepMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.PlaySound = BeepMenuItem.IsChecked;
        SoundCheck.IsChecked = _settings.PlaySound;
        _settings.Save();
    }

    // ---------- Datei-Dialoge ----------

    private void ImportVoices()
    {
        if (_store == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Stimmen importieren",
            Filter = "Stimmen und Stimmen-Pakete (*.txt;*.zip)|*.txt;*.zip|Alle Dateien (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        if (ImportVoiceFiles(dlg.FileNames) == 0)
            ShowStatusMessage("Keine neuen Stimmen gefunden.");
    }

    private void AddSoundsFromDialog()
    {
        string exts = string.Join(";", SoundLibrary.Extensions.Select(x => "*" + x));
        var dlg = new OpenFileDialog
        {
            Title = "Sounds hinzufügen",
            Filter = $"Audiodateien ({exts})|{exts}|Alle Dateien (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            int n = SoundLibrary.Import(dlg.FileNames);
            LoadSounds();
            Tabs.SelectedIndex = 1;
            ShowStatusMessage(n == 1 ? "1 Sound hinzugefügt." : $"{n} Sounds hinzugefügt.");
        }
        catch (Exception ex) { ShowSoundError("Import fehlgeschlagen: " + ex.Message); }
    }

    // ---------- Statusleiste ----------

    private static readonly Brush ActiveDot = new SolidColorBrush(Color.FromRgb(0xF2, 0x8C, 0x28));
    private static readonly Brush IdleDot = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    private static readonly Brush ErrorDot = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x4B));

    /// <summary>Kurze Meldung links in der Statusleiste (verschwindet nach ein paar Sekunden).</summary>
    private void ShowStatusMessage(string message)
    {
        _statusMessage = message;
        _statusMessageTimer.Stop();
        _statusMessageTimer.Start();
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        if (_store == null)
        {
            StatusDot.Fill = ErrorDot;
            StatusText.Text = _statusMessage ?? "Equalizer APO nicht gefunden – siehe Einrichtung";
            return;
        }
        var active = CurrentVoice();
        string voice = active?.Name ?? "keine Stimme";
        if (_holdVoice != null) voice += " (gehalten)";
        string profile = _settings.Profiles.Count > 1 ? _settings.Current.Name + "  ·  " : "";

        StatusDot.Fill = active == null || active.IsNormal ? IdleDot : ActiveDot;
        StatusText.Text = _statusMessage ?? $"{profile}{voice}  ·  wirkt auf {_settings.TargetDevice}";
        StatusText.ToolTip = $"Charakter: {_settings.Current.Name}\nAktive Stimme: {voice}\nWirkt auf: {_settings.TargetDevice}";
    }

    private void UpdateSoundStatus()
    {
        SoundStatusText.Text = _soundEngine.Error != null ? "Soundboard: Fehler" : $"Soundboard: {_sounds.Count} Sounds";
        SoundStatusText.ToolTip = _soundEngine.Error ?? $"Spielt auf: {_soundEngine.RemoteDeviceName ?? _settings.SoundOutputDevice}";
    }

    // ---------- Tastatur ----------

    /// <summary>
    /// Nach dem Aufnehmen eines Shortcuts mit Alt würde das Loslassen von Alt sonst die Menüleiste aktivieren.
    /// </summary>
    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        bool alt = e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt;
        if (alt && (_capture != null || DateTime.Now - _captureEndedAt < TimeSpan.FromSeconds(1)))
            e.Handled = true;
    }

    private void MarkCaptureEnded() => _captureEndedAt = DateTime.Now;

    // ---------- Fensterposition ----------

    private void RestoreWindowPlacement()
    {
        if (_settings.WindowWidth is not double w || _settings.WindowHeight is not double h ||
            _settings.WindowLeft is not double l || _settings.WindowTop is not double t) return;

        // Nur übernehmen, wenn das Fenster noch auf einem vorhandenen Bildschirm liegt (Monitor abgesteckt?).
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(l + 40, t + 10, Math.Max(1, w - 80), 40))) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = l; Top = t;
        Width = Math.Max(MinWidth, w); Height = Math.Max(MinHeight, h);
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized && RestoreBounds.IsEmpty) return;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (b.IsEmpty || b.Width < 100) return;
        _settings.WindowLeft = b.Left;
        _settings.WindowTop = b.Top;
        _settings.WindowWidth = b.Width;
        _settings.WindowHeight = b.Height;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }

    // ---------- Info ----------

    private void ShowAbout()
    {
        var asm = Assembly.GetExecutingAssembly();
        string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
                         ?? asm.GetName().Version?.ToString() ?? "?";
        MessageBox.Show(this,
            $"RP Voice Switcher {version}\n\n" +
            "Stimmen, Charaktere und Soundboard für Roleplay.\n\n" +
            "Baut auf:\n" +
            "  • Equalizer APO – Effekte am Mikrofon\n" +
            "  • VB-CABLE – virtuelles Kabel ins Spiel\n" +
            "  • Graillon (optional) – Tonhöhe\n" +
            "  • NAudio – Soundboard\n\n" +
            $"Einstellungen: {AppSettings.SettingsFile}",
            "Info über RP Voice Switcher", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
