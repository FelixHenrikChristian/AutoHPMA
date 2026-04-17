using System.Reflection;
using System.Linq;

using AutoHPMA.Configuration;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Settings;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using Windows.ApplicationModel;

namespace AutoHPMA.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IUpdateService _updateService;

    private bool _suppressThemeChange;
    private bool _suppressPreventSleepPersist;

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
    [NotifyPropertyChangedFor(nameof(IsUpdateCheckEnabled))]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    public bool IsUpdateCheckEnabled => !IsCheckingUpdate;

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService,
        ILocalSettingsService localSettingsService,
        IUpdateService updateService)
    {
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _updateService = updateService;

        AppVersion = GetShortAppVersion();
    }

    /// <summary>
    /// 在页面 Loaded 时调用，同步本地偏好与当前主题。
    /// </summary>
    public async Task LoadAsync()
    {
        _suppressPreventSleepPersist = true;
        try
        {
            var saved = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.PreventSleepWhileRunning);
            PreventSleepWhileRunning = saved ?? true;

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

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        await _localSettingsService.ResetAllAsync();
        PowerSaveHelper.SetPreventSleepWhileRunning(false);
        // 未接 UI：重置前无确认框，直接退出。
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
            // UpdateService 内尚未接界面提示，用户侧无弹窗反馈。
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
