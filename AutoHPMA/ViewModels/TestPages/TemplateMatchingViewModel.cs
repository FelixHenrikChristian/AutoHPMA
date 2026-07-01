using System.Collections.ObjectModel;
using System.Globalization;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenCvSharp;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace AutoHPMA.ViewModels.TestPages;

public partial class TemplateMatchingViewModel : ObservableObject
{
    private readonly ITemplateMatchingService _templateMatchingService;
    private readonly IInfoBarNotificationService _infoBar;
    private IReadOnlyList<TemplateMatchRegion> _matchRegions = [];

    public TemplateMatchingViewModel(
        ITemplateMatchingService templateMatchingService,
        IInfoBarNotificationService infoBar)
    {
        _templateMatchingService = templateMatchingService;
        _infoBar = infoBar;
    }

    public ObservableCollection<TemplateMatchModes> MatchModes { get; } =
        new((TemplateMatchModes[])Enum.GetValues(typeof(TemplateMatchModes)));

    public Visibility SourcePreviewVisibility =>
        SourceImagePreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SourcePlaceholderVisibility =>
        SourceImagePreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TemplatePreviewVisibility =>
        TemplateImagePreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TemplatePlaceholderVisibility =>
        TemplateImagePreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MaskPreviewVisibility =>
        MaskImagePreview is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MaskPlaceholderVisibility =>
        MaskImagePreview is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResultPreviewVisibility =>
        ResultImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ResultPlaceholderVisibility =>
        ResultImage is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MatchActionsVisibility =>
        MatchCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProgressVisibility =>
        IsMatching ? Visibility.Visible : Visibility.Collapsed;

    public string ThresholdText => Threshold.ToString("F2");

    [ObservableProperty]
    public partial string? SourceImagePath { get; set; }

    [ObservableProperty]
    public partial string? TemplateImagePath { get; set; }

    [ObservableProperty]
    public partial string? MaskImagePath { get; set; }

    [ObservableProperty]
    public partial double Threshold { get; set; } = 0.8;

    [ObservableProperty]
    public partial TemplateMatchModes SelectedMatchMode { get; set; } = TemplateMatchModes.CCoeffNormed;

    [ObservableProperty]
    public partial ImageSource? SourceImagePreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? TemplateImagePreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? MaskImagePreview { get; set; }

    [ObservableProperty]
    public partial ImageSource? ResultImage { get; set; }

    [ObservableProperty]
    public partial int MatchCount { get; set; }

    [ObservableProperty]
    public partial string MatchRectInfo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMatching { get; set; }

    [RelayCommand]
    private async Task SelectSourceImageAsync()
    {
        var file = await PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        SourceImagePath = file.Path;
        SourceImagePreview = await CreateBitmapImageAsync(file);
        ClearResult();
    }

    [RelayCommand]
    private async Task SelectTemplateImageAsync()
    {
        var file = await PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        TemplateImagePath = file.Path;
        TemplateImagePreview = await CreateBitmapImageAsync(file);
        if (string.IsNullOrWhiteSpace(MaskImagePath))
        {
            MaskImagePreview = null;
        }

        ClearResult();
    }

    [RelayCommand]
    private async Task SelectMaskImageAsync()
    {
        var file = await PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        MaskImagePath = file.Path;
        MaskImagePreview = await CreateBitmapImageAsync(file);
        ClearResult();
    }

    [RelayCommand]
    private void ClearMask()
    {
        MaskImagePath = null;
        MaskImagePreview = null;
        ClearResult();
    }

    [RelayCommand(CanExecute = nameof(CanMatch))]
    private async Task TemplateMatchAsync()
    {
        IsMatching = true;

        try
        {
            var request = new TemplateMatchingRequest
            {
                SourceImagePath = SourceImagePath!,
                TemplateImagePath = TemplateImagePath!,
                MaskImagePath = MaskImagePath,
                MatchMode = SelectedMatchMode,
                Threshold = Threshold,
            };

            var result = await Task.Run(() => _templateMatchingService.Match(request));

            _matchRegions = result.Regions;
            MatchCount = result.Regions.Count;
            MatchRectInfo = FormatRegions(result.Regions);
            ResultImage = await CreateBitmapImageAsync(result.AnnotatedImageBytes);

            if (result.MaskImageBytes is not null && string.IsNullOrWhiteSpace(MaskImagePath))
            {
                MaskImagePreview = await CreateBitmapImageAsync(result.MaskImageBytes);
            }

            if (MatchCount == 0)
            {
                ShowError("未找到匹配区域，请调整阈值或匹配模式。");
            }
            else
            {
                ShowSuccess($"找到 {MatchCount} 处匹配。");
            }
        }
        catch (Exception ex)
        {
            ShowError($"模板匹配失败：{ex.Message}");
        }
        finally
        {
            IsMatching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCrop))]
    private async Task CropImageAsync()
    {
        try
        {
            var savedFiles = await Task.Run(() =>
                _templateMatchingService.CropMatches(SourceImagePath!, _matchRegions));
            ShowSuccess($"已成功裁切 {savedFiles.Count} 个区域。");
        }
        catch (Exception ex)
        {
            ShowError($"裁切失败：{ex.Message}");
        }
    }

    private bool CanMatch()
        => !IsMatching &&
           !string.IsNullOrWhiteSpace(SourceImagePath) &&
           !string.IsNullOrWhiteSpace(TemplateImagePath);

    private bool CanCrop()
        => !IsMatching &&
           !string.IsNullOrWhiteSpace(SourceImagePath) &&
           _matchRegions.Count > 0;

    private void ClearResult()
    {
        _matchRegions = [];
        MatchCount = 0;
        MatchRectInfo = string.Empty;
        ResultImage = null;
    }

    private static string FormatRegions(IReadOnlyList<TemplateMatchRegion> regions)
    {
        if (regions.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine,
            regions.Select(r =>
                $"X: {r.X}, Y: {r.Y}, Width: {r.Width}, Height: {r.Height}{FormatScore(r.Score)}"));
    }

    private static string FormatScore(double? score)
    {
        if (!score.HasValue)
        {
            return string.Empty;
        }

        var value = score.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return string.Empty;
        }

        return $", Score: {(Math.Clamp(value, 0d, 1d) * 100d).ToString("0.0", CultureInfo.InvariantCulture)}%";
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

    partial void OnSourceImagePathChanged(string? value)
        => TemplateMatchCommand.NotifyCanExecuteChanged();

    partial void OnTemplateImagePathChanged(string? value)
        => TemplateMatchCommand.NotifyCanExecuteChanged();

    partial void OnThresholdChanged(double value)
        => OnPropertyChanged(nameof(ThresholdText));

    partial void OnSourceImagePreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(SourcePreviewVisibility));
        OnPropertyChanged(nameof(SourcePlaceholderVisibility));
    }

    partial void OnTemplateImagePreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(TemplatePreviewVisibility));
        OnPropertyChanged(nameof(TemplatePlaceholderVisibility));
    }

    partial void OnMaskImagePreviewChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(MaskPreviewVisibility));
        OnPropertyChanged(nameof(MaskPlaceholderVisibility));
    }

    partial void OnResultImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(ResultPreviewVisibility));
        OnPropertyChanged(nameof(ResultPlaceholderVisibility));
    }

    partial void OnMatchCountChanged(int value)
    {
        OnPropertyChanged(nameof(MatchActionsVisibility));
        CropImageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMatchingChanged(bool value)
    {
        OnPropertyChanged(nameof(ProgressVisibility));
        TemplateMatchCommand.NotifyCanExecuteChanged();
        CropImageCommand.NotifyCanExecuteChanged();
    }
}
