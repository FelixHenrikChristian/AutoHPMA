using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Helpers.DataHelper;
using AutoHPMA.Helpers.ImageHelper;
using AutoHPMA.Helpers.RecognizeHelper;
using AutoHPMA.Services;
using AutoHPMA.Views.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static AutoHPMA.Helpers.WindowInteractionHelper;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace AutoHPMA.GameTask.Permanent;

public enum AutoClubQuizState
{
    Unknown,
    Map,
    ClubScene,
    ChatFrame,
    Events,
    Wait,
    Quiz,
    Over,
    Victory,
}

public class AutoClubQuiz : BaseGameTask
{
    #region 字段

    private static ExcelHelper excelHelper;

    private volatile AutoClubQuizState _state = AutoClubQuizState.Unknown;

    // 状态字段
    private string? excelPath;
    private char bestOption;
    private string? q, a, b, c, d, answer, i;
    private bool _optionLocated = false, _questionLocated = false;
    private bool _quiz_over = true;
    private Dictionary<char, Rect> optionRects = new();
    private Rect question_rect;
    private Rect index_rect;
    private int detect_gap = 200;
    private enum GatherRefreshMode { ChatBox, Badge }
    private GatherRefreshMode _gatherRefreshMode = GatherRefreshMode.Badge;
    private int _answerDelay = 0;
    private bool _joinOthers = true;
    private bool _stopWhenContributionFull = false;
    private int roundIndex = 1;

    // OCR 服务
    private readonly IOcrService _ocrService;

    #endregion

    // 状态检测规则
    private StateRule<AutoClubQuizState>[] _stateRules = null!;

    public AutoClubQuiz(ILogger<AutoClubQuiz> logger, IOcrService ocrService, nint displayHwnd, nint gameHwnd)
        : base(logger, displayHwnd, gameHwnd)
    {
        _ocrService = ocrService;
        excelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/ClubQuiz", "club_question_bank.xlsx");
        excelHelper = new ExcelHelper(excelPath);
        LoadAssets();
        CalOffset();
        InitStateRules();
    }

    private void InitStateRules()
    {
        _stateRules = new StateRule<AutoClubQuizState>[]
        {
            new(new[] { GetImage("ui_club_symbol") }, AutoClubQuizState.ClubScene, "社团答题-等待中"),
            new(new[] { GetImage("map_return") }, AutoClubQuizState.Map, "社团答题-地图"),
            new(new[] { GetImage("chat_mail"), GetImage("chat_whisper") }, AutoClubQuizState.ChatFrame, "社团答题-聊天框"),
            new(new[] { GetImage("badge_club_shop") }, AutoClubQuizState.Events, "社团答题-活动选择"),
            new(new[] { GetImage("quiz_wait") }, AutoClubQuizState.Wait, "社团答题-集结中"),
            new(new[] { GetImage("quiz_leave") }, AutoClubQuizState.Quiz, "社团答题-活动中"),
            new(new[] { GetImage("quiz_over") }, AutoClubQuizState.Over, "社团答题-已结束"),
            new(new[] { GetImage("quiz_victory") }, AutoClubQuizState.Victory, "社团答题-结算中"),
        };
    }



    public void LoadAssets()
    {
        LoadImagesFromDirectory("Assets/ClubQuiz/Image/");
    }

    public override async void Start()
    {
        _state = AutoClubQuizState.Unknown;
        StartStateMonitor(_stateRules, OnStateDetected, AutoClubQuizState.Unknown, "社团答题-未进入场景");
        await RunTaskAsync("社团答题");
    }

    private void OnStateDetected(AutoClubQuizState newState)
    {
        _state = newState;
        if (newState != AutoClubQuizState.Unknown)
            _waited = false;
    }

    protected override async Task ExecuteLoopAsync()
    {
        await CloseDialogsAsync();
        // 状态由后台监测任务更新，直接使用 _state
        switch (_state)
        {
            case AutoClubQuizState.Unknown:
                await HandleUnknownState();
                break;

            case AutoClubQuizState.Map:
                await HandleMapState();
                break;

            case AutoClubQuizState.ClubScene:
                await HandleClubSceneState();
                break;

            case AutoClubQuizState.ChatFrame:
                await HandleChatFrameState();
                break;

            case AutoClubQuizState.Events:
                await HandleEventsState();
                break;

            case AutoClubQuizState.Wait:
                await Task.Delay(1000, _cts.Token);
                break;

            case AutoClubQuizState.Quiz:
                await HandleQuizState();
                break;

            case AutoClubQuizState.Over:
                await HandleOverState();
                break;

            case AutoClubQuizState.Victory:
                await HandleVictoryState();
                break;
        }
    }

