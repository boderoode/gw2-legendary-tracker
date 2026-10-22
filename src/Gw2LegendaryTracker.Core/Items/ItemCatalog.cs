using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Items;

/// <summary>Resolves item ids to names and icons: cache first, the public API only for what is missing.</summary>
public sealed class ItemCatalog
{
    /// <summary>The API accepts at most 200 ids per request.</summary>
    internal const int MaxIdsPerRequest = 200;

    private readonly IGw2ApiClient _api;
    private readonly IItemInfoStore _store;

    public ItemCatalog(IGw2ApiClient api, IItemInfoStore store)
    {
        _api = api;
        _store = store;
    }

    /// <summary>
    /// Returns what is known for <paramref name="ids"/>. Items that could not be fetched (offline, unknown id)
    /// are simply absent from the result; the cached part is always returned.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, ItemInfo>> ResolveAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = ids.Distinct().ToList();
        var known = new Dictionary<int, ItemInfo>(await _store.GetAsync(wanted, cancellationToken).ConfigureAwait(false));

        var missing = wanted.Where(id => !known.ContainsKey(id)).ToList();
        foreach (var chunk in missing.Chunk(MaxIdsPerRequest))
        {
            IReadOnlyList<ItemInfo> fetched;
            try
            {
                fetched = await _api.GetItemsAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            catch (Gw2ApiException)
            {
                // Offline or API trouble: show what the cache has; the next sync tries again.
                break;
            }

            if (fetched.Count == 0)
            {
                continue;
            }

            await _store.SaveAsync([.. fetched], cancellationToken).ConfigureAwait(false);
            foreach (var item in fetched)
            {
                known[item.Id] = item;
            }
        }

        return known;
    }
}
