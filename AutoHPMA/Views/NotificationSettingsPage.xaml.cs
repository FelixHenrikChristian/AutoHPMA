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

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.LoadAsync();
    }
}

