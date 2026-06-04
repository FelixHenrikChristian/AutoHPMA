using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using OpenCvSharp;

namespace AutoHPMA.Core.Services;

public sealed class TemplateMatchingService : ITemplateMatchingService
{
    public TemplateMatchingResult Match(TemplateMatchingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        using var sourceMat = ReadImage(request.SourceImagePath, ImreadModes.Color, "source image");
        using var templateWithAlpha = ReadImage(request.TemplateImagePath, ImreadModes.Unchanged, "template image");
        using var templateMat = ConvertTemplateToBgr(templateWithAlpha);
        using var maskMat = CreateMask(request.MaskImagePath, templateWithAlpha, templateMat.Width, templateMat.Height);

        ValidateTemplateSize(sourceMat, templateMat);

        var maxCount = request.MaxCount ?? CalculateDefaultMaxCount(sourceMat, templateMat);
        var regions = MatchRegions(sourceMat, templateMat, request.MatchMode, request.Threshold, maxCount, maskMat);

        using var annotated = sourceMat.Clone();
        foreach (var region in regions)
        {
            Cv2.Rectangle(
                annotated,
                new Rect(region.X, region.Y, region.Width, region.Height),
                Scalar.Red,
                2);
        }

        return new TemplateMatchingResult(
            regions,
            EncodePng(annotated),
            maskMat is null || maskMat.Empty() ? null : EncodePng(maskMat));
    }

