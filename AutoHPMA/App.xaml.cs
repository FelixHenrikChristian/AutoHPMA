using AutoHPMA.Activation;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Services;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Services;
using AutoHPMA.ViewModels;
using AutoHPMA.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

using Serilog;

namespace AutoHPMA;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();

        LoggingHelper.ConfigureSerilog();
        LoggingHelper.LogStartupBanner();
        AppNotificationHelper.Register();

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(dispose: true);
        }).
        ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            // Other Activation Handlers

            // Services
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IInfoBarNotificationService, InfoBarNotificationService>();
            services.AddSingleton<IHotkeyService, HotkeyService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Core Services
            services.AddSingleton<IFileService, FileService>();

            // Views and ViewModels
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<HomeViewModel>();
            services.AddTransient<HomePage>();
            services.AddTransient<TaskViewModel>();
            services.AddTransient<TaskPage>();
            services.AddTransient<LogViewModel>();
            services.AddTransient<LogPage>();
            services.AddTransient<TestViewModel>();
            services.AddTransient<TestPage>();
            services.AddTransient<HotkeySettingsViewModel>();
            services.AddTransient<HotkeySettingsPage>();
            services.AddTransient<NotificationSettingsViewModel>();
            services.AddTransient<NotificationSettingsPage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "未处理异常: {Message}", e.Message);
        Log.CloseAndFlush();
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await App.GetService<IActivationService>().ActivateAsync(args);
    }
}
