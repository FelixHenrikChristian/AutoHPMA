namespace AutoHPMA.Core.Models;

public enum OverlayRegionStatusKind
{
    Inline,
    Detail,
}

public enum OverlayRegionKind
{
    Default,
    TemplateMatch,
    Ocr,
}

public sealed record OverlayRegion(
    int X,
    int Y,
    int Width,
    int Height,
    string? Name = null,
    string? StatusText = null,
    OverlayRegionStatusKind StatusKind = OverlayRegionStatusKind.Inline,
    OverlayRegionKind Kind = OverlayRegionKind.Default);
