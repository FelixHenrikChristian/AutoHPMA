using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Models;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace AutoHPMA.Views.Windows;

public partial class ScreenshotPreviewWindow : FluentWindow
{
    private readonly CaptureMethod _captureMethod;
    private readonly IntPtr _targetWindow;
    private CancellationTokenSource? _cancellationTokenSource;
    private IScreenCapture? _capture;
    private Mat? _currentFrame;
    private readonly ILogger<ScreenshotPreviewWindow> _logger;

    public ScreenshotPreviewWindow(CaptureMethod captureMethod, IntPtr targetWindow)
    {
        InitializeComponent();
        _captureMethod = captureMethod;
        _targetWindow = targetWindow;
        _logger = App.GetLogger<ScreenshotPreviewWindow>();

        Title = $"截屏实时预览 - {captureMethod}";
        StatusText.Text = $"截屏方式: {captureMethod}";

        // 启用窗口拖拽
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "窗口拖拽失败，可能由窗口状态变化导致。");
            }
        };

        // 窗口加载后自动开始捕获
        Loaded += async (_, _) => await StartCaptureAsync();
    }

    private async Task StartCaptureAsync()
    {
        try
        {
            StatusText.Text = "正在启动截屏...";

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // 根据截屏方式创建对应的捕获器
            _capture = _captureMethod switch
            {
                CaptureMethod.WGC => new WindowsGraphicsCapture(),
                CaptureMethod.BitBlt => new BitBltCapture(),
                CaptureMethod.PrintWindow => new PrintWindowCapture(),
                _ => throw new NotSupportedException($"Unsupported capture method: {_captureMethod}")
            };

            _capture.Start(_targetWindow);

            // WGC需要等待初始化
            if (_captureMethod == CaptureMethod.WGC)
                await Task.Delay(100, token);

            StatusText.Text = "截屏中...";

            // 开始截屏循环
            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var frame = _capture.Capture();

                        if (frame != null && !frame.Empty())
                        {
                            // 更新当前帧
                            _currentFrame?.Dispose();
                            _currentFrame = frame.Clone();

                            // 在UI线程更新图像
                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var bitmapSource = frame.ToBitmapSource();
                                    bitmapSource.Freeze();
                                    PreviewImage.Source = bitmapSource;
                                }
                                catch (Exception ex)
                                {
                                    StatusText.Text = $"显示错误: {ex.Message}";
                                }
                            });

                            frame.Dispose();
                        }

                        // 控制帧率，大约30fps
                        await Task.Delay(33, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => StatusText.Text = $"截屏错误: {ex.Message}");
                        await Task.Delay(1000, token);
                    }
                }
            }, token);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"启动失败: {ex.Message}";
            StopCapture();
        }
    }

    private void StopCapture()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _capture?.Dispose();
        _capture = null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFrame == null || _currentFrame.Empty())
        {
            _ = new MessageBox { Title = "错误", Content = "没有可保存的截图" }.ShowDialogAsync();
            return;
        }

        try
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "PNG图片|*.png|JPEG图片|*.jpg|所有文件|*.*",
                DefaultExt = "png",
                FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                using var bitmap = _currentFrame.ToBitmap();
                var format = Path.GetExtension(saveDialog.FileName).ToLower() switch
                {
                    ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                    _ => ImageFormat.Png
                };

                bitmap.Save(saveDialog.FileName, format);
                _ = new MessageBox { Title = "成功", Content = $"截图已保存到: {saveDialog.FileName}" }.ShowDialogAsync();
            }
        }
        catch (Exception ex)
        {
            _ = new MessageBox { Title = "保存失败", Content = ex.Message }.ShowDialogAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "标题栏拖拽失败，可能由窗口状态变化导致。");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopCapture();
        _currentFrame?.Dispose();
        base.OnClosed(e);
    }
}