using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace Fpe2001Remake.Automation;

/// <summary>
/// Win32 输入自动化 P/Invoke：RegisterHotKey、SendInput（键盘/鼠标）。
/// </summary>
internal static class NativeInput
{
    // ---------- RegisterHotKey ----------

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    /// <summary>"Ctrl+Shift+F12" 等 → (modifiers, vk)。支持 Alt/Ctrl/Shift/Win 组合 + 单键名。</summary>
    internal static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        var parts = hotkey.Split('+');
        foreach (var part in parts)
        {
            var p = part.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "cmd" or "windows": modifiers |= MOD_WIN; break;
                default:
                    var key = ParseKey(p);
                    if (key == Key.None)
                    {
                        return false;
                    }
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    private static Key ParseKey(string name) => name.ToLowerInvariant() switch
    {
        "f1" => Key.F1, "f2" => Key.F2, "f3" => Key.F3, "f4" => Key.F4,
        "f5" => Key.F5, "f6" => Key.F6, "f7" => Key.F7, "f8" => Key.F8,
        "f9" => Key.F9, "f10" => Key.F10, "f11" => Key.F11, "f12" => Key.F12,
        "a" => Key.A, "b" => Key.B, "c" => Key.C, "d" => Key.D, "e" => Key.E,
        "f" => Key.F, "g" => Key.G, "h" => Key.H, "i" => Key.I, "j" => Key.J,
        "k" => Key.K, "l" => Key.L, "m" => Key.M, "n" => Key.N, "o" => Key.O,
        "p" => Key.P, "q" => Key.Q, "r" => Key.R, "s" => Key.S, "t" => Key.T,
        "u" => Key.U, "v" => Key.V, "w" => Key.W, "x" => Key.X, "y" => Key.Y, "z" => Key.Z,
        "0" => Key.D0, "1" => Key.D1, "2" => Key.D2, "3" => Key.D3, "4" => Key.D4,
        "5" => Key.D5, "6" => Key.D6, "7" => Key.D7, "8" => Key.D8, "9" => Key.D9,
        "esc" => Key.Escape, "enter" => Key.Enter, "space" => Key.Space,
        "tab" => Key.Tab, "backspace" => Key.Back,
        "up" => Key.Up, "down" => Key.Down, "left" => Key.Left, "right" => Key.Right,
        "home" => Key.Home, "end" => Key.End, "pageup" => Key.PageUp, "pagedown" => Key.PageDown,
        "delete" => Key.Delete, "insert" => Key.Insert,
        _ => Key.None,
    };

    // ---------- SendInput ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint INPUT_MOUSE = 0;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    internal static void SendKey(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = down ? 0 : KEYEVENTF_KEYUP,
                },
            },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    internal static void SendMouseClick(bool right, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = right
                        ? (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP)
                        : (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP),
                },
            },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }
}
