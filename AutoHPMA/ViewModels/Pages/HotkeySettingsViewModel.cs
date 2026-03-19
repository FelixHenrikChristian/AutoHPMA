using System;
using System.IO;
using System.Threading.Tasks;
using AutoHPMA.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Linq;
using System.Collections.Generic;
using AutoHPMA.ViewModels.Pages;
using Microsoft.Extensions.Logging;
using AutoHPMA.Services;
using AutoHPMA.Services.Interface;
using Wpf.Ui.Controls;
using OpenCvSharp.Extensions;

namespace AutoHPMA.ViewModels.Pages
{
    public partial class HotkeySettingsViewModel : ObservableObject, IDisposable
    {
        private readonly AppSettings _settings;
        private readonly ILogger<HotkeySettingsViewModel> _logger;
        private readonly IAppContextService _appContextService;

        [ObservableProperty]
        private ObservableCollection<HotkeyBinding> _hotkeyBindings;

        [ObservableProperty]
        private HotkeyBinding _selectedBinding = new();

        [ObservableProperty]
        private bool _isWaitingForKey;

        private KeyboardHookManager? _keyboardHookManager;

        public HotkeySettingsViewModel(
            AppSettings settings,
            ILogger<HotkeySettingsViewModel> logger,
            IAppContextService appContextService)
        {
            _settings = settings;
            _logger = logger;
            _appContextService = appContextService;
            _hotkeyBindings = new ObservableCollection<HotkeyBinding>();
            LoadHotkeyBindings();
        }

        /// <summary>
        /// 设置键盘钩子管理器
        /// </summary>
        public void SetKeyboardHookManager(KeyboardHookManager manager)
        {
            if (_keyboardHookManager != null)
            {
                _keyboardHookManager.HotkeyPressed -= OnHotkeyPressed;
            }

            _keyboardHookManager = manager;
            _keyboardHookManager.HotkeyPressed += OnHotkeyPressed;
            RegisterAllHotkeys();
        }

        /// <summary>
        /// 更新目标窗口句柄（当游戏窗口句柄发生变化时调用）
        /// </summary>
        public void UpdateTargetWindow(IntPtr hwnd)
        {
            _keyboardHookManager?.SetTargetWindow(hwnd);
        }

        private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
        {
            // 直接使用 ActionName 执行操作
            if (!string.IsNullOrEmpty(e.ActionName))
            {
                ExecuteHotkeyAction(e.ActionName);
            }
        }

        private void LoadHotkeyBindings()
        {
            // 主要程序
            HotkeyBindings.Add(new HotkeyBinding { Name = "AutoHPMA", Description = "启动或终止AutoHPMA", Modifiers = ModifierKeys.None, Key = Key.None, Group = "主要程序" });
            // 基础功能
            HotkeyBindings.Add(new HotkeyBinding { Name = "截图", Description = "截图当前游戏页面", Modifiers = ModifierKeys.None, Key = Key.None, Group = "基础功能" });

            // 任务启动
            
            HotkeyBindings.Add(new HotkeyBinding { Name = "社团答题", Description = "开始/停止 社团答题", Modifiers = ModifierKeys.None, Key = Key.None, Group = "任务启动" });
            HotkeyBindings.Add(new HotkeyBinding { Name = "禁林探索", Description = "开始/停止 禁林探索", Modifiers = ModifierKeys.None, Key = Key.None, Group = "任务启动" });
            HotkeyBindings.Add(new HotkeyBinding { Name = "自动烹饪", Description = "开始/停止 自动烹饪", Modifiers = ModifierKeys.None, Key = Key.None, Group = "任务启动" });
            HotkeyBindings.Add(new HotkeyBinding { Name = "甜蜜冒险", Description = "开始/停止 甜蜜冒险", Modifiers = ModifierKeys.None, Key = Key.None, Group = "任务启动" });

            // 从设置中加载保存的热键
            foreach (var binding in HotkeyBindings)
            {
                if (_settings.HotkeyBindings.TryGetValue(binding.Name, out var hotkeyStr))
                {
                    ParseHotkeyString(hotkeyStr, out var mod, out var key);
                    binding.Modifiers = mod;
                    binding.Key = key;
                    _logger.LogDebug($"Loaded hotkey binding: {binding.Name} -> {mod}+{key}");
                }
            }
        }

        public void ChangeHotkey(HotkeyBinding binding)
        {
            SelectedBinding = binding;
            IsWaitingForKey = true;
        }

        public void OnKeyDown(Key key, ModifierKeys modifiers)
        {
            _logger.LogDebug($"OnKeyDown called with key={key}, modifiers={modifiers}");
            if (!IsWaitingForKey || SelectedBinding == null)
                return;

            if (key == Key.Escape)
            {
                SelectedBinding.Key = Key.None;
                SelectedBinding.Modifiers = ModifierKeys.None;
                IsWaitingForKey = false;
                _settings.HotkeyBindings[SelectedBinding.Name] = "";
                _settings.Save();
                RegisterAllHotkeys();
                _logger.LogDebug($"Cancelled hotkey binding for: {SelectedBinding.Name}");
                return;
            }

            if (HotkeyBindings.Any(b => b != SelectedBinding && b.Key == key && b.Modifiers == modifiers))
            {
                var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "⚠️ 热键冲突",
                    Content = "该热键已被其他功能使用，请选择其他按键。",
                };
                _ = uiMessageBox.ShowDialogAsync();
                return;
            }

