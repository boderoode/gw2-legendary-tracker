using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Items;

/// <summary>Currency names for the screens. Few and rarely changing, so they are kept in memory for the session.</summary>
public sealed class CurrencyCatalog
{
    private readonly IGw2ApiClient _api;
    private readonly Dictionary<int, string> _names = [];

    public CurrencyCatalog(IGw2ApiClient api) => _api = api;

    /// <summary>Names for the ids; ids that cannot be resolved (offline, unknown) are simply absent.</summary>
    public async Task<IReadOnlyDictionary<int, string>> ResolveAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = ids.Distinct().ToList();
        var missing = wanted.Where(id => !_names.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            try
            {
                foreach (var (id, name) in await _api.GetCurrencyNamesAsync(missing, cancellationToken).ConfigureAwait(false))
                {
                    _names[id] = name;
                }
            }
            catch (Gw2ApiException)
            {
                // Offline: show "currency #id" for now; the next recalculation asks again.
            }
        }

        return wanted.Where(_names.ContainsKey).ToDictionary(id => id, id => _names[id]);
    }
}
