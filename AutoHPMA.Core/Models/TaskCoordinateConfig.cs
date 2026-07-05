namespace AutoHPMA.Core.Models;

public sealed class TaskCoordinateConfig
{
    public int CanonicalWidth { get; set; }

    public int CanonicalHeight { get; set; }

    public List<TaskCoordinatePointDefinition> Points { get; set; } = [];

    public List<TaskCoordinateRegionDefinition> Regions { get; set; } = [];
}

public sealed class TaskCoordinatePointDefinition
{
    public string Id { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }
}

public sealed class TaskCoordinateRegionDefinition
{
    public string Id { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public readonly record struct TaskCoordinatePoint(int X, int Y);

public readonly record struct TaskCoordinateRegion(int X, int Y, int Width, int Height);

public static class TaskCoordinateIds
{
    public const string ClubQuizProgress = "club-quiz.progress";
    public const string ChatChannels = "chat.channels";
    public const string CookingNextOrder = "cooking.next-order";
}
