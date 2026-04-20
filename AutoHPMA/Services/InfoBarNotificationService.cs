using AutoHPMA.Contracts.Services;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Services;

public sealed class InfoBarNotificationService : IInfoBarNotificationService
{
    private static readonly TimeSpan DefaultDismiss = TimeSpan.FromSeconds(4);

    private InfoBar? _presenter;
    private DispatcherQueueTimer? _dismissTimer;

    public void Register(InfoBar presenter)
    {
        _presenter = presenter;
    }

    public void Show(InfoBarSeverity severity, string title, string message, TimeSpan? autoDismiss = null)
    {
        var dq = global::AutoHPMA.App.MainWindow.DispatcherQueue;
        _ = dq.TryEnqueue(() => ShowCore(severity, title, message, autoDismiss));
    }

    private void ShowCore(InfoBarSeverity severity, string title, string message, TimeSpan? autoDismiss)
    {
        if (_presenter == null)
        {
            return;
        }

        if (_dismissTimer is not null)
        {
            _dismissTimer.Stop();
            _dismissTimer.Tick -= OnDismissTick;
        }

        _presenter.Severity = severity;
        _presenter.Title = title;
        _presenter.Message = message;
        _presenter.IsClosable = false;
        _presenter.IsOpen = true;

        var delay = autoDismiss ?? DefaultDismiss;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var dq = _presenter.DispatcherQueue;
        _dismissTimer ??= dq.CreateTimer();
        _dismissTimer.Interval = delay;
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Tick += OnDismissTick;
        _dismissTimer.Start();
    }

    private void OnDismissTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        sender.Tick -= OnDismissTick;
        if (_presenter != null)
        {
            _presenter.IsOpen = false;
        }
    }
}
