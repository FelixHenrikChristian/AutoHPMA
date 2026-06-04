using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using OpenCvSharp;

namespace AutoHPMA.Core.Services;

public sealed class ContourDetectionService : IContourDetectionService
{
    public byte[] Binarize(ContourDetectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        using var sourceMat = ReadImage(request.ImagePath);
        using var binaryMat = Binarize(sourceMat, request.Threshold);
        return EncodePng(binaryMat);
    }

    public ContourDetectionResult DetectApproxRectangle(ContourDetectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        using var sourceMat = ReadImage(request.ImagePath);
        using var binaryMat = Binarize(sourceMat, request.Threshold);
        var rect = DetectApproxRectangle(binaryMat, request.MinimumArea, request.ApproximationEpsilon);

        using var annotated = sourceMat.Clone();
        ContourDetectionRectangle? rectangle = null;
        if (rect.Width > 0 && rect.Height > 0)
        {
            Cv2.Rectangle(annotated, rect, Scalar.Red, 2);
            rectangle = new ContourDetectionRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        return new ContourDetectionResult(
            rectangle,
            EncodePng(binaryMat),
            EncodePng(annotated));
    }

    private static void ValidateRequest(ContourDetectionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImagePath);

        if (!File.Exists(request.ImagePath))
        {
            throw new FileNotFoundException("Image was not found.", request.ImagePath);
        }

        if (request.Threshold is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Threshold), "Threshold must be between 0 and 255.");
        }

        if (request.MinimumArea < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MinimumArea), "Minimum area cannot be negative.");
        }

        if (request.ApproximationEpsilon <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ApproximationEpsilon), "Approximation epsilon must be greater than zero.");
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

    private static Mat Binarize(Mat sourceMat, double threshold)
    {
        using var gray = new Mat();
        var binary = new Mat();
        Cv2.CvtColor(sourceMat, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary);
        return binary;
    }

    private static Rect DetectApproxRectangle(Mat binaryMat, double minimumArea, double approximationEpsilon)
    {
        if (binaryMat.Channels() != 1)
        {
            throw new ArgumentException("Input image must be binary (1-channel).", nameof(binaryMat));
        }

        using var contourSource = binaryMat.Clone();
        Cv2.FindContours(
            contourSource,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var bestRect = default(Rect);
        var maxArea = 0d;

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < minimumArea)
            {
                continue;
            }

            var approx = Cv2.ApproxPolyDP(contour, approximationEpsilon, true);
            var boundingRect = Cv2.BoundingRect(approx);
            if (area <= maxArea)
            {
                continue;
            }

            maxArea = area;
            bestRect = boundingRect;
        }

        return bestRect;
    }

    private static byte[] EncodePng(Mat mat)
    {
        Cv2.ImEncode(".png", mat, out var bytes);
        return bytes;
    }
}
