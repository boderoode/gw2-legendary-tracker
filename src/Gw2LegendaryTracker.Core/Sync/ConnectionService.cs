using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Core.Sync;

public sealed record ConnectionResult(bool IsConnected, string Message, string? AccountName = null);

/// <summary>Validates an API key and, only when it is usable, stores it.</summary>
public sealed class ConnectionService
{
    private readonly IGw2ApiClient _api;
    private readonly IApiKeyStore _keys;

    public ConnectionService(IGw2ApiClient api, IApiKeyStore keys)
    {
        _api = api;
        _keys = keys;
    }

    public async Task<ConnectionResult> ConnectAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (!ApiKeyFormat.TryNormalize(input, out var key))
        {
            return new ConnectionResult(false, "Dit lijkt geen geldige API-key. Een key bestaat uit letters en cijfers met streepjes, en is 72 tekens lang. Kopieer hem opnieuw in zijn geheel.");
        }

        try
        {
            var token = await _api.GetTokenInfoAsync(key, cancellationToken).ConfigureAwait(false);
            var missing = ApiPermissions.Missing(token.Permissions);
            if (missing.Count > 0)
            {
                return new ConnectionResult(false, $"Deze key mist rechten: {string.Join(", ", missing)}. Maak een nieuwe key aan en vink deze rechten aan.");
            }

            await _keys.SaveAsync(key, cancellationToken).ConfigureAwait(false);
            return new ConnectionResult(true, "Verbonden met je Guild Wars 2-account.", token.Name);
        }
        catch (Gw2ApiException ex)
        {
            return new ConnectionResult(false, ex.Message);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => _keys.DeleteAsync(cancellationToken);

    public async Task<bool> HasKeyAsync(CancellationToken cancellationToken = default) =>
        await _keys.LoadAsync(cancellationToken).ConfigureAwait(false) is not null;
}
