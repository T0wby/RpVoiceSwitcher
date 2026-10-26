using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

/// <summary>
/// Kleine Einblendung oben in der Bildschirmmitte. Nimmt keinen Fokus und lässt Klicks durch,
/// damit das Spiel nicht unterbrochen wird (sichtbar in Fenster-/Randlos-Modus, nicht in exklusivem Vollbild).
/// </summary>
public sealed class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly TextBlock _text;
    private readonly DispatcherTimer _hideTimer;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1B, 0x1B, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x8C, 0x28)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22, 10, 22, 10),
            Child = _text,
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
            fade.Completed += (_, _) => Hide();
            BeginAnimation(OpacityProperty, fade);
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE,
                GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    private bool _closed;
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _hideTimer.Stop();
        base.OnClosed(e);
    }

    public void Flash(string message)
    {
        if (_closed) return; // App wird gerade beendet
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        _text.Text = message;

        Show();
        UpdateLayout();
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = 40;
        Topmost = false; // erneut nach vorne holen, falls das Spiel selbst "topmost" ist
        Topmost = true;
        _hideTimer.Start();
    }
}
