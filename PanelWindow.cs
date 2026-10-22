using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RpVoiceSwitcher;

/// <summary>
/// Eigenes Fenster für Einrichtung bzw. Einstellungen (Menü Extras).
/// Der Inhalt bleibt Teil des Hauptfensters (gleiche Felder, gleiche Ereignisse) und wird hier nur angezeigt.
/// Schließen blendet das Fenster aus, damit der Inhalt beim nächsten Öffnen wieder da ist.
/// </summary>
public sealed class PanelWindow : Window
{
    public PanelWindow(Window owner, string title, FrameworkElement panel, double width, double height)
    {
        Owner = owner;
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 480;
        MinHeight = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Language = owner.Language;
        WindowTheme.Apply(this);

        var close = new Button { Content = "Schließen", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Hide();
        var footer = new Border
        {
            Child = close,
            Padding = new Thickness(20, 10, 20, 12),
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var root = new DockPanel();
        root.Children.Add(footer);
        root.Children.Add(panel);
        Content = root;

        // Die Befehle (Strg+N, F1 …) des Hauptfensters auch hier anbieten
        CommandBindings.AddRange(owner.CommandBindings);
        InputBindings.Add(new KeyBinding(new RelayCommand(Hide), Key.Escape, ModifierKeys.None));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Beim Beenden der App wirklich schließen – sonst nur ausblenden.
        if (!_closingForReal)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private bool _closingForReal;

    public void CloseForReal()
    {
        _closingForReal = true;
        Close();
    }

    public void ShowAndActivate()
    {
        if (!Owner.IsVisible) Owner.Show();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private sealed class RelayCommand(System.Action run) : ICommand
    {
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
