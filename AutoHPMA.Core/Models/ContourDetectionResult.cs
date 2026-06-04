namespace AutoHPMA.Core.Models;

public sealed class ContourDetectionResult
{
    public ContourDetectionResult(
        ContourDetectionRectangle? rectangle,
        byte[] binarizedImageBytes,
        byte[] annotatedImageBytes)
    {
        Rectangle = rectangle;
        BinarizedImageBytes = binarizedImageBytes;
        AnnotatedImageBytes = annotatedImageBytes;
    }

    public ContourDetectionRectangle? Rectangle { get; }

    public byte[] BinarizedImageBytes { get; }

    public byte[] AnnotatedImageBytes { get; }
}
