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

public sealed class AutoCookingTask : AutomationTaskBase<CookingTaskOptions>
{
    private const string ImageDirectory = "Assets/Tasks/Cooking/Image";
    private const string ConfigDirectory = "Assets/Tasks/Cooking/Config";
    private const int DefaultCookingGapMilliseconds = 100;

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

    private readonly Dictionary<string, Mat> _images;
    private readonly IReadOnlyList<AutomationTaskStateRule<CookingState>> _stateRules;
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

    private int _stateValue = (int)CookingState.Unknown;
    private bool _initialized;
    private bool _unknownLogged;
    private int _round;
    private Point _nextOrder = new(400, 130);

    public AutoCookingTask(
        CookingTaskOptions options,
        ILogger<AutoCookingTask> logger)
        : base(options, logger)
    {
        _images = LoadImagesFromDirectory(ImageDirectory, ImreadModes.Unchanged, recursive: true);
        var configStore = CookingDishConfigStore.Load(Path.Combine(AppContext.BaseDirectory, ConfigDirectory));
        _dishConfig = configStore.GetRequired(options.Dish);
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

    private CookingState CurrentState
    {
        get => (CookingState)Volatile.Read(ref _stateValue);
        set => Volatile.Write(ref _stateValue, (int)value);
    }

    protected override async Task ExecuteAsync(
        AutomationTaskContext context,
        CancellationToken cancellationToken)
    {
        CurrentState = CookingState.Unknown;
        ClearCookingState();
        _round = 0;
        _unknownLogged = false;

        var monitorInterval = TimeSpan.FromMilliseconds(
            Math.Max(context.RuntimeOptions?.StateMonitorInterval ?? 200, 50));
        StartStateMonitor(
            _stateRules,
            OnStateDetected,
            CookingState.Unknown,
            "烹饪-未知状态",
            monitorInterval);

        while (!cancellationToken.IsCancellationRequested && _round < Options.Times)
        {
            await ExecuteLoopAsync(cancellationToken);
        }

        if (_round >= Options.Times)
        {
            AppNotificationHelper.Show("烹饪任务完成", $"已完成 {Options.Times} 轮烹饪任务。");
            Logger.LogInformation(
                "[Cyan]自动烹饪[/Cyan] 任务完成：共完成 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮。",
                _round,
                Options.Times);
        }
    }

    private async Task ExecuteLoopAsync(CancellationToken cancellationToken)
    {
        switch (CurrentState)
        {
            case CookingState.Unknown:
                await HandleUnknownStateAsync(cancellationToken);
                break;

            case CookingState.Workbench:
                await TryClickNamedTemplateAsync("click_challenge", cancellationToken: cancellationToken);
                await Task.Delay(1000, cancellationToken);
                break;

            case CookingState.Challenge:
                await ChooseDishAsync(cancellationToken);
                await Task.Delay(1500, cancellationToken);
                await TryClickNamedTemplateAsync("click_start", cancellationToken: cancellationToken);
                await Task.Delay(2000, cancellationToken);
                break;

            case CookingState.Cooking:
                await HandleCookingStateAsync(cancellationToken);
                break;

            case CookingState.Summary:
                await HandleSummaryStateAsync(cancellationToken);
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

        if (!_unknownLogged)
        {
            Logger.LogInformation("未识别到烹饪界面，请手动进入烹饪玩法。");
            _unknownLogged = true;
        }

        await Task.Delay(1000, cancellationToken);
    }

    private async Task HandleCookingStateAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            if (!InitializeCooking(cancellationToken))
            {
                await Task.Delay(500, cancellationToken);
                return;
            }

            await Task.Delay(500, cancellationToken);
            return;
        }

        RefreshKitchenwareStatus(cancellationToken);
        if (HasOvercookedKitchenware())
        {
            Logger.LogWarning("检测到厨具糊锅，正在丢弃当前食物并重新处理。");
            await DiscardAllFoodAsync(cancellationToken);
            _completedSteps.Clear();
            _preCookedSteps.Clear();
            await Task.Delay(DefaultCookingGapMilliseconds, cancellationToken);
            return;
        }

        await ExecuteCookingCycleAsync(cancellationToken);
        await Task.Delay(DefaultCookingGapMilliseconds, cancellationToken);
    }

    private async Task HandleSummaryStateAsync(CancellationToken cancellationToken)
    {
        _round++;
        ClearCookingState();
        Logger.LogInformation(
            "第 [Gold]{Round}[/Gold]/[Gold]{Total}[/Gold] 轮 [Cyan]自动烹饪[/Cyan] 已完成。",
            _round,
            Options.Times);

        await Task.Delay(3000, cancellationToken);
        await Context.SendSpaceAsync(cancellationToken);
        await Task.Delay(3000, cancellationToken);
        await Context.SendSpaceAsync(cancellationToken);
        await Task.Delay(3000, cancellationToken);
    }

