using AutoHPMA.Core.Models;
using OpenCvSharp;

namespace AutoHPMA.Tasks;

public sealed record AutomationTaskStateRule<TState>(
    IReadOnlyList<Mat> Templates,
    TState State,
    string DisplayName,
    double Threshold = 0.9,
    TemplateSearchOptions? SearchOptions = null);
