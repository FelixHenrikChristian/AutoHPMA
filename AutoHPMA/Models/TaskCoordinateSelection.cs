namespace AutoHPMA.Models;

public enum TaskCoordinateSelectionMode
{
    Region,
    Point,
}

public readonly record struct TaskCoordinateSelection(
    int X,
    int Y,
    int Width,
    int Height);
