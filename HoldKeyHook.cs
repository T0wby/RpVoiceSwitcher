using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace RpVoiceSwitcher;

/// <summary>
/// Systemweiter Tastatur-Hook für Halte-Tasten: Taste runter = Aktion starten, Taste hoch = beenden.
/// RegisterHotKey kennt kein "Loslassen", deshalb ein Low-Level-Hook (wie ihn z. B. Discord für Push-to-Talk nutzt).
/// Gehaltene Tasten werden geschluckt, damit sie nicht zusätzlich im Spiel ankommen.
/// </summary>
public sealed class HoldKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public int vkCode; public int scanCode; public int flags; public int time; public IntPtr extra; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private readonly LowLevelKeyboardProc _proc; // Referenz halten, sonst räumt der GC den Callback weg
    private readonly Dispatcher _dispatcher;
    private readonly List<(Hotkey Key, Action Down, Action Up)> _bindings = new();
    private IntPtr _hook;
    private int _heldVk = -1;
    private Action? _heldUp;

    /// <summary>Während der Tastenaufnahme in der App soll der Hook nichts auslösen.</summary>
    public bool Suspended { get; set; }

    public HoldKeyHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookProc;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Clear()
    {
        ReleaseHeld();
        _bindings.Clear();
    }

    /// <summary>Registriert eine Halte-Taste (inkl. Gegenstück auf der anderen Zahlenreihe).</summary>
    public void Add(Hotkey key, Action down, Action up)
    {
        _bindings.Add((key, down, up));
        if (key.NumberAlias is Hotkey alias) _bindings.Add((alias, down, up));
    }

    /// <summary>Falls die App z. B. das Profil wechselt, während eine Taste gehalten wird.</summary>
    public void ReleaseHeld()
    {
        if (_heldUp == null) return;
        var up = _heldUp;
        _heldUp = null;
        _heldVk = -1;
        up();
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !Suspended)
        {
            int msg = wParam.ToInt32();
            int vk = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;

            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (vk == _heldVk) return (IntPtr)1; // Tasten-Wiederholung schlucken

                var key = KeyInterop.KeyFromVirtualKey(vk);
                var mods = CurrentModifiers();
                foreach (var b in _bindings)
                {
                    if (b.Key.Key != key || b.Key.Modifiers != mods) continue;
                    // Callback schnell beenden (Windows bricht langsame Hooks ab) – Arbeit an die UI übergeben.
                    var previousUp = _heldUp;
                    _heldVk = vk;
                    _heldUp = b.Up;
                    _dispatcher.BeginInvoke(() =>
                    {
                        previousUp?.Invoke();
                        b.Down();
                    });
                    return (IntPtr)1;
                }
            }
            else if ((msg is WM_KEYUP or WM_SYSKEYUP) && vk == _heldVk)
            {
                var up = _heldUp;
                _heldVk = -1;
                _heldUp = null;
                if (up != null) _dispatcher.BeginInvoke(up);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static ModifierKeys CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        var m = ModifierKeys.None;
        if (Down(VK_CONTROL)) m |= ModifierKeys.Control;
        if (Down(VK_MENU)) m |= ModifierKeys.Alt;
        if (Down(VK_SHIFT)) m |= ModifierKeys.Shift;
        if (Down(VK_LWIN) || Down(VK_RWIN)) m |= ModifierKeys.Windows;
        return m;
    }

    public void Dispose()
    {
        _bindings.Clear();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
