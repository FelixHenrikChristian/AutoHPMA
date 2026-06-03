using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using System;

namespace AutoHPMA.Helpers.RecognizeHelper;

/// <summary>
/// PaddleOCR 辅助类（单例模式）
/// </summary>
public class PaddleOCRHelper : IDisposable
{
    private static readonly Lazy<PaddleOCRHelper> _instance = new(() => new PaddleOCRHelper());
    
    public static PaddleOCRHelper Instance => _instance.Value;

    private readonly PaddleOcrAll _paddleOcrAll;
    private bool _isDisposed;

    private PaddleOCRHelper()
    {
        _paddleOcrAll = new PaddleOcrAll(LocalFullModels.ChineseV4, PaddleDevice.Onnx())
        {
            AllowRotateDetection = false,
            Enable180Classification = false
        };
    }

    /// <summary>
    /// 识别图像中的文字
    /// </summary>
    public string Ocr(Mat mat)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _paddleOcrAll.Run(mat).Text;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _paddleOcrAll?.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
