using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using AutoHPMA.Configuration;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.ViewModels;

public partial class TaskViewModel : ObservableRecipient, IDisposable
{
    private const double DefaultAnswerDelay = 0;
    private const double DefaultForbiddenForestTimes = 30;
    private const double DefaultCookingTimes = 2;
    private const string DefaultTeamPosition = "队长";
    private const string DefaultCookingDish = "海鱼黄金焗饭";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IAutomationTaskRunner _taskRunner;
    private readonly IInfoBarNotificationService _infoBar;
    private readonly ILogger<TaskViewModel> _logger;
    private bool _suppressSettingPersistence;

    public TaskViewModel(
        ILocalSettingsService localSettingsService,
        IAutomationTaskRunner taskRunner,
        IInfoBarNotificationService infoBar,
        ILogger<TaskViewModel> logger)
    {
        _localSettingsService = localSettingsService;
        _taskRunner = taskRunner;
        _infoBar = infoBar;
        _logger = logger;

        _taskRunner.StateChanged += OnTaskRunnerStateChanged;
        CurrentTaskType = _taskRunner.CurrentState.CurrentTaskType;

        TeamPositions =
        [
            "队长",
            "队员",
        ];

        Dishes = [];
        ReloadDishes();
    }

    public ObservableCollection<string> TeamPositions { get; }

    public ObservableCollection<string> Dishes { get; }

    public bool TaskCommandsEnabled => true;

    public string StartButtonText => CurrentTaskType == AutomationTaskType.None ? "启动" : "停止";

    public string StartButtonGlyph => CurrentTaskType == AutomationTaskType.None ? "\uE768" : "\uE711";

    public string AutoClubQuizButtonText => GetTaskButtonText(AutomationTaskType.AutoClubQuiz);

    public string AutoClubQuizButtonGlyph => GetTaskButtonGlyph(AutomationTaskType.AutoClubQuiz);

    public string AutoForbiddenForestButtonText => GetTaskButtonText(AutomationTaskType.AutoForbiddenForest);

    public string AutoForbiddenForestButtonGlyph => GetTaskButtonGlyph(AutomationTaskType.AutoForbiddenForest);

    public string AutoCookingButtonText => GetTaskButtonText(AutomationTaskType.AutoCooking);

    public string AutoCookingButtonGlyph => GetTaskButtonGlyph(AutomationTaskType.AutoCooking);

    public string AutoSweetAdventureButtonText => GetTaskButtonText(AutomationTaskType.AutoSweetAdventure);

    public string AutoSweetAdventureButtonGlyph => GetTaskButtonGlyph(AutomationTaskType.AutoSweetAdventure);

    [ObservableProperty]
    public partial AutomationTaskType CurrentTaskType { get; set; } = AutomationTaskType.None;

    [ObservableProperty]
    public partial double AnswerDelay { get; set; } = DefaultAnswerDelay;

    [ObservableProperty]
    public partial bool JoinOthers { get; set; }

    [ObservableProperty]
    public partial bool StopWhenContributionFull { get; set; }

    [ObservableProperty]
    public partial double AutoForbiddenForestTimes { get; set; } = DefaultForbiddenForestTimes;

    [ObservableProperty]
    public partial string SelectedTeamPosition { get; set; } = DefaultTeamPosition;

    [ObservableProperty]
    public partial double AutoCookingTimes { get; set; } = DefaultCookingTimes;

    [ObservableProperty]
    public partial string AutoCookingSelectedDish { get; set; } = DefaultCookingDish;

