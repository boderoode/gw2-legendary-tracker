namespace Gw2LegendaryTracker.Core.Gw2;

/// <summary>Read-only access to the official Guild Wars 2 API. Implementations throw <see cref="Gw2ApiException"/> on failure.</summary>
public interface IGw2ApiClient
{
    Task<Gw2TokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<AccountSnapshot> GetAccountSnapshotAsync(string apiKey, DateTimeOffset capturedUtc, CancellationToken cancellationToken = default);
}
