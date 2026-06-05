using AutoHPMA.Core.Models;
using AutoHPMA.Tasks;

namespace AutoHPMA.Contracts.Tasks;

public interface IAutomationTask
{
    AutomationTaskType TaskType { get; }

    string DisplayName { get; }

    Task RunAsync(AutomationTaskContext context, CancellationToken cancellationToken);
}
