using AutoHPMA.Config;
using AutoHPMA.Helpers.RecognizeHelper;
using OpenCvSharp;

namespace AutoHPMA.Services;

/// <summary>
/// OCR 服务实现
/// </summary>
public class OcrService : IOcrService
{
    private readonly AppSettings _settings;

    public OcrService(AppSettings settings)
    {
        _settings = settings;
    }

    public OcrEngineType CurrentEngine => _settings.SelectedOCR?.ToLowerInvariant() switch
    {
        "tesseract" => OcrEngineType.Tesseract,
        "windowsocr" => OcrEngineType.WindowsOCR,
        "rapidocr" => OcrEngineType.RapidOCR,
        _ => OcrEngineType.PaddleOCR
    };

    public string Recognize(Mat mat)
    {
        return CurrentEngine switch
        {
            OcrEngineType.PaddleOCR => PaddleOCRHelper.Instance.Ocr(mat),
            OcrEngineType.Tesseract => TesseractOCRHelper.Instance.Ocr(mat),
            OcrEngineType.WindowsOCR => WindowsOCRHelper.Instance.Ocr(mat),
            OcrEngineType.RapidOCR => RapidOCRHelper.Instance.Ocr(mat),
            _ => throw new NotSupportedException($"不支持的 OCR 引擎: {CurrentEngine}")
        };
    }
}

