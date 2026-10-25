using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Cosmetics;

/// <summary>What a refresh did, so the screen can say it in plain words.</summary>
public enum CatalogRefreshResult
{
    /// <summary>The cache was recent enough.</summary>
    UpToDate,
    Updated,

    /// <summary>The API could not be reached; the cache stays in use.</summary>
    Offline,
}

/// <summary>Skins and dye colours, served from the local cache and refreshed from the API when they are old.</summary>
public sealed class CosmeticsCatalog
{
    /// <summary>The set of skins changes with game updates, not by the day; a week is fresh enough.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly ICosmeticsApiClient _api;
    private readonly ICosmeticsStore _store;
    private readonly TimeProvider _time;

    public CosmeticsCatalog(ICosmeticsApiClient api, ICosmeticsStore store, TimeProvider? time = null)
    {
        _api = api;
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<CosmeticSkin> Skins { get; private set; } = [];

    public IReadOnlyList<DyeColor> Colors { get; private set; } = [];

    public async Task LoadCacheAsync(CancellationToken cancellationToken = default)
    {
        Skins = await _store.LoadSkinsAsync(cancellationToken).ConfigureAwait(false);
        Colors = await _store.LoadColorsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refreshes when there is no cache, it is too old, or <paramref name="force"/> is set. Failures keep the cache.</summary>
    public async Task<CatalogRefreshResult> RefreshAsync(bool force = false, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var fetched = await _store.GetSkinsFetchedUtcAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var skinsAreFresh = fetched is { } at && now - at < MaxAge && Skins.Count > 0;
        if (!force && skinsAreFresh && Colors.Count > 0)
        {
            return CatalogRefreshResult.UpToDate;
        }

        try
        {
            // Colours are small; skins take dozens of requests. Both are stored before they are used.
            var colors = await _api.GetAllDyeColorsAsync(cancellationToken).ConfigureAwait(false);
            await _store.SaveColorsAsync(colors, cancellationToken).ConfigureAwait(false);
            Colors = colors;

            if (force || !skinsAreFresh)
            {
                var skins = await _api.GetAllSkinsAsync(progress, cancellationToken).ConfigureAwait(false);
                await _store.SaveSkinsAsync(skins, now, cancellationToken).ConfigureAwait(false);
                Skins = skins;
            }

            return CatalogRefreshResult.Updated;
        }
        catch (Gw2ApiException)
        {
            return CatalogRefreshResult.Offline;
        }
    }
}
