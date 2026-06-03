using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using AutoHPMA.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace AutoHPMA.ViewModels.TestPages;

public partial class TextRecognitionViewModel : ObservableObject
{
    private const int MaxScreenshotWaitMilliseconds = 60_000;
    private const int ClipboardCheckIntervalMilliseconds = 100;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    private readonly IOcrService _ocrService;
    private readonly IInfoBarNotificationService _infoBar;

    public TextRecognitionViewModel(IOcrService ocrService, IInfoBarNotificationService infoBar)
    {
        _ocrService = ocrService;
        _infoBar = infoBar;
    }

    public ObservableCollection<string> OCRs { get; } =
    [
        "PaddleOCR",
        "WindowsOCR",
        "RapidOCR",
        "TesseractOCR",
    ];

    public Visibility PreviewImageVisibility =>
        OcrPreviewImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PreviewPlaceholderVisibility =>
        OcrPreviewImage is null ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial string OcrResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedOCR { get; set; } = "PaddleOCR";

    [ObservableProperty]
    public partial ImageSource? OcrPreviewImage { get; set; }

    [ObservableProperty]
    public partial bool HideWindowOnScreenshot { get; set; }

    [ObservableProperty]
    public partial bool IsRecognizing { get; set; }

    [RelayCommand(CanExecute = nameof(CanStartRecognition))]
    private async Task OCRTestAsync()
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

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        IsRecognizing = true;
        OcrResult = "正在识别...";

        try
        {
            var bytes = await ReadFileBytesAsync(file);
            await RecognizeBytesAsync(bytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OCR 文件识别失败：{ex}");
            ShowError($"识别出错：{ex.Message}");
            OcrResult = $"识别出错：{ex.Message}";
        }
        finally
        {
            IsRecognizing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRecognition))]
    private async Task OCRFromScreenshotAsync()
    {
        var mainWindowHandle = WindowNative.GetWindowHandle(App.MainWindow);
        var minimized = false;

        IsRecognizing = true;
        OcrResult = "请完成截图...";

        try
        {
            if (HideWindowOnScreenshot)
            {
                minimized = ShowWindow(mainWindowHandle, SwMinimize);
                await Task.Delay(300);
            }

            Clipboard.Clear();
            StartScreenClip();

            var bytes = await WaitForClipboardBitmapAsync(mainWindowHandle);
            if (bytes is null)
            {
                OcrResult = "截图已取消";
                return;
            }

            OcrResult = "正在识别...";
            await RecognizeBytesAsync(bytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OCR 截图识别失败：{ex}");
            ShowError($"识别出错：{ex.Message}");
            OcrResult = $"识别出错：{ex.Message}";
        }
        finally
        {
            if (minimized)
            {
                _ = ShowWindow(mainWindowHandle, SwRestore);
                App.MainWindow.Activate();
            }

            IsRecognizing = false;
        }
    }

    private bool CanStartRecognition()
        => !IsRecognizing;

    private async Task RecognizeBytesAsync(byte[] bytes)
    {
        OcrPreviewImage = await CreateBitmapImageAsync(bytes);

        using var bitmap = await DecodeSoftwareBitmapAsync(bytes);
        var engineType = ParseEngineType(SelectedOCR);
        var result = await _ocrService.RecognizeAsync(bitmap, engineType);
        OcrResult = string.IsNullOrWhiteSpace(result) ? "未识别到文字" : result;
    }

    private static void StartScreenClip()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "ms-screenclip:",
            UseShellExecute = false,
        };
        Process.Start(startInfo);
    }

    private static async Task<byte[]?> WaitForClipboardBitmapAsync(IntPtr mainWindowHandle)
    {
        var elapsed = 0;
        var foregroundMainWindowCount = 0;

        while (elapsed < MaxScreenshotWaitMilliseconds)
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Bitmap))
                {
                    var bitmapReference = await content.GetBitmapAsync();
                    using var stream = await bitmapReference.OpenReadAsync();
                    return await ReadStreamBytesAsync(stream);
                }
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"读取剪贴板图片失败，等待重试：{ex.Message}");
            }

            if (mainWindowHandle != IntPtr.Zero && elapsed > 2_000)
            {
                if (GetForegroundWindow() == mainWindowHandle)
                {
                    foregroundMainWindowCount++;
                    if (foregroundMainWindowCount >= 3)
                    {
                        break;
                    }
                }
                else
                {
                    foregroundMainWindowCount = 0;
                }
            }

            await Task.Delay(ClipboardCheckIntervalMilliseconds);
            elapsed += ClipboardCheckIntervalMilliseconds;
        }

        return null;
    }

    private static async Task<byte[]> ReadFileBytesAsync(StorageFile file)
    {
        var buffer = await FileIO.ReadBufferAsync(file);
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<byte[]> ReadStreamBytesAsync(IRandomAccessStream stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes)
    {
        using var stream = await CreateRandomAccessStreamAsync(bytes);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    private static async Task<SoftwareBitmap> DecodeSoftwareBitmapAsync(byte[] bytes)
    {
        using var stream = await CreateRandomAccessStreamAsync(bytes);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static async Task<InMemoryRandomAccessStream> CreateRandomAccessStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        return stream;
    }

    private static OcrEngineType ParseEngineType(string engineName)
        => Enum.TryParse<OcrEngineType>(engineName, out var engineType)
            ? engineType
            : OcrEngineType.WindowsOCR;

    private void ShowError(string message)
        => _infoBar.Show(InfoBarSeverity.Error, "错误", message);

    partial void OnOcrPreviewImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(PreviewImageVisibility));
        OnPropertyChanged(nameof(PreviewPlaceholderVisibility));
    }

    partial void OnIsRecognizingChanged(bool value)
    {
        OCRTestCommand.NotifyCanExecuteChanged();
        OCRFromScreenshotCommand.NotifyCanExecuteChanged();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
