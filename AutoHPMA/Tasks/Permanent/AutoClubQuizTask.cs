using System.Text.RegularExpressions;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Contracts.Tasks;
using AutoHPMA.Core.Models;
using AutoHPMA.Core.Services;
using AutoHPMA.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AutoHPMA.Tasks.Permanent;

internal enum ClubQuizState
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

public sealed class AutoClubQuizTask : AutomationTaskBase<ClubQuizTaskOptions>
{
    private const string ImageDirectory = "Assets/Tasks/ClubQuiz/Image";
    private const string QuestionBankPath = "Assets/Tasks/ClubQuiz/club_question_bank.xlsx";
    private const int OpenMapKey = 0x4D;
    private const int DetectGapMilliseconds = 200;

    private static readonly double[] StateScaleFactors = [1d, 0.98d, 1.02d, 0.95d, 1.05d];
    private static readonly Regex ProgressRegex = new(@"\d+\s*/\s*\d+", RegexOptions.Compiled);
    private static readonly Regex ContributionRegex = new(@"\+(\d+)\s*社团贡献\s*\((\d+\s*/\s*\d+)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly TemplateSearchOptions StateSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = StateScaleFactors,
    };

    private readonly Dictionary<string, Mat> _images;
    private readonly IReadOnlyList<AutomationTaskStateRule<ClubQuizState>> _stateRules;
    private readonly IOcrService _ocrService;
    private readonly ClubQuizQuestionBank _questionBank;
    private readonly Dictionary<char, Rect> _optionRects = [];

    private int _stateValue = (int)ClubQuizState.Unknown;
    private GatherRefreshMode _gatherRefreshMode = GatherRefreshMode.Badge;
    private Rect _questionRect;
    private Rect _indexRect;
    private bool _optionLocated;
    private bool _questionLocated;
    private bool _quizOver = true;
    private bool _shouldStop;
    private int _roundIndex = 1;

    public AutoClubQuizTask(
        ClubQuizTaskOptions options,
        ILogger<AutoClubQuizTask> logger,
        IOcrService ocrService)
        : base(options, logger)
    {
        _ocrService = ocrService;
        _images = LoadImagesFromDirectory(ImageDirectory, ImreadModes.Unchanged);
        _questionBank = ClubQuizQuestionBank.Load(Path.Combine(AppContext.BaseDirectory, QuestionBankPath));
        _stateRules =
        [
            new([GetImage("ui_club_symbol")], ClubQuizState.ClubScene, "社团问答-社团场景", SearchOptions: StateSearchOptions),
            new([GetImage("map_return")], ClubQuizState.Map, "社团问答-地图", SearchOptions: StateSearchOptions),
            new([GetImage("chat_mail"), GetImage("chat_whisper")], ClubQuizState.ChatFrame, "社团问答-聊天框", SearchOptions: StateSearchOptions),
            new([GetImage("badge_club_shop")], ClubQuizState.Events, "社团问答-活动选择", SearchOptions: StateSearchOptions),
            new([GetImage("quiz_wait")], ClubQuizState.Wait, "社团问答-集合中", SearchOptions: StateSearchOptions),
            new([GetImage("quiz_leave")], ClubQuizState.Quiz, "社团问答-答题中", SearchOptions: StateSearchOptions),
            new([GetImage("quiz_over")], ClubQuizState.Over, "社团问答-已结束", SearchOptions: StateSearchOptions),
            new([GetImage("quiz_victory")], ClubQuizState.Victory, "社团问答-结算中", SearchOptions: StateSearchOptions),
        ];

        UnknownBufferMilliseconds = 3000;
        Logger.LogDebug("已加载社团问答题库 {QuestionCount} 条。", _questionBank.Count);
    }

    public override AutomationTaskType TaskType => AutomationTaskType.AutoClubQuiz;

    public override string DisplayName => "社团问答";

    private ClubQuizState CurrentState
    {
        get => (ClubQuizState)Volatile.Read(ref _stateValue);
        set => Volatile.Write(ref _stateValue, (int)value);
    }

