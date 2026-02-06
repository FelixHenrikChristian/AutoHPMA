using AutoHPMA.Helpers;
using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Helpers.ImageHelper;
using AutoHPMA.Helpers.RecognizeHelper;
using AutoHPMA.Models;
using AutoHPMA.Views.Windows;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Wpf.Ui.Controls;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace AutoHPMA.ViewModels.Pages
{
    public partial class TestViewModel : ObservableObject
    {
        #region Observable Properties

        // 截屏测试
        [ObservableProperty]
        private CaptureMethod _selectedCaptureMethod = CaptureMethod.WGC;

        public ObservableCollection<CaptureMethod> CaptureMethods { get; } =
            new((CaptureMethod[])Enum.GetValues(typeof(CaptureMethod)));

        // 窗口选择
        [ObservableProperty]
        private WindowInfo? _selectedWindow;

        public ObservableCollection<WindowInfo> AvailableWindows { get; } = new();

        // 鼠标模拟
        [ObservableProperty]
        private WindowInfo? _selectedClickWindow;

        [ObservableProperty]
        private WindowInfo? _selectedClickChildWindow;

        public ObservableCollection<WindowInfo> AvailableChildWindows { get; } = new();

        [ObservableProperty]
        private bool _hasChildWindows = false;

        // 统一的鼠标动作列表
        public ObservableCollection<MouseActionModel> MouseActions { get; } = new();

        [ObservableProperty]
        private MouseActionModel? _selectedMouseAction;

        // 文字识别
        [ObservableProperty]
        private string _ocrResult = string.Empty;

        [ObservableProperty]
        private string _selectedOCR = "PaddleOCR";

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _ocrPreviewImage;

        [ObservableProperty]
        private bool _hideWindowOnScreenshot = false;

        public ObservableCollection<string> OCRs { get; } = new() { "PaddleOCR", "Tesseract" };

        // 模板匹配
        [ObservableProperty]
        private string? _sourceImagePath;

        [ObservableProperty]
        private string? _templateImagePath;

        [ObservableProperty]
        private string? _maskImagePath;

        [ObservableProperty]
        private double _threshold = 0.8;

        public ObservableCollection<TemplateMatchModes> MatchModes { get; } =
            new((TemplateMatchModes[])Enum.GetValues(typeof(TemplateMatchModes)));

        [ObservableProperty]
        private TemplateMatchModes _selectedMatchMode = TemplateMatchModes.CCoeffNormed;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _resultImage;

        [ObservableProperty]
        private string _matchRectInfo = string.Empty;

        [ObservableProperty]
        private List<Rect> _matchRects = new();

        // 图像预览
        [ObservableProperty]
        private System.Windows.Media.ImageSource? _sourceImagePreview;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _templateImagePreview;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _maskImagePreview;

        [ObservableProperty]
        private int _matchCount = 0;

        // 路径变更时更新预览
        partial void OnSourceImagePathChanged(string? value) => UpdateImagePreview(value, v => SourceImagePreview = v);
        partial void OnTemplateImagePathChanged(string? value) => UpdateImagePreview(value, v => TemplateImagePreview = v);
        partial void OnMaskImagePathChanged(string? value) => UpdateImagePreview(value, v => MaskImagePreview = v);

        private void UpdateImagePreview(string? path, Action<System.Windows.Media.ImageSource?> setter)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                setter(null);
                return;
            }
            try
            {
                using var mat = Cv2.ImRead(path);
                setter(ToImageSource(mat));
            }
            catch
            {
                setter(null);
            }
        }

        // 轮廓检测
        [ObservableProperty] private string? _contourImagePath;
        [ObservableProperty] private System.Windows.Media.ImageSource? _contourImagePreview;
        [ObservableProperty] private System.Windows.Media.ImageSource? _binarizedImagePreview;
        [ObservableProperty] private System.Windows.Media.ImageSource? _contourResultImage;
        [ObservableProperty] private double _binarizeThreshold = 200;
        [ObservableProperty] private string _detectedRectInfo = string.Empty;

        // 路径变更时更新预览
        partial void OnContourImagePathChanged(string? value) => UpdateImagePreview(value, v => ContourImagePreview = v);

        // 色彩过滤
        [ObservableProperty] private string? _colorFilterSourcePath;
        [ObservableProperty] private string? _colorFilterMaskPath;
        [ObservableProperty] private string _targetColorHex = "ffffff";
        [ObservableProperty] private int _colorThreshold = 30;  // 色相阈值 (H)
        [ObservableProperty] private int _saturationTolerance = 100; // 饱和度容差 (S)
        [ObservableProperty] private int _valueTolerance = 100;  // 明度容差 (V/L)
        [ObservableProperty] private string _selectedColorSpace = "LAB"; // 颜色空间
        [ObservableProperty] private System.Windows.Media.ImageSource? _colorFilterResultImage;
        [ObservableProperty] private string _colorFilterStats = string.Empty;
        [ObservableProperty] private System.Windows.Media.ImageSource? _colorFilterSourcePreview;
        [ObservableProperty] private System.Windows.Media.ImageSource? _colorFilterMaskPreview;

        // 颜色空间选项
        public string[] ColorSpaceOptions { get; } = new[] { "HSV", "LAB" };

        // 路径变更时更新预览
        partial void OnColorFilterSourcePathChanged(string? value) => UpdateImagePreview(value, v => ColorFilterSourcePreview = v);
        partial void OnColorFilterMaskPathChanged(string? value) => UpdateImagePreview(value, v => ColorFilterMaskPreview = v);

        #endregion

        #region 构造函数

        public TestViewModel()
        {
            RefreshWindowList();
            // 添加示例动作
            MouseActions.Add(MouseActionModel.CreateClick(200, 200));
        }

        #endregion

        #region 通用辅助方法

        /// <summary>
        /// 显示错误消息框
        /// </summary>
        private static void ShowError(string content)
        {
            var box = new Wpf.Ui.Controls.MessageBox { Title = "❎ 错误", Content = content };
            _ = box.ShowDialogAsync();
        }

        /// <summary>
        /// 显示成功消息框
        /// </summary>
        private static void ShowSuccess(string content)
        {
            var box = new Wpf.Ui.Controls.MessageBox { Title = "✅ 成功", Content = content };
            _ = box.ShowDialogAsync();
        }

        /// <summary>
        /// 打开文件选择对话框
        /// </summary>
        private static string? SelectImageFile(string filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg")
        {
            var dlg = new OpenFileDialog { Filter = filter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        /// <summary>
        /// 验证窗口是否已选择
        /// </summary>
        private bool ValidateWindow(WindowInfo? window, string action)
        {
            if (window == null)
            {
                ShowError($"请先选择要{action}的窗口");
                return false;
            }
            if (window.Handle == IntPtr.Zero)
            {
                ShowError("目标窗口句柄无效");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 将 Mat 转换为 ImageSource
        /// </summary>
        private static System.Windows.Media.ImageSource ToImageSource(Mat mat)
        {
            var bmp = BitmapSourceConverter.ToBitmapSource(mat);
            bmp.Freeze();
            return bmp;
        }

        #endregion

        #region 窗口管理

        [RelayCommand]
        private void RefreshWindowList()
        {
            AvailableWindows.Clear();
            AvailableChildWindows.Clear();
            SelectedClickWindow = null;
            SelectedClickChildWindow = null;
            HasChildWindows = false;

            // 只获取父窗口列表
            var windows = WindowHelper.GetAllVisibleWindows();
            foreach (var window in windows)
                AvailableWindows.Add(window);
        }

        /// <summary>
        /// 刷新子窗口列表（基于选中的父窗口）
        /// </summary>
        private void RefreshChildWindowList()
        {
            AvailableChildWindows.Clear();
            SelectedClickChildWindow = null;
            HasChildWindows = false;

            if (SelectedClickWindow == null)
                return;

            var childWindows = WindowHelper.GetChildWindows(
                SelectedClickWindow.Handle,
                SelectedClickWindow.ProcessName,
                SelectedClickWindow.ProcessId);

            if (childWindows.Count > 0)
            {
                HasChildWindows = true;
                foreach (var child in childWindows)
                    AvailableChildWindows.Add(child);
            }
        }

        partial void OnSelectedClickWindowChanged(WindowInfo? value) => RefreshChildWindowList();

        /// <summary>
        /// 获取实际用于交互的窗口句柄（优先使用子窗口）
        /// </summary>
        private IntPtr GetEffectiveClickWindowHandle()
        {
            if (SelectedClickChildWindow != null)
                return SelectedClickChildWindow.Handle;
            return SelectedClickWindow?.Handle ?? IntPtr.Zero;
        }

        #endregion

        #region 截屏测试

        [RelayCommand]
        public void OnScreenshotTest(object sender)
        {
            if (!ValidateWindow(SelectedWindow, "截屏")) return;

            try
            {
                var previewWindow = new ScreenshotPreviewWindow(_selectedCaptureMethod, SelectedWindow!.Handle);
                previewWindow.Show();
            }
            catch (Exception ex)
            {
                ShowError("打开预览窗口失败：" + ex.Message);
            }
        }

        #endregion

        #region 鼠标模拟

        /// <summary>
        /// 执行单个鼠标动作
        /// </summary>
        private async Task ExecuteSingleAction(MouseActionModel action, IntPtr hwnd)
        {
            switch (action.ActionType)
            {
                case MouseActionType.Click:
                    for (int i = 0; i < action.Times; i++)
                    {
                        await WindowInteractionHelper.SendMouseClickAsync(hwnd, (uint)action.X, (uint)action.Y);
                        if (i < action.Times - 1)
                            await Task.Delay(action.Interval);
                    }
                    break;

                case MouseActionType.Drag:
                    for (int i = 0; i < action.Times; i++)
                    {
                        await WindowInteractionHelper.SendMouseDragWithNoiseAsync(
                            hwnd,
                            (uint)action.X, (uint)action.Y,
                            (uint)action.EndX, (uint)action.EndY,
                            action.Duration);
                        if (i < action.Times - 1)
                            await Task.Delay(action.Interval);
                    }
                    break;

                case MouseActionType.LongPress:
                    for (int i = 0; i < action.Times; i++)
                    {
                        await WindowInteractionHelper.SendMouseLongPressAsync(hwnd, (uint)action.X, (uint)action.Y, action.Duration);
                        if (i < action.Times - 1)
                            await Task.Delay(action.Interval);
                    }
                    break;
            }
        }

        /// <summary>
        /// 执行所有鼠标动作
        /// </summary>
        [RelayCommand]
        public async Task ExecuteAllActions()
        {
            if (!ValidateWindow(SelectedClickWindow, "执行")) return;

            if (MouseActions.Count == 0)
            {
                ShowError("操作列表为空，请先添加动作");
                return;
            }

            var targetHwnd = GetEffectiveClickWindowHandle();
            if (targetHwnd == IntPtr.Zero)
            {
                ShowError("目标窗口句柄无效");
                return;
            }

            try
            {
                // 先将父窗口置于前台
                WindowInteractionHelper.SetForegroundWindow(SelectedClickWindow!.Handle);
                await Task.Delay(3000);

                for (int i = 0; i < MouseActions.Count; i++)
                {
                    var action = MouseActions[i];
                    await ExecuteSingleAction(action, targetHwnd);

                    // 如果不是最后一个动作，等待间隔时间
                    if (i < MouseActions.Count - 1)
                        await Task.Delay(action.Interval);
                }

                ShowSuccess($"已完成 {MouseActions.Count} 个动作");
            }
            catch (Exception ex)
            {
                ShowError($"执行失败：{ex.Message}");
            }
        }

        #region 动作管理命令

        [RelayCommand]
        public void AddClickAction() =>
            MouseActions.Add(MouseActionModel.CreateClick(200, 200));

        [RelayCommand]
        public void AddDragAction() =>
            MouseActions.Add(MouseActionModel.CreateDrag(200, 200, 400, 400));

        [RelayCommand]
        public void AddLongPressAction() =>
            MouseActions.Add(MouseActionModel.CreateLongPress(200, 200));

        [RelayCommand]
        public void RemoveMouseAction(MouseActionModel? action)
        {
            if (action != null)
                MouseActions.Remove(action);
        }

        [RelayCommand]
        public void MoveActionUp(MouseActionModel? action)
        {
            if (action == null) return;
            int index = MouseActions.IndexOf(action);
            if (index > 0)
                MouseActions.Move(index, index - 1);
        }

        [RelayCommand]
        public void MoveActionDown(MouseActionModel? action)
        {
            if (action == null) return;
            int index = MouseActions.IndexOf(action);
            if (index >= 0 && index < MouseActions.Count - 1)
                MouseActions.Move(index, index + 1);
        }

        [RelayCommand]
        public void ClearAllActions()
        {
            MouseActions.Clear();
            SelectedMouseAction = null;
        }

        [RelayCommand]
        public void ClearSelection()
        {
            SelectedMouseAction = null;
        }

        #endregion

        #region 导入导出

        [RelayCommand]
        public void ExportActions()
        {
            if (MouseActions.Count == 0)
            {
                ShowError("操作列表为空，无法导出");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json",
                DefaultExt = ".json",
                FileName = "mouse_actions"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var actionList = new MouseActionList
                    {
                        Version = "1.0",
                        Actions = MouseActions.ToList()
                    };

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };

                    string json = JsonSerializer.Serialize(actionList, options);
                    File.WriteAllText(dlg.FileName, json);
                    ShowSuccess($"已成功导出 {MouseActions.Count} 个动作");
                }
                catch (Exception ex)
                {
                    ShowError($"导出失败：{ex.Message}");
                }
            }
        }

        [RelayCommand]
        public void ImportActions()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var actionList = JsonSerializer.Deserialize<MouseActionList>(json);

                    if (actionList?.Actions == null || actionList.Actions.Count == 0)
                    {
                        ShowError("文件中没有有效的动作数据");
                        return;
                    }

                    // 替换当前列表
                    MouseActions.Clear();
                    foreach (var action in actionList.Actions)
                        MouseActions.Add(action);

                    ShowSuccess($"已成功导入 {MouseActions.Count} 个动作");
                }
                catch (Exception ex)
                {
                    ShowError($"导入失败：{ex.Message}");
                }
            }
        }

        #endregion

        #endregion

        #region 文字识别

        [RelayCommand]
        public async Task OnOCRTest()
        {
            var filePath = SelectImageFile();
            if (filePath == null) return;

            try
            {
                using Mat mat = Cv2.ImRead(filePath);
                OcrPreviewImage = ToImageSource(mat);
                OcrResult = await Task.Run(() => OcrText(mat));
            }
            catch (Exception ex)
            {
                OcrResult = "识别出错：" + ex.Message;
            }
        }

        [RelayCommand]
        public async Task OCRFromScreenshot()
        {
            // 获取主窗口引用
            System.Windows.Window? mainWindow = null;
            System.Windows.WindowState? previousState = null;
            
            try
            {
                // 如果启用了隐藏窗口选项，最小化应用窗口
                if (HideWindowOnScreenshot)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // 通过 Windows 集合查找 MainWindow
                        mainWindow = System.Windows.Application.Current.Windows
                            .OfType<Views.Windows.MainWindow>()
                            .FirstOrDefault();
                        
                        if (mainWindow != null)
                        {
                            previousState = mainWindow.WindowState;
                            mainWindow.WindowState = System.Windows.WindowState.Minimized;
                        }
                    });
                    await Task.Delay(300); // 等待窗口最小化动画完成
                }

                // 清空剪切板以便检测新的截图
                System.Windows.Clipboard.Clear();

                // 启动 Windows 截图工具
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "ms-screenclip:",
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi);

                // 等待截图工具启动
                await Task.Delay(800);

                // 等待用户完成截图（监控剪切板变化）
                OcrResult = "请完成截图...";
                
                bool screenshotCaptured = false;
                const int maxWaitMs = 60000; // 最多等待60秒
                const int checkIntervalMs = 100; // 每100毫秒检查一次
                int elapsedMs = 0;
                int noChangeCount = 0; // 检测前台窗口无变化计数
                
                // 获取本应用主窗口句柄
                IntPtr mainWindowHandle = IntPtr.Zero;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var win = System.Windows.Application.Current.Windows
                        .OfType<Views.Windows.MainWindow>()
                        .FirstOrDefault();
                    if (win != null)
                    {
                        mainWindowHandle = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                    }
                });
                
                while (elapsedMs < maxWaitMs)
                {
                    // 检查剪贴板是否有图像
                    if (System.Windows.Clipboard.ContainsImage())
                    {
                        screenshotCaptured = true;
                        break;
                    }
                    
                    // 检测当前前台窗口是否为本应用（表示截图工具已关闭）
                    if (mainWindowHandle != IntPtr.Zero && elapsedMs > 2000)
                    {
                        var foregroundWindow = Helpers.WindowInteractionHelper.GetForegroundWindow();
                        if (foregroundWindow == mainWindowHandle)
                        {
                            noChangeCount++;
                            // 连续检测到多次前台窗口是本应用，说明截图工具已关闭
                            if (noChangeCount >= 3)
                            {
                                break;
                            }
                        }
                        else
                        {
                            noChangeCount = 0;
                        }
                    }
                    
                    await Task.Delay(checkIntervalMs);
                    elapsedMs += checkIntervalMs;
                }

                if (!screenshotCaptured)
                {
                    OcrResult = "截图已取消";
                    return;
                }

                // 从剪切板获取截图并识别
                var bitmapSource = System.Windows.Clipboard.GetImage();
                if (bitmapSource == null)
                {
                    OcrResult = "无法获取截图";
                    return;
                }

                OcrPreviewImage = bitmapSource;
                using Mat mat = BitmapSourceToMat(bitmapSource);
                OcrResult = await Task.Run(() => OcrText(mat));
            }
            catch (Exception ex)
            {
                OcrResult = "识别出错：" + ex.Message;
            }
            finally
            {
                // 恢复窗口状态
                if (previousState.HasValue && mainWindow != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        mainWindow.WindowState = previousState.Value;
                        mainWindow.Activate();
                    });
                }
            }
        }

        /// <summary>
        /// 将 BitmapSource 转换为 OpenCvSharp Mat
        /// </summary>
        private static Mat BitmapSourceToMat(System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            // 确保像素格式为 Bgra32
            var convertedBitmap = bitmapSource;
            if (bitmapSource.Format != System.Windows.Media.PixelFormats.Bgra32)
            {
                convertedBitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                    bitmapSource, 
                    System.Windows.Media.PixelFormats.Bgra32, 
                    null, 
                    0);
            }

            int width = convertedBitmap.PixelWidth;
            int height = convertedBitmap.PixelHeight;
            int stride = width * 4; // BGRA format
            byte[] pixels = new byte[height * stride];
            convertedBitmap.CopyPixels(pixels, stride, 0);

            Mat mat = new Mat(height, width, MatType.CV_8UC4);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
            
            // 转换为 BGR (OpenCV 标准格式)
            Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2BGR);
            return mat;
        }

        private string OcrText(Mat mat)
        {
            return SelectedOCR switch
            {
                "PaddleOCR" => PaddleOCRHelper.Instance.Ocr(mat),
                "Tesseract" => TesseractOCRHelper.Instance.Ocr(mat),
                _ => string.Empty
            };
        }

        #endregion

        #region 模板匹配

        [RelayCommand]
        private void OnSelectSourceImage() => SourceImagePath = SelectImageFile() ?? SourceImagePath;

        [RelayCommand]
        private void OnSelectTemplateImage() => TemplateImagePath = SelectImageFile() ?? TemplateImagePath;

        [RelayCommand]
        private void OnSelectMaskImage() => MaskImagePath = SelectImageFile() ?? MaskImagePath;

        [RelayCommand]
        private void OnClearMask()
        {
            MaskImagePath = null;
            MaskImagePreview = null;
        }

        [RelayCommand]
        private void OnTemplateMatch()
        {
            if (string.IsNullOrEmpty(SourceImagePath) || string.IsNullOrEmpty(TemplateImagePath))
                return;

            // 使用 using 语句确保资源正确释放
            using Mat originalMat = Cv2.ImRead(SourceImagePath);
            using Mat templateMatWithAlpha = Cv2.ImRead(TemplateImagePath, ImreadModes.Unchanged);

            // 生成 mask（如果需要）
            using Mat? maskMat = GetMaskMat(templateMatWithAlpha);

            // 将模板转换为 BGR 格式用于匹配
            using Mat templateMat = templateMatWithAlpha.Channels() == 4
                ? new Mat()
                : templateMatWithAlpha.Clone();

            if (templateMatWithAlpha.Channels() == 4)
            {
                Cv2.CvtColor(templateMatWithAlpha, templateMat, ColorConversionCodes.BGRA2BGR);
            }

            var matches = MatchTemplateHelper.MatchOnePicForOnePic(
                originalMat, templateMat, SelectedMatchMode, maskMat, threshold: Threshold);

            MatchRects = matches;
            MatchCount = matches.Count;
            MatchRectInfo = string.Join("\n", matches.Select(r => $"X: {r.X}, Y: {r.Y}, Width: {r.Width}, Height: {r.Height}"));

            // 创建结果图像副本用于绘制
            using Mat resultMat = originalMat.Clone();
            foreach (var rect in matches)
            {
                Cv2.Rectangle(resultMat, new Point(rect.X, rect.Y),
                    new Point(rect.X + rect.Width, rect.Y + rect.Height), Scalar.Red, thickness: 2);
            }

            ResultImage = ToImageSource(resultMat);
        }

        /// <summary>
        /// 获取遮罩图像（调用方负责释放返回的 Mat）
        /// </summary>
        private Mat? GetMaskMat(Mat templateMat)
        {
            if (!string.IsNullOrEmpty(MaskImagePath))
            {
                var mask = Cv2.ImRead(MaskImagePath, ImreadModes.Grayscale);
                return mask;
            }

            if (templateMat.Channels() == 4)
            {
                var mask = MatchTemplateHelper.GenerateMask(templateMat);
                // 可选：保存生成的 mask 用于调试
                string maskPath = Path.ChangeExtension(TemplateImagePath, null) + "_mask.png";
                mask.SaveImage(maskPath);
                return mask;
            }

            return null;
        }

        [RelayCommand]
        private void OnCropImage()
        {
            if (string.IsNullOrEmpty(SourceImagePath) || MatchRects.Count == 0)
                return;

            try
            {
                using Mat originalMat = Cv2.ImRead(SourceImagePath);
                string directory = Path.GetDirectoryName(SourceImagePath)!;
                string fileName = Path.GetFileNameWithoutExtension(SourceImagePath);
                string extension = Path.GetExtension(SourceImagePath);

                for (int i = 0; i < MatchRects.Count; i++)
                {
                    using var croppedMat = new Mat(originalMat, MatchRects[i]);
                    string outputPath = Path.Combine(directory, $"{fileName}_cropped_{i + 1}{extension}");
                    croppedMat.SaveImage(outputPath);
                }

                ShowSuccess($"已成功裁切 {MatchRects.Count} 个区域");
            }
            catch (Exception ex)
            {
                ShowError("裁切失败：" + ex.Message);
            }
        }

        #endregion

        #region 轮廓检测

        [RelayCommand]
        private void SelectContourImage() => ContourImagePath = SelectImageFile() ?? ContourImagePath;

        [RelayCommand]
        private void TestBinarize()
        {
            if (string.IsNullOrEmpty(ContourImagePath)) return;

            try
            {
                using Mat src = Cv2.ImRead(ContourImagePath, ImreadModes.Color);
                using Mat binary = ContourDetectHelper.Binarize(src, BinarizeThreshold);
                BinarizedImagePreview = ToImageSource(binary);
            }
            catch (Exception ex)
            {
                ShowError("二值化失败：" + ex.Message);
            }
        }

        [RelayCommand]
        private void TestDetectRectangle()
        {
            if (string.IsNullOrEmpty(ContourImagePath)) return;

            try
            {
                using Mat src = Cv2.ImRead(ContourImagePath, ImreadModes.Color);
                using Mat binary = ContourDetectHelper.Binarize(src, BinarizeThreshold);
                var rect = ContourDetectHelper.DetectApproxRectangle(binary);
                
                // 在原图上绘制检测到的矩形
                using Mat result = src.Clone();
                if (rect.Width > 0 && rect.Height > 0)
                {
                    Cv2.Rectangle(result, rect, Scalar.Red, 2);
                    DetectedRectInfo = $"X: {rect.X}, Y: {rect.Y}, 宽: {rect.Width}, 高: {rect.Height}";
                }
                else
                {
                    DetectedRectInfo = "未检测到有效矩形";
                }
                
                ContourResultImage = ToImageSource(result);
            }
            catch (Exception ex)
            {
                ShowError("矩形检测失败：" + ex.Message);
            }
        }

        #endregion

        #region 色彩过滤

        [RelayCommand]
        private void SelectColorFilterSource() => ColorFilterSourcePath = SelectImageFile() ?? ColorFilterSourcePath;

        [RelayCommand]
        private void SelectColorFilterMask() => ColorFilterMaskPath = SelectImageFile() ?? ColorFilterMaskPath;

        [RelayCommand]
        private void ClearColorFilterMask()
        {
            ColorFilterMaskPath = null;
            ColorFilterMaskPreview = null;
        }

        [RelayCommand]
        private void OpenColorPicker()
        {
            using var colorDialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true, // 默认展开完整调色板
                SolidColorOnly = true
            };

            // 尝试设置当前颜色为初始值
            try
            {
                var currentColor = ColorFilterHelper.GetColorFromHex(TargetColorHex);
                colorDialog.Color = System.Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B);
            }
            catch { }

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedColor = colorDialog.Color;
                TargetColorHex = $"{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}".ToLower();
            }
        }

        [RelayCommand]
        private void StartColorFilter()
        {
            if (string.IsNullOrEmpty(ColorFilterSourcePath)) return;

            try
            {
                using Mat sourceMat = Cv2.ImRead(ColorFilterSourcePath);
                if (sourceMat.Empty())
                    throw new Exception("无法读取源图像");

                Mat? maskMat = null;
                if (!string.IsNullOrEmpty(ColorFilterMaskPath))
                {
                    maskMat = Cv2.ImRead(ColorFilterMaskPath);
                    if (maskMat.Empty())
                    {
                        maskMat?.Dispose();
                        throw new Exception("无法读取遮罩图像");
                    }
                }

                try
                {
                    using var resultMat = ColorFilterHelper.FilterColor(
                        sourceMat, maskMat, TargetColorHex, 
                        ColorThreshold, SaturationTolerance, ValueTolerance, SelectedColorSpace);
                    ColorFilterResultImage = ToImageSource(resultMat);

                    int totalFilterPixels = resultMat.Get<int>(ColorFilterHelper.KEY_TOTAL_FILTER_PIXELS);
                    int matchedPixels = resultMat.Get<int>(ColorFilterHelper.KEY_MATCHED_PIXELS);
                    double percentage = totalFilterPixels > 0 ? (double)matchedPixels / totalFilterPixels * 100 : 0;

                    ColorFilterStats = $"[{SelectedColorSpace}] 参与: {totalFilterPixels} | 匹配: {matchedPixels} | 占比: {percentage:F2}%";
                }
                finally
                {
                    maskMat?.Dispose();
                }
            }
            catch (Exception ex)
            {
                ShowError("色彩过滤失败：" + ex.Message);
            }
        }

        #endregion
    }
}
