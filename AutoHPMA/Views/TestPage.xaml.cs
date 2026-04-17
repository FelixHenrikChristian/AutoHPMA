using AutoHPMA.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views;

public sealed partial class TestPage : Page
{
    public TestViewModel ViewModel { get; }

    public TestPage()
    {
        ViewModel = App.GetService<TestViewModel>();
        InitializeComponent();
    }
}

