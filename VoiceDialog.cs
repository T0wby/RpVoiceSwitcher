using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RpVoiceSwitcher;

/// <summary>Name, Beschreibung und Graillon-Hinweis einer Stimme anlegen oder ändern.</summary>
public sealed class VoiceDialog : Window
{
    public sealed record Result(string Name, string Description, string Graillon);

    private readonly TextBox _name, _description, _graillon;

    private VoiceDialog(Window owner, string title, string hint, Result initial)
    {
        Owner = owner;
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var dim = (Brush)Application.Current.Resources["FgDim"];

        _name = new TextBox { Text = initial.Name, Margin = new Thickness(0, 4, 0, 12) };
        _description = new TextBox
        {
            Text = initial.Description, Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinHeight = 52, VerticalContentAlignment = VerticalAlignment.Top,
        };
        _graillon = new TextBox { Text = initial.Graillon, Margin = new Thickness(0, 4, 0, 4) };

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
        stack.Children.Add(new TextBlock { Text = "z. B. „Pitch -2, Formant -2“. Nur eine Notiz für dich – Graillon selbst baust du im EAPO-Editor ein.", Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(error);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) => { _name.SelectAll(); _name.Focus(); Keyboard.Focus(_name); };
    }

    public static Result? Ask(Window owner, string title, string hint, Result initial)
    {
        var dlg = new VoiceDialog(owner, title, hint, initial);
        return dlg.ShowDialog() == true
            ? new Result(dlg._name.Text.Trim(), dlg._description.Text.Trim(), dlg._graillon.Text.Trim())
            : null;
    }
}