    public IReadOnlyList<string> CropMatches(string sourceImagePath, IReadOnlyList<TemplateMatchRegion> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);
        ArgumentNullException.ThrowIfNull(regions);

        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Source image was not found.", sourceImagePath);
        }

        using var sourceMat = ReadImage(sourceImagePath, ImreadModes.Color, "source image");
        var directory = Path.GetDirectoryName(sourceImagePath)
            ?? throw new InvalidOperationException("Source image has no parent directory.");
        var fileName = Path.GetFileNameWithoutExtension(sourceImagePath);
        var extension = Path.GetExtension(sourceImagePath);
        var savedFiles = new List<string>(regions.Count);

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var rect = new Rect(region.X, region.Y, region.Width, region.Height);
            ValidateCropRect(sourceMat, rect);

            using var cropped = new Mat(sourceMat, rect);
            var outputPath = Path.Combine(directory, $"{fileName}_cropped_{i + 1}{extension}");
            cropped.SaveImage(outputPath);
            savedFiles.Add(outputPath);
        }

        return savedFiles;
    }

    private static void ValidateRequest(TemplateMatchingRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateImagePath);

        if (!File.Exists(request.SourceImagePath))
        {
            throw new FileNotFoundException("Source image was not found.", request.SourceImagePath);
        }

        if (!File.Exists(request.TemplateImagePath))
        {
            throw new FileNotFoundException("Template image was not found.", request.TemplateImagePath);
        }

        if (!string.IsNullOrWhiteSpace(request.MaskImagePath) && !File.Exists(request.MaskImagePath))
        {
            throw new FileNotFoundException("Mask image was not found.", request.MaskImagePath);
        }

        if (request.Threshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Threshold), "Threshold must be between 0 and 1.");
        }

        if (request.MaxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaxCount), "Max count must be greater than zero.");
        }
    }

    private static Mat ReadImage(string path, ImreadModes mode, string name)
    {
        var mat = Cv2.ImRead(path, mode);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException($"Unable to read {name}: {path}");
        }

        return mat;
    }

    private static Mat ConvertTemplateToBgr(Mat templateWithAlpha)
    {
        if (templateWithAlpha.Channels() == 4)
        {
            var templateMat = new Mat();
            Cv2.CvtColor(templateWithAlpha, templateMat, ColorConversionCodes.BGRA2BGR);
            return templateMat;
        }

        if (templateWithAlpha.Channels() == 1)
        {
            var templateMat = new Mat();
            Cv2.CvtColor(templateWithAlpha, templateMat, ColorConversionCodes.GRAY2BGR);
            return templateMat;
        }

        return templateWithAlpha.Clone();
    }

    private static Mat? CreateMask(string? maskImagePath, Mat templateWithAlpha, int templateWidth, int templateHeight)
    {
        if (!string.IsNullOrWhiteSpace(maskImagePath))
        {
            var mask = ReadImage(maskImagePath, ImreadModes.Grayscale, "mask image");
            return EnsureMaskSize(mask, templateWidth, templateHeight);
        }

        return templateWithAlpha.Channels() == 4
            ? GenerateAlphaMask(templateWithAlpha)
            : null;
    }

    private static Mat EnsureMaskSize(Mat mask, int templateWidth, int templateHeight)
    {
        if (mask.Width == templateWidth && mask.Height == templateHeight)
        {
            return mask;
        }

        using (mask)
        {
            var resized = new Mat();
            Cv2.Resize(mask, resized, new Size(templateWidth, templateHeight), 0, 0, InterpolationFlags.Nearest);
            return resized;
        }
    }

    private static Mat GenerateAlphaMask(Mat templateWithAlpha)
    {
        Cv2.Split(templateWithAlpha, out var channels);
        try
        {
            var mask = new Mat();
            Cv2.Threshold(channels[3], mask, 0, 255, ThresholdTypes.Binary);
            return mask;
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static void ValidateTemplateSize(Mat sourceMat, Mat templateMat)
    {
        if (templateMat.Width > sourceMat.Width || templateMat.Height > sourceMat.Height)
        {
            throw new InvalidOperationException("Template image cannot be larger than source image.");
        }
    }

    private static int CalculateDefaultMaxCount(Mat sourceMat, Mat templateMat)
    {
        var sourceArea = Math.Max(sourceMat.Width * sourceMat.Height, 1);
        var templateArea = Math.Max(templateMat.Width * templateMat.Height, 1);
        return Math.Max(sourceArea / templateArea, 1);
    }

    private static IReadOnlyList<TemplateMatchRegion> MatchRegions(
        Mat sourceMat,
        Mat templateMat,
        TemplateMatchModes matchMode,
        double threshold,
        int maxCount,
        Mat? maskMat)
    {
        using var result = new Mat();
        if (maskMat is null)
        {
            Cv2.MatchTemplate(sourceMat, templateMat, result, matchMode);
        }
        else
        {
            Cv2.MatchTemplate(sourceMat, templateMat, result, matchMode, maskMat);
        }

        if (result.Depth() == MatType.CV_32F)
        {
            Cv2.PatchNaNs(result, 0);
        }

        if (matchMode is TemplateMatchModes.SqDiff or TemplateMatchModes.CCoeff or TemplateMatchModes.CCorr)
        {
            Cv2.Normalize(result, result, 0, 1, NormTypes.MinMax);
        }

        using var searchMask = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.White);
        var regions = new List<TemplateMatchRegion>();

        while (regions.Count < maxCount && Cv2.CountNonZero(searchMask) > 0)
        {
            Cv2.MinMaxLoc(result, out var minValue, out var maxValue, out var minLoc, out var maxLoc, searchMask);

            var isSqDiff = matchMode is TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed;
            var accepted = isSqDiff
                ? minValue <= 1 - threshold
                : maxValue >= threshold;

            if (!accepted)
            {
                break;
            }

            var location = isSqDiff ? minLoc : maxLoc;
            regions.Add(new TemplateMatchRegion(location.X, location.Y, templateMat.Width, templateMat.Height));

            var suppressed = ClampRectToMat(
                new Rect(location.X, location.Y, templateMat.Width, templateMat.Height),
                searchMask);
            Cv2.Rectangle(searchMask, suppressed, Scalar.Black, -1);
        }

        return regions;
    }

    private static Rect ClampRectToMat(Rect rect, Mat mat)
    {
        var x = Math.Clamp(rect.X, 0, mat.Width);
        var y = Math.Clamp(rect.Y, 0, mat.Height);
        var right = Math.Clamp(rect.X + rect.Width, 0, mat.Width);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, mat.Height);
        return new Rect(x, y, Math.Max(right - x, 1), Math.Max(bottom - y, 1));
    }

    private static byte[] EncodePng(Mat mat)
    {
        Cv2.ImEncode(".png", mat, out var bytes);
        return bytes;
    }

    private static void ValidateCropRect(Mat sourceMat, Rect rect)
    {
        if (rect.X < 0 || rect.Y < 0 ||
            rect.Width <= 0 || rect.Height <= 0 ||
            rect.X + rect.Width > sourceMat.Width ||
            rect.Y + rect.Height > sourceMat.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Match region is outside the source image.");
        }
    }
}
