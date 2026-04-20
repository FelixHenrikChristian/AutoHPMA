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

using Windows.ApplicationModel;

namespace AutoHPMA.Services;

public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/FelixHenrikChristian/AutoHPMA/releases/latest";
    private const string DownloadPageUrl = "https://github.com/FelixHenrikChristian/AutoHPMA/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly ILogger<UpdateService> _logger;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "AutoHPMA");
        return client;
    }

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
    }

    public async Task CheckUpdateAsync(UpdateOption option)
    {
        try
        {
            var latestRelease = await GetLatestReleaseAsync();
            if (latestRelease == null)
            {
                // TODO: 无法获取版本信息时弹出 Snackbar（手动检查时提示）
                return;
            }

            var latestVersionStr = latestRelease.TagName.TrimStart('v');
            var latestVersion = new Version(latestVersionStr);
            var currentVersion = GetCurrentAppVersion();

            if (latestVersion <= currentVersion)
            {
                // TODO: 已是最新版本时弹出 Snackbar（手动检查时提示）
                return;
            }

            await ShowUpdateDialogAsync(latestRelease);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查更新时发生错误");
            // TODO: 手动检查异常时弹出 Snackbar
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
                // TODO: 更新程序不存在时提示用户（Snackbar 等）
                OpenDownloadPage();
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
            // TODO: 启动更新程序失败时提示用户（Snackbar 等）
            OpenDownloadPage();
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
