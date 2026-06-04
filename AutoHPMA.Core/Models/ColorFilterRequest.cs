namespace AutoHPMA.Core.Models;

public sealed class ColorFilterRequest
{
    public required string SourceImagePath { get; init; }

    public string? MaskImagePath { get; init; }

    public required string TargetColorHex { get; init; }

    public int HueThreshold { get; init; } = 30;

    public int SaturationTolerance { get; init; } = 100;

    public int ValueTolerance { get; init; } = 100;

    public ColorFilterColorSpace ColorSpace { get; init; } = ColorFilterColorSpace.LAB;
}
