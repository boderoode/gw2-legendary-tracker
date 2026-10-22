using Gw2LegendaryTracker.Core.Crafting.Forge;

namespace Gw2LegendaryTracker.Core.Crafting;

/// <param name="UnreviewedIds">Recipes from files that are marked as not yet checked against a second source.</param>
public sealed record LocalRecipes(IReadOnlyList<Recipe> Recipes, IReadOnlySet<int> UnreviewedIds, IReadOnlyList<string> Errors);

/// <param name="SingleSourceCount">Recipes in the book that come from one source only and are not confirmed by the official data.</param>
public sealed record RecipeBookResult(RecipeBook Book, int SingleSourceCount, IReadOnlyList<string> LocalErrors);

/// <summary>Merges the official recipes, the downloaded forge recipes and the local files into one book.</summary>
public sealed class RecipeBookProvider
{
    private readonly RecipeCatalog _official;
    private readonly ForgeRecipeCatalog _forge;
    private readonly Func<CancellationToken, Task<LocalRecipes>> _local;

    public RecipeBookProvider(RecipeCatalog official, ForgeRecipeCatalog forge, Func<CancellationToken, Task<LocalRecipes>> local)
    {
        _official = official;
        _forge = forge;
        _local = local;
    }

    public async Task<RecipeBookResult> BuildAsync(CancellationToken cancellationToken = default)
    {
        var local = await _local(cancellationToken).ConfigureAwait(false);
        var forged = await _forge.LoadAsync(cancellationToken).ConfigureAwait(false);

        // Downloaded recipes come first, so where both say the same, the fresh (and verified) one is kept.
        var book = await _official.LoadBookAsync([.. forged.Recipes, .. local.Recipes], cancellationToken).ConfigureAwait(false);

        var singleSource = forged.SingleSourceRecipeIds.Union(local.UnreviewedIds).Count(book.Contains);
        return new RecipeBookResult(book, singleSource, local.Errors);
    }
}
