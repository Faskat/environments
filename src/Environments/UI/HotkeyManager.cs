using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using Environments.Core;

namespace Environments.UI;

public static class Hotkey
{
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8;

    public static string Format(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(key - Key.NumPad0),
        _ => key.ToString()
    };

    static bool TryParseKey(string s, out Key key)
    {
        key = Key.None;
        if (s.Length == 1 && char.IsDigit(s[0])) { key = Key.D0 + (s[0] - '0'); return true; }
        if (s.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && s.Length == 4 && char.IsDigit(s[3]))
        {
            key = Key.NumPad0 + (s[3] - '0');
            return true;
        }
        return Enum.TryParse(s, true, out key) && key != Key.None;
    }

    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: return false;
            }
        }
        if (!TryParseKey(parts[^1], out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
}

/// <summary>Global hotkeys via RegisterHotKey on a hidden message-only window.</summary>
public sealed class HotkeyManager : IDisposable
{
    const uint MOD_NOREPEAT = 0x4000;
    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 1;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("EnvironmentsHotkeys")
        {
            Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3)
        });
        _source.AddHook(Hook);
    }

    public bool Register(string hotkey, Action action)
    {
        if (!Hotkey.TryParse(hotkey, out var mods, out var vk)) return false;
        int id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, vk)) return false;
        _actions[id] = action;
        return true;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys.ToList()) Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.Dispose();
    }
}
