namespace AutoHPMA.Core.Models;

public sealed class TemplateSearchResult
{
    public TemplateSearchResult(IReadOnlyList<TemplateMatchRegion> regions)
    {
        Regions = regions;
    }

    public IReadOnlyList<TemplateMatchRegion> Regions { get; }

    public bool Success => Regions.Count > 0;

    public TemplateMatchRegion? FirstRegion => Success ? Regions[0] : null;

    public static TemplateSearchResult Failed { get; } = new([]);
}
