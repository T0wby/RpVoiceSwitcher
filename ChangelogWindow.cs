using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RpVoiceSwitcher;

/// <summary>
/// "Was ist neu": nach einem Update die Änderungen seit der letzten Version, über das Hilfe-Menü alle Versionen.
/// Nicht modal, damit es die App nicht blockiert.
/// </summary>
public sealed class ChangelogWindow : Window
{
    private static ChangelogWindow? _open;

    private ChangelogWindow(string heading, string? intro, IReadOnlyList<Changelog.Entry> entries, bool showAllLink)
    {
        Title = "Was ist neu – RP Voice Switcher";
        Width = 640;
        Height = 620;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowTheme.Apply(this);
        var dim = (Brush)Application.Current.Resources["FgDim"];
        var accent = (Brush)Application.Current.Resources["Accent"];
        var current = UpdateChecker.CurrentVersion;

        var stack = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        stack.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (intro != null)
            stack.Children.Add(new TextBlock { Text = intro, Foreground = dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        if (entries.Count == 0)
            stack.Children.Add(new TextBlock { Text = "Keine Einträge gefunden.", Foreground = dim, Margin = new Thickness(0, 12, 0, 0) });

        foreach (var entry in entries)
        {
            var head = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6), TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new Run("Version " + entry.Title));
            if (entry.Version == current)
                head.Inlines.Add(new Run("  · installiert") { Foreground = accent, FontWeight = FontWeights.Normal, FontSize = 12 });
            stack.Children.Add(head);

            foreach (var raw in entry.Lines)
            {
                if (raw.Trim().Length == 0) continue;
                int indent = raw.Length - raw.TrimStart().Length;
                string text = raw.TrimStart();
                bool bullet = text.StartsWith("- ");
                if (bullet) text = text[2..];

                var row = new DockPanel { Margin = new Thickness(indent >= 2 ? 22 : 0, 0, 0, 5) };
                if (bullet)
                {
                    var dot = new TextBlock { Text = indent >= 2 ? "–" : "•", Foreground = accent, Margin = new Thickness(2, 0, 10, 0) };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                }
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = dim, LineHeight = 19 };
                AddInlines(tb, text);
                row.Children.Add(tb);
                stack.Children.Add(row);
            }
        }

        var close = new Button { Content = "Schließen", IsCancel = true, IsDefault = true, MinWidth = 90 };
        close.Click += (_, _) => Close();
        var footerPanel = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        footerPanel.Children.Add(close);
        if (showAllLink)
        {
            var all = new Button { Content = "Alle Versionen anzeigen", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            all.Click += (_, _) => { var owner = Owner; Close(); ShowAll(owner); };
            DockPanel.SetDock(all, Dock.Left);
            footerPanel.Children.Add(all);
        }
        var footer = new Border
        {
            Padding = new Thickness(24, 10, 24, 14),
            Child = footerPanel,
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var root = new DockPanel();
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack, Focusable = false });
        Content = root;
        Closed += (_, _) => _open = null;
    }

    /// <summary>**fett**, `Code` und *kursiv* als Formatierung, Rest als Text.</summary>
    private static void AddInlines(TextBlock tb, string text)
    {
        var fg = (Brush)Application.Current.Resources["Fg"];
        foreach (var part in Regex.Split(text, @"(\*\*.+?\*\*|`.+?`|(?<![\w*])\*(?!\s).+?(?<!\s)\*(?![\w*]))"))
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                tb.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeights.SemiBold, Foreground = fg });
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Consolas"), Foreground = fg });
            else if (part.StartsWith('*') && part.EndsWith('*') && part.Length > 2)
                tb.Inlines.Add(new Run(part[1..^1]) { FontStyle = FontStyles.Italic });
            else
                tb.Inlines.Add(new Run(part));
        }
    }

    private static void Show(Window? owner, ChangelogWindow w)
    {
        _open?.Close();
        _open = w;
        w.Owner = owner is { IsVisible: true } ? owner : null;
        if (w.Owner == null) w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Show();
        w.Activate();
    }

    /// <summary>Nach einem Update: was seit der letzten Version neu ist.</summary>
    public static void ShowWhatsNew(Window? owner, IReadOnlyList<Changelog.Entry> entries, Version? from)
    {
        string v = UpdateChecker.CurrentVersionText;
        string intro = from != null && entries.Count > 1
            ? $"Seit deiner letzten Version ({UpdateChecker.Format(from)}) hat sich das geändert:"
            : "Das hat sich mit dem Update geändert:";
        Show(owner, new ChangelogWindow($"Update auf Version {v}", intro, entries, showAllLink: true));
    }

    /// <summary>Hilfe-Menü: alle Versionen.</summary>
    public static void ShowAll(Window? owner) =>
        Show(owner, new ChangelogWindow("Was ist neu – alle Versionen", $"Installiert: Version {UpdateChecker.CurrentVersionText}", Changelog.All, showAllLink: false));
}
