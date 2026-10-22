namespace Gw2LegendaryTracker.Core.Crafting;

public enum RecipeSource
{
    /// <summary>From the official API (positive ids).</summary>
    Api,

    /// <summary>From locally imported data (negative ids, so they never collide with API ids).</summary>
    Local,
}

public sealed record Ingredient(MaterialKey Key, long Count);

/// <param name="RequiresGuildUpgrades">Needs guild upgrades the app cannot track; such recipes are never used in plans.</param>
public sealed record Recipe(
    int Id,
    int OutputItemId,
    int OutputCount,
    IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<string> Disciplines,
    RecipeSource Source = RecipeSource.Api,
    bool RequiresGuildUpgrades = false);
