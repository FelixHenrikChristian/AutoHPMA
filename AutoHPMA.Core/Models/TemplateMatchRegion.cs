namespace AutoHPMA.Core.Models;

public readonly record struct TemplateMatchRegion(
    int X,
    int Y,
    int Width,
    int Height,
    double? Score = null);
