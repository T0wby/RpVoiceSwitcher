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
}

public sealed class SoundItem : ViewModel
{
    public SoundItem(SoundFile file) => File = file;

    public SoundFile File { get; }
    public string FileName => File.FileName;
    public string Name => File.Name;

    private string _hotkeyText = "Kein Shortcut";
    public string HotkeyText { get => _hotkeyText; set => Set(ref _hotkeyText, value); }

    /// <summary>true = Sound läuft durch den aktiven Stimmeffekt (z. B. gedämpft unter der Maske).</summary>
    private bool _throughEffect;
    public bool ThroughEffect { get => _throughEffect; set => Set(ref _throughEffect, value); }
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
