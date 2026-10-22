using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Goals;

/// <summary>Local copy of the ids of all Legendaries in the game, so the goal picker works offline.</summary>
public interface ILegendaryStore
{
    Task<IReadOnlyList<int>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>When the stored list was fetched, or <c>null</c> when nothing is stored.</summary>
    Task<DateTimeOffset?> GetFetchedUtcAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<int> itemIds, DateTimeOffset fetchedUtc, CancellationToken cancellationToken = default);
}

public sealed class LegendaryCatalog
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

    private readonly IGw2ApiClient _api;
    private readonly ILegendaryStore _store;
    private readonly TimeProvider _time;

    public LegendaryCatalog(IGw2ApiClient api, ILegendaryStore store, TimeProvider? time = null)
    {
        _api = api;
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<int>> LoadIdsAsync(CancellationToken cancellationToken = default) => _store.LoadAsync(cancellationToken);

    /// <summary>
    /// Returns the stored list when it is younger than <paramref name="maxAge"/> (24 hours by default), otherwise fetches it again.
    /// Throws <see cref="Gw2ApiException"/> when a needed fetch fails; the stored list stays available.
    /// </summary>
    public async Task<IReadOnlyList<int>> RefreshIfStaleAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        var fetched = await _store.GetFetchedUtcAsync(cancellationToken).ConfigureAwait(false);
        if (fetched is { } at && _time.GetUtcNow() - at < (maxAge ?? DefaultMaxAge))
        {
            return await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches the current list and stores it. An empty answer never replaces a stored list.</summary>
    public async Task<IReadOnlyList<int>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _api.GetLegendaryItemIdsAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count > 0)
        {
            await _store.SaveAsync([.. ids], _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return ids;
        }

        return await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
    }
}
