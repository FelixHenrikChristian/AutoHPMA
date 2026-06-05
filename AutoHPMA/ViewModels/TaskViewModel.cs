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

public partial class TaskViewModel : ObservableRecipient
{
    private const double DefaultAnswerDelay = 0;
    private const double DefaultForbiddenForestTimes = 30;
    private const double DefaultCookingTimes = 2;
    private const string DefaultTeamPosition = "队长";
    private const string DefaultCookingDish = "海鱼黄金焗饭";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IInfoBarNotificationService _infoBar;
    private readonly ILogger<TaskViewModel> _logger;
    private bool _suppressSettingPersistence;

    public TaskViewModel(
        ILocalSettingsService localSettingsService,
        IInfoBarNotificationService infoBar,
        ILogger<TaskViewModel> logger)
    {
        _localSettingsService = localSettingsService;
        _infoBar = infoBar;
        _logger = logger;

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

    public bool TaskCommandsEnabled => false;

    public string StartButtonText => "启动";

    public string StartButtonGlyph => "\uE768";

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
    private void AutoClubQuizToggle() => ShowTaskSkeletonNotice("自动社团答题");

    [RelayCommand]
    private void AutoForbiddenForestToggle() => ShowTaskSkeletonNotice("自动禁林探索");

    [RelayCommand]
    private void AutoCookingToggle() => ShowTaskSkeletonNotice("自动巫师烹饪");

    [RelayCommand]
    private void AutoSweetAdventureToggle() => ShowTaskSkeletonNotice("自动甜蜜冒险");

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

    private void ShowTaskSkeletonNotice(string taskName) =>
        _infoBar.Show(
            InfoBarSeverity.Informational,
            "任务骨架已迁移",
            $"{taskName} 的页面入口和参数已经就位，任务运行逻辑将在下一步接入。");

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

