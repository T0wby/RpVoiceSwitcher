using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

/// <summary>
/// Extras → Probehören: kurz sprechen, dann normal und mit Effekt vergleichen.
/// Misst auch, ob die Stimme leiser oder lauter ankommt, und kann den Ausgleich direkt setzen.
/// </summary>
public sealed class VoiceTestWindow : Window
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly Func<IReadOnlyList<VoiceItem>> _voices;
    private readonly Func<VoiceItem?> _current;
    private readonly Action<VoiceItem> _activate;
    private readonly Action<VoiceItem, double> _setGain;
    private readonly Func<string> _targetDevice;

    private readonly VoiceTester _tester = new();
    private readonly ComboBox _voiceBox;
    private readonly Button _record, _playDry, _playWet, _playBoth, _stop, _applyGain;
    private readonly TextBlock _status, _result;
    private readonly ProgressBar _dryLevel, _wetLevel;
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private CancellationTokenSource? _cts;
    private DateTime _recordEnd;
    private VoiceItem? _recordedVoice;
    private double _suggestedGain;
    private bool _updatingBox;

    public VoiceTestWindow(Window owner, Func<IReadOnlyList<VoiceItem>> voices, Func<VoiceItem?> current,
                           Action<VoiceItem> activate, Action<VoiceItem, double> setGain, Func<string> targetDevice)
    {
        _voices = voices;
        _current = current;
        _activate = activate;
        _setGain = setGain;
        _targetDevice = targetDevice;

        Owner = owner;
        Title = "Probehören – RP Voice Switcher";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Language = owner.Language;
        WindowTheme.Apply(this);
        var dim = (Brush)Application.Current.Resources["FgDim"];

        var intro = new TextBlock
        {
            Text = "Nimmt 5 Sekunden gleichzeitig auf: dein Mikrofon ohne Effekt und das, was die anderen hören. " +
                   "Danach kannst du beides vergleichen. Am besten mit Kopfhörern – sprich ganz normal, wie im Spiel.",
            TextWrapping = TextWrapping.Wrap, Foreground = dim, Margin = new Thickness(0, 0, 0, 14),
        };

        _voiceBox = new ComboBox { MinWidth = 240, Margin = new Thickness(10, 0, 0, 0), DisplayMemberPath = nameof(VoiceItem.Name) };
        System.Windows.Automation.AutomationProperties.SetName(_voiceBox, "Stimme");
        _voiceBox.SelectionChanged += (_, _) =>
        {
            if (_updatingBox || _voiceBox.SelectedItem is not VoiceItem v || v.IsActive) return;
            _activate(v); // wirkt sofort – so hört man beim Aufnehmen genau diese Stimme
        };
        var voiceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        voiceRow.Children.Add(new TextBlock { Text = "Stimme:", VerticalAlignment = VerticalAlignment.Center });
        voiceRow.Children.Add(_voiceBox);

        _record = new Button { Content = "● Aufnehmen (5 s)", Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        _record.Click += async (_, _) => await RecordAsync();

        _dryLevel = LevelBar();
        _wetLevel = LevelBar();
        var levels = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        levels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        levels.ColumnDefinitions.Add(new ColumnDefinition());
        levels.RowDefinitions.Add(new RowDefinition());
        levels.RowDefinitions.Add(new RowDefinition());
        AddLevel(levels, 0, "Mikrofon (ohne Effekt)", _dryLevel, dim);
        AddLevel(levels, 1, "Bei den anderen (mit Effekt)", _wetLevel, dim);

        _status = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = dim };

        _playDry = PlayButton("▶ Normal", VoiceTester.Playback.Dry);
        _playWet = PlayButton("▶ Mit Effekt", VoiceTester.Playback.Wet);
        _playBoth = PlayButton("▶ Abwechselnd", VoiceTester.Playback.Both);
        _playBoth.ToolTip = "Erst normal, dann mit Effekt";
        _stop = new Button { Content = "■ Stopp", Padding = new Thickness(10, 4, 10, 4), IsEnabled = false };
        _stop.Click += (_, _) => _tester.Stop();
        var playRow = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in new[] { _playDry, _playWet, _playBoth, _stop }) playRow.Children.Add(b);

        _result = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _applyGain = new Button { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        _applyGain.Click += (_, _) => ApplyGain();

        var close = new Button { Content = "Schließen", MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        close.Click += (_, _) => Close();

        var stack = new StackPanel { Margin = new Thickness(18) };
        foreach (UIElement e in new UIElement[] { intro, voiceRow, _record, levels, _status, playRow, _result, _applyGain, close })
            stack.Children.Add(e);
        Content = stack;

        _tester.LevelChanged += (dry, wet) => Dispatcher.InvokeAsync(() =>
        {
            if (!double.IsNaN(dry)) _dryLevel.Value = ToBar(dry);
            if (!double.IsNaN(wet)) _wetLevel.Value = ToBar(wet);
        });
        _countdown.Tick += (_, _) =>
        {
            var left = _recordEnd - DateTime.Now;
            _status.Text = $"Aufnahme läuft … noch {Math.Max(0, Math.Ceiling(left.TotalSeconds)):0} s – jetzt sprechen!";
        };

        RefreshVoices();
        SetPlayEnabled(false);
        _status.Text = "Bereit.";
    }

    private static ProgressBar LevelBar() => new()
    {
        Height = 8, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4),
        Background = (Brush)Application.Current.Resources["Card"],
        Foreground = (Brush)Application.Current.Resources["Accent"],
        BorderThickness = new Thickness(0),
    };

    private static void AddLevel(Grid grid, int row, string label, ProgressBar bar, Brush dim)
    {
        var text = new TextBlock { Text = label, Foreground = dim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(text, row);
        Grid.SetRow(bar, row);
        Grid.SetColumn(bar, 1);
        grid.Children.Add(text);
        grid.Children.Add(bar);
    }

    /// <summary>Pegel logarithmisch anzeigen (-60 dB … 0 dB).</summary>
    private static double ToBar(double peak) => peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);

    private Button PlayButton(string text, VoiceTester.Playback what)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
        b.Click += (_, _) =>
        {
            string? error = _tester.Play(what, () => Dispatcher.InvokeAsync(() => { _stop.IsEnabled = false; SetPlayEnabled(true); }));
            if (error != null) { _status.Text = error; return; }
            _stop.IsEnabled = true;
            _status.Text = what == VoiceTester.Playback.Both ? "Spielt: erst normal, dann mit Effekt …" : "Spielt …";
        };
        return b;
    }

    private void SetPlayEnabled(bool on)
    {
        bool has = on && _tester.Dry != null;
        _playDry.IsEnabled = _playWet.IsEnabled = _playBoth.IsEnabled = has;
    }

    /// <summary>Stimmenliste aktualisieren, z. B. nach Umschalten per Shortcut.</summary>
    public void RefreshVoices() => Dispatcher.InvokeAsync(() =>
    {
        // Verzögert, weil das oft mitten aus SelectionChanged heraus (Umschalten über die Auswahl) aufgerufen wird.
        _updatingBox = true;
        var list = _voices();
        _voiceBox.ItemsSource = list;
        _voiceBox.SelectedItem = list.FirstOrDefault(v => v.IsActive) ?? _current();
        _updatingBox = false;
    });

    private async Task RecordAsync()
    {
        _tester.Stop();
        _record.IsEnabled = false;
        _voiceBox.IsEnabled = false;
        SetPlayEnabled(false);
        _result.Visibility = _applyGain.Visibility = Visibility.Collapsed;
        _recordedVoice = _current();
        _cts = new CancellationTokenSource();
        _recordEnd = DateTime.Now + Duration;
        _countdown.Start();
        try
        {
            await _tester.RecordAsync(SetupChecker.FindMicrophoneId(), _targetDevice(), Duration, _cts.Token);
            _status.Text = $"Aufgenommen: {_tester.MicrophoneName} und {_tester.TargetName}.";
            ShowResult();
            SetPlayEnabled(true);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            _status.Text = "Aufnahme fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _countdown.Stop();
            _dryLevel.Value = _wetLevel.Value = 0;
            _record.IsEnabled = true;
            _voiceBox.IsEnabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ShowResult()
    {
        double? dry = _tester.Dry is { } d ? VoiceTester.SpeechLevelDb(d) : null;
        double? wet = _tester.Wet is { } w ? VoiceTester.SpeechLevelDb(w) : null;
        _result.Visibility = Visibility.Visible;
        _applyGain.Visibility = Visibility.Collapsed;
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

        if (dry == null)
        {
            _result.Text = "Am Mikrofon war nichts zu hören. Ist das richtige Mikro angeschlossen und nicht stummgeschaltet?";
            return;
        }
        if (wet == null)
        {
            _result.Text = $"Bei den anderen kam nichts an ({_tester.TargetName}). Prüfe Extras → Einrichtung: " +
                           "wird das Mikrofon per „Abhören“ ins Kabel geleitet?";
            return;
        }

        double diff = wet.Value - dry.Value;
        string name = _recordedVoice?.Name ?? "Die Stimme";
        _result.Text = Math.Abs(diff) < 1.5
            ? $"„{name}“ kommt etwa gleich laut an wie deine normale Stimme ({VoiceStore.FormatGain(Math.Round(diff * 2) / 2)}). Passt."
            : $"„{name}“ kommt {Math.Abs(diff).ToString("0.#", de)} dB {(diff < 0 ? "leiser" : "lauter")} an als deine normale Stimme.";

        if (_recordedVoice != null && Math.Abs(diff) >= 1.5)
        {
            _suggestedGain = Math.Clamp(Math.Round((_recordedVoice.Preset.GainDb - diff) * 2) / 2, -VoiceStore.MaxGainDb, VoiceStore.MaxGainDb);
            if (Math.Abs(_suggestedGain - _recordedVoice.Preset.GainDb) >= 0.5)
            {
                _applyGain.Content = $"Lautstärke angleichen ({VoiceStore.FormatGain(_suggestedGain)})";
                _applyGain.Visibility = Visibility.Visible;
            }
        }
    }

    private void ApplyGain()
    {
        if (_recordedVoice == null) return;
        var name = _recordedVoice.FileName;
        _setGain(_recordedVoice, _suggestedGain);
        _applyGain.Visibility = Visibility.Collapsed;
        _result.Text += $" Ausgleich gesetzt: {VoiceStore.FormatGain(_suggestedGain)}. Zum Prüfen einfach nochmal aufnehmen.";
        RefreshVoices();
        // Nach dem Neuladen ist das VoiceItem ein neues Objekt
        _recordedVoice = _voices().FirstOrDefault(v => v.FileName == name);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel();
        _tester.Dispose();
        base.OnClosing(e);
    }
}
