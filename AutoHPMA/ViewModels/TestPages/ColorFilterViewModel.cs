using System.Collections.ObjectModel;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;

namespace AutoHPMA.ViewModels.TestPages;

public partial class ColorFilterViewModel : ObservableObject
{
    private readonly IColorFilterService _colorFilterService;
    private readonly IInfoBarNotificationService _infoBar;
    private bool _isSyncingColor;

    public ColorFilterViewModel(
        IColorFilterService colorFilterService,
        IInfoBarNotificationService infoBar)
    {
        _colorFilterService = colorFilterService;
        _infoBar = infoBar;
    }

    public ObservableCollection<ColorFilterColorSpace> ColorSpaceOptions { get; } =
        new((ColorFilterColorSpace[])Enum.GetValues(typeof(ColorFilterColorSpace)));

    public Visibility SourcePreviewVisibility =>
        ColorFilterSourcePreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SourcePlaceholderVisibility =>
        ColorFilterSourcePreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MaskPreviewVisibility =>
        ColorFilterMaskPreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MaskPlaceholderVisibility =>
        ColorFilterMaskPreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultPreviewVisibility =>
        ColorFilterResultImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ResultPlaceholderVisibility =>
        ColorFilterResultImage is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StatsVisibility =>
        string.IsNullOrWhiteSpace(ColorFilterStats) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ProgressVisibility =>
        IsFiltering ? Visibility.Visible : Visibility.Collapsed;

    public string SourceImagePathText =>
        string.IsNullOrWhiteSpace(ColorFilterSourcePath) ? "请选择源图像" : ColorFilterSourcePath;

    public string MaskImagePathText =>
        string.IsNullOrWhiteSpace(ColorFilterMaskPath) ? "未选择遮罩图像" : ColorFilterMaskPath;

    public string ColorThresholdText => ColorThreshold.ToString("F0");

    public string SaturationToleranceText => SaturationTolerance.ToString("F0");

    public string ValueToleranceText => ValueTolerance.ToString("F0");

    public string ColorSpaceDescription => SelectedColorSpace == ColorFilterColorSpace.LAB
        ? "LAB 通道容差 (更接近人眼感知)"
        : "HSV 通道容差 (按色相分离)";

    public string HueChannelLabel => SelectedColorSpace == ColorFilterColorSpace.LAB
        ? "b* (黄蓝)"
        : "H (色相)";

    public string SaturationChannelLabel => SelectedColorSpace == ColorFilterColorSpace.LAB
        ? "a* (红绿)"
        : "S (饱和度)";

    public string ValueChannelLabel => SelectedColorSpace == ColorFilterColorSpace.LAB
        ? "L (明度)"
        : "V (明度)";

    public Brush TargetColorBrush => new SolidColorBrush(TargetColor);

    [ObservableProperty]
    public partial string? ColorFilterSourcePath { get; set; }

    [ObservableProperty]
    public partial string? ColorFilterMaskPath { get; set; }

    [ObservableProperty]
    public partial string TargetColorHex { get; set; } = "ffffff";

    [ObservableProperty]
    public partial Color TargetColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial double ColorThreshold { get; set; } = 30;

    [ObservableProperty]
    public partial double SaturationTolerance { get; set; } = 100;

    [ObservableProperty]
    public partial double ValueTolerance { get; set; } = 100;

    [ObservableProperty]
    public partial ColorFilterColorSpace SelectedColorSpace { get; set; } = ColorFilterColorSpace.LAB;

    [ObservableProperty]
    public partial ImageSource? ColorFilterSourcePreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? ColorFilterMaskPreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? ColorFilterResultImage { get; set; }

