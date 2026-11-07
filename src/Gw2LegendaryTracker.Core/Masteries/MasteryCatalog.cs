using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Masteries;

/// <summary>The mastery tracks of the game, kept locally and refreshed once a week.</summary>
public sealed class MasteryCatalog
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    private readonly IGw2ApiClient _api;
    private readonly IMasteryDefinitionStore _store;
    private readonly TimeProvider _time;
    private IReadOnlyDictionary<int, MasteryDefinition>? _definitions;

    public MasteryCatalog(IGw2ApiClient api, IMasteryDefinitionStore store, TimeProvider? time = null)
    {
        _api = api;
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after the tracks were loaded or refreshed; may be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The known tracks by id; <c>null</c> while none are known.</summary>
    public IReadOnlyDictionary<int, MasteryDefinition>? Definitions => Volatile.Read(ref _definitions);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Publish(stored);
    }

    /// <summary>
    /// Refreshes the tracks when the stored copy is older than <paramref name="maxAge"/> (a week by default) or missing.
    /// Throws <see cref="Gw2ApiException"/> when a needed fetch fails; the stored tracks stay available.
    /// </summary>
    public async Task RefreshIfStaleAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        var fetched = await _store.GetFetchedUtcAsync(cancellationToken).ConfigureAwait(false);
        if (fetched is { } at && _time.GetUtcNow() - at < (maxAge ?? DefaultMaxAge))
        {
            if (Definitions is null)
            {
                await LoadAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches and stores the tracks. An empty answer never replaces stored tracks.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var fresh = await _api.GetMasteriesAsync(cancellationToken).ConfigureAwait(false);
        if (fresh.Count == 0)
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _store.SaveAsync([.. fresh], _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        Publish(fresh);
    }

    private void Publish(IReadOnlyList<MasteryDefinition> definitions)
    {
        Volatile.Write(ref _definitions, definitions.Count == 0 ? null : definitions.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First()));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
