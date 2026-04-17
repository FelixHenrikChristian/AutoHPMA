namespace AutoHPMA.Contracts.Services;

public interface ILocalSettingsService
{
    Task<T?> ReadSettingAsync<T>(string key);

    Task SaveSettingAsync<T>(string key, T value);

    /// <summary>
    /// 清除全部本地设置（用于「恢复设置」）。
    /// </summary>
    Task ResetAllAsync();
}
