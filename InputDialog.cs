using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RpVoiceSwitcher;

/// <summary>Kleiner Eingabedialog (WPF hat keinen eingebauten), z. B. für Profilnamen.</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _box;

    private InputDialog(Window owner, string title, string prompt, string initial)
    {
        Owner = owner;
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Bg"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Fg"];
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        _box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 14) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => { DialogResult = true; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(_box);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) => { _box.SelectAll(); _box.Focus(); Keyboard.Focus(_box); };
    }

    public static string? Ask(Window owner, string title, string prompt, string initial = "")
    {
        var dlg = new InputDialog(owner, title, prompt, initial);
        return dlg.ShowDialog() == true ? dlg._box.Text.Trim() : null;
    }
}
