using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Views;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel;

namespace AutoHPMA.Services;

public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/FelixHenrikChristian/AutoHPMA/releases/latest";
    private const string DownloadPageUrl = "https://github.com/FelixHenrikChristian/AutoHPMA/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly ILogger<UpdateService> _logger;
    private readonly IInfoBarNotificationService _infoBar;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "AutoHPMA");
        return client;
    }

    public UpdateService(ILogger<UpdateService> logger, IInfoBarNotificationService infoBar)
    {
        _logger = logger;
        _infoBar = infoBar;
    }

    public async Task CheckUpdateAsync(UpdateOption option)
    {
        try
        {
            var latestRelease = await GetLatestReleaseAsync();
            if (latestRelease == null)
            {
                if (option.Trigger == UpdateTrigger.Manual)
                {
                    _infoBar.Show(
                        InfoBarSeverity.Warning,
                        "检查更新",
                        "无法获取最新版本信息，请稍后再试。");
                }

                return;
            }

            var latestVersionStr = latestRelease.TagName.TrimStart('v');
            var latestVersion = new Version(latestVersionStr);
            var currentVersion = GetCurrentAppVersion();

            if (latestVersion <= currentVersion)
            {
                if (option.Trigger == UpdateTrigger.Manual)
                {
                    _infoBar.Show(
                        InfoBarSeverity.Success,
                        "已是最新版本",
                        "当前已安装最新版本。");
                }

                return;
            }

            await ShowUpdateDialogAsync(latestRelease);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查更新时发生错误");
            if (option.Trigger == UpdateTrigger.Manual)
            {
                _infoBar.Show(
                    InfoBarSeverity.Error,
                    "检查更新失败",
                    "请检查网络连接后重试。");
            }
        }
    }

    private static Version GetCurrentAppVersion()
    {
        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;
            return new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }

        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            var json = await HttpClient.GetStringAsync(GitHubApiUrl);
            return JsonSerializer.Deserialize<GitHubRelease>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task ShowUpdateDialogAsync(GitHubRelease release)
    {
        var tcs = new TaskCompletionSource<UpdateWindow.UpdateResult>();
        var dq = App.MainWindow.DispatcherQueue;
        if (!dq.TryEnqueue(() => _ = ShowUpdateWindowAsync()))
        {
            tcs.TrySetException(new InvalidOperationException("无法将更新窗口调度到 UI 线程。"));
        }

        async Task ShowUpdateWindowAsync()
        {
            try
            {
                var updateWindow = new UpdateWindow(release);
                var result = await updateWindow.ShowAsync();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        var result = await tcs.Task;
        switch (result)
        {
            case UpdateWindow.UpdateResult.Update:
                await StartUpdaterAsync();
                break;
            case UpdateWindow.UpdateResult.Download:
                OpenDownloadPage();
                break;
            case UpdateWindow.UpdateResult.Cancel:
                break;
        }
    }

    private Task StartUpdaterAsync()
    {
        try
        {
            var updaterExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoHPMA.update.exe");
            if (!File.Exists(updaterExePath))
            {
                _infoBar.Show(
                    InfoBarSeverity.Warning,
                    "无法启动更新",
                    "未找到更新程序。");
                return Task.CompletedTask;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterExePath,
                Arguments = "-I",
                UseShellExecute = true,
            });
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动更新程序失败");
            _infoBar.Show(
                InfoBarSeverity.Error,
                "启动更新失败",
                "无法启动更新程序。");
        }

        return Task.CompletedTask;
    }

    private void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DownloadPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开下载页面失败");
        }
    }
}
