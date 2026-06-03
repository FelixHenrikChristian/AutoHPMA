using AutoHPMA.ViewModels.TestPages;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AutoHPMA.Views.TestPages;

public sealed partial class TextRecognitionPage : Page
{
    public TextRecognitionViewModel ViewModel { get; }

    public TextRecognitionPage()
    {
        ViewModel = App.GetService<TextRecognitionViewModel>();
        InitializeComponent();
    }

    private async void PreviewImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.OcrPreviewImage is null)
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
                    Source = ViewModel.OcrPreviewImage,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    MaxWidth = 960,
                    MaxHeight = 720,
                },
            },
        };

        await dialog.ShowAsync();
    }
}
