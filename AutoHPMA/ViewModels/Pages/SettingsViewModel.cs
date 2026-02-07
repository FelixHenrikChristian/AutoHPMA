// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using AutoHPMA.Config;
using AutoHPMA.Models;
using AutoHPMA.Services.Interface;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using System.Windows.Media;

namespace AutoHPMA.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;
        private readonly AppSettings _settings;
        private readonly IUpdateService _updateService;

        [ObservableProperty]
        private string _appVersion = String.Empty;
        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;
        [ObservableProperty]
        private ThemeOption _selectedThemeOption;

        [ObservableProperty]
        private int _logFileLimit = 10;

        [ObservableProperty]
        private bool _isCheckingUpdate = false;

        public class ThemeOption
        {
            public string ThemeKey { get; set; }  // "Light", "Dark", or "System"
            public string Name { get; set; }
        }

        private readonly ThemeOption[] _themeOptions = new[]
        {
            new ThemeOption { ThemeKey = "System", Name = "跟随系统" },
            new ThemeOption { ThemeKey = "Light", Name = "浅色" },
            new ThemeOption { ThemeKey = "Dark", Name = "深色" }
        };

        public IEnumerable<ThemeOption> ThemeOptions => _themeOptions;

        public SettingsViewModel(AppSettings settings, IUpdateService updateService)
        {
            _settings = settings;
            _updateService = updateService;
            LogFileLimit = _settings.LogFileLimit;
            
            // Subscribe to theme changes to handle "follow system" mode
            ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        }

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            // Load saved theme preference
            var savedTheme = _settings.Theme;
            SelectedThemeOption = _themeOptions.FirstOrDefault(t => t.ThemeKey == savedTheme) 
                                  ?? _themeOptions.First(); // Default to "System"
            
            // Apply the theme based on saved preference
            ApplyTheme(savedTheme);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersion = $"v{version.Major}.{version.Minor}.{version.Build}";

            _isInitialized = true;
        }
        
        private void OnApplicationThemeChanged(ApplicationTheme currentTheme, Color accentColor)
        {
            // Update the current theme when it changes (e.g., by SystemThemeWatcher)
            if (_settings.Theme == "System")
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentTheme = currentTheme;
                });
            }
        }
        
        private void ApplyTheme(string themeKey)
        {
            ApplicationTheme appTheme;
            
            if (themeKey == "System")
            {
                appTheme = GetSystemTheme();
            }
            else if (themeKey == "Dark")
            {
                appTheme = ApplicationTheme.Dark;
            }
            else
            {
                appTheme = ApplicationTheme.Light;
            }
            
            ApplicationThemeManager.Apply(appTheme);
            CurrentTheme = appTheme;
        }

        private ApplicationTheme GetSystemTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value != null)
                        {
                            return (int)value == 1 ? ApplicationTheme.Light : ApplicationTheme.Dark;
                        }
                    }
                }
            }
            catch
            {
                // 如果无法获取系统主题，默认使用浅色主题
            }
            return ApplicationTheme.Light;
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        partial void OnSelectedThemeOptionChanged(ThemeOption value)
        {
            if (value != null && _isInitialized)
            {
                // Apply the theme
                ApplyTheme(value.ThemeKey);
                
                // Save the preference
                _settings.Theme = value.ThemeKey;
                _settings.Save();
            }
        }

        [RelayCommand]
        private void ResetSettings()
        {
            //_settings.Reset();
            _settings.Clear();
            var uiMessageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "⚠️ 提示",
                Content = "偏好设置已重置，程序即将退出。",
            };
            var result = uiMessageBox.ShowDialogAsync();
            Application.Current.Shutdown();
        }

        [RelayCommand]
        private void OpenLogFolder()
        {
            var logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logFolderPath))
            {
                Directory.CreateDirectory(logFolderPath);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logFolderPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        partial void OnLogFileLimitChanged(int value)
        {
            _settings.LogFileLimit = value;
            _settings.Save();
        }

        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            if (IsCheckingUpdate) return;

            IsCheckingUpdate = true;
            try
            {
                await _updateService.CheckUpdateAsync(new UpdateOption { Trigger = UpdateTrigger.Manual });
            }
            catch (Exception ex)
            {
                var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "❌ 错误",
                    Content = $"检查更新时发生错误：{ex.Message}",
                };
                await uiMessageBox.ShowDialogAsync();
            }
            finally
            {
                IsCheckingUpdate = false;
            }
        }
    }
}
