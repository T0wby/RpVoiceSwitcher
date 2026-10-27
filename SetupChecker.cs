using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace RpVoiceSwitcher;

public enum CheckState { Ok, Warning, Error, Info }

public sealed class CheckResult
{
    public required string Title { get; init; }
    public required CheckState State { get; init; }
    public string Detail { get; init; } = "";
    public string? FixLabel { get; init; }
    public Action? Fix { get; init; }
}

/// <summary>
/// Prüft die komplette Kette: Mikrofon → (Abhören) → CABLE Input → CABLE Output (+ Equalizer APO) → Spiel/Discord.
/// Liest nur – Änderungen passieren erst, wenn der Nutzer auf "Beheben" klickt.
/// </summary>
public static class SetupChecker
{
    private const string MMDevices = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";
    private const string PropDesc = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string PropInterface = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";
    private const string PropListenTarget = "{24dbb0fc-9311-4b3d-9cf0-18ff155639d4},0";
    private const string PropListenEnabled = "{24dbb0fc-9311-4b3d-9cf0-18ff155639d4},1";
    private const string EapoClsid = "EACD2258-FCAC-4FF4-B36D-419E924A6D79";

    private sealed record Endpoint(string Guid, string Name, bool Active, string? ListenTarget, bool ListenEnabled, bool HasEapo);

    public const string EapoUrl = "https://sourceforge.net/projects/equalizerapo/";
    public const string CableUrl = "https://vb-audio.com/Cable/";

