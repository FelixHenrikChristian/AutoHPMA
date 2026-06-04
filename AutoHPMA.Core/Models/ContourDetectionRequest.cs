namespace AutoHPMA.Core.Models;

public sealed class ContourDetectionRequest
{
    public required string ImagePath { get; init; }

    public double Threshold { get; init; } = 200;

    public double MinimumArea { get; init; } = 1000;

    public double ApproximationEpsilon { get; init; } = 5;
}
