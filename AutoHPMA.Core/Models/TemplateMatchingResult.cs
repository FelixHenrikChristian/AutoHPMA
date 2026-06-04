namespace AutoHPMA.Core.Models;

public sealed class TemplateMatchingResult
{
    public TemplateMatchingResult(
        IReadOnlyList<TemplateMatchRegion> regions,
        byte[] annotatedImageBytes,
        byte[]? maskImageBytes)
    {
        Regions = regions;
        AnnotatedImageBytes = annotatedImageBytes;
        MaskImageBytes = maskImageBytes;
    }

    public IReadOnlyList<TemplateMatchRegion> Regions { get; }

    public byte[] AnnotatedImageBytes { get; }

    public byte[]? MaskImageBytes { get; }
}
