using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace AutoHPMA.ViewModels.TestPages;

public partial class ContourDetectionViewModel : ObservableObject
{
    private readonly IContourDetectionService _contourDetectionService;
    private readonly IInfoBarNotificationService _infoBar;

    public ContourDetectionViewModel(
        IContourDetectionService contourDetectionService,
        IInfoBarNotificationService infoBar)
    {
        _contourDetectionService = contourDetectionService;
        _infoBar = infoBar;
    }

    public Visibility ContourPreviewVisibility =>
        ContourImagePreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ContourPlaceholderVisibility =>
        ContourImagePreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BinarizedPreviewVisibility =>
        BinarizedImagePreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BinarizedPlaceholderVisibility =>
        BinarizedImagePreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultPreviewVisibility =>
        ContourResultImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ResultPlaceholderVisibility =>
        ContourResultImage is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DetectedRectInfoVisibility =>
        string.IsNullOrWhiteSpace(DetectedRectInfo) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ProgressVisibility =>
        IsProcessing ? Visibility.Visible : Visibility.Collapsed;

    public string ContourImagePathText =>
        string.IsNullOrWhiteSpace(ContourImagePath) ? "请选择要检测的图像" : ContourImagePath;

    public string BinarizeThresholdText => BinarizeThreshold.ToString("F0");

    [ObservableProperty]
    public partial string? ContourImagePath { get; set; }

    [ObservableProperty]
    public partial ImageSource? ContourImagePreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? BinarizedImagePreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? ContourResultImage { get; set; }

    [ObservableProperty]
    public partial double BinarizeThreshold { get; set; } = 200;

    [ObservableProperty]
    public partial string DetectedRectInfo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [RelayCommand]
    private async Task SelectContourImageAsync()
    {
        var file = await PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        ContourImagePath = file.Path;
        ContourImagePreview = await CreateBitmapImageAsync(file);
        ClearResult();
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task TestBinarizeAsync()
    {
        IsProcessing = true;

        try
        {
            var bytes = await Task.Run(() => _contourDetectionService.Binarize(CreateRequest()));
            BinarizedImagePreview = await CreateBitmapImageAsync(bytes);
            ShowSuccess("二值化完成。");
        }
        catch (Exception ex)
        {
            ShowError($"二值化失败：{ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task TestDetectRectangleAsync()
    {
        IsProcessing = true;

        try
        {
            var result = await Task.Run(() => _contourDetectionService.DetectApproxRectangle(CreateRequest()));
            BinarizedImagePreview = await CreateBitmapImageAsync(result.BinarizedImageBytes);
            ContourResultImage = await CreateBitmapImageAsync(result.AnnotatedImageBytes);
            DetectedRectInfo = FormatDetectedRectangle(result.Rectangle);

            if (result.Rectangle is null)
            {
                ShowError("未检测到有效矩形。");
            }
            else
            {
                ShowSuccess("矩形检测完成。");
            }
        }
        catch (Exception ex)
        {
            ShowError($"矩形检测失败：{ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanProcess()
        => !IsProcessing && !string.IsNullOrWhiteSpace(ContourImagePath);

    private ContourDetectionRequest CreateRequest()
        => new()
        {
            ImagePath = ContourImagePath!,
            Threshold = BinarizeThreshold,
        };

    private void ClearResult()
    {
        BinarizedImagePreview = null;
        ContourResultImage = null;
        DetectedRectInfo = string.Empty;
    }

    private static string FormatDetectedRectangle(ContourDetectionRectangle? rectangle)
        => rectangle is null
            ? "未检测到有效矩形"
            : $"X: {rectangle.X}, Y: {rectangle.Y}, 宽: {rectangle.Width}, 高: {rectangle.Height}";

    private static FileOpenPicker CreateImageOpenPicker()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };

        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".tiff");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        return picker;
    }

    private static async Task<StorageFile?> PickImageFileAsync()
        => await CreateImageOpenPicker().PickSingleFileAsync();

    private static async Task<BitmapImage> CreateBitmapImageAsync(StorageFile file)
    {
        using var stream = await file.OpenReadAsync();
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }

        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    private void ShowError(string message)
        => _infoBar.Show(InfoBarSeverity.Error, "错误", message);

    private void ShowSuccess(string message)
        => _infoBar.Show(InfoBarSeverity.Success, "成功", message);

    partial void OnContourImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(ContourImagePathText));
        TestBinarizeCommand.NotifyCanExecuteChanged();
        TestDetectRectangleCommand.NotifyCanExecuteChanged();
    }

    partial void OnContourImagePreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(ContourPreviewVisibility));
        OnPropertyChanged(nameof(ContourPlaceholderVisibility));
    }

    partial void OnBinarizedImagePreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(BinarizedPreviewVisibility));
        OnPropertyChanged(nameof(BinarizedPlaceholderVisibility));
    }

    partial void OnContourResultImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(ResultPreviewVisibility));
        OnPropertyChanged(nameof(ResultPlaceholderVisibility));
    }

    partial void OnBinarizeThresholdChanged(double value)
        => OnPropertyChanged(nameof(BinarizeThresholdText));

    partial void OnDetectedRectInfoChanged(string value)
        => OnPropertyChanged(nameof(DetectedRectInfoVisibility));

    partial void OnIsProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(ProgressVisibility));
        TestBinarizeCommand.NotifyCanExecuteChanged();
        TestDetectRectangleCommand.NotifyCanExecuteChanged();
    }
}
