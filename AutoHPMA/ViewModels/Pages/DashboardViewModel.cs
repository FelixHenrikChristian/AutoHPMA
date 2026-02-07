// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using AutoHPMA.Config;
using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Messages;
using AutoHPMA.Services;
using AutoHPMA.Views.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AutoHPMA.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject
    {
        #region 字段

        private readonly AppSettings _settings;
        private readonly ILogger<DashboardViewModel> _logger;
        private DispatcherTimer _syncWindowTimer;

        #endregion

        #region Observable Properties

        [ObservableProperty]
        private bool _logWindowEnabled = true;

        [ObservableProperty]
        private bool _logWindowMarqueeEnabled = true;

        [ObservableProperty]
        private bool _debugLogEnabled = false;

        [ObservableProperty]
        private bool _maskWindowEnabled = false;

        [ObservableProperty]
        private bool _maskWindowShowTextLabels = true;

        [ObservableProperty]
        private int _stateMonitorInterval = 200;

        [ObservableProperty]
        private string _selectedOcrEngine = "PaddleOCR";

        public ObservableCollection<string> OcrEngines { get; } = ["PaddleOCR", "Tesseract"];

        [ObservableProperty]
        private bool _isRunning = false;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ToggleTriggerCommand))]
        private bool _toggleButtonEnabled = true;

        #endregion

        #region AppContextService 属性

        private LogWindow? _logWindow
        {
            get => AppContextService.Instance.LogWindow;
            set => AppContextService.Instance.LogWindow = value;
        }

        private MaskWindow? _maskWindow
        {
            get => AppContextService.Instance.MaskWindow;
            set => AppContextService.Instance.MaskWindow = value;
        }

        private WindowsGraphicsCapture _capture
        {
            get => AppContextService.Instance.Capture;
            set => AppContextService.Instance.Capture = value;
        }

        private IntPtr _displayHwnd
        {
            get => AppContextService.Instance.DisplayHwnd;
            set => AppContextService.Instance.DisplayHwnd = value;
        }

        private IntPtr _gameHwnd
        {
            get => AppContextService.Instance.GameHwnd;
            set => AppContextService.Instance.GameHwnd = value;
        }

        #endregion

        #region 枚举与事件

        private enum StartupOption { OfficialLauncher, MumuSimulator, None }
        private StartupOption _startupOption = StartupOption.None;

        #endregion

        #region 构造函数

        public DashboardViewModel(AppSettings settings, ILogger<DashboardViewModel> logger)
        {
            _settings = settings;
            _logger = logger;
            LoadSettings();
        }

        private void LoadSettings()
        {
            LogWindowEnabled = _settings.LogWindowEnabled;
            LogWindowMarqueeEnabled = _settings.LogWindowMarqueeEnabled;
            DebugLogEnabled = _settings.DebugLogEnabled;
            MaskWindowEnabled = _settings.MaskWindowEnabled;
            MaskWindowShowTextLabels = _settings.MaskWindowShowTextLabels;
            StateMonitorInterval = _settings.StateMonitorInterval;
            SelectedOcrEngine = _settings.SelectedOCR;
            
            // 初始化AppContextService中的共享配置
            AppContextService.Instance.StateMonitorInterval = _settings.StateMonitorInterval;
        }

        #endregion

        #region Timer 处理

        public void InitializeSyncWindowTimer()
        {
            _syncWindowTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
        }

        private void SyncWindowTimer_Tick(object? sender, EventArgs e)
        {
            if (_gameHwnd == IntPtr.Zero) return;

            bool shouldHide = NativeMethodsService.IsIconic(_gameHwnd) ||
                             NativeMethodsService.GetForegroundWindow() != _displayHwnd;

            if (shouldHide)
            {
                _logWindow?.Hide();
                _maskWindow?.Hide();
            }
            else
            {
                _logWindow?.Show();
                _maskWindow?.Show();
                
                // 确保 LogWindow 始终在 MaskWindow 之上
                _logWindow?.BringToFront();
            }

            _logWindow?.RefreshPosition(_gameHwnd);
            _maskWindow?.RefreshPosition(_displayHwnd);
        }

        #endregion

        #region 命令

        private bool CanToggleTrigger() => ToggleButtonEnabled;

        // 公共方法供热键调用
        public void ToggleAutoHPMA() => OnToggleTrigger();

        [RelayCommand(CanExecute = nameof(CanToggleTrigger))]
        private void OnToggleTrigger()
        {
            if (IsRunning)
                StopAutoHPMA();
            else
                StartAutoHPMA();
        }

        private void StartAutoHPMA()
        {
            GetGameHwnd();

            if (_gameHwnd == IntPtr.Zero)
            {
                ShowErrorMessage("未找到游戏窗口。请先启动游戏！");
                return;
            }

            SnackbarHelper.ShowSuccess("启动成功", "截图器已启动，可启动其他任务。");
            IsRunning = true;

            // 当官方启动器时，将游戏窗口置于前端
            if (_startupOption == StartupOption.OfficialLauncher)
            {
                NativeMethodsService.ShowWindow(_gameHwnd, NativeMethodsService.SW_SHOW);
                NativeMethodsService.SetForegroundWindow(_gameHwnd);
            }

            // 隐藏主窗口
            Application.Current.Dispatcher.Invoke(() => Application.Current.MainWindow.Hide());

            InitializeTimersAndWindows();
        }

        private void InitializeTimersAndWindows()
        {
            InitializeSyncWindowTimer();

            if (_logWindowEnabled)
            {
                _syncWindowTimer.Tick += SyncWindowTimer_Tick;
                _syncWindowTimer.Start();

                _logWindow = new LogWindow();
                _logWindow.Show();
                _logWindow.ShowInTaskbar = false;
                _logWindow.RefreshPosition(_gameHwnd);
                _logWindow.ShowDebugLogs = DebugLogEnabled;
                _logWindow.ShowMarquee = LogWindowMarqueeEnabled;
                
                _logger.LogInformation("检测到[Yellow]{StartupOption}[/Yellow]已启动", _startupOption);
                ShowGameWindowInfo();
            }

            if (_maskWindowEnabled)
            {
                _maskWindow = new MaskWindow();
                _maskWindow.Show();
                _maskWindow.ShowInTaskbar = false;
                _maskWindow.RefreshPosition(_displayHwnd);
                _maskWindow.ShowTextLabels = MaskWindowShowTextLabels;
            }

            _capture = new WindowsGraphicsCapture();
            _capture.Start(_displayHwnd);
        }

        private void StopAutoHPMA()
        {
            IsRunning = false;

            // 发送停止所有任务的消息
            WeakReferenceMessenger.Default.Send(new StopAllTasksMessage(true));

            CleanupResources();
        }

        private void CleanupResources()
        {
            _logWindow?.Close();
            _logWindow = null;

            _maskWindow?.Close();
            _maskWindow = null;

            _syncWindowTimer.Tick -= SyncWindowTimer_Tick;
            _syncWindowTimer.Stop();

            _capture?.Stop();
            _capture = null;
            
            GC.Collect();
        }

        [RelayCommand]
        public void OnGoToWikiUrl()
        {
            Process.Start(new ProcessStartInfo("https://autohpma-web.vercel.app/") { UseShellExecute = true });
        }

        #endregion

        #region 辅助方法

        private void ShowErrorMessage(string content)
        {
            var uiMessageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "⚠️ 错误",
                Content = content,
            };
            _ = uiMessageBox.ShowDialogAsync();
        }



        private void GetGameHwnd()
        {
            _displayHwnd = WindowHelper.FindHandleByProcessName("MuMu安卓设备", "MuMuNxDevice");
            if (_displayHwnd != IntPtr.Zero)
            {
                _startupOption = StartupOption.MumuSimulator;
                _gameHwnd = WindowHelper.FindChildWindowByTitle(_displayHwnd, "MuMuNxDevice");
            }
            else
            {
                _gameHwnd = WindowHelper.FindHandleByProcessName("哈利波特：魔法觉醒", "Harry Potter Magic Awakened");
                if (_gameHwnd != IntPtr.Zero)
                {
                    _startupOption = StartupOption.OfficialLauncher;
                }
            }
        }

        private void ShowGameWindowInfo()
        {
            try
            {
                WindowInteractionHelper.GetWindowPositionAndSize(_gameHwnd, out int left, out int top, out int width, out int height);
                _logger.LogDebug("窗口句柄信息 - DisplayHwnd: [Yellow]0x{DisplayHwnd:X}[/Yellow], GameHwnd: [Yellow]0x{GameHwnd:X}[/Yellow]", 
                    _displayHwnd.ToInt64(), _gameHwnd.ToInt64());
                _logger.LogInformation("检测到游戏窗口分辨率 [Yellow]{Width}*{Height}[/Yellow]", width, height);
                _logger.LogDebug("游戏窗口位置：左上角({Left},{Top})", left, top);

                WindowInteractionHelper.GetWindowPositionAndSize(_displayHwnd, out int leftMumu, out int topMumu, out width, out height);
                _logger.LogDebug("检测到模拟器窗口分辨率{Width}*{Height}", width, height);
                _logger.LogDebug("模拟器窗口位置：左上角({Left},{Top})", leftMumu, topMumu);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取窗口信息失败");
            }
        }

        #endregion

        #region 设置保存


        partial void OnLogWindowEnabledChanged(bool value) => SaveSetting(() => _settings.LogWindowEnabled = value);
        partial void OnDebugLogEnabledChanged(bool value) => SaveSetting(() => _settings.DebugLogEnabled = value);
        partial void OnMaskWindowEnabledChanged(bool value) => SaveSetting(() => _settings.MaskWindowEnabled = value);
        partial void OnMaskWindowShowTextLabelsChanged(bool value) => SaveSetting(() => _settings.MaskWindowShowTextLabels = value);
        partial void OnLogWindowMarqueeEnabledChanged(bool value) => SaveSetting(() => _settings.LogWindowMarqueeEnabled = value);
        partial void OnStateMonitorIntervalChanged(int value) => SaveSetting(() => _settings.StateMonitorInterval = value);
        partial void OnSelectedOcrEngineChanged(string value) => SaveSetting(() => _settings.SelectedOCR = value);

        private void SaveSetting(Action updateAction)
        {
            updateAction();
            _settings.Save();
        }

        #endregion
    }
}
