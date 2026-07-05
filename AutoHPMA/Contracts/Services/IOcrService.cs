using OpenCvSharp;
using Windows.Graphics.Imaging;

namespace AutoHPMA.Contracts.Services;

public enum OcrEngineType
{
    PaddleOCR,
    WindowsOCR,
    RapidOCR,
    TesseractOCR,
}

public sealed record OcrTextRegion(
    string Text,
    Rect Bounds,
    float? Score = null);

public interface IOcrService
{
    Task<string> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default);

    Task<string> RecognizeAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OcrTextRegion>> RecognizeRegionsAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default);
}
