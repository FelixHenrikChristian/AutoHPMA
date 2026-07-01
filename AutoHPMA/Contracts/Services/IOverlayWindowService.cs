using AutoHPMA.Core.Models;
using AutoHPMA.Models;

namespace AutoHPMA.Contracts.Services;

public interface IOverlayWindowService : IDisposable
{
    void Start(GameWindowTarget target, AutomationRuntimeOptions options);

    void Refresh(GameWindowTarget target);

    void Stop();

    void SetGameState(string state);

    void AddTemporaryRegion(OverlayRegion region, int durationMs = 1000);

    void AddTemporaryRegions(IReadOnlyList<OverlayRegion> regions, int durationMs = 1000);

    void SetStateIndicatorRegions(IReadOnlyList<OverlayRegion> regions);

    void ClearStateIndicatorRegions();

    void SetTaskStateRegions(IReadOnlyList<OverlayRegion> regions);

    void ClearTaskStateRegions();

    void ClearMask();
}
