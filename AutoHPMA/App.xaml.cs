// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using AutoHPMA.GameTask;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Services;
using AutoHPMA.Services.Interface;
using AutoHPMA.ViewModels.Pages;
using AutoHPMA.ViewModels.Windows;
using AutoHPMA.Views.Pages;
using AutoHPMA.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using AutoHPMA.Config;
using AutoHPMA.Helpers.LogHelper;

namespace AutoHPMA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => 
            { 
                c.SetBasePath(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location));
                c.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // 注册配置服务
                services.AddSingleton<AppSettings>(sp => AppSettings.Load());

                var logWindow = new LogWindow();
                services.AddSingleton(logWindow);

                var maskWindow = new MaskWindow();
                services.AddSingleton(maskWindow);

                services.AddNavigationViewPageProvider();

                // 配置Serilog
                var logServiceSink = new LogServiceSink();
                var logFileSink = new LogFileSink();
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Sink(logServiceSink)  // 统一的UI日志Sink
                    .WriteTo.Sink(logFileSink)     // 文件日志Sink
                    .CreateLogger();

                services.AddLogging(c => c.AddSerilog());
                services.AddHostedService<ApplicationHostService>();

                // Theme manipulation
                services.AddSingleton<IThemeService, ThemeService>();

                // TaskBar manipulation
                services.AddSingleton<ITaskBarService, TaskBarService>();

                // Service containing navigation, same as INavigationWindow... but without window
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();

                // Main window with navigation
                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<TaskPage>();
                services.AddSingleton<TaskViewModel>();
                services.AddSingleton<LogPage>();
                services.AddSingleton<LogViewModel>();
                services.AddSingleton<TestPage>();
                services.AddSingleton<TestViewModel>();
                services.AddSingleton<NotificationSettingsPage>();
                services.AddSingleton<NotificationSettingsViewModel>();
                services.AddSingleton<HotkeySettingsPage>();
                services.AddSingleton<HotkeySettingsViewModel>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();

                services.AddSingleton<CookingConfigService>();
                services.AddSingleton<IOcrService, OcrService>();
                
                // 注册更新服务
                services.AddSingleton<IUpdateService, UpdateService>();
        }).Build();

        public static ILogger<T> GetLogger<T>()
        {
            return _host.Services.GetService<ILogger<T>>()!;
        }

        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

        /// <summary>
        /// Occurs when the application is loading.
        /// </summary>
        private async void OnStartup(object sender, StartupEventArgs e)
        {
            await _host.StartAsync();

            // 检查是否显示过使用条款
            var settings = AppSettings.Load();
            if (!settings.HasShownTermsOfUse)
            {
                var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "使用条款",
                    Content =
                    "1. 本软件免费开源，仅供学习和研究使用，旨在为学术和研究人员提供参考和资料，任何其他目的均不适用。\n\n" +
                    "2. 严禁将本软件用于任何商业或非法用途。对于因违反此规定而产生的任何法律后果，用户需自行承担全部责任。\n\n" +
                    "3. 软件目录下的所有资源信息均来源于网络。如有关于版权的争议或问题，请联系原作者或权利人。\n\n" +
                    "4. 由于用户计算机软硬件环境的差异性和复杂性，本软件所提供的各项功能并不能保证在任何情况下都能正常执行或达到用户所期望的结果。 用户使用本软件所产生的一切后果，软件作者不承担任何责任。\n\n" +
                    "点击确定表示您同意以上条款。",
                    PrimaryButtonText = "确定",
                    CloseButtonText = "退出",
                };

                var result = await uiMessageBox.ShowDialogAsync();
                if(result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    settings.HasShownTermsOfUse = true;
                    settings.Save();
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();

            _host.Dispose();
        }

        /// <summary>
        /// Occurs when an exception is thrown by an application but not handled.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        }
    }
}
