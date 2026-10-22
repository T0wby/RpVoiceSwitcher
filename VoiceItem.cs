using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace RpVoiceSwitcher;

public sealed class VoiceItem : INotifyPropertyChanged
{
    public VoiceItem(VoicePreset preset) => Preset = preset;

    public VoicePreset Preset { get; }
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string GraillonHint => string.IsNullOrEmpty(Preset.GraillonHint) ? "" : "Mit Graillon: " + Preset.GraillonHint;
    public Visibility GraillonVisibility => string.IsNullOrEmpty(Preset.GraillonHint) ? Visibility.Collapsed : Visibility.Visible;

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private string _hotkeyText = "Kein Shortcut";
    public string HotkeyText { get => _hotkeyText; set => Set(ref _hotkeyText, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
