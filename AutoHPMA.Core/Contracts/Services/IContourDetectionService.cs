using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Contracts.Services;

public interface IContourDetectionService
{
    byte[] Binarize(ContourDetectionRequest request);

    ContourDetectionResult DetectApproxRectangle(ContourDetectionRequest request);
}
