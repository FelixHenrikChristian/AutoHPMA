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

internal sealed class AutoForbiddenForestTask :
    StateMachineAutomationTaskBase<ForbiddenForestTaskOptions, ForbiddenForestState>
{
    private const string ImageDirectory = "Assets/Tasks/ForbiddenForest/Image";
    private const int LoopDelayMilliseconds = 1000;
    private const int LeaderConfirmDelayMilliseconds = 1500;
    private const int SummaryReadyDelayMilliseconds = 3000;
    private const int SummaryThumbDelayMilliseconds = 1000;
    private const int SummaryExitDelayMilliseconds = 1500;
    private const int SummaryCooldownMilliseconds = 2000;

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
    private static readonly TemplateSearchOptions FightAutoSearchOptions = new()
    {
        UseAlphaMask = true,
        Threshold = 0.8,
    };
    private static readonly TemplateSearchOptions SummaryThumbSearchOptions = new()
    {
        FindMultiple = true,
    };

    private readonly IReadOnlyList<AutomationTaskStateRule<ForbiddenForestState>> _stateRules;
    private int _round;
    private bool _summaryHandled;

    public AutoForbiddenForestTask(
        ForbiddenForestTaskOptions options,
        ILogger<AutoForbiddenForestTask> logger)
        : base(options, logger, ForbiddenForestState.Unknown)
    {
        LoadTaskImages(ImageDirectory, ImreadModes.Unchanged);
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

    protected override ForbiddenForestState UnknownState => ForbiddenForestState.Unknown;

    protected override string UnknownDisplayName => "禁林-未知状态";

    protected override IReadOnlyList<AutomationTaskStateRule<ForbiddenForestState>> StateRules => _stateRules;

    protected override Task InitializeStateMachineAsync(CancellationToken cancellationToken)
    {
        _round = 0;
        _summaryHandled = false;
        return Task.CompletedTask;
    }

    protected override bool ShouldContinue(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _round < Options.Times;

    protected override async Task HandleStateAsync(
        ForbiddenForestState state,
        CancellationToken cancellationToken)
    {
        switch (state)
        {
            case ForbiddenForestState.Unknown:
                await HandleUnknownStateAsync(cancellationToken);
                break;
            case ForbiddenForestState.Teaming:
                await HandleTeamingStateAsync(cancellationToken);
                break;
            case ForbiddenForestState.Loading:
                await DelayAsync(cancellationToken, LoopDelayMilliseconds);
                break;
            case ForbiddenForestState.Fighting:
                await HandleFightingStateAsync(cancellationToken);
                break;
            case ForbiddenForestState.Summary:
                await HandleSummaryStateAsync(cancellationToken);
                break;
        }
    }

    protected override void OnDetectedStateChanged(
        ForbiddenForestState previousState,
        ForbiddenForestState currentState)
    {
        base.OnDetectedStateChanged(previousState, currentState);
        if (currentState != ForbiddenForestState.Summary)
        {
            _summaryHandled = false;
        }
    }

    protected override Task OnStateMachineCompletedAsync(CancellationToken cancellationToken)
    {
        if (_round < Options.Times)
        {
            return Task.CompletedTask;
        }

        AppNotificationHelper.Show("禁林任务完成", $"已完成 {_round} 轮禁林任务。");
        Logger.LogInformation(
            "[Cyan]禁林[/Cyan] 任务完成：共完成 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮。",
            _round,
            Options.Times);
        return Task.CompletedTask;
    }

    private async Task HandleUnknownStateAsync(CancellationToken cancellationToken)
    {
        if (!IsUnknownBufferElapsed())
        {
            await DelayAsync(cancellationToken, LoopDelayMilliseconds);
            return;
        }

        await DelayAsync(cancellationToken, LoopDelayMilliseconds);
    }

    private async Task HandleTeamingStateAsync(CancellationToken cancellationToken)
    {
        if (await TryClickNamedTemplateAsync("team_auto", cancellationToken: cancellationToken))
        {
            Logger.LogDebug("点击禁林自动战斗按钮。");
        }

        await DelayAsync(cancellationToken, LoopDelayMilliseconds);
        if (Options.IsLeader)
        {
            await HandleLeaderTeamingAsync(cancellationToken);
            return;
        }

        await HandleMemberTeamingAsync(cancellationToken);
    }

    private async Task HandleLeaderTeamingAsync(CancellationToken cancellationToken)
    {
        if (await TryClickNamedTemplateAsync("team_start", cancellationToken: cancellationToken))
        {
            Logger.LogDebug("点击禁林开始按钮。");
        }

        await DelayAsync(cancellationToken, LeaderConfirmDelayMilliseconds);
        if (await TryClickNamedTemplateAsync("team_confirm", cancellationToken: cancellationToken))
        {
            Logger.LogDebug("确认禁林队伍出发。");
        }
    }

    private async Task HandleMemberTeamingAsync(CancellationToken cancellationToken)
    {
        if (await TryClickNamedTemplateAsync("team_ready", cancellationToken: cancellationToken))
        {
            Logger.LogDebug("点击禁林准备按钮。");
        }

        await DelayAsync(cancellationToken, LoopDelayMilliseconds);
    }

    private async Task HandleFightingStateAsync(CancellationToken cancellationToken)
    {
        var fightResult = FindImage("fight_auto", FightAutoSearchOptions, cancellationToken);
        if (fightResult.FirstRegion is { } fightRegion)
        {
            ShowMatchRegions(fightResult);
            await Context.ClickMatchCenterAsync(fightRegion, cancellationToken);
        }

        await DelayAsync(cancellationToken, LoopDelayMilliseconds);
    }

    private async Task HandleSummaryStateAsync(CancellationToken cancellationToken)
    {
        if (_summaryHandled)
        {
            await DelayAsync(cancellationToken, LoopDelayMilliseconds);
            return;
        }

        _summaryHandled = true;
        Logger.LogDebug("检测到禁林结算页面。");
        await DelayAsync(cancellationToken, SummaryReadyDelayMilliseconds);
        await ClickAllSummaryThumbsAsync(cancellationToken);
        await DelayAsync(cancellationToken, SummaryExitDelayMilliseconds);
        await Context.SendSpaceAsync(cancellationToken);

        _round++;
        Logger.LogInformation(
            "第 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮 [Cyan]禁林[/Cyan] 已完成。",
            _round,
            Options.Times);
        await DelayAsync(cancellationToken, SummaryCooldownMilliseconds);
    }

    private async Task ClickAllSummaryThumbsAsync(CancellationToken cancellationToken)
    {
        var thumbResult = FindImage("over_thumb", SummaryThumbSearchOptions, cancellationToken);
        if (!thumbResult.Success)
        {
            return;
        }

        foreach (var region in thumbResult.Regions)
        {
            Context.Overlay.AddTemporaryRegion(Context.ToOverlayRegion(region));
            await Context.ClickMatchCenterAsync(region, cancellationToken);
            await DelayAsync(cancellationToken, SummaryThumbDelayMilliseconds);
        }
    }
}

internal sealed class AutoForbiddenForestTaskFactory : IAutomationTaskFactory
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
