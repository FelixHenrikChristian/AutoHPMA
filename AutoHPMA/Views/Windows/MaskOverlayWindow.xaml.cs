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

    public void AddTemporaryRegion(OverlayRegion region, int durationMs = 500)
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

    public void AddTemporaryRegions(IReadOnlyList<OverlayRegion> regions, int durationMs = 500)
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
                DrawRegion(stateRegion.Region, Colors.LimeGreen, scale, canvasWidth, canvasHeight);
            }

            foreach (var taskRegion in _taskStateRegions)
            {
                DrawRegion(taskRegion.Region, Colors.DeepSkyBlue, scale, canvasWidth, canvasHeight);
            }

            foreach (var tempRegion in _temporaryRegions)
            {
                DrawRegion(tempRegion.Region, Colors.MediumPurple, scale, canvasWidth, canvasHeight);
            }
        }
    }

    private void DrawRegion(OverlayRegion region, Color color, double scale, double canvasWidth, double canvasHeight)
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

        var stroke = new SolidColorBrush(color);
        var shape = new Rectangle
        {
            Stroke = stroke,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x18, color.R, color.G, color.B)),
            Width = width,
            Height = height,
        };

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
            AddStatusText(region.StatusText, x, y, width, height);
        }
    }

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

    private void AddStatusText(string statusText, double x, double y, double width, double height)
    {
        var maxWidth = Math.Max(24, width - 8);
        var statusElement = new Border
        {
            MaxWidth = maxWidth,
            Padding = new Thickness(5, 1, 5, 1),
            Background = new SolidColorBrush(Color.FromArgb(0xB8, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = statusText,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

        statusElement.Measure(new global::Windows.Foundation.Size(maxWidth, double.PositiveInfinity));
        var desiredWidth = Math.Min(statusElement.DesiredSize.Width, maxWidth);
        var desiredHeight = statusElement.DesiredSize.Height;

        Canvas.SetLeft(statusElement, x + Math.Max(4, (width - desiredWidth) / 2));
        Canvas.SetTop(statusElement, y + Math.Max(4, height - desiredHeight - 6));
        OverlayCanvas.Children.Add(statusElement);
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
