// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using AutoHPMA.Messages;
using AutoHPMA.Models;
using AutoHPMA.Services.Interface;
using AutoHPMA.ViewModels.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using System.Windows.Interop;
using AutoHPMA.Services;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using AutoHPMA.ViewModels.Pages;
using System.Runtime.InteropServices;
using System.Linq;
using System.Collections.Concurrent;

namespace AutoHPMA.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        private readonly ISnackbarService _snackbarService;
        private readonly IUpdateService _updateService;
        private readonly ILogger<MainWindow> _logger;
        private static KeyboardHookManager? _keyboardHookManager;

        public MainWindowViewModel ViewModel { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService,
            ISnackbarService snackbarService,
            IUpdateService updateService)
        {
            ViewModel = viewModel;
            DataContext = this;
            _logger = App.GetLogger<MainWindow>();
            _snackbarService = snackbarService;
            _updateService = updateService;

            InitializeComponent();

            SystemThemeWatcher.Watch(this);

            _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

            // 注册消息接收器
            WeakReferenceMessenger.Default.Register<ShowSnackbarMessage>(this, (r, message) =>
            {
                var info = message.Value;
                _snackbarService.Show(
                    info.Title,
                    info.Message,
                    info.Appearance,
                    info.Icon,
                    info.Duration);
            });

            SetPageService(navigationViewPageProvider);
            navigationService.SetNavigationControl(RootNavigation);
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }



        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化键盘钩子管理器
            if (_keyboardHookManager == null)
            {
                _keyboardHookManager = new KeyboardHookManager();
                // 设置初始目标窗口
                _keyboardHookManager.SetTargetWindow(AppContextService.Instance.DisplayHwnd);
                
                // 监听 DisplayHwnd 变化，动态更新目标窗口
                AppContextService.Instance.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(AppContextService.DisplayHwnd))
                    {
                        _keyboardHookManager.SetTargetWindow(AppContextService.Instance.DisplayHwnd);
                    }
                };
            }
            
            var hotkeyVM = App.Services.GetService(typeof(HotkeySettingsViewModel)) as HotkeySettingsViewModel;
            if (hotkeyVM != null)
            {
                hotkeyVM.SetKeyboardHookManager(_keyboardHookManager);
            }

            // 启动时自动检查更新
            await Task.Delay(500);
            await _updateService.CheckUpdateAsync(new UpdateOption { Trigger = UpdateTrigger.Auto });
        }

    }
}
