using System.Threading.Tasks;

using AutoHPMA.Configuration;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoHPMA.ViewModels;

public partial class NotificationSettingsViewModel : ObservableRecipient
{
    private readonly ILocalSettingsService _localSettingsService;

    private bool _suppressEnabledPersist;
    private bool _suppressSoundPersist;

    [ObservableProperty]
    private bool _notificationEnabled = true;

    [ObservableProperty]
    private bool _notificationSoundEnabled = true;

    public NotificationSettingsViewModel(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task LoadAsync()
    {
        _suppressEnabledPersist = true;
        _suppressSoundPersist = true;
        try
        {
            var enabled = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.NotificationEnabled);
            NotificationEnabled = enabled ?? true;

            var sound = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.NotificationSoundEnabled);
            NotificationSoundEnabled = sound ?? true;
        }
        finally
        {
            _suppressEnabledPersist = false;
            _suppressSoundPersist = false;
        }

        // 同步到全局帮助类，使其它模块发送通知时遵循当前偏好。
        AppNotificationHelper.IsEnabled = NotificationEnabled;
        AppNotificationHelper.IsSoundEnabled = NotificationSoundEnabled;
    }

    partial void OnNotificationEnabledChanged(bool value)
    {
        AppNotificationHelper.IsEnabled = value;

        if (_suppressEnabledPersist)
        {
            return;
        }

        _ = _localSettingsService.SaveSettingAsync(SettingsKeys.NotificationEnabled, value);
    }

    partial void OnNotificationSoundEnabledChanged(bool value)
    {
        AppNotificationHelper.IsSoundEnabled = value;

        if (_suppressSoundPersist)
        {
            return;
        }

        _ = _localSettingsService.SaveSettingAsync(SettingsKeys.NotificationSoundEnabled, value);
    }

    [RelayCommand]
    private void TestNotification()
    {
        AppNotificationHelper.Show(
            "测试通知",
            "这是一条来自 AutoHPMA 的测试通知，用于验证 Windows 通知功能是否正常工作。");
    }
}

