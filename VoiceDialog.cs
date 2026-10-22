using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

/// <summary>Name, Beschreibung, Graillon-Hinweis und Lautstärke-Ausgleich einer Stimme anlegen oder ändern.</summary>
public sealed class VoiceDialog : Window
{
    public sealed record Result(string Name, string Description, string Graillon, double GainDb = 0);

    private readonly TextBox _name, _description, _graillon;
    private readonly Slider _gain;

    private VoiceDialog(Window owner, string title, string hint, Result initial, Action<double>? previewGain)
    {
        Owner = owner;
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Language = owner.Language;
        WindowTheme.Apply(this);
        var dim = (Brush)Application.Current.Resources["FgDim"];

        _name = new TextBox { Text = initial.Name, Margin = new Thickness(0, 4, 0, 12) };
        _description = new TextBox
        {
            Text = initial.Description, Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinHeight = 52, VerticalContentAlignment = VerticalAlignment.Top,
        };
        _graillon = new TextBox { Text = initial.Graillon, Margin = new Thickness(0, 4, 0, 4) };

        // Lautstärke-Ausgleich
        var gainLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right };
        _gain = new Slider
        {
            Minimum = -VoiceStore.MaxGainDb, Maximum = VoiceStore.MaxGainDb, Value = initial.GainDb,
            TickFrequency = 0.5, IsSnapToTickEnabled = true, SmallChange = 0.5, LargeChange = 3,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(_gain, "Lautstärke-Ausgleich");
        var zero = new Button { Content = "Zurücksetzen", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(10, 0, 0, 0), ToolTip = "Kein Ausgleich (0 dB)" };
        zero.Click += (_, _) => _gain.Value = 0;

        // Live mithören, falls die Stimme gerade aktiv ist – gedrosselt, damit Equalizer APO nicht ständig neu lädt.
        var previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); previewGain?.Invoke(_gain.Value); };
        void UpdateGainLabel() => gainLabel.Text = VoiceStore.FormatGain(_gain.Value);
        UpdateGainLabel();
        _gain.ValueChanged += (_, _) =>
        {
            UpdateGainLabel();
            if (previewGain == null) return;
            previewTimer.Stop();
            previewTimer.Start();
        };

        var gainRow = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(zero, Dock.Right);
        DockPanel.SetDock(gainLabel, Dock.Right);
        gainRow.Children.Add(zero);
        gainRow.Children.Add(gainLabel);
        gainRow.Children.Add(_gain);

        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x80, 0x7A)), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var ok = new Button { Content = "Speichern", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                error.Text = "Bitte einen Namen eingeben.";
                error.Visibility = Visibility.Visible;
                _name.Focus();
                return;
            }
            DialogResult = true;
        };
        Closed += (_, _) =>
        {
            previewTimer.Stop();
            if (DialogResult != true) previewGain?.Invoke(initial.GainDb); // Vorschau zurücknehmen
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var stack = new StackPanel { Margin = new Thickness(18) };
        if (hint.Length > 0) stack.Children.Add(new TextBlock { Text = hint, Foreground = dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(new TextBlock { Text = "Name" });
        stack.Children.Add(_name);
        stack.Children.Add(new TextBlock { Text = "Beschreibung (optional)" });
        stack.Children.Add(_description);
        stack.Children.Add(new TextBlock { Text = "Graillon-Hinweis (optional) – erscheint gelb unter der Stimme" });
        stack.Children.Add(_graillon);
        stack.Children.Add(new TextBlock { Text = "z. B. „Pitch -2, Formant -2“. Nur eine Notiz für dich – Graillon selbst baust du im EAPO-Editor ein.", Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(new TextBlock { Text = "Lautstärke-Ausgleich" });
        stack.Children.Add(gainRow);
        stack.Children.Add(new TextBlock
        {
            Text = previewGain != null
                ? "Die Stimme ist gerade aktiv – du hörst die Änderung sofort. Wie laut sie bei den anderen ankommt, zeigt Extras → Probehören."
                : "Gleicht aus, wenn die Stimme leiser oder lauter als deine normale Stimme ist. Extras → Probehören misst den Unterschied.",
            Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(error);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) => { _name.SelectAll(); _name.Focus(); Keyboard.Focus(_name); };
    }

    /// <param name="previewGain">Optional: wird beim Verschieben des Lautstärke-Reglers aufgerufen (live anhören).</param>
    public static Result? Ask(Window owner, string title, string hint, Result initial, Action<double>? previewGain = null)
    {
        var dlg = new VoiceDialog(owner, title, hint, initial, previewGain);
        return dlg.ShowDialog() == true
            ? new Result(dlg._name.Text.Trim(), dlg._description.Text.Trim(), dlg._graillon.Text.Trim(), Math.Round(dlg._gain.Value * 2) / 2)
            : null;
    }
}
