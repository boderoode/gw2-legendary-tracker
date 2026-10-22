namespace Gw2LegendaryTracker.Core.Crafting;

/// <summary>Immutable, in-memory lookup of recipes by the item they produce.</summary>
public sealed class RecipeBook
{
    public static RecipeBook Empty { get; } = new([]);

    private readonly Dictionary<int, IReadOnlyList<Recipe>> _byOutput;

    public RecipeBook(IEnumerable<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);

        _byOutput = recipes
            .Where(r => !r.RequiresGuildUpgrades && r.OutputCount > 0 && r.Ingredients.Count > 0)
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .GroupBy(r => r.OutputItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Recipe>)g.OrderBy(r => r.Id).ToList());
    }

    public int OutputCount => _byOutput.Count;

    /// <summary>All usable recipes that produce the item, ordered by recipe id (empty when it cannot be crafted).</summary>
    public IReadOnlyList<Recipe> RecipesFor(int outputItemId) =>
        _byOutput.TryGetValue(outputItemId, out var recipes) ? recipes : [];
}
