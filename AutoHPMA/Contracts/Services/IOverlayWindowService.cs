using AutoHPMA.Models;
using OpenCvSharp;

namespace AutoHPMA.Contracts.Services;

public interface IOverlayWindowService : IDisposable
{
    void Start(GameWindowTarget target, AutomationRuntimeOptions options);

    void Refresh(GameWindowTarget target);

    void Stop();

    void SetGameState(string state);

    void AddTemporaryRect(Rect rect, string? text = null, int durationMs = 500);

    void AddTemporaryRects(IReadOnlyList<Rect> rects, IReadOnlyDictionary<Rect, string>? textContents = null, int durationMs = 500);

    void SetStateIndicatorRects(IReadOnlyList<Rect> rects);

    void ClearStateIndicatorRects();

    void SetTaskStateRects(IReadOnlyList<Rect> rects, IReadOnlyDictionary<Rect, string>? textContents = null);

    void ClearTaskStateRects();

    void ClearMask();
}
