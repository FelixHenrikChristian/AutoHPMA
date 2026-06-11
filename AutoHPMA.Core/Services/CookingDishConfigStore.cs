using System.Text.Json;
using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Services;

public sealed class CookingDishConfigStore
{
    private readonly Dictionary<string, CookingDishConfig> _configs;

    private CookingDishConfigStore(Dictionary<string, CookingDishConfig> configs)
    {
        _configs = configs;
    }

    public int Count => _configs.Count;

    public static CookingDishConfigStore Load(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        if (!Directory.Exists(configDirectory))
        {
            throw new DirectoryNotFoundException($"烹饪配置目录不存在：{configDirectory}");
        }

        var configs = new Dictionary<string, CookingDishConfig>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(configDirectory, "*.json"))
        {
            using var stream = File.OpenRead(file);
            var config = JsonSerializer.Deserialize<CookingDishConfig>(stream);
            if (config is null || string.IsNullOrWhiteSpace(config.Name))
            {
                continue;
            }

            configs[config.Name] = config;
        }

        if (configs.Count == 0)
        {
            throw new InvalidOperationException("烹饪菜品配置为空。");
        }

        return new CookingDishConfigStore(configs);
    }

    public CookingDishConfig GetRequired(string dishName)
    {
        if (_configs.TryGetValue(dishName, out var config))
        {
            return config;
        }

        var availableDishes = string.Join("、", _configs.Keys.Order(StringComparer.CurrentCulture));
        throw new KeyNotFoundException($"未找到菜品配置：{dishName}。可用菜品：{availableDishes}");
    }
}
