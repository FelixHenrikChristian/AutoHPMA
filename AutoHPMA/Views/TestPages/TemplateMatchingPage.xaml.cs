using AutoHPMA.ViewModels.TestPages;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AutoHPMA.Views.TestPages;

public sealed partial class TemplateMatchingPage : Page
{
    public TemplateMatchingViewModel ViewModel { get; }

    public TemplateMatchingPage()
    {
        ViewModel = App.GetService<TemplateMatchingViewModel>();
        InitializeComponent();
    }

    private async void PreviewImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Image { Source: not null } image)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "图像预览",
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image
                {
                    Source = image.Source,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    MaxWidth = 960,
                    MaxHeight = 720,
                },
            },
        };

        await dialog.ShowAsync();
    }
}
