using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;

using Microsoft.Extensions.Logging;

using AutoHPMA.Configuration;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Settings;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel;

namespace AutoHPMA.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IUpdateService _updateService;
    private readonly IInfoBarNotificationService _infoBar;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _suppressThemeChange;
    private bool _suppressPreventSleepPersist;
    private bool _suppressDiagnosticModePersist;

    public ThemeOption[] ThemeOptions { get; } =
    {
        new ThemeOption { ThemeKey = "System", Name = "跟随系统" },
        new ThemeOption { ThemeKey = "Light", Name = "浅色" },
        new ThemeOption { ThemeKey = "Dark", Name = "深色" },
    };

    [ObservableProperty]
    private ThemeOption? _selectedThemeOption;

    [ObservableProperty]
    private bool _preventSleepWhileRunning = true;

    [ObservableProperty]
    private bool _diagnosticMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateCheckEnabled))]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    public bool IsUpdateCheckEnabled => !IsCheckingUpdate;

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService,
        ILocalSettingsService localSettingsService,
        IUpdateService updateService,
        IInfoBarNotificationService infoBar,
        ILogger<SettingsViewModel> logger)
    {
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _updateService = updateService;
        _infoBar = infoBar;
        _logger = logger;

        AppVersion = GetShortAppVersion();
    }

    /// <summary>
    /// 在页面 Loaded 时调用，同步本地偏好与当前主题。
    /// </summary>
    public async Task LoadAsync()
    {
        _suppressPreventSleepPersist = true;
        _suppressDiagnosticModePersist = true;
        try
        {
            var savedSleep = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.PreventSleepWhileRunning);
            PreventSleepWhileRunning = savedSleep ?? true;

            var savedDiag = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.DiagnosticMode);
            DiagnosticMode = savedDiag ?? false;

            _suppressThemeChange = true;
            try
            {
                var key = KeyFromElementTheme(_themeSelectorService.Theme);
                SelectedThemeOption = ThemeOptions.FirstOrDefault(t => t.ThemeKey == key) ?? ThemeOptions[0];
            }
            finally
            {
                _suppressThemeChange = false;
            }
        }
        finally
        {
            _suppressPreventSleepPersist = false;
            _suppressDiagnosticModePersist = false;
        }
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (_suppressThemeChange || value == null)
        {
            return;
        }

        _ = ApplyThemeAsync(value);
    }

    private async Task ApplyThemeAsync(ThemeOption option)
    {
        await _themeSelectorService.SetThemeAsync(ElementThemeFromKey(option.ThemeKey));
    }

    partial void OnPreventSleepWhileRunningChanged(bool value)
    {
        if (_suppressPreventSleepPersist)
        {
            return;
        }

        _ = PersistPreventSleepAsync(value);
    }

    private async Task PersistPreventSleepAsync(bool value)
    {
        await _localSettingsService.SaveSettingAsync(SettingsKeys.PreventSleepWhileRunning, value);
        PowerSaveHelper.SetPreventSleepWhileRunning(value);
    }

    partial void OnDiagnosticModeChanged(bool value)
    {

        LoggingHelper.SetDiagnosticMode(value);

        if (_suppressDiagnosticModePersist)
        {
            return;
        }

        _ = _localSettingsService.SaveSettingAsync(SettingsKeys.DiagnosticMode, value);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LoggingHelper.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = LoggingHelper.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开日志目录失败");
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        var xamlRoot = (App.MainWindow.Content as FrameworkElement)?.XamlRoot;
        if (xamlRoot == null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "重置设置",
            Content = "将清除所有用户偏好，并立即关闭应用。是否继续？",
            PrimaryButtonText = "重置并退出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        await _localSettingsService.ResetAllAsync();
        PowerSaveHelper.SetPreventSleepWhileRunning(false);

        _infoBar.Show(
            InfoBarSeverity.Success,
            "重置完成",
            "应用即将退出，请重新打开。",
            TimeSpan.FromSeconds(2.5));

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate)
        {
            return;
        }

        IsCheckingUpdate = true;
        try
        {
            await _updateService.CheckUpdateAsync(new UpdateOption { Trigger = UpdateTrigger.Manual });
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private static ElementTheme ElementThemeFromKey(string themeKey) => themeKey switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static string KeyFromElementTheme(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => "Light",
        ElementTheme.Dark => "Dark",
        _ => "System",
    };

    private static string GetShortAppVersion()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;
            version = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        }

        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }
}
