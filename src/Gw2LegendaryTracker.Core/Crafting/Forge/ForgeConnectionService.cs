using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Sync;

namespace Gw2LegendaryTracker.Core.Crafting.Forge;

/// <summary>Checks a gw2treasures key with one real request and stores it only when the service accepts it.</summary>
public sealed class ForgeConnectionService
{
    /// <summary>Any item works; the answer only has to prove the key is accepted.</summary>
    private const int ProbeItemId = 19672;

    private readonly IForgeApiClient _forge;
    private readonly IApiKeyStore _keys;

    public ForgeConnectionService(IForgeApiClient forge, IApiKeyStore keys)
    {
        _forge = forge;
        _keys = keys;
    }

    public async Task<ConnectionResult> ConnectAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(input?.Trim(), "D", out var guid))
        {
            return new ConnectionResult(false, "Dit lijkt geen geldige gw2treasures-key. Zo'n key bestaat uit letters en cijfers met streepjes, bijvoorbeeld 123e4567-e89b-12d3-a456-426614174000.");
        }

        var key = guid.ToString("D");
        try
        {
            await _forge.GetRecipesAsync(ProbeItemId, key, cancellationToken).ConfigureAwait(false);
            await _keys.SaveAsync(key, cancellationToken).ConfigureAwait(false);
            return new ConnectionResult(true, "Verbonden met gw2treasures.");
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
