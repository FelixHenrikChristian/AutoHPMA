using AutoHPMA.Configuration;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.ViewModels;

public partial class HomeViewModel : ObservableRecipient, IDisposable
{
    private const int DefaultStateMonitorInterval = 200;
    private const int MinStateMonitorInterval = 50;
    private const int MaxStateMonitorInterval = 60000;

    private readonly IAutomationRuntimeService _runtimeService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IInfoBarNotificationService _infoBar;
    private readonly ILogger<HomeViewModel> _logger;

    private bool _logWindowEnabled = true;
    private bool _logWindowMarqueeEnabled = true;
    private bool _hideDebugLog = true;
    private bool _maskWindowEnabled = true;
    private bool _maskWindowShowTextLabels = true;
    private double _stateMonitorInterval = DefaultStateMonitorInterval;
    private string _selectedOcrEngine = "PaddleOCR";
    private bool _isRunning;
    private bool _toggleButtonEnabled = true;
    private bool _suppressSettingPersistence;

    public HomeViewModel(
        IAutomationRuntimeService runtimeService,
        ILocalSettingsService localSettingsService,
        IInfoBarNotificationService infoBar,
        ILogger<HomeViewModel> logger)
    {
        _runtimeService = runtimeService;
        _localSettingsService = localSettingsService;
        _infoBar = infoBar;
        _logger = logger;

        _runtimeService.StateChanged += OnRuntimeStateChanged;
        IsRunning = _runtimeService.IsRunning;
    }

    public bool LogWindowEnabled
    {
        get => _logWindowEnabled;
        set
        {
            if (SetProperty(ref _logWindowEnabled, value))
            {
                PersistSetting(SettingsKeys.HomeLogWindowEnabled, value);
            }
        }
    }

    public bool LogWindowMarqueeEnabled
    {
        get => _logWindowMarqueeEnabled;
        set
        {
            if (SetProperty(ref _logWindowMarqueeEnabled, value))
            {
                PersistSetting(SettingsKeys.HomeLogWindowMarqueeEnabled, value);
            }
        }
    }

    public bool HideDebugLog
    {
        get => _hideDebugLog;
        set
        {
            if (SetProperty(ref _hideDebugLog, value))
            {
                PersistSetting(SettingsKeys.HomeHideDebugLog, value);
            }
        }
    }

    public bool MaskWindowEnabled
    {
        get => _maskWindowEnabled;
        set
        {
            if (SetProperty(ref _maskWindowEnabled, value))
            {
                PersistSetting(SettingsKeys.HomeMaskWindowEnabled, value);
            }
        }
    }

    public bool MaskWindowShowTextLabels
    {
        get => _maskWindowShowTextLabels;
        set
        {
            if (SetProperty(ref _maskWindowShowTextLabels, value))
            {
                PersistSetting(SettingsKeys.HomeMaskWindowShowTextLabels, value);
            }
        }
    }

    public double StateMonitorInterval
    {
        get => _stateMonitorInterval;
        set
        {
            var normalizedValue = NormalizeStateMonitorInterval(value);
            if (SetProperty(ref _stateMonitorInterval, normalizedValue))
            {
                PersistSetting(SettingsKeys.HomeStateMonitorInterval, (int)normalizedValue);
            }
        }
    }

    public string SelectedOcrEngine
    {
        get => _selectedOcrEngine;
        set
        {
            var normalizedValue = OcrEngines.Contains(value) ? value : OcrEngines[0];
            if (SetProperty(ref _selectedOcrEngine, normalizedValue))
            {
                PersistSetting(SettingsKeys.HomeSelectedOcrEngine, normalizedValue);
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(TriggerButtonText));
                OnPropertyChanged(nameof(TriggerButtonGlyph));
            }
        }
    }

    public bool ToggleButtonEnabled
    {
        get => _toggleButtonEnabled;
        set => SetProperty(ref _toggleButtonEnabled, value);
    }

    public string[] OcrEngines { get; } =
    [
        "PaddleOCR",
        "WindowsOCR",
        "RapidOCR",
        "TesseractOCR",
    ];

    public string TriggerButtonText => IsRunning ? "停止" : "启动";

    public string TriggerButtonGlyph => IsRunning ? "\uE711" : "\uE768";

    [RelayCommand]
    private async Task ToggleTriggerAsync()
    {
        ToggleButtonEnabled = false;
        try
        {
            if (_runtimeService.IsRunning)
            {
                _runtimeService.Stop();
                IsRunning = false;
                _infoBar.Show(InfoBarSeverity.Informational, "已停止", "截图器已停止。");
                return;
            }

            var result = await _runtimeService.StartAsync(CreateRuntimeOptions());
            IsRunning = _runtimeService.IsRunning;

            _infoBar.Show(
                result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                result.Title,
                result.Message);
        }
        finally
        {
            ToggleButtonEnabled = true;
        }
    }

    public async Task LoadAsync()
    {
        _runtimeService.StateChanged -= OnRuntimeStateChanged;
        _runtimeService.StateChanged += OnRuntimeStateChanged;

        _suppressSettingPersistence = true;
        try
        {
            LogWindowEnabled = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.HomeLogWindowEnabled) ?? true;
            LogWindowMarqueeEnabled = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.HomeLogWindowMarqueeEnabled) ?? true;
            HideDebugLog = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.HomeHideDebugLog) ?? true;
            MaskWindowEnabled = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.HomeMaskWindowEnabled) ?? true;
            MaskWindowShowTextLabels = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.HomeMaskWindowShowTextLabels) ?? true;
            StateMonitorInterval = await _localSettingsService.ReadSettingAsync<int?>(SettingsKeys.HomeStateMonitorInterval) ?? DefaultStateMonitorInterval;
            SelectedOcrEngine = await _localSettingsService.ReadSettingAsync<string>(SettingsKeys.HomeSelectedOcrEngine) ?? OcrEngines[0];
        }
        finally
        {
            _suppressSettingPersistence = false;
        }

        IsRunning = _runtimeService.IsRunning;
    }

    public void Dispose()
    {
        _runtimeService.StateChanged -= OnRuntimeStateChanged;
    }

    private AutomationRuntimeOptions CreateRuntimeOptions() => new()
    {
        LogWindowEnabled = LogWindowEnabled,
        LogWindowMarqueeEnabled = LogWindowMarqueeEnabled,
        ShowDebugLogs = !HideDebugLog,
        MaskWindowEnabled = MaskWindowEnabled,
        MaskWindowShowTextLabels = MaskWindowShowTextLabels,
        StateMonitorInterval = (int)StateMonitorInterval,
        SelectedOcrEngine = SelectedOcrEngine,
    };

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        _ = dispatcherQueue.TryEnqueue(() => IsRunning = _runtimeService.IsRunning);
    }

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
            _logger.LogError(ex, "保存首页设置失败：{Key}", key);
        }
    }

    private static double NormalizeStateMonitorInterval(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultStateMonitorInterval;
        }

        return Math.Clamp(Math.Round(value), MinStateMonitorInterval, MaxStateMonitorInterval);
    }
}
