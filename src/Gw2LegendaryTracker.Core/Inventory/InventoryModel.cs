using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Inventory;

/// <summary>Where some of an item is stored.</summary>
public sealed record StockLocation(InventorySource Source, string CharacterName, long Count);

/// <summary>Everything the account owns of one item, summed over all stacks and locations.</summary>
public sealed record ItemStock(int ItemId, long Total, IReadOnlyList<StockLocation> Locations);

/// <summary>
/// Immutable view of the account's possessions at one moment. The crafting engine and both windows
/// read from the same instance, so every screen agrees on what the player owns.
/// </summary>
public sealed class InventoryModel
{
    public static InventoryModel Empty { get; } = new([], new Dictionary<int, long>(), null);

    private readonly Dictionary<int, ItemStock> _stocks;
    private readonly IReadOnlyDictionary<int, long> _wallet;

    private InventoryModel(Dictionary<int, ItemStock> stocks, IReadOnlyDictionary<int, long> wallet, DateTimeOffset? capturedUtc)
    {
        _stocks = stocks;
        _wallet = wallet;
        CapturedUtc = capturedUtc;
    }

    /// <summary>When the underlying snapshot was taken, or <c>null</c> for <see cref="Empty"/>.</summary>
    public DateTimeOffset? CapturedUtc { get; }

    /// <summary>All owned items, ordered by item id so the result is deterministic.</summary>
    public IReadOnlyCollection<ItemStock> Items => _stocks.Values.OrderBy(s => s.ItemId).ToList();

    public static InventoryModel From(AccountSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var stocks = snapshot.Items
            .Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.ItemId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var locations = group
                        .GroupBy(e => (e.Source, e.CharacterName))
                        .Select(g => new StockLocation(g.Key.Source, g.Key.CharacterName, g.Sum(e => (long)e.Count)))
                        .OrderBy(l => l.Source)
                        .ThenBy(l => l.CharacterName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return new ItemStock(group.Key, locations.Sum(l => l.Count), locations);
                });

        // The API lists each currency once; last write wins if a snapshot ever contains duplicates.
        var wallet = snapshot.Wallet.GroupBy(w => w.CurrencyId).ToDictionary(g => g.Key, g => g.Last().Value);

        return new InventoryModel(stocks, wallet, snapshot.CapturedUtc);
    }

    /// <summary>How many of the item the account owns in total (0 when none).</summary>
    public long CountOf(int itemId) => _stocks.TryGetValue(itemId, out var stock) ? stock.Total : 0;

    public ItemStock? Find(int itemId) => _stocks.GetValueOrDefault(itemId);

    /// <summary>The wallet amount of a currency (0 when the account has none). Coin is expressed in copper.</summary>
    public long CurrencyAmount(int currencyId) => _wallet.GetValueOrDefault(currencyId);
}
