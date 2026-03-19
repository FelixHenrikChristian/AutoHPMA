using AutoHPMA.GameTask.Model;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Helpers.ImageHelper;
using AutoHPMA.Services;
using AutoHPMA.Services.Interface;
using AutoHPMA.Views.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AutoHPMA.Helpers.WindowInteractionHelper;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace AutoHPMA.GameTask.Temporary;

public enum AutoSweetAdventureState
{
    Unknown,
    Teaming,
    Gaming,
    Ending,
}

public class AutoSweetAdventure : BaseGameTask
{
    private volatile AutoSweetAdventureState _state = AutoSweetAdventureState.Unknown;

    private int _round = 0, _prevRound = 0, _step = 1;
    private int _maxStep = 12;

    // 状态检测规则
    private StateRule<AutoSweetAdventureState>[] _stateRules = null!;

    public AutoSweetAdventure(
        ILogger<AutoSweetAdventure> logger,
        IAppContextService appContextService,
        nint displayHwnd,
        nint gameHwnd)
        : base(logger, appContextService, displayHwnd, gameHwnd)
    {
        LoadAssets();
        CalculateOffset();
        InitStateRules();
    }



    private void InitStateRules()
    {
        _stateRules = new StateRule<AutoSweetAdventureState>[]
        {
            new(new[] { GetImage("ui_teaming") }, AutoSweetAdventureState.Teaming, "甜蜜冒险-组队中"),
            new(new[] { GetImage("ui_gaming") }, AutoSweetAdventureState.Gaming, "甜蜜冒险-游戏中"),
            new(new[] { GetImage("ui_endding") }, AutoSweetAdventureState.Ending, "甜蜜冒险-结算中"),
        };
    }

    private void LoadAssets()
    {
        LoadImagesFromDirectory("Assets/Tasks/SweetAdventure/Image/");
    }

    public override void Start()
    {
        _state = AutoSweetAdventureState.Unknown;
        StartStateMonitor(_stateRules, OnStateDetected, AutoSweetAdventureState.Unknown, "甜蜜冒险-未知状态");
        SafeFireAndForget(RunTaskAsync("甜蜜冒险"));
    }

    private void OnStateDetected(AutoSweetAdventureState newState)
    {
        _state = newState;
        if (newState != AutoSweetAdventureState.Unknown)
            _hasWaitedForInitialState = false;
    }

    protected override async Task ExecuteLoopAsync()
    {
        // 状态由后台监测任务更新，直接使用 _state
        switch (_state)
        {
            case AutoSweetAdventureState.Unknown:
                if (!_hasWaitedForInitialState)
                {
                    await Task.Delay(5000, _cts.Token);
                    _hasWaitedForInitialState = true;
                    return;
                }
                _hasWaitedForInitialState = false;
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoSweetAdventureState.Teaming:
                var startResult = Find(GetImage("teaming_start"));
                if (startResult.Success)
                {
                    await ClickMatchCenterAsync(startResult);
                    await Task.Delay(3000, _cts.Token);
                }
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoSweetAdventureState.Gaming:
                _round = FindRound();
                if (_round > _prevRound)
                {
                    _logger.LogInformation("当前回合数：[Yellow]{Round}[/Yellow]。", _round);
                    _step = 1;
                    _prevRound = _round;
                }
                if (_step < _maxStep)
                {
                    var forwardResult = Find(GetImage("gaming_forward"), new MatchOptions { Threshold = 0.96 });
                    if (forwardResult.Success)
                    {
                        await ClickMatchCenterAsync(forwardResult);
                        _step++;
                        _logger.LogInformation("第[Yellow]{Step}[/Yellow]步：前进。", _step);
                        await Task.Delay(1000, _cts.Token);
                        return;
                    }
                    
                    var candyResult = Find(GetImage("gaming_candy"), new MatchOptions { Threshold = 0.96 });
                    if (candyResult.Success)
                    {
                        await ClickMatchCenterAsync(candyResult);
                        _step++;
                        _logger.LogInformation("第[Yellow]{Step}[/Yellow]步：预测糖果。", _step);
                        await Task.Delay(1000, _cts.Token);
                        return;
                    }
                }
                else
                {
                    var returnResult = Find(GetImage("gaming_return"), new MatchOptions { Threshold = 0.96 });
                    if (returnResult.Success)
                    {
                        await ClickMatchCenterAsync(returnResult);
                        _step++;
                        _logger.LogInformation("第[Yellow]{Step}[/Yellow]步：返回。", _step);
                        await Task.Delay(1000, _cts.Token);
                        return;
                    }
                    
                    var monsterResult = Find(GetImage("gaming_monster"), new MatchOptions { Threshold = 0.96 });
                    if (monsterResult.Success)
                    {
                        await ClickMatchCenterAsync(monsterResult);
                        _step++;
                        _logger.LogInformation("第[Yellow]{Step}[/Yellow]步：预测怪物。", _step);
                        await Task.Delay(1000, _cts.Token);
                        return;
                    }
                }
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoSweetAdventureState.Ending:
                ClearStateRects();
                _round = 0;
                _prevRound = 0;
                _step = 1;
                _logger.LogInformation("游戏结束，正在结算中...");
                await SendSpaceAsync();
                await Task.Delay(2000, _cts.Token);
                break;
        }
    }

    private int FindRound()
    {
        // 使用模板数组简化重复代码
        var roundTemplates = new[] 
        { 
            (GetImage("gaming_round1"), 1), 
            (GetImage("gaming_round2"), 2), 
            (GetImage("gaming_round3"), 3), 
            (GetImage("gaming_round4"), 4), 
            (GetImage("gaming_round5"), 5) 
        };

        foreach (var (template, roundNum) in roundTemplates)
        {
            var result = Find(template);
            if (result.Success)
            {
                SetStateRects(result.Rects);
                return roundNum;
            }
        }

        return -1;
    }

    public override bool SetParameters(Dictionary<string, object> parameters)
    {
        // 甜蜜冒险目前没有需要设置的参数
        return true;
    }
}
