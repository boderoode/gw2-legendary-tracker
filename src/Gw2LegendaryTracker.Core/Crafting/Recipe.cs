namespace Gw2LegendaryTracker.Core.Crafting;

public enum RecipeSource
{
    /// <summary>From the official API (positive ids).</summary>
    Api,

    /// <summary>From locally imported data (negative ids, so they never collide with API ids).</summary>
    Local,
}

/// <summary>How the output is obtained; decides the wording ("Maak", "Koop") and where to do it.</summary>
public enum RecipeKind
{
    /// <summary>Crafted by a crafting discipline.</summary>
    Crafting,

    MysticForge,

    /// <summary>Bought from a vendor, usually with a currency such as karma.</summary>
    Vendor,
}

public sealed record Ingredient(MaterialKey Key, long Count);

/// <param name="RequiresGuildUpgrades">Needs guild upgrades the app cannot track; such recipes are never used in plans.</param>
/// <param name="Note">Extra text for the user, e.g. which vendor sells it and where.</param>
public sealed record Recipe(
    int Id,
    int OutputItemId,
    int OutputCount,
    IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<string> Disciplines,
    RecipeSource Source = RecipeSource.Api,
    bool RequiresGuildUpgrades = false,
    RecipeKind Kind = RecipeKind.Crafting,
    string? Note = null);
