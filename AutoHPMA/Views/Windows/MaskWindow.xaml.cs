using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoHPMA.Helpers;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

namespace AutoHPMA.Views.Windows;

public partial class MaskWindow : System.Windows.Window
{
    #region 内部类型

    /// <summary>
    /// 检测框数据结构
    /// </summary>
    private class RectData
    {
        public Rect Rect { get; set; }
        public string? Text { get; set; }
    }

    /// <summary>
    /// 临时检测框（带过期时间）
    /// </summary>
    private class TemporaryRect : RectData
    {
        public DateTime ExpireTime { get; set; }
    }

    #endregion

    #region 字段

    // 临时检测框列表（自动过期，如点击操作）
    private readonly List<TemporaryRect> _temporaryRects = new();

    // 状态标识框（由 FindStateByRules 自动设置，表示当前所处的状态）
    private readonly List<RectData> _stateIndicatorRects = new();

    // 任务状态框（由任务代码手动设置，如选项框、厨具状态等）
    private readonly List<RectData> _taskStateRects = new();

    // 清理定时器
    private DispatcherTimer? _cleanupTimer;

    // 用于线程安全的锁
    private readonly object _lock = new();

    // 复用的 Canvas
    private Canvas? _canvas;

    // 缓存的 DPI 值
    private double _dpiX = 1.0;
    private double _dpiY = 1.0;

    // 是否显示文本标签
    public bool ShowTextLabels { get; set; } = true;

    #endregion

    #region 构造函数

    public MaskWindow()
    {
        InitializeComponent();
    }

    #endregion