    protected override async Task ExecuteAsync(
        AutomationTaskContext context,
        CancellationToken cancellationToken)
    {
        CurrentState = ClubQuizState.Unknown;
        ResetQuizState();
        _shouldStop = false;
        _roundIndex = 1;

        var monitorInterval = TimeSpan.FromMilliseconds(
            Math.Max(context.RuntimeOptions?.StateMonitorInterval ?? 200, 50));
        StartStateMonitor(
            _stateRules,
            OnStateDetected,
            ClubQuizState.Unknown,
            "社团问答-等待进入场景",
            monitorInterval);

        while (!cancellationToken.IsCancellationRequested && !_shouldStop)
        {
            await ExecuteLoopAsync(cancellationToken);
        }
    }

    private async Task ExecuteLoopAsync(CancellationToken cancellationToken)
    {
        await CloseDialogsAsync(cancellationToken);

        switch (CurrentState)
        {
            case ClubQuizState.Unknown:
                await HandleUnknownStateAsync(cancellationToken);
                break;

            case ClubQuizState.Map:
                await HandleMapStateAsync(cancellationToken);
                break;

            case ClubQuizState.ClubScene:
                await HandleClubSceneStateAsync(cancellationToken);
                break;

            case ClubQuizState.ChatFrame:
                await HandleChatFrameStateAsync(cancellationToken);
                break;

            case ClubQuizState.Events:
                await HandleEventsStateAsync(cancellationToken);
                break;

            case ClubQuizState.Wait:
                await Task.Delay(1000, cancellationToken);
                break;

            case ClubQuizState.Quiz:
                await HandleQuizStateAsync(cancellationToken);
                break;

            case ClubQuizState.Over:
                await HandleOverStateAsync(cancellationToken);
                break;

            case ClubQuizState.Victory:
                await HandleVictoryStateAsync(cancellationToken);
                break;
        }
    }

    private async Task HandleUnknownStateAsync(CancellationToken cancellationToken)
    {
        if (!IsUnknownBufferElapsed())
        {
            await Task.Delay(1000, cancellationToken);
            return;
        }

        Logger.LogDebug("社团问答未识别到场景，尝试打开地图。");
        await Context.SendEscapeAsync(cancellationToken);
        await Task.Delay(2000, cancellationToken);
        await Context.SendKeyAsync(OpenMapKey, cancellationToken);
        await Task.Delay(2000, cancellationToken);
    }

    private async Task HandleMapStateAsync(CancellationToken cancellationToken)
    {
        await TryClickNamedTemplateAsync("map_castle_symbol", cancellationToken: cancellationToken);
        await Task.Delay(1000, cancellationToken);
        await TryClickNamedTemplateAsync("map_club_symbol", cancellationToken: cancellationToken);
        await Task.Delay(1000, cancellationToken);
        await TryClickNamedTemplateAsync("map_club_enter", cancellationToken: cancellationToken);
        await Task.Delay(1000, cancellationToken);
        await Context.SendEscapeAsync(cancellationToken);
        await Task.Delay(2000, cancellationToken);
    }

    private async Task HandleClubSceneStateAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("[Cyan]社团问答[/Cyan] 等待下一场答题集合。");
        await Task.Delay(5000, cancellationToken);

        if (_gatherRefreshMode == GatherRefreshMode.ChatBox)
        {
            await Context.SendEnterAsync(cancellationToken);
            _gatherRefreshMode = GatherRefreshMode.Badge;
            await Task.Delay(2000, cancellationToken);
            return;
        }

