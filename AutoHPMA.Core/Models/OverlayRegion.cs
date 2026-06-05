namespace AutoHPMA.Core.Models;

public sealed record OverlayRegion(
    int X,
    int Y,
    int Width,
    int Height,
    string? Name = null,
    string? StatusText = null);
