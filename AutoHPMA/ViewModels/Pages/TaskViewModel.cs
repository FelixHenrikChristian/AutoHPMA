using AutoHPMA.GameTask;
using AutoHPMA.GameTask.Permanent;
using AutoHPMA.GameTask.Temporary;
using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Messages;
using AutoHPMA.Services;
using AutoHPMA.Views.Windows;
using AutoHPMA.Config;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Microsoft.Extensions.Logging;

namespace AutoHPMA.ViewModels.Pages
{
    /// <summary>
    /// 任务类型枚举
    /// </summary>
    public enum TaskType
    {
        None,
        AutoClubQuiz,
        AutoForbiddenForest,
        AutoCooking,
        AutoSweetAdventure
    }

    public partial class TaskViewModel : ObservableObject, INavigationAware
    {
        private readonly AppSettings _settings;
        private readonly ILogger<TaskViewModel> _logger;
        private readonly CookingConfigService _cookingConfigService;

        #region Observable Properties

        [ObservableProperty]
        private TaskType _currentTaskType = TaskType.None;

        [ObservableProperty]
        private int _answerDelay = 0;

        [ObservableProperty]
        private bool _joinOthers = false;

        [ObservableProperty]
        private bool _stopWhenContributionFull = false;

        [ObservableProperty]
        private int _autoForbiddenForestTimes = 30;

        [ObservableProperty]
        private int _autoCookingTimes = 2;

        [ObservableProperty]
        private ObservableCollection<string> _teamPositions = ["队长", "队员"];

        [ObservableProperty]
        private string _selectedTeamPosition = "队长";

        [ObservableProperty]
        private ObservableCollection<string> _dishes = new();

        [ObservableProperty]
        private string _autoCookingSelectedDish = "海鱼黄金焗饭";

        [ObservableProperty]
        private string _autoCookingSelectedOCR = "Tesseract";

        public ObservableCollection<string> OCRs { get; } = new ObservableCollection<string> { "Tesseract", "PaddleOCR" };

        #endregion



        #region 服务引用

        private IntPtr _displayHwnd => AppContextService.Instance.DisplayHwnd;
        private IntPtr _gameHwnd => AppContextService.Instance.GameHwnd;
        private LogWindow? _logWindow => AppContextService.Instance.LogWindow;
        private WindowsGraphicsCapture _capture => AppContextService.Instance.Capture;

        private IGameTask? _currentTask;
        private AppContextService appContextService;

        #endregion

        #region 构造函数

        public TaskViewModel(AppSettings settings, ILogger<TaskViewModel> logger, CookingConfigService cookingConfigService)
        {
            _settings = settings;
            _logger = logger;
            _cookingConfigService = cookingConfigService;

            appContextService = AppContextService.Instance;
            appContextService.PropertyChanged += AppContextService_PropertyChanged;

            // 注册停止所有任务的消息接收器
            WeakReferenceMessenger.Default.Register<StopAllTasksMessage>(this, (r, message) =>
            {
                StopAllRunningTasks();
            });

            // 从设置中加载数据
            LoadSettings();
            LoadDishes();
        }

        private void LoadSettings()
        {
            AnswerDelay = _settings.AnswerDelay;
            JoinOthers = _settings.JoinOthers;
            StopWhenContributionFull = _settings.StopWhenContributionFull;
            AutoForbiddenForestTimes = _settings.AutoForbiddenForestTimes;
            SelectedTeamPosition = _settings.SelectedTeamPosition;
            AutoCookingTimes = _settings.AutoCookingTimes;
            AutoCookingSelectedDish = _settings.AutoCookingSelectedDish;
            AutoCookingSelectedOCR = _settings.AutoCookingSelectedOCR;
        }

        private void LoadDishes()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Cooking/Config");
            if (!Directory.Exists(configPath)) return;

            foreach (var file in Directory.GetFiles(configPath, "*.json"))
            {
                var json = File.ReadAllText(file);
                var config = System.Text.Json.JsonSerializer.Deserialize<Models.Cooking.DishConfig>(json);
                if (config != null)
                {
                    Dishes.Add(config.Name);
                }
            }
        }

