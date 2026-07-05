using System.Runtime.InteropServices.WindowsRuntime;
using AutoHPMA.Capture.Models;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using WinUIEx;

namespace AutoHPMA.Views.Windows;

internal sealed partial class TaskCoordinateSelectionWindow : WindowEx
{
    private const float MinimumZoomFactor = 0.25f;
    private const float MaximumZoomFactor = 4f;
    private const float ZoomStep = 0.25f;
    private const double MinimumSelectionScreenSize = 4d;
    private const double SelectionStrokeScreenThickness = 2d;
    private const double PointMarkerScreenSize = 12d;
    private const int OemPlusVirtualKey = 0xBB;
    private const int OemMinusVirtualKey = 0xBD;

    private readonly CapturedFrame _frame;
    private readonly TaskCoordinateSelectionMode _mode;
    private readonly TaskCompletionSource<TaskCoordinateSelection?> _selectionCompletion = new();

    private Rect _imageBounds;
    private Point _selectionStart;
    private TaskCoordinateSelection? _selectedCoordinate;
    private bool _isDragging;
    private bool _hasImageBounds;
    private bool _hasCompleted;

    public TaskCoordinateSelectionWindow(
        CapturedFrame frame,
        string sourceName,
        TaskCoordinateSelectionMode mode,
        TaskCoordinateSelection? initialCoordinate = null)
    {
        _frame = frame;
        _mode = mode;
        _selectedCoordinate = initialCoordinate;

        InitializeComponent();

        RootHost.RequestedTheme = App.GetService<IThemeSelectorService>().Theme;
        Title = mode == TaskCoordinateSelectionMode.Region ? "框选任务区域" : "选取任务坐标";
        AppWindow.Title = Title;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        Width = 1280;
        Height = 800;

        SelectionTitleText.Text = Title;
        SourceText.Text = sourceName;
        FrameSizeText.Text = $"{frame.Width} x {frame.Height}";
        StatusText.Text = GetDefaultStatusText();
        RegisterZoomKeyboardAccelerators();

        Closed += OnClosed;
        RootHost.ActualThemeChanged += OnRootHostActualThemeChanged;
    }

    public Task<TaskCoordinateSelection?> SelectAsync()
    {
        Activate();
        return _selectionCompletion.Task;
    }

    private void RootHost_Loaded(object sender, RoutedEventArgs e)
    {
        PresentFrame();
        UpdateImageHostSize();
        UpdateImageBounds();
        UpdateZoomControls();

        if (_selectedCoordinate is not null)
        {
            ShowSelectedCoordinate();
            ConfirmButton.IsEnabled = true;
            StatusText.Text = $"当前坐标：{FormatCoordinate(_selectedCoordinate.Value)}";
        }
    }

