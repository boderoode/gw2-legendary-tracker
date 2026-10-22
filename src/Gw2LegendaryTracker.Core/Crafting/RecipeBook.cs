namespace Gw2LegendaryTracker.Core.Crafting;

/// <summary>Immutable, in-memory lookup of recipes by the item they produce.</summary>
public sealed class RecipeBook
{
    public static RecipeBook Empty { get; } = new([]);

    private readonly Dictionary<int, IReadOnlyList<Recipe>> _byOutput;
    private readonly HashSet<int> _ids;

    public RecipeBook(IEnumerable<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);

        // The same recipe can arrive from several sources (e.g. a shipped file and a fresh download).
        // Keeping the one with the lowest id shows each way of making an item once.
        var usable = recipes
            .Where(r => !r.RequiresGuildUpgrades && r.OutputCount > 0 && r.Ingredients.Count > 0)
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .OrderBy(r => r.Id)
            .DistinctBy(Signature)
            .ToList();

        _ids = [.. usable.Select(r => r.Id)];
        _byOutput = usable
            .GroupBy(r => r.OutputItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Recipe>)g.ToList());
    }

    public int OutputCount => _byOutput.Count;

    /// <summary>All usable recipes that produce the item, ordered by recipe id (empty when it cannot be crafted).</summary>
    public IReadOnlyList<Recipe> RecipesFor(int outputItemId) =>
        _byOutput.TryGetValue(outputItemId, out var recipes) ? recipes : [];

    public bool Contains(int recipeId) => _ids.Contains(recipeId);

    private static string Signature(Recipe recipe) =>
        $"{recipe.OutputItemId}x{recipe.OutputCount}:" + string.Join(
            ',',
            recipe.Ingredients.Select(i => $"{(int)i.Key.Kind}-{i.Key.Id}-{i.Count}").Order(StringComparer.Ordinal));
}
