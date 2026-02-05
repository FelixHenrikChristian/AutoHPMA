using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Services.Interface;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using Wpf.Ui.Controls;

namespace AutoHPMA.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly ILogger<UpdateService> _logger;
        private const string GitHubApiUrl = "https://api.github.com/repos/FelixChristian011226/AutoHPMA/releases/latest";
        private const string DownloadPageUrl = "https://github.com/FelixChristian011226/AutoHPMA/releases/latest";

        public UpdateService(ILogger<UpdateService> logger)
        {
            _logger = logger;
        }

        public async Task CheckUpdateAsync(UpdateOption option)
        {
            try
            {
#if DEBUG
                // 在调试模式下跳过更新检查
                if (option.Trigger == UpdateTrigger.Manual)
                {
                    await ShowMessageBoxAsync("调试模式", "调试模式下不检查更新");
                }
                return;
#endif

                var latestRelease = await GetLatestReleaseAsync();
                if (latestRelease == null)
                {
                    if (option.Trigger == UpdateTrigger.Manual)
                    {
                        await ShowMessageBoxAsync("检查更新失败", "无法获取最新版本信息，请稍后重试");
                    }
                    return;
                }

                var latestVersionStr = latestRelease.TagName.TrimStart('v');
                var latestVersion = new Version(latestVersionStr);
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                if (latestVersion <= currentVersion)
                {
                    if (option.Trigger == UpdateTrigger.Manual)
                    {
                        SnackbarHelper.ShowSuccess("检查更新", "当前已是最新版本");
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
                    await ShowMessageBoxAsync("更新检测失败", $"检查更新时发生错误：{ex.Message}");
                }
            }
        }

        private async Task<GitHubRelease?> GetLatestReleaseAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "AutoHPMA");
                
                var json = await client.GetStringAsync(GitHubApiUrl);
                return JsonConvert.DeserializeObject<GitHubRelease>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新版本信息失败");
                return null;
            }
        }

        private async Task ShowUpdateDialogAsync(GitHubRelease release)
        {
            var updateWindow = new Views.Windows.UpdateWindow(release);
            
            // 安全设置Owner属性
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            {
                updateWindow.Owner = Application.Current.MainWindow;
                updateWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                updateWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var result = await updateWindow.ShowDialogAsync();
            
            switch (result)
            {
                case Views.Windows.UpdateWindow.UpdateResult.Update:
                    await StartUpdaterAsync();
                    break;
                case Views.Windows.UpdateWindow.UpdateResult.Download:
                    OpenDownloadPage();
                    break;
                case Views.Windows.UpdateWindow.UpdateResult.Cancel:
                    break;
            }
        }

        private async Task StartUpdaterAsync()
        {
            try
            {
                string updaterExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoHPMA.update.exe");
                
                if (!File.Exists(updaterExePath))
                {
                    await ShowMessageBoxAsync("更新程序不存在", "更新程序不存在，请选择手动下载更新！");
                    OpenDownloadPage();
                    return;
                }

                // 启动更新程序
                Process.Start(updaterExePath, "-I");
                
                // 退出当前程序
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动更新程序失败");
                await ShowMessageBoxAsync("启动更新程序失败", $"无法启动更新程序：{ex.Message}");
                OpenDownloadPage();
            }
        }

        private void OpenDownloadPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(DownloadPageUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开下载页面失败");
            }
        }

        private async Task ShowMessageBoxAsync(string title, string content)
        {
            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = content,
                CloseButtonText = "确定"
            };

            // 安全设置Owner属性
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            {
                messageBox.Owner = Application.Current.MainWindow;
            }

            await messageBox.ShowDialogAsync();
        }
    }
}