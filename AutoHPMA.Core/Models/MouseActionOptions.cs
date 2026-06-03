namespace AutoHPMA.Core.Models;

public enum MouseActionType
{
    Click,
    Drag,
    LongPress,
}

/// <summary>
/// Describes one simulated mouse action in target-window client coordinates.
/// </summary>
public sealed class MouseActionOptions
{
    public MouseActionType ActionType { get; init; } = MouseActionType.Click;

    public int X { get; init; } = 200;

    public int Y { get; init; } = 200;

    public int EndX { get; init; } = 400;

    public int EndY { get; init; } = 400;

    public int DurationMilliseconds { get; init; } = 500;

    public int RepeatIntervalMilliseconds { get; init; } = 500;

    public int RepeatCount { get; init; } = 1;
}
