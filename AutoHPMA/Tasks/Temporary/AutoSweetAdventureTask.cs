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

public sealed class AutoSweetAdventureTask : AutomationTaskBase<SweetAdventureTaskOptions>
{
    private const string ImageDirectory = "Assets/Tasks/SweetAdventure/Image";
    private const int MaxStep = 12;
    private static readonly double[] StateScaleFactors = [1d, 0.98d, 1.02d, 0.95d, 1.05d];
    private static readonly TemplateSearchOptions StateSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = StateScaleFactors,
    };
    private static readonly TemplateSearchOptions HighConfidenceSearchOptions = new()
    {
        Threshold = 0.96,
    };

    private readonly Dictionary<string, Mat> _images;
    private readonly IReadOnlyList<AutomationTaskStateRule<SweetAdventureState>> _stateRules;
    private int _stateValue = (int)SweetAdventureState.Unknown;
    private int _round;
    private int _previousRound;
    private int _step = 1;

    public AutoSweetAdventureTask(
        SweetAdventureTaskOptions options,
        ILogger<AutoSweetAdventureTask> logger)
        : base(options, logger)
    {
        _images = LoadImagesFromDirectory(ImageDirectory, ImreadModes.Unchanged);
        _stateRules =
        [
            new([GetImage("ui_teaming")], SweetAdventureState.Teaming, "甜蜜冒险-组队中", SearchOptions: StateSearchOptions),
            new([GetImage("ui_gaming")], SweetAdventureState.Gaming, "甜蜜冒险-游戏中", SearchOptions: StateSearchOptions),
            new([GetImage("ui_endding")], SweetAdventureState.Ending, "甜蜜冒险-结算中", SearchOptions: StateSearchOptions),
        ];

        UnknownBufferMilliseconds = 5000;
    }

    public override AutomationTaskType TaskType => AutomationTaskType.AutoSweetAdventure;

    public override string DisplayName => "甜蜜冒险";

    private SweetAdventureState CurrentState
    {
        get => (SweetAdventureState)Volatile.Read(ref _stateValue);
        set => Volatile.Write(ref _stateValue, (int)value);
    }

    protected override async Task ExecuteAsync(
        AutomationTaskContext context,
        CancellationToken cancellationToken)
    {
        CurrentState = SweetAdventureState.Unknown;
        _round = 0;
        _previousRound = 0;
        _step = 1;

        var monitorInterval = TimeSpan.FromMilliseconds(
            Math.Max(context.RuntimeOptions?.StateMonitorInterval ?? 200, 50));
        StartStateMonitor(
            _stateRules,
            OnStateDetected,
            SweetAdventureState.Unknown,
            "甜蜜冒险-未知状态",
            monitorInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            await ExecuteLoopAsync(cancellationToken);
        }
    }

    private async Task ExecuteLoopAsync(CancellationToken cancellationToken)
    {
        switch (CurrentState)
        {
            case SweetAdventureState.Unknown:
                if (!IsUnknownBufferElapsed())
                {
                    await Task.Delay(1000, cancellationToken);
                    return;
                }

                await Task.Delay(1000, cancellationToken);
                break;

            case SweetAdventureState.Teaming:
                if (await TryClickNamedTemplateAsync("teaming_start", cancellationToken: cancellationToken))
                {
                    Logger.LogInformation("[Cyan]甜蜜冒险[/Cyan] 已点击开始匹配。");
                    await Task.Delay(3000, cancellationToken);
                }

                await Task.Delay(1000, cancellationToken);
                break;

            case SweetAdventureState.Gaming:
                _round = FindRound(cancellationToken);
                if (_round > _previousRound)
                {
                    Logger.LogInformation("当前回合：[Gold]{Round}[/Gold]。", _round);
                    _step = 1;
                    _previousRound = _round;
                }

                if (_step < MaxStep)
                {
                    if (await TryClickStepTemplateAsync("gaming_forward", "前进", cancellationToken))
                    {
                        return;
                    }

                    if (await TryClickStepTemplateAsync("gaming_candy", "预测糖果", cancellationToken))
                    {
                        return;
                    }
                }
                else
                {
                    if (await TryClickStepTemplateAsync("gaming_return", "返回", cancellationToken))
                    {
                        return;
                    }

                    if (await TryClickStepTemplateAsync("gaming_monster", "预测怪物", cancellationToken))
                    {
                        return;
                    }
                }

                await Task.Delay(1000, cancellationToken);
                break;

            case SweetAdventureState.Ending:
                ClearTaskStateRegions();
                _round = 0;
                _previousRound = 0;
                _step = 1;
                Logger.LogInformation("[Cyan]甜蜜冒险[/Cyan] 游戏结束，正在结算。");
                await Context.SendSpaceAsync(cancellationToken);
                await Task.Delay(2000, cancellationToken);
                break;
        }
    }

    private void OnStateDetected(SweetAdventureState newState)
    {
        CurrentState = newState;
        if (newState != SweetAdventureState.Unknown)
        {
            ResetUnknownBuffer();
        }
    }

    private async Task<bool> TryClickStepTemplateAsync(
        string imageName,
        string actionName,
        CancellationToken cancellationToken)
    {
        var result = Find(GetImage(imageName), HighConfidenceSearchOptions, cancellationToken);
        if (result.FirstRegion is not { } region)
        {
            return false;
        }

        ShowMatchRegions(result);
        await Context.ClickMatchCenterAsync(region, cancellationToken);
        _step++;
        Logger.LogInformation("第 [Gold]{Step}[/Gold] 步：{ActionName}。", _step, actionName);
        await Task.Delay(1000, cancellationToken);
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
            var result = Find(GetImage(imageName), cancellationToken: cancellationToken);
            if (!result.Success)
            {
                continue;
            }

            SetTaskStateRegions(result.Regions);
            return round;
        }

        return -1;
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

public sealed class AutoSweetAdventureTaskFactory : IAutomationTaskFactory
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
