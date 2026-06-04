using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Contracts.Services;

public interface IColorFilterService
{
    ColorFilterResult Filter(ColorFilterRequest request);
}
