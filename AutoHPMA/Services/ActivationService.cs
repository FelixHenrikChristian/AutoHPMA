using AutoHPMA.Activation;
using AutoHPMA.Configuration;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers;
using AutoHPMA.Views;
using AutoHPMA.Views.Dialogs;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILocalSettingsService _localSettingsService;
    private UIElement? _shell = null;

    public ActivationService(
        ActivationHandler<LaunchActivatedEventArgs> defaultHandler,
        IEnumerable<IActivationHandler> activationHandlers,
        IThemeSelectorService themeSelectorService,
        ILocalSettingsService localSettingsService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        // Execute tasks before activation.
        await InitializeAsync();

        // Set the MainWindow Content.
        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        // Handle activation via ActivationHandlers.
        await HandleActivationAsync(activationArgs);

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // 检查是否已接受使用条款（使用 ContentDialog 在 MainWindow 内以模态方式显示）
        var hasShownTerms = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.HasShownTermsOfUse);
        if (hasShownTerms != true)
        {
            // XamlRoot 在内容渲染完成后才可用，需要等待 Loaded 事件
            var shell = App.MainWindow.Content as FrameworkElement;
            if (shell != null)
            {
                if (shell.XamlRoot == null)
                {
                    var tcs = new TaskCompletionSource();
                    shell.Loaded += (_, _) => tcs.TrySetResult();
                    await tcs.Task;
                }

                var dialog = new TermsOfUseDialog { XamlRoot = shell.XamlRoot };
                await dialog.ShowAsync();

                if (dialog.Accepted)
                {
                    await _localSettingsService.SaveSettingAsync(SettingsKeys.HasShownTermsOfUse, true);
                }
                else
                {
                    Application.Current.Exit();
                    return;
                }
            }
        }

        // Execute tasks after activation.
        await StartupAsync();
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
        {
            await activationHandler.HandleAsync(activationArgs);
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await Task.CompletedTask;
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();

        var preventSleep = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.PreventSleepWhileRunning);
        PowerSaveHelper.SetPreventSleepWhileRunning(preventSleep ?? true);

        var notificationEnabled = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.NotificationEnabled);
        AppNotificationHelper.IsEnabled = notificationEnabled ?? true;

        var notificationSound = await _localSettingsService.ReadSettingAsync<bool?>(SettingsKeys.NotificationSoundEnabled);
        AppNotificationHelper.IsSoundEnabled = notificationSound ?? true;

        await Task.CompletedTask;
    }
}
