using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Contracts.Services;

public interface ITemplateMatchingService
{
    TemplateMatchingResult Match(TemplateMatchingRequest request);

    IReadOnlyList<string> CropMatches(string sourceImagePath, IReadOnlyList<TemplateMatchRegion> regions);
}
