using AutoHPMA.Core.Models;

namespace AutoHPMA.Contracts.Tasks;

public interface IAutomationTaskFactory
{
    AutomationTaskType TaskType { get; }

    IAutomationTask Create(AutomationTaskOptions options);
}
