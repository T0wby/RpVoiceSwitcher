using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace RpVoiceSwitcher;

/// <summary>
/// Meldet, wenn Audiogeräte an-/abgesteckt werden oder sich das Standardgerät ändert.
/// Windows ruft das aus einem Hintergrund-Thread auf – der Empfänger muss selbst auf die UI wechseln.
/// </summary>
public sealed class DeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Action _changed;
    private bool _registered;

    public DeviceWatcher(Action changed)
    {
        _changed = changed;
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
            _registered = true;
        }
        catch { /* ohne Benachrichtigung geht's auch – dann eben per "Erneut prüfen" */ }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _changed();
    public void OnDeviceAdded(string pwstrDeviceId) => _changed();
    public void OnDeviceRemoved(string deviceId) => _changed();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _changed();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { /* zu häufig, uninteressant */ }

    public void Dispose()
    {
        if (_registered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            _registered = false;
        }
        _enumerator.Dispose();
    }
}
