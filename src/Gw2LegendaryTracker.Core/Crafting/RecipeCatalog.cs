using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Crafting;

/// <summary>
/// Keeps the recipe cache current and builds the <see cref="RecipeBook"/> the planner uses.
/// All recipes are downloaded once (about 70 requests) instead of searched per item, which would hit the API rate limit.
/// </summary>
public sealed class RecipeCatalog
{
    /// <summary>The API accepts at most 200 ids per request.</summary>
    internal const int MaxIdsPerRequest = 200;

    private readonly IGw2ApiClient _api;
    private readonly IRecipeStore _store;

    public RecipeCatalog(IGw2ApiClient api, IRecipeStore store)
    {
        _api = api;
        _store = store;
    }

    /// <summary>
    /// Downloads recipes that are not cached yet. Progress is saved per request, so an interrupted
    /// download resumes where it stopped. Returns the number of recipes added; throws <see cref="Gw2ApiException"/> when offline.
    /// </summary>
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _api.GetRecipeIdsAsync(cancellationToken).ConfigureAwait(false);
        var known = await _store.GetKnownIdsAsync(cancellationToken).ConfigureAwait(false);

        var added = 0;
        foreach (var chunk in ids.Where(id => !known.Contains(id)).Chunk(MaxIdsPerRequest))
        {
            var recipes = await _api.GetRecipesAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (recipes.Count > 0)
            {
                await _store.SaveAsync([.. recipes], cancellationToken).ConfigureAwait(false);
                added += recipes.Count;
            }
        }

        return added;
    }

    /// <summary>Builds a book from the cache plus any locally imported recipes.</summary>
    public async Task<RecipeBook> LoadBookAsync(IEnumerable<Recipe>? localRecipes = null, CancellationToken cancellationToken = default)
    {
        var cached = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return new RecipeBook(localRecipes is null ? cached : cached.Concat(localRecipes));
    }
}
