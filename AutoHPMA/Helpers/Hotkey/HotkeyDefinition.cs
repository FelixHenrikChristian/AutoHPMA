using System;
using System.Collections.Generic;
using System.Text;

namespace AutoHPMA.Helpers.Hotkey;

/// <summary>
/// Immutable description of a hotkey: a single non-modifier virtual key plus
/// any combination of modifiers. <see cref="VirtualKey"/> uses standard Win32
/// VK_* codes.
/// </summary>
public readonly record struct HotkeyDefinition(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static HotkeyDefinition Empty => default;

    public bool IsEmpty => VirtualKey == 0;

    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if ((Modifiers & HotkeyModifiers.Control) != 0) sb.Append("Ctrl+");
        if ((Modifiers & HotkeyModifiers.Alt)     != 0) sb.Append("Alt+");
        if ((Modifiers & HotkeyModifiers.Shift)   != 0) sb.Append("Shift+");
        if ((Modifiers & HotkeyModifiers.Win)     != 0) sb.Append("Win+");
        sb.Append(KeyName(VirtualKey));
        return sb.ToString();
    }

    public static bool TryParse(string? text, out HotkeyDefinition definition)
    {
        definition = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var mods = HotkeyModifiers.None;
        uint vk = 0;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.ToUpperInvariant();
            switch (part)
            {
                case "CTRL":
                case "CONTROL":
                    mods |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                case "MENU":
                    mods |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    mods |= HotkeyModifiers.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                case "LWIN":
                case "RWIN":
                    mods |= HotkeyModifiers.Win;
                    break;
                default:
                    if (vk != 0)
                    {
                        return false; // multiple non-modifier keys
                    }
                    if (!TryParseKey(part, out vk))
                    {
                        return false;
                    }
                    break;
            }
        }

        if (vk == 0)
        {
            return false;
        }

        definition = new HotkeyDefinition(mods, vk);
        return true;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        if (KeyNameToVk.TryGetValue(token, out vk))
        {
            return true;
        }
        // Single letter / digit
        if (token.Length == 1)
        {
            var c = token[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }
        // F1 - F24
        if (token.Length >= 2 && token[0] == 'F' && uint.TryParse(token.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            vk = 0x6F + n; // VK_F1 = 0x70
            return true;
        }
        return false;
    }

    private static string KeyName(uint vk)
    {
        if (VkToKeyName.TryGetValue(vk, out var name))
        {
            return name;
        }
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)vk).ToString();
        }
        if (vk is >= 0x70 and <= 0x87) // F1..F24
        {
            return "F" + (vk - 0x6F);
        }
        return $"VK_{vk:X2}";
    }

    // Friendly names for non-alphanumeric keys we want to support.
    private static readonly Dictionary<string, uint> KeyNameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BACKSPACE"] = 0x08, ["BACK"] = 0x08,
        ["TAB"]       = 0x09,
        ["ENTER"]     = 0x0D, ["RETURN"] = 0x0D,
        ["ESC"]       = 0x1B, ["ESCAPE"] = 0x1B,
        ["SPACE"]     = 0x20,
        ["PAGEUP"]    = 0x21, ["PRIOR"] = 0x21,
        ["PAGEDOWN"]  = 0x22, ["NEXT"]  = 0x22,
        ["END"]       = 0x23,
        ["HOME"]      = 0x24,
        ["LEFT"]      = 0x25,
        ["UP"]        = 0x26,
        ["RIGHT"]     = 0x27,
        ["DOWN"]      = 0x28,
        ["INSERT"]    = 0x2D, ["INS"]    = 0x2D,
        ["DELETE"]    = 0x2E, ["DEL"]    = 0x2E,
        ["NUMLOCK"]   = 0x90,
        ["SCROLL"]    = 0x91, ["SCROLLLOCK"] = 0x91,
        ["CAPSLOCK"]  = 0x14, ["CAPITAL"]    = 0x14,
        ["OEM_PLUS"]  = 0xBB, ["="] = 0xBB,
        ["OEM_MINUS"] = 0xBD, ["-"] = 0xBD,
        ["OEM_COMMA"] = 0xBC, [","] = 0xBC,
        ["OEM_PERIOD"]= 0xBE, ["."] = 0xBE,
        ["OEM_2"]     = 0xBF, ["/"] = 0xBF,
        ["OEM_3"]     = 0xC0, ["`"] = 0xC0,
        ["OEM_4"]     = 0xDB, ["["] = 0xDB,
        ["OEM_5"]     = 0xDC, ["\\"]= 0xDC,
        ["OEM_6"]     = 0xDD, ["]"] = 0xDD,
        ["OEM_7"]     = 0xDE, ["'"] = 0xDE,
    };

    private static readonly Dictionary<uint, string> VkToKeyName = BuildReverse();

    private static Dictionary<uint, string> BuildReverse()
    {
        var d = new Dictionary<uint, string>();
        // Prefer human names ("Enter" over "Return", "Esc" over "Escape", etc.)
        foreach (var preferred in new (string Name, uint Vk)[]
        {
            ("Backspace", 0x08), ("Tab", 0x09), ("Enter", 0x0D), ("Esc", 0x1B),
            ("Space", 0x20), ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23),
            ("Home", 0x24), ("Left", 0x25), ("Up", 0x26), ("Right", 0x27),
            ("Down", 0x28), ("Insert", 0x2D), ("Delete", 0x2E),
            ("CapsLock", 0x14), ("NumLock", 0x90), ("ScrollLock", 0x91),
            ("=", 0xBB), ("-", 0xBD), (",", 0xBC), (".", 0xBE),
            ("/", 0xBF), ("`", 0xC0), ("[", 0xDB), ("\\", 0xDC),
            ("]", 0xDD), ("'", 0xDE),
        })
        {
            d[preferred.Vk] = preferred.Name;
        }
        return d;
    }
}
