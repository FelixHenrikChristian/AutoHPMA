namespace AutoHPMA.Core.Models;

public sealed class ColorFilterResult
{
    public ColorFilterResult(
        byte[] filteredImageBytes,
        int totalFilterPixels,
        int matchedPixels)
    {
        FilteredImageBytes = filteredImageBytes;
        TotalFilterPixels = totalFilterPixels;
        MatchedPixels = matchedPixels;
    }

    public byte[] FilteredImageBytes { get; }

    public int TotalFilterPixels { get; }

    public int MatchedPixels { get; }

    public double MatchPercentage =>
        TotalFilterPixels > 0 ? (double)MatchedPixels / TotalFilterPixels * 100 : 0;
}
