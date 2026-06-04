using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using OpenCvSharp;

namespace AutoHPMA.Core.Services;

public sealed class ColorFilterService : IColorFilterService
{
    public ColorFilterResult Filter(ColorFilterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        using var sourceMat = ReadImage(request.SourceImagePath);
        using var areaMask = ReadMask(request.MaskImagePath, sourceMat.Size());
        using var colorMask = CreateColorMask(sourceMat, request);

        var totalFilterPixels = areaMask is null
            ? sourceMat.Rows * sourceMat.Cols
            : Cv2.CountNonZero(areaMask);

        if (areaMask is not null)
        {
            Cv2.BitwiseAnd(colorMask, areaMask, colorMask);
        }

        var matchedPixels = Cv2.CountNonZero(colorMask);
        using var resultMat = new Mat(sourceMat.Size(), sourceMat.Type(), Scalar.Black);
        Cv2.BitwiseAnd(sourceMat, sourceMat, resultMat, colorMask);

        return new ColorFilterResult(
            EncodePng(resultMat),
            totalFilterPixels,
            matchedPixels);
    }

    private static void ValidateRequest(ColorFilterRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceImagePath);

        if (!File.Exists(request.SourceImagePath))
        {
            throw new FileNotFoundException("Source image was not found.", request.SourceImagePath);
        }

        if (!string.IsNullOrWhiteSpace(request.MaskImagePath) && !File.Exists(request.MaskImagePath))
        {
            throw new FileNotFoundException("Mask image was not found.", request.MaskImagePath);
        }

        if (!TryParseHexColor(request.TargetColorHex, out _, out _, out _))
        {
            throw new ArgumentException("Target color must be a 6-digit hex value.", nameof(request.TargetColorHex));
        }

        if (request.HueThreshold is < 0 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(request.HueThreshold), "Hue threshold must be between 0 and 90.");
        }

        if (request.SaturationTolerance is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SaturationTolerance), "Saturation tolerance must be between 0 and 255.");
        }

        if (request.ValueTolerance is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ValueTolerance), "Value tolerance must be between 0 and 255.");
        }
    }

    private static Mat ReadImage(string path)
    {
        var mat = Cv2.ImRead(path, ImreadModes.Color);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException($"Unable to read image: {path}");
        }

        return mat;
    }

    private static Mat? ReadMask(string? path, Size sourceSize)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        using var maskMat = ReadImage(path);
        if (maskMat.Size() != sourceSize)
        {
            throw new InvalidOperationException("Mask image size must match source image size.");
        }

        var grayMask = new Mat();
        Cv2.CvtColor(maskMat, grayMask, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(grayMask, grayMask, 127, 255, ThresholdTypes.Binary);
        return grayMask;
    }

    private static Mat CreateColorMask(Mat sourceMat, ColorFilterRequest request)
    {
        var (red, green, blue) = ParseHexColor(request.TargetColorHex);

        return request.ColorSpace == ColorFilterColorSpace.LAB
            ? CreateLabMask(sourceMat, red, green, blue, request)
            : CreateHsvMask(sourceMat, red, green, blue, request);
    }

    private static Mat CreateLabMask(Mat sourceMat, byte red, byte green, byte blue, ColorFilterRequest request)
    {
        using var targetBgr = new Mat(1, 1, MatType.CV_8UC3, new Scalar(blue, green, red));
        using var targetLab = new Mat();
        Cv2.CvtColor(targetBgr, targetLab, ColorConversionCodes.BGR2Lab);
        var targetLabValue = targetLab.Get<Vec3b>(0, 0);

        using var labMat = new Mat();
        Cv2.CvtColor(sourceMat, labMat, ColorConversionCodes.BGR2Lab);

        var lLow = Math.Max(0, targetLabValue.Item0 - request.ValueTolerance);
        var lHigh = Math.Min(255, targetLabValue.Item0 + request.ValueTolerance);
        var aLow = Math.Max(0, targetLabValue.Item1 - request.SaturationTolerance);
        var aHigh = Math.Min(255, targetLabValue.Item1 + request.SaturationTolerance);
        var bLow = Math.Max(0, targetLabValue.Item2 - request.HueThreshold * 3);
        var bHigh = Math.Min(255, targetLabValue.Item2 + request.HueThreshold * 3);

        var mask = new Mat();
        Cv2.InRange(
            labMat,
            new Scalar(lLow, aLow, bLow),
            new Scalar(lHigh, aHigh, bHigh),
            mask);
        return mask;
    }

    private static Mat CreateHsvMask(Mat sourceMat, byte red, byte green, byte blue, ColorFilterRequest request)
    {
        using var targetBgr = new Mat(1, 1, MatType.CV_8UC3, new Scalar(blue, green, red));
        using var targetHsv = new Mat();
        Cv2.CvtColor(targetBgr, targetHsv, ColorConversionCodes.BGR2HSV);
        var targetHsvValue = targetHsv.Get<Vec3b>(0, 0);

        using var hsvMat = new Mat();
        Cv2.CvtColor(sourceMat, hsvMat, ColorConversionCodes.BGR2HSV);

        var hLow = targetHsvValue.Item0 - request.HueThreshold;
        var hHigh = targetHsvValue.Item0 + request.HueThreshold;
        var sLow = Math.Max(0, targetHsvValue.Item1 - request.SaturationTolerance);
        var vLow = Math.Max(0, targetHsvValue.Item2 - request.ValueTolerance);

        var mask = new Mat();
        if (hLow < 0)
        {
            using var mask1 = new Mat();
            using var mask2 = new Mat();
            Cv2.InRange(hsvMat, new Scalar(0, sLow, vLow), new Scalar(hHigh, 255, 255), mask1);
            Cv2.InRange(hsvMat, new Scalar(180 + hLow, sLow, vLow), new Scalar(180, 255, 255), mask2);
            Cv2.BitwiseOr(mask1, mask2, mask);
        }
        else if (hHigh > 180)
        {
            using var mask1 = new Mat();
            using var mask2 = new Mat();
            Cv2.InRange(hsvMat, new Scalar(hLow, sLow, vLow), new Scalar(180, 255, 255), mask1);
            Cv2.InRange(hsvMat, new Scalar(0, sLow, vLow), new Scalar(hHigh - 180, 255, 255), mask2);
            Cv2.BitwiseOr(mask1, mask2, mask);
        }
        else
        {
            Cv2.InRange(hsvMat, new Scalar(hLow, sLow, vLow), new Scalar(hHigh, 255, 255), mask);
        }

        return mask;
    }

    private static (byte Red, byte Green, byte Blue) ParseHexColor(string hex)
    {
        if (!TryParseHexColor(hex, out var red, out var green, out var blue))
        {
            throw new ArgumentException("Target color must be a 6-digit hex value.", nameof(hex));
        }

        return (red, green, blue);
    }

    private static bool TryParseHexColor(string? hex, out byte red, out byte green, out byte blue)
    {
        red = 0;
        green = 0;
        blue = 0;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6)
        {
            return false;
        }

        return byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out red) &&
               byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out green) &&
               byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out blue);
    }

    private static byte[] EncodePng(Mat mat)
    {
        Cv2.ImEncode(".png", mat, out var bytes);
        return bytes;
    }
}
