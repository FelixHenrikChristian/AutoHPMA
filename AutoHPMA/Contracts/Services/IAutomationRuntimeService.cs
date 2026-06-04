using AutoHPMA.Capture.Models;
using AutoHPMA.Models;

namespace AutoHPMA.Contracts.Services;

public interface IAutomationRuntimeService
{
    event EventHandler? StateChanged;

    bool IsRunning { get; }

    GameWindowTarget? CurrentTarget { get; }

    AutomationRuntimeOptions? CurrentOptions { get; }

    Task<AutomationRuntimeStartResult> StartAsync(
        AutomationRuntimeOptions options,
        CancellationToken cancellationToken = default);

    void Stop();

    bool IsGameWindow(IntPtr hWnd);

    CapturedFrame? TryGetFrame();
}
