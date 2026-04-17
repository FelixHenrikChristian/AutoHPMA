using AutoHPMA.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views;

public sealed partial class NotificationSettingsPage : Page
{
    public NotificationSettingsViewModel ViewModel { get; }

    public NotificationSettingsPage()
    {
        ViewModel = App.GetService<NotificationSettingsViewModel>();
        InitializeComponent();
    }
}

