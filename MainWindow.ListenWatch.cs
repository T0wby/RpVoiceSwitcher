using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace RpVoiceSwitcher;

// Wächter für Windows-„Abhören“ (Mikrofon → CABLE Input): Bleibt die Weiterleitung hängen, startet die App sie neu.
// Erkennt zwei Fälle, ohne mitzuhören (nur Pegel, keine Aufnahme):
//  1. Die Weiterleitung läuft gar nicht (ihre Sitzung auf CABLE Input fehlt oder ist inaktiv), obwohl sie eingestellt ist.
//  2. Sie läuft, aber deine Stimme kommt nicht durch: Das Mikrofon hat Pegel, die Weiterleitung nicht.
public partial class MainWindow
{
    private readonly DispatcherTimer _listenTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MMDeviceEnumerator? _listenEnum;
    private MMDevice? _listenMic, _listenCable;
    private string? _listenMicId;
    private AudioSessionControl? _listenSession;
    private long _listenRefsAt, _listenSessionAt, _listenMissingSince = -1, _listenNextRepair;
    private int _listenSilentHits;
    private bool _listenRepairing, _listenGaveUp;
    private readonly Queue<DateTime> _listenRepairs = new();

    /// <summary>So lange darf die Weiterleitung fehlen/inaktiv sein, bevor neu gestartet wird.</summary>
    private const int ListenMissingMs = 4000;
    /// <summary>So viele Messungen (je 250 ms) mit Stimme am Mikrofon, aber nichts in der Weiterleitung → neu starten.</summary>
    private const int ListenSilentHits = 8;
    private const float ListenVoicePeak = 0.05f, ListenDeadPeak = 0.003f, ListenAlivePeak = 0.01f;

    private void InitListenWatch()
    {
        AutoFixListenCheck.IsChecked = _settings.AutoFixListen;
        // Testläufe arbeiten mit deinen echten Geräten – dort nur, wenn ein Test es ausdrücklich will
        if (IsTestRun && Environment.GetEnvironmentVariable("RPVS_TEST_LISTEN") != "1") return;
        _listenTimer.Tick += (_, _) => ListenTick();
        if (_settings.AutoFixListen) _listenTimer.Start();
    }

    private void AutoFixListen_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoFixListen = AutoFixListenCheck.IsChecked == true;
        _settings.Save();
        _listenGaveUp = false;
        _listenRepairs.Clear();
        if (_settings.AutoFixListen && (!IsTestRun || Environment.GetEnvironmentVariable("RPVS_TEST_LISTEN") == "1")) _listenTimer.Start();
        else _listenTimer.Stop();
        MicListen.Log(_settings.AutoFixListen ? "Wächter an" : "Wächter aus");
    }

    /// <summary>Geräte neu suchen (nach An-/Abstecken oder Änderungen in der Systemsteuerung).</summary>
    private void InvalidateListenWatch()
    {
        _listenRefsAt = 0;
        _listenSessionAt = 0;
        _listenMissingSince = -1;
        _listenSilentHits = 0;
    }

    private void ListenTick()
    {
        if (_listenRepairing || _exiting) return;
        long now = Environment.TickCount64;
        try
        {
            if (now >= _listenRefsAt) RefreshListenDevices(now);
            if (_listenMic == null || _listenCable == null || _listenMicId == null) return;

            if (now >= _listenSessionAt)
            {
                _listenSession = MicListen.FindSession(_listenCable, _listenMic.FriendlyName);
                _listenSessionAt = now + 2000;
            }

            // 1) Weiterleitung läuft nicht
            if (_listenSession == null || _listenSession.State != AudioSessionState.AudioSessionStateActive)
            {
                _listenSilentHits = 0;
                if (_listenMissingSince < 0) _listenMissingSince = now;
                else if (now - _listenMissingSince >= ListenMissingMs) RepairListen("Weiterleitung lief nicht");
                return;
            }
            _listenMissingSince = -1;

            // 2) Stimme am Mikrofon, aber nichts in der Weiterleitung
            float mic = _listenMic.AudioMeterInformation.MasterPeakValue;
            float passed = _listenSession.AudioMeterInformation.MasterPeakValue;
            if (passed > ListenAlivePeak) _listenSilentHits = 0;
            else if (mic > ListenVoicePeak && passed < ListenDeadPeak) _listenSilentHits++;
            if (_listenSilentHits >= ListenSilentHits)
                RepairListen($"Mikrofon hatte Pegel ({mic:0.00}), im Kabel kam nichts an");
        }
        catch
        {
            // Gerät weg o. Ä. – beim nächsten Mal neu suchen
            _listenRefsAt = now + 3000;
            _listenMic = _listenCable = null;
            _listenSession = null;
        }
    }

    private void RefreshListenDevices(long now)
    {
        _listenRefsAt = now + 15000;
        _listenMicId = SetupChecker.FindMicrophoneId();
        _listenEnum ??= new MMDeviceEnumerator();
        _listenMic = null; _listenCable = null; _listenSession = null; _listenSessionAt = 0;
        if (_listenMicId == null) return; // Abhören nicht eingerichtet – nichts zu bewachen
        try { _listenMic = _listenEnum.GetDevice(_listenMicId); } catch { _listenMic = null; }
        if (_listenMic?.State != DeviceState.Active) { _listenMic = null; return; }
        foreach (var d in _listenEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            if (d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) { _listenCable = d; break; }
    }

    private void RepairListen(string why)
    {
        long now = Environment.TickCount64;
        if (_listenGaveUp || now < _listenNextRepair || _listenMicId is not string id) return;
        // Hast du Abhören gerade selbst ausgeschaltet (oder ein anderes Mikrofon gewählt)? Dann nicht wieder einschalten.
        if (!string.Equals(SetupChecker.FindMicrophoneId(), id, StringComparison.OrdinalIgnoreCase))
        {
            MicListen.Log("Abhören wurde umgestellt – nichts neu gestartet");
            InvalidateListenWatch();
            return;
        }

        // Notbremse: Hilft Neustarten nicht, nicht endlos weiter unterbrechen
        var utc = DateTime.UtcNow;
        while (_listenRepairs.Count > 0 && utc - _listenRepairs.Peek() > TimeSpan.FromMinutes(3)) _listenRepairs.Dequeue();
        if (_listenRepairs.Count >= 3)
        {
            _listenGaveUp = true;
            MicListen.Log($"Gebe auf: {why} – dreimal neu gestartet, hilft nicht");
            ShowStatusMessage("Abhören hängt weiter – Mikrofon-USB einmal ab- und anstecken, dann Extras → Einrichtung.");
            return;
        }
        _listenRepairs.Enqueue(utc);
        _listenNextRepair = now + 20000;
        _listenRepairing = true;
        string mic = _listenMic?.FriendlyName ?? "Mikrofon";
        MicListen.Log($"Neustart ({mic}): {why}");
        Task.Run(() => MicListen.Restart(id)).ContinueWith(t => Dispatcher.Invoke(() =>
        {
            _listenRepairing = false;
            InvalidateListenWatch();
            _listenRefsAt = Environment.TickCount64 + 15000; // Geräte stimmen noch, nur die Sitzung neu suchen
            string? error = t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result;
            if (error != null)
            {
                MicListen.Log("Neustart fehlgeschlagen: " + error);
                ShowStatusMessage("Abhören konnte nicht neu gestartet werden: " + error);
            }
            else ShowStatusMessage("Abhören hing – automatisch neu gestartet. Deine Stimme kommt wieder im Kabel an.");
        }));
    }
}
