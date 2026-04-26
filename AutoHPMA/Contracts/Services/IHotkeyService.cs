using System;
using AutoHPMA.Helpers.Hotkey;

namespace AutoHPMA.Contracts.Services;

/// <summary>
/// Application-wide hotkey registry.
/// <para>
/// Two scopes are supported:
/// <list type="bullet">
///   <item><see cref="HotkeyScope.Global"/> — registered with Win32 <c>RegisterHotKey</c>.
///   The key combination is consumed system-wide.</item>
///   <item><see cref="HotkeyScope.GameWindow"/> — only fires when
///   <see cref="GameWindowPredicate"/> returns true for the current foreground window.
///   Other applications keep working normally.</item>
/// </list>
/// </para>
/// <para>
/// Handlers are invoked on a worker thread. Marshal back to the UI thread inside
/// the handler if you need to touch the UI (e.g. via <c>DispatcherQueue.TryEnqueue</c>).
/// </para>
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Predicate used by <see cref="HotkeyScope.GameWindow"/> hotkeys to decide whether
    /// the current foreground window belongs to the target game. If left null, scoped
    /// hotkeys never fire.
    /// </summary>
    Func<IntPtr, bool>? GameWindowPredicate { get; set; }

    /// <summary>
    /// Registers a hotkey. Returns an opaque id used to unregister later. Throws if
    /// <paramref name="definition"/> is empty or a global registration conflicts.
    /// </summary>
    /// <param name="consumeKey">For <see cref="HotkeyScope.GameWindow"/> only:
    /// when true (default), the key event is swallowed and not delivered to the game.
    /// Ignored for <see cref="HotkeyScope.Global"/> (always consumed by Windows).</param>
    Guid Register(HotkeyDefinition definition, HotkeyScope scope, Action handler, bool consumeKey = true);

    /// <summary>Unregisters a previously registered hotkey. Safe to call with an unknown id.</summary>
    bool Unregister(Guid registrationId);

    /// <summary>Starts the hotkey thread. Idempotent.</summary>
    void Start();

    /// <summary>Stops the hotkey thread and releases all native resources.</summary>
    void Stop();
}
