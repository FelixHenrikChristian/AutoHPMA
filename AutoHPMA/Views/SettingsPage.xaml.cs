using AutoHPMA.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.LoadAsync();
    }
}
