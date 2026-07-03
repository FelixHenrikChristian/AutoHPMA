using System;
using System.IO;

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OpenCvSharp;
using Serilog;

namespace AutoHPMA.Helpers;

/// <summary>
/// 使用 WinAppSDK 原生 <see cref="AppNotificationManager"/> 发送 Windows 通知的工具类。
/// 支持应用 Logo（始终启用）、可选的内联大图，以及可开关的默认提示音。
/// </summary>
public static class AppNotificationHelper
{
    private const int NotificationImageRetentionDays = 7;
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
        ShowCore(title, message, heroImage, inlineImage: null);
    }

    /// <summary>
    /// 发送一条带内联截图的通知。
    /// </summary>
    public static void ShowWithImage(string title, string message, Mat image)
    {
        if (!IsEnabled)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(image);

        try
        {
            if (image.Empty())
            {
                Show(title, message);
                return;
            }

            var imagePath = SaveNotificationImage(image);
            ShowCore(title, message, heroImage: null, inlineImage: new Uri(imagePath));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存通知截图失败，将发送纯文本通知。");
            Show(title, message);
        }
    }

    private static void ShowCore(string title, string message, Uri? heroImage, Uri? inlineImage)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);

            if (ResolveLogoUri() is { } logoUri)
            {
                builder.SetAppLogoOverride(logoUri, AppNotificationImageCrop.Circle);
            }

            if (heroImage is not null)
            {
                builder.SetHeroImage(heroImage);
            }

            if (inlineImage is not null)
            {
                builder.SetInlineImage(inlineImage);
            }

            ApplyAudioSettings(builder);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "发送 Windows 通知失败。标题：{NotificationTitle}", title);
        }
    }

    private static Uri? ResolveLogoUri()
    {
        if (RuntimeHelper.IsMSIX)
        {
            return new Uri("ms-appx:///Assets/logo.png");
        }

        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
        return File.Exists(logoPath) ? new Uri(logoPath) : null;
    }

    private static void ApplyAudioSettings(AppNotificationBuilder builder)
    {
        if (IsSoundEnabled)
        {
            builder.SetAudioEvent(AppNotificationSoundEvent.Default);
        }
        else
        {
            builder.MuteAudio();
        }
    }

    private static string SaveNotificationImage(Mat image)
    {
        var imageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoHPMA",
            "cache",
            "Notifications");
        Directory.CreateDirectory(imageDirectory);
        DeleteExpiredNotificationImages(imageDirectory);

        var imagePath = Path.Combine(imageDirectory, $"notification_{Guid.NewGuid():N}.png");
        image.SaveImage(imagePath);
        return imagePath;
    }

    private static void DeleteExpiredNotificationImages(string imageDirectory)
    {
        var expireBefore = DateTime.UtcNow.AddDays(-NotificationImageRetentionDays);
        foreach (var imagePath in Directory.EnumerateFiles(imageDirectory, "notification_*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(imagePath) < expireBefore)
                {
                    File.Delete(imagePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Debug(ex, "清理过期通知截图失败：{ImagePath}", imagePath);
            }
        }
    }
}