    #region 状态处理方法

    private async Task HandleUnknownState()
    {
        if (!_waited)
        {
            _logWindow?.SetGameState("社团答题-等待加载");
            await Task.Delay(3000, _cts.Token);
            _waited = true;
            return;
        }
        // 已等待过，执行操作进入地图
        _waited = false;
        await SendESCAsync();
        await Task.Delay(2000, _cts.Token);
        await WindowInteractionHelper.SendKeyAsync(_gameHwnd, 0x4D); // M键打开地图
        await Task.Delay(2000, _cts.Token);
    }

    private async Task HandleMapState()
    {
        await TryClickTemplateAsync(GetImage("map_castle_symbol"));
        await Task.Delay(1000, _cts.Token);
        await TryClickTemplateAsync(GetImage("map_club_symbol"));
        await Task.Delay(1000, _cts.Token);
        await TryClickTemplateAsync(GetImage("map_club_enter"));
        await Task.Delay(1000, _cts.Token);
        await SendESCAsync();
        await Task.Delay(2000, _cts.Token);
    }

    private async Task HandleClubSceneState()
    {
        _logger.LogInformation("等待下一场答题...");
        for (int i = 5; i > 0; i--)
        {
            _logger.LogInformation("还剩[Yellow]{Count}[/Yellow]秒...", i);
            await Task.Delay(1000, _cts.Token);
            _logWindow?.DeleteLastLogMessage();
        }
        _logWindow?.DeleteLastLogMessage();

        switch (_gatherRefreshMode)
        {
            case GatherRefreshMode.ChatBox:
                await SendEnterAsync();
                await Task.Delay(2000, _cts.Token);
                _gatherRefreshMode = GatherRefreshMode.Badge;
                break;
            case GatherRefreshMode.Badge:
                await TryClickTemplateAsync(GetImage("ui_badge"));
                await Task.Delay(3000, _cts.Token);
                _gatherRefreshMode = GatherRefreshMode.ChatBox;
                break;
        }
    }

    private async Task HandleChatFrameState()
    {
        // 点击展开社团频道
        if (await TryClickTemplateAsync(GetImage("chat_club"), 0.88))
            await Task.Delay(2000, _cts.Token);
        // 点击前往活动面板
        if (await TryClickTemplateAsync(GetImage("chat_club_quiz"), 0.98))
            await Task.Delay(2000, _cts.Token);

        if (_joinOthers)
        {
            await TryJoinOthersQuiz();
        }

        // 关闭聊天框
        await SendESCAsync();
        await Task.Delay(1500, _cts.Token);
    }

    private async Task TryJoinOthersQuiz()
    {
        // 尝试在学院互助频道找到社团答题
        if (await TryClickTemplateAsync(GetImage("chat_college_help"), 0.88) || await TryClickTemplateAsync(GetImage("chat_college"), 0.88))
        {
            await Task.Delay(1500, _cts.Token);
            
            // 如果是学院频道，先点学院互助
            if (await TryClickTemplateAsync(GetImage("chat_college_help")))
                await Task.Delay(1500, _cts.Token);
            
            // 尝试点击前往活动
            if (await TryClickTemplateAsync(GetImage("chat_club_quiz"), 0.98))
            {
                await Task.Delay(2000, _cts.Token);
                if (Find(GetImage("chat_club_quiz"), new MatchOptions { Threshold = 0.98 }).Success)
                {
                    await SendESCAsync();
                    await Task.Delay(1500, _cts.Token);
                }
            }
        }
    }

    private async Task HandleEventsState()
    {
        var enterResult = Find(GetImage("badge_enter"), new MatchOptions { Mask = GetImage("badge_enter_mask") });
        if (!enterResult.Success)
        {
            await SendESCAsync();
        }
        else
        {
            await ClickMatchCenterAsync(enterResult);
        }
        await Task.Delay(1500, _cts.Token);
    }