        await TryClickNamedTemplateAsync("ui_badge", cancellationToken: cancellationToken);
        _gatherRefreshMode = GatherRefreshMode.ChatBox;
        await Task.Delay(3000, cancellationToken);
    }

    private async Task HandleChatFrameStateAsync(CancellationToken cancellationToken)
    {
        if (await TryClickNamedTemplateAsync("chat_club", 0.88, cancellationToken))
        {
            await Task.Delay(2000, cancellationToken);
        }

        if (await TryClickNamedTemplateAsync("chat_club_quiz", 0.98, cancellationToken))
        {
            await Task.Delay(2000, cancellationToken);
        }

        if (Options.JoinOthers)
        {
            await TryJoinOthersQuizAsync(cancellationToken);
        }

        await Context.SendEscapeAsync(cancellationToken);
        await Task.Delay(1500, cancellationToken);
    }

    private async Task TryJoinOthersQuizAsync(CancellationToken cancellationToken)
    {
        var enteredCollegeChannel =
            await TryClickNamedTemplateAsync("chat_college_help", 0.88, cancellationToken) ||
            await TryClickNamedTemplateAsync("chat_college", 0.88, cancellationToken);
        if (!enteredCollegeChannel)
        {
            return;
        }

        await Task.Delay(1500, cancellationToken);
        if (await TryClickNamedTemplateAsync("chat_college_help", cancellationToken: cancellationToken))
        {
            await Task.Delay(1500, cancellationToken);
        }

        if (!await TryClickNamedTemplateAsync("chat_club_quiz", 0.98, cancellationToken))
        {
            return;
        }

        await Task.Delay(2000, cancellationToken);
        if (Find(GetImage("chat_club_quiz"), new TemplateSearchOptions { Threshold = 0.98 }, cancellationToken).Success)
        {
            await Context.SendEscapeAsync(cancellationToken);
            await Task.Delay(1500, cancellationToken);
        }
    }

    private async Task HandleEventsStateAsync(CancellationToken cancellationToken)
    {
        using var sourceMask = ToGrayMask(GetImage("badge_enter_mask"));
        var enterResult = Find(
            GetImage("badge_enter"),
            new TemplateSearchOptions
            {
                Threshold = 0.9,
                SourceMask = sourceMask,
            },
            cancellationToken);

        if (enterResult.FirstRegion is null)
        {
            await Context.SendEscapeAsync(cancellationToken);
        }
        else
        {
            ShowMatchRegions(enterResult);
            await Context.ClickMatchCenterAsync(enterResult.FirstRegion.Value, cancellationToken);
        }

        await Task.Delay(1500, cancellationToken);
    }

    private async Task HandleQuizStateAsync(CancellationToken cancellationToken)
    {
        if (_quizOver)
        {
            Logger.LogInformation("第 [Gold]{RoundIndex}[/Gold] 轮 [Cyan]社团问答[/Cyan] 开始。", _roundIndex);
            _quizOver = false;
        }

        if (!_optionLocated && !LocateOptions(cancellationToken))
        {
            Logger.LogWarning("未定位到社团问答选项区域，将重试。");
            await Task.Delay(1000, cancellationToken);
            return;
        }

        if (!_questionLocated && !LocateQuestion(cancellationToken))
        {
            Logger.LogWarning("未定位到社团问答问题区域，将重试。");
            await Task.Delay(1000, cancellationToken);
            return;
        }

        if (!FindTime20AndIndex(cancellationToken))
        {
            await Task.Delay(DetectGapMilliseconds, cancellationToken);
            return;
        }

        try
        {
            await Task.Delay(500, cancellationToken);
            LocateQuestion(cancellationToken);
            await Task.Delay(100, cancellationToken);
            await AcquireAnswerAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "社团问答识别或点击答案失败，将重试。");
            await Task.Delay(1000, cancellationToken);
        }
    }

    private async Task HandleOverStateAsync(CancellationToken cancellationToken)
    {
        ClearTaskStateRegions();
        await Task.Delay(1000, cancellationToken);
        await TryClickNamedTemplateAsync("quiz_over", cancellationToken: cancellationToken);
        await Task.Delay(2000, cancellationToken);
    }

    private async Task HandleVictoryStateAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
        _roundIndex++;
        _quizOver = true;
        await FindScoreAsync(cancellationToken);
        await Context.SendEscapeAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken);
    }

    private void OnStateDetected(ClubQuizState newState)
    {
        CurrentState = newState;
        if (newState != ClubQuizState.Unknown)
        {
            ResetUnknownBuffer();
        }
    }

    private async Task AcquireAnswerAsync(CancellationToken cancellationToken)
    {
        var text = await RecognizeQuestionTextAsync(cancellationToken);
        UpdateRecognizedTextRegions(text);
        var match = _questionBank.FindBestMatch(text.Question);
        var bestOption = ClubQuizQuestionBank.FindBestOption(
            match.Answer,
            new Dictionary<char, string?>
            {
                ['A'] = text.OptionA,
                ['B'] = text.OptionB,
                ['C'] = text.OptionC,
                ['D'] = text.OptionD,
            });

        Logger.LogDebug("问题 OCR：{Question}", text.Question);
        Logger.LogDebug("选项 OCR：A={OptionA} B={OptionB} C={OptionC} D={OptionD}", text.OptionA, text.OptionB, text.OptionC, text.OptionD);
        Logger.LogDebug("题库匹配：{MatchedQuestion}，相似度 {Score:P1}，答案 {Answer}", match.Question, match.Score, match.Answer);

        if (Options.AnswerDelay > 0)
        {
            await Task.Delay(Options.AnswerDelay, cancellationToken);
        }

        var progress = NormalizeProgress(text.Index);
        Logger.LogInformation(
            "答题进度：[Gold]{Progress}[/Gold]，选择：[Gold]{Option}[/Gold]。",
            progress,
            bestOption);
        await ClickOptionAsync(bestOption, cancellationToken);
    }

    private bool LocateOptions(CancellationToken cancellationToken)
    {
        using var captureMat = Context.CaptureBgrMat(cancellationToken);
        using var optionMask = ToGrayMask(GetImage("quiz_option_mask"));
        var optionTemplates = new[]
        {
            ('A', GetImage("quiz_option_a")),
            ('B', GetImage("quiz_option_b")),
            ('C', GetImage("quiz_option_c")),
            ('D', GetImage("quiz_option_d")),
        };

        _optionRects.Clear();
        foreach (var (key, template) in optionTemplates)
        {
            var result = Context.TemplateMatching.Search(
                captureMat,
                template,
                new TemplateSearchOptions
                {
                    Threshold = 0.85,
                    Mask = optionMask,
                });

            if (result.FirstRegion is not { } region)
            {
                _optionRects.Clear();
                return false;
            }

            _optionRects[key] = new Rect(region.X, region.Y, region.Width, region.Height);
        }

        _optionLocated = true;
        UpdateLocatedRegions();
        return true;
    }

    private bool LocateQuestion(CancellationToken cancellationToken)
    {
        using var captureMat = Context.CaptureBgrMat(cancellationToken);
        using var binary = Binarize(captureMat, 200);
        var rect = DetectApproxRectangle(binary, 1000, 5);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        _questionRect = rect;
        _questionLocated = true;
        UpdateLocatedRegions();
        return true;
    }

    private async Task<QuizRecognizedText> RecognizeQuestionTextAsync(CancellationToken cancellationToken)
    {
        var engineType = ResolveOcrEngineType();
        using var captureMat = Context.CaptureBgrMat(cancellationToken);
        using var question = Crop(captureMat, _questionRect);
        using var optionA = Crop(captureMat, _optionRects['A']);
        using var optionB = Crop(captureMat, _optionRects['B']);
        using var optionC = Crop(captureMat, _optionRects['C']);
        using var optionD = Crop(captureMat, _optionRects['D']);
        using var index = Crop(captureMat, _indexRect);

        return new QuizRecognizedText(
            await RecognizeAsync(question, engineType, cancellationToken),
            await RecognizeAsync(optionA, engineType, cancellationToken),
            await RecognizeAsync(optionB, engineType, cancellationToken),
            await RecognizeAsync(optionC, engineType, cancellationToken),
            await RecognizeAsync(optionD, engineType, cancellationToken),
            await RecognizeAsync(index, engineType, cancellationToken));
    }

    private void UpdateRecognizedTextRegions(QuizRecognizedText text)
    {
        var regions = new List<OverlayRegion>();
        if (_questionLocated)
        {
            AddOcrOverlayRegion(regions, _questionRect, text.Question);
        }

        foreach (var (option, rect) in _optionRects.OrderBy(pair => pair.Key))
        {
            AddOcrOverlayRegion(regions, rect, option switch
            {
                'A' => text.OptionA,
                'B' => text.OptionB,
                'C' => text.OptionC,
                'D' => text.OptionD,
                _ => string.Empty,
            });
        }

        if (_indexRect.Width > 0 && _indexRect.Height > 0)
        {
            AddOcrOverlayRegion(regions, _indexRect, text.Index);
        }

        if (regions.Count > 0)
        {
            Context.Overlay.SetTaskStateRegions(regions);
        }
    }

    private void AddOcrOverlayRegion(List<OverlayRegion> regions, Rect rect, string text)
    {
        regions.Add(Context.ToOverlayRegion(
            ToTemplateRegion(rect),
            null,
            FormatOcrOverlayText(text),
            OverlayRegionStatusKind.Detail,
            OverlayRegionKind.Ocr));
    }

    private async Task<string> RecognizeAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken) =>
        (await _ocrService.RecognizeAsync(mat, engineType, cancellationToken)).Trim();

    private async Task ClickOptionAsync(char option, CancellationToken cancellationToken)
    {
        if (!_optionRects.TryGetValue(option, out var targetRect) &&
            !_optionRects.TryGetValue('A', out targetRect))
        {
            Logger.LogWarning("未找到选项 {Option} 的点击区域。", option);
            return;
        }

        var optionMask = GetImage("quiz_option_mask");
        var centerX = targetRect.X + optionMask.Width / 4;
        var centerY = targetRect.Y + optionMask.Height / 2;
        await Context.ClickCanonicalAsync(centerX, centerY, cancellationToken);
    }

    private async Task CloseDialogsAsync(CancellationToken cancellationToken)
    {
        if (await TryClickNamedTemplateAsync("close_quiz_info", cancellationToken: cancellationToken))
        {
            await Task.Delay(1000, cancellationToken);
        }

        if (await TryClickNamedTemplateAsync("close_club_rank", cancellationToken: cancellationToken))
        {
            await Task.Delay(1000, cancellationToken);
        }
    }

    private async Task FindScoreAsync(CancellationToken cancellationToken)
    {
        var engineType = ResolveOcrEngineType();
        using var captureMat = Context.CaptureBgrMat(cancellationToken);
        var ocrText = await RecognizeAsync(captureMat, engineType, cancellationToken);
        var match = ContributionRegex.Match(ocrText);

        if (!match.Success)
        {
            if (ocrText.Contains("本周", StringComparison.Ordinal) &&
                ocrText.Contains("上限", StringComparison.Ordinal))
            {
                Logger.LogInformation("本周社团贡献已达上限。");
                AppNotificationHelper.Show("答题结束", "本周社团贡献已达上限。");

                if (Options.StopWhenContributionFull)
                {
                    Logger.LogInformation("已启用贡献满额终止，[Cyan]社团问答[/Cyan] 即将停止。");
                    _shouldStop = true;
                }

                return;
            }

            Logger.LogWarning("无法识别社团贡献分数，请检查 OCR 设置或截图质量。");
            return;
        }

        var addScore = match.Groups[1].Value;
        var weekTotal = Regex.Replace(match.Groups[2].Value, "\\s+", string.Empty);
        Logger.LogInformation("本次社团贡献：[Gold]+{AddScore}[/Gold]。", addScore);
        Logger.LogInformation("本周社团贡献：[Gold]{WeekTotal}[/Gold]。", weekTotal);
        AppNotificationHelper.Show("答题结束", $"本次社团贡献：+{addScore}\n本周社团贡献：{weekTotal}");
    }

    private bool FindTime20AndIndex(CancellationToken cancellationToken)
    {
        var result = Find(GetImage("quiz_time20"), cancellationToken: cancellationToken);
        if (result.FirstRegion is not { } region)
        {
            return false;
        }

        var time20 = GetImage("quiz_time20");
        _indexRect = new Rect(region.X, region.Y + time20.Height, time20.Width, time20.Height);
        UpdateLocatedRegions(result.Regions);
        return true;
    }

    private void ResetQuizState()
    {
        _optionRects.Clear();
        _optionLocated = false;
        _questionLocated = false;
        _questionRect = default;
        _indexRect = default;
        _quizOver = true;
        _gatherRefreshMode = GatherRefreshMode.Badge;
        ClearTaskStateRegions();
    }

    private void UpdateLocatedRegions(IEnumerable<TemplateMatchRegion>? extraRegions = null)
    {
        var regions = new List<OverlayRegion>();
        foreach (var (option, rect) in _optionRects.OrderBy(pair => pair.Key))
        {
            regions.Add(Context.ToOverlayRegion(
                ToTemplateRegion(rect),
                kind: OverlayRegionKind.Ocr));
        }

        if (_questionLocated)
        {
            regions.Add(Context.ToOverlayRegion(
                ToTemplateRegion(_questionRect),
                kind: OverlayRegionKind.Ocr));
        }

        if (_indexRect.Width > 0 && _indexRect.Height > 0)
        {
            regions.Add(Context.ToOverlayRegion(
                ToTemplateRegion(_indexRect),
                kind: OverlayRegionKind.Ocr));
        }

        if (extraRegions is not null)
        {
            regions.AddRange(Context.ToOverlayRegions(extraRegions));
        }

        if (regions.Count > 0)
        {
            Context.Overlay.SetTaskStateRegions(regions);
        }
    }

    private OcrEngineType ResolveOcrEngineType() =>
        Enum.TryParse<OcrEngineType>(Context.RuntimeOptions?.SelectedOcrEngine, ignoreCase: true, out var engineType)
            ? engineType
            : OcrEngineType.PaddleOCR;

    private Mat GetImage(string name) =>
        _images.TryGetValue(name, out var image)
            ? image
            : throw new KeyNotFoundException($"Task image was not loaded: {name}");

    private async Task<bool> TryClickNamedTemplateAsync(
        string name,
        double threshold = 0.9,
        CancellationToken cancellationToken = default)
    {
        var token = cancellationToken == default ? TaskCancellationToken : cancellationToken;
        var result = Find(GetImage(name), new TemplateSearchOptions { Threshold = threshold }, token);
        if (result.FirstRegion is not { } region)
        {
            return false;
        }

        ShowMatchRegions(result);
        await Context.ClickMatchCenterAsync(region, token);
        return true;
    }

    private static string FormatOcrOverlayText(string text)
    {
        var normalized = Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "未识别"
            : TruncateOverlayText(normalized, 80);
    }

    private static string TruncateOverlayText(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    private static string NormalizeProgress(string? input)
    {
        var value = ProgressRegex.Match(input ?? string.Empty).Value;
        return string.IsNullOrWhiteSpace(value)
            ? "未知"
            : Regex.Replace(value, "\\s+", string.Empty);
    }

    private static TemplateMatchRegion ToTemplateRegion(Rect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Mat Crop(Mat source, Rect rect) =>
        new(source, ClampRect(rect, source));

    private static Rect ClampRect(Rect rect, Mat mat)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(mat.Width - 1, 0));
        var y = Math.Clamp(rect.Y, 0, Math.Max(mat.Height - 1, 0));
        var right = Math.Clamp(rect.X + rect.Width, x + 1, mat.Width);
        var bottom = Math.Clamp(rect.Y + rect.Height, y + 1, mat.Height);
        return new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static Mat ToGrayMask(Mat mat)
    {
        if (mat.Channels() == 1)
        {
            return mat.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(
            mat,
            gray,
            mat.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static Mat Binarize(Mat source, double threshold)
    {
        using var gray = new Mat();
        var binary = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary);
        return binary;
    }

    private static Rect DetectApproxRectangle(Mat binaryImage, double minimumArea, double approximationEpsilon)
    {
        using var contourSource = binaryImage.Clone();
        Cv2.FindContours(
            contourSource,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var bestRect = default(Rect);
        var maxArea = 0d;
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < minimumArea)
            {
                continue;
            }

            var approx = Cv2.ApproxPolyDP(contour, approximationEpsilon, true);
            var boundingRect = Cv2.BoundingRect(approx);
            if (area > maxArea)
            {
                maxArea = area;
                bestRect = boundingRect;
            }
        }

        return bestRect;
    }

    private enum GatherRefreshMode
    {
        ChatBox,
        Badge,
    }

    private sealed record QuizRecognizedText(
        string Question,
        string OptionA,
        string OptionB,
        string OptionC,
        string OptionD,
        string Index);
}

public sealed class AutoClubQuizTaskFactory : IAutomationTaskFactory
{
    private readonly ILogger<AutoClubQuizTask> _logger;
    private readonly IOcrService _ocrService;

    public AutoClubQuizTaskFactory(
        ILogger<AutoClubQuizTask> logger,
        IOcrService ocrService)
    {
        _logger = logger;
        _ocrService = ocrService;
    }

    public AutomationTaskType TaskType => AutomationTaskType.AutoClubQuiz;

    public IAutomationTask Create(AutomationTaskOptions options)
    {
        if (options is not ClubQuizTaskOptions clubQuizOptions)
        {
            throw new ArgumentException(
                $"Options for {TaskType} must be {nameof(ClubQuizTaskOptions)}.",
                nameof(options));
        }

        return new AutoClubQuizTask(clubQuizOptions, _logger, _ocrService);
    }
}
