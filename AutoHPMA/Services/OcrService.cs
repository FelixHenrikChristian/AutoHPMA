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

    public async Task<string> RecognizeAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mat);
        if (mat.Empty())
        {
            return string.Empty;
        }

        return engineType switch
        {
            OcrEngineType.WindowsOCR => await RecognizeWithWindowsOcrAsync(mat, cancellationToken),
            OcrEngineType.PaddleOCR => await RecognizeWithMatAsync(mat, static input => PaddleOCRHelper.Instance.Ocr(input), cancellationToken),
            OcrEngineType.RapidOCR => await RecognizeWithMatAsync(mat, static input => RapidOCRHelper.Instance.Ocr(input), cancellationToken),
            OcrEngineType.TesseractOCR => await RecognizeWithMatAsync(mat, static input => TesseractOCRHelper.Instance.Ocr(input), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(engineType), engineType, "不支持的 OCR 引擎。"),
        };
    }

    public async Task<IReadOnlyList<OcrTextRegion>> RecognizeRegionsAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mat);
        if (mat.Empty())
        {
            return Array.Empty<OcrTextRegion>();
        }

        return engineType switch
        {
            OcrEngineType.WindowsOCR => await RecognizeRegionsWithWindowsOcrAsync(mat, cancellationToken),
            OcrEngineType.PaddleOCR => await RecognizeRegionsWithMatAsync(mat, static input => PaddleOCRHelper.Instance.OcrRegions(input), cancellationToken),
            OcrEngineType.RapidOCR or OcrEngineType.TesseractOCR => await RecognizeWholeTextRegionAsync(mat, engineType, cancellationToken),
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

    private async Task<string> RecognizeWithWindowsOcrAsync(Mat mat, CancellationToken cancellationToken)
    {
        using var bitmap = ToSoftwareBitmap(mat);
        return await RecognizeWithWindowsOcrAsync(bitmap, cancellationToken);
    }

    private async Task<IReadOnlyList<OcrTextRegion>> RecognizeRegionsWithWindowsOcrAsync(
        SoftwareBitmap bitmap,
        CancellationToken cancellationToken)
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
            return result.Lines
                .SelectMany(line => line.Words)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new OcrTextRegion(
                    word.Text,
                    ToOpenCvRect(word.BoundingRect, input.PixelWidth, input.PixelHeight)))
                .ToArray();
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private async Task<IReadOnlyList<OcrTextRegion>> RecognizeRegionsWithWindowsOcrAsync(
        Mat mat,
        CancellationToken cancellationToken)
    {
        using var bitmap = ToSoftwareBitmap(mat);
        return await RecognizeRegionsWithWindowsOcrAsync(bitmap, cancellationToken);
    }

    private async Task<IReadOnlyList<OcrTextRegion>> RecognizeWholeTextRegionAsync(
        Mat mat,
        OcrEngineType engineType,
        CancellationToken cancellationToken)
    {
        var text = await RecognizeAsync(mat, engineType, cancellationToken);
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<OcrTextRegion>()
            : [new OcrTextRegion(text.Trim(), new Rect(0, 0, mat.Width, mat.Height))];
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

    private static Task<string> RecognizeWithMatAsync(
        Mat source,
        Func<Mat, string> recognizer,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mat = ToBgrMat(source);
            cancellationToken.ThrowIfCancellationRequested();
            return recognizer(mat);
        }, cancellationToken);

    private static Task<IReadOnlyList<OcrTextRegion>> RecognizeRegionsWithMatAsync(
        Mat source,
        Func<Mat, IReadOnlyList<OcrTextRegion>> recognizer,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mat = ToBgrMat(source);
            cancellationToken.ThrowIfCancellationRequested();
            return recognizer(mat);
        }, cancellationToken);

    private static Rect ToOpenCvRect(
        Windows.Foundation.Rect rect,
        int imageWidth,
        int imageHeight)
    {
        var x = Math.Clamp((int)Math.Floor(rect.X), 0, Math.Max(imageWidth - 1, 0));
        var y = Math.Clamp((int)Math.Floor(rect.Y), 0, Math.Max(imageHeight - 1, 0));
        var right = Math.Clamp((int)Math.Ceiling(rect.X + rect.Width), x + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Y + rect.Height), y + 1, imageHeight);
        return new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static SoftwareBitmap ToSoftwareBitmap(Mat mat)
    {
        using var bgra = ToBgraMat(mat);
        var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            bgra.Width,
            bgra.Height,
            BitmapAlphaMode.Premultiplied);

        bitmap.CopyFromBuffer(CopyBgraPixels(bgra).AsBuffer());
        return bitmap;
    }

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

    private static Mat ToBgrMat(Mat mat)
    {
        return mat.Channels() switch
        {
            1 => ConvertMat(mat, ColorConversionCodes.GRAY2BGR),
            3 => mat.Clone(),
            4 => ConvertMat(mat, ColorConversionCodes.BGRA2BGR),
            _ => throw new ArgumentException("OCR 只支持灰度、BGR 或 BGRA 图像。", nameof(mat)),
        };
    }

    private static Mat ToBgraMat(Mat mat)
    {
        return mat.Channels() switch
        {
            1 => ConvertMat(mat, ColorConversionCodes.GRAY2BGRA),
            3 => ConvertMat(mat, ColorConversionCodes.BGR2BGRA),
            4 => mat.Clone(),
            _ => throw new ArgumentException("OCR 只支持灰度、BGR 或 BGRA 图像。", nameof(mat)),
        };
    }

    private static Mat ConvertMat(Mat source, ColorConversionCodes conversion)
    {
        var converted = new Mat();
        Cv2.CvtColor(source, converted, conversion);
        return converted;
    }

    private static byte[] CopyBgraPixels(Mat bgra)
    {
        var rowBytes = checked(bgra.Width * 4);
        var pixels = new byte[checked(rowBytes * bgra.Height)];
        for (var y = 0; y < bgra.Height; y++)
        {
            Marshal.Copy(bgra.Ptr(y), pixels, y * rowBytes, rowBytes);
        }

        return pixels;
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
