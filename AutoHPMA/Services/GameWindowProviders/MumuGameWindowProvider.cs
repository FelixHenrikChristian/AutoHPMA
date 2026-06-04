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
        var displayWindow = WindowEnumerator
            .EnumerateVisibleWindows()
            .FirstOrDefault(IsMumuDisplayWindow);

        if (displayWindow is null)
        {
            return null;
        }

        var gameWindow = WindowEnumerator
            .EnumerateChildWindows(displayWindow)
            .FirstOrDefault(IsMumuGameChildWindow)
            ?? displayWindow;

        return new GameWindowTarget
        {
            ClientKind = ClientKind,
            ProviderName = Name,
            DisplayWindow = displayWindow,
            GameWindow = gameWindow,
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
