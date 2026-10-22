using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RpVoiceSwitcher;

/// <summary>Gemeinsames Erscheinungsbild aller Fenster: App-Icon und dunkle Titelleiste (Windows 10 20H1+ / 11).</summary>
public static class WindowTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static readonly Uri IconUri = new("pack://application:,,,/Assets/app.ico");

    /// <summary>Vor dem ersten Anzeigen (bzw. vor EnsureHandle) aufrufen.</summary>
    public static void Apply(Window window)
    {
        try { window.Icon = BitmapFrame.Create(IconUri); } catch { /* ohne Icon geht's auch */ }
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
        };
    }

    /// <summary>App-Icon in Tray-Größe.</summary>
    public static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            using var stream = Application.GetResourceStream(IconUri)?.Stream;
            if (stream == null) return null;
            var size = System.Windows.Forms.SystemInformation.SmallIconSize;
            return new System.Drawing.Icon(stream, size.Width, size.Height);
        }
        catch { return null; }
    }
}
