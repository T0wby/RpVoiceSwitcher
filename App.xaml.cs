using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace RpVoiceSwitcher;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "RpVoiceSwitcher_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("RP Voice Switcher läuft bereits (siehe Infobereich der Taskleiste).",
                "RP Voice Switcher", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        // --settings <Datei>: z. B. für Tests, damit die echten Einstellungen nie angefasst werden
        int s = Array.FindIndex(e.Args, a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        if (s >= 0 && s + 1 < e.Args.Length)
            AppSettings.OverridePath = System.IO.Path.GetFullPath(e.Args[s + 1]);
        var window = new MainWindow();
        if (startMinimized)
            window.InitializeHidden();
        else
            window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
