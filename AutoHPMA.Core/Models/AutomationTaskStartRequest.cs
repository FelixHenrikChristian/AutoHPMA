namespace AutoHPMA.Core.Models;

public sealed record AutomationTaskStartRequest(
    AutomationTaskType TaskType,
    AutomationTaskOptions Options);
