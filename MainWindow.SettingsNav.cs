using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RpVoiceSwitcher;

// Einstellungen: Navigation links springt zur Karte, die gerade sichtbare Karte ist links markiert.
public partial class MainWindow
{
    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string target }) ScrollToSettingsSection(target);
    }

    /// <summary>Karte oben im Einstellungsfenster zeigen (z. B. "PttSection").</summary>
    private void ScrollToSettingsSection(string name)
    {
        if (FindName(name) is not FrameworkElement card) return;
        SettingsScroll.UpdateLayout();
        var top = card.TransformToAncestor(SettingsCards).Transform(new Point(0, 0)).Y;
        SettingsScroll.ScrollToVerticalOffset(Math.Max(0, top - 4));
        MarkSettingsNav(name);
    }

    private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (SettingsCards.Children.Count == 0) return;
        // Ganz unten: letzte Karte markieren, auch wenn sie nicht bis nach oben scrollen kann
        if (SettingsScroll.ScrollableHeight > 0 && SettingsScroll.VerticalOffset >= SettingsScroll.ScrollableHeight - 1)
        {
            MarkSettingsNav(((FrameworkElement)SettingsCards.Children[^1]).Name);
            return;
        }
        string current = ((FrameworkElement)SettingsCards.Children[0]).Name;
        foreach (FrameworkElement card in SettingsCards.Children)
        {
            var y = card.TransformToAncestor(SettingsCards).Transform(new Point(0, 0)).Y;
            if (y - SettingsScroll.VerticalOffset <= 40) current = card.Name;
        }
        MarkSettingsNav(current);
    }

    private void MarkSettingsNav(string name)
    {
        foreach (var b in SettingsNav.Children.OfType<Button>())
            b.Tag = (b.CommandParameter as string) == name ? "on" : null;
    }
}
