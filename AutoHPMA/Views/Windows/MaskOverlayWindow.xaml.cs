using AutoHPMA.Core.Models;
using AutoHPMA.Helpers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace AutoHPMA.Views.Windows;

public sealed partial class MaskOverlayWindow : WindowEx
{
    private class RegionData
    {
        public required OverlayRegion Region { get; init; }
    }

    private sealed class TemporaryRegion : RegionData
    {
        public DateTime ExpireTime { get; init; }
    }

    private sealed record RegionStyle(Color StrokeColor, double StrokeThickness, byte FillAlpha, bool Dashed = false);

    private static readonly Color RecognitionColor = Color.FromArgb(0xFF, 0x2F, 0xD7, 0xFF);
    private static readonly Color MatchResultColor = Color.FromArgb(0xFF, 0xFF, 0xD7, 0x2F);
    private static readonly RegionStyle StateIndicatorStyle = new(RecognitionColor, 2, 0x18);
    private static readonly RegionStyle TaskStateStyle = new(RecognitionColor, 1.5, 0x14);
    private static readonly RegionStyle TemplateMatchStyle = new(MatchResultColor, 1.5, 0x14);
    private static readonly RegionStyle TemporaryMatchStyle = new(MatchResultColor, 1.5, 0x14, true);
    private static readonly RegionStyle OcrStyle = new(RecognitionColor, 1.5, 0x14);

    private readonly object _gate = new();
    private readonly List<TemporaryRegion> _temporaryRegions = [];
    private readonly List<RegionData> _stateIndicatorRegions = [];
    private readonly List<RegionData> _taskStateRegions = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _cleanupTimer;
    private readonly IDisposable _messageHook;

    private bool _isClosed;

    public MaskOverlayWindow()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        InitializeComponent();

        OverlayWindowHelper.ApplyOverlayChrome(this);
        _messageHook = OverlayWindowHelper.InstallMessageHook(this);

        _cleanupTimer = _dispatcher.CreateTimer();
        _cleanupTimer.Interval = TimeSpan.FromMilliseconds(100);
        _cleanupTimer.Tick += OnCleanupTimerTick;
        _cleanupTimer.Start();

