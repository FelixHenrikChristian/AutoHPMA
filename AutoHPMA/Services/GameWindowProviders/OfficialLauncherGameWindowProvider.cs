using AutoHPMA.Capture;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Models;

namespace AutoHPMA.Services.GameWindowProviders;

public sealed class OfficialLauncherGameWindowProvider : IGameWindowProvider
{
    private static readonly string[] ProcessNameCandidates =
    [
        "Harry Potter Magic Awakened",
    ];

    private static readonly string[] TitleCandidates =
    [
        "哈利波特：魔法觉醒",
        "Harry Potter Magic Awakened",
    ];

    public string Name => "官方启动器";

    public GameClientKind ClientKind => GameClientKind.OfficialLauncher;

    public GameWindowTarget? TryLocate()
    {
        var gameWindow = WindowEnumerator
            .EnumerateVisibleWindows()
            .FirstOrDefault(window =>
                MatchesAny(window.ProcessName, ProcessNameCandidates) ||
                MatchesAny(window.Title, TitleCandidates));

        if (gameWindow is null)
        {
            return null;
        }

        return new GameWindowTarget
        {
            ClientKind = ClientKind,
            ProviderName = Name,
            DisplayWindow = gameWindow,
            GameWindow = gameWindow,
        };
    }

    private static bool MatchesAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
