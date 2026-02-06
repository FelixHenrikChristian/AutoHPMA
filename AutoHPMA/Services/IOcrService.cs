using OpenCvSharp;

namespace AutoHPMA.Services;

/// <summary>
/// OCR 引擎类型
/// </summary>
public enum OcrEngineType
{
    PaddleOCR,
    Tesseract
}

/// <summary>
/// OCR 服务接口
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// 识别图像中的文字
    /// </summary>
    string Recognize(Mat mat);

    /// <summary>
    /// 当前使用的 OCR 引擎
    /// </summary>
    OcrEngineType CurrentEngine { get; }
}