        Closed += OnClosed;
    }

    public bool ShowTextLabels { get; set; } = true;

    public void AddTemporaryRegion(OverlayRegion region, int durationMs = 1000)
    {
        lock (_gate)
        {
            _temporaryRegions.Add(new TemporaryRegion
            {
                Region = region,
                ExpireTime = DateTime.Now.AddMilliseconds(durationMs),
            });
        }

        Redraw();
    }

    public void AddTemporaryRegions(IReadOnlyList<OverlayRegion> regions, int durationMs = 1000)
    {
        var expireTime = DateTime.Now.AddMilliseconds(durationMs);
        lock (_gate)
        {
            foreach (var region in regions)
            {
                _temporaryRegions.Add(new TemporaryRegion
                {
                    Region = region,
                    ExpireTime = expireTime,
                });
            }
        }

        Redraw();
    }

    public void SetStateIndicatorRegions(IReadOnlyList<OverlayRegion> regions)
    {
        lock (_gate)
        {
            _stateIndicatorRegions.Clear();
            _stateIndicatorRegions.AddRange(regions.Select(region => new RegionData { Region = region }));
        }

        Redraw();
    }

    public void ClearStateIndicatorRegions()
    {
        lock (_gate)
        {
            _stateIndicatorRegions.Clear();
        }

        Redraw();
    }

    public void SetTaskStateRegions(IReadOnlyList<OverlayRegion> regions)
    {
        lock (_gate)
        {
            _taskStateRegions.Clear();
            _taskStateRegions.AddRange(regions.Select(region => new RegionData { Region = region }));
        }

        Redraw();
    }

    public void ClearTaskStateRegions()
    {
        lock (_gate)
        {
            _taskStateRegions.Clear();
        }

        Redraw();
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _temporaryRegions.Clear();
            _stateIndicatorRegions.Clear();
            _taskStateRegions.Clear();
        }

        Redraw();
    }

    public void RefreshPosition(IntPtr hWnd, int offsetX = 0, int offsetY = 0)
    {
        if (OverlayWindowHelper.TryFitToWindow(this, hWnd, offsetX, offsetY))
        {
            Redraw();
        }
    }

    public void BringOverlayToFront() => OverlayWindowHelper.BringToTop(this);

    private void Redraw()
    {
        if (_isClosed)
        {
            return;
        }

        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(Redraw);
            return;
        }

        var scale = GetRasterizationScale();
        var windowSize = AppWindow.Size;
        var canvasWidth = Math.Max(1, windowSize.Width / scale);
        var canvasHeight = Math.Max(1, windowSize.Height / scale);

        OverlayCanvas.Width = canvasWidth;
        OverlayCanvas.Height = canvasHeight;
        OverlayCanvas.Children.Clear();

        lock (_gate)
        {
            foreach (var stateRegion in _stateIndicatorRegions)
            {
                DrawRegion(stateRegion.Region, StateIndicatorStyle, scale, canvasWidth, canvasHeight);
            }

            foreach (var taskRegion in _taskStateRegions)
            {
                DrawRegion(taskRegion.Region, TaskStateStyle, scale, canvasWidth, canvasHeight);
            }

            foreach (var tempRegion in _temporaryRegions)
            {
                DrawRegion(tempRegion.Region, TemporaryMatchStyle, scale, canvasWidth, canvasHeight);
            }
        }
    }

    private void DrawRegion(OverlayRegion region, RegionStyle style, double scale, double canvasWidth, double canvasHeight)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return;
        }

        var x = region.X / scale;
        var y = region.Y / scale;
        var width = region.Width / scale;
        var height = region.Height / scale;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        style = ResolveStyle(region, style);
        var color = style.StrokeColor;
        var stroke = new SolidColorBrush(color);
        var shape = new Rectangle
        {
            Stroke = stroke,
            StrokeThickness = style.StrokeThickness,
            Fill = new SolidColorBrush(Color.FromArgb(style.FillAlpha, color.R, color.G, color.B)),
            Width = width,
            Height = height,
        };
        if (style.Dashed)
        {
            shape.StrokeDashArray = new DoubleCollection { 4, 2 };
        }

        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        OverlayCanvas.Children.Add(shape);

        if (!ShowTextLabels)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(region.Name))
        {
            AddNameLabel(region.Name, stroke, x, y, width, canvasWidth, canvasHeight);
        }

        if (!string.IsNullOrWhiteSpace(region.StatusText))
        {
            if (region.StatusKind == OverlayRegionStatusKind.Detail)
            {
                AddDetailStatusText(region.StatusText, stroke, x, y, width, canvasWidth, canvasHeight);
            }
            else
            {
                if (region.Kind == OverlayRegionKind.TemplateMatch)
                {
                    AddMatchScoreText(region.StatusText, stroke, x, y, height, canvasWidth, canvasHeight);
                }
                else
                {
                    AddInlineStatusText(region.StatusText, stroke, x, y, width, height, canvasWidth, canvasHeight);
                }
            }
        }
    }

    private static RegionStyle ResolveStyle(OverlayRegion region, RegionStyle defaultStyle) =>
        region.Kind switch
        {
            OverlayRegionKind.Ocr => OcrStyle,
            OverlayRegionKind.TemplateMatch when ReferenceEquals(defaultStyle, StateIndicatorStyle) => defaultStyle,
            OverlayRegionKind.TemplateMatch => ReferenceEquals(defaultStyle, TemporaryMatchStyle)
                ? TemporaryMatchStyle
                : TemplateMatchStyle,
            _ => defaultStyle,
        };

    private void AddNameLabel(
        string name,
        Brush borderBrush,
        double x,
        double y,
        double width,
        double canvasWidth,
        double canvasHeight)
    {
        var labelMaxWidth = Math.Max(80, Math.Min(Math.Max(width, 120), canvasWidth - x - 8));
        var labelTop = y >= 24 ? y - 24 : Math.Min(y + 4, Math.Max(0, canvasHeight - 24));
        var labelElement = new Border
        {
            MaxWidth = labelMaxWidth,
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

        Canvas.SetLeft(labelElement, Math.Max(0, Math.Min(x, Math.Max(0, canvasWidth - 8))));
        Canvas.SetTop(labelElement, labelTop);
        OverlayCanvas.Children.Add(labelElement);
    }

    private void AddMatchScoreText(
        string statusText,
        Brush foregroundBrush,
        double x,
        double y,
        double height,
        double canvasWidth,
        double canvasHeight)
    {
        var statusElement = new Border
        {
            Padding = new Thickness(3, 0, 3, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0)),
            BorderBrush = foregroundBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock
            {
                Text = statusText,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.Clip,
            },
        };

        statusElement.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = statusElement.DesiredSize.Width;
        var desiredHeight = statusElement.DesiredSize.Height;
        var labelLeft = Math.Max(0, Math.Min(x, Math.Max(0, canvasWidth - desiredWidth - 2)));
        var bottomOutsideTop = y + height + 2;
        var labelTop = bottomOutsideTop <= canvasHeight - desiredHeight - 2
            ? bottomOutsideTop
            : Math.Max(0, y - desiredHeight - 2);

        Canvas.SetLeft(statusElement, labelLeft);
        Canvas.SetTop(statusElement, Math.Max(0, Math.Min(labelTop, Math.Max(0, canvasHeight - desiredHeight - 2))));
        OverlayCanvas.Children.Add(statusElement);
    }

    private void AddInlineStatusText(
        string statusText,
        Brush foregroundBrush,
        double x,
        double y,
        double width,
        double height,
        double canvasWidth,
        double canvasHeight)
    {
        var statusElement = new Border
        {
            Padding = new Thickness(3, 0, 3, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0)),
            BorderBrush = foregroundBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock
            {
                Text = statusText,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.Clip,
            },
        };

        statusElement.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = statusElement.DesiredSize.Width;
        var desiredHeight = statusElement.DesiredSize.Height;
        var labelLeft = x + Math.Max(2, (width - desiredWidth) / 2);
        var labelTop = height >= desiredHeight + 4
            ? y + height - desiredHeight - 2
            : y + 1;

        Canvas.SetLeft(statusElement, Math.Max(0, Math.Min(labelLeft, Math.Max(0, canvasWidth - desiredWidth - 2))));
        Canvas.SetTop(statusElement, Math.Max(0, Math.Min(labelTop, Math.Max(0, canvasHeight - desiredHeight - 2))));
        OverlayCanvas.Children.Add(statusElement);
    }

    private void AddDetailStatusText(
        string statusText,
        Brush borderBrush,
        double x,
        double y,
        double width,
        double canvasWidth,
        double canvasHeight)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return;
        }

        var maximumCanvasLabelWidth = Math.Max(0, canvasWidth - 16);
        if (maximumCanvasLabelWidth <= 0)
        {
            return;
        }

        var labelMaxWidth = Math.Min(Math.Max(width, 180), maximumCanvasLabelWidth);
        var labelLeft = Math.Max(0, Math.Min(x, Math.Max(0, canvasWidth - labelMaxWidth - 8)));
        var labelElement = new Border
        {
            MaxWidth = labelMaxWidth,
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = statusText.Trim(),
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                MaxLines = 3,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.WordEllipsis,
            },
        };

        labelElement.Measure(new global::Windows.Foundation.Size(labelMaxWidth, double.PositiveInfinity));
        var desiredHeight = labelElement.DesiredSize.Height;
        var labelTop = y >= desiredHeight + 4
            ? y - desiredHeight - 4
            : Math.Min(y + 4, Math.Max(0, canvasHeight - desiredHeight - 4));

        Canvas.SetLeft(labelElement, labelLeft);
        Canvas.SetTop(labelElement, labelTop);
        OverlayCanvas.Children.Add(labelElement);
    }

    private double GetRasterizationScale()
    {
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1d;
        return scale > 0 ? scale : 1d;
    }

    private void OnCleanupTimerTick(DispatcherQueueTimer sender, object args)
    {
        var needsRedraw = false;
        lock (_gate)
        {
            var now = DateTime.Now;
            var removed = _temporaryRegions.RemoveAll(region => region.ExpireTime <= now);
            needsRedraw = removed > 0;
        }

        if (needsRedraw)
        {
            Redraw();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        Closed -= OnClosed;
        _messageHook.Dispose();
        _cleanupTimer.Stop();
        _cleanupTimer.Tick -= OnCleanupTimerTick;
    }
}
