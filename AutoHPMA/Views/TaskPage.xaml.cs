using AutoHPMA.ViewModels;
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
}