    private void OnStateDetected(CookingState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        if (newState != CookingState.Unknown)
        {
            ResetUnknownBuffer();
            _unknownLogged = false;
        }

        if (oldState == CookingState.Cooking && newState != CookingState.Cooking)
        {
            ClearCookingState();
        }
    }

    private async Task<bool> ChooseDishAsync(CancellationToken cancellationToken)
    {
        var dishKey = NormalizeImageKey(_dishConfig.ImagePath);
        var result = Find(
            GetImage(dishKey),
            AlphaSearchOptions,
            cancellationToken);
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

    private bool InitializeCooking(CancellationToken cancellationToken)
    {
        Logger.LogDebug("开始定位烹饪元素：{DishName}。", _dishConfig.Name);

        _kitchenwareRects.Clear();
        _kitchenwareCenters.Clear();
        _ingredientRects.Clear();
        _ingredientCenters.Clear();
        _condimentRects.Clear();
        _condimentCenters.Clear();
        _kitchenwareStatus.Clear();

        using var captureMat = Context.CaptureBgrMat(cancellationToken);
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
                ingredient => GetImage($"Ingredients/{ingredient}"),
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
                condiment => GetImage($"Condiment/{condiment}"),
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
        Logger.LogInformation("[Cyan]{DishName}[/Cyan] 烹饪元素定位完成。", _dishConfig.Name);
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
            var result = Context.TemplateMatching.Search(source, template, searchOptions);
            if (result.FirstRegion is not { } region)
            {
                Logger.LogDebug("{ItemType} {Item} 定位失败。", itemType, item);
                return false;
            }

            rects[item] = new Rect(region.X, region.Y, region.Width, region.Height);
            centers[item] = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
            Logger.LogDebug("{ItemType} {Item} 定位成功。", itemType, item);
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
        ResetDefaultOrder();

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

        Logger.LogDebug("将食材 {Ingredient} 放入厨具 {Kitchenware}。", ingredient, kitchenware);
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

        Logger.LogDebug("将食物从厨具 {Kitchenware} 移到砧板。", kitchenware);
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
            Logger.LogDebug("已预烹饪 {Count} 份食材。", preCookedCount);
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

        await DragAsync(_kitchenwareCenters["board"], _nextOrder, cancellationToken);
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

    private void ResetDefaultOrder()
    {
        _nextOrder = new Point(400, 130);
        _condimentCounts.Clear();
        foreach (var condiment in _dishConfig.RequiredCondiments)
        {
            _condimentCounts[condiment] = 1;
        }
    }

    private void RefreshKitchenwareStatus(CancellationToken cancellationToken)
    {
        using var captureMat = Context.CaptureBgrMat(cancellationToken);
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
        using var ringMask = ToGrayMask(GetImage($"Kitchenware/{kitchenware}_ring"));

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
        !Find(GetImage("ui_clock"), cancellationToken: cancellationToken).Success;

    private async Task DragAsync(Point start, Point end, CancellationToken cancellationToken) =>
        await Context.DragCanonicalAsync(start.X, start.Y, end.X, end.Y, 100, cancellationToken);

    private void ClearCookingState()
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
        ClearTaskStateRegions();
    }

    private void UpdateCookingOverlay()
    {
        var regions = new List<OverlayRegion>();
        regions.AddRange(_ingredientRects.Select(pair => Context.ToOverlayRegion(ToTemplateRegion(pair.Value), pair.Key)));
        regions.AddRange(_condimentRects.Select(pair => Context.ToOverlayRegion(ToTemplateRegion(pair.Value), pair.Key)));

        foreach (var (kitchenware, rect) in _kitchenwareRects)
        {
            _kitchenwareStatus.TryGetValue(kitchenware, out var progress);
            regions.Add(Context.ToOverlayRegion(
                ToTemplateRegion(rect),
                kitchenware,
                FormatCookingStatus(progress)));
        }

        Context.Overlay.SetTaskStateRegions(regions);
    }

    private Mat GetKitchenwareImage(string kitchenware) =>
        kitchenware switch
        {
            "bin" => GetImage("Section/bin"),
            "board" => GetImage("Section/board"),
            _ => GetImage($"Kitchenware/{kitchenware}"),
        };

    private Mat GetImage(string name) =>
        _images.TryGetValue(NormalizeImageKey(name), out var image)
            ? image
            : throw new KeyNotFoundException($"Task image was not loaded: {name}");

    private Task<bool> TryClickNamedTemplateAsync(
        string name,
        double threshold = 0.9,
        CancellationToken cancellationToken = default) =>
        TryClickTemplateAsync(GetImage(name), threshold, cancellationToken);

    private static string NormalizeImageKey(string imagePath) =>
        Path.ChangeExtension(imagePath, null)?.Replace('\\', '/') ?? imagePath.Replace('\\', '/');

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

public sealed class AutoCookingTaskFactory : IAutomationTaskFactory
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
