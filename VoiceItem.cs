using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace RpVoiceSwitcher;

public abstract class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class VoiceItem : ViewModel
{
    public VoiceItem(VoicePreset preset) => Preset = preset;

    public VoicePreset Preset { get; }
    public string FileName => Preset.FileName;
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public bool IsNormal => VoiceStore.IsNormalFile(Preset.FileName);
    public string GraillonHint => string.IsNullOrEmpty(Preset.GraillonHint) ? "" : "Mit Graillon: " + Preset.GraillonHint;
    public Visibility GraillonVisibility => string.IsNullOrEmpty(Preset.GraillonHint) ? Visibility.Collapsed : Visibility.Visible;
    public string GainText => "Lautstärke " + VoiceStore.FormatGain(Preset.GainDb);
    public Visibility GainVisibility => System.Math.Abs(Preset.GainDb) >= 0.05 ? Visibility.Visible : Visibility.Collapsed;

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private string _hotkeyText = "Kein Shortcut";
    public string HotkeyText { get => _hotkeyText; set => Set(ref _hotkeyText, value); }

    private string _holdText = "Keine Taste";
    public string HoldText { get => _holdText; set => Set(ref _holdText, value); }

    // Profil-Auswahlmodus
    private bool _inProfile;
    public bool InProfile { get => _inProfile; set => Set(ref _inProfile, value); }

    private bool _selecting;
    public bool Selecting
    {
        get => _selecting;
        set { Set(ref _selecting, value); Raise(nameof(SelectorVisibility)); Raise(nameof(ControlsVisibility)); }
    }

    public bool CanToggleProfile => !IsNormal;
    public Visibility DeleteVisibility => IsNormal ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SelectorVisibility => Selecting ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ControlsVisibility => Selecting ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Für Auswahllisten und Screenreader.</summary>
    public override string ToString() => Name;
}

public sealed class SoundItem : ViewModel
{
    public SoundItem(SoundFile file) => File = file;

    public SoundFile File { get; }
    public string FileName => File.FileName;
    public string Name => File.Name;

    private string _hotkeyText = "Kein Shortcut";
    public string HotkeyText { get => _hotkeyText; set => Set(ref _hotkeyText, value); }

    // Auswahlmodus "Sounds auswählen": welche Sounds gehören zum Charakter?
    private bool _inProfile = true;
    public bool InProfile { get => _inProfile; set => Set(ref _inProfile, value); }

    private bool _selecting;
    public bool Selecting
    {
        get => _selecting;
        set { Set(ref _selecting, value); Raise(nameof(SelectorVisibility)); Raise(nameof(ControlsVisibility)); }
    }

    public Visibility SelectorVisibility => Selecting ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ControlsVisibility => Selecting ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>true = Sound läuft durch den aktiven Stimmeffekt (z. B. gedämpft unter der Maske).</summary>
    private bool _throughEffect;
    public bool ThroughEffect { get => _throughEffect; set => Set(ref _throughEffect, value); }

    public sealed record RepeatChoice(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Auswahl "Wiederholen": 1 = einmal, n = n-mal hintereinander, 0 = endlos.</summary>
    public static RepeatChoice[] RepeatChoices { get; } =
    [
        new(1, "Einmal"), new(2, "2×"), new(3, "3×"), new(5, "5×"), new(10, "10×"), new(0, "Endlos (Loop)"),
    ];

    private int _repeat = 1;
    /// <summary>1 = einmal, n = n-mal, 0 = endlos.</summary>
    public int Repeat
    {
        get => _repeat;
        set { Set(ref _repeat, value); Raise(nameof(PlayText)); Raise(nameof(PlayTooltip)); }
    }

    public bool Repeats => Repeat != 1;

    private bool _isPlaying;
    /// <summary>Läuft gerade – dann stoppt erneutes Drücken (Klick oder Shortcut).</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            Set(ref _isPlaying, value);
            Raise(nameof(PlayText)); Raise(nameof(PlayTooltip)); Raise(nameof(ProgressVisibility));
            if (!value) { Progress = 0; TimeText = ""; }
        }
    }

    public string PlayText => IsPlaying ? "■  Stoppen" : Repeat == 0 ? "▶  Loop starten" : "▶  Abspielen";

    public string PlayTooltip => IsPlaying
        ? "Stoppt den Sound (derselbe Shortcut geht auch)."
        : "Abspielen – nochmal drücken (oder derselbe Shortcut) stoppt ihn wieder.";

    // Fortschritt während des Abspielens
    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private string _timeText = "";
    public string TimeText { get => _timeText; set => Set(ref _timeText, value); }

    public Visibility ProgressVisibility => IsPlaying ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Text wie „0:03 / 0:12“, bei Wiederholung mit Durchgang („2/3 · 0:03 / 0:12“, „Loop 4 · …“).</summary>
    public static string FormatProgress(SoundEngine.Progress p)
    {
        static string T(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        // Am Ende nicht auf 0:00 zurückspringen, wenn die Länge minimal überschritten ist
        var total = p.Repeat > 0 ? p.FileDuration * p.Repeat : TimeSpan.MaxValue;
        var inPass = p.Elapsed >= total ? p.FileDuration : p.InPass;
        // Gesamtlänge gerundet (1,9 s → 0:02), Position abgerundet – wie in üblichen Playern
        string times = $"{T(inPass)} / {T(TimeSpan.FromSeconds(Math.Round(p.FileDuration.TotalSeconds)))}";
        return p.Repeat switch
        {
            1 => times,
            0 => $"Loop {p.Pass} · {times}",
            _ => $"{Math.Min(p.Pass, p.Repeat)}/{p.Repeat} · {times}",
        };
    }
}

public sealed class ProfileChip : ViewModel
{
    public ProfileChip(string name, bool active) { Name = name; IsActive = active; }
    public string Name { get; }
    public bool IsActive { get; }
}

public sealed class CheckItem
{
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6B));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x28));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x4B));
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x9C, 0xE8));

    public CheckItem(CheckResult result) => Result = result;

    public CheckResult Result { get; }
    public string Title => Result.Title;
    public string Detail => Result.Detail;
    public string? FixLabel => Result.FixLabel;
    public Visibility FixVisibility => Result.Fix != null ? Visibility.Visible : Visibility.Collapsed;

    public string Icon => Result.State switch
    {
        CheckState.Ok => "",       // Haken
        CheckState.Warning => "",  // Warndreieck
        CheckState.Error => "",    // X
        _ => "",                   // Info
    };

    public Brush IconBrush => Result.State switch
    {
        CheckState.Ok => OkBrush,
        CheckState.Warning => WarnBrush,
        CheckState.Error => ErrorBrush,
        _ => InfoBrush,
    };
}
