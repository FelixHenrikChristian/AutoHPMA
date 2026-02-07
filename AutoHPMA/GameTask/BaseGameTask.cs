using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Helpers.ImageHelper;
using AutoHPMA.Services;
using AutoHPMA.Views.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace AutoHPMA.GameTask
{
    #region 匹配相关类

    /// <summary>
    /// 模板匹配选项
    /// </summary>
    public class MatchOptions
    {
        /// <summary>自定义遮罩（可选）</summary>
        public Mat? Mask { get; set; }

        /// <summary>是否使用模板的 Alpha 通道生成遮罩</summary>
        public bool UseAlphaMask { get; set; } = false;

        /// <summary>是否查找多个匹配</summary>
        public bool FindMultiple { get; set; } = false;

        /// <summary>匹配阈值（默认 0.9）</summary>
        public double Threshold { get; set; } = 0.9;

        /// <summary>匹配模式</summary>
        public TemplateMatchModes MatchMode { get; set; } = TemplateMatchModes.CCoeffNormed;
    }

    /// <summary>
    /// 模板匹配结果
    /// </summary>
    public class MatchResult
    {
        /// <summary>是否匹配成功</summary>
        public bool Success { get; set; }

        /// <summary>匹配位置（单个匹配时的左上角坐标，未缩放）</summary>
        public Point Location { get; set; }

        /// <summary>匹配区域列表（已缩放，用于显示）</summary>
        public List<Rect> Rects { get; set; } = new();

        /// <summary>匹配区域列表（未缩放，用于多重匹配点击）</summary>
        public List<Rect> RectsUnscaled { get; set; } = new();

        /// <summary>模板尺寸（未缩放）</summary>
        public Size TemplateSize { get; set; }

        /// <summary>静态失败结果</summary>
        public static MatchResult Failed => new() { Success = false };
    }

    #endregion

    #region 状态规则

    /// <summary>
    /// 状态检测规则
    /// </summary>
    /// <typeparam name="TState">状态枚举类型</typeparam>
    public record StateRule<TState>(
        Mat[] Templates,
        TState State,
        string DisplayName,
        double Threshold = 0.9
    );

    #endregion

    public abstract class BaseGameTask : IGameTask
    {
        protected static LogWindow _logWindow => AppContextService.Instance.LogWindow;
        protected static MaskWindow _maskWindow => AppContextService.Instance.MaskWindow;
        protected static WindowsGraphicsCapture _capture => AppContextService.Instance.Capture;

        protected readonly ILogger _logger;
        protected nint _displayHwnd, _gameHwnd;
        protected int offsetX, offsetY;
        protected double scale;
        protected CancellationTokenSource _cts;
        protected bool _waited = false;
        protected Dictionary<string, Mat> _images = new();

        // 状态监测任务
        private Task? _stateMonitorTask;
        private volatile bool _isStateMonitoring = false;
        protected int _stateMonitorIntervalMs = 200;

        // 操作级别的取消令牌，用于在状态变化时立即取消当前操作
        private CancellationTokenSource? _operationCts;
        private readonly object _operationCtsLock = new object();

        public event EventHandler? TaskCompleted;

        public BaseGameTask(ILogger logger, nint displayHwnd, nint gameHwnd)
        {
            _logger = logger;
            _displayHwnd = displayHwnd;
            _gameHwnd = gameHwnd;
            _cts = new CancellationTokenSource();
            InitializeOperationCts();
            CalOffset();
        }

        #region 操作取消管理

        /// <summary>
        /// 初始化操作级别的取消令牌
        /// </summary>
        private void InitializeOperationCts()
        {
            lock (_operationCtsLock)
            {
                _operationCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            }
        }

        /// <summary>
        /// 取消当前操作并创建新的操作 CTS
        /// 在状态变化时调用，确保正在进行的操作立即停止
        /// </summary>
        protected void CancelCurrentOperation()
        {
            lock (_operationCtsLock)
            {
                try
                {
                    _operationCts?.Cancel();
                    _operationCts?.Dispose();
                }
                catch { /* 忽略异常 */ }

                // 创建链接到主 CTS 的新操作 CTS
                _operationCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            }
        }

        /// <summary>
        /// 获取当前操作的取消令牌
        /// </summary>
        protected CancellationToken OperationToken
        {
            get
            {
                lock (_operationCtsLock)
                {
                    return _operationCts?.Token ?? _cts.Token;
                }
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 缩放矩形区域
        /// </summary>
        protected Rect ScaleRect(Rect rect, double scale)
        {
            return new Rect(
                (int)(rect.X * scale),
                (int)(rect.Y * scale),
                (int)(rect.Width * scale),
                (int)(rect.Height * scale)
            );
        }

        /// <summary>
        /// 计算游戏窗口偏移和缩放比例
        /// </summary>
        protected void CalOffset()
        {
            int left, top, width, height;
            int leftMumu, topMumu;
            WindowInteractionHelper.GetWindowPositionAndSize(_displayHwnd, out leftMumu, out topMumu, out width, out height);
            WindowInteractionHelper.GetWindowPositionAndSize(_gameHwnd, out left, out top, out width, out height);
            offsetX = left - leftMumu;
            offsetY = top - topMumu;
            scale = width / 1280.0;
        }

        #endregion

        #region 资源加载

        /// <summary>
        /// 从指定目录加载所有 PNG 图片到 _images 字典
        /// </summary>
        /// <param name="directory">目录路径</param>
        /// <param name="mode">图像读取模式（默认 Color）</param>
        protected void LoadImagesFromDirectory(string directory, ImreadModes mode = ImreadModes.Color)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("目录不存在：{Directory}", directory);
                return;
            }

            foreach (var file in Directory.GetFiles(directory, "*.png"))
            {
                var key = Path.GetFileNameWithoutExtension(file);
                _images[key] = Cv2.ImRead(file, mode);
            }
        }

        /// <summary>
        /// 从指定目录递归加载所有 PNG 图片到 _images 字典，使用相对路径作为键
        /// </summary>
        /// <param name="directory">目录路径</param>
        /// <param name="mode">图像读取模式（默认 Color）</param>
        protected void LoadImagesFromDirectoryRecursive(string directory, ImreadModes mode = ImreadModes.Color)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("目录不存在：{Directory}", directory);
                return;
            }

            foreach (var file in Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories))
            {
                // 使用相对于根目录的路径作为键，不含扩展名
                var relativePath = Path.GetRelativePath(directory, file);
                var key = Path.ChangeExtension(relativePath, null).Replace("\\", "/");
                _images[key] = Cv2.ImRead(file, mode);
            }
        }

        /// <summary>
        /// 获取已加载的图片
        /// </summary>
        protected Mat GetImage(string name) => _images.TryGetValue(name, out var mat) ? mat : throw new KeyNotFoundException($"图片未找到：{name}");

        #endregion

        #region 截屏与预处理

        /// <summary>
        /// 捕获屏幕并进行预处理（缩放和颜色转换）
        /// </summary>
        /// <returns>预处理后的 Mat 对象（BGR 格式，已缩放至 1280 基准）</returns>
        protected Mat CaptureAndPreprocess()
        {
            _cts.Token.ThrowIfCancellationRequested();
            var captureMat = _capture.Capture();
            Cv2.Resize(captureMat, captureMat, new Size(captureMat.Width / scale, captureMat.Height / scale));
            Cv2.CvtColor(captureMat, captureMat, ColorConversionCodes.BGRA2BGR);
            return captureMat;
        }

        #endregion

        #region 模板匹配（Find）

        /// <summary>
        /// 在屏幕上查找模板（自动截屏）
        /// </summary>
        /// <param name="template">模板图像（支持 BGR 或 BGRA）</param>
        /// <param name="options">匹配选项（可选）</param>
        /// <returns>匹配结果</returns>
        protected MatchResult Find(Mat template, MatchOptions? options = null)
        {
            var captureMat = CaptureAndPreprocess();
            return FindInSource(captureMat, template, options);
        }

        /// <summary>
        /// 在给定的图像中查找模板（不重新截屏）
        /// </summary>
        /// <param name="source">源图像</param>
        /// <param name="template">模板图像（支持 BGR 或 BGRA）</param>
        /// <param name="options">匹配选项（可选）</param>
        /// <returns>匹配结果</returns>
        protected MatchResult FindInSource(Mat source, Mat template, MatchOptions? options = null)
        {
            options ??= new MatchOptions();

            // 处理模板和遮罩
            Mat templateBGR;
            Mat? mask = options.Mask;

            if (template.Channels() == 4)
            {
                // 4通道模板（带透明通道）
                if (options.UseAlphaMask && mask == null)
                {
                    mask = MatchTemplateHelper.GenerateMask(template);
                }
                templateBGR = new Mat();
                Cv2.CvtColor(template, templateBGR, ColorConversionCodes.BGRA2BGR);
            }
            else
            {
                templateBGR = template;
            }

            // 执行匹配
            if (options.FindMultiple)
            {
                // 多重匹配
                var rects = MatchTemplateHelper.MatchOnePicForOnePic(
                    source, templateBGR, options.MatchMode, mask, options.Threshold);

                if (rects.Count == 0) return MatchResult.Failed;

                return new MatchResult
                {
                    Success = true,
                    Location = new Point(rects[0].X, rects[0].Y),
                    Rects = rects.Select(r => ScaleRect(r, scale)).ToList(),
                    RectsUnscaled = rects,
                    TemplateSize = new Size(template.Width, template.Height)
                };
            }
            else
            {
                // 单个匹配
                var matchPoint = MatchTemplateHelper.MatchTemplate(
                    source, templateBGR, options.MatchMode, mask, options.Threshold);

                if (matchPoint == default) return MatchResult.Failed;

                var unscaledRect = new Rect(matchPoint.X, matchPoint.Y, template.Width, template.Height);

                return new MatchResult
                {
                    Success = true,
                    Location = matchPoint,
                    Rects = new List<Rect> { ScaleRect(unscaledRect, scale) },
                    RectsUnscaled = new List<Rect> { unscaledRect },
                    TemplateSize = new Size(template.Width, template.Height)
                };
            }
        }

        #endregion

        #region 点击操作（Click）

        /// <summary>
        /// 异步点击指定位置（未缩放坐标）
        /// </summary>
        /// <param name="location">未缩放的坐标位置</param>
        protected async Task ClickAsync(Point location)
        {
            var token = OperationToken;
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendMouseClickAsync(
                _gameHwnd,
                (uint)(location.X * scale - offsetX),
                (uint)(location.Y * scale - offsetY),
                token
            );
        }

        /// <summary>
        /// 异步根据匹配结果点击模板中心位置，自动显示检测框
        /// </summary>
        /// <param name="result">匹配结果</param>
        protected async Task ClickMatchCenterAsync(MatchResult result)
        {
            if (!result.Success) return;
            ShowMatchRects(result);
            var centerX = result.Location.X + result.TemplateSize.Width / 2.0;
            var centerY = result.Location.Y + result.TemplateSize.Height / 2.0;
            await ClickAsync(new Point((int)centerX, (int)centerY));
        }

        /// <summary>
        /// 尝试查找并点击模板，成功时自动显示检测框
        /// </summary>
        /// <param name="template">模板图像</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>是否成功找到并点击</returns>
        protected async Task<bool> TryClickTemplateAsync(Mat template, double threshold = 0.9)
        {
            var result = Find(template, new MatchOptions { Threshold = threshold });
            if (result.Success)
            {
                await ClickMatchCenterAsync(result);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 尝试查找并点击带 Alpha 通道的模板，成功时自动显示检测框
        /// </summary>
        /// <param name="template">带 Alpha 通道的模板图像</param>
        /// <param name="threshold">匹配阈值</param>
        /// <returns>是否成功找到并点击</returns>
        protected async Task<bool> TryClickTemplateWithAlphaAsync(Mat template, double threshold = 0.9)
        {
            var result = Find(template, new MatchOptions { UseAlphaMask = true, Threshold = threshold });
            if (result.Success)
            {
                await ClickMatchCenterAsync(result);
                return true;
            }
            return false;
        }

        #endregion

        #region 拖拽操作

        /// <summary>
        /// 异步拖拽移动（未缩放坐标），不阻塞线程
        /// </summary>
        protected async Task<bool> DragMoveAsync(Point start, Point end, int duration = 500)
        {
            var token = OperationToken;
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendMouseDragWithNoiseAsync(
                _gameHwnd,
                (uint)(start.X * scale - offsetX),
                (uint)(start.Y * scale - offsetY),
                (uint)(end.X * scale - offsetX),
                (uint)(end.Y * scale - offsetY),
                duration,
                token
            );
            return true;
        }

        #endregion

        #region 键盘操作

        /// <summary>
        /// 异步发送空格键
        /// </summary>
        protected async Task SendSpaceAsync()
        {
            var token = OperationToken;
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendSpaceAsync(_gameHwnd, token);
        }

        /// <summary>
        /// 异步发送回车键
        /// </summary>
        protected async Task SendEnterAsync()
        {
            var token = OperationToken;
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendEnterAsync(_gameHwnd, token);
        }

        /// <summary>
        /// 异步发送 ESC 键
        /// </summary>
        protected async Task SendESCAsync()
        {
            var token = OperationToken;
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendESCAsync(_gameHwnd, token);
        }

        #endregion

        #region 显示检测框

        /// <summary>
        /// 显示匹配结果的临时检测框
        /// </summary>
        /// <param name="result">匹配结果</param>
        /// <param name="durationMs">显示时长（毫秒），默认 1500ms</param>
        protected void ShowMatchRects(MatchResult result, int durationMs = 500)
        {
            if (result.Success)
            {
                _maskWindow?.AddTemporaryRects(result.Rects, durationMs: durationMs);
            }
        }

        /// <summary>
        /// 设置任务状态检测框（用于任务特定的状态框，如选项框、厨具状态等）
        /// </summary>
        /// <param name="rects">检测框区域列表</param>
        /// <param name="textContents">可选的文字内容字典</param>
        protected void SetStateRects(List<Rect> rects, Dictionary<Rect, string>? textContents = null)
        {
            _maskWindow?.SetTaskStateRects(rects, textContents);
        }

        /// <summary>
        /// 清除任务状态检测框
        /// </summary>
        protected void ClearStateRects()
        {
            _maskWindow?.ClearTaskStateRects();
        }

        #endregion

        #region 状态检测

        /// <summary>
        /// 通用状态检测方法，使用规则数组匹配状态
        /// </summary>
        /// <typeparam name="TState">状态枚举类型</typeparam>
        /// <param name="rules">状态规则数组</param>
        /// <param name="defaultState">默认状态</param>
        /// <param name="defaultDisplayName">默认显示名称</param>
        /// <returns>匹配到的状态</returns>
        protected TState FindStateByRules<TState>(
            StateRule<TState>[] rules,
            TState defaultState,
            string defaultDisplayName)
        {
            foreach (var (templates, state, displayName, threshold) in rules)
            {
                foreach (var template in templates)
                {
                    var result = Find(template, new MatchOptions { Threshold = threshold });
                    if (result.Success)
                    {
                        // 显示状态标识检测框（绿色，持续显示直到状态改变）
                        _maskWindow?.SetStateIndicatorRects(result.Rects);
                        _logWindow?.SetGameState(displayName);
                        return state;
                    }
                }
            }
            // 未匹配到任何状态时清除状态标识框
            _maskWindow?.ClearStateIndicatorRects();
            _logWindow?.SetGameState(defaultDisplayName);
            return defaultState;
        }

        #endregion

        #region 状态监测

        /// <summary>
        /// 启动状态监测后台任务
        /// </summary>
        /// <typeparam name="TState">状态枚举类型</typeparam>
        /// <param name="rules">状态检测规则数组</param>
        /// <param name="onStateDetected">状态更新回调</param>
        /// <param name="defaultState">默认状态</param>
        /// <param name="defaultDisplayName">默认显示名称</param>
        protected void StartStateMonitor<TState>(
            StateRule<TState>[] rules,
            Action<TState> onStateDetected,
            TState defaultState,
            string defaultDisplayName) where TState : struct
        {
            if (_isStateMonitoring) return;
            _isStateMonitoring = true;
            _stateMonitorIntervalMs = AppContextService.Instance.StateMonitorInterval;
            _stateMonitorTask = Task.Run(async () =>
            {
                while (_isStateMonitoring && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var newState = FindStateByRules(rules, defaultState, defaultDisplayName);
                        onStateDetected(newState);
                        await Task.Delay(_stateMonitorIntervalMs, _cts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("状态监测异常：{Message}", ex.Message);
                    }
                }
            });
            _logger.LogDebug("状态监测已启动，间隔：{Interval}ms", _stateMonitorIntervalMs);
        }

        /// <summary>
        /// 停止状态监测后台任务
        /// </summary>
        protected void StopStateMonitor()
        {
            if (!_isStateMonitoring) return;
            _isStateMonitoring = false;
            
            // 等待后台任务完成，确保不会在设置"空闲"状态后再更新状态显示
            try
            {
                _stateMonitorTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException) { /* 忽略任务取消异常 */ }
            
            _logger.LogDebug("状态监测已停止");
        }

        #endregion

        #region 任务控制

        public virtual void Stop()
        {
            _cts.Cancel();
            TaskCompleted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 运行任务的模板方法，统一处理任务生命周期
        /// </summary>
        /// <param name="taskName">任务名称（用于日志显示）</param>
        protected async Task RunTaskAsync(string taskName)
        {
            _logWindow?.SetGameState(taskName);
            _logger.LogInformation("[Aquamarine]---{TaskName}任务已启动---[/Aquamarine]", taskName);

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        GC.Collect();
                        await ExecuteLoopAsync();
                    }
                    catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
                    {
                        // 操作级别取消（状态变化导致），继续下一次循环
                        // 只有当主 CTS 未取消时才继续循环
                        continue;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 任务级别取消（Stop() 调用导致），正常流程
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发生异常：{Message}", ex.Message);
            }
            finally
            {
                StopStateMonitor();
                _logger.LogInformation("[Aquamarine]---{TaskName}任务已终止---[/Aquamarine]", taskName);
                _maskWindow?.ClearAll();
                _logWindow?.SetGameState("空闲");
                _waited = false;
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
        }

        /// <summary>
        /// 任务循环执行逻辑，子类必须实现
        /// </summary>
        protected abstract Task ExecuteLoopAsync();

        // 子类必须实现
        public abstract void Start();
        public abstract bool SetParameters(Dictionary<string, object> parameters);

        /// <summary>
        /// 尝试从参数字典中获取指定类型的值
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="parameters">参数字典</param>
        /// <param name="key">参数键名</param>
        /// <param name="value">输出值</param>
        /// <returns>是否成功获取</returns>
        protected bool TryGetParameter<T>(Dictionary<string, object> parameters, string key, out T value)
        {
            value = default!;
            if (!parameters.TryGetValue(key, out var obj) || obj == null) 
                return false;

            try
            {
                if (typeof(T) == typeof(bool) && obj is string strVal)
                {
                    // 特殊处理布尔类型的字符串转换
                    value = (T)(object)bool.Parse(strVal);
                }
                else
                {
                    value = (T)Convert.ChangeType(obj, typeof(T));
                }
                return true;
            }
            catch
            {
                _logger.LogWarning("参数 {Key} 类型转换失败", key);
                return false;
            }
        }

        #endregion
    }
}