    private void PresentFrame()
    {
        var expectedLength = checked(_frame.Width * _frame.Height * 4);
        if (_frame.PixelsBgra8.Length < expectedLength)
        {
            throw new InvalidDataException("捕获帧像素数据不完整。");
        }

        var bitmap = new WriteableBitmap(_frame.Width, _frame.Height);
        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Seek(0, SeekOrigin.Begin);
        stream.Write(_frame.PixelsBgra8, 0, expectedLength);
        bitmap.Invalidate();
        PreviewImage.Source = bitmap;
    }

    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _isDragging = false;
        UpdateImageBounds();
        ShowSelectedCoordinate();
    }

    private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateImageHostSize();

    private void ImageScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e) =>
        UpdateZoomControls();

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) =>
        ChangeZoom(-ZoomStep);

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e) =>
        ResetZoom();

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) =>
        ChangeZoom(ZoomStep);

    private void ImageHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageHost).Position;
        if (!IsInsideImage(point))
        {
            StatusText.Text = "请在游戏截图内部选择。";
            return;
        }

        if (_mode == TaskCoordinateSelectionMode.Point)
        {
            _selectedCoordinate = CreatePointFromDisplayPoint(point);
            ConfirmButton.IsEnabled = true;
            ShowSelectedCoordinate();
            StatusText.Text = $"已选择坐标：{FormatCoordinate(_selectedCoordinate.Value)}";
            e.Handled = true;
            return;
        }

        ClearSelection();
        _selectionStart = ClampToImage(point);
        _isDragging = true;
        ImageHost.CapturePointer(e.Pointer);
        SetSelectionRect(_selectionStart, _selectionStart);
        e.Handled = true;
    }

    private void ImageHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _mode != TaskCoordinateSelectionMode.Region)
        {
            return;
        }

        var point = ClampToImage(e.GetCurrentPoint(ImageHost).Position);
        SetSelectionRect(_selectionStart, point);
        e.Handled = true;
    }

    private void ImageHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _mode != TaskCoordinateSelectionMode.Region)
        {
            return;
        }

        _isDragging = false;
        ImageHost.ReleasePointerCapture(e.Pointer);

        var end = ClampToImage(e.GetCurrentPoint(ImageHost).Position);
        var selectedRect = GetRect(_selectionStart, end);
        var minimumSelectionSize = MinimumSelectionScreenSize / ImageScrollViewer.ZoomFactor;
        if (selectedRect.Width < minimumSelectionSize ||
            selectedRect.Height < minimumSelectionSize)
        {
            StatusText.Text = "区域太小，请重新框选。";
            HideSelection();
            return;
        }

        _selectedCoordinate = CreateRegionFromDisplayRect(selectedRect);
        ConfirmButton.IsEnabled = true;
        StatusText.Text = $"已选择区域：{FormatCoordinate(_selectedCoordinate.Value)}";
    }

    private void ImageHost_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        ClearSelection();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _isDragging = false;
        ClearSelection();
        StatusText.Text = GetDefaultStatusText();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CompleteSelection(null);
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCoordinate is null)
        {
            ConfirmButton.IsEnabled = false;
            StatusText.Text = GetDefaultStatusText();
            return;
        }

        CompleteSelection(_selectedCoordinate);
        Close();
    }

    private void ChangeZoom(float delta)
    {
        var currentZoom = ImageScrollViewer.ZoomFactor;
        var targetZoom = Math.Clamp(
            (float)Math.Round((currentZoom + delta) / ZoomStep) * ZoomStep,
            MinimumZoomFactor,
            MaximumZoomFactor);
        if (Math.Abs(targetZoom - currentZoom) < 0.001f)
        {
            return;
        }

        _ = ImageScrollViewer.ChangeView(null, null, targetZoom, disableAnimation: false);
    }

    private void ResetZoom() =>
        _ = ImageScrollViewer.ChangeView(0, 0, 1f, disableAnimation: false);

    private void UpdateZoomControls()
    {
        var zoomFactor = ImageScrollViewer.ZoomFactor;
        ZoomPercentText.Text = $"{Math.Round(zoomFactor * 100):0}%";
        ZoomOutButton.IsEnabled = zoomFactor > MinimumZoomFactor + 0.001f;
        ZoomInButton.IsEnabled = zoomFactor < MaximumZoomFactor - 0.001f;
        SelectionRectangle.StrokeThickness = SelectionStrokeScreenThickness / zoomFactor;
        PointMarker.StrokeThickness = SelectionStrokeScreenThickness / zoomFactor;
        PointMarker.Width = PointMarkerScreenSize / zoomFactor;
        PointMarker.Height = PointMarkerScreenSize / zoomFactor;
        ShowSelectedCoordinate();
    }

    private void RegisterZoomKeyboardAccelerators()
    {
        AddZoomKeyboardAccelerator(VirtualKey.Add, VirtualKeyModifiers.Control, () => ChangeZoom(ZoomStep));
        AddZoomKeyboardAccelerator(
            (VirtualKey)OemPlusVirtualKey,
            VirtualKeyModifiers.Control,
            () => ChangeZoom(ZoomStep));
        AddZoomKeyboardAccelerator(
            (VirtualKey)OemPlusVirtualKey,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            () => ChangeZoom(ZoomStep));
        AddZoomKeyboardAccelerator(
            VirtualKey.Subtract,
            VirtualKeyModifiers.Control,
            () => ChangeZoom(-ZoomStep));
        AddZoomKeyboardAccelerator(
            (VirtualKey)OemMinusVirtualKey,
            VirtualKeyModifiers.Control,
            () => ChangeZoom(-ZoomStep));
        AddZoomKeyboardAccelerator(VirtualKey.Number0, VirtualKeyModifiers.Control, ResetZoom);
        AddZoomKeyboardAccelerator(VirtualKey.NumberPad0, VirtualKeyModifiers.Control, ResetZoom);
    }

    private void AddZoomKeyboardAccelerator(
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        Action action)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
        };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        RootHost.KeyboardAccelerators.Add(accelerator);
    }

    private void UpdateImageHostSize()
    {
        if (ImageScrollViewer.ActualWidth <= 0 || ImageScrollViewer.ActualHeight <= 0)
        {
            return;
        }

        ImageHost.Width = ImageScrollViewer.ActualWidth;
        ImageHost.Height = ImageScrollViewer.ActualHeight;
    }

    private void UpdateImageBounds()
    {
        var hostWidth = ImageHost.ActualWidth;
        var hostHeight = ImageHost.ActualHeight;
        OverlayCanvas.Width = hostWidth;
        OverlayCanvas.Height = hostHeight;

        if (hostWidth <= 0 || hostHeight <= 0 || _frame.Width <= 0 || _frame.Height <= 0)
        {
            _imageBounds = default;
            _hasImageBounds = false;
            return;
        }

        var scale = Math.Min(hostWidth / _frame.Width, hostHeight / _frame.Height);
        var displayWidth = _frame.Width * scale;
        var displayHeight = _frame.Height * scale;
        _imageBounds = new Rect(
            (hostWidth - displayWidth) / 2,
            (hostHeight - displayHeight) / 2,
            displayWidth,
            displayHeight);
        _hasImageBounds = displayWidth > 0 && displayHeight > 0;
    }

    private void SetSelectionRect(Point start, Point end) =>
        SetSelectionRect(GetRect(start, end));

    private void SetSelectionRect(Rect rect)
    {
        PointMarker.Visibility = Visibility.Collapsed;
        SelectionRectangle.Visibility = Visibility.Visible;
        SelectionRectangle.Width = rect.Width;
        SelectionRectangle.Height = rect.Height;
        Canvas.SetLeft(SelectionRectangle, rect.X);
        Canvas.SetTop(SelectionRectangle, rect.Y);
    }

    private void SetPointMarker(Point point)
    {
        SelectionRectangle.Visibility = Visibility.Collapsed;
        PointMarker.Visibility = Visibility.Visible;
        Canvas.SetLeft(PointMarker, point.X - (PointMarker.Width / 2));
        Canvas.SetTop(PointMarker, point.Y - (PointMarker.Height / 2));
    }

    private void HideSelection()
    {
        SelectionRectangle.Visibility = Visibility.Collapsed;
        PointMarker.Visibility = Visibility.Collapsed;
    }

    private void ClearSelection()
    {
        _selectedCoordinate = null;
        ConfirmButton.IsEnabled = false;
        HideSelection();
    }

    private void ShowSelectedCoordinate()
    {
        if (_selectedCoordinate is null || !_hasImageBounds)
        {
            HideSelection();
            return;
        }

        var coordinate = _selectedCoordinate.Value;
        if (_mode == TaskCoordinateSelectionMode.Point)
        {
            SetPointMarker(new Point(
                _imageBounds.X + (coordinate.X * _imageBounds.Width / _frame.Width),
                _imageBounds.Y + (coordinate.Y * _imageBounds.Height / _frame.Height)));
            return;
        }

        SetSelectionRect(new Rect(
            _imageBounds.X + (coordinate.X * _imageBounds.Width / _frame.Width),
            _imageBounds.Y + (coordinate.Y * _imageBounds.Height / _frame.Height),
            coordinate.Width * _imageBounds.Width / _frame.Width,
            coordinate.Height * _imageBounds.Height / _frame.Height));
    }

    private TaskCoordinateSelection CreatePointFromDisplayPoint(Point displayPoint)
    {
        var xRatio = (displayPoint.X - _imageBounds.X) / _imageBounds.Width;
        var yRatio = (displayPoint.Y - _imageBounds.Y) / _imageBounds.Height;
        return new TaskCoordinateSelection(
            ClampToRange((int)Math.Round(xRatio * _frame.Width), 0, _frame.Width - 1),
            ClampToRange((int)Math.Round(yRatio * _frame.Height), 0, _frame.Height - 1),
            0,
            0);
    }

    private TaskCoordinateSelection CreateRegionFromDisplayRect(Rect displayRect)
    {
        var left = (displayRect.X - _imageBounds.X) / _imageBounds.Width;
        var top = (displayRect.Y - _imageBounds.Y) / _imageBounds.Height;
        var right = (displayRect.X + displayRect.Width - _imageBounds.X) / _imageBounds.Width;
        var bottom = (displayRect.Y + displayRect.Height - _imageBounds.Y) / _imageBounds.Height;

        var x = ClampToRange((int)Math.Round(left * _frame.Width), 0, _frame.Width - 1);
        var y = ClampToRange((int)Math.Round(top * _frame.Height), 0, _frame.Height - 1);
        var regionRight = ClampToRange((int)Math.Round(right * _frame.Width), x + 1, _frame.Width);
        var regionBottom = ClampToRange((int)Math.Round(bottom * _frame.Height), y + 1, _frame.Height);
        return new TaskCoordinateSelection(
            x,
            y,
            regionRight - x,
            regionBottom - y);
    }

    private Point ClampToImage(Point point) =>
        new(
            Clamp(point.X, _imageBounds.X, _imageBounds.X + _imageBounds.Width),
            Clamp(point.Y, _imageBounds.Y, _imageBounds.Y + _imageBounds.Height));

    private bool IsInsideImage(Point point) =>
        _hasImageBounds &&
        point.X >= _imageBounds.X &&
        point.Y >= _imageBounds.Y &&
        point.X <= _imageBounds.X + _imageBounds.Width &&
        point.Y <= _imageBounds.Y + _imageBounds.Height;

    private void CompleteSelection(TaskCoordinateSelection? coordinate)
    {
        if (_hasCompleted)
        {
            return;
        }

        _hasCompleted = true;
        _selectionCompletion.TrySetResult(coordinate);
    }

    private void OnRootHostActualThemeChanged(FrameworkElement sender, object args)
    {
        AppWindow.TitleBar.PreferredTheme = sender.ActualTheme switch
        {
            ElementTheme.Dark => TitleBarTheme.Dark,
            ElementTheme.Light => TitleBarTheme.Light,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        RootHost.ActualThemeChanged -= OnRootHostActualThemeChanged;
        CompleteSelection(null);
    }

    private string GetDefaultStatusText() =>
        _mode == TaskCoordinateSelectionMode.Region
            ? "在截图上拖拽矩形区域；可使用 Ctrl++、Ctrl+- 和 Ctrl+0 调整缩放。"
            : "在截图上单击目标坐标；可使用 Ctrl++、Ctrl+- 和 Ctrl+0 调整缩放。";

    private static string FormatCoordinate(TaskCoordinateSelection coordinate) =>
        coordinate.Width > 0 && coordinate.Height > 0
            ? $"X={coordinate.X}, Y={coordinate.Y}, 宽={coordinate.Width}, 高={coordinate.Height}"
            : $"X={coordinate.X}, Y={coordinate.Y}";

    private static Rect GetRect(Point start, Point end) =>
        new(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private static int ClampToRange(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
