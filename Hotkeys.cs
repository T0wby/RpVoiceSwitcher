using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace RpVoiceSwitcher;

public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public string Serialize() => $"{Modifiers}|{Key}";

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('|');
        if (parts.Length != 2) return null;
        if (!Enum.TryParse(parts[0], out ModifierKeys mods)) return null;
        if (!Enum.TryParse(parts[1], out Key key) || key == Key.None) return null;
        return new Hotkey(mods, key);
    }

    /// <summary>Gegenstück auf der anderen Zahlenreihe (obere Reihe ↔ Ziffernblock), sonst null.</summary>
    public Hotkey? NumberAlias => Modifiers == ModifierKeys.None ? null : Key switch
    {
        >= Key.D0 and <= Key.D9 => this with { Key = Key.NumPad0 + (Key - Key.D0) },
        >= Key.NumPad0 and <= Key.NumPad9 => this with { Key = Key.D0 + (Key - Key.NumPad0) },
        _ => null,
    };

    /// <summary>Ohne Modifier sind nur F-Tasten und der Ziffernblock erlaubt, sonst wäre z. B. "W" systemweit blockiert.</summary>
    public bool IsSafeWithoutModifier =>
        Key is >= Key.F1 and <= Key.F24 or >= Key.NumPad0 and <= Key.Divide;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Strg");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Umschalt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num ,",
        _ => key.ToString(),
    };
}

/// <summary>Systemweite Hotkeys über RegisterHotKey – funktionieren auch, wenn ein Spiel im Vordergrund ist.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x5000;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    public bool Register(Hotkey hotkey, Action action)
    {
        uint mods = MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, mods, vk))
            return false;

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