    public async Task LoadAsync()
    {
        _taskRunner.StateChanged -= OnTaskRunnerStateChanged;
        _taskRunner.StateChanged += OnTaskRunnerStateChanged;
        CurrentTaskType = _taskRunner.CurrentState.CurrentTaskType;

        _suppressSettingPersistence = true;
        try
        {
            ReloadDishes();

            AnswerDelay = NormalizeNonNegative(await ReadIntAsync(SettingsKeys.TaskAnswerDelay, (int)DefaultAnswerDelay));
            JoinOthers = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.TaskJoinOthers) ?? false;
            StopWhenContributionFull = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.TaskStopWhenContributionFull) ?? false;
            AutoForbiddenForestTimes = NormalizePositive(await ReadIntAsync(SettingsKeys.TaskForbiddenForestTimes, (int)DefaultForbiddenForestTimes));
            SelectedTeamPosition = NormalizeTeamPosition(
                await _localSettingsService.ReadSettingAsync<string>(SettingsKeys.TaskSelectedTeamPosition) ?? DefaultTeamPosition);
            AutoCookingTimes = NormalizePositive(await ReadIntAsync(SettingsKeys.TaskCookingTimes, (int)DefaultCookingTimes));
            AutoCookingSelectedDish = NormalizeDish(
                await _localSettingsService.ReadSettingAsync<string>(SettingsKeys.TaskCookingSelectedDish) ?? DefaultCookingDish);
        }
        finally
        {
            _suppressSettingPersistence = false;
        }
    }

    [RelayCommand]
    private Task AutoClubQuizToggleAsync() =>
        ToggleTaskAsync(
            AutomationTaskType.AutoClubQuiz,
            new ClubQuizTaskOptions(
                (int)NormalizeNonNegative(AnswerDelay),
                JoinOthers,
                StopWhenContributionFull));

    [RelayCommand]
    private Task AutoForbiddenForestToggleAsync() =>
        ToggleTaskAsync(
            AutomationTaskType.AutoForbiddenForest,
            new ForbiddenForestTaskOptions(
                (int)NormalizePositive(AutoForbiddenForestTimes),
                string.Equals(SelectedTeamPosition, TeamPositions[0], StringComparison.Ordinal)));

    [RelayCommand]
    private Task AutoCookingToggleAsync() =>
        ToggleTaskAsync(
            AutomationTaskType.AutoCooking,
            new CookingTaskOptions(
                (int)NormalizePositive(AutoCookingTimes),
                NormalizeDish(AutoCookingSelectedDish)));

    [RelayCommand]
    private Task AutoSweetAdventureToggleAsync() =>
        ToggleTaskAsync(
            AutomationTaskType.AutoSweetAdventure,
            new SweetAdventureTaskOptions());

    [RelayCommand]
    private void OpenQuestionBank()
    {
        var questionBankFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Tasks", "ClubQuiz");
        if (!Directory.Exists(questionBankFolder))
        {
            _infoBar.Show(
                InfoBarSeverity.Warning,
                "题库资源尚未接入",
                "当前输出目录中还没有 Assets/Tasks/ClubQuiz，后续迁移社团答题资源后此入口会可用。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(questionBankFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开题库目录失败：{Folder}", questionBankFolder);
            _infoBar.Show(InfoBarSeverity.Error, "打开失败", ex.Message);
        }
    }

    partial void OnAnswerDelayChanged(double value) =>
        PersistSetting(SettingsKeys.TaskAnswerDelay, (int)NormalizeNonNegative(value));

    partial void OnJoinOthersChanged(bool value) =>
        PersistSetting(SettingsKeys.TaskJoinOthers, value);

    partial void OnStopWhenContributionFullChanged(bool value) =>
        PersistSetting(SettingsKeys.TaskStopWhenContributionFull, value);

    partial void OnAutoForbiddenForestTimesChanged(double value) =>
        PersistSetting(SettingsKeys.TaskForbiddenForestTimes, (int)NormalizePositive(value));

    partial void OnSelectedTeamPositionChanged(string value) =>
        PersistSetting(SettingsKeys.TaskSelectedTeamPosition, NormalizeTeamPosition(value));

    partial void OnAutoCookingTimesChanged(double value) =>
        PersistSetting(SettingsKeys.TaskCookingTimes, (int)NormalizePositive(value));

    partial void OnAutoCookingSelectedDishChanged(string value) =>
        PersistSetting(SettingsKeys.TaskCookingSelectedDish, NormalizeDish(value));

    partial void OnCurrentTaskTypeChanged(AutomationTaskType value) =>
        NotifyTaskButtonStateChanged();

    public void Dispose()
    {
        _taskRunner.StateChanged -= OnTaskRunnerStateChanged;
    }

    private async Task ToggleTaskAsync(AutomationTaskType taskType, AutomationTaskOptions options)
    {
        try
        {
            var currentState = _taskRunner.CurrentState;
            if (currentState.IsRunning && currentState.CurrentTaskType == taskType)
            {
                await StopTaskAsync();
                return;
            }

            var result = await _taskRunner.StartAsync(new AutomationTaskStartRequest(taskType, options));
            CurrentTaskType = _taskRunner.CurrentState.CurrentTaskType;

            _infoBar.Show(
                result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                result.Title,
                result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "任务切换失败：{TaskType}", taskType);
            _infoBar.Show(InfoBarSeverity.Error, "任务操作失败", ex.Message);
        }
    }

    private async Task StopTaskAsync()
    {
        try
        {
            await _taskRunner.StopAsync();
            CurrentTaskType = _taskRunner.CurrentState.CurrentTaskType;
            _infoBar.Show(InfoBarSeverity.Informational, "已停止", "任务已停止。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止任务失败");
            _infoBar.Show(InfoBarSeverity.Error, "停止失败", ex.Message);
        }
    }

    private void OnTaskRunnerStateChanged(object? sender, EventArgs e)
    {
        var state = _taskRunner.CurrentState;
        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        _ = dispatcherQueue.TryEnqueue(() => CurrentTaskType = state.CurrentTaskType);
    }

    private string GetTaskButtonText(AutomationTaskType taskType) =>
        CurrentTaskType == taskType ? "停止" : "启动";

    private string GetTaskButtonGlyph(AutomationTaskType taskType) =>
        CurrentTaskType == taskType ? "\uE711" : "\uE768";

    private void NotifyTaskButtonStateChanged()
    {
        OnPropertyChanged(nameof(TaskCommandsEnabled));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonGlyph));
        OnPropertyChanged(nameof(AutoClubQuizButtonText));
        OnPropertyChanged(nameof(AutoClubQuizButtonGlyph));
        OnPropertyChanged(nameof(AutoForbiddenForestButtonText));
        OnPropertyChanged(nameof(AutoForbiddenForestButtonGlyph));
        OnPropertyChanged(nameof(AutoCookingButtonText));
        OnPropertyChanged(nameof(AutoCookingButtonGlyph));
        OnPropertyChanged(nameof(AutoSweetAdventureButtonText));
        OnPropertyChanged(nameof(AutoSweetAdventureButtonGlyph));
    }

    private void ReloadDishes()
    {
        var previousSelection = AutoCookingSelectedDish;
        Dishes.Clear();

        var configFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Tasks", "Cooking", "Config");
        if (Directory.Exists(configFolder))
        {
            foreach (var file in Directory.EnumerateFiles(configFolder, "*.json"))
            {
                if (TryReadDishName(file, out var dishName) && !Dishes.Contains(dishName))
                {
                    Dishes.Add(dishName);
                }
            }
        }

        if (Dishes.Count == 0)
        {
            Dishes.Add(DefaultCookingDish);
        }

        AutoCookingSelectedDish = NormalizeDish(previousSelection);
    }

    private static bool TryReadDishName(string file, out string dishName)
    {
        dishName = string.Empty;
        try
        {
            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("Name", out var nameProperty))
            {
                dishName = nameProperty.GetString() ?? string.Empty;
            }
        }
        catch
        {
            dishName = string.Empty;
        }

        return !string.IsNullOrWhiteSpace(dishName);
    }

    private async Task<int> ReadIntAsync(string key, int defaultValue) =>
        await _localSettingsService.ReadSettingAsync<int?>(key) ?? defaultValue;

    private string NormalizeTeamPosition(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TeamPositions.Contains(value)
            ? value
            : DefaultTeamPosition;

    private string NormalizeDish(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Dishes.Contains(value))
        {
            return value;
        }

        return Dishes.FirstOrDefault() ?? DefaultCookingDish;
    }

    private static double NormalizeNonNegative(double value) =>
        Math.Max(0, Math.Round(value));

    private static double NormalizePositive(double value) =>
        Math.Max(1, Math.Round(value));

    private void PersistSetting<T>(string key, T value)
    {
        if (_suppressSettingPersistence)
        {
            return;
        }

        _ = PersistSettingAsync(key, value);
    }

    private async Task PersistSettingAsync<T>(string key, T value)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务页设置失败：{Key}", key);
        }
    }
}

