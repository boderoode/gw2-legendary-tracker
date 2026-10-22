namespace Gw2LegendaryTracker.Core.Sync;

/// <summary>Protected storage for the API key. The key must never be written anywhere in plain text.</summary>
public interface IApiKeyStore
{
    /// <summary>Returns the stored key, or <c>null</c> when none is stored or it cannot be decrypted.</summary>
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string apiKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
