using AutoHPMA.GameTask;

namespace AutoHPMA.Services.Interface;

public interface IGameTaskManager
{
    bool IsTaskRunning { get; }

    IGameTask? CurrentTask { get; }

    event EventHandler? TaskStarted;

    event EventHandler? TaskStopped;

    bool TryStartTask(Func<IGameTask> createTask, out string? errorMessage);

    void StopCurrentTask();
}
