using AutoHPMA.Models;

namespace AutoHPMA.Contracts.Services;

public interface IGameWindowProvider
{
    string Name { get; }

    GameClientKind ClientKind { get; }

    GameWindowTarget? TryLocate();
}
