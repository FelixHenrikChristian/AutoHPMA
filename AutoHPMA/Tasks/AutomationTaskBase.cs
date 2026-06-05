using AutoHPMA.Contracts.Tasks;
using AutoHPMA.Core.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AutoHPMA.Tasks;

public abstract class AutomationTaskBase<TOptions> : IAutomationTask
    where TOptions : AutomationTaskOptions
{
    private static readonly TimeSpan DefaultStateMonitorInterval = TimeSpan.FromMilliseconds(200);

    private readonly List<IDisposable> _ownedResources = [];
    private CancellationTokenSource? _stateMonitorCts;
    private Task? _stateMonitorTask;
    private DateTimeOffset? _unknownStateSince;

    protected AutomationTaskBase(TOptions options, ILogger logger)
    {
        Options = options;
        Logger = logger;
    }

    public abstract AutomationTaskType TaskType { get; }

    public abstract string DisplayName { get; }

    protected TOptions Options { get; }

    protected ILogger Logger { get; }

    protected AutomationTaskContext Context { get; private set; } = null!;

    protected CancellationToken TaskCancellationToken { get; private set; }

    protected int UnknownBufferMilliseconds { get; set; } = 3000;

    public async Task RunAsync(AutomationTaskContext context, CancellationToken cancellationToken)
    {
        Context = context;
        TaskCancellationToken = cancellationToken;

        Context.Overlay.SetGameState(DisplayName);
        Logger.LogInformation("=== [Cyan]{TaskName}[/Cyan] 任务已启动 ===", DisplayName);

        try
        {
            await ExecuteAsync(context, cancellationToken);
        }
        finally
        {
            await StopStateMonitorAsync();
            Context.Overlay.ClearTaskStateRegions();
            Context.Overlay.ClearStateIndicatorRegions();
            Context.Overlay.SetGameState("空闲");
            DisposeOwnedResources();
            Logger.LogInformation("=== [Cyan]{TaskName}[/Cyan] 任务已停止 ===", DisplayName);
        }
    }

    protected abstract Task ExecuteAsync(
        AutomationTaskContext context,
        CancellationToken cancellationToken);

    protected async Task RunLoopAsync(
        Func<CancellationToken, Task> executeLoopAsync,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await executeLoopAsync(cancellationToken);
        }
    }

    protected Mat LoadImage(string relativePath, ImreadModes mode = ImreadModes.Color)
    {
        var absolutePath = Path.Combine(AppContext.BaseDirectory, relativePath);
        var mat = Cv2.ImRead(absolutePath, mode);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new FileNotFoundException($"Task image could not be loaded: {relativePath}", absolutePath);
        }

        _ownedResources.Add(mat);
        return mat;
    }

    protected Dictionary<string, Mat> LoadImagesFromDirectory(
        string relativeDirectory,
        ImreadModes mode = ImreadModes.Color,
        bool recursive = false)
    {
        var absoluteDirectory = Path.Combine(AppContext.BaseDirectory, relativeDirectory);
        if (!Directory.Exists(absoluteDirectory))
        {
            throw new DirectoryNotFoundException($"Task image directory was not found: {absoluteDirectory}");
        }

        var images = new Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(absoluteDirectory, "*.png", searchOption))
        {
            var key = recursive
                ? Path.ChangeExtension(Path.GetRelativePath(absoluteDirectory, file), null)?.Replace("\\", "/")
                : Path.GetFileNameWithoutExtension(file);

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var mat = Cv2.ImRead(file, mode);
            if (mat.Empty())
            {
                mat.Dispose();
                continue;
            }

            images[key] = mat;
            _ownedResources.Add(mat);
        }

        return images;
    }

    protected TemplateSearchResult Find(
        Mat template,
        TemplateSearchOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Context.SearchCurrentFrame(
            template,
            options,
            cancellationToken == default ? TaskCancellationToken : cancellationToken);

    protected async Task<bool> TryClickTemplateAsync(
        Mat template,
        double threshold = 0.9,
        CancellationToken cancellationToken = default)
    {
        var token = cancellationToken == default ? TaskCancellationToken : cancellationToken;
        var result = Find(template, new TemplateSearchOptions { Threshold = threshold }, token);
        if (!result.Success || result.FirstRegion is not { } region)
        {
            return false;
        }

        ShowMatchRegions(result);
        await Context.ClickMatchCenterAsync(region, token);
        return true;
    }

    protected void ShowMatchRegions(TemplateSearchResult result, int durationMs = 500)
    {
        if (result.Success)
        {
            Context.Overlay.AddTemporaryRegions(Context.ToOverlayRegions(result.Regions), durationMs);
        }
    }

    protected void SetTaskStateRegions(
        IEnumerable<TemplateMatchRegion> regions,
        string? name = null)
    {
        Context.Overlay.SetTaskStateRegions(Context.ToOverlayRegions(regions, name));
    }

    protected void ClearTaskStateRegions() =>
        Context.Overlay.ClearTaskStateRegions();

    protected void StartStateMonitor<TState>(
        IReadOnlyList<AutomationTaskStateRule<TState>> rules,
        Action<TState> onStateDetected,
        TState defaultState,
        string defaultDisplayName,
        TimeSpan? interval = null)
        where TState : struct
    {
        if (_stateMonitorTask is not null)
        {
            return;
        }

        _stateMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(TaskCancellationToken);
        var token = _stateMonitorCts.Token;
        _stateMonitorTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    onStateDetected(FindStateByRules(rules, defaultState, defaultDisplayName, token));
                    await Task.Delay(interval ?? DefaultStateMonitorInterval, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "任务状态监测失败。");
                }
            }
        }, token);
    }

    protected bool IsUnknownBufferElapsed()
    {
        if (_unknownStateSince is null)
        {
            _unknownStateSince = DateTimeOffset.UtcNow;
            return false;
        }

        return DateTimeOffset.UtcNow - _unknownStateSince.Value >=
            TimeSpan.FromMilliseconds(UnknownBufferMilliseconds);
    }

    protected void ResetUnknownBuffer() => _unknownStateSince = null;

    private TState FindStateByRules<TState>(
        IReadOnlyList<AutomationTaskStateRule<TState>> rules,
        TState defaultState,
        string defaultDisplayName,
        CancellationToken cancellationToken)
        where TState : struct
    {
        foreach (var rule in rules)
        {
            foreach (var template in rule.Templates)
            {
                var result = Find(
                    template,
                    new TemplateSearchOptions { Threshold = rule.Threshold },
                    cancellationToken);
                if (!result.Success)
                {
                    continue;
                }

                Context.Overlay.SetStateIndicatorRegions(Context.ToOverlayRegions(result.Regions));
                Context.Overlay.SetGameState(rule.DisplayName);
                return rule.State;
            }
        }

        Context.Overlay.ClearStateIndicatorRegions();
        Context.Overlay.SetGameState(defaultDisplayName);
        return defaultState;
    }

    private async Task StopStateMonitorAsync()
    {
        if (_stateMonitorTask is null)
        {
            return;
        }

        _stateMonitorCts?.Cancel();
        try
        {
            await _stateMonitorTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // The monitor is best-effort; task shutdown should not be blocked by it.
        }
        finally
        {
            _stateMonitorCts?.Dispose();
            _stateMonitorCts = null;
            _stateMonitorTask = null;
        }
    }

    private void DisposeOwnedResources()
    {
        foreach (var resource in _ownedResources)
        {
            resource.Dispose();
        }

        _ownedResources.Clear();
    }
}
