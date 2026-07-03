using AutoHPMA.Contracts.Tasks;
using AutoHPMA.Core.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AutoHPMA.Tasks.Temporary;

internal enum SweetAdventureState
{
    Unknown,
    Teaming,
    Gaming,
    Ending,
}

internal sealed class AutoSweetAdventureTask :
    StateMachineAutomationTaskBase<SweetAdventureTaskOptions, SweetAdventureState>
{
    private const string ImageDirectory = "Assets/Tasks/SweetAdventure/Image";
    private const int LoopDelayMilliseconds = 1000;
    private const int TeamingStartDelayMilliseconds = 3000;
    private const int EndingDelayMilliseconds = 2000;

    private static readonly double[] StateScaleFactors = [1d, 0.98d, 1.02d, 0.95d, 1.05d];
    private static readonly TemplateSearchOptions StateSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = StateScaleFactors,
    };

    private readonly IReadOnlyList<AutomationTaskStateRule<SweetAdventureState>> _stateRules;
    private readonly SweetAdventureGameSession _gameSession;
    private bool _endingHandled;

    public AutoSweetAdventureTask(
        SweetAdventureTaskOptions options,
        ILogger<AutoSweetAdventureTask> logger)
        : base(options, logger, SweetAdventureState.Unknown)
    {
        LoadTaskImages(ImageDirectory, ImreadModes.Unchanged);
        _stateRules =
        [
            new([GetImage("ui_teaming")], SweetAdventureState.Teaming, "甜蜜冒险-组队中", SearchOptions: StateSearchOptions),
            new([GetImage("ui_gaming")], SweetAdventureState.Gaming, "甜蜜冒险-游戏中", SearchOptions: StateSearchOptions),
            new([GetImage("ui_endding")], SweetAdventureState.Ending, "甜蜜冒险-结算中", SearchOptions: StateSearchOptions),
        ];
        _gameSession = new SweetAdventureGameSession(this);

        UnknownBufferMilliseconds = 5000;
    }

    public override AutomationTaskType TaskType => AutomationTaskType.AutoSweetAdventure;

    public override string DisplayName => "甜蜜冒险";

    protected override SweetAdventureState UnknownState => SweetAdventureState.Unknown;

    protected override string UnknownDisplayName => "甜蜜冒险-未知状态";

    protected override IReadOnlyList<AutomationTaskStateRule<SweetAdventureState>> StateRules => _stateRules;

    protected override Task InitializeStateMachineAsync(CancellationToken cancellationToken)
    {
        _gameSession.Reset();
        _endingHandled = false;
        return Task.CompletedTask;
    }

    protected override async Task HandleStateAsync(
        SweetAdventureState state,
        CancellationToken cancellationToken)
    {
        switch (state)
        {
            case SweetAdventureState.Unknown:
                await HandleUnknownStateAsync(cancellationToken);
                break;
            case SweetAdventureState.Teaming:
                await HandleTeamingStateAsync(cancellationToken);
                break;
            case SweetAdventureState.Gaming:
                await _gameSession.PlayAsync(cancellationToken);
                break;
            case SweetAdventureState.Ending:
                await HandleEndingStateAsync(cancellationToken);
                break;
        }
    }

    protected override void OnDetectedStateChanged(
        SweetAdventureState previousState,
        SweetAdventureState currentState)
    {
        base.OnDetectedStateChanged(previousState, currentState);
        if (currentState != SweetAdventureState.Ending)
        {
            _endingHandled = false;
        }
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
        if (await TryClickNamedTemplateAsync("teaming_start", cancellationToken: cancellationToken))
        {
            Logger.LogInformation("[Cyan]甜蜜冒险[/Cyan] 已点击开始匹配。");
            await DelayAsync(cancellationToken, TeamingStartDelayMilliseconds);
        }

        await DelayAsync(cancellationToken, LoopDelayMilliseconds);
    }

    private async Task HandleEndingStateAsync(CancellationToken cancellationToken)
    {
        if (_endingHandled)
        {
            await DelayAsync(cancellationToken, LoopDelayMilliseconds);
            return;
        }

        _endingHandled = true;
        ClearTaskStateRegions();
        _gameSession.Reset();
        Logger.LogInformation("[Cyan]甜蜜冒险[/Cyan] 游戏结束，正在结算。");
        await Context.SendSpaceAsync(cancellationToken);
        await DelayAsync(cancellationToken, EndingDelayMilliseconds);
    }

    private sealed class SweetAdventureGameSession
    {
        private const int MaxStep = 12;
        private static readonly TemplateSearchOptions StepSearchOptions = new()
        {
            Threshold = 0.96,
        };

        private readonly AutoSweetAdventureTask _task;
        private int _round;
        private int _previousRound;
        private int _step = 1;

        public SweetAdventureGameSession(AutoSweetAdventureTask task)
        {
            _task = task;
        }

        public void Reset()
        {
            _round = 0;
            _previousRound = 0;
            _step = 1;
        }

        public async Task PlayAsync(CancellationToken cancellationToken)
        {
            UpdateRound(cancellationToken);
            if (_step < MaxStep)
            {
                if (await TryClickStepAsync("gaming_forward", "前进", cancellationToken) ||
                    await TryClickStepAsync("gaming_candy", "预测糖果", cancellationToken))
                {
                    return;
                }
            }
            else if (await TryClickStepAsync("gaming_return", "返回", cancellationToken) ||
                     await TryClickStepAsync("gaming_monster", "预测怪物", cancellationToken))
            {
                return;
            }

            await DelayAsync(cancellationToken, LoopDelayMilliseconds);
        }

        private void UpdateRound(CancellationToken cancellationToken)
        {
            _round = FindRound(cancellationToken);
            if (_round <= _previousRound)
            {
                return;
            }

            _task.Logger.LogInformation("当前回合：[Gold]{Round}[/Gold]。", _round);
            _step = 1;
            _previousRound = _round;
        }

        private async Task<bool> TryClickStepAsync(
            string imageName,
            string actionName,
            CancellationToken cancellationToken)
        {
            var result = _task.Find(_task.GetImage(imageName), StepSearchOptions, cancellationToken);
            if (result.FirstRegion is not { } region)
            {
                return false;
            }

            _task.ShowMatchRegions(result);
            await _task.Context.ClickMatchCenterAsync(region, cancellationToken);
            _step++;
            _task.Logger.LogInformation("第 [Gold]{Step}[/Gold] 步：{ActionName}。", _step, actionName);
            await DelayAsync(cancellationToken, LoopDelayMilliseconds);
            return true;
        }

        private int FindRound(CancellationToken cancellationToken)
        {
            var roundTemplates = new[]
            {
                ("gaming_round1", 1),
                ("gaming_round2", 2),
                ("gaming_round3", 3),
                ("gaming_round4", 4),
                ("gaming_round5", 5),
            };

            foreach (var (imageName, round) in roundTemplates)
            {
                var result = _task.Find(_task.GetImage(imageName), cancellationToken: cancellationToken);
                if (!result.Success)
                {
                    continue;
                }

                _task.SetTaskStateRegions(result.Regions);
                return round;
            }

            return -1;
        }
    }
}

internal sealed class AutoSweetAdventureTaskFactory : IAutomationTaskFactory
{
    private readonly ILogger<AutoSweetAdventureTask> _logger;

    public AutoSweetAdventureTaskFactory(ILogger<AutoSweetAdventureTask> logger)
    {
        _logger = logger;
    }

    public AutomationTaskType TaskType => AutomationTaskType.AutoSweetAdventure;

    public IAutomationTask Create(AutomationTaskOptions options)
    {
        if (options is not SweetAdventureTaskOptions sweetAdventureOptions)
        {
            throw new ArgumentException(
                $"Options for {TaskType} must be {nameof(SweetAdventureTaskOptions)}.",
                nameof(options));
        }

        return new AutoSweetAdventureTask(sweetAdventureOptions, _logger);
    }
}
