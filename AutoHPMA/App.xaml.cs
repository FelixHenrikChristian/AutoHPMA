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
using AutoHPMA.Helpers;
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

            var appSettings = _host.Services.GetRequiredService<AppSettings>();
            PowerSaveHelper.SetPreventSleepWhileRunning(appSettings.PreventSleepWhileRunning);

            // 检查是否显示过使用条款
            if (!appSettings.HasShownTermsOfUse)
            {
                var termsWindow = new TermsOfUseWindow();
                var accepted = termsWindow.ShowDialog() == true;
                if (accepted)
                {
                    appSettings.HasShownTermsOfUse = true;
                    appSettings.Save();
                }
                else
                {
                    Application.Current.Shutdown();
                    return;
                }
            }
        }

        /// <summary>
        /// Occurs when the application is closing.
        /// </summary>
        private async void OnExit(object sender, ExitEventArgs e)
        {
            PowerSaveHelper.SetPreventSleepWhileRunning(false);

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