    #region 事件处理

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NativeWindowHelper.SetWindowTransparent(this);
        UpdateDpiScale();
        InitializeCanvas();
        StartCleanupTimer();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StopCleanupTimer();
        base.OnClosing(e);
    }

    #endregion

    #region 初始化

    private void InitializeCanvas()
    {
        _canvas = new Canvas { Background = Brushes.Transparent };
        RootGrid.Children.Add(_canvas);
    }

    private void UpdateDpiScale()
    {
        var (dpiX, dpiY) = NativeWindowHelper.GetDpiScale(this);
        _dpiX = dpiX;
        _dpiY = dpiY;
    }

    #endregion

    #region 定时器管理

    private void StartCleanupTimer()
    {
        _cleanupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _cleanupTimer.Tick += (_, _) => CleanupExpiredRects();
        _cleanupTimer.Start();
    }

    private void StopCleanupTimer()
    {
        _cleanupTimer?.Stop();
        _cleanupTimer = null;
    }

    private void CleanupExpiredRects()
    {
        bool needsRedraw;
        lock (_lock)
        {
            var now = DateTime.Now;
            int removed = _temporaryRects.RemoveAll(r => r.ExpireTime <= now);
            needsRedraw = removed > 0;
        }

        if (needsRedraw)
        {
            Redraw();
        }
    }

    #endregion

    #region 临时检测框方法

    /// <summary>
    /// 添加单个临时检测框
    /// </summary>
    public void AddTemporaryRect(Rect rect, string? text = null, int durationMs = 500)
    {
        lock (_lock)
        {
            _temporaryRects.Add(new TemporaryRect
            {
                Rect = rect,
                Text = text,
                ExpireTime = DateTime.Now.AddMilliseconds(durationMs)
            });
        }
        Redraw();
    }

    /// <summary>
    /// 添加多个临时检测框
    /// </summary>
    public void AddTemporaryRects(List<Rect> rects, Dictionary<Rect, string>? textContents = null, int durationMs = 500)
    {
        var expireTime = DateTime.Now.AddMilliseconds(durationMs);
        lock (_lock)
        {
            foreach (var rect in rects)
            {
                string? text = null;
                textContents?.TryGetValue(rect, out text);
                _temporaryRects.Add(new TemporaryRect
                {
                    Rect = rect,
                    Text = text,
                    ExpireTime = expireTime
                });
            }
        }
        Redraw();
    }

    #endregion

    #region 状态标识框方法

    /// <summary>
    /// 设置状态标识框（由 FindStateByRules 自动调用）
    /// </summary>
    public void SetStateIndicatorRects(List<Rect> rects)
    {
        lock (_lock)
        {
            _stateIndicatorRects.Clear();
            foreach (var rect in rects)
            {
                _stateIndicatorRects.Add(new RectData { Rect = rect });
            }
        }
        Redraw();
    }

    /// <summary>
    /// 清除状态标识框
    /// </summary>
    public void ClearStateIndicatorRects()
    {
        lock (_lock)
        {
            _stateIndicatorRects.Clear();
        }
        Redraw();
    }

    #endregion

    #region 任务状态框方法

    /// <summary>
    /// 设置任务状态框（覆盖式更新）
    /// </summary>
    public void SetTaskStateRects(List<Rect> rects, Dictionary<Rect, string>? textContents = null)
    {
        lock (_lock)
        {
            _taskStateRects.Clear();
            foreach (var rect in rects)
            {
                string? text = null;
                textContents?.TryGetValue(rect, out text);
                _taskStateRects.Add(new RectData
                {
                    Rect = rect,
                    Text = text
                });
            }
        }
        Redraw();
    }

    /// <summary>
    /// 清除任务状态框
    /// </summary>
    public void ClearTaskStateRects()
    {
        lock (_lock)
        {
            _taskStateRects.Clear();
        }
        Redraw();
    }

    #endregion

    #region 兼容旧 API

    public void SetStateRects(List<Rect> rects, Dictionary<Rect, string>? textContents = null)
        => SetTaskStateRects(rects, textContents);

    public void ClearStateRects() => ClearTaskStateRects();

    #endregion

    #region 清除所有

    /// <summary>
    /// 清除所有检测框
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _temporaryRects.Clear();
            _stateIndicatorRects.Clear();
            _taskStateRects.Clear();
        }
        Redraw();
    }

    #endregion

    #region 绘制逻辑

    private void Redraw()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Redraw);
            return;
        }

        if (_canvas == null) return;

        _canvas.Children.Clear();

        lock (_lock)
        {
            // 绘制状态标识框（绿色）
            foreach (var stateRect in _stateIndicatorRects)
            {
                DrawRect(stateRect.Rect, stateRect.Text, Brushes.LimeGreen);
            }

            // 绘制任务状态框（蓝色）
            foreach (var taskRect in _taskStateRects)
            {
                DrawRect(taskRect.Rect, taskRect.Text, Brushes.DeepSkyBlue);
            }

            // 绘制临时检测框（红色）
            foreach (var tempRect in _temporaryRects)
            {
                DrawRect(tempRect.Rect, tempRect.Text, Brushes.Red);
            }
        }
    }

    private void DrawRect(Rect rect, string? text, Brush stroke)
    {
        if (_canvas == null) return;

        var shape = new Rectangle
        {
            Stroke = stroke,
            StrokeThickness = 2,
            Width = rect.Width * _dpiX,
            Height = rect.Height * _dpiY
        };
        Canvas.SetLeft(shape, rect.X * _dpiX);
        Canvas.SetTop(shape, rect.Y * _dpiY);
        _canvas.Children.Add(shape);

        if (ShowTextLabels && !string.IsNullOrEmpty(text))
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(textBlock, rect.X * _dpiX + 5);
            Canvas.SetTop(textBlock, rect.Y * _dpiY + 5);
            _canvas.Children.Add(textBlock);
        }
    }

    #endregion

    #region 窗口位置同步

    /// <summary>
    /// 同步遮罩窗口位置与大小
    /// </summary>
    public void RefreshPosition(IntPtr targetHwnd, int offsetX = 0, int offsetY = 0)
    {
        if (NativeWindowHelper.GetWindowPosition(targetHwnd, this, out double left, out double top, out double width, out double height))
        {
            Left = left + offsetX;
            Top = top + offsetY;
            Width = width;
            Height = height;
            
            // 更新 DPI 缓存
            UpdateDpiScale();
        }
    }

    #endregion
}
