namespace Gw2LegendaryTracker.Core.Crafting;

/// <summary>Local cache of API recipes. Public game data, kept apart from user data.</summary>
public interface IRecipeStore
{
    Task<IReadOnlySet<int>> GetKnownIdsAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<Recipe> recipes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recipe>> LoadAllAsync(CancellationToken cancellationToken = default);
}
