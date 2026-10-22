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

    public void SetVoice(string name, bool isNormal)
    {
        _name.Text = name;
        _dot.Fill = isNormal ? IdleBrush : AccentBrush;
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