            SelectedBinding.Key = key;
            SelectedBinding.Modifiers = modifiers;
            IsWaitingForKey = false;
            _settings.HotkeyBindings[SelectedBinding.Name] = HotkeyToString(modifiers, key);
            _settings.Save();
            RegisterAllHotkeys();
            _logger.LogInformation($"Set hotkey binding: {SelectedBinding.Name} -> {HotkeyToString(modifiers, key)}");
        }

        private void RegisterAllHotkeys()
        {
            if (_keyboardHookManager == null) return;
            
            // 先注销所有热键
            _keyboardHookManager.UnregisterAll();
            
            // 重新注册所有热键
            foreach (var binding in HotkeyBindings)
            {
                if (binding.Key != Key.None)
                {
                    try
                    {
                        _keyboardHookManager.RegisterHotkey(binding.Name, binding.Modifiers, binding.Key);
                        _logger.LogDebug($"Registered hotkey: {binding.Name} -> {binding.Modifiers}+{binding.Key}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"注册热键失败: {binding.Name}");
                    }
                }
            }
        }

        private string HotkeyToString(ModifierKeys mod, Key key)
        {
            string s = "";
            if ((mod & ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((mod & ModifierKeys.Alt) != 0) s += "Alt+";
            if ((mod & ModifierKeys.Shift) != 0) s += "Shift+";
            if ((mod & ModifierKeys.Windows) != 0) s += "Win+";
            s += key.ToString();
            return s;
        }

        private void ParseHotkeyString(string str, out ModifierKeys mod, out Key key)
        {
            mod = ModifierKeys.None;
            key = Key.None;
            if (string.IsNullOrEmpty(str)) return;
            var parts = str.Split('+');
            foreach (var p in parts)
            {
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mod |= ModifierKeys.Control;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mod |= ModifierKeys.Alt;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mod |= ModifierKeys.Shift;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mod |= ModifierKeys.Windows;
                else if (Enum.TryParse<Key>(p, out var k)) key = k;
            }
        }

        /// <summary>
        /// 执行热键操作
        /// </summary>
        public void ExecuteHotkeyAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return;

            // 使用字典映射替代 switch-case 和反射
            var actions = new Dictionary<string, Action>
            {
                ["截图"] = ExecuteScreenshot,
                ["AutoHPMA"] = ExecuteAutoHPMAToggle,
                ["社团答题"] = () => GetTaskVM()?.ToggleAutoClubQuiz(),
                ["禁林探索"] = () => GetTaskVM()?.ToggleAutoForbiddenForest(),
                ["自动烹饪"] = () => GetTaskVM()?.ToggleAutoCooking(),
                ["甜蜜冒险"] = () => GetTaskVM()?.ToggleAutoSweetAdventure(),
            };

            if (actions.TryGetValue(actionName, out var action))
            {
                action();
            }
        }

        private TaskViewModel? GetTaskVM() => App.Services.GetService(typeof(TaskViewModel)) as TaskViewModel;
        private DashboardViewModel? GetDashboardVM() => App.Services.GetService(typeof(DashboardViewModel)) as DashboardViewModel;

        private void ExecuteScreenshot()
        {
            var capture = _appContextService.Capture;
            
            if (capture == null)
            {
                _ = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "⚠️ 错误",
                    Content = "截图失败。请先启动截图器!",
                }.ShowDialogAsync();
                return;
            }
            
            try
            {
                using var frame = capture.Capture();
                if (frame != null)
                {
                    var bitmap = frame.ToBitmap();
                    
                    string folderPath = Path.Combine(Environment.CurrentDirectory, "Captures");
                    Directory.CreateDirectory(folderPath);
                    
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string filename = $"screenshot_{timestamp}.png";
                    string fullPath = Path.Combine(folderPath, filename);
                    
                    bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
                    bitmap.Dispose();
                    
                    _logger.LogInformation($"截图已保存到: {fullPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "截图保存失败");
            }
        }

        private void ExecuteAutoHPMAToggle()
        {
            var dashboardVM = GetDashboardVM();
            if (dashboardVM == null) return;

            if (dashboardVM.ToggleButtonEnabled)
                dashboardVM.ToggleAutoHPMA();
        }

        public void Dispose()
        {
            if (_keyboardHookManager != null)
            {
                _keyboardHookManager.HotkeyPressed -= OnHotkeyPressed;
            }
        }
    }

    public partial class HotkeyBinding : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        private ModifierKeys _modifiers;
        public ModifierKeys Modifiers
        {
            get => _modifiers;
            set
            {
                SetProperty(ref _modifiers, value);
                OnPropertyChanged(nameof(ModKeyTuple));
            }
        }

        private Key _key;
        public Key Key
        {
            get => _key;
            set
            {
                SetProperty(ref _key, value);
                OnPropertyChanged(nameof(ModKeyTuple));
            }
        }

        [ObservableProperty]
        private string _group = string.Empty;

        public Tuple<ModifierKeys, Key> ModKeyTuple => Tuple.Create(Modifiers, Key);
    }
}