    [ObservableProperty]
    public partial string ColorFilterStats { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFiltering { get; set; }

    [RelayCommand]
    private async Task SelectColorFilterSourceAsync()
    {
        var file = await PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        ColorFilterSourcePath = file.Path;
        ColorFilterSourcePreview = await CreateBitmapImageAsync(file);
        ClearResult();
    }

    [RelayCommand]
    private async Task SelectColorFilterMaskAsync()
    {
        var file = await PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        ColorFilterMaskPath = file.Path;
        ColorFilterMaskPreview = await CreateBitmapImageAsync(file);
        ClearResult();
    }

    [RelayCommand]
    private void ClearColorFilterMask()
    {
        ColorFilterMaskPath = null;
        ColorFilterMaskPreview = null;
        ClearResult();
    }

    [RelayCommand(CanExecute = nameof(CanFilter))]
    private async Task FilterColorAsync()
    {
        IsFiltering = true;

        try
        {
            var result = await Task.Run(() => _colorFilterService.Filter(CreateRequest()));
            ColorFilterResultImage = await CreateBitmapImageAsync(result.FilteredImageBytes);
            ColorFilterStats = $"[{SelectedColorSpace}] 参与: {result.TotalFilterPixels} | 匹配: {result.MatchedPixels} | 占比: {result.MatchPercentage:F2}%";
            ShowSuccess("色彩过滤完成。");
        }
        catch (Exception ex)
        {
            ShowError($"色彩过滤失败：{ex.Message}");
        }
        finally
        {
            IsFiltering = false;
        }
    }

    private bool CanFilter()
        => !IsFiltering &&
           !string.IsNullOrWhiteSpace(ColorFilterSourcePath) &&
           TryCreateColor(TargetColorHex, out _);

    private ColorFilterRequest CreateRequest()
        => new()
        {
            SourceImagePath = ColorFilterSourcePath!,
            MaskImagePath = ColorFilterMaskPath,
            TargetColorHex = TargetColorHex,
            HueThreshold = ClampToInt(ColorThreshold, 0, 90),
            SaturationTolerance = ClampToInt(SaturationTolerance, 0, 255),
            ValueTolerance = ClampToInt(ValueTolerance, 0, 255),
            ColorSpace = SelectedColorSpace,
        };

    private void ClearResult()
    {
        ColorFilterResultImage = null;
        ColorFilterStats = string.Empty;
    }

    private static int ClampToInt(double value, int min, int max)
        => Math.Clamp((int)Math.Round(value), min, max);

    private static bool TryCreateColor(string? hex, out Color color)
    {
        color = Colors.Red;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return false;
        }

        color = Color.FromArgb(255, red, green, blue);
        return true;
    }

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

    partial void OnColorFilterSourcePathChanged(string? value)
    {
        OnPropertyChanged(nameof(SourceImagePathText));
        FilterColorCommand.NotifyCanExecuteChanged();
    }

    partial void OnColorFilterMaskPathChanged(string? value)
        => OnPropertyChanged(nameof(MaskImagePathText));

    partial void OnColorFilterSourcePreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(SourcePreviewVisibility));
        OnPropertyChanged(nameof(SourcePlaceholderVisibility));
    }

    partial void OnColorFilterMaskPreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(MaskPreviewVisibility));
        OnPropertyChanged(nameof(MaskPlaceholderVisibility));
    }

    partial void OnColorFilterResultImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(ResultPreviewVisibility));
        OnPropertyChanged(nameof(ResultPlaceholderVisibility));
    }

    partial void OnColorFilterStatsChanged(string value)
        => OnPropertyChanged(nameof(StatsVisibility));

    partial void OnTargetColorHexChanged(string value)
    {
        FilterColorCommand.NotifyCanExecuteChanged();

        if (_isSyncingColor || !TryCreateColor(value, out var color))
        {
            return;
        }

        _isSyncingColor = true;
        TargetColor = color;
        _isSyncingColor = false;
        OnPropertyChanged(nameof(TargetColorBrush));
    }

    partial void OnTargetColorChanged(Color value)
    {
        OnPropertyChanged(nameof(TargetColorBrush));

        if (_isSyncingColor)
        {
            return;
        }

        _isSyncingColor = true;
        TargetColorHex = $"{value.R:X2}{value.G:X2}{value.B:X2}".ToLowerInvariant();
        _isSyncingColor = false;
    }

    partial void OnColorThresholdChanged(double value)
        => OnPropertyChanged(nameof(ColorThresholdText));

    partial void OnSaturationToleranceChanged(double value)
        => OnPropertyChanged(nameof(SaturationToleranceText));

    partial void OnValueToleranceChanged(double value)
        => OnPropertyChanged(nameof(ValueToleranceText));

    partial void OnSelectedColorSpaceChanged(ColorFilterColorSpace value)
    {
        OnPropertyChanged(nameof(ColorSpaceDescription));
        OnPropertyChanged(nameof(HueChannelLabel));
        OnPropertyChanged(nameof(SaturationChannelLabel));
        OnPropertyChanged(nameof(ValueChannelLabel));
    }

    partial void OnIsFilteringChanged(bool value)
    {
        OnPropertyChanged(nameof(ProgressVisibility));
        FilterColorCommand.NotifyCanExecuteChanged();
    }
}
