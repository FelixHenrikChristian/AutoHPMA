using AutoHPMA.Contracts.Tasks;
using AutoHPMA.Core.Models;
using AutoHPMA.Core.Services;
using AutoHPMA.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AutoHPMA.Tasks.Permanent;

internal enum CookingState
{
    Unknown,
    Workbench,
    Challenge,
    Cooking,
    Summary,
}

internal enum CookingStatus
{
    Idle,
    Cooking,
    Cooked,
    Overcooked,
}

internal sealed class AutoCookingTask :
    StateMachineAutomationTaskBase<CookingTaskOptions, CookingState>
{
    private const string ImageDirectory = "Assets/Tasks/Cooking/Image";
    private const string ConfigDirectory = "Assets/Tasks/Cooking/Config";
    private const int DefaultLoopDelayMilliseconds = 1000;
    private const int DefaultCookingGapMilliseconds = 100;
    private const int ChallengeDishDelayMilliseconds = 1500;
    private const int ChallengeStartDelayMilliseconds = 2000;
    private const int SummaryDelayMilliseconds = 3000;

    private static readonly double[] StateScaleFactors = [1d, 0.98d, 1.02d, 0.95d, 1.05d];
    private static readonly TemplateSearchOptions StateSearchOptions = new()
    {
        Threshold = 0.88,
        ScaleFactors = StateScaleFactors,
    };
    private static readonly TemplateSearchOptions AlphaSearchOptions = new()
    {
        Threshold = 0.9,
        UseAlphaMask = true,
    };
    private static readonly TemplateSearchOptions SqDiffAlphaSearchOptions = new()
    {
        Threshold = 0.9,
        UseAlphaMask = true,
        MatchMode = TemplateMatchModes.SqDiffNormed,
    };

    private readonly IReadOnlyList<AutomationTaskStateRule<CookingState>> _stateRules;
    private readonly CookingDishConfig _dishConfig;
    private readonly CookingSession _session;
    private bool _unknownLogged;
    private bool _summaryHandled;
    private int _round;

    public AutoCookingTask(
        CookingTaskOptions options,
        ILogger<AutoCookingTask> logger)
        : base(options, logger, CookingState.Unknown)
    {
        LoadTaskImages(ImageDirectory, ImreadModes.Unchanged, recursive: true);

        var configStore = CookingDishConfigStore.Load(Path.Combine(AppContext.BaseDirectory, ConfigDirectory));
        _dishConfig = configStore.GetRequired(options.Dish);
        _session = new CookingSession(this, _dishConfig);
        _stateRules =
        [
            new([GetImage("ui_clock")], CookingState.Cooking, "烹饪-烹饪中", SearchOptions: StateSearchOptions),
            new([GetImage("ui_shop")], CookingState.Workbench, "烹饪-工作台", SearchOptions: StateSearchOptions),
            new([GetImage("ui_challenge")], CookingState.Challenge, "烹饪-订单挑战", SearchOptions: StateSearchOptions),
            new([GetImage("ui_continue")], CookingState.Summary, "烹饪-结算中", SearchOptions: StateSearchOptions),
        ];

        UnknownBufferMilliseconds = 2000;
        Logger.LogDebug("已加载烹饪配置：{DishName}。", _dishConfig.Name);
    }

    public override AutomationTaskType TaskType => AutomationTaskType.AutoCooking;

    public override string DisplayName => "自动烹饪";

    protected override CookingState UnknownState => CookingState.Unknown;

    protected override string UnknownDisplayName => "烹饪-未知状态";

    protected override IReadOnlyList<AutomationTaskStateRule<CookingState>> StateRules => _stateRules;

    protected override Task InitializeStateMachineAsync(CancellationToken cancellationToken)
    {
        _round = 0;
        _unknownLogged = false;
        _summaryHandled = false;
        _session.Clear();
        return Task.CompletedTask;
    }

    protected override bool ShouldContinue(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _round < Options.Times;

    protected override async Task HandleStateAsync(
        CookingState state,
        CancellationToken cancellationToken)
    {
        switch (state)
        {
            case CookingState.Unknown:
                await HandleUnknownStateAsync(cancellationToken);
                break;
            case CookingState.Workbench:
                await HandleWorkbenchStateAsync(cancellationToken);
                break;
            case CookingState.Challenge:
                await HandleChallengeStateAsync(cancellationToken);
                break;
            case CookingState.Cooking:
                await _session.TickAsync(cancellationToken);
                break;
            case CookingState.Summary:
                await HandleSummaryStateAsync(cancellationToken);
                break;
        }
    }

    protected override void OnDetectedStateChanged(CookingState previousState, CookingState currentState)
    {
        base.OnDetectedStateChanged(previousState, currentState);
        if (currentState != CookingState.Unknown)
        {
            _unknownLogged = false;
        }

        if (currentState != CookingState.Summary)
        {
            _summaryHandled = false;
        }

        if (previousState == CookingState.Cooking && currentState != CookingState.Cooking)
        {
            _session.Clear();
        }
    }

    protected override Task OnStateMachineCompletedAsync(CancellationToken cancellationToken)
    {
        if (_round < Options.Times)
        {
            return Task.CompletedTask;
        }

        AppNotificationHelper.Show("烹饪任务完成", $"已完成 {Options.Times} 轮烹饪任务。");
        Logger.LogInformation(
            "[Cyan]自动烹饪[/Cyan] 任务完成：共完成 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮。",
            _round,
            Options.Times);
        return Task.CompletedTask;
    }

    private async Task HandleUnknownStateAsync(CancellationToken cancellationToken)
    {
        if (!IsUnknownBufferElapsed())
        {
            await DelayAsync(cancellationToken, DefaultLoopDelayMilliseconds);
            return;
        }

        if (!_unknownLogged)
        {
            Logger.LogInformation("未识别到烹饪界面，请手动进入烹饪玩法。");
            _unknownLogged = true;
        }

        await DelayAsync(cancellationToken, DefaultLoopDelayMilliseconds);
    }

    private async Task HandleWorkbenchStateAsync(CancellationToken cancellationToken)
    {
        await TryClickNamedTemplateAsync("click_challenge", cancellationToken: cancellationToken);
        await DelayAsync(cancellationToken, DefaultLoopDelayMilliseconds);
    }

    private async Task HandleChallengeStateAsync(CancellationToken cancellationToken)
    {
        await ChooseDishAsync(cancellationToken);
        await DelayAsync(cancellationToken, ChallengeDishDelayMilliseconds);
        await TryClickNamedTemplateAsync("click_start", cancellationToken: cancellationToken);
        await DelayAsync(cancellationToken, ChallengeStartDelayMilliseconds);
    }

    private async Task HandleSummaryStateAsync(CancellationToken cancellationToken)
    {
        if (_summaryHandled)
        {
            await DelayAsync(cancellationToken, DefaultLoopDelayMilliseconds);
            return;
        }

        _summaryHandled = true;
        _round++;
        _session.Clear();
        Logger.LogInformation(
            "第 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮 [Cyan]自动烹饪[/Cyan] 已完成。",
            _round,
            Options.Times);

        await DelayAsync(cancellationToken, SummaryDelayMilliseconds);
        await Context.SendSpaceAsync(cancellationToken);
        await DelayAsync(cancellationToken, SummaryDelayMilliseconds);
        await Context.SendSpaceAsync(cancellationToken);
        await DelayAsync(cancellationToken, SummaryDelayMilliseconds);
    }

    private async Task<bool> ChooseDishAsync(CancellationToken cancellationToken)
    {
        var result = Find(GetImage(_dishConfig.ImagePath), AlphaSearchOptions, cancellationToken);
        if (result.FirstRegion is not { } region)
        {
            Logger.LogWarning("未找到菜品入口：{DishName}。", _dishConfig.Name);
            return false;
        }

        ShowMatchRegions(result);
        await Context.ClickMatchCenterAsync(region, cancellationToken);
        Logger.LogInformation("已选择菜品：[Cyan]{DishName}[/Cyan]。", _dishConfig.Name);
        return true;
    }

    private sealed class CookingSession
    {
        private readonly AutoCookingTask _task;
        private readonly CookingDishConfig _dishConfig;
        private readonly Dictionary<string, Rect> _kitchenwareRects = [];
        private readonly Dictionary<string, Point> _kitchenwareCenters = [];
        private readonly Dictionary<string, Rect> _ingredientRects = [];
        private readonly Dictionary<string, Point> _ingredientCenters = [];
        private readonly Dictionary<string, Rect> _condimentRects = [];
        private readonly Dictionary<string, Point> _condimentCenters = [];
        private readonly Dictionary<string, CookingProgress> _kitchenwareStatus = [];
        private readonly HashSet<int> _completedSteps = [];
        private readonly HashSet<int> _preCookedSteps = [];
        private readonly Dictionary<string, int> _condimentCounts = [];
        private bool _initialized;

        public CookingSession(AutoCookingTask task, CookingDishConfig dishConfig)
        {
            _task = task;
            _dishConfig = dishConfig;
        }

        public void Clear()
        {
            _initialized = false;
            _kitchenwareRects.Clear();
            _kitchenwareCenters.Clear();
            _ingredientRects.Clear();
            _ingredientCenters.Clear();
            _condimentRects.Clear();
            _condimentCenters.Clear();
            _kitchenwareStatus.Clear();
            _completedSteps.Clear();
            _preCookedSteps.Clear();
            _condimentCounts.Clear();
            _task.ClearTaskStateRegions();
        }

        public async Task TickAsync(CancellationToken cancellationToken)
        {
            if (!_initialized)
            {
                if (!Initialize(cancellationToken))
                {
                    await DelayAsync(cancellationToken, 500);
                    return;
                }

                await DelayAsync(cancellationToken, 500);
                return;
            }

            RefreshKitchenwareStatus(cancellationToken);
            if (HasOvercookedKitchenware())
            {
                _task.Logger.LogWarning("检测到厨具糊锅，正在丢弃当前食物并重新处理。");
                await DiscardAllFoodAsync(cancellationToken);
                _completedSteps.Clear();
                _preCookedSteps.Clear();
                await DelayAsync(cancellationToken, DefaultCookingGapMilliseconds);
                return;
            }

            await ExecuteCookingCycleAsync(cancellationToken);
            await DelayAsync(cancellationToken, DefaultCookingGapMilliseconds);
        }

        private bool Initialize(CancellationToken cancellationToken)
        {
            _task.Logger.LogDebug("开始定位烹饪元素：{DishName}。", _dishConfig.Name);

            _kitchenwareRects.Clear();
            _kitchenwareCenters.Clear();
            _ingredientRects.Clear();
            _ingredientCenters.Clear();
            _condimentRects.Clear();
            _condimentCenters.Clear();
            _kitchenwareStatus.Clear();

            using var captureMat = _task.Context.CaptureBgrMat(cancellationToken);
            if (!LocateItems(
                    captureMat,
                    _dishConfig.RequiredKitchenware,
                    GetKitchenwareImage,
                    _kitchenwareRects,
                    _kitchenwareCenters,
                    "厨具",
                    AlphaSearchOptions))
            {
                return false;
            }

            if (!LocateItems(
                    captureMat,
                    _dishConfig.RequiredIngredients,
                    ingredient => _task.GetImage($"Ingredients/{ingredient}"),
                    _ingredientRects,
                    _ingredientCenters,
                    "食材",
                    SqDiffAlphaSearchOptions))
            {
                return false;
            }

            if (!LocateItems(
                    captureMat,
                    _dishConfig.RequiredCondiments,
                    condiment => _task.GetImage($"Condiment/{condiment}"),
                    _condimentRects,
                    _condimentCenters,
                    "调料",
                    SqDiffAlphaSearchOptions))
            {
                return false;
            }

            _completedSteps.Clear();
            _preCookedSteps.Clear();
            _condimentCounts.Clear();
            _initialized = true;
            RefreshKitchenwareStatus(cancellationToken);
            _task.Logger.LogInformation("[Cyan]{DishName}[/Cyan] 烹饪元素定位完成。", _dishConfig.Name);
            return true;
        }

        private bool LocateItems(
            Mat source,
            IReadOnlyList<string> items,
            Func<string, Mat> getTemplate,
            Dictionary<string, Rect> rects,
            Dictionary<string, Point> centers,
            string itemType,
            TemplateSearchOptions searchOptions)
        {
            foreach (var item in items.Distinct(StringComparer.Ordinal))
            {
                var template = getTemplate(item);
                var result = _task.Context.TemplateMatching.Search(source, template, searchOptions);
                if (result.FirstRegion is not { } region)
                {
                    _task.Logger.LogDebug("{ItemType} {Item} 定位失败。", itemType, item);
                    return false;
                }

                rects[item] = new Rect(region.X, region.Y, region.Width, region.Height);
                centers[item] = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
                _task.Logger.LogDebug("{ItemType} {Item} 定位成功。", itemType, item);
            }

            return true;
        }

        private async Task ExecuteCookingCycleAsync(CancellationToken cancellationToken)
        {
            if (IsAllStepsCompleted())
            {
                await ExecuteFinalStageAsync(cancellationToken);
                return;
            }

            await ExecuteNextCookingStepAsync(cancellationToken);
        }

        private bool IsAllStepsCompleted()
        {
            if (_completedSteps.Count < _dishConfig.CookingSteps.Count)
            {
                return false;
            }

            foreach (var kitchenware in _dishConfig.RequiredKitchenware)
            {
                if (IsStaticKitchenware(kitchenware))
                {
                    continue;
                }

                if (_kitchenwareStatus.TryGetValue(kitchenware, out var progress) &&
                    progress.Status is CookingStatus.Cooking or CookingStatus.Cooked)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task ExecuteNextCookingStepAsync(CancellationToken cancellationToken)
        {
            for (var index = 0; index < _dishConfig.CookingSteps.Count; index++)
            {
                var step = _dishConfig.CookingSteps[index];
                var targetKitchenware = step.TargetKitchenware;
                if (!_kitchenwareStatus.TryGetValue(targetKitchenware, out var progress))
                {
                    continue;
                }

                switch (progress.Status)
                {
                    case CookingStatus.Idle:
                        if (!_completedSteps.Contains(index) && !_preCookedSteps.Contains(index))
                        {
                            await PlaceIngredientInKitchenwareAsync(step.Ingredient, targetKitchenware, cancellationToken);
                            _completedSteps.Add(index);
                        }
                        return;

                    case CookingStatus.Cooked:
                        await MoveFromKitchenwareToBoardAsync(targetKitchenware, cancellationToken);
                        if (_preCookedSteps.Remove(index))
                        {
                            _completedSteps.Add(index);
                        }
                        return;
                }
            }
        }

        private async Task ExecuteFinalStageAsync(CancellationToken cancellationToken)
        {
            _completedSteps.Clear();
            ResetOrder();

            if (IsCookingOver(cancellationToken))
            {
                return;
            }

            await StartPreCookingAsync(cancellationToken);
            await SeasoningAsync(cancellationToken);
            if (IsCookingOver(cancellationToken))
            {
                return;
            }

            await SubmitOrderAsync(cancellationToken);
        }

        private async Task PlaceIngredientInKitchenwareAsync(
            string ingredient,
            string kitchenware,
            CancellationToken cancellationToken)
        {
            if (IsCookingOver(cancellationToken))
            {
                return;
            }

            _task.Logger.LogDebug("将食材 {Ingredient} 放入厨具 {Kitchenware}。", ingredient, kitchenware);
            await DragAsync(_ingredientCenters[ingredient], _kitchenwareCenters[kitchenware], cancellationToken);
        }

        private async Task MoveFromKitchenwareToBoardAsync(
            string kitchenware,
            CancellationToken cancellationToken)
        {
            if (IsCookingOver(cancellationToken))
            {
                return;
            }

            _task.Logger.LogDebug("将食物从厨具 {Kitchenware} 移到砧板。", kitchenware);
            await DragAsync(_kitchenwareCenters[kitchenware], _kitchenwareCenters["board"], cancellationToken);
        }

        private async Task StartPreCookingAsync(CancellationToken cancellationToken)
        {
            var usedKitchenware = new HashSet<string>(StringComparer.Ordinal);
            var preCookedCount = 0;
            const int maxPreCookItems = 2;

            for (var index = 0; index < _dishConfig.CookingSteps.Count && preCookedCount < maxPreCookItems; index++)
            {
                var step = _dishConfig.CookingSteps[index];
                if (_preCookedSteps.Contains(index) || usedKitchenware.Contains(step.TargetKitchenware))
                {
                    continue;
                }

                if (_kitchenwareStatus.TryGetValue(step.TargetKitchenware, out var progress) &&
                    progress.Status == CookingStatus.Idle)
                {
                    await PlaceIngredientInKitchenwareAsync(step.Ingredient, step.TargetKitchenware, cancellationToken);
                    _preCookedSteps.Add(index);
                    usedKitchenware.Add(step.TargetKitchenware);
                    preCookedCount++;
                }
            }

            if (preCookedCount > 0)
            {
                _task.Logger.LogDebug("已预烹饪 {Count} 份食材。", preCookedCount);
            }
        }

        private async Task SeasoningAsync(CancellationToken cancellationToken)
        {
            foreach (var condiment in _dishConfig.RequiredCondiments)
            {
                if (!_condimentCounts.TryGetValue(condiment, out var count))
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    if (IsCookingOver(cancellationToken))
                    {
                        return;
                    }

                    await DragAsync(_condimentCenters[condiment], _kitchenwareCenters["board"], cancellationToken);
                }
            }
        }

        private async Task SubmitOrderAsync(CancellationToken cancellationToken)
        {
            if (IsCookingOver(cancellationToken))
            {
                return;
            }

            var nextOrder = _task.Context.TaskCoordinates.GetRequiredPoint(
                TaskCoordinateIds.CookingNextOrder);
            await DragAsync(
                _kitchenwareCenters["board"],
                new Point(nextOrder.X, nextOrder.Y),
                cancellationToken);
        }

        private async Task DiscardAllFoodAsync(CancellationToken cancellationToken)
        {
            foreach (var kitchenware in _dishConfig.RequiredKitchenware)
            {
                if (IsStaticKitchenware(kitchenware))
                {
                    continue;
                }

                if (IsCookingOver(cancellationToken))
                {
                    return;
                }

                await DragAsync(_kitchenwareCenters[kitchenware], _kitchenwareCenters["bin"], cancellationToken);
            }

            if (!IsCookingOver(cancellationToken))
            {
                await DragAsync(_kitchenwareCenters["board"], _kitchenwareCenters["bin"], cancellationToken);
            }
        }

        private void ResetOrder()
        {
            _condimentCounts.Clear();
            foreach (var condiment in _dishConfig.RequiredCondiments)
            {
                _condimentCounts[condiment] = 1;
            }
        }

        private void RefreshKitchenwareStatus(CancellationToken cancellationToken)
        {
            using var captureMat = _task.Context.CaptureBgrMat(cancellationToken);
            foreach (var kitchenware in _dishConfig.RequiredKitchenware)
            {
                if (IsStaticKitchenware(kitchenware))
                {
                    continue;
                }

                _kitchenwareStatus[kitchenware] = GetKitchenwareStatus(captureMat, kitchenware);
            }

            UpdateCookingOverlay();
        }

        private CookingProgress GetKitchenwareStatus(Mat captureMat, string kitchenware)
        {
            if (!_kitchenwareRects.TryGetValue(kitchenware, out var rect))
            {
                return new CookingProgress(CookingStatus.Idle, 0);
            }

            using var region = Crop(captureMat, rect);
            using var ringMask = ToGrayMask(_task.GetImage($"Kitchenware/{kitchenware}_ring"));

            var completedPercentage = CalculateColorMatchPercentage(region, ringMask, "ed5432", 5);
            if (completedPercentage > 0)
            {
                return completedPercentage > 95
                    ? new CookingProgress(CookingStatus.Overcooked, completedPercentage)
                    : new CookingProgress(CookingStatus.Cooked, completedPercentage);
            }

            var cookingPercentage = CalculateColorMatchPercentage(region, ringMask, "f6b622", 5);
            return cookingPercentage > 0
                ? new CookingProgress(CookingStatus.Cooking, cookingPercentage)
                : new CookingProgress(CookingStatus.Idle, 0);
        }

        private bool HasOvercookedKitchenware() =>
            _dishConfig.RequiredKitchenware
                .Where(kitchenware => !IsStaticKitchenware(kitchenware))
                .Any(kitchenware =>
                    _kitchenwareStatus.TryGetValue(kitchenware, out var progress) &&
                    progress.Status == CookingStatus.Overcooked);

        private bool IsCookingOver(CancellationToken cancellationToken) =>
            !_task.Find(_task.GetImage("ui_clock"), cancellationToken: cancellationToken).Success;

        private async Task DragAsync(Point start, Point end, CancellationToken cancellationToken) =>
            await _task.Context.DragCanonicalAsync(start.X, start.Y, end.X, end.Y, 100, cancellationToken);

        private void UpdateCookingOverlay()
        {
            var regions = new List<OverlayRegion>();
            regions.AddRange(_ingredientRects.Select(pair => _task.Context.ToOverlayRegion(ToTemplateRegion(pair.Value), pair.Key)));
            regions.AddRange(_condimentRects.Select(pair => _task.Context.ToOverlayRegion(ToTemplateRegion(pair.Value), pair.Key)));

            foreach (var (kitchenware, rect) in _kitchenwareRects)
            {
                _kitchenwareStatus.TryGetValue(kitchenware, out var progress);
                regions.Add(_task.Context.ToOverlayRegion(
                    ToTemplateRegion(rect),
                    kitchenware,
                    FormatCookingStatus(progress)));
            }

            _task.Context.Overlay.SetTaskStateRegions(regions);
        }

        private Mat GetKitchenwareImage(string kitchenware) =>
            kitchenware switch
            {
                "bin" => _task.GetImage("Section/bin"),
                "board" => _task.GetImage("Section/board"),
                _ => _task.GetImage($"Kitchenware/{kitchenware}"),
            };

        private static bool IsStaticKitchenware(string kitchenware) =>
            string.Equals(kitchenware, "bin", StringComparison.Ordinal) ||
            string.Equals(kitchenware, "board", StringComparison.Ordinal);

        private static TemplateMatchRegion ToTemplateRegion(Rect rect) =>
            new(rect.X, rect.Y, rect.Width, rect.Height);

        private static string FormatCookingStatus(CookingProgress progress) =>
            progress.Status switch
            {
                CookingStatus.Cooking => $"烹饪中 {progress.Progress:F0}%",
                CookingStatus.Cooked => "已完成",
                CookingStatus.Overcooked => "糊了",
                _ => "空闲",
            };

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
            Cv2.Threshold(gray, gray, 127, 255, ThresholdTypes.Binary);
            return gray;
        }

        private static double CalculateColorMatchPercentage(
            Mat sourceMat,
            Mat maskMat,
            string targetColorHex,
            int colorThreshold)
        {
            using var grayMask = EnsureMaskSize(maskMat, sourceMat.Size());
            var maskWhitePixels = Cv2.CountNonZero(grayMask);
            if (maskWhitePixels == 0)
            {
                return 0;
            }

            var (red, green, blue) = ParseHexColor(targetColorHex);
            using var targetBgr = new Mat(1, 1, MatType.CV_8UC3, new Scalar(blue, green, red));
            using var targetHsv = new Mat();
            Cv2.CvtColor(targetBgr, targetHsv, ColorConversionCodes.BGR2HSV);
            var target = targetHsv.Get<Vec3b>(0, 0);

            using var hsvMat = new Mat();
            Cv2.CvtColor(sourceMat, hsvMat, ColorConversionCodes.BGR2HSV);
            using var colorMask = new Mat();
            Cv2.InRange(
                hsvMat,
                new Scalar(
                    Math.Max(0, target.Item0 - colorThreshold),
                    Math.Max(50, target.Item1 - 50),
                    Math.Max(50, target.Item2 - 50)),
                new Scalar(Math.Min(180, target.Item0 + colorThreshold), 255, 255),
                colorMask);

            using var finalMask = new Mat();
            Cv2.BitwiseAnd(colorMask, grayMask, finalMask);
            return Cv2.CountNonZero(finalMask) / (double)maskWhitePixels * 100d;
        }

        private static Mat EnsureMaskSize(Mat mask, Size size)
        {
            if (mask.Size() == size)
            {
                return mask.Clone();
            }

            var resized = new Mat();
            Cv2.Resize(mask, resized, size, 0, 0, InterpolationFlags.Nearest);
            return resized;
        }

        private static (byte Red, byte Green, byte Blue) ParseHexColor(string hex)
        {
            var value = hex.Trim().TrimStart('#');
            return (
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value[2..4], 16),
                Convert.ToByte(value[4..6], 16));
        }

        private readonly record struct CookingProgress(CookingStatus Status, double Progress);
    }
}

internal sealed class AutoCookingTaskFactory : IAutomationTaskFactory
{
    private readonly ILogger<AutoCookingTask> _logger;

    public AutoCookingTaskFactory(ILogger<AutoCookingTask> logger)
    {
        _logger = logger;
    }

    public AutomationTaskType TaskType => AutomationTaskType.AutoCooking;

    public IAutomationTask Create(AutomationTaskOptions options)
    {
        if (options is not CookingTaskOptions cookingOptions)
        {
            throw new ArgumentException(
                $"Options for {TaskType} must be {nameof(CookingTaskOptions)}.",
                nameof(options));
        }

        return new AutoCookingTask(cookingOptions, _logger);
    }
}
