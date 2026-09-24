using System.Runtime.InteropServices;

namespace DispCtrl.App.Services;

/// <summary>
/// Opens Windows' own shell flyouts.
/// </summary>
/// <remarks>
/// There is no API for these panes. They are keyboard shortcuts the shell owns,
/// so the only way to raise one is to send the shortcut — which is exactly what
/// is wanted here: the real Cast pane, with Windows' own discovery and pairing,
/// rather than a reimplementation that could leave a device half-paired.
/// </remarks>
public static partial class ShellFlyout
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;

        // The union is as large as its biggest member (MOUSEINPUT). Declaring
        // only the keyboard member would understate the struct size and make
        // SendInput reject the whole array.
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    private const ushort VkLWin = 0x5B;
    private const ushort VkK = 0x4B;
    private const ushort VkP = 0x50;

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] INPUT[] inputs, int size);

    /// <summary>Opens the Cast pane — the same one Win+K raises.</summary>
    public static bool OpenCast() => SendChord(VkLWin, VkK);
    public static bool OpenProject() => SendChord(VkLWin, VkP);

    private static bool SendChord(ushort modifier, ushort key)
    {
        INPUT[] inputs =
        [
            Key(modifier, down: true),
            Key(key, down: true),
            Key(key, down: false),
            Key(modifier, down: false),
        ];

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT Key(ushort vk, bool down) => new()
    {
        type = InputKeyboard,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = down ? 0 : KeyEventKeyUp,
            },
        },
    };
}
