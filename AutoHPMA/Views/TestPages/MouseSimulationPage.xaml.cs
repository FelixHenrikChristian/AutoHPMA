using AutoHPMA.ViewModels.TestPages;
using AutoHPMA.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views.TestPages;

public sealed partial class MouseSimulationPage : Page
{
    public MouseSimulationViewModel ViewModel { get; }

    public MouseSimulationPage()
    {
        ViewModel = App.GetService<MouseSimulationViewModel>();
        InitializeComponent();
    }

    private void MoveActionUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MouseActionModel action)
        {
            ViewModel.MoveActionUpCommand.Execute(action);
        }
    }

    private void MoveActionDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MouseActionModel action)
        {
            ViewModel.MoveActionDownCommand.Execute(action);
        }
    }

    private void RemoveMouseAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MouseActionModel action)
        {
            ViewModel.RemoveMouseActionCommand.Execute(action);
        }
    }
}
