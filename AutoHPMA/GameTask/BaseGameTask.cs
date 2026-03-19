using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Helpers.ImageHelper;
using AutoHPMA.GameTask.Model;
using AutoHPMA.GameTask.Core.Recognition;
using AutoHPMA.GameTask.Core.Simulator;
using AutoHPMA.GameTask.Core.Overlay;
using AutoHPMA.Services;
using AutoHPMA.Services.Interface;
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
    public abstract class BaseGameTask : IGameTask
    {
        protected LogWindow _logWindow => _appContextService.LogWindow;
        protected MaskWindow _maskWindow => _appContextService.MaskWindow;
        protected WindowsGraphicsCapture _capture => _appContextService.Capture;
        protected readonly IAppContextService _appContextService;

        protected readonly ILogger _logger;
        protected nint _displayHwnd, _gameHwnd;
        protected int _offsetX, _offsetY;
        protected double _scale;
        protected CancellationTokenSource _cts;
        protected bool _hasWaitedForInitialState = false;
        protected Dictionary<string, Mat> _images = new();
        private bool _disposed = false;

        // 核心服务
        protected readonly TemplateMatchService _matchService;
        protected readonly InputSimulator _inputSimulator;
        protected readonly OverlayService _overlayService;

        // 状态监测任务
        private Task? _stateMonitorTask;
        private volatile bool _isStateMonitoring = false;
        protected int _stateMonitorIntervalMs = 200;

        // 操作级别的取消令牌，用于在状态变化时立即取消当前操作
        private CancellationTokenSource? _operationCts;
        private readonly object _operationCtsLock = new object();

        public event EventHandler? TaskCompleted;

        public BaseGameTask(ILogger logger, IAppContextService appContextService, nint displayHwnd, nint gameHwnd)
        {
            _logger = logger;
            _appContextService = appContextService;
            _displayHwnd = displayHwnd;
            _gameHwnd = gameHwnd;
            _cts = new CancellationTokenSource();
            InitializeOperationCts();
            CalculateOffset();

            _matchService = new TemplateMatchService(_scale);
            _inputSimulator = new InputSimulator(_gameHwnd, _scale, _offsetX, _offsetY);
            _overlayService = new OverlayService(_maskWindow);
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
                catch (Exception ex) { _logger.LogDebug(ex, "取消操作CTS时异常"); }

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
        protected void CalculateOffset()
        {
            int left, top, width, height;
            int leftMumu, topMumu;
            WindowInteractionHelper.GetWindowPositionAndSize(_displayHwnd, out leftMumu, out topMumu, out width, out height);
            WindowInteractionHelper.GetWindowPositionAndSize(_gameHwnd, out left, out top, out width, out height);
            _offsetX = left - leftMumu;
            _offsetY = top - topMumu;
            _scale = width / 1280.0;
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
            Cv2.Resize(captureMat, captureMat, new Size(captureMat.Width / _scale, captureMat.Height / _scale));
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

        protected MatchResult FindInSource(Mat source, Mat template, MatchOptions? options = null)
        {
            return _matchService.FindInSource(source, template, options);
        }

        #endregion

        #region 点击操作（Click）

        protected async Task ClickAsync(Point location)
        {
            await _inputSimulator.ClickAsync(location, OperationToken);
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

        protected async Task<bool> DragMoveAsync(Point start, Point end, int duration = 500)
        {
            return await _inputSimulator.DragMoveAsync(start, end, duration, OperationToken);
        }

        #endregion

        #region 键盘操作

        protected async Task SendSpaceAsync()
        {
            await _inputSimulator.SendSpaceAsync(OperationToken);
        }

        /// <summary>
        /// 异步发送回车键
        /// </summary>
        protected async Task SendEnterAsync()
        {
            await _inputSimulator.SendEnterAsync(OperationToken);
        }

        /// <summary>
        /// 异步发送 ESC 键
        /// </summary>
        protected async Task SendESCAsync()
        {
            await _inputSimulator.SendESCAsync(OperationToken);
        }

        #endregion

        #region 显示检测框

        protected void ShowMatchRects(MatchResult result, int durationMs = 500)
        {
            _overlayService.ShowMatchRects(result, durationMs);
        }

        /// <summary>
        /// 设置任务状态检测框（用于任务特定的状态框，如选项框、厨具状态等）
        /// </summary>
        /// <param name="rects">检测框区域列表</param>
        /// <param name="textContents">可选的文字内容字典</param>
        protected void SetStateRects(List<Rect> rects, Dictionary<Rect, string>? textContents = null)
        {
            _overlayService.SetStateRects(rects, textContents);
        }

        /// <summary>
        /// 清除任务状态检测框
        /// </summary>
        protected void ClearStateRects()
        {
            _overlayService.ClearStateRects();
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
            var captureMat = CaptureAndPreprocess();

            foreach (var (templates, state, displayName, threshold) in rules)
            {
                foreach (var template in templates)
                {
                    var result = FindInSource(captureMat, template, new MatchOptions { Threshold = threshold });
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
            _stateMonitorIntervalMs = _appContextService.StateMonitorInterval;
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
        /// 安全地启动异步任务（fire-and-forget），避免 async void 导致的异常丢失
        /// </summary>
        protected void SafeFireAndForget(Task task)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                    // 任务取消，正常流程
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "任务执行异常：{Message}", ex.Message);
                }
            });
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
                _hasWaitedForInitialState = false;
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

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放托管和非托管资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // 释放 _images 中的所有 Mat
                foreach (var mat in _images.Values)
                    mat?.Dispose();
                _images.Clear();

                // 释放取消令牌
                try { _operationCts?.Dispose(); } catch { }
                try { _cts?.Dispose(); } catch { }
            }
            _disposed = true;
        }

        #endregion
    }
}