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
    ClubScene,
    ChatFrame,
    Events,
    Wait,
    Quiz,
    Over,
    Victory,
}

internal sealed class AutoClubQuizTask :
    StateMachineAutomationTaskBase<ClubQuizTaskOptions, ClubQuizState>
{
    private const string ImageDirectory = "Assets/Tasks/ClubQuiz/Image";
    private const string QuestionBankPath = "Assets/Tasks/ClubQuiz/club_question_bank.xlsx";
    private const int DetectGapMilliseconds = 200;

    private static readonly double[] StateScaleFactors = [1d, 0.98d, 1.02d, 0.95d, 1.05d];
    private static readonly Regex ProgressRegex = new(@"\d+\s*/\s*\d+", RegexOptions.Compiled);
    private static readonly Regex ContributionRegex = new(@"\+(\d+)\s*社团贡献\s*\((\d+\s*/\s*\d+)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly TemplateSearchOptions StateSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = StateScaleFactors,
    };

    private readonly IReadOnlyList<AutomationTaskStateRule<ClubQuizState>> _stateRules;
    private readonly IOcrService _ocrService;
    private readonly ClubQuizQuestionBank _questionBank;
    private readonly ClubQuizChatNavigator _chatNavigator;
    private readonly ClubQuizSession _quizSession;

    private GatherRefreshMode _gatherRefreshMode = GatherRefreshMode.Badge;
    private bool _sessionAnnouncementPending = true;
    private bool _unknownLogged;
    private bool _victoryHandled;
    private bool _shouldStop;
    private int _sessionIndex = 1;

    public AutoClubQuizTask(
        ClubQuizTaskOptions options,
        ILogger<AutoClubQuizTask> logger,
        IOcrService ocrService)
        : base(options, logger, ClubQuizState.Unknown)
    {
        _ocrService = ocrService;
        LoadTaskImages(ImageDirectory, ImreadModes.Unchanged);
        _questionBank = ClubQuizQuestionBank.Load(Path.Combine(AppContext.BaseDirectory, QuestionBankPath));
        _chatNavigator = new ClubQuizChatNavigator(this);
        _quizSession = new ClubQuizSession(this);
        _stateRules =
        [
            new([GetImage("ui_club_symbol")], ClubQuizState.ClubScene, "社团问答-社团场景", SearchOptions: StateSearchOptions),
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

    protected override ClubQuizState UnknownState => ClubQuizState.Unknown;

    protected override string UnknownDisplayName => "社团问答-等待进入场景";

    protected override IReadOnlyList<AutomationTaskStateRule<ClubQuizState>> StateRules => _stateRules;

    protected override Task InitializeStateMachineAsync(CancellationToken cancellationToken)
    {
        ResetQuizSession();
        _unknownLogged = false;
        _victoryHandled = false;
        _shouldStop = false;
        _sessionIndex = 1;
        return Task.CompletedTask;
    }

    protected override bool ShouldContinue(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && !_shouldStop;

    protected override async Task HandleStateAsync(
        ClubQuizState state,
        CancellationToken cancellationToken)
    {
        await CloseDialogsAsync(cancellationToken);

        switch (state)
        {
            case ClubQuizState.Unknown:
                await HandleUnknownStateAsync(cancellationToken);
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

    protected override void OnDetectedStateChanged(
        ClubQuizState previousState,
        ClubQuizState currentState)
    {
        base.OnDetectedStateChanged(previousState, currentState);
        if (currentState != ClubQuizState.Unknown)
        {
            _unknownLogged = false;
        }

        if (currentState != ClubQuizState.Victory)
        {
            _victoryHandled = false;
        }
    }

    private async Task HandleUnknownStateAsync(CancellationToken cancellationToken)
    {
        if (!IsUnknownBufferElapsed())
        {
            await Task.Delay(1000, cancellationToken);
            return;
        }

        if (!_unknownLogged)
        {
            Logger.LogInformation("未识别到社团问答场景，请手动进入社团答题入口。");
            _unknownLogged = true;
        }

        await Task.Delay(1000, cancellationToken);
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
        var enteredQuiz = await _chatNavigator.TryEnterOwnClubQuizAsync(cancellationToken);

        if (enteredQuiz)
        {
            Logger.LogDebug("已通过社团聊天找到答题入口，不再查找学院互助。");
        }
        else if (Options.JoinOthers)
        {
            await _chatNavigator.TryEnterCollegeHelpQuizAsync(cancellationToken);
        }
        else
        {
            Logger.LogDebug("未在社团聊天找到答题入口，且未开启加入其他社团答题。");
        }

        await Context.SendEscapeAsync(cancellationToken);
        await Task.Delay(2000, cancellationToken);
        ClearTaskStateRegions();
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
        if (_sessionAnnouncementPending)
        {
            Logger.LogInformation("第 [Gold]{SessionIndex}[/Gold] 轮 [Cyan]社团问答[/Cyan] 开始。", _sessionIndex);
            _sessionAnnouncementPending = false;
        }

        await _quizSession.TickAsync(cancellationToken);
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
        if (_victoryHandled)
        {
            await Task.Delay(1000, cancellationToken);
            return;
        }

        _victoryHandled = true;
        await Task.Delay(1000, cancellationToken);
        _sessionIndex++;
        ResetQuizSession();
        await ReadContributionAsync(cancellationToken);
        await Context.SendEscapeAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken);
    }

    private async Task<string> RecognizeAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken) =>
        (await _ocrService.RecognizeAsync(mat, engineType, cancellationToken)).Trim();

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

    private async Task ReadContributionAsync(CancellationToken cancellationToken)
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
                AppNotificationHelper.ShowWithImage("答题结束", "本周社团贡献已满。", captureMat);

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
        AppNotificationHelper.ShowWithImage(
            "答题结束",
            $"本次社团贡献：+{addScore}。\n本周社团贡献：{weekTotal}。",
            captureMat);
    }

    private void ResetQuizSession()
    {
        _quizSession.Reset();
        _sessionAnnouncementPending = true;
        _gatherRefreshMode = GatherRefreshMode.Badge;
    }

    private OcrEngineType ResolveOcrEngineType() =>
        Enum.TryParse<OcrEngineType>(Context.RuntimeOptions?.SelectedOcrEngine, ignoreCase: true, out var engineType)
            ? engineType
            : OcrEngineType.PaddleOCR;

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

    private sealed class ClubQuizChatNavigator
    {
        private const int ChannelExpandDelayMilliseconds = 1500;
        private const int ChannelSwitchDelayMilliseconds = 2200;
        private const int ChannelRetryDelayMilliseconds = 600;
        private const int QuizEntryDelayMilliseconds = 2500;
        private const double QuizEntryThreshold = 0.98;

        private readonly AutoClubQuizTask _task;

        public ClubQuizChatNavigator(AutoClubQuizTask task)
        {
            _task = task;
        }

        public async Task<bool> TryEnterOwnClubQuizAsync(CancellationToken cancellationToken)
        {
            if (!await EnsureChannelGroupExpandedAsync("社团", "社团聊天", cancellationToken))
            {
                _task.Logger.LogDebug("未展开社团聊天频道。");
                return false;
            }

            if (!await ClickChannelAsync("社团聊天", cancellationToken))
            {
                return false;
            }

            await Task.Delay(ChannelSwitchDelayMilliseconds, cancellationToken);
            return await TryClickQuizEntryAsync(closeIfStillVisible: false, cancellationToken);
        }

        public async Task<bool> TryEnterCollegeHelpQuizAsync(CancellationToken cancellationToken)
        {
            if (!await EnsureChannelGroupExpandedAsync("学院", "学院互助", cancellationToken))
            {
                _task.Logger.LogDebug("未展开学院互助频道。");
                return false;
            }

            if (!await ClickChannelAsync("学院互助", cancellationToken))
            {
                return false;
            }

            await Task.Delay(ChannelSwitchDelayMilliseconds, cancellationToken);
            return await TryClickQuizEntryAsync(closeIfStillVisible: true, cancellationToken);
        }

        private async Task<bool> EnsureChannelGroupExpandedAsync(
            string groupName,
            string expandedChildName,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var channels = await ReadChannelTextsAsync(cancellationToken);
                if (FindChannel(channels, expandedChildName) is not null)
                {
                    return true;
                }

                var group = FindChannel(channels, groupName);
                if (group is null)
                {
                    _task.Logger.LogDebug("未识别到聊天频道分组：{GroupName}", groupName);
                    await Task.Delay(ChannelRetryDelayMilliseconds, cancellationToken);
                    continue;
                }

                await ClickChannelAsync(group, cancellationToken);
                await Task.Delay(ChannelExpandDelayMilliseconds, cancellationToken);
            }

            return FindChannel(await ReadChannelTextsAsync(cancellationToken), expandedChildName) is not null;
        }

        private async Task<bool> ClickChannelAsync(
            string channelName,
            CancellationToken cancellationToken)
        {
            var channels = await ReadChannelTextsAsync(cancellationToken);
            var channel = FindChannel(channels, channelName);
            if (channel is null)
            {
                _task.Logger.LogDebug("未识别到聊天频道：{ChannelName}", channelName);
                return false;
            }

            await ClickChannelAsync(channel, cancellationToken);
            return true;
        }

        private async Task ClickChannelAsync(
            ChatChannelTextRegion channel,
            CancellationToken cancellationToken)
        {
            var centerX = channel.Bounds.X + channel.Bounds.Width / 2;
            var centerY = channel.Bounds.Y + channel.Bounds.Height / 2;
            _task.Logger.LogDebug("切换聊天频道：{ChannelText}", channel.Text);
            await _task.Context.ClickCanonicalAsync(centerX, centerY, cancellationToken);
        }

        private async Task<bool> TryClickQuizEntryAsync(
            bool closeIfStillVisible,
            CancellationToken cancellationToken)
        {
            if (!await _task.TryClickNamedTemplateAsync("chat_club_quiz", QuizEntryThreshold, cancellationToken))
            {
                _task.Logger.LogDebug("当前聊天频道未找到社团答题入口。");
                return false;
            }

            await Task.Delay(QuizEntryDelayMilliseconds, cancellationToken);
            if (_task.Find(
                    _task.GetImage("chat_club_quiz"),
                    new TemplateSearchOptions { Threshold = QuizEntryThreshold },
                    cancellationToken).Success)
            {
                if (closeIfStillVisible)
                {
                    await _task.Context.SendEscapeAsync(cancellationToken);
                    await Task.Delay(ChannelSwitchDelayMilliseconds, cancellationToken);
                }

                return false;
            }

            return true;
        }

        private async Task<IReadOnlyList<ChatChannelTextRegion>> ReadChannelTextsAsync(
            CancellationToken cancellationToken)
        {
            var configuredRegion = _task.Context.TaskCoordinates.GetRequiredRegion(TaskCoordinateIds.ChatChannels);
            var channelRegion = new Rect(
                configuredRegion.X,
                configuredRegion.Y,
                configuredRegion.Width,
                configuredRegion.Height);

            using var captureMat = _task.Context.CaptureBgrMat(cancellationToken);
            using var cropped = Crop(captureMat, channelRegion);
            var ocrRegions = await _task._ocrService.RecognizeRegionsAsync(
                cropped,
                OcrEngineType.PaddleOCR,
                cancellationToken);

            var channels = ocrRegions
                .Where(region => !string.IsNullOrWhiteSpace(region.Text))
                .Select(region => new ChatChannelTextRegion(
                    region.Text.Trim(),
                    new Rect(
                        channelRegion.X + region.Bounds.X,
                        channelRegion.Y + region.Bounds.Y,
                        region.Bounds.Width,
                        region.Bounds.Height)))
                .OrderBy(region => region.Bounds.Y)
                .ThenBy(region => region.Bounds.X)
                .ToArray();

            ShowChannelRegions(channels);
            return channels;
        }

        private void ShowChannelRegions(IReadOnlyList<ChatChannelTextRegion> channels)
        {
            if (channels.Count == 0)
            {
                _task.ClearTaskStateRegions();
                return;
            }

            _task.Context.Overlay.SetTaskStateRegions(
                channels.Select(channel => _task.Context.ToOverlayRegion(
                    ToTemplateRegion(channel.Bounds),
                    null,
                    channel.Text,
                    OverlayRegionStatusKind.Detail,
                    OverlayRegionKind.Ocr)).ToArray());
        }

        private static ChatChannelTextRegion? FindChannel(
            IReadOnlyList<ChatChannelTextRegion> channels,
            string channelName)
        {
            var normalizedTarget = NormalizeChannelText(channelName);
            var exactMatch = channels.FirstOrDefault(channel =>
                NormalizeChannelText(channel.Text).Equals(normalizedTarget, StringComparison.Ordinal));
            if (exactMatch is not null)
            {
                return exactMatch;
            }

            if (normalizedTarget.Length <= 2)
            {
                return channels.FirstOrDefault(channel =>
                {
                    var normalizedText = NormalizeChannelText(channel.Text);
                    return normalizedText.StartsWith(normalizedTarget, StringComparison.Ordinal) &&
                        normalizedText.Length <= normalizedTarget.Length + 1;
                });
            }

            return channels.FirstOrDefault(channel =>
                NormalizeChannelText(channel.Text).Contains(normalizedTarget, StringComparison.Ordinal));
        }

        private static string NormalizeChannelText(string text)
        {
            return new string((text ?? string.Empty)
                .Where(character =>
                    !char.IsWhiteSpace(character) &&
                    !char.IsPunctuation(character) &&
                    !char.IsSymbol(character))
                .ToArray());
        }

        private sealed record ChatChannelTextRegion(string Text, Rect Bounds);
    }

    private sealed class ClubQuizSession
    {
        private readonly AutoClubQuizTask _task;
        private readonly Dictionary<char, Rect> _optionRegions = [];

        private Rect _progressRegion;
        private Rect _questionRegion;
        private bool _hasAnsweredSinceJoining;
        private bool _optionsLocated;
        private bool _questionLocated;

        public ClubQuizSession(AutoClubQuizTask task)
        {
            _task = task;
        }

        public void Reset()
        {
            var progressRegion = _task.Context.TaskCoordinates.GetRequiredRegion(
                TaskCoordinateIds.ClubQuizProgress);
            _progressRegion = new Rect(
                progressRegion.X,
                progressRegion.Y,
                progressRegion.Width,
                progressRegion.Height);
            _optionRegions.Clear();
            _questionRegion = default;
            _hasAnsweredSinceJoining = false;
            _optionsLocated = false;
            _questionLocated = false;
            _task.ClearTaskStateRegions();
        }

        public async Task TickAsync(CancellationToken cancellationToken)
        {
            if (!_optionsLocated && !TryLocateOptions(cancellationToken))
            {
                _task.Logger.LogDebug("未定位到社团问答选项区域，将重试。");
                await Task.Delay(1000, cancellationToken);
                return;
            }

            if (!_questionLocated && !TryRefreshQuestionRegion(cancellationToken))
            {
                _task.Logger.LogWarning("未定位到社团问答问题区域，将重试。");
                await Task.Delay(1000, cancellationToken);
                return;
            }

            var roundStart = FindRoundStart(cancellationToken);
            var shouldAnswer = roundStart.Success || !_hasAnsweredSinceJoining;
            if (!shouldAnswer)
            {
                await Task.Delay(DetectGapMilliseconds, cancellationToken);
                return;
            }

            if (roundStart.Success)
            {
                _task.ShowMatchRegions(roundStart);
            }

            try
            {
                await Task.Delay(500, cancellationToken);
                TryRefreshQuestionRegion(cancellationToken);
                await Task.Delay(100, cancellationToken);
                await AnswerCurrentQuestionAsync(cancellationToken);
                _hasAnsweredSinceJoining = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _task.Logger.LogWarning(ex, "社团问答识别或点击答案失败，将重试。");
                await Task.Delay(1000, cancellationToken);
            }
        }

        private async Task AnswerCurrentQuestionAsync(CancellationToken cancellationToken)
        {
            var text = await ReadCurrentQuestionAsync(cancellationToken);
            ShowRecognizedRegions(text);
            var match = _task._questionBank.FindBestMatch(text.Question);
            var bestOption = ClubQuizQuestionBank.FindBestOption(
                match.Answer,
                new Dictionary<char, string?>
                {
                    ['A'] = text.OptionA,
                    ['B'] = text.OptionB,
                    ['C'] = text.OptionC,
                    ['D'] = text.OptionD,
                });

            _task.Logger.LogDebug("问题 OCR：{Question}", text.Question);
            _task.Logger.LogDebug(
                "选项 OCR：A={OptionA} B={OptionB} C={OptionC} D={OptionD}",
                text.OptionA,
                text.OptionB,
                text.OptionC,
                text.OptionD);
            _task.Logger.LogDebug(
                "题库匹配：{MatchedQuestion}，相似度 {Score:P1}，答案 {Answer}",
                match.Question,
                match.Score,
                match.Answer);

            if (_task.Options.AnswerDelay > 0)
            {
                await Task.Delay(_task.Options.AnswerDelay, cancellationToken);
            }

            _task.Logger.LogInformation(
                "答题进度：[Gold]{Progress}[/Gold]，选择：[Gold]{Option}[/Gold]。",
                NormalizeProgress(text.Progress),
                bestOption);
            await ClickOptionAsync(bestOption, cancellationToken);
        }

        private TemplateSearchResult FindRoundStart(CancellationToken cancellationToken) =>
            _task.Find(_task.GetImage("quiz_time20"), cancellationToken: cancellationToken);

        private bool TryLocateOptions(CancellationToken cancellationToken)
        {
            using var captureMat = _task.Context.CaptureBgrMat(cancellationToken);
            using var optionMask = ToGrayMask(_task.GetImage("quiz_option_mask"));
            var optionTemplates = new[]
            {
                ('A', _task.GetImage("quiz_option_a")),
                ('B', _task.GetImage("quiz_option_b")),
                ('C', _task.GetImage("quiz_option_c")),
                ('D', _task.GetImage("quiz_option_d")),
            };
            var locatedRegions = new Dictionary<char, Rect>();

            foreach (var (key, template) in optionTemplates)
            {
                var result = _task.Context.TemplateMatching.Search(
                    captureMat,
                    template,
                    new TemplateSearchOptions
                    {
                        Threshold = 0.85,
                        Mask = optionMask,
                    });

                if (result.FirstRegion is not { } region)
                {
                    return false;
                }

                locatedRegions[key] = new Rect(region.X, region.Y, region.Width, region.Height);
            }

            _optionRegions.Clear();
            foreach (var (option, region) in locatedRegions)
            {
                _optionRegions[option] = region;
            }

            _optionsLocated = true;
            ShowLocatedRegions();
            return true;
        }

        private bool TryRefreshQuestionRegion(CancellationToken cancellationToken)
        {
            using var captureMat = _task.Context.CaptureBgrMat(cancellationToken);
            using var binary = Binarize(captureMat, 200);
            var region = DetectApproxRectangle(binary, 1000, 5);
            if (region.Width <= 0 || region.Height <= 0)
            {
                return false;
            }

            _questionRegion = region;
            _questionLocated = true;
            ShowLocatedRegions();
            return true;
        }

        private async Task<ClubQuizRecognizedText> ReadCurrentQuestionAsync(
            CancellationToken cancellationToken)
        {
            var engineType = _task.ResolveOcrEngineType();
            using var captureMat = _task.Context.CaptureBgrMat(cancellationToken);
            using var question = Crop(captureMat, _questionRegion);
            using var optionA = Crop(captureMat, _optionRegions['A']);
            using var optionB = Crop(captureMat, _optionRegions['B']);
            using var optionC = Crop(captureMat, _optionRegions['C']);
            using var optionD = Crop(captureMat, _optionRegions['D']);
            using var progress = Crop(captureMat, _progressRegion);

            return new ClubQuizRecognizedText(
                await _task.RecognizeAsync(question, engineType, cancellationToken),
                await _task.RecognizeAsync(optionA, engineType, cancellationToken),
                await _task.RecognizeAsync(optionB, engineType, cancellationToken),
                await _task.RecognizeAsync(optionC, engineType, cancellationToken),
                await _task.RecognizeAsync(optionD, engineType, cancellationToken),
                await _task.RecognizeAsync(progress, engineType, cancellationToken));
        }

        private async Task ClickOptionAsync(char option, CancellationToken cancellationToken)
        {
            if (!_optionRegions.TryGetValue(option, out var targetRegion) &&
                !_optionRegions.TryGetValue('A', out targetRegion))
            {
                _task.Logger.LogWarning("未找到选项 {Option} 的点击区域。", option);
                return;
            }

            var optionMask = _task.GetImage("quiz_option_mask");
            var centerX = targetRegion.X + optionMask.Width / 4;
            var centerY = targetRegion.Y + optionMask.Height / 2;
            await _task.Context.ClickCanonicalAsync(centerX, centerY, cancellationToken);
        }

        private void ShowLocatedRegions()
        {
            var regions = _optionRegions
                .OrderBy(pair => pair.Key)
                .Select(pair => _task.Context.ToOverlayRegion(
                    ToTemplateRegion(pair.Value),
                    kind: OverlayRegionKind.Ocr))
                .ToList();

            if (_questionLocated)
            {
                regions.Add(_task.Context.ToOverlayRegion(
                    ToTemplateRegion(_questionRegion),
                    kind: OverlayRegionKind.Ocr));
            }

            if (regions.Count > 0)
            {
                _task.Context.Overlay.SetTaskStateRegions(regions);
            }
        }

        private void ShowRecognizedRegions(ClubQuizRecognizedText text)
        {
            var regions = new List<OverlayRegion>();
            AddOcrRegion(regions, _questionRegion, text.Question);
            foreach (var (option, region) in _optionRegions.OrderBy(pair => pair.Key))
            {
                AddOcrRegion(regions, region, option switch
                {
                    'A' => text.OptionA,
                    'B' => text.OptionB,
                    'C' => text.OptionC,
                    'D' => text.OptionD,
                    _ => string.Empty,
                });
            }

            AddOcrRegion(regions, _progressRegion, text.Progress);
            _task.Context.Overlay.SetTaskStateRegions(regions);
        }

        private void AddOcrRegion(List<OverlayRegion> regions, Rect region, string text)
        {
            regions.Add(_task.Context.ToOverlayRegion(
                ToTemplateRegion(region),
                null,
                FormatOcrOverlayText(text),
                OverlayRegionStatusKind.Detail,
                OverlayRegionKind.Ocr));
        }
    }

    private enum GatherRefreshMode
    {
        ChatBox,
        Badge,
    }

    private sealed record ClubQuizRecognizedText(
        string Question,
        string OptionA,
        string OptionB,
        string OptionC,
        string OptionD,
        string Progress);
}

internal sealed class AutoClubQuizTaskFactory : IAutomationTaskFactory
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
