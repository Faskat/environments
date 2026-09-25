using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Environments.Core;

/// <summary>
/// Virtual desktops through the system shortcuts (Win+Ctrl+D, Win+Ctrl+F4). Windows has no public API to create
/// or remove desktops, and the internal COM interfaces change between builds, so the shortcuts are the stable way.
/// </summary>
public static class VirtualDesktop
{
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_F4 = 0x73, VK_D = 0x44;

    /// <summary>Creates a desktop and switches to it. New windows then open there.</summary>
    public static async Task CreateAndSwitch()
    {
        await WaitForModifiersUp();
        Chord(VK_LWIN, VK_CONTROL, VK_D);
        // Let the switch animation finish, or new windows may land on the old desktop.
        await Task.Delay(700);
    }

    /// <summary>Closes the current desktop; its windows move to the neighbouring one.</summary>
    public static async Task CloseCurrent()
    {
        await WaitForModifiersUp();
        Chord(VK_LWIN, VK_CONTROL, VK_F4);
    }

    /// <summary>A preset started by Ctrl+Alt+N arrives while those keys are still down; Win+Ctrl+D would become Win+Ctrl+Alt+D.</summary>
    static async Task WaitForModifiersUp()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && IsDown(VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN))
            await Task.Delay(30);
    }

    static bool IsDown(params int[] keys)
    {
        foreach (var k in keys)
            if ((GetAsyncKeyState(k) & 0x8000) != 0) return true;
        return false;
    }

    static void Chord(params int[] keys)
    {
        var inputs = new INPUT[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = Key(keys[i], up: false);
            inputs[inputs.Length - 1 - i] = Key(keys[i], up: true);
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | ExtendedFlag(vk) } },
    };

    // Win keys are extended keys; without the flag some layouts ignore the chord.
    static uint ExtendedFlag(int vk) => vk is VK_LWIN or VK_RWIN ? KEYEVENTF_EXTENDEDKEY : 0;

    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);
}
