using AutoHPMA.Capture.Native;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Models;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Views.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace AutoHPMA.Services;

public sealed class OverlayWindowService : IOverlayWindowService
{
    private static readonly TimeSpan HideGracePeriod = TimeSpan.FromMilliseconds(350);

    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger<OverlayWindowService> _logger;
    private readonly object _gate = new();

    private LogOverlayWindow? _logWindow;
    private MaskOverlayWindow? _maskWindow;
    private GameWindowTarget? _target;
    private bool _overlaysVisible;
    private DateTimeOffset? _hideCandidateSince;

    public OverlayWindowService(ILogger<OverlayWindowService> logger)
    {
        _dispatcher = App.MainWindow.DispatcherQueue;
        _logger = logger;
    }

    public void Start(GameWindowTarget target, AutomationRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        RunOnUiThread(() =>
        {
            StopCore();
            _target = target;

            if (options.LogWindowEnabled)
            {
                _logWindow = new LogOverlayWindow
                {
                    ShowDebugLogs = options.ShowDebugLogs,
                    ShowMarquee = options.LogWindowMarqueeEnabled,
                };
                _logWindow.LoadSnapshot();
                _logWindow.RefreshPosition(target.GameWindow.Handle);
            }

            if (options.MaskWindowEnabled)
            {
                _maskWindow = new MaskOverlayWindow
                {
                    ShowTextLabels = options.MaskWindowShowTextLabels,
                };
                _maskWindow.RefreshPosition(target.CaptureHandle);
            }

            RefreshCore(target);
        });
    }

    public void Refresh(GameWindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        RunOnUiThread(() => RefreshCore(target));
    }

    public void Stop() => RunOnUiThread(StopCore);

    public void SetGameState(string state) =>
        RunOnUiThread(() => _logWindow?.SetGameState(state));

    public void AddTemporaryRegion(OverlayRegion region, int durationMs = 500) =>
        RunOnUiThread(() => _maskWindow?.AddTemporaryRegion(region, durationMs));

    public void AddTemporaryRegions(IReadOnlyList<OverlayRegion> regions, int durationMs = 500) =>
        RunOnUiThread(() => _maskWindow?.AddTemporaryRegions(regions, durationMs));

    public void SetStateIndicatorRegions(IReadOnlyList<OverlayRegion> regions) =>
        RunOnUiThread(() => _maskWindow?.SetStateIndicatorRegions(regions));

    public void ClearStateIndicatorRegions() =>
        RunOnUiThread(() => _maskWindow?.ClearStateIndicatorRegions());

    public void SetTaskStateRegions(IReadOnlyList<OverlayRegion> regions) =>
        RunOnUiThread(() => _maskWindow?.SetTaskStateRegions(regions));

    public void ClearTaskStateRegions() =>
        RunOnUiThread(() => _maskWindow?.ClearTaskStateRegions());

    public void ClearMask() =>
        RunOnUiThread(() => _maskWindow?.ClearAll());

    public void Dispose() => Stop();

    private void RefreshCore(GameWindowTarget target)
    {
        lock (_gate)
        {
            _target = target;
        }

        var shouldHide = ShouldHide(target, out var hideImmediately);
        if (shouldHide)
        {
            if (hideImmediately || IsHideGracePeriodElapsed())
            {
                SetOverlayVisibilityCore(false);
            }

            return;
        }

        _hideCandidateSince = null;

        if (_logWindow is not null)
        {
            _logWindow.RefreshPosition(target.GameWindow.Handle);
        }

        if (_maskWindow is not null)
        {
            _maskWindow.RefreshPosition(target.CaptureHandle);
        }

        SetOverlayVisibilityCore(true);
    }

    private bool ShouldHide(GameWindowTarget target, out bool hideImmediately)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.IsIconic(target.ForegroundHandle))
        {
            hideImmediately = true;
            return true;
        }

        hideImmediately = false;

        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (IsOverlayWindow(foreground))
        {
            return false;
        }

        if (foreground == target.DisplayWindow.Handle || foreground == target.GameWindow.Handle)
        {
            return false;
        }

        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        var displayRoot = NativeMethods.GetAncestor(target.DisplayWindow.Handle, NativeMethods.GA_ROOT);
        var gameRoot = NativeMethods.GetAncestor(target.GameWindow.Handle, NativeMethods.GA_ROOT);
        if (foregroundRoot == displayRoot || foregroundRoot == gameRoot)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        return foregroundProcessId != target.DisplayWindow.ProcessId;
    }

    private bool IsHideGracePeriodElapsed()
    {
        if (!_overlaysVisible)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        _hideCandidateSince ??= now;
        return now - _hideCandidateSince >= HideGracePeriod;
    }

    private void SetOverlayVisibilityCore(bool visible)
    {
        if (_overlaysVisible == visible)
        {
            return;
        }

        if (visible)
        {
            if (_maskWindow is not null)
            {
                OverlayWindowHelper.ShowNoActivate(_maskWindow);
            }

            if (_logWindow is not null)
            {
                OverlayWindowHelper.ShowNoActivate(_logWindow);
                _logWindow.BringOverlayToFront();
            }
        }
        else
        {
            if (_logWindow is not null)
            {
                OverlayWindowHelper.HideOverlay(_logWindow);
            }

            if (_maskWindow is not null)
            {
                OverlayWindowHelper.HideOverlay(_maskWindow);
            }
        }

        _overlaysVisible = visible;
    }

    private bool IsOverlayWindow(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        return IsSameWindowOrRoot(foreground, foregroundRoot, OverlayWindowHelper.GetWindowHandle(_logWindow)) ||
            IsSameWindowOrRoot(foreground, foregroundRoot, OverlayWindowHelper.GetWindowHandle(_maskWindow));
    }

    private static bool IsSameWindowOrRoot(IntPtr foreground, IntPtr foregroundRoot, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return foreground == hwnd ||
            foregroundRoot == hwnd ||
            foregroundRoot == NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
    }

    private void StopCore()
    {
        try
        {
            _logWindow?.Close();
            _maskWindow?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Closing overlay windows failed.");
        }
        finally
        {
            _logWindow = null;
            _maskWindow = null;
            _target = null;
            _overlaysVisible = false;
            _hideCandidateSince = null;
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(() => action());
    }
}
