using OpenCvSharp;
using AutoHPMA.Contracts.Services;
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

    /// <summary>
    /// 识别图像中的文字和对应区域
    /// </summary>
    public IReadOnlyList<OcrTextRegion> OcrRegions(Mat mat)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var result = _paddleOcrAll.Run(mat);
        return result.Regions
            .Select(region => new OcrTextRegion(
                region.Text,
                ToBoundingRect(region.Rect, mat),
                ToNullableScore(region.Score)))
            .ToArray();
    }

    private static Rect ToBoundingRect(RotatedRect rotatedRect, Mat source)
    {
        var points = rotatedRect.Points();
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);

        var x = Math.Clamp((int)Math.Floor(minX), 0, Math.Max(source.Width - 1, 0));
        var y = Math.Clamp((int)Math.Floor(minY), 0, Math.Max(source.Height - 1, 0));
        var right = Math.Clamp((int)Math.Ceiling(maxX), x + 1, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(maxY), y + 1, source.Height);
        return new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static float? ToNullableScore(double score)
    {
        var value = Convert.ToSingle(score);
        return float.IsNaN(value) || float.IsInfinity(value)
            ? null
            : value;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _paddleOcrAll?.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
