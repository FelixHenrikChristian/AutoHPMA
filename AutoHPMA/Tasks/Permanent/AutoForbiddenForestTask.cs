using AutoHPMA.Contracts.Tasks;
using AutoHPMA.Core.Models;
using AutoHPMA.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AutoHPMA.Tasks.Permanent;

internal enum ForbiddenForestState
{
    Unknown,
    Teaming,
    Loading,
    Fighting,
    Summary,
}

public sealed class AutoForbiddenForestTask : AutomationTaskBase<ForbiddenForestTaskOptions>
{
    private const string ImageDirectory = "Assets/Tasks/ForbiddenForest/Image";
    private static readonly double[] StateIconScaleFactors = [1d, 0.98d, 1.02d, 0.95d, 1.05d];
    private static readonly double[] LargeStateScaleFactors = [1d, 0.98d, 1.02d];
    private static readonly TemplateSearchOptions StateIconSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = StateIconScaleFactors,
    };
    private static readonly TemplateSearchOptions LargeStateSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = LargeStateScaleFactors,
    };

    private readonly Dictionary<string, Mat> _images;
    private readonly IReadOnlyList<AutomationTaskStateRule<ForbiddenForestState>> _stateRules;
    private int _stateValue = (int)ForbiddenForestState.Unknown;
    private int _round;

    public AutoForbiddenForestTask(
        ForbiddenForestTaskOptions options,
        ILogger<AutoForbiddenForestTask> logger)
        : base(options, logger)
    {
        _images = LoadImagesFromDirectory(ImageDirectory, ImreadModes.Unchanged);
        _stateRules =
        [
            new([GetImage("ui_explore")], ForbiddenForestState.Teaming, "禁林-组队中", SearchOptions: StateIconSearchOptions),
            new([GetImage("ui_loading")], ForbiddenForestState.Loading, "禁林-加载中", SearchOptions: LargeStateSearchOptions),
            new([GetImage("ui_clock")], ForbiddenForestState.Fighting, "禁林-战斗中", SearchOptions: StateIconSearchOptions),
            new([GetImage("ui_statistics")], ForbiddenForestState.Summary, "禁林-结算中", SearchOptions: StateIconSearchOptions),
        ];

        UnknownBufferMilliseconds = 5000;
    }

    public override AutomationTaskType TaskType => AutomationTaskType.AutoForbiddenForest;

    public override string DisplayName => "禁林";

    private ForbiddenForestState CurrentState
    {
        get => (ForbiddenForestState)Volatile.Read(ref _stateValue);
        set => Volatile.Write(ref _stateValue, (int)value);
    }

    protected override async Task ExecuteAsync(
        AutomationTaskContext context,
        CancellationToken cancellationToken)
    {
        CurrentState = ForbiddenForestState.Unknown;
        _round = 0;

        var monitorInterval = TimeSpan.FromMilliseconds(
            Math.Max(context.RuntimeOptions?.StateMonitorInterval ?? 200, 50));
        StartStateMonitor(
            _stateRules,
            OnStateDetected,
            ForbiddenForestState.Unknown,
            "禁林-未知状态",
            monitorInterval);

        while (!cancellationToken.IsCancellationRequested && _round < Options.Times)
        {
            await ExecuteLoopAsync(cancellationToken);
        }

        if (_round >= Options.Times)
        {
            AppNotificationHelper.Show("禁林任务完成", $"已完成 {_round} 轮禁林任务。");
            Logger.LogInformation(
                "[Cyan]禁林[/Cyan] 任务完成：共完成 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮。",
                _round,
                Options.Times);
        }
    }

    private async Task ExecuteLoopAsync(CancellationToken cancellationToken)
    {
        switch (CurrentState)
        {
            case ForbiddenForestState.Unknown:
                if (!IsUnknownBufferElapsed())
                {
                    await Task.Delay(1000, cancellationToken);
                    return;
                }

                await Task.Delay(1000, cancellationToken);
                break;

            case ForbiddenForestState.Teaming:
                if (await TryClickNamedTemplateAsync("team_auto", cancellationToken: cancellationToken))
                {
                    Logger.LogDebug("点击禁林自动战斗按钮。");
                }

                await Task.Delay(1000, cancellationToken);

                if (Options.IsLeader)
                {
                    if (await TryClickNamedTemplateAsync("team_start", cancellationToken: cancellationToken))
                    {
                        Logger.LogDebug("点击禁林开始按钮。");
                    }

                    await Task.Delay(1500, cancellationToken);

                    if (await TryClickNamedTemplateAsync("team_confirm", cancellationToken: cancellationToken))
                    {
                        Logger.LogDebug("确认禁林队伍出发。");
                    }
                }
                else
                {
                    if (await TryClickNamedTemplateAsync("team_ready", cancellationToken: cancellationToken))
                    {
                        Logger.LogDebug("点击禁林准备按钮。");
                    }

                    await Task.Delay(1000, cancellationToken);
                }

                break;

            case ForbiddenForestState.Loading:
                await Task.Delay(1000, cancellationToken);
                break;

            case ForbiddenForestState.Fighting:
                var fightResult = Find(
                    GetImage("fight_auto"),
                    new TemplateSearchOptions
                    {
                        UseAlphaMask = true,
                        Threshold = 0.8,
                    },
                    cancellationToken);

                if (fightResult.FirstRegion is { } fightRegion)
                {
                    ShowMatchRegions(fightResult);
                    await Context.ClickMatchCenterAsync(fightRegion, cancellationToken);
                }

                await Task.Delay(1000, cancellationToken);
                break;

            case ForbiddenForestState.Summary:
                Logger.LogDebug("检测到禁林结算页面。");
                await Task.Delay(3000, cancellationToken);

                var thumbResult = Find(
                    GetImage("over_thumb"),
                    new TemplateSearchOptions { FindMultiple = true },
                    cancellationToken);

                if (thumbResult.Success)
                {
                    foreach (var region in thumbResult.Regions)
                    {
                        Context.Overlay.AddTemporaryRegion(Context.ToOverlayRegion(region), 1000);
                        await Context.ClickMatchCenterAsync(region, cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                    }
                }

                await Task.Delay(1500, cancellationToken);
                await Context.SendSpaceAsync(cancellationToken);
                _round++;
                Logger.LogInformation(
                    "第 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮 [Cyan]禁林[/Cyan] 已完成。",
                    _round,
                    Options.Times);
                await Task.Delay(2000, cancellationToken);
                break;
        }
    }

    private void OnStateDetected(ForbiddenForestState newState)
    {
        CurrentState = newState;
        if (newState != ForbiddenForestState.Unknown)
        {
            ResetUnknownBuffer();
        }
    }

    private Mat GetImage(string name) =>
        _images.TryGetValue(name, out var image)
            ? image
            : throw new KeyNotFoundException($"Task image was not loaded: {name}");

    private Task<bool> TryClickNamedTemplateAsync(
        string name,
        double threshold = 0.9,
        CancellationToken cancellationToken = default) =>
        TryClickTemplateAsync(GetImage(name), threshold, cancellationToken);
}

public sealed class AutoForbiddenForestTaskFactory : IAutomationTaskFactory
{
    private readonly ILogger<AutoForbiddenForestTask> _logger;

    public AutoForbiddenForestTaskFactory(ILogger<AutoForbiddenForestTask> logger)
    {
        _logger = logger;
    }

    public AutomationTaskType TaskType => AutomationTaskType.AutoForbiddenForest;

    public IAutomationTask Create(AutomationTaskOptions options)
    {
        if (options is not ForbiddenForestTaskOptions forbiddenForestOptions)
        {
            throw new ArgumentException(
                $"Options for {TaskType} must be {nameof(ForbiddenForestTaskOptions)}.",
                nameof(options));
        }

        return new AutoForbiddenForestTask(forbiddenForestOptions, _logger);
    }
}