    public static List<CheckResult> Run(VoiceStore? store, AppSettings settings, Action repairConfig, Action<string> setTargetDevice)
    {
        var results = new List<CheckResult>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        var capture = ReadEndpoints(root, "Capture");
        var render = ReadEndpoints(root, "Render");

        // 1) Equalizer APO
        if (store == null)
        {
            results.Add(new CheckResult
            {
                Title = "Equalizer APO",
                State = CheckState.Error,
                Detail = "Nicht gefunden. Equalizer APO wendet die Stimmen-Effekte an und wird zwingend gebraucht.",
                FixLabel = "Download-Seite öffnen",
                Fix = () => OpenUrl(EapoUrl),
            });
        }
        else
        {
            results.Add(new CheckResult { Title = "Equalizer APO", State = CheckState.Ok, Detail = "Installiert: " + store.ConfigDir });

            // Ohne Schreibrechte im Config-Ordner kann die App nicht umschalten (Equalizer APO liegt unter "Programme").
            if (!CanWrite(store.ConfigDir))
                results.Add(new CheckResult
                {
                    Title = "Schreibrechte im Equalizer-APO-Ordner",
                    State = CheckState.Error,
                    Detail = "Die App darf die Stimmen-Dateien nicht schreiben – Umschalten wirkt dann nicht. " +
                             "Einmalig mit Administratorrechten freigeben (Windows fragt nach).",
                    FixLabel = "Rechte freigeben …",
                    Fix = () => GrantWriteAccess(store.ConfigDir),
                });

            bool included = store.IsMainConfigIncludingVoice();
            results.Add(new CheckResult
            {
                Title = "Stimmen in Equalizer APO eingebunden",
                State = included ? CheckState.Ok : CheckState.Error,
                Detail = included
                    ? "config.txt bindet voice.txt ein."
                    : "In der config.txt fehlt die Zeile \"Include: voice.txt\" – ohne sie hat das Umschalten keine Wirkung.",
                FixLabel = included ? null : "Beheben",
                Fix = included ? null : repairConfig,
            });
        }

        // 2) VB-CABLE
        var cableOut = capture.FirstOrDefault(e => e.Active && e.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));
        var cableIn = render.FirstOrDefault(e => e.Active && e.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        bool cableOk = cableOut != null && cableIn != null;
        results.Add(new CheckResult
        {
            Title = "Virtuelles Kabel (VB-CABLE)",
            State = cableOk ? CheckState.Ok : CheckState.Error,
            Detail = cableOk
                ? "CABLE Input und CABLE Output sind vorhanden."
                : "Nicht gefunden. Über das Kabel läuft deine Stimme mit Effekt ins Spiel. Nach der Installation neu starten.",
            FixLabel = cableOk ? null : "Download-Seite öffnen",
            Fix = cableOk ? null : () => OpenUrl(CableUrl),
        });

        // 3) Equalizer APO am Zielgerät
        var targets = capture.Where(e => e.Active && e.Name.Contains(settings.TargetDevice, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0)
        {
            results.Add(new CheckResult
            {
                Title = "Zielgerät der Stimmen",
                State = CheckState.Error,
                Detail = $"Kein aktives Aufnahmegerät passt zu \"{settings.TargetDevice}\".",
                FixLabel = cableOut != null ? "Auf CABLE Output setzen" : null,
                Fix = cableOut != null ? () => setTargetDevice("CABLE Output") : null,
            });
        }
        else if (store != null)
        {
            var target = targets.FirstOrDefault(t => t.HasEapo) ?? targets[0];
            results.Add(new CheckResult
            {
                Title = "Equalizer APO am Zielgerät",
                State = target.HasEapo ? CheckState.Ok : CheckState.Error,
                Detail = target.HasEapo
                    ? $"Aktiv auf \"{target.Name}\"."
                    : $"Auf \"{target.Name}\" nicht installiert. Im Device Selector unter \"Aufnahmegeräte\" anhaken und danach neu starten.",
                FixLabel = target.HasEapo ? null : "Device Selector öffnen",
                Fix = target.HasEapo ? null : () => OpenDeviceSelector(store),
            });
        }

        // 3b) Exklusivmodus am Zielgerät – sonst kann ein Spiel den Effekt umgehen
        if (targets.Count > 0 && ExclusiveMode.Probe(settings.TargetDevice) is { } exclusive)
        {
            results.Add(new CheckResult
            {
                Title = "Exklusivmodus am Zielgerät",
                State = exclusive.Allowed ? CheckState.Warning : CheckState.Ok,
                Detail = exclusive.Allowed
                    ? $"An „{exclusive.Name}“ dürfen Programme die alleinige Kontrolle übernehmen. Tut ein Spiel oder Voice-Client das, ist der Stimmeffekt weg. Besser abschalten (Windows fragt nach Administratorrechten)."
                    : "Aus – kein Programm kann den Stimmeffekt umgehen.",
                FixLabel = exclusive.Allowed ? "Abschalten …" : null,
                Fix = exclusive.Allowed ? () => { if (ExclusiveMode.Disable(exclusive.DeviceId) is string error) throw new InvalidOperationException(error); } : null,
            });
        }

        // 3c) Windows-Standardmikrofon – Steam (und damit z. B. nanos world) fragt nicht, sondern nimmt den Standard
        if (cableOk)
        {
            var mic = DefaultMic.Read(settings.TargetDevice);
            if (mic.CableId != null)
            {
                string Others() => string.Join(" / ", new[] { mic.DefaultIsCable ? null : mic.DefaultName, mic.CommsIsCable ? null : mic.CommsName }
                                                      .Where(n => n != null).Distinct());
                results.Add(new CheckResult
                {
                    Title = "Windows-Standardmikrofon",
                    State = mic.Ok ? CheckState.Ok : CheckState.Warning,
                    Detail = mic.Ok
                        ? $"„{mic.CableName}“ ist Standard- und Kommunikationsgerät – Programme ohne eigene Auswahl (z. B. Steam-Sprachchat) bekommen die Effekte."
                        : $"Standard ist gerade „{Others()}“. Programme, die einfach das Standardmikrofon nehmen – z. B. der Steam-Sprachchat und damit nanos world –, " +
                          "hören dich dann ohne Effekt und ohne Sounds. Springt das immer wieder zurück, stellt meist ein anderes Programm sein Gerät als Standard ein " +
                          "(z. B. Shure MOTIV Mix, NVIDIA Broadcast, Realtek/Sonic Studio) – dann in den Einstellungen „Kabel als Standardmikrofon halten“ anhaken.",
                    FixLabel = mic.Ok ? null : "Kabel als Standard setzen",
                    Fix = mic.Ok ? null : () => { if (DefaultMic.SetCableAsDefault(settings.TargetDevice) is string error) throw new InvalidOperationException(error); },
                });
            }
        }

        // 4) Mikrofon → Kabel weiterleiten
        if (cableIn != null)
        {
            var mics = capture.Where(e => e.Active && !e.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)).ToList();
            var toCable = mics.Where(m => m.ListenEnabled && Contains(m.ListenTarget, cableIn.Guid)).ToList();
            var elsewhere = mics.Where(m => m.ListenEnabled && !Contains(m.ListenTarget, cableIn.Guid)).ToList();
            // Eingerichtet, aber gerade nicht da (USB getrennt, Stromsparmodus …)
            var disconnected = capture.Where(e => !e.Active && !e.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                                                 && e.ListenEnabled && Contains(e.ListenTarget, cableIn.Guid)).ToList();

            if (toCable.Count == 0 && disconnected.Count > 0)
                results.Add(new CheckResult
                {
                    Title = "Mikrofon ins Kabel weitergeleitet",
                    State = CheckState.Error,
                    Detail = $"„{disconnected[0].Name}“ ist richtig eingerichtet, aber gerade nicht angeschlossen (USB-Kabel, Hub, Stromsparmodus?). " +
                             "Sobald es wieder verbunden ist, aktualisiert sich diese Prüfung von selbst.",
                });
            else if (toCable.Count == 1)
            {
                string micId = "{0.0.1.00000000}." + toCable[0].Guid;
                results.Add(new CheckResult
                {
                    Title = "Mikrofon ins Kabel weitergeleitet",
                    State = CheckState.Ok,
                    Detail = $"{toCable[0].Name} → {cableIn.Name}. Kommt trotzdem nichts im Kabel an, hängt „Abhören“ – „Neu starten“ schaltet es kurz aus und wieder an.",
                    FixLabel = "Neu starten",
                    Fix = () =>
                    {
                        MicListen.Log($"Neustart ({toCable[0].Name}): von Hand in der Einrichtung");
                        if (MicListen.Restart(micId) is string error) throw new InvalidOperationException(error);
                    },
                });
            }
            else if (toCable.Count > 1)
                results.Add(new CheckResult
                {
                    Title = "Mikrofon ins Kabel weitergeleitet",
                    State = CheckState.Warning,
                    Detail = "Mehrere Mikrofone werden ins Kabel geleitet – die anderen hören dich evtl. doppelt: " + string.Join(", ", toCable.Select(m => m.Name)),
                    FixLabel = "Aufnahme-Einstellungen öffnen",
                    Fix = () => OpenSoundPanel(1),
                });
            else
                results.Add(new CheckResult
                {
                    Title = "Mikrofon ins Kabel weitergeleitet",
                    State = CheckState.Error,
                    Detail = "Kein Mikrofon wird ins Kabel geleitet. Dein Mikrofon doppelklicken → Tab \"Abhören\" → \"Dieses Gerät als Wiedergabequelle verwenden\" → \"CABLE Input\" wählen."
                             + (elsewhere.Count > 0 ? $" (Aktuell hört \"{elsewhere[0].Name}\" auf ein anderes Gerät ab.)" : ""),
                    FixLabel = "Aufnahme-Einstellungen öffnen",
                    Fix = () => OpenSoundPanel(1),
                });
        }

        // 5) Standard-Wiedergabegerät
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var standard = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            bool isCable = standard.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase);
            results.Add(new CheckResult
            {
                Title = "Standard-Wiedergabegerät",
                State = isCable ? CheckState.Error : CheckState.Ok,
                Detail = isCable
                    ? $"\"{standard.FriendlyName}\" ist Standard – dann hörst du selbst nichts mehr, und andere hören deine PC-Sounds. " +
                      "Das passiert oft, wenn das Headset oder Mikro getrennt wurde. Wähle deine Kopfhörer oder Lautsprecher."
                    : standard.FriendlyName,
                FixLabel = isCable ? "Wiedergabe-Einstellungen öffnen" : null,
                Fix = isCable ? () => OpenSoundPanel(0) : null,
            });
        }
        catch { /* kein Ausgabegerät – nicht unser Thema */ }

        // 6) Graillon (optional)
        string? graillon = FindGraillon();
        results.Add(new CheckResult
        {
            Title = "Graillon (optional)",
            State = graillon != null ? CheckState.Ok : CheckState.Info,
            Detail = graillon != null
                ? "Gefunden: " + graillon
                : "Nicht gefunden. Nur nötig, wenn du die Tonhöhe ändern willst (tiefere/höhere Stimme).",
            FixLabel = graillon != null ? null : "Download-Seite öffnen",
            Fix = graillon != null ? null : () => OpenUrl("https://www.auburnsounds.com/products/Graillon.html"),
        });

        // 7) Sprachtaste (Push-to-Talk) – nur ein Hinweis, das Spiel selbst lässt sich nicht abfragen
        var ptt = PushToTalk.Binding.Parse(settings.PushToTalkKey);
        results.Add(new CheckResult
        {
            Title = "Sprachtaste im Spiel (Push-to-Talk)",
            State = CheckState.Info,
            Detail = ptt is PushToTalk.Binding b
                ? $"Eingestellt: {b}" + (settings.PushToTalkEnabled ? " – wird beim Abspielen von Sounds automatisch gedrückt." : " – beim Abspielen von Sounds aus (nur Dauersenden).") +
                  " Muss dieselbe Taste sein wie im Spiel."
                : "Nicht eingestellt. Nur nötig, wenn das Spiel nur mit gedrückter Sprechtaste sendet (z. B. nanos world): Einstellungen → Sprachtaste im Spiel.",
        });

        // 8) Hinweis Spiel/Discord – lässt sich nicht von außen prüfen
        results.Add(new CheckResult
        {
            Title = "Im Spiel und in Discord",
            State = CheckState.Info,
            Detail = "Als Mikrofon \"CABLE Output\" auswählen. Wo man kein Gerät wählen kann (z. B. Steam-Sprachchat), zählt das Windows-Standardmikrofon. " +
                     "Starke Rauschunterdrückung (z. B. Krisp) kann Effekte abschwächen.",
        });

        return results;
    }

    private static bool Contains(string? haystack, string guid) =>
        haystack != null && haystack.Contains(guid, StringComparison.OrdinalIgnoreCase);

    /// <summary>Zustand aller Einrichtungs-Schritte auf einen Blick (für den Assistenten).</summary>
    public sealed record Status
    {
        public bool CableInstalled { get; init; }
        public bool EapoInstalled { get; init; }
        public string? EapoConfigDir { get; init; }
        public bool EapoOnTarget { get; init; }
        public string? TargetName { get; init; }
        /// <summary>Equalizer APO wurde nach dem letzten Windows-Start eingerichtet – erst ein Neustart aktiviert es.</summary>
        public bool RestartPending { get; init; }
        public bool ConfigWritable { get; init; }
        public bool IncludeOk { get; init; }
        public string? MicToCable { get; init; }
        public string? MicDisconnected { get; init; }
        public string? MicListensElsewhere { get; init; }
        public string? DefaultPlayback { get; init; }
        public bool DefaultPlaybackIsCable { get; init; }
        public string? Graillon { get; init; }
        /// <summary>Windows-Standardmikrofon (Standard- und Kommunikationsgerät).</summary>
        public DefaultMic.State? DefaultMic { get; init; }
        /// <summary>null = Zielgerät nicht gefunden.</summary>
        public ExclusiveMode.Info? Exclusive { get; init; }
    }

    public static Status Probe(AppSettings settings)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var capture = ReadEndpoints(root, "Capture");
            var render = ReadEndpoints(root, "Render");
            var cableOut = capture.FirstOrDefault(e => e.Active && e.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));
            var cableIn = render.FirstOrDefault(e => e.Active && e.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
            var store = VoiceStore.TryCreate(settings.VoicesFolder);

            var targets = capture.Where(e => e.Active && e.Name.Contains(settings.TargetDevice, StringComparison.OrdinalIgnoreCase)).ToList();
            var target = targets.FirstOrDefault(t => t.HasEapo) ?? targets.FirstOrDefault();
            bool restartPending = false;
            if (target is { HasEapo: true })
            {
                var registered = KeyLastWriteUtc(root, $@"SOFTWARE\EqualizerAPO\Child APOs\{target.Guid}");
                var bootTime = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
                restartPending = registered is DateTime r && r > bootTime;
            }

            var mics = capture.Where(e => !e.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)).ToList();
            var toCable = cableIn == null ? null : mics.FirstOrDefault(m => m.Active && m.ListenEnabled && Contains(m.ListenTarget, cableIn.Guid));
            var gone = cableIn == null ? null : mics.FirstOrDefault(m => !m.Active && m.ListenEnabled && Contains(m.ListenTarget, cableIn.Guid));
            var elsewhere = mics.FirstOrDefault(m => m.Active && m.ListenEnabled && (cableIn == null || !Contains(m.ListenTarget, cableIn.Guid)));

            string? playback = null;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                playback = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName;
            }
            catch { /* kein Ausgabegerät */ }

            return new Status
            {
                CableInstalled = cableOut != null && cableIn != null,
                EapoInstalled = store != null,
                EapoConfigDir = store?.ConfigDir,
                EapoOnTarget = target?.HasEapo == true,
                TargetName = target?.Name,
                RestartPending = restartPending,
                ConfigWritable = store != null && CanWrite(store.ConfigDir),
                IncludeOk = store?.IsMainConfigIncludingVoice() == true,
                MicToCable = toCable?.Name,
                MicDisconnected = toCable == null ? gone?.Name : null,
                MicListensElsewhere = elsewhere?.Name,
                DefaultPlayback = playback,
                DefaultPlaybackIsCable = playback?.Contains("CABLE", StringComparison.OrdinalIgnoreCase) == true,
                Graillon = FindGraillon(),
                DefaultMic = cableOut != null ? RpVoiceSwitcher.DefaultMic.Read(settings.TargetDevice) : null,
                Exclusive = target != null ? ExclusiveMode.Probe(settings.TargetDevice) : null,
            };
        }
        catch
        {
            return new Status();
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle hKey, IntPtr cls, IntPtr clsLen, IntPtr reserved,
        IntPtr subKeys, IntPtr maxSubKeyLen, IntPtr maxClassLen, IntPtr values, IntPtr maxValueNameLen, IntPtr maxValueLen,
        IntPtr securityDescriptor, out long lastWriteTime);

    /// <summary>Wann ein Registry-Schlüssel zuletzt geändert wurde (= wann Equalizer APO am Gerät eingerichtet wurde).</summary>
    private static DateTime? KeyLastWriteUtc(RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return null;
            return RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                                   IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out long ft) == 0
                ? DateTime.FromFileTimeUtc(ft) : null;
        }
        catch { return null; }
    }

    /// <summary>Alle aktiven Audiogeräte als Textzeilen (für die Diagnose).</summary>
    public static IEnumerable<string> DescribeEndpoints()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        foreach (var (flow, label) in new[] { ("Capture", "Aufnahme"), ("Render", "Wiedergabe") })
            foreach (var e in ReadEndpoints(root, flow).Where(e => e.Active).OrderBy(e => e.Name))
                yield return $"{label}: {e.Name}" + (e.HasEapo ? " [Equalizer APO]" : "") + (e.ListenEnabled ? " [Abhören → " + (e.ListenTarget ?? "?") + "]" : "");
    }

    /// <summary>Startet Windows neu (nach Rückfrage durch den Aufrufer).</summary>
    public static void RestartWindows() =>
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"RP Voice Switcher: Neustart fuer Equalizer APO\"") { UseShellExecute = false, CreateNoWindow = true });

    private static bool CanWrite(string dir)
    {
        try
        {
            // Vorhandene voice.txt nur zum Schreiben öffnen (ohne etwas zu ändern) – eine neue Datei würde
            // Equalizer APO unnötig neu laden lassen.
            string voice = Path.Combine(dir, "voice.txt");
            if (File.Exists(voice))
            {
                using (new FileStream(voice, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                return true;
            }
            string probe = Path.Combine(dir, ".rpvs-schreibtest");
            using (new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Gibt der Gruppe "Benutzer" Änderungsrechte am Config-Ordner (einmalige UAC-Abfrage).</summary>
    public static void GrantWriteAccess(string dir)
    {
        var p = Process.Start(new ProcessStartInfo("icacls.exe", $"\"{dir}\" /grant *S-1-5-32-545:(OI)(CI)M")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        p?.WaitForExit(15000);
    }

    /// <summary>
    /// Das echte Mikrofon: das Aufnahmegerät, das per "Abhören" ins Kabel weitergeleitet wird.
    /// Liefert die Geräte-ID für NAudio (oder null).
    /// </summary>
    public static string? FindMicrophoneId()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var capture = ReadEndpoints(root, "Capture");
            var cableIn = ReadEndpoints(root, "Render").FirstOrDefault(e => e.Active && e.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
            var mic = capture.FirstOrDefault(e => e.Active && !e.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                                                  && e.ListenEnabled && (cableIn == null || Contains(e.ListenTarget, cableIn.Guid)));
            return mic == null ? null : "{0.0.1.00000000}." + mic.Guid;
        }
        catch { return null; }
    }

    private static List<Endpoint> ReadEndpoints(RegistryKey root, string flow)
    {
        var list = new List<Endpoint>();
        using var key = root.OpenSubKey($@"{MMDevices}\{flow}");
        if (key == null) return list;
        using var eapoChildren = root.OpenSubKey(@"SOFTWARE\EqualizerAPO\Child APOs");
        var eapoGuids = new HashSet<string>(eapoChildren?.GetSubKeyNames() ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var guid in key.GetSubKeyNames())
        {
            try
            {
                using var dev = key.OpenSubKey(guid);
                using var props = dev?.OpenSubKey("Properties");
                if (dev == null || props == null) continue;
                bool active = dev.GetValue("DeviceState") is int state && state == 1;
                string name = $"{props.GetValue(PropDesc)} ({props.GetValue(PropInterface)})";
                string? listenTarget = props.GetValue(PropListenTarget) as string;
                bool listenEnabled = props.GetValue(PropListenEnabled) is byte[] b && b.Length >= 10 && b[8] != 0;

                bool hasEapo = eapoGuids.Contains(guid);
                if (!hasEapo)
                {
                    using var fx = dev.OpenSubKey("FxProperties");
                    hasEapo = fx?.GetValueNames().Any(n => fx.GetValue(n) is string v && v.Contains(EapoClsid, StringComparison.OrdinalIgnoreCase)) == true;
                }
                list.Add(new Endpoint(guid, name, active, listenTarget, listenEnabled, hasEapo));
            }
            catch { /* einzelne kaputte Einträge überspringen */ }
        }
        return list;
    }

    private static string? FindGraillon()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] dirs =
        [
            Path.Combine(pf, "VSTPlugins"),
            Path.Combine(pf, "Steinberg", "VSTPlugins"),
            Path.Combine(pf, "Common Files", "VST2"),
            Path.Combine(pf, "EqualizerAPO", "VSTPlugins"),
        ];
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            var hit = Directory.EnumerateFiles(dir, "*Graillon*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (hit != null) return hit;
        }
        return null;
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>0 = Wiedergabe, 1 = Aufnahme.</summary>
    public static void OpenSoundPanel(int tab) => Process.Start("control.exe", $"mmsys.cpl,,{tab}");

    public static void OpenDeviceSelector(VoiceStore store)
    {
        string dir = Path.GetDirectoryName(store.ConfigDir)!;
        string exe = Path.Combine(dir, "DeviceSelector.exe");
        if (!File.Exists(exe)) exe = Path.Combine(dir, "Configurator.exe");
        // Wie beim Editor: Qt findet seine Plugins nur mit dem Programmordner als Arbeitsverzeichnis.
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
    }
}
