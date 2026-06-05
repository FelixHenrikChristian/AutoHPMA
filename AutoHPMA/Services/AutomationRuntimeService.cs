using AutoHPMA.Capture;
using AutoHPMA.Capture.Models;
using AutoHPMA.Capture.Native;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Services;

public sealed class AutomationRuntimeService : IAutomationRuntimeService, IDisposable
{
    private const int MinimumMonitorIntervalMs = 50;
    private static readonly TimeSpan DefaultMonitorInterval = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<IGameWindowProvider> _windowProviders;
    private readonly IHotkeyService _hotkeyService;
    private readonly IOverlayWindowService _overlayWindowService;
    private readonly IInfoBarNotificationService _infoBar;
    private readonly ILogger<AutomationRuntimeService> _logger;
    private readonly object _gate = new();

    private WindowsGraphicsCapture? _capture;
    private DispatcherQueueTimer? _monitorTimer;

    public AutomationRuntimeService(
        IEnumerable<IGameWindowProvider> windowProviders,
        IHotkeyService hotkeyService,
        IOverlayWindowService overlayWindowService,
        IInfoBarNotificationService infoBar,
        ILogger<AutomationRuntimeService> logger)
    {
        _windowProviders = windowProviders.ToArray();
        _hotkeyService = hotkeyService;
        _overlayWindowService = overlayWindowService;
        _infoBar = infoBar;
        _logger = logger;
    }

    public event EventHandler? StateChanged;

    public bool IsRunning { get; private set; }

    public GameWindowTarget? CurrentTarget { get; private set; }

    public AutomationRuntimeOptions? CurrentOptions { get; private set; }

    public Task<AutomationRuntimeStartResult> StartAsync(
        AutomationRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsRunning)
            {
                return Task.FromResult(AutomationRuntimeStartResult.AlreadyRunning(CurrentTarget));
            }

            if (!WindowsGraphicsCapture.IsSupported)
            {
                return Task.FromResult(AutomationRuntimeStartResult.Failure(
                    "启动失败",
                    "当前系统不支持 Windows Graphics Capture，无法启动截图器。"));
            }

            var target = LocateTarget();
            if (target is null)
            {
                return Task.FromResult(AutomationRuntimeStartResult.Failure(
                    "未找到游戏窗口",
                    "请先启动游戏后再试。"));
            }

            try
            {
                var capture = new WindowsGraphicsCapture();
                capture.Start(target.CaptureHandle);

                _capture = capture;
                CurrentTarget = target;
                CurrentOptions = options;
                IsRunning = true;

                _hotkeyService.GameWindowPredicate = IsGameWindow;
                TryBringTargetToForeground(target);
                _overlayWindowService.Start(target, options);
                StartMonitorTimer(options.StateMonitorInterval);

                _logger.LogInformation(
                    "Automation runtime started. Provider={Provider}, Client={Client}, DisplayHwnd=0x{DisplayHwnd:X}, GameHwnd=0x{GameHwnd:X}",
                    target.ProviderName,
                    target.ClientKind,
                    target.DisplayWindow.Handle.ToInt64(),
                    target.GameWindow.Handle.ToInt64());

                RaiseStateChanged();
                return Task.FromResult(AutomationRuntimeStartResult.Success(target));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动自动化运行时失败");
                StopCore();

                return Task.FromResult(AutomationRuntimeStartResult.Failure(
                    "启动失败",
                    $"截图器启动失败：{ex.Message}",
                    ex));
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public bool IsGameWindow(IntPtr hWnd)
    {
        var target = CurrentTarget;
        return hWnd != IntPtr.Zero &&
            target is not null &&
            (hWnd == target.DisplayWindow.Handle || hWnd == target.GameWindow.Handle);
    }

    public CapturedFrame? TryGetFrame() => _capture?.TryGetFrame();

    public void Dispose()
    {
        Stop();
        _overlayWindowService.Dispose();
        _monitorTimer = null;
    }

    private GameWindowTarget? LocateTarget()
    {
        foreach (var provider in _windowProviders)
        {
            try
            {
                var target = provider.TryLocate();
                if (target is not null)
                {
                    return target;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "游戏窗口检测失败：{Provider}", provider.Name);
            }
        }

        return null;
    }

    private void StartMonitorTimer(int intervalMs)
    {
        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        _monitorTimer ??= dispatcherQueue.CreateTimer();
        _monitorTimer.Interval = intervalMs > 0
            ? TimeSpan.FromMilliseconds(Math.Max(MinimumMonitorIntervalMs, intervalMs))
            : DefaultMonitorInterval;
        _monitorTimer.IsRepeating = true;
        _monitorTimer.Tick -= OnMonitorTimerTick;
        _monitorTimer.Tick += OnMonitorTimerTick;
        _monitorTimer.Start();
    }

    private void OnMonitorTimerTick(DispatcherQueueTimer sender, object args)
    {
        lock (_gate)
        {
            if (!IsRunning || CurrentTarget is null)
            {
                return;
            }

            if (IsTargetAlive(CurrentTarget) && (_capture?.IsCapturing ?? false))
            {
                _overlayWindowService.Refresh(CurrentTarget);
                return;
            }

            var targetName = CurrentTarget.DisplayName;
            StopCore();

            _infoBar.Show(
                InfoBarSeverity.Warning,
                "游戏窗口已关闭",
                $"检测到 {targetName} 已不可用，已停止截图器。");
        }
    }

    private void StopCore()
    {
        var wasRunning = IsRunning;

        if (_monitorTimer is not null)
        {
            _monitorTimer.Stop();
            _monitorTimer.Tick -= OnMonitorTimerTick;
        }

        _capture?.Dispose();
        _capture = null;

        _overlayWindowService.Stop();

        CurrentTarget = null;
        CurrentOptions = null;
        IsRunning = false;
        _hotkeyService.GameWindowPredicate = null;

        if (wasRunning)
        {
            _logger.LogInformation("Automation runtime stopped.");
            RaiseStateChanged();
        }
    }

    private static bool IsTargetAlive(GameWindowTarget target) =>
        NativeMethods.IsWindow(target.DisplayWindow.Handle) &&
        NativeMethods.IsWindow(target.GameWindow.Handle);

    private static void TryBringTargetToForeground(GameWindowTarget target)
    {
        var hWnd = target.ForegroundHandle;
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return;
        }

        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }
        else
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
        }

        NativeMethods.SetForegroundWindow(hWnd);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
