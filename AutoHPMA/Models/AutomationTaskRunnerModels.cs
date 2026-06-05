using AutoHPMA.Core.Models;

namespace AutoHPMA.Models;

public sealed record AutomationTaskRunnerState(
    bool IsRunning,
    AutomationTaskType CurrentTaskType,
    string? CurrentTaskName,
    DateTimeOffset? StartedAt)
{
    public static AutomationTaskRunnerState Idle { get; } =
        new(false, AutomationTaskType.None, null, null);
}

public sealed class AutomationTaskStartResult
{
    private AutomationTaskStartResult(
        bool succeeded,
        AutomationTaskType taskType,
        string title,
        string message,
        Exception? exception = null)
    {
        Succeeded = succeeded;
        TaskType = taskType;
        Title = title;
        Message = message;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public AutomationTaskType TaskType { get; }

    public string Title { get; }

    public string Message { get; }

    public Exception? Exception { get; }

    public static AutomationTaskStartResult Success(AutomationTaskType taskType, string taskName) =>
        new(true, taskType, "启动成功", $"{taskName} 已启动。");

    public static AutomationTaskStartResult AlreadyRunning(string taskName) =>
        new(false, AutomationTaskType.None, "已有任务正在运行", $"请先停止当前任务：{taskName}。");

    public static AutomationTaskStartResult RuntimeNotStarted(AutomationTaskType taskType) =>
        new(false, taskType, "任务启动失败", "请先在首页启动 AutoHPMA 截图器。");

    public static AutomationTaskStartResult TaskNotRegistered(AutomationTaskType taskType) =>
        new(false, taskType, "任务尚未接入", "任务运行框架已就位，但该任务的具体实现还没有注册。");

    public static AutomationTaskStartResult Failure(
        AutomationTaskType taskType,
        string title,
        string message,
        Exception? exception = null) =>
        new(false, taskType, title, message, exception);
}
