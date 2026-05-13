using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using AutoHPMA.Capture;
using AutoHPMA.Capture.Models;
using AutoHPMA.Contracts.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;
using WinUIEx;

namespace AutoHPMA.Views.Windows;

/// <summary>
/// 实时捕获并预览所选窗口画面，可保存当前帧为 PNG/JPG。
/// </summary>
public sealed partial class CapturePreviewWindow : WindowEx
{
    private const int TargetFps = 30;

    private readonly CaptureMethod _method;
    private readonly WindowInfo _target;
    private readonly DispatcherQueue _dispatcher;

    private IScreenCapture? _capture;
    private DispatcherQueueTimer? _timer;
    private WriteableBitmap? _bitmap;
    private CapturedFrame? _lastFrame;
    private int _running;

    public CapturePreviewWindow(CaptureMethod method, WindowInfo target)
    {
        _method = method;
        _target = target;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        InitializeComponent();

        RootHost.RequestedTheme = App.GetService<IThemeSelectorService>().Theme;

        Title = $"截屏预览 — {target.DisplayName}";
        AppWindow.Title = Title;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        ApplyTitleBarTheme(RootHost.ActualTheme);
        Width = 960;
        Height = 640;

        HeaderText.Text = $"截屏方式：{method}";
        StatusText.Text = "正在启动…";

        Closed += OnClosed;
        RootHost.Loaded += OnRootHostLoaded;
        RootHost.ActualThemeChanged += OnRootHostActualThemeChanged;
        Activated += OnFirstActivated;
    }

    private void OnRootHostLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTitleBarTheme(RootHost.ActualTheme);
    }

    private void OnRootHostActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTitleBarTheme(sender.ActualTheme);
    }

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ElementTheme.Dark => TitleBarTheme.Dark,
            ElementTheme.Light => TitleBarTheme.Light,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        StartCapture();
    }

    private void StartCapture()
    {
        try
        {
            _capture = ScreenCaptureFactory.Create(_method);
            _capture.Start(_target.Handle);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"启动失败：{ex.Message}";
            return;
        }

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFps);
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
        _timer.Start();

        StatusText.Text = "捕获中…";
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        try
        {
            var frame = _capture?.TryGetFrame();
            if (frame is null) return;

            _lastFrame = frame;
            RenderFrame(frame);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"捕获错误：{ex.Message}";
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private void RenderFrame(CapturedFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height);
            PreviewImage.Source = _bitmap;
        }

        using var stream = _bitmap.PixelBuffer.AsStream();
        stream.Seek(0, SeekOrigin.Begin);
        stream.Write(frame.PixelsBgra8, 0, frame.PixelsBgra8.Length);
        _bitmap.Invalidate();
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var frame = _lastFrame;
        if (frame is null)
        {
            StatusText.Text = "尚无可保存的帧";
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        picker.FileTypeChoices.Add("PNG", new[] { ".png" });
        picker.FileTypeChoices.Add("JPEG", new[] { ".jpg" });

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            await SaveFrameAsync(frame, file);
            StatusText.Text = $"已保存：{file.Path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"保存失败：{ex.Message}";
        }
    }

    private static async Task SaveFrameAsync(CapturedFrame frame, StorageFile file)
    {
        var encoderId = Path.GetExtension(file.Path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => BitmapEncoder.JpegEncoderId,
            _ => BitmapEncoder.PngEncoderId,
        };

        using var raStream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(encoderId, raStream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)frame.Width,
            (uint)frame.Height,
            96, 96,
            frame.PixelsBgra8);
        await encoder.FlushAsync();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        RootHost.Loaded -= OnRootHostLoaded;
        RootHost.ActualThemeChanged -= OnRootHostActualThemeChanged;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        _capture?.Dispose();
        _capture = null;
        _bitmap = null;
        _lastFrame = null;
    }
}
