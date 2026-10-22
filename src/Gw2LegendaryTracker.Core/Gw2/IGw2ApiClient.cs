using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Items;

namespace Gw2LegendaryTracker.Core.Gw2;

/// <summary>Read-only access to the official Guild Wars 2 API. Implementations throw <see cref="Gw2ApiException"/> on failure.</summary>
public interface IGw2ApiClient
{
    Task<Gw2TokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<AccountSnapshot> GetAccountSnapshotAsync(string apiKey, DateTimeOffset capturedUtc, CancellationToken cancellationToken = default);

    /// <summary>Public item data (no API key needed). At most 200 ids per call; unknown ids are left out of the result.</summary>
    Task<IReadOnlyList<ItemInfo>> GetItemsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Ids of all recipes in the game (public, no key needed).</summary>
    Task<IReadOnlyList<int>> GetRecipeIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Recipes by id (public). At most 200 ids per call. Malformed recipes are left out.</summary>
    Task<IReadOnlyList<Recipe>> GetRecipesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}
