using Gw2LegendaryTracker.Core.Items;

namespace Gw2LegendaryTracker.Core.Gw2;

/// <summary>Read-only access to the official Guild Wars 2 API. Implementations throw <see cref="Gw2ApiException"/> on failure.</summary>
public interface IGw2ApiClient
{
    Task<Gw2TokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<AccountSnapshot> GetAccountSnapshotAsync(string apiKey, DateTimeOffset capturedUtc, CancellationToken cancellationToken = default);

    /// <summary>Public item data (no API key needed). At most 200 ids per call; unknown ids are left out of the result.</summary>
    Task<IReadOnlyList<ItemInfo>> GetItemsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}
