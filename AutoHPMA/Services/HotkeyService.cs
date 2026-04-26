using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers.Hotkey;
using Microsoft.Extensions.Logging;
using static AutoHPMA.Helpers.Hotkey.HotkeyNativeMethods;

namespace AutoHPMA.Services;

/// <summary>
/// Owns a dedicated background thread that:
/// <list type="number">
///   <item>Runs a Win32 message loop.</item>
///   <item>Hosts thread-targeted <c>RegisterHotKey</c> registrations (global scope).</item>
///   <item>Hosts a single <c>WH_KEYBOARD_LL</c> hook used for game-window-scoped hotkeys.</item>
/// </list>
/// External calls (<see cref="Register"/>/<see cref="Unregister"/>) are marshalled onto
/// that thread via <c>PostThreadMessage</c> so all native handles stay thread-affine.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ILogger<HotkeyService> _logger;
    private readonly object _stateLock = new();

    // Keep the delegate alive for as long as the hook is installed.
    private LowLevelKeyboardProc? _hookProcDelegate;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hookHandle = IntPtr.Zero;
    private volatile bool _running;

    // All registrations, keyed by the public id we hand back to callers.
    private readonly ConcurrentDictionary<Guid, Registration> _registrations = new();

    // Marshalled work for the hotkey thread.
    private readonly ConcurrentQueue<Action> _pendingWork = new();

    // Monotonic id passed to RegisterHotKey. WM_HOTKEY arrives with this in wParam.
    private int _nextNativeId;
    private readonly Dictionary<int, Registration> _nativeIdToRegistration = new();

    // Tracks whether a GameWindow hotkey is currently held down so we only fire once
    // per press (Windows auto-repeats keydowns when held).
    private readonly HashSet<Guid> _heldDown = new();

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
    }

    public Func<IntPtr, bool>? GameWindowPredicate { get; set; }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_running)
            {
                return;
            }
            _running = true;

            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => ThreadMain(ready))
            {
                IsBackground = true,
                Name = "AutoHPMA.HotkeyThread",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();
            _logger.LogInformation("HotkeyService started on thread {ThreadId}", _threadId);
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint tid;
        lock (_stateLock)
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            thread = _thread;
            tid = _threadId;
            _thread = null;
            _threadId = 0;
        }

        if (tid != 0)
        {
            PostThreadMessage(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        thread?.Join(TimeSpan.FromSeconds(2));
        _logger.LogInformation("HotkeyService stopped");
    }

    public Guid Register(HotkeyDefinition definition, HotkeyScope scope, Action handler, bool consumeKey = true)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        if (definition.IsEmpty) throw new ArgumentException("Hotkey definition is empty.", nameof(definition));

        Start(); // lazy

        var reg = new Registration(Guid.NewGuid(), definition, scope, handler, consumeKey);
        _registrations[reg.Id] = reg;

        InvokeOnHotkeyThread(() => ApplyRegistration(reg));
        return reg.Id;
    }

    public bool Unregister(Guid registrationId)
    {
        if (!_registrations.TryRemove(registrationId, out var reg))
        {
            return false;
        }
        InvokeOnHotkeyThread(() => RemoveRegistration(reg));
        return true;
    }

    public void Dispose()
    {
        foreach (var reg in _registrations.Values)
        {
            InvokeOnHotkeyThread(() => RemoveRegistration(reg));
        }
        _registrations.Clear();
        Stop();
    }

    // ---- thread internals --------------------------------------------------

    private void InvokeOnHotkeyThread(Action action)
    {
        var tid = _threadId;
        if (tid == 0)
        {
            // Service is not running; execute synchronously to surface errors.
            action();
            return;
        }

        if (GetCurrentThreadId() == tid)
        {
            action();
            return;
        }

        _pendingWork.Enqueue(action);
        PostThreadMessage(tid, WM_APP_INVOKE, IntPtr.Zero, IntPtr.Zero);
    }

    private void ThreadMain(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();

        // Force the thread message queue to exist before we signal readiness, otherwise
        // an early PostThreadMessage from another thread could fail with ERROR_INVALID_THREAD_ID.
        PeekMessage(out _, IntPtr.Zero, WM_APP_INVOKE, WM_APP_INVOKE, PM_NOREMOVE);

        _hookProcDelegate = LowLevelKeyboardHook;
        _hookHandle = SetWindowsHookExW(
            WH_KEYBOARD_LL,
            _hookProcDelegate,
            GetModuleHandle(null),
            0);
        if (_hookHandle == IntPtr.Zero)
        {
            _logger.LogError("Failed to install low-level keyboard hook (Win32 error {Error})",
                Marshal.GetLastWin32Error());
        }

        ready.Set();

        while (true)
        {
            int rc = GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (rc <= 0)
            {
                break; // WM_QUIT or error
            }

            if (msg.message == WM_HOTKEY)
            {
                int id = msg.wParam.ToInt32();
                Registration? reg;
                lock (_nativeIdToRegistration)
                {
                    _nativeIdToRegistration.TryGetValue(id, out reg);
                }
                if (reg is not null)
                {
                    InvokeHandler(reg);
                }
            }
            else if (msg.message == WM_APP_INVOKE)
            {
                while (_pendingWork.TryDequeue(out var work))
                {
                    try { work(); }
                    catch (Exception ex) { _logger.LogError(ex, "Hotkey thread work failed"); }
                }
            }
            else
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        // Cleanup on this thread.
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hookProcDelegate = null;

        lock (_nativeIdToRegistration)
        {
            foreach (var id in _nativeIdToRegistration.Keys)
            {
                UnregisterHotKey(IntPtr.Zero, id);
            }
            _nativeIdToRegistration.Clear();
        }
    }

    // Must be called on the hotkey thread.
    private void ApplyRegistration(Registration reg)
    {
        if (reg.Scope == HotkeyScope.Global)
        {
            int id = ++_nextNativeId;
            if (!RegisterHotKey(IntPtr.Zero, id, (uint)reg.Definition.Modifiers, reg.Definition.VirtualKey))
            {
                _logger.LogWarning("RegisterHotKey failed for {Hotkey} (Win32 error {Error}). Removing.",
                    reg.Definition, Marshal.GetLastWin32Error());
                _registrations.TryRemove(reg.Id, out _);
                return;
            }
            reg.NativeId = id;
            lock (_nativeIdToRegistration)
            {
                _nativeIdToRegistration[id] = reg;
            }
            _logger.LogInformation("Registered global hotkey {Hotkey}", reg.Definition);
        }
        else
        {
            // GameWindow scope is purely driven by the LL hook lookup over _registrations.
            _logger.LogInformation("Registered scoped hotkey {Hotkey} (consume={Consume})",
                reg.Definition, reg.ConsumeKey);
        }
    }

    private void RemoveRegistration(Registration reg)
    {
        if (reg.Scope == HotkeyScope.Global && reg.NativeId != 0)
        {
            UnregisterHotKey(IntPtr.Zero, reg.NativeId);
            lock (_nativeIdToRegistration)
            {
                _nativeIdToRegistration.Remove(reg.NativeId);
            }
        }
        _heldDown.Remove(reg.Id);
    }

    // ---- low-level hook ----------------------------------------------------

    private IntPtr LowLevelKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _registrations.IsEmpty)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        int msg = wParam.ToInt32();
        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool isUp   = msg == WM_KEYUP   || msg == WM_SYSKEYUP;
        if (!isDown && !isUp)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var vk = data.vkCode;

        // Skip pure modifier keys — they only make sense as part of a combination.
        if (IsModifierVk(vk))
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // Only resolve foreground/predicate when we actually have scoped registrations.
        bool foregroundIsGame = false;
        bool foregroundResolved = false;

        bool consumed = false;
        var currentMods = ReadModifierState();

        foreach (var reg in _registrations.Values)
        {
            if (reg.Scope != HotkeyScope.GameWindow) continue;
            if (reg.Definition.VirtualKey != vk) continue;
            if (reg.Definition.Modifiers != currentMods) continue;

            if (!foregroundResolved)
            {
                var fg = GetForegroundWindow();
                foregroundIsGame = GameWindowPredicate?.Invoke(fg) ?? false;
                foregroundResolved = true;
            }
            if (!foregroundIsGame) continue;

            if (isDown)
            {
                if (_heldDown.Add(reg.Id))
                {
                    InvokeHandler(reg);
                }
            }
            else
            {
                _heldDown.Remove(reg.Id);
            }

            if (reg.ConsumeKey)
            {
                consumed = true;
            }
        }

        if (consumed)
        {
            // Returning non-zero stops further processing (game/app won't see the key).
            return new IntPtr(1);
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsModifierVk(uint vk) =>
        vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
            or 0xA0 /*LSHIFT*/ or 0xA1 /*RSHIFT*/
            or 0xA2 /*LCTRL*/  or 0xA3 /*RCTRL*/
            or 0xA4 /*LALT*/   or 0xA5 /*RALT*/;

    private static HotkeyModifiers ReadModifierState()
    {
        var m = HotkeyModifiers.None;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) m |= HotkeyModifiers.Control;
        if ((GetAsyncKeyState(VK_MENU)    & 0x8000) != 0) m |= HotkeyModifiers.Alt;
        if ((GetAsyncKeyState(VK_SHIFT)   & 0x8000) != 0) m |= HotkeyModifiers.Shift;
        if (((GetAsyncKeyState(VK_LWIN) | GetAsyncKeyState(VK_RWIN)) & 0x8000) != 0)
            m |= HotkeyModifiers.Win;
        return m;
    }

    private void InvokeHandler(Registration reg)
    {
        // Run off the hook/message thread so a slow handler can't stall the LL hook
        // (Windows will silently uninstall hooks that exceed LowLevelHooksTimeout).
        Task.Run(() =>
        {
            try { reg.Handler(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hotkey handler threw for {Hotkey}", reg.Definition);
            }
        });
    }

    private sealed class Registration
    {
        public Registration(Guid id, HotkeyDefinition def, HotkeyScope scope, Action handler, bool consumeKey)
        {
            Id = id;
            Definition = def;
            Scope = scope;
            Handler = handler;
            ConsumeKey = consumeKey;
        }
        public Guid Id { get; }
        public HotkeyDefinition Definition { get; }
        public HotkeyScope Scope { get; }
        public Action Handler { get; }
        public bool ConsumeKey { get; }
        public int NativeId { get; set; }
    }
}
