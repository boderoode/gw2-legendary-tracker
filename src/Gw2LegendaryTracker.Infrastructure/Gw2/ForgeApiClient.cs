using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2LegendaryTracker.Core.Crafting;
using Gw2LegendaryTracker.Core.Crafting.Forge;
using Gw2LegendaryTracker.Core.Gw2;

namespace Gw2LegendaryTracker.Infrastructure.Gw2;

/// <summary>Reads Mystic Forge recipes from api.gw2treasures.com. The key is sent only in the Authorization header.</summary>
public sealed class ForgeApiClient : IForgeApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ForgeApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ForgeRecipe>> GetRecipesAsync(int itemId, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"items/{itemId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/mystic-forge");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new Gw2ApiException(Gw2ApiErrorKind.Offline, "Geen verbinding met gw2treasures. Controleer je internetverbinding.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Gw2ApiException(Gw2ApiErrorKind.Offline, "gw2treasures reageert niet. Probeer het later opnieuw.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw ToException(response.StatusCode);
            }

            try
            {
                var dtos = await response.Content.ReadFromJsonAsync<List<ForgeRecipeDto>>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];

                // A recipe we cannot interpret completely is dropped rather than planned with wrong numbers.
                return [.. dtos
                    .Where(r => r.OutputItemId > 0 && r.OutputMin > 0 && r.OutputMax >= r.OutputMin
                        && r.Ingredients is { Count: > 0 } && r.Ingredients.All(i => i.ItemId > 0 && i.Count > 0))
                    .Select(r => new ForgeRecipe(
                        r.OutputItemId,
                        r.OutputMin,
                        r.OutputMax,
                        [.. r.Ingredients!.Select(i => new Ingredient(MaterialKey.Item(i.ItemId), i.Count))]))];
            }
            catch (JsonException ex)
            {
                throw new Gw2ApiException(Gw2ApiErrorKind.Unexpected, "gw2treasures gaf een onverwacht antwoord.", ex);
            }
        }
    }

    private static Gw2ApiException ToException(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized =>
            new Gw2ApiException(Gw2ApiErrorKind.InvalidKey, "De gw2treasures-key is ongeldig of ingetrokken. Controleer de key of maak een nieuwe aan."),
        HttpStatusCode.Forbidden =>
            new Gw2ApiException(Gw2ApiErrorKind.MissingPermissions, "gw2treasures geeft deze key geen toegang tot de receptdata. Mogelijk is goedkeuring nodig voor deze applicatie."),
        HttpStatusCode.TooManyRequests =>
            new Gw2ApiException(Gw2ApiErrorKind.RateLimited, "Te veel verzoeken aan gw2treasures. Wacht een minuut en probeer het opnieuw."),
        >= HttpStatusCode.InternalServerError =>
            new Gw2ApiException(Gw2ApiErrorKind.ServerError, "gw2treasures heeft een storing. Probeer het later opnieuw."),
        _ => new Gw2ApiException(Gw2ApiErrorKind.Unexpected, $"Onverwachte fout van gw2treasures (HTTP {(int)status})."),
    };

    private sealed record ForgeRecipeDto(
        [property: JsonPropertyName("output_item_id")] int OutputItemId,
        [property: JsonPropertyName("output_item_count_min")] int OutputMin,
        [property: JsonPropertyName("output_item_count_max")] int OutputMax,
        List<ForgeIngredientDto>? Ingredients);

    private sealed record ForgeIngredientDto([property: JsonPropertyName("item_id")] int ItemId, long Count);
}