        private void AppContextService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 当共享数据有更新时可以在这里处理
        }

        #endregion

        #region 通用任务控制方法

        /// <summary>
        /// 验证必要参数是否就绪
        /// </summary>
        private bool ValidateRequiredParameters() =>
            _gameHwnd != IntPtr.Zero && _displayHwnd != IntPtr.Zero && _capture != null && _logWindow != null;

        /// <summary>
        /// 检查是否有任务正在运行
        /// </summary>
        private bool CheckTaskRunningStatus()
        {
            if (_currentTask != null)
            {
                ShowErrorMessage("已有其他任务正在运行，请先停止当前任务！");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 显示错误消息框
        /// </summary>
        private void ShowErrorMessage(string content)
        {
            var uiMessageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "⚠️ 提示",
                Content = content,
            };
            uiMessageBox.ShowDialogAsync();
        }



        /// <summary>
        /// 订阅任务完成事件
        /// </summary>
        private void SubscribeTaskCompleted()
        {
            _currentTask!.TaskCompleted += (sender, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentTaskType = TaskType.None;
                    _currentTask = null;
                });
            };
        }

        /// <summary>
        /// 通用任务启动方法
        /// </summary>
        private bool StartTask(
            TaskType taskType,
            string taskName,
            Func<IGameTask> createTask,
            Dictionary<string, object>? parameters = null)
        {
            if (CheckTaskRunningStatus()) return false;

            if (!ValidateRequiredParameters())
            {
                ShowErrorMessage("任务启动失败。请先启动截图器!");
                return false;
            }

            SnackbarHelper.ShowSuccess("启动成功", $"{taskName}已启动。");
            CurrentTaskType = taskType;

            _currentTask = createTask();
            if (parameters != null)
            {
                _currentTask.SetParameters(parameters);
            }

            SubscribeTaskCompleted();
            _currentTask.Start();
            return true;
        }

        /// <summary>
        /// 统一的停止任务方法
        /// </summary>
        private void StopTask()
        {
            _currentTask?.Stop();
            _currentTask = null;
            CurrentTaskType = TaskType.None;
            GC.Collect();
        }

        /// <summary>
        /// 停止所有正在运行的任务
        /// </summary>
        private void StopAllRunningTasks()
        {
            if (_currentTask != null)
            {
                _logger.LogInformation("收到停止信号，正在停止当前任务...");
                StopTask();
            }
        }

        #endregion

        #region 任务启动/停止命令

        // 公共方法供热键调用
        public void ToggleAutoClubQuiz() => OnAutoClubQuizToggle();
        public void ToggleAutoForbiddenForest() => OnAutoForbiddenForestToggle();
        public void ToggleAutoCooking() => OnAutoCookingToggle();
        public void ToggleAutoSweetAdventure() => OnAutoSweetAdventureToggle();

        [RelayCommand]
        private void OnAutoClubQuizToggle()
        {
            if (CurrentTaskType == TaskType.AutoClubQuiz)
                StopTask();
            else
                StartTask(
                    TaskType.AutoClubQuiz,
                    "自动社团答题",
                    () => new AutoClubQuiz(App.GetLogger<AutoClubQuiz>(), _displayHwnd, _gameHwnd),
                    new Dictionary<string, object>
                    {
                        { "AnswerDelay", AnswerDelay },
                        { "JoinOthers", JoinOthers },
                        { "StopWhenContributionFull", StopWhenContributionFull }
                    });
        }

        [RelayCommand]
        private void OnAutoForbiddenForestToggle()
        {
            if (CurrentTaskType == TaskType.AutoForbiddenForest)
                StopTask();
            else
                StartTask(
                    TaskType.AutoForbiddenForest,
                    "自动禁林",
                    () => new AutoForbiddenForest(App.GetLogger<AutoForbiddenForest>(), _displayHwnd, _gameHwnd),
                    new Dictionary<string, object>
                    {
                        { "Times", AutoForbiddenForestTimes },
                        { "TeamPosition", SelectedTeamPosition }
                    });
        }

        [RelayCommand]
        private void OnAutoCookingToggle()
        {
            if (CurrentTaskType == TaskType.AutoCooking)
                StopTask();
            else
                StartTask(
                    TaskType.AutoCooking,
                    "自动烹饪",
                    () => new AutoCooking(App.GetLogger<AutoCooking>(), _cookingConfigService, _displayHwnd, _gameHwnd),
                    new Dictionary<string, object>
                    {
                        { "Times", AutoCookingTimes },
                        { "Dish", AutoCookingSelectedDish },
                        { "OCR", AutoCookingSelectedOCR }
                    });
        }

        [RelayCommand]
        private void OnAutoSweetAdventureToggle()
        {
            if (CurrentTaskType == TaskType.AutoSweetAdventure)
                StopTask();
            else
                StartTask(
                    TaskType.AutoSweetAdventure,
                    "甜蜜冒险",
                    () => new AutoSweetAdventure(App.GetLogger<AutoSweetAdventure>(), _displayHwnd, _gameHwnd));
        }

        [RelayCommand]
        private void OnOpenQuestionBank(object sender)
        {
            var questionBankPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/ClubQuiz");
            if (!Directory.Exists(questionBankPath))
            {
                Directory.CreateDirectory(questionBankPath);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = questionBankPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        #endregion

        #region 导航

        public Task OnNavigatedToAsync() => Task.CompletedTask;

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        #endregion

        #region 设置保存

        partial void OnAnswerDelayChanged(int value) => SaveSetting(() => _settings.AnswerDelay = value);
        partial void OnJoinOthersChanged(bool value) => SaveSetting(() => _settings.JoinOthers = value);
        partial void OnStopWhenContributionFullChanged(bool value) => SaveSetting(() => _settings.StopWhenContributionFull = value);
        partial void OnAutoForbiddenForestTimesChanged(int value) => SaveSetting(() => _settings.AutoForbiddenForestTimes = value);
        partial void OnSelectedTeamPositionChanged(string value) => SaveSetting(() => _settings.SelectedTeamPosition = value);
        partial void OnAutoCookingTimesChanged(int value) => SaveSetting(() => _settings.AutoCookingTimes = value);
        partial void OnAutoCookingSelectedDishChanged(string value) => SaveSetting(() => _settings.AutoCookingSelectedDish = value);
        partial void OnAutoCookingSelectedOCRChanged(string value) => SaveSetting(() => _settings.AutoCookingSelectedOCR = value);

        private void SaveSetting(Action updateAction)
        {
            updateAction();
            _settings.Save();
        }



        #endregion
    }
}
