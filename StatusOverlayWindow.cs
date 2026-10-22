using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

/// <summary>
/// Dauerhafte Statusanzeige der aktiven Stimme.
/// Im Normalbetrieb klick-durchlässig und ohne Fokus (stört das Spiel nicht),
/// im Bearbeitungsmodus per Maus verschiebbar. Sichtbar in Fenster-/Randlos-Modus, nicht in exklusivem Vollbild.
/// </summary>
public sealed class StatusOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x8C, 0x28));
    private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x55, 0x5E));

    private readonly TextBlock _name;
    private readonly TextBlock _hint;
    private readonly Ellipse _dot;
    private readonly Border _border;
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private bool _editMode;

    /// <summary>Wird nach jedem Verschieben mit der neuen Position ausgelöst.</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Doppelklick im Bearbeitungsmodus = fertig.</summary>
    public event Action? EditFinished;

    public StatusOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _dot = new Ellipse { Width = 10, Height = 10, Fill = IdleBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var mic = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _name = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _hint = new TextBlock
        {
            Text = "Ziehen zum Verschieben · Doppelklick = fertig",
            Foreground = AccentBrush,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(mic);
        row.Children.Add(_name);

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_soundRows);
        stack.Children.Add(_hint);

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1B, 0x1B, 0x1F)),
            BorderBrush = IdleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 12, 5),
            Child = stack,
        };
        Content = _border;

        // Manche Spiele schieben sich selbst nach vorne – regelmäßig wieder "topmost" setzen (ohne Fokus zu klauen).
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) =>
        {
            if (IsVisible && _hwnd != IntPtr.Zero)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyExStyle();
            _topmostTimer.Start();
        };
        Closed += (_, _) => _topmostTimer.Stop();

        MouseLeftButtonDown += OnMouseDown;
    }

    private TextBlock? _transmit;

    /// <summary>Zeile "● Dauersenden" unter der Stimme (nur sichtbar, wenn an).</summary>
    public void SetTransmitting(bool on)
    {
        if (_transmit == null)
        {
            if (!on) return;
            _transmit = new TextBlock
            {
                Text = "● Dauersenden",
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x5A, 0x4E)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(1, 4, 0, 0),
            };
            ((StackPanel)_border.Child).Children.Insert(1, _transmit);
        }
        _transmit.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetVoice(string name, bool isNormal)
    {
        _name.Text = name;
        _dot.Fill = isNormal ? IdleBrush : AccentBrush;
    }

    // ---------- Laufende Sounds ----------

    /// <summary>Ein laufender Sound: Name, Fortschritt 0 … 1, Zeittext.</summary>
    public sealed record SoundLine(string Name, double Fraction, string Time);

    private readonly StackPanel _soundRows = new() { Margin = new Thickness(0, 0, 0, 0) };
    private sealed record SoundRow(TextBlock Name, ProgressBar Bar, TextBlock Time);
    private readonly System.Collections.Generic.List<SoundRow> _rows = new();

    /// <summary>Zeigt unter der Stimme die laufenden Sounds mit Fortschrittsbalken (leer = keine Zeile).</summary>
    public void SetSounds(System.Collections.Generic.IReadOnlyList<SoundLine> sounds)
    {
        // Zeilen wiederverwenden statt neu bauen – sonst flackert es bei jeder Aktualisierung
        while (_rows.Count < sounds.Count)
        {
            var name = new TextBlock { Foreground = Brushes.White, FontSize = 12, Width = 130, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var bar = new ProgressBar
            {
                Width = 110, Height = 4, Minimum = 0, Maximum = 1, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x88)), Foreground = AccentBrush, BorderThickness = new Thickness(0),
            };
            var time = new TextBlock { Foreground = IdleBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var icon = new TextBlock { Text = "♪", Foreground = AccentBrush, FontSize = 12, Margin = new Thickness(1, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            line.Children.Add(icon);
            line.Children.Add(name);
            line.Children.Add(bar);
            line.Children.Add(time);
            _soundRows.Children.Add(line);
            _rows.Add(new SoundRow(name, bar, time));
        }
        while (_rows.Count > sounds.Count)
        {
            _soundRows.Children.RemoveAt(_soundRows.Children.Count - 1);
            _rows.RemoveAt(_rows.Count - 1);
        }
        for (int i = 0; i < sounds.Count; i++)
        {
            _rows[i].Name.Text = sounds[i].Name;
            _rows[i].Bar.Value = sounds[i].Fraction;
            _rows[i].Time.Text = sounds[i].Time;
        }
    }

    private double _scale = -1, _opacity = -1;

    /// <summary>Größe (1 = 100 %) und Deckkraft (0,3 … 1) der Anzeige.</summary>
    public void SetLook(double scale, double opacity)
    {
        if (scale == _scale && opacity == _opacity) return; // unverändert – nichts neu berechnen
        _scale = scale;
        _opacity = opacity;
        scale = Math.Clamp(scale, 0.6, 2.5);
        _border.LayoutTransform = Math.Abs(scale - 1) < 0.01 ? Transform.Identity : new ScaleTransform(scale, scale);
        Opacity = Math.Clamp(opacity, 0.2, 1);
        UpdateLayout();
        if (IsVisible) ClampToScreen();
    }

    /// <summary>Zeigt die Anzeige an der gespeicherten Position (oder oben links, falls keine gespeichert ist).</summary>
    public void ShowAt(double? left, double? top)
    {
        var area = SystemParameters.WorkArea;
        Left = left ?? area.Left + 16;
        Top = top ?? area.Top + 16;
        Show();
        UpdateLayout();
        ClampToScreen();
    }

    public void MoveTo(double? left, double? top)
    {
        var area = SystemParameters.WorkArea;
        Left = left ?? area.Left + 16;
        Top = top ?? area.Top + 16;
        ClampToScreen();
    }

    public void SetEditMode(bool on)
    {
        _editMode = on;
        ApplyExStyle();
        _hint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _border.BorderBrush = on ? AccentBrush : IdleBorder;
        _border.BorderThickness = new Thickness(on ? 2 : 1);
        Cursor = on ? Cursors.SizeAll : null;
        UpdateLayout();
        ClampToScreen();
    }

    private void ApplyExStyle()
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        ex = _editMode
            ? ex & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE
            : ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        if (e.ClickCount == 2)
        {
            EditFinished?.Invoke();
            return;
        }
        try { DragMove(); } catch (InvalidOperationException) { /* Maus schon losgelassen */ }
        ClampToScreen();
        PositionChanged?.Invoke(Left, Top);
    }

    /// <summary>Hält die Anzeige innerhalb aller Bildschirme, z. B. wenn ein Monitor abgesteckt wurde.</summary>
    private void ClampToScreen()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        double w = ActualWidth > 0 ? ActualWidth : 160, h = ActualHeight > 0 ? ActualHeight : 32;
        Left = Math.Max(vl, Math.Min(Left, vr - w));
        Top = Math.Max(vt, Math.Min(Top, vb - h));
    }
}
