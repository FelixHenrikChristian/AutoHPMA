using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoHPMA.Helpers.RecognizeHelper;

/// <summary>
/// RapidOCR 辅助类（单例模式）
/// 使用 PaddleOCR ONNX 模型进行 OCR
/// </summary>
public class RapidOCRHelper : IDisposable
{
    private static readonly Lazy<RapidOCRHelper> _instance = new(() => new RapidOCRHelper());

    public static RapidOCRHelper Instance => _instance.Value;

    private static readonly string ModelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Models", "OCR", "RapidOCR");
    
    // PP-OCRv5 模型路径
    private static readonly string DetModelPath = Path.Combine(ModelsPath, "PP-OCRv5_server_det_infer.onnx");
    private static readonly string RecModelPath = Path.Combine(ModelsPath, "PP-OCRv5_server_rec_infer.onnx");
    private static readonly string KeysPath = Path.Combine(ModelsPath, "ppocr_keys_v5.txt");

    private InferenceSession? _detSession;
    private InferenceSession? _recSession;
    private string[]? _keys;
    private bool _isDisposed;
    private bool _isInitialized;
    private string? _initError;

    private RapidOCRHelper()
    {
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Debug.WriteLine($"RapidOCR: Models directory: {ModelsPath}");
            Debug.WriteLine($"RapidOCR: Directory exists: {Directory.Exists(ModelsPath)}");
            
            // 检查模型和字典文件
            Debug.WriteLine($"RapidOCR: det model exists: {File.Exists(DetModelPath)}");
            Debug.WriteLine($"RapidOCR: rec model exists: {File.Exists(RecModelPath)}");
            Debug.WriteLine($"RapidOCR: keys file exists: {File.Exists(KeysPath)}");
            
            if (!File.Exists(DetModelPath))
            {
                _initError = $"Detection model not found: {DetModelPath}";
                Debug.WriteLine($"RapidOCR: {_initError}");
                return;
            }
            
            if (!File.Exists(RecModelPath))
            {
                _initError = $"Recognition model not found: {RecModelPath}";
                Debug.WriteLine($"RapidOCR: {_initError}");
                return;
            }
            
            if (!File.Exists(KeysPath))
            {
                _initError = $"Keys file not found: {KeysPath}";
                Debug.WriteLine($"RapidOCR: {_initError}");
                return;
            }

            // 加载模型
            Debug.WriteLine("RapidOCR: Loading PP-OCRv5 models...");
            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            _detSession = new InferenceSession(DetModelPath, sessionOptions);
            Debug.WriteLine($"RapidOCR: Detection model loaded. Inputs: {_detSession.InputNames.Count}, Outputs: {_detSession.OutputNames.Count}");
            
            _recSession = new InferenceSession(RecModelPath, sessionOptions);
            Debug.WriteLine($"RapidOCR: Recognition model loaded. Inputs: {_recSession.InputNames.Count}, Outputs: {_recSession.OutputNames.Count}");
            
            // 打印识别模型的输入信息
            foreach (var input in _recSession.InputMetadata)
            {
                Debug.WriteLine($"RapidOCR: Rec model input '{input.Key}': Type={input.Value.ElementType}, Shape=[{string.Join(", ", input.Value.Dimensions)}]");
            }

            // 加载字符集
            _keys = File.ReadAllLines(KeysPath, Encoding.UTF8);
            Debug.WriteLine($"RapidOCR: Loaded {_keys.Length} keys from ppocr_keys_v5.txt");

            _isInitialized = true;
            Debug.WriteLine("RapidOCR: Initialized successfully");
        }
        catch (Exception ex)
        {
            _initError = $"{ex.GetType().Name}: {ex.Message}";
            Debug.WriteLine($"RapidOCR initialization error: {_initError}");
            Debug.WriteLine($"RapidOCR stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 检查 RapidOCR 是否可用
    /// </summary>
    public bool IsAvailable => _isInitialized;

    /// <summary>
    /// 获取初始化错误信息
    /// </summary>
    public string? InitializationError => _initError;

    /// <summary>
    /// 识别图像中的文字
    /// </summary>
    public string Ocr(Mat mat)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_isInitialized)
        {
            Debug.WriteLine($"RapidOCR: Not initialized. Error: {_initError}");
            return string.Empty;
        }

        try
        {
            Debug.WriteLine($"RapidOCR: Input image size: {mat.Width}x{mat.Height}, Channels: {mat.Channels()}");

            // 步骤1: 检测文本区域
            var textBoxes = DetectTextBoxes(mat);
            Debug.WriteLine($"RapidOCR: Detected {textBoxes.Count} text regions");

            if (textBoxes.Count == 0)
            {
                // 如果没检测到文本区域，尝试直接识别整张图
                Debug.WriteLine("RapidOCR: No text boxes detected, trying direct recognition");
                return RecognizeText(mat);
            }

            // 步骤2: 对每个文本区域进行识别
            var results = new StringBuilder();
            foreach (var box in textBoxes)
            {
                using var cropped = CropTextRegion(mat, box);
                if (cropped != null && cropped.Width > 0 && cropped.Height > 0)
                {
                    var text = RecognizeText(cropped);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (results.Length > 0)
                            results.Append('\n');
                        results.Append(text);
                    }
                }
            }

            var finalResult = results.ToString();
            Debug.WriteLine($"RapidOCR: Final result: '{finalResult}' (length: {finalResult.Length})");
            return finalResult;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RapidOCR Error: {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"RapidOCR Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Debug.WriteLine($"RapidOCR Inner Exception: {ex.InnerException.Message}");
            }
            return string.Empty;
        }
    }

    /// <summary>
    /// 使用检测模型找到文本区域
    /// </summary>
    private List<Point2f[]> DetectTextBoxes(Mat mat)
    {
        var boxes = new List<Point2f[]>();
        
        if (_detSession == null)
        {
            Debug.WriteLine("RapidOCR: Detection session is null");
            return boxes;
        }

        try
        {
            // 预处理图像用于检测
            int targetSize = 960; // 检测模型的标准尺寸
            float scale = Math.Min((float)targetSize / mat.Width, (float)targetSize / mat.Height);
            int newWidth = (int)(mat.Width * scale);
            int newHeight = (int)(mat.Height * scale);
            
            // 确保尺寸是32的倍数 (检测模型要求)
            newWidth = (newWidth / 32) * 32;
            newHeight = (newHeight / 32) * 32;
            if (newWidth < 32) newWidth = 32;
            if (newHeight < 32) newHeight = 32;

            using var resized = new Mat();
            Cv2.Resize(mat, resized, new OpenCvSharp.Size(newWidth, newHeight));
            
            // 转换为 RGB
            using var rgb = new Mat();
            if (resized.Channels() == 4)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGRA2RGB);
            else if (resized.Channels() == 1)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
            else
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            // 创建输入张量
            var inputTensor = MatToTensor(rgb);
            Debug.WriteLine($"RapidOCR Det: Input tensor: [{string.Join(", ", inputTensor.Dimensions.ToArray())}]");

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_detSession.InputNames[0], inputTensor)
            };

            using var results = _detSession.Run(inputs);
            var output = results.First().AsTensor<float>();
            Debug.WriteLine($"RapidOCR Det: Output tensor: [{string.Join(", ", output.Dimensions.ToArray())}]");

            // 解析检测结果 - 输出是概率图，需要后处理找到文本框
            boxes = ParseDetectionOutput(output, mat.Width, mat.Height, newWidth, newHeight);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RapidOCR Detection error: {ex.Message}");
        }

        return boxes;
    }

    /// <summary>
    /// 解析检测模型输出，提取文本框
    /// </summary>
    private List<Point2f[]> ParseDetectionOutput(Tensor<float> output, int origWidth, int origHeight, int resizedWidth, int resizedHeight)
    {
        var boxes = new List<Point2f[]>();
        var dims = output.Dimensions.ToArray();
        
        // 输出格式: [1, 1, H, W] - 概率图
        if (dims.Length != 4 || dims[1] != 1)
        {
            Debug.WriteLine($"RapidOCR Det: Unexpected output shape: [{string.Join(", ", dims)}]");
            return boxes;
        }

        int outHeight = dims[2];
        int outWidth = dims[3];

        // 将概率图转换为二值图
        using var probMat = new Mat(outHeight, outWidth, MatType.CV_32FC1);
        for (int y = 0; y < outHeight; y++)
        {
            for (int x = 0; x < outWidth; x++)
            {
                probMat.Set(y, x, output[0, 0, y, x]);
            }
        }

        // 二值化
        using var binaryMat = new Mat();
        Cv2.Threshold(probMat, binaryMat, 0.3, 1.0, ThresholdTypes.Binary);
        
        using var binaryU8 = new Mat();
        binaryMat.ConvertTo(binaryU8, MatType.CV_8UC1, 255);

        // 查找轮廓
        Cv2.FindContours(binaryU8, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        
        Debug.WriteLine($"RapidOCR Det: Found {contours.Length} contours");

        float scaleX = (float)origWidth / resizedWidth;
        float scaleY = (float)origHeight / resizedHeight;

        foreach (var contour in contours)
        {
            if (contour.Length < 4)
                continue;

            var rect = Cv2.MinAreaRect(contour);
            if (rect.Size.Width < 5 || rect.Size.Height < 5)
                continue;

            var pts = rect.Points();
            var scaledPts = pts.Select(p => new Point2f(
                p.X * scaleX,
                p.Y * scaleY
            )).ToArray();

            boxes.Add(scaledPts);
        }

        return boxes;
    }

    /// <summary>
    /// 从原图中裁剪文本区域
    /// </summary>
    private Mat? CropTextRegion(Mat src, Point2f[] box)
    {
        try
        {
            // 计算边界框
            float minX = box.Min(p => p.X);
            float maxX = box.Max(p => p.X);
            float minY = box.Min(p => p.Y);
            float maxY = box.Max(p => p.Y);

            // 添加边距
            int padding = 2;
            int x = Math.Max(0, (int)minX - padding);
            int y = Math.Max(0, (int)minY - padding);
            int w = Math.Min(src.Width - x, (int)(maxX - minX) + padding * 2);
            int h = Math.Min(src.Height - y, (int)(maxY - minY) + padding * 2);

            if (w <= 0 || h <= 0)
                return null;

            var rect = new OpenCvSharp.Rect(x, y, w, h);
            return new Mat(src, rect);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 识别单个文本区域
    /// </summary>
    private string RecognizeText(Mat mat)
    {
        if (_recSession == null || _keys == null)
        {
            Debug.WriteLine("RapidOCR: RecSession or Keys is null");
            return string.Empty;
        }

        Debug.WriteLine($"RapidOCR: Input image size: {mat.Width}x{mat.Height}, Channels: {mat.Channels()}");

        // 预处理图像
        using var resized = PreprocessForRecognition(mat);
        Debug.WriteLine($"RapidOCR: Resized image size: {resized.Width}x{resized.Height}");

        // 创建输入张量
        var inputTensor = MatToTensor(resized);
        Debug.WriteLine($"RapidOCR: Tensor dimensions: [{string.Join(", ", inputTensor.Dimensions.ToArray())}]");

        // 运行推理
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_recSession.InputNames[0], inputTensor)
        };

        Debug.WriteLine($"RapidOCR: Running inference with input name: {_recSession.InputNames[0]}");
        using var results = _recSession.Run(inputs);
        var output = results.First().AsTensor<float>();
        Debug.WriteLine($"RapidOCR: Output dimensions: [{string.Join(", ", output.Dimensions.ToArray())}]");

        // 解码输出
        var text = DecodeOutput(output);
        Debug.WriteLine($"RapidOCR: Decoded text: '{text}' (length: {text.Length})");
        return text;
    }

    /// <summary>
    /// 预处理图像用于识别
    /// </summary>
    private Mat PreprocessForRecognition(Mat mat)
    {
        // 转换为 RGB（如果需要）
        Mat rgb;
        if (mat.Channels() == 1)
        {
            rgb = new Mat();
            Cv2.CvtColor(mat, rgb, ColorConversionCodes.GRAY2RGB);
        }
        else if (mat.Channels() == 4)
        {
            rgb = new Mat();
            Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGRA2RGB);
        }
        else
        {
            rgb = new Mat();
            Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);
        }

        // 调整高度为 48（PaddleOCR 识别模型的标准输入高度）
        const int targetHeight = 48;
        double ratio = (double)targetHeight / rgb.Height;
        int targetWidth = (int)(rgb.Width * ratio);
        
        // 限制最大宽度
        targetWidth = Math.Min(targetWidth, 1280);

        var resized = new Mat();
        Cv2.Resize(rgb, resized, new OpenCvSharp.Size(targetWidth, targetHeight));
        rgb.Dispose();

        return resized;
    }

    /// <summary>
    /// 将 Mat 转换为 ONNX 输入张量
    /// </summary>
    private DenseTensor<float> MatToTensor(Mat mat)
    {
        int height = mat.Height;
        int width = mat.Width;
        int channels = mat.Channels();

        var tensor = new DenseTensor<float>(new[] { 1, channels, height, width });

        // 归一化参数 (PaddleOCR 标准)
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        var data = new byte[height * width * channels];
        System.Runtime.InteropServices.Marshal.Copy(mat.Data, data, 0, data.Length);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int idx = (y * width + x) * channels + c;
                    float value = data[idx] / 255.0f;
                    tensor[0, c, y, x] = (value - mean[c]) / std[c];
                }
            }
        }

        return tensor;
    }

    /// <summary>
    /// 解码模型输出为文本
    /// </summary>
    private string DecodeOutput(Tensor<float> output)
    {
        if (_keys == null)
            return string.Empty;

        var dims = output.Dimensions.ToArray();
        int seqLen = dims[1];
        int numClasses = dims[2];

        Debug.WriteLine($"RapidOCR Decode: SeqLen={seqLen}, NumClasses={numClasses}, Keys count={_keys.Length}");

        var result = new StringBuilder();
        int lastIdx = 0;
        int nonBlankCount = 0;

        for (int t = 0; t < seqLen; t++)
        {
            // 找到当前时间步的最大概率索引
            int maxIdx = 0;
            float maxVal = float.MinValue;

            for (int c = 0; c < numClasses; c++)
            {
                float val = output[0, t, c];
                if (val > maxVal)
                {
                    maxVal = val;
                    maxIdx = c;
                }
            }

            // CTC 解码：跳过空白符 (index 0) 和连续重复
            // 注意：空白符用于分隔重复的相同字符
            if (maxIdx != 0 && maxIdx != lastIdx)
            {
                if (maxIdx <= _keys.Length)
                {
                    result.Append(_keys[maxIdx - 1]);
                    nonBlankCount++;
                }
            }

            lastIdx = maxIdx;
        }

        Debug.WriteLine($"RapidOCR Decode: Found {nonBlankCount} non-blank characters");
        return result.ToString();
    }

    /// <summary>
    /// 获取模型下载说明
    /// </summary>
    public static string GetModelDownloadInstructions()
    {
        return $"""
            RapidOCR 需要下载模型文件到以下目录:
            {ModelsPath}

            需要的文件 (PP-OCRv5):
            1. PP-OCRv5_server_det_infer.onnx (检测模型)
            2. PP-OCRv5_server_rec_infer.onnx (识别模型)
            3. ppocr_keys_v5.txt (字符字典，约18382字符)

            下载地址:
            魔搭: https://www.modelscope.cn/models/RapidAI/RapidOCR/files

            字符字典从 PP-OCRv5_server_rec_infer.yml 中提取
            """;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _detSession?.Dispose();
        _recSession?.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
