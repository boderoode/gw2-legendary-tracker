using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Items;
using Gw2LegendaryTracker.Core.Trading;

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

    /// <summary>Item ids of every Legendary in the game (public, no key needed).</summary>
    Task<IReadOnlyList<int>> GetLegendaryItemIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Item ids of all raw crafting materials (public). These are bought or gathered, never forged.</summary>
    Task<IReadOnlyList<int>> GetMaterialItemIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Names of wallet currencies by id (public). Unknown ids are left out of the result.</summary>
    Task<IReadOnlyDictionary<int, string>> GetCurrencyNamesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trading Post prices (public, no key needed). At most 200 ids per call; items that are not on the
    /// Trading Post are left out of the result.
    /// </summary>
    Task<IReadOnlyList<TradingPostPrice>> GetTradingPostPricesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}
