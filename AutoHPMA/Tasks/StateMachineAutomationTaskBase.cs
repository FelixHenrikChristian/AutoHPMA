using AutoHPMA.Core.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AutoHPMA.Tasks;

internal abstract class StateMachineAutomationTaskBase<TOptions, TState> : AutomationTaskBase<TOptions>
    where TOptions : AutomationTaskOptions
    where TState : struct, Enum
{
    private const int DefaultStateMonitorIntervalMilliseconds = 200;
    private const int MinimumStateMonitorIntervalMilliseconds = 50;
    private const int DefaultLoopDelayMilliseconds = 1000;

    private readonly object _stateGate = new();
    private Dictionary<string, Mat> _images = new(StringComparer.OrdinalIgnoreCase);
    private TState _currentState;

    protected StateMachineAutomationTaskBase(
        TOptions options,
        ILogger logger,
        TState initialState)
        : base(options, logger)
    {
        _currentState = initialState;
    }

    protected TState CurrentState
    {
        get
        {
            lock (_stateGate)
            {
                return _currentState;
            }
        }
    }

    protected abstract TState UnknownState { get; }

    protected abstract string UnknownDisplayName { get; }

    protected abstract IReadOnlyList<AutomationTaskStateRule<TState>> StateRules { get; }

    protected sealed override async Task ExecuteAsync(
        AutomationTaskContext context,
        CancellationToken cancellationToken)
    {
        SetCurrentState(UnknownState);
        await InitializeStateMachineAsync(cancellationToken);

        StartStateMonitor(
            StateRules,
            ApplyDetectedState,
            UnknownState,
            UnknownDisplayName,
            ResolveStateMonitorInterval(context));

        while (ShouldContinue(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleStateAsync(CurrentState, cancellationToken);
        }

        await OnStateMachineCompletedAsync(cancellationToken);
    }

    protected virtual Task InitializeStateMachineAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected virtual bool ShouldContinue(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested;

    protected abstract Task HandleStateAsync(TState state, CancellationToken cancellationToken);

    protected virtual Task OnStateMachineCompletedAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected virtual void OnDetectedStateChanged(TState previousState, TState currentState)
    {
        if (!EqualityComparer<TState>.Default.Equals(currentState, UnknownState))
        {
            ResetUnknownBuffer();
        }
    }

    protected void LoadTaskImages(
        string relativeDirectory,
        ImreadModes mode = ImreadModes.Color,
        bool recursive = false)
    {
        _images = LoadImagesFromDirectory(relativeDirectory, mode, recursive);
    }

    protected Mat GetImage(string name)
    {
        var key = NormalizeImageKey(name);
        return _images.TryGetValue(key, out var image)
            ? image
            : throw new KeyNotFoundException($"Task image was not loaded: {name}");
    }

    protected TemplateSearchResult FindImage(
        string name,
        TemplateSearchOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Find(GetImage(name), options, cancellationToken);

    protected Task<bool> TryClickNamedTemplateAsync(
        string name,
        double threshold = 0.9,
        CancellationToken cancellationToken = default) =>
        TryClickTemplateAsync(GetImage(name), threshold, cancellationToken);

    protected static Task DelayAsync(
        CancellationToken cancellationToken,
        int milliseconds = DefaultLoopDelayMilliseconds) =>
        Task.Delay(milliseconds, cancellationToken);

    protected static string NormalizeImageKey(string imagePath) =>
        Path.ChangeExtension(imagePath, null)?.Replace('\\', '/') ??
        imagePath.Replace('\\', '/');

    private void ApplyDetectedState(TState newState)
    {
        TState previousState;
        lock (_stateGate)
        {
            previousState = _currentState;
            _currentState = newState;
        }

        OnDetectedStateChanged(previousState, newState);
    }

    private void SetCurrentState(TState state)
    {
        lock (_stateGate)
        {
            _currentState = state;
        }
    }

    private static TimeSpan ResolveStateMonitorInterval(AutomationTaskContext context)
    {
        var interval = context.RuntimeOptions?.StateMonitorInterval ??
            DefaultStateMonitorIntervalMilliseconds;
        return TimeSpan.FromMilliseconds(
            Math.Max(interval, MinimumStateMonitorIntervalMilliseconds));
    }
}
