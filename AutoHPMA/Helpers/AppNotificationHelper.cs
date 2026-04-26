using System;

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AutoHPMA.Helpers;

/// <summary>
/// 使用 WinAppSDK 原生 <see cref="AppNotificationManager"/> 发送 Windows 通知的工具类。
/// 支持应用 Logo（始终启用）、可选的内联大图，以及可开关的默认提示音。
/// </summary>
public static class AppNotificationHelper
{
    private static bool _isRegistered;

    /// <summary>
    /// 通知是否启用（关闭时不会发送）。
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否启用通知声音（true = 使用系统默认提示音；false = 静音）。
    /// </summary>
    public static bool IsSoundEnabled { get; set; } = true;

    /// <summary>
    /// 注册通知管理器。应在应用启动时调用一次。
    /// </summary>
    public static void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Register();
            _isRegistered = true;
        }
        catch
        {
            // 在未打包或环境异常时静默失败，避免影响应用启动。
        }
    }

    /// <summary>
    /// 注销通知管理器。
    /// </summary>
    public static void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
        }
        finally
        {
            _isRegistered = false;
        }
    }

    /// <summary>
    /// 发送一条通知。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="message">通知正文。</param>
    /// <param name="heroImage">可选的大图，传入 <c>ms-appx:///</c> 或本地文件 URI。</param>
    public static void Show(string title, string message, Uri? heroImage = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .SetAppLogoOverride(new Uri("ms-appx:///Assets/logo.png"), AppNotificationImageCrop.Circle);

            if (heroImage != null)
            {
                builder.SetHeroImage(heroImage);
            }

            if (IsSoundEnabled)
            {
                builder.SetAudioEvent(AppNotificationSoundEvent.Default);
            }
            else
            {
                builder.MuteAudio();
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // 通知失败不应影响主流程。
        }
    }
}
