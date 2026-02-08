using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AutoHPMA.Models;
using Microsoft.Extensions.Logging;

namespace AutoHPMA.Services;

public class CookingConfigService
{
    private readonly ILogger<CookingConfigService> _logger;
    private readonly Dictionary<string, DishConfig> _dishConfigs;
    private readonly string _configPath;

    public CookingConfigService(ILogger<CookingConfigService> logger)
    {
        _logger = logger;
        _dishConfigs = new Dictionary<string, DishConfig>();
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/Tasks/Cooking/Config");
        
        if (!Directory.Exists(_configPath))
        {
            Directory.CreateDirectory(_configPath);
        }
        
        LoadConfigs();
    }

    private void LoadConfigs()
    {
        try
        {
            var configFiles = Directory.GetFiles(_configPath, "*.json");
            foreach (var file in configFiles)
            {
                var json = File.ReadAllText(file);
                var config = JsonSerializer.Deserialize<DishConfig>(json);
                if (config != null)
                {
                    _dishConfigs[config.Name] = config;
                    _logger.LogDebug("已加载菜品配置：{Name}", config.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载菜品配置时发生错误");
        }
    }

    public DishConfig? GetDishConfig(string dishName)
    {
        return _dishConfigs.TryGetValue(dishName, out var config) ? config : null;
    }

    public void SaveDishConfig(DishConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(_configPath, $"{config.Name}.json");
            File.WriteAllText(filePath, json);
            _dishConfigs[config.Name] = config;
            _logger.LogInformation("已保存菜品配置：{Name}", config.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存菜品配置时发生错误");
        }
    }
}