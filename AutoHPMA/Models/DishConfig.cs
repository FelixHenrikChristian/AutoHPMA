using System.Collections.Generic;

namespace AutoHPMA.Models;

public class DishConfig
{
    public string Name { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public List<string> RequiredKitchenware { get; set; } = new();
    public List<string> RequiredIngredients { get; set; } = new();
    public List<string> RequiredCondiments { get; set; } = new();
    public List<CookingStep> CookingSteps { get; set; } = new();
    public Dictionary<string, int> CondimentPositions { get; set; } = new();
}

public class CookingStep
{
    public string Ingredient { get; set; } = string.Empty;
    public string TargetKitchenware { get; set; } = string.Empty;
}