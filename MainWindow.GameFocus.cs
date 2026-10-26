using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

// Sprachtaste nur drücken, solange das Spiel im Vordergrund ist. Ohne das landet z. B. "Maus 5 gedrückt" im Browser
// oder in Windows (dann lässt sich nichts mehr anklicken), sobald du per Alt+Tab rauswechselst.
// Dazu: Warnung, wenn das Spiel als Administrator läuft – dann blockiert Windows die simulierte Taste.
public partial class MainWindow
{
    public const string DefaultGameProcess = "NanosWorld-Win64-Shipping";

    private static string NormalizeProcessName(string? s)
    {
        s = (s ?? "").Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s;
    }

    private string GameProcessName => NormalizeProcessName(_settings.PttGameProcess);
    private bool GameGateOn => _settings.PttOnlyInGame && GameProcessName.Length > 0;

    /// <summary>Darf die Taste jetzt gedrückt sein? (kein Spiel-Filter oder das Spiel ist vorne)</summary>
    private bool GameAllowsPtt() => !GameGateOn || GameInFront();

    private uint _fgPid;
    private string? _fgName;

    private bool GameInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        if (pid != _fgPid)
        {
            _fgPid = pid;
            _fgName = ProcessNameOf(pid);
        }
        bool front = _fgName != null && string.Equals(_fgName, GameProcessName, StringComparison.OrdinalIgnoreCase);
        if (front) CheckGameElevation(pid);
        return front;
    }

    private static string? ProcessNameOf(uint pid)
    {
        if (pid == 0) return null;
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return null; }
    }

    // ---------- Spiel als Administrator? ----------

    private uint _elevationCheckedPid;
    private bool _gameElevated;

    private void CheckGameElevation(uint pid)
    {
        if (pid == _elevationCheckedPid) return;
        _elevationCheckedPid = pid;
        bool was = _gameElevated;
        _gameElevated = !Environment.IsPrivilegedProcess && IsElevated(pid);
        if (_gameElevated && !was)
        {
            PttLog($"Warnung: {GameProcessName} läuft als Administrator – Windows blockiert dann die Sprachtaste der App");
            FlashOverlay("Das Spiel läuft als Administrator – die App kann die Sprachtaste nicht drücken. RP Voice Switcher auch als Administrator starten.");
        }
        if (was != _gameElevated) { UpdatePttSummary(); UpdateGameStatusText(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int retLen);

    /// <summary>Läuft der Prozess mit Administratorrechten? (Token nicht lesbar = höchstwahrscheinlich ja)</summary>
    private static bool IsElevated(uint pid)
    {
        IntPtr proc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (proc == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(proc, 0x0008 /* TOKEN_QUERY */, out IntPtr token)) return true;
            try { return GetTokenInformation(token, 20 /* TokenElevation */, out int elevated, 4, out _) && elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(proc); }
    }

    // ---------- Einstellungen ----------

    private readonly DispatcherTimer _gameStatusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _gameStatusWired;

    private void UpdateGameUi()
    {
        PttOnlyInGameCheck.IsChecked = _settings.PttOnlyInGame;
        PttRefreshCheck.IsChecked = _settings.PttRefreshSeconds > 0;
        PttRefreshCheck.IsEnabled = !OpenMicMode;
        if (!PttGameBox.IsKeyboardFocusWithin) PttGameBox.Text = _settings.PttGameProcess ?? "";
        PttGameBox.IsEnabled = PttGameDetect.IsEnabled = _settings.PttOnlyInGame;
        if (!_gameStatusWired)
        {
            _gameStatusWired = true;
            _gameStatusTimer.Tick += (_, _) => { if (_settingsWindow?.IsVisible == true) UpdateGameStatusText(); else _gameStatusTimer.Stop(); };
        }
        UpdateGameStatusText();
    }

    private void UpdateGameStatusText()
    {
        if (!_settings.PttOnlyInGame || GameProcessName.Length == 0)
        {
            PttGameStatus.Text = "Aus – die App drückt die Taste auch, wenn ein anderes Fenster vorne ist.";
            return;
        }
        bool running = false;
        try { var ps = Process.GetProcessesByName(GameProcessName); running = ps.Length > 0; foreach (var p in ps) p.Dispose(); } catch { }
        PttGameStatus.Text = !running ? "Spiel läuft gerade nicht – die App drückt erst, wenn es vorne ist."
            : _gameElevated ? "⚠ Spiel läuft als Administrator – dann die App ebenfalls als Administrator starten."
            : "Spiel läuft ✓";
        if (_settingsWindow?.IsVisible == true && !_gameStatusTimer.IsEnabled) _gameStatusTimer.Start();
    }

    private void PttOnlyInGame_Click(object sender, RoutedEventArgs e)
    {
        bool on = PttOnlyInGameCheck.IsChecked == true;
        if (on == _settings.PttOnlyInGame) return;
        _settings.PttOnlyInGame = on;
        _settings.Save();
        PttLog(on ? "nur im Spiel: an" : "nur im Spiel: aus");
        UpdateGameUi();
    }

    private void PttRefresh_Click(object sender, RoutedEventArgs e)
    {
        bool on = PttRefreshCheck.IsChecked == true;
        if (on == _settings.PttRefreshSeconds > 0) return;
        _settings.PttRefreshSeconds = on ? 45 : 0;
        _settings.Save();
        _pttNextPeriodic = on ? Environment.TickCount64 + 45_000 : -1;
        PttLog(on ? "regelmäßiges Auffrischen: an" : "regelmäßiges Auffrischen: aus");
    }

    private void PttGameBox_LostFocus(object sender, RoutedEventArgs e) => SaveGameProcess(PttGameBox.Text);

    private void PttGameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { SaveGameProcess(PttGameBox.Text); e.Handled = true; }
    }

    private void SaveGameProcess(string? text)
    {
        string name = NormalizeProcessName(text);
        if (name.Length == 0) name = DefaultGameProcess;
        if (name == _settings.PttGameProcess) { PttGameBox.Text = name; return; }
        _settings.PttGameProcess = name;
        _settings.Save();
        _fgPid = 0; _elevationCheckedPid = 0; _gameElevated = false;
        PttGameBox.Text = name;
        PttLog($"Spiel: {name}");
        UpdateGameUi();
        UpdatePttSummary();
    }

    /// <summary>"Erkennen": 5 Sekunden Zeit, ins Spiel zu wechseln – dann wird das Programm im Vordergrund übernommen.</summary>
    private async void PttGameDetect_Click(object sender, RoutedEventArgs e)
    {
        PttGameDetect.IsEnabled = false;
        try
        {
            for (int i = 5; i > 0; i--)
            {
                PttGameStatus.Text = $"Wechsle jetzt ins Spiel … ({i})";
                await System.Threading.Tasks.Task.Delay(1000);
            }
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            string? name = pid == (uint)Environment.ProcessId ? null : ProcessNameOf(pid);
            if (name == null || name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                PttGameStatus.Text = "Kein Spiel erkannt – bitte innerhalb der 5 Sekunden ins Spielfenster klicken.";
                return;
            }
            SaveGameProcess(name);
            PttGameStatus.Text = $"Übernommen: {name}";
        }
        finally { PttGameDetect.IsEnabled = _settings.PttOnlyInGame; }
    }
}
