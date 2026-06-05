using AutoHPMA.Helpers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Rect = OpenCvSharp.Rect;

namespace AutoHPMA.Views.Windows;

public sealed partial class MaskOverlayWindow : WindowEx
{
    private class RectData
    {
        public Rect Rect { get; init; }

        public string? Text { get; init; }
    }

    private sealed class TemporaryRect : RectData
    {
        public DateTime ExpireTime { get; init; }
    }

    private readonly object _gate = new();
    private readonly List<TemporaryRect> _temporaryRects = [];
    private readonly List<RectData> _stateIndicatorRects = [];
    private readonly List<RectData> _taskStateRects = [];
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

    public void AddTemporaryRect(Rect rect, string? text = null, int durationMs = 500)
    {
        lock (_gate)
        {
            _temporaryRects.Add(new TemporaryRect
            {
                Rect = rect,
                Text = text,
                ExpireTime = DateTime.Now.AddMilliseconds(durationMs),
            });
        }

        Redraw();
    }

    public void AddTemporaryRects(IReadOnlyList<Rect> rects, IReadOnlyDictionary<Rect, string>? textContents = null, int durationMs = 500)
    {
        var expireTime = DateTime.Now.AddMilliseconds(durationMs);
        lock (_gate)
        {
            foreach (var rect in rects)
            {
                string? text = null;
                textContents?.TryGetValue(rect, out text);
                _temporaryRects.Add(new TemporaryRect
                {
                    Rect = rect,
                    Text = text,
                    ExpireTime = expireTime,
                });
            }
        }

        Redraw();
    }

    public void SetStateIndicatorRects(IReadOnlyList<Rect> rects)
    {
        lock (_gate)
        {
            _stateIndicatorRects.Clear();
            _stateIndicatorRects.AddRange(rects.Select(rect => new RectData { Rect = rect }));
        }

        Redraw();
    }

    public void ClearStateIndicatorRects()
    {
        lock (_gate)
        {
            _stateIndicatorRects.Clear();
        }

        Redraw();
    }

    public void SetTaskStateRects(IReadOnlyList<Rect> rects, IReadOnlyDictionary<Rect, string>? textContents = null)
    {
        lock (_gate)
        {
            _taskStateRects.Clear();
            foreach (var rect in rects)
            {
                string? text = null;
                textContents?.TryGetValue(rect, out text);
                _taskStateRects.Add(new RectData
                {
                    Rect = rect,
                    Text = text,
                });
            }
        }

        Redraw();
    }

    public void ClearTaskStateRects()
    {
        lock (_gate)
        {
            _taskStateRects.Clear();
        }

        Redraw();
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _temporaryRects.Clear();
            _stateIndicatorRects.Clear();
            _taskStateRects.Clear();
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

        OverlayCanvas.Children.Clear();

        lock (_gate)
        {
            foreach (var stateRect in _stateIndicatorRects)
            {
                DrawRect(stateRect.Rect, stateRect.Text, Colors.LimeGreen);
            }

            foreach (var taskRect in _taskStateRects)
            {
                DrawRect(taskRect.Rect, taskRect.Text, Colors.DeepSkyBlue);
            }

            foreach (var tempRect in _temporaryRects)
            {
                DrawRect(tempRect.Rect, tempRect.Text, Colors.Red);
            }
        }
    }

    private void DrawRect(Rect rect, string? text, Color color)
    {
        var stroke = new SolidColorBrush(color);
        var shape = new Rectangle
        {
            Stroke = stroke,
            StrokeThickness = 2,
            Width = rect.Width,
            Height = rect.Height,
        };

        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        OverlayCanvas.Children.Add(shape);

        if (!ShowTextLabels || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        };
        Canvas.SetLeft(textBlock, rect.X + 5);
        Canvas.SetTop(textBlock, rect.Y + 5);
        OverlayCanvas.Children.Add(textBlock);
    }

    private void OnCleanupTimerTick(DispatcherQueueTimer sender, object args)
    {
        var needsRedraw = false;
        lock (_gate)
        {
            var now = DateTime.Now;
            var removed = _temporaryRects.RemoveAll(rect => rect.ExpireTime <= now);
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
