using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Core.Items;

namespace Gw2LegendaryTracker.Infrastructure.Gw2;

/// <summary>Read-only client for the official API. The key is sent only in the Authorization header, never in URLs or messages.</summary>
public sealed class Gw2ApiClient : IGw2ApiClient
{
    private const int MaxItemsPerRequest = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public Gw2ApiClient(HttpClient http) => _http = http;

    public async Task<Gw2TokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var dto = await GetAsync<TokenInfoDto>("v2/tokeninfo", apiKey, cancellationToken).ConfigureAwait(false);
        return new Gw2TokenInfo(dto.Id ?? string.Empty, dto.Name ?? string.Empty, dto.Permissions ?? []);
    }

    public async Task<AccountSnapshot> GetAccountSnapshotAsync(string apiKey, DateTimeOffset capturedUtc, CancellationToken cancellationToken = default)
    {
        var account = await GetAsync<AccountDto>("v2/account", apiKey, cancellationToken).ConfigureAwait(false);
        var bank = await GetAsync<List<StackDto?>>("v2/account/bank", apiKey, cancellationToken).ConfigureAwait(false);
        var materials = await GetAsync<List<StackDto?>>("v2/account/materials", apiKey, cancellationToken).ConfigureAwait(false);
        var shared = await GetAsync<List<StackDto?>>("v2/account/inventory", apiKey, cancellationToken).ConfigureAwait(false);
        var wallet = await GetAsync<List<WalletDto>>("v2/account/wallet", apiKey, cancellationToken).ConfigureAwait(false);
        var characters = await GetAsync<List<CharacterDto>>("v2/characters?ids=all", apiKey, cancellationToken).ConfigureAwait(false);

        var items = new List<InventoryEntry>();
        items.AddRange(ToEntries(bank, InventorySource.Bank));
        items.AddRange(ToEntries(materials, InventorySource.Materials));
        items.AddRange(ToEntries(shared, InventorySource.SharedInventory));
        foreach (var character in characters)
        {
            var slots = (character.Bags ?? []).Where(bag => bag is not null).SelectMany(bag => bag!.Inventory ?? []);
            items.AddRange(ToEntries(slots, InventorySource.Character, character.Name ?? string.Empty));
        }

        return new AccountSnapshot(
            account.Name ?? string.Empty,
            capturedUtc,
            items,
            wallet.Select(w => new WalletEntry(w.Id, w.Value)).ToList());
    }

    public async Task<IReadOnlyList<ItemInfo>> GetItemsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > MaxItemsPerRequest)
        {
            throw new ArgumentException($"At most {MaxItemsPerRequest} ids per request.", nameof(ids));
        }

        var query = string.Join(',', ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

        // The API answers 404 when none of the ids exist and 206 when only some do; both are normal for us.
        var items = await GetAsync<List<ItemDto>>($"v2/items?ids={query}&lang=en", apiKey: null, cancellationToken, notFoundIsEmpty: true).ConfigureAwait(false);
        return (items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => new ItemInfo(i.Id, i.Name!, i.Icon, i.Rarity ?? "Basic"))
            .ToList();
    }

    private static IEnumerable<InventoryEntry> ToEntries(IEnumerable<StackDto?> stacks, InventorySource source, string character = "") =>
        stacks.Where(s => s is { Count: > 0 }).Select(s => new InventoryEntry(s!.Id, s.Count, source, character));

    private async Task<T> GetAsync<T>(string path, string? apiKey, CancellationToken cancellationToken, bool notFoundIsEmpty = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding met de Guild Wars 2-servers. Controleer je internetverbinding.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Gw2ApiException(Gw2ApiErrorKind.Offline, "De Guild Wars 2-servers reageren niet. Probeer het later opnieuw.", ex);
        }

        using (response)
        {
            if (notFoundIsEmpty && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default!;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new Gw2ApiException(Gw2ApiErrorKind.Unexpected, "De Guild Wars 2-servers gaven een leeg antwoord.");
            }
            catch (JsonException ex)
            {
                throw new Gw2ApiException(Gw2ApiErrorKind.Unexpected, "De Guild Wars 2-servers gaven een onverwacht antwoord.", ex);
            }
        }
    }

    private static async Task<Gw2ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await ReadErrorTextAsync(response, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            // The API reports "requires scope X" when a valid key lacks a permission.
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest
                when text.Contains("scope", StringComparison.OrdinalIgnoreCase) =>
                new Gw2ApiException(Gw2ApiErrorKind.MissingPermissions, "De API-key mist een benodigd recht. Maak een nieuwe key met alle gevraagde rechten."),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest =>
                new Gw2ApiException(Gw2ApiErrorKind.InvalidKey, "De API-key is ongeldig of ingetrokken. Maak een nieuwe key aan."),
            HttpStatusCode.TooManyRequests =>
                new Gw2ApiException(Gw2ApiErrorKind.RateLimited, "Te veel verzoeken aan de Guild Wars 2-servers. Wacht een minuut en probeer het opnieuw."),
            >= HttpStatusCode.InternalServerError =>
                new Gw2ApiException(Gw2ApiErrorKind.ServerError, "De Guild Wars 2-servers hebben een storing. Probeer het later opnieuw."),
            _ => new Gw2ApiException(Gw2ApiErrorKind.Unexpected, $"Onverwachte fout van de Guild Wars 2-servers (HTTP {(int)response.StatusCode})."),
        };
    }

    private static async Task<string> ReadErrorTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return error?.Text ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private sealed record TokenInfoDto(string? Id, string? Name, List<string>? Permissions);

    private sealed record ItemDto(int Id, string? Name, string? Icon, string? Rarity);

    private sealed record AccountDto(string? Name);

    private sealed record StackDto(int Id, int Count);

    private sealed record WalletDto(int Id, long Value);

    private sealed record BagDto(List<StackDto?>? Inventory);

    private sealed record CharacterDto(string? Name, List<BagDto?>? Bags);

    private sealed record ErrorDto(string? Text);
}
