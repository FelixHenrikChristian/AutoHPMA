using AutoHPMA.GameTask;
using AutoHPMA.Services.Interface;
using Microsoft.Extensions.Logging;

namespace AutoHPMA.Services;

public class GameTaskManager : IGameTaskManager
{
    private readonly ILogger<GameTaskManager> _logger;
    private readonly object _syncRoot = new();
    private IGameTask? _currentTask;

    public GameTaskManager(ILogger<GameTaskManager> logger)
    {
        _logger = logger;
    }

    public bool IsTaskRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentTask != null;
            }
        }
    }

    public IGameTask? CurrentTask
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentTask;
            }
        }
    }

    public event EventHandler? TaskStarted;

    public event EventHandler? TaskStopped;

    public bool TryStartTask(Func<IGameTask> createTask, out string? errorMessage)
    {
        lock (_syncRoot)
        {
            if (_currentTask != null)
            {
                errorMessage = "已有其他任务正在运行，请先停止当前任务。";
                return false;
            }

            try
            {
                var task = createTask();
                task.TaskCompleted += OnTaskCompleted;
                _currentTask = task;
                _currentTask.Start();
                errorMessage = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动任务失败");
                errorMessage = ex.Message;
                _currentTask = null;
                return false;
            }
        }

        TaskStarted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void StopCurrentTask()
    {
        IGameTask? taskToStop;

        lock (_syncRoot)
        {
            taskToStop = _currentTask;
        }

        if (taskToStop == null)
        {
            return;
        }

        try
        {
            taskToStop.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止任务失败");
        }
    }

    private void OnTaskCompleted(object? sender, EventArgs e)
    {
        IGameTask? completedTask = null;

        lock (_syncRoot)
        {
            if (sender is IGameTask task && ReferenceEquals(task, _currentTask))
            {
                completedTask = _currentTask;
                _currentTask = null;
            }
        }

        if (completedTask == null)
        {
            return;
        }

        try
        {
            completedTask.TaskCompleted -= OnTaskCompleted;
            completedTask.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "任务完成后清理失败");
        }

        TaskStopped?.Invoke(this, EventArgs.Empty);
    }
}
