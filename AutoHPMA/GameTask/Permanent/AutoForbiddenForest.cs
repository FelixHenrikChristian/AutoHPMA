using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Helpers.ImageHelper;
using AutoHPMA.Helpers.RecognizeHelper;
using AutoHPMA.Services;
using AutoHPMA.Views.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AutoHPMA.Helpers.WindowInteractionHelper;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace AutoHPMA.GameTask.Permanent;

public enum AutoForbiddenForestState
{
    Unknown,
    Teaming,
    Loading,
    Fighting,
    Summary,
}

public enum AutoForbiddenForestOption
{
    Leader,
    Member,
}

public class AutoForbiddenForest : BaseGameTask
{
    private volatile AutoForbiddenForestState _state = AutoForbiddenForestState.Unknown;

    private int _autoForbiddenForestTimes;
    private AutoForbiddenForestOption _autoForbiddenForestOption = AutoForbiddenForestOption.Leader;

    private int round = 0;

    // 状态检测规则
    private StateRule<AutoForbiddenForestState>[] _stateRules = null!;

    public AutoForbiddenForest(ILogger<AutoForbiddenForest> logger, nint displayHwnd, nint gameHwnd)
        : base(logger, displayHwnd, gameHwnd)
    {
        LoadAssets();
        CalOffset();
        InitStateRules();
    }



    private void InitStateRules()
    {
        _stateRules = new StateRule<AutoForbiddenForestState>[]
        {
            new(new[] { GetImage("ui_explore") }, AutoForbiddenForestState.Teaming, "禁林-组队中"),
            new(new[] { GetImage("ui_loading") }, AutoForbiddenForestState.Loading, "禁林-加载中"),
            new(new[] { GetImage("ui_clock") }, AutoForbiddenForestState.Fighting, "禁林-战斗中"),
            new(new[] { GetImage("ui_statistics") }, AutoForbiddenForestState.Summary, "禁林-结算中"),
        };
    }

    private void LoadAssets()
    {
        string imageFolder = "Assets/ForbiddenForest/Image/";
        // 加载普通图片（Color模式）
        LoadImagesFromDirectory(imageFolder);
        // 单独加载需要 Alpha 通道的图片
        _images["fight_auto"] = Cv2.ImRead(imageFolder + "fight_auto.png", ImreadModes.Unchanged);
    }

    public override async void Start()
    {
        _state = AutoForbiddenForestState.Unknown;
        StartStateMonitor(_stateRules, OnStateDetected, AutoForbiddenForestState.Unknown, "禁林-未知状态");
        await RunTaskAsync("禁林");
    }

    private void OnStateDetected(AutoForbiddenForestState newState)
    {
        _state = newState;
        if (newState != AutoForbiddenForestState.Unknown)
            _waited = false;
    }

    protected override async Task ExecuteLoopAsync()
    {
        if (round >= _autoForbiddenForestTimes)
        {
            ToastNotificationHelper.ShowToast("禁林任务完成", $"已完成 {round} 轮禁林任务。");
            Stop();
            return;
        }

        // 状态由后台监测任务更新，直接使用 _state
        switch (_state)
        {
            case AutoForbiddenForestState.Unknown:
                if (!_waited)
                {
                    await Task.Delay(5000, _cts.Token);
                    _waited = true;
                    return;
                }
                _waited = false;
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoForbiddenForestState.Teaming:
                var autoResult = Find(GetImage("team_auto"));
                if (autoResult.Success)
                {
                    await ClickMatchCenterAsync(autoResult);
                    _logger.LogDebug("点击自动战斗按钮。");
                }
                await Task.Delay(1000, _cts.Token);
                
                switch (_autoForbiddenForestOption)
                {
                    case AutoForbiddenForestOption.Leader:
                        var startResult = Find(GetImage("team_start"));
                        if (startResult.Success)
                        {
                            await ClickMatchCenterAsync(startResult);
                            _logger.LogDebug("点击开始。");
                        }
                        await Task.Delay(1500, _cts.Token);
                        
                        var confirmResult = Find(GetImage("team_confirm"));
                        if (confirmResult.Success)
                        {
                            await ClickMatchCenterAsync(confirmResult);
                            _logger.LogDebug("点击是。");
                        }
                        break;
                        
                    case AutoForbiddenForestOption.Member:
                        var readyResult = Find(GetImage("team_ready"));
                        if (readyResult.Success)
                        {
                            await ClickMatchCenterAsync(readyResult);
                            _logger.LogDebug("点击准备。");
                        }
                        await Task.Delay(1000, _cts.Token);
                        break;
                }
                break;

            case AutoForbiddenForestState.Loading:
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoForbiddenForestState.Fighting:
                var fightResult = Find(GetImage("fight_auto"), new MatchOptions 
                { 
                    UseAlphaMask = true, 
                    Threshold = 0.8 
                });
                if (fightResult.Success)
                {
                    await ClickMatchCenterAsync(fightResult);
                }
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoForbiddenForestState.Summary:
                _logger.LogDebug("检测到点赞页面");
                await Task.Delay(3000, _cts.Token);
                
                var thumbResult = Find(GetImage("over_thumb"), new MatchOptions { FindMultiple = true });
                if (thumbResult.Success)
                {
                    // 逐个显示检测框并点击
                    for (int i = 0; i < thumbResult.RectsUnscaled.Count; i++)
                    {
                        var rect = thumbResult.RectsUnscaled[i];
                        var scaledRect = thumbResult.Rects[i];
                        
                        // 显示当前点击目标的检测框
                        _maskWindow?.AddTemporaryRect(scaledRect, durationMs: 1000);
                        
                        // 点击目标中心
                        var centerX = rect.X + thumbResult.TemplateSize.Width / 2.0;
                        var centerY = rect.Y + thumbResult.TemplateSize.Height / 2.0;
                        await ClickAsync(new OpenCvSharp.Point((int)centerX, (int)centerY));
                        
                        await Task.Delay(1000, _cts.Token);
                    }
                }
                
                await Task.Delay(1500, _cts.Token);
                await SendSpaceAsync();
                _logger.LogInformation("第[Yellow]{Round}[/Yellow]/[Yellow]{Total}[/Yellow]次禁林任务完成。", ++round, _autoForbiddenForestTimes);
                await Task.Delay(2000, _cts.Token);
                break;
        }
    }

    public override bool SetParameters(Dictionary<string, object> parameters)
    {
        try
        {
            if (TryGetParameter(parameters, "Times", out int times))
            {
                if (times < 0)
                {
                    _logger.LogWarning("禁林次数必须大于等于0。已设置为默认值。");
                    return false;
                }
                _autoForbiddenForestTimes = times;
                _logger.LogDebug("禁林次数设置为：{Times}次", _autoForbiddenForestTimes);
            }

            if (TryGetParameter(parameters, "TeamPosition", out string position))
            {
                _autoForbiddenForestOption = position switch
                {
                    "队长" => AutoForbiddenForestOption.Leader,
                    "队员" => AutoForbiddenForestOption.Member,
                    _ => throw new ArgumentException("无效的队伍位置")
                };
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("设置参数时发生错误：{Message}", ex.Message);
            return false;
        }
    }
}
