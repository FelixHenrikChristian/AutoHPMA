using AutoHPMA.Core.Models;
using AutoHPMA.Models;

namespace AutoHPMA.Contracts.Services;

public interface IAutomationTaskRunner
{
    event EventHandler? StateChanged;

    AutomationTaskRunnerState CurrentState { get; }

    Task<AutomationTaskStartResult> StartAsync(
        AutomationTaskStartRequest request,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
