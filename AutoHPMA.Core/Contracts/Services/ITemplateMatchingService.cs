using AutoHPMA.Core.Models;
using OpenCvSharp;

namespace AutoHPMA.Core.Contracts.Services;

public interface ITemplateMatchingService
{
    TemplateMatchingResult Match(TemplateMatchingRequest request);

    TemplateSearchResult Search(
        Mat sourceMat,
        Mat templateMat,
        TemplateSearchOptions? options = null);

    IReadOnlyList<string> CropMatches(string sourceImagePath, IReadOnlyList<TemplateMatchRegion> regions);
}
