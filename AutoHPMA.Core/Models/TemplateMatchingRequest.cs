using OpenCvSharp;

namespace AutoHPMA.Core.Models;

public sealed class TemplateMatchingRequest
{
    public required string SourceImagePath { get; init; }

    public required string TemplateImagePath { get; init; }

    public string? MaskImagePath { get; init; }

    public TemplateMatchModes MatchMode { get; init; } = TemplateMatchModes.CCoeffNormed;

    public double Threshold { get; init; } = 0.8;

    public int? MaxCount { get; init; }
}
