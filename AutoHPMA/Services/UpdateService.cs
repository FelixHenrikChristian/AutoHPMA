using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

using AutoHPMA.Contracts.Services;
using AutoHPMA.Models;

namespace AutoHPMA.Services;

public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/FelixHenrikChristian/AutoHPMA/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "AutoHPMA");
        return client;
    }

    public async Task CheckUpdateAsync(UpdateOption option)
    {
        try
        {
            var latestRelease = await GetLatestReleaseAsync();
            if (latestRelease == null)
            {
                // TODO: 无法获取版本信息时弹出SnackBar
                return;
            }

            var latestVersionStr = latestRelease.TagName.TrimStart('v');
            var latestVersion = new Version(latestVersionStr);
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

            if (latestVersion <= currentVersion)
            {
                // 未接 UI：已是最新版本时不提示。
                return;
            }

            // TODO: 检查到新版本，弹出更新窗口
        }
        catch (Exception ex)
        {
            // TODO: 手动检查异常时弹出SnackBar
        }
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
}
