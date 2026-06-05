using AutoHPMA.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views;

public sealed partial class TaskPage : Page
{
    public TaskViewModel ViewModel { get; }

    public TaskPage()
    {
        ViewModel = App.GetService<TaskViewModel>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
    }
}