    private async Task HandleQuizState()
    {
        
        if (_quiz_over)
        {
            _logger.LogInformation("第[Yellow]{roundIndex}[/Yellow]轮答题开始", roundIndex);
            _quiz_over = false;
        }
        
        if (!_optionLocated && !LocateOptions())
        {
            _logger.LogWarning("未定位到选项框位置！即将重新尝试定位。");
            await Task.Delay(1000, _cts.Token);
            return;
        }
        
        if (!_questionLocated && !LocateQuestion())
        {
            _logger.LogWarning("未定位到问题框位置！即将重新尝试定位。");
            await Task.Delay(1000, _cts.Token);
            return;
        }

        if (FindTime20AndIndex())
        {
            await Task.Delay(500, _cts.Token);
            LocateQuestion();
            await Task.Delay(100, _cts.Token);
            await AcquireAnswerAsync();
            return;
        }

        await Task.Delay(detect_gap, _cts.Token);
    }

    private async Task HandleOverState()
    {
        ClearStateRects();
        await Task.Delay(1000, _cts.Token);
        await TryClickTemplateAsync(GetImage("quiz_over"));
        await Task.Delay(2000, _cts.Token);
    }

    private async Task HandleVictoryState()
    {
        await Task.Delay(1000, _cts.Token);
        roundIndex++;
        _quiz_over = true;
        FindScore();
        await SendESCAsync();
        await Task.Delay(1000, _cts.Token);
    }

    #endregion

    #region 辅助方法

    private async Task AcquireAnswerAsync()
    {
        await Task.Run(() => RecogniseText());
        q = TextMatchHelper.FilterChineseAndPunctuation(q);
        PrintText();
        answer = excelHelper.GetBestMatchingAnswer(q);
        bestOption = TextMatchHelper.FindBestOption(answer, a, b, c, d);
        await Task.Delay(_answerDelay, _cts.Token);
        i = Regex.Match(i, @"\d+/\d+").Value;
        _logger.LogInformation("进度：[Yellow]{i}[/Yellow]。答案：[Lime]{bestOption}[/Lime]。", i, bestOption);
        await ClickOptionAsync();
    }

    /// <summary>
    /// 定位所有选项位置（使用循环简化重复代码）
    /// </summary>
    private bool LocateOptions()
    {
        var captureMat = CaptureAndPreprocess();
        
        var optionTemplates = new (char key, Mat template)[]
        {
            ('A', GetImage("quiz_option_a")),
            ('B', GetImage("quiz_option_b")),
            ('C', GetImage("quiz_option_c")),
            ('D', GetImage("quiz_option_d"))
        };

        optionRects.Clear();

        foreach (var (key, template) in optionTemplates)
        {
            var matchPoint = MatchTemplateHelper.MatchTemplate(
                captureMat, template, TemplateMatchModes.CCoeffNormed, GetImage("quiz_option_mask"), 0.85);
            
            if (matchPoint == default || !IsValidPoint(matchPoint, captureMat))
                return false;
            
            optionRects[key] = new Rect(matchPoint.X, matchPoint.Y, template.Width, template.Height);
        }

        // 显示定位结果
        var scaledRects = optionRects.Values.Select(r => ScaleRect(r, scale)).ToList();
        SetStateRects(scaledRects);

        _optionLocated = true;
        return true;
    }

    private bool IsValidPoint(Point p, Mat mat) =>
        p.X >= 0 && p.Y >= 0 && p.X < mat.Width && p.Y < mat.Height;

    private bool LocateQuestion()
    {
        var captureMat = CaptureAndPreprocess();
        Mat captureMat_binary = ContourDetectHelper.Binarize(captureMat, 200);
        question_rect = ContourDetectHelper.DetectApproxRectangle(captureMat_binary);
        
        if (question_rect == default)
            return false;

        _questionLocated = true;
        // 将问题框也加入状态检测框
        var allRects = optionRects.Values.Select(r => ScaleRect(r, scale)).ToList();
        allRects.Add(ScaleRect(question_rect, scale));
        SetStateRects(allRects);
        return true;
    }

