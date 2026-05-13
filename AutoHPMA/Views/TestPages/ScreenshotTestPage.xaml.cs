using AutoHPMA.ViewModels.TestPages;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views.TestPages;

public sealed partial class ScreenshotTestPage : Page
{
    public ScreenshotTestViewModel ViewModel { get; }

    public ScreenshotTestPage()
    {
        ViewModel = App.GetService<ScreenshotTestViewModel>();
        InitializeComponent();
    }
}
