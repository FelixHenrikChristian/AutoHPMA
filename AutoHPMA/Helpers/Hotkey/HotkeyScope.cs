namespace AutoHPMA.Helpers.Hotkey;

/// <summary>
/// Determines when a hotkey is allowed to fire.
/// </summary>
public enum HotkeyScope
{
    /// <summary>
    /// System-wide hotkey registered via <c>RegisterHotKey</c>. Other applications
    /// (including the game itself) will NOT receive the key while it is registered.
    /// Best for keys that should never be typed normally (e.g. F11, Ctrl+Alt+...).
    /// </summary>
    Global,

    /// <summary>
    /// Only fires while the foreground window matches the game-window predicate
    /// (low-level keyboard hook). Other applications keep working normally.
    /// </summary>
    GameWindow,
}
