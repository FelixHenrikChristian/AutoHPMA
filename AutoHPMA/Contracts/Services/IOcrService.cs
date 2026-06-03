using Windows.Graphics.Imaging;

namespace AutoHPMA.Contracts.Services;

public enum OcrEngineType
{
    PaddleOCR,
    WindowsOCR,
    RapidOCR,
    TesseractOCR,
}

public interface IOcrService
{
    Task<string> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default);
}
