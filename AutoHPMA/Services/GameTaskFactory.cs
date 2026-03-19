using AutoHPMA.GameTask;
using AutoHPMA.GameTask.Permanent;
using AutoHPMA.GameTask.Temporary;
using AutoHPMA.Services.Interface;
using Microsoft.Extensions.Logging;

namespace AutoHPMA.Services;

public class GameTaskFactory : IGameTaskFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly CookingConfigService _cookingConfigService;
    private readonly IOcrService _ocrService;
    private readonly IAppContextService _appContextService;

    public GameTaskFactory(
        ILoggerFactory loggerFactory,
        CookingConfigService cookingConfigService,
        IOcrService ocrService,
        IAppContextService appContextService)
    {
        _loggerFactory = loggerFactory;
        _cookingConfigService = cookingConfigService;
        _ocrService = ocrService;
        _appContextService = appContextService;
    }

    public IGameTask CreateAutoClubQuiz(
        nint displayHwnd,
        nint gameHwnd,
        int answerDelay,
        bool joinOthers,
        bool stopWhenContributionFull)
    {
        var task = new AutoClubQuiz(
            _loggerFactory.CreateLogger<AutoClubQuiz>(),
            _ocrService,
            _appContextService,
            displayHwnd,
            gameHwnd);

        ConfigureTask(
            task,
            new Dictionary<string, object>
            {
                { "AnswerDelay", answerDelay },
                { "JoinOthers", joinOthers },
                { "StopWhenContributionFull", stopWhenContributionFull },
            },
            nameof(AutoClubQuiz));

        return task;
    }

    public IGameTask CreateAutoForbiddenForest(
        nint displayHwnd,
        nint gameHwnd,
        int times,
        string teamPosition)
    {
        var task = new AutoForbiddenForest(
            _loggerFactory.CreateLogger<AutoForbiddenForest>(),
            _appContextService,
            displayHwnd,
            gameHwnd);

        ConfigureTask(
            task,
            new Dictionary<string, object>
            {
                { "Times", times },
                { "TeamPosition", teamPosition },
            },
            nameof(AutoForbiddenForest));

        return task;
    }

    public IGameTask CreateAutoCooking(
        nint displayHwnd,
        nint gameHwnd,
        int times,
        string dish)
    {
        var task = new AutoCooking(
            _loggerFactory.CreateLogger<AutoCooking>(),
            _cookingConfigService,
            _ocrService,
            _appContextService,
            displayHwnd,
            gameHwnd);

        ConfigureTask(
            task,
            new Dictionary<string, object>
            {
                { "Times", times },
                { "Dish", dish },
            },
            nameof(AutoCooking));

        return task;
    }

    public IGameTask CreateAutoSweetAdventure(nint displayHwnd, nint gameHwnd)
    {
        return new AutoSweetAdventure(
            _loggerFactory.CreateLogger<AutoSweetAdventure>(),
            _appContextService,
            displayHwnd,
            gameHwnd);
    }

    private static void ConfigureTask(IGameTask task, Dictionary<string, object> parameters, string taskName)
    {
        if (!task.SetParameters(parameters))
        {
            throw new InvalidOperationException($"配置任务参数失败: {taskName}");
        }
    }
}