    public void RecogniseText()
    {
        var captureMat = CaptureAndPreprocess();

        q = _ocrService.Recognize(new Mat(captureMat, question_rect));
        a = _ocrService.Recognize(new Mat(captureMat, optionRects['A']));
        b = _ocrService.Recognize(new Mat(captureMat, optionRects['B']));
        c = _ocrService.Recognize(new Mat(captureMat, optionRects['C']));
        d = _ocrService.Recognize(new Mat(captureMat, optionRects['D']));
        i = _ocrService.Recognize(new Mat(captureMat, index_rect));
    }

    private void PrintText()
    {
        _logger.LogDebug("问题：{q}", q);
        _logger.LogDebug("选项A：{a}", a);
        _logger.LogDebug("选项B：{b}", b);
        _logger.LogDebug("选项C：{c}", c);
        _logger.LogDebug("选项D：{d}", d);
    }

    private async Task ClickOptionAsync()
    {
        var targetRect = optionRects.GetValueOrDefault(bestOption, optionRects['A']);
        var centerX = targetRect.X + GetImage("quiz_option_mask").Width / 4;
        var centerY = targetRect.Y + GetImage("quiz_option_mask").Height / 2;
        await ClickAsync(new Point(centerX, centerY));
    }

    public async Task CloseDialogsAsync()
    {
        if (await TryClickTemplateAsync(GetImage("close_quiz_info")))
            await Task.Delay(1000, _cts.Token);
        if (await TryClickTemplateAsync(GetImage("close_club_rank")))
            await Task.Delay(1000, _cts.Token);
    }

    private void FindScore()
    {
        var captureMat = CaptureAndPreprocess();
        string ocrText = _ocrService.Recognize(captureMat);
        var match = Regex.Match(ocrText, @"\+(\d+)\s*社团贡献\s*\((\d+\/\d+)\)", RegexOptions.Singleline);

        if (!match.Success)
        {
            // 检测是否是贡献已满的情况（模糊匹配"本周"和"上限"）
            if (ocrText.Contains("本周") && ocrText.Contains("上限"))
            {
                _logger.LogInformation("本周社团贡献已满。");
                ToastNotificationHelper.ShowToastWithImage("答题结束", "本周社团贡献已满。", captureMat);
                
                if (_stopWhenContributionFull)
                {
                    _logger.LogInformation("已配置贡献满时停止，任务即将终止。");
                    Stop();
                }
                return;
            }
            
            _logger.LogWarning("无法识别社团贡献分数，请检查OCR设置或截图质量。");
            return;
        }
        
        int addScore = int.Parse(match.Groups[1].Value);
        string weekTotal = match.Groups[2].Value;

        _logger.LogInformation("本次社团贡献：[Yellow]+{addScore}[/Yellow]。", addScore);
        _logger.LogInformation("本周社团贡献：[Yellow]{weekTotal}[/Yellow]。", weekTotal);
        ToastNotificationHelper.ShowToastWithImage("答题结束", $"本次社团贡献：+{addScore}。\n本周社团贡献：{weekTotal}。", captureMat);
    }

    private bool FindTime20AndIndex()
    {
        var result = Find(GetImage("quiz_time20"));
        if (!result.Success) return false;

        // 将时间框加入状态检测框
        var allRects = optionRects.Values.Select(r => ScaleRect(r, scale)).ToList();
        allRects.Add(ScaleRect(question_rect, scale));
        allRects.AddRange(result.Rects);
        SetStateRects(allRects);
        var time20 = GetImage("quiz_time20");
        index_rect = new Rect(result.Location.X, result.Location.Y + time20.Height, time20.Width, time20.Height);
        return true;
    }

    #endregion

    #region 参数设置

    public override bool SetParameters(Dictionary<string, object> parameters)
    {
        try
        {
            if (TryGetParameter(parameters, "AnswerDelay", out int answerDelay))
            {
                if (answerDelay < 0)
                {
                    _logger.LogWarning("答题延迟不能小于0。已设置为默认值。");
                    return false;
                }
                _answerDelay = answerDelay;
                _logger.LogDebug("答题延迟设置为：{AnswerDelay}秒", _answerDelay);
            }

            if (TryGetParameter(parameters, "JoinOthers", out bool joinOthers))
            {
                _joinOthers = joinOthers;
            }

            if (TryGetParameter(parameters, "StopWhenContributionFull", out bool stopWhenContributionFull))
            {
                _stopWhenContributionFull = stopWhenContributionFull;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("设置参数时发生错误：{Message}", ex.Message);
            return false;
        }
    }

    #endregion
}
