namespace AutoHPMA.Core.Models;

public abstract record AutomationTaskOptions;

public sealed record ClubQuizTaskOptions(
    int AnswerDelay,
    bool JoinOthers,
    bool StopWhenContributionFull) : AutomationTaskOptions;

public sealed record ForbiddenForestTaskOptions(
    int Times,
    bool IsLeader) : AutomationTaskOptions;

public sealed record CookingTaskOptions(
    int Times,
    string Dish) : AutomationTaskOptions;

public sealed record SweetAdventureTaskOptions : AutomationTaskOptions;
