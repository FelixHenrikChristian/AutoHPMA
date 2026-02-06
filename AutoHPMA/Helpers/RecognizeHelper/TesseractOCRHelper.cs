using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Tesseract;

namespace AutoHPMA.Helpers.RecognizeHelper;

/// <summary>
/// Tesseract OCR 辅助类（单例模式）
/// </summary>
public class TesseractOCRHelper : IDisposable
{
    private static readonly Lazy<TesseractOCRHelper> _instance = new(() => new TesseractOCRHelper());
    
    public static TesseractOCRHelper Instance => _instance.Value;

    private static readonly string TessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    
    private readonly TesseractEngine _engine;
    private bool _isDisposed;

    private TesseractOCRHelper()
    {
        _engine = new TesseractEngine(TessDataPath, "chi_sim", EngineMode.Default);
    }

    /// <summary>
    /// 识别图像中的文字
    /// </summary>
    public string Ocr(Mat mat)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        
        try
        {
            using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
            using var preprocessed = PreprocessImage(bitmap);
            using var pix = BitmapToPix(preprocessed);
            using var page = _engine.Process(pix);
            return page.GetText().Trim();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tesseract OCR Error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 图像预处理：灰度化、二值化、降噪
    /// </summary>
    private static Bitmap PreprocessImage(Bitmap inputBitmap)
    {
        using Mat src = OpenCvSharp.Extensions.BitmapConverter.ToMat(inputBitmap);
        using Mat gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using Mat binary = new Mat();
        Cv2.Threshold(gray, binary, 128, 255, ThresholdTypes.Binary);

        using Mat denoised = new Mat();
        Cv2.MedianBlur(binary, denoised, 3);

        return OpenCvSharp.Extensions.BitmapConverter.ToBitmap(denoised);
    }

    /// <summary>
    /// 将 Bitmap 转换为 Tesseract Pix 对象
    /// </summary>
    private static Pix BitmapToPix(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Pix.LoadFromMemory(ms.ToArray());
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _engine?.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
