using System;

namespace AutoHPMA.Helpers.Hotkey;

/// <summary>
/// Modifier keys for a hotkey combination. Values match the flags expected by
/// <c>RegisterHotKey</c> (MOD_ALT/CONTROL/SHIFT/WIN), which makes interop trivial.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None    = 0x0000,
    Alt     = 0x0001,
    Control = 0x0002,
    Shift   = 0x0004,
    Win     = 0x0008,
}
