using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RpVoiceSwitcher;

/// <summary>
/// Drückt die Sprachtaste (Push-to-Talk) des Spiels, solange ein Sound läuft – sonst überträgt der Sprachchat
/// den Sound nicht. Gesendet wird über SendInput mit Scancode (so erkennen es auch Spiele, die Tasten "roh" lesen).
/// Unterstützt Tastaturtasten und die Maus-Seitentasten (Maus 4/5).
/// </summary>
public sealed class PushToTalk : IDisposable
{
    /// <summary>Markierung an unseren eigenen Eingaben – der Halte-Tasten-Hook ignoriert sie.</summary>
    public static readonly IntPtr InjectedMarker = new(0x52505653); // "RPVS"

    public readonly record struct Binding(bool IsMouse, Key Key, int MouseButton)
    {
        public string Serialize() => IsMouse ? $"Mouse|{MouseButton}" : $"Key|{Key}";

        public static Binding? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text.Split('|');
            if (parts.Length != 2) return null;
            if (parts[0] == "Mouse" && int.TryParse(parts[1], out int b) && b is 1 or 2) return new Binding(true, Key.None, b);
            if (parts[0] == "Key" && Enum.TryParse(parts[1], out Key k) && k != Key.None) return new Binding(false, k, 0);
            return null;
        }

        public override string ToString() => IsMouse
            ? (MouseButton == 1 ? "Maus 4 (Seitentaste hinten)" : "Maus 5 (Seitentaste vorne)")
            : Key switch
            {
                Key.LeftCtrl => "Strg links",
                Key.RightCtrl => "Strg rechts",
                Key.LeftShift => "Umschalt links",
                Key.RightShift => "Umschalt rechts",
                Key.LeftAlt => "Alt",
                Key.RightAlt => "Alt Gr",
                Key.CapsLock => "Feststelltaste",
                Key.Space => "Leertaste",
                Key.Tab => "Tab",
                Key.Enter => "Eingabe",
                Key.Oem3 => "Ö",
                Key.Oem7 => "Ä",
                Key.Oem1 => "Ü",
                Key.Oem5 => "^",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemMinus => "-",
                Key.OemPlus => "+",
                Key.Oem102 => "<",
                Key.Oem2 => "#",
                Key.Oem4 => "ß",
                _ => new Hotkey(ModifierKeys.None, Key).ToString(),
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;
    private const uint MOUSEEVENTF_XDOWN = 0x80, MOUSEEVENTF_XUP = 0x100;

    private Binding? _held;

    public bool IsHeld => _held != null;

    /// <summary>Taste runter (nichts tun, wenn schon gedrückt).</summary>
    public void Press(Binding binding)
    {
        if (_held != null) return;
        WriteMarker(binding);
        Send(binding, down: true);
        _held = binding;
        _repressPending = _keepDownBroken = false;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Hat jemand die Taste losgelassen, während wir sie halten (z. B. selbst kurz gedrückt und wieder los)?
    /// Dann sofort wieder drücken. Liefert true, wenn neu gedrückt wurde.
    /// </summary>
    public bool KeepDown()
    {
        if (_held is not Binding b || _keepDownBroken) return false;
        int vk = b.IsMouse ? (b.MouseButton == 1 ? 0x05 : 0x06) : KeyInterop.VirtualKeyFromKey(b.Key);
        if ((GetAsyncKeyState(vk) & 0x8000) != 0) { _repressPending = false; return false; }
        if (_repressPending)
        {
            // Schon neu gedrückt, aber Windows meldet weiter "oben" (z. B. Spiel läuft als Administrator) –
            // dann nicht dauernd weiterdrücken, bis zum nächsten Drücken aufgeben.
            _keepDownBroken = true;
            return false;
        }
        Send(b, down: true);
        _repressPending = true;
        return true;
    }

    private bool _repressPending, _keepDownBroken;

    /// <summary>Taste wieder los – nur wenn wir sie gedrückt haben.</summary>
    public void Release()
    {
        if (_held is not Binding b) return;
        _held = null;
        Send(b, down: false);
        DeleteMarker();
    }

    // ---------- Absicherung: Taste nie "hängen" lassen ----------
    // Wird die App hart beendet (Absturz, Task-Manager), während sie die Taste hält, bleibt sie in Windows gedrückt –
    // bei einer Maustaste lässt sich dann nichts mehr anklicken. Deshalb merkt sich eine kleine Datei, was gedrückt ist;
    // beim nächsten Start wird es losgelassen.

    /// <summary>Datei, in der steht, welche Taste die App gerade hält (null = nicht merken).</summary>
    public static string? MarkerFile { get; set; }

    private static void WriteMarker(Binding b)
    {
        if (MarkerFile == null) return;
        try { System.IO.File.WriteAllText(MarkerFile, b.Serialize()); } catch { /* nur Absicherung */ }
    }

    private static void DeleteMarker()
    {
        if (MarkerFile == null) return;
        try { System.IO.File.Delete(MarkerFile); } catch { /* nur Absicherung */ }
    }

    /// <summary>Hängt noch eine Taste vom letzten Mal? Dann jetzt loslassen. Liefert die Taste oder null.</summary>
    public static Binding? ReleaseLeftover()
    {
        if (MarkerFile == null || !System.IO.File.Exists(MarkerFile)) return null;
        Binding? b = null;
        try { b = Binding.Parse(System.IO.File.ReadAllText(MarkerFile).Trim()); } catch { }
        if (b is Binding binding) Send(binding, down: false);
        DeleteMarker();
        return b;
    }

    /// <summary>Notbremse beim Beenden des Prozesses (auch aus anderen Threads aufrufbar).</summary>
    public void ReleaseOnExit()
    {
        var b = _held;
        _held = null;
        if (b is Binding binding) { Send(binding, down: false); DeleteMarker(); }
    }

    private static void Send(Binding b, bool down)
    {
        var input = new INPUT();
        if (b.IsMouse)
        {
            input.type = INPUT_MOUSE;
            input.u.mi = new MOUSEINPUT
            {
                mouseData = (uint)b.MouseButton,
                dwFlags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
                dwExtraInfo = InjectedMarker,
            };
        }
        else
        {
            int vk = KeyInterop.VirtualKeyFromKey(b.Key);
            uint flags = KEYEVENTF_SCANCODE | (down ? 0 : KEYEVENTF_KEYUP);
            if (IsExtended(b.Key)) flags |= KEYEVENTF_EXTENDEDKEY;
            input.type = INPUT_KEYBOARD;
            input.u.ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = (ushort)MapVirtualKey((uint)vk, 0), // MAPVK_VK_TO_VSC
                dwFlags = flags,
                dwExtraInfo = InjectedMarker,
            };
        }
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    /// <summary>Tasten, die Windows als "erweitert" führt – ohne die Markierung käme z. B. statt "Pfeil hoch" "Num 8" an.</summary>
    private static bool IsExtended(Key k) => k is Key.RightCtrl or Key.RightAlt or Key.Insert or Key.Delete or Key.Home or Key.End
        or Key.PageUp or Key.PageDown or Key.Left or Key.Right or Key.Up or Key.Down or Key.NumLock or Key.Divide
        or Key.PrintScreen or Key.LWin or Key.RWin or Key.Apps;

    public void Dispose() => Release();
}
