using OpenCvSharp;

namespace AutoHPMA.Core.Models;

public sealed class TemplateSearchOptions
{
    public Mat? Mask { get; init; }

    public bool UseAlphaMask { get; init; }

    public Mat? SourceMask { get; init; }

    public bool FindMultiple { get; init; }

    public double Threshold { get; init; } = 0.9;

    public TemplateMatchModes MatchMode { get; init; } = TemplateMatchModes.CCoeffNormed;

    public int? MaxCount { get; init; }
}
