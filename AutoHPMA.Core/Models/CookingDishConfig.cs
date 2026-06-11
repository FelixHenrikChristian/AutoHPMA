namespace AutoHPMA.Core.Models;

public sealed class CookingDishConfig
{
    public string Name { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public List<string> RequiredKitchenware { get; set; } = [];

    public List<string> RequiredIngredients { get; set; } = [];

    public List<string> RequiredCondiments { get; set; } = [];

    public List<CookingStepConfig> CookingSteps { get; set; } = [];

    public Dictionary<string, int> CondimentPositions { get; set; } = [];
}

public sealed class CookingStepConfig
{
    public string Ingredient { get; set; } = string.Empty;

    public string TargetKitchenware { get; set; } = string.Empty;
}
