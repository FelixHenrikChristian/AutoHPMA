using AutoHPMA.Capture;
using AutoHPMA.Capture.Models;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Models;

namespace AutoHPMA.Services.GameWindowProviders;

public sealed class MumuGameWindowProvider : IGameWindowProvider
{
    private const string MumuProcessName = "MuMuNxDevice";
    private const string MumuChildWindowTitle = "MuMuNxDevice";

    public string Name => "MuMu 模拟器";

    public GameClientKind ClientKind => GameClientKind.MumuSimulator;

    public GameWindowTarget? TryLocate()
    {
        var candidates = WindowEnumerator
            .EnumerateVisibleWindows()
            .Where(IsMumuDisplayWindow)
            .Select(window => new
            {
                DisplayWindow = window,
                GameWindow = WindowEnumerator
                    .EnumerateChildWindows(window)
                    .FirstOrDefault(IsMumuGameChildWindow),
            })
            .ToArray();

        var target = candidates.FirstOrDefault(candidate => candidate.GameWindow is not null)
            ?? candidates.FirstOrDefault();

        if (target is null)
        {
            return null;
        }

        return new GameWindowTarget
        {
            ClientKind = ClientKind,
            ProviderName = Name,
            DisplayWindow = target.DisplayWindow,
            GameWindow = target.GameWindow ?? target.DisplayWindow,
        };
    }

    private static bool IsMumuDisplayWindow(WindowInfo window) =>
        string.Equals(window.ProcessName, MumuProcessName, StringComparison.OrdinalIgnoreCase) &&
        (window.Title.Contains("MuMu", StringComparison.OrdinalIgnoreCase) ||
         window.Title.Contains("安卓", StringComparison.OrdinalIgnoreCase) ||
         window.Title.Contains(MumuProcessName, StringComparison.OrdinalIgnoreCase));

    private static bool IsMumuGameChildWindow(WindowInfo window) =>
        string.Equals(window.Title, MumuChildWindowTitle, StringComparison.OrdinalIgnoreCase);
}
