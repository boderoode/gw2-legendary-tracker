using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Trading;

/// <summary>Local copy of Trading Post prices, including "this item is not on the Trading Post".</summary>
public interface IPriceStore
{
    Task<IReadOnlyDictionary<int, PriceLookup>> GetAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<PriceLookup> lookups, CancellationToken cancellationToken = default);
}

/// <summary>Keeps prices current without asking more often than needed; falls back to the last known prices offline.</summary>
public sealed class PriceCatalog
{
    /// <summary>The API itself only updates its prices every few minutes, so asking more often gains nothing.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>The API accepts at most 200 ids per request.</summary>
    internal const int MaxIdsPerRequest = 200;

    private readonly IGw2ApiClient _api;
    private readonly IPriceStore _store;
    private readonly TimeProvider _time;

    public PriceCatalog(IGw2ApiClient api, IPriceStore store, TimeProvider? time = null)
    {
        _api = api;
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns prices for the items, fetching those that are missing or older than <paramref name="maxAge"/>.
    /// Never throws for API trouble: the stored prices are returned and the book carries a warning.
    /// </summary>
    public async Task<PriceBook> EnsureAsync(IEnumerable<int> itemIds, TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var wanted = itemIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return PriceBook.Empty;
        }

        var known = new Dictionary<int, PriceLookup>(await _store.GetAsync(wanted, cancellationToken).ConfigureAwait(false));
        var now = _time.GetUtcNow();
        var limit = maxAge ?? DefaultMaxAge;
        var stale = wanted.Where(id => !known.TryGetValue(id, out var lookup) || now - lookup.FetchedUtc >= limit).ToList();

        string? warning = null;
        foreach (var chunk in stale.Chunk(MaxIdsPerRequest))
        {
            IReadOnlyList<TradingPostPrice> listed;
            try
            {
                listed = await _api.GetTradingPostPricesAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            catch (Gw2ApiException ex)
            {
                warning = ex.Message;
                break;
            }

            // Items missing from the answer are not on the Trading Post; remember that too, so it is not asked again.
            var byId = listed.ToDictionary(p => p.ItemId);
            var lookups = chunk.Select(id => new PriceLookup(id, byId.GetValueOrDefault(id), now)).ToList();
            await _store.SaveAsync(lookups, cancellationToken).ConfigureAwait(false);
            foreach (var lookup in lookups)
            {
                known[lookup.ItemId] = lookup;
            }
        }

        var prices = known.Values.Where(l => l.Price is not null).ToDictionary(l => l.ItemId, l => l.Price!);
        DateTimeOffset? oldest = known.Count == 0 ? null : known.Values.Min(l => l.FetchedUtc);
        return new PriceBook(prices, oldest, warning);
    }
}
