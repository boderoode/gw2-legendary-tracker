using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Goals;

/// <summary>Local copy of the ids of all Legendaries in the game, so the goal picker works offline.</summary>
public interface ILegendaryStore
{
    Task<IReadOnlyList<int>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default);
}

public sealed class LegendaryCatalog
{
    private readonly IGw2ApiClient _api;
    private readonly ILegendaryStore _store;

    public LegendaryCatalog(IGw2ApiClient api, ILegendaryStore store)
    {
        _api = api;
        _store = store;
    }

    public Task<IReadOnlyList<int>> LoadIdsAsync(CancellationToken cancellationToken = default) => _store.LoadAsync(cancellationToken);

    /// <summary>
    /// Fetches the current list and stores it. An empty answer never replaces a stored list.
    /// Throws <see cref="Gw2ApiException"/> when offline; the stored list stays available.
    /// </summary>
    public async Task<IReadOnlyList<int>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _api.GetLegendaryItemIdsAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count > 0)
        {
            await _store.SaveAsync([.. ids], cancellationToken).ConfigureAwait(false);
            return ids;
        }

        return await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
    }
}
