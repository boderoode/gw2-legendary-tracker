using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Crafting.Forge;

/// <summary>A Mystic Forge recipe as reported by gw2treasures.</summary>
public sealed record ForgeRecipe(int OutputItemId, int OutputMin, int OutputMax, IReadOnlyList<Ingredient> Ingredients)
{
    /// <summary>A random outcome is a lottery, not a way to make something.</summary>
    public bool IsRandom => OutputMin != OutputMax;
}

/// <summary>Access to <c>items/:id/mystic-forge</c> on api.gw2treasures.com. Implementations throw <see cref="Gw2ApiException"/>.</summary>
public interface IForgeApiClient
{
    /// <summary>All forge recipes that make the item; empty when there are none or the item is unknown.</summary>
    Task<IReadOnlyList<ForgeRecipe>> GetRecipesAsync(int itemId, string apiKey, CancellationToken cancellationToken = default);
}

/// <param name="IsVerified">The ingredients and quantities match the official item description.</param>
public sealed record ForgeRecipeDraft(int OutputCount, IReadOnlyList<Ingredient> Ingredients, bool IsVerified);

/// <summary>Cached forge recipes. Ids of the recipes it returns are negative and start below <see cref="IdBase"/>.</summary>
public sealed record ForgeRecipeSet(IReadOnlyList<Recipe> Recipes, IReadOnlySet<int> SingleSourceRecipeIds);

public interface IForgeRecipeStore
{
    /// <summary>Recipe ids handed out by the store are <c>-(IdBase + n)</c>, so they never meet API (positive) or file (small negative) ids.</summary>
    public const int IdBase = 1_000_000;

    /// <summary>When the item was last looked up (also when it had no recipe), or <c>null</c> if never.</summary>
    Task<DateTimeOffset?> GetFetchedUtcAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>Replaces what is stored for the item, atomically, and remembers when it was looked up.</summary>
    Task SaveAsync(int itemId, IReadOnlyList<ForgeRecipeDraft> recipes, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recipe>> GetRecipesForAsync(int itemId, CancellationToken cancellationToken = default);

    Task<ForgeRecipeSet> LoadAllAsync(CancellationToken cancellationToken = default);
}
