using AutoHPMA.Contracts.Services;
using AutoHPMA.Helpers.RecognizeHelper;
using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace AutoHPMA.Services;

public sealed class OcrService : IOcrService
{
    private readonly Lazy<OcrEngine?> _windowsOcrEngine = new(CreateWindowsOcrEngine);

    public async Task<string> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        return engineType switch
        {
            OcrEngineType.WindowsOCR => await RecognizeWithWindowsOcrAsync(bitmap, cancellationToken),
            OcrEngineType.PaddleOCR => await RecognizeWithMatAsync(bitmap, static mat => PaddleOCRHelper.Instance.Ocr(mat), cancellationToken),
            OcrEngineType.RapidOCR => await RecognizeWithMatAsync(bitmap, static mat => RapidOCRHelper.Instance.Ocr(mat), cancellationToken),
            OcrEngineType.TesseractOCR => await RecognizeWithMatAsync(bitmap, static mat => TesseractOCRHelper.Instance.Ocr(mat), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(engineType), engineType, "不支持的 OCR 引擎。"),
        };
    }

    private async Task<string> RecognizeWithWindowsOcrAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        var engine = _windowsOcrEngine.Value
            ?? throw new InvalidOperationException("当前系统没有可用的 Windows OCR 语言。");

        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
        {
            throw new InvalidOperationException($"图片尺寸超过 Windows OCR 限制：最大 {OcrEngine.MaxImageDimension}px。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        SoftwareBitmap? converted = null;
        var input = bitmap;
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            input = converted;
        }

        try
        {
            var result = await engine.RecognizeAsync(input);
            cancellationToken.ThrowIfCancellationRequested();
            return result.Text?.Trim() ?? string.Empty;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private static Task<string> RecognizeWithMatAsync(
        SoftwareBitmap bitmap,
        Func<Mat, string> recognizer,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mat = ToBgrMat(bitmap);
            cancellationToken.ThrowIfCancellationRequested();
            return recognizer(mat);
        }, cancellationToken);

    private static Mat ToBgrMat(SoftwareBitmap bitmap)
    {
        SoftwareBitmap? converted = null;
        var input = bitmap;
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            input = converted;
        }

        try
        {
            var width = input.PixelWidth;
            var height = input.PixelHeight;
            var pixels = new byte[width * height * 4];
            input.CopyToBuffer(pixels.AsBuffer());

            using var bgra = new Mat(height, width, MatType.CV_8UC4);
            Marshal.Copy(pixels, 0, bgra.Data, pixels.Length);

            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private static OcrEngine? CreateWindowsOcrEngine()
    {
        var preferredLanguages = new[]
        {
            new Windows.Globalization.Language("zh-Hans-CN"),
            new Windows.Globalization.Language("zh-CN"),
            new Windows.Globalization.Language("en-US"),
        };

        foreach (var language in preferredLanguages)
        {
            if (OcrEngine.IsLanguageSupported(language))
            {
                return OcrEngine.TryCreateFromLanguage(language);
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }
}
