using AutoHPMA.Contracts.Services;
using AutoHPMA.Contracts.Tasks;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using AutoHPMA.Models;
using AutoHPMA.Tasks;
using Microsoft.Extensions.Logging;

namespace AutoHPMA.Services;

public sealed class AutomationTaskRunner : IAutomationTaskRunner, IDisposable
{
    private readonly IAutomationRuntimeService _runtime;
    private readonly IOverlayWindowService _overlay;
    private readonly IWindowInteractionService _windowInteraction;
    private readonly ITemplateMatchingService _templateMatching;
    private readonly IReadOnlyDictionary<AutomationTaskType, IAutomationTaskFactory> _factories;
    private readonly ILogger<AutomationTaskRunner> _logger;
    private readonly object _gate = new();

    private ActiveTask? _activeTask;
    private AutomationTaskRunnerState _currentState = AutomationTaskRunnerState.Idle;

    public AutomationTaskRunner(
        IAutomationRuntimeService runtime,
        IOverlayWindowService overlay,
        IWindowInteractionService windowInteraction,
        ITemplateMatchingService templateMatching,
        IEnumerable<IAutomationTaskFactory> factories,
        ILogger<AutomationTaskRunner> logger)
    {
        _runtime = runtime;
        _overlay = overlay;
        _windowInteraction = windowInteraction;
        _templateMatching = templateMatching;
        _factories = factories.ToDictionary(factory => factory.TaskType);
        _logger = logger;

        _runtime.StateChanged += OnRuntimeStateChanged;
    }

    public event EventHandler? StateChanged;

    public AutomationTaskRunnerState CurrentState
    {
        get
        {
            lock (_gate)
            {
                return _currentState;
            }
        }
    }

    public Task<AutomationTaskStartResult> StartAsync(
        AutomationTaskStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_activeTask is not null)
            {
                return Task.FromResult(AutomationTaskStartResult.AlreadyRunning(_activeTask.AutomationTask.DisplayName));
            }
        }

        if (!_runtime.IsRunning || _runtime.CurrentTarget is null)
        {
            return Task.FromResult(AutomationTaskStartResult.RuntimeNotStarted(request.TaskType));
        }

        if (!_factories.TryGetValue(request.TaskType, out var factory))
        {
            return Task.FromResult(AutomationTaskStartResult.TaskNotRegistered(request.TaskType));
        }

        try
        {
            var task = factory.Create(request.Options);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var context = new AutomationTaskContext(
                _runtime,
                _overlay,
                _windowInteraction,
                _templateMatching,
                _runtime.CurrentTarget,
                _runtime.CurrentOptions);
            var activeTask = new ActiveTask(task, linkedCts);

            lock (_gate)
            {
                if (_activeTask is not null)
                {
                    linkedCts.Dispose();
                    return Task.FromResult(AutomationTaskStartResult.AlreadyRunning(_activeTask.AutomationTask.DisplayName));
                }

                _activeTask = activeTask;
                _currentState = new AutomationTaskRunnerState(
                    true,
                    task.TaskType,
                    task.DisplayName,
                    DateTimeOffset.Now);
            }

            activeTask.ExecutionTask = RunActiveTaskAsync(activeTask, context);
            RaiseStateChanged();
            return Task.FromResult(AutomationTaskStartResult.Success(task.TaskType, task.DisplayName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start automation task {TaskType}.", request.TaskType);
            return Task.FromResult(AutomationTaskStartResult.Failure(
                request.TaskType,
                "任务启动失败",
                ex.Message,
                ex));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ActiveTask? activeTask;
        lock (_gate)
        {
            activeTask = _activeTask;
        }

        if (activeTask is null)
        {
            return;
        }

        activeTask.Cancellation.Cancel();
        try
        {
            await activeTask.ExecutionTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Automation task stop observed a task exception.");
        }
    }

    public void Dispose()
    {
        _runtime.StateChanged -= OnRuntimeStateChanged;

        lock (_gate)
        {
            _activeTask?.Cancellation.Cancel();
        }
    }

    private async Task RunActiveTaskAsync(ActiveTask activeTask, AutomationTaskContext context)
    {
        try
        {
            await activeTask.AutomationTask.RunAsync(context, activeTask.Cancellation.Token);
        }
        catch (OperationCanceledException) when (activeTask.Cancellation.IsCancellationRequested)
        {
            // Normal stop path.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation task {TaskName} failed.", activeTask.AutomationTask.DisplayName);
        }
        finally
        {
            var shouldRaiseStateChanged = false;
            activeTask.Cancellation.Dispose();

            lock (_gate)
            {
                if (ReferenceEquals(_activeTask, activeTask))
                {
                    _activeTask = null;
                    _currentState = AutomationTaskRunnerState.Idle;
                    shouldRaiseStateChanged = true;
                }
            }

            if (shouldRaiseStateChanged)
            {
                RaiseStateChanged();
            }
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (_runtime.IsRunning)
        {
            return;
        }

        lock (_gate)
        {
            _activeTask?.Cancellation.Cancel();
        }
    }

    private sealed class ActiveTask
    {
        public ActiveTask(IAutomationTask task, CancellationTokenSource cancellation)
        {
            AutomationTask = task;
            Cancellation = cancellation;
        }

        public IAutomationTask AutomationTask { get; }

        public CancellationTokenSource Cancellation { get; }

        public Task ExecutionTask { get; set; } = System.Threading.Tasks.Task.CompletedTask;
    }
